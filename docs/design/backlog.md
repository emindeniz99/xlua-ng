# Backlog from the 2026-10-17 research

Every action item that came out of the research notes, in one place. The
order of the large work items is in [presets.md](presets.md); this list
also holds the smaller fixes, tests and docs. Each item links to its
evidence. Status: **todo** unless marked.

## Size and presets

| Item | Evidence |
|---|---|
| C# flags: SignatureLoader, `template`, embedded `xlua.util` and profiler, LuaSocket registration | [presets.md](presets.md), [SIZE.md](../../build/SIZE.md) |
| Remove `tdr` and `perf/memory.lua.txt`; add a `string.pack` versioned-message example and a pointer to lua-protobuf | [tdr.md](../research/tdr.md) |
| Native options: LuaSocket, `mime`, leak checker, uint64 helpers, choice of standard libraries, computed export list | [presets.md](presets.md) |
| Android API 23 everywhere; APS2 packed relocations and gnu-hash | [platform-levels.md](../research/platform-levels.md) |
| Compiler and linker flags from the device benchmark (hybrid `-O3`/`-Oz`, LTO) | device benchmark, in progress |
| Plugin variant selection with `SetIncludeInBuildDelegate`; unique export names per variant | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md), [puerts.md](../research/puerts.md) |
| Opt-in source plugin through `FORCE_PINVOKE_xlua_INTERNAL`; use zlua's lumped-compile fixes (`getline`, `luai_*`, `tmpnam`) as a checklist | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md), [focus-creative-games.md](../research/focus-creative-games.md) |
| Document IL2CPP "Faster (smaller) builds" (−116 KB of xLua's cost) | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md) |
| Document Lua 5.4 as the most mature choice for conservative production use | [presets.md](presets.md) |
| Ship no addon enabled by default (PuerTS ships a 1.78 MB WebSocket addon on Android) | [puerts-js.md](../research/puerts-js.md) |

## Limits and sandbox (`LuaOptions`, `LuaScript`)

| Item | Evidence |
|---|---|
| `xlua_core.c` with a limited allocator, instruction hook, safe callback trampoline and library selection | [presets.md](presets.md) |
| Step budget in `lstrlib.c` `match()` so limits also cover patterns; record it in `LUA_PATCHES.md` | [luau.md](../research/luau.md), [puerts-js.md](../research/puerts-js.md) |
| `TimeLimit` next to `InstructionLimit`; a watchdog arms `lua_sethook` on the running coroutine and keeps it armed until the C# entry returns | [puerts-js.md](../research/puerts-js.md) |
| `HashSeed` option (Lua 5.5 `lua_newstate` seed; build-time on 5.4) | [presets.md](presets.md) |
| Sandbox shape: frozen base environment, a fresh `_ENV` per script, `__metatable` locks, `collectgarbage("count")` only | [luau.md](../research/luau.md) |
| `buffer` library on userdata with Luau's API names | [luau.md](../research/luau.md) |
| A security statement for `LuaScript`, modelled on Luau's SECURITY.md | [luau.md](../research/luau.md) |
| Coroutine helpers: `IEnumerator` and `Task`/`Awaitable` drivers for `LuaScript` | [presets.md](presets.md) |
| luban `lua-lua` data as a realistic core-preset fixture | [focus-creative-games.md](../research/focus-creative-games.md) |

## Bindings (source generator)

| Item | Evidence |
|---|---|
| `IIncrementalGenerator` against Roslyn 4.3.1, scoped by the asmdef folder; runtime detects a missing registration and falls back to reflection with a warning | [SOURCEGEN_SPIKE.md](../../build/SOURCEGEN_SPIKE.md) |
| One canonical "reduced marshal signature" key for deduplication, with its own tests | [puerts.md](../research/puerts.md), [focus-creative-games.md](../research/focus-creative-games.md) |
| Reflection fallback chosen per member; log once per member in Development builds | same |
| One marshalling model shared by generated code and the reflection path | [puerts.md](../research/puerts.md) |
| Compile-time name table (literals plus `typeof`) replacing every runtime `Type.FullName` and name lookup | [obfuscators.md](../research/obfuscators.md) |
| Preserve only used members instead of `preserve="all"` | [binding-generation.md](../research/binding-generation.md) |
| Measure generated vs reflection size by type count (no public data exists) | [binding-generation.md](../research/binding-generation.md) |
| Hotfix bridges with source generation: the `Inline` path or a bridge class per game assembly | [hotfix.md](../research/hotfix.md) |

## Hotfix

| Item | Evidence |
|---|---|
| Measure the size (`size_report.py --hotfix`) and the cost of an unpatched method with `HOTFIX_ENABLE` on and off | [hotfix.md](../research/hotfix.md) |
| Separate `HOTFIX_ENABLE` from `THREAD_SAFE` locking | same |
| Check whether the iOS IntKey flag (`HotfixFlags.cpp`, one P/Invoke per method entry) can become a managed array read | same |
| Fix the build hook that throws without an `Assembly-CSharp` (`Hotfix.cs:1633`) | same |
| Warn when IntKey silently falls back to field mode across assemblies (UPM layout) | same |
| A ready "whole assembly" hotfix configuration, with its measured cost | this list |
| HybridCLR integration guide | [hotfix.md](../research/hotfix.md) |
| ILPostProcessor injection: postponed until the hotfix config is static | same |
| Spike: move the injector to Unity's `com.unity.nuget.mono-cecil` package. Check:<br>• which Unity versions have it and which Cecil version it contains;<br>• whether injection can run inside the Editor instead of `XLuaHotfixInject.exe` under Mono;<br>• whether that removes the bundled Cecil, Rocks and manifest handling;<br>• conflicts with tools that bring other IL libraries. | Cecil vs dnlib discussion, 2026-10-17: Cecil stays; dnlib only if PDB problems remain |
| Within that spike, test whether symbol rewriting fixes Beebyte's `SymbolsNotMatchingException` ([xLua#1122](https://github.com/Tencent/xLua/issues/1122)) | [obfuscators.md](../research/obfuscators.md) |

## Obfuscation

| Item | Evidence |
|---|---|
| Emit `[Obfuscation(Exclude=true)]` on the XLua assembly and injected members; read it and the tool-specific attributes at Editor time | [obfuscators.md](../research/obfuscators.md) |
| Use `AOT.MonoPInvokeCallbackAttribute` under Unity | same |
| `IXLuaNameMapper` (Type → original name) and an Editor `IObfuscatorAdapter` | same |
| obfuz adapter, a generated rule XML and a CI test; optionally an Obfuscar rename pass in the native suite | same |
| Test whether symbol rewriting closes Beebyte's `SymbolsNotMatchingException` ([xLua#1122](https://github.com/Tencent/xLua/issues/1122)) | same |
| Docs: which native symbols each preset exports, for protectors that hook Lua loaders | same |

## Correctness and security

| Item | Evidence |
|---|---|
| Lock bridge metatables with `__metatable`; scripts can call `getmetatable(obj).__gc(obj)` today | [luau.md](../research/luau.md), [ROADMAP.md](../../ROADMAP.md) |
| Test raising `lua_error` inside a C# callback on LuaJIT Win64 Mono (zlua reports SEH crashes) | [focus-creative-games.md](../research/focus-creative-games.md) |
| Done: duplicate overloads in `private_accessible` (0b9f7b6) | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md) |
| Done: runtime LINQ removed (−54 KB APK) | same |

## Loading and tooling

| Item | Evidence |
|---|---|
| `xlua.import(name)`: async loading on coroutines with deduplication; `xlua.await(task)` | [puerts-js.md](../research/puerts-js.md) |
| `xlua.reload(name)` with documented limits | same |
| Always use `@<real path>` chunk names | same |
| LuaCATS: XML doc comments, a declare-only list, generic definitions once (LuaLS spike first), write only changed files | same |
| Debugger and LuaSocket only in development builds, behind a C# stub | same |
| Luau type definitions only if a Luau backend lands | [luau.md](../research/luau.md) |

## Tests and CI

| Item | Evidence |
|---|---|
| A hard per-run timeout in `run_managed.py` that kills the child (the xctest leak) | [puerts-js.md](../research/puerts-js.md) |
| One test corpus with capability flags across Lua versions and binding modes | same |
| A `.meta` linter in CI | [puerts.md](../research/puerts.md) |
| An IL2CPP APK run on an emulator in CI | same |
| A device benchmark against PuerTS-Lua and zlua with the same workload (zlua's case list) | [focus-creative-games.md](../research/focus-creative-games.md) |
| luban `lua-bin` as a Lua→C# binding benchmark and integration sample | same |

## Outside xlua-ng

| Item | Evidence |
|---|---|
| Small PRs to PuerTS: 16 KB alignment for `libPapiLua.so`, a minimal export list | [puerts.md](../research/puerts.md) |
| Watch LeanCLR as a possible compatibility target for WebGL and mini-games | [focus-creative-games.md](../research/focus-creative-games.md) |
| Possible later: a `core-luau` plugin variant, measured on device first | [luau.md](../research/luau.md) |
