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
p.add_argument("--source-gen", action="store_true",
               help="also build the source generator (Tools/build_source_generator.py), compile the test assembly "
                    "with it using the .NET SDK's csc, and run the suite through Test/Native/SourceGenLauncher.cs")
p.add_argument("--mono-api", type=Path,
               help="the Mono assemblies --source-gen compiles against (default: lib/mono/4.5 next to the mono executable)")
p.add_argument("--define", action="append", default=[],
               help="extra C# symbol for xLua, the tools and the test assemblies, "
                    "e.g. XLUA_LOCK_METATABLES (repeatable)")
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
EXPECTED_TESTS = 395
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


def source_gen_pass():
    """XLuaUnitTest compiled with the source generator (milestone M1: registration and names, no wrappers).

    Mono's csc (Roslyn 3.9) cannot load a generator built against Roslyn 4.3.1, so
    the test assembly is compiled with the .NET SDK's csc against the Mono
    assemblies that mcs compiles against, and run on Mono as in the other modes.
    The fixtures written for mcs are rewritten for Roslyn (roslyn_fixtures.py)."""
    sys.path.insert(0, str(repo / "Tools"))
    import build_source_generator
    from roslyn_fixtures import roslyn_sources
    sg = out / "source-gen"
    shutil.rmtree(sg, ignore_errors=True)
    sg.mkdir()
    analyzer = build_source_generator.build(out / "source-gen-build")
    dotnet = build_source_generator.dotnet_path()
    cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
    api = a.mono_api or Path(shutil.which(a.mono)).resolve().parents[1] / "lib/mono/4.5"
    if not cscs or not (api / "mscorlib.dll").is_file():
        raise SystemExit("--source-gen needs the .NET SDK's csc.dll and Mono's lib/mono/4.5 (--mono-api)")
    refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll"]]
    tests = sg / "XLuaUnitTest.exe"
    generated = sg / "generated"
    rsp = sg / "XLuaUnitTest.rsp"
    # C# 12 for the ref readonly fixtures; the generator unit tests check its output at C# 9.
    lines = ["-nologo", "-noconfig", "-nostdlib", "-unsafe", "-langversion:12", "-deterministic",
             "-define:" + ";".join(["XLUA_GENERAL"] + a.define), "-analyzer:" + str(analyzer),
             "-generatedfilesout:" + str(generated), "-out:" + str(tests), "-r:" + str(out / "XLua.Mini.dll")] \
        + ["-r:" + str(r) for r in refs] + (["-debug:portable"] if a.coverage else []) \
        + [str(f) for f in roslyn_sources(sources("XLuaUnitTest"), repo, sg / "roslyn-fixtures")] \
        + [str(repo / "Test/Native/SourceGenConfig.cs")]
    generated.mkdir()
    rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines) + "\n")
    result = subprocess.run([dotnet, str(cscs[-1]), "@" + str(rsp)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True)
    (sg / "csc.log").write_text(result.stdout)
    print(result.stdout)
    if result.returncode != 0:
        raise SystemExit("XLuaUnitTest does not compile with the source generator; see " + str(sg / "csc.log"))
    if not list(generated.rglob("XLua.Bindings.g.cs")):
        raise SystemExit("the source generator wrote no XLua.Bindings.g.cs (did it load? look for CS8032/CS9057 in csc.log)")
    if re.search(r"warning (CS8032|CS8784|CS8785|CS9057)|XLSG\d+: error|error XLSG", result.stdout):
        raise SystemExit("analyzer load problem or generator error in csc.log")
    shutil.copy2(out / "XLua.Mini.dll", sg / "XLua.Mini.dll")
    launcher = sg / "SourceGenLauncher.exe"
    subprocess.run(compiler + ["-unsafe", "-define:XLUA_GENERAL"] + extra_defines + debug
                   + ["-r:" + str(sg / "XLua.Mini.dll"), "-r:" + str(tests), "-out:" + str(launcher),
                      str(repo / "Test/Native/SourceGenLauncher.cs")], check=True)
    log = "managed-suite-source-gen.log"
    run_suite(launcher, log, [sg / "XLua.Mini.dll", tests, launcher])
    if "XLUA_SOURCE_GEN_CHECKS_PASS" not in (out / log).read_text():
        raise SystemExit(launcher.name + ": the source generator checks did not run")
    print("XLUA_MANAGED_SOURCE_GEN_PASS: %d tests in each of two modes, registered by the source generator, "
          "no wrappers" % EXPECTED_TESTS)


if a.source_gen:
    source_gen_pass()
