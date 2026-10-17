# Backlog from the 2026-10-17 research

Every action item that came out of the research notes, in one place. The
order of the large work items is in [presets.md](presets.md); this list
also holds the smaller fixes, tests and docs. Each item links to its
evidence. Status: **todo** unless marked.

## Size and presets

| Item | Evidence |
|---|---|
| C# flags, decided 2026-10-17. Measured basis in [SIZE.md](../../build/SIZE.md).<br>• **SignatureLoader:** behind a flag; off means it is not compiled (−239 KB at Minimal).<br>• **`template` library:** not opened at run time unless flagged; it stays in the Editor for the legacy generator and user custom templates ([Custom_Generate_EN.md](../../Assets/XLua/Doc/Custom_Generate_EN.md)). Its Regex use is not rewritten, and no other template engine replaces it; the source generator emits C# without templates.<br>• **`xlua.util`:** embedded in C# as a builtin module under a flag; `require` keeps working with any loader setup.<br>• **`perf.profiler`:** embedded the same way, on in development builds. | [presets.md](presets.md) |
| **LuaSocket and other native modules register themselves from native code**, decided 2026-10-17.<br>• **How:** when compiled in, the library puts `socket.core` and `socket` into `package.preload`. C# has no `DllImport` for them, so a variant without LuaSocket cannot fail to link on iOS, and the C# code is the same for every variant.<br>• **Legacy:** keeps `LuaDLL.Lua.luaopen_socket_core`.<br>• **Same pattern for:** mime, the leak checker and user C libraries. A later step lets a third-party C library be added through CMake alone, without C# changes.<br>• **Tests:** `require "socket"` works with LuaSocket and fails with "module not found" without it, on every Lua version. | this list |
| Later: Ed25519 signature checking in native code (Monocypher or TweetNaCl, about 10–15 KB) as the cheap signed-loader option for minimal and core; RSA stays in legacy | this list |
| Remove `tdr` and the `perf/memory.lua.txt` stub (both functions only raise errors; a real tool is the next item); add a `string.pack` versioned-message example and a pointer to lua-protobuf | [tdr.md](../research/tdr.md) |
| Native options: LuaSocket, `mime`, leak checker, uint64 helpers, choice of standard libraries, computed export list | [presets.md](presets.md) |
| Android API 23 everywhere; APS2 packed relocations and gnu-hash | [platform-levels.md](../research/platform-levels.md) |
| Apply the measured flags. **Every preset:** `-O3` for the 18 hot files and `-Oz -mllvm -enable-machine-outliner=never` for the rest; API 23 with APS2. **Minimal and core only:** `-fvisibility=hidden` and the version script. **Not used:** `--icf=all` (it merges functions whose addresses Lua compares; saves 264 B) and the outliner (slower and larger on Android). **ThinLTO** (+7.6% speed, +61 KB) could be an opt-in speed option, Android only. **iOS:** the same hybrid split with Apple's `-Oz`. | [FLAGS.md](../../build/FLAGS.md) |
| Plugin variant selection with `SetIncludeInBuildDelegate`; unique export names per variant | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md), [puerts.md](../research/puerts.md) |
| Opt-in source plugin through `FORCE_PINVOKE_xlua_INTERNAL`; use zlua's lumped-compile fixes (`getline`, `luai_*`, `tmpnam`) as a checklist | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md), [focus-creative-games.md](../research/focus-creative-games.md) |
| Document IL2CPP "Faster (smaller) builds" (−116 KB of xLua's cost) | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md) |
| Document Lua 5.4 as the most mature choice for conservative production use | [presets.md](presets.md) |
| Ship no addon enabled by default (PuerTS ships a 1.78 MB WebSocket addon on Android) | [puerts-js.md](../research/puerts-js.md) |

| New feature, dev builds only: Lua memory diagnostics, C# first. `LuaEnv` snapshot and diff of table sizes and of the C# objects and delegates Lua holds, built on the native leak checker. An Editor window shows them, and a Lua module is at most a thin wrapper. Not a revival of the `perf/memory` stub. No ready-made tool covers the C#↔Lua cross references, so the native reachability walk is ported from cloudwu/lua-snapshot (MIT). That walk also covers userdata ([xLua#1020](https://github.com/Tencent/xLua/issues/1020)), `finobj` and LuaJIT, and must be reentrancy-safe ([xLua#1149](https://github.com/Tencent/xLua/issues/1149)). | [lua-memory.md](../research/lua-memory.md) |

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
| Known so far (2026-10-17): <br>• **Source:** the Unity registry and the package in a local PackageCache.<br>• **Latest version:** `com.unity.nuget.mono-cecil` 1.11.6 (2025-10-02) declares `"unity": "2019.4"`, so it installs on every editor we support.<br>• **Cecil inside:** Cecil 0.11.5 with the patch from [jbevain/cecil#914](https://github.com/jbevain/cecil/pull/914), shipping `Mono.Cecil`, `Rocks`, `Pdb` and `Mdb` DLLs. We bundle 0.11.6 today.<br>• **Older versions:** 1.10.x and 1.11.4 (2018.4+) back to 2020.<br>• **Unity's own advice:** the description says "intended for internal Unity use only", but also "to avoid assembly clashes, please use this package if you intend to use Mono.Cecil". | registry metadata |
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
| Done: `XLUA_LOCK_METATABLES` locks bridge metatables (opt-in; on in minimal once presets land) | [bridge-audit.md](../research/bridge-audit.md) |
| Bridge hardening proposals 2–12 (struct header on LuaJIT int64, `xlua.cast`, `metatable_operation`, reduced `debug`, read-only `xlua`) | [bridge-audit.md](../research/bridge-audit.md) |
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
