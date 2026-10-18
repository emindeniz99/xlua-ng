#!/usr/bin/env python3
"""Check the configuration migration tool (XLuaGenerate --migrate, source generator milestone M9) on the examples.

The tool converts the configuration lists that only the legacy generator reads
into the assembly-level attributes the source generator reads. This script
compiles xLua's examples and tutorials against a Unity editor's DLLs, without
starting Unity, and checks two configurations:

  examples  the lists of Assets/XLua/Examples (ExampleGenConfig, CoroutineConfig,
            MessageBoxConfig, the Editor folder's HotfixTestCfg). The tool's
            output must equal the checked-in Assets/XLua/Examples/XLuaConfig.cs
            (--update writes it).
  sample    Test/Native/MigrationSample/ExampleConfigSample.cs: ExampleConfig.cs
            with its LINQ queries over the loaded assemblies switched on, which
            the tool evaluates when it runs, plus a filter, DoNotGen,
            GCOptimize(PackAsTable) and AdditionalProperties lists.

For each one, the legacy generator (XLuaGenerate, with XLUA_GEN_MANIFEST) runs
on the assemblies with the lists, and the source generator compiles the same
sources with the tool's attributes instead; Test/Native/surface_diff.py then
compares the binding surfaces, the bridge sets and the GCOptimize types. The
source generator build fails on a generator warning or error.

The assemblies, as Unity compiles them in the default layout (xLua in
Assembly-CSharp), with the editor's .NET 4.8 API profile so that Mono can load
them:

  UnityEngine.UI.dll         com.unity.ugui from the editor's built-in packages
  Assembly-CSharp.dll        Assets/XLua/Src without Editor, the examples and tutorials
  Assembly-CSharp-Editor.dll their Editor folders (HotfixTestCfg.cs, BuildFromCLI.cs)
  XLuaDefaultConfig.dll      Assets/XLua/Editor (ExampleConfig.cs, XLuaUnityDefaultConfig.cs),
                             which Unity also compiles into Assembly-CSharp-Editor;
                             Generate Code reads them, the examples' conversion leaves
                             them to the project's own run of the tool
  Sample.dll                 the sample (only for the sample configuration)

    python3 Test/Native/migration_check.py --plugin libxlua.dylib --output DIR [--unity Unity.app] [--update]
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

repo = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo / "Tools"))
import build_source_generator  # noqa: E402

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="the host's native plugin (XLuaGenerate starts a LuaEnv)")
p.add_argument("--output", type=Path, required=True)
p.add_argument("--unity", type=Path, default=Path("/Volumes/T7Data/Unity/Editors/2021.3.45f2/Unity.app"),
               help="Unity.app (macOS) or the editor's folder that contains Contents or Data")
p.add_argument("--unity-version", default="2021.3", help="major.minor for the UNITY_X_Y_OR_NEWER defines")
p.add_argument("--mono", default="mono")
p.add_argument("--bcl", choices=["mono", "unity"], default="mono",
               help="the .NET assemblies to compile against: Mono's lib/mono/4.5 (default), which the legacy generator "
                    "reflects on here, or the editor's unity-4.8-api reference assemblies, whose member order differs "
                    "(so overload ties of .NET types are ordered differently; see the M9 notes in source-generator.md)")
p.add_argument("--update", action="store_true", help="write the tool's output to Assets/XLua/Examples/XLuaConfig.cs")
p.add_argument("--only", choices=["examples", "sample", "edge"], help="check one configuration")
a = p.parse_args()

out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
EXAMPLES_CONFIG = repo / "Assets/XLua/Examples/XLuaConfig.cs"


def find_contents(unity):
    for c in [unity / "Contents", unity / "Data", unity]:
        if (c / "Managed").is_dir() or (c / "Resources/Scripting/Managed").is_dir():
            return c
    sys.exit("no Unity Managed folder under " + str(unity))


contents = find_contents(a.unity.resolve())
scripting = contents if (contents / "Managed/UnityEditor.dll").is_file() else contents / "Resources/Scripting"
managed = scripting / "Managed"
api48 = scripting / "UnityReferenceAssemblies/unity-4.8-api"
if a.bcl == "unity":
    bcl = sorted(api48.glob("*.dll")) + sorted((api48 / "Facades").glob("*.dll"))
else:
    mono_api = Path(shutil.which(a.mono)).resolve().parents[1] / "lib/mono/4.5"
    bcl = [mono_api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "System.Xml.dll", "System.Xml.Linq.dll",
                                  "System.Numerics.dll", "System.Runtime.Serialization.dll"]] + sorted((mono_api / "Facades").glob("*.dll"))
engine = [managed / "UnityEngine.dll"] + sorted((managed / "UnityEngine").glob("UnityEngine*.dll"))
editor = [managed / "UnityEditor.dll"]
packages = [c / "Resources/PackageManager/BuiltInPackages" for c in [contents, contents.parent]]
ugui = next((d / "com.unity.ugui/Runtime" for d in packages if (d / "com.unity.ugui/Runtime").is_dir()), None)
if not all(f.is_file() for f in bcl[:7]) or not ugui:
    sys.exit("missing Unity's unity-4.8-api or the com.unity.ugui built-in package under " + str(contents))

major, minor = (int(x) for x in a.unity_version.split(".")[:2])
releases = [(5, m) for m in range(3, 7)] + [(y, m) for y in range(2017, 2021) for m in range(1, 5)] \
    + [(2020, m) for m in range(1, 4)] + [(2021, m) for m in range(1, 4)] + [(2022, m) for m in range(1, 4)] \
    + [(2023, m) for m in range(1, 3)] + [(6000, m) for m in range(0, 10)]
defines = ["UNITY_%d_%d_OR_NEWER" % r for r in releases if r <= (major, minor)] + [
    "UNITY_%d" % major, "UNITY_%d_%d" % (major, minor), "UNITY_64", "UNITY_ASSERTIONS", "ENABLE_MONO", "CSHARP_7_3_OR_NEWER",
    "UNITY_STANDALONE_OSX", "UNITY_STANDALONE", "UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_OSX", "NET_4_6", "NET_UNITY_4_8"]

dotnet = build_source_generator.dotnet_path()
cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
if not cscs:
    sys.exit("no csc.dll in the .NET SDK next to " + dotnet)


def csc(name, sources, refs, extra_defines=(), analyzer=None, generated=None):
    dll = out / name
    lines = ["-nologo", "-noconfig", "-nostdlib", "-target:library", "-unsafe", "-langversion:9", "-deterministic",
             "-nowarn:0618,0649,0168,0219,0414,0067,0109,0162,0429,8632",
             "-define:" + ";".join(defines + list(extra_defines)), "-out:" + str(dll)]
    lines += ["-r:" + str(r) for r in bcl + engine + refs]
    if analyzer:
        generated.mkdir(parents=True, exist_ok=True)
        lines += ["-analyzer:" + str(analyzer), "-generatedfilesout:" + str(generated)]
    lines += [str(s) for s in sources]
    rsp = out / (name + ".rsp")
    rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines) + "\n")
    result = subprocess.run([dotnet, str(cscs[-1]), "@" + str(rsp)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    (out / (name + ".log")).write_text(result.stdout)
    errors = [l for l in result.stdout.splitlines() if ": error " in l]
    print("%-34s %4d files  %s" % (name, len(sources), "ok" if result.returncode == 0 else "%d errors" % len(errors)))
    if result.returncode != 0:
        print("\n".join(errors[:40]))
        sys.exit(name + " does not compile; see " + str(out / (name + ".log")))
    if analyzer and re.search(r"warning (CS8032|CS8784|CS8785|CS9057)|(error|warning) XLSG", result.stdout):
        print("\n".join(l for l in result.stdout.splitlines() if "XLSG" in l or "CS90" in l or "CS87" in l)[:4000])
        sys.exit("analyzer load problem, or a source generator error or warning, in " + str(out / (name + ".log")))
    return dll


def under(folder, editor_only):
    """C# files of folder, split as Unity does by an Editor folder in the path."""
    return [f for f in sorted(folder.rglob("*.cs"))
            if ("Editor" in f.relative_to(repo / "Assets").parts[:-1]) == editor_only]


src = repo / "Assets/XLua/Src"
runtime_sources = [f for f in sorted(src.rglob("*.cs")) if (src / "Editor") not in f.parents]
user = [repo / "Assets/XLua/Examples", repo / "Assets/XLua/Tutorial"]
# The examples' checked-in conversion is not part of the legacy build's input;
# the source generator builds add the configuration they check.
user_runtime = [f for d in user for f in under(d, False) if f != EXAMPLES_CONFIG]
# BuildFromCLI.cs calls xLua's Editor code (Generator.GenAll); it has no configuration.
user_editor = [f for d in user for f in under(d, True) if f.name != "BuildFromCLI.cs"]
default_config = sorted((repo / "Assets/XLua/Editor").glob("*.cs"))
sample = repo / "Test/Native/MigrationSample/ExampleConfigSample.cs"

# The tools, with the plugin next to XLuaGenerate.
tools = out / "tools"
subprocess.run([sys.executable, str(repo / "Tools/build_tools.py"), "--output", str(tools)], check=True,
               stdout=subprocess.DEVNULL)
shutil.copy2(a.plugin, tools / a.plugin.name)
env = dict(os.environ)
for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
    env[key] = str(tools) + os.pathsep + env.get(key, "")
analyzer = build_source_generator.build(out / "source-gen-build")

ui = csc("UnityEngine.UI.dll", sorted(ugui.rglob("*.cs")), editor,
         ["PACKAGE_PHYSICS", "PACKAGE_PHYSICS2D", "PACKAGE_TILEMAP", "PACKAGE_ANIMATION", "PACKAGE_UITOOLKIT"])
game = csc("Assembly-CSharp.dll", runtime_sources + user_runtime, [ui])
game_editor = csc("Assembly-CSharp-Editor.dll", user_editor, [game, ui] + editor)
default_dll = csc("XLuaDefaultConfig.dll", default_config, [game] + editor)
sample_dll = csc("Sample.dll", [sample], [game, ui])
edge = repo / "Test/Native/MigrationSample/MigrationEdgeCases.cs"
edge_dll = csc("Edge.dll", [edge], [game])
search = [str(managed), str(managed / "UnityEngine"), str(out), str(api48)]


def xluagenerate(args, log):
    result = subprocess.run([a.mono, str(tools / "XLuaGenerate.exe")] + args, cwd=tools, env=env,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="backslashreplace")
    (out / log).write_text(result.stdout)
    return result


def check(name, inputs, lists_in, extra_sources, compare=None, expect_files=("Assembly-CSharp",), expect_report=()):
    """Run the tool on inputs (converting the lists of lists_in), the legacy generator on the same
    assemblies, and the source generator on one compilation of the sources (extra_sources stand for the
    other inputs) with the tool's output; diff the surfaces. Returns the files the tool wrote."""
    migrated = out / ("migrate-" + name)
    shutil.rmtree(migrated, ignore_errors=True)
    env["XLUA_MIGRATION_EDGE_THROW"] = "1"
    result = xluagenerate(["--migrate", "--output", str(migrated), "--bindings-host", "Assembly-CSharp",
                           "--source-root", str(repo / "Assets/XLua"), "--source-root", str(repo / "Test/Native/MigrationSample")]
                          + sum([["--lists-in", l] for l in lists_in], []) + sum([["--define", d] for d in defines], [])
                          + [str(i) for i in inputs] + search, "migrate-" + name + ".log")
    del env["XLUA_MIGRATION_EDGE_THROW"]
    print(result.stdout.strip())
    if result.returncode != 0:
        sys.exit(name + ": XLuaGenerate --migrate failed; see " + str(migrated / "XLuaMigrationReport.md"))
    files = sorted(migrated.glob("*.XLuaConfig.cs"))
    if [f.name for f in files] != [n + ".XLuaConfig.cs" for n in sorted(expect_files)]:
        sys.exit(name + ": expected files for " + ", ".join(expect_files) + ", got " + ", ".join(f.name for f in files))
    report = (migrated / "XLuaMigrationReport.md").read_text(encoding="utf-8")
    missing = [e for e in expect_report if e not in report]
    if missing:
        sys.exit(name + ": the report lacks " + "; ".join(repr(m) for m in missing) + "; see " + str(migrated / "XLuaMigrationReport.md"))
    config = files[0]
    if compare is not None:
        if a.update:
            compare.write_text(config.read_text(encoding="utf-8"), encoding="utf-8")
            print("wrote", compare.relative_to(repo))
        elif not compare.is_file() or compare.read_text(encoding="utf-8") != config.read_text(encoding="utf-8"):
            sys.exit(name + ": the tool's output differs from " + str(compare.relative_to(repo)) + " (--update writes it)")
        else:
            print("XLUA_MIGRATION_OUTPUT_PASS: " + name + ": the tool reproduces " + str(compare.relative_to(repo)))
    # The legacy surface, from the lists.
    gen = out / ("gen-" + name)
    shutil.rmtree(gen, ignore_errors=True)
    legacy_surface = out / ("legacy-surface-" + name + ".json")
    env["XLUA_GEN_MANIFEST"] = str(legacy_surface)
    result = xluagenerate([str(i) for i in inputs] + [str(gen) + "/"] + search, "legacy-" + name + ".log")
    del env["XLUA_GEN_MANIFEST"]
    if result.returncode != 0 or not legacy_surface.is_file():
        print(result.stdout[-3000:])
        sys.exit(name + ": the legacy generator failed; see " + str(out / ("legacy-" + name + ".log")))
    # The source generator, from the attributes.
    generated = out / ("source-gen-" + name)
    shutil.rmtree(generated, ignore_errors=True)
    csc("Assembly-CSharp." + name + ".sg.dll", runtime_sources + user_runtime + list(extra_sources) + files, [ui],
        analyzer=analyzer, generated=generated)
    manifest = next(generated.rglob("XLua.Manifest.g.cs"))
    allow = out / ("surface-allow-" + name + ".json")
    allow.write_text(json.dumps(ALLOWED.get(name, [])))
    diff = subprocess.run([sys.executable, str(repo / "Test/Native/surface_diff.py"), str(legacy_surface), str(manifest), "--allow", str(allow)],
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    (out / ("surface-diff-" + name + ".log")).write_text(diff.stdout)
    print("\n".join(l for l in diff.stdout.splitlines() if not l.startswith("fallback:")))
    if diff.returncode != 0:
        sys.exit(name + ": the binding surfaces differ; see " + str(out / ("surface-diff-" + name + ".log")))
    print("XLUA_MIGRATION_CHECK_PASS: " + name)
    return files


# Documented differences between the legacy generator and the source generator
# that the conversion does not cause (docs/design/source-generator.md, "M9 as
# implemented"), as surface_diff.py --allow entries.
ALLOWED = {
    "examples": [],
    "edge": [
        {"type": "System.Int32[]", "field": "type", "value": "System.Int32[]",
         "reason": "array types in LuaCallCSharp are not wrapped by the source generator (XLSG004, M2 difference 4)"},
    ],
    "sample": [
        {"type": "UnityEngine.TrailRenderer", "field": "methods", "value": "AddPositions",
         "reason": "overload order: the NativeArray and NativeSlice overloads have [Out] on a parameter that is not by-ref; the "
                   "legacy OverloadCosting does not count it (ParameterInfo.IsOut), and the source generator cannot see the "
                   "metadata flag through Roslyn's public API, so it keeps declaration order"},
    ],
}

examples_inputs = [game, game_editor, default_dll]
examples_config = [EXAMPLES_CONFIG]
if a.only in (None, "examples"):
    examples_config = check("examples", examples_inputs, ["Assembly-CSharp", "Assembly-CSharp-Editor"], [], EXAMPLES_CONFIG,
                            expect_report=["`XLuaTest.CoroutineConfig.LuaCallCSharp` is computed when it is read",
                                           "`UnityEngine.WWW` in `ExampleGenConfig.LuaCallCSharp`: [Obsolete]",
                                           "`ExampleConfig.BlackList`: declared in assembly XLuaDefaultConfig"])
# The other configurations keep the examples' lists, converted by the examples' file.
if a.only in (None, "sample"):
    check("sample", examples_inputs + [sample_dll], ["Sample"], examples_config,
          expect_report=["`ExampleConfigSample.LuaCallCSharp` ([LuaCallCSharp], computed by a property getter)",
                         "`ExampleConfigSample.MethodFilter` is a filter: it matched 6 members"])
if a.only in (None, "edge"):
    check("edge", examples_inputs + [edge_dll], ["Edge"], examples_config + [edge], expect_files=["Assembly-CSharp", "Edge"],
          expect_report=["threw for MigrationEdge.Plain.Thrower: InvalidOperationException: filter failure",
                         "`MigrationEdge.Outer`1[T]` in `MigrationEdge.Lists.Wrapped`: an open generic type",
                         "`MigrationEdge.Callback` in `MigrationEdge.Lists.Wrapped`: a delegate",
                         "`MigrationEdge.Plain` in `MigrationEdge.Lists.Bridged`: neither a delegate nor an interface",
                         "`MigrationEdge.Hidden+Private` in `MigrationEdge.Lists.Reflected`: no typeof spelling (not accessible",
                         "`Missing` of `MigrationEdge.Plain` in `MigrationEdge.Lists.Lazy`: no such member",
                         "`MigrationEdge.Outer`1[System.Int32]` in `MigrationEdge.Lists.Patched`: the hotfix injector skips it",
                         "`MigrationEdge.Both` in `MigrationEdge.Lists.Wrapped`: also `[LuaCallCSharp]` on the type",
                         "`MigrationEdge.Plain, NoSuchMember` (MigrationEdge.Lists.Black): no member matches",
                         "`MigrationEdge.NoSuchType, Member` (MigrationEdge.Lists.Black): no loaded type has that name",
                         "`MigrationEdge.Plain, Twice, System.Int32&` (MigrationEdge.Lists.Black): a parameter type has no typeof spelling"])
print("XLUA_MIGRATION_PASS")
