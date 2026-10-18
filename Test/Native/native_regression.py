"""Regression checks for defects found by analyzing and fuzzing xLua's C code,
the C exports that no C# code calls, and the argument and error branches of
the exports the managed bridge calls (struct accessors, indexers, path access,
C# function wrappers).

Usage: python3 Test/Native/native_regression.py /absolute/path/libxlua.so [check ...]
Checks: parse, struct_offsets, struct_alignment, exported_helpers, value_helpers,
struct_accessors, struct_clone, indexers, path_access, csharp_functions, setfenv, uint_conversion,
integer_conversion, argerror, native_modules, luajit_int64, luajit_int64_helpers, luajit_int64_header,
checked_accessors, checked_cache, core_state, core_host_oom, memory_snapshot_tables,
memory_snapshot_userdata, memory_snapshot_functions, memory_snapshot_limits,
memory_snapshot_reentrancy, memory_snapshot_out_of_memory, memory_snapshot_core_limit, uint64_table,
std_libraries, library_parts, core_limits, core_watchdog_race, core_stop_latency_env, core_sandbox, core_sandbox_abi, core_lua_api (default: all). Works with Lua
5.3, 5.4, 5.5 and LuaJIT plugins (LuaJIT also checks int64).

A core library (XLUA_PRESET=core: no luaopen_xlua) runs the core_sandbox checks
only: the rules of LuaScript's sandbox (Test/Native/core_sandbox_cases.txt, the
cases core_smoke.c runs too) and the edges of its exports; core_lua_api, which
needs the Lua C API exported (XLUA_EXPORTS=all), checks the bytecode stubs and
states without the sandbox; core_trusted_bytecode checks xlua_core_call's mode
(XLUA_EXPECT_TRUSTED_BYTECODE). A bridge library built with XLUA_LUASCRIPT=ON
runs them after the bridge checks.

A bytecode-only library (XLUA_BYTECODE_ONLY: it refuses text chunks) runs the
checks of Test/Native/bytecode_only.py only, with chunks compiled by the library
XLUA_COMPILER_LIB names. XLUA_EXPECT_BYTECODE_ONLY=ON or OFF says which build it is.

The optional parts follow the build: XLUA_EXPECT_MEMORY_SNAPSHOT, XLUA_EXPECT_UINT64,
XLUA_EXPECT_NATIVE_MODULES and XLUA_EXPECT_LIBS say what it contains; unset, the
library's exports decide. XLUA_EXPECT_OS_FULL, XLUA_EXPECT_LOADLIB and XLUA_EXPECT_DOFILE
do the same for the parts of os, package and the base library (library_parts). Without the uint64 table (XLUA_UINT64=OFF, Lua 5.3+) the
checks that only need uint64.parse to make a 64-bit value get a Lua stand-in.
"""
import ctypes as C
import os
from pathlib import Path
import sys

lib = C.CDLL(str(Path(sys.argv[1]).resolve()))
P = C.c_void_p


def core_checks(names):
    """The core_sandbox checks of core_regression.py (next to this file)."""
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import core_regression
    return core_regression.main(lib, names)


# A core library (XLUA_PRESET=core) has none of the bridge.
if not hasattr(lib, "luaopen_xlua"):
    sys.exit(core_checks(sys.argv[2:]))
lib.luaL_newstate.restype = P
lib.luaopen_xlua.argtypes = [P]
lib.luaopen_i64lib.argtypes = [P]
lib.luaL_loadstring.argtypes = [P, C.c_char_p]
lib.lua_pcall.argtypes = [P, C.c_int, C.c_int, C.c_int]
lib.lua_tolstring.argtypes = [P, C.c_int, P]
lib.lua_tolstring.restype = C.c_char_p
lib.lua_settop.argtypes = [P, C.c_int]
lib.lua_close.argtypes = [P]
lib.xlua_pushstruct.argtypes = [P, C.c_uint, C.c_int]
lib.xlua_pushstruct.restype = P
CSFunction = C.CFUNCTYPE(C.c_int, P)
for name, args, res in [("lua_gettop", [P], C.c_int), ("lua_pushnil", [P], None),
                        ("lua_pushboolean", [P, C.c_int], None), ("lua_pushlightuserdata", [P, P], None),
                        ("lua_pushstring", [P, C.c_char_p], P), ("lua_pushnumber", [P, C.c_double], None),
                        ("lua_createtable", [P, C.c_int, C.c_int], None), ("lua_rawset", [P, C.c_int], None),
                        ("lua_setglobal", [P, C.c_char_p], None), ("lua_touserdata", [P, C.c_int], P),
                        ("lua_type", [P, C.c_int], C.c_int), ("lua_toboolean", [P, C.c_int], C.c_int),
                        ("lua_setmetatable", [P, C.c_int], C.c_int),
                        ("luaL_ref", [P, C.c_int], C.c_int), ("xlua_tag", [], P),
                        ("xlua_get_registry_index", [], C.c_int), ("xlua_pushinteger", [P, C.c_int], None),
                        ("xlua_tointeger", [P, C.c_int], C.c_int), ("xlua_rawseti", [P, C.c_int, C.c_int64], None),
                        ("xlua_rawgeti", [P, C.c_int, C.c_int64], None)]:
    getattr(lib, name).argtypes = args
    getattr(lib, name).restype = res
REGISTRY = lib.xlua_get_registry_index()
LUA_ERRRUN = 2


def run(state, code):
    if lib.luaL_loadstring(state, code.encode()) != 0 or lib.lua_pcall(state, 0, 0, 0) != 0:
        raise AssertionError(lib.lua_tolstring(state, -1, None).decode())


def push_results(state, code, n):
    """Runs a chunk and leaves its first n results on the stack."""
    if lib.luaL_loadstring(state, code.encode()) != 0 or lib.lua_pcall(state, 0, n, 0) != 0:
        raise AssertionError(lib.lua_tolstring(state, -1, None).decode())


def is_luajit(state):
    push_results(state, "return jit ~= nil", 1)
    result = lib.lua_toboolean(state, -1)
    lib.lua_settop(state, -2)
    return result


def push_userdata(state, size):
    """A full userdata with no metatable; lua_newuserdata is a macro since Lua 5.4."""
    if hasattr(lib, "lua_newuserdatauv"):
        lib.lua_newuserdatauv.argtypes = [P, C.c_size_t, C.c_int]
        lib.lua_newuserdatauv.restype = P
        return lib.lua_newuserdatauv(state, size, 0)
    lib.lua_newuserdata.argtypes = [P, C.c_size_t]
    lib.lua_newuserdata.restype = P
    return lib.lua_newuserdata(state, size)


def tagged_meta(state, type_id=None):
    """A registry reference to a metatable carrying the xlua tag, as C# objects and
    structs get; [1] holds type_id when given."""
    lib.lua_createtable(state, 0, 0)
    lib.lua_pushlightuserdata(state, lib.xlua_tag())
    lib.lua_pushboolean(state, 1)
    lib.lua_rawset(state, -3)
    if type_id is not None:
        lib.xlua_pushinteger(state, type_id)
        lib.xlua_rawseti(state, -2, 1)
    return lib.luaL_ref(state, REGISTRY)


def uint64_built():
    """Whether the library has the uint64 table (CMake XLUA_UINT64). XLUA_EXPECT_UINT64=ON
    or OFF says how it was built and fails the check otherwise."""
    built = hasattr(lib, "uint64_parse") or hasattr(lib, "luaJIT_setmode")
    expect = os.environ.get("XLUA_EXPECT_UINT64", "").upper()
    if expect in ("ON", "1", "TRUE", "YES"):
        assert built, "XLUA_EXPECT_UINT64=ON, but the library has no uint64_parse"
    elif expect in ("OFF", "0", "FALSE", "NO"):
        assert not built, "XLUA_EXPECT_UINT64=OFF, but the library has the uint64 table"
    return built


# Lua 5.3+ without the uint64 table: uint64.parse for the checks that only use it
# to make a 64-bit value; integer arithmetic wraps like strtoull.
UINT64_STAND_IN = """
uint64 = {xlua_test_stand_in = true, parse = function(s)
    local n = 0
    for d in tostring(s):gmatch("%d") do n = n * 10 + tonumber(d) end
    return n
end}
"""


def check_uint64_table(state):
    """XLUA_UINT64: luaopen_i64lib sets the global uint64 or, without it, nothing; the
    push and get functions keep all 64 bits either way."""
    built = uint64_built()
    s = fresh_state()
    try:
        push_results(s, "return type(uint64) == 'table' and type(uint64.parse) == 'function'", 1)
        assert bool(lib.lua_toboolean(s, -1)) == built, built
        lib.lua_settop(s, 0)
        for name, args, res in [("lua_pushuint64", [P, C.c_uint64], None), ("lua_touint64", [P, C.c_int], C.c_uint64),
                                ("lua_pushint64", [P, C.c_int64], None), ("lua_toint64", [P, C.c_int], C.c_int64)]:
            getattr(lib, name).argtypes = args
            getattr(lib, name).restype = res
        for value in [0, 1, 2**63 - 1, 2**63, 2**64 - 1]:
            lib.lua_pushuint64(s, value)
            assert lib.lua_touint64(s, -1) == value, value
            lib.lua_settop(s, 0)
        for value in [-2**63, -1, 2**63 - 1]:
            lib.lua_pushint64(s, value)
            assert lib.lua_toint64(s, -1) == value, value
            lib.lua_settop(s, 0)
        if not built:
            lib.luaopen_i64lib.restype = C.c_int
            assert lib.luaopen_i64lib(s) == 0 and lib.lua_gettop(s) == 0
            run(s, "assert(uint64 == nil)")
    finally:
        lib.lua_close(s)
    print("uint64 table:", "built" if built else "not built (XLUA_UINT64=OFF)")


# XLUA_LIBS: the global each standard library sets, by runtime. ffi is preloaded on LuaJIT.
STD_LIBRARIES = {
    "lua": ["package", "coroutine", "table", "io", "os", "string", "math", "utf8", "debug"],
    "luajit": ["package", "table", "io", "os", "string", "math", "debug", "bit", "jit", "ffi"],
}


def check_std_libraries(state):
    """luaL_openlibs (through luaopen_xlua) opens the base library and the libraries
    XLUA_LIBS selected, and no other. XLUA_EXPECT_LIBS ("all" or a list) says which;
    without it the library's luaopen_* exports decide, which needs XLUA_EXPORTS=all."""
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from managed_imports import expected_libs
    runtime = "luajit" if hasattr(lib, "luaJIT_setmode") else "lua"
    names = STD_LIBRARIES[runtime]
    expected = expected_libs(runtime)
    if expected is None:
        # LuaJIT exports luaopen_ffi either way, so only XLUA_EXPECT_LIBS decides about ffi.
        names = [n for n in names if n != "ffi"]
        expected = {n for n in names if hasattr(lib, "luaopen_" + n)}
    lines = ["local expected = {"]
    for n in names:
        lines.append("  [%r] = %s," % (n, "true" if n in expected else "false"))
    lines.append("}")
    run(state, "\n".join(lines) + """
assert(type(print) == "function" and type(pairs) == "function" and _G._G == _G, "base")
for name, want in pairs(expected) do
    local have
    if name == "ffi" then
        have = package.preload.ffi ~= nil or package.loaded.ffi ~= nil
    else
        have = type(rawget(_G, name)) == "table"
    end
    assert(have == want, name .. (want and " is not open" or " is open"))
    if want and name ~= "ffi" then
        assert(package.loaded[name] == _G[name], name .. " is not in package.loaded")
    end
end
-- LuaJIT's base library opens coroutine.
if jit then assert(type(coroutine) == "table") end
""")
    print("standard libraries:", ", ".join(n for n in names if n in expected))


def check_library_parts(state):
    """The parts of os, package and the base library that CMake XLUA_OS_FULL,
    XLUA_LOADLIB and XLUA_DOFILE leave out: each is there whole or not at all.
    XLUA_EXPECT_OS_FULL, XLUA_EXPECT_LOADLIB and XLUA_EXPECT_DOFILE (ON or OFF) say
    which; unset, either is accepted."""
    parts = {}
    for option in ("OS_FULL", "LOADLIB", "DOFILE"):
        value = os.environ.get("XLUA_EXPECT_" + option, "").upper()
        parts[option] = "true" if value in ("ON", "1", "TRUE", "YES") else \
            "false" if value in ("OFF", "0", "FALSE", "NO") else "nil"
    run(state, """
local expect = {os_full = %(OS_FULL)s, loadlib = %(LOADLIB)s, dofile = %(DOFILE)s}
local function whole(what, names, tbl)
    local have = 0
    for _, n in ipairs(names) do
        if rawget(tbl, n) ~= nil then have = have + 1 end
    end
    assert(have == 0 or have == #names, what .. ": only " .. have .. " of " .. #names .. " functions")
    local on = have == #names
    if expect[what] ~= nil then
        assert(on == expect[what], what .. (on and " is there" or " is missing"))
    end
    return on
end
local got = {}
if rawget(_G, "os") then
    -- LuaJIT also has os.execute; xlua-ng's PUC Lua never had it.
    local names = {"exit", "getenv", "remove", "rename", "setlocale", "tmpname"}
    if jit then names[#names + 1] = "execute" end
    got.os_full = whole("os_full", names, os)
    for _, n in ipairs({"clock", "date", "difftime", "time"}) do
        assert(type(os[n]) == "function", "os." .. n .. " is missing")
    end
    assert(os.difftime(5, 3) == 2 and type(os.time()) == "number" and type(os.clock()) == "number")
    assert(os.date("!%%Y-%%m-%%d", 0) == "1970-01-01" and os.time({year = 2000, month = 1, day = 1, hour = 12}) > 0)
end
got.dofile = whole("dofile", {"dofile", "loadfile"}, _G)
if rawget(_G, "package") then
    got.loadlib = whole("loadlib", {"loadlib"}, package)
    -- The C searchers look for a file on package.cpath; without them the message names none.
    local cpath, path = package.cpath, package.path
    package.cpath, package.path = "./?.xlua_c", "./?.xlua_lua"
    local ok, message = pcall(require, "xlua_no_such_module")
    package.cpath, package.path = cpath, path
    assert(not ok and message:find("xlua_no_such_module.xlua_lua", 1, true), message)
    assert((message:find("xlua_no_such_module.xlua_c", 1, true) ~= nil) == got.loadlib, message)
    -- require keeps the preload searcher.
    package.preload.xlua_parts_probe = function() return 42 end
    assert(require("xlua_parts_probe") == 42)
    package.preload.xlua_parts_probe, package.loaded.xlua_parts_probe = nil, nil
end
xlua_parts_result = string.format("os %%s, loadlib %%s, dofile and loadfile %%s",
    got.os_full == nil and "absent" or (got.os_full and "whole" or "time only"),
    tostring(got.loadlib), tostring(got.dofile))
""" % parts)
    push_results(state, "return xlua_parts_result", 1)
    print("library parts:", lib.lua_tolstring(state, -1, None).decode())
    lib.lua_settop(state, -2)
    # dlopen goes with package.loadlib (the stubs of loadlib.c replace it).
    if parts["LOADLIB"] == "false":
        import subprocess
        args = ["nm", "-u", sys.argv[1]] if sys.platform == "darwin" else ["nm", "-D", "--undefined-only", sys.argv[1]]
        try:
            imports = subprocess.run(args, check=True, capture_output=True, text=True).stdout
        except (OSError, subprocess.CalledProcessError):
            imports = None
        if imports is not None and not hasattr(lib, "luaJIT_setmode"):
            assert "dlopen" not in imports, "XLUA_LOADLIB=OFF, but the library imports dlopen"


def check_parse(state):
    # uint64.parse handed lua_tostring's NULL to strtoull for non-strings.
    if not uint64_built():
        print("parse: skipped, built without the uint64 table (XLUA_UINT64=OFF)")
        return
    run(state, """
local ok, err = pcall(uint64.parse)
assert(not ok and err:find("string expected", 1, true), err)
for _, v in ipairs({{}, true, print}) do
    ok, err = pcall(uint64.parse, v)
    assert(not ok and err:find("string expected", 1, true), err)
end
assert(uint64.tostring(uint64.parse("18446744073709551615")) == "18446744073709551615")
assert(uint64.tostring(uint64.parse(42)) == "42")
""")


def check_struct_offsets(state):
    # A negative offset wrapped in size_t, passed the length check and wrote
    # into the fake_id/len header in front of the struct data.
    size = 24
    floats = [(f"float{n}", [C.c_float] * n, 4 * n) for n in range(2, 7)]
    scalars = [("int8_t", [C.c_int8], 1), ("int16_t", [C.c_int16], 2),
               ("int32_t", [C.c_int32], 4), ("int64_t", [C.c_int64], 8),
               ("float", [C.c_float], 4), ("double", [C.c_double], 8)]
    decimal_out = [C.c_uint8, C.c_uint8, C.c_int, C.c_uint64]
    cases = [(name, args, args, width) for name, args, width in scalars + floats]
    cases.append(("decimal", [C.POINTER(C.c_int)], decimal_out, 16))
    for name, in_types, out_types, width in cases:
        pack = getattr(lib, "xlua_pack_" + name)
        unpack = getattr(lib, "xlua_unpack_" + name)
        pack.argtypes = [P, C.c_int] + in_types
        unpack.argtypes = [P, C.c_int] + [C.POINTER(t) for t in out_types]
        for offset, expected in [(-width, 0), (-1, 0), (-2**31, 0), (0, 1),
                                 (size - width, 1), (size - width + 1, 0)]:
            buff = lib.xlua_pushstruct(state, size, -2)  # LUA_NOREF: no metatable
            values = [(C.c_int * 4)(1, 2, 3, 4)] if name == "decimal" else [0x41] * len(in_types)
            outs = [t() for t in out_types]
            assert pack(buff, offset, *values) == expected, (name, "pack", offset)
            assert unpack(buff, offset, *[C.byref(o) for o in outs]) == expected, (name, "unpack", offset)
            header = (C.c_int.from_address(buff).value, C.c_uint.from_address(buff + 4).value)
            assert header == (-1, size), (name, offset, header)
            lib.lua_settop(state, 0)
        # A block whose fake_id is not -1 (a C# object handle) is not a struct.
        for fake_id in [0, 7, -2]:
            buff = lib.xlua_pushstruct(state, size, -2)
            C.c_int.from_address(buff).value = fake_id
            C.memset(buff + 8, 0, size)
            values = [(C.c_int * 4)(1, 2, 3, 4)] if name == "decimal" else [0x41] * len(in_types)
            assert pack(buff, 0, *values) == 0, (name, "pack", fake_id)
            assert unpack(buff, 0, *[C.byref(t()) for t in out_types]) == 0, (name, "unpack", fake_id)
            assert all(C.c_ubyte.from_address(buff + 8 + i).value == 0 for i in range(width)), (name, fake_id)
            lib.lua_settop(state, 0)


def check_struct_alignment(state):
    # Generated Pack/UnPack code packs fields without padding, so floats and
    # decimals can sit at odd offsets; they were accessed through float * and
    # DECIMAL * (undefined behaviour, reported by UBSan in sanitize.sh).
    for offset in [1, 2, 3, 5]:
        buff = lib.xlua_pushstruct(state, 40, -2)
        for n in range(2, 7):
            pack = getattr(lib, "xlua_pack_float%d" % n)
            unpack = getattr(lib, "xlua_unpack_float%d" % n)
            pack.argtypes = [P, C.c_int] + [C.c_float] * n
            unpack.argtypes = [P, C.c_int] + [C.POINTER(C.c_float)] * n
            values = [i + 0.5 for i in range(n)]
            outs = [C.c_float() for _ in range(n)]
            assert pack(buff, offset, *values) == 1
            assert unpack(buff, offset, *[C.byref(o) for o in outs]) == 1
            assert [o.value for o in outs] == values, (n, offset)
        scale, sign, hi, lo64 = C.c_uint8(), C.c_uint8(), C.c_int(), C.c_uint64()
        lib.xlua_pack_decimal.argtypes = [P, C.c_int, C.POINTER(C.c_int)]
        lib.xlua_unpack_decimal.argtypes = [P, C.c_int] + [C.POINTER(t) for t in
                                                           (C.c_uint8, C.c_uint8, C.c_int, C.c_uint64)]
        # System.Decimal memory: flags (scale in byte 2, sign in byte 3), hi, lo, mid.
        assert lib.xlua_pack_decimal(buff, offset, (C.c_int * 4)(0x00030000 | -2**31, 7, 5, 6)) == 1
        assert lib.xlua_unpack_decimal(buff, offset, C.byref(scale), C.byref(sign), C.byref(hi), C.byref(lo64)) == 1
        assert (scale.value, sign.value, hi.value, lo64.value) == (3, 0x80, 7, 5 | 6 << 32), offset
        lib.lua_settop(state, 0)


def check_exported_helpers(state):
    # Exports that no C# code calls, kept for native plugins that extend xLua.
    for name, args, res in [("xlua_get_registry_index", [], C.c_int), ("lua_createtable", [P, C.c_int, C.c_int], None),
                            ("luaL_ref", [P, C.c_int], C.c_int), ("xlua_rawgeti", [P, C.c_int, C.c_int64], None),
                            ("xlua_rawseti", [P, C.c_int, C.c_int64], None), ("xlua_pushinteger", [P, C.c_int], None),
                            ("lua_pushstring", [P, C.c_char_p], P), ("lua_gettop", [P], C.c_int),
                            ("xlua_newstruct", [P, C.c_int, C.c_int], P), ("xlua_tostruct", [P, C.c_int, C.c_int], P),
                            ("xlua_ref_indirect", [P, C.c_int], C.c_int), ("xlua_getref_indirect", [P, C.c_int, C.c_int], None)]:
        getattr(lib, name).argtypes = args
        getattr(lib, name).restype = res
    registry = lib.xlua_get_registry_index()

    # xlua_tostruct accepts a struct only when metatable[1] is the expected reference.
    lib.lua_createtable(state, 0, 0)
    meta = lib.luaL_ref(state, registry)
    lib.xlua_rawgeti(state, registry, meta)
    lib.xlua_pushinteger(state, meta)
    lib.xlua_rawseti(state, -2, 1)
    lib.lua_settop(state, 0)
    # A negative size wrapped to a tiny allocation with a length near 2^32;
    # it now pushes nil and returns NULL. Size 0 is still a valid empty struct.
    for size in (-1, -8, -9, -2**31):
        assert lib.xlua_newstruct(state, size, meta) is None, size
        assert lib.lua_gettop(state) == 1 and lib.lua_type(state, -1) == 0, size  # LUA_TNIL
        lib.lua_settop(state, 0)
    empty = lib.xlua_newstruct(state, 0, meta)
    assert empty is not None and lib.xlua_tostruct(state, -1, meta) == empty
    assert C.c_uint.from_address(empty - 4).value == 0  # the len header
    lib.lua_settop(state, 0)
    data = lib.xlua_newstruct(state, 8, meta)
    C.c_int64.from_address(data).value = -42
    assert lib.xlua_tostruct(state, -1, meta) == data
    assert C.c_int64.from_address(lib.xlua_tostruct(state, -1, meta)).value == -42
    assert lib.xlua_tostruct(state, -1, meta + 1) is None
    lib.xlua_pushinteger(state, 1)
    assert lib.xlua_tostruct(state, -1, meta) is None
    lib.lua_settop(state, 0)
    # No metatable, or a metatable whose [1] is not a number.
    push_userdata(state, 16)
    assert lib.xlua_tostruct(state, -1, meta) is None
    lib.lua_settop(state, 0)
    lib.lua_createtable(state, 0, 0)
    lib.lua_pushstring(state, b"1")
    lib.xlua_rawseti(state, -2, 1)
    string_meta = lib.luaL_ref(state, registry)
    lib.xlua_pushstruct(state, 8, string_meta)
    assert lib.xlua_tostruct(state, -1, string_meta) is None
    lib.lua_settop(state, 0)

    # xlua_ref_indirect stores the top value in a referenced table and pops it.
    lib.lua_createtable(state, 0, 0)
    table = lib.luaL_ref(state, registry)
    lib.lua_pushstring(state, b"indirect")
    ref = lib.xlua_ref_indirect(state, table)
    assert lib.lua_gettop(state) == 0
    lib.xlua_getref_indirect(state, table, ref)
    assert lib.lua_tolstring(state, -1, None) == b"indirect"
    lib.lua_settop(state, 0)

    # Upstream's debug print helpers call the global print. They are not LUA_API
    # (build/xlua.c), so a Windows DLL does not export them.
    if sys.platform == "win32" and not hasattr(lib, "print_top"):
        return
    for name, args in [("print_top", [P]), ("print_str", [P, C.c_char_p]), ("print_value", [P, C.c_char_p, C.c_int])]:
        getattr(lib, name).argtypes = args
        getattr(lib, name).restype = None
    run(state, "printed = {} saved_print = print print = function(...) printed[#printed + 1] = table.concat({...}, ' ') end")
    lib.lua_pushstring(state, b"top value")
    lib.print_top(state)
    lib.print_str(state, b"a string")
    lib.print_value(state, b"label", -1)
    assert lib.lua_gettop(state) == 1
    lib.lua_settop(state, 0)
    run(state, """
print = saved_print
assert(#printed == 3, #printed)
assert(printed[1] == "top value" and printed[2] == "a string" and printed[3] == "label top value", table.concat(printed, "|"))
printed, saved_print = nil
""")


def check_value_helpers(state):
    # Type checks in the helpers the managed bridge calls for every argument.
    for name, args, res in [("xlua_gc", [P, C.c_int, C.c_int], C.c_int),
                            ("xlua_tocsobj_safe", [P, C.c_int], C.c_int), ("xlua_tocsobj_fast", [P, C.c_int], C.c_int),
                            ("xlua_gettypeid", [P, C.c_int], C.c_int),
                            ("xlua_is_eq_str", [P, C.c_int, C.c_char_p, C.c_int], C.c_int)]:
        getattr(lib, name).argtypes = args
        getattr(lib, name).restype = res
    # Operation numbers no runtime defines return -1 (Lua 5.4 has 10 and 11).
    for what in [-1, -100, 8, 1000]:
        assert lib.xlua_gc(state, what, 0) == -1, what
    # A C# object is a userdata holding its object pool index, with a tagged metatable.
    obj_meta = tagged_meta(state)
    C.c_int.from_address(push_userdata(state, 4)).value = 12
    lib.xlua_rawgeti(state, REGISTRY, obj_meta)
    lib.lua_setmetatable(state, -2)
    assert lib.xlua_tocsobj_safe(state, -1) == 12
    assert lib.xlua_tocsobj_fast(state, -1) == 12
    # Without a metatable, or with one that lacks the tag, it is not a C# object.
    C.c_int.from_address(push_userdata(state, 4)).value = 13
    assert lib.xlua_tocsobj_safe(state, -1) == -1
    assert lib.xlua_tocsobj_fast(state, -1) == 13
    lib.lua_createtable(state, 0, 0)
    lib.lua_setmetatable(state, -2)
    assert lib.xlua_tocsobj_safe(state, -1) == -1
    lib.xlua_pushinteger(state, 12)
    assert lib.xlua_tocsobj_safe(state, -1) == -1
    assert lib.xlua_tocsobj_fast(state, -1) == -1
    lib.lua_settop(state, 0)
    # xlua_gettypeid: [1] of the metatable of a userdata, else -1.
    lib.xlua_pushstruct(state, 8, tagged_meta(state, 77))
    assert lib.xlua_gettypeid(state, -1) == 77
    lib.xlua_pushstruct(state, 8, tagged_meta(state))
    assert lib.xlua_gettypeid(state, -1) == -1
    lib.xlua_pushstruct(state, 8, -2)
    assert lib.xlua_gettypeid(state, -1) == -1
    for push in [lambda: lib.xlua_pushinteger(state, 77), lambda: lib.lua_pushstring(state, b"77"),
                 lambda: lib.lua_pushlightuserdata(state, lib.xlua_tag()), lambda: lib.lua_createtable(state, 0, 0)]:
        push()
        assert lib.xlua_gettypeid(state, -1) == -1
    lib.lua_settop(state, 0)
    # xlua_is_eq_str compares the bytes of a string only, embedded NULs included.
    push_results(state, 'return "ab\\0c", "ab", 12, "12"', 4)
    assert lib.xlua_is_eq_str(state, 1, b"ab\0c", 4) == 1
    assert lib.xlua_is_eq_str(state, 1, b"ab\0d", 4) == 0
    assert lib.xlua_is_eq_str(state, 1, b"ab", 2) == 0
    assert lib.xlua_is_eq_str(state, 2, b"ab", 2) == 1
    assert lib.xlua_is_eq_str(state, 2, b"", 0) == 0
    assert lib.xlua_is_eq_str(state, 3, b"12", 2) == 0
    assert lib.xlua_is_eq_str(state, 4, b"12", 2) == 1
    # A negative length never matches (it is not converted to a huge size_t).
    assert lib.xlua_is_eq_str(state, 2, b"ab", -1) == 0
    lib.lua_settop(state, 0)


def check_struct_accessors(state):
    # xlua.genaccessor(offset, type) returns getter, setter and a no-op; the
    # accessors reject anything but a struct big enough for the field.
    size = 16
    buff = lib.xlua_pushstruct(state, size, -2)
    lib.lua_setglobal(state, b"s")
    C.c_int.from_address(push_userdata(state, size + 8)).value = 7  # not a struct header
    lib.lua_setglobal(state, b"u")
    lib.lua_pushlightuserdata(state, buff)  # the struct's own address as light userdata
    lib.lua_setglobal(state, b"lud")
    run(state, """
local jit = jit ~= nil
local widths = {[0] = 1, 1, 2, 2, 4, 4, 8, 8, 4, 8}
local function raises(text, f, ...)
    local ok, err = pcall(f, ...)
    assert(not ok and tostring(err):find(text, 1, true), tostring(err))
end
local function roundtrip(get, set, value, expected)
    set(s, value)
    local got = get(s)
    if jit and type(got) == "userdata" then
        -- LuaJIT returns 64-bit fields as int64 userdata; UINT_ESPECIALLY adds "U".
        got = tostring(got):gsub("U$", "")
        expected = tostring(expected)
    end
    assert(got == expected, string.format("%s -> %s, expected %s", tostring(value), tostring(got), tostring(expected)))
end
-- Integer fields wrap like a C cast; floats keep float precision.
local cases = {
    [0] = {{127, 127}, {128, -128}, {-129, 127}, {-1, -1}, {-128, -128}, {127.0, 127}, {-128.0, -128}},
    {{255, 255}, {256, 0}, {-1, 255}, {0, 0}, {255.0, 255}},
    {{32767, 32767}, {32768, -32768}, {-1, -1}},
    {{65535, 65535}, {65536, 0}, {-1, 65535}},
    {{2147483647, 2147483647}, {2147483648, -2147483648}, {-1, -1}},
    {{4294967295, 4294967295}, {7, 7}, {4294967296, 0}, {-1, 4294967295}, {-2.0, 4294967294}, {1e10, 1410065408}},
    {{-5, -5}, {0, 0}},
    {{5, 5}, {0, 0}},
    {{0.5, 0.5}, {-2, -2}, {1 / 0, 1 / 0}},
    {{1 / 3, 1 / 3}, {-1e300, -1e300}},
}
if not jit then
    table.insert(cases[6], {math.mininteger, math.mininteger})
    table.insert(cases[7], {-1, -1})  -- a ulong shares the signed representation
end
for t = 0, 9 do
    local w = widths[t]
    for _, offset in ipairs({0, 1, 16 - w}) do
        local get, set, nop = xlua.genaccessor(offset, t)
        assert(select("#", nop(s)) == 0)
        for _, case in ipairs(cases[t]) do roundtrip(get, set, case[1], case[2]) end
    end
    -- One byte past the end of the struct.
    get, set = xlua.genaccessor(16 - w + 1, t)
    raises("invalid c# struct!", get, s)
    raises("invalid c# struct!", set, s, 1)
    get, set = xlua.genaccessor(0, t)
    for _, bad in ipairs({5, "x", {}, lud, u, print}) do
        raises("invalid c# struct!", get, bad)
        raises("invalid c# struct!", set, bad, 1)
    end
    raises("invalid c# struct!", get)
    -- The offset is the closures' upvalue; debug.setupvalue can make it negative.
    assert(debug.setupvalue(get, 1, -1) == "" and debug.setupvalue(set, 1, -w) == "")
    raises("invalid c# struct!", get, s)
    raises("invalid c# struct!", set, s, 1)
end
-- Setters take a number or a string Lua converts to one (lua_isnumber), and
-- on LuaJIT a 64-bit field also takes an int64/uint64 userdata. Any other
-- value raises and leaves the field unchanged; upstream stored 0.
local accepted = {
    [0] = {{"12", 12}, {" -3 ", -3}, {3.0, 3}, {"1e2", 100}},
    {{"12", 12}, {3.0, 3}},
    {{"-12", -12}, {3.0, 3}},
    {{"12", 12}, {3.0, 3}},
    {{"0x10", 16}, {"-7", -7}, {3.0, 3}},
    {{"12", 12}, {"-1", 4294967295}, {"3.0", 3}, {3.0, 3}},
    {{"-12", -12}, {3.0, 3}, {uint64.parse("5"), 5}},
    {{"12", 12}, {3.0, 3}, {uint64.parse("5"), 5}},
    {{"0.5", 0.5}, {"-2", -2}, {7, 7}},
    {{"0.25", 0.25}, {"1e300", 1e300}, {7, 7}},
}
-- The 64-bit fields take -2^63 and 2^53 as floats: both have integer values.
-- A UINT_ESPECIALLY uint64 field reads -2^63 back as the unsigned 2^63.
local distinct = jit and tostring(uint64.parse("1")) == "1U"
for _, t in ipairs({6, 7}) do
    local min = (t == 7 and distinct) and "9223372036854775808" or "-9223372036854775808"
    table.insert(accepted[t], {-2^63, jit and min or math.mininteger})
    table.insert(accepted[t], {2^53, jit and "9007199254740992" or 9007199254740992})
end
local rejected = {{nil, "nil"}, {true, "boolean"}, {false, "boolean"}, {{}, "table"},
                  {"x", "string"}, {"", "string"}, {"12abc", "string"}, {print, "function"},
                  {u}, {lud}, {coroutine.create(function() end), "thread"}}
for t = 0, 9 do
    local get, set = xlua.genaccessor(0, t)
    for _, case in ipairs(accepted[t]) do roundtrip(get, set, case[1], case[2]) end
    set(s, 9)
    local before = tostring(get(s))
    for _, case in ipairs(rejected) do
        raises("bad argument #2", set, s, case[1])
        raises("number expected, got " .. (case[2] or ""), set, s, case[1])
        assert(tostring(get(s)) == before, t)
    end
    raises("number expected, got no value", set, s)
    assert(tostring(get(s)) == before, t)
    -- A bad struct is reported before a bad value.
    raises("invalid c# struct!", set, u, nil)
end
-- An integer field takes a number by Lua 5.3's rule (luaL_checkinteger), on
-- every runtime: a fraction, NaN, an infinity or a value outside the 64-bit
-- range raises and leaves the field unchanged. Upstream stored 0 on Lua 5.3+
-- and the truncated value on LuaJIT; uint32 truncated and wrapped everywhere.
local noint = {2.75, -2.75, 0.5, -0.5, "2.75", "-0.5", 0/0, 1/0, -1/0, 2^63, -2^64, 1e300, "1e300"}
for t = 0, 7 do
    local get, set = xlua.genaccessor(0, t)
    set(s, 9)
    local before = tostring(get(s))
    for _, v in ipairs(noint) do
        raises("bad argument #2", set, s, v)
        raises("(number has no integer representation)", set, s, v)
        assert(tostring(get(s)) == before, t)
    end
end
local get64, set64 = xlua.genaccessor(0, 6)
set64(s, uint64.parse("9223372036854775808"))  -- 2^63: INT64_MIN as int64
assert(tostring(get64(s)) == "-9223372036854775808")
set64(s, get64(s))  -- what the getter returns is accepted back
assert(tostring(get64(s)) == "-9223372036854775808")
local getf, setf = xlua.genaccessor(0, 8)
setf(s, 1 / 3)
assert(getf(s) ~= 1 / 3 and math.abs(getf(s) - 1 / 3) < 1e-7)
raises("offset must larger than 0", xlua.genaccessor, -1, 4)
raises("unknow tag[10]", xlua.genaccessor, 0, 10)
raises("unknow tag[-1]", xlua.genaccessor, 0, -1)
""")
    header = (C.c_int.from_address(buff).value, C.c_uint.from_address(buff + 4).value)
    assert header == (-1, size), header
    run(state, "s, u, lud = nil, nil, nil")


def check_struct_clone(state):
    # xlua.structclone copies a tagged struct and refuses everything else.
    meta = tagged_meta(state, 5)
    buff = lib.xlua_pushstruct(state, 12, meta)
    C.memmove(buff + 8, b"0123456789ab", 12)
    lib.lua_setglobal(state, b"tagged")
    lib.xlua_pushstruct(state, 12, -2)
    lib.lua_setglobal(state, b"untagged")
    lib.lua_createtable(state, 0, 0)  # a metatable without the tag
    lib.xlua_pushstruct(state, 12, lib.luaL_ref(state, REGISTRY))
    lib.lua_setglobal(state, b"plainmeta")
    # A C# object: tagged metatable, but its first int is a pool index.
    C.c_int.from_address(push_userdata(state, 4)).value = 3
    lib.xlua_rawgeti(state, REGISTRY, meta)
    lib.lua_setmetatable(state, -2)
    lib.lua_setglobal(state, b"object")
    lib.lua_pushlightuserdata(state, buff)
    lib.lua_setglobal(state, b"lud")
    run(state, """
local copy = xlua.structclone(tagged)
assert(type(copy) == "userdata" and not rawequal(copy, tagged))
assert(rawequal(debug.getmetatable(copy), debug.getmetatable(tagged)))
local get, set = xlua.genaccessor(0, 4)
assert(get(copy) == get(tagged))
set(copy, 1)
assert(get(copy) == 1 and get(tagged) ~= 1)
local getlast = xlua.genaccessor(11, 1)
assert(getlast(copy) == string.byte("b"))
assert(not pcall(xlua.genaccessor(12, 1), copy))
for _, bad in ipairs({untagged, plainmeta, object, lud, {}, 1}) do
    local ok, err = pcall(xlua.structclone, bad)
    assert(not ok and tostring(err):find("invalid c# struct!", 1, true), tostring(err))
end
assert(not pcall(xlua.structclone))
tagged, untagged, plainmeta, object, lud = nil
""")
    lib.lua_settop(state, 0)


def check_indexers(state):
    # gen_obj_indexer and friends build the __index/__newindex closures of
    # C# objects and classes from values on the stack. Lookups go: members,
    # C# indexer, array indexer, then up the BaseType chain to the first type
    # with an entry in the index function table, which is cached.
    for name in ["gen_obj_indexer", "gen_obj_newindexer", "gen_cls_indexer", "gen_cls_newindexer"]:
        getattr(lib, name).argtypes = [P]
    lib.lua_pushcclosure.argtypes = [P, P, C.c_int]

    def make(global_name, generator, code, n):
        push_results(state, code, n)
        generator(state)
        lib.lua_setglobal(state, global_name)
        assert lib.lua_gettop(state) == 0

    chain = """
calls = {}
local C = {}
local B = setmetatable({}, {__index = function(t, k) calls[#calls + 1] = k if k == "BaseType" then return C end end})
local A = {BaseType = B}
types = {A = A, B = B, C = C}
"""
    make(b"oi", lib.gen_obj_indexer, chain + """
local methods = {m = "method"}
local getters = {g = function(obj) return "getter of " .. obj.name end}
local csindexer = function(obj, key) if key == "cs" then return true, "cs value" end return false, "ignored" end
local indexfuncs = {[C] = function(obj, key) return "base " .. tostring(key) end}
local arrayindexer = function(obj, i) return i * 10 end
return methods, getters, csindexer, A, indexfuncs, arrayindexer
""", 6)
    make(b"oi_empty", lib.gen_obj_indexer, chain + "return nil, nil, nil, A, {}, nil", 6)
    make(b"oi_none", lib.gen_obj_indexer, "return nil, nil, nil, nil, {}, nil", 6)
    make(b"on", lib.gen_obj_newindexer, chain + """
local setters = {s = function(obj, v) rawset(obj, "set_s", v) end}
local csnewindexer = function(obj, k, v) if k == "cs" then rawset(obj, "set_cs", v) return true end return false end
local newindexfuncs = {[C] = function(obj, k, v) rawset(obj, "base_" .. tostring(k), v) end}
local arraysetter = function(obj, i, v) rawset(obj, "array_" .. i, v) end
return setters, csnewindexer, A, newindexfuncs, arraysetter
""", 5)
    make(b"on_empty", lib.gen_obj_newindexer, chain + "return nil, nil, A, {}, nil", 5)
    make(b"on_none", lib.gen_obj_newindexer, "return nil, nil, nil, {}, nil", 5)
    make(b"ci", lib.gen_cls_indexer, chain + """
local getters = {g = function() return "static getter" end}
local fields = {f = 42, Method = "static method"}
local indexfuncs = {[C] = function(cls, k) return "class base " .. tostring(k) end}
return getters, fields, A, indexfuncs
""", 4)
    make(b"ci_empty", lib.gen_cls_indexer, chain + "return nil, nil, A, {}", 4)
    make(b"cn", lib.gen_cls_newindexer, chain + """
local setters = {s = function(v) static_s = v end}
local indexfuncs = {[C] = function(cls, k, v) rawset(cls, "class_base_" .. tostring(k), v) end}
return setters, A, indexfuncs
""", 3)
    make(b"cn_empty", lib.gen_cls_newindexer, chain + "return nil, A, {}", 3)
    run(state, """
local function raises(text, f, ...)
    local ok, err = pcall(f, ...)
    assert(not ok and tostring(err):find(text, 1, true), tostring(err))
end
local obj = {name = "obj"}
assert(oi(obj, "m") == "method")
assert(oi(obj, "g") == "getter of obj")
assert(oi(obj, 3) == 30)
assert(oi(obj, "cs") == "cs value")
calls = {}
assert(oi(obj, "other") == "base other")  -- A and B have no entry; C has
assert(#calls == 1 and calls[1] == "BaseType")
assert(oi(obj, "again") == "base again")  -- cached: no second walk
assert(#calls == 1)
assert(select("#", oi_empty(obj, "x")) == 0)  -- chain ends with no entry
assert(select("#", oi_empty(obj, "x")) == 0)
assert(select("#", oi_none(obj, 1)) == 0)

on(obj, "s", 1)
on(obj, "cs", 2)
on(obj, 5, 3)
on(obj, "field", 4)
assert(obj.set_s == 1 and obj.set_cs == 2 and obj.array_5 == 3 and obj.base_field == 4)
assert(obj.base_5 == nil)
raises("cannot set nope, no such field", on_empty, obj, "nope", 1)
raises("cannot set nope, no such field", on_empty, obj, "nope", 1)
raises("cannot set 7, no such field", on_none, obj, 7, 1)

local cls = {}
assert(ci(cls, "g") == "static getter")
assert(ci(cls, "f") == 42 and ci(cls, "Method") == "static method")
assert(ci(cls, "other") == "class base other")
assert(ci_empty(cls, "x") == nil and select("#", ci_empty(cls, "x")) == 1)

cn(cls, "s", 5)
assert(static_s == 5)
cn(cls, "other", 6)
assert(rawget(cls, "class_base_other") == 6)
raises("no static field nope", cn_empty, cls, "nope", 1)
raises("no static field nope", cn_empty, cls, "nope", 1)
oi, oi_empty, oi_none, on, on_empty, on_none, ci, ci_empty, cn, cn_empty, calls, types, static_s = nil
""")


def check_path_access(state):
    # LuaTable.GetInPath/SetInPath: dotted paths through nested tables.
    for name in ["xlua_pgettable_bypath", "xlua_psettable_bypath"]:
        getattr(lib, name).argtypes = [P, C.c_int, C.c_char_p]
        getattr(lib, name).restype = C.c_int
    for name in ["xlua_pgettable", "xlua_psettable"]:
        getattr(lib, name).argtypes = [P, C.c_int]
        getattr(lib, name).restype = C.c_int
    push_results(state, "t = {a = {b = {c = 1}, n = 5, s = 'str'}, top = 2, [''] = {[''] = 'empty'}} return t", 1)

    def get(path):
        assert lib.xlua_pgettable_bypath(state, 1, path) == 0, path
        assert lib.lua_gettop(state) == 2
        value = lib.lua_type(state, -1), lib.lua_tolstring(state, -1, None)
        lib.lua_settop(state, 1)
        return value

    def set_value(path, text):
        lib.lua_pushstring(state, text)
        status = lib.xlua_psettable_bypath(state, 1, path)
        message = lib.lua_tolstring(state, -1, None) if status else None
        assert lib.lua_gettop(state) == (2 if status else 1), path
        lib.lua_settop(state, 1)
        return status, message

    TNIL, TNUMBER, TSTRING, TTABLE = 0, 3, 4, 5
    assert get(b"a.b.c") == (TNUMBER, b"1")
    assert get(b"a.b")[0] == TTABLE
    assert get(b"top") == (TNUMBER, b"2")
    assert get(b"a.n") == (TNUMBER, b"5")
    assert get(b"missing") == (TNIL, None)
    assert get(b"a.missing") == (TNIL, None)
    assert get(b"a.missing.c") == (TNIL, None)  # a missing table midway
    assert get(b"a.n.c") == (TNIL, None)  # a number midway
    assert get(b"a.s.len") == (TNIL, None)  # a string midway: not indexed
    assert get(b"a.b.c.d") == (TNIL, None)
    assert get(b".") == (TSTRING, b"empty")
    assert set_value(b"a.b.d", b"v") == (0, None)
    assert get(b"a.b.d") == (TSTRING, b"v")
    assert set_value(b"a.b.c", b"w") == (0, None)
    assert get(b"a.b.c") == (TSTRING, b"w")
    assert set_value(b"new", b"x") == (0, None)
    assert get(b"new") == (TSTRING, b"x")
    for path in [b"a.missing.d", b"a.n.d", b"a.b.d.e", b"nothere.x"]:
        status, message = set_value(path, b"v")
        assert status == LUA_ERRRUN and b"can not set value to " + path in message, (path, status, message)
    assert get(b"a.missing") == (TNIL, None)
    lib.lua_settop(state, 0)
    # xlua_pgettable/psettable: one key, metamethods allowed to raise.
    push_results(state, "return setmetatable({}, {__index = function(t, k) error('no ' .. k) end,"
                        " __newindex = function(t, k, v) error('ro ' .. k) end})", 1)
    lib.lua_pushstring(state, b"key")
    assert lib.xlua_pgettable(state, 1) == LUA_ERRRUN
    assert b"no key" in lib.lua_tolstring(state, -1, None)
    lib.lua_settop(state, 1)
    lib.lua_pushstring(state, b"key")
    lib.lua_pushstring(state, b"value")
    assert lib.xlua_psettable(state, 1) == LUA_ERRRUN
    assert b"ro key" in lib.lua_tolstring(state, -1, None)
    lib.lua_settop(state, 0)


def check_csharp_functions(state):
    # The C closures that wrap C# callbacks: a reported error is raised after
    # the callback returns, and the xlua.sethook profiler sees the return.
    for name, args, res in [("xlua_push_csharp_function", [P, P, C.c_int], None),
                            ("xlua_push_csharp_wrapper", [P, C.c_int], None),
                            ("xlua_csharp_str_error", [P, C.c_char_p], C.c_int),
                            ("xlua_csharp_error", [P], C.c_int), ("xlua_upvalueindex", [C.c_int], C.c_int),
                            ("lua_pushvalue", [P, C.c_int], None)]:
        getattr(lib, name).argtypes = args
        getattr(lib, name).restype = res

    def twice(L):
        lib.xlua_pushinteger(L, 2 * lib.xlua_tointeger(L, 1))
        return 1

    def fail_str(L):
        return lib.xlua_csharp_str_error(L, b"str error from C#")

    def fail(L):
        lib.lua_pushstring(L, b"error object from C#")
        return lib.xlua_csharp_error(L)

    def upvalue(L):
        # xlua_upvalueindex(1) is the first upvalue after the C function and the error flag.
        lib.lua_pushvalue(L, lib.xlua_upvalueindex(1))
        return 1

    callbacks = [CSFunction(f) for f in (twice, fail_str, fail, upvalue)]
    for name, fn in zip([b"twice", b"fail_str", b"fail"], callbacks):
        lib.xlua_push_csharp_function(state, C.cast(fn, P), 0)
        lib.lua_setglobal(state, name)
    lib.lua_pushstring(state, b"first upvalue")
    lib.xlua_push_csharp_function(state, C.cast(callbacks[3], P), 1)
    lib.lua_setglobal(state, b"upvalue")
    # GEN_CODE_MINIMIZE wrappers need the managed caller, which a plain host has not set.
    lib.xlua_push_csharp_wrapper(state, 7)
    lib.lua_setglobal(state, b"wrapper")
    run(state, """
local function raises(text, f, ...)
    local ok, err = pcall(f, ...)
    assert(not ok and tostring(err):find(text, 1, true), tostring(err))
end
assert(twice(21) == 42)
raises("str error from C#", fail_str)
raises("error object from C#", fail)
assert(twice(2) == 4)  -- the error flag was reset
assert(upvalue() == "first upvalue")
raises("g_csharp_wrapper_caller not set", wrapper)

local events = {}
xlua.sethook(function(event, name, where) events[#events + 1] = {event, name, where} end)
local chunk = load("return 1", "=main chunk")
assert(chunk() == 1)
assert(twice(3) == 6)
xlua.sethook(nil)
local main_call, cs_return = false, false
for _, e in ipairs(events) do
    -- A main chunk has linedefined 0.
    if e[1] == "call" and e[2] == "main chunk:0" then main_call = true end
    if e[1] == "return" and e[3] == "[C#]" then cs_return = true end
end
assert(main_call and cs_return)
-- A hook set by debug.sethook, not xlua.sethook: the wrapper skips the profiler report.
local count = 0
debug.sethook(function() count = count + 1 end, "cr")
assert(twice(4) == 8)
debug.sethook()
assert(count > 0)
twice, fail_str, fail, upvalue, wrapper = nil
collectgarbage()
""")
    del callbacks


def check_setfenv(state):
    # Lua 5.3+: xlua.c's lua_setfenv (LuaEnv.LoadString/DoString with an env,
    # LuaFunction.SetEnv) pops the table and makes it the function's _ENV.
    # It used to set upvalue 1, which in a closure may be another variable,
    # and in place, which changed the globals of every function sharing that
    # _ENV (the whole chunk that defined the closure).
    if is_luajit(state):
        return  # LuaJIT has Lua 5.1's own lua_setfenv
    lib.lua_setfenv.argtypes = [P, C.c_int]
    lib.lua_setfenv.restype = C.c_int

    def setfenv(push_function, env="{y = 'env'}"):
        lib.lua_settop(state, 0)
        push_function()
        push_results(state, "return " + env, 1)
        result = lib.lua_setfenv(state, -2)
        assert lib.lua_gettop(state) == 1, lib.lua_gettop(state)  # the table is popped
        lib.lua_setglobal(state, b"f")
        return result

    run(state, "y = 'global'")
    assert setfenv(lambda: lib.luaL_loadstring(state, b"return y")) == 1
    run(state, "assert(f() == 'env')")
    # Without debug information a main chunk's only upvalue is still _ENV.
    assert setfenv(lambda: push_results(state, "return load(string.dump(load('return y'), true))", 1)) == 1
    run(state, "assert(f() == 'env')")
    # _ENV is the first upvalue, shared with a sibling closure, which keeps its globals.
    assert setfenv(lambda: push_results(state, """
local function get() return y end
sibling = function() return y end
return get""", 1)) == 1
    run(state, "assert(f() == 'env') assert(sibling() == 'global', tostring(sibling()))")
    # _ENV is the second upvalue.
    assert setfenv(lambda: push_results(state, """
local x = "up"
local function get() return x, y end
sibling = function() return y end
return get""", 1)) == 1
    run(state, """
local a, b = f()
assert(a == "up" and b == "env", tostring(a) .. " " .. tostring(b))
assert(sibling() == "global", tostring(sibling()))
assert(y == "global")
""")
    # No _ENV: nothing to set, the upvalue is unchanged.
    assert setfenv(lambda: push_results(state, "local x = 'up' return function() return x end", 1)) == 0
    run(state, "assert(f() == 'up')")
    # C functions keep their upvalues (coroutine.wrap keeps its coroutine there).
    assert setfenv(lambda: push_results(state, "return coroutine.wrap(function() coroutine.yield(1) return 2 end)", 1)) == 0
    run(state, "assert(f() == 1 and f() == 2)")
    assert setfenv(lambda: push_results(state, "return print", 1)) == 0
    for value in ["{}", "1", "'s'"]:
        assert setfenv(lambda: push_results(state, "return " + value, 1)) == 0
    run(state, "f, sibling = nil, nil")
    lib.lua_settop(state, 0)
    # Out of memory while making the function's own _ENV: nothing is set and
    # the table is still popped.
    Alloc = C.CFUNCTYPE(P, P, P, C.c_size_t, C.c_size_t)
    lib.lua_getallocf.argtypes = [P, C.POINTER(P)]
    lib.lua_getallocf.restype = P
    lib.lua_setallocf.argtypes = [P, P, P]
    lib.lua_setallocf.restype = None
    original_ud = P()
    original_ptr = lib.lua_getallocf(state, C.byref(original_ud))
    original = Alloc(original_ptr)
    failing = [False]

    def alloc(ud, ptr, osize, nsize):
        if failing[0] and nsize > 0 and (ptr is None or nsize > osize):
            return None
        return original(original_ud, ptr, osize, nsize)

    hook = Alloc(alloc)
    lib.luaL_loadstring(state, b"return y")
    push_results(state, "return {y = 'env'}", 1)
    lib.lua_setallocf(state, C.cast(hook, P), None)
    failing[0] = True
    try:
        result = lib.lua_setfenv(state, -2)
    finally:
        failing[0] = False
        lib.lua_setallocf(state, original_ptr, original_ud)
    assert result == 0 and lib.lua_gettop(state) == 1, (result, lib.lua_gettop(state))
    assert lib.lua_pcall(state, 0, 1, 0) == 0
    assert lib.lua_tolstring(state, -1, None) == b"global"
    lib.lua_settop(state, 0)


def check_uint_conversion(state):
    # xlua_touint converts a C# uint argument (and a uint32 struct field). A
    # float went through a plain (uint32_t) cast, which is undefined for
    # negative and too large values: -1.5 gave 0 on ARM64 and 4294967295 on
    # x86-64. A float now converts like the integer it truncates to.
    lib.xlua_touint.argtypes = [P, C.c_int]
    lib.xlua_touint.restype = C.c_uint32
    cases = [("0", 0), ("7", 7), ("-1", 4294967295), ("4294967296", 0), ("4294967297", 1),
             ("2.75", 2), ("-0.5", 0), ("-1.5", 4294967295), ("-2.0", 4294967294), ("4294967295.0", 4294967295),
             ("4294967296.5", 0), ("1e10", 1410065408), ("-4294967297.0", 4294967295),
             ("2^53", 0), ("-2^63", 0), ("2^63", 0), ("1e300", 0), ("-1e300", 0), ("1/0", 0), ("-1/0", 0),
             ("0/0", 0), ("'12'", 12), ("'-1'", 4294967295), ("nil", 0), ("true", 0), ("{}", 0)]
    for text, expected in cases:
        push_results(state, "return " + text, 1)
        got = lib.xlua_touint(state, -1)
        assert got == expected, (text, got, expected)
        lib.lua_settop(state, 0)


def check_integer_conversion(state):
    # xlua_tointeger64 applies Lua 5.3's rule for a value going into an
    # integer slot (luaL_checkinteger) on every runtime, LuaJIT included: an
    # integer, a float with an integer value in the 64-bit range or a numeric
    # string that converts to one. valid is 0 for a number or numeric string
    # without one; other values give 0 and stay valid (the C# casters keep
    # reading them as 0). xlua_isinteger_number is the overload check: a
    # number (not a string) with an integer value.
    lib.xlua_tointeger64.argtypes = [P, C.c_int, C.POINTER(C.c_int)]
    lib.xlua_tointeger64.restype = C.c_int64
    lib.xlua_isinteger_number.argtypes = [P, C.c_int]
    lib.xlua_isinteger_number.restype = C.c_int
    jit = is_luajit(state)
    cases = [("0", 0, 1), ("7", 7, 1), ("-1", -1, 1), ("3.0", 3, 1), ("-3.0", -3, 1), ("-0.0", 0, 1),
             ("2^53", 2**53, 1), ("-2^63", -2**63, 1), ("1e10", 10**10, 1), ("4294967296", 2**32, 1),
             ("2.75", 0, 0), ("-2.75", 0, 0), ("0.5", 0, 0), ("-0.5", 0, 0), ("2^51 + 0.5", 0, 0),
             ("0/0", 0, 0), ("1/0", 0, 0), ("-1/0", 0, 0), ("2^63", 0, 0), ("-2^64", 0, 0), ("1e300", 0, 0),
             ("'12'", 12, 1), ("' 7 '", 7, 1), ("'3.0'", 3, 1), ("'0x10'", 16, 1), ("'-12'", -12, 1),
             ("'2.75'", 0, 0), ("'1e300'", 0, 0), ("'12abc'", 0, 1), ("''", 0, 1), ("nil", 0, 1),
             ("true", 0, 1), ("{}", 0, 1), ("print", 0, 1),
             # i64lib values: integers on Lua 5.3+, int64/uint64 userdata (their bits) on LuaJIT.
             ("uint64.parse('18446744073709551615')", -1, 1), ("uint64.parse('9223372036854775807')", 2**63 - 1, 1)]
    if not jit:
        cases += [("math.maxinteger", 2**63 - 1, 1), ("math.mininteger", -2**63, 1)]
    valid = C.c_int(-1)
    for text, expected, expected_valid in cases:
        push_results(state, "return " + text, 1)
        got = lib.xlua_tointeger64(state, -1, C.byref(valid))
        assert (got, valid.value) == (expected, expected_valid), (text, got, valid.value, expected, expected_valid)
        lib.lua_settop(state, 0)
    for text, expected in [("3", 1), ("3.0", 1), ("-2^63", 1), ("2.75", 0), ("0/0", 0), ("1/0", 0), ("2^63", 0),
                           ("'12'", 0), ("nil", 0), ("{}", 0), ("uint64.parse('5')", 0 if jit else 1)]:
        push_results(state, "return " + text, 1)
        got = lib.xlua_isinteger_number(state, -1)
        assert got == expected, (text, got, expected)
        lib.lua_settop(state, 0)


def check_argerror(state):
    # xlua_pushargerror pushes the message luaL_argerror would raise for an
    # argument of the running C function. The C# bridge cannot raise a Lua
    # error from C#, so it throws an exception with this text.
    lib.xlua_pushargerror.argtypes = [P, C.c_int, C.c_char_p]
    lib.xlua_pushargerror.restype = None
    lib.xlua_push_csharp_function.argtypes = [P, P, C.c_int]

    def argerr(L):
        # The last argument is the argument number to report.
        lib.xlua_pushargerror(L, lib.xlua_tointeger(L, lib.lua_gettop(L)), b"number has no integer representation")
        return 1

    callback = CSFunction(argerr)
    lib.xlua_push_csharp_function(state, C.cast(callback, P), 0)
    lib.lua_setglobal(state, b"argerr")
    run(state, """
local function ends(text, suffix)
    assert(text:sub(-#suffix) == suffix, text)
    return text
end
-- luaL_where(L, 1): the line of the Lua code that called the function.
local msg = ends(argerr(2), "bad argument #2 to 'argerr' (number has no integer representation)")
assert(msg:find(":%d+: bad argument") ~= nil, msg)
local t = {m = argerr}
-- A method call does not count self, as in luaL_argerror.
ends(t:m(3), "bad argument #2 to 'm' (number has no integer representation)")
ends(t:m(1), "calling 'm' on bad self (number has no integer representation)")
-- Called from C (pcall): no name is known.
local ok, text = pcall(argerr, 4)
assert(ok)
ends(text, "bad argument #4 to '?' (number has no integer representation)")
argerr = nil
""")
    # No running function at all: the message has no position and no name.
    lib.xlua_pushargerror(state, 1, b"number has no integer representation")
    msg = lib.lua_tolstring(state, -1, None)
    assert msg == b"bad argument #1 (number has no integer representation)", msg
    lib.lua_settop(state, 0)
    del callback


def check_luajit_int64(state):
    # Int64 metamethods overflowed signed arithmetic (INT64_MIN / -1 raises
    # SIGFPE on x86) and cast out-of-range doubles to integers.
    run(state, """
if not jit then return end
-- With UINT_ESPECIALLY, uint64.parse returns the unsigned kind, which prints
-- with a "U" suffix; negating it gives the signed kind with the same bits.
local distinct = tostring(uint64.parse("1")) == "1U"
local min = uint64.parse("9223372036854775808")
if distinct then min = -min end
local function s(v) return tostring(v) end
assert(s(min) == "-9223372036854775808", s(min))
assert(s(min / -1) == "-9223372036854775808")
assert(s(min % -1) == "0")
assert(s(-min) == "-9223372036854775808")
assert(s(min - 1) == "9223372036854775807")
assert(s(min * -1) == "-9223372036854775808")
assert(s(min + 1e300) == "-1")
assert(s(min + (0/0)) == "-9223372036854775808")
if distinct then
    assert(s(uint64.parse("10") ^ 100) == "18446744073709551615U")
    assert(s(uint64.parse("7") / 2) == "3U")
    local ok, err = pcall(function() return min + uint64.parse("1") end)
    assert(not ok and err:find("type not match", 1, true), err)
else
    assert(s(uint64.parse("10") ^ 100) == "9223372036854775807")
    assert(s(uint64.parse("7") / 2) == "3")
end
""")


def check_luajit_int64_helpers(state):
    # LuaJIT only: __concat and the uint64 bitwise, shift and floor division
    # helpers; Lua 5.3+ must not get the helpers (it has native operators).
    run(state, """
if not jit then
    -- Lua 5.3+ has native integers, bitwise operators and //.
    for _, name in ipairs({"band", "bor", "bxor", "bnot", "lshift", "rshift", "arshift", "idiv"}) do
        assert(uint64[name] == nil, name)
    end
    return
end
-- With UINT_ESPECIALLY, uint64.parse returns the unsigned kind ("1U"); unary
-- minus returns the signed kind, so -(-x) is the signed value with x's bits.
local distinct = tostring(uint64.parse("1")) == "1U"
local function I(text) return -(-uint64.parse(text)) end
local MIN, MAX, M1 = I("9223372036854775808"), I("9223372036854775807"), I("18446744073709551615")
local function eq(v, text)
    assert(type(v) == "userdata", type(v))
    assert(tostring(v) == text, tostring(v) .. " ~= " .. text)
end
local function raises(text, f, ...)
    local ok, err = pcall(f, ...)
    assert(not ok and err:find(text, 1, true), tostring(err))
end

-- __concat uses the tostring text on either side; numbers and strings as Lua does.
assert("a" .. MIN == "a-9223372036854775808")
assert(MAX .. "b" == "9223372036854775807b")
assert(1 .. MIN == "1-9223372036854775808")
assert(MIN .. 2.5 == "-92233720368547758082.5")
assert(M1 .. MAX == "-19223372036854775807")
assert("[" .. MIN .. "]" .. 3 == "[-9223372036854775808]3")
assert("x" .. uint64.parse("18446744073709551615") == (distinct and "x18446744073709551615U" or "x-1"))
for _, bad in ipairs({{}, true, print}) do
    raises("attempt to concatenate a " .. type(bad) .. " value", function() return MIN .. bad end)
    raises("attempt to concatenate a " .. type(bad) .. " value", function() return bad .. MIN end)
end
raises("attempt to concatenate a nil value", function() return MIN .. nil end)

-- Bitwise operators; two numbers give an Int64.
eq(uint64.band(MIN, M1), "-9223372036854775808")
eq(uint64.band(0xFF, 0x0F), "15")
eq(uint64.bor(MIN, 1), "-9223372036854775807")
eq(uint64.bxor(M1, MIN), "9223372036854775807")
eq(uint64.bxor(-1, 0), "-1")
eq(uint64.bnot(0), "-1")
eq(uint64.bnot(MIN), "9223372036854775807")
eq(uint64.bnot(M1), "0")

-- Shifts: negative counts shift the other way, 64 or more bits leave 0.
eq(uint64.lshift(1, 0), "1")
eq(uint64.lshift(1, 63), "-9223372036854775808")
eq(uint64.lshift(1, 64), "0")
eq(uint64.lshift(2, -1), "1")
eq(uint64.lshift(M1, -63), "1")
eq(uint64.lshift(M1, -64), "0")
eq(uint64.lshift(M1, MIN), "0")
eq(uint64.lshift(1, MAX), "0")
eq(uint64.lshift(1, 2 ^ 70), "0")
eq(uint64.lshift(3, 1.9), "6")
eq(uint64.rshift(M1, 0), "-1")
eq(uint64.rshift(M1, 63), "1")
eq(uint64.rshift(M1, 64), "0")
eq(uint64.rshift(1, -1), "2")
eq(uint64.rshift(1, -63), "-9223372036854775808")
eq(uint64.rshift(1, -64), "0")
eq(uint64.rshift(MIN, MIN), "0")
eq(uint64.arshift(MIN, 1), "-4611686018427387904")
eq(uint64.arshift(MIN, 63), "-1")
eq(uint64.arshift(MIN, 64), "-1")
eq(uint64.arshift(MIN, MAX), "-1")
eq(uint64.arshift(MAX, 62), "1")
eq(uint64.arshift(MAX, 64), "0")
eq(uint64.arshift(-8, 1), "-4")
eq(uint64.arshift(1, -63), "-9223372036854775808")
eq(uint64.arshift(M1, -64), "0")

-- idiv: floor division, n // 0 raises, INT64_MIN // -1 wraps.
eq(uint64.idiv(7, 2), "3")
eq(uint64.idiv(-7, 2), "-4")
eq(uint64.idiv(7, -2), "-4")
eq(uint64.idiv(-7, -2), "3")
eq(uint64.idiv(-8, 2), "-4")
eq(uint64.idiv(0, -5), "0")
eq(uint64.idiv(MIN, -1), "-9223372036854775808")
eq(uint64.idiv(MIN, M1), "-9223372036854775808")
eq(uint64.idiv(MIN, 1), "-9223372036854775808")
eq(uint64.idiv(MAX, -1), "-9223372036854775807")
eq(uint64.idiv(MIN, MAX), "-2")
eq(uint64.idiv(MAX, MIN), "-1")
eq(uint64.idiv(M1, MIN), "0")
for _, zero in ipairs({0, I("0"), 0.5}) do
    raises("attempt to perform 'n//0'", uint64.idiv, 1, zero)
end

-- Arguments are numbers or Int64 values.
for _, name in ipairs({"band", "bor", "bxor", "lshift", "rshift", "arshift", "idiv"}) do
    raises("Integer64 expected, got string", uint64[name], 1, "2")
    raises("Integer64 expected, got nil", uint64[name], nil, 1)
    raises("Integer64 expected, got no value", uint64[name], 1)
end
raises("Integer64 expected, got no value", uint64.bnot)

local UMAX = uint64.parse("18446744073709551615")
if distinct then
    -- The unsigned kind: results keep it, and it does not mix with Int64.
    eq(uint64.band(UMAX, 255), "255U")
    eq(uint64.bor(uint64.parse("1"), 6), "7U")
    eq(uint64.bnot(uint64.parse("0")), "18446744073709551615U")
    eq(uint64.lshift(uint64.parse("1"), 63), "9223372036854775808U")
    eq(uint64.rshift(UMAX, 60), "15U")
    eq(uint64.arshift(UMAX, 60), "18446744073709551615U")
    eq(uint64.lshift(uint64.parse("1"), UMAX), "0U")
    eq(uint64.rshift(UMAX, uint64.parse("63")), "1U")
    eq(uint64.lshift(MIN, uint64.parse("1")), "0")
    eq(uint64.idiv(UMAX, 2), "9223372036854775807U")
    eq(uint64.idiv(UMAX, -1), "1U")
    eq(uint64.idiv(uint64.parse("7"), uint64.parse("2")), "3U")
    eq(uint64.idiv(uint64.parse("9223372036854775808"), -1), "0U")
    raises("attempt to perform 'n//0'", uint64.idiv, UMAX, uint64.parse("0"))
    for _, name in ipairs({"band", "bor", "bxor", "idiv"}) do
        raises("type not match, lhs is Int64, rhs is UInt64", uint64[name], MIN, UMAX)
        raises("type not match, lhs is UInt64, rhs is Int64", uint64[name], UMAX, MIN)
    end
else
    eq(uint64.band(UMAX, 255), "255")
    eq(uint64.rshift(UMAX, 60), "15")
    eq(uint64.idiv(UMAX, 2), "-1")
end
""")


def check_native_modules(state):
    """LuaSocket and mime put their loaders into package.preload from luaopen_xlua
    when the library includes them (XLUA_LUASOCKET, XLUA_MIME), and nothing is
    registered without them. XLUA_EXPECT_NATIVE_MODULES ("socket,mime", "socket"
    or "") says what the build has; without it the exports decide, which needs an
    XLUA_EXPORTS=all library like the rest of this file."""
    import os
    expected = os.environ.get("XLUA_EXPECT_NATIVE_MODULES")
    if expected is None:
        have = {"socket": hasattr(lib, "luaopen_socket_core"), "mime": hasattr(lib, "luaopen_mime_core")}
    else:
        names = {n.strip() for n in expected.split(",") if n.strip()}
        assert names <= {"socket", "mime"}, expected
        have = {"socket": "socket" in names, "mime": "mime" in names}
    run(state, """
local have = {socket = %s, mime = %s}
-- Only package.preload may provide them: no Lua or C module files from the host.
local path, cpath = package.path, package.cpath
package.path, package.cpath = "", ""
for _, base in ipairs({"socket", "mime"}) do
    for _, name in ipairs({base .. ".core", base}) do
        if have[base] then
            assert(type(package.preload[name]) == "function", name .. " is not in package.preload")
            local module = require(name)
            assert(type(module) == "table" and type(module._VERSION) == "string", name)
        else
            assert(package.preload[name] == nil, name .. " is in package.preload")
            local ok, err = pcall(require, name)
            assert(not ok and string.find(tostring(err), "module '" .. name .. "' not found", 1, true), tostring(err))
        end
    end
end
if have.socket then
    assert(type(require("socket").tcp) == "function" and type(require("socket.core").udp) == "function")
end
if have.mime then
    assert(require("mime").b64("xlua") == "eGx1YQ==")
end
package.path, package.cpath = path, cpath
""" % ("true" if have["socket"] else "false", "true" if have["mime"] else "false"))
    print("native modules in package.preload:", ", ".join(n for n in ("socket", "mime") if have[n]) or "none")


def check_luajit_int64_header(state):
    # LuaJIT only: an Int64 userdata starts with fake_id = -1, the CSharpStruct
    # marker, and the four bytes after it (the struct length) were the type byte
    # and uninitialised padding. Struct accessors then took an int64 for a
    # struct of any length. Fill freed blocks of that size with 0xFF first, so
    # the padding is not zero by chance.
    lib.luaL_loadstring(state, b"return jit ~= nil")
    lib.lua_pcall(state, 0, 1, 0)
    lib.lua_toboolean.argtypes = [P, C.c_int]
    is_jit = lib.lua_toboolean(state, -1)
    lib.lua_settop(state, 0)
    if not is_jit:
        return
    for name, args, res in [("lua_newuserdata", [P, C.c_size_t], P), ("lua_touserdata", [P, C.c_int], P),
                            ("lua_gc", [P, C.c_int, C.c_int], C.c_int), ("lua_pushint64", [P, C.c_int64], None),
                            ("lua_pushuint64", [P, C.c_uint64], None)]:
        getattr(lib, name).argtypes = args
        getattr(lib, name).restype = res
    count = 2000
    for _ in range(count):
        C.memset(lib.lua_newuserdata(state, 16), 0xFF, 16)
        lib.lua_settop(state, 0)
    lib.lua_gc(state, 2, 0)  # LUA_GCCOLLECT
    for i in range(count):
        push, value = (lib.lua_pushint64, -i) if i % 2 else (lib.lua_pushuint64, 2**64 - 1 - i)
        push(state, value)
        p = lib.lua_touserdata(state, -1)
        header = (C.c_int.from_address(p).value, C.c_uint.from_address(p + 4).value)
        # The length is the type byte: 0 for Int64, 1 for the UINT_ESPECIALLY UInt64.
        assert header[0] == -1 and header[1] in (0, 1), (i, header)
        lib.lua_settop(state, 0)


def check_checked_accessors(state):
    # XLUA_CHECK_STRUCT_ACCESSORS: xlua_check_struct_accessors replaces
    # xlua.genaccessor with one whose accessors also require the bridge tag; the
    # default accessors trust any full userdata with a struct header.
    lib.xlua_check_struct_accessors.argtypes = [P]
    lib.xlua_check_struct_accessors.restype = C.c_int
    meta = tagged_meta(state, 5)
    C.memset(lib.xlua_pushstruct(state, 8, meta) + 8, 0, 8)
    lib.lua_setglobal(state, b"tagged")
    C.memset(lib.xlua_pushstruct(state, 8, -2) + 8, 0, 8)
    lib.lua_setglobal(state, b"untagged")
    lib.lua_createtable(state, 0, 0)  # a metatable without the tag
    C.memset(lib.xlua_pushstruct(state, 8, lib.luaL_ref(state, REGISTRY)) + 8, 0, 8)
    lib.lua_setglobal(state, b"plainmeta")
    run(state, """
original_genaccessor = xlua.genaccessor
old_get, old_set = xlua.genaccessor(0, 4)
old_set(untagged, 5)
assert(old_get(untagged) == 5)
""")
    assert lib.xlua_check_struct_accessors(state) == 1
    assert lib.lua_gettop(state) == 0
    run(state, """
assert(xlua.genaccessor ~= original_genaccessor)
-- On LuaJIT the 64-bit fields read back as int64/uint64 userdata.
local function is(v, n) return v == n or tostring(v):match("^" .. n .. "U?$") ~= nil end
for t = 0, 9 do
    local get, set = xlua.genaccessor(0, t)
    assert(is(get(tagged), 0))
    set(tagged, 1)
    assert(is(get(tagged), 1))
    set(tagged, 0)
    for _, bad in ipairs({untagged, plainmeta}) do
        local ok, err = pcall(get, bad)
        assert(not ok and tostring(err):find("invalid c# struct!", 1, true), tostring(err))
        ok, err = pcall(set, bad, 1)
        assert(not ok and tostring(err):find("invalid c# struct!", 1, true), tostring(err))
    end
    assert(not pcall(get, {}) and not pcall(get, 1) and not pcall(get))
end
assert(not pcall(xlua.genaccessor, -1, 4) and not pcall(xlua.genaccessor, 0, 10))
assert(select("#", xlua.genaccessor(0, 4)) == 3)
-- Accessors made before stay unchecked; the others stay as they were.
assert(old_get(untagged) == 5)
assert(not pcall(xlua.structclone, untagged))
saved_xlua = xlua
xlua = nil
""")
    # Without a global xlua table it changes nothing.
    assert lib.xlua_check_struct_accessors(state) == 0
    assert lib.lua_gettop(state) == 0
    run(state, """
xlua = saved_xlua
xlua.genaccessor = original_genaccessor
saved_xlua, original_genaccessor, old_get, old_set, tagged, untagged, plainmeta = nil
""")
    lib.lua_settop(state, 0)


def check_checked_cache(state):
    # XLUA_CHECK_OBJECT_CACHE: xlua_tryget_cachedud_checked takes a cache entry
    # only if it is a tagged userdata that holds the key; xlua_tryget_cachedud
    # returns whatever is stored there.
    for name in ["xlua_tryget_cachedud", "xlua_tryget_cachedud_checked"]:
        getattr(lib, name).argtypes = [P, C.c_int, C.c_int]
        getattr(lib, name).restype = C.c_int
    meta = tagged_meta(state, 9)
    lib.lua_createtable(state, 0, 0)
    cache = lib.luaL_ref(state, REGISTRY)

    def store(key, push):
        lib.xlua_rawgeti(state, REGISTRY, cache)
        push()
        lib.xlua_rawseti(state, -2, key)
        lib.lua_settop(state, 0)

    def userdata(value, size=4, tagged=True):
        def push():
            block = push_userdata(state, size)
            if size >= 4:
                C.c_int.from_address(block).value = value
            if tagged:
                lib.xlua_rawgeti(state, REGISTRY, meta)
                lib.lua_setmetatable(state, -2)
        return push

    store(7, userdata(7))
    assert lib.xlua_tryget_cachedud_checked(state, 7, cache) == 1
    assert lib.lua_gettop(state) == 1 and lib.lua_type(state, -1) == 7  # LUA_TUSERDATA
    assert C.c_int.from_address(lib.lua_touserdata(state, -1)).value == 7
    lib.lua_settop(state, 0)
    misses = [lambda: lib.lua_pushstring(state, b"planted"), lambda: lib.lua_createtable(state, 0, 0),
              userdata(8), userdata(7, tagged=False), userdata(0, size=0),
              lambda: lib.lua_pushlightuserdata(state, lib.xlua_tag())]
    for push in misses:
        store(7, push)
        assert lib.xlua_tryget_cachedud_checked(state, 7, cache) == 0
        assert lib.lua_gettop(state) == 0
        assert lib.xlua_tryget_cachedud(state, 7, cache) == 1  # upstream: any non-nil value
        lib.lua_settop(state, 0)
    assert lib.xlua_tryget_cachedud_checked(state, 12, cache) == 0  # empty slot
    assert lib.xlua_tryget_cachedud(state, 12, cache) == 0
    assert lib.lua_gettop(state) == 0

# ---------------------------------------------------------------- memory snapshot
# build/memory_leak_checker.c, xlua_memory_snapshot: the reachability walk that
# LuaMemoryDiagnostics (Assets/XLua/Src/LuaMemorySnapshot.cs) reads.
import struct

SNAP_ROOT, SNAP_VALUE, SNAP_KEY, SNAP_METATABLE, SNAP_UPVALUE, SNAP_USERVALUE, SNAP_ENVIRONMENT, \
    SNAP_STACK, SNAP_LOCAL, SNAP_FRAME = range(1, 11)
SNAP_KEY_STRING, SNAP_KEY_INTEGER, SNAP_KEY_NUMBER, SNAP_KEY_BOOLEAN = 1, 2, 3, 4
SNAP_TRUNCATED, SNAP_GC_WAS_RUNNING = 1, 2
SNAP_WEAK_KEYS, SNAP_WEAK_VALUES, SNAP_HAS_METATABLE, SNAP_HAS_FINALIZER, SNAP_C_FUNCTION, \
    SNAP_CSHARP_OBJECT, SNAP_CSHARP_STRUCT, SNAP_CURRENT_THREAD = 1, 2, 4, 8, 16, 32, 64, 128
LUA_TTABLE, LUA_TFUNCTION, LUA_TUSERDATA, LUA_TTHREAD = 5, 6, 7, 8


class Snapshot:
    def __init__(self, data):
        (magic, version, header_bytes, node_bytes, edge_bytes, node_count, edge_count, string_bytes,
         self.flags, self.lua_version) = struct.unpack_from("=10i", data, 0)
        self.heap_bytes, self.string_total, self.string_count = struct.unpack_from("=qqi", data, 40)
        assert (magic, version, header_bytes, node_bytes, edge_bytes) == (0x534D4C58, 1, 64, 40, 32)
        assert len(data) == header_bytes + node_count * node_bytes + edge_count * edge_bytes + string_bytes
        self.nodes = [dict(zip(("address", "size", "type", "flags", "count", "csobj", "info", "parent"),
                               struct.unpack_from("=Qq6i", data, header_bytes + i * node_bytes)))
                      for i in range(node_count)]
        base = header_bytes + node_count * node_bytes
        self.edges = [dict(zip(("from", "to", "kind", "key_type", "key", "number"),
                               struct.unpack_from("=4iqd", data, base + i * edge_bytes)))
                      for i in range(edge_count)]
        self.pool = data[base + edge_count * edge_bytes:]
        self.by_address = {n["address"]: i for i, n in enumerate(self.nodes)}

    def string(self, offset):
        length = struct.unpack_from("=i", self.pool, offset)[0]
        return self.pool[offset + 4:offset + 4 + length].decode("utf-8", "replace")

    def info(self, node):
        return None if self.nodes[node]["info"] < 0 else self.string(self.nodes[node]["info"])

    def key(self, edge):
        if edge["key_type"] == SNAP_KEY_STRING:
            return self.string(edge["key"])
        if edge["key_type"] in (SNAP_KEY_INTEGER, SNAP_KEY_BOOLEAN):
            return edge["key"]
        if edge["key_type"] == SNAP_KEY_NUMBER:
            return edge["number"]
        return None

    def node(self, address):
        return self.by_address.get(address)

    def path(self, node):
        """The keys from a root down to the node, along the edges that reached each node first."""
        parts = []
        while node >= 0:
            edge = self.edges[self.nodes[node]["parent"]]
            parts.append((edge["kind"], self.key(edge)))
            node = edge["from"]
        return list(reversed(parts))

    def referrers(self, node):
        return [e for e in self.edges if e["to"] == node]


def snapshot_built():
    """Whether the library has the walk (CMake XLUA_MEMORY_SNAPSHOT). XLUA_EXPECT_MEMORY_SNAPSHOT=ON or OFF
    says how it was built and fails the check otherwise; unset, the library's exports decide."""
    built = hasattr(lib, "xlua_memory_snapshot")
    expect = os.environ.get("XLUA_EXPECT_MEMORY_SNAPSHOT", "").upper()
    if expect in ("ON", "1", "TRUE", "YES"):
        assert built, "XLUA_EXPECT_MEMORY_SNAPSHOT=ON, but the library has no xlua_memory_snapshot"
    elif expect in ("OFF", "0", "FALSE", "NO"):
        assert not built, "XLUA_EXPECT_MEMORY_SNAPSHOT=OFF, but the library has xlua_memory_snapshot"
        assert not hasattr(lib, "xlua_memory_snapshot_free")
    if not built and not getattr(snapshot_built, "noted", False):
        snapshot_built.noted = True
        print("memory_snapshot checks skipped: built without XLUA_MEMORY_SNAPSHOT")
    return built


def snapshot(state, max_objects=0):
    lib.xlua_memory_snapshot.argtypes = [P, C.c_int, C.POINTER(P), C.POINTER(C.c_int)]
    lib.xlua_memory_snapshot.restype = C.c_int
    lib.xlua_memory_snapshot_free.argtypes = [P]
    buffer, size = P(), C.c_int()
    status = lib.xlua_memory_snapshot(state, max_objects, C.byref(buffer), C.byref(size))
    if status != 0:
        assert not buffer.value and size.value == 0, (status, buffer, size)
        return status, None
    data = C.string_at(buffer, size.value)
    lib.xlua_memory_snapshot_free(buffer)
    return status, Snapshot(data)


def fresh_state():
    lib.lua_getglobal.argtypes = [P, C.c_char_p]
    s = lib.luaL_newstate()
    assert s
    lib.luaopen_xlua(s)
    lib.luaopen_i64lib(s)
    return s


def pointer_of(state, expression):
    lib.lua_topointer.argtypes = [P, C.c_int]
    lib.lua_topointer.restype = P
    push_results(state, "return " + expression, 1)
    p = lib.lua_topointer(state, -1)
    lib.lua_settop(state, -2)
    return p


def global_flag(state, expression):
    push_results(state, "return " + expression, 1)
    value = lib.lua_toboolean(state, -1)
    lib.lua_settop(state, -2)
    return value


def check_memory_snapshot_tables(state):
    if not snapshot_built():
        return
    s = fresh_state()
    try:
        # Stopped, so that the weakly held tables below are not collected before the walk.
        run(s, """
collectgarbage("stop")
root = {child = {}, [5] = {}, [1.5] = {}, [true] = {}, list = {{}, {}, {}}}
key = {}
root[key] = {}
-- A cycle: each table is reported once and both references are edges.
a, b = {}, {}
a.b, b.a, a.self = b, a, a
-- Weak values are not references; weak keys still keep their values.
weak_values = setmetatable({{}}, {__mode = "v"})
weak_keys = setmetatable({}, {__mode = "k"})
weak_keys[{}] = {}
""")
        status, snap = snapshot(s)
        assert status == 0, status
        assert not snap.flags & (SNAP_GC_WAS_RUNNING | SNAP_TRUNCATED), snap.flags
        root = snap.node(pointer_of(s, "root"))
        assert snap.nodes[root]["type"] == LUA_TTABLE and snap.nodes[root]["count"] == 6, snap.nodes[root]
        assert snap.path(root) == [(SNAP_ROOT, "_G"), (SNAP_VALUE, "root")], snap.path(root)
        for expression, last in [("root.child", "child"), ("root[5]", 5), ("root[1.5]", 1.5), ("root[true]", 1),
                                 ("root.list[3]", 3), ("root[key]", None)]:
            node = snap.node(pointer_of(s, expression))
            assert node is not None, expression
            assert snap.path(node)[-1] == (SNAP_VALUE, last), (expression, snap.path(node))
        assert snap.path(snap.node(pointer_of(s, "key")))[-1] == (SNAP_VALUE, "key")
        assert any(e["kind"] == SNAP_KEY and e["from"] == root for e in snap.referrers(snap.node(pointer_of(s, "key"))))
        # Sizes in bytes come from Lua's internals on 5.3 to 5.5; LuaJIT reports -1.
        if snap.lua_version >= 503:
            assert snap.nodes[root]["size"] > 6 * 16, snap.nodes[root]
        else:
            assert snap.nodes[root]["size"] == -1
        node_a, node_b = snap.node(pointer_of(s, "a")), snap.node(pointer_of(s, "b"))
        assert {(e["from"], snap.key(e)) for e in snap.referrers(node_a)} >= {(node_b, "a"), (node_a, "self")}
        assert len([n for n in snap.nodes if n["address"] == snap.nodes[node_a]["address"]]) == 1
        weak = snap.node(pointer_of(s, "weak_values"))
        assert snap.nodes[weak]["flags"] & SNAP_WEAK_VALUES and snap.nodes[weak]["count"] == 1
        assert snap.node(pointer_of(s, "weak_values[1]")) is None, "a weak value was reported as reachable"
        wk = snap.node(pointer_of(s, "weak_keys"))
        assert snap.nodes[wk]["flags"] & SNAP_WEAK_KEYS
        assert snap.node(pointer_of(s, "next(weak_keys)")) is None, "a weak key was reported as reachable"
        assert snap.node(pointer_of(s, "select(2, next(weak_keys))")) is not None
        # The string metatable is a root; _G is the first root.
        assert snap.path(snap.node(pointer_of(s, "getmetatable('')")))[0] == (SNAP_ROOT, "metatable(string)")
        assert snap.edges[0]["kind"] == SNAP_ROOT and snap.key(snap.edges[0]) == "_G"
        assert snap.string_count > 10 and snap.string_total > snap.string_count, (snap.string_count, snap.string_total)
        assert snap.heap_bytes > 0
        # Two snapshots of an unchanged state agree; a running collector is stopped and restarted.
        run(s, "collectgarbage('restart')")
        status, again = snapshot(s)
        assert status == 0 and len(again.nodes) == len(snap.nodes), (len(again.nodes), len(snap.nodes))
        assert again.flags & SNAP_GC_WAS_RUNNING, again.flags
        assert global_flag(s, "collectgarbage('isrunning')"), "the collector was not restarted"
    finally:
        lib.lua_close(s)


def check_memory_snapshot_userdata(state):
    # https://github.com/Tencent/xLua/issues/1020: userdata, xLua's C# object
    # references and objects with __gc (finobj on Lua 5.4 and 5.5).
    if not snapshot_built():
        return
    s = fresh_state()
    try:
        lib.xlua_pushcsobj.argtypes = [P, C.c_int, C.c_int, C.c_int, C.c_int]
        meta = tagged_meta(s)
        lib.lua_createtable(s, 0, 0)
        cache = lib.luaL_ref(s, REGISTRY)
        lib.xlua_pushcsobj(s, 42, meta, 1, cache)
        lib.lua_setglobal(s, b"csobj")
        struct_meta = tagged_meta(s, 7)
        lib.xlua_pushstruct(s, 12, struct_meta)
        lib.lua_setglobal(s, b"cstruct")
        push_userdata(s, 24)
        lib.lua_setglobal(s, b"plain")
        run(s, """
finalized = setmetatable({}, {__gc = function() end})
holder = {inner = setmetatable({}, {__gc = function() end})}
""")
        if hasattr(lib, "lua_setiuservalue"):  # Lua 5.4 and 5.5: two user values
            lib.lua_newuserdatauv.argtypes = [P, C.c_size_t, C.c_int]
            lib.lua_newuserdatauv.restype = P
            lib.lua_setiuservalue.argtypes = [P, C.c_int, C.c_int]
            lib.lua_newuserdatauv(s, 8, 2)
            for n in (1, 2):
                run(s, "uv%d = {}" % n)
                lib.lua_getglobal(s, b"uv%d" % n)
                lib.lua_setiuservalue(s, -2, n)
        elif hasattr(lib, "lua_setuservalue"):  # Lua 5.3
            lib.lua_setuservalue.argtypes = [P, C.c_int]
            push_userdata(s, 8)
            run(s, "uv1 = {}")
            lib.lua_getglobal(s, b"uv1")
            lib.lua_setuservalue(s, -2)
        else:  # LuaJIT: the environment table
            lib.lua_setfenv.argtypes = [P, C.c_int]
            push_userdata(s, 8)
            run(s, "uv1 = {}")
            lib.lua_getglobal(s, b"uv1")
            assert lib.lua_setfenv(s, -2) == 1
        lib.lua_setglobal(s, b"with_values")
        status, snap = snapshot(s)
        assert status == 0, status
        obj = snap.node(pointer_of(s, "csobj"))
        assert snap.nodes[obj]["type"] == LUA_TUSERDATA and snap.nodes[obj]["csobj"] == 42, snap.nodes[obj]
        assert snap.nodes[obj]["flags"] & SNAP_CSHARP_OBJECT and snap.nodes[obj]["size"] == 4
        assert snap.path(obj) == [(SNAP_ROOT, "_G"), (SNAP_VALUE, "csobj")], snap.path(obj)
        # The cache table is a weak-valued table in xLua; here it is strong, so it refers to it too.
        assert any(snap.key(e) == 42 for e in snap.referrers(obj)), snap.referrers(obj)
        st = snap.node(pointer_of(s, "cstruct"))
        assert snap.nodes[st]["flags"] & SNAP_CSHARP_STRUCT and snap.nodes[st]["csobj"] == -1, snap.nodes[st]
        plain = snap.node(pointer_of(s, "plain"))
        assert snap.nodes[plain]["size"] == 24 and not snap.nodes[plain]["flags"] & SNAP_HAS_METATABLE
        for name in ("finalized", "holder.inner"):
            node = snap.node(pointer_of(s, name))
            assert node is not None and snap.nodes[node]["flags"] & SNAP_HAS_FINALIZER, (name, node)
        holder = snap.node(pointer_of(s, "with_values"))
        values = [e for e in snap.edges if e["from"] == holder and e["kind"] in (SNAP_USERVALUE, SNAP_ENVIRONMENT)]
        expected = 2 if hasattr(lib, "lua_setiuservalue") else 1
        assert len(values) == expected and snap.nodes[holder]["count"] == expected, values
        assert snap.nodes[values[0]["to"]]["address"] == pointer_of(s, "uv1")
    finally:
        lib.lua_close(s)


def check_memory_snapshot_functions(state):
    # Upvalues, coroutines (stack, frames, locals) and the registry.
    if not snapshot_built():
        return
    s = fresh_state()
    try:
        run(s, """
local captured = {}
function closure() return captured end
co = coroutine.create(function(arg)
    local inner = {}
    coroutine.yield(inner)
end)
assert(coroutine.resume(co, {}))
fresh_co = coroutine.create(function() end)
""")
        lib.lua_createtable(s, 0, 0)
        ref = lib.luaL_ref(s, REGISTRY)
        status, snap = snapshot(s)
        assert status == 0, status
        fn = snap.node(pointer_of(s, "closure"))
        assert snap.nodes[fn]["type"] == LUA_TFUNCTION and snap.nodes[fn]["count"] >= 1
        assert snap.info(fn).endswith(":3"), snap.info(fn)
        up = snap.node(pointer_of(s, "select(2, debug.getupvalue(closure, 1))"))
        assert snap.path(up)[-1] == (SNAP_UPVALUE, "captured"), snap.path(up)
        co = snap.node(pointer_of(s, "co"))
        assert snap.nodes[co]["type"] == LUA_TTHREAD and snap.info(co) == "suspended", snap.info(co)
        local_edges = [e for e in snap.edges if e["from"] == co and e["kind"] == SNAP_LOCAL]
        names = {snap.key(e).split(" @ ")[0] for e in local_edges}
        assert {"arg", "inner"} <= names, names
        frames = [e for e in snap.edges if e["from"] == co and e["kind"] == SNAP_FRAME]
        assert frames, "no frame of the suspended coroutine"
        fresh = snap.node(pointer_of(s, "fresh_co"))
        # A coroutine that has not started holds its function on its stack.
        assert any(e["kind"] == SNAP_STACK for e in snap.edges if e["from"] == fresh)
        assert snap.info(fresh) == "suspended", snap.info(fresh)
        lib.xlua_rawgeti(s, REGISTRY, ref)
        lib.lua_topointer.argtypes = [P, C.c_int]
        lib.lua_topointer.restype = P
        ref_table = snap.node(lib.lua_topointer(s, -1))
        lib.lua_settop(s, 0)
        assert snap.path(ref_table) == [(SNAP_ROOT, "registry"), (SNAP_VALUE, ref)], snap.path(ref_table)
        current = [i for i, n in enumerate(snap.nodes) if n["flags"] & SNAP_CURRENT_THREAD]
        assert len(current) == 1 and snap.info(current[0]) == "running"
    finally:
        lib.lua_close(s)


def check_memory_snapshot_limits(state):
    # A chain much deeper than the C stack would allow with recursion, and max_objects.
    if not snapshot_built():
        return
    s = fresh_state()
    try:
        run(s, """
chain = {}
local t = chain
for i = 1, 200000 do t.next = {}; t = t.next end
""")
        status, snap = snapshot(s)
        assert status == 0, status
        # Breadth first: the end of the chain is the last table found, 200000 steps below _G.chain.
        last = max(i for i, n in enumerate(snap.nodes) if n["type"] == LUA_TTABLE)
        path = snap.path(last)
        assert len(path) == 200002 and path[1] == (SNAP_VALUE, "chain"), path[:3]
        status, small = snapshot(s, 100)
        assert status == 0 and len(small.nodes) == 100 and small.flags & SNAP_TRUNCATED
    finally:
        lib.lua_close(s)


def check_memory_snapshot_reentrancy(state):
    # https://github.com/Tencent/xLua/issues/1149: a snapshot taken from a C
    # function that Lua called (as a C# callback is), from a finalizer, inside a
    # coroutine and with a debug hook set, runs no hook, finalizer or collector
    # step of its own and leaves the collector and the hook as they were.
    if not snapshot_built():
        return
    s = fresh_state()
    taken = []

    def snap_callback(L):
        taken.append(snapshot(L))
        return 0

    callback = CSFunction(snap_callback)
    try:
        lib.lua_pushcclosure.argtypes = [P, P, C.c_int]
        lib.lua_pushcclosure(s, C.cast(callback, P), 0)
        lib.lua_setglobal(s, b"snap")
        run(s, """
function from_lua()
    local marker, shared = {}, {}
    shared_marker = shared
    snap()
    return marker, shared
end
from_lua()
""")
        status, snap = taken.pop()
        assert status == 0, status
        # Reached from _G and from a local: the path goes through _G, the thread is the last root.
        assert snap.path(snap.node(pointer_of(s, "shared_marker"))) == [(SNAP_ROOT, "_G"), (SNAP_VALUE, "shared_marker")]
        current = [i for i, n in enumerate(snap.nodes) if n["flags"] & SNAP_CURRENT_THREAD][0]
        names = [snap.key(e).split(" @ ")[0] for e in snap.edges if e["from"] == current and e["kind"] == SNAP_LOCAL]
        assert "marker" in names, names
        frames = [snap.key(e) for e in snap.edges if e["from"] == current and e["kind"] == SNAP_FRAME]
        assert any(f.endswith(":5") for f in frames), frames
        # Inside a finalizer, while the collector runs.
        # Lua 5.1 and LuaJIT run __gc for userdata only: newproxy makes one.
        run(s, """
finalizer_runs = 0
local function finalized(fn)
    if newproxy then
        local p = newproxy(true)
        getmetatable(p).__gc = fn
        return p
    end
    return setmetatable({payload = {}}, {__gc = fn})
end
for i = 1, 20 do
    finalized(function(o) finalizer_runs = finalizer_runs + 1; snap() end)
end
collectgarbage()
collectgarbage()
assert(finalizer_runs == 20, finalizer_runs)
assert(collectgarbage("isrunning"))
""")
        assert len(taken) == 20 and all(status == 0 for status, _ in taken), [st for st, _ in taken]
        # The object being finalized is the finalizer's argument, so the walk reaches it.
        assert all(any(e["kind"] == SNAP_LOCAL and snap.key(e).startswith("o @") for e in snap.edges)
                   for _, snap in taken)
        taken.clear()
        # Inside a coroutine: that thread is the current one, the main thread is "normal".
        run(s, """
co = coroutine.wrap(function() local in_co = {} snap() return in_co end)
co()
""")
        status, snap = taken.pop()
        assert status == 0
        current = [i for i, n in enumerate(snap.nodes) if n["flags"] & SNAP_CURRENT_THREAD]
        assert len(current) == 1 and any(e["kind"] == SNAP_ROOT and snap.key(e) == "thread"
                                         for e in snap.referrers(current[0])), snap.referrers(current[0])
        if snap.lua_version >= 502:  # Lua 5.1 and LuaJIT keep no main thread in the registry
            assert any(snap.info(i) == "normal" for i, n in enumerate(snap.nodes) if n["type"] == LUA_TTHREAD)
        # A hook stays set and is not called by the snapshot; a stopped collector stays stopped.
        run(s, """
hook_calls = 0
debug.sethook(function() hook_calls = hook_calls + 1 end, "cr")
""")
        # Each read counts its own call and return, so compare with two reads in a row.
        first = run_value(s, "hook_calls")
        baseline = run_value(s, "hook_calls") - first
        before = run_value(s, "hook_calls")
        assert snapshot(s)[0] == 0
        assert run_value(s, "hook_calls") - before == baseline, "the snapshot ran the hook"
        push_results(s, "return debug.gethook() ~= nil", 1)
        assert lib.lua_toboolean(s, -1), "the snapshot removed the hook"
        lib.lua_settop(s, 0)
        run(s, "debug.sethook(); collectgarbage('stop')")
        assert snapshot(s)[0] == 0
        assert not global_flag(s, "collectgarbage('isrunning')"), "the snapshot restarted a stopped collector"
        assert not snapshot(s)[1].flags & SNAP_GC_WAS_RUNNING
        run(s, "collectgarbage('restart')")
    finally:
        lib.lua_close(s)


def run_value(state, expression):
    push_results(state, "return " + expression, 1)
    value = lib.xlua_tointeger(state, -1)
    lib.lua_settop(state, -2)
    return value


def check_memory_snapshot_core_limit(state):
    # A core state with LuaOptions.MemoryLimit (xlua_core.c): a snapshot from a C#
    # function, where the allocator grants overdrafts, is a protected entry, so the
    # walk's allocations over the limit are refused and it returns status 3; no
    # overdraft is left for the trampoline to raise as a memory error afterwards.
    if not snapshot_built() or is_luajit(state):
        return
    lib.xlua_push_csharp_function.argtypes = [P, P, C.c_int]
    lib.xlua_core_set_memory_limit.argtypes = [P, C.c_int64]
    lib.xlua_core_set_memory_limit.restype = C.c_int
    lib.xlua_core_close.argtypes = [P]
    L, status = core_newstate()
    assert L and status == 0
    results = []

    def snap_in_csharp(state_):
        results.append(snapshot(state_)[0])
        return 0

    callback = CSFunction(snap_in_csharp)
    try:
        lib.luaopen_xlua(L)
        lib.xlua_push_csharp_function(L, C.cast(callback, P), 0)
        lib.lua_setglobal(L, b"snap_in_csharp")
        run(L, "big = {} for i = 1, 20000 do big[i] = {} end collectgarbage()")
        found, stats = core_stats(L)
        assert found == 1
        # Room for a few small allocations, not for the walk's queue of 20000 entries.
        assert lib.xlua_core_set_memory_limit(L, stats.memory_used + 4096) == 1
        run(L, "snap_in_csharp()")
        assert results == [3], results
        found, after = core_stats(L)
        assert after.overdrafts == stats.overdrafts and after.refusals > stats.refusals, (after.overdrafts, after.refusals)
        assert lib.xlua_core_set_memory_limit(L, 0) == 1
        run(L, "snap_in_csharp()")
        assert results == [3, 0], results
    finally:
        lib.xlua_core_close(L)


def check_memory_snapshot_out_of_memory(state):
    # A Lua allocation that fails during the walk is a status code, not a Lua
    # error through the caller, and leaves a usable state.
    if not snapshot_built():
        return
    # The C runtime's realloc and free: the process itself on POSIX, the
    # Universal CRT on Windows (CDLL(None) is POSIX only).
    libc = C.CDLL("ucrtbase") if sys.platform == "win32" else C.CDLL(None)
    libc.realloc.argtypes = [P, C.c_size_t]
    libc.realloc.restype = P
    libc.free.argtypes = [P]
    fail = [False]
    Alloc = C.CFUNCTYPE(P, P, P, C.c_size_t, C.c_size_t)

    def alloc(ud, ptr, osize, nsize):
        if nsize == 0:
            libc.free(ptr)
            return None
        if fail[0] and nsize > (osize if ptr else 0):
            return None
        return libc.realloc(ptr, nsize)

    allocator = Alloc(alloc)
    lib.lua_newstate.restype = P
    if hasattr(lib, "luaL_openselectedlibs"):  # Lua 5.5: lua_newstate(f, ud, seed)
        lib.lua_newstate.argtypes = [P, P, C.c_uint]
        s = lib.lua_newstate(C.cast(allocator, P), None, 0)
    else:
        lib.lua_newstate.argtypes = [P, P]
        s = lib.lua_newstate(C.cast(allocator, P), None)
    if not s:  # LuaJIT without GC64 has no lua_newstate with a custom allocator
        return
    try:
        lib.luaopen_xlua(s)
        run(s, "big = {} for i = 1, 1000 do big[i] = {} end")
        fail[0] = True
        status, snap = snapshot(s)
        fail[0] = False
        assert status == 3 and snap is None, status
        assert global_flag(s, "collectgarbage('isrunning')")
        run(s, "big[1001] = {}")
        status, snap = snapshot(s)
        assert status == 0 and snap.node(pointer_of(s, "big[1001]")) is not None
    finally:
        lib.lua_close(s)


class CoreConfig(C.Structure):  # xlua_core_config in build/xlua_core.h
    _fields_ = [("memory_limit", C.c_int64), ("seed", C.c_uint32), ("has_seed", C.c_int32),
                ("libs", C.c_int32), ("sandbox", C.c_int32)]


class CoreStats(C.Structure):  # xlua_core_stats in build/xlua_core.h
    _fields_ = [(n, C.c_int64) for n in ("memory_used", "memory_peak", "memory_limit", "refusals", "overdrafts",
                                         "host_oom", "instructions", "stop_reason", "instruction_limit",
                                         "time_limit_ns")]


XLUA_ERRMEM, XLUA_ERRCONFIG = 4, 23


def core_newstate(limit=0, seed=None):
    """xlua_core_newstate; returns (state or None, status)."""
    lib.xlua_core_newstate.argtypes = [C.POINTER(CoreConfig), C.POINTER(C.c_int32)]
    lib.xlua_core_newstate.restype = P
    cfg = CoreConfig(limit, seed or 0, int(seed is not None), 0, 0)
    status = C.c_int32(-1)
    return lib.xlua_core_newstate(C.byref(cfg), C.byref(status)), status.value


def core_stats(L):
    lib.xlua_core_get_stats.argtypes = [P, C.POINTER(CoreStats)]
    lib.xlua_core_get_stats.restype = C.c_int
    stats = CoreStats()
    return lib.xlua_core_get_stats(L, C.byref(stats)), stats


def lua_version(state):
    push_results(state, "return _VERSION", 1)
    v = lib.lua_tolstring(state, -1, None).decode()
    lib.lua_settop(state, 0)
    return v


def check_core_state(state):
    # The core layer (build/xlua_core.c): xlua_core_newstate with the limited
    # allocator and the hash seed, the overdraft rule in the C# function
    # trampolines and the statistics. The managed side of the same rules is in
    # luaCallCsCoverage.lua (CaseLuaOptions*).
    lib.xlua_core_version.restype = C.c_int
    assert lib.xlua_core_version() == 5
    lib.xlua_core_close.argtypes = [P]
    lib.xlua_core_set_memory_limit.argtypes = [P, C.c_int64]
    lib.xlua_core_set_memory_limit.restype = C.c_int
    lib.luaL_openlibs.argtypes = [P]
    # A state that luaL_newstate created has no core state.
    found, stats = core_stats(state)
    assert found == 0 and stats.memory_used == 0
    assert lib.xlua_core_set_memory_limit(state, 1 << 20) == 0
    version = "LuaJIT" if is_luajit(state) else lua_version(state)
    if version == "LuaJIT":
        # LuaJIT keeps its own allocator: no limit and no seed, but a plain state.
        assert core_newstate(limit=1 << 20) == (None, XLUA_ERRCONFIG)
        assert core_newstate(seed=1) == (None, XLUA_ERRCONFIG)
        L, status = core_newstate()
        assert L and status == 0
        assert core_stats(L)[0] == 0 and lib.xlua_core_set_memory_limit(L, 1) == 0
        lib.xlua_core_close(L)
        return
    if version == "Lua 5.3":
        assert core_newstate(seed=1) == (None, XLUA_ERRCONFIG)
    # A limit below the core struct itself, then one below the first block of lua_newstate.
    assert core_newstate(limit=1) == (None, XLUA_ERRMEM)
    assert core_newstate(limit=600) == (None, XLUA_ERRMEM)

    L, status = core_newstate()
    assert L and status == 0
    try:
        lib.luaL_openlibs(L)
        found, stats = core_stats(L)
        assert found == 1 and stats.memory_limit == 0 and 0 < stats.memory_used <= stats.memory_peak
        base = stats.memory_used
        # A refused growth is Lua's memory error, which pcall catches; the state stays usable.
        assert lib.xlua_core_set_memory_limit(L, base + 256 * 1024) == 1
        run(L, """
local t = {}
local ok, err = pcall(function() for i = 1, 1e7 do t[i] = ('x'):rep(64) .. i end end)
assert(not ok and err == 'not enough memory', tostring(err))
t = nil
collectgarbage()
assert(#('y'):rep(1000) == 1000)
""")
        found, stats = core_stats(L)
        assert stats.refusals > 0 and stats.memory_peak <= base + 256 * 1024, (stats.refusals, stats.memory_peak)
        # A limit below the current use: the next growth fails, shrinking and freeing still work.
        assert lib.xlua_core_set_memory_limit(L, 1024) == 1
        assert lib.luaL_loadstring(L, b"return {1, 2, 3}") == 4  # LUA_ERRMEM from the parser
        lib.lua_settop(L, 0)
        assert lib.xlua_core_set_memory_limit(L, 0) == 1
        run(L, "collectgarbage()")

        # Overdraft: a C# function's own allocations are granted over the limit,
        # and the trampoline raises the memory error after it returns.
        for name, args, res in [("xlua_push_csharp_function", [P, P, C.c_int], None),
                                ("xlua_csharp_str_error", [P, C.c_char_p], C.c_int),
                                ("lua_getglobal", [P, C.c_char_p], C.c_int)]:
            getattr(lib, name).argtypes = args
            getattr(lib, name).restype = res
        big = b"z" * (512 * 1024)

        def push_big(state_):
            lib.lua_pushstring(state_, big)
            return 1

        def push_big_and_fail(state_):
            lib.lua_pushstring(state_, big)
            return lib.xlua_csharp_str_error(state_, b"also a C# error")

        def small(state_):
            lib.xlua_pushinteger(state_, 7)
            return 1

        def nested(state_):
            # C# calling Lua without a protected entry (lua_callk): push_big's
            # overdraft cannot raise while this frame is on the stack; it is
            # checked again when nested returns.
            lib.lua_getglobal(state_, b"call_big")
            if hasattr(lib, "lua_callk"):
                lib.lua_callk.argtypes = [P, C.c_int, C.c_int, P, P]
                lib.lua_callk(state_, 0, 0, None, None)
            lib.xlua_pushinteger(state_, 1)
            return 1

        def tiny(state_):
            lib.lua_pushstring(state_, b"a new tiny string")
            return 1

        def unlimit(state_):
            # The limit is removed while the overdraft is pending: nothing to raise.
            lib.lua_pushstring(state_, big)
            lib.xlua_core_set_memory_limit(state_, 0)
            return 1

        callbacks = [CSFunction(f) for f in (push_big, push_big_and_fail, small, nested, tiny, unlimit)]
        for name, fn in zip([b"push_big", b"push_big_and_fail", b"small", b"nested", b"tiny", b"unlimit"], callbacks):
            lib.xlua_push_csharp_function(L, C.cast(fn, P), 0)
            lib.lua_setglobal(L, name)
        run(L, "collectgarbage()")
        found, stats = core_stats(L)
        assert lib.xlua_core_set_memory_limit(L, stats.memory_used + 64 * 1024) == 1
        run(L, """
local function nomem(f)
    local ok, err = pcall(f)
    assert(not ok and err == 'not enough memory', tostring(err))
end
nomem(push_big)
-- The big string is garbage now, but the state is over the limit until a
-- collection: the next overdraft collects before it decides to raise.
assert(tiny() == 'a new tiny string')
nomem(push_big_and_fail)
assert(small() == 7)       -- no overdraft left, and the error flag was reset
-- nested: push_big's overdraft cannot raise inside it (a C# frame is still
-- on the stack); when nested returns, its string is garbage and the
-- collection brings the state back under the limit.
function call_big() local s = push_big() end
assert(nested() == 1)
assert(small() == 7)
""")
        found, stats = core_stats(L)
        assert stats.overdrafts >= 3, stats.overdrafts
        limit = stats.memory_limit
        run(L, "assert(#unlimit() == 512 * 1024)")
        found, stats = core_stats(L)
        assert stats.memory_limit == 0
        run(L, "collectgarbage()")
        assert lib.xlua_core_set_memory_limit(L, limit) == 1

        # The error handler gets a reserve above the limit for its traceback.
        lib.errorfunc.argtypes = [P]
        run(L, "collectgarbage()")
        found, stats = core_stats(L)
        assert lib.xlua_core_set_memory_limit(L, stats.memory_used + 32 * 1024) == 1
        lib.get_error_func_ref.argtypes = [P]
        handler = lib.get_error_func_ref(L)
        lib.xlua_rawgeti(L, REGISTRY, handler)
        # A linked list fills the state up to the limit in small steps and stays
        # reachable; error(msg, 0) itself allocates nothing.
        assert lib.luaL_loadstring(L, b"""
local msg, head = 'boom at the limit', nil
pcall(function() while true do head = {head} end end)
error(msg, 0)
""") == 0
        assert lib.lua_pcall(L, 0, 0, 1) == LUA_ERRRUN
        message = lib.lua_tolstring(L, -1, None).decode()
        assert "boom at the limit" in message and "stack traceback" in message, message
        lib.lua_settop(L, 0)
        assert lib.xlua_core_set_memory_limit(L, 0) == 1
        run(L, "push_big, push_big_and_fail, small, nested, call_big, tiny, unlimit = nil collectgarbage()")
        del callbacks
    finally:
        lib.xlua_core_close(L)

    # The hash seed (Lua 5.4 through the luaconf.h hook, 5.5 through lua_newstate):
    # the same seed gives the same pairs order, other seeds another one.
    if version != "Lua 5.3":
        orders = {}
        for seed in (42, 42, 43, None):
            L, status = core_newstate(seed=seed)
            assert L and status == 0
            lib.luaL_openlibs(L)
            push_results(L, """
local t = {}
for i = 1, 64 do t['key' .. i] = i end
local keys = {}
for k in pairs(t) do keys[#keys + 1] = k end
return table.concat(keys, ',')
""", 1)
            orders.setdefault(seed, []).append(lib.lua_tolstring(L, -1, None))
            lib.xlua_core_close(L)
        assert orders[42][0] == orders[42][1]
        assert orders[42][0] != orders[43][0]


def check_core_host_oom(state):
    # The C library's own failure (xlua_core_alloc counts it as host_oom):
    # Linux only, with an address-space limit; ASan reserves too much address space for that.
    if not sys.platform.startswith("linux") or "asan" in os.environ.get("LD_PRELOAD", "") or is_luajit(state):
        print("core_host_oom: skipped (needs Linux without ASan, PUC Lua)")
        return
    import resource
    L, status = core_newstate()
    assert L and status == 0
    lib.luaL_openlibs.argtypes = [P]
    lib.luaL_openlibs(L)
    soft, hard = resource.getrlimit(resource.RLIMIT_AS)
    vm = int(next(line for line in open("/proc/self/status") if line.startswith("VmSize:")).split()[1]) * 1024
    try:
        resource.setrlimit(resource.RLIMIT_AS, (vm + (256 << 20), hard))
        run(L, """
-- Lua 5.3's buffer says "not enough memory for buffer allocation".
local ok, err = pcall(string.rep, 'x', 1 << 30)
assert(not ok and tostring(err):find('not enough memory', 1, true), tostring(err))
""")
    finally:
        resource.setrlimit(resource.RLIMIT_AS, (soft, hard))
    found, stats = core_stats(L)
    assert stats.host_oom >= 1, stats.host_oom
    lib.xlua_core_close(L)


XLUA_LIMIT_INSTR, XLUA_LIMIT_TIME, XLUA_INTERRUPTED = 10, 11, 12
CLOCK_CHECK, SMALL_SLICES = 1, 2


def check_core_limits(state):
    # Instruction and time limits of core states (build/xlua_core.c,
    # core-luascript.md sections 3.5, 3.6 and 3.11): the budget hook, the stop
    # hook, the coroutine switches, Interrupt, the watchdog entry point, the C
    # budget of the string and table libraries, and the profiler on the same
    # hook. Lua 5.4 and 5.5; Lua 5.3 and LuaJIT have no limits.
    import threading
    import time
    for name, args, res in [("xlua_core_set_limits", [P, C.c_int64, C.c_int64, C.c_int32], C.c_int),
                            ("xlua_core_interrupt", [P], C.c_int), ("xlua_core_watch", [P], C.c_int64),
                            ("xlua_core_set_memory_limit", [P, C.c_int64], C.c_int),
                            ("xlua_push_csharp_function", [P, P, C.c_int], None),
                            ("lua_pushvalue", [P, C.c_int], None)]:
        getattr(lib, name).argtypes = args
        getattr(lib, name).restype = res
    lib.xlua_core_close.argtypes = [P]
    assert lib.xlua_core_set_limits(state, 1000, 0, 0) == 0  # no core state
    assert lib.xlua_core_interrupt(state) == 0 and lib.xlua_core_watch(state) == -1
    L, status = core_newstate()
    assert L and status == 0
    supported = lib.xlua_core_set_limits(L, 0, 0, 0)
    if not supported:
        assert lib.xlua_core_interrupt(L) == 0 and lib.xlua_core_watch(L) == -1
        lib.xlua_core_close(L)
        print("core_limits: no limits on this runtime")
        return
    lib.luaopen_xlua(L)
    lib.get_error_func_ref.argtypes = [P]
    handler = lib.get_error_func_ref(L)

    def call(code, instructions=0, time_ns=0, flags=0, msgh=False):
        """Runs code as one outermost call; returns (status, message, stats)."""
        if os.environ.get("XLUA_NR_VERBOSE"):
            print("core_limits:", code[:70].replace("\n", " "), instructions, time_ns, flags, flush=True)
        assert lib.xlua_core_set_limits(L, instructions, time_ns, flags) == 1
        base = 0
        if msgh:
            lib.xlua_rawgeti(L, REGISTRY, handler)
            base = 1
        assert lib.luaL_loadstring(L, code.encode()) == 0, lib.lua_tolstring(L, -1, None)
        status = lib.lua_pcall(L, 0, 1, base)
        message = lib.lua_tolstring(L, -1, None)
        lib.lua_settop(L, 0)
        return status, (message or b"").decode(), core_stats(L)[1]

    def stopped(code, reason, **kw):
        status, message, stats = call(code, **kw)
        text = {XLUA_LIMIT_INSTR: "instruction limit exceeded", XLUA_LIMIT_TIME: "time limit exceeded",
                XLUA_INTERRUPTED: "interrupted"}[reason]
        assert status != 0 and stats.stop_reason == reason, (code, status, message, stats.stop_reason)
        return status, message, stats

    # The budget: a plain loop, a loop that swallows the error with pcall, a
    # Lua message handler, a __close handler, and work spread over coroutines.
    status, message, stats = stopped("while true do end", XLUA_LIMIT_INSTR, instructions=100000)
    assert "instruction limit exceeded" in message and stats.instructions >= 100000, (message, stats.instructions)
    stopped("while true do pcall(function() while true do end end) end", XLUA_LIMIT_INSTR, instructions=50000)
    stopped("xpcall(function() while true do end end, function(m) while true do end end)", XLUA_LIMIT_INSTR,
            instructions=50000)
    stopped("do local x <close> = setmetatable({}, {__close = function() while true do end end})"
            " while true do end end", XLUA_LIMIT_INSTR, instructions=50000)
    stopped("for i = 1, 1e9 do coroutine.wrap(function() for j = 1, 10 do end end)() end", XLUA_LIMIT_INSTR,
            instructions=200000)
    stopped("local co = coroutine.create(function() while true do end end) coroutine.resume(co)"
            " while true do end", XLUA_LIMIT_INSTR, instructions=200000)
    # The count is exact and the same with small slices and across coroutines.
    work = ("local s = 0 for i = 1, 500 do s = s + i end"
            " local co = coroutine.wrap(function() for i = 1, 300 do coroutine.yield(i) end end)"
            " for i = 1, 300 do s = s + co() end"
            " local c2 = coroutine.create(function() coroutine.yield(1) end)"
            " coroutine.resume(c2) coroutine.close(c2) return s")
    counts = set()
    for flags in (0, 0, SMALL_SLICES):
        status, message, stats = call(work, instructions=10 ** 9, flags=flags)
        assert status == 0 and stats.stop_reason == 0, message
        counts.add(stats.instructions)
    assert len(counts) == 1 and next(iter(counts)) > 1000, counts
    # A budget larger than a slice.
    status, message, stats = call(work, instructions=(1 << 31) + 7)
    assert status == 0 and stats.instructions in counts, stats.instructions
    # coroutine.close on the running, a normal and a non-coroutine value raises as upstream.
    status, message, stats = call("local ok, e = pcall(coroutine.close, coroutine.running()) assert(not ok)"
                                  " ok = pcall(coroutine.close, 1) assert(not ok)"
                                  " local outer = coroutine.wrap(function() local inner = coroutine.running()"
                                  " coroutine.wrap(function() assert(not pcall(coroutine.close, inner)) end)() end)"
                                  " outer() local ok2 = pcall(coroutine.resume, 1) assert(not ok2)",
                                  instructions=10 ** 7)
    assert status == 0, message
    # An error inside a wrapped coroutine closes it and reaches the caller.
    status, message, stats = call("local f = coroutine.wrap(function() local x <close> = setmetatable({}, "
                                  "{__close = function() closed = true end}) error('inner') end)"
                                  " local ok, e = pcall(f) assert(not ok and closed and e:find('inner'))",
                                  instructions=10 ** 7)
    assert status == 0, message
    # A coroutine suspended with a slice of an earlier call, resumed when no limit is set.
    status, message, stats = call("stale = coroutine.create(function() while true do"
                                  " for i = 1, 2000 do end coroutine.yield() end end) coroutine.resume(stale)",
                                  instructions=10 ** 7, flags=SMALL_SLICES)
    assert status == 0, message
    status, message, stats = call("for i = 1, 5 do assert(coroutine.resume(stale)) end stale = nil")
    assert status == 0 and stats.stop_reason == 0, message
    # The message handler still builds its traceback after a stop, unless it runs Lua code itself.
    status, message, stats = stopped("while true do end", XLUA_LIMIT_INSTR, instructions=50000, msgh=True)
    assert "instruction limit exceeded" in message and "stack traceback" in message, message
    status, message, stats = stopped("debug.traceback = function() while true do end end while true do end",
                                     XLUA_LIMIT_INSTR, instructions=50000, msgh=True)
    run(L, "debug.traceback = require('debug').traceback")

    # Interrupt from a C function (as C# would), and the statistics of the running call.
    def interrupt(state_):
        assert lib.xlua_core_interrupt(state_) == 1
        assert lib.xlua_core_interrupt(state_) == 0  # already stopped
        found, stats_ = core_stats(state_)
        assert stats_.stop_reason == XLUA_INTERRUPTED
        assert lib.xlua_core_watch(state_) == -1  # stopped already
        return 0

    def watch(state_):
        left = lib.xlua_core_watch(state_)
        assert left > 0, left
        lib.xlua_pushinteger(state_, 1)
        return 1

    callbacks = [CSFunction(interrupt), CSFunction(watch)]
    for name, fn in zip([b"interrupt", b"watch"], callbacks):
        lib.xlua_push_csharp_function(L, C.cast(fn, P), 0)
        lib.lua_setglobal(L, name)
    assert lib.xlua_core_interrupt(L) == 0  # nothing runs
    stopped("interrupt() while true do end", XLUA_INTERRUPTED, instructions=10 ** 9)
    stopped("interrupt() local x = 1", XLUA_INTERRUPTED, time_ns=10 ** 10)
    status, message, stats = call("assert(watch() == 1)", time_ns=10 ** 10)
    assert status == 0, message

    # The time limit: the clock check of the hook (WebGL), with and without an
    # instruction limit, and the watchdog from another thread.
    stopped("while true do end", XLUA_LIMIT_TIME, time_ns=20 * 10 ** 6, flags=CLOCK_CHECK)
    stopped("while true do end", XLUA_LIMIT_TIME, instructions=10 ** 12, time_ns=20 * 10 ** 6, flags=CLOCK_CHECK)
    stopped("while true do end", XLUA_LIMIT_TIME, instructions=10 ** 12, time_ns=20 * 10 ** 6)
    done = threading.Event()

    def watchdog():
        # No sleep: the owner thread also meets the lock taken (the spin loop).
        while not done.is_set():
            lib.xlua_core_watch(L)

    thread = threading.Thread(target=watchdog)
    thread.start()
    try:
        stopped("while true do end", XLUA_LIMIT_TIME, time_ns=20 * 10 ** 6)
        stopped("coroutine.wrap(function() while true do end end)()", XLUA_LIMIT_TIME, time_ns=20 * 10 ** 6)
        stopped("local s = ('a'):rep(1e5) s:find('.-.-.-.-b')", XLUA_LIMIT_TIME, time_ns=50 * 10 ** 6)
        for _ in range(200):  # back-to-back short calls under a tiny limit are never hit by a stale deadline
            status, message, stats = call("return 1", time_ns=50 * 10 ** 6)
            assert status == 0, message
    finally:
        done.set()
        thread.join()

    # The C budget: patterns, plain find and table.move stop within the
    # instruction limit, and results are unchanged.
    stopped("local s = ('a'):rep(1e5) s:find('.-.-.-.-b')", XLUA_LIMIT_INSTR, instructions=10 ** 6)
    stopped("local s = ('a'):rep(1e5) for _ in s:gmatch('.-.-.-.-b') do end", XLUA_LIMIT_INSTR, instructions=10 ** 6)
    stopped("local s = ('a'):rep(1e5) s:gsub('.-.-.-.-b', '')", XLUA_LIMIT_INSTR, instructions=10 ** 6)
    stopped("local s = ('a'):rep(1e6) s:find(('a'):rep(5e5) .. 'b', 1, true)", XLUA_LIMIT_INSTR, instructions=10 ** 6)
    stopped("table.move({}, 1, 1 << 62, 2)", XLUA_LIMIT_INSTR, instructions=10 ** 6)
    stopped("table.move({}, 1, 1 << 40, 2, {})", XLUA_LIMIT_INSTR, instructions=10 ** 6)
    status, message, stats = call("local s = ('ab'):rep(5000) assert(select(2, s:gsub('a(b)', '%1')) == 5000)"
                                  " assert(s:find(('ab'):rep(4000) .. 'x', 1, true) == nil)"
                                  " local t = table.move({1, 2, 3}, 1, 3, 2) assert(t[4] == 3)"
                                  " assert(#table.move({}, 1, 10000, 1, {}) == 0)", instructions=10 ** 8)
    assert status == 0, message
    # A C budget charge that the slice covers, and the stop seen by the C budget.
    # (about 5,000 match steps: one charge of 4,096 comes out of the thread's slice)
    status, message, stats = call("local s = ('a'):rep(100) assert(s:find('.-b') == nil)", instructions=6000)
    assert status == 0, message
    stopped("interrupt() local s = ('a'):rep(1e5) s:find('.-.-.-.-b')", XLUA_INTERRUPTED, time_ns=10 ** 10)

    # The profiler shares the hook: it sees calls and C# returns, and the limit still applies.
    status, message, stats = call("""
local events = 0
xlua.sethook(function(event, name) events = events + 1 end)
local function f() return 1 end
for i = 1, 10 do f() end
assert(watch() == 1)
xlua.sethook(nil)
assert(events > 10, events)""", instructions=10 ** 7, time_ns=10 ** 10)
    assert status == 0, message
    stopped("xlua.sethook(function() end) while true do end", XLUA_LIMIT_INSTR, instructions=10 ** 5)
    run(L, "xlua.sethook(nil)")
    # A coroutine armed for the profiler during a limited call keeps call events
    # after the profiler is gone; core's hook ignores them.
    status, message, stats = call("xlua.sethook(function() end) prof_co = coroutine.wrap(function()"
                                  " while true do coroutine.yield() end end) prof_co() xlua.sethook(nil)",
                                  instructions=10 ** 7)
    assert status == 0, message
    assert call("for i = 1, 3 do prof_co() end prof_co = nil")[0] == 0

    # The edges of the switches (Lua's own coroutine code): too many arguments
    # and results, errors in wrapped coroutines (a table and a memory error),
    # closing a finished coroutine.
    status, message, stats = call("""
local t = {} for i = 1, 600000 do t[i] = i end
local co = coroutine.create(function(...) coroutine.yield() end)
assert(coroutine.resume(co, table.unpack(t)))
local ok, err = coroutine.resume(co, table.unpack(t))
assert(not ok and err == 'too many arguments to resume', err)
local w = coroutine.wrap(function(...) coroutine.yield() end)
w(table.unpack(t))
assert(not pcall(w, table.unpack(t)))
local co2 = coroutine.create(function() coroutine.yield(table.unpack(t)) end)
local function g(...) return coroutine.resume(co2) end
local ok2, err2 = g(table.unpack(t))
assert(not ok2 and err2 == 'too many results to resume', err2)
local wd = coroutine.wrap(function() end) wd() assert(not pcall(wd))  -- dead: its status stays OK
local w2 = coroutine.wrap(function() error({}) end)
local ok3, e3 = pcall(w2) assert(not ok3 and type(e3) == 'table')
local done = coroutine.create(function() end) coroutine.resume(done)
assert(coroutine.close(done))
""", instructions=10 ** 9)
    assert status == 0, message
    found, stats = core_stats(L)
    assert lib.xlua_core_set_memory_limit(L, stats.memory_used + 256 * 1024) == 1
    status, message, stats = call("local w = coroutine.wrap(function() local t = {} while true do t[#t + 1] = {} end end)"
                                  " local ok, e = pcall(w) assert(not ok and e == 'not enough memory', e)"
                                  " collectgarbage()", instructions=10 ** 9)
    assert status == 0, message
    assert lib.xlua_core_set_memory_limit(L, 0) == 1

    # The C budget outside a limited call (a plain state and no limits set),
    # with a budget beyond one slice, with a foreign hook, and seeing a stop
    # that another thread set while the pattern runs.
    run(state, "assert(('a'):rep(3000):find('.-b') == nil)")
    assert call("assert(('a'):rep(3000):find('.-b') == nil)")[0] == 0
    stopped("debug.sethook(function() end, '', 1000000000) local s = ('a'):rep(1e5) s:find('.-.-.-.-b')",
            XLUA_LIMIT_INSTR, instructions=10 ** 6)
    run(L, "debug.sethook()")
    def stop_later():
        time.sleep(0.05)
        lib.xlua_core_interrupt(L)
    thread = threading.Thread(target=stop_later)
    thread.start()
    stopped("local s = ('a'):rep(1e5) s:find('.-.-.-.-b')", XLUA_INTERRUPTED, instructions=10 ** 15)
    thread.join()
    # A coroutine resumed by C code (lua_resume, not coroutine.resume) runs
    # with the hook it inherited; core's hook disarms it.
    lib.lua_newthread.argtypes = [P]
    lib.lua_newthread.restype = P
    lib.lua_resume.argtypes = [P, P, C.c_int, C.POINTER(C.c_int)]
    lib.lua_getglobal.argtypes = [P, C.c_char_p]  # also when this check runs alone

    def c_resume(state_):
        co = lib.lua_newthread(state_)
        lib.lua_getglobal(co, b"spin")
        nres = C.c_int()
        assert lib.lua_resume(co, state_, 0, C.byref(nres)) == 0
        lib.lua_settop(state_, 0)
        lib.xlua_pushinteger(state_, 1)
        return 1

    def stats_now(state_):
        found_, stats_ = core_stats(state_)
        assert found_ == 1 and stats_.stop_reason == 0
        assert lib.xlua_core_watch(state_) == -1  # no deadline
        lib.xlua_pushinteger(state_, 1)
        return 1

    more = [CSFunction(c_resume), CSFunction(stats_now)]
    for name, fn in zip([b"c_resume", b"stats_now"], more):
        lib.xlua_push_csharp_function(L, C.cast(fn, P), 0)
        lib.lua_setglobal(L, name)
    run(L, "function spin() for i = 1, 5000 do end end")
    status, message, stats = call("assert(c_resume() == 1) assert(stats_now() == 1)", instructions=10 ** 7,
                                  flags=SMALL_SLICES)
    assert status == 0, message
    assert call("assert(stats_now() == 1)")[0] == 0
    # A time limit too large for a deadline is kept in range.
    assert call("return 1", time_ns=1 << 62)[0] == 0
    found, stats = core_stats(L)
    assert stats.time_limit_ns == (1 << 63) // 4 - 1 or stats.time_limit_ns < (1 << 62), stats.time_limit_ns
    run(L, "spin, c_resume, stats_now = nil")
    del more
    assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
    run(L, "interrupt, watch = nil collectgarbage()")
    del callbacks
    lib.xlua_core_close(L)


def check_core_watchdog_race(state):
    # The stop that the watchdog arms from another thread reaches the running
    # thread (build/xlua_core_limits.c, arm_stop_remote; core-luascript.md 8.3).
    # A lost arming left "while true do end" running forever, on macOS arm64
    # about one call in 500 under this watchdog. Many calls under a 1 ms limit,
    # with a watchdog that sleeps between polls as LuaWatchdog.cs does; a call
    # that outlives its limit by far ends the run with a message instead of
    # hanging it.
    import threading
    import time
    for name, args, res in [("xlua_core_set_limits", [P, C.c_int64, C.c_int64, C.c_int32], C.c_int),
                            ("xlua_core_watch", [P], C.c_int64)]:
        getattr(lib, name).argtypes = args
        getattr(lib, name).restype = res
    lib.xlua_core_close.argtypes = [P]
    L, status = core_newstate()
    assert L and status == 0
    if not lib.xlua_core_set_limits(L, 0, 0, 0):
        lib.xlua_core_close(L)
        print("core_watchdog_race: no limits on this runtime")
        return
    lib.luaopen_xlua(L)
    assert lib.xlua_core_set_limits(L, 0, 10 ** 6, 0) == 1
    calls = int(os.environ.get("XLUA_NR_WATCHDOG_CALLS", "3000"))
    running = [0.0, 0]  # start of the running call (0: none) and its number
    done = threading.Event()

    def watchdog():
        while not done.is_set():
            lib.xlua_core_watch(L)
            begin = running[0]
            if begin and time.monotonic() - begin > 10:
                print("core_watchdog_race: call %d did not stop within 10 s: the stop was lost" % running[1],
                      flush=True)
                os._exit(3)
            time.sleep(0.00025)

    codes = [b"while true do end", b"coroutine.wrap(function() while true do end end)()",
             b"local function f() end while true do f() end"]
    thread = threading.Thread(target=watchdog)
    thread.start()
    try:
        for i in range(calls):
            assert lib.luaL_loadstring(L, codes[i % len(codes)]) == 0
            running[1] = i
            running[0] = time.monotonic()
            status = lib.lua_pcall(L, 0, 0, 0)
            running[0] = 0.0
            found, stats = core_stats(L)
            assert status != 0 and stats.stop_reason == XLUA_LIMIT_TIME, (i, status, stats.stop_reason)
            lib.lua_settop(L, 0)
    finally:
        done.set()
        thread.join()
    # Once the hook has fired, Interrupt and the watchdog leave the stop as it
    # is: a C message handler runs while the stopped call unwinds.
    lib.xlua_core_interrupt.argtypes = [P]
    lib.xlua_core_interrupt.restype = C.c_int
    lib.xlua_push_csharp_function.argtypes = [P, P, C.c_int]
    lib.xlua_push_csharp_function.restype = None
    seen = []

    def handler(state_):
        seen.append((lib.xlua_core_interrupt(state_), lib.xlua_core_watch(state_)))
        return 1

    fn = CSFunction(handler)
    assert lib.xlua_core_set_limits(L, 1000, 10 ** 10, 0) == 1
    lib.xlua_push_csharp_function(L, C.cast(fn, P), 0)
    assert lib.luaL_loadstring(L, b"while true do end") == 0
    assert lib.lua_pcall(L, 0, 0, 1) != 0
    found, stats = core_stats(L)
    assert stats.stop_reason == XLUA_LIMIT_INSTR and seen == [(0, -1)], (stats.stop_reason, seen)
    lib.lua_settop(L, 0)
    assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
    lib.xlua_core_close(L)
    del fn


def check_core_stop_latency_env(state):
    # The time limit and Interrupt stop LuaEnv's entry (lua_pcall) within 100 ms,
    # also with an instruction limit (Test/Native/stop_latency.py; before the fix
    # of 2026-10-18 they waited up to 5 s for the end of a count slice). LuaEnv's
    # Interrupt needs a limit, so there is no case without one.
    import stop_latency
    lib.xlua_core_close.argtypes = [P]
    lib.xlua_core_set_limits.argtypes = [P, C.c_int64, C.c_int64, C.c_int32]
    lib.xlua_core_set_limits.restype = C.c_int
    L, status = core_newstate()
    assert L and status == 0
    if not lib.xlua_core_set_limits(L, 0, 0, 0):
        lib.xlua_core_close(L)
        print("core_stop_latency_env: no limits on this runtime")
        return
    lib.luaopen_xlua(L)
    lib.lua_settop(L, 0)

    def call(code):
        assert lib.luaL_loadstring(L, code) == 0
        status = lib.lua_pcall(L, 0, 0, 0)
        lib.lua_settop(L, 0)
        return core_stats(L)[1].stop_reason if status != 0 else 0

    try:
        n, worst = stop_latency.check(lib, L, call, None)
    finally:
        lib.xlua_core_close(L)
    print("core_stop_latency_env: %d stops, the latest %.1f ms after its deadline (%s)" % (n, worst[3] * 1000, worst[:3]))


state = lib.luaL_newstate()
assert state
try:
    lib.luaopen_xlua(state)
    lib.luaopen_i64lib(state)
    # A bytecode-only library (XLUA_BYTECODE_ONLY) cannot run the checks below,
    # which compile text: Test/Native/bytecode_only.py has its own.
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import bytecode_only
    text_refused = bytecode_only.is_bytecode_only(lib, state)
    expect_bytecode_only = os.environ.get("XLUA_EXPECT_BYTECODE_ONLY", "").upper()
    assert expect_bytecode_only != "ON" or text_refused, "XLUA_EXPECT_BYTECODE_ONLY=ON, but the library compiles text"
    assert expect_bytecode_only != "OFF" or not text_refused, "XLUA_EXPECT_BYTECODE_ONLY=OFF, but the library refuses text"
    if text_refused:
        sys.exit(bytecode_only.main(lib, state))
    # The checks tell LuaJIT from Lua 5.x by the global jit, as scripts usually do.
    assert not hasattr(lib, "luaJIT_setmode") or is_luajit(state), \
        "a LuaJIT library without the jit library (XLUA_LIBS): these checks need the global jit"
    if not uint64_built():
        run(state, UINT64_STAND_IN)
    for name in sys.argv[2:] or ["uint64_table", "std_libraries", "library_parts", "parse", "struct_offsets", "struct_alignment", "exported_helpers", "value_helpers",
                                    "struct_accessors", "struct_clone", "indexers", "path_access", "csharp_functions",
                                    "setfenv", "uint_conversion", "integer_conversion", "argerror", "native_modules", "luajit_int64", "luajit_int64_helpers", "luajit_int64_header",
                                    "checked_accessors", "checked_cache", "core_state", "core_host_oom",
                                    "memory_snapshot_tables", "memory_snapshot_userdata", "memory_snapshot_functions",
                                    "memory_snapshot_limits", "memory_snapshot_reentrancy", "memory_snapshot_out_of_memory",
                                    "memory_snapshot_core_limit", "core_limits", "core_watchdog_race", "core_stop_latency_env"] + (["core_sandbox"] if hasattr(lib, "xlua_core_open") else []):
        if name.startswith("core_sandbox") or name == "core_lua_api":  # XLUA_LUASCRIPT=ON on the full library
            assert core_checks([name] if name != "core_sandbox" else []) == 0
            continue
        globals()["check_" + name](state)
    print("XLUA_NATIVE_REGRESSION_PASS")
finally:
    lib.lua_close(state)
