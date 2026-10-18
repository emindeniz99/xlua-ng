"""SPIKE (spike/descriptor-wrappers): compile SpikeDescCheck.cs in three modes and diff the Lua-visible output.

    python3 -I Test/Native/spike_desc_check.py --output DIR --plugin path/to/libxlua.dylib
"""
import argparse
import difflib
import os
from pathlib import Path
import shutil
import subprocess
import sys

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here))
import benchmark  # noqa: E402

MODES = {"plain": ["XLUA_SPIKE_SUBSET"], "gcm": ["GEN_CODE_MINIMIZE", "XLUA_SPIKE_SUBSET"],
         "desc": ["GEN_CODE_MINIMIZE", "XLUA_SPIKE_DESC"]}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--plugin", type=Path, required=True)
    p.add_argument("--mono", default="mono")
    p.add_argument("--mono-api", type=Path)
    p.add_argument("--timeout", type=int, default=300)
    a = p.parse_args()
    out = a.output.resolve()
    outputs = {}
    for mode, defines in MODES.items():
        bin_dir = out / "bin" / mode
        shutil.rmtree(bin_dir, ignore_errors=True)
        bin_dir.mkdir(parents=True)
        exe = bin_dir / "SpikeCheck.exe"
        files = benchmark.sources("XLua.Mini") + [str(here / "SpikeDescCheck.cs")]
        benchmark.csc(a, out, bin_dir, exe, defines, files, True)
        shutil.copy2(a.plugin, bin_dir / a.plugin.name)
        env = dict(os.environ, DYLD_LIBRARY_PATH=str(bin_dir), LD_LIBRARY_PATH=str(bin_dir))
        r = subprocess.run([a.mono, str(exe), str(here / "spike_desc_check.lua")], cwd=bin_dir, env=env,
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=a.timeout)
        (out / (mode + ".txt")).write_text(r.stdout)
        outputs[mode] = r.stdout.splitlines()
        counts = [l for l in (bin_dir / "csc.log").read_text().splitlines() if "SPIKE001" in l]
        print(mode, "exit", r.returncode, len(outputs[mode]), "lines;", counts[0].split("spike counts")[-1] if counts else "")
    failed = False
    for mode in ("gcm", "desc"):
        diff = list(difflib.unified_diff(outputs["plain"], outputs[mode], "plain", mode, lineterm="", n=0))
        print("== plain vs %s: %s" % (mode, "same" if not diff else "%d diff lines" % len(diff)))
        for line in diff:
            print(line)
        failed |= bool(diff)
    print("SPIKE_DESC_CHECK_" + ("DIFF" if failed else "SAME"))


if __name__ == "__main__":
    main()
