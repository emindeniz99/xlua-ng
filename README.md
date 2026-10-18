# xLua-NG

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.TXT)

Lua scripting for Unity, .NET, and Mono, with calls in both directions between
Lua and C#.

> **Work in progress.** xLua-NG is under active development: APIs, presets and
> native exports still change, and nothing is published (no release, no UPM
> package in a registry). Build from source to try it.

xLua-NG is an independent community-maintained fork of
[Tencent/xLua](https://github.com/Tencent/xLua), not an official Tencent product.
It retains the `XLua` namespace and existing bridge API while updating Lua
runtimes, platform builds, and regression coverage.

English is the sole maintained documentation language of this fork.
[Tencent's original documentation](https://github.com/Tencent/xLua/tree/master/Assets/XLua/Doc)
and [the preserved source snapshot](https://github.com/emindeniz99/xlua-ng/tree/b14ab72dd6bd34293f50c92472e0919dd0b4d677/docs/zh-CN)
remain available for historical reference.

## Runtime and validation status

- Lua **5.5.1** is the default native build. Lua **5.4.9** and **5.3.6**
  can also be selected through CMake.
- The release-free native CI workflow builds all three versions for Linux x64,
  Windows x64, macOS ARM64, Android (four ABIs), and iOS ARM64, and runs an
  ASan/UBSan job on Lua 5.3.6, 5.4.9, 5.5.1 and LuaJIT. All 16 jobs passed
  [run 38809281337](https://github.com/emindeniz99/xlua-ng/actions/runs/38809281337).
- Linux runs the managed bridge suite (`Test/Native/run_managed.py`) in both
  normal and coroutine modes, through reflection, generated and minimized
  generated wrappers. Linux and macOS also run native export, GC/profiler and
  native regression checks.
- Four upstream bridge examples passed on a real Android device with Lua 5.5.1
  and IL2CPP: C# to Lua, Lua to C#, Unity lifecycle callbacks, and UI events.
- A Unity editor matrix is configured for 2021.3, 2022.3, 6.0, 6.3, and 6.6.
  Hosted execution is pending Unity license secrets; configuring the matrix
  does not establish compatibility with every editor version. Its editor
  tests (`Test/UnityMatrix/editor_tests.py`) passed locally on macOS ARM64
  with 2021.3.45f2, 2022.3.62f3, 6000.0.85f1, 6000.3.21f1, 6000.3.26f1,
  6000.6.4f1 and 6000.6.6f1 on 2026-10-16, with generated code and without it,
  and again with 2021.3.45f2, 6000.3.26f1 and 6000.6.6f1 on 2026-10-17 after
  the integer conversion change (see [Test/README.md](Test/README.md#unity-editor-tests)).

The repository ships no prebuilt binaries. Build the native plugin for your
runtime and platform, or take it from a `native-build.yml` artifact, and copy it
into `Assets/Plugins`; the committed `.meta` files already hold each plugin's
import settings. Build the hotfix and signing tools with
`python3 Tools/build_tools.py`. See [native builds](build/README.md), [Lua 5.5 integration](build/LUA55.md),
[patch history](build/LUA_PATCHES.md), and [test coverage](Test/README.md).
Legacy runtimes and optional backends remain in the tree; the matrix above
states what this fork has verified.

## Features

- Call C# methods, properties, operators, delegates, and events from Lua.
- Call Lua functions from C# through delegates and interfaces.
- Replace instrumented C# implementations with Lua implementations using the
  optional hotfix feature.
- Pass configured structs and enums across the bridge without C# GC allocations
  on the generated conversion paths.
- Develop in the editor without generating bindings: the editor builds type
  wrappers and `CSharpCallLua` bridges at run time with Reflection.Emit, under
  both API compatibility levels (.NET Standard 2.1 and .NET Framework). Players
  cannot emit code; generate the bindings for AOT/IL2CPP builds.

## Installation

Copy the required contents of this repository's `Assets` directory into your
Unity project's `Assets` directory, preserving the layout. Replace the native
plugin for your target with the rebuilt plugin you intend to ship.

The [examples](Assets/XLua/Examples/) and [tutorials](Assets/XLua/Tutorial/) are
learning material; exclude unused example content from production projects.
For a different installation directory, see the [FAQ](Assets/XLua/Doc/Faq_EN.md).

### As a UPM package (preview)

xLua-NG can also be installed as a Unity Package Manager package (Unity
2021.3 or later). No package is published yet; build one from this
repository with the native plugins you ship:

```sh
python3 Tools/make_upm_package.py --output ../xlua-package \
    --plugins build/plugin_lua55 --tarball ../com.xlua-ng.xlua-0.0.1-dev.tgz
```

`--plugins` takes the output of the `build/make_*.sh` scripts (a folder with
`Plugins/` inside); `--plugin arm64/libxlua.dylib=PATH` adds a single file.
`--source-gen BUILD_DIR` also builds the xLua source generator and puts it in
the package, which then binds the configuration attributes at compile time
instead of "XLua > Generate Code" (see [MIGRATING.md](docs/MIGRATING.md#source-generator-preview)).

Then, in Unity, open Window > Package Manager, press "+" and choose "Add
package from tarball..." with the `.tgz`, or "Add package from disk..." with
the folder's `package.json`. In `Packages/manifest.json` that is
`"com.xlua-ng.xlua": "file:../com.xlua-ng.xlua-0.0.1-dev.tgz"` (relative to
`Packages`). A tarball install is read-only, like a registry install.

- **The package layout:** `Runtime` compiles into the `XLua` assembly and
  `Editor` into `XLua.Editor` (the asmdefs of
  [`Assets/XLua/Asmdef~`](docs/MIGRATING.md#xlua-in-its-own-assemblies-asmdef-opt-in));
  the native plugins are in `Plugins`, the guides in `Documentation~`, and
  the examples and tutorials are samples you import from the Package
  Manager window.
- **Moving from the Assets layout:** delete `Assets/XLua` (keep
  `Assets/XLua/Gen` only if you keep using Generate Code) and the xLua
  libraries in `Assets/Plugins` before adding the package. The package's
  files have the same GUIDs, so scenes and assets keep their references,
  but two copies of a file conflict. Your scripts must not depend on xLua
  being in Assembly-CSharp; code in an asmdef needs a reference to `XLua`
  (also when the source generator writes bindings into it), and to
  `XLua.Core` only when the code itself uses its types (`LuaScript`, the Lua
  C API).
- **Defines** such as `HOTFIX_ENABLE` and `GEN_CODE_MINIMIZE` go into
  Player Settings or `Assets/csc.rsp`, as before; Unity applies both to the
  package's assemblies.
- **Hotfix:** the injector stays in your project's `Tools/` folder
  (`python3 Tools/build_tools.py`), as in the Assets layout.
- **Not yet in the package:** the `XLua.Core` assembly of the core preset;
  a project that defines `XLUA_CORE_ONLY` compiles none of xLua.

## Quick start

After installing xLua-NG, add this code to a `MonoBehaviour`'s `Start` method:

```csharp
using (var luaEnv = new XLua.LuaEnv())
{
    luaEnv.DoString("CS.UnityEngine.Debug.Log('hello world')");
}
```

Each `LuaEnv` owns a Lua VM. Reuse an environment for ongoing game logic rather
than creating one for every call, and dispose it when its work is finished.

To call a Lua function through a typed delegate, declare the delegate outside
the `MonoBehaviour` method:

```csharp
[XLua.CSharpCallLua]
public delegate double LuaMax(double a, double b);
```

Then bind once and reuse it while the environment is alive:

```csharp
var max = luaEnv.Global.GetInPath<LuaMax>("math.max");
UnityEngine.Debug.Log(max(32, 12));
```

Follow the configuration and generation guides before building an IL2CPP player.
Release Lua-backed delegates and interfaces before disposing their environment.

## Documentation

English guides merge the upstream documentation and are the primary reference.
See the [documentation index and language policy](docs/README.md) for all guides
and historical source references. Use the fork's build and test notes for
runtime-specific validation.

- [Tutorial](Assets/XLua/Doc/XLua_Tutorial_EN.md) and [tutorial source](Assets/XLua/Tutorial/)
- [Configuration](Assets/XLua/Doc/Configure_EN.md)
- [FAQ](Assets/XLua/Doc/Faq_EN.md)
- [API reference](Assets/XLua/Doc/XLua_API_EN.md)
- [Loading Lua sources](Assets/XLua/Doc/Lua_Loaders.md): Resources, StreamingAssets, downloads, AssetBundles, Addressables
- [Hotfix guide](Assets/XLua/Doc/Hotfix_EN.md)
- [Adding or removing Lua libraries](Assets/XLua/Doc/Add_Remove_Lua_Lib.md)
- [Custom code generation](Assets/XLua/Doc/Custom_Generate_EN.md)
- [Build and test artifacts without a release](.github/CI.md)

## Examples

| Example | What it demonstrates |
| --- | --- |
| [Hello world](Assets/XLua/Examples/01_Helloworld/) | Initializing Lua and calling C# |
| [Unity scripting](Assets/XLua/Examples/02_U3DScripting/) | Implementing MonoBehaviour lifecycle behavior in Lua |
| [UI events](Assets/XLua/Examples/03_UIEvent/) | Connecting UI events to Lua callbacks |
| [Lua objects](Assets/XLua/Examples/04_LuaObjectOrented/) | Combining Lua objects with C# |
| [No-GC conversions](Assets/XLua/Examples/05_NoGc/) | Avoiding allocations in configured value-type conversions |
| [Coroutines](Assets/XLua/Examples/06_Coroutine/) | Integrating Lua and Unity coroutines |
| [Async operations](Assets/XLua/Examples/07_AsyncTest/) | Expressing asynchronous operations with Lua coroutines |
| [Hotfix](Assets/XLua/Examples/08_Hotfix/) | Replacing instrumented C# behavior with Lua |
| [Generic methods](Assets/XLua/Examples/09_GenericMethod/) | Calling generic methods |
| [Signature loader](Assets/XLua/Examples/10_SignatureLoader/) | Loading digitally signed Lua scripts |
| [Raw objects](Assets/XLua/Examples/11_RawObject/) | Selecting a boxed C# type for a Lua value |
| [Lua implementations](Assets/XLua/Examples/12_ReImplementInLua/) | Implementing complex value-type behavior in Lua |
| [Async hotfix](Assets/XLua/Examples/14_HotfixAsyncAwait/) | Hotfixing async/await behavior |
| [Lua loaders](Assets/XLua/Examples/15_LuaLoaders/) | Loading Lua from a directory, Resources, an AssetBundle or preloaded StreamingAssets |

## Contributing and language

Contributions from developers of all nationalities are welcome. English is the
sole maintained documentation language and is preferred for issues and pull
requests. Read the [documentation language policy](docs/README.md#documentation-language-policy).
Translate useful technical content before removing redundant documentation;
we do not maintain synchronized multilingual copies.

Do not change executable strings, test fixtures, configuration, or public API
names merely to remove Chinese text. Preserve copyright notices, license
headers, third-party notices, and original author attribution.

Report fork-specific problems in the
[xLua-NG issue tracker](https://github.com/emindeniz99/xlua-ng/issues), including
the Unity version, Lua runtime, target platform, backend, and a reproducer.
Read the FAQ first. Consult [Tencent/xLua](https://github.com/Tencent/xLua) for
upstream community information; this fork operates independently.

## License and attribution

xLua-NG derives from Tencent/xLua. The original Tencent copyright and MIT
license notices are preserved in [LICENSE.TXT](LICENSE.TXT). Embedded runtimes
and native dependencies retain their own license notices; upgrading them
requires checking those obligations as well as runtime compatibility.

The logo used in the documentation (`Assets/XLua/Doc/logo.png` and the website)
is Tencent's original xLua artwork, kept from the upstream project.
