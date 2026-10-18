# XLua configuration

All xLua configurations support three methods: tagging, static lists, and dynamic lists.

There are two requirements and two recommended items for configuration:

* List mode must use static fields/properties.
* List mode must be placed in a static type.
* Using tagging is not recommended.
* Placing list configuration in the Editor directory is recommended. For Hotfix configuration targeting types in DLLs other than Assembly-CSharp.dll, this is required.

**Where xLua compiles.** xLua ships assembly definitions: `Assets/XLua/Core` compiles into `XLua.Core`, `Assets/XLua/Src` into `XLua`, and their `Editor` folders into `XLua.Core.Editor` and `XLua.Editor`. Configuration in scripts without an asmdef (Assembly-CSharp, Assembly-CSharp-Editor, such as `Assets/XLua/Editor/ExampleConfig.cs`) sees xLua as before. Configuration in an asmdef of yours needs `XLua` in its Assembly Definition References, and `XLua.Core` too with the source generator; **XLua > Validate Setup** reports asmdefs that lack them. Keep `GenPath` outside `Assets/XLua/Src` and `Assets/XLua/Core`, or the generated code lands in xLua's assembly, which cannot see your types. **XLua > Project Layout > Use Assembly-CSharp (no asmdefs)** puts xLua back into Assembly-CSharp, as upstream xLua; see [MIGRATING.md](../../../docs/MIGRATING.md#xlua-in-its-own-assemblies-asmdefs-the-default).

**Tagging**

xLua uses a whitelist to indicate which code is to be generated, and the whitelist is configured via attributes. For example, if you want to call a C# type from Lua or you want to generate the adaptation code, you can add a LuaCallCSharp tag for this type:

~~~csharp
[LuaCallCSharp]
public class A
{

}
~~~

This mode is convenient, but it will increase the code on the il2cpp and therefore is not recommended.

**Static list**

Sometimes we cannot directly tag a type, such as a system API, a library without source code, or an instantiated generic type. In this case, you can declare a static field in a static type. This field can be any type except for BlackList and AdditionalProperties, as long as IEnumerable&lt;Type&gt; is implemented (these two exceptions will be specifically described later). Then add a tag to this field:

~~~csharp
[LuaCallCSharp]
public static List<Type> mymodule_lua_call_cs_list = new List<Type>()
{
    typeof(GameObject),
    typeof(Dictionary<string, int>),
};
~~~

This field needs to be placed in a **static type** and placing it in the **Editor directory** is recommended.

**Dynamic list**

Declare a static property and tag it accordingly.

~~~csharp
[Hotfix]
public static List<Type> by_property
{
    get
    {
        return (from type in Assembly.Load("Assembly-CSharp").GetTypes()
                where type.Namespace == "XXXX"
                select type).ToList();
    }
}
~~~

The website version of this example uses `Assembly.GetExecutingAssembly().GetTypes()` instead. That selects the assembly containing the configuration, whereas `Assembly.Load("Assembly-CSharp")` selects the gameplay assembly explicitly; choose the assembly that contains the target types.

Getter is code. You can use it to implement a lot of results, such as configuration by namespace, configuration by assembly, and so on.

This property needs to be placed in a **static type** and placing it in the **Editor directory** is recommended.

**Assembly-level attributes (xlua-ng)**

Each attribute also takes the configured type, at assembly level:
`[assembly: LuaCallCSharp(typeof(GameObject))]`. `LuaCallCSharp`,
`CSharpCallLua`, `GCOptimize`, `ReflectionUse` and `Hotfix` also take several
types in one attribute, with the flags of `GCOptimize` and `Hotfix` first,
for all of them:

~~~csharp
[assembly: LuaCallCSharp(typeof(GameObject), typeof(Transform), typeof(Vector3))]
[assembly: GCOptimize(OptimizeFlag.PackAsTable, typeof(Game.Pair), typeof(Game.Range))]
[assembly: Hotfix(HotfixFlag.IntKey, typeof(Game.Shop), typeof(Game.Bag))]
~~~

Both forms mean the same: one attribute with several types configures each
of them as one attribute per type would. `BlackList`, `DoNotGen` and
`AdditionalProperties` name members of one type, so they take one type each.

The source generator (preview)
reads these forms and the tags, but not static or dynamic lists. **XLua >
Migrate Configuration to Attributes** converts the lists, computed ones
included, one type per attribute; **XLua > Migrate Configuration to
Attributes (Grouped)** writes the types of a list that share a namespace into
one attribute. See [Converting configuration lists](Migrating_Configuration.md).
The generator runs in every assembly that references XLua. In the asmdef and
package layouts such an asmdef must also reference XLua.Core, whose types the
generated code uses: `"references": ["XLua", "XLua.Core"]`. Without it the
generator reports error `XLSG012` with the line to add and generates nothing
for that assembly. Assembly-CSharp needs nothing: it references every
auto-referenced asmdef.

### XLua.LuaCallCSharp

When adding this configuration for a C# type, xLua will generate the adapter code for this type (including constructing an instance for the type, and accessing its member properties & methods and static properties & methods). Otherwise, it will try to gain access using the reflection mode with lower performance.

Adding this configuration to the Extension Methods of a type will also generate the adaptation code and append it to the member methods of the extended type.

XLua will only generate the type loaded with this configuration. It will not automatically generate the adaptation code of its parent type. When accessing the parent type method of the child type object, if the parent type has the LuaCallCSharp configuration, the parent type's adaptation code will be executed. Otherwise it will try to gain access using the reflection mode.

The reflection mode access not only has poor performance, but also may cause failed access on the il2cpp due to code stripping. This problem can be avoided through the ReflectionUse tag, which is described below.

### XLua.ReflectionUse

When adding this configuration to a C# type, xLua generates a link.xml to block code stripping on the il2cpp.

For extension methods, you must add LuaCallCSharp or ReflectionUse to make them accessible.

It is recommended that all types to be accessed in Lua have the LuaCallCSharp or ReflectionUse tag, to insure their proper operation on all platforms.

### XLua.DoNotGen

This indicates that some of the functions, fields, and properties in a type do not generate code and are accessed through the reflection mode.

Only static fields or properties of type `Dictionary<Type, List<string>>` can be used. The key indicates the effective type. Value is a list. The name of the functions, fields, and properties with no code generated are configured.

The differences from ReflectionUse are: 1. ReflectionUse specifies the entire type; 2. Upon the first access to a function (field, property), ReflectionUse will wrap the entire type, while DoNotGen will only wrap the function (field, property). In other words, DoNotGen is lazier.

The differences from BlackList are: 1. BlackList cannot be used when it is configured. 2. BlackList can specify an overloaded function, while DoNotGen cannot.

### XLua.CSharpCallLua

This allows you to adapt a Lua function to a C# delegate (one scenario is various callbacks at the C# side: UI events, delegate parameters, such as List&lt;T&gt;:ForEach; another scenario is to use the Get function of LuaTable to indicate that a Lua function is bound to a delegate), or to adapt a Lua table to a C# interface. The delegate or interface needs this configuration.

### XLua.GCOptimize

A C# pure value type (Note: It refers to a struct that contains only the value type, and it can nest other structs that contain only the value type) or a C# enumerated value has this configuration. xLua generates gc-optimized code for this type. The result is that the value type is passed between Lua and C# with no (C#)gc alloc generated, and that no gc is generated during array access to this type. For various GC-free scenarios, refer to the 05\_NoGc example.

For non-enum complex types with a parameterless constructor, the generator also emits conversion code from a Lua table to the type and to a one-dimensional array of that type. This will optimize the performance of this conversion, including fewer gc allocs.

### XLua.AdditionalProperties

This is GCOptimize's extended configuration. Sometimes, some structs want to make the field private and access the field through the property. In this case, you need to use this configuration (by default, GCOptimize only packetizes/depacketizes the public field).

The tagging mode is relatively simple and the configuration mode is complicated. The requirements are that Dictionary&lt;Type, List&lt;string&gt;&gt; type, and the Key of the Dictionary are effective types; and value is the list of property names. See xLua's configuration of several UnityEngine value types and the SysGCOptimize type.

### XLua.BlackList

If you do not want to generate an adaption code for a member of a type, you can implement it with this configuration.

The tagging method is relatively simple, and the corresponding member can be added.

Considering that it may be necessary to add one of the overloaded functions to the blacklist, the configuration is more complicated. The type is List&lt;List&lt;string&gt;&gt;. For each member, add one entry to the outer list and the second-level list is a string list. The first string is the full path name of the type, the second string is the member name. If the member is a method, you also need to list the full path of the type of its parameters starting from the third string.

For example, the following adds a property of GameObject and a method of FileInfo to the blacklist:

~~~csharp
[BlackList]
public static List<List<string>> BlackList = new List<List<string>>()  {
    new List<string>(){"UnityEngine.GameObject", "networkView"},
    //new List<string>(){ typeof(UnityEngine.GameObject).FullName, "networkView"},
    new List<string>(){"System.IO.FileInfo", "GetAccessControl", "System.Security.AccessControl.AccessControlSections"},
    //new List<string>(){ typeof(System.IO.FileInfo).FullName, "GetAccessControl",typeof(System.Security.AccessControl.AccessControlSections).FullName },
};
~~~

### The following is the generator configuration, which must be placed in the Editor directory.

### CSObjectWrapEditor.GenPath

Configures the path of the generated code, with the type being a string. By default, it is placed in &quot;Assets/XLua/Gen/&quot;.

### CSObjectWrapEditor.LuaCATSPath

Configures the output folder of &quot;XLua/Generate LuaCATS Definitions&quot;, with the type being a string. By default, it is &quot;LuaTypes/&quot; in the project folder, outside Assets. See [LuaCATS definitions](LuaCATS.md).

### CSObjectWrapEditor.LuaCATSFileSuffix, LuaCATSStrict, LuaCATSLuarcPath

Further LuaCATS options: the ending of the file names (a string, default &quot;.lua&quot;), strict `CS` names (a bool, default false), and the `.luarc.json` the generator writes (a string; empty turns it off). See [LuaCATS definitions](LuaCATS.md#options).

### CSObjectWrapEditor.GenCodeMenu

This configuration is used for secondary development of the build engine. When adding this tag to a parameterless function, it will trigger calling the function when executing the &quot;XLua/Generate Code&quot; menu.


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
