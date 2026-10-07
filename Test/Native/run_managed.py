"""Compile and run the existing general C# suite against a host-native plugin."""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("--plugin", type=Path, required=True)
p.add_argument("--output", type=Path, required=True)
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
a = p.parse_args()
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
result = subprocess.run([a.mono, str(out / "XLuaUnitTest.exe")], cwd=out / "General",
    env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
(out / "managed-suite.log").write_text(result.stdout)
print(result.stdout)
result.check_returncode()
counts = re.findall(r"\[\s*PASSED\s*\]\s+(\d+) tests", result.stdout)
if counts != ["275", "275"]:
    raise SystemExit("Expected 275 passing tests in both normal and coroutine modes: " + repr(counts))
print("XLUA_MANAGED_SUITE_PASS: 275 tests in each of two modes")
