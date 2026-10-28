#!/usr/bin/env python3
"""The branch coverage gate of Test/Native/coverage.sh.

    python3 Test/Native/coverage_gate.py SUMMARY.json RUNTIME FILE=PERCENT...

SUMMARY.json is `llvm-cov export -summary-only` output. For each FILE=PERCENT
whose file (by base name, such as xlua.c) the summary has, prints an
XLUA_COVERAGE_GATE line with the branch coverage and fails (exit 1) when it is
below PERCENT. A file the summary lacks is skipped: the core preset's runtimes
do not build xlua.c. Without a FILE=PERCENT the gate is off.
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
    for item in gate:
        name, sep, minimum = item.partition("=")
        if not sep:
            sys.exit("coverage_gate.py: expected FILE=PERCENT, got " + item)
        if name not in files:
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
