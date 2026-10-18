"""Hotfix in a macOS player: build-time injection, then patching from Lua.

Prepares a project with prepare.py --editor-tools --hotfix (and --asmdef with
--asmdef, or --package with --package: xLua from the UPM package), the injector that Tools/build_tools.py built (copied to
<project>/Tools) and the fixture in Test/UnityMatrix/HotfixIntKey:
HotfixIntKeyLocal in Assembly-CSharp, Game.Shop in the asmdef Game, their
IntKey [Hotfix] list under an Editor folder, and HotfixPlayerProbe in a scene.
Then, one launch at a time:

1. -executeMethod XluaEditorTestSteps.GenerateCode -quit, and in the Assets
   layout -executeMethod XLua.Hotfix.HotfixInject -quit, which writes the
   IntKey id map into Assets/XLua/Gen/Resources before the build packs it
2. -executeMethod HotfixPlayerSteps.BuildMac -quit: a macOS Mono player (ARM64,
   or --arch); xLua's post-build step injects the player's script assemblies.
   The plugins keep the repository's import settings. The build
   log must show the injection and, for Game.dll (and, in the asmdef layout,
   Assembly-CSharp.dll until milestone M7), the IntKey fallback warning.
3. the player in batch mode: HotfixPlayerProbe patches both Price methods
   from Lua and restores them, and logs XLUA_HOTFIX_PLAYER_PASS. An x64
   player runs under Rosetta (arch -x86_64), a universal one both ways.

--bundle adds a universal xlua.bundle (build/make_osx_lua55.sh) next to the
arm64 dylib, as the release plugins ship, for the x64 and universal players.

--source-gen BUILD_DIR (with --asmdef or --package) installs the source
generator and configures both types as hotfix_intkey.py --source-gen does
(HotfixIntKey/SourceGen): step 1 runs Hotfix Inject In Editor only (for the
id map), no assembly may warn about the IntKey fallback, and the player must
patch both types through their assemblies' own IntKey slots (no __Hotfix
fields, the id map loaded).

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
p.add_argument("--asmdef", action="store_true", help="the opt-in asmdef layout (prepare.py --asmdef)")
p.add_argument("--package", action="store_true",
               help="xLua as the UPM package from a file: tarball (prepare.py --package); injected as in the "
                    "asmdef layout, since xLua is in XLua.dll there too")
p.add_argument("--arch", choices=["ARM64", "x64", "x64ARM64"], default="ARM64", help="player architecture")
p.add_argument("--bundle", type=Path, help="a built xlua.bundle folder to add as Assets/Plugins/xlua.bundle")
p.add_argument("--timeout", type=int, default=1800, help="seconds per editor launch")
p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
               help="with --asmdef or --package: the source generator and the attribute configuration of "
                    "HotfixIntKey/SourceGen; IntKey through each assembly's own slots")
a = p.parse_args()
if a.source_gen and not (a.asmdef or a.package):
    p.error("--source-gen needs --asmdef or --package")
if a.package and (a.asmdef or a.bundle):
    p.error("--package has the asmdefs and takes no --bundle")
# The package compiles xLua into XLua.dll, as the asmdef layout does.
own_assembly = a.asmdef or a.package
a.project, a.out = a.project.resolve(), a.out.resolve()
a.out.mkdir(parents=True, exist_ok=True)
for name in TOOLS:
    if not (a.tools / name).is_file():
        raise SystemExit("missing %s; run python3 Tools/build_tools.py first" % (a.tools / name))

subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools", "--hotfix"]
               + (["--asmdef"] if a.asmdef else []) + (["--package"] if a.package else [])
               + (["--source-gen", str(a.source_gen), "--define", "XLUA_TEST_HOTFIX_SOURCE_GEN"] if a.source_gen else []),
               check=True)
(a.project / "Tools").mkdir()
for name in TOOLS:
    shutil.copy2(a.tools / name, a.project / "Tools" / name)
fixture = here / "HotfixIntKey"
shutil.copytree(fixture / "Game", a.project / "Assets/Game")
shutil.copy2(fixture / "HotfixIntKeyLocal.cs", a.project / "Assets/HotfixIntKeyLocal.cs")
shutil.copy2(fixture / "HotfixPlayerProbe.cs", a.project / "Assets/HotfixPlayerProbe.cs")
if a.source_gen:
    shutil.copy2(fixture / "SourceGen/Game.asmdef", a.project / "Assets/Game/Game.asmdef")
    shutil.copy2(fixture / "SourceGen/GameHotfix.cs", a.project / "Assets/Game/GameHotfix.cs")
    shutil.copy2(fixture / "SourceGen/LocalHotfix.cs", a.project / "Assets/LocalHotfix.cs")
else:
    shutil.copy2(fixture / "HotfixIntKeyConfig.cs", a.project / "Assets/Editor/HotfixIntKeyConfig.cs")
shutil.copy2(fixture / "HotfixPlayerSteps.cs", a.project / "Assets/Editor/HotfixPlayerSteps.cs")
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


# The source generator writes the bridges; Generate Code would register the suite's types twice.
code = 0 if a.source_gen else unity("generate", ["-executeMethod", "XluaEditorTestSteps.GenerateCode", "-quit"])[0]
if code:
    raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: Generate Code exited %d; see %s" % (code, a.out / "generate.log"))

# IntKey ids, from the global list (Assets layout) or the generator's slots.
int_key = not own_assembly or a.source_gen is not None
if int_key:
    # IntKey (Assets layout): the player needs Gen/Resources/hotfix_id_map.lua.txt,
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
expected = [] if a.source_gen else ["Assembly-CSharp.dll", "Game.dll"] if own_assembly else ["Game.dll"]
if fallbacks != expected:
    raise SystemExit("XLUA_HOTFIX_PLAYER_FAIL: IntKey fallback warnings for %s, expected %s" % (fallbacks, expected))

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
                         % (local_fields, "on" if int_key else "off (xLua in XLua.dll)"))
print("XLUA_HOTFIX_PLAYER_ALL_PASS (%s, %s layout, %s player, %s)" % (a.unity_version, "package" if a.package else "asmdef" if a.asmdef else "Assets",
                                                                    a.arch + (" source generator" if a.source_gen else ""),
                                                                    ", ".join(runs)))
