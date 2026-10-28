"""The checks of native_regression.py for a library built with CMake
XLUA_BYTECODE_ONLY=ON (build/README.md, "Bytecode options"): Lua's compiler is
left out, so a text chunk fails with "text chunks are not supported in this
build" and only precompiled chunks load.

The chunks are compiled by another library of the same Lua version and bytecode
format (any build with the compiler, for example the default one), named by
XLUA_COMPILER_LIB; Lua 5.3 needs the same LUAC_COMPATIBLE_FORMAT setting.

Checks:
- text chunks fail through luaL_loadbufferx, luaL_loadstring, Lua's load (also
  from a reader function) and require's file searcher, with the chunk name in
  the message; a text chunk with mode "b" keeps Lua's own message;
- precompiled chunks load with modes nil, "b" and "bt" and run, and mode "t"
  refuses them as Lua does;
- string.dump and load of its result work inside the library;
- lua_setfenv (LuaEnv.LoadString/DoString with an env, LuaFunction.SetEnv)
  without the empty text chunk it uses elsewhere: the function's own _ENV, a
  closure that shares _ENV with a sibling, a stripped function, functions without
  _ENV and C functions, and out of memory at each allocation of the copy;
- require of a precompiled file from package.path;
- LuaEnv's built-in chunks (build/xlua_chunkc.c): luaopen_xlua's loader in the
  registry gives every chunk of the <xlua-chunk> markers of Assets/XLua/Src, found
  and hashed here independently of xlua_chunkc, with its chunk name as source, and
  refuses another hash and an unknown name, and out of memory while it loads a
  chunk. XLUA_EXPECT_BYTECODE_MODULES
  ("util,profiler,memory", the CMake options XLUA_BYTECODE_UTIL ...) says which
  optional ones the library has; unset, either is fine.
"""
import ctypes as C
import os
from pathlib import Path
import re
import tempfile

REPO = Path(__file__).resolve().parents[2]

P = C.c_void_p
OK, ERRSYNTAX = 0, 3
WRITER = C.CFUNCTYPE(C.c_int, P, P, C.c_size_t, P)
MESSAGE = b"text chunks are not supported in this build"


class Api:
    """A library's functions with this module's own ctypes signatures: lib[name] is a
    new function object, so the bindings native_regression.py set on the same
    library stay as they are."""

    def __init__(self, lib):
        self._lib = lib

    def __getattr__(self, name):
        fn = self._lib[name]
        setattr(self, name, fn)
        return fn


def bind(lib):
    api = Api(lib)
    for name, args, res in [("luaL_newstate", [], P), ("lua_close", [P], None),
                            ("luaL_loadbufferx", [P, C.c_char_p, C.c_size_t, C.c_char_p, C.c_char_p], C.c_int),
                            ("luaL_loadstring", [P, C.c_char_p], C.c_int),
                            ("lua_dump", [P, P, P, C.c_int], C.c_int),
                            ("lua_tolstring", [P, C.c_int, C.POINTER(C.c_size_t)], P),
                            ("lua_settop", [P, C.c_int], None), ("lua_gettop", [P], C.c_int),
                            ("lua_setglobal", [P, C.c_char_p], None),
                            # The library keeps a lua_pcall for C#, a macro over lua_pcallk since 5.4.
                            ("lua_pcall", [P, C.c_int, C.c_int, C.c_int], C.c_int)]:
        fn = getattr(api, name)
        fn.argtypes = args
        fn.restype = res
    return api


def is_bytecode_only(lib, state):
    """Whether the library refuses text chunks with the stub's message. Leaves the stack as it was."""
    lib = bind(lib)
    top = lib.lua_gettop(state)
    status = lib.luaL_loadstring(state, b"return 1")
    refused = status == ERRSYNTAX and MESSAGE in tostring(lib, state, -1)
    lib.lua_settop(state, top)
    return refused


def tostring(lib, state, idx):
    n = C.c_size_t()
    p = lib.lua_tolstring(state, idx, C.byref(n))
    return C.string_at(p, n.value) if p else b""


class Compiler:
    """Compiles text with the library of XLUA_COMPILER_LIB into bytecode."""

    def __init__(self, path):
        self.lib = bind(C.CDLL(str(Path(path).resolve())))
        self.L = self.lib.luaL_newstate()
        assert self.L

    def __call__(self, code, name=None, strip=False):
        code = code.encode() if isinstance(code, str) else code
        name = name or b"=" + code[:20]
        assert self.lib.luaL_loadbufferx(self.L, code, len(code), name, b"t") == OK, tostring(self.lib, self.L, -1)
        out = []

        def write(_L, p, size, _ud):
            out.append(C.string_at(p, size) if p else b"")
            return 0

        writer = WRITER(write)
        assert self.lib.lua_dump(self.L, C.cast(writer, P), None, int(strip)) == 0
        self.lib.lua_settop(self.L, 0)
        return b"".join(out)

    def close(self):
        self.lib.lua_close(self.L)


def load(lib, state, chunk, name=b"=chunk", mode=None):
    return lib.luaL_loadbufferx(state, chunk, len(chunk), name, mode)


def run(lib, state, chunk, nresults=0):
    """Loads and runs a binary chunk; leaves its results on the stack."""
    assert load(lib, state, chunk) == OK, tostring(lib, state, -1)
    assert lib.lua_pcall(state, 0, nresults, 0) == OK, tostring(lib, state, -1)


def value(lib, state, chunk):
    top = lib.lua_gettop(state)
    run(lib, state, chunk, 1)
    result = tostring(lib, state, -1)
    lib.lua_settop(state, top)
    return result


def check_text_refused(lib, state, compile_):
    for code, name, where in [(b"return 1", None, b'[string "return 1"]: '), (b"return 2", b"@scripts/main.lua", b"scripts/main.lua: "),
                              (b"x = = 1", b"=stdin", b"stdin: ")]:
        if name is None:
            status = lib.luaL_loadstring(state, code)
        else:
            status = load(lib, state, code, name)
        message = tostring(lib, state, -1)
        lib.lua_settop(state, 0)
        assert status == ERRSYNTAX and message.startswith(where) and MESSAGE in message, (status, message)
    # The mode check runs first, as before.
    assert load(lib, state, b"return 1", b"=t", b"b") == ERRSYNTAX
    assert b"attempt to load a text chunk (mode is 'b')" in tostring(lib, state, -1)
    lib.lua_settop(state, 0)
    # From Lua: load of a string and of a reader function, and loadstring's successors.
    out = value(lib, state, compile_("""
local f, err = load("return 1")
assert(f == nil and err:find("text chunks are not supported in this build", 1, true), err)
local sent = false
local g, err2 = load(function() if sent then return nil end sent = true return "return 2" end)
assert(g == nil and err2:find("text chunks are not supported", 1, true), err2)
local ok, err3 = pcall(require, "no_such_module_anywhere")
assert(not ok)
return "refused"
"""))
    assert out == b"refused", out


def check_bytecode_loads(lib, state, compile_):
    chunk = compile_("return 40 + 2")
    for mode in (None, b"b", b"bt"):
        assert load(lib, state, chunk, b"=bin", mode) == OK, (mode, tostring(lib, state, -1))
        assert lib.lua_pcall(state, 0, 1, 0) == OK
        assert tostring(lib, state, -1) == b"42"
        lib.lua_settop(state, 0)
    assert load(lib, state, chunk, b"=bin", b"t") == ERRSYNTAX
    assert b"attempt to load a binary chunk (mode is 't')" in tostring(lib, state, -1)
    lib.lua_settop(state, 0)
    # Errors keep their positions, and stripped chunks load too.
    assert load(lib, state, compile_("error('boom')", b"@game/boom.lua")) == OK
    assert lib.lua_pcall(state, 0, 0, 0) != OK and tostring(lib, state, -1).startswith(b"game/boom.lua:1: boom")
    lib.lua_settop(state, 0)
    assert value(lib, state, compile_("return 'stripped'", strip=True)) == b"stripped"
    # string.dump and load of a binary string work inside the library.
    assert value(lib, state, compile_("""
local function twice(a) return a * 2 end
local f = load(string.dump(twice))
local g = load(string.dump(twice, true), "=dumped", "b")
return f(21) + g(0)""")) == b"42"


def check_setfenv(lib, state, compile_):
    if not hasattr(lib, "lua_setfenv"):
        return
    lib.lua_setfenv.argtypes = [P, C.c_int]
    lib.lua_setfenv.restype = C.c_int

    def setfenv(chunk, loaded=False):
        """lua_setfenv with {y = 'env'} on the function the chunk returns (loaded: on the
        chunk itself); the function becomes the global f."""
        lib.lua_settop(state, 0)
        if loaded:
            assert load(lib, state, chunk) == OK, tostring(lib, state, -1)
        else:
            run(lib, state, chunk, 1)
        run(lib, state, compile_("return {y = 'env'}"), 1)
        result = lib.lua_setfenv(state, -2)
        assert lib.lua_gettop(state) == 1, lib.lua_gettop(state)  # the table is popped
        lib.lua_setglobal(state, b"f")
        return result

    def check(code):
        run(lib, state, compile_(code))

    check("y = 'global'")
    # A loaded chunk, as LuaEnv.LoadString passes it, with and without debug
    # information (a main chunk's only upvalue is _ENV).
    assert setfenv(compile_("return y"), loaded=True) == 1
    check("assert(f() == 'env')")
    assert setfenv(compile_("return y", strip=True), loaded=True) == 1
    check("assert(f() == 'env')")
    # _ENV is the first upvalue, shared with a sibling closure, which keeps its globals.
    assert setfenv(compile_("""
local function get() return y end
sibling = function() return y end
return get""")) == 1
    check("assert(f() == 'env') assert(sibling() == 'global', tostring(sibling()))")
    # _ENV is the second upvalue, and the first keeps its value.
    assert setfenv(compile_("""
local x = "up"
local function get() return x, y end
sibling = function() return y end
return get""")) == 1
    check("""
local a, b = f()
assert(a == "up" and b == "env", tostring(a) .. " " .. tostring(b))
assert(sibling() == "global" and y == "global")
""")
    # Nothing to set: no _ENV, C functions, values that are not functions.
    assert setfenv(compile_("local x = 'up' return function() return x end")) == 0
    check("assert(f() == 'up')")
    assert setfenv(compile_("return print")) == 0
    assert setfenv(compile_("return 1")) == 0
    check("f, sibling = nil, nil")
    lib.lua_settop(state, 0)
    # Out of memory while the function is copied, at each allocation in turn (the
    # dump's buffer, its result, loading the copy) until the copy succeeds: nothing
    # is set and the table is popped.
    with failing_alloc(lib, state) as fail_after:
        for allowed in range(1000):
            lib.lua_settop(state, 0)
            assert load(lib, state, compile_("return y")) == OK
            run(lib, state, compile_("return {y = 'env'}"), 1)
            fail_after(allowed)
            result = lib.lua_setfenv(state, -2)
            fail_after(None)
            assert lib.lua_gettop(state) == 1, (allowed, lib.lua_gettop(state))
            assert lib.lua_pcall(state, 0, 1, 0) == OK
            got = tostring(lib, state, -1)
            if result == 1:
                assert got == b"env", (allowed, got)
                break
            assert result == 0 and got == b"global", (allowed, result, got)
        else:
            raise AssertionError("lua_setfenv failed with 1000 allocations allowed")
    lib.lua_settop(state, 0)


class failing_alloc:
    """Wraps the state's allocator; fail_after(n) makes every growth after the
    next n fail, fail_after(None) stops failing."""

    def __init__(self, lib, state):
        self.lib, self.state = lib, state

    def __enter__(self):
        lib, state = self.lib, self.state
        Alloc = C.CFUNCTYPE(P, P, P, C.c_size_t, C.c_size_t)
        lib.lua_getallocf.argtypes = [P, C.POINTER(P)]
        lib.lua_getallocf.restype = P
        lib.lua_setallocf.argtypes = [P, P, P]
        lib.lua_setallocf.restype = None
        self.original_ud = P()
        self.original_ptr = lib.lua_getallocf(state, C.byref(self.original_ud))
        original = Alloc(self.original_ptr)
        left = [None]

        def alloc(ud, ptr, osize, nsize):
            if left[0] is not None and nsize > 0 and (ptr is None or nsize > osize):
                if left[0] == 0:
                    return None
                left[0] -= 1
            return original(self.original_ud, ptr, osize, nsize)

        def fail_after(n):
            left[0] = n

        self.hook = Alloc(alloc)
        lib.lua_setallocf(state, C.cast(self.hook, P), None)
        return fail_after

    def __exit__(self, *exc):
        self.lib.lua_setallocf(self.state, self.original_ptr, self.original_ud)
        return False


def check_require(lib, state, compile_):
    with tempfile.TemporaryDirectory() as folder:
        Path(folder, "precompiled.lua").write_bytes(compile_("return {name = ...}", b"@precompiled.lua"))
        Path(folder, "plain.lua").write_text("return 1\n")
        pattern = os.path.join(folder, "?.lua")
        out = value(lib, state, compile_("""
package.path = ...
""".replace("...", repr(pattern)) + """
local m = require("precompiled")
assert(m.name == "precompiled", tostring(m.name))
local ok, err = pcall(require, "plain")
assert(not ok and err:find("text chunks are not supported in this build", 1, true), err)
return "required"
"""))
    assert out == b"required", out


CHUNK_MARKER = re.compile(r'//\s*<xlua-chunk ([^>\n]*)>.*?@"((?:[^"]|"")*)"\s*;\s*//\s*</xlua-chunk>', re.S)


def builtin_chunks(root=REPO / "Assets/XLua/Src"):
    """The built-in chunks of the C# runtime: (name, chunkname, option or None, hash), with
    the hash of LuaEnv.BuiltinChunkHash (FNV-1a of the text without carriage returns)."""
    out = []
    for path in sorted(root.rglob("*.cs")):
        for attributes, literal in CHUNK_MARKER.findall(path.read_bytes().decode("latin-1")):
            fields = dict(re.findall(r'(\w+)="([^"]*)"', attributes))
            text = literal.replace('""', '"').encode("latin-1")
            h = 2166136261
            for byte in text:
                if byte != 13:
                    h = ((h ^ byte) * 16777619) & 0xFFFFFFFF
            out.append((fields["name"], fields["chunkname"], fields.get("option"), h))
    assert len({c[0] for c in out}) == len(out), "two <xlua-chunk> markers with the same name"
    return out


MODULE_OPTIONS = {"util": "XLUA_BYTECODE_UTIL", "profiler": "XLUA_BYTECODE_PROFILER", "memory": "XLUA_BYTECODE_MEMORY"}


def check_builtin_chunks(lib, state, compile_):
    chunks = builtin_chunks()
    names = {c[0]: c for c in chunks}
    assert set(names) >= {"init_xlua", "remove_libraries", "enumerable_pairs", "xlua.util"}, chunks
    modules = os.environ.get("XLUA_EXPECT_BYTECODE_MODULES")
    wanted = None if modules is None else {MODULE_OPTIONS[m.strip()] for m in modules.split(",") if m.strip()}
    rows = []
    for name, chunkname, option, h in chunks:
        # true: the library must have it, false: must not, nil: either.
        need = "true" if option is None else ("nil" if wanted is None else ("true" if option in wanted else "false"))
        rows.append('{"%s", "%s", %d, %s}' % (name, chunkname, h, need))
    code = """
local loader = debug.getregistry()["xlua.builtin_chunk"]
assert(type(loader) == "function", "no xlua.builtin_chunk loader in the registry")
local found = {}
for _, c in ipairs({ROWS}) do
    local name, chunkname, hash, need = c[1], c[2], c[3], c[4]
    local f, err = loader(name, hash)
    if f then
        assert(need ~= false, name .. ": the library has it, but XLUA_EXPECT_BYTECODE_MODULES leaves it out")
        assert(type(f) == "function", name)
        local source = debug.getinfo(f, "S").source
        assert(source == chunkname, name .. ": source " .. tostring(source) .. ", expected " .. chunkname)
        found[#found + 1] = name
        local g, other = loader(name, (hash + 1) % 4294967296)
        assert(g == nil and other:find("built from other C# sources", 1, true), tostring(other))
    else
        assert(need ~= true, name .. ": " .. tostring(err))
        assert(err:find("has no precompiled chunk '" .. name .. "'", 1, true), err)
    end
end
local f, err = loader("no.such.chunk", 0)
assert(f == nil and err:find("has no precompiled chunk 'no.such.chunk'", 1, true), tostring(err))
-- The setup chunks give a function each, which LuaEnv calls.
assert(type(loader("enumerable_pairs", PAIRS)()) == "function")
assert(type(loader("remove_libraries", REMOVE)()) == "function")
return table.concat(found, " ")
""".replace("ROWS", ", ".join(rows)).replace("PAIRS", str(names["enumerable_pairs"][3])) \
        .replace("REMOVE", str(names["remove_libraries"][3]))
    print("built-in chunks: " + value(lib, state, compile_(code)).decode())
    # Out of memory while the loader loads a chunk, at each allocation in turn: nil
    # and Lua's message, until the chunk loads.
    lib.lua_settop(state, 0)
    get = compile_('return debug.getregistry()["xlua.builtin_chunk"], "remove_libraries", %d'
                   % names["remove_libraries"][3])
    with failing_alloc(lib, state) as fail_after:
        for allowed in range(1000):
            run(lib, state, get, 3)
            fail_after(allowed)
            status = lib.lua_pcall(state, 2, 2, 0)
            fail_after(None)
            if status == OK and lib.lua_gettop(state) == 2 and tostring(lib, state, -1) == b"":
                lib.lua_settop(state, 0)
                break  # the function and nil
            message = tostring(lib, state, -1)
            assert b"not enough memory" in message, (allowed, status, message)
            lib.lua_settop(state, 0)
        else:
            raise AssertionError("the built-in chunk loader failed with 1000 allocations allowed")


def check_no_builtin_chunks(lib, state):
    """A library with the compiler has no loader of built-in chunks: LuaEnv loads their text."""
    lib = bind(lib)
    top = lib.lua_gettop(state)
    code = b"return debug.getregistry()['xlua.builtin_chunk'] == nil and 'none' or 'found'"
    assert lib.luaL_loadstring(state, code) == OK and lib.lua_pcall(state, 0, 1, 0) == OK
    assert tostring(lib, state, -1) == b"none", "xlua.builtin_chunk is in the registry of a library with the compiler"
    lib.lua_settop(state, top)


def main(lib, state):
    path = os.environ.get("XLUA_COMPILER_LIB")
    assert path, ("a bytecode-only library (XLUA_BYTECODE_ONLY): set XLUA_COMPILER_LIB to a library "
                  "with the compiler and the same Lua version and bytecode format")
    compile_ = Compiler(path)
    lib = bind(lib)
    try:
        for check in (check_text_refused, check_bytecode_loads, check_setfenv, check_require, check_builtin_chunks):
            check(lib, state, compile_)
            assert lib.lua_gettop(state) == 0, (check.__name__, lib.lua_gettop(state))
            print(check.__name__[6:] + ": ok")
    finally:
        compile_.close()
    print("XLUA_NATIVE_REGRESSION_PASS (bytecode only)")
    return 0
