# LuaEnv creation measurements

## The CSharpCallLua scan kept for the process (2026-10-18)

**Change.** Without generated delegate bridges, every `LuaEnv` ran
`ObjectTranslator.initCSharpCallLua`, which reads every type of every loaded
assembly for `[CSharpCallLua]` and the static configuration lists. The scan is
now kept for the process (`ObjectTranslator.CSharpCallLuaScan`,
`Assets/XLua/Src/ObjectTranslator.cs:343`) while
`AppDomain.GetAssemblies()` returns the same assemblies in the same order;
the configuration fields and properties are still read for every env. A
second change looks up CodeEmit's members once per process instead of once
per env (`static CodeEmit()`, `Assets/XLua/Src/CodeEmit.cs:142`). Neither
changes what an env binds.

**Method.** `Test/Native/benchmark.py --modes reflection,emit,gencode,sourcegen
--filter create_dispose --no-lua --runs 15 --warmup 2`. The case creates and
disposes 20 `LuaEnv`s per timed run. The baseline is `82c570f9` (main with
the shared CodeEmit assembly); "after" is this branch merged with main
`f1f9d6a9`, whose changes since `82c570f9` touch no runtime or native code.
Baseline and change ran alternately: 3 rounds before and 4 after, 15 timed
runs each, on the host plugins built from `build/` with `-DBUILD_SILICON=ON`
(Lua 5.5.1 and LuaJIT 2.1). Host: Apple M2 Pro (10 cores), macOS 27.0.1,
Mono 6.14.1. Other agents were using the machine (load average 16 to 20), so
single runs vary by up to 4x; compare the medians and the minimums.

| Runtime | Mode | Before: median of all runs (round medians), ms | After: median of all runs (round medians), ms | Min before / after, ms | Managed bytes per env, before / after |
| --- | --- | --- | --- | --- | --- |
| Lua 5.5.1 | reflection | 8.26 (8.09, 9.21, 8.19) | 2.04 (2.14, 2.25, 2.03, 1.92) | 7.59 / 1.76 | 698,968 / 176,400 |
| Lua 5.5.1 | emit | 8.06 (7.89, 8.39, 7.85) | 2.00 (1.97, 2.43, 2.04, 1.88) | 7.48 / 1.71 | 698,968 / 176,400 |
| Lua 5.5.1 | gencode | 1.97 (1.96, 2.61, 1.90) | 1.91 (1.98, 2.67, 1.90, 1.80) | 1.63 / 1.68 | 190,240 / 180,256 |
| Lua 5.5.1 | sourcegen | 2.08 (2.06, 2.46, 1.94) | 2.01 (2.08, 2.01, 1.99, 1.90) | 1.72 / 1.64 | 193,952 / 183,968 |
| LuaJIT 2.1 | reflection | 8.15 (8.15, 9.23, 7.92) | 1.97 (2.44, 2.00, 1.92, 1.90) | 7.64 / 1.72 | 698,968 / 176,400 |
| LuaJIT 2.1 | emit | 8.24 (8.24, 9.26, 7.78) | 1.97 (2.20, 2.06, 1.96, 1.84) | 7.58 / 1.70 | 698,968 / 176,400 |
| LuaJIT 2.1 | gencode | 1.93 (1.87, 2.18, 1.83) | 1.90 (2.01, 1.93, 1.88, 1.86) | 1.72 / 1.64 | 190,240 / 180,256 |
| LuaJIT 2.1 | sourcegen | 2.05 (2.01, 2.32, 1.99) | 2.06 (3.78, 2.07, 2.33, 1.93) | 1.73 / 1.66 | 193,952 / 183,968 |

Reading:

- **Reflection and emit** (no generated delegate bridges, so
  `DelegateBridge.Gen_Flag` is off and the scan ran): creating and disposing
  an env takes 4 times less time, about 2.0 ms instead of 8.1 ms, and
  allocates 522 KB less. They now cost the same as the generated-code modes
  **[C]**.
- **Gencode and sourcegen** do not scan (their delegate bridges set
  `Gen_Flag`). They allocate 9,984 bytes less per env, from the CodeEmit
  lookups, which the `XLUA_GENERAL` and Editor builds make even when nothing
  is emitted **[C]**. Their time is within the noise of this machine.
- **The benchmark executable loads about seven assemblies** (Mono's
  `Assembly.GetTypes` ran 4,218 times for 601 envs under the profiler). A
  Unity Editor domain loads far more, and the scan grows with the number of
  types, so the saving there should be larger; it was not measured in Unity
  **[U]**.

**Where the time goes.** Mono's log profiler in calls mode
(`mono --profile=log:calls`, `mprof-report --method-sort=total`, 601 envs,
Lua 5.5.1). The profiler slows call-heavy code more than other code, so the
shares are rough.

| Inclusive time under the profiler | emit, before | emit, after | gencode, before |
| --- | --- | --- | --- |
| `LuaEnv..ctor` | 13,663 ms | 1,542 ms | 1,440 ms |
| `initCSharpCallLua` | 12,098 ms (89%) | 61 ms (4%) | not called (`Gen_Flag`) |
| `CreateArrayMetatable`, which reflection-wraps `System.Array` | 1,204 ms | 1,173 ms (76%) | 1,122 ms (78%) |
| `CodeEmit..ctor` | 115 ms | 110 ms (made static after this profile) | 108 ms |
| `LuaEnv.Dispose`, of which `GC.Collect` | 916 ms, 882 ms | 870 ms, 838 ms | 794 ms, 768 ms |

What remains in every mode is per env but not a scan of the loaded
assemblies:

- `ObjectTranslator.CreateArrayMetatable`
  (`Assets/XLua/Src/ObjectTranslator.cs:870`) registers `System.Array` as the
  base of the array metatable, and `System.Array` has no generated wrapper in
  these configurations, so every env builds its reflection wrapper
  (`Utils.makeReflectionWrap`: 45,075 `_GenMethodWrap` calls for 601 envs).
  The wrappers bind the env's translator; sharing them would need the
  member list cached separately from the closures.
- `LuaEnv.Dispose` (`Assets/XLua/Src/LuaEnv.cs:675`) runs
  `GC.Collect` and `GC.WaitForPendingFinalizers` twice.

**Other scans on the way to an env.** These were already once per process
and are unchanged:

- `Utils.GetExtensionMethodsOf` (`Assets/XLua/Src/Utils.cs:403`) fills
  `InternalGlobals.extensionMethodMap` on first use; generated bindings merge
  their lists into it (`Assets/XLua/Src/Binding.cs:483`). It is not rebuilt
  when an assembly is loaded later.
- `BlackListRules` (`Assets/XLua/Src/GenAttributes.cs:199`, Editor and
  `XLUA_GENERAL` only) reads the `[BlackList]` configuration once, and is not
  rebuilt either.
- `Binding.BindingRegistry.EnsureRegistered`
  (`Assets/XLua/Src/Binding.cs:509`) scans for `[assembly: GeneratedBindings]`
  once, and again when a hotfix slot is missing.
- The `GenConfig` reflection in `Editor/Generator.cs` runs when code is
  generated, not when an env is created.
- `ObjectTranslator`'s own assembly list (`ObjectTranslator.cs:264`) is per
  env by design (`xlua.load_assembly` adds to it) and took 4 ms of the 1,542
  under the profiler.

**Invalidation.** The key is the list of loaded assemblies, compared at each
env creation, rather than `AppDomain.AssemblyLoad`: nothing can be loaded
between an event and the scan unseen, and no handler stays registered.
Emitted assemblies are left out of the key and the scan, as `GetAllTypes`
leaves them out, and so is `XLuaCodeEmit` (on .NET 5 and later its
`ManifestModule` is not a `ModuleBuilder`; `CodeEmit.IsEmittedAssembly`,
`CodeEmit.cs:278`). While any other dynamic assembly is loaded nothing is
kept and every env scans as before, since types can still be added to it.

**Tests.** `Test/UnitTest/StreamingAssets/luaCallCsUpstream.lua`
(`CaseEnvScanReused`, `CaseEnvScanSeesLaterAssembly`; types in
`Upstream.EnvScanCache`). The first fails without the cache, the second fails
with a cache that is never invalidated ("This type must add to
CSharpCallLua"); both checked on the Lua 5.5.1 host plugin. On .NET 10
(`coverage_cs_dotnet.py`) the first case found that every env still scanned
because of `XLuaCodeEmit`, which the `IsEmittedAssembly` check fixes; the
second case needs `AssemblyBuilderAccess.Save` and runs on Mono only.
