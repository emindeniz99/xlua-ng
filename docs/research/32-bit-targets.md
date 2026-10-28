# 32-bit targets in 2026

Researched 2026-10-17. Confidence: **[C]** confirmed from a primary source,
**[S]** secondary, **[U]** unconfirmed or inferred.

**Owner rule (2026-10-17):** drop nothing. A target stays while even one
person could use it, unless keeping it costs real code complexity. CI and build
time alone is not a reason to drop it. [Per-target cost](#per-target-cost)
below applies the rule: no 32-bit target has a code cost, so every one stays.

## Summary

| Platform | State | xlua-ng today | Decision |
|---|---|---|---|
| Android armeabi-v7a | Google Play still serves 32-bit devices (Android Go, TV, old phones) but only alongside 64-bit; 64-bit-only devices since Pixel 7, and Arm's 2023+ cores are AArch64-only. No reliable share figure; estimated low single-digit percent and falling. **[C/S/U]** | built by default | Keep |
| Android x86 | Unity 2021.3, 2022.3 and 2023.2 offer "x86 (ChromeOS)"; Unity 6 (6000.0) dropped it **[C]**, see [below](#android-x86-in-unity) | built by default (`build/android_common.sh:18`) | Keep: the 2021.3 and 2022.3 LTS releases can still ship it |
| Android x86_64 | Removed in Unity 6.5 (Magic Leap 2 only) **[C]**; 6000.0 and 6000.3 list it as "x86-64 (Magic Leap 2)", 2021.3–2023.2 as "x86-64 (ChromeOS and Magic Leap 2)" **[C]** | built by default | Keep |
| iOS | 64-bit only since iOS 11; Unity 6 needs A8+ **[C]** | 64-bit only | Nothing to do |
| Windows x86 | Steam survey shows no 32-bit Windows row (under 0.1%); Windows 11 is 64-bit only; Unity 6.6 still offers "Intel 32-bit" **[C]** | `make_win32_*`, 32-bit half of `make_win_lua5*` | Keep (Lua 5.x and LuaJIT) |
| Windows ARM64 | Unity offers "ARM 64-bit" from 2023.1; 2022.3 has only Intel 32/64-bit **[C]** | `make_win_arm64_*.bat`, CI on `windows-11-arm` (added 2026-10-17) | Added; built and native checks pass on `windows-11-arm` (2026-10-17); not loaded in Unity yet |
| Linux x86 | No Unity Linux x86 player since 2019.2 **[S]**; Unity 6 is x64 only **[C]** | `make_linux32_*` (no CI, no plugin copy step) | Keep |
| OpenHarmony armeabi-v7a | Tuanjie manuals still list ARMv7, but DevEco reportedly no longer compiles armeabi-v7a and HarmonyOS NEXT phones are arm64 **[C/S]** | built | Keep |
| WebGL | wasm32 default; Unity 6.6 adds WebAssembly64 for heaps above 4 GB **[C]** | compiled in the player | Keep wasm32; wasm64 audited, see [below](#webassembly64-audit) |
| Consoles | All 64-bit | `make_nx64_*` | Nothing to do |

## Android x86 in Unity

The Target Architectures list of the Android Player Settings page, per Unity
version **[C]**:

| Unity | 32-bit x86 | Wording |
|---|---|---|
| 2021.3 | yes | "32-bit ARM, 64-bit ARM, 32-bit x86, and 64-bit x86–64" |
| 2022.3 | yes | ARMv7, ARM64, **x86 (ChromeOS)**, x86–64 (ChromeOS and Magic Leap 2) |
| 2023.2 | yes | same as 2022.3 |
| 6000.0 | no | ARMv7, ARM64, x86–64 (Magic Leap 2) |
| 6000.3 | no | ARMv7, ARM64, x86–64 (Magic Leap 2), "limited to existing projects only" |

So the two older LTS releases that xlua-ng still supports (its WebGL and C17
notes start at 2021.3) can build x86 players for ChromeOS. The default ABIs in
`build/android_common.sh:18` stay `armeabi-v7a arm64-v8a x86 x86_64`; a
project can still narrow them with `ANDROID_ABIS`.

## Windows ARM64

Unity's Windows build settings list "ARM 64-bit" from 2023.1; the 2022.3 page
lists only "Intel 64-bit" and "Intel 32-bit" **[C]**. The plugin goes to
`Assets/Plugins/arm64/xlua.dll`: Unity staff list `Plugins/arm64` as the
folder name its import heuristic maps to ARM64, next to `x86_64`/`x64` for
x64 **[S]** (Unity discussions, "The Plugin Inspector is a mess", 2024). The
documented folder table (Plug-in Inspector, 6000.3) only names `x86`,
`x86_64` and `x64` **[C]**, so the committed `.meta` sets the platform
explicitly: Standalone Win64 with CPU ARM64, and the Windows Editor on ARM64.
The same folder already holds the macOS Apple silicon `libxlua.dylib`; the
names differ, so Unity does not report a duplicate. `Plugins/x86_64/xlua.dll`
was enabled for Win64 with CPU "AnyCPU", which on 2023.1+ also matches an
ARM64 player; it is now CPU x86_64.

LuaJIT supports Windows ARM64: `luajit-2.1/doc/install.html` (Windows,
"Building with MSVC") names an ARM64 developer prompt and the x64 to ARM64
cross-build (`vcvarsall.bat x64_arm64`), and `src/msvcbuild_mt.bat` selects
`vm_arm64.dasc` when `VSCMD_ARG_TGT_ARCH` is `arm64` **[C]**. ARM64 LuaJIT is
always GC64 (`lj_arch.h:303`).

The `windows-11-arm` runner is a standard GitHub-hosted runner for private
repositories too (GitHub "Supported runners and hardware resources") **[S]**.
`native-build.yml` builds Lua 5.3.6, 5.4.9 and 5.5.1 there and runs
`check_exports.py`, `profiler_smoke.py` and `native_regression.py` (and the
first and last on LuaJIT) when the runner and its Python are ARM64. These
checks had never run on Windows before, for any architecture. The first run
(2026-10-17, run 39055937800) failed twice: the scripts built an invalid
generator name from vswhere for Visual Studio 2026 ("Visual Studio 18 18"),
and `native_regression.py` looked for debug helpers that a Windows DLL does
not export. With both fixed, all three Lua versions and LuaJIT build and pass
(run 39059626940, Visual Studio 18 with MSVC 14.51 on an ARM64 host) **[C]**.

### Import settings in the editors (checked 2026-10-17)

Question: with `arm64/xlua.dll` added and `x86_64/xlua.dll` changed from
`AnyCPU` to `x86_64`, does a Windows x64 player still get the x64 DLL in
2021.3 and 2022.3, which have no Windows ARM64 player? Checked on macOS
ARM64 editors 2021.3.45f2, 2022.3.62f3, 6000.3.26f1 and 6000.6.6f1, at
`f515466`. None of them has the Windows Build Support module
(`BuildPipeline.IsBuildTargetSupported(Standalone, StandaloneWindows64)` is
false), so no Windows player was built.

An editor script imported the committed `.meta` files of
`x86/xlua.dll`, `x86_64/xlua.dll`, `arm64/xlua.dll` and
`arm64/libxlua.dylib` (an x86-64 PE stood in for each DLL; the importer reads
the `.meta`, not the machine type). None of the `.meta` files was rewritten.
For the Win64 target, in all four editors **[C]**:

| Plugin | `GetCompatibleWithPlatform` | `GetPlatformData(…, "CPU")` | `DesktopPluginImporterExtension.CalculateFinalPluginPath("Win64")` (internal) |
|---|---|---|---|
| `x86_64/xlua.dll` | true | `x86_64` | `x86_64/xlua.dll` |
| `arm64/xlua.dll` | true | `ARM64` | 2021.3, 2022.3: empty (not in the player); 6000.3, 6000.6: `ARM64/xlua.dll` |
| `x86/xlua.dll` | false (Win: true, `AnyCPU`, `x86/xlua.dll`) | `None` | empty |

`PluginImporter.GetImporters(StandaloneWindows64)` returns both Win64 DLLs in
every editor, because without the Windows module it applies no platform
extension and returns every compatible importer (`PluginImporter.GetImporters(string)`
returns `allImporters` when `ModuleManager.GetPluginImporterExtension` is
null). The build-time collision check (`PluginsHelper.CheckFileCollisions`,
internal) reports no collision for Win64 or Win **[C]**.

The editor's own code explains the table. In `UnityEditor.CoreModule.dll`
(disassembled with monodis), `DesktopPluginImporterExtension.CalculateFinalPluginPath`
for a Windows target returns an empty path for CPU `None`; 2021.3.45f2 and
2022.3.62f3 also return an empty path for CPU `ARM64` and put every other
value, `AnyCPU` and `x86_64` included, under `x86_64/` for Win64 (`x86/` for
Win). 6000.3.26f1 and 6000.6.6f1 put CPU `ARM64` under `ARM64/` and the rest
under `x86_64/` **[C]**. So on 2021.3 and 2022.3 the change from `AnyCPU` to
`x86_64` gives the same destination as before, and the ARM64 DLL is left out
of Win64 players. Which of the two DLLs an x64 or ARM64 player of 6000.x
copies is decided by the Windows module's build code, which is not installed
here; that part, and the Windows editors' choice between the two DLLs (Editor
CPU `x86_64` or `ARM64`, OS Windows), is **[U]**. The macOS editors logged no
"Multiple plugins with the same name" error with both DLLs present.

Upstream's `x86_64/xlua.dll.meta` also enables Linux64 (CPU `AnyCPU` in 2021.3
and 2022.3, `x86_64` in 6000.x); the final path is empty because the Linux
extension takes only `.so` files, so this upstream setting copies nothing.

## Per-target cost

Searched 2026-10-17 in `build/xlua.c`, `build/i64lib.c`,
`build/memory_leak_checker.c`, `build/CMakeLists.txt`, the build scripts and
`Assets/XLua/Src`. CI minutes are from the 2026-10-17 runs of `build.yml`
(run 39038658279) and `native-build.yml` (run 39035823674).

**Code cost, all targets:** none. The three bridge C files have no `#if` on
pointer size, word size or a 32-bit architecture; the only platform branches
are `i64lib.c:16-32` and `i64lib.c:510`, which pick printf formats for MSVC on
every Windows architecture. `Assets/XLua/Src` has no `IntPtr.Size`,
`UNITY_64` or architecture branch. The P/Invoke signatures are the same for
every architecture (checked by `Test/Native/check_pinvoke_abi.py`). One fix
was found on a 32-bit target and made for all: struct float and decimal
fields are copied with `memcpy` (`aa42eb5`), because misaligned VFP/LDRD
access can raise SIGBUS on armeabi-v7a.

| Target | Build cost | CI cost | Known target-specific bugs | Who can use it | Decision |
|---|---|---|---|---|---|
| Android armeabi-v7a | `CMakeLists.txt:160-166`: 32-bit Android below API 24 keeps `_FILE_OFFSET_BITS=32` (shared with x86). LuaJIT needs a 32-bit host compiler (`gcc -m32`, `gcc-multilib`; `android_common.sh:61`) | Part of the Android builds: about 0.5 of the 1.1 min `build.yml` Android build, a quarter of each 0.5 min `native-build.yml` Android leg, and `apt-get install gcc-multilib` in `android_luajit`. `check_android_elf.py` checks it; nothing executes it in CI | `fseeko64`/`ftello64` missing below API 24 (`4393399`); misaligned struct float/decimal access, SIGBUS risk (`aa42eb5`, fixed for all targets) | Every Unity version builds ARMv7; Play still serves 32-bit-only devices | Keep |
| Android x86 | Same `_FILE_OFFSET_BITS` rule and the same `gcc -m32` need for LuaJIT (`android_common.sh:63`) | Same as armeabi-v7a (about a quarter of each Android build) | `fseeko64` below API 24 (`4393399`) | Unity 2021.3–2023.2 "x86 (ChromeOS)" players; not Unity 6 | Keep |
| Windows x86, Lua 5.x | `make_win32_lua53.bat`; the second half of `make_win_lua54.bat` and `make_win_lua55.bat` (`-A Win32`) | Three CMake builds inside the 1.6 min `build.yml` `windows` job (Windows minutes bill at twice the Linux rate). Not run or loaded in CI | None found in the history | Unity 6.6 still builds "Intel 32-bit" Windows players | Keep |
| Windows x86, LuaJIT | `make_win32_luajit.bat` with an x86 developer prompt (`msvc-dev-cmd` `arch: x86`) | Own job `windows-luajit-32`, 1.0 min, in `build.yml` and `publish.yml`. Not run in CI | None found | Same Unity players as above | Keep |
| Linux 32-bit | `make_linux32_lua53.sh`, `make_linux32_luajit.sh` (`-m32`, multilib). Both stop after the build: no `plugin_*` copy, and no Lua 5.4 or 5.5 script | None: no workflow builds them, so they may not build | None recorded | No Unity Linux x86 player since 2019.2 **[S]**. Outside Unity, the `General/` tools are .NET Framework 3.5/4.0 projects (`General/vs2013/*.csproj`) that could run under a 32-bit Mono on Linux; no such user is known **[U]** | Keep (zero code cost) |
| OpenHarmony armeabi-v7a | One `build armeabi-v7a` line in each `make_ohos_lua5*.sh` | About a third of the 0.8 min `build.yml` OHOS build. Cannot be tested: a static OpenHarmony hello world stops with SIGILL under `qemu-arm`, so `ohos_check.sh` runs only `x86_64` and `arm64-v8a` in CI (`build.yml:86-91`, `Test/README.md`) | None recorded | Tuanjie manuals list ARMv7 **[C]**; DevEco reportedly no longer compiles it **[S]** | Keep |
| Lua 5.3 compatible bytecode (`LUAC_COMPATIBLE_FORMAT`) | 27 lines under `#if LUAC_COMPATIBLE_FORMAT` in the vendored `lundump.c` (11), `ldump.c` (11) and `lundump.h` (5); the CMake option and its 5.3-only check (`CMakeLists.txt:36`, `:52-53`, `:229-230`); `build/luac` builds the matching compiler | One leg of the Lua 5.3.6 test suite in `analysis.yml` ("Lua 5.3.6, LUAC_COMPATIBLE_FORMAT"); `lua_tests.py:110-115` adapts `calls.lua` to the format byte 1 | None recorded; the format drops the `size_t` header check by design (`LUA_PATCHES.md`) | A Lua 5.3 project that ships one precompiled bytecode set to 32-bit and 64-bit players (`Assets/XLua/Doc/compatible_bytecode.md`) | Keep |

Nothing above reaches the "discuss with owner" bar: every cost is a build
script line, a CMake rule or CI minutes.

Update 2026-10-28: the "Not run in CI" cells for Windows x86 were wrong in
part: `windows-luajit-32` ran, but `make_win32_luajit.bat` asked for Visual
Studio 2019, which `windows-2022` lacks, so it built nothing and the job
passed anyway. The script now opens its own x86 developer prompt
(`msvc-dev-cmd` is gone) and stops at a failure, and `build.yml` loads all
four x86 DLLs with a 32-bit Python (`check_exports.py`). Run
[40671930012](https://github.com/emindeniz99/xlua-ng/actions/runs/40671930012)
built and checked them. [C]

## WebAssembly64 audit

Unity 6.6 builds WebAssembly64 (Memory64) when Maximum Memory Size is above
4096 MB, and turns on WebAssembly 2023 with it (Manual: "Enable
WebAssembly64"; scripting API `PlayerSettings.WebGL.maximumMemorySize`)
**[C]**. Chrome and Edge 128+, Firefox 129+ on desktop; Safari 26.5 has no
64-bit WebAssembly **[C]**. Pointers, `size_t` and `long` become 64-bit;
`int` stays 32-bit, `lua_Integer` is `long long` either way.

What was checked:

- **`xlua.c`, `i64lib.c`, `memory_leak_checker.c`:** no pointer is stored in
  an `int` or `lua_Integer`. Object handles are `int` keys inside full
  userdata (`xlua.c:458`); light userdata carry real pointers (`&tag`,
  `&hook_index`, and on LuaJIT the helper function pointers,
  `xlua.c:297`). Sizes from Lua are `size_t` (`xlua.c:326`, `:1208`). The
  `int` and `unsigned int` sizes that cross into C# (`xlua_pushlstring`,
  `xluaL_loadbuffer`, struct offsets and lengths) are `int` on both sides.
- **`CSharpStruct`** (`xlua.c:929-933`: `int fake_id`, `unsigned int len`,
  `char data[1]`): the data offset is 8 on every target, and every access goes
  through `memcpy` with a bounds check against `len`, so neither the layout
  nor alignment changes with 64-bit pointers.
- **Lua configuration:** the amalgamation uses the stock 5.5.1 `luaconf.h`
  (64-bit `lua_Integer`, `double`), which Lua supports on LP64.
- **`LuaDLL.cs` and `DelegateBridge.cs`:** every import that carries a pointer
  or a size uses `IntPtr`, `out IntPtr`, `string` or `byte[]` where C has a
  pointer or `size_t` (`lua_tolstring`'s length is `out IntPtr`).
  `Test/Native/check_pinvoke_abi.py` now checks all 119 imports against the C
  prototypes in pointer/32/64-bit/float/double classes.

Found:

1. **`lua_pushstring` with `NATIVE_LUA_PUSHSTRING`** (fixed): the import was
   `void`, the Lua 5.2+ function returns `const char *`. Native ABIs ignore the
   result; WebAssembly links the IL2CPP declaration to the C definition by
   signature, so the call would trap on wasm32 too, not only on wasm64. The
   import now declares the `IntPtr` result. Only builds that define
   `NATIVE_LUA_PUSHSTRING` were affected.
2. **`LuaBase.GetHashCode`** (fixed): `IntPtr.ToInt32()` on the table or
   function address. Mono and IL2CPP truncate (checked with Mono 6.14.1: a
   pointer above 2^31 gave a negative value), but .NET Framework on a 64-bit
   process throws `OverflowException`, which the `General/` tools can hit. It
   now uses `IntPtr.GetHashCode()`, which gives the same value on Mono.

The rest of the bridge already runs on LP64 targets (arm64 and x86_64
everywhere), so wasm64 adds no new pointer-size case beyond what the
signature check covers.

## Sources

- Google Play 64-bit requirement: <https://developer.android.com/google/play/requirements/64-bit>
- 64-bit-only devices (Pixel 7): <https://android-developers.googleblog.com/2022/10/64-bit-only-devices.html>
- Google TV / Wear OS 64-bit (2026), "no changes to 32-bit support":
  <https://android-developers.googleblog.com/2025/08/64-bit-app-compatibility-for-google-tv-android-tv.html>,
  <https://android-developers.googleblog.com/2026/04/get-your-wear-os-apps-ready-for-64-bit-requirement.html>
- Unity Android Player Settings, Target Architectures:
  <https://docs.unity3d.com/2021.3/Documentation/Manual/class-PlayerSettingsAndroid.html>,
  <https://docs.unity3d.com/2022.3/Documentation/Manual/class-PlayerSettingsAndroid.html>,
  <https://docs.unity3d.com/2023.2/Documentation/Manual/class-PlayerSettingsAndroid.html>,
  <https://docs.unity3d.com/6000.0/Documentation/Manual/class-PlayerSettingsAndroid.html>,
  <https://docs.unity3d.com/6000.3/Documentation/Manual/class-PlayerSettingsAndroid.html>
- Unity 6 system requirements: <https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html>
- Unity 6.5 breaking changes (x86_64 Android removed, minimum API 26):
  <https://discussions.unity.com/t/planned-breaking-changes-in-unity-6-5-updated-2026-03-27/1694205>
- Unity ChromeOS and x86 Android: <https://discussions.unity.com/t/the-future-of-chromebook-and-chromeos-runtime-support/943516>
- Steam Hardware Survey: <https://store.steampowered.com/hwsurvey/>
- Unity Windows architectures:
  <https://docs.unity3d.com/2022.3/Documentation/Manual/WindowsStandaloneBinaries.html>,
  <https://docs.unity3d.com/2023.1/Documentation/Manual/WindowsStandaloneBinaries.html>,
  <https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/windows/standalone-binaries>
- Unity plug-in folder defaults: <https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/plug-ins/plug-in-inspector>;
  staff reply on `Plugins/arm64`: <https://discussions.unity.com/t/the-plugin-inspector-is-a-mess/943961>
- GitHub-hosted runners (`windows-11-arm`): <https://docs.github.com/en/actions/reference/runners/github-hosted-runners>
- Tuanjie system requirements: <https://docs.unity.cn/cn/tuanjiemanual/Manual/system-requirements.html>
- Unity WebAssembly64: <https://docs.unity3d.com/Manual/wasm-64bit-support.html>; "Enable
  WebAssembly64" and "Prerequisites for WebAssembly64" in the 6000.6.4f1 offline manual
  (`Documentation/en/Manual/wasm-64bit-enable.html`, `wasm-64bit-prereq.html`)

## Open points

- The Android 32-bit share needs real customer data (Play Console, installs by ABI).
- The default ABIs of a new Unity 6 Android project were not confirmed.
- Whether Tuanjie really builds armeabi-v7a for HarmonyOS NEXT phones is
  unconfirmed beyond its manual tables.
- The Windows ARM64 plugins have not run in a Unity player or Editor.
