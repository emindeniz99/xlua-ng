"""Run the xLua editor tests in a project made by prepare.py --editor-tools.

Generated code only works after Unity compiles it, so `run` launches the
editor in batch mode four times, one launch at a time:

1. -executeMethod XluaEditorTestSteps.GenerateCode -quit
   (the "XLua/Generate Code" menu entry; also writes XluaEditorTestMode.txt)
2. -runTests: the EditMode tests in mode "generated" (results-generated.xml)
3. -executeMethod XluaEditorTestSteps.ClearCode -quit ("XLua/Clear Generated Code")
4. -runTests: the same tests in mode "cleared", where xLua emits wrappers and
   [CSharpCallLua] bridges at run time instead (results-cleared.xml)

After step 2 lua-language-server checks Test/UnityMatrix/luacats/good.lua and
bad.lua against the LuaCATS files that the tests wrote to <project>/LuaTypes.
LuaLS runs here, outside Unity, so the editor needs no extra tool.

A project made with prepare.py --fast-enter-play-mode also has to pass the
XluaPlayModeTests, which enter and exit play mode from EditMode tests.

A project made with prepare.py --csharp12 (-langversion:12, Unity 6000.6 or
later) also has to pass the XluaRefReadonlyTests for C# 12 `ref readonly`
parameters. One made with prepare.py --roslyn-sdk has a precompiled DLL with
`ref readonly` members that Roslyn before Unity 6000.6 cannot use: it has to
pass the XluaRefReadonlyLibTests, and Generate Code has to warn once about each
skipped member before 6000.6 and about none after. Every run fails on
Roslyn's ref-kind mismatch warnings (CS9191, CS9192, CS9195, CS9196, CS9198)
in the generate and test logs.

`luals` runs only that check and `verify` only checks a result file; the
Unity editor matrix workflow uses them between its GameCI steps.
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

# Tests that must pass in every run; the generated-code tests check the mode.
REQUIRED = [
    "XluaSmokeTests.Lua55BridgeRunsInTheEditor",
    "XluaEditorToolsTests.LuaCATSDefinitionsAreGenerated",
    "XluaEditorToolsTests.LuaCATSDescribesUnityAndTestTypes",
    "XluaEditorToolsTests.LuaCATSTreatsInParametersAsArguments",
    "XluaEditorToolsTests.LuarcMatchesThePlugin",
    "XluaEditorToolsTests.ClearAndGenerateKeepsUserFiles",
    "XluaEditorToolsTests.ValidateSetupAcceptsThePlugin",
    "XluaGeneratedCodeTests.GeneratedCodeMatchesMode",
    "XluaGeneratedCodeTests.CodeEmitIsCompiledIntoTheEditor",
    "XluaGeneratedCodeTests.LuaUsesVector3",
    "XluaGeneratedCodeTests.LuaCreatesAndDestroysGameObjects",
    "XluaGeneratedCodeTests.LuaCallsOverloadsRefOutStructsAndEnums",
    "XluaGeneratedCodeTests.LuaFunctionsBecomeCSharpCallLuaDelegates",
    "XluaGeneratedCodeTests.LuaPassesInParametersByValue",
    "XluaGeneratedCodeTests.LuaUsesCamera",
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
# Also required in a project made by prepare.py --roslyn-sdk, which has this DLL.
REF_READONLY_LIB = "Assets/XluaRefReadonlyLib/XluaRefReadonlyLib.dll"
REQUIRED_REF_READONLY_LIB = [
    "XluaRefReadonlyLibTests.GenerationSkipsMembersTheCompilerCannotUse",
    "XluaRefReadonlyLibTests.GeneratedCodeHasOnlyUsableMembers",
    "XluaRefReadonlyLibTests.LuaCallsUsableRefReadonlyMembers",
]
# What Generate Code logs for each of the DLL's members that the editor's
# Roslyn cannot use (CS0570); expected before Unity 6000.6, never after.
REF_READONLY_SKIPPED = [
    "xLua skipped [CSharpCallLua] XluaRefReadonlyLib.Measure (its XluaRefReadonlyLib.Measure.Invoke)",
    "xLua skipped [CSharpCallLua] XluaRefReadonlyLib.IGauge (its XluaRefReadonlyLib.IGauge.Read)",
    "xLua skipped XluaRefReadonlyLib.Gauge.Scaled",
    "xLua skipped XluaRefReadonlyLib.IGauge.Read",
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
        mode_file = Path(project) / "XluaEditorTestMode.txt"
        actual = mode_file.read_text().strip() if mode_file.exists() else "reflection"
        if actual != mode:
            problems.append("XluaEditorTestMode.txt says %s, expected %s" % (actual, mode))
    summary = "total=%s passed=%s failed=%s skipped=%s" % tuple(
        root.get(k) for k in ["total", "passed", "failed", "skipped"])
    if problems:
        raise SystemExit("XLUA_EDITOR_TESTS_FAIL (%s, %s): %s\n  %s" % (mode, results, summary, "\n  ".join(problems)))
    print("XLUA_EDITOR_TESTS_PASS (%s): %s" % (mode, summary), flush=True)
    return summary


def unity(editor, project, log, args, timeout):
    """One batch-mode launch; kills the editor's process group on timeout."""
    cmd = [str(editor), "-batchmode", "-nographics", "-projectPath", str(project), "-logFile", str(log)] + args
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
    if results.exists():
        results.unlink()
    unity(a.unity, a.project, a.out / ("tests-%s.log" % mode),
          ["-runTests", "-testPlatform", "EditMode", "-testResults", str(results)], a.timeout)
    if not results.exists():
        raise SystemExit("XLUA_EDITOR_TESTS_FAIL (%s): no result file; see %s" % (mode, a.out / ("tests-%s.log" % mode)))
    summary = verify(results, mode, a.project)
    # Unity compiles the generated code during these launches and logs its warnings.
    for name in ["generate.log", "tests-%s.log" % mode]:
        log = a.out / name
        text = log.read_text(errors="replace") if log.exists() else ""
        warnings = sorted(set(line.strip() for line in text.splitlines() if REF_KIND_WARNING.search(line)))
        if warnings:
            raise SystemExit("XLUA_EDITOR_TESTS_FAIL (%s): ref-kind warnings in %s:\n  %s" % (mode, log, "\n  ".join(warnings)))
    return summary


def step(a, method, name):
    code = unity(a.unity, a.project, a.out / (name + ".log"), ["-executeMethod", method, "-quit"], a.timeout)
    if code != 0:
        raise SystemExit("XLUA_EDITOR_STEP_FAIL: %s exited %d; see %s" % (method, code, a.out / (name + ".log")))


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


p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
sub = p.add_subparsers(dest="command", required=True)
run_p = sub.add_parser("run", help="generate, test, clear and test again")
run_p.add_argument("--unity", type=Path, required=True, help="Unity editor executable")
run_p.add_argument("--timeout", type=int, default=1800, help="seconds per editor launch")
run_p.add_argument("--skip-clear", action="store_true", help="stop after the generated-code run")
luals_p = sub.add_parser("luals", help="only run lua-language-server on <project>/LuaTypes")
for sp in (run_p, luals_p):
    sp.add_argument("--project", type=Path, required=True, help="project made by prepare.py --editor-tools")
    sp.add_argument("--out", type=Path, required=True, help="logs, results and LuaLS workspaces")
    sp.add_argument("--luals", type=Path, help="lua-language-server executable")
    sp.add_argument("--download-luals", action="store_true",
                    help="download lua-language-server %s (Linux)" % luals_tools.VERSION)
verify_p = sub.add_parser("verify", help="check one NUnit result file")
verify_p.add_argument("--results", type=Path, required=True)
verify_p.add_argument("--mode", required=True, choices=["generated", "cleared", "reflection"])
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

def check_skipped_members(a):
    """Generate Code warns once about each ref readonly member it skips, before 6000.6 only."""
    if not (a.project / REF_READONLY_LIB).exists():
        return
    version = (a.project / "ProjectSettings/ProjectVersion.txt").read_text().split(":", 1)[1].strip()
    major, minor = (int(x) for x in re.match(r"(\d+)\.(\d+)", version).groups())
    expect = (major, minor) < (6000, 6)
    lines = (a.out / "generate.log").read_text(errors="replace").splitlines()
    problems = []
    for skipped in REF_READONLY_SKIPPED:
        # The message line, not the stack trace and "(Filename: ...)" lines after it.
        count = sum(1 for line in lines if line.startswith(skipped + ":"))
        if expect and count != 1:
            problems.append("%d warnings instead of one: %s" % (count, skipped))
        if not expect and count:
            problems.append("unexpected warning: " + skipped)
    if problems:
        raise SystemExit("XLUA_EDITOR_STEP_FAIL: Generate Code in %s (%s):\n  %s" % (version, a.out / "generate.log", "\n  ".join(problems)))
    print("XLUA_EDITOR_REF_READONLY_SKIP_PASS: %s, %s" % (version, "members skipped with warnings" if expect else "no members skipped"), flush=True)


a.project = a.project.resolve()
summaries = []
step(a, "XluaEditorTestSteps.GenerateCode", "generate")
check_skipped_members(a)
summaries.append("generated: " + run_tests(a, "generated"))
check_luals(a)
if not a.skip_clear:
    step(a, "XluaEditorTestSteps.ClearCode", "clear")
    summaries.append("cleared: " + run_tests(a, "cleared"))
print("XLUA_EDITOR_ALL_PASS: " + "; ".join(summaries))
