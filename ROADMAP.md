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

## Upcoming Unity versions

Checked 2026-10-16. Every local editor (2021.3, 2022.3, 6000.3, 6000.6)
compiles scripts with `-langversion:9.0`; 2021.3 to 6000.3 ship Roslyn 4.3.1
(C# 10 at most through `csc.rsp`), 6000.6 ships Roslyn 4.10.0 (C# 12).

- Unity 6.7 LTS (not released yet; Mono, C# 9 by default): add it to
  `unity-matrix.yml` and the local editor runs when it ships, and note the
  Roslyn version it bundles. Try its experimental CoreCLR player and the
  experimental IL2CPP iOS player with the Unity smoke test.
- Unity 7.0 (CoreCLR, .NET 10, C# 14 planned): check Reflection.Emit in the
  CoreCLR editor and players, native callbacks (`MonoPInvokeCallback`, and
  whether function pointers or `UnmanagedCallersOnly` are needed), the
  `NET_*` defines that guard emit, and that `Hotfix` injection still works.
- Projects that raise the C# version with `csc.rsp`: run the editor tests with
  `-langversion:latest` in 6000.6 so generated code is known to compile with
  newer language rules, and check C# 13/14 member shapes (for example
  `params` spans and extension members) in the generator and LuaCATS.

## Dependencies and tooling

- OpenHarmony: load the plugin on a device or the emulator, and run
  armeabi-v7a somewhere (`Test/Native/ohos_check.sh` covers x86_64 and
  arm64-v8a with a static build). The x86_64 static binary crashes under
  Rosetta on macOS, so it is only checked on the x86_64 CI runner.

## Editor tests and LuaCATS

- C# `in` and `ref readonly` parameters: generated code and Lua callbacks
  with them are only tested in the Unity editors (Mono's mcs cannot declare
  real `in` methods), hotfix of a `ref readonly` method is not run, and an
  interface indexer with an `in` key has no test. Generate Code skips the
  members Roslyn 4.3 (Unity 2021.3 to 6000.3, and conservatively 6000.4/6000.5)
  cannot call (CS0570) with a warning; on 6000.6 a class indexer with a
  `ref readonly` key is generated with a plain argument, which probably does
  not compile below C# 12. `unity-matrix.yml` runs neither the
  `prepare.py --csharp12` nor the `--roslyn-sdk` variant.
- Editor Reflection.Emit: check Unity 6.0, an editor with an IL2CPP build
  target selected (type wrapping then uses reflection, delegate emit stays on),
  and interface bridges (`EmitInterfaceImpl` compiles but no test calls it).
- The managed suite fails against a LuaJIT plugin built with
  `UINT_ESPECIALLY=ON` (275 of 298 per mode): the Lua tests expect ulong to
  print without the `U` suffix, and `testGetFuncToDelegate_Step_7_3` throws
  an OverflowException. Decide whether the option is supported, then either
  make the tests runtime-aware or fix the conversion. (LuaCATS already models
  it as `xlua.UInt64`; `luacats_check.py --uint-especially` passes.)
- WebGL player check in Unity 2021.3 and 2022.3 (the WebGL module is only
  installed for 6000.6 locally).

- Non-ASCII names outside Unity: .NET Framework on Windows (an XLUA_GENERAL
  host) marshals `string` P/Invoke parameters with the ANSI code page, so
  non-ASCII member and enum names cannot match there (Unity's Mono and IL2CPP
  use UTF-8 on every platform). Fixing it would change DllImport signatures.
- LuaCATS writes non-identifier field names unquoted (`---@field Şehir
  integer`); `---@field ['Şehir']` may be the better form, as methods already
  get a comment with the `obj['name']` spelling.

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
