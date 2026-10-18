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
| Lua number to a C# integer parameter, field, property or indexer (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`) and to an integer struct field from `xlua.genaccessor` | A float with a fraction was cast without a check: `Foo(2.75)` passed 0 on Lua 5.3+ (2 for `uint`) and 2 on LuaJIT; NaN and infinities gave 0 or a CPU-dependent value; on LuaJIT a numeric string gave 0 for `long` and `ulong`. Overload checks accepted any number, so `Foo(int)` declared before `Foo(double)` took 2.75 | Lua 5.3's rule on every runtime, LuaJIT included, and in reflection, emitted and generated wrappers: an integer, a float with an integer value (`3.0`) or a numeric string that converts to one (`"12"`) is accepted; a fraction, NaN, an infinity or a value outside the 64-bit range raises `bad argument #n to 'Foo' (number has no integer representation)` (from C# wrappers inside a `c# exception:` error). Integral values outside the C# type's range still wrap (`Byte(300)` is 44, `UInt(-1)` is 4294967295). An integer overload now matches only a number with an integer value, so `Foo(2.75)` picks `Foo(double)`; when several overloads match, the first declared one still wins (`Foo(3)` calls `Foo(double)` if it is declared before `Foo(int)`). The other integer slots got the same rule later (next row). Native bridge version 107: rebuild the plugins and regenerate code | `fix(native): convert Lua numbers to integers by Lua 5.3's rule`, `fix(runtime): apply Lua's integer rule to C# integer parameters and fields` |
| Lua number to the other C# integer slots: array elements and indices (`arr[i] = v`, `arr[i]`), Lua tables converted to arrays, `List<T>`, `Dictionary<K, V>` values and struct fields (reflection and `[GCOptimize]`), `params` arrays, nullable integers, enums and `Enum.__CastFrom`, and values C# reads from Lua: delegate and interface results and out values, `LuaFunction.Func`/`Call` results, `LuaTable.Get`, `GetInPath`, `ForEach` and `Cast` | As in the row above before its change: a float with a fraction became 0 (Lua 5.3+) or the truncated value (LuaJIT), NaN and infinities 0 or a CPU-dependent value. A 64-bit enum got only the low 32 bits of a number. A `List<int>` or `Dictionary<_, int>` value with a fraction was left out silently (since the row above), and a delegate result that failed to convert could leave its results on the Lua stack | The same rule on every runtime and binding path (reflection, emitted, generated, `GEN_CODE_MINIMIZE`, source generator). A `params` element names its argument (`bad argument #2 to 'Foo' (number has no integer representation)`); the other slots throw `InvalidCastException: number has no integer representation`, which Lua sees inside the wrapper's `c# exception:` error and C# callers (delegates, `LuaTable`, `LuaFunction`) receive as is. Values of other types are still left out of a `List` or `Dictionary`, and `Dictionary` keys and `LuaTable.ForEach`/`GetKeys` keys still only select entries (`[1.5]` is not an `int` key). An enum takes all 64 bits of its underlying type. The delegate and interface bridges read their results inside `try`/`finally`, so a failed conversion restores the Lua stack. Integral values outside the type's range still wrap unless `XLUA_STRICT_INT_RANGE` is defined (opt-in defines below). Regenerate code | `fix(runtime): apply Lua's integer rule to the other integer slots`, `fix(generator): read integer results by the integer rule inside try/finally` |
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
| `[BlackList]` in emitted and reflection wrappers, in the Editor and outside Unity (`XLUA_GENERAL`): types without generated code, including types without `[LuaCallCSharp]` in a project with generated code, non-public types, the .NET Standard 2.0 Editor profile, `FORCE_REFLECTION` and the members `xlua.private_accessible` adds | Emitted wrappers ignored `Func<MemberInfo, bool>` filters and matched entries with their own rules: parameter types by `FullName` (so an entry with a generic parameter type such as ``System.Collections.Generic.List`1[System.Int32]`` did not match), constructor entries with parameter types never. Reflection wrappers applied no `[BlackList]` at all, neither the attribute nor entries nor filters. Members that generated code leaves out stayed callable from Lua, for example `Dictionary<,>.TryAdd` with `ExampleConfig`'s `MethodFilter` | Both apply the attribute, the entries and the filters with the generator's rules (`BlackListRules`: type and parameter type names as `Type.ToString()` gives them; an entry or filter that matches a property accessor `get_X`/`set_X` hides that direction only), so they leave out the same members as generated code. An entry with an assembly-qualified parameter type now matches nowhere; write it as **XLua/Validate Setup** expects. A filter that throws is logged once per filter and the member stays. Players still read no configuration: their reflection wrappers show every member | `fix(runtime): apply [BlackList] filters in emitted and reflection wrappers` |
| A `[DoNotGen]` method with an extension method of the same name, in generated code | The lazy member wrapped only the type's own methods: `o:Method("x")` called `Method(int)` with a converted argument instead of `Method(this T, string)`. If the own method was static, or the name was only an extension method's, the extension method was not callable at all. Emitted and reflection wrappers had both | The generator registers a lazy instance member for such a name and lists its extension methods in `XLuaGenAutoRegister`; the lazy member takes the static methods for the class and the instance and extension methods for objects, like the other wrappers. Regenerate code | `fix(generator): keep extension methods of [DoNotGen] methods callable` |
| `xlua.private_accessible(T)` on a type with an extension method that shares its name with a public method | The extension method replaced the public overloads: `o:Method(1)` called `Method(this T, string)` with `"1"` | The public overloads stay | `fix(runtime): keep extension overloads after xlua.private_accessible` |
| Interface bridges (a Lua table as a `[CSharpCallLua]` interface) with an ordinary method called `get_X`, `set_X`, `add_X` or `remove_X`, an indexer renamed with `[IndexerName]`, or a property called `Item` without parameters | The emitted bridge (types without generated code on Mono) failed to load with `TypeLoadException` for the first two, and for the property called `Item` called the Lua function `get_Item`/`set_Item` instead of reading and writing the table's field `Item`. **Generate Code** left out methods called `add_X` and `remove_X` (the bridge did not compile) and stopped with "index out of range" on the other two | The emitted and generated bridges implement them as the source generator does: such methods call the Lua function of that name, an indexer calls its accessors' names (`get_At` for `[IndexerName("At")]`), and a property called `Item` is a field. Regenerate code | `fix(runtime): bridge interface members by kind, not by name` |
| `xlua.get_generic_method(type, name, index)` | The index was read only when `lua_isinteger` was true: never on LuaJIT, and not for a float such as `1.0` on Lua 5.3+, so the first generic method of that name was used | Any number with an integer value selects the method, on every runtime | `fix(runtime): honour the method index of xlua.get_generic_method on LuaJIT` |
| LuaSocket (`socket`, `socket.core`) and `mime` | `LuaEnv` registered `socket` and `socket.core` as C# built-in libraries (`AddBuildin`) that called the `luaopen_socket_core` import, except in Switch and WebGL players. `mime.c` was compiled (and exported on Android and desktop) but `require "mime"` failed | `luaopen_xlua` puts `socket.core`, `socket`, `mime.core` and `mime` into `package.preload` when the native library contains them (CMake `XLUA_LUASOCKET` and `XLUA_MIME`, both on by default; see Native plugins below). C# imports nothing for them, so a library built without LuaSocket links on iOS with the same C#. `require "socket"` returns the same core module as before; `require "mime"` now works. `package.preload` lists the four names, and since the preload searcher runs before the built-in one, `AddBuildin` with one of those names no longer takes effect (upstream's `AddBuildin("socket", ...)` threw for the duplicate name); set `package.preload[name] = nil` first or build without the module. `LuaDLL.Lua.luaopen_socket_core` stays, as a C# method that runs the preload entry and pushes the module; without LuaSocket it raises `LuaSocket is not compiled into the xlua native library`. Which platforms have LuaSocket now depends only on the native build: the WebGL source build and the Switch build do not compile it, the Editor's library does | `feat(native): register LuaSocket and mime in package.preload`, `refactor(runtime): drop the C# LuaSocket registration and import` |

## Opt-in defines (no change unless defined)

| Define | Effect | Docs |
|---|---|---|
| `XLUA_NO_STREAMING_ASSETS_LOADER` | Removes the StreamingAssets `require` searcher (−570 KB on an Android APK that does not otherwise use UnityWebRequest) | [Lua_Loaders.md](../Assets/XLua/Doc/Lua_Loaders.md) |
| `XLUA_LOCK_METATABLES` | `getmetatable` on bridge objects returns `"locked"` and `setmetatable` fails | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md), [Security.md](../Assets/XLua/Doc/Security.md) |
| `XLUA_CHECK_OBJECTS` | The bridge reads a C# object (`self`, `__tostring`, delegate calls, enum operators) only from a userdata with xLua's tag; a foreign userdata passed as `self` raises instead of naming another object | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_check_objects), [Security.md](../Assets/XLua/Doc/Security.md#opt-in-bridge-hardening) |
| `XLUA_CHECK_STRUCT_ACCESSORS` | `xlua.genaccessor` getters and setters also require xLua's tag; a userdata that only looks like a struct raises `invalid c# struct!` | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_check_struct_accessors) |
| `XLUA_RESTRICT_GETMETATABLE` | `xlua.getmetatable` returns a copy without `__gc` and the tag; editing it changes the type only after `xlua.setmetatable` | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_restrict_getmetatable) |
| `XLUA_CHECK_OBJECT_CACHE` | A value stored in the userdata cache is used for an object only if it is that object's userdata | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_check_object_cache) |
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
     `XLUA_CHECK_OBJECTS`, `XLUA_CHECK_STRUCT_ACCESSORS`,
     `XLUA_RESTRICT_GETMETATABLE`, `XLUA_CHECK_OBJECT_CACHE`,
     `XLUA_LOCK_XLUA_TABLE`, `XLUA_RESTRICT_PRIVATE_ACCESS` and
     `XLUA_REDUCED_DEBUG`; each one's
     behaviour change is in the table above. A `csc.rsp` entry also works for the
     C#, but the native plugin selector reads only Player Settings. Code of your
     own that tests one of those defines must also test `XLUA_PRESET_MINIMAL`.
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
     (`General/LuaMemoryLeakChecker`) and no memory snapshot walk, and on
     Android it exports only the functions the C# imports, so native Lua modules
     loaded at run time cannot link against the Lua API
     ([build/README.md](../build/README.md#presets)). Without the variant the
     build warns and uses the default plugin; the minimal C# works on it.

Apart from `xlua.util` (`XLUA_UTIL`), a C# part of the preset cannot be turned
back on while the preset define is set; every native option set in the environment or with
`-D` wins over the preset (`XLUA_PRESET=minimal XLUA_LUASOCKET=ON ...`). Values
the owner has not decided yet are marked provisional in
[presets.md](design/presets.md#provisional-values-owner-decisions).

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
  and `xlua_check_struct_accessors` for `XLUA_CHECK_OBJECT_CACHE` and
  `XLUA_CHECK_STRUCT_ACCESSORS`. The C# side checks the version, so rebuild
  the plugins together with the C# update. Nothing changes without those
  defines.
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
  `new LuaEnv()` is unchanged. A source plugin or
  other build that compiles the C sources itself must add
  `build/xlua_core_limits.c`. See
  [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#luaenv-luaenvluaoptions-options).
- **C budget patch in the vendored Lua 5.4.9 and 5.5.1:** `lstrlib.c` and
  `ltablib.c` charge long pattern matches, plain finds and `table.move` to
  the instruction and time limits through `luai_budget`, which does nothing
  outside libxlua; Lua 5.5's `luaconf.h` gets the same hook include as 5.4's
  `luaconf.h.in`. Results are unchanged (Lua's own test suites pass). See
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
  `XLUA_LIB_<NAME>` for each library, and `xlua_core.c` with `XLUA_SANDBOX`.
- **Bytecode options (opt-in):** two new CMake options, off by default and
  also read from the environment, so the default libraries are unchanged.
  `-DXLUA_BYTECODE_ONLY=ON` (Lua 5.3 to 5.5 with the bridge; refused on LuaJIT)
  leaves Lua's compiler out: every text chunk then fails with "text chunks are
  not supported in this build (bytecode only)", including the Lua source that
  `new LuaEnv()` runs, so `LuaEnv` cannot start on such a library yet. A build
  without CMake compiles `build/xlua_noparser.c` instead of `lparser.c`,
  `lcode.c` and `llex.c`, and `xlua.c` with `XLUA_BYTECODE_ONLY`.
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
  from how LuaJIT itself is built (`msvcbuild_mt.bat nogc64` or not); passing
  `-DGC64=ON` now only gives CMake's unused-variable warning.

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
    works there, with ids of its own, and there is no warning.
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

  See [Hotfix_EN.md](../Assets/XLua/Doc/Hotfix_EN.md).
- **XLua/Validate Setup** checks the installation. It also lists the Lua
  files that earlier versions shipped in `Assets/XLua/Resources`
  (`xlua/util`, `perf/profiler`, `perf/memory`, `tdr/tdr`) when an in-place
  upgrade left them in any `Resources` folder, and says whether each one is
  unchanged from a shipped version; see
  [LuaCATS.md](../Assets/XLua/Doc/LuaCATS.md#validate-setup).
- **LuaCATS** definitions are generated for LuaLS; see
  [LuaCATS.md](../Assets/XLua/Doc/LuaCATS.md).
- **XLua/Lua Memory** is a new Editor window: it takes memory snapshots of
  the running LuaEnv in Play Mode and shows one snapshot or the difference
  between two, with the Lua paths that hold tables and C# objects. Upstream
  had only `General/LuaMemoryLeakChecker` outside Unity and the
  `perf.memory` stub; see
  [Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md#the-lua-memory-window).

## Generated code (XLua/Generate Code)

Generated code no longer extends xLua's own classes as `partial` classes. It
calls a public binding API, `XLua.Binding` (hidden from IDE completion,
version `BindingApi.Version` = 1), so it compiles in an assembly other than
xLua's, as it must once xLua has its own asmdef. The wrappers, bridges and the
members Lua sees are the same as before. See
[source-generator.md](design/source-generator.md), section 3 and milestone M0.

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
- **Hotfix:** the injector takes the bridges from
  `XLua.CSObjectWrap.DelegatesGensBridge` and calls them as static methods.
  The fields it adds still hold a `DelegateBridge`. IntKey and direct bridge
  calls still need xLua, the generated code and the hotfixed types in one
  assembly, as before.

## xLua in its own assemblies (asmdef, opt-in)

By default nothing changes: `Assets/XLua/Src` has no asmdef, and xLua
compiles into Assembly-CSharp and Assembly-CSharp-Editor as before. The
asmdefs for the layout of a UPM package, which the source generator is built
for, ship in `Assets/XLua/Asmdef~` (Unity does not import folders whose name
ends in `~`); see [source-generator.md](design/source-generator.md),
milestone M1.

- **To opt in**, copy `Assets/XLua/Asmdef~/Src/XLua.asmdef` (with its `.meta`)
  into `Assets/XLua/Src`, and `Assets/XLua/Asmdef~/Src/Editor/XLua.Editor.asmdef`
  (with its `.meta`) into `Assets/XLua/Src/Editor`. xLua then compiles into
  `XLua.dll` (every platform) and `XLua.Editor.dll` (Editor only). Keep the
  `.meta` files, so the GUIDs stay the ones a later package uses. To opt out
  again, delete the two copies.
- **What the asmdef layout costs for hotfix.** For types in Assembly-CSharp
  (or any assembly other than xLua's):
  - without the source generator, `IntKey` falls back to field mode, with
    the injector's warning; with the source generator installed, each
    assembly has its own hotfix bridges and IntKey slots, and there is no
    fallback;
  - generic methods and types marked `Inline` are injected since milestone
    M7 (before, they were skipped without a warning);
  - without the source generator, the other methods are injected through a
    delegate type created in the injected assembly, as for any second
    assembly before. The hotfix bridges they call are in the generated code,
    which compiles into Assembly-CSharp; for a game asmdef the injector takes
    them from there (the injected assembly then references
    Assembly-CSharp). Before 2026-10-18 it looked only in xLua's assembly and
    the injected one, and such an asmdef failed with "can not find
    delegate". With the source generator, the injector calls the assembly's
    own bridges directly.
- **What else changes with the asmdefs:**
  - Code that uses xLua's public API keeps compiling: both asmdefs are
    auto-referenced, so Assembly-CSharp, Assembly-CSharp-Editor and project
    asmdefs see them. Code that used xLua's `internal` members does not;
    nothing in `Assets/XLua` outside `Src` does.
  - Generated code ("Generate Code", `Assets/XLua/Gen`) compiles into
    Assembly-CSharp; it uses only xLua's public API, so it compiles there.
    Configuration classes in `Editor` folders compile into
    Assembly-CSharp-Editor.
  - xLua cannot see project types at compile time (an asmdef cannot
    reference Assembly-CSharp); it finds them only at run time, by reflection
    and through the binding registry, so nothing in xLua needs that.
- **The hotfix Editor code no longer assumes Assembly-CSharp** in either
  layout: it finds the project folder and the player's script folder through
  xLua's own assembly (there may be no Assembly-CSharp at all), passes the
  player build of xLua's assembly to the injector, and reads `[Hotfix]` lists
  in editor code from every project assembly instead of only the one that
  holds xLua's editor code. In the default layout xLua's assembly is
  Assembly-CSharp, so the injector gets the same files as before.
- **`XLUA_CORE_ONLY`** (since 2026-10-18): both asmdefs have the define
  constraint `!XLUA_CORE_ONLY`, so a project that defines it compiles
  neither; `XLua.Core` (`Assets/XLua/Core`, with its own two asmdefs since
  milestone 6) is then the only xLua assembly
  ([core-luascript.md](design/core-luascript.md#221-one-package-two-assemblies),
  [XLua.Core and LuaScript](#xluacore-and-luascript)). Without the define
  nothing changes.

## UPM package (preview)

Since 2026-10-18 `Tools/make_upm_package.py` builds xLua as a UPM package
(installation: [README](../README.md#as-a-upm-package-preview)). It is the
asmdef layout above in a package; the Assets layout stays the default and
does not change. Compared with copying `Assets` into a project:

- `Assets/XLua/Src` is `Runtime`, `Assets/XLua/Src/Editor` is `Editor`, and
  `Assets/XLua/Editor/XLuaUnityDefaultConfig.cs` (the Span `BlackList` filter
  on 2022.1 and later) compiles into `XLua.Editor` instead of
  Assembly-CSharp-Editor. `ExampleConfig.cs` comes with the Examples sample.
- Files keep their GUIDs (the repository gained the missing
  `XLuaUnityDefaultConfig.cs.meta`). Folders shared with other plugins, such
  as `Plugins/Android`, do not take the GUIDs of `Assets/Plugins`.
- `xlua_webgl.cpp` includes the C sources from the package's `build~`
  folder instead of `../../../build` next to `Assets` (not built yet).
- Generate Code still writes into `Assets/XLua/Gen`, and LuaCATS into
  `LuaTypes` next to `Assets`; both work with a read-only package.
- `SignatureLoaderTest.cs` of the Examples sample reads its Lua files from
  `Assets/XLua/Examples` in the Editor, so that example only works where the
  sample is imported at that path.

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
  milestone M7) read these forms. The legacy generator ("Generate Code",
  `XLuaGenerate`) and CodeEmit read the attributes on types, members and
  configuration lists as before, and ignore the new forms.
- **xLua's own GCOptimize defaults** (`UnityEngine.Vector2`, `Vector3`,
  `Vector4`, `Color`, `Quaternion`, `Ray`, `Bounds`, `Ray2D`, and the
  properties of `Ray`, `Ray2D` and `Bounds` that `SysGenConfig` lists) are also
  stated as `[assembly: GCOptimize]` and `[assembly: AdditionalProperties]` in
  xLua's assembly, outside `XLUA_GENERAL`, so that the source generator finds
  them. `SysGenConfig` keeps its lists for the legacy generator.
- Each attribute has a read-only `Type` property (null in the old forms);
  `BlackList` also has `TypeName`, `Member`, `ParameterTypes` and
  `ParameterTypeNames`, `DoNotGen` and `AdditionalProperties` have `Members`.

## Source generator (preview)

`General/SourceGenerator` holds the Roslyn source generator that will replace
"Generate Code" (`Tools/build_source_generator.py` builds it, and
`Tools/make_upm_package.py --source-gen` puts it into the UPM package's
`Runtime/Analyzers`). It reads the configuration attributes,
reports diagnostics `XLSG001` to `XLSG012` with file and line, registers
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
  xLua's own assembly and of "Generate Code" output.
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
  (`List`1[System.Int32]::Add(System.Int32)`), as the legacy surface dump
  does; before M9 it listed those of the definition (`List`1[T]::Add(T)`).
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

In Unity it needs the
opt-in asmdef layout above: next to `XLua.asmdef` it runs only in the
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
else: Generate Code still reads only the lists, so keep them while you use it.
See [Converting configuration lists](../Assets/XLua/Doc/Migrating_Configuration.md).

- **Where:** a project's own types in the assembly that defines them; engine,
  .NET and package types in an `XLuaBindings` asmdef that the tool creates
  (or in Assembly-CSharp when xLua compiles into it); hotfix types in their
  own assembly.
- **Not converted:** `[GenPath]`, `[GenCodeMenu]` custom generators and their
  templates, `GEN_CODE_MINIMIZE`, `GENERIC_SHARING`, and types without a
  `typeof` spelling; the report lists them.
- **Snapshots:** computed lists and filters are written as they evaluated when
  the tool ran, with the Editor's defines; run the tool again after changing
  them, adding assemblies or upgrading Unity.
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
  it XLuaGenerate ignores `[Hotfix]` lists, as before.

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
    (`System.Collections.Generic.List`1[System.Int32]`) instead of the
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
assembly of the asmdef and package layouts. In the default Assets layout it
compiles into Assembly-CSharp with the rest, as before.

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
- **The asmdef layout** (opt-in, above) has two more asmdefs: copy
  `Assets/XLua/Asmdef~/Core/XLua.Core.asmdef` into `Assets/XLua/Core` and
  `Asmdef~/Core/Editor/XLua.Core.Editor.asmdef` into `Assets/XLua/Core/Editor`
  (with their `.meta` files). `XLua.asmdef` now references `XLua.Core`, and
  `XLua.Editor.asmdef` references both. `XLua.Core` grants `XLua` its
  internal members (`InternalsVisibleTo`). The UPM package has them in its
  `Core` folder.
- **Your asmdefs that use the source generator reference `XLua.Core` too**
  (asmdef and package layouts). The generated wrappers name
  `XLua.LuaDLL.lua_CSFunction` and `MonoPInvokeCallbackAttribute`, which are
  in `XLua.Core` now, and Unity does not pass `XLua`'s references on to an
  asmdef that references it. An asmdef with `"references": ["XLua"]` only
  gets error `XLSG012` and no bindings (before 2026-10-18 it got a CS0234
  for every wrapper method); add `"XLua.Core"`. XLua > Migrate Configuration
  to Attributes adds it to the `XLuaBindings.asmdef` it writes. Assemblies
  without an asmdef (Assembly-CSharp and the firstpass assemblies) reference
  every auto-referenced asmdef already.
- **`LuaScript`** (sandboxed Lua 5.4 and 5.5 without the C# bridge:
  `Load`, `Call`, limits, `require` through `ModuleResolver`, and since
  milestone 7 `Register` of C# functions; no table handles or C#-driven
  coroutines yet) compiles only with the scripting
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
  where Luau truncates. A `LuaEnv` still ignores the bit.
- **`XLUA_CORE_ONLY`** leaves out `XLua` and `XLua.Editor` only in the asmdef
  and package layouts (their define constraints); in the Assets layout,
  delete `Assets/XLua/Src` for a core-only project.
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

## Planned changes (not released yet)

These are decided but not implemented; see [design](design/).

- **Presets:** `legacy` and `minimal` are available ([Presets](#presets));
  `core` has its native library and the first part of `LuaScript`
  ([XLua.Core and LuaScript](#xluacore-and-luascript)), with `Register` and its
  `buffer` library; table handles and coroutines are planned, see
  [core-luascript.md](design/core-luascript.md). Parts of the minimal
  column are still to do: the type allowlist and an opt-in reflection fallback.
- **Code generation:** source generation replaces the "Generate Code" menu
  before the first public release. The migration tool that converts the
  configuration lists is available ([Converting configuration lists](#converting-configuration-lists));
  see [source-generator.md](design/source-generator.md).
  The partial classes are already gone ([Generated code](#generated-code-xluagenerate-code)).
