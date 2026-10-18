"""The core_sandbox checks of native_regression.py: LuaScript's native sandbox
(build/xlua_core_sandbox.c, docs/design/core-luascript.md sections 3.8, 3.9 and
4) through its exports, on a core library (XLUA_PRESET=core) or a bridge library
built with XLUA_LUASCRIPT=ON. native_regression.py runs them; they are a module
of their own because a core library has none of the bridge functions that
native_regression.py binds when it starts.

Checks:
- core_sandbox: every case of core_sandbox_cases.txt (the sandbox rules), the
  same file core_smoke.c runs;
- core_sandbox_abi: xlua_core_open's configuration and memory errors, library
  subsets, the hash seed, environment handles, require without a resolver or
  switched off, and the memory overdraft rule around the resolver;
- core_host_oom: the C library's allocation failure (Linux without ASan);
- core_lua_api: needs the Lua C API exported (XLUA_EXPORTS=all): the bytecode
  stubs of a core library (xlua_core_nobc.c) behind lua_load and lua_dump,
  states without the sandbox, and handles that are not environments;
- core_buffer: the buffer operations of xlua_core_handle (NEWBUFFER,
  BUFFER), buffer handles in xlua_core_call's arguments and results
  and their release through xlua_core_unref, the memory limit, and a state
  without the library. XLUA_EXPECT_BUFFER=ON or OFF says whether the build has
  the library (CMake XLUA_BUFFER); unset, the library decides. The cases with
  "--- libs: buffer" run in a second state with the library, or are skipped
  without it;
- core_trusted_bytecode: the modes of xlua_core_call's chunks. Without CMake
  XLUA_TRUSTED_BYTECODE binary chunks are refused before anything runs; with it
  the host's precompiled chunks (TRUSTED below) run in an environment, text and
  binary keep to their modes, a damaged chunk fails in Lua's loader, and a
  script's load stays text only. XLUA_EXPECT_TRUSTED_BYTECODE=ON or OFF says
  which build it is; unset, the library decides.
- core_coroutines: XLUA_CALL_START, _RESUME and _CLOSE (milestone 9), coroutines
  as handles with their status, and the limits per step.
- core_stop_latency: the time limit and Interrupt stop xlua_core_call within
  100 ms, also with an instruction limit, and Interrupt works without limits
  (XLUA_LIMITS_INTERRUPT); Test/Native/stop_latency.py.
"""
import ctypes as C
import os
from pathlib import Path

P = C.c_void_p
HERE = Path(__file__).resolve().parent
OK, ERRRUN, ERRSYNTAX, ERRMEM, LIMIT_INSTR, ERRHANDLE, ERRCONFIG = 0, 2, 3, 4, 10, 22, 23
LIBS = {"coroutine": 1, "string": 2, "table": 4, "math": 8, "utf8": 16, "buffer": 32,
        "io": 64, "os": 128, "debug": 256, "package": 512}
SMALL_SLICES = 2
BINARY = b"\x1bLua\x54\x00\x19\x93\r\n\x1a\n"
REGISTRY = {"Lua 5.4": -1000000 - 1000, "Lua 5.5": -((2 ** 31 - 1) // 2 + 1000)}  # LUA_REGISTRYINDEX (lua.h)
MODE_TEXT, MODE_BINARY = 1, 2  # XLUA_CORE_MODE_* (build/xlua_core.h)
# Chunks precompiled with the Lua 5.4.9 and 5.5.1 of build/ (stripped). Their format
# holds on every supported target: little-endian, 4-byte int and instruction,
# 64-bit integers and floats. "main" is the chunk
#   return (_G == _ENV and string ~= nil and io == nil) and "trusted" or "wrong"
# and "noup" string.dump(function() return 7 end, true), a function without upvalues.
# core_smoke.c has the same bytes.
TRUSTED = {
    "Lua 5.4": {
        "main": bytes.fromhex(
            "1b4c7561540019930d0a1a0a040808785600000000000000000000002877400180808000010291510000000b000000890000"
            "0039000100380400800b0000013c800200b80200800b0000033c000200380100800300020042800000380000800380020046"
            "000201460001018604835f470487737472696e67000483696f048874727573746564048677726f6e67810100008080808080"),
        "noup": bytes.fromhex(
            "1b4c7561540019930d0a1a0a04080878560000000000000000000000287740008081810000028301000380480002004700"
            "010080808080808080"),
    },
    "Lua 5.5": {
        "main": bytes.fromhex(
            "1b4c7561550019930d0a1a0a0488a9ffff04785634120888a9ffffffffffff0800000000002877c00100000001021100530000"
            "000b0000008900000039000100380400800b0000013c800200b80200800b0000033c000200380100800300020042800000380000"
            "800380020046000201460001010604035f47000407737472696e6700000403696f0004087472757374656400040677726f6e67"
            "000101000000000000000000"),
        "noup": bytes.fromhex(
            "1b4c7561550019930d0a1a0a0488a9ffff04785634120888a9ffffffffffff0800000000002877c000010100000203000100038048"
            "00020047000100000000000000000000"),
    },
}
DIRECTIVES = ("env", "status", "expect", "instructions", "require", "libs")
SAFE = LIBS["coroutine"] | LIBS["string"] | LIBS["table"] | LIBS["math"] | LIBS["utf8"]


class Config(C.Structure):  # xlua_core_config, build/xlua_core.h
    _fields_ = [("memory_limit", C.c_int64), ("seed", C.c_uint32), ("has_seed", C.c_int32),
                ("libs", C.c_int32), ("sandbox", C.c_int32)]


class Stats(C.Structure):  # xlua_core_stats
    _fields_ = [(n, C.c_int64) for n in ("memory_used", "memory_peak", "memory_limit", "refusals", "overdrafts",
                                         "host_oom", "instructions", "stop_reason", "instruction_limit",
                                         "time_limit_ns")]


class Source(C.Structure):  # xlua_core_source
    _fields_ = [("code", P), ("code_len", C.c_int32), ("chunkname", P), ("chunkname_len", C.c_int32)]


class Value(C.Structure):  # xlua_value
    class U(C.Union):
        _fields_ = [("i", C.c_int64), ("n", C.c_double), ("s", P)]
    _fields_ = [("type", C.c_int32), ("len", C.c_int32), ("u", U)]


class Call(C.Structure):  # xlua_core_call_t
    _fields_ = [("instructions", C.c_int64), ("name", P), ("code", P), ("args", C.POINTER(Value)),
                ("results", C.POINTER(Value)), ("error", P), ("traceback", P)] \
        + [(n, C.c_int32) for n in ("kind", "env", "fn", "name_len", "code_len", "nargs", "results_cap", "nresults",
                                    "error_len", "traceback_len", "error_ref", "mode", "flags")]


T_NIL, T_BOOL, T_INT, T_NUM, T_STR, T_BUFFER, T_OTHER = 0, 1, 2, 3, 4, 7, 8
CALL_CHUNK = 2
TYPE_NAMES = {5: "table", 6: "function", 7: "buffer", 8: "userdata", 9: "thread"}
RESOLVER = C.CFUNCTYPE(C.c_int32, P, P, C.c_int32, C.POINTER(Source))
CALLBACK_ERRORS = []


def bind(lib):
    for name, args, res in [
            ("xlua_core_version", [], C.c_int),
            ("xlua_core_open", [C.POINTER(Config), P, C.POINTER(C.c_int32)], P),
            ("xlua_core_close", [P], None),
            ("xlua_core_set_memory_limit", [P, C.c_int64], C.c_int),
            ("xlua_core_get_stats", [P, C.POINTER(Stats)], C.c_int),
            ("xlua_core_set_limits", [P, C.c_int64, C.c_int64, C.c_int32], C.c_int),
            ("xlua_core_handle", [P, C.c_int32, C.POINTER(Value)], C.c_int),
            ("xlua_core_unref", [P, C.c_int32], C.c_int),
            ("xlua_core_call", [P, C.POINTER(Call)], C.c_int),
            ("xlua_core_set_resolver", [P], None),
            ("xlua_core_set_require", [P, C.c_int32], C.c_int),
            ("xlua_core_interrupt", [P], C.c_int), ("xlua_core_watch", [P], C.c_int64),
            ("xlua_gc", [P, C.c_int, C.c_int], C.c_int),
            ("lua_checkstack", [P, C.c_int], C.c_int),
            ("lua_gettop", [P], C.c_int)]:
        fn = getattr(lib, name)
        fn.argtypes = args
        fn.restype = res
    handle_shims(lib)


H_NEWENV, H_NEWBUFFER, H_BUFFER = 6, 10, 11


def handle_shims(lib):
    """What the exports xlua_core_newenv, _newbuffer and _tobuffer did before
    xlua_core_handle replaced them (milestone 8), with the same arguments
    (C.byref outputs)."""
    def newenv(L, env):
        v = (Value * 4)()
        status = lib.xlua_core_handle(L, H_NEWENV, v)
        env._obj.value = v[0].u.i if status == OK else 0
        return status

    def newbuffer(L, length, ref, data):
        v = (Value * 4)()
        v[1].u.i = length
        status = lib.xlua_core_handle(L, H_NEWBUFFER, v)
        ref._obj.value = v[0].u.i if status == OK else 0
        data._obj.value = v[2].u.s if status == OK else None
        return status

    def tobuffer(L, ref, length):
        v = (Value * 4)()
        v[0].u.i = ref
        if lib.xlua_core_handle(L, H_BUFFER, v) != OK:
            length._obj.value = 0
            return None
        length._obj.value = v[2].len
        return v[2].u.s

    lib.xlua_core_newenv = newenv
    lib.xlua_core_newbuffer = newbuffer
    lib.xlua_core_tobuffer = tobuffer


def parse_cases(path):
    """(cases, modules) of a case file: lists of dicts with name, code (bytes) and
    the directives."""
    cases, modules, cur = [], {}, None
    lines = path.read_text().split("\n")

    def finish():
        if cur is not None:
            code = "\n".join(cur.pop("lines")).rstrip("\n") + "\n"
            cur["code"] = BINARY if code == "@BINARY@\n" else code.encode()

    for line in lines:
        if line.startswith("=== module "):
            finish()
            cur = {"name": line[11:], "lines": []}
            modules[cur["name"]] = cur
        elif line.startswith("--- "):
            key, _, value = line[4:].partition(": ")
            if cur is not None and not cur["lines"] and key in DIRECTIVES:
                cur[key] = value
                continue
            finish()
            cur = {"name": line[4:], "lines": []}
            cases.append(cur)
        elif cur is None:
            assert not line or line.startswith("#"), "text before the first case: " + line
        else:
            cur["lines"].append(line)
    finish()
    return cases, {name: m["code"] for name, m in modules.items()}


class Host:
    """The resolver of the cases (core_sandbox_cases.txt, its header) and of the
    overdraft checks."""

    def __init__(self, lib, modules):
        self.lib, self.modules = lib, modules
        self.L, self.flaky, self.overdraft = None, 0, 0
        self.keep = []  # buffers handed to the library stay alive until the next request
        self.callback = RESOLVER(self.resolve_checked)

    def give(self, out, code, chunkname=b""):
        self.keep = [C.create_string_buffer(code, len(code) + 1), C.create_string_buffer(chunkname, len(chunkname) + 1)]
        out.contents.code = C.cast(self.keep[0], P)
        out.contents.code_len = len(code)
        out.contents.chunkname = C.cast(self.keep[1], P)
        out.contents.chunkname_len = len(chunkname)

    def resolve_checked(self, host, name, length, out):
        """ctypes ignores an exception in a callback: record it for main()."""
        try:
            return self.resolve(host, name, length, out)
        except BaseException as e:  # noqa: B902 - any failure, assertions included
            CALLBACK_ERRORS.append(repr(e))
            return 0

    def resolve(self, _host, name, length, out):
        name = C.string_at(name, length).decode()
        if name == "missing":
            return 0
        if name == "broken":
            self.give(out, b"resolver failed")
            return -1
        if name == "no_reason":
            out.contents.code_len = -1
            return -1
        if name == "flaky":
            self.flaky += 1
            self.give(out, b"return = 1" if self.flaky == 1 else b"return 'fixed'")
            return 1
        if name == "empty":
            out.contents.code_len = -5
            return 1
        if name == "nested":
            # C# calls into the state while require waits for it: nested entries.
            assert run(self.lib, self.L, "return 'inner'") == (OK, "inner")
            status, message = run(self.lib, self.L, "error('inner error')")
            assert status == ERRRUN and "inner error" in message, (status, message)
            self.give(out, b"return 'outer'")
            return 1
        if name == "interrupt":
            # Interrupt from C# while the call runs: the module never starts.
            stats = Stats()
            assert self.lib.xlua_core_interrupt(self.L) == 1 and self.lib.xlua_core_interrupt(self.L) == 0
            assert self.lib.xlua_core_get_stats(self.L, C.byref(stats)) == 1 and stats.stop_reason == 12
            self.give(out, b"return 'never'")
            return 1
        if name == "watch":
            # The watchdog's entry: time left, then the stop once the deadline passed.
            assert self.lib.xlua_core_watch(self.L) > 0
            while self.lib.xlua_core_watch(self.L) != -1:
                pass
            self.give(out, b"return 'never'")
            return 1
        if name == "overdraft":
            stats = Stats()
            # C# runs: the stack grows over the limit, which is granted.
            assert self.lib.lua_checkstack(self.L, 200000) == 1
            assert self.lib.xlua_core_get_stats(self.L, C.byref(stats)) == 1 and stats.overdrafts > 0
            if self.overdraft == 2:
                self.lib.xlua_core_set_memory_limit(self.L, 0)
            elif self.overdraft == 3:
                self.lib.xlua_core_set_memory_limit(self.L, stats.memory_used * 4)
            self.give(out, b"return 'loaded'")
            return 1
        if name in self.modules:
            self.give(out, self.modules[name], b"custom/named.lua" if name == "named" else b"")
            return 1
        return 0


def open_state(lib, limit=0, libs=0, seed=None, sandbox=1):
    """xlua_core_open; returns (state or None, status). The host pointer is NULL:
    Host keeps its own state."""
    status = C.c_int32(-1)
    cfg = Config(limit, seed or 0, int(seed is not None), libs, sandbox)
    return lib.xlua_core_open(C.byref(cfg), None, C.byref(status)), status.value


def value_text(v):
    """A result as tostring makes it, for the types the cases return."""
    if v.type == T_STR:
        return C.string_at(v.u.s, v.len).decode("utf-8", "replace") if v.len else ""
    if v.type == T_BOOL:
        return "true" if v.u.i else "false"
    if v.type == T_INT:
        return str(v.u.i)
    if v.type == T_NUM:
        return "%.14g" % v.u.n
    return TYPE_NAMES.get(v.type, "nil")


def run(lib, L, code, env=0, name=b"t", code_len=None, name_len=None, mode=0):
    """A chunk through xlua_core_call: (status, the first result as text, "nil" without one,
    or the message and traceback of an error, None without a message)."""
    code = code if isinstance(code, bytes) else code.encode()
    result = Value()
    c = Call(kind=CALL_CHUNK, env=env, code=C.cast(C.c_char_p(code), P), name=C.cast(C.c_char_p(name), P),
             code_len=len(code) if code_len is None else code_len, name_len=len(name) if name_len is None else name_len,
             results=C.pointer(result), results_cap=1, mode=mode)
    status = lib.xlua_core_call(L, C.byref(c))
    if status != OK:
        assert c.nresults == 0
        if not c.error:
            assert not c.traceback
            return status, None
        text = C.string_at(c.error, c.error_len).decode("utf-8", "replace")
        if c.traceback:
            text += "\n" + C.string_at(c.traceback, c.traceback_len).decode("utf-8", "replace")
        return status, text
    return status, value_text(result) if c.nresults else "nil"


def run_mode(lib, L, code, mode, env=0, name=b"t"):
    """run with xlua_core_call_t.mode (XLUA_CORE_MODE_* bits; 0 is text)."""
    return run(lib, L, code, env, name, mode=mode)


def trusted_bytecode(lib):
    """Whether xlua_core_call takes binary chunks (CMake XLUA_TRUSTED_BYTECODE), checked
    against XLUA_EXPECT_TRUSTED_BYTECODE when it is set."""
    L, _ = open_state(lib)
    try:
        status, out = run_mode(lib, L, "return 1", MODE_TEXT | MODE_BINARY)
    finally:
        lib.xlua_core_close(L)
    built = status == OK
    assert built or (status, out) == (ERRCONFIG, "binary chunks are not supported in this build (XLUA_TRUSTED_BYTECODE is off)"), (status, out)
    expect_ = os.environ.get("XLUA_EXPECT_TRUSTED_BYTECODE", "").upper()
    assert expect_ != "ON" or built, "XLUA_EXPECT_TRUSTED_BYTECODE=ON, but xlua_core_call refuses binary chunks"
    assert expect_ != "OFF" or not built, "XLUA_EXPECT_TRUSTED_BYTECODE=OFF, but xlua_core_call takes binary chunks"
    return built


def buffer_built(lib):
    """Whether the library has the buffer library (CMake XLUA_BUFFER), checked against
    XLUA_EXPECT_BUFFER when it is set."""
    L, status = open_state(lib, libs=SAFE | LIBS["buffer"])
    built = L is not None
    if built:
        lib.xlua_core_close(L)
    else:
        assert status == ERRCONFIG, status
    expect_ = os.environ.get("XLUA_EXPECT_BUFFER", "").upper()
    assert expect_ != "ON" or built, "XLUA_EXPECT_BUFFER=ON, but xlua_core_open refuses LuaLibs.Buffer"
    assert expect_ != "OFF" or not built, "XLUA_EXPECT_BUFFER=OFF, but xlua_core_open opens LuaLibs.Buffer"
    return built


def expect(lib, L, code, status, text=None, env=0):
    got, out = run(lib, L, code, env)
    assert got == status and (text is None or text in (out or "")), (code, got, status, out)
    return out


def stats_of(lib, L):
    stats = Stats()
    assert lib.xlua_core_get_stats(L, C.byref(stats)) == 1
    return stats


def check_core_sandbox(lib):
    cases, modules = parse_cases(HERE / "core_sandbox_cases.txt")
    assert len(cases) >= 50 and len(modules) >= 5, (len(cases), len(modules))
    host = Host(lib, modules)
    L, status = open_state(lib, limit=16 << 20)
    assert L and status == OK, status
    host.L = L
    lib.xlua_core_set_resolver(C.cast(host.callback, P))
    main_state, main_envs = L, {"globals": 0}
    buffer_state, buffer_envs, skipped = None, {"globals": 0}, 0
    has_buffer = buffer_built(lib)
    if has_buffer:
        buffer_state, status = open_state(lib, limit=16 << 20, libs=SAFE | LIBS["buffer"])
        assert buffer_state and status == OK, status
    failed = []
    try:
        for case in cases:
            assert case.get("libs") in (None, "buffer"), case
            if case.get("libs") == "buffer":
                if not has_buffer:
                    skipped += 1
                    continue
                L, envs = buffer_state, buffer_envs
            else:
                L, envs = main_state, main_envs
            if case.get("require") == "on":
                assert lib.xlua_core_set_require(L, 1) == OK
            fresh = "env" not in case
            if fresh:
                env = C.c_int32()
                assert lib.xlua_core_newenv(L, C.byref(env)) == OK and env.value > 0
                env = env.value
            else:
                if case["env"] not in envs:
                    handle = C.c_int32()
                    assert lib.xlua_core_newenv(L, C.byref(handle)) == OK
                    envs[case["env"]] = handle.value
                env = envs[case["env"]]
            instructions = int(case.get("instructions", 0))
            if instructions:
                assert lib.xlua_core_set_limits(L, instructions, 0, SMALL_SLICES) == 1
            status, out = run(lib, L, case["code"], env)
            if instructions:
                assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
            want = int(case.get("status", 0))
            if status != want or ("expect" in case and case["expect"] not in (out or "")):
                failed.append("%s: status %d (expected %d), out: %s" % (case["name"], status, want, out))
            if fresh:
                assert lib.xlua_core_unref(L, env) == 1
        assert host.flaky == 2, host.flaky
        assert lib.lua_gettop(main_state) == 0
        assert buffer_state is None or lib.lua_gettop(buffer_state) == 0
    finally:
        lib.xlua_core_set_resolver(None)
        lib.xlua_core_close(main_state)
        if buffer_state:
            lib.xlua_core_close(buffer_state)
    assert not failed, "\n".join(failed)
    print("core_sandbox: %d cases, %d modules%s" % (len(cases), len(modules),
          ", %d buffer cases skipped (no buffer library)" % skipped if skipped else ""))


def check_core_sandbox_abi(lib):
    assert lib.xlua_core_version() == 6
    # Configuration: the sandbox is required, and only the libraries this build has.
    assert open_state(lib, sandbox=0) == (None, ERRCONFIG)
    for libs in (LIBS["io"], LIBS["os"], LIBS["debug"], LIBS["package"] | LIBS["string"], 1 << 12) \
            + (() if buffer_built(lib) else (LIBS["buffer"],)):
        assert open_state(lib, libs=libs) == (None, ERRCONFIG), libs
    assert open_state(lib, limit=1) == (None, ERRMEM)  # not even the core struct
    # Memory errors in xlua_core_newstate and while the libraries open.
    errors, limit = 0, 512
    while True:
        L, status = open_state(lib, limit=limit)
        if L:
            break
        assert status == ERRMEM, status
        errors += 1
        limit += 512
    assert errors > 4 and stats_of(lib, L).memory_used <= limit
    lib.xlua_core_close(L)
    # Library subsets.
    L, _ = open_state(lib, libs=LIBS["string"])
    expect(lib, L, "assert(coroutine == nil and table == nil and math == nil and utf8 == nil) return ('x'):rep(3)", OK, "xxx")
    lib.xlua_core_close(L)
    L, _ = open_state(lib, libs=LIBS["coroutine"] | LIBS["math"])
    expect(lib, L, "assert(string == nil and coroutine.wrap and math.pi) return getmetatable('')", OK, "nil")
    expect(lib, L, "return ('x'):upper()", ERRRUN, "attempt to index a string value")
    lib.xlua_core_close(L)
    L, _ = open_state(lib, libs=LIBS["table"] | LIBS["utf8"])
    expect(lib, L, "return table.concat({utf8.char(72), 'i'}) .. tostring(coroutine)", OK, "Hinil")
    lib.xlua_core_close(L)
    # The same seed, the same pairs order.
    order = "local t = {} for i = 1, 64 do t['key' .. i] = i end local s = {} for k in pairs(t) do s[#s + 1] = k end return table.concat(s, ',')"
    a, _ = open_state(lib, seed=42)
    b, _ = open_state(lib, seed=42)
    assert run(lib, a, order) == run(lib, b, order)
    lib.xlua_core_close(a)
    lib.xlua_core_close(b)
    # Environment handles.
    L, _ = open_state(lib)
    assert lib.xlua_core_unref(L, 0) == 0 and lib.xlua_core_unref(L, -3) == 0
    assert lib.xlua_core_unref(L, 1) == 0 and lib.xlua_core_unref(L, 2) == 0  # the main thread and the base
    env, other = C.c_int32(), C.c_int32()
    assert lib.xlua_core_newenv(L, C.byref(env)) == OK and lib.xlua_core_newenv(L, C.byref(other)) == OK
    expect(lib, L, "x = 'mine' return x", OK, "mine", env.value)
    expect(lib, L, "return tostring(x)", OK, "nil", other.value)
    assert lib.xlua_core_unref(L, env.value) == 1 and lib.xlua_core_unref(L, env.value) == 0
    for handle in (env.value, -1, 2, 99999):
        assert run(lib, L, "return 1", handle) == (ERRHANDLE, "invalid environment handle"), handle
    assert run(lib, L, b"return 1", other.value, b"neg", -1, -4) == (OK, "nil")  # negative lengths are empty
    assert lib.lua_gettop(L) == 0
    # xlua_gc, LuaScript's CollectGarbage: Lua 5.5 maps the managed option numbers.
    assert lib.xlua_gc(L, 3, 0) > 0  # LUA_GCCOUNT
    for what in (2, 4, 5, 6, 7, 0, 9, 1):  # collect, countb, step, setpause, setstepmul, stop, isrunning, restart
        lib.xlua_gc(L, what, 100 if what in (6, 7) else 0)
    assert lib.xlua_gc(L, 9, 0) == 1 and lib.xlua_gc(L, 8, 0) == -1 and lib.xlua_gc(L, -1, 0) == -1
    # A limit below the current use: every growth fails, also in the exports' own work.
    assert lib.xlua_core_set_memory_limit(L, 1) == 1
    assert run(lib, L, "return {}") == (ERRMEM, "not enough memory")
    handle = C.c_int32(7)
    assert lib.xlua_core_newenv(L, C.byref(handle)) == ERRMEM
    # A negative memory limit is none.
    assert lib.xlua_core_set_memory_limit(L, -5) == 1 and stats_of(lib, L).memory_limit == 0
    # Interrupt and the watchdog entry from inside a call, through the resolver.
    host = Host(lib, {})
    host.L = L
    lib.xlua_core_set_resolver(C.cast(host.callback, P))
    try:
        assert lib.xlua_core_set_require(L, 1) == OK
        assert lib.xlua_core_interrupt(L) == 0 and lib.xlua_core_watch(L) == -1  # nothing runs
        assert lib.xlua_core_set_limits(L, 10 ** 9, 0, 0) == 1
        expect(lib, L, "require('interrupt')", 12, "interrupted")
        expect(lib, L, "return require('nested')", OK, "outer")
        assert lib.xlua_core_set_limits(L, 0, 10 ** 6, 0) == 1
        expect(lib, L, "require('watch')", 11, "time limit exceeded")
        assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
    finally:
        lib.xlua_core_set_resolver(None)
    lib.xlua_core_close(L)
    # require without a resolver, switched off, and the overdraft rule.
    host = Host(lib, {})
    L, _ = open_state(lib)
    host.L = L
    assert lib.xlua_core_set_require(L, 1) == OK
    lib.xlua_core_set_resolver(None)
    expect(lib, L, "require('x')", ERRRUN, "module 'x' not found: no module resolver")
    lib.xlua_core_set_resolver(C.cast(host.callback, P))
    try:
        expect(lib, L, "require('no_reason')", ERRRUN, "error loading module 'no_reason': \n")
        expect(lib, L, "return require('empty')", OK, "true")
        assert lib.xlua_core_set_require(L, 0) == OK
        expect(lib, L, "return require", OK, "nil")
        assert lib.xlua_core_set_require(L, 1) == OK
        used = stats_of(lib, L).memory_used
        lib.xlua_core_set_memory_limit(L, used + 64 * 1024)
        host.overdraft = 1
        expect(lib, L, "return require('overdraft')", ERRMEM, "not enough memory")
        lib.xlua_core_set_memory_limit(L, used + 64 * 1024)
        host.overdraft = 2
        expect(lib, L, "return require('overdraft')", OK, "loaded")
        lib.xlua_core_close(L)
        L, _ = open_state(lib)
        host.L = L
        assert lib.xlua_core_set_require(L, 1) == OK
        lib.xlua_core_set_memory_limit(L, stats_of(lib, L).memory_used + 64 * 1024)
        host.overdraft = 3
        expect(lib, L, "return require('overdraft')", OK, "loaded")
    finally:
        lib.xlua_core_set_resolver(None)
        lib.xlua_core_close(L)
    print("core_sandbox_abi: ok")


def check_core_host_oom(lib):
    # The C library's own failure (xlua_core_alloc counts it as host_oom), as
    # native_regression.py's core_host_oom: Linux only, with an address-space
    # limit, which ASan's reservations do not leave room for.
    import os
    import sys
    if not sys.platform.startswith("linux") or "asan" in os.environ.get("LD_PRELOAD", ""):
        print("core_host_oom: skipped (needs Linux without ASan)")
        return
    import resource
    L, status = open_state(lib)
    assert L and status == OK
    soft, hard = resource.getrlimit(resource.RLIMIT_AS)
    vm = int(next(line for line in open("/proc/self/status") if line.startswith("VmSize:")).split()[1]) * 1024
    try:
        resource.setrlimit(resource.RLIMIT_AS, (vm + (256 << 20), hard))
        result = run(lib, L, "local ok, err = pcall(string.rep, 'x', 1 << 30) return err")
    finally:
        resource.setrlimit(resource.RLIMIT_AS, (soft, hard))
    assert result == (OK, "not enough memory"), result
    assert stats_of(lib, L).host_oom >= 1
    lib.xlua_core_close(L)
    print("core_host_oom: ok")


def check_core_lua_api(lib):
    if not hasattr(lib, "luaL_loadbufferx"):
        print("core_lua_api: the Lua C API is not exported (XLUA_EXPORTS=managed), skipped")
        return
    for name, args, res in [("luaL_loadbufferx", [P, C.c_char_p, C.c_size_t, C.c_char_p, C.c_char_p], C.c_int),
                            ("lua_tolstring", [P, C.c_int, P], C.c_char_p), ("lua_settop", [P, C.c_int], None),
                            ("lua_createtable", [P, C.c_int, C.c_int], None), ("lua_setmetatable", [P, C.c_int], C.c_int),
                            ("luaL_ref", [P, C.c_int], C.c_int), ("xlua_core_newstate", [C.POINTER(Config), C.POINTER(C.c_int32)], P),
                            ("lua_dump", [P, P, P, C.c_int], C.c_int), ("lua_pushstring", [P, C.c_char_p], P)]:
        fn = getattr(lib, name)
        fn.argtypes = args
        fn.restype = res
    core = not hasattr(lib, "luaopen_xlua")
    stub = core and not trusted_bytecode(lib)
    L, _ = open_state(lib)
    version = run(lib, L, "return _VERSION")[1]
    try:
        # A binary chunk through the C API, past the sandbox's mode "t": the
        # stubs refuse it in a core library; a full library, or a core one with
        # XLUA_TRUSTED_BYTECODE, has Lua's loader, and LuaScript's own entries
        # ask it for binary chunks only through xlua_core_call's mode.
        # Lua 5.5's mode "B" (fixed buffers) skips lua_load's mode check.
        for mode in [b"b", None] + ([b"B"] if version == "Lua 5.5" else []):
            status = lib.luaL_loadbufferx(L, BINARY, len(BINARY), b"=bin", mode)
            message = lib.lua_tolstring(L, -1, None).decode()
            lib.lua_settop(L, 0)
            assert status == ERRSYNTAX, (mode, status, message)
            assert ("binary chunks are not supported" in message) == stub, (mode, message)
        if core:
            writer = C.CFUNCTYPE(C.c_int, P, P, C.c_size_t, P)(lambda *a: 0)
            assert lib.luaL_loadbufferx(L, b"return 1", 8, b"=f", b"t") == OK
            assert lib.lua_dump(L, C.cast(writer, P), None, 0) == 1  # the dumper stub
            lib.lua_settop(L, 0)
        # Since milestone 8 any table handle is an environment (a registry reference to a
        # table, whatever its metatable), and xlua_core_unref releases it; a string is neither.
        lib.lua_createtable(L, 0, 0)
        lib.lua_createtable(L, 0, 0)
        lib.lua_setmetatable(L, -2)
        ref = lib.luaL_ref(L, REGISTRY[version])
        assert run(lib, L, "return 1", ref) == (OK, "1")
        assert lib.xlua_core_unref(L, ref) == 1 and lib.xlua_core_unref(L, ref) == 0
        lib.lua_pushstring(L, b"not a table")
        ref = lib.luaL_ref(L, REGISTRY[version])
        assert run(lib, L, "return 1", ref) == (ERRHANDLE, "invalid environment handle")
        assert lib.xlua_core_unref(L, ref) == 0
    finally:
        lib.xlua_core_close(L)
    # A state without the core allocator: no core state.
    lib.luaL_newstate.restype = P
    plain = lib.luaL_newstate()
    stats = Stats()
    assert lib.xlua_core_get_stats(plain, C.byref(stats)) == 0 and lib.xlua_core_set_memory_limit(plain, 1) == 0
    assert run(lib, plain, "return 1") == (ERRCONFIG, None)
    lib.xlua_core_close(plain)
    # A state without the sandbox.
    status = C.c_int32()
    L = lib.xlua_core_newstate(C.byref(Config(0, 0, 0, 0, 0)), C.byref(status))
    assert L and status.value == OK
    env = C.c_int32(5)
    assert run(lib, L, "return 1") == (ERRCONFIG, None)
    assert lib.xlua_core_newenv(L, C.byref(env)) == ERRCONFIG and env.value == 0
    assert lib.xlua_core_set_require(L, 1) == ERRCONFIG and lib.xlua_core_unref(L, 3) == 0
    lib.xlua_core_close(L)
    print("core_lua_api: ok")


def call_global(lib, L, name, args=(), cap=4):
    """xlua_core_call of the global function name with xlua_value arguments:
    (status, nresults, results array, error text or None)."""
    argv = (Value * max(1, len(args)))(*args)
    results = (Value * cap)()
    name = name.encode()
    c = Call(kind=0, name=C.cast(C.c_char_p(name), P), name_len=len(name), args=argv, nargs=len(args),
             results=results, results_cap=cap)
    status = lib.xlua_core_call(L, C.byref(c))
    error = C.string_at(c.error, c.error_len).decode("utf-8", "replace") if c.error else None
    return status, c.nresults, results, error


def buffer_value(handle):
    v = Value()
    v.type, v.u.i = T_BUFFER, handle
    return v


def check_core_buffer(lib):
    built = buffer_built(lib)
    ref, data = C.c_int32(5), P(1)
    length = C.c_int32(7)
    if not built:
        # The exports exist and report that the build has no buffer library.
        L, _ = open_state(lib)
        assert lib.xlua_core_newbuffer(L, 4, C.byref(ref), C.byref(data)) == ERRCONFIG and ref.value == 0 and not data.value
        assert not lib.xlua_core_tobuffer(L, 1, C.byref(length)) and length.value == 0
        lib.xlua_core_close(L)
        print("core_buffer: no buffer library (XLUA_BUFFER=OFF): the exports refuse")
        return
    # A state without LuaLibs.Buffer cannot make buffers.
    L, _ = open_state(lib)
    assert lib.xlua_core_newbuffer(L, 4, C.byref(ref), C.byref(data)) == ERRCONFIG and ref.value == 0
    assert run(lib, L, "return buffer") == (OK, "nil")
    lib.xlua_core_close(L)
    L, status = open_state(lib, limit=8 << 20, libs=SAFE | LIBS["buffer"])
    assert L and status == OK
    try:
        expect(lib, L, "function len(b) return buffer.len(b) end\n"
                       "function make(n) local b = buffer.create(n) buffer.writeu32(b, 0, 0xA1B2C3D4) return b end\n"
                       "function same(a, b) return rawequal(a, b) end\n"
                       "function keep(b) kept = b end\n"
                       "function many(n) local t = {} for i = 1, n do t[i] = buffer.create(8) end return table.unpack(t) end", OK)
        # Out-of-range lengths.
        for n in (-1, (1 << 30) + 1, -(1 << 31)):
            assert lib.xlua_core_newbuffer(L, n, C.byref(ref), C.byref(data)) == ERRCONFIG and ref.value == 0, n
        # A buffer from C: zero-filled, writable through its pointer, seen by Lua.
        assert lib.xlua_core_newbuffer(L, 16, C.byref(ref), C.byref(data)) == OK and ref.value > 0 and data.value
        assert C.string_at(data.value, 16) == b"\0" * 16
        C.memmove(data.value, b"\x04\x03\x02\x01", 4)
        p = lib.xlua_core_tobuffer(L, ref.value, C.byref(length))
        assert p == data.value and length.value == 16
        status, n, results, _ = call_global(lib, L, "len", [buffer_value(ref.value)])
        assert status == OK and n == 1 and results[0].type == T_INT and results[0].u.i == 16
        expect(lib, L, "", OK)
        status, n, results, _ = call_global(lib, L, "keep", [buffer_value(ref.value)])
        assert status == OK
        expect(lib, L, "return buffer.readu32(kept, 0)", OK, str(0x01020304))
        # A buffer result is an owning handle with the length; the same buffer twice is two handles.
        status, n, results, _ = call_global(lib, L, "make", [Value(type=T_INT, u=Value.U(i=8))])
        assert status == OK and n == 1 and results[0].type == T_BUFFER and results[0].len == 8 and results[0].u.i > 0
        made = results[0].u.i
        p = lib.xlua_core_tobuffer(L, made, C.byref(length))
        assert p and length.value == 8 and C.string_at(p, 4) == b"\xd4\xc3\xb2\xa1", "little-endian"
        status, n, results, _ = call_global(lib, L, "same", [buffer_value(made), buffer_value(made)])
        assert status == OK and results[0].type == T_BOOL and results[0].u.i == 1
        # Handles that are not buffers, or released ones, are refused.
        assert not lib.xlua_core_tobuffer(L, 0, C.byref(length)) and length.value == 0
        assert not lib.xlua_core_tobuffer(L, -1, C.byref(length))
        assert not lib.xlua_core_tobuffer(L, 2, C.byref(length))  # the base table, LUA_RIDX_GLOBALS
        env = C.c_int32()
        assert lib.xlua_core_newenv(L, C.byref(env)) == OK
        assert not lib.xlua_core_tobuffer(L, env.value, C.byref(length)), "an environment is no buffer"
        for bad in (0, -3, 2, env.value, 1 << 40):
            status, _, _, error = call_global(lib, L, "len", [buffer_value(bad)])
            assert status == ERRHANDLE and error == "argument 1 is not a valid handle (type 7)", (bad, status, error)
        assert lib.xlua_core_unref(L, env.value) == 1
        assert lib.xlua_core_unref(L, made) == 1 and lib.xlua_core_unref(L, made) == 0
        assert not lib.xlua_core_tobuffer(L, made, C.byref(length))
        status, _, _, error = call_global(lib, L, "len", [buffer_value(made)])
        assert status == ERRHANDLE, status
        # Results beyond results_cap get no handle; a buffer at index cap does not leak.
        before = stats_of(lib, L).memory_used
        for _ in range(50):
            status, n, results, _ = call_global(lib, L, "many", [Value(type=T_INT, u=Value.U(i=20000))], cap=1)
            assert status == OK and n == 20000 and results[0].type == T_BUFFER
            assert lib.xlua_core_unref(L, results[0].u.i) == 1
        lib.xlua_gc(L, 2, 0)
        lib.xlua_gc(L, 2, 0)
        after = stats_of(lib, L).memory_used
        assert after < before + 200000, (before, after)
        # Released handles let Lua collect the buffer.
        expect(lib, L, "kept = nil", OK)
        lib.xlua_gc(L, 2, 0)
        handles = []
        before = stats_of(lib, L).memory_used
        for _ in range(4):
            assert lib.xlua_core_newbuffer(L, 1 << 20, C.byref(ref), C.byref(data)) == OK
            handles.append(ref.value)
        assert stats_of(lib, L).memory_used >= before + (4 << 20), "MemoryUsed counts buffers"
        for h in handles:
            assert lib.xlua_core_unref(L, h) == 1
        lib.xlua_gc(L, 2, 0)
        assert stats_of(lib, L).memory_used < before + 100000
        # The memory limit applies to buffers from C.
        assert lib.xlua_core_newbuffer(L, 16 << 20, C.byref(ref), C.byref(data)) == ERRMEM and ref.value == 0 and not data.value
        assert stats_of(lib, L).memory_used <= (8 << 20) + 16384
        assert lib.xlua_core_newbuffer(L, 0, C.byref(ref), C.byref(data)) == OK and ref.value > 0
        assert lib.xlua_core_tobuffer(L, ref.value, C.byref(length)) and length.value == 0
        assert lib.xlua_core_unref(L, ref.value) == 1
        # A buffer from C under an instruction limit: creating it charges nothing.
        assert lib.xlua_core_set_limits(L, 10, 0, SMALL_SLICES) == 1
        assert lib.xlua_core_newbuffer(L, 4 << 20, C.byref(ref), C.byref(data)) == OK
        assert lib.xlua_core_unref(L, ref.value) == 1
        assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
        assert lib.lua_gettop(L) == 0
    finally:
        lib.xlua_core_close(L)
    print("core_buffer: ok")


def check_core_trusted_bytecode(lib):
    built = trusted_bytecode(lib)
    L, _ = open_state(lib)
    version = run(lib, L, "return _VERSION")[1]
    chunks = TRUSTED[version]
    other = TRUSTED["Lua 5.4" if version == "Lua 5.5" else "Lua 5.5"]["main"]
    try:
        # Mode 0 is text, as XLUA_CORE_MODE_TEXT; a mode with another bit is refused.
        assert run_mode(lib, L, "return 'text'", MODE_TEXT) == (OK, "text")
        assert run_mode(lib, L, "return 'text'", 0) == (OK, "text")
        for mode in (4, -1, MODE_TEXT | 4):
            status, out = run_mode(lib, L, "return 1", mode)
            assert status == ERRCONFIG and out.startswith("invalid mode"), (mode, status, out)
        if not built:
            for mode in (MODE_BINARY, MODE_TEXT | MODE_BINARY):
                for code in (chunks["main"], b"return 1"):
                    assert run_mode(lib, L, code, mode) == \
                        (ERRCONFIG, "binary chunks are not supported in this build (XLUA_TRUSTED_BYTECODE is off)"), mode
            assert lib.lua_gettop(L) == 0
            print("core_trusted_bytecode: refused (XLUA_TRUSTED_BYTECODE is off)")
            return
        # The host's chunk runs in the environment it is given: the default one and a new one.
        env = C.c_int32()
        assert lib.xlua_core_newenv(L, C.byref(env)) == OK
        for handle in (0, env.value):
            for mode in (MODE_BINARY, MODE_TEXT | MODE_BINARY):
                assert run_mode(lib, L, chunks["main"], mode, handle) == (OK, "trusted"), (handle, mode)
        assert run_mode(lib, L, "return 'both'", MODE_TEXT | MODE_BINARY) == (OK, "both")
        assert run_mode(lib, L, chunks["noup"], MODE_BINARY) == (OK, "7")  # no upvalue to take the environment
        assert run_mode(lib, L, chunks["main"], MODE_BINARY, 99999) == (ERRHANDLE, "invalid environment handle")
        assert lib.xlua_core_unref(L, env.value) == 1
        # Each kind keeps to its mode, as lua_load checks it.
        status, out = run_mode(lib, L, chunks["main"], MODE_TEXT)
        assert status == ERRSYNTAX and "attempt to load a binary chunk (mode is 't')" in out, (status, out)
        status, out = run(lib, L, chunks["main"])
        assert status == ERRSYNTAX and "attempt to load a binary chunk (mode is 't')" in out, (status, out)
        status, out = run_mode(lib, L, "return 1", MODE_BINARY)
        assert status == ERRSYNTAX and "attempt to load a text chunk (mode is 'b')" in out, (status, out)
        # A damaged chunk and one of the other version fail in Lua's loader.
        status, out = run_mode(lib, L, chunks["main"][:-8], MODE_BINARY)
        assert status == ERRSYNTAX and "truncated" in out, (status, out)
        status, out = run_mode(lib, L, other, MODE_BINARY)
        assert status == ERRSYNTAX and "version mismatch" in out, (status, out)
        # Scripts still load text only, and string.dump stays out.
        quoted = "".join("\\%d" % b for b in chunks["main"])
        status, out = run(lib, L, 'local f, err = load("%s") assert(f == nil) return err' % quoted)
        assert status == OK and "attempt to load a binary chunk" in out, (status, out)
        assert run(lib, L, "return tostring(string.dump)") == (OK, "nil")
        assert lib.lua_gettop(L) == 0
    finally:
        lib.xlua_core_close(L)
    print("core_trusted_bytecode: ok")


YIELD, ERRSTACK = 1, 21
CALL_RESUME, CALL_CLOSE, CALL_START = 4, 5, 8
CALLF_DEAD = 8
T_THREAD = 9
CO_SUSPENDED, CO_FINISHED, CO_FAILED, CO_RUNNING = 0, 1, 2, 4


def co_call(lib, L, kind, fn=0, name=None, args=(), cap=4):
    """One xlua_core_call of a coroutine kind: (status, call, results, error text or None)."""
    argv = (Value * max(1, len(args)))(*args)
    results = (Value * cap)()
    name = name.encode() if name else b""
    c = Call(kind=kind, fn=fn, name=C.cast(C.c_char_p(name), P), name_len=len(name), args=argv, nargs=len(args),
             results=results, results_cap=cap)
    status = lib.xlua_core_call(L, C.byref(c))
    error = C.string_at(c.error, c.error_len).decode("utf-8", "replace") if c.error else None
    return status, c, results, error


def int_value(i):
    v = Value()
    v.type, v.u.i = T_INT, i
    return v


def check_core_coroutines(lib):
    """Milestone 9: XLUA_CALL_START, _RESUME and _CLOSE (LuaScript.Start and
    LuaCoroutine), coroutines as handles with their status, and the limits per
    step (core_smoke.c has the full list)."""
    L, status = open_state(lib, limit=16 << 20)
    assert L, status
    try:
        expect(lib, L, "function gen(n) for i = 1, n do coroutine.yield(i) end return 'done' end "
                       "function closer() local x <close> = setmetatable({}, {__close = function() closed = true end}) coroutine.yield(1) end "
                       "function spin() coroutine.yield(1) while true do end end "
                       "function mk() return coroutine.create(gen), coroutine.running() end", OK)
        s, c, r, _ = co_call(lib, L, CALL_START, name="gen", args=[int_value(2)])
        assert s == YIELD and c.fn > 3 and r[0].u.i == 1 and not c.flags & CALLF_DEAD, (s, c.fn)
        co = c.fn
        s, c, r, _ = co_call(lib, L, CALL_RESUME, fn=co)
        assert s == YIELD and r[0].u.i == 2, s
        s, c, r, _ = co_call(lib, L, CALL_RESUME, fn=co)
        assert s == OK and c.flags & CALLF_DEAD and value_text(r[0]) == "done", s
        s, c, r, err = co_call(lib, L, CALL_RESUME, fn=co)
        assert s == ERRHANDLE and c.flags & CALLF_DEAD and "cannot resume dead coroutine" in err, (s, err)
        assert lib.xlua_core_unref(L, co) == 1
        s, c, r, err = co_call(lib, L, CALL_START, name="missing")
        assert s == ERRRUN and c.fn == 0 and "global 'missing'" in err, (s, err)
        s, c, r, _ = co_call(lib, L, CALL_START, name="closer")
        assert s == YIELD, s
        co = c.fn
        s, c, r, _ = co_call(lib, L, CALL_CLOSE, fn=co)
        assert s == OK and c.flags & CALLF_DEAD and c.nresults == 0, s
        expect(lib, L, "return closed", OK, "true")
        assert lib.xlua_core_unref(L, co) == 1
        s, n, r, _ = call_global(lib, L, "mk")
        assert s == OK and n == 3 and r[0].type == T_THREAD and r[0].len == CO_SUSPENDED and r[1].type == T_THREAD \
            and r[1].len == CO_RUNNING, (s, r[0].type, r[0].len, r[1].len)
        made, main_thread = r[0].u.i, r[1].u.i
        s, c, r, _ = co_call(lib, L, CALL_RESUME, fn=made, args=[int_value(1)])
        assert s == YIELD and r[0].u.i == 1, s
        s, c, r, err = co_call(lib, L, CALL_RESUME, fn=main_thread)
        assert s == ERRHANDLE and not c.flags & CALLF_DEAD and "non-suspended" in err, (s, err)
        assert lib.xlua_core_unref(L, made) == 1 and lib.xlua_core_unref(L, main_thread) == 1
        # The limits per step.
        assert lib.xlua_core_set_limits(L, 100000, 0, SMALL_SLICES) == 1
        s, c, r, _ = co_call(lib, L, CALL_START, name="spin")
        assert s == YIELD, s
        co = c.fn
        s, c, r, _ = co_call(lib, L, CALL_RESUME, fn=co)
        assert s == LIMIT_INSTR and c.flags & CALLF_DEAD and c.traceback, s
        assert lib.xlua_core_unref(L, co) == 1
        s, c, r, _ = co_call(lib, L, CALL_START, name="gen", args=[int_value(1)])
        assert s == YIELD and 0 < c.instructions < 100000, (s, c.instructions)
        assert lib.xlua_core_unref(L, c.fn) == 1
        assert lib.lua_gettop(L) == 0
    finally:
        lib.xlua_core_close(L)
    print("core_coroutines: ok")


LIMITS_INTERRUPT = 4  # XLUA_LIMITS_INTERRUPT (build/xlua_core.h)


def check_core_stop_latency(lib):
    import stop_latency
    L, status = open_state(lib)
    assert L and status == OK
    try:
        if not lib.xlua_core_set_limits(L, 0, 0, 0):
            print("core_stop_latency: no limits on this runtime")
            return
        n, worst = stop_latency.check(lib, L, lambda code: run(lib, L, code)[0], LIMITS_INTERRUPT)
    finally:
        lib.xlua_core_close(L)
    print("core_stop_latency: %d stops, the latest %.1f ms after its deadline (%s)" % (n, worst[3] * 1000, worst[:3]))


def main(lib, names):
    bind(lib)
    import sys
    sys.path.insert(0, str(HERE))
    for name in names or ["core_sandbox", "core_sandbox_abi", "core_host_oom", "core_lua_api", "core_buffer", "core_trusted_bytecode",
                          "core_stop_latency", "core_coroutines"]:
        globals()["check_" + name](lib)
        assert not CALLBACK_ERRORS, (name, CALLBACK_ERRORS)
    if not hasattr(lib, "luaopen_xlua"):
        print("XLUA_NATIVE_REGRESSION_PASS (core)")
    return 0
