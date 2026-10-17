#!/usr/bin/env python3
"""Compare the binding surface of the legacy generator with the source generator's.

    python3 Test/Native/surface_diff.py LEGACY.json SOURCE [--allow FILE]

LEGACY.json is what XLuaGenerate writes when XLUA_GEN_MANIFEST names a file
(Generator.cs BindingSurface); SOURCE is the source generator's manifest, as
JSON or as the generated XLua.Manifest.g.cs. run_managed.py --gen-code
--source-gen runs this for the suite's configuration
(docs/design/source-generator.md, section 8, "Binding surface diff").

Both list, per wrapped type, the constructors and the method overloads in the
order the wrapper tries them, the getters, setters, operators, indexers,
events and lazy (DoNotGen) members, the enum values, and the extension methods
of the extension method map. The order of overloads, constructors and
indexers is compared as is (it decides which overload a call takes); the
other lists are compared as sets.

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
            if field == "type":
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


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("legacy")
    p.add_argument("source")
    p.add_argument("--allow", help="JSON list of documented differences")
    a = p.parse_args()
    legacy, legacy_ext = normalise(load(a.legacy))
    source, source_ext = normalise(load(a.source))
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

    def is_allowed(d):
        return any(x.get("type") == d["type"] and x.get("field") == d["field"] and x.get("value", d["value"]) == d["value"] for x in allowed)

    unexpected = [d for d in differences if not is_allowed(d)]
    for d in differences:
        print(("allowed:    " if is_allowed(d) else "DIFFERENCE: ") + json.dumps(d, ensure_ascii=False))
    members = sum(len(t.get("methods", {})) + len(t.get("getters", [])) + len(t.get("setters", [])) + len(t.get("events", []))
                  + len(t.get("fields", [])) for t in legacy.values())
    if unexpected:
        print("XLUA_SURFACE_DIFF_FAIL: %d differences in %d types" % (len(unexpected), len(legacy)))
        return 1
    print("XLUA_SURFACE_DIFF_PASS: %d types, %d members, %d extension methods equal (%d allowed differences)"
          % (len(legacy), members, len(legacy_ext), len(differences)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
