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

## Sources

- Lua 5.4.9 and 5.5.1 in `build/lua-5.4.9`, `build/lua-5.5.1` (lines
  above); `objdump -d` of the 5.5.1 release library. [C]
- Arm, "Arm Architecture Reference Manual for A-profile", the memory model
  chapter (B2): a plain load and store are not an atomic read-modify-write,
  and stores from two cores to one location are ordered only by coherence. [S]
