"""Run the xLua editor tests in a project made by prepare.py --editor-tools.

Generated code only works after Unity compiles it, so `run` launches the
editor in batch mode four times, one launch at a time:

1. -executeMethod XluaEditorTestSteps.GenerateCode -quit
   (the "XLua/Generate Code" menu entry; also writes XluaEditorTestMode.txt)
2. -runTests: the EditMode tests in mode "generated" (results-generated.xml)
3. -executeMethod XluaEditorTestSteps.ClearCode -quit ("XLua/Clear Generated Code")
4. -runTests: the same tests in mode "cleared", which use reflection (results-cleared.xml)

After step 2 lua-language-server checks Test/UnityMatrix/luacats/good.lua and
bad.lua against the LuaCATS files that the tests wrote to <project>/LuaTypes.
LuaLS runs here, outside Unity, so the editor needs no extra tool.

`luals` runs only that check and `verify` only checks a result file; the
Unity editor matrix workflow uses them between its GameCI steps.
"""
import argparse
import os
from pathlib import Path
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
    "XluaGeneratedCodeTests.GeneratedCodeMatchesMode",
    "XluaGeneratedCodeTests.LuaUsesVector3",
    "XluaGeneratedCodeTests.LuaCreatesAndDestroysGameObjects",
    "XluaGeneratedCodeTests.LuaCallsOverloadsRefOutStructsAndEnums",
    "XluaGeneratedCodeTests.LuaFunctionsBecomeCSharpCallLuaDelegates",
]


def verify(results, mode, project=None):
    """Fail unless every required test passed; return the summary line."""
    root = ET.parse(results).getroot()
    cases = {c.get("fullname"): c for c in root.iter("test-case")}
    problems = []
    for name in REQUIRED:
        case = cases.get(name)
        if case is None:
            problems.append(name + ": not run")
        elif case.get("result") != "Passed":
            message = case.find("failure/message")
            problems.append("%s: %s %s" % (name, case.get("result"),
                                           (message.text or "").strip() if message is not None else ""))
    failed = [n for n, c in cases.items() if c.get("result") != "Passed" and n not in REQUIRED]
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
    return verify(results, mode, a.project)


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

a.project = a.project.resolve()
summaries = []
step(a, "XluaEditorTestSteps.GenerateCode", "generate")
summaries.append("generated: " + run_tests(a, "generated"))
check_luals(a)
if not a.skip_clear:
    step(a, "XluaEditorTestSteps.ClearCode", "clear")
    summaries.append("cleared: " + run_tests(a, "cleared"))
print("XLUA_EDITOR_ALL_PASS: " + "; ".join(summaries))
