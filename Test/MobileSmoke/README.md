# Unity mobile smoke test

This project checks Lua 5.3, 5.4, or 5.5 loading, managed GC setters and getters,
coroutines, full GC, and LuaEnv disposal in Unity. Lua 5.4 and 5.5 also check
to-be-closed values. It also checks:

- the integer rule for C# integer parameters (docs/MIGRATING.md): `3.0` and
  `"12"` are accepted, `2.75` raises "number has no integer representation";
- Lua functions cast to C# delegates, from `LuaTable.Get` and as a method
  argument. A `Func<string, string>` uses `DelegateBridge`'s generic `Func`
  methods. Without generated code a `Func<int, int>` raises "This type must
  add to CSharpCallLua"; with `--source-gen` it gets the bridge of a
  configured delegate with the same signature (below). Until milestone M11
  `--gen` ran Generate Code, whose bridges were static methods of
  `XLua.CSObjectWrap.DelegatesGensBridge`;
- the C# feature defines: the `template` library, the embedded `xlua.util`
  and `perf.profiler`, and `SignatureLoader` are present unless
  `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL`, `XLUA_NO_PROFILER` and
  `XLUA_NO_SIGNATURE_LOADER` remove them. A player keeps `perf.profiler` in a
  Development Build and the Editor keeps `template` and `perf.profiler`, as
  documented.

- the `debug` library: under `XLUA_REDUCED_DEBUG` (part of the minimal
  preset) a release player's env has only `debug.traceback` and
  `debug.getinfo`; the Editor, a Development Build and a player without the
  define have the whole library. `debug.traceback` must work as an error
  handler either way. The pass line says `debug reduced` or `debug full`.

The pass line lists what was found, for example `delegates generic,
template off, xlua.util off, perf.profiler off, SignatureLoader off, debug reduced`.
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
Since 2026-10-18 xLua goes to `Assets/XLua/Core` and `Assets/XLua/Src` with
their asmdefs, as in the repository; `--no-asmdef` puts it into
Assembly-CSharp from the historical `Assets/XLua/*.cs` (the layout of XLua >
Project Layout > Use Assembly-CSharp). Generated bindings come only from the
xLua source generator since milestone M11 removed Generate Code (and with it
`--gen` and `SmokeGenConfig.cs`). `--source-gen BUILD_DIR` binds through it:
the Editor code comes along (with `XLua.Editor.asmdef` in the asmdef layout),
the generator is built into `BUILD_DIR` and installed in
`Assets/XLua/Src/Analyzers` (`Tools/build_source_generator.py`; with
`--no-asmdef` in `Assets/XLua/Analyzers`, where no asmdef scopes it, so Unity
runs it in every assembly and it generates into Assembly-CSharp, which then
holds xLua; with `--package` in the package's `Runtime/Analyzers`), and
`SgSmoke.cs` goes into Assembly-CSharp with `XLUA_SMOKE_SOURCE_GEN` defined.
Nothing is generated before the build. Without `--source-gen` the player has
no generated code and wraps every type through reflection. The pass line then adds `Func<int, int> by signature` and
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

The minimal preset ([presets.md](../../docs/design/presets.md#implemented-legacy-and-minimal)):
`--android-minimal-plugin`, `--ios-minimal-plugin` and `--macos-minimal-plugin`
copy the library that `XLUA_VARIANT=minimal XLUA_PRESET=minimal
build/make_<platform>_lua55.sh` builds as the plugin variant
`Plugins/<platform>/xlua-variants/minimal/...` next to the default plugin (which
the same platform's `--*-plugin` gives), copy the plugin selector
(`Assets/XLua/Core/Editor/PluginVariants.cs`) and define
`XLUA_SMOKE_MINIMAL_PLUGIN`. The variants have no `.meta`: before a build
`SmokeBuild.cs` runs **XLua > Plugin Variants > Copy Import Settings**, as a
project does once after adding a variant. Then `XLUA_SMOKE_DEFINES=XLUA_PRESET_MINIMAL`
(Player Settings, where the selector reads it) must give a player that loads the
minimal library, and any other value one that loads the default library. The
pass line reports which: `io off, LuaSocket off` for minimal (no `io` library,
no `socket.core` in `package.preload`), `io on, LuaSocket on` for legacy, and
with `XLUA_SMOKE_MINIMAL_PLUGIN` a player with the wrong one fails. The Editor
always loads the default plugin.

Strict minimal, the preset as shipped: the minimal preset has no reflection
fallback (`XLUA_NO_REFLECTION`) unless `XLUA_REFLECTION` is defined, and
without the generator nothing is bound, so a minimal project without
`--source-gen` needs `XLUA_SMOKE_DEFINES=XLUA_PRESET_MINIMAL;XLUA_REFLECTION`.
With `--source-gen` and `XLUA_SMOKE_DEFINES=XLUA_PRESET_MINIMAL` alone,
`SgSmoke.cs` (under `XLUA_NO_REFLECTION`, which its preset block defines) also
binds what Lua uses outside `SgApi`: `[assembly: LuaCallCSharp(typeof(SmokeApi))]`
for the integer rule and the delegate argument, and `typeof(SgPoint)` for the
fields of the `[GCOptimize]` struct that `SgApi.Swap` returns (`[GCOptimize]`
generates only its pusher and reader). The `[ReflectionUse]` type must then
raise `cannot construct SgReflected: SgReflected has no generated bindings ...
(XLUA_NO_REFLECTION ...)` instead of working; the `[DoNotGen]` method and the
init-only setter keep their per-member fallback, as the preset documents. The
preset implies `GEN_CODE_MINIMIZE`, so the generator's checks run in GCM mode,
and the source generator part of the pass line starts with `no reflection
fallback`.

The core-only smoke (`LuaScript` without the bridge,
[core-luascript.md](../../docs/design/core-luascript.md) section 8.10):
`--package --core --android-plugin CORE_LIB` installs the package with a core
library (`XLUA_VARIANT=core XLUA_PRESET=core build/make_android_lua55.sh`) as
its plugin and copies `CoreSmoke.cs` instead of `XluaSmoke.cs`. Build with
`XLUA_SMOKE_DEFINES=XLUA_CORE_ONLY`, and run `-executeMethod
SmokeBuild.ApplyDefines` once before `SmokeBuild.BuildAndroid` with the same
variable, so the build launch compiles without `XLua` and `XLua.Editor`. The
player checks `Load` and `Call`, `Register`, the sandbox, the buffer library,
the memory, instruction and time limits and `Interrupt`, and that no `XLua`
assembly is loaded. Then it measures how late a 100 ms `TimeLimit` (alone, and
with an `InstructionLimit` of 3e9 or 1e11) and `Interrupt()` from a second
thread (without limits, with an instruction limit, with a time limit, with
both) stop an endless loop, 20 runs per case. It logs `XLUA_NG_CORE_SMOKE_PASS`
or `XLUA_NG_CORE_SMOKE_FAIL`, then one line per case: `XLUA_NG_CORE_LATENCY
<case>: n=20 min=... p50=... p90=... max=... mean=... ms all=[...]`, and writes
the same lines to `xlua-smoke-result.txt`. A case fails when its 90th
percentile is over 100 ms, a single stop is over 1 s, or a stop is missed.
Single stops on the emulator of a loaded host scatter up to a few hundred
milliseconds, so a 100 ms bound on every stop fails there
([core-stop-latency.md](../../docs/research/core-stop-latency.md#on-target-2026-10-18)).
A player runs the checks on a worker thread. A case starts no new run after
20 s, so a player from before the fix of 2026-10-18 (stops seconds late)
finishes within a few minutes. `core_only_player_check.py APK --project DIR`
checks the APK for bridge code.

Run Unity in batch mode with the prepared project path and one of these
`-executeMethod` values:

- `SmokeBuild.CheckEditor`: run the checks inside the Editor.
- `SmokeBuild.BuildIos`: export an ARM64 IL2CPP iOS project targeting iOS 15,
  for devices, or with `XLUA_SMOKE_IOS_SDK=SimulatorSDK` for the arm64 iOS
  Simulator (below).
- `SmokeBuild.BuildAndroid`: build an ARM64 IL2CPP APK targeting Android API 25 (API 26 on Unity 6.6 and later, which supports nothing lower).
- `SmokeBuild.BuildWebGL`: build an uncompressed WebGL player into
  `unity-webgl-smoke`, which any static file server can host.
- `SmokeBuild.BuildMac`: build an ARM64 macOS Mono player into
  `unity-macos-smoke.app` (the local editors have no macOS IL2CPP module).
  Run `Contents/MacOS/XluaNgSmoke -batchmode -nographics -logFile LOG`; a
  desktop player quits after the checks.

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
launching the relevant player build. An iOS device export still requires an
Xcode build, development signing, installation, and launch before it counts as a
device test; `ios_sim_smoke.py` runs a simulator export (below).

The player logs `XLUA_NG_SMOKE_PASS` or `XLUA_NG_SMOKE_FAIL` and writes
`xlua-smoke-result.txt` under `Application.persistentDataPath`. A successful
export or APK build alone does not establish that these runtime checks passed.

The panic experiment: with `XLUA_SMOKE_PANIC` in `XLUA_SMOKE_DEFINES` (applied
by `SmokeBuild.ApplyDefines` like the others) the player and
`SmokeBuild.CheckEditor` run only `PanicSmoke` (`XluaSmoke.cs`), which makes
Lua call xLua's panic handler on purpose (`lua_rawset` with a nil key from C#,
with no protected call around it), twice, and checks the env after each. It
logs `XLUA_NG_PANIC <step> ...` lines as it goes and `XLUA_NG_PANIC_RESULT:
...` at the end. Since 2026-10-28 an IL2CPP player ends at the first panic
instead, after `xLua: unprotected error in call to Lua API (...)` (Unity's
log) and `xLua: Lua panic, ...` (IL2CPP's FailFast message); the Editor gives
the result line. Results:
[crash-inventory.md](../../docs/research/crash-inventory.md), item 9.

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
Install with `adb install --no-streaming -r APK`: the default streamed install
failed intermittently on the AVD with an empty error message, and the
non-streamed one (copy the APK, then install) did not. No script here runs
`adb install`; whoever installs by hand or in a wrapper should pass the flag
(or retry once with it).

## iOS Simulator

The simulator needs a simulator build of the plugin: `XLUA_IOS_SDK=simulator
build/make_ios_lua55.sh` (and with `XLUA_VARIANT=minimal XLUA_PRESET=minimal`
for the minimal one; [build/README.md](../../build/README.md#ios-simulator)).
`prepare.py --ios-plugin DEVICE_LIB --ios-simulator-plugin SIM_LIB` copies it as
the plugin variant `Plugins/iOS/xlua-variants/simulator/libxlua.a`, and
`--ios-minimal-simulator-plugin` as `minimal_simulator` next to
`--ios-minimal-plugin`; the selector takes them for a Simulator SDK build only,
so one project builds both. With `--package` the variants go into the package
(since 2026-10-18 `Tools/make_upm_package.py` gives them their default
plugin's import settings with the Editor off), so a package project also
builds both. On 2026-10-18 a `--package` project with the device and the
simulator library of Lua 5.5.1 (Unity 6000.3.21f1) passed in the iOS 27.1
Simulator (`XLUA_NG_SMOKE_PASS`), and the device export of the same project
took `Plugins/iOS/libxlua.a` ("XLua plugin variants" lines of the two build
logs).

```sh
XLUA_SMOKE_IOS_SDK=SimulatorSDK XLUA_SMOKE_OUTPUT_ROOT=OUT Unity -batchmode -nographics -quit \
    -projectPath PROJECT -buildTarget iOS -executeMethod SmokeBuild.BuildIos -logFile LOG
python3 Test/MobileSmoke/ios_sim_smoke.py --xcode-project OUT/unity-ios-smoke \
    --derived-data WORK/DerivedData --logs WORK/logs
```

`ios_sim_smoke.py` compiles the export with `xcodebuild -sdk iphonesimulator
CODE_SIGNING_ALLOWED=NO` (the linker signs the arm64 binaries ad hoc, which
the simulator accepts), installs the app on a booted simulator or boots the
newest iPhone, launches it, waits for `Documents/xlua-smoke-result.txt` in
the app's data container and prints the result line; it shuts down a
simulator it booted. The app's stdout and stderr go to its own `tmp` folder
and are copied to `--logs`: with `simctl launch --stdout=` on an external
volume, launchd_sim fails the spawn with EPERM ("Launchd job spawn failed",
FBSOpenApplicationServiceErrorDomain code 3). `SmokeBuild.cs` sets the Target
SDK back to the device SDK when `XLUA_SMOKE_IOS_SDK` is unset, because the
setting persists in the project.

Unity 6000.x builds arm64 simulator players. Unity 2021.3 builds only x86_64
ones, which the arm64-only iOS 27.1 simulator runtime cannot run.

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

### 2026-10-28: the panic experiment

`XLUA_SMOKE_PANIC` players (above), Lua 5.5.1, Unity 6000.3.21f1, Android
ARM64 IL2CPP, Minimal stripping, release and Development, on a moto g play
2024 (Android 14, 4 KB pages) and the `xlua16k` AVD (Android 16, 16 KB pages).
Before the change of that day each player caught `LuaException` twice and
kept a working env; after it each ends at the first panic with the messages
above and a SIGTRAP tombstone. Details:
[crash-inventory.md](../../docs/research/crash-inventory.md), item 9.

### 2026-10-28: strict minimal players

The first players of the minimal preset as shipped, without the reflection
fallback (strict minimal, above). Main at `aadd1d87` with this branch,
whose runtime C# and native code equal `d37515c2`'s (the run below), and the
same Lua 5.5.1 plugins: `build/make_android_lua55.sh` (NDK r30) with and
without `XLUA_VARIANT=minimal XLUA_PRESET=minimal`. `prepare.py
--android-plugin ... --android-minimal-plugin ... --macos-plugin ...
--source-gen BUILD_DIR`, `XLUA_SMOKE_DEFINES=XLUA_PRESET_MINIMAL` applied by
`SmokeBuild.ApplyDefines` in a launch of its own, Unity 6000.3.21f1, Android
ARM64 IL2CPP, Minimal stripping, the `xlua16k` AVD (Android 16, API 36, 16 KB
pages) headless, `adb install --no-streaming`. Both players and the Editor
(`SmokeBuild.CheckEditor` with the Android target) printed
`XLUA_NG_SMOKE_PASS`, and both builds logged the minimal plugin variant:

| Build | APK | Pass line (features) |
| --- | ---: | --- |
| Editor | | template on, xlua.util off, perf.profiler on, SignatureLoader off, debug full, io on, LuaSocket on, `source generator (no reflection fallback, GCM, 4 methods of W_SgApi_0217fb75, 12 generated types, ...)` |
| release | 12,819,054 | integer rule, delegates generic, `Func<int, int> by signature`, template, xlua.util, perf.profiler, SignatureLoader off, **debug reduced**, io off, LuaSocket off, `source generator (no reflection fallback, GCM, 4 methods of W_SgApi_0217fb75, 9 generated types, bridge XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0)` |
| Development | 18,535,511 | the same with perf.profiler on and **debug full**, and the init-only setter's warning once |

The first Editor run failed with `cannot use SgPoint.X: SgPoint has no
generated bindings`: the `[GCOptimize]` struct's fields went through
reflection before, so the strict configuration binds `SgPoint` too (above).
The APKs are larger than the minimal players below (12,775,158 and
18,494,755) because this project also has the generator and `SgSmoke.cs`;
the size of the preset itself is measured with the size probe
([build/SIZE.md](../../build/SIZE.md#reflection-off-in-the-minimal-preset),
`size_report.py --configs strict`: +499,162 bytes over the baseline strict,
+489,970 with `XLUA_REFLECTION`).

Not run: strict minimal with High stripping, from the package, on iOS, WebGL,
a phone and other Unity versions.

### 2026-10-28: the minimal preset's reduced debug library and main after M11

Main at `d37515c2` with the `debug` check above (a first round at `b3adc8e8`
gave the same pass lines): after the minimal preset
lost its reflection fallback, overload selection by best fit, the bridge
checks by build type and the #999 `ObjectDisposedException`, with the native
bridge version 110, the plugin import settings that enable each desktop
plugin only for its own OS (`36303557`) and the reproducible native builds.
Unity 6000.3.21f1, Android ARM64 IL2CPP, Minimal
stripping, Lua 5.5.1 plugins built from the tree with
`build/make_android_lua55.sh` (NDK r30) with and without
`XLUA_VARIANT=minimal XLUA_PRESET=minimal`, each define list applied by
`SmokeBuild.ApplyDefines` in a launch of its own, the `xlua16k` AVD (Android
16, API 36, 16 KB pages) headless, `adb install --no-streaming`. The minimal
project has both libraries (`--android-plugin ... --android-minimal-plugin
...`) and no generator, so its players need `XLUA_REFLECTION` for the
smoke's unbound `SmokeApi`. Every player printed `XLUA_NG_SMOKE_PASS`:

| Project | Build | `XLUA_SMOKE_DEFINES` | APK | Pass line (features) |
| --- | --- | --- | ---: | --- |
| minimal | release | `XLUA_PRESET_MINIMAL;XLUA_REFLECTION` | 12,775,158 | template, xlua.util, perf.profiler, SignatureLoader off, **debug reduced**, io off, LuaSocket off |
| minimal | Development | the same | 18,494,755 | the same with perf.profiler on and **debug full** |
| `--source-gen` | release | none (`XLUA_SMOKE_NONE`) | 13,828,382 | all on, debug full, io on, LuaSocket on, `Func<int, int> by signature`, `source generator (7 generated types, bridge XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0)` |
| `--source-gen` | release | `GEN_CODE_MINIMIZE` | 13,829,958 | the same with `GCM, 4 methods of W_SgApi_0217fb75` |
| every ABI | release, `XLUA_SMOKE_ANDROID_ARCHS='ARMv7, ARM64, X86_64'` | none | 31,060,347 | as legacy without the generator |

Both minimal builds logged `XLua plugin variants:
Assets/Plugins/Android/libs/arm64-v8a/libxlua.so ->
Assets/Plugins/Android/xlua-variants/minimal/libs/arm64-v8a/libxlua.so`.
The "every ABI" project has the armeabi-v7a, x86 and x86_64 libraries next
to arm64-v8a, each with the repository's `.meta`; its APK has
`lib/<abi>/libxlua.so` for armeabi-v7a (258,500 bytes), arm64-v8a (385,720)
and x86_64 (370,888), and the arm64 player ran. Unity builds no 32-bit x86
Android players since 2019.3, so the x86 library was only imported. So
`XLUA_REDUCED_DEBUG` takes effect in a release player and not in a
Development one, as `LuaEnv.DefaultLibraries` says, and the error handler
keeps its traceback. `size_report.py --configs minimal` measured the preset
again ([build/SIZE.md](../../build/SIZE.md#reflection-off-in-the-minimal-preset)).

Not run: a minimal player without `XLUA_REFLECTION` (the smoke calls
`SmokeApi`, which nothing binds, and `SgSmoke.cs` checks members that reach
C# through reflection on purpose; strict minimal players ran later the same
day, above), Development and High stripping players
with the generator, other Unity versions, iOS, WebGL and a phone.

### 2026-10-18: without the legacy generator (milestone M11)

The first players after M11 removed Generate Code (and `--gen`):
`prepare.py --android-plugin SO --source-gen BUILD_DIR`, Unity 6000.3.21f1,
Android ARM64 IL2CPP, release (`XLUA_SMOKE_RELEASE=1`), Minimal stripping,
the Lua 5.5.1 plugin built from this tree with `build/make_android_lua55.sh`
(NDK r30, arm64-v8a, stripped; `check_android_elf.py` passed: PT_LOAD
alignment 0x4000, 293 exports), the `xlua16k` AVD (Android 16, API 36, 16 KB
pages) headless. GCM: `XLUA_SMOKE_DEFINES=GEN_CODE_MINIMIZE` applied by
`SmokeBuild.ApplyDefines` in a launch of its own. Every player printed
`XLUA_NG_SMOKE_PASS: Lua 5.5, ..., delegates generic, Func<int, int> by
signature, ..., source generator (7 generated types, bridge
XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0),
disposal`, the GCM players with `GCM, 4 methods of W_SgApi_0217fb75`.

Size against the tree before M11 (main at `24ed1374`, an export of it), the
same project, plugin, editor and settings, one build each, bytes
(uncompressed / compressed in the APK):

| Tree | APK | `libil2cpp.so` | `global-metadata.dat` | `libxlua.so` |
| --- | ---: | ---: | ---: | ---: |
| `24ed1374` (before M11) | 13,812,706 | 11,070,240 / 3,308,198 | 1,824,320 / 604,857 | 384,824 / 164,400 |
| `b80e165c` (M11) | 13,811,110 | 11,066,608 / 3,306,535 | 1,824,284 / 604,917 | 384,824 / 164,400 |
| `afa14181` (M11 with `a5e3882e`) | 13,812,118 | 11,069,320 / 3,307,459 | 1,824,512 / 604,993 | 384,824 / 164,400 |
| `b80e165c`, `GEN_CODE_MINIMIZE` | 13,812,306 | 11,070,656 / 3,307,229 | 1,826,132 / 605,422 | 384,824 / 164,400 |
| `afa14181`, `GEN_CODE_MINIMIZE` | 13,813,686 | 11,073,432 / 3,308,494 | 1,826,352 / 605,527 | 384,824 / 164,400 |

The smoke's code and configuration are the same in every tree, and the
generator wrote the same 7 types; the runtime differs. At `b80e165c`
`libil2cpp.so` is 3,632 bytes smaller, most likely from what `ff8792d9`
removed (`AssemblyBindings.DelegateBridges` and the registry's list of
bridge classes, `XLua.SysGenConfig`; not checked symbol by symbol);
`a5e3882e` (the runtime reads the assembly-level configuration) adds 2,712
back. At the final tree the APK is
588 bytes (0.004%) smaller than before M11, `libil2cpp.so` 920 bytes
(0.008%), and `global-metadata.dat` 192 bytes larger. The players of the
first two rows passed on the emulator as well (`24ed1374` with the same
pass line).

Not run: Development players, High stripping, `--no-asmdef --source-gen` and
`--package` players, other Unity versions, iOS, WebGL and a phone.

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

### 2026-10-18: the source generator's GCM mode in IL2CPP players

`prepare.py --source-gen` as above (C# at `4f114134`), with
`XLUA_SMOKE_DEFINES=GEN_CODE_MINIMIZE` applied by `SmokeBuild.ApplyDefines` in a
launch of its own, so the generator writes its GCM wrappers
([source-generator.md](../../docs/design/source-generator.md#gcm-mode-as-implemented-2026-10-18)).
Unity 6000.3.21f1, Android ARM64 IL2CPP, Development builds with High and
Minimal stripping, Lua 5.5.1 plugin from `build/make_android_lua55.sh` (NDK
r30), the `xlua16k` AVD (Android 16, 16 KB pages), headless. Both players
printed `XLUA_NG_SMOKE_PASS: ..., source generator (GCM, 4 methods of
W_SgApi_0217fb75, 7 generated types, bridge
XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0), ...`
and the init-only setter's fallback warning once. `SgSmoke.cs` now also
checks, in both modes, that a C# exception thrown in a wrapped method
(`SgApi.Fail`) reaches Lua as `c# exception:...`; in GCM mode the
trampoline's C# entry catches it. APKs: 18,655,814 bytes (High) and
22,449,722 (Minimal); the IL2CPP output has 34 reverse P/Invoke wrappers in
each.

The AVD's package verifier made `adb install` wait until the timeout; with
`adb shell settings put global verifier_verify_adb_installs 0` (and
`package_verifier_enable 0`) installs take seconds.

### 2026-10-18: xLua from the UPM package in IL2CPP players

`prepare.py --package` installs xLua as the UPM package of
`Tools/make_upm_package.py` from a `file:` tarball in `LocalPackages/`
(`Packages/manifest.json`), with the given plugins inside it, instead of
copying it into `Assets`; with `--source-gen` the generator is in the
package's `Runtime/Analyzers`. Since 2026-10-19 `--package-tarball FILE.tgz`
installs a finished tarball instead, such as the release file
`com.xlua-ng.xlua-<version>.tgz`, with its own plugins (it takes no plugin
options; with `--source-gen` the tarball must hold the generator), so a
release can be checked as users get it ([Test/README.md](../README.md#a-release-shaped-tarball-and-its-samples-2026-10-19)).
`--webgl --package` builds the WebGL smoke from
the package's own `Plugins/WebGL/xlua_webgl.cpp` and `build~` sources (below).
Unity 6000.3.21f1, Android ARM64 IL2CPP, Development builds, Lua 5.5.1
plugin from `build/make_android_lua55.sh` (NDK r30), the `xlua16k` AVD
(Android 16, 16 KB pages), headless. Every player printed
`XLUA_NG_SMOKE_PASS`; the generator players also the source generator part
(`7 generated types, bridge XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0`,
`Func<int, int> by signature`) and the init-only setter's fallback warning
once:

| Variant | Stripping | APK | `libil2cpp.so` (compressed) | `global-metadata.dat` | stripped `Assembly-CSharp.dll` | stripped `XLua.dll` |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `--package --source-gen` | Minimal | 19,641,339 | 11,522,648 (3,765,395) | 1,853,156 | 26,112 | 289,280 |
| `--package --source-gen` | High | 18,621,475 | 9,127,128 (2,954,070) | 1,298,456 | 23,552 | 222,720 |
| `--package` (reflection) | Minimal | 19,542,195 | 11,143,904 (3,675,244) | 1,831,332 | 13,824 | 271,872 |

The players ran on `4ab2c296` (the table) and passed again on `976cbe1e`
(main at `f8e6210d` merged, plugin rebuilt), with the same `libil2cpp.so`,
metadata and stripped assembly sizes and APKs within 16 bytes.

The `libil2cpp.so`, metadata and Assembly-CSharp sizes of the generator
players are within 0.2% of the asmdef layout players above (the C# differs
by the merges in between); the package changes where xLua's files are, not
what IL2CPP compiles.

`size_report.py` has release variants for the package and for the source
generator against legacy Generate Code and GCM (`--configs
sg-minimal,sg-high`); the results are in
[build/SIZE.md](../../build/SIZE.md#generated-bindings-generate-code-gcm-and-the-source-generator).

Not run from the package: iOS, release players on the emulator (the
size builds are not run), other Unity versions' players and a phone.

### 2026-10-18: WebGL from the UPM package

`prepare.py --webgl --package` (refused before: the package's WebGL source
had never been built) and `SmokeBuild.BuildWebGL` on Unity 6000.6.4f1, a
Development build, Lua 5.5.1, C# and C from main at `65f7ea17` with the fix
below. Unity compiled `Library/PackageCache/com.xlua-ng.xlua@<hash>/Plugins/WebGL/xlua_webgl.cpp`,
which includes the C sources from the package's `build~` folder (Unity does
not import it), and the player ran in headless Chrome as above:

| Layout | Result |
| --- | --- |
| package (`--webgl --package`) | `XLUA_NG_SMOKE_PASS: Lua 5.5, GC setters/getters, coroutine, integer rule, delegates generic, template on, xlua.util on, perf.profiler on, SignatureLoader on, io on, LuaSocket off, disposal` |
| Assets (`--webgl`, control) | the same line |

The first build from the package failed in Emscripten with "templates must
have C++ linkage" (20 errors in `em_asm.h`): since the core time limits,
`xlua_core_limits.c` includes `<emscripten.h>`, and `xlua_webgl.cpp`
includes the C sources inside `extern "C"`. The Assets layout failed the
same way; the 2026-10-18 WebGL runs above were built before that include.
`xlua_webgl.cpp` now includes `<emscripten.h>` before the block, and
`Test/Native/webgl_compile_check.py --package` compiles both copies with the
editor's Emscripten on the host (it failed on both before the fix).

Not run: WebGL with the source generator, a release WebGL build from the
package, WebGL on 2021.3 or 2022.3 (no WebGL module installed locally).

### 2026-10-18: core-only players

`prepare.py --package --core` with main at `1ff0609d` (core milestones 6, 7
and 10) plus this harness, Unity 6000.3.21f1, ARM64 IL2CPP, the core Lua 5.5.1
library from `XLUA_VARIANT=core XLUA_PRESET=core build/make_android_lua55.sh`
(NDK r30, release build, 268,488 bytes stripped), `XLUA_CORE_ONLY` set by
`SmokeBuild.ApplyDefines`. Every player printed `XLUA_NG_CORE_SMOKE_PASS: Lua
5.5, Load/Call, Register, sandbox, buffer, memory/instruction/time limits
(time N ms), Interrupt, no XLua assembly`, and `core_only_player_check.py`
passed on every APK (no `XLua.dll` in `ScriptingAssemblies.json` or among the
stripped assemblies, no bridge type in the metadata, no bridge export):

| Build | Stripping | `xlua16k` AVD (Android 16, 16 KB pages) | moto g play 2024 (Android 14) |
| --- | --- | --- | --- |
| Development | Minimal | pass (time limit 100 ms: 100 ms) | not run |
| release | Minimal | pass (107 ms) | pass (243 ms) |
| release | High | pass (966 ms) | pass (222 ms) |

The time-limit check sets an instruction limit as a backstop as well; with
both limits the time limit was late by up to a count slice
([core-luascript.md](../../docs/design/core-luascript.md), section 8.10),
which the 966 ms run shows. Fixed in section 8.11; the players were measured
again after the fix (below, "stop latency in core-only players"). High
stripping needs no `link.xml` for `LuaScript`. The phone log is not in the
repository.

Not run: iOS, Lua 5.4.9, other Unity versions, a core-only WebGL player (the
WebGL plugin has no sandbox).

### 2026-10-18: stop latency in core-only players

A recheck of the fix for late stops under an instruction limit
([core-luascript.md](../../docs/design/core-luascript.md), section 8.11) on
target. Two release players with Minimal stripping from Unity 6000.3.21f1,
ARM64 IL2CPP, `prepare.py --package --core` with the extended `CoreSmoke.cs`
in both. *Before* has the C# and the core Lua 5.5.1 libraries of main
`bebb8faa`; *after* has those of `446dea2b`, with the fix (Android arm64-v8a,
NDK r30; arm64 iOS Simulator, Xcode 27.1). `core_only_player_check.py`
passed on both APKs. Milliseconds after the deadline or the `Interrupt()`
call; the 90th percentile, then the maximum, over 20 runs per case (after:
2 or 3 player runs pooled):

| Case | phone before | phone after | AVD before | AVD after | Simulator before | Simulator after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `TimeLimit` 100 ms | 0.9 / 1.2 | 20.6 / 20.7 | 28 / 66 | 42 / 130 | 23 / 25 | 23 / 78 |
| `TimeLimit`, `InstructionLimit` 3e9 | 807 / 1,229 | 9.0 / 12.3 | 79 / 178 | 44 / 219 | 461 / 695 | 23 / 92 |
| `TimeLimit`, `InstructionLimit` 1e11 | 847 / 1,148 | 9.1 / 9.4 | 108 / 223 | 24 / 151 | 497 / 643 | 5.5 / 41 |
| `Interrupt`, no limit | never stops | 0.6 / 2.9 | never stops | 34 / 123 | never stops | 1.2 / 23 |
| `Interrupt`, `InstructionLimit` 3e9 | 12,197 / 12,197 | 0.6 / 0.7 | 11,775 / 11,775 | 13 / 59 | 3,589 / 3,589 | 0.3 / 5.0 |
| `Interrupt`, `TimeLimit` 30 s | 0.5 / 0.6 | 0.6 / 0.6 | 26 / 58 | 14 / 316 | 0.2 / 0.4 | 0.3 / 6.0 |
| `Interrupt`, `TimeLimit` 30 s, `InstructionLimit` 1e11 | 715 / 1,641 | 0.6 / 0.6 | 132 / 186 | 13 / 222 | 53 / 132 | 0.9 / 34 |

Phone: moto g play 2024 (Android 14). AVD: `xlua16k` (Android 16, 16 KB
pages). Simulator: iOS 27.1, "iPhone Duo", run by `ios_sim_smoke.py`. Every
player from before the fix fails the smoke's bound, and every player from
after it passes (the AVD's first two runs were of a build that required every
stop within 100 ms; they failed that bound with single stops of 115 to 316 ms
at a host load average of 20 to 78, and the third run, at a load of 16 to 25,
had every stop within 18.7 ms). The medians, distributions and findings are in
[core-stop-latency.md](../../docs/research/core-stop-latency.md#on-target-2026-10-18).
The phone log is not in the repository.

Not run: an iPhone, Lua 5.4.9 players, High stripping, other Unity versions.

### 2026-10-18: the minimal preset in players

The first builds in which the plugin selector (`PluginVariants.cs`) chose a
library. Main at `35165d02` plus this harness, Unity 6000.3.21f1, ARM64
IL2CPP, Minimal stripping. Lua 5.5.1 plugins from this checkout, NDK r30:
`build/make_android_lua55.sh` (legacy) and `XLUA_VARIANT=minimal
XLUA_PRESET=minimal build/make_android_lua55.sh` (minimal), both in each
project (`prepare.py --android-plugin ... --android-minimal-plugin ...`);
`--source-gen` for the generator row. The build log names the choice, for
example `XLua plugin variants: Assets/Plugins/Android/libs/arm64-v8a/libxlua.so
-> Assets/Plugins/Android/xlua-variants/minimal/libs/arm64-v8a/libxlua.so`,
after `copied the import settings of the default plugin to 1 variant(s)`.
Every player printed `XLUA_NG_SMOKE_PASS` on the `xlua16k` AVD (Android 16,
16 KB pages) and on a moto g play 2024 (Android 14, 4 KB pages, Adreno 610):

| Build | `XLUA_SMOKE_DEFINES` | Selected | APK | `libxlua.so` in the APK | Pass line (features) |
| --- | --- | --- | ---: | ---: | --- |
| release | `XLUA_PRESET_MINIMAL` | minimal | 12,724,314 | 312,744 | template, xlua.util, perf.profiler, SignatureLoader off; io off, LuaSocket off |
| Development | `XLUA_PRESET_MINIMAL` | minimal (no `development` variant) | 18,443,551 | 312,744 | the same, perf.profiler on |
| release | none (`XLUA_SMOKE_NONE`) | default | 13,693,930 | 383,672 | all on; io on, LuaSocket on |
| Development, `--source-gen` | `XLUA_PRESET_MINIMAL` | minimal | 18,537,063 | 312,744 | as minimal Development, plus `Func<int, int> by signature` and `source generator (7 generated types, ...)` |

So the minimal C# with all its hardening defines passes the smoke and the
source generator's checks (wrappers, closed bridges, reflection fallback,
`[GCOptimize]`) in IL2CPP, and the selector honours a Development Build
without a `development` variant. The two release smoke APKs differ by
969,616 bytes. `size_report.py --configs minimal` measured the preset with the
size probe against a baseline, Android and iOS, the selector choosing the
library there too: minimal adds 440,458 bytes to the APK where legacy adds
1,410,978, and 394,233 to the zipped unsigned `.app` where legacy adds
765,523 ([build/SIZE.md](../../build/SIZE.md#players)). The iOS players were
exported and compiled with `xcodebuild` only; none ran.

On the phone the two Development APKs first showed Google Play Protect's
"Send app for a security check?" prompt during `adb install`, which waits
until it is answered (it was answered "Don't send"). Started at once after
that, the player was paused and closed within two seconds, with no result;
started 30 seconds after the install, both passed. The release APKs got no
prompt. The phone log is not in the repository.

### 2026-10-18: iOS players in the Simulator

The first iOS players from the current sources that ran (the iPhone runs
below are from 2026-10-07 and 2026-10-08, before the presets, the selector
and LuaScript; every later iOS build was exported and compiled with
`xcodebuild` only). Main at `98b3bab5` plus this branch, Unity 6000.3.21f1,
ARM64 IL2CPP, Minimal stripping, Xcode 27.1, iOS 27.1 simulator runtime
(24A94401, device "iPhone Duo"), Lua 5.5.1 libraries from
`build/make_ios_lua55.sh` with `XLUA_IOS_SDK=device` and `simulator`, each
project holding both (the core project the simulator library only).
`ios_sim_smoke.py` built, installed and ran each player; every one printed its
pass line and wrote it to `xlua-smoke-result.txt`:

| Project | Build | `XLUA_SMOKE_DEFINES` | Selected library | Pass line (features) |
| --- | --- | --- | --- | --- |
| legacy | Development | none (`XLUA_SMOKE_NONE`) | `xlua-variants/simulator` | `XLUA_NG_SMOKE_PASS`: delegates generic, template, xlua.util, perf.profiler, SignatureLoader on; io on, LuaSocket on |
| legacy | Development | `XLUA_PRESET_MINIMAL` | `xlua-variants/minimal_simulator` | template, xlua.util, SignatureLoader off, perf.profiler on; io off, LuaSocket off |
| legacy | release | `XLUA_PRESET_MINIMAL` | `xlua-variants/minimal_simulator` | the same, perf.profiler off |
| `--source-gen` | Development | none | `xlua-variants/simulator` | as legacy, plus `Func<int, int> by signature` and `source generator (7 generated types, bridge XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0)`; the init-only setter's warning once |
| `--package --core` | release | `XLUA_CORE_ONLY` | the core simulator library (package plugin) | `XLUA_NG_CORE_SMOKE_PASS: Lua 5.5, Load/Call, Register, sandbox, buffer, memory/instruction/time limits (time 1472 ms), Interrupt, no XLua assembly` |

`core_only_player_check.py` passed on the core `.app` with `--project` and
`--link-map` (only `XLua.Core.dll` in `ScriptingAssemblies.json` and among
the stripped assemblies, no bridge type in the metadata, no bridge symbol in
the link map). A second launch of the core player reported 2191 ms for the
100 ms time limit: the limit is late by much more on the simulator than on
the Android AVD (107 to 966 ms) and still under the smoke's 5 s bound. The
minimal release player's link map has nothing of `memory_leak_checker.o`.

A device export from the same legacy project (`XLUA_PRESET_MINIMAL`,
Development, Device SDK) took `xlua-variants/minimal/libxlua.a` and compiled
with `xcodebuild -sdk iphoneos CODE_SIGNING_ALLOWED=NO`; it did not run (no
iPhone attached).

Failures on the way:

- The device library does not link for the simulator, and no simulator
  library existed: `XLUA_IOS_SDK=simulator` for the iOS scripts and the
  `simulator` variants of the selector (`PluginVariants.cs`) are new;
  `plugin_variants_check.py` covers the choice.
- The minimal Development player stopped in the Xcode link: "Undefined
  symbols: _xlua_memory_snapshot, _xlua_memory_snapshot_free". Development
  Builds compile the snapshot imports and IL2CPP links every `__Internal`
  import, but the minimal preset built every library without the walk. The
  preset keeps it for Apple static libraries now
  ([build/README.md](../../build/README.md#memory-snapshot));
  `presets_check.py` checks it. A minimal Development build for an iPhone had
  the same failure.
- `prepare.py` with plugin variants and without `--gen` copied
  `PluginVariants.cs` from `Assets/XLua/Src/Editor`, where it no longer is
  (it moved to `Assets/XLua/Core/Editor`), and stopped with
  FileNotFoundError.
- `simctl launch --stdout=` with a file on the external disk: "Launchd job
  spawn failed" (above).

Not run: an iPhone, Unity 2021.3.45f2 (x86_64 simulator players only; the iOS
27.1 runtime is arm64 only), other Unity versions, `--gen` (needs the macOS
Editor plugin), Lua 5.3, 5.4 and LuaJIT, the minimal source generator player,
High stripping, `hotfix_il2cpp.py` on iOS.

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
