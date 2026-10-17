# xLua-NG documentation

English is the sole maintained documentation language. xLua-NG is an independent community
fork of [Tencent/xLua](https://github.com/Tencent/xLua). Original attribution,
copyright notices, and license obligations remain in force.

## Guides

| Topic | English guide |
| --- | --- |
| Getting started | [Repository README](../README.md) |
| Migrating from xLua | [Behaviour changes](MIGRATING.md) |
| Tutorial | [Tutorial](../Assets/XLua/Doc/XLua_Tutorial_EN.md) |
| Configuration | [Configuration](../Assets/XLua/Doc/Configure_EN.md) |
| API | [API reference](../Assets/XLua/Doc/XLua_API_EN.md) |
| Loading Lua | [Loading Lua sources](../Assets/XLua/Doc/Lua_Loaders.md) |
| Troubleshooting | [FAQ](../Assets/XLua/Doc/Faq_EN.md) |
| Hotfixing | [Hotfix guide](../Assets/XLua/Doc/Hotfix_EN.md) |
| Custom generation | [Code generation](../Assets/XLua/Doc/Custom_Generate_EN.md) |
| Native extensions | [Lua libraries](../Assets/XLua/Doc/Add_Remove_Lua_Lib.md) |
| Struct allocations | [GC optimization](../Assets/XLua/Doc/GC_Optimization.md) |
| Profiling and memory snapshots | [Performance analysis](../Assets/XLua/Doc/Performance_Analysis.md) |
| Script signatures | [Signatures](../Assets/XLua/Doc/signature.md) |
| Custom bytecode | [Compatible bytecode](../Assets/XLua/Doc/compatible_bytecode.md) |
| Security | [Security and sandboxing](../Assets/XLua/Doc/Security.md) |
| Features | [Features](../Assets/XLua/Doc/features.md) |
| Ecosystem | [Tools and libraries that work with xLua-NG](ECOSYSTEM.md) |

The English guides merge the useful content of the upstream Chinese and English
versions. Historical toolchain instructions remain where they explain the
implementation; they do not establish current platform compatibility. Check
[native builds](../build/README.md), [patch history](../build/LUA_PATCHES.md),
and [test coverage](../Test/README.md) for the fork's tested behavior.

## Design and research

Design decisions that are not implemented yet live in [design](design/), for
example [presets, size flags and the core preset](design/presets.md),
[source-generated bindings](design/source-generator.md),
[the core preset's `LuaScript` API](design/core-luascript.md), and every
action item from the research in [the backlog](design/backlog.md). The
research behind them is in [research](research/README.md): binding
generation, platform levels and TDR. Size measurements are in
[SIZE.md](../build/SIZE.md) and [UNITY_SPIKE.md](../build/UNITY_SPIKE.md).

## Documentation language policy

- English is the sole maintained documentation language for xLua-NG.
- Consolidate existing English documentation into the primary README and documentation structure.
- Translate all useful Chinese-only documentation into English before removing the original Chinese documentation files.
- Translate Chinese code comments into clear, technically accurate English.
- Remove redundant Chinese translations after verifying that no unique technical information is lost.
- Do not maintain synchronized multilingual documentation.
- Accept contributions from developers of all nationalities. English is preferred for issues and pull requests.
- Preserve all copyright notices, license texts, attribution, and required third-party notices.
- Do not remove or modify Chinese string literals, test fixtures, identifiers, or other functional content merely because of their language.
- Link to Tencent's original repository and documentation for historical reference.
- Keep the cleanup focused. Avoid unrelated refactoring and preserve compatibility.

## Historical sources

The maintained English guides consolidate the useful textual content of the
upstream Chinese and English Markdown, website sources, and extracted Word
documents. Historical screenshots and embedded objects remain accessible in
Git history. We do not keep or synchronize a second documentation language in
the current tree.

- [Tencent/xLua](https://github.com/Tencent/xLua)
- [Original upstream documentation](https://github.com/Tencent/xLua/tree/master/Assets/XLua/Doc)
- [Preserved source snapshot before cleanup](https://github.com/emindeniz99/xlua-ng/tree/b14ab72dd6bd34293f50c92472e0919dd0b4d677/docs/zh-CN)

## Website

The [Astro Starlight site](source/README.md) is built from these English guides.
Edit the canonical guides; the site's Markdown and HTML are generated and not committed.
