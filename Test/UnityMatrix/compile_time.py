"""Unity script compile times: the source generator against legacy generated code.

Makes two projects with prepare.py --editor-tools --asmdef (without the
editor tests and their configuration), one with the source generator in
Assets/XLua/Src/Analyzers, and binds the first N UnityEngine types of the
"Lua-only scripting" list in Assets/XLua/Editor/ExampleConfig.cs
(XluaCompileTiming.cs writes it in the editor):

- legacy: a [LuaCallCSharp] list under Assets/Editor, then Generate Code;
  Assets/XLua/Gen compiles into Assembly-CSharp;
- sourcegen-asmdef: [assembly: LuaCallCSharp(typeof(...))] in an asmdef
  XLuaBindings that references XLua (decision 4 of the design);
- sourcegen-csharp: the same file in Assembly-CSharp.

Each measurement edits one line of a game script in Assembly-CSharp
(Assets/TimingGame/TimingProbe.cs), or of the bindings file, and starts the
editor in batch mode with -quit, which compiles at startup. The time of each
assembly is the duration of its Csc node in Unity's Bee build trace
(Library/Bee/*.traceevents): compiler and generators together, without the
domain reload. Bee reports these in steps of about one second, so each
measured assembly is also compiled again outside the editor ("replay"):
the command of its Csc node in Library/Bee/*.dag.json, the editor's own
compiler with its compiler server (/shared) and its response file, with the
output redirected, --replays times; the median of all but the first run is
the replay time. Launches run one at a time; prints a JSON summary.
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

p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--unity", type=Path, required=True, help="Unity editor executable")
p.add_argument("--unity-version", required=True)
p.add_argument("--plugin", type=Path, required=True, help="rebuilt macOS libxlua.dylib")
p.add_argument("--work", type=Path, required=True, help="new folder for the projects, logs and results")
p.add_argument("--source-gen-build", type=Path, required=True, help="build folder for Tools/build_source_generator.py")
p.add_argument("--counts", nargs="+", default=["100", "500", "all"], help="numbers of types, or all")
p.add_argument("--edits", type=int, default=3, help="one-line edits (compiles) per measurement")
p.add_argument("--wide", action="store_true",
               help="every namespace under UnityEngine, not only ExampleConfig's UnityEngine namespace "
                    "(the exclude list still applies)")
p.add_argument("--timeout", type=int, default=1500, help="seconds per editor launch")
p.add_argument("--exclude", action="append", default=[],
               help="leave a type out of the list (repeatable), such as UnityEngine.PhysicsScene2D, whose "
                    "wrappers do not compile in Unity 6000.6 with either generator (CS0121)")
p.add_argument("--replays", type=int, default=6, help="compiles of each measured assembly outside the editor")
a = p.parse_args()
a.work = a.work.resolve()
if a.work.exists() and any(a.work.iterdir()):
    p.error("--work must be empty or missing")
a.work.mkdir(parents=True, exist_ok=True)
logs = a.work / "logs"
logs.mkdir()
launches = [0]


def unity(project, name, args):
    """One batch-mode launch; returns (exit code, start time, log text)."""
    launches[0] += 1
    log = logs / ("%03d-%s-%s.log" % (launches[0], project.name, name))
    cmd = [str(a.unity), "-batchmode", "-nographics", "-projectPath", str(project), "-logFile", str(log)] + args
    start = time.time()
    proc = subprocess.Popen(cmd, start_new_session=True)
    try:
        code = proc.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid, signal.SIGKILL)
        proc.wait()
        raise SystemExit("XLUA_COMPILE_TIME_TIMEOUT after %ds: %s (log %s)" % (a.timeout, name, log))
    print("  %s %s: exit %d in %.0fs" % (project.name, name, code, time.time() - start), flush=True)
    return code, start, log.read_text(errors="replace") if log.exists() else ""


def csc_times(project, since):
    """Csc node durations (ms) per assembly from the Bee traces written since `since`."""
    times = {}
    for trace in (project / "Library/Bee").glob("*.traceevents"):
        if trace.stat().st_mtime < since - 1:
            continue
        text = trace.read_text(errors="replace").strip().rstrip(",")
        try:
            events = json.loads("[" + text + "]")
        except ValueError:
            events = [json.loads(line.lstrip(",")) for line in text.splitlines() if line.strip().lstrip(",").startswith("{")]
        for e in events:
            if e.get("name") != "Csc" or e.get("ts", 0) / 1e6 < since - 1:
                continue
            m = re.search(r"([^/]+)\.dll", e.get("args", {}).get("detail", ""))
            if m:
                times[m.group(1)] = times.get(m.group(1), 0) + e["dur"] / 1000.0
    return times


def replay(project, assembly):
    """Compile `assembly` again with the editor's Csc command; returns ms per run."""
    dags = sorted((project / "Library/Bee").glob("*.dag.json"), key=lambda f: f.stat().st_mtime)
    action = None
    for node in json.loads(dags[-1].read_text()).get("Nodes", []):
        if re.match(r"Csc \S*/%s\.dll\b" % re.escape(assembly), node.get("Annotation", "")):
            action = node.get("Action")
    if action is None:
        return {"error": "no Csc node for " + assembly}
    m = re.search(r'"@([^"]+\.rsp)"', action)
    out = a.work / "replay"
    out.mkdir(exist_ok=True)
    lines = []
    for line in (project / m.group(1)).read_text().splitlines():
        if re.match(r"[-/](out|refout|pdb|doc|errorlog|generatedfilesout):", line):
            continue
        lines.append(line)
    lines.append('-out:"%s"' % (out / (assembly + ".dll")))

    def timed(rsp_lines, name):
        rsp = out / (name + ".rsp")
        rsp.write_text("\n".join(rsp_lines) + "\n")
        cmd = action.replace('"@' + m.group(1) + '"', '"@' + str(rsp) + '"')
        times = []
        for i in range(a.replays):
            start = time.time()
            r = subprocess.run(cmd, shell=True, cwd=str(project), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            times.append((time.time() - start) * 1000)
            if r.returncode:
                return None, "exit %d: %s" % (r.returncode, r.stdout[-2000:])
        return times, None

    times, error = timed(lines, assembly)
    if error:
        return {"error": error}
    warm = times[1:] or times
    result = {"runs_ms": [round(t) for t in times], "median_ms": round(statistics.median(warm))}
    # The same without the xLua generator: what the generator itself costs.
    plain = [l for l in lines if not ("-analyzer:" in l and "XLua.SourceGenerator" in l)]
    if len(plain) != len(lines):
        times, error = timed(plain, assembly + "-nogen")
        if not error:
            result["without_generator_median_ms"] = round(statistics.median(times[1:] or times))
    return result


def compile_once(project, name):
    code, start, text = unity(project, name, ["-quit"])
    errors = sorted(set(l.strip() for l in text.splitlines() if re.search(r"error CS\d+|error XLSG\d+", l)))
    return {"exit": code, "csc_ms": csc_times(project, start), "errors": errors[:10], "error_count": len(errors)}


stamp = [0]


def edit_probe(project):
    stamp[0] += 1
    (project / "Assets/TimingGame/TimingProbe.cs").write_text(
        "public static class TimingProbe\n{\n    public const int Stamp = %d;\n}\n" % stamp[0])


def measure_edits(project, name, edit, replay_assembly=None):
    runs = []
    for i in range(a.edits):
        edit(project)
        runs.append(compile_once(project, "%s-edit%d" % (name, i + 1)))
    result = {"runs": runs}
    assemblies = set(k for r in runs for k in r["csc_ms"])
    result["median_ms"] = {k: statistics.median([r["csc_ms"].get(k, 0) for r in runs]) for k in sorted(assemblies)}
    if replay_assembly:
        result["replay"] = {replay_assembly: replay(project, replay_assembly)}
    print("  %s: %s %s" % (name, json.dumps(result["median_ms"]), json.dumps(result.get("replay", {}))), flush=True)
    return result


MODULES = ["ai", "animation", "assetbundle", "audio", "director", "imageconversion", "imgui", "particlesystem",
           "physics", "physics2d", "terrain", "tilemap", "ui", "uielements", "unitywebrequest", "video"]


def make_project(path, source_gen):
    args = [sys.executable, str(here / "prepare.py"), "--output", str(path), "--plugin", str(a.plugin),
            "--unity-version", a.unity_version, "--platform", "macos", "--editor-tools", "--asmdef"]
    subprocess.run(args, check=True)
    # No editor tests and no test configuration: only xLua, the steps and the probe.
    shutil.rmtree(path / "Assets/Tests")
    (path / "Assets/XluaEditorTestTarget.cs").unlink()
    shutil.copy2(here / "XluaCompileTiming.cs", path / "Assets/Editor/XluaCompileTiming.cs")
    # The built-in modules a game typically enables, so their types are in the list.
    manifest = json.loads((path / "Packages/manifest.json").read_text())
    for module in MODULES:
        manifest["dependencies"]["com.unity.modules." + module] = "1.0.0"
    (path / "Packages/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (path / "Assets/TimingGame").mkdir()
    edit_probe(path)
    if source_gen:
        subprocess.run([sys.executable, str(repo / "Tools/build_source_generator.py"), "--output",
                        str(a.source_gen_build.resolve()), "--install", str(path / "Assets/XLua/Src/Analyzers")], check=True)


def bindings_source(types, stamp_line=0):
    return ("// compile_time.py: %d UnityEngine types (edit %d)\n" % (len(types), stamp_line)
            + "".join("[assembly: XLua.LuaCallCSharp(typeof(%s))]\n" % t for t in types))


legacy, sg = a.work / "legacy", a.work / "sourcegen"
make_project(legacy, False)
code, _, text = unity(legacy, "types", ["-executeMethod", "XluaCompileTiming.WriteTypeList", "-quit",
                                        "-xluaTypesOut", str(a.work / "types.txt")] + (["-xluaWide"] if a.wide else []))
if code:
    raise SystemExit("XLUA_COMPILE_TIME_FAIL: WriteTypeList exited %d" % code)
all_types = [t for t in (a.work / "types.txt").read_text().split() if t not in a.exclude]
print("UnityEngine types: %d" % len(all_types), flush=True)
make_project(sg, True)
compile_once(sg, "first")

summary = {"unity": a.unity_version, "types_available": len(all_types), "wide": a.wide, "excluded": a.exclude, "edits": a.edits,
           "baseline": {"legacy-project": measure_edits(legacy, "baseline", edit_probe, "Assembly-CSharp"),
                        "sourcegen-project": measure_edits(sg, "baseline", edit_probe, "Assembly-CSharp")},
           "counts": {}}
for count in a.counts:
    types = all_types if count == "all" else all_types[:int(count)]
    entry = summary["counts"][str(len(types))] = {}
    print("== %d types" % len(types), flush=True)

    # Legacy: list under Editor, Generate Code, then edits of the game script.
    config = legacy / "Assets/Editor/XluaTimingConfig.cs"
    config.write_text("using System;\nusing System.Collections.Generic;\nusing XLua;\n\n"
                      "public static class XluaTimingConfig\n{\n    [LuaCallCSharp]\n"
                      "    public static List<Type> Types = new List<Type>()\n    {\n"
                      + "".join("        typeof(%s),\n" % t for t in types) + "    };\n}\n")
    code, _, text = unity(legacy, "generate-%d" % len(types), ["-executeMethod", "XluaEditorTestSteps.GenerateCode", "-quit"])
    gen = legacy / "Assets/XLua/Gen"
    lines = sum(len(f.read_text(errors="replace").splitlines()) for f in gen.rglob("*.cs")) if gen.exists() else 0
    entry["legacy"] = {"generate_exit": code, "generated_lines": lines,
                       "edit": measure_edits(legacy, "legacy-%d" % len(types), edit_probe, "Assembly-CSharp")}
    unity(legacy, "clear-%d" % len(types), ["-executeMethod", "XluaEditorTestSteps.ClearCode", "-quit"])
    config.unlink()
    Path(str(config) + ".meta").unlink(missing_ok=True)

    # Source generator in an XLuaBindings asmdef.
    folder = sg / "Assets/XLuaBindings"
    in_game = sg / "Assets/TimingGame/XLuaBindings.cs"
    for stale in [in_game, Path(str(in_game) + ".meta")]:
        if stale.exists():
            stale.unlink()
    folder.mkdir(exist_ok=True)
    (folder / "XLuaBindings.asmdef").write_text(json.dumps({"name": "XLuaBindings", "references": ["XLua"]}, indent=4) + "\n")
    (folder / "XLuaBindings.cs").write_text(bindings_source(types))
    first = compile_once(sg, "asmdef-first-%d" % len(types))
    edits = [0]

    def edit_bindings(project):
        edits[0] += 1
        (folder / "XLuaBindings.cs").write_text(bindings_source(types, edits[0]))

    entry["sourcegen-asmdef"] = {"first": first,
                                 "edit_game": measure_edits(sg, "asmdef-game-%d" % len(types), edit_probe, "Assembly-CSharp"),
                                 "edit_bindings": measure_edits(sg, "asmdef-bindings-%d" % len(types), edit_bindings,
                                                                "XLuaBindings")}

    # Source generator in Assembly-CSharp.
    shutil.rmtree(folder)
    Path(str(folder) + ".meta").unlink(missing_ok=True)
    in_game.write_text(bindings_source(types))
    entry["sourcegen-csharp"] = {"first": compile_once(sg, "csharp-first-%d" % len(types)),
                                 "edit_game": measure_edits(sg, "csharp-game-%d" % len(types), edit_probe, "Assembly-CSharp")}
    (a.work / "summary.json").write_text(json.dumps(summary, indent=1) + "\n")

(a.work / "summary.json").write_text(json.dumps(summary, indent=1) + "\n")
print("XLUA_COMPILE_TIME_DONE: " + str(a.work / "summary.json"))
