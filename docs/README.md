# xLua-NG documentation

English is the maintained primary language. xLua-NG is an independent community
fork of [Tencent/xLua](https://github.com/Tencent/xLua). Original attribution,
copyright notices, and license obligations remain in force.

## Guides

| Topic | Maintained English guide | Historical Chinese source |
| --- | --- | --- |
| Getting started | [Repository README](../README.md) | [README](zh-CN/README.md) |
| Tutorial | [Tutorial](../Assets/XLua/Doc/XLua_Tutorial_EN.md) | [Tutorial](zh-CN/legacy-assets-doc/XLua教程.md) |
| Configuration | [Configuration](../Assets/XLua/Doc/Configure_EN.md) | [Configuration](zh-CN/legacy-assets-doc/configure.md) |
| API | [API reference](../Assets/XLua/Doc/XLua_API_EN.md) | [API](zh-CN/legacy-assets-doc/XLua_API.md) |
| Troubleshooting | [FAQ](../Assets/XLua/Doc/Faq_EN.md) | [FAQ](zh-CN/legacy-assets-doc/faq.md) |
| Hotfixing | [Hotfix guide](../Assets/XLua/Doc/Hotfix_EN.md) | [Hotfix guide](zh-CN/legacy-assets-doc/hotfix.md) |
| Custom generation | [Code generation](../Assets/XLua/Doc/Custom_Generate_EN.md) | [Code generation](zh-CN/legacy-assets-doc/custom_generate.md) |
| Native extensions | [Lua libraries](../Assets/XLua/Doc/Add_Remove_Lua_Lib.md) | [Lua libraries](zh-CN/legacy-assets-doc/XLua增加删除第三方lua库.md) |
| Struct allocations | [GC optimization](../Assets/XLua/Doc/GC_Optimization.md) | [GC optimization](zh-CN/legacy-assets-doc/XLua复杂值类型（struct）gc优化指南.md) |
| Profiling | [Performance analysis](../Assets/XLua/Doc/Performance_Analysis.md) | [Profiling](zh-CN/legacy-assets-doc/XLua性能分析工具.md) |
| Script signatures | [Signatures](../Assets/XLua/Doc/signature.md) | [Signatures](zh-CN/legacy-assets-doc/signature.md) |
| Custom bytecode | [Compatible bytecode](../Assets/XLua/Doc/compatible_bytecode.md) | [Bytecode](zh-CN/legacy-assets-doc/compatible_bytecode.md) |
| Features | [Features](../Assets/XLua/Doc/features.md) | [Features](zh-CN/legacy-assets-doc/features.md) |

The English guides merge the useful content of the upstream Chinese and English
versions. Historical toolchain instructions remain where they explain the
implementation; they do not establish current platform compatibility. Check
[native builds](../build/README.md), [patch history](../build/LUA_PATCHES.md),
and [test coverage](../Test/README.md) for the fork's tested behavior.

## Language and preservation policy

- Write maintained documentation, code comments, and contributor guidance in English.
- Translate technical meaning and merge useful information before retiring a duplicate.
- Preserve original Chinese documents in [the archive](zh-CN/INDEX.md). They may be
  community-maintained, but must state when their content is outdated.
- Keep Unicode test fixtures, executable identifiers, and meaningful data intact.
- Preserve all copyright, attribution, and third-party licensing requirements.
- Do not describe this fork as an official Tencent product.

## Website

The [Hexo site](source/README.md) is generated from these English guides. Edit the
canonical guides, not generated Markdown or HTML. The archived Chinese site
sources remain available for comparison.
