# Unity mobile smoke test

This project checks Lua 5.3, 5.4, or 5.5 loading, managed GC setters and getters,
coroutines, full GC, and LuaEnv disposal in Unity. Lua 5.4 and 5.5 also check
to-be-closed values. It also checks:

- the integer rule for C# integer parameters (docs/MIGRATING.md): `3.0` and
  `"12"` are accepted, `2.75` raises "number has no integer representation";
- Lua functions cast to C# delegates, from `LuaTable.Get` and as a method
  argument. Without generated code a `Func<string, string>` uses
  `DelegateBridge`'s generic `Func` methods and a `Func<int, int>` raises
  "This type must add to CSharpCallLua". With `--gen`, both are static methods
  of `XLua.CSObjectWrap.DelegatesGensBridge` bound as delegates closed over
  the `DelegateBridge`;
- the C# feature defines: the `template` library, the embedded `xlua.util`
  and `perf.profiler`, and `SignatureLoader` are present unless
  `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL`, `XLUA_NO_PROFILER` and
  `XLUA_NO_SIGNATURE_LOADER` remove them. A player keeps `perf.profiler` in a
  Development Build and the Editor keeps `template` and `perf.profiler`, as
  documented.

The pass line lists what was found, for example `delegates generated,
template off, xlua.util off, perf.profiler off, SignatureLoader off`.
It is a smoke test, not the complete unit suite or a support matrix.

Run `prepare.py --help` for the required rebuilt plugin paths. Use an empty
output directory on an external disk. The script copies the runtime sources
and platform metadata into a separate project; it never changes an existing
project or uses the upstream prebuilt binaries. The default editor version is
6000.3.21f1. Pass `--lua-version 5.3` when supplying Lua 5.3.6 plugins;
pass `--lua-version 5.4` for Lua 5.4.9 plugins. The default is 5.5. Keep prepared projects outside this repository.
Pass only the plugins for the targets you build. `--webgl` copies
`Assets/Plugins/WebGL/xlua_webgl.cpp` and the Lua 5.5.1 and bridge sources it
includes into a `build/` directory next to `Assets`; Unity compiles them with
Emscripten, so WebGL needs no prebuilt plugin and supports Lua 5.5 only.
`--gen` also copies the xLua Editor code (`Assets/XLua/Src/Editor`) and
`SmokeGenConfig.cs` (`SmokeApi`, `Func<int, int>` and `Func<string, string>`)
and defines `XLUA_SMOKE_GEN`. Run `-executeMethod
CSObjectWrapEditor.Generator.GenAll` once before the build; code generation
runs Lua in the Editor, so `--gen` needs `--macos-plugin`.
`--source-gen BUILD_DIR` binds through the source generator instead: xLua
goes to `Assets/XLua/Src` with `XLua.asmdef` and the Editor code with
`XLua.Editor.asmdef` (the opt-in asmdef layout), the generator is built into
`BUILD_DIR` and installed in `Assets/XLua/Src/Analyzers`
(`Tools/build_source_generator.py`), and `SgSmoke.cs` goes into
Assembly-CSharp with `XLUA_SMOKE_SOURCE_GEN` defined. Nothing is generated
before the build. The pass line then adds `Func<int, int> by signature` and
`source generator (...)`: the generated `Bindings` and `W_SgApi` exist, Lua
does not see the blacklisted `SgApi.Hidden` (so the call went through the
wrapper), an `[assembly: DoNotGen]` method, an init-only setter and a
`[ReflectionUse]` type work through reflection (the generated link.xml
keeps them under High stripping), a `[GCOptimize]` struct goes in and out,
the `[CSharpCallLua]` delegate `SgTwice` is a static `__Gen_Delegate_Imp`
method of the generated `Bridges` class in Assembly-CSharp closed over its
`DelegateBridge`, and `Func<int, int>`, which nothing configures, gets that
bridge by signature. A development player also logs the init-only setter's
fallback warning once. Set `XLUA_SMOKE_STRIPPING=High` for the stripping
check.
To check the feature defines, set `XLUA_SMOKE_DEFINES` (below) to them, for
example `XLUA_NO_SIGNATURE_LOADER;XLUA_NO_TEMPLATE;XLUA_NO_UTIL;XLUA_NO_PROFILER`,
and `XLUA_SMOKE_RELEASE=1` for a build without the Development flag.

Run Unity in batch mode with the prepared project path and one of these
`-executeMethod` values:

- `SmokeBuild.CheckEditor`: run the checks inside the Editor.
- `SmokeBuild.BuildIos`: export an ARM64 IL2CPP iOS project targeting iOS 15.
- `SmokeBuild.BuildAndroid`: build an ARM64 IL2CPP APK targeting Android API 25 (API 26 on Unity 6.6 and later, which supports nothing lower).
- `SmokeBuild.BuildWebGL`: build an uncompressed WebGL player into
  `unity-webgl-smoke`, which any static file server can host.

`size_report.py` reuses `SmokeBuild.cs` for release size builds and sets
`XLUA_SMOKE_RELEASE`, `XLUA_SMOKE_STRIPPING`, `XLUA_SMOKE_ANDROID_ARCHS`,
`XLUA_SMOKE_AAB`, `XLUA_SMOKE_WEBGL_COMPRESSION` and `XLUA_SMOKE_DEFINES`
(the Player Settings scripting defines); unset, the smoke builds
above are unchanged (see "Player size" in `Test/README.md`).
`XLUA_SMOKE_WEBGL_MAX_MEMORY` sets the WebGL Maximum Memory Size in MB (Unity
2022.1+); above 4096, Unity 6.6 builds WebAssembly64 (Memory64).

Set `XLUA_SMOKE_OUTPUT_ROOT` to the external build output directory. If unset,
the harness uses a `builds` directory alongside the prepared project. For
Android builds, set an isolated `GRADLE_USER_HOME` on the external disk.
Use `-buildTarget iOS`, `-buildTarget Android` or `-buildTarget WebGL` when
launching the relevant player build. iOS export still requires an Xcode
build, development signing, installation, and launch before it counts as a
device test.

The player logs `XLUA_NG_SMOKE_PASS` or `XLUA_NG_SMOKE_FAIL` and writes
`xlua-smoke-result.txt` under `Application.persistentDataPath`. A successful
export or APK build alone does not establish that these runtime checks passed.

The iOS 15 setting belongs to this modern Unity smoke project. It does not
verify or define a legacy iOS support promise for the standalone library.

## Android emulator with 16 KB pages

Use a Google "16 KB Page Size" arm64-v8a system image (for example
`system-images;android-36;google_apis_ps16k;arm64-v8a`) and boot it headless
with `-no-window -no-audio -no-boot-anim -no-snapshot`. `adb shell getconf
PAGE_SIZE` must print 16384. Before launching the APK, run
`adb shell settings put secure immersive_mode_confirmations confirmed`:
otherwise the "Viewing full screen" tip takes focus and the Unity player
stops before the scene starts, so the smoke never logs a result. Launch with
`am start -n org.xluang.smoke/com.unity3d.player.UnityPlayerGameActivity`;
`monkey` exits on emulators without hardware keys. Pass `-s` to every `adb`
command when another device is connected. If the emulator's
`/sdcard/Android/data/org.xluang.smoke` survives from an earlier install, it
belongs to the old app uid: IL2CPP logs "il2cpp failed to extract resources",
the scene never starts, and the old `xlua-smoke-result.txt` is still there.
Remove that directory with `adb shell rm -rf` before installing.

## WebGL in a browser

Serve `unity-webgl-smoke` from any static server on localhost and open
`index.html`. The result appears in the browser console. Headless Chrome
prints it to stderr with `--headless=new --enable-logging=stderr --v=0
--use-angle=swiftshader --enable-unsafe-swiftshader`.

`--webgl` also writes an `Assets/link.xml` that keeps
`UnityEngine.ContentLoadModule`. Without it, Unity 6000.6.4f1 strips
`ContentLoadingSystem.ProcessResults` from this small project and the player
halts with `RuntimeError: null function` before `XluaSmoke.Start` runs.

## Recorded validation

Using Unity 6000.3.21f1:

| Runtime | macOS ARM64 Editor | iPhone 14 Pro IL2CPP | Android ARM64 IL2CPP |
| --- | --- | --- | --- |
| Lua 5.3.6 | Passed | Passed | Not run |
| Lua 5.4.9 | Passed | Passed | Passed |
| Lua 5.5.1 | Passed | Passed | Passed |

Lua 5.5.1 also passed on 2026-10-16 in two more players, built from
rebuilt sources. An Android ARM64 IL2CPP APK (Unity 6000.3.21f1, plugin
built with NDK r30) ran on an Android 16 (API 36) emulator with 16 KB pages.
A Unity 6000.6.4f1 WebGL player ran in headless Chrome 155. Unity compiled
`xlua_webgl.cpp` with its Emscripten 4.0.20.

### 2026-10-18: main at 227ed4c

The smoke test with the integer rule, delegate and feature define checks
above, Lua 5.5.1. The Android plugin was built from this checkout with
`build/make_android_lua55.sh` (NDK r30, arm64-v8a, the release build: API 23,
`XLUA_SIZE_FLAGS` with packed relocations); `check_android_elf.py` passed
(PT_LOAD alignment 0x4000, 283 exports). The macOS arm64 Editor plugin for
code generation was built with CMake (`BUILD_SILICON`, Release). Android runs
used the `xlua16k` AVD (Android 16, API 36, 16 KB pages) headless, Unity's
Minimal managed stripping, ARM64 IL2CPP. "Four defines" is
`XLUA_NO_SIGNATURE_LOADER;XLUA_NO_TEMPLATE;XLUA_NO_UTIL;XLUA_NO_PROFILER` in
`XLUA_SMOKE_DEFINES`. Every player printed `XLUA_NG_SMOKE_PASS`:

| Unity | Player | Variant | Delegates | template, xlua.util, perf.profiler, SignatureLoader |
| --- | --- | --- | --- | --- |
| 6000.3.21f1 | macOS Editor | default | generic | on, on, on, on |
| 6000.3.21f1 | macOS Editor | `--gen` | generated | on, on, on, on |
| 6000.3.21f1 | Android, Development | default | generic | on, on, on, on |
| 6000.3.21f1 | Android, Development | `--gen` | generated | on, on, on, on |
| 6000.3.21f1 | Android, release | four defines | generic | off, off, off, off |
| 6000.3.21f1 | Android, Development | four defines | generic | off, off, on, off |
| 2021.3.45f2 | Android, Development | default | generic | on, on, on, on |
| 2021.3.45f2 | Android, Development | `--gen` | generated | on, on, on, on |
| 2021.3.45f2 | Android, release | four defines | generic | off, off, off, off |
| 6000.6.4f1 | WebGL, Development, Chrome 155 headless | default | generic | on, on, on, on |
| 6000.6.4f1 | WebGL, Development, Chrome 155 headless | `--gen` | generated | on, on, on, on |
| 6000.6.4f1 | WebGL, release, Chrome 155 headless | four defines | generic | off, off, off, off |

"Generated" means both delegates were static `__Gen_Delegate_Imp` methods of
`XLua.CSObjectWrap.DelegatesGensBridge` with a `DelegateBridge` target, so
IL2CPP runs the source generator M0's closed static delegates, on ARM64 and
in WebAssembly. With `--gen` the integer rule went through the generated
`SmokeApiWrap`, otherwise through the reflection wrapper.

Unity 6000.3.26f1 and 6000.6.6f1, the editors the Unity editor suite used
that day, have no Android or WebGL module installed, so their players were
not built. Unity 2021.3.45f2's own libraries (`libunity.so`, `libil2cpp.so`,
`libmain.so`) have 4 KB PT_LOAD alignment: Android 16 shows "This app isn't
16 KB compatible" and runs the app in page size compatibility mode, and the
player waits behind that dialog until its OK button is pressed. Unity 6000.3
aligns every library to 16 KB. Unity 2021.3 also packs `libxlua.so`
unstripped (1,985,360 bytes, 640,441 compressed in the release APK), where
6000.3 strips it to 372,560 (159,185 compressed).

The first WebGL run failed with "try to dispose a LuaEnv with C# callback!":
WebGL collects garbage only between frames, so the delegates dropped in the
frame that ran the checks were still alive at `LuaEnv.Dispose`. The player
now disposes one frame later (see the FAQ entry on that message).

Player size, release ARM64 APKs from `size_report.py` (`--configs default`,
Unity 6000.3.21f1, Minimal stripping), bytes:

| variant | APK | over baseline | `libxlua.so` compressed / uncompressed | `libil2cpp.so` delta | `global-metadata.dat` delta |
| --- | --- | --- | --- | --- | --- |
| baseline | 12,279,052 | | | | |
| ng-5.5 | 13,655,742 | +1,376,690 | 159,185 / 372,560 | +549,987 / +1,838,856 | +121,946 / +344,984 |
| ng-5.5+nosig | 13,411,470 | +1,132,418 | 159,185 / 372,560 | +362,803 / +1,287,520 | +64,919 / +207,020 |
| ng-5.5+notemplate | 13,640,814 | +1,361,762 | 159,185 / 372,560 | +538,899 / +1,803,376 | +118,102 / +335,024 |
| ng-5.5+noutil | 13,652,686 | +1,373,634 | 159,185 / 372,560 | +550,408 / +1,838,616 | +118,465 / +332,240 |
| ng-5.5+noprofiler | 13,653,922 | +1,374,870 | 159,185 / 372,560 | +550,417 / +1,838,616 | +119,692 / +335,944 |
| all four defines | 13,392,790 | +1,113,738 | 159,185 / 372,560 | +351,652 / +1,249,792 | +57,392 / +174,884 |

Against the 2026-10-16 figures in `build/README.md` ("Player size"; baseline
12,279,056, ng-5.5 +1,397,134): xlua-ng now adds 20,444 bytes less.
`libxlua.so` is 10,241 bytes smaller compressed (169,426 to 159,185; the size
flags), the Lua files in the assets went from +6,965 to +1,380 compressed
(xlua.util and perf.profiler moved into `EmbeddedModules.cs`), and
`global-metadata.dat` grew by 5,586 (+116,360 to +121,946: the embedded
module strings and `XLua.Binding`). The four defines together save 262,952
bytes: `XLUA_NO_SIGNATURE_LOADER` 244,272, `XLUA_NO_TEMPLATE` 14,928,
`XLUA_NO_UTIL` 3,056, `XLUA_NO_PROFILER` 1,820.

Not run: iOS, physical devices, Lua 5.3, 5.4 and LuaJIT players, Medium or
High stripping in the smoke players, hotfix injection in a player.

### 2026-10-18: the source generator in IL2CPP players

`prepare.py --source-gen` with main at `7ff004a` plus this branch, Unity
6000.3.21f1, Android ARM64 IL2CPP, Development builds, Lua 5.5.1 plugin from
`build/make_android_lua55.sh` (NDK r30, release build), the `xlua16k` AVD
(Android 16, API 36, 16 KB pages), headless. Both players printed
`XLUA_NG_SMOKE_PASS: ..., delegates generic, Func<int, int> by signature,
..., source generator (7 generated types, bridge
XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0)` and the
init-only setter's fallback warning once:

| Stripping | APK | `libil2cpp.so` (compressed) | `global-metadata.dat` | stripped `Assembly-CSharp.dll` | stripped `XLua.dll` |
| --- | ---: | ---: | ---: | ---: | ---: |
| Minimal | 23,403,254 | 11,518,064 (3,764,399) | 1,850,684 | 26,112 | 285,184 |
| High | 21,572,931 | 9,124,344 (2,953,168) | 1,297,904 | 23,552 | 221,184 |

Static checks of the High build (`Library/Bee`):

- the stripped `Assembly-CSharp.dll` (monodis) keeps `Bindings`, `W_SgApi_*`,
  `W_SgInit_*`, `Bridges`, `Structs` and `PackUnpack`, the `DoNotGen` method
  `SgApi.Reflected` (the wrapper has no function for it), `SgInit.set_Locked`
  and all of `SgReflected`, and drops `SgUnused`, which nothing uses; it has
  no `LinkXml` or `Manifest` class. Minimal keeps `SgUnused`;
- UnityLinker got `--include-link-xml=<project>/Temp/XLuaSourceGen/link.xml`;
- `RuntimeInitializeOnLoads.json` lists `Bindings.Register` of
  Assembly-CSharp (and of XLua, whose default GCOptimize pushers M4
  generates);
- in `il2cppOutput/cpp/Assembly-CSharp.cpp` the registration lambda creates
  `SgTwice` with `il2cpp_codegen_object_new` and its constructor with the
  `DelegateBridge` as target and `Bridges.__Gen_Delegate_Imp0` as method, as
  for an instance method.

Failures on the way: the first build with the generator installed failed in
UnityLinker ("There is no Unicode byte order mark. Cannot switch to
Unicode."): `SourceGenChecks.MergedLinkXml` wrote `encoding="utf-16"` into a
UTF-8 file, so every IL2CPP build with the generator installed and a
non-empty fragment failed. Fixed in `SourceGenChecks.cs`;
`XluaSourceGenTests.MergedLinkXmlReadsAsUtf8` covers it in the Editor suite.
The smoke's own checks needed two changes: `Func<int, int>` is no longer an
error with the generator (it gets `SgTwice`'s bridge by signature), and High
stripping removes an unused `SignatureLoader`, so the smoke now references it
unless `XLUA_NO_SIGNATURE_LOADER` is set.

Not run with the generator: release builds, Android players from Unity
2021.3.45f2 and 6000.6.4f1 (both have the Android module; 2021.3's 4 KB
aligned libraries stop at a dialog on this AVD), iOS, WebGL and a phone.

Device runs were completed on 2026-10-07 and 2026-10-08. iOS used Xcode
27.1 beta 1; iPhone result files were retrieved from the app container.
Android results were captured from the launched app's process log.
The Lua 5.5 APK contains the final rebuilt ARM64 plugin, with Unity's
native symbol stripping applied during packaging.

The expanded Lua 5.5 checks include GC stop, step, restart, memory count,
and runtime-normalized pause/step multiplier. Lua 5.5 normalizes requests
224 and 240 to 212 and 237. Exact setter round-trips across different Lua
runtimes are not promised.

These results do not establish legacy iOS support, Unity versions other
than those named above, 16 KB physical devices, other browsers, full
application compatibility, or UWP compilation/execution. UWP guard checks on
macOS are separate from a Windows SDK build and device run.
