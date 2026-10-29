"""Check Assets/Plugins/iOS/HotfixFlags.cpp, the IntKey flags of iOS players, under ThreadSanitizer.

Links HotfixFlags.cpp with Test/Native/HotfixFlagsRace.cpp (three threads read
flags while two threads grow the array and set them) with -fsanitize=thread
and runs it: any ThreadSanitizer report, a lost flag or a crash fails. Before
2026-10-29 the array grew with realloc while other threads read it.

Where the compiler cannot build or start a ThreadSanitizer program (no
runtime, or a kernel memory layout the runtime rejects), the same stress test
runs without the sanitizer, which still catches lost flags and most crashes,
and the script says so.

    hotfix_flags_race.py --output DIR [--cxx c++] [--ids 20000] [--timeout 300]

hotfix_check.py runs it as its last step.
"""
import argparse
import os
from pathlib import Path
import signal
import subprocess
import sys

repo = Path(__file__).resolve().parents[2]
SOURCES = [repo / "Assets/Plugins/iOS/HotfixFlags.cpp", repo / "Test/Native/HotfixFlagsRace.cpp"]

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--cxx", default=os.environ.get("CXX") or "c++", help="C++ compiler (default: $CXX or c++)")
p.add_argument("--ids", type=int, default=20000, help="flags the two writers set")
p.add_argument("--timeout", type=int, default=300, help="seconds per compile and per run")
a = p.parse_args()
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)


def run(cmd, env=None):
    """Run in its own process group; on a timeout the group is killed and the run fails."""
    print("+", " ".join(str(c) for c in cmd), flush=True)
    proc = subprocess.Popen([str(c) for c in cmd], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                            errors="replace", start_new_session=True, env=env)
    try:
        text, _ = proc.communicate(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        text, _ = proc.communicate()
        print(text, flush=True)
        raise SystemExit("XLUA_HOTFIX_FLAGS_RACE_FAIL: %s did not finish in %d s" % (Path(str(cmd[0])).name, a.timeout))
    print(text, flush=True)
    return proc.returncode, text


def build(name, flags):
    exe = out / name
    code, text = run([a.cxx, "-std=c++17", "-g", "-pthread"] + flags + ["-o", exe] + SOURCES)
    return exe if code == 0 else None


tsan = build("HotfixFlagsRace-tsan", ["-O1", "-fsanitize=thread"])
mode = "ThreadSanitizer"
if tsan is not None:
    env = dict(os.environ, TSAN_OPTIONS="halt_on_error=1:exitcode=66:second_deadlock_stack=1")
    code, text = run([tsan, a.ids], env)
    if "FATAL: ThreadSanitizer" in text and "WARNING: ThreadSanitizer" not in text:
        print("XLUA_HOTFIX_FLAGS_RACE_NOTE: the ThreadSanitizer runtime did not start here; running without it", flush=True)
        tsan = None
if tsan is None:
    mode = "no sanitizer"
    plain = build("HotfixFlagsRace", ["-O2"])
    if plain is None:
        raise SystemExit("XLUA_HOTFIX_FLAGS_RACE_FAIL: %s could not build the test" % a.cxx)
    code, text = run([plain, a.ids])
if code != 0 or "HOTFIX_FLAGS_RACE_PASS" not in text or "WARNING: ThreadSanitizer" in text:
    raise SystemExit("XLUA_HOTFIX_FLAGS_RACE_FAIL (%s, exit %d)" % (mode, code))
print("XLUA_HOTFIX_FLAGS_RACE_PASS (%s)" % mode, flush=True)
