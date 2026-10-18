import json, sys
d = json.load(open(sys.argv[1]))
N = {"game": 4308, "small": 160}   # Lua functions in the subset (SPIKE001)
NO = {"game": 5270, "small": 219}  # overloads
P = {}
for p in d["players"]:
    P[(p["config"], p["variant"])] = p

def comp(p, key, field):
    c = p["components"].get(key)
    return c[field] if c else 0

modes = [("plain", "ng-5.5+pkg+sg+sub"), ("gcm", "ng-5.5+pkg+sg+gcm+sub"), ("desc", "ng-5.5+pkg+sg+gcm+desc")]
rows = {}
for strip in ("minimal", "high"):
    for size in ("small", "game"):
        cfg = "spike-%s-%s" % (size, strip)
        base = P[(cfg, "baseline+game" if size == "game" else "baseline")]
        for m, v in modes:
            p = P[(cfg, v + ("+game" if size == "game" else ""))]
            r = {"apk": p["bytes"] - base["bytes"]}
            for k, name in (("arm64-v8a/libil2cpp.so", "il2cpp"), ("global-metadata.dat", "meta"), ("arm64-v8a/libunity.so", "unity")):
                r[name + "_z"] = comp(p, k, "stored") - comp(base, k, "stored")
                r[name + "_u"] = comp(p, k, "bytes") - comp(base, k, "bytes")
            ms = p.get("managed_stripped", {})
            r["asm_cs"] = ms.get("Assembly-CSharp.dll")
            r["xlua"] = ms.get("XLua.dll")
            rows[(strip, size, m)] = r
            print("%-8s %-6s %-6s apk %+10d  il2cpp %+9d / %+10d  meta %+8d / %+9d  unity %+8d  Asm-CS %s XLua %s" % (
                strip, size, m, r["apk"], r["il2cpp_z"], r["il2cpp_u"], r["meta_z"], r["meta_u"], r["unity_z"], r["asm_cs"], r["xlua"]))
print()
print("slope per Lua function (game - small)/(%d - %d), and intercept = small - 160 * slope" % (N["game"], N["small"]))
for strip in ("minimal", "high"):
    for m, _ in modes:
        g, s = rows[(strip, "game", m)], rows[(strip, "small", m)]
        out = []
        for k in ("apk", "il2cpp_u", "meta_u"):
            slope = (g[k] - s[k]) / float(N["game"] - N["small"])
            icpt = s[k] - N["small"] * slope
            out.append("%s slope %.1f B, intercept %+.0f" % (k, slope, icpt))
        print("%-8s %-6s " % (strip, m) + "; ".join(out))
print()
for strip in ("minimal", "high"):
    for size in ("small", "game"):
        g, c, pl = rows[(strip, size, "desc")], rows[(strip, size, "gcm")], rows[(strip, size, "plain")]
        print("%-8s %-6s desc-gcm apk %+d (%.1f%% of gcm delta), il2cpp %+d/%+d, meta %+d/%+d; gcm-plain apk %+d" % (
            strip, size, g["apk"] - c["apk"], 100.0 * (g["apk"] - c["apk"]) / c["apk"], g["il2cpp_z"] - c["il2cpp_z"], g["il2cpp_u"] - c["il2cpp_u"],
            g["meta_z"] - c["meta_z"], g["meta_u"] - c["meta_u"], c["apk"] - pl["apk"]))
# break-even: N where desc == gcm
print()
for strip in ("minimal", "high"):
    g = {m: rows[(strip, "game", m)]["apk"] for m, _ in modes}
    s = {m: rows[(strip, "small", m)]["apk"] for m, _ in modes}
    sl = {m: (g[m] - s[m]) / float(N["game"] - N["small"]) for m in g}
    ic = {m: s[m] - N["small"] * sl[m] for m in g}
    be = (ic["desc"] - ic["gcm"]) / (sl["gcm"] - sl["desc"])
    print("%s: break-even desc vs gcm at %.0f Lua functions (APK)" % (strip, be))
