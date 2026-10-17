#!/usr/bin/env python3
"""Run LuaJIT's test suite (LuaJIT/LuaJIT-test-cleanup) against the vendored
build/luajit-2.1 tree.

LuaJIT's own repository has no tests. Mike Pall's tests live in
https://github.com/LuaJIT/LuaJIT-test-cleanup, pinned here to one commit. The
script downloads that commit's archive (or takes it from --archives), checks
its size and SHA-256, copies build/luajit-2.1 for each mode and builds the
stand-alone `luajit` with LuaJIT's Makefile and the flags of our build
scripts, then runs two parts of the suite in a fresh copy:

  runner      `luajit test.lua +slow` in test/: every test the `index` files
              list, one result per `do --- name` block. Tests whose tags do
              not match the build (+jit without JIT, +ffi without FFI, ...)
              are reported as skipped with the tag that excluded them.
  standalone  the files the suite has not converted to the runner yet
              (misc/, lib/ffi/ffi_*.lua, lib/table/misc.lua, sysdep/,
              unportable/), each as `luajit <file>` in its own directory;
              exit 0 passes. They need the C helpers in test/src, which this
              script compiles (the suite has no build for them).

Modes (--modes; a build of build/luajit-2.1 each):

  default   LuaJIT's defaults (GC64 on 64-bit, JIT, FFI) with the flags of
            make_linux64_luajit.sh (CFLAGS=-fPIC) or make_osx_luajit.sh
            (BUILDMODE=static XCFLAGS=-fPIC, macOS 12.0)
  assert    default plus LUA_USE_ASSERT and LUA_USE_APICHECK (internal checks)
  nojit     LUAJIT_DISABLE_JIT and LUAJIT_NO_UNWIND: the interpreter-only VM
            that lj_arch.h selects for iOS (LJ_OS_NOJIT, LJ_NO_UNWIND)
  nx        LUAJIT_USE_SYSMALLOC, LUAJIT_DISABLE_JIT, LUAJIT_DISABLE_FFI: the
            flags of make_nx64_luajit_gc64.bat, without the console target
  nogc64    LUAJIT_DISABLE_GC64 (x86_64 only): make_win64_luajit.bat's VM

Without --modes, every mode this host can build runs (`unavailable`).

Every failure must be listed in KNOWN_FAILURES with its cause; an unlisted
failure or a listed test that passes fails the run. SKIP lists the tests that
are not run, each with a reason, and ADJUST the few edits to the suite copy
(expectations that upstream LuaJIT changed after the suite's last commit).
Each mode writes results.tsv (part, test, status, detail for every test) and
summary.json. Linux and macOS only.

    python3 Test/Native/luajit_tests.py --output /abs/empty-dir [--modes default]

--cross and --exec-wrapper build for another architecture and run the result
under an emulator, e.g. `--cross x86_64-linux-gnu- --exec-wrapper
"qemu-x86_64 -L /usr/x86_64-linux-gnu"` on Linux arm64.

The archive is test data: only the interpreters built here run it, and this
script compiles its C helpers itself.
"""
import argparse
import hashlib
import json
import os
import platform
import re
import shlex
import shutil
import signal
import subprocess
import sys
import tarfile
import time
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
LUAJIT = REPO / "build" / "luajit-2.1"

# Pinned suite, read by Tools/dependencies.py for DEPENDENCIES.md and the
# SBOM. The newest commit of LuaJIT-test-cleanup (2016-10-19); the archive is
# GitHub's tarball of that commit, hashed after download on 2026-10-18 (the
# github.com/.../archive/ and codeload URLs gave the same bytes).
SUITE_REPO = "LuaJIT/LuaJIT-test-cleanup"
SUITE_COMMIT = "014708bceb70550a3ab8d539cff14d9085ca9cb8"
SUITE_ARCHIVE = "LuaJIT-test-cleanup-014708bc.tar.gz"
SUITE_SIZE = 112237
SUITE_SHA256 = "6003d032d74e48516526a5e81f1e40ea13072395af85c79b63aeffd415756fba"
SUITE_URL = f"https://codeload.github.com/{SUITE_REPO}/tar.gz/{SUITE_COMMIT}"

MODES = {
    "default": [],
    "assert": ["-DLUA_USE_ASSERT", "-DLUA_USE_APICHECK"],
    "nojit": ["-DLUAJIT_DISABLE_JIT", "-DLUAJIT_NO_UNWIND"],
    "nx": ["-DLUAJIT_USE_SYSMALLOC", "-DLUAJIT_DISABLE_JIT", "-DLUAJIT_DISABLE_FFI"],
    "nogc64": ["-DLUAJIT_DISABLE_GC64"],
}
NOJIT_MODES = ("nojit", "nx")
NOFFI_MODES = ("nx",)
NOUNWIND_MODES = ("nojit",)

# Tests that are not run. Key: "runner:<file> --- <test name>" or
# "standalone:<file>". Value: (condition, reason). Conditions: None (always),
# or a set of words that must all hold: "darwin", "linux", "arm64",
# "x86_64", a mode name, "no-jit" (no JIT compiler), "no-ffi" (no FFI),
# "no-unwind" (LUAJIT_NO_UNWIND).
SKIP = {
    # sysdep/ needs system headers, libraries or a C++ runtime set-up the
    # suite does not describe; the README calls it unresolved.
    "standalone:sysdep/ffi_include_gtk.lua": (None, "needs GTK development headers (sysdep)"),
    "standalone:sysdep/ffi_include_std.lua": (None, "runs `cc -E` on system headers and writes /tmp/__tmp.c (sysdep)"),
    "standalone:sysdep/ffi_lib_z.lua": (None, "needs zlib loaded by name through ffi.load (sysdep)"),
    # Not applicable: without unwinding LuaJIT has no C++ exception
    # interoperability (doc/extensions.html, "C++ Exception
    # Interoperability": other platforms "No"; lj_arch.h sets LJ_NO_UNWIND
    # for iOS). A C++ throw through Lua frames ends in std::terminate.
    "standalone:sysdep/catch_cpp.lua": (("no-unwind",), "LUAJIT_NO_UNWIND has no C++ exception interoperability"),
}

# Edits to the extracted suite copy: (condition as in SKIP, file, old text,
# new text, reason). Each old text must occur exactly once where it applies,
# so a changed suite stops the run. Nothing else in the suite changes.
ADJUST = [
    # The suite's last commit (2016-10-19) predates two library changes in
    # LuaJIT 2.1, and lib/contents.lua checks the library contents of that
    # time. The edits follow LuaJIT, as tarantool/luajit's maintained copy of
    # these tests does (test/LuaJIT-tests/lib/contents.lua).
    (None, "lib/contents.lua", "do --- pre-5.2 math +lua<5.2 -compat5.2\n  assert(math.mod)\n",
     "do --- pre-5.2 math +lua<5.2 -compat5.2\n  assert(not math.mod)\n",
     "LuaJIT de5568e0 (2017-03-17) removed the Lua 5.0 alias math.mod"),
    (None, "lib/contents.lua", "do --- pre-5.2 string +lua<5.2 -compat5.2\n  assert(string.gfind)\n",
     "do --- pre-5.2 string +lua<5.2 -compat5.2\n  assert(not string.gfind)\n",
     "LuaJIT de5568e0 (2017-03-17) removed the Lua 5.0 alias string.gfind"),
    (None, "lib/contents.lua", '"concat:foreach:foreachi:getn:insert:maxn:remove:sort"',
     '"concat:foreach:foreachi:getn:insert:maxn:move:remove:sort"',
     "LuaJIT c1981676 (2017-01-17) added table.move from Lua 5.3"),
    # LuaJIT e9af1abe (2020-09-29, "Add support for full-range 64 bit
    # lightuserdata") accepts any 64-bit pointer on x64, so these two calls
    # now succeed (tarantool/luajit test/LuaJIT-tests/lang/lightud.lua
    # expects true too). The checks only run on x64.
    (None, "misc/lightud.lua", "  assert(pcall(lightud, 2^47) == false)\n  assert(pcall(lightud, 2^64-2048) == false)\n",
     "  assert(pcall(lightud, 2^47) == true)\n  assert(pcall(lightud, 2^64-2048) == true)\n",
     "LuaJIT e9af1abe allows full-range 64-bit lightuserdata"),
    # LuaJIT defe61a5 (2024-02-04, "Rework stack overflow handling") replaces
    # the last frame on a Lua stack overflow, and there may be no room left
    # to call the xpcall handler, so the message for the plain recursion `f`
    # can come back without a traceback (always in the interpreter, and on
    # x64 with the JIT). tarantool/luajit's copy (lang/stackov.lua) checks
    # only the error reason for this case, as here; the traceback length is
    # still checked by the vararg case below it (`vcall`).
    (None, "misc/stackov.lua",
     'local first = string.match(s, "[^\\n]+")\nlocal line = debug.getinfo(f, "S").linedefined+1\n'
     'assert(string.match(first, ":"..line..": stack overflow$"))\n\n'
     'local n = 1\nfor _ in string.gmatch(s, "\\n") do n = n + 1 end\nassert(n == 1+1+11+1+10)\n',
     'assert(string.match(s, "stack overflow"))\n',
     "LuaJIT defe61a5 may leave no stack for the handler on overflow"),
    # LuaJIT 5c647754 (2025-11-10, "Run VM events and finalizers in separate
    # state") runs __gc on a separate lua_State, so debug.getinfo(2) in a
    # finalizer is nil and the finalizer itself errors. tarantool/luajit's
    # copy (lang/gc_debug.lua) drops the debug-info checks, as here; the test
    # still checks that the finalizers run. The file returns at once unless
    # the JIT compiler is off (the nojit and nx modes).
    (None, "misc/debug_gc.lua",
     "    local dbg = debug.getinfo(2)\n    caught_line = dbg.currentline\n    caught_mm = debug.getinfo(1).name\n", "",
     "LuaJIT 5c647754: no caller debug info inside finalizers"),
    (None, "misc/debug_gc.lua",
     '  if type(caught_line) ~= "number" or caught_line < 0 then\n    error("bad linenumber in debug info", 2)\n  end\n'
     '  if caught_mm ~= mm then\n    error("bad name for metamethod in debug info", 2)\n  end\n', "",
     "LuaJIT 5c647754: no caller debug info inside finalizers"),
    # Environment: since glibc 2.34 libpthread is part of libc and distros
    # ship only the runtime name libpthread.so.0, not the unversioned
    # libpthread.so that ffi.load("pthread") opens. The name with a dot is
    # used as given (lj_clib.c adds only the "lib" prefix).
    (("linux",), "sysdep/ffi_lib_c.lua", 'ffi.load("pthread")', 'ffi.load("pthread.so.0")',
     "glibc >= 2.34 has no unversioned libpthread.so"),
]

# Failures with a known cause. Key as in SKIP. Value: (condition, cause).
KNOWN_FAILURES = {
    # Upstream defect, not ours (build/luajit-2.1 is unmodified upstream):
    # Apple's ARM64 ABI packs stack arguments by their size. lj_ccall.c does
    # that for calls (CCALL_PACK_STACKARG, LuaJIT#205, fixed 2023-08-29), but
    # lj_ccallback.c still reads every stack argument from an 8-byte slot, so
    # the 10th int of a callback reads as 0 and the assertion at line 40
    # fails. The rest of the file passes with that assertion removed.
    "standalone:lib/ffi/ffi_callback.lua": (
        ("darwin", "arm64"),
        "upstream: FFI callbacks read stack-passed arguments narrower than 8 bytes from 8-byte slots "
        "on Apple ARM64 (lj_ccallback.c ignores CCALL_PACK_STACKARG; calls were fixed for LuaJIT#205)"),
    # Not portable, as its directory says: the expected checksums assume
    # LuaJIT's single-number mode (x86, x64). ARM64 is a dual-number target
    # (lj_arch.h LJ_ARCH_NUMMODE LJ_NUMMODE_DUAL): integer arithmetic gives
    # +0 where doubles give -0 (-1 * 0), so the printed results differ. It
    # passes on x86_64.
    "standalone:unportable/ffi_arith_int64.lua": (
        ("arm64",),
        "unportable: expected checksums assume single-number mode; ARM64 LuaJIT uses dual numbers (-1*0 is +0)"),
}


def log(msg):
    print(msg, flush=True)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 16), b""):
            h.update(block)
    return h.hexdigest()


def fetch(archives, cache):
    dest = cache / SUITE_ARCHIVE
    if archives:
        shutil.copyfile(Path(archives) / SUITE_ARCHIVE, dest)
    else:
        log(f"downloading {SUITE_URL}")
        with urllib.request.urlopen(SUITE_URL, timeout=120) as r, open(dest, "wb") as f:
            shutil.copyfileobj(r, f)
    got_size, got = dest.stat().st_size, sha256(dest)
    if got_size != SUITE_SIZE or got != SUITE_SHA256:
        dest.unlink()
        sys.exit(f"luajit_tests.py: {SUITE_ARCHIVE}: size {got_size}, SHA-256 {got}; "
                 f"expected {SUITE_SIZE}, {SUITE_SHA256}")
    return dest


def run(cmd, timeout, cwd=None, env=None, logfile=None):
    """Run cmd in its own process group with an empty stdin pipe; on timeout
    kill the whole group and return None as the exit code."""
    out = open(logfile, "w") if logfile else None
    try:
        p = subprocess.Popen(cmd, cwd=cwd, env=env, stdin=subprocess.PIPE, stdout=out or subprocess.PIPE,
                             stderr=subprocess.STDOUT, start_new_session=True, text=True, errors="replace")
        try:
            text, _ = p.communicate(input="", timeout=timeout)
            rc = p.returncode
        except subprocess.TimeoutExpired:
            os.killpg(p.pid, signal.SIGKILL)
            p.wait()
            text, rc = "", None
        if logfile:
            text = Path(logfile).read_text(encoding="utf-8", errors="replace")
        return rc, text
    finally:
        if out:
            out.close()


def target_machine(args):
    """The architecture LuaJIT is built for: the host's, or the --cross prefix's."""
    machine = args.cross.split("-")[0] if args.cross else platform.machine()
    return {"aarch64": "arm64", "amd64": "x86_64"}.get(machine.lower(), machine.lower())


def conditions(mode, args):
    conds = {sys.platform, target_machine(args), mode}
    if mode in NOJIT_MODES:
        conds.add("no-jit")
    if mode in NOFFI_MODES:
        conds.add("no-ffi")
    if mode in NOUNWIND_MODES:
        conds.add("no-unwind")
    return conds


def applies(entry, conds):
    cond = entry[0]
    return cond is None or set(cond) <= conds


def build_luajit(mode, into, args):
    tree = into / "luajit"
    shutil.copytree(LUAJIT, tree, symlinks=True)
    xcflags = list(MODES[mode])
    cmd = ["make", "-C", str(tree), f"-j{args.jobs}"]
    env = dict(os.environ)
    if args.cross:
        cmd += [f"CROSS={args.cross}", f"HOST_CC={args.host_cc}"]
    if sys.platform == "darwin":
        # make_osx_luajit.sh, one architecture.
        env["MACOSX_DEPLOYMENT_TARGET"] = "12.0"
        cmd += ["BUILDMODE=static", f"TARGET_FLAGS=-arch {platform.machine()}"]
        xcflags.insert(0, "-fPIC")
    else:
        # make_linux64_luajit.sh.
        cmd.append("CFLAGS=-fPIC")
    if mode == "assert":
        cmd.append("CCDEBUG=-g")
    if args.cc:
        cmd.append(f"CC={args.cc}")
    if xcflags:
        cmd.append("XCFLAGS=" + " ".join(xcflags))
    rc, text = run(cmd, args.timeout, env=env, logfile=into / "build.log")
    if rc != 0:
        sys.exit(f"luajit_tests.py: {' '.join(cmd)} failed ({'timeout' if rc is None else rc}):\n{text[-4000:]}")
    exe = tree / "src" / "luajit"
    if not exe.is_file():
        sys.exit(f"luajit_tests.py: {exe} was not built")
    return tree, exe


def extract(archive, into, conds):
    into.mkdir(parents=True)
    with tarfile.open(archive) as t:
        t.extractall(into, filter="data")
    (top,) = into.iterdir()
    test = top / "test"
    for cond, name, old, new, _ in ADJUST:
        if not applies((cond,), conds):
            continue
        f = test / name
        text = f.read_text(encoding="latin-1")
        if text.count(old) != 1:
            sys.exit(f"luajit_tests.py: {f}: expected {old!r} exactly once")
        f.write_text(text.replace(old, new), encoding="latin-1")
    return test


def build_helpers(tree, test, mode, args):
    """Compile test/src/ctest.c and cpptest.cpp (what the suite's old makefile
    did) and place them where the standalone files look for them:
    require("ctest") / require("cpptest") through LUA_CPATH, and
    ffi.load("../clib/ctest") from lib/ffi/. Also give lib/ffi/ the
    ../common/ffi_util.inc it includes (the file moved to test/common)."""
    clib = test.parent / "clib"
    clib.mkdir()
    inc = ["-I", str(tree / "src")]
    shared = ["-bundle", "-undefined", "dynamic_lookup"] if sys.platform == "darwin" else ["-shared"]
    cc = args.cc or (f"{args.cross}gcc" if args.cross else "cc")
    cxx = args.cxx or (f"{args.cross}g++" if args.cross else "c++")
    jobs = [([cc, "-std=gnu99"], "ctest.c", "ctest.so"), ([cxx], "cpptest.cpp", "cpptest.so")]
    for compiler, src, out in jobs:
        cmd = [*compiler, "-O2", "-fPIC", *inc, *shared, "-o", str(clib / out), str(test / "src" / src)]
        rc, text = run(cmd, args.timeout)
        if rc != 0:
            sys.exit(f"luajit_tests.py: {' '.join(cmd)} failed:\n{text[-4000:]}")
    (test / "lib" / "clib").mkdir()
    shutil.copyfile(clib / "ctest.so", test / "lib" / "clib" / "ctest")
    shutil.copytree(test / "common", test / "lib" / "common")
    return clib


def env_for(tree, clib, tmp):
    env = {k: v for k, v in os.environ.items() if not k.startswith(("LUA_INIT", "LUA_PATH", "LUA_CPATH"))}
    env["LUA_PATH"] = f"{tree / 'src'}/?.lua;;"   # jit.* modules (jit.dump, jit.v, ...)
    env["LUA_CPATH"] = f"{clib}/?.so;;"
    env["TMPDIR"] = str(tmp)
    env["LC_ALL"] = "C"
    return env


META = re.compile(r" +[-+@!]")


def split_meta(info):
    m = META.search(info)
    return (info, "") if not m else (info[:m.start()], info[m.start():].strip())


def enumerate_runner_tests(test):
    """Every test the index files reach, as (file, name, metadata), in plan
    order, without evaluating tags (the runner does that)."""
    result = []

    def scan(rel, meta):
        path = test / rel if rel else test
        if path.is_file():
            text = path.read_text(encoding="latin-1")
            for m in re.finditer(r"(?:^|(?<=\n))do +--- +([^\r\n]+)", text):
                name, tmeta = split_meta(m.group(1))
                result.append((rel, name, " ".join(x for x in (meta, tmeta) if x)))
            return
        for line in (path / "index").read_text().splitlines():
            if line:
                name, imeta = split_meta(line)
                scan(f"{rel}/{name}" if rel else name, " ".join(x for x in (meta, imeta) if x))

    scan("", "")
    return result


def apply_runner_skips(test, conds):
    """Tag skipped runner tests with +xlua_skipped, which the runner never sets."""
    for key, entry in SKIP.items():
        if not key.startswith("runner:") or not applies(entry, conds):
            continue
        rel, name = key[len("runner:"):].split(" --- ", 1)
        f = test / rel
        text = f.read_text(encoding="latin-1")
        pat = re.compile(r"(^|\n)(do +--- +" + re.escape(name) + r")((?: +[-+@!][^\r\n]*)?)(\r?\n)")
        hits = pat.findall(text)
        if len(hits) != 1:
            sys.exit(f"luajit_tests.py: SKIP {key}: expected the test exactly once in {f}, found {len(hits)}")
        text = pat.sub(lambda m: f"{m.group(1)}{m.group(2)} +xlua_skipped{m.group(3)}{m.group(4)}", text)
        f.write_text(text, encoding="latin-1")


TAG = re.compile(r"^[-+][A-Za-z_][\w.]*(?:[<>=]=?[\d.]+)?$|^![A-Za-z_]+$")
HEADER = re.compile(r"^\[ *(\d+)/(\d+)\] (.*?)(?: --- (.*))?$")


def run_runner(exe, test, env, logdir, args):
    """Run test.lua and return {(file, name, ordinal): (status, detail)}."""
    rc, text = run([*args.wrapper, str(exe), "test.lua", "+slow"], args.timeout, cwd=test, env=env,
                   logfile=logdir / "runner.log")
    lines = text.splitlines()
    started, errors, current = [], {}, None
    for line in lines:
        m = HEADER.match(line)
        if m:
            current = int(m.group(1))
            started.append((current, m.group(3), m.group(4) or ""))
        elif current is not None:
            errors.setdefault(current, []).append(line)
    failed = set()
    summary = next((l for l in reversed(lines) if re.match(r"^\d+ passed", l)), None)
    m = re.search(r"pass command line arguments: ([\d ]+)$", text, re.M)
    if m:
        failed = {int(x) for x in m.group(1).split()}
    elif summary and "failed" in summary:
        failed = {n for n, *_ in started}  # every test failed
    results, seen = {}, {}
    for n, f, name in started:
        k = (f, name)
        seen[k] = seen.get(k, 0) + 1
        key = (f, name, seen[k])
        err = [l for l in errors.get(n, []) if not re.match(r"^\d+ passed|^to run just failing", l)]
        if n in failed:
            results[key] = ("FAIL", " | ".join(err)[:600])
        else:
            results[key] = ("PASS", "")
    if summary is None:
        last = started[-1] if started else None
        reason = (f"timeout after {args.timeout}s" if rc is None else f"exit {rc} without a summary")
        if last:
            k = (last[1], last[2], seen[(last[1], last[2])])
            results[k] = ("CRASH" if rc is not None else "TIMEOUT",
                          f"{reason}; last test started: {last[1]} --- {last[2]}; "
                          + " | ".join(errors.get(last[0], []))[:600])
        results[("<runner>", "", 1)] = ("CRASH" if rc is not None else "TIMEOUT", reason)
    return results, summary, rc


def standalone_files(test):
    indexed = set((test / "lib" / "ffi" / "index").read_text().split())
    files = sorted(f"misc/{p.name}" for p in (test / "misc").glob("*.lua"))
    files += sorted(f"lib/ffi/{p.name}" for p in (test / "lib" / "ffi").glob("*.lua") if p.name not in indexed)
    files.append("lib/table/misc.lua")
    files += sorted(f"sysdep/{p.name}" for p in (test / "sysdep").glob("*.lua"))
    files += sorted(f"unportable/{p.name}" for p in (test / "unportable").glob("*.lua"))
    return files


def needs_ffi(path):
    return re.search(r"""require\s*\(?\s*["']ffi["']""", path.read_text(encoding="latin-1")) is not None


def run_standalone(exe, test, env, conds, logdir, args):
    results = {}
    sdir = logdir / "standalone"
    sdir.mkdir()
    for rel in standalone_files(test):
        key = f"standalone:{rel}"
        if key in SKIP and applies(SKIP[key], conds):
            results[rel] = ("SKIP", SKIP[key][1])
            continue
        path = test / rel
        if "no-ffi" in conds and needs_ffi(path):
            results[rel] = ("SKIP", "requires the FFI, which this mode disables")
            continue
        t0 = time.monotonic()
        rc, text = run([*args.wrapper, str(exe), path.name], args.file_timeout, cwd=path.parent, env=env,
                       logfile=sdir / (rel.replace("/", "_") + ".log"))
        secs = time.monotonic() - t0
        if rc is None:
            results[rel] = ("TIMEOUT", f"no exit after {args.file_timeout}s")
        elif rc != 0:
            tail = " | ".join(text.strip().splitlines()[-4:])
            results[rel] = ("FAIL" if rc > 0 else "CRASH", f"exit {rc} ({secs:.1f}s): {tail}"[:600])
        else:
            results[rel] = ("PASS", f"{secs:.1f}s")
    return results


def run_mode(mode, archive, out, args):
    mdir = out / mode
    mdir.mkdir()
    conds = conditions(mode, args)
    tree, exe = build_luajit(mode, mdir, args)
    rc, version = run([*args.wrapper, str(exe), "-e", "print(jit.version, jit.arch, jit.status())"], 60)
    log(f"== {mode}: {version.strip()}")
    test = extract(archive, mdir / "suite", conds)
    apply_runner_skips(test, conds)
    clib = build_helpers(tree, test, mode, args)
    tmp = mdir / "tmp"
    tmp.mkdir()
    env = env_for(tree, clib, tmp)

    rows = []  # (part, id, status, detail)
    t0 = time.monotonic()
    runner, summary, _ = run_runner(exe, test, env, mdir, args)
    stopped = ("<runner>", "", 1) in runner  # crash or timeout: later tests never started
    counts = {}
    for f, name, meta in enumerate_runner_tests(test):
        counts[(f, name)] = counts.get((f, name), 0) + 1
        key = (f, name, counts[(f, name)])
        tid = f"{f} --- {name}"
        status, detail = runner.pop(key, None) or ("SKIP", "")
        if status == "SKIP" and stopped:
            status, detail = "NOTRUN", "not started: the runner stopped first (or its tags exclude it)"
        elif status == "SKIP":
            if "+xlua_skipped" in meta:
                detail = SKIP[f"runner:{tid}"][1]
            else:
                # Only the tag conditions; test names may continue past " +".
                tags = " ".join(t for t in meta.split() if TAG.match(t))
                detail = f"not selected by the runner's tags: {tags}" if tags else "not selected by the runner"
        rows.append(("runner", tid, status, detail))
    for (f, name, _), (status, detail) in runner.items():  # crash marker or unparsed lines
        rows.append(("runner", f"{f} --- {name}" if name else f, status, detail))
    for rel, (status, detail) in run_standalone(exe, test, env, conds, mdir, args).items():
        rows.append(("standalone", rel, status, detail))
    secs = time.monotonic() - t0

    unexpected = []
    final = []
    for part, tid, status, detail in rows:
        key = f"{part}:{tid}"
        known = KNOWN_FAILURES.get(key)
        known = known if known and applies(known, conds) else None
        if status in ("FAIL", "CRASH", "TIMEOUT"):
            if known:
                status, detail = "XFAIL", f"{known[1]} [{status}: {detail}]"
            else:
                unexpected.append(f"{key}: {status} {detail}")
        elif status == "PASS" and known:
            status = "XPASS"
            unexpected.append(f"{key}: listed in KNOWN_FAILURES but passed")
        final.append((part, tid, status, detail))

    with open(mdir / "results.tsv", "w") as f:
        for row in final:
            f.write("\t".join(" ".join(x.split()) for x in row) + "\n")
    tally = {}
    for part, _, status, _ in final:
        tally.setdefault(part, {}).setdefault(status, 0)
        tally[part][status] += 1
    (mdir / "summary.json").write_text(json.dumps({"mode": mode, "luajit": version.strip(), "seconds": round(secs, 1),
                                                   "runner_summary": summary, "counts": tally}, indent=1))
    for part in ("runner", "standalone"):
        log(f"   {part}: " + ", ".join(f"{k} {v}" for k, v in sorted(tally.get(part, {}).items())))
    for line in unexpected:
        log(f"   UNEXPECTED {line}")
    log(f"   {secs:.0f}s; results: {mdir / 'results.tsv'}")
    return not unexpected


def unavailable(mode, args):
    """Why a mode cannot run on this host, or None."""
    x64 = target_machine(args) == "x86_64"
    if mode == "nogc64" and not x64:
        return "LuaJIT has no non-GC64 mode on ARM64 (lj_arch.h); it needs an x86_64 host"
    if mode == "nogc64" and sys.platform == "darwin":
        return "lj_arch.h requires GC64 on macOS"
    if mode == "nx" and sys.platform == "linux" and not x64:
        # GC64 keeps GC object pointers in 47 bits (checkptrGC, lj_def.h).
        # With LUAJIT_USE_SYSMALLOC the objects come from glibc malloc,
        # which on Linux arm64 (48-bit address space) returns addresses at or
        # above 2^47 (the heap follows the PIE base 0xaaaa...), so
        # lua_newstate fails with "not enough memory". LuaJIT's own allocator
        # asks mmap for low addresses instead.
        return ("LUAJIT_USE_SYSMALLOC with GC64 needs malloc addresses below 2^47; glibc on Linux arm64 "
                "(48-bit address space) returns higher ones")
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--output", required=True, help="new or empty output directory")
    ap.add_argument("--modes", default=None,
                    help="space-separated modes (default: all that this host can build)")
    ap.add_argument("--archives", help="directory that already holds the archive (still hash-checked)")
    ap.add_argument("--timeout", type=int, default=900, help="seconds per build step or runner run")
    ap.add_argument("--file-timeout", type=int, default=300, help="seconds per standalone file")
    ap.add_argument("--jobs", type=int, default=4)
    ap.add_argument("--cc", default=os.environ.get("CC"), help="C compiler (LuaJIT and the C helper)")
    ap.add_argument("--cxx", default=os.environ.get("CXX"), help="C++ compiler for the C++ helper")
    ap.add_argument("--cross", help="cross prefix for LuaJIT's Makefile (CROSS=), e.g. x86_64-linux-gnu-; "
                    "the C helpers then use <prefix>gcc and <prefix>g++")
    ap.add_argument("--host-cc", default="gcc", help="HOST_CC for a --cross build")
    ap.add_argument("--exec-wrapper", help='command that runs the built luajit, e.g. '
                    '"qemu-x86_64 -L /usr/x86_64-linux-gnu" for a --cross build')
    args = ap.parse_args()

    if sys.platform not in ("linux", "darwin"):
        sys.exit("luajit_tests.py: Linux and macOS only")
    if args.cross and sys.platform != "linux":
        sys.exit("luajit_tests.py: --cross is for Linux hosts")
    args.wrapper = shlex.split(args.exec_wrapper or "")
    modes = args.modes.split() if args.modes else [m for m in MODES if not unavailable(m, args)]
    for m in modes:
        if m not in MODES:
            sys.exit(f"luajit_tests.py: unknown mode {m} (have {', '.join(MODES)})")
        if unavailable(m, args):
            sys.exit(f"luajit_tests.py: mode {m} is not available here: {unavailable(m, args)}")
    out = Path(args.output).resolve()
    if out.exists() and any(out.iterdir()):
        sys.exit(f"luajit_tests.py: {out} is not empty")
    cache = out / "archives"
    cache.mkdir(parents=True)
    archive = fetch(args.archives, cache)
    log(f"LuaJIT-test-cleanup {SUITE_COMMIT[:12]} (SHA-256 {SUITE_SHA256[:12]}) against {LUAJIT}")

    failed = [m for m in modes if not run_mode(m, archive, out, args)]
    if failed:
        sys.exit("luajit_tests.py: unexpected results in: " + ", ".join(failed))
    print("XLUA_LUAJIT_TESTS_PASS")


if __name__ == "__main__":
    main()
