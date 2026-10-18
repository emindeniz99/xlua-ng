"""How late the time limit and Interrupt stop a running call (the core_stop_latency
checks of core_regression.py and native_regression.py; docs/research/core-stop-latency.md).

Before the fix of 2026-10-18 an instruction limit made both wait for the end of
a count slice of 2^30 instructions: the running thread's decrement of
L->hookcount overwrote the hookcount = 1 that the watchdog or Interrupt stored
(build/xlua_core_limits.c, arm_stop_remote). A 50 ms time limit then stopped
after up to 2 s and Interrupt after 2 to 5 s. Test/Native/core_stop_latency.c
measures the same with many more runs and prints the distributions.

check(lib, L, call, interrupt_flag) runs endless chunks through call(code) (which
returns the stop reason) under a 50 ms time limit with a watchdog thread that
polls xlua_core_watch as LuaWatchdog.cs does, and with Interrupt from a second
thread 20 ms into the call, without and with instruction limits far above what
the call can run. interrupt_flag is the xlua_core_set_limits flag for a call
without limits (XLUA_LIMITS_INTERRUPT, as LuaScript passes it), or None where
the entry has no Interrupt without limits (LuaEnv). Every stop must come within
SLACK seconds of its deadline or of the interrupt.

xlua_core_interrupt returns 0 while no call runs, and on a loaded machine the
call may not have begun 20 ms after the run started: the interrupting thread
then tries again until it returns 1 and the latency counts from there
(2026-10-18: one Interrupt that came too early ended a run of
native_regression.py with the instruction limit's status; core-luascript.md,
section 8.14). extra_codes are endless chunks that need the caller's globals
(LuaEnv's re-entry from a coroutine); they run in every case like CODES.
LONG_CHUNK compiles for about 100 ms before it loops: a stop during the compile
must stop the call, at the latest when the compile ends (LuaEnv compiles in an
entry of its own, which lost the stop before the fix of section 8.14).
"""
import threading
import time

LIMIT_TIME, INTERRUPTED = 11, 12
SLACK = 0.1  # the watchdog's period (12.5 ms) and a busy machine; before the fix stops came 0.1 to 5 s late
LIMIT = 0.05
CODES = [b"while true do end", b"local function f() end while true do f() end",
         b"coroutine.wrap(function() while true do end end)()", b"local function f(n) return f(n + 1) end return f(0)"]
# Without a limit nothing else stops a lost Interrupt: a loop that ends on its own
# after a few seconds, so that the check fails instead of hanging.
BOUNDED = b"for i = 1, 3e9 do end"
# "do end" compiles to no instruction: only parse time, then the loop.
LONG_CHUNK = b"do end " * 2000000 + b"while true do end"
LONG_SLACK = 2.0  # the rest of the compile, which no stop interrupts


def check(lib, L, call, interrupt_flag, runs=3, extra_codes=()):
    import ctypes as C
    lib.xlua_core_set_limits.argtypes = [C.c_void_p, C.c_int64, C.c_int64, C.c_int32]
    lib.xlua_core_set_limits.restype = C.c_int
    lib.xlua_core_watch.argtypes = lib.xlua_core_interrupt.argtypes = [C.c_void_p]
    lib.xlua_core_watch.restype = C.c_int64
    lib.xlua_core_interrupt.restype = C.c_int
    state = {"start": 0.0, "poll": 0.0, "interrupt_at": 0.0, "interrupted": 0.0, "early": 0, "quit": False}
    gate = threading.Lock()
    codes = CODES + list(extra_codes)

    def helper():
        while not state["quit"]:
            with gate:
                if state["start"] and state["poll"]:
                    lib.xlua_core_watch(L)
                if state["interrupt_at"] and time.monotonic() >= state["interrupt_at"]:
                    if lib.xlua_core_interrupt(L):
                        state["interrupted"] = time.monotonic()
                        state["interrupt_at"] = 0.0
                    else:
                        state["early"] += 1  # the call has not begun: try again
            time.sleep(state["poll"] or 0.0005)

    def one(code, expect, interrupt):
        with gate:
            state["interrupted"] = 0.0
            state["start"] = time.monotonic()
            state["interrupt_at"] = state["start"] + 0.02 if interrupt else 0.0
        reason = call(code)
        end = time.monotonic()
        with gate:
            start, state["start"], state["interrupt_at"] = state["start"], 0.0, 0.0
        assert reason == expect, (code[:60], reason, expect, "interrupt accepted" if state["interrupted"] else "")
        return end - (state["interrupted"] if interrupt else start + LIMIT)

    thread = threading.Thread(target=helper)
    thread.start()
    late, long_late = [], []
    try:
        for instructions in (0, 3 * 10 ** 9, 10 ** 11):
            assert lib.xlua_core_set_limits(L, instructions, int(LIMIT * 1e9), 0) == 1
            state["poll"] = LIMIT / 4  # LuaWatchdog: a quarter of the limit, 1 to 20 ms
            for code in codes:
                late += [("time", instructions, code, one(code, LIMIT_TIME, False)) for _ in range(runs)]
            if instructions:
                long_late += [("time", instructions, b"long chunk", one(LONG_CHUNK, LIMIT_TIME, False))]
            state["poll"] = 0.0  # the C# watchdog polls only states with a time limit
            if instructions:
                assert lib.xlua_core_set_limits(L, instructions, 0, 0) == 1
                for code in codes:
                    late += [("interrupt", instructions, code, one(code, INTERRUPTED, True)) for _ in range(runs)]
                long_late += [("interrupt", instructions, b"long chunk", one(LONG_CHUNK, INTERRUPTED, True))]
            elif interrupt_flag is not None:
                assert lib.xlua_core_set_limits(L, 0, 0, interrupt_flag) == 1
                late += [("interrupt", 0, BOUNDED, one(BOUNDED, INTERRUPTED, True)) for _ in range(runs)]
    finally:
        state["quit"] = True
        thread.join()
        lib.xlua_core_set_limits(L, 0, 0, 0)
    worst = max(late, key=lambda x: x[3])
    bad = [x for x in late if x[3] > SLACK] + [x for x in long_late if x[3] > LONG_SLACK]
    assert not bad, "%d of %d stops later than %d ms, e.g. %s" % (len(bad), len(late) + len(long_late),
                                                                  SLACK * 1000, bad[:4])
    return len(late) + len(long_late), worst, state["early"]
