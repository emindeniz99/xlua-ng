#!/usr/bin/env python3
"""Build the same native configuration in two checkouts and compare the bytes.

    python3 Test/Native/repro_check.py --work DIR [-- CMAKE_ARGS...]
    python3 Test/Native/repro_check.py --work DIR --script make_osx_lua55.sh [--env XLUA_PRESET=minimal]

The build/ folder (with Assets/XLua/Src, which bytecode-only builds read) is
copied to two places under DIR whose paths differ in name and length. In the
first copy the build directory lies inside build/, as the make_* scripts put
it; in the second it lies outside the checkout. Each copy is configured and
built with the same CMake arguments (default: Release, Lua 5.5.1), or, with
--script, runs that make_* script, and every library the builds write
(libxlua.so, libxlua.dylib, libxlua.a, xlua.dll and the macOS bundle; with
--script every file of the plugin_* folder it fills) must have the same
SHA-256. A difference names the object files that differ too, and for ELF
libraries the GNU build ID. Exit status 1 on a difference.

build/CMakeLists.txt maps both directories to fixed names
(-ffile-prefix-map, /pathmap) and refuses __DATE__ and __TIME__; build/README.md,
"Reproducible builds", has the measurements. --source takes another build/
folder, such as an older revision's, to see what the mapping changes.
--timeout bounds each configure, build and script run (default 1800 s); a
run that times out is killed with its process group.
"""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys

repo = Path(__file__).resolve().parents[2]
LIBRARY_NAMES = {"libxlua.so", "libxlua.dylib", "libxlua.a", "xlua.dll"}
# Output of earlier local builds in build/ (build_osx_55, build55, plugin_lua55 ...); never copied.
SKIP = ("build", "plugin_")


def run(cmd, cwd, env, timeout, log):
    """Run cmd in its own process group; on a timeout kill the whole group and stop."""
    with open(log, "ab") as out:
        out.write(("$ " + " ".join(str(c) for c in cmd) + "\n").encode())
        out.flush()
        proc = subprocess.Popen([str(c) for c in cmd], cwd=cwd, env=env, stdout=out, stderr=subprocess.STDOUT,
                                start_new_session=True)
        try:
            code = proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid, signal.SIGKILL)
            proc.wait()
            sys.exit("timed out after %d s: %s (log: %s)" % (timeout, " ".join(str(c) for c in cmd), log))
    if code != 0:
        tail = Path(log).read_text(errors="replace").splitlines()[-30:]
        sys.exit("failed (%d): %s\n%s\n(log: %s)" % (code, " ".join(str(c) for c in cmd), "\n".join(tail), log))


def copy_checkout(source, dest):
    """build/ and Assets/XLua/Src at dest, in the repository's layout."""
    ignore = shutil.ignore_patterns(".DS_Store", "__pycache__")

    def top_level(folder, names):
        skipped = set(ignore(folder, names))
        if Path(folder).resolve() == source.resolve():
            skipped |= {n for n in names if n.startswith(SKIP)}
        return skipped
    shutil.copytree(source, dest / "build", ignore=top_level, symlinks=True)
    src = repo / "Assets/XLua/Src"
    shutil.copytree(src, dest / "Assets/XLua/Src", ignore=shutil.ignore_patterns("*.meta"))


def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def outputs(root, script):
    """Relative path -> file, for the files the comparison covers."""
    found = {}
    for path in sorted(root.rglob("*")):
        if not path.is_file() or path.name == ".DS_Store":
            continue
        rel = path.relative_to(root).as_posix()
        if script or path.name in LIBRARY_NAMES or "/xlua.bundle/Contents/MacOS/" in "/" + rel:
            found[rel] = path
    return found


def objects(root):
    return {p.relative_to(root).as_posix(): p for p in sorted(root.rglob("*")) if p.suffix in (".o", ".obj")}


def build_id(path):
    """The GNU build ID of an ELF file, or None (readelf or llvm-readelf on PATH)."""
    for tool in ("readelf", "llvm-readelf"):
        if shutil.which(tool):
            out = subprocess.run([tool, "-n", str(path)], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                 text=True).stdout
            for line in out.splitlines():
                if "Build ID" in line:
                    return line.split(":", 1)[1].strip()
    return None


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--work", type=Path, required=True, help="an empty or new folder for the two checkouts")
    p.add_argument("--source", type=Path, default=repo / "build", help="the build/ folder to copy (default: this one)")
    p.add_argument("--script", help="run this build/make_*.sh in each copy instead of cmake")
    p.add_argument("--env", action="append", default=[], metavar="KEY=VALUE", help="environment for the builds")
    p.add_argument("--generator", help="CMake generator (default: CMake's)")
    p.add_argument("--timeout", type=int, default=1800, help="seconds per configure, build or script run")
    p.add_argument("--keep", action="store_true", help="keep the two checkouts")
    p.add_argument("cmake_args", nargs="*", help="after --: arguments for cmake (default: -DLUA_VERSION=5.5.1)")
    a = p.parse_args()
    work = a.work.resolve()
    if work.exists() and any(work.iterdir()):
        p.error("--work must be empty: " + str(work))
    env = dict(os.environ)
    for item in a.env:
        key, sep, value = item.partition("=")
        if not sep:
            p.error("--env takes KEY=VALUE: " + item)
        env[key] = value
    # The two checkouts: different names and lengths, build directories inside and outside build/.
    layouts = [(work / "a" / "xlua-ng", "build/out"),
               (work / "b" / "another-checkout-with-a-longer-path" / "xlua-ng-copy", "../../build-elsewhere")]
    results = []
    for n, (checkout, build_rel) in enumerate(layouts):
        copy_checkout(a.source.resolve(), checkout)
        log = work / ("build-%d.log" % n)
        if a.script:
            script = checkout / "build" / a.script
            if not script.is_file():
                p.error("no such script: build/" + a.script)
            run(["bash", script], checkout / "build", env, a.timeout, log)
            plugin_dirs = sorted(d for d in (checkout / "build").glob("plugin_*") if d.is_dir())
            if not plugin_dirs:
                sys.exit("build/%s wrote no plugin_* folder (log: %s)" % (a.script, log))
            results.append((checkout / "build", {rel: f for d in plugin_dirs for rel, f in
                                                 ((d.name + "/" + r, f) for r, f in outputs(d, True).items())}))
        else:
            build_dir = (checkout / build_rel).resolve()
            args = a.cmake_args or ["-DLUA_VERSION=5.5.1"]
            generator = ["-G", a.generator] if a.generator else []
            run(["cmake", "-S", checkout / "build", "-B", build_dir] + generator + args, checkout, env, a.timeout, log)
            run(["cmake", "--build", build_dir, "--config", "Release", "--parallel", "4"], checkout, env, a.timeout, log)
            results.append((build_dir, outputs(build_dir, False)))
        print("built %d: %s (log: %s)" % (n + 1, checkout, log))
    (root_a, files_a), (root_b, files_b) = results
    if not files_a:
        sys.exit("no library found in " + str(root_a))
    status = 0
    for rel in sorted(set(files_a) | set(files_b)):
        if rel not in files_a or rel not in files_b:
            print("MISSING  %s (only in the %s build)" % (rel, "first" if rel in files_a else "second"))
            status = 1
            continue
        da, db = digest(files_a[rel]), digest(files_b[rel])
        print("%s %s  %s" % ("SAME    " if da == db else "DIFFERS ", da, rel))
        if da != db:
            status = 1
            print("         second build: " + db)
            ids = build_id(files_a[rel]), build_id(files_b[rel])
            if ids[0] or ids[1]:
                print("         GNU build ID: %s / %s" % ids)
    if status:
        objs_a, objs_b = objects(root_a), objects(root_b)
        differing = [rel for rel, f in objs_a.items() if rel in objs_b and digest(f) != digest(objs_b[rel])]
        if differing:
            print("object files that differ (%d of %d):" % (len(differing), len(objs_a)))
            for rel in differing[:20]:
                print("  " + rel)
    if not a.keep:
        shutil.rmtree(work / "a", ignore_errors=True)
        shutil.rmtree(work / "b", ignore_errors=True)
    print("XLUA_REPRO_%s: %d file(s) compared%s" % ("PASS" if status == 0 else "FAIL", len(files_a),
                                                     ", " + " ".join(a.cmake_args) if a.cmake_args else ""))
    return status


if __name__ == "__main__":
    sys.exit(main())
