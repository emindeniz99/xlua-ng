"""Regression checks for defects found by analyzing and fuzzing xLua's C code.

Usage: python3 Test/Native/native_regression.py /absolute/path/libxlua.so [check ...]
Checks: parse, struct_offsets, struct_alignment, luajit_int64 (default: all). Works with Lua 5.3, 5.4, 5.5 and LuaJIT plugins (LuaJIT also checks int64).
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


def run(state, code):
    if lib.luaL_loadstring(state, code.encode()) != 0 or lib.lua_pcall(state, 0, 0, 0) != 0:
        raise AssertionError(lib.lua_tolstring(state, -1, None).decode())


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


def check_luajit_int64(state):
    # Int64 metamethods overflowed signed arithmetic (INT64_MIN / -1 raises
    # SIGFPE on x86) and cast out-of-range doubles to integers.
    run(state, """
if not jit then return end
local min = uint64.parse("9223372036854775808")
local function s(v) return tostring(v) end
assert(s(min) == "-9223372036854775808", s(min))
assert(s(min / -1) == "-9223372036854775808")
assert(s(min % -1) == "0")
assert(s(-min) == "-9223372036854775808")
assert(s(min - 1) == "9223372036854775807")
assert(s(min * -1) == "-9223372036854775808")
assert(s(min + 1e300) == "-1")
assert(s(min + (0/0)) == "-9223372036854775808")
assert(s(uint64.parse("10") ^ 100) == "9223372036854775807")
assert(s(uint64.parse("7") / 2) == "3")
""")


state = lib.luaL_newstate()
assert state
try:
    lib.luaopen_xlua(state)
    lib.luaopen_i64lib(state)
    for name in sys.argv[2:] or ["parse", "struct_offsets", "struct_alignment", "luajit_int64"]:
        globals()["check_" + name](state)
    print("XLUA_NATIVE_REGRESSION_PASS")
finally:
    lib.lua_close(state)
