"""Measure what xLua adds to a Unity player: upstream Tencent/xLua against xlua-ng.

`run` prepares one Unity project per platform and variant under WORK/projects, builds
release IL2CPP players with SmokeBuild.cs into WORK/players and, for iOS, compiles the
exported Xcode project unsigned with a linker map. Finished builds are skipped, so an
interrupted run resumes. `report` measures every player under WORK/players and writes
WORK/size-report.json and WORK/size-report.md with deltas against the baseline.

Variants:
  baseline     SizeProbe.cs only, no xLua
  upstream     Assets/XLua/{Src,Resources,Editor}, Assets/Plugins and WebGLPlugins from
               git history (--upstream-commit, default 59bf426), i.e. what an upstream user ships
  ng-5.5 ...   this checkout's Assets/XLua/{Src,Resources,Editor} with plugins built by
               build/make_*_lua55.sh etc. into --plugins (their XLUA_BUILD_ROOT); WebGL compiles
               Assets/Plugins/WebGL/xlua_webgl.cpp and needs no plugin
  upstream+gen upstream with SizeGenConfig.cs and its own XLua/Generate Code run before the build (also
               NAME+at-REF+gen for a commit before milestone M11); this checkout has no Generate Code since M11
               (the "+gen" and "+gen+gcm" variants of ng-*, measured until then, are in build/SIZE.md)
  NAME+pkg     xLua as the UPM package of Tools/make_upm_package.py (Runtime with XLua.asmdef, Editor with
               XLua.Editor.asmdef, the plugins) installed from a file: tarball instead of Assets/XLua
  NAME+pkg+sg  the package with the source generator in Runtime/Analyzers, and SizeSgConfig.cs (the
               configuration of SizeGenConfig.cs as assembly attributes) in Assembly-CSharp; nothing is
               generated before the build
  NAME+pkg+sg+gcm  the same compiled with GEN_CODE_MINIMIZE (the source generator's GCM mode)
  NAME+game    a game-sized configuration instead of the small one for +sg: SizeGameSgConfig.cs (every class
               and struct of UnityEngine.CoreModule and UnityEngine.UI, 264 types in 6000.3, as attributes); every
               +game project, baseline+game too, has com.unity.ugui, and the probe also times Lua calls into the
               wrapped types (XLUA_SIZE_GAME)
  NAME+noasmdef  without xLua's four asmdefs (XLua > Project Layout > Use Assembly-CSharp): xLua compiles into
               Assembly-CSharp, as before 2026-10-18 and as every +at-REF of an older commit; without it this
               checkout's tree has the asmdefs (XLua.Core, XLua and their Editor assemblies)
  NAME+nosa    compiled with XLUA_NO_STREAMING_ASSETS_LOADER (no StreamingAssets searcher)
  NAME+nosig, NAME+notemplate, NAME+noutil, NAME+noprofiler  compiled with XLUA_NO_SIGNATURE_LOADER,
               XLUA_NO_TEMPLATE, XLUA_NO_UTIL or XLUA_NO_PROFILER (combinable, e.g. ng-5.5+nosig+notemplate)
  NAME+minimal the minimal preset (docs/design/presets.md): XLUA_PRESET_MINIMAL in the Player Settings
               scripting defines, and the plugin built by XLUA_VARIANT=minimal XLUA_PRESET=minimal
               build/make_*.sh (Plugins/<platform>/xlua-variants/minimal/ under --plugins) copied as a
               variant next to the default one, so the plugin selector (Editor/PluginVariants.cs) picks
               it at build time, as in a project
  NAME+uwr     the probe also calls UnityWebRequest.Get once, like a project that uses it anyway;
               its deltas are against baseline+uwr
  NAME+at-REF  Assets/XLua/{Src,Resources,Editor} from commit REF instead of this checkout
               (plugins still come from --plugins), to compare a change with the commit before it
  NAME+gsize   IL2CPP Code Generation "Faster (smaller) builds" (Il2CppCodeGeneration.OptimizeSize,
               full generic sharing) instead of the default "Faster runtime"
  NAME+srcplugin  no prebuilt library: the Lua 5.5.1, xLua and LuaSocket C sources go into
               Assets/Plugins/Android/xlua (IL2CPP compiles them into libil2cpp.so; the project
               defines XLUA_ANDROID_SOURCE_PLUGIN so LuaDLL imports from __Internal) or
               Assets/Plugins/iOS/xlua (Xcode compiles them into UnityFramework)
  NAME+srcplugin+forceinternal  the sources without the C# define: IL2CPP's own
               -DFORCE_PINVOKE_xlua_INTERNAL (in --compiler-flags) turns DllImport("xlua") into direct calls
  NAME+master  IL2CPP C++ Compiler Configuration Master instead of Release
  NAME+cflags  Additional IL2CPP Arguments --compiler-flags="-O2", to see where extra flags go
  NAME+alloc   AllocProbe.cs instead of SizeProbe.cs: allocations and time of reflection-mode paths
  NAME+sim     iOS: Simulator SDK export, compiled with xcodebuild -sdk iphonesimulator
  baseline+select  two prebuilt Android libraries, Plugins/Android/full (Lua 5.5) and minimal (Lua 5.3);
               PluginVariantSelector.cs picks one with PluginImporter.SetIncludeInBuildDelegate:
               full when the Player Settings define XLUA_FULL_PLUGIN or
               EditorUserBuildSettings.development is set
  baseline+constraints  the same two libraries with PluginImporter define constraints instead
               ("XLUA_FULL_PLUGIN || DEVELOPMENT_BUILD" and "!XLUA_FULL_PLUGIN", "!DEVELOPMENT_BUILD")
  core-5.5     LuaScript alone (docs/design/core-luascript.md, section 6): the UPM package (as +pkg) with the
               core library (XLUA_VARIANT=core XLUA_PRESET=core build/make_*_lua55.sh, from
               Plugins/<platform>/xlua-variants/core/ under --plugins) as its plugin, XLUA_CORE_ONLY in the
               Player Settings defines (XLua and XLua.Editor are left out by their define constraints), and
               SizeProbeCore.cs instead of SizeProbe.cs; core-5.4 the same with Lua 5.4.9
  core-5.5+nodefine  the same package and probe with XLUA_LUASCRIPT instead of XLUA_CORE_ONLY: the XLua
               assembly is compiled into the player but nothing references it (the stripping question)
  mixed-5.5    LuaEnv and LuaScript in one player: the package with the bridge library built with
               XLUA_VARIANT=luascript XLUA_LUASCRIPT=ON XLUA_BUFFER=ON (xlua-variants/luascript/), XLUA_LUASCRIPT
               in the Player Settings, SizeProbe.cs with XLUA_SIZE_MIXED running SizeProbeCore.cs as well

Every other xLua variant runs SizeProbe.cs: a LuaEnv, a chunk, one C# static method called from
Lua and one Lua function called from C#. Examples, Tutorial and Doc are not copied.
Test/MobileSmoke/core_only_player_check.py checks the core players for bridge code.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time
import zipfile

here = Path(__file__).resolve().parent
repo = here.parents[1]
sys.path.insert(0, str(repo / "Test/Native"))
import benchmark  # noqa: E402  (size_entry and player_entry)

# Variant flags for the C# feature defines (docs/MIGRATING.md, "Opt-in defines").
CS_FEATURE_FLAGS = {"nosig": "XLUA_NO_SIGNATURE_LOADER", "notemplate": "XLUA_NO_TEMPLATE",
                    "noutil": "XLUA_NO_UTIL", "noprofiler": "XLUA_NO_PROFILER"}
PLUGIN_DIRS = {"5.5": "plugin_lua55", "5.4": "plugin_lua54", "5.3": "plugin_lua53", "luajit": "plugin_luajit"}
# Android: Unity's default managed stripping, then Medium, then ARMv7 + ARM64. Managed stripping
# persists in ProjectSettings, so the later configurations name the level they want.
ANDROID_ALL = ["baseline", "upstream", "ng-5.5", "ng-5.4", "ng-5.3", "ng-luajit"]
# The source generator and its GCM mode (GEN_CODE_MINIMIZE) in the package layout
# (docs/design/source-generator.md, M6 and "GCM mode as implemented"). Until milestone M11 this also
# built Generate Code and its GEN_CODE_MINIMIZE output in both layouts.
SOURCE_GEN = ["baseline", "ng-5.5+pkg", "ng-5.5+pkg+sg", "ng-5.5+pkg+sg+gcm"]
# The same with a game-sized configuration (source-generator.md, M6 question 2).
GAME = ["baseline+game", "ng-5.5+pkg+sg+game", "ng-5.5+pkg+sg+gcm+game"]
PRESETS = ["baseline", "ng-5.5", "ng-5.5+minimal"]
# LuaScript (core-luascript.md, section 6): alone, without the define, and next to LuaEnv; the package
# layout throughout, so ng-5.5+pkg is the legacy row to compare with.
CORE = ["baseline", "ng-5.5+pkg", "core-5.5", "core-5.5+nodefine", "mixed-5.5"]
# The library of the core and mixed variants: a plugin variant of the make_* scripts' build root.
CORE_PLUGIN_VARIANT = {"core": "core", "mixed": "luascript"}
CASES = {
    "android": [("default", {}, ANDROID_ALL + ["upstream+gen", "ng-5.5+pkg+sg", "ng-5.5+nosa",
                                               "baseline+uwr", "ng-5.5+uwr", "ng-5.5+nosa+uwr"]),
                ("medium", {"XLUA_SMOKE_STRIPPING": "Medium"}, ANDROID_ALL),
                ("armv7", {"XLUA_SMOKE_STRIPPING": "Minimal", "XLUA_SMOKE_ANDROID_ARCHS": "ARMv7, ARM64"},
                 ["baseline", "upstream", "ng-5.5"]),
                ("aab", {"XLUA_SMOKE_STRIPPING": "Minimal", "XLUA_SMOKE_AAB": "1"}, ["baseline", "upstream", "ng-5.5"]),
                # Which of two prebuilt plugins a build includes (run with --configs select-...).
                ("select-none", {"XLUA_SMOKE_DEFINES": "XLUA_SPIKE_NONE"}, ["baseline+select", "baseline+constraints"]),
                ("select-define", {"XLUA_SMOKE_DEFINES": "XLUA_FULL_PLUGIN"}, ["baseline+select", "baseline+constraints"]),
                ("select-dev", {"XLUA_SMOKE_DEFINES": "XLUA_SPIKE_NONE", "XLUA_SMOKE_RELEASE": "0"},
                 ["baseline+select", "baseline+constraints"]),
                # Run with --configs sg-minimal,sg-high.
                ("sg-minimal", {"XLUA_SMOKE_STRIPPING": "Minimal"}, SOURCE_GEN),
                ("sg-high", {"XLUA_SMOKE_STRIPPING": "High"}, SOURCE_GEN),
                # Run with --configs game-minimal,game-high,game-medium.
                ("game-minimal", {"XLUA_SMOKE_STRIPPING": "Minimal"}, GAME),
                ("game-high", {"XLUA_SMOKE_STRIPPING": "High"}, GAME),
                ("game-medium", {"XLUA_SMOKE_STRIPPING": "Medium"}, GAME),
                # The minimal preset against legacy (run with --configs minimal).
                ("minimal", {"XLUA_SMOKE_STRIPPING": "Minimal"}, PRESETS),
                # LuaScript (run with --configs core,core-high).
                ("core", {"XLUA_SMOKE_STRIPPING": "Minimal"}, CORE),
                ("core-high", {"XLUA_SMOKE_STRIPPING": "High"}, CORE)],
    "ios": [("default", {}, ["baseline", "upstream", "ng-5.5", "ng-5.4", "ng-5.3", "upstream+gen", "ng-5.5+pkg+sg"]),
            ("minimal", {}, PRESETS),
            ("core", {"XLUA_SMOKE_STRIPPING": "Minimal"}, CORE)],
    "webgl": [("uncompressed", {"XLUA_SMOKE_WEBGL_COMPRESSION": "Disabled"}, ["baseline", "upstream", "ng-5.5"]),
              ("brotli", {"XLUA_SMOKE_WEBGL_COMPRESSION": "Brotli"}, ["baseline", "upstream", "ng-5.5"])],
}
UNITY_TARGET = {"android": ("Android", "SmokeBuild.BuildAndroid"), "ios": ("iOS", "SmokeBuild.BuildIos"),
                "webgl": ("WebGL", "SmokeBuild.BuildWebGL")}


# ---------------------------------------------------------------- projects

def copy_with_meta(src, dst, meta_root=None, rel=None):
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    meta = (meta_root / (rel + ".meta")) if meta_root else Path(str(src) + ".meta")
    if meta.is_file():
        shutil.copy2(meta, str(dst) + ".meta")


def upstream_tree(a, work):
    tree = work / ("upstream-" + a.upstream_commit)
    if not tree.is_dir():
        tree.mkdir(parents=True)
        archive = subprocess.run(["git", "-C", str(repo), "archive", a.upstream_commit, "Assets/XLua/Src",
                                  "Assets/XLua/Resources", "Assets/XLua/Editor", "Assets/Plugins", "WebGLPlugins"],
                                 check=True, stdout=subprocess.PIPE).stdout
        subprocess.run(["tar", "-x", "-C", str(tree)], input=archive, check=True)
    return tree


def ref_tree(work, ref):
    """Assets/XLua/{Core,Src,Resources,Editor} of commit REF (as far as it has them), extracted once into WORK/tree-REF."""
    tree = work / ("tree-" + ref)
    if not tree.is_dir():
        tree.mkdir(parents=True)
        parts = [p for p in ["Assets/XLua/Core", "Assets/XLua/Src", "Assets/XLua/Resources", "Assets/XLua/Editor"]
                 if subprocess.run(["git", "-C", str(repo), "cat-file", "-e", ref + ":" + p]).returncode == 0]
        archive = subprocess.run(["git", "-C", str(repo), "archive", ref] + parts,
                                 check=True, stdout=subprocess.PIPE).stdout
        subprocess.run(["tar", "-x", "-C", str(tree)], input=archive, check=True)
    return tree


# The definitions build/CMakeLists.txt passes with -D. Unity compiles plugin sources with its own flags and
# no -D or -I of ours, so the source plugin puts them at the top of luaconf.h, which every Lua and LuaSocket
# file includes (LUA_COMPAT_APIINTCASTS is for LuaSocket only in CMake; it only adds macros). XLUA_LUASOCKET
# and XLUA_MIME make xlua.c register the copied LuaSocket and mime in package.preload, as CMake's options do.
SOURCE_PLUGIN_DEFINES = {"android": ["LUA_USE_LINUX", "LUA_COMPAT_APIINTCASTS", "XLUA_LUASOCKET", "XLUA_MIME"],
                         "ios": ["LUA_USE_IOS", "LUA_COMPAT_APIINTCASTS", "XLUA_LUASOCKET", "XLUA_MIME"]}


def copy_source_plugin(dest, platform):
    """The Lua 5.5.1, xLua and LuaSocket sources in one directory, so quoted #includes find their headers
    next to the file without include paths. The files are the ones build/CMakeLists.txt compiles."""
    dest.mkdir(parents=True)
    lua = repo / "build/lua-5.5.1/src"
    files = [f for f in sorted(lua.iterdir()) if f.suffix in (".c", ".h") and f.name not in ("lua.c", "luac.c")]
    files += [repo / "build" / n for n in ["xlua_core.c", "xlua_core_limits.c", "xlua_core.h", "xlua_luahooks.h", "xlua.c", "i64lib.c", "i64lib.h", "memory_leak_checker.c"]]
    files += [f for f in sorted((repo / "build/luasocket").iterdir())
              if f.suffix in (".c", ".h") and not f.name.startswith("wsocket")]
    for f in files:
        shutil.copy2(f, dest / f.name)
    conf = dest / "luaconf.h"
    head = "/* xlua-ng source plugin: the definitions build/CMakeLists.txt passes with -D. */\n" + "".join(
        "#ifndef %s\n#define %s\n#endif\n" % (d, d) for d in SOURCE_PLUGIN_DEFINES[platform])
    conf.write_text(head + conf.read_text())
    return files


ANDROID_PLUGIN_META = """fileFormatVersion: 2
guid: %s
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: %s
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  platformData:
  - first:
      Android: Android
    second:
      enabled: 1
      settings:
        CPU: ARM64
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def prepare_plugin_variants(a, assets, flags):
    """Plugins/Android/full/libxlua.so (Lua 5.5) and minimal/libxlua.so (Lua 5.3): +select picks one with
    PluginVariantSelector.cs, +constraints with define constraints in the .meta files."""
    constrained = "constraints" in flags
    for folder, lua, guid, constraints in [
            ("full", "5.5", "9a1f0c4e5b6d4e7f8a9b0c1d2e3f4a51", ["XLUA_FULL_PLUGIN || DEVELOPMENT_BUILD"]),
            ("minimal", "5.3", "9a1f0c4e5b6d4e7f8a9b0c1d2e3f4a52", ["!XLUA_FULL_PLUGIN", "!DEVELOPMENT_BUILD"])]:
        so = a.plugins / PLUGIN_DIRS[lua] / "Plugins/Android/libs/arm64-v8a/libxlua.so"
        dest = assets / "Plugins/Android" / folder / "libxlua.so"
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(so, dest)
        listed = "".join("\n  - %s" % c for c in constraints) if constrained else " []"
        Path(str(dest) + ".meta").write_text(ANDROID_PLUGIN_META % (guid, listed))
    if not constrained:
        shutil.copy2(here / "PluginVariantSelector.cs", assets / "Editor/PluginVariantSelector.cs")


def prepare(a, work, platform, variant, project):
    name, flags = variant.split("+")[0], variant.split("+")[1:]
    gen = "gen" in flags
    at = next((f[len("at-"):] for f in flags if f.startswith("at-")), None)
    srcplugin = "srcplugin" in flags
    kind = name.split("-")[0]  # ng, core or mixed (or baseline, upstream)
    # LuaScript's variants need the package: in the Assets layout XLUA_CORE_ONLY leaves nothing out.
    pkg, sg = "pkg" in flags or kind in CORE_PLUGIN_VARIANT, "sg" in flags
    if gen and not (name == "upstream" or at):
        sys.exit("+gen runs Generate Code, which this checkout no longer has (milestone M11); use +pkg+sg: " + variant)
    if gen and ("gcm" in flags or "game" in flags or pkg):
        sys.exit("+gen takes no +gcm, +game or +pkg: " + variant)
    if sg and not pkg:
        sys.exit("+sg needs +pkg: " + variant)
    if "gcm" in flags and not sg:
        sys.exit("+gcm needs +sg: " + variant)
    assets = project / "Assets"
    for d in ["Assets/Editor", "Packages", "ProjectSettings"]:
        (project / d).mkdir(parents=True, exist_ok=True)
    # xLua's StaticLuaCallbacks uses UnityEngine.WWW. Every variant gets the module, so the baseline
    # differs only by xLua; engine code stripping drops it where nothing uses it.
    game = "game" in flags
    deps = {"com.unity.modules.unitywebrequestwww": "1.0.0"}
    if game:
        deps["com.unity.ugui"] = "2.0.0"  # UnityEngine.UI, which the game configuration binds
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": deps}) + "\n")
    unity = a.unity_webgl if platform == "webgl" else a.unity_mobile
    version = re.search(r"/(\d+\.\d+\.\d+[abfp]\d+)/", str(unity))
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + (version.group(1) if version else "") + "\n")
    defines = (["XLUA_SIZE_BASELINE"] if name == "baseline" else []) + \
        (["XLUA_NO_STREAMING_ASSETS_LOADER"] if "nosa" in flags else []) + (["XLUA_SIZE_UWR"] if "uwr" in flags else []) + \
        [define for flag, define in CS_FEATURE_FLAGS.items() if flag in flags] + \
        (["GEN_CODE_MINIMIZE"] if "gcm" in flags else []) + (["XLUA_SIZE_MIXED"] if kind == "mixed" else []) + \
        (["XLUA_SIZE_GAME"] if game else []) + \
        (["XLUA_ANDROID_SOURCE_PLUGIN"] if srcplugin and platform == "android" and "forceinternal" not in flags else [])
    (assets / "csc.rsp").write_text("-unsafe\n" + "".join("-define:%s\n" % d for d in defines))
    probe = "AllocProbe.cs" if "alloc" in flags else "SizeProbeCore.cs" if kind == "core" else "SizeProbe.cs"
    shutil.copy2(here / probe, assets / "XluaSmoke.cs")
    if kind == "mixed":
        shutil.copy2(here / "SizeProbeCore.cs", assets / "SizeProbeCore.cs")
    shutil.copy2(here / "SmokeBuild.cs", assets / "Editor/SmokeBuild.cs")
    if gen:
        shutil.copy2(here / "SizeGenConfig.cs", assets / "Editor/SizeGenConfig.cs")
    if sg:
        config = "SizeGameSgConfig.cs" if game else "SizeSgConfig.cs"
        shutil.copy2(here / config, assets / config)
    if platform == "webgl":
        # As in prepare.py, for every variant: Unity 6000.6 otherwise strips ContentLoadingSystem.ProcessResults.
        (assets / "link.xml").write_text('<linker>\n  <assembly fullname="UnityEngine.ContentLoadModule" preserve="all"/>\n</linker>\n')
    if "select" in flags or "constraints" in flags:
        prepare_plugin_variants(a, assets, flags)
    if name == "baseline":
        return
    src = upstream_tree(a, work) if name == "upstream" else ref_tree(work, at) if at else repo
    for part in ([] if pkg else ["Core", "Src", "Resources", "Editor"]):
        # Resources is gone since 2026-10-17 (xlua.util and perf.profiler are in EmbeddedModules.cs).
        if (src / "Assets/XLua" / part).is_dir():
            shutil.copytree(src / "Assets/XLua" / part, assets / "XLua" / part,
                            ignore=shutil.ignore_patterns("*.asmdef", "*.asmdef.meta") if "noasmdef" in flags else None)
    if name == "upstream":
        # xlua.bundle is x86_64 only and an arm64 Editor would load it instead of arm64/libxlua.dylib
        # (code generation fails with DllNotFoundException). It never ships in a mobile or WebGL player.
        skip = ["xlua.bundle", "xlua.bundle.meta"] + ([] if platform == "webgl" else ["WebGL", "WebGL.meta"])
        shutil.copytree(src / "Assets/Plugins", assets / "Plugins", ignore=shutil.ignore_patterns(*skip))
        if platform == "webgl":
            shutil.copytree(src / "WebGLPlugins", project / "WebGLPlugins")
        return
    lua = name.split("-", 1)[1]
    plugins = a.plugins / PLUGIN_DIRS[lua] / "Plugins"
    wanted = {"android": ["Android/libs/*/libxlua.so"], "ios": ["iOS/libxlua.a"], "webgl": []}[platform]
    if kind in CORE_PLUGIN_VARIANT:
        if platform == "webgl" or srcplugin or at or variant_dir_flags(flags):
            sys.exit("%s takes the prebuilt Android or iOS plugin of this checkout: %s" % (kind, variant))
        # The variant's library becomes the package's plugin: the core library is what a core-only
        # project installs, and the size is then that of a package without variants.
        vdir = "xlua-variants/%s/" % CORE_PLUGIN_VARIANT[kind]
        found = [f for f in plugins.glob({"android": "Android/" + vdir + "libs/*/libxlua.so", "ios": "iOS/" + vdir + "libxlua.a"}[platform])]
        if not found:
            sys.exit("no %s plugin under %s" % (vdir, plugins))
        install_package(work, project, [(str(f.relative_to(plugins)).replace(vdir, ""), f) for f in found], sg)
        return
    variant_dir = "xlua-variants/minimal/" if "minimal" in flags else ""
    if variant_dir:
        # The preset's library goes next to the default one, without a .meta: SmokeBuild.cs runs
        # Copy Import Settings and the selector picks it. The WebGL build compiles every part.
        wanted += {"android": ["Android/" + variant_dir + "libs/*/libxlua.so"],
                   "ios": ["iOS/" + variant_dir + "libxlua.a"], "webgl": []}[platform]
    if srcplugin:
        if lua != "5.5" or platform == "webgl":
            sys.exit("+srcplugin compiles Lua 5.5.1 for Android or iOS")
        copy_source_plugin(assets / "Plugins" / {"android": "Android", "ios": "iOS"}[platform] / "xlua", platform)
        wanted = []
    if gen:
        wanted.append("arm64/libxlua.dylib")  # code generation runs Lua templates in the Editor
    found = [f for pattern in wanted for f in plugins.glob(pattern)]
    if platform != "webgl" and not srcplugin and not any(f.suffix != ".dylib" for f in found):
        sys.exit("no %s plugin under %s" % (platform, plugins))
    if pkg:
        if variant_dir or srcplugin or at or platform == "webgl":
            sys.exit("+pkg takes the default prebuilt Android or iOS plugins of this checkout: " + variant)
        install_package(work, project, [(str(f.relative_to(plugins)), f) for f in found], sg)
        found = []
    if variant_dir and platform != "webgl" and not any(variant_dir in str(f) for f in found):
        sys.exit("no %s plugin under %s" % (variant_dir, plugins))
    for f in found:
        rel = str(f.relative_to(plugins))
        if variant_dir and variant_dir in rel:
            dest = assets / "Plugins" / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(f, dest)
            continue
        copy_with_meta(f, assets / "Plugins" / rel, repo / "Assets/Plugins", rel)
    if platform == "ios":
        for n in ["HotfixFlags.cpp", "HotfixFlags.cpp.meta"]:
            shutil.copy2(repo / "Assets/Plugins/iOS" / n, assets / "Plugins/iOS" / n)
    if platform == "webgl":
        if lua != "5.5":
            sys.exit("WebGL compiles Lua 5.5.1 only")
        (assets / "Plugins/WebGL").mkdir(parents=True)
        for n in ["xlua_webgl.cpp", "xlua_webgl.cpp.meta"]:
            shutil.copy2(repo / "Assets/Plugins/WebGL" / n, assets / "Plugins/WebGL" / n)
        shutil.copytree(repo / "build/lua-5.5.1/src", project / "build/lua-5.5.1/src")
        for n in ["xlua_core.c", "xlua_core_limits.c", "xlua_core.h", "xlua_luahooks.h", "xlua.c", "i64lib.c", "i64lib.h", "memory_leak_checker.c"]:
            shutil.copy2(repo / "build" / n, project / "build" / n)


def variant_dir_flags(flags):
    return [f for f in flags if f in ("minimal", "gen", "gcm", "select", "constraints", "game")]


def install_package(work, project, plugins, sg):
    """The +pkg variants: Tools/make_upm_package.py into a file: tarball under LocalPackages/."""
    sys.path.insert(0, str(repo / "Tools"))
    import make_upm_package
    local = project / "LocalPackages"
    tgz = local / ("%s-%s.tgz" % (make_upm_package.DEFAULT_NAME, make_upm_package.DEFAULT_VERSION))
    cmd = [sys.executable, str(repo / "Tools/make_upm_package.py"), "--output", str(local / "package"),
           "--tarball", str(tgz), "--no-samples"]
    for rel, path in plugins:
        cmd += ["--plugin", "%s=%s" % (rel, path)]
    if sg:
        cmd += ["--source-gen", str(work / "source-gen-build")]
    subprocess.run(cmd, check=True)
    shutil.rmtree(local / "package")
    manifest = project / "Packages/manifest.json"
    data = json.loads(manifest.read_text())
    data["dependencies"][make_upm_package.DEFAULT_NAME] = "file:../LocalPackages/" + tgz.name
    manifest.write_text(json.dumps(data) + "\n")


# ---------------------------------------------------------------- running tools

class Lock:
    """mkdir lock shared with other jobs that start native test hosts on this machine."""
    def __init__(self, path):
        self.path = path

    def __enter__(self):
        if self.path:
            while True:
                try:
                    self.path.mkdir()
                    break
                except FileExistsError:
                    time.sleep(30)
            (self.path / "owner").write_text("player-size\n")

    def __exit__(self, *exc):
        if self.path:
            (self.path / "owner").unlink(missing_ok=True)
            self.path.rmdir()


def run_tool(a, cmd, log, env):
    """Run one Unity or xcodebuild process under the lock; on timeout kill its process group and stop."""
    print("+", " ".join(str(c) for c in cmd), flush=True)
    with Lock(a.lock), open(log, "ab") as out:
        # -x matches the process name, so only editors (and their import workers) count; -f on the path
        # also matched other drivers that carry the editor path in their arguments and made two runs wait for each other.
        while a.one_unity and "Unity.app" in str(cmd[0]) and subprocess.run(
                ["pgrep", "-x", "Unity"], stdout=subprocess.DEVNULL).returncode == 0:
            print("  another Unity editor runs; waiting", flush=True)
            time.sleep(30)
        started = time.time()
        proc = subprocess.Popen([str(c) for c in cmd], env=env, stdout=out, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            rc = proc.wait(timeout=a.timeout)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid, 9)
            proc.wait()
            sys.exit("timed out after %ds: %s (see %s)" % (a.timeout, cmd[0], log))
        # The process's own time, without waiting for the lock or another editor.
        a.tool_seconds = round(time.time() - started)
    return rc


def unity(a, platform, project, method, log, env):
    exe = a.unity_webgl if platform == "webgl" else a.unity_mobile
    cmd = [exe, "-batchmode", "-nographics", "-quit", "-projectPath", project, "-buildTarget",
           UNITY_TARGET[platform][0], "-executeMethod", method, "-logFile", log]
    rc = run_tool(a, cmd, log.with_suffix(".stdout"), env)
    # Unity leaves its Gradle daemon (-Xmx4096m) running; stop the ones in our GRADLE_USER_HOME.
    android = Path(exe).resolve().parents[3] / "PlaybackEngines/AndroidPlayer"
    launcher = sorted(android.glob("Tools/gradle/lib/gradle-launcher-*.jar"))
    if platform == "android" and launcher:
        subprocess.run([str(android / "OpenJDK/bin/java"), "-cp", str(launcher[-1]), "org.gradle.launcher.GradleMain", "--stop"],
                       env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if rc != 0:
        sys.exit("Unity exited with %d: %s" % (rc, log))


def managed_stripped(project):
    """Sizes of the stripped managed assemblies IL2CPP compiled in the latest build."""
    dlls = sorted(project.glob("Library/Bee/artifacts/*/ManagedStripped/*.dll"), key=lambda p: p.stat().st_mtime)
    if not dlls:
        return {}
    folder = dlls[-1].parent
    return {p.name: p.stat().st_size for p in folder.glob("*.dll")}


def run(a):
    work = a.work.resolve()
    env = dict(os.environ, TMPDIR=str(work / "tmp"), GRADLE_USER_HOME=str(work / "gradle-home"), XLUA_SMOKE_RELEASE="1")
    (work / "tmp").mkdir(parents=True, exist_ok=True)
    for platform in a.platforms.split(","):
        variants = []
        for config, _, vs in CASES[platform]:
            if a.configs and config not in a.configs.split(","):
                continue
            variants += [v for v in (a.variants.split(",") if a.variants else vs) if v not in variants]
        for variant in variants:
            if a.only and not re.search(a.only, platform + "/" + variant):
                continue
            project = work / "projects" / (platform + "-" + variant.replace("+", "-"))
            if not project.is_dir():
                prepare(a, work, platform, variant, project)
                if "gen" in variant.split("+")[1:]:
                    unity(a, platform, project, "CSObjectWrapEditor.Generator.GenAll", project / "gen.log", env)
                    # The launch imports the project and compiles it before Generate Code runs.
                    gen_files = sorted((project / "Assets/XLua/Gen").rglob("*.cs"))
                    (project / "gen-info.json").write_text(json.dumps({
                        "gen_launch_seconds": a.tool_seconds, "files": len(gen_files),
                        "bytes": sum(f.stat().st_size for f in gen_files),
                        "lines": sum(f.read_bytes().count(b"\n") for f in gen_files)}, indent=1) + "\n")
            for config, settings, vs in CASES[platform]:
                if a.variants:
                    vs = a.variants.split(",")
                if variant not in vs or a.configs and config not in a.configs.split(","):
                    continue
                out = work / "players" / platform / config / variant
                info = out / "build-info.json"
                if info.is_file():
                    continue
                shutil.rmtree(out, ignore_errors=True)
                out.mkdir(parents=True)
                benv = dict(env, XLUA_SMOKE_OUTPUT_ROOT=str(out), **settings)
                flags = variant.split("+")[1:]
                if "minimal" in flags:
                    # In Player Settings, where the plugin selector reads it (csc.rsp would only compile the C#).
                    benv["XLUA_SMOKE_DEFINES"] = ";".join([d for d in benv.get("XLUA_SMOKE_DEFINES", "").split(";") if d]
                                                          + ["XLUA_PRESET_MINIMAL"])
                kind = variant.split("+")[0].split("-")[0]
                if kind in CORE_PLUGIN_VARIANT:
                    # LuaScript compiles with either define; XLUA_CORE_ONLY also leaves XLua and XLua.Editor out.
                    define = "XLUA_CORE_ONLY" if kind == "core" and "nodefine" not in flags else "XLUA_LUASCRIPT"
                    benv["XLUA_SMOKE_DEFINES"] = ";".join([d for d in benv.get("XLUA_SMOKE_DEFINES", "").split(";") if d]
                                                          + [define])
                if "gsize" in flags:
                    benv["XLUA_SMOKE_IL2CPP_CODEGEN"] = "OptimizeSize"
                if "sim" in flags:
                    benv["XLUA_SMOKE_IOS_SDK"] = "SimulatorSDK"
                if "master" in flags:
                    benv["XLUA_SMOKE_IL2CPP_CONFIG"] = "Master"
                il2cpp_flags = (["-DFORCE_PINVOKE_xlua_INTERNAL"] if "forceinternal" in flags else []) + \
                    (["-O2"] if "cflags" in flags else [])
                if il2cpp_flags:
                    benv["XLUA_SMOKE_IL2CPP_ARGS"] = '--compiler-flags="%s"' % " ".join(il2cpp_flags)
                log = out / "unity.log"
                if kind in CORE_PLUGIN_VARIANT:
                    # The defines in a launch of their own, so the build launch compiles with them from the start.
                    unity(a, platform, project, "SmokeBuild.ApplyDefines", out / "defines.log", benv)
                unity(a, platform, project, UNITY_TARGET[platform][1], log, benv)
                seconds = a.tool_seconds
                text = log.read_text(errors="replace")
                line = re.search(r"XLUA_NG_BUILD_SETTINGS: .*", text)
                data = {"platform": platform, "config": config, "variant": variant, "settings": settings,
                        "unity_settings": line.group(0) if line else None, "managed_stripped": managed_stripped(project),
                        "unity_seconds": seconds,
                        "script_assemblies": {p.name: p.stat().st_size for p in (project / "Library/ScriptAssemblies").glob("*.dll")}}
                if (project / "gen-info.json").is_file():
                    data["generate_code"] = json.loads((project / "gen-info.json").read_text())
                if platform == "ios":
                    started = time.time()
                    data["xcodebuild"] = xcode(a, work, out, env, "sim" in flags)
                    data["xcodebuild_seconds"] = round(time.time() - started)
                info.write_text(json.dumps(data, indent=1) + "\n")


def xcode(a, work, out, env, simulator=False):
    derived = work / "DerivedData" / out.relative_to(work / "players").as_posix().replace("/", "-").replace("+", "-")
    shutil.rmtree(derived, ignore_errors=True)
    sdk, destination = ("iphonesimulator", "generic/platform=iOS Simulator") if simulator else ("iphoneos", "generic/platform=iOS")
    cmd = ["xcodebuild", "-project", out / "unity-ios-smoke/Unity-iPhone.xcodeproj", "-scheme", "Unity-iPhone",
           "-configuration", "Release", "-sdk", sdk, "-destination", destination,
           "-derivedDataPath", derived, "CODE_SIGNING_ALLOWED=NO", "LD_GENERATE_MAP_FILE=YES",
           "DEPLOYMENT_POSTPROCESSING=YES", "build"]
    rc = run_tool(a, cmd, out / "xcodebuild.log", env)
    if rc != 0:
        sys.exit("xcodebuild exited with %d: %s" % (rc, out / "xcodebuild.log"))
    apps = list((derived / ("Build/Products/Release-" + sdk)).glob("*.app"))
    maps = list(derived.glob("Build/Intermediates.noindex/**/UnityFramework-LinkMap-*-arm64.txt"))
    if len(apps) != 1:
        sys.exit("expected one .app under " + str(derived))
    shutil.copytree(apps[0], out / apps[0].name, symlinks=True)
    if maps:
        shutil.copy2(maps[0], out / "UnityFramework-LinkMap-arm64.txt")
    return " ".join(str(c) for c in cmd)


# ---------------------------------------------------------------- measuring

def zip_components(path):
    comp = {}
    with zipfile.ZipFile(path) as z:
        for i in z.infolist():
            n = i.filename
            if n.startswith("lib/") or n.startswith("base/lib/"):
                key = "/".join(n.split("/")[-2:])
            elif n.endswith("global-metadata.dat"):
                key = "global-metadata.dat"
            elif "/assets/" in "/" + n:
                key = "assets (other)"
            elif n.endswith(".dex"):
                key = "dex"
            else:
                key = "other"
            c = comp.setdefault(key, [0, 0])
            c[0] += i.compress_size
            c[1] += i.file_size
    return {k: {"stored": v[0], "bytes": v[1]} for k, v in comp.items()}


def link_map(path):
    """Bytes each library or object file contributes to the linked binary (live symbols only)."""
    files, sizes, section = {}, {}, None
    for line in path.read_text(errors="replace").splitlines():
        if line.startswith("# ") and line.endswith(":"):  # "# Object files:", "# Symbols:", "# Dead Stripped Symbols:"
            section = line
            continue
        if section == "# Object files:":
            m = re.match(r"\[\s*(\d+)\]\s+(.*)", line)
            if m:
                name = m.group(2)
                lib = re.match(r"(.*?\.a)(\[.*\])?\(", name)
                # Loose objects are the Xcode project's own sources (Classes/, Libraries/Plugins/iOS/*.cpp).
                files[m.group(1)] = Path(lib.group(1)).name if lib else ("objects" if name.endswith(".o") else Path(name).name)
        elif section == "# Symbols:":
            m = re.match(r"0x[0-9A-F]+\s+0x([0-9A-F]+)\s+\[\s*(\d+)\]", line)
            if m:
                key = files.get(m.group(2), "?")
                sizes[key] = sizes.get(key, 0) + int(m.group(1), 16)
    return dict(sorted(sizes.items(), key=lambda kv: -kv[1]))


# Symbols of the legacy library that the minimal preset leaves out (build/xlua_presets.cmake).
NATIVE_PARTS = ["luaopen_io", "luaopen_socket_core"]


def measure_player(player_dir):
    info = json.loads((player_dir / "build-info.json").read_text())
    for name in ["unity-android-smoke.apk", "unity-android-smoke.aab", "unity-webgl-smoke"]:
        if (player_dir / name).exists():
            path = player_dir / name
            break
    else:
        path = next(player_dir.glob("*.app"))
    entry = benchmark.player_entry(info["variant"], path)
    entry.update(info)
    if zipfile.is_zipfile(path) if path.is_file() else False:
        entry["components"] = zip_components(path)
        with zipfile.ZipFile(path) as z:
            # Which xlua library the build packed: the minimal preset's has no io library and no LuaSocket.
            for n in z.namelist():
                if n.endswith("/libxlua.so"):
                    data = z.read(n)
                    entry.setdefault("xlua_library", {})[n] = {s: s.encode() in data for s in NATIVE_PARTS}
    elif path.suffix == ".app":
        files = [f for f in path.rglob("*") if f.is_file() and not f.is_symlink()]
        comp = {"UnityFramework": 0, "global-metadata.dat": 0, "Data (other)": 0, "other": 0}
        for f in files:
            rel = f.relative_to(path).as_posix()
            key = ("UnityFramework" if rel.endswith("UnityFramework.framework/UnityFramework") else
                   "global-metadata.dat" if rel.endswith("global-metadata.dat") else
                   "Data (other)" if rel.startswith("Data/") else "other")
            comp[key] += f.stat().st_size
        entry["components"] = {k: {"bytes": v} for k, v in comp.items()}
        with tempfile.TemporaryDirectory() as tmp:
            # Stand-in for the IPA: the .app zipped with zip's default deflate, as Xcode exports do.
            zipped = Path(tmp) / "app.zip"
            subprocess.run(["zip", "-qry", str(zipped), path.name], cwd=path.parent, check=True)
            entry["zip_bytes"] = zipped.stat().st_size
        lm = player_dir / "UnityFramework-LinkMap-arm64.txt"
        if lm.is_file():
            entry["link_map"] = link_map(lm)
            text = lm.read_text(errors="replace")
            entry["xlua_library"] = {"UnityFramework": {s: ("_" + s + "\n") in text for s in NATIVE_PARTS}}
    else:
        comp = {}
        for f in (path / "Build").iterdir():
            key = re.sub(r"^.*?\.(?=wasm|data|framework\.js|loader\.js)", "", f.name)
            comp[key] = {"bytes": f.stat().st_size}
        entry["components"] = comp
    return entry


def fmt(n):
    return "-" if n is None else "{:,}".format(n)


def delta(n, base):
    if n is None or base is None:
        return "-"
    return "{:+,}".format(n - base)


def comp_bytes(p, key, field="bytes"):
    c = p.get("components", {}).get(key)
    return c.get(field) if c else None


def markdown(data):
    out = []
    groups = {}
    for p in data["players"]:
        groups.setdefault((p["platform"], p["config"]), []).append(p)
    order = {v: i for i, v in enumerate(["baseline", "upstream", "upstream+gen", "ng-5.5",
                                         "ng-5.5+pkg", "ng-5.5+pkg+sg", "ng-5.5+pkg+sg+gcm", "ng-5.5+nosa",
                                         "baseline+game", "ng-5.5+pkg+sg+game",
                                         "ng-5.5+pkg+sg+gcm+game", "ng-5.5+minimal", "core-5.5", "core-5.5+nodefine", "mixed-5.5", "ng-5.4", "ng-5.3", "ng-luajit",
                                         "baseline+uwr", "ng-5.5+uwr", "ng-5.5+nosa+uwr"])}
    for (platform, config), players in groups.items():
        players.sort(key=lambda p: order.get(p["variant"], 99))
        base = next((p for p in players if p["variant"] in ("baseline", "baseline+game")), None)
        uwr_base = next((p for p in players if p["variant"] == "baseline+uwr"), None)
        plain_base = base
        out.append("### %s, %s\n" % (platform, config))
        if base and base.get("unity_settings"):
            out.append("`%s`\n" % base["unity_settings"].split(": ", 1)[1])
        if platform == "android":
            abis = sorted({k.split("/")[0] for p in players for k in p["components"] if k.endswith("libil2cpp.so")})
            head = ["variant", "APK or AAB bytes", "delta"]
            for abi in abis:
                head += [abi + " libxlua.so", abi + " libil2cpp.so delta", abi + " libunity.so delta"]
            head += ["global-metadata delta", "assets delta"]
            rows = []

            def both(p, key, diff):
                if diff:
                    return "%s / %s" % (delta(comp_bytes(p, key, "stored"), base and comp_bytes(base, key, "stored")),
                                        delta(comp_bytes(p, key), base and comp_bytes(base, key)))
                return "%s / %s" % (fmt(comp_bytes(p, key, "stored")), fmt(comp_bytes(p, key))) if key in p["components"] else "-"
            for p in players:
                # both() reads base: +uwr variants are measured against the baseline that also uses UnityWebRequest.
                base = uwr_base if "uwr" in p["variant"].split("+") and uwr_base else plain_base
                r = [p["variant"], fmt(p["bytes"]), delta(p["bytes"], base and base["bytes"])]
                for abi in abis:
                    r += [both(p, abi + "/libxlua.so", False), both(p, abi + "/libil2cpp.so", True), both(p, abi + "/libunity.so", True)]
                r += [both(p, "global-metadata.dat", True), both(p, "assets (other)", True)]
                rows.append(r)
            out.append("Package bytes; component cells are compressed in the package / uncompressed.\n")
        elif platform == "ios":
            head = ["variant", ".app zip bytes", "delta", "UnityFramework bytes", "delta", "libxlua.a (link map)",
                    "libGameAssembly.a delta", "libiPhone-lib.a delta", "global-metadata delta", "Data delta"]
            rows = []
            for p in players:
                lm, blm = p.get("link_map", {}), (base or {}).get("link_map", {})
                rows.append([p["variant"], fmt(p.get("zip_bytes")), delta(p.get("zip_bytes"), base and base.get("zip_bytes")),
                             fmt(comp_bytes(p, "UnityFramework")),
                             delta(comp_bytes(p, "UnityFramework"), base and comp_bytes(base, "UnityFramework")),
                             fmt(lm.get("libxlua.a")) if lm else "-",
                             delta(lm.get("libGameAssembly.a"), blm.get("libGameAssembly.a")),
                             delta(lm.get("libiPhone-lib.a"), blm.get("libiPhone-lib.a")),
                             delta(comp_bytes(p, "global-metadata.dat"), base and comp_bytes(base, "global-metadata.dat")),
                             delta(comp_bytes(p, "Data (other)"), base and comp_bytes(base, "Data (other)"))])
        else:
            kinds = sorted({k for p in players for k in p["components"]})
            head = ["variant", "total bytes", "delta"] + [k + " (delta)" for k in kinds]
            rows = [[p["variant"], fmt(p["bytes"]), delta(p["bytes"], base and base["bytes"])] +
                    ["%s (%s)" % (fmt(comp_bytes(p, k)), delta(comp_bytes(p, k), base and comp_bytes(base, k))) for k in kinds]
                    for p in players]
        out.append("| " + " | ".join(head) + " |")
        out.append("|" + " --- |" * len(head))
        out += ["| " + " | ".join(r) + " |" for r in rows]
        out.append("")
    if data["plugins"]:
        out.append("### Native plugins\n")
        out.append("| plugin | format | arch | bytes | stripped | stripped, gzip -9 |")
        out.append("| --- | --- | --- | --- | --- | --- |")
        for s in data["plugins"]:
            out.append("| %s | %s | %s | %s | %s | %s |" % (s["label"], s["format"], s["arch"] or "-", fmt(s["bytes"]),
                                                          fmt(s["stripped_bytes"]), fmt(s["stripped_gzip_bytes"])))
        out.append("")
    return "\n".join(out)


def report(a):
    work = a.work.resolve()
    players = sorted((work / "players").glob("*/*/*/build-info.json"))
    data = {"schema": 1, "created": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
            "commit": benchmark.commit(), "upstream_commit": a.upstream_commit,
            "players": [measure_player(p.parent) for p in players], "plugins": []}
    for label, path in a.size:
        path = path.resolve()
        if path.suffix == ".a" and b"armv7" in subprocess.run(["lipo", "-info", str(path)], stdout=subprocess.PIPE).stdout:
            thin = work / "tmp" / (label.replace("/", "_") + "-arm64.a")
            subprocess.run(["lipo", "-thin", "arm64", str(path), "-output", str(thin)], check=True)
            path = thin
        data["plugins"].append(benchmark.size_entry(label, path))
    (work / "size-report.json").write_text(json.dumps(data, indent=1) + "\n")
    (work / "size-report.md").write_text(markdown(data) + "\n")
    print(markdown(data))
    print("Wrote", work / "size-report.json", "and", work / "size-report.md")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("command", choices=["run", "report"])
    p.add_argument("--work", required=True, type=Path, help="work directory (external disk): projects, players, report")
    p.add_argument("--plugins", type=Path, help="XLUA_BUILD_ROOT of the make_*.sh builds (plugin_lua55/... inside)")
    p.add_argument("--unity-mobile", type=Path, help="Unity executable with the Android and iOS modules")
    p.add_argument("--unity-webgl", type=Path, help="Unity executable with the WebGL module")
    p.add_argument("--platforms", default="android,ios,webgl")
    p.add_argument("--only", help="regex on PLATFORM/VARIANT; build only the matching projects")
    p.add_argument("--configs", help="comma-separated configurations from CASES (such as default); build only those")
    p.add_argument("--variants", help="comma-separated variants (such as ng-5.5+gsize) built in every selected "
                   "configuration instead of its list in CASES")
    p.add_argument("--upstream-commit", default="59bf426")
    p.add_argument("--lock", type=Path, help="mkdir lock held while Unity or xcodebuild runs")
    p.add_argument("--timeout", type=int, default=3600, help="seconds per Unity or xcodebuild run")
    p.add_argument("--one-unity", action="store_true",
                   help="before each Unity launch, wait until no other Unity editor runs on this machine")
    p.add_argument("--size", type=benchmark.label_path, action="append", default=[], metavar="LABEL=PATH",
                   help="report: a native plugin to record raw and stripped (set STRIP to the NDK llvm-strip for ELF)")
    a = p.parse_args()
    if a.command == "run":
        if not a.plugins:
            p.error("run needs --plugins")
        a.plugins = a.plugins.resolve()
        run(a)
    else:
        report(a)


if __name__ == "__main__":
    main()
