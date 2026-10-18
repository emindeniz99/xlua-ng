"""Check a player for code of the XLua (bridge) assembly (docs/design/core-luascript.md, section 6).

    python3 Test/MobileSmoke/core_only_player_check.py PLAYER [--project DIR] [--link-map FILE] [--report]

PLAYER is an Android APK or AAB, or an iOS .app. The check fails when

- ScriptingAssemblies.json in the player data lists XLua.dll;
- global-metadata.dat names a type or the image of the XLua assembly
  (ObjectTranslator, LuaEnv, StaticLuaCallbacks, XLua.dll);
- the xlua native library (libxlua.so in the package) has a bridge export
  (xlua_pushcsobj, luaopen_xlua, xlua_tocsobj_safe);
- with --project, the stripped assemblies IL2CPP compiled last
  (Library/Bee/artifacts/*/ManagedStripped) include XLua.dll;
- with --link-map (the iOS UnityFramework link map), a symbol of the XLua
  assembly's IL2CPP code (XLua_ObjectTranslator, LuaEnv__ctor) is linked.

It also requires LuaScript to be there (LuaScript in the metadata,
xlua_core_call in the library), so a player without either fails too.
--report prints the same findings for a player that is expected to have the
bridge (the core-5.5+nodefine and mixed-5.5 variants of size_report.py) and
exits 0. The last line is XLUA_CORE_PLAYER_PASS, _FAIL or _REPORT.
"""
import argparse
import json
from pathlib import Path
import sys
import zipfile

BRIDGE_TYPES = [b"ObjectTranslator", b"LuaEnv", b"StaticLuaCallbacks", b"XLua.dll"]
BRIDGE_EXPORTS = [b"xlua_pushcsobj", b"luaopen_xlua", b"xlua_tocsobj_safe"]
BRIDGE_LINKED = ["XLua_ObjectTranslator", "LuaEnv__ctor"]


def player_files(path):
    """(name, bytes) of ScriptingAssemblies.json, global-metadata.dat and every libxlua.so / libxlua.a found."""
    found = {}
    if path.is_file():
        with zipfile.ZipFile(path) as z:
            for n in z.namelist():
                base = n.rsplit("/", 1)[-1]
                if base in ("ScriptingAssemblies.json", "global-metadata.dat") or base == "libxlua.so":
                    found[n] = z.read(n)
    else:
        for f in path.rglob("*"):
            if f.is_file() and f.name in ("ScriptingAssemblies.json", "global-metadata.dat"):
                found[f.relative_to(path).as_posix()] = f.read_bytes()
    return found


def null_terminated(data, word):
    return (b"\0" + word + b"\0") in data


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("player", type=Path)
    p.add_argument("--project", type=Path, help="the Unity project that built the player (stripped assemblies)")
    p.add_argument("--link-map", type=Path, help="iOS: the UnityFramework link map")
    p.add_argument("--report", action="store_true", help="report the findings and exit 0")
    a = p.parse_args()
    files = player_files(a.player)
    problems, findings = [], {}

    assemblies = [v for k, v in files.items() if k.endswith("ScriptingAssemblies.json")]
    if not assemblies:
        problems.append("no ScriptingAssemblies.json in the player")
    else:
        names = json.loads(assemblies[0].decode("utf-8-sig"))["names"]
        findings["ScriptingAssemblies.json"] = [n for n in names if n.startswith("XLua")]
        if "XLua.dll" in names:
            problems.append("ScriptingAssemblies.json lists XLua.dll")
        if "XLua.Core.dll" not in names:
            problems.append("ScriptingAssemblies.json does not list XLua.Core.dll")

    metadata = [v for k, v in files.items() if k.endswith("global-metadata.dat")]
    if not metadata:
        problems.append("no global-metadata.dat in the player")
    else:
        present = [w.decode() for w in BRIDGE_TYPES if null_terminated(metadata[0], w)]
        findings["global-metadata.dat bridge names"] = present
        findings["global-metadata.dat bytes"] = len(metadata[0])
        if present:
            problems.append("global-metadata.dat names " + ", ".join(present))
        if not null_terminated(metadata[0], b"LuaScript"):
            problems.append("global-metadata.dat has no LuaScript")

    libraries = {k: v for k, v in files.items() if k.endswith("libxlua.so")}
    if a.player.is_file() and not libraries:
        problems.append("no libxlua.so in the package")
    for name, data in sorted(libraries.items()):
        exports = [w.decode() for w in BRIDGE_EXPORTS if null_terminated(data, w)]
        findings[name + " bridge exports"] = exports
        findings[name + " bytes"] = len(data)
        if exports:
            problems.append("%s exports %s" % (name, ", ".join(exports)))
        if not null_terminated(data, b"xlua_core_call"):
            problems.append(name + " has no xlua_core_call (no LuaScript sandbox)")

    if a.project:
        dlls = sorted(a.project.glob("Library/Bee/artifacts/*/ManagedStripped/*.dll"), key=lambda f: f.stat().st_mtime)
        if not dlls:
            problems.append("no stripped assemblies under " + str(a.project / "Library/Bee/artifacts"))
        else:
            folder = dlls[-1].parent
            stripped = {f.name: f.stat().st_size for f in folder.glob("XLua*.dll")}
            findings["ManagedStripped"] = stripped
            if "XLua.dll" in stripped:
                problems.append("the stripped assemblies include XLua.dll (%d bytes)" % stripped["XLua.dll"])

    if a.link_map:
        text = a.link_map.read_text(errors="replace")
        linked = [s for s in BRIDGE_LINKED if s in text]
        findings["link map bridge symbols"] = linked
        if linked:
            problems.append("the link map has " + ", ".join(linked))

    for key, value in findings.items():
        print("  %s: %s" % (key, value))
    for problem in problems:
        print("  - " + problem)
    if a.report:
        print("XLUA_CORE_PLAYER_REPORT: %s (%d finding(s) against a core-only player)" % (a.player, len(problems)))
        return
    if problems:
        sys.exit("XLUA_CORE_PLAYER_FAIL: " + str(a.player))
    print("XLUA_CORE_PLAYER_PASS: %s has LuaScript and no code of the XLua assembly" % a.player)


if __name__ == "__main__":
    main()
