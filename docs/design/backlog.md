# Backlog from the 2026-10-17 research

Every action item that came out of the research notes, in one place. The
order of the large work items is in [presets.md](presets.md); this list
also holds the smaller fixes, tests and docs. Each item links to its
evidence. Status: **todo** unless marked.

## Next up (agreed order, 2026-10-17)

Native test work runs one agent at a time (see the note in AGENTS.md), so
these go in order:

1. Bridge items #4 (`xlua.cast`), #6 (`metatable_operation` keeps the tag
   and `__gc`) and #14 (stack growth in C# callbacks, with the
   `LUA_USE_APICHECK` assertion). In progress.
2. Crash sweep: the adversarial fuzz target plus `sanitize.sh` on the
   2026-10-17 changes.
3. C# coverage: measured on the host suite with Mono's coverage profiler
   (`Test/Native/coverage_cs.py`, 70.3% of runtime lines) and with coverlet
   on .NET (`Test/Native/coverage_cs_dotnet.py`, 62.1% of runtime branches),
   2026-10-17. Next: tests for the gaps listed under
   [Tests and CI](#tests-and-ci), then the Unity Code Coverage package for
   the editor suite.
4. Re-run the Unity editor suite on the new runtime changes.
5. Implementation, in the order of [presets.md](presets.md): C# flags first.

## Size and presets

| Item | Evidence |
|---|---|
| Done (2026-10-17) as opt-out defines `XLUA_NO_SIGNATURE_LOADER`, `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` and `XLUA_NO_PROFILER` ([MIGRATING.md](../MIGRATING.md#opt-in-defines-no-change-unless-defined)). C# flags, decided 2026-10-17. Measured basis in [SIZE.md](../../build/SIZE.md).<br>• **SignatureLoader:** behind a flag; off means it is not compiled (−239 KB at Minimal).<br>• **`template` library:** not opened at run time unless flagged; it stays in the Editor for the legacy generator and user custom templates ([Custom_Generate_EN.md](../../Assets/XLua/Doc/Custom_Generate_EN.md)). Its Regex use is not rewritten, and no other template engine replaces it; the source generator emits C# without templates.<br>• **`xlua.util`:** embedded in C# as a builtin module under a flag; `require` keeps working with any loader setup.<br>• **`perf.profiler`:** embedded the same way, on in development builds. | [presets.md](presets.md) |
| **LuaSocket and other native modules register themselves from native code**, decided 2026-10-17.<br>• **How:** when compiled in, the library puts `socket.core` and `socket` into `package.preload`. C# has no `DllImport` for them, so a variant without LuaSocket cannot fail to link on iOS, and the C# code is the same for every variant.<br>• **Legacy:** keeps `LuaDLL.Lua.luaopen_socket_core`.<br>• **Same pattern for:** mime, the leak checker and user C libraries. A later step lets a third-party C library be added through CMake alone, without C# changes.<br>• **Tests:** `require "socket"` works with LuaSocket and fails with "module not found" without it, on every Lua version. | this list |
| Bytecode options, decided 2026-10-17:<br>• **Minimal and legacy** keep loading precompiled bytecode.<br>• **New "bytecode-only" build flag:** leaves out the parser (`lparser`, `lcode`, `llex`, about 54 KB) for projects that precompile every script, hotfix scripts included.<br>• **Core:** an opt-in "trusted bytecode" flag adds `lundump.c` back (about 6 KB) for a project's own precompiled scripts; the default stays text-only.<br>• **Portability:** bytecode depends on the Lua version. On 5.3 it also depends on `sizeof(int)`/`sizeof(size_t)` (32- vs 64-bit; see [compatible_bytecode.md](../../Assets/XLua/Doc/compatible_bytecode.md)). LuaJIT has its own format in two variants that do not load in each other: GC64 (every 64-bit build except `make_win64_luajit.bat`) and non-GC64 (32-bit builds and `make_win64_luajit.bat`); `luajit -b -X` or `-W` writes either ([build/README.md](../../build/README.md#luajit-gc64-on-x64)). | 2026-10-17 discussion |
| Turn off `LUA_COMPAT_APIINTCASTS` (`build/CMakeLists.txt:256`). Nothing in `LuaDLL.cs`, `xlua.c`, `i64lib.c` or LuaSocket uses the unsigned API casts. | 2026-10-17 check |
| Done: struct accessor setters raise for a value that is not a number or a numeric string, instead of storing 0 (upstream behaviour). Decided 2026-10-17 by the owner for every preset, no legacy flag. | [bridge-audit.md](../research/bridge-audit.md#c3-struct-field-setters-stored-0-for-non-numbers-changed) |
| Done: Lua numbers go into C# integer parameters, fields, properties, indexers and integer struct fields by Lua 5.3's rule (`luaL_checkinteger`) on every runtime and binding path, through one native check (`xlua_tointeger64`); integer overloads match only numbers with an integer value. Decided 2026-10-17 by the owner (option A) for every preset. | [bridge-audit.md](../research/bridge-audit.md#c4-floats-with-a-fraction-went-into-integer-slots-changed) |
| Decided 2026-10-17 by the owner: integral values outside the C# type's range (300 for a `byte`, -1 for a `uint`) keep wrapping in every preset. Planned flag, not implemented: a strict range check that raises "value out of range" for int8 to uint32 (never for long and ulong, whose bit patterns the bridge relies on), available for `minimal` | [bridge-audit.md](../research/bridge-audit.md#c4-floats-with-a-fraction-went-into-integer-slots-changed) |
| Decided 2026-10-17 by the owner: no overload reordering. The first declared overload that matches wins, as upstream, so `Foo(3)` calls `Foo(double)` when it is declared before `Foo(int)`, and `Mathf.Max(3, 5)` stays float | [bridge-audit.md](../research/bridge-audit.md#c4-floats-with-a-fraction-went-into-integer-slots-changed) |
| Errors raised from C# wrappers: a clean Lua error ("bad argument #1 to 'Foo' (...)") instead of the `c# exception:` text with the .NET exception type and stack, from every catch block in the templates, CodeEmit and the reflection wrappers. Kept as is for now (owner, 2026-10-17) | [bridge-audit.md](../research/bridge-audit.md#c4-floats-with-a-fraction-went-into-integer-slots-changed) |
| The integer rule for the slots C4 left unchanged: array elements, `params` arrays, nullable integers, enums, table contents converted to structs and collections, and values C# reads from Lua (delegate and interface results, `LuaTable.Get`). The bridges need a `try`/`finally` that restores the Lua stack before their casts can throw | [bridge-audit.md](../research/bridge-audit.md#c4-floats-with-a-fraction-went-into-integer-slots-changed) |
| Later: Ed25519 signature checking in native code (Monocypher or TweetNaCl, about 10–15 KB) as the cheap signed-loader option for minimal and core; RSA stays in legacy | this list |
| Done (2026-10-17): remove `tdr` and the `perf/memory.lua.txt` stub (both functions only raise errors; a real tool is the next item); add a `string.pack` versioned-message example and a pointer to lua-protobuf | [tdr.md](../research/tdr.md) |
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
| Core preset design: `LuaScript` API, `xlua_core.c` ABI, sandbox, security statement, size budget, tests and milestones; 13 open questions for the owner | [core-luascript.md](core-luascript.md) |
| One UPM package with `XLua.Core` and `XLua` asmdefs; `XLUA_CORE_ONLY` excludes the full assembly and selects the core plugin; a size check that a core-only player has no `XLua` code, which also answers whether Minimal stripping drops the unreferenced assembly | [core-luascript.md](core-luascript.md) |
| `__gc` finalizers run with hooks off (`lgc.c:920-929` in 5.4), so core's `setmetatable` refuses `__gc`; `table.move` and plain `string.find` are unbounded C loops, so the C budget patch covers them too | [core-luascript.md](core-luascript.md) |
| `MemoryLimit` on `LuaEnv`: the allocator must not refuse while a C# callback runs, because generated wrappers call raising Lua APIs unprotected | [core-luascript.md](core-luascript.md) |
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

The design for these items is in [source-generator.md](source-generator.md).

| Item | Evidence |
|---|---|
| `IIncrementalGenerator` against Roslyn 4.3.1, scoped by the asmdef folder; runtime detects a missing registration and falls back to reflection with a warning | [SOURCEGEN_SPIKE.md](../../build/SOURCEGEN_SPIKE.md) |
| One canonical "reduced marshal signature" key for deduplication, with its own tests | [puerts.md](../research/puerts.md), [focus-creative-games.md](../research/focus-creative-games.md) |
| Reflection fallback chosen per member; log once per member in Development builds | same |
| One marshalling model shared by generated code and the reflection path | [puerts.md](../research/puerts.md) |
| Compile-time name table (literals plus `typeof`) replacing every runtime `Type.FullName` and name lookup | [obfuscators.md](../research/obfuscators.md) |
| Preserve only used members instead of `preserve="all"` | [binding-generation.md](../research/binding-generation.md) |
| Measure generated vs reflection size by type count (no public data exists) | [binding-generation.md](../research/binding-generation.md) |
| Remove the partial-class code generation before the first public release, once the generator reaches parity and the migration tool exists | [presets.md](presets.md) |
| Migration tool: evaluate the old config lists in the Editor and write them as source-generator config; report what cannot be converted | [presets.md](presets.md) |
| Hotfix bridges with source generation: the `Inline` path or a bridge class per game assembly | [hotfix.md](../research/hotfix.md) |

## Hotfix

| Item | Evidence |
|---|---|
| Measure the size (`size_report.py --hotfix`) and the cost of an unpatched method with `HOTFIX_ENABLE` on and off | [hotfix.md](../research/hotfix.md) |
| Separate `HOTFIX_ENABLE` from `THREAD_SAFE` locking | same |
| Check whether the iOS IntKey flag (`HotfixFlags.cpp`, one P/Invoke per method entry) can become a managed array read | same |
| Fix the build hook that throws without an `Assembly-CSharp` (`Hotfix.cs:1633`) | same |
| Done (2026-10-18): the injector warns when IntKey falls back to field mode outside the xLua assembly, naming the types (`Hotfix.cs`, `warnIntKeyFallback`); the Editor logs the tool's `Warning:` lines as Unity warnings. Tested by `hotfix_check.py` (two assemblies) | [Hotfix_EN.md](../../Assets/XLua/Doc/Hotfix_EN.md#several-hotfix-assemblies) |
| Bug: `Inline` hotfix injection silently does nothing when the target is not the XLua assembly (`Hotfix.cs:1366-1370` returns true without injecting), so with XLua in its own asmdef generic methods and `Inline` types are not hotfixable | [source-generator.md](source-generator.md) |
| Done (2026-10-18): IntKey with several assemblies. `auto_id_map` requires only `hotfix_id_map`, which the Editor did not write when it wrote one map per assembly, so the call failed (reproduced by `hotfix_check.py`: "module 'hotfix_id_map' not found") or found a stale map. The Editor now also writes `hotfix_id_map.lua.txt` as the union of the per-assembly maps (`HotfixIdMap.Merge`, also `XLuaHotfixInject --merge-id-maps`); `xlua.util` is unchanged. Only the xLua assembly's map can hold ids today, so the union has no id clashes; per-assembly IntKey (source-generator option B) needs ids per assembly | [source-generator.md](source-generator.md), [MIGRATING.md](../MIGRATING.md#tooling) |
| Bug: a `[Hotfix]` attribute on a type outside the xLua assembly is ignored: `InjectType` compares the attribute's type reference with the xLua assembly's `HotfixAttribute` definition by reference (`Hotfix.cs:769`); configuration lists work. Reproduced with `XLuaHotfixInject` on 2026-10-18 (no `__Hotfix` field). Matching by full name would inject types that are not injected today, so it needs the owner's decision | this list |
| Bug (from reading, not run): from Unity 2019.1 the Editor passes `Assembly-CSharp.dll` as the xLua assembly for every injected assembly (`Hotfix.cs:1816`), so with xLua in its own asmdef (the UPM layout) the injector would fail at `Types.Single(... "XLua.DelegateBridge")` (`Hotfix.cs:336`). Pass the player build of `typeof(LuaEnv)`'s assembly instead; check in Unity | this list |
| Done (2026-10-18): `XLuaHotfixInject` threw when the id map path had no directory part (`Directory.CreateDirectory("")`, `Hotfix.cs:975`); it now writes the map to the working directory. Tested by `hotfix_check.py` (bare file names) | [MIGRATING.md](../MIGRATING.md#tooling) |
| Bug: `LuaDelegateWrap.tpl.txt:85` reads an undefined `param_offset` (the template is unreachable today because `GetGenConfig` drops delegates, `Generator.cs:1719`) | [source-generator.md](source-generator.md) |
| A ready "whole assembly" hotfix configuration, with its measured cost | this list |
| HybridCLR integration guide | [hotfix.md](../research/hotfix.md) |
| ILPostProcessor injection: postponed until the hotfix config is static | same |
| Decide (owner): Unity's `com.unity.nuget.mono-cecil` package. The 2026-10-18 spike found:<br>• 1.11.5 and 1.11.6 hold the same Cecil 0.11.5 build, with #914 added. 2021.3 bundles 1.10.1 (Cecil 0.10.0) and 2022.3 bundles 1.11.4.<br>• The injector passes `hotfix_check.py` with that build.<br>• But 0.11.5 lacks jbevain/cecil#958 and writes wrong portable-PDB line numbers after a backward jump of 8192 lines or more (reproduced).<br>• Injecting inside the Editor would need asmdefs and a new way to evaluate `HotfixConfig` for player assemblies.<br>**Recommendation:**<br>• Keep NuGet 0.11.6 as the default and the out-of-process injector.<br>• Keep `build_tools.py --cecil-dir` as an opt-in for machines without nuget.org access. It accepts only the pinned 1.11.5/1.11.6 hashes, so each new package build means one hash update.<br>• Do not load Cecil from the PackageCache at injection time.<br>• Revisit in-Editor injection with the UPM package and the ILPostProcessor item.<br>**Alternative:** drop `--cecil-dir` and keep only NuGet (no code to maintain, no offline path).<br>**Open:** the Unity checks listed in the research note. | [hotfix.md](../research/hotfix.md#unitys-cecil-package-spike-2026-10-18); Cecil vs dnlib, 2026-10-17: Cecil stays |
| Test whether symbol rewriting fixes Beebyte's `SymbolsNotMatchingException` ([xLua#1122](https://github.com/Tencent/xLua/issues/1122)) | [obfuscators.md](../research/obfuscators.md) |

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
| Done: bridge items 3 (zeroed LuaJIT int64 header) and 8 (NULL check in `csharp_function_wrap`) | [bridge-audit.md](../research/bridge-audit.md) |
| Done: bridge items 4 (`xlua.cast` only for C# objects of the target type, in every preset; decided 2026-10-17: no legacy flag), 6 (`xlua.setmetatable` keeps the tag and `__gc`) and 14 (stack room; clean `LUA_USE_APICHECK` run) | [bridge-audit.md](../research/bridge-audit.md) |
| Bridge hardening proposals 2, 5, 7 and 9–12, and restricting `xlua.getmetatable` in minimal (6) | [bridge-audit.md](../research/bridge-audit.md) |
| Test raising `lua_error` inside a C# callback on LuaJIT Win64 Mono (zlua reports SEH crashes) | [focus-creative-games.md](../research/focus-creative-games.md) |
| Done: duplicate overloads in `private_accessible` (0b9f7b6) | [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md) |
| Done: runtime LINQ removed (−54 KB APK) | same |
| Decided 2026-10-17, no change: `tostring` of a C# object keeps calling `ToString()` with the current culture in every preset. It is .NET's behaviour, the text is for debugging (it ends in the hash code), and Lua numbers are not affected. The FAQ shows how to ask for the invariant culture. The .NET coverage run sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` so its expected strings hold on any machine | [Faq_EN.md](../../Assets/XLua/Doc/Faq_EN.md) |

## Loading and tooling

| Item | Evidence |
|---|---|
| `xlua.import(name)`: async loading on coroutines with deduplication; `xlua.await(task)` | [puerts-js.md](../research/puerts-js.md) |
| `xlua.reload(name)` with documented limits | same |
| Always use `@<real path>` chunk names | same |
| LuaCATS: XML doc comments, a declare-only list, generic definitions once (LuaLS spike first), write only changed files | same |
| Debugger and LuaSocket only in development builds, behind a C# stub | same |
| Luau type definitions only if a Luau backend lands | [luau.md](../research/luau.md) |
| Done (2026-10-17, owner's decision): **XLua/Validate Setup** warns about `xlua/util`, `perf/profiler`, `perf/memory` and `tdr/tdr` Lua files that an in-place upgrade left in any `Resources` folder, where the Resources searcher would find them before the embedded modules, and says whether each is unchanged from a shipped version. Tested by `luacats_check.py` and the Unity editor suite (`ValidateSetupReportsOldModulesInResources`, 6000.3.26f1 on macOS) | [LuaCATS.md](../../Assets/XLua/Doc/LuaCATS.md#validate-setup), [MIGRATING.md](../MIGRATING.md) |

## Tests and CI

| Item | Evidence |
|---|---|
| **Crash sweep:** a new adversarial fuzz target that calls every native bridge function (`xlua.*`, struct accessors, uint64) with arguments of random types, the `debug` library enabled, under ASan for hours; plus `sanitize.sh` on Linux with the 2026-10-17 changes. Each crash gets a test and a fix. | bridge items 3 and 8, 2026-10-17 |
| Re-run the Unity editor suite with the 2026-10-17 runtime changes (metatable lock, items 3/8, StreamingAssets define), at least on 6000.3 and 2021.3 | 2026-10-17 status review |
| Done (2026-10-17): **C# coverage** measured. `Test/Native/coverage_cs.py` runs the managed suite in all three modes and `XLuaGenerate` under Mono's coverage profiler (no download). Runtime lines: 62.6% reflection, 49.4% gencode, 49.6% gencode-minimize, 68.9% for the three together; `Generator.cs` 61.2%. Lines and methods only, no branches. CI runs it on the Linux 5.5.1 leg and uploads the report | [Test/README.md](../../Test/README.md#c-coverage) |
| **C# coverage gaps** (suite, 2026-10-17; `methods.txt` in the report lists every method by missed lines). Each needs a test, or a decision that the code can go:<br>• **Generic delegate fallback:** `GenericDelegateBridge.cs` (0%) and `ObjectTranslator.getCreatorUsingGeneric`. They build a delegate when no generated bridge matches and Emit is unavailable, which is the IL2CPP case; reflection mode uses `CodeEmit` instead and the gencode modes have bridges for every test delegate.<br>• **Lazy members:** `Utils.LazyReflectionCall` and `RegisterLazyFunc` (116 lines, never run), the wrappers' lazy member registration.<br>• **`xlua.*` helpers in `StaticLuaCallbacks.cs`** (38.9%): `get_generic_method` (`GetGenericMethod`, `GenericMethodWraper`), `import_generic_type`, `load_assembly`, `release`, `tofunction`, delegate construction, enum `&`, the `xlua.access` error paths, and the `float`, `double`, `bool`, `long` and other primitive array fast paths (`tryPrimitiveArrayGet`, `TryPrimitiveArraySet`; only `int[]` runs).<br>• **C#-side API:** `LuaTable` (39.8%: `ForEach`, `GetKeys`, `Length`, `ContainsKey`, `Get<TKey,TValue>`, `Cast`, `ToString`), `LuaFunction` (36.5%: generic `Action`/`Func`, `Cast`), `LuaBase.Equals`/`GetHashCode`, `LuaEnv.GC`, `StopGc`, `RestartGc`, `GcStep`, `Memroy`, `Alias`.<br>• **`XLua.Cast.*`** (`RawObject.cs`, 0%), used to pick an overload from Lua.<br>• **Casters and checkers** for `char`, `sbyte`, `short`, `ushort`, `uint`, `ulong`, `decimal`, `IntPtr` and `LuaFunction` (`ObjectCasters.cs`).<br>• **CodeEmit** (86.9%): parts of `EmitInterfaceImpl`, default-value literals (`emitLiteralLoad`), the blacklist.<br>• **Generator** (`Generator.cs` 61.2% outside Unity): `getClassInfo` and `MergeCfg` branches, the hotfix bridge generation (`makeHotfixMethodInfoSimulation`, `injectByGeneric`), generic method support checks. | [Test/README.md](../../Test/README.md#c-coverage) |
| C# coverage tooling: also profile `signature_check.py` (`SignatureLoader.cs`), `hotfix_check.py` (the `HOTFIX_ENABLE` runtime), `luacats_check.py --validate` (`SetupValidator.cs`) and a `--define XLUA_LOCK_METATABLES` / `THREAD_SAFE` run, so that the totals include them; add a threshold check in CI once the gaps are tested | this list |
| Done (2026-10-17, owner's request): C# **branch** coverage. `Test/Native/coverage_cs_dotnet.py` builds the suite, `XLuaGenerate` and the generated wrappers with the .NET SDK and runs all three modes and the generator under coverlet 10.1.0 (NuGet, SHA-512 pinned). Runtime branches: 56.0% reflection, 41.0% gencode, 41.1% gencode-minimize, 62.1% for the three together; `Generator.cs` 47.2%. CI runs it after the Mono coverage step on the Linux 5.5.1 leg when the runner image has a .NET SDK. Left: the same in the Unity editor suite with `com.unity.testtools.codecoverage`, for the `UNITY_*` paths | [Test/README.md](../../Test/README.md#branch-coverage-on-net) |
| Done: native **branch** coverage. `xlua.c` went from 60% to 100% of branches (and 100% of lines and functions) on Lua 5.3.6, 5.4.9 and 5.5.1, with a test per branch; two bugs found and fixed (`lua_setfenv` set upvalue 1 in place; `xlua_touint` cast floats with undefined behaviour). The fuzz targets now reach the type id, `uint`, single-key and global helpers too. | [bridge-audit.md](../research/bridge-audit.md#found-by-coverage-work), [Test/README.md](../../Test/README.md#static-analysis-coverage-and-fuzzing) |
| Bound the `BaseType` walk in the indexer closures: a `debug.setupvalue` cycle loops in C past the instruction hook (debug library only) | [bridge-audit.md](../research/bridge-audit.md#not-changed) |
| CI: run `coverage.sh` and fail when `xlua.c` branch coverage drops below a set threshold | this list |
| Run the test suite inside an IL2CPP player (Android on device first), automated | 2026-10-17 status review |
| A second host runtime: run the managed suite on .NET (CoreCLR) besides Mono, ahead of Unity's CoreCLR move | 2026-10-17 status review |
| Done (2026-10-17): Lua's official test suites. `Test/Native/lua_tests.py` downloads the pinned, hashed lua.org archives (5.3.4 for 5.3.6, 5.4.9, 5.5.1), builds a stand-alone `lua` from the libxlua core sources and options, and runs the basic, complete and internal modes; the `lua-tests` job in `analysis.yml` runs them on Linux, plain and under ASan/UBSan, plus 5.3.6 with `LUAC_COMPATIBLE_FORMAT`. All pass. The only patch-related differences are the removed `os.execute` and `LUA_IDSIZE` 120, both intended; no fix was needed. Run it again for the planned budget and seed patches | [Test/README.md](../../Test/README.md#luas-official-test-suites), [LUA_PATCHES.md](../../build/LUA_PATCHES.md#lua-test-suites) |
| LuaJIT test suite: pin a commit of LuaJIT-tests (tarantool/luajit `test/LuaJIT-tests`, from LuaJIT/LuaJIT-test-cleanup) and run it against `build/luajit-2.1` like `lua_tests.py`. Check the licence first; nothing was downloaded yet | [Test/README.md](../../Test/README.md#luas-official-test-suites) |
| C setup, from [c17-setup.md](../research/c17-setup.md):<br>• Done (2026-10-17): the recommended warnings on our three files only, `-Werror` in CI through `XLUA_WERROR` (not on Windows yet); `()` → `(void)`, `#if defined(USING_LUAJIT)`, the `fuzz_common.h` mismatch; the negative `size` in `xlua_newstruct`.<br>• **MSVC:** check the `/W4` output on a Windows run, then turn `XLUA_WERROR` on there and decide on `/sdl`.<br>• **Sign conversions:** triage the 55 hits by hand.<br>• **Prototypes:** a private `xlua.h` with every export, which enables `-Wmissing-prototypes`.<br>• **`_Static_assert`** for the `CSharpStruct` layout. | [c17-setup.md](../research/c17-setup.md#implemented-2026-10-17) |
| Sanitizer and analysis jobs:<br>• Done (2026-10-17): `float-cast-overflow` for GCC in `sanitize.sh`; `LUA_USE_APICHECK` (`XLUA_APICHECK`) in the sanitizer, fuzz and coverage builds, LuaJIT included;<br>• `CC=clang sanitize.sh` preloads GCC's `libasan.so` and fails to load a Clang-built library: preload Clang's runtime instead;<br>• an MSan job for the fuzz targets;<br>• a nightly, non-blocking GCC `-fanalyzer` job;<br>• a fuzz-corpus regression run with periodic merges;<br>• consider CodeQL `security-extended`. | [c17-setup.md](../research/c17-setup.md) |
| Hardening: done (2026-10-17) behind `XLUA_HARDEN`, off by default, with BTI/PAC checked by `check_android_elf.py --expect-branch-protection`. Left: the owner's decision on the default from the [FLAGS.md](../../build/FLAGS.md#xlua_harden) measurements; LuaJIT's `libluajit.a` (its Makefile, `-mbranch-protection=standard -DLUAJIT_ENABLE_CET_BR`, stack protector); a Linux x86_64, OpenHarmony and MSVC build with the option. | [c17-setup.md](../research/c17-setup.md#hardening-1) |
| Reproducible builds: `-ffile-prefix-map` and `-Wdate-time` | [c17-setup.md](../research/c17-setup.md) |
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
