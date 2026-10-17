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

  See [Hotfix_EN.md](../Assets/XLua/Doc/Hotfix_EN.md).
- **XLua/Validate Setup** checks the installation. It also lists the Lua
  files that earlier versions shipped in `Assets/XLua/Resources`
  (`xlua/util`, `perf/profiler`, `perf/memory`, `tdr/tdr`) when an in-place
  upgrade left them in any `Resources` folder, and says whether each one is
  unchanged from a shipped version; see
  [LuaCATS.md](../Assets/XLua/Doc/LuaCATS.md#validate-setup).
- **LuaCATS** definitions are generated for LuaLS; see
  [LuaCATS.md](../Assets/XLua/Doc/LuaCATS.md).

## Planned changes (not released yet)

These are decided but not implemented; see [design](design/).

- **Presets:** `legacy` (today's behaviour), `minimal` and `core`; see
  [presets.md](design/presets.md).
- **Android:** minimum API 23 in every preset.
- **Code generation:** source generation replaces the "Generate Code" menu
  and its partial classes before the first public release. A migration tool
  converts the configuration lists; see
  [source-generator.md](design/source-generator.md).
