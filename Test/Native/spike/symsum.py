"""Sum function sizes in a project's latest libil2cpp.sym.so by regex groups.
usage: symsum.py NM PROJECT NAME=REGEX...
"""
import re, subprocess, sys
from pathlib import Path
nm, project = sys.argv[1], Path(sys.argv[2])
lib = sorted(project.glob("Library/Bee/artifacts/objcopy_*/libil2cpp.sym.so"), key=lambda p: p.stat().st_mtime)[-1]
groups = [(a.split("=", 1)[0], re.compile(a.split("=", 1)[1])) for a in sys.argv[3:]]
tot = {n: [0, 0] for n, _ in groups}
out = subprocess.run([nm, "-S", "--defined-only", str(lib)], stdout=subprocess.PIPE, text=True).stdout
for line in out.splitlines():
    parts = line.split()
    if len(parts) < 4 or parts[2] not in "tTwW":
        continue
    for n, r in groups:
        if r.search(parts[3]):
            tot[n][0] += 1
            tot[n][1] += int(parts[1], 16)
print(project.name, lib.parent.name, {n: "%d fn, %d B" % tuple(v) for n, v in tot.items()})
