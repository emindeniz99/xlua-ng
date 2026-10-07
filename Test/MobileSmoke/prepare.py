"""Prepare an isolated Unity smoke project without copying upstream binaries."""
import argparse
import json
from pathlib import Path
import shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--unity-version", default="6000.3.21f1")
parser.add_argument("--macos-plugin", required=True, type=Path)
parser.add_argument("--ios-plugin", required=True, type=Path)
parser.add_argument("--android-plugin", required=True, type=Path)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
output = args.output.resolve()
if output.exists() and any(output.iterdir()):
    parser.error("Output must be empty; existing projects are never overwritten")
plugins = [("arm64/libxlua.dylib", args.macos_plugin),
           ("iOS/libxlua.a", args.ios_plugin),
           ("Android/libs/arm64-v8a/libxlua.so", args.android_plugin)]
for _, source in plugins:
    if not source.is_file():
        parser.error("Missing rebuilt plugin: " + str(source))
for name in ["Assets/Editor", "Assets/XLua", "Packages", "ProjectSettings"]:
    (output / name).mkdir(parents=True, exist_ok=True)
(output / "Packages/manifest.json").write_text(json.dumps({"dependencies": {"com.unity.modules.unitywebrequestwww": "1.0.0"}}) + "\n")
(output / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + args.unity_version + "\n")
(output / "Assets/csc.rsp").write_text("-unsafe\n")
for source in (repo / "Assets/XLua/Src").glob("*.cs"):
    shutil.copy2(source, output / "Assets/XLua" / source.name)
shutil.copytree(repo / "Assets/XLua/Src/TemplateEngine", output / "Assets/XLua/TemplateEngine")
for relative, source in plugins:
    dest = output / "Assets/Plugins" / relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, dest)
    shutil.copy2(repo / "Assets/Plugins" / (relative + ".meta"), str(dest) + ".meta")
for name in ["HotfixFlags.cpp", "HotfixFlags.cpp.meta"]:
    shutil.copy2(repo / "Assets/Plugins/iOS" / name, output / "Assets/Plugins/iOS" / name)
shutil.copy2(Path(__file__).with_name("XluaSmoke.cs"), output / "Assets/XluaSmoke.cs")
shutil.copy2(Path(__file__).with_name("SmokeBuild.cs"), output / "Assets/Editor/SmokeBuild.cs")
print(output)
