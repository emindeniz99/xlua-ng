"""Hotfix in a macOS player: build-time injection, then patching from Lua.

Prepares a project with prepare.py --editor-tools --hotfix --source-gen (the
asmdef layout; --no-asmdef with --no-asmdef, or --package with --package: xLua
from the UPM package), the injector that Tools/build_tools.py built (copied to
<project>/Tools), the fixture in Test/UnityMatrix/HotfixIntKey configured as
hotfix_intkey.py configures it, and HotfixPlayerProbe in a scene. Then, one
launch at a time:

1. -executeMethod XLua.Hotfix.HotfixInject -quit (Hotfix Inject In Editor),
   which writes the IntKey id map into Assets/XLua/Gen/Resources before the
   build packs it
2. -executeMethod HotfixPlayerSteps.BuildMac -quit: a macOS Mono player (ARM64,
   or --arch); xLua's post-build step injects the player's script assemblies
   through the source generator's HotfixBridges. The plugins keep the
   repository's import settings. The build log must show no IntKey fallback
   warning.
3. the player in batch mode: HotfixPlayerProbe patches both Price methods
   from Lua and restores them, and logs XLUA_HOTFIX_PLAYER_PASS: both types
   through their assemblies' own IntKey slots (no __Hotfix fields, the id map
   loaded), or without asmdefs Game.Shop in field mode (HotfixFlag.Inline).
   In the asmdef and package layouts it also patches and restores Game's
   [Hotfix] type, Inline type and generic method (Game/GameShapes.cs,
   HotfixShapes.cs), which upstream's injector skipped outside xLua's
   assembly. An
   x64 player runs under Rosetta (arch -x86_64), a universal one both ways.

--no-assembly-csharp (asmdef or package layout) moves every script that
Assembly-CSharp would compile (the fixture's HotfixIntKeyLocal, the probe and
prepare.py's own) into an asmdef Main, so the player has no
Assembly-CSharp.dll: the post-build step threw there before 2026-10-18
(it looked for Assembly-CSharp.dll among the player's script assemblies).

--bundle adds a universal xlua.bundle (build/make_osx_lua55.sh) next to the
arm64 dylib, as the release plugins ship, for the x64 and universal players.

Before milestone M11 step 1 also ran Generate Code, whose bridges the fixture's
Editor list used, and Game.dll fell back to field mode with a warning.

Prints XLUA_HOTFIX_PLAYER_ALL_PASS or exits with an error.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time

here = Path(__file__).resolve().parent
repo = here.parents[1]
TOOLS = ["XLuaHotfixInject.exe", "XLuaHotfixInject.manifest", "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll",
         "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll"]

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, required=True, help="Unity editor executable")
p.add_argument("--unity-version", required=True)
p.add_argument("--plugin", type=Path, required=True, help="rebuilt macOS arm64 libxlua.dylib")
p.add_argument("--project", type=Path, required=True, help="new project folder (must not exist or be empty)")
p.add_argument("--out", type=Path, required=True, help="logs and the player")
p.add_argument("--tools", type=Path, default=repo / "Tools", help="folder that build_tools.py wrote")
p.add_argument("--no-asmdef", action="store_true",
               help="the layout without asmdefs (prepare.py --no-asmdef): xLua in Assembly-CSharp")
p.add_argument("--asmdef", action="store_true", help="the asmdef layout, the default; accepted for older command lines")
p.add_argument("--package", action="store_true",
               help="xLua as the UPM package from a file: tarball (prepare.py --package); injected as in the "
                    "asmdef layout, since xLua is in XLua.dll there too")
p.add_argument("--no-assembly-csharp", action="store_true",
               help="move the scripts outside asmdefs into an asmdef Main, so that there is no Assembly-CSharp "
                    "(asmdef or package layout)")
p.add_argument("--arch", choices=["ARM64", "x64", "x64ARM64"], default="ARM64", help="player architecture")
p.add_argument("--bundle", type=Path, help="a built xlua.bundle folder to add as Assets/Plugins/xlua.bundle")
p.add_argument("--timeout", type=int, default=1800, help="seconds per editor launch")
p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
               help="where Tools/build_source_generator.py builds the source generator (default: <out>/source-generator)")
a = p.parse_args()
if a.package and (a.asmdef or a.no_asmdef or a.bundle):
    p.error("--package has the asmdefs and takes no --asmdef, --no-asmdef or --bundle")
if a.no_assembly_csharp and a.no_asmdef:
    p.error("--no-assembly-csharp needs xLua in its own asmdefs; it takes no --no-asmdef")
layout = "package" if a.package else "Assets" if a.no_asmdef else "asmdef"
a.project, a.out = a.project.resolve(), a.out.resolve()
a.source_gen = (a.source_gen or a.out / "source-generator").resolve()
a.out.mkdir(parents=True, exist_ok=True)
for name in TOOLS:
    if not (a.tools / name).is_file():
        raise SystemExit("missing %s; run python3 Tools/build_tools.py first" % (a.tools / name))

subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools", "--hotfix"]
               + ["--source-gen", str(a.source_gen)]
               + (["--no-asmdef", "--define", "XLUA_TEST_HOTFIX_GAME_INLINE"] if a.no_asmdef else [])
               + (["--package"] if a.package else []), check=True)
(a.project / "Tools").mkdir()
for name in TOOLS:
    shutil.copy2(a.tools / name, a.project / "Tools" / name)
fixture = here / "HotfixIntKey"
(a.project / "Assets/Game").mkdir()
shutil.copy2(fixture / "Game/Shop.cs", a.project / "Assets/Game/Shop.cs")
if a.no_asmdef:
    shutil.copy2(fixture / "NoAsmdef/Game.asmdef", a.project / "Assets/Game/Game.asmdef")
    shutil.copy2(fixture / "NoAsmdef/HotfixIntKeyInline.cs", a.project / "Assets/Editor/HotfixIntKeyInline.cs")
else:
    shutil.copy2(fixture / "Game/Game.asmdef", a.project / "Assets/Game/Game.asmdef")
    shutil.copy2(fixture / "Game/GameHotfix.cs", a.project / "Assets/Game/GameHotfix.cs")
    shutil.copy2(fixture / "Game/GameShapes.cs", a.project / "Assets/Game/GameShapes.cs")
shutil.copy2(fixture / "HotfixIntKeyLocal.cs", a.project / "Assets/HotfixIntKeyLocal.cs")
shutil.copy2(fixture / "LocalHotfix.cs", a.project / "Assets/LocalHotfix.cs")
shutil.copy2(fixture / "HotfixShapes.cs", a.project / "Assets/HotfixShapes.cs")
shutil.copy2(fixture / "HotfixPlayerProbe.cs", a.project / "Assets/HotfixPlayerProbe.cs")
shutil.copy2(fixture / "HotfixPlayerSteps.cs", a.project / "Assets/Editor/HotfixPlayerSteps.cs")
if a.no_assembly_csharp:
    # Every runtime script directly in Assets goes to Main, which references what
    # Assembly-CSharp referenced; the Editor folders stay in Assembly-CSharp-Editor,
    # which references every auto-referenced asmdef.
    main = a.project / "Assets/Main"
    main.mkdir()
    for script in sorted((a.project / "Assets").glob("*.cs")):
        shutil.move(str(script), str(main / script.name))
    references = ["XLua", "XLua.Core", "Game"] + [d.stem for d in (a.project / "Assets").glob("*/*.asmdef")
                                                   if d.stem not in ("Game", "Main")]
    (main / "Main.asmdef").write_text('{\n    "name": "Main",\n    "references": [%s]\n}\n'
                                      % ", ".join('"%s"' % r for r in references))
    # A script outside every asmdef and every Editor folder would still make an Assembly-CSharp.
    assets = a.project / "Assets"

    def in_asmdef(script):
        return any(list(folder.glob("*.asmdef")) for folder in script.parents if folder == assets or assets in folder.parents)

    leftovers = [str(f.relative_to(a.project)) for f in assets.rglob("*.cs")
                 if "Editor" not in f.relative_to(assets).parts and not in_asmdef(f)]
    if leftovers:
        raise SystemExit("scripts left for Assembly-CSharp: %s" % leftovers)
if a.bundle:
    # The repository's bundle folder (Info.plist and the .meta files), then the binary.
    shutil.copytree(repo / "Assets/Plugins/xlua.bundle", a.project / "Assets/Plugins/xlua.bundle")
    shutil.copy2(repo / "Assets/Plugins/xlua.bundle.meta", a.project / "Assets/Plugins/xlua.bundle.meta")
    shutil.copy2(a.bundle / "Contents/MacOS/xlua", a.project / "Assets/Plugins/xlua.bundle/Contents/MacOS/xlua")


def run(name, cmd, timeout):
    log = a.out / (name + ".log")
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True)
    try:
        code = proc.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        raise SystemExit("XLUA_HOTFIX_PLAYER_TIMEOUT after %ds: %s (log %s)" % (timeout, name, log))
    print("  exit %d in %.0fs" % (code, time.time() - start), flush=True)
    return code, log.read_text(errors="replace") if log.exists() else ""


def unity(name, args):
    return run(name, [str(a.unity), "-batchmode", "-nographics", "-projectPath", str(a.project),
                      "-logFile", str(a.out / (name + ".log"))] + args, a.timeout)


# IntKey ids for HotfixIntKeyLocal, through Assembly-CSharp's own slots.
int_key = True
if int_key:
    # IntKey: the player needs Gen/Resources/hotfix_id_map.lua.txt,
    # and the injection during the build writes it after the build has taken
    # the resources. Hotfix Inject In Editor writes it first.
    code, _ = unity("inject", ["-executeMethod", "XLua.Hotfix.HotfixInject", "-quit"])
    if code or not (a.project / "Assets/XLua/Gen/Resources/hotfix_id_map.lua.txt").is_file():
        raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: Hotfix Inject In Editor exited %d or wrote no id map" % code)

app = a.out / "XluaHotfixPlayer.app"
code, text = unity("build", ["-executeMethod", "HotfixPlayerSteps.BuildMac", "-quit", "-xluaPlayerOut", str(app),
                             "-xluaPlayerArch", a.arch])
print("\n".join(l for l in text.splitlines() if l.startswith(("XLUA_HOTFIX_PLAYER_ARCH", "XLUA_HOTFIX_PLAYER_PLUGIN_CPU",
                                                             "XLUA_HOTFIX_PLAYER_BUNDLE_CPU"))), flush=True)
fallbacks = sorted(set(re.findall(r"IntKey falls back to field mode in (\S+) for", text)))
print("IntKey fallback warnings for: %s" % (", ".join(fallbacks) or "(none)"), flush=True)
if code or "XLUA_HOTFIX_PLAYER_BUILD: Succeeded" not in text:
    raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: the build exited %d; see %s" % (code, a.out / "build.log"))
if fallbacks:
    raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: IntKey fallback warnings for %s, expected none" % fallbacks)

managed = app / "Contents/Resources/Data/Managed"
if a.no_assembly_csharp:
    if (managed / "Assembly-CSharp.dll").exists() or not (managed / "Main.dll").is_file():
        raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: expected Main.dll and no Assembly-CSharp.dll in %s" % managed)
    print("player script assemblies: Main.dll, no Assembly-CSharp.dll", flush=True)
binary = app / "Contents/MacOS/XluaHotfixPlayer"
plugins = sorted(str(f.relative_to(app / "Contents")) for f in (app / "Contents/PlugIns").rglob("*")
                 if f.is_file()) if (app / "Contents/PlugIns").is_dir() else []
print("plugins in the player: %s" % (", ".join(plugins) or "(none)"), flush=True)
runs = {"ARM64": ["arm64"], "x64": ["x86_64"], "x64ARM64": ["arm64", "x86_64"]}[a.arch]
for cpu in runs:
    name = "player" if cpu == "arm64" else "player-x86_64"
    prefix = [] if cpu == "arm64" else ["arch", "-x86_64"]
    code, text = run(name, prefix + [str(binary), "-batchmode", "-nographics", "-logFile", str(a.out / (name + ".log"))], 300)
    lines = [l for l in text.splitlines() if l.startswith("XLUA_HOTFIX_PLAYER_")]
    print("\n".join(lines), flush=True)
    if code or not any(l.startswith("XLUA_HOTFIX_PLAYER_PASS") for l in lines):
        raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: the %s player exited %d; see %s" % (cpu, code, a.out / (name + ".log")))
    local_fields = int(re.search(r"fields (\d+),", [l for l in lines if l.startswith("XLUA_HOTFIX_PLAYER_PASS")][0]).group(1))
    if (local_fields > 0) == int_key:
        raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: HotfixIntKeyLocal has %d __Hotfix fields; IntKey expected %s"
                         % (local_fields, "on" if int_key else "off"))
print("XLUA_HOTFIX_PLAYER_ALL_PASS (%s, %s layout, %s player, %s)" % (a.unity_version, layout,
                                                                    a.arch + " source generator"
                                                                    + (", Game.Shop Inline" if a.no_asmdef else "")
                                                                    + (", no Assembly-CSharp" if a.no_assembly_csharp else ""),
                                                                    ", ".join(runs)))
