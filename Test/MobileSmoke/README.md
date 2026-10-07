# Unity mobile smoke test

This project checks Lua 5.3, 5.4, or 5.5 loading, managed GC setters and getters,
coroutines, full GC, and LuaEnv disposal in Unity. Lua 5.4 and 5.5 also check
to-be-closed values.
It is a smoke test, not the complete unit suite or a support matrix.

Run `prepare.py --help` for the required rebuilt plugin paths. Use an empty
output directory on an external disk. The script copies the runtime sources
and platform metadata into a separate project; it never changes an existing
project or uses the upstream prebuilt binaries. The default editor version is
6000.3.21f1. Pass `--lua-version 5.3` when supplying Lua 5.3.6 plugins;
pass `--lua-version 5.4` for Lua 5.4.9 plugins. The default is 5.5. Keep prepared projects outside this repository.

Run Unity in batch mode with the prepared project path and one of these
`-executeMethod` values:

- `SmokeBuild.CheckEditor`: run the checks inside the Editor.
- `SmokeBuild.BuildIos`: export an ARM64 IL2CPP iOS project targeting iOS 15.
- `SmokeBuild.BuildAndroid`: build an ARM64 IL2CPP APK targeting Android API 25.

Set `XLUA_SMOKE_OUTPUT_ROOT` to the external build output directory. If unset,
the harness uses a `builds` directory alongside the prepared project. For
Android builds, set an isolated `GRADLE_USER_HOME` on the external disk.
Use `-buildTarget iOS` or `-buildTarget Android` when launching the relevant
player build. iOS export still requires an Xcode build, development signing,
installation, and launch before it counts as a device test.

The player logs `XLUA_NG_SMOKE_PASS` or `XLUA_NG_SMOKE_FAIL` and writes
`xlua-smoke-result.txt` under `Application.persistentDataPath`. A successful
export or APK build alone does not establish that these runtime checks passed.

The iOS 15 setting belongs to this modern Unity smoke project. It does not
verify or define a legacy iOS support promise for the standalone library.

## Recorded validation

Using Unity 6000.3.21f1:

| Runtime | macOS ARM64 Editor | iPhone 14 Pro IL2CPP | Android ARM64 IL2CPP |
| --- | --- | --- | --- |
| Lua 5.3.6 | Passed | Passed | Not run |
| Lua 5.4.9 | Passed | Passed | Passed |
| Lua 5.5.1 | Passed | Passed | Passed |

Device runs were completed on 2026-10-07 and 2026-10-08. iOS used Xcode
27.1 beta 1; iPhone result files were retrieved from the app container.
Android results were captured from the launched app's process log.
The Lua 5.5 APK contains the final rebuilt ARM64 plugin, with Unity's
native symbol stripping applied during packaging.

The expanded Lua 5.5 checks include GC stop, step, restart, memory count,
and runtime-normalized pause/step multiplier. Lua 5.5 normalizes requests
224 and 240 to 212 and 237. Exact setter round-trips across different Lua
runtimes are not promised.

These results do not establish legacy iOS support, other Unity versions,
full application compatibility, or UWP compilation/execution. UWP guard
checks on macOS are separate from a Windows SDK build and device run.
