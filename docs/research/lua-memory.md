# Lua memory diagnostics for Unity bridges

Researched 2026-10-17 from read-only clones of the following repositories,
plus web sources for UWA, Unity and OpenResty:

- lua-snapshot (`f74f362`)
- LuaMemorySnapshotDump (`2d0f6aa`)
- Miku-LuaProfiler (`4cbf1bb`)
- both lmprof variants
- LuaMemorySnapShot

Nothing was run. Confidence: **[C]** confirmed in source or docs, **[S]**
secondary, **[U]** unconfirmed.

## Bottom line

- **Nothing ready-made fits.** No open-source tool maps Lua userdata to the
  C# objects a bridge holds, or answers "which C# object is kept alive, from
  which Lua path". **[C]**
- **Adopt only the algorithm:** port the reachability walk of
  [cloudwu/lua-snapshot](https://github.com/cloudwu/lua-snapshot) (MIT, about
  430 lines of C, public Lua API only, keep its notice).
- **Learn from:** Miku-LuaProfiler's workflow (Apache-2.0: reimplement, do
  not copy) and LuaMemorySnapshotDump's output format.
- **Build the C# layer ourselves.** The xLua-specific part is the link from a
  userdata to its `ObjectPool` index and then to the C# object, plus the
  `LuaBase` and delegate roots in the registry.

## Existing xLua pieces and their gaps

**`build/memory_leak_checker.c`** walks `G(L)->allgc` through Lua's internal
headers and reports table sizes and table-to-table edges. **[C]** Gaps:

- No userdata, strings, threads or C functions
  ([xLua#1020](https://github.com/Tencent/xLua/issues/1020)).
- In 5.4 and 5.5, objects with `__gc` live on `finobj` and `tobefnz`, which
  it never visits. xLua's C# object userdata have `__gc`, so they are almost
  certainly missed.
- It visits unreachable garbage (callers run a full GC first) and reports
  slot counts, not byte sizes.
- It is excluded from LuaJIT builds (`build/CMakeLists.txt:131-133`).
- An intermittent crash is reported upstream
  ([xLua#1149](https://github.com/Tencent/xLua/issues/1149)). The walk pushes
  values without `luaL_checkstack` and calls into C# in the middle of the
  walk. The cause is not isolated. **[U]**

**`General/LuaMemoryLeakChecker`** takes a snapshot keyed by table pointer and
reports grown tables as text paths, at most 10 hops from `_G` or the registry.
It is not available in Unity's runtime and has no C# object information.
**[C]**

**`Assets/XLua/Doc/Performance_Analysis.md`** documents
`memory.snapshot`/`memory.total`, but `perf/memory.lua.txt` only raises
errors. **[C]**

**How xLua keeps C# objects alive** (what a C# layer reads):

- **Lua to C#:** `xlua_pushcsobj` (`build/xlua.c:417`) stores an `int` key in
  the userdata. The key indexes `translator.objects` (an `ObjectPool` of
  strong references). A registry cache maps key to userdata.
- **C# to Lua:** `LuaTable` and `LuaFunction` hold registry refs, and delegate
  bridges hold refs too.

## Tools compared

| Tool | What it measures | How | Licence, activity | Fit |
|---|---|---|---|---|
| [cloudwu/lua-snapshot](https://github.com/cloudwu/lua-snapshot) | Reachable tables, functions, userdata, threads; edges with keys | C, public API, marks from the registry into a scratch state | MIT; 532★; 2024-08 | **Algorithm to port.** Lua 5.1–5.4 confirmed; 5.5 and LuaJIT plausible but not compiled **[U]** |
| [yaukeywang/LuaMemorySnapshotDump](https://github.com/yaukeywang/LuaMemorySnapshotDump) | Reference chains and counts; single-object search | Pure Lua `debug.*` walk | MIT; 585★; 2024-06 | Heavy: it allocates in the heap it measures. Learn its output format and queries |
| [Miku-LuaProfiler](https://github.com/ElPsyCongree/LuaProfiler-For-Unity) | CPU and GC per function; mark/diff; destroyed Unity objects still held; registry-ref monitor | Rewrites Lua source at load; hooks native exports and `dlopen` (EasyHook/Dobby); takes over GC | Apache-2.0; 2,020★; 2026-01 | Invasive: hooking `dlopen` can crash, Android only on device. **Learn its workflow** |
| [citizenfx/lmprof](https://github.com/citizenfx/lmprof), [pmusa/lmprof](https://github.com/pmusa/lmprof) | Allocation per function | `lua_Alloc` wrapper plus hooks | MIT; inactive | A later idea: allocation attribution |
| [Tarantool memprof](https://www.tarantool.io/en/doc/latest/tooling/luajit_memprof/) | Allocation events with Lua stacks | LuaJIT fork | MIT | Design reference only |
| UWA GOT Online | Lua heap size; count of destroyed Unity objects referenced from Lua (no file/line) | Closed SDK | Commercial | Not integrable |
| Unity Profiler / Memory Profiler | No Lua awareness; plugin heaps show as "Untracked" | – | – | A `ProfilerCounterValue` for the Lua heap is possible in development builds |
| tolua, SLua, PuerTS, HybridCLR | No leak tooling found | – | – | – |

## Recommendation: minimal feature set

1. **Native walker** ported from lua-snapshot.
   - It uses the public API, so it reaches `finobj` objects and builds on
     LuaJIT too.
   - It emits a callback stream of nodes and edges instead of Lua strings.
   - It is reentrancy-safe and checks stack space, which is the #1149 risk.
   - The internal-header walker stays for exact table sizes on 5.3–5.5.
2. **`LuaEnv.TakeSnapshot()`**, run after a full GC.
3. **The xLua cross-reference layer**, which is the differentiator.
   - Each C# object userdata is resolved through its `int` key to type,
     `ToString` and "destroyed `UnityEngine.Object`".
   - Each `LuaBase` and delegate bridge is recorded with its registry ref.
   - Reports work in both directions: "C# object X is held by Lua path P",
     and "Lua object Y is held by C# `LuaFunction` ref N".
4. **Diff by stable identity:** type, parent path and key, not address alone,
   because addresses are reused. It shows new objects, grown tables and
   objects still held after release.
5. **Paths from roots** (registry, `_G`, C# refs, thread stacks), with a depth
   cap.
6. **Editor window and JSON export.**
7. **Development builds only.** A `ProfilerCounterValue` reports the Lua heap
   and the count of C#-held objects.
8. **Tests:** the `LuaMemoryLeakCheckerTest` leak scenarios as automated
   tests, plus userdata and `finobj` cases.
9. **Docs:** replace the stale `memory.snapshot`/`memory.total` text.

Ideas to take from Miku-LuaProfiler and LuaMemorySnapshotDump:

- the mark, record and diff workflow around a UI being opened and released;
- a "null list" of userdata whose C# object is a destroyed Unity object;
- a live monitor of registry-ref adds and removes, for leaked `LuaFunction`s
  and delegates;
- root from the registry, not `_G`;
- a "single object, all references" query.

Later: per-function allocation attribution through a `lua_Alloc` wrapper,
which the planned `LuaOptions.MemoryLimit` allocator could share.
