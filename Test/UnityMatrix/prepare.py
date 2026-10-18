"""Prepare a disposable Unity project for the real-editor xLua smoke test."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

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
parser.add_argument("--asmdef", action="store_true",
                    help="also copy the opt-in XLua.asmdef (and, with --editor-tools, XLua.Editor.asmdef) from "
                         "Assets/XLua/Asmdef~, so that xLua "
                         "compiles into its own assemblies instead of Assembly-CSharp; xLua then goes to "
                         "Assets/XLua/Src as in the repository, so that generated code in Assets/XLua/Gen "
                         "stays outside the XLua asmdef (docs/design/source-generator.md, milestone M1)")
parser.add_argument("--package", nargs="?", const="tarball", choices=["tarball", "folder"],
                    help="install xLua as the UPM package of Tools/make_upm_package.py (Runtime with "
                         "XLua.asmdef, Editor with XLua.Editor.asmdef, the plugin in Plugins) through "
                         "Packages/manifest.json instead of copying it into Assets: 'tarball' (the default) "
                         "is a file: .tgz, read-only like a registry install; 'folder' a file: folder. The "
                         "package goes to LocalPackages/ in the project folder, outside Assets")
parser.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
                    help="with --editor-tools and --asmdef or --package: build the xLua source generator into BUILD_DIR "
                         "(Tools/build_source_generator.py), install it in Assets/XLua/Src/Analyzers (with --package: "
                         "the package's Runtime/Analyzers) and add "
                         "the fixture in Test/UnityMatrix/SourceGen (Assembly-CSharp and an asmdef SgGame) "
                         "and its tests; editor_tests.py then runs the tests once, without Generate Code")
parser.add_argument("--hotfix", action="store_true",
                    help="compile the project with HOTFIX_ENABLE, so the editor's hotfix code is built too")
parser.add_argument("--no-streaming-assets-loader", action="store_true",
                    help="compile the project with XLUA_NO_STREAMING_ASSETS_LOADER, which leaves out "
                         "the StreamingAssets searcher")
parser.add_argument("--define", action="append", default=[],
                    help="add a scripting define to Assets/csc.rsp, such as XLUA_NO_UTIL or "
                         "XLUA_NO_SIGNATURE_LOADER (repeatable); XluaSmokeTests.FeatureDefinesInTheEditor "
                         "checks the C# feature defines")
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
if args.package and args.asmdef:
    parser.error("--package and --asmdef are two layouts; pass one (the package has the asmdefs)")
if args.source_gen and not (args.editor_tools and (args.asmdef or args.package)):
    parser.error("--source-gen needs --editor-tools and --asmdef or --package (Unity scopes the generator "
                 "to the XLua asmdef)")
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

# The Assets layout keeps the historical Assets/XLua/*.cs. The asmdef layout
# needs xLua in Assets/XLua/Src, as in the repository: an XLua.asmdef in
# Assets/XLua would also take Assets/XLua/Gen, and generated code there
# cannot see the project types it wraps (CS0246 in 2021.3.45f2, 2026-10-18).
xlua_dir = output / ("Assets/XLua/Src" if args.asmdef else "Assets/XLua")
for folder in ["Assets/Editor", "Assets/Tests/Editor", "Packages", "ProjectSettings"]:
    (output / folder).mkdir(parents=True, exist_ok=True)
plugin_rel = "x86_64/libxlua.so" if args.platform == "linux" else "arm64/libxlua.dylib"
package_dependency = {}
if args.package:
    # xLua, its Editor code, the plugin and (with --source-gen) the generator come from the package.
    sys.path.insert(0, str(repo / "Tools"))
    import make_upm_package  # noqa: E402
    local = output / "LocalPackages"
    folder = local / make_upm_package.DEFAULT_NAME
    tgz = local / ("%s-%s.tgz" % (make_upm_package.DEFAULT_NAME, make_upm_package.DEFAULT_VERSION))
    cmd = [sys.executable, str(repo / "Tools/make_upm_package.py"), "--output", str(folder),
           "--plugin", plugin_rel + "=" + str(args.plugin.resolve())]
    if args.source_gen:
        cmd += ["--source-gen", str(args.source_gen.resolve())]
    if args.package == "tarball":
        cmd += ["--tarball", str(tgz)]
    subprocess.run(cmd, check=True)
    if args.package == "tarball":
        shutil.rmtree(folder)
    package_dependency[make_upm_package.DEFAULT_NAME] = "file:../LocalPackages/" + (
        tgz.name if args.package == "tarball" else folder.name)
else:
    xlua_dir.mkdir(parents=True, exist_ok=True)
    for source in (repo / "Assets/XLua/Src").glob("*.cs"):
        shutil.copy2(source, xlua_dir / source.name)
    shutil.copytree(repo / "Assets/XLua/Src/TemplateEngine", xlua_dir / "TemplateEngine")
# The opt-in asmdefs live in Assets/XLua/Asmdef~ (Unity skips folders ending in ~).
asmdefs = repo / "Assets/XLua/Asmdef~/Src"
if args.asmdef:
    for name in ["XLua.asmdef", "XLua.asmdef.meta"]:
        shutil.copy2(asmdefs / name, xlua_dir / name)

if not args.package:
    plugin = output / "Assets/Plugins" / plugin_rel
    plugin.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(args.plugin, plugin)
    shutil.copy2(repo / "Assets/Plugins" / (plugin_rel + ".meta"), str(plugin) + ".meta")

shutil.copy2(repo / "Test/MobileSmoke/XluaSmoke.cs", output / "Assets/XluaSmoke.cs")
shutil.copy2(repo / "Test/MobileSmoke/SmokeBuild.cs", output / "Assets/Editor/SmokeBuild.cs")
shutil.copy2(repo / "Test/UnityMatrix/XluaSmokeTests.cs",
            output / "Assets/Tests/Editor/XluaSmokeTests.cs")
if args.editor_tools:
    # Keep the .meta files: TemplateRef.cs.meta references the templates by GUID.
    # Without --asmdef the editor tools compile into Assembly-CSharp-Editor, as before.
    if not args.package:
        shutil.copytree(repo / "Assets/XLua/Src/Editor", xlua_dir / "Editor")
    if args.asmdef:
        for name in ["XLua.Editor.asmdef", "XLua.Editor.asmdef.meta"]:
            shutil.copy2(asmdefs / "Editor" / name, xlua_dir / "Editor" / name)
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
if args.no_streaming_assets_loader:
    csc += "-define:XLUA_NO_STREAMING_ASSETS_LOADER\n"
for define in args.define:
    csc += "-define:" + define + "\n"
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
if args.source_gen:
    # editor_tests.py requires XluaSourceGenTests when XluaSourceGenLocal.cs is present.
    if not args.package:
        subprocess.run([sys.executable, str(repo / "Tools/build_source_generator.py"), "--output",
                        str(args.source_gen.resolve()), "--install", str(xlua_dir / "Analyzers")], check=True)
    fixture = repo / "Test/UnityMatrix/SourceGen"
    shutil.copytree(fixture / "SgGame", output / "Assets/SgGame")
    shutil.copy2(fixture / "XluaSourceGenLocal.cs", output / "Assets/XluaSourceGenLocal.cs")
    shutil.copy2(fixture / "XluaSourceGenTests.cs", output / "Assets/Tests/Editor/XluaSourceGenTests.cs")
    # The suite's configuration lists in the attribute forms, so the generator binds them.
    shutil.copy2(fixture / "XluaSourceGenSuiteConfig.cs", output / "Assets/XluaSourceGenSuiteConfig.cs")
    if args.fast_enter_play_mode:
        shutil.copy2(fixture / "XluaSourceGenPlayModeConfig.cs", output / "Assets/XluaSourceGenPlayModeConfig.cs")
    if args.roslyn_sdk:
        shutil.copy2(fixture / "XluaSourceGenRefReadonlyConfig.cs", output / "Assets/XluaSourceGenRefReadonlyConfig.cs")
        shutil.copy2(fixture / "XluaSourceGenRefReadonlyTests.cs",
                     output / "Assets/Tests/Editor/XluaSourceGenRefReadonlyTests.cs")
(output / "Assets/csc.rsp").write_text(csc)
(output / "ProjectSettings/ProjectVersion.txt").write_text(
    "m_EditorVersion: " + args.unity_version + "\n")
(output / "Packages/manifest.json").write_text(json.dumps({"dependencies": dict({
    "com.unity.test-framework": args.test_framework,
    "com.unity.modules.jsonserialize": "1.0.0"
}, **package_dependency)}, indent=2) + "\n")
print(output)
