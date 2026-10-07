# Unity mobile smoke test

This project checks Lua 5.3 or 5.4 loading, managed GC setters and getters,
coroutines, full GC, and LuaEnv disposal in Unity. Lua 5.4 also checks
to-be-closed values.
It is a smoke test, not the complete unit suite or a support matrix.

Run `prepare.py --help` for the required rebuilt plugin paths. Use an empty
output directory on an external disk. The script copies the runtime sources
and platform metadata into a separate project; it never changes an existing
project or uses the upstream prebuilt binaries. The default editor version is
6000.3.21f1. Pass `--lua-version 5.3` when supplying Lua 5.3.6 plugins;
the default is 5.4. Keep prepared projects outside this repository.

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

## Validation recorded on 2026-10-07

- Unity 6000.3.21f1 Editor on macOS ARM64: Lua 5.4.9 and Lua 5.3.6
  smoke checks passed.
- Unity 6000.3.21f1 iOS ARM64 IL2CPP, Xcode 27.1 beta 1: export,
  compilation, signing, installation, and smoke checks passed on iPhone 14 Pro.
  The player result file was retrieved from its app data container.
- Android ARM64 IL2CPP APK built successfully from the prepared project.
  Its packaged libxlua.so retains the xlua_gc and library version exports;
  Unity strips native debug symbols while packaging.
  No Android device was attached, so Android player runtime remains unverified.
- These results do not establish iOS 9 compatibility, other Unity versions,
  or full application compatibility. UWP guard checks on a macOS host are
  separate from a real Windows SDK/UWP build and device run.
