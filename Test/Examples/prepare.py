"""Prepare an isolated Android project containing four upstream xLua scenes.

The bindings come from the xLua source generator (Tools/build_source_generator.py
builds it into --source-gen and installs it in Assets/XLua/Src/Analyzers);
ExampleConfig.cs configures the Unity types with assembly attributes. Until
milestone M11 a separate editor launch ran Generate Code before the build.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--macos-plugin", required=True, type=Path)
parser.add_argument("--android-plugin", required=True, type=Path)
parser.add_argument("--unity-version", default="6000.3.27f2")
parser.add_argument("--source-gen", required=True, type=Path, metavar="BUILD_DIR",
                    help="where Tools/build_source_generator.py builds the source generator")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
output = args.output.resolve()
if output.exists() and any(output.iterdir()):
    parser.error("Output must be empty; existing projects are never overwritten")
for plugin in [args.macos_plugin, args.android_plugin]:
    if not plugin.is_file():
        parser.error("Missing rebuilt plugin: " + str(plugin))
for folder in ["Assets/Editor", "Packages", "ProjectSettings"]:
    (output / folder).mkdir(parents=True, exist_ok=True)
shutil.copytree(repo / "Assets/XLua/Core", output / "Assets/XLua/Core")
shutil.copytree(repo / "Assets/XLua/Src", output / "Assets/XLua/Src")
subprocess.run([sys.executable, str(repo / "Tools/build_source_generator.py"), "--output", str(args.source_gen.resolve()),
                "--install", str(output / "Assets/XLua/Src/Analyzers")], check=True)
for relative in ["Tutorial/CSharpCallLua", "Tutorial/LuaCallCSharp",
                 "Examples/02_U3DScripting", "Examples/03_UIEvent"]:
    shutil.copytree(repo / "Assets/XLua" / relative, output / "Assets/XLua" / relative)
for relative, source in [("arm64/libxlua.dylib", args.macos_plugin),
                         ("Android/libs/arm64-v8a/libxlua.so", args.android_plugin)]:
    dest = output / "Assets/Plugins" / relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, dest)
    shutil.copy2(repo / "Assets/Plugins" / (relative + ".meta"), str(dest) + ".meta")
(output / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
    "com.unity.ugui": "2.0.0", "com.unity.modules.unitywebrequestwww": "1.0.0",
    "com.unity.modules.androidjni": "1.0.0"}}) + "\n")
(output / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + args.unity_version + "\n")
(output / "Assets/csc.rsp").write_text("-unsafe\n")
for name, dest in [("ExampleBuild.cs", "Assets/Editor/ExampleBuild.cs"),
                   ("ExampleConfig.cs", "Assets/ExampleConfig.cs"),
                   ("ExampleRunner.cs", "Assets/ExampleRunner.cs")]:
    shutil.copy2(Path(__file__).with_name(name), output / dest)
print(output)
