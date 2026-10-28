"""Measure what hotfix costs at run time on the host (Test/Native/HotfixBench.cs).

Builds HotfixBench.exe with the xLua sources and the source generator (the
.NET SDK's csc, -optimize+, as source_gen_build.py compiles), twice:

  off  without HOTFIX_ENABLE: nothing is injected
  on   with HOTFIX_ENABLE, injected by XLuaHotfixInject (Tools/build_tools.py)

Both are release-like (XLUA_NO_BRIDGE_CHECKS: the owner-thread check, which
HOTFIX_ENABLE turns off, would otherwise count against the off build). It
also compiles Assets/Plugins/iOS/HotfixFlags.cpp into a host library for the
check_native case. Then it runs both builds under Mono, --rounds times each,
alternating, and prints the lowest median per case and build:

    hotfix_bench.py --plugin libxlua.dylib --output DIR [--rounds 3] [--scale 1] [--define D]

Writes DIR/hotfix-bench.json. Nothing is asserted beyond the patches running
(HotfixBench.cs throws when a patched method returns the original result).
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo / "Test/Native"))
import source_gen_build  # noqa: E402

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, required=True, help="host-native libxlua")
p.add_argument("--output", type=Path, required=True, help="scratch directory")
p.add_argument("--rounds", type=int, default=3, help="runs of each build, alternating")
p.add_argument("--runs", type=int, default=9, help="timed runs per case in one process")
p.add_argument("--scale", type=float, default=1.0, help="multiply the calls per run")
p.add_argument("--define", action="append", default=[], help="extra C# symbol for both builds (repeatable)")
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs", help="for Tools/build_tools.py")
p.add_argument("--mono-api", type=Path, help="the Mono assemblies csc compiles against")
p.add_argument("--timeout", type=int, default=900, help="seconds per benchmark process")
a = p.parse_args()
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
tools = out / "tools"


def run(cmd, **kw):
    print("+", " ".join(str(c) for c in cmd), flush=True)
    return subprocess.run([str(c) for c in cmd], check=True, **kw)


def xlua_sources():
    project = repo / "General/vs2013/XLua.Mini.csproj"
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]


run([sys.executable, repo / "Tools/build_tools.py", "--output", tools, "--compiler", a.compiler])
builder = source_gen_build.Builder(out / "source-gen-build", a.mono, a.mono_api)
base = ["XLUA_GENERAL", "XLUA_NO_BRIDGE_CHECKS"] + a.define
builds = {"off": base, "on": base + ["HOTFIX_ENABLE"]}
exes = {}
for name, defines in builds.items():
    exe = out / name / "HotfixBench.exe"
    shutil.rmtree(exe.parent, ignore_errors=True)
    builder.compile(exe, xlua_sources() + [repo / "Test/Native/HotfixBench.cs"], defines=defines, extra=["-optimize+"])
    shutil.copy2(a.plugin, exe.parent / a.plugin.name)
    exes[name] = exe
id_map = out / "on" / "hotfix_id_map.lua"
log = subprocess.run([a.mono, tools / "XLuaHotfixInject.exe", exes["on"], exes["on"], id_map], cwd=exes["on"].parent,
                     stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
(out / "inject.log").write_text(log.stdout)
if log.returncode != 0 or "inject finish!" not in log.stdout or "IntKey falls back" in log.stdout:
    print(log.stdout)
    raise SystemExit("injection failed; see " + str(out / "inject.log"))

# check_native: HotfixFlags.cpp as a host library (HotfixBench.cs imports "hotfixflags").
flags_lib = {"darwin": "libhotfixflags.dylib", "linux": "libhotfixflags.so"}.get(sys.platform)
if flags_lib and shutil.which("c++"):
    for exe in exes.values():
        run(["c++", "-O2", "-shared", "-fPIC", "-o", exe.parent / flags_lib, repo / "Assets/Plugins/iOS/HotfixFlags.cpp"])

env = dict(os.environ)
results = {}
meta = {}
for r in range(a.rounds):
    for name, exe in exes.items():
        e = dict(env)
        for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
            e[key] = os.pathsep.join([str(exe.parent), env.get(key, "")])
        cmd = [a.mono, exe, "--scale", str(a.scale), "--runs", str(a.runs)] + (["--id-map", id_map] if name == "on" else [])
        print("+", " ".join(str(c) for c in cmd), flush=True)
        proc = subprocess.run([str(c) for c in cmd], cwd=exe.parent, env=e, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              text=True, timeout=a.timeout)
        (out / ("%s-round%d.log" % (name, r + 1))).write_text(proc.stdout)
        if proc.returncode != 0 or "XLUA_HOTFIX_BENCH_DONE" not in proc.stdout:
            print(proc.stdout[-4000:])
            raise SystemExit("%s round %d failed" % (name, r + 1))
        meta[name] = json.loads(re.search(r"^XLUA_HOTFIX_BENCH_META (.+)$", proc.stdout, re.M).group(1))
        for line in re.findall(r"^XLUA_HOTFIX_BENCH (\{.+\})$", proc.stdout, re.M):
            item = json.loads(line)
            results.setdefault(item["name"], {}).setdefault(name, []).append(item)

table = []
print("\nns per call, lowest median of %d rounds (minimum of all runs in brackets)" % a.rounds)
print("%-18s %18s %18s" % ("case", "off", "on (HOTFIX_ENABLE)"))
for case, by_build in results.items():
    row = {"name": case}
    cells = []
    for name in exes:
        items = by_build.get(name)
        if items:
            row[name] = {"median_ns": min(i["median_ns"] for i in items), "min_ns": min(i["min_ns"] for i in items),
                         "round_medians_ns": [i["median_ns"] for i in items]}
            cells.append("%9.2f [%6.2f]" % (row[name]["median_ns"], row[name]["min_ns"]))
        else:
            cells.append("%18s" % "-")
    table.append(row)
    print("%-18s %s" % (case, " ".join(cells)))
mono = subprocess.run([a.mono, "--version"], stdout=subprocess.PIPE, text=True).stdout.splitlines()[0]
data = {"created": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
        "commit": subprocess.run(["git", "-C", str(repo), "rev-parse", "--short", "HEAD"], stdout=subprocess.PIPE,
                                 text=True).stdout.strip(),
        "host": {"platform": platform.platform(), "machine": platform.machine(), "mono": mono},
        "settings": {"rounds": a.rounds, "runs": a.runs, "scale": a.scale, "defines": builds}, "meta": meta, "results": table}
(out / "hotfix-bench.json").write_text(json.dumps(data, indent=1) + "\n")
print("\nWrote", out / "hotfix-bench.json")
print("XLUA_HOTFIX_BENCH_PASS")
