"""Measure line and method coverage of xLua's C# code under Mono.

Runs Test/Native/run_managed.py --coverage --source-gen --source-gen-minimize,
which compiles every assembly that mcs builds with -debug (and the test
assembly of the source generator passes, which the .NET SDK's csc compiles,
with -debug:portable) and runs each Mono process under Mono's coverage profiler
(mono --profile=coverage, part of Mono; nothing is downloaded). The profiler
lists only the methods that were compiled, so CoverageLines.cs reads each
assembly's symbols (.mdb, or the portable .pdb with the tools' pinned
Mono.Cecil) to add the methods that never ran.

Reports, each in <output>/coverage/<name>.txt and summary.json:

  reflection          the suite with reflection (XLua.Mini.dll)
  sourcegen           the suite through the source generator's bindings
  sourcegen-minimize  the same with GEN_CODE_MINIMIZE
  suite               the three suite runs together
  tools               the XLuaGenerate runs: --migrate on the suite's lists, and
                      --luacats on the source-generated test assembly
  combined            all of them

Files are grouped as runtime (Assets/XLua/Src and Core outside Editor), editor
(their Editor folders) and generated (the source generator's files of the
sourcegen and sourcegen-minimize passes, <output>/source-gen*/generated: how
much of the generator's output the suite runs; until milestone M11 the group
measured Generate Code's output). Test code (Test/, General/Src) is excluded. A line counts as covered when one of its sequence
points ran. Mono's profiler has no branch counters; coverage_cs_dotnet.py
measures branches with coverlet on .NET. Code in inactive #if regions (HOTFIX_ENABLE, UNITY_*, ...) is not
compiled and so not counted.

<output>/coverage/annotated/ holds each runtime and editor file with the
combined hit count per line ("#####" for an executable line that never ran,
"-" for a line without code), uncovered-methods.txt lists the methods that
never ran, and methods.txt lists every method with its missed lines, most
missed first. --report-only rebuilds the reports from an earlier run.
"""
import argparse
import collections
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument("--plugin", type=Path, help="host-native libxlua (required unless --report-only)")
p.add_argument("--output", type=Path, required=True)
p.add_argument("--mono", default="mono")
p.add_argument("--compiler", default="mcs")
p.add_argument("--mono-version", default="6.14.1")
p.add_argument("--define", action="append", default=[], help="passed to run_managed.py (repeatable)")
p.add_argument("--timeout", type=int, default=3600, help="seconds for the whole run_managed.py run")
p.add_argument("--report-only", action="store_true",
               help="reuse the profiles already in <output>/coverage instead of running the suite")
a = p.parse_args()
out = a.output.resolve()
cov = out / "coverage"
src = (repo / "Assets/XLua/Src").resolve()
editor = src / "Editor"
# XLua.Core (Assets/XLua/Core): runtime, its Editor folder editor.
core = (repo / "Assets/XLua/Core").resolve()
compiler = [a.mono, a.compiler] if a.compiler.endswith(".exe") else [a.compiler]

if not a.report_only:
    if a.plugin is None:
        p.error("--plugin is required unless --report-only")
    command = [sys.executable, str(repo / "Test/Native/run_managed.py"), "--coverage", "--source-gen",
               "--source-gen-minimize", "--plugin", str(a.plugin.resolve()), "--output", str(out),
               "--mono", a.mono, "--compiler", a.compiler, "--mono-version", a.mono_version]
    command += ["--define=" + d for d in a.define]
    # A new process group, so that a timeout also stops the Mono children.
    process = subprocess.Popen(command, start_new_session=True)
    try:
        code = process.wait(timeout=a.timeout)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait()
        runs = json.loads((cov / "runs.json").read_text()) if (cov / "runs.json").is_file() else []
        raise SystemExit("run_managed.py timed out after %d s; last profiled process: %s"
                         % (a.timeout, runs[-1]["name"] if runs else "none"))
    if code:
        raise SystemExit("run_managed.py failed with exit code %d" % code)
    # The LuaCATS generator in the same tool, on the source-generated test assembly (its manifest).
    runs = json.loads((cov / "runs.json").read_text())
    tools = out / "tools"
    xml = cov / "xluagenerate-luacats.xml"
    runs.append({"name": "xluagenerate-luacats", "xml": str(xml),
                 "assemblies": [str(tools / "XLua.Mini.dll"), str(tools / "XLuaGenerate.exe")]})
    (cov / "runs.json").write_text(json.dumps(runs, indent=1) + "\n")
    env = dict(os.environ)
    for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
        env[key] = str(tools) + os.pathsep + env.get(key, "")
    process = subprocess.Popen([a.mono, "--debug", "--profile=coverage:output=" + str(xml),
                                str(tools / "XLuaGenerate.exe"), "--luacats", str(out / "source-gen/XLuaUnitTest.exe"),
                                str(out / "LuaTypes")], cwd=tools, env=env, start_new_session=True)
    try:
        code = process.wait(timeout=600)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait()
        raise SystemExit("XLuaGenerate --luacats timed out after 600 s")
    if code:
        raise SystemExit("XLuaGenerate --luacats failed with exit code %d" % code)

runs = json.loads((cov / "runs.json").read_text())
lines_tool = cov / "CoverageLines.exe"
# The portable .pdb reader is the Mono.Cecil that run_managed.py's tools build put
# next to the tools (Tools/build_tools.py: pinned, SHA-512 checked); the tool gets a copy.
cecil = out / "tools/Mono.Cecil.dll"
if not cecil.is_file():
    raise SystemExit("%s is missing: run without --report-only first (run_managed.py builds the tools)" % cecil)
shutil.copy2(cecil, cov / cecil.name)
subprocess.run(compiler + ["-r:Mono.CompilerServices.SymbolWriter", "-r:" + str(cov / cecil.name), "-out:" + str(lines_tool),
                           str(repo / "Test/Native/CoverageLines.cs")], check=True)


def category(path):
    path = Path(path)
    if path.is_relative_to(editor) or path.is_relative_to(core / "Editor"):
        return "editor"
    if path.is_relative_to(src) or path.is_relative_to(core):
        return "runtime"
    # <output>/source-gen<suffix>/generated/...: the source generator's files of one pass.
    parts = path.relative_to(out).parts if path.is_relative_to(out) else ()
    if len(parts) > 2 and parts[0].startswith("source-gen") and parts[1] == "generated":
        return "generated"
    return None


points_cache = {}


def sequence_points(assembly):
    """{token: [(offset, line, file, method)]} for every method with debug lines."""
    if assembly not in points_cache:
        listing = subprocess.run([a.mono, str(lines_tool), assembly], stdout=subprocess.PIPE,
                                 text=True, check=True).stdout
        points = collections.defaultdict(list)
        for row in listing.splitlines():
            token, offset, line, file, method = row.split("\t")
            points[int(token)].append((int(offset), int(line), str(Path(file).resolve()), method))
        points_cache[assembly] = points
    return points_cache[assembly]


def load_run(run):
    """Per-line hit counts and per-method hit flags of one profiled process."""
    hits = collections.Counter()
    for method in ET.parse(run["xml"]).getroot().iter("method"):
        for statement in method.iter("statement"):
            key = (method.get("assembly"), int(method.get("token")), int(statement.get("offset")))
            hits[key] += int(statement.get("counter"))
    lines = collections.Counter()
    methods = {}
    for assembly in run["assemblies"]:
        name = Path(assembly).stem
        for token, points in sequence_points(assembly).items():
            ran = False
            for offset, line, file, method in points:
                if category(file) is None:
                    continue
                count = hits.get((name, token, offset), 0)
                # A line's count is that of its most-run sequence point.
                lines[(file, line)] = max(lines.get((file, line), 0), count)
                ran = ran or count > 0
            first = min(points, key=lambda x: x[1])
            if category(first[2]) is not None:
                key = (first[2], first[3], first[1])
                methods[key] = methods.get(key, False) or ran
                method_lines[key].update((file, line) for _, line, file, _ in points)
    return lines, methods


method_lines = collections.defaultdict(set)


data = {run["name"]: load_run(run) for run in runs}
suite = {"managed-suite": "reflection", "managed-suite-source-gen": "sourcegen",
         "managed-suite-source-gen-minimize": "sourcegen-minimize"}
reports = {mode: [name] for name, mode in suite.items() if name in data}
reports["suite"] = [name for name in suite if name in data]
reports["tools"] = [name for name in data if name.startswith("xluagenerate")]
reports["combined"] = list(data)


def merge(names):
    lines, methods = collections.Counter(), {}
    for name in names:
        run_lines, run_methods = data[name]
        for key, count in run_lines.items():
            lines[key] += count
        for key, ran in run_methods.items():
            methods[key] = methods.get(key, False) or ran
    return lines, methods


def percent(covered, total):
    return round(100.0 * covered / total, 1) if total else 0.0


def table(lines, methods):
    """{category: {file: [covered lines, lines, covered methods, methods]}}"""
    result = collections.defaultdict(lambda: collections.defaultdict(lambda: [0, 0, 0, 0]))
    for (file, _), count in lines.items():
        row = result[category(file)][file]
        row[1] += 1
        row[0] += count > 0
    for (file, _, _), ran in methods.items():
        row = result[category(file)][file]
        row[3] += 1
        row[2] += ran
    return result


def display(file):
    path = Path(file)
    return str(path.relative_to(repo)) if path.is_relative_to(repo) else str(path.relative_to(out))


summary = {}
for report, names in reports.items():
    if not names:
        continue
    files = table(*merge(names))
    text = ["%s: %s" % (report, ", ".join(names)), ""]
    summary[report] = {}
    for group in ["runtime", "editor", "generated"]:
        if group not in files:
            continue
        rows = files[group]
        total = [sum(r[i] for r in rows.values()) for i in range(4)]
        summary[report][group] = {
            "lines": total[1], "covered_lines": total[0], "line_percent": percent(total[0], total[1]),
            "methods": total[3], "covered_methods": total[2], "method_percent": percent(total[2], total[3]),
            "files": {display(f): {"lines": r[1], "covered_lines": r[0], "line_percent": percent(r[0], r[1]),
                                   "methods": r[3], "covered_methods": r[2]} for f, r in rows.items()}}
        text.append("%-58s %15s %7s %13s" % (group, "lines", "", "methods"))
        if group != "generated":  # one file per wrapped type: summary.json lists them
            for f, r in sorted(rows.items(), key=lambda x: display(x[0])):
                text.append("%-58s %7d/%-7d %6.1f%% %6d/%-6d" % (display(f), r[0], r[1], percent(r[0], r[1]), r[2], r[3]))
        text.append("%-58s %7d/%-7d %6.1f%% %6d/%-6d" % ("TOTAL " + group + " (%d files)" % len(rows),
                    total[0], total[1], percent(total[0], total[1]), total[2], total[3]))
        text.append("")
    (cov / (report + ".txt")).write_text("\n".join(text) + "\n")
    print("\n".join(text))
(cov / "summary.json").write_text(json.dumps(summary, indent=1, sort_keys=True) + "\n")

# Annotated sources and never-run methods from the combined run.
lines, methods = merge(reports["combined"])
annotated = cov / "annotated"
by_file = collections.defaultdict(dict)
for (file, line), count in lines.items():
    by_file[file][line] = count
for file, counts in by_file.items():
    if category(file) not in ("runtime", "editor"):
        continue
    target = annotated / (display(file) + ".txt")
    target.parent.mkdir(parents=True, exist_ok=True)
    source = Path(file).read_text(encoding="utf-8-sig", errors="replace").splitlines()
    target.write_text("".join("%9s:%5d: %s\n" % (
        "-" if n not in counts else ("#####" if counts[n] == 0 else counts[n]), n, text)
        for n, text in enumerate(source, 1)))
never = sorted((display(f), line, m) for (f, m, line), ran in methods.items()
               if not ran and category(f) in ("runtime", "editor"))
(cov / "uncovered-methods.txt").write_text("".join("%s:%d %s\n" % row for row in never))
# Every method with its missed lines, most missed first: where tests would add the most.
partial = []
for (f, m, line), keys in method_lines.items():
    if category(f) in ("runtime", "editor"):
        missed = sum(lines[k] == 0 for k in keys)
        partial.append((-missed, display(f), line, m, len(keys)))
(cov / "methods.txt").write_text("missed/lines  method\n" + "".join(
    "%6d/%-6d %s:%d %s\n" % (-missed, total, f, line, m) for missed, f, line, m, total in sorted(partial)))


def line_percent(report, group):
    return summary.get(report, {}).get(group, {}).get("line_percent", 0.0)


print("XLUA_CS_COVERAGE: runtime lines %.1f%% in the suite, %.1f%% combined (reflection %.1f%%, "
      "sourcegen %.1f%%, sourcegen-minimize %.1f%%); generated lines %.1f%% (sourcegen), %.1f%% (sourcegen-minimize); "
      "ConfigMigration.cs lines %.1f%%; report in %s" % (
          line_percent("suite", "runtime"), line_percent("combined", "runtime"),
          line_percent("reflection", "runtime"),
          line_percent("sourcegen", "runtime"), line_percent("sourcegen-minimize", "runtime"),
          line_percent("sourcegen", "generated"), line_percent("sourcegen-minimize", "generated"),
          summary.get("tools", {}).get("editor", {}).get("files", {})
          .get("Assets/XLua/Src/Editor/ConfigMigration.cs", {}).get("line_percent", 0.0), cov))
