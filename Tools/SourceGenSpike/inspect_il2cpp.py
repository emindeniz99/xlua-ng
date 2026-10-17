#!/usr/bin/env python3
"""Static checks on an IL2CPP build: does the generated registration survive stripping?

usage: inspect_il2cpp.py <project dir> <player output dir>

Looks at RuntimeInitializeOnLoads.json, the stripped managed assemblies, the IL2CPP C++
output and global-metadata.dat, and prints one line per check.
"""
import json
import os
import sys

proj, out = sys.argv[1], sys.argv[2]


def walk(root, pred):
    for base, dirs, files in os.walk(root):
        for f in files:
            if pred(base, f):
                yield os.path.join(base, f)


def has(path, needle):
    with open(path, "rb") as f:
        return needle.encode() in f.read()


print("native libs:", [os.path.relpath(p, out) for p in walk(out, lambda b, f: f.endswith(".so") or f == "UnityFramework")][:6])

for p in walk(out, lambda b, f: f == "RuntimeInitializeOnLoads.json"):
    data = json.load(open(p))
    items = data.get("root", data if isinstance(data, list) else [])
    ours = [i for i in items if i.get("nameSpace") in ("XLuaTest.Generated", "Game")]
    print("RuntimeInitializeOnLoads.json:", os.path.relpath(p, out))
    for i in ours:
        print("  ", i["assemblyName"], i["nameSpace"] + "." + i["className"] + "." + i["methodName"], "loadTypes=%s" % i.get("loadTypes"))

stripped = [p for p in walk(os.path.join(proj, "Library", "Bee"), lambda b, f: f in ("Assembly-CSharp.dll", "UserGame.dll")
                                                                   and "ManagedStripped" in b)]
for p in stripped:
    print("stripped", os.path.relpath(p, proj))
    for n in ("XLuaGen_Assembly_CSharp", "XLuaGen_UserGame", "Register", "Invoke", "Answer", "OnlyReflected", "XLuaSGMarker", "GenericThing"):
        print("   %-24s %s" % (n, has(p, n)))

meta = list(walk(out, lambda b, f: f == "global-metadata.dat"))
for p in meta:
    print("global-metadata.dat", os.path.relpath(p, out))
    for n in ("XLuaGen_Assembly_CSharp", "XLuaGen_UserGame", "Answer", "OnlyReflected"):
        print("   %-24s %s" % (n, has(p, n)))

cpp_roots = [os.path.join(proj, "Library", "Bee"), out]
cpp = [p for r in cpp_roots for p in walk(r, lambda b, f: f.endswith(".cpp") and ("il2cppOutput" in b or "Il2CppOutput" in b))]
print("il2cpp cpp files:", len(cpp))
hits = {}
for p in cpp:
    with open(p, "rb") as f:
        d = f.read()
    for n in (b"XLuaGen_Assembly_CSharp_Register", b"XLuaGen_Assembly_CSharp_Invoke", b"XLuaGen_UserGame_Register",
              b"UserThing_Answer", b"UserThing_OnlyReflected", b"Registry_Register"):
        if n in d:
            hits.setdefault(n.decode(), []).append(os.path.basename(p))
for n in ("XLuaGen_Assembly_CSharp_Register", "XLuaGen_Assembly_CSharp_Invoke", "XLuaGen_UserGame_Register",
          "UserThing_Answer", "UserThing_OnlyReflected", "Registry_Register"):
    print("   cpp %-34s %s" % (n, sorted(set(hits.get(n, [])))[:3]))
