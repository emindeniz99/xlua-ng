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
- core_raw_api: needs the Lua C API exported: the limit exports and the C
  budget on a state without the core allocator, a host function's argument
  that is light userdata or a 2 GB external string (Lua 5.5), a hook that is
  not core's own, and a coroutine resumed without the switch.
- core_sandbox_edges: the edges of xlua_core_sandbox.c and xlua_core_buffer.c
  that the checks above leave untaken: handles out of range, full stacks of a
  coroutine and of its caller, the exports on an exactly full stack, a
  coroutine error whose __tostring fails, the
  edge cases of the handle operations, a name registered twice; with the C API
  exported, userdata that are no buffers and (Lua 5.5) a 2 GB string; with
  xlua_core_limits.c's test points, a stop after call_body's last check.
- core_set_limits: the first xlua_core_set_limits of a state on an exactly
  full stack and with a memory limit that refuses every allocation
  (XLUA_ERRMEM, nothing pushed, the limits unchanged).
"""
import ctypes as C
import os
import threading
import time
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
    stop_latency.retry_once(lambda: check_core_stop_latency_once(lib))


def check_core_stop_latency_once(lib):
    import stop_latency
    L, status = open_state(lib)
    assert L and status == OK
    try:
        if not lib.xlua_core_set_limits(L, 0, 0, 0):
            print("core_stop_latency: no limits on this runtime")
            return
        n, worst, early = stop_latency.check(lib, L, lambda code: run(lib, L, code)[0], LIMITS_INTERRUPT)
        # A stop that the script catches with pcall right before it returns: no Lua
        # instruction follows the tail call, so the stop hook never fired again, and
        # before the fix of section 8.14 the call returned normally (false and the message) although
        # its instruction limit had tripped (Security.md, item 6; section 8.14).
        assert lib.xlua_core_set_limits(L, 100000, 0, 0) == 1
        s = run(lib, L, b"return pcall(string.find, string.rep('a', 100000), '.-b')")
        assert s[0] == LIMIT_INSTR, s
        # Interrupt while XLUA_CALL_COMPILE compiles: before the fix of section 8.14 the compile
        # returned its function and the Interrupt was lost with the call.
        assert lib.xlua_core_set_limits(L, 0, 0, LIMITS_INTERRUPT) == 1
        code = stop_latency.LONG_CHUNK
        c = Call(kind=3, code=C.cast(C.c_char_p(code), P), code_len=len(code), name=C.cast(C.c_char_p(b"c"), P),
                 name_len=1)
        accepted = []

        def interrupt():
            deadline = time.monotonic() + 5
            while not accepted and time.monotonic() < deadline:
                time.sleep(0.005)
                if lib.xlua_core_interrupt(L):
                    accepted.append(time.monotonic())

        t = threading.Thread(target=interrupt)
        t.start()
        s = lib.xlua_core_call(L, C.byref(c))
        t.join()
        assert accepted and s == 12 and c.nresults == 0, (s, c.nresults, accepted)  # XLUA_INTERRUPTED
    finally:
        lib.xlua_core_close(L)
    print("core_stop_latency: %d stops, the latest %.1f ms after its deadline (%s); %d interrupts before a call began"
          % (n, worst[3] * 1000, worst[:3], early))


class Frame(C.Structure):  # xlua_core_frame
    _fields_ = [("L", P), ("args", C.POINTER(Value)), ("results", C.POINTER(Value)), ("error", P)] \
        + [(n, C.c_int32) for n in ("nargs", "results_cap", "nresults", "status", "error_len", "error_arg",
                                    "yieldable", "id")]


DISPATCH = C.CFUNCTYPE(None, P, C.c_int32, C.POINTER(Frame))


def check_core_raw_api(lib):
    """What only a host with the Lua C API (XLUA_EXPORTS=all) can do to a core
    state, for the branches of xlua_core.c and xlua_core_limits.c that LuaScript
    and scripts cannot reach in a core library (Test/README.md, the coverage
    gate): the limit exports and the C budget on a state without the core
    allocator; a host function's argument that is light userdata, or a string
    of 2 GB (an external string, Lua 5.5); a hook that is not core's own (the
    budget and the end of a call), and a coroutine resumed without the switch.
    In a bridge library the bridge reaches the same branches (debug.sethook,
    LuaFunction.Call on a coroutine)."""
    if not hasattr(lib, "luaL_loadbufferx"):
        print("core_raw_api: the Lua C API is not exported (XLUA_EXPORTS=managed), skipped")
        return
    for name, args, res in [("luaL_newstate", [], P), ("lua_close", [P], None),
                            ("luaL_requiref", [P, C.c_char_p, P, C.c_int], None),
                            ("luaL_loadbufferx", [P, C.c_char_p, C.c_size_t, C.c_char_p, C.c_char_p], C.c_int),
                            ("lua_pcallk", [P, C.c_int, C.c_int, C.c_int, P, P], C.c_int),
                            ("lua_settop", [P, C.c_int], None), ("lua_gettop", [P], C.c_int),
                            ("lua_rawgeti", [P, C.c_int, C.c_longlong], C.c_int),
                            ("lua_pushlightuserdata", [P, P], None), ("lua_sethook", [P, P, C.c_int, C.c_int], None),
                            ("lua_newthread", [P], P), ("lua_resume", [P, P, C.c_int, C.POINTER(C.c_int)], C.c_int),
                            ("lua_tolstring", [P, C.c_int, P], C.c_char_p),
                            ("xlua_core_set_dispatcher", [P], None),
                            ("xlua_core_register", [P, C.c_int32, C.c_char_p, C.c_int32, C.c_int32, C.POINTER(C.c_int32)],
                             C.c_int)]:
        fn = getattr(lib, name)
        fn.argtypes = args
        fn.restype = res

    # A state without the core allocator: the limit exports refuse it, and the
    # C budget of the string library does nothing there.
    plain = lib.luaL_newstate()
    try:
        assert lib.xlua_core_set_limits(plain, 1000, 0, 0) == 0
        assert lib.xlua_core_interrupt(plain) == 0 and lib.xlua_core_watch(plain) == -1
        lib.luaL_requiref(plain, b"string", C.cast(lib.luaopen_string, P), 1)
        lib.lua_settop(plain, 0)
        code = b"return string.find(string.rep('a', 5000), '.-b')"
        assert lib.luaL_loadbufferx(plain, code, len(code), b"=plain", b"t") == OK
        assert lib.lua_pcallk(plain, 0, 0, 0, None, None) == OK, lib.lua_tolstring(plain, -1, None)
    finally:
        lib.lua_close(plain)

    L, status = open_state(lib, libs=SAFE)
    assert L, status
    version = run(lib, L, "return _VERSION")[1]
    registry = REGISTRY[version]
    seen = {}

    def dispatch(host, fn_id, frame):
        try:
            f = frame.contents
            if fn_id == 1:  # the first argument, as the trampoline passed it
                seen["arg"] = (f.args[0].type, f.args[0].len)
            elif fn_id == 2:  # a hook of the host's own: none
                lib.lua_sethook(f.L, None, 0, 0)
            elif fn_id == 3:  # a coroutine resumed through lua_resume, not coroutine.resume
                top = lib.lua_gettop(f.L)
                co = lib.lua_newthread(f.L)  # with the count hook of the running thread
                chunk = b"local n = 0 for i = 1, 10000 do n = n + 1 end return n"
                assert lib.luaL_loadbufferx(co, chunk, len(chunk), b"=co", b"t") == OK
                nres = C.c_int()
                seen["resume"] = lib.lua_resume(co, f.L, 0, C.byref(nres))
                lib.lua_settop(f.L, top)
        except Exception as e:  # noqa: BLE001 (reported by main)
            CALLBACK_ERRORS.append(repr(e))

    dispatcher = DISPATCH(dispatch)
    lib.xlua_core_set_dispatcher(C.cast(dispatcher, P))
    try:
        old = C.c_int32()
        for name, fn_id in [(b"arg", 1), (b"unhook", 2), (b"rawresume", 3)]:
            assert lib.xlua_core_register(L, 0, name, len(name), fn_id, C.byref(old)) == OK
        # The registered function as a handle, to call it with what a script cannot pass.
        result = Value()
        code = b"return arg"
        c = Call(kind=CALL_CHUNK, code=C.cast(C.c_char_p(code), P), code_len=len(code), name=C.cast(C.c_char_p(b"t"), P),
                 name_len=1, results=C.pointer(result), results_cap=1)
        assert lib.xlua_core_call(L, C.byref(c)) == OK and result.type == 6, result.type
        ref = result.u.i

        def call_arg(push):
            top = lib.lua_gettop(L)
            assert lib.lua_rawgeti(L, registry, ref) == 6  # LUA_TFUNCTION
            push()
            assert lib.lua_pcallk(L, 1, 0, 0, None, None) == OK, lib.lua_tolstring(L, -1, None)
            lib.lua_settop(L, top)

        call_arg(lambda: lib.lua_pushlightuserdata(L, 16))
        assert seen.pop("arg") == (T_OTHER, 0)
        if version == "Lua 5.5":
            # 2 GB of address space that is never touched but for its last byte, which
            # an external string needs to be 0: the trampoline passes its first 2 GB - 1.
            import mmap
            size = 1 << 31
            flags = mmap.MAP_PRIVATE | getattr(mmap, "MAP_ANONYMOUS", 0x20) | getattr(mmap, "MAP_NORESERVE", 0)
            region = mmap.mmap(-1, size + 1, flags=flags)
            try:
                lib.lua_pushexternalstring.argtypes = [P, P, C.c_size_t, P, P]
                lib.lua_pushexternalstring.restype = C.c_char_p
                start = C.c_char.from_buffer(region)
                call_arg(lambda: lib.lua_pushexternalstring(L, C.addressof(start), size, None, None))
                assert seen.pop("arg") == (T_STR, 2 ** 31 - 1)
                lib.xlua_gc(L, 2, 0)  # LUA_GCCOLLECT: the string goes before its memory
                del start
            finally:
                region.close()
        assert lib.xlua_core_unref(L, ref) == 1

        # A host that removes the hook: the end of the call gives no slice back,
        # and the C budget, left without a slice to borrow from, stops the call.
        assert lib.xlua_core_set_limits(L, 1000000, 0, SMALL_SLICES) == 1
        assert run(lib, L, "unhook() return 1") == (OK, "1")
        assert lib.xlua_core_set_limits(L, 3000, 0, SMALL_SLICES) == 1
        s = run(lib, L, "unhook() return string.find(string.rep('a', 3000), '.-b')")
        assert s[0] == LIMIT_INSTR, s
        # A coroutine that inherited the running thread's count hook, resumed
        # without the switch: its hook finds it is not the running thread and
        # disarms it, and it runs to its end.
        assert lib.xlua_core_set_limits(L, 1000000, 0, SMALL_SLICES) == 1
        assert run(lib, L, "rawresume() return 2") == (OK, "2")
        assert seen.pop("resume") == OK
        assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
    finally:
        lib.xlua_core_set_dispatcher(None)
        lib.xlua_core_close(L)
    del dispatcher
    print("core_raw_api: ok")


ERRTYPE, INTERRUPTED = 20, 12
H_GET, H_SET, H_NEXT, H_INEXT, H_NEWTABLE, H_REF, H_KEY, H_EQUAL = 0, 1, 3, 4, 5, 7, 8, 9
T_TABLE, T_FUNC = 5, 6
CALLF_ERROR_TABLE, CALLF_ERROR_FUNC = 2, 4
TP_END = 2  # xlua_core_limits.c's test point in xlua_core_end (XLUA_LIMITS_TEST_POINTS)
LUAI_MAXSTACK = 1000000  # luaconf.h of Lua 5.4 and 5.5


def handle_value(type_, handle, length=0):
    v = Value()
    v.type, v.len, v.u.i = type_, length, handle
    return v


def handle_op(lib, L, op, *values):
    """xlua_core_handle with v[0], v[1], ... (None leaves one zeroed): (status, v)."""
    v = (Value * 4)()
    for i, x in enumerate(values):
        if x is not None:
            v[i] = x
    return lib.xlua_core_handle(L, op, v), v


def check_core_sandbox_edges(lib):
    """The edges of xlua_core_sandbox.c and xlua_core_buffer.c that the other
    checks leave untaken (Test/README.md, the coverage gate): handles out of
    range (INT32_MIN, beyond 32 bits, a negative handle of a slot that is no
    iterator), a function handle as an argument, more arguments than the
    stack has slots, a chunk without a name, error(function), a coroutine
    whose resume finds its own stack or the caller's full, a coroutine error
    or a plain call's error whose __tostring raises (protected: the plain call's
    text, the error object as the error handle, a memory error and a stop
    kept), the edge cases of the
    handle operations (an iterator at math.maxinteger, a huge iterator handle or array size,
    REF of every kind of value, a key handle as a key, EQUAL of a free slot),
    xlua_core_call, _handle, _register, _set_require and _unref on an exactly
    full stack (none may push before it checks), a dotted name registered twice, and a
    state without the sandbox. With the Lua C API exported (XLUA_EXPORTS=all)
    also full userdata that are no buffers and, on Lua 5.5, a 2 GB external
    string; with xlua_core_limits.c's test points (-DXLUA_LIMITS_TEST_POINTS),
    a stop between call_body's check and the end of the call, which otherwise
    only another thread can make."""
    for name, args, res in [("xlua_core_set_dispatcher", [P], None),
                            ("xlua_core_register", [P, C.c_int32, C.c_char_p, C.c_int32, C.c_int32, C.POINTER(C.c_int32)],
                             C.c_int)]:
        fn = getattr(lib, name)
        fn.argtypes = args
        fn.restype = res
    raw = hasattr(lib, "luaL_loadbufferx")
    with_buffer = buffer_built(lib)
    L, status = open_state(lib, libs=SAFE | (LIBS["buffer"] if with_buffer else 0))
    assert L, status
    version = run(lib, L, "return _VERSION")[1]
    seen = {}

    def dispatch(host, fn_id, frame):
        try:
            f = frame.contents
            seen.setdefault("calls", []).append(fn_id)
            if fn_id == 1:  # the caller's stack is nearly full: resume a coroutine that yields 500000 values
                s, c, _, err = co_call(lib, L, CALL_RESUME, fn=seen["co"])
                seen["hold"] = (s, c.flags, err)
            elif fn_id == 2:  # a full stack: xlua_core_handle cannot have its 8 slots
                top = lib.lua_gettop(f.L)
                # A request over the limit leaves the stack at its largest size (Lua's
                # error size), which no later request grows: the search below finds
                # exactly the slots that are left.
                assert lib.lua_checkstack(f.L, LUAI_MAXSTACK) == 0
                lo, hi = 0, 2 * LUAI_MAXSTACK
                while lo < hi:
                    mid = (lo + hi + 1) // 2
                    if lib.lua_checkstack(f.L, mid):
                        lo = mid
                    else:
                        hi = mid - 1
                assert lib.lua_checkstack(f.L, lo) == 1 and lib.lua_checkstack(f.L, lo + 1) == 0
                # Exactly full: one more push trips Lua's API check (XLUA_APICHECK), so every
                # export must find out that the stack has no room before it pushes anything.
                # Until 2026-10-28 the exports' check that the state is a sandbox pushed one
                # value first. unref cannot release a handle without a slot: it keeps it.
                lib.lua_settop(f.L, top + lo)
                call = call_global(lib, L, "id")
                seen["full"] = (handle_op(lib, L, H_NEWTABLE)[0], call[0], call[3],
                                lib.xlua_core_register(L, 0, b"edges.g", 7, 5, C.byref(C.c_int32())),
                                lib.xlua_core_set_require(L, 1), lib.xlua_core_unref(L, seen["table"]),
                                lib.lua_gettop(f.L) == top + lo)
                lib.lua_settop(f.L, top)
        except Exception as e:  # noqa: BLE001 (reported by main)
            CALLBACK_ERRORS.append(repr(e))

    if raw:
        lib.lua_settop.argtypes = [P, C.c_int]
        lib.lua_settop.restype = None
    dispatcher = DISPATCH(dispatch)
    lib.xlua_core_set_dispatcher(C.cast(dispatcher, P))
    try:
        expect(lib, L, "function id(...) return ... end\n"
                       "function holder()\n"
                       "  local function f(...) coroutine.yield() return 'held' end\n"
                       "  return f(table.unpack({}, 1, 600000))\n"
                       "end\n"
                       "function gen_big() coroutine.yield() coroutine.yield(table.unpack({}, 1, 500000)) return 'end' end\n"
                       "function deep(...) hold() return 'deep' end\n"
                       "function run_deep() return deep(table.unpack({}, 1, 600000)) end\n"
                       "function bad_tostring() error(setmetatable({code = 9}, {__tostring = function() error({}) end})) end\n"
                       "function bad_tostring_str() error(setmetatable({code = 9}, {__tostring = function() error('again') end})) end\n"
                       "function bad_tostring_later() coroutine.yield() "
                       "error(setmetatable({}, {__tostring = function() error('again') end})) end\n"
                       "function bad_tostring_mem() error(setmetatable({}, {__tostring = function() "
                       "local t = {} while true do t[#t + 1] = {} end end})) end\n"
                       "function bad_tostring_spin() error(setmetatable({}, {__tostring = function() while true do end end})) end\n"
                       "function make() return {}, function() return 'f' end, coroutine.create(id) end", OK)
        s, n, r, _ = call_global(lib, L, "make")
        assert s == OK and n == 3 and (r[0].type, r[1].type, r[2].type) == (T_TABLE, T_FUNC, T_THREAD), s
        table, func, thread = r[0].u.i, r[1].u.i, r[2].u.i
        seen["table"] = table

        # Handles as arguments: a function handle (case XLUA_T_FUNC of xlua_core_push_value),
        # handles beyond 32 bits, INT32_MIN, and a negative handle whose slot is no iterator.
        s, n, r, _ = call_global(lib, L, "id", [handle_value(T_FUNC, func)])
        assert s == OK and n == 1 and r[0].type == T_FUNC and r[0].u.i != func, s
        assert lib.xlua_core_unref(L, r[0].u.i) == 1
        for bad in (-(1 << 40), 1 << 40, -(2 ** 31), -2 * func, -2 * func - 1):
            s, _, _, err = call_global(lib, L, "id", [handle_value(T_TABLE, bad)])
            assert s == ERRHANDLE and err == "argument 1 is not a valid handle (type 5)", (bad, s, err)

        # More arguments than INT32_MAX - 3: refused before the stack is asked.
        one = (Value * 1)()
        c = Call(kind=0, name=C.cast(C.c_char_p(b"id"), P), name_len=2, args=one, nargs=2 ** 31 - 3)
        assert lib.xlua_core_call(L, C.byref(c)) == ERRSTACK and C.string_at(c.error, c.error_len) == b"too many arguments"

        # A chunk without a name: "@" alone.
        code = b"error('unnamed')"
        c = Call(kind=CALL_CHUNK, code=C.cast(C.c_char_p(code), P), code_len=len(code), name=None, name_len=5)
        s = lib.xlua_core_call(L, C.byref(c))
        assert s == ERRRUN and C.string_at(c.error, c.error_len) == b":1: unnamed", (s, C.string_at(c.error, c.error_len))

        # error(function): ErrorValue is the function, as a handle.
        code = b"error(function() end)"
        c = Call(kind=CALL_CHUNK, code=C.cast(C.c_char_p(code), P), code_len=len(code), name=C.cast(C.c_char_p(b"t"), P),
                 name_len=1)
        assert lib.xlua_core_call(L, C.byref(c)) == ERRRUN and c.flags & CALLF_ERROR_FUNC and c.error_ref > 0
        assert C.string_at(c.error, c.error_len) == b"(error object is a function value)"
        assert lib.xlua_core_unref(L, c.error_ref) == 1

        # A suspended coroutine holding 600000 values cannot take 500000 more arguments; it
        # stays suspended and runs on.
        s, c, _, _ = co_call(lib, L, CALL_START, name="holder")
        assert s == YIELD, s
        co = c.fn
        many = (Value * 500000)()  # nils
        c = Call(kind=CALL_RESUME, fn=co, args=many, nargs=len(many))
        s = lib.xlua_core_call(L, C.byref(c))
        assert s == ERRSTACK and C.string_at(c.error, c.error_len) == b"too many arguments to resume" \
            and not c.flags & CALLF_DEAD, s
        s, c, r, _ = co_call(lib, L, CALL_RESUME, fn=co)
        assert s == OK and c.flags & CALLF_DEAD and value_text(r[0]) == "held", s
        assert lib.xlua_core_unref(L, co) == 1
        # A host function whose thread holds 600000 values cannot take the 500000 a coroutine yields.
        assert lib.xlua_core_register(L, 0, b"hold", 4, 1, C.byref(C.c_int32())) == OK
        s, c, _, _ = co_call(lib, L, CALL_START, name="gen_big")
        assert s == YIELD, s
        seen["co"] = c.fn
        s, n, r, _ = call_global(lib, L, "run_deep")
        assert s == OK and value_text(r[0]) == "deep", s
        hold = seen.pop("hold")
        assert hold[0] == ERRSTACK and hold[2] == "too many results to resume" and not hold[1] & CALLF_DEAD, hold
        s, c, r, _ = co_call(lib, L, CALL_RESUME, fn=seen["co"])
        assert s == OK and value_text(r[0]) == "end", s
        assert lib.xlua_core_unref(L, seen.pop("co")) == 1

        # A coroutine's error whose __tostring raises: the description runs protected, so
        # the call reports the plain call's text, "(error object is a table value)", with
        # the coroutine's traceback, and the error object itself as the error handle
        # (ErrorValue). Until 2026-10-28 the call failed without a message or traceback.
        s, c, _, err = co_call(lib, L, CALL_START, name="bad_tostring")
        assert s == ERRRUN and err == "(error object is a table value)" and c.flags & CALLF_DEAD, (s, err)
        assert c.traceback and b"in function <" in C.string_at(c.traceback, c.traceback_len), s
        assert c.flags & CALLF_ERROR_TABLE and c.error_ref > 0, c.flags
        key = b"code"
        s2, v = handle_op(lib, L, H_GET, handle_value(T_TABLE, c.error_ref),
                          Value(type=T_STR, len=len(key), u=Value.U(s=C.cast(C.c_char_p(key), P))))
        assert s2 == OK and v[2].type == T_INT and v[2].u.i == 9, "the error handle is the object error() raised"
        assert lib.xlua_core_unref(L, c.error_ref) == 1 and lib.xlua_core_unref(L, c.fn) == 1
        # A plain call the same (owner decision of 2026-10-28, Q54): the error object itself
        # stays the error handle, with the same text and the call's traceback. Until then the
        # message handler ran again for what __tostring raised, which became the error
        # handle (a table) or left none (a string, whose message was "again").
        for name in ("bad_tostring", "bad_tostring_str"):
            s, c, _, err = co_call(lib, L, 0, name=name)
            assert s == ERRRUN and err == "(error object is a table value)", (name, s, err)
            assert c.traceback and b"in function <" in C.string_at(c.traceback, c.traceback_len), name
            assert c.flags & CALLF_ERROR_TABLE and c.error_ref > 0, (name, c.flags)
            s2, v = handle_op(lib, L, H_GET, handle_value(T_TABLE, c.error_ref),
                              Value(type=T_STR, len=len(key), u=Value.U(s=C.cast(C.c_char_p(key), P))))
            assert s2 == OK and v[2].type == T_INT and v[2].u.i == 9, (name, "the error handle is the object error() raised")
            assert lib.xlua_core_unref(L, c.error_ref) == 1
        code = b"error(setmetatable({}, {__tostring = function() error({}) end}))"
        c = Call(kind=CALL_CHUNK, code=C.cast(C.c_char_p(code), P), code_len=len(code), name=C.cast(C.c_char_p(b"t"), P),
                 name_len=1)
        assert lib.xlua_core_call(L, C.byref(c)) == ERRRUN \
            and C.string_at(c.error, c.error_len) == b"(error object is a table value)", "the plain call's text"
        assert c.flags & CALLF_ERROR_TABLE and lib.xlua_core_unref(L, c.error_ref) == 1, c.flags
        # The same from RESUME, with a string raised in __tostring.
        s, c, _, _ = co_call(lib, L, CALL_START, name="bad_tostring_later")
        assert s == YIELD, s
        co = c.fn
        s, c, _, err = co_call(lib, L, CALL_RESUME, fn=co)
        assert s == ERRRUN and err == "(error object is a table value)" and c.traceback and c.flags & CALLF_ERROR_TABLE, \
            (s, err)
        assert lib.xlua_core_unref(L, c.error_ref) == 1 and lib.xlua_core_unref(L, co) == 1
        # A memory error or a stop inside __tostring is the call's, as in a plain call.
        lib.xlua_core_set_memory_limit(L, stats_of(lib, L).memory_used + (1 << 20))
        s, c, _, err = co_call(lib, L, CALL_START, name="bad_tostring_mem")
        lib.xlua_core_set_memory_limit(L, 0)
        assert s == ERRMEM and c.flags & CALLF_DEAD and not c.error_ref, (s, err)
        assert lib.xlua_core_unref(L, c.fn) == 1
        assert lib.xlua_core_set_limits(L, 100000, 0, 0) == 1
        s, c, _, err = co_call(lib, L, CALL_START, name="bad_tostring_spin")
        assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
        assert s == LIMIT_INSTR and err == "instruction limit exceeded" and not c.error_ref, (s, err)
        assert lib.xlua_core_unref(L, c.fn) == 1
        # And in a plain call: the memory error, and the stop's message, without an error handle.
        lib.xlua_core_set_memory_limit(L, stats_of(lib, L).memory_used + (1 << 20))
        s, c, _, err = co_call(lib, L, 0, name="bad_tostring_mem")
        lib.xlua_core_set_memory_limit(L, 0)
        assert s == ERRMEM and not c.error_ref, (s, err)
        assert lib.xlua_core_set_limits(L, 100000, 0, 0) == 1
        s, c, _, err = co_call(lib, L, 0, name="bad_tostring_spin")
        assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
        assert s == LIMIT_INSTR and err == "instruction limit exceeded" and not c.error_ref, (s, err)

        # Handle operations. An iterator is a table handle too: its key slot set to
        # math.maxinteger ends an INEXT traversal instead of overflowing.
        assert handle_op(lib, L, H_SET, handle_value(T_TABLE, table), int_value(1), int_value(10))[0] == OK
        s, v = handle_op(lib, L, H_INEXT, handle_value(T_TABLE, table), None, None, handle_value(0, 0, 1))
        assert s == OK and v[1].type == T_INT and v[1].u.i == 1 and v[3].u.i > 0, s
        it = v[3].u.i
        assert handle_op(lib, L, H_SET, handle_value(T_TABLE, it), int_value(1), int_value(2 ** 63 - 1))[0] == OK
        s, v = handle_op(lib, L, H_INEXT, handle_value(T_TABLE, table), None, None, handle_value(0, it))
        assert s == OK and v[1].type == T_NIL, s
        assert lib.xlua_core_unref(L, it) == 1
        # An iterator handle above INT32_MAX / 2 is none.
        s, v = handle_op(lib, L, H_NEXT, handle_value(T_TABLE, table), None, None, handle_value(0, 2 ** 30))
        assert s == ERRHANDLE, s
        # An array size of INT32_MAX or more is no size hint.
        for size in (2 ** 31 - 1, 1 << 40):
            s, v = handle_op(lib, L, H_NEWTABLE, None, int_value(size))
            assert s == OK and v[0].type == T_TABLE and lib.xlua_core_unref(L, v[0].u.i) == 1, (size, s)
        # REF of a table, a function and a coroutine: new handles; of a key's string: none.
        for h, kind in ((table, T_TABLE), (func, T_FUNC), (thread, T_THREAD)):
            s, v = handle_op(lib, L, H_REF, handle_value(0, h))
            assert s == OK and v[2].type == kind and v[2].u.i not in (0, h), (kind, s)
            assert lib.xlua_core_unref(L, v[2].u.i) == 1
        key = b"key"
        s, v = handle_op(lib, L, H_KEY, None, Value(type=T_STR, len=len(key), u=Value.U(s=C.cast(C.c_char_p(key), P))))
        assert s == OK and v[2].len == -1, s
        key_value = v[2]
        assert handle_op(lib, L, H_REF, handle_value(0, key_value.u.i))[0] == ERRHANDLE
        # A key handle is no string to make a key of.
        assert handle_op(lib, L, H_KEY, None, key_value)[0] == ERRTYPE
        # EQUAL with a free slot: false.
        s, v = handle_op(lib, L, H_EQUAL, handle_value(0, 999999), handle_value(0, table))
        assert s == OK and v[2].type == T_BOOL and v[2].u.i == 0, s
        s, v = handle_op(lib, L, H_EQUAL, handle_value(0, table), handle_value(0, table))
        assert s == OK and v[2].u.i == 1, s
        if with_buffer:
            s, v = handle_op(lib, L, H_NEWBUFFER, None, int_value(4))
            assert s == OK, s
            buf = v[0].u.i
            s, v = handle_op(lib, L, H_REF, handle_value(0, buf))
            assert s == OK and v[2].type == T_BUFFER and v[2].len == 4, s
            assert lib.xlua_core_unref(L, v[2].u.i) == 1 and lib.xlua_core_unref(L, buf) == 1

        # A name of a library registered twice: the copy in each environment that still has the
        # first function gets the second.
        old = C.c_int32()
        env = C.c_int32()
        assert lib.xlua_core_register(L, 0, b"edges.f", 7, 3, C.byref(old)) == OK and old.value == 0
        assert lib.xlua_core_newenv(L, C.byref(env)) == OK
        assert lib.xlua_core_register(L, 0, b"edges.f", 7, 4, C.byref(old)) == OK and old.value == 3
        seen["calls"] = []
        for handle in (0, env.value):
            assert run(lib, L, "edges.f()", handle) == (OK, "nil")
        assert seen["calls"] == [4, 4], seen["calls"]
        assert lib.xlua_core_unref(L, env.value) == 1

        if raw:
            # The exports from a host function whose thread's stack is exactly full.
            assert lib.xlua_core_register(L, 0, b"fill", 4, 2, C.byref(old)) == OK
            assert run(lib, L, "fill()") == (OK, "nil")
            assert seen.pop("full") == (ERRMEM, ERRSTACK, "stack overflow", ERRMEM, ERRMEM, 0, True)
            assert run(lib, L, "return edges and edges.g") == (OK, "nil"), "the refused registration left nothing"
            check_core_foreign_userdata(lib, L, version, with_buffer)
        for h in (table, func, thread):
            assert lib.xlua_core_unref(L, h) == 1

        # A state without the sandbox.
        if hasattr(lib, "xlua_core_newstate"):
            lib.xlua_core_newstate.argtypes = [C.POINTER(Config), C.POINTER(C.c_int32)]
            lib.xlua_core_newstate.restype = P
            plain = lib.xlua_core_newstate(C.byref(Config(0, 0, 0, 0, 0)), C.byref(C.c_int32()))
            assert plain
            assert lib.xlua_core_register(plain, 0, b"f", 1, 1, C.byref(old)) == ERRCONFIG
            assert lib.xlua_core_unref(plain, 100) == 0
            lib.xlua_core_close(plain)

        if hasattr(lib, "xlua_core_set_test_point") and with_buffer:
            check_core_stop_window(lib, L)
        else:
            print("core_sandbox_edges: no test points (XLUA_LIMITS_TEST_POINTS) or no buffer library: "
                  "the stop after call_body's check is not reached")
        assert lib.lua_gettop(L) == 0
    finally:
        lib.xlua_core_set_dispatcher(None)
        lib.xlua_core_close(L)
    del dispatcher
    print("core_sandbox_edges: ok")


def check_core_foreign_userdata(lib, L, version, with_buffer):
    """Full userdata that are no buffers, which only the C API can make in a
    sandbox: one without a metatable and one with another metatable, as
    globals, as results and as handles; on Lua 5.5 a 2 GB external string
    (an untouched anonymous mmap) as a result and for buffer.fromstring."""
    registry = REGISTRY[version]
    for name, args, res in [("lua_newuserdatauv", [P, C.c_size_t, C.c_int], P),
                            ("lua_createtable", [P, C.c_int, C.c_int], None),
                            ("lua_setmetatable", [P, C.c_int], C.c_int), ("lua_setglobal", [P, C.c_char_p], None),
                            ("lua_pushnil", [P], None), ("lua_pushvalue", [P, C.c_int], None),
                            ("luaL_ref", [P, C.c_int], C.c_int)]:
        fn = getattr(lib, name)
        fn.argtypes = args
        fn.restype = res
    refs = []
    for name, meta in ((b"plainud", False), (b"metaud", True)):
        lib.lua_newuserdatauv(L, 8, 0)
        if meta:
            lib.lua_createtable(L, 0, 0)
            lib.lua_setmetatable(L, -2)
        lib.lua_pushvalue(L, -1)
        refs.append(lib.luaL_ref(L, registry))
        lib.lua_setglobal(L, name)
    results = (Value * 2)()
    code = b"return plainud, metaud"
    c = Call(kind=CALL_CHUNK, code=C.cast(C.c_char_p(code), P), code_len=len(code), name=C.cast(C.c_char_p(b"t"), P),
             name_len=1, results=results, results_cap=2)
    assert lib.xlua_core_call(L, C.byref(c)) == OK and c.nresults == 2 and results[0].type == results[1].type == T_OTHER
    for ref in refs:
        assert handle_op(lib, L, H_BUFFER, handle_value(0, ref))[0] == (ERRHANDLE if with_buffer else ERRCONFIG)
        assert handle_op(lib, L, H_REF, handle_value(0, ref))[0] == ERRHANDLE
        assert lib.xlua_core_unref(L, ref) == 1
    if with_buffer:
        expect(lib, L, "for _, u in ipairs({plainud, metaud}) do\n"
                       "  assert(not buffer.isbuffer(u))\n"
                       "  local ok, e = pcall(buffer.len, u)\n"
                       "  assert(not ok and e:find('buffer expected, got userdata', 1, true), e)\n"
                       "end\n"
                       "return 'ok'", OK, "ok")
    for name in (b"plainud", b"metaud"):
        lib.lua_pushnil(L)
        lib.lua_setglobal(L, name)
    if version != "Lua 5.5":
        return
    import mmap
    size = 1 << 31
    flags = mmap.MAP_PRIVATE | getattr(mmap, "MAP_ANONYMOUS", 0x20) | getattr(mmap, "MAP_NORESERVE", 0)
    region = mmap.mmap(-1, size + 1, flags=flags)
    try:
        lib.lua_pushexternalstring.argtypes = [P, P, C.c_size_t, P, P]
        lib.lua_pushexternalstring.restype = C.c_char_p
        start = C.c_char.from_buffer(region)
        lib.lua_pushexternalstring(L, C.addressof(start), size, None, None)
        lib.lua_setglobal(L, b"huge")
        expect(lib, L, "return huge", ERRRUN, "a string of more than 2 GB cannot be passed to C#")
        if with_buffer:
            expect(lib, L, "local ok, e = pcall(buffer.fromstring, huge) assert(not ok) return e", OK,
                   "string too long for a buffer (more than 1073741824 bytes)")
        lib.lua_pushnil(L)
        lib.lua_setglobal(L, b"huge")
        lib.xlua_gc(L, 2, 0)  # LUA_GCCOLLECT: the string goes before its memory
        del start
    finally:
        region.close()


def check_core_stop_window(lib, L):
    """A stop that comes after call_body's last check, before xlua_core_end
    (core-luascript.md, section 8.14): in a real build only another thread's
    Interrupt or watchdog makes it; the test point in xlua_core_end makes it
    on this thread. The results' handles are released, and the call fails."""
    lib.xlua_core_set_test_point.argtypes = [P]
    lib.xlua_core_set_test_point.restype = None
    armed = []

    def point(p):
        if p == TP_END and armed:
            armed.pop()
            assert lib.xlua_core_interrupt(L) == 1

    callback = C.CFUNCTYPE(None, C.c_int)(point)
    lib.xlua_core_set_test_point(C.cast(callback, P))
    try:
        assert lib.xlua_core_set_limits(L, 0, 0, LIMITS_INTERRUPT) == 1
        expect(lib, L, "function kinds() return {}, function() end, buffer.create(1), coroutine.create(id), 1 end", OK)
        for cap in (8, 2):  # the loop ends at nresults, and at results_cap
            armed.append(1)
            s, n, r, err = call_global(lib, L, "kinds", cap=cap)
            assert s == INTERRUPTED and n == 0 and err and not armed, (cap, s, err)
            kinds = [r[i].type for i in range(min(cap, 5))]
            assert kinds == [T_TABLE, T_FUNC, T_BUFFER, T_THREAD, T_INT][:len(kinds)], kinds
            for i in range(len(kinds) - (cap > 4)):
                assert lib.xlua_core_unref(L, r[i].u.i) == 0, "released by xlua_core_call"
        assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1
    finally:
        lib.xlua_core_set_test_point(None)
    print("core_sandbox_edges: the stop after call_body's check (test points)")


def check_set_limits_first_call(lib, new_state, close):
    """The first xlua_core_set_limits of a state creates the stop messages, and
    until 2026-10-28 pushed them with no stack check and outside a protected
    call: on a C host's exactly full stack Lua's API check aborted an
    XLUA_APICHECK library, and a refused allocation (a memory limit, also
    before any call) raised with no handler, a panic. Both now return
    XLUA_ERRMEM, push nothing and leave the limits as they were; a later call
    creates the messages. new_state() returns a fresh core state, which close
    frees. native_regression.py runs this on xlua_core_newstate's states."""
    for name, args, res in [("xlua_core_set_limits", [P, C.c_int64, C.c_int64, C.c_int32], C.c_int),
                            ("xlua_core_set_memory_limit", [P, C.c_int64], C.c_int),
                            ("xlua_core_get_stats", [P, C.POINTER(Stats)], C.c_int),
                            ("xlua_gc", [P, C.c_int, C.c_int], C.c_int),
                            ("lua_checkstack", [P, C.c_int], C.c_int), ("lua_gettop", [P], C.c_int),
                            ("lua_settop", [P, C.c_int], None)]:
        fn = getattr(lib, name)
        fn.argtypes = args
        fn.restype = res

    def stats(L):
        s = Stats()
        assert lib.xlua_core_get_stats(L, C.byref(s)) == 1
        return s

    # An exactly full stack: one more push trips the API check.
    L = new_state()
    try:
        top = lib.lua_gettop(L)
        # A request over the limit leaves the stack at its largest size; the search
        # then finds exactly the slots that are left (check_core_sandbox_edges).
        assert lib.lua_checkstack(L, LUAI_MAXSTACK) == 0
        lo, hi = 0, 2 * LUAI_MAXSTACK
        while lo < hi:
            mid = (lo + hi + 1) // 2
            if lib.lua_checkstack(L, mid):
                lo = mid
            else:
                hi = mid - 1
        lib.lua_settop(L, top + lo)
        assert lib.xlua_core_set_limits(L, 1000, 0, 0) == ERRMEM
        assert lib.lua_gettop(L) == top + lo
        lib.lua_settop(L, top)
        assert stats(L).instruction_limit == 0, "the refused call left the limits as they were"
        assert lib.xlua_core_set_limits(L, 1000, 0, 0) == 1 and stats(L).instruction_limit == 1000
        # Once the messages exist the call pushes nothing: a full stack is fine.
        lib.lua_settop(L, top + lo)
        assert lib.xlua_core_set_limits(L, 2000, 0, 0) == 1 and stats(L).instruction_limit == 2000
        lib.lua_settop(L, top)
    finally:
        close(L)

    # A refused allocation, outside any call: a limit of one byte refuses every
    # growth, also after Lua's emergency collection.
    L = new_state()
    try:
        before = stats(L).refusals
        assert lib.xlua_core_set_memory_limit(L, 1) == 1
        assert lib.xlua_core_set_limits(L, 1000, 0, 0) == ERRMEM
        s = stats(L)
        assert s.refusals > before and s.instruction_limit == 0, (s.refusals, s.instruction_limit)
        assert lib.lua_gettop(L) == 0
        assert lib.xlua_core_set_limits(L, 1000, 0, 0) == ERRMEM, "still no room"
        assert lib.xlua_core_set_memory_limit(L, 0) == 1
        assert lib.xlua_core_set_limits(L, 1000, 0, 0) == 1 and stats(L).instruction_limit == 1000
        # Once the messages exist the call allocates nothing.
        assert lib.xlua_core_set_memory_limit(L, 1) == 1
        assert lib.xlua_core_set_limits(L, 0, 0, 0) == 1 and stats(L).instruction_limit == 0
        assert lib.xlua_core_set_memory_limit(L, 0) == 1
    finally:
        close(L)


def check_core_set_limits(lib):
    """check_set_limits_first_call on xlua_core_open's states."""
    probe, status = open_state(lib, libs=SAFE)
    assert probe, status
    supported = lib.xlua_core_set_limits(probe, 0, 0, 0)
    lib.xlua_core_close(probe)
    if not supported:
        print("core_set_limits: no limits on this runtime")
        return

    def new_state():
        L, status = open_state(lib, libs=SAFE)
        assert L, status
        return L

    check_set_limits_first_call(lib, new_state, lib.xlua_core_close)


def main(lib, names):
    bind(lib)
    import sys
    sys.path.insert(0, str(HERE))
    for name in names or ["core_sandbox", "core_sandbox_abi", "core_host_oom", "core_lua_api", "core_buffer", "core_trusted_bytecode",
                          "core_stop_latency", "core_coroutines", "core_raw_api", "core_sandbox_edges", "core_set_limits"]:
        globals()["check_" + name](lib)
        assert not CALLBACK_ERRORS, (name, CALLBACK_ERRORS)
    if not hasattr(lib, "luaopen_xlua"):
        print("XLUA_NATIVE_REGRESSION_PASS (core)")
    return 0
