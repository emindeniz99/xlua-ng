"""Compile and run the existing general C# suite against a host-native plugin.

The first run binds everything through reflection and CodeEmit (mcs, no
generated code). Generated bindings come from the xLua source generator only;
the legacy generator and its --gen-code, --gen-code-minimize and --gen-assembly
passes were removed in source generator milestone M11.

--source-gen compiles the test assembly with the source generator and runs the
suite through its wrappers and bridges. The generated code is in the test
assembly, next to XLua.Mini.dll, the layout of xLua in its own asmdef. The
suite's configuration lists reach the generator through the migration tool
(XLuaGenerate --migrate --group), whose output must equal the checked-in
Test/Native/XLuaUnitTest.XLuaConfig.cs; --group makes the lists of several types
(Coverage.MultiTypeConfig) attributes of several types. The generator's
manifest (binding surface, bridge sets, GCOptimize types) must equal
Test/Native/expected-surface/suite.json (surface_diff.py), the surface the
legacy generator bound for the same configuration when it was removed;
--update-surface writes the manifest there instead, after a fixture change.
--source-gen-minimize also runs the generator's GEN_CODE_MINIMIZE mode: xLua and
the test assembly compiled with the define, the suite through the GCM wrappers,
and the same surface check.
--source-gen-fallback runs the --source-gen build again with
XLUA_SG_SKIP_REGISTER, so that nothing registers, as when the generator did not
run: the suite must pass through reflection and CodeEmit.
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
for removed in ["--gen-code", "--gen-code-minimize", "--gen-assembly"]:
    p.add_argument(removed, action="store_true", help=argparse.SUPPRESS)
p.add_argument("--source-gen", action="store_true",
               help="also build the source generator (Tools/build_source_generator.py), compile the test assembly "
                    "with it using the .NET SDK's csc, and run the suite through Test/Native/SourceGenLauncher.cs")
p.add_argument("--source-gen-minimize", action="store_true",
               help="also run the source generator's GEN_CODE_MINIMIZE mode (xLua and the test assembly with the define)")
p.add_argument("--source-gen-fallback", action="store_true",
               help="also run the --source-gen build with XLUA_SG_SKIP_REGISTER: no generated unit registers")
p.add_argument("--update-surface", action="store_true",
               help="write the source generator's manifest to Test/Native/expected-surface/suite.json instead of "
                    "comparing it (after a change to the suite's configured types)")
p.add_argument("--mono-api", type=Path,
               help="the Mono assemblies --source-gen compiles against (default: lib/mono/4.5 next to the mono executable)")
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
if a.gen_code or a.gen_code_minimize or a.gen_assembly:
    p.error("--gen-code, --gen-code-minimize and --gen-assembly were removed with the legacy generator (source generator "
            "milestone M11); use --source-gen, --source-gen-minimize and --source-gen-fallback")
if a.mono_version != "any":
    banner = subprocess.run([a.mono, "--version"], stdout=subprocess.PIPE, text=True, check=True).stdout
    found = re.search(r"version (\S+)", banner)
    if not found or found.group(1) != a.mono_version:
        raise SystemExit("Expected Mono " + a.mono_version + ", found: " + banner.splitlines()[0])
repo = Path(__file__).resolve().parents[2]
# Lua test cases in Test/UnitTest/StreamingAssets; each mode must run and pass all of them.
EXPECTED_TESTS = 477
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
# A preset define (XLUA_PRESET_MINIMAL) expects a native library built with that preset's
# defaults (Tools/presets.py); XLUA_EXPECT_* variables already set win.
sys.path.insert(0, str(repo / "Tools"))
import presets
for preset in presets.NATIVE:
    if presets.define(preset) in a.define:
        for key, value in presets.expect_env(preset).items():
            env.setdefault(key, value)
        print("Preset:", preset)
if a.native_modules is not None:
    env["XLUA_EXPECT_NATIVE_MODULES"] = a.native_modules
print("Expected native modules:", env.get("XLUA_EXPECT_NATIVE_MODULES", "socket,mime") or "none")
# CaseUint64TableFollowsTheBuild and CaseStdLibrariesFollowTheBuild read these (CMake XLUA_UINT64, XLUA_LIBS).
print("Expected uint64 table:", env.get("XLUA_EXPECT_UINT64", "ON"), "- standard libraries:", env.get("XLUA_EXPECT_LIBS", "all"))
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

def build_tools(defines, suffix):
    """XLuaGenerate and the other tools (Tools/build_tools.py), with the native plugin next to them;
    returns their folder and the environment to run XLuaGenerate in."""
    tools = out / ("tools" + suffix)
    subprocess.run([sys.executable, str(repo / "Tools/build_tools.py"), "--output", str(tools),
                    "--compiler", a.compiler] + ["--define=" + d for d in defines]
                   + (["--debug"] if a.coverage else []), check=True)
    # XLuaGenerate starts a LuaEnv, so it needs the native plugin next to it.
    shutil.copy2(a.plugin, tools / a.plugin.name)
    gen_env = dict(env)
    for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
        gen_env[key] = str(tools) + os.pathsep + gen_env.get(key, "")
    return tools, gen_env


MIGRATED_CONFIG = repo / "Test/Native/XLuaUnitTest.XLuaConfig.cs"
# The tools of this run, built by the first migrated_config.
built_tools = []


def migrated_config(sg):
    """The suite's configuration lists converted by the migration tool (XLuaGenerate --migrate, milestone M9).

    The fresh output must equal the checked-in Test/Native/XLuaUnitTest.XLuaConfig.cs, so that the converted test
    configuration in the repository stays the tool's output; the source generator build compiles it. With --group,
    so that the build reads the attributes with several types ([assembly: XLua.LuaCallCSharp(typeof(A), typeof(B))])
    for the lists of several types; migration_check.py checks the default, one type per attribute."""
    if not built_tools:
        built_tools.append(build_tools(a.define, ""))
    tools, gen_env = built_tools[0]
    migrated = sg / "migrated"
    shutil.rmtree(migrated, ignore_errors=True)
    result = subprocess.run(mono("xluagenerate-migrate", [tools / "XLua.Mini.dll", tools / "XLuaGenerate.exe"])
                            + [str(tools / "XLuaGenerate.exe"), "--migrate", "--group", "--output", str(migrated), "--bindings-host",
                               "XLuaUnitTest", "--source-root", str(repo / "Test/UnitTest"), str(out / "XLuaUnitTest.exe")],
                            cwd=tools, env=gen_env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    print(result.stdout)
    if result.returncode != 0:
        raise SystemExit("XLuaGenerate --migrate failed; see " + str(migrated / "XLuaMigrationReport.md"))
    fresh = migrated / "XLuaUnitTest.XLuaConfig.cs"
    if not fresh.is_file() or fresh.read_text(encoding="utf-8") != MIGRATED_CONFIG.read_text(encoding="utf-8"):
        raise SystemExit("the migration tool's output for the suite differs from " + str(MIGRATED_CONFIG.relative_to(repo))
                         + "; if the configuration lists changed, copy " + str(fresh) + " over it")
    text = fresh.read_text(encoding="utf-8")
    for kind in ["LuaCallCSharp", "CSharpCallLua", "GCOptimize", "ReflectionUse"]:
        if not re.search(r"\[assembly: XLua\." + kind + r"\(\n    typeof\([^)]*\),\n    typeof", text):
            raise SystemExit("the migrated suite configuration has no [" + kind + "] attribute with several types")
    print("XLUA_MIGRATED_CONFIG_PASS: the migration tool reproduces " + str(MIGRATED_CONFIG.relative_to(repo)))
    return fresh


EXPECTED_SURFACE = repo / "Test/Native/expected-surface/suite.json"


def source_gen_pass(defines=(), suffix="", surface=True):
    """XLuaUnitTest compiled with the source generator (M1: registration and names; M2: class and enum wrappers;
    M3: bridges; M4: GCOptimize; M5: per-member fallback and link.xml).

    Mono's csc (Roslyn 3.9) cannot load a generator built against Roslyn 4.3.1, so
    the test assembly is compiled with the .NET SDK's csc against the Mono
    assemblies that mcs compiles against, and run on Mono as in the other modes.
    The fixtures written for mcs are rewritten for Roslyn (roslyn_fixtures.py).
    surface: compare the manifest with EXPECTED_SURFACE (the plain and the GCM build bind the same surface)."""
    sys.path.insert(0, str(repo / "Tools"))
    # python3 -I leaves the script's folder out of sys.path.
    sys.path.insert(0, str(repo / "Test/Native"))
    import build_source_generator
    from roslyn_fixtures import roslyn_sources
    sg = out / ("source-gen" + suffix)
    shutil.rmtree(sg, ignore_errors=True)
    sg.mkdir()
    analyzer = build_source_generator.build(out / "source-gen-build")
    dotnet = build_source_generator.dotnet_path()
    cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
    api = a.mono_api or Path(shutil.which(a.mono)).resolve().parents[1] / "lib/mono/4.5"
    if not cscs or not (api / "mscorlib.dll").is_file():
        raise SystemExit("--source-gen needs the .NET SDK's csc.dll and Mono's lib/mono/4.5 (--mono-api)")
    refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll"]]
    config = migrated_config(sg)
    # xLua compiled with the pass's defines: GEN_CODE_MINIMIZE changes its registration API (CSharpWrapper).
    mini = sg / "XLua.Mini.dll"
    if "GEN_CODE_MINIMIZE" in defines:
        subprocess.run(compiler + ["-target:library", "-unsafe", "-define:XLUA_GENERAL"] + extra_defines
                       + ["-define:" + d for d in defines] + debug + ["-out:" + str(mini)] + sources("XLua.Mini"), check=True)
    else:
        shutil.copy2(out / "XLua.Mini.dll", mini)
        if (out / "XLua.Mini.dll.mdb").is_file():
            shutil.copy2(out / "XLua.Mini.dll.mdb", sg / "XLua.Mini.dll.mdb")
    tests = sg / "XLuaUnitTest.exe"
    generated = sg / "generated"
    rsp = sg / "XLuaUnitTest.rsp"
    # C# 12 for the ref readonly fixtures; the generator unit tests check its output at C# 9.
    lines = ["-nologo", "-noconfig", "-nostdlib", "-unsafe", "-langversion:12", "-deterministic",
             "-define:" + ";".join(["XLUA_GENERAL"] + a.define + list(defines)), "-analyzer:" + str(analyzer),
             "-generatedfilesout:" + str(generated), "-out:" + str(tests), "-r:" + str(mini)] \
        + ["-r:" + str(r) for r in refs] + (["-debug:portable"] if a.coverage else []) \
        + [str(f) for f in roslyn_sources(sources("XLuaUnitTest"), repo, sg / "roslyn-fixtures")] \
        + [str(config), str(repo / "Test/Native/SourceGenFallback.cs")]
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
    # The suite's configuration must give no generator warning either (XLuaUnitTest.XLuaConfig.cs).
    if re.search(r"warning (CS8032|CS8784|CS8785|CS9057)|(error|warning) XLSG", result.stdout):
        raise SystemExit("analyzer load problem, or a generator error or warning, in " + str(sg / "csc.log"))
    # The binding surface, the bridge sets and the GCOptimize types must equal the expected surface: the one the
    # legacy generator bound for this configuration when it was removed (milestone M11), or what --update-surface
    # wrote since.
    if surface:
        manifest = next(generated.rglob("XLua.Manifest.g.cs"))
        if a.update_surface:
            subprocess.run([sys.executable, str(repo / "Test/Native/surface_diff.py"), "--write", str(EXPECTED_SURFACE),
                            str(manifest)], check=True)
        # SourceGenFallback.cs is compiled only here (mcs has no init accessors), so the frozen legacy surface
        # lacks it.
        allow = sg / "surface-allow.json"
        # IInKeyIndexer's key is by value for mcs and `in` for Roslyn (roslyn_fixtures.py).
        allow.write_text(json.dumps([{"type": "SourceGenFallback", "field": "type", "value": "SourceGenFallback",
                                      "reason": "fixture of the per-member fallback, compiled only with the source generator"}]
                                    + [{"type": "IInKeyIndexer", "field": "interfaceBridges",
                                        "value": "indexers IInKeyIndexer::this[System.Int32%s] get set" % ref,
                                        "reason": "the key is `in` only where Roslyn compiles the fixture"}
                                       for ref in ["", "&"]]))
        diff = subprocess.run([sys.executable, str(repo / "Test/Native/surface_diff.py"), str(EXPECTED_SURFACE), str(manifest),
                               "--allow", str(allow)],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        diff_log = out / ("surface-diff%s.log" % suffix)
        diff_log.write_text(diff.stdout)
        print(diff.stdout)
        if diff.returncode != 0:
            raise SystemExit("the binding surface or the bridge sets differ from " + str(EXPECTED_SURFACE.relative_to(repo))
                             + "; see " + str(diff_log) + " (--update-surface writes the manifest there after a fixture change)")
    launcher = sg / "SourceGenLauncher.exe"
    subprocess.run(compiler + ["-unsafe", "-define:XLUA_GENERAL"] + extra_defines + ["-define:" + d for d in defines] + debug
                   + ["-r:" + str(sg / "XLua.Mini.dll"), "-r:" + str(tests), "-r:System.Xml.dll", "-out:" + str(launcher),
                      str(repo / "Test/Native/SourceGenLauncher.cs")], check=True)
    log = "managed-suite-source-gen" + suffix + ".log"
    run_suite(launcher, log, [sg / "XLua.Mini.dll", tests, launcher])
    if "XLUA_SOURCE_GEN_CHECKS_PASS" not in (out / log).read_text():
        raise SystemExit(launcher.name + ": the source generator checks did not run")
    print("XLUA_MANAGED_SOURCE_GEN_PASS%s: %d tests in each of two modes with %d source-generated wrappers"
          % (suffix.upper().replace("-", "_"), EXPECTED_TESTS, len(list(generated.rglob("W_*.g.cs")))))


if a.source_gen:
    source_gen_pass()
if a.source_gen_minimize:
    source_gen_pass(["GEN_CODE_MINIMIZE"], "-minimize")
if a.source_gen_fallback:
    source_gen_pass(["XLUA_SG_SKIP_REGISTER"], "-fallback", surface=False)
