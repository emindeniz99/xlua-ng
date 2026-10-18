"""Prepare an isolated Unity smoke project without copying upstream binaries."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--unity-version", default="6000.3.21f1")
parser.add_argument("--lua-version", choices=["5.3", "5.4", "5.5"], default="5.5")
parser.add_argument("--macos-plugin", type=Path)
parser.add_argument("--ios-plugin", type=Path)
parser.add_argument("--android-plugin", type=Path)
parser.add_argument("--webgl", action="store_true",
                    help="copy the WebGL plugin source; Unity compiles it with Emscripten (Lua 5.5 only)")
parser.add_argument("--gen", action="store_true",
                    help="copy the xLua Editor code and SmokeGenConfig.cs and define XLUA_SMOKE_GEN; run "
                         "CSObjectWrapEditor.Generator.GenAll before the build (needs --macos-plugin)")
parser.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
                    help="the opt-in asmdef layout (Assets/XLua/Src with XLua.asmdef, the Editor code with "
                         "XLua.Editor.asmdef), the source generator built into BUILD_DIR and installed in "
                         "Assets/XLua/Src/Analyzers (Tools/build_source_generator.py), and SgSmoke.cs with "
                         "XLUA_SMOKE_SOURCE_GEN; nothing to generate before the build")
parser.add_argument("--package", action="store_true",
                    help="xLua as the UPM package of Tools/make_upm_package.py (Runtime with XLua.asmdef, Editor "
                         "with XLua.Editor.asmdef, the plugins given, with --source-gen the generator in "
                         "Runtime/Analyzers), installed from a file: tarball in LocalPackages/ through "
                         "Packages/manifest.json instead of being copied into Assets")
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
if args.gen and not args.macos_plugin:
    parser.error("--gen runs XLua/Generate Code in the Editor, which needs --macos-plugin")
if args.gen and args.source_gen:
    parser.error("--gen and --source-gen are two ways to generate the bindings; pass one")
if args.package and args.webgl:
    parser.error("--webgl is not supported with --package yet (the package's WebGL source was never built)")
if args.webgl and args.lua_version != "5.5":
    parser.error("WebGL compiles Lua 5.5.1 only")
for _, source in plugins:
    if not source.is_file():
        parser.error("Missing rebuilt plugin: " + str(source))
for name in ["Assets/Editor", "Assets/XLua", "Packages", "ProjectSettings"]:
    (output / name).mkdir(parents=True, exist_ok=True)
dependencies = {"com.unity.modules.unitywebrequestwww": "1.0.0"}
if args.package:
    # xLua, its Editor code, the plugins and (with --source-gen) the generator come from the package.
    sys.path.insert(0, str(repo / "Tools"))
    import make_upm_package  # noqa: E402
    local = output / "LocalPackages"
    tgz = local / ("%s-%s.tgz" % (make_upm_package.DEFAULT_NAME, make_upm_package.DEFAULT_VERSION))
    cmd = [sys.executable, str(repo / "Tools/make_upm_package.py"), "--output", str(local / "package"),
           "--tarball", str(tgz), "--no-samples"]
    for relative, source in plugins:
        cmd += ["--plugin", relative + "=" + str(source.resolve())]
    if args.source_gen:
        cmd += ["--source-gen", str(args.source_gen.resolve())]
    subprocess.run(cmd, check=True)
    shutil.rmtree(local / "package")
    dependencies[make_upm_package.DEFAULT_NAME] = "file:../LocalPackages/" + tgz.name
(output / "Packages/manifest.json").write_text(json.dumps({"dependencies": dependencies}) + "\n")
(output / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + args.unity_version + "\n")
(output / "Assets/csc.rsp").write_text("-unsafe\n" +
    ("-define:XLUA_SMOKE_LUA53\n" if args.lua_version == "5.3" else
     "-define:XLUA_SMOKE_LUA55\n" if args.lua_version == "5.5" else "") +
    ("-define:XLUA_SMOKE_GEN\n" if args.gen else "") +
    ("-define:XLUA_SMOKE_SOURCE_GEN\n" if args.source_gen else ""))
# The source generator runs in the assemblies that reference the XLua asmdef, so
# --source-gen uses the asmdef layout: xLua in Assets/XLua/Src, as in the repository.
xlua_dir = output / ("Assets/XLua/Src" if args.source_gen else "Assets/XLua")
if not args.package:
    xlua_dir.mkdir(parents=True, exist_ok=True)
    for source in (repo / "Assets/XLua/Src").glob("*.cs"):
        shutil.copy2(source, xlua_dir / source.name)
    shutil.copytree(repo / "Assets/XLua/Src/TemplateEngine", xlua_dir / "TemplateEngine")
    # XLua.Core (Assets/XLua/Core): next to the bridge in the Assets layout, in
    # Assets/XLua/Core with its asmdef in the asmdef layout.
    core_dir = output / "Assets/XLua/Core" if args.source_gen else xlua_dir
    core_dir.mkdir(parents=True, exist_ok=True)
    for source in (repo / "Assets/XLua/Core").glob("*.cs"):
        shutil.copy2(source, core_dir / source.name)
if (args.gen or args.source_gen) and not args.package:
    # The generator, its templates and their .meta files (TemplateRef finds the templates by GUID);
    # with the source generator, the build checks and the link.xml processor.
    shutil.copytree(repo / "Assets/XLua/Src/Editor", xlua_dir / "Editor")
    core_editor = (output / "Assets/XLua/Core" if args.source_gen else xlua_dir) / "Editor"
    core_editor.mkdir(parents=True, exist_ok=True)
    for source in (repo / "Assets/XLua/Core/Editor").glob("*.cs"):
        shutil.copy2(source, core_editor / source.name)
if args.gen:
    shutil.copy2(Path(__file__).with_name("SmokeGenConfig.cs"), output / "Assets/Editor/SmokeGenConfig.cs")
if args.source_gen and not args.package:
    asmdefs = repo / "Assets/XLua/Asmdef~/Src"
    for name in ["XLua.asmdef", "XLua.asmdef.meta"]:
        shutil.copy2(asmdefs / name, xlua_dir / name)
    for name in ["XLua.Editor.asmdef", "XLua.Editor.asmdef.meta"]:
        shutil.copy2(asmdefs / "Editor" / name, xlua_dir / "Editor" / name)
    core_asmdefs = repo / "Assets/XLua/Asmdef~/Core"
    for name in ["XLua.Core.asmdef", "XLua.Core.asmdef.meta"]:
        shutil.copy2(core_asmdefs / name, output / "Assets/XLua/Core" / name)
    for name in ["XLua.Core.Editor.asmdef", "XLua.Core.Editor.asmdef.meta"]:
        shutil.copy2(core_asmdefs / "Editor" / name, output / "Assets/XLua/Core/Editor" / name)
    subprocess.run([sys.executable, str(repo / "Tools/build_source_generator.py"), "--output", str(args.source_gen.resolve()),
                    "--install", str(xlua_dir / "Analyzers")], check=True)
if args.source_gen:
    shutil.copy2(Path(__file__).with_name("SgSmoke.cs"), output / "Assets/SgSmoke.cs")
for relative, source in ([] if args.package else plugins):
    dest = output / "Assets/Plugins" / relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, dest)
    shutil.copy2(repo / "Assets/Plugins" / (relative + ".meta"), str(dest) + ".meta")
if args.ios_plugin and not args.package:
    for name in ["HotfixFlags.cpp", "HotfixFlags.cpp.meta"]:
        shutil.copy2(repo / "Assets/Plugins/iOS" / name, output / "Assets/Plugins/iOS" / name)
if args.webgl:
    # xlua_webgl.cpp includes ../../../build/... relative to itself, so the project gets
    # the same build/ layout: the Lua 5.5.1 sources and the bridge sources.
    (output / "Assets/Plugins/WebGL").mkdir(parents=True, exist_ok=True)
    for name in ["xlua_webgl.cpp", "xlua_webgl.cpp.meta"]:
        shutil.copy2(repo / "Assets/Plugins/WebGL" / name, output / "Assets/Plugins/WebGL" / name)
    shutil.copytree(repo / "build/lua-5.5.1/src", output / "build/lua-5.5.1/src")
    for name in ["xlua_core.c", "xlua_core_limits.c", "xlua_core.h", "xlua_luahooks.h", "xlua.c", "i64lib.c", "i64lib.h", "memory_leak_checker.c"]:
        shutil.copy2(repo / "build" / name, output / "build" / name)
    # Unity 6000.6.4f1 strips ContentLoadingSystem.ProcessResults from this minimal project,
    # and the WebGL player halts with "null function" before the first scene starts.
    (output / "Assets/link.xml").write_text(
        '<linker>\n  <assembly fullname="UnityEngine.ContentLoadModule" preserve="all"/>\n</linker>\n')
shutil.copy2(Path(__file__).with_name("XluaSmoke.cs"), output / "Assets/XluaSmoke.cs")
shutil.copy2(Path(__file__).with_name("SmokeBuild.cs"), output / "Assets/Editor/SmokeBuild.cs")
print(output)
