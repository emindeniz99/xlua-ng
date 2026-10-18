# Source-generated bindings

Status: **design, written 2026-10-17; milestones M0, M1 (host part), M2
(host part), M3 (host part), M4 (host part), M5 (host part), M7 (host part),
M8 (host part), M9 (host part) and M10 (host part) implemented 2026-10-18**
(see "M0 as implemented" to "M10 as implemented" after section 9; M6 is not
started; Android IL2CPP players with the generator passed on 2026-10-18,
"IL2CPP players with the source generator"). The `GEN_CODE_MINIMIZE` mode
was added on 2026-10-18 ("GCM mode as implemented"). It covers the work in
[presets.md](presets.md), "Legacy code generation goes before the first public
release", and the Bindings rows of [backlog.md](backlog.md).

Background:

- [research/binding-generation.md](../research/binding-generation.md)
- [SOURCEGEN_SPIKE.md](../../build/SOURCEGEN_SPIKE.md) and `Tools/SourceGenSpike/`
- [research/puerts.md](../research/puerts.md) and
  [research/focus-creative-games.md](../research/focus-creative-games.md)
- [research/obfuscators.md](../research/obfuscators.md)
- [research/hotfix.md](../research/hotfix.md)
- the UPM spike report and its Mode 2 prototype (local work files, not in the
  repository; `Tools/make_upm_package.py` implements its result)

Line numbers refer to commit `5bc720d`. Claims about the current code carry
`file:line`; anything not read directly in code is marked **[inferred]**.
Paths in short form:

| Short form | Path |
|---|---|
| `Gen.cs` | `Assets/XLua/Src/Editor/Generator.cs` |
| `T/` | `Assets/XLua/Src/Editor/Template/` |
| `OT.cs` | `Assets/XLua/Src/ObjectTranslator.cs` |
| other runtime files | `Assets/XLua/Src/` |

## 1. Goals and non-goals

### Goals

1. **Parity with the legacy generator before it is removed.** Everything in
   section 2 has a source-generator counterpart, or the owner has dropped it
   (section 10). The 335 Lua tests (`Test/Native/run_managed.py:39`) pass
   through generated code, as they do today with `--gen-code`.
2. **No partial classes of xLua or user types.** Today the generated code
   extends five runtime types:
   - `ObjectTranslator` (`T/LuaRegister.tpl.txt:94`, `T/LuaWrapPusher.tpl.txt:18`,
     `T/LuaClassWrapGCM.tpl.txt:123`);
   - `DelegateBridge` (`T/LuaDelegateBridge.tpl.txt:20`);
   - `InternalGlobals` (`T/LuaRegister.tpl.txt:100`);
   - `StaticLuaCallbacks` (`T/LuaWrapPusher.tpl.txt:197`);
   - `CopyByValue` (`T/PackUnpack.tpl.txt:18`).

   That only compiles when the generated code and the runtime are in the
   same assembly. The source generator emits types of its own and connects
   them through a small public binding API (section 3).
3. **XLua in its own asmdef**: `XLua` for the runtime, `XLua.Editor` for the
   tools, shipped as a read-only UPM package. Today XLua compiles into
   Assembly-CSharp. With an asmdef, Generate Code output fails to compile
   with 1052–1959 errors (UPM report, finding 2).
4. **Safe with High managed stripping.**
   - Generated code calls members directly, so the linker keeps exactly what
     is bound.
   - Registration runs from `[RuntimeInitializeOnLoadMethod]`, which the
     spike showed survives High stripping on Mono and in an IL2CPP Android
     build (SOURCEGEN_SPIKE.md, "IL2CPP, High stripping").
   - Nothing on the generated path looks members up by name.
5. **A name table that survives obfuscation.** Lua-visible type names, member
   names and the hotfix type keys come from compile-time literals plus
   `typeof`, never from `Type.FullName` or `MemberInfo.Name` at run time
   (obfuscators.md, recommendation 1).
6. **Unity 2021.3+, Roslyn 4.3.1.**
   - The generator is an `IIncrementalGenerator` using
     `ForAttributeWithMetadataName`, built against
     Microsoft.CodeAnalysis.CSharp 4.3.1 and nothing newer.
   - It does not call `RegisterPostInitializationOutput`.
   - It ships as a netstandard2.0 DLL labelled `RoslynAnalyzer` in the
     `XLua` asmdef folder, so it runs only in assemblies that reference XLua.

   These rules come from SOURCEGEN_SPIKE.md, "Recommendation", and
   `Tools/SourceGenSpike/Generator/XLuaTest.Generator.csproj:18-22`.

### Non-goals

- **No new binding features in v1** (async wrappers, `Span<T>` support,
  generic methods without constraints). Parity comes first; section 10 lists
  the changes that would alter behaviour.
- **No runtime code generation.** CodeEmit stays as it is, an opt-in fallback
  for the Editor and Mono (presets.md table).
- **No custom generation templates.** Custom templates (`Gen.cs:1954-1986`,
  `[GenCodeMenu]` at `Gen.cs:1851-1859`) have no source-generator form. The
  migration tool reports them (presets.md, condition 2).
- **No ILPostProcessor and no Roslyn newer than 4.3.1.** See
  binding-generation.md and SOURCEGEN_SPIKE.md note 2.
- **No hotfix rewrite.** Injection stays on Cecil. Only where the bridges come
  from changes (section 6).

## 2. Inventory of the legacy generator

The pipeline:

- **Entry points:** `XLua/Generate Code` (`Gen.cs:1861-1887`) and the CLI
  `XLuaGenerate.exe` (`Gen.cs:1827-1847`, `General/Src/XLuaGenerate.cs`).
  Both evaluate the configuration by reflection (`GetGenConfig`,
  `Gen.cs:1668-1733`, `MergeCfg` at `Gen.cs:1561-1650`).
- **Order of work:** delegate bridges, enums, classes, then the register file
  (`Gen.cs:1880-1884`), and finally custom generators.
- **Templates:** Lua text templates run on the Editor `LuaEnv`
  (`Gen.cs:762-799`), with helpers from `T/TemplateCommon.lua.txt`.

For the test configuration, the generated output on the build disk
(`gcm-5.5.1/Gen`, 2026-10-16) has:

- 57 files and 19,354 lines;
- 16,301 lines with `GEN_CODE_MINIMIZE`;
- 48 wrapped types, 23 delegate types folded into 18 bridge methods, 6
  interface bridges and 16 extension-method delegates.

| # | Item | Legacy source | Output | Source generator |
|---|---|---|---|---|
| 1 | **Class wrappers** | `GenWrap` `Gen.cs:1175-1226`; member model `getClassInfo` `Gen.cs:309-530`; template `T/LuaClassWrap.tpl.txt:120-595` | `XLua.CSObjectWrap.<Name>Wrap` with `__Register` (`:125-159`), `__CreateInstance` (`:161-232`), indexers (`:234-329`), operators (`:331-369`), methods (`:371-479`), getters/setters (`:482-526`), events (`:528-593`) | One `internal static` class per type in the assembly's own namespace (section 5.1). Same member selection and overload order: `OverloadCosting` `Gen.cs:216-234`, optional-parameter expansion `Gen.cs:387-401`, the `_xlua_st_` static/instance split `Gen.cs:371`, string-keyed `get_Item`/`set_Item` `Gen.cs:362-367`. It registers through `AssemblyBindings.Type` instead of `DelayWrapLoader` (`T/LuaRegister.tpl.txt:32`). |
| 1a | Minimized class wrappers (`GEN_CODE_MINIMIZE`) | `T/LuaClassWrapGCM.tpl.txt:123-160`: instance methods on `partial ObjectTranslator`, `int f(IntPtr L, int top)` called through the native `CSharpWrapper` trampoline (`OT.cs:1453-1490`) | `ObjectTranslator.__Register<Name>` | Static methods with a leading `ObjectTranslator` parameter, bound as closed `CSharpWrapper` delegates (`new CSharpWrapper(translator.W_m_Foo)` over an extension method). No partial class. Implemented 2026-10-18, selected by the define ("GCM mode as implemented"). |
| 1b | Lazy members (`DoNotGen`) | `Gen.cs:455-529` → `Utils.RegisterLazyFunc` `Utils.cs:1312-1324` → `LazyReflectionCall` `Utils.cs:799` | `RegisterLazyFunc` lines in `__Register` (`T/LuaClassWrap.tpl.txt:140,156`) | Same call. The path also becomes the per-member fallback (section 5.5). |
| 1c | Extension methods | `GetExtensionMethods` `Gen.cs:238-302`; register map `Gen.cs:1317-1330`; `T/LuaRegister.tpl.txt:100-135` | Extension methods are merged into the extended type's wrapper. `__GEN_DELEGATE<n>` delegate types, whose `.Method` fills `InternalGlobals.extensionMethodMap` (`InternalGlobals.cs:59`) without a name lookup. | Same merge into wrappers. The map is filled through `AssemblyBindings.ExtensionMethods(Type, MethodInfo[])`, still taking `MethodInfo`s from delegates (stripping-safe). The runtime merges per-assembly maps; today it holds one dictionary set from one static constructor (`T/LuaRegister.tpl.txt:118-138`). |
| 1d | Generic type wrappers (`GENERIC_SHARING`) | `Gen.cs:1179-1181`, `Gen.cs:1333-1351`, `T/LuaRegister.tpl.txt:42-67`, `OT.cs:969-982` | Generic wrapper classes | Not in v1 unless the owner keeps it: it is undocumented and untested. Closed generic types (`typeof(List<int>)`) are supported as ordinary types. |
| 2 | **Enum wrappers** | `GenEnumWrap` `Gen.cs:801-816`; `T/LuaEnumWrap.tpl.txt:18-102` (GCM: `T/LuaEnumWrapGCM.tpl.txt:20`) | One `EnumWrap.cs` with a class per enum: `__Register` and `__CastFrom`. Above 20 values it uses `RegisterEnumType` (`T/LuaEnumWrap.tpl.txt:39-47`). | One class per enum. The 20-value threshold and `__CastFrom` are kept. Enum push and caching go through the struct slots (row 5). |
| 3 | **Delegate wrappers** (`LuaCallCSharp` on a delegate type) | `Gen.cs:1202-1212`, `T/LuaDelegateWrap.tpl.txt` | `__CallMeta`/`__AddMeta`/`__SubMeta`. Unreachable today: `GetGenConfig` filters delegates out of `LuaCallCSharp` (`Gen.cs:1719`). | Not generated (parity with what actually runs). The template also has a latent bug: it reads the undefined Lua global `param_offset` (`T/LuaDelegateWrap.tpl.txt:85`), so a delegate with a `ref` struct parameter would fail to generate. |
| 4 | **Delegate bridges** (`__Gen_Delegate_Imp*`) | `GenDelegateBridge` `Gen.cs:1089-1151`; key `MethodInfoSimulation` `Gen.cs:871-903` and comparer `Gen.cs:1019-1059`; template `T/LuaDelegateBridge.tpl.txt:20-113` | Methods on `partial DelegateBridge`, a `GetDelegateByType` override with an `if (type == typeof(...))` chain (`:94-112`), and `Gen_Flag = true` in a static constructor (`:89-92`). | `internal static` extension methods `__Gen_Delegate_Imp<k>(this DelegateBridge b, ...)` in a per-assembly class. The registration adds `AssemblyBindings.Delegate(typeof(D), b => new D(b.__Gen_Delegate_Imp<k>))` per delegate type: a closed static delegate, so `Target` is still the bridge. `getDelegate` (`OT.cs:447-500`) asks the registry first, instead of the virtual call (`OT.cs:449`) and the name scan (`OT.cs:466-469`). Grouping uses the canonical key (section 5.4). |
| 5 | **Interface bridges** | `GenInterfaceBridge` `Gen.cs:823-837`; model `Gen.cs:540-558`; `T/LuaInterfaceBridge.tpl.txt:18-396` | `<Name>Bridge : LuaBase, I` with `__Create`; registered by `AddInterfaceBridgeCreator` (`T/LuaRegister.tpl.txt:79`) | Same class shape (`internal sealed`, deriving from public `LuaBase`), registered through `AssemblyBindings.InterfaceBridge`. Its uses of `luaEnv.L`, `translator`, `errorFuncRef` and `luaEnvLock` go through `BindingApi` (section 3). |
| 6 | **GCOptimize pushers** | `GenWrapPusher` `Gen.cs:1153-1173`; `T/LuaWrapPusher.tpl.txt:16-195` | On `partial ObjectTranslator`: per-translator fields `<T>_TypeID` and `<T>_EnumRef` (`:53-55`); `Push<T>`, `Get(out T)`, `Update<T>` (`:56-158`); table-optimised `Get` (`:162-193`); an `IniterAdder` static-constructor trick that registers through `LuaEnv.AddIniter` (`:20-46`) | `internal static` `Push`/`Get`/`Update` taking `ObjectTranslator`. The fields become **struct slots**: a process-wide slot index per type (`BindingApi.AllocateSlot`) and a per-translator `int[]` read by `BindingApi.TypeId(t, L, slot)` and `EnumCacheRef`. Registered per `LuaEnv` with `RegisterPushAndGetAndUpdate`/`RegisterCaster` (`OT.cs:1644-1691`), which are already public. |
| 7 | **GCOptimize pack/unpack** (`CopyByValue`) | `GenPackUnpack` `Gen.cs:1443-1463`; float grouping `Gen.cs:1399-1441`; sizes `Gen.cs:1735-1796`; `T/PackUnpack.tpl.txt:18-122` | Overloads on `static partial class CopyByValue` | A per-assembly `internal static class Pack`. Primitive fields call the public `CopyByValue.Pack`/`UnPack` (`CopyByValue.cs:26-142`); nested structs call the generated overloads. The `xlua_pack_float2..6` grouping is kept. |
| 8 | **Array fast paths** | `T/LuaWrapPusher.tpl.txt:197-232` (`StaticLuaCallbacks.__tryArrayGet/Set`), wired at `T/LuaRegister.tpl.txt:136-137` into `InternalGlobals.genTryArrayGetPtr/SetPtr` (`InternalGlobals.cs:31-34`); read at `StaticLuaCallbacks.cs:363-367,523-527` and `ObjectCasters.cs:538-539` | Two single static delegates | `AssemblyBindings.ArrayAccessor<T>`: a `Dictionary<Type, …>` keyed by array type, so several assemblies can contribute and the lookup is O(1) instead of an `if` chain. |
| 9 | **Registration initer** | `GenLuaRegister` `Gen.cs:1306-1360`; `T/LuaRegister.tpl.txt:20-98` | `XLua_Gen_Initer_Register__` with `wrapInit<n>` batches of 50. Its static constructor calls `LuaEnv.AddIniter` (`:83-86`), triggered by `ObjectTranslator.s_gen_reg_dumb_obj` (`:94-98`) or, under `XLUA_GENERAL`/UWP, by reflection on that field (`OT.cs:228-234`). | One `Bindings` class per assembly. `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` and, in the Editor, `[InitializeOnLoadMethod]` fill an `AssemblyBindings` and call `BindingApi.Register` (section 5.2). |
| 10 | **`link.xml`** | `LinkXmlGen.cs:13-33`, `LinkXmlGen.tpl.txt:5-13`, run through `[GenCodeMenu]` | `Gen/link.xml` with `preserve="all"` for every `ReflectionUse` and `LuaCallCSharp` type. This keeps 36 KB more of `libunity.so` for `GameObject` and `Transform` (SIZE.md, "Reached only through reflection"). | None for wrapped members: direct calls keep them. Entries only for members that are reached by reflection (`ReflectionUse`, `DoNotGen`, per-member fallback), written per member. The generator embeds the list in an Editor-only constant; an `IUnityLinkerProcessor` writes it at build time (section 5.7). |
| 11 | **Hotfix bridges** | `Gen.cs:1094-1145` adds `__Gen_Hotfix_Delegate<n>` signatures (`makeHotfixMethodInfoSimulation` `Gen.cs:970-1017`, `injectByGeneric` `Gen.cs:1061-1082`) to the delegate bridge set | `__Gen_Delegate_Imp*` on `DelegateBridge`; the injector finds them by name (`Hotfix.cs:306-308`) | A per-assembly bridge class emitted in each assembly that has `[Hotfix]` types (section 6). |
| 12 | **LuaCATS interplay** | `LuaCATSGenerator.cs:1271,1282` calls `GetGenConfig` and repeats the member filters | `.lua` definition files and `.luarc.json` | Generators cannot write non-C# files. The menu stays, fed by a per-assembly **manifest** (bound types, members, overloads, fallback flags). The source generator emits it under `#if UNITY_EDITOR`; the menu reads it after compilation. The member filters then live only in the generator: "generate type definitions from exactly the binding set" (focus-creative-games.md, zlua lessons). |
| 13 | **Generated-code flag and build check** | `DelegateBridge.Gen_Flag` (`DelegateBridge.cs:130`); `CheckGenerate` throws after a build without it (`Gen.cs:2032-2043`); CodeEmit is gated on it (`OT.cs:161,309,545`) | | `Gen_Flag` means "at least one assembly registered". CodeEmit and reflection are chosen per type, not globally. The build check becomes an `IPreprocessBuildWithReport` that finds assemblies with xLua configuration but no generated `Bindings` (the CS9057 case, SOURCEGEN_SPIKE.md note 2). |
| 14 | **`ref readonly` handling** | `Gen.cs:592-691` guesses the compiler from `UNITY_6000_x` defines; pragmas from `RefReadOnlyPragma` (`T/TemplateCommon.lua.txt:398-409`) | Members skipped with a warning | The generator runs inside the compiler. It reads the parse options' language version and the compiler assembly version **[inferred: `typeof(Compilation).Assembly` version]** and decides exactly. Members still unusable go to the per-member fallback or are reported. |
| 15 | Hotfix id map location | `Hotfix.cs:833,842,1707-1714` write into `GeneratorConfig.common_path + "Resources/"`, the `Gen` folder | `hotfix_id_map*.lua.txt` | `Gen` goes away. The id maps move to a project folder that stays writable (`Assets/XLuaGenerated/Resources/` by default), which the injector owns. |
| 16 | Dead or legacy-only bits | `GenUsingCLI` (`Gen.cs:1864-1870,1890-1943`, Unity 2018 only); `ClearAll` (`Gen.cs:1946-1950`); `USE_UNI_LUA` aliases in every template (no UniLua source exists); `UNITY_WSA` `.GetMethodInfo()` (`T/LuaRegister.tpl.txt:126-130`); `NOT_GEN_WARNING` (`OT.cs:174-183`) | | Dropped, except that `NOT_GEN_WARNING` becomes the per-member and per-type fallback log (section 5.5). |
| 17 | Validation | `SetupValidator.cs:245-286` re-evaluates the config to check `BlackList` and `DoNotGen` names | Editor report | Compiler diagnostics with file and line (`XLSG0xx`, section 5.8). The validator keeps the project-level checks: the generator DLL is imported with its label, and no CS9057. |

Two things the source generator does not need from the legacy pipeline:

- **The Editor `LuaEnv` and the `template` library.** The generator builds C#
  text in C#, which matches the backlog decision "the source generator emits
  C# without templates".
- **The `GeneratorConfig` paths** (`Gen.cs:24-88`), except the LuaCATS ones.

## 3. The public binding API

### What generated code uses today that is not public

Every member below is reached from generated code. That works today only
because `Gen/` compiles into the same assembly as XLua. Gathered from the
templates and from counting member uses in the generated test output.

| Member | Kind today | Where declared | Used by |
|---|---|---|---|
| `ObjectTranslator.FastGetCSObj(IntPtr, int)` | `internal` | `OT.cs:1410` | every instance member wrapper, 243 uses (`T/TemplateCommon.lua.txt:424-426`) |
| `ObjectTranslator.objectCasters` + `ObjectCasters.GetCaster` | `internal` field | `OT.cs:115`, `ObjectCasters.cs:725` | GCOptimize `Get` fallback (`T/LuaWrapPusher.tpl.txt:128`) |
| `ObjectTranslator.getTypeId(..., out bool, LOGLEVEL)` | `internal` | `OT.cs:1058` | GCOptimize push (`T/LuaWrapPusher.tpl.txt:61`); the public `GetTypeId` (`OT.cs:1038`) is enough |
| `LuaIndexes.LUA_REGISTRYINDEX` | `internal` class | `OT.cs:92-104` | enum cache refs (`T/LuaWrapPusher.tpl.txt:66`) |
| `Utils.EnsureStack(IntPtr, int)` | `internal` | `Utils.cs:1649` | wrappers with more than 10 results, bridges (`T/LuaClassWrap.tpl.txt:198,442`, `T/LuaDelegateBridge.tpl.txt:59-61`, `T/LuaInterfaceBridge.tpl.txt:66-68`) |
| `Utils.UncheckedPushes` | `internal const` | `Utils.cs:1636` | same; a constant, so the generator can inline `10` |
| `LuaEnv.rawL` | `internal` field | `LuaEnv.cs:30` | delegate bridges (`T/LuaDelegateBridge.tpl.txt:52`) |
| `LuaEnv.L` | `internal` property | `LuaEnv.cs:32` | interface bridges (`T/LuaInterfaceBridge.tpl.txt:60,127`) |
| `LuaEnv.translator` | `internal` field | `LuaEnv.cs:46` | bridges (`T/LuaDelegateBridge.tpl.txt:63`, `T/LuaInterfaceBridge.tpl.txt:70`) |
| `LuaEnv.errorFuncRef` | `internal` field | `LuaEnv.cs:48` | interface bridges (`T/LuaInterfaceBridge.tpl.txt:69`) |
| `LuaEnv.luaEnvLock` | `internal` property | `LuaEnv.cs:53` | every bridge under `THREAD_SAFE \|\| HOTFIX_ENABLE` |
| `DelegateBridgeBase.errorFuncRef` | `protected` | `DelegateBridge.cs:33` | delegate bridges (`T/LuaDelegateBridge.tpl.txt:62`) |
| `LuaBase.luaReference`, `LuaBase.luaEnv` | `protected` | `LuaBase.cs:26-27` | bridges. Subclasses in other assemblies (interface bridges) can use them; static delegate bridges cannot. |
| `InternalGlobals.extensionMethodMap` | `internal static` | `InternalGlobals.cs:59` | register file (`T/LuaRegister.tpl.txt:120`) |
| `InternalGlobals.genTryArrayGetPtr/SetPtr`, `TryArrayGet/TryArraySet` | `internal` | `InternalGlobals.cs:31-34` | register file (`T/LuaRegister.tpl.txt:136-137`) |
| `partial class DelegateBridge` | partial | `DelegateBridge.cs:126` | delegate and hotfix bridges, `GetDelegateByType`, `Gen_Flag` |
| `partial class ObjectTranslator` | partial | `OT.cs:111` | registration trigger, struct pushers, GCM wrappers |
| `partial class InternalGlobals` | partial | `InternalGlobals.cs:25` | extension map, array hooks |
| `partial class StaticLuaCallbacks` | partial | `StaticLuaCallbacks.cs:25` | `__tryArrayGet/Set` |
| `static partial class CopyByValue` | partial | `CopyByValue.cs:23` | struct pack/unpack |

### Options

**`InternalsVisibleTo` is rejected.**

- The friend assemblies have to be named when XLua is compiled. A read-only
  UPM package cannot know a project's asmdef names. The Mode 2 prototype
  could name only `Assembly-CSharp` (`upm-spike/tools/proto/GeneratedCodeHooks.cs:8`).
- It would expose every internal member, with no contract, so a runtime
  refactor would break generated code silently.
- `IgnoresAccessChecksToAttribute` is honoured only by the runtime, not by the
  C# compiler **[inferred]**, so it cannot replace it either.

**A small, versioned public surface is proposed.** It lives in the
`XLua.Binding` namespace, and every member carries
`[EditorBrowsable(EditorBrowsableState.Never)]` so that IDE completion hides
it. It has two types:

```csharp
namespace XLua.Binding
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class BindingApi
    {
        public const int Version = 1;

        // Registration
        public static void Register(AssemblyBindings bindings);
        public static int AllocateSlot(Type type);                 // struct and enum slots, process-wide

        // Translator state that was internal
        public static object FastGetCSObj(ObjectTranslator t, IntPtr L, int index);
        public static ObjectCast GetCaster(ObjectTranslator t, Type type);
        public static int TypeId(ObjectTranslator t, IntPtr L, int slot);        // replaces <T>_TypeID fields
        public static int EnumCacheRef(ObjectTranslator t, IntPtr L, int slot);  // replaces <T>_EnumRef fields
        public static int RegistryIndex { get; }
        public static void EnsureStack(IntPtr L, int extra);

        // Bridge state that was internal or protected
        public static IntPtr State(LuaBase o);        // luaEnv.L: checks for a disposed env
        public static IntPtr RawState(LuaBase o);     // luaEnv.rawL: hot path of delegate bridges
        public static ObjectTranslator Translator(LuaBase o);
        public static int ErrorFuncRef(LuaBase o);
        public static int Reference(LuaBase o);
        public static object Lock(LuaBase o);
        public static void ThrowError(LuaBase o, int oldTop);   // luaEnv.ThrowExceptionFromError
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class AssemblyBindings
    {
        public AssemblyBindings(string assemblyName, int apiVersion);
        public void Type(Type type, string luaName, Action<IntPtr> loader);   // wrapper + name table
        public void Name(Type type, string luaName);                          // name table only
        public void InterfaceBridge(Type type, Func<int, LuaEnv, LuaBase> create);
        public void Delegate(Type delegateType, Func<DelegateBridge, Delegate> create);
        public void Struct<T>(Action<ObjectTranslator, IntPtr, T> push,
                              ObjectTranslator.GetFunc<T> get,         // wrapped per translator
                              Action<ObjectTranslator, IntPtr, int, T> update);
        public void Caster<T>(Action<ObjectTranslator, IntPtr, int, T> get);  // PackAsTable / table-optimised types
        public void ArrayAccessor<T>(...);                                  // T[] get/set without boxing
        public void ExtensionMethods(Type extended, MethodInfo[] methods);
        public void HotfixSlots(DelegateBridge[] slots, string idMapName);  // section 6
        public void Initer(Action<LuaEnv, ObjectTranslator> init);          // escape hatch, = LuaEnv.AddIniter
    }
}
```

Notes on the shape:

- **Size:** 17 static members on `BindingApi` and 11 members on
  `AssemblyBindings`, about 28 new public members in all. Most are one-line
  forwarders that IL2CPP's C++ compiler should inline **[inferred]**.
- **Versioning:**
  - Generated code references `BindingApi.Version`.
  - The generator reads the constant from the referenced XLua assembly
    (`compilation.GetTypeByMetadataName`) and reports XLSG007 when it does not
    match the version the generator was built for. A package mismatch then
    fails at compile time with a clear message.
  - `Register` checks `apiVersion` again at run time.
  - Removing or changing a member means a new `Version`.
- **Existing public members become part of the contract:**
  - `ObjectTranslatorPool.Instance.Find` (`ObjectTranslatorPool.cs:66`);
  - the `Utils.Begin/End{Object,Class}Register`, `RegisterFunc`,
    `RegisterLazyFunc`, `RegisterObject`, `RegisterEnumType`, `LoadField`,
    `LoadCSTable` helpers and the `*_IDX` constants (`Utils.cs:63,734,1106-1439,1463`);
  - on `ObjectTranslator`: `Get<T>`, `Push`, `PushAny`, `PushByType`,
    `GetObject`, `Assignable<T>`, `GetDelegate<T>`, `GetParams<T>`, `Update`,
    `PushDecimal`, `IsDecimal`, `TranslateToEnumToTop`, `GetTypeId`,
    `RegisterPushAndGetAndUpdate`, `RegisterCaster` (`OT.cs:875-1723`);
  - `LuaEnv.ThrowExceptionFromError` (`LuaEnv.cs:431`);
  - `DelegateBridge.PCall` (`DelegateBridge.cs:136`);
  - `CopyByValue`'s primitive overloads;
  - `MonoPInvokeCallbackAttribute` (`OT.cs:48`);
  - `LazyMemberTypes` (`Utils.cs:30`);
  - all of `LuaDLL.Lua`. Every function the templates call is already public,
    checked one by one in `LuaDLL.cs`.

  That is about 45 members. They need no visibility change, but they get a
  comment saying generated code depends on them. A test compiles the
  generated output for the test configuration against the API, so any
  change shows up.
- **Members that change:**
  - `LuaBase`'s `_L`, `_translator` and `_errorFuncRef` (`LuaBase.cs:29-33`,
    Editor and `XLUA_GENERAL` only, used by CodeEmit) stay as they are.
  - `DelegateBridge.Gen_Flag` (`DelegateBridge.cs:130`) is set by `Register`
    instead of by a generated static constructor.
- **Configuration attributes** (section 4): the attribute types in
  `GenAttributes.cs:22-123` gain constructors and `AttributeUsage`. That
  changes existing public types but adds no members to the binding API.

**Rejected alternative:** a generated `DelegateBridge` subclass per assembly,
as the Mode 2 prototype did (`upm-spike/tools/proto/DelegatesGensBridge.cs`,
`GeneratedCodeHooks.cs`).

- It works for one assembly.
- It does not compose. A Lua function has one bridge object, created at
  `OT.cs:552` and shared by every delegate type it is converted to
  (`OT.cs:511-528`). That object can be an instance of only one subclass, so
  bridges from a second assembly could not attach to it.
- Static extension methods bound as closed delegates attach to any bridge.
  The C# compiler emits them as `ldftn` plus `newobj` with the bridge as
  target. IL2CPP supports closed static delegates **[checked 2026-10-18:
  the M0 bridges in Android ARM64 IL2CPP players from Unity 6000.3.21f1 and
  2021.3.45f2 and a 6000.6.4f1 WebGL player, `Test/MobileSmoke` with
  `--gen`]**.

## 4. Configuration

### Attributes

Configuration moves from static lists, evaluated by Editor reflection
(`Gen.cs:1697-1716`, `Hotfix.cs:82-107`), to attributes that the compiler
sees. Each attribute class in `GenAttributes.cs` gets:

- `[AttributeUsage(AttributeTargets.Assembly | <the targets it has today>, AllowMultiple = true)]`.
  Today none has `AttributeUsage`, so `AllowMultiple` is false and two
  `[assembly: LuaCallCSharp(...)]` lines would be CS0579.
- A constructor that takes the type, plus the flags it has today.

```csharp
// Assembly level: types the project may not own
[assembly: LuaCallCSharp(typeof(UnityEngine.Transform))]
[assembly: LuaCallCSharp(typeof(System.Collections.Generic.List<int>))]
[assembly: CSharpCallLua(typeof(System.Action<float>))]
[assembly: CSharpCallLua(typeof(Game.IScriptBehaviour))]
[assembly: GCOptimize(typeof(Game.HitInfo))]
[assembly: GCOptimize(typeof(Game.Pair), OptimizeFlag.PackAsTable)]
[assembly: AdditionalProperties(typeof(UnityEngine.Ray), "origin", "direction")]
[assembly: ReflectionUse(typeof(Game.Settings))]
[assembly: DoNotGen(typeof(Game.Big), "RarelyUsed")]
[assembly: BlackList(typeof(UnityEngine.Light), "shadowAngle")]
[assembly: BlackList(typeof(System.IO.File), "Open", typeof(string), typeof(System.IO.FileMode))]
[assembly: BlackList("UnityEngine.MonoBehaviour", "runInEditMode")]  // by name: the member may not exist on every platform
[assembly: Hotfix(typeof(Game.Shop), HotfixFlag.IntKey)]

// Type and member level, as today
[LuaCallCSharp] public class Player { [BlackList] public void Debug() {} }
[CSharpCallLua] public delegate int Score(Player p);
[GCOptimize] public struct HitInfo { public float x, y; }
[Hotfix] public class Shop { }

// Generator options, one per assembly
[assembly: XLuaBindingOptions(Minimize = false, ReflectionFallback = true)]
```

Rules:

- **Where the code goes:** an attribute generates code in the assembly that
  declares it. Type-level attributes, which today also feed the static-list
  path (`Gen.cs:1699`), keep working unchanged.
- **The by-name `BlackList`** covers members missing on some build targets,
  as the string lists do today (`Gen.cs:709-757`). A name that matches
  nothing gives warning XLSG003.
- **`Func<MemberInfo, bool>` filters** (`Gen.cs:1617-1620`) cannot run inside
  the compiler. The migration tool evaluates them and writes the matching
  `BlackList` entries.
- **The system defaults** in `XLua.SysGenConfig`, GCOptimize for Unity math
  types (`GenAttributes.cs:131-164`), become `[assembly: GCOptimize]` and
  `[assembly: AdditionalProperties]` in the XLua assembly. Every generator
  sees them through the referenced assembly's attributes (section 5.1).
- **The same configuration is readable by reflection.** Assembly attributes
  are metadata, so the Editor (migration tool, LuaCATS, validator) and the
  `XLuaGenerate` CLI can read them with `Assembly.GetCustomAttributes`.
- **Option to evaluate:** `[Conditional("UNITY_EDITOR")]` on the assembly-level
  form would keep the configuration out of player metadata. Roslyn still
  binds conditional attributes in source, so the generator still sees them
  **[inferred]**. The cost: CodeEmit's runtime scan (`OT.cs:312-339`) would
  not find them in a Mono player. Measure the metadata saving first.

### Migration tool

It runs in the Editor, where the old configuration can still be evaluated by
reflection, and writes one generated C# file per target assembly, for example
`Assets/XLuaConfig/XLuaBindings.Config.cs`.

| Today | Written as |
|---|---|
| `[LuaCallCSharp] static IEnumerable<Type> X` (any expression, including LINQ over `AppDomain`, as in `Assets/XLua/Editor/ExampleConfig.cs`) | The evaluated list, sorted, one `[assembly: LuaCallCSharp(typeof(T))]` per public, non-obsolete type. It applies `GetGenConfig`'s filters (`Gen.cs:1717-1732`) so that the result is the set the legacy generator used. |
| `[CSharpCallLua]`, `[GCOptimize(flag)]`, `[ReflectionUse]` lists | The same, with the flag kept. Conflicting GCOptimize flags fail, as at `Gen.cs:1530-1541`. |
| `[LuaCallCSharp(GenFlag.GCOptimize)]` (obsolete, `Gen.cs:1569`) | Both `LuaCallCSharp` and `GCOptimize` |
| `[BlackList] List<List<string>>` | `BlackList("Type", "member", "paramType"...)` by name; by `typeof` when the type resolves in the target assembly |
| `[BlackList] Func<MemberInfo, bool>` | Evaluated against every member of every configured type; one `BlackList` per match. Reported as "re-run the tool when the filter changes". |
| `[DoNotGen] Dictionary<Type, List<string>>` | `DoNotGen(typeof(T), "a", "b")` |
| `[AdditionalProperties] Dictionary<Type, List<string>>` | `AdditionalProperties(typeof(T), "a", "b")` |
| `[Hotfix] IEnumerable<Type>` (`Hotfix.cs:82-107`) | `[assembly: Hotfix(typeof(T), flags)]` in the assembly that **defines** T. A source generator sees only its own compilation, and the bridges must sit next to the injected type (section 6). |
| Type-level attributes | Unchanged |

Where external types go: the tool writes their attributes into a chosen
"bindings host":

- by default Assembly-CSharp;
- optionally a dedicated `XLuaBindings.asmdef` that the tool creates. It
  references the game assemblies the bound types come from. Since the game
  code does not reference it, an edit in game code does not recompile it
  (section 7).

The project's own types go to the assembly that defines them.

It reports what it cannot convert, as presets.md requires:

- custom templates and `[GenCodeMenu]` methods;
- `GenPath` (`Gen.cs:108-111`);
- `GENERIC_SHARING`;
- the legacy-only features presets.md lists;
- configuration types it could not evaluate, for example those that throw.

## 5. Generated code shape

### 5.1 Per-assembly naming (no CS0436)

- **Namespace:** each assembly's output goes into
  `XLua.Generated.<Id>`, where `<Id>` is the sanitised assembly name plus the
  first 8 hex digits of a hash of the full name. The suffix keeps `My.Game`
  and `My_Game` apart.
- **Visibility:** every generated type is `internal`. Internal types never
  clash with a referenced assembly's types, even when test asmdefs grant
  `InternalsVisibleTo` to each other. The spike saw no CS0436 with internal,
  per-assembly names (SOURCEGEN_SPIKE.md, results table).
- **Files:** one `AddSource` per bound type
  (`W.UnityEngine.Transform.g.cs`), plus `Bindings.g.cs`, `Bridges.g.cs`,
  `Structs.g.cs` and `Manifest.g.cs`.
- **File content:** each file starts with `// <auto-generated/>` and
  `#nullable disable`, uses only C# 9 syntax (Unity's default
  `-langversion:9`) and qualifies every type with `global::`.
- **Determinism:** output is sorted by metadata name, so identical input
  gives byte-identical output.
- **Cross-assembly marker:** the generator emits
  `[assembly: XLua.Binding.GeneratedBindings(typeof(Bindings), BindingApi.Version, new[] { typeof(T1), ... })]`.
  A generator in an assembly that references this one reads the attribute.
  It then skips types that are already bound there (info XLSG008) and learns
  their GCOptimize settings. Assemblies that do not reference each other may
  both bind a type. The runtime keeps the first registration and, in
  development builds, logs the duplicate.

### 5.2 Registration through `RuntimeInitializeOnLoadMethod`

```csharp
namespace XLua.Generated.Game_1a2b3c4d
{
    [global::UnityEngine.Scripting.Preserve]
    internal static class Bindings
    {
#if UNITY_5_3_OR_NEWER
        [global::UnityEngine.RuntimeInitializeOnLoadMethod(
            global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        [global::UnityEngine.Scripting.Preserve]
        internal static void Register()
        {
            var b = new global::XLua.Binding.AssemblyBindings("Game", global::XLua.Binding.BindingApi.Version);
            b.Type(typeof(global::UnityEngine.Transform), "UnityEngine.Transform", W_UnityEngine_Transform.__Register);
            b.Delegate(typeof(global::System.Action<float>), d => new global::System.Action<float>(d.__Gen_Delegate_Imp3));
            b.Struct<global::Game.HitInfo>(Structs.Push_Game_HitInfo, Structs.Get_Game_HitInfo, Structs.Update_Game_HitInfo);
            b.Name(typeof(global::Game.Shop), "Game.Shop");   // hotfix-only type, for the name table
            global::XLua.Binding.BindingApi.Register(b);
        }
#if UNITY_EDITOR
        [global::UnityEditor.InitializeOnLoadMethod]
        static void EditorRegister() { Register(); }
#endif
    }
}
```

- **Timing:**
  - `SubsystemRegistration` is the earliest load type. The Mode 2 prototype
    used it in a High-stripping Mono player (`proto/DelegatesGensBridge.cs`).
  - The spike used `BeforeSceneLoad` in IL2CPP
    (`RuntimeInitializeOnLoads.json`, `loadTypes=1`).
  - `InitializeOnLoadMethod` covers edit-mode tests, which never enter play
    mode.
- **Idempotent:** `Register` ignores an assembly name it has already seen.
  With domain reload off (Enter Play Mode options), the callbacks run again
  each play.
- **Existing `LuaEnv`s:** a `LuaEnv` created before registration (for
  example from another `SubsystemRegistration` callback) runs the registry's
  initers in its constructor, at the same point where `initers` run today
  (`LuaEnv.cs:181-186`). A late `Register` also applies itself to `LuaEnv`s
  that are already live.
- **Hosts without Unity** (`XLUA_GENERAL`, the Mono test suite):
  `LuaEnv`'s constructor calls `BindingApi.EnsureRegistered()` once when the
  registry is empty. It scans `AppDomain.CurrentDomain.GetAssemblies()` for
  `GeneratedBindings` attributes and calls their `Register` methods. This
  replaces the reflection on `s_gen_reg_dumb_obj` (`OT.cs:228-234`) and is
  what the prototype's `EnsureRegistered` did. In players it is only a
  fallback, because a stripped player might drop assembly attributes
  **[inferred]**.

### 5.3 The runtime fallback when no registration ran

A generator built against a newer Roslyn loads with warning CS9057 and
silently generates nothing (SOURCEGEN_SPIKE.md note 2). Three layers catch
it:

1. **Editor, after each domain reload.** For every loaded assembly that
   references XLua and carries xLua configuration attributes but has no
   `GeneratedBindings` attribute, it logs an error naming the assembly and the
   likely cause:
   - the generator DLL is missing its `RoslynAnalyzer` label;
   - CS9057;
   - the Unity version is older than 2021.3.
2. **Build.** An `IPreprocessBuildWithReport` makes the same check.
   - It **fails** the build for IL2CPP targets when `CSharpCallLua`
     configuration exists. Without bridges, those delegates throw
     `InvalidCastException` at run time (`OT.cs:499,643`).
   - Otherwise it warns. It replaces `CheckGenerate` (`Gen.cs:2032-2043`).
3. **Run time.** `TryDelayWrapLoader` (`OT.cs:142-200`) already falls back to
   `ReflectionWrap` or CodeEmit per type. When the registry is empty, the
   first `LuaEnv` logs once: "no source-generated bindings registered; using
   reflection". In players this is compiled in only for development builds.

### 5.4 The signature dedup key

One function, `SignatureKey.Of(IMethodSymbol m, KeyPurpose purpose)`, returns
a canonical string. The delegate bridges and the hotfix bridges use it, and
so does the per-type list of struct helpers, so no other code compares
signatures.

```text
Key       := Ret "(" [Param {"," Param}] ")"
Ret       := "void" | Type
Param     := Mode Type ["..."]           ; "..." = params array
Mode      := "" | "ref " | "out " | "in " ; "in" also covers C# 12 ref readonly (IsInParam)
Type      := metadata name with generic arguments, "global::" form; nullable
             annotations, tuple element names and dynamic→object normalised away
```

- **`KeyPurpose.DelegateBridge`** reproduces the legacy comparer exactly
  (`Gen.cs:1019-1059`): same return type and same parameter types, same
  `IsOut`, same `IsInParam`, and the same params flag on the last parameter.
  This keeps the 23 → 18 folding of the test configuration, and the bridge
  sets of the two generators can be compared one to one.
- **`KeyPurpose.Hotfix(flags)`** first applies the legacy reduction
  (`Gen.cs:970-1017`): `self` comes first, and value types become `object`
  under `ValueTypeBoxing`. Other non-ref, non-params parameters become
  `object`. Then it builds the same string.
- **Possible follow-up (not v1, measured first):** fold reference-type
  parameters of delegate bridges to `object`. Method group conversion allows
  that contravariance, and `translator.Push(object)` is the push those
  parameters use anyway (`T/TemplateCommon.lua.txt:434-451`).
- **Not deduplicated:** Lua→C# member wrappers. Each one calls a different
  member directly. Only their argument checks and casts are shared, through
  `TemplateCommon`'s typed helpers, which become C# helper methods.
- **Tests:** a table-driven unit test of `SignatureKey` (pairs of
  signatures, same or different, for each rule above), as zlua does
  (`Editor/Utils/NameUtil.cs:451-469` there).

### 5.5 Per-member reflection fallback

Legacy behaviour is all or nothing:

- A member the generator cannot handle is **dropped from Lua**:
  `isMethodInBlackList`/`isMemberInBlackList` (`Gen.cs:693-759`), and
  `isRefReadOnlyUnusable` with its warning (`Gen.cs:650-675`).
- A type with no generated wrapper falls back to reflection as a whole
  (`OT.cs:153-173`).

The source generator decides per member. For a member it cannot generate,
it emits `Utils.RegisterLazyFunc` (`Utils.cs:1312`) with a reason code:

- an inaccessible type in the signature;
- `ref readonly` on an old compiler;
- an unconstrained generic method;
- an unsupported indexer shape.

This is the path `DoNotGen` already uses. `LazyReflectionCall`
(`Utils.cs:799-950`) wraps the member on first use and caches it in the
table (`Utils.cs:940-942`). It also logs **once per member** in the Editor and
in development builds, keyed by (Type, name) in a static set, with the reason
text compiled only into those builds.

Not every member can fall back:

- **Members reflection cannot call either** (pointers, ref structs:
  `Gen.cs:585-590`) stay dropped. The manifest records them, and info
  XLSG004 names them.
- **Blacklisted members** stay dropped.

The fallback members need preservation (section 5.7) and are not safe under
obfuscation (section 5.6). The XLSG004 diagnostic says so.

Whether this is a behaviour change in the `legacy` preset is section 10,
question 6.

### 5.6 The compile-time name table

`AssemblyBindings.Type` and `AssemblyBindings.Name` record (`typeof(T)`, the
original Lua path as a literal). Member names are already literals in
`RegisterFunc` calls (`T/LuaClassWrap.tpl.txt:130-157`). The runtime uses the
table at every name lookup that obfuscators.md lists:

| Site today | With the name table |
|---|---|
| `CS.A.B` resolution: `FindType` → `assembly.GetType(className)` (`OT.cs:768-778`) | Look the original name up in the table first, then fall back to `GetType` (unbound types) |
| `CS` table path: `getPathOfType` uses `type.Namespace` and `type.ToString()` (`Utils.cs:1441-1460`) | The table's path for bound and named types |
| Metatable keys: `luaL_newmetatable(L, type.FullName)` (`Utils.cs:1116,1120`, `OT.cs:148,1080,1088`, `StaticLuaCallbacks.cs:949`, `Utils.cs:750,959,963`) | The key stays unique after renaming, so it keeps working. Use the table name when there is one, so keys and error messages show original names. |
| Delegate bridge discovery by the `__Gen_Delegate_Imp` prefix (`OT.cs:466-469`) | Gone on the generated path: the registry is keyed by `Type` |
| IntKey hotfix `hotfix_id_map[typeof(cs):ToString()]` (`xlua.util` in `EmbeddedModules.cs`, formerly `Resources/xlua/util.lua.txt:84`) | `xlua.typename(cs)` returns the table name, which matches the injector's map keys (original names) |
| Field-mode hotfix `xlua.access(cs, '__Hotfix0_'..k)` (`LuaEnv.cs:566-585`) | The fields are injected after compilation, so the table cannot cover them. Prefer IntKey, and emit `[Obfuscation(Exclude)]` on injected fields (presets.md). |
| Reflection fallback members | Not covered, by definition. XLSG004 says so, and the obfuz adapter excludes them from renaming. |

Every `[Hotfix]` and `ReflectionUse` type is added with `Name()` even when
it has no wrapper, so `CS.Game.Shop` resolves after renaming.

### 5.7 Members preserved one by one, not `preserve="all"`

- **Wrapped members:** nothing to do. Direct calls keep them, as the spike
  showed for High stripping on Mono and in the IL2CPP output.
- **Members reached by reflection** (`ReflectionUse` types, `DoNotGen`,
  per-member fallback): the generator writes a link.xml fragment per
  assembly into an Editor-only constant
  (`#if UNITY_EDITOR internal const string LinkXml`).
  - It lists each member, using `<method signature=...>`, `<field>` and
    `<property>` elements.
  - A `ReflectionUse` type stays `preserve="all"` only when the user asks
    for it with `ReflectionUse(typeof(T), Preserve = All)`, which is today's
    behaviour (`LinkXmlGen.cs:16-26`).
- **How it reaches the linker:** an Editor `IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile`
  collects the fragments and returns one file. It reads the player-compiled
  assemblies (from `UnityLinkerBuildPipelineData.inputDirectory`) with the
  Cecil that hotfix already bundles. Reading them, rather than the Editor
  assemblies, keeps player-only `#if` branches right **[inferred: API present
  in 2021.3; check in M5]**.
- **Under obfuscation:** obfuz rewrites the `link.xml` files it knows about
  (obfuscators.md, "Other bridges"). A file generated at linker time carries
  original names. The fallback members are excluded from renaming anyway
  (5.6), so the names still match.

### 5.8 Diagnostics

| Id | Severity | When |
|---|---|---|
| XLSG001 | warning | Generic type definition configured (skipped, or `GENERIC_SHARING` if kept) |
| XLSG002 | error | Configured type or member signature not accessible from generated code |
| XLSG003 | warning | `BlackList`/`DoNotGen`/`AdditionalProperties` names no member |
| XLSG004 | info | Member falls back to reflection, with the reason; or is dropped |
| XLSG005 | error | Conflicting GCOptimize flags (`Gen.cs:1530-1541`) |
| XLSG006 | warning | `CSharpCallLua` type is not a delegate or interface, or is open generic (`Gen.cs:1093,1722-1726`) |
| XLSG007 | error | `BindingApi.Version` differs from the generator's |
| XLSG008 | info | Type already bound by a referenced assembly |
| XLSG009 | warning | `[Hotfix]` on a type in another assembly (needs its own assembly's attribute) |
| XLSG010 | warning | GCOptimize struct not packable, so `PackAsTable` or the default path is used (`SizeOf` == -1, `Gen.cs:1757-1760`) |
| XLSG011 | info | A configuration list, which only the legacy generator reads |
| XLSG012 | error | The assembly references XLua but not XLua.Core; the message names the asmdef and the line to add, and nothing is generated ([Generated code without XLua.Core](#generated-code-without-xluacore-measured-not-taken)) |

The spike showed warnings and errors from a 4.3.1 generator in Unity's
console with file and line in all five editors.

## 6. Hotfix bridges without the `DelegateBridge` partial

How it works today:

- **Default and IntKey paths** (`Hotfix.cs:1196-1360`): a call to a
  `__Gen_Delegate_Imp*` instance method on `XLua.DelegateBridge`, found by
  name and signature (`Hotfix.cs:306-308,519-584`).
- **`Inline` path, generic methods and generic in/out** (`Hotfix.cs:775-777`):
  `injectGenericMethod` (`Hotfix.cs:1364-1550`). It emits calls to public
  `DelegateBridge` methods (`InvokeSessionStart`, `InParam<T>`, `Invoke`,
  `OutParam<T>`, `InvokeSessionEnd`; `DelegateBridge.cs:147-243`), so it
  needs no generated bridge.

When the injected assembly is not the XLua assembly, which is always the case
once XLua has its own asmdef:

- IntKey is turned off (`Hotfix.cs:523,1221`).
- The default path goes through a delegate type created in the injected
  assembly (`createDelegateFor`, `Hotfix.cs:434-516`). Lua then converts the
  Lua function to that delegate through a `__Gen_Delegate_Imp*` bridge.
- **`injectGenericMethod` returns `true` without injecting anything**
  (`Hotfix.cs:1366-1370`). Generic methods, and every type marked `Inline`,
  are silently not hotfixable.
- Lua's IntKey support loads only one map, `require 'hotfix_id_map'`
  (`auto_id_map` in `xlua.util`), and the bridge list is process-global by index
  (`DelegateBridge.cs:99-122`). Several injected assemblies cannot share it,
  although the injector writes one map per assembly (`Hotfix.cs:839-843`).
  Since 2026-10-18 the Editor also writes `hotfix_id_map` as the union of the
  per-assembly maps (`HotfixIdMap.Merge`). That is enough today, because only
  the xLua assembly's map holds ids; per-assembly IntKey still needs ids
  keyed by assembly.

### Option A: the `Inline` path everywhere

- **Pros:**
  - no generated bridges;
  - one mechanism.
- **Cons:**
  - It does not work across assemblies today. The early return has to go, and
    the reason for it is not written down **[inferred: calls to the public
    generic `InParam<T>` across assemblies should be legal IL]**.
  - Each argument goes through `PushByType<T>`, a dictionary lookup per push
    (`OT.cs:956-967`), and each result through `Get<T>` (`OT.cs:943-954`).
    That is slower than a typed bridge.
  - Each patched method gets a larger injected body (hotfix.md), and every
    method in the type pays it, even unpatched.
  - Still no IntKey across assemblies.

### Option B: a bridge class per assembly (recommended)

The generator runs in each assembly that has `[Hotfix]` types (type-level or
`[assembly: Hotfix(typeof(T))]`) and emits:

```csharp
internal static class HotfixBridges
{
    internal static global::XLua.DelegateBridge[] Slots = new global::XLua.DelegateBridge[0];  // IntKey, this assembly only
    internal static int __Gen_Delegate_Imp0(this global::XLua.DelegateBridge b, object self, int p0) { ... }
    ...
}
```

- **Signatures:** those of every injectable method in the assembly's
  `[Hotfix]` types, using the same selection the legacy generator applies:
  - the flags `IgnoreProperty`, `IgnoreNotPublic` and `IgnoreCompilerGenerated`;
  - no unsupported types, and `injectByGeneric` (`Gen.cs:1109-1135`).

  Grouping uses `SignatureKey.Of(…, Hotfix(flags))`.
- **Injector change:** in `Hotfix.Init` (`Hotfix.cs:290-308`), look for the
  bridges in the injected assembly's own `XLua.Generated.<Id>.HotfixBridges`
  instead of `XLua.DelegateBridge`.
  - The emitted IL stays the same except for the method token: a `call` to a
    static method whose first parameter is the bridge, where today it is an
    instance call on the bridge. The injected sequence (`ldsfld`, `brfalse`,
    `ldloc`, arguments, `call`) does not change.
  - Since the bridge and the injected type are in one assembly, the
    `isTheSameAssembly` restriction becomes "bridge class found in the
    injected assembly". Direct calls and IntKey then work in every assembly.
- **IntKey per assembly:**
  - The flag check becomes `ldsfld Slots; ldc.i4 id; …` on the assembly's
    own array: a managed array read. That also answers the backlog item
    "can the iOS IntKey flag (`HotfixFlags.cpp`, one P/Invoke per method
    entry) become a managed array read".
  - `AssemblyBindings.HotfixSlots` registers the array and the map's name.
  - `xlua.util` loads one map per registered assembly and calls
    `HotfixDelegateBridge.Set(assembly, id, f)`.
- **Inline:** keep it as the path for generic methods (it is the only one),
  and fix the cross-assembly early return in the same milestone. The `Inline`
  flag stays opt-in for projects that want no bridges at all.

Why B:

- It is today's fast path, generalised to every assembly.
- It brings IntKey to the UPM layout.
- Generator and injector agree on bridges through one key function.
- The injector needs a small change, not a rewrite.

Option A alone would leave hotfix slower than today for every project that
moves to the asmdef layout.

**Ordering constraint:** the injector runs at `IPostBuildPlayerScriptDLLs`
order 0, after compilation. The bridge class therefore exists before
injection. When the generator did not run (5.3), the injector fails with
"no HotfixBridges in <assembly>" instead of today's "try re-generate code"
(`Hotfix.cs:1208`).

## 7. Incrementality and performance

The pipeline, Roslyn 4.3.1 APIs only:

1. **Collect the configuration.**
   - `ForAttributeWithMetadataName` per configuration attribute, with
     predicates for `CompilationUnitSyntax` (assembly attributes) and type
     or member declarations. The spike showed this works for
     assembly-level attributes in 4.3.1.
   - Each match becomes a value-equatable `ConfigEntry`: metadata name,
     flags and location as path plus spans (spike: `ExportItem`,
     `ExportGenerator.cs:200-221`).
   - Roslyn re-runs this step only for syntax trees that changed.
2. **Merge.** Collect the entries into an `EquatableArray<ConfigEntry>`.
   Pipeline values compare by `Equals`, but `ImmutableArray<T>` compares by
   reference, so a plain `ImmutableArray` would defeat the cache after every
   edit **[inferred: well-known Roslyn pitfall]**.
3. **Build the models.**
   - Combine each requested type with `CompilationProvider` and build a
     `TypeModel`: strings, flags and nested equatable arrays only, never
     symbols.
   - This step re-runs on every compilation. Its cost is a symbol walk over
     the bound types' members.
   - Because its output is equatable, the next step runs only for types
     whose model changed.
   - Models for types from metadata references (UnityEngine, packages) do not
     change between edits. If measurement shows the walk matters, cache them
     in a static, size-bounded table keyed by (assembly MVID, metadata name).
     The 6000.x compiler server is long-lived (SOURCEGEN_SPIKE.md note 5).
4. **Emit.** `RegisterSourceOutput` per `TypeModel` (one file per type), plus
   one output for the assembly-level files. Their inputs are the collected
   models, reduced to what each file needs: names for `Bindings`, signature
   keys for `Bridges`.

Expected cost **[inferred, to be measured in M2]**:

- **Generator time:** the symbol walk and string building are linear in
  bound members. The legacy generator needs an Editor `LuaEnv` and template
  execution for the same work, and it does not report numbers beyond
  `Debug.Log("finished! use N ms")` (`Gen.cs:1885`).
- **Compile time dominates for large lists.**
  - The test configuration produces 19,354 lines for 48 types, about
    400 lines per type, and `LuaTestObjWrap.cs` alone has 2,735.
  - A "whole UnityEngine namespace" list (`ExampleConfig.cs`) is on the order
    of 1,000–2,000 types, so several hundred thousand lines.
  - csc compiles that on every recompile of the host assembly, while the
    legacy `Gen/` folder cost it only when it changed.
  - Mitigation: the bindings host asmdef (section 4), which edits to game
    code do not recompile.
- **Measurement in M2:** a benchmark project with 100, 500 and 2,000 bound
  types (UnityEngine), timing:
  - the generator, through `GeneratorDriver` timing in the host harness;
  - full csc, and csc after a one-line edit, both in the host assembly and
    in an unrelated assembly;
  - the Unity Editor's script compile, in 2021.3 and 6000.3.

  Results go next to the build docs (a new `build/SOURCEGEN.md` section).
- **Player size:** measure generated against reflection by type count with
  `Test/MobileSmoke/size_breakdown.py` (backlog: "no public data exists"),
  and source-generated against legacy for the same configuration. Dropping
  `preserve="all"` should make source-generated output smaller than legacy
  output **[inferred]**.

## 8. Testing plan

Native and Unity runs go one at a time through the locking wrapper
(AGENTS.md). Every run has a timeout, and a hang is reported, not retried.

1. **Managed suite through generated code** (`run_managed.py`):
   - **New `--source-gen` mode.**
     - Build the generator (netstandard2.0, Roslyn 4.3.1).
     - Compile `XLuaUnitTest` with the .NET SDK's csc and
       `-analyzer:XLua.SourceGenerator.dll`, against the Mono reference
       assemblies, and run it on Mono 6.14.1 as today. Mono's own `csc` is
       Roslyn 3.9 (`csc -version`: 3.9.0-6.21124.20), too old to load a 4.3.1
       generator. The test configuration moves to assembly attributes, with
       the old lists kept for the `--gen-code` comparison until legacy is
       removed.
     - The registration path is `EnsureRegistered` (no Unity), so the host
       suite also covers the fallback discovery.
   - **Expectation:** `EXPECTED_TESTS` (335) in both modes. Variants:
     `--source-gen --gen-code-minimize` if GCM stays, and `--source-gen` with
     `XLUA_SG_SKIP_REGISTER` defined, which forces the reflection fallback and
     checks that the "no registration" path still passes.
   - **Comparing old and new.** Comparing generated text is pointless, because
     the shapes differ by design. Instead:
     - **Binding surface diff:** a test helper loads every configured type in
       a `LuaEnv` and dumps its metatable tables (methods, getters, setters,
       class fields, operator metamethods, constructor overload count) as
       sorted JSON. It runs in the `--gen-code` and `--source-gen` builds, and
       the two files must be equal except for an allow-list of documented
       differences (fallback members).
     - **Bridge set diff:** the legacy generator gets a test-only dump of its
       delegate groups (`Gen.cs:1142-1145`). The source generator's manifest
       has the same data. Diff the `SignatureKey` strings.
   - **Generator unit tests**, a dotnet test project with `CSharpGeneratorDriver`
     4.3.1:
     - `SignatureKey` tables;
     - one test per diagnostic;
     - incrementality, using tracked steps: a whitespace edit and an edit to
       an unrelated file must emit no output, and an edit to one bound type
       re-emits only that file;
     - determinism: run twice and compare the bytes.
2. **Unity editor suite** (`Test/UnityMatrix/prepare.py` + `editor_tests.py`),
   on 2021.3.45f2 and 6000.3.x, plus one 6000.6 run (Roslyn 4.10 host):
   - **New layout `--source-gen`:** XLua and XLua.Editor asmdefs as a `file:`
     package (read-only, like a registry install), the generator DLL with its
     generated `.meta` (as the spike did), and the test configuration in a
     game asmdef and in Assembly-CSharp.
   - **Checks:**
     - the `XluaGeneratedCodeTests` cases;
     - generated delegates come from the user assembly
       (`f.Method.DeclaringType.Assembly`, as `proto/ProtoTests.cs` checks);
     - edit-mode registration through `InitializeOnLoadMethod`;
     - Enter Play Mode without domain reload, twice;
     - the incremental edit check from the spike;
     - the CS9057 negative control: a 4.10-built generator must trigger the
       Editor error and the build check from 5.3.
   - `editor_tests.py verify --mode` gains `source-gen`.
3. **IL2CPP High-stripping build:**
   - **Static checks**, Android ARM64 export, as in SOURCEGEN_SPIKE.md:
     - `RuntimeInitializeOnLoads.json` lists every `Bindings.Register`;
     - the stripped DLLs keep the wrappers and drop unbound members;
     - closed static delegates appear in the C++ (`inspect_il2cpp.py`,
       extended).
   - **Run:** the `Test/MobileSmoke` smoke scene on an emulator or device.
     IL2CPP has never been run for the generator; the phone needs the
     owner's go-ahead. It covers wrappers, GCOptimize, delegates, interfaces,
     the per-member fallback with link.xml, and hotfix IntKey.
   - **Size:** `size_report.py` variants for reflection, legacy and
     source-generated with the same configuration.
4. **obfuz build:**
   - obfuz 4.0.0 at its default order (10000), obfuscating Assembly-CSharp
     and the game asmdef, with the generated rule XML from the obfuz adapter
     (presets.md).
   - It runs the smoke scene and checks:
     - `CS.Game.T` resolution;
     - member calls;
     - delegates;
     - the hotfix IntKey patch;
     - a reflection-fallback member, which must keep working only because the
       adapter excluded it.
   - A cheap host check: an Obfuscar rename pass over the `--source-gen` test
     assembly, then the managed suite (backlog, Obfuscation table).

What these do not cover: iOS device runs, WebGL, and HybridCLR hot-update
assemblies. They are listed for the release checklist, not for this design.

## 9. Work breakdown

Each milestone ends with a passing run of the suites it names. Sizes are
rough, in working weeks for one agent with owner reviews.

| # | Milestone | Testable by | Size |
|---|---|---|---|
| M0 | **Binding API and registry in the runtime; legacy templates moved onto it.** Add `XLua.Binding` (section 3) and the registry. Change the templates to call it instead of the five partials (it is the same output with new calls). Remove the partial hooks from the runtime. The legacy generator stays the only generator. | `run_managed.py --gen-code --gen-code-minimize` green; editor suite in Assets layout green; the generated output compiles in an assembly separate from XLua (asmdef layout test) | 1.5 |
| M1 | **XLua asmdefs, generator skeleton, attributes.** `XLua`/`XLua.Editor` asmdefs; Hotfix.cs assumptions about Assembly-CSharp fixed (`Hotfix.cs:111-114,1633,1706`; UPM report finding 3); attribute constructors and `AttributeUsage`; the generator project from the spike with registration, name table and diagnostics plumbing; the `run_managed.py --source-gen` harness; IL2CPP check that closed static delegates work. | Generator unit tests; host suite runs with registration and no wrappers (all reflection) | 1.5 |
| M2 | **Enum and class wrappers.** Member model, overloads, operators, indexers, events, constructors, extension methods; manifest; binding surface diff; performance benchmark. | `--source-gen` 335/335; surface diff equal; benchmark numbers recorded | 3 |
| M3 | **Delegate and interface bridges.** `SignatureKey` with tests; registry-based `getDelegate`. | Bridge set diff equal; CSharpCallLua tests | 1 |
| M4 | **GCOptimize.** Struct slots, push/get/update, pack/unpack with float grouping, table-optimised types, array accessors. | GC tests and `AllocProbe` allocation checks equal to legacy | 1 |
| M5 | **Fallbacks and preservation.** Per-member fallback with its log, `DoNotGen`, `ReflectionUse`, link.xml through `IUnityLinkerProcessor`, the three no-registration layers. | Forced-fallback host run; editor CS9057 control; first IL2CPP High-stripping static check | 1 |
| M6 | **Unity matrix.** `prepare.py --source-gen` package layout; editor suite on 2021.3, 6000.3 and 6000.6; IL2CPP Android run; size report. | Section 8, items 2 and 3 | 1.5 |
| M7 | **Hotfix.** Per-assembly `HotfixBridges`, injector lookup, per-assembly IntKey with Lua map loading, `Inline` cross-assembly fix. | Hotfix fixture (`Test/Native/HotfixFixture.cs`) through the source generator in two assemblies; editor hotfix run (`prepare.py --hotfix`) | 2 |
| M8 | **Obfuscation.** Name table in `FindType`, `getPathOfType`, metatable keys and `xlua.typename`; `[Obfuscation(Exclude)]` emission; obfuz adapter and rule XML. | Section 8, item 4 | 1 |
| M9 | **Migration tool** with its report; examples and tutorials converted. | Running it on `Assets/XLua/Examples`, the test configuration and a sample `ExampleConfig` gives equal binding surfaces against legacy | 1.5 |
| M10 | **LuaCATS from the manifest**; `SetupValidator` reduced; GCM in the source generator if kept. | `Test/Native/luacats_check.py` expected files unchanged; GCM host run | 1 (+1 GCM) |
| M11 | **Remove legacy:** the "Generate Code" menu, templates, `Gen/`, `XLuaGenerate` wrapper mode, `--gen-code` modes; docs (Configure, Custom Generate, Hotfix, Faq). | All suites in `--source-gen` only | 1 |

Total: about 16–17 weeks, plus 1 if GCM is kept. M0 can start now: it needs
no Roslyn and makes the asmdef layout possible with the legacy generator.
M7 can run in parallel with M3 to M6 once M2 is done.

### M0 as implemented (2026-10-18)

The API is in `Assets/XLua/Src/Binding.cs`; the legacy templates call it,
and generated code no longer declares any partial class of xLua. User-visible
changes are in [MIGRATING.md](../MIGRATING.md#generated-code-xluagenerate-code).

Differences from section 3:

- **`StructGetter<T>`** `(ObjectTranslator, IntPtr L, int index, out T)`
  replaces `ObjectTranslator.GetFunc<T>` in `Struct<T>` and `Caster<T>`: the
  generated getters need the translator for the caster fallback and table
  unpacking. `Struct<T>` wraps them per `LuaEnv` with
  `RegisterPushAndGetAndUpdate`, `Caster<T>` with `RegisterCaster`.
- **`ArrayAccessor<T>(ArrayElementPusher<T> get, ArrayElementSetter<T> set)`**,
  `get` may be null (table-optimised types only have a setter, as before). The
  runtime dispatches through a dictionary keyed by array type, installed as
  `InternalGlobals.genTryArrayGetPtr`/`SetPtr`.
- **Added `AssemblyBindings.DelegateBridges(Type bridgeClass)`:** the lookup by
  signature for delegate types without a `Delegate` entry, which legacy did by
  scanning `DelegateBridge`'s `__Gen_Delegate_Imp*` methods (`OT.cs:466-469`).
  It keeps two legacy behaviours: an unlisted delegate type with the signature
  of a generated bridge works, and so do the delegates the hotfix injector
  creates (`createDelegateFor`). The scan runs once, on the first miss, and
  tries the generated bridges before `DelegateBridge.Action`.
- **Added `BindingApi.UncheckedPushes`** (const 10), so generated code needs no
  `Utils` internals.
- **`GeneratedBindingsAttribute(Type bindings, int apiVersion)`** has no type
  list yet; M2 adds it for the cross-assembly skip (5.1).
- **Not added:** `HotfixSlots` (M7).
- **Legacy units:** `XLuaGenAutoRegister.cs` (wrappers, interface bridges,
  extension methods, name table), `DelegatesGensBridge.cs` (delegate and hotfix
  bridges) and `WrapPusher.cs` (structs, casters, array accessors) each register
  their own `AssemblyBindings`, named `<assembly>/<class>`, so a file generated
  alone (`Generator.Gen`) still registers.
- **Name table:** `Type` and `Name` record `Type.ToString()` literals, the
  string `getPathOfType` computes today. Nothing reads the table yet (M8).
- **Struct slots:** `AllocateSlot` returns the same slot for the same type;
  the per-translator arrays start at -1 and grow on first use.
- **Registration timing:** `Register` takes a lock and the lookups do not. A
  late registration does not reach `LuaEnv`s that already exist (as with
  `LuaEnv.AddIniter`), and duplicates keep the first registration without a
  log. Both are left for M1/M5.
- **Hotfix (section 6) in M0:** the injector finds the static bridges in
  `XLua.CSObjectWrap.DelegatesGensBridge` of the xLua or the injected assembly
  and emits a `call` to them; the injected field keeps the `DelegateBridge`.
  IntKey and the same-assembly rule are unchanged until M7.

Tested on macOS arm64, Lua 5.5.1, Mono 6.14.1: `run_managed.py --gen-code
--gen-code-minimize --gen-assembly` (reflection, generated, minimized
generated, and both generated modes compiled into an assembly separate from
XLua), `hotfix_check.py`, `luacats_check.py`. The separate generated
assemblies also compile with `UNITY_EDITOR` and `UNITY_5_3_OR_NEWER` against
Unity 2022.3's `UnityEngine.CoreModule` and `UnityEditor.CoreModule` (mcs, no
Unity run). Not run at the time: the Unity editor suite, IL2CPP and devices.
The editor suite ran on 2026-10-18 (`Test/README.md`, "Unity editor tests"),
and so did an IL2CPP player with generated delegate bridges and a generated
wrapper on an Android 16 emulator (`Test/MobileSmoke/README.md`, "Recorded
validation").

Speed, `Test/Native/benchmark.py --modes gencode,gencode-min --no-lua` against
`72715b4` with the same plugin, on a machine shared with other test runs:
every case in `gencode` stayed within ±5% (5 runs; the `cs_lua` cases again
with 15 runs: +0.4% to +3.8%). `gencode-min` showed single outliers in both
directions in cases this change does not touch (`luafunction_func` −7% in
one run, +15% in the other), so its numbers are noise at this precision.
Plugin size is unchanged (no native change). Repeat on a quiet machine
before drawing conclusions below 5%.

### M1 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#xlua-in-its-own-assemblies-asmdef-opt-in)
(opt-in asmdefs, configuration attributes, source generator preview). What landed:

| Part | Where |
|---|---|
| `XLua` and `XLua.Editor` asmdefs, opt-in | `Assets/XLua/Asmdef~/Src/XLua.asmdef` (every platform, unsafe code) and `Assets/XLua/Asmdef~/Src/Editor/XLua.Editor.asmdef` (Editor, references `XLua`), both auto-referenced; a project copies them into `Assets/XLua/Src` and `Assets/XLua/Src/Editor`. Unity does not import the `~` folder, so the default layout is unchanged |
| Hotfix without Assembly-CSharp | `Hotfix.cs`: `HotfixConfig.GetProjectPath`/`IsProjectAssembly`, the post-build callback, the xLua assembly passed to the injector, editor `[Hotfix]` lists |
| Attribute constructors and `AttributeUsage` | `GenAttributes.cs` |
| Generator | `General/SourceGenerator/XLua.SourceGenerator` (netstandard2.0, Microsoft.CodeAnalysis.CSharp 4.3.1, C# 9) |
| Generator unit tests | `General/SourceGenerator/XLua.SourceGenerator.Tests` (16 tests, `CSharpGeneratorDriver` 4.3.1) |
| Build, test, install | `Tools/build_source_generator.py` (NuGet in locked mode, `--test`, `--install <folder>`) |
| Host suite with the generator | `run_managed.py --source-gen`, `Test/Native/SourceGenLauncher.cs`, `Test/Native/SourceGenConfig.cs` |
| asmdef layout compile check | `Test/Native/asmdef_compile_check.py`; `Test/UnityMatrix/prepare.py --asmdef` for a later editor run |

Differences from sections 3 to 5 and the decisions:

- **`AttributeUsage(AttributeTargets.All, AllowMultiple = true)`.** Section 4
  asks for "Assembly plus the targets it has today". Without `AttributeUsage`
  an attribute allows every target, so `All` changes no target; narrowing
  them could break projects that put an attribute somewhere no generator
  reads. The constructors are as in section 4, plus a by-name `BlackList`
  (`string typeName, string member, params string[] parameterTypeNames`,
  `Type.ToString()` names as in the legacy lists) and `Hotfix(Type, flags)`.
  The legacy generator, CodeEmit and the injector do not read the new forms.
  The injector reads `[Hotfix]` flags from the last constructor argument.
- **`[Conditional("UNITY_EDITOR")]` (decision 5) is not applied.** See the
  question below.
- **The asmdefs are opt-in, not in `Assets/XLua/Src`.** In the asmdef layout,
  hotfix of types outside xLua's assembly loses IntKey (field mode) and
  `Inline`/generic-method injection until M7, so the `legacy` default stays the
  single-assembly layout until the owner decides. They live in
  `Assets/XLua/Asmdef~/Src`, mirroring where they go, with stable GUIDs, so the
  M6 package layout (and `prepare.py --asmdef`, `asmdef_compile_check.py`)
  take them from there. `asmdef_compile_check.py` also fails if an asmdef
  appears under `Assets/XLua/Src`.
- **Generator source:** `General/SourceGenerator`, outside `Assets`. The DLL is
  a build output and is not committed (no binaries in the repository).
  `build_source_generator.py --install <folder>` writes it with a `.meta`
  that has the `RoslynAnalyzer` label and no platform, with fixed GUIDs; the
  intended folder is `Assets/XLua/Src/Analyzers` with the opt-in asmdefs
  (inside the `XLua` asmdef folder, so Unity scopes it to XLua and its
  referencers). Without an asmdef above it, Unity would run it in every
  assembly (SOURCEGEN_SPIKE.md, "Shipping"), so the generator needs the asmdef
  layout. Packaging it is M6.
- **Pipeline:** `ForAttributeWithMetadataName` per attribute kind, a
  value-equatable `ConfigEntry` per attribute (strings, flags, location; all
  symbol work happens in the transform), collected into an `EquatableArray`,
  combined with an `AssemblyInfo` from `CompilationProvider`, one
  `RegisterSourceOutput`. There is no `TypeModel` yet (M2).
- **Output:** one file, `XLua.Bindings.g.cs`, only when at least one type is
  configured: `[assembly: GeneratedBindings(typeof(Bindings), BindingApi.Version)]`
  and `internal static class XLua.Generated.<Id>.Bindings` whose `Register`
  creates an `AssemblyBindings` named after the assembly, adds one `Name`
  per configured type and calls `BindingApi.Register`.
  - `<Id>`: the assembly name with every character other than an ASCII letter,
    digit or `_` replaced by `_` (and `_` in front of a leading digit), then
    `_` and the 8 hex digits of the 32-bit FNV-1a hash of the UTF-8 name.
  - `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, `[Preserve]` and
    the `#if UNITY_EDITOR` `[InitializeOnLoadMethod]` are emitted when the
    compilation can resolve those attributes, rather than under
    `UNITY_5_3_OR_NEWER`. Outside Unity, `EnsureRegistered` finds the unit
    through the assembly attribute.
  - Names are `Type.ToString()` strings computed from symbols (nested types
    with `+`, arity, the type arguments of the whole containing chain, open
    types with their parameter names); the unit test loads the output and
    compares every entry with `Type.ToString()` at run time.
  - Types listed: every valid `LuaCallCSharp`, `CSharpCallLua`, `GCOptimize`,
    `ReflectionUse` and `Hotfix` type, once, sorted by name.
- **Runtime:** a unit that binds no code (only names) no longer sets
  `DelegateBridge.Gen_Flag` and no longer replaces the reflection scan for
  extension methods (`AssemblyBindings.BindsCode`). Otherwise an M1
  registration would turn CodeEmit off and hide extension methods.
- **Diagnostics reported:** XLSG001 (also for `Hotfix` on a constructed
  generic type; `Hotfix` accepts generic definitions, as the legacy config
  does), XLSG002, XLSG003, XLSG005, XLSG006, XLSG007, XLSG009, and a new
  **XLSG011** (info) for static configuration lists, which only the legacy
  generator evaluates. XLSG003 is info, not a warning, for by-name
  `BlackList` entries (they exist for members missing on some targets), and
  is not reported for a `[DoNotGen]` name that matches an extension method of
  the type declared in the same compilation (the legacy generator binds
  those lazily since `b5c007f`). Not yet: XLSG004, 008, 010 (they need the
  member model).
- **Member matching for XLSG003** follows the legacy rules at compile time:
  fields, properties and events by name, methods and `.ctor` by exact
  parameter types. The runtime `BlackListRules` (`GenAttributes.cs`, from
  `b5c007f`) is the reference for M2's member model.
- **The generator also runs in the XLua assembly itself** when Unity scopes it
  there. It emits nothing: XLua configures no types in the attribute form,
  and `SysGenConfig`'s lists only give XLSG011 infos. Moving those defaults to
  `[assembly: GCOptimize]` is M4.
- **`GeneratedBindingsAttribute`** keeps its two arguments (the type list for
  the cross-assembly skip is M2).
- **Host harness:** Mono's `mcs` cannot run a generator and Mono's `csc` is
  Roslyn 3.9, so `--source-gen` compiles the test assembly with the .NET
  SDK's csc against `lib/mono/4.5` (what `mcs` compiles against; the
  4.7.1-api reference set has no `Span<T>`), at `-langversion:12` for the
  `ref readonly` fixtures, which are rewritten for Roslyn as for
  `coverage_cs_dotnet.py` (`Test/Native/roslyn_fixtures.py`). The generator
  unit tests check the output at C# 9. The run fails on a generator warning
  or error, and on analyzer load warnings (CS8032, CS8784, CS8785, CS9057).

Tested on macOS arm64, Lua 5.5.1, Mono 6.14.1, .NET SDK 10.0.401, with the
asmdefs opt-in and `main` merged at `28969f2` (396 Lua tests; plugin rebuilt
from the merged tree):

- `build_source_generator.py --test`: 16 of 16 (locked restore);
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen`:
  all five modes 396 of 396; the source generator run registered 112 names
  and no wrappers;
- `hotfix_check.py`, `luacats_check.py --download-luals`, `dependencies.py
  --check`; `coverage_cs_dotnet.py` at the `227ed4c` merge;
- `editor_compile_check.py` (default layout) against 2021.3.45f2 and
  6000.3.26f1: 78 ok, 2 skipped;
- `asmdef_compile_check.py` (opt-in layout) against 2021.3.45f2, 2022.3.62f3,
  6000.3.26f1 and 6000.6.6f1, with and without `HOTFIX_ENABLE`.

Not run at the time: any Unity editor or player. Items 1 to 3 ran on
2026-10-18 (Unity 2021.3.45f2, 6000.3.26f1 and 6000.6.6f1, macOS ARM64,
with main at `650e32d`; results in `Test/README.md`, "asmdef layout, source
generator and hotfix players"):

1. **[checked] The editor suite in the asmdef layout:** `prepare.py
   --editor-tools --asmdef` (and with `--hotfix`), then `editor_tests.py`,
   passed in both modes on the three editors, after `prepare.py` moved
   xLua to `Assets/XLua/Src` (its asmdef had taken `Assets/XLua/Gen`). The
   suite does not inject; `hotfix_intkey.py --asmdef` does (item 3).
2. **[checked] The generator inside Unity:** `prepare.py --source-gen`
   installs it in `Assets/XLua/Src/Analyzers` (inside the XLua asmdef, as
   intended above; the M1 text named `Assets/XLua/Analyzers`). No
   CS8032/CS9057; a `GeneratedBindings` unit and `XLua.Generated.*.Bindings`
   in Assembly-CSharp and in the asmdef `SgGame`; `Register` run by
   `InitializeOnLoadMethod` in edit mode (without the first-`LuaEnv` scan)
   and by `RuntimeInitializeOnLoadMethod` at each play mode start with
   domain reload off (shown by removing the registrations first; the second
   call for registered units is a quiet no-op) and after a domain reload;
   XLSG diagnostics in the Editor log with file and line (XLSG004 and
   XLSG008 for types from metadata have none).
3. **[checked] Hotfix in the asmdef layout:** `hotfix_player.py --asmdef`
   built a macOS ARM64 Mono player on 2021.3.45f2 and 6000.3.26f1: the
   post-build callback found `XLua.dll`, injected Assembly-CSharp and Game
   without the `Types.Single` exception, warned about the IntKey fallback
   for both, and the player patched and restored both types from Lua.
   `hotfix_intkey.py --asmdef` showed the same in the Editor. Both found
   that the injector had no bridges for a game asmdef (they are in the
   generated code in Assembly-CSharp); fixed in `Hotfix.cs`, covered by
   `hotfix_check.py`.
4. **IL2CPP and closed static delegates** (section 3, "rejected alternative";
   M0 already emits them through `b.Delegate(typeof(D), d => new D(d.__Gen_Delegate_Imp<k>))`
   and the hotfix bridges are called the same way). Steps, on an Android
   ARM64 IL2CPP build with High stripping (6000.3.21f1 has the module):
   1. Make a project from `Test/MobileSmoke/prepare.py --android-plugin ...`
      and add a script with `[CSharpCallLua] public delegate int Twice(int x);`
      that runs `env.DoString("return function(x) return x * 2 end")[0]`,
      `Cast<Twice>()` and checks `f(21) == 42`,
      `f.Method.Name.StartsWith("__Gen_Delegate_Imp")`,
      `f.Target is DelegateBridge` and the same for `System.Action`; log a
      marker such as `XLUA_CLOSED_DELEGATE_PASS`.
   2. Run Generate Code (`CSObjectWrapEditor.Generator.GenAll()`) in batch
      mode before `SmokeBuild.BuildAndroid` with
      `XLUA_SMOKE_STRIPPING=High`.
   3. Static check of the exported project: the C++ for
      `DelegatesGensBridge` contains the `__Gen_Delegate_Imp` bodies and the
      lambda in `XLuaGenAutoRegister`'s registration creates the delegate
      with the bridge as target (`il2cpp_codegen_object_new` plus the
      delegate constructor with a non-null object, as for an instance
      method); `ManagedStripped` keeps the bridge methods.
   4. Run the APK on the emulator (`Test/MobileSmoke/README.md`) and look for
      the marker in `adb logcat`. Not while a phone benchmark runs: Unity
      starts its own adb server.
   5. Repeat with the source generator once M3 emits bridges.

Remaining in M1 after this run: the owner's answer to the question below.
Item 4 is done: the M0 bridges ran in IL2CPP players on 2026-10-18, and
step 5, with the source generator's bridges, passed the same day ([IL2CPP players](#il2cpp-players-with-the-source-generator-2026-10-18)).

**Questions for the owner (M1):**

1. **`[Conditional("UNITY_EDITOR")]` on the configuration attributes
   (decision 5).** `Conditional` applies to an attribute class, so it would
   remove every use of it from player builds, not only the assembly-level
   form. The cost in the `legacy` preset:
   - `HotfixAttribute`: the injector reads `[Hotfix]` from the *player*
     assemblies after compilation (`IPostBuildPlayerScriptDLLs`), so hotfix
     would inject nothing in players. It cannot be conditional.
   - `XLuaGenerate` and `XLuaHotfixInject` read the attributes from assemblies
     built without `UNITY_EDITOR` (the host tests and command-line users);
     adding `[Conditional("XLUA_GENERAL")]` as a second condition keeps them.
   - CodeEmit in a Mono player scans `CSharpCallLua` at run time
     (`OT.cs` `initCSharpCallLua`), which would find nothing.

   Alternative: keep the attributes in metadata (a few bytes per configured
   type), or add separate assembly-only attribute classes that are
   conditional while the old ones stay as they are. Neither was measured.

The asmdefs, which were the second question, stay opt-in until M7 (above);
whether the default layout changes after M7 is the owner's decision.

### M2 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#source-generator-preview)
and, for the runtime, under [Generated code](../MIGRATING.md#generated-code-xluagenerate-code).
What landed:

| Part | Where |
|---|---|
| Member model: the legacy generator's selection and order rules on Roslyn symbols | `General/SourceGenerator/XLua.SourceGenerator/Members.cs` |
| Class and enum wrappers, extension method map, manifest | `Wrappers.cs`, `Emitter.cs` |
| Cross-assembly skip: `GeneratedBindings(Type, int, Type[])`, XLSG008 | `Assets/XLua/Src/Binding.cs`, `Wrappers.cs` |
| Legacy surface dump (test only, `XLUA_GEN_MANIFEST`) | `Assets/XLua/Src/Editor/Generator.cs` `BindingSurface` |
| Binding surface diff | `Test/Native/surface_diff.py`, run by `run_managed.py --gen-code --source-gen` |
| Generator unit tests (19, three new for M2) | `General/SourceGenerator/XLua.SourceGenerator.Tests` |
| Benchmark modes `sourcegen` and `gencode-csc` | `Test/Native/benchmark.py` |
| Model cache for types from metadata (section 7) | `Wrappers.cs` `ModelOf` |
| Generator timing probe | `XLua.SourceGenerator.Tests <repo> --timing N...` |
| Measurements | [build/SOURCEGEN.md](../../build/SOURCEGEN.md) |

How the pieces follow sections 2 to 7 and the decisions:

- **Member model.** `MemberContext.Build` is `getClassInfo` (`Gen.cs:309-549`)
  rule by rule: public declared members only; constructors with one entry per
  trailing default value left out (a struct's implicit constructor is not
  listed, as reflection does not list it); methods filtered by `DoNotGen`,
  `BlackList`, `[Obsolete]` and the generic-method rule
  (`isSupportedGenericMethod`), grouped with the `_xlua_st_` split, ordered by
  `OverloadCosting` with a stable sort (ties keep declaration order); extension
  methods of `[LuaCallCSharp]` static classes merged in
  (`GetExtensionMethods`/`isSupportedExtensionMethod`); string-keyed
  `get_Item`/`set_Item` as methods; indexers, operators (not filtered, as in
  legacy) and events (with legacy's `CanAdd` from the remove accessor); lazy
  members for `DoNotGen` names including extension methods of that name
  (`b5c007f`). `BlackList` entries match with the `BlackListRules` rules
  (`GenAttributes.cs`): fields, properties and events by name, methods by exact
  `Type.ToString()` parameter names, `.ctor` included.
- **Emission.** The C# is that of `LuaClassWrap.tpl.txt` and
  `LuaEnumWrap.tpl.txt` with `TemplateCommon`'s helpers ported to C# (checkers,
  casters, pushers, `CallNeedTranslator`, `ref`/`out`/`in`/`ref readonly`
  handling, `EnsureStack` above 10 results), in `internal static class
  W_<name>_<fnv32>` per type in `XLua.Generated.<Id>`, one file each. Extension
  methods are called statically (`Ext.M(self, ...)`), so no `using` lines are
  needed. Instance self goes through `BindingApi.FastGetCSObj`, so the
  `XLUA_CHECK_OBJECTS` check of the bridge hardening applies.
- **Not in M2, as planned:** struct and enum values go through
  `ObjectTranslator.Push`/`Get<T>`/`Update` (boxing) until the GCOptimize
  pushers of M4; delegate and interface bridges are M3.
- **`Gen_Flag`.** It is now set only by units that bind delegates
  (`AssemblyBindings.BindsDelegates`); see the runtime change below.
- **Incrementality.** One `Select` over `CompilationProvider` and the
  configuration builds a `WrapperSet` whose `TypeModel`s carry their rendered
  text, so values compare as strings; a `SelectMany` feeds one
  `RegisterSourceOutput` per type. The model step re-runs on every
  compilation (section 7, step 3); an edit to one bound type re-writes only its
  wrapper and the registration (unit test `WrapperOfAnEditedTypeIsWrittenAgain`).
  Unity's compiler server starts a new driver per compilation, so the
  pipeline cache does not carry over there; the static cache of section 7
  does: models of types from metadata are kept, keyed by the module version
  id of their assembly, the type, the configuration and compiler facts the
  model reads, and the extension methods that apply to the type. With 2,000
  types it cuts the model step from about 2.5 s to 0.4 s.
- **Manifest.** `XLua.Manifest.g.cs`, `#if UNITY_EDITOR || XLUA_GENERAL`, a
  JSON constant: per type the constructors and overloads in call order,
  getters, setters, operators, indexers, events, lazy members, enum values, and
  the extension method map. The legacy dump writes the same format.
- **Diagnostics added:** XLSG004 (info: init-only setters, array types not yet
  wrapped) and XLSG008 (info: wrapped by a referenced assembly).

Differences from the legacy generator, all outside the suite's configuration
(the surface diff has no allow-list entries):

1. **Obsolete members** are always left out, as the legacy tool does in
   practice (Unity, and `XLuaGenerate`, which `Tools/build_tools.py` builds
   with `XLUA_ALL_OBSOLETE`); `XLUA_JUST_EXCLUDE_ERROR` keeps the non-error
   ones. A compilation's own `XLUA_GENERAL` does not change it.
2. **`Func<MemberInfo, bool>` BlackList filters** cannot run in the compiler
   (section 4); the suite lists what `BlackListConfig.Filter` matches as
   attributes in `SourceGenConfig.cs`. The reflection wrappers still apply the
   filter at run time.
3. **Init-only setters** are left out (XLSG004); legacy wrote code that does
   not compile.
4. **Array types** in `LuaCallCSharp` are not wrapped (XLSG004): legacy wraps
   the members reflection reports for arrays (`Get`, `Set`, `Address`, the
   length constructor), which Roslyn does not model. Lua reaches them through
   reflection. *Resolved 2026-10-18:* the legacy wrapper did not compile
   (its constructor wrote `new int[]` with an argument list) and was never used, since every array takes the common
   array metatable (`ObjectTranslator.getTypeId`), so Generate Code now leaves
   array types out with a warning as well, and the surfaces are equal.
5. **Unspeakable names** (`CanBeReferencedByName` false, such as a record's
   `<Clone>$`) are left out; legacy would write code that does not compile.
6. **Compiler-dependent `ref readonly` rules** (`Gen.cs:611-694`) are decided
   from the running compiler (`typeof(Compilation)` 4.10 or later), not from
   `UNITY_6000_x` defines: older Roslyn drops members with a
   `modreq(InAttribute)` `ref readonly` parameter and calls `ref readonly`
   indexers through their accessors. Only the 4.10+ branch ran on the host.
7. **The order of extension classes** (which breaks overload ties between
   extension methods of equal cost from different classes) follows the
   attributes' file and position; legacy follows the assembly's type order.
   They agree for one class per file and for the suite.

Runtime changes (`Binding.cs`):

- `DelegateBridge.Gen_Flag` is set only by units with `Delegate` or
  `DelegateBridges` entries. Otherwise the M2 wrappers would have turned
  CodeEmit off and left `CSharpCallLua` delegates without bridges in the Editor
  and on the host. Legacy output always contains its `DelegatesGensBridge`
  unit, so legacy projects keep the flag.
- `BindingApi.EnumCacheRef` refers to a registry table of its own instead of
  the enum's class table (bridge hardening, finding D): a script could plant a
  value with `rawset(CS.E, 1, x)` that C# then pushed for that enum value.
  `CaseEnumCacheOutOfReach` failed in the generated-code mode before the
  change. The legacy template is unchanged. M4's source-generated enum pushers
  use the same API and inherit the fix.
- `GeneratedBindingsAttribute` gains `(Type, int, Type[])` and `Types`.

Tested on macOS arm64, Lua 5.5.1, Mono 6.14.1, .NET SDK 10.0.401, with `main`
merged at `c067a2b` (409 Lua tests):

- `build_source_generator.py --test`: 19 of 19;
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen`:
  every mode 409 of 409; the source generator run wraps 71 types (the same
  71 as legacy) and registers 113 names; surface diff: 71 types, 655 members
  and 13 extension methods equal, no allowed differences;
- the same with all seven bridge hardening defines (`--define XLUA_CHECK_OBJECTS`
  ... `XLUA_NO_LOAD_ASSEMBLY`), `--gen-code --source-gen`, at the earlier
  merge of `ab4f498` (404 tests then);
- `hotfix_check.py`, `luacats_check.py --download-luals`;
- `editor_compile_check.py`: 7 editors x 47 configurations, 322 ok, 7 skipped;
- `asmdef_compile_check.py` against 2021.3.45f2 and, with `HOTFIX_ENABLE`,
  6000.6.6f1;
- `coverage_cs_dotnet.py`: all three .NET modes 409 of 409; runtime branches
  79.2% combined, lines 87.9%.

**Benchmark** ([build/SOURCEGEN.md](../../build/SOURCEGEN.md), `gencode-csc`
against `sourcegen`, both compiled by the same csc, three interleaved rounds
on a shared machine):

- member calls, fields, properties and strings through the wrappers: within
  ±5% of the legacy wrappers (static call 50.3 against 50.0 ns, instance call
  83.5 against 82.1 ns);
- `[GCOptimize]` structs: 53% to 83% slower and 32 managed bytes per call,
  boxed until the M4 pushers;
- C# calling Lua goes through CodeEmit's bridges until M3 (`delegate_int`
  +12%);
- `env.create_dispose` 3.7 times slower: with `Gen_Flag` off each `LuaEnv`
  runs CodeEmit's `CSharpCallLua` scan of all loaded types. M3 brings it back.

**Generator time** (same file; BCL types as a stand-in for a UnityEngine
list): 2,000 types give 378,761 lines that compile without errors; the
generator takes 2.5 s with an empty cache and 1.7 to 1.9 s with it filled;
compiling the output takes about 5 s, against 56 ms without it.

Remaining in M2:

1. **Unity runs** (items 1 and 2 checked on 2026-10-18 with M3 merged, see
   "M1 as implemented" items 2 and 3 and `Test/README.md`; item 3 in
   [build/SOURCEGEN.md](../../build/SOURCEGEN.md)):
   1. **[checked]** `build_source_generator.py --install <project>/Assets/XLua/Src/Analyzers`
      in a project prepared with `Test/UnityMatrix/prepare.py --asmdef
      --editor-tools`, with the suite's configuration in Assembly-CSharp:
      compile in 2021.3.45f2 and 6000.3.26f1 (Roslyn 4.3.1, so the
      `OldCompiler` branch of the `ref readonly` rules) and 6000.6.6f1;
      no CS8032/CS9057; `W_*` wrappers in Assembly-CSharp; XLSG infos in the
      console. The 4.3.1 branch compiled the `XluaRefReadonlyLib` wrappers
      without CS0570 on 2021.3 and 6000.3 and left out the virtual members;
      6000.6 kept them. On 6000.6 the registration warned CS9198 for an
      extension method with a `ref readonly` parameter; it now disables the
      warning as the legacy template does.
   2. **[checked]** `editor_tests.py` with the generated wrappers registered by
      `InitializeOnLoadMethod`, and in play mode (twice, domain reload off):
      the whole suite in mode `sourcegen`, with its lists also given as
      attributes (42/42, and 46/46 on 6000.6).
   3. **[measured]** The editor script compile time with 100, 500 and 2,000 UnityEngine types
      (section 7), and after a one-line edit in game code with the bindings
      in an `XLuaBindings` asmdef.
2. **[checked 2026-10-18] IL2CPP High stripping** static check of the
   wrappers (M5 lists it with link.xml); see [IL2CPP players](#il2cpp-players-with-the-source-generator-2026-10-18).
3. Array type wrappers (difference 4): neither generator writes them since
   2026-10-18; arrays use the common array metatable.
4. The 2.5 ms per type of compile time (above) if the `XLuaBindings` asmdef
   is not enough: a smaller emission mode (decision 1 leaves room for one).

**Questions for the owner (M2):**

1. **`Gen_Flag` before M3** (resolved by M3, whose bridges set it). Source-generated wrappers without bridges leave
   `Gen_Flag` off, so CodeEmit keeps making delegate bridges in the Editor and
   on Mono hosts, and each `LuaEnv` pays its `CSharpCallLua` scan (+9 ms per
   `LuaEnv` in the benchmark). In IL2CPP players delegates fall back to the
   generic bridges until M3. Acceptable for the preview, or should M3 come
   before the generator is shown to anyone?
2. **Array types in `LuaCallCSharp`.** Legacy wraps them (`Get`, `Set`,
   `Address`, the length constructor); the source generator leaves them to
   reflection with XLSG004. Generate them (small, from `IArrayTypeSymbol`
   rules written by hand), or keep reflection and document it? *2026-10-18:*
   reflection, in both generators; the legacy wrapper did not compile and
   arrays never used it (difference 4).
3. **Internal types.** Legacy wraps only public types (`IsPublic`); the source
   generator does the same, although code in the same assembly could wrap
   `internal` `[LuaCallCSharp]` types. Keep legacy's rule in every preset?

### M3 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#source-generator-preview)
and, for the runtime lookup, under [Generated code](../MIGRATING.md#generated-code-xluagenerate-code).
What landed:

| Part | Where |
|---|---|
| `SignatureKey` (section 5.4), both purposes | `General/SourceGenerator/XLua.SourceGenerator/Bridges.cs` |
| Delegate bridges (row 4) and interface bridges (row 5) | `Bridges.cs`, registered from `Emitter.cs` |
| Registry lookup by signature for unconfigured delegate types | `Assets/XLua/Src/Binding.cs` `CreatorBySignature`, `ObjectTranslator.cs` `getDelegate` |
| Legacy bridge dump (test only, `XLUA_GEN_MANIFEST`) | `Assets/XLua/Src/Editor/Generator.cs` `bridgeKey`, `delegateBridgesSurface`, `interfaceSurface` |
| Bridge set diff | `Test/Native/surface_diff.py` (`XLUA_BRIDGE_DIFF_PASS`), run by `run_managed.py --gen-code --source-gen` |
| Host checks of the bridges | `Test/Native/SourceGenLauncher.cs` |
| Generator unit tests (21; `SignatureKeyTable`, `DelegateAndInterfaceBridges` are new) | `General/SourceGenerator/XLua.SourceGenerator.Tests` |
| Measurements | [build/SOURCEGEN.md](../../build/SOURCEGEN.md), "Delegate bridges and LuaEnv start-up" |

How it follows sections 2 to 6:

- **`SignatureKey.Of(IMethodSymbol, KeyPurpose, flags)`** writes
  `Ret(Mode Type[...],...)` as in section 5.4, with one change: Type is the
  `Type.ToString()` name (`Names.Clr`, which M1 already checks against the
  runtime), not the `global::` C# form. The legacy dump can then write the
  same key from reflection (`Generator.cs bridgeKey`), and the bridge set
  diff compares the strings. Nullable annotations and tuple element names
  are not part of that name; `dynamic` is now `System.Object` in `Names.Clr`
  (also in the name table, where it is what `Type.ToString()` says).
  `KeyPurpose.Hotfix` applies the legacy reduction (`self` first, value
  types kept unless `ValueTypeBoxing`, other non-ref, non-params parameters
  as `object`); nothing calls it before M7, but the unit test covers it.
- **Delegate bridges:** the `[CSharpCallLua]` delegate types (public, not
  obsolete, as `GetGenConfig`) are grouped by key, sorted by key, and each
  group gets `internal static __Gen_Delegate_Imp<k>(this DelegateBridge, ...)`
  in `internal static class Bridges` (`XLua.Bridges.g.cs`), with the body of
  `LuaDelegateBridge.tpl.txt` (results through the lenient integer casters of
  `GetResultCasterStatement`, `in` for `in` and `ref readonly`, `EnsureStack`
  above `UncheckedPushes`, the `THREAD_SAFE || HOTFIX_ENABLE` lock). Each type is
  registered as `b.Delegate(typeof(D), __gen_bridge => new D(__gen_bridge.__Gen_Delegate_Imp<k>))`.
  For the suite: 32 delegate types on 26 bridges, as legacy.
- **Interface bridges:** `internal sealed class B_<name>_<hash> : LuaBase, I` per
  interface, in a file of its own, with explicit implementations of what
  `getInterfaceInfo` selects (methods, properties, events, single-key
  indexers of the interface and every interface it extends), the body of
  `LuaInterfaceBridge.tpl.txt`, errors through `BindingApi.ThrowError`, and
  `b.InterfaceBridge(typeof(I), B_...__Create)`. Parameters are named `p0`...
  so no parameter name can clash with the generated locals. Static members
  with bodies and private or sealed default members are not implemented (a
  class cannot); default implementations that can be overridden are, as
  legacy does.
- **Not bridged (info XLSG004), where legacy wrote code that did not compile or
  an incomplete class:** pointer or ref struct parameters and results, ref
  returns, generic and static abstract interface members, indexers with more
  than one key, init-only interface setters, and, with Roslyn before 4.10,
  `ref readonly` parameters with `modreq` (legacy skips those types too, with
  an Editor warning). Such a type converts as an unconfigured one.
- **Registry-based `getDelegate`** (section 2 row 4): the registry is asked
  first by type, then the virtual `GetDelegateByType` (CodeEmit). For a type
  without an entry, `BindingRegistry.CreatorBySignature` takes the first
  registered delegate type whose `Invoke` has the same parameters
  (`Utils.IsParamsMatch`, which agrees with the legacy comparer) and binds the
  new type to the method of a delegate that its creator makes
  (`Delegate.Method`, `Delegate.CreateDelegate(type, bridge, method)`), so the
  legacy rule "an unconfigured delegate with a matching signature works"
  holds without a lookup by name. The source generator does not call
  `AssemblyBindings.DelegateBridges`; the name scan stays for the legacy
  `DelegatesGensBridge` and its hotfix bridges.
- **`Gen_Flag`** is set by units with `Delegate`, `DelegateBridges` or (new)
  `InterfaceBridge` entries. A source-generated unit with `[CSharpCallLua]`
  types therefore turns CodeEmit off, which ends M2's +9 ms per `LuaEnv`. A unit
  with wrappers only still leaves it off (question 1 below).
- **Bridge set diff** (section 8): the legacy dump lists each bridge group by
  key with its delegate types (and how many hotfix signatures share it), and
  each interface bridge with its members; the manifest lists the same.
  `surface_diff.py` requires every delegate type to have the same key in both
  and every interface bridge the same members; legacy groups that serve only
  hotfix signatures are counted and left for M7. A negative control (a key
  and a member changed in the legacy dump) fails with both differences.
- **Not in M3:** hotfix bridges in user assemblies (M7), and skipping bridges
  that a referenced assembly already has (5.1 covers wrapped types; for
  bridges the first registration wins, and duplicate code in two assemblies
  is harmless).

**Benchmark** ([build/SOURCEGEN.md](../../build/SOURCEGEN.md), `gencode-csc`
against `sourcegen`, three interleaved rounds on a shared machine):
`env.create_dispose` 3.35 ms against 3.04 ms for legacy (12.0 ms in M2) with
the same 190 KB per `LuaEnv`; `delegate_int` 65.1 against 64.1 ns (+12% through
CodeEmit in M2); the other delegate and `LuaFunction` cases within 5% except
the struct cases, which wait for the M4 pushers.

Tested on macOS arm64, Lua 5.5.1, Mono 6.14.1, .NET SDK 10.0.401, at `2698d42`
plus this work (415 Lua tests):

- `build_source_generator.py --test`: 21 of 21;
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen`:
  every mode 415 of 415; surface diff 72 types, 656 members, 13 extension
  methods equal; bridge diff 32 delegate types on 26 bridges and 8 interface
  bridges equal, no allowed differences;
- the same (`--gen-code --source-gen`) with all seven bridge hardening defines,
  and with `THREAD_SAFE` (the lock path of the bridges);
- `hotfix_check.py` (85 fixture checks, two assemblies with a merged id map);
- `luacats_check.py --download-luals`;
- `editor_compile_check.py`: 7 editors x 48 configurations, 329 ok, 7 skipped;
- `asmdef_compile_check.py` against 2021.3.45f2 and, with `HOTFIX_ENABLE`,
  6000.6.6f1;
- `docs/source/sync.mjs --check`.

Not run: any Unity editor or player. Remaining in M3:

1. **[checked 2026-10-18] IL2CPP:** the M1 step 5 of "M1 as implemented"
   (the closed static delegates were checked with the legacy bridges; the
   source-generated ones are internal methods of the user assembly), and
   `CreatorBySignature` in a player: it calls `Delegate.CreateDelegate` with
   a `MethodInfo` taken from a generated delegate, as the legacy lookup by
   signature does with a `MethodInfo` found by name. Both work in Android
   ARM64 IL2CPP players; see [IL2CPP players](#il2cpp-players-with-the-source-generator-2026-10-18).
2. **[checked 2026-10-18] The Editor:** the bridges in Assembly-CSharp and a game asmdef with the
   generator installed (M2 remaining item 1), and the editor suite's
   `CSharpCallLua` cases through them. In 2021.3.45f2, 6000.3.26f1 and
   6000.6.6f1 (`prepare.py --source-gen`, mode `sourcegen`) `Combine` went
   to `XLua.Generated.Assembly_CSharp_<hash>.Bridges.__Gen_Delegate_Imp0`
   (static, closed over the `DelegateBridge`), a delegate of the asmdef
   `SgGame` to `XLua.Generated.SgGame_<hash>.Bridges`, and the interfaces to
   `B_*` classes in their assembly; `CreatorBySignature` bound an
   unconfigured `Func<int, int>` to a configured `(int) -> int` delegate of
   `SgGame` (the fixture now avoids that signature, since the smoke test
   expects that delegate to fail without generated code). With
   `Gen_Flag` on, list-only configuration got no CodeEmit: in a first run
   the play mode host's `IEnumerator` (a list entry) had no bridge and 8
   tests failed, until the lists were also given as attributes. That is
   question 1 below in practice: a project that installs the generator
   before converting its lists loses those bridges in the Editor, with only
   an XLSG011 info.

**Questions for the owner (M3):**

1. **`Gen_Flag` for units without `[CSharpCallLua]` types.** Legacy output
   always sets it. A source-generated unit sets it only when it has bridges,
   so a project whose source-generated assemblies only wrap types keeps
   CodeEmit on in the Editor and pays its type scan per `LuaEnv`, but
   delegates and interfaces configured in an assembly where the generator did
   not run still work there through CodeEmit. Setting it for every unit that
   binds code would match legacy and save the scan; those delegates would then
   fail as they do in IL2CPP players, which the section 5.3 checks report.
   Which one?
2. **Bridges for types that legacy could not compile** (the XLSG004 list
   above) are skipped instead of failing the build. Keep that, or make some of
   them (for example a generic method in a `[CSharpCallLua]` interface) an
   error, since the interface then silently falls back?

### M4 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#source-generator-preview)
and, for xLua's defaults, under [Configuration attributes](../MIGRATING.md#configuration-attributes).
What landed:

| Part | Where |
|---|---|
| GCOptimize selection, push/get/update, table readers, Pack/UnPack, registration | `General/SourceGenerator/XLua.SourceGenerator/Structs.cs` |
| Wrappers and bridges push, read and update through them | `Wrappers.cs` `Push`, `Cast`, `Update`, `Self` |
| xLua's UnityEngine defaults as `[assembly: GCOptimize]`, `[assembly: AdditionalProperties]` | `Assets/XLua/Src/GenAttributes.cs` |
| Legacy dump of the GCOptimize types (test only) | `Generator.cs` `structsSurface`, `packUnpackSurface` |
| Struct-push diff | `Test/Native/surface_diff.py` (`XLUA_STRUCT_DIFF_PASS`) |
| Host checks: registration, array accessors, no managed allocation per struct | `Test/Native/SourceGenLauncher.cs` |
| The generator in the asmdef layout against an editor's DLLs | `Test/Native/asmdef_compile_check.py --source-gen` |
| Generator unit tests (22; `StructsFollowTheLegacyModel` is new) | `XLua.SourceGenerator.Tests` |
| Measurements | [build/SOURCEGEN.md](../../build/SOURCEGEN.md), "GCOptimize structs" |

How it follows section 2 rows 6 to 8 and the M0 API:

- **Selection,** rule by rule from `Generator.cs`: the GCOptimize types
  (public, not obsolete, not open) and the `[LuaCallCSharp]` enums;
  `SizeOf` over public instance fields and `[AdditionalProperties]`
  properties (primitives but `bool`, `char` and `IntPtr`, `decimal`,
  packable structs); "value" types (enums and packable structs) get
  `Push_`/`Get_`/`Update_`, unpackable ones and classes a table reader
  `Get_`; the wrappers use the helpers for exactly `genPushAndUpdateTypes`
  (packable GCOptimize types and `[LuaCallCSharp]` enums) and
  `StructGetName`'s types, so a GCOptimize enum outside `[LuaCallCSharp]`
  gets registered pushers but wrappers box it, as legacy does. `AllSubStruct`
  gives the Pack/UnPack types; floats are packed up to six at a time when
  every field is a float (`xlua_pack_float2..6`).
- **Shape:** `internal static class Structs` and `PackUnpack` in
  `XLua.Generated.<Id>` (`XLua.Structs.g.cs`, `XLua.PackUnpack.g.cs`), static
  methods with the translator as first parameter, called as
  `Structs.Push_<name>_<hash>(translator, L, v)`. Type ids and enum caches go
  through one `BindingApi.AllocateSlot` per type, `TypeId` and `EnumCacheRef`
  (the registry table of M2, out of reach of scripts). Registration:
  `b.Struct<T>(Push, Get, Update)` and `b.ArrayAccessor<T>(get, set)` per value
  type, `b.Caster<T>(Get)` and a setter-only `ArrayAccessor` per table type.
- **Across assemblies:** a type configured in a referenced assembly that can
  configure xLua (xLua itself or one that references it: its
  `[assembly: GCOptimize]` attributes and `[GCOptimize]` on its public types) is
  pushed without boxing here too, through this assembly's own helpers. Those
  are written only when this assembly's wrappers or bridges use them (found
  in the generated text, so cached models count), and registered as well;
  the first registration of a type wins (section 5.1). The legacy generator
  sees all assemblies at once, so it needs no such rule.
- **xLua's defaults:** `SysGenConfig`'s lists (UnityEngine vectors, `Color`,
  `Quaternion`, `Ray`, `Bounds`, `Ray2D` and three `AdditionalProperties`
  entries) are repeated as assembly attributes, as M1 planned. In the asmdef
  layout the generator then writes their pushers into `XLua.dll` too
  (`asmdef_compile_check.py --source-gen` checks `Push_UnityEngine_Vector3_`
  there, against 2021.3, 6000.3 and 6000.6).
- **XLSG010** is a warning for a GCOptimize struct of this assembly that cannot
  be packed, as section 5.8 says; GCOptimize classes (table-read by design) get
  none.
- **Differences from legacy,** all outside the suite's configuration: keyword
  field names are escaped in C# (legacy wrote `val.class`, and its table
  reader looked up the escaped name `@class` in Lua); and the property
  temporaries in `UnPack` are named `__gen_<name>` instead of the property's
  own name.
- **The +21% `delegate_struct_boxed` gap of M3** was not in the boxed path:
  the same M3 executables run with only the boxed cases give equal times in
  both modes. It came from the boxed `struct_gcopt_*` cases that run before
  them in the same process, and is gone now that those do not box
  (build/SOURCEGEN.md).

**Benchmark** (build/SOURCEGEN.md, three interleaved rounds): every
`struct_gcopt_*` and `delegate_struct_gcopt` case within 3% of legacy with the
same bytes (0 managed + 52 Lua per value; M3: 32 + 36 and +52% to +99%); the
boxed struct cases within 2%; the other cases within -1% to +4%.

**"AllocProbe" checks:** `Test/MobileSmoke/alloc_probe.py` measures
`new LuaEnv()` and first type access in reflection mode (`AllocProbe.cs`,
`AllocProbeGeneral.cs`), which no generated code touches, so it is not the
probe for this milestone and was not run. The per-value allocation of the
generated struct path is measured by the benchmark's allocation pass (equal
to legacy above) and asserted by `SourceGenLauncher.cs` (288 managed bytes for
5,000 `TestStruct` values made and read in Lua).

Tested on macOS arm64, Lua 5.5.1, Mono 6.14.1, .NET SDK 10.0.401, at `650e32d`
plus this work (415 Lua tests):

- `build_source_generator.py --test`: 22 of 22;
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen`:
  every mode 415 of 415; surface diff (72 types, 656 members, 13 extension
  methods), bridge diff (32 delegate types on 26 bridges, 8 interface bridges)
  and struct diff (19 GCOptimize types, 16 of them pushed without boxing, 14
  packed types) equal, no allowed differences;
- the same (`--gen-code --source-gen`) with all seven bridge hardening defines,
  and with `THREAD_SAFE`;
- `hotfix_check.py`; `luacats_check.py --download-luals`;
- `editor_compile_check.py`: 7 editors x 48 configurations, 329 ok, 7 skipped;
- `asmdef_compile_check.py` against 2021.3.45f2, and 6000.6.6f1 with
  `HOTFIX_ENABLE`; with `--source-gen` against 2021.3.45f2, 6000.6.6f1 and,
  with `HOTFIX_ENABLE`, 6000.3.26f1;
- `docs/source/sync.mjs --check`.

Not run: any Unity editor or player. Remaining in M4:

1. **[checked 2026-10-18] IL2CPP:** the GCOptimize helpers in a player
   (they are ordinary static methods and delegates over them, as the M0
   legacy shape is; the array accessors are lambdas closed over nothing);
   see [IL2CPP players](#il2cpp-players-with-the-source-generator-2026-10-18).
2. **The Editor:** the generator's pushers for xLua's defaults in `XLua.dll`
   and in Assembly-CSharp with a real UnityEngine configuration, and the
   editor suite's GCOptimize cases through them.

**Questions for the owner (M4):**

1. **Copies of the helpers for types configured elsewhere.** An assembly that
   uses `UnityEngine.Vector3` gets its own copy of the Vector3 helpers (about
   80 lines per type) instead of calling xLua's, which are `internal`. The
   alternative is a public, versioned per-type entry point in the binding API
   (for example `BindingApi.Pusher<T>()` returning the registered delegates),
   one delegate call per value instead of a direct call. Keep the copies?
2. **`SysGenConfig` twice.** The defaults are now in the lists (legacy) and in
   assembly attributes (source generator), to be kept in step by hand until
   the legacy generator goes (M11). Acceptable, or should the legacy
   generator read the assembly attributes too?

### M5 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#source-generator-preview).
What landed:

| Part | Where |
|---|---|
| Per-member fallback: selection and registration | `General/SourceGenerator/XLua.SourceGenerator/Members.cs`, `Wrappers.cs` `LazyRegistration` |
| `BindingApi.RegisterFallback` and the log on first use | `Assets/XLua/Src/Binding.cs`, `Utils.cs` `LazyReflectionCall` |
| link.xml fragment per assembly | `LinkXml.cs`, `Emitter.cs` (`XLua.LinkXml.g.cs`) |
| No-registration layers: Editor, build, development player | `Assets/XLua/Src/Editor/SourceGenChecks.cs`, `Binding.cs` `EnsureRegistered` |
| link.xml for UnityLinker (`IUnityLinkerProcessor`) | `SourceGenChecks.cs` `SourceGenLinkXml` |
| Forced-fallback host run (`XLUA_SG_SKIP_REGISTER`) | `run_managed.py --source-gen-fallback`, `Emitter.cs` |
| Host checks: fallback member and its log, link.xml against the reflection uses | `Test/Native/SourceGenFallback.cs`, `SourceGenLauncher.cs` |
| Generator unit tests (23; `FallbackAndLinkXml` is new) | `XLua.SourceGenerator.Tests` |

How it follows sections 5.3, 5.5 and 5.7 and the decisions:

- **Which members fall back** (decision 6): the members the compiler cannot
  call but reflection can, registered with `BindingApi.RegisterFallback`
  (`Utils.RegisterLazyFunc`, the `DoNotGen` path, plus the reason) and
  reported as info XLSG004:
  - init-only setters (legacy wrote code that did not compile; M2 dropped
    them);
  - methods whose `ref readonly` parameters carry `modreq` on Roslyn before
    4.10 (CS0570; legacy drops them with an Editor warning). A lazy member
    replaces a whole name, so a name falls back only when none of its
    overloads can be wrapped; an overload next to wrapped ones stays dropped
    (XLSG004).

  Not falling back, though section 5.5 lists them, because reflection cannot
  call them either: generic methods without usable constraints
  (`MethodWrapsCache.tryMakeGenericMethod` skips them too; Lua reaches them
  with `xlua.get_generic_method`), indexers with several keys or a
  `ref readonly` key (there is no lazy indexer), pointers and ref structs.
  "An inaccessible type in the signature" does not occur: only public
  members of public types are wrapped.
- **The log:** the first use of a fallback member logs
  "xLua: T.m is reached through reflection: <reason>", once per member per
  process, as `Debug.LogWarning` (the console under `XLUA_GENERAL`). Generated
  code passes the reason only under `UNITY_EDITOR || DEVELOPMENT_BUILD ||
  XLUA_GENERAL`; release players get `null` and log nothing.
- **link.xml** (section 5.7): `XLua.LinkXml.g.cs` holds the fragment,
  compiled only with `UNITY_EDITOR` or `XLUA_GENERAL`, as the manifest:
  - `[ReflectionUse]` types `preserve="all"`, as the legacy `LinkXmlGen` does.
    Section 5.7 proposed listing their members one by one unless the user asks
    for `preserve="all"`; that would stop `xlua.private_accessible` and other
    non-public uses on them under stripping, a change to the `legacy`
    behaviour, so it is a question below.
  - each `DoNotGen` or fallback member of a wrapped type, as
    `<method|field|property|event name="..."/>` (by name, all overloads), when
    the type declares a member of that name; a lazy member that names only
    extension methods needs none (the extension method map keeps them through
    delegates).
  - wrapped members: nothing; the generated code calls them.
- **`IUnityLinkerProcessor`** (`SourceGenLinkXml`) merges the fragments of the
  assemblies loaded in the Editor by assembly and type (a type with
  `preserve="all"` anywhere keeps it) into `Temp/XLuaSourceGen/link.xml` and
  returns that path. Section 5.7 planned to read the player-compiled
  assemblies with Cecil; xLua has Cecil only with `HOTFIX_ENABLE`, so the
  Editor assemblies are read instead, and configuration under player-only
  `#if` branches is not seen. `IUnityLinkerProcessor` in 2021.3 has the one
  method `GenerateAdditionalLinkXmlFile(BuildReport,
  UnityLinkerBuildPipelineData)`; the class compiles against all seven
  editors (`editor_compile_check.py`).
- **The three no-registration layers** (section 5.3), all inactive unless
  `XLua.SourceGenerator.dll` with the `RoslynAnalyzer` label is in the
  project, so "Generate Code" projects see nothing new:
  1. after each domain reload, `Debug.LogError` for every loaded assembly
     with `[LuaCallCSharp]`, `[CSharpCallLua]`, `[GCOptimize]` or
     `[ReflectionUse]` on a type (`TypeCache`) or at assembly level and no
     `[assembly: GeneratedBindings]` (legacy units count), naming the likely
     causes (CS9057, a lost label, no reference to the XLua assembly, Unity
     older than 2021.3);
  2. `SourceGenBuildCheck` (`IPreprocessBuildWithReport`) makes the same
     check, fails an IL2CPP build when such an assembly has
     `[CSharpCallLua]` configuration (decision 9), and warns otherwise;
  3. a development player in which nothing registered warns once from
     `BindingRegistry.EnsureRegistered` (not compiled into the Editor or
     release players).
- **Forced fallback** (section 8, item 1): `XLUA_SG_SKIP_REGISTER` compiles
  every generated `Register` empty. `run_managed.py --source-gen-fallback`
  builds the source-gen suite with it: nothing registers, `Gen_Flag` stays
  off, the suite passes through reflection and CodeEmit.
- **The surface diff** lists the fallback members apart (`"fallback"`), since
  the legacy generator drops them. For the suite that is the fixture
  `SourceGenFallback.Locked`, an allowed difference, because only the
  source-gen build compiles the fixture (mcs has no `init`).

Tested on macOS arm64, Mono 6.14.1, .NET SDK 10.0.401, at `a84e037` plus this
work (417 Lua tests), with Lua 5.5.1 and with LuaJIT 2.1:

- `build_source_generator.py --test`: 23 of 23;
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen
  --source-gen-fallback`, with both plugins: every mode 417 of 417, including
  the run without registration; surface diff (72 types, 656 members, 13
  extension methods; the fixture type allowed, one fallback member listed),
  bridge diff and struct diff equal; the link.xml check keeps 16 lazy members
  and 2 `[ReflectionUse]` types; the fallback member logged once;
- with Lua 5.5.1, `--gen-code --source-gen --source-gen-fallback` with all
  seven bridge hardening defines, and `--gen-code --source-gen` with
  `THREAD_SAFE`;
- `hotfix_check.py`; `luacats_check.py --download-luals`;
- `editor_compile_check.py`: 7 editors x 48 configurations, 329 ok, 7 skipped
  (`SourceGenChecks.cs` compiles in every one);
- `asmdef_compile_check.py` against 2021.3.45f2, and 6000.6.6f1 with
  `HOTFIX_ENABLE`; with `--source-gen` against 2021.3.45f2 and, with
  `HOTFIX_ENABLE`, 6000.6.6f1;
- `docs/source/sync.mjs --check`.

The fallback of methods with `ref readonly` parameters is not covered on the
host: it needs a compiler older than Roslyn 4.10 compiling `modreq`
parameters, which only Unity 2021.3 to 6000.3 runs (the Editor step below).

Not run: any Unity editor or player. The Unity and IL2CPP parts of M5's
"testable by" follow, for an agent with a Unity slot (one editor at a time;
not while a phone benchmark holds adb).

1. **Editor CS9057 control** (2021.3.45f2, Roslyn 4.3):
   1. Build a generator against Roslyn 4.10: a copy of
      `General/SourceGenerator` with `Microsoft.CodeAnalysis.CSharp` 4.10.0 in
      the `.csproj` and its lock file updated (`--update-lock` on the copy
      only), built to a separate `--output`.
   2. `Test/UnityMatrix/prepare.py --asmdef --editor-tools` into an empty
      folder, a script with `[LuaCallCSharp]` and `[CSharpCallLua]` types in
      Assembly-CSharp, `build_source_generator.py --install
      <project>/Assets/XLua/Src/Analyzers`, then the DLL replaced by the 4.10
      build (keeping the `.meta`).
   3. Open in batch mode (`-quit -logFile`): expect warning CS9057 for the
      analyzer and, after the reload, "xLua: assembly Assembly-CSharp has xLua
      configuration but no generated bindings". With the 4.3.1 build: no such
      error, and `XLua.Generated.Assembly_CSharp_*` types.
   4. An Android IL2CPP build with the 4.10 build must fail with
      "[CSharpCallLua] configuration but no generated bindings"; with Mono it
      must warn and go on.
2. **[checked 2026-10-18, [IL2CPP players](#il2cpp-players-with-the-source-generator-2026-10-18)] IL2CPP High-stripping static check**
   (6000.3.21f1, Android ARM64, `Test/MobileSmoke`):
   1. `prepare.py --android-plugin ... --macos-plugin ...`, the asmdef layout
      (copy `Assets/XLua/Asmdef~/Src/*.asmdef` into the project's
      `Assets/XLua/Src` and `Src/Editor`), the generator installed as in 1.2,
      and a script in Assembly-CSharp with a `[LuaCallCSharp]` class that has a
      `[DoNotGen]` method and an unrelated public method, a `[ReflectionUse]`
      class, a `[GCOptimize]` struct and a `[CSharpCallLua]` delegate.
   2. `XLUA_SMOKE_STRIPPING=High` and `SmokeBuild.BuildAndroid`, keeping
      `Library/Bee`.
   3. Check:
      - `Temp/XLuaSourceGen/link.xml` exists, lists the `[DoNotGen]` method and
        the `[ReflectionUse]` class with `preserve="all"`, and is among the
        linker's inputs (the UnityLinker response file under `Library/Bee`);
      - the stripped `Assembly-CSharp.dll` (`ManagedStripped`, read with
        monodis) keeps `XLua.Generated.*.Bindings`, the `W_*` wrapper,
        `Structs`, `Bridges`, the `[DoNotGen]` method and every member of the
        `[ReflectionUse]` class, drops the unrelated public method that only
        reflection could reach, and has no `LinkXml` or `Manifest` class;
      - `RuntimeInitializeOnLoads.json` lists `Bindings.Register`;
      - the C++ of `Bridges` creates its delegates with the bridge as target,
        as the M1 check of the legacy bridges did.
   4. Run the APK on the emulator (`Test/MobileSmoke/README.md`): the smoke
      pass line and, in a development build with an init-only property set
      from Lua, the fallback warning once.
3. **Editor suite** (`Test/UnityMatrix`): with the generator installed and the
   suite's configuration in Assembly-CSharp, no "no generated bindings" error
   after the reload, and a player build that writes the merged link.xml.

**Questions for the owner (M5):**

1. **`[ReflectionUse]` and `preserve="all"`.** M5 keeps the legacy
   `preserve="all"` for `[ReflectionUse]` types. Section 5.7 proposed listing
   their public members instead (smaller players with High stripping), with
   `preserve="all"` only on request (a new `ReflectionUse(typeof(T), Preserve
   = All)` form). That stops `xlua.private_accessible` and other non-public
   reflection on those types under stripping. Make it the `minimal` preset's
   behaviour, every preset's, or none?
2. **link.xml from the Editor assemblies.** Reading the player-compiled
   assemblies, as section 5.7 planned, needs Cecil in `XLua.Editor` without
   `HOTFIX_ENABLE` (Unity's `com.unity.nuget.mono-cecil` package, or the Cecil
   the hotfix tools pin). Worth it for configuration under player-only `#if`
   branches, or keep reading the Editor assemblies?

### IL2CPP players with the source generator (2026-10-18)

M1 step 5, M2 item 2, M3 item 1, M4 item 1 and M5 item 2 above, in one
fixture: `Test/MobileSmoke/prepare.py --source-gen` (the asmdef layout, the
generator installed, `SgSmoke.cs` in Assembly-CSharp with a
`[LuaCallCSharp]` class with a blacklisted field and an
`[assembly: DoNotGen]` method, an init-only setter, a `[ReflectionUse]`
class, a `[GCOptimize]` struct, a `[CSharpCallLua]` delegate and an unused
class). Unity 6000.3.21f1, Android ARM64 IL2CPP, Development builds with
Minimal and with High stripping, run on the 16 KB `xlua16k` emulator
(Android 16). Results and sizes are in
[Test/MobileSmoke/README.md](../../Test/MobileSmoke/README.md#2026-10-18-the-source-generator-in-il2cpp-players).

- **Both players pass** the smoke and the generator checks: Lua calls go
  through the generated wrapper (the blacklisted field is not visible),
  reflection reaches the `DoNotGen` method, the init-only setter (with the
  fallback warning once) and the `[ReflectionUse]` type, the GCOptimize
  struct goes in and out, `SgTwice` is the static
  `Bridges.__Gen_Delegate_Imp0` of Assembly-CSharp closed over its
  `DelegateBridge` **[C]**, and `Func<int, int>`, which nothing configures,
  gets that bridge through `CreatorBySignature` **[C]**.
- **High stripping:** the stripped Assembly-CSharp keeps the generated
  types and every member Lua reaches only by reflection, drops the unused
  class, and has no `LinkXml` or `Manifest` class; UnityLinker gets the
  merged link.xml (`--include-link-xml`); `RuntimeInitializeOnLoads.json`
  lists both `Bindings.Register` (Assembly-CSharp and XLua's default
  pushers); the C++ of the registration creates the delegate with the
  bridge as target **[C]**.
- **Fixed on the way:** every IL2CPP build with the generator installed
  failed in UnityLinker, because `SourceGenChecks` wrote the merged link.xml
  with an `encoding="utf-16"` declaration into a UTF-8 file ("There is no
  Unicode byte order mark"). It is now written without a declaration;
  `XluaSourceGenTests.MergedLinkXmlReadsAsUtf8` checks it in the Editor
  suite, and `BindingsAreGeneratedInEachAssembly` now expects XLua's own
  unit of M4 (39/39 on 2021.3.45f2 in mode `sourcegen`).

Not run: release players, other Unity versions' players, iOS, WebGL, a
phone, the M5 item 1 CS9057 control and the size of the generator against
legacy Generate Code in the same player.

### M6 as implemented (2026-10-18)

The package layout, the editor suite in it on the three editors, an IL2CPP
Android player from it and the size report. Installation is in the
[README](../../README.md#as-a-upm-package-preview), the differences from
the Assets layout in [MIGRATING.md](../MIGRATING.md#upm-package-preview).

| Part | Where |
| --- | --- |
| Package assembly, lint, reproducible tarball | `Tools/make_upm_package.py` |
| Editor suite in the package layout | `Test/UnityMatrix/prepare.py --package [folder]` |
| Hotfix player from the package | `Test/UnityMatrix/hotfix_player.py --package` |
| IL2CPP smoke from the package | `Test/MobileSmoke/prepare.py --package [--source-gen]` |
| Size variants | `Test/MobileSmoke/size_report.py` `+pkg`, `+gen+gcm`, `+pkg+sg`, configurations `sg-minimal` and `sg-high`; `SizeSgConfig.cs` |

**The layout** (one package, decision of 2026-10-17; name `com.xlua-ng.xlua`
and version `0.0.1-dev` are placeholders, `--name` and `--version` set them):

```
package.json            "unity": "2021.3", samples Examples and Tutorial
README.md, CHANGELOG.md, LICENSE.md
Runtime/                Assets/XLua/Src without Editor; XLua.asmdef
Runtime/Analyzers/      XLua.SourceGenerator.dll, RoslynAnalyzer label (--source-gen)
Editor/                 Assets/XLua/Src/Editor + XLuaUnityDefaultConfig.cs; XLua.Editor.asmdef
Core/                   Assets/XLua/Core with XLua.Core.asmdef, once that folder exists
Plugins/                the plugins given (--plugins BUILD/plugin_lua55, --plugin REL=PATH)
Plugins/WebGL/          xlua_webgl.cpp, includes pointed at build~/
build~/                 the C sources xlua_webgl.cpp includes
Documentation~/         Assets/XLua/Doc and the upstream changelog
Samples~/               Examples (with Editor/ExampleConfig.cs), Tutorial
```

- **GUIDs:** every file keeps its repository `.meta`, so a project that moves
  from the Assets layout keeps its references (UPM spike, finding 4). A file
  without a `.meta` stops the build, since an immutable package ignores it;
  the repository gained the missing `XLuaUnityDefaultConfig.cs.meta`.
  Folders shared with other plugins (`Plugins`, `Plugins/Android`, ...) get
  GUIDs derived from their package path, because a moving project keeps its
  `Assets/Plugins` folders and two folders with one GUID conflict;
  `xlua.bundle` keeps its `.meta` (its import settings). The lint checks that
  every asset has a `.meta`, every `.meta` an asset, and that GUIDs are
  unique.
- **Asmdefs:** the files of `Assets/XLua/Asmdef~` with their GUIDs. Both now
  have the `!XLUA_CORE_ONLY` define constraint of
  [core-luascript.md 2.2.1](core-luascript.md#221-one-package-two-assemblies)
  (also in the Assets opt-in layout). `Assets/XLua/Core` does not exist yet
  (core M5 is native only), so the package has no `XLua.Core` assembly; the
  script picks the folder up when it appears.
- **The generator goes into `Runtime/Analyzers`,** inside the XLua asmdef
  folder, so Unity runs it in the assemblies that reference XLua, as in the
  asmdef layout. Without `--source-gen` the package is the legacy flavour
  (Generate Code into `Assets/XLua/Gen`).
- **WebGL** (`xlua_webgl.cpp` includes `../../../build/...` next to `Assets`)
  gets a copy whose includes point at the package's `build~` folder, which
  Unity does not import. Not built yet; `MobileSmoke/prepare.py` refuses
  `--webgl --package`.
- **Plugin variants** (`xlua-variants/<name>/`) have no repository `.meta` and
  are left out with a note.
- **Defines:** `Assets/csc.rsp` reaches the package's assemblies (checked in
  the compiler response files of every run), as do Player Settings defines;
  `GEN_CODE_MINIMIZE` through `csc.rsp` compiled and sized below.

**Editor suite** (`Test/README.md`, "UPM package layout"), the package from
a `file:` tarball (read-only), on 2021.3.45f2, 6000.3.26f1 and 6000.6.6f1:
Generate Code and Clear Generated Code, the same with `--hotfix`, mode
`sourcegen` with and without `--hotfix` (with `--roslyn-sdk`), a `file:`
folder install on 6000.3, and the macOS hotfix player built and injected
from the package on all three. Every run passed on `976cbe1e` (main at
`f8e6210d` merged): 32/32 (2021.3, 6000.3) and 36/36 (6000.6) in both legacy
modes, with and without `--hotfix`; 43/43 in mode `sourcegen` on each
editor, with and without `--hotfix`, and from the folder install.

**IL2CPP Android** (6000.3.21f1, `xlua16k` emulator): the smoke from the
package with the generator (Minimal and High) and without it (Minimal)
passed; IL2CPP compiles the same code as in the asmdef layout (sizes within
0.2%, `Test/MobileSmoke/README.md`). All three passed on `4ab2c296` and
again on `976cbe1e` (main at `f8e6210d` merged), with the same sizes.

**Size** ([build/SIZE.md](../../build/SIZE.md#generated-bindings-generate-code-gcm-and-the-source-generator)),
release ARM64 APKs, five wrapped types and two delegates in one
configuration:

| bindings (package layout) | APK delta, Minimal | APK delta, High |
| --- | ---: | ---: |
| Generate Code | +1,639,710 | +1,521,482 |
| `GEN_CODE_MINIMIZE` | +1,608,362 | +1,484,402 |
| source generator | +1,619,530 | +1,500,490 |

The source generator is 20 KB below Generate Code and 11 KB (Minimal) to
16 KB (High) above GCM; `libil2cpp.so` is 52 KB below Generate Code and
63 KB above GCM, uncompressed. The package layout itself adds nothing
measurable (within 2.3 KB of the Assets layout with Generate Code and with
GCM). This answers the GCM question of M10 for a small configuration;
the gap should grow with the number of wrapped members (not measured).

**Fixed on the way** (details in `Test/README.md`): LuaCATS and Validate
Setup took Generate Code output for source generator output since M10
(`LuaCATSGenerator.cs`, regression case in `luacats_check.py`); CS9198 on
6000.6 in the generator's registration for `ref readonly` delegates
(`Emitter.cs`, generator unit test); two editor tests that predated M5 and
M10.

Not run then: 2022.3 and 6000.0, Linux and Windows editors, importing the samples
(`SignatureLoaderTest.cs` reads from `Assets/XLua/Examples`, so that example
needs the old path), iOS, WebGL, Android players from 2021.3 or 6000.6,
release players on a device, a phone, publishing (nothing was published),
and a package with the `XLua.Core` assembly.

**Follow-up runs (2026-10-18, main at `65f7ea17` with core M6, so the
package has `Core/` with `XLua.Core`):** the editor suite from the package on
2022.3.62f3 and 6000.0.85f1 (33/33 in both legacy modes with and without
`--hotfix`, 44/44 in mode `sourcegen` with and without `--hotfix`), a
Development WebGL player from the package on 6000.6.4f1 in headless Chrome,
`hotfix_intkey.py --package` on 2021.3.45f2 and 6000.3.26f1, and with the
generator `hotfix_intkey.py` and `hotfix_player.py` (2021.3.45f2,
6000.3.26f1, 6000.6.6f1; `Test/README.md`, "2022.3, 6000.0, IL2CPP targets
and hotfix with the generator"). Two failures, both from changes after M6:

- **XLSG012 (new):** core M6 moved `lua_CSFunction` and
  `MonoPInvokeCallbackAttribute`, which every generated wrapper names, into
  `XLua.Core`; an asmdef that references only `XLua` failed with CS0234 for
  each wrapper method. The generator now reports XLSG012 (error, "add
  XLua.Core to the references of its asmdef") once and writes nothing for
  that assembly; unit test `Xlsg012CoreNotReferenced`. Kept as the rule
  after the alternative was measured
  ([Generated code without XLua.Core](#generated-code-without-xluacore-measured-not-taken)).
- **WebGL:** `<emscripten.h>` inside `xlua_webgl.cpp`'s `extern "C"` block
  (since the core time limits) broke every WebGL build;
  `Test/Native/webgl_compile_check.py` covers it.

**Questions for the owner (M6):**

1. **Package name and version:** answered 2026-10-18: keep `com.xlua-ng.xlua`
   for now and use `0.0.1-dev` while the project is under development; nothing
   is published to a registry until the owner has tested everything. Originally:
   `com.xlua-ng.xlua` and `0.1.0-dev` were placeholders. Which name (it is permanent once published), and does the
   first version follow upstream's 2.1.x or start again?
2. **GCM:** answered 2026-10-18: measure a game-sized configuration first
   (GCM was 11 to 16 KB smaller than the source generator on the small
   configuration); if the gap is big, add GCM to the source generator, if
   small, drop it. Measured the same day: the gap is about 450 to 470 KB of
   APK with 264 wrapped types, and the open question is now in
   ["GCM with a game-sized configuration"](#gcm-with-a-game-sized-configuration-2026-10-18).
3. **One package or two flavours until M11:** with the generator in the
   package, every assembly that references XLua gets generated bindings, and
   Generate Code output next to them would register the same types twice
   (the editor suite skips Generate Code in mode `sourcegen` for that
   reason). Ship only the generator flavour, or both (`--source-gen` or not)
   while the legacy preset exists?
4. **Samples:** keep Examples and Tutorial as samples (they import under
   `Assets/Samples/<package>/<version>/`, which breaks the SignatureLoader
   example's path), or fix the example to find its folder?

### GCM with a game-sized configuration (2026-10-18)

The measurement the owner asked for in M6 question 2, before M11 removes the
legacy generator. Numbers and method are in
[build/SIZE.md](../../build/SIZE.md#a-game-sized-configuration-2026-10-18).

**What GCM is.** `GEN_CODE_MINIMIZE` ("generic code minimization") is a
scripting define, not a menu item or a `GenConfig` setting: put it in the
Player Settings scripting defines (or `csc.rsp`) and both the Editor's
Generate Code and the xLua runtime change. Generate Code then takes the `GCM`
templates (`LuaClassWrapGCM.tpl.txt`, `LuaEnumWrapGCM.tpl.txt`,
`LuaRegisterGCM.tpl.txt`; `Generator.cs:174-191`); the command-line
`XLuaGenerate` has no `--minimize` switch, it takes the define at build time
(`Tools/build_tools.py --define GEN_CODE_MINIMIZE`, `XLuaGenerate.cs:250-290`).
In plain words: without GCM every wrapped member becomes its own native
entry point, a `[MonoPInvokeCallback]` method that Lua calls directly and
that looks up its `ObjectTranslator`, reads the Lua stack top and catches C#
exceptions itself. With GCM the wrapped members are ordinary C# methods
`int f(ObjectTranslator translator, IntPtr L, int top)`, registered as
`CSharpWrapper` delegates in a table of the translator; Lua calls one native
trampoline (`xlua_push_csharp_wrapper`, `xlua_core.c:577-611`) with the
member's index, and one C# entry (`StaticLuaCallbacks.CSharpWrapperCallerImpl`)
finds the translator, catches exceptions and calls the delegate. The
generated code is the same per member minus that per-member boilerplate.

**Configuration.** `Test/MobileSmoke/SizeGameGenConfig.cs`: `ExampleConfig.cs`
with its LINQ queries on (the `migration_check.py` sample without Hotfix),
every exported class and struct of `UnityEngine.CoreModule` and
`UnityEngine.UI` in two namespaces; with Unity 6000.3.21f1, 264 wrapped types
and 26 delegate types. The source generator gets the same configuration as
attributes from the migration tool (`SizeGameSgConfig.cs`). Generate Code's
wrappers for 43 members and two types that exist only in the Editor's
assemblies did not compile in the player; they are in the configuration's
`BlackList` filter, so all three modes leave them out.

**Size** (release ARM64 APK delta over a baseline with uGUI, xLua from the
UPM package, Lua 5.5.1):

| stripping | Generate Code | GCM | source generator | source generator - GCM |
| --- | ---: | ---: | ---: | ---: |
| Minimal | +6,517,306 | +5,825,054 | +6,297,634 | +472,580 (7.5%) |
| Medium | +6,295,094 | +5,602,606 | +6,051,494 | +448,888 (7.4%) |
| High | +6,309,866 | +5,624,514 | +6,070,914 | +446,400 (7.4%) |

- The difference is in `libil2cpp.so`: 2.1 MB uncompressed (580 KB in the
  APK) between the source generator and GCM. IL2CPP writes a reverse P/Invoke
  wrapper for each `[MonoPInvokeCallback]` method: 4,625 in the Generate Code
  and source generator players, 34 with GCM; the C++ for Assembly-CSharp is
  38.8 MB, 39.0 MB and 25.4 MB. The source generator's own wrappers are as
  large as Generate Code's; it is below Generate Code (220 to 240 KB) only
  because it keeps less engine code (members preserved one by one, not
  `preserve="all"`), and its metadata is 60 KB smaller than GCM's.
- IL of the player's Assembly-CSharp: 1,432,064 (Generate Code), 1,293,312
  (GCM), 1,505,792 (source generator). Generated source: 5.4 MB / 130,751
  lines, 4.1 MB / 105,767 lines, 5.8 MB in 268 player files.
- With five wrapped types (M6) the gap was 11 to 16 KB, so it grows with the
  wrapped members: here about 100 bytes of APK for each of the 4,591 wrapper
  methods GCM folds into the trampoline.

**Run-time cost.** Calls through GCM go through the trampoline, a delegate
table lookup and a delegate call more than a direct wrapper.

- Host, `Test/Native/benchmark.py` executables (`gencode`, `gencode-min`,
  `gencode-csc`, `sourcegen`; Mono 6.14.1, macOS arm64, Lua 5.5.1), run in ten
  interleaved rounds on a loaded machine, minimum of the per-round medians:
  Lua-to-C# calls with GCM are 4 to 14 ns slower than mcs-compiled Generate
  Code (+4% to +23%: `static_call` 58.7 against 47.8 ns, `instance_call` 85.1
  against 77.8, `property_get` 91.9 against 86.9, `field_get` 90.6 against
  87.0, `string_return` 94.6 against 80.6); struct construction and C#-to-Lua
  calls are unchanged. The source generator (csc) is within 4% of
  `gencode-csc`. An earlier sequential run (one mode after the other) had GCM
  equal to or faster than Generate Code, which shows how much the load moved
  the numbers.
- Emulator (`xlua16k`, Android 16 arm64, the Minimal release players above,
  the `XLUA_SIZE_TIMING` probe, best of six launches): no difference beyond
  the noise; static call 53 (Generate Code), 46 (GCM) and 57 ns (source
  generator), instance property get 132, 142 and 145 ns, instance method 119,
  121 and 122 ns. The machine ran other agents' builds, so launches varied by
  up to 2x and only the best runs are comparable.

**Projected GCM mode in the source generator** (*estimate* at the time; the
measured result, 225 to 248 KB below legacy GCM, is in the next section): its
wrappers are the size of Generate Code's, so GCM's saving over Generate Code
(692 KB APK, 2.7 MB `libil2cpp.so` uncompressed with Minimal) should apply,
ending about 200 KB below legacy GCM because of the engine code the source
generator already saves.

**Effort to add it:** about one week, as row 1a of section 2 estimated. The
emitter already writes the per-member boilerplate from two constants
(`Wrappers.cs:117-118`) and the registration in one place
(`Wrappers.cs:829-855`):

1. A mode taken from `GEN_CODE_MINIMIZE` in the compilation's preprocessor
   symbols (the runtime must be compiled with the same define, which a
   project-wide define gives), so no new attribute or option (1 day).
2. Emitter path: wrapper methods as static `int f(ObjectTranslator
   translator, IntPtr L, int top)` without the callback attribute, the
   translator lookup, `lua_gettop` and the per-member `try`/`catch`;
   registration with `CSharpWrapper` delegates (`Utils.RegisterFunc` and
   `EndObjectRegister` overloads exist under the define); enum wrappers as in
   `LuaEnumWrapGCM.tpl.txt` (2 to 3 days).
3. Tests: generator unit tests, `run_managed.py --source-gen
   --gen-code-minimize` (the 424 Lua tests and the surface diff), the editor
   suite's `sourcegen` mode with the define, and an IL2CPP player with this
   size run (2 days).

**Recommendation:** add the GCM mode to the source generator (owner decision
1 allowed it as "one more emitter path") rather than drop GCM: 450 to 470 KB
is 7.5% of xLua's APK cost in a configuration that binds the engine API
broadly, the run-time cost is a few nanoseconds per call on the host and not
measurable on the emulator, and the work is about a week. Keep it opt-in with
the same define, so projects that bind few types or count every nanosecond
are not affected. Not measured: iOS, WebGL, a phone, and the mode itself.

**Question for the owner:** add a GCM mode to the source generator (about one
week, before or after M11), or drop GCM at M11 as decision 1 planned?
*Answered by the owner's rule of 2026-10-17/18* (measure at game size, add GCM
if the gap is big): the gap is 446 to 473 KB, so the mode was added before M11
(next section).

### GCM mode as implemented (2026-10-18)

The owner's rule for the question above, applied: the measured gap is big, so
the source generator has a `GEN_CODE_MINIMIZE` mode. User-visible changes are
in [MIGRATING.md](../MIGRATING.md#source-generator-preview).

**Selection.** The mode follows `GEN_CODE_MINIMIZE` in the compilation's
preprocessor symbols (`Wrappers.GcmDefined`), the define legacy GCM and the
runtime already use; no attribute or option. What decides the code is xLua's
own mode: an xLua compiled with the define declares `XLua.CSharpWrapper`, and
its `Utils.RegisterFunc`, `BeginClassRegister` and `EndObjectRegister` then
take that delegate instead of `lua_CSFunction` (`Utils.cs:1397-1513`). When
the assembly's define and xLua's differ (a define in one assembly's
`csc.rsp`, a precompiled xLua), the new warning **XLSG013** says so and the
wrappers follow xLua (`Wrappers.RuntimeIsGcm`), the only mode that compiles.
In Unity the define is project-wide, so the two agree. The mode is part of the
model cache key (`MemberContext.Fingerprint`), so cached models of metadata
types never cross modes.

**Emitter path** (`Wrappers.cs`, the `Writer.Gcm` flag; the plain path is
unchanged byte for byte). As `LuaClassWrapGCM.tpl.txt` and
`LuaEnumWrapGCM.tpl.txt`:

- every member function (constructor, indexers, operators, methods, getters,
  setters, events, an enum's `__CastFrom`) is
  `static int _m_Foo(this ObjectTranslator translator, IntPtr L, int gen_param_count)`:
  no `[MonoPInvokeCallback]`, no `ObjectTranslatorPool.Instance.Find`, no
  `lua_gettop` (`gen_param_count` is the trampoline's top), no per-member
  `try`/`catch`. `CSharpWrapperCallerImpl` (`StaticLuaCallbacks.cs:147-160`)
  finds the translator and turns an exception into the same
  `c# exception:...` error;
- `__Register` registers `translator._m_Foo`, a `CSharpWrapper` closed over
  the translator, as the legacy template does; the registration itself
  (`Bindings.g.cs`) is the plain mode's;
- the functions are **private**, unlike legacy's `UnityEngineTransform_m_Foo`
  names: extension method lookup only takes accessible candidates, so the
  `_m_Foo` of one wrapper class is not a candidate in another's `__Register`
  (CS0121) and the short names stay. 2,000 BCL types compile in one assembly
  (`XLua.SourceGenerator.Tests --timing-gcm 2000`, 0 errors);
- an enum with more than 20 values keeps the `try`/`catch` around
  `TranslateToEnumToTop`, as the legacy GCM template does.

Delegate, interface and hotfix bridges, GCOptimize code, link.xml, the obfuz
rules and the manifest are the same in both modes, as legacy GCM uses the same
templates for them; the unit tests compare those files byte for byte.

**Size** (`size_report.py run --platforms android --configs
game-minimal,game-high` with the new `ng-5.5+pkg+sg+gcm+game`, release ARM64
APKs, Unity 6000.3.21f1, the package layout, Lua 5.5.1 from
`build/make_android_lua55.sh` (NDK r30) and C# at `4f114134`, all variants
rebuilt in one run; details in
[build/SIZE.md](../../build/SIZE.md#a-game-sized-configuration-2026-10-18)):

| stripping | legacy GCM | source generator | source generator GCM | SG GCM - legacy GCM | SG - SG GCM |
| --- | ---: | ---: | ---: | ---: | ---: |
| Minimal | +5,828,158 | +6,301,626 | +5,602,662 | -225,496 (-3.9%) | 698,964 |
| High | +5,625,970 | +6,073,266 | +5,377,678 | -248,292 (-4.4%) | 695,588 |

- **Against legacy GCM** the generator's GCM player is 225 to 248 KB smaller,
  as projected: the GCM saving applies, and the generator keeps less engine
  code (`libunity.so` 33 KB smaller; members preserved one by one) and less
  IL2CPP code (`libil2cpp.so` 136 to 163 KB compressed, 587 to 711 KB
  uncompressed). Its metadata is 40 to 44 KB smaller compressed.
- **Against the plain generator** the GCM mode saves 696 to 699 KB
  (`libil2cpp.so` 2.7 MB uncompressed), the same as legacy GCM saves against
  Generate Code (692 KB): reverse P/Invoke wrappers fall from 4,625 to 34 (the
  same 34 as legacy GCM; counted in `il2cppOutput/cpp`), the C++ of
  Assembly-CSharp from 38,176 KB to 24,628 KB (legacy GCM 24,828 KB; `du`, High), the stripped
  Assembly-CSharp (High) from 1,458,176 to 1,204,736 bytes (legacy GCM
  1,261,056). Metadata grows by 19 to 20 KB compressed (probably the two extra parameters per function; not traced).

**Call cost** (host, `Test/Native/benchmark.py` executables, Mono 6.14.1,
macOS arm64, Lua 5.5.1, all five modes built with the .NET SDK's csc except
`gencode-min` (mcs), ten interleaved rounds on a loaded machine, minimum of
the per-round medians, ns per call):

| case | legacy GCM (mcs) | legacy GCM (csc) | SG GCM | Generate Code (csc) | SG |
| --- | ---: | ---: | ---: | ---: | ---: |
| `static_call` | 59.3 | 59.1 | 59.3 | 48.5 | 49.2 |
| `instance_call` | 85.0 | 86.3 | 84.3 | 82.1 | 82.3 |
| `property_get` | 91.8 | 92.6 | 90.9 | 92.0 | 93.0 |
| `field_get` | 92.4 | 91.5 | 91.9 | 89.9 | 89.2 |
| `string_return` | 95.9 | 94.6 | 92.9 | 83.7 | 84.2 |
| `params_int` | 158.3 | 157.0 | 155.8 | 144.1 | 146.0 |
| `int_array_get` | 113.6 | 114.2 | 114.2 | 104.2 | 102.0 |

The generator's GCM costs what legacy GCM costs (within 2%); both are 0 to
12 ns slower per Lua-to-C# call than the plain modes, the trampoline, a table
lookup and a delegate call. C#-to-Lua calls and struct accessors do not
change (`interleaved.json` of the run lists every case).

**Tests:**

- generator unit tests (35, `Tools/build_source_generator.py --test`):
  `GcmWrappers` (surface and registration equal to the plain mode, one GCM
  function per plain callback, no boilerplate, the registration shapes),
  `GcmLeavesBridgesAndStructsAsTheyAre`, `Xlsg013GcmDefineDiffersFromXLua`
  (both directions), `GcmModelsAreCachedApart`;
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen
  --source-gen-fallback` on Lua 5.5.1 and LuaJIT 2.1: every pass 458 tests in
  both modes, among them the new `--source-gen --gen-code-minimize` pass
  (`XLUA_MANAGED_SOURCE_GEN_PASS_MINIMIZE`) whose surface, bridge, hotfix and
  struct diffs against legacy GCM pass, and whose launcher finds 107 GCM
  functions in `LuaTestObj`'s wrapper that fill 107 `CSharpWrapper` entries;
- `hotfix_check.py --define GEN_CODE_MINIMIZE` (the generator's hotfix
  bridges with the define: `HOTFIX_SOURCE_GEN_PASS`,
  `HOTFIX_SOURCE_GEN_MULTI_PASS`), `coverage_cs_dotnet.py`,
  `luacats_check.py --source-gen`;
- `editor_compile_check.py`: four new `*source-gen` configurations (Editor and
  Android IL2CPP player, plain and with the define) on the seven installed
  editors, 2021.3.45f2 to 6000.6.6f1: 42 of 42 with the existing
  `GEN_CODE_MINIMIZE` configurations;
- `asmdef_compile_check.py --source-gen --define GEN_CODE_MINIMIZE` with
  6000.3.21f1 and 2021.3.45f2 (47 GCM wrappers in Assembly-CSharp, xLua in its
  own assembly);

Not done: iOS, WebGL, a phone, the Unity editor suite (`editor_tests.py`) in
mode `sourcegen` with the define, and Medium stripping.

### M7 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md) (Tooling, Hotfix;
the asmdef layout; Source generator preview) and in the hotfix guide
([Hotfix_EN.md](../../Assets/XLua/Doc/Hotfix_EN.md), "Several hotfix
assemblies"). What landed:

| Part | Where |
|---|---|
| `HotfixBridges` per assembly: bridges and IntKey slots | `General/SourceGenerator/XLua.SourceGenerator/HotfixBridges.cs` |
| Bridge emitter on a signature model shared by delegates and hotfix | `Bridges.cs` `BridgeSignature` |
| `AssemblyBindings.HotfixSlots`, `HotfixDelegateBridge.SetSlot` | `Assets/XLua/Src/Binding.cs`, `DelegateBridge.cs` |
| Injector: local bridges, per-assembly IntKey, `Inline` and generic methods everywhere, `[Hotfix]` by name, `[assembly: Hotfix]` | `Assets/XLua/Src/Editor/Hotfix.cs` |
| Lua map loading | `xlua.util.auto_id_map` (`EmbeddedModules.cs`) |
| Hotfix bridge diff | `Test/Native/surface_diff.py` (`XLUA_HOTFIX_BRIDGE_DIFF_PASS`) |
| Fixture in two assemblies through the source generator | `Test/Native/hotfix_check.py` step 7, `HotfixFixtureSecondSG.cs` |
| Generator unit tests (24; `HotfixBridgesFollowTheLegacySelection` is new) | `XLua.SourceGenerator.Tests` |

How it follows section 6 (option B) and decision 8:

- **Bridges:** an assembly with `[Hotfix]` types (on the type, or
  `[assembly: Hotfix(typeof(T), flags)]` for its own types) gets
  `internal static class HotfixBridges` in `XLua.Generated.<Id>`. The methods
  are those `Generator.cs GenDelegateBridge` selects: declared methods with a
  body and the instance constructors, minus `Inline` types, the
  `IgnoreProperty`, `IgnoreNotPublic` and `IgnoreCompilerGenerated` filters
  (compiler-generated: implicitly declared members and auto-property
  accessors, which carry `[CompilerGenerated]` in metadata) and the
  `injectByGeneric` rule. They are grouped by `SignatureKey.Of(m,
  KeyPurpose.Hotfix, flags)` from M3. For the fixture the 11 signatures equal
  the legacy generator's (the bridge diff).
- **The injector** (`Hotfix.Init`) looks for `HotfixBridges` in the injected
  assembly first, then for the legacy `DelegatesGensBridge`. A bridge in the
  injected assembly is called directly (`call`, as section 6 says), one in
  another assembly through a delegate type the injector creates, as before.
- **IntKey per assembly:** the design proposed `ldsfld Slots; ldc.i4 id; ...`
  on an array registered with `HotfixSlots(DelegateBridge[] slots, string
  idMapName)`. A fixed array cannot grow when Lua sets an id, and the
  generator does not know the ids, so the slots are private to
  `HotfixBridges` instead, with `Get(int id)` (a bounds-checked array read,
  one static call at each method entry) and `Set(int id, DelegateBridge)`
  (grows the array under a lock). Bindings registers
  `b.HotfixSlots(HotfixBridges.Set, typeof(HotfixBridges))`; the class also
  serves the lookup by signature for delegate types the injector creates
  (`AdaptByDelegate`), without setting `Gen_Flag`. Ids number from 0 in each
  assembly. The process-wide list (`HotfixDelegateBridge.Set(id, f)`, and the
  native flag on iOS) stays for xLua's own assembly and "Generate Code"
  output.
- **Id maps:** the injector writes one map per assembly as before; an assembly
  with slots puts `["<assembly>"] = "<name>"` in each type's entry (no method
  name has a `<`). The Editor's merge into `hotfix_id_map` copies entries
  whole, so one module still serves every assembly, and
  `xlua.util.auto_id_map()` calls `HotfixDelegateBridge.SetSlot(assembly, id,
  f)` for such entries. Decision 8 says "loading every map in `util.lua`":
  the merged module already holds every map, so `util.lua` keeps one
  `require`.
- **`SetSlot`, not an overload of `Set`:** with two `Set` overloads the
  existing `CS.XLua.HotfixDelegateBridge.Set(id, f)` from Lua failed with
  "invalid arguments to Set" in the legacy hotfix check. A separate name keeps
  the legacy call as it was.
- **`Inline` and generic methods across assemblies:** `injectGenericMethod`
  returned before injecting outside xLua's assembly. The path calls only
  `DelegateBridge`'s public `InvokeSessionStart`, `InParam<T>`, `Invoke`,
  `OutParam<T>` and `InvokeSessionEnd*`, which other assemblies may call; with
  the early return removed, the fixture's second assembly injects and patches
  an `Inline` type and a generic method. IntKey there uses the slots, the
  global list only in xLua's assembly, and field mode otherwise (with the
  warning, which now also covers `Inline` IntKey types).
- **Configuration fixes the hotfix agent found:**
  - `[Hotfix]` on a type outside xLua's assembly was ignored: the injector
    compared the attribute type with xLua's `TypeDefinition` by reference.
    It compares full names now.
  - `[assembly: Hotfix(typeof(T), flags)]` is read by the injector (for types
    of the injected assembly) and by the Editor when it picks the assemblies
    to inject (`HotfixConfig.GetHotfixAssembly`).
  - A configuration list in Assembly-CSharp that names types of another
    assembly: not changed (question 2 below).
- **Host registration:** a unit of an assembly loaded after the first
  `LuaEnv` (hosts without Unity) is registered when one of its slots is set
  (`BindingRegistry.SetHotfixSlot` scans again); in Unity,
  `RuntimeInitializeOnLoadMethod` registers every unit.
- **With what main gained meanwhile** (merged at `7ff004a`):
  - The injector's search of the script assemblies next to the injected one
    for legacy bridges (the "can not find delegate" fix for a game asmdef)
    stays, and runs only when neither the injected assembly's
    `HotfixBridges` nor the legacy bridges of xLua's and the injected
    assembly were found.
  - `auto_id_map` looks the type up with `xlua.typename` (M8) and then sets
    the ids with `SetSlot` or `Set` as above.
  - Obfuscation: the injected code reaches `HotfixBridges.Get` and the
    bridges by IL reference, so renaming them after injection keeps hotfix
    working. The lookup by name (`BindingRegistry.BridgeMethods`, the
    `__Gen_Delegate_Imp` prefix) also lists the `HotfixBridges` methods, for
    delegate types the injector creates (none when the bridges are in the
    injected assembly) and for an unconfigured delegate type with the same
    parameters. After renaming, such a delegate type no longer finds a
    hotfix bridge there and falls back to the other bridges, as M8 leaves
    the legacy scan **[inferred from the code, not run]**.
- **Minimal preset:** `xlua.util` is left out, so there is no
  `auto_id_map()`. Hotfix_EN.md says to define `XLUA_UTIL` or to set the ids
  from the map with `Set` and `SetSlot`; the fixture does the latter under
  `XLUA_NO_UTIL`, and `hotfix_check.py --define XLUA_PRESET_MINIMAL` checks it.

Tested on macOS arm64, Mono 6.14.1, .NET SDK 10.0.401, at `3758f90` plus this
work (423 Lua tests), before the merge with `main`:

| Check | Result |
|---|---|
| `hotfix_check.py`, Lua 5.5.1, LuaJIT 2.1, and Lua 5.5.1 with the seven hardening defines | all steps pass on all three: `XLUA_HOTFIX_BRIDGE_DIFF_PASS: 11 hotfix bridge signatures equal`, `HOTFIX_SOURCE_GEN_PASS` (IntKey through the assembly's own slots, 103 fixture checks), `HOTFIX_SOURCE_GEN_MULTI_PASS` (second assembly), and the legacy steps as before (PDB, MDB, none, unreadable, bare paths, two assemblies) |
| `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen --source-gen-fallback`, Lua 5.5.1 and LuaJIT 2.1 | pass, 423 tests in each mode; surface, bridge and struct diffs unchanged from M5 |
| `run_managed.py` with `THREAD_SAFE` | pass |
| `run_managed.py` with the seven hardening defines | first run: the reflection pass hung (100% CPU for 28 minutes) in `CaseLuaOptionsTimeLimit` (`while true do end` under a 50 ms `TimeLimit`), with another agent's Unity run and a hotfix check on the machine; that test and the watchdog predate M7. One rerun passed every mode |
| `hotfix_check.py --define XLUA_PRESET_MINIMAL` | fails in the existing two-assembly step: `module 'xlua.util' not found`, because the minimal preset leaves out `xlua.util` and the step calls `auto_id_map()`. The same step is on `main`; not caused by M7 |
| Generator unit tests | 24 pass |
| `luacats_check.py` | pass |
| `editor_compile_check.py` | 7 editors x 55 configurations: 378 ok, 7 skip |
| `asmdef_compile_check.py`, 2021.3 and 6000.6 with `HOTFIX_ENABLE`, with and without the source generator | pass |

After the merge with `main` at `7ff004a` (424 Lua tests), with the minimal
preset's id setting above, every run passes:

| Check | Result |
|---|---|
| `build_source_generator.py --test` | 26 tests pass |
| `hotfix_check.py`, Lua 5.5.1, LuaJIT 2.1, and Lua 5.5.1 with `XLUA_PRESET_MINIMAL` | `HOTFIX_CHECK_PASS` on all three, with main's asmdef layout case (`HOTFIX_ASMDEF_LAYOUT_PASS`), the obfuscation attribute checks of the fixture, `XLUA_HOTFIX_BRIDGE_DIFF_PASS: 11 hotfix bridge signatures equal`, `HOTFIX_SOURCE_GEN_PASS` and `HOTFIX_SOURCE_GEN_MULTI_PASS` |
| `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen --source-gen-fallback`, Lua 5.5.1 and LuaJIT 2.1 | 424 tests in every mode; surface, bridge and struct diffs equal |
| `obfuscation_check.py`, Lua 5.5.1 and LuaJIT 2.1 | `XLUA_OBFUSCATION_CHECK_PASS` (plain 17 of 17; rules and attributes failing only as expected) |
| `coverage_cs_dotnet.py` (.NET SDK 10.0.401) | 424 tests in each of two modes for reflection, gencode and gencode-minimize; runtime branches 78.6% combined |
| `luacats_check.py --download-luals` | pass |
| `editor_compile_check.py` | 7 editors x 55 configurations: 378 ok, 7 skip |
| `asmdef_compile_check.py`, 2021.3 and 6000.6 with `HOTFIX_ENABLE`, with and without the source generator | pass |

The hardened `run_managed.py` was not run again after the merge; the
`CaseLuaOptionsTimeLimit` hang is left to a separate investigation.

Not run then: any Unity editor (another agent held the Unity slot) or
player. On 2026-10-18, after core M6, steps 1 and 2 ran: `hotfix_intkey.py
--asmdef --source-gen` and `--package --source-gen` (the IntKey types
configured with `[assembly: Hotfix(typeof(T), HotfixFlag.IntKey)]` in their
own assemblies, `Game.dll` referencing XLua and XLua.Core) passed on
2021.3.45f2 and 6000.3.26f1 with no fallback warning, maps with
`["<assembly>"]` and ids from 0, and patch and restore in play mode; the
macOS Mono hotfix player with the generator (`hotfix_player.py
--source-gen`) passed on 2021.3.45f2, 6000.3.26f1 and 6000.6.6f1, and the
editor suite in mode `sourcegen` with `--hotfix` passed (2021.3, 6000.3 and
6000.6 at M6, 2022.3 and 6000.0 after core M6).
Not run: the `Inline` type and generic method of step 1, and step 3. The
Unity steps as planned:

1. **Editor hotfix run in the asmdef layout with the generator:**
   `Test/UnityMatrix/hotfix_intkey.py` with `--asmdef` added to its
   `prepare.py` call and `build_source_generator.py --install
   <project>/Assets/XLua/Src/Analyzers`, and with the fixture's IntKey types
   configured by `[Hotfix(HotfixFlag.IntKey)]` on the types instead of the
   Editor list. Expect: no "IntKey falls back to field mode" warning for
   Game.dll, `hotfix_id_map_Game` with `["<assembly>"] = "Game"` and ids from
   0, the merged `hotfix_id_map`, and the play-mode patch and restore of both
   types. Then once with a type marked `Inline` and a generic method in
   Game.dll.
2. **`prepare.py --hotfix --asmdef` editor suite** (`editor_tests.py`), which
   the M1 notes list.
3. **IL2CPP:** an Android player with a hotfixed IntKey type in an asmdef
   assembly: the injected `HotfixBridges.Get` calls and the bridges survive
   High stripping (the injected code references them, so the linker keeps
   them **[inferred]**), and a patch loaded at run time applies.

**Questions for the owner (M7):**

1. **Default layout.** With the generator installed, the asmdef layout no
   longer costs hotfix anything on the host checks. The Unity runs above are
   still open; after them, should the asmdefs become the default (M1 kept
   them opt-in until M7)?
2. **Configuration lists for other assemblies.** The guide says a `[Hotfix]`
   list in Assembly-CSharp that names `Game.Shop` leaves it uninjected
   (the injector reads the injected assembly's lists and those the Editor
   collects in `hotfix_cfg_in_editor.data`). Reading the Editor collection, I
   could not find why Assembly-CSharp's lists would be missing from it
   (`HotfixConfig.GetConfig` over every project assembly, but only for static
   classes, while `GetHotfixAssembly` also reads lists in non-static
   classes, so a list in a non-static class picks the assembly and then
   injects nothing). Should the lists keep working across assemblies (and be
   fixed after a Unity reproduction), or should cross-assembly configuration
   move to `[Hotfix]` on the type or `[assembly: Hotfix]` only, which M7 makes
   work everywhere?
3. **`SetSlot` naming.** Patch scripts that set IntKey ids by hand need the
   assembly now (`SetSlot('Game', id, f)`) for source-generated assemblies;
   `auto_id_map()` hides it. Acceptable, or should `Set(id, f)` also accept
   ids of a single source-generated assembly when only one is registered?

### M8 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#obfuscation-name-table-and-obfuz).
What landed:

| Part | Where |
|---|---|
| Reverse name table (name to type, generic definitions) | `Assets/XLua/Src/Binding.cs` `BindingRegistry.TypeByName`, `LuaNameOf` |
| Lookups through it: `FindType`, `getPathOfType`, metatable keys | `ObjectTranslator.cs` `FindType`; `Utils.cs` `getPathOfType`, `pathOfName`, `MetatableKey`, `LuaTypeName` |
| `xlua.typename`, and the IntKey map lookup through it | `StaticLuaCallbacks.cs` `TypeName`; `EmbeddedModules.cs` (`xlua.util` `auto_id_map`) |
| obfuz rule fragment per assembly | `General/SourceGenerator/XLua.SourceGenerator/ObfuzRules.cs` (`XLua.ObfuzRules.g.cs`) |
| `[Obfuscation(Exclude = true)]` on `Bindings.Register` | `Emitter.cs` |
| `[Obfuscation(Exclude = true)]` on injected `__Hotfix` fields and the flag type | `Assets/XLua/Src/Editor/Hotfix.cs` `ExcludeFromObfuscation` |
| obfuz adapter: the rule file, the menu, the build check | `Assets/XLua/Src/Editor/ObfuzRules.cs` (no Unity dependency), `ObfuzAdapter.cs` |
| Host test with renamed types | `Test/Native/obfuscation_check.py`, `ObfuscationFixture.cs`, `ObfuscationRenamer.cs`, `ObfuzRulesTool.cs` |
| Tests of the parts | `CaseTypeName` (`luaCallCsCoverage.lua`), `HotfixFixture.cs` `CheckObfuscationExclude`, generator test `ObfuzRulesAndObfuscationExclude` (24 tests) |

How it follows sections 5.6 and 8 (item 4) and the decision that the
compile-time name table is the obfuscation-safe path:

- **`CS.A.B` and `FindType`:** the table is asked first, then
  `assembly.GetType` for types without an entry. The reverse table keeps the
  first type registered under a name. A generic type also enters its
  definition's name (`Game.Box`1` from `Game.Box`1[System.Int32]`), which
  `xlua.import_generic_type` looks up, so `CS.Game.Box(CS.System.Int32)`
  works for a renamed definition that was configured only closed.
- **`getPathOfType`:** a type with an entry is placed under `CS` at the path
  of its entry. The namespace ends at the last `.` before the first `+` or
  `[`, and nested names split at `+`, which is what the code computed from
  `Type.Namespace` and `Type.ToString()` before; unrenamed types get the same
  path.
- **Metatable keys:** `Utils.MetatableKey` gives the entry, or `Type.FullName`
  as before. The key is fixed per type on first use (a locked dictionary),
  because registration may come after a `LuaEnv` loaded the type (section
  5.1, late registration), and two keys for one type would split its
  metatable. A name that another type registered first (one name in two
  assemblies, or `Type.ToString()` of two generic instances whose arguments
  have the same name) is not used, so two types never share a key or a path.
  With generated bindings, generic types change key from `FullName`
  (assembly-qualified arguments) to `Type.ToString()`; nothing in the
  runtime reads keys by name except these sites.
- **`xlua.typename(t)`** (new) takes a class table, a `System.Type` or a
  name and returns the entry or `Type.ToString()`. `xlua.util`'s
  `auto_id_map` uses it instead of `typeof(cs):ToString()`, so the IntKey map,
  keyed by original names, matches a renamed type. Milestone M7 changes the
  map loading; the lookup key is the only line M8 changed there.
- **Delegate bridges by prefix** (`BindingRegistry.BridgeMethods`, the
  `__Gen_Delegate_Imp` scan of legacy `DelegatesGensBridge`): left as it is.
  The source generator registers delegates by `Type` (M3) and needs no scan;
  under obfuz the adapter keeps `XLua.*` names (below), which covers the
  legacy bridge class.
- **`[Obfuscation(Exclude)]` emission:**
  - `Bindings.Register`: emitted, because hosts without Unity find it by name
    (`EnsureRegistered`). obfuz keeps `[RuntimeInitializeOnLoadMethod]`
    methods and their classes by itself (`UnityRenamePolicy`).
  - Injected `__Hotfix*` fields and `__XLUA_GEN.__XLUA_GEN_FLAG`: emitted by
    the injector, with the attribute type taken from the module's core
    library. The flag type also keeps [xLua#526](https://github.com/Tencent/xLua/issues/526)
    (double injection after renaming) closed for attribute-driven tools.
  - `[ReflectionUse]` types, `[DoNotGen]` and fallback members: **not
    emitted.** A source generator only adds files; it cannot put an attribute
    on a member declared in the user's file. Partial types would allow it for
    types only, and only when the user declares them `partial`. The obfuz
    rules cover these names; for other tools MIGRATING.md lists what to mark.
    Writing the attribute with Cecil before the obfuscator runs is possible
    (as the injector does) but needs Cecil without `HOTFIX_ENABLE`, the same
    cost as M5's question 2.
  - The XLua assembly itself (`[assembly: Obfuscation(Exclude = true,
    ApplyToMembers = true)]`, obfuscators.md recommendation 2): not emitted.
    In the default layout xLua compiles into Assembly-CSharp, where the
    attribute would exclude the whole game assembly; the asmdef layout makes
    `XLua.dll` an assembly that projects do not usually list for obfuscation.
- **The obfuz rule file.** obfuz 4.0.0 (`6912a92`, the current tag on
  2026-10-18) does not read `[Obfuscation]`; it reads rule files
  (`ConfigurableRenamePolicy.cs`, manual "Symbol Obfuscation"). The generator
  writes a fragment per assembly, `XLua.ObfuzRules.g.cs`, compiled only with
  `UNITY_EDITOR` or `XLUA_GENERAL` like the link.xml fragment:
  - `[ReflectionUse]` types: `obName="0" applyToMembers="*"`;
  - wrapped types with `[DoNotGen]` or fallback members: those members by
    name with `obName="0"`, and the type's own name (`obName="0"
    applyToNestedTypes="0"`), because the link.xml fragment, written at link
    time after obfuz ran, names the type by its original name;
  - enums with more than 20 values: `<field name="*" obName="0"/>`, because
    their wrapper reads value names by reflection (`Utils.RegisterEnumType`,
    `Enum.Parse` in `__CastFrom`); smaller enums use literals.

  The Editor adapter (`ObfuzRules.Build`) merges the fragments of the loaded
  assemblies and keeps only the assemblies in obfuz's `AssemblySettings`
  (obfuz stops with "unknown assembly name" otherwise). It adds
  `<type name="XLua.*" obName="0" applyToMembers="*"/>` for an obfuscated
  assembly that holds xLua code (the default layout, or generated code),
  since xLua reaches its own members by name (CodeEmit, `CS.XLua.*` from Lua,
  the legacy bridge scan), and, with `HOTFIX_ENABLE`, `__Hotfix*` and
  `_c__Hotfix*` fields in every type and the flag type. `ObfuzAdapter`
  (Editor only) reads obfuz's settings through reflection (xLua does not
  reference obfuz), writes `Assets/Obfuz/xlua-ng-rules.xml` from **XLua >
  Obfuscation > Write obfuz Rule File**, rewrites it before each build while
  obfuz lists it, warns when obfuz is enabled and does not list it, and warns
  with `HOTFIX_ENABLE` when obfuz runs at or before the injector's order 0.
- **The `IXLuaNameMapper` and `IObfuscatorAdapter` interfaces** of
  presets.md and the backlog are not added. The name table answers "original
  name of a type" for every configured type without a runtime hook, and the
  one supported obfuscator has its adapter as a class. An unconfigured type
  reached by reflection is still found only by its current name; obfuz's own
  `ObfuscationTypeMapper` could answer for it (question 2 below).
- **Section 8, item 4** asks for an obfuz build in Unity and "a cheap host
  check: an Obfuscar rename pass". The host check uses a Cecil renamer of its
  own instead of Obfuscar: Obfuscar is a .NET tool with its own build, not
  pinned in the repository, and the check needs both rule semantics, obfuz's
  rule file and the attribute. `ObfuscationRenamer.cs` applies obfuz's rules
  as `ConfigurableRenamePolicy.BuildTypeRuleResult` does (type rules by full
  name or wildcard, `obName`, `applyToMembers`, `applyToNestedTypes`, field,
  method, property and event rules, last rule wins; it refuses `modifier`,
  `classType`, `inherit` and `hasCustomAttributes`, which xLua's rules do not
  use), and keeps what obfuz's `SystemRenamePolicy` keeps. It does not rename
  virtual methods (obfuz renames override groups together) or parameters.

Tested on macOS arm64, Mono 6.14.1, .NET SDK 10.0.401, at `main` `3758f90`
plus this work (424 Lua tests), with Lua 5.5.1 and with LuaJIT 2.1:

- `build_source_generator.py --test`: 24 of 24;
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen
  --source-gen-fallback`, with both plugins: every mode 424 of 424; surface
  diff (72 types, 662 members, 13 extension methods), bridge diff and struct
  diff equal;
- `obfuscation_check.py`, with both plugins: plain 17 of 17; renamed with the
  rules (42 symbols renamed, `Game.Shop` is `$N0.$32`), 16 pass and the
  unconfigured type fails as expected; renamed with the attribute only (153
  symbols, the generated classes too), 12 pass and the expected 5 fail. The
  same renamed assembly against the runtime before this milestone (merge
  `44b7b8d`) fails 12 of 17 checks (`No such type: Game.Shop`);
- `hotfix_check.py` (the fixture checks the attribute on the injected fields
  and the flag type; `auto_id_map` finds both assemblies through
  `xlua.typename`); `luacats_check.py --download-luals`;
- `editor_compile_check.py`: 7 editors x 55 configurations, 378 ok, 7
  skipped (`ObfuzRules.cs` and `ObfuzAdapter.cs` compile in every one);
- `asmdef_compile_check.py --source-gen` against 2021.3.45f2: the four
  assemblies compile, `XLua.Editor` with the two new files.

Not run: any Unity editor or player, and obfuz itself. The Unity part of
section 8, item 4, for an agent with a Unity slot (one editor at a time; not
while a phone benchmark holds adb):

1. **obfuz in the Editor** (2021.3.45f2, Mono backend first):
   1. `Test/UnityMatrix/prepare.py --asmdef --editor-tools` into an empty
      folder, the generator installed (`build_source_generator.py --install
      <project>/Assets/XLua/Src/Analyzers`), and obfuz 4.0.0 added to
      `Packages/manifest.json` as `"com.code-philosophy.obfuz":
      "https://github.com/focus-creative-games/obfuz.git#v4.0.0"` (the
      package is the repository root; obfuz manual, "Installation"; tag
      `v4.0.0` is commit `6912a92`).
   2. A game asmdef `Game` (or Assembly-CSharp) with the fixture's types:
      `Test/Native/ObfuscationFixture.cs` without its `Main`, and a
      MonoBehaviour that runs the same Lua checks in `Start` and logs
      `XLUA_OBFUSCATION_UNITY_PASS` or the failing names.
   3. In `ProjectSettings/Obfuz.asset`: `assemblySettings.assembliesToObfuscate`
      = the game assembly, Symbol Obfuscation enabled, the default order
      (10000). Run **XLua > Obfuscation > Write obfuz Rule File**, check the
      warning about the missing rule file, then add
      `Assets/Obfuz/xlua-ng-rules.xml` to Symbol Obfuscation > Rule Files and
      run it again (no warning).
   4. Build a Mono player (`-executeMethod` with a build script, batch mode).
      Check that `Assets/Obfuz/SymbolObfus/symbol-mapping.xml` renames
      `Game.Shop` and keeps `Game.Reflected`, `Game.Slow.Secret`,
      `Game.Locked.X` and the `Game.Big` value names, and that the player log
      has `XLUA_OBFUSCATION_UNITY_PASS` with only `unbound` failing.
   5. Repeat with IL2CPP (Android ARM64, High stripping): the merged
      `Temp/XLuaSourceGen/link.xml` keeps `Game.Slow.Secret` under its
      original type name, which obfuz did not rename; run the APK on the
      emulator.
   6. With `HOTFIX_ENABLE` and a `[Hotfix]` IntKey type in the game assembly:
      the patch through `xlua.hotfix` after obfuscation works (the id map has
      original names, `xlua.typename` gives them), and a field-mode type keeps
      its `__Hotfix` fields (symbol mapping).
   7. obfuz's `[ReflectionCompatibilityDetector]` output: list what it reports
      for xLua's own code and the generated code.
2. **The adapter against obfuz's settings:** with obfuz disabled
   (`buildPipelineSettings.enable = false`) a build writes nothing and warns
   nothing; with `obfuscationProcessCallbackOrder` set to 0 and
   `HOTFIX_ENABLE`, the order warning appears.

**Questions for the owner (M8):**

1. **Type names kept for link.xml.** A wrapped type with `[DoNotGen]` or
   fallback members keeps its name under obfuz, because the link.xml that
   UnityLinker reads names it by its original name and is written after obfuz
   ran. The alternative is to translate the merged link.xml through obfuz's
   `symbol-mapping.xml` in `SourceGenLinkXml`, as obfuz's own
   `LinkXmlProcess` does for the project's link.xml files: more hiding, but
   xLua would then parse obfuz's mapping format, which can change between
   obfuz versions. Keep the names, or translate?
2. **Unconfigured types under obfuscation.** A type that Lua reaches only by
   reflection and that no attribute names is not in the name table, so
   `CS.Game.Unbound` fails after renaming. Options: document it (now: configure
   the type with `[ReflectionUse]`, which adds it to the table), or add a
   runtime hook (`IXLuaNameMapper`, presets.md) that obfuz's
   `ObfuscationTypeMapper` can fill, as obfuz's xLua sample does. The hook
   costs a call per unresolved name and a public API.
3. **Generic metatable keys in the `legacy` preset.** With generated bindings,
   a generic type's metatable key changes from `Type.FullName` to
   `Type.ToString()`. Nothing in xLua reads keys by name, but a script that
   indexes `debug.getregistry()` by the old key would miss. Accept, or key by
   `FullName` in `legacy` and use the table name only for renamed types?

### M9 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#converting-configuration-lists);
the user guide is [Converting configuration lists](../../Assets/XLua/Doc/Migrating_Configuration.md).
What landed:

| Part | Where |
|---|---|
| The conversion: evaluation, routing, C# spelling, report | `Assets/XLua/Src/Editor/ConfigMigration.cs` `Generator.MigrateConfiguration` |
| **XLua > Migrate Configuration to Attributes**, `XLuaBindings.asmdef` | the same file, `ConfigMigrationMenu` |
| `XLuaGenerate --migrate` | `General/Src/XLuaGenerate.cs` |
| The suite's lists converted by the tool, checked on every `--source-gen` run | `Test/Native/XLuaUnitTest.XLuaConfig.cs` (was the hand-written `SourceGenConfig.cs`), `run_managed.py` `migrated_config` |
| The examples converted | `Assets/XLua/Examples/XLuaConfig.cs` |
| Surface diffs of the examples, a computed `ExampleConfig` and edge cases | `Test/Native/migration_check.py`, `Test/Native/MigrationSample/` |
| Manifest fix for closed generic types (generator unit test extended) | `General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs` `Signature` |

How it follows section 4 ("Migration tool") and the decisions:

- **Evaluation.** The tool runs `GetGenConfig`, so it sees the set Generate
  Code uses, with its filters, and reads the lists again for their names and
  order (MergeCfg's member types: `IEnumerable<Type>` or `Type`,
  `List<List<string>>`, `Func<MemberInfo, bool>`, `Dictionary<Type,
  List<string>>`; Hotfix lists as `HotfixConfig.mergeConfig` filters them).
  An entry that `GetGenConfig` drops is reported with the reason Generate Code
  drops it, not written. Two exceptions: `CSharpCallLua` types that Generate
  Code drops only because its compiler cannot call their `ref readonly`
  members are written (the generator decides that from its own compiler), and
  `XLua.SysGenConfig` is skipped (its assembly form is in `GenAttributes.cs`
  since M4).
- **Order.** Section 4 asked for a sorted list. The tool keeps the order of the
  lists (first appearance wins), grouped by list with a comment, because the
  order of extension classes breaks overload ties (M2 difference 7), and so a
  reader finds each entry's list.
- **BlackList lists and filters:** `typeof` only for a member of a wrapped
  type of the project's own assemblies that exists in this build, with
  `typeof` parameter types; otherwise the by-name form (engine, .NET, package
  and precompiled types, types that are not wrapped, not found or ambiguous,
  members missing on this platform, by-ref parameter types such as
  `System.Int32&`). The by-name form gives at most info XLSG003, so an entry
  for a member that exists only in some Unity versions or on some build
  targets does not warn elsewhere. The first version also wrote `typeof` for
  engine types; `GameObject.networkView`, which Unity 6000.6 removed, then
  gave warning XLSG003 in `asmdef_compile_check.py --source-gen` on 6000.6.
  `DoNotGen` and `AdditionalProperties` have no by-name form; the report
  names their entries for types the project does not own.
- **Filters** run on the members `getClassInfo`, `GenEnumWrap` and the
  extension map pass to them (public declared constructors, methods including
  accessors, properties, fields and events of every wrapped type). Matches that
  are left out anyway (`[BlackList]` on the member, `[Obsolete]`, a converted
  list entry, a pointer or ref struct in the signature) are counted, not
  written; for Unity's `Span<T>` filters (`XLuaUnityDefaultConfig.cs`,
  `ExampleGenConfig.GenericTypeFilter`) that is every match. An exception from
  a filter is reported and the member taken as not matching; Generate Code
  stops on it.
- **Where the attributes go** (decision 4): a type of a project assembly that
  references XLua goes into that assembly, any other type into the bindings
  host: `XLuaBindings`, an asmdef the Editor creates under
  `Assets/XLuaConfig/XLuaBindings/` (references `XLua` and the script
  assemblies of the types it configures, auto-referenced so that
  Assembly-CSharp sees its `GCOptimize` entries), or Assembly-CSharp when xLua
  compiles into Assembly-CSharp. A closed generic type goes where its
  arguments' project assembly is; Editor-only engine types into
  Assembly-CSharp-Editor; Hotfix types into the assembly that defines them, or
  the report. Added to the design: a list-configured type extended by an
  extension class of another assembly moves to that assembly, since the
  generator merges extension methods only within one compilation; and
  attributes on the types of precompiled DLLs are written into the bindings
  host (no generator compiles them). Member entries (`BlackList`, `DoNotGen`)
  go where their type is wrapped, `AdditionalProperties` where its
  `GCOptimize` entry is.
- **Re-running.** Each written file names the lists it converts
  (`// xlua-migrated-list:` lines). The Editor rewrites its own files and
  leaves out lists that another `XLuaConfig.cs` already converts, so a project
  that contains xLua's converted examples gets no duplicates.
- **The lists stay.** Generate Code, the `legacy` default, reads only the
  lists, and the Editor's reflection wrappers read the `BlackList` lists
  (`BlackListRules`); the examples and the suite therefore carry both forms,
  kept equal by `migration_check.py` and `run_managed.py`. The tool does not
  edit user code, and it does not put attributes on the user's types (section
  4 left that open): assembly attributes keep one file per assembly that the
  tool owns.
- **Report** (section 4's list): custom generators (`[GenCodeMenu]`),
  `[GenPath]`, `GEN_CODE_MINIMIZE` and `GENERIC_SHARING` when defined, types
  without a `typeof` spelling (private nested, compiler-generated), lists that
  could not be evaluated, plus what to check by hand: computed lists and
  filters are snapshots, `#if` branches in the configuration files (the
  conditions are quoted), Hotfix lists that the injector still reads, and
  extension classes left in another assembly than the type they extend.
- **XLuaGenerate** answers `Assembly.Load` of an assembly on its command line
  with the loaded one (`ExampleConfig.cs` loads Assembly-CSharp by name, which
  gave a second copy with other `Type` objects), and loads what the given
  assemblies reference before reading the configuration. Without that, a list
  computed from `AppDomain.CurrentDomain.GetAssemblies()` saw a different set
  of assemblies on each evaluation outside Unity.
- **Not done:** the examples' lists and `Assets/XLua/Editor` (`ExampleConfig`,
  `XLuaUnityDefaultConfig`) stay; the second is converted by a project's own
  run of the tool, since it ships in every install (question 3). Hotfix:
  converted, but the injector reads the assembly form only after M7.

Findings about the generator, not caused by the conversion (the surface diffs
of `migration_check.py` found them):

1. **Closed generic types in the manifest** (fixed): it listed the members of
   the definition (`List`1[T]::Add(T)`) instead of the wrapped type's. The
   suite has no closed generic `[LuaCallCSharp]` type, so M2 did not see it.
2. **Overload order of .NET types in Unity.** Overloads of equal cost keep
   declaration order in both generators, but the legacy generator reflects on
   the Editor's runtime assemblies and the source generator reads the
   reference assemblies the scripts compile against, whose member order
   differs: with Unity 2021.3's `unity-4.8-api`, `List<int>`'s constructors
   come as `()`, `(IEnumerable<int>)`, `(int)` instead of `()`, `(int)`,
   `(IEnumerable<int>)` (`migration_check.py --bcl unity`). The default check
   compiles against Mono's runtime assemblies, which the legacy generator
   reflects on there, and is equal. It matters only when a Lua call matches
   several overloads (question 2).
3. **`[Out]` parameters that are not `out`.** The legacy `OverloadCosting`
   does not count a parameter with the metadata out flag (`ParameterInfo.IsOut`),
   such as Unity's `TrailRenderer.AddPositions([Out] NativeArray<Vector3>)`.
   Roslyn's public API does not expose that flag for metadata parameters
   **[inferred: `IsMetadataOut` is internal to Roslyn]**, so the generator
   counts it and keeps declaration order: the one allowed difference of the
   sample's 287 types (question 2).
4. Array types in `LuaCallCSharp` (M2 difference 4), as expected.

Tested on macOS arm64, Mono 6.14.1, .NET SDK 10.0.401, with `main` merged at
`6b9df53` (M7 and M10; 424 Lua tests), with Lua 5.5.1 and LuaJIT 2.1:

- `build_source_generator.py --test`: 28 of 28 (the closed generic check fails
  without the fix);
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen
  --source-gen-fallback`, with both plugins: every mode 424 of 424; the tool
  reproduces `XLuaUnitTest.XLuaConfig.cs` (16 attributes from 5 lists; the
  hand-written file had 15, the filter also matches the `FilteredReflected`
  overload of `HiddenExtension`); surface diff (72 types, 662 members, 13
  extension methods), bridge diff (32 delegate types on 26 bridges, 8 interface
  bridges) and struct diff (19 types) equal, as with the hand-written file;
  `coverage_cs_dotnet.py`: the three .NET modes 424 of 424;
- `migration_check.py` against Unity 2021.3.45f2's DLLs:
  - `examples`: 59 attributes from 7 lists, equal to `XLuaConfig.cs`; 47
    types, 843 members, 15 delegate types on 14 bridges, 4 interface bridges,
    14 GCOptimize types and 18 hotfix bridge signatures equal;
  - `sample`: 350 attributes from 9 lists (264 `LuaCallCSharp` from the LINQ
    query, 63 `CSharpCallLua` from two computed lists, 13 Hotfix, 6 entries of
    the `Dictionary` filter, `PackAsTable` and `AdditionalProperties` for
    `Rect`); 287 types and 4,050 members, 74 delegate types on 70 bridges, 27
    hotfix bridge signatures and 15 GCOptimize types equal but the one allowed
    order (finding 3);
  - `edge`: 21 attributes in two assemblies, every expected report line, 53
    types and 20 hotfix bridge signatures equal but `int[]` (finding 4);
  - the legacy side runs `XLuaGenerate --hotfix-assemblies` (new): the given
    assemblies' `[Hotfix]` lists count, as `HotfixConfig.GetHotfixAssembly`
    makes them count in the Editor with `HOTFIX_ENABLE`, so the hotfix bridge
    diff of M7 covers the converted Hotfix lists;
- `editor_compile_check.py`: 7 editors x 55 configurations, 378 ok, 7
  skipped (the first run found a CS0104 in the menu code, fixed; on the
  merged tree one configuration hit a crash of Mono's csc, `exited with -6`,
  and passed when run again alone);
- `asmdef_compile_check.py --source-gen` against 2021.3.45f2 (the examples'
  `XLuaConfig.cs` compiles with the generator in Assembly-CSharp, no XLSG
  warning);
- `hotfix_check.py`; `luacats_check.py --source-gen --download-luals`. After
  the merge with M10 its manifest part lost every member of `List<int>`:
  M10's reader mapped reflected members of a closed generic type to the
  definition (`List`1[T]::Add(T)`) to match the manifest, which this
  milestone changed to the wrapped form (finding 1). The reader
  (`LuaCATSGenerator.cs` `ManifestSignature`) now uses the wrapped form too;
  the expected files are unchanged;
- `docs/source/sync.mjs --check`.

The LuaJIT plugin was the one built for M8 (`sourcegen-m8-2026-10-18`); no
native source changed since.

After the by-name change for types the project does not own (at `main`
`ea25dc0`, 427 Lua tests): `migration_check.py` (examples: 8 BlackList
entries of engine types now by name; edge: `GameObject.networkView` by name,
`MigrationEdge.Plain` entries by `typeof`, both checked in the written file),
`run_managed.py` in every mode with both plugins (427 of 427;
`XLuaUnitTest.XLuaConfig.cs` unchanged, its BlackList types are the suite's
own), `asmdef_compile_check.py --source-gen --hotfix` against 6000.6.6f1 and
2021.3.45f2 (on 6000.6 `networkView` is now info XLSG003),
`editor_compile_check.py` (378 ok, 7 skipped) and `luacats_check.py
--source-gen --download-luals`.

Not run: any Unity editor. The Unity part, for an agent with a Unity slot (one
editor at a time; not while a phone benchmark holds adb):

1. **The menu in the default layout** (2021.3.45f2): a project from
   `Test/UnityMatrix/prepare.py --editor-tools` with the examples; run
   `CSObjectWrapEditor.ConfigMigrationMenu.MigrateWithBindingsAsmdef` with
   `-executeMethod` in batch mode. Expect `Assets/XLuaConfig/XLuaConfig.cs`
   with the lists of `Assets/XLua/Editor` only (`ExampleConfig.BlackList` by
   name; the examples' lists reported as already converted in
   `Assets/XLua/Examples/XLuaConfig.cs`), no `XLuaBindings` folder, the report,
   and a clean compile. Run it a second time: no file changes.
2. **The asmdef layout:** `prepare.py --asmdef --editor-tools` and the
   generator installed (`build_source_generator.py --install
   <project>/Assets/XLua/Src/Analyzers`); a game asmdef `Game` that references
   XLua with a `[LuaCallCSharp]` list of its types and of `UnityEngine.Rect`,
   and an Editor-folder list of `UnityEditor.EditorWindow`. Run the menu.
   Expect `Assets/XLuaConfig/XLuaBindings/XLuaBindings.asmdef` (references
   `XLua`), `Rect` there, the `Game` types next to `Game.asmdef`,
   `EditorWindow` in `Assets/XLuaConfig/Editor/`, no error that a file
   compiles into the wrong assembly, and after the reload no "no generated
   bindings" error (SourceGenChecks) and `W_UnityEngine_Rect_*` in
   `XLuaBindings.dll`.
3. **The editor suite with converted lists:** run the tool on the editor
   suite's project and replace `Test/UnityMatrix/SourceGen/XluaSourceGenSuiteConfig.cs`
   (hand-written "as the migration tool will write them"; it leaves out
   Camera's nested types, which the tool would write) with the output, then
   `editor_tests.py` in mode `sourcegen`.

**Questions for the owner (M9):**

1. **The legacy generator reading the assembly forms.** The examples and the
   suite now carry each configuration twice, lists for Generate Code and
   attributes for the source generator, kept equal by the checks. If Generate
   Code also read `[assembly: LuaCallCSharp(typeof(T))]` and the other forms
   (additive: no upstream project has them), the lists could go from the
   examples now and projects could migrate before M11, and `SysGenConfig`
   would not be needed twice either (M4 question 2). Do it, or keep both forms
   until M11 removes Generate Code? **Answered 2026-10-18: keep both forms until
   M11; at M11 the example lists go and the migration tool stays.**
2. **The two overload-order differences** (findings 2 and 3). Options: accept
   and document them (done in the guide); make the generator read parameter
   flags and declaration order through `System.Reflection.Metadata` for
   metadata types (exact legacy order in the Editor, more code and one more
   dependency); or define the tie order by signature in both generators (a
   behaviour change in the `legacy` preset). Which? **Answered 2026-10-18: accept
   and document them (MIGRATING.md).**
3. **`Assets/XLua/Editor`'s configuration** (`ExampleConfig.BlackList`,
   `MethodFilter`, `XLuaUnityDefaultConfig.SpanMembersFilter`) is not converted
   in the repository: it ships in every install, and each project's run of the
   tool converts it into that project's files. Most of its `BlackList` entries
   exist because Generate Code wrote calls to members missing on some
   platforms, which the source generator, compiling per platform, does not
   need; the `Span` filter matches nothing that the generator would wrap. Keep
   it as is, or drop these lists from the source-generator path (M11)?
   **Answered 2026-10-18: drop them at M11.**

### M10 as implemented (2026-10-18)

User-visible changes are in [MIGRATING.md](../MIGRATING.md#source-generator-preview)
and [LuaCATS.md](../../Assets/XLua/Doc/LuaCATS.md#with-the-source-generator).
What landed:

| Part | Where |
|---|---|
| Manifest keys `reflectionUse` and `blackList` | `General/SourceGenerator/XLua.SourceGenerator/Emitter.cs` `Manifest` |
| Manifest reader: markers, JSON, type names, member signatures | `Assets/XLua/Src/Editor/LuaCATSGenerator.cs` `SourceGenManifests`, `LuaCATSBound`, `LuaCATSJson` |
| LuaCATS configuration per assembly, member filters from the manifest | `LuaCATSGenerator.cs` `luaCATSConfig`, `LuaCATSWriter.bound`/`keepMethod` |
| Validate Setup on the source generator path | `Assets/XLua/Src/Editor/SetupValidator.cs` `checkSourceGenerator`, `checkGeneratorInstall`, `AssembliesWithoutBindings` |
| Generic methods C# cannot infer left out (XLSG004) | `General/SourceGenerator/XLua.SourceGenerator/Members.cs` `TypeParametersInferable` |
| Manifest run of the LuaCATS check | `Test/Native/luacats_check.py --source-gen`, `Test/LuaCATS/SampleAttributes.cs`, `SampleVariant.cs`, `NoBindingsSample.cs` |
| Generator unit tests (27; `GenericMethodsCSharpCannotInfer` and `ManifestForLuaCATS` are new) | `General/SourceGenerator/XLua.SourceGenerator.Tests` |

How it follows section 2, rows 12 and 17, and owner decision 1:

- **One configuration per assembly.** `SourceGenManifests.Read` finds the
  assemblies with `[assembly: XLua.Binding.GeneratedBindings]` (matched by
  name, since `XLuaGenerate` loads assemblies built against another
  `XLua.Mini.dll`) and reads `Manifest.Json` from the namespace of the
  marker's `Bindings` type. Such an assembly's configuration lists are not
  read (the generator does not read them either, XLSG011); every other
  assembly is read with `GetGenConfig` as before. With no marker anywhere the
  code path is the old one, call for call.
- **Wrapped types: the manifest decides.** A type in a manifest's `types`
  (resolved through the marker's `Types`, so generic types need no parsing)
  declares a constructor, method, operator or indexer only when its
  signature is in the manifest, and a field, property or event only when the
  getter, setter or event list names it. `DoNotGen` (lazy) members stay, as
  before, with the old filters; fallback members (M5) stay because reflection
  reaches them. The LuaCATS-only rules (Lua identifiers, the stricter
  generic-method rule, string-keyed indexers as methods, the overload
  collapse) still apply. The obsolete and `BlackList` filters and the
  compiler-dependent `ref readonly` guess (`Gen.cs:611-694`) are not applied
  to these types: the generator already decided.
- **Signatures.** The reflection side writes what `Wrappers.Signature`
  writes: the member of the generic type or method definition (found by
  metadata token), `Type.ToString()` names, by-ref `&`, no default-value
  suffix.
- **Types the manifest does not wrap** (base classes declared in full,
  `[ReflectionUse]` types) go through the old filters, with the manifests'
  `blackList` entries added to `Generator.BlackList`. Their extension methods
  come from the manifests' extension method map, which is what Lua reaches at
  run time. `reflectionUse` names are `Type.ToString()` strings, resolved
  against the loaded assemblies (`Ns.Outer+Inner`, ``Ns.Generic`2[A,B]``, `T[]`).
- **Validate Setup** (row 17). The source generator path is on when an
  assembly has generated bindings or, in Unity, a copy of
  `XLua.SourceGenerator.dll` exists. Then the `GenPath` check is replaced by:
  each copy of the DLL has the `RoslynAnalyzer` label (Unity); every assembly
  with configuration has bindings (Unity: `SourceGenChecks.AssembliesWithoutBindings`,
  the same answer as the check after each domain reload; `XLuaGenerate`: the
  same rule over the given assemblies, by attribute name), with the IL2CPP
  consequence for `[CSharpCallLua]`; and no `XLuaGenAutoRegister.cs` in
  `GenPath`. The `BlackList`/`DoNotGen`/`AdditionalProperties` name checks
  run only for assemblies without bindings; XLSG003 reports the others with
  file and line. The legacy path keeps all its checks.
- **GCM (decision 1: not in the first version).** Nothing is added to the
  source generator; legacy `GEN_CODE_MINIMIZE` is unchanged and stays tested
  (`run_managed.py --gen-code-minimize`, also as a separate assembly). The
  decision says to measure the size first. A host measurement for the suite's
  configuration (72 wrapped types, 73 with the generator's fallback fixture, and
  26 bridges), all compiled by the same csc with
  `-optimize+`:

  | Bindings | Managed size | Lines |
  |---|---|---|
  | Legacy wrappers (`XLuaGen.dll`) | 274,944 bytes | 26,238 |
  | Legacy `GEN_CODE_MINIMIZE` | 228,352 bytes (−17%) | 22,410 |
  | Source generator, player files | 259,584 bytes (−6%) | 19,908 |
  | Source generator, with the Editor-only files | 417,280 bytes | 19,993 |

  The player files leave out `XLua.Manifest.g.cs`, `XLua.LinkXml.g.cs` and
  `XLua.ObfuzRules.g.cs`, which only Editor and host builds compile; they add
  157,696 bytes, mostly the manifest's JSON as UTF-16 user strings. So the
  source generator is 14% above GCM in IL. This is a proxy: GCM's main saving
  should be in IL2CPP, where each `[MonoPInvokeCallback]` wrapper method gets
  a reverse P/Invoke wrapper and GCM calls instance methods through one
  native trampoline instead (`OT.cs:1453-1490`) **[inferred: not measured
  here]**.
  The number that decides is the IL2CPP `libil2cpp.so` size from
  `Test/MobileSmoke/size_report.py` with the same configuration in three
  variants (legacy, legacy GCM, source generator); that needs a Unity slot.
  **Cost of adding it later:** about one week (row 1a): an emitter path that
  writes static `int f(ObjectTranslator, IntPtr L, int top)` methods bound
  as closed `CSharpWrapper` delegates, an `XLuaBindingOptions(Minimize = true)`
  switch, a `run_managed.py --source-gen` minimized pass and its surface
  diff. **Alternative:** keep GCM only in the legacy preset until M11
  removes it, and drop it if the IL2CPP difference is small. **Measured
  since:** 11 to 16 KB of APK with five wrapped types (M6), 450 to 470 KB
  with 264 ([GCM with a game-sized configuration](#gcm-with-a-game-sized-configuration-2026-10-18)).

Differences found on the way:

1. **Generic methods with a type parameter only the return type uses**
   (`U Convert<T, U>(T)`): the wrapper's call without type arguments failed
   with CS0411 in the user's assembly. Both generators accepted them
   (`isSupportedGenericMethod`, `IsSupportedGenericMethod`), so the legacy
   output has the same error. The source generator now leaves them out with
   XLSG004; reflection (`Utils.IsSupportedMethod`) and LuaCATS never offered
   them. `Test/LuaCATS/Sample.cs` has one, and compiling it with the
   generator failed before the change.
2. **The suite's own configuration, both ways** (one-off, not in a script):
   LuaCATS for the `run_managed.py` `XLuaUnitTest.exe` from its lists (mcs
   build) and from its manifest (`--source-gen` build) gives the same 91
   files; the manifest build adds `SourceGenFallback.lua`, a fixture compiled
   only there.

Tested on macOS arm64, Mono 6.14.1, .NET SDK 10.0.401, at `main` `7ff004a`
plus this work (424 Lua tests), with the M8 host plugins (no native change
since):

- `build_source_generator.py --test`: 27 of 27 (`GenericMethodsCSharpCannotInfer`
  fails without the `Members.cs` change);
- `luacats_check.py --source-gen --download-luals`, Lua 5.5.1 and LuaJIT 2.1:
  every legacy check as before (golden, strict, suffix, `.luarc.json` rules,
  Clear, determinism with tr-TR through `CultureLauncher.cs` and CRLF
  sources, Validate Setup, unit-test smoke, LuaLS); the manifest run gives
  the same 15 expected files (default and strict, LuaJIT overlays for
  LuaJIT), equal on a second run, under tr-TR and from CRLF sources; the
  variant and the five Validate Setup cases of the source generator path
  pass;
- `run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen
  --source-gen-fallback`, Lua 5.5.1: every mode 424 of 424; surface diff
  (72 types, 662 members, 13 extension methods), bridge and struct diffs
  equal;
- `editor_compile_check.py`: 7 editors x 55 configurations, 378 ok, 7 skipped
  (the C# 12 test fixture), no warning from the two changed Editor files.

Not run: any Unity editor. For an agent with a Unity slot (one editor at a
time, not during a phone benchmark):

1. In a project from `Test/UnityMatrix/prepare.py --asmdef --editor-tools`
   with the generator installed (`build_source_generator.py --install
   <project>/Assets/XLua/Src/Analyzers`) and the `Test/LuaCATS/Sample.cs` and
   `SampleAttributes.cs` pair in Assembly-CSharp: **XLua > Generate LuaCATS
   Definitions** logs "the configuration of Assembly-CSharp ... comes from
   the xLua source generator's manifest", and `LuaTypes/` equals
   `Test/LuaCATS/expected` (plus the files for the XLua assembly's own
   bindings, if it has wrapped types). On 2021.3.45f2 (Roslyn 4.3.1) and
   6000.6.
2. **XLua > Validate Setup** in that project: no problems. Then remove the
   `RoslynAnalyzer` label from `XLua.SourceGenerator.dll` (after the
   recompile: the label problem and one "no generated bindings" problem per
   configured assembly), put the label back, and add
   `Assets/XLua/Gen/XLuaGenAutoRegister.cs` (the Generate Code problem).
3. The same project without the generator (Assets layout, Generate Code):
   Validate Setup and the LuaCATS menu behave as before.
4. The IL2CPP size measurement above, Android ARM64, if the owner wants the
   GCM numbers. Done: M6 (small configuration) and
   [GCM with a game-sized configuration](#gcm-with-a-game-sized-configuration-2026-10-18).

**Questions for the owner (M10):**

1. **GCM:** run the IL2CPP size comparison now (Unity slot, about an hour),
   or settle on dropping GCM at M11 from the host numbers? Run (M6, then a
   game-sized configuration); the remaining question is in
   [GCM with a game-sized configuration](#gcm-with-a-game-sized-configuration-2026-10-18).
2. **Legacy Generate Code and `Convert<T, U>`-style generic methods:** the
   legacy generator writes a call that does not compile. Leave the legacy
   generator as it is (it goes at M11), or leave such methods out there too,
   which changes the `legacy` preset's output for a case that never
   compiled? **Answered 2026-10-18: leave such methods out of Generate Code
   (they go to reflection) until M11 removes it.**
3. **Configuration lists in an assembly with generated bindings:** LuaCATS
   does not read them, as the generator does not; Lua still reaches such
   types through reflection, so they are typed `any` (or reported in strict
   mode). Keep that, or also declare them? **Answered 2026-10-18: keep it;
   Validate Setup already reports such lists, and after M11 lists are not a
   configuration form any more (the migration tool converts them).**

### Generated code without XLua.Core: measured, not taken

**Question (2026-10-18).** Could the generated code compile in an asmdef
that references only XLua, so that users do not add XLua.Core? Built in
https://github.com/emindeniz99/xlua-ng/pull/1 (closed, not merged; branch
`archive/xlua-binding-luaapi`, commits `e36e7bb6` to `5677363d`, head
`1b0dfa66`).

**What it needed.** The generator's output for the name table fixture,
compiled against the runtime split as Unity splits it with only XLua
referenced (main at `cdf3b3f3`, XLSG012 check off) [C]: 611 CS0012 for
every member access on `XLua.LuaDLL.Bridge` (its base class `Lua` is in
XLua.Core, so even Bridge's own members fail), 195 CS0012 from overload
resolution of `ObjectTranslator.Push`, `Utils.RegisterFunc`,
`BeginClassRegister` and `EndObjectRegister` (each has a `lua_CSFunction`
overload), and CS0234 for `MonoPInvokeCallback`, `lua_CSFunction` and
`LuaTypes`. A second delegate type and attribute in XLua were therefore not
enough. The branch added, in `XLua.Binding`, a `CSFunction` delegate, a
`MonoPInvokeCallbackAttribute`, and `LuaApi`, a class without a base with
72 AggressiveInlining forwarders to Bridge and the registration functions
taking `CSFunction` (one copy of every import; `Utils` gained pointer
overloads). Members whose signature still used an XLua.Core type (such as
`LuaEnv.AddBuildin`) went to reflection with an XLSG012 info. Everything
passed: the generator tests, `run_managed.py` in all modes on Lua 5.5.1 and
LuaJIT with equal surface and bridge diffs, the editor suite on 2022.3.62f3
from the package, and an IL2CPP Android player on the emulator.

**Cost**, Android ARM64 Development players of `Test/MobileSmoke prepare.py
--package --source-gen`, 6000.3.21f1, Minimal stripping, main at `3098746b`
against the branch merged with it:

| | main | forwarders | change |
|---|---:|---:|---:|
| APK | 19,674,823 | 19,681,647 | +6,824 |
| `libil2cpp.so` | 11,656,648 | 11,674,328 | +17,680 (+0.15%) |
| `global-metadata.dat` | 1,873,276 | 1,878,880 | +5,604 |
| player `XLua.dll` (asmdef layout, 6000.6.6f1) | 258,048 | 263,168 | +5,120 |

IL2CPP inlined the forwarders (`IL2CPP_MANAGED_FORCE_INLINE`), so calls
cost the same; host Mono timings were within noise.

**Decision (owner, 2026-10-18, question 25 a):** keep the mandatory
XLua.Core reference; the size is too much against one asmdef line. Kept
from the branch: XLSG012 stays an error and its message now names the
asmdef and the exact line (`"references": ["XLua", "XLua.Core"]`), and a
fix to the model cache (section 7). The cache keyed a metadata type's model
by its assembly's MVID and the configuration only, but the model depends
on which of that assembly's references the compilation has (without them
their types are error types: a struct parameter is read as an object), so
two asmdefs compiled by one Unity compiler server could share a wrong
model. The key now also lists the referenced assemblies the compilation
lacks; unit test `ModelCacheKeysTheMissingReferences` (fails before the
fix).

## 10. Open questions for the owner

1. **`GEN_CODE_MINIMIZE`:** keep it in the source generator (about 1 week,
   section 2 row 1a), or drop it? It is one of the three modes that
   `run_managed.py` tests today. The UPM spike left it out of Mode 2 v1.
   *Answered:* decision 1 below, then the owner's rule of 2026-10-17/18
   (measure at game size, add it if the gap is big); added 2026-10-18.
2. **`GENERIC_SHARING`, UWP (`UNITY_WSA`) code paths and the `USE_UNI_LUA`
   aliases:** drop them from generated code? The first is undocumented and
   untested. The second is an old platform (see the "old platform support"
   rule). The third has no UniLua source behind it.
3. **Binding API form:** is a public `[EditorBrowsable(Never)]` `XLua.Binding`
   namespace with a version constant acceptable, given that
   `InternalsVisibleTo` is rejected (section 3)?
4. **Where external-type bindings go:** default to Assembly-CSharp, or have
   the migration tool create an `XLuaBindings.asmdef` to keep compile times
   down (section 7)?
5. **Configuration in player metadata:** make the assembly-level attributes
   `[Conditional("UNITY_EDITOR")]` (zero player bytes, but CodeEmit in a Mono
   player no longer sees them), or keep them in metadata?
6. **Per-member fallback in the `legacy` preset:** the legacy generator drops
   members it cannot generate; the source generator would make many of them
   callable through reflection (section 5.5). That makes more members visible
   to Lua than upstream. Accept, or keep "dropped" in legacy and "fallback" in
   minimal?
7. **Overlapping bindings across sibling assemblies:** accept duplicate code
   with first-registration-wins at run time (section 5.1), or make it a
   compile-time error?
8. **Hotfix:** approve option B (per-assembly bridges, per-assembly IntKey).
   It changes the id map layout, `HotfixDelegateBridge.Set`'s signature and
   `util.lua`'s map loading. Also approve fixing the `Inline` early return
   (`Hotfix.cs:1366-1370`) in the same milestone.
9. **Build policy without registration:** fail IL2CPP builds that have
   `CSharpCallLua` configuration but no generated bindings (section 5.3), or
   only warn as everywhere else?
10. **Custom templates:** is "reported by the migration tool, no replacement"
    enough? The alternative is to keep `Generator.CustomGen` as an Editor-only
    API fed by the manifest. It keeps the `template` library in the Editor,
    which the backlog already allows.
11. **Delegate wrappers for `LuaCallCSharp` delegates** (section 2 row 3):
    confirm they can stay unimplemented. They are unreachable in the legacy
    generator, and their template has a latent bug (row 3).
12. **Order against the presets work:** presets.md puts source-generated
    bindings fourth, after the C# flags, native options and `LuaOptions`.
    Milestone M0 is independent and unblocks the UPM layout. Start M0 early?

## Decisions (owner, 2026-10-17)

| Question | Decision |
|---|---|
| 1. `GEN_CODE_MINIMIZE` | Not in the first version. The emitter stays modular, so a size-optimised emission mode can be added later as one more emitter path. Measure the size first. *2026-10-17/18:* measure GCM at game size first and add it to the source generator if the gap is big. Measured 446 to 473 KB ("GCM with a game-sized configuration"), so it was added ("GCM mode as implemented"). |
| 2. `GENERIC_SHARING`, UWP, `USE_UNI_LUA` | Dropped. UWP is deprecated by Microsoft, UniLua is unmaintained, and `GENERIC_SHARING` was an undocumented experiment: one wrapper per generic type definition instead of per closed type (`ObjectTranslator.cs:969`, `Generator.cs:1179`). |
| 3. Binding API | A versioned public `XLua.Binding` API, not `InternalsVisibleTo`. |
| 4. Where external types are bound | The migration tool creates an `XLuaBindings` asmdef for engine, .NET and package types, so that editing game scripts does not recompile their bindings. A project's own types are bound in their own assembly, because an asmdef cannot reference Assembly-CSharp. New projects may keep everything in one assembly. |
| 5. Config attributes | `[Conditional("UNITY_EDITOR")]`, so they cost no player bytes. |
| 6. Members the generator cannot handle | Fall back to reflection, with one log per member in development builds. |
| 7. Two assemblies binding the same type | A warning; the first registration wins. |
| 8. Hotfix | One bridge class and one IntKey id map per assembly. This includes the fix for the `Inline` early return and loading every map in `util.lua`. |
| 9. `CSharpCallLua` config without generated bindings on IL2CPP | Fails the build. |
| 10. Custom templates | Reported by the migration tool; no `CustomGen` replacement until someone needs it. |
| 11. Wrappers for delegates listed in `LuaCallCSharp` | Not implemented. Lua keeps calling C# delegates through reflection, as today. The unreachable `LuaDelegateWrap.tpl.txt` is removed together with the legacy generator. |
| 12. M0 | Starts in its place in the presets order. |
