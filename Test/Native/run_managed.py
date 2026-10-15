"""Compile and run the existing general C# suite against a host-native plugin.

With --gen-code the suite also runs a second time with generated wrappers
(XLuaUnitTestGenCode), which exercises the code paths that generated code uses.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("--plugin", type=Path, required=True)
p.add_argument("--output", type=Path, required=True)
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--mono-version", default="6.14.1",
               help="required Mono version, or 'any' to skip the check")
p.add_argument("--gen-code", action="store_true",
               help="also generate wrappers with XLuaGenerate and run XLuaUnitTestGenCode")
a = p.parse_args()
if a.mono_version != "any":
    banner = subprocess.run([a.mono, "--version"], stdout=subprocess.PIPE, text=True, check=True).stdout
    found = re.search(r"version (\S+)", banner)
    if not found or found.group(1) != a.mono_version:
        raise SystemExit("Expected Mono " + a.mono_version + ", found: " + banner.splitlines()[0])
repo = Path(__file__).resolve().parents[2]
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
def sources(name):
    project = repo / "General/vs2013" / (name + ".csproj")
    tree = ET.parse(project)
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in tree.iter() if x.tag.endswith("}Compile")]
compiler = [a.mono, a.compiler] if a.compiler.endswith(".exe") else [a.compiler]
subprocess.run(compiler + ["-target:library", "-unsafe", "-define:XLUA_GENERAL",
    "-out:" + str(out / "XLua.Mini.dll")] + sources("XLua.Mini"), check=True)
subprocess.run(compiler + ["-unsafe", "-define:XLUA_GENERAL",
    "-r:" + str(out / "XLua.Mini.dll"), "-out:" + str(out / "XLuaUnitTest.exe")]
    + sources("XLuaUnitTest"), check=True)
shutil.copy2(a.plugin, out / a.plugin.name)
# Preserve the relative layout expected by LuaTestCommon without writing to the checkout.
shutil.copytree(repo / "Test/UnitTest", out / "Test/UnitTest", dirs_exist_ok=True)
(out / "General").mkdir(exist_ok=True)
env = dict(os.environ)
for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
    env[key] = str(out) + os.pathsep + env.get(key, "")


def run_suite(exe, log_name):
    result = subprocess.run([a.mono, str(exe)], cwd=out / "General",
        env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    (out / log_name).write_text(result.stdout)
    print(result.stdout)
    result.check_returncode()
    counts = re.findall(r"\[\s*PASSED\s*\]\s+(\d+) tests", result.stdout)
    if counts != ["277", "277"]:
        raise SystemExit(exe.name + ": expected 277 passing tests in both normal and coroutine modes: " + repr(counts))


run_suite(out / "XLuaUnitTest.exe", "managed-suite.log")
print("XLUA_MANAGED_SUITE_PASS: 277 tests in each of two modes")

if a.gen_code:
    tools = out / "tools"
    subprocess.run([sys.executable, str(repo / "Tools/build_tools.py"), "--output", str(tools),
                    "--compiler", a.compiler], check=True)
    gen = out / "Gen"
    shutil.rmtree(gen, ignore_errors=True)
    # XLuaGenerate starts a LuaEnv, so it needs the native plugin next to it.
    shutil.copy2(a.plugin, tools / a.plugin.name)
    gen_env = dict(env)
    for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
        gen_env[key] = str(tools) + os.pathsep + gen_env.get(key, "")
    subprocess.run([a.mono, str(tools / "XLuaGenerate.exe"), str(out / "XLuaUnitTest.exe"), str(gen) + "/"],
                   cwd=tools, env=gen_env, check=True)
    generated = sorted(str(f) for f in gen.glob("*.cs"))
    assert generated, "XLuaGenerate wrote no wrappers"
    # The project lists a checked-in Gen2 snapshot; use the freshly generated files instead.
    project_sources = [f for f in sources("XLuaUnitTestGenCode") if "/General/Gen2/" not in f]
    subprocess.run(compiler + ["-unsafe", "-define:XLUA_GENERAL",
        "-out:" + str(out / "XLuaUnitTestGenCode.exe")] + project_sources + generated, check=True)
    run_suite(out / "XLuaUnitTestGenCode.exe", "managed-suite-gencode.log")
    print("XLUA_MANAGED_GENCODE_PASS: 277 tests in each of two modes with", len(generated), "generated files")
