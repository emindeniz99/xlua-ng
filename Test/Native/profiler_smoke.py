"""Check native profiler graph traversal against known Lua object identities."""
import ctypes as C
from pathlib import Path
import sys

lib = C.CDLL(str(Path(sys.argv[1]).resolve()))
P = C.c_void_p
lib.luaL_newstate.restype = P
lib.luaopen_xlua.argtypes = [P]
lib.luaL_loadstring.argtypes = [P, C.c_char_p]
lib.lua_pcall.argtypes = [P, C.c_int, C.c_int, C.c_int]
lib.lua_getglobal.argtypes = [P, C.c_char_p]
lib.lua_topointer.argtypes = [P, C.c_int]
lib.lua_topointer.restype = P
lib.lua_settop.argtypes = [P, C.c_int]
lib.lua_close.argtypes = [P]
for name in ["xlua_registry_pointer", "xlua_global_pointer"]:
    fn = getattr(lib, name)
    fn.argtypes = [P]
    fn.restype = P
size_cb = C.CFUNCTYPE(None, P, C.c_int)
graph_cb = C.CFUNCTYPE(None, P, P, C.c_int, C.c_char_p, C.c_double, C.c_char_p)
lib.xlua_report_table_size.argtypes = [P, size_cb, C.c_int]
lib.xlua_report_object_relationship.argtypes = [P, graph_cb]
state = lib.luaL_newstate()
assert state
try:
    lib.luaopen_xlua(state)
    code = b"""
child, sparse, named, key, value, meta = {}, {}, {}, {}, {}, {}
root = {child, [5]=sparse, named=named, [key]=value}
setmetatable(root, meta)
local captured = child
closure = function() return captured end
"""
    assert lib.luaL_loadstring(state, code) == 0
    assert lib.lua_pcall(state, 0, 0, 0) == 0
    def pointer(name):
        lib.lua_getglobal(state, name.encode())
        result = lib.lua_topointer(state, -1)
        lib.lua_settop(state, -2)
        return result
    objects = {name: pointer(name) for name in
               ["root", "child", "sparse", "named", "key", "value", "meta", "closure", "_G"]}
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
    print("XLUA_PROFILER_SMOKE_PASS")
finally:
    lib.lua_close(state)
