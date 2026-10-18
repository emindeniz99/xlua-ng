"""XLua > Project Layout in a real Unity editor: switch to Assembly-CSharp and back.

Prepares a project with prepare.py --editor-tools (the asmdef layout, the
default) and launches the editor in batch mode, one launch at a time:

1. -executeMethod XLua.ProjectLayout.Report: the layout must be Asmdefs, with
   xLua's C API in XLua.Core
2. -executeMethod XLua.ProjectLayout.UseAssemblyCSharp: the four asmdefs and
   their .meta files must move to Assets/XLua/Asmdef~/, unchanged
3. -executeMethod XluaEditorTestSteps.GenerateCode: the project compiles with
   xLua in Assembly-CSharp, and Generate Code writes Assets/XLua/Gen
4. Report: the layout must be AssemblyCSharp, xLua in Assembly-CSharp, and the
   generated code must compile there
5. -executeMethod XLua.ProjectLayout.UseAsmdefs: the files must move back,
   unchanged, and Asmdef~ must be gone
6. Report: Asmdefs and XLua.Core again; the same generated code compiles
   against XLua.dll without generating it again
7. why the switch moves files instead of using a define constraint: an
   Editor script puts XLUA_CORE_ONLY into the Player Settings (XLua.asmdef
   has the constraint !XLUA_CORE_ONLY); the recompile that follows in the
   same launch must fail with CS0246 or CS0234 for xLua's types in the
   project's own scripts, because the scripts of Assets/XLua/Src went into no
   assembly at all, not into Assembly-CSharp

Launches 1 to 6 must exit 0 without a compiler error in their logs. Prints
XLUA_LAYOUT_SWITCH_PASS or exits with an error.
"""
import argparse
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time

here = Path(__file__).resolve().parent
repo = here.parents[1]
ASMDEFS = ["Core/XLua.Core.asmdef", "Core/Editor/XLua.Core.Editor.asmdef", "Src/XLua.asmdef",
           "Src/Editor/XLua.Editor.asmdef"]

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, required=True, help="Unity editor executable")
p.add_argument("--unity-version", required=True)
p.add_argument("--plugin", type=Path, required=True, help="rebuilt macOS libxlua.dylib")
p.add_argument("--project", type=Path, required=True, help="new project folder (must not exist or be empty)")
p.add_argument("--out", type=Path, required=True, help="logs")
p.add_argument("--timeout", type=int, default=1500, help="seconds per editor launch")
a = p.parse_args()
a.project, a.out = a.project.resolve(), a.out.resolve()
a.out.mkdir(parents=True, exist_ok=True)
subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools"], check=True)
xlua = a.project / "Assets/XLua"


def unity(name, method, expect_errors=False):
    log = a.out / (name + ".log")
    cmd = [str(a.unity), "-batchmode", "-nographics", "-quit", "-projectPath", str(a.project), "-logFile", str(log),
           "-executeMethod", method]
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True)
    try:
        code = proc.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        raise SystemExit("XLUA_LAYOUT_SWITCH_TIMEOUT after %ds: %s (log %s)" % (a.timeout, name, log))
    text = log.read_text(errors="replace") if log.exists() else ""
    errors = sorted(set(l.strip() for l in text.splitlines() if re.search(r"error CS\d+", l)))
    print("  exit %d in %.0fs, %d compiler errors" % (code, time.time() - start, len(errors)), flush=True)
    if expect_errors:
        return errors
    if code or errors:
        raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: %s exited %d; see %s\n  %s" % (name, code, log, "\n  ".join(errors[:20])))
    return text


def report(name, layout, assembly):
    text = unity(name, "XLua.ProjectLayout.Report")
    found = re.findall(r"^XLUA_PROJECT_LAYOUT: (\S+) (\S+)", text, re.M)
    if found != [(layout, assembly)]:
        raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: %s reported %s, expected %s %s" % (name, found, layout, assembly))
    print("  layout %s, xLua in %s" % (layout, assembly), flush=True)


def files(where):
    """The asmdefs and .meta files under where, by path relative to it."""
    return {rel + ext: (where / (rel + ext)).read_bytes() for rel in ASMDEFS for ext in ["", ".meta"]
            if (where / (rel + ext)).is_file()}


original = files(xlua)
if len(original) != 2 * len(ASMDEFS):
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: the prepared project lacks asmdefs: %s" % sorted(original))
report("report-asmdefs", "Asmdefs", "XLua.Core")
unity("use-assembly-csharp", "XLua.ProjectLayout.UseAssemblyCSharp")
if files(xlua) or files(xlua / "Asmdef~") != original:
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: Use Assembly-CSharp did not move the asmdefs to Asmdef~ unchanged")
unity("generate", "XluaEditorTestSteps.GenerateCode")
if not (xlua / "Gen/XLuaGenAutoRegister.cs").is_file():
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: Generate Code wrote no Assets/XLua/Gen")
report("report-assembly-csharp", "AssemblyCSharp", "Assembly-CSharp")
unity("use-asmdefs", "XLua.ProjectLayout.UseAsmdefs")
if files(xlua) != original or (xlua / "Asmdef~").exists():
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: Use asmdefs did not move the asmdefs back unchanged, or left Asmdef~")
report("report-asmdefs-again", "Asmdefs", "XLua.Core")
(a.project / "Assets/Editor/XluaLayoutDefine.cs").write_text("""using UnityEditor;
using UnityEditor.Build;

public static class XluaLayoutDefine
{
    public static void Define()
    {
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, "XLUA_CORE_ONLY");
        AssetDatabase.SaveAssets();
    }
}
""")
# Setting the define recompiles in the same launch, which then aborts on the errors.
errors = unity("define-core-only", "XluaLayoutDefine.Define", expect_errors=True)
outside = [e for e in errors if re.search(r" error CS02(46|34)", e) and "Assets/XLua/" not in e]
if not outside:
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: with XLUA_CORE_ONLY the project's scripts still found xLua's types; "
                     "see " + str(a.out / "report-core-only.log"))
print("  with XLUA_CORE_ONLY: %d CS0246 errors outside Assets/XLua, e.g. %s" % (len(outside), outside[0][:200]), flush=True)
print("XLUA_LAYOUT_SWITCH_PASS (%s: asmdefs -> Assembly-CSharp -> asmdefs, GUIDs kept, generated code compiled in both; "
      "a failing define constraint drops xLua instead of moving it to Assembly-CSharp)" % a.unity_version)
