#!/usr/bin/env python3
"""Compile xLua's Unity C# against installed Unity editors without starting Unity.

The Unity editor suite is the only other check that compiles the Editor-only
code (`#if UNITY_EDITOR`, `Assets/XLua/Src/Editor`, the hotfix Editor parts),
and it needs a licence and a running editor. This script only reads an
editor's reference assemblies and runs a C# compiler on them, the way Unity's
own build does:

- in the asmdef layout (the default since 2026-10-18; xLua's four asmdefs):
  `XLua.Core` (`Assets/XLua/Core` without `Editor/`) and `XLua`
  (`Assets/XLua/Src` without `Editor/`, referencing `XLua.Core`), against .NET
  Standard 2.1 (`NetStandard/ref/2.1.0` and its shims) and the UnityEngine
  modules; in Editor configurations also `XLua.Core.Editor` and `XLua.Editor`
  (their `Editor` folders), against `unity-4.8-api`, UnityEngine and
  UnityEditor. Test sources go into `Assembly-CSharp` and
  `Assembly-CSharp-Editor`, which reference all of them;
- in the Assets layout (the `assets-*` configurations, after XLua > Project
  Layout > Use Assembly-CSharp): `Assembly-CSharp` with `Assets/XLua/Core` and
  `Assets/XLua/Src` without `Editor/`, and `Assembly-CSharp-Editor` (Editor
  configurations only) with their `Editor` folders, which references
  `Assembly-CSharp`;
- with the `tests` configurations, the Test/UnityMatrix and Test/MobileSmoke
  sources that `Test/UnityMatrix/prepare.py --editor-tools
  --fast-enter-play-mode` copies, against the editor's bundled nunit and
  test-framework packages (compiled from their sources first). The C# 12
  fixture (`--csharp12`) needs a compiler that knows C# 12, such as Unity
  6000.6's own (`--compiler unity`); `XluaRefReadonlyLib*` (`--roslyn-sdk`)
  is left out.

The `source-gen` configurations also run the xLua source generator
(Tools/build_source_generator.py, built once into --output) in `XLua` and in
`Assembly-CSharp`, as Unity runs it next to `XLua.asmdef`, with the editor
suite's configuration (the attributes in
`Test/UnityMatrix/XluaEditorTestTarget.cs`) in `Assembly-CSharp`. They always use the editor's
own Roslyn, since the generator needs Roslyn 4.3.1 or later, and check the
generated wrappers: native callbacks, or with `GEN_CODE_MINIMIZE` (also implied
by `XLUA_PRESET_MINIMAL` unless `XLUA_NO_GCM` is defined) the GCM functions
without them (docs/design/source-generator.md, "GCM mode as implemented"), and
fail on warning XLSG013, a mode that differs between xLua and Assembly-CSharp.

Player configurations compile the runtime assemblies without UNITY_EDITOR and
without the UnityEditor assemblies, with the platform and scripting backend
defines that the xLua sources branch on. The version defines
(`UNITY_2021_3_OR_NEWER`, `UNITY_6000_0_OR_NEWER`, ...) follow each editor.

Errors fail the run; warnings are counted and listed with --warnings. The
response files and compiler logs stay in --output.

Examples:

    python3 Test/Native/editor_compile_check.py --output /tmp/ecc
    python3 Test/Native/editor_compile_check.py --unity /Applications/Unity/Hub/Editor/6000.3.26f1 \\
        --config 'editor-*' --output /tmp/ecc
    python3 Test/Native/editor_compile_check.py --unity /opt/unity --unity-version 2022.3.62f3 \\
        --compiler unity --output /tmp/ecc
"""
import argparse
import concurrent.futures
import fnmatch
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import sys
import tarfile
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools"))
import presets  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
SRC = REPO / "Assets/XLua/Src"
# XLua.Core (Assets/XLua/Core): in the Assets layout it compiles into the same
# two assemblies, its Editor folder into Assembly-CSharp-Editor.
CORE = REPO / "Assets/XLua/Core"
DISCOVER = [Path("/Volumes/T7Data/Unity/Editors"), Path("/Applications/Unity/Hub/Editor"),
            Path.home() / "Unity/Hub/Editor", Path("/opt/unity")]
VERSION_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+)([abfpcx]\d+)?$")

# Sources that prepare.py --editor-tools --fast-enter-play-mode puts outside
# and inside Editor folders.
TEST_RUNTIME = ["Test/MobileSmoke/XluaSmoke.cs", "Test/UnityMatrix/XluaEditorTestTarget.cs",
                "Test/UnityMatrix/XluaPlayModeHost.cs", "Test/MobileSmoke/StreamingAssetsSmoke.cs"]
TEST_EDITOR = ["Test/MobileSmoke/SmokeBuild.cs", "Test/UnityMatrix/XluaSmokeTests.cs",
               "Test/UnityMatrix/XluaEditorToolsTests.cs", "Test/UnityMatrix/XluaGeneratedCodeTests.cs",
               "Test/UnityMatrix/XluaStringTests.cs", "Test/UnityMatrix/XluaEditorTestSteps.cs",
               "Test/UnityMatrix/XluaPlayModeTests.cs", "Test/UnityMatrix/XluaPluginVariantTests.cs"]
# The C# 12 fixture of prepare.py --csharp12, compiled with -langversion:12 by
# the csharp12 configuration when the compiler knows C# 12 (Unity 6000.6's).
TEST_CSHARP12_RUNTIME = ["Test/UnityMatrix/XluaRefReadonlyTarget.cs"]
TEST_CSHARP12_EDITOR = ["Test/UnityMatrix/XluaRefReadonlyTests.cs"]
# prepare.py --roslyn-sdk first builds XluaRefReadonlyLib.cs into a DLL with
# the .NET SDK; not compiled here.
TEST_SKIPPED = ["XluaRefReadonlyLib.cs", "XluaRefReadonlyLibConfig.cs", "XluaRefReadonlyLibTests.cs"]

FEATURES = ["XLUA_NO_SIGNATURE_LOADER", "XLUA_NO_TEMPLATE", "XLUA_NO_UTIL", "XLUA_NO_PROFILER",
            "XLUA_NO_STREAMING_ASSETS_LOADER", "XLUA_LOCK_METATABLES",
            # Bridge hardening (docs/design/bridge-hardening.md). The bridge checks follow
            # the build type; XLUA_BRIDGE_CHECKS forces them into a release player
            # (player-android-il2cpp-xlua-bridge-checks), XLUA_NO_BRIDGE_CHECKS leaves them out.
            "XLUA_BRIDGE_CHECKS", "XLUA_NO_BRIDGE_CHECKS", "XLUA_RESTRICT_GETMETATABLE",
            "XLUA_LOCK_XLUA_TABLE", "XLUA_RESTRICT_PRIVATE_ACCESS", "XLUA_NO_LOAD_ASSEMBLY",
            # Lua's integer rule with range checks (docs/research/bridge-audit.md, C4 part 2).
            "XLUA_STRICT_INT_RANGE"]

# The source-gen configurations: the editor suite's types and their configuration attributes.
SOURCE_GEN_RUNTIME = ["Test/UnityMatrix/XluaEditorTestTarget.cs"]
SOURCE_GEN_CONFIGS = set()
GCM_PARAMETERS = "(this global::XLua.ObjectTranslator translator, global::System.IntPtr L, int gen_param_count)"

# The minimal preset: one define, expanded in each file by Tools/presets.py.
MINIMAL = ["XLUA_PRESET_MINIMAL"]

# Defines Unity sets for every script compilation that the xLua sources or
# the tests could see. The full list is in Library/Bee/artifacts/*/*.rsp.
COMMON = ["UNITY_64", "DEBUG", "TRACE", "UNITY_ASSERTIONS", "CSHARP_7_OR_LATER", "CSHARP_7_3_OR_NEWER"]
EDITOR_HOSTS = {
    "osx": ["UNITY_EDITOR_OSX", "UNITY_STANDALONE", "UNITY_STANDALONE_OSX"],
    "win": ["UNITY_EDITOR_WIN", "UNITY_STANDALONE", "UNITY_STANDALONE_WIN"],
    "linux": ["UNITY_EDITOR_LINUX", "UNITY_STANDALONE", "UNITY_STANDALONE_LINUX"],
}
PLAYERS = {
    "standalone-osx-mono": ["UNITY_STANDALONE", "UNITY_STANDALONE_OSX", "ENABLE_MONO"],
    "standalone-win-il2cpp": ["UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "ENABLE_IL2CPP"],
    "android-mono": ["UNITY_ANDROID", "ENABLE_MONO"],
    "android-il2cpp": ["UNITY_ANDROID", "ENABLE_IL2CPP"],
    "android-source-plugin": ["UNITY_ANDROID", "ENABLE_IL2CPP", "XLUA_ANDROID_SOURCE_PLUGIN"],
    "ios": ["UNITY_IOS", "UNITY_IPHONE", "ENABLE_IL2CPP"],
    "tvos": ["UNITY_TVOS", "ENABLE_IL2CPP"],
    "webgl": ["UNITY_WEBGL", "ENABLE_IL2CPP"],
    "switch": ["UNITY_SWITCH", "ENABLE_IL2CPP"],
    # SignatureLoader uses Windows.Security.Cryptography on UWP, which needs the
    # Windows SDK's Windows.winmd: not part of a Unity install, so it is left out.
    "wsa-il2cpp": ["UNITY_WSA", "UNITY_WSA_10_0", "UNITY_WINRT", "UNITY_WINRT_10_0", "ENABLE_IL2CPP",
                   "XLUA_NO_SIGNATURE_LOADER"],
}


# Configurations repeated in the Assets layout (xLua in Assembly-CSharp), as
# "assets-" + name: the main platforms, hotfix, the features and the presets.
ASSETS_LAYOUT = ["editor-osx", "editor-win", "editor-osx-hotfix", "editor-osx-features", "editor-osx-tests",
                 "editor-osx-hotfix-tests", "editor-osx-minimal", "editor-osx-luascript", "player-android-il2cpp",
                 "player-ios", "player-webgl", "player-android-il2cpp-hotfix", "player-ios-features-hotfix",
                 "player-android-il2cpp-minimal", "player-android-il2cpp-everything",
                 # The source generator in the way back's Assembly-CSharp, with xLua in it: the only
                 # generated bindings that layout gets since the legacy generator went (M11).
                 "editor-osx-source-gen", "player-android-il2cpp-source-gen"]


def configurations():
    """(name, kind, defines, tests, layout) for every configuration, in report order.

    tests is False, True (the test sources) or "csharp12" (also the C# 12 fixture);
    layout is "asmdef" (xLua's four asmdefs, the default) or "assets" (no asmdefs).
    """
    out = []

    def editor(name, extra=(), host="osx", tests=False, source_gen=False):
        out.append((name, "editor", ["UNITY_EDITOR", "UNITY_EDITOR_64", "ENABLE_MONO"]
                    + EDITOR_HOSTS[host] + list(extra), tests, "asmdef"))
        if source_gen:
            SOURCE_GEN_CONFIGS.add(name)

    def player(name, base, extra=(), source_gen=False):
        out.append((name, "player", PLAYERS[base] + list(extra), False, "asmdef"))
        if source_gen:
            SOURCE_GEN_CONFIGS.add(name)

    editor("editor-osx")
    editor("editor-win", host="win")
    editor("editor-linux", host="linux")
    editor("editor-osx-hotfix", ["HOTFIX_ENABLE"])
    editor("editor-osx-features", FEATURES)
    editor("editor-osx-thread-safe", ["THREAD_SAFE"])
    editor("editor-osx-gen-code-minimize", ["GEN_CODE_MINIMIZE"])
    editor("editor-osx-legacy-options", ["FORCE_REFLECTION", "GENERIC_SHARING", "DISABLE_OBSOLETE_WARNING",
                                         "NOT_GEN_WARNING"])
    # Active build target other than the editor's own platform.
    editor("editor-target-android", ["UNITY_ANDROID"])
    editor("editor-target-ios", ["UNITY_IOS", "UNITY_IPHONE"])
    editor("editor-target-webgl", ["UNITY_WEBGL"])
    # XLUA_SMOKE_STREAMING_ASSETS: StreamingAssetsSmoke.cs (Test/MobileSmoke/prepare.py --streaming-assets).
    editor("editor-osx-tests", ["UNITY_INCLUDE_TESTS", "XLUA_SMOKE_LUA55", "XLUA_SMOKE_STREAMING_ASSETS"], tests=True)
    editor("editor-osx-hotfix-tests", ["UNITY_INCLUDE_TESTS", "XLUA_SMOKE_LUA55", "HOTFIX_ENABLE"], tests=True)
    editor("editor-osx-features-tests", ["UNITY_INCLUDE_TESTS", "XLUA_SMOKE_LUA55"] + FEATURES, tests=True)
    editor("editor-osx-minimal", MINIMAL)
    editor("editor-osx-minimal-tests", ["UNITY_INCLUDE_TESTS", "XLUA_SMOKE_LUA55", "XLUA_SMOKE_STREAMING_ASSETS"] + MINIMAL, tests=True)
    # The preset's opt-in back to the reflection fallback and CodeEmit (XLUA_REFLECTION).
    editor("editor-osx-minimal-reflection", MINIMAL + ["XLUA_REFLECTION"])
    editor("editor-osx-csharp12-tests", ["UNITY_INCLUDE_TESTS", "XLUA_SMOKE_LUA55"], tests="csharp12")
    # LuaScript next to the bridge (XLUA_LUASCRIPT; core-luascript.md, section 2.2.2).
    editor("editor-osx-luascript", ["XLUA_LUASCRIPT"])
    # The source generator, plain and in its GEN_CODE_MINIMIZE mode.
    editor("editor-osx-source-gen", source_gen=True)
    editor("editor-osx-gen-code-minimize-source-gen", ["GEN_CODE_MINIMIZE"], source_gen=True)
    # The minimal preset implies the GCM mode (2026-10-19); XLUA_NO_GCM keeps the plain wrappers.
    editor("editor-osx-minimal-source-gen", MINIMAL, source_gen=True)
    editor("editor-osx-minimal-no-gcm-source-gen", MINIMAL + ["XLUA_NO_GCM"], source_gen=True)
    for base in PLAYERS:
        player("player-" + base, base)
    player("player-standalone-osx-mono-hotfix", "standalone-osx-mono", ["HOTFIX_ENABLE"])
    player("player-android-il2cpp-hotfix", "android-il2cpp", ["HOTFIX_ENABLE"])
    for define in FEATURES + ["GEN_CODE_MINIMIZE", "THREAD_SAFE"]:
        player("player-android-il2cpp-" + define.lower().replace("_", "-"), "android-il2cpp", [define])
    player("player-android-il2cpp-development", "android-il2cpp", ["DEVELOPMENT_BUILD", "XLUA_NO_PROFILER"])
    # The memory diagnostics in a release player, with the defines their code branches on.
    player("player-android-il2cpp-memory-diagnostics", "android-il2cpp",
           ["XLUA_MEMORY_DIAGNOSTICS", "XLUA_NO_UTIL", "XLUA_NO_PROFILER", "SINGLE_ENV", "THREAD_SAFE"])
    player("player-android-il2cpp-features", "android-il2cpp", FEATURES)
    player("player-android-il2cpp-everything", "android-il2cpp",
           FEATURES + ["GEN_CODE_MINIMIZE", "THREAD_SAFE", "HOTFIX_ENABLE"])
    player("player-ios-features-hotfix", "ios", FEATURES + ["HOTFIX_ENABLE", "GEN_CODE_MINIMIZE"])
    player("player-android-il2cpp-minimal", "android-il2cpp", MINIMAL)
    player("player-android-il2cpp-minimal-util", "android-il2cpp", MINIMAL + ["XLUA_UTIL"])
    player("player-android-il2cpp-minimal-development", "android-il2cpp", MINIMAL + ["DEVELOPMENT_BUILD"])
    player("player-ios-minimal-hotfix", "ios", MINIMAL + ["HOTFIX_ENABLE", "GEN_CODE_MINIMIZE"])
    player("player-android-il2cpp-luascript", "android-il2cpp", ["XLUA_LUASCRIPT"])
    player("player-ios-luascript", "ios", ["XLUA_LUASCRIPT"])
    player("player-webgl-luascript", "webgl", ["XLUA_LUASCRIPT"])
    player("player-webgl-minimal", "webgl", MINIMAL)
    player("player-android-il2cpp-source-gen", "android-il2cpp", source_gen=True)
    player("player-android-il2cpp-gen-code-minimize-source-gen", "android-il2cpp", ["GEN_CODE_MINIMIZE"], source_gen=True)
    player("player-android-il2cpp-minimal-source-gen", "android-il2cpp", MINIMAL, source_gen=True)
    player("player-android-il2cpp-minimal-no-gcm-source-gen", "android-il2cpp", MINIMAL + ["XLUA_NO_GCM"], source_gen=True)
    player("player-legacy-options", "android-il2cpp", ["NATIVE_LUA_PUSHSTRING", "SINGLE_ENV", "GENERIC_SHARING",
                                                       "NOT_GEN_WARNING"])
    by_name = {c[0]: c for c in out}
    out += [("assets-" + n,) + by_name[n][1:4] + ("assets",) for n in ASSETS_LAYOUT]
    SOURCE_GEN_CONFIGS.update("assets-" + n for n in ASSETS_LAYOUT if n in SOURCE_GEN_CONFIGS)
    return out


class EditorError(Exception):
    pass


class Editor:
    """An installed Unity editor: macOS Unity.app or the Linux Editor/Data layout."""

    def __init__(self, path, version=None):
        path = Path(path).resolve()
        if (path / "Unity.app").is_dir():
            path = path / "Unity.app"
        if path.suffix == ".app":
            base = path / "Contents"
        elif (path / "Editor/Data").is_dir():
            base = path / "Editor/Data"
        elif (path / "Data/Managed").is_dir() or (path / "Data/Resources/Scripting").is_dir():
            base = path / "Data"
        else:
            base = path
        for scripting in (base / "Resources/Scripting", base):
            if (scripting / "Managed/UnityEngine/UnityEngine.dll").is_file():
                break
        else:
            raise EditorError("no Managed/UnityEngine/UnityEngine.dll under " + str(base))
        self.path = path
        self.base = base
        self.scripting = scripting
        self.packages = base / "Resources/PackageManager"
        self.version = version or self._version()
        if not self.version or not VERSION_RE.match(self.version):
            raise EditorError("cannot tell the Unity version of %s; pass --unity-version" % path)

    def _version(self):
        plist = self.base / "Info.plist"
        if plist.is_file():
            with plist.open("rb") as f:
                version = plistlib.load(f).get("CFBundleVersion", "")
            if VERSION_RE.match(version):
                return version
        for part in reversed(self.path.parts):
            if VERSION_RE.match(part):
                return part
        return None

    def version_defines(self):
        major, minor, patch = (int(x) for x in VERSION_RE.match(self.version).groups()[:3])
        out = ["UNITY_%d_%d_%d" % (major, minor, patch), "UNITY_%d_%d" % (major, minor), "UNITY_%d" % major]
        lines = [(5, [3, 4, 5, 6]), (2017, [1, 2, 3, 4]), (2018, [1, 2, 3, 4]), (2019, [1, 2, 3, 4]),
                 (2020, [1, 2, 3]), (2021, [1, 2, 3]), (2022, [1, 2, 3]), (2023, [1, 2, 3])]
        if major >= 6000:
            lines.append((6000, range(0, minor + 1)))
        for line, minors in lines:
            for n in minors:
                if (line, n) <= (major, minor):
                    out.append("UNITY_%d_%d_OR_NEWER" % (line, n))
        return out

    def bcl(self, profile):
        """Reference assemblies and defines of an API compatibility level."""
        s = self.scripting
        if profile == "netstandard2.1":
            refs = [s / "NetStandard/ref/2.1.0/netstandard.dll"]
            for folder in ["NetStandard/compat/2.1.0/shims/netstandard", "NetStandard/compat/2.1.0/shims/netfx",
                           "NetStandard/Extensions/2.0.0"]:
                refs += sorted((s / folder).glob("*.dll"))
            defines = ["NET_STANDARD_2_0", "NET_STANDARD", "NET_STANDARD_2_1", "NETSTANDARD", "NETSTANDARD2_1"]
        else:
            api = s / "UnityReferenceAssemblies/unity-4.8-api"
            if not api.is_dir():
                api = s / "MonoBleedingEdge/lib/mono/4.7.1-api"
            refs = sorted(api.glob("*.dll")) + sorted((api / "Facades").glob("*.dll"))
            defines = ["NET_4_6", "NET_UNITY_4_8"]
        refs += sorted((s / "BCLExtensions/TargetingPacks/netstandard2.1/ref").glob("*.dll"))
        missing = [r for r in refs[:1] if not r.is_file()]
        if missing:
            raise EditorError("missing " + str(missing[0]))
        return refs, defines

    def engine(self, with_editor):
        """UnityEngine (and UnityEditor) assemblies as Unity references them."""
        managed = self.scripting / "Managed"
        refs = []
        for dll in sorted((managed / "UnityEngine").glob("*.dll")):
            if dll.name.startswith("UnityEngine"):
                refs.append(dll)
            elif with_editor and dll.name.startswith("UnityEditor") and dll.name != "UnityEditor.MSBuild.dll":
                refs.append(dll)
        refs += sorted((managed / "Bootstrap").glob("Unity.Scripting.dll"))
        if with_editor:
            refs += [p for p in [managed / "UnityEditor.Graphs.dll"] if p.is_file()]
        return refs

    def cecil(self):
        return sorted((self.scripting / "Managed/UnityEngine").glob("Unity.Cecil*.dll"))

    def roslyn(self):
        """The editor's own C# compiler: (dotnet, csc.dll)."""
        s = self.scripting
        candidates = [(s / "NetCoreRuntime/dotnet", s / "DotNetSdkRoslyn/csc.dll")]
        candidates += [(s / "DotNetSdk/dotnet", c) for c in sorted((s / "DotNetSdk/sdk").glob("*/Roslyn/bincore/csc.dll"))]
        for dotnet, csc in candidates:
            for exe in (dotnet, dotnet.with_suffix(".exe")):
                if exe.is_file() and csc.is_file():
                    return [str(exe), str(csc)]
        return None

    def package(self, name, out):
        """A package bundled with the editor, extracted from its .tgz if needed."""
        builtin = self.packages / "BuiltInPackages" / name
        if (builtin / "package.json").is_file():
            return builtin
        tgzs = sorted((self.packages / "Editor").glob(name + "-[0-9]*.tgz"))
        if not tgzs:
            return None
        dest = out / "packages" / tgzs[-1].name[:-len(".tgz")]
        if not (dest / "package.json").is_file():
            tmp = dest.with_name(dest.name + ".tmp")
            shutil.rmtree(tmp, ignore_errors=True)
            with tarfile.open(tgzs[-1]) as tar:
                try:
                    tar.extractall(tmp, filter="data")
                except TypeError:  # Python before 3.12 and the backports
                    tar.extractall(tmp)
            shutil.rmtree(dest, ignore_errors=True)
            (tmp / "package").rename(dest)
            shutil.rmtree(tmp, ignore_errors=True)
        return dest


def package_version(pkg):
    m = re.search(r'"version"\s*:\s*"([^"]+)"', (pkg / "package.json").read_text(encoding="utf-8"))
    return m.group(1) if m else "?"


class Result:
    def __init__(self, status, errors=(), warnings=(), seconds=0.0, note=""):
        self.status = status  # ok, fail or skip
        self.errors = list(errors)
        self.warnings = list(warnings)
        self.seconds = seconds
        self.note = note


DIAG_RE = re.compile(r"^(?P<file>.*?)(\((?P<pos>\d+,\d+)\))?\s*:\s*(?P<kind>error|warning)\s+(?P<code>CS\d+)\s*:\s*(?P<msg>.*)$")


def relative(path):
    try:
        return str(Path(path).resolve().relative_to(REPO))
    except (ValueError, OSError):
        return path


def compile_assembly(compiler, name, out_dir, sources, refs, defines, langversion, extra=()):
    """Compile one assembly; returns (ok, errors, warnings)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    rsp = out_dir / (name + ".rsp")
    lines = ["-nologo", "-nostdlib", "-target:library", "-unsafe", "-deterministic",
             "-langversion:" + langversion, "-nowarn:0169,0649,0282,1701,1702",
             "-out:" + str(out_dir / (name + ".dll"))] + list(extra)
    lines += ["-define:" + ";".join(defines)]
    lines += ['-r:"%s"' % r for r in refs]
    lines += ['"%s"' % s for s in sources]
    rsp.write_text("\n".join(lines) + "\n", encoding="utf-8")
    # -noconfig only works on the command line, not in a response file.
    proc = subprocess.run(compiler + ["-noconfig", "@" + str(rsp)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, errors="replace")
    (out_dir / (name + ".log")).write_text(proc.stdout, encoding="utf-8")
    errors, warnings = [], []
    for line in proc.stdout.splitlines():
        m = DIAG_RE.match(line.strip())
        if not m:
            continue
        where = relative(m.group("file")) + ("(%s)" % m.group("pos") if m.group("pos") else "")
        text = "%s: %s %s: %s" % (where, m.group("kind"), m.group("code"), m.group("msg"))
        (errors if m.group("kind") == "error" else warnings).append(text)
    if proc.returncode != 0 and not errors:
        errors.append("%s exited with %d: %s" % (compiler[-1], proc.returncode, proc.stdout.strip()[-400:]))
    return proc.returncode == 0 and not errors, errors, warnings


def asm_sources(folder):
    """C# files of an asmdef folder, without subfolders that have their own asmdef."""
    owned = [d for d in folder.rglob("*.asmdef") if d.parent != folder]
    skip = [d.parent for d in owned]
    return sorted(p for p in folder.rglob("*.cs") if not any(s in p.parents for s in skip))


def build_test_framework(editor, compiler, out, langversion):
    """Compile the editor's bundled nunit/test-framework; returns (refs, note) or (None, reason)."""
    nunit_pkg = editor.package("com.unity.ext.nunit", out)
    tf_pkg = editor.package("com.unity.test-framework", out)
    if nunit_pkg is None or tf_pkg is None:
        return None, "the editor bundles no com.unity.ext.nunit or com.unity.test-framework package"
    nunits = sorted(nunit_pkg.glob("net*/unity-custom/nunit.framework.dll"))
    if not nunits:
        return None, "no nunit.framework.dll in " + str(nunit_pkg)
    nunit = nunits[-1]
    note = "nunit %s, test-framework %s" % (package_version(nunit_pkg), package_version(tf_pkg))
    host = ["UNITY_EDITOR", "UNITY_EDITOR_64", "ENABLE_MONO"] + EDITOR_HOSTS["osx"]
    defines = editor.version_defines() + COMMON + host + ["UNITY_INCLUDE_TESTS", "UNITY_TESTS_FRAMEWORK"]
    tf_out = out / "test-framework"
    engine_refs = editor.engine(True)
    refs, bcl_defines = editor.bcl("netstandard2.1")
    ok, errors, _ = compile_assembly(compiler, "UnityEngine.TestRunner", tf_out,
                                     asm_sources(tf_pkg / "UnityEngine.TestRunner"),
                                     refs + engine_refs + [nunit], defines + bcl_defines, langversion, ["-warn:0"])
    if not ok:
        return None, "%s: UnityEngine.TestRunner did not compile: %s" % (note, errors[0])
    engine_runner = tf_out / "UnityEngine.TestRunner.dll"
    refs, bcl_defines = editor.bcl("net4.8")
    ok, errors, _ = compile_assembly(compiler, "UnityEditor.TestRunner", tf_out,
                                     asm_sources(tf_pkg / "UnityEditor.TestRunner"),
                                     refs + engine_refs + [nunit, engine_runner] + editor.cecil(),
                                     defines + bcl_defines + ["UNITY_EDITOR_ONLY_COMPILATION"], langversion,
                                     ["-warn:0"])
    if not ok:
        return None, "%s: UnityEditor.TestRunner did not compile: %s" % (note, errors[0])
    return [nunit, engine_runner, tf_out / "UnityEditor.TestRunner.dll"], note


def check_generated(generated, gcm):
    """Errors in the source generator's wrappers of a source-gen configuration."""
    wrappers = sorted(generated.rglob("W_*.g.cs"))
    if not list(generated.rglob("XLua.Bindings.g.cs")) or not wrappers:
        return ["the source generator wrote no bindings into " + str(generated)]
    errors = []
    functions = 0
    for f in wrappers:
        text = f.read_text(encoding="utf-8")
        if gcm:
            functions += text.count(GCM_PARAMETERS)
            if "MonoPInvokeCallback" in text or "lua_gettop" in text:
                errors.append("%s: a GCM wrapper with per-member callbacks" % f.name)
        else:
            functions += text.count("[global::XLua.MonoPInvokeCallback(")
            if GCM_PARAMETERS in text:
                errors.append("%s: GCM functions without the GCM mode" % f.name)
    if functions == 0:
        errors.append("no wrapper functions in " + str(generated))
    return errors


# IL disassemblers that come with Mono; monodis asserts on some assemblies
# (get.c:913, the Assets layout with XLUA_LUASCRIPT), so ikdasm goes first.
DISASSEMBLERS = [d for d in (shutil.which("ikdasm"), shutil.which("monodis")) if d]
# The bridge checks (docs/design/bridge-hardening.md) call these exports; the
# DllImport declarations stay in every build, so only a call ("Bridge::name") counts.
BRIDGE_CHECK_CALLS = ["xlua_tryget_cachedud_checked", "xlua_check_struct_accessors", "xlua_check_registry_refs"]


def bridge_checks_expected(kind, defines):
    """Tools/presets.py BUILD_TYPE: on in the Editor and Development Builds, unless
    XLUA_NO_BRIDGE_CHECKS; XLUA_BRIDGE_CHECKS turns them on in any build."""
    return "XLUA_BRIDGE_CHECKS" in defines or (
        (kind == "editor" or "DEVELOPMENT_BUILD" in defines) and "XLUA_NO_BRIDGE_CHECKS" not in defines)


def check_bridge_checks(dll, expected):
    """Errors when the compiled runtime calls the bridge check exports in a build that
    should leave them out, or the other way round (needs ikdasm or monodis)."""
    for tool in DISASSEMBLERS:
        proc = subprocess.run([tool, str(dll)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                              errors="replace", timeout=300)
        if proc.returncode == 0:
            text = proc.stdout
            break
    else:
        return ["%s: no disassembler read it (%s)" % (dll.name, ", ".join(DISASSEMBLERS))]
    errors = []
    for name in BRIDGE_CHECK_CALLS:
        found = "Bridge::" + name + "(" in text
        if found != expected:
            errors.append("%s: the bridge checks should be %s, but %s %s %s" % (
                dll.name, "compiled in" if expected else "left out", "it calls" if found else "nothing calls",
                name, "(XLUA_BRIDGE_CHECKS, Tools/presets.py BUILD_TYPE)"))
    return errors


def run_config(editor, compiler, out, config, test_refs, langversion, analyzer=None):
    name, kind, defines, tests, layout = config
    start = time.monotonic()
    if tests and test_refs is None:
        return Result("skip", note="no test framework")
    work = out / name
    shutil.rmtree(work, ignore_errors=True)
    base_defines = editor.version_defines() + COMMON + defines
    extra_refs = list(test_refs) if tests else []
    test_runtime = [REPO / s for s in TEST_RUNTIME] if tests else []
    test_editor = [REPO / s for s in TEST_EDITOR] if tests else []
    if tests == "csharp12":
        test_runtime += [REPO / s for s in TEST_CSHARP12_RUNTIME]
        test_editor += [REPO / s for s in TEST_CSHARP12_EDITOR]
        langversion = "12"
    source_gen = name in SOURCE_GEN_CONFIGS
    if source_gen:
        compiler = editor.roslyn()
        if compiler is None or analyzer is None:
            return Result("skip", note="needs the editor's Roslyn and the source generator")
        test_runtime += [REPO / s for s in SOURCE_GEN_RUNTIME]

    def runtime_of(folder):
        return sorted(p for p in folder.rglob("*.cs") if (folder / "Editor") not in p.parents)

    def editor_of(folder):
        return sorted((folder / "Editor").rglob("*.cs"))

    # (assembly, sources, Editor only, the assemblies before it that it references)
    if layout == "asmdef":
        plan = [("XLua.Core", runtime_of(CORE), False, []), ("XLua", runtime_of(SRC), False, ["XLua.Core"])]
        if test_runtime:
            plan.append(("Assembly-CSharp", test_runtime, False, ["XLua.Core", "XLua"]))
        if kind == "editor":
            plan += [("XLua.Core.Editor", editor_of(CORE), True, ["XLua.Core"]),
                     ("XLua.Editor", editor_of(SRC), True, ["XLua.Core", "XLua"])]
            if test_editor:
                plan.append(("Assembly-CSharp-Editor", test_editor, True, [p[0] for p in plan]))
    else:
        plan = [("Assembly-CSharp", runtime_of(CORE) + runtime_of(SRC) + test_runtime, False, [])]
        if kind == "editor":
            plan.append(("Assembly-CSharp-Editor", editor_of(CORE) + editor_of(SRC) + test_editor, True,
                         ["Assembly-CSharp"]))
    errors, warnings = [], []
    for assembly, sources, editor_only, deps in plan:
        refs, bcl_defines = editor.bcl("net4.8" if editor_only else "netstandard2.1")
        # Unity runs the generator next to XLua.asmdef: in XLua and the assemblies that
        # reference it, here XLua and Assembly-CSharp (in the Assets layout, Assembly-CSharp).
        analyze = source_gen and assembly in ("XLua", "Assembly-CSharp")
        generated = work / ("generated-" + assembly)
        generator_args = []
        if analyze:
            generated.mkdir(parents=True)
            generator_args = ["-analyzer:" + str(analyzer), "-generatedfilesout:" + str(generated)]
        ok, errors2, warnings2 = compile_assembly(
            compiler, assembly, work, sources,
            refs + editor.engine(editor_only or kind == "editor") + extra_refs + [work / (d + ".dll") for d in deps],
            base_defines + bcl_defines + (["UNITY_EDITOR_ONLY_COMPILATION"] if editor_only else []), langversion,
            generator_args)
        if any(" CS1617:" in e for e in errors2):  # unknown -langversion
            return Result("skip", note="the compiler does not know C# " + langversion)
        if ok and analyze:
            log = (work / (assembly + ".log")).read_text(encoding="utf-8")
            loaded = re.findall(r"warning (CS8032|CS8784|CS8785|CS9057)\b.*", log)
            errors2 += ["the source generator did not run: " + w for w in loaded]
            errors2 += re.findall(r"warning XLSG013\b.*", log)
            if assembly == "Assembly-CSharp":
                errors2 += check_generated(generated, presets.gcm(defines))
            ok = not errors2
        # The runtime: XLua, or Assembly-CSharp in the Assets layout.
        if ok and DISASSEMBLERS and assembly == plan[0 if layout == "assets" else 1][0]:
            errors2 += check_bridge_checks(work / (assembly + ".dll"), bridge_checks_expected(kind, defines))
            ok = not errors2
        errors += errors2
        warnings += warnings2
        if not ok:
            break
    return Result("fail" if errors else "ok", errors, warnings, time.monotonic() - start)


def find_compiler(kind, csc, editor):
    if kind in ("system", "auto"):
        path = csc or shutil.which("csc")
        if path:
            if path.endswith((".exe", ".dll")):
                return [shutil.which("mono") or "mono", path] if path.endswith(".exe") else ["dotnet", path]
            return [path]
        if kind == "system":
            raise EditorError("no csc on PATH; install Mono (brew install mono) or pass --csc or --compiler unity")
    found = editor.roslyn()
    if not found:
        raise EditorError("no bundled Roslyn (DotNetSdkRoslyn or DotNetSdk) in " + str(editor.scripting))
    return found


def discover():
    found = []
    for root in DISCOVER:
        if not root.is_dir():
            continue
        if (root / "Editor/Data").is_dir():
            found.append(root)
            continue
        for child in sorted(root.iterdir()):
            if VERSION_RE.match(child.name) and ((child / "Unity.app").is_dir() or (child / "Editor/Data").is_dir()):
                found.append(child)
    return found


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--unity", action="append", default=[], type=Path,
                   help="Unity editor: an install folder, Unity.app or a Linux folder with Editor/Data "
                        "(repeatable; default: every editor under " + ", ".join(str(d) for d in DISCOVER) + ")")
    p.add_argument("--unity-version", help="version of the single --unity editor, when its path does not tell")
    p.add_argument("--output", type=Path, help="folder for response files, logs and assemblies (required "
                                                 "unless --list)")
    p.add_argument("--compiler", choices=["auto", "system", "unity"], default="auto",
                   help="system: csc on PATH or --csc (Homebrew Mono's Roslyn); unity: the editor's own Roslyn "
                        "run with its bundled dotnet; auto (default): system if found, else unity")
    p.add_argument("--csc", help="C# compiler for --compiler system (a script, or an .exe run with mono)")
    p.add_argument("--langversion", default="9.0", help="C# language version (Unity uses 9.0)")
    p.add_argument("--config", action="append", default=[],
                   help="only configurations matching this glob, such as 'editor-*' (repeatable)")
    p.add_argument("--list", action="store_true", help="list the configurations and their defines, then exit")
    p.add_argument("--jobs", type=int, default=2, help="compilations in parallel (default 2)")
    p.add_argument("--warnings", action="store_true", help="print each distinct warning")
    a = p.parse_args()

    configs = [c for c in configurations() if not a.config or any(fnmatch.fnmatch(c[0], g) for g in a.config)]
    if a.list:
        for name, kind, defines, tests, layout in configs:
            print("%-44s %-6s %-6s %s%s%s" % (name, kind, layout, " ".join(defines),
                                               {False: "", True: " +tests"}.get(tests, " +tests (C# 12)"),
                                               " +source generator" if name in SOURCE_GEN_CONFIGS else ""))
        return 0
    if a.output is None:
        p.error("--output is required")
    if not configs:
        p.error("no configuration matches --config")
    paths = a.unity or discover()
    if not paths:
        p.error("no Unity editor found; pass --unity")
    if a.unity_version and len(paths) != 1:
        p.error("--unity-version needs exactly one --unity")

    out = a.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    print("tests leave out: " + ", ".join(TEST_SKIPPED) + " (prepare.py --roslyn-sdk)")
    table = {}
    editors = []
    failed = False
    analyzer = None
    for path in paths:
        try:
            editor = Editor(path, a.unity_version)
            compiler = find_compiler(a.compiler, a.csc, editor)
        except EditorError as e:
            print("FAIL  %s: %s" % (path, e))
            failed = True
            continue
        editors.append(editor.version)
        eout = out / editor.version
        print("== Unity %s (%s), compiler: %s" % (editor.version, editor.scripting, " ".join(compiler)), flush=True)
        test_refs = None
        if any(c[3] for c in configs):
            test_refs, note = build_test_framework(editor, compiler, eout, a.langversion)
            print("   tests: " + (note if test_refs else "skipped, " + note), flush=True)
        if analyzer is None and any(c[0] in SOURCE_GEN_CONFIGS for c in configs):
            sys.path.insert(0, str(REPO / "Tools"))
            import build_source_generator
            analyzer = build_source_generator.build(out / "source-gen-build")
        with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, a.jobs)) as pool:
            futures = [(c, pool.submit(run_config, editor, compiler, eout, c, test_refs, a.langversion, analyzer))
                       for c in configs]
            for config, future in futures:
                r = future.result()
                table[(config[0], editor.version)] = r
                if r.status == "fail":
                    failed = True
                print("%-5s %-12s %-44s %3d errors %4d warnings %6.1f s%s" % (
                    r.status.upper(), editor.version, config[0], len(r.errors), len(r.warnings), r.seconds,
                    "  (" + r.note + ")" if r.note else ""), flush=True)
                for e in r.errors[:20]:
                    print("        " + e)
                if a.warnings:
                    for w in sorted(set(r.warnings)):
                        print("        " + w)

    if editors:
        print()
        width = max(len(c[0]) for c in configs)
        print("%-*s  %s" % (width, "configuration", "  ".join("%-12s" % v for v in editors)))
        for config in configs:
            cells = []
            for v in editors:
                r = table[(config[0], v)]
                cells.append("%-12s" % (r.status if r.status != "ok" else "ok (%dw)" % len(r.warnings)))
            print("%-*s  %s" % (width, config[0], "  ".join(cells)))
        counts = {}
        for r in table.values():
            counts[r.status] = counts.get(r.status, 0) + 1
        print("\n%d editors x %d configurations: %s" % (
            len(editors), len(configs), ", ".join("%d %s" % (n, s) for s, n in sorted(counts.items()))))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
