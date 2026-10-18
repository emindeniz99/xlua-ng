#!/usr/bin/env python3
"""Compare the source generator's binding surface with an expected one.

    python3 Test/Native/surface_diff.py EXPECTED.json SOURCE [--allow FILE]
    python3 Test/Native/surface_diff.py --write EXPECTED.json SOURCE

EXPECTED.json is a binding surface in the manifest's format: for the suite
(Test/Native/expected-surface/suite.json) and the migration tool's cases
(Test/Native/MigrationSample/expected/), the surface that the legacy generator
bound for the same configuration, which XLuaGenerate wrote with
XLUA_GEN_MANIFEST until the legacy generator was removed (source generator
milestone M11); "legacy" in the messages below means that file. SOURCE is the
source generator's manifest, as JSON or as the generated XLua.Manifest.g.cs.
run_managed.py --source-gen runs this for the suite's configuration
(docs/design/source-generator.md, section 8, "Binding surface diff"). --write
writes SOURCE's manifest as EXPECTED.json, for a fixture change that changes
the surface on purpose; review that diff.

Both list, per wrapped type, the constructors and the method overloads in the
order the wrapper tries them, the getters, setters, operators, indexers,
events and lazy (DoNotGen) members, the enum values, and the extension methods
of the extension method map. The order of overloads, constructors and
indexers is compared as is (it decides which overload a call takes); the
other lists are compared as sets.

Both also list the bridges of the [CSharpCallLua] types (the bridge set
diff, milestone M3): each delegate bridge by its SignatureKey with the
delegate types it serves, and each interface bridge with the members it
implements. A delegate type must have the same key in both, and the delegate
types of one key must be the same, so the signature folding is equal too. The
legacy groups that serve only hotfix signatures are counted, not compared:
the source generator writes hotfix bridges from milestone M7. Bridge
differences use the field names "delegateBridges" and "interfaceBridges" in
the allow list. The signatures of the legacy hotfix bridges are compared with
the source generator's per-assembly HotfixBridges ("hotfixBridges", M7).

Both list the GCOptimize types too (the struct-push diff, milestone M4): per
type its kind (value, enum or table), whether wrappers push it without
boxing, the OptimizeFlag, the packed size, the fields in packing order and
the float groups; and the types that get Pack/UnPack, with the configured
ones marked root. These differences use the field names "structs" and
"packUnpack".

Members the source generator leaves to reflection (the "fallback" lists,
milestone M5) are printed and counted; the legacy generator drops them.

--allow names a JSON list of documented differences, each
{"type": ..., "field": ..., "value": ..., "reason": ...}; a difference that
matches one is reported as allowed. Exits with 1 on any other difference.
"""
import argparse
import json
from pathlib import Path
import re
import sys

ORDERED = {"constructors", "indexers", "newindexers"}


def load(path):
    text = Path(path).read_text(encoding="utf-8")
    if path.endswith(".cs"):
        found = re.search(r'internal const string Json = @"((?:[^"]|"")*)";', text)
        if not found:
            raise SystemExit(path + ": no manifest constant")
        text = found.group(1).replace('""', '"')
    return json.loads(text)


def normalise(manifest):
    """{type: {field: value}} with order-free lists sorted."""
    types = {}
    for t in manifest["types"]:
        entry = {}
        for field, value in t.items():
            # Members the source generator leaves to reflection (milestone M5);
            # the legacy generator drops them, so they are listed, not compared.
            if field in ("type", "fallback"):
                continue
            if field == "methods":
                value = {("static " if m["static"] else "") + m["name"]: m["overloads"] for m in value}
            elif field == "operators":
                value = {o["name"]: o["overloads"] for o in value}
            elif isinstance(value, list) and field not in ORDERED and field != "fields":
                value = sorted(value)
            entry[field] = value
        types[t["type"]] = entry
    return types, sorted(manifest["extensionMethods"])


def bridges(manifest):
    """({delegate type: key}, number of legacy hotfix-only bridges, {interface: {field: sorted list}}), or None."""
    if "delegateBridges" not in manifest or "interfaceBridges" not in manifest:
        return None
    keys = {}
    hotfix_only = 0
    for g in manifest["delegateBridges"]:
        if not g["delegates"]:
            hotfix_only += 1
        for d in g["delegates"]:
            keys[d] = g["key"]
    interfaces = {i["type"]: {f: sorted(v) for f, v in i.items() if f != "type"} for i in manifest["interfaceBridges"]}
    return keys, hotfix_only, interfaces


def write(expected, source):
    manifest = load(source)
    manifest = dict([("generator", "the xLua source generator's manifest, written by surface_diff.py --write")]
                    + [(k, v) for k, v in manifest.items() if k != "generator"])
    Path(expected).write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print("wrote " + expected)
    return 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("legacy", metavar="EXPECTED")
    p.add_argument("source")
    p.add_argument("--allow", help="JSON list of documented differences")
    p.add_argument("--write", action="store_true", help="write SOURCE's manifest to EXPECTED instead of comparing")
    a = p.parse_args()
    if a.write:
        return write(a.legacy, a.source)
    legacy_manifest, source_manifest = load(a.legacy), load(a.source)
    legacy, legacy_ext = normalise(legacy_manifest)
    source, source_ext = normalise(source_manifest)
    allowed = json.loads(Path(a.allow).read_text()) if a.allow else []

    differences = []

    def differ(type_name, field, value, legacy_value, source_value):
        differences.append({"type": type_name, "field": field, "value": value, "legacy": legacy_value, "source": source_value})

    for name in sorted(set(legacy) | set(source)):
        if name not in source:
            differ(name, "type", name, "wrapped", "not wrapped")
            continue
        if name not in legacy:
            differ(name, "type", name, "not wrapped", "wrapped")
            continue
        l, s = legacy[name], source[name]
        for field in sorted(set(l) | set(s)):
            lv, sv = l.get(field), s.get(field)
            if lv == sv:
                continue
            if isinstance(lv, dict) and isinstance(sv, dict):
                for key in sorted(set(lv) | set(sv)):
                    if lv.get(key) != sv.get(key):
                        differ(name, field, key, lv.get(key), sv.get(key))
            elif isinstance(lv, list) and isinstance(sv, list) and field not in ORDERED and field != "fields":
                for v in sorted(set(lv) - set(sv)):
                    differ(name, field, v, "present", "missing")
                for v in sorted(set(sv) - set(lv)):
                    differ(name, field, v, "missing", "present")
            else:
                differ(name, field, None, lv, sv)
    for v in sorted(set(legacy_ext) - set(source_ext)):
        differ("", "extensionMethods", v, "present", "missing")
    for v in sorted(set(source_ext) - set(legacy_ext)):
        differ("", "extensionMethods", v, "missing", "present")

    legacy_bridges, source_bridges = bridges(legacy_manifest), bridges(source_manifest)
    if legacy_bridges and source_bridges:
        (lk, hotfix_only, li), (sk, _, si) = legacy_bridges, source_bridges
        for d in sorted(set(lk) | set(sk)):
            if lk.get(d) != sk.get(d):
                differ(d, "delegateBridges", d, lk.get(d, "no bridge"), sk.get(d, "no bridge"))
        for name in sorted(set(li) | set(si)):
            if name not in si or name not in li:
                differ(name, "interfaceBridges", name, "bridged" if name in li else "no bridge", "bridged" if name in si else "no bridge")
                continue
            for field in sorted(set(li[name]) | set(si[name])):
                lv, sv = li[name].get(field, []), si[name].get(field, [])
                for v in sorted(set(lv) - set(sv)):
                    differ(name, "interfaceBridges", field + " " + v, "present", "missing")
                for v in sorted(set(sv) - set(lv)):
                    differ(name, "interfaceBridges", field + " " + v, "missing", "present")

    # Hotfix bridges (milestone M7): the legacy groups that serve hotfix
    # signatures against the source generator's HotfixBridges, by SignatureKey.
    have_hotfix = "delegateBridges" in legacy_manifest and "hotfixBridges" in source_manifest
    if have_hotfix:
        lh = {g["key"] for g in legacy_manifest["delegateBridges"] if g.get("hotfix", 0) > 0}
        sh = set(source_manifest["hotfixBridges"])
        for v in sorted(lh - sh):
            differ("", "hotfixBridges", v, "present", "missing")
        for v in sorted(sh - lh):
            differ("", "hotfixBridges", v, "missing", "present")

    have_structs = "structs" in legacy_manifest and "structs" in source_manifest
    if have_structs:
        ls = {t["type"]: t for t in legacy_manifest["structs"]}
        ss = {t["type"]: t for t in source_manifest["structs"]}
        for name in sorted(set(ls) | set(ss)):
            if name not in ls or name not in ss:
                differ(name, "structs", name, "listed" if name in ls else "not listed", "listed" if name in ss else "not listed")
                continue
            for field in sorted(set(ls[name]) | set(ss[name])):
                if ls[name].get(field) != ss[name].get(field):
                    differ(name, "structs", field, ls[name].get(field), ss[name].get(field))
        lp, sp = set(legacy_manifest["packUnpack"]), set(source_manifest["packUnpack"])
        for v in sorted(lp - sp):
            differ("", "packUnpack", v, "present", "missing")
        for v in sorted(sp - lp):
            differ("", "packUnpack", v, "missing", "present")

    def is_allowed(d):
        return any(x.get("type") == d["type"] and x.get("field") == d["field"] and x.get("value", d["value"]) == d["value"] for x in allowed)

    fallbacks = sorted(t["type"] + ": " + f for t in source_manifest["types"] for f in t.get("fallback", []))
    for f in fallbacks:
        print("fallback:   " + f)
    unexpected = [d for d in differences if not is_allowed(d)]
    for d in differences:
        print(("allowed:    " if is_allowed(d) else "DIFFERENCE: ") + json.dumps(d, ensure_ascii=False))
    members = sum(len(t.get("methods", {})) + len(t.get("getters", [])) + len(t.get("setters", [])) + len(t.get("events", []))
                  + len(t.get("fields", [])) for t in legacy.values())
    if unexpected:
        print("XLUA_SURFACE_DIFF_FAIL: %d differences in %d types" % (len(unexpected), len(legacy)))
        return 1
    print("XLUA_SURFACE_DIFF_PASS: %d types, %d members, %d extension methods equal (%d allowed differences, "
          "%d members left to reflection that the legacy generator drops)"
          % (len(legacy), members, len(legacy_ext), len(differences), len(fallbacks)))
    if legacy_bridges and source_bridges:
        print("XLUA_BRIDGE_DIFF_PASS: %d delegate types on %d bridges and %d interface bridges equal "
              "(%d legacy bridges serve only hotfix signatures, milestone M7)"
              % (len(lk), len(set(lk.values())), len(li), hotfix_only))
    else:
        print("bridge set diff skipped: a manifest without delegateBridges and interfaceBridges")
    if have_hotfix:
        print("XLUA_HOTFIX_BRIDGE_DIFF_PASS: %d hotfix bridge signatures equal" % len(lh))
    if have_structs:
        print("XLUA_STRUCT_DIFF_PASS: %d GCOptimize types (%d pushed without boxing) and %d packed types equal"
              % (len(ls), sum(1 for t in ls.values() if t["pushed"]), len(lp)))
    else:
        print("struct-push diff skipped: a manifest without structs")
    return 0


if __name__ == "__main__":
    sys.exit(main())
