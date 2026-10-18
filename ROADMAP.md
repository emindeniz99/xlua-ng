# Roadmap

Open work for xLua-NG, roughly in priority order. Finished work is in the git
history; current dependency versions are in [DEPENDENCIES.md](DEPENDENCIES.md),
and checked upstream Tencent/xLua issues in [UPSTREAM.md](UPSTREAM.md).

## Verification that needs real hardware or licences

- Unity editor matrix (`unity-matrix.yml`): add the Unity secrets and run it;
  the commands are in [.github/CI.md](.github/CI.md#manual-run-checklist).
  (The local EditMode test, bridge smoke plus LuaCATS generation, passes in
  Unity 2021.3.45f2, 2022.3.62f3, 6000.3.21f1 and 6000.6.4f1 on macOS arm64.
  2021.3 and 2022.3 need Rosetta 2 there, because their Package Manager server
  is x86_64 only.)
- Device runs with the current sources: an iOS build on an iPhone, and an
  Android phone with 16 KB pages. (On 2026-10-18 the Lua 5.5 smoke APKs from
  Unity 6000.3.21f1 and 2021.3.45f2 passed on an Android 16 emulator with
  16 KB pages, with and without generated code and with the four C# feature
  defines, and the Unity 6000.6.4f1 WebGL player passed in headless Chrome.
  The same day the minimal and legacy release APKs and two Development APKs
  from 6000.3.21f1, one with the source generator, passed on a moto g play
  2024 with Android 14; see `Test/MobileSmoke/README.md`.)
- Player smoke runs on Unity 6000.3.26f1 (Android) and 6000.6.6f1 (WebGL):
  the installed editors have no Android or WebGL module, so the 2026-10-18
  runs used 6000.3.21f1 and 6000.6.4f1.
- Unity 2021.3.45f2 packs the Android `libxlua.so` unstripped (1,985,360
  bytes, 640,441 compressed in a release APK; 6000.3 strips it to 372,560 and
  159,185). Decide whether `build/android_common.sh` should ship a stripped
  library (with the unstripped one kept for symbols).
- Source generator in players: Android ARM64 IL2CPP players from 6000.3.21f1
  with Minimal and High stripping passed on 2026-10-18 on the emulator
  (wrappers, closed static bridges in the user assembly,
  `CreatorBySignature`, GCOptimize, the link.xml fragment;
  `Test/MobileSmoke/README.md`), and again from the UPM package (M6), where
  the release size builds put the generator between legacy Generate Code
  and GCM (`build/SIZE.md`). A Development player with the minimal preset
  passed on a phone (moto g play 2024) the same day. Left: release builds on
  a device, players from other Unity versions, iOS and WebGL.
- UPM package (source generator M6, 2026-10-18): `Tools/make_upm_package.py`
  builds it and the editor suite, a hotfix player and the IL2CPP smoke pass
  from it (`Test/README.md`, "UPM package layout"). Left: the owner's choice
  of package name and version, the `XLua.Core` assembly once core C# exists,
  a WebGL build from the package, importing the samples, 2022.3 and 6000.0,
  Linux and Windows editors, and a publish step (none exists; nothing is
  published).
- macOS players: the dylib's import setting is AnyCPU since 2026-10-18,
  and ARM64 and universal Mono players pass on 2021.3 to 6000.6
  (`Test/README.md`, "macOS plugin import settings"). Left: an Intel Mac,
  IL2CPP macOS players, and a deployment target for `make_osx_lua5*.sh`
  and `make_osx_silicon_lua5*.sh`, which set none, so the plugins require
  the build machine's macOS version.
- Obfuscation (source generator milestone M8): an obfuz 4.0.0 build in Unity
  with the generated rule file (Mono, then IL2CPP with High stripping), and
  hotfix after obfuscation; the steps are in
  [source-generator.md](docs/design/source-generator.md#m8-as-implemented-2026-10-18).
  The host check passes: `obfuscation_check.py` renames the generated
  assembly with Cecil and runs it with Lua 5.5.1 and LuaJIT.
- LuaCATS and Validate Setup with the source generator (milestone M10): the
  **XLua > Generate LuaCATS Definitions** and **XLua > Validate Setup** menus in
  a project on the generator, on 2021.3.45f2 and 6000.6, and the IL2CPP size of
  legacy, legacy `GEN_CODE_MINIMIZE` and source-generated bindings for the GCM
  decision; the steps are in
  [source-generator.md](docs/design/source-generator.md#m10-as-implemented-2026-10-18).
  The host checks pass (`luacats_check.py --source-gen`, Lua 5.5.1 and LuaJIT).
- Configuration migration (source generator milestone M9): **XLua > Migrate
  Configuration to Attributes** in the default and the asmdef layout, and the
  editor suite with its lists converted by the tool; the steps are in
  [source-generator.md](docs/design/source-generator.md#m9-as-implemented-2026-10-18).
  The host check passes: `migration_check.py` compares the binding surfaces
  of the examples, a computed `ExampleConfig` and edge cases.
- Windows ARM64: load `Plugins/arm64/xlua.dll` in a Unity 2023.1+ Windows
  ARM64 player (and the Windows on Arm Editor), and run the `windows-arm64`
  job of `build.yml` once. `native-build.yml` built the Lua 5.3.6, 5.4.9,
  5.5.1 and LuaJIT plugins on `windows-11-arm` and passed the native checks on
  2026-10-17 (run 39059626940); no Unity process has loaded them yet.

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
  arm64-v8a with a static build).

## Upstream follow-ups

The P1 items from the [UPSTREAM.md](UPSTREAM.md) sweep are fixed. Left over:

- Lua's C API checks (`XLUA_APICHECK`) are clean on Lua 5.3.6, 5.4.9, 5.5.1
  and LuaJIT, in the legacy, minimal and core presets and with
  `XLUA_LUASCRIPT=ON`. They ran on macOS and under ASan and UBSan on Linux
  ([apicheck-runs.md](docs/research/apicheck-runs.md)). Left: a Windows
  (MSVC) run. The CI sanitizer job runs only the default `VERSIONS` of the
  legacy preset, without the core, minimal, `XLUA_LUASCRIPT` or
  `luajit-uint` runs.
- #1064 (IL2CPP "undeclared label" after hotfix) may share #1003's cause; run
  an IL2CPP build with an injected constructor that has long branches.
- Bridge hardening: `XLUA_LOCK_METATABLES` locks the bridge metatables
  (opt-in; on in the minimal preset). The remaining proposals are in
  [docs/research/bridge-audit.md](docs/research/bridge-audit.md). Items 3
  (zeroed LuaJIT `Integer64` block), 4 (`xlua.cast` checks the object), 6
  (set keeps the tag and `__gc`; get unchanged), 8 (NULL check in
  `csharp_function_wrap`) and 14 (stack room) are done. The `xlua.cast` check applies
  in every preset, legacy included, with no flag (decided 2026-10-17).
- The P2 rows in UPSTREAM.md.
- `unity-matrix.yml` has the `prepare.py --hotfix`, `--csharp12`,
  `--roslyn-sdk` and `--fast-enter-play-mode` variants since 2026-10-18
  ([CI.md](.github/CI.md)), but has not run on hosted runners: the Unity
  licence secrets are missing. The first run checks them, including the
  hotfix legs in the Assets layout, which ran locally only with `--asmdef`.

## Editor tests and LuaCATS

- C# `in` and `ref readonly` parameters: generated code and Lua callbacks
  with them are tested in the Unity editors and, for an interface indexer
  with an `in` key, in the host Roslyn builds (`coverage_cs_dotnet.py`,
  `run_managed.py --source-gen`; Mono's mcs cannot declare real `in`
  members). Hotfix of a `ref readonly` method is not run. Generate Code skips the
  members Roslyn 4.3 (Unity 2021.3 to 6000.3, and conservatively 6000.4/6000.5)
  cannot call (CS0570) with a warning; on 6000.6 a class indexer with a
  `ref readonly` key is generated with a plain argument, which probably does
  not compile below C# 12.
- Editor Reflection.Emit: check Unity 6.0 and an editor with an IL2CPP build
  target selected (type wrapping then uses reflection, delegate emit stays on).
  Emitted interface bridges are tested on the host (Mono and .NET) since
  2026-10-18.
- Emitted delegate and interface bridges (`CodeEmit`) take no lock under
  `THREAD_SAFE` or `HOTFIX_ENABLE`, while generated and source-generated ones
  lock the env; upstream behaviour, Editor and `XLUA_GENERAL` only. A missing
  Lua function raises "attempt to call a nil value" there and "no such
  function X" in generated bridges. An interface with a generic method
  cannot be bridged in any mode (emitted: `TypeLoadException`; Generate Code
  leaves the method out and the bridge does not compile; the source
  generator refuses it with a reason).
- WebGL player check in Unity 2021.3 and 2022.3 (the WebGL module is only
  installed for 6000.6 locally).

- Non-ASCII names outside Unity: .NET Framework on Windows (an XLUA_GENERAL
  host) marshals `string` P/Invoke parameters with the ANSI code page, so
  non-ASCII member and enum names cannot match there (Unity's Mono and IL2CPP
  use UTF-8 on every platform). Fixing it would change DllImport signatures.

## Before the repository is public

- Restore the commented push, pull request and schedule triggers in
  `.github/workflows/`.
- Enable CodeQL (needs GitHub Advanced Security while private) and GitHub Pages
  (Settings → Pages → Source: GitHub Actions).
- Protect `main` (no force pushes or deletion) and release tags.
- Ship a versioned UPM package and the `Assets` layout from one source, keeping
  the Unity `.meta` files and native import settings (`Tools/make_upm_package.py`
  assembles the package from the repository since 2026-10-18; publishing it
  from a release workflow is left).
- Run `publish.yml` once (it creates a draft prerelease by default) and check
  that rebuilding the native plugins gives identical binaries; today only the
  packaging step is checked for reproducibility.

## Design work

The preset design is in [docs/design/presets.md](docs/design/presets.md), and
every action item from the 2026-10-17 research is in
[docs/design/backlog.md](docs/design/backlog.md).

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
