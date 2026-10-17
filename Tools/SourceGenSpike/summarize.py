#!/usr/bin/env python3
"""Prints a Markdown table from the run_spike.py result files."""
import glob
import json
import os

BASE = "/Volumes/T7Data/ext-ai-projects/xlua-ng/sourcegen-spike/results"
STEPS = ["compile", "error", "incremental", "mono-build-High", "mono-player-High", "android-build"]

print("| run | " + " | ".join(STEPS) + " | analyzer load problems |")
print("|---|" + "---|" * (len(STEPS) + 1))
for path in sorted(glob.glob(os.path.join(BASE, "*.json"))):
    r = json.load(open(path))
    cells = []
    problems = set()
    for s in STEPS:
        if s not in r:
            cells.append("-")
            continue
        x = r[s]
        problems.update(x.get("analyzer_problems", []))
        cells.append(("pass" if x.get("pass") else "FAIL") + " (%ss)" % x.get("secs"))
    print("| %s | %s | %s |" % (os.path.basename(path)[:-5], " | ".join(cells), "; ".join(sorted(problems)) or "none"))
