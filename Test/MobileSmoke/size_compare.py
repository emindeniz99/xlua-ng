"""Compare pairs of Android players from a size_report.py work directory.

For each --pair LABEL=A:B (project names under WORK/projects, players under WORK/players/android/CONFIG)
it prints the APK delta, the libil2cpp.so, global-metadata.dat and libxlua.so deltas (compressed in the
APK / uncompressed), and size_breakdown.py's split of the libil2cpp.dbg.so symbols that A adds over B:
xLua's own C# by file and the class library by family (LINQ, collections ...).

--generics PROJECT prints how much of that project's libil2cpp.so is generic code: the symbols defined in
the generated Generics*.cpp and GenericMethods*.cpp files, split into shared bodies (IL2CPP names them
"..._gshared": one body for several instances, such as every reference-type instance, or with full
generic sharing every instance) and the rest (mostly 8-byte stubs that jump to a shared body).

--linq PROJECT reports whether System.Linq is in the player: the size of the stripped System.Core.dll that
IL2CPP compiled and the System.Linq symbols left in libil2cpp.dbg.so.

Needs ANDROID_NDK for llvm-nm and llvm-readelf, as size_breakdown.py does.
"""
import argparse
import collections
import json
from pathlib import Path
import re
import sys

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here))
import size_breakdown as sb  # noqa: E402
import size_report as sr  # noqa: E402

OUT = "Library/Bee/artifacts/Android/il2cppOutput"


def player_dir(work, project, config):
    variant = project[len("android-"):]
    # Project directories use "-" where variant names use "+": find the player whose project name matches.
    for d in (work / "players/android" / config).iterdir():
        if "android-" + d.name.replace("+", "-") == project:
            return d
    sys.exit("no player for %s under %s" % (project, work / "players/android" / config))


def comp(entry, key):
    c = entry["components"].get(key)
    return (c["stored"], c["bytes"]) if c else (0, 0)


def fmt(n):
    return "{:+,}".format(n)


def pair(work, config, label, a, b):
    pa, pb = sr.measure_player(player_dir(work, a, config)), sr.measure_player(player_dir(work, b, config))
    print("### %s: %s against %s\n" % (label, a, b))
    print("| part | compressed in APK | uncompressed |")
    print("| --- | --- | --- |")
    print("| APK | %s | |" % fmt(pa["bytes"] - pb["bytes"]))
    for key in ["arm64-v8a/libil2cpp.so", "global-metadata.dat", "arm64-v8a/libxlua.so", "arm64-v8a/libunity.so"]:
        (sa, ua), (sb_, ub) = comp(pa, key), comp(pb, key)
        print("| %s | %s | %s |" % (key, fmt(sa - sb_), fmt(ua - ub)))
    r, owner, order = sb.il2cpp(work, a, b)
    # sb.il2cpp names symbols from A's generated C++ only, so code that only B has (what a change removed)
    # would count as tables. Name it from B's C++ as well and classify again.
    owner_b, order_b = sb.cpp_definitions(work / "projects" / b / OUT / "cpp")
    merged = dict(owner_b)
    merged.update(owner)
    dbg = lambda p: next((work / "projects" / p / OUT / "build").glob("*/libil2cpp.dbg.so"))
    r.update(sb.classify(sb.nm_sizes(dbg(a)), sb.nm_sizes(dbg(b)), merged, order_b + order))
    print("\nlibil2cpp.so symbols A adds over B (stripped file delta %s):\n" % fmt(r["stripped_delta"]))
    print("| owner | bytes |")
    print("| --- | --- |")
    xlua = sum(v for k, v in r["files"].items())
    for k, v in r["files"].items():
        print("| %s | %s |" % (k, fmt(v)))
    print("| **xLua's own C# (sum)** | **%s** |" % fmt(xlua))
    for k, v in r["bcl"].items():
        print("| class library: %s | %s |" % (k, fmt(v["bytes"])))
    print("| tables and data | %s |" % fmt(r["tables"]))
    print()
    return {"label": label, "a": a, "b": b, "apk": pa["bytes"] - pb["bytes"], "il2cpp": r}


def generics(work, project):
    cpp = work / "projects" / project / OUT / "cpp"
    owner, _ = sb.cpp_definitions(cpp)
    dbg = next((work / "projects" / project / OUT / "build").glob("*/libil2cpp.dbg.so"))
    sizes = sb.nm_sizes(dbg)
    by = collections.Counter()
    counts = collections.Counter()
    for sym, n in sizes.items():
        where = owner.get(sym)
        kind = "generic" if where in ("Generics", "GenericMethods") else "other C#" if where else "runtime and tables"
        shared = "FullySharedGeneric" in sym or "_gshared" in sym
        by[kind + (", shared" if kind == "generic" and shared else "")] += n
        counts[kind + (", shared" if kind == "generic" and shared else "")] += 1
    text = "".join(f.read_text(errors="replace") for f in cpp.glob("Generics*.cpp"))
    full = len(re.findall(r"Il2CppFullySharedGenericAny", text))
    print("### generic code in %s\n" % project)
    print("| symbols | count | bytes |")
    print("| --- | --- | --- |")
    for k in sorted(by):
        print("| %s | %s | %s |" % (k, "{:,}".format(counts[k]), "{:,}".format(by[k])))
    print("\nGenerics*.cpp: %s bytes of C++, %d uses of Il2CppFullySharedGenericAny\n" %
          ("{:,}".format(len(text)), full))


def linq(work, project, config):
    info = json.loads((player_dir(work, project, config) / "build-info.json").read_text())
    core = info.get("managed_stripped", {}).get("System.Core.dll")
    dbg = next((work / "projects" / project / OUT / "build").glob("*/libil2cpp.dbg.so"))
    cpp = work / "projects" / project / OUT / "cpp"
    owner, _ = sb.cpp_definitions(cpp)
    sizes = sb.nm_sizes(dbg)
    linq_syms = {s: n for s, n in sizes.items() if re.match(r"(Enumerable|OrderedEnumerable|EnumerableSorter|Lookup_2|Grouping_2|"
                                                          r"\w*Iterator_\d|\w*Iterator\w*_\d)", s) and owner.get(s) != "Assembly-CSharp"}
    files = sorted(f.name for f in cpp.glob("System.Core*.cpp"))
    print("### System.Linq in %s\n" % project)
    print("- stripped System.Core.dll: %s bytes" % ("{:,}".format(core) if core else "not in the build"))
    print("- generated System.Core C++ files: %s" % (", ".join(files) or "none"))
    print("- LINQ-looking symbols in libil2cpp.dbg.so: %d, %s bytes; largest: %s\n" % (
        len(linq_syms), "{:,}".format(sum(linq_syms.values())),
        ", ".join(s for s, _ in sorted(linq_syms.items(), key=lambda kv: -kv[1])[:5])))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--work", required=True, type=Path)
    p.add_argument("--config", default="default")
    p.add_argument("--pair", action="append", default=[], metavar="LABEL=A:B")
    p.add_argument("--generics", action="append", default=[], metavar="PROJECT")
    p.add_argument("--linq", action="append", default=[], metavar="PROJECT")
    p.add_argument("--json", type=Path, help="also write the pair results here")
    a = p.parse_args()
    work = a.work.resolve()
    results = []
    for spec in a.pair:
        label, _, ab = spec.partition("=")
        x, _, y = ab.partition(":")
        results.append(pair(work, a.config, label, x, y))
    for project in a.generics:
        generics(work, project)
    for project in a.linq:
        linq(work, project, a.config)
    if a.json:
        a.json.write_text(json.dumps(results, indent=1) + "\n")


if __name__ == "__main__":
    main()
