"""Regression checks for defects found by analyzing and fuzzing xLua's C code.

Usage: python3 Test/Native/native_regression.py /absolute/path/libxlua.so [check ...]
Checks: parse (default: all). Works with Lua 5.3, 5.4, 5.5 and LuaJIT plugins.
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
lib.lua_close.argtypes = [P]


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


state = lib.luaL_newstate()
assert state
try:
    lib.luaopen_xlua(state)
    lib.luaopen_i64lib(state)
    for name in sys.argv[2:] or ["parse"]:
        globals()["check_" + name](state)
    print("XLUA_NATIVE_REGRESSION_PASS")
finally:
    lib.lua_close(state)
