"""Regression checks for defects found by analyzing and fuzzing xLua's C code,
the C exports that no C# code calls, and the argument and error branches of
the exports the managed bridge calls (struct accessors, indexers, path access,
C# function wrappers).

Usage: python3 Test/Native/native_regression.py /absolute/path/libxlua.so [check ...]
Checks: parse, struct_offsets, struct_alignment, exported_helpers, value_helpers,
struct_accessors, struct_clone, indexers, path_access, csharp_functions, setfenv, uint_conversion,
integer_conversion, argerror, luajit_int64, luajit_int64_helpers, luajit_int64_header (default: all). Works with Lua 5.3, 5.4, 5.5 and LuaJIT
plugins (LuaJIT also checks int64).
"""
import ctypes as C
from pathlib import Path
import sys

lib = C.CDLL(str(Path(sys.argv[1]).resolve()))
P = C.c_void_p
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


def check_parse(state):
    # uint64.parse handed lua_tostring's NULL to strtoull for non-strings.
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


state = lib.luaL_newstate()
assert state
try:
    lib.luaopen_xlua(state)
    lib.luaopen_i64lib(state)
    for name in sys.argv[2:] or ["parse", "struct_offsets", "struct_alignment", "exported_helpers", "value_helpers",
                                    "struct_accessors", "struct_clone", "indexers", "path_access", "csharp_functions",
                                    "setfenv", "uint_conversion", "integer_conversion", "argerror", "luajit_int64", "luajit_int64_helpers", "luajit_int64_header"]:
        globals()["check_" + name](state)
    print("XLUA_NATIVE_REGRESSION_PASS")
finally:
    lib.lua_close(state)
