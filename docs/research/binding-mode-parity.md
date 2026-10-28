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

   **Decided 2026-10-28 (owner, Q21): left out of reflection wrappers too,
   in every preset, with no flag.** Implemented as follows [C]:
   - `Utils.cs` `ReflectionMembers.make`: `isListed` and `isListedMethod`
     also apply `Utils.IsLeftOutObsolete`, the rule the emitted wrappers and
     lazy members already use. That is the generator's
     `Names.IsObsolete`: any `[Obsolete]` on the member itself, warning or
     error (`[Obsolete("...", false)]` is left out too), or only
     `[Obsolete(..., true)]` with `XLUA_JUST_EXCLUDE_ERROR`. The attribute is
     read with `inherit: false`, as Roslyn's `GetAttributes` reads it. It
     covers fields and constants, events, properties (the property or the
     accessor), indexers (the indexer or one accessor, string-keyed ones
     included), operators, methods, single overloads and extension methods.
   - `MethodWarpsCache.GetConstructorWrap`: constructors, as
     `Members.Build` leaves them out.
   - Only the public pass changes. The non-public pass that
     `xlua.private_accessible` adds is the same in every mode, since no
     generated or emitted wrapper binds non-public members, so it keeps
     obsolete members.
   - An `[Obsolete]` type configured for generation is still skipped by the
     generator (`ConfigEntry.Obsolete`) and then wrapped by reflection or
     CodeEmit with its members. This is unchanged and the same in every mode.
   - Cost: one `IsDefined` per member when the type's members are first made
     (`ReflectionMembers` caches them per process); nothing per call.
   - Tests: `CaseParityObsolete` (`luaCallCsCoverage.lua`, new) runs the same
     checks on `Coverage.Parity` and `ParityReflected`: an obsolete method,
     a static method with `error: true`, an overload next to kept ones
     (`Over(bool)`), fields, a constant, a property, an event, a constructor
     and an operator, plus `ObsoleteOperatorReflected` for an indexer.
     `CaseObsoleteOperator` lost its reflection branch, and the
     `LuaTestCommon.ConfiguredTypesUseReflection` helper is gone.
     `CaseParitySameResults` compares the obsolete members of both types.
     The generated surface did not change (`suite.json`): the generator
     already left these members out.
2. **A struct parameter given nil or a number** (`S.Read(nil)`,
   `S.Read(5)` for `Read(S s)` with one overload): generated code raises
   `NullReferenceException` (`ObjectTranslator.Get<T>` unboxes null); emitted
   and reflection wrappers pass `default(S)`. For `int` parameters every mode
   passes 0, so the lax answer is consistent with the rest; the strict one
   would raise "invalid arguments" in every mode. [C] (probe)

   **Decided 2026-10-28 (owner, Q22): raise in every mode, every preset, no
   flag.** The raise point is the conversion, not the overload check, so the
   error comes only where no check ran: a method with one overload, a field,
   a property. With several overloads, nil already failed every struct check
   (`ObjectCheckers`: a value type takes a table or a C# object of the type,
   never nil). The text is new and the same everywhere:
   `c# exception:System.InvalidCastException: T expected, got nil` (or
   `number`, `string`, `boolean`, `no value`). It follows the form of the
   UnityEngine.Object rule for a value C# reads (`ObjectCasters.tableRefused`).
   Generated code's old text was a bare `NullReferenceException`. Implemented
   [C]:
   - **Which types** (`ObjectCasters.IsStrictStruct`, the generator's
     `Wrappers.IsStrictStruct`): value types other than primitives, enums,
     `decimal` and `Nullable<T>`. Enums are left as they were (see the
     questions below).
   - **Reflection:** `ObjectCasters.GetArgCaster` wraps the struct caster and
     throws `ObjectCasters.StructExpected` for null. The same caster serves
     reflection parameters, fields and properties (`GetArgObject`), `params`
     elements and `xlua.access`. `OverloadMethodWrap.Call` runs the caster
     for a missing non-optional struct argument too, which passed the zero
     value before.
   - **Emitted:** `CodeEmit.EmitGetObject` reads a struct argument with
     `ObjectTranslator.GetStructArg` instead of `GetObject` followed by
     "null means `initobj`". Results that C# reads from Lua are unchanged.
   - **Generated:** `Wrappers.Cast` writes
     `BindingApi.GetStructArg(translator, L, i, out v)` for a struct argument
     without a generated reader (it was `translator.Get(L, i, out v)`). The
     generated readers of `[GCOptimize]` structs (`Structs.cs`) throw
     `BindingApi.StructExpected` in their last branch. A packed struct read
     `(T)GetCaster(...)` and threw `NullReferenceException`; a struct read
     field by field threw `Exception("can not cast ...")`. Those readers also
     serve values C# reads (a `LuaTable.Get<T>`, a delegate result), which now
     throw the same `InvalidCastException` in generated code. `BindingApi`
     gains `GetStructArg<T>` and `StructExpected`, which needs no new API
     version (only removing or changing a member does).
   - **Unchanged on purpose:** `Nullable<S>` takes nil and nothing; an
     optional struct parameter left out gets its default (generated code's
     `DefaultCount` overloads, reflection's default values); an explicit nil
     for it raises "invalid arguments", as before in every mode; nil picks a
     reference-type overload over a struct one.
   - **Cost:** none on the paths that succeed. Generated and emitted code
     test the boxed value for null, which the cast did anyway. Packed
     `[GCOptimize]` readers change only in their failing branch. No benchmark
     was run for this part.
   - **Tests:** `CaseParityStructArgs` (new) checks the texts for nil, a
     number, a string, a boolean and no argument on `ParityStruct` and
     `ParityReflectedStruct`, plus `Pick(nil)` choosing the class overload,
     `Opt(1)`, `Opt(1, nil)`, `NulStruct(nil)` and `NulStruct()`, a struct
     field set to nil or 5, and the `[GCOptimize]` readers
     (`MultiListedB.SumVec(nil)` packed, `Swap(nil)` read field by field).
     `CaseParitySameResults` compares the same calls on both types. New
     fixtures (`Parity.Pick`, `Opt`, `NulStruct` and `Inner`) were copied
     into `Test/Native/expected-surface/suite.json` from the generator's
     manifest.

   Questions this leaves for the owner:
   - **Enums.** An enum parameter given nil: generated code throws
     `NullReferenceException` (or reads nil through the enum's generated
     reader), and emitted and reflection wrappers pass 0. Same kind of
     difference; not changed here.
   - **Values C# reads from Lua.** A struct result of a delegate or
     interface bridge, or `LuaTable.Get<S>`, given nil: emitted bridges
     return `default(S)`, generated ones throw (now `InvalidCastException`
     for `[GCOptimize]` types, still `NullReferenceException` for others
     through `ObjectTranslator.Get<T>`). Raising there too would be a change
     for every `LuaTable.Get<S>` of a missing key.
   - **A C# object of another class** passed for a struct parameter still
     differs: generated code throws `InvalidCastException` from the cast,
     emitted code the same from `unbox`, and reflection `ArgumentException`
     from `MethodBase.Invoke`.

   **Decided 2026-10-28 (owner, Q34, Q35, Q36).** Implemented [C]:
   - **Q34, enums: an enum argument given nil raises the struct error in every
     mode.** The record above was wrong in one point: emitted and reflection
     wrappers did not pass 0 for nil but threw
     `InvalidCastException: invalid value for enum E` from the enum caster
     (`ObjectCasters.genCaster`) [C] (`CaseParityEnumArgs` on main). Now every
     mode raises `c# exception:System.InvalidCastException: E expected, got
     nil` (`no value`, `boolean`, `table`, or the class of a C# object of
     another class), as for structs: `ObjectCasters.IsStrictArg` (a strict
     struct or an enum) selects `GetStructArg` in emitted code and in
     generated code without a reader (`Wrappers.IsStrictArg`), the new
     `ObjectCasters.genEnumArgCaster` serves reflection parameters, fields and
     properties (a number, a value name or the enum's own userdata go to the
     caster as before; anything else raises `StructExpected`), a missing
     argument of a reflection overload raises as nil does
     (`OverloadMethodWrap.Call`), and the generated reader of a
     `[LuaCallCSharp]` enum raises `StructExpected` in its last branch for an
     argument that is no number or string, and for another type's userdata.
     Unchanged: a number and a value name convert, `Nullable<E>` takes nil, an
     omitted optional enum parameter gets its default, and an enum read back
     from Lua where Lua has no value raises `invalid value for enum E` in
     every mode, as before (the same everywhere, so outside Q35).
   - **Q35, values C# reads back: `default(S)` where Lua has no value.**
     `ObjectTranslator.Get<T>` gives `default(T)` where `GetObject` returns
     null (unboxing null threw `NullReferenceException`): delegate and
     interface results of generated bridges without a reader,
     `GenericDelegateBridge`, `LuaFunction.Func`, `DelegateBridge`. The
     generated readers of `[GCOptimize]` structs take `bool result = false`:
     with `true` a struct gives `default(S)` for nil or nothing, and the
     bridges' result reads and the registration (`b.Struct`, `b.Caster`, the
     get functions of `Get<T>` and the casters of `GetObject`) pass `true`;
     arguments, `self` and array elements do not. Since a `[GCOptimize]`
     table type's caster now gives `default(S)` for nil,
     `ObjectCasters.genStructArgCaster` refuses nil and nothing before it
     calls the caster, and `ObjectTranslator.GetStructArg<T>` refuses them
     before a get function. Emitted bridges already gave the zero value.
     **Not changed: `LuaTable.Get<S>` and `GetInPath<S>`** raise
     `InvalidCastException: can not assign nil to S` for a missing key before
     any conversion, for every value type, `int` included
     (`LuaTable.cs:57`, `:152`, upstream), and the same in every mode, so the
     premise "the same as `Get<int>` returning 0" does not hold there; making
     only structs give `default` would separate them from `int`. Left as it is
     and reported to the owner. A value of another kind read back (a number
     for a struct result) still differs: generated `[GCOptimize]` readers
     raise `S expected, got number`, the other paths give `default(S)`; not
     decided, reported.
   - **Q36, a C# object of another class for a struct parameter:
     `InvalidCastException` with the Q22 text.** `ObjectCasters.StructExpected`
     names the class of a C# object (`S expected, got Coverage.Parity`),
     `ObjectTranslator.getObject` raises it for an argument of a strict struct
     or enum type given a C# object of another class (emitted `GetStructArg`,
     reflection fields and properties), reflection parameters reach it through
     the caster as before, and the generated readers raise it instead of
     `Exception("invalid userdata for S")` (packed: `BindingApi.StructBuffer`,
     which the generated `Update_` methods share, so updating a struct
     userdata of another type raises it too) or the cast's exception (read
     field by field). Enums the same (Q34).
   - **Cost:** none on the paths that succeed, except one `lua_type` per
     reflection struct argument (`genStructArgCaster`), which the reflection
     path pays next to `MethodBase.Invoke`; generated and emitted arguments
     and results are unchanged where the value is right. No benchmark was run.
   - **Tests** (`luaCallCsCoverage.lua`): `CaseParityEnumArgs` (Q34, on
     `Parity.Kind`/`OptKind`/`NulKind`/`KindField` with the `[LuaCallCSharp]`
     enum `ParityKind`, the same on `ParityReflected` with the unconfigured
     `ParityReflectedKind`, and `Day(System.DayOfWeek)`),
     `CaseParityStructResults` (Q35: `ParityResults.Func` through
     `LuaFunction.Func` and `ParityResults.Call` through five `[CSharpCallLua]`
     delegates, for `ParityStruct`, `ParityReflectedStruct`, the packed
     `MultiListedVec`, the table-read `MultiListedPair` and `ParityKind`; and
     `ParityResults.Get`, which records that `LuaTable.Get` raises for every
     value type), `CaseParityStructOtherObject` (Q36, also `MultiListedB.SumVec`
     and `Swap`). Before the change all three failed in the first pass
     (reflection and CodeEmit, Lua 5.5.1): "invalid value for enum
     Coverage.ParityKind", "Specified cast is not valid." and
     `NullReferenceException` from `LuaFunction.Func` [C]. Generator test
     `EnumArgumentsAndStructResults` (new); `StructArgumentsRaiseForNil` and
     `StructsFollowTheLegacyModel` now expect `GetStructArg` for an enum
     argument without a reader and `result: true` in the registration. The
     new fixtures' entries were copied into
     `Test/Native/expected-surface/suite.json` from the generator's manifest.
3. **`xlua.private_accessible` and readonly fields.** It still writes
   non-public readonly fields (the reflection wrappers' non-public pass);
   public readonly fields are no longer writable in any mode. Keep, or make
   them read-only too?
   **Decided 2026-10-28 (owner): keep.** `xlua.private_accessible` keeps
   allowing writes to non-public readonly fields; it is an explicit request
   for private access. Public readonly fields stay read-only in every mode
   (since 2026-10-28). No code change.

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

### Tests of the 2026-10-28 decisions (Q21, Q22)

The same machine, on 3042ef63 with the changes, host plugins built from
this tree (run folder `owner-decisions-2026-10-28b`):

- `run_managed.py --source-gen --source-gen-minimize --source-gen-fallback`,
  510 tests in each mode of each of the four passes, surface diff included:
  Lua 5.5.1 and LuaJIT 2.1, legacy and `--define XLUA_PRESET_MINIMAL`
  (libraries built with `XLUA_PRESET=minimal`), and Lua 5.5.1 with
  `--define THREAD_SAFE`. All pass. [C]
- The same tests on the code before the changes (runtime and generator of
  3042ef63, Lua 5.5.1, minimal preset, first pass): `CaseParityObsolete`,
  `CaseParityStructArgs`, `CaseObsoleteOperator` and `CaseParityErrorTexts`
  (the new obsolete constructor took `T("x")`) fail. [C]
- `build_source_generator.py --test`: 45 tests pass, `StructArgumentsRaiseForNil`
  among them. It was not run on the old generator; there it would find no
  `GetStructArg`. [U]
- `hotfix_check.py` and `crash_repro.py` (Lua 5.5.1): pass, and give the same
  results as recorded in crash-inventory.md. [C]
- `editor_compile_check.py`: 7 editors x 85 configurations, 588 ok and 7
  skipped (the C# 12 configurations on Roslyn 4.3.1). [C]
- `run_core.py` (core preset, and `--bridge` with `XLUA_LUASCRIPT=ON`):
  pass on Mono and .NET. In the first `--bridge` run,
  `TestTimeLimitIsOnTimeWithAnInstructionLimit` (a 50 ms limit that stopped
  143 ms late) failed on .NET while a THREAD_SAFE suite ran next to it; it
  passed when run again alone. [C]
- Not run: Lua 5.3.6 and 5.4.9, Linux, the sanitizers, the Unity editor
  suite and players (no IL2CPP run of the reflection changes).
- `node sync.mjs --check`: all links resolve. [C]

### Tests of the 2026-10-28 decisions (Q34 to Q37, Q41)

macOS 27 arm64, Mono 6.14.1, after merging main at ab73d3b9 (run folder
`c-part-2026-10-28`):

- `run_managed.py --source-gen --source-gen-minimize --source-gen-fallback`,
  517 tests in each mode of each of the four passes, surface diff included:
  Lua 5.5.1, 5.3.6 and LuaJIT 2.1, legacy and `--define XLUA_PRESET_MINIMAL`
  (libraries built with `XLUA_PRESET=minimal`). All pass. [C]
- `native_regression.py` and `check_exports.py` on the six libraries: pass.
  [C]
- Docker (`xlua-analysis:26.04`, Linux arm64, `--memory 6g`): `sanitize.sh`
  for 5.5.1 and 5.3.6 (ASan, UBSan, API checks; the managed suite through
  reflection, as the image has no .NET SDK) pass; `coverage.sh` for 5.3.6,
  5.4.9 and 5.5.1 passes the gate, `xlua.c` at 100% of its branches (it fell
  to 349 of 350 before `registry_checks` called the plain `xlua_pushcsobj`
  without the cache, which the managed suite, running with the bridge
  checks, no longer calls). [C]
- `build_source_generator.py --test`: 48 tests pass; `EnumArgumentsAndStructResults`,
  `Xlsg014ReferencedDefaultStructs`, `StructArgumentsRaiseForNil` and
  `StructsFollowTheLegacyModel` fail on the generator before the change. [C]
- `lua_tests.py` (basic, complete, internal) on 5.3.6, 5.4.9 and 5.5.1 with
  the `luaL_traceback` patch: final OK. [C]
- `editor_compile_check.py`: 7 editors x 85 configurations, 587 ok, 7
  skipped, one failure where Mono's `csc` crashed (exit -6, no compiler
  error) on `player-android-il2cpp-xlua-no-load-assembly` for 6000.6.6f1; that
  configuration passed on all seven editors when run again. [C]
- `check_meta.py`, `check_pinvoke_abi.py`, `Tools/dependencies.py --check`,
  `node sync.mjs --check`: pass. [C]
- Not run: Lua 5.4.9 managed suites, Windows and Linux hosts for the managed
  suite, the Unity editor suite and players (no IL2CPP run of the enum,
  struct and registry changes), `publish.yml`, a benchmark of the debug-build
  registry checks.
