# xLua-NG

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.TXT)

Lua scripting for Unity, .NET, and Mono, with calls in both directions between
Lua and C#.

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
- The release-free native CI matrix builds all three versions for Linux x64,
  Windows x64, macOS ARM64, Android (four ABIs), and iOS ARM64. All 15 jobs
  passed the [verified hosted run](https://github.com/emindeniz99/xlua-ng/actions/runs/37694199598).
- Linux runs the 276-test managed bridge suite in both normal and
  coroutine modes. Linux and macOS also run native export and GC/profiler checks.
- Four upstream bridge examples passed on a real Android device with Lua 5.5.1
  and IL2CPP: C# to Lua, Lua to C#, Unity lifecycle callbacks, and UI events.
- A Unity editor matrix is configured for 2021.3, 2022.3, 6.0, 6.3, and 6.6.
  Hosted execution is pending Unity license secrets; configuring the matrix
  does not establish compatibility with every editor version.

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
- Develop in the editor without generating bindings; generate the required
  bridges for AOT/IL2CPP players.

## Installation

Copy the required contents of this repository's `Assets` directory into your
Unity project's `Assets` directory, preserving the layout. Replace the native
plugin for your target with the rebuilt plugin you intend to ship.

The [examples](Assets/XLua/Examples/) and [tutorials](Assets/XLua/Tutorial/) are
learning material; exclude unused example content from production projects.
For a different installation directory, see the [FAQ](Assets/XLua/Doc/Faq_EN.md).

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
