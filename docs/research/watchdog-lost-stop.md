# The time-limit watchdog lost its stop (2026-10-18)

A host run of the managed suite hung for 28 minutes at 100% CPU in
`CaseLuaOptionsTimeLimit`: `while true do end` under a 50 ms `TimeLimit`
never stopped. This note records the cause, the evidence and the fix.
Design context: [core-luascript.md](../design/core-luascript.md), sections
3.6 and 8.3.

Confidence marks: [C] confirmed here by a test or by the code at the cited
line, [S] from a secondary source, [U] unconfirmed.

## Setup

- macOS 27 (Darwin 27.0.0) on Apple silicon (6 performance and 4
  efficiency cores), Apple clang, Mono 6.14.1. Lua 5.5.1 and 5.4.9, Release
  `libxlua.dylib` from main 7ff004a.
- `wd_stress.c`: a C program that runs outermost calls of
  `while true do end` (and a coroutine and a calling variant) through the
  exported `lua_pcall` under a short time limit, with a second thread that
  polls `xlua_core_watch` as `LuaWatchdog.cs` does and a third thread that
  reports a call running for 2 s, samples the process and then arms the hook
  once more. `ManagedRepro.cs`: the same pattern through `LuaEnv.DoString`
  and the real `LuaWatchdog`, with and without the seven bridge hardening
  defines. Both are kept outside the repository, with the logs, in the
  owner's scratch area (`watchdog-hang-2026-10-18/` next to the other runs).

## Cause

1. With a time limit and no instruction limit the running thread has no
   hook (`arm_thread`, `build/xlua_core_limits.c`). The watchdog stops a call
   from its own thread with `lua_sethook(running, core_hook, LUA_MASKCOUNT,
   1)` under the core spinlock. [C]
2. `lua_sethook` stores `L->hook`, the counts and `L->hookmask`, then sets
   `ci->u.l.trap = 1` on every Lua frame (`settraps`; `ldebug.c:117-121` and
   `134-145` in 5.4.9, `116-120` and `133-144` in 5.5.1). Its comment says it
   "can be called during a signal" (`ldebug.c:124-132` in 5.4.9): a signal
   runs on the hooked thread itself, so the stores need no ordering. [C]
3. The running thread executes `OP_JMP`, whose `dojump` reloads the frame's
   trap flag (`updatetrap`, `lvm.c:1120` and `1133`, `OP_JMP` at `1652` in
   5.5.1; `1083`, `1096`, `1607` in 5.4.9). It never reads `hookmask` in that
   loop. In the hung process the loop was `ldr w8, [x20, #0x28]; cbnz w8`
   (`luaV_execute+11412` in the 5.5.1 build), offset 0x28 being `u.l.trap`.
   [C]
4. On AArch64 two plain stores to different cache lines (`hookmask` at offset
   196 of `lua_State`, `trap` at offset 40 of the `CallInfo`) may become
   visible to another core in either order. The running thread then takes
   the trap, enters `luaG_traceexec`, reads the old mask (0), takes the trap
   for a spurious one, clears it and returns (`ldebug.c:933-936` in 5.4.9,
   `941-944` in 5.5.1). [C for the effect, see the evidence below; the CPU's
   reordering itself is inferred]
5. The watchdog armed only once: `xlua_core_watch` did nothing once `stop`
   was set. The loop then ran forever with `stop` set and the mask armed.
   [C]

`volatile` (the fields are `volatile l_signalT`, `lstate.h:184` and `331` in
5.4.9) keeps the compiler from reordering or caching them but gives no
ordering between cores. [C]

## Evidence

- **State of a hung process** (harness dump, every harness hang):
  `entry_active=1 stop=11` (time), `hook=set hookmask=8` (`LUA_MASKCOUNT`)
  `hookcount=1 basehookcount=1`, the looping frame `trap=0`. Arming the hook
  once more from the monitor thread ended the call every time. [C]
- **Stacks** (`sample`, both harness and Mono): the Lua thread entirely in
  `luaV_execute` under `lua_pcall` (from `DoString` in Mono); the watchdog
  thread asleep in `usleep` (harness) or `Monitor.Wait` (`LuaWatchdog`), not
  spinning on the lock. In the hung managed suite the loop was the
  coroutine variant of the new case (`csharp_function_wrap` → `lua_pcall`
  → `core_auxwrap` → `lua_resume` → `luaV_execute`). [C]
- **Rates before the fix** (main 7ff004a):

  | runner | Lua | limit / poll | hangs |
  |---|---|---|---|
  | harness | 5.5.1 | 2 ms / 0.5 ms | 8 of 8 runs; hangs after 324 to 2,916 calls |
  | harness | 5.5.1 | 2 ms / 0.5 ms, 6 busy processes | 1 of 8 runs |
  | harness | 5.5.1 | 50 ms / 12.5 ms | 4 of 4 runs; after 257 to 676 calls |
  | harness | 5.4.9 | 2 ms / 0.5 ms, with and without load | 16 of 16 runs; after 1 to 5,100 calls |
  | harness | 5.4.9 | 50 ms / 12.5 ms | 3 of 4 runs |
  | `ManagedRepro` | 5.5.1 and 5.4.9 | 50 ms (`LuaWatchdog`) | 8 of 16 runs of 400 calls, with and without the hardening defines |
  | `ManagedRepro` | 5.5.1 and 5.4.9 | 5 ms | 12 of 12 runs, after 7 to 436 calls |
  | `ManagedRepro` | 5.5.1 and 5.4.9 | 1 ms | 4 of 44 runs of 1,500 or 3,000 calls |
  | `native_regression.py core_watchdog_race` | both | 1 ms / 0.25 ms | 6 of 6 runs, after 24 to 355 calls |
  | managed suite with the new `CaseLuaOptionsTimeLimit` loop | both | 5 ms | 2 of 2 runs hung (killed after 600 s) |

  About one call in 300 to 2,000 lost its stop; under CPU load it was rarer
  on 5.5.1, and with `LuaWatchdog`'s 1 ms floor on the poll it was rarer at
  a 1 ms limit than at 2 or 5 ms. With the managed suite's 50 ms case
  running a few times per suite run, a hang per run is rare, which matches
  one hang and a passing rerun. The same harness in the Linux arm64
  container (ASan build, about 40,000 calls) lost no stop; only macOS on
  Apple silicon showed it. [C]
- **Attribution.** Two variants of the fix, each alone: only the ordering
  (mask, fence, traps) and only the re-arming. Each ran about 100,000
  calls per Lua version in the same configurations without a hang. The
  ordering alone stopping the hangs confirms the reordering of step 4. [C]

## Fix

`arm_stop_remote` in `build/xlua_core_limits.c`, used by `xlua_core_watch`
and `xlua_core_interrupt`:

1. Store `hook`, the counts and `hookmask` first, then a full fence
   (`__atomic_thread_fence(__ATOMIC_SEQ_CST)`, `MemoryBarrier()` on MSVC,
   nothing on single-threaded WebGL), then `lua_sethook`, which stores the
   same values again and sets the traps. A thread that sees a trap from this
   arming also sees the mask. [C]
2. `stop_pending` stays set until the stop hook fires on the running thread.
   While it is set, every watchdog poll (and a further `Interrupt`) arms
   again. This covers what the fence cannot: the running thread's loads are
   not ordered by an acquire (Lua's VM is unchanged), so in principle a
   stale mask is still possible. [C for the mechanism; how often it is
   needed is not measured]

After the fix, without a hang: about 165,000 harness calls (both versions,
with and without load, 2 ms and 50 ms limits), 20 runs of
`core_watchdog_race`, 72 `ManagedRepro` runs (50 ms and 1 ms, with and
without load and the hardening defines), and the managed suite: reflection,
generated and minimized generated code on 5.5.1, reflection and generated
code on 5.4.9, the hardening defines on 5.5.1 and reflection on 5.3.6, with
a first version of the new loop (1,500 calls at 1 ms); the final loop (400
calls at 5 ms) in reflection mode, twice per version. [C]

## Second finding: the frame walk read freed frames

`settraps` walks `L->ci` and its `previous` links from the watchdog thread.
The running thread frees `CallInfo` blocks on its own, without the core
lock, when a caught error shrinks the list (`luaD_shrinkstack` from
`luaD_pcall`, `ldo.c:989` in 5.4.9 and `1096` in 5.5.1; `luaE_shrinkCI`,
`lstate.c:143` and `109`) and during garbage collection (`lgc.c:648` and
`710`).

- ThreadSanitizer (Linux arm64 container `xlua-analysis:26.04`, clang,
  harness variant `while true do pcall(f) end` where `f` catches an
  `error`) reported a heap-use-after-free: the watchdog's `settraps`
  (`ldebug.c:117`) read a frame that `luaE_shrinkCI` had freed from
  `luaD_pcall` (`ldo.c:1096`) on Lua 5.5.1. One report in a 10-second run of
  that variant; ASan in the same container saw none in about 42,000 calls.
  [C]
- Fix: `xlua_core_alloc` frees a block of `sizeof(CallInfo)` through
  `xlua_core_free_frame`, under the core lock, while an outermost call with
  limits runs. A walk then ends before the free or starts after it from the
  lowered `L->ci`, below every freed frame. Cost: one compare on every free,
  and an uncontended lock on the frees of that size (64 bytes on 64-bit
  5.5.1) during limited calls. A second TSan run (four times as many calls of
  that variant) reported no use after free. [C]

## Still open: the walk reads frames the running thread is writing

TSan still reports data races, all on the hook and frame fields: `trap`,
`hookmask`, `hookcount` (`settraps`, `lua_sethook`, `luaG_traceexec`,
`luaD_hook`), a frame's `callstatus` while `prepCallInfo` sets it
(`ldo.c:635` in 5.5.1), and the `previous` link of a frame that
`luaE_extendCI` has just allocated. The first group is what the fix orders
by hand. The other two mean the watchdog can read a frame that is being
reused or published: on AArch64 it could see a new frame's `previous`
before it is written, or take a frame now used by a C function for a Lua
one and write its trap flag over the C frame's saved error function. [C for
the reported races; U for any visible effect: no run has shown one]

Without patching Lua, the only way to make the walk race-free is to run it
on the hooked thread, as `lua.c` does from a signal handler, which the
design rules out for Mono and IL2CPP players. A small patch to Lua's VM
(also check `L->hookmask` where `updatetrap` reloads the trap flag) would
let the watchdog store only the mask and never touch a frame, at the cost of
one more load per jump; it would have to be measured. Left to the owner
(core-luascript.md, section 9).

## Sources

- Lua 5.4.9 and 5.5.1 sources in `build/lua-5.4.9`, `build/lua-5.5.1`
  (lines above). [C]
- Arm, "Arm Architecture Reference Manual for A-profile", the memory model
  chapter (B2): stores to different locations are not ordered without a
  barrier, and a control dependency does not order a later load. [S]
- Lua's `lua.c` uses `lua_sethook` from a SIGINT handler (`lua.c:63-79` in
  5.4.9), the signal case the comment covers. [C]
