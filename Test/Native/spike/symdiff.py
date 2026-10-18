"""Diff the libil2cpp runtime (il2cpp:: and il2cpp_ functions) between two players' libil2cpp.sym.so.

usage: symdiff.py NM BASE_PROJECT OTHER_PROJECT [PATTERN]
"""
import re, subprocess, sys
from pathlib import Path

nm, base, other = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
pat = re.compile(sys.argv[4] if len(sys.argv) > 4 else r"GenericVirtual|GetGenericInstanceMethod|GenericMethod::|Inflate|GenericMetadata|GenericInterface")


def syms(project):
    lib = sorted(project.glob("Library/Bee/artifacts/objcopy_*/libil2cpp.sym.so"), key=lambda p: p.stat().st_mtime)[-1]
    out = subprocess.run([nm, "-C", "-S", "--defined-only", str(lib)], stdout=subprocess.PIPE, text=True).stdout
    d = {}
    for line in out.splitlines():
        parts = line.split(" ", 3)
        if len(parts) < 4 or parts[2] not in "tTwW":
            continue
        name = parts[3]
        if name.startswith("il2cpp::") or name.startswith("il2cpp_") or name.startswith("Il2Cpp"):
            d[name] = d.get(name, 0) + int(parts[1], 16)
    return lib, d


lb, b = syms(base)
lo, o = syms(other)
print("base", lb, len(b), "runtime functions,", sum(b.values()), "bytes")
print("other", lo, len(o), "runtime functions,", sum(o.values()), "bytes")
added = {k: v for k, v in o.items() if k not in b}
removed = {k: v for k, v in b.items() if k not in o}
print("added: %d functions, %d bytes; removed: %d, %d bytes" % (len(added), sum(added.values()), len(removed), sum(removed.values())))
for k, v in sorted(added.items(), key=lambda kv: -kv[1])[:40]:
    print("  + %6d %s" % (v, k[:160]))
print("matching pattern in base:")
for k, v in sorted(b.items()):
    if pat.search(k):
        print("  base %6d %s" % (v, k[:160]))
print("matching pattern added in other:")
for k, v in sorted(added.items()):
    if pat.search(k):
        print("  + %6d %s" % (v, k[:160]))
