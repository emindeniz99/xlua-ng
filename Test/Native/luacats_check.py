"""Check the LuaCATS generator (XLuaGenerate --luacats).

1. Builds the tools, compiles Test/LuaCATS/Sample.cs and generates its
   definitions, then compares them with Test/LuaCATS/expected (--update
   rewrites the expected files instead).
2. Generates definitions for the fixture together with the general unit-test
   assembly (XLuaUnitTest.exe, the one run_managed.py runs) as a smoke test;
   the LuaLS check below uses them.
3. With --luals (or --download-luals), runs lua-language-server in check mode
   over Test/LuaCATS/good.lua, which must have no diagnostics, and bad.lua,
   whose diagnostics must match its `-- expect: <code>` comments line by line
   (one code per line; LuaLS may report it once per argument).

With --luajit the plugin is a LuaJIT build. Generated files are compared with
Test/LuaCATS/expected-luajit where that directory has them, else with
Test/LuaCATS/expected, and LuaLS also checks good_luajit.lua and bad_luajit.lua
for the int64 userdata that LuaJIT uses for long and ulong.
"""
import argparse
import filecmp
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

import luals as luals_tools

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="host-native libxlua built with Lua 5.3 or later, or LuaJIT")
p.add_argument("--luajit", action="store_true", help="the plugin is built with LuaJIT")
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--luals", type=Path, help="lua-language-server executable")
p.add_argument("--download-luals", action="store_true",
               help="download lua-language-server " + luals_tools.VERSION + " (Linux) into the output directory")
p.add_argument("--update", action="store_true",
               help="rewrite Test/LuaCATS/expected (with --luajit: expected-luajit, only the files that differ)")
a = p.parse_args()
out = a.output.resolve()
tools = out / "tools"
out.mkdir(parents=True, exist_ok=True)
case_dir = repo / "Test/LuaCATS"
expected = case_dir / "expected"
# LuaJIT output differs only where integers appear; those files override expected.
expected_luajit = case_dir / "expected-luajit"


def run(cmd, **kw):
    print("+", " ".join(str(c) for c in cmd), flush=True)
    return subprocess.run([str(c) for c in cmd], check=True, **kw)


def sources(name):
    project = repo / "General/vs2013" / (name + ".csproj")
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]


def generate(assemblies, dest):
    if dest.exists():
        shutil.rmtree(dest)
    env = dict(os.environ)
    env["LD_LIBRARY_PATH"] = str(tools) + os.pathsep + env.get("LD_LIBRARY_PATH", "")
    env["DYLD_LIBRARY_PATH"] = str(tools) + os.pathsep + env.get("DYLD_LIBRARY_PATH", "")
    run([a.mono, tools / "XLuaGenerate.exe", "--luacats"] + assemblies + [dest], cwd=tools, env=env)


def tree(root):
    return sorted(str(f.relative_to(root)) for f in root.rglob("*") if f.is_file())


run([sys.executable, repo / "Tools/build_tools.py", "--output", tools, "--compiler", a.compiler])
shutil.copy2(a.plugin, tools / a.plugin.name)

# 1. Golden files for the fixture assembly.
run([a.compiler, "-target:library", "-r:" + str(tools / "XLua.Mini.dll"),
     "-out:" + str(tools / "LuaCATSSample.dll"), case_dir / "Sample.cs"])
sample = out / "sample"
generate([tools / "LuaCATSSample.dll"], sample)


def golden(name):
    override = expected_luajit / name
    return override if a.luajit and override.exists() else expected / name


if a.update and not a.luajit:
    if expected.exists():
        shutil.rmtree(expected)
    shutil.copytree(sample, expected)
    print("Updated", expected)
elif a.update:
    shutil.rmtree(expected_luajit, ignore_errors=True)
    for name in tree(sample):
        if not (expected / name).exists() or not filecmp.cmp(sample / name, expected / name, shallow=False):
            (expected_luajit / name).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(sample / name, expected_luajit / name)
    print("Updated", expected_luajit)
else:
    failures = []
    names = set(tree(expected)) | (set(tree(expected_luajit)) if a.luajit and expected_luajit.exists() else set())
    if set(tree(sample)) != names:
        failures.append("file lists differ:\n  only generated: %s\n  only expected: %s" % (
            sorted(set(tree(sample)) - names), sorted(names - set(tree(sample)))))
    for name in sorted(set(tree(sample)) & names):
        if not filecmp.cmp(sample / name, golden(name), shallow=False):
            diff = subprocess.run(["diff", "-u", str(golden(name)), str(sample / name)],
                                  stdout=subprocess.PIPE, text=True).stdout
            failures.append(diff)
    if failures:
        print("\n".join(failures))
        raise SystemExit("LuaCATS output differs from the expected files; rerun with --update if intended")
    print("LUACATS_GOLDEN_PASS: %d files match" % len(tree(sample)))

# 2. Smoke test on the general unit-test assembly.
run([a.compiler, "-target:library", "-unsafe", "-define:XLUA_GENERAL",
     "-out:" + str(out / "XLua.Mini.dll")] + sources("XLua.Mini"))
run([a.compiler, "-unsafe", "-define:XLUA_GENERAL", "-r:" + str(out / "XLua.Mini.dll"),
     "-out:" + str(tools / "XLuaUnitTest.exe")] + sources("XLuaUnitTest"))
unittest = out / "unittest"
generate([tools / "LuaCATSSample.dll", tools / "XLuaUnitTest.exe"], unittest)
for name in ["CS.lua", "xlua.lua", "xlua/util.lua", "LuaTestObj.lua", "LuaTestType.lua", "GenCodeDrivedClass.lua"]:
    text = (unittest / name).read_text()
    if not text.startswith("---@meta\n"):
        raise SystemExit("missing or invalid " + name)
print("LUACATS_UNITTEST_PASS: %d files for XLuaUnitTest.exe" % len(tree(unittest)))

# 3. lua-language-server check mode.
luals = luals_tools.download(out) if a.download_luals else a.luals
if luals is None:
    print("lua-language-server not given; skipped the editor check")
    sys.exit(0)

runtime = "LuaJIT" if a.luajit else "Lua 5.4"
libraries = [unittest]
suffixes = ["", "_luajit"] if a.luajit else [""]
for good_file in [case_dir / ("good%s.lua" % x) for x in suffixes]:
    luals_tools.check_good(luals, good_file, libraries, out, runtime)
for bad_file in [case_dir / ("bad%s.lua" % x) for x in suffixes]:
    luals_tools.check_bad(luals, bad_file, libraries, out, runtime)
