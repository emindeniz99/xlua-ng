"""Prepare an isolated Unity smoke project without copying upstream binaries."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--unity-version", default="6000.3.21f1")
parser.add_argument("--lua-version", choices=["5.3", "5.4", "5.5"], default="5.5")
parser.add_argument("--macos-plugin", type=Path)
parser.add_argument("--ios-plugin", type=Path)
parser.add_argument("--android-plugin", type=Path)
parser.add_argument("--android-minimal-plugin", type=Path,
                    help="the minimal preset's Android arm64-v8a library (XLUA_VARIANT=minimal XLUA_PRESET=minimal "
                         "build/make_android_lua55.sh), copied as the plugin variant "
                         "Plugins/Android/xlua-variants/minimal/libs/arm64-v8a/libxlua.so next to --android-plugin; "
                         "the plugin selector (Assets/XLua/Core/Editor/PluginVariants.cs) picks it when "
                         "XLUA_SMOKE_DEFINES contains XLUA_PRESET_MINIMAL")
parser.add_argument("--ios-minimal-plugin", type=Path,
                    help="the same for iOS: Plugins/iOS/xlua-variants/minimal/libxlua.a next to --ios-plugin")
parser.add_argument("--ios-simulator-plugin", type=Path,
                    help="the arm64 iOS Simulator library (XLUA_IOS_SDK=simulator build/make_ios_lua55.sh), copied as the "
                         "plugin variant Plugins/iOS/xlua-variants/simulator/libxlua.a next to --ios-plugin; the selector "
                         "takes it for a build with XLUA_SMOKE_IOS_SDK=SimulatorSDK (SmokeBuild.cs)")
parser.add_argument("--ios-minimal-simulator-plugin", type=Path,
                    help="the minimal preset's simulator library (XLUA_IOS_SDK=simulator XLUA_VARIANT=minimal "
                         "XLUA_PRESET=minimal), as Plugins/iOS/xlua-variants/minimal_simulator/libxlua.a")
parser.add_argument("--macos-minimal-plugin", type=Path,
                    help="the same for macOS players (SmokeBuild.BuildMac): Plugins/arm64/xlua-variants/minimal/"
                         "libxlua.dylib next to --macos-plugin; the Editor keeps loading --macos-plugin")
parser.add_argument("--webgl", action="store_true",
                    help="copy the WebGL plugin source (with --package: the package's Plugins/WebGL and build~); Unity "
                         "compiles it with Emscripten (Lua 5.5 only)")
parser.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
                    help="the source generator built into BUILD_DIR and installed in Assets/XLua/Src/Analyzers "
                         "(with --no-asmdef Assets/XLua/Analyzers, where Unity runs it in every assembly; with "
                         "--package the package's Runtime/Analyzers) by Tools/build_source_generator.py, the Editor "
                         "code, and SgSmoke.cs with XLUA_SMOKE_SOURCE_GEN; nothing to generate before the build. "
                         "The generated bindings come only from it since milestone M11 removed Generate Code "
                         "(--gen until then)")
parser.add_argument("--no-asmdef", action="store_true",
                    help="the layout without asmdefs (XLua > Project Layout > Use Assembly-CSharp): xLua compiles into "
                         "Assembly-CSharp from the historical Assets/XLua/*.cs; without it xLua goes to Assets/XLua/Core "
                         "and Assets/XLua/Src with their asmdefs, as in the repository (the default since 2026-10-18)")
parser.add_argument("--package", action="store_true",
                    help="xLua as the UPM package of Tools/make_upm_package.py (Runtime with XLua.asmdef, Editor "
                         "with XLua.Editor.asmdef, the plugins given, with --source-gen the generator in "
                         "Runtime/Analyzers), installed from a file: tarball in LocalPackages/ through "
                         "Packages/manifest.json instead of being copied into Assets")
parser.add_argument("--package-tarball", type=Path, metavar="FILE.tgz",
                    help="install this UPM tarball (a release asset, or make_upm_package.py --tarball) as it is, "
                         "with its own plugins: implies --package and takes no plugin options; with --source-gen "
                         "the tarball must hold the generator (Runtime/Analyzers), which is then not built")
parser.add_argument("--core", action="store_true",
                    help="a core-only project (docs/design/core-luascript.md): with --package, whose plugins must be "
                         "core libraries (XLUA_PRESET=core), and CoreSmoke.cs instead of XluaSmoke.cs; build with "
                         "XLUA_SMOKE_DEFINES=XLUA_CORE_ONLY and run SmokeBuild.ApplyDefines in a launch before the build")
parser.add_argument("--streaming-assets", action="store_true",
                    help="write Lua modules below Assets/StreamingAssets/xlua_sa (20 of about 2 KB, and the nested "
                         "a/b/c.lua) and add StreamingAssetsSmoke.cs with XLUA_SMOKE_STREAMING_ASSETS: the player "
                         "requires them through the StreamingAssets searcher, or with XLUA_PRESET_MINIMAL through "
                         "StreamingAssetsLoader.Load, and logs XLUA_NG_SA_TIMING lines")
parser.add_argument("--no-smoke", action="store_true",
                    help="leave out XluaSmoke.cs, SgSmoke.cs and SmokeBuild.cs, for a project that brings its own "
                         "scene and build script (Test/UnityMatrix/il2cpp_suite.py)")
args = parser.parse_args()
if args.package_tarball:
    args.package = True
    if not args.package_tarball.is_file():
        parser.error("no such tarball: " + str(args.package_tarball))
repo = Path(__file__).resolve().parents[2]
output = args.output.resolve()
if output.exists() and any(output.iterdir()):
    parser.error("Output must be empty; existing projects are never overwritten")
plugins = [(relative, source) for relative, source in
           [("arm64/libxlua.dylib", args.macos_plugin),
            ("iOS/libxlua.a", args.ios_plugin),
            ("Android/libs/arm64-v8a/libxlua.so", args.android_plugin)] if source]
variants = [(relative, source) for relative, source in
            [("Android/xlua-variants/minimal/libs/arm64-v8a/libxlua.so", args.android_minimal_plugin),
             ("iOS/xlua-variants/minimal/libxlua.a", args.ios_minimal_plugin),
             ("iOS/xlua-variants/simulator/libxlua.a", args.ios_simulator_plugin),
             ("iOS/xlua-variants/minimal_simulator/libxlua.a", args.ios_minimal_simulator_plugin),
             ("arm64/xlua-variants/minimal/libxlua.dylib", args.macos_minimal_plugin)] if source]
if (args.android_minimal_plugin and not args.android_plugin
        or (args.ios_minimal_plugin or args.ios_simulator_plugin or args.ios_minimal_simulator_plugin) and not args.ios_plugin
        or args.macos_minimal_plugin and not args.macos_plugin):
    parser.error("a minimal plugin is a variant of the default plugin; pass the platform's default plugin too")
if args.no_asmdef and args.package:
    parser.error("--no-asmdef takes no --package (which has the asmdefs)")
# The asmdef layout, unless the package (which has the asmdefs) or --no-asmdef.
asmdef = not args.package and not args.no_asmdef
if args.core and (not args.package or args.source_gen or args.webgl):
    parser.error("--core needs --package and takes no --source-gen or --webgl "
                 "(in the Assets layout XLUA_CORE_ONLY leaves nothing out)")
if args.package_tarball and (plugins or variants):
    parser.error("--package-tarball brings its plugins; it takes no plugin options")
if not plugins and not args.webgl and not args.package_tarball:
    parser.error("Pass at least one rebuilt plugin or --webgl")
if args.webgl and args.lua_version != "5.5":
    parser.error("WebGL compiles Lua 5.5.1 only")
for _, source in plugins + variants:
    if not source.is_file():
        parser.error("Missing rebuilt plugin: " + str(source))
for name in ["Assets/Editor", "Assets/XLua", "Packages", "ProjectSettings"]:
    (output / name).mkdir(parents=True, exist_ok=True)
dependencies = {"com.unity.modules.unitywebrequestwww": "1.0.0"}
if args.package_tarball:
    local = output / "LocalPackages"
    local.mkdir(parents=True)
    tgz = local / args.package_tarball.name
    shutil.copy2(args.package_tarball, tgz)
    with tarfile.open(tgz) as tar:
        names = set(tar.getnames())
        manifest = json.loads(tar.extractfile("package/package.json").read().decode("utf-8"))
    if args.source_gen and "package/Runtime/Analyzers/XLua.SourceGenerator.dll" not in names:
        parser.error("%s has no Runtime/Analyzers/XLua.SourceGenerator.dll" % args.package_tarball)
    dependencies[manifest["name"]] = "file:../LocalPackages/" + tgz.name
elif args.package:
    # xLua, its Editor code, the plugins and (with --source-gen) the generator come from the package.
    sys.path.insert(0, str(repo / "Tools"))
    import make_upm_package  # noqa: E402
    local = output / "LocalPackages"
    tgz = local / ("%s-%s.tgz" % (make_upm_package.DEFAULT_NAME, make_upm_package.DEFAULT_VERSION))
    # The package always has the WebGL source (Plugins/WebGL/xlua_webgl.cpp, the C sources in build~),
    # so --webgl needs nothing more than the link.xml below.
    cmd = [sys.executable, str(repo / "Tools/make_upm_package.py"), "--output", str(local / "package"),
           "--tarball", str(tgz), "--no-samples"]
    # Variants get their default plugin's import settings in the package (make_upm_package.py).
    for relative, source in plugins + variants:
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
    ("-define:XLUA_SMOKE_SOURCE_GEN\n" if args.source_gen else "") +
    ("-define:XLUA_SMOKE_MINIMAL_PLUGIN\n" if any("/minimal" in relative for relative, _ in variants) else "") +
    ("-define:XLUA_SMOKE_STREAMING_ASSETS\n" if args.streaming_assets and not args.no_smoke else ""))
# The asmdef layout: xLua in Assets/XLua/Src and Core, as in the repository; the
# source generator runs in the assemblies that reference the XLua asmdef. Without
# asmdefs it runs in every assembly, Assembly-CSharp (which holds xLua) among them.
xlua_dir = output / ("Assets/XLua/Src" if asmdef else "Assets/XLua")
if not args.package:
    xlua_dir.mkdir(parents=True, exist_ok=True)
    for source in (repo / "Assets/XLua/Src").glob("*.cs"):
        shutil.copy2(source, xlua_dir / source.name)
    shutil.copytree(repo / "Assets/XLua/Src/TemplateEngine", xlua_dir / "TemplateEngine")
    # XLua.Core (Assets/XLua/Core): next to the bridge in the Assets layout, in
    # Assets/XLua/Core with its asmdef in the asmdef layout.
    core_dir = output / "Assets/XLua/Core" if asmdef else xlua_dir
    core_dir.mkdir(parents=True, exist_ok=True)
    for source in (repo / "Assets/XLua/Core").glob("*.cs"):
        shutil.copy2(source, core_dir / source.name)
if args.source_gen and not args.package:
    # The Editor code: the source generator's build checks and the link.xml processor.
    shutil.copytree(repo / "Assets/XLua/Src/Editor", xlua_dir / "Editor",
                    ignore=None if asmdef else shutil.ignore_patterns("*.asmdef", "*.asmdef.meta"))
    core_editor = (output / "Assets/XLua/Core" if asmdef else xlua_dir) / "Editor"
    core_editor.mkdir(parents=True, exist_ok=True)
    for source in (repo / "Assets/XLua/Core/Editor").glob("*.cs"):
        shutil.copy2(source, core_editor / source.name)
if asmdef:
    # The runtime asmdefs; XLua.Editor.asmdef came with the Editor folder.
    for name in ["XLua.asmdef", "XLua.asmdef.meta"]:
        shutil.copy2(repo / "Assets/XLua/Src" / name, xlua_dir / name)
    for name in ["XLua.Core.asmdef", "XLua.Core.asmdef.meta"]:
        shutil.copy2(repo / "Assets/XLua/Core" / name, output / "Assets/XLua/Core" / name)
    if args.source_gen:
        for name in ["XLua.Core.Editor.asmdef", "XLua.Core.Editor.asmdef.meta"]:
            shutil.copy2(repo / "Assets/XLua/Core/Editor" / name, output / "Assets/XLua/Core/Editor" / name)
if args.source_gen and not args.package:
    subprocess.run([sys.executable, str(repo / "Tools/build_source_generator.py"), "--output", str(args.source_gen.resolve()),
                    "--install", str(xlua_dir / "Analyzers")], check=True)
if args.source_gen and not args.no_smoke:
    shutil.copy2(Path(__file__).with_name("SgSmoke.cs"), output / "Assets/SgSmoke.cs")
for relative, source in ([] if args.package else plugins):
    dest = output / "Assets/Plugins" / relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, dest)
    shutil.copy2(repo / "Assets/Plugins" / (relative + ".meta"), str(dest) + ".meta")
if variants and not args.package:
    # No .meta: SmokeBuild.cs runs XLua/Plugin Variants/Copy Import Settings before the build, as a
    # project does once after adding a variant. The selector is Editor code (Assets/XLua/Core/Editor);
    # the default layout copies the Editor folders only with --source-gen.
    for relative, source in variants:
        dest = output / "Assets/Plugins" / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)
    if not args.source_gen:
        core_editor = (output / "Assets/XLua/Core" if asmdef else xlua_dir) / "Editor"
        core_editor.mkdir(exist_ok=True)
        names = ["PluginVariants.cs", "PluginVariants.cs.meta"]
        if asmdef:
            names += ["XLua.Core.Editor.asmdef", "XLua.Core.Editor.asmdef.meta"]
        for name in names:
            shutil.copy2(repo / "Assets/XLua/Core/Editor" / name, core_editor / name)
if args.ios_plugin and not args.package:
    for name in ["HotfixFlags.cpp", "HotfixFlags.cpp.meta"]:
        shutil.copy2(repo / "Assets/Plugins/iOS" / name, output / "Assets/Plugins/iOS" / name)
if args.webgl and not args.package:
    # xlua_webgl.cpp includes ../../../build/... relative to itself, so the project gets
    # the same build/ layout: the Lua 5.5.1 sources and the bridge sources.
    (output / "Assets/Plugins/WebGL").mkdir(parents=True, exist_ok=True)
    for name in ["xlua_webgl.cpp", "xlua_webgl.cpp.meta"]:
        shutil.copy2(repo / "Assets/Plugins/WebGL" / name, output / "Assets/Plugins/WebGL" / name)
    shutil.copytree(repo / "build/lua-5.5.1/src", output / "build/lua-5.5.1/src")
    for name in ["xlua_core.c", "xlua_core_limits.c", "xlua_core.h", "xlua_luahooks.h", "xlua.c", "i64lib.c", "i64lib.h", "memory_leak_checker.c"]:
        shutil.copy2(repo / "build" / name, output / "build" / name)
if args.webgl:
    # Unity 6000.6.4f1 strips ContentLoadingSystem.ProcessResults from this minimal project,
    # and the WebGL player halts with "null function" before the first scene starts.
    (output / "Assets/link.xml").write_text(
        '<linker>\n  <assembly fullname="UnityEngine.ContentLoadModule" preserve="all"/>\n</linker>\n')
if args.streaming_assets and not args.no_smoke:
    # Modules of about 2 KB each, as a game's would be; StreamingAssetsSmoke.cs requires them.
    streaming = output / "Assets/StreamingAssets/xlua_sa"
    (streaming / "a/b").mkdir(parents=True)
    for i in range(1, 21):
        body = "".join("function M.f%02d(x) return x * %d + %d end\n" % (j, i, j) for j in range(1, 51))
        (streaming / ("m%02d.lua" % i)).write_text("local M = {id = %d}\n%sreturn M\n" % (i, body))
    (streaming / "a/b/c.lua").write_text("return 'nested a.b.c'\n")
    shutil.copy2(Path(__file__).with_name("StreamingAssetsSmoke.cs"), output / "Assets/StreamingAssetsSmoke.cs")
if not args.no_smoke:
    shutil.copy2(Path(__file__).with_name("CoreSmoke.cs" if args.core else "XluaSmoke.cs"), output / "Assets/XluaSmoke.cs")
    shutil.copy2(Path(__file__).with_name("SmokeBuild.cs"), output / "Assets/Editor/SmokeBuild.cs")
print(output)
