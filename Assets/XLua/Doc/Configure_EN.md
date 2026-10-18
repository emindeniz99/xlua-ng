# XLua configuration

xLua's configuration is a set of attributes in the `XLua` namespace. They take two forms:

* **On a type or member**, for code you own: `[LuaCallCSharp] public class A`, `[BlackList] public void Debug()`.
* **At assembly level**, with the configured type as an argument, for any type, including engine, .NET, package and generic types you cannot annotate: `[assembly: LuaCallCSharp(typeof(GameObject))]`.

An attribute on a type and an assembly-level attribute that names the type configure the same. The xLua source generator reads both while the scripts compile and writes the bindings into the assembly that declares each attribute; see [The source generator](#the-source-generator) below. Without it, xLua reaches the configured types through reflection, in the Editor also through Reflection.Emit, and an IL2CPP player build with `CSharpCallLua` configuration fails, since such a player cannot turn Lua functions into those delegates and interfaces.

**Where xLua compiles.** xLua ships assembly definitions: `Assets/XLua/Core` compiles into `XLua.Core`, `Assets/XLua/Src` into `XLua`, and their `Editor` folders into `XLua.Core.Editor` and `XLua.Editor`. Configuration in scripts without an asmdef (Assembly-CSharp, Assembly-CSharp-Editor) sees xLua as before. Configuration in an asmdef of yours needs `XLua` and `XLua.Core` in its Assembly Definition References; **XLua > Validate Setup** reports asmdefs that lack them. **XLua > Project Layout > Use Assembly-CSharp (no asmdefs)** puts xLua back into Assembly-CSharp, as upstream xLua; see [MIGRATING.md](../../../docs/MIGRATING.md#xlua-in-its-own-assemblies-asmdefs-the-default).

**Tagging**

xLua uses a whitelist to indicate which code is to be generated, and the whitelist is configured via attributes. For example, if you want to call a C# type from Lua or you want to generate the adaptation code, you can add a LuaCallCSharp tag for this type:

~~~csharp
[LuaCallCSharp]
public class A
{

}
~~~

**Assembly-level attributes**

Sometimes we cannot directly tag a type, such as a system API, a library without source code, or an instantiated generic type. Name it in an assembly-level attribute instead, in any C# file of the assembly that should hold its bindings:

~~~csharp
[assembly: LuaCallCSharp(typeof(GameObject))]
[assembly: LuaCallCSharp(typeof(Dictionary<string, int>))]
~~~

`LuaCallCSharp`, `CSharpCallLua`, `GCOptimize`, `ReflectionUse` and `Hotfix` also take several types in one attribute, with the flags of `GCOptimize` and `Hotfix` first, for all of them:

~~~csharp
[assembly: LuaCallCSharp(typeof(GameObject), typeof(Transform), typeof(Vector3))]
[assembly: GCOptimize(OptimizeFlag.PackAsTable, typeof(Game.Pair), typeof(Game.Range))]
[assembly: Hotfix(HotfixFlag.IntKey, typeof(Game.Shop), typeof(Game.Bag))]
~~~

Both forms mean the same: one attribute with several types configures each of them as one attribute per type would. `BlackList`, `DoNotGen` and `AdditionalProperties` name members of one type, so they take one type each.

Rules for the assembly-level form:

* The bindings are generated into the assembly that declares the attribute. Put the attributes in a script of a player assembly, not under an `Editor` folder, whose assembly is not in players. Configuration for Editor-only types (`UnityEditor`) belongs in an Editor assembly.
* `[assembly: Hotfix(typeof(T), flags)]` must be in the assembly that defines `T`, where the hotfix bridges are generated; see [Hotfix_EN.md](Hotfix_EN.md#configure-types-to-patch).
* [Assets/XLua/Examples/XLuaConfig.cs](../Examples/XLuaConfig.cs) configures the examples this way.

**Configuration lists are not read**

Upstream xLua also read static fields and properties marked with a configuration attribute, such as `[LuaCallCSharp] public static List<Type> mymodule_lua_call_cs_list` or a property that computes its types with LINQ. The source generator does not read them; it reports each with info `XLSG011`, and **XLua > Validate Setup** lists them. Convert them once with **XLua > Migrate Configuration to Attributes** (or `XLuaGenerate --migrate` outside Unity), which evaluates them, computed lists and `Func<MemberInfo, bool>` filters included, and writes the same configuration as assembly-level attributes, one `XLuaConfig.cs` per assembly. Then delete the lists. See [Converting configuration lists](Migrating_Configuration.md).

The reflection and emitted wrappers in the Editor and outside Unity still apply `[BlackList]` lists and filters, for types without generated bindings; the source generator and the reflection wrappers in players apply only the `BlackList` attributes (on members and at assembly level).

### XLua.LuaCallCSharp

When adding this configuration for a C# type, xLua will generate the adapter code for this type (including constructing an instance for the type, and accessing its member properties & methods and static properties & methods). Otherwise, it will try to gain access using the reflection mode with lower performance.

Adding this configuration to a static class of extension methods will also generate the adaptation code and append it to the member methods of the extended type. The source generator merges extension methods into a wrapper only within one assembly, so configure the extended type in the assembly of the extension class.

XLua will only generate the type loaded with this configuration. It will not automatically generate the adaptation code of its parent type. When accessing the parent type method of the child type object, if the parent type has the LuaCallCSharp configuration, the parent type's adaptation code will be executed. Otherwise it will try to gain access using the reflection mode.

The reflection mode access not only has poor performance, but also may cause failed access on the il2cpp due to code stripping. This problem can be avoided through the ReflectionUse tag, which is described below.

### XLua.ReflectionUse

When adding this configuration to a C# type, the source generator lists the type for `link.xml` (`XLua.LinkXml.g.cs`, compiled only in the Editor), and a player build passes those entries to UnityLinker, so il2cpp does not strip the type.

For extension methods, you must add LuaCallCSharp or ReflectionUse to make them accessible.

It is recommended that all types to be accessed in Lua have the LuaCallCSharp or ReflectionUse tag, to insure their proper operation on all platforms.

### XLua.DoNotGen

This indicates that some of the functions, fields, and properties in a type do not generate code and are accessed through the reflection mode.

Name the type and the members at assembly level: `[assembly: DoNotGen(typeof(Game.Big), "RarelyUsed", "Other")]`. A name that matches no member gives warning `XLSG003`.

The differences from ReflectionUse are: 1. ReflectionUse specifies the entire type; 2. Upon the first access to a function (field, property), ReflectionUse will wrap the entire type, while DoNotGen will only wrap the function (field, property). In other words, DoNotGen is lazier.

The differences from BlackList are: 1. BlackList cannot be used when it is configured. 2. BlackList can specify an overloaded function, while DoNotGen cannot.

### XLua.CSharpCallLua

This allows you to adapt a Lua function to a C# delegate (one scenario is various callbacks at the C# side: UI events, delegate parameters, such as List&lt;T&gt;:ForEach; another scenario is to use the Get function of LuaTable to indicate that a Lua function is bound to a delegate), or to adapt a Lua table to a C# interface. The delegate or interface needs this configuration.

### XLua.GCOptimize

A C# pure value type (Note: It refers to a struct that contains only the value type, and it can nest other structs that contain only the value type) or a C# enumerated value has this configuration. xLua generates gc-optimized code for this type. The result is that the value type is passed between Lua and C# with no (C#)gc alloc generated, and that no gc is generated during array access to this type. For various GC-free scenarios, refer to the 05\_NoGc example.

For non-enum complex types with a parameterless constructor, the generator also emits conversion code from a Lua table to the type and to a one-dimensional array of that type. This will optimize the performance of this conversion, including fewer gc allocs.

### XLua.AdditionalProperties

This is GCOptimize's extended configuration. Sometimes, some structs want to make the field private and access the field through the property. In this case, you need to use this configuration (by default, GCOptimize only packetizes/depacketizes the public field).

Name the type and its properties at assembly level: `[assembly: AdditionalProperties(typeof(UnityEngine.Ray), "origin", "direction")]`. xLua configures several UnityEngine value types this way in [GenAttributes.cs](../Src/GenAttributes.cs).

### XLua.BlackList

If you do not want to generate an adaption code for a member of a type, you can implement it with this configuration.

The tagging method is relatively simple, and the corresponding member can be added.

For a type you cannot tag, name the member at assembly level. A method also lists its parameter types, so that one overload can be excluded; listing none means the overload without parameters. The by-name form takes `Type.ToString()` names, for members that do not exist on every build target or in every Unity version: a name that matches nothing is only info `XLSG003` in that form.

For example, the following adds a property of GameObject and a method of FileInfo to the blacklist:

~~~csharp
[assembly: BlackList(typeof(UnityEngine.GameObject), "networkView")]
[assembly: BlackList(typeof(System.IO.FileInfo), "GetAccessControl", typeof(System.Security.AccessControl.AccessControlSections))]
// by name:
[assembly: BlackList("UnityEngine.MonoBehaviour", "runInEditMode")]
~~~

A `Func<MemberInfo, bool>` filter cannot run in the compiler. The migration tool evaluates such a filter once and writes one `BlackList` attribute per member it matches.

## The source generator

The source generator (`XLua.SourceGenerator.dll`, from [General/SourceGenerator](../../../General/SourceGenerator)) is a Roslyn analyzer built against the compiler of Unity 2021.3. Unity runs it whenever the scripts compile, so the bindings follow the configuration and the code; there is no generation step and no generated folder.

1. Install it. In the Assets layout, build it with the .NET SDK pinned in `mise.toml` and copy it next to the `XLua` asmdef:

   ```sh
   python3 Tools/build_source_generator.py --output <build folder> --install <project>/Assets/XLua/Src/Analyzers
   ```

   The `.meta` it writes gives the DLL the `RoslynAnalyzer` label, which makes Unity hand it to the compiler. A UPM package built with `python3 Tools/make_upm_package.py --source-gen <build folder>` has it in `Runtime/Analyzers`.
2. Unity runs it in every assembly that references `XLua`: Assembly-CSharp, Assembly-CSharp-Editor, and each asmdef of yours that lists `XLua` in its references. Such an asmdef also needs `XLua.Core`, or it gets error `XLSG012`, with the line to add, and no bindings. After **XLua > Project Layout > Use Assembly-CSharp (no asmdefs)** no asmdef scopes the analyzer, so Unity runs it in every assembly, and the bindings go into Assembly-CSharp, which then holds xLua.
3. Configure the types with the attributes above and let Unity compile. The compiler reports configuration problems as `XLSG` diagnostics with file and line.

`GEN_CODE_MINIMIZE` (Player Settings > Other Settings > Scripting Define Symbols) selects minimized wrappers: each wrapped member becomes a C# method that one native trampoline calls, instead of a native callback of its own, which makes an IL2CPP player with many wrapped types smaller and each call into C# a few nanoseconds slower. Set it in the Player Settings, so that xLua and every assembly with bindings compile with it; an assembly whose define differs from xLua's gets warning `XLSG013`, and its wrappers follow xLua's mode.

When the generator did not run for an assembly with configuration (for example warning CS9057, a compiler older than the one it needs), the Editor logs an error after each domain reload, **XLua > Validate Setup** names the assembly and the likely cause, and an IL2CPP player build with `CSharpCallLua` configuration in it fails. What the generator writes, and what it leaves to reflection, is in [MIGRATING.md](../../../docs/MIGRATING.md#source-generator-preview).

Output of the removed **XLua > Generate Code** (`Assets/XLua/Gen/*.cs`: `XLuaGenAutoRegister.cs`, `DelegatesGensBridge.cs`, `WrapPusher.cs`, `PackUnpack.cs`, `EnumWrap.cs`, the `*Wrap.cs` and `*Bridge.cs` files, and its `link.xml`) no longer compiles. Delete it, but keep `Assets/XLua/Gen/Resources` if it holds hotfix id maps. **XLua > Validate Setup** reports such files. The steps for a project that used Generate Code are in [MIGRATING.md](../../../docs/MIGRATING.md#generate-code-removed).

### Editor settings

These settings are static fields or properties of a static class, marked with an attribute. Put them in an Editor script.

### CSObjectWrapEditor.GenPath

The folder of the hotfix id maps, with the type being a string: the injector writes `Resources/hotfix_id_map*.lua.txt` there (see [IntKey](Hotfix_EN.md#intkey)). By default, it is &quot;Assets/XLua/Gen/&quot;. It was also the output folder of the removed Generate Code.

### CSObjectWrapEditor.LuaCATSPath

Configures the output folder of &quot;XLua/Generate LuaCATS Definitions&quot;, with the type being a string. By default, it is &quot;LuaTypes/&quot; in the project folder, outside Assets. See [LuaCATS definitions](LuaCATS.md).

### CSObjectWrapEditor.LuaCATSFileSuffix, LuaCATSStrict, LuaCATSLuarcPath

Further LuaCATS options: the ending of the file names (a string, default &quot;.lua&quot;), strict `CS` names (a bool, default false), and the `.luarc.json` the generator writes (a string; empty turns it off). See [LuaCATS definitions](LuaCATS.md#options).

`CSObjectWrapEditor.GenCodeMenu`, which marked methods for the Generate Code menu to run, was removed with that menu; see [Custom code generation](Custom_Generate_EN.md).


## Presets

xlua-ng's optional parts are switched with scripting defines (C#) and with
options of the native build. A preset sets many of them at once.

* **legacy** (the default) needs no setting. It behaves like upstream xLua, with
  every part included.
* **minimal** leaves out the StreamingAssets loader, `SignatureLoader`, the
  `template` library in players, `xlua.util`, `perf.profiler` in release
  players, `xlua.load_assembly`, LuaSocket and the `io` library, and turns on
  the [bridge hardening](Security.md#opt-in-bridge-hardening) checks.

To use **minimal**:

1. Add `XLUA_PRESET_MINIMAL` to **Player Settings > Other Settings > Scripting
   Define Symbols** for every platform you build. Add `XLUA_UTIL` as well if
   your Lua code uses `require "xlua.util"`.
2. Build the minimal native plugin next to the default one, for example
   `XLUA_VARIANT=minimal XLUA_PRESET=minimal build/make_android_lua55.sh` and
   `XLUA_VARIANT=minimal XLUA_PRESET=minimal build/make_ios_lua55.sh`. Copy the
   `Plugins/<platform>/xlua-variants/minimal/` folders into `Assets/Plugins`
   and run **XLua/Plugin Variants/Copy Import Settings**.
3. Check with **XLua/Plugin Variants/Show Selection**: a release build takes the
   `minimal` plugins, a Development Build the `development` ones if you built
   them.

Without step 2 the build prints a warning and ships the default plugin, which
works with the minimal C# but is larger. What each part changes, and how to keep
one part of the preset, is in [MIGRATING.md](../../../docs/MIGRATING.md#presets);
the native options are in [build/README.md](../../../build/README.md#presets).
