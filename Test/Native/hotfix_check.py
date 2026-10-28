"""Check hotfix injection end to end with XLuaHotfixInject and Mono.

The hotfix bridges come from the xLua source generator, which writes a
HotfixBridges class (bridges and IntKey slots) into each assembly with
[Hotfix] types; the legacy generator's DelegatesGensBridge went with it in
source generator milestone M11.

1. Builds the tools (Tools/build_tools.py) and compiles Test/Native/HotfixFixture.cs
   with the xLua sources (XLUA_GENERAL, HOTFIX_ENABLE) by the .NET SDK's csc
   with the generator (source_gen_build.py). Its hotfix bridge signatures must
   equal Test/Native/expected-surface/hotfix-fixture.json, the ones the legacy
   generator wrote for the same fixture when it was removed (surface_diff.py).
2. Builds the fixture with Mono's mcs and the generator's files (C# 9, which
   mcs reads once #nullable is left out), with each kind of debug symbols: a
   portable .pdb (what Roslyn and Unity write; converted from mcs output with
   MdbToPortablePdb.cs), the .mdb that Mono's mcs writes for -debug, no
   symbols, and an unreadable .pdb (the injector must then ignore it). For
   each, injects it, checks that IntKey used the fixture's own slots (the id
   map names the assembly), that a second injection reports "had injected!"
   and leaves the file unchanged, and runs the fixture under `mono --debug`:
   original behaviour before patching, patched behaviour after xlua.hotfix,
   original behaviour again after xlua.hotfix(..., nil), and whether stack
   traces of instrumented methods still have line numbers.
   Before that, ShortBranches.cs gives the fixture's LoopN and FwdN
   constructors short branches, as Roslyn writes them; injection must widen
   those that no longer fit (Tencent/xLua#1003). Before and after injection,
   `ShortBranches.exe --check` checks the IL of the fixture's methods as a
   verifier would: Mono runs IL that IL2CPP cannot convert, such as a hotfix
   placed inside the finally handler that ends a constructor
   (Tencent/xLua#1064; the shapes are in HotfixRegions.cs).
3. Injects a fixture given as bare file names, with an id map path that has
   no directory part (the injector used to throw for it), and runs it.
4. Injects HotfixFixture.Second.dll (HotfixFixtureSecond.cs), which mcs builds
   without the generator and whose type only a [Hotfix] configuration list
   names: the injector must stop with the hint to convert the list.
5. The asmdef layout: xLua alone in XLua.dll, the fixture compiled with the
   generator against it, injected with XLua.dll as xLua's assembly: IntKey
   through the fixture's own slots, and the fixture patches its IntKey type.
6. The fixture as csc compiles it (Roslyn's IL, as Unity's): injected, its IL
   checked and run. Then HotfixFixture.SecondSG.dll (HotfixFixtureSecondSG.cs),
   also compiled with the generator, in a second assembly: [Hotfix] on its types
   and at assembly level, IntKey through its own slots without the field-mode
   warning, Inline and a generic method injected, all patched by name after
   auto_id_map() reads the id maps merged by `XLuaHotfixInject --merge-id-maps`.
   That assembly, compiled at C# 12, also holds the hotfix cases of C# `in` and
   `ref readonly` parameters, which mcs cannot declare: plain and virtual
   methods (whose parameters carry modreq(InAttribute)), in the default,
   IntKey and Inline modes, with a `ref` control; a patch's extra result must
   reach the `ref` argument only.
7. HotfixFixtureShared.cs, compiled by mcs with xLua and without the
   generator: no HotfixBridges in the assembly that holds xLua, so its
   `Inline` IntKey type (and a generic method) get ids of the process-wide
   list, which the map gives without an assembly; patched through
   HotfixDelegateBridge.Set, and ids set from four threads at once must all
   stay set.
8. Test/Native/hotfix_flags_race.py: the iOS native flags of that list
   (Assets/Plugins/iOS/HotfixFlags.cpp) read while two threads grow them,
   under ThreadSanitizer.

Until M11, steps 2 to 5 used Generate Code's bridges, steps 4 and 5 checked that
an assembly without them fell back to field mode and took the bridges from the
assembly next to it, and step 1 compared the two generators' bridges.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

# Line numbers in stack traces before and after injection, per symbol format.
SYMBOLS = {"pdb": "present", "mdb": "present", "none": "missing", "unreadable": "missing"}

repo = Path(__file__).resolve().parents[2]
FIXTURE = [repo / "Test/Native/HotfixFixture.cs", repo / "Test/Native/HotfixRegions.cs"]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="host-native libxlua")
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--define", action="append", default=[],
               help="extra C# symbol for xLua, the tools and the fixture, e.g. XLUA_LOCK_METATABLES (repeatable)")
p.add_argument("--mono-api", type=Path,
               help="the Mono assemblies csc compiles against (default: lib/mono/4.5 next to the mono executable)")
p.add_argument("--cecil-dir", type=Path,
               help="build and run the injector with the Mono.Cecil of this com.unity.nuget.mono-cecil folder "
                    "(passed to Tools/build_tools.py) instead of the NuGet package")
a = p.parse_args()
out = a.output.resolve()
tools = out / "tools"
out.mkdir(parents=True, exist_ok=True)
env = dict(os.environ)
for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
    env[key] = os.pathsep.join([str(tools), env.get(key, "")])


def run(cmd, **kw):
    print("+", " ".join(str(c) for c in cmd), flush=True)
    return subprocess.run([str(c) for c in cmd], check=True, **kw)


def run_output(cmd, cwd, timeout=600):
    print("+", " ".join(str(c) for c in cmd), flush=True)
    # A hang (for example a finalizer deadlock) fails the check instead of blocking it.
    result = subprocess.run([str(c) for c in cmd], cwd=cwd, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, timeout=timeout)
    print(result.stdout, flush=True)
    return result


def xlua_sources():
    project = repo / "General/vs2013/XLua.Mini.csproj"
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]


def compile_fixture(dest, extra_sources=(), debug=False):
    dest.parent.mkdir(parents=True, exist_ok=True)
    run([a.compiler, "-unsafe", "-define:" + ";".join(["XLUA_GENERAL", "HOTFIX_ENABLE"] + a.define), "-out:" + str(dest)]
        + (["-debug"] if debug else []) + xlua_sources() + FIXTURE + list(extra_sources))
    shutil.copy2(a.plugin, dest.parent / a.plugin.name)


def fixture(exe, *args):
    result = run_output([a.mono, "--debug", exe] + list(args), cwd=exe.parent)
    if result.returncode != 0 or "HOTFIX_FIXTURE_PASS" not in result.stdout:
        raise SystemExit("fixture failed: " + " ".join([exe.name] + [str(x) for x in args]))
    for state in ["present", "missing"]:
        if "SYMBOLS: " + state in result.stdout:
            return state
    raise SystemExit("fixture printed no SYMBOLS line")


def inject(exe, id_map):
    result = run_output([a.mono, tools / "XLuaHotfixInject.exe", exe, exe, id_map], cwd=exe.parent)
    result.check_returncode()
    return result.stdout


run([sys.executable, repo / "Tools/build_tools.py", "--output", tools, "--compiler", a.compiler]
    + ["--define=" + d for d in a.define] + (["--cecil-dir", a.cecil_dir] if a.cecil_dir else []))
# Unity's Cecil builds target netstandard2.0, so the helpers below need Mono's facade too.
cecil_extra = ["-r:Facades/netstandard.dll"] if a.cecil_dir else []
shutil.copy2(a.plugin, tools / a.plugin.name)

# 1. The hotfix bridges come from the source generator: the fixture with xLua,
# compiled by the .NET SDK's csc with the generator (as Unity's Roslyn would),
# writes HotfixBridges for its [Hotfix] types. Their signatures must be those
# the legacy generator wrote for the same fixture when it was removed (the
# frozen Test/Native/expected-surface/hotfix-fixture.json).
sys.path.insert(0, str(repo / "Test/Native"))
import source_gen_build  # noqa: E402
builder = source_gen_build.Builder(out / "source-gen-build", a.mono, a.mono_api)
DEFINES = ["XLUA_GENERAL", "HOTFIX_ENABLE"] + a.define
stage1 = out / "stage1/HotfixFixture.exe"
builder.compile(stage1, xlua_sources() + FIXTURE, defines=DEFINES)
manifest = next(builder.generated(stage1).rglob("XLua.Manifest.g.cs"))
diff = run_output([sys.executable, repo / "Test/Native/surface_diff.py", repo / "Test/Native/expected-surface/hotfix-fixture.json",
                   manifest], cwd=out)
if diff.returncode != 0 or "XLUA_HOTFIX_BRIDGE_DIFF_PASS" not in diff.stdout:
    raise SystemExit("the source generator's hotfix bridges differ from Test/Native/expected-surface/hotfix-fixture.json")
# The generator's files, for the fixture builds of Mono's mcs below (which writes
# the .mdb and the long branches the checks need): its output is C# 9, which mcs
# reads except for #nullable.
generated_dir = out / "stage1/mcs-sources"
shutil.rmtree(generated_dir, ignore_errors=True)
generated_dir.mkdir()
generated = []
for f in sorted(builder.generated(stage1).rglob("*.g.cs")):
    target = generated_dir / f.name
    target.write_text("".join(line for line in f.read_text(encoding="utf-8").splitlines(True) if not line.startswith("#nullable")),
                      encoding="utf-8")
    generated.append(target)
if not any(f.name == "XLua.HotfixBridges.g.cs" for f in generated):
    raise SystemExit("the source generator wrote no HotfixBridges for the fixture")
converter = tools / "MdbToPortablePdb.exe"
run([a.compiler, "-out:" + str(converter)] + ["-r:" + str(tools / n) for n in ["Mono.Cecil.dll", "Mono.Cecil.Mdb.dll"]]
    + cecil_extra + [repo / "Test/Native/MdbToPortablePdb.cs"])
shortener = tools / "ShortBranches.exe"
run([a.compiler, "-out:" + str(shortener)] + ["-r:" + str(tools / n) for n in ["Mono.Cecil.dll", "Mono.Cecil.Rocks.dll"]]
    + cecil_extra + [repo / "Test/Native/ShortBranches.cs"])


def shorten(exe):
    """Give the LoopN and FwdN constructors the short branches Roslyn would write (mcs writes long ones)."""
    tmp = exe.parent / "short"
    shutil.rmtree(tmp, ignore_errors=True)
    tmp.mkdir()
    result = run_output([a.mono, shortener, exe, tmp / exe.name], cwd=tools)
    result.check_returncode()
    if "BRANCH Loop72 br.s" not in result.stdout or "BRANCH Fwd101 ble.s" not in result.stdout:
        raise SystemExit("ShortBranches did not shorten the fixture branches")
    for f in tmp.iterdir():
        f.replace(exe.parent / f.name)
    tmp.rmdir()


def check_il(exe):
    """The fixture's IL keeps the rules IL2CPP needs and Mono's JIT does not check (Tencent/xLua#1064)."""
    result = run_output([a.mono, shortener, "--check", exe], cwd=tools)
    if result.returncode != 0 or "CHECKED " not in result.stdout:
        raise SystemExit("invalid IL in %s (ShortBranches.exe --check)" % exe)


def check_branch_limits(exe):
    """After injection every short branch must fit, and the fixture must have long branches just past the limits."""
    result = run_output([a.mono, shortener, "--list", exe], cwd=tools)
    result.check_returncode()
    found = set()
    for line in result.stdout.splitlines():
        if line.startswith("BRANCH "):
            _, name, opcode, disp = line.split()
            found.add((name.rstrip("0123456789"), opcode, int(disp)))
    problems = ["%s %s %d does not fit" % f for f in sorted(found) if f[1].endswith(".s") and not -128 <= f[2] <= 127]
    wanted = {
        "long backward branch just past -128": lambda f: f[:2] == ("Loop", "br") and -131 <= f[2] <= -129,
        "long forward branch just past +127": lambda f: f[:2] == ("Fwd", "ble") and 128 <= f[2] <= 130,
    }
    problems += ["no " + w for w, test in wanted.items() if not any(test(f) for f in found)]
    if problems:
        raise SystemExit("injected fixture branches: " + "; ".join(problems))


def inject_ok(target, xlua, id_map, *search):
    """Injects target with xlua as xLua's assembly; IntKey must use the target's own slots (no field-mode warning)."""
    result = run_output([a.mono, tools / "XLuaHotfixInject.exe", target, xlua, id_map] + list(search), cwd=target.parent)
    result.check_returncode()
    if "inject finish!" not in result.stdout or "Error:" in result.stdout or "Exception in hotfix inject" in result.stdout:
        raise SystemExit("injection of %s failed" % target.name)
    if "IntKey falls back to field mode" in result.stdout:
        raise SystemExit("IntKey fell back to field mode in %s although it has hotfix bridges" % target.name)
    return result.stdout


# 2. Each kind of debug symbols, on the fixture as mcs builds it with the generated files.
for symbols in SYMBOLS:
    print("== symbols: %s" % symbols, flush=True)
    exe = out / symbols / "HotfixFixture.exe"
    shutil.rmtree(exe.parent, ignore_errors=True)
    if symbols == "pdb":
        built = out / "pdb-mcs/HotfixFixture.exe"
        compile_fixture(built, generated, debug=True)
        exe.parent.mkdir(parents=True)
        run([a.mono, converter, built, exe])
        shutil.copy2(a.plugin, exe.parent / a.plugin.name)
    else:
        compile_fixture(exe, generated, debug=symbols == "mdb")
    shorten(exe)
    if symbols == "unreadable":
        exe.with_suffix(".pdb").write_text("not a pdb\n")
    if symbols == "mdb" and not Path(str(exe) + ".mdb").is_file():
        raise SystemExit("mcs -debug wrote no .mdb")

    if fixture(exe, "baseline") != SYMBOLS[symbols]:
        raise SystemExit("unexpected line numbers before injection")
    check_il(exe)
    id_map = exe.parent / "hotfix_id_map.lua"
    original = exe.read_bytes()
    log = inject(exe, id_map)
    if "inject finish!" not in log or "Error:" in log or "Exception in hotfix inject" in log:
        raise SystemExit("injection failed")
    if (symbols == "unreadable") != ("ignoring debug symbols" in log):
        raise SystemExit("unexpected debug symbol warning")
    if "IntKey falls back to field mode" in log or '["<assembly>"] = "HotfixFixture"' not in id_map.read_text():
        raise SystemExit("IntKey did not use the fixture's own slots (HotfixBridges)")
    injected = exe.read_bytes()
    if injected == original:
        raise SystemExit("injection did not change the assembly")

    log = inject(exe, exe.parent / "second_id_map.lua")
    if "had injected!" not in log or exe.read_bytes() != injected:
        raise SystemExit("second injection was not refused")

    check_il(exe)
    state = fixture(exe, "patch", id_map)
    if symbols == "none":
        # The branch layout check needs the fixture as mcs compiles it without -debug.
        check_branch_limits(exe)
    if state != SYMBOLS[symbols]:
        raise SystemExit("line numbers after injection with %s symbols: %s, expected %s"
                         % (symbols, state, SYMBOLS[symbols]))
    print("HOTFIX_%s_PASS (line numbers %s after injection)" % (symbols.upper(), state), flush=True)

# 3. Paths without a directory part, relative to the working directory. The
# injector threw ArgumentException from Directory.CreateDirectory("") for an
# id map path such as "hotfix_id_map.lua" and left the assembly uninjected.
print("== bare file names", flush=True)
bare = out / "bare"
shutil.rmtree(bare, ignore_errors=True)
exe = bare / "HotfixFixture.exe"
compile_fixture(exe, generated)
result = run_output([a.mono, tools / "XLuaHotfixInject.exe", exe.name, exe.name, "hotfix_id_map.lua"], cwd=bare)
result.check_returncode()
if "inject finish!" not in result.stdout or "Error:" in result.stdout or not (bare / "hotfix_id_map.lua").is_file():
    raise SystemExit("injection with an id map path without a directory part failed")
fixture(exe, "patch", "hotfix_id_map.lua")
print("HOTFIX_BARE_PATHS_PASS", flush=True)

# 4. An assembly the source generator did not compile (HotfixFixtureSecond.cs,
# built by mcs) whose type only a [Hotfix] configuration list names: the injector
# still reads the list, but there are no hotfix bridges for it since the legacy
# generator went (milestone M11), so injection must stop and say what to do.
print("== an assembly without hotfix bridges", flush=True)
nobridges = out / "no-bridges"
shutil.rmtree(nobridges, ignore_errors=True)
exe = nobridges / "HotfixFixture.exe"
compile_fixture(exe, generated)
second = nobridges / "HotfixFixture.Second.dll"
run([a.compiler, "-target:library", "-out:" + str(second), "-r:" + str(exe), repo / "Test/Native/HotfixFixtureSecond.cs"])
result = run_output([a.mono, tools / "XLuaHotfixInject.exe", second, exe, nobridges / "hotfix_id_map_Second.lua", nobridges],
                    cwd=nobridges)
if "configured only by a [Hotfix] configuration list" not in result.stdout or "inject finish!" in result.stdout:
    raise SystemExit("injecting a list-only type without hotfix bridges did not stop with the conversion hint")
print("HOTFIX_NO_BRIDGES_PASS (a list-only type in an assembly the generator did not compile is refused with the hint)",
      flush=True)

# 5. The asmdef layout: xLua alone in XLua.dll, the fixture compiled with the
# generator against it (as Assembly-CSharp or a game asmdef next to the XLua
# asmdef), injected with XLua.dll as xLua's assembly: IntKey through the
# fixture's own slots, the id map names the assembly.
print("== xLua in its own assembly (asmdef layout)", flush=True)
layout = out / "asmdef"
shutil.rmtree(layout, ignore_errors=True)
layout.mkdir(parents=True)
xlua_dll = layout / "XLua.dll"
run([a.compiler, "-target:library", "-unsafe", "-define:" + ";".join(DEFINES), "-out:" + str(xlua_dll)] + xlua_sources())
shutil.copy2(a.plugin, layout / a.plugin.name)
bindings = layout / "HotfixFixture.Game.exe"
builder.compile(bindings, FIXTURE, refs=[xlua_dll], defines=DEFINES)
id_map = layout / "hotfix_id_map.lua"
inject_ok(bindings, xlua_dll, id_map, layout)
if '["<assembly>"] = "HotfixFixture.Game"' not in id_map.read_text() or "HotfixFixture.Keyed" not in id_map.read_text():
    raise SystemExit("the id map of HotfixFixture.Game.exe lacks HotfixFixture.Keyed with its assembly")
fixture(bindings, "patch", id_map)
print("HOTFIX_ASMDEF_LAYOUT_PASS (the fixture next to XLua.dll; IntKey through its own slots)", flush=True)


def source_gen_pass():
    """6. The fixture and a second assembly compiled by csc with the source generator, as Unity compiles them."""
    print("== source generator, Roslyn's IL", flush=True)
    sg = out / "source-gen"
    shutil.rmtree(sg, ignore_errors=True)
    exe = sg / "HotfixFixture.exe"
    builder.compile(exe, xlua_sources() + FIXTURE, defines=DEFINES)
    shutil.copy2(a.plugin, sg / a.plugin.name)
    pristine = sg / "pristine.exe"
    shutil.copy2(exe, pristine)
    if fixture(exe, "baseline") != "missing":
        raise SystemExit("unexpected line numbers before injection")
    id_map = sg / "hotfix_id_map.lua"
    check_il(exe)
    inject_ok(exe, exe, id_map)
    check_il(exe)
    if '["<assembly>"] = "HotfixFixture"' not in id_map.read_text():
        raise SystemExit("the id map of HotfixFixture.exe does not name its assembly: IntKey did not use its own slots")
    fixture(exe, "patch", id_map)
    print("HOTFIX_SOURCE_GEN_PASS (IntKey through the assembly's own slots)", flush=True)

    multi_sg = sg / "multi"
    multi_sg.mkdir()
    exe2 = multi_sg / "HotfixFixture.exe"
    shutil.copy2(pristine, exe2)
    shutil.copy2(a.plugin, multi_sg / a.plugin.name)
    second_sg = multi_sg / "HotfixFixture.SecondSG.dll"
    # C# 12 for the `ref readonly` parameters of RemoteReadonlyArgs*.
    builder.compile(second_sg, [repo / "Test/Native/HotfixFixtureSecondSG.cs"], refs=[exe2], defines=DEFINES, library=True,
                    langversion="12")
    if not list(builder.generated(second_sg).rglob("XLua.HotfixBridges.g.cs")):
        raise SystemExit("the source generator wrote no HotfixBridges for " + second_sg.name)
    maps = []
    for target in [exe2, second_sg]:
        map_path = multi_sg / ("hotfix_id_map_" + target.stem + ".lua")
        inject_ok(target, exe2, map_path, multi_sg)
        maps.append(map_path)
    if '["<assembly>"] = "HotfixFixture.SecondSG"' not in maps[1].read_text() or "HotfixFixture.SecondSG.RemoteKeyed" not in maps[1].read_text():
        raise SystemExit("the id map of HotfixFixture.SecondSG.dll lacks RemoteKeyed with its assembly")
    # [assembly: Hotfix(HotfixFlag.IntKey, typeof(A), typeof(B))]: both types, with the flag.
    for keyed in ["HotfixFixture.SecondSG.ByListKeyed", "HotfixFixture.SecondSG.ByListKeyedToo"]:
        if keyed not in maps[1].read_text():
            raise SystemExit("the id map of HotfixFixture.SecondSG.dll lacks " + keyed + " ([assembly: Hotfix(HotfixFlag.IntKey, ...)])")
    merged_sg = multi_sg / "hotfix_id_map.lua"
    result = run_output([a.mono, tools / "XLuaHotfixInject.exe", "--merge-id-maps", merged_sg] + maps, cwd=multi_sg)
    result.check_returncode()
    if not merged_sg.is_file() or "Warning:" in result.stdout or "Error:" in result.stdout:
        raise SystemExit("--merge-id-maps failed")
    fixture(exe2, "multi-sg", multi_sg, second_sg)
    print("HOTFIX_SOURCE_GEN_MULTI_PASS (a second assembly: its own IntKey slots, Inline and generic methods, [Hotfix] on its types, "
          "[assembly: Hotfix] with one and with several types, merged id maps)",
          flush=True)


source_gen_pass()

# 7. The process-wide IntKey list: xLua in the injected assembly, which has no
# HotfixBridges (mcs, no generator), so the injector gives its IntKey ids to
# the list that HotfixDelegateBridge.Set writes (and on iOS the native flags);
# only Inline types and generic methods inject there. HotfixFixtureShared.cs
# patches through the map's ids and sets ids of the list from four threads.
print("== xLua in the injected assembly without HotfixBridges (the process-wide IntKey list)", flush=True)
shared = out / "shared-list"
shutil.rmtree(shared, ignore_errors=True)
shared.mkdir(parents=True)
exe = shared / "HotfixFixtureShared.exe"
run([a.compiler, "-unsafe", "-define:" + ";".join(DEFINES), "-out:" + str(exe)] + xlua_sources()
    + [repo / "Test/Native/HotfixFixtureShared.cs"])
shutil.copy2(a.plugin, shared / a.plugin.name)


def shared_fixture(*args):
    result = run_output([a.mono, "--debug", exe] + list(args), cwd=shared)
    if result.returncode != 0 or "HOTFIX_SHARED_FIXTURE_PASS" not in result.stdout:
        raise SystemExit("HotfixFixtureShared failed: " + " ".join(str(x) for x in args))


shared_fixture("baseline")
id_map = shared / "hotfix_id_map.lua"
log = inject(exe, id_map)
if "inject finish!" not in log or "Error:" in log or "IntKey falls back to field mode" in log:
    raise SystemExit("injecting HotfixFixtureShared.exe failed, or IntKey fell back to field mode")
if "HotfixShared.Keyed" not in id_map.read_text() or '["<assembly>"]' in id_map.read_text():
    raise SystemExit("HotfixShared.Keyed has no ids of the process-wide list in " + str(id_map))
shared_fixture("patch", id_map)
print("HOTFIX_SHARED_LIST_PASS (Inline IntKey types and a generic method through HotfixDelegateBridge.Set; "
      "Set from four threads)", flush=True)

# 8. The iOS flags behind that list (Assets/Plugins/iOS/HotfixFlags.cpp) under ThreadSanitizer.
run([sys.executable, repo / "Test/Native/hotfix_flags_race.py", "--output", out / "flags-race"])

print("HOTFIX_CHECK_PASS")
