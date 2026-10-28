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

    # Tencent/xLua#1149 (https://github.com/Tencent/xLua/issues/1149; owner decision
    # of 2026-10-28, crash-inventory.md item 11): the walks run over G(L)->allgc,
    # which in the collector's sweep phase still holds dead objects that point to
    # objects swept and freed already (the keys of a dead table, the upvalue boxes
    # of a dead closure on Lua 5.4 and 5.5). Each round makes garbage whose parts
    # are newer than their owners (swept first), then walks after every small
    # collection step; the callbacks read every key and source name. Before the
    # fix AddressSanitizer reported the reads of freed memory (sanitize.sh); the
    # names must also be what the script made. The walks also stop the collector
    # and restore its state.
    bad = []

    def read_edge(parent, child, kind, key, number, upvalue):
        # ctypes reads each string here; key is the source name for kind 5.
        for text in (key, upvalue):
            if text is not None and (not text or any(b < 32 or b > 126 for b in text)):
                bad.append(text)

    read_callback = graph_cb(read_edge)
    size_callback = size_cb(lambda obj, size: None)
    # Small collection steps, so that the sweep phase spans many of them:
    # one single step per call on Lua 5.4 (step size 2^1 bytes), about 340
    # objects on 5.3 (the smallest multiplier), 200 on 5.5 (step size 800 bytes;
    # a size that makes no work at all runs a whole cycle in one step).
    garbage_code = b"""
return function(round)
    collectgarbage()
    collectgarbage("stop")  -- only the steps below collect
    if _VERSION == "Lua 5.4" then
        collectgarbage("incremental", 0, 0, 1)
    elseif _VERSION == "Lua 5.5" then
        collectgarbage("incremental")
        collectgarbage("param", "stepsize", 800)
    else
        collectgarbage("setstepmul", 40)
    end
    for i = 1, 1500 do
        local t = {}
        t["dead_key_" .. round .. "_" .. i] = {}  -- key and value newer than t
        local up = {}
        local f = function() return up end  -- its upvalue box is newer than f
    end
end
"""
    lib.lua_setglobal.argtypes = [P, C.c_char_p]
    assert lib.luaL_loadstring(state, garbage_code) == 0 and lib.lua_pcall(state, 0, 1, 0) == 0
    lib.lua_setglobal(state, b"make_garbage")
    lib.lua_toboolean.argtypes = [P, C.c_int]
    walks = 0
    for round_ in range(4):
        assert lib.luaL_loadstring(state, b"make_garbage(%d)" % round_) == 0
        assert lib.lua_pcall(state, 0, 0, 0) == 0
        for _ in range(5000):
            assert lib.luaL_loadstring(state, b"return collectgarbage('step', 0)") == 0
            assert lib.lua_pcall(state, 0, 1, 0) == 0
            cycle_done = lib.lua_toboolean(state, -1)
            lib.lua_settop(state, 0)
            lib.xlua_report_object_relationship(state, read_callback)
            lib.xlua_report_table_size(state, size_callback, walks % 2)
            walks += 1
            assert lib.xlua_gc(state, 9, 0) == 0  # LUA_GCISRUNNING: still stopped
            if cycle_done:
                break
    assert not bad, bad[:10]
    assert walks > 8, walks
    # A running collector is stopped during a walk and runs again after it.
    lib.xlua_gc(state, 1, 0)  # LUA_GCRESTART
    lib.xlua_report_table_size(state, size_callback, 1)
    lib.xlua_report_object_relationship(state, read_callback)
    assert lib.xlua_gc(state, 9, 0) == 1
    # A stopped collector stays stopped.
    lib.xlua_gc(state, 0, 0)  # LUA_GCSTOP
    lib.xlua_report_table_size(state, size_callback, 1)
    lib.xlua_report_object_relationship(state, read_callback)
    assert lib.xlua_gc(state, 9, 0) == 0
    lib.xlua_gc(state, 1, 0)  # LUA_GCRESTART

    # A walk from a C function whose free slots are used up (a C# function called
    # from Lua): it pushed each closure without lua_checkstack, past the stack
    # (Lua's API checks, sanitize.sh, stopped it); it grows the stack now.
    full_edges = []

    def full_stack(L):
        for _ in range(20):  # LUA_MINSTACK
            lib.lua_pushnil(L)
        lib.xlua_report_object_relationship(L, graph_cb(
            lambda parent, child, kind, key, number, upvalue: full_edges.append((parent, child, kind))))
        return 0

    full_stack_fn = C.CFUNCTYPE(C.c_int, P)(full_stack)
    lib.lua_pushnil.argtypes = [P]
    lib.lua_pushcclosure.argtypes = [P, P, C.c_int]
    lib.lua_pushcclosure(state, C.cast(full_stack_fn, P), 0)
    assert lib.lua_pcall(state, 0, 0, 0) == 0
    assert (objects["closure"], objects["child"], 5) in full_edges, "no closure edge with a full stack"
    print("XLUA_PROFILER_SMOKE_PASS")
finally:
    lib.lua_close(state)
