# Research notes

Background research behind xlua-ng design decisions. Each note records the
date, sources and confidence of its claims. Measurements live next to the
build docs; decisions live in [design](../design/).

| Note | Topic |
|---|---|
| [binding-generation.md](binding-generation.md) | Reflection vs generated wrappers (measured), source generators, ILPostProcessor, other bridges |
| [luau.md](luau.md) | Roblox Luau: sandbox, interrupt, CodeGen, measured size; backend verdict and ideas to copy |
| [obfuscators.md](obfuscators.md) | Unity obfuscators and protectors, where xLua breaks under renaming, support tiers |
| [platform-levels.md](platform-levels.md) | Unity's Android minimums, bionic loader features (APS2, RELR, gnu-hash), HarmonyOS, native plugin variant selection |
| [focus-creative-games.md](focus-creative-games.md) | zlua (Lua inside libil2cpp), ZenTS, luban, obfuz: what they do and what to copy or avoid |
| [hotfix.md](hotfix.md) | xLua hotfix vs ILRuntime, HybridCLR and InjectFix; store policy; what to improve |
| [puerts.md](puerts.md) | Tencent PuerTS 3.x: binding, packaging, native builds, tests; what to copy and avoid |
| [puerts-js.md](puerts-js.md) | PuerTS JS side: watchdog, debugger in dev builds only, async import, .d.ts generator, one API for several backends |
| [tdr.md](tdr.md) | Tencent TDR: what the shipped wrapper is, schema and wire format, alternatives |

Measurements:

- [SIZE.md](../../build/SIZE.md): per-component player size.
- [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md): LINQ, generic sharing, source
  plugins, plugin selection.
- The "Player size" section of [build/README.md](../../build/README.md).

Design: [presets, size flags and the core preset](../design/presets.md).
