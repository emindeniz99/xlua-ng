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
| C# `in` and `ref readonly` parameters | Treated like `ref` | Passed as inputs. Members the editor's Roslyn cannot call are skipped | the `in`/`ref readonly` commits in the history |
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
| `xlua.get_generic_method(type, name, index)` | The index was read only when `lua_isinteger` was true: never on LuaJIT, and not for a float such as `1.0` on Lua 5.3+, so the first generic method of that name was used | Any number with an integer value selects the method, on every runtime | `fix(runtime): honour the method index of xlua.get_generic_method on LuaJIT` |

## Opt-in defines (no change unless defined)

| Define | Effect | Docs |
|---|---|---|
| `XLUA_NO_STREAMING_ASSETS_LOADER` | Removes the StreamingAssets `require` searcher (−570 KB on an Android APK that does not otherwise use UnityWebRequest) | [Lua_Loaders.md](../Assets/XLua/Doc/Lua_Loaders.md) |
| `XLUA_LOCK_METATABLES` | `getmetatable` on bridge objects returns `"locked"` and `setmetatable` fails | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md), [Security.md](../Assets/XLua/Doc/Security.md) |
| `XLUA_NO_SIGNATURE_LOADER` | `SignatureLoader` is not compiled, and with it the RSA/SHA-256 code it pulls in (−239 KB on an Android APK at Minimal stripping; nothing at Medium, which already strips it) | [signature.md](../Assets/XLua/Doc/signature.md) |
| `XLUA_NO_TEMPLATE` | Players do not open the `template` Lua library and do not compile `XLua.TemplateEngine`. The Editor (`UNITY_EDITOR`) and the command-line tools keep both, for code generation and custom templates | [Custom_Generate_EN.md](../Assets/XLua/Doc/Custom_Generate_EN.md) |
| `XLUA_NO_UTIL` | Leaves out the embedded `xlua.util`; `require 'xlua.util'` then fails unless a loader or `Resources` provides it | [XLua_API_EN.md](../Assets/XLua/Doc/XLua_API_EN.md#xlua_no_util) |
| `XLUA_NO_PROFILER` | Leaves out the embedded `perf.profiler`, except in the Editor and in Development Builds (`UNITY_EDITOR`, `DEVELOPMENT_BUILD`) | [Performance_Analysis.md](../Assets/XLua/Doc/Performance_Analysis.md) |
| `XLUA_ANDROID_SOURCE_PLUGIN` | Android imports the bridge from `__Internal` for a source-plugin build | [build/UNITY_SPIKE.md](../build/UNITY_SPIKE.md) |

## Native plugins

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
- **`XLUA_HARDEN` reaches LuaJIT:** with `XLUA_HARDEN=ON` in the
  environment, the LuaJIT build scripts pass the hardening flags to
  LuaJIT's own Makefile, which turns off the stack protector and
  `_FORTIFY_SOURCE` by itself. An arm64 LuaJIT library is then marked for
  BTI. Before, `libluajit.a` stayed unhardened.
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
    matching signature.
  - `GEN_CODE_MINIMIZE` wrappers are one static class per type,
    `XLua.CSObjectWrap.<Type>Wrap`, as in the default mode; their functions are
    private `ObjectTranslator` extension methods.
  - `DelegateBridge.Gen_Flag` is set by `BindingApi.Register`.
- **Hotfix:** the injector takes the bridges from
  `XLua.CSObjectWrap.DelegatesGensBridge` and calls them as static methods.
  The fields it adds still hold a `DelegateBridge`. IntKey and direct bridge
  calls still need xLua, the generated code and the hotfixed types in one
  assembly, as before.

## Planned changes (not released yet)

These are decided but not implemented; see [design](design/).

- **Presets:** `legacy` (today's behaviour), `minimal` and `core`; see
  [presets.md](design/presets.md).
- **Code generation:** source generation replaces the "Generate Code" menu
  before the first public release. A migration tool converts the
  configuration lists; see [source-generator.md](design/source-generator.md).
  The partial classes are already gone ([Generated code](#generated-code-xluagenerate-code)).
