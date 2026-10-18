"""Compile xLua in its asmdef layout against a Unity editor's DLLs, without starting Unity.

The asmdef layout is the default (since 2026-10-18): Assets/XLua/Core has
XLua.Core.asmdef, Assets/XLua/Core/Editor XLua.Core.Editor.asmdef,
Assets/XLua/Src XLua.asmdef and Assets/XLua/Src/Editor XLua.Editor.asmdef.
Unity compiles Assets/XLua/Core into the XLua.Core assembly,
Assets/XLua/Core/Editor into XLua.Core.Editor, Assets/XLua/Src into XLua
(which references XLua.Core) and Assets/XLua/Src/Editor into XLua.Editor.
Everything else in Assets/XLua goes into the predefined assemblies: scripts
into Assembly-CSharp and scripts under an Editor folder into
Assembly-CSharp-Editor, which reference every auto-referenced asmdef. This
script builds the same assemblies with the .NET SDK's csc (mise.toml) against
the reference assemblies and engine DLLs of the given Unity editor, in the
order Unity does:

  XLua.Core.dll             the Lua C API imports, LuaOptions and (with
                            XLUA_LUASCRIPT or XLUA_CORE_ONLY) LuaScript; also
                            as XLua.Core.Player.dll
  XLua.Core.Editor.dll      the plugin variant selector, -r XLua.Core.dll, UnityEditor
  XLua.dll                  the runtime, -r XLua.Core.dll, for the Editor (UNITY_EDITOR) and,
                            as XLua.Player.dll, for an IL2CPP player
  XLua.Editor.dll           -r XLua.dll, XLua.Core.dll, UnityEditor
  Assembly-CSharp.dll       examples and tutorials, -r XLua.dll, XLua.Core.dll
  Assembly-CSharp-Editor.dll their Editor folders, -r all of the above

--core-only is the editor-level core-only check (docs/design/core-luascript.md,
section 7.6): it defines XLUA_CORE_ONLY as a core-only project does in Player
Settings, leaves out every asmdef whose define constraints exclude it (it must
be XLua and XLua.Editor, and XLua.Core must stay), and compiles
Test/Core/UnitySample/LuaScriptSample.cs into Assembly-CSharp against
XLua.Core alone, so that nothing in XLua.Core needs the bridge. --luascript
compiles the same sample next to the bridge with XLUA_LUASCRIPT (both APIs in
one player, section 2.2.2).

--no-asmdef compiles the way back instead, the layout after XLua > Project
Layout > Use Assembly-CSharp (the four asmdefs moved to Assets/XLua/Asmdef~):
xLua, the examples and the tutorial in Assembly-CSharp, every Editor folder
in Assembly-CSharp-Editor, as upstream xLua compiles. With --source-gen the
generator then runs in all three, as Unity runs an analyzer that is installed
outside any asmdef folder (the only place it can be in that layout), and
Assembly-CSharp must get the pushers of xLua's GCOptimize defaults from its
own [assembly: GCOptimize] attributes.

A failure shows code that relied on xLua being compiled into Assembly-CSharp:
a use of an internal member across the new assembly boundary (CS0122), or a
reference from XLua to project code. It reads the asmdef files to check that
the layout is the one described above (names, references, platforms).
Without --source-gen nothing is generated. With --source-gen the xLua source
generator (Tools/build_source_generator.py)
runs in every one of them, as Unity runs it next to XLua.asmdef: the build
fails on a generator warning or error or an analyzer load warning, and
XLua.dll must contain the pushers of xLua's own GCOptimize defaults (the
UnityEngine structs of [assembly: GCOptimize] in GenAttributes.cs, one
attribute with several types), and Assembly-CSharp, which reads them from
XLua.dll's metadata, those that the examples use.

    python3 Test/Native/asmdef_compile_check.py --unity /path/Unity.app --output DIR [--hotfix] [--define NAME]
        [--source-gen] [--no-asmdef]
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, required=True,
               help="Unity.app (macOS) or the editor's folder that contains Contents or Data")
p.add_argument("--unity-version", default="2021.3",
               help="major.minor for the UNITY_X_Y_OR_NEWER defines (default 2021.3)")
p.add_argument("--output", type=Path, required=True)
p.add_argument("--hotfix", action="store_true", help="define HOTFIX_ENABLE, as a hotfix project does")
p.add_argument("--define", action="append", default=[], help="extra scripting define (repeatable)")
p.add_argument("--langversion", default="9", help="C# version Unity compiles with (9 in 2021.3 to 6000.x)")
p.add_argument("--dotnet", default=None, help="dotnet executable (default: `mise which dotnet`)")
p.add_argument("--source-gen", action="store_true", help="also run the xLua source generator in every assembly")
p.add_argument("--core-only", action="store_true",
               help="define XLUA_CORE_ONLY: XLua and XLua.Editor must be left out, and a LuaScript sample compiles against XLua.Core")
p.add_argument("--luascript", action="store_true", help="define XLUA_LUASCRIPT and compile the LuaScript sample next to the bridge")
p.add_argument("--no-asmdef", action="store_true",
               help="the layout without asmdefs (XLua > Project Layout > Use Assembly-CSharp): xLua compiles into "
                    "Assembly-CSharp and Assembly-CSharp-Editor with the examples")
a = p.parse_args()
if a.no_asmdef and a.core_only:
    p.error("--no-asmdef takes no --core-only: it needs the asmdefs")


def find_contents(unity):
    for c in [unity / "Contents", unity / "Data", unity]:
        if (c / "Managed").is_dir() or (c / "Resources/Scripting/Managed").is_dir():
            return c
    sys.exit("no Unity Managed folder under " + str(unity))


contents = find_contents(a.unity.resolve())
scripting = contents
if not (contents / "Managed/UnityEditor.dll").is_file():
    scripting = contents / "Resources/Scripting"  # Unity 6 on macOS
managed = scripting / "Managed"
# Assemblies for every platform use the Api Compatibility Level ".NET Standard
# 2.1", the default from 2021.2: references and defines as in a 2022.3
# project's Library/Bee/artifacts/*/Assembly-CSharp.rsp. Editor-only assemblies
# use Unity's .NET Framework 4.8 API, as Test/Native/editor_compile_check.py
# compiles Assembly-CSharp-Editor.
shims = scripting / "NetStandard/compat/2.1.0/shims"
api48 = scripting / "UnityReferenceAssemblies/unity-4.8-api"
extensions = sorted((scripting / "BCLExtensions/TargetingPacks/netstandard2.1/ref").glob("*.dll"))
profiles = {
    "player": (sorted((shims / "netfx").glob("*.dll")) + sorted((shims / "netstandard").glob("*.dll"))
               + [scripting / "NetStandard/ref/2.1.0/netstandard.dll"] + extensions,
               ["NET_STANDARD_2_0", "NET_STANDARD", "NET_STANDARD_2_1", "NETSTANDARD", "NETSTANDARD2_1"]),
    "editor": (sorted(api48.glob("*.dll")) + sorted((api48 / "Facades").glob("*.dll")) + extensions,
               ["NET_4_6", "NET_UNITY_4_8"]),
}
engine = [managed / "UnityEngine.dll"] + sorted((managed / "UnityEngine").glob("UnityEngine*.dll"))
editor = [managed / "UnityEditor.dll"]
missing = [str(f) for f in [scripting / "NetStandard/ref/2.1.0/netstandard.dll", api48 / "mscorlib.dll"]
           + engine + editor if not f.is_file()]
if missing:
    sys.exit("missing Unity reference assemblies: " + ", ".join(missing))
# The UnityEngine module DLLs include UnityEditor modules in Unity 6; they are
# engine references there as well.

major, minor = (int(x) for x in a.unity_version.split(".")[:2])
releases = [(5, m) for m in range(3, 7)] + [(y, m) for y in range(2017, 2021) for m in range(1, 5)] \
    + [(2020, m) for m in range(1, 4)] + [(2021, m) for m in range(1, 4)] + [(2022, m) for m in range(1, 4)] \
    + [(2023, m) for m in range(1, 3)] + [(6000, m) for m in range(0, 10)]
version_defines = ["UNITY_%d_%d_OR_NEWER" % r for r in releases if r <= (major, minor)] + ["UNITY_%d" % major,
                   "UNITY_%d_%d" % (major, minor)]
common = version_defines + ["UNITY_64", "UNITY_ASSERTIONS", "ENABLE_MONO", "CSHARP_7_3_OR_NEWER", "UNITY_STANDALONE_OSX",
                            "UNITY_STANDALONE"] + (["HOTFIX_ENABLE"] if a.hotfix else []) + a.define \
    + (["XLUA_CORE_ONLY"] if a.core_only else []) + (["XLUA_LUASCRIPT"] if a.luascript else [])
editor_defines = common + ["UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_OSX"]
player_defines = [d for d in common if d not in ("ENABLE_MONO", "UNITY_STANDALONE_OSX", "UNITY_STANDALONE")] \
    + ["ENABLE_IL2CPP", "UNITY_ANDROID"]


def asmdef(path):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    return data


src = repo / "Assets/XLua/Src"
core = repo / "Assets/XLua/Core"
# The four asmdefs, at their places, with the GUIDs that other asmdefs, the UPM
# package and XLua > Project Layout (ProjectLayout.cs) rely on.
LAYOUT = {
    "Core/XLua.Core.asmdef": "4b076258bb6e496aa6f3404327f20852",
    "Core/Editor/XLua.Core.Editor.asmdef": "646ad94d870c4544bbd80a0b971efa07",
    "Src/XLua.asmdef": "d67ecd89999f4eea8a9ab3c0ecd8057d",
    "Src/Editor/XLua.Editor.asmdef": "ab5755dc6ef2406fae4ec66b0178bc2d",
}
problems = []
found = sorted(f.relative_to(repo / "Assets/XLua").as_posix() for f in (repo / "Assets/XLua").rglob("*.asmdef"))
if found != sorted(LAYOUT):
    problems.append("Assets/XLua has the asmdefs %s; expected %s (the default layout; Asmdef~ is the way back, not "
                    "part of the repository)" % (", ".join(found) or "none", ", ".join(sorted(LAYOUT))))
for rel, guid in LAYOUT.items():
    meta = repo / "Assets/XLua" / (rel + ".meta")
    if not meta.is_file() or not re.search(r"^guid: %s$" % guid, meta.read_text(), re.M):
        problems.append("%s.meta: the GUID must stay %s" % (rel, guid))
if problems:
    sys.exit("\n".join(problems))
runtime_def, editor_def = asmdef(src / "XLua.asmdef"), asmdef(src / "Editor/XLua.Editor.asmdef")
core_def = asmdef(core / "XLua.Core.asmdef")
core_editor_def = asmdef(core / "Editor/XLua.Core.Editor.asmdef")
if runtime_def.get("name") != "XLua" or runtime_def.get("includePlatforms") or runtime_def.get("references") != ["XLua.Core"]:
    problems.append("XLua.asmdef: expected name XLua, every platform, references [XLua.Core]")
if not runtime_def.get("allowUnsafeCode"):
    problems.append("XLua.asmdef: allowUnsafeCode must be true (LuaDLL, CopyByValue)")
if editor_def.get("name") != "XLua.Editor" or editor_def.get("includePlatforms") != ["Editor"] \
        or editor_def.get("references") != ["XLua", "XLua.Core"]:
    problems.append("XLua.Editor.asmdef: expected name XLua.Editor, Editor only, references [XLua, XLua.Core]")
if core_def.get("name") != "XLua.Core" or core_def.get("includePlatforms") or core_def.get("references") \
        or not core_def.get("allowUnsafeCode") or core_def.get("defineConstraints"):
    problems.append("XLua.Core.asmdef: expected name XLua.Core, every platform, no references, unsafe code, no define constraints")
if core_editor_def.get("name") != "XLua.Core.Editor" or core_editor_def.get("includePlatforms") != ["Editor"] \
        or core_editor_def.get("references") != ["XLua.Core"] or core_editor_def.get("defineConstraints"):
    problems.append("XLua.Core.Editor.asmdef: expected name XLua.Core.Editor, Editor only, references [XLua.Core], no define constraints")
for d in [runtime_def, editor_def]:
    if "!XLUA_CORE_ONLY" not in d.get("defineConstraints", []):
        problems.append(d.get("name") + ".asmdef: the !XLUA_CORE_ONLY define constraint is missing (core-luascript.md 2.2.1)")
for d, n in [(runtime_def, "XLua"), (editor_def, "XLua.Editor"), (core_def, "XLua.Core"), (core_editor_def, "XLua.Core.Editor")]:
    if d.get("autoReferenced", True) is not True:
        problems.append(n + ".asmdef: must stay autoReferenced, so that Assembly-CSharp and its Editor assembly see it")
if problems:
    sys.exit("\n".join(problems))


def constraints_hold(d, defines):
    """Unity's define constraints: every entry holds; "!X" means X is not defined, "A || B" either."""
    def one(term):
        term = term.strip()
        return term[1:] not in defines if term.startswith("!") else term in defines
    return all(any(one(t) for t in c.split("||")) for c in d.get("defineConstraints", []))


bridge = constraints_hold(runtime_def, common) and constraints_hold(editor_def, common)
if a.core_only and (constraints_hold(runtime_def, common) or constraints_hold(editor_def, common)):
    sys.exit("with XLUA_CORE_ONLY the define constraints must leave out XLua and XLua.Editor")
if not (constraints_hold(core_def, common) and constraints_hold(core_editor_def, common)):
    sys.exit("XLua.Core and XLua.Core.Editor must compile in every project")

out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
dotnet = a.dotnet or subprocess.run(["mise", "which", "dotnet"], stdout=subprocess.PIPE, text=True,
                                    check=True, cwd=repo).stdout.strip()
cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
if not cscs:
    sys.exit("no csc.dll in the .NET SDK next to " + dotnet)


analyzer = None
if a.source_gen:
    sys.path.insert(0, str(repo / "Tools"))
    import build_source_generator
    analyzer = build_source_generator.build(out / "source-gen-build")


def under(folder, editor_only):
    """C# files of folder, split as Unity does by an Editor folder in the path."""
    return [f for f in sorted(folder.rglob("*.cs"))
            if ("Editor" in f.relative_to(repo / "Assets").parts[:-1]) == editor_only]


runtime_sources = [f for f in sorted(src.rglob("*.cs")) if (src / "Editor") not in f.parents]
editor_sources = sorted((src / "Editor").rglob("*.cs"))
core_sources = [f for f in sorted(core.rglob("*.cs")) if (core / "Editor") not in f.parents]
core_editor_sources = sorted((core / "Editor").rglob("*.cs"))
sample = [repo / "Test/Core/UnitySample/LuaScriptSample.cs"] if a.core_only or a.luascript else []
user = [repo / "Assets/XLua/Examples", repo / "Assets/XLua/Tutorial"]
# Needs com.unity.ugui (UnityEngine.UI), a package that is not in the editor install.
needs_packages = {repo / "Assets/XLua/Examples/07_AsyncTest"}
user_runtime = [f for d in user if d.is_dir() for f in under(d, False) if not needs_packages & set(f.parents)]
user_editor = [f for d in user if d.is_dir() for f in under(d, True)]


def csc(name, sources, defines, refs, profile="player", analyze=True):
    # Player builds keep the assembly name of the Editor build (XLua.Core grants XLua its internals).
    dll = out / "player" / name.replace(".Player.dll", ".dll") if name.endswith(".Player.dll") else out / name
    dll.parent.mkdir(exist_ok=True)
    rsp = out / (name + ".rsp")
    bcl, api_defines = profiles[profile]
    lines = ["-nologo", "-noconfig", "-nostdlib", "-target:library", "-unsafe", "-langversion:" + a.langversion,
             "-nowarn:0618,0649,0168,0219,0414,0067,0109,0162,0429,8632",
             "-define:" + ";".join(defines + api_defines), "-out:" + str(dll)]
    lines += ["-r:" + str(r) for r in bcl + engine + refs]
    if analyzer and analyze:
        generated = out / (name + ".generated")
        generated.mkdir(exist_ok=True)
        lines += ["-analyzer:" + str(analyzer), "-generatedfilesout:" + str(generated)]
    lines += [str(s) for s in sources]
    rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines) + "\n")
    result = subprocess.run([dotnet, str(cscs[-1]), "@" + str(rsp)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True)
    (out / (name + ".log")).write_text(result.stdout)
    errors = [l for l in result.stdout.splitlines() if ": error " in l]
    print("%-28s %3d files  %s" % (name, len(sources), "ok" if result.returncode == 0 else "%d errors" % len(errors)))
    if result.returncode != 0:
        print("\n".join(errors[:60]))
        sys.exit(name + " does not compile; see " + str(out / (name + ".log")))
    if analyzer and analyze and re.search(r"warning (CS8032|CS8784|CS8785|CS9057)|(error|warning) XLSG", result.stdout):
        sys.exit("analyzer load problem, or a source generator error or warning, in " + str(out / (name + ".log")))
    return dll


if a.no_asmdef:
    # XLua > Project Layout > Use Assembly-CSharp: every script outside an Editor
    # folder in Assembly-CSharp, every Editor folder in Assembly-CSharp-Editor.
    for name in ["XLua.Core.dll", "XLua.Core.Editor.dll", "XLua.dll", "XLua.Editor.dll"]:
        (out / name).unlink(missing_ok=True)
    game = csc("Assembly-CSharp.dll", core_sources + runtime_sources + user_runtime + sample, editor_defines, [])
    csc("Assembly-CSharp-Player.dll", core_sources + runtime_sources + sample, player_defines, [])
    csc("Assembly-CSharp-Editor.dll", core_editor_sources + editor_sources + user_editor, editor_defines,
        [game] + editor, "editor")
    if analyzer:
        for name in ["Assembly-CSharp.dll", "Assembly-CSharp-Player.dll"]:
            structs = list((out / (name + ".generated")).rglob("XLua.Structs.g.cs"))
            if not structs or "Push_UnityEngine_Vector3_" not in structs[0].read_text():
                sys.exit(name + ": the source generator wrote no pushers for xLua's GCOptimize defaults in the same assembly")
        if not list((out / "Assembly-CSharp.dll.generated").rglob("W_*.g.cs")):
            sys.exit("Assembly-CSharp.dll: the source generator wrote no wrappers for the examples' configuration")
    print("XLUA_NO_ASMDEF_COMPILE_PASS: without asmdefs (Use Assembly-CSharp), xLua, the examples and the tutorial compile "
          "into Assembly-CSharp and Assembly-CSharp-Editor against " + str(contents.parent)
          + (" with HOTFIX_ENABLE" if a.hotfix else "") + (" with the source generator" if analyzer else "")
          + (" with LuaScript (XLUA_LUASCRIPT)" if a.luascript else ""))
    sys.exit(0)


# The generator sits in the XLua asmdef's folder, so Unity runs it only in
# assemblies that reference XLua: not in XLua.Core.
xlua_core = csc("XLua.Core.dll", core_sources, editor_defines, [], analyze=False)
xlua_core_player = csc("XLua.Core.Player.dll", core_sources, player_defines, [], analyze=False)
xlua_core_editor = csc("XLua.Core.Editor.dll", core_editor_sources, editor_defines, [xlua_core] + editor, "editor",
                       analyze=False)
if bridge:
    xlua = csc("XLua.dll", runtime_sources, editor_defines, [xlua_core])
    csc("XLua.Player.dll", runtime_sources, player_defines, [xlua_core_player])
    xlua_editor = csc("XLua.Editor.dll", editor_sources, editor_defines, [xlua, xlua_core] + editor, "editor")
    game = csc("Assembly-CSharp.dll", user_runtime + sample, editor_defines, [xlua, xlua_core])
    csc("Assembly-CSharp-Player.dll", sample, player_defines, [xlua_core_player], analyze=False) if sample else None
    csc("Assembly-CSharp-Editor.dll", user_editor, editor_defines, [xlua, xlua_editor, xlua_core, xlua_core_editor, game]
        + editor, "editor")
else:
    # Core only: the sample against XLua.Core alone; no example uses LuaScript yet.
    for name in ["XLua.dll", "XLua.Editor.dll"]:
        (out / name).unlink(missing_ok=True)
    csc("Assembly-CSharp.dll", sample, editor_defines, [xlua_core], analyze=False)
    csc("Assembly-CSharp-Player.dll", sample, player_defines, [xlua_core_player], analyze=False)
    print("XLua and XLua.Editor are left out by their define constraints (XLUA_CORE_ONLY)")
if analyzer and bridge:
    for name in ["XLua.dll", "XLua.Player.dll"]:
        structs = list((out / (name + ".generated")).rglob("XLua.Structs.g.cs"))
        if not structs or "Push_UnityEngine_Vector3_" not in structs[0].read_text():
            sys.exit(name + ": the source generator wrote no pushers for xLua's GCOptimize defaults")
    # Assembly-CSharp reads them from XLua.dll's metadata ([assembly: GCOptimize(typeof(A), typeof(B)...)])
    # and writes pushers for those the examples' configuration uses.
    structs = list((out / "Assembly-CSharp.dll.generated").rglob("XLua.Structs.g.cs"))
    if not structs or "Push_UnityEngine_Vector3_" not in structs[0].read_text():
        sys.exit("Assembly-CSharp.dll: the source generator wrote no pushers for the GCOptimize defaults of the referenced XLua.dll")
if a.core_only:
    print("XLUA_ASMDEF_CORE_ONLY_PASS: with XLUA_CORE_ONLY only XLua.Core and XLua.Core.Editor compile, and a LuaScript "
          "sample builds against XLua.Core alone (" + str(contents.parent) + ")")
else:
    print("XLUA_ASMDEF_COMPILE_PASS: XLua.Core, XLua.Core.Editor, XLua, XLua.Editor, Assembly-CSharp and Assembly-CSharp-Editor "
          "compile against " + str(contents.parent) + (" with HOTFIX_ENABLE" if a.hotfix else "")
          + (" with the source generator" if analyzer else "") + (" with LuaScript (XLUA_LUASCRIPT)" if a.luascript else ""))
