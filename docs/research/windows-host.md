# Windows host checks: .NET Framework, Mono, MSVC API checks and SEH (2026-10-28)

ROADMAP.md listed three checks that need a Windows host, and the development
machine is a Mac:

- the managed suite on the .NET Framework, for
  [Tencent/xLua#1081](https://github.com/Tencent/xLua/issues/1081) and
  [#1179](https://github.com/Tencent/xLua/issues/1179);
- Lua's C API checks (`XLUA_APICHECK`) in MSVC builds;
- Lua errors raised through C# callbacks on LuaJIT for Win64, where zlua
  reports SEH crashes ([focus-creative-games.md](focus-creative-games.md)).

They now run in `.github/workflows/windows-host.yml` on GitHub's
`windows-2022` runners. This note records what the runs found.

Marks: [C] confirmed here, [S] secondary, [U] unconfirmed.

## Result

- **Every check passes** in run
  [40645581045](https://github.com/emindeniz99/xlua-ng/actions/runs/40645581045):
  five legs (Lua 5.3.6, 5.4.9, 5.5.1, LuaJIT GC64, LuaJIT without GC64), each
  with an `XLUA_APICHECK` build of `xlua.dll`. The managed suite passed on the
  .NET Framework and on Mono 6.12.0 for Windows, 496 tests in each of two
  modes, on every leg. They passed again after merging `main`, in run
  [40646848333](https://github.com/emindeniz99/xlua-ng/actions/runs/40646848333). [C]
- **#1081 is confirmed and fixed on the .NET Framework.** The ANSI code page of
  the runner is 1252. With the string imports of `LuaDLLBridge.cs` back in
  ANSI (the code before 2026-10-18), `CaseUtf8StringParameters` fails with
  `chunk name @??/??.lua; GetInPath ; SetInPath ;`. With the UTF-8 imports it
  passes. [C]
- **A second ANSI import was wrong, fixed:** `luaL_newmetatable`
  (`Assets/XLua/Core/LuaDLL.cs`). With it in ANSI, a type whose name is not
  ASCII gets no metatable under the name the bridge looks up, and the next
  `lua_rawset` on it trips Lua's API check (`lapi.c:755`, "table expected").
  Without the checks that is undefined behaviour in the native library. [C]
- **The LuaJIT build without GC64 did not work at all, fixed.** The first
  `LuaEnv` failed with a `NullReferenceException` in `ImportType` on the .NET
  Framework (run 40642149114). `xlua_gl` read `lua_State` with the GC64 layout
  while the library had the other one. [C]
- **No SEH problem on LuaJIT for Win64** in the paths xLua uses: errors raised
  from C# functions, C# exceptions, errors through eight nested
  Lua/C#/`LuaFunction.Call` levels, in coroutines and with a C# message
  handler, on the .NET Framework and Mono, GC64 and not. [C]
- **No C API check fired** with MSVC on any of the five builds, and a probe
  shows that each build has the checks. [C]
- **The `[CSharpCallLua]` scan differed between runtimes** (below). Owner
  decision of 2026-10-28: dynamic assemblies are left out on every runtime,
  and the scan stays cached. [C, run 40669037845]
- **`NATIVE_LUA_PUSHSTRING` marshalled ANSI** on the .NET Framework; it passes
  UTF-8 since 2026-10-28 (owner decision), checked with a control as the
  other imports were. [C, run 40669037845]

## How the workflow runs

Each leg (`windows-host.yml`, about 2 to 3 minutes):

1. Installs Mono 6.12.0.206, the last Windows release, from its MSI (SHA-256
   checked; the MSI is cached). About 50 s.
2. For LuaJIT, builds `lua51.lib` with `msvcbuild_mt.bat [nogc64] mixed` and
   `CL=/DLUA_USE_APICHECK` (cl reads extra options from `CL`), and checks
   that the library has `lj_assert_fail`.
3. Builds `xlua.dll` with CMake, `-A x64 -DXLUA_APICHECK=ON`, Release. The
   build log shows `D9025: overriding '/DNDEBUG' with '/UNDEBUG'`, so the
   asserts stay. [C]
4. `apicheck_probe.py`, then `check_exports.py`, `profiler_smoke.py` (Lua
   5.x) and `native_regression.py`.
5. `run_managed.py --runtime netfx` with Visual Studio 2022's Roslyn csc
   (4.14.0) on the image's .NET Framework (CLR 4), then `run_managed.py` with Mono's
   `mcs` on Mono. Only the first pass (reflection and CodeEmit) runs; the
   source generator passes need the .NET SDK and Mono's reference assemblies.
6. On 5.5.1, the .NET Framework run again with `--define NATIVE_LUA_PUSHSTRING`
   (since 2026-10-28), then `ansi_control.py` (the #1081 control below).

## The .NET Framework and string parameters

The .NET Framework marshals a `string` parameter of a P/Invoke import as
`LPStr` in the ANSI code page; Mono marshals UTF-8 on every platform, and so
do Unity's Mono and IL2CPP. [S: the .NET documentation on default
marshalling for strings; C: the control below]

`ansi_control.py` turns the UTF-8 imports of one file off at a time (`#if
XLUA_GENERAL` becomes a symbol nobody defines, the code before the fixes) and
runs the suite on the .NET Framework:

| File in ANSI | `CaseUtf8StringParameters` | `CaseUtf8TypeNames` |
|---|---|---|
| neither (the code now) | pass | pass |
| `Assets/XLua/Src/LuaDLLBridge.cs` | fails: `chunk name @??/??.lua; GetInPath ; SetInPath ;` | pass |
| `Assets/XLua/Core/LuaDLL.cs` | pass | the process aborts: `Assertion failed ... lapi.c, line 755` |

[C, run 40645581045, leg 5.5.1]

The second row is #1081: CJK characters are not in code page 1252, so chunk
names and table paths reach Lua as `?`. The same holds for global names,
`xlua_is_eq_str` and error messages (the same imports); the suite checks
chunk names, `GetInPath`, `SetInPath` and a C# exception's message.

The third row is the new finding. `Utils.MetatableKey` names a type's
metatable after the type (`Upstream.Utf8Names.甲`).
`luaL_newmetatable` registered it under the ANSI bytes
(`Upstream.Utf8Names.?`), while `luaL_getmetatable` pushes the name as UTF-8
(`xlua_pushasciistring`), so the bridge did not find the metatable it had just
made and used `nil` as a table. Two types whose names differ only outside the
code page would also have shared one metatable. The import now takes UTF-8
bytes under `XLUA_GENERAL`, as the others do (`fix(runtime): pass metatable
names to luaL_newmetatable as UTF-8 outside Unity`). Unity builds do not
change: their runtimes marshal UTF-8. [C]

#1179 (Chinese text garbled under `XLUA_GENERAL`) has the same cause on the
.NET Framework for the paths above; the suite's UTF-8 cases pass there now.
Its other suspect, the console, was not checked: under `XLUA_GENERAL`
`LuaEnv` keeps Lua's own `print` (`Assets/XLua/Src/LuaEnv.cs`, the `#if
!XLUA_GENERAL` around `StaticLuaCallbacks.Print`), which writes the UTF-8
bytes to stdout, and a Windows console shows them in its output code page
(OEM 437 on the runner) unless the program switches it to UTF-8. [U]

Not affected: `lua_tostring` decodes UTF-8 itself (`DecodeLuaString`), and
`lua_pushstring` passes UTF-8 bytes (`xlua_pushlstring`). [C: code read]

`NATIVE_LUA_PUSHSTRING` (off by default) imported `lua_pushstring` with a
`string`, which the .NET Framework marshals in ANSI; the same import pushes
member and metatable names (`xlua_pushasciistring`). Owner decision of
2026-10-28 (question 3 below): under `XLUA_GENERAL` it takes the UTF-8 bytes,
as the other imports do (`fix(runtime): pass NATIVE_LUA_PUSHSTRING strings as
UTF-8 outside Unity`). The 5.5.1 leg now runs the suite on the .NET Framework
with `--define NATIVE_LUA_PUSHSTRING` too, and `ansi_control.py` has a third
variant, the same define with only that import back in ANSI. Run
[40669037845](https://github.com/emindeniz99/xlua-ng/actions/runs/40669037845)
(ACP 1252) [C]:

| Variant | Result |
|---|---|
| `NATIVE_LUA_PUSHSTRING`, UTF-8 import | 508 tests pass in each of two modes |
| `NATIVE_LUA_PUSHSTRING`, ANSI import | 13 cases fail (`CaseUtf8StringParameters`, `CaseGetChineseString`, `CaseUtf8TextRoundTrip`, `CaseUtf8MemberNames` and others), then `CaseUtf8TypeNames` aborts in `lapi.c` line 755 |

The flag keeps one difference from the default path on every runtime:
`lua_pushstring` ends a C# string at its first NUL, so `CaseUtf8EmbeddedNul`
expects the shorter string with the define (`Defines.NativeLuaPushString()`).
[C: also on Mono 6.14.1, macOS arm64, 508 tests]

## LuaJIT without GC64

`build/make_win64_luajit.bat` builds LuaJIT with `msvcbuild_mt.bat nogc64`,
the mode of upstream xLua's Windows x64 plugin. `msvcbuild` defines
`LUAJIT_DISABLE_GC64` for LuaJIT's own files only. `xlua.c` included
`lj_obj.h` for `xlua_gl` (`G(L)`) and was compiled by CMake without the
define, so it saw the GC64 layout: `glref` as a 64-bit field. In the non-GC64
layout that is the 32-bit `glref` followed by `gclist`, so `xlua_gl` changed
whenever the collector touched `gclist`. `ObjectTranslatorPool` finds an
env's translator by `xlua_gl`, found none, and `ImportType` threw while the
first `LuaEnv` was being made. [C, run 40642149114, leg luajit-nogc64]

Upstream did not have this: its LuaJIT predated the GC64 default on x64, and
its CMake `GC64` option defined `LUAJIT_ENABLE_GC64` for both sides. The
x64 default changed with the LuaJIT upgrade, and xlua-ng later removed the
option as unused (`25da6ba8`). [S: build/README.md, "LuaJIT GC64 on x64"]

Fix (`fix(native): identify a LuaJIT state without lj_obj.h`): on LuaJIT,
`xlua_gl` returns the registry table's address (`lua_topointer(L,
LUA_REGISTRYINDEX)`), one per global state, and `xlua.c` no longer includes
`lj_obj.h`. Cost: about 2.5 ns per call against a release `libluajit.a`
(Apple M2 Pro, 200 million calls, three rounds 2.48 to 2.62 ns); the old
`G(L)` is one load. The C# side calls it once per C# function called from Lua
(`ObjectTranslatorPool.Find`). `native_regression.py check_global_state_key`
checks that the value stays the same across collections and threads. PUC Lua
keeps `G(L)`. [C]

The alternative without the cost: a CMake option that defines
`LUAJIT_DISABLE_GC64` for `xlua.c` and must match the library. It is not
taken, because a mismatch fails as above and nothing can detect it at build
time.

## Errors through C# callbacks (SEH)

On Windows x64 LuaJIT raises its errors with `RaiseException` and unwinds
with the system unwinder; every frame between the error and the catching
`pcall` must have unwind information. zlua reports crashes when that walk
crosses reverse P/Invoke frames. [S: focus-creative-games.md]

xLua keeps managed frames out of the walk: a C# function records an error
(`luaL_error`, `lua_error` and C# exceptions in wrappers go through
`xlua_csharp_str_error` and `xlua_csharp_error`) and returns; the C
trampoline (`csharp_function_wrap`, `build/xlua_core.c:537`) raises the error
after the managed frames are gone. A Lua error inside `LuaFunction.Call` is
caught by `lua_pcall` below the C# frame and comes back as a `LuaException`.
[C: code read]

`CaseErrorsThroughCSharpCallbacks` (`Upstream.CallbackErrors`) runs, in a
fresh `LuaEnv`, 300 rounds of `luaL_error` and `lua_error` (with a number as
the error value) from raw C# functions and of C# exceptions; 30 rounds of
errors at the bottom of eight nested Lua → C# → `LuaFunction.Call` levels,
raised from C# and from Lua; the same in a coroutine; an error that ends a
coroutine; `xpcall` with a C# message handler; an error that reaches
`DoString`'s own `pcall`; and then a plain `DoString`. It passed on LuaJIT
GC64 and without GC64, on the .NET Framework and Mono, and on Lua 5.3.6,
5.4.9 and 5.5.1. [C, run 40645581045]

Not covered: errors raised by a Lua API call made from C# while C# frames are
on the stack (a memory error in `lua_pushstring`, for example). Those are
undefined behaviour on every platform; [crash-inventory.md](crash-inventory.md)
covers the known ones. The test runs in the managed test program, not in a Unity player.

The first version of the test failed on LuaJIT everywhere: LuaJIT's
`require` leaves its loading mark in `package.loaded` when a loader raises, so
the next `require` of the same name fails with "loop or previous error
loading module". The case now clears `package.loaded` first. [C]

## API checks with MSVC

PUC Lua's checks are `assert()`, LuaJIT's call `abort()`. `xlua.dll` links
the C runtime statically (`/MT`), and that copy of the runtime does not know
that the host is a console program: a failed assert opens a message box,
which would hang an unattended run. `XLUA_APICHECK` with MSVC now adds
`build/xlua_apicheck_msvc.c`, a C runtime initializer (`.CRT$XCU`) that sets
`_set_error_mode(_OUT_TO_STDERR)` and clears abort's message and the Windows
Error Reporting call. [S: the UCRT's assert, for a DLL's static runtime;
C: the probe below prints to stderr and exits with 3]

`apicheck_probe.py` misuses the API once in a child process. Every build
aborted, with exit code 3:

| Build | Message |
|---|---|
| 5.3.6 | `Assertion failed: (idx <= ci->top - (ci->func + 1)) && "unacceptable index", file ...\lua-5.3.6\src\lapi.c, line 64` |
| 5.4.9 | `Assertion failed: (idx <= ci->top.p - (ci->func.p + 1)) && "unacceptable index", file ...\lua-5.4.9\src\lapi.c, line 64` |
| 5.5.1 | `Assertion failed: idx <= ci->top.p - (ci->func.p + 1), file ...\lua-5.5.1\src\lapi.c, line 62` |
| LuaJIT (both modes) | `LuaJIT ASSERT lj_api.c:825: lua_rawgeti: stack slot -1 is not a table` |

[C, runs 40641746628 and 40645581045]

The native checks and the managed suite then passed on all five builds on
both runtimes: no check fired. [C] [apicheck-runs.md](apicheck-runs.md) has
the macOS and Linux runs.

## The [CSharpCallLua] scan on the .NET Framework

Without generated code each `LuaEnv` scans the loaded types for
`[CSharpCallLua]`; the scan is kept while the loaded assemblies stay the same
(`ObjectTranslator.CSharpCallLuaScan`), except when it includes a dynamic
assembly, which can still gain types (`keep = false`,
`Assets/XLua/Src/ObjectTranslator.cs:364`). The scan leaves out assemblies
whose `ManifestModule` is a `ModuleBuilder`, meant for xLua's own
`XLuaCodeEmit`. [C: code read]

- **Mono** lists each `AssemblyBuilder` itself; its `ManifestModule` is a
  `ModuleBuilder`, so every dynamic assembly is left out, the cache stays on,
  and `[CSharpCallLua]` types in a dynamic assembly are never found.
- **The .NET Framework** lists the runtime assembly behind a builder, whose
  `ManifestModule` is not a `ModuleBuilder`, so the scan includes it and is
  not kept: once the program has made any dynamic assembly (the suite's
  `EnvScanCache.LaterAssembly` makes one), each later `LuaEnv` scans every
  loaded type again.

[C, run 40642149114: `CaseEnvScanReused` "scans for 3 envs: 3 != 0" and
`CaseEnvScanSeesLaterAssembly` "4 != 3"]

Both are consistent with the code; they differ in cost and in which types
are found. Until the decision below the cases expected the rescans when such
an assembly was loaded.

**Owner decision of 2026-10-28 (question 1, option b):** the scan skips
`Assembly.IsDynamic` on every runtime and stays cached
(`ObjectTranslator.CSharpCallLuaScan`, `Utils.GetAllTypes`, which the
blacklist and extension-method scans use, and `Binding.ScanAssemblies` for
`[GeneratedBindings]`). `[CSharpCallLua]` types in a dynamic assembly are not
found on the .NET Framework either (MIGRATING.md). The scan-cache cases
expect no rescans on every runtime again, and `CaseEnvScanReused` makes a
dynamic assembly with a `[CSharpCallLua]` interface: two more envs must not
scan, and the interface must not be in the scan. They pass on the .NET
Framework and Mono on every leg of run
[40669037845](https://github.com/emindeniz99/xlua-ng/actions/runs/40669037845),
and on Mono 6.14.1 (macOS arm64) in every binding mode. [C]

## Smaller findings

- `roslyn_fixtures.py` compared relative paths with `/` keys, and Windows
  paths have `\`; fixed with `as_posix()`. [C]
- The .NET Framework has no `Span<T>` without the System.Memory package, so
  `run_managed.py --runtime netfx` defines `XLUA_TEST_NO_SPAN`, which leaves
  the `Span` members of the `SpanOverloads` fixture out; the Lua cases expect
  them missing from Lua anyway. [C]
- Mono for Windows reports `6.12.0 (Visual Studio built mono)`. [C]
- `main`'s reproducible builds (`a457a6e1`) stopped every MSVC configure:
  `check_c_compiler_flag("/pathmap:C:\\a=b")` is evaluated again as CMake
  code, where `C:\a` is an invalid escape (run 40646541080). The check now
  uses `C:/a`. [C]
- The suite runs faster on Mono than on the .NET Framework here (Lua 5.5.1:
  about 9 s against 11 s per mode; 5.3.6: 1.3 s against 3 s). Not
  investigated. [C]

## Not verified

- Unity on Windows (Editor or player). Unity's runtimes marshal UTF-8, so the
  #1081 fixes do not change them. [S]
- .NET (Core) on Windows, which also marshals `LPStr` as ANSI. The UTF-8
  imports cover it the same way; not run. [U]
- The source generator passes on Windows, and the debug or `RelWithDebInfo`
  runtime. [U]
- Windows ARM64 and 32-bit x86. [U]
- The console code page part of #1179. [U]

## Questions for the owner

1. **The `[CSharpCallLua]` scan and dynamic assemblies.** Today Mono skips
   every dynamic assembly (cache on, their types never found) and the .NET
   Framework scans them (types found, the cache off for the rest of the
   process, a full type scan per `LuaEnv`). Options: (a) leave it; (b) skip
   every dynamic assembly on every runtime, as Mono already does: cheaper
   `LuaEnv` creation on the .NET Framework and .NET, and `[CSharpCallLua]` in
   a dynamic assembly is never found; (c) scan them on every runtime and keep
   the cache off: slower `LuaEnv` creation on Mono in the Editor once
   anything emits an assembly. Unity players use IL2CPP or Mono, so (b) is
   closest to what Unity users see.
   **Owner decision of 2026-10-28: (b)**, on every runtime, with the cache
   kept (above).
2. **`xlua_gl` on LuaJIT** costs about 2.5 ns more per C# call from Lua, for
   a build mode (non-GC64) that only `make_win64_luajit.bat` uses. The
   alternative is a CMake option that has to match the library. Keep the
   fix, or drop the non-GC64 script instead?
3. **`NATIVE_LUA_PUSHSTRING` with `XLUA_GENERAL`** marshals ANSI on the .NET
   Framework. Leave it, make it UTF-8 there, or let it apply to Unity only?
   **Owner decision of 2026-10-28:** UTF-8 there, as the other imports
   (above).

## Reproducing

```sh
gh workflow run windows-host.yml --ref <branch>   # once the file is on main
python Test/Native/run_managed.py --runtime netfx --compiler "<VS>\MSBuild\Current\Bin\Roslyn\csc.exe" --plugin xlua.dll --output out
python Test/Native/ansi_control.py --compiler "<csc.exe>" --plugin xlua.dll --output out-ansi
python Test/Native/apicheck_probe.py [--luajit] xlua.dll
```
