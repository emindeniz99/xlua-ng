#!/usr/bin/env python3
"""Drop CodeQL results located in vendored code before the SARIF is uploaded.

Usage: codeql-drop-vendored.py <input dir> <output dir>

The C/C++ analysis traces a manual libxlua build, which compiles the vendored
Lua, LuaJIT headers and LuaSocket too. CodeQL's `paths-ignore` applies only
to languages analysed without a build, so results in those directories are
removed here instead. The database still contains the vendored code, so data
flow through the Lua C API is unchanged; only the results are dropped. The
input files are left as they are (they go into the workflow artifact).

Vendored paths: build/lua-<version>, build/luajit-2.1, build/luasocket
(DEPENDENCIES.md, "Vendored runtimes and libraries"). Alerts there are
upstream code: record them, do not patch them.
"""
import json
import re
import sys
from pathlib import Path

VENDORED = re.compile(r"^build/(lua-\d+\.\d+\.\d+|luajit-2\.1|luasocket)/")


def primary_uri(result):
    locations = result.get("locations") or [{}]
    return locations[0].get("physicalLocation", {}).get("artifactLocation", {}).get("uri", "")


def main(src, dst):
    dst.mkdir(parents=True, exist_ok=True)
    files = sorted(src.glob("*.sarif"))
    if not files:
        sys.exit(f"codeql-drop-vendored: no SARIF file in {src}")
    for f in files:
        sarif = json.loads(f.read_text(encoding="utf-8"))
        for run in sarif["runs"]:
            results = run.get("results", [])
            kept = [r for r in results if not VENDORED.match(primary_uri(r))]
            print(f"{f.name}: kept {len(kept)} of {len(results)} results, "
                  f"dropped {len(results) - len(kept)} in vendored code")
            run["results"] = kept
        (dst / f.name).write_text(json.dumps(sarif), encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(Path(sys.argv[1]), Path(sys.argv[2]))
