# Roadmap

Open work for xLua-NG, roughly in priority order. Finished work is in the git
history; current dependency versions are in [DEPENDENCIES.md](DEPENDENCIES.md),
and checked upstream Tencent/xLua issues in [UPSTREAM.md](UPSTREAM.md).

## Verification that needs real hardware or licences

Each item says what is left and where the runs that passed are recorded.

- **Unity CI** (`unity-matrix.yml`): waits for the owner to add the
  `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` secrets; the commands
  are in [CI.md](.github/CI.md#manual-run-checklist). Without them its Unity
  steps skip (`b6500f95`), so none of them has run on hosted runners: the
  editor suite with the `--hotfix`, `--csharp12`, `--roslyn-sdk` and
  `--fast-enter-play-mode` variants, and `hotfix-il2cpp`, the Android IL2CPP
  hotfix player on 2021.3.45f2 and 6000.6.6f1 (owner's decision of
  2026-10-18, for [#1064](https://github.com/Tencent/xLua/issues/1064)).
- **The Unity editor suite without Generate Code** (source generator
  milestone M11): passed on 2021.3.45f2, 2022.3.62f3, 6000.0.85f1,
  6000.3.21f1 and 6000.6.6f1 in modes `sourcegen` and `reflection`, on
  6000.3 also with `--package`, `--roslyn-sdk`, `--build-target iOS`, the
  strict minimal preset and the minimal preset with `XLUA_REFLECTION`, on
  6000.6 with `--csharp12` and `--langversion latest` (2026-10-28,
  [Test/README.md](Test/README.md#after-m11-on-every-local-editor-2026-10-28)).
  After the day's merges, main at `a2f8ad12` passed again on 6000.3.21f1
  (`sourcegen`, `reflection`, strict minimal, `--hotfix`, and
  `hotfix_player.py`), 2021.3.45f2 and 6000.6.6f1 (`sourcegen`), with the
  host, container and player checks of the
  [verification round](Test/README.md#verification-round-of-2026-10-28).
  Left: `compile_time.py`; the device examples (`Test/Examples`);
  `--build-target WebGL` (no local WebGL module for 6000.3 or 6000.6); the
  Linux and Windows editors. On macOS arm64, 2021.3 and 2022.3 need Rosetta 2,
  because their Package Manager server is x86_64 only.
- **iOS on an iPhone** (development signing): waits for the hardware. The
  iOS players from the current sources ran only in the iOS 27.1 Simulator,
  from Unity 6000.3.21f1: legacy, minimal Development and release, the source
  generator and core-only
  ([Test/MobileSmoke/README.md](Test/MobileSmoke/README.md#2026-10-18-ios-players-in-the-simulator)).
  The core-only player's iOS size (device SDK, compiled unsigned) was
  measured on 2026-10-19: +242,243 bytes of `.app` zip at Minimal stripping
  and +173,513 at High ([build/SIZE.md](build/SIZE.md#the-core-only-player-on-ios)).
  Unity 2021.3's simulator players are x86_64 only and do not run on the
  arm64-only iOS 27.1 runtime. `native-build.yml` and `publish.yml` build the
  simulator libraries since 2026-10-18 (owner's decision;
  [build/README.md](build/README.md#ios-simulator)); `native-build.yml`'s
  iOS jobs built and checked them in run 39170081830 (2026-10-18, main at
  `d57eb884`); `publish.yml` has not run.
- **An Android phone with 16 KB pages**: waits for the hardware. 16 KB pages
  ran only on the Android 16 emulator (`xlua16k`); the phone runs (moto g
  play 2024, Android 14) have 4 KB pages
  ([Test/MobileSmoke/README.md](Test/MobileSmoke/README.md#recorded-validation)).
- **Player modules that are not installed locally**: Android for
  6000.3.26f1, and WebGL for 6000.6.6f1, 2022.3.62f3 and 2021.3.45f2. The
  2026-10-18 Android and WebGL players used 6000.3.21f1 and 6000.6.4f1.
  Waits for the owner to install the modules (or for the Unity CI).
- **Source generator in players**: Android ARM64 IL2CPP players from
  6000.3.21f1 passed on the emulator (release and Development, Minimal and
  High stripping, the GCM mode, from the UPM package, and after M11), and a
  minimal Development player passed on the moto g play 2024
  ([Test/MobileSmoke/README.md](Test/MobileSmoke/README.md#recorded-validation);
  sizes in [build/SIZE.md](build/SIZE.md)). Left: players from other Unity
  versions, an iPhone (a Development player passed in the iOS Simulator),
  WebGL, and the GCM mode on iOS and WebGL (a release player with the
  generator in GCM mode, the strict minimal one below, passed on the phone
  on 2026-10-28). The strict minimal preset (no reflection fallback, the generator
  binding everything the smoke uses) passed in a release and a Development
  player on the emulator on 2026-10-28
  ([Test/MobileSmoke/README.md](Test/MobileSmoke/README.md#2026-10-28-strict-minimal-players));
  its release player passed again on the emulator and, as the first release
  player of the preset on a phone, on the moto g play 2024 in the
  verification round of the same day
  ([Test/MobileSmoke/README.md](Test/MobileSmoke/README.md#2026-10-28-the-final-verification-round));
  left for it: High stripping, iOS and WebGL.
- **UPM package** (`Tools/make_upm_package.py`, source generator M6): the
  editor suite, hotfix players, the IL2CPP smoke and a Development WebGL
  player in headless Chrome pass from it
  ([Test/README.md](Test/README.md#upm-package-layout-source-generator-m6)).
  Left: the owner's choice of package name and version, importing the
  samples, a release or source generator WebGL build from the package, the
  Linux and Windows editors, and the suite with `--package` since M11 (item
  above). Publishing is under "Before the first release".
- **macOS players**: ARM64 and universal Mono players pass on 2021.3 to
  6000.6 ([Test/README.md](Test/README.md#macos-plugin-import-settings-2026-10-18)),
  and the plugins target macOS 12.0 (`080eaf08`, checked by
  `check_apple_minos.py` since `31e23815`;
  [build/README.md](build/README.md#macos-and-ios-deployment-targets)).
  Left: an Intel Mac and IL2CPP macOS players.
- **Obfuscation** (source generator M8): an obfuz 4.0.0 build in Unity with
  the generated rule file (Mono, then IL2CPP with High stripping), and hotfix
  after obfuscation; the steps are in
  [source-generator.md](docs/design/source-generator.md#m8-as-implemented-2026-10-18).
  The host check passes: `obfuscation_check.py` renames the generated
  assembly with Cecil and runs it with Lua 5.5.1 and LuaJIT.
- **LuaCATS with the source generator in the editor** (M10): M10's Unity
  steps passed on 6000.6.6f1 on 2026-10-28 (`LuaTypes/` equal to
  `Test/LuaCATS/expected`, Validate Setup's label and Generate Code problems;
  [Test/README.md](Test/README.md#after-m11-on-every-local-editor-2026-10-28)),
  and M9's steps 1 and 2 on 2021.3.45f2. The comparison is a script since
  then (`Test/UnityMatrix/luacats_unity.py`) and passed on 2021.3.45f2 and
  6000.3.21f1 with `Sample.cs` without its `ref readonly` members
  (`LUACATS_NO_REF_READONLY`, `Test/LuaCATS/expected-no-ref-readonly`) and
  on 6000.6.6f1 with them
  ([Test/README.md](Test/README.md#luacats-from-the-editor-and-the-strict-minimal-player-2026-10-28)).
  Left: 2022.3 and 6000.0, the Linux and Windows editors, and adding the
  script to `unity-matrix.yml`.
- **Windows ARM64**: load `Plugins/arm64/xlua.dll` in a Unity 2023.1+ Windows
  ARM64 player (and the Windows on Arm Editor), and run the `windows-arm64`
  job of `build.yml` once. `native-build.yml` built the Lua 5.3.6, 5.4.9,
  5.5.1 and LuaJIT plugins on `windows-11-arm` and passed the native checks on
  2026-10-17 (run 39059626940); no Unity process has loaded them yet.
- **Windows hosts** (`windows-host.yml`, since 2026-10-28): the managed suite
  on the .NET Framework and on Mono 6.12.0 for Windows, the MSVC
  `XLUA_APICHECK` builds of Lua 5.3.6, 5.4.9, 5.5.1 and LuaJIT (GC64 and
  not) and errors through C# callbacks on LuaJIT Win64 pass (run
  40645581045; [windows-host.md](docs/research/windows-host.md)). The runs
  confirmed [#1081](https://github.com/Tencent/xLua/issues/1081) on the .NET
  Framework and fixed `luaL_newmetatable`'s ANSI import and `xlua_gl` on
  LuaJIT without GC64. The owner then dropped that mode (question 2, option
  c): every 64-bit LuaJIT is GC64, `make_win64_luajit.bat` included, and
  `xlua_gl` reads `G(L)` again. Left: owner questions 1 and 3 in
  windows-host.md (the `[CSharpCallLua]` scan and dynamic assemblies,
  `NATIVE_LUA_PUSHSTRING`); the source generator passes, .NET (Core)
  and the console code page of #1179 on Windows; and a `gh workflow run` of
  the workflow from `main` (until the merge it ran from pushes to
  `ci/windows-host`).

## Upcoming Unity versions

Checked 2026-10-16, 6000.0 on 2026-10-18. Every local editor (2021.3,
2022.3, 6000.0, 6000.3, 6000.6) compiles scripts with `-langversion:9.0`;
2021.3 to 6000.3 ship Roslyn 4.3.1 (C# 10 at most through `csc.rsp`), 6000.6
ships Roslyn 4.10.0 (C# 12).

- Unity 6.7 LTS (not released yet; Mono, C# 9 by default): add it to
  `unity-matrix.yml` and the local editor runs when it ships, and note the
  Roslyn version it bundles. Try its experimental CoreCLR player and the
  experimental IL2CPP iOS player with the Unity smoke test.
- Unity 7.0 (CoreCLR, .NET 10, C# 14 planned): check Reflection.Emit in the
  CoreCLR editor and players, native callbacks (`MonoPInvokeCallback`, and
  whether function pointers or `UnmanagedCallersOnly` are needed), the
  `NET_*` defines that guard emit, and that `Hotfix` injection still works.
- Projects that raise the C# version with `csc.rsp`: the editor tests with
  `-langversion:latest` passed in 6000.6.6f1 with the source generator
  (`prepare.py --langversion latest`, 2026-10-28). Left: check C# 13/14
  member shapes (for example `params` spans and extension
  members) in the generator and LuaCATS.

## Dependencies and tooling

- OpenHarmony: load the plugin on a device or the emulator, and run
  armeabi-v7a somewhere (`Test/Native/ohos_check.sh` covers x86_64 and
  arm64-v8a with a static build).

## Upstream follow-ups

The P1 items from the [UPSTREAM.md](UPSTREAM.md) sweep are fixed. Left over:

- Lua's C API checks (`XLUA_APICHECK`) are clean on Lua 5.3.6, 5.4.9, 5.5.1
  and LuaJIT, in the legacy, minimal and core presets and with
  `XLUA_LUASCRIPT=ON`, on macOS and under ASan and UBSan on Linux
  ([apicheck-runs.md](docs/research/apicheck-runs.md)), and with MSVC on
  Windows since 2026-10-28 (`windows-host.yml`, legacy preset only).
  Decided 2026-10-18 by the owner: a second CI sanitizer job
  (`sanitizers-extra`) runs the core, minimal, `XLUA_LUASCRIPT` and
  `luajit-uint` runs, and a `webgl` job compiles the WebGL source with
  Emscripten 4.0.20 (`.github/CI.md`). Both sanitizer jobs failed in their
  first hosted run, 39170081830 (2026-10-18); the causes were fixed in
  `9c166e9d`, and every native-build run since 39176832606 (2026-10-19)
  passed them.
- [#1064](https://github.com/Tencent/xLua/issues/1064) (IL2CPP "undeclared
  label" after hotfix) is fixed and tested on Android IL2CPP with 2021.3 and
  6000.3 ([hotfix-il2cpp-labels.md](docs/research/hotfix-il2cpp-labels.md)).
  Left: `hotfix_il2cpp.py` on iOS, 2022.3, 6000.6 and a phone; the CI job is
  under "Unity CI" above.
- Bridge hardening: every item of
  [bridge-audit.md](docs/research/bridge-audit.md#summary) is done or has a
  design. Left are owner decisions, listed in
  [backlog.md](docs/design/backlog.md#correctness-and-security): whether the
  three checks become unconditional, read-only class tables and the enum
  cache in class tables. The type allowlist is not planned for now (owner,
  2026-10-18).
- The P2 rows of UPSTREAM.md that are not closed:
  [#741](https://github.com/Tencent/xLua/issues/741) (fixed, needs a WebGL
  player that runs finalizers during an allocation),
  [#1179](https://github.com/Tencent/xLua/issues/1179) (its console code page
  part; the ANSI imports are fixed and checked on the .NET Framework,
  2026-10-28). [#1149](https://github.com/Tencent/xLua/issues/1149) is
  fixed (2026-10-28, owner decision; reproduced under ASan, see
  [crash-inventory.md](docs/research/crash-inventory.md#11-the-old-walker-1149)).
  [#999](https://github.com/Tencent/xLua/issues/999) is fixed (2026-10-19).
  [#1122](https://github.com/Tencent/xLua/issues/1122) (debug symbols after
  injection, with an obfuscator) waits for the obfuscation run above.

## Editor tests and LuaCATS

- C# `in` and `ref readonly` parameters: hotfix of plain and virtual methods
  with them (every hotfix mode) and Lua callbacks with them (delegates and an
  interface, generated and emitted bridges, Mono and .NET) are tested on the
  host since 2026-10-19, and the hotfix also in Unity 6000.6.6f1
  (`hotfix_intkey.py --csharp12`;
  [Test/README.md](Test/README.md#in-and-ref-readonly-parameters-2026-10-19)).
  Injecting the virtual ones failed before
  ([MIGRATING.md](docs/MIGRATING.md#tooling)). The editor suite with
  `--csharp12` (6000.6.6f1), and with `--roslyn-sdk` on 6000.3.21f1, where
  `ref readonly` methods with `modreq` parameters go through reflection,
  passed on 2026-10-28. Left: `hotfix_intkey.py` on an editor with Roslyn
  4.3.1 (`in` only).
- Done (2026-10-19, owner decisions): emitted delegate and interface
  bridges (`CodeEmit`) lock the env under `THREAD_SAFE` or `HOTFIX_ENABLE`,
  as the generated and source-generated ones do (`CaseBridgesOnThreads`); a
  missing Lua function raises "no such function X" in every binding mode
  (`CaseInterfaceBridgeMissingFunction`); an interface with a generic method
  cannot be bridged in any mode, and the emitted bridge now says so with an
  `InvalidCastException` that names the method instead of a
  `TypeLoadException` (`CaseInterfaceBridgeGenericMethod`; the source
  generator refuses it with a reason).

## Before the first release

The repository is public since 2026-10-19: the CI triggers are back, CodeQL
uploads to code scanning, `main` and `v*` tags are protected by rulesets, and
secret scanning, push protection and private vulnerability reporting are on
(`.github/CI.md`). Left before a first release, which the owner decides:

- Release: prepared on 2026-10-19, not run. `publish.yml` builds every
  native plugin, the UPM package (`make_upm_package.py --release`: the source
  generator, the Lua 5.5.1 plugins of every platform with the `minimal`,
  `core` and simulator variants, samples, licences) and the hotfix tools,
  checks them, and creates the release `v<VERSION>` only with the input
  `release`, the `upm` branch for git URL installs only with `upm_branch`.
  The steps, the inputs and what to check afterwards are in
  [release.md](docs/design/release.md). The package from a local tarball
  passed the editor suite on 2021.3.45f2 and 6000.3.21f1 and an IL2CPP
  Android player on the emulator (`Test/README.md`, "UPM package layout").
  Left: a dry run of `publish.yml` on hosted runners (its reproducibility
  jobs have not run; locally Android, macOS and iOS rebuilt to the same
  bytes), then the owner's version bump and release run.
- Left out of the first package, each written up in release.md: UWP (its
  scripts need Visual Studio 2019), the hotfix tools inside the package (a
  separate release file for now), `LuaScript` in the Editor from the package
  (the default plugins lack `XLUA_LUASCRIPT`; core-luascript.md question 16),
  and stripping the macOS plugins (7% smaller, needs re-signing).
- Owner's call: Windows x64 and x86 LuaJIT build again since 2026-10-28
  (`make_win64_luajit.bat` and `make_win32_luajit.bat` use the newest
  Visual Studio and stop at a failure; `build.yml` run 40671930012 built
  and checked both), so the next `publish.yml` run puts them into
  `luajit_<tag>.tgz`. Keep them in the first release or exclude them
  (release.md, "Open for the owner"). Not checked in a Unity player or
  Editor.
- Unity CI: add the `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD`
  secrets (the matrix skips without them), and `RENOVATE_TOKEN` for Renovate.

## Design work

The preset design is in [docs/design/presets.md](docs/design/presets.md), and
every action item from the 2026-10-17 research is in
[docs/design/backlog.md](docs/design/backlog.md).

## Optional features, after the above is stable

- **A descriptor wrapper mode**, an opt-in size mode next to GCM: about
  445 KB less APK than GCM at game size (264 types, 4,308 Lua functions),
  at about +50 ns per call over GCM on the moto g play 2024 (+11% over the
  hot-path mix; boxed structs and class arguments get faster). **Owner's
  decision of 2026-10-29 (question 81, option b): decide after the first
  release.** The 2-day experiment on the per-call overhead before that is
  done ([descriptor-wrappers.md](docs/research/descriptor-wrappers.md#per-call-overhead-on-the-phone-2026-10-29)):
  a smaller hot path in the prototype's core took the gap for a static call
  with two integers from +48 to +35 ns (+60 to +51 ns over the mix); the
  rest is structural, so the work stopped there, as the owner asked. If the
  mode is chosen, the full version:
  1. **Per-call cost first** (about 2 days, then the gate): the second
     dispatch, the general argument loop and the buffer round trip are
     what is left. Try argument readers generated per signature shape and
     shared by the members of that shape, or stubs that read their own
     arguments; both give back part of the size (about 100 bytes of APK
     per Lua function against GCM). Measure each candidate in more than one
     build: code placement moves a call by 10 to 35 ns between builds on
     the phone.
  2. **The member kinds still missing** (the prototype leaves them out in
     every mode): `out`, `ref` and `in` parameters, `params` arrays,
     events, indexers, operators, generic and extension methods, delegate
     parameters, `byte[]`, `IntPtr`, `decimal`, `Nullable`,
     `LuaTable`/`LuaFunction` parameters and results, more than 8
     arguments and more than 10 results, and enum wrappers (GCM's for now).
     About 3 to 4 days.
  3. **The runtime core** (about 2 days): a buffer per translator with its
     references cleared after a call; the rules for nested calls,
     coroutines and an exception in the middle of a call, written down and
     tested; GCM's error texts; the struct `self` paths.
  4. **Mode selection** (about 3 days): an opt-in define or generator
     option, a per-type way back to GCM for the types a game calls every
     frame, diagnostics (an XLSG013-style mismatch check), the generator's
     cache key and manifest, MIGRATING.md, and the preset question.
  5. **Tests** (about 3 to 4 days): generator unit tests, a descriptor pass
     in `run_managed.py` (the Lua suite and the surface, bridge, hotfix and
     struct diffs), `hotfix_check.py`, the Unity editor suite, IL2CPP
     players on the emulator and the phone, and the size run again. Docs:
     about 1 day.

  Effort: about 15 working days, plus the design work of step 1 if the
  cost must come down further. Open risks: step 1 may not reach about 20
  ns per call; small gains are hard to prove when builds alone move a call
  by 10 to 35 ns; a fourth binding mode costs test and CI time in every
  suite; the shared buffer's re-entrancy (nested calls, coroutines,
  `THREAD_SAFE` envs); the prototype's pointer stores into the argument
  buffer rely on IL2CPP keeping the declared field order (Mono and CoreCLR
  may reorder a struct that holds references); the size gain is measured on
  Android with Minimal and High stripping only, not on iOS or WebGL. The
  prototype is on the branch `spike/descriptor-wrappers` (not for merging);
  the experiment's copies of its core are in `overhead-2026-10-29/` on the
  build disk, and the backlog row is in [backlog.md](docs/design/backlog.md).

- A JSON module: decided 2026-10-18 by the owner, no native module (yyjson
  would cost 38 to 104 KB, our own parser risks bugs). The FAQ recommends
  pure-Lua libraries (dkjson, lunajson) loaded as ordinary modules; kept here
  in case a native fast path is wanted later
  ([json-module.md](docs/research/json-module.md)).
- A signed, versioned script loader built on `SignatureLoader`: a narrow C#
  capability API, a script cache, rollback and error reporting, and a
  `LuaComputed<T>` with dependency invalidation. A normal `LuaEnv` is not a
  sandbox (see [Security.md](Assets/XLua/Doc/Security.md)).
- A Teal `.d.tl` generator that uses the generator's member filters directly,
  not LuaCATS output.
- APK, IPA and WebGL size measurements once player builds run in CI.
