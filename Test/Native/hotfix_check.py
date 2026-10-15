"""Check hotfix injection end to end with XLuaHotfixInject and Mono.

1. Builds the tools (Tools/build_tools.py) and compiles Test/Native/HotfixFixture.cs
   with the xLua sources (XLUA_GENERAL, HOTFIX_ENABLE).
2. Runs XLuaGenerate on that assembly and recompiles it with the generated
   wrappers and hotfix bridges, which the injector needs.
3. Builds the final assembly twice, with the .mdb symbols that Mono's mcs
   writes for -debug and without symbols. For each, injects it, checks that a
   second injection reports "had injected!" and leaves the file unchanged, and
   runs the fixture under `mono --debug`: original behaviour before patching,
   patched behaviour after xlua.hotfix, original behaviour again after
   xlua.hotfix(..., nil), and whether stack traces of instrumented methods
   still have line numbers.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

# Line numbers in stack traces before and after injection, per symbol format.
SYMBOLS = {"mdb": "present", "none": "missing"}

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="host-native libxlua")
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
a = p.parse_args()
out = a.output.resolve()
tools = out / "tools"
out.mkdir(parents=True, exist_ok=True)
env = dict(os.environ)
for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
    env[key] = os.pathsep.join([str(tools), env.get(key, "")])


def run(cmd, **kw):
    print("+", " ".join(str(c) for c in cmd), flush=True)
    return subprocess.run([str(c) for c in cmd], check=True, **kw)


def run_output(cmd, cwd):
    print("+", " ".join(str(c) for c in cmd), flush=True)
    result = subprocess.run([str(c) for c in cmd], cwd=cwd, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True)
    print(result.stdout, flush=True)
    return result


def xlua_sources():
    project = repo / "General/vs2013/XLua.Mini.csproj"
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]


def compile_fixture(dest, extra_sources=(), debug=False):
    dest.parent.mkdir(parents=True, exist_ok=True)
    run([a.compiler, "-unsafe", "-define:XLUA_GENERAL;HOTFIX_ENABLE", "-out:" + str(dest)]
        + (["-debug"] if debug else []) + xlua_sources() + [repo / "Test/Native/HotfixFixture.cs"]
        + list(extra_sources))
    shutil.copy2(a.plugin, dest.parent / a.plugin.name)


def fixture(exe, *args):
    result = run_output([a.mono, "--debug", exe] + list(args), cwd=exe.parent)
    if result.returncode != 0 or "HOTFIX_FIXTURE_PASS" not in result.stdout:
        raise SystemExit("fixture failed: " + " ".join([exe.name] + list(args)))
    for state in ["present", "missing"]:
        if "SYMBOLS: " + state in result.stdout:
            return state
    raise SystemExit("fixture printed no SYMBOLS line")


def inject(exe, id_map):
    result = run_output([a.mono, tools / "XLuaHotfixInject.exe", exe, exe, id_map], cwd=exe.parent)
    result.check_returncode()
    return result.stdout


run([sys.executable, repo / "Tools/build_tools.py", "--output", tools, "--compiler", a.compiler])
shutil.copy2(a.plugin, tools / a.plugin.name)

# The injector looks for the hotfix bridges that XLuaGenerate writes.
stage1 = out / "stage1/HotfixFixture.exe"
compile_fixture(stage1)
gen = out / "Gen"
shutil.rmtree(gen, ignore_errors=True)
run([a.mono, tools / "XLuaGenerate.exe", stage1, str(gen) + "/"], cwd=tools, env=env)
generated = sorted(gen.glob("*.cs"))
if not any(f.name == "DelegatesGensBridge.cs" for f in generated):
    raise SystemExit("XLuaGenerate wrote no DelegatesGensBridge.cs")

for symbols in SYMBOLS:
    print("== symbols: %s" % symbols, flush=True)
    exe = out / symbols / "HotfixFixture.exe"
    shutil.rmtree(exe.parent, ignore_errors=True)
    compile_fixture(exe, generated, debug=symbols != "none")
    if symbols == "mdb" and not Path(str(exe) + ".mdb").is_file():
        raise SystemExit("mcs -debug wrote no .mdb")

    if fixture(exe, "baseline") != SYMBOLS[symbols]:
        raise SystemExit("unexpected line numbers before injection")
    id_map = exe.parent / "hotfix_id_map.lua"
    original = exe.read_bytes()
    log = inject(exe, id_map)
    if "inject finish!" not in log or "Error:" in log or "Exception" in log:
        raise SystemExit("injection failed")
    injected = exe.read_bytes()
    if injected == original:
        raise SystemExit("injection did not change the assembly")

    log = inject(exe, exe.parent / "second_id_map.lua")
    if "had injected!" not in log or exe.read_bytes() != injected:
        raise SystemExit("second injection was not refused")

    state = fixture(exe, "patch", id_map)
    if state != SYMBOLS[symbols]:
        raise SystemExit("line numbers after injection with %s symbols: %s, expected %s"
                         % (symbols, state, SYMBOLS[symbols]))
    print("HOTFIX_%s_PASS (line numbers %s after injection)" % (symbols.upper(), state), flush=True)

print("HOTFIX_CHECK_PASS")
