# Frequently asked questions

## How do I use an xLua distribution package?

Extract the distribution zip into your Unity project directory. For this fork, also follow the repository README and native plugin compatibility instructions in [build/README.md](../../../build/README.md); this repository ships no prebuilt native plugins, so build or download one for your runtime and platform.

## Can I place xLua in another directory?

Yes. Configure `GenPath` to change the generated code directory (the default is `Assets/XLua/Gen`); see [Configuration](Configure_EN.md). Generated code and xLua core code must be in the same assembly. The standard Unity hotfix tooling expects xLua core code in `Assembly-CSharp`; moving files into assembly definitions requires checking that tooling, rather than merely changing `GenPath`.

## How should I configure xLua?

For Lua scripting, put frequently accessed C# types in `LuaCallCSharp`. Exclude members available only under conditional compilation (often `UNITY_EDITOR`) using `BlackList`. Put delegate types used for C# callbacks into Lua in `CSharpCallLua`.

For hotfixes, put types to instrument in the `Hotfix` list. Delegate types used to call back into Lua still need `CSharpCallLua`.

Dynamic configuration can use reflection to automate these lists; see [Configuration](Configure_EN.md). [ExampleConfig.cs](../Editor/ExampleConfig.cs) contains commented reference configurations for both workflows. Enable the relevant sections and adapt them to your project.

## Must Lua source files have a .txt extension?

No. xLua can load files with any extension. Unity does not import `.lua` files as `TextAsset`, so scripts bundled as text assets, for example under `Resources`, use `.lua.txt` in the examples.

Files downloaded or otherwise stored outside Unity's text asset import pipeline have no such extension restriction. Load them through a `CustomLoader` or an appropriate `package.path`. xLua is a library and does not provide a download system; text assets make the bundled examples easy to run.

## Why does iOS report "attempt to call a nil value" when the editor works?

IL2CPP can strip engine APIs, .NET APIs, or third-party DLL members that C# does not reference. An API available in the editor may therefore be absent from the player.

Preserve the API by adding a C# reference, generating wrappers through `LuaCallCSharp`, or configuring `link.xml`. `ReflectionUse` configuration causes xLua's generation step to write preservation entries to `link.xml`.

## What are the historical Unity 2018 compatibility issues?

The upstream guide recommends upgrading versions older than xLua 2.1.14 to at least 2.1.14. It documents two Unity 2018 issues:

1. The .NET Standard 2.0 compatibility profile has no Reflection.Emit, so xLua compiles its emit code (`CodeEmit`) out under that profile; without generated code, delegates and interfaces then fail with "This type must add to CSharpCallLua" and types fall back to reflection. The historical workaround was to use .NET 4.x during editor development. Unity 2021.2 replaced that profile with .NET Standard 2.1, which has the emit APIs: in Unity 2021.3 and later the editor emits wrappers and bridges under both .NET Standard 2.1 and .NET Framework.
2. Unity 2018's .NET 4.x Equivalent runtime and compiler reference assemblies exposed different APIs. The documented Windows installation paths were `Editor/Data/MonoBleedingEdge/lib/mono/unityjit/mscorlib.dll` at runtime and `Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/mscorlib.dll` at compilation. Generated wrappers could reference methods absent from the compiler's assembly.

Exclude those methods with `BlackList`. Before August 6, 2019, generic blacklist entries had to be repeated for each closed type, such as `Dictionary<int, int>` and `Dictionary<float, int>`. Later upstream versions support predicate filters; see the Dictionary filter in [ExampleConfig.cs](../Editor/ExampleConfig.cs) and adapt it for other affected generic types.

## Where is the native plugin source, and how do I build it?

Native sources and build scripts are under [`build`](../../../build). Read [native plugin compatibility and build instructions](../../../build/README.md) first. This fork provides Lua 5.3, 5.4, 5.5, and LuaJIT scripts; choose the runtime explicitly and use a separate output directory for each runtime.

Scripts generally follow `make_<platform>_<runtime>.sh` or `.bat`; inspect the selected script for its dependencies. CMake is required. iOS and macOS builds require a Mac; Android builds require Android NDK r30: set `ANDROID_NDK` to it. The four Android scripts (Lua 5.3, 5.4, 5.5 and LuaJIT) build armeabi-v7a, arm64-v8a, x86 and x86_64 at API 21 with the NDK's own toolchain.

## How do I diagnose "xlua.access, no field __Hitfix0_Update"?

This means the method in the assembly actually being run was not instrumented. Follow every step and precaution in the [Hotfix Guide](Hotfix_EN.md).

Possible causes include failed injection, a missing type in the `Hotfix` list, or a build script recompiling an assembly after successful injection and overwriting the result. Find and fix that underlying cause. Even a successful injection message does not prove that the final packaged assembly retained the injected code. The upstream [issue #850](https://github.com/Tencent/xLua/issues/850) provides additional troubleshooting context.

## What did the Visual Studio 2017 UWP build require?

The historical UWP instructions require the **Universal Windows Platform development** workload and the individual components for Visual C++ compilers and libraries for ARM and ARM64, plus the C++ Universal Windows Platform tools for ARM64. These are instructions for the legacy VS 2017 setup; use the requirements of the selected UWP script for other toolchains.

## How do I fix "please install the Tools"?

Place the `Tools` directory alongside `Assets`, at the Unity project root. The original English translation incorrectly said not to do this. The distribution and repository include `Tools`.

## How do I fix "This delegate/interface must add to CSharpCallLua: XXX"?

Add the named delegate or interface type to `CSharpCallLua`. If code was generated before that change, clear it and regenerate it.

In the editor without generated code, xLua emits the bridges for `CSharpCallLua` types at run time (Unity 2021.3 and later). If the message persists after adding the type, the project still has generated code from before the change; clear it or generate again. Before an AOT/mobile build, execute **XLua/Generate Code**. If the editor works but the player fails, check that generation happened for the final configuration. See the historical Unity 2018 compatibility notes above where applicable.

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

Generated wrappers and bridges adapt calls between Lua and C#. They generally perform better than reflection and are recommended for frequently used APIs. Reflection reduces wrapper code size and can suit less performance-sensitive APIs, provided required metadata and AOT code are preserved.

## What if generated code breaks after an API changes?

Use **Clear Generated Code**, wait for compilation, then generate again. If that menu is unavailable after restarting, remove the configured generated code directory manually and regenerate after compilation completes.

## When should I generate code?

Where the editor supports reflection emit, developing without generated code avoids stale wrapper errors and wrapper compilation time. Generate code before AOT/mobile builds, preferably as part of the build pipeline. Generate it before profiling or performance testing as well, because wrapper and reflection performance differ substantially.

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

For Lua-to-C# calls using reflection, configure `ReflectionUse` and run **Generate Code** to create `link.xml` preservation entries without generating `LuaCallCSharp` wrappers for those types. Generated C#-to-Lua bridges remain required; Lua-to-C# wrappers can often be replaced by reflection if preservation and AOT requirements are satisfied.

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

## Why do generated wrappers reference members missing in a player build?

The member may exist only under `UNITY_EDITOR` or another conditional compilation symbol. Exclude it using `BlackList`, wait for compilation, and regenerate code for the intended build configuration.

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

## Why does LuaEnv.Dispose crash?

Do not dispose the VM from a Lua-driven call while Lua is still executing. Return to C# and dispose it only after execution has finished and callbacks have been released.

## Does xLua work with Enter Play Mode Options (domain reload disabled)?

Yes. With **Reload Domain** disabled in Enter Play Mode Options, static fields keep their values from one play session to the next and static constructors do not run again. xLua's own statics need no reset: the loaders registered by generated code, the extension method map and the delegates passed to the native plugin stay valid for the whole domain, and `ObjectTranslatorPool` has to keep every env that outlives a session. What carries over is what your code leaves behind:

- Dispose every `LuaEnv` a session creates, for example in `OnDestroy`. An env you do not dispose keeps its native Lua state and everything Lua references, and its translator stays in `ObjectTranslatorPool`. A domain reload used to drop the C# side at the next play session; without one, every session adds another env.
- A `static LuaEnv`, like the one in the LuaBehaviour example, survives into the next session with its Lua globals. Reset it in your own startup code if a session must start clean.
- The hotfix table (`HotfixDelegateBridge`) is static too. A hotfix installed by an env you never dispose keeps calling that env in later sessions. Remove hotfixes with `xlua.hotfix(class, method, nil)` before `Dispose`, which refuses to run while any are installed.
- A `LuaTable`, `LuaFunction` or interface bridge from a disposed env throws `InvalidOperationException` ("this lua env had disposed!") when used, and its `Dispose` does nothing. Before xLua-NG that `Dispose` threw the same exception, which broke play mode exit when one `OnDestroy` disposed the env before another disposed its tables.
- Without generated code, each `LuaEnv` in the editor emits its wrappers and bridges into a new dynamic assembly (`XLuaCodeEmit`), and .NET never unloads one. With domain reload disabled they add up, one per env per session, until the next script change reloads the domain. Generate code if you enter play mode many times in a row.

A script change in edit mode still reloads the domain. An env alive at that moment is lost to C# and its native state is never closed; dispose edit-mode envs before scripts recompile, for example from `AssemblyReloadEvents.beforeAssemblyReload`.

`Test/UnityMatrix/prepare.py --fast-enter-play-mode` adds editor tests that enter and exit play mode three times with domain reload disabled, with and without scene reload, with envs disposed and left undisposed, once with the default domain reload and once across a script recompile.

## How do I pass an int rather than long to a C# object parameter?

Integers passed to `object` default to `long`. See [Example 11](../Examples/11_RawObject/RawObjectTest.cs) for explicit raw-object type handling.

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

See [Example 13](../Examples/13_BuildFromCLI/), which invokes a custom Unity build method from the command line.

## Why does DelegatesGensBridge.cs reference editor-only types in a mobile hotfix build?

A type in the `Hotfix` list (type A) references a type unavailable in the player (type B). Exclude type A from the hotfix configuration, rather than excluding only type B.

Use **Find All References** on the unavailable type to locate the methods, properties, and other members referencing it; their declaring types are the candidates to remove from the hotfix list.

## How do I load Lua bytecode?

Compile with the matching runtime's `luac` and load the result as you would source bytes. Upstream Lua 5.3 bytecode distinguishes 32-bit and 64-bit runtimes; see [Compatible Bytecode](compatible_bytecode.md) for that runtime's compatibility option.

This fork's compatibility-format extension is Lua 5.3-only. Lua 5.4, Lua 5.5, and LuaJIT bytecode must match the selected runtime and its format requirements; do not assume the Lua 5.3 option makes other bytecode portable. See [build instructions](../../../build/README.md).

## How are C# objects held by Lua released?

Remove all Lua references to the object and allow a Lua GC cycle to collect its wrapper. This releases Lua's reference; the C# object remains alive if other managed references exist.

Lua does not run a background GC thread. Collection normally advances during allocations, so waiting while Lua is idle, or while it allocates nothing, does not necessarily release references. A small Lua wrapper can retain a large managed or native resource such as a texture, making Lua's own memory usage a poor indicator of retained resource size. The original guide described the wrapper's reference bookkeeping as four bytes; that is historical implementation detail rather than a total per-object memory guarantee.

The historical incremental collector settings used `GcPause = 200` to start the next cycle around twice the memory at the previous cycle's end and `GcStepmul = 200` to control collection effort. The upstream guide cited Cocos2d-x values of 100 and 5000 as examples, not universal recommendations. Tune for your runtime and collector mode; Lua 5.4/5.5 behavior differs from Lua 5.3.

At a suitable point such as a scene transition, request a full Lua collection with `LuaEnv.FullGc()` or `collectgarbage('collect')`. Measure the pause before putting full collections on a frequently executed path.

## How do I diagnose crashes when multiple threads use xLua?

Enable `THREAD_SAFE` in the relevant Unity scripting define symbols when xLua is accessed from multiple threads. The source also enables its locking path with `HOTFIX_ENABLE`. Watch for less obvious entry points such as asynchronous C# socket callbacks or finalizers, and coordinate VM lifetime with callers.

## What if macOS 10.15 or later reports xlua.bundle as damaged?

For a trusted plugin blocked by macOS quarantine, the original workaround removes the quarantine attribute from that bundle. Replace the path below with the actual trusted bundle path:

```sh
xattr -r -d com.apple.quarantine /absolute/path/to/xlua.bundle
```

The upstream command used `sudo`; elevated privileges are only necessary if file permissions require them. For this fork, first rebuild the matching native plugin as described in [build/README.md](../../../build/README.md).
