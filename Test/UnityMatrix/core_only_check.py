"""The editor-level core-only check in a real Unity editor (docs/design/core-luascript.md, section 7.6).

    python3 Test/UnityMatrix/core_only_check.py --unity /path/Unity.app/Contents/MacOS/Unity \\
        --unity-version 6000.3.26f1 --plugin BUILD/host-core/libxlua.dylib --output DIR

Builds the xLua UPM package (Tools/make_upm_package.py, a file: folder) with
the given native library as the macOS Editor plugin (a core library, built
with XLUA_PRESET=core), installs it into an empty project, and runs the editor twice in batch
mode: first to put XLUA_CORE_ONLY into the Player Settings, as a user does
(a hand-written ProjectSettings.asset is replaced by the editor), then, with
Test/Core/UnitySample/LuaScriptSample.cs and the Editor probe
Test/UnityMatrix/CoreOnly/XluaCoreOnlyProbe.cs added, with -executeMethod
XluaCoreOnlyProbe.Run.

It passes when the editor exits 0 and the probe logs XLUA_CORE_ONLY_PROBE_PASS
(XLua and XLua.Editor not loaded, XLua.Core and XLua.Core.Editor loaded, the
define in Player Settings, the plugin selector preferring the core variant, a
LuaScript call on the core library), and Library/ScriptAssemblies has
XLua.Core.dll, XLua.Core.Editor.dll and Assembly-CSharp.dll but neither
XLua.dll nor XLua.Editor.dll. One editor at a time; each launch has a
timeout that kills the editor's process group.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, required=True, help="the editor executable (Unity.app/Contents/MacOS/Unity)")
p.add_argument("--unity-version", required=True, help="written to ProjectSettings/ProjectVersion.txt")
p.add_argument("--plugin", type=Path, required=True, help="a core library for the macOS Editor (arm64)")
p.add_argument("--output", type=Path, required=True, help="an empty folder for the project and logs")
p.add_argument("--timeout", type=int, default=1800, help="seconds for each editor launch")
a = p.parse_args()

out = a.output.resolve()
if out.exists() and any(out.iterdir()):
    p.error("--output must be empty")
project = out / "project"
for folder in ["Assets/Editor", "Packages", "ProjectSettings", "LocalPackages"]:
    (project / folder).mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(repo / "Tools"))
import make_upm_package  # noqa: E402
package = project / "LocalPackages" / make_upm_package.DEFAULT_NAME
subprocess.run([sys.executable, str(repo / "Tools/make_upm_package.py"), "--output", str(package), "--no-samples",
                "--no-webgl", "--plugin", "arm64/libxlua.dylib=" + str(a.plugin.resolve())], check=True)
(project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
    make_upm_package.DEFAULT_NAME: "file:../LocalPackages/" + package.name}}, indent=2) + "\n")
(project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + a.unity_version + "\n")
(project / "Assets/Editor/XluaCoreOnlySetup.cs").write_text("""using UnityEditor;
using UnityEditor.Build;

public static class XluaCoreOnlySetup
{
    public static void Define()
    {
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, "XLUA_CORE_ONLY");
        AssetDatabase.SaveAssets();
    }
}
""")


def launch(log, method, quit):
    cmd = [str(a.unity), "-batchmode", "-nographics", "-projectPath", str(project), "-logFile", str(log),
           "-executeMethod", method] + (["-quit"] if quit else [])
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True)
    try:
        code = proc.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        sys.exit("XLUA_CORE_ONLY_TIMEOUT after %ds in %s (log %s)" % (a.timeout, method, log))
    print("  exit %d in %.0fs" % (code, time.time() - start), flush=True)
    return code


if launch(out / "setup.log", "XluaCoreOnlySetup.Define", True) != 0:
    sys.exit("XLUA_CORE_ONLY_FAIL: setting the define failed (log %s)" % (out / "setup.log"))
shutil.copy2(repo / "Test/Core/UnitySample/LuaScriptSample.cs", project / "Assets/LuaScriptSample.cs")
shutil.copy2(repo / "Test/UnityMatrix/CoreOnly/XluaCoreOnlyProbe.cs", project / "Assets/Editor/XluaCoreOnlyProbe.cs")
log = out / "editor.log"
code = launch(log, "XluaCoreOnlyProbe.Run", False)
text = log.read_text(errors="replace") if log.is_file() else ""
compiled = sorted(f.name for f in (project / "Library/ScriptAssemblies").glob("*.dll"))
problems = []
for name in ["XLua.Core.dll", "XLua.Core.Editor.dll", "Assembly-CSharp.dll"]:
    if name not in compiled:
        problems.append(name + " was not compiled")
for name in ["XLua.dll", "XLua.Editor.dll"]:
    if name in compiled:
        problems.append(name + " was compiled despite XLUA_CORE_ONLY")
if "XLUA_CORE_ONLY_PROBE_PASS" not in text:
    problems.append("no XLUA_CORE_ONLY_PROBE_PASS in the log")
if code != 0:
    problems.append("the editor exited with %d" % code)
for line in text.splitlines():
    if "XLUA_CORE_ONLY_PROBE" in line or ": error CS" in line:
        print("  " + line)
print("  ScriptAssemblies: " + ", ".join(compiled))
if problems:
    sys.exit("XLUA_CORE_ONLY_FAIL: " + "; ".join(problems) + " (log " + str(log) + ")")
print("XLUA_CORE_ONLY_PASS: with XLUA_CORE_ONLY Unity %s compiled %s, no XLua or XLua.Editor, and ran a LuaScript"
      % (a.unity_version, ", ".join(compiled)))
