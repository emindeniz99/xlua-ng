"""Interleaved host benchmark of the spike modes: build once, then R rounds of every mode in rotating order.

usage: spike_bench.py REPO OUT PLUGIN ROUNDS MODES [FILTER]
"""
import json
import sys
import types
from pathlib import Path

repo, out, plugin, rounds, modes = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]), int(sys.argv[4]), sys.argv[5].split(",")
filt = sys.argv[6] if len(sys.argv) > 6 else "lua_cs."
sys.path.insert(0, str(repo / "Test/Native"))
import benchmark  # noqa: E402

a = types.SimpleNamespace(mono="mono", mono_api=None, compiler="mcs", define=[], runs=5, warmup=1, scale=1.0,
                          filter=filt, timeout=1200)
out.mkdir(parents=True, exist_ok=True)
exes = benchmark.build(a, out, modes)
raw = []
for r in range(rounds):
    order = modes[r % len(modes):] + modes[:r % len(modes)]
    for m in order:
        meta, results, errors = benchmark.run_bench(a, out, exes[m], m, "r%d" % r, plugin, ["lua_cs"])
        for x in results:
            x["round"] = r
        raw += results
(out / "interleaved-raw.json").write_text(json.dumps(raw, indent=1))
best = {}
for x in raw:
    k = (x["mode"], x["group"] + "." + x["name"])
    best[k] = min(best.get(k, 1e18), x["median_ns_per_op"])
names = []
for x in raw:
    n = x["group"] + "." + x["name"]
    if n not in names:
        names.append(n)
print("%-34s" % "case (min of round medians, ns)" + "".join("%20s" % m for m in modes))
for n in names:
    print("%-34s" % n + "".join("%20s" % ("%.1f" % best[(m, n)] if (m, n) in best else "-") for m in modes))
alloc = {}
for x in raw:
    alloc[(x["mode"], x["group"] + "." + x["name"])] = x["managed_bytes_per_op"]
print("\nmanaged B/op")
for n in names:
    print("%-34s" % n + "".join("%20s" % alloc.get((m, n), "-") for m in modes))
(out / "interleaved.json").write_text(json.dumps({"modes": modes, "rounds": rounds,
    "best": {"%s|%s" % k: v for k, v in best.items()}}, indent=1))
