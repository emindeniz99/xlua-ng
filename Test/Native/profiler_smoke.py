"""Check native profiler graph traversal against known Lua object identities.

The leak checker these functions belong to is the CMake option XLUA_LEAK_CHECKER
(Lua 5.3 to 5.5). A library built without it passes with a note, unless
XLUA_EXPECT_LEAK_CHECKER=ON; XLUA_EXPECT_LEAK_CHECKER=OFF fails a library that has it.
"""
import ctypes as C
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from managed_imports import OPTION_EXPORTS, expected_options  # noqa: E402

lib = C.CDLL(str(Path(sys.argv[1]).resolve()))
if hasattr(lib, "luaJIT_setmode"):
    sys.exit("profiler_smoke.py checks PUC Lua libraries; LuaJIT has no leak checker and other GC options")
built =[hasattr(lib, name) for name in OPTION_EXPORTS["XLUA_LEAK_CHECKER"]]
assert all(built) or not any(built), "only part of the leak checker is exported"
wanted = expected_options()["XLUA_LEAK_CHECKER"]
assert wanted is not True or all(built), "XLUA_EXPECT_LEAK_CHECKER=ON, but the library has no leak checker"
assert wanted is not False or not any(built), "XLUA_EXPECT_LEAK_CHECKER=OFF, but the library has the leak checker"
leak_checker = all(built)
P = C.c_void_p
lib.luaL_newstate.restype = P
lib.luaL_openlibs.argtypes = [P]
lib.luaL_loadstring.argtypes = [P, C.c_char_p]
lib.lua_pcall.argtypes = [P, C.c_int, C.c_int, C.c_int]
lib.lua_getglobal.argtypes = [P, C.c_char_p]
lib.lua_topointer.argtypes = [P, C.c_int]
lib.lua_topointer.restype = P
lib.lua_settop.argtypes = [P, C.c_int]
lib.lua_close.argtypes = [P]
lib.xlua_gc.argtypes = [P, C.c_int, C.c_int]
size_cb = C.CFUNCTYPE(None, P, C.c_int)
graph_cb = C.CFUNCTYPE(None, P, P, C.c_int, C.c_char_p, C.c_double, C.c_char_p)
if leak_checker:
    for name in ["xlua_registry_pointer", "xlua_global_pointer"]:
        fn = getattr(lib, name)
        fn.argtypes = [P]
        fn.restype = P
    lib.xlua_report_table_size.argtypes = [P, size_cb, C.c_int]
    lib.xlua_report_object_relationship.argtypes = [P, graph_cb]
state = lib.luaL_newstate()
assert state
try:
    lib.luaL_openlibs(state)
    assert lib.xlua_gc(state, 0, 0) == 0
    assert lib.xlua_gc(state, 9, 0) == 0
    lib.xlua_gc(state, 5, 1)
    assert lib.xlua_gc(state, 9, 0) == 0
    assert lib.xlua_gc(state, 1, 0) == 0
    assert lib.xlua_gc(state, 9, 0) == 1
    assert lib.xlua_gc(state, 8, 0) == -1
    if not leak_checker:
        print("XLUA_PROFILER_SMOKE_PASS: xlua_gc only, built without the leak checker (XLUA_LEAK_CHECKER=OFF)")
        sys.exit(0)
    code = b"""
child, sparse, named, key, value, meta = {}, {}, {}, {}, {}, {}
root = {child, [5]=sparse, named=named, [key]=value}
setmetatable(root, meta)
local captured = child
closure = function() return captured end
-- Keys that are long strings (not interned) and floats.
long_key, float_key = {}, {}
root[string.rep("k", 50)] = long_key
root[1.5] = float_key
local number, text = 5, "text"
plain_closure = function() return number, text end
"""
    assert lib.luaL_loadstring(state, code) == 0
    assert lib.lua_pcall(state, 0, 0, 0) == 0
    def pointer(name):
        lib.lua_getglobal(state, name.encode())
        result = lib.lua_topointer(state, -1)
        lib.lua_settop(state, -2)
        return result
    objects = {name: pointer(name) for name in
               ["root", "child", "sparse", "named", "key", "value", "meta", "closure", "plain_closure",
                "long_key", "float_key", "_G"]}
    assert lib.xlua_registry_pointer(state)
    assert lib.xlua_global_pointer(state) == objects["_G"]
    for fast in [0, 1]:
        sizes = {}
        callback = size_cb(lambda obj, size: sizes.__setitem__(obj, size))
        lib.xlua_report_table_size(state, callback, fast)
        assert sizes[objects["root"]] >= 4, sizes
    edges = []
    callback = graph_cb(lambda parent, child, kind, key, number, upvalue:
                        edges.append((parent, child, kind, key, number, upvalue)))
    lib.xlua_report_object_relationship(state, callback)
    expected = [("root", "child", 2), ("root", "sparse", 2),
                ("root", "named", 1), ("root", "key", 3),
                ("root", "meta", 4), ("closure", "child", 5)]
    for parent, child, kind in expected:
        assert any(edge[:3] == (objects[parent], objects[child], kind)
                   for edge in edges), (parent, child, kind, edges)
    assert any(edge[:4] == (objects["root"], objects["named"], 1, b"named")
               for edge in edges)
    assert any(edge[:4] == (objects["root"], objects["long_key"], 1, b"k" * 50) for edge in edges)
    assert any(edge[:3] == (objects["root"], objects["float_key"], 2) and edge[4] == 1.5 for edge in edges)
    # Only table upvalues are references; numbers and strings are not reported.
    assert not any(edge[0] == objects["plain_closure"] for edge in edges), edges
    print("XLUA_PROFILER_SMOKE_PASS")
finally:
    lib.lua_close(state)
