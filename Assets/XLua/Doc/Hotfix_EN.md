# Hotfix guide

Hotfixes replace or augment C# behavior with Lua implementations. The target assemblies must be instrumented before release; a Lua patch cannot add an injection point to an already shipped assembly.

## Usage

0. Build the injector once from the project root with `python3 Tools/build_tools.py` (requires Mono's `mcs`). The editor runs `Tools/XLuaHotfixInject.exe`; the repository does not ship a prebuilt copy. The script downloads Mono.Cecil 0.11.6 from nuget.org, checks its SHA-512 and copies it next to the injector; set `XLUA_TOOLS_CACHE` (or `--cache`) to reuse one download. Without access to nuget.org, `--cecil-dir <folder>` takes the DLLs from Unity's `com.unity.nuget.mono-cecil` 1.11.5 or 1.11.6 package instead (for example `Library/PackageCache/com.unity.nuget.mono-cecil@...`); only that build is accepted, by SHA-256. It is Cecil 0.11.5, which can write wrong line numbers into portable PDBs for methods whose `#line` numbers go back by 8192 or more, so the NuGet default is preferred.
1. Enable `HOTFIX_ENABLE` in Unity's scripting define symbols for each relevant build target before compilation. The original guide calls this **File → Build Settings → Scripting Define Symbols**; the exact location depends on Unity's version. Its automated-build warning is that setting the symbol too late through an API does not affect assemblies already compiled. Configure the editor/build target in advance and wait for compilation.
2. Run **XLua/Generate Code**.
3. Instrument the assemblies. The standard mobile build integration performs injection during the build. When developing a patch in the editor, run **XLua/Hotfix Inject In Editor** manually. Success is indicated by `hotfix inject finish!` or `had injected!`; otherwise inspect the reported errors.

The upstream recommendation is to leave `HOTFIX_ENABLE` disabled during ordinary business-code development and enable it when building a hotfix-capable player or testing a patch.

The injector keeps debug symbols: it rewrites the `.pdb` or `.mdb` file next to the assembly, so stack traces keep their line numbers. If the symbol file cannot be read, it prints `ignoring debug symbols ...` and injects without it.

`Test/Native/hotfix_check.py` runs this workflow outside Unity with Mono, from injection to patching and removing patches; see [Test/README.md](../../../Test/README.md).

If `xlua.hotfix` still reports `xlua.access, no field __Hitfix0_Update` after a success message, check that the type is in the `Hotfix` configuration and that no subsequent compilation overwrote the instrumented assembly. Verify the assembly used by the final player, not just an earlier build artifact. See the [FAQ](Faq_EN.md) for related diagnostics.

## Constraints

Static constructors are not supported.

The standard Unity workflow described here patches project code under `Assets`, not engine or .NET system libraries. It expects xLua core code in `Assembly-CSharp`. Configurations involving other project assemblies must use tooling and configuration appropriate to those assemblies; do not infer engine-library support from that capability.

## API

### xlua.hotfix(class, [method_name], fix)

Installs a Lua patch.

* `class`: a C# type, represented as `CS.Namespace.TypeName` or a string accepted by C# `Type.GetType`, such as `"Namespace.TypeName"`. For a non-public nested type, use the string form `"Namespace.TypeName+NestedTypeName"`.
* `method_name`: optional method name.
* `fix`: a Lua function when `method_name` is supplied; otherwise a table mapping method names to Lua functions. Pass `nil` in the single-method form to remove a patch.

### base(csobj)

Returns a proxy through which a patched override can invoke its base-class implementation. `csobj` is the C# instance.

Example from [HotfixTest2.cs](../Examples/08_Hotfix/HotfixTest2.cs):

```lua
xlua.hotfix(CS.BaseTest, 'Foo', function(self, p)
    print('BaseTest', p)
    base(self):Foo(p)
end)
```

The older website guide said base calls were unsupported. That is historical: this source includes base proxies. `NoBaseProxy` explicitly disables their generation.

### xlua.private_accessible(class)

Enables access to a type's private fields, properties, and methods. The type argument has the same forms as for `xlua.hotfix`. Upstream versions after 2.1.11 enable private access while applying a hotfix, so a separate call is normally unnecessary in that workflow.

### util.hotfix_ex(class, method_name, fix)

An enhanced form of `xlua.hotfix` that allows `fix` to call the original C# method. `method_name` is a string and `fix` is a Lua function. Import the utility with `local util = require 'xlua.util'`.

This adds overhead: the utility temporarily removes the patch while the callback runs, then reinstalls it afterward. See the [FAQ example](Faq_EN.md#how-do-i-run-the-original-c-method-from-a-patch).

## Configure types to patch

Like other xLua configuration, hotfix types can be specified in two ways:

1. Add `[Hotfix]` directly to a type. The upstream guide discourages this outside demonstrations and warns that it is unsupported in newer Unity versions.
2. Put a `[Hotfix]` list on a static field or property in a static configuration class. This is the recommended workflow. A property can select types using reflection, for example by namespace.

Keep the configuration under an `Editor` directory, particularly for newer Unity versions or when configuring assemblies other than `Assembly-CSharp`. The historical guide does not identify an exact Unity version boundary for its direct-attribute warning; use the editor configuration rather than treating an unspecified boundary as a tested compatibility claim. The examples below retain direct attributes for illustration; configure those types through your editor list when required.

The configuration example requires the `System`, `System.Collections.Generic`, `System.Linq`, `System.Reflection`, and `XLua` namespaces:

~~~csharp
public static class HotfixCfg
{
    [Hotfix]
    public static List<Type> by_field = new List<Type>()
    {
        typeof(HotFixSubClass),
        typeof(GenericClass<>),
    };

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
}
~~~

## Hotfix flags

`HotfixFlag` values customize wrapper generation and instrumentation. Combine flags in the hotfix configuration as needed.

### Stateless and Stateful

`Stateless` is the default (`0`) and passes the C# instance as `self`. `Stateful` is obsolete and rejected by the current attribute definition; use `xlua.util.state` instead. See [HotfixTest2.cs](../Examples/08_Hotfix/HotfixTest2.cs).

The old website's Stateful mode stored a Lua table per instance, returned by the patched constructor. Objects constructed before the patch had no initialized Lua state, so patches needed to run before object creation. This describes the removed mode, not a current configuration option. With `util.state`, initialize the Lua-side state explicitly for each object that needs it; the entire-type example below does so in the constructor patch.

### ValueTypeBoxing

Uses `object` in adaptation delegates for value types, reducing generated code size at the cost of boxing and managed garbage collection. Consider this when native code size (the text segment) matters more than allocation cost.

### IgnoreProperty

Skips property accessor instrumentation and its adaptation code. The upstream guide recommends this for simple, low-risk properties; use it only if those properties do not need patches.

### IgnoreNotPublic

Skips all non-public methods. Private Unity callbacks invoked through reflection, such as MonoBehaviour lifecycle methods, also need injection if you want to patch them; this flag does not exempt them automatically. For private helpers called only by your public methods, you can instead patch every public caller, at the cost of duplicating more logic.

### Inline

Injects the bridge handling directly into method bodies rather than generating adaptation delegates.

### Additional flags in this source

* `AdaptByDelegate` selects delegate adaptation for eligible non-`IntKey` injection points instead of direct same-assembly bridge calls.
* `IgnoreCompilerGenerated` skips types and methods marked with `CompilerGeneratedAttribute`.
* `NoBaseProxy` skips generation of the base-call proxies used by `base(self)`.

These definitions are in [GenAttributes.cs](../Src/GenAttributes.cs), and their injection behavior is implemented in [Hotfix.cs](../Src/Editor/Hotfix.cs).

### IntKey

Stores injection points in a shared array instead of individual static fields, reducing impact on native code size. Injection assigns numeric IDs, so the patch must use the mapping belonging to the exact released build. A generic type definition cannot be marked `IntKey`; the injector rejects that combination.

The function-to-ID mapping is written to `Gen/Resources/hotfix_id_map.lua.txt`, with timestamped backups beside it. Preserve this file with each released player build.

The mapping has this form. If no types use `IntKey`, the file returns an empty table:

~~~lua
return {
    ["HotfixTest"] = {
        [".ctor"] = {
            5
        },
        ["Start"] = {
            6
        },
        ["Update"] = {
            7
        },
        ["FixedUpdate"] = {
            8
        },
        ["Add"] = {
            9,10
        },
        ["OnGUI"] = {
            11
        },
    },
}
~~~

To replace `HotfixTest.Update` for this example mapping:

~~~lua
CS.XLua.HotfixDelegateBridge.Set(7, func)
~~~

Overloaded methods map to multiple IDs, as shown by `Add` above.

Call `xlua.util.auto_id_map()` once to install name-based lookup, then use `xlua.hotfix` with type and method names:

~~~lua
(require 'xlua.util').auto_id_map()
xlua.hotfix(CS.HotfixTest, 'Update', function(self)
        self.tick = self.tick + 1
        if (self.tick % 50) == 0 then
            print('<<<<<<<<Update in lua, tick = ' .. self.tick)
        end
    end)
~~~

The mapping must be loadable through `require 'hotfix_id_map'`.

#### Several hotfix assemblies

When hotfix types are in more than one assembly (for example `Assembly-CSharp` and an assembly definition), the injector:

* writes one map per assembly, `Gen/Resources/hotfix_id_map_<assembly>.lua.txt`, each with timestamped backups;
* then writes `Gen/Resources/hotfix_id_map.lua.txt` as the union of those maps. It holds their contents, so `require 'hotfix_id_map'` and `auto_id_map()` need no other file at run time. Ship this file, as with a single assembly.

With "Generate Code", `IntKey` works only in the assembly that contains xLua, which is `Assembly-CSharp` in the default layout. In any other assembly, types marked `IntKey` get field mode instead: one static field per method, and no IDs in the maps. `xlua.hotfix` still patches them by name. The injector then logs a warning that names the types:

~~~text
IntKey falls back to field mode in Game.dll for 2 types: Game.Shop, Game.Bag. IntKey works only in the assembly that holds xLua (Assembly-CSharp), ...
~~~

To use `IntKey` for those types, use the xLua source generator for their assembly, or move them into the assembly that contains xLua. To silence the warning, remove `HotfixFlag.IntKey` from their configuration.

With the source generator (preview, [source-generator.md](../../../docs/design/source-generator.md) milestone M7), every assembly with `[Hotfix]` types gets hotfix bridges and `IntKey` slots of its own: `IntKey` works in that assembly with IDs numbered from 0 there, and its entries in the map name the assembly:

~~~lua
    ["Game.Shop"] = {
        ["<assembly>"] = "Game",
        ["Buy"] = {
            0
        },
    },
~~~

`xlua.util.auto_id_map()` handles both kinds of entry. To set such an ID directly, pass the assembly: `CS.XLua.HotfixDelegateBridge.SetSlot('Game', 0, func)`.

`[Hotfix]` on a type and `[assembly: Hotfix(typeof(T), flags)]` work in every assembly (before 2026-10-18 the injector saw a `[Hotfix]` attribute only inside the xLua assembly). `Inline` types and generic methods are injected in every assembly too; outside the xLua assembly they used to be skipped without a warning. A `[Hotfix]` configuration list (see [Configure types to patch](#configure-types-to-patch)) for types in other assemblies belongs under an `Editor` folder: the injector reads the configuration of the assembly it injects and of the Editor assembly, so a list in `Assembly-CSharp` that names `Game.Shop` leaves `Game.Shop` uninjected, with an empty `hotfix_id_map_Game` and no warning.

This layout (`Assembly-CSharp`, an asmdef `Game`, the list in an Editor folder, Hotfix Inject In Editor, then `auto_id_map()` and `xlua.hotfix` in play mode) passed in Unity 2021.3.45f2 and 6000.3.26f1 on 2026-10-18 (`Test/UnityMatrix/hotfix_intkey.py`). Build-time injection into a player was not run.

Before xlua-ng, `hotfix_id_map.lua.txt` was not written when there were several hotfix assemblies, so `auto_id_map()` failed with "module 'hotfix_id_map' not found", or loaded a stale map left by an earlier single-assembly build (see [MIGRATING.md](../../../docs/MIGRATING.md#tooling)).

Outside Unity, inject each assembly with `XLuaHotfixInject` and its own map, then merge the maps:

~~~text
XLuaHotfixInject --merge-id-maps hotfix_id_map.lua.txt hotfix_id_map_Game.lua.txt hotfix_id_map_Tools.lua.txt
~~~

The older site guide notes that Unity may already have finished importing new resources when build-time injection writes this file. Ensure the matching map is included in your patch delivery/loading pipeline. Do not substitute an editor-generated map: conditional compilation and platform-only APIs can assign different IDs in editor and player builds.

## Usage suggestions

* Configure `Hotfix` for types likely to need changes.
* Use reflection-based configuration to find delegate types used in method parameters, fields, properties, and events; configure them as `CSharpCallLua` when Lua callbacks are needed.
* Configure `LuaCallCSharp` for business, engine, and .NET APIs requiring fast access from Lua patches.
* Preserve engine and .NET APIs a future patch may call. Use `LuaCallCSharp` wrappers or `ReflectionUse` preservation and run generation before release; APIs unreferenced by C# can otherwise be stripped.

## Patching

Lua functions implement method patches, including property accessors and event subscription methods. Constructors and finalizers augment rather than replace the original implementation, as described below.

### Methods

Pass the method name as `method_name`. All overloads of that name are forwarded to the same Lua function.

For example:

```csharp
// C# type to patch
[Hotfix]
public class HotfixCalc
{
    public int Add(int a, int b)
    {
        return a - b;
    }

    public Vector3 Add(Vector3 a, Vector3 b)
    {
        return a - b;
    }
}
```

```lua
xlua.hotfix(CS.HotfixCalc, 'Add', function(self, a, b)
    return a + b
end)
```

Instance methods receive `self` as their first Lua argument: the C# instance, corresponding to `this`. Static methods do not receive an instance argument.

Ordinary parameters become Lua arguments. A `ref` parameter supplies a Lua argument and receives a Lua return value; an `out` parameter receives a Lua return value without supplying an argument.

Generic methods follow the same patching rules as ordinary methods.

### Constructors

Use `".ctor"` as the method name.

A constructor patch runs after the original constructor logic; it does not replace that logic.

### Properties

For a property named `AProp`, patch `get_AProp` for its getter and `set_AProp` for its setter.

### Indexers

Patch `set_Item(self, key, value)` for assignment and `get_Item(self, key)` for retrieval. The getter returns the retrieved value.

### Other operators

C# operators have CLR method names. For example, patching `op_Addition` replaces the `+` operator implementation. Use the corresponding CLR name for other operators.

### Events

For an instance event `AEvent`, patch `add_AEvent(self, callback)` for `+=` and `remove_AEvent(self, callback)` for `-=`. The callback argument is the supplied delegate. Static event accessors do not receive `self`.

Enable private access with `xlua.private_accessible` when required (upstream versions after 2.1.11 do this during hotfix application). Invoke an event's backing delegate through `self['&MyEvent']()`, replacing `MyEvent` with the event name and supplying its arguments.

### Finalizers

Use `"Finalize"` as the method name; the Lua function receives `self`.

The Lua patch runs before the original finalizer logic, which then continues.

### Generic types

Each closed generic type is a separate runtime type and must be patched separately. For example:

```csharp
public class GenericClass<T>
{
}
```

Patch `GenericClass<double>` or `GenericClass<int>`, not the open `GenericClass<>` definition. You can still list the open definition in the editor hotfix configuration so its code is instrumented.

For example, patch `GenericClass<double>` as follows:

```csharp
luaenv.DoString(@"
    xlua.hotfix(CS.GenericClass(CS.System.Double), {
        ['.ctor'] = function(obj, a)
            print('GenericClass<double>', obj, a)
        end;
        Func1 = function(obj)
            print('GenericClass<double>.Func1', obj)
        end;
        Func2 = function(obj)
            print('GenericClass<double>.Func2', obj)
            return 1314
        end
    })
");
```

### Unity coroutines

`util.cs_generator` builds an `IEnumerator` implementation from a Lua function. Inside it, `coroutine.yield` corresponds to C# `yield return`. The following C# method and Lua patch have equivalent behavior:

~~~csharp
[XLua.Hotfix]
public class HotFixSubClass : MonoBehaviour {
    IEnumerator Start()
    {
        while (true)
        {
            yield return new WaitForSeconds(3);
            Debug.Log("Wait for 3 seconds");
        }
    }
}
~~~

~~~csharp
luaenv.DoString(@"
    local util = require 'xlua.util'
    xlua.hotfix(CS.HotFixSubClass,{
        Start = function(self)
            return util.cs_generator(function()
                while true do
                    coroutine.yield(CS.UnityEngine.WaitForSeconds(3))
                    print('Wait for 3 seconds')
                end
            end)
        end;
    })
");
~~~

### Entire type

To patch several members in one call, pass a table mapping method names to functions. This replaces the listed members, not the type definition. The constructor patch below initializes Lua state using `util.state`; require the utility module first.

```lua
local util = require 'xlua.util'

xlua.hotfix(CS.StatefullTest, {
    ['.ctor'] = function(csobj)
        return util.state(csobj, {evt = {}, start = 0, prop = 0})
    end;
    set_AProp = function(self, v)
        print('set_AProp', v)
        self.prop = v
    end;
    get_AProp = function(self)
        return self.prop
    end;
    get_Item = function(self, k)
        print('get_Item', k)
        return 1024
    end;
    set_Item = function(self, k, v)
        print('set_Item', k, v)
    end;
    add_AEvent = function(self, cb)
        print('add_AEvent', cb)
        table.insert(self.evt, cb)
    end;
    remove_AEvent = function(self, cb)
       print('remove_AEvent', cb)
       for i, v in ipairs(self.evt) do
           if v == cb then
               table.remove(self.evt, i)
               break
           end
       end
    end;
    Start = function(self)
        print('Start')
        for _, cb in ipairs(self.evt) do
            cb(self.start, 2)
        end
        self.start = self.start + 1
    end;
    StaticFunc = function(a, b, c)
       print(a, b, c)
    end;
    GenericTest = function(self, a)
       print(self, a)
    end;
    Finalize = function(self)
       print('Finalize', self)
    end
})
```


## Historical tooling notes

The older website guide describes an embedded injector selected by `INJECT_WITHOUT_TOOL`, rather than the separate injection tool. Later FAQ guidance discourages this debugging mode because editor/library conflicts can occur. The current editor injector has evolved; do not assume the historical macro is a supported switch in every checkout.

That old mode required matching `Mono.Cecil.dll`, `Mono.Cecil.Pdb.dll`, and `Mono.Cecil.Mdb.dll` from the installed Unity version in `Assets/XLua/Src/Editor`. Its example commands were:

```sh
# Historical macOS Unity installation layout
cp /Applications/Unity/Unity.app/Contents/Managed/Mono.Cecil.* Project/Assets/XLua/Src/Editor/
```

```bat
rem Historical Windows Unity installation layout
copy UnityPath\Editor\Data\Managed\Mono.Cecil.* Project\Assets\XLua\Src\Editor\
```

For old Unity installations missing the PDB/MDB libraries, the historical fallback copied only that installation's `Mono.Cecil.dll` and defined `HOTFIX_SYMBOLS_DISABLE`. This disabled C# debugging information and could produce incorrect source paths and line numbers in logs. Copying Cecil libraries from an unrelated Unity version could destabilize the editor. These notes preserve the original troubleshooting context; prefer the injector and matching dependencies supplied by the checkout you actually use.

The older site guide also used the CLR name `CS['GenericClass`1[System.Double]']` for a closed generic type. The type-argument syntax shown above is the newer supported expression; see the [FAQ](Faq_EN.md#how-do-i-construct-a-closed-generic-type) for both forms.
