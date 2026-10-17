"""Compile xLua in its asmdef layout against a Unity editor's DLLs, without starting Unity.

The asmdefs are opt-in: Assets/XLua/Asmdef~/Src holds XLua.asmdef and
Editor/XLua.Editor.asmdef, which a project copies into Assets/XLua/Src and
Assets/XLua/Src/Editor (Unity does not import folders ending in ~). With them,
Unity compiles Assets/XLua/Src into the XLua assembly and Assets/XLua/Src/Editor
into XLua.Editor. Everything else
in Assets/XLua goes into the predefined assemblies: scripts into
Assembly-CSharp and scripts under an Editor folder into Assembly-CSharp-Editor,
which reference every auto-referenced asmdef. This script builds the same four
assemblies with the .NET SDK's csc (mise.toml) against the reference
assemblies and engine DLLs of the given Unity editor, in the order Unity does:

  XLua.dll                  the runtime, for the Editor (UNITY_EDITOR) and,
                            as XLua.Player.dll, for an IL2CPP player
  XLua.Editor.dll           -r XLua.dll, UnityEditor
  Assembly-CSharp.dll       examples and tutorials, -r XLua.dll
  Assembly-CSharp-Editor.dll their Editor folders, -r all of the above

A failure shows code that relied on xLua being compiled into Assembly-CSharp:
a use of an internal member across the new assembly boundary (CS0122), or a
reference from XLua to project code. It reads the asmdef files to check that
the layout is the one described above (names, references, platforms).
Nothing is generated: `run_managed.py --gen-assembly` covers generated code.

    python3 Test/Native/asmdef_compile_check.py --unity /path/Unity.app --output DIR [--hotfix] [--define NAME]
"""
import argparse
import json
from pathlib import Path
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
a = p.parse_args()


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
                            "UNITY_STANDALONE"] + (["HOTFIX_ENABLE"] if a.hotfix else []) + a.define
editor_defines = common + ["UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_OSX"]
player_defines = [d for d in common if d not in ("ENABLE_MONO", "UNITY_STANDALONE_OSX", "UNITY_STANDALONE")] \
    + ["ENABLE_IL2CPP", "UNITY_ANDROID"]


def asmdef(path):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    return data


src = repo / "Assets/XLua/Src"
asmdefs = repo / "Assets/XLua/Asmdef~/Src"
runtime_def, editor_def = asmdef(asmdefs / "XLua.asmdef"), asmdef(asmdefs / "Editor/XLua.Editor.asmdef")
if list(src.rglob("*.asmdef")):
    sys.exit("Assets/XLua/Src has an asmdef: the default layout compiles xLua into Assembly-CSharp; "
             "the opt-in asmdefs belong in Assets/XLua/Asmdef~")
problems = []
if runtime_def.get("name") != "XLua" or runtime_def.get("includePlatforms") or runtime_def.get("references"):
    problems.append("XLua.asmdef: expected name XLua, every platform, no references")
if not runtime_def.get("allowUnsafeCode"):
    problems.append("XLua.asmdef: allowUnsafeCode must be true (LuaDLL, CopyByValue)")
if editor_def.get("name") != "XLua.Editor" or editor_def.get("includePlatforms") != ["Editor"] \
        or editor_def.get("references") != ["XLua"]:
    problems.append("XLua.Editor.asmdef: expected name XLua.Editor, Editor only, references [XLua]")
for d, n in [(runtime_def, "XLua"), (editor_def, "XLua.Editor")]:
    if d.get("autoReferenced", True) is not True:
        problems.append(n + ".asmdef: must stay autoReferenced, so that Assembly-CSharp and its Editor assembly see it")
if problems:
    sys.exit("\n".join(problems))

out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
dotnet = a.dotnet or subprocess.run(["mise", "which", "dotnet"], stdout=subprocess.PIPE, text=True,
                                    check=True, cwd=repo).stdout.strip()
cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
if not cscs:
    sys.exit("no csc.dll in the .NET SDK next to " + dotnet)


def under(folder, editor_only):
    """C# files of folder, split as Unity does by an Editor folder in the path."""
    return [f for f in sorted(folder.rglob("*.cs"))
            if ("Editor" in f.relative_to(repo / "Assets").parts[:-1]) == editor_only]


runtime_sources = [f for f in sorted(src.rglob("*.cs")) if (src / "Editor") not in f.parents]
editor_sources = sorted((src / "Editor").rglob("*.cs"))
user = [repo / "Assets/XLua/Examples", repo / "Assets/XLua/Tutorial", repo / "Assets/XLua/Editor"]
# Needs com.unity.ugui (UnityEngine.UI), a package that is not in the editor install.
needs_packages = {repo / "Assets/XLua/Examples/07_AsyncTest"}
user_runtime = [f for d in user if d.is_dir() for f in under(d, False) if not needs_packages & set(f.parents)]
user_editor = [f for d in user if d.is_dir() for f in under(d, True)]


def csc(name, sources, defines, refs, profile="player"):
    dll = out / name
    rsp = out / (name + ".rsp")
    bcl, api_defines = profiles[profile]
    lines = ["-nologo", "-noconfig", "-nostdlib", "-target:library", "-unsafe", "-langversion:" + a.langversion,
             "-nowarn:0618,0649,0168,0219,0414,0067,0109,0162,0429,8632",
             "-define:" + ";".join(defines + api_defines), "-out:" + str(dll)]
    lines += ["-r:" + str(r) for r in bcl + engine + refs]
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
    return dll


xlua = csc("XLua.dll", runtime_sources, editor_defines, [])
csc("XLua.Player.dll", runtime_sources, player_defines, [])
xlua_editor = csc("XLua.Editor.dll", editor_sources, editor_defines, [xlua] + editor, "editor")
game = csc("Assembly-CSharp.dll", user_runtime, editor_defines, [xlua])
csc("Assembly-CSharp-Editor.dll", user_editor, editor_defines, [xlua, xlua_editor, game] + editor, "editor")
print("XLUA_ASMDEF_COMPILE_PASS: XLua, XLua.Editor, Assembly-CSharp and Assembly-CSharp-Editor compile against "
      + str(contents.parent) + (" with HOTFIX_ENABLE" if a.hotfix else ""))
