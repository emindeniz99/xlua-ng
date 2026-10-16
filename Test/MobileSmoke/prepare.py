"""Prepare an isolated Unity smoke project without copying upstream binaries."""
import argparse
import json
from pathlib import Path
import shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--unity-version", default="6000.3.21f1")
parser.add_argument("--lua-version", choices=["5.3", "5.4", "5.5"], default="5.5")
parser.add_argument("--macos-plugin", type=Path)
parser.add_argument("--ios-plugin", type=Path)
parser.add_argument("--android-plugin", type=Path)
parser.add_argument("--webgl", action="store_true",
                    help="copy the WebGL plugin source; Unity compiles it with Emscripten (Lua 5.5 only)")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
output = args.output.resolve()
if output.exists() and any(output.iterdir()):
    parser.error("Output must be empty; existing projects are never overwritten")
plugins = [(relative, source) for relative, source in
           [("arm64/libxlua.dylib", args.macos_plugin),
            ("iOS/libxlua.a", args.ios_plugin),
            ("Android/libs/arm64-v8a/libxlua.so", args.android_plugin)] if source]
if not plugins and not args.webgl:
    parser.error("Pass at least one rebuilt plugin or --webgl")
if args.webgl and args.lua_version != "5.5":
    parser.error("WebGL compiles Lua 5.5.1 only")
for _, source in plugins:
    if not source.is_file():
        parser.error("Missing rebuilt plugin: " + str(source))
for name in ["Assets/Editor", "Assets/XLua", "Packages", "ProjectSettings"]:
    (output / name).mkdir(parents=True, exist_ok=True)
(output / "Packages/manifest.json").write_text(json.dumps({"dependencies": {"com.unity.modules.unitywebrequestwww": "1.0.0"}}) + "\n")
(output / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + args.unity_version + "\n")
(output / "Assets/csc.rsp").write_text("-unsafe\n" +
    ("-define:XLUA_SMOKE_LUA53\n" if args.lua_version == "5.3" else
     "-define:XLUA_SMOKE_LUA55\n" if args.lua_version == "5.5" else ""))
for source in (repo / "Assets/XLua/Src").glob("*.cs"):
    shutil.copy2(source, output / "Assets/XLua" / source.name)
shutil.copytree(repo / "Assets/XLua/Src/TemplateEngine", output / "Assets/XLua/TemplateEngine")
for relative, source in plugins:
    dest = output / "Assets/Plugins" / relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, dest)
    shutil.copy2(repo / "Assets/Plugins" / (relative + ".meta"), str(dest) + ".meta")
if args.ios_plugin:
    for name in ["HotfixFlags.cpp", "HotfixFlags.cpp.meta"]:
        shutil.copy2(repo / "Assets/Plugins/iOS" / name, output / "Assets/Plugins/iOS" / name)
if args.webgl:
    # xlua_webgl.cpp includes ../../../build/... relative to itself, so the project gets
    # the same build/ layout: the Lua 5.5.1 sources and the bridge sources.
    (output / "Assets/Plugins/WebGL").mkdir(parents=True, exist_ok=True)
    for name in ["xlua_webgl.cpp", "xlua_webgl.cpp.meta"]:
        shutil.copy2(repo / "Assets/Plugins/WebGL" / name, output / "Assets/Plugins/WebGL" / name)
    shutil.copytree(repo / "build/lua-5.5.1/src", output / "build/lua-5.5.1/src")
    for name in ["xlua.c", "i64lib.c", "i64lib.h", "memory_leak_checker.c"]:
        shutil.copy2(repo / "build" / name, output / "build" / name)
    # Unity 6000.6.4f1 strips ContentLoadingSystem.ProcessResults from this minimal project,
    # and the WebGL player halts with "null function" before the first scene starts.
    (output / "Assets/link.xml").write_text(
        '<linker>\n  <assembly fullname="UnityEngine.ContentLoadModule" preserve="all"/>\n</linker>\n')
shutil.copy2(Path(__file__).with_name("XluaSmoke.cs"), output / "Assets/XluaSmoke.cs")
shutil.copy2(Path(__file__).with_name("SmokeBuild.cs"), output / "Assets/Editor/SmokeBuild.cs")
print(output)
