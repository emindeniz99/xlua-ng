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
- core_trusted_bytecode: xlua_core_run_mode's modes. Without CMake
  XLUA_TRUSTED_BYTECODE binary chunks are refused before anything runs; with it
  the host's precompiled chunks (TRUSTED below) run in an environment, text and
  binary keep to their modes, a damaged chunk fails in Lua's loader, and a
  script's load stays text only. XLUA_EXPECT_TRUSTED_BYTECODE=ON or OFF says
  which build it is; unset, the library decides.
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
DIRECTIVES = ("env", "status", "expect", "instructions", "require")


class Config(C.Structure):  # xlua_core_config, build/xlua_core.h
    _fields_ = [("memory_limit", C.c_int64), ("seed", C.c_uint32), ("has_seed", C.c_int32),
                ("libs", C.c_int32), ("sandbox", C.c_int32)]


class Stats(C.Structure):  # xlua_core_stats
    _fields_ = [(n, C.c_int64) for n in ("memory_used", "memory_peak", "memory_limit", "refusals", "overdrafts",
                                         "host_oom", "instructions", "stop_reason", "instruction_limit",
                                         "time_limit_ns")]


class Source(C.Structure):  # xlua_core_source
    _fields_ = [("code", P), ("code_len", C.c_int32), ("chunkname", P), ("chunkname_len", C.c_int32)]


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
            ("xlua_core_newenv", [P, C.POINTER(C.c_int32)], C.c_int),
            ("xlua_core_unref", [P, C.c_int32], C.c_int),
            ("xlua_core_run", [P, C.c_int32, C.c_char_p, C.c_int32, C.c_char_p, C.c_int32,
                               C.POINTER(P), C.POINTER(C.c_int32)], C.c_int),
            ("xlua_core_run_mode", [P, C.c_int32, C.c_char_p, C.c_int32, C.c_char_p, C.c_int32, C.c_int32,
                                    C.POINTER(P), C.POINTER(C.c_int32)], C.c_int),
            ("xlua_core_set_resolver", [P], None),
            ("xlua_core_set_require", [P, C.c_int32], C.c_int),
            ("xlua_core_interrupt", [P], C.c_int), ("xlua_core_watch", [P], C.c_int64),
            ("xlua_gc", [P, C.c_int, C.c_int], C.c_int),
            ("lua_checkstack", [P, C.c_int], C.c_int),
            ("lua_gettop", [P], C.c_int)]:
        fn = getattr(lib, name)
        fn.argtypes = args
        fn.restype = res


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


def run(lib, L, code, env=0, name=b"t"):
    out, n = P(), C.c_int32()
    code = code if isinstance(code, bytes) else code.encode()
    status = lib.xlua_core_run(L, env, code, len(code), name, len(name), C.byref(out), C.byref(n))
    return status, (C.string_at(out, n.value).decode("utf-8", "replace") if out.value else None)


def run_mode(lib, L, code, mode, env=0, name=b"t"):
    out, n = P(), C.c_int32()
    code = code if isinstance(code, bytes) else code.encode()
    status = lib.xlua_core_run_mode(L, env, code, len(code), name, len(name), mode, C.byref(out), C.byref(n))
    return status, (C.string_at(out, n.value).decode("utf-8", "replace") if out.value else None)


def trusted_bytecode(lib):
    """Whether xlua_core_run_mode takes binary chunks (CMake XLUA_TRUSTED_BYTECODE), checked
    against XLUA_EXPECT_TRUSTED_BYTECODE when it is set."""
    L, _ = open_state(lib)
    try:
        status, out = run_mode(lib, L, "return 1", MODE_TEXT | MODE_BINARY)
    finally:
        lib.xlua_core_close(L)
    built = status == OK
    assert built or (status, out) == (ERRCONFIG, "binary chunks are not supported in this build (XLUA_TRUSTED_BYTECODE is off)"), (status, out)
    expect_ = os.environ.get("XLUA_EXPECT_TRUSTED_BYTECODE", "").upper()
    assert expect_ != "ON" or built, "XLUA_EXPECT_TRUSTED_BYTECODE=ON, but xlua_core_run_mode refuses binary chunks"
    assert expect_ != "OFF" or not built, "XLUA_EXPECT_TRUSTED_BYTECODE=OFF, but xlua_core_run_mode takes binary chunks"
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
    envs, failed = {"globals": 0}, []
    try:
        for case in cases:
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
        assert lib.lua_gettop(L) == 0
    finally:
        lib.xlua_core_set_resolver(None)
        lib.xlua_core_close(L)
    assert not failed, "\n".join(failed)
    print("core_sandbox: %d cases, %d modules" % (len(cases), len(modules)))


def check_core_sandbox_abi(lib):
    assert lib.xlua_core_version() == 2
    # Configuration: the sandbox is required, and only the libraries this build has.
    assert open_state(lib, sandbox=0) == (None, ERRCONFIG)
    for libs in (LIBS["io"], LIBS["os"], LIBS["debug"], LIBS["package"] | LIBS["string"], LIBS["buffer"], 1 << 12):
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
    out, n = P(), C.c_int32()
    assert lib.xlua_core_run(L, other.value, b"return 1", -1, b"neg", -4, C.byref(out), C.byref(n)) == OK
    assert C.string_at(out, n.value) == b"nil"  # negative lengths are empty
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
                            ("lua_dump", [P, P, P, C.c_int], C.c_int)]:
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
        # ask it for binary chunks only through xlua_core_run_mode.
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
        # A registry reference to a table with another metatable is no environment.
        lib.lua_createtable(L, 0, 0)
        lib.lua_createtable(L, 0, 0)
        lib.lua_setmetatable(L, -2)
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


def check_core_trusted_bytecode(lib):
    built = trusted_bytecode(lib)
    L, _ = open_state(lib)
    version = run(lib, L, "return _VERSION")[1]
    chunks = TRUSTED[version]
    other = TRUSTED["Lua 5.4" if version == "Lua 5.5" else "Lua 5.5"]["main"]
    try:
        # xlua_core_run is the text mode; a mode without either kind is refused.
        assert run_mode(lib, L, "return 'text'", MODE_TEXT) == (OK, "text")
        for mode in (0, 4, -1, MODE_TEXT | 4):
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


def main(lib, names):
    bind(lib)
    for name in names or ["core_sandbox", "core_sandbox_abi", "core_host_oom", "core_lua_api", "core_trusted_bytecode"]:
        globals()["check_" + name](lib)
        assert not CALLBACK_ERRORS, (name, CALLBACK_ERRORS)
    if not hasattr(lib, "luaopen_xlua"):
        print("XLUA_NATIVE_REGRESSION_PASS (core)")
    return 0
