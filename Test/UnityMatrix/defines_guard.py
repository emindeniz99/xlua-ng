"""A player build whose defines differ from the Editor's compiled ones fails (BuildDefinesCheck.cs).

    python3 Test/UnityMatrix/defines_guard.py --unity /path/Unity.app/Contents/MacOS/Unity \\
        --unity-version 6000.3.21f1 --plugin BUILD/libxlua.dylib --project P --out O

Prepares a project with prepare.py --platform macos --editor-tools --source-gen
(the asmdef layout; the generator is built into <out>/source-generator), adds
DefinesGuard/XluaDefinesGuardSteps.cs, and runs one editor launch per step.
Each launch runs XluaDefinesGuardSteps.Build, which may set the Player
Settings defines of Standalone and then builds a macOS Mono player in the same
launch:

1. set XLUA_PRESET_MINIMAL;XLUA_REFLECTION and build: must fail with xLua's
   message (the Editor was compiled for legacy);
2. build in the next launch, compiled with them: must succeed;
3. clear the defines and build (minimal to legacy, the Q75 bug of
   docs/design/source-generator.md): must fail;
4. build in the next launch: must succeed, and the generator's link.xml must
   keep more types whole than in step 2 (legacy keeps every [LuaCallCSharp]
   type whole, minimal with XLUA_REFLECTION does not);
5. build with BuildPlayerOptions.extraScriptingDefines HOTFIX_ENABLE: must
   fail and say that extraScriptingDefines cannot add it;
6. set a define xLua's build steps do not read (XLUA_GUARD_UNRELATED) and
   build: must succeed.

Before the check, steps 1 and 3 built players whose link.xml came from the
other preset. One editor at a time; each launch has a timeout that kills the
editor's process group. --keep-library keeps the project's Library folder.
Prints XLUA_DEFINES_GUARD_PASS or exits with an error.
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
MESSAGE = "xLua: this player build's scripting defines differ from the ones the Editor's scripts were compiled with"

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, required=True, help="Unity editor executable")
p.add_argument("--unity-version", required=True)
p.add_argument("--plugin", type=Path, required=True, help="a rebuilt macOS arm64 libxlua.dylib (Lua 5.5.1)")
p.add_argument("--project", type=Path, required=True, help="new project folder (must not exist or be empty)")
p.add_argument("--out", type=Path, required=True, help="logs and players")
p.add_argument("--source-gen", type=Path, metavar="BUILD_DIR",
               help="where Tools/build_source_generator.py builds the source generator (default: <out>/source-generator)")
p.add_argument("--timeout", type=int, default=1800, help="seconds per editor launch")
p.add_argument("--keep-library", action="store_true", help="keep <project>/Library afterwards")
a = p.parse_args()
a.project, a.out = a.project.resolve(), a.out.resolve()
a.source_gen = (a.source_gen or a.out / "source-generator").resolve()
a.out.mkdir(parents=True, exist_ok=True)

subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(a.project), "--plugin", str(a.plugin),
                "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools",
                "--source-gen", str(a.source_gen)], check=True)
shutil.copy2(here / "DefinesGuard/XluaDefinesGuardSteps.cs", a.project / "Assets/Editor/XluaDefinesGuardSteps.cs")


def launch(name, extra):
    log = a.out / (name + ".log")
    app = a.out / (name + ".app")
    cmd = [str(a.unity), "-batchmode", "-nographics", "-projectPath", str(a.project), "-logFile", str(log),
           "-executeMethod", "XluaDefinesGuardSteps.Build", "-quit", "-xluaPlayerOut", str(app)] + extra
    print("+", " ".join(cmd), flush=True)
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True)
    try:
        code = proc.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        sys.exit("XLUA_DEFINES_GUARD_TIMEOUT after %ds in %s (log %s)" % (a.timeout, name, log))
    text = log.read_text(errors="replace") if log.is_file() else ""
    result = re.search(r"XLUA_DEFINES_GUARD_BUILD: (\w+)", text)
    kept = re.search(r"XLUA_DEFINES_GUARD_LINKXML: (\d+) types kept whole, compiled with (.*)", text)
    message = next((l.strip() for l in text.splitlines() if MESSAGE in l), None)
    print("  exit %d in %.0fs: build %s, link.xml %s, compiled with '%s'"
          % (code, time.time() - start, result.group(1) if result else "(none)", kept.group(1) if kept else "?",
             kept.group(2).strip() if kept else "?"), flush=True)
    if message:
        print("  " + message, flush=True)
    shutil.rmtree(app, ignore_errors=True)
    return (result.group(1) if result else None), (int(kept.group(1)) if kept else None), message, log


problems = []


def expect(name, extra, succeed, says=()):
    result, kept, message, log = launch(name, extra)
    if succeed:
        if result != "Succeeded" or message:
            problems.append("%s: expected a build without xLua's defines error, got %s (log %s)" % (name, result, log))
    else:
        if result == "Succeeded" or not message:
            problems.append("%s: expected xLua's defines error, got %s (log %s)" % (name, result, log))
        for part in says:
            if not message or part not in message:
                problems.append("%s: the message does not say '%s' (log %s)" % (name, part, log))
    return kept


expect("1-set-minimal-same-launch", ["-xluaSetDefines", "XLUA_PRESET_MINIMAL;XLUA_REFLECTION"], False,
       ["XLUA_PRESET_MINIMAL added", "XLUA_REFLECTION added"])
minimal = expect("2-minimal-next-launch", [], True)
expect("3-set-legacy-same-launch", ["-xluaSetDefines", "-"], False,
       ["XLUA_PRESET_MINIMAL removed", "XLUA_REFLECTION removed"])
legacy = expect("4-legacy-next-launch", [], True)
expect("5-extra-hotfix", ["-xluaExtraDefines", "HOTFIX_ENABLE"], False,
       ["HOTFIX_ENABLE added", "extraScriptingDefines cannot add HOTFIX_ENABLE"])
expect("6-set-unrelated-same-launch", ["-xluaSetDefines", "XLUA_GUARD_UNRELATED"], True)
if minimal is None or legacy is None or not legacy > minimal:
    problems.append("link.xml: legacy should keep more types whole than minimal with XLUA_REFLECTION (%s, %s)"
                    % (legacy, minimal))
if not a.keep_library:
    shutil.rmtree(a.project / "Library", ignore_errors=True)
if problems:
    sys.exit("XLUA_DEFINES_GUARD_FAIL: " + "; ".join(problems))
print("XLUA_DEFINES_GUARD_PASS: Unity %s refused both same-launch preset switches and the extra HOTFIX_ENABLE, built in "
      "the next launches (link.xml: %d types kept whole in legacy, %d in minimal) and with an unrelated define"
      % (a.unity_version, legacy, minimal))
