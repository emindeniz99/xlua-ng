# LuaEnv creation measurements

## Reflection members kept for the process (2026-10-18)

**What every env wrapped.** `LuaEnv`'s constructor calls
`ObjectTranslator.CreateArrayMetatable` (`Assets/XLua/Src/LuaEnv.cs:259`),
which pushes `typeof(System.Array)` as the base of the array metatable
(`Utils.EndObjectRegister`). That value is a `Type` object, and pushing it
needs the metatable of its class, `System.RuntimeType` on Mono and .NET. The
class is not public, so no generator or CodeEmit wraps it, and every env made
its reflection wrapper: 79 public methods and 30 properties on Mono 6.14.1,
75 method wrappers per env. `System.Array` itself is wrapped only when a
script first looks up an array member that is not `Length` or an index. A
probe that lists the translator's `loaded_types` after `new LuaEnv()` shows
only `System.RuntimeType` **[C]**. The previous section's profile counted
this as the `System.Array` wrapper.

**Change.** `Utils.ReflectionMembers` (`Assets/XLua/Src/Utils.cs:550`) keeps,
per type and binding access, what `makeReflectionWrap`
(`Assets/XLua/Src/Utils.cs:860`) binds: the fields, events, accessors,
operators and overload groups that pass the `[BlackList]` rules and the
unsupported-type checks, in the order they are set, with the parameter
analysis of every overload (`OverloadSignature`,
`Assets/XLua/Src/MethodWarpsCache.cs:29`, split out of
`OverloadMethodWrap.Init`). It refers to no env or Lua state. Each env still
makes its own closures, `MethodWrap`s, checkers and casters, and reads
`static readonly` and `const` fields again, so the tables it builds are the
same as before. This covers every type wrapped by reflection, not only
`RuntimeType`.

- **Invalidation:** an entry is made again when the extension-method list of
  its type is not the one it was made with. Registering generated bindings
  replaces the lists of the types they extend (`Binding.cs`), and
  `Utils.GetExtensionMethodsOf` returns the same list object otherwise.
  Members of a loaded type do not change, and `BlackListRules` is read once
  per process already.
- **Threads:** the cache is read and written under a lock. An entry is not
  changed once it is in; two threads may make the same entry and the later
  one wins. The item getters and setters, which share an argument buffer,
  stay per env.
- **Memory:** the cache keeps `MemberInfo`s and signatures for each type a
  process has wrapped by reflection, until the domain unloads. A
  collectible assembly (`AssemblyLoadContext`) that was wrapped by reflection
  is not freed while the cache holds its types **[inferred, not tested]**.
- **Errors:** when the parameter analysis of an overload group throws (a
  default value that cannot be converted), the group keeps its members
  without signatures, and each env calls `_GenMethodWrap` for it, which
  throws at the same point as before.

**Method.** `Test/Native/benchmark.py --modes reflection,emit,gencode,sourcegen
--filter create_dispose --no-lua --runs 15 --warmup 2`, which creates and
disposes 20 `LuaEnv`s per timed run. Baseline `11a13982` (main), change
`3d48f674` (this branch before merging main), three rounds each, alternating
(before, after, before, after; round 1 about 50 minutes before rounds 2
and 3), on the
host plugins built from `build/` with `-DBUILD_SILICON=ON` (Lua 5.5.1 and
LuaJIT 2.1, `Release`). Host: Apple M2 Pro (10 cores), macOS 27.0.1, Mono
6.14.1. Other agents were using the machine (load average 6 to 19), so single
runs vary by up to 7x; compare the medians.

| Runtime | Mode | Before: median of all runs (round medians), ms | After: median of all runs (round medians), ms | Change | Min / max before, ms | Min / max after, ms | Managed bytes per env, before / after |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Lua 5.5.1 | reflection | 1.94 (1.90, 2.21, 1.90) | 1.57 (1.59, 1.64, 1.53) | -19% | 1.75 / 2.94 | 1.34 / 1.77 | 176,800 / 85,648 |
| Lua 5.5.1 | emit | 2.01 (2.01, 2.02, 2.01) | 1.60 (1.59, 1.53, 1.68) | -20% | 1.65 / 6.58 | 1.41 / 1.84 | 176,800 / 85,648 |
| Lua 5.5.1 | gencode | 1.89 (1.89, 2.16, 1.82) | 1.42 (1.38, 1.39, 1.48) | -25% | 1.59 / 10.77 | 1.21 / 1.67 | 180,656 / 89,504 |
| Lua 5.5.1 | sourcegen | 2.03 (2.03, 2.21, 1.87) | 1.63 (1.63, 1.59, 1.70) | -20% | 1.70 / 5.20 | 1.31 / 7.15 | 185,008 / 93,856 |
| LuaJIT 2.1 | reflection | 1.97 (1.99, 2.02, 1.91) | 1.61 (1.59, 1.41, 1.71) | -18% | 1.71 / 6.43 | 1.33 / 2.85 | 176,800 / 85,648 |
| LuaJIT 2.1 | emit | 1.91 (1.95, 2.10, 1.85) | 1.52 (1.57, 1.53, 1.49) | -21% | 1.71 / 6.82 | 1.36 / 2.28 | 176,800 / 85,648 |
| LuaJIT 2.1 | gencode | 1.86 (2.05, 1.85, 1.78) | 1.41 (1.41, 1.42, 1.40) | -24% | 1.59 / 3.28 | 1.20 / 1.54 | 180,656 / 89,504 |
| LuaJIT 2.1 | sourcegen | 1.98 (2.71, 1.95, 1.88) | 1.55 (1.51, 1.51, 1.56) | -22% | 1.64 / 14.85 | 1.21 / 1.80 | 185,008 / 93,856 |

**Creation and disposal apart.** A harness built against each mode's
benchmark executable times `new LuaEnv()` and `Dispose()` separately (20
envs per run, 15 runs, two warm-up envs), and in a second loop
`FullGc()` plus `Dispose(true)`, which is `Dispose()` without its
`GC.Collect` and `GC.WaitForPendingFinalizers` calls. Medians of the three
rounds' medians, same builds and rounds as above:

| Runtime | Mode | Constructor before / after, ms | `Dispose()` before / after, ms | `Dispose()` without its GC calls, before / after, ms | GC calls' share of `Dispose()`, after |
| --- | --- | --- | --- | --- | --- |
| Lua 5.5.1 | reflection | 0.67 / 0.31 | 1.45 / 1.39 | 0.018 / 0.018 | 99% |
| Lua 5.5.1 | emit | 0.63 / 0.29 | 1.38 / 1.44 | 0.018 / 0.017 | 99% |
| Lua 5.5.1 | gencode | 0.67 / 0.28 | 1.32 / 1.29 | 0.019 / 0.017 | 99% |
| Lua 5.5.1 | sourcegen | 0.64 / 0.28 | 1.44 / 1.51 | 0.018 / 0.017 | 99% |
| LuaJIT 2.1 | reflection | 0.61 / 0.28 | 1.35 / 1.42 | 0.048 / 0.048 | 97% |
| LuaJIT 2.1 | emit | 0.60 / 0.28 | 1.38 / 1.41 | 0.049 / 0.046 | 97% |
| LuaJIT 2.1 | gencode | 0.63 / 0.27 | 1.33 / 1.31 | 0.048 / 0.047 | 96% |
| LuaJIT 2.1 | sourcegen | 0.62 / 0.26 | 1.57 / 1.47 | 0.048 / 0.046 | 97% |

Reading:

- **The constructor** takes less than half the time, about 0.28 ms instead
  of 0.63 ms, in every mode and on both runtimes, and allocates 91 KB less
  per env **[C]**.
- **`LuaEnv.Dispose()` is now about 80% of creating and disposing an env**:
  about 1.4 ms, of which its two `GC.Collect` and
  `GC.WaitForPendingFinalizers` pairs (`Assets/XLua/Src/LuaEnv.cs:749`) are
  96 to 99%; closing the state and the rest of `Dispose(true)` take 0.02 ms
  (Lua 5.5.1) and 0.05 ms (LuaJIT). The collections back the "try to dispose
  a LuaEnv with C# callback" check, which needs finalized delegate bridges,
  and their cost grows with the managed heap, which this benchmark keeps
  small. They are left alone; whether to keep them is the owner's question
  **[C for the share in this benchmark; U for a game's heap]**.
- **Under the profiler** (`mono --profile=log:calls`, 601 envs, gencode, Lua
  5.5.1): `LuaEnv..ctor` 1,440 ms before, 585 ms after;
  `CreateArrayMetatable` 1,122 ms before, 379 ms after, of which
  `MethodWrapsCache.GenMethodWrap` 194 ms (the per-env wrappers, checkers and
  casters) and the members made once 31 ms. `LuaEnv.Dispose` is 779 ms, of
  which `GC.Collect` 750 ms.

**Tests.** `Test/UnitTest/StreamingAssets/luaCallCsUpstream.lua`
(`CaseEnvReflectionReused`, `CaseEnvReflectionOnThreads`; types in
`Upstream.EnvReflectionCache` and the internal `Upstream.ReflectedShape`,
which every mode wraps by reflection). The first checks that three more envs
make no members and bind the same fields, properties, indexer, overloads,
default parameters, operator and `Type` members; it fails on the previous
code, which has no counter. The second does the same in envs on four threads
(in turn without `THREAD_SAFE`).

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
  base of the array metatable, and every env builds a reflection wrapper
  (`Utils.makeReflectionWrap`: 45,075 `_GenMethodWrap` calls for 601 envs).
  The wrapped type is `System.RuntimeType`, the class of the `Type` object
  pushed as the base, not `System.Array` (corrected in the section above,
  which keeps the member lists for the process).
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
