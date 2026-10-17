# Research notes

Background research behind xlua-ng design decisions. Each note records the
date, sources and confidence of its claims. Measurements live next to the
build docs; decisions live in [design](../design/).

| Note | Topic |
|---|---|
| [binding-generation.md](binding-generation.md) | Reflection vs generated wrappers (measured), source generators, ILPostProcessor, other bridges |
| [platform-levels.md](platform-levels.md) | Unity's Android minimums, bionic loader features (APS2, RELR, gnu-hash), HarmonyOS, native plugin variant selection |
| [hotfix.md](hotfix.md) | xLua hotfix vs ILRuntime, HybridCLR and InjectFix; store policy; what to improve |
| [tdr.md](tdr.md) | Tencent TDR: what the shipped wrapper is, schema and wire format, alternatives |

Measurements:

- [SIZE.md](../../build/SIZE.md): per-component player size.
- [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md): LINQ, generic sharing, source
  plugins, plugin selection.
- The "Player size" section of [build/README.md](../../build/README.md).

Design: [presets, size flags and the core preset](../design/presets.md).
