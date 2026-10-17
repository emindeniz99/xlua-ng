#!/usr/bin/env python3
"""Run Lua's official test suites (https://www.lua.org/tests/) against the
vendored, xLua-patched Lua trees.

For each runtime it downloads the pinned suite archive (or takes it from
--archives), checks its size and SHA-256, builds a stand-alone `lua` from
build/CMakeLists.txt with -DXLUA_LUA_INTERPRETER=ON (the libxlua core sources
and definitions plus lua.c) and runs the suite as lua.org describes:

  basic     lua -e"_U=true" all.lua   (portable subset)
  complete  lua all.lua               (with the C libraries in libs/ built)
  internal  lua all.lua               (interpreter built with ltests.c and
                                       LUA_USER_H="ltests.h", so the testC
                                       library and internal checks are on)

A run passes when the interpreter exits with 0 and prints "final OK".
Every run uses a fresh copy of the suite. Linux and macOS only.

    python3 Test/Native/lua_tests.py --output /abs/empty-dir [--versions 5.4.9] [--modes basic]

The archives are test data from lua.org: only the interpreters built here run
them, and their C libraries are compiled by this script, not by their makefile.
"""
import argparse
import hashlib
import os
import platform
import shutil
import signal
import subprocess
import sys
import tarfile
import time
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
BASE_URL = "https://www.lua.org/tests/"

# Pinned suites, read by Tools/dependencies.py for DEPENDENCIES.md and the
# SBOM. SHA-256 and size as published on https://www.lua.org/tests/ and
# checked after download on 2026-10-17. lua.org has no 5.3.5 or 5.3.6 suite;
# its page says to use the closest release of the same version, 5.3.4.
SUITES = {
    "5.3.6": ("lua-5.3.4-tests.tar.gz", 103438, "b80771238271c72565e5a1183292ef31bd7166414cd0d43a8eb79845fa7f599f"),
    "5.4.9": ("lua-5.4.9-tests.tar.gz", 137260, "7d971845f545ffc09fbb3128a86b2c6524161c70d0fdf0154a16e8c00c343fca"),
    "5.5.1": ("lua-5.5.1-tests.tar.gz", 150553, "da07b543872dc0bb2ff12aabd0c248578d78df3eb6b67efdc537a46d455c7f31"),
}
MODES = ("basic", "complete", "internal")

# xLua removes os.execute from every PUC runtime (iOS 11 compatibility; see
# build/LUA_PATCHES.md). main.lua and files.lua need it only in the complete
# and internal modes, where `_port` is false. This prelude restores an
# equivalent built on io.popen, whose close() returns what os.execute returns
# (luaL_execresult in both), and copies the command's stdout through. The
# patch itself is checked first: os.execute must be nil in the built lua.
OS_EXECUTE_SHIM = (
    "assert(os.execute == nil, 'xLua patch: os.execute should be removed') "
    "os.execute = function (cmd) "
    "if cmd == nil then return true end "
    "io.stdout:flush() "
    "local p = assert(io.popen(cmd, 'r')) "
    "for chunk in function () return p:read(4096) end do io.stdout:write(chunk) end "
    "return p:close() end"
)

# libxlua's LUA_IDSIZE (build/CMakeLists.txt default; upstream Lua uses 60).
LUA_IDSIZE = 120

# Edits to the extracted suite copy: (runtimes or None for all, condition or
# None for always, file, old text, new text, reason). Conditions: "darwin"
# (running on macOS) and "compatible-format" (--compatible-format). Each old
# text must occur exactly once where it applies, so a changed suite stops the
# run. Nothing else in the suites is changed.
ADJUST = [
    # Configuration difference caused by xLua (build/LUA_PATCHES.md).
    (None, None, "errors.lua", "idsize = 60 - 1", f"idsize = {LUA_IDSIZE} - 1",
     f"checksize assumes upstream LUA_IDSIZE 60; xLua builds with {LUA_IDSIZE}"),
    (None, None, "errors.lua", "for i = 60 - 10, 60 + 10 do", f"for i = {LUA_IDSIZE} - 10, {LUA_IDSIZE} + 10 do",
     f"move the border cases to LUA_IDSIZE {LUA_IDSIZE}"),
    # Environment: on macOS luaconf.h makes lua.c use readline, which is
    # libedit there. Its REPL does not echo prompts for piped input the way
    # GNU readline and the plain fgets fallback do, so main.lua's `-i` tests
    # fail (also with stock Lua). main.lua runs on Linux.
    (None, "darwin", "all.lua", "dofile('main.lua')", "-- dofile('main.lua')  -- skipped on macOS (libedit)",
     "macOS readline is libedit"),
    # Environment: macOS /bin/sh (bash) execs the last command of `sh -c`, so
    # `sh -c 'kill -s HUP $$'` run through another sh ends by signal instead
    # of the "exit" status the suite expects from a shell that forks.
    (None, "darwin", "files.lua", '''    {"sh -c 'kill -s HUP $$'", "exit"},\n''', "",
     "macOS sh execs the last command"),
    # Environment: macOS mktime accepts tm_year = 2^31 - 1 with sec = 60
    # (it returns a time instead of failing), so os.time does not raise.
    (("5.4.9", "5.5.1"), "darwin", "files.lua", '        checkerr("represented", os.time,\n',
     '        local _ = true or checkerr("represented", os.time,\n', "macOS mktime does not overflow"),
    # Environment: macOS has no /dev/full (internal mode, T.nonblock).
    (("5.5.1",), "darwin", "files.lua",
     '  -- unable to write anything to /dev/full\n  local f = io.open("/dev/full", "w")\n'
     '  assert(f:setvbuf("no"))\n  local _, _, err, count = f:write("abcd")\n'
     '  assert(err > 0 and count == 0)\n  assert(f:close())\n',
     '  -- unable to write anything to /dev/full (skipped: macOS has none)\n', "macOS has no /dev/full"),
    # Environment: macOS has no /dev/full. The label at the end of the block
    # is outside the scope of `local f`, so the goto is valid Lua 5.5.
    (("5.5.1",), "darwin", "files.lua", '  local f = io.output("/dev/full")',
     '  goto no_dev_full\n  local f = io.output("/dev/full")', "macOS has no /dev/full"),
    (("5.5.1",), "darwin", "files.lua",
     '  assert(not io.flush())    -- cannot write to device\n  assert(f:close())\nend',
     '  assert(not io.flush())    -- cannot write to device\n  assert(f:close())\n  ::no_dev_full::\nend',
     "end of the skipped /dev/full block"),
    # xLua's opt-in Lua 5.3 LUAC_COMPATIBLE_FORMAT bytecode writes format 1
    # and drops the sizeof(size_t) byte from the header (lundump.h, ldump.c).
    (("5.3.6",), "compatible-format", "calls.lua", 'local header = string.pack("c4BBc6BBBBBj",',
     'local header = string.pack("c4BBc6BBBBj",', "header has one size byte less"),
    (("5.3.6",), "compatible-format", "calls.lua", '    0,                       -- format\n',
     '    1,                       -- format (LUAC_COMPATIBLE_FORMAT)\n', "format byte is 1"),
    (("5.3.6",), "compatible-format", "calls.lua", '    string.packsize("T"),    -- sizeof(size_t)\n', "",
     "no sizeof(size_t) byte"),
]


def log(msg):
    print(msg, flush=True)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 16), b""):
            h.update(block)
    return h.hexdigest()


def fetch(version, archives, cache):
    name, size, digest = SUITES[version]
    dest = cache / name
    if not dest.exists():
        if archives:
            shutil.copyfile(Path(archives) / name, dest)
        else:
            log(f"downloading {BASE_URL}{name}")
            with urllib.request.urlopen(BASE_URL + name, timeout=120) as r, open(dest, "wb") as f:
                shutil.copyfileobj(r, f)
    got_size, got = dest.stat().st_size, sha256(dest)
    if got_size != size or got != digest:
        dest.unlink()
        sys.exit(f"lua_tests.py: {name}: size {got_size}, SHA-256 {got}; expected {size}, {digest}")
    return dest


def extract(version, archive, into, conds):
    """Extract into a new directory, apply ADJUST, return the suite directory."""
    into.mkdir(parents=True)
    with tarfile.open(archive) as t:
        t.extractall(into, filter="data")
    (suite,) = into.iterdir()
    for versions, plat, name, old, new, _ in ADJUST:
        if (versions and version not in versions) or (plat and plat not in conds):
            continue
        f = suite / name
        text = f.read_text(encoding="latin-1")
        if text.count(old) != 1:
            sys.exit(f"lua_tests.py: {f}: expected {old!r} exactly once")
        f.write_text(text.replace(old, new), encoding="latin-1")
    return suite


def run(cmd, timeout, cwd=None, env=None, logfile=None):
    """Run cmd in its own process group; on timeout kill the whole group.

    stdin is an empty pipe: files.lua expects io.stdin:seek to fail, which it
    does not when stdin is /dev/null or a regular file (as under CI runners).
    """
    out = open(logfile, "w") if logfile else None
    try:
        p = subprocess.Popen(cmd, cwd=cwd, env=env, stdin=subprocess.PIPE, stdout=out or subprocess.PIPE,
                             stderr=subprocess.STDOUT, start_new_session=True, text=True)
        try:
            text, _ = p.communicate(input="", timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(p.pid, signal.SIGKILL)
            p.wait()
            return None, (text_of(logfile) if logfile else "")
        return p.returncode, (text_of(logfile) if logfile else text)
    finally:
        if out:
            out.close()


def text_of(path):
    return Path(path).read_text(encoding="utf-8", errors="replace")


def build_lua(version, build, args, ltests=None):
    cfg = ["cmake", "-S", str(REPO / "build"), "-B", str(build), f"-DLUA_VERSION={version}",
           "-DXLUA_LUA_INTERPRETER=ON", f"-DLUA_IDSIZE={LUA_IDSIZE}", f"-DCMAKE_BUILD_TYPE={args.build_type}"]
    if sys.platform == "darwin" and platform.machine() == "arm64":
        cfg.append("-DBUILD_SILICON=ON")
    if ltests:
        cfg.append(f"-DXLUA_LUA_LTESTS_DIR={ltests}")
    if args.sanitize:
        cfg.append(f"-DXLUA_SANITIZE={args.sanitize}")
    if args.compatible_format:
        cfg.append("-DLUAC_COMPATIBLE_FORMAT=ON")
    for cmd in (cfg, ["cmake", "--build", str(build), "--target", "xlua_lua", "--parallel", str(args.jobs)]):
        rc, text = run(cmd, args.timeout)
        if rc != 0:
            sys.exit(f"lua_tests.py: {' '.join(cmd)} failed ({'timeout' if rc is None else rc}):\n{text[-4000:]}")
    lua = build / "lua"
    if not lua.is_file():
        sys.exit(f"lua_tests.py: {lua} was not built")
    return lua


def build_libs(version, suite, build, cc):
    """Build the C modules in libs/ that attrib.lua loads (what libs/makefile does)."""
    libs = suite / "libs"
    inc = ["-I", str(REPO / "build" / f"lua-{version}" / "src"), "-I", str(build)]
    shared = ["-bundle", "-undefined", "dynamic_lookup"] if sys.platform == "darwin" else ["-shared"]
    # 5.4 and 5.5 build lib2-v2.so from lib22.c; 5.3.4's makefile renames lib2.so.
    targets = [("lib1.so", "lib1.c"), ("lib11.so", "lib11.c"), ("lib2.so", "lib2.c"), ("lib21.so", "lib21.c")]
    if (libs / "lib22.c").is_file():
        targets.append(("lib2-v2.so", "lib22.c"))
    for out, src in targets:
        subprocess.run([cc, "-std=gnu99", "-O2", "-fPIC", *inc, *shared, "-o", out, src], cwd=libs, check=True)
    if not (libs / "lib22.c").is_file():
        (libs / "lib2.so").rename(libs / "lib2-v2.so")


def suite_env(tmp, lua):
    """Environment for a suite run. The built lua comes first on PATH because
    a few tests start a plain `lua` through the shell (files.lua's popen
    receiver), and that must be the interpreter under test."""
    env = {k: v for k, v in os.environ.items()
           if not k.startswith(("LUA_INIT", "LUA_PATH", "LUA_CPATH"))}
    env["PATH"] = f"{lua.parent}{os.pathsep}{env.get('PATH', '')}"
    env["TMPDIR"] = str(tmp)
    env["LC_ALL"] = "C"
    return env


def run_suite(version, mode, lua, suite, logfile, args):
    tmp = suite.parent / "tmp"
    tmp.mkdir()
    cmd = [str(lua)]
    if mode == "basic":
        cmd += ["-e", "_U=true"]
    else:
        cmd += ["-e", OS_EXECUTE_SHIM]
    cmd.append("all.lua")
    t0 = time.monotonic()
    rc, text = run(cmd, args.timeout, cwd=suite, env=suite_env(tmp, lua), logfile=logfile)
    secs = time.monotonic() - t0
    if rc is None:
        last = [l for l in text.splitlines() if l.startswith("***** FILE")]
        return False, f"timeout after {args.timeout}s; last file started: {last[-1] if last else 'none'}"
    if rc != 0 or "final OK" not in text:
        tail = "\n".join(text.splitlines()[-15:])
        return False, f"exit {rc}, no 'final OK' ({secs:.0f}s):\n{tail}"
    return True, f"final OK ({secs:.0f}s)"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--output", required=True, help="new or empty output directory")
    ap.add_argument("--versions", default=" ".join(SUITES), help="space-separated runtimes (default: all)")
    ap.add_argument("--modes", default=" ".join(MODES), help="space-separated modes (default: all)")
    ap.add_argument("--archives", help="directory that already holds the archives (still hash-checked)")
    ap.add_argument("--timeout", type=int, default=1800, help="seconds per build step or suite run")
    ap.add_argument("--jobs", type=int, default=4)
    ap.add_argument("--build-type", default="Release")
    ap.add_argument("--sanitize", default="", help="XLUA_SANITIZE list, e.g. address,undefined")
    ap.add_argument("--compatible-format", action="store_true",
                    help="build Lua 5.3 with xLua's LUAC_COMPATIBLE_FORMAT bytecode (5.3.6 only)")
    ap.add_argument("--cc", default=os.environ.get("CC", "cc"), help="compiler for the suite's C libraries")
    args = ap.parse_args()

    out = Path(args.output).resolve()
    if out.exists() and any(out.iterdir()):
        sys.exit(f"lua_tests.py: {out} is not empty")
    versions, modes = args.versions.split(), args.modes.split()
    for v in versions:
        if v not in SUITES:
            sys.exit(f"lua_tests.py: unknown runtime {v} (have {', '.join(SUITES)})")
    for m in modes:
        if m not in MODES:
            sys.exit(f"lua_tests.py: unknown mode {m} (have {', '.join(MODES)})")
    if args.compatible_format and versions != ["5.3.6"]:
        sys.exit("lua_tests.py: --compatible-format needs --versions 5.3.6")
    conds = {sys.platform} | ({"compatible-format"} if args.compatible_format else set())
    cache = out / "archives"
    cache.mkdir(parents=True)

    failures = []
    for version in versions:
        archive = fetch(version, args.archives, cache)
        log(f"== Lua {version}: {archive.name} (SHA-256 {SUITES[version][2][:12]})")
        vdir = out / version
        lua = None
        for mode in modes:
            suite = extract(version, archive, vdir / mode, conds)
            if mode == "internal":
                exe = build_lua(version, vdir / "build-ltests", args, ltests=suite / "ltests")
                build = vdir / "build-ltests"
            else:
                lua = lua or build_lua(version, vdir / "build", args)
                exe, build = lua, vdir / "build"
            if mode != "basic":
                build_libs(version, suite, build, args.cc)
            ok, detail = run_suite(version, mode, exe, suite, vdir / f"{mode}.log", args)
            log(f"{'PASS' if ok else 'FAIL'} {version} {mode}: {detail}  [{vdir / (mode + '.log')}]")
            if not ok:
                failures.append(f"{version} {mode}")
    if failures:
        sys.exit("lua_tests.py: failed: " + ", ".join(failures))
    print("XLUA_LUA_TESTS_PASS")


if __name__ == "__main__":
    main()
