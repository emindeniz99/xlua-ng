# Upstream Android example checks

This harness builds four existing scenes with rebuilt Lua 5.5.1 plugins and
the xLua source generator's IL2CPP bindings and bridges. It does not copy the
checked-in upstream binaries.

The original scenes are loaded in order:

1. `CSharpCallLua/CSCallLua`: delegate and interface calls, tables and out values.
2. `LuaCallCSharp/LuaCallCs`: methods, inheritance, events and interface access.
3. `02_U3DScripting/U3DScripting`: Lua `update` rotates a Unity transform.
4. `03_UIEvent/UI`: a Unity button invokes a Lua listener with an input value.

The runner checks the tutorial output, observes a transform rotation, and
invokes the button's actual `onClick` event after setting the input. Errors or
exceptions observed during the run make the final result fail. This exercises
event wiring; it does not automate physical touch or keyboard input. The UI
scene remains visible at the end for manual interaction.

## Prepare and build

On an Apple Silicon Mac with Unity 6000.3.21f1 and Android build support:

```sh
python3 Test/Examples/prepare.py --output /absolute/empty/project \
  --macos-plugin /absolute/rebuilt/libxlua.dylib \
  --android-plugin /absolute/rebuilt/libxlua.so \
  --source-gen /absolute/source-generator-build
```

`--source-gen` builds the xLua source generator there
(`Tools/build_source_generator.py`, needs the .NET SDK) and installs it in
`Assets/XLua/Src/Analyzers`; Unity runs it when it compiles the scripts, so
one editor launch builds the player. Use your Unity executable path as
`UNITY`:

```sh
XLUA_EXAMPLE_APK=/absolute/examples.apk "$UNITY" -batchmode -nographics -quit \
  -projectPath /absolute/empty/project -buildTarget Android \
  -executeMethod ExampleBuild.Android -logFile /absolute/build.log
```

`ExampleConfig.cs` configures the Unity APIs these scenes use with assembly
attributes (`[assembly: LuaCallCSharp(typeof(...))]`,
`[assembly: CSharpCallLua(typeof(...))]`); the scenes' own types carry the
attributes themselves. Until milestone M11 removed Generate Code, a first
launch ran `ExampleBuild.Generate`, and the configuration was lists with a
BlackList filter for byref-like APIs such as `Span<T>` and Light's
Editor-only members; the generator skips byref-like members itself and
compiles each platform against that platform's UnityEngine. The UI script
uses explicit component types so the player does not depend on short-name
component lookup.

Install the APK with `adb -s DEVICE install -r /absolute/examples.apk`, then:

```sh
adb -s DEVICE shell am start -n org.xluang.examples/com.unity3d.player.UnityPlayerGameActivity
adb -s DEVICE logcat -d -s Unity
```

Require four `XLUA_EXAMPLE_PASS` markers and the final `XLUA_EXAMPLES_PASS`
marker from that launch's process. A successful build alone is insufficient.
Do not mix results from older launches in the same log buffer.

## Verified run

The source generator version of this harness (2026-10-18, milestone M11) has
not been run on a device or emulator yet. The run below used Generate Code.

On 2026-10-08, all four checks and the final pass marker succeeded on a
physical Moto G Play (2024), Android 14, using Unity 6000.3.21f1, ARM64 IL2CPP
and Lua 5.5.1. The UI scene remained available for manual use. No iPhone run
was performed in this validation. The device log is stored locally under
`builds/android-lua551-examples-device.log` in the external build directory.
