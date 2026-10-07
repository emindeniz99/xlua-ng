# Upstream Android example checks

This harness builds four existing scenes with rebuilt Lua 5.5.1 plugins and
generated IL2CPP bridges. It does not copy the checked-in upstream binaries.

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
  --android-plugin /absolute/rebuilt/libxlua.so
```

Run Unity in two separate invocations so generated sources compile before the
player build. Use your Unity executable path as `UNITY`:

```sh
"$UNITY" -batchmode -nographics -quit -projectPath /absolute/empty/project \
  -buildTarget Android -executeMethod ExampleBuild.Generate -logFile /absolute/generate.log
XLUA_EXAMPLE_APK=/absolute/examples.apk "$UNITY" -batchmode -nographics -quit \
  -projectPath /absolute/empty/project -buildTarget Android \
  -executeMethod ExampleBuild.Android -logFile /absolute/build.log
```

The configuration generates wrappers for the Unity APIs used by these scenes,
filters byref-like APIs such as `Span<T>`, and excludes the upstream Light
Editor-only members. The UI script uses explicit component types so the
player does not depend on short-name component lookup.

Install the APK with `adb -s DEVICE install -r /absolute/examples.apk`, then:

```sh
adb -s DEVICE shell am start -n org.xluang.examples/com.unity3d.player.UnityPlayerGameActivity
adb -s DEVICE logcat -d -s Unity
```

Require four `XLUA_EXAMPLE_PASS` markers and the final `XLUA_EXAMPLES_PASS`
marker from that launch's process. A successful build alone is insufficient.
Do not mix results from older launches in the same log buffer.

## Verified run

On 2026-10-08, all four checks and the final pass marker succeeded on a
physical Moto G Play (2024), Android 14, using Unity 6000.3.21f1, ARM64 IL2CPP
and Lua 5.5.1. The UI scene remained available for manual use. No iPhone run
was performed in this validation. The device log is stored locally under
`builds/android-lua551-examples-device.log` in the external build directory.
