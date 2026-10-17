"""How Unity compiled a source plugin in a size_report.py +srcplugin Android project.

Reads the player build's Bee graph (Library/Bee/Player*.dag.json) and trace (Library/Bee/backend*.traceevents):
the compiler command of one plugin file and of one generated IL2CPP file, the flags that differ between
them, the link flags, and the compile time of the plugin files (sum of the per-file times, and the span from
the first plugin compile starting to the last one ending, as Bee runs them in parallel with IL2CPP's own).
With --exports LIBXLUA_SO --prebuilt PROJECT it also lists which of the prebuilt library's exported
functions are left in libil2cpp.so.
"""
import argparse
import glob
import json
import os
from pathlib import Path
import re
import subprocess
import sys


def tool(name):
    ndk = os.environ.get("ANDROID_NDK")
    found = glob.glob(os.path.join(ndk, "toolchains/llvm/prebuilt/*/bin", name)) if ndk else []
    return found[0] if found else name


def nm(path, dynamic=False):
    out = subprocess.run([tool("llvm-nm"), "-S", "--defined-only"] + (["-D"] if dynamic else []) + [str(path)],
                         capture_output=True, text=True).stdout
    r = {}
    for line in out.splitlines():
        f = line.split()
        if len(f) == 4:
            r[f[3]] = (f[2], int(f[1], 16))
    return r


def flags(cmd):
    return [t for t in re.findall(r'"[^"]*"|\S+', cmd) if t.startswith("-") and not t.startswith(("-o", "-I"))]


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("project", type=Path)
    p.add_argument("--plugin-dir", default="Assets/Plugins/Android/xlua")
    p.add_argument("--exports", type=Path, help="the prebuilt libxlua.so")
    p.add_argument("--prebuilt", type=Path, help="the same project with the prebuilt library")
    a = p.parse_args()
    proj = a.project.resolve()
    plugin_files = {f.name for f in (proj / a.plugin_dir).glob("*.c")}
    dag = json.loads(sorted((proj / "Library/Bee").glob("Player*.dag.json"), key=os.path.getmtime)[-1].read_text())
    compiles, generated, link = {}, None, None
    for i, n in enumerate(dag["Nodes"]):
        ann = n.get("Annotation", "")
        if ann.startswith("C_Android") and "Action" in n:
            src = next((x for x in n["Inputs"] if x.endswith((".c", ".cpp"))), "")
            if Path(src).name in plugin_files:
                compiles[ann] = (Path(src).name, n["Action"])
            elif src.endswith("Assembly-CSharp.cpp"):
                generated = n["Action"]
        elif ann.startswith("Link_Android") and "libil2cpp" in ann:
            link = n["Action"]
    if not compiles:
        sys.exit("no plugin compile nodes in " + str(proj))
    name, cmd = sorted(compiles.values())[0]
    print("plugin files compiled: %d" % len(compiles))
    print("compiler: %s" % re.match(r'"?([^" ]+)', cmd).group(1))
    print("plugin flags (%s): %s" % (name, " ".join(flags(cmd))))
    if generated:
        fp, fg = flags(cmd), flags(generated)
        print("only for the plugin: %s" % " ".join(f for f in fp if f not in fg))
        print("only for generated C++: %s" % " ".join(f for f in fg if f not in fp))
    if link:
        print("link flags: %s" % " ".join(t for t in flags(link) if t.startswith("-Wl") or t in ("-shared", "-static-libstdc++", "-flto", "-flto=thin")))
    # Durations from the newest trace that has the plugin nodes (Chrome trace format, microseconds).
    events, others = [], []
    outputs = {ann.split(" ", 1)[1] for ann in compiles}
    for trace in sorted((proj / "Library/Bee").glob("backend*.traceevents"), key=os.path.getmtime, reverse=True):
        for line in trace.read_text(errors="replace").splitlines():
            line = line.strip().strip(",")
            if not line.startswith("{"):
                continue
            try:
                e = json.loads(line)
            except ValueError:
                continue
            if e.get("name", "").startswith("C_Android") and "dur" in e:
                (events if e.get("args", {}).get("detail") in outputs else others).append(e)
        if events:
            break
    spans = [(e["ts"], e["ts"] + e.get("dur", 0)) for e in events if "dur" in e]
    if spans:
        print("plugin compile time: %.1f s summed over %d files, %.1f s from first start to last end" % (
            sum(b - s for s, b in spans) / 1e6, len(spans), (max(b for _, b in spans) - min(s for s, _ in spans)) / 1e6))
        print("other C/C++ compiles in that build: %.1f s summed over %d files" % (sum(e["dur"] for e in others) / 1e6, len(others)))
    else:
        print("plugin compile time: not in the trace (the objects came from Bee's cache)")
    if a.exports and a.prebuilt:
        exports = {s for s, v in nm(a.exports, True).items() if v[0] in "TW"}
        out = "Library/Bee/artifacts/Android/il2cppOutput/build"
        syms = nm(next((proj / out).glob("*/libil2cpp.dbg.so")))
        ref = nm(next((a.prebuilt.resolve() / out).glob("*/libil2cpp.dbg.so")))
        gone = sorted(s for s in exports if s not in syms)
        added = {s: v for s, v in syms.items() if s not in ref}
        print("prebuilt exports: %d functions; in libil2cpp.so: %d; dropped by --gc-sections: %d (%s)" % (
            len(exports), len(exports) - len(gone), len(gone), " ".join(gone)))
        print("symbols the sources add to libil2cpp.dbg.so: %d, %s bytes" % (len(added), "{:,}".format(sum(v[1] for v in added.values()))))


if __name__ == "__main__":
    main()
