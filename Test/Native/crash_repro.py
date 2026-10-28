"""Run the crash inventory's reproductions (Test/Native/CrashRepro.cs), each in its own process.

docs/research/crash-inventory.md lists every known way a script or ordinary C#
use can crash the process. The cases here reproduce those that are cheap to
reproduce on a host: each starts `mono CrashRepro.exe <case>` in a new process
group with a timeout, kills the group after it, and prints the exit status
(exit code, signal or TIMEOUT) and the first lines of output. A case that ends
in a managed exception or returns prints REPRO_OK; a crash prints nothing more.
Nothing is asserted: some cases still crash by design (a C# stack overflow;
two threads in one LuaEnv where the bridge checks are off, which this build of
XLUA_GENERAL keeps on), and the inventory records which.

xLua (XLua.Mini.csproj) and CrashRepro.cs are compiled with mcs for XLUA_GENERAL,
so the bridges are emitted or reflected; the generated-code paths are covered by
the managed suite (run_managed.py --source-gen).

    python3 Test/Native/crash_repro.py --plugin /abs/libxlua.dylib --output /abs/dir [case ...]
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True)
p.add_argument("--output", type=Path, required=True)
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--timeout", type=int, default=30, help="seconds per case")
p.add_argument("cases", nargs="*", help="default: every case of CrashRepro.cs")
a = p.parse_args()
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
source = repo / "Test/Native/CrashRepro.cs"
project = repo / "General/vs2013/XLua.Mini.csproj"
xlua = [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
        for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]
compiler = [a.mono, a.compiler] if a.compiler.endswith(".exe") else [a.compiler]
subprocess.run(compiler + ["-target:library", "-unsafe", "-nowarn:108,649", "-define:XLUA_GENERAL",
                           "-out:" + str(out / "XLua.Mini.dll")] + xlua, check=True, stdout=subprocess.DEVNULL)
subprocess.run(compiler + ["-define:XLUA_GENERAL", "-r:" + str(out / "XLua.Mini.dll"), "-out:" + str(out / "CrashRepro.exe"),
                           str(source)], check=True)
shutil.copy2(a.plugin, out / a.plugin.name)
cases = a.cases or re.findall(r'^\s*case "(\w+)":', source.read_text(), re.M)
env = dict(os.environ, DYLD_LIBRARY_PATH=str(out), LD_LIBRARY_PATH=str(out))
for case in cases:
    proc = subprocess.Popen([a.mono, str(out / "CrashRepro.exe"), case], cwd=out, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, errors="backslashreplace", start_new_session=True)
    try:
        text, _ = proc.communicate(timeout=a.timeout)
        code = proc.returncode
        status = "exit %d" % code if code >= 0 else "signal " + signal.Signals(-code).name
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        text, _ = proc.communicate()
        status = "TIMEOUT"
    (out / (case + ".log")).write_text(text)
    lines = text.splitlines()
    ok = [l for l in lines if l.startswith("REPRO_OK")]
    first = [l.strip()[:110] for l in lines if l.strip() and not l.startswith("REPRO_OK") and not l.startswith("=")][:2]
    result = (ok[0][len("REPRO_OK " + case):].strip() or "returned") if ok else "CRASH (no result)"
    print("%-30s %-14s %s | %s" % (case, status, result, " / ".join(first)))
