"""IntKey hotfix with two assemblies in a real Unity editor.

Prepares a project with prepare.py --editor-tools --hotfix, the injector that
Tools/build_tools.py built (copied to <project>/Tools), and the fixture in
Test/UnityMatrix/HotfixIntKey: HotfixIntKeyLocal in Assembly-CSharp, Game.Shop
in an asmdef assembly Game, and an IntKey [Hotfix] list for both under an
Editor folder (Hotfix_EN.md, "Several hotfix assemblies"). Then it launches
the editor three times, one launch at a time:

1. -executeMethod XluaEditorTestSteps.GenerateCode -quit
2. -executeMethod HotfixIntKeySteps.Validate -quit: XLua/Validate Setup must
   report no problem
3. -runTests -testFilter HotfixIntKeyTests: Hotfix Inject In Editor must warn
   that IntKey falls back to field mode in Game.dll for Game.Shop, write
   hotfix_id_map_Assembly-CSharp, hotfix_id_map_Game and the merged
   hotfix_id_map; then in play mode (a domain reload loads the injected
   assemblies) auto_id_map() and xlua.hotfix patch and restore both types.

--asmdef and --package put xLua in XLua.dll, where IntKey falls back to field
mode in both assemblies. --source-gen BUILD_DIR (with one of them) installs the
source generator, gives Game.dll a reference to XLua and configures both types
with [assembly: Hotfix(typeof(T), HotfixFlag.IntKey)] in their own assemblies
(HotfixIntKey/SourceGen) instead of the Editor list; step 1 is skipped, and
each assembly must use its own IntKey slots: no fallback warning, no
__Hotfix fields, and id maps that name their assembly.

Prints XLUA_HOTFIX_INTKEY_PASS or exits with an error.
"""
import argparse
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

here = Path(__file__).resolve().parent
repo = here.parents[1]
TOOLS = ["XLuaHotfixInject.exe", "XLuaHotfixInject.manifest", "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll",
         "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll"]

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, required=True, help="Unity editor executable")
p.add_argument("--unity-version", required=True)
p.add_argument("--plugin", type=Path, required=True, help="rebuilt macOS libxlua.dylib")
p.add_argument("--project", type=Path, required=True, help="new project folder (must not exist or be empty)")
p.add_argument("--out", type=Path, required=True, help="logs and results")
p.add_argument("--tools", type=Path, default=repo / "Tools", help="folder that build_tools.py wrote")
p.add_argument("--timeout", type=int, default=1500, help="seconds per editor launch")
p.add_argument("--asmdef", action="store_true",
               help="the opt-in asmdef layout (prepare.py --asmdef): xLua in XLua.dll, so IntKey falls back to "
                    "field mode in Assembly-CSharp too until milestone M7 (HotfixIntKeyTests checks that)")
p.add_argument("--package", action="store_true",
               help="xLua as the UPM package (prepare.py --package): in XLua.dll, as with --asmdef")
p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
               help="with --asmdef or --package: the source generator (prepare.py --source-gen) and the "
                    "attribute configuration of HotfixIntKey/SourceGen; IntKey through each assembly's own slots")
a = p.parse_args()
if a.asmdef and a.package:
    p.error("--asmdef and --package are two layouts; pass one")
if a.source_gen and not (a.asmdef or a.package):
    p.error("--source-gen needs --asmdef or --package")
a.project, a.out = a.project.resolve(), a.out.resolve()
a.out.mkdir(parents=True, exist_ok=True)
for name in TOOLS:
    if not (a.tools / name).is_file():
        raise SystemExit("missing %s; run python3 Tools/build_tools.py first" % (a.tools / name))

subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools", "--hotfix"]
               + (["--asmdef"] if a.asmdef else []) + (["--package"] if a.package else [])
               + (["--source-gen", str(a.source_gen), "--define", "XLUA_TEST_HOTFIX_SOURCE_GEN"] if a.source_gen
                  else ["--define", "XLUA_TEST_HOTFIX_ASMDEF"] if a.asmdef or a.package else []), check=True)
(a.project / "Tools").mkdir()
for name in TOOLS:
    shutil.copy2(a.tools / name, a.project / "Tools" / name)
fixture = here / "HotfixIntKey"
shutil.copytree(fixture / "Game", a.project / "Assets/Game")
shutil.copy2(fixture / "HotfixIntKeyLocal.cs", a.project / "Assets/HotfixIntKeyLocal.cs")
if a.source_gen:
    shutil.copy2(fixture / "SourceGen/Game.asmdef", a.project / "Assets/Game/Game.asmdef")
    shutil.copy2(fixture / "SourceGen/GameHotfix.cs", a.project / "Assets/Game/GameHotfix.cs")
    shutil.copy2(fixture / "SourceGen/LocalHotfix.cs", a.project / "Assets/LocalHotfix.cs")
else:
    shutil.copy2(fixture / "HotfixIntKeyConfig.cs", a.project / "Assets/Editor/HotfixIntKeyConfig.cs")
shutil.copy2(fixture / "HotfixIntKeySteps.cs", a.project / "Assets/Editor/HotfixIntKeySteps.cs")
shutil.copy2(fixture / "HotfixIntKeyTests.cs", a.project / "Assets/Tests/Editor/HotfixIntKeyTests.cs")


def unity(name, args):
    log = a.out / (name + ".log")
    cmd = [str(a.unity), "-batchmode", "-nographics", "-projectPath", str(a.project), "-logFile", str(log)] + args
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True)
    try:
        code = proc.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        raise SystemExit("XLUA_HOTFIX_INTKEY_TIMEOUT after %ds: %s (log %s)" % (a.timeout, name, log))
    print("  exit %d in %.0fs" % (code, time.time() - start), flush=True)
    return code, log.read_text(errors="replace") if log.exists() else ""


# The source generator writes the bindings and bridges; Generate Code would register them twice.
code = 0 if a.source_gen else unity("generate", ["-executeMethod", "XluaEditorTestSteps.GenerateCode", "-quit"])[0]
if code:
    raise SystemExit("XLUA_HOTFIX_INTKEY_FAIL: Generate Code exited %d; see %s" % (code, a.out / "generate.log"))

code, text = unity("validate", ["-executeMethod", "HotfixIntKeySteps.Validate", "-quit"])
lines = text.splitlines()
problems = [l for l in lines if l.startswith("XLUA_VALIDATE_PROBLEM:")]
if code or not any(l.startswith("XLUA_VALIDATE_DONE:") for l in lines):
    raise SystemExit("XLUA_HOTFIX_INTKEY_FAIL: Validate Setup did not finish; see %s" % (a.out / "validate.log"))
if problems:
    raise SystemExit("XLUA_HOTFIX_INTKEY_FAIL: Validate Setup reported:\n  " + "\n  ".join(problems))
print("Validate Setup: no problems", flush=True)

results = a.out / "results-intkey.xml"
if results.exists():
    results.unlink()
code, text = unity("intkey", ["-runTests", "-testPlatform", "EditMode", "-testFilter", "HotfixIntKeyTests",
                              "-testResults", str(results)])
if not results.exists():
    raise SystemExit("XLUA_HOTFIX_INTKEY_FAIL: no result file; see %s" % (a.out / "intkey.log"))
root = ET.parse(results).getroot()
for case in root.iter("test-case"):
    output, message = case.find("output"), case.find("failure/message")
    print(case.get("fullname"), case.get("result"))
    if output is not None and output.text:
        print(output.text.strip())
    if message is not None and message.text:
        print("FAILURE:", message.text.strip())
if root.get("total") != "1" or root.get("passed") != "1":
    raise SystemExit("XLUA_HOTFIX_INTKEY_FAIL: total=%s passed=%s; see %s" % (root.get("total"), root.get("passed"), a.out / "intkey.log"))
print("XLUA_HOTFIX_INTKEY_PASS (%s, %s layout%s)" % (a.unity_version, "package" if a.package else "asmdef" if a.asmdef else "Assets",
                                                     ", source generator" if a.source_gen else ""))
