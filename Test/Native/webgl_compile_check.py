"""Compile the Unity WebGL plugin source with a Unity editor's own Emscripten.

    python3 Test/Native/webgl_compile_check.py --output DIR [--emscripten DIR] [--package]

Unity compiles Assets/Plugins/WebGL/xlua_webgl.cpp (one C++ translation unit
that includes the Lua and bridge C sources inside extern "C") with the
Emscripten in PlaybackEngines/WebGLSupport/BuildTools/Emscripten. This runs
that emcc on the host, without starting Unity or needing a licence, with the
flags of Unity 6000.6's IL2CPP build (C++17, wasm exceptions, wasm longjmp),
then llvm-nm and check_webgl_exports.py on the object file.

--package also builds the UPM package (Tools/make_upm_package.py) and compiles
its copy, Plugins/WebGL/xlua_webgl.cpp with the C sources in build~.

Without --emscripten it uses the first editor under /Volumes/T7Data/Unity/Editors,
/Applications/Unity/Hub/Editor or ~/Unity/Hub/Editor that has the WebGL module.
Prints XLUA_WEBGL_COMPILE_PASS or exits with an error.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

here = Path(__file__).resolve().parent
repo = here.parents[1]
ROOTS = [Path("/Volumes/T7Data/Unity/Editors"), Path("/Applications/Unity/Hub/Editor"), Path.home() / "Unity/Hub/Editor"]
# What Unity 6000.6.4f1 passes for a release WebGL build (its bee log), less the include paths.
FLAGS = ["-std=c++17", "-O3", "-fwasm-exceptions", "-sSUPPORT_LONGJMP=wasm", "-sWASM_LEGACY_EXCEPTIONS=1",
         "-fno-rtti", "-fvisibility=hidden", "-ffunction-sections", "-fdata-sections", "-DUNITY_WEBGL=1",
         "-Wno-c++11-extensions", "-Wno-nonportable-include-path"]

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--output", type=Path, required=True)
p.add_argument("--emscripten", type=Path,
               help="a Unity editor's PlaybackEngines/WebGLSupport/BuildTools/Emscripten")
p.add_argument("--package", action="store_true", help="also compile the UPM package's copy")
a = p.parse_args()
if a.emscripten is None:
    found = sorted(root / v / "PlaybackEngines/WebGLSupport/BuildTools/Emscripten"
                   for root in ROOTS if root.is_dir() for v in os.listdir(root)
                   if (root / v / "PlaybackEngines/WebGLSupport/BuildTools/Emscripten/emscripten/emcc").is_file())
    if not found:
        raise SystemExit("no editor with the WebGL module found; pass --emscripten")
    a.emscripten = found[-1]
em = a.emscripten.resolve()
emcc = em / "emscripten/emcc"
nm = em / "llvm/llvm-nm"
python = em / "python/bin/python3"
for f in (emcc, nm):
    if not f.is_file():
        raise SystemExit("missing " + str(f))
env = dict(os.environ, EM_CONFIG=str(em / ".emscripten"), EM_FROZEN_CACHE="1", EMCC_SKIP_SANITY_CHECK="1")
if python.is_file():
    env["EMSDK_PYTHON"] = str(python)
out = a.output.resolve()
if out.exists():
    shutil.rmtree(out)
out.mkdir(parents=True)

sources = [("assets", repo / "Assets/Plugins/WebGL/xlua_webgl.cpp")]
if a.package:
    pkg = out / "package"
    subprocess.run([sys.executable, str(repo / "Tools/make_upm_package.py"), "--output", str(pkg), "--no-samples"],
                   check=True, stdout=subprocess.DEVNULL)
    sources.append(("package", pkg / "Plugins/WebGL/xlua_webgl.cpp"))

problems = []
for name, source in sources:
    obj = out / (name + ".o")
    r = subprocess.run([str(emcc)] + FLAGS + ["-c", "-xc++", str(source), "-o", str(obj)], env=env,
                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=900)
    (out / (name + ".log")).write_text(r.stdout)
    errors = [l for l in r.stdout.splitlines() if " error: " in l]
    if r.returncode or not obj.is_file():
        problems.append("%s: emcc exited %d with %d errors, the first: %s (log %s)" % (
            name, r.returncode, len(errors), errors[0] if errors else "-", out / (name + ".log")))
        continue
    symbols = out / (name + ".nm.txt")
    with open(symbols, "w") as f:
        subprocess.run([str(nm), "--defined-only", str(obj)], stdout=f, check=True)
    r = subprocess.run([sys.executable, str(here / "check_webgl_exports.py"), str(symbols)],
                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    print("%s: %s" % (name, r.stdout.strip()))
    if r.returncode:
        problems.append(name + ": exports: " + r.stdout.strip())
if problems:
    raise SystemExit("XLUA_WEBGL_COMPILE_FAIL (%s):\n  %s" % (em, "\n  ".join(problems)))
print("XLUA_WEBGL_COMPILE_PASS: %s compiled with %s" % (", ".join(n for n, _ in sources), em))
