#!/usr/bin/env python3
"""Check the Unity .meta files of the repository and of the UPM package.

    python3 Tools/check_meta.py [--no-package] [--work DIR]

The asset folders are Assets/ (a Unity project's Assets folder) and
Test/UnitTest and Test/PrefTest, whose folders are copied into a project's
Assets folder (Test/README.md); Unity gives those top-level folders their
.meta there, so they need none here. In them:

1. Every file and folder has a .meta, except what Unity does not import:
   names that start with "." or end with "~", "cvs" and "*.tmp", and what is
   inside such folders.
2. Every .meta has its file or folder. The native plugins are the exception:
   they are built from build/ and not committed (.gitignore), so the .meta of
   a library under Assets/Plugins stands alone.
3. Every .meta has "fileFormatVersion: 2" and one well-formed GUID (32 lower
   case hex digits, not all zero), and no two .meta files share a GUID.
4. Every plugin under Assets/Plugins has the import settings that PLUGINS
   below lists for its path: the player platforms it is enabled for, with
   their CPU, and the Editor's CPU and OS (or the Editor off: the 32-bit x86
   desktop plugins, since the Editor is 64-bit only). An enabled Editor must
   name a CPU of EDITOR_CPUS. The PluginImporter must be serializedVersion
   2, the one every supported editor reads: 6000.6 warns about version 1
   and writes version 3, which 2021.3 does not read. No other
   platform may be enabled, "Any" never; a platform Unity 2021.3 no longer
   has (OSXIntel, LinuxUniversal ...) may stay enabled only when it is of the
   plugin's own OS. A plugin that PLUGINS does not list, and a plugin of
   Tools/make_upm_package.py's RELEASE_PLUGINS without a .meta (the Android
   x86_64 library until 2026-10-19), is an error.
5. Unless --no-package: Tools/make_upm_package.py assembles the package with
   a placeholder for every plugin of RELEASE_PLUGINS and of PLUGINS (its own
   check covers every file and folder of the package), every plugin of
   RELEASE_PLUGINS must be in it, and every plugin .meta of the package,
   the variants' included, must pass check 4 (a variant with the settings of
   its default plugin and the Editor off).
6. A plugin source that defines functions the C# code imports from
   "__Internal" (INTERNAL_SOURCES) is enabled in PLUGINS for exactly the
   players whose UNITY_* symbols guard those imports: HotfixFlags.cpp for
   the `#if (UNITY_IPHONE || UNITY_TVOS)` of DelegateBridge.cs. A player
   that imports a function its build does not compile fails to link (tvOS
   until 2026-10-29). The source must define each function.

The file list comes from git (tracked files and untracked ones that are not
ignored), so build output and local Unity files do not count; outside a git
checkout the folders are walked. Exit status 1 on any finding.
"""
import argparse
from pathlib import Path
import re
import subprocess
import sys
import tempfile

repo = Path(__file__).resolve().parents[1]
# (folder, whether its top-level entries need a .meta in the repository)
ASSET_ROOTS = [("Assets", True), ("Test/UnitTest", False), ("Test/PrefTest", False)]
NATIVE_SUFFIXES = (".so", ".a", ".dll", ".dylib")
GUID = re.compile(r"^[0-9a-f]{32}$")

# The import settings of every plugin, by path below Plugins/: the player platforms that must be enabled with
# their CPU (None: no CPU setting), and the Editor's (CPU, OS), or None for the Editor off.
# CPU and OS names compare exactly, in the spelling Unity writes (owner decision 2026-10-28), which depends on
# the platform: the Android importer's AndroidPluginCPUArchitecture (2021.3) and AndroidArchitecture (6000.x)
# say ARMv7, ARM64, X86 and X86_64; the desktop importer's DesktopPluginCPUArchitecture says None, AnyCPU,
# x86, x86_64 and ARM64, and the Editor's EditorPluginCPUArchitecture AnyCPU, x86_64 and ARM64 (both the same
# in 2021.3 and 6000.6; Test/README.md "Unity .meta files"). The WSA spellings (ARM, ARM64, x64, x86) are
# upstream's: no Universal Windows Platform module was installed to check them.
# The macOS standalone takes AnyCPU for the arm64 dylib on purpose: with ARM64, Unity 2021.3 copies it where
# its Mono player does not look (docs/MIGRATING.md, fix of 2026-10-18).
ANDROID_CPU = {"arm64-v8a": "ARM64", "armeabi-v7a": "ARMv7", "x86": "X86", "x86_64": "X86_64"}
PLUGINS = {
    **{"Android/libs/%s/libxlua.so" % abi: ({"Android": cpu}, None) for abi, cpu in ANDROID_CPU.items()},
    "iOS/libxlua.a": ({"iOS": None}, None),
    # Compiled into every player that imports its functions from "__Internal" (check 6).
    "iOS/HotfixFlags.cpp": ({"iOS": None, "tvOS": None}, None),
    "WebGL/xlua_webgl.cpp": ({"WebGL": None}, None),
    **{"WSA/%s/xlua.dll" % d: ({"WindowsStoreApps": d}, None) for d in ("ARM", "ARM64", "x64", "x86")},
    # 32-bit players only: the Unity Editor is 64-bit, and its EditorPluginCPUArchitecture has no x86 (owner
    # decision Q40, 2026-10-28).
    "x86/xlua.dll": ({"Win": "AnyCPU"}, None),
    "x86/libxlua.so": ({"Linux": "x86"}, None),
    "x86_64/xlua.dll": ({"Win64": "x86_64"}, ("x86_64", "Windows")),
    "x86_64/libxlua.so": ({"Linux64": "x86_64"}, ("x86_64", "Linux")),
    "arm64/xlua.dll": ({"Win64": "ARM64"}, ("ARM64", "Windows")),
    "arm64/libxlua.dylib": ({"OSXUniversal": "AnyCPU"}, ("ARM64", "OSX")),
    "xlua.bundle": ({"OSXUniversal": "AnyCPU"}, ("AnyCPU", "OSX")),
}
# The CPU names of the Editor's EditorPluginCPUArchitecture (2021.3 to 6000.6); an enabled Editor with another
# one, such as x86, names a CPU no Editor has.
EDITOR_CPUS = {"AnyCPU", "x86_64", "ARM64"}
# Platforms of the plugin's OS that may stay enabled besides the required ones: those Unity 2021.3 no longer
# has (32-bit macOS and Linux, the universal Linux player), which upstream's .meta files still list.
LEGACY = {"Linux": {"Linux", "LinuxUniversal"}, "Windows": set(), "OSX": {"OSXIntel", "OSXIntel64"}}
OS_OF = {"Linux": "Linux", "Linux64": "Linux", "LinuxUniversal": "Linux", "Win": "Windows", "Win64": "Windows",
         "OSXUniversal": "OSX", "OSXIntel": "OSX", "OSXIntel64": "OSX"}


def unity_hidden(name):
    """Unity does not import these (Manual: Special folder names, hidden assets)."""
    return name.startswith(".") or name.endswith("~") or name.lower() == "cvs" or name.endswith(".tmp")


def list_files(root):
    """Files below root (relative to the repository), from git when possible."""
    try:
        out = subprocess.run(["git", "-C", str(repo), "ls-files", "-z", "--cached", "--others", "--exclude-standard",
                              "--", root], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=True).stdout
        names = [n for n in out.decode().split("\0") if n]
        # A file deleted from the work tree but still in the index is not there for Unity either.
        return sorted({n for n in names if (repo / n).is_file()})
    except (OSError, subprocess.CalledProcessError):
        return sorted(p.relative_to(repo).as_posix() for p in (repo / root).rglob("*") if p.is_file())


def parse_yaml(text):
    """The subset of YAML that Unity writes into .meta files: mappings, block lists, {} and scalars."""
    lines = [l for l in text.splitlines() if l.strip() and not l.startswith(("%", "---"))]

    def indent(line):
        return len(line) - len(line.lstrip(" "))

    def block(i, level):
        if i < len(lines) and indent(lines[i]) == level and lines[i].lstrip().startswith("- "):
            items = []
            while i < len(lines) and indent(lines[i]) == level and lines[i].lstrip().startswith("- "):
                lines[i] = " " * (level + 2) + lines[i].lstrip()[2:]
                item, i = block(i, level + 2)
                items.append(item)
            return items, i
        mapping = {}
        while i < len(lines) and indent(lines[i]) == level and not lines[i].lstrip().startswith("- "):
            key, _, value = lines[i].strip().partition(":")
            value = value.strip()
            i += 1
            if value == "" and i < len(lines) and (indent(lines[i]) > level or (
                    indent(lines[i]) == level and lines[i].lstrip().startswith("- "))):
                value, i = block(i, indent(lines[i]))
            elif value == "{}":
                value = {}
            elif value == "":
                value = None
            # serializedVersion 2 of some importers repeats a "data:" key per entry (xlua_webgl.cpp.meta).
            if key in mapping:
                if not isinstance(mapping[key], list) or not getattr(mapping[key], "repeated", False):
                    mapping[key] = RepeatedList([mapping[key]])
                mapping[key].append(value)
            else:
                mapping[key] = value
        return mapping, i

    result, i = block(0, 0)
    if i != len(lines):
        raise ValueError("cannot read line %d: %r" % (i + 1, lines[i]))
    return result


class RepeatedList(list):
    repeated = True


def platforms(meta):
    """{platform: (enabled, settings)} of a PluginImporter, from serializedVersion 1 or 2."""
    data = (meta.get("PluginImporter") or {}).get("platformData") or {}
    entries = []
    if isinstance(data, list):
        entries = data
    elif isinstance(data, dict) and "data" in data:
        entries = data["data"] if isinstance(data["data"], list) else [data["data"]]
    result = {}
    if entries:
        for entry in entries:
            (group, name), = (entry.get("first") or {}).items()
            if not group and name == "Any":
                continue  # ": Any", the exclude settings of the Any platform
            # Other names without a group are platforms the editor no longer has (": LinuxUniversal").
            second = entry.get("second") or {}
            result[name or group] = (str(second.get("enabled")) == "1", second.get("settings") or {})
    else:
        for name, value in data.items():
            value = value or {}
            result[name] = (str(value.get("enabled")) == "1", value.get("settings") or {})
    return result


def check_plugin(meta, expected, problems, label):
    players, editor = expected
    version = str((meta.get("PluginImporter") or {}).get("serializedVersion"))
    if version != "2":
        problems.append("%s: PluginImporter serializedVersion %s, expected 2 (Unity 6000.6 warns below 2, and Unity 2021.3 "
                        "cannot read the version 3 that 6000.6 writes; see Test/README.md, \"Unity .meta files\")"
                        % (label, version))
    found = platforms(meta)
    if not found:
        problems.append("%s: no platform settings in its PluginImporter" % label)
        return
    oses = {OS_OF[p] for p in players if p in OS_OF}
    allowed_legacy = set().union(*(LEGACY[o] for o in oses)) if oses else set()
    for platform, (enabled, settings) in sorted(found.items()):
        if platform == "Editor":
            continue
        if platform in players:
            want = players[platform]
            if not enabled:
                problems.append("%s: not enabled for %s" % (label, platform))
            elif want is not None and str(settings.get("CPU")) != want:
                problems.append("%s: CPU %s for %s, expected %s" % (label, settings.get("CPU"), platform, want))
        elif enabled and platform in allowed_legacy:
            continue
        elif enabled:
            problems.append("%s: enabled for %s, which it is not built for" % (label, platform))
    for platform in players:
        if platform not in found:
            problems.append("%s: no settings for %s" % (label, platform))
    enabled, settings = found.get("Editor", (False, {}))
    if enabled and str(settings.get("CPU")) not in EDITOR_CPUS:
        problems.append("%s: enabled for the Editor with CPU %s, which no Unity Editor has (%s)"
                        % (label, settings.get("CPU"), ", ".join(sorted(EDITOR_CPUS))))
    if editor is None and enabled:
        problems.append("%s: enabled for the Editor, which must not load it" % label)
    elif editor is not None:
        cpu, os_name = editor
        if not enabled:
            problems.append("%s: not enabled for the Editor" % label)
        elif (str(settings.get("CPU")), str(settings.get("OS"))) != (cpu, os_name):
            problems.append("%s: Editor CPU %s and OS %s, expected %s and %s"
                            % (label, settings.get("CPU"), settings.get("OS"), cpu, os_name))


def check_repository(problems):
    guids = {}
    metas = 0
    for root, top_needs_meta in ASSET_ROOTS:
        files = list_files(root)
        on_disk = set(files)
        assets, meta_files = set(), set()
        for rel in files:
            parts = Path(rel).relative_to(root).parts
            if any(unity_hidden(p) for p in parts):
                if rel.endswith(".meta"):
                    meta_files.add(rel)
                continue
            if rel.endswith(".meta"):
                meta_files.add(rel)
            else:
                assets.add(rel)
            # Its folders, below the root (the root itself is Assets, or copied into it).
            for depth in range(1 if top_needs_meta else 2, len(parts)):
                assets.add(Path(root, *parts[:depth]).as_posix())
        for asset in sorted(assets):
            if asset + ".meta" not in on_disk:
                problems.append("no .meta: " + asset)
        for meta_rel in sorted(meta_files):
            metas += 1
            asset = meta_rel[:-5]
            text = (repo / meta_rel).read_text(encoding="utf-8", errors="replace")
            if asset not in assets and not any(unity_hidden(p) for p in Path(asset).parts):
                plugin_binary = (asset.startswith("Assets/Plugins/") and
                                 (asset.endswith(NATIVE_SUFFIXES) or "/xlua.bundle/Contents/MacOS/" in asset))
                if not plugin_binary:
                    problems.append("orphan .meta (no file or folder %s): %s" % (asset, meta_rel))
            if not re.search(r"^fileFormatVersion: 2$", text, re.M):
                problems.append("no 'fileFormatVersion: 2' in " + meta_rel)
            found = re.findall(r"^guid: *(\S*)\s*$", text, re.M)
            if len(found) != 1:
                problems.append("%d guid lines in %s" % (len(found), meta_rel))
                continue
            guid = found[0]
            if not GUID.match(guid) or guid == "0" * 32:
                problems.append("malformed guid %r in %s" % (guid, meta_rel))
            elif guid in guids:
                problems.append("duplicate guid %s: %s and %s" % (guid, guids[guid], meta_rel))
            else:
                guids[guid] = meta_rel
    return metas


def check_plugins(base, problems, label_prefix, variants=False):
    """Check 4 for every PluginImporter .meta below base (Assets/Plugins or a package's Plugins)."""
    count = 0
    for meta_path in sorted(base.rglob("*.meta")):
        rel = meta_path.relative_to(base).as_posix()[:-5]
        text = meta_path.read_text(encoding="utf-8", errors="replace")
        if "PluginImporter:" not in text:
            continue
        count += 1
        label = label_prefix + rel + ".meta"
        try:
            meta = parse_yaml(text)
        except ValueError as e:
            problems.append("%s: %s" % (label, e))
            continue
        default_rel = rel
        if "/xlua-variants/" in "/" + rel:
            if not variants:
                problems.append("%s: plugin variants are written by make_upm_package.py, not committed" % label)
                continue
            parts = rel.split("/")
            i = parts.index("xlua-variants")
            default_rel = "/".join(parts[:i] + parts[i + 2:])
        expected = PLUGINS.get(default_rel)
        if expected is None:
            problems.append("%s: no expected import settings for Plugins/%s in Tools/check_meta.py (PLUGINS)"
                            % (label, default_rel))
            continue
        if default_rel != rel:
            expected = (expected[0], None)  # a variant: the Editor loads the default plugin only
        check_plugin(meta, expected, problems, label)
    return count


# Check 6: plugin sources (below Plugins/) that define functions C# imports from "__Internal", with the C# file
# that imports them. The players that compile the source must be the players whose conditional compilation
# symbols guard the imports.
INTERNAL_SOURCES = {
    "iOS/HotfixFlags.cpp": ("Assets/XLua/Src/DelegateBridge.cs", ["xlua_get_hotfix_flag", "xlua_set_hotfix_flag"]),
}
# The PluginImporter platform of a player's UNITY_* symbol.
PLAYER_OF_SYMBOL = {"UNITY_IPHONE": "iOS", "UNITY_IOS": "iOS", "UNITY_TVOS": "tvOS", "UNITY_VISIONOS": "VisionOS",
                    "UNITY_ANDROID": "Android", "UNITY_WEBGL": "WebGL", "UNITY_SWITCH": "Switch",
                    "UNITY_WSA": "WindowsStoreApps"}


def internal_import_players(text, function):
    """The players (PLAYER_OF_SYMBOL) whose symbols the #if branches around the "__Internal" import of
    function name, or None if there is no such import."""
    stack = []  # (condition, in the #else part)
    lines = text.splitlines()
    for i, line in enumerate(lines):
        directive = line.strip()
        if directive.startswith("#if "):
            stack.append((directive[4:], False))
        elif directive.startswith("#elif ") and stack:
            stack[-1] = (directive[6:], False)
        elif directive.startswith("#else") and stack:
            stack[-1] = (stack[-1][0], True)
        elif directive.startswith("#endif") and stack:
            stack.pop()
        elif re.search(r"\bextern\b.*\b%s\s*\(" % re.escape(function), line):
            before = "\n".join(lines[max(0, i - 3):i + 1])
            if 'DllImport("__Internal"' not in before:
                continue
            players = set()
            for condition, in_else in stack:
                if not in_else:
                    players |= {PLAYER_OF_SYMBOL[s] for s in re.findall(r"(?<!!)\b(UNITY_[A-Z]+)\b", condition)
                                if s in PLAYER_OF_SYMBOL}
            return players
    return None


def check_internal_sources(problems):
    """Check 6."""
    for source, (cs_file, functions) in INTERNAL_SOURCES.items():
        cs_text = (repo / cs_file).read_text(encoding="utf-8")
        source_text = (repo / "Assets/Plugins" / source).read_text(encoding="utf-8")
        compiled = set(PLUGINS[source][0])
        for function in functions:
            players = internal_import_players(cs_text, function)
            if players is None:
                problems.append("%s: no DllImport(\"__Internal\") of %s; update INTERNAL_SOURCES in "
                                "Tools/check_meta.py" % (cs_file, function))
                continue
            if players != compiled:
                problems.append("Plugins/%s is compiled for %s, but %s imports %s from __Internal for %s"
                                % (source, ", ".join(sorted(compiled)), cs_file, function,
                                   ", ".join(sorted(players)) or "no player"))
            if not re.search(r"^\s*(?:extern \"C\" )?[\w ]+\b%s\s*\(" % re.escape(function), source_text, re.M):
                problems.append("Plugins/%s does not define %s" % (source, function))
    return len(INTERNAL_SOURCES)


def release_plugins():
    sys.path.insert(0, str(repo / "Tools"))
    import make_upm_package
    return make_upm_package.RELEASE_PLUGINS


def check_package(work, problems):
    """Check 5: the package that make_upm_package.py assembles from placeholder plugins."""
    plugins = work / "plugins" / "Plugins"
    wanted = sorted(set(release_plugins()) | {p for p in PLUGINS if p.endswith(NATIVE_SUFFIXES)}
                    | {"xlua.bundle/Contents/MacOS/xlua"})
    for rel in wanted:
        path = plugins / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"placeholder for Tools/check_meta.py\n")
    output = work / "package"
    result = subprocess.run([sys.executable, str(repo / "Tools/make_upm_package.py"), "--output", str(output),
                             "--plugins", str(work / "plugins")],
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if result.returncode != 0:
        problems.append("make_upm_package.py failed:\n    " + "\n    ".join(result.stdout.strip().splitlines()[-20:]))
        return 0
    for rel in release_plugins():
        if not (output / "Plugins" / rel).is_file() or not (output / "Plugins" / (rel + ".meta")).is_file():
            problems.append("package: the release plugin Plugins/%s is missing or has no .meta" % rel)
    return check_plugins(output / "Plugins", problems, "package Plugins/", variants=True)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--no-package", action="store_true", help="skip the package check (check 5)")
    p.add_argument("--work", type=Path, help="folder for the package check (default: a temporary folder)")
    a = p.parse_args()
    problems = []
    metas = check_repository(problems)
    plugin_count = check_plugins(repo / "Assets/Plugins", problems, "Assets/Plugins/")
    check_internal_sources(problems)
    for rel in release_plugins():
        if "/xlua-variants/" in "/" + rel:
            continue
        meta = repo / "Assets/Plugins" / (rel + ".meta")
        if not meta.is_file():
            problems.append("no .meta for the release plugin Assets/Plugins/%s (make_upm_package.py RELEASE_PLUGINS)"
                            % rel)
        if rel not in PLUGINS and not rel.startswith("xlua.bundle/"):
            problems.append("no expected import settings for the release plugin %s in Tools/check_meta.py" % rel)
    package_plugins = 0
    if not a.no_package:
        if a.work:
            a.work.mkdir(parents=True, exist_ok=True)
            package_plugins = check_package(a.work.resolve(), problems)
        else:
            with tempfile.TemporaryDirectory(prefix="check_meta-") as tmp:
                package_plugins = check_package(Path(tmp), problems)
    for problem in problems:
        print(problem)
    print("XLUA_META_%s: %d .meta files, %d plugin import settings in Assets/Plugins%s, %d finding(s)"
          % ("FAIL" if problems else "PASS", metas, plugin_count,
             "" if a.no_package else ", %d in the package" % package_plugins, len(problems)))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
