"""Prepare a disposable Unity project for the real-editor xLua smoke test."""
import argparse
import json
from pathlib import Path
import shutil

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
args = parser.parse_args()
if args.csharp12 and not args.editor_tools:
    parser.error("--csharp12 needs --editor-tools")

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
    for name in ["XluaEditorToolsTests.cs", "XluaGeneratedCodeTests.cs"]:
        shutil.copy2(repo / "Test/UnityMatrix" / name, output / "Assets/Tests/Editor" / name)
    # Steps for editor_tests.py, and the types that generated wrappers cover.
    shutil.copy2(repo / "Test/UnityMatrix/XluaEditorTestSteps.cs",
                 output / "Assets/Editor/XluaEditorTestSteps.cs")
    shutil.copy2(repo / "Test/UnityMatrix/XluaEditorTestTarget.cs",
                 output / "Assets/XluaEditorTestTarget.cs")
csc = "-unsafe\n-define:XLUA_SMOKE_LUA55\n"
if args.csharp12:
    # editor_tests.py requires the ref readonly tests when the fixture is present.
    shutil.copy2(repo / "Test/UnityMatrix/XluaRefReadonlyTarget.cs",
                 output / "Assets/XluaRefReadonlyTarget.cs")
    shutil.copy2(repo / "Test/UnityMatrix/XluaRefReadonlyTests.cs",
                 output / "Assets/Tests/Editor/XluaRefReadonlyTests.cs")
    csc += "-langversion:12\n"
(output / "Assets/csc.rsp").write_text(csc)
(output / "ProjectSettings/ProjectVersion.txt").write_text(
    "m_EditorVersion: " + args.unity_version + "\n")
(output / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
    "com.unity.test-framework": args.test_framework,
    "com.unity.modules.jsonserialize": "1.0.0"
}}, indent=2) + "\n")
print(output)
