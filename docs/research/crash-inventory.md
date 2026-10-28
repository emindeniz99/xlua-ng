# Crash inventory: LuaEnv and LuaScript (2026-10-19)

Every known or likely way a Lua script or ordinary C# use of xLua can crash
the process: native undefined behaviour, use after free, stack overflow, Lua
API calls that raise outside a protected call, thread misuse. For each: how it
fails, its status, a reproduction where one is cheap, the cost of a fix and a
proposal. The fixes made with this note are listed at the end.

Marks: [C] confirmed here by a test, a reproduction or the code at the cited
line; [S] from a secondary source (another note, an upstream report); [U]
unconfirmed (inferred from the code, not observed).

## Method and sources

- Setup: macOS 27 arm64 (Apple silicon), Apple clang, Mono 6.14.1, .NET SDK
  10.0.401. Host plugins built from this branch with `-DBUILD_SILICON=ON`
  for Lua 5.3.6, 5.5.1 and LuaJIT 2.1 (2a8e017e).
- Documents read: [bridge-audit.md](bridge-audit.md),
  [bridge-hardening.md](../design/bridge-hardening.md),
  [backlog.md](../design/backlog.md), [UPSTREAM.md](../../UPSTREAM.md),
  [Security.md](../../Assets/XLua/Doc/Security.md), the research notes that
  mention crashes ([apicheck-runs.md](apicheck-runs.md),
  [c17-setup.md](c17-setup.md), [watchdog-lost-stop.md](watchdog-lost-stop.md),
  [core-stop-latency.md](core-stop-latency.md), [lua-memory.md](lua-memory.md),
  [focus-creative-games.md](focus-creative-games.md)), the fuzz and sanitizer
  notes in [Test/README.md](../../Test/README.md) and
  [core-luascript.md](../design/core-luascript.md).
- Code read: every Lua API import of `LuaDLLBridge.cs` and `Core/LuaDLL.cs`
  checked for "can it raise" (an allocation can raise a memory error; a
  metamethod can raise anything; `lua_next` raises on a key not in the
  table; a GC step can run finalizers), and every caller of the raising ones
  for whether a protected call surrounds it; `build/xlua.c`'s functions that
  read userdata; the delegate, interface and hotfix bridges.
- Reproductions: `Test/Native/CrashRepro.cs`, run by
  `Test/Native/crash_repro.py`, one process per case with a 30 s timeout and
  the process group killed after it. Results below are "before" (main at
  6b82489c plus the #999 fix) and "after" (this branch). Crashing cases also
  ran in the managed suite where the suite covers them.
- Upstream report of the #999 crash:
  https://github.com/Tencent/xLua/issues/999 ("pcall_prepare" in a generated
  `__Gen_Delegate_Imp` when stopping Play, Unity 2020.3; no further detail).

## Summary table

Class: (a) plainly wrong and cheap: fixed now; (b) a cost on a hot path, or a
change of behaviour users may depend on: follows the build type or is an
owner question; (c) by design: documented.

| # | Way to crash | Runtime | How it fails | Status | Fix cost | Class |
|---|---|---|---|---|---|---|
| 1 | Delegate or hotfix bridge called after `LuaEnv.Dispose` ([#999](https://github.com/Tencent/xLua/issues/999)) | all | NULL state to `pcall_prepare`: SIGSEGV | **fixed** here | one comparison per call, not measurable | (a) |
| 2 | `LuaEnv.Dispose` from a C# function Lua called (also in a coroutine or `__gc`) | all | `lua_close` under the running call: SIGABRT | **fixed** here | one `lua_getstack` per Dispose | (a) |
| 3 | `LuaTable.ForEach` / `GetKeys` after the table changed | all | `lua_next` raises "invalid key to 'next'" with no handler: panic | **fixed** here | +22 ns per element (pcall) | (a) |
| 4 | `__len` that raises, for `LuaTable.Length`, `AddSearcher` and tables converted to `T[]` / `List<T>` | 5.3+ | luaL_len raises with no handler (panic) or across the caster's managed frames | **fixed** here | none without `__len` | (a) |
| 5 | Two threads in one `LuaEnv` without `THREAD_SAFE` | all | heap corruption: SIGABRT, or wrong errors | **checked** in debug builds (2026-10-28) | about 1 to 2 ns per call into Lua where checked | (b) |
| 6 | A C# finalizer that calls Lua (a second thread) | all | as 5: SIGABRT | **checked** in debug builds (2026-10-28) | as 5 | (b) |
| 7 | Deep C# ↔ Lua recursion | LuaJIT | native stack overflow at about 10,240 levels: SIGSEGV | **fixed** (2026-10-28) | about 2.5 ns per C# call on LuaJIT | (b) |
| 8 | Errors in `__gc` on Lua 5.3 | 5.3 | propagate from a GC step: panic in an unprotected C# call (`FullGc`, any allocating push) | **fixed** (2026-10-28, owner): written to stderr and dropped, as 5.4 and 5.5 warn | none (an error path) | (b) |
| 9 | The panic handler throws a C# exception through native frames | all | undefined behaviour; Mono unwinds, IL2CPP need not | open | none | (b) |
| 10 | Struct accessors read the 8-byte header of any full userdata, whatever its size | all (mostly LuaJIT) | heap over-read; a write if the bytes look like a header | **fixed** (2026-10-28, owner): the length is checked first, in every build | +0.5 ns per accessor; a C# struct argument one native call instead of two | (b) |
| 11 | Old leak-checker walk `xlua_report_object_relationship` ([#1149](https://github.com/Tencent/xLua/issues/1149)) | 5.3+ | use after free of objects swept during the walk (reproduced under ASan on 2026-10-28) | **fixed** (2026-10-28, owner) | none (diagnostic path only) | (b) |
| 12 | A disposed `LuaBase` used while its env lives (released reference reused) | all | wrong function or table, not a crash | **fixed** (2026-10-28, owner): `ObjectDisposedException` | one comparison per call, within the noise | (b) |
| 13 | Foreign `self` (`obj.Method(other)`) without `XLUA_CHECK_OBJECTS` | all | wrong object or a Lua error, no crash | by design, opt-in check | +10 to 13 ns per instance call | (b) |
| 14 | Crafted binary chunks (`load` with mode `"b"`) | PUC Lua | memory corruption | by design | n/a | (c) |
| 15 | C# stack overflow in a C# method Lua called | all | SIGSEGV, uncatchable in .NET | by design | n/a | (c) |
| 16 | Memory exhaustion without `LuaOptions.MemoryLimit` | all | the host runs out of memory | by design | `MemoryLimit` exists | (c) |
| 17 | Allocation failure inside a C# callback without a limit (real OOM) | all | longjmp across managed frames | by design | n/a | (c) |
| 18 | `debug` library tricks: replaced C closure upvalue, a `BaseType` cycle, `registry[8]` | all | wrong function [U]; a hang in C [C]; int64 checks change | open, listed in the audit | n/a | (c) |
| 19 | LuaJIT FFI (`LuaLibs.Ffi`) | LuaJIT | arbitrary memory access by design; ARM64 callback argument defect | by design | n/a | (c) |
| 20 | Lua errors through C# frames on IL2CPP / Win64 LuaJIT SEH | all | undefined behaviour | avoided by design; Win64 untested | n/a | (c) |
| 21 | Fixed earlier: stack overflow of pushes (#1153), NULL C function (`debug.setupvalue`), LuaJIT int64 header, `xlua.cast`, negative `xlua_newstruct`, float casts, watchdog races | — | see "Fixed earlier" | fixed | — | — |
| 22 | `debug.getregistry`: a script replaces an entry xLua reads (a metatable reference, the userdata cache, `registry[8]` on LuaJIT) | all | a number or string read as a table by `lua_setmetatable`, `lua_rawgeti`: SIGSEGV | by design, found by the bridge fuzzer | one `lua_type` check per push | (c) |
| 23 | `debug.setmetatable` on an io file, then a call of it that LuaJIT's JIT records | LuaJIT | NULL read in `lj_record_mm_lookup` | LuaJIT's own assumption | n/a | (c) |
| 24 | `debug.traceback(msg, level)` with a level near `INT_MIN` or `INT_MAX` | PUC Lua | signed overflow in `luaL_traceback` (undefined behaviour; no failure seen) | open | two lines in vendored `ldblib.c` or `lauxlib.c` | (b) |
| 25 | `LuaEnv.AddLoader` after a script replaced `package` | all | panic (item 9) | **fixed** here | none (a cold path) | (a) |
| 26 | A host function's type error for an argument past `nargs` + 20 | Lua 5.4, 5.5 (LuaScript) | Lua API check failure in builds with `XLUA_APICHECK` | **fixed** here | none (an error path) | (a) |
| 27 | A new metatable for an all-weak (`__mode = "kv"`) table during a GC cycle, from any script | Lua 5.3 | heap use after free (upstream Lua bug, fixed in 5.4) | **fixed** here (vendored patch) | none measurable | (a) |
| 28 | A light userdata with the int64 metatable (`debug.setmetatable`) read as an int64 | LuaJIT | read of 16 bytes at the address it holds | **fixed** here | one `lua_type` per int64 check | (a) |
| 29 | LuaEnv's message handler after a script replaced `debug` | all; LuaJIT crashes | "error in error handling"; on LuaJIT with a hook that makes a protected call, C stack overflow | **fixed** here | none (an error path) | (a) |

## The fixes made here

### 1. Bridges of a disposed LuaEnv ([#999](https://github.com/Tencent/xLua/issues/999))

**Cause [C].** Generated delegate and hotfix bridges read the state with
`BindingApi.RawState` (`Assets/XLua/Src/Binding.cs:165` before this change:
`return o.Env.rawL`), which skipped the disposed check; `LuaEnv.Dispose` sets
`rawL` to zero (`LuaEnv.cs`, `Dispose(bool)`), so the next call passed NULL
to `pcall_prepare` (`build/xlua.c`, `pcall_prepare`). Emitted bridges
(`CodeEmit.cs`, the `_L` getter), reflection bridges (`DelegateBridge.cs`,
`GenericDelegateBridge.cs`), interface bridges (`BindingApi.State`),
`LuaTable` and `LuaFunction` read `LuaEnv.L`, which threw
`InvalidOperationException`.

**How a bridge reaches a disposed env [C].** `Dispose` refuses while a
delegate bridge is reachable (`ObjectTranslator.AllDelegateBridgeReleased`,
`ObjectTranslator.cs:759`, short weak references at `:678`). Two ordinary
paths get past it:

- the holder releases the bridge itself, as code that frees Lua callbacks
  eagerly does: `((IDisposable)callback.Target).Dispose()` removes its entry
  (`ReleaseLuaBase`), and `Dispose` then succeeds while the delegate stays
  reachable;
- a C# finalizer keeps the delegate it held (stores it in a static): when
  the holder becomes unreachable the GC clears the bridge's short weak
  reference before the finalizers run, so `Dispose` succeeds, and the
  delegate lives on. In Unity, objects torn down when Play stops are a
  plausible source of such late calls [U]; the upstream report gives no
  caller.

Interface bridges are not tracked at all, so `Dispose` never refuses because
of them; they already threw. Hotfix bridges are reachable from static fields,
so `Dispose` refuses while a hotfix is installed; a hotfix bridge released by
hand crashed the same way.

**Reproduction [C].** `CaseBridgeAfterDisposeThrows`
(`Upstream.DisposedEnvBridges`, `luaCallCsUpstream.lua`) crashed the
`--source-gen` run on Lua 5.5.1 before the fix, with the stack of the
upstream report:

```
at XLua.LuaDLL.Bridge:pcall_prepare <0x00007>
at XLua.Generated.XLuaUnitTest_82b47ad5.Bridges:__Gen_Delegate_Imp10 <0x00047>
at Upstream.DisposedEnvBridges:Check <0x006e3>
```

and the finalizer path alone (`CheckResurrected`) crashed in
`__Gen_Delegate_Imp28`. The emitted bridges of the plain run threw
`InvalidOperationException`.

**Fix.** `LuaEnv.L` throws `ObjectDisposedException("LuaEnv", "this lua env
had disposed!")` from an out-of-line helper; `ObjectDisposedException` is an
`InvalidOperationException`, so existing `catch` blocks still match. The
source generator emits `BindingApi.State`, and `RawState`, kept for
bindings generated before, makes the same check. With it, every bridge kind
(generated plain and GCM, CodeEmit, reflection, interface, hotfix, `LuaTable`,
`LuaFunction`) throws the same exception. Found on the way and fixed:
`LuaEnv.Alias` after Dispose threw `NullReferenceException` (now the same
exception); a second `LuaEnv.Dispose()` threw from `FullGc` (now does
nothing, as `IDisposable` requires); the hotfix `InvokeSessionStart` path kept
`luaEnvLock` when it threw (`DelegateBridge.cs:162`).

**Cost [C].** Measured with `benchmark.py --modes sourcegen,emit --filter
cs_lua --runs 9`, two rounds, against the same tree with the old `RawState`
(Lua 5.5.1, Mono 6.14.1, macOS arm64, a machine shared with other test runs):
minimum of 18 runs for `cs_lua.delegate_int` 76.7 ns before, 76.3 ns after;
`delegate_string_arg` 134.4 / 135.2 ns; `delegate_struct_gcopt` 161.0 /
161.7 ns. The difference is below the noise: the emitted bridges, whose code
did not change, moved by up to 11 ns between the two trees. Medians were too
noisy on the loaded machine to use.

**Tests.** `CaseBridgeAfterDisposeThrows` in all four binding modes (plain,
`--source-gen`, `--source-gen-minimize`, `--source-gen-fallback`) on Lua
5.5.1 and LuaJIT; `HotfixFixture.CheckHotfixAfterDispose` in every
`hotfix_check.py` step.

### 2. Dispose from a running call

**Cause [C].** `LuaEnv.Dispose` called from a C# function that Lua called
closed the state (`lua_close`) while the call was on the C stack; returning
into Lua then used freed frames. The FAQ only said not to
(`Assets/XLua/Doc/Faq_EN.md`, "Why does LuaEnv.Dispose crash?").
`LuaScript.Dispose` already refuses (`Assets/XLua/Core/LuaScript.cs:168-181`,
its `depth` counter).

**Reproduction [C].** `crash_repro.py` cases `dispose_in_callback` and
`dispose_in_coroutine`: SIGABRT on Lua 5.5.1 and LuaJIT before; after,
`InvalidOperationException: a LuaEnv cannot be disposed while one of its
calls runs` reaches Lua as an error, on 5.3.6, 5.5.1 and LuaJIT.

**Fix.** `xlua_running` (`build/xlua.c:689`, `lua_getstack(L, 0, ...)` on
the main thread: 1 while any function runs, including `coroutine.resume` on
the way to a coroutine and a `__gc` handler during `lua_close`) and
`LuaEnv.CheckNotRunning` in both `Dispose` methods (`LuaEnv.cs:901`). Cost:
one native call per `Dispose`. A `LuaEnv` has no counter of its entries the
way `LuaScript` has, and adding one would cost every call; the native frame
check is free outside `Dispose`.

**Tests.** `CaseDisposeWhileRunningThrows` (direct and from a coroutine, the
env still works afterwards); `native_regression.py protected_helpers` checks
`xlua_running` at the top level, in a C function and in a coroutine.

### 3. Table traversal after a change

**Cause [C].** `LuaTable.ForEach`, `GetKeys` and `GetKeys<T>` called
`lua_next` directly (`LuaTable.cs`, before this change) and ran C# code
between two steps. An action that clears the current key and adds keys makes
Lua rehash and drop the key, and the next `lua_next` raises "invalid key to
'next'". No protected call surrounds it, so Lua calls its panic function:
xLua's (`StaticLuaCallbacks.Panic`, `StaticLuaCallbacks.cs:632`) throws a
C# exception through the native frames of `lua_next`.

**Reproduction [C].** `next_invalid_key`: before, `LuaException:
unprotected error in call to Lua API (invalid key to 'next')` on 5.5.1 and
LuaJIT; Mono unwound the native frames, which is not something IL2CPP
promises (item 9), and the Lua state's own bookkeeping for the error
(`nCcalls`, the `CallInfo` chain) was never restored [U].

**Fix.** `xlua_pnext` (`build/xlua.c:636`) runs the step under `lua_pcall`
and returns 1, 0 or -1 with the error object; `ForEach` and both `GetKeys`
throw it as a `LuaException` (`LuaTable.cs:237`, `:301`, `:330`).

**Cost [C].** Per element, measured by a loop of 300 traversals of a
1,000-key table (Lua 5.5.1, Mono): `lua_next` 27.0 ns, `xlua_pnext` 49.6 ns,
so about +22 ns per element of a `ForEach` whose own work (two value reads
and a delegate call) is larger. The table casters (`T[]`, `List<T>`,
`Dictionary`) keep `lua_next`: no C# code runs between their steps.

**Tests.** `CaseTableChangedDuringForEach`; `native_regression.py
protected_helpers` (a traversal, a missing key).

### 4. A raising `__len`

**Cause [C].** `xlua_objlen` was `luaL_len` on Lua 5.3+ (`build/xlua.c:284`),
which calls `__len` and raises for its error or for a result that is not an
integer. C# called it unprotected from `LuaTable.Length` and
`LuaEnv.AddSearcher` (panic, as in 3) and, inside a C# callback, from the
array and `List<T>` casters (`ObjectCasters.cs`, before this change), where
the error longjmp'd from the native call across the caster's managed frames
to the callback's `lua_pcall`. LuaJIT's `lua_objlen` reads the raw length and
never raised.

**Reproduction [C].** `len_metamethod_error_length`: before, `unprotected
error in call to Lua API ([string "chunk"]:1: len failed)` on 5.5.1;
`len_metamethod_error_array_arg` and `_list_arg`: the raw Lua error reached
`pcall` with no C# frame involved, which means the frames were skipped.

**Fix.** `xlua_plen` (`build/xlua.c:662`): the raw length when the value has
no `__len` (one `luaL_getmetafield`, no call), else `luaL_len` under
`lua_pcall`; an error becomes a `LuaException` in C#, which the callback
wrapper turns into a Lua error as for any C# exception. LuaJIT is unchanged.
The C# side no longer imports `xlua_objlen`; the bridge version is 109.
`xlua_plen` takes 13 ns per call for a plain table (P/Invoke included).

**Tests.** `CaseLengthMetamethodError` (Length, a length that is not an
integer, `int[]` and `List<int>` parameters, the raw length without `__len`);
`native_regression.py protected_helpers`.

## Open items and proposals

### 5, 6. Two threads in one LuaEnv; finalizers that call Lua

**How it fails [C].** `two_threads` (two threads calling one `LuaFunction`
for 5 s without `THREAD_SAFE`): SIGABRT on Lua 5.3.6 and 5.5.1 in one run, an
odd `LuaException` ("function :13", "Unknown Lua Error") in another, SIGABRT
on LuaJIT. `finalizer_thread_call` (a C# finalizer calls a `LuaFunction`
while the main thread runs Lua): SIGABRT on all three runtimes. `LuaEnv` is
not thread-safe without `THREAD_SAFE` (FAQ), and a finalizer is a thread the
user may not think of [S, Faq_EN.md].

**Cheap detection.** An owner-thread check: `LuaEnv` records
`Environment.CurrentManagedThreadId` at construction, and `LuaEnv.L` (every
C#→Lua entry: bridges, `LuaTable`, `LuaFunction`, `DoString`) compares it,
as `LuaScript.CheckThread` does (`LuaScript.cs:227`). It cannot see a C#
callback on the wrong thread, but a C# callback only runs on the thread that
is running Lua. Cost: a thread-static read and a compare, about 1 to 2 ns
per entry [U, not measured]. Under `THREAD_SAFE` the check must be off.
A failure on the finalizer thread ends the process with the exception's
message instead of corrupting the heap (an unhandled exception in a
finalizer terminates .NET and Mono processes) [S].

**Proposal (b).** A mistake-catching check, so it follows the build type
(AGENTS.md): on in the Editor, Development builds and `XLUA_GENERAL`, off in
release players and under `THREAD_SAFE`. The agent working on the build-type
checks (`XLUA_CHECK_OBJECTS` and the like) owns the defines; this note only
proposes the check. Owner question: also in release, or never?

**Decided 2026-10-28 (owner) and implemented.** One of the bridge checks: in
debug builds only, off under `THREAD_SAFE`; the owner is the creating thread;
a public hand-off; an `InvalidOperationException` that names both thread IDs
and the fixes. As implemented [C]:

- **The check.** `LuaEnv.L` (`Assets/XLua/Src/LuaEnv.cs:67`), which every
  C#→Lua entry reads (bridges through `BindingApi.State`, `LuaTable`,
  `LuaFunction`, `DoString`, `Tick`, `Dispose`), compares `ownerThread` with
  the calling thread's `Environment.CurrentManagedThreadId`, cached in a
  `[ThreadStatic]` field (`:104`), under
  `#if XLUA_BRIDGE_CHECKS && !THREAD_SAFE && !HOTFIX_ENABLE`; a mismatch calls
  the out-of-line `CheckOwnerThread` (`:138`), which fills the cache on a
  thread's first check, so the getter stays small.
  `HOTFIX_ENABLE` is exempt as well as `THREAD_SAFE`: both take `luaEnvLock`
  around every entry (`#if THREAD_SAFE || HOTFIX_ENABLE` in `LuaEnv.cs`,
  `LuaTable.cs`, `LuaFunction.cs`, `GenericDelegateBridge.cs`, `CodeEmit.cs`
  and the source generator's `Bridges.cs`; `LuaDLLBridge.cs:465` shares the
  string buffer only without both), so a hotfixed method may run on any
  thread, as upstream allows. This differs from the brief's
  `!THREAD_SAFE` alone. **Owner decision of 2026-10-28:** the check stays
  off under `HOTFIX_ENABLE`.
- **Owner and hand-off.** `ownerThread` is set in the constructor before the
  state exists (`LuaEnv.cs:216`); since the second decision below, only for
  the constructor's own run. `LuaEnv.SetOwnerThread()` (`:117`) makes
  the calling thread the owner; `LuaScript.SetOwnerThread()`
  (`Assets/XLua/Core/LuaScript.cs:236`) is new with the same name, since
  `LuaScript`'s `ownerThread` was `readonly` and had no hand-off. Neither
  refuses while the other thread runs a call: the caller must hand over once
  the old owner is done, which also orders the write before the old owner's
  last use. `LuaOptions.CheckThread = false`, which only `LuaScript` read,
  turns the check off for one `LuaEnv` too, so both classes share the option.
  **Owner decision of 2026-10-28:** `CheckThread` applies to both
  `LuaScript` and `LuaEnv`, as a per-instance option passed to the
  constructor, not a global.
- **Any thread, as `LuaScript`:** `Interrupt()` (reads `rawL`), and the
  statistics getters (`MemoryUsed`, `MemoryPeak`, `LastInstructionCount`, the
  limits' getters) through `StatsState` (`:124`), which checks only for
  disposal. The setters stay checked.
- **Exemptions checked.** Dispose from the owner: `FullGc` and `Dispose(bool)`
  read `L` on the owner. The finalizers: `~LuaBase` (`LuaBase.cs:45`) calls
  `Dispose(false)`, which only calls `equeueGCAction` (`:84`; a lock-free push,
  `LuaEnv.cs:1091`) and never reads `L`; it is the only finalizer in
  `Assets/XLua/Src` and `Core` besides `~LuaScript`, which closes the state
  without the thread check (`LuaScript.cs:155`). Delegate, interface and
  generated bridges derive from `LuaBase` and do not override it. Hotfix
  bridges read `rawL` in `InvokeSessionStart` (`DelegateBridge.cs:165`), and
  exist only under `HOTFIX_ENABLE`. Unity calls `Update`, coroutines and
  `UnitySynchronizationContext` continuations on the main thread, which
  created the env in the usual setup; an env created elsewhere needs the
  hand-off [S: the Unity script lifecycle].
- **Found on the way.** Two suite fixtures used one env from a second thread
  without `THREAD_SAFE`, one thread at a time (`Coverage.Bridges.Threads`
  without `THREAD_SAFE`, `DisposedEnvBridges.CheckResurrected`); they now hand the
  env over and back with `SetOwnerThread`.

**Cost [C].** Where the check runs (debug builds), a thread-static read
and a compare per read of `L`. The thread's id is cached in a `[ThreadStatic]`
field: `Environment.CurrentManagedThreadId` costs about 6 ns on Mono 6.14.1
(it is `Thread.CurrentThread.ManagedThreadId`), the cached read about 2 ns (a
loop of 50 million calls through a delegate: 7.35 and 3.37 ns, the empty
delegate call 1.5 ns). `benchmark.py --modes sourcegen,emit --filter cs_lua
--runs 9`, Lua 5.5.1 Release plugin, `XLUA_GENERAL` (checks on), the base tree
(`b3adc8e8`) against this one, three interleaved rounds, lowest median in ns
per call, on macOS 27 arm64 at a load average of 25 to 42 from other test
runs:

| Case | generated, before → after | emitted (the Editor's path), before → after |
|---|---|---|
| `delegate_int` | 77.9 → 78.2 | 81.9 → 85.9 |
| `interface_method` | 202.2 → 198.8 | 204.5 → 211.2 |
| `interface_property_get` | 129.3 → 128.0 | 133.1 → 141.1 |
| `luafunction_call` | 314.5 → 313.1 | 320.1 → 322.1 |
| `luafunction_func` | 224.5 → 223.8 | 222.0 → 228.3 |

Generated bridges read `L` once per call (`BindingApi.State`), and their
difference is within the noise; CodeEmit's bridges read it several times
(the `_L` getter per use), about 4 to 8 ns more per call. The first version
compared `Environment.CurrentManagedThreadId` directly: generated
`delegate_int` 77.0 → 83.5, `interface_method` 204.9 → 214.0,
`luafunction_func` 225.4 → 237.3, which led to the cache. Release players
compile no check and pay nothing. IL2CPP, where a thread-static read is a
call into the runtime, was not measured.

**Tests.** `CaseOwnerThreadCheck` (`luaCallCsUpstream.lua`, fixtures
`Upstream.OwnerThread` in `UpstreamForLuaCallCS.cs`) in both states:
with the check, a call from another thread through a delegate, a configured
delegate, an interface bridge, `LuaFunction.Call`, `LuaTable.Get`,
`DoString`, `NewTable` and `Dispose` throws `InvalidOperationException`
naming both thread IDs, `SetOwnerThread()`, `THREAD_SAFE` and finalizers,
and leaves the env usable; the hand-off to another thread and back works and
refuses the creator in between; xLua's own finalizers queue releases that
`Tick` frees; a finalizer of the user's that calls Lua gets the exception;
`CheckThread = false` and the statistics from another thread work. Without
the check (`THREAD_SAFE`, or `XLUA_NO_BRIDGE_CHECKS`) the same calls must
succeed. `TestThreadCheck` (`Test/Core/CoreTests.cs`) checks
`LuaScript.SetOwnerThread`. `crash_repro.py`: `two_threads` and
`finalizer_thread_call` end in the exception on 5.3.6, 5.5.1 and LuaJIT
(below).

**Decided 2026-10-28 (owner, Q9, second decision): the owner is the first
thread that uses the env after the constructor, not the thread that created
it.** This applies in every preset, with no flag. Unity may run field
initializers and static constructors on a loading thread. Upstream's example
`Assets/XLua/Examples/02_U3DScripting/LuaBehaviour.cs:30` has
`static LuaEnv luaEnv = new LuaEnv();`, so the first rule would have refused
that env on the main thread in the Editor and in Development builds.
Implemented [C]:

- **`LuaEnv`.** The constructor sets `ownerThread` to the creating thread for
  its own run: its init chunk, the initers and `OpenLibraries` read `L`. Its
  last statement sets it to `Unclaimed` (-1); managed thread IDs are positive,
  and 0 is the cache's "not read yet". The getter `L` is unchanged: the cached
  thread ID is compared with `ownerThread`, and a mismatch goes to the
  out-of-line `CheckOwnerThread`. There, an `Unclaimed` env is claimed with
  `Interlocked.CompareExchange(ref ownerThread, thread, Unclaimed)`. The
  winner returns. The loser of a race, and every later caller from another
  thread, gets the exception as before. `SetOwnerThread()` (an
  `Interlocked.Exchange`) still hands the env over, and also works before any
  use. `Dispose()` reads `L` through `FullGc`, so a first-use `Dispose` claims
  the env, and a `Dispose` from a non-owner after the claim is refused.
- **`LuaScript` follows the same rule**, for consistency: the two classes
  share `LuaOptions.CheckThread` and `SetOwnerThread()`, and a loader thread
  that makes scripts for the main thread is the same case. `CheckThread()`
  compares `Environment.CurrentManagedThreadId` as before (`LuaScript` has no
  thread-ID cache, and none was added), and claims or refuses in
  `ClaimOrRefuse`.
- **What the rule cannot tell:** whether the first user is the intended
  owner. A finalizer or a `Task.Run` continuation that happens to touch an
  unused env first takes it, and the main thread is then refused with the
  usual message. An env that its creator also uses (the usual case, such as a
  `DoString` right after `new LuaEnv()`) is claimed by the creator, as before.
  A subclass constructor runs after the base constructor has released the
  env, so its first use of `L` claims it for the creating thread.
- **Cost.** The fast path is the same comparison of the cached thread ID with
  a field. The claim runs once per env, in the method that already handled a
  thread's first check. Measured on 2026-10-28 with
  `benchmark.py --modes sourcegen,emit --filter cs_lua --runs 9`, Lua 5.5.1
  Release plugin, `XLUA_GENERAL` (checks on), this tree with `LuaEnv.cs` of
  the base (`3042ef63`) against the new one, three interleaved rounds,
  lowest median in ns per call. macOS 27 arm64 (M2 Pro), at a load average
  of 55 to 83 from other agents' runs:

  | Case | generated, before → after | emitted, before → after |
  |---|---|---|
  | `delegate_int` | 87.3 → 78.6 | 87.3 → 84.6 |
  | `delegate_string_arg` | 139.8 → 137.9 | 133.5 → 139.8 |
  | `interface_method` | 205.8 → 217.5 | 209.1 → 214.8 |
  | `interface_property_get` | 133.7 → 130.9 | 134.4 → 138.6 |
  | `luafunction_call` | 330.8 → 318.7 | 327.8 → 326.6 |
  | `luafunction_func` | 229.5 → 230.4 | 228.2 → 228.8 |

  The differences run from -10% to +6% in both directions, which is the noise
  of a loaded machine. No cost could be measured; the code on the path that
  succeeds is the same as before. [C]
- **Tests.** `Upstream.OwnerThread.FirstUse`, called from `CaseOwnerThreadCheck`:
  - an env made in a static constructor on a worker thread (upstream's
    example), then used on this thread, and refused from a second thread;
  - an env with `LuaOptions` made on one worker and first used on a second
    worker, which owns it: the test thread is refused, `Dispose` from the test
    thread is refused, and `SetOwnerThread` takes it back;
  - `Dispose` as the first use on another thread than the creator's;
  - `SetOwnerThread` before any use;
  - ten rounds of two threads racing for the first use (with the check
    only), where exactly one runs and one is refused.

  `TestThreadFirstUse` (`Test/Core/CoreTests.cs`) does the same for
  `LuaScript`. Both fail on the code before the change (2026-10-28,
  `before-551-minimal.log` and `before-core.log` in the run folder: "a
  LuaScript is used from thread 1 but belongs to thread 186").
- **Runs** (2026-10-28, macOS arm64, Mono 6.14.1): the full managed suite in
  its four passes on Lua 5.5.1 and LuaJIT, legacy and minimal, and on 5.5.1
  with `THREAD_SAFE`, where the race part is skipped; `run_core.py` with the
  core preset and with `--bridge`, on Mono and .NET; `crash_repro.py`
  (5.5.1): the same results as the table below. Not run: Unity editors and
  players, IL2CPP (where the claim's `Interlocked.CompareExchange` runs only
  in Development builds).

### 7. Deep C# ↔ Lua recursion on LuaJIT

**How it fails [C].** `deep_recursion_cs_lua` (a Lua function that calls a
C# method that calls the Lua function through `LuaFunction.Call`): PUC Lua
stops at `LUAI_MAXCCALLS` (200) with a "C stack overflow" error that reaches
C# as a `LuaException` (5.3.6, 5.5.1). LuaJIT has no such limit for
`lua_pcall` from C: the run reached depth 10,240 and then died with SIGSEGV
on the 8 MB main thread stack. A generated or delegate bridge recursion
behaves the same [U].

**Fix and cost.** A per-thread nesting counter in the C# function
trampolines of `build/xlua_core.c` (`managed_enter` / `managed_leave` exist
for core states), on LuaJIT only, raising Lua's "C stack overflow" before it
calls C# when the count passes 200: an increment, a decrement and a compare
per C# call on LuaJIT, about 1 ns [U]. Proposal (b): owner question, since
it touches every C# call on LuaJIT and caps a depth that LuaJIT users can
reach today (200 nested C#↔Lua levels).

**Decided 2026-10-28 (owner) and implemented:** a crash fix, no flag; the
same error at the same depth as PUC Lua. As implemented [C]:

- **Where.** In the two C# function trampolines (`csharp_function_wrap` and
  the GCM `csharp_function_wrapper_wrapper`), through `managed_enter` and
  `managed_leave` (`build/xlua_core.c:530`, `:547`), on LuaJIT only. Every
  call from Lua into C# passes one of them, whatever the binding mode, so the
  native side has one place; a C# counter would need every generated
  wrapper, `StaticLuaCallbacks` and the reflection paths.
- **The count.** A thread-local `int` (`_Thread_local`; `__declspec(thread)`
  under MSVC, `thread_local` if compiled as C++). The C stack that
  overflows is the thread's, and LuaJIT runs coroutines on the caller's C
  stack, so per thread is the measure that matters. A per-state count would
  need a field in LuaJIT's `lua_State` or `global_State` (a patch to the
  vendored tree) or a registry lookup per call. PUC Lua counts per state, so
  two envs nested on one thread can still go deeper there [U: from
  `ldo.c`, not tested]. **Owner decision of 2026-10-28:** LuaJIT's limit
  counts per thread, not per state.
- **The depth.** `deep_recursion_cs_lua` reached C# function 199 on Lua
  5.3.6 and 5.5.1 (the 199th's `lua_pcall` fails: the outer `DoString` plus
  199 nested calls reach `LUAI_MAXCCALLS`, 200). `XLUA_CSHARP_MAX_DEPTH` is
  199: the trampoline refuses a 200th C# function with
  `luaL_error(L, "C stack overflow")`, raised in the Lua function that called
  it, which the 199th function's `pcall` catches. The message and the depth
  match (the innermost `LuaException` is "C stack overflow" on all three).
  When C# functions already run on the thread (the managed suite calls its
  fixtures from Lua), LuaJIT stops that many levels earlier; the test runs
  on a thread of its own.
- **Error paths.** The trampoline decrements before it raises a deferred C#
  error; the refusal raises before the increment. A Lua error that crossed a
  trampoline without returning through it would leave the count high, but
  that is undefined behaviour already (item 17).

**Cost [C].** One thread-local address per trampoline call (on macOS one
`tlv_get_addr` call, whose result stays in a register across the C# call:
`objdump -d` of `csharp_function_wrap`), a compare, an increment and a
decrement. A C loop calling a function through such a wrapper in a shared
library (Apple clang 21, `-O2`, macOS 27 arm64): 1.55 ns without the counter,
4.04 ns with it, so about 2.5 ns per call. `benchmark.py --modes
sourcegen,emit --filter lua_cs --runs 9` with the base LuaJIT plugin and this
one (three rounds, lowest median, a machine loaded by other test runs):
`static_call` 57.1 → 58.1 ns generated and 67.4 → 67.4 emitted,
`instance_call` 95.3 → 98.6 and 95.9 → 94.1, `field_get` 97.8 → 100.9 and
98.0 → 106.7; the other cases moved between −4 and +10 ns in both
directions, so the cost is below what that benchmark resolves. Not measured:
Android, where the NDK emulates thread-local storage below API level 29
(`__emutls_get_address`, a call and a lookup per access) [S], Windows and
iOS. The counter costs nothing on PUC Lua, where it is not compiled.

**Tests.** `CaseDeepCSharpRecursion` (`Upstream.DeepRecursion`): on a thread
with a 64 MB stack, the recursion ends in "C stack overflow" with C#
function 199 the deepest, twice (the count is back after the error), and a
50-level recursion returns, on Lua 5.3.6, 5.5.1 and LuaJIT. Before the fix
the LuaJIT run crashes. `crash_repro.py deep_recursion_cs_lua` prints the
deepest C# call: 199 on all three.

### 8. `__gc` errors on Lua 5.3

**How it fails [C].** Lua 5.3 propagates an error raised by a `__gc`
metamethod from the GC step that ran it; Lua 5.4 and 5.5 turn it into a
warning, and LuaJIT 2.1 prints "ERROR in finalizer" and goes on.
`gc_error_fullgc`: `env.FullGc()` (also the first step of
`LuaEnv.Dispose()`) calls `lua_gc` unprotected, and on 5.3.6 it ended in
`unprotected error in call to Lua API (error in __gc metamethod ...)`.
`gc_error_alloc`: a GC step inside a protected call reported the finalizer's
error as the error of an unrelated `LuaTable.Set`; a step inside an
unprotected push (`lua_pushstring`, `lua_createtable`) panics like
`FullGc` [U for the push: not caught in the run]. A script reaches it with
`setmetatable({}, {__gc = function() error("x") end})`.

**Options.** (i) `FullGc`, `GC` and the collection in `Dispose` through a
protected call (a native helper; cheap, not on a hot path); this does not
cover GC steps in unprotected pushes. (ii) A vendored patch to Lua 5.3's
`lgc.c` that reports finalizer errors as 5.4 does instead of propagating
them (`LUA_PATCHES.md`); a change of 5.3 semantics. Proposal (b): owner
question; (i) alone is (a)-cheap but only half a fix.

**Decided 2026-10-28 (owner): option (ii), and done.** The vendored 5.3.6
`GCTM` (`build/lua-5.3.6/src/lgc.c`) pops a finalizer's error and goes on, as
5.4's does with `luaE_warnerror`, so every pending finalizer runs and nothing
is raised from a collection step, protected or not. 5.3 has no warning
system: the message goes to stderr in the text of 5.4's and 5.5's warning
function, `Lua warning: error in __gc (<message>)`, which Lua 5.5 prints under
xLua too (its `luaL_newstate` turns warnings on; 5.4's leaves them off and
drops the error) [C]. Recorded in [LUA_PATCHES.md](../../build/LUA_PATCHES.md)
next to the weak-table backport of item 27, which stays. Cost: none, an error
path. Tests, each failing before the patch [C]: `native_regression.py
gc_errors` (an unprotected `lua_gc` collect with raising finalizers: "PANIC:
unprotected error in call to Lua API (error in __gc metamethod (no message))"
and abort on the old 5.3.6; every finalizer runs after it, on all four
runtimes) and the managed `CaseGcErrorsAreWarnings` (`FullGc` twice and
`Dispose`, on the old 5.3.6 "FullGc threw LuaException: unprotected error ...").
Lua 5.3.4's official suite expects `collectgarbage()` to fail in `gc.lua` and
counts the failures in `api.lua`; `Test/Native/lua_tests.py` changes those
expectations for 5.3.6 (the fourth adjustment there).

### 9. The panic handler

`StaticLuaCallbacks.Panic` (`StaticLuaCallbacks.cs:632`, installed in
`LuaEnv`'s constructor, `LuaEnv.cs:166`) throws a C# `LuaException` from a
native callback; Lua calls it with no handler on the stack, so the exception
unwinds native Lua frames. Mono 6.14.1 on macOS unwound them in every run
above [C]; IL2CPP and CoreCLR do not promise it [S, core-luascript.md:886-888]
and the Lua state is left with its error bookkeeping undone [U]. After items 3
and 4 the known ways to reach it are a real allocation failure in an
unprotected call (no `MemoryLimit`) and item 8 (fixed on 2026-10-28). Options: keep it (Mono
"works"); or log the message and `Environment.FailFast`, a clean abort that
names the error. Proposal (b): owner question (it changes "an exception,
maybe recoverable on Mono" into "the process ends").

### 10. Struct accessors and foreign userdata

`accessor_field` (`build/xlua.c:1473`) checks the header (`fake_id == -1`,
`len >= offset + width`) of argument 1 but not that the userdata is at least
8 bytes long, so an accessor called with a smaller userdata (LuaJIT's
`newproxy()`, 0 bytes) reads past it; it writes only when those bytes happen
to look like a header [U, bridge-audit.md:1397-1400]. The opt-in
`XLUA_CHECK_STRUCT_ACCESSORS` (tag check, +11 to 13 ns) closes it.
A cheaper bound: require `css->len + 8 <= lua_rawlen(L, 1)`, which makes the
default accessors memory-safe for any userdata without the tag check, for
one `lua_rawlen` per access (about 1 to 2 ns on a 14 to 25 ns accessor [U]).
Proposal (b): owner question, or fold it into the build-type decision for
`XLUA_CHECK_STRUCT_ACCESSORS`.

**Decided 2026-10-28 (owner): a bounds check in every build, and done.**
The build-type check (`XLUA_BRIDGE_CHECKS`, the tag) stays as it is: it
tests the metatable, and a userdata that `debug.setmetatable` gave a struct's
metatable passes it, so the bounds check is needed with it too.

- `accessor_field` (`build/xlua.c`) requires `lua_rawlen(L, 1) >= 8 + offset +
  width` before it reads the header, the cheapest check that keeps the
  header and the field inside the block. It replaces the `lua_type` test,
  since a light userdata has length 0. A field inside the block of a
  userdata whose header claims more is still accessed, safely.
- `xlua.structclone` (`css_clone`) and the new export `xlua_tostructbuffer(L,
  idx, type_id)` take a struct only when `8 + len <= lua_rawlen` (`struct_block`).
- The pack and unpack helpers take a pointer and no `lua_State`, so C# could
  not check there: it called `xlua_gettypeid` and then `lua_touserdata`. C#
  (`ObjectTranslator`'s `decimal`) and the source generator's struct readers
  and writers (`Structs.cs`, `Get_` and `Update_`) now call
  `xlua_tostructbuffer`, which checks the type id and the length in one call
  and returns NULL otherwise; the helpers return 0 for NULL, which raises
  "unpack fail for T". Bridge version 111.

Cost, measured: `benchmark.py --modes sourcegen --define
XLUA_NO_BRIDGE_CHECKS --runs 9`, Lua 5.5.1 Release plugin, macOS 27 arm64
(M2 Pro, load average 13 to 40 from other jobs), Mono 6.14.1, lowest median
of three interleaved rounds per side, ns per operation:

| Case | before | after |
|---|---|---|
| `struct_accessor_get` | 13.6 | 14.1 |
| `struct_accessor_set` | 16.2 | 16.5 |
| `struct_gcopt_pass` (C# reads and pushes a `[GCOptimize]` struct) | 155.0 | 148.9 |
| `struct_gcopt_field_get` | 108.3 | 106.2 |
| `delegate_struct_gcopt` | 163.3 | 158.2 |

A native loop (`lua_pcall` of a Lua loop over the accessor, lowest of 30 runs
per side, six alternations) agrees: get 14.14 to 14.74 ns, set 16.26 to 16.79
ns, so about 0.5 ns per accessor, under the owner's limit of 3 ns. The C#
path's native part costs 2.8 ns more in a C loop (`xlua_gettypeid`,
`lua_touserdata` and an unpack 11.96 ns; `xlua_tostructbuffer` and an unpack
14.74 ns), but saves one P/Invoke, so the managed cases above got faster.

Tests [C]: `native_regression.py struct_bounds` (a 12-byte block whose
header claims 16 bytes, blocks of 0 to 7 bytes filled with 0xff, an exact
8-byte header, a light userdata, a number; the accessors, `structclone`,
`xlua_tostructbuffer` and the helpers given NULL). On the old library the
accessors read and wrote past the blocks and the check crashed (SIGSEGV on
macOS); under ASan the helpers given NULL failed first. `fuzz_struct.c` got
two kinds of foreign userdata for its accessor operation (a header that
claims more than the block, a block shorter than the header) and checks
`xlua_tostructbuffer`; its corpus has `short-block`. `fuzz_bridge.c`'s
stand-in for the C# struct path calls `xlua_tostructbuffer`.

### 11. The old walker ([#1149](https://github.com/Tencent/xLua/issues/1149))

Not reproduced. `xlua_report_object_relationship`
(`build/memory_leak_checker.c:183`) walks `G(L)->allgc` and, for each Lua
closure, pushes it and reads its upvalues, with no `lua_checkstack` and with
the collector running. A likely cause [U]: during the sweep phase `allgc`
still holds dead objects that are not swept yet, and the walk pushes a dead
closure and reads its upvalues, which newer and already swept `UpVal`
objects may no longer back (use after free). The callback can also reach C#,
which can run Lua and step the collector during the walk. Fix: skip objects
for which `isdead(G(L), p)` holds, call `lua_checkstack(L, 4)` once, and stop
the collector around the walk (`lua_gc` STOP/RESTART); cheap, diagnostic
path only, but no failing test can be written without driving the collector
into its sweep phase from a test (internal headers). Its replacement,
`LuaEnv.TakeMemorySnapshot()`, is protected and stops the collector
[C, lua-memory.md]. Proposal (b): apply that fix without a failing test, or
drop the old walker and `General/LuaMemoryLeakChecker` in favour of the
snapshot.

**Decided 2026-10-28 (owner): fix the old walker and keep it, and done.**
Both walks (`xlua_report_object_relationship`, `xlua_report_table_size`)
skip objects for which `isdead(G(L), p)` holds, stop the collector during the
walk (`lua_gc` `LUA_GCISRUNNING`, `LUA_GCSTOP`, and `LUA_GCRESTART` only if it
was running), and the relationship walk calls `lua_checkstack(L, 2)` once
(a closure takes two slots; without room it reports tables only). A callback
that runs a full collection itself (`LuaEnv.FullGc`) can still free objects
ahead of the walk; the comment in `memory_leak_checker.c` says so. Cost: none
outside the diagnostic walk.

Reproduced on the old walker [C], with a test that fails before the fix
(`profiler_smoke.py`, under `sanitize.sh`'s ASan and API checks, Linux
arm64): each round makes 1,500 tables whose key strings and values are newer
than the table and 1,500 closures whose upvalue box (Lua 5.4, 5.5) is newer
than the closure, with the collector stopped, then runs small collection
steps (one single step on 5.4, a step size of 800 bytes on 5.5, the smallest
multiplier on 5.3) and both walks after each, until the cycle ends (30 steps
on 5.3.6, 165 on 5.4.9, 59 on 5.5.1); the callbacks read every key and source
name. The old walker: heap use after free in `aux_upvalue` (`lua_getupvalue`
on a dead closure whose upvalue box was swept, `lapi.c:1375` in 5.4.9,
`lapi.c:1390` in 5.5.1) and in the callback's read of a swept key string
(5.3.6). The second part calls the walk from a C function that filled its 20
free slots, which the API checks stop on the old walker (two unchecked
pushes past `ci->top`). The fixed walker passes on 5.3.6, 5.4.9 and 5.5.1, and
the check also tests that the collector's state comes back (running or
stopped).

### 12. A disposed LuaBase while the env lives

`LuaFunction.Dispose()` or a finalized bridge releases its registry
reference (`luaL_unref`); a later call through the same object reads
whatever the slot now holds: another function, a free-list number ("attempt
to call a number value") or nothing [U]. Not a crash, but a call of the
wrong function. A `disposed` check in the call paths costs one more
comparison per call. Proposal (b): owner question; the finalizer path (a
resurrected delegate) is rare, the explicit one is a user error.

**Decided 2026-10-28 (owner): every use after `Dispose()` throws
`ObjectDisposedException`, and done.** `LuaBase.State` (`LuaBase.cs`) checks
the `disposed` flag, then returns `LuaEnv.L` (which checks the env); every
path takes the state through it: `LuaFunction`, `LuaTable` and
`GenericDelegateBridge` methods, CodeEmit's bridges (`_L`), generated bridges
(`BindingApi.State` and the old `RawState`), `Equals` and `GetHashCode`; the
hotfix session (`DelegateBridge.InvokeSessionStart`) checks it too, and
`LuaBase.push` (a `LuaTable` or `LuaFunction` passed to Lua, as an argument,
a table value or `DoString`'s env) throws before it reads the reference.
`ToString` and a second `Dispose` still work. The exception names the type
("this LuaFunction has been disposed; its Lua reference may belong to another
object now").

Cost: one field comparison per call. `benchmark.py --modes sourcegen
--define XLUA_NO_BRIDGE_CHECKS --runs 9`, the setup of item 10, lowest median
of three interleaved rounds, ns per operation, before and after:
`delegate_int` 75.4 and 75.9, `luafunction_call` 309.6 and 312.2,
`luafunction_func` 223.3 and 222.9, `interface_method` 199.5 and 200.4,
`interface_property_get` 127.4 and 127.5, `delegate_string_arg` 130.6 and
135.0; `LuaTable` `get_int_key` 191.3 and 190.8, `set_string_key` 252.5 and
250.9 (two rounds). Within the noise (about ±4 ns on this machine).

Test [C]: `CaseDisposedLuaBaseThrows` (`Upstream.DisposedLuaBase`): a
`LuaFunction`, a `LuaTable`, a delegate and an interface bridge are disposed,
new references are taken until the released numbers are reused (checked
through `ToString`), and 24 uses must throw. On the old runtime the calls
ran the newer object's code ("attempt to call a table value"), `Get`, `Set`
and the others read and wrote the newer table without an error, and the
interface bridge indexed a function; with the fix all throw and the newer
objects are untouched.

### 13. Foreign `self`

`crash_repro.py` `foreign_self_file` and `foreign_self_tag` (an `io` file or
a thread as `self` of a `StringBuilder` method): "invalid arguments" errors,
no crash [C]. The pool lookup is bounds-checked [C, bridge-audit.md item 2].
`XLUA_CHECK_OBJECTS` makes the bridge reject the foreign value
(+10 to 13 ns per instance call). Proposal: part of the build-type decision
of the other agent (on in the Editor and Development builds as a
mistake-catching check); no crash to fix.

## By design (c)

- **14. Binary chunks.** PUC Lua does not verify bytecode; `load` with mode
  `"b"` or `"bt"` lets a script corrupt memory [S, Security.md]. Core refuses
  binary chunks (`xlua_core_nobc.c`), and the minimal preset plans text-only
  loading (bridge-audit.md item 13).
- **15. C# stack overflow.** `csharp_stack_overflow` (a C# method Lua calls
  recurses forever): SIGSEGV on every runtime [C]; .NET cannot catch it.
- **16. Memory exhaustion.** Without `LuaOptions.MemoryLimit` a script can
  allocate until the host fails: a `string.rep('x', 1 << 40)` run on Lua
  5.5.1 was still filling memory when the 30 s timeout killed it [C]; the
  case is not in `CrashRepro.cs` for that reason. `MemoryLimit` refuses
  such allocations cleanly (core-luascript.md, section 3.4).
- **17. Allocation failure inside C# callbacks without a limit.** Generated
  and reflection wrappers push results with raising API calls; a real
  out-of-memory there longjmps across managed frames. With `MemoryLimit` the
  overdraft rule grants the allocation and raises after C# returns
  (`build/xlua_core.c`, `managed_enter` / `managed_leave`, `:506`) [C].
- **18. `debug` tricks.** With the full `debug` library a script can replace a
  C# function closure's upvalue with another C function (it then runs with
  the wrong upvalues [U]; the NULL case is fixed), plant a cycle in a
  `BaseType` chain (a hang in C, still running after 15 s [C,
  bridge-audit.md:1387-1396]) or replace `registry[8]` on LuaJIT
  (bridge-audit.md:459-471). `XLUA_REDUCED_DEBUG` (minimal) removes the
  means; legacy keeps the full library.
- **19. LuaJIT FFI.** `LuaLibs.Ffi` exposes raw memory access by design; on
  Apple ARM64 an FFI callback reads stack arguments narrower than 8 bytes
  wrongly (a wrong value, not a crash; Test/README.md, LuaJIT known
  failures) [C].
- **20. Lua errors through C# frames.** xLua's C# functions never raise
  themselves: wrappers return an error status and the native trampoline
  raises after C# has returned (`csharp_function_wrap`, `build/xlua.c`) [C].
  The remaining ways for a Lua error to cross a managed frame are items 8,
  9 and 17. LuaJIT on Win64 with Mono, where zlua reports SEH crashes
  [S, focus-creative-games.md:82-83], is untested (backlog.md).
- **Dispose and coroutines.** A Lua coroutine suspended at `Dispose` is freed
  with the state; C# handles to it (`LuaFunction` over `coroutine.wrap`,
  `xlua.util.cs_generator`'s `MoveNext`) go through `LuaEnv.L` and throw
  `ObjectDisposedException` [C by the code; the managed case covers
  `LuaFunction.Call`].
- **LuaScript.** Its entries are protected exports; it refuses Dispose during
  a call and use from another thread (`CheckThread`); its known crash items
  are fixed or listed in Security.md and core-luascript.md (watchdog frame
  walk race, owner question Q14/Q52) [S].

## The bridge fuzz target (2026-10-28)

The crash sweep of the [backlog](../design/backlog.md#tests-and-ci):
`Test/Fuzz/fuzz_bridge.c`, run by `Test/Fuzz/run_bridge.sh` (Test/README.md
describes it). A Lua driver turns each input into a list of operations: build
values of every kind, call a native with arguments drawn from them, run
operators, metatables, the `debug` library, `collectgarbage` and global
replacement on them. The natives are `xlua.*` and the accessors, the `uint64`
table and LuaJIT's int64 metamethods, the exports C# calls (through C
wrappers that call them as `LuaDLLBridge.cs` does), the class and object
indexers and the C# function trampolines around C stand-ins for C#
functions; on the core preset, the buffer library, the sandbox's coroutine
functions and host functions with a dispatcher that answers with every frame
status. Oracles besides the sanitizers, Lua's API checks and hangs: a helper
that C# calls outside a protected call raises nothing but a memory error, the
stack is left as the caller expects, struct headers stay intact.

Setup: `xlua-analysis:26.04` (Linux arm64 in OrbStack on macOS 27, Clang
21.1.8), the library built by CMake for each preset with ASan, UBSan and
`XLUA_APICHECK`, LuaJIT with its API checks only; libFuzzer in fork mode
(`-fork=1`, continuing after a crash), 1 KB random seeds, inputs up to
2 KB, 10 to 80 ms per input (up to 400 calls).

### Found and fixed (a)

- **25. `xlua_getloaders` raised** [C]. `LuaEnv.AddSearcher` (`AddLoader`)
  starts with `xlua_getloaders`, which read `package.searchers` with
  `lua_getglobal` and `lua_getfield` outside a protected call. A script that
  set `package` to nil or a boolean, or gave `_G` an `__index` that raises,
  made the next `AddLoader` raise with no handler: Lua's panic (item 9).
  Found on legacy 5.4.9 by the "unprotected helper raised" oracle in the
  first survey (33 of 56 reports); replacing `package` needs no `debug`.
  Fix (`build/xlua.c`): the lookup runs in a helper under `lua_pcall`, and an
  error gives nil, which C# already reports as "Can not set searcher!".
  Cost: none on a hot path (`AddSearcher` only). Test:
  `native_regression.py protected_helpers` (`package = nil`, `package = true`,
  a raising `_G.__index`): before the fix "PANIC: unprotected error in call
  to Lua API (attempt to index a nil value)" and exit 134 on 5.4.9 under ASan.
- **26. A host function's type error past the frame** [C]. LuaScript's
  `LuaCall` reports `GetLong(i)` on an argument the call did not pass as
  `XLUA_FRAME_TYPEERROR` with argument `i + 1` (`LuaCall.cs:1057`); the
  trampoline (`raise_frame_error`, `build/xlua_core.c`) passed it to
  `luaL_typeerror`, which reads the argument. Past `nargs` + `LUA_MINSTACK`
  that index is outside the frame: Lua's API check "unacceptable index"
  aborted the core preset at once (5.4.9, 5.5.1). Release builds got Lua's
  nil value there, so the message was right. Fix: for an argument above
  `nargs` (or below 1, which `LuaCall` never sends), `luaL_argerror` with the
  same "X expected, got no value" text. Test: `core_smoke.c`
  (`typeerr(40)`): an assertion failure before, the expected message after.
- **27. Lua 5.3: a new metatable for an all-weak table** [C]. A heap use after
  free on legacy 5.3.6 (three crashes: in `luaV_finishset`, `luaH_get` and the
  collector's `traversetable`), reduced to `setmetatable(t, {__mode = "kv"})`,
  a GC step, `setmetatable(t, {})`, another step, then any access to `t`'s
  metatable. No xLua code is involved: the stand-alone 5.3.6 interpreter
  under ASan crashes the same way with `__mode = "kv"` (not with "k" or
  "v"), and 5.4.9 and 5.5.1 do not. In 5.3.6's `traversetable` an all-weak
  table met during propagation goes to `allweak`, which the atomic phase only
  clears, and stays gray; `lua_setmetatable`'s barrier acts only for a black
  table, so a metatable set afterwards is never marked and the sweep frees
  it. Upstream lists it as "New metatable in an all-weak table can fool the
  GC" (lua.org/bugs.html, reported 2025-06-12, since 5.3.0) [S]; 5.4 puts
  the table in `grayagain` during propagation. That fix is copied into
  `build/lua-5.3.6/src/lgc.c` (five lines, `build/LUA_PATCHES.md`). Test:
  `native_regression.py weak_table_metatable`: ASan abort before, pass after
  (5.3.6, sanitized build).
- **28. LuaJIT: a light userdata with the int64 metatable** [C]. The int64
  checks of `build/i64lib.c` (`lua_isint64`, `lua_isuint64`,
  `lua_isinteger64`, `lua_isint64_or_uint64`) took `lua_touserdata`, which is
  also the address a light userdata holds; after
  `debug.setmetatable(ptr, getmetatable(int64))` a C# `long` read or `..`
  read 16 bytes there (a global-buffer-overflow under ASan, legacy LuaJIT).
  Fix: only a full userdata counts, as for the C# object readers
  (MIGRATING.md, "A light userdata ... where the bridge expects a C#
  object"). Cost: one `lua_type` per int64 check on LuaJIT [U, not
  measured]. Test: `native_regression.py luajit_int64`: the light userdata
  counted as an int64 before, and its concatenation succeeded.

- **29. LuaEnv's message handler with `debug` replaced** [C]. `errorfunc`
  (`build/xlua_core.c`), the handler of every protected call from C#, looked
  up `debug.traceback` with `lua_getglobal` and `lua_getfield`; after a script
  set `debug = nil` (or a number, or `debug.traceback` to a non-function) the
  lookup raised inside the handler, so C# got "error in error handling"
  instead of the error on every runtime. On LuaJIT a protected call inside
  the handler resets that state (a plain `xpcall` whose handler calls
  `pcall` and then fails runs the handler 10,913 times, until "stack
  overflow"; PUC Lua stops after 215 with "error in error handling"), and
  with a C handler each level also takes C stack: with
  `debug.sethook(function() pcall(type) end, 'c')` and `debug = nil`, a
  delegate call that fails overflowed the C stack (ASan stack-overflow,
  legacy and minimal LuaJIT; the fuzzer reached it through a hook that called
  `xlua_getloaders`). Fix: when `debug.traceback` is not a function, the
  handler returns `luaL_traceback`'s text (what `debug.traceback` returns) or
  a non-string message unchanged; a script's own `debug.traceback` is still
  used. Test: `native_regression.py error_handler` ("error in error
  handling" before; the LuaJIT hook case also checked).

### By design or upstream (c)

- **22. `debug.getregistry`** [C]. A script with the full `debug` library can
  replace any registry entry that xLua's helpers read without a type check:
  the metatable references of `xlua_pushcsobj`, `xlua_pushstruct`,
  `xlua_newstruct`, `xlua_pushcstable` (`lua_setmetatable` with a non-table:
  LuaJIT reads it as a table), the userdata cache of
  `xlua_tryget_cachedud` and `_checked` (`lua_rawgeti` on a non-table), and
  `registry[8]` on LuaJIT (int64). All crashed under `FUZZ_TRICKS=1` (legacy
  5.4.9, LuaJIT). Like item 18, `XLUA_REDUCED_DEBUG` removes the means. A
  `lua_type` check in each would turn them into errors; the checked cache
  lookup, made against scripts that write into the cache, does not cover a
  replaced cache table. Proposal: owner question (see below).
- **23. LuaJIT: `debug.setmetatable` on an io file** [C, by the code;
  reproduced under `FUZZ_TRICKS=1`]. LuaJIT's trace recorder treats the
  metatables of special userdata as immutable and reads them without a NULL
  check (`lj_record_mm_lookup`, `immutable_mt:`); with the metatable removed,
  a recorded call of the file reads through NULL. A LuaJIT limitation; a
  script needs the `debug` library.
- **Item 18, more cases** [C]. A class indexer's fields upvalue
  (`cls_indexer`, upvalue 2) is read with `lua_rawget`; `debug.setupvalue`
  with a non-table makes LuaJIT read a number as a table (SIGSEGV). The C
  function inside a C# function's trampoline (upvalue 1), taken out with
  `debug.getupvalue` and called alone, writes its error flag to an upvalue
  the C function does not have.
- **Item 7, confirmed by the fuzzer** [C]: C# callbacks that call back into
  Lua overflowed the C stack on LuaJIT (stack-overflow reports in
  `lj_strfmt_pushvf` and the unwinder, nesting of `csharp_function_wrap` and
  `lua_pcall`). Main fixed it the same day (the trampolines' limit of 199
  levels); after the merge the target nests without a limit of its own and
  meets that one.
- **Item 18, BaseType cycles**: the target kept its BaseType chains out of
  the operations' reach until main's walk limit landed the same day; since
  the merge the chains are in the pool and cycles end with the indexers'
  error.

### Open (b)

- **24. `debug.traceback(msg, level)` with a level near `INT_MIN` or
  `INT_MAX`** [C]. Signed overflow in `luaL_traceback` (`last - level`,
  `level++`; `lauxlib.c:132`, `:139` in 5.5.1, `:137`, `:144` in 5.4.9),
  undefined behaviour that UBSan reports; no wrong result or crash seen.
  Any script with the `debug` library reaches it, and the error handler of
  LuaEnv passes a fixed level. Options: a two-line clamp in the vendored
  `ldblib.c` (`db_traceback`) or `lauxlib.c` of 5.3.6, 5.4.9 and 5.5.1, with
  a `LUA_PATCHES.md` entry, or a report to lua-l only. Owner question.

### Questions for the owner

1. Item 22: add a `lua_type` check to the helpers that read xLua's registry
   references (about 1 ns per push of a C# object or struct on the bridge's
   hot path), or keep it as a `debug` trick of item 18?
2. Item 24: patch the vendored Lua (three versions) for an overflow with no
   observed effect, or only report it upstream?
3. Item 27 changes the vendored Lua 5.3.6 (a backport of Lua 5.4's own fix):
   kept as decided for (a) cases; say if vendored GC changes should wait for
   you.

### CPU time and coverage

Fork mode with one job: CPU time is about the fuzzing time. Runs are
libFuzzer's count of executed inputs.

| Configuration | Fuzzing time | Runs | Reports |
|---|---|---|---|
| legacy 5.5.1 | 15 min | 29,382 | 3: item 24 |
| legacy 5.4.9 | 15 min | 33,262 | 32: item 24 |
| legacy 5.3.6 | 15 + 5 min (after the fix) | 28,103 + 6,627 | 4: item 27 |
| legacy LuaJIT | 15 + 5 min | 221,553 + 72,112 | 6: item 7 (5), item 28 |
| minimal 5.5.1 | 10 min | 24,061 | 0 |
| minimal 5.4.9 | 10 min | 35,596 | 0 |
| minimal 5.3.6 | 10 min | 19,227 | 0 |
| minimal LuaJIT | 10 + 5 min | 180,885 + 89,509 | 10: item 29 |
| core 5.5.1 | 15 min + 1 min | 107,768 | 2 smoke runs: item 26 |
| core 5.4.9 | 15 min | 125,196 | 0 |
| surveys with `FUZZ_TRICKS=1` (legacy 5.4.9 twice, LuaJIT) | 8.5 min | 89,812 (63,489 in the first, whose inputs stayed at 8 bytes or less) | items 10, 18, 22, 23, 25 |
| smoke and triage runs | about 5 min | | |
| after the merge with main: legacy LuaJIT, legacy 5.4.9 | 5 + 5 min | 78,822 + 8,389 | 0 |

Total about 2 hours 40 minutes, and 10 minutes after the merge with main
(legacy LuaJIT and 5.4.9, below). Item 25 was fixed after the first survey;
the reports of item 24 led to the limit the target keeps outside
`FUZZ_TRICKS` (traceback levels within ±10^6). During the campaign the
target also capped C# callback nesting at 150 (item 7) and kept BaseType
chains private (item 18); both went once main's fixes were merged.

Coverage of the final corpora replayed once against libraries built with
`XLUA_COVERAGE=ON` (llvm-cov):

| Corpus | `xlua.c` functions | `xlua.c` branches | `i64lib.c` branches | `xlua_core.c` branches |
|---|---|---|---|---|
| legacy 5.5.1 | 79% | 71% | 100% (8) | 69% |
| legacy LuaJIT | 83% | 75% | 83% | 65% |

On the core preset (5.5.1): `xlua_core_sandbox.c` 61% of lines,
`xlua_core_buffer.c` 76%, `xlua_core_limits.c` 58%.

**Not covered:** the exports C# does not import (`xlua_tostruct`, the
indirect references, `xlua_objlen`, the `print_*` helpers) and those it
calls with fixed arguments (`xlua_get_registry_index`, `load_error_func`,
`xlua_gl`); most float and decimal pack and unpack variants after
`xlua_gettypeid` (`fuzz_struct.c` covers them); the overdraft of a core
state (`raise_overdraft`: the stand-in C# functions allocate too little);
the leak checker (`xlua_report_*`); LuaSocket and the `io` and `os`
functions, which the target removes; `LuaEnv`'s own Lua code; on the core
preset, `require` and its resolver, `XLUA_CALL_START` coroutines, handle
iteration, time limits, `Interrupt` and the watchdog (`fuzz_core.c` and
`core_smoke.c` cover most of them); C# itself (the stand-ins only do what C#
functions do); bit 1 (checked accessors) and bit 0 (core states) were
reached, but their share of the runs was not measured. Lua 5.3.6 with a core
state has no instruction limit; Windows, macOS, x86-64 and 32-bit targets
were not run.

### Tests run for these fixes

- Each regression check failed before its fix and passes after it, in the
  container under ASan, UBSan and the API checks: `native_regression.py
  protected_helpers` (5.4.9), `error_handler` (LuaJIT; 5.4.9 after),
  `weak_table_metatable` (5.3.6), `luajit_int64` (LuaJIT), `core_smoke.c`
  (core 5.5.1).
- `sanitize.sh` (export check, all native regression checks, profiler smoke,
  the managed suite through reflection; the image has no .NET SDK): legacy
  5.3.6, 5.4.9, 5.5.1 and LuaJIT; `core-5.4.9` and `core-5.5.1`; minimal
  5.5.1 and LuaJIT. All pass.
- Lua 5.3.6's official suite (`lua_tests.py`, basic, complete and internal,
  with the `lgc.c` patch): final OK.
- `Tools/dependencies.py --check`: up to date.
- Not run: macOS and Windows hosts, the source generator passes of the
  managed suite, Unity, devices.

## Fixed earlier (for completeness)

From the documents, with their tests: stack room for pushes and type
registration ([#1153](https://github.com/Tencent/xLua/pull/1153),
`Utils.EnsureStack`, bridge-audit.md item 14); NULL C function after
`debug.setupvalue` (`csharp_function_wrap`, `CaseCSharpFunctionUpvalueReplaced`);
LuaJIT int64 userdata read as a struct header (`check_luajit_int64_header`);
`xlua.cast` on any value (`CaseCastRejectsInvalid`); negative
`xlua_newstruct` sizes; undefined float casts in `xlua_touint` and
`xlua_tointeger` (`fuzz_struct`, `fuzz_i64`); invalid IL from the hotfix
injector (#1003, a Mono SIGSEGV); the watchdog's lost stop and its
use-after-free of a `CallInfo` (watchdog-lost-stop.md); the core handle
error path that left `L->ci` on a dead frame (core-luascript.md:3638-3670);
the finalizer deadlock under `HOTFIX_ENABLE` (#1127) and the WebGL GC queue
(#741). `sanitize.sh` and the fuzz targets run clean on them (Test/README.md).

## Found later

- **An event handler that is not a function, in reflection wrappers**
  (2026-10-28, fixed). `MethodWrapsCache.GetEventWrap` passed the handler to
  `ObjectTranslator.CreateDelegateBridge` without a check, which uses the
  value as a registry key: `o:Ev("+")` (nil) raised "table index is nil" from
  `lua_rawset` inside the C# wrapper, a Lua error unwinding through managed
  frames; `o:Ev("+", 5)` threw `KeyNotFoundException` or could find the
  bridge of another Lua function under that integer key. After the nil case,
  the next C# exception thrown inside a reflection call crashed Mono 6.14.1
  while unwinding (SIGSEGV in `mono_arch_unwind_frame`) or hung in its crash
  handler, in the source-generator pass of the managed suite, Lua 5.5.1,
  macOS arm64. [C] The wrapper now converts the handler first and raises
  "#3 need T!" as generated code does (`CaseParityEvents`,
  [binding-mode-parity.md](binding-mode-parity.md)).

## Reproduction results

`python3 Test/Native/crash_repro.py --plugin <libxlua> --output <dir>`,
30 s per case, Mono 6.14.1, macOS arm64. "managed" is a C# exception that
reached `Main`; "error" a Lua error that the case printed.

| Case | 5.5.1 before | 5.5.1 after | LuaJIT before | LuaJIT after | 5.3.6 after |
|---|---|---|---|---|---|
| `next_invalid_key` | managed, from the panic | managed, protected | managed, from the panic | managed, protected | managed, protected |
| `len_metamethod_error_length` | managed, from the panic | managed, protected | raw length, no error | same | managed, protected |
| `len_metamethod_error_array_arg` / `_list_arg` | Lua error across C# frames | C# exception | raw length | same | C# exception |
| `gc_error_fullgc` | warning | warning | "ERROR in finalizer" | same | **panic** |
| `gc_error_alloc` | warnings | warnings | "ERROR in finalizer" | same | error in an unrelated call |
| `dispose_in_callback` / `_coroutine` | **SIGABRT** | InvalidOperationException | **SIGABRT** | InvalidOperationException | InvalidOperationException |
| `deep_recursion_cs_lua` | C stack overflow error | same | **SIGSEGV** at depth 10,240 | same | C stack overflow error |
| `lua_stack_overflow` | stack overflow error | same | same | same | same |
| `foreign_self_file` / `_tag` | invalid arguments | same | same | same | same |
| `two_threads` | **SIGABRT** | odd LuaException (racy) | odd LuaException | **SIGABRT** | **SIGABRT** |
| `finalizer_thread_call` | **SIGABRT** | **SIGABRT** | managed | **SIGABRT** | **SIGABRT** |
| `csharp_stack_overflow` | **SIGSEGV** | **SIGSEGV** | **SIGSEGV** | **SIGSEGV** | **SIGSEGV** |

The thread cases are races: one run can abort and the next end in an
exception.

After the 2026-10-28 changes (`crash_repro.py` builds with `XLUA_GENERAL`, so
the owner-thread check is on), on 5.3.6, 5.5.1 and LuaJIT [C]:
`two_threads` ends in `InvalidOperationException: this LuaEnv belongs to
thread 1 but is used from thread 3 ...` on the first call of either thread;
`finalizer_thread_call` ends with no call made from the finalizer (each
throws, and the case's finalizer catches it); `deep_recursion_cs_lua` ends
in "C stack overflow" with C# function 199 the deepest, LuaJIT included. The Lua 5.3.6 "before" column was not run (the plugin had been
rebuilt); its "after" results for items 3, 4 and 2 match 5.5.1.

## Tests run for these changes

- `run_managed.py --source-gen --source-gen-minimize --source-gen-fallback`:
  482 tests in each of two modes in all four binding modes, Lua 5.5.1 and
  LuaJIT 2.1, macOS arm64, Mono 6.14.1, after merging main at 9c166e9d.
- `coverage_cs_dotnet.py` (Lua 5.5.1): passes, runtime branches 79.1%
  combined.
- `native_regression.py` (all checks) and `check_exports.py`: Lua 5.3.6,
  5.5.1, LuaJIT.
- `hotfix_check.py` (Lua 5.5.1): pass, `CheckHotfixAfterDispose` in each
  step; with the old `RawState` it crashed in `pcall_prepare`.
- `sanitize.sh` in `xlua-analysis:26.04` (Linux arm64, ASan + UBSan +
  APICHECK): 5.3.6, 5.4.9, 5.5.1 and LuaJIT pass.
- Not run: Windows, Unity players and the Editor suite, IL2CPP, Lua 5.4.9 on
  the host (only under `sanitize.sh`).
