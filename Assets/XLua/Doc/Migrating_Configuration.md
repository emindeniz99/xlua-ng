# Converting configuration lists

xLua's source generator (preview, see [MIGRATING.md](../../../docs/MIGRATING.md#source-generator-preview))
reads configuration attributes: on your types (`[LuaCallCSharp] public class Player`)
and at assembly level (`[assembly: LuaCallCSharp(typeof(UnityEngine.Transform))]`).
It does not read configuration lists, the static fields and properties that
"Generate Code" evaluates:

```csharp
public static class MyConfig
{
    [LuaCallCSharp]
    public static List<Type> LuaCallCSharp = new List<Type> { typeof(UnityEngine.Transform) };
}
```

Such a list only gets info `XLSG011` from the generator. The migration tool
converts the lists to attributes. It evaluates them the way Generate Code does,
so a list computed with LINQ, such as the ones in `ExampleConfig.cs`, gives the
types it returns when the tool runs.

## Running the tool

In the Editor, run **XLua > Migrate Configuration to Attributes**. The tool:

1. reads every configuration list in the loaded assemblies, with the filters
   Generate Code applies (public, not obsolete, not an open generic type...);
2. writes `XLuaConfig.cs` for each assembly that receives configuration (see
   below);
3. writes the report `Assets/XLuaConfig/XLuaMigrationReport.md` and logs a
   one-line summary.

Outside Unity, `XLuaGenerate --migrate` does the same for the assemblies you
name (`Tools/build_tools.py` builds `XLuaGenerate.exe`):

```sh
mono XLuaGenerate.exe --migrate --output Converted --bindings-host Assembly-CSharp \
    --source-root Assets Library/ScriptAssemblies/Assembly-CSharp.dll \
    /path/to/Unity/Managed /path/to/Unity/Managed/UnityEngine
```

| Option | Meaning |
|---|---|
| `--output dir` | Writes `<assembly>.XLuaConfig.cs` per assembly and `XLuaMigrationReport.md` there (default `XLuaConfig`) |
| `--bindings-host name` | The assembly for the configuration of types from other assemblies (default `XLuaBindings`) |
| `--lists-in name` | Converts only the lists declared in this assembly (repeatable; default all) |
| `--source-root dir` | Where the configuration classes' sources are, to point at their `#if` branches in the report (repeatable) |
| `--define symbol` | The project's scripting defines, for the report (repeatable) |
| `--report path` | Writes the report elsewhere |

Paths after the assemblies are search paths for their references. The exit
code is 1 when a list could not be evaluated.

## Where the attributes go

| Configured type | Written into |
|---|---|
| A type of your project | The assembly that defines it: `Assets/XLuaConfig/XLuaConfig.cs` for Assembly-CSharp, `Assets/XLuaConfig/Editor/` for Assembly-CSharp-Editor, `Assets/Plugins/XLuaConfig/` for the firstpass assemblies, next to the `.asmdef` for an asmdef that references XLua |
| An engine, .NET or package type, or a type of an asmdef that does not reference XLua | The bindings host: `Assets/XLuaConfig/XLuaBindings/`, with an `XLuaBindings.asmdef` that the tool creates, references XLua, XLua.Core (which every asmdef with generated bindings needs, `XLSG012` otherwise) and the asmdefs of the configured types, and keeps auto-referenced. When xLua compiles into Assembly-CSharp (the default layout), Assembly-CSharp instead |
| An Editor-only engine type (UnityEditor) | Assembly-CSharp-Editor |
| A `[Hotfix]` type | The assembly that defines it (the hotfix bridges are generated there) |
| A type that an extension class extends | The assembly of the extension class, since the generator merges extension methods into a wrapper only within one assembly |

Keeping engine and package types in `XLuaBindings` means that editing game
scripts does not recompile their bindings. For a project that wants
everything in one assembly, run `CSObjectWrapEditor.ConfigMigrationMenu.MigrateIntoAssemblyCSharp`
(for example with `-executeMethod`).

Attributes on types and members (`[LuaCallCSharp] class Player`,
`[BlackList] void Debug()`) stay where they are; the generator reads them.
Attributes on the types of a precompiled DLL are written into the bindings
host, since no generator compiles that DLL.

## What the tool writes

| List | Attributes |
|---|---|
| `[LuaCallCSharp]`, `[CSharpCallLua]`, `[ReflectionUse]` `IEnumerable<Type>` | One `[assembly: XLua.LuaCallCSharp(typeof(T))]` (and so on) per type Generate Code uses, in list order |
| `[GCOptimize]` | The same, with the `OptimizeFlag` |
| `[BlackList]` `List<List<string>>` | `XLua.BlackList(typeof(T), "member", typeof(P)...)` for a member of a wrapped type of your project; the by-name form `XLua.BlackList("T", "member", "P"...)` for the rest: engine, .NET, package and precompiled types (their members differ between Unity versions and build targets, and a missing member is only info `XLSG003` in this form), types that are not wrapped or not found, members that do not exist here, by-ref parameter types |
| `[BlackList]` `Func<MemberInfo, bool>` | The filter is run on every member of every wrapped type that Generate Code passes to it; one `BlackList` entry per match, in the same two forms |
| `[DoNotGen]` and `[AdditionalProperties]` `Dictionary<Type, List<string>>` | `XLua.DoNotGen(typeof(T), "a", "b")`; names that match nothing are left out. There is no by-name form: the report lists entries for engine and package types, whose members may be missing in another Unity version (warning `XLSG003`) |
| `[Hotfix]` `IEnumerable<Type>` | `XLua.Hotfix(typeof(T), flags)` with the injector's filter (no enums, delegates or constructed generic types) |

Each written file starts with a comment that names the lists it converts.
When the tool runs again it rewrites its files, and it leaves out lists that
another `XLuaConfig.cs` already converts (xLua's converted examples,
`Assets/XLua/Examples/XLuaConfig.cs`), so nothing is configured twice.

## The report

- **Converted:** each list, how many attributes it gave and where.
- **Not converted:** configuration that has no attribute form: `[GenPath]`,
  `[GenCodeMenu]` custom generators and their templates (no replacement,
  decision 10 of the [design](../../../docs/design/source-generator.md#decisions-owner-2026-10-17)),
  `GEN_CODE_MINIMIZE` and `GENERIC_SHARING`, and types without a `typeof`
  spelling (private nested types, compiler-generated types).
- **Check by hand:** see below.
- **BlackList entries kept by name**, with the reason.
- **Left out, as the legacy generator leaves them out:** obsolete or
  non-public types, delegates under `LuaCallCSharp`, types that are also
  configured on the type, and so on. Generate Code ignores these too.

## Check by hand

- **Computed lists and filters are snapshots.** A list that a property getter
  computes (LINQ over `AppDomain.CurrentDomain.GetAssemblies()`) and every
  `Func<MemberInfo, bool>` filter are written out as the types and members
  they gave when the tool ran. Run the tool again after adding types,
  assemblies or packages, changing a filter or upgrading Unity.
- **`#if` in configuration classes.** The tool sees the lists as the Editor
  compiled them, with its defines (for example, the `UNITY_WEBGL` entry of
  `ExampleGenConfig.BlackList` is missing unless WebGL is the active
  platform). The report names the files with `#if`. Put platform entries
  under the same `#if` in the written file by hand, or keep the by-name
  `BlackList` form, which only gives info `XLSG003` where a member is missing.
  Many such entries exist only because Generate Code wrote calls to members
  that are missing on some platforms; the source generator compiles against
  each platform's assemblies and wraps only the members that exist there.
- **Keep the lists while you use Generate Code**, which still reads only the
  lists. The Editor's reflection wrappers also read `[BlackList]` lists and
  filters. Remove the lists when the project no longer uses Generate Code.
- **`[Hotfix]` lists:** the hotfix injector reads the lists and `[Hotfix]` on
  types; keep the lists while you inject.
- **Extension methods across assemblies:** the report names an extension
  class and an extended type that ended up in different assemblies.

## Differences that remain

For the same configuration the source generator binds what Generate Code binds;
[MIGRATING.md](../../../docs/MIGRATING.md#source-generator-preview) lists the
exceptions (array types, init-only setters, members that fall back to
reflection). Two more, found with the examples:

- **Overload order of .NET types.** Generate Code orders overloads of equal
  cost as reflection returns them from the Editor's runtime assemblies; the
  source generator as the reference assemblies the scripts compile against
  declare them. The two orders can differ (for example the constructors of
  `List<int>`), which matters only when a Lua call matches several overloads.
- **`[Out]` parameters that are not `out`.** Generate Code's overload costing
  skips them (Unity's `TrailRenderer.AddPositions(NativeArray<Vector3>)`); the
  source generator cannot see that flag in compiled assemblies and keeps the
  declaration order.
