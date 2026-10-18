# Research notes

Background research behind xlua-ng design decisions. Each note records the
date, sources and confidence of its claims. Measurements live next to the
build docs; decisions live in [design](../design/).

| Note | Topic |
|---|---|
| [bridge-audit.md](bridge-audit.md) | Hardening review of the C#↔Lua bridge: state Lua can reach, `XLUA_LOCK_METATABLES`, proposals per preset |
| [32-bit-targets.md](32-bit-targets.md) | Which 32-bit (and Android x86) builds are still worth shipping in 2026 |
| [binding-generation.md](binding-generation.md) | Reflection vs generated wrappers (measured), source generators, ILPostProcessor, other bridges |
| [lua-memory.md](lua-memory.md) | Lua memory and leak tools for Unity bridges; plan for a C#-first diagnostics tool |
| [luau.md](luau.md) | Roblox Luau: sandbox, interrupt, CodeGen, measured size; backend verdict and ideas to copy |
| [obfuscators.md](obfuscators.md) | Unity obfuscators and protectors, where xLua breaks under renaming, support tiers |
| [platform-levels.md](platform-levels.md) | Unity's Android minimums, bionic loader features (APS2, RELR, gnu-hash), HarmonyOS, native plugin variant selection |
| [c17-setup.md](c17-setup.md) | Warnings, hardening, sanitizers and reproducibility for our three C files |
| [focus-creative-games.md](focus-creative-games.md) | zlua (Lua inside libil2cpp), ZenTS, luban, obfuz: what they do and what to copy or avoid |
| [hotfix.md](hotfix.md) | xLua hotfix vs ILRuntime, HybridCLR and InjectFix; store policy; what to improve |
| [hotfix-il2cpp-labels.md](hotfix-il2cpp-labels.md) | Tencent/xLua#1064: a constructor hotfix placed inside a finally handler; IL2CPP's undeclared labels, reproduced on 2021.3 and 6000.3, fixed |
| [puerts.md](puerts.md) | Tencent PuerTS 3.x: binding, packaging, native builds, tests; what to copy and avoid |
| [puerts-js.md](puerts-js.md) | PuerTS JS side: watchdog, debugger in dev builds only, async import, .d.ts generator, one API for several backends |
| [tdr.md](tdr.md) | Tencent TDR: what the shipped wrapper is, schema and wire format, alternatives |
| [json-module.md](json-module.md) | Optional JSON module: yyjson, lua-cjson and forks, rapidjson, simdjson, jsmn/cJSON, pure Lua and C# options; measured size, 64-bit and limits fit, ranking for the bridge presets and core |
| [apicheck-runs.md](apicheck-runs.md) | Lua's C API checks on 5.3.6, 5.4.9, 5.5.1 and LuaJIT, every preset, macOS and Linux ASan: no assertion; the LuaJIT library check |
| [watchdog-lost-stop.md](watchdog-lost-stop.md) | Why the time-limit watchdog lost its stop on AArch64 (`lua_sethook` from another thread), the fix, and the frame-walk race left open |
| [core-stop-latency.md](core-stop-latency.md) | Why `TimeLimit` and `Interrupt` stopped seconds late under an `InstructionLimit` (a lost `hookcount` store), the line-event fix, and `Interrupt` without limits; why stops seemed lost under load (an `Interrupt` before the call, a stop during LuaEnv's compile, LuaEnv callbacks from coroutines) |

Measurements:

- [SIZE.md](../../build/SIZE.md): per-component player size.
- [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md): LINQ, generic sharing, source
  plugins, plugin selection.
- The "Player size" section of [build/README.md](../../build/README.md).

Design: [presets, size flags and the core preset](../design/presets.md).
