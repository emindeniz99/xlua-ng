"""Hotfix in an Android ARM64 IL2CPP player: the injected IL must convert and run.

Tencent/xLua#1064: IL2CPP failed to compile the C++ it generated from an
injected constructor ("use of undeclared label"). This prepares a project with
prepare.py --editor-tools --hotfix --source-gen (the source generator writes
the hotfix bridges for the [Hotfix] types of HotfixRegions.cs into
Assembly-CSharp; Generate Code, which wrote them in a launch of its own before
milestone M11, is gone), the injector that Tools/build_tools.py built (copied
to <project>/Tools), the Android plugin, the method shapes of
Test/Native/HotfixRegions.cs in Assembly-CSharp and HotfixRegionsProbe in a
scene (Test/UnityMatrix/HotfixRegions). Then one launch:
-buildTarget Android -executeMethod HotfixRegionsSteps.BuildAndroid -quit: a
release APK (with --development a Development one, whose scripts are compiled
without optimizations); xLua's post-build step injects Assembly-CSharp before
IL2CPP converts it. The build log must show the injection and no "undeclared
label" error.

With --check-tool (ShortBranches.exe, which hotfix_check.py builds in
<output>/tools), the injected Assembly-CSharp.dll (the path the injection
logs) is checked with `ShortBranches.exe --check` as hotfix_check.py checks
its fixture, and copied to the --out folder.

--prepare-only stops after preparing the project, and --verify-only runs
only the checks, on the <out>/build.log that HotfixRegionsSteps.BuildAndroid
writes with -xluaBuildLog: unity-matrix.yml launches the editor itself
(GameCI's unity-builder, which holds the licence and sends the editor log to
stdout) between the two, with --platform linux (the Editor plugin is
x86_64/libxlua.so):

    hotfix_il2cpp.py --prepare-only --platform linux --unity-version V --plugin SO --android-plugin SO \
        --project P --out O
    Unity -projectPath P -buildTarget Android -executeMethod HotfixRegionsSteps.BuildAndroid -quit \
        -xluaPlayerOut O/XluaHotfixRegions.apk -xluaBuildLog O/build.log
    hotfix_il2cpp.py --verify-only --project P --out O [--check-tool EXE]

Running the APK is separate: install it, start
org.xluang.hotfixregions/com.unity3d.player.UnityPlayerGameActivity (Unity 6;
UnityPlayerActivity on 2021.3 and 2022.3) and look for
XLUA_HOTFIX_REGIONS_PASS in logcat (Test/MobileSmoke/README.md has the
emulator notes).

Prints XLUA_HOTFIX_IL2CPP_BUILD_PASS or exits with an error.
"""
import argparse
import json
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
p.add_argument("--unity", type=Path, help="Unity editor executable")
p.add_argument("--unity-version")
p.add_argument("--platform", choices=["macos", "linux"], default="macos",
               help="the Editor's platform (prepare.py --platform; default macos)")
p.add_argument("--plugin", type=Path,
               help="rebuilt libxlua for the Editor (code generation): macOS arm64 libxlua.dylib, "
                    "or x86_64 libxlua.so with --platform linux")
p.add_argument("--android-plugin", type=Path, help="rebuilt arm64-v8a libxlua.so")
p.add_argument("--project", type=Path, required=True, help="new project folder (must not exist or be empty)")
p.add_argument("--out", type=Path, required=True, help="logs and the APK")
p.add_argument("--tools", type=Path, default=repo / "Tools", help="folder that build_tools.py wrote")
p.add_argument("--check-tool", type=Path, help="ShortBranches.exe: check the injected Assembly-CSharp.dll")
p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
               help="where Tools/build_source_generator.py builds the source generator (default: <out>/source-generator)")
p.add_argument("--development", action="store_true", help="a Development player")
p.add_argument("--mono", default="mono")
p.add_argument("--timeout", type=int, default=2400, help="seconds per editor launch")
mode = p.add_mutually_exclusive_group()
mode.add_argument("--prepare-only", action="store_true", help="prepare the project and stop")
mode.add_argument("--verify-only", action="store_true",
                  help="check <out>/build.log (-xluaBuildLog) and the APK of a build launched elsewhere")
a = p.parse_args()
a.project, a.out = a.project.resolve(), a.out.resolve()
a.out.mkdir(parents=True, exist_ok=True)
needed = [] if a.verify_only else ["unity_version", "plugin", "android_plugin"] + ([] if a.prepare_only else ["unity"])
missing = ["--" + n.replace("_", "-") for n in needed if getattr(a, n) is None]
if missing:
    p.error("this mode needs " + ", ".join(missing))


def prepare():
    for name in TOOLS:
        if not (a.tools / name).is_file():
            raise SystemExit("missing %s; run python3 Tools/build_tools.py first" % (a.tools / name))
    subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                    "--unity-version", a.unity_version, "--platform", a.platform, "--editor-tools", "--hotfix",
                    "--source-gen", str((a.source_gen or a.out / "source-generator").resolve())], check=True)
    # The Android player's StreamingAssets loader uses UnityEngine.WWW.
    manifest = a.project / "Packages/manifest.json"
    packages = json.loads(manifest.read_text())
    packages["dependencies"]["com.unity.modules.unitywebrequestwww"] = "1.0.0"
    manifest.write_text(json.dumps(packages, indent=2) + "\n")
    (a.project / "Tools").mkdir()
    for name in TOOLS:
        shutil.copy2(a.tools / name, a.project / "Tools" / name)
    android = a.project / "Assets/Plugins/Android/libs/arm64-v8a"
    android.mkdir(parents=True, exist_ok=True)
    shutil.copy2(a.android_plugin, android / "libxlua.so")
    shutil.copy2(repo / "Assets/Plugins/Android/libs/arm64-v8a/libxlua.so.meta", android / "libxlua.so.meta")
    shutil.copy2(repo / "Test/Native/HotfixRegions.cs", a.project / "Assets/HotfixRegions.cs")
    shutil.copy2(here / "HotfixRegions/HotfixRegionsProbe.cs", a.project / "Assets/HotfixRegionsProbe.cs")
    shutil.copy2(here / "HotfixRegions/HotfixRegionsSteps.cs", a.project / "Assets/Editor/HotfixRegionsSteps.cs")


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
        raise SystemExit("XLUA_HOTFIX_IL2CPP_TIMEOUT after %ds: %s (log %s)" % (timeout, name, log))
    print("  exit %d in %.0fs" % (code, time.time() - start), flush=True)
    return code, log.read_text(errors="replace") if log.exists() else ""


def unity(name, args):
    return run(name, [str(a.unity), "-batchmode", "-nographics", "-projectPath", str(a.project),
                      "-logFile", str(a.out / (name + ".log"))] + args, a.timeout)


apk = a.out / "XluaHotfixRegions.apk"
if a.verify_only:
    # The launch that built the APK passed -xluaBuildLog <out>/build.log; its exit status was checked there.
    if not (a.out / "build.log").is_file():
        raise SystemExit("XLUA_HOTFIX_IL2CPP_FAIL: no %s (-xluaBuildLog)" % (a.out / "build.log"))
    code, text = 0, (a.out / "build.log").read_text(errors="replace")
else:
    prepare()
    if a.prepare_only:
        print("XLUA_HOTFIX_IL2CPP_PREPARED: %s" % a.project)
        sys.exit(0)
    code, text = unity("build", ["-buildTarget", "Android", "-executeMethod", "HotfixRegionsSteps.BuildAndroid", "-quit",
                                 "-xluaPlayerOut", str(apk)] + (["-xluaDevelopment"] if a.development else []))
labels = sorted(set(re.findall(r"use of undeclared label '(IL_[0-9a-f]+)'", text)))
for line in text.splitlines():
    if "undeclared label" in line or "inject finish" in line or "had injected" in line:
        print(line.strip(), flush=True)
invalid_il = False
if a.check_tool:
    # The injected script assembly (Temp/StagingArea/Data/Managed, which a
    # successful build removes; then the stripped copy that IL2CPP converted),
    # checked also when IL2CPP failed.
    injected = [Path(m) for m in re.findall(r"^(\S*/Assembly-CSharp\.dll) inject finish!", text, re.M)]
    injected += [a.project / "Library/Bee/artifacts/Android/ManagedStripped/Assembly-CSharp.dll"]
    injected = [f for f in injected if f.is_file()]
    if not injected:
        raise SystemExit("XLUA_HOTFIX_IL2CPP_FAIL: found no injected Assembly-CSharp.dll")
    print("checking " + str(injected[0]), flush=True)
    shutil.copy2(injected[0], a.out / "Assembly-CSharp.injected.dll")
    result = subprocess.run([a.mono, str(a.check_tool), "--check", str(a.out / "Assembly-CSharp.injected.dll")],
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, cwd=a.check_tool.parent)
    (a.out / "check.log").write_text(result.stdout)
    print(result.stdout, flush=True)
    invalid_il = result.returncode != 0
if labels:
    raise SystemExit("XLUA_HOTFIX_IL2CPP_FAIL: IL2CPP's C++ has undeclared labels %s; see %s"
                     % (", ".join(labels), a.out / "build.log"))
if code or "XLUA_HOTFIX_IL2CPP_BUILD: Succeeded" not in text or not apk.is_file():
    raise SystemExit("XLUA_HOTFIX_IL2CPP_FAIL: the build exited %d; see %s" % (code, a.out / "build.log"))
if "inject finish!" not in text:
    raise SystemExit("XLUA_HOTFIX_IL2CPP_FAIL: the build log shows no hotfix injection")
if invalid_il:
    raise SystemExit("XLUA_HOTFIX_IL2CPP_FAIL: the injected Assembly-CSharp.dll has invalid IL; see %s" % (a.out / "check.log"))
built = re.search(r"XLUA_HOTFIX_IL2CPP_BUILD: Succeeded \S+ \((\w+)\)", text)
print("XLUA_HOTFIX_IL2CPP_BUILD_PASS (%s, Android ARM64 IL2CPP, %s, %s)"
      % (a.unity_version or "Unity", built.group(1) if built else "release", apk))
