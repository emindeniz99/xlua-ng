"""IntKey hotfix with two assemblies in a real Unity editor.

Prepares a project with prepare.py --editor-tools --hotfix --source-gen (the
hotfix bridges come only from the xLua source generator since milestone M11),
the injector that Tools/build_tools.py built (copied to <project>/Tools), and
the fixture in Test/UnityMatrix/HotfixIntKey: HotfixIntKeyLocal in
Assembly-CSharp, configured with [assembly: Hotfix(typeof(HotfixIntKeyLocal),
HotfixFlag.IntKey)] (LocalHotfix.cs), and Game.Shop in an asmdef assembly
Game. Then it launches the editor twice, one launch at a time:

1. -executeMethod HotfixIntKeySteps.Validate -quit: XLua/Validate Setup must
   report no problem (without asmdefs only the one about the Inline list below)
2. -runTests -testFilter HotfixIntKeyTests: Hotfix Inject In Editor must write
   hotfix_id_map_Assembly-CSharp, hotfix_id_map_Game and the merged
   hotfix_id_map, with no IntKey fallback warning; then in play mode (a
   domain reload loads the injected assemblies) auto_id_map() and xlua.hotfix
   patch and restore both types. HotfixIntKeyLocal's methods with a `ref`, an
   `in` and a virtual `in` parameter (modreq(InAttribute)) are patched with a
   function that returns an extra result, which must reach the `ref`
   argument only; --csharp12 adds two with `ref readonly` parameters. In
   the asmdef and package layouts Game's [Hotfix] type, Inline type and
   generic method (Game/GameShapes.cs) are patched and restored as well
   (HotfixShapes.cs).

The default is the asmdef layout (xLua in XLua.dll); --package installs xLua
as the UPM package (also XLua.dll). In both, Game.asmdef references XLua and
XLua.Core, Game.Shop is configured with [assembly: Hotfix(typeof(Game.Shop),
HotfixFlag.IntKey)] in Game (Game/GameHotfix.cs), and each assembly uses its
own IntKey slots (milestone M7): no __Hotfix fields, and id maps that name
their assembly. --no-asmdef puts xLua and the generator into Assembly-CSharp
(the generator in Assets/XLua/Analyzers, run in every assembly); Game cannot
reference Assembly-CSharp, so an Editor list marks Game.Shop HotfixFlag.Inline
(NoAsmdef/HotfixIntKeyInline.cs, define XLUA_TEST_HOTFIX_GAME_INLINE): Game.Shop
in field mode, HotfixIntKeyLocal through Assembly-CSharp's slots.

Before M11 the fixture's Editor list configured both types for Generate
Code's bridges, and Game.Shop fell back to field mode with a warning; a type
only a list names is now refused unless it is Inline (hotfix_check.py checks
the refusal on the host).

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

from unity_adb import unity_env

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
p.add_argument("--no-asmdef", action="store_true",
               help="the layout without asmdefs (prepare.py --no-asmdef): xLua in Assembly-CSharp")
p.add_argument("--asmdef", action="store_true", help="the asmdef layout, the default; accepted for older command lines")
p.add_argument("--package", action="store_true",
               help="xLua as the UPM package (prepare.py --package): in XLua.dll, as in the asmdef layout")
p.add_argument("--csharp12", action="store_true",
               help="compile scripts with -langversion:12 and add HotfixIntKeyLocal's ref readonly methods "
                    "(define XLUA_TEST_HOTFIX_CSHARP12); needs a Roslyn that knows C# 12, such as Unity 6000.6's")
p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
               help="where Tools/build_source_generator.py builds the source generator (default: <out>/source-generator)")
a = p.parse_args()
if sum([a.asmdef, a.package, a.no_asmdef]) > 1:
    p.error("--asmdef, --no-asmdef and --package are three layouts; pass one")
a.project, a.out = a.project.resolve(), a.out.resolve()
a.source_gen = (a.source_gen or a.out / "source-generator").resolve()
a.out.mkdir(parents=True, exist_ok=True)
for name in TOOLS:
    if not (a.tools / name).is_file():
        raise SystemExit("missing %s; run python3 Tools/build_tools.py first" % (a.tools / name))

subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools", "--hotfix"]
               + ["--source-gen", str(a.source_gen)]
               + (["--no-asmdef", "--define", "XLUA_TEST_HOTFIX_GAME_INLINE"] if a.no_asmdef else [])
               + (["--package"] if a.package else [])
               + (["--langversion", "12", "--define", "XLUA_TEST_HOTFIX_CSHARP12"] if a.csharp12 else []), check=True)
(a.project / "Tools").mkdir()
for name in TOOLS:
    shutil.copy2(a.tools / name, a.project / "Tools" / name)
fixture = here / "HotfixIntKey"
(a.project / "Assets/Game").mkdir()
shutil.copy2(fixture / "Game/Shop.cs", a.project / "Assets/Game/Shop.cs")
if a.no_asmdef:
    shutil.copy2(fixture / "NoAsmdef/Game.asmdef", a.project / "Assets/Game/Game.asmdef")
    shutil.copy2(fixture / "NoAsmdef/HotfixIntKeyInline.cs", a.project / "Assets/Editor/HotfixIntKeyInline.cs")
else:
    shutil.copy2(fixture / "Game/Game.asmdef", a.project / "Assets/Game/Game.asmdef")
    shutil.copy2(fixture / "Game/GameHotfix.cs", a.project / "Assets/Game/GameHotfix.cs")
    shutil.copy2(fixture / "Game/GameShapes.cs", a.project / "Assets/Game/GameShapes.cs")
shutil.copy2(fixture / "HotfixIntKeyLocal.cs", a.project / "Assets/HotfixIntKeyLocal.cs")
shutil.copy2(fixture / "LocalHotfix.cs", a.project / "Assets/LocalHotfix.cs")
shutil.copy2(fixture / "HotfixShapes.cs", a.project / "Assets/HotfixShapes.cs")
shutil.copy2(fixture / "HotfixIntKeySteps.cs", a.project / "Assets/Editor/HotfixIntKeySteps.cs")
shutil.copy2(fixture / "HotfixIntKeyTests.cs", a.project / "Assets/Tests/Editor/HotfixIntKeyTests.cs")


def unity(name, args):
    log = a.out / (name + ".log")
    cmd = [str(a.unity), "-batchmode", "-nographics", "-projectPath", str(a.project), "-logFile", str(log)] + args
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True, env=unity_env())
    try:
        code = proc.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        raise SystemExit("XLUA_HOTFIX_INTKEY_TIMEOUT after %ds: %s (log %s)" % (a.timeout, name, log))
    print("  exit %d in %.0fs" % (code, time.time() - start), flush=True)
    return code, log.read_text(errors="replace") if log.exists() else ""


code, text = unity("validate", ["-executeMethod", "HotfixIntKeySteps.Validate", "-quit"])
lines = text.splitlines()
problems = [l for l in lines if l.startswith("XLUA_VALIDATE_PROBLEM:")]
if code or not any(l.startswith("XLUA_VALIDATE_DONE:") for l in lines):
    raise SystemExit("XLUA_HOTFIX_INTKEY_FAIL: Validate Setup did not finish; see %s" % (a.out / "validate.log"))
# Without asmdefs Validate Setup reports the Inline list as one the source generator does not read,
# which is true; the injector still reads it, and Game cannot carry the attribute form.
expected = [l for l in problems if a.no_asmdef and "Configuration list: HotfixIntKeyInline.Types ([Hotfix])" in l]
for line in expected:
    print("Validate Setup (expected without asmdefs): " + line, flush=True)
problems = [l for l in problems if l not in expected]
if problems:
    raise SystemExit("XLUA_HOTFIX_INTKEY_FAIL: Validate Setup reported:\n  " + "\n  ".join(problems))
print("Validate Setup: no other problems" if expected else "Validate Setup: no problems", flush=True)

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
print("XLUA_HOTFIX_INTKEY_PASS (%s, %s layout, source generator%s%s)" % (
    a.unity_version, "package" if a.package else "Assets" if a.no_asmdef else "asmdef",
    ", Game.Shop Inline" if a.no_asmdef else "", ", C# 12 ref readonly" if a.csharp12 else ""))
