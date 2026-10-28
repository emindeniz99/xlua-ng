#!/usr/bin/env python3
"""The branch coverage gate of Test/Native/coverage.sh.

    python3 Test/Native/coverage_gate.py SUMMARY.json RUNTIME FILE[@RUNTIME]=PERCENT...

SUMMARY.json is `llvm-cov export -summary-only` output. For each FILE=PERCENT
(FILE by base name, such as xlua.c) prints an XLUA_COVERAGE_GATE line with the
branch coverage and fails (exit 1) when it is below PERCENT, or when the
summary lacks FILE: coverage.sh passes each kind of runtime the files its
build compiles. FILE@RUNTIME=PERCENT replaces FILE's minimum on that runtime
only (xlua_core.c@core-5.4.9=99). A file without branches counts as 100%.
Without a FILE=PERCENT the gate is off.
"""
import json
import os
import sys


def main(argv):
    if len(argv) < 3:
        sys.exit(__doc__)
    summary, runtime, gate = argv[1], argv[2], argv[3:]
    with open(summary) as f:
        data = json.load(f)
    files = {os.path.basename(entry["filename"]): entry["summary"]["branches"] for entry in data["data"][0]["files"]}
    failed = False
    minimums = {}
    for item in gate:
        name, sep, minimum = item.partition("=")
        if not sep:
            sys.exit("coverage_gate.py: expected FILE=PERCENT or FILE@RUNTIME=PERCENT, got " + item)
        name, at, only = name.partition("@")
        if at and only != runtime:
            continue
        if at or name not in minimums:  # FILE@RUNTIME wins over FILE for that runtime
            minimums[name] = minimum
    for name, minimum in minimums.items():
        if name not in files:
            # coverage.sh gates each kind of runtime with the files its build compiles,
            # so a file missing from the summary is a mistake in the gate or the build.
            print("XLUA_COVERAGE_GATE: FAIL %s on %s: not measured in this build" % (name, runtime))
            failed = True
            continue
        branches = files[name]
        count, covered = branches["count"], branches["covered"]
        percent = 100.0 * covered / count if count else 100.0
        ok = percent >= float(minimum)
        failed |= not ok
        print("XLUA_COVERAGE_GATE: %s %s on %s: %.2f%% of branches (%d of %d), minimum %s%%"
              % ("PASS" if ok else "FAIL", name, runtime, percent, covered, count, minimum))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
