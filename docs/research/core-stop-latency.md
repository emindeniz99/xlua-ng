# Late stops while an instruction limit runs (2026-10-18)

While an `InstructionLimit` was set, `TimeLimit` and `Interrupt()` stopped a
call hundreds of milliseconds to seconds late, and `LuaScript.Interrupt()`
did nothing on a script without limits (core-luascript.md, sections 8.10
and 8.11; questions 35 and 36). This note records the cause, the evidence,
the fix and its cost. It follows
[watchdog-lost-stop.md](watchdog-lost-stop.md), which fixed a different lost
stop of the same arming code.

Confidence marks: [C] confirmed here by a test or by the code at the cited
line, [S] from a secondary source, [U] unconfirmed.

## Setup

- macOS 27 (Darwin 27.0.0) on Apple silicon (6 performance and 4 efficiency
  cores), Apple clang, Mono 6.14.1, .NET 10.0.12. Release libraries of
  `XLUA_PRESET=core` and of the legacy preset with `XLUA_LUASCRIPT=ON`, Lua
  5.4.9 and 5.5.1, from main `c74f4bbc` (before) and this branch (after).
- Other agents ran tests on the machine at the same time, so single stops
  vary by tens of milliseconds; the tables give distributions.
- `Test/Native/core_stop_latency.c`: endless chunks (0 `while true do end`,
  1 a loop of calls, 2 a loop in a coroutine, 3 table and arithmetic work,
  4 endless tail calls, 5 a loop over two lines) through LuaScript's entry
  (`xlua_core_call`) or LuaEnv's (`lua_pcall`), under a 50 ms time limit
  with a thread that polls `xlua_core_watch` as `LuaWatchdog.cs` does (every
  quarter of the limit, so 12.5 ms), or with one `xlua_core_interrupt` from
  a second thread 20 ms into the call. "interrupt-watched" adds a 60 s time
  limit, so the watchdog polls and re-arms. Latency is the time from the
  deadline or the interrupt to the return of the call.
- A diagnostic program (not in the repository, with the logs in the owner's
  scratch area, `core-latency/diag/`) compiled against the vendored
  `lstate.h` reads the running thread's `hookcount`, `basehookcount` and
  `hookmask` from the interrupting thread right after `xlua_core_interrupt`.

## Before the fix

`core_stop_latency`, core 5.5.1, 5 runs per case, milliseconds after the
deadline or the interrupt (min / median / max over the six chunks):

| Case | Instruction limit | min | median of medians | max |
|---|---|---:|---:|---:|
| time limit | none | 0.2 | 10 | 18 |
| time limit | 3e9 | 14 | 370 | 1,959 |
| time limit | 1e11 | 9 | 230 | 2,130 |
| Interrupt | 3e9 | 0.1 | 2,580 | 5,381 |
| Interrupt | 1e11 | 0.2 | 2,580 | 5,287 |
| Interrupt, watchdog polling | none | 0.0 | 0.1 | 0.3 |
| Interrupt, watchdog polling | 1e11 | 0.0 | 140 | 2,169 |
| Interrupt | no limit at all | never: `Interrupt` did nothing | | |

Interrupt waited for the end of the slice almost every time: 2.1 to 2.3 s
for the plain loop (2^30 instructions), 5.3 s for the loop of calls. With a
time limit the watchdog arms again on every poll, so a later arming
sometimes got through. The C# tests (`StopLatencyTests.cs`, Mono, the real
`LuaWatchdog`) showed the same: a 50 ms time limit 186 to 2,135 ms late,
Interrupt 2,160 to 5,394 ms late, and without limits the bounded loop ran to
its end (6.5 s) without an exception. [C]

## Cause

1. With an instruction limit the running thread has a count hook armed, so
   every Lua frame has its `trap` flag set and `luaV_execute` calls
   `luaG_traceexec` before every instruction (`vmfetch`, `lvm.c:1146-1152`
   in 5.4.9, `1191-1197` in 5.5.1). [C]
2. `luaG_traceexec` decrements the count with a plain load and store:
   `--L->hookcount` (`ldebug.c:939` in 5.4.9, `947` in 5.5.1). The field is a
   plain `int` (`lstate.h:330` in 5.4.9, `306` in 5.5.1); only `hookmask` is
   `volatile` (`331`, `307`). In the 5.5.1 build this is `ldr w8, [x0,
   #0xc0]; subs w8, w8, #1; str w8, [x0, #0xc0]` (`luaG_traceexec+0x3c`), and
   `hookmask` (offset 0xc4) is loaded once per call. [C]
3. The watchdog and `Interrupt` arm the stop hook from their own thread
   (`arm_stop_remote`, `build/xlua_core_limits.c`): `basehookcount = 1`,
   `hookcount = 1`, the mask, a fence, then `lua_sethook` with the same
   values. [C]
4. The running thread's next decrement stores the old count minus one over
   the `1`: its load came before the remote store became visible, or it took
   the value from its own pending store. The lost update needs no reordering;
   on a thread that writes the field every few nanoseconds it is the usual
   outcome. [C for the effect (below); the store-buffer explanation is
   inferred]
5. `basehookcount = 1` survives, because the running thread only reads it.
   At the end of the slice `resethookcount` loads it, the hook fires with
   count 1 and the stop is raised. The stop therefore came at the end of the
   current slice: up to 2^30 instructions, 1 to 5 s at this machine's speed
   on the hook path. [C]

**Evidence** (the diagnostic program, `while true do end` under a 1e11
instruction limit, Interrupt 30 ms into the call, 20 calls per version):

- 5.5.1: 19 of 20 armings lost; 5.4.9: 20 of 20. [C]
- In a lost arming the interrupting thread reads `hookcount` 1 right after
  its own store, and 1 µs later the old count again, decreasing (for example
  1,057,078,311 before, 1, then 1,057,066,779 1 µs later), with
  `basehookcount` 1 and `hookmask` 8 (`LUA_MASKCOUNT`). The call ended 2.1 s
  later, at the end of the slice. [C]
- Without an instruction limit the running thread has no hook, does not
  write `hookcount`, and the time limit held (0.2 to 18 ms late: the
  watchdog's 12.5 ms period). [C]

**`Interrupt()` without limits.** `xlua_core_interrupt` acts only while
`entry_active`, which `xlua_core_begin` sets only for a state with a limit
(`xc->limited`, `xlua_core_set_limits`); without one there is no running
thread to arm. [C]

## Fix

In `build/xlua_core_limits.c`, without a change to the vendored Lua:

1. **Line events.** `arm_stop_remote` stores `LUA_MASKCOUNT | LUA_MASKLINE`.
   The running thread never writes `hookmask`, so this store cannot be lost,
   and `luaG_traceexec` reads the mask on every instruction and calls the
   hook at the next backward jump, change of line or entry into a Lua
   function (`ldebug.c:952-962` in 5.4.9, `960-970` in 5.5.1; `oldpc = 0` at
   a call, `ldo.c:396` and `479`; `oldpc = 1` after `VARARGPREP`,
   `lvm.c:1894` and `1965`). Code that runs forever has a loop, a call or a
   tail call, so it reaches one within a few instructions; C code is stopped
   by the C budget and at the return of a host function, as before.
   `core_hook` treats a line event like a count event; with `stop` set it
   replaces the remote arming with this thread's own count-1 stop hook, so
   that no further Lua instruction runs. [C]
2. **Slices of at most 2^20 instructions** (was 2^30). A stop that does not
   reach the thread for any reason is late by one slice at most (1 to 5 ms
   on the hook path), and while an instruction limit runs the hook reads the
   native deadline at every slice end, so the time limit no longer depends on
   the watchdog's period then. The count stays exact: slices only split the
   budget. [C]
3. **`XLUA_LIMITS_INTERRUPT`** (flag 4 of `xlua_core_set_limits`): every
   outermost call begins and ends like a limited one (no hook, no deadline),
   so the running thread is known and `Interrupt` arms it. `LuaScript` sets it
   on every state; `LuaEnv` does not (its `Interrupt` is documented to need a
   limit). [C]

**Attribution** (core 5.5.1, 20 runs per case, each change alone): line
events alone stop Interrupt with a 1e11 limit within 0.1 to 0.2 ms (median;
max 4.8 ms) and the time limit within the watchdog's period (max 42 ms,
median 7 to 13 ms); 2^20 slices alone stop Interrupt at the next slice end
(median 1.3 to 2.5 ms, max 45 ms) and the time limit 1 to 2.5 ms late
(median; max 6 ms). Both
together give the best of each. [C]

## After the fix

`core_stop_latency` on the merged tree (main `98b3bab5` and this branch),
macOS arm64 with a load average under 7, 20 runs per case, core libraries
through LuaScript's entry and legacy libraries through LuaEnv's (`lua_pcall`),
Lua 5.4.9 and 5.5.1, milliseconds:

| Case | median range | max |
|---|---:|---:|
| time limit, no instruction limit | 6.6 to 11.7 | 17.4 |
| time limit, 3e9 or 1e11 | 0.3 to 4.0 | 9.7 |
| Interrupt, 3e9 or 1e11 | 0.1 to 0.3 | 2.5 |
| Interrupt, watchdog polling, no instruction limit | 0.1 to 0.2 | 20.9 |
| Interrupt, watchdog polling, 3e9 or 1e11 | 0.0 to 0.2 | 6.3 |
| Interrupt, no limit at all (LuaScript) | 0.1 to 0.2 | 0.3 |

A further 400 stops of the plain loop and the coroutine loop with
instruction limits (100 runs each) were all within 4.9 ms. Before the fix
the same program on LuaEnv's entry (5.5.1) and on core 5.4.9 showed what the
table above shows for core 5.5.1: a time limit with an instruction limit
median 10 to 982 ms late (max 2,160), Interrupt 2,147 to 5,361 ms. [C]

**Under load.** While other agents' tests and a Unity editor loaded the
machine (load average 30 to 320 on 10 cores), single stops after the fix
came up to 125 ms late with an instruction limit, and up to 1,064 ms late
with a time limit alone, a path this change does not touch: the running or
the watchdog thread was not scheduled. The suites' checks
(`stop_latency.py`, `StopLatencyTests.cs`) therefore allow 100 ms;
`core_stop_latency.c` keeps 50 ms for measurements on a quiet machine. [C]

## Cost

- **`benchmark`-style C cases** (`Test/Native/flag_bench.c` on the host,
  legacy 5.5.1, five interleaved rounds, minimum per case, before against
  after): the geometric mean changes by +0.6% for a core state without
  limits (code not touched: the noise level), +0.6% with an instruction
  limit, +0.2% with a time limit and -1.1% with both; single cases move by
  up to ±4% in both directions. No measurable cost. [C]
- **`Test/Native/benchmark.py`** (gencode, Mono, `XLUA_BENCH_LIMITS`, main
  `98b3bab5` against this branch, legacy 5.5.1, one round of 7 runs): with an
  instruction limit the geometric mean of 44 cases is +0.8% (`lua.function_call`
  28.4 to 28.8 ns, `lua.table_array_read` 17.4 to 17.6, `cs_lua.delegate_int`
  91.1 to 90.1); with a time limit +1.5%, the largest moves in Lua-to-C#
  cases that the change does not reach (no hook runs under a time limit
  alone), so layout and machine noise. [C]
- **`XLUA_LIMITS_INTERRUPT`** (a scratch benchmark of `xlua_core_call` and a
  host function called from Lua, core 5.5.1, best of 9 rounds): 97.9 to
  104.2 ns per one-argument `xlua_core_call` (+6.3 ns) and 27.2 to 28.1 ns
  per host function call from Lua (+0.9 ns): the entry's lock and stores,
  and the stop check after each host function. A state without the flag
  pays nothing. [C]
- **Size:** the fix adds a few instructions to `arm_stop_remote` and
  `core_hook`; not measured separately on Android.

## ThreadSanitizer

`core_stop_latency.c` under ThreadSanitizer (clang 21, Linux arm64 container
`xlua-analysis:26.04`, core 5.4.9 and 5.5.1, every case, 5 runs): 451 and 531
reports, all data races on the hook fields that the arming writes from
another thread and the running thread reads or writes: `hookmask`,
`hookcount`, `basehookcount` and the frames' `trap` (`arm_stop_remote`,
`lua_sethook`, `settraps`, `luaG_traceexec`, `luaG_tracecall`, `luaD_hook`,
the trap reload in `luaV_execute`), the same fields as before the fix; no
use after free, no other kind of report. A few reports were the harness's
own globals, made atomic afterwards. Under the sanitizer everything runs 10
to 50 times slower, so stops came 100 to 650 ms late there; that run says
nothing about latency. AddressSanitizer and UBSan (`sanitize.sh`,
`core-5.4.9 core-5.5.1`) pass. [C]

## Still open

- The arming from another thread is still a data race in the C sense: the
  remote stores to `hookmask`, `hookcount` and the frames' `trap` race with
  the running thread (ThreadSanitizer reports them, as before the fix). The
  frame walk of question 14 is unchanged. The vendored VM change described in
  [watchdog-lost-stop.md](watchdog-lost-stop.md) (the VM checks
  `L->hookmask` where it reloads its trap flag) would make the remote side
  store only the mask; with the line-event arming the mask alone already
  stops the call, so that change would now be enough by itself. [C for the
  races; U for any effect]
- Not measured on a phone or the emulator; the M11 smoke saw 966 ms there.
- `LuaEnv.Interrupt()` without limits still does nothing (documented).

## Sources

- Lua 5.4.9 and 5.5.1 in `build/lua-5.4.9`, `build/lua-5.5.1` (lines
  above); `objdump -d` of the 5.5.1 release library. [C]
- Arm, "Arm Architecture Reference Manual for A-profile", the memory model
  chapter (B2): a plain load and store are not an atomic read-modify-write,
  and stores from two cores to one location are ordered only by coherence. [S]
