"""Run the xLua editor tests in a project made by prepare.py --editor-tools.

`run` launches the editor once in batch mode with -runTests, the EditMode
tests, in the mode prepare.py wrote into XluaEditorTestMode.txt:

- "sourcegen" (prepare.py --source-gen): the xLua source generator is
  installed (Assets/XLua/Src/Analyzers in the asmdef layout,
  Assets/XLua/Analyzers with --no-asmdef, the package's Runtime/Analyzers with
  --package) and binds the suite's types at compile time, from the
  configuration attributes in XluaEditorTestTarget.cs and the fixture in
  Test/UnityMatrix/SourceGen; XluaSourceGenTests must pass too (results-sourcegen.xml);
- "reflection" (no --source-gen): no generated bindings; the Editor emits type
  wrappers and [CSharpCallLua] bridges at run time (CodeEmit), and LuaCATS
  declares only xLua's own API (results-reflection.xml).

Until milestone M11 removed the legacy generator, `run` had four launches:
Generate Code, the tests in mode "generated", Clear Generated Code and the
tests in mode "cleared" (what "reflection" checks now).

In mode "sourcegen" lua-language-server then checks
Test/UnityMatrix/luacats/good.lua and bad.lua against the LuaCATS files that
the tests wrote to <project>/LuaTypes (LuaLS runs here, outside Unity), and the
editor log must not contain the analyzer load warnings CS8032, CS8784, CS8785
and CS9057, any XLSG error or an XLSG011 (a configuration list the generator
does not read: the suite has none), and must contain the deliberate XLSG003
warning of the fixture with its file and line. In mode "reflection" the log
must contain no XLSG diagnostic at all.

A project made with prepare.py --fast-enter-play-mode also has to pass the
XluaPlayModeTests, which enter and exit play mode from EditMode tests.

A project made with prepare.py --csharp12 (-langversion:12, Unity 6000.6 or
later) also has to pass the XluaRefReadonlyTests for C# 12 `ref readonly`
parameters. One made with prepare.py --roslyn-sdk has a precompiled DLL with
`ref readonly` members that Roslyn before Unity 6000.6 cannot use: it has to
pass the XluaRefReadonlyLibTests (and, with the generator,
XluaSourceGenRefReadonlyTests). Every run fails on Roslyn's ref-kind mismatch
warnings (CS9191, CS9192, CS9195, CS9196, CS9198) in the test log.

`--build-target iOS` or `--build-target WebGL` passes -buildTarget to the
launch, so the suite runs with an IL2CPP target active. `run` writes the
backend it expects (IL2CPP, or Mono2x for the default standalone target) into
XluaEditorTestBackend.txt, and XluaGeneratedCodeTests.EditorEmitsWithEveryBuildTarget
checks it, that ENABLE_IL2CPP is not defined in the editor (Unity compiles
editor scripts for Mono with every target) and, without the generator, that
the type wrapper and the delegate bridge were emitted. The target's player
module must be installed.

`luals` runs only that check and `verify` only checks a result file; the
Unity editor matrix workflow uses them after its GameCI step.
"""
import argparse
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here.parent / "Native"))
import luals as luals_tools  # noqa: E402

MODES = ["sourcegen", "reflection"]

# Tests that must pass in every run; the generated-code tests check the mode.
REQUIRED = [
    "XluaSmokeTests.Lua55BridgeRunsInTheEditor",
    "XluaSmokeTests.StreamingAssetsSearcherFollowsTheDefine",
    "XluaSmokeTests.FeatureDefinesInTheEditor",
    "XluaEditorToolsTests.LuaCATSDefinitionsAreGenerated",
    "XluaEditorToolsTests.LuaCATSDescribesUnityAndTestTypes",
    "XluaEditorToolsTests.LuaCATSTreatsInParametersAsArguments",
    "XluaEditorToolsTests.LuarcMatchesThePlugin",
    "XluaEditorToolsTests.ClearAndGenerateKeepsUserFiles",
    "XluaEditorToolsTests.ValidateSetupAcceptsThePlugin",
    "XluaEditorToolsTests.ValidateSetupReportsOldModulesInResources",
    "XluaEditorToolsTests.HotfixToolsAreCheckedAgainstTheManifest",
    "XluaGeneratedCodeTests.GeneratedCodeMatchesMode",
    "XluaGeneratedCodeTests.GeneratedWrappersFollowGenCodeMinimize",
    "XluaGeneratedCodeTests.CodeEmitIsCompiledIntoTheEditor",
    "XluaGeneratedCodeTests.EditorEmitsWithEveryBuildTarget",
    "XluaGeneratedCodeTests.LuaUsesVector3",
    "XluaGeneratedCodeTests.LuaCreatesAndDestroysGameObjects",
    "XluaGeneratedCodeTests.LuaCallsOverloadsRefOutStructsAndEnums",
    "XluaGeneratedCodeTests.LuaIntegerArgumentsFollowLuasRule",
    "XluaGeneratedCodeTests.LuaFunctionsBecomeCSharpCallLuaDelegates",
    "XluaGeneratedCodeTests.LuaPassesInParametersByValue",
    "XluaGeneratedCodeTests.LuaUsesIndexersWithInKeys",
    "XluaGeneratedCodeTests.LuaUsesCamera",
    "XluaGeneratedCodeTests.LuaCallsBclMethodsWithSpanOverloads",
    "XluaStringTests.PrintAcceptsInvalidUtf8",
    "XluaStringTests.LuaStringsReadAsUtf8",
    "XluaStringTests.LuaErrorWithInvalidUtf8BecomesLuaException",
    "XluaStringTests.TurkishTextRoundTrips",
    "XluaStringTests.LuaReachesNonAsciiMemberNames",
]

# Also required in a project made by prepare.py --csharp12, which has this fixture.
REF_READONLY_FIXTURE = "Assets/XluaRefReadonlyTarget.cs"
REQUIRED_REF_READONLY = [
    "XluaRefReadonlyTests.RefReadonlyParametersAreInParameters",
    "XluaRefReadonlyTests.LuaPassesRefReadonlyParametersByValue",
    "XluaRefReadonlyTests.LuaUsesIndexersWithRefReadonlyKeys",
    "XluaRefReadonlyTests.LuaCATSTreatsRefReadonlyParametersAsArguments",
]
# Also required in a project made by prepare.py --fast-enter-play-mode.
PLAY_MODE_FIXTURE = "Assets/XluaPlayModeHost.cs"
REQUIRED_PLAY_MODE = [
    "XluaPlayModeTests.FastEnterPlayModeDisposesEachSession",
    "XluaPlayModeTests.FastEnterPlayModeWithoutSceneReload",
    "XluaPlayModeTests.FastEnterPlayModeLeavesEnvsUndisposed",
    "XluaPlayModeTests.DomainReloadEachSession",
    "XluaPlayModeTests.ScriptRecompileWithALiveEnv",
]
# Also required in a project made by prepare.py --source-gen, which has this fixture.
SOURCEGEN_FIXTURE = "Assets/XluaSourceGenLocal.cs"
REQUIRED_SOURCEGEN = [
    "XluaSourceGenTests.BindingsAreGeneratedInEachAssembly",
    "XluaSourceGenTests.RegisteredInEditMode",
    "XluaSourceGenTests.MergedLinkXmlReadsAsUtf8",
    "XluaSourceGenTests.LuaCallsThroughTheGeneratedWrappers",
    "XluaSourceGenTests.CSharpCallLuaThroughTheGeneratedBridges",
    "XluaSourceGenTests.RegisteredAtEachPlayModeStartWithoutDomainReload",
    "XluaSourceGenTests.RegisteredInPlayModeAfterADomainReload",
]
# Also required in a project made by prepare.py --minimal-plugin.
PLUGIN_VARIANTS_FIXTURE = "Assets/Tests/Editor/XluaPluginVariantTests.cs"
REQUIRED_PLUGIN_VARIANTS = [
    "XluaPluginVariantTests.TheSelectorPicksTheMinimalVariantForThePreset",
    "XluaPluginVariantTests.TheEditorLoadsTheDefaultPlugin",
]
# With --source-gen and --roslyn-sdk.
SOURCEGEN_REF_READONLY_FIXTURE = "Assets/Tests/Editor/XluaSourceGenRefReadonlyTests.cs"
REQUIRED_SOURCEGEN_REF_READONLY = ["XluaSourceGenRefReadonlyTests.WrappersFollowTheEditorsCompiler"]
# Unity could not load or run the generator.
ANALYZER_WARNING = re.compile(r"\b(?:warning|error) CS(8032|8784|8785|9057)\b")
XLSG_LINE = re.compile(r"\b(warning|error|info) (XLSG\d{3})\b")
# The fixture's BlackList entry for a member that does not exist.
XLSG003_EXPECTED = re.compile(r"XluaSourceGenLocal\.cs\(\d+,\d+\): warning XLSG003: BlackList names 'NoSuchMember'")
# Also required in a project made by prepare.py --roslyn-sdk, which has this DLL.
REF_READONLY_LIB = "Assets/XluaRefReadonlyLib/XluaRefReadonlyLib.dll"
REQUIRED_REF_READONLY_LIB = [
    "XluaRefReadonlyLibTests.LuaCATSDescribesTheLibraryFromTheManifest",
    "XluaRefReadonlyLibTests.LuaCallsUsableRefReadonlyMembers",
]
# Roslyn's ref-kind mismatch warnings: generated code passes ref readonly
# arguments with `ref` and suppresses the warnings its `in` bridges cause.
REF_KIND_WARNING = re.compile(r"warning CS(9191|9192|9195|9196|9198)\b")


def verify(results, mode, project=None):
    """Fail unless every required test passed; return the summary line."""
    root = ET.parse(results).getroot()
    cases = {c.get("fullname"): c for c in root.iter("test-case")}
    problems = []
    required = list(REQUIRED)
    if project is not None and (Path(project) / REF_READONLY_LIB).exists():
        required += REQUIRED_REF_READONLY_LIB
    if project is not None and (Path(project) / REF_READONLY_FIXTURE).exists():
        required += REQUIRED_REF_READONLY
    if project is not None and (Path(project) / PLAY_MODE_FIXTURE).exists():
        required += REQUIRED_PLAY_MODE
    if project is not None and (Path(project) / SOURCEGEN_FIXTURE).exists():
        required += REQUIRED_SOURCEGEN
    if project is not None and (Path(project) / SOURCEGEN_REF_READONLY_FIXTURE).exists():
        required += REQUIRED_SOURCEGEN_REF_READONLY
    if project is not None and (Path(project) / PLUGIN_VARIANTS_FIXTURE).exists():
        required += REQUIRED_PLUGIN_VARIANTS
    for name in required:
        case = cases.get(name)
        if case is None:
            problems.append(name + ": not run")
        elif case.get("result") != "Passed":
            message = case.find("failure/message")
            problems.append("%s: %s %s" % (name, case.get("result"),
                                           (message.text or "").strip() if message is not None else ""))
    failed = [n for n, c in cases.items() if c.get("result") != "Passed" and n not in required]
    problems += [n + ": " + cases[n].get("result") for n in failed]
    if project is not None:
        actual = read_mode(project)
        if actual != mode:
            problems.append("XluaEditorTestMode.txt says %s, expected %s" % (actual, mode))
        expected = "sourcegen" if (Path(project) / SOURCEGEN_FIXTURE).exists() else "reflection"
        if mode != expected:
            problems.append("mode %s, but the project is one for mode %s" % (mode, expected))
    summary = "total=%s passed=%s failed=%s skipped=%s" % tuple(
        root.get(k) for k in ["total", "passed", "failed", "skipped"])
    if problems:
        raise SystemExit("XLUA_EDITOR_TESTS_FAIL (%s, %s): %s\n  %s" % (mode, results, summary, "\n  ".join(problems)))
    print("XLUA_EDITOR_TESTS_PASS (%s): %s" % (mode, summary), flush=True)
    return summary


def read_mode(project):
    mode_file = Path(project) / "XluaEditorTestMode.txt"
    return mode_file.read_text().strip() if mode_file.exists() else "reflection"


# The scripting backend each --build-target always uses.
BUILD_TARGET_BACKENDS = {"iOS": "IL2CPP", "WebGL": "IL2CPP"}
build_target_args = []


def unity(editor, project, log, args, timeout):
    """One batch-mode launch; kills the editor's process group on timeout."""
    cmd = ([str(editor), "-batchmode", "-nographics", "-projectPath", str(project), "-logFile", str(log)]
           + build_target_args + args)
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True)
    try:
        code = proc.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        raise SystemExit("XLUA_EDITOR_TIMEOUT after %ds: %s (log %s)" % (timeout, " ".join(args), log))
    print("  exit %d in %.0fs" % (code, time.time() - start), flush=True)
    return code


def run_tests(a, mode):
    results = a.out / ("results-%s.xml" % mode)
    log = a.out / ("tests-%s.log" % mode)
    if results.exists():
        results.unlink()
    unity(a.unity, a.project, log, ["-runTests", "-testPlatform", "EditMode", "-testResults", str(results)], a.timeout)
    if not results.exists():
        raise SystemExit("XLUA_EDITOR_TESTS_FAIL (%s): no result file; see %s" % (mode, log))
    summary = verify(results, mode, a.project)
    # Unity compiles the project, and the generator's code, during the launch and logs its warnings.
    text = log.read_text(errors="replace") if log.exists() else ""
    warnings = sorted(set(line.strip() for line in text.splitlines() if REF_KIND_WARNING.search(line)))
    if warnings:
        raise SystemExit("XLUA_EDITOR_TESTS_FAIL (%s): ref-kind warnings in %s:\n  %s" % (mode, log, "\n  ".join(warnings)))
    return summary


def check_luals(a):
    luals = luals_tools.download(a.out / "luals-download") if a.download_luals else a.luals
    if luals is None:
        print("lua-language-server not given; skipped the editor check")
        return
    types = a.project.resolve() / "LuaTypes"
    if not (types / "CS.lua").exists():
        raise SystemExit("no LuaCATS definitions in " + str(types))
    work = a.out / "luals"
    luals_tools.check_good(luals, here / "luacats/good.lua", [types], work)
    luals_tools.check_bad(luals, here / "luacats/bad.lua", [types], work)


def editor_log_lines(a, mode):
    log = a.out / ("tests-%s.log" % mode)
    return [line.strip() for line in (log.read_text(errors="replace") if log.exists() else "").splitlines()]


def check_source_gen_logs(a):
    """The editor loaded the source generator and printed its diagnostics."""
    lines = editor_log_lines(a, "sourcegen")
    problems = [line for line in lines if ANALYZER_WARNING.search(line)]
    xlsg = sorted(set(line for line in lines if XLSG_LINE.search(line)))
    problems += ["XLSG error: " + line for line in xlsg if " error XLSG" in line]
    problems += ["a configuration list (the suite has none): " + line for line in xlsg if "XLSG011" in line]
    if not any(XLSG003_EXPECTED.search(line) for line in xlsg):
        problems.append("no XLSG003 warning for XluaSourceGenLocal.cs with file and line")
    print("XLSG diagnostics in the editor log:\n  " + ("\n  ".join(xlsg) if xlsg else "(none)"), flush=True)
    if problems:
        raise SystemExit("XLUA_EDITOR_SOURCEGEN_FAIL:\n  " + "\n  ".join(problems))
    print("XLUA_EDITOR_SOURCEGEN_LOGS_PASS", flush=True)


def check_reflection_logs(a):
    """Without the generator nothing prints its diagnostics."""
    xlsg = sorted(set(line for line in editor_log_lines(a, "reflection") if XLSG_LINE.search(line)))
    if xlsg:
        raise SystemExit("XLUA_EDITOR_REFLECTION_FAIL: XLSG diagnostics without the generator:\n  " + "\n  ".join(xlsg))
    print("XLUA_EDITOR_REFLECTION_LOGS_PASS: no XLSG diagnostics", flush=True)


p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
sub = p.add_subparsers(dest="command", required=True)
run_p = sub.add_parser("run", help="run the EditMode tests in the project's mode")
run_p.add_argument("--unity", type=Path, required=True, help="Unity editor executable")
run_p.add_argument("--timeout", type=int, default=1800, help="seconds for the editor launch")
run_p.add_argument("--build-target", choices=sorted(BUILD_TARGET_BACKENDS),
                   help="launch the editor with this active build target (an IL2CPP one); its player module "
                        "must be installed")
luals_p = sub.add_parser("luals", help="only run lua-language-server on <project>/LuaTypes")
for sp in (run_p, luals_p):
    sp.add_argument("--project", type=Path, required=True, help="project made by prepare.py --editor-tools")
    sp.add_argument("--out", type=Path, required=True, help="logs, results and LuaLS workspaces")
    sp.add_argument("--luals", type=Path, help="lua-language-server executable")
    sp.add_argument("--download-luals", action="store_true",
                    help="download lua-language-server %s (Linux or macOS)" % luals_tools.VERSION)
verify_p = sub.add_parser("verify", help="check one NUnit result file")
verify_p.add_argument("--results", type=Path, required=True)
verify_p.add_argument("--mode", required=True, choices=MODES)
verify_p.add_argument("--project", type=Path, help="also check its XluaEditorTestMode.txt")
a = p.parse_args()

if a.command == "verify":
    verify(a.results, a.mode, a.project)
    sys.exit(0)
a.out = a.out.resolve()
a.out.mkdir(parents=True, exist_ok=True)
if a.command == "luals":
    check_luals(a)
    sys.exit(0)

a.project = a.project.resolve()
# What XluaGeneratedCodeTests.EditorEmitsWithEveryBuildTarget expects.
if a.build_target:
    build_target_args[:] = ["-buildTarget", a.build_target]
(a.project / "XluaEditorTestBackend.txt").write_text(
    (BUILD_TARGET_BACKENDS[a.build_target] if a.build_target else "Mono2x") + "\n")
mode = "sourcegen" if (a.project / SOURCEGEN_FIXTURE).exists() else "reflection"
summary = run_tests(a, mode)
if mode == "sourcegen":
    check_luals(a)
    check_source_gen_logs(a)
else:
    # LuaCATS declares only xLua's own API without the generator's manifests,
    # which good.lua's checks of the suite's types need.
    print("lua-language-server: skipped in mode reflection (LuaCATS declares only xLua's own API)", flush=True)
    check_reflection_logs(a)
print("XLUA_EDITOR_ALL_PASS: %s: %s" % (mode, summary))
