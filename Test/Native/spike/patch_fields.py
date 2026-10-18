import re, sys
root = sys.argv[1]
n = 0
for rel in ["Assets/XLua/Core/LuaOptions.cs", "Assets/XLua/Core/LuaException.cs"]:
    p = root + "/" + rel
    s = open(p).read()
    # { get; set; } and { get; internal/protected set; } -> a plain field; { get; } -> readonly field
    s, a = re.subn(r"public ([\w?<>.]+) (\w+) \{ get; (?:(?:internal|protected|private) )?set; \}", r"public \1 \2;", s)
    s, b = re.subn(r"public ([\w?<>.]+) (\w+) \{ get; \}", r"public readonly \1 \2;", s)
    s = s.replace("[field: NonSerialized]", "[NonSerialized]")
    n += a + b
    open(p, "w").write(s)
print("converted", n)
