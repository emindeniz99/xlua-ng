# Binding-mode parity (2026-10-28)

xLua binds a C# type to Lua in one of four ways: reflection wrappers
(`Utils.ReflectionWrap`, the IL2CPP path without generated code and every
type without `[LuaCallCSharp]` in a project with generated code), emitted
wrappers (`CodeEmit`, the Editor and `XLUA_GENERAL` on Mono), and the source
generator's wrappers, plain or `GEN_CODE_MINIMIZE`. The same Lua call should
give the same result and the same error in all four. This note records the
differences the 2026-10-18 coverage work left
([backlog.md](../design/backlog.md), "Binding-mode differences left after
2026-10-18"), the ones a search across the modes found next to them, their
fixes, and what is left for the owner.

Marks: [C] confirmed here by a test or a run; [U] inferred from the code.

## Method

- Setup: macOS 27 arm64, Mono 6.14.1, .NET SDK 10.0.401, host plugins built
  from this branch for Lua 5.5.1 and LuaJIT 2.1, legacy and minimal presets.
- Fixtures (`Test/UnitTest/xLuaTest/LuaCallCS/CoverageForLuaCallCS.cs`):
  `Coverage.Parity` with `[LuaCallCSharp]` and `Coverage.ParityReflected`, the
  same class without configuration, plus their indexer, struct and derived
  classes. In the `--source-gen` passes of `run_managed.py` the first gets
  generated wrappers and the second reflection wrappers; in the first pass
  and `--source-gen-fallback` both get emitted wrappers.
- A temporary probe case ran about 190 calls (constructors, fields,
  properties, methods with every argument shape, overloads, operators,
  indexers, events, structs, inheritance, delegates) on both types in every
  pass and wrote each result or error text to a file; a script compared the
  eight columns (four passes, two types). The differences became the
  `CaseParity*` cases in `luaCallCsCoverage.lua`, which stay: each runs the
  same calls on both types and checks expected values and error texts, and
  `CaseParitySameResults` compares about thirty calls of the two types one by
  one. A pass is a process of its own, so a case compares a mode with an
  expected value or with the other type of the same process.

## Differences and fixes

Every fix applies in every preset, legacy included (plainly wrong
behaviour, AGENTS.md), with a row in [MIGRATING.md](../MIGRATING.md#changes-in-every-build).
"Before" is main at b3adc8e8.

| # | Difference | Modes that differed | Fix | Test |
|---|---|---|---|---|
| 1 | `[BlackList]` on an operator, or an entry with its parameter types, did not leave it out | generated kept it; emitted and reflection left it out | `Members.cs` (operator loop): `IsMethodBlackListed`, `IsObsolete` | `CaseParityOperators`, generator test `BlackListLeavesOutOperatorsIndexersAndLazyMembers` |
| 2 | `[BlackList]` on an indexer or one accessor, `[Obsolete]` indexers | generated kept them; reflection kept a blacklisted accessor | `Members.cs` `IsIndexerAccessorExcluded`; `Utils.cs` `ReflectionMembers.make` (one list per accessor) | `CaseParityIndexers`, `CaseObsoleteOperator`, generator test |
| 3 | An entry `{type, "Item"}` or `{type, "get_Item"}` | the generator matched Roslyn's name `this[]` and warned XLSG003 for both | `Members.ReflectionName` (MetadataName), `Config.cs` `Members` and accessors by name | generator test |
| 4 | `[BlackList]` on a string-keyed indexer (its `get_Item`/`set_Item` methods) | generated and emitted kept the methods; reflection left them out | `IsIndexerAccessorExcluded` in the method candidates; `CodeEmit.isIndexerAccessorInBlackList` | `CaseParityIndexers` |
| 5 | A member in `[DoNotGen]` and `[BlackList]` or `[Obsolete]` | generated registered it lazily (callable); emitted left it out; a lazy method kept blacklisted overloads | `Members.BuildLazy` skips them; `Utils.isLeftOutOverload` in `LazyReflectionCall` | `CaseLazyBlackListed`, generator test |
| 6 | `[Obsolete]` operator of a configured type | generated kept it; emitted left it out | as 1 | `CaseObsoleteOperator` |
| 7 | Public readonly instance field written from Lua | emitted and reflection wrote it; generated raised "no such field" | no setter in `CodeEmit.emitTypeWrap` and `makeReflectionWrap` (non-public ones stay for `xlua.private_accessible`) | `CaseParityReadonlyField` |
| 8 | Indexer with a private setter | reflection called it | public accessors only | `CaseParityIndexers` |
| 9 | Indexer with two keys | reflection passed one key, `TargetParameterCountException`; emitted bound it | one-key indexers only, as generated code | `CaseParityIndexers` |
| 10 | Binary operator with one overload and an operand of another type (`a + 1`) | emitted and reflection passed null into the operator; generated raised "invalid arguments to right hand of + operator, need T!" | forced check and `BindingApi.InvalidOperatorArguments` | `CaseParityOperators` |
| 11 | `self` that is not an object of the class (`o.Add({}, 1, 2)`) | emitted made a new object from the table and called the method; reflection raised `TargetException`; generated `NullReferenceException` | emitted reads `self` with `BindingApi.FastGetCSObj` and a cast; reflection raises the exceptions of that cast | `CaseParityErrorTexts` |
| 12 | Event handler that is not a function (`o:Changed("+")`, `o:Changed("+", 5)`) | reflection passed it to `CreateDelegateBridge`, which used it as a registry key: nil raised "table index is nil" from inside C#, a number threw `KeyNotFoundException` or could name another function's bridge; generated and emitted raised "#3 need System.Action!" and "#3, need System.Action" | reflection converts the handler first and uses generated code's texts; emitted's text is generated code's | `CaseParityEvents` |
| 13 | Error texts | reflection: "invalid arguments to Over", "invalid arguments to .ctor", "c# exception:" + message + ",stack:" + trace, "try to get T.Item throw a exception:", "c# exception in FixCSFunction:" for fields; emitted's event text | generated code's texts in reflection wrappers | `CaseParityErrorTexts`, `CaseParityEvents` |
| 14 | Missing argument for `int?` (`o:Nul()`) | generated and emitted read 0; reflection null | `ObjectCasters.genNullableCaster` treats none as nil | `CaseParitySameResults` |
| 15 | `XLUA_JUST_EXCLUDE_ERROR` | generated kept warning-only `[Obsolete]` members; emitted left out every `[Obsolete]` member | `Utils.IsLeftOutObsolete`, used by `CodeEmit` and lazy members | not tested (a define of its own) [U] |

Evidence, after the change: `General/SourceGenerator/XLua.SourceGenerator/Members.cs:146`
(`ReflectionName`), `:191` (`IsIndexerAccessorExcluded`), `:590` (operators),
`:611` and `:617` (indexers), `:660` (`BuildLazy`); `Config.cs:357` and `:371`;
`Assets/XLua/Src/Utils.cs:889` (indexers), `:1045` (operators), `:1085`
(readonly fields), `:1247` and `:1255` (lazy overloads, obsolete);
`CodeEmit.cs:1474`, `:1561`, `:1617`, `:1782`, `:2138`;
`MethodWarpsCache.cs:362` (self), `:562` (texts), `:752` (events);
`Binding.cs:150`; `ObjectCasters.cs:1214`.

Item 12 is also a crash. After the nil handler's Lua error had unwound
through the reflection wrapper's managed frames, the next C# exception thrown
inside a reflection call made Mono 6.14.1 crash while unwinding
(`mono_arch_unwind_frame`, SIGSEGV) or hang in its crash handler; the probe
hit both in the source-generator pass, inside the Lua coroutine run. [C]
The same exception without the earlier event call was caught normally (a
run of main with a throwing indexer, `FORCE_REFLECTION` and `--source-gen`).
[C] Recorded in [crash-inventory.md](crash-inventory.md#found-later).

The third item of the 2026-10-18 row, Validate Setup reporting a `[DoNotGen]`
name that only an extension method has, needed nothing: `SetupValidator`
no longer checks `[DoNotGen]` names (it reports configuration lists as not
read by the generator), and the generator's XLSG003 accepts such a name
(`Config.cs` `HasExtensionMethod`, generator test `Xlsg003NoSuchMember`). [C]

## The BaseType walk

Not a binding-mode difference, done with this work:
[bridge-audit.md](bridge-audit.md#not-changed), "BaseType cycle".
`build/xlua.c` `find_base_func` stops the walk with an error after 1000
types. [C] `native_regression.py` `indexer_base_walk` failed on main (the
finite chain of 1002 types ended without an error) and passes now.

## Owner questions

Compatibility questions the search found, not changed:

1. **`[Obsolete]` members in reflection wrappers.** Generated and emitted
   wrappers leave `[Obsolete]` members out (all of them, or only errors with
   `XLUA_JUST_EXCLUDE_ERROR`); reflection wrappers bind them, so an IL2CPP
   player without generated code, or a type without `[LuaCallCSharp]`, has
   members that the Editor's emitted wrappers lack. Leaving them out of
   reflection would change what players can call (upstream behaviour);
   binding them in the other modes would change generated code. Cost of
   either: one `IsDefined` per member wrapped.
2. **A struct parameter given nil or a number** (`S.Read(nil)`,
   `S.Read(5)` for `Read(S s)` with one overload): generated code raises
   `NullReferenceException` (`ObjectTranslator.Get<T>` unboxes null); emitted
   and reflection wrappers pass `default(S)`. For `int` parameters every mode
   passes 0, so the lax answer is consistent with the rest; the strict one
   would raise "invalid arguments" in every mode. [C] (probe)
3. **`xlua.private_accessible` and readonly fields.** It still writes
   non-public readonly fields (the reflection wrappers' non-public pass);
   public readonly fields are no longer writable in any mode. Keep, or make
   them read-only too?

Seen but not differences between the modes (the same in every mode,
upstream behaviour, not changed): writing a `const` or `static readonly`
field from Lua (`T.Const = 1`) replaces the value in the class table, so
later reads in Lua see 1; an argument of the wrong type for a method with one
overload is converted without a check (`o:Add("a", 2)` passes 0).

## Tests

macOS arm64, Mono 6.14.1, after merging main at aadd1d87:

- `run_managed.py --source-gen --source-gen-minimize --source-gen-fallback`
  (reflection/CodeEmit, generated, GCM, fallback; 502 tests in each mode),
  on Lua 5.5.1 and LuaJIT 2.1, legacy and `--define XLUA_PRESET_MINIMAL`
  with libraries built with `XLUA_PRESET=minimal`: all pass; the surface
  diff passes. [C]
- Before the change (main b3adc8e8 with the new cases, Lua 5.5.1 legacy):
  the six `CaseParity*` fail in the first pass, and those plus
  `CaseLazyBlackListed` and `CaseObsoleteOperator` in the source
  generator's pass. [C]
- `build_source_generator.py --test`: 44 tests pass; the new one fails on
  main's generator. [C]
- `native_regression.py` on the four libraries, `check_exports.py`,
  `hotfix_check.py` (Lua 5.5.1): pass. [C]
- `editor_compile_check.py`: 7 editors x 85 configurations, 588 ok, 7
  skipped (C# 12 configurations on Roslyn 4.3.1). [C]

Not run: Lua 5.3.6 and 5.4.9, Linux, the sanitizers, the Unity editor suite
and players (IL2CPP reflection wrappers are the players' path for types
without generated code; the reflection changes are checked on Mono only).
