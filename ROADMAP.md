# Roadmap

Open work for xLua-NG, roughly in priority order. Finished work is in the git
history; current dependency versions are in [DEPENDENCIES.md](DEPENDENCIES.md).

## Verification that needs real hardware or licences

- Unity editor matrix (`unity-matrix.yml`): add the Unity secrets and run it;
  the commands are in [.github/CI.md](.github/CI.md#manual-run-checklist).
  (The local EditMode test, bridge smoke plus LuaCATS generation, passes in
  Unity 2021.3.45f2, 2022.3.62f3, 6000.3.21f1 and 6000.6.4f1 on macOS arm64.
  2021.3 and 2022.3 need Rosetta 2 there, because their Package Manager server
  is x86_64 only.)
- Device runs with the current sources: the Android APK on a physical device
  and an iOS build on an iPhone. (The Unity 6000.3 Lua 5.5 smoke APK passes on
  an Android 16 emulator with 16 KB pages, and the Unity 6000.6 WebGL player
  passes in headless Chrome; see `Test/MobileSmoke/README.md`.)

## Dependencies and tooling

- OpenHarmony: load the plugin on a device or the emulator, and run
  armeabi-v7a somewhere (`Test/Native/ohos_check.sh` covers x86_64 and
  arm64-v8a with a static build). The x86_64 static binary crashes under
  Rosetta on macOS, so it is only checked on the x86_64 CI runner.

## Editor tests and LuaCATS

- Generator bugs found by the Unity editor tests: "Clear Generated Code"
  passes `/XLua/Gen/` instead of an `Assets/...` path to
  `AssetDatabase.DeleteAsset` (it throws in 6000.6), wrappers are generated
  for `ref struct` (`Span<T>`) parameters (CS0306 in Unity 6.3), a type listed
  twice under `GCOptimize` crashes generation, and `in` parameters are treated
  as `ref`.
- Editor development without generated code: Unity 2021.3+ defines
  `NET_STANDARD_2_0`, which compiles out the Reflection.Emit path, so delegates
  need generated `CSharpCallLua` bridges even in the editor.
- LuaCATS on LuaJIT built with `UINT_ESPECIALLY=ON`: ulong is then a separate
  unsigned kind that `xlua.Int64` does not model. Delegate returns and long
  indexer keys still use the read type.
- WebGL player check in Unity 2021.3 and 2022.3 (the WebGL module is only
  installed for 6000.6 locally).

## Before the repository is public

- Restore the commented push, pull request and schedule triggers in
  `.github/workflows/`.
- Enable CodeQL (needs GitHub Advanced Security while private) and GitHub Pages
  (Settings → Pages → Source: GitHub Actions).
- Protect `main` (no force pushes or deletion) and release tags.
- Ship a versioned UPM package and the `Assets` layout from one source, keeping
  the Unity `.meta` files and native import settings.
- Run `publish.yml` once (it creates a draft prerelease by default) and check
  that rebuilding the native plugins gives identical binaries; today only the
  packaging step is checked for reproducibility.

## Optional features, after the above is stable

- A JSON module, evaluating yyjson: exact 64-bit integers, a null sentinel,
  strict UTF-8, depth and size limits. Behind a build option, so apps that do
  not use it do not pay for its size.
- A signed, versioned script loader built on `SignatureLoader`: a narrow C#
  capability API, a script cache, rollback and error reporting, and a
  `LuaComputed<T>` with dependency invalidation. A normal `LuaEnv` is not a
  sandbox (see [Security.md](Assets/XLua/Doc/Security.md)).
- A Teal `.d.tl` generator that uses the generator's member filters directly,
  not LuaCATS output.
- APK, IPA and WebGL size measurements once player builds run in CI.
