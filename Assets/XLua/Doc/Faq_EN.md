# Frequently asked questions

## How do I use an xLua distribution package?

Extract the distribution zip into your Unity project directory. For this fork, also follow the repository README and native plugin compatibility instructions in [build/README.md](../../../build/README.md); this repository ships no prebuilt native plugins, so build or download one for your runtime and platform.

## Can I place xLua in another directory?

Yes. Move the `XLua` folder with its `.meta` files, and keep `Core` and `Src` inside it next to each other (XLua > Project Layout finds them by their GUIDs). The source generator writes the bindings into each assembly that uses xLua, wherever xLua is. `GenPath` sets only the folder of the hotfix id maps (the default is `Assets/XLua/Gen`); see [Configuration](Configure_EN.md#csobjectwrapeditorgenpath). Hotfix injection works with xLua in its own assemblies; see [Hotfix_EN.md](Hotfix_EN.md#several-hotfix-assemblies).

## My asmdef does not find xLua (error CS0246)

xLua compiles into its own assemblies (`XLua`, `XLua.Core` and their Editor assemblies). Scripts without an asmdef see them; an asmdef of yours must list `XLua` in its Assembly Definition References, and `XLua.Core` too when the source generator writes bindings into it (error `XLSG012` otherwise). **XLua > Validate Setup** names such asmdefs and the references they lack.

## Can I keep xLua in Assembly-CSharp, as upstream?

Yes: **XLua > Project Layout > Use Assembly-CSharp (no asmdefs)** moves xLua's four `.asmdef` files, with their `.meta` files, into `Assets/XLua/Asmdef~`, which Unity does not import; **Use asmdefs** moves them back. Code that uses xLua's internal members or extends its classes with `partial` declarations needs that layout. Asmdefs of yours cannot use xLua there. The source generator keeps working: with no asmdef to scope it, Unity runs it in every assembly, and Assembly-CSharp, which then holds xLua, gets the bindings (checked with host compilers; a run in a Unity editor is pending). See [MIGRATING.md](../../../docs/MIGRATING.md#the-way-back-everything-in-assembly-csharp).

## How should I configure xLua?

For Lua scripting, put frequently accessed C# types in `LuaCallCSharp`. Exclude members available only under conditional compilation (often `UNITY_EDITOR`) using `BlackList`. Put delegate types used for C# callbacks into Lua in `CSharpCallLua`.

For hotfixes, mark the types to instrument with `[Hotfix]`, or name them in `[assembly: Hotfix(typeof(T))]` in the assembly that defines them. Delegate types used to call back into Lua still need `CSharpCallLua`.

Configure types you cannot annotate with assembly-level attributes, `[assembly: LuaCallCSharp(typeof(UnityEngine.Transform))]`; see [Configuration](Configure_EN.md). [Assets/XLua/Examples/XLuaConfig.cs](../Examples/XLuaConfig.cs) configures the examples this way. Configuration lists, including those computed with reflection, are not read any more: **XLua > Migrate Configuration to Attributes** converts them once ([Converting configuration lists](Migrating_Configuration.md)).

## Must Lua source files have a .txt extension?

No. xLua can load files with any extension. Unity does not import `.lua` files as `TextAsset`, so scripts bundled as text assets, for example under `Resources`, use `.lua.txt` in the examples.

Files downloaded or otherwise stored outside Unity's text asset import pipeline have no such extension restriction. Load them through a `CustomLoader` or an appropriate `package.path`; [Loading Lua sources](Lua_Loaders.md) has loaders for each location. xLua is a library and does not provide a download system; text assets make the bundled examples easy to run.

## Why is a C# member nil in an IL2CPP player when the Editor has it?

(Upstream asked it as: why does iOS report "attempt to call a nil value" when the editor works?)

**A member that IL2CPP stripped reads as nil in Lua, with no error.** IL2CPP players (iOS, Android, WebGL, consoles) strip the engine APIs, .NET APIs and third-party DLL members that no C# code references. Lua reaches a type that has no generated bindings (no `LuaCallCSharp`) through reflection, and reflection finds only what is left: a stripped method reads as nil, so calling it raises "attempt to call a nil value", and a stripped field, property or constant reads as nil and the script goes on with that value. The Editor and Mono players strip nothing, so the same script works there. The 2026-10-28 run of the test suite in an Android IL2CPP player with Minimal stripping hit it with `CS.System.Int64.MaxValue` (C# compiles a `const` into its users, so nothing references the field) and `System.IO.Path.GetExtension` ([il2cpp-suite.md](../../../docs/research/il2cpp-suite.md#differences-from-mono-and-what-was-done)). Minimal stripping still strips the engine and the class libraries; your own assemblies lose members from Low stripping up.

Keep what Lua uses through reflection:

* `[ReflectionUse]` on the type, or `[assembly: ReflectionUse(typeof(T))]` for a type you cannot change (an engine or .NET type). The source generator writes a `link.xml` entry that keeps the whole type (`preserve="all"`), and the player build passes it to UnityLinker. It needs the source generator ([Configuration](Configure_EN.md#xluareflectionuse)).
* `[LuaCallCSharp]`, which generates wrappers that call the members, so the linker keeps them, and makes the calls faster.
* Your own `link.xml` or `[Preserve]`.

xLua cannot tell a stripped member from a misspelt one, so it gives no warning; test the reflected API in a player build.

## What are the historical Unity 2018 compatibility issues?

The upstream guide recommends upgrading versions older than xLua 2.1.14 to at least 2.1.14. It documents two Unity 2018 issues:

1. The .NET Standard 2.0 compatibility profile has no Reflection.Emit, so xLua compiles its emit code (`CodeEmit`) out under that profile; without generated code, delegates and interfaces then fail with "This type must add to CSharpCallLua" and types fall back to reflection. The historical workaround was to use .NET 4.x during editor development. Unity 2021.2 replaced that profile with .NET Standard 2.1, which has the emit APIs: in Unity 2021.3 and later the editor emits wrappers and bridges under both .NET Standard 2.1 and .NET Framework.
2. Unity 2018's .NET 4.x Equivalent runtime and compiler reference assemblies exposed different APIs. The documented Windows installation paths were `Editor/Data/MonoBleedingEdge/lib/mono/unityjit/mscorlib.dll` at runtime and `Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/mscorlib.dll` at compilation. Generated wrappers could reference methods absent from the compiler's assembly.

Exclude those methods with `BlackList`. Before August 6, 2019, generic blacklist entries had to be repeated for each closed type, such as `Dictionary<int, int>` and `Dictionary<float, int>`. Later upstream versions supported predicate filters for this. xlua-ng's source generator reads the reference assemblies that the scripts compile against, so it does not wrap members that are missing from them.

## Where is the native plugin source, and how do I build it?

Native sources and build scripts are under [`build`](../../../build). Read [native plugin compatibility and build instructions](../../../build/README.md) first. This fork provides Lua 5.3, 5.4, 5.5, and LuaJIT scripts; choose the runtime explicitly and use a separate output directory for each runtime.

Scripts generally follow `make_<platform>_<runtime>.sh` or `.bat`; inspect the selected script for its dependencies. CMake is required. iOS and macOS builds require a Mac; Android builds require Android NDK r30: set `ANDROID_NDK` to it. The four Android scripts (Lua 5.3, 5.4, 5.5 and LuaJIT) build armeabi-v7a, arm64-v8a, x86 and x86_64 at API 23 (the lowest level xlua-ng supports) with the NDK's own toolchain.

## Which Lua version should I use?

Lua 5.5.1 is the default build, and new projects can use it. For conservative production use, choose Lua 5.4.9 (`-DLUA_VERSION=5.4.9`, or the `make_<platform>_lua54` scripts): Lua 5.4 has been in use since 2020, has had nine bugfix releases, and its list of known bugs is mature. Lua 5.5 is newer; it adds global declarations, more compact arrays and changes to the garbage collector. xlua-ng's native and managed test suites run on both, and 64-bit integers stay lossless on both. Lua 5.3.6 stays available for projects that already ship it, and LuaJIT 2.1 for its speed on platforms that allow a JIT, with the 64-bit integer limits described in the [tutorial](XLua_Tutorial_EN.md). The size differences are small: in the 2026-10-16 measurements, xLua with 5.4.9 added about 9 KB less to an Android APK than with 5.5.1 ([player size](../../../build/README.md#player-size-2026-10-16)).

## How do I make an IL2CPP player smaller?

Besides the native build options ([presets and size flags](../../../build/README.md#native-layers-and-presets)), Unity's **Player Settings > Other Settings > IL2CPP Code Generation** affects what xLua costs. With **Faster (smaller) builds** instead of the default **Faster runtime**, IL2CPP compiles one shared body for each generic method instead of one per value-type instantiation. In the 2026-10-17 measurement (Unity 6000.3.21f1, Android ARM64, release IL2CPP, managed stripping Minimal, Lua 5.5.1, no generated code), what xLua adds to the APK went from 1,397,146 to 1,280,734 bytes, 116,412 bytes less, and the empty project itself was 375,832 bytes smaller. Almost all of the saving is in the class library generics that xLua uses (collections, LINQ, delegates); xLua's own code changes by about 7 KB. The run time cost of fully shared generics was not measured, so measure your game on a device before you switch. xlua-ng does not change the setting for you; it is a project-wide choice. Details: [UNITY_SPIKE.md](../../../build/UNITY_SPIKE.md#q2-il2cpp-generic-sharing).

The generated wrappers have a smaller form, GCM (`GEN_CODE_MINIMIZE`): each wrapped member becomes a C# method behind one shared native entry. The **minimal preset turns it on by default**; in the legacy preset define `GEN_CODE_MINIMIZE` yourself. With 264 wrapped engine types (Unity 6000.3.21f1, Android ARM64 release) GCM made the APK about 628 KB smaller, and on a moto g play 2024 phone each Lua-to-C# call cost about 24 ns more over a mix of common calls (about 5%) ([descriptor-wrappers.md](../../../docs/research/descriptor-wrappers.md)). To keep the plain, faster wrappers in the minimal preset, define **`XLUA_NO_GCM`** next to `XLUA_PRESET_MINIMAL` ([Presets](Configure_EN.md#presets)).

## How do I diagnose "xlua.access, no field __Hitfix0_Update"?

This means the method in the assembly actually being run was not instrumented. Follow every step and precaution in the [Hotfix Guide](Hotfix_EN.md).

Possible causes include failed injection, a type missing from the `Hotfix` configuration, or a build script recompiling an assembly after successful injection and overwriting the result. Find and fix that underlying cause. Even a successful injection message does not prove that the final packaged assembly retained the injected code. The upstream [issue #850](https://github.com/Tencent/xLua/issues/850) provides additional troubleshooting context.

## What did the Visual Studio 2017 UWP build require?

The historical UWP instructions require the **Universal Windows Platform development** workload and the individual components for Visual C++ compilers and libraries for ARM and ARM64, plus the C++ Universal Windows Platform tools for ARM64. These are instructions for the legacy VS 2017 setup; use the requirements of the selected UWP script for other toolchains.

## How do I fix "please install the Tools"?

Place the `Tools` directory alongside `Assets`, at the Unity project root. The original English translation incorrectly said not to do this. The distribution and repository include `Tools`.

## How do I fix "This delegate/interface must add to CSharpCallLua: XXX"?

Configure the named delegate or interface type as `CSharpCallLua`. With the source generator, the next compilation adds its bridge.

In the editor without generated bindings, xLua emits the bridges for `CSharpCallLua` types at run time (Unity 2021.3 and later), except in the minimal preset, where the editor needs the generated bridges as players do. An IL2CPP player cannot emit them: if the editor works but the player fails, check with **XLua > Validate Setup** that the source generator ran for the assembly that declares the configuration, and that the configuration is not in an `Editor` folder, whose assembly is not in the player. An IL2CPP build with `CSharpCallLua` configuration in an assembly without generated bindings fails. See the historical Unity 2018 compatibility notes above where applicable.

## What does "T has no generated bindings" mean?

The project uses the minimal preset (`XLUA_PRESET_MINIMAL`), which turns off xLua's reflection fallback (`XLUA_NO_REFLECTION`), and Lua used a member of a C# type that the source generator did not bind. Mark the type `[LuaCallCSharp]`, or add `[assembly: XLua.LuaCallCSharp(typeof(T))]` for a type you cannot change; **XLua > Migrate Configuration to Attributes** converts old configuration lists. If the type is configured, check with **XLua > Validate Setup** that the generator ran for its assembly. To keep reflection in the preset, define `XLUA_REFLECTION`. See [MIGRATING.md](../../../docs/MIGRATING.md#reflection-off-in-the-minimal-preset).

The preset does not bind `System.Object` and `System.Type` for you, so `obj:ToString()`, `obj:GetType()`, `obj:Equals(x)` and `typeof(CS.T).Name` raise this error, with a hint. `tostring()` needs no bindings: `tostring(obj)` gives `obj.ToString()` followed by `: ` and the hash code, and `tostring(typeof(CS.T))` the type's name in the same form. To call their members, bind them once in a player assembly:

```csharp
[assembly: XLua.LuaCallCSharp(typeof(object), typeof(System.Type))]
```

`[ReflectionUse]` does not help here: in the minimal preset it only writes `link.xml` entries and does not open reflection for its type. The only way back to the reflection fallback is `XLUA_REFLECTION`, for the whole project.

## What does "T has no generated table converter" mean?

The project uses the minimal preset without `XLUA_REFLECTION`, and Lua passed a table where C# expects a class or struct `T`: a parameter, a field or property, `LuaTable.Get<T>` or `Cast<T>`, a delegate's or interface's result, or an element of a list. Upstream filled the object's public fields from the table through reflection; the preset does that only through code the source generator writes, which it writes for `[GCOptimize]` types: structs, and classes with a public parameterless constructor. Mark `T` `[GCOptimize]` (or add `[assembly: XLua.GCOptimize(typeof(T))]`), pass a `T` made in C# instead of a table, or define `XLUA_REFLECTION`. A `[GCOptimize]` struct whose fields Lua reads needs `[LuaCallCSharp]` too (warning XLSG014). See [MIGRATING.md](../../../docs/MIGRATING.md#reflection-off-in-the-minimal-preset).

## What does the Unity 5.5 .NET runtime warning mean?

The historical message `WARNING: The runtime version supported by this application is unavailable.` came from running the .NET 3.5 injection tool under Unity 5.5's MonoBleedingEdge environment. Upstream reported that backward compatibility allowed injection despite the warning.

The older `INJECT_WITHOUT_TOOL` embedded mode could avoid that warning, but was intended for debugging and could introduce library conflicts. Do not select it merely to suppress the message; see the [historical tooling notes](Hotfix_EN.md#historical-tooling-notes).

## How do I trigger an event from a hotfix?

Access its backing delegate using the `&` prefix, for example `self['&MyEvent']()`. Older xLua versions require `xlua.private_accessible` first; the hotfix guide notes that versions after 2.1.11 enable private access when applying a hotfix. See [events](Hotfix_EN.md#events) for details.

## How do I patch a Unity coroutine?

Return an enumerator built with `util.cs_generator`; see [Unity coroutines](Hotfix_EN.md#unity-coroutines).

## Are NGUI, UGUI, DOTween, and other C# libraries supported?

Generally, yes. xLua exposes C# APIs to Lua, so libraries usable from C# can generally be used from Lua. Configure wrappers, callbacks, and stripping preservation as required by the APIs you use.

## How should CustomLoader set filepath for debugging?

For `require 'a.b'`, the loader receives `"a.b"`. Resolve that module name, load its source from your chosen storage, and return the source as a UTF-8 `byte[]`. Set the `ref string filepath` argument to a path the debugger understands, such as `a/b.lua`. The updated argument supplies the debug source path; the return value supplies the source bytes.

## What is generated code?

Generated wrappers and bridges adapt calls between Lua and C#. They generally perform better than reflection and are recommended for frequently used APIs. Reflection reduces wrapper code size and can suit less performance-sensitive APIs, provided required metadata and AOT code are preserved. The xLua source generator writes them for the configured types while the scripts compile ([Configuration](Configure_EN.md#the-source-generator)).

## What if generated code breaks after an API changes?

The source generator writes the bindings again in every compilation, so they follow the code and the configuration; there is nothing to clear. Errors in files under `Assets/XLua/Gen` come from output of the removed **Generate Code** menu: delete the C# files there (`XLuaGenAutoRegister.cs`, `DelegatesGensBridge.cs`, `WrapPusher.cs`, `PackUnpack.cs`, `EnumWrap.cs`, the `*Wrap.cs` and `*Bridge.cs` files) and its `link.xml`, and keep its `Resources` folder if it holds hotfix id maps. **XLua > Validate Setup** reports such files; [MIGRATING.md](../../../docs/MIGRATING.md#generate-code-removed) has the other steps. An upgrade that copied the new `Assets/XLua` over an old one also leaves `Assets/XLua/Editor` (`ExampleConfig.cs`, `XLuaUnityDefaultConfig.cs`, configuration lists for Generate Code) behind; delete it.

## When should I generate code?

There is no generation step. With the source generator installed, every compilation writes the bindings, in the editor and in player builds. Without it, the editor emits wrappers and bridges at run time where it supports reflection emit, which is enough for development but not for AOT/mobile players. Profile with the generator installed, because wrapper and reflection performance differ substantially.

## Does exposing all C# APIs under CS use a lot of memory?

Types load lazily. For example, `UnityEngine.GameObject` members are loaded when Lua first accesses `CS.UnityEngine.GameObject` or receives the first instance of that type, rather than when the `CS` namespace is created.

## When do I use LuaCallCSharp and CSharpCallLua?

Follow the direction of the call. To call `GameObject.Find` or access a GameObject's methods and properties from Lua, configure `GameObject` as `LuaCallCSharp`. To use a Lua function as a C# UI callback, configure the callback's delegate type as `CSharpCallLua`.

For `List<int>.Find(Predicate<int> match)`, `List<int>` belongs in `LuaCallCSharp`, while `Predicate<int>` belongs in `CSharpCallLua`: the list implementation calls the Lua predicate from C#. `LuaCallSharp` in older prose is a typo; the attribute is `LuaCallCSharp`.

## Does transferring value types allocate managed garbage?

With the supported allocation-free delegate, `LuaTable`, `LuaFunction`, and array access paths, these values can cross the bridge without managed GC allocations:

1. Primitive numeric value types, including integer types, floating-point types, and `decimal`.
2. Enums.
3. Structs whose fields contain only value types, including nested structs meeting the same condition.

Configure enums and eligible structs with `GCOptimize`. This does not promise that all calling APIs or the surrounding application code are allocation-free.

## Can I use reflection on iOS?

Yes, but account for AOT compilation and stripping. C#-to-Lua delegate/interface adapters created with reflection emit require runtime code generation; generate `CSharpCallLua` bridges for AOT players instead.

For Lua-to-C# calls using reflection, configure `ReflectionUse`: the source generator writes `link.xml` preservation entries for those types, without `LuaCallCSharp` wrappers, and the player build passes them to UnityLinker. Generated C#-to-Lua bridges remain required; Lua-to-C# wrappers can often be replaced by reflection if preservation and AOT requirements are satisfied.

The minimal preset has no reflection fallback: there `ReflectionUse` only preserves its types, for `xlua.get_generic_method`, `xlua.tofunction` and, with `HOTFIX_ENABLE`, `xlua.private_accessible` and `xlua.access`, and Lua reaches other members only through `LuaCallCSharp` wrappers. Define `XLUA_REFLECTION` to use reflection in that preset.

## Can I call generic methods?

Methods constrained to a supported base class can be called as illustrated in [Example 9](../Examples/09_GenericMethod/). For unconstrained methods, a non-generic C# wrapper is often simplest. Wrap static methods with another static method, or use an extension method for instance methods:

```csharp
// C#
public static Button GetButton(this GameObject go)
{
    return go.GetComponent<Button>();
}
```

```lua
-- lua
local go = CS.UnityEngine.GameObject.Find("button")
go:GetButton().onClick:AddListener(function()
    print('onClick')
end)
```

Upstream versions after 2.1.12 also support explicit generic method construction through reflection:

```csharp
public class GetGenericMethodTest
{
    int a = 100;
    public int Foo<T1, T2>(T1 p1, T2 p2)
    {
        Debug.Log(typeof(T1));
        Debug.Log(typeof(T2));
        Debug.Log(p1);
        Debug.Log(p2);
        return a;
    }

    public static void Bar<T1, T2>(T1 p1, T2 p2)
    {
        Debug.Log(typeof(T1));
        Debug.Log(typeof(T2));
        Debug.Log(p1);
        Debug.Log(p2);
    }
}
```

```lua
local foo_generic = xlua.get_generic_method(CS.GetGenericMethodTest, 'Foo')
local bar_generic = xlua.get_generic_method(CS.GetGenericMethodTest, 'Bar')

local foo = foo_generic(CS.System.Int32, CS.System.Double)
local bar = bar_generic(CS.System.Double, CS.UnityEngine.GameObject)

-- call instance method
local o = CS.GetGenericMethodTest()
local ret = foo(o, 1, 2)
print(ret)

-- call static method
bar(2, nil)
```

The documented supported cases are Mono; IL2CPP with reference-type generic arguments; and IL2CPP with value-type arguments when C# already calls the same generic instantiation, so the required AOT code exists. Hotfixes often reuse such existing instantiations, but verify that the exact type arguments are present.

## Can Lua call overloaded C# methods?

Yes, but overload resolution is less precise than C#. Both `int` and `short` map to Lua numbers, so `Foo(int)` and `Foo(short)` cannot always be distinguished. Give an overload a distinct name through a C# wrapper or extension method. See the explicit overload selection example below.

An overload is a candidate when its parameters accept the arguments. An integer parameter (`int`, `long`, `byte` and the other integer types) accepts only a number with an integer value, as Lua 5.3's `math.tointeger` defines it: `3` and `3.0` match it, `2.75`, NaN and the infinities do not. When several candidates match, xLua calls the best match, as C# would choose for the Lua value's natural type, whatever the declaration order:

- A Lua integer prefers `int`, then `long`, then the other integer types, then `double`, `float` and `decimal`. An integer outside the `int` range prefers `long`. So `Foo(3)` calls `Foo(int)` and `Mathf.Max(3, 5)` calls `Max(int, int)`.
- A float prefers `double`, then `float`, then `decimal`: `Foo(2.75)` calls `Foo(double)`. On Lua 5.3 and later `3.0` (and `6 / 2`) is a float even though its value is an integer, so it prefers `Foo(double)` too; `Foo(math.tointeger(6 / 2))` or `Foo(6 // 2)` calls `Foo(int)`. LuaJIT has no integer subtype and treats `3.0` as `3`, so there `Foo(6 / 2)` calls `Foo(int)`.
- A string prefers `string` over `byte[]`. A C# object prefers its own class, then the nearest base class, then an interface. `object` is the last choice for any value, and `T` comes before `T?`.
- An overload that needs a `params` array or a default value loses to one that matches the arguments as well without it.
- For `nil`, tables and functions the first matching overload is called.

With several arguments the costs of the arguments add up, so where C# reports an ambiguous call xLua still calls one overload; equal costs keep the declaration order (generated wrappers put overloads with fewer non-primitive parameters first). A method without overloads raises `bad argument #1 to 'Foo' (number has no integer representation)` for `Foo(2.75)`; when no overload matches, the error is `invalid arguments to Foo`.

## Why do generated wrappers reference members missing in a player build?

The member may exist only in the Editor, under `UNITY_EDITOR` or another conditional compilation symbol, or only in the Editor's engine assemblies (upstream [issue #1094](https://github.com/Tencent/xLua/issues/1094)). The removed Generate Code reflected on the Editor's assemblies and could write calls to such members. The source generator compiles with each build's own reference assemblies and defines, so it wraps only the members that exist there. If errors name files under `Assets/XLua/Gen`, delete that Generate Code output (see above). To keep a member from Lua on every target, use `BlackList`; the by-name form only gives info `XLSG003` where the member is missing.

## Why do string/object indexers not work as dic[key]?

String and object indexers conflict with C# member lookup: a key such as `TryGetValue` would resolve to the method rather than dictionary data, and base-class members could become inaccessible. The old site guide records removal of this behavior in xLua 2.1.5–2.1.6.

Versions after 2.1.11 expose `get_Item` and `set_Item` for these string/object indexers:

```lua
dic:set_Item('a', 1)
dic:set_Item('b', 2)
print(dic:get_Item('a'))
print(dic:get_Item('b'))
```

Other key types do not use this particular fallback. In older versions, use an equivalent method such as `TryGetValue`, or expose a C# extension method.

## Why is a destroyed Unity object not nil in Lua?

`UnityEngine.Object` overloads `==`. A destroyed or uninitialized native object can compare equal to C# `null` even while its managed wrapper is a real reference. `System.Object.ReferenceEquals(null, obj)` distinguishes that case.

Expose Unity's null comparison through an extension method:

```csharp
[LuaCallCSharp]
[ReflectionUse]
public static class UnityEngineObjectExtention
{
    public static bool IsNull(this UnityEngine.Object o) // Or name this IsDestroyed, for example.
    {
        return o == null;
    }
}
```

Use it on Unity object instances:

```lua
print(go:GetComponent('Animator'):IsNull())
```

## How do I construct a closed generic type?

The modern syntax supported since upstream versions after 2.1.12 applies type arguments and then invokes the resulting type:

```lua
-- local List_String = CS.System.Collections.Generic['List<>'](CS.System.String) -- another way
local List_String = CS.System.Collections.Generic.List(CS.System.String)
local lst = List_String()

local Dictionary_String_Vector3 = CS.System.Collections.Generic.Dictionary(CS.System.String, CS.UnityEngine.Vector3)
local dic = Dictionary_String_Vector3()
dic:Add('a', CS.UnityEngine.Vector3(1, 2, 3))
print(dic:TryGetValue('a'))
```

The older syntax uses CLR type names. For types resolved through `mscorlib` and `Assembly-CSharp`, for example:

```lua
local lst = CS.System.Collections.Generic["List`1[System.String]"]()
```

Print `typeof(YourClosedType).ToString()` in C# if unsure of the name. Types in other assemblies can be constructed through reflection with assembly-qualified names:

```lua
local dic = CS.System.Activator.CreateInstance(CS.System.Type.GetType('System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[UnityEngine.Vector3, UnityEngine]],mscorlib'))
dic:Add('a', CS.UnityEngine.Vector3(1, 2, 3))
print(dic:TryGetValue('a'))
```

The assembly names in that legacy example depend on the Unity/.NET version; use the actual assemblies in your project. AOT builds also need the required generic instantiations.

## Why does LuaEnv.Dispose report "try to dispose a LuaEnv with C# callback!"?

C# still holds delegates referencing Lua functions. xLua refuses to dispose the VM because invoking those delegates afterward could cause an exception or crash.

Before disposal, clear members holding delegates obtained with `LuaTable.Get`, unregister Lua functions from C# events, and remove hotfix callbacks with `xlua.hotfix(class, method, nil)`.

A delegate counts as released only after the .NET garbage collector has collected it. On WebGL, Unity collects garbage only between frames, when the stack is empty, so `GC.Collect()` and the collection `Dispose` runs itself do not free delegates that were dropped in the same frame. Dispose the `LuaEnv` in a later frame, for example after `yield return null` in a coroutine. The WebGL smoke player (`Test/MobileSmoke`) does this; disposing in the same frame failed there with this message on Unity 6000.6.4f1.

To locate Lua functions still referenced by C#, use:

```lua
local util = require 'xlua.util'
util.print_func_ref_by_csharp()
```

Example output identifies source files and definition lines:

```bash
LUA: main.lua:2
LUA: main.lua:13
```

## Why does LuaEnv.Dispose throw?

`LuaEnv.Dispose` refuses in two cases:

- "try to dispose a LuaEnv with C# callback!": a delegate bridge (a Lua function held as a C# delegate, or an installed hotfix) is still reachable. Release the delegates and remove hotfixes with `xlua.hotfix(class, method, nil)` first.
- "a LuaEnv cannot be disposed while one of its calls runs": `Dispose` was called from a C# function that Lua called, also from a coroutine or a `__gc` handler. Return to C# and dispose it after the call has finished. Before xLua-NG this closed the state under the running call and crashed the process.

A bridge, `LuaTable` or `LuaFunction` used after `Dispose` throws `ObjectDisposedException`. A second `Dispose` does nothing.

## Does xLua work with Enter Play Mode Options (domain reload disabled)?

Yes. With **Reload Domain** disabled in Enter Play Mode Options, static fields keep their values from one play session to the next and static constructors do not run again. xLua's own statics need no reset: the loaders registered by generated code, the extension method map and the delegates passed to the native plugin stay valid for the whole domain, and `ObjectTranslatorPool` has to keep every env that outlives a session. What carries over is what your code leaves behind:

- Dispose every `LuaEnv` a session creates, for example in `OnDestroy`. An env you do not dispose keeps its native Lua state and everything Lua references, and its translator stays in `ObjectTranslatorPool`. A domain reload used to drop the C# side at the next play session; without one, every session adds another env.
- A `static LuaEnv`, like the one in the LuaBehaviour example, survives into the next session with its Lua globals. Reset it in your own startup code if a session must start clean.
- The hotfix table (`HotfixDelegateBridge`) is static too. A hotfix installed by an env you never dispose keeps calling that env in later sessions. Remove hotfixes with `xlua.hotfix(class, method, nil)` before `Dispose`, which refuses to run while any are installed.
- A `LuaTable`, `LuaFunction`, delegate or interface bridge from a disposed env throws `ObjectDisposedException` (an `InvalidOperationException`, "this lua env had disposed!") when used, and its `Dispose` does nothing. Before xLua-NG that `Dispose` threw the same exception, which broke play mode exit when one `OnDestroy` disposed the env before another disposed its tables.
- Without generated code, the editor emits wrappers and bridges into one dynamic assembly (`XLuaCodeEmit`), which every `LuaEnv` in the domain shares; later envs and play sessions reuse the types already emitted. Before xLua-NG each env emitted a new assembly, which .NET never unloads, so with domain reload disabled they added up, one per env per session, until the next script change reloaded the domain.

A script change in edit mode still reloads the domain. An env alive at that moment is lost to C# and its native state is never closed; dispose edit-mode envs before scripts recompile, for example from `AssemblyReloadEvents.beforeAssemblyReload`.

`Test/UnityMatrix/prepare.py --fast-enter-play-mode` adds editor tests that enter and exit play mode three times with domain reload disabled, with and without scene reload, with envs disposed and left undisposed, once with the default domain reload and once across a script recompile.

## How do I pass an int rather than long to a C# object parameter?

Integers passed to `object` default to `long`. See [Example 11](../Examples/11_RawObject/RawObjectTest.cs) for explicit raw-object type handling.

## Why is a C# long never equal to 1 on LuaJIT?

LuaJIT has no 64-bit integers, so a `long` or `ulong` arrives as a userdata, and Lua 5.1 never runs `__eq` or `__lt` for a userdata and a number: `x == 1` is always false and `x < 1` raises an error. Compare with `uint64.compare(x, 1) == 0` or `x < 0 * x + 1`, and use `tostring(x)` for table keys and `string.format`. The [64-bit integer section of the tutorial](XLua_Tutorial_EN.md) lists what works on LuaJIT and the alternatives for the rest.

## How do I run the original C# method from a patch?

Use `util.hotfix_ex`, which allows the patch to call the original implementation:

```lua
local util = require 'xlua.util'
util.hotfix_ex(CS.HotfixTest, 'Add', function(self, a, b)
   local org_sum = self:Add(a, b)
   print('org_sum', org_sum)
   return a + b
end)
```

## How do I assign a C# method directly to a delegate field?

In upstream versions through 2.1.8, treating the C# method as a Lua function introduced a C# → Lua → C# round trip. `xlua.util.createdelegate`, added in 2.1.9, creates a direct C# delegate instead:

```csharp
public class TestClass
{
    public void Foo(int a)
    {
    }

    public static void SFoo(int a)
    {
    }
}
public delegate void TestDelegate(int a);
```

```lua
local util = require 'xlua.util'

local d1 = util.createdelegate(CS.TestDelegate, obj, CS.TestClass, 'Foo', {typeof(CS.System.Int32)}) -- Foo is an instance method; argument 2 must be a TestClass instance.
local d2 = util.createdelegate(CS.TestDelegate, nil, CS.TestClass, 'SFoo', {typeof(CS.System.Int32)})

obj_has_TestDelegate.field = d1 + d2 -- Invoking field calls Foo and SFoo directly, without a Lua bridge.

```

## Why can Lua stop without displaying an error?

A coroutine reports errors through the return values of `coroutine.resume`. To propagate a failed resume, change:

```lua
coroutine.resume(co, ...)
```

to:

```lua
assert(coroutine.resume(co, ...))
```

Also check callers: an SDK or other outer layer may catch and suppress an exception raised during a callback.

## How do I select an ambiguous overload?

Ignored `out` arguments or numeric types such as `short` and `int` can make overloads ambiguous. The upstream guide reported `Physics.Raycast` as an example in September 2017. Prefer a distinctly named static wrapper or instance extension method.

If a hotfix needs an overload that was not wrapped beforehand, select its `MethodBase` using reflection and convert it with `xlua.tofunction`:

```csharp
class TestOverload
{
    public int Add(int a, int b)
    {
        Debug.Log("int version");
        return a + b;
    }

    public short Add(short a, short b)
    {
        Debug.Log("short version");
        return (short)(a + b);
    }
}
```

```lua
local m1 = typeof(CS.TestOverload):GetMethod('Add', {typeof(CS.System.Int16), typeof(CS.System.Int16)})
local m2 = typeof(CS.TestOverload):GetMethod('Add', {typeof(CS.System.Int32), typeof(CS.System.Int32)})
local f1 = xlua.tofunction(m1) -- Convert each MethodBase once, then reuse the function.
local f2 = xlua.tofunction(m2)

local obj = CS.TestOverload()

f1(obj, 1, 2) -- short overload; instance methods need an object, static methods do not.
f2(obj, 1, 2) -- int overload
```

Convert a given `MethodBase` once and reuse the function. Instance methods require the target object as their first argument; static methods do not. Because this approach uses reflection and is less convenient, prefer wrappers for planned APIs.

## Are extension methods on interfaces supported?

To limit generated code size, interface extension methods are not exposed as `obj:ExtensionMethod()`. Call them as static methods instead: `CS.ExtensionClass.ExtensionMethod(obj)`.

## How do I integrate wrapper generation into automated builds?

There is no generation step to integrate: the source generator writes the bindings while Unity compiles the scripts, also in batch mode. [Example 13](../Examples/13_BuildFromCLI/) builds a player from the command line with a custom Unity build method.

## Why does DelegatesGensBridge.cs reference editor-only types in a mobile hotfix build?

`DelegatesGensBridge.cs` was the hotfix bridge file of the removed Generate Code, which wrote it in the Editor, so it could name types and methods that a player build does not compile. Delete it with the rest of that output in `Assets/XLua/Gen` (keep `Resources`). The source generator writes the hotfix bridges in each compilation of the hotfixed type's own assembly, from the code that build compiles, so methods under `#if UNITY_EDITOR` get no bridges in a player build.

## How do I read and write JSON from Lua?

xLua-NG ships no JSON module and adds no native one. Use a pure-Lua library as
an ordinary Lua module, loaded through your own loader like any other script:

- [dkjson](http://dkolf.de/dkjson-lua/) (MIT-style, Lua 5.1 to 5.5, actively
  maintained): `json.null`, key order control, NaN and infinity as `null`.
- [lunajson](https://github.com/grafi-tt/lunajson) (MIT): faster, keeps
  integers exact through `math.tointeger`.

Pure Lua runs under every limit of the state (`MemoryLimit`,
`InstructionLimit`, `TimeLimit`), also inside `LuaScript`. Things to check:
none of them validates UTF-8 bytes or limits nesting depth, and integers above
2^63-1 become floats. If you decode untrusted input, bound its size before
parsing. For large or frequent documents, decoding in C# and handing a table to
Lua may be faster. The comparison behind this advice is in
[json-module.md](../../../docs/research/json-module.md).

## How do I load Lua bytecode?

Compile with the matching runtime's `luac` and load the result as you would source bytes. Upstream Lua 5.3 bytecode distinguishes 32-bit and 64-bit runtimes; see [Compatible Bytecode](compatible_bytecode.md) for that runtime's compatibility option.

This fork's compatibility-format extension is Lua 5.3-only. Lua 5.4, Lua 5.5, and LuaJIT bytecode must match the selected runtime and its format requirements; do not assume the Lua 5.3 option makes other bytecode portable. See [build instructions](../../../build/README.md).

## How are C# objects held by Lua released?

Remove all Lua references to the object and allow a Lua GC cycle to collect its wrapper. This releases Lua's reference; the C# object remains alive if other managed references exist.

Lua does not run a background GC thread. Collection normally advances during allocations, so waiting while Lua is idle, or while it allocates nothing, does not necessarily release references. A small Lua wrapper can retain a large managed or native resource such as a texture, making Lua's own memory usage a poor indicator of retained resource size. The original guide described the wrapper's reference bookkeeping as four bytes; that is historical implementation detail rather than a total per-object memory guarantee.

The historical incremental collector settings used `GcPause = 200` to start the next cycle around twice the memory at the previous cycle's end and `GcStepmul = 200` to control collection effort. The upstream guide cited Cocos2d-x values of 100 and 5000 as examples, not universal recommendations. Tune for your runtime and collector mode; Lua 5.4/5.5 behavior differs from Lua 5.3.

At a suitable point such as a scene transition, request a full Lua collection with `LuaEnv.FullGc()` or `collectgarbage('collect')`. Measure the pause before putting full collections on a frequently executed path.

## Why does `tostring` of a C# object print a comma as the decimal separator?

`tostring(obj)` on a C# object returns `obj.ToString()` followed by `: ` and the object's hash code, for debugging. `ToString()` formats numbers and dates with the thread's current culture, as it does everywhere in .NET, so a `decimal` or `DateTime` prints `1,5` on a device set to Turkish or German. Lua numbers are not affected: a C# `float`, `double` or `int` becomes a Lua number, which Lua formats the same on every device.

When the text must not depend on the device, ask for a culture explicitly, as in C#:

```lua
local invariant = CS.System.Globalization.CultureInfo.InvariantCulture
print(obj:ToString(nil, invariant))   -- decimal, double, DateTime and other IFormattable types
```

## How do I diagnose crashes when multiple threads use xLua?

A `LuaEnv` is not thread-safe. In the Editor, Development Builds and outside Unity (`XLUA_GENERAL`) it belongs to the first thread that uses it after the constructor (not to the thread that created it), and a call from any other thread throws `InvalidOperationException` ("this LuaEnv belongs to thread 1 but is used from thread 7 ...") instead of corrupting the Lua heap. Release players leave the check out. Then:

- An env created on one thread and used only on another, for example in a field initializer or static constructor that Unity runs on a loading thread, needs nothing: the first thread that uses it owns it.
- To move an env that one thread has used to another thread, for example a loader thread that ran the scripts and hands the env to the main thread, call `env.SetOwnerThread()` on the new thread once the old one is done with it. `LuaScript.SetOwnerThread()` does the same for a script.
- To use one env from several threads, define `THREAD_SAFE` in the scripting define symbols: every call then takes the env's lock, and the check is off. `HOTFIX_ENABLE` takes the same lock.
- Do not call Lua from a C# finalizer: finalizers run on their own thread. The finalizers of xLua's `LuaTable`, `LuaFunction` and delegates are fine; they only queue the release for the next `LuaEnv.Tick`.
- Watch for other hidden threads: asynchronous socket callbacks, `Task.Run` continuations, `Application.logMessageReceivedThreaded`. Unity may run a MonoBehaviour's constructor and field initializers off the main thread while it loads a scene; creating an env there is fine, but run Lua from `Awake` or `Start`, not from the initializer.
- `Interrupt()` and the statistics (`MemoryUsed`, `MemoryPeak`, the limits) work from any thread. `LuaOptions.CheckThread = false` turns the check off for one env.

## What if macOS 10.15 or later reports xlua.bundle as damaged?

For a trusted plugin blocked by macOS quarantine, the original workaround removes the quarantine attribute from that bundle. Replace the path below with the actual trusted bundle path:

```sh
xattr -r -d com.apple.quarantine /absolute/path/to/xlua.bundle
```

The upstream command used `sudo`; elevated privileges are only necessary if file permissions require them. For this fork, first rebuild the matching native plugin as described in [build/README.md](../../../build/README.md).
