# Source-generated bindings

Status: **design, written 2026-10-17; milestones M0, M1 (host part), M2
(host part), M3 (host part) and M4 (host part) implemented 2026-10-18** (see
"M0 as implemented" to "M4 as implemented" after section 9). It covers the work in
[presets.md](presets.md), "Legacy code generation goes before the first public
release", and the Bindings rows of [backlog.md](backlog.md).

Background:

- [research/binding-generation.md](../research/binding-generation.md)
- [SOURCEGEN_SPIKE.md](../../build/SOURCEGEN_SPIKE.md) and `Tools/SourceGenSpike/`
- [research/puerts.md](../research/puerts.md) and
  [research/focus-creative-games.md](../research/focus-creative-games.md)
- [research/obfuscators.md](../research/obfuscators.md)
- [research/hotfix.md](../research/hotfix.md)
- the UPM spike report, `/Volumes/T7Data/ext-ai-projects/xlua-ng/upm-spike/REPORT.md`
  (with its Mode 2 prototype in `tools/proto/`)

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
| 1a | Minimized class wrappers (`GEN_CODE_MINIMIZE`) | `T/LuaClassWrapGCM.tpl.txt:123-160`: instance methods on `partial ObjectTranslator`, `int f(IntPtr L, int top)` called through the native `CSharpWrapper` trampoline (`OT.cs:1453-1490`) | `ObjectTranslator.__Register<Name>` | Static methods with a leading `ObjectTranslator` parameter, bound as closed `CSharpWrapper` delegates (`new CSharpWrapper(translator.W_m_Foo)` over an extension method). No partial class. Kept or dropped by the owner (section 10). |
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
- `GEN_CODE_MINIMIZE`, if the owner drops it;
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

Not run: any Unity editor or player. In particular:

1. **The editor suite in the asmdef layout:** `prepare.py --editor-tools
   --asmdef` (and with `--hotfix`), then `editor_tests.py`. Without
   `--asmdef` the suite stays in the Assets layout, as before.
2. **The generator inside Unity:** `build_source_generator.py --install
   <project>/Assets/XLua/Analyzers` next to the copied `XLua.asmdef`, then a
   compile with a configured type: no CS8032/CS9057, `XLua.Generated.*.Bindings`
   in Assembly-CSharp and in a game asmdef, `Register` run by
   `InitializeOnLoadMethod` in edit mode and by `RuntimeInitializeOnLoadMethod`
   in play mode (twice with domain reload off), XLSG diagnostics in the console
   with file and line.
3. **Hotfix in the asmdef layout:** `prepare.py --hotfix --asmdef`, a player
   build with `[Hotfix]` types in Assembly-CSharp: the post-build callback
   finds `XLua.dll`, the injector runs without the `Types.Single` exception,
   and IntKey types report the field-mode fallback.
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

Remaining in M1 after this run: items 1 to 4 above, and the owner's answer to
the question below.

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
   reflection.
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

1. **Unity runs** (not started; one Unity at a time on this machine):
   1. `build_source_generator.py --install <project>/Assets/XLua/Src/Analyzers`
      in a project prepared with `Test/UnityMatrix/prepare.py --asmdef
      --editor-tools`, with the suite's configuration in Assembly-CSharp:
      compile in 2021.3.45f2 and 6000.3.26f1 (Roslyn 4.3.1, so the
      `OldCompiler` branch of the `ref readonly` rules) and 6000.6.6f1;
      no CS8032/CS9057; `W_*` wrappers in Assembly-CSharp; XLSG infos in the
      console.
   2. `editor_tests.py` with the generated wrappers registered by
      `InitializeOnLoadMethod`, and in play mode (twice, domain reload off).
   3. The editor script compile time with 100, 500 and 2,000 UnityEngine types
      (section 7), and after a one-line edit in game code with the bindings
      in an `XLuaBindings` asmdef.
2. **IL2CPP High stripping** static check of the wrappers (M5 lists it with
   link.xml); until M3 delegates in an IL2CPP player go to the generic
   bridges.
3. Array type wrappers (difference 4), if the owner wants them before M11.
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
   rules written by hand), or keep reflection and document it?
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

1. **IL2CPP:** the M1 step 5 of "M1 as implemented" (the closed static
   delegates were checked with the legacy bridges; the source-generated ones
   are internal methods of the user assembly, which should not matter
   **[inferred]**), and `CreatorBySignature` in a player: it calls
   `Delegate.CreateDelegate` with a `MethodInfo` taken from a generated
   delegate, as the legacy lookup by signature does with a `MethodInfo` found
   by name.
2. **The Editor:** the bridges in Assembly-CSharp and a game asmdef with the
   generator installed (M2 remaining item 1), and the editor suite's
   `CSharpCallLua` cases through them.

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

1. **IL2CPP:** the GCOptimize helpers in a player (they are ordinary static
   methods and delegates over them, as the M0 legacy shape is; the array
   accessors are lambdas closed over nothing).
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

## 10. Open questions for the owner

1. **`GEN_CODE_MINIMIZE`:** keep it in the source generator (about 1 week,
   section 2 row 1a), or drop it? It is one of the three modes that
   `run_managed.py` tests today. The UPM spike left it out of Mode 2 v1.
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
| 1. `GEN_CODE_MINIMIZE` | Not in the first version. The emitter stays modular, so a size-optimised emission mode can be added later as one more emitter path. Measure the size first. |
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
