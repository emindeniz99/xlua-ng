# Hotfix guide

Hotfixes replace or augment C# behavior with Lua implementations. The target assemblies must be instrumented before release; a Lua patch cannot add an injection point to an already shipped assembly.

## Usage

0. Build the injector once from the project root with `python3 Tools/build_tools.py` (requires Mono's `mcs`). The editor runs `Tools/XLuaHotfixInject.exe`; the repository does not ship a prebuilt copy. The script downloads Mono.Cecil 0.11.6 from nuget.org, checks its SHA-512 and copies it next to the injector; set `XLUA_TOOLS_CACHE` (or `--cache`) to reuse one download. Without access to nuget.org, `--cecil-dir <folder>` takes the DLLs from Unity's `com.unity.nuget.mono-cecil` 1.11.5 or 1.11.6 package instead (for example `Library/PackageCache/com.unity.nuget.mono-cecil@...`); only that build is accepted, by SHA-256. It is Cecil 0.11.5, which can write wrong line numbers into portable PDBs for methods whose `#line` numbers go back by 8192 or more, so the NuGet default is preferred.
1. Enable `HOTFIX_ENABLE` in Unity's scripting define symbols for each relevant build target before compilation. The original guide calls this **File → Build Settings → Scripting Define Symbols**; the exact location depends on Unity's version. Its automated-build warning is that setting the symbol too late through an API does not affect assemblies already compiled. Configure the editor/build target in advance and wait for compilation.
2. Install the xLua source generator ([Configuration](Configure_EN.md#the-source-generator)) and configure the types to patch with `[Hotfix]` on the type or `[assembly: Hotfix(typeof(T), flags)]` in the assembly that defines `T` (see [Configure types to patch](#configure-types-to-patch)). When Unity compiles that assembly, the generator writes its hotfix bridges into it (a `HotfixBridges` class); there is no generation step.
3. Instrument the assemblies. The standard mobile build integration performs injection during the build. When developing a patch in the editor, run **XLua/Hotfix Inject In Editor** manually. Success is indicated by `hotfix inject finish!` or `had injected!`; otherwise inspect the reported errors.

The upstream recommendation is to leave `HOTFIX_ENABLE` disabled during ordinary business-code development and enable it when building a hotfix-capable player or testing a patch.

The injector keeps debug symbols: it rewrites the `.pdb` or `.mdb` file next to the assembly, so stack traces keep their line numbers. If the symbol file cannot be read, it prints `ignoring debug symbols ...` and injects without it.

`Test/Native/hotfix_check.py` runs this workflow outside Unity with Mono, from injection to patching and removing patches; see [Test/README.md](../../../Test/README.md).

If `xlua.hotfix` still reports `xlua.access, no field __Hitfix0_Update` after a success message, check that the type is in the `Hotfix` configuration and that no subsequent compilation overwrote the instrumented assembly. Verify the assembly used by the final player, not just an earlier build artifact. See the [FAQ](Faq_EN.md) for related diagnostics.

## Constraints

Static constructors are not supported.

The standard Unity workflow described here patches project code under `Assets`, not engine or .NET system libraries. xLua itself compiles into `XLua.dll` (its asmdefs, the default; [MIGRATING.md](../../../docs/MIGRATING.md#xlua-in-its-own-assemblies-asmdefs-the-default)), and the injector injects `Assembly-CSharp` and every other project assembly with hotfix types; see [Several hotfix assemblies](#several-hotfix-assemblies). Do not infer engine-library support from that capability.

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

Like other xLua configuration ([Configuration](Configure_EN.md)), hotfix types can be specified in two ways:

1. Add `[Hotfix]` (with flags if needed, `[Hotfix(HotfixFlag.IntKey)]`) directly to a type.
2. Name the type in an assembly-level attribute, in the assembly that defines it. The source generator writes the hotfix bridges next to the injected type, and it sees only the assembly it compiles, so an `[assembly: Hotfix]` for a type of another assembly gets warning `XLSG009` and is skipped. Several types can share one attribute and its flags:

~~~csharp
[assembly: Hotfix(typeof(HotFixSubClass))]
[assembly: Hotfix(typeof(GenericClass<>))]
[assembly: Hotfix(HotfixFlag.IntKey, typeof(Game.Shop), typeof(Game.Bag))]
~~~

A generic type is configured by its definition (`GenericClass<>`), not by a constructed type.

**Configuration lists** (a `[Hotfix]` static field or property of type `List<Type>`, often computed with reflection, for example every type of a namespace) are still read by the injector to choose what it injects, but the source generator does not read them, so their types get no hotfix bridges of their own. Injecting such a type stops with an error, unless a bridge that its assembly has for another type fits each of its methods:

~~~text
can not find delegate for Game.Shop.Buy: the type is configured only by a [Hotfix] configuration list, and the xLua source generator, which writes the hotfix bridges, reads [Hotfix] on the type or [assembly: Hotfix(typeof(T))] in Game (XLua > Migrate Configuration to Attributes converts the list), or mark it HotfixFlag.Inline.
~~~

Convert the lists once with **XLua > Migrate Configuration to Attributes**, which writes `[assembly: Hotfix(typeof(T), flags)]` into the assembly that defines each type, then delete them; see [Converting configuration lists](Migrating_Configuration.md). A computed list is written out as the types it returned when the tool ran; configure types added later by hand. Types marked `HotfixFlag.Inline` need no bridges, so a list still works for them.

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

Injects the bridge handling directly into method bodies rather than generating adaptation delegates. An `Inline` type needs no hotfix bridges, so it is also injected when only a configuration list names it.

### Additional flags in this source

* `AdaptByDelegate` selects delegate adaptation for eligible non-`IntKey` injection points instead of direct same-assembly bridge calls.
* `IgnoreCompilerGenerated` skips types and methods marked with `CompilerGeneratedAttribute`.
* `NoBaseProxy` skips generation of the base-call proxies used by `base(self)`.

These definitions are in [GenAttributes.cs](../Src/GenAttributes.cs), and their injection behavior is implemented in [Hotfix.cs](../Src/Editor/Hotfix.cs).

### IntKey

Stores injection points in an array instead of individual static fields, reducing impact on native code size. Each assembly has its own array, the IntKey slots of the `HotfixBridges` class that the source generator writes into it, with IDs numbered from 0. Injection assigns the numeric IDs, so the patch must use the mapping belonging to the exact released build. A generic type definition cannot be marked `IntKey`; the injector rejects that combination.

The function-to-ID mapping is written to `Resources/hotfix_id_map.lua.txt` in the `GenPath` folder (default `Assets/XLua/Gen/`; see [Configuration](Configure_EN.md#csobjectwrapeditorgenpath)), with timestamped backups beside it. Preserve this file with each released player build.

The mapping has this form; each type's entry names its assembly. If no types use `IntKey`, the file returns an empty table:

~~~lua
return {
    ["HotfixTest"] = {
        ["<assembly>"] = "Assembly-CSharp",
        [".ctor"] = {
            0
        },
        ["Start"] = {
            1
        },
        ["Update"] = {
            2
        },
        ["FixedUpdate"] = {
            3
        },
        ["Add"] = {
            4,5
        },
        ["OnGUI"] = {
            6
        },
    },
}
~~~

To replace `HotfixTest.Update` for this example mapping:

~~~lua
CS.XLua.HotfixDelegateBridge.SetSlot('Assembly-CSharp', 2, func)
~~~

Overloaded methods map to multiple IDs, as shown by `Add` above.

An entry without `["<assembly>"]` holds IDs of the process-wide list, which only xLua's own assembly uses, when it has no `HotfixBridges` (only `Inline` types there); set those with `CS.XLua.HotfixDelegateBridge.Set(id, func)`.

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

The mapping must be loadable through `require 'hotfix_id_map'`. `auto_id_map()` handles both kinds of entry.

The `minimal` preset (`XLUA_PRESET_MINIMAL`) leaves `xlua.util` out, so `auto_id_map()` is not there. Either define `XLUA_UTIL` as well, which keeps `xlua.util`, or set the IDs from the map yourself: `CS.XLua.HotfixDelegateBridge.SetSlot(assembly, id, func)` for each ID of the method, with the assembly that the type's entry names, or `Set(id, func)` for an entry without one; pass `nil` instead of `func` to remove the patch. Types without IDs (field mode) need no map: `xlua.hotfix` patches them by name in every preset.

An assembly without `HotfixBridges` has no IntKey slots, so its types marked `IntKey` get field mode instead, one static field per method and no IDs in the maps, except in xLua's own assembly, which then uses the process-wide list. `xlua.hotfix` still patches them by name. The injector logs a warning that names the types:

~~~text
IntKey falls back to field mode in Game.dll for 2 types: Game.Shop, Game.Bag. The assembly has no HotfixBridges class with IntKey slots, which the xLua source generator writes into an assembly with [Hotfix] types, ...
~~~

To use `IntKey` for those types, make sure the source generator runs for their assembly and configure them with `[Hotfix]` or `[assembly: Hotfix(...)]` there. To silence the warning, remove `HotfixFlag.IntKey` from their configuration.

#### Several hotfix assemblies

When hotfix types are in more than one assembly (for example `Assembly-CSharp` and an assembly definition), each assembly gets its own hotfix bridges and IntKey slots, and the injector:

* writes one map per assembly, `Resources/hotfix_id_map_<assembly>.lua.txt` in the `GenPath` folder, each with timestamped backups;
* then writes `Resources/hotfix_id_map.lua.txt` there as the union of those maps. It holds their contents, so `require 'hotfix_id_map'` and `auto_id_map()` need no other file at run time. Ship this file, as with a single assembly.

`[Hotfix]` on a type and `[assembly: Hotfix(typeof(T), flags)]` work in every assembly (before 2026-10-18 the injector saw a `[Hotfix]` attribute only inside the xLua assembly). `Inline` types and generic methods are injected in every assembly too; outside the xLua assembly they used to be skipped without a warning.

`Test/UnityMatrix/hotfix_intkey.py --source-gen` checks this layout: `Assembly-CSharp` and an asmdef `Game`, each IntKey type configured with `[assembly: Hotfix(typeof(T), HotfixFlag.IntKey)]` in its own assembly, Hotfix Inject In Editor, then `auto_id_map()` and `xlua.hotfix` in play mode. It passed in Unity 2021.3.45f2 and 6000.3.26f1 on 2026-10-18, before the legacy generator was removed. Build-time injection into a player was not run.

Before xlua-ng, `hotfix_id_map.lua.txt` was not written when there were several hotfix assemblies, so `auto_id_map()` failed with "module 'hotfix_id_map' not found", or loaded a stale map left by an earlier single-assembly build (see [MIGRATING.md](../../../docs/MIGRATING.md#tooling)).

Outside Unity, compile each assembly with the source generator and `HOTFIX_ENABLE` ([General/README.md](../../../General/README.md#hotfixing)), inject it with `XLuaHotfixInject` and its own map, then merge the maps:

~~~text
XLuaHotfixInject --merge-id-maps hotfix_id_map.lua.txt hotfix_id_map_Game.lua.txt hotfix_id_map_Tools.lua.txt
~~~

The older site guide notes that Unity may already have finished importing new resources when build-time injection writes this file. Ensure the matching map is included in your patch delivery/loading pipeline. Do not substitute an editor-generated map: conditional compilation and platform-only APIs can assign different IDs in editor and player builds.

## Usage suggestions

* Configure `Hotfix` for types likely to need changes.
* Configure the delegate types used in method parameters, fields, properties, and events of those types as `CSharpCallLua` when Lua callbacks are needed.
* Configure `LuaCallCSharp` for business, engine, and .NET APIs requiring fast access from Lua patches.
* Preserve engine and .NET APIs a future patch may call. Use `LuaCallCSharp` wrappers or `ReflectionUse` preservation, and before release check with **XLua > Validate Setup** that every configured assembly has generated bindings; APIs unreferenced by C# can otherwise be stripped.

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

Patch `GenericClass<double>` or `GenericClass<int>`, not the open `GenericClass<>` definition. Configure the open definition (`[Hotfix]` on `GenericClass<T>`, or `[assembly: Hotfix(typeof(GenericClass<>))]`) so its code is instrumented.

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
