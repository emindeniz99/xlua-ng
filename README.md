# xLua-NG

<!-- badges: start (sync.mjs copies this block to the website's home page) -->
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE.TXT)
[![Lua 5.5.1 (default), 5.4, 5.3, LuaJIT](https://img.shields.io/badge/Lua-5.5.1%20%28default%29%20%7C%205.4%20%7C%205.3%20%7C%20LuaJIT-2C2D72?logo=lua)](#supported-versions-and-platforms)
[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-222222?logo=unity)](#supported-versions-and-platforms)
[![Platforms](https://img.shields.io/badge/platforms-Android%20%7C%20iOS%20%7C%20macOS%20%7C%20Windows%20%7C%20Linux%20%7C%20WebGL%20%7C%20OpenHarmony-informational)](#supported-versions-and-platforms)
[![Status: work in progress](https://img.shields.io/badge/status-work%20in%20progress-orange)](ROADMAP.md)
[![Documentation](https://img.shields.io/badge/docs-website-blue)](https://emindeniz99.github.io/xlua-ng/)
[![native-build](https://github.com/emindeniz99/xlua-ng/actions/workflows/native-build.yml/badge.svg?branch=main)](https://github.com/emindeniz99/xlua-ng/actions/workflows/native-build.yml)
[![unity-matrix](https://github.com/emindeniz99/xlua-ng/actions/workflows/unity-matrix.yml/badge.svg?branch=main)](https://github.com/emindeniz99/xlua-ng/actions/workflows/unity-matrix.yml)
[![analysis](https://github.com/emindeniz99/xlua-ng/actions/workflows/analysis.yml/badge.svg?branch=main)](https://github.com/emindeniz99/xlua-ng/actions/workflows/analysis.yml)
[![docs](https://github.com/emindeniz99/xlua-ng/actions/workflows/docs.yml/badge.svg?branch=main)](https://github.com/emindeniz99/xlua-ng/actions/workflows/docs.yml)
<!-- badges: end -->

Lua scripting for Unity, .NET and Mono, with calls in both directions between
Lua and C#. xLua-NG is an independent, community-maintained fork of
[Tencent/xLua](https://github.com/Tencent/xLua); it is not affiliated with or
endorsed by Tencent. It keeps the `XLua` namespace and the bridge API, and
modernises the rest: current Lua versions, a Roslyn source generator instead
of the code generation menu, presets that leave out what a project does not
use, a sandbox for untrusted scripts, and regression tests for its changes.

> **Work in progress.** APIs, presets and native exports still change, and
> nothing is published: no release, no prebuilt plugins, no UPM package in a
> registry. Build from source to try it. Open work is in [ROADMAP.md](ROADMAP.md).

## What is different from xLua

- **Lua 5.5.1 by default**, with Lua 5.4.9, 5.3.6 and LuaJIT 2.1 as build
  options (upstream ships Lua 5.3.5). 64-bit integers stay lossless on Lua
  5.3 and later.
- **Source-generated bindings.** A Roslyn source generator writes the
  bindings while Unity compiles the scripts; there is no generation step and
  no generated folder. Upstream's **XLua > Generate Code** menu and its
  templates are removed. `GEN_CODE_MINIMIZE` selects the generator's smaller
  wrappers. See [Configuration](Assets/XLua/Doc/Configure_EN.md#the-source-generator).
- **Configuration with attributes**, on types or at assembly level for types
  you do not own. **XLua > Migrate Configuration to Attributes** converts
  upstream's static configuration lists once
  ([Converting configuration lists](Assets/XLua/Doc/Migrating_Configuration.md)).
- **Presets** (`legacy`, `minimal`, `core`) and a build option for every
  optional part, so a project pays in size only for what it uses
  ([below](#presets)).
- **`LuaScript` (preview)**: a separate API for scripts you do not trust, such
  as mods or downloaded rules. It has memory, instruction and time limits and
  no bridge to C#: scripts reach only the functions you register
  ([LuaScript](Assets/XLua/Doc/LuaScript.md)).
- **Assembly definitions by default**: xLua compiles into `XLua.Core` and
  `XLua`, so asmdefs of your own can use it.
  **XLua > Project Layout > Use Assembly-CSharp (no asmdefs)** goes back to
  upstream's layout.
- **A UPM package** built from the repository (preview,
  [below](#as-a-upm-package-preview)).
- **Bridge fixes and opt-in hardening.** Upstream bugs fixed in this fork are
  listed with their issues in [UPSTREAM.md](UPSTREAM.md), and every change in
  behaviour is in [MIGRATING.md](docs/MIGRATING.md). Scripting defines add
  checks to the bridge, such as locked metatables and tag checks on C#
  objects ([opt-in bridge hardening](Assets/XLua/Doc/Security.md#opt-in-bridge-hardening)).
- **Tests.** Managed bridge suites in every binding mode, native checks,
  sanitizers, fuzzing, Lua's and LuaJIT's own test suites, a Unity editor
  suite and player smoke tests ([Test/README.md](Test/README.md)).

Kept from upstream: calling C# from Lua and Lua from C# (delegates,
interfaces, `LuaTable`, `LuaFunction`), hotfix of instrumented C# methods
([Hotfix guide](Assets/XLua/Doc/Hotfix_EN.md)), passing configured structs
without allocations ([GC optimization](Assets/XLua/Doc/GC_Optimization.md)), and running
in the Editor without generated bindings through reflection and
Reflection.Emit.

## Supported versions and platforms

| | Supported | Notes |
| --- | --- | --- |
| Lua | 5.5.1 (default), 5.4.9, 5.3.6, LuaJIT 2.1 | `LuaScript` needs Lua 5.4 or 5.5; WebGL builds Lua 5.5.1 only. [Which version to use](Assets/XLua/Doc/Faq_EN.md#which-lua-version-should-i-use) |
| Unity | 2021.3 LTS to 6000.6 | Editor tests run locally on macOS arm64; the hosted matrix (`unity-matrix.yml`) has not run yet. Details in [Test/README.md](Test/README.md#unity-editor-tests) and [ROADMAP.md](ROADMAP.md) |
| Scripting backends | IL2CPP and Mono | IL2CPP players need the source generator for `[CSharpCallLua]` delegates and interfaces |
| Outside Unity | .NET and Mono applications | [General/README.md](General/README.md) |

Each platform needs a native library built from this repository
([build/README.md](build/README.md)). What has run so far:

| Platform | Native library | Run in a Unity player |
| --- | --- | --- |
| Android | armeabi-v7a, arm64-v8a, x86, x86_64; API 23 and later | IL2CPP players on an emulator (16 KB pages) and on a phone |
| iOS | arm64 device and simulator libraries; iOS 15.0 and later | IL2CPP players in the iOS Simulator; not yet on an iPhone |
| macOS | universal bundle and arm64 dylib; macOS 12.0 and later | Editor, and Mono players (arm64 and universal) |
| Windows | x64, x86, ARM64 | not yet; the x64 and ARM64 libraries pass the native checks in CI |
| Linux | x64 | not yet; the managed and native test suites run on Linux |
| WebGL | compiled by Unity from the sources, Lua 5.5.1 | a Unity 6000.6 player in headless Chrome |
| OpenHarmony | built with the OpenHarmony SDK | not yet; a static smoke test runs on x86_64 and arm64-v8a |

The UWP and Nintendo Switch build scripts come from upstream and are not
verified by this fork.

## Presets

A preset is a set of C# scripting defines and native build options
([presets.md](docs/design/presets.md), sizes in
[SIZE.md](build/SIZE.md#presets)).

| Preset | Select it with | What you get |
| --- | --- | --- |
| `legacy` (default) | nothing | Upstream's feature set and behaviour |
| `minimal` | the define `XLUA_PRESET_MINIMAL` and a native library built with `XLUA_PRESET=minimal` | The bridge without rarely used parts (StreamingAssets loader, `SignatureLoader`, `template`, LuaSocket, `io`, most of `os`, C module loading) and with the bridge hardening checks on |
| `core` | the define `XLUA_CORE_ONLY` and a native library built with `XLUA_PRESET=core` | `LuaScript` only, without the C# bridge (preview) |

How to switch, and what each define changes, is in
[MIGRATING.md](docs/MIGRATING.md#presets).

## Quick start

### 1. Build the native library

The repository ships no binaries. Build the library for each target with the
scripts in `build/` (the tools are pinned in [mise.toml](mise.toml); see
[build/README.md](build/README.md) for Android, iOS, Windows, Linux and the
build options). For the Editor on a Mac:

```sh
XLUA_BUILD_ROOT=../xlua-build build/make_osx_lua55.sh           # Plugins/xlua.bundle
XLUA_BUILD_ROOT=../xlua-build build/make_osx_silicon_lua55.sh   # Plugins/arm64/libxlua.dylib
```

These write `plugin_lua55/Plugins/...` under `XLUA_BUILD_ROOT`. A
`native-build.yml` run also uploads the libraries as artifacts. WebGL has no
prebuilt library: Unity compiles the C sources into the player
([Unity WebGL](build/README.md#unity-webgl)).

### 2. Add xLua to a Unity project

Copy `Assets/XLua` (at least `Core` and `Src`) and `Assets/Plugins` from this
repository into your project's `Assets` folder, then copy the built
`Plugins` folder over `Assets/Plugins`. The committed `.meta` files carry
the plugins' import settings. The [examples](docs/EXAMPLES.md) and the
[tutorial](Assets/XLua/Tutorial/) are learning material; leave them out of a
production project.

Scripts without an asmdef see xLua as before. An asmdef of yours that uses
xLua adds `XLua` and `XLua.Core` to its references; see
[MIGRATING.md](docs/MIGRATING.md#xlua-in-its-own-assemblies-asmdefs-the-default)
for the layout and the way back to Assembly-CSharp.

### 3. Install the source generator

Build it with the .NET SDK pinned in [mise.toml](mise.toml) and install it
next to the `XLua` asmdef:

```sh
python3 Tools/build_source_generator.py --output ../xlua-sourcegen \
    --install <project>/Assets/XLua/Src/Analyzers
```

Then configure the types Lua uses with attributes
([Configuration](Assets/XLua/Doc/Configure_EN.md)). Without the generator,
xLua binds every type through reflection, and an IL2CPP player cannot turn
Lua functions into `[CSharpCallLua]` delegates.

### As a UPM package (preview)

The package `com.xlua-ng.xlua` (Unity 2021.3 or later) holds the C#, the
source generator, the Lua 5.5.1 native plugins of Android, iOS, macOS,
Windows and Linux, the WebGL sources, the guides, and the examples and
tutorial as samples. No version has been released yet; once one is, install
it one of these ways:

- **Tarball:** download `com.xlua-ng.xlua-<version>.tgz` from the
  [releases](https://github.com/emindeniz99/xlua-ng/releases) (check it
  against `SHA256SUMS.txt`), then in Window > Package Manager press "+",
  choose "Add package from tarball..." and pick the file. Keep the file in
  the project, for example in `Packages/`, since the manifest points at it.
- **Git URL:** "+" > "Add package from git URL..." with
  `https://github.com/emindeniz99/xlua-ng.git#upm/v<version>` for one version,
  or `#upm` for the newest. The `upm` branch is created with the first
  release that publishes it ([release.md](docs/design/release.md)).
- **From source:** build the plugins (step 1) and the package yourself:

  ```sh
  python3 Tools/make_upm_package.py --output ../xlua-package \
      --plugins ../xlua-build/plugin_lua55 --source-gen ../xlua-sourcegen \
      --tarball ../com.xlua-ng.xlua-0.0.1-dev.tgz
  ```

  and add the `.tgz` with "Add package from tarball...", or the folder's
  `package.json` with "Add package from disk...". The version comes from
  `VERSION`.

Remove a copied `Assets/XLua` and the xLua libraries in `Assets/Plugins`
first: the package has the same assemblies and GUIDs as the Assets layout,
and two copies conflict ([MIGRATING.md](docs/MIGRATING.md#upm-package-preview)).
Projects that use hotfix also need the injector in their `Tools/` folder:
extract `xlua-tools_<tag>.tgz` from the same release into the project folder.
The samples import into `Assets/Samples/xLua-NG/<version>/` and use uGUI.

### Run Lua from C#

```csharp
using UnityEngine;
using XLua;

[CSharpCallLua]
public delegate double LuaMax(double a, double b);

public class HelloLua : MonoBehaviour
{
    LuaEnv luaEnv;
    LuaMax max;

    void Start()
    {
        luaEnv = new LuaEnv();
        luaEnv.DoString("CS.UnityEngine.Debug.Log('hello from Lua')");

        max = luaEnv.Global.GetInPath<LuaMax>("math.max");
        Debug.Log(max(32, 12));   // 32
    }

    void OnDestroy()
    {
        max = null;   // release delegates bound to Lua before disposing
        luaEnv.Dispose();
    }
}
```

Each `LuaEnv` owns a Lua state. Keep one for the game's lifetime rather than
creating one per call. A `LuaEnv` is not a sandbox: its scripts can reach C#
([Security and sandboxing](Assets/XLua/Doc/Security.md)). The
[tutorial](Assets/XLua/Doc/XLua_Tutorial_EN.md) continues from here.

### Run a script you do not trust

`LuaScript` needs the scripting define `XLUA_LUASCRIPT` and a native library
built with `XLUA_LUASCRIPT=ON`, or the `core` preset for a project without the
C# bridge ([setting up](Assets/XLua/Doc/LuaScript.md#setting-up)):

```csharp
using System;
using XLua;

public static class RewardRules
{
    public static long Reward()
    {
        using (var lua = new LuaScript(new LuaOptions
        {
            MemoryLimit = 4 << 20,                       // bytes
            InstructionLimit = 10000000,                 // VM instructions per call
            TimeLimit = TimeSpan.FromMilliseconds(50),   // wall clock per call
        }))
        {
            lua.Register<long, long>("playerLevel", id => 12);   // the only C# the script can reach
            lua.Load("function reward(id) return playerLevel(id) * 10 end", "rules");
            return lua.Call<long>("reward", 7);                // 120
        }
    }
}
```

A script that exceeds a limit throws a `LuaLimitException`; a script error
throws a `LuaException` with the Lua traceback.

## Migrating from Tencent/xLua

[MIGRATING.md](docs/MIGRATING.md) lists every behaviour change against
upstream and what to do once: install the source generator, convert the
configuration lists, delete the output of Generate Code, and check the asmdef
references. [UPSTREAM.md](UPSTREAM.md) tracks the upstream issues and their
status here.

## Documentation

The guides are on the [documentation website](https://emindeniz99.github.io/xlua-ng/),
built from the Markdown in this repository ([index](docs/README.md)).

- [Tutorial](Assets/XLua/Doc/XLua_Tutorial_EN.md),
  [Configuration](Assets/XLua/Doc/Configure_EN.md),
  [API reference](Assets/XLua/Doc/XLua_API_EN.md),
  [FAQ](Assets/XLua/Doc/Faq_EN.md)
- [Loading Lua sources](Assets/XLua/Doc/Lua_Loaders.md): Resources,
  StreamingAssets, downloads, AssetBundles, Addressables
- [LuaScript](Assets/XLua/Doc/LuaScript.md),
  [Security and sandboxing](Assets/XLua/Doc/Security.md),
  [Hotfix guide](Assets/XLua/Doc/Hotfix_EN.md),
  [Examples](docs/EXAMPLES.md)
- [Native builds](build/README.md), [test suites](Test/README.md),
  [CI without a release](.github/CI.md)
- Decisions in [docs/design](docs/design/), research behind them in
  [docs/research](docs/research/README.md), measurements in
  [SIZE.md](build/SIZE.md)

English is the only maintained documentation language.
[Tencent's original documentation](https://github.com/Tencent/xLua/tree/master/Assets/XLua/Doc)
and [the Chinese sources before the cleanup](https://github.com/emindeniz99/xlua-ng/tree/b14ab72dd6bd34293f50c92472e0919dd0b4d677/docs/zh-CN)
remain available for reference.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) (and [AGENTS.md](AGENTS.md) for the
design rules, commit format and test requirements). Contributions from
developers of all nationalities are welcome; English is preferred for issues
and pull requests. Report problems in the
[issue tracker](https://github.com/emindeniz99/xlua-ng/issues) with the Unity
version, Lua version, preset, platform, scripting backend and a reproducer.
For upstream community information, see [Tencent/xLua](https://github.com/Tencent/xLua);
this fork is developed independently.

## Security

Report vulnerabilities privately, as described in [SECURITY.md](SECURITY.md).

## License and attribution

xLua-NG is released under the MIT license and derives from Tencent/xLua. The
original Tencent copyright and MIT license notices are preserved in
[LICENSE.TXT](LICENSE.TXT). Embedded runtimes and native dependencies keep
their own license notices ([DEPENDENCIES.md](DEPENDENCIES.md)).

The logo used in the documentation (`Assets/XLua/Doc/logo.png` and the website)
is Tencent's original xLua artwork, kept from the upstream project.
