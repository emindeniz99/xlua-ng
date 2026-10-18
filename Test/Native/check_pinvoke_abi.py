"""Check that every native import in the managed sources has the C function's signature.

    python3 Test/Native/check_pinvoke_abi.py

Each parameter and result is reduced to the class a WebAssembly signature uses:
pointer-sized, 32-bit integer, 64-bit integer, float, double or void. The C#
declaration (all #if branches of Core/LuaDLL.cs, LuaDLLBridge.cs and DelegateBridge.cs) must give the
same classes as the C definition in the native sources (xlua_core*.c, xlua.c, i64lib.c,
memory_leak_checker.c, Assets/Plugins/iOS/HotfixFlags.cpp) or the
Lua 5.3.6, 5.4.9 and 5.5.1 headers.

Native ABIs tolerate some mismatches, such as an ignored result or an int where
C has size_t. WebAssembly does not: the IL2CPP declaration and the C definition
are linked by signature, and a mismatched call traps. On wasm32 a pointer, a
size_t and an int are all i32, so a pointer/int mismatch only shows up with
WebAssembly64 (Memory64), where pointers and size_t are i64. LuaJIT-only
definitions (LUA_VERSION_NUM == 501) are not checked; LuaJIT has no WebGL build.
"""
from pathlib import Path
import re
import sys

REPO = Path(__file__).resolve().parents[2]
LUA_VERSIONS = ["5.3.6", "5.4.9", "5.5.1"]

CS_TYPES = {
    "IntPtr": "ptr", "UIntPtr": "ptr", "string": "ptr", "byte[]": "ptr", "lua_CSFunction": "ptr",
    "int": "i32", "uint": "i32", "bool": "i32", "byte": "i32", "sbyte": "i32", "short": "i32",
    "ushort": "i32", "LuaTypes": "i32", "LuaGCOptions": "i32", "long": "i64", "ulong": "i64",
    "double": "f64", "float": "f32", "void": "void",
}
C_TYPES = {
    "int": "i32", "unsigned int": "i32", "unsigned": "i32", "uint32_t": "i32", "int32_t": "i32",
    "int8_t": "i32", "int16_t": "i32", "uint8_t": "i32", "uint16_t": "i32", "char": "i32", "bool": "i32",
    "int64_t": "i64", "uint64_t": "i64", "lua_Integer": "i64", "lua_Unsigned": "i64",
    "lua_Number": "f64", "double": "f64", "float": "f32", "void": "void",
    # pointer-sized: size_t, intptr_t and long are 64-bit on wasm64 (LP64)
    "size_t": "ptr", "ptrdiff_t": "ptr", "intptr_t": "ptr", "uintptr_t": "ptr", "long": "ptr",
    "unsigned long": "ptr", "lua_KContext": "ptr", "lua_CFunction": "ptr", "lua_KFunction": "ptr",
    "lua_Alloc": "ptr", "lua_Reader": "ptr", "lua_Writer": "ptr", "lua_WarnFunction": "ptr",
    "lua_Hook": "ptr", "lua_CSWrapperCaller": "ptr", "TableSizeReport": "ptr",
    "ObjectRelationshipReport": "ptr", "xlua_core_resolver": "ptr",
}
CS_IMPORT = re.compile(r'\[DllImport\(([^)]*)\)\]\s*(?://[^\n]*\s*)*(?:\[[^\]]+\]\s*)*'
                       r'(?:public|internal|private)?\s*static extern ([\w\[\]]+)\s+(\w+)\s*\(([^)]*)\)')
# A result type ends in a space or '*' ("const char *(lua_pushstring) (...)", "int xlua_gc(...)"),
# so a call such as "lua_pop(L, 1);" is not read as a declaration.
C_FUNCTION = re.compile(r'(?:LUA_API|LUALIB_API|LUAMOD_API|LUASOCKET_API|^)[ \t]*(\w[\w \t]*?[ \t*])'
                        r'\(?[ \t]*(\w+)[ \t]*\)?[ \t]*\(([^()]*)\)\s*[{;]', re.M)


def cs_class(text):
    text = re.sub(r"^\[[^\]]*\]\s*", "", text.strip())  # parameter attributes such as [In]
    if text.startswith(("out ", "ref ", "in ")):
        return "ptr"
    text = re.sub(r"\s+\w+$", "", text) if " " in text else text  # drop the parameter name
    return CS_TYPES.get(text, "unknown C# type " + text)


def managed_imports():
    imports = {}
    for name in ["Core/LuaDLL.cs", "Src/LuaDLLBridge.cs", "Src/DelegateBridge.cs"]:
        text = (REPO / "Assets/XLua" / name).read_text(encoding="utf-8")
        for attribute, result, method, params in CS_IMPORT.findall(text):
            entry = re.search(r'EntryPoint\s*=\s*"([^"]+)"', attribute)
            signature = (cs_class(result), tuple(cs_class(p) for p in params.split(",") if p.strip()))
            imports.setdefault(entry.group(1) if entry else method, {}).setdefault(signature, method)
    return imports


def c_class(text):
    text = re.sub(r"\b(const|volatile|struct|static|inline|extern)\b", " ", text)
    if "*" in text or "[" in text:
        return "ptr"
    words = text.split()
    # drop the parameter name; a type of several words ("unsigned int") stays whole
    if len(words) > 1 and words[-1] not in C_TYPES and " ".join(words) not in C_TYPES:
        words = words[:-1]
    return C_TYPES.get(" ".join(words), "unknown C type " + " ".join(words))


def without_luajit_blocks(text):
    """Blank out #if LUA_VERSION_NUM == 501 / #if defined(USING_LUAJIT) branches (LuaJIT only)."""
    out, stack = [], []  # stack of "skipping this branch" flags
    for line in text.splitlines():
        directive = re.match(r"\s*#\s*(if|ifdef|ifndef|elif|else|endif)\b(.*)", line)
        if directive:
            kind, rest = directive.groups()
            if kind in ("if", "ifdef", "ifndef"):
                stack.append(bool(re.match(r"\s*(LUA_VERSION_NUM\s*==\s*501|USING_LUAJIT|defined\s*\(\s*USING_LUAJIT\s*\))(?!\w)", rest)))
            elif kind in ("elif", "else") and stack:
                stack[-1] = False
            elif kind == "endif" and stack:
                stack.pop()
            out.append("")
        else:
            out.append("" if any(stack) else line)
    return "\n".join(out)


def c_prototypes(lua_version):
    sources = [REPO / "build" / name for name in ["xlua_core.c", "xlua_core_limits.c", "xlua_core_sandbox.c", "xlua_core_buffer.c", "xlua.c", "i64lib.c", "memory_leak_checker.c"]]
    sources += [REPO / "Assets/Plugins/iOS/HotfixFlags.cpp"]
    sources += [REPO / "build" / ("lua-" + lua_version) / "src" / name for name in ["lua.h", "lauxlib.h", "lualib.h"]]
    prototypes = {}
    for source in sources:
        text = source.read_text(encoding="utf-8", errors="replace")
        text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
        text = re.sub(r"//[^\n]*", "", text)
        text = without_luajit_blocks(text)
        for result, name, params in C_FUNCTION.findall(text):
            if re.search(r"\b(return|if|while|for|switch|sizeof|define|else|case|goto)\b", result):
                continue
            params = params.strip()
            classes = () if params in ("", "void") else tuple(
                "..." if p.strip() == "..." else c_class(p) for p in params.split(","))
            prototypes.setdefault(name, (c_class(result), classes, source.name))
    # PACK_UNPACK_OF(type) in xlua.c: int xlua_pack_<type>(void *p, int offset, type field)
    # and int xlua_unpack_<type>(void *p, int offset, type *pfield)
    for t in re.findall(r"^PACK_UNPACK_OF\((\w+)\)", (REPO / "build/xlua.c").read_text(), re.M):
        prototypes["xlua_pack_" + t] = ("i32", ("ptr", "i32", C_TYPES[t]), "xlua.c")
        prototypes["xlua_unpack_" + t] = ("i32", ("ptr", "i32", "ptr"), "xlua.c")
    return prototypes


def main():
    imports = managed_imports()
    assert len(imports) > 100, "found only %d imports" % len(imports)
    errors = []
    for lua_version in LUA_VERSIONS:
        prototypes = c_prototypes(lua_version)
        for name, signatures in sorted(imports.items()):
            if name not in prototypes:
                errors.append("%s: no C definition found (Lua %s)" % (name, lua_version))
                continue
            c_result, c_params, where = prototypes[name]
            for (result, params), method in signatures.items():
                if (result, params) != (c_result, c_params):
                    errors.append("%s (C# %s): C# %s(%s), C %s(%s) in %s (Lua %s)" % (
                        name, method, result, ", ".join(params), c_result, ", ".join(c_params), where, lua_version))
    if errors:
        print("\n".join(sorted(set(errors))))
        sys.exit("XLUA_PINVOKE_ABI_FAIL: %d mismatches" % len(set(errors)))
    print("XLUA_PINVOKE_ABI_PASS:", len(imports), "imports match their C signatures for Lua", ", ".join(LUA_VERSIONS))


if __name__ == "__main__":
    main()
