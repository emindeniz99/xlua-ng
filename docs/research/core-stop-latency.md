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

## On target (2026-10-18)

The same fix in IL2CPP players, measured by the core-only player smoke
(`Test/MobileSmoke/CoreSmoke.cs`, which now prints a latency distribution
per case; see [Test/MobileSmoke/README.md](../../Test/MobileSmoke/README.md)).

**Setup.**

- Two players with the same `CoreSmoke.cs`, built with Unity 6000.3.21f1,
  release, ARM64 IL2CPP, Minimal stripping and `XLUA_CORE_ONLY`
  (`prepare.py --package --core`):
  - *before*: the C# and the core Lua 5.5.1 libraries of main `bebb8faa`, the
    commit before the fix was merged;
  - *after*: main `446dea2b`, with the fix.
- The libraries are `XLUA_VARIANT=core XLUA_PRESET=core`, Android arm64-v8a
  from `build/make_android_lua55.sh` (NDK r30) and the arm64 iOS Simulator
  from `XLUA_IOS_SDK=simulator build/make_ios_lua55.sh` (Xcode 27.1).
- Targets:
  - the `xlua16k` AVD (Android 16, 16 KB pages, 4 vCPUs);
  - the iOS 27.1 Simulator ("iPhone Duo"), through `ios_sim_smoke.py`;
  - a moto g play 2024 (Android 14, thermal status 0).
- The host was shared with other agents' builds. The load average was 20 to
  78 during the first emulator session, 16 to 25 during the second, and 10 to
  200 during the Simulator runs. The phone runs its code on its own CPU.
- Every case runs `while true do end` (the case without limits: `for i = 1,
  3e9 do end`), 20 times on one `LuaScript`, with no new run after 20 s. A
  time limit's latency is the time from its deadline to the return of
  `Load`. An interrupt's latency is the time from the moment a second thread
  calls `Interrupt()`, 50 ms into the call, to that return. With an
  instruction limit the cases set 3e9 or 1e11 instructions; "time 30 s" is a
  time limit that only makes the watchdog poll.
- Before the fix the players ran once per target. After the fix, the phone
  ran twice and the AVD and the Simulator three times; the tables pool the
  runs (n = 40 or 60). The first two runs on the AVD and the Simulator were
  of a build with the first pass bound (every stop within 100 ms), the third
  of the final one (below). Their latency code is the same.

**Results** (milliseconds: median / 90th percentile / maximum). [C]

Phone (moto g play 2024):

| Case | before | after |
|---|---:|---:|
| time limit 100 ms | 0.6 / 0.9 / 1.2 | 1.1 / 20.6 / 20.7 |
| time limit, instructions 3e9 | 182 / 807 / 1,229 | 5.2 / 9.0 / 12.3 |
| time limit, instructions 1e11 | 222 / 847 / 1,148 | 4.2 / 9.1 / 9.4 |
| Interrupt, no limit | never stopped (the loop ended after 15 s) | 0.6 / 0.6 / 2.9 |
| Interrupt, instructions 3e9 | 12,188 / 12,197 / 12,197 (2 runs in 20 s) | 0.5 / 0.6 / 0.7 |
| Interrupt, time 30 s | 0.5 / 0.5 / 0.6 | 0.5 / 0.6 / 0.6 |
| Interrupt, time 30 s, instructions 1e11 | 292 / 715 / 1,641 | 0.5 / 0.6 / 0.6 |

Android emulator (`xlua16k`):

| Case | before | after |
|---|---:|---:|
| time limit 100 ms | 16 / 28 / 66 | 9.8 / 42 / 130 |
| time limit, instructions 3e9 | 33 / 79 / 178 | 4.5 / 44 / 219 |
| time limit, instructions 1e11 | 40 / 108 / 223 | 3.3 / 24 / 151 |
| Interrupt, no limit | never stopped (24 s) | 0.6 / 34 / 123 |
| Interrupt, instructions 3e9 | 9,399 / 11,775 / 11,775 (2 runs) | 0.7 / 13 / 59 |
| Interrupt, time 30 s | 3.3 / 26 / 58 | 0.5 / 14 / 316 |
| Interrupt, time 30 s, instructions 1e11 | 7.1 / 132 / 186 | 0.5 / 13 / 222 |

iOS Simulator:

| Case | before | after |
|---|---:|---:|
| time limit 100 ms | 18.5 / 23 / 25 | 17 / 23 / 78 |
| time limit, instructions 3e9 | 90 / 461 / 695 | 1.9 / 23 / 92 |
| time limit, instructions 1e11 | 258 / 497 / 643 | 1.8 / 5.5 / 41 |
| Interrupt, no limit | never stopped (6.2 s) | 0.1 / 1.2 / 23 |
| Interrupt, instructions 3e9 | 2,738 / 3,589 / 3,589 (8 runs; one stopped after 2.8 ms) | 0.1 / 0.3 / 5.0 |
| Interrupt, time 30 s | 0.1 / 0.2 / 0.4 | 0.1 / 0.3 / 6.0 |
| Interrupt, time 30 s, instructions 1e11 | 11 / 53 / 132 | 0.1 / 0.9 / 34 |

The smoke's older single measurement (a 100 ms time limit with a 3e9
backstop, timed from before `new LuaScript` to after `Dispose`) gave 343 ms
on the phone, 117 ms on the AVD and 292 ms in the Simulator before the fix.
After the fix it gave 102 ms on the phone, 103 to 109 ms on the AVD and 100 to
112 ms in the Simulator. (The M11 runs, also before the fix, gave 222 to 243,
107 to 966, and 1,472 and 2,191 ms.)

The same `CoreSmoke.cs` on the macOS host gave the same picture (Mono, a stub
of `UnityEngine`, core 5.5.1 libraries of the two commits, a loaded
machine). Before: time limit with an instruction limit median 68 to 362 ms,
max 1,342; Interrupt with 3e9 instructions 2,114 to 2,350 ms; no stop without
a limit. After: every case within 5.1 ms. [C]

**Findings.**

- The fix holds on all three targets. Before it, every target showed the
  lost arming: Interrupt with an instruction limit waited 2.4 to 12.2 s for
  the slice end (one run in the Simulator got through after 2.8 ms), and a
  time limit with an instruction limit came up to 1.2 s late on the phone.
  After it, the phone stopped every call under a time and an instruction
  limit within 12.3 ms and every Interrupt within 2.9 ms. [C]
- `Interrupt()` without limits did nothing in the players before the fix and
  stops within 3 ms on the phone after it. [C]
- A time limit alone is late by up to the watchdog's period (a quarter of the
  limit, at most 20 ms: 20 ms here), before and after the fix, as designed.
  On the phone the stops fell at about 0.6 ms or about 20.6 ms; before the fix
  all 20 fell at 0.6. The fix does not touch that path (no hook runs under a
  time limit alone), so this is a matter of which poll catches the deadline;
  that consecutive runs keep the same offset to the watchdog's polls is
  inferred, not checked. [C for the values; U for the cause]
- **The emulator's single stops scatter.** In the first session, at a host
  load average of 20 to 78, single stops after the fix came up to 316 ms late,
  in cases the fix does not change as well (time limit alone: 130 ms;
  Interrupt with a time limit alone: 316 ms, against 58 ms before). The
  medians were 0.5 to 10 ms. The third session (load 16 to 25) had every stop
  within 18.7 ms. The scatter is the AVD's virtual CPUs waiting for the
  loaded host; the phone shows none of it. [C for the values; the cause is
  inferred from the load and the cases]
- **Interrupt with a time limit alone** (no hook runs, the watchdog polls
  every 20 ms): milestone 9's run under heavy load once saw such an
  Interrupt arrive only at the 30 s deadline
  ([core-luascript.md](../design/core-luascript.md), milestone 9's results
  after merging main `6b37d258`). On target, 160 stops of this case after
  the fix (phone 40, AVD 60, Simulator 60) came within 316 ms, 135 of them
  within 1 ms, and 40 on the host within 0.1 ms; none was missed. That does
  not rule the case out under heavier load. [C for these runs]
- **The smoke's pass bound** is therefore a 90th percentile within 100 ms
  and every stop within 1 s per case, and no missed stop. The fixed players
  pass it everywhere (the phone has a 90th percentile of 20.6 ms at most).
  The players from before the fix fail it everywhere: stops 2 to 12 s late,
  the Interrupt without limits never stops, and the time limit with 1e11
  instructions has a 90th percentile of 108 ms even on the emulator. [C]

The result files, the device logs and the phone's command log were kept
with the run's work files, outside the repository.

## Missed stops under load (2026-10-18, second investigation)

After the fix above, three runs on a loaded machine (load average 50 to 100
from other agents) looked as if a stop had arrived late by seconds, although
the line-event arming and the 2^20-instruction slices should bound any delay
to a few milliseconds. This section finds what happened, on main
`314711d3`. Design summary: [core-luascript.md](../design/core-luascript.md),
section 8.14.

### The three observations, read from their logs

- **M9, `run_core.py --bridge`, Mono:** `TestInterruptIsOnTime` with a 30 s
  `TimeLimit` and no instruction limit. The log (`core-m9/final2/bridge/mono.log`,
  lines 56 and 57, in the owner's scratch area) says "expected
  LuaInterruptedException, got LuaTimeLimitException", then "Interrupt stopped
  the call 30009 ms late". The call was not interrupted late: the Interrupt
  never took effect, and the time limit ended the call. [C]
- **`native_regression.py` on legacy with the sandbox:** `AssertionError:
  (b'while true do end', 10, 12)`. Status 10 is the instruction limit: the
  Interrupt never set the stop, and the 3e9 instructions ran out. [C]
- **`core_stop_latency --entry pcall`:** `interrupt-watched/3e+09/0 run 1:
  status 10, expected 12 (after 9857.8 ms)`, the same as the previous one, and
  one stop 122.9 ms late. [C]

So two of the three were not late stops but lost Interrupts, and in both
the instruction or time limit ended the call.

### Tools

- **`Test/Native/core_stop_latency.c`** now:
  - retries the Interrupt every 0.2 ms while `xlua_core_interrupt` returns 0
    (no call running) and counts those runs as "early"; `--interrupt-once 1`
    tries once, as the old harness did, and reports a lost Interrupt as LOST;
  - prints every late run with the CPU time the running thread got after the
    deadline or the accepted Interrupt (`thread_info` on macOS,
    `pthread_getcpuclockid` on Linux). A stop that the thread does not see
    costs as much CPU as wall time. A thread the scheduler does not run costs
    almost none;
  - has four more endless codes: a loop of host-function calls (6), the same
    in a coroutine (7), a coroutine whose host function calls back into Lua
    (8; on LuaEnv's entry on the main thread, as `LuaFunction.Call` does), the
    callback without a coroutine (9), and a chunk that compiles for about
    100 ms before it loops (10, not in the default set);
  - loads LuaEnv's chunks through `xluaL_loadbuffer`, `DoString`'s own entry,
    instead of `luaL_loadstring`;
  - `--demote main|helpers|all` (utility QoS on macOS: the background class
    starved the thread completely) and `--point NAME:N --pause-ms P`, below.
- **Test points** (`-DXLUA_LIMITS_TEST_POINTS`, never in a release,
  `build/xlua_core_limits.c`): a hook the harness sets is called at eight
  points of the limits code: the start of `xlua_core_begin` and
  `xlua_core_end`, `core_hook` before its lock and before raising,
  `xlua_core_switch`, `xlua_core_check_stop`, and inside `arm_stop_remote`
  after the field stores and after `lua_sethook`. The harness holds the
  thread that reaches the N-th hit of a point for 3 ms and interrupts while
  it is held. The same builds export `xlua_core_debug_state`, which the
  harness prints when a call is never stopped.
- **Artificial load:** 12 `yes > /dev/null` processes on top of the other
  agents' work (load average 60 to 250 during the runs), for 1 to 5 minutes
  at a time, with the calling thread demoted.

### Findings

1. **An Interrupt before the call has begun does nothing** [C]. This is the
   documented behaviour (`xlua_core_interrupt` acts only while
   `entry_active`, `xlua_core_limits.c:599` at `314711d3`), and the cause of
   the M9 failure and the two lost Interrupts above. The tests started a
   thread that slept 20 ms and then interrupted. They assumed the call was
   running by then. On a loaded machine the calling thread can be off the CPU
   for more than 20 ms between the start of the run and `xlua_core_begin`.
   That window holds Mono's marshalling into `xlua_core_call`, or Python's
   GIL and `ctypes`. Holding the calling thread at the `begin` point loses
   every Interrupt (40 of 40, both entries, before and after the fix). The
   C harness with its short window saw no early Interrupt in about 3,700
   loaded runs. The Python and C# harnesses' windows are wider.
2. **LuaEnv lost an Interrupt or a deadline that came while `DoString`
   compiled** [C]. `DoString` is two outermost entries:
   `xluaL_loadbuffer`, then `lua_pcall` (`LuaEnv.cs:633` and `641`). A stop
   accepted during the compile was cleared by the compile's own
   `xlua_core_end` (`xlua_core_limits.c:295`). The chunk then ran without
   it, until the instruction limit or forever. The time limit started again
   with the second entry, so a compile longer than the limit gave the chunk a
   whole new limit. Evidence:
   - the `end:1` point on LuaEnv's entry lost 36 of 36 accepted Interrupts
     (the instruction limit ended the calls);
   - under artificial load, without any test point, 1 run in 60 to 540 never
     stopped. The debug state showed `stop 0 pending 0` with the call still
     running and the slices still counting;
   - code 10 (a 14 MB chunk, about 80 ms to compile) was interrupted 20 ms in
     and ran 6 to 10 s, to the 3e9 instruction limit.
3. **LuaEnv did not limit a call back into Lua from a coroutine** [C]. A C#
   function that a coroutine calls and that calls a `LuaFunction` or a
   delegate runs that function on the main thread (`luaEnv.L`,
   `LuaFunction.cs:38`, `DelegateBridge.cs:165`), while `running` is still
   the coroutine. Two things followed:
   - A nested entry did not switch threads (`xlua_core.h:486-488` at
     `314711d3`), so `arm_stop_remote` armed the waiting coroutine.
   - `core_hook` disarms any thread that is not `running`
     (`xlua_core_limits.c:247-249`), so after its first count event the
     main thread ran with no hook at all.

   The result: Interrupt, the time limit and the instruction limit all
   failed to stop `inner = function() while true do end end
   coroutine.wrap(function() reenter() end)()` (code 8): the harness's 3 s
   and 30 s caps ended it. The same callback on the main thread (code 9), and
   LuaScript (which runs host callbacks on the coroutine's thread), were not
   affected.
4. **LuaScript returned normally from a call whose instruction limit had
   tripped** [C], in one case. `return pcall(string.find,
   string.rep('a', 100000), '.-b')` under a 100,000-instruction limit: the C
   budget raised inside `string.find`, `pcall` caught it, and the tail call
   left no Lua instruction for the stop hook. Status 0, result `false`.
   That breaks item 6 of `Security.md`. The same holds for any stop that
   another thread sets after the last instruction of a call, and for an
   Interrupt during `XLUA_CALL_COMPILE` (the compile returned its function).
5. **Late stops under load were scheduling** [C]. Under load the C harness
   flagged 163 runs later than 100 ms: 81 before the fix and 82 after, at most
   461 ms late. In every one the running thread got at most 10.4 ms of CPU
   between the stop and the return (3.0 ms after the fix). The 123 ms case
   above has the same shape. No run on either entry, before or after the fix,
   consumed CPU without seeing its stop, except the lost stops of findings 2
   and 3.
6. **Interrupt without any limit has no second chance** [U]. With a time
   limit the watchdog arms again on every poll while `stop_pending` is set.
   With an instruction limit the next slice end raises the stop. With
   neither, nothing arms again. The arming can still be missed in theory:
   - The running thread enters a new Lua function in two steps: it stores
     `L->ci` (`ldo.c:735` in 5.5.1), then loads `L->hookmask` into its trap
     (`lvm.c:1214`).
   - The arming thread does the reverse: it stores the mask, fences, then
     walks `L->ci` (`settraps`).
   - Without a fence on the running side, both threads can read the old
     value (store buffering, allowed on x86-64 as well as AArch64). The new
     frame then starts with trap 0, and a loop in it that makes no call never
     reads the mask.

   No run has shown this; the window is a few nanoseconds per call.

### The fix (`build/xlua_core.h`, `xlua_core_limits.c`, `xlua.c`, `xlua_core_sandbox.c`)

- **Nested entries follow the thread** (finding 3). A nested entry on a
  thread other than `running` switches to it (`xlua_core_switch`, as a
  coroutine resume does) and switches back when it leaves. The budget, the
  stop hook and the remote arming then reach the main thread while it runs
  the callback. Cost: one compare in nested entries; the switch only in that
  case.
- **A stop that an entry accepted fails that entry** (findings 2 and 4).
  `xlua_core_end` returns the stop it records under the lock, and
  `xlua_core_leave` keeps it in the entry. Two places use it:
  - In LuaEnv's `lua_pcall` and `xluaL_loadbuffer`, a body that returned
    normally then fails with the stop's message (`xlua_core_fail_stopped`):
    `DoString` throws `LuaInterruptedException` or `LuaTimeLimitException`
    at the end of the compile, and C# reads the reason from `stop_reason` as
    before.
  - LuaScript's `call_body` checks the stop before it converts any result
    (`xlua_core_check_stop`). A stop that comes after that check is caught at
    the end of `xlua_core_call`, which releases the result handles and fails.

  Reading the stop under the lock at the end of the call closes the window.
  An Interrupt either lands before the end and fails the call, or lands
  after it and returns 0.
- **The harnesses wait for the call** (finding 1):
  - `core_stop_latency.c` and `stop_latency.py` retry while
    `xlua_core_interrupt` returns 0;
  - the C# tests (`StopLatencyTests.cs`, `CoroutineTests.cs`,
    `CoreTests.cs`) call a host function `started()` at the start of the code
    and interrupt 20 ms after it ran;
  - the managed suite (`LuaOptionsForLuaCallCS.cs`) interrupts every 5 ms
    until the call has ended.

  The API is unchanged: an Interrupt with no call running still does nothing
  (question 80).

### Deterministic interleavings, before and after

`--interrupt-once 1`, instruction limit 3e8, `interrupt` and
`interrupt-watched`, codes 0 to 9 (0 to 7 and 9 before the fix, where code 8
never stops), 2 runs per case, the legacy 5.5.1 library with the sandbox and
the test points; "held" counts the Interrupts that came while the calling
thread was held at the point:

| Point, hit | LuaEnv entry before | LuaEnv entry after | LuaScript entry before | LuaScript entry after |
|---|---|---|---|---|
| `begin`, 1 (no call yet) | 36 of 36 held, all lost | 40 of 40, all lost | 40 of 40, all lost | 40 of 40, all lost |
| `begin`, 2 (LuaEnv: the `lua_pcall` after the compile) | 35 of 36, all lost | 40 of 40, all lost | not reached | not reached |
| `end`, 1 (LuaEnv: the end of the compile) | 36 of 36, all wrong status | 40 of 40, all stopped | not reached | not reached |
| `hook`, 1 to 3 | 93 of 108, all stopped | 113 of 120, all stopped | 102 of 120, all stopped | 101 of 120, all stopped |
| `switch`, 1 and 2 | 8 of 72, all stopped | 16 of 80, all stopped | 12 of 80, all stopped | 12 of 80, all stopped |
| `check-stop`, 1 to 3 | not reached | not reached | 24 of 120, all stopped | 24 of 120, all stopped |
| `remote-mask`, `remote-traps` (the arming thread held) | every run stopped | every run stopped | every run stopped | every run stopped |

The few late runs in these sweeps (11, at most 149 ms) all had at most 0.1 ms
of CPU after the Interrupt. The `begin` rows are finding 1, which the fix
leaves as it is. `hook-raise` is reached only after a stop, so it never held
an Interrupt.

### Under load, before and after

`core_stop_latency.c`, 12 CPU burners, the calling thread demoted, 10 runs
per case, codes 0 to 7 and 9 (and 8 after the fix on LuaEnv's entry), all
four kinds with instruction limits 0, 3e9 and 1e11, the release libraries of
core 5.5.1 and of legacy 5.5.1 with the sandbox. Milliseconds after the
deadline or the accepted Interrupt: range of the case medians, and the
largest maximum; load averages are at the start and end of each run:

| Group | LuaScript before (load 32 to 83) | LuaScript after (83 to 125) | LuaEnv before (125 to 184) | LuaEnv after (184 to 251) |
|---|---|---|---|---|
| time limit, no instruction limit | 32 to 57; 140 | 18 to 46; 158 | 31 to 46; 181 | 19 to 67; 304 |
| time limit and an instruction limit | 28 to 49; 172 | 13 to 57; 153 | 5 to 58; 252 | 23 to 62; 136 |
| Interrupt, instruction limit | 2 to 63; 154 | 5 to 61; 160 | 14 to 59; 145, then a call never stopped | 5 to 117; 258 |
| Interrupt, watchdog polling | 3 to 59; 214 | 0.1 to 68; 228 | not reached | 0.1 to 61; 195 |
| Interrupt, no limit at all | 4 to 16; 64 | 32 to 61; 175 | not reached | 0.1 to 61; 179 |
| most CPU of a late run after its stop | 0.6 ms | 3.0 ms | 10.4 ms | 0.0 ms |

The run on LuaEnv's entry before the fix ended in its 32nd case: an
Interrupt accepted during the compile entry, and the endless tail calls
(code 4) ran past the 15 s cap (finding 2). Two more loaded runs of that
library hit the same thing at codes 7 and 2 within 60 to 140 runs. The
second run printed the state: `depth 1 entry_active 1 stop 0 pending 0`, the
coroutine running and its slices still counting. After the fix, 600 loaded
runs of the same cases plus code 8 all stopped, and every late run had 0.0 ms
of CPU after its stop. The medians are tens of milliseconds on both sides
because the demoted thread waits for a CPU; the libraries do not differ
there. Without load the numbers are those of the table above.

The Python check (`native_regression.py core_stop_latency_env`, 79 stops)
passes on the fixed library without artificial load (latest 22 ms). With the
12 burners one time-limit stop came 112 ms late, over its 100 ms bound. The
suites' bound is not meant for that load. [C]

### ThreadSanitizer

`core_stop_latency.c` (codes 0 to 9; code 10 too on LuaEnv's entry) under
ThreadSanitizer, `xlua-analysis:26.04` (clang, Linux arm64), 3 runs per case:
core 5.5.1 and 5.4.9 through LuaScript's entry, 669 and 715 reports; legacy
5.5.1 with the sandbox through LuaEnv's entry, 723. All are data races on the
hook fields that the arming writes from another thread, as before:
- the arming thread's side: `arm_stop_remote`, `lua_sethook`, `settraps`;
- the running thread's side: `luaG_traceexec`, `luaD_hook`, the trap reload
  in `luaV_execute`, `precallC`, `luaD_poscall`, `luaG_tracecall`.

No report has a frame in the new code: the nested entry's `xlua_core_switch`,
`xlua_core_end`, `xlua_core_fail_stopped` and the end check run on the
calling thread under the core lock, as the coroutine switches do. No other
kind of report appeared. Every call stopped (latest 1.4 s under the
sanitizer, which slows everything 10 to 50 times). [C]

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
- On target the fix holds (above). The emulator's single stops still scatter
  up to 316 ms on a loaded host, in cases the fix does not touch as well;
  not measured: an iPhone, Lua 5.4.9 players, High stripping.
- `LuaEnv.Interrupt()` without limits still does nothing (documented).
- An `Interrupt()` before the call begins does nothing, and `Interrupt()`
  does not tell the caller (question 80); for `DoString` that includes the
  microseconds between its compile and its call (question 81). [C]
- `Interrupt()` without any limit has no second arming (finding 6, question
  83). [U]
- A compile is not interrupted: the stop comes when it ends (question 84).
  [C]

## Sources

- Lua 5.4.9 and 5.5.1 in `build/lua-5.4.9`, `build/lua-5.5.1` (lines
  above); `objdump -d` of the 5.5.1 release library. [C]
- Arm, "Arm Architecture Reference Manual for A-profile", the memory model
  chapter (B2): a plain load and store are not an atomic read-modify-write,
  and stores from two cores to one location are ordered only by coherence. [S]
