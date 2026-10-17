# Binding generation: reflection, generated code, source generators

Researched 2026-10-17. Confidence: **[C]** confirmed from a primary source,
**[C-local]** checked on this machine, **[S]** secondary, **[U]** unconfirmed.

## Reflection vs generated wrappers, measured

Source: `Test/Native/benchmark.py`, Lua 5.5.1, on a macOS host under Mono JIT
(run on 2026-10-16 at d276d342, saved in `bench/full2/out/benchmark.json` on
the build disk). IL2CPP timings will differ, but the ratios are a fair guide.

| Case | Reflection | Generated | Speed-up | Bytes per call (refl → gen) |
|---|---|---|---|---|
| Lua calls a C# static method | 568 ns | 84 ns | 6.8× | 72 → 0 |
| Instance method | 689 ns | 128 ns | 5.4× | 48 → 0 |
| Property get | 678 ns | 134 ns | 5.1× | 56 → 0 |
| Field set | 603 ns | 115 ns | 5.2× | 24 → 0 |
| String return | 524 ns | 105 ns | 5.0× | 32 → 0 |
| Struct argument (GCOptimize) | 878 ns | 256 ns | 3.4× | 56 → 0 |
| C# calls a Lua delegate | 145 ns | 123 ns | 1.2× | 0 → 0 |
| `LuaTable` get/set | ~300 ns | ~290 ns | ~1× | 0 → 0 |
| `LuaEnv` create and dispose | 13.4 ms | 7.3 ms | 1.9× | 1.19 MB → 0.63 MB |

- **Lua to C# calls:** reflection boxes the arguments into an `object[]` and
  calls `MethodInfo.Invoke`. Each call is therefore 5–7× slower and leaves
  24–120 bytes of garbage. Generated wrappers make direct calls and allocate
  nothing.
- **C# to Lua calls and table access:** no reflection is involved, so both
  modes perform the same.

### What reflection can reach

In reflection mode Lua can call any public member of any loaded type
(`CS.Namespace.Type.Member`) without configuration. After
`xlua.private_accessible(CS.Type)` it can call private members too. On IL2CPP,
two things still need preparation:

1. The linker removes members that C# code never references, so they must be
   preserved with `ReflectionUse`, `link.xml` or `[Preserve]`.
2. C#-to-Lua delegates and interfaces need generated bridges (`CSharpCallLua`),
   because IL2CPP cannot emit code at run time. `CodeEmit.cs`
   (Reflection.Emit) covers this case only in the Editor and on Mono.

## Ways to generate bindings

| Approach | When it runs | IL2CPP | Stripping | Notes |
|---|---|---|---|---|
| Runtime reflection | At run time | Works, if members are preserved | Needs link.xml or `[Preserve]` | Small fixed code cost. Slow, with garbage on every call. |
| Editor "Generate Code" (today) | Menu step | Safe | Writes `preserve="all"` | Code can go stale. Uses `partial` classes, which block an asmdef/UPM layout. |
| **Roslyn source generator** | Inside csc, on every compile | Safe | Direct calls; High stripping works without link.xml | Never stale. No menu. Can bind types the project does not own (see below). |
| ILPostProcessor | After compile, as IL | Safe | Neutral | The API is public in the DLL, but Unity staff call it internal and unsupported (2020, 2023). Sees only the assembly being compiled. Hard to debug. |
| Mono.Cecil weaving (hotfix today) | After the player scripts are built | Safe | Neutral | External tool. Can patch any assembly it is given. |
| Expression trees | At run time | Not usable on IL2CPP [S] | Same as reflection | |
| `UnmanagedCallersOnly` / function pointers | Native callback path | Not supported by IL2CPP for managed callbacks [S] | | Keep `MonoPInvokeCallback`. |

### Source generators in Unity

- **Compiler version:** Unity's csc is Roslyn **4.3.1** in 2021.3.45f2,
  2022.3.62f3, 6000.0.85f1 and 6000.3.26f1, and **4.10.0** in 6000.6.6f1.
  **[C-local]**
- **Documentation:** the 2021.3 and 2022.3 manuals still say generators must
  use Microsoft.CodeAnalysis 3.8. The 6000.0 manual says 4.3. **[C]**
- **Incremental generators:** `IIncrementalGenerator` needs Roslyn 4.0.1 or
  later, and `ForAttributeWithMetadataName` needs 4.3.0 or later. **[S]**
- **Evidence it works:** MessagePack v3 and MemoryPack ship incremental
  generators and require Unity 2022.3.12f1+. Unity's own Entities package
  ships `IIncrementalGenerator`-based generators. **[C]**
- **Configuring external types:** `[assembly: X(typeof(UnityEngine.Vector3))]`
  works. A generator built on Roslyn 4.3.1 saw `typeof()` arguments that name
  types in referenced assemblies. **[C-local]** Wrappers can use public API
  only.
- **Shipping:** build the generator for netstandard2.0 and label the DLL
  `RoslynAnalyzer`. Entities also adds `RunOnlyOnAssembliesWithReference`,
  but the spike found that label ignored. Put
  it next to the runtime asmdef so it runs only in assemblies that reference
  it. Generated types must be `internal`, or named per assembly, to avoid
  CS0436.
- **What to avoid:**
  - `ModuleInitializer`: not supported in Unity.
  - `RegisterPostInitializationOutput`: errors on some Unity 6 versions.
  - Assuming C# above 9: Unity compiles with `-langversion:9` unless `csc.rsp`
    raises it.

### What other bridges do

- **tolua#** and **SLua:** static wrapper generation from Editor menus.
- **PuerTS:** reflection, plus optional static wrappers, plus the xIl2cpp
  mode (direct IL2CPP calls without P/Invoke).
- **MoonSharp:** reflection descriptors plus a "hardwire" generator for AOT.
- **Lua-CSharp:** uses a source generator, with `partial` classes and no
  reflection fallback.

No other Lua or JS bridge for Unity was found to use ILPostProcessor. No
published measurement compares generated and reflection binding size under
IL2CPP; `Test/MobileSmoke/size_breakdown.py` can produce the first one.

## Decision

- **Bindings:** a Roslyn `IIncrementalGenerator` built against 4.3.1. Wrappers
  are non-partial, per-assembly `internal static` classes configured by
  assembly attributes. They register without `ModuleInitializer`, and only the
  members actually used are preserved.
- **Reflection and CodeEmit:** stay as an opt-in fallback. They are on in the
  legacy preset and off by default in minimal, because large projects may
  prefer reflection's fixed code size.
- **Hotfix:** stays on Cecil. ILPostProcessor is a possible later experiment.
- **Minimum Unity for the generator mode: 2021.3.** The compatibility spike
  ([SOURCEGEN_SPIKE.md](../../build/SOURCEGEN_SPIKE.md)) tested
  2021.3.45f2, 2022.3.62f3, 6000.0.85f1, 6000.3.26f1 and 6000.6.6f1. In all
  five, a 4.3.1 `IIncrementalGenerator`:
  - loads;
  - runs only in assemblies that reference the runtime asmdef;
  - reports diagnostics with file and line;
  - regenerates after an edit;
  - keeps its registration through High stripping (Mono player run; IL2CPP
    Android build output checked).

  Rules from the spike:
  - Build against Microsoft.CodeAnalysis.CSharp **4.3.1, never newer**. A
    4.10 generator only warns (CS9057) and silently generates nothing, so
    the runtime must detect that no registration ran and fall back to
    reflection.
  - The `RunOnlyOnAssembliesWithReference` label has no effect in any tested
    editor. Scope the generator by putting the DLL in the runtime asmdef's
    folder.
  - Only the latest patch of each line was tested. Document "latest 2021.3
    or 2022.3 patch".
