"""Run AllocProbe.cs and compare its results between two versions of Assets/XLua/Src.

  editor   prepare a macOS Editor project (Test/MobileSmoke/prepare.py layout, AllocProbe.cs as the
           smoke script) from this checkout or from commit --ref, and run SmokeBuild.CheckEditor in
           batch mode --runs times, one Editor at a time under --lock
  sim      install a Simulator .app built by size_report.py (an ios ...+alloc+sim variant) on a booted
           or new iPhone simulator, launch it --runs times and read xlua-alloc-result.json from the
           app's data container
  general  compile AllocProbeGeneral.cs with Assets/XLua/Src (XLUA_GENERAL, from the checkout or --ref)
           and run it on Mono, whose SGen GC counts allocations exactly; class library types stand in
           for the UnityEngine ones
  compare  print a markdown table of two labels: bytes and median milliseconds per step

Results go to WORK/LABEL/run-N.json. Each Editor launch and each app launch is a new process, so
"first in process" rows are cold every run.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import signal
import statistics
import subprocess
import sys
import time

here = Path(__file__).resolve().parent
repo = here.parents[1]
sys.path.insert(0, str(here))
from size_report import Lock, ref_tree  # noqa: E402

MARK = re.compile(r"XLUA_ALLOC_RESULT: (\{.*\})")


def prepare_editor(a, project):
    subprocess.run([sys.executable, str(here / "prepare.py"), "--output", str(project), "--unity-version", a.unity_version,
                    "--macos-plugin", str(a.plugin)], check=True)
    shutil.copy2(here / "AllocProbe.cs", project / "Assets/XluaSmoke.cs")
    if a.ref:
        src = ref_tree(a.work, a.ref) / "Assets/XLua/Src"
        for f in (project / "Assets/XLua").glob("*.cs"):
            f.unlink()
        shutil.rmtree(project / "Assets/XLua/TemplateEngine")
        for f in src.glob("*.cs"):
            shutil.copy2(f, project / "Assets/XLua" / f.name)
        shutil.copytree(src / "TemplateEngine", project / "Assets/XLua/TemplateEngine")


def editor(a):
    out = a.work / a.label
    project = a.work / ("project-" + a.label)
    if not project.is_dir():
        prepare_editor(a, project)
    out.mkdir(parents=True, exist_ok=True)
    for n in range(1, a.runs + 1):
        log = out / ("editor-%d.log" % n)
        cmd = [str(a.unity), "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
               "-executeMethod", "SmokeBuild.CheckEditor", "-logFile", str(log)]
        print("+", " ".join(cmd), flush=True)
        with Lock(a.lock):
            proc = subprocess.Popen(cmd, start_new_session=True)
            try:
                rc = proc.wait(timeout=a.timeout)
            except subprocess.TimeoutExpired:
                os.killpg(proc.pid, signal.SIGKILL)
                proc.wait()
                sys.exit("Editor timed out after %ds; see %s" % (a.timeout, log))
        m = MARK.search(log.read_text(errors="replace"))
        if rc != 0 or not m:
            sys.exit("no XLUA_ALLOC_RESULT (exit %d); see %s" % (rc, log))
        (out / ("run-%d.json" % n)).write_text(m.group(1) + "\n")


def general(a):
    out = a.work / a.label
    out.mkdir(parents=True, exist_ok=True)
    src = (ref_tree(a.work, a.ref) if a.ref else repo) / "Assets/XLua/Src"
    files = [str(p) for p in sorted(src.rglob("*.cs")) if "Editor" not in p.relative_to(src).parts]
    exe = out / "AllocProbeGeneral.exe"
    subprocess.run([a.compiler, "-nologo", "-unsafe", "-define:XLUA_GENERAL", "-nowarn:618,612,414,169,649,219,168,436",
                    "-out:" + str(exe), str(here / "AllocProbeGeneral.cs")] + files, check=True)
    shutil.copy2(a.plugin, out / a.plugin.name)
    env = dict(os.environ, DYLD_LIBRARY_PATH=str(out), LD_LIBRARY_PATH=str(out))
    for n in range(1, a.runs + 1):
        with Lock(a.lock):
            r = subprocess.run([a.mono, str(exe)], cwd=out, env=env, capture_output=True, text=True, timeout=a.timeout)
        (out / ("general-%d.log" % n)).write_text(r.stdout + r.stderr)
        m = MARK.search(r.stdout)
        if r.returncode != 0 or not m:
            sys.exit("no XLUA_ALLOC_RESULT (exit %d); see %s" % (r.returncode, out / ("general-%d.log" % n)))
        (out / ("run-%d.json" % n)).write_text(m.group(1) + "\n")


def simctl(*args, check=True):
    r = subprocess.run(["xcrun", "simctl"] + list(args), capture_output=True, text=True)
    if check and r.returncode:
        sys.exit("simctl %s failed (%d): %s" % (" ".join(args), r.returncode, r.stderr.strip()))
    return r.stdout


def sim(a):
    out = a.work / a.label
    out.mkdir(parents=True, exist_ok=True)
    booted = re.findall(r"\(([0-9A-F-]{36})\) \(Booted\)", simctl("list", "devices", "booted"))
    created = None
    if booted:
        udid = booted[0]
    else:
        runtimes = [r for r in json.loads(simctl("list", "runtimes", "-j"))["runtimes"]
                    if r["platform"] == "iOS" and r["isAvailable"]]
        # The newest iPhone the runtime supports (devicetypes lists phones a runtime may not run).
        device = [t for t in runtimes[-1]["supportedDeviceTypes"] if t["name"].startswith("iPhone")][-1]
        udid = created = simctl("create", "xlua-alloc-probe", device["identifier"], runtimes[-1]["identifier"]).strip()
        simctl("boot", udid)
        simctl("bootstatus", udid, "-b")
    try:
        bundle = "org.xluang.smoke"
        simctl("install", udid, str(a.app))
        for n in range(1, a.runs + 1):
            simctl("terminate", udid, bundle, check=False)
            data = Path(simctl("get_app_container", udid, bundle, "data").strip())
            result = data / "Documents/xlua-alloc-result.json"
            result.unlink(missing_ok=True)
            simctl("launch", udid, bundle)
            deadline = time.time() + a.timeout
            while not result.is_file() and time.time() < deadline:
                time.sleep(2)
            if not result.is_file():
                sys.exit("no %s after %ds" % (result, a.timeout))
            time.sleep(1)
            (out / ("run-%d.json" % n)).write_text(result.read_text() + "\n")
            print("run", n, "ok", flush=True)
        simctl("terminate", udid, bundle, check=False)
    finally:
        if created:
            simctl("shutdown", created, check=False)
            simctl("delete", created, check=False)


def load(work, label):
    runs = [json.loads(p.read_text()) for p in sorted((work / label).glob("run-*.json"))]
    if not runs:
        sys.exit("no runs under " + str(work / label))
    rows = {}
    for run in runs:
        for r in run["rows"]:
            rows.setdefault(r["name"], []).append(r)
    return runs[0]["mode"], len(runs), rows


def compare(a):
    mode_a, na, ra = load(a.work, a.labels[0])
    mode_b, nb, rb = load(a.work, a.labels[1])
    print("%s (%s, %d runs) against %s (%s, %d runs). Bytes: median over the runs without a GC in the step; ms: median over runs.\n" %
          (a.labels[1], mode_b, nb, a.labels[0], mode_a, na))
    print("| step | bytes %s | bytes %s | delta | ms %s | ms %s |" % (a.labels[0], a.labels[1], a.labels[0], a.labels[1]))
    print("| --- | --- | --- | --- | --- | --- |")
    def median_bytes(rows):
        # A row with a collection during the step ("gcs") has no valid byte count.
        valid = [r["bytes"] for r in rows if not r.get("gcs")]
        return int(statistics.median(valid)) if valid else None

    for name, rows in ra.items():
        other = rb.get(name, [])
        ba = median_bytes(rows)
        bb = median_bytes(other) if other else None
        if ba is None:
            print("| %s | (GC ran in every run) | | | | |" % name)
            continue
        ma = statistics.median(r["ms"] for r in rows)
        mb = statistics.median(r["ms"] for r in other) if other else None
        print("| %s | %s | %s | %s | %.2f | %s |" % (name, "{:,}".format(ba), "-" if bb is None else "{:,}".format(bb),
              "-" if bb is None else "{:+,}".format(bb - ba), ma, "-" if mb is None else "%.2f" % mb))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="command", required=True)
    e = sub.add_parser("editor")
    e.add_argument("--plugin", required=True, type=Path, help="macOS arm64 libxlua.dylib (Lua 5.5)")
    e.add_argument("--unity", required=True, type=Path)
    e.add_argument("--unity-version", default="6000.3.21f1")
    e.add_argument("--ref", help="take Assets/XLua/Src from this commit instead of the checkout")
    s = sub.add_parser("sim")
    s.add_argument("--app", required=True, type=Path, help="Simulator .app (size_report.py ios ...+alloc+sim)")
    g = sub.add_parser("general")
    g.add_argument("--plugin", required=True, type=Path, help="host libxlua (Lua 5.5), as for run_managed.py")
    g.add_argument("--ref", help="take Assets/XLua/Src from this commit instead of the checkout")
    g.add_argument("--mono", default="mono")
    g.add_argument("--compiler", default="mcs")
    for sp in (e, s, g):
        sp.add_argument("--work", required=True, type=Path)
        sp.add_argument("--label", required=True)
        sp.add_argument("--runs", type=int, default=3)
        sp.add_argument("--timeout", type=int, default=900)
        sp.add_argument("--lock", type=Path, help="mkdir lock held while the Editor runs")
    c = sub.add_parser("compare")
    c.add_argument("--work", required=True, type=Path)
    c.add_argument("labels", nargs=2, metavar="LABEL")
    a = p.parse_args()
    a.work = a.work.resolve()
    if a.command == "sim":
        with Lock(a.lock):  # a test host, like the Editor
            sim(a)
    else:
        {"editor": editor, "general": general, "compare": compare}[a.command](a)


if __name__ == "__main__":
    main()
