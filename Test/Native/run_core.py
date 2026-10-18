"""Compile and run LuaScript's host tests (Test/Core) on Mono and .NET.

The tests are compiled with the .NET SDK's Roslyn (mise.toml) from
Assets/XLua/Core alone, without the bridge, with XLUA_GENERAL and
XLUA_LUASCRIPT: for Mono against Mono's 4.5 profile, for .NET against the SDK's
reference pack, and run next to the given native library, which must have
LuaScript's sandbox (XLUA_PRESET=core, or a bridge library built with
XLUA_LUASCRIPT=ON). With --bridge the tests are compiled with the whole
runtime instead (the XLua.Mini sources of General/vs2013, Core included) and
also run TestLuaEnvAndLuaScriptSideBySide, which needs a bridge library with
XLUA_LUASCRIPT=ON. Each run has a timeout; on a timeout the process group is
killed and the test that was running is reported. Every run must pass all
EXPECTED_TESTS tests.

    python3 Test/Native/run_core.py --plugin BUILD/libxlua.dylib --output DIR [--runtime mono|dotnet|both]
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys

# Test* methods in Test/Core/CoreTests.cs, RegisterTests.cs, BufferTests.cs,
# TableTests.cs, LubanFixtureTests.cs and StopLatencyTests.cs; every run must pass
# all of them (--bridge adds the BRIDGE_TESTS under XLUA_CORE_TEST_BRIDGE).
EXPECTED_TESTS = 68
BRIDGE_TESTS = 2

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True)
p.add_argument("--output", type=Path, required=True)
p.add_argument("--runtime", choices=["mono", "dotnet", "both"], default="both")
p.add_argument("--mono", default="mono")
p.add_argument("--dotnet", default=None, help="dotnet executable (default: `mise which dotnet`, else dotnet on PATH)")
p.add_argument("--timeout", type=int, default=600, help="seconds per run (default 600)")
p.add_argument("--define", action="append", default=[], help="extra C# symbol (repeatable)")
p.add_argument("--bridge", action="store_true", help="compile with the whole runtime (XLua.Mini sources) and test both APIs together")
p.add_argument("tests", nargs="*", help="run only these Test* methods")
a = p.parse_args()

out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
dotnet = a.dotnet
if dotnet is None:
    try:
        dotnet = subprocess.run(["mise", "which", "dotnet"], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True,
                                check=True, cwd=repo).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        dotnet = shutil.which("dotnet")
if not dotnet:
    sys.exit("no dotnet: install the .NET SDK of mise.toml or pass --dotnet")
sdk = Path(dotnet).resolve().parent
cscs = sorted(sdk.glob("sdk/*/Roslyn/bincore/csc.dll"))
if not cscs:
    sys.exit("no Roslyn csc.dll in " + str(sdk))
core = sorted(f for f in (repo / "Assets/XLua/Core").rglob("*.cs") if "Editor" not in f.relative_to(repo / "Assets/XLua/Core").parts)
if a.bridge:
    import xml.etree.ElementTree as ET
    project = repo / "General/vs2013/XLua.Mini.csproj"
    core = [(project.parent / x.attrib["Include"].replace("\\", "/")).resolve() for x in ET.parse(project).iter()
            if x.tag.endswith("}Compile")]
tests = sorted((repo / "Test/Core").glob("*.cs"))
defines = ["XLUA_GENERAL", "XLUA_LUASCRIPT"] + (["XLUA_CORE_TEST_BRIDGE"] if a.bridge else []) + a.define
expected = EXPECTED_TESTS + (BRIDGE_TESTS if a.bridge else 0)


def csc(name, refs, extra_defines=()):
    target = out / name
    target.mkdir(exist_ok=True)
    exe = target / "CoreTests.exe" if name == "mono" else target / "CoreTests.dll"
    rsp = target / "csc.rsp"
    lines = ["-nologo", "-nostdlib", "-unsafe", "-langversion:9", "-optimize", "-target:exe",
             "-nowarn:1591,1573,1574", "-define:" + ";".join(defines + list(extra_defines)), "-out:" + str(exe)] \
        + ["-r:" + str(r) for r in refs] + [str(f) for f in core + tests]
    rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines) + "\n")
    r = subprocess.run([dotnet, str(cscs[-1]), "-noconfig", "@" + str(rsp)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    (target / "csc.log").write_text(r.stdout)
    if r.returncode != 0:
        print(r.stdout)
        sys.exit("Test/Core does not compile for " + name)
    shutil.copy2(a.plugin, target / a.plugin.name)
    return exe


def run(name, cmd, cwd):
    # The luban fixture (LubanFixtureTests.cs) reads Test/Core/Fixtures.
    env = dict(os.environ, XLUA_CORE_FIXTURES=str(repo / "Test/Core/Fixtures"))
    for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
        env[key] = str(cwd) + os.pathsep + env.get(key, "")
    proc = subprocess.Popen(cmd + a.tests, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                            errors="backslashreplace", start_new_session=True)
    try:
        output, _ = proc.communicate(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        output, _ = proc.communicate()
        (out / (name + ".log")).write_text(output)
        done = re.findall(r"^(?:ok|FAIL) +(Test\w+)$", output, re.M)
        sys.exit("%s: TIMEOUT after %d s; %d tests finished, the last: %s (the next one hung)"
                 % (name, a.timeout, len(done), done[-1] if done else "none"))
    (out / (name + ".log")).write_text(output)
    print(output)
    passed = re.search(r"XLUA_CORE_TESTS_PASS: (\d+) tests", output)
    if proc.returncode != 0 or not passed:
        sys.exit(name + ": the LuaScript tests failed; see " + str(out / (name + ".log")))
    if not a.tests and int(passed.group(1)) != expected:
        sys.exit("%s: %s tests ran, expected %d" % (name, passed.group(1), expected))
    print("XLUA_CORE_%s_PASS: %s tests" % (name.upper(), passed.group(1)))


if a.runtime in ("mono", "both"):
    api = Path(shutil.which(a.mono)).resolve().parents[1] / "lib/mono/4.5"
    refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll"]
            + (["System.Xml.dll"] if a.bridge else [])]
    exe = csc("mono", refs)
    run("mono", [a.mono, "--debug", str(exe)], exe.parent)
if a.runtime in ("dotnet", "both"):
    packs = sorted((sdk / "packs/Microsoft.NETCore.App.Ref").glob("*/ref/net*"))
    if not packs:
        sys.exit("no Microsoft.NETCore.App.Ref pack in " + str(sdk))
    version = packs[-1].parents[1].name
    # The symbols the SDK defines for its target framework (NET5_0_OR_GREATER ...), which CodeEmit.cs reads.
    major = int(packs[-1].name[3:].split(".")[0])
    dll = csc("dotnet", sorted(packs[-1].glob("*.dll")),
              ["NETCOREAPP", "NET"] + ["NET%d_0_OR_GREATER" % v for v in range(5, major + 1)])
    framework = sorted((sdk / "shared/Microsoft.NETCore.App").iterdir())[-1].name
    (dll.parent / "CoreTests.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": packs[-1].name, "framework": {"name": "Microsoft.NETCore.App", "version": framework}}}, indent=2))
    run("dotnet", [dotnet, str(dll)], dll.parent)
    print("(.NET reference pack %s, runtime %s)" % (version, framework))
