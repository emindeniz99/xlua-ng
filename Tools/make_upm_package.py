#!/usr/bin/env python3
"""Assemble xLua as a Unity Package Manager (UPM) package; nothing is published.

    python3 Tools/make_upm_package.py --output DIR --plugins BUILD/plugin_lua55
    python3 Tools/make_upm_package.py --output DIR --plugin arm64/libxlua.dylib=PATH \\
        --source-gen GEN_BUILD_DIR --tarball OUT.tgz
    python3 Tools/make_upm_package.py --release --output DIR --plugins ANDROID_BUILD/plugin_lua55 \\
        --plugins IOS_BUILD/plugin_lua55 ... --source-gen GEN_BUILD_DIR --tarball OUT.tgz

Layout (docs/design/source-generator.md, "M6 as implemented"):

    package.json            name, version (default: the VERSION file), "unity": "2021.3", the
                            documentation, changelog and licence URLs, samples
    README.md               what the package holds and how to install it
    CHANGELOG.md            the repository's CHANGELOG.md
    LICENSE.md              the repository's LICENSE.TXT
    Third Party Notices.md  Lua, LuaSocket and lua-snapshot, which the plugins and build~ contain
    Runtime/                Assets/XLua/Src without Editor, with XLua.asmdef
    Runtime/Analyzers/      the source generator with its RoslynAnalyzer .meta (--source-gen)
    Editor/                 Assets/XLua/Src/Editor, with XLua.Editor.asmdef
    Core/                   Assets/XLua/Core with XLua.Core.asmdef, Core/Editor with XLua.Core.Editor.asmdef
                            (the plugin variant selector); compiled also with XLUA_CORE_ONLY (core-luascript.md 2.2.1)
    Plugins/                the native plugins given, with the .meta files of Assets/Plugins
    Plugins/WebGL/          xlua_webgl.cpp, its includes pointed at build~/ (the C sources it compiles)
    Documentation~/         Assets/XLua/Doc
    Samples~/Examples, Samples~/Tutorial   Assets/XLua/Examples (with their XLuaConfig.cs) and Tutorial

Without --source-gen the package has no code generation at all (the legacy
generator went in source generator milestone M11): everything is bound through
reflection, and [CSharpCallLua] delegates fail in IL2CPP players. Tests build
such packages on purpose; a release package needs --source-gen.

--release checks that the package is one that may be released: the source
generator, the samples, the WebGL sources and every plugin of RELEASE_PLUGINS
(the Lua 5.5.1 libraries of every platform and the variants the plugin
selector takes) must be in it, and CHANGELOG.md must have a section for the
version. publish.yml builds the release package with it
(docs/design/release.md).

The asmdefs are those of the Assets folder (Assets/XLua/Src/XLua.asmdef and
the others, the default layout since 2026-10-18; from Assets/XLua/Asmdef~ when
XLua > Project Layout > Use Assembly-CSharp moved them there), with the same GUIDs. Every file keeps the GUID of its .meta in
the repository, so a project that moves from the Assets layout to the package
keeps its references (UPM spike, finding 4); folders without a .meta in the
repository and the generated files get GUIDs derived from their package path.
A file without a .meta in the repository stops the build: in an immutable
package (registry, git or tarball install) Unity ignores such files.

Native plugins are not built here. --plugins takes a directory with the
layout the build/make_*.sh scripts write (XLUA_BUILD_ROOT/plugin_lua55, with
Plugins/ inside; its symbols/ folder is not part of the package); --plugin
REL=PATH adds one file as Plugins/REL. A plugin variant
(<path>/xlua-variants/<name>/<file>, such as the iOS Simulator library
iOS/xlua-variants/simulator/libxlua.a) gets the .meta of its default plugin
(Assets/Plugins/<path>/<file>.meta) with the Editor turned off and a GUID
derived from its package path: the import settings that XLua/Plugin
Variants/Copy Import Settings writes, which an immutable package cannot run.
The selector (Core/Editor/PluginVariants.cs) then picks it. A variant whose
default plugin has no .meta, or of the macOS bundle, is left out with a note.

--tarball writes the package as an npm-style .tgz (files under package/, the
form `file:` tarball installs and registries use), with fixed order, owner
and time, so the same input gives the same bytes.
"""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile

repo = Path(__file__).resolve().parents[1]
ASSETS = repo / "Assets"
DEFAULT_NAME = "com.xlua-ng.xlua"
# The one place the version is set: VERSION at the repository root (docs/design/release.md).
DEFAULT_VERSION = (repo / "VERSION").read_text(encoding="utf-8").strip()
UNITY_MINIMUM = "2021.3"
DISPLAY_NAME = "xLua-NG"
REPOSITORY = "https://github.com/emindeniz99/xlua-ng"
SITE = "https://emindeniz99.github.io/xlua-ng/"
# The plugins a release package must have (paths below Plugins/): the Lua 5.5.1
# libraries that publish.yml builds, and the variants the plugin selector takes
# (Core/Editor/PluginVariants.cs): minimal for XLUA_PRESET_MINIMAL, core for
# XLUA_CORE_ONLY, and the iOS Simulator build of each. Only Android and iOS have
# variant builds (build/make_android_*.sh, make_ios_lua5*.sh); on the other
# platforms a minimal project takes the default plugin, which runs the minimal C# too.
ANDROID_ABIS = ["arm64-v8a", "armeabi-v7a", "x86", "x86_64"]
RELEASE_PLUGINS = (
    ["Android/libs/%s/libxlua.so" % abi for abi in ANDROID_ABIS]
    + ["Android/xlua-variants/%s/libs/%s/libxlua.so" % (v, abi) for v in ("minimal", "core") for abi in ANDROID_ABIS]
    + ["iOS/libxlua.a"]
    + ["iOS/xlua-variants/%s/libxlua.a" % v
       for v in ("simulator", "minimal", "minimal_simulator", "core", "core_simulator")]
    + ["xlua.bundle/Contents/MacOS/xlua", "arm64/libxlua.dylib",
       "x86_64/xlua.dll", "x86/xlua.dll", "arm64/xlua.dll",
       "x86_64/libxlua.so"]
)
# The WebGL plugin source includes the C sources relative to itself (xlua_webgl.cpp).
WEBGL_INCLUDE = "../../../build/"
WEBGL_PACKAGE_INCLUDE = "../../build~/"

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
IMPORTER_META = """fileFormatVersion: 2
guid: {guid}
{importer}:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def guid_for(package_path):
    """A stable GUID for a package path that has no .meta in the repository."""
    return hashlib.md5(("xlua-ng upm package:" + package_path).encode()).hexdigest()


class Package:
    def __init__(self, root):
        self.root = root
        self.notes = []

    def rel(self, path):
        return path.relative_to(self.root).as_posix()

    def copy_file(self, src, dest, meta=None):
        """Copy src with its .meta from the repository (src.meta unless meta is given)."""
        meta = Path(str(src) + ".meta") if meta is None else meta
        if not meta.is_file():
            sys.exit("no .meta in the repository for %s (an immutable package would ignore it)" % src)
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        shutil.copy2(meta, str(dest) + ".meta")

    def copy_tree(self, src, dest, skip=()):
        """Copy a folder of the Assets tree: files with their .meta, folders with theirs."""
        dest.mkdir(parents=True, exist_ok=True)
        self.folder_meta(dest, Path(str(src) + ".meta"))
        for item in sorted(src.iterdir()):
            if item.name.endswith(".meta") or item.name in (".DS_Store",) or item.name in skip:
                continue
            if item.is_dir():
                self.copy_tree(item, dest / item.name)
            else:
                self.copy_file(item, dest / item.name)

    def folder_meta(self, dest, repo_meta=None):
        """The .meta of a folder: the repository's if there is one, else a derived GUID."""
        if self.inside_tilde(dest) or dest == self.root:
            if repo_meta is not None and repo_meta.is_file() and dest != self.root:
                shutil.copy2(repo_meta, str(dest) + ".meta")
            return
        meta = Path(str(dest) + ".meta")
        if repo_meta is not None and repo_meta.is_file():
            shutil.copy2(repo_meta, meta)
        elif not meta.exists():
            meta.write_text(FOLDER_META.format(guid=guid_for(self.rel(dest))))

    def inside_tilde(self, path):
        """Unity skips folders whose name ends in ~ (no .meta files needed inside)."""
        return any(part.endswith("~") for part in path.relative_to(self.root).parts)

    def write(self, rel, text, importer="TextScriptImporter"):
        dest = self.root / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(text)
        if not self.inside_tilde(dest):
            Path(str(dest) + ".meta").write_text(IMPORTER_META.format(guid=guid_for(rel), importer=importer))

    def ensure_folder_metas(self):
        """Folders still without a .meta get derived GUIDs. Plugins/, Plugins/Android and the like
        do not take the GUIDs of Assets/Plugins: a project that moves to the package keeps those
        folders for its other plugins, and two folders with one GUID conflict. The macOS bundle is
        a plugin, not a shared folder: it keeps its .meta files and their import settings."""
        for folder in sorted(p for p in self.root.rglob("*") if p.is_dir()):
            if self.inside_tilde(folder) or Path(str(folder) + ".meta").exists():
                continue
            parts = folder.relative_to(self.root).parts
            bundle = parts[0] == "Plugins" and len(parts) > 1 and parts[1] == "xlua.bundle"
            self.folder_meta(folder, ASSETS.joinpath(*parts[:-1], parts[-1] + ".meta") if bundle else None)


def variant_meta(default_meta, package_path):
    """The .meta of a plugin variant: its default plugin's, with a GUID derived from the variant's
    package path and the Editor off (the Editor loads the default plugin; two enabled libraries of
    one name conflict there). Returns None when the default plugin's settings cannot be adapted."""
    text = default_meta.read_text()
    text, guids = re.subn(r"^guid: [0-9a-f]{32}$", "guid: " + guid_for(package_path), text, flags=re.M)
    # serializedVersion 2 lists platformData entries ("- first: / Editor: Editor / second: / enabled:"),
    # serializedVersion 1 maps them ("Editor: / enabled:").
    editor_v2 = r"(- first:\n\s+Editor: Editor\n\s+second:\n\s+enabled: )\d"
    editor_v1 = r"(\n\s+Editor:\n\s+enabled: )\d"
    text, n2 = re.subn(editor_v2, r"\g<1>0", text)
    text, n1 = re.subn(editor_v1, r"\g<1>0", text)
    any_on = re.search(r"- first:\n\s+Any: *\n\s+second:\n\s+enabled: 1|\n\s+Any:\n\s+enabled: 1", text)
    if guids != 1 or n1 + n2 != 1 or any_on:
        return None
    return text


def add_plugins(pkg, plugin_dirs, plugin_files):
    """Native plugins under Plugins/, each with the .meta of the same path in Assets/Plugins."""
    found = {}
    for d in plugin_dirs:
        d = d.resolve()
        base = d / "Plugins" if (d / "Plugins").is_dir() else d
        if not base.is_dir():
            sys.exit("no plugin directory: " + str(d))
        for f in sorted(base.rglob("*")):
            if f.is_file() and f.name != ".DS_Store":
                found[f.relative_to(base).as_posix()] = f
    for rel, path in plugin_files:
        if not path.is_file():
            sys.exit("missing plugin: " + str(path))
        found[rel] = path
    plugins = ASSETS / "Plugins"
    added = []
    for rel, path in sorted(found.items()):
        if "/xlua-variants/" in "/" + rel:
            parts = rel.split("/")
            i = parts.index("xlua-variants")
            default_rel = "/".join(parts[:i] + parts[i + 2:])
            default_meta = plugins / (default_rel + ".meta")
            meta = None
            if i + 2 < len(parts) and "xlua.bundle" not in parts and default_meta.is_file():
                meta = variant_meta(default_meta, "Plugins/" + rel)
            if meta is None:
                pkg.notes.append("left out the plugin variant %s (no default plugin .meta in Assets/Plugins "
                                 "to take its import settings from)" % rel)
                continue
            dest = pkg.root / "Plugins" / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, dest)
            Path(str(dest) + ".meta").write_text(meta)
            added.append(rel)
            continue
        # A macOS bundle is a folder plugin; its parts have their .meta files in the repository.
        meta = plugins / (rel + ".meta")
        if not meta.is_file():
            sys.exit("no Assets/Plugins/%s.meta for the plugin %s" % (rel, path))
        pkg.copy_file(path, pkg.root / "Plugins" / rel, meta)
        added.append(rel)
    # A macOS bundle is a folder plugin: its folder .meta files (xlua.bundle.meta holds the
    # import settings) come from the repository in ensure_folder_metas, Info.plist from here.
    bundle = pkg.root / "Plugins/xlua.bundle"
    if any(rel.startswith("xlua.bundle/") for rel in added) and not (bundle / "Contents/Info.plist").exists():
        pkg.copy_file(plugins / "xlua.bundle/Contents/Info.plist", bundle / "Contents/Info.plist")
    if any(rel.startswith("iOS/") for rel in added):
        # The hotfix flags that the iOS player links (Assets/Plugins/iOS/HotfixFlags.cpp).
        pkg.copy_file(plugins / "iOS/HotfixFlags.cpp", pkg.root / "Plugins/iOS/HotfixFlags.cpp")
    return added


def add_webgl(pkg):
    """xlua_webgl.cpp compiles the Lua and bridge C sources it includes; they go to build~/."""
    src = ASSETS / "Plugins/WebGL/xlua_webgl.cpp"
    text = src.read_text(encoding="utf-8")
    included = sorted(set(re.findall(r'#include "' + re.escape(WEBGL_INCLUDE) + r'([^"]+)"', text)))
    if not included:
        sys.exit("xlua_webgl.cpp no longer includes " + WEBGL_INCLUDE + "; update make_upm_package.py")
    dest = pkg.root / "Plugins/WebGL/xlua_webgl.cpp"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(text.replace('#include "' + WEBGL_INCLUDE, '#include "' + WEBGL_PACKAGE_INCLUDE), encoding="utf-8")
    shutil.copy2(str(src) + ".meta", str(dest) + ".meta")
    sources = pkg.root / "build~"
    folders = set()
    for rel in included:
        folders.add(Path(rel).parent)
        target = sources / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(repo / "build" / rel, target)
    # The headers next to the included files, which those files include in turn.
    for folder in folders:
        for header in (repo / "build" / folder).glob("*.h"):
            target = sources / folder / header.name
            if not target.exists():
                shutil.copy2(header, target)
    return len(included)


def add_source_generator(pkg, build_dir):
    subprocess.run([sys.executable, str(repo / "Tools/build_source_generator.py"), "--output",
                    str(build_dir.resolve()), "--install", str(pkg.root / "Runtime/Analyzers")], check=True)


def asmdef_source(rel):
    """Assets/XLua/<rel>, or Assets/XLua/Asmdef~/<rel> in a tree switched to Assembly-CSharp."""
    placed, parked = ASSETS / "XLua" / rel, ASSETS / "XLua/Asmdef~" / rel
    return placed if placed.is_file() or not parked.is_file() else parked


def adapt_asmdef(src, dest, core=False):
    """An asmdef of the Assets folder with its GUID; checked to parse. The bridge's
    asmdefs have the !XLUA_CORE_ONLY define constraint, XLua.Core's have none."""
    data = json.loads(src.read_text(encoding="utf-8-sig"))
    if core and data.get("defineConstraints"):
        sys.exit("%s: XLua.Core compiles in every project; it has no define constraint (core-luascript.md 2.2.1)" % src)
    if not core and "!XLUA_CORE_ONLY" not in data.get("defineConstraints", []):
        sys.exit("%s lacks the !XLUA_CORE_ONLY define constraint (core-luascript.md 2.2.1)" % src)
    shutil.copy2(src, dest)
    shutil.copy2(str(src) + ".meta", str(dest) + ".meta")


def package_json(a, samples):
    return json.dumps({
        "name": a.name,
        "version": a.version,
        "displayName": DISPLAY_NAME,
        "description": "Lua 5.5.1 scripting for Unity, with calls in both directions between Lua and C#: a fork "
                       "of Tencent/xLua with source-generated bindings, hotfix, presets, a sandbox for untrusted "
                       "scripts (LuaScript) and LuaCATS definitions. Native plugins for Android, iOS, macOS, "
                       "Windows and Linux, and the WebGL sources, are included.",
        "unity": UNITY_MINIMUM,
        "author": {"name": "xLua-NG contributors", "url": REPOSITORY},
        "license": "MIT",
        "documentationUrl": SITE,
        "changelogUrl": SITE + "changes/",
        "licensesUrl": REPOSITORY + "/blob/main/LICENSE.TXT",
        "keywords": ["lua", "scripting", "hotfix", "modding", "sandbox", "xlua"],
        "samples": samples,
    }, indent=2) + "\n"


def changelog(version, commit, release):
    """The repository's CHANGELOG.md, with the commit the package was built from. A release
    package needs a section for its version ("## 0.1.0 (2026-11-02)")."""
    text = (repo / "CHANGELOG.md").read_text(encoding="utf-8")
    heading = re.compile(r"^## " + re.escape(version) + r"(?:\s|$)", re.M)
    if not heading.search(text):
        if release:
            sys.exit("CHANGELOG.md has no section '## %s'; write it before a release (docs/design/release.md)"
                     % version)
        text += "\n## %s\n\nNo section for this version in CHANGELOG.md.\n" % version
    return text.rstrip("\n") + "\n\nThis package was built from xlua-ng commit %s.\n" % commit


def third_party_notices():
    """The notices of the third-party code that the native plugins and build~ contain."""
    lua_h = (repo / "build/lua-5.5.1/src/lua.h").read_text(encoding="utf-8")
    m = re.search(r"/\*{10,}\n(\* Copyright \(C\) [^\n]*Lua\.org.*?)\*{10,}/", lua_h, re.S)
    if not m:
        sys.exit("no licence block at the end of build/lua-5.5.1/src/lua.h; update make_upm_package.py")
    lua = "\n".join(re.sub(r"^\* ?", "", line) for line in m.group(1).splitlines())
    socket = (repo / "build/luasocket/LICENSE").read_text(encoding="utf-8").strip()
    checker = (repo / "build/memory_leak_checker.c").read_text(encoding="utf-8")
    m = re.search(r"^ \*   (Copyright \(c\) 2012 codingow\.com.*?DEALINGS IN THE\s+(?:\*\s+)?SOFTWARE\.)", checker, re.M | re.S)
    if not m:
        sys.exit("no lua-snapshot notice in build/memory_leak_checker.c; update make_upm_package.py")
    snapshot = "\n".join(re.sub(r"^ \*(   )?", "", line) for line in m.group(1).splitlines())
    parts = [
        "# Third Party Notices",
        "",
        "This package contains third-party software under the licences below. The native plugins in "
        "`Plugins/` link it, and `build~/` holds the C sources that the WebGL plugin compiles. xLua itself "
        "is under the MIT License in `LICENSE.md` (Copyright (C) 2016 THL A29 Limited, a Tencent company).",
        "",
        "## Lua 5.5.1",
        "",
        "Component: the Lua interpreter and standard libraries, in every native plugin and in "
        "`build~/lua-5.5.1`. License type: MIT. Source: https://www.lua.org/",
        "",
        "```text", lua.strip(), "```",
        "",
        "## LuaSocket 3.1.0",
        "",
        "Component: the `socket` and `mime` modules, in the native plugins of the legacy preset (not in "
        "the minimal and core variants, and not in WebGL). License type: MIT. "
        "Source: https://github.com/lunarmodules/luasocket",
        "",
        "```text", socket, "```",
        "",
        "## lua-snapshot",
        "",
        "Component: the memory snapshot walk in `memory_leak_checker.c` (native plugins and `build~/`), "
        "ported from its `snapshot.c`. License type: MIT. Source: https://github.com/cloudwu/lua-snapshot",
        "",
        "```text", snapshot.strip(), "```",
        "",
    ]
    return "\n".join(parts)


README = """# {display} ({name} {version})

Lua scripting for Unity {unity} and later. Documentation: {site}

xLua-NG for Unity as a package: `Core` compiles into the `XLua.Core`
assembly (the Lua C API imports, `LuaOptions`, the exceptions and, with the
scripting define `XLUA_LUASCRIPT` or `XLUA_CORE_ONLY`, `LuaScript`) and
`Core/Editor` into `XLua.Core.Editor` (the plugin variant selector);
`Runtime` compiles into the `XLua` assembly and `Editor` into `XLua.Editor`;
these two are left out when the project defines `XLUA_CORE_ONLY`. Bindings come from the xLua source generator,
`Runtime/Analyzers/XLua.SourceGenerator.dll`, which runs while Unity compiles the assemblies that reference `XLua`;
without it every type is bound through reflection.

Install it with Window > Package Manager > "+" > "Add package from tarball"
(or "from disk" for the folder), with "Add package from git URL" and
`{repository}.git#upm` once the `upm` branch is published, or with an entry in
`Packages/manifest.json`. Remove an `Assets/XLua` and the xLua plugins in
`Assets/Plugins` first: the package keeps their GUIDs, and two copies
conflict.

The native plugins are Lua 5.5.1 builds. The guides are in `Documentation~`;
the examples and tutorials are samples you import from the Package Manager
window. Third-party licences are in `Third Party Notices.md`.
"""


def tarball(root, out):
    """npm-style .tgz: every entry under package/, sorted, owner 0, fixed mtime, no gzip name or time."""
    out.parent.mkdir(parents=True, exist_ok=True)
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.PAX_FORMAT) as tar:
        for path in [root] + sorted(root.rglob("*")):
            info = tar.gettarinfo(str(path), arcname="package/" + path.relative_to(root).as_posix() if path != root else "package")
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            info.mtime = 0
            info.mode = 0o755 if path.is_dir() else 0o644
            if path.is_file():
                with open(path, "rb") as f:
                    tar.addfile(info, f)
            else:
                tar.addfile(info)
    with open(out, "wb") as f:
        with gzip.GzipFile(filename="", mode="wb", fileobj=f, mtime=0, compresslevel=9) as gz:
            gz.write(raw.getvalue())


def lint(root):
    """Every asset outside ~ folders has a .meta, every .meta an asset, and GUIDs are unique."""
    problems, guids = [], {}
    for path in sorted(root.rglob("*")):
        rel = path.relative_to(root)
        if any(part.endswith("~") for part in rel.parts) or path.name == ".DS_Store":
            continue
        if path.name.endswith(".meta"):
            asset = Path(str(path)[:-5])
            if not asset.exists():
                problems.append("orphan .meta: " + rel.as_posix())
            m = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(errors="replace"), re.M)
            if not m:
                problems.append("no guid in " + rel.as_posix())
            elif m.group(1) in guids:
                problems.append("duplicate guid %s: %s and %s" % (m.group(1), guids[m.group(1)], rel.as_posix()))
            else:
                guids[m.group(1)] = rel.as_posix()
        elif not Path(str(path) + ".meta").exists():
            problems.append("no .meta: " + rel.as_posix())
    return problems, len(guids)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--output", required=True, type=Path, help="empty folder for the package")
    p.add_argument("--name", default=DEFAULT_NAME, help="package name (default %(default)s)")
    p.add_argument("--version", default=DEFAULT_VERSION, help="package version (default %(default)s)")
    p.add_argument("--plugins", type=Path, action="append", default=[], metavar="DIR",
                   help="a plugin build (XLUA_BUILD_ROOT/plugin_lua55, with Plugins/ inside); repeatable")
    p.add_argument("--plugin", action="append", default=[], metavar="REL=PATH",
                   help="one plugin file as Plugins/REL, such as arm64/libxlua.dylib=PATH; repeatable")
    p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
                   help="build the source generator into BUILD_DIR (Tools/build_source_generator.py) "
                        "and put it in Runtime/Analyzers")
    p.add_argument("--no-samples", action="store_true", help="leave out Samples~ (Examples and Tutorial)")
    p.add_argument("--no-webgl", action="store_true", help="leave out the WebGL plugin source and build~")
    p.add_argument("--tarball", type=Path, metavar="FILE.tgz", help="also write the package as a .tgz")
    p.add_argument("--release", action="store_true",
                   help="fail unless the package may be released: --source-gen, samples, WebGL, every plugin of "
                        "RELEASE_PLUGINS and a CHANGELOG.md section for the version")
    a = p.parse_args()
    if a.release and (not a.source_gen or a.no_samples or a.no_webgl):
        p.error("--release needs --source-gen, and takes no --no-samples or --no-webgl")
    if not re.match(r"^[a-z0-9][a-z0-9._-]*$", a.name):
        p.error("--name must be lower case: letters, digits, dots, hyphens and underscores")
    if not re.match(r"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", a.version):
        p.error("--version must be semantic, such as 1.2.3 or 1.2.3-preview.1")
    plugin_files = []
    for item in a.plugin:
        rel, sep, path = item.partition("=")
        if not sep or not rel or rel.startswith("/") or ".." in rel.split("/"):
            p.error("--plugin takes REL=PATH, REL relative to Plugins/: " + item)
        plugin_files.append((rel, Path(path)))
    root = a.output.resolve()
    if root.exists() and any(root.iterdir()):
        p.error("--output must be empty")
    root.mkdir(parents=True, exist_ok=True)
    pkg = Package(root)

    src = ASSETS / "XLua/Src"
    pkg.copy_tree(src, root / "Runtime", skip=("Editor", "Analyzers"))
    adapt_asmdef(asmdef_source("Src/XLua.asmdef"), root / "Runtime/XLua.asmdef")
    pkg.copy_tree(src / "Editor", root / "Editor")
    adapt_asmdef(asmdef_source("Src/Editor/XLua.Editor.asmdef"), root / "Editor/XLua.Editor.asmdef")
    core = ASSETS / "XLua/Core"
    pkg.copy_tree(core, root / "Core")
    adapt_asmdef(asmdef_source("Core/XLua.Core.asmdef"), root / "Core/XLua.Core.asmdef", core=True)
    adapt_asmdef(asmdef_source("Core/Editor/XLua.Core.Editor.asmdef"), root / "Core/Editor/XLua.Core.Editor.asmdef",
                 core=True)
    if a.source_gen:
        add_source_generator(pkg, a.source_gen)
    added = add_plugins(pkg, a.plugins, plugin_files)
    if a.release:
        missing = [rel for rel in RELEASE_PLUGINS if rel not in added]
        if missing:
            sys.exit("--release: plugins missing from the package:\n  " + "\n  ".join(missing))
    webgl = 0 if a.no_webgl else add_webgl(pkg)

    shutil.copytree(ASSETS / "XLua/Doc", root / "Documentation~", ignore=shutil.ignore_patterns("*.meta"))
    shutil.copy2(ASSETS / "XLua/CHANGELOG.txt", root / "Documentation~/CHANGELOG-upstream.txt")
    samples = []
    if not a.no_samples:
        # Samples keep their .meta files: imported into Assets, scenes find their scripts by GUID.
        pkg.copy_tree(ASSETS / "XLua/Examples", root / "Samples~/Examples")
        pkg.copy_tree(ASSETS / "XLua/Tutorial", root / "Samples~/Tutorial")
        samples = [{"displayName": "Examples", "description": "The xLua examples, with their configuration "
                    "(XLuaConfig.cs) for the xLua source generator.", "path": "Samples~/Examples"},
                   {"displayName": "Tutorial", "description": "The xLua tutorial scenes.", "path": "Samples~/Tutorial"}]

    commit = subprocess.run(["git", "-C", str(repo), "rev-parse", "--short", "HEAD"], stdout=subprocess.PIPE,
                            text=True).stdout.strip() or "unknown"
    pkg.write("package.json", package_json(a, samples), importer="PackageManifestImporter")
    pkg.write("README.md", README.format(display=DISPLAY_NAME, name=a.name, version=a.version, unity=UNITY_MINIMUM,
                                         site=SITE, repository=REPOSITORY))
    pkg.write("CHANGELOG.md", changelog(a.version, commit, a.release))
    pkg.write("LICENSE.md", (repo / "LICENSE.TXT").read_text(encoding="utf-8", errors="replace"))
    pkg.write("Third Party Notices.md", third_party_notices())
    pkg.ensure_folder_metas()
    problems, count = lint(root)
    if problems:
        sys.exit("package check failed:\n  " + "\n  ".join(problems))
    if a.tarball:
        tarball(root, a.tarball.resolve())
    for note in pkg.notes:
        print("note: " + note)
    print("XLUA_UPM_PACKAGE: %s %s at %s (%d assets with .meta, plugins: %s, webgl sources: %d, source generator: %s%s)" % (
        a.name, a.version, root, count, ", ".join(added) or "none", webgl, "yes" if a.source_gen else "no",
        ", tarball " + str(a.tarball.resolve()) if a.tarball else ""))


if __name__ == "__main__":
    main()
