"""Test sources as Roslyn compiles them (coverage_cs_dotnet.py, run_managed.py --source-gen,
luacats_check.py --source-gen).

Test fixtures written for mcs, which compiles the `in` keyword as by-value and
has no `ref readonly`, spell out the attributes Roslyn emits, and Roslyn
rejects those attributes in source (CS8335). Roslyn builds compile copies with
the keywords instead, which give the same metadata. Every replacement must
match, so a change to the fixtures fails here first.
"""
from pathlib import Path

ROSLYN_FIXTURES = {
    "Test/UnitTest/xLuaTest/LuaCallCS/InParamsForLuaCallCS.cs": [
        ("namespace System.Runtime.CompilerServices\n{\n    [AttributeUsage(AttributeTargets.Parameter)]\n"
         "    sealed class RequiresLocationAttribute : Attribute\n    {\n    }\n}\n", "", 1),
        ("[In, IsReadOnly] ref ", "in ", 5),
        ("[In, RequiresLocation] ref ", "ref readonly ", 10),
        ("int this[/*in*/ int key]", "int this[in int key]", 1),
    ],
    "Test/LuaCATS/Sample.cs": [
        ("namespace System.Runtime.CompilerServices\n{\n    [AttributeUsage(AttributeTargets.Parameter)]\n"
         "    sealed class RequiresLocationAttribute : Attribute\n    {\n    }\n}\n", "", 1),
        ("[In, IsReadOnly] ref ", "in ", 4),
        ("[In, RequiresLocation] ref ", "ref readonly ", 4),
    ],
    "Test/UnitTest/xLuaTest/LuaCallCS/UpstreamForLuaCallCS.cs": [
        ("[In, System.Runtime.CompilerServices.IsReadOnly, Optional, DefaultParameterValue(5)] ref int x",
         "in int x = 5", 1),
    ],
}


def roslyn_sources(files, repo, folder):
    """files with the fixtures above replaced by rewritten copies in folder."""
    result = []
    for f in files:
        f = Path(f)
        rel = str(f.relative_to(repo)) if f.is_relative_to(repo) else None
        if rel not in ROSLYN_FIXTURES:
            result.append(f)
            continue
        text = f.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
        for old, new, count in ROSLYN_FIXTURES[rel]:
            if text.count(old) != count:
                raise SystemExit("%s: expected %d of %r, found %d" % (rel, count, old, text.count(old)))
            text = text.replace(old, new)
        copy = Path(folder) / f.name
        copy.parent.mkdir(parents=True, exist_ok=True)
        copy.write_text(text, encoding="utf-8")
        result.append(copy)
    return result
