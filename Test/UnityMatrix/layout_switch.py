"""XLua > Project Layout in a real Unity editor: switch to Assembly-CSharp and back.

Prepares a project with prepare.py --editor-tools --source-gen (the asmdef
layout, the default, with the xLua source generator in
Assets/XLua/Src/Analyzers) without the SourceGen fixture's SgGame asmdef (an
asmdef that references XLua cannot compile once XLua.asmdef is parked), and
launches the editor in batch mode, one launch at a time:

1. -executeMethod XLua.ProjectLayout.Report: the layout must be Asmdefs, with
   xLua's C API in XLua.Core
2. -executeMethod XluaEditorTestSteps.ReportBindings: the generator's units,
   one in Assembly-CSharp with the suite's types (XluaEditorTestTarget.cs) and
   one in XLua (xLua's default GCOptimize pushers)
3. -executeMethod XLua.ProjectLayout.UseAssemblyCSharp: the four asmdefs and
   their .meta files must move to Assets/XLua/Asmdef~/, unchanged, and the
   menu must log that the generator now runs in every assembly (since commit
   b202b0e8 it no longer refuses while the generator is installed)
4. Report: the layout must be AssemblyCSharp, xLua in Assembly-CSharp, and
   the project must compile
5. ReportBindings: one unit in Assembly-CSharp, which now holds xLua, with the
   suite's types and xLua's defaults; no XLua assembly
6. -executeMethod XLua.ProjectLayout.UseAsmdefs: the files must move back,
   unchanged, and Asmdef~ must be gone
7. Report and ReportBindings: Asmdefs and XLua.Core again, the units of step 2
8. why the switch moves files instead of using a define constraint: an
   Editor script puts XLUA_CORE_ONLY into the Player Settings (XLua.asmdef
   has the constraint !XLUA_CORE_ONLY); the recompile that follows (in the
   same launch on 2021.3, in the next one on 6000.3) must fail with CS0246 or
   CS0234 for xLua's types in the project's own scripts, because the scripts
   of Assets/XLua/Src went into no assembly at all, not into Assembly-CSharp

Launches 1 to 7 must exit 0 without a compiler error in their logs. Until
milestone M11 step 2 and 5 were Generate Code, the only generated bindings the
layout without asmdefs had. Prints XLUA_LAYOUT_SWITCH_PASS or exits with an
error.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
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
p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
               help="where Tools/build_source_generator.py builds the source generator (default: <out>/source-generator)")
p.add_argument("--timeout", type=int, default=1500, help="seconds per editor launch")
a = p.parse_args()
a.project, a.out = a.project.resolve(), a.out.resolve()
a.out.mkdir(parents=True, exist_ok=True)
subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools",
                "--source-gen", str((a.source_gen or a.out / "source-generator").resolve())], check=True)
# The SourceGen fixture: SgGame.asmdef references XLua, and XluaSourceGenLocal.cs
# and its tests use SgGame.
shutil.rmtree(a.project / "Assets/SgGame")
(a.project / "Assets/XluaSourceGenLocal.cs").unlink()
(a.project / "Assets/Tests/Editor/XluaSourceGenTests.cs").unlink()
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
    errors = sorted(set(l.strip() for l in text.splitlines() if re.search(r"error CS\d+|error XLSG\d+", l)))
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


def bindings(name, expected):
    """The generator's units by assembly; `expected` lists the assemblies that must have one (and XLua
    has one only when expected)."""
    text = unity(name, "XluaEditorTestSteps.ReportBindings")
    units = {}
    for assembly, cls, count, types in re.findall(r"^XLUA_GENERATED_BINDINGS: (\S+) (\S+) (\d+) \[(.*)\]$", text, re.M):
        if assembly in units:
            raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: %s: two units in %s" % (name, assembly))
        units[assembly] = (cls, int(count), [t for t in types.split(", ") if t])
    for assembly, (cls, count, _) in sorted(units.items()):
        print("  %s: %s, %d types" % (assembly, cls, count), flush=True)
    if not set(expected) <= set(units) or "XLua" in units and "XLua" not in expected:
        raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: %s: units in %s, expected %s" % (name, sorted(units), sorted(expected)))
    cls, _, types = units["Assembly-CSharp"]
    if not cls.startswith("XLua.Generated.Assembly_CSharp_"):
        raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: %s: Assembly-CSharp's bindings class is %s" % (name, cls))
    missing = [t for t in ["XluaEditorTest.Target", "XluaEditorTest.Pair", "UnityEngine.Vector3"] if t not in types]
    if missing:
        raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: %s: Assembly-CSharp's unit lacks %s" % (name, missing))
    return units


def files(where):
    """The asmdefs and .meta files under where, by path relative to it."""
    return {rel + ext: (where / (rel + ext)).read_bytes() for rel in ASMDEFS for ext in ["", ".meta"]
            if (where / (rel + ext)).is_file()}


original = files(xlua)
if len(original) != 2 * len(ASMDEFS):
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: the prepared project lacks asmdefs: %s" % sorted(original))
report("report-asmdefs", "Asmdefs", "XLua.Core")
before = bindings("bindings-asmdefs", ["Assembly-CSharp", "XLua"])
text = unity("use-assembly-csharp", "XLua.ProjectLayout.UseAssemblyCSharp")
if files(xlua) or files(xlua / "Asmdef~") != original:
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: Use Assembly-CSharp did not move the asmdefs to Asmdef~ unchanged")
notice = [l for l in text.splitlines() if "xLua's source generator" in l and "now runs in every assembly" in l]
if not notice:
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: Use Assembly-CSharp did not log that the generator runs in every assembly")
print("  " + notice[0].strip()[:200], flush=True)
report("report-assembly-csharp", "AssemblyCSharp", "Assembly-CSharp")
after = bindings("bindings-assembly-csharp", ["Assembly-CSharp"])
if after["Assembly-CSharp"][1] < before["Assembly-CSharp"][1]:
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: Assembly-CSharp's unit lost types: %d before, %d after"
                     % (before["Assembly-CSharp"][1], after["Assembly-CSharp"][1]))
unity("use-asmdefs", "XLua.ProjectLayout.UseAsmdefs")
if files(xlua) != original or (xlua / "Asmdef~").exists():
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: Use asmdefs did not move the asmdefs back unchanged, or left Asmdef~")
report("report-asmdefs-again", "Asmdefs", "XLua.Core")
again = bindings("bindings-asmdefs-again", ["Assembly-CSharp", "XLua"])
if {k: v[1] for k, v in again.items()} != {k: v[1] for k, v in before.items()}:
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: the units after the way back differ: %s, before %s" % (again, before))
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
# 2021.3 recompiles in the launch that sets the define, which then aborts on the
# errors; 6000.3 exits 0 and compiles in the next launch.
errors = unity("define-core-only", "XluaLayoutDefine.Define", expect_errors=True)
log = "define-core-only"
if not errors:
    errors = unity("report-core-only", "XLua.ProjectLayout.Report", expect_errors=True)
    log = "report-core-only"
outside = [e for e in errors if re.search(r" error CS02(46|34)", e) and "Assets/XLua/" not in e]
if not outside:
    raise SystemExit("XLUA_LAYOUT_SWITCH_FAIL: with XLUA_CORE_ONLY the project's scripts still found xLua's types; "
                     "see " + str(a.out / (log + ".log")))
print("  with XLUA_CORE_ONLY: %d CS0246 errors outside Assets/XLua, e.g. %s" % (len(outside), outside[0][:200]), flush=True)
print("XLUA_LAYOUT_SWITCH_PASS (%s: asmdefs -> Assembly-CSharp -> asmdefs, GUIDs kept, the source generator's bindings "
      "in Assembly-CSharp in both; a failing define constraint drops xLua instead of moving it to Assembly-CSharp)"
      % a.unity_version)
