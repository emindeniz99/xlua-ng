# Obfuscators and protectors for Unity, and xLua

Researched 2026-10-17. Read-only clones: ConfuserEx2 (`657fb58`), ConfuserEx
(yck1509), Obfuscar (`7bc2c77`), the obfuz xLua sample and obfuz itself
(`6912a92`, v4.0.0). No tool was run. Confidence: **[C]** confirmed in
source or primary docs, **[S]** vendor or secondary claim, **[U]**
unconfirmed.

## Where xlua-ng breaks under renaming

These runtime name lookups were found in xlua-ng's code. **[C]**

| Site | Lookup |
|---|---|
| Metatable keys | `Type.FullName` at `Utils.cs:729,938,942,1093,1097` |
| `CS.X` resolution | `ObjectTranslator.FindType` → `assembly.GetType(name)` (`ObjectTranslator.cs:765-769`) |
| Delegate fallback | Scans the bridge for methods whose names start with `__Gen_Delegate_Imp` (`ObjectTranslator.cs:464-468`) |
| CodeEmit | `GetMethod("ThrowExceptionFromError")` and similar (`CodeEmit.cs:41-64`) |
| Field-mode hotfix | `xlua.access(cs, '__Hotfix0_'..k)` resolves fields by name (`LuaEnv.cs:555-575`, `StaticLuaCallbacks.cs:996-1006`) |
| IntKey hotfix | `hotfix_id_map[typeof(cs):ToString()]` (`Resources/xlua/util.lua.txt:84`). The map uses original names but the lookup uses the obfuscated one. A miss falls back to field mode, which fails as well. |
| Injection guard | A type named `__XLUA_GEN_FLAG` (`Hotfix.cs:897`). Renaming it allows double injection ([xLua#526](https://github.com/Tencent/xLua/issues/526)). |

**What survives renaming:**

- **Generated wrappers:** they register member names as literals, so member
  names survive renaming. **[C]**
- **The callback attribute:** xlua-ng declares its own
  `XLua.MonoPInvokeCallbackAttribute` (`ObjectTranslator.cs:48`). IL2CPP
  finds that attribute by name. A renamer that does not special-case the
  type name breaks every Lua→C# callback. obfuz special-cases it; others may
  not. **[C/U]**

**Assembly-CSharp:** xlua-ng has no asmdef today, so XLua compiles into
Assembly-CSharp. In the obfuz xLua sample, "obfuscate Assembly-CSharp"
renamed 187 of 206 `XLua.*` types, including
`DelegateBridge.__Gen_Delegate_Imp0`. **[C]**

**Maintainer guidance** ([xLua#271](https://github.com/Tencent/xLua/issues/271)):
use IntKey, inject before obfuscating, and refer to private members by their
obfuscated names in Lua patches. xlua-ng injects at `IPostBuildPlayerScriptDLLs`
order 0, which is before obfuz (10000) and GuardingPear (recommended
`int.MaxValue`). **[C/S]**

**Beebyte:** running Beebyte after injection failed with
`SymbolsNotMatchingException` ([xLua#1122](https://github.com/Tencent/xLua/issues/1122),
open). xlua-ng rewrites symbols when it can read them (`Hotfix.cs:851-885`),
but a test is needed to show that this closes #1122. **[C/U]**

## Tools

| Tool | Status | Price | When it runs | What it changes | Mapping | Honours `[Obfuscation(Exclude)]` |
|---|---|---|---|---|---|---|
| **GuardingPearSoftware Obfuscator Free/Pro** | Most active Asset Store option; v2026.7.2 (2026-10-16); Pro has 63 reviews at 5★ | Free / $99.99 | `IPostBuildPlayerScriptDLLs`; run last (`int.MaxValue`) | Types, members, parameters. Pro adds namespaces, strings, Mono-only control flow and global-metadata encryption (beta) | JSON (file or HTTP) | Yes, plus its own attributes and a heuristic that keeps names used as string literals **[S]** |
| **Beebyte Obfuscator** | Deprecated (not sold); 202 reviews; installed base remains | was paid | `PostProcessScene` | Names, literals, fake code | `nameTranslation.txt` | Yes, plus `[Skip]`, `[SkipRename]` **[C]** |
| **obfuz** | 822★, v4.0.0 (2026-09); the HybridCLR ecosystem's choice | MIT | `IPostBuildPlayerScriptDLLs` 10000; also rewrites link.xml | Symbols, constants, fields, calls, control flow; opt-in assembly list | `symbol-mapping.xml`; runtime `ObfuscationTypeMapper` (types only) | **No**; uses `[ObfuzIgnore]` and rule XML **[C]** |
| **Mfuscator** | v2.1.0 (2026-10) | $79.99 | Build | IL2CPP metadata encryption and export renaming; no IL renaming | Not needed | Not applicable **[S]** |
| **Code Stage Anti-Cheat Toolkit** | Very popular (375 reviews) | $80 | Runtime | Not an obfuscator: obscured values, cheat detectors, code hashes | Not applicable | Not applicable |
| **ConfuserEx2** | 2.9k★, last release 2022 | MIT | External CLI | Rename and more; only metadata-level passes survive IL2CPP | `symbols.map` | Yes **[C]** |
| **Obfuscar** | 3.2k★, active 2026-09 | MIT | CLI or .NET tool | Rename, string hiding | `Mapping.txt`/`.xml` | Yes **[C]** |
| **BitMono** | 568★, 0.45.0 (2026-09) | MIT | Unity integration | IL2CPP-aware renaming and strings; metadata encryption | not checked | not checked |
| Dotfuscator, .NET Reactor, Eazfuscator, ByteHide | Commercial | – | Varies | IL2CPP support is unclear or limited; native and virtualisation passes cannot survive IL2CPP | map files | Mostly yes; ByteHide uses different semantics **[S]** |

**IL2CPP rule:** only transforms that change managed metadata or IL before
il2cpp.exe survive: renaming, and string encryption with managed decryptors.
Packing, `calli`, IL virtualisation and native passes do not. **[S]**

## Metadata and native protectors (mostly China)

The following work on the final package or the metadata file and leave names
intact:

- Tencent ACE (metadata encryption, AssetBundles, anti-cheat);
- NetEase Yidun;
- Virbox Protector ($899/yr; Mono method encryption and IL2CPP metadata);
- FairGuard;
- DIY libil2cpp patches that encrypt `global-metadata.dat`.

They are transparent to xLua's name lookups. **[S/U]** Two caveats:

- **DIY patches:** they conflict with HybridCLR, which replaces libil2cpp
  too. **[U]**
- **Lua-script protection:** vendors that protect Lua scripts typically hook
  `luaL_loadbuffer`-style exports or ship their own Lua build. xlua-ng's
  minimal and core presets cut the export list, which could break such hooks.
  This needs documenting. **[S/U]**

## What big studios do

Public evidence comes from reverse-engineering tools, not studio talks. The
pattern is IL2CPP, plus custom `global-metadata.dat` encryption, plus native
protection or anti-cheat:

- miHoYo: metadata encryption and VMProtect;
- Arknights and Call of Duty: Mobile: XOR-encrypted metadata;
- Garena Free Fire and Riot's mobile titles: metadata and string
  encryption. **[C, Il2CppInspector README]**

Renaming also appears: a Beebyte deobfuscator lists Among Us, Phasmophobia
and Free Fire as targets. **[S]** Every dumper notes that dumping memory
bypasses static encryption. **[C]**

## Other bridges

- **tolua:** registers types by runtime `t.Name`, so type renaming breaks
  it. Member names are literals and survive. **[C]**
- **PuerTS:** resolves types with `Type.GetType` by string, so type renaming
  breaks it. **[C]**
- **HybridCLR:** obfuscating hot-update DLLs is fine
  ([hybridclr#61](https://github.com/focus-creative-games/hybridclr/issues/61)).
  obfuz needs `obfuz4hybridclr` alongside it. **[C]**
- **link.xml:** a name-based `link.xml` does not survive renaming unless the
  obfuscator rewrites it, which obfuz does; GuardingPear claims to. That
  affects the link.xml written by xlua-ng's generator. `[Preserve]` survives.
  **[C/S/U]**

## Recommendation

1. **Compile-time name table.** The source generator records original names
   as literals plus `typeof`, registered through
   `[RuntimeInitializeOnLoadMethod]`. Route **every** site in the first table
   through it: metatable keys, `FindType`, `xlua.hotfix`, `hotfix_id_map` and
   `xlua.access` for `__Hotfix` fields. Caveat: GuardingPear may then keep
   every name it finds as a literal, which is correct but weaker obfuscation.
2. **`[Obfuscation(Exclude)]` in two directions, but not alone, because
   obfuz ignores it.**
   - **Emit it:** `[assembly: Obfuscation(Exclude=true, ApplyToMembers=true)]`
     on the XLua assembly (needs the XLua asmdef), and on injected
     `__HotfixN_*` fields and the flag type.
   - **Read it at Editor time:** together with `ObfuzIgnore`,
     `DoNotRename`/`DoNotObfuscateClass` and Beebyte `Skip`/`SkipRename`,
     matched by attribute full name. Its `StripAfterObfuscation` default
     removes the attribute at run time.
3. **Use `AOT.MonoPInvokeCallbackAttribute` under Unity** instead of
   xlua-ng's own class, so that no renamer can break callbacks.
4. **`IXLuaNameMapper`:** maps Type to original name only, which is all
   obfuz exposes; a member map would undo the obfuscation. Add an Editor-side
   `IObfuscatorAdapter` with three jobs:
   - check the build order;
   - answer `IsExcluded(member)`;
   - export exclusion rules from GenConfig and HotfixConfig.

**Support tiers:**

| Tier | Tools | What we provide |
|---|---|---|
| Code adapter plus CI test | obfuz | Runtime mapper; a generated rule XML using `hasCustomAttributes` for `LuaCallCSharp`, `Hotfix` and `CSharpCallLua`; `__Hotfix*` fields excluded. Optionally a cheap Obfuscar rename pass in the native test suite. |
| `ObfuscationAttribute` plus docs | GuardingPear, Beebyte (and #1122), ConfuserEx, Obfuscar, commercial .NET tools | Emitted attributes and exclusion guides |
| Docs only | ACE, Yidun, Virbox, FairGuard, Mfuscator, GuardingPear metadata beta, DIY libil2cpp | Names are unaffected; the HybridCLR conflict; which native symbols each preset exports; compute code hashes after the final build |

Hotfix guidance: prefer IntKey, inject before obfuscating (already the
case), and exclude members that Lua patches reach by name.

## Sources

- obfuz: [repo](https://github.com/focus-creative-games/obfuz), [xLua guide](https://www.obfuz.com/en/docs/manual/xlua/work-with-xlua), [sample](https://github.com/focus-creative-games/obfuz-samples/tree/main/WorkWithXLua)
- xLua issues: [#271](https://github.com/Tencent/xLua/issues/271), [#526](https://github.com/Tencent/xLua/issues/526), [#610](https://github.com/Tencent/xLua/issues/610), [#1122](https://github.com/Tencent/xLua/issues/1122)
- GuardingPear: [attributes](https://docs.guardingpearsoftware.com/manual/Obfuscator/Attributes.html), [global metadata](https://docs.guardingpearsoftware.com/manual/Obfuscator/Guides/6_GlobalMetadata/GlobalMetadata.html)
- Asset Store: [Beebyte](https://assetstore.unity.com/packages/tools/utilities/obfuscator-48919), [Obfuscator Pro](https://assetstore.unity.com/packages/tools/utilities/obfuscator-pro-plug-and-play-code-protection-v2026-89589), [Obfuscator Free](https://assetstore.unity.com/packages/tools/utilities/obfuscator-free-89420), [Mfuscator](https://assetstore.unity.com/packages/tools/utilities/mfuscator-il2cpp-encryption-256631), [Anti-Cheat Toolkit](https://assetstore.unity.com/packages/tools/utilities/anti-cheat-toolkit-2024-202695)
- [ConfuserEx2](https://github.com/mkaring/ConfuserEx), [Obfuscar](https://github.com/obfuscar/obfuscar), [BitMono IL2CPP notes](https://docs.bitmono.dev/en/latest/_sources/developers/il2cpp-compatibility.rst.txt), [Il2CppInspector](https://github.com/djkaty/Il2CppInspector), [Beebyte-Deobfuscator](https://github.com/OsOmE1/Beebyte-Deobfuscator)
- [Tencent ACE](http://intl.anticheatexpert.com/products/code-encryption-ios/), [Virbox](https://appshield.virbox.com/unity3d-protect.html), [FairGuard](https://fairguard.github.io/2024/08/15/Unity%E5%BC%95%E6%93%8E%E5%8A%A0%E5%AF%86%E6%96%B9%E6%A1%88%E8%A7%A3%E6%9E%90)
- [callbackOrder](https://docs.unity3d.com/ScriptReference/Build.IOrderedCallback-callbackOrder.html), [ObfuscationAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.obfuscationattribute)
