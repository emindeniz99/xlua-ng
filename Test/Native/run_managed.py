"""Compile and run the existing general C# suite against a host-native plugin.

With --gen-code the suite also runs a second time with generated wrappers
(XLuaUnitTestGenCode), which exercises the code paths that generated code uses.
--gen-code-minimize adds a run with GEN_CODE_MINIMIZE, the smaller generated
code that calls the native struct and C# function wrapper helpers.
--gen-assembly also compiles each pass's generated code into an assembly of its
own, as with XLua in an asmdef, and runs the suite through GenAssemblyLauncher.cs.
--coverage compiles everything with -debug and runs each Mono process under
Mono's coverage profiler; Test/Native/coverage_cs.py turns the result into a report.
"""
import argparse
import json
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
p.add_argument("--gen-code-minimize", action="store_true",
               help="also run the generated-code suite built with GEN_CODE_MINIMIZE")
p.add_argument("--gen-assembly", action="store_true",
               help="also compile the generated wrappers of each generated-code pass into their own "
                    "assembly (XLuaGen.dll) and run the suite through Test/Native/GenAssemblyLauncher.cs")
p.add_argument("--define", action="append", default=[],
               help="extra C# symbol for xLua, the tools and the test assemblies, "
                    "e.g. XLUA_LOCK_METATABLES (repeatable)")
p.add_argument("--native-modules", default=None,
               help="C modules the plugin was built with, for CaseNativeModulesFollowTheBuild: "
                    "'socket,mime' (default), 'socket' (-DXLUA_MIME=OFF) or '' (-DXLUA_LUASOCKET=OFF); "
                    "sets XLUA_EXPECT_NATIVE_MODULES, which is also read from the environment")
p.add_argument("--coverage", action="store_true",
               help="compile with -debug and run every Mono process under Mono's coverage "
                    "profiler; writes coverage/*.xml and coverage/runs.json (see coverage_cs.py)")
a = p.parse_args()
if a.mono_version != "any":
    banner = subprocess.run([a.mono, "--version"], stdout=subprocess.PIPE, text=True, check=True).stdout
    found = re.search(r"version (\S+)", banner)
    if not found or found.group(1) != a.mono_version:
        raise SystemExit("Expected Mono " + a.mono_version + ", found: " + banner.splitlines()[0])
repo = Path(__file__).resolve().parents[2]
# Lua test cases in Test/UnitTest/StreamingAssets; each mode must run and pass all of them.
EXPECTED_TESTS = 396
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
def sources(name):
    project = repo / "General/vs2013" / (name + ".csproj")
    tree = ET.parse(project)
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in tree.iter() if x.tag.endswith("}Compile")]
compiler = [a.mono, a.compiler] if a.compiler.endswith(".exe") else [a.compiler]
extra_defines = ["-define:" + d for d in a.define]
# -debug writes the .mdb that maps the profiler's IL offsets to source lines.
debug = ["-debug"] if a.coverage else []
coverage_dir = out / "coverage"
coverage_runs = []
if a.coverage:
    shutil.rmtree(coverage_dir, ignore_errors=True)
    coverage_dir.mkdir()


def mono(name, assemblies):
    """The Mono command for one process; with --coverage it also records a profile."""
    if not a.coverage:
        return [a.mono]
    xml = coverage_dir / (name + ".xml")
    coverage_runs.append({"name": name, "xml": str(xml), "assemblies": [str(x) for x in assemblies]})
    (coverage_dir / "runs.json").write_text(json.dumps(coverage_runs, indent=1) + "\n")
    return [a.mono, "--debug", "--profile=coverage:output=" + str(xml)]


subprocess.run(compiler + ["-target:library", "-unsafe", "-define:XLUA_GENERAL"] + extra_defines + debug + [
    "-out:" + str(out / "XLua.Mini.dll")] + sources("XLua.Mini"), check=True)
subprocess.run(compiler + ["-unsafe", "-define:XLUA_GENERAL"] + extra_defines + debug + [
    "-r:" + str(out / "XLua.Mini.dll"), "-out:" + str(out / "XLuaUnitTest.exe")]
    + sources("XLuaUnitTest"), check=True)
shutil.copy2(a.plugin, out / a.plugin.name)
# Preserve the relative layout expected by LuaTestCommon without writing to the checkout.
shutil.copytree(repo / "Test/UnitTest", out / "Test/UnitTest", dirs_exist_ok=True)
# xlua.util and perf.profiler are embedded in the xLua assembly; no Lua files are copied for them.
(out / "General").mkdir(exist_ok=True)
env = dict(os.environ)
if a.native_modules is not None:
    env["XLUA_EXPECT_NATIVE_MODULES"] = a.native_modules
print("Expected native modules:", env.get("XLUA_EXPECT_NATIVE_MODULES", "socket,mime") or "none")
for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
    env[key] = str(out) + os.pathsep + env.get(key, "")


def run_suite(exe, log_name, assemblies):
    # Lua's print writes the raw bytes of strings, which need not be UTF-8.
    result = subprocess.run(mono(Path(log_name).stem, assemblies) + [str(exe)], cwd=out / "General",
        env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="backslashreplace")
    (out / log_name).write_text(result.stdout)
    print(result.stdout)
    result.check_returncode()
    # luaunit_compat.lua prints "[luaunit <mode>] Ran N tests in T seconds, S successes, ..." per mode.
    counts = {mode: (ran, ok) for mode, ran, ok in re.findall(
        r"\[luaunit (normal|coroutine)\] Ran (\d+) tests in \S+ seconds, (\d+) success", result.stdout)}
    expected = (str(EXPECTED_TESTS), str(EXPECTED_TESTS))
    if counts != {"coroutine": expected, "normal": expected}:
        raise SystemExit(exe.name + ": expected %d passing tests in both normal and coroutine modes: %r" % (EXPECTED_TESTS, counts))


run_suite(out / "XLuaUnitTest.exe", "managed-suite.log", [out / "XLua.Mini.dll", out / "XLuaUnitTest.exe"])
print("XLUA_MANAGED_SUITE_PASS: %d tests in each of two modes" % EXPECTED_TESTS)

def gen_code_pass(defines, suffix):
    """Generate wrappers with XLuaGenerate, build XLuaUnitTestGenCode and run it."""
    defines = defines + a.define
    tools = out / ("tools" + suffix)
    subprocess.run([sys.executable, str(repo / "Tools/build_tools.py"), "--output", str(tools),
                    "--compiler", a.compiler] + ["--define=" + d for d in defines]
                   + (["--debug"] if a.coverage else []), check=True)
    gen = out / ("Gen" + suffix)
    shutil.rmtree(gen, ignore_errors=True)
    # XLuaGenerate starts a LuaEnv, so it needs the native plugin next to it.
    shutil.copy2(a.plugin, tools / a.plugin.name)
    gen_env = dict(env)
    for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
        gen_env[key] = str(tools) + os.pathsep + gen_env.get(key, "")
    generate = mono("xluagenerate" + suffix, [tools / "XLua.Mini.dll", tools / "XLuaGenerate.exe"])
    subprocess.run(generate + [str(tools / "XLuaGenerate.exe"), str(out / "XLuaUnitTest.exe"), str(gen) + "/"],
                   cwd=tools, env=gen_env, check=True)
    generated = sorted(str(f) for f in gen.glob("*.cs"))
    assert generated, "XLuaGenerate wrote no wrappers"
    # The project lists a checked-in Gen2 snapshot; use the freshly generated files instead.
    project_sources = [f for f in sources("XLuaUnitTestGenCode") if "/General/Gen2/" not in f]
    exe = out / ("XLuaUnitTestGenCode" + suffix + ".exe")
    subprocess.run(compiler + ["-unsafe", "-define:XLUA_GENERAL"] + ["-define:" + d for d in defines] + debug
        + ["-out:" + str(exe)] + project_sources + generated, check=True)
    run_suite(exe, "managed-suite-gencode" + suffix + ".log", [exe])
    if a.gen_assembly:
        gen_assembly_pass(defines, suffix, generated)
    return len(generated)


def gen_assembly_pass(defines, suffix, generated):
    """The generated code in its own assembly: it may use only xLua's public API (XLua.Binding)."""
    sep = out / ("gen-assembly" + suffix)
    shutil.rmtree(sep, ignore_errors=True)
    sep.mkdir()
    flags = ["-unsafe", "-define:XLUA_GENERAL"] + ["-define:" + d for d in defines] + debug
    mini, tests, gen = sep / "XLua.Mini.dll", sep / "XLuaUnitTest.exe", sep / "XLuaGen.dll"
    subprocess.run(compiler + ["-target:library"] + flags + ["-out:" + str(mini)] + sources("XLua.Mini"), check=True)
    subprocess.run(compiler + flags + ["-r:" + str(mini), "-out:" + str(tests)] + sources("XLuaUnitTest"), check=True)
    subprocess.run(compiler + ["-target:library"] + flags + ["-r:" + str(mini), "-r:" + str(tests), "-out:" + str(gen)]
                   + generated, check=True)
    launcher = sep / "GenAssemblyLauncher.exe"
    subprocess.run(compiler + flags + ["-r:" + str(mini), "-r:" + str(tests), "-r:" + str(gen), "-out:" + str(launcher),
                   str(repo / "Test/Native/GenAssemblyLauncher.cs")], check=True)
    log = "managed-suite-gen-assembly" + suffix + ".log"
    run_suite(launcher, log, [mini, tests, gen, launcher])
    if "XLUA_GEN_ASSEMBLY_CHECKS_PASS" not in (out / log).read_text():
        raise SystemExit(launcher.name + ": the generated-assembly checks did not run")
    print("XLUA_MANAGED_GEN_ASSEMBLY_PASS: %d tests in each of two modes%s" % (EXPECTED_TESTS, suffix and " (" + suffix[1:] + ")"))


if a.gen_code:
    count = gen_code_pass([], "")
    print("XLUA_MANAGED_GENCODE_PASS: %d tests in each of two modes with" % EXPECTED_TESTS, count, "generated files")
if a.gen_code_minimize:
    count = gen_code_pass(["GEN_CODE_MINIMIZE"], "-minimize")
    print("XLUA_MANAGED_GENCODE_MINIMIZE_PASS: %d tests in each of two modes with" % EXPECTED_TESTS, count, "generated files")
