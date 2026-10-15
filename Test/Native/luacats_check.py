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
"""
import argparse
import filecmp
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import xml.etree.ElementTree as ET

LUALS_VERSION = "3.19.1"
LUALS_SHA256 = {
    "linux-x64": "e9235d2d72ef55bc41cf8c99cda2ed64777682024b4bb81f5dea425060c5cbb8",
    "linux-arm64": "abd2572e8fc929dc838a81ffb8473c5bce0bf39bfe8edb4b120b3b623176ce83",
}

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="host-native libxlua built with Lua 5.3 or later")
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--luals", type=Path, help="lua-language-server executable")
p.add_argument("--download-luals", action="store_true",
               help="download lua-language-server " + LUALS_VERSION + " (Linux) into the output directory")
p.add_argument("--update", action="store_true", help="rewrite Test/LuaCATS/expected")
a = p.parse_args()
out = a.output.resolve()
tools = out / "tools"
out.mkdir(parents=True, exist_ok=True)
case_dir = repo / "Test/LuaCATS"
expected = case_dir / "expected"


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
if a.update:
    if expected.exists():
        shutil.rmtree(expected)
    shutil.copytree(sample, expected)
    print("Updated", expected)
else:
    failures = []
    if tree(sample) != tree(expected):
        failures.append("file lists differ:\n  only generated: %s\n  only expected: %s" % (
            sorted(set(tree(sample)) - set(tree(expected))), sorted(set(tree(expected)) - set(tree(sample)))))
    for name in sorted(set(tree(sample)) & set(tree(expected))):
        if not filecmp.cmp(sample / name, expected / name, shallow=False):
            diff = subprocess.run(["diff", "-u", str(expected / name), str(sample / name)],
                                  stdout=subprocess.PIPE, text=True).stdout
            failures.append(diff)
    if failures:
        print("\n".join(failures))
        raise SystemExit("LuaCATS output differs from Test/LuaCATS/expected; rerun with --update if intended")
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
luals = a.luals
if a.download_luals:
    arch = {"x86_64": "linux-x64", "aarch64": "linux-arm64", "arm64": "linux-arm64"}[platform.machine()]
    name = "lua-language-server-%s-%s.tar.gz" % (LUALS_VERSION, arch)
    archive = out / name
    if not archive.exists():
        urllib.request.urlretrieve("https://github.com/LuaLS/lua-language-server/releases/download/%s/%s"
                                   % (LUALS_VERSION, name), archive)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != LUALS_SHA256[arch]:
        raise SystemExit("unexpected sha256 for %s: %s" % (name, digest))
    install = out / "luals"
    shutil.rmtree(install, ignore_errors=True)
    with tarfile.open(archive) as tar:
        tar.extractall(install)
    luals = install / "bin/lua-language-server"
if luals is None:
    print("lua-language-server not given; skipped the editor check")
    sys.exit(0)


def check(lua_file, library):
    workspace = out / ("ws-" + lua_file.stem)
    shutil.rmtree(workspace, ignore_errors=True)
    workspace.mkdir()
    shutil.copy2(lua_file, workspace / lua_file.name)
    (workspace / ".luarc.json").write_text(json.dumps({
        "runtime.version": "Lua 5.4",
        "workspace.library": [str(lib) for lib in library],
        "workspace.checkThirdParty": False,
    }, indent=2))
    logs = out / ("log-" + lua_file.stem)
    shutil.rmtree(logs, ignore_errors=True)
    # LuaLS exits with 1 when it reports problems; the report decides.
    cmd = [str(luals), "--check=" + str(workspace), "--checklevel=Warning", "--check_format=json",
           "--logpath=" + str(logs)]
    print("+", " ".join(cmd), flush=True)
    subprocess.run(cmd, cwd=workspace)
    report = logs / "check.json"
    found = []
    if report.exists():
        data = json.loads(report.read_text())
        # LuaLS writes [] when it finds nothing, else {uri: [diagnostic]}.
        for diags in (data.values() if isinstance(data, dict) else []):
            for d in diags:
                found.append((d["range"]["start"]["line"] + 1, d["code"], d["message"].splitlines()[0]))
    return sorted(found)


libraries = [unittest]
good = check(case_dir / "good.lua", libraries)
for line in good:
    print("good.lua:%d: %s: %s" % line)
if good:
    raise SystemExit("good.lua has %d diagnostics" % len(good))
print("LUACATS_LUALS_GOOD_PASS: 0 diagnostics")

bad_file = case_dir / "bad.lua"
want = sorted((i + 1, m.group(1)) for i, text in enumerate(bad_file.read_text().splitlines())
              for m in [re.search(r"-- expect: ([\w-]+)", text)] if m)
bad = check(bad_file, libraries)
for line in bad:
    print("bad.lua:%d: %s: %s" % line)
got = sorted(set((line, code) for line, code, _ in bad))
if got != want:
    raise SystemExit("bad.lua diagnostics differ:\n  expected %s\n  got      %s" % (want, got))
print("LUACATS_LUALS_BAD_PASS: %d expected diagnostics" % len(bad))
