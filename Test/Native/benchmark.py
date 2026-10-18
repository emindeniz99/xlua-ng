"""Benchmark the C#/Lua bridge, the Lua runtime, GC behaviour and plugin size.

Compiles Test/Native/Benchmark.cs with the xLua sources under Mono once per
bridge mode, runs it against each --plugin, and writes one JSON file plus a
table on stdout. Nothing is asserted; compare two result files with
--compare OLD.json NEW.json.

Bridge modes:
  reflection   FORCE_REFLECTION: Utils.ReflectionWrap, the IL2CPP path without generated code
  emit         default XLUA_GENERAL/editor path: wrappers emitted at run time (CodeEmit)
  sourcegen    wrappers from the xLua source generator (Tools/build_source_generator.py),
               compiled with the .NET SDK's csc
  sourcegen-min    the source generator's GEN_CODE_MINIMIZE mode, compiled with csc

The source generator modes compile against the Mono assemblies mcs uses
(lib/mono/4.5 next to the mono executable, or --mono-api) and run on Mono like the
others. The legacy generator's modes (gencode, gencode-min and their -csc builds)
went with it in source generator milestone M11; results recorded before then
(build/SOURCEGEN.md) compare them with these.

Sizes: every --plugin, and each --size LABEL=PATH, is recorded raw, stripped
(a stripped copy; the input is not changed) and gzip -9 of the stripped copy.
--player LABEL=PATH records a built player (APK, AAB, IPA, zip, or a directory
such as a WebGL build) with the xLua entries found inside it.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
import zlib

MODES = {
    "reflection": {"defines": ["FORCE_REFLECTION"]},
    "emit": {"defines": []},
    "sourcegen": {"defines": [], "csc": True, "analyzer": True},
    "sourcegen-min": {"defines": ["GEN_CODE_MINIMIZE"], "csc": True, "analyzer": True},
    # SPIKE (spike/descriptor-wrappers): the descriptor prototype's member subset in every mode.
    "sourcegen-sub": {"defines": ["XLUA_SPIKE_SUBSET"], "csc": True, "analyzer": True},
    "sourcegen-min-sub": {"defines": ["GEN_CODE_MINIMIZE", "XLUA_SPIKE_SUBSET"], "csc": True, "analyzer": True},
    "sourcegen-desc": {"defines": ["GEN_CODE_MINIMIZE", "XLUA_SPIKE_DESC"], "csc": True, "analyzer": True},
    "sourcegen-desc-stack": {"defines": ["GEN_CODE_MINIMIZE", "XLUA_SPIKE_DESC", "XLUA_SPIKE_DESC_STACK"], "csc": True, "analyzer": True},
}
repo = Path(__file__).resolve().parents[2]


def label_path(text):
    label, sep, path = text.partition("=")
    if not sep or not label or not path:
        raise argparse.ArgumentTypeError("expected LABEL=PATH, got " + text)
    return label, Path(path)


def output(cmd, **kw):
    try:
        return subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, **kw).stdout.strip()
    except OSError:
        return None


def host_info(a):
    info = {"platform": platform.platform(), "machine": platform.machine(), "cpus": os.cpu_count(),
            "python": platform.python_version(), "container": Path("/.dockerenv").exists()}
    mono = output([a.mono, "--version"])
    info["mono"] = mono.splitlines()[0] if mono else None
    cpuinfo = Path("/proc/cpuinfo")
    if cpuinfo.exists():
        found = re.search(r"^model name\s*:\s*(.+)$", cpuinfo.read_text(), re.M)
        info["cpu"] = found.group(1) if found else None
    elif sys.platform == "darwin":
        info["cpu"] = output(["sysctl", "-n", "machdep.cpu.brand_string"])
    # Free text for what the script cannot see, e.g. the machine behind a container.
    info["note"] = os.environ.get("XLUA_BENCH_HOST")
    return info


def commit():
    if os.environ.get("XLUA_BENCH_COMMIT"):
        return os.environ["XLUA_BENCH_COMMIT"]
    found = output(["git", "-C", str(repo), "rev-parse", "HEAD"])
    return found if found and re.fullmatch(r"[0-9a-f]{40}", found) else None


# ---------------------------------------------------------------- sizes

def binary_format(path):
    head = path.read_bytes()[:20]
    if head[:4] == b"\x7fELF":
        machine = int.from_bytes(head[18:20], "little" if head[5] == 1 else "big")
        arch = {3: "x86", 40: "arm", 62: "x86_64", 183: "arm64"}.get(machine, str(machine))
        return "elf", arch
    if head[:4] in (b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe"):
        return "mach-o", None
    if head[:8] == b"!<arch>\n":
        return "archive", None
    if head[:2] == b"MZ":
        return "pe", None
    return "other", None


def strip_copy(path, fmt):
    """Strip a temporary copy and return (stripped bytes, tool) or (None, reason)."""
    if fmt == "elf":
        tools = [[os.environ["STRIP"], "--strip-unneeded"]] if os.environ.get("STRIP") else []
        tools += [["llvm-strip", "--strip-unneeded"], ["strip", "--strip-unneeded"]]
    elif fmt == "mach-o":
        tools = [["strip", "-x"]]
    elif fmt == "archive":
        tools = [["llvm-strip", "--strip-debug"], ["strip", "-S"]]
    else:
        return None, "no strip for " + fmt
    with tempfile.TemporaryDirectory() as tmp:
        copy = Path(tmp) / path.name
        for tool in tools:
            if not shutil.which(tool[0]):
                continue
            shutil.copy2(path, copy)
            if subprocess.run(tool + [str(copy)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0:
                data = copy.read_bytes()
                return (len(data), len(zlib.compress(data, 9))), " ".join(tool)
        return None, "strip failed or not found"


def size_entry(label, path):
    fmt, arch = binary_format(path)
    entry = {"label": label, "path": str(path), "format": fmt, "arch": arch, "bytes": path.stat().st_size}
    stripped, tool = strip_copy(path, fmt)
    entry["strip"] = tool
    entry["stripped_bytes"], entry["stripped_gzip_bytes"] = stripped if stripped else (None, None)
    return entry


def player_entry(label, path):
    """Total size of a player build and of the xLua files inside it."""
    entry = {"label": label, "path": str(path), "xlua_entries": []}
    if path.is_dir():
        files = [f for f in path.rglob("*") if f.is_file()]
        entry["kind"] = "directory"
        entry["bytes"] = sum(f.stat().st_size for f in files)
        entry["largest"] = [{"name": str(f.relative_to(path)), "bytes": f.stat().st_size}
                            for f in sorted(files, key=lambda f: -f.stat().st_size)[:5]]
        entry["xlua_entries"] = [{"name": str(f.relative_to(path)), "bytes": f.stat().st_size}
                                 for f in files if "xlua" in f.name.lower()]
    else:
        entry["kind"] = path.suffix.lstrip(".").lower() or "file"
        entry["bytes"] = path.stat().st_size
        if zipfile.is_zipfile(path):
            with zipfile.ZipFile(path) as z:
                entry["xlua_entries"] = [{"name": i.filename, "bytes": i.file_size, "compressed_bytes": i.compress_size}
                                         for i in z.infolist() if "xlua" in i.filename.lower()]
    return entry


# ---------------------------------------------------------------- build and run

def sources(name):
    project = repo / "General/vs2013" / (name + ".csproj")
    return [str((project.parent / x.attrib["Include"].replace("\\", "/")).resolve())
            for x in ET.parse(project).iter() if x.tag.endswith("}Compile")]


def build(a, out, modes):
    compiler = [a.mono, a.compiler] if a.compiler.endswith(".exe") else [a.compiler]
    bench_cs = str(repo / "Test/Native/Benchmark.cs")
    exes = {}
    for mode in modes:
        cfg = MODES[mode]
        bin_dir = out / "bin" / mode
        shutil.rmtree(bin_dir, ignore_errors=True)
        bin_dir.mkdir(parents=True)
        exe = bin_dir / "XLuaBench.exe"
        files = sources("XLua.Mini") + [bench_cs]
        if cfg.get("csc"):
            csc(a, out, bin_dir, exe, cfg["defines"] + a.define, files, cfg.get("analyzer"))
        else:
            subprocess.run(compiler + ["-optimize+", "-unsafe", "-define:XLUA_GENERAL"] + ["-define:" + d for d in cfg["defines"] + a.define]
                           + ["-out:" + str(exe)] + files, check=True)
        exes[mode] = exe
    return exes


def csc(a, out, bin_dir, exe, defines, files, analyzer):
    """Compile with the .NET SDK's csc against Mono's assemblies, as run_managed.py --source-gen does."""
    sys.path.insert(0, str(repo / "Tools"))
    import build_source_generator
    dotnet = build_source_generator.dotnet_path()
    cscs = sorted(Path(dotnet).resolve().parent.glob("sdk/*/Roslyn/bincore/csc.dll"))
    api = a.mono_api or Path(shutil.which(a.mono)).resolve().parents[1] / "lib/mono/4.5"
    if not cscs or not (api / "mscorlib.dll").is_file():
        raise SystemExit("the csc modes need the .NET SDK's csc.dll and Mono's lib/mono/4.5 (--mono-api)")
    refs = [api / n for n in ["mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll"]]
    lines = ["-nologo", "-noconfig", "-nostdlib", "-unsafe", "-optimize+", "-deterministic", "-langversion:9",
             "-define:" + ";".join(["XLUA_GENERAL"] + defines), "-out:" + str(exe)] + ["-r:" + str(r) for r in refs]
    if analyzer:
        generator = build_source_generator.build(out / "source-gen-build")
        lines += ["-analyzer:" + str(generator), "-generatedfilesout:" + str(bin_dir / "generated")]
        (bin_dir / "generated").mkdir(exist_ok=True)
    rsp = bin_dir / "csc.rsp"
    rsp.write_text("\n".join('"%s"' % l if " " in l else l for l in lines + [str(f) for f in files]) + "\n")
    result = subprocess.run([dotnet, str(cscs[-1]), "@" + str(rsp)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    (bin_dir / "csc.log").write_text(result.stdout)
    if result.returncode != 0 or re.search(r"(error|warning) XLSG|warning (CS8032|CS8784|CS8785|CS9057)", result.stdout):
        sys.stdout.write(result.stdout[-4000:])
        raise SystemExit("csc failed for " + str(exe) + "; see " + str(bin_dir / "csc.log"))
    if analyzer and not list((bin_dir / "generated").rglob("W_*.g.cs")):
        raise SystemExit("the source generator wrote no wrappers for " + str(exe))


def run_bench(a, out, exe, mode, label, plugin, groups):
    run_dir = out / "run" / label
    run_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(plugin, run_dir / plugin.name)
    env = dict(os.environ)
    for key in ["LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"]:
        env[key] = str(run_dir) + os.pathsep + env.get(key, "")
    cmd = [a.mono, str(exe), "--mode", mode, "--runs", str(a.runs), "--warmup", str(a.warmup),
           "--scale", str(a.scale), "--groups", ",".join(groups)]
    if a.filter:
        cmd += ["--filter", a.filter]
    log = out / "logs" / (label + "-" + mode + ".log")
    log.parent.mkdir(exist_ok=True)
    print("==>", label, mode, flush=True)
    with open(log, "w") as f:
        proc = subprocess.Popen(cmd, cwd=run_dir, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        try:
            text, _ = proc.communicate(timeout=a.timeout)
        except subprocess.TimeoutExpired:
            proc.kill()
            text, _ = proc.communicate()
            f.write(text)
            started = re.findall(r"^XLUA_BENCH_START (\S+)", text, re.M)
            raise SystemExit("%s %s: timed out after %ds; last case started: %s (log %s)"
                             % (label, mode, a.timeout, started[-1] if started else "none", log))
        f.write(text)
    if proc.returncode != 0 or "XLUA_BENCH_DONE" not in text:
        sys.stdout.write(text[-4000:])
        raise SystemExit("%s %s: benchmark exited with %d (log %s)" % (label, mode, proc.returncode, log))
    meta = json.loads(re.search(r"^XLUA_BENCH_META (.+)$", text, re.M).group(1))
    results = [json.loads(m) for m in re.findall(r"^XLUA_BENCH (\{.+\})$", text, re.M)]
    errors = [json.loads(m) for m in re.findall(r"^XLUA_BENCH_ERROR (.+)$", text, re.M)]
    for e in errors:
        print("  error:", e["group"] + "." + e["name"], e["error"])
    for r in results + errors:
        r["runtime"] = label
        r.setdefault("mode", mode)
    return meta, results, errors


# ---------------------------------------------------------------- reports

def fmt_ns(v):
    return "-" if v is None else ("%.1f" % v if v < 1000 else "%.0f" % v)


def fmt_b(v):
    return "-" if v is None else ("%.0f" % v if abs(v) >= 10 else "%.1f" % v)


def key(r):
    return r["runtime"], r["mode"], r["group"] + "." + r["name"]


def print_tables(data):
    by_key = {key(r): r for r in data["results"]}
    modes = data["settings"]["modes"]

    def cells(r):
        if r is None:
            return "%10s%8s%7s" % ("-", "-", "-")
        return "%10s%8s%7s" % (fmt_ns(r["median_ns_per_op"]), fmt_b(r["managed_bytes_per_op"]), fmt_b(r["lua_bytes_per_op"]))

    for label, rt in data["runtimes"].items():
        print("\n%s (%s%s): median ns/op, managed B/op, Lua B/op"
              % (label, rt["lua_version"], ", " + rt["jit_version"] if rt.get("jit_version") else ""))
        names = []
        for r in data["results"]:
            n = r["group"] + "." + r["name"]
            if r["runtime"] == label and n not in names:
                names.append(n)
        bridge = [n for n in names if not n.startswith("lua.")]
        if bridge:
            print("%-30s" % "" + "".join("%25s" % m for m in modes))
            print("%-30s" % "bridge" + "%10s%8s%7s" % ("ns/op", "B/op", "LuaB") * len(modes))
            for n in bridge:
                print("%-30s" % n + "".join(cells(by_key.get((label, m, n))) for m in modes))
        pure = [n for n in names if n.startswith("lua.")]
        if pure:
            print("%-30s%10s%8s%7s" % ("pure Lua (any mode)", "ns/op", "B/op", "LuaB"))
            for n in pure:
                print("%-30s" % n + cells(by_key.get((label, "any", n))))
    if data["sizes"]:
        print("\n%-24s %-8s %-7s %12s %12s %12s" % ("plugin size", "format", "arch", "bytes", "stripped", "gzip -9"))
        for s in data["sizes"]:
            print("%-24s %-8s %-7s %12s %12s %12s" % (s["label"], s["format"], s["arch"] or "-", s["bytes"],
                                                    s["stripped_bytes"] or "-", s["stripped_gzip_bytes"] or "-"))
    for p in data["players"]:
        print("\nplayer %s (%s): %d bytes" % (p["label"], p["kind"], p["bytes"]))
        for e in p["xlua_entries"]:
            print("  %-60s %12d" % (e["name"], e["bytes"]))


def pct(old, new):
    if old in (None, 0) or new is None:
        return "-"
    return "%+.1f%%" % ((new - old) * 100.0 / old)


def compare(old_path, new_path):
    old, new = (json.loads(Path(p).read_text()) for p in (old_path, new_path))
    print("old: %s %s\nnew: %s %s" % (old_path, old.get("commit"), new_path, new.get("commit")))
    print("time change: + is slower. B/op: managed / Lua bytes per op, old -> new.\n")
    olds = {key(r): r for r in old["results"]}
    print("%-8s %-11s %-34s %10s %10s %8s  %s" % ("runtime", "mode", "benchmark", "old ns", "new ns", "change", "B/op"))
    for r in new["results"]:
        o = olds.pop(key(r), None)
        if o is None:
            print("%-8s %-11s %-34s %10s %10s %8s" % (key(r) + ("-", fmt_ns(r["median_ns_per_op"]), "new")))
            continue
        alloc = ""
        if (o["managed_bytes_per_op"], o["lua_bytes_per_op"]) != (r["managed_bytes_per_op"], r["lua_bytes_per_op"]):
            alloc = "%s/%s -> %s/%s" % (fmt_b(o["managed_bytes_per_op"]), fmt_b(o["lua_bytes_per_op"]),
                                        fmt_b(r["managed_bytes_per_op"]), fmt_b(r["lua_bytes_per_op"]))
        print("%-8s %-11s %-34s %10s %10s %8s  %s" % (key(r) + (fmt_ns(o["median_ns_per_op"]), fmt_ns(r["median_ns_per_op"]),
                                                             pct(o["median_ns_per_op"], r["median_ns_per_op"]), alloc)))
    for k in olds:
        print("%-8s %-11s %-34s %10s" % (k + ("removed",)))
    for section, field in (("sizes", "stripped_bytes"), ("players", "bytes")):
        olds = {s["label"]: s for s in old.get(section, [])}
        rows = [(s["label"], olds.get(s["label"], {}).get(field), s.get(field)) for s in new.get(section, [])]
        if rows:
            print("\n%-24s %12s %12s %8s" % (section + " (" + field + ")", "old", "new", "change"))
            for label, o, n in rows:
                print("%-24s %12s %12s %8s" % (label, o if o is not None else "-", n if n is not None else "-", pct(o, n)))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--compare", nargs=2, metavar=("OLD", "NEW"), help="print the relative change between two result files")
    p.add_argument("--plugin", type=label_path, action="append", default=[], metavar="LABEL=PATH",
                   help="host-native libxlua to benchmark, e.g. 5.5.1=/path/libxlua.so")
    p.add_argument("--size", type=label_path, action="append", default=[], metavar="LABEL=PATH",
                   help="another native plugin to record the size of (any platform)")
    p.add_argument("--player", type=label_path, action="append", default=[], metavar="LABEL=PATH",
                   help="built player (APK/AAB/IPA/zip or a directory) to record the size of")
    p.add_argument("--output", type=Path, help="work directory for builds and logs")
    p.add_argument("--json", type=Path, help="result file (default OUTPUT/benchmark.json)")
    p.add_argument("--modes", default="reflection,emit,sourcegen", help="comma-separated: " + ", ".join(MODES))
    p.add_argument("--runs", type=int, default=5, help="timed runs per case (median and min are reported)")
    p.add_argument("--warmup", type=int, default=1, help="untimed runs per case before timing")
    p.add_argument("--scale", type=float, default=1.0, help="multiply every iteration count")
    p.add_argument("--filter", help="only cases whose group.name contains this text")
    p.add_argument("--no-lua", action="store_true", help="skip the pure Lua workloads")
    p.add_argument("--timeout", type=int, default=1200, help="seconds per benchmark process")
    p.add_argument("--define", action="append", default=[],
                   help="extra C# symbol for xLua and the benchmark in every mode, e.g. XLUA_CHECK_OBJECTS (repeatable)")
    p.add_argument("--mono", default="mono")
    p.add_argument("--compiler", default="mcs")
    p.add_argument("--mono-api", type=Path, help="the Mono assemblies the csc modes compile against")
    a = p.parse_args()
    if a.compare:
        compare(*a.compare)
        return
    if not a.output:
        p.error("--output is required")
    modes = [m for m in a.modes.split(",") if m]
    for m in modes:
        if m not in MODES:
            p.error("unknown mode " + m)
    out = a.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    plugins = [(label, path.resolve()) for label, path in a.plugin]
    data = {"schema": 1, "created": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
            "commit": commit(), "host": host_info(a),
            "settings": {"modes": modes, "runs": a.runs, "warmup": a.warmup, "scale": a.scale, "filter": a.filter},
            "runtimes": {}, "results": [], "errors": [], "sizes": [], "players": []}
    if plugins:
        exes = build(a, out, modes)
        for label, plugin in plugins:
            rt = None
            for i, mode in enumerate(modes):
                groups = ["lua_cs", "cs_lua", "table", "env"]
                if i == 0 and not a.no_lua:
                    groups.append("lua")  # the pure Lua cases do not depend on the bridge mode
                meta, results, errors = run_bench(a, out, exes[mode], mode, label, plugin, groups)
                if rt is None:
                    rt = {"plugin": str(plugin), "lua_version": meta["lua_version"], "jit_version": meta["jit_version"],
                          "init_lua_kb": meta["init_lua_kb"], "clr": meta["runtime"],
                          "managed_alloc_counter": meta["managed_alloc_counter"]}
                data["results"] += results
                data["errors"] += errors
            data["runtimes"][label] = rt
    data["sizes"] = [size_entry(label, path.resolve()) for label, path in plugins + a.size]
    data["players"] = [player_entry(label, path.resolve()) for label, path in a.player]
    result = (a.json or out / "benchmark.json").resolve()
    result.write_text(json.dumps(data, indent=1) + "\n")
    print_tables(data)
    print("\nWrote", result)


if __name__ == "__main__":
    main()
