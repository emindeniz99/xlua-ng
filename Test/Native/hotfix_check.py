"""Check hotfix injection end to end with XLuaHotfixInject and Mono.

1. Builds the tools (Tools/build_tools.py) and compiles Test/Native/HotfixFixture.cs
   with the xLua sources (XLUA_GENERAL, HOTFIX_ENABLE).
2. Runs XLuaGenerate on that assembly and recompiles it with the generated
   wrappers and hotfix bridges, which the injector needs.
3. Builds the final assembly with each kind of debug symbols: a portable .pdb
   (what Roslyn and Unity write; converted from mcs output with
   MdbToPortablePdb.cs), the .mdb that Mono's mcs writes for -debug, no
   symbols, and an unreadable .pdb (the injector must then ignore it). For
   each, injects it, checks that a second injection reports "had injected!"
   and leaves the file unchanged, and runs the fixture under `mono --debug`:
   original behaviour before patching, patched behaviour after xlua.hotfix,
   original behaviour again after xlua.hotfix(..., nil), and whether stack
   traces of instrumented methods still have line numbers.
   Before that, ShortBranches.cs gives the fixture's LoopN and FwdN
   constructors short branches, as Roslyn writes them; injection must widen
   those that no longer fit (Tencent/xLua#1003).
4. Injects a fixture given as bare file names, with an id map path that has
   no directory part (the injector used to throw for it), and runs it.
5. Injects two assemblies as the Editor does when several assemblies have
   hotfix types: the fixture, which holds xLua, and HotfixFixture.Second.dll
   (HotfixFixtureSecond.cs), with one id map per assembly, then merges the
   maps into hotfix_id_map with `XLuaHotfixInject --merge-id-maps`. IntKey
   in the second assembly must fall back to field mode with a warning, and
   xlua.util.auto_id_map() must find the fixture's IntKey ids.
6. The asmdef layout: xLua alone in XLua.dll, the generated code with the
   fixture in HotfixFixture.Bindings.exe (as Assets/XLua/Gen in
   Assembly-CSharp) and HotfixFixture.Second.dll injected with XLua.dll as the
   xLua assembly. Neither of the two holds the generated hotfix bridges, so
   the injector must take them from the assembly next to it that does; it
   reported "can not find delegate" before (Unity 2021.3.45f2, 2026-10-18).
7. With the source generator (unless --no-source-gen; docs/design/source-generator.md
   section 6, milestone M7): compiles the fixture with the .NET SDK's csc and the
   generator, which writes its HotfixBridges, and compares their signatures with
   the legacy generator's hotfix bridges for the same fixture (surface_diff.py).
   Injects and runs it: IntKey goes through the fixture's own slots, and the id
   map names the assembly. Then HotfixFixture.SecondSG.dll (HotfixFixtureSecondSG.cs),
   also compiled with the generator, in a second assembly: [Hotfix] on its types
   and at assembly level, IntKey through its own slots without the field-mode
   warning, Inline and a generic method injected, all patched by name after
   auto_id_map() reads the merged map.
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
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="host-native libxlua")
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--define", action="append", default=[],
               help="extra C# symbol for xLua, the tools and the fixture, e.g. XLUA_LOCK_METATABLES (repeatable)")
p.add_argument("--no-source-gen", action="store_true", help="skip step 7 (the source generator's hotfix bridges)")
p.add_argument("--mono-api", type=Path,
               help="the Mono assemblies csc compiles against in step 7 (default: lib/mono/4.5 next to the mono executable)")
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
        + (["-debug"] if debug else []) + xlua_sources() + [repo / "Test/Native/HotfixFixture.cs"]
        + list(extra_sources))
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

# The injector looks for the hotfix bridges that XLuaGenerate writes.
stage1 = out / "stage1/HotfixFixture.exe"
compile_fixture(stage1)
gen = out / "Gen"
shutil.rmtree(gen, ignore_errors=True)
# The legacy generator's bridges (with the hotfix signatures), for step 7.
legacy_surface = out / "legacy-surface.json"
run([a.mono, tools / "XLuaGenerate.exe", stage1, str(gen) + "/"], cwd=tools, env=dict(env, XLUA_GEN_MANIFEST=str(legacy_surface)))
generated = sorted(gen.glob("*.cs"))
if not any(f.name == "DelegatesGensBridge.cs" for f in generated):
    raise SystemExit("XLuaGenerate wrote no DelegatesGensBridge.cs")
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
    id_map = exe.parent / "hotfix_id_map.lua"
    original = exe.read_bytes()
    log = inject(exe, id_map)
    if "inject finish!" not in log or "Error:" in log or "Exception in hotfix inject" in log:
        raise SystemExit("injection failed")
    if (symbols == "unreadable") != ("ignoring debug symbols" in log):
        raise SystemExit("unexpected debug symbol warning")
    injected = exe.read_bytes()
    if injected == original:
        raise SystemExit("injection did not change the assembly")

    log = inject(exe, exe.parent / "second_id_map.lua")
    if "had injected!" not in log or exe.read_bytes() != injected:
        raise SystemExit("second injection was not refused")

    state = fixture(exe, "patch", id_map)
    if symbols == "none":
        # The branch layout check needs the fixture as mcs compiles it without -debug.
        check_branch_limits(exe)
    if state != SYMBOLS[symbols]:
        raise SystemExit("line numbers after injection with %s symbols: %s, expected %s"
                         % (symbols, state, SYMBOLS[symbols]))
    print("HOTFIX_%s_PASS (line numbers %s after injection)" % (symbols.upper(), state), flush=True)

# Paths without a directory part, relative to the working directory. The
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

# Several injected assemblies. With more than one hotfix assembly the Editor
# (Hotfix.HotfixInject(string) in Hotfix.cs) injects each with the assembly
# that holds xLua, writes hotfix_id_map_<assembly>.lua.txt for each and then
# merges them into hotfix_id_map.lua.txt.
print("== several assemblies", flush=True)
multi = out / "multi"
shutil.rmtree(multi, ignore_errors=True)
exe = multi / "HotfixFixture.exe"
compile_fixture(exe, generated)
second = multi / "HotfixFixture.Second.dll"
run([a.compiler, "-target:library", "-out:" + str(second), "-r:" + str(exe),
     repo / "Test/Native/HotfixFixtureSecond.cs"])
if fixture(exe, "baseline") != "missing":
    raise SystemExit("unexpected line numbers before injection")
maps, logs = [], {}
for target in [exe, second]:
    id_map = multi / ("hotfix_id_map_" + target.stem + ".lua")
    result = run_output([a.mono, tools / "XLuaHotfixInject.exe", target, exe, id_map, multi], cwd=multi)
    result.check_returncode()
    if "inject finish!" not in result.stdout or "Error:" in result.stdout or "Exception in hotfix inject" in result.stdout:
        raise SystemExit("injection of %s failed" % target.name)
    maps.append(id_map)
    logs[target] = result.stdout
fallback = "Warning:IntKey falls back to field mode in HotfixFixture.Second.dll for 1 type: HotfixFixture.Second.Remote."
if fallback not in logs[second]:
    raise SystemExit("no IntKey fallback warning for HotfixFixture.Second.dll")
if "Warning:" in logs[exe]:
    raise SystemExit("unexpected warning for HotfixFixture.exe")
merged = multi / "hotfix_id_map.lua"
result = run_output([a.mono, tools / "XLuaHotfixInject.exe", "--merge-id-maps", merged] + maps, cwd=multi)
result.check_returncode()
if not merged.is_file() or "Warning:" in result.stdout or "Error:" in result.stdout:
    raise SystemExit("--merge-id-maps failed")
fixture(exe, "multi", multi, second)
print("HOTFIX_MULTI_PASS (two assemblies, merged id map)", flush=True)

# The asmdef layout: the generated bridges are in a third assembly.
print("== bridges in a third assembly (asmdef layout)", flush=True)
layout = out / "asmdef"
shutil.rmtree(layout, ignore_errors=True)
layout.mkdir(parents=True)
xlua_dll = layout / "XLua.dll"
run([a.compiler, "-target:library", "-unsafe", "-define:" + ";".join(["XLUA_GENERAL", "HOTFIX_ENABLE"] + a.define),
     "-out:" + str(xlua_dll)] + xlua_sources())
shutil.copy2(a.plugin, layout / a.plugin.name)
bindings = layout / "HotfixFixture.Bindings.exe"
run([a.compiler, "-unsafe", "-define:" + ";".join(["XLUA_GENERAL", "HOTFIX_ENABLE"] + a.define), "-out:" + str(bindings),
     "-r:" + str(xlua_dll), repo / "Test/Native/HotfixFixture.cs"] + generated)
second = layout / "HotfixFixture.Second.dll"
run([a.compiler, "-target:library", "-out:" + str(second), "-r:" + str(bindings), "-r:" + str(xlua_dll),
     repo / "Test/Native/HotfixFixtureSecond.cs"])
result = run_output([a.mono, tools / "XLuaHotfixInject.exe", second, xlua_dll, layout / "hotfix_id_map_Second.lua", layout],
                    cwd=layout)
result.check_returncode()
if "inject finish!" not in result.stdout or "Error:" in result.stdout or "Exception in hotfix inject" in result.stdout:
    raise SystemExit("injection of HotfixFixture.Second.dll with the bridges in a third assembly failed")
if "Warning:IntKey falls back to field mode in HotfixFixture.Second.dll" not in result.stdout:
    raise SystemExit("no IntKey fallback warning for HotfixFixture.Second.dll in the asmdef layout")
print("HOTFIX_ASMDEF_LAYOUT_PASS (bridges taken from HotfixFixture.Bindings.exe)", flush=True)


def source_gen_pass():
    """Step 7: the fixture and a second assembly compiled with the source generator."""
    sys.path.insert(0, str(repo / "Tools"))
    import build_source_generator
    analyzer = build_source_generator.build(out / "source-gen-build")
    dotnet = build_source_generator.dotnet_path()
    cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
    api = a.mono_api or Path(shutil.which(a.mono)).resolve().parents[1] / "lib/mono/4.5"
    if not cscs or not (api / "mscorlib.dll").is_file():
        raise SystemExit("step 7 needs the .NET SDK's csc.dll and Mono's lib/mono/4.5 (--mono-api)")
    refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll"]]

    def csc(dest, sources, extra_refs=(), library=False):
        dest.parent.mkdir(parents=True, exist_ok=True)
        generated = dest.parent / (dest.stem + ".generated")
        shutil.rmtree(generated, ignore_errors=True)
        generated.mkdir()
        lines = ["-nologo", "-noconfig", "-nostdlib", "-unsafe", "-langversion:9", "-nowarn:0618,0649,0168,0219,0414,0067,0109,0162,8632",
                 "-define:" + ";".join(["XLUA_GENERAL", "HOTFIX_ENABLE"] + a.define), "-analyzer:" + str(analyzer),
                 "-generatedfilesout:" + str(generated), "-out:" + str(dest)] + (["-target:library"] if library else []) \
            + ["-r:" + str(r) for r in refs + list(extra_refs)] + [str(s) for s in sources]
        rsp = dest.parent / (dest.stem + ".rsp")
        rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines) + "\n")
        result = subprocess.run([dotnet, str(cscs[-1]), "@" + str(rsp)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        (dest.parent / (dest.stem + ".csc.log")).write_text(result.stdout)
        if result.returncode != 0 or re.search(r"warning (CS8032|CS8784|CS8785|CS9057)|(error|warning) XLSG", result.stdout):
            print(result.stdout)
            raise SystemExit("%s does not compile with the source generator, or the generator warned" % dest.name)
        bridges = list(generated.rglob("XLua.HotfixBridges.g.cs"))
        if not bridges:
            raise SystemExit("the source generator wrote no HotfixBridges for " + dest.name)
        return next(generated.rglob("XLua.Manifest.g.cs"))

    def inject_into(target, xlua, id_map, *search):
        result = run_output([a.mono, tools / "XLuaHotfixInject.exe", target, xlua, id_map] + list(search), cwd=target.parent)
        result.check_returncode()
        if "inject finish!" not in result.stdout or "Error:" in result.stdout or "Exception in hotfix inject" in result.stdout:
            raise SystemExit("injection of %s failed" % target.name)
        if "IntKey falls back to field mode" in result.stdout:
            raise SystemExit("IntKey fell back to field mode in %s although it has hotfix bridges" % target.name)
        return result.stdout

    print("== source generator", flush=True)
    sg = out / "source-gen"
    shutil.rmtree(sg, ignore_errors=True)
    exe = sg / "HotfixFixture.exe"
    manifest = csc(exe, xlua_sources() + [repo / "Test/Native/HotfixFixture.cs"])
    shutil.copy2(a.plugin, sg / a.plugin.name)
    # The hotfix bridge sets of both generators for the same configuration.
    diff = run_output([sys.executable, repo / "Test/Native/surface_diff.py", legacy_surface, manifest], cwd=sg)
    if diff.returncode != 0 or "XLUA_HOTFIX_BRIDGE_DIFF_PASS" not in diff.stdout:
        raise SystemExit("the hotfix bridges of the legacy and the source generator differ")
    pristine = sg / "pristine.exe"
    shutil.copy2(exe, pristine)
    if fixture(exe, "baseline") != "missing":
        raise SystemExit("unexpected line numbers before injection")
    id_map = sg / "hotfix_id_map.lua"
    inject_into(exe, exe, id_map)
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
    csc(second_sg, [repo / "Test/Native/HotfixFixtureSecondSG.cs"], [exe2], library=True)
    maps = []
    for target in [exe2, second_sg]:
        map_path = multi_sg / ("hotfix_id_map_" + target.stem + ".lua")
        inject_into(target, exe2, map_path, multi_sg)
        maps.append(map_path)
    if '["<assembly>"] = "HotfixFixture.SecondSG"' not in maps[1].read_text() or "HotfixFixture.SecondSG.RemoteKeyed" not in maps[1].read_text():
        raise SystemExit("the id map of HotfixFixture.SecondSG.dll lacks RemoteKeyed with its assembly")
    merged_sg = multi_sg / "hotfix_id_map.lua"
    result = run_output([a.mono, tools / "XLuaHotfixInject.exe", "--merge-id-maps", merged_sg] + maps, cwd=multi_sg)
    result.check_returncode()
    fixture(exe2, "multi-sg", multi_sg, second_sg)
    print("HOTFIX_SOURCE_GEN_MULTI_PASS (a second assembly: its own IntKey slots, Inline and generic methods, [Hotfix] on its types)",
          flush=True)


if not a.no_source_gen:
    source_gen_pass()

print("HOTFIX_CHECK_PASS")
