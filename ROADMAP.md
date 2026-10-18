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
- Device runs with the current sources: the Android APK on a physical device
  and an iOS build on an iPhone. (On 2026-10-18 the Lua 5.5 smoke APKs from
  Unity 6000.3.21f1 and 2021.3.45f2 passed on an Android 16 emulator with
  16 KB pages, with and without generated code and with the four C# feature
  defines, and the Unity 6000.6.4f1 WebGL player passed in headless Chrome;
  see `Test/MobileSmoke/README.md`.)
- Player smoke runs on Unity 6000.3.26f1 (Android) and 6000.6.6f1 (WebGL):
  the installed editors have no Android or WebGL module, so the 2026-10-18
  runs used 6000.3.21f1 and 6000.6.4f1.
- Unity 2021.3.45f2 packs the Android `libxlua.so` unstripped (1,985,360
  bytes, 640,441 compressed in a release APK; 6000.3 strips it to 372,560 and
  159,185). Decide whether `build/android_common.sh` should ship a stripped
  library (with the unstripped one kept for symbols).
- Source generator in players: an IL2CPP player (Android ARM64, High
  stripping) with the source generator's wrappers and bridges, including
  `CreatorBySignature` (milestones M1 step 5, M2 item 2, M3 item 1). The
  Editor checks of M1 to M3 passed on 2021.3.45f2, 6000.3.26f1 and
  6000.6.6f1 on 2026-10-18 (`Test/README.md`, "asmdef layout, source
  generator and hotfix players"), and a macOS Mono hotfix player in the
  asmdef layout passed on 2021.3 and 6000.3.
- Unity 6000.6: wrappers of `UnityEngine.PhysicsScene2D` do not compile
  with either generator (CS0121: `GetRayIntersection` is ambiguous between
  `(Ray, float, int)` and the new `(Ray, float, int, Allocator)` overload);
  see build/SOURCEGEN.md, "Unity compile times".
- macOS players from Unity 2021.3: with the shipped `libxlua.dylib.meta`
  (CPU ARM64) the plugin lands in `Contents/PlugIns/ARM64` of an ARM64 Mono
  player, which fails with `DllNotFoundException` (6000.3 loads it). Decide
  whether the macOS import setting should be AnyCPU, and check an x64 and a
  universal player.
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

- Domain reload disabled: every LuaEnv created without generated code emits a
  new `XLuaCodeEmit` dynamic assembly that is never unloaded, so with Enter
  Play Mode Options they add up until the next script reload. Fix with a
  static cache of emitted types in `CodeEmit.cs`.
- A `LUA_USE_APICHECK` build passes the managed suite on Lua 5.5.1 (macOS).
  Not yet run with API checks: Lua 5.3, 5.4 and LuaJIT, and under ASan on
  Linux (`sanitize.sh`). The earlier `lua_pcallk` assertion was a real bug,
  fixed (bridge-audit.md item 14).
- #1064 (IL2CPP "undeclared label" after hotfix) may share #1003's cause; run
  an IL2CPP build with an injected constructor that has long branches.
- Bridge hardening: `XLUA_LOCK_METATABLES` locks the bridge metatables
  (opt-in; to be on in the minimal preset). The remaining proposals are in
  [docs/research/bridge-audit.md](docs/research/bridge-audit.md). Items 3
  (zeroed LuaJIT `Integer64` block), 4 (`xlua.cast` checks the object), 6
  (set keeps the tag and `__gc`; get unchanged), 8 (NULL check in
  `csharp_function_wrap`) and 14 (stack room) are done. The `xlua.cast` check applies
  in every preset, legacy included, with no flag (decided 2026-10-17).
- The P2 rows in UPSTREAM.md.
- Run the `prepare.py --hotfix`, `--csharp12`, `--roslyn-sdk` and
  `--fast-enter-play-mode` variants in `unity-matrix.yml` (today they run
  locally only).

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
