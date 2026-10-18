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
| Lua number to C# `uint` (arguments, `uint32` struct fields) | Undefined float cast; results differed by CPU (e.g. -1.5 gave 0 on arm64) | A float is truncated and wraps modulo 2^32, like the integer it truncates to; NaN and values outside the 64-bit range give 0. Arguments and struct fields now follow the integer rule in the next row; this rule remains for `uint[]` elements and wrappers generated before it | `fix(native): convert floats to uint32 without undefined casts` |
| Lua number to a C# integer parameter, field, property or indexer (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`) and to an integer struct field from `xlua.genaccessor` | A float with a fraction was cast without a check: `Foo(2.75)` passed 0 on Lua 5.3+ (2 for `uint`) and 2 on LuaJIT; NaN and infinities gave 0 or a CPU-dependent value; on LuaJIT a numeric string gave 0 for `long` and `ulong`. Overload checks accepted any number, so `Foo(int)` declared before `Foo(double)` took 2.75 | Lua 5.3's rule on every runtime, LuaJIT included, and in reflection, emitted and generated wrappers: an integer, a float with an integer value (`3.0`) or a numeric string that converts to one (`"12"`) is accepted; a fraction, NaN, an infinity or a value outside the 64-bit range raises `bad argument #n to 'Foo' (number has no integer representation)` (from C# wrappers inside a `c# exception:` error). Integral values outside the C# type's range still wrap (`Byte(300)` is 44, `UInt(-1)` is 4294967295). An integer overload now matches only a number with an integer value, so `Foo(2.75)` picks `Foo(double)`; when several overloads match, the first declared one still wins (`Foo(3)` calls `Foo(double)` if it is declared before `Foo(int)`). Values C# reads from Lua (delegate and interface results, `LuaTable.Get`), table contents converted to C# structs or collections, array elements, `params` arrays, nullable integers and enums keep the old conversion. Native bridge version 107: rebuild the plugins and regenerate code | `fix(native): convert Lua numbers to integers by Lua 5.3's rule`, `fix(runtime): apply Lua's integer rule to C# integer parameters and fields` |
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
| `XLUA_NO_SIGNATURE_LOADER` | `SignatureLoader` is not compiled, and with it the RSA/SHA-256 code it pulls in (−239 KB on an Android APK at Minimal stripping; nothing at Medium, which already strips it) | [signature.md](../Assets/XLua/Doc/signature.md) |
| `XLUA_NO_TEMPLATE` | Players do not open the `template` Lua library and do not compile `XLua.TemplateEngine`. The Editor (`UNITY_EDITOR`) and the command-line tools keep both, for code generation and custom templates | [Custom_Generate_EN.md](../Assets/XLua/Doc/Custom_Generate_EN.md) |
| `XLUA_NO_UTIL` | Leaves out the embedded `xlua.util`; `require 'xlua.util'` then fails unless a loader or `Resources` provides it | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_no_util) |
| `XLUA_NO_PROFILER` | Leaves out the embedded `perf.profiler`, except in the Editor and in Development Builds (`UNITY_EDITOR`, `DEVELOPMENT_BUILD`) | [Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md) |
| `XLUA_ANDROID_SOURCE_PLUGIN` | Android imports the bridge from `__Internal` for a source-plugin build | [build/UNITY_SPIKE.md](../build/UNITY_SPIKE.md) |
| `XLUA_MEMORY_DIAGNOSTICS` | Compiles the Lua memory diagnostics (`LuaEnv.TakeMemorySnapshot()`, `LuaMemoryDiagnostics.Diff()`, the `xlua.memory` module) into a release player. The Editor and Development Builds (`UNITY_EDITOR`, `DEVELOPMENT_BUILD`) always have them; other release players carry none of their code | [Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md#memory-snapshots) |

## Native plugins

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
  exports only the functions `LuaDLL.cs` imports, which saves another 6 to 7 KB
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
- **`XLUA_HARDEN` reaches LuaJIT:** with `XLUA_HARDEN=ON` in the
  environment, the LuaJIT build scripts pass the hardening flags to
  LuaJIT's own Makefile, which turns off the stack protector and
  `_FORTIFY_SOURCE` by itself. An arm64 LuaJIT library is then marked for
  BTI. Before, `libluajit.a` stayed unhardened.
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
    that names them, and the Editor shows it as a warning. The injected code
    is unchanged.
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
- **What the asmdef layout costs for hotfix until milestone M7**, which
  generates the bridges in each injected assembly. For types in
  Assembly-CSharp (or any assembly other than xLua's):
  - `IntKey` falls back to field mode, with the injector's warning;
  - generic methods and types marked `Inline` are not injected, without a
    warning;
  - the other methods are injected through a delegate type created in the
    injected assembly, as for any second assembly before. The hotfix
    bridges they call are in the generated code, which compiles into
    Assembly-CSharp; for a game asmdef the injector takes them from there
    (the injected assembly then references Assembly-CSharp). Before
    2026-10-18 it looked only in xLua's assembly and the injected one, and
    such an asmdef failed with "can not find delegate".

  Projects that use hotfix should stay on the default layout until M7.
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

  Only the source generator reads these forms. The legacy generator
  ("Generate Code", `XLuaGenerate`), CodeEmit and the hotfix injector read
  the attributes on types, members and configuration lists as before, and
  ignore the new forms.
- Each attribute has a read-only `Type` property (null in the old forms);
  `BlackList` also has `TypeName`, `Member`, `ParameterTypes` and
  `ParameterTypeNames`, `DoNotGen` and `AdditionalProperties` have `Members`.

## Source generator (preview)

`General/SourceGenerator` holds the Roslyn source generator that will replace
"Generate Code" (`Tools/build_source_generator.py` builds it; the Unity
package does not ship it yet). It reads the configuration attributes,
reports diagnostics `XLSG001` to `XLSG011` with file and line, registers
each assembly with `XLua.Binding` and its name table, and writes class and
enum wrappers for the `[LuaCallCSharp]` types (milestone M2) and delegate and
interface bridges for the `[CSharpCallLua]` types (M3):

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
- **Not yet generated:** hotfix bridges (M7) and GCOptimize pushers (M4).
  Struct and enum values go through `ObjectTranslator`'s general push and get,
  which box.
- **Obsolete members** are left out, as the legacy generator does;
  `XLUA_JUST_EXCLUDE_ERROR` keeps those not marked as errors.
- **A `Func<MemberInfo, bool>` `[BlackList]` filter is not applied** (it cannot
  run in the compiler); list the members it matches as
  `[assembly: BlackList(typeof(T), "member", ...)]`. The migration tool will
  write them (M9).
- **Init-only setters** (`init`) are left out with info `XLSG004`: C# cannot
  call them outside an initializer. The legacy generator wrote code that did
  not compile for them.
- **Array types** in `LuaCallCSharp` (`typeof(int[])`) get no wrapper yet
  (info `XLSG004`) and go through reflection; the legacy generator wraps them.
- **Types a referenced assembly already wraps** are not wrapped again (info
  `XLSG008`): `[assembly: XLua.Binding.GeneratedBindings]` now also lists the
  wrapped types (a new three-argument constructor; the two-argument one
  stays).
- **Output:** one `internal static class W_<name>_<hash>` per type and one
  `internal sealed class B_<name>_<hash>` per interface bridge in
  `XLua.Generated.<assembly>_<hash>`, `XLua.Bindings.g.cs`, `XLua.Bridges.g.cs`
  with the delegate bridges and, in the Editor and outside Unity,
  `XLua.Manifest.g.cs` with the binding surface and the bridges as JSON.

In Unity it needs the
opt-in asmdef layout above: next to `XLua.asmdef` it runs only in the
assemblies that reference xLua. Configuration lists
(`[LuaCallCSharp] static List<Type> ...`) are not read by it (info
`XLSG011`); a migration tool will convert them.

## Planned changes (not released yet)

These are decided but not implemented; see [design](design/).

- **Presets:** `legacy` (today's behaviour), `minimal` and `core`; see
  [presets.md](design/presets.md).
- **Code generation:** source generation replaces the "Generate Code" menu
  before the first public release. A migration tool converts the
  configuration lists; see [source-generator.md](design/source-generator.md).
  The partial classes are already gone ([Generated code](#generated-code-xluagenerate-code)).
