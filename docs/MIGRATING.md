# Migrating from Tencent/xLua

Behaviour that differs from upstream [Tencent/xLua](https://github.com/Tencent/xLua),
for projects moving to xlua-ng. Fixes of upstream bugs, with issue links,
are listed in [UPSTREAM.md](../UPSTREAM.md); this page covers what can change
how existing code behaves. Every behaviour change is added here when it lands.

## Runtime defaults

- **Lua version:** the default is Lua 5.5.1. Upstream ships Lua 5.3.5.
  Lua 5.4.9, 5.3.6 and LuaJIT 2.1 remain available; see
  [build/README.md](../build/README.md). Lua 5.4 and 5.5 change some
  language rules compared with 5.3, so read the Lua manuals' "Incompatibilities
  with the previous version" sections.
- **Native build:** Release by default when the CMake generator is
  single-config.

## Changes in every build

| Area | Upstream | xlua-ng | Commit |
|---|---|---|---|
| `xlua.cast(obj, type)` | Accepted any value and attached the type's metatable, which gave a broken object for tables, numbers or unrelated types | Accepts only a C# object that is an instance of the type (its own type, a base class or an implemented interface); anything else raises a Lua error | `fix(runtime): accept xlua.cast only for C# objects of the target type` |
| `xlua.setmetatable` / `metatable_operation` set | The new metatable lost xLua's tag and `__gc`, so the object stopped being recognised and its C# side was never released | The tag and `__gc` are copied into the new metatable | `fix(runtime): keep the bridge tag and __gc in xlua.setmetatable` |
| `LuaFunction.SetEnv`, `DoString`/`LoadString` with an env (Lua 5.3+) | Replaced upvalue 1, which in a closure can be a captured local. A closure's env change also changed the globals of the whole chunk that defined it | Sets the function's own `_ENV`; other closures of the chunk keep theirs. C functions and functions without `_ENV` are left unchanged | `fix(native): set a function's _ENV, not upvalue 1, in lua_setfenv` |
| Lua number to C# `uint` (arguments, `uint32` struct fields) | Undefined float cast; results differed by CPU (e.g. -1.5 gave 0 on arm64) | A float is truncated and wraps modulo 2^32, like the integer it truncates to; NaN and values outside the 64-bit range give 0. Every `uint` slot now follows the integer rule in the next rows; this rule remains only for wrappers generated before it | `fix(native): convert floats to uint32 without undefined casts` |
| Lua number to a C# integer parameter, field, property or indexer (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`) and to an integer struct field from `xlua.genaccessor` | A float with a fraction was cast without a check: `Foo(2.75)` passed 0 on Lua 5.3+ (2 for `uint`) and 2 on LuaJIT; NaN and infinities gave 0 or a CPU-dependent value; on LuaJIT a numeric string gave 0 for `long` and `ulong`. Overload checks accepted any number, so `Foo(int)` declared before `Foo(double)` took 2.75 | Lua 5.3's rule on every runtime, LuaJIT included, and in reflection, emitted and generated wrappers: an integer, a float with an integer value (`3.0`) or a numeric string that converts to one (`"12"`) is accepted; a fraction, NaN, an infinity or a value outside the 64-bit range raises `bad argument #n to 'Foo' (number has no integer representation)` (from C# wrappers inside a `c# exception:` error). Integral values outside the C# type's range still wrap (`Byte(300)` is 44, `UInt(-1)` is 4294967295). An integer overload now matches only a number with an integer value, so `Foo(2.75)` picks `Foo(double)`; which overload wins when several match changed later (overload selection row below). The other integer slots got the same rule later (next row). Native bridge version 107: rebuild the plugins and regenerate code | `fix(native): convert Lua numbers to integers by Lua 5.3's rule`, `fix(runtime): apply Lua's integer rule to C# integer parameters and fields` |
| Lua number to the other C# integer slots: array elements and indices (`arr[i] = v`, `arr[i]`), Lua tables converted to arrays, `List<T>`, `Dictionary<K, V>` values and struct fields (reflection and `[GCOptimize]`), `params` arrays, nullable integers, enums and `Enum.__CastFrom`, and values C# reads from Lua: delegate and interface results and out values, `LuaFunction.Func`/`Call` results, `LuaTable.Get`, `GetInPath`, `ForEach` and `Cast` | As in the row above before its change: a float with a fraction became 0 (Lua 5.3+) or the truncated value (LuaJIT), NaN and infinities 0 or a CPU-dependent value. A 64-bit enum got only the low 32 bits of a number. A `List<int>` or `Dictionary<_, int>` value with a fraction was left out silently (since the row above), and a delegate result that failed to convert could leave its results on the Lua stack | The same rule on every runtime and binding path (reflection, emitted, generated, `GEN_CODE_MINIMIZE`, source generator). A `params` element names its argument (`bad argument #2 to 'Foo' (number has no integer representation)`); the other slots throw `InvalidCastException: number has no integer representation`, which Lua sees inside the wrapper's `c# exception:` error and C# callers (delegates, `LuaTable`, `LuaFunction`) receive as is. Values of other types are still left out of a `List` or `Dictionary`, and `Dictionary` keys and `LuaTable.ForEach`/`GetKeys` keys still only select entries (`[1.5]` is not an `int` key). An enum takes all 64 bits of its underlying type. The delegate and interface bridges read their results inside `try`/`finally`, so a failed conversion restores the Lua stack. Integral values outside the type's range still wrap unless `XLUA_STRICT_INT_RANGE` is defined (opt-in defines below). Regenerate code | `fix(runtime): apply Lua's integer rule to the other integer slots`, `fix(generator): read integer results by the integer rule inside try/finally` |
| Overload selection: a method, constructor, indexer or operator with several overloads that match the arguments, on every binding path (reflection, CodeEmit, source generator, `GEN_CODE_MINIMIZE`) | The first matching overload in the wrapper's order won: declaration order through reflection and CodeEmit, the generators' order (primitive parameters first) in generated code. `Foo(3)` called `Foo(double)` when it was declared before `Foo(int)`, `Foo(object)` declared first took every value, `Foo(Base)` declared first took a `Derived`, and the reflection and generated paths could call different overloads for the same arguments | The best match wins, as C# overload resolution would choose for the Lua value's natural type, in every preset. A Lua integer prefers `int`, then `long`, then the other integer types, then `double`, `float` and `decimal`; an integer outside the `int` range prefers `long`. A float prefers `double`, then `float`, then `decimal`; a float with an integer value (`3.0`, `6 / 2`) is a float on Lua 5.3+ and prefers `double` over `int`, while LuaJIT, which has no integer subtype, treats it as the integer 3. A string prefers `string` over `byte[]`; a C# object its own class over a base class (the nearer one first), an interface and `object`; `object` is the last choice for any value, and `T` comes before `T?`. A `params` array or a default value is used only when no overload matches as well. nil, tables and functions still go to the first matching overload. With several arguments the costs add up, so where C# reports an ambiguity (`Foo(int, object)` and `Foo(double, string)` for `(1, "x")`) the cheaper total wins and equal totals keep the old order. Examples: `Mathf.Max(3, 5)` now calls `Max(int, int)` and returns an integer; `Foo(2^40)` with `Foo(int)` and `Foo(long)` calls `Foo(long)` instead of wrapping. To keep a specific overload, call it through a wrapper with its own name or `xlua.tofunction` (FAQ). Native bridge version 110 (`xlua_number_kind`): rebuild the plugins and regenerate code | `feat(native): add xlua_number_kind for overload selection`, `fix(runtime): call the overload that fits the arguments best`, `fix(generator): choose overloads by the best fit in generated wrappers` |
| Struct field setters from `xlua.genaccessor` | Converted any value, so `nil`, a boolean, a table, a function or a non-numeric string stored 0. On LuaJIT a numeric string stored 0 in a 64-bit field | Accept a number or a string Lua converts to a number, and on LuaJIT an int64/uint64 for a 64-bit field. Anything else raises `bad argument #2 (number expected, got <type>)` and leaves the field unchanged. Numbers convert as before; integer fields then got the integer rule in the row above | `fix(native): reject non-numeric values in struct field setters` |
| Private overloads through `xlua.private_accessible` | Each overload could be listed several times; a single private overload lost its direct call and rejected arguments that a public method accepts | Each method is listed once and behaves like a public one | `fix(xlua-ng): list each method once in private_accessible wrappers` |
| Lua strings to C# | `PtrToStringAnsi`; invalid UTF-8 could throw ([#1203](https://github.com/Tencent/xLua/issues/1203)) | Decoded as UTF-8 with replacement characters on every build | `fix(xlua-ng): decode Lua strings as UTF-8 with replacement characters on every build` |
| C# `in` and `ref readonly` parameters | Treated like `ref` | Passed as inputs. Members the editor's Roslyn cannot call are skipped. Indexers with such a key also work through reflection (IL2CPP players or `FORCE_REFLECTION` without a generated wrapper), where `x[k] = v` failed with "no such field" | the `in`/`ref readonly` commits in the history, `fix(runtime): index by an in key through reflection` |
| Members with `ref struct` or pointer types | Could break emitted or reflection wrappers | Skipped | `fix(xlua-ng): skip ref struct and pointer members in emitted and reflection wrappers` |
| Calling a C# function whose wrapper was tampered with through `debug.setupvalue` | Crashed (NULL call) | Raises a Lua error | `fix(native): raise instead of calling NULL in csharp_function_wrap` |
| `LuaEnv` construction | The first internal `DoString` ran with a nil error handler | The error handler exists before any Lua code runs | `fix(runtime): create the Lua error handler before the first DoString` |
| `tdr` module (`Resources/tdr/tdr.lua.txt`) | A wrapper around Tencent's `libtdrlua`, a native module that was never published, so `require 'tdr'` always failed | Removed. For binary messages use `string.pack`/`string.unpack` (example in [ECOSYSTEM.md](ECOSYSTEM.md#stringpack-and-stringunpack)) or [lua-protobuf](https://github.com/starwing/lua-protobuf); see [research/tdr.md](research/tdr.md). **XLua/Validate Setup** lists a `Resources/tdr/tdr.lua.txt` left by an in-place upgrade | `feat(runtime): remove the tdr and perf.memory modules` |
| `perf.memory` module (`Resources/perf/memory.lua.txt`) | `snapshot()` and `total()` only raised "use memory leak checker instead!" | Removed; `require 'perf.memory'` fails with "module not found". `collectgarbage('count')` gives the Lua heap size. **XLua/Validate Setup** lists a `Resources/perf/memory.lua.txt` left by an in-place upgrade | `feat(runtime): remove the tdr and perf.memory modules` |
| `xlua.util` and `perf.profiler` | Lua files in `Assets/XLua/Resources`, found by the Resources searcher | Embedded in the xLua assembly (`EmbeddedModules.cs`) and found by a searcher placed right after the Resources searcher, so `require` works with custom loaders only, without the StreamingAssets searcher and outside Unity. `Assets/XLua/Resources` is gone; a project that edited those files keeps its copy in any `Resources` folder, which still takes precedence, as does a custom loader. An upgrade that copies the new xLua over the old one leaves `Resources/xlua/util.lua.txt` and `Resources/perf/profiler.lua.txt` behind, and they then replace the embedded modules: run **XLua/Validate Setup**, which lists them, and delete them unless you edited them on purpose. Chunk names stay `@xlua/util.lua` and `@perf/profiler.lua` | `feat(runtime): embed xlua.util and perf.profiler in C#` |
| `xlua_newstruct` with a negative size (native C export; no C# code calls it) | Sizes -8 to -1 allocated fewer bytes than the struct header and set a length near 2^32, so the struct accessors could write past the userdata; smaller sizes raised a memory error | Pushes `nil` and returns `NULL`; the stack still grows by one. Sizes of 0 and above are unchanged | `fix(native): reject a negative size in xlua_newstruct` |
| A Lua function passed as a delegate type with more than four parameters that is not in `CSharpCallLua` and has no bridge with its signature, outside Unity (`XLUA_GENERAL`) | `IndexOutOfRangeException` from the generic delegate fallback | `InvalidCastException: This type must add to CSharpCallLua: <type>`, as in Unity builds | `fix(runtime): reject delegates with five or more parameters in the generic fallback` |
| `\|` and `&` on enums whose underlying type is `ulong` (Lua 5.3+) | A value above `long.MaxValue` (the top bit set) raised `c# exception in Enum BitwiseOr: System.OverflowException` | The bits are combined as `ulong`, so `Flags.Low \| Flags.High` works for every value. Other underlying types are unchanged | `fix(runtime): combine ulong enum values without overflow in bitwise operators` |
| `[BlackList]` `List<List<string>>` entries in emitted wrappers (types without generated code on Mono, e.g. in the Editor before **Generate Code**) | An entry applied only to types in xLua's own assembly or mscorlib (`Type.GetType` without an assembly name). Entries for types in another assembly (an assembly definition, a DLL, or outside Unity) were ignored, so those members stayed callable from Lua while generated code left them out | The type is looked up in every loaded assembly, so the emitted wrappers leave out the same members as generated code. An entry whose type is not found is still ignored | `fix(runtime): apply BlackList entries to types in other assemblies in emitted wrappers` |
| `[BlackList]` in emitted and reflection wrappers, in the Editor and outside Unity (`XLUA_GENERAL`): types without generated code, including types without `[LuaCallCSharp]` in a project with generated code, non-public types, the .NET Standard 2.0 Editor profile, `FORCE_REFLECTION` and the members `xlua.private_accessible` adds | Emitted wrappers ignored `Func<MemberInfo, bool>` filters and matched entries with their own rules: parameter types by `FullName` (so an entry with a generic parameter type such as ``System.Collections.Generic.List`1[System.Int32]`` did not match), constructor entries with parameter types never. Reflection wrappers applied no `[BlackList]` at all, neither the attribute nor entries nor filters. Members that generated code leaves out stayed callable from Lua, for example `Dictionary<,>.TryAdd` with `ExampleConfig`'s `MethodFilter` | Both apply the attribute, the entries and the filters with the generator's rules (`BlackListRules`: type and parameter type names as `Type.ToString()` gives them; an entry or filter that matches a property accessor `get_X`/`set_X` hides that direction only), so they leave out the same members as generated code. An entry with an assembly-qualified parameter type now matches nowhere; write it as **XLua/Validate Setup** expects. A filter that throws is logged once per filter and the member stays. Players read no configuration then (since 2026-10-19 they read the attribute forms; see below) | `fix(runtime): apply [BlackList] filters in emitted and reflection wrappers` |
| A `[DoNotGen]` method with an extension method of the same name, in generated code | The lazy member wrapped only the type's own methods: `o:Method("x")` called `Method(int)` with a converted argument instead of `Method(this T, string)`. If the own method was static, or the name was only an extension method's, the extension method was not callable at all. Emitted and reflection wrappers had both | The generator registers a lazy instance member for such a name and lists its extension methods in `XLuaGenAutoRegister`; the lazy member takes the static methods for the class and the instance and extension methods for objects, like the other wrappers. Regenerate code | `fix(generator): keep extension methods of [DoNotGen] methods callable` |
| `xlua.private_accessible(T)` on a type with an extension method that shares its name with a public method | The extension method replaced the public overloads: `o:Method(1)` called `Method(this T, string)` with `"1"` | The public overloads stay | `fix(runtime): keep extension overloads after xlua.private_accessible` |
| Interface bridges (a Lua table as a `[CSharpCallLua]` interface) with an ordinary method called `get_X`, `set_X`, `add_X` or `remove_X`, an indexer renamed with `[IndexerName]`, or a property called `Item` without parameters | The emitted bridge (types without generated code on Mono) failed to load with `TypeLoadException` for the first two, and for the property called `Item` called the Lua function `get_Item`/`set_Item` instead of reading and writing the table's field `Item`. **Generate Code** left out methods called `add_X` and `remove_X` (the bridge did not compile) and stopped with "index out of range" on the other two | The emitted and generated bridges implement them as the source generator does: such methods call the Lua function of that name, an indexer calls its accessors' names (`get_At` for `[IndexerName("At")]`), and a property called `Item` is a field. Regenerate code | `fix(runtime): bridge interface members by kind, not by name` |
| Emitted delegate and interface bridges (types without generated code, in the Editor and outside Unity) with `THREAD_SAFE` or `HOTFIX_ENABLE` | Took no lock, unlike the generated bridges and `DelegateBridge`'s own methods, so threads that called Lua through them at the same time shared the env's Lua stack: wrong results, Lua errors or a crash | Every emitted bridge call (delegates, interface methods, properties, indexers, event `add`/`remove`) runs in `lock` on the env, as generated and source-generated bridges do. Owner decision of 2026-10-19. Cost with the defines: 12 to 15 ns per call on Mono 6.14 (M2 Pro), 6 to 16% of a bridge call; without them the emitted code is unchanged | `fix(runtime): lock the env in emitted delegate and interface bridges` |
| An emitted interface bridge whose Lua table has no function for the member (a method, an indexer accessor such as `get_Item`, an event `add_X`/`remove_X`) | `lua_pcall` raised "attempt to call a nil value" with a Lua traceback, while generated bridges raised "no such function X"; a callable table or userdata in the field was called | `no such function <name>` (a `LuaException`), as the generated and source-generated bridges; a field that is not a function is refused, callable tables included. Owner decision of 2026-10-19 | `fix(runtime): raise "no such function X" from emitted interface bridges` |
| A Lua table converted to a `[CSharpCallLua]` interface that has a generic method (its own or an inherited interface's), in the Editor or outside Unity (`XLUA_GENERAL`) | `TypeLoadException: VTable setup of type XLuaGenInterfaceImpl<n> failed` without generated code; with source-generated bindings, which give such an interface no bridge, `InvalidCastException: This type must add to CSharpCallLua` | `InvalidCastException: <interface> cannot be bridged to Lua: '<method>' is a generic method, which an interface bridge cannot implement`. Lua has no type arguments to call such a method with; the source generator already gives the interface no bridge (`'<method>' is a generic method`). Owner decision of 2026-10-19 | `fix(runtime): name the generic method when an interface cannot be bridged` |
| `xlua.get_generic_method(type, name, index)` | The index was read only when `lua_isinteger` was true: never on LuaJIT, and not for a float such as `1.0` on Lua 5.3+, so the first generic method of that name was used | Any number with an integer value selects the method, on every runtime | `fix(runtime): honour the method index of xlua.get_generic_method on LuaJIT` |
| LuaSocket (`socket`, `socket.core`) and `mime` | `LuaEnv` registered `socket` and `socket.core` as C# built-in libraries (`AddBuildin`) that called the `luaopen_socket_core` import, except in Switch and WebGL players. `mime.c` was compiled (and exported on Android and desktop) but `require "mime"` failed | `luaopen_xlua` puts `socket.core`, `socket`, `mime.core` and `mime` into `package.preload` when the native library contains them (CMake `XLUA_LUASOCKET` and `XLUA_MIME`, both on by default; see Native plugins below). C# imports nothing for them, so a library built without LuaSocket links on iOS with the same C#. `require "socket"` returns the same core module as before; `require "mime"` now works. `package.preload` lists the four names, and since the preload searcher runs before the built-in one, `AddBuildin` with one of those names no longer takes effect (upstream's `AddBuildin("socket", ...)` threw for the duplicate name); set `package.preload[name] = nil` first or build without the module. `LuaDLL.Lua.luaopen_socket_core` stays, as a C# method that runs the preload entry and pushes the module; without LuaSocket it raises `LuaSocket is not compiled into the xlua native library`. Which platforms have LuaSocket now depends only on the native build: the WebGL source build and the Switch build do not compile it, the Editor's library does | `feat(native): register LuaSocket and mime in package.preload`, `refactor(runtime): drop the C# LuaSocket registration and import` |
| An error inside an `xlua.util.cs_generator` coroutine | `coroutine.wrap` passed on the message only (with `util.lua`'s position in front on Lua 5.3+); the coroutine's frames were lost ([#1082](https://github.com/Tencent/xLua/pull/1082)) | A string error ends with the coroutine's traceback (`debug.traceback(co)`) and has no `util.lua` position in front; an error value of another type is raised unchanged, as before | `fix(runtime): keep the coroutine traceback in cs_generator errors` |
| String parameters of the native bridge imports outside Unity (`XLUA_GENERAL`): chunk names, global names, `LuaTable.GetInPath`/`SetInPath` paths, C# error messages | Marshalled by the runtime: UTF-8 on Mono and on .NET outside Windows, the ANSI code page on .NET Framework and .NET on Windows, where non-ASCII names and messages reached Lua garbled ([#1081](https://github.com/Tencent/xLua/issues/1081)) | Passed as UTF-8 bytes on every runtime, as Unity's Mono and IL2CPP already do. Unity builds are unchanged | `fix(runtime): pass UTF-8 strings to the native imports outside Unity` |
| The metatable of a C# type whose name is not ASCII (`luaL_newmetatable`), outside Unity (`XLUA_GENERAL`) on the .NET Framework and .NET on Windows | The import marshalled the type's name in the ANSI code page, while `luaL_getmetatable` looks it up as UTF-8, so the bridge did not find the metatable it had made and used `nil` as a table: undefined behaviour in the native library, an abort with `XLUA_APICHECK` ("table expected"); two types whose names differ only outside the code page could share one metatable ([#1081](https://github.com/Tencent/xLua/issues/1081)) | The name is passed as UTF-8 bytes, as the other imports are since the row above. Unity builds and ASCII names are unchanged | `fix(runtime): pass metatable names to luaL_newmetatable as UTF-8 outside Unity` |
| C# strings pushed to Lua with `NATIVE_LUA_PUSHSTRING` outside Unity (`XLUA_GENERAL`) on the .NET Framework and .NET on Windows | The `lua_pushstring` import marshalled every C# string (values, member and metatable names) in the ANSI code page, so characters outside it reached Lua as `?` ([#1081](https://github.com/Tencent/xLua/issues/1081), [#1179](https://github.com/Tencent/xLua/issues/1179)) | Passed as UTF-8 bytes, as the other imports are since the rows above. Unity builds and builds without the define are unchanged. As before, `lua_pushstring` ends a string at its first NUL. Owner decision of 2026-10-28 | `fix(runtime): pass NATIVE_LUA_PUSHSTRING strings as UTF-8 outside Unity` |
| `[CSharpCallLua]` types in dynamic assemblies (`AssemblyBuilder`), without generated code on the .NET Framework (`XLUA_GENERAL` tools, .NET Framework hosts) | Found by the type scan, which then was not kept: once the program had made any dynamic assembly, every later `LuaEnv` scanned all loaded types again. Mono and Unity already left dynamic assemblies out | Not found on the .NET Framework either: the `[CSharpCallLua]` scan and the other attribute scans (blacklists, extension methods, `[GeneratedBindings]`) skip `Assembly.IsDynamic` on every runtime, and the scan stays cached. Owner decision of 2026-10-28 | `fix(runtime): leave dynamic assemblies out of the attribute scans on every runtime` |
| A Lua table where a `UnityEngine.Object` type is expected (`GameObject`, `Component`, `Transform`, `MonoBehaviour`, `ScriptableObject`...): parameters, `ref` arguments, fields, properties, fields of a table converted to a class, `params` elements, delegate and interface results, `LuaTable.Get` and `Cast` | A class with a default constructor was made from the table field by field, so `SetPosition({}, 0, 0, 0)` added a new, empty GameObject to the scene, and a `MonoBehaviour` or `ScriptableObject` came out unusable (Unity warns); a type without a public constructor, such as `Transform`, got `null`. The overload check accepted the table, so it could pick the `UnityEngine.Object` overload over the intended one ([#918](https://github.com/Tencent/xLua/issues/918)) | A table raises, in every preset and on every binding path (reflection, emitted, generated, `GEN_CODE_MINIMIZE`, source generator): `bad argument #1 to 'SetPosition' (UnityEngine.GameObject expected, got table)` for a parameter, field or property that Lua sets (inside the wrapper's `c# exception:` error), `InvalidCastException: UnityEngine.GameObject expected, got table` for a value C# reads from Lua and for a `params` element of a generated or emitted wrapper (reflection wrappers name the argument). The overload check no longer takes a table for such a parameter, so `Pick({})` with `Pick(GameObject)` and `Pick(LuaTable)` calls the second. `nil` and C# objects pass as before, other classes still convert from tables, and a `List<GameObject>` leaves a table element out, as it does other values of the wrong type. The base class is found by its name, `UnityEngine.Object`. Generated code reads such parameters with the new `ObjectTranslator.GetUnityObjectArg`: regenerate code | `fix(runtime): refuse Lua tables for UnityEngine.Object types`, `fix(generator): read UnityEngine.Object arguments with GetUnityObjectArg` |
| `require` through xLua's searchers: custom loaders (`LuaEnv.AddLoader`, `SignatureLoader`), `Resources`, StreamingAssets, the embedded `xlua.util`, `perf.profiler` and `xlua.memory`, `AddBuildin` libraries | The searchers returned only the loader, so on Lua 5.4 and 5.5 `require` returned `nil` as its second value, and on Lua 5.3 and later the module's `...` was `(name, nil)` ([#1182](https://github.com/Tencent/xLua/pull/1182)) | The searchers return loader data, as Lua's own searchers do: the path a custom loader set through its `ref` argument (the chunk name without `@`), the `Resources` or StreamingAssets path (`a/b.lua`), the embedded module's file name (`xlua/util.lua`), and `":preload:"` for an `AddBuildin` library, as Lua 5.4 gives for `package.preload`. On Lua 5.4 and 5.5 `require` returns it as its second value and passes it to the module as its second argument; Lua 5.3's `require` passes it but returns only the module; LuaJIT's `require` passes only the name, so nothing changes there. Code that counts `select('#', ...)` in a module, or collects `require`'s results in a table constructor (`{require 'm'}`) or an argument list (`f(require 'm')`), sees the extra value | `fix(runtime): return the loader data from xLua's require searchers` |
| `perf.profiler` and `xlua.sethook` | On Lua 5.3, 5.4 and 5.5 every C# function's return reached the hook twice: from Lua's own return hook (with no third argument) and from the bridge's own report for LuaJIT (with `"[C#]"`). perf.profiler's report showed each C# function with twice its real CALLED count and half its real AVERAGE(MS). On every runtime, a C# return after `debug.sethook` had replaced the hook of `xlua.sethook` still called the `xlua.sethook` function and put its hook back | One call and one return event per C# call, the return with `"[C#]"` as the third argument, on every runtime; perf.profiler's counts and averages for C# functions are the real ones, and the source column still says `[C#]`. LuaJIT's events are unchanged. A hook set by `debug.sethook` stays in place. Code that halved the counts of C# functions to make up for the old output, or that counted returns without a third argument, must stop doing so | `fix(native): report each C# return once to the xlua.sethook profiler` |
| A userdata that is not one of xLua's where the bridge expects a C# object or struct, in the Editor, in Development Builds and outside Unity (`XLUA_GENERAL`): a foreign userdata as `self` (`obj.Method(file)` where `obj:Method()` was meant) or as an object argument, a userdata that only looks like a struct in the `xlua.genaccessor` accessors, a value a script stored in the userdata cache through `debug.getregistry` | The bridge read the first int of the userdata as an object handle, so it could reach another C# object; the accessors read and wrote any block with a struct header; the stored value was returned for that object | The bridge checks (`XLUA_BRIDGE_CHECKS`) refuse them: a method or field raises its usual `c# exception` error, `__tostring` gives `<invalid c# object>`, an accessor raises `invalid c# struct!`, and the cache entry is ignored. Correct scripts behave the same; where the checks run, an instance call, field or property access costs about 10 to 13 ns more. Release players leave the checks out and behave as upstream unless they define `XLUA_BRIDGE_CHECKS`; `XLUA_NO_BRIDGE_CHECKS` leaves them out everywhere (below). Until 2026-10-19 they were the opt-in defines `XLUA_CHECK_OBJECTS`, `XLUA_CHECK_STRUCT_ACCESSORS` and `XLUA_CHECK_OBJECT_CACHE`, which no longer exist | `feat(runtime): run the bridge checks in debug builds only` |
| A light userdata (a C# `IntPtr`, a native module's pointer) or a userdata smaller than an int where the bridge expects a C# object, for example `obj.Method(ptr)` where `obj:Method(ptr)` was meant | The bridge read an int at the address the light userdata holds, or past the end of the block: undefined behaviour that could name another C# object or crash | No object, in every build: the call raises its usual error and `__tostring` gives `<invalid c# object>` | `fix(native): read C# object handles only from full userdata` |
| `xlua.sethook` and `perf.profiler`: a function reached through a tail call (`return f()`) on Lua 5.3, 5.4 and 5.5 | `xlua.sethook` named the hook's event 4 `"tail return"`, its Lua 5.1 name; since Lua 5.2 that event is a tail call, the entry of the function `return f()` calls. perf.profiler took it for a return and counted the function once more for each tail call: 5 direct and 2 tail calls of one function reported CALLED 9 on PUC Lua and 7 on LuaJIT | The event is named `"tail call"` on PUC Lua, and perf.profiler counts it as a call that the function's `"return"` ends, so both runtimes report 7. LuaJIT reports a tail call as `"call"` and is unchanged. An `xlua.sethook` function that tested for `"tail return"` must test for `"tail call"` and take it as an entry; the function that made the tail call still gets no return event (its frame is reused), as before. On PUC Lua a C# function in a tail call is reported as `"call"`, as before; on LuaJIT perf.profiler does not count a C# function in a tail call, as before | `fix(native): name the tail call event of xlua.sethook "tail call"` |
| Adding or removing a handler of a struct's instance event from Lua (`s:Changed('+', f)`, `s:Changed('-', f)`) | The emitted wrappers (types without generated code on Mono: the Editor and `XLUA_GENERAL`) and the generated and source-generated wrappers added the handler to a copy of the struct and never stored it back, so the struct in Lua never raised it. The reflection wrappers (IL2CPP players without generated code, `GEN_CODE_MINIMIZE`) kept it | Every binding path stores the struct back into the Lua value after `'+'` and `'-'`, as struct field and property setters and method calls already did, so `s:Fire()` runs the handler. As in C#, a struct read from a field or property is a copy: `holder.Inner:Changed('+', f)` changes only Lua's copy, and the handler reaches `holder` after `holder.Inner = inner` | `fix(runtime): store a struct back after an event handler is added from Lua`, `fix(generator): store a struct back after an event handler is added from Lua` |
| `[BlackList]` in players' reflection wrappers: types without generated code (every type of an IL2CPP build without the source generator, types without `[LuaCallCSharp]`), `[ReflectionUse]` types and the members `xlua.private_accessible` adds | Ignored, so a member that generated code and the Editor leave out stayed callable from Lua in players | Players apply the attribute forms with the generator's rules: `[BlackList]` on the member and `[assembly: BlackList(...)]` (by type or by name) in any loaded assembly, read once per process at the first reflection wrapper (`GetCustomAttributes` on each assembly, no type scan). The `List<List<string>>` lists and `Func<MemberInfo, bool>` filters of static classes are still read only in the Editor and outside Unity: finding them takes a scan of every loaded type, and they usually live in an Editor folder. **XLua > Migrate Configuration to Attributes** turns them into assembly-level attributes in a player assembly, filters evaluated | `fix(runtime): apply [BlackList] attributes in player reflection wrappers` |
| A Lua table converted to a struct that sets none of its fields: `{}`, or a table whose keys are all misspelt (`Vector3.one - {X = 1}`) | Gave the struct's zero value ([#751](https://github.com/Tencent/xLua/issues/751)). From 2026-10-18 xlua-ng raised only with the opt-in define `XLUA_STRICT_STRUCT_TABLES` (in the minimal preset) | Raises in every preset (owner, 2026-10-19): `InvalidCastException: table sets none of the fields of UnityEngine.Vector3 (x, y, z)`. A partial table (`{x = 1}`) still converts, keys that are not fields are still ignored, classes are not checked, and the overload check still accepts any table for a struct parameter. Reflection, emitted and source-generated conversions. `XLUA_STRICT_STRUCT_TABLES` is gone and defining it does nothing. Where `{}` meant zero, pass the zero value (`CS.UnityEngine.Vector3.zero`, or `CS.UnityEngine.Vector3()` for any struct) or a table that sets the fields (`{x = 0, y = 0, z = 0}`) | `fix(runtime): raise for a struct table that sets none of its fields in every preset` |
| A delegate, hotfix or interface bridge, `LuaTable` or `LuaFunction` used after `LuaEnv.Dispose`, and `LuaEnv` members after it ([#999](https://github.com/Tencent/xLua/issues/999)) | Generated delegate and hotfix bridges passed a NULL state to `pcall_prepare` and the process crashed; the others threw `InvalidOperationException("this lua env had disposed!")`, `LuaEnv.Alias` a `NullReferenceException` | Every one throws `ObjectDisposedException` with the same message. It derives from `InvalidOperationException`, so existing `catch` blocks still match. A bridge reaches a disposed env when its holder released it (`Delegate.Target`) or a finalizer kept the delegate | `fix(runtime): throw ObjectDisposedException from bridges of a disposed LuaEnv` |
| A second `LuaEnv.Dispose()` | Threw `InvalidOperationException` from its first step (`FullGc`) | Does nothing, as `Dispose(bool)` already did | same commit |
| `LuaEnv.Dispose` from a C# function that Lua called, also from a coroutine or a `__gc` handler | Closed the state under the running call; the process crashed when the call returned | Throws `InvalidOperationException("a LuaEnv cannot be disposed while one of its calls runs")`, which reaches Lua as an error; dispose after the call has returned | `fix(native): protect table traversal, length and Dispose from raising through C#` |
| `LuaTable.ForEach`, `GetKeys` and `GetKeys<T>` when the action changed the table so that the current key is gone | Lua raised "invalid key to 'next'" with no handler: a `LuaException("unprotected error in call to Lua API (...)")` thrown through native frames, a crash where the runtime does not unwind them (IL2CPP) | A `LuaException` with Lua's message; the traversal stops | same commit |
| Lua 5.3: a new metatable for a table whose `__mode` is `"kv"`, during a garbage collection cycle | The collector could free the new metatable while the table still used it (heap use after free; a script reaches it with `setmetatable` and `collectgarbage` alone). Upstream bug "New metatable in an all-weak table can fool the GC", which Lua 5.4 fixed and 5.3.6, the last 5.3 release, keeps | Lua 5.4's fix is applied to the vendored Lua 5.3.6 (`build/LUA_PATCHES.md`) | `fix(lua): keep the new metatable of an all-weak table alive on Lua 5.3` |
| LuaJIT: a light userdata that `debug.setmetatable` gave the int64 metatable, read as a C# `long` or `ulong` or concatenated | The int64 checks read 16 bytes at the address the light userdata holds | Not an int64: it converts like any other light userdata, and `..` raises "attempt to concatenate" | `fix(native): accept only full userdata as LuaJIT int64 values` |
| An error in Lua code that C# called (`DoString`, `LuaFunction.Call`, delegates) after a script set the global `debug` or `debug.traceback` to something that is not a table or function | LuaEnv's message handler raised while it looked up `debug.traceback`, so C# got "error in error handling" instead of the error. On LuaJIT, with a hook that makes a protected call (`debug.sethook(function() pcall(f) end, 'c')`), the handler re-entered itself until the C stack overflowed | The handler builds the traceback itself (`luaL_traceback`, the same text as `debug.traceback`) when `debug.traceback` is not a function; a script's own `debug.traceback` is still called | `fix(native): keep the error message when a script replaced debug.traceback` |
| Lua 5.4: calling a value through a chain of `__call` metamethods (a table whose `__call` is a table with its own `__call`), also a cycle such as a table that is its own `__call` | Any chain length was followed. A cycle ran in C, with no hook between steps and the stack growing by one slot per step, for minutes until "stack overflow"; the instruction and time limits and `Interrupt` could not stop it | A chain longer than 2000 raises "'__call' chain too long", as `__index` chains longer than 2000 raise "'__index' chain too long; possible loop". Lua 5.5 has the same error for chains longer than 15; Lua 5.3 and LuaJIT accept only a function as `__call` | `fix(lua): limit chains of __call metamethods on Lua 5.4` |
| `LuaEnv.AddLoader` (`AddSearcher`) after a script set the global `package` to nil or another value, or gave `_G` an `__index` that raises | Looking up `package.searchers` raised with no handler: Lua's panic, a `LuaException` thrown through native frames (a crash where the runtime does not unwind them, IL2CPP) | The lookup runs under a protected call; `AddLoader` throws `Exception("Can not set searcher!")`, as it did for a `package.searchers` that is not a table | `fix(native): look up package.searchers under a protected call` |
| A `__len` metamethod that raises or returns a non-integer, for `LuaTable.Length`, `LuaEnv.AddSearcher` and a table passed for a C# array or `List<T>` (Lua 5.3 and later) | `Length` failed as above; a conversion raised across the converter's managed frames | `Length` throws a `LuaException` with the error; a conversion inside a call from Lua raises a Lua error with the C# exception's text. Lengths without an error are unchanged; LuaJIT keeps reading the raw length | same commit |
| Operators and indexers of `[LuaCallCSharp]` types in generated code (the source generator, plain and `GEN_CODE_MINIMIZE`) | `[BlackList]` (on the member, or an entry with the operator's parameter types) and `[Obsolete]` did not leave them out, while the emitted and reflection wrappers did | Left out in every binding mode. An entry `{type, "Item"}` names an indexer and `{type, "get_Item"}` its get accessor, also of a string-keyed indexer (its `get_Item` and `set_Item` methods), which a `[BlackList]` indexer now takes along in emitted wrappers too | `fix(generator): leave out blacklisted and obsolete operators, indexers and lazy members` |
| A member in both `[DoNotGen]` and `[BlackList]` (or `[Obsolete]`), in generated code | Registered as a lazy member, so Lua could call it; a lazy method also took the overloads `[BlackList]` names | Left out, as in the emitted wrappers; a lazy method takes only the overloads the other wrappers take | same commit |
| A public readonly field (`readonly`, not `static readonly` or `const`) in emitted and reflection wrappers: types without generated code | `o.F = v` wrote the field; generated code raised "cannot set F, no such field" | Raises as generated code does. `xlua.private_accessible` still writes non-public readonly fields | `fix(runtime): bind emitted and reflection wrappers as generated code` |
| Indexers in reflection wrappers: a private setter, `[BlackList]` on one accessor, two or more keys | A private setter was called, so `o[k] = v` wrote; a blacklisted accessor stayed; an indexer with two keys got one and threw `TargetParameterCountException`. Emitted wrappers also bound two-key indexers | As generated code: public accessors that `[BlackList]` leaves in, indexers with one key only | same commit |
| A binary operator with one overload in emitted and reflection wrappers, given an operand of another type (`a + 1` where only `T + T` exists) | The operand reached the operator as null (a `NullReferenceException` inside it, or a wrong result) | Raises "invalid arguments to right hand of + operator, need T!" (or "left hand"), as generated code does | same commit |
| An instance method called with something other than an object of its class as `self` (`o.Method({}, 1)` where `o:Method(1)` was meant) | Emitted wrappers made a new object from the Lua table and called the method on it; reflection wrappers raised `TargetException` | `NullReferenceException` for no object and `InvalidCastException` for an object of another class, as generated code raises | same commit |
| An event in reflection wrappers given a handler that is neither a function nor a delegate (`o:Changed("+")`, `o:Changed("+", 5)`) | nil raised "table index is nil" from inside C#, a Lua error through the managed frames after which Mono could crash on a later exception; a number could name the bridge of another Lua function or threw `KeyNotFoundException` | "#3 need System.Action!" as in generated code; an operator other than `+` or `-` raises "invalid arguments to T.Changed!". Emitted wrappers' "#3, need System.Action" reads the same now | same commit |
| Error texts of the reflection wrappers (IL2CPP players without generated code, types without `[LuaCallCSharp]`) | "invalid arguments to Method", "invalid arguments to .ctor", "c# exception:" with the message and ",stack:" and the trace, "try to get T.Item throw a exception:" for indexers, "c# exception in FixCSFunction:" for fields | The texts of generated code: "invalid arguments to T.Method!", "invalid arguments to T constructor!", "c# exception:" followed by the exception (its type, message and trace) | same commit |
| A missing argument for a `Nullable<T>` parameter (`o:F()` for `F(int? x)`) in generated and emitted wrappers | Read as 0 (`has 0`), while nil gave null | null, as nil gives and as in reflection wrappers | same commit |
| `XLUA_JUST_EXCLUDE_ERROR` in emitted wrappers and lazy members | Ignored: emitted wrappers left out every `[Obsolete]` member | Only `[Obsolete(..., true)]` members are left out, as in generated code | same commit |
| `[Obsolete]` members in reflection wrappers (IL2CPP players without generated code, types without `[LuaCallCSharp]`, the minimal preset with `XLUA_REFLECTION`) | Bound: Lua could call obsolete methods, constructors, operators, indexers, fields, properties and events that generated code and the Editor's emitted wrappers leave out | Left out as generated code decides (owner decision of 2026-10-28): every `[Obsolete]` member, `[Obsolete("...", false)]` included, or with `XLUA_JUST_EXCLUDE_ERROR` only `[Obsolete(..., true)]` ones. An obsolete overload drops out of its method's overloads, so a call it matched goes to another overload or raises "invalid arguments". The non-public members that `xlua.private_accessible` adds are unchanged | `fix(runtime): leave obsolete members out of reflection wrappers` |
| A parameter, field or property of a struct type (not a primitive, enum, `decimal` or `Nullable<T>`) given nil, a number, a string, a boolean or nothing, where no overload check rejects it (a method with one overload, a field) | Generated code threw `NullReferenceException`; emitted and reflection wrappers passed the struct's zero value | Raises `c# exception:System.InvalidCastException: T expected, got nil` (or `number`, `string`, `boolean`, `no value`) in every binding mode (owner decision of 2026-10-28). `Nullable<T>` parameters still take nil, an omitted optional parameter still gets its default, and overload selection is unchanged: nil never matched a struct overload. The same applies to `xlua.access(obj, "field", nil)` for a struct field. Where C# reads a value from Lua through the generated reader of a `[GCOptimize]` struct (a `LuaTable.Get<T>`, a delegate's result), nil now throws this `InvalidCastException` instead of `NullReferenceException`, and a struct read field by field throws it instead of `Exception("can not cast ...")` | `fix(runtime): raise for a struct parameter given nil or a number` |
| A parameter, field or property of an enum type (not `Nullable<E>`) given nil (a mistyped `CS.E.Name` is nil), nothing, a boolean, a table or a C# object of another class, where no overload check rejects it | Generated code threw `NullReferenceException` or `InvalidCastException: invalid value for enum E`; emitted and reflection wrappers threw the latter; the generated reader of a `[LuaCallCSharp]` enum gave "invalid userdata for E" for another type's userdata | Raises `c# exception:System.InvalidCastException: E expected, got nil` (or `no value`, `boolean`, `table`, the other object's class) in every binding mode (owner decision Q34 of 2026-10-28), as for structs. A number and a value name still convert, `Nullable<E>` still takes nil, an omitted optional enum parameter still gets its default. An enum that C# reads back from Lua where Lua has no value still raises `invalid value for enum E`, as it did in every mode | `fix(runtime): raise the same error for an enum argument given nil in every mode` |
| A struct that C# reads back from Lua where Lua has no value: a delegate or interface result, `LuaFunction.Func<T, S>`, an `out` value | Emitted bridges gave `default(S)`; generated code threw `NullReferenceException`, or `InvalidCastException: S expected, got nil` for a `[GCOptimize]` struct (since 2026-10-28); `LuaFunction.Func` threw `NullReferenceException` in every mode | `default(S)` in every binding mode (owner decision Q35 of 2026-10-28), as an `int` result gives 0. Arguments to C# methods still raise. `LuaTable.Get<S>` and `GetInPath<S>` of a missing key are unchanged: they raise `can not assign nil to S` for every value type, `int` included, before any conversion, in every mode | `fix(runtime): give default(S) for a struct result Lua does not return` |
| A C# object of another class passed for a struct parameter, field or property | Generated and emitted code threw the runtime's `InvalidCastException` ("Specified cast is not valid." on Mono), reflection `ArgumentException` from `MethodBase.Invoke` or the struct error with "got userdata", the generated readers of `[GCOptimize]` structs `Exception("invalid userdata for S")` | Raises `c# exception:System.InvalidCastException: S expected, got C` (the object's class) in every binding mode (owner decision Q36 of 2026-10-28); the struct error names the class of any C# object it is given. The generated `[GCOptimize]` code also raises it where it updated a struct userdata of another type ("invalid userdata for S" before) | `fix(runtime): raise the same error for a struct argument of another class` |
| A registry entry of xLua replaced by a script through `debug.getregistry` (the metatable of a C# type, by reference or by name, the userdata cache, a generated enum cache, LuaJIT's int64 metatable `registry[8]`) with a number, a string or another value that is not a table | The next push of a value of that kind gave `lua_setmetatable` or `lua_rawgeti` the value as a table: a crash on LuaJIT and on Lua without its API checks (docs/research/crash-inventory.md, item 22) | In debug builds (the bridge checks, `XLUA_BRIDGE_CHECKS`: the Editor, Development Builds, `XLUA_GENERAL`; owner decision Q37 of 2026-10-28) the entry is used only if it is a table: the object, struct or int64 is pushed without a metatable, so using it raises a Lua error; a replaced cache is not used and a name entry is dropped; int64 arithmetic and the `uint64` functions on LuaJIT raise "the int64 metatable (registry[8]) was replaced". Release players are unchanged, at no cost: they import the same native functions as before. Native bridge version 112 | `fix(runtime): check xLua's registry entries in debug builds` |
| A `BaseType` chain that does not end, in the closures of a C# object's or class's `__index` and `__newindex` (only with the `debug` library: `debug.setupvalue` puts a table whose `BaseType` leads back to itself into the closure) | The walk looped in C, where the instruction hook (`LuaOptions.InstructionLimit`, `TimeLimit`) cannot stop it | Stops after 1000 types with "the BaseType chain is longer than 1000 types (a cycle?)"; real type chains are far shorter | `fix(native): bound the BaseType walk of the indexer closures` |
| A `LuaEnv` used from a thread other than its owner, in the Editor, in Development Builds and outside Unity (`XLUA_GENERAL`), without `THREAD_SAFE` or `HOTFIX_ENABLE`: a second thread, or a C# finalizer that calls Lua | No check: two threads in one env corrupted the Lua heap, and the process aborted or got odd errors | Throws `InvalidOperationException` that names both thread IDs and the fixes. The owner is the first thread that uses the env after its constructor returns, not the thread that created it (owner decision of 2026-10-28), so an env made in a field initializer or static constructor that Unity runs on a loading thread works on the main thread unchanged; `LuaScript` follows the same rule. The new `LuaEnv.SetOwnerThread()` (and `LuaScript.SetOwnerThread()`) hands it to the calling thread, and `LuaOptions.CheckThread = false` turns the check off for one env. `Interrupt()` and the statistics (`MemoryUsed`, `MemoryPeak`, the limits' getters) work from any thread; the finalizers of `LuaTable`, `LuaFunction` and the bridges only queue a release, as before. One of the [bridge checks](#opt-in-defines-no-change-unless-defined): release players leave it out unless they define `XLUA_BRIDGE_CHECKS`, and `XLUA_NO_BRIDGE_CHECKS` removes it. Where it runs it costs about 1 to 2 ns per call into Lua (one more thread-static read; CodeEmit bridges read it several times per call, about 4 to 8 ns). An env that one thread has used and another takes over, such as one a loader thread ran scripts in, needs `SetOwnerThread()` on the new thread | `feat(runtime): check the owner thread of a LuaEnv in debug builds` |
| Nested calls between Lua and C# on LuaJIT (a Lua function calls C#, which calls Lua, which calls C# ...) | No limit: the C stack overflowed at about 10,240 levels and the process crashed (SIGSEGV). PUC Lua raised `C stack overflow` | LuaJIT raises the same `C stack overflow` Lua error at the same depth as Lua 5.3 to 5.5: when a 200th C# function would start while 199 run on the thread (the count is per thread, so C# functions of other `LuaEnv`s running on it count too). Every build, no define; about 2.5 ns per call from Lua into C# on LuaJIT, a thread-local counter in the trampoline (macOS arm64; below the noise of the bridge benchmarks) | `fix(native): stop deep C# and Lua recursion on LuaJIT as PUC Lua does` |
| The struct helpers given a userdata whose block is shorter than its struct header says: the `xlua.genaccessor` accessors and `xlua.structclone` with any such userdata, and a C# parameter of a `[GCOptimize]` struct or of `decimal` given a userdata that `debug.setmetatable` gave the struct's metatable (LuaJIT's 0-byte `newproxy()`, a small userdata of a C module) | The header was read past the end of the block, and then the fields there were read or written: a heap overflow, a crash or silent corruption, in every build (the bridge checks test the metatable, not the size) | Refused in every build (owner decision of 2026-10-28): the accessors and `structclone` raise `invalid c# struct!`, the C# parameter raises `unpack fail for T` (`unpack decimal fail!`), and writing it back `pack fail for T`. xLua's own structs behave as before. An accessor call costs about 0.5 ns more; a C# struct argument is checked in one native call (`xlua_tostructbuffer`) instead of two. Native bridge version 111 (below) | `fix(native): check the length of a struct userdata before its header` |
| Lua 5.3: an error raised in a `__gc` finalizer | Raised again from the collection that ran the finalizer: `LuaEnv.FullGc`, `LuaEnv.Dispose` and C# calls that allocate (pushing a string or a table) ended in the panic handler (`LuaException: unprotected error in call to Lua API (error in __gc metamethod ...)`, a crash where the runtime does not unwind native frames); inside a protected call it became the error of an unrelated call, and `pcall(collectgarbage)` failed | Written to stderr as `Lua warning: error in __gc (...)` and dropped, as Lua 5.5 does under xLua (5.4 drops it silently while warnings are off; LuaJIT prints `ERROR in finalizer: ...`); the collection goes on and runs the other finalizers (owner decision of 2026-10-28, [build/LUA_PATCHES.md](../build/LUA_PATCHES.md)). Code that relied on `collectgarbage()` failing must check what its finalizers did instead | `fix(lua): report errors in __gc as warnings on Lua 5.3` |
| The old leak checker, `xlua_report_object_relationship` and `xlua_report_table_size` (`General/LuaMemoryLeakChecker`, CMake `XLUA_LEAK_CHECKER`, Lua 5.3 to 5.5), during a collection cycle or from a C function whose stack is full ([#1149](https://github.com/Tencent/xLua/issues/1149)) | The walks reported dead objects that the collector had not swept yet and read what they point to after it was freed (an intermittent crash), a callback that allocates could step the collector during the walk, and each closure was pushed without checking the stack | Dead objects are left out, the collector is stopped during a walk and its state restored afterwards, and closures are pushed after `lua_checkstack` (without room the walk reports tables only). A callback must not run a full collection itself (owner decision of 2026-10-28). `LuaEnv.TakeMemorySnapshot` is unchanged | `fix(native): skip dead objects in the old leak checker walks` |
| A `LuaFunction`, `LuaTable`, delegate or interface bridge used after its own `Dispose()` while its `LuaEnv` lives | The released registry reference went to the next object that took one: a call ran another Lua function or failed with "attempt to call a table value", and `Get`, `Set` and the others read or changed another table | Every use throws `ObjectDisposedException` (an `InvalidOperationException`): calls (`Call`, `Func`, `Action`, the delegate or interface itself), `Get`, `Set`, `ContainsKey`, `GetInPath`, `SetInPath`, `Length`, `ForEach`, `GetKeys`, `Cast`, `SetEnv`, `SetMetaTable`, passing it to Lua (as an argument, a table value or `DoString`'s env), `Equals` and `GetHashCode`. `ToString` and a second `Dispose` still work. One comparison per call (owner decision of 2026-10-28) | `fix(runtime): throw ObjectDisposedException from a disposed LuaBase` |
| A Lua table converted to a packable `[GCOptimize]` struct through `LuaTable.Cast<T>`, `LuaFunction.Cast<T>`, `ObjectTranslator.GetObject`, or as an element of an array, `List<T>` or `Dictionary<K, V>` or a field of a class filled from a table, with generated bindings | Filled the struct's public fields by reflection, so its `[AdditionalProperties]` properties stayed at their defaults, while a parameter of a generated wrapper and `LuaTable.Get<T>` used the generated reader and set them | Every path uses the generated reader, in every preset (owner decision of 2026-10-28), so the properties are set everywhere, as for a parameter. Without generated bindings (no source generator, or before it ran) the fields are still filled by reflection | `fix(runtime): read packable GCOptimize structs through the generated reader everywhere` |
| LuaJIT: an `xlua.sethook` profiler function (`perf.profiler` included) that raises an error at the return of a C# function | The profiler's hook was turned off while the function ran and not set again after its error, so the profiler saw no further event until `xlua.sethook` was called again | The hook is set again before the error goes on, so the profiler keeps working, as on Lua 5.3 to 5.5, where it always did. The error reaches the caller with the same value; a message handler (`xpcall`, LuaEnv's traceback) now runs where the error goes on, at the C# function's return, so the traceback no longer shows the profiler function's own frames | `fix(native): keep the LuaJIT profiler hook after the profiler function raises` |

## Opt-in defines (no change unless defined)

| Define | Effect | Docs |
|---|---|---|
| `XLUA_NO_STREAMING_ASSETS_LOADER` | Removes the StreamingAssets `require` searcher (−570 KB on an Android APK that does not otherwise use UnityWebRequest) | [Lua_Loaders.md](../Assets/XLua/Doc/Lua_Loaders.md) |
| `XLUA_LOCK_METATABLES` | `getmetatable` on bridge objects returns `"locked"` and `setmetatable` fails | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md), [Security.md](../Assets/XLua/Doc/Security.md) |
| `XLUA_BRIDGE_CHECKS` | Keeps the bridge checks (see [Changes in every build](#changes-in-every-build)) in release players, which otherwise leave them out. The Editor, Development Builds and `XLUA_GENERAL` builds have them without the define | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_bridge_checks), [Security.md](../Assets/XLua/Doc/Security.md#bridge-checks-in-debug-builds) |
| `XLUA_NO_BRIDGE_CHECKS` | Leaves the bridge checks out of the Editor, Development Builds and `XLUA_GENERAL` builds too, as in release players: for profiling the call costs of a Development player, and for programs that ship on `XLUA_GENERAL`. `XLUA_BRIDGE_CHECKS` wins when both are defined | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_no_bridge_checks) |
| `XLUA_RESTRICT_GETMETATABLE` | `xlua.getmetatable` returns a copy without `__gc` and the tag; editing it changes the type only after `xlua.setmetatable` | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_restrict_getmetatable) |
| `XLUA_LOCK_XLUA_TABLE` | `xlua` is a read-only proxy (`xlua.f = v` raises; code that adds functions must use `rawset`), `pairs(xlua)` lists nothing on LuaJIT, and xLua's helpers keep the functions they started with | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_lock_xlua_table) |
| `XLUA_RESTRICT_PRIVATE_ACCESS` | Without `HOTFIX_ENABLE`: no `xlua.access`, `xlua.private_accessible` or `xlua.hotfix`. With it: `xlua.access` reaches only `__Hotfix` fields. Always: `xlua.tofunction` and `xlua.get_generic_method` take public methods only | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_restrict_private_access) |
| `XLUA_NO_LOAD_ASSEMBLY` | No `xlua.load_assembly` | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_no_load_assembly) |
| `XLUA_STRICT_INT_RANGE` | An integral Lua number outside the range of an `sbyte`, `byte`, `short`, `ushort`, `char`, `int` or `uint` slot raises `value out of range` (`bad argument #1 to 'Foo' (value out of range)` for an argument) instead of wrapping: `Byte(300)`, `UInt(-1)`, `arr[2^32]`. `long` and `ulong` keep their bit patterns. Not part of any preset yet | [bridge-audit.md](research/bridge-audit.md#c4-part-2-the-other-integer-slots-changed) |
| `XLUA_NO_SIGNATURE_LOADER` | `SignatureLoader` is not compiled, and with it the RSA/SHA-256 code it pulls in (−239 KB on an Android APK at Minimal stripping; nothing at Medium, which already strips it) | [signature.md](../Assets/XLua/Doc/signature.md) |
| `XLUA_NO_TEMPLATE` | Players do not open the `template` Lua library and do not compile `XLua.TemplateEngine`. The Editor (`UNITY_EDITOR`) and the command-line tools keep both, for code generation and custom templates | [Custom_Generate_EN.md](../Assets/XLua/Doc/Custom_Generate_EN.md) |
| `XLUA_NO_UTIL` | Leaves out the embedded `xlua.util`; `require 'xlua.util'` then fails unless a loader or `Resources` provides it | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_no_util) |
| `XLUA_NO_PROFILER` | Leaves out the embedded `perf.profiler`, except in the Editor and in Development Builds (`UNITY_EDITOR`, `DEVELOPMENT_BUILD`) | [Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md) |
| `XLUA_ANDROID_SOURCE_PLUGIN` | Android imports the bridge from `__Internal` for a source-plugin build | [build/UNITY_SPIKE.md](../build/UNITY_SPIKE.md) |
| `XLUA_REDUCED_DEBUG` | In release players (not the Editor, Development Builds or the `XLUA_GENERAL` tools), a `LuaEnv` created without `LuaOptions.Libraries` gets a `debug` table with only `traceback` and `getinfo`. `xlua.util`'s `state` and `print_func_ref_by_csharp` then fail (they need `debug.getmetatable`, `setmetatable` and `getregistry`), and on LuaJIT `util.createdelegate` as well; debuggers and `debug.sethook` are gone. A `LuaEnv` that needs the whole library passes `Libraries = LuaLibs.All` | [presets.md](design/presets.md#the-minimal-column-as-implemented), [bridge-hardening.md](design/bridge-hardening.md#b-reduced-debug-library-audit-item-7-done-2026-10-18-with-option-1) |
| `XLUA_PRESET_MINIMAL` | The minimal preset: every define of its column at once, see [Presets](#presets) below | [presets.md](design/presets.md#implemented-legacy-and-minimal) |
| `XLUA_UTIL` | With `XLUA_PRESET_MINIMAL`: keeps the embedded `xlua.util`, which the preset leaves out. No effect without the preset | [presets.md](design/presets.md#the-minimal-column-as-implemented) |
| `XLUA_NO_REFLECTION` | No reflection fallback and no CodeEmit: a C# type without generated bindings keeps its class table, `typeof` and its objects, but Lua using a member it declares raises `cannot use T.m: T has no generated bindings ...` with the fix, and the Editor emits no delegate or interface bridges, as players. The reflection wrapper code is not compiled unless `xlua.private_accessible` exists. Part of the minimal preset, see [Reflection off in the minimal preset](#reflection-off-in-the-minimal-preset) | [presets.md](design/presets.md#reflection-fallback-and-codeemit-as-implemented) |
| `XLUA_REFLECTION` | With `XLUA_PRESET_MINIMAL`: keeps the reflection fallback and CodeEmit, which the preset turns off (`XLUA_NO_REFLECTION`). No effect without the preset | [presets.md](design/presets.md#reflection-fallback-and-codeemit-as-implemented) |
| `XLUA_NO_GCM` | With `XLUA_PRESET_MINIMAL`: keeps the plain wrappers (a native callback per wrapped member) instead of the `GEN_CODE_MINIMIZE` wrappers the preset selects since 2026-10-19. No effect without the preset, and none when `GEN_CODE_MINIMIZE` is defined too | [presets.md](design/presets.md#the-minimal-column-as-implemented) |
| `XLUA_MEMORY_DIAGNOSTICS` | Compiles the Lua memory diagnostics (`LuaEnv.TakeMemorySnapshot()`, `LuaMemoryDiagnostics.Diff()`, the `xlua.memory` module) into a release player. The Editor and Development Builds (`UNITY_EDITOR`, `DEVELOPMENT_BUILD`) always have them; other release players carry none of their code | [Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md#memory-snapshots) |

## Presets

A preset is a set of the opt-in defines above and of native build options. Two
exist ([presets.md](design/presets.md)):

- **`legacy`** is the default and needs nothing. It behaves as described on this
  page without any opt-in define.
- **`minimal`** leaves out what most projects do not use and turns on the bridge
  hardening. Select it in two places:
  1. **C#:** add the scripting define `XLUA_PRESET_MINIMAL` in Player Settings
     (Other Settings, Scripting Define Symbols) for each platform. It implies
     `XLUA_NO_STREAMING_ASSETS_LOADER`, `XLUA_NO_SIGNATURE_LOADER`,
     `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` (unless `XLUA_UTIL` is also defined),
     `XLUA_NO_PROFILER`, `XLUA_NO_LOAD_ASSEMBLY`, `XLUA_LOCK_METATABLES`,
     `XLUA_RESTRICT_GETMETATABLE`, `XLUA_LOCK_XLUA_TABLE`,
     `XLUA_RESTRICT_PRIVATE_ACCESS`, `XLUA_REDUCED_DEBUG`, `XLUA_NO_REFLECTION`
     (unless `XLUA_REFLECTION` is also defined) and, since 2026-10-19,
     `GEN_CODE_MINIMIZE` (unless `XLUA_NO_GCM` is also defined; below); each
     one's behaviour change is in the table above. The bridge checks follow
     the build type in every preset, minimal included (until 2026-10-19 the
     minimal preset kept them in release players). A `csc.rsp` entry also works for the
     C#, but the native plugin selector reads only Player Settings. Code of your
     own that tests one of those defines must also test `XLUA_PRESET_MINIMAL`.

     **The minimized wrappers (since 2026-10-19):** the preset selects the
     source generator's `GEN_CODE_MINIMIZE` mode ([below](#source-generator-preview)):
     every wrapped member is a C# method that xLua's one native trampoline
     calls, instead of a native callback of its own. A call from Lua into C#
     costs up to about 10 ns more (0 to 12 ns on the host benchmark), and an
     Android IL2CPP release player with a game-sized configuration (264 wrapped
     engine types) is about 700 KB smaller. The source generator reads the same
     rule from each assembly's defines, so `XLUA_PRESET_MINIMAL` must be defined
     for every assembly with bindings, as Player Settings do; an assembly whose
     mode differs from xLua's gets warning `XLSG013`. In the Editor and other
     Mono builds, a type without generated bindings now gets reflection
     wrappers instead of CodeEmit's emitted ones, as `GEN_CODE_MINIMIZE` always
     did (CodeEmit's delegate bridges stay). Projects that prefer the faster
     calls define `XLUA_NO_GCM` next to the preset: the rest of the preset
     stays, the wrappers are the plain ones, and xLua and the generator both
     follow it. `GEN_CODE_MINIMIZE` defined explicitly wins over `XLUA_NO_GCM`.
     Before, a minimal project used the plain wrappers unless it defined
     `GEN_CODE_MINIMIZE` itself.
  2. **Native:** build the `minimal` plugin variant next to the default plugin,
     `XLUA_VARIANT=minimal XLUA_PRESET=minimal build/make_android_lua55.sh` and
     `build/make_ios_lua55.sh` likewise, and copy `Plugins/<platform>/xlua-variants/minimal/`
     into `Assets/Plugins` (run **XLua/Plugin Variants/Copy Import Settings**
     once). The selector then ships it in release builds; a Development Build
     ships the `development` variant where one exists. The minimal library has
     no LuaSocket (`require "socket"` and `require "mime"` fail with "module not
     found"), no `io` library (`io` is `nil`); `os` has only `time`, `clock`,
     `date` and `difftime` (no `getenv`, `exit`, `remove`, `rename`,
     `setlocale` or `tmpname`); there is no `package.loadlib` and no C
     searchers, so `require` does not look on `package.cpath` and C modules
     cannot be loaded at run time (the library does not link `libdl`;
     `require` with `package.preload`, `package.path`, custom loaders and xLua's
     other searchers works); `dofile` and `loadfile` are `nil`; no leak checker
     (`General/LuaMemoryLeakChecker`) and no memory snapshot walk (except the
     iOS, tvOS and visionOS static libraries, since 2026-10-18: without it an
     iOS Development player did not link; release players leave it out), and on
     Android it exports only the functions the C# imports, so native Lua modules
     loaded at run time cannot link against the Lua API
     ([build/README.md](../build/README.md#presets)). Without the variant the
     build warns and uses the default plugin; the minimal C# works on it.
  3. **iOS Simulator:** a build with Player Settings > Target SDK = Simulator
     SDK needs simulator libraries: `XLUA_IOS_SDK=simulator` with the iOS
     scripts builds them as the variants `simulator` and `minimal_simulator`
     (`Plugins/iOS/xlua-variants/<name>/`), which the selector takes for
     simulator builds only ([build/README.md](../build/README.md#ios-simulator)).

Apart from `xlua.util` (`XLUA_UTIL`), the reflection fallback
(`XLUA_REFLECTION`) and the plain wrappers (`XLUA_NO_GCM`), a C# part of the
preset cannot be turned back on while the preset define is set; every native option set in the environment or with
`-D` wins over the preset (`XLUA_PRESET=minimal XLUA_LUASOCKET=ON ...`). The
owner decided the values that were provisional on 2026-10-18, as they were
([presets.md](design/presets.md#owner-decisions-2026-10-18)).

### Reflection off in the minimal preset

Since 2026-10-19 the minimal preset turns off the reflection fallback and
CodeEmit (`XLUA_NO_REFLECTION`). Lua reaches the members of a C# type only
through the bindings the source generator writes, in players and in the Editor
alike:

- **A type without bindings** keeps `CS.T`, `typeof(CS.T)` and its nested
  types, and its objects still pass through Lua and back to C#. Using a member
  the type declares raises, for example:

  ```text
  cannot use Game.Inventory.Count: Game.Inventory has no generated bindings, and this build
  does not reach C# through reflection (XLUA_NO_REFLECTION, part of XLUA_PRESET_MINIMAL).
  Mark it [LuaCallCSharp], or add [assembly: XLua.LuaCallCSharp(typeof(Game.Inventory))] for a
  type you cannot change (XLua > Migrate Configuration to Attributes converts configuration
  lists), or define XLUA_REFLECTION
  ```

  `cannot set T.m` and `cannot construct T` follow the same pattern. Members of
  a base class that has bindings still work through the type
  (`myScript.gameObject` on a `MonoBehaviour` subclass without bindings), and a
  name no type declares is `nil`, as before.
- **Enums without bindings:** values pass and numbers convert, but `CS.E.Name`
  and `CS.E.__CastFrom` raise; mark the enum `[LuaCallCSharp]`.
- **`[GCOptimize]` structs without bindings:** `[GCOptimize]` gives a struct
  its pusher and reader, not a wrapper, so Lua gets the values but `v.x`
  raises `has no generated bindings`. Since 2026-10-28 the source generator
  says so at compile time: warning XLSG014 names each struct that an assembly
  configures with `[GCOptimize]` and that neither it nor a referenced assembly
  binds. Mark the struct `[LuaCallCSharp]` too (or
  `[assembly: XLua.LuaCallCSharp(typeof(T))]`). A packable
  `OptimizeFlag.PackAsTable` struct is a Lua table and needs nothing. xLua's
  own UnityEngine defaults (`Vector3` and the others) and the structs of
  other referenced assemblies are reported (since owner decision Q41 of
  2026-10-28) only where a member your assembly binds passes one, such as a
  bound method that returns a `Vector3`; the warning names that member. Bind
  the ones your Lua code reads fields of.
- **`obj:ToString()`, `obj:GetType()`, `obj:Equals()`** come from
  `System.Object`, and `t.Name` from `System.Type`, which the preset does not
  bind for you (owner decision, 2026-10-28): bind them
  (`[assembly: XLua.LuaCallCSharp(typeof(object), typeof(System.Type))]`; the
  examples bind `object`) if your Lua code uses them. Without bindings their
  error ends with a hint: `tostring(obj)` and `tostring(typeof(CS.T))` work
  without bindings and give `ToString()` followed by `: ` and the hash code.
  `xlua.util` helpers that use C# types, such as `util.createdelegate`, need
  those types bound too.
- **Delegates and interfaces:** the Editor no longer emits bridges for
  `[CSharpCallLua]` types without generated ones, so a missing bridge fails in
  the Editor as it does on the device: `This type must add to CSharpCallLua: T.
  Mark it [CSharpCallLua] ...`. Delegates that Lua calls and `CS.D(f)` work as
  before.
- **Lua tables converted to classes or structs** (since 2026-10-28, owner
  decision): a table where C# expects a class or struct converts only through
  the converter the source generator writes for a `[GCOptimize]` type (a
  struct, or a class with a public parameterless constructor). Before, the
  preset still filled the object's public fields from the table through
  reflection. This covers parameters, fields and properties Lua sets,
  `LuaTable.Get<T>` and `Cast<T>`, `LuaFunction.Func` and `Cast<T>`, delegate
  and interface results, and the elements of arrays, lists and dictionaries.
  Otherwise it raises, for example:

  ```text
  cannot convert a table to Game.SpawnInfo: Game.SpawnInfo has no generated table converter,
  and this build does not fill C# objects from tables through reflection (XLUA_NO_REFLECTION,
  part of XLUA_PRESET_MINIMAL). Mark it [GCOptimize], or add
  [assembly: XLua.GCOptimize(typeof(Game.SpawnInfo))] for a type you cannot change, so that the
  source generator writes one; or pass a Game.SpawnInfo object instead of a table, or define
  XLUA_REFLECTION
  ```

  `[LuaCallCSharp]` writes no table converter. A C# object of the type still
  passes, and a table still converts to an array, a `List<T>` or a
  `Dictionary<K, V>`, whose elements follow the same rule. A packable
  `[GCOptimize]` struct that a table converts to through `Cast<T>` or as a
  list element now uses its generated reader, which reads the fields the
  same way and also the `[AdditionalProperties]` properties. Legacy, and the
  preset with `XLUA_REFLECTION`, are unchanged.
- **Still through reflection:** the members the source generator leaves to
  reflection on a bound type (`[DoNotGen]`, info XLSG004), which it names at
  compile time and preserves for the linker; `xlua.private_accessible` (with
  `HOTFIX_ENABLE`), which on a type without bindings now also makes its public
  members and constructors reachable; `xlua.get_generic_method`,
  `xlua.tofunction`, `xlua.access`. These are explicit: a script names them,
  and the preset keeps them (owner decision, 2026-10-28).
- **`pairs`** over a C# collection works without bindings for the collection
  or its enumerator.
- **`[ReflectionUse]`** keeps only its meaning for the linker (link.xml); it
  does not make its type reachable without bindings (owner decision,
  2026-10-28). `XLUA_REFLECTION` is the opt-in, for the whole project.
- **To keep reflection**, define `XLUA_REFLECTION` next to
  `XLUA_PRESET_MINIMAL`. A project without the source generator needs it, since
  nothing is bound there. **XLua > Validate Setup** warns when the preset is on
  without it and an assembly has configuration but no generated bindings.

### `LuaOptions.Libraries`

- **New option, no change by default:** `new LuaEnv(new LuaOptions { Libraries
  = LuaLibs.Safe | LuaLibs.Package })` opens only the selected standard
  libraries; `LuaLibs.Default` (0, also what `new LuaEnv()` uses) keeps every
  library the native library contains, except under `XLUA_REDUCED_DEBUG` (above).
  The base library is always open, and a library the native build does not
  contain (CMake `XLUA_LIBS`) stays absent even when asked for. The bridge keeps
  what it needs: without `LuaLibs.Debug`, `debug` has `traceback` and `getinfo`
  (the error handler calls `debug.traceback`); without `LuaLibs.Package`, scripts
  have no `package` table but `require` and the loaders keep working. Without
  `LuaLibs.String`, strings have no metatable (`("x"):upper()` fails, and so
  does Lua 5.4's string-to-number coercion in arithmetic). A removed library is
  also removed from `package.loaded`. The bits are those of LuaScript's
  `LuaLibs` ([core-luascript.md](design/core-luascript.md), section 2.14) plus
  `Bit`, `Jit` and `Ffi` for LuaJIT. See
  [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#luaenv-luaenvluaoptions-options).
- **`setfenv` and `getfenv` on Lua 5.2 and later** (`LuaEnv`'s emulation) now
  use the `debug.getinfo`, `debug.getupvalue` and `debug.upvaluejoin` that
  exist when the environment starts, so a script that later replaces those
  fields no longer changes them; they keep working with a reduced `debug`.
- **`xlua.util`** takes the enum `|` helper from Lua 5.3's operator when
  `debug.getmetatable` is missing; with the whole `debug` library nothing
  changes.

## Native plugins

- **Stripped Android libraries (2026-10-18):** `build/make_android_*.sh` copy
  `libxlua.so` into `Plugins/Android/.../libs/<abi>/` stripped with the NDK's
  `llvm-strip --strip-unneeded` (Lua 5.5.1 arm64-v8a: 384,040 bytes instead of
  2,066,128). Every export stays, and both files keep the same GNU build ID.
  The linked library with its symbols goes to `plugin_<runtime>/symbols/Android/`
  with the same layout, for `ndk-stack -sym` and the Play Console's native
  debug symbols; `native-build.yml` uploads it as the
  `xlua-lua-<version>-android-symbols` artifact and `publish.yml` as the
  release asset `android-symbols_<tag>.tgz`. Unity 2021.3 packs Android plugins
  as they are, so its APKs shrink (about 640 KB to 160 KB compressed); Unity
  6000.x stripped them already. `XLUA_ANDROID_STRIP=OFF` copies the linked
  library as before. See [build/README.md](../build/README.md#stripped-libraries-and-symbols).
- **iOS Simulator plugins from CI (2026-10-18):** `native-build.yml` and
  `publish.yml` build the arm64 simulator library of Lua 5.3, 5.4 and 5.5 next
  to the device library, as the plugin variant
  `Plugins/iOS/xlua-variants/simulator/libxlua.a`, so the release archives
  carry it. The plugin selector takes it only for a Target SDK = Simulator SDK
  build; device players are unchanged. LuaJIT has no simulator build.
- **Windows x64 LuaJIT is GC64 (2026-10-28):** `build/make_win64_luajit.bat`
  built LuaJIT without GC64 (`msvcbuild_mt.bat nogc64`, upstream xLua's
  Windows x64 mode) and now builds it in GC64 mode, like every other 64-bit
  LuaJIT plugin; `make_win64_luajit_gc64.bat`, which did that already, is
  gone. A 64-bit LuaJIT without GC64 is no longer supported: `xlua.c` stops
  with an `#error` when compiled with `LUAJIT_DISABLE_GC64`, and CMake's
  configure refuses a `lua51.lib` or `libluajit.a` built without GC64. Since
  the LuaJIT upgrade such a library had failed the first `LuaEnv`
  (`NullReferenceException` in `ImportType`), because `xlua_gl` read
  `lua_State` in the other layout. **Recompile LuaJIT bytecode** made for the
  old Windows x64 plugin: a GC64 runtime rejects non-GC64 bytecode (the
  `BCDUMP_F_FR2` flag, `lj_bcread.c:403`), so `require` or `DoString` of such
  a chunk fails. Use a GC64 `luajit -b`, or `luajit -b -X` from any LuaJIT.
  The Lua heap on Windows x64 is no longer limited to the low 2 GB. 32-bit
  LuaJIT (x86, armv7) has only the non-GC64 mode and is unchanged. `xlua_gl`
  returns LuaJIT's `global_State` pointer again, as upstream did; on `main`
  between the Windows host change and this one (both 2026-10-28) it returned
  the registry's address, which cost about 1.5 ns per C# call from Lua. See [build/README.md](../build/README.md#luajit-gc64) and
  [windows-host.md](research/windows-host.md#luajit-without-gc64).
- **Parts of `os`, `package` and the base library as options:** the CMake
  options `XLUA_OS_FULL`, `XLUA_LOADLIB` and `XLUA_DOFILE` (on by default, off
  in the minimal preset, also read from the environment) build `os` with only
  `time`, `clock`, `date` and `difftime`; no `package.loadlib`, C searchers or
  `dlopen`; and no `dofile` or `loadfile`. The default library is unchanged.
  On PUC Lua the vendored `loslib.c`, `loadlib.c` and `lbaselib.c` leave the
  code out (guards recorded in [build/LUA_PATCHES.md](../build/LUA_PATCHES.md));
  on LuaJIT the fields are removed when the libraries are opened and the code
  stays. See [build/README.md](../build/README.md#parts-of-os-package-and-the-base-library).
- **Native bridge version 108:** the plugin adds `xlua_tryget_cachedud_checked`
  and `xlua_check_struct_accessors` for the bridge checks
  (`XLUA_BRIDGE_CHECKS`). The C# side checks the version, so rebuild the
  plugins together with the C# update. A release player without the checks
  does not call them.
- **Native bridge version 109:** the plugin adds `xlua_pnext`, `xlua_plen`
  and `xlua_running` for the crash fixes of 2026-10-19 (see the table above).
  The C# side no longer imports `xlua_objlen`. Rebuild the plugins with the
  C# update.
- **Native bridge version 111:** the plugin adds `xlua_tostructbuffer`, which
  C# and source-generated code call for a struct argument instead of
  `xlua_gettypeid` and `lua_touserdata` (the struct bounds row of the table
  above). Rebuild the plugins with the C# update and build again, so that the
  source generator's code calls it.
- **Native bridge version 112 (2026-10-28):** the plugin adds
  `xlua_pushcsobj_checked`, `xlua_pushstruct_checked`,
  `xlua_pushcstable_checked`, `lua_pushint64_checked`,
  `lua_pushuint64_checked` and `xlua_check_registry_refs` for the registry
  checks of debug builds (owner decision Q37; "Changes in every build").
  Rebuild the plugins with the C# update; a release player without the bridge
  checks does not call them.
- **Windows ARM64:** `build/make_win_arm64_*.bat` build `Plugins/arm64/xlua.dll`
  for Unity 2023.1+ Windows ARM64 players; upstream has no Windows ARM64
  plugin. See [build/README.md](../build/README.md#windows-libraries).
- **`Plugins/x86_64/xlua.dll` import settings:** Standalone Win64 now has CPU
  `x86_64` instead of `AnyCPU`, so a Windows ARM64 player takes only
  `arm64/xlua.dll`. A project that keeps upstream's `.meta` and adds the ARM64
  DLL gets both in an ARM64 player.
- **`Plugins/arm64/libxlua.dylib` import settings (macOS):** the macOS
  standalone CPU is now `AnyCPU` instead of `ARM64`. With `ARM64`, Unity
  2021.3 copies the plugin to `Contents/PlugIns/ARM64` of the player, where
  its Mono player does not look: an ARM64 player with only this plugin failed
  with `DllNotFoundException` (Unity 6000.3 loads it from there). The dylib
  is arm64 only (`make_osx_silicon_*.sh`); the universal `Plugins/xlua.bundle`
  (`make_osx_*.sh`, x86_64 and arm64) is unchanged and still serves Intel and
  universal players. Take the new `.meta` together with the plugin.
- **Compiler warnings and new CMake options:** xLua's own C files compile
  with a warning set (`-Wall -Wextra -Wpedantic` and more; `/W4` on MSVC),
  and the vendored Lua and LuaJIT include directories are `SYSTEM`
  directories. A missing return value, an implicit function declaration and
  integer/pointer mismatches are always errors. New options, all off by
  default: `XLUA_WERROR` (warnings become errors; CI uses it), `XLUA_HARDEN`
  (stack protector, PAC/BTI on arm64, zeroed locals,
  `-fno-delete-null-pointer-checks`, `_FORTIFY_SOURCE=2`, `/guard:cf`) and
  `XLUA_APICHECK` (Lua's C API checks, for test builds). `XLUA_WERROR` and
  `XLUA_HARDEN` also read an environment variable of the same name. See
  [build/README.md](../build/README.md#warnings-hardening-and-test-options).
- **The cheap hardening set is on by default** (2026-10-18, every preset):
  CMake `XLUA_CHEAP_HARDEN` compiles every C file of the library with
  `-fno-delete-null-pointer-checks` and xLua's own files with
  `-ftrivial-auto-var-init=zero` (GCC and Clang; MSVC gets nothing). A null
  check after a dereference is no longer optimised away, and uninitialised
  locals in xLua's files read as zero. Lua's behaviour does not change; the
  Android libraries grow by 0.3 to 0.5 KB stripped (0.1 to 0.5 KB of APK) at
  no measured speed cost ([SIZE.md](../build/SIZE.md#cheap-hardening)).
  `-DXLUA_CHEAP_HARDEN=OFF` or `XLUA_CHEAP_HARDEN=OFF` in the environment
  builds the library of before. LuaJIT's `libluajit.a` and the WebGL and
  Android source plugins are not affected.
- **macOS 12.0 and iOS 15.0 minimum.** The macOS plugins (the universal
  `xlua.bundle` and `Plugins/arm64/libxlua.dylib`, Lua 5.x and LuaJIT) are
  built for macOS 12.0 (Monterey) and later; before 2026-10-18 the Lua 5.x
  ones needed the macOS of the machine that built them (macOS 27 for a build
  on macOS 27). The iOS libraries
  were already built for iOS 15.0. These are the lowest targets Xcode 27
  builds. Unity 2021.3 to 6000.2 players also run on older systems (macOS
  10.13 to 11, iOS 12 or 13); for those, build the plugin with an older
  Xcode and `MACOSX_DEPLOYMENT_TARGET` or `IOS_DEPLOYMENT_TARGET`. See
  [build/README.md](../build/README.md#macos-and-ios-deployment-targets).
- **Android: minimum API 23 and size flags.** The Android scripts and CI now
  build for API 23 instead of 21 (`ANDROID_API_LEVEL` still overrides it;
  CMake warns below 23). The libraries do not load on Android 5.x (API 21
  and 22): they have only a GNU hash table and packed relocations. A Unity 2021.3 or 2022.3 project whose
  minimum API level is 22 must raise it to 23 (Unity 6 already requires 23
  or more). Android and OpenHarmony builds also turn on `XLUA_SIZE_FLAGS`:
  `-O3` for the interpreter's hot files and `xlua.c`, `-Oz` for the rest,
  identical code folding and packed relocations. The Lua 5.5.1 library is
  48 KB (arm64-v8a) and 25 KB (armeabi-v7a) smaller stripped and 11 KB and
  10 KB smaller in the APK, at the same speed. Two trivial C functions that
  Lua can see, `xlua.genaccessor`'s third result and LuaSocket's internal
  `do_nothing`, now share an address and compare equal; `-DXLUA_ICF=OFF`
  keeps them apart. `-DXLUA_SIZE_FLAGS=OFF` restores the previous `-O3`
  build. Exports are unchanged: `-DXLUA_EXPORTS=managed` (off by default)
  exports only the functions the C# imports, which saves another 6 to 7 KB
  of APK but breaks native plugins that call other Lua or xLua functions.
  See [build/README.md](../build/README.md#android-size-flags).
- **LuaSocket and mime options:** `-DXLUA_LUASOCKET=OFF` builds the library
  without LuaSocket and `mime.c`, `-DXLUA_MIME=OFF` without `mime.c` only;
  both default to on, and both also read an environment variable of the same
  name (`XLUA_LUASOCKET=OFF build/make_android_lua55.sh`). Without LuaSocket,
  `require "socket"` fails with "module 'socket' not found", and the Lua 5.5.1
  library is 44 KB (arm64-v8a) and 30 KB (armeabi-v7a) smaller stripped, 18 KB
  and 15 KB in the APK, and 28 KB of code on iOS. Registering `mime` adds the
  6 KB of `mime.c` back where the linker used to drop it: on iOS and with
  `-DXLUA_EXPORTS=managed`; `-DXLUA_MIME=OFF` gives the previous size there.
  See [build/README.md](../build/README.md#luasocket-and-mime) and
  [build/SIZE.md](../build/SIZE.md#without-luasocket-or-mime).
- **Leak checker, uint64 table and standard library options:** three new
  CMake options, each also read from an environment variable of the same
  name and each defaulting to today's library. `-DXLUA_LEAK_CHECKER=OFF`
  leaves out `xlua_report_table_size`, `xlua_report_object_relationship`,
  `xlua_registry_pointer` and `xlua_global_pointer`, which only
  `General/LuaMemoryLeakChecker` imports (Lua 5.3 to 5.5). `-DXLUA_UINT64=OFF`
  (Lua 5.3 to 5.5; refused for LuaJIT) leaves the global `uint64` table and the
  export `uint64_parse` out; C# `long` and `ulong` values still keep all 64
  bits, but a `ulong` of 2^63 or more reads as a negative integer in Lua, as it
  already did with the table. `-DXLUA_LIBS=<list>` builds and opens only the
  listed standard libraries (`package` and `debug` are required by `LuaEnv`);
  the others are `nil` globals and their `luaopen_*` exports are gone, and
  `build/xlua_libs.c` replaces Lua's `linit.c` in such a build. A build that
  compiles the sources without CMake is unchanged; it can define
  `XLUA_NO_LEAK_CHECKER` and `XLUA_NO_UINT64`. On Android arm64-v8a the first
  two save 1.4 KB stripped each, a library without `io`, `os` and `utf8` 17.6 KB.
  See [build/README.md](../build/README.md#leak-checker-uint64-table-and-standard-libraries).
- **The `XLUA_EXPORTS=managed` list follows the options:** `build/xlua_exports.txt`
  has a second column naming the CMake option a function needs, and CMake
  leaves those functions out when the option is off (it did so for the memory
  snapshot by a fixed rule before). A tool that reads the file must take the
  first word of each line. `check_exports.py` and `check_android_elf.py`
  accept a library without an optional part and check what the
  `XLUA_EXPECT_*` variables say.
- **Plugin variants:** `Assets/XLua/Core/Editor/PluginVariants.cs` (in
  `Assets/XLua/Src/Editor` before 2026-10-18) registers a
  `PluginImporter.SetIncludeInBuildDelegate` on xlua native plugins that have
  variants (the default plugin's path with `xlua-variants/<name>/` inserted)
  and picks one per platform and CPU: the variant a scripting define
  `XLUA_PLUGIN_<NAME>` names, `core` with `XLUA_CORE_ONLY`, `development` in a
  Development Build, else the default plugin. A project without
  `xlua-variants` folders is not affected; a project with two `XLUA_PLUGIN_`
  defines fails to build. `XLUA_VARIANT=<name>` makes the Android scripts and
  the Lua 5.x iOS scripts (`make_ios_lua5*.sh`) build into that layout. See
  [build/README.md](../build/README.md#plugin-variants).
- **`XLUA_HARDEN` reaches LuaJIT:** with `XLUA_HARDEN=ON` in the
  environment, the LuaJIT build scripts pass the hardening flags to
  LuaJIT's own Makefile, which turns off the stack protector and
  `_FORTIFY_SOURCE` by itself. An arm64 LuaJIT library is then marked for
  BTI. Before, `libluajit.a` stayed unhardened.
- **Instruction and time limits (`LuaOptions.InstructionLimit`, `TimeLimit`):**
  new in `LuaEnv(LuaOptions)` on Lua 5.4 and 5.5, with
  `LuaEnv.InstructionLimit`, `TimeLimit`, `LastInstructionCount`,
  `Interrupt()` and the exceptions `LuaLimitException`,
  `LuaInstructionLimitException`, `LuaTimeLimitException` and
  `LuaInterruptedException` (all `LuaException`s). New exports:
  `xlua_core_set_limits`, `xlua_core_interrupt`, `xlua_core_watch`; the core
  ABI version is 2 (`xlua_core_stats` has three more fields). Visible
  changes for such environments, also without a limit set:
  `coroutine.resume`, `coroutine.wrap` and `coroutine.close` are xlua-ng's
  versions (the same behaviour, plus the budget moving between threads),
  and `xlua.sethook` (the profiler) shares the hook with the limits; while a
  limited call runs, it also sees the coroutines the call resumes.
  `Interrupt()` stops only a call with a limit, unless the environment was
  created with `LuaOptions.AllowInterrupt = true` (2026-10-18), which costs
  about 6 ns per call from C#.
  `new LuaEnv()` is unchanged. A source plugin or
  other build that compiles the C sources itself must add
  `build/xlua_core_limits.c`. See
  [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#luaenv-luaenvluaoptions-options).
- **C budget patch in the vendored Lua 5.4.9 and 5.5.1:** `lstrlib.c` and
  `ltablib.c` charge long pattern matches, plain finds, `table.move` and
  (since a second pass on 2026-10-18) `table.insert`, `table.remove`,
  `table.concat` and `table.sort` to the instruction and time limits through
  `luai_budget`, which does nothing outside libxlua; Lua 5.5's `luaconf.h`
  gets the same hook include as 5.4's `luaconf.h.in`. Results are unchanged
  (Lua's own test suites pass). On Lua 5.4.9, `string.rep` of an empty
  string with an empty separator returns `""` at once, as on Lua 5.5, in
  every build; it used to make n - 1 empty copies in C. See
  [build/LUA_PATCHES.md](../build/LUA_PATCHES.md).
- **Memory snapshot export:** the plugin exports `xlua_memory_snapshot` and
  `xlua_memory_snapshot_free` (`build/memory_leak_checker.c`), the native
  walk behind `LuaEnv.TakeMemorySnapshot()`. CMake now compiles
  `memory_leak_checker.c` for LuaJIT too; the internal-header leak checker
  in it (`xlua_report_table_size`, `xlua_report_object_relationship`) is
  still Lua 5.3 to 5.5 only. The new CMake option `XLUA_MEMORY_SNAPSHOT`
  (also read from the environment) builds the walk: on by default for
  macOS, Windows, Linux and iOS, off for Android, OpenHarmony, UWP and
  Switch, where it would add about 7.5 KB to every release library. The
  default Android library is the same size as before. For snapshots in a
  development Android build use `XLUA_MEMORY_SNAPSHOT=ON
  build/make_android_lua55.sh`; with a library built without it,
  `TakeMemorySnapshot` throws `NotSupportedException`. See
  [build/README.md](../build/README.md#memory-snapshot).
- **The core preset's native library (`XLUA_PRESET=core`):** it now
  configures instead of stopping with "not implemented yet", and builds
  LuaScript's native sandbox without the C# bridge (Lua 5.4 and 5.5; see
  [build/README.md](../build/README.md#presets)). The C# `LuaScript` that uses
  it came with milestone 6 ([XLua.Core and LuaScript](#xluacore-and-luascript));
  nothing changes for `LuaEnv`. The sandbox's exports
  (`xlua_core_open`, `xlua_core_newenv`, `xlua_core_unref`, `xlua_core_call`
  (`xlua_core_run` before milestone 6), `xlua_core_set_resolver`,
  `xlua_core_set_require`) are in a bridge library
  only with the new CMake option `XLUA_LUASCRIPT=ON` (off by default, so the
  legacy and minimal libraries are unchanged). A source plugin or other build
  that wants them compiles `build/xlua_core_sandbox.c` with `XLUA_SANDBOX` and
  `XLUA_LIB_<NAME>` for each library, and `xlua_core.c` and (since 2026-10-28)
  `xlua_core_limits.c` with `XLUA_SANDBOX`.
- **Bytecode options (opt-in):** two new CMake options, off by default and
  also read from the environment, so the default libraries are unchanged.
  `-DXLUA_BYTECODE_ONLY=ON` (Lua 5.3 to 5.5 with the bridge; refused on LuaJIT)
  leaves Lua's compiler out: every text chunk then fails with "text chunks are
  not supported in this build (bytecode only)". `LuaEnv` runs on such a
  library: its own Lua source (`init_xlua`, the library removal, the `pairs`
  helper, `xlua.util`, `perf.profiler`, `xlua.memory`) is compiled at build time
  from the C# strings between `<xlua-chunk>` markers and carried by the library
  (new options `XLUA_BYTECODE_UTIL`, `XLUA_BYTECODE_PROFILER`,
  `XLUA_BYTECODE_MEMORY` for the modules); C# loads that copy instead of the
  text. Lua 5.3 for a 32-bit target built on a 64-bit host needs
  `LUAC_COMPATIBLE_FORMAT=ON` with it. `template.compile` and
  `LuaTemplate.Compile` make Lua text at run time and fail there. A build
  without CMake compiles `build/xlua_noparser.c` instead of `lparser.c`,
  `lcode.c` and `llex.c`, and `xlua.c` with `XLUA_BYTECODE_ONLY` and the
  `xlua_builtin_chunks.h` that `build/xlua_chunkc.c` writes
  (`xlua_chunkc -D XLUA_BYTECODE_UTIL ... -o xlua_builtin_chunks.h Assets/XLua/Src/*.cs`).
  A change to one of those C# strings needs a new bytecode-only library; an old
  one reports that it was built from other C# sources.
  `-DXLUA_TRUSTED_BYTECODE=ON` (with LuaScript's sandbox) lets the new export
  `xlua_core_run_mode` load the host's precompiled chunks; the core library then
  contains Lua's bytecode loader. Since milestone 6 the load mode is the `mode`
  field of `xlua_core_call`, which replaced `xlua_core_run` and
  `xlua_core_run_mode` ([XLua.Core and LuaScript](#xluacore-and-luascript)). See [build/README.md](../build/README.md#bytecode-options).
- **`LUA_COMPAT_APIINTCASTS` no longer defined for LuaSocket:** nothing used
  its macros (`lua_pushunsigned`, `luaL_checkint` ...), and the libraries are
  byte for byte the same. A build that compiles LuaSocket itself can drop the
  definition too.
- **`LuaEnv(LuaOptions)` and new core exports:** `new LuaEnv(new LuaOptions
  { MemoryLimit = ..., HashSeed = ... })` creates the state with
  `xlua_core_newstate` (a counting allocator) instead of `luaL_newstate`; it
  is closed with `xlua_core_close`. New exports: `xlua_core_version`,
  `xlua_core_newstate`, `xlua_core_close`, `xlua_core_set_memory_limit` and
  `xlua_core_get_stats` (`build/xlua_exports.txt`). `new LuaEnv()` is
  unchanged. The exported `lua_pcall` (Lua 5.3 and later) and
  `xluaL_loadbuffer` now also mark a protected entry for such states; for a
  `new LuaEnv()` state they only add a check of the allocator. See
  [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#luaenv-luaenvluaoptions-options).
- **Lua 5.4 `luaconf.h.in`:** three lines include `build/xlua_luahooks.h`
  when `XLUA_LUA_HOOKS` is defined (CMake does for libxlua), which routes
  Lua 5.4's seed through `xlua_core.c`. A 5.4 build without that define keeps
  upstream's seed and rejects `HashSeed`. See
  [build/LUA_PATCHES.md](../build/LUA_PATCHES.md).
- **New source file `build/xlua_core.c`:** the GC option wrapper
  (`xlua_gc`), the C# function trampolines (`xlua_push_csharp_function`,
  `xlua_push_csharp_wrapper` and their error helpers) and the error handler
  (`errorfunc`, `get_error_func_ref`) moved from `xlua.c` into it, with the
  internal header `xlua_core.h`. The exports and their behaviour are
  unchanged. A build that compiles the C sources without
  `build/CMakeLists.txt`, such as a source plugin or another build system,
  must add `xlua_core.c`; `Assets/Plugins/WebGL/xlua_webgl.cpp` already
  includes it. In CMake, the list `XLUA_CORE` (the bridge files) is now
  `XLUA_BRIDGE_SOURCES`, the new `XLUA_SOURCES` holds every xLua file of the
  build, and the new cache option `XLUA_PRESET` accepts only `legacy` (the
  default) so far. See
  [build/README.md](../build/README.md#native-layers-and-presets).
- **CMake `GC64` option removed:** it defined `LUAJIT_ENABLE_GC64`, which
  LuaJIT 2.1 ignores, so it never changed a build. The LuaJIT GC mode comes
  from how LuaJIT itself is built, and every 64-bit build must be GC64 (the
  Windows x64 entry above); passing `-DGC64=ON` now only gives CMake's
  unused-variable warning.
- **Desktop plugin import settings (2026-10-28):** upstream's `.meta` files
  enabled `Plugins/x86_64/libxlua.so` for Windows x64 players,
  `Plugins/x86/libxlua.so` for Windows x86 players, `Plugins/x86_64/xlua.dll`
  for Linux x64 (and 64-bit Intel macOS) players and `Plugins/x86/xlua.dll`
  for 32-bit Linux and macOS players, so those players carried a second
  library they cannot load. Each plugin is now enabled only for its own OS;
  the Editor loads `libxlua.so` on Linux only (was any OS) and
  `arm64/libxlua.dylib` on macOS only (was any OS). Players and editors on
  the plugin's own OS are unchanged. `Assets/Plugins/arm64.meta` is new.
  `Tools/check_meta.py` checks these settings (Test/README.md, "Unity .meta
  files"). Not checked in a Unity editor or player yet.
- **32-bit desktop plugins no longer load in the Editor (2026-10-28, owner
  decision Q40):** `Plugins/x86/xlua.dll` and `Plugins/x86/libxlua.so` were
  enabled for the Editor with CPU `x86`, which Unity's Editor CPU enum does
  not have: the Unity Editor is 64-bit only, so the setting could never
  apply. Their `.meta` files now have the Editor off; they stay enabled for
  32-bit Windows and Linux players. The Editor keeps loading the `x86_64`
  and `arm64` plugins, so nothing changes in a 64-bit Editor; a project that
  copied these `.meta` files into a fork for a 32-bit editor build has no
  such editor to run them in. `Tools/check_meta.py` fails for an enabled
  Editor with a CPU outside its enum. Not checked in a Unity editor.
- **Reproducible native builds (2026-10-28):** `build/CMakeLists.txt` maps
  the checkout's `build/` folder to `build` and the build directory to `.`
  in debug information and `__FILE__` (`-ffile-prefix-map`; MSVC `/pathmap`
  and `/Brepro`), so two checkouts give the same bytes. The Android
  libraries and their `symbols/` files change once: the DWARF names
  `build/lua-5.5.1/src/lvm.c` instead of the absolute path of the machine
  that built it, so a debugger that shows source lines needs a source map to
  the checkout (LLDB: `settings set target.source-map build
  /path/to/xlua-ng/build`). Symbol names and line numbers are unchanged, and
  the Linux and Apple release libraries keep their bytes. `__DATE__`,
  `__TIME__` and `__TIMESTAMP__` are now errors in every C file of the
  library. See [build/README.md](../build/README.md#reproducible-builds).

## Tooling

- **Hotfix:**
  - The injector checks its tool DLLs against a manifest written by
    `Tools/build_tools.py`.
  - `Mono.Cecil.Rocks.dll` is required.
  - The Unity 6.3 build step compiles again.
  - `Tools/build_tools.py --cecil-dir` can take Mono.Cecil from Unity's
    `com.unity.nuget.mono-cecil` 1.11.5/1.11.6 package instead of nuget.org.
    The default is unchanged. The manifest now also records which Cecil the
    tools were built with (`cecil=`).
  - **Several hotfix assemblies:** the Editor still writes
    `hotfix_id_map_<assembly>.lua.txt` for each one, and now also
    `hotfix_id_map.lua.txt`, the union of those maps. Upstream wrote no
    `hotfix_id_map` then, so `xlua.util.auto_id_map()` failed with "module
    'hotfix_id_map' not found", or used a stale map from an earlier
    single-assembly build, which gave wrong ids. A project that ships the
    per-assembly maps under their own names keeps working; it can ship
    `hotfix_id_map.lua.txt` alone instead. `XLuaHotfixInject --merge-id-maps`
    does the same merge outside Unity.
  - **IntKey outside the xLua assembly:** such types silently got field mode
    (a static field per method, no ids). The injector now logs a warning
    that names them, and the Editor shows it as a warning. With the source
    generator's hotfix bridges in the assembly (source generator below) IntKey
    works there, with ids of its own, and there is no warning. From
    2026-10-18 until Generate Code was removed (M11), the assembly that held
    its bridges (Assembly-CSharp when xLua is in its own assemblies) also kept
    IntKey, with the process-wide ids that only xLua's assembly had.
  - **Outside the xLua assembly, the injector now also** injects `Inline` types
    and generic methods (it skipped them without a word), honours `[Hotfix]`
    on the types of that assembly (it compared the attribute with xLua's own
    attribute type by reference, so such types were not injected unless a
    configuration list named them), reads
    `[assembly: Hotfix(typeof(T), flags)]`, and calls bridges that are in the
    injected assembly directly instead of through a delegate type it creates.
  - **`XLuaHotfixInject` outside Unity** also resolves references to `.exe`
    assemblies in its search paths, so an assembly that references the
    program holding xLua can be injected. An id map path without a
    directory part (`hotfix_id_map.lua`) is written to the working
    directory; upstream threw `ArgumentException` and injected nothing.
  - **Virtual methods with `in` or `ref readonly` parameters** (Roslyn marks
    such a parameter of a virtual or override method with
    `modreq(InAttribute)`): injection stopped with "can not find delegate"
    for them, because the injector did not see the parameter as by
    reference. They are now injected like other `in` parameters, in every
    hotfix mode: a patch's extra Lua results are never written back into
    them. Since 2026-10-19 (`fix(hotfix): inject methods whose in parameters
    carry modreq`).

  See [Hotfix_EN.md](../Assets/XLua/Doc/Hotfix_EN.md).
- **XLua/Validate Setup** checks the installation. Since 2026-10-18 it also
  lists asmdefs under `Assets` whose scripts use xLua without referencing
  `XLua` (and, with the source generator, `XLua.Core`), with the fix. It also lists the Lua
  files that earlier versions shipped in `Assets/XLua/Resources`
  (`xlua/util`, `perf/profiler`, `perf/memory`, `tdr/tdr`) when an in-place
  upgrade left them in any `Resources` folder, and says whether each one is
  unchanged from a shipped version; see
  [LuaCATS.md](../Assets/XLua/Doc/LuaCATS.md#validate-setup).
- **LuaCATS** definitions are generated for LuaLS; see
  [LuaCATS.md](../Assets/XLua/Doc/LuaCATS.md).
- **XLua/Project Layout** is new: Use Assembly-CSharp (no asmdefs) and Use
  asmdefs move xLua's four asmdefs out of the way and back; see
  [the way back](#the-way-back-everything-in-assembly-csharp).
- **XLua/Lua Memory** is a new Editor window: it takes memory snapshots of
  the running LuaEnv in Play Mode and shows one snapshot or the difference
  between two, with the Lua paths that hold tables and C# objects. Upstream
  had only `General/LuaMemoryLeakChecker` outside Unity and the
  `perf.memory` stub; see
  [Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md#the-lua-memory-window).

## Generate Code removed

Since 2026-10-18 (source generator milestone M11) xLua has one code generator,
the source generator, which runs while Unity compiles the scripts. The legacy
one is gone: the **XLua > Generate Code** and **XLua > Clear Generated Code**
menus, the Lua templates (`Assets/XLua/Src/Editor/Template`, also the
`GEN_CODE_MINIMIZE` ones), custom generators (`Generator.CustomGen`,
`[GenCodeMenu]`, [Custom_Generate_EN.md](../Assets/XLua/Doc/Custom_Generate_EN.md)),
`LinkXmlGen`, the `Gen/` output, and `XLuaGenerate`'s wrapper mode. This was
decided on 2026-10-17 for the first public release
([presets.md](design/presets.md#legacy-code-generation-goes-before-the-first-public-release)).
Entries elsewhere on this page that mention Generate Code describe the changes
as they were made.

What to do once, in a project that used Generate Code:

1. **Install the source generator** next to the XLua asmdef:
   `python3 Tools/build_source_generator.py --output <build folder> --install Assets/XLua/Src/Analyzers`
   (the UPM package brings it in `Runtime/Analyzers` when it is built with
   `make_upm_package.py --source-gen`). Asmdefs of yours that use xLua need
   `"XLua"` and `"XLua.Core"` in their references (`XLSG012` otherwise). Without
   the generator every type is bound through reflection, and in IL2CPP players
   Lua functions cannot become `[CSharpCallLua]` delegates.
2. **Convert the configuration lists** (`[LuaCallCSharp] static List<Type>
   ...` and the other list forms): **XLua > Migrate Configuration to
   Attributes** (or **(Grouped)**, or `XLuaGenerate --migrate [--group]`
   outside Unity) writes them as `[assembly: ...]` attributes into one
   `XLuaConfig.cs` per assembly; see [Converting configuration
   lists](#converting-configuration-lists). The source generator reads only the
   attribute forms, and nothing reads the lists any more except the Editor's
   reflection wrappers, which still apply `[BlackList]` lists and filters to
   types without generated bindings. Then **delete the lists**; the menu no
   longer rewrites a converted file whose lists are gone or that names no list.
3. **Delete Generate Code's output:** the C# files in `Assets/XLua/Gen`
   (`XLuaGenAutoRegister.cs`, `DelegatesGensBridge.cs`, `WrapPusher.cs`,
   `PackUnpack.cs`, `EnumWrap.cs`, the `*Wrap.cs` and `*Bridge.cs` files) and
   its `link.xml`. Left there, it does not compile:
   `AssemblyBindings.DelegateBridges`, which it calls, is an error-level
   `[Obsolete]` member that says so. Keep `Assets/XLua/Gen/Resources` if it
   holds hotfix id maps. **XLua > Validate Setup** reports leftover output.
4. **`GEN_CODE_MINIMIZE`** keeps working: with the define in the Player
   Settings the source generator writes its minimized wrappers (every wrapped
   member an `ObjectTranslator` method called through one native trampoline),
   225 to 248 KB smaller than legacy GCM's on Android with 264 wrapped types
   ([source-generator.md](design/source-generator.md#gcm-mode-as-implemented-2026-10-18)).
5. **Hotfix:** see the hotfix item below.
6. **Custom templates** have no replacement (owner decision); code that called
   `Generator.CustomGen` or used `[GenCodeMenu]` no longer compiles. The
   generator's manifest (`XLua.Manifest.g.cs`, Editor builds) lists what it
   binds, for tools that need it.

Behaviour that changes with it:

- **Binding differences between the two generators** that projects can notice,
  all documented when they were found: the order of overloads of equal cost of
  .NET types in Unity and of a method with an `[Out]` parameter that is not
  `out` ([Converting configuration lists](#converting-configuration-lists)),
  members the compiler cannot call that now fall back to reflection instead of
  being dropped, and the other items under [Source generator](#source-generator-preview).
  The overload-order differences are accepted (owner answer, 2026-10-18);
  since the best match wins (overload selection, under
  [Changes in every build](#changes-in-every-build)) they decide only
  between overloads of equal cost.
  `U Convert<T, U>(T)`-style generic methods, which Generate Code output did
  not compile with, are left out by the source generator (`XLSG004`).
- **The assembly-level forms count without the generator too.** Types without
  generated bindings (no generator, or a type it does not bind) go through
  xLua's emitted wrappers and bridges in the Editor and through reflection, which
  read only the lists and the attributes on types until now. They now also read
  `[assembly: CSharpCallLua(...)]` (emitted delegate and interface bridges in the
  Editor), `[assembly: ReflectionUse(...)]` and, in the Editor,
  `[assembly: LuaCallCSharp(...)]` on extension classes (their extension methods
  on reflection wrappers, in players too), and `[assembly: BlackList(...)]`
  (members left out of emitted and reflection wrappers in the Editor). Before,
  deleting the converted lists made a project without the generator fail in the
  Editor with "This type must add to CSharpCallLua" for types such as
  `IEnumerator` that only an assembly attribute named.
- **XLuaGenerate** has the modes `--migrate`, `--luacats` and `--validate`;
  without one it prints its usage and exits with 2. `--hotfix-assemblies` and
  the wrapper output path are gone.
- **LuaCATS** (`XLua > Generate LuaCATS Definitions`, `XLuaGenerate --luacats`)
  declares what the generator's manifests bind; an assembly without generated
  bindings declares nothing, and configuration lists are not read.
- **Validate Setup** no longer asks for a `Gen` folder. It reports assemblies
  with xLua configuration but no generated bindings (and says when the
  generator is not installed), Generate Code output left in the `GenPath`
  folder, and each configuration list as not read by the generator.
  `[BlackList]` list entries are still checked by name; the name checks of
  `DoNotGen` and `AdditionalProperties` lists went with them.
- **Player builds:** an IL2CPP build of a project whose assemblies have
  `[CSharpCallLua]` configuration but no generated bindings now fails even when
  the generator is not installed (before, only with it; Generate Code's
  post-build check threw after every player build without its output). Other
  unbound configuration gives a warning.
- **`[GenPath]`** only sets the folder of the hotfix id maps
  (`Resources/hotfix_id_map*.lua.txt`), `Assets/XLua/Gen/` by default as before.
- **Hotfix:** the injector takes hotfix bridges only from the generator's
  `HotfixBridges` class in the injected assembly, which the generator writes for
  `[Hotfix]` on a type or `[assembly: Hotfix(typeof(T), flags)]` in the type's
  own assembly. IntKey uses that assembly's own ids in every layout
  (`HotfixDelegateBridge.SetSlot(assembly, id, f)`, which
  `xlua.util.auto_id_map()` calls); the process-wide list
  (`HotfixDelegateBridge.Set`) is left to xLua's own assembly when it has no
  `HotfixBridges`. `[Hotfix]` lists, also in Editor folders, still choose what is
  injected, but get no bridges: a type only a list names stops injection with
  "can not find delegate ...: the type is configured only by a [Hotfix]
  configuration list", unless another bridge of its assembly fits it or it is
  `HotfixFlag.Inline`. The migration tool converts those lists too. Projects that
  used Generate Code's bridges for an assembly the generator does not compile
  (a precompiled DLL) can hotfix it only with `HotfixFlag.Inline`.
- **XLua > Project Layout > Use Assembly-CSharp** no longer refuses while the
  generator is installed under `Assets/XLua`. Without the asmdefs Unity runs
  the generator in every assembly, Assembly-CSharp among them, where xLua then
  compiles, so the way back keeps its generated bindings.
- **Removed with it:** `XLua.SysGenConfig` (xLua's GCOptimize defaults, stated
  as `[assembly: GCOptimize]` in `GenAttributes.cs` since milestone M4),
  `ObjectTranslator.GetByType` (`GENERIC_SHARING`, which only generated code
  called), the examples' configuration lists (`Assets/XLua/Examples/ExampleGenConfig.cs`,
  `CoroutineConfig`, `MessageBoxConfig`, `08_Hotfix/Editor/HotfixTestCfg.cs`;
  the examples are configured by `Assets/XLua/Examples/XLuaConfig.cs` and the
  attributes on their types), and `Assets/XLua/Editor` (`ExampleConfig.cs` with
  its `BlackList` entries for members Generate Code could not call on some
  platforms, and `XLuaUnityDefaultConfig.cs` with its `Span` filter, both for
  Generate Code; the source generator compiles per platform and leaves `ref
  struct` members out). An in-place upgrade that copies the new `Assets/XLua`
  over the old one leaves those files behind: delete them.
- **General (non-Unity) applications** get generated bindings by compiling with
  the .NET SDK's csc (Roslyn 4.3.1 or later) and
  `-analyzer:XLua.SourceGenerator.dll` against `XLua.Mini.dll`; see
  [General/README.md](../General/README.md).

## Generated code (XLua/Generate Code)

Generate Code was removed in source generator milestone M11 ([above](#generate-code-removed)). The binding
API below is what the source generator's output calls; the notes on Generate Code's output are history.

Generated code no longer extends xLua's own classes as `partial` classes. It
calls a public binding API, `XLua.Binding` (hidden from IDE completion,
version `BindingApi.Version` = 2), so it compiles in an assembly other than
xLua's, as it must now that xLua has its own asmdefs. The wrappers, bridges and the
members Lua sees are the same as before. See
[source-generator.md](design/source-generator.md), section 3 and milestone M0.
Version 2 (2026-10-28) registers GCOptimize structs and enums without
generic methods, which made IL2CPP players larger
([source-generator.md](design/source-generator.md#xluadlls-own-bindings-2026-10-28)).
The source generator writes its code again whenever Unity compiles, so only a
DLL compiled outside the project with version 1 bindings is affected: its
registration stops with "generated bindings ... were made for binding API
version 1"; compile it again.

- **Regenerate code** after updating: run **XLua/Clear Generated Code**, then
  **XLua/Generate Code** (or `XLuaGenerate` again). Code generated by an
  earlier version declares partial classes that no longer exist and does not
  compile. With hotfix, inject again after regenerating.
- **No partial classes:** `ObjectTranslator`, `InternalGlobals`,
  `StaticLuaCallbacks` and `CopyByValue` are no longer `partial`.
  `DelegateBridge` stays `partial` only to share its class with
  `GenericDelegateBridge.cs`; nothing generated is added to it. Project code
  or custom templates (`Generator.CustomGen`) that added members to one of
  these classes through `partial` no longer compile; move those members to a
  static class of your own, as extension methods where they need the
  instance.
- **Registration:** each generated unit (`XLuaGenAutoRegister.cs`,
  `DelegatesGensBridge.cs`, `WrapPusher.cs`) registers itself with
  `XLua.Binding.BindingApi.Register` from
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` and, in the Editor,
  `[InitializeOnLoadMethod]`. Outside Unity (`XLUA_GENERAL`) the first `LuaEnv`
  finds them through `[assembly: XLua.Binding.GeneratedBindings]`. This
  replaces the static constructors reached through
  `ObjectTranslator.s_gen_reg_dumb_obj` and `LuaEnv.AddIniter`, which user code
  can still call.
- **Process-wide registry:** generated class wrappers, delegate and interface
  bridges and extension method lists are kept once per process instead of per
  `LuaEnv`. `ObjectTranslator.DelayWrapLoader` and `AddInterfaceBridgeCreator`
  still take precedence for one `LuaEnv`. The translator's private `delayWrap`
  table no longer holds the generated loaders.
- **Generated shapes:**
  - GCOptimize push, get and update are extension methods of
    `ObjectTranslator` in `XLua.CSObjectWrap.WrapPusher`: `translator.PushT`,
    `translator.UpdateT` and, new, `translator.GetT(L, index, out value)`. The
    non-generic `translator.Get(L, index, out T)` overloads are gone; such a
    call now binds to the generic `Get<T>`, which reaches the same code through
    the registered getter. Code outside `XLua.CSObjectWrap` needs
    `using XLua.CSObjectWrap;` for the extension methods. The per-`LuaEnv` type
    ids that were `<T>_TypeID` and `<T>_EnumRef` fields of `ObjectTranslator`
    are slots (`BindingApi.AllocateSlot`, `TypeId`, `EnumCacheRef`).
  - Struct pack and unpack are in `XLua.CSObjectWrap.PackUnpack`;
    `CopyByValue` keeps only the primitive and `decimal` overloads.
  - Delegate and hotfix bridges are static methods
    `__Gen_Delegate_Imp<n>(this DelegateBridge, ...)` in
    `XLua.CSObjectWrap.DelegatesGensBridge`, bound as delegates closed over the
    bridge, so `Delegate.Target` is still the `DelegateBridge`. Generated code
    no longer overrides `DelegateBridgeBase.GetDelegateByType` (CodeEmit still
    does). A delegate type that is not configured still gets a bridge with a
    matching signature. Since source generator milestone M3 the registry is
    asked first, by delegate type and then by the signature of the
    registered delegate types (the bridge method is taken from a delegate the
    registered creator makes, not by name), before `GetDelegateByType` and the
    scan of `DelegatesGensBridge` by method name, which stays for hotfix
    bridges.
  - `GEN_CODE_MINIMIZE` wrappers are one static class per type,
    `XLua.CSObjectWrap.<Type>Wrap`, as in the default mode; their functions are
    private `ObjectTranslator` extension methods.
  - `DelegateBridge.Gen_Flag` is set by `BindingApi.Register` when the
    registered unit bridges calls into Lua (`Delegate`, `DelegateBridges` or
    `InterfaceBridge` entries; the generated `DelegatesGensBridge.cs` always
    has them). A unit that binds
    other code (wrappers, structs, extension methods) replaces the reflection
    scan for extension methods but leaves `Gen_Flag`, and with it CodeEmit,
    alone; a unit with only name table entries changes neither.
  - The cache of pushed enum values (`[LuaCallCSharp]` and `[GCOptimize]`
    enums, `BindingApi.EnumCacheRef`) is a registry table of its own instead
    of the enum's class table `CS.E`. A script could replace a cached value
    with `rawset(CS.E, 1, x)`, or `CS.E[1] = x` once the value had been
    pushed, and C# then pushed `x` for that enum value. `rawget(CS.E, 1)` no
    longer returns the cached userdata of value 1; `CS.E.Name` and
    `CS.E.__CastFrom` are unchanged.
- **Overloads with ambiguous defaults:** a wrapper calls an overload with its
  trailing optional parameters left out. When another method or constructor
  of that name takes the same leading parameter types and fills the rest
  with defaults too, that call was ambiguous (CS0121) and the generated code
  did not compile, as for Unity 6000.6's `PhysicsScene2D.GetRayIntersection`
  next to its new `Allocator` overload. Both generators now write the
  defaults out as typed constants in such calls, so the call reaches the
  overload it was generated for. Other calls are unchanged.
- **Array types in `LuaCallCSharp`** (such as `typeof(int[])`) get no
  wrapper. Generate Code wrote one whose constructor did not compile
  (`new int[]` with an argument list); it now leaves them out with the warning "xLua does not generate a
  wrapper for the array type ...", as the source generator does (XLSG004).
  Nothing changes in Lua: every array goes through the common array
  metatable, with `Length`, the indexer and `System.Array`'s members, in
  every binding mode. Remove such entries to silence the warning. The
  configuration migration tool leaves them out as well, with "an array type"
  in its report.
- **Types that generated code cannot name** in the configuration lists,
  which computed lists such as ExampleConfig's `AllDelegate` pick up: a
  delegate or class over an anonymous type or a closure class
  (`Func<..., <>f__AnonymousType1<...>>` from a LINQ query) or over a
  private or protected nested type. Generate Code wrote bridges and wrappers
  for them that did not compile
  ([#731](https://github.com/Tencent/xLua/issues/731)); it now leaves them
  out of `LuaCallCSharp`, `CSharpCallLua` and `GCOptimize`, and the
  configuration migration tool lists them as left out. Lua reaches them as
  before through emitted code and reflection where those exist.
- **Hotfix lists with a nested type of an `[Obsolete]` class**: Generate
  Code made no bridges for such a type although the injector injects it, so
  injection stopped with "can not find delegate for Outer/Nested..ctor"
  ([#799](https://github.com/Tencent/xLua/issues/799)). Only the type's own
  `[Obsolete]` now leaves it out, as in the injector, so the nested type gets
  its bridges and is injected. Regenerate code.
- **Generic extension methods with a parameter of a generic type over their
  type parameter**, such as `ApplySelfTo<T>(this T self, Action<T> f)`:
  Generate Code stopped with "TemplateCommon: attempt to index a nil value
  (field 'FullName')" ([#924](https://github.com/Tencent/xLua/issues/924),
  #1140). Generated code (both generators) now leaves such a method out, as
  it already did for other generic methods with such parameters; reflection
  and emitted wrappers still call it with the constraint as `T`.
- **`[GCOptimize]` structs with `bool`, `char`, `IntPtr` or `UIntPtr`
  fields** were boxed: the size table had no entry for them, so the struct
  fell back to an ordinary C# object in Lua
  ([#1186](https://github.com/Tencent/xLua/pull/1186),
  [#1141](https://github.com/Tencent/xLua/issues/1141)). Both generators now
  copy them by value like the other `[GCOptimize]` structs (a `bool` takes
  one byte, a `char` two, `IntPtr` and `UIntPtr` eight on every platform).
  Lua reads and writes the same fields, and a struct passed to C# arrives
  as a copy, as before. Regenerate code.
- **Operator errors in generated wrappers** always said "invalid arguments
  to right hand of - operator, need T!", also when the left operand was
  wrong ([#1187](https://github.com/Tencent/xLua/issues/1187)). They now say
  "left hand" when the left operand is not of the wrapped type and "right
  hand" otherwise. Regenerate code.
- **Hotfix:** the injector takes the bridges from
  `XLua.CSObjectWrap.DelegatesGensBridge` and calls them as static methods.
  The fields it adds still hold a `DelegateBridge`. IntKey and direct bridge
  calls need the generated code and the hotfixed types in one assembly (since
  2026-10-18 not necessarily xLua's; see
  [xLua in its own assemblies](#xlua-in-its-own-assemblies-asmdefs-the-default)).

## xLua in its own assemblies (asmdefs, the default)

Since 2026-10-18 the Assets folder ships four assembly definitions, so a
project that copies `Assets/XLua` compiles xLua into assemblies of its own
(owner decision of 2026-10-18, [source-generator.md](design/source-generator.md#the-asmdef-layout-becomes-the-default-2026-10-18)):

| Folder | asmdef | Assembly | Platforms |
|---|---|---|---|
| `Assets/XLua/Core` | `XLua.Core.asmdef` | `XLua.Core` | all |
| `Assets/XLua/Core/Editor` | `XLua.Core.Editor.asmdef` | `XLua.Core.Editor` | Editor |
| `Assets/XLua/Src` | `XLua.asmdef` | `XLua` (references `XLua.Core`) | all |
| `Assets/XLua/Src/Editor` | `XLua.Editor.asmdef` | `XLua.Editor` (references both) | Editor |

Upstream xLua, and xlua-ng before, compiled everything into Assembly-CSharp
and Assembly-CSharp-Editor; the four files were opt-in copies in
`Assets/XLua/Asmdef~`. They keep their GUIDs, so a project that had copied
them into place gets the same files. All four are auto-referenced, and
`XLua.Core` and `XLua` allow unsafe code. To keep upstream's layout, see
"The way back" below.

- **Nothing changes for scripts without an asmdef** (Assembly-CSharp,
  Assembly-CSharp-Editor and the firstpass assemblies): they reference every
  auto-referenced asmdef. That covers the examples and the tutorial (and,
  until milestone M11 removed them, `Assets/XLua/Editor` and Generate Code
  output in `Assets/XLua/Gen`, which used only xLua's public API), so xLua
  needs no `InternalsVisibleTo` for them.
  `XLua.Core` grants only `XLua` its internals.
- **Your asmdefs that use xLua** add `"XLua"` to their references (in the
  Inspector: Assembly Definition References), and with the source generator
  `"XLua.Core"` as well (error `XLSG012` otherwise; see
  [XLua.Core and LuaScript](#xluacore-and-luascript)). Before, an asmdef could
  not use xLua at all, because no asmdef can reference Assembly-CSharp.
  **XLua > Validate Setup** lists asmdefs under `Assets` whose scripts use
  xLua without these references, with the fix.
- **Projects that added an asmdef for xLua themselves** (any name, often at
  `Assets/XLua`): delete it and point the asmdefs that referenced it at
  `XLua` (and `XLua.Core`). Kept, it would take what xLua's four asmdefs do
  not (the examples), and the source generator would run in it, not in the
  assemblies whose types it should bind.
- **No longer compiles:**
  - code that uses xLua's `internal` members, or that adds members to xLua's
    classes with `partial` declarations: those need the same assembly. Move
    such members to a class of your own (extension methods where they need
    the instance), or use the way back;
  - code that names xLua's assembly as `Assembly-CSharp`, such as
    `Type.GetType("XLua.LuaEnv, Assembly-CSharp")`: use `XLua` (or `XLua.Core`
    for `XLua.LuaDLL`), or `typeof(XLua.LuaEnv).Assembly`.
    `Assembly.Load("Assembly-CSharp")` in a configuration list still finds
    the project's own types, as `ExampleConfig.cs` shows.
- **link.xml** entries that keep xLua's types under
  `<assembly fullname="Assembly-CSharp">` must name `XLua` or `XLua.Core`.
  The link.xml that xLua writes (the source generator's fragments, and before
  M11 `LinkXmlGen`) names each type's own assembly.
- **Hotfix:** the injector injects every assembly that has hotfix types,
  Assembly-CSharp included, with `XLua.dll` as xLua's assembly. Each one calls
  the hotfix bridges the source generator wrote into it and numbers its IntKey
  ids in its own slots; until Generate Code was removed (M11), Assembly-CSharp
  could also call Generate Code's bridges in `Assets/XLua/Gen` and keep the
  process-wide ids. `Inline` types and generic methods are injected in every
  assembly since milestone M7. See
  [Hotfix_EN.md](../Assets/XLua/Doc/Hotfix_EN.md#several-hotfix-assemblies).
- **The source generator** goes into `Assets/XLua/Src/Analyzers`
  (`python3 Tools/build_source_generator.py --output <build folder> --install Assets/XLua/Src/Analyzers`);
  there is nothing else to copy. The `XLua` asmdef limits it to the
  assemblies that reference xLua.
- **`XLUA_CORE_ONLY`** now works in the Assets folder too: `XLua` and
  `XLua.Editor` have the define constraint `!XLUA_CORE_ONLY`, so a project
  that defines it compiles only `XLua.Core` and `XLua.Core.Editor`. Without
  asmdefs, delete `Assets/XLua/Src` instead, as before.
- **Obfuscators** that work per assembly can leave out `XLua.dll` and
  `XLua.Core.dll` instead of excluding the `XLua` namespaces inside
  Assembly-CSharp.
- **The hotfix Editor code no longer assumes Assembly-CSharp** in either
  layout: it finds the project folder and the player's script folder through
  xLua's own assembly (there may be no Assembly-CSharp at all), passes the
  player build of xLua's assembly to the injector, and reads `[Hotfix]` lists
  in editor code from every project assembly instead of only the one that
  holds xLua's editor code.
- **xLua cannot see project types at compile time** (an asmdef cannot
  reference Assembly-CSharp); it finds them at run time, by reflection and
  through the binding registry, so nothing in xLua needs that.

### The way back: everything in Assembly-CSharp

**XLua > Project Layout > Use Assembly-CSharp (no asmdefs)** moves the four
`.asmdef` files, each with its `.meta`, to `Assets/XLua/Asmdef~/` (the same
subfolders: `Asmdef~/Core/XLua.Core.asmdef`, `Asmdef~/Core/Editor/...`,
`Asmdef~/Src/XLua.asmdef`, `Asmdef~/Src/Editor/...`) and refreshes the asset
database. Unity does not import folders whose name ends in `~`, so xLua
compiles into Assembly-CSharp and Assembly-CSharp-Editor again, as upstream.
**XLua > Project Layout > Use asmdefs** moves them back. The `.meta` files
travel with them, so the GUIDs, and asmdef references by GUID, stay. Generated
code, configuration and hotfix setup need no change in either direction;
run Hotfix Inject as usual.

- In batch mode: `-executeMethod XLua.ProjectLayout.UseAssemblyCSharp` or
  `XLua.ProjectLayout.UseAsmdefs` (the launch fails when the move cannot be
  done); `XLua.ProjectLayout.Report` logs the current layout.
- The menu refuses, and changes nothing, when xLua is a UPM package (always
  its own assemblies), when a file exists in both places, when a `.meta` is
  missing, and, for Use Assembly-CSharp, when the source generator
  (`XLua.SourceGenerator.dll`) is installed under `Assets/XLua`: without the
  `XLua` asmdef Unity would run it in every assembly. Remove it first.
- By hand: move (or delete) `Assets/XLua/Core/XLua.Core.asmdef`,
  `Assets/XLua/Core/Editor/XLua.Core.Editor.asmdef`,
  `Assets/XLua/Src/XLua.asmdef` and `Assets/XLua/Src/Editor/XLua.Editor.asmdef`,
  each with its `.meta` file, while Unity is closed or followed by
  Assets > Refresh. Copying a newer `Assets/XLua` over the project brings them
  back; switch again after each update.
- With the asmdefs moved away, asmdefs of yours cannot use xLua, and the
  costs above disappear: internals and `partial` classes work as upstream.
- **Why not a define constraint:** an asmdef whose `defineConstraints` do not
  hold leaves its scripts out of every assembly; they do not fall back into
  Assembly-CSharp. A define that switched the asmdefs off would therefore
  remove xLua instead of moving it, which is exactly how `XLUA_CORE_ONLY`
  leaves out `XLua` (`Test/UnityMatrix/core_only_check.py`). Moving the
  files is the only switch.

## UPM package (preview)

Since 2026-10-18 `Tools/make_upm_package.py` builds xLua as a UPM package
(installation: [README](../README.md#as-a-upm-package-preview)). It is the
asmdef layout above in a package, with the same four asmdefs, and has no way
back to Assembly-CSharp. Compared with copying `Assets` into a project:

- `Assets/XLua/Src` is `Runtime`, `Assets/XLua/Src/Editor` is `Editor`, and
  `Assets/XLua/Editor/XLuaUnityDefaultConfig.cs` (the Span `BlackList` filter
  on 2022.1 and later) compiles into `XLua.Editor` instead of
  Assembly-CSharp-Editor. `ExampleConfig.cs` comes with the Examples sample.
- Files keep their GUIDs (the repository gained the missing
  `XLuaUnityDefaultConfig.cs.meta`). Folders shared with other plugins, such
  as `Plugins/Android`, do not take the GUIDs of `Assets/Plugins`.
- `xlua_webgl.cpp` includes the C sources from the package's `build~`
  folder instead of `../../../build` next to `Assets` (not built yet).
- Plugin variants (`<path>/xlua-variants/<name>/<file>`, such as the iOS
  Simulator library) are part of the package since 2026-10-18. Each gets the
  `.meta` of its default plugin with the Editor turned off and a GUID derived
  from its package path, which is what **XLua/Plugin Variants/Copy Import
  Settings** writes in the Assets layout; a read-only package cannot run that
  menu. Variants of the macOS bundle are left out.
- LuaCATS writes into `LuaTypes` next to `Assets` and the hotfix injector its
  id maps into `Assets/XLua/Gen/Resources` (`[GenPath]`); both work with a
  read-only package. (Generate Code, which wrote into `Assets/XLua/Gen` too,
  was removed in M11.)
- The samples import into `Assets/Samples/xLua-NG/<version>/Examples` and
  `.../Tutorial`. `SignatureLoaderTest.cs` finds its signed Lua files next to
  its own script since 2026-10-19 (it read them from `Assets/XLua/Examples`
  before; imported, the example fell through to the Resources loader, which
  returned the signed bytes unchecked: "signatured1.lua:1: syntax error near
  '}'"). The samples need uGUI (`com.unity.ugui`) and the built-in modules
  of a new project.

Installing and using the package:

- **`Packages/manifest.json`:** a tarball install is
  `"com.xlua-ng.xlua": "file:../com.xlua-ng.xlua-0.0.1-dev.tgz"` (relative to
  `Packages`). A tarball install is read-only, like a registry install.
- **Plugins:** the release package (`publish.yml`, since 2026-10-19) has
  the Lua 5.5.1 plugins of Android, iOS, macOS, Windows and Linux, with the
  `minimal` and `core` variants for Android and iOS and the iOS Simulator
  builds ([release.md](design/release.md)); not UWP, OpenHarmony, or Lua
  5.4, 5.3 and LuaJIT. For another runtime, copy the package into
  `Packages/` and replace its plugins. To build a package yourself,
  `make_upm_package.py --plugins` takes the output of the `build/make_*.sh`
  scripts (a folder with `Plugins/` inside); `--plugin
  arm64/libxlua.dylib=PATH` adds a single file. Without `--source-gen
  BUILD_DIR`, which builds the source generator into `Runtime/Analyzers`,
  the package has no generated bindings
  ([Source generator](#source-generator-preview)); `--release` refuses such
  a package, and one that lacks a platform.
- **Version and changelog:** the version is the repository's `VERSION`
  file; the package's `CHANGELOG.md` is the repository's, and
  `Third Party Notices.md` holds the licences of Lua, LuaSocket and
  lua-snapshot, which the plugins contain.
- **The package layout:** `Core` compiles into the `XLua.Core` assembly,
  `Runtime` into `XLua` and `Editor` into `XLua.Editor` (the asmdefs of the
  [Assets folder](#xlua-in-its-own-assemblies-asmdefs-the-default)); the
  native plugins are in `Plugins`, the guides in `Documentation~`, and the
  examples and tutorial are samples you import from the Package Manager
  window.
- **Moving from the Assets layout:** delete `Assets/XLua` (keep
  `Assets/XLua/Gen/Resources` if it holds hotfix id maps, which the injector
  still writes there) and the xLua libraries in `Assets/Plugins` before
  adding the package. The package's files have the same GUIDs, so scenes and
  assets keep their references, but two copies of a file conflict. Scripts
  must not depend on xLua being in Assembly-CSharp; code in an asmdef needs a
  reference to `XLua`, and to `XLua.Core` as well when the source generator
  writes bindings into it: `"references": ["XLua", "XLua.Core"]` (error
  `XLSG012`, which names the asmdef, otherwise).
- **Defines** such as `HOTFIX_ENABLE` and `GEN_CODE_MINIMIZE` go into Player
  Settings or `Assets/csc.rsp`, as before; Unity applies both to the
  package's assemblies.
- **Hotfix:** the injector stays in your project's `Tools/` folder, as in
  the Assets layout: extract the release file `xlua-tools_<tag>.tgz` into
  the project folder, or run `python3 Tools/build_tools.py` from a clone.
- **`XLUA_CORE_ONLY`** leaves out `XLua` and `XLua.Editor`; `XLua.Core`
  ([LuaScript](../Assets/XLua/Doc/LuaScript.md)) still compiles. The release
  package's default plugins are built without `XLUA_LUASCRIPT`, so
  `LuaScript` runs in Android and iOS players (the `core` variant) but not
  in the Editor or in desktop players from it.

## Configuration attributes

- **`AttributeUsage`:** `LuaCallCSharp`, `CSharpCallLua`, `GCOptimize`,
  `ReflectionUse`, `BlackList`, `DoNotGen`, `AdditionalProperties` and
  `Hotfix` now declare `[AttributeUsage(AttributeTargets.All, AllowMultiple
  = true)]`. The targets are the ones they allowed without it; repeating one
  on the same target is now allowed.
- **Assembly-level forms**, with the configured type as the first argument,
  also for types the project does not own:

  ```csharp
  [assembly: LuaCallCSharp(typeof(UnityEngine.Transform))]
  [assembly: CSharpCallLua(typeof(System.Action<float>))]
  [assembly: GCOptimize(typeof(Game.Pair), OptimizeFlag.PackAsTable)]
  [assembly: ReflectionUse(typeof(Game.Settings))]
  [assembly: BlackList(typeof(UnityEngine.Light), "shadowAngle")]
  [assembly: BlackList(typeof(System.IO.File), "Open", typeof(string), typeof(System.IO.FileMode))]
  [assembly: BlackList("UnityEngine.MonoBehaviour", "runInEditMode")]  // by name
  [assembly: DoNotGen(typeof(Game.Big), "RarelyUsed")]
  [assembly: AdditionalProperties(typeof(UnityEngine.Ray), "origin", "direction")]
  [assembly: Hotfix(typeof(Game.Shop), HotfixFlag.IntKey)]  // in the assembly that defines Game.Shop
  ```

  Only the source generator and, for `Hotfix`, the hotfix injector (since
  milestone M7) read these forms. CodeEmit reads the attributes on types,
  members and configuration lists as before, and ignores the new forms; so
  did the legacy generator ("Generate Code", `XLuaGenerate`) until it was
  removed (M11).
- **Several types in one attribute** (2026-10-18): `LuaCallCSharp`,
  `CSharpCallLua`, `GCOptimize`, `ReflectionUse` and `Hotfix` also take a list
  of types; `GCOptimize` and `Hotfix` then take their flags first, for every
  type of the list. The one-type form stays:

  ```csharp
  [assembly: LuaCallCSharp(typeof(UnityEngine.Transform), typeof(UnityEngine.GameObject))]
  [assembly: CSharpCallLua(typeof(System.Action), typeof(System.Action<float>))]
  [assembly: GCOptimize(OptimizeFlag.PackAsTable, typeof(Game.Pair), typeof(Game.Range))]
  [assembly: Hotfix(HotfixFlag.IntKey, typeof(Game.Shop), typeof(Game.Bag))]
  ```

  Each type of the list is configured as with its own one-type attribute;
  the source generator binds the same surface and the hotfix injector patches
  the same types. These are new `params Type[]` constructors (and
  `(flags, params Type[])` for `GCOptimize` and `Hotfix`); C# still picks the
  existing constructors for `[X]`, `[X(flags)]`, `[X(typeof(T))]` and
  `[X(typeof(T), flags)]`, so compiled assemblies and their attributes stay
  as they were. The one exception is a `null` literal, `[LuaCallCSharp(null)]`
  (which on a type meant `[LuaCallCSharp]`, and which upstream xLua never
  accepted): it now fits both constructors and is error CS0121; write
  `[LuaCallCSharp]`. `BlackList`, `DoNotGen` and
  `AdditionalProperties` name members of one type and have no list form. An assembly compiled with the
  list form needs an xLua with these constructors, and its `[Hotfix]` list
  needs this hotfix injector: an older one reads only the first argument and
  skips such an attribute.
- **xLua's own GCOptimize defaults** (`UnityEngine.Vector2`, `Vector3`,
  `Vector4`, `Color`, `Quaternion`, `Ray`, `Bounds`, `Ray2D`, and the
  properties of `Ray`, `Ray2D` and `Bounds` that `SysGenConfig` lists) are also
  stated as `[assembly: GCOptimize]` and `[assembly: AdditionalProperties]` in
  xLua's assembly, outside `XLUA_GENERAL`, so that the source generator finds
  them. `SysGenConfig`'s lists, which the legacy generator read, went with it
  in M11.
- Each attribute has a read-only `Type` property (null in the old forms and
  in the list form); `LuaCallCSharp`, `CSharpCallLua`, `GCOptimize`,
  `ReflectionUse` and `Hotfix` also have `Types`, the configured types of the
  one-type and the list form (null in the forms without a type).
  `BlackList` also has `TypeName`, `Member`, `ParameterTypes` and
  `ParameterTypeNames`, `DoNotGen` and `AdditionalProperties` have `Members`.

## Source generator (preview)

`General/SourceGenerator` holds the Roslyn source generator that will replace
"Generate Code" (`Tools/build_source_generator.py` builds it, and
`Tools/make_upm_package.py --source-gen` puts it into the UPM package's
`Runtime/Analyzers`). It reads the configuration attributes,
reports diagnostics `XLSG001` to `XLSG013` with file and line, registers
each assembly with `XLua.Binding` and its name table, and writes class and
enum wrappers for the `[LuaCallCSharp]` types (milestone M2), delegate and
interface bridges for the `[CSharpCallLua]` types (M3), and the GCOptimize
pushers (M4):

- **The same members as the legacy generator:** constructors, methods with the
  same overload order, fields, properties, indexers, operators, events,
  extension methods of `[LuaCallCSharp]` classes, `DoNotGen` members as lazy
  reflection members, `BlackList` entries (attribute forms only). For the test
  suite's configuration the two binding surfaces are equal
  (`Test/Native/surface_diff.py`).
- **The same bridges as the legacy generator:** delegate types whose
  signatures are equal under the legacy rule share one bridge method (for the
  test suite, 32 delegate types on 26 bridges), so `Delegate.Target` is the
  `DelegateBridge` as before, and a delegate type without configuration but
  with a matching signature still works. Interface bridges implement the same
  members. `surface_diff.py` compares both bridge sets with the legacy
  generator's. A unit with bridges sets `DelegateBridge.Gen_Flag`, so CodeEmit
  no longer scans the loaded types when a `LuaEnv` starts.
- **Delegates and interfaces that cannot be bridged** (pointer or ref struct
  parameters, ref returns, generic or static abstract interface members,
  indexers with more than one key, `ref readonly` parameters with Roslyn
  before 4.10) get info `XLSG004` and no bridge, and convert as an
  unconfigured type does. The legacy generator wrote bridges for most of them
  that did not compile, or left a generic interface method unimplemented.
- **GCOptimize as with the legacy generator:** packable `[GCOptimize]` structs
  and `[LuaCallCSharp]` enums are pushed, read and updated without boxing,
  `PackAsTable` structs as tables, unpackable ones and classes read from Lua
  tables field by field, with the same float grouping and
  `[AdditionalProperties]`; arrays of them get the element accessors.
  `surface_diff.py` compares the selection with the legacy generator's. A
  struct configured in a referenced assembly (its `[GCOptimize]` types and
  `[assembly: GCOptimize]`, such as xLua's UnityEngine defaults) is pushed
  without boxing in the assemblies that use it too, through their own copy of
  the helpers. A `[GCOptimize]` struct that cannot be packed (a field that is
  not a primitive, `decimal` or packable struct) gets warning `XLSG010`.
- **Hotfix bridges per assembly (M7):** an assembly with `[Hotfix]` types (on
  the type or `[assembly: Hotfix(typeof(T), flags)]` in that assembly) gets an
  internal `HotfixBridges` class with the bridges of its injectable methods,
  selected as the legacy generator does and keyed by the same signatures, and
  its IntKey slots. The injector calls them directly, IntKey works in that
  assembly with ids numbered from 0 there, and its `hotfix_id_map` entries
  name the assembly (`["<assembly>"] = "Game"`), which
  `xlua.util.auto_id_map()` passes to the new
  `HotfixDelegateBridge.SetSlot(assembly, id, f)`. Code that calls
  `HotfixDelegateBridge.Set(id, f)` with ids from such a map must call
  `SetSlot` with the assembly instead; `Set` keeps working for the ids of
  xLua's own assembly without `HotfixBridges` (and did for "Generate Code"
  output until M11).
- **Obsolete members** are left out, as the legacy generator does;
  `XLUA_JUST_EXCLUDE_ERROR` keeps those not marked as errors.
- **A `Func<MemberInfo, bool>` `[BlackList]` filter is not applied** (it cannot
  run in the compiler); list the members it matches as
  `[assembly: BlackList(typeof(T), "member", ...)]`. The migration tool writes
  them ([Converting configuration lists](#converting-configuration-lists)).
- **Members the compiler cannot call fall back to reflection** (M5) instead
  of being dropped: init-only setters (`init`; the legacy generator wrote code
  that did not compile for them) and, with the Roslyn of Unity 2021.3 to
  6000.3, methods with `ref readonly` parameters (CS0570; the legacy generator
  drops them). Each gets info `XLSG004`, and its first use logs
  "xLua: T.m is reached through reflection: ..." once in the Editor and in
  development builds. A method falls back only when none of its overloads can
  be wrapped.
- **Array types** in `LuaCallCSharp` (`typeof(int[])`) get no wrapper yet
  (info `XLSG004`) and go through reflection; the legacy generator wraps them.
- **Types a referenced assembly already wraps** are not wrapped again (info
  `XLSG008`): `[assembly: XLua.Binding.GeneratedBindings]` now also lists the
  wrapped types (a new three-argument constructor; the two-argument one
  stays).
- **The manifest** lists the members of a closed generic type as wrapped
  (``List`1[System.Int32]::Add(System.Int32)``), as the legacy surface dump
  does; before M9 it listed those of the definition (``List`1[T]::Add(T)``).
- **Output:** one `internal static class W_<name>_<hash>` per type and one
  `internal sealed class B_<name>_<hash>` per interface bridge in
  `XLua.Generated.<assembly>_<hash>`, `XLua.Bindings.g.cs`, `XLua.Bridges.g.cs`
  with the delegate bridges, `XLua.Structs.g.cs` and `XLua.PackUnpack.g.cs` with
  the GCOptimize helpers and, in the Editor and outside Unity,
  `XLua.Manifest.g.cs` with the binding surface and the bridges as JSON.

- **Stripping (M5):** each assembly's `XLua.LinkXml.g.cs` (Editor builds only)
  lists what Lua reaches by reflection: `[ReflectionUse]` types with
  `preserve="all"`, as the legacy `LinkXmlGen`, and the `DoNotGen` and fallback
  members of wrapped types. A player build passes them to UnityLinker as one
  file (`Temp/XLuaSourceGen/link.xml`). Wrapped members need no entry. The
  legacy `link.xml` kept every member of every `[LuaCallCSharp]` type.
- **When the generator did not run** (for example warning CS9057, a compiler
  older than it needs): after each domain reload the Editor logs an error for
  every assembly with xLua configuration and no generated bindings, an IL2CPP
  build of such an assembly with `[CSharpCallLua]` types fails, other builds
  warn, and a development player without any registration warns once. None
  of this happens unless `XLua.SourceGenerator.dll` with its `RoslynAnalyzer`
  label is in the project.
- **LuaCATS definitions from the manifest (M10):** **XLua > Generate LuaCATS
  Definitions** and `XLuaGenerate.exe --luacats` read an assembly that the
  generator compiled from its `XLua.Manifest.g.cs`, so a project on the
  source generator gets definitions without Generate Code, and they declare
  exactly the members the wrappers bind. The manifest now also lists the
  `[ReflectionUse]` types (`reflectionUse`) and the `BlackList` entries by
  type (`blackList`). Assemblies without a manifest are read from their
  configuration lists as before. For the same configuration the files are the
  same either way. See
  [LuaCATS.md](../Assets/XLua/Doc/LuaCATS.md#with-the-source-generator).
- **Validate Setup on the source generator path (M10):** when
  `XLua.SourceGenerator.dll` is in the project or an assembly has generated
  bindings, the missing `GenPath` folder is no longer reported. Reported
  instead: a copy of the DLL without the `RoslynAnalyzer` label, an assembly
  with xLua configuration but no generated bindings, and Generate Code output
  (`XLuaGenAutoRegister.cs`) left in `GenPath`. `BlackList`, `DoNotGen` and
  `AdditionalProperties` names are checked only in assemblies the generator
  did not compile; for the others the compiler reports them (`XLSG003`).
- **A generic method whose type parameter only the return type uses** (`U
  Convert<T, U>(T x) where T : Base where U : Base`) is left out with info
  `XLSG004`: the wrapper calls it without type arguments, which does not
  compile (CS0411). The legacy generator writes the same call, so a
  configured type with such a method breaks Generate Code output. Lua could not call it
  through reflection either, and the LuaCATS definitions never declared it.
- **`GEN_CODE_MINIMIZE` (minimized wrappers):** a project that defines
  `GEN_CODE_MINIMIZE` (Player Settings scripting defines) now gets the
  minimized wrappers from the source generator too, as Generate Code writes
  them with the define: every wrapped member is a private `ObjectTranslator`
  extension method `int f(ObjectTranslator translator, IntPtr L, int
  gen_param_count)`, registered as a `CSharpWrapper` delegate and called
  through xLua's one native trampoline, instead of a
  `[MonoPInvokeCallback]` method of its own. Lua sees the same members, the
  same overload order and the same error messages (`c# exception:...` comes
  from the trampoline's C# entry). Delegate, interface and hotfix bridges,
  GCOptimize code, the manifest and the LuaCATS definitions do not change.
  With 264 wrapped engine types an Android IL2CPP release player is about
  700 KB smaller than with the plain source generator and 225 to 250 KB
  smaller than with Generate Code's GCM (`build/SIZE.md`); calls into C# cost
  up to about 10 ns more on the host, as with Generate Code's GCM. Before, the generator ignored
  the define and the migration tool listed it as not converted; it now lists
  it under "check by hand". New warning `XLSG013`: the define of an assembly
  differs from the one xLua was compiled with (a define set for one assembly
  only, or a precompiled xLua); the wrappers then follow xLua's mode, the
  only one that compiles. Since 2026-10-19 the minimal preset selects the mode
  too: `XLUA_PRESET_MINIMAL` counts as `GEN_CODE_MINIMIZE` for xLua and for the
  generator unless `XLUA_NO_GCM` is defined ([Presets](#presets)), and
  `XLSG013` follows the same rule.

In Unity it needs the
asmdef layout above (the default): next to `XLua.asmdef` it runs only in the
assemblies that reference xLua. Configuration lists
(`[LuaCallCSharp] static List<Type> ...`) are not read by it (info
`XLSG011`); the migration tool converts them (next section).

## Converting configuration lists

**XLua > Migrate Configuration to Attributes** (and `XLuaGenerate --migrate`
outside Unity) is new: it evaluates the configuration lists as "Generate Code"
does, computed lists and `Func<MemberInfo, bool>` filters included, and writes
the same configuration as `[assembly: ...]` attributes, which the source
generator reads, one `XLuaConfig.cs` per assembly, plus a report of what was
converted, what has no attribute form and what to check. It changes nothing
else. Since Generate Code was removed (M11) the lists bind nothing: delete them
after the conversion ([Generate Code removed](#generate-code-removed)).
See [Converting configuration lists](../Assets/XLua/Doc/Migrating_Configuration.md).

- **Where:** a project's own types in the assembly that defines them; engine,
  .NET and package types in an `XLuaBindings` asmdef that the tool creates
  (or in Assembly-CSharp when xLua compiles into it, after XLua > Project
  Layout > Use Assembly-CSharp); hotfix types in their own assembly.
- **Not converted:** `[GenCodeMenu]` custom generators and their templates
  (removed in M11), `GENERIC_SHARING`, and types without a `typeof` spelling;
  the report lists them. `[GenPath]` is listed under "check by hand": since M11
  it only places the hotfix id maps. `GEN_CODE_MINIMIZE` is listed under "check by hand": the
  source generator follows the define.
- **Snapshots:** computed lists and filters are written as they evaluated when
  the tool ran, with the Editor's defines; run the tool again after changing
  them, adding assemblies or upgrading Unity.
- **One type per attribute** by default, so that a diff shows one line per
  configured type. **XLua > Migrate Configuration to Attributes (Grouped)**
  (`ConfigMigrationMenu.MigrateGrouped`) and `XLuaGenerate --migrate --group`
  instead write the types of a list that share an attribute, flags and
  namespace into one attribute, one `typeof` per line (2026-10-18).
- **Without asmdefs** (xLua in Assembly-CSharp), the menu took
  Assembly-CSharp for xLua's own assembly until 2026-10-28: it copied the
  `[LuaCallCSharp]`, `[CSharpCallLua]` and `[GCOptimize]` attributes on the
  project's types into `Assets/XLuaConfig/XLuaConfig.cs`, where the source
  generator already reads them, and reported the types of `[Hotfix]` lists as
  "not one the tool writes into". It now writes the lists of Assembly-CSharp's
  types there and leaves attributes on types alone, as in the asmdef layout.
  Running the menu a second time also no longer stops with "was not rewritten
  because it was converted from [...] on the types of ..., which no longer
  exists" for a file converted from the attributes of a precompiled DLL. A
  file the old menu wrote is kept as it is (it names lists that are gone);
  delete it and run the menu again to drop the copied attributes.
- **xLua's examples** now also carry their converted configuration,
  `Assets/XLua/Examples/XLuaConfig.cs`, next to the lists. The tool leaves
  lists that such a file already converts out of its own output.
- **`XLuaGenerate`** (all modes) now answers `Assembly.Load` of an assembly
  given on its command line with the loaded one, instead of a second copy from
  the search paths, and loads what the given assemblies reference before it
  reads the configuration, as the Editor has every assembly loaded. A list that
  loads `Assembly-CSharp` by name, as `ExampleConfig.cs` shows, therefore gets
  the same types Generate Code reads, and lists computed from
  `AppDomain.CurrentDomain.GetAssemblies()` see the same assemblies each time.
  New `XLuaGenerate --hotfix-assemblies <assemblies...>` (Generate Code's
  mode) counts the `[Hotfix]` lists of the given assemblies, as the Editor
  does with `HOTFIX_ENABLE`, so their signatures get hotfix bridges; without
  it XLuaGenerate ignores `[Hotfix]` lists, as before. (Both went with the
  wrapper mode in M11.)

## Obfuscation (name table and obfuz)

Generated bindings, from "Generate Code" and from the source generator,
record each configured type's original name next to `typeof`. Since source
generator milestone M8 the runtime looks types up through that table, so a
type an obfuscator renamed after compilation keeps working from Lua under its
original name. See [source-generator.md](design/source-generator.md#m8-as-implemented-2026-10-18)
and [obfuscators.md](research/obfuscators.md).

- **Type lookups:** `CS.A.B`, `xlua.import_type`, `xlua.import_generic_type`
  (also for the generic definition of a configured closed type), the type's
  place under `CS` and the registry key of its object metatable use the name
  table entry, then the current name as before for types without one.
  Without generated bindings nothing changes. With them:
  - a bound type is found without searching every assembly. When two
    assemblies define a type of the same full name and one of them is bound,
    `CS.A.B` gives the bound one (before: the first assembly in load order);
  - the metatable key of a bound generic type is `Type.ToString()`
    (``System.Collections.Generic.List`1[System.Int32]``) instead of the
    assembly-qualified `Type.FullName`. Other types keep `FullName`, which is
    the same string. Only code that indexes `debug.getregistry()` by such a
    key sees the difference.
- **New `xlua.typename(t)`:** the name Lua knows a type by (a class table, a
  `System.Type` or a type name): the original name from the table, or
  `Type.ToString()`. `xlua.util`'s `auto_id_map` uses it to find a type in
  the IntKey `hotfix_id_map`, which the injector writes with original names.
- **`[Obfuscation(Exclude = true)]`** (`System.Reflection.ObfuscationAttribute`)
  is now on the injected `__Hotfix` fields and the `__XLUA_GEN.__XLUA_GEN_FLAG`
  type (hotfix), and on the source generator's `Bindings.Register`, for
  obfuscators that read it and run after the injector (Obfuscar, ConfuserEx,
  GuardingPear, Beebyte). Its default `StripAfterObfuscation` lets them remove
  it.
- **obfuz** (4.0.0) does not read that attribute; it reads rule files. With
  the source generator, each assembly carries an obfuz rule fragment
  (`XLua.ObfuzRules.g.cs`, Editor builds only). **XLua > Obfuscation > Write
  obfuz Rule File** writes `Assets/Obfuz/xlua-ng-rules.xml` for the
  assemblies obfuz obfuscates; add that file once to obfuz's settings
  (Symbol Obfuscation > Rule Files). While obfuz lists it, xLua rewrites it
  before each build; while obfuz is enabled and does not list it, builds log
  a warning. It keeps the names of `[ReflectionUse]` types and their members,
  `[DoNotGen]` and fallback members with their types, the value names of
  `[LuaCallCSharp]` enums with more than 20 values, everything in the `XLua`
  namespaces when xLua's code is in an obfuscated assembly and, with
  `HOTFIX_ENABLE`, the `__Hotfix` fields. With `HOTFIX_ENABLE` a build also
  warns when obfuz runs before the injector (keep obfuz's default order,
  10000).
- **Still reached by the current name** (keep these names, with the
  obfuscator's own exclusions, or configure the type):
  - types without any configuration attribute: add `[ReflectionUse]` (or
    another configuration attribute) to put them into the table;
  - members Lua reaches through reflection: members of unconfigured or
    `[ReflectionUse]` types, `[DoNotGen]` and fallback members, enum value
    names of enums with more than 20 values, and everything that
    `xlua.private_accessible` or a hotfix patch accesses by name. With a tool
    that reads `[Obfuscation]`, mark them yourself; the generator cannot add
    attributes to members you declare;
  - the names `tostring` shows, such as an enum value's `ToString()`;
  - with "Generate Code", xLua's generated classes (`XLua.CSObjectWrap`):
    exclude the `XLua` namespaces, as the obfuz rule file does.

## XLua.Core and LuaScript

Since 2026-10-18 (milestone 6 of [core-luascript.md](design/core-luascript.md#85-milestone-6-as-implemented-2026-10-18)),
part of the runtime is a second folder, `Assets/XLua/Core`, the `XLua.Core`
assembly of the asmdef layout (the default since 2026-10-18) and the package.
Without the asmdefs it compiles into Assembly-CSharp with the rest.

- **Copy `Assets/XLua/Core` too.** It holds `LuaDLL.cs`, `LuaOptions.cs`,
  `LuaException.cs` and `LuaWatchdog.cs` (moved from `Assets/XLua/Src`, same
  GUIDs), `Editor/PluginVariants.cs` (moved from `Src/Editor`), and new
  files. When updating a project, delete the old copies in
  `Assets/XLua/Src`, or the types are defined twice. `LuaTypes`,
  `LuaGCOptions`, `LuaThreadStatus` and `MonoPInvokeCallbackAttribute` moved
  from `ObjectTranslator.cs` to `Core/LuaTypes.cs`, with the same names.
- **`XLua.LuaDLL.Lua` holds only the Lua C API and the core layer** (owner
  decision 1): the functions every native library defines, such as
  `lua_gettop`, `lua_pushnumber`, `luaL_ref`, `lua_tostring`, and the
  `xlua_core_*` functions. The imports of the bridge (`xlua.c` and
  `i64lib.c`: `xlua_pushinteger`, `lua_pushstring`, `xlua_getglobal`,
  `lua_pcall`, `lua_insert`, `lua_tonumber`, `luaL_error`, `lua_pushint64`,
  `xlua_pushcsobj`, the struct packers, `StackToString` ...) moved to the new
  class `XLua.LuaDLL.Bridge`, which derives from `Lua`, so `Bridge` reaches
  both. **Code that calls bridge functions through `XLua.LuaDLL.Lua`
  (usually `using LuaAPI = XLua.LuaDLL.Lua;`) no longer compiles: change
  the alias to `XLua.LuaDLL.Bridge`.** xLua's own runtime, the templates of
  Generate Code and the source generator do so. **Regenerate generated code**
  (XLua > Clear Generated Code, then Generate Code): wrappers from before
  use the old alias. Reflection that looks a bridge method up on
  `typeof(LuaDLL.Lua)` must use `Bridge`, and lookups on `Bridge` of the C
  API need `BindingFlags.FlattenHierarchy`.
- **The asmdef layout** (above) has two more asmdefs:
  `Assets/XLua/Core/XLua.Core.asmdef` and
  `Assets/XLua/Core/Editor/XLua.Core.Editor.asmdef`. `XLua.asmdef`
  references `XLua.Core`, and `XLua.Editor.asmdef` references both. `XLua.Core` grants `XLua` its
  internal members (`InternalsVisibleTo`). The UPM package has them in its
  `Core` folder.
- **Your asmdefs that use the source generator reference `XLua.Core` too**
  (asmdef and package layouts): `"references": ["XLua", "XLua.Core"]`. The
  generated wrappers name `XLua.LuaDLL.Bridge` (whose base class `Lua` is in
  `XLua.Core`), `lua_CSFunction`, `MonoPInvokeCallbackAttribute` and
  `LuaTypes`, and Unity does not pass `XLua`'s references on to an asmdef
  that references it. An asmdef with `"references": ["XLua"]` only gets
  error `XLSG012`, which names the asmdef and the line to add, and no
  bindings (before 2026-10-18 it got a CS0234 for every wrapper method).
  XLua > Migrate Configuration to Attributes adds `XLua.Core` to the
  `XLuaBindings.asmdef` it writes. Assemblies without an asmdef
  (Assembly-CSharp and the firstpass assemblies) reference every
  auto-referenced asmdef already. The reference stays mandatory (owner,
  2026-10-18): generated code that compiles against `XLua` alone was built
  and measured (+6.8 KB APK, +17.7 KB `libil2cpp.so` at Minimal stripping)
  and not taken
  ([source-generator.md](design/source-generator.md#generated-code-without-xluacore-measured-not-taken)).
- **`LuaScript`** (sandboxed Lua 5.4 and 5.5 without the C# bridge:
  `Load`, `Call`, limits, `require` through `ModuleResolver`, since
  milestone 7 `Register` of C# functions, since milestone 8 table and
  function handles, and since milestone 9 coroutines driven from C#) compiles only with the scripting
  define `XLUA_LUASCRIPT` (next to the bridge) or `XLUA_CORE_ONLY` (core
  only). It needs a native library with the sandbox: the `core` preset's
  (`XLUA_PRESET=core XLUA_VARIANT=core build/make_android_lua55.sh`, the core
  plugin variant), or a legacy or minimal library built with
  `XLUA_LUASCRIPT=ON`. On another library its constructor throws
  `NotSupportedException` (on iOS and WebGL the player does not link). The
  WebGL plugin source has no sandbox yet.
- **The `buffer` library** (milestone 10, 2026-10-18): `LuaLibs.Buffer`
  opens it for a `LuaScript` (it is not in `LuaLibs.Safe` or the default), and
  `LuaBufferRef` (`LuaScript.NewBuffer`, buffer results and arguments) reads
  and writes its bytes from C#. Before, `LuaLibs.Buffer` threw
  `NotSupportedException`; it still does on a native library built without
  CMake `XLUA_BUFFER` (on in the core preset, off in legacy and minimal). The
  functions have Luau's names, with Lua's integer rule: a fraction is refused
  where Luau truncates. A `LuaEnv` still ignores the bit. Since 2026-10-18
  the table also has `isbuffer(v)` and the 64-bit `readi64`/`writei64`,
  which Luau does not have; `pairs(buffer)` lists 27 functions.
- **`LuaOptions.AllowInterrupt`** (2026-10-18): null by default, which keeps
  each API's behaviour (`LuaScript.Interrupt()` stops a call also without
  limits; `LuaEnv.Interrupt()` only with one). `false` makes a `LuaScript`
  call without limits skip the entry protocol (about 6 ns per call and 1 ns
  per C# function called from Lua) and `Interrupt()` then reaches only calls
  with a limit; `true` lets `LuaEnv.Interrupt()` stop calls without limits
  (`NotSupportedException` on Lua 5.3 and LuaJIT).
- **`XLUA_CORE_ONLY`** leaves out `XLua` and `XLua.Editor` in the asmdef
  layout and the package (their define constraints); without the asmdefs
  (Use Assembly-CSharp), delete `Assets/XLua/Src` for a core-only project.
- **New API, no change for existing code:** `LuaException.LuaStackTrace`
  (null for `LuaEnv`'s errors, which keep the traceback in the message, and
  added to `ToString` when set); `LuaLimit.Memory`, added after the existing
  values; `LuaOptions.CheckThread` and `LuaOptions.ScriptDefaults` for
  LuaScript, which also reads `LuaOptions.Libraries` ([above](#luaoptionslibraries);
  for a LuaScript `Default` is `LuaLibs.Safe`, and io, os, debug, package,
  bit, jit and ffi are refused).
- **Native:** `xlua_core_run` is replaced by `xlua_core_call` (typed
  arguments and results, one call per load or function call), and the core
  ABI version is 3: a `LuaEnv(LuaOptions)` or `LuaScript` on a library from
  before throws `InvalidProgramException` ("wrong core version"). The core
  preset's managed export list (`build/xlua_core_exports.txt`) has 19
  functions; the Lua C API functions that only the bridge imports are not
  exported from a core library.
- **Core ABI version 4** (milestone 7, `LuaScript.Register`): the new
  exports `xlua_core_set_dispatcher` and `xlua_core_register` (23 in the core
  list with milestone 10's buffer exports), the host function frame
  `xlua_core_frame`, and an out field `flags` at the end of
  `xlua_core_call_t`. A `LuaEnv(LuaOptions)` or `LuaScript` on a
  library built before throws `InvalidProgramException` ("wrong core
  version"): rebuild the native library with the C# sources. Native code
  that fills `xlua_core_call_t` itself must add the field.
- **Handles, core ABI version 5** (milestone 8, 2026-10-18): `LuaTableRef`,
  `LuaFunctionRef`, `LuaKey`, `Pairs`/`IPairs`, `Globals`, `NewEnvironment`,
  `NewTable`, `Compile`, the environment overloads of `Load` and `Register`,
  `LuaCall.GetTable`/`GetFunction`/`GetBuffer` and
  `LuaRuntimeException.ErrorValue`. What changes for code written against
  milestones 6 and 7:
  - a table or function result of `Call<LuaValue>` or `Load<LuaValue>`, or in a
    results span, is now an owning handle: dispose it (`LuaValue.Dispose`, new) or it lives
    until the script is disposed (`LuaScript.LiveHandles` counts it); before, it
    carried only its type;
  - `LuaCall.Get(i)` of a table, function or buffer argument is a borrowed
    handle, valid until the host function returns;
  - a `LuaValue` of a table or function can be passed back as an argument or
    returned from a host function; one without a handle still cannot;
  - `LuaBufferRef` equality compares the buffers in native code (the same
    result as before);
  - **native:** `xlua_core_handle` (one export for every handle operation)
    replaces `xlua_core_newenv`, `xlua_core_newbuffer` and `xlua_core_tobuffer`
    (21 core exports); `xlua_core_unref` releases any table, function or
    userdata reference, and an environment handle is any table handle, so a
    registry reference to a table is no longer refused as an environment;
    `XLUA_CALL_REF` and `XLUA_CALL_COMPILE` work; table and function results
    of `xlua_core_call` carry owning handles in `u.i`, and a table or function
    error value one in `error_ref` (`XLUA_CALLF_ERROR_TABLE`, `_FUNC`). The
    version check of `LuaEnv(LuaOptions)` and `LuaScript` throws
    `InvalidProgramException` for a library built before.
- **Stops on time, and `Interrupt()` without limits** (2026-10-18): with an
  `InstructionLimit` set, `TimeLimit` and `Interrupt()` could stop a call up
  to about five seconds late; they now stop it within a few milliseconds, in
  `LuaScript` and in `LuaEnv(LuaOptions)`. `LuaScript.Interrupt()` now stops
  a running call also when the script has no instruction or time limit
  (before, it did nothing); `LuaEnv.Interrupt()` still needs a limit, as
  documented. **Native:** the `xlua_core_set_limits` flag
  `XLUA_LIMITS_INTERRUPT` (4), which `LuaScript` sets on every state, makes
  every outermost call interruptible (about 6 ns per `xlua_core_call` on a
  macOS arm64 host); the instruction budget is handed to threads in slices
  of 2^20 instead of 2^30 instructions (the count is unchanged), and a stop
  armed from another thread also uses Lua's line hook. See
  [core-stop-latency.md](research/core-stop-latency.md).
- **A stopped call never returns normally; LuaEnv's callbacks from
  coroutines are limited** (2026-10-18, a second change):
  - A call whose limit tripped, or which an `Interrupt()` reached, now
    fails also when its body had already finished. Before, it could return
    normally: `return pcall(f)` where `f` hit the instruction limit, a stop
    set after the call's last instruction, an `Interrupt()` while `LuaScript`
    compiled a chunk without running it.
  - `LuaEnv(LuaOptions)`: `DoString` and `LoadString` throw
    `LuaInterruptedException` or `LuaTimeLimitException` when `Interrupt()`
    or the time limit comes while they compile. Before, the stop was dropped
    and the chunk ran without it.
  - `LuaEnv(LuaOptions)`: a `LuaFunction` or delegate called from a C#
    function while a Lua coroutine runs is now under the environment's
    limits and `Interrupt()`. Before, nothing stopped it and its
    instructions were not counted.

  `Interrupt()` before a call begins still does nothing. See
  [core-stop-latency.md](research/core-stop-latency.md#missed-stops-under-load-2026-10-18-second-investigation).
- **Coroutines, core ABI version 6** (milestone 9, 2026-10-18):
  `LuaScript.Start`, `LuaCoroutine` (`Resume`, `Status`, `co[i]`, `Close`,
  `Retain`), the drivers `Run` (IEnumerator), `RunAsync` (Task) and, on Unity
  2023.1 and later, `RunAwaitable`, with `LuaReply`; the scripting define
  `XLUA_NO_COROUTINE_DRIVERS` leaves the drivers out. What changes for code
  written against milestone 8:
  - a coroutine (`coroutine.create`, `coroutine.running()`) that reaches C#
    is a handle: `LuaType.Coroutine` (new, 9; before such a value was
    `LuaType.Other`) with `LuaValue.AsCoroutine()`. As a result of
    `Call<LuaValue>` or in a results span it is owning: dispose it, or it
    counts in `LiveHandles` until the script is disposed. It can be passed
    back to Lua, which threw `ArgumentException` before;
  - `LuaCall.ArgType(i)` of a coroutine argument is `Coroutine` (was
    `Other`), and `LuaCall.Get(i)` gives a borrowed handle;
  - **native:** `XLUA_T_THREAD` (9) values carry an owning handle in `u.i`
    and the coroutine's status (`XLUA_CO_*`) in `len`, where they were
    `XLUA_T_OTHER` without a handle; `xlua_core_call` takes the kinds
    `XLUA_CALL_RESUME`, `XLUA_CALL_CLOSE` and the flag `XLUA_CALL_START`
    (with `XLUA_CALL_GLOBAL` or `_REF`; the new coroutine's handle comes back
    in `fn`), returns `XLUA_YIELD` for a step that yielded, and sets
    `XLUA_CALLF_DEAD`; `xlua_core_unref` releases coroutine handles. No
    export was added. The version check of `LuaEnv(LuaOptions)` and
    `LuaScript` throws `InvalidProgramException` for a library built before.
- **Table loops under the C budget, and handle errors under `xpcall`**
  (security fixes, 2026-10-18;
  [core-luascript.md](design/core-luascript.md), section 8.12):
  - with an instruction limit, `table.insert`, `table.remove`,
    `table.concat` and `table.sort` now count one instruction per element
    step towards `InstructionLimit`, as `table.move` and patterns already
    did, so a script that sorts large tables under a tight limit may stop
    earlier. Without a limit nothing changes. This applies to
    `LuaEnv(LuaOptions)` with a limit as well;
  - a table operation from C# that fails inside a host function (a nil or
    NaN key) no longer runs the message handler of a `xpcall` in the
    script: the C# exception is all that happens, as for any other caller.
- **A host function's type error for an argument the call did not pass**
  (fix, 2026-10-28): `LuaCall.GetLong(30)` and the other getters on a call
  with fewer arguments failed Lua's API check ("unacceptable index") in a
  library built with `XLUA_APICHECK` once the argument number passed the
  frame's 20 spare slots; the error is raised without reading the
  argument. The message stays "bad argument #31 to 'f' (number expected,
  got no value)". A frame with a type error for argument 0 or below, which
  `LuaCall` does not produce, gets the same message instead of reading an
  invalid stack index.
- **A coroutine error whose `__tostring` raises, and the exports on a full
  stack** (fixes, 2026-10-28):
  - `LuaScript.Start` and `LuaCoroutine.Resume` for a coroutine that fails
    with an error object whose `__tostring` raises threw
    `LuaRuntimeException` with "unknown error (status 2)", no
    `LuaStackTrace` and no `ErrorValue`. They now throw it with "(error
    object is a table value)", the text a call gives for an error object
    without a usable `__tostring`, with the coroutine's traceback and the
    error object as `ErrorValue`. A memory error or a stop inside that
    `__tostring` throws as it does in a call. Calls (`Load`, `Call`) do the
    same since the next entry.
  - A call (`Load`, `Call`, `LuaFunctionRef.Call`) that fails with an error
    object whose `__tostring` raises (owner decision Q54, 2026-10-28) threw
    `LuaRuntimeException` for what `__tostring` raised instead: for a table,
    "(error object is a table value)" with that table as `ErrorValue`; for a
    string, that message ("chunk:1: again"), also as `ErrorValue`, with the
    traceback of `__tostring`. It now keeps the error object itself as `ErrorValue`, with
    "(error object is a table value)" and the traceback of where it was
    raised, as a coroutine does. A memory error or a stop inside that
    `__tostring` throws as before. **native:** `xlua_core_call` gives the
    same through `error`, `traceback` and `error_ref`.
  - **native:** `xlua_core_call`, `xlua_core_handle`, `xlua_core_register`,
    `xlua_core_set_require` and `xlua_core_unref` pushed a value before they
    checked the stack, so a C host whose stack was exactly full failed Lua's
    API check (an abort in an `XLUA_APICHECK` build). They now check first:
    `xlua_core_call` returns `XLUA_ERRSTACK` with "stack overflow", the next
    three `XLUA_ERRMEM`, and `xlua_core_unref` returns 0 and keeps the
    handle. `LuaScript` never fills the stack; no ABI change.
- **The first instruction or time limit of a state that is out of memory**
  (fix, 2026-10-28): the first `xlua_core_set_limits` of a state creates
  the stop messages ("instruction limit exceeded" and the others). It
  allocated them outside a protected call and with no stack check, so when
  the memory limit refused the allocation (a `MemoryLimit` already used up,
  also before any call) Lua panicked and the process aborted, and a C host
  whose stack was exactly full failed Lua's API check. What can be seen:
  - `LuaScript`'s `InstructionLimit` and `TimeLimit` setters and its
    constructor (with limits, or with `AllowInterrupt`) throw
    `LuaMemoryLimitException` there, and `LuaEnv`'s setters and constructor
    with `LuaOptions` throw `OutOfMemoryException`; the limits stay as they
    were, and a later call with room succeeds;
  - **native:** `xlua_core_set_limits` returns `XLUA_ERRMEM` (4) then,
    besides 1 and 0, and pushes nothing. Later calls create nothing, so
    they never fail. No ABI change.
- **Smaller core-only players** (2026-10-18,
  [core-luascript.md](design/core-luascript.md) section 8.15): the same
  `LuaScript` API in less IL2CPP code. What can be seen:
  - with `XLUA_CORE_ONLY`, `XLua.LuaDLL.Lua` in `XLua.Core` has only the
    imports `LuaScript` uses (`xlua_core_*` and `lua_gc`); the Lua C API
    functions that only the bridge calls (`lua_pushnumber`, `luaL_ref`,
    `lua_tostring` ...) and `xlua_core_newstate` are left out. A core
    library does not export them, so a call threw `EntryPointNotFoundException`
    before. In the Assets layout a project with `XLUA_CORE_ONLY` that still
    has `Assets/XLua/Src` no longer compiles: delete `Src`, as above;
  - the new scripting define `XLUA_NO_LUASCRIPT_EXTRAS` leaves out
    `LuaTableRef.ToDictionary`, `LuaTableRef.IPairs`, the tuple calls
    `Call<T1, T2>` and `Call<T1, T2, T3>` of `LuaScript` and `LuaFunctionRef`,
    and the `Register` shorthands with three and four arguments. Without it
    nothing changes;
  - `LuaCoroutine.RunAsync` and `RunAwaitable` are no longer `async`
    methods; the task or `Awaitable` completes as before (cancelled for an
    `OperationCanceledException`, faulted for other errors). Their state is a
    little larger: `Start` and `Dispose` of a coroutine allocate 256 to 280
    bytes (were 184 to 192), a run of ten yields with `Run` 280 to 304 (168
    to 176) and with `RunAsync` and handlers that finish at once 392 to 436
    (104 to 138). Calls, resumes and table access still allocate nothing.

## Planned changes (not released yet)

These are decided but not implemented; see [design](design/).

- **Presets:** `legacy` and `minimal` are available ([Presets](#presets));
  `core` has its native library and the first part of `LuaScript`
  ([XLua.Core and LuaScript](#xluacore-and-luascript)), with `Register`, its
  `buffer` library, table handles and coroutines; what is left is in
  [core-luascript.md](design/core-luascript.md). Part of the minimal
  column is still to do: the type allowlist (postponed).
- **Code generation:** done in M11: source generation replaced the "Generate
  Code" menu ([Generate Code removed](#generate-code-removed)).
