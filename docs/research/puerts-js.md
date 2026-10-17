# PuerTS JavaScript side: lessons for xlua-ng

Researched 2026-10-17 from [Tencent/puerts](https://github.com/Tencent/puerts)
`master` at `0ac7d2f1`. The architecture and the Lua backend are covered in
[puerts.md](puerts.md). Only QuickJS's C files were compiled, to measure their
size; nothing was run.

Path prefixes:

- `nat/` = `unity/native/`
- `upms/` = `unity/upms/`
- `U/` = `unity/`

Items marked **[inferred]** were not read directly in code.

## Lessons, in priority order

### 1. Watchdog

**What PuerTS does:**

- V8 exposes `TerminateExecution` to C# without a lock
  (`nat/papi-v8/source/PapiExport.cpp:135-138`,
  `upms/v8/Runtime/Src/Backends/BackendV8.cs:72-75`).
- Taking a `v8::Locker` there used to hang the calling thread. It was fixed
  in fc44c6a8. The Node.js copy still takes it under `THREAD_SAFE`.
  **[inferred]**
- QuickJS marks its interrupt error uncatchable
  (`nat/papi-quickjs/quickjs/quickjs.c:7019-7031`).

**The Lua equivalent:** call `lua_sethook(L, stop, mask, 1)` from another
thread, as `lua.c` does on SIGINT (`build/lua-5.4.9/src/lua.c:63-79`).
`ldebug.c:125-133` documents it as signal-safe. Two pitfalls:

- Hooks belong to each coroutine. A coroutine copies the hook only when it
  is created (`lstate.c:306-308`), so arm the hook on the running coroutine.
- A script `pcall` can catch the limit error. Keep the hook armed until the
  C# entry call returns, so the script cannot swallow it.

**Long C functions:** QuickJS-ng's `libregexp.c` has the same hole as Lua's
pattern matcher. The fix for Lua is a step budget in `lstrlib.c` `match()`
that errors at the existing "pattern too complex" exit (`lstrlib.c:570-572`).
Record it in `LUA_PATCHES.md`. See also [luau.md](luau.md).

### 2. Test infrastructure

- **Hang timeout:** `dotnet test --blame-hang-timeout 10000ms`
  (`U/cli/test.mjs:229`) kills a hung test host. `run_managed.py` should
  likewise get a hard per-run timeout that kills its child process.
- **Build once, run many:** one player is built, and an environment variable
  selects the backend per run (`U/test/Src/UnitTestEnv.cs:29-46`). The matrix
  is three binding modes times three backends.
- **Don't copy tests per language:** PuerTS's JS cross-language tests are
  3,230 lines, Lua 642 and Python 537, so coverage was lost. Keep one corpus
  with capability flags, like our existing `DISTINCT_UINT64`, if Luau or
  other backends arrive.

### 3. Debugger only in development builds

- **Compiled out, API unchanged:** the V8 inspector is built only with
  `WITH_INSPECTOR` (`U/cli/make.mjs:405-407`). Otherwise it is a stub that
  returns nullptr, and the exports and C# API stay the same. It is polled from
  `Tick()`, with `WaitDebugger`/`WaitDebuggerAsync`
  (`upms/core/Runtime/Src/PInvoke/ScriptEnv.cs:305-357`).
- **For xlua-ng:** choose the LuaSocket/debugger plugin variant with
  `SetIncludeInBuildDelegate` on Development Build, and keep a C# stub in
  release builds.
- **Correction:** `libWSPPAddon` is a WebSocket client for scripts, not the
  debugger (`nat/wsppaddon/source/WSPPAddon.cpp:23-25`). It is still enabled
  for Android by default, so Lua games ship its 1.78 MB. Never ship an addon
  that is enabled by default.

### 4. Async require

**PuerTS:** `import()` only enqueues a microtask. The module tree is fetched
through the synchronous `ILoader`
(`nat/papi-v8/source/BackendEnv.cpp:755-855`). A top-level await that is still
pending is ignored (`:680-688`).

**Proposal for xlua-ng:** an `xlua.import(name)` written in Lua.

1. If the module is in `package.loaded` or a preload cache, call `require`.
2. Otherwise ask a C# `ILuaAsyncLoader` (Addressables or a download) and call
   `coroutine.yield()`.
3. When the bytes arrive, C# fills the cache and resumes the coroutine, and
   the plain synchronous `require` then runs.
4. Deduplicate concurrent imports of one name with a table of waiting
   coroutines. An optional manifest can prefetch dependencies.
5. Add an `xlua.await(task)` for `Task`, modelled on `puer.$promise`
   (`upms/core/Runtime/Resources/puerts/csharp.mjs:142-161`).

This uses the same yield-request model as `LuaScript`.

### 5. LuaCATS generator

- **XML documentation comments:** load them from `<assembly>.xml` as
  descriptions (`upms/core/Editor/Src/DocResolver.cs:100-133`).
- **A declare-only list:** like `[Typing]`, it declares types for the IDE
  without generating wrappers or link.xml entries.
- **Generic definitions:** declare each generic type definition once with
  type parameters (`List$1<T>`, `upms/v8/Editor/Src/Generator/DTS.cs:309-312`)
  instead of one class per instantiation. This needs a LuaLS spike first.
- **Write only changed files:** `LuaCATSFiles.Write` always rewrites today
  (`Assets/XLua/Src/Editor/LuaCATSGenerator.cs:813-816`). PuerTS does no
  better: it writes one monolithic `index.d.ts`.
- **Keep the generator in pure C#:** PuerTS renders `.d.ts` templates in V8,
  so projects without V8 cannot generate typings. **[inferred]**

How PuerTS maps types, for reference:

- **Integers:** `long` and `ulong` become `bigint`; the other integer types
  become `number`.
- **Nullable:** `T | null`.
- **ref/out:** `$Ref<T>`.
- **Indexers:** the `Item` property is dropped; arrays use
  `get_Item`/`set_Item`.
- **Events:** `add_X` and `remove_X` methods.
- **Delegates:** a callable interface with a constructor.
- **Overloads:** native TS overloads, with base-class overloads re-declared.
- **Generic methods:** generic parameters are replaced by their constraint,
  as in our generator.

### 6. One C# API for two presets

- **Abstract at the native ABI:** PuerTS's marshalling sits behind a C
  function table, and each backend implements one `Backend` class
  (`upms/core/Runtime/Src/Backend.cs:13-57`).
- **Avoid same-named classes chosen by `#if`:** its `IL2Cpp/ScriptEnv.cs` and
  `PInvoke/ScriptEnv.cs` drifted apart. `SetDefaultBindingMode`,
  `ExecuteModule<T>` and `UsingAction` exist only in the IL2CPP one.
- **Avoid downcasts for engine features:** `TerminateExecution` exists only
  on the concrete `BackendV8`.
- **For xlua-ng:** `LuaEnv` composes `LuaScript` and the `xlua_core.c` ABI
  rather than duplicating them. Limits and interrupts are exposed through
  capability interfaces. A Luau backend would be another `xlua_core`
  implementation. **[inferred]**

### 7. Chunk names and reload

- **Chunk names:** `ReadFile(path, out debugpath)` gives IDE debuggers real
  paths (`upms/core/Runtime/Src/Loader.cs:21,110-124`). Always use
  `@<real path>` chunk names. Document traceback rewriting for
  TypeScriptToLua and Teal.
- **Cache clearing:** only the CommonJS caches can be cleared
  (`upms/core/Runtime/Resources/puerts/module.mjs:342-360`). The native ESM
  cache-clearing code is never exported.
- **For xlua-ng:** offer `xlua.reload(name)` (`package.loaded[name] = nil`)
  with its limits documented. Skip weak module caches.

## Other facts

**QuickJS:** the backend is quickjs-ng 0.6.1, vendored on 2025-06-18.

- **Limits:** none. There is no `JS_SetMemoryLimit` or
  `JS_SetInterruptHandler`, and C# cannot pass in its own runtime.
- **Measured size:** the five QuickJS C files have 892,651 bytes of `__TEXT`
  at `-O3` and 616,976 at `-Os` (arm64 macOS, glue excluded). That is about
  2–2.5× PuerTS's Lua backend. **[inferred for Android]**
- **What keeps it small:**
  - hidden symbols;
  - no C++ exceptions, RTTI or unwind tables;
  - EASTL instead of libc++, compiled once into PuertsCore;
  - 16 KB pages.

  It does not use `-Os`, LTO or gc-sections.

**V8:** isolates get only an array buffer allocator. There are no heap limits,
no near-heap-limit callback and no snapshot. `--jitless` is chosen at build
time for iOS and OHOS.

**WebGL backend:** pesapi is implemented in TypeScript (1,173 lines), bundled
into `puerts-runtime.js` (36,550 bytes gzip -9). C# reaches it through
`.jslib` and `DllImport("__Internal")`, so scripts run in the browser's JS
engine. Limits:

- one environment only, which cannot be destroyed;
- the build processor permanently edits `PlayerSettings` to allow table
  growth.

Lua has no equivalent, because browsers have no Lua VM. xlua-ng keeps Lua
small inside the IL2CPP wasm and works on interop cost instead.
