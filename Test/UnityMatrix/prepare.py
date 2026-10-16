"""Prepare a disposable Unity project for the real-editor xLua smoke test."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--plugin", required=True, type=Path)
parser.add_argument("--unity-version", required=True)
parser.add_argument("--test-framework", default="1.4.6")
parser.add_argument("--platform", choices=["linux", "macos"], default="linux")
parser.add_argument("--editor-tools", action="store_true",
                    help="also copy the xLua editor tools and the tests for code and LuaCATS "
                         "generation; run them with editor_tests.py")
parser.add_argument("--csharp12", action="store_true",
                    help="with --editor-tools: compile with -langversion:12 and add the C# 12 "
                         "ref readonly fixture and tests (needs a Roslyn that knows C# 12, "
                         "such as Unity 6000.6's)")
parser.add_argument("--roslyn-sdk", type=Path,
                    help="with --editor-tools: a .NET SDK whose Roslyn knows C# 12 (Unity 6000.6's "
                         "Contents/Resources/Scripting/DotNetSdk) that compiles XluaRefReadonlyLib.cs "
                         "into a precompiled DLL, and adds the tests for it; without it they are left out")
parser.add_argument("--hotfix", action="store_true",
                    help="compile the project with HOTFIX_ENABLE, so the editor's hotfix code is built too")
parser.add_argument("--fast-enter-play-mode", action="store_true",
                    help="with --editor-tools: add PlayModeHost and the tests that enter and exit "
                         "play mode with Enter Play Mode Options (domain reload disabled, with and "
                         "without scene reload), with the default domain reload and across a "
                         "script recompile")
parser.add_argument("--langversion",
                    help="with --editor-tools: compile scripts with -langversion:VALUE (such as latest, "
                         "or 10 for Roslyn 4.3) so generated code is checked under newer language rules; "
                         "with --csharp12 it replaces 12 and must allow C# 12")
args = parser.parse_args()
if args.fast_enter_play_mode and not args.editor_tools:
    parser.error("--fast-enter-play-mode needs --editor-tools")
if args.csharp12 and not args.editor_tools:
    parser.error("--csharp12 needs --editor-tools")
if args.roslyn_sdk and not args.editor_tools:
    parser.error("--roslyn-sdk needs --editor-tools")
if args.langversion is not None:
    if not args.editor_tools:
        parser.error("--langversion needs --editor-tools")
    if args.csharp12 and not (args.langversion in ("latest", "preview")
                              or args.langversion.replace(".", "", 1).isdigit() and float(args.langversion) >= 12):
        parser.error("--csharp12 needs a --langversion of 12 or later")


def build_ref_readonly_lib(sdk, out_dll):
    """Compile XluaRefReadonlyLib.cs at C# 12 against .NET Standard 2.1."""
    dotnet = sdk / "dotnet"
    cscs = sorted(sdk.glob("sdk/*/Roslyn/bincore/csc.dll"))
    netstandard = sdk / "packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1/netstandard.dll"
    if not dotnet.is_file() or not cscs or not netstandard.is_file():
        parser.error("--roslyn-sdk needs dotnet, sdk/*/Roslyn/bincore/csc.dll and the "
                     "NETStandard.Library.Ref 2.1.0 pack in " + str(sdk))
    out_dll.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(dotnet), str(cscs[-1]), "-nologo", "-noconfig", "-nostdlib", "-langversion:12",
                    "-target:library", "-r:" + str(netstandard), "-out:" + str(out_dll),
                    str(repo / "Test/UnityMatrix/XluaRefReadonlyLib.cs")], check=True)

repo = Path(__file__).resolve().parents[2]
output = args.output.resolve()
if output.exists() and any(output.iterdir()):
    parser.error("Output must be empty; existing projects are never overwritten")
if not args.plugin.is_file():
    parser.error("Missing rebuilt plugin: " + str(args.plugin))

for folder in ["Assets/Editor", "Assets/Tests/Editor", "Assets/XLua",
               "Packages", "ProjectSettings"]:
    (output / folder).mkdir(parents=True, exist_ok=True)

for source in (repo / "Assets/XLua/Src").glob("*.cs"):
    shutil.copy2(source, output / "Assets/XLua" / source.name)
shutil.copytree(repo / "Assets/XLua/Src/TemplateEngine",
                output / "Assets/XLua/TemplateEngine")

if args.platform == "linux":
    plugin = output / "Assets/Plugins/x86_64/libxlua.so"
    metadata = repo / "Assets/Plugins/x86_64/libxlua.so.meta"
else:
    plugin = output / "Assets/Plugins/arm64/libxlua.dylib"
    metadata = repo / "Assets/Plugins/arm64/libxlua.dylib.meta"
plugin.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(args.plugin, plugin)
shutil.copy2(metadata, str(plugin) + ".meta")

shutil.copy2(repo / "Test/MobileSmoke/XluaSmoke.cs", output / "Assets/XluaSmoke.cs")
shutil.copy2(repo / "Test/MobileSmoke/SmokeBuild.cs", output / "Assets/Editor/SmokeBuild.cs")
shutil.copy2(repo / "Test/UnityMatrix/XluaSmokeTests.cs",
            output / "Assets/Tests/Editor/XluaSmokeTests.cs")
if args.editor_tools:
    # Keep the .meta files: TemplateRef.cs.meta references the templates by GUID.
    shutil.copytree(repo / "Assets/XLua/Src/Editor", output / "Assets/XLua/Editor")
    for name in ["XluaEditorToolsTests.cs", "XluaGeneratedCodeTests.cs", "XluaStringTests.cs"]:
        shutil.copy2(repo / "Test/UnityMatrix" / name, output / "Assets/Tests/Editor" / name)
    # Steps for editor_tests.py, and the types that generated wrappers cover.
    shutil.copy2(repo / "Test/UnityMatrix/XluaEditorTestSteps.cs",
                 output / "Assets/Editor/XluaEditorTestSteps.cs")
    shutil.copy2(repo / "Test/UnityMatrix/XluaEditorTestTarget.cs",
                 output / "Assets/XluaEditorTestTarget.cs")
if args.fast_enter_play_mode:
    # editor_tests.py requires the play mode tests when the host is present.
    shutil.copy2(repo / "Test/UnityMatrix/XluaPlayModeHost.cs", output / "Assets/XluaPlayModeHost.cs")
    shutil.copy2(repo / "Test/UnityMatrix/XluaPlayModeTests.cs",
                 output / "Assets/Tests/Editor/XluaPlayModeTests.cs")
csc = "-unsafe\n-define:XLUA_SMOKE_LUA55\n"
if args.hotfix:
    csc += "-define:HOTFIX_ENABLE\n"
if args.csharp12:
    # editor_tests.py requires the ref readonly tests when the fixture is present.
    shutil.copy2(repo / "Test/UnityMatrix/XluaRefReadonlyTarget.cs",
                 output / "Assets/XluaRefReadonlyTarget.cs")
    shutil.copy2(repo / "Test/UnityMatrix/XluaRefReadonlyTests.cs",
                 output / "Assets/Tests/Editor/XluaRefReadonlyTests.cs")
    if args.langversion is None:
        csc += "-langversion:12\n"
if args.langversion is not None:
    csc += "-langversion:" + args.langversion + "\n"
if args.roslyn_sdk:
    # editor_tests.py requires the tests for the DLL when it is present.
    build_ref_readonly_lib(args.roslyn_sdk.resolve(), output / "Assets/XluaRefReadonlyLib/XluaRefReadonlyLib.dll")
    shutil.copy2(repo / "Test/UnityMatrix/XluaRefReadonlyLibConfig.cs",
                 output / "Assets/XluaRefReadonlyLibConfig.cs")
    shutil.copy2(repo / "Test/UnityMatrix/XluaRefReadonlyLibTests.cs",
                 output / "Assets/Tests/Editor/XluaRefReadonlyLibTests.cs")
(output / "Assets/csc.rsp").write_text(csc)
(output / "ProjectSettings/ProjectVersion.txt").write_text(
    "m_EditorVersion: " + args.unity_version + "\n")
(output / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
    "com.unity.test-framework": args.test_framework,
    "com.unity.modules.jsonserialize": "1.0.0"
}}, indent=2) + "\n")
print(output)
