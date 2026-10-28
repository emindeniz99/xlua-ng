# Test coverage

The repository has a bridge unit suite, performance examples, native regression
checks, and Unity player checks. It does not yet have an exhaustive test matrix
for every supported Unity version, platform, backend, or optional extension.

## General C# bridge suite

`Test/UnitTest` exercises Lua-to-C# calls, the explicit reflection cases, `in` and `ref readonly` parameters,
regression tests for upstream issues listed in `UPSTREAM.md`
(`StreamingAssets/luaCallCsUpstream.lua`), the code that the
[coverage runs](#c-coverage) found untested (`StreamingAssets/luaCallCsCoverage.lua`,
fixtures in `xLuaTest/LuaCallCS/CoverageForLuaCallCS.cs`), the bridge checks
and the opt-in bridge hardening defines (`StreamingAssets/luaBridgeHardening.lua`)
and C#-to-Lua calls. The Lua
runner (`StreamingAssets/main.lua`) enables seven
suites and runs their tests twice: first inside a coroutine, then on the main
thread. `EXPECTED_TESTS` in `Test/Native/run_managed.py` holds the number of
tests. Its generated-code suite is
currently commented out upstream; a pass does not mean that optional suite
ran. The TDR suite and its data were removed with the `tdr` module.

The tests run on [luaunit](https://github.com/bluebird75/luaunit) 3.5
(`StreamingAssets/luaunit.lua`, unmodified upstream file).
`StreamingAssets/luaunit_compat.lua` keeps the API the test files were written
for (`TestCase` classes, `SetUp`/`TearDown` hooks, `ASSERT_EQ`, `ASSERT_TRUE`)
and maps it onto luaunit: `ASSERT_EQ` is `assertEquals`, `ASSERT_TRUE` is
`assertEvalToTrue`. A failed assertion fails its test even when the test code
catches the error with `pcall`. Each mode prints a line such as
`[luaunit coroutine] Ran N tests in 0.2 seconds, N successes, 0 failures`,
and a failure in either mode raises a Lua error, which `LuaEnv.DoString`
turns into a C# exception. Outside Unity, Lua's `print` writes to C's
`stdout` and C# (`LuaTestCommon.Log`) to `Console`, two buffers on one file.
PUC Lua flushes after each `print` and LuaJIT does not, so
`luaunit_compat.lua` flushes through `io.stdout`; a library without `io`
(the minimal preset) has nothing to flush with, and there `print` goes
through `LuaTestCommon.Log`. Before that, LuaJIT's lines left in blocks
that end anywhere, and a C# line could land inside one, also inside the
summary line, which the scripts then missed (native-build run 40672755047).

With Mono 6.14.1 and `mcs` installed (`brew install mono` on macOS and Linux),
first build a host-native plugin, then run:

```sh
python3 Test/Native/run_managed.py --plugin /absolute/path/libxlua.so --output /absolute/path/test-output
```

The runner stops unless `mono --version` reports 6.14.1; pass `--mono-version any`
to skip that check. On macOS use a rebuilt `libxlua.dylib`. `--mono` and `--compiler` accept explicit
tool paths; an `.exe` compiler runs through Mono. The runner compiles the
existing project source lists, copies test data to the output directory, saves
`managed-suite.log`, and fails unless the process exits with 0 and both
modes report `EXPECTED_TESTS` tests run and as many successes.
Every process the runner starts (a compiler, a Mono run of the suite, a tool)
runs in its own process group under a hard timeout, `--timeout` seconds
(default 1800). On a timeout the whole group is killed, Mono and anything it
started included, and the runner fails with the last 40 lines of the
process's output, which name the test that hung; nothing is left running
(checked on 2026-10-28 with a compiler and a `mono` that hang and start a
child each: the run stopped after the timeout and no child was left).
That first run binds everything through reflection and CodeEmit. Generated
bindings come from the xLua source generator only: the legacy generator and the
runner's `--gen-code`, `--gen-code-minimize` and `--gen-assembly` passes were
removed in source generator milestone M11 (those options now stop with a
pointer to the ones below). CI, `sanitize.sh` and `coverage.sh` use the
source generator's passes where a .NET SDK is installed. `--source-gen` builds the
source generator (`Tools/build_source_generator.py`, see below), compiles
`XLuaUnitTest` again with the .NET SDK's csc and `-analyzer`, against the Mono
assemblies `mcs` uses and at C# 12 (Mono's own csc, Roslyn 3.9, cannot load a
generator built against 4.3.1; `Test/Native/roslyn_fixtures.py` rewrites the
`in`/`ref readonly` fixtures for Roslyn, as for `coverage_cs_dotnet.py`), and
runs the suite on Mono through `Test/Native/SourceGenLauncher.cs`
(`managed-suite-source-gen.log`). The generated code is in the test assembly,
next to `XLua.Mini.dll`, the layout of xLua in its own asmdef, so the build also
fails if generated code uses anything of xLua's that is not public (what
`--gen-assembly` checked for Generate Code's output). The suite's configuration lists reach the
generator through the migration tool: `XLuaGenerate --migrate --group` converts them
to `[assembly: ...]` attributes, several types per attribute for the lists of
several types (`Coverage.MultiTypeConfig`: LuaCallCSharp, CSharpCallLua,
GCOptimize and ReflectionUse, `CaseMultiTypeLists`), and its output must equal the checked-in
`Test/Native/XLuaUnitTest.XLuaConfig.cs` (`XLUA_MIGRATED_CONFIG_PASS`; when a
list changes, copy the fresh file from `source-gen/migrated/` over it), which
the build compiles. The lists stay in the suite as the migration tool's
fixture. The generator
writes the registration, the name table, the class and enum wrappers of the
`[LuaCallCSharp]` types, the delegate and interface bridges of the
`[CSharpCallLua]` types and the GCOptimize pushers: the launcher checks that the first `LuaEnv` registered the unit
through `[assembly: GeneratedBindings]`, that every name is `Type.ToString()`
of its type, that the configured types' loaders are the generated wrappers,
that the delegate types and interfaces are registered from the generated
namespace, that `Gen_Flag` is set, that a Lua function's delegates are
closed over its `DelegateBridge` with a method of the generated `Bridges`
class (also for a delegate type no configuration lists, found by signature)
that a Lua table cast to an interface is the generated bridge, that the
GCOptimize types are registered with their array accessors, and that 5,000
`TestStruct` values made and read in Lua allocate no managed memory per value
(`XLUA_SOURCE_GEN_STRUCT_ALLOC`), that `SourceGenFallback.Locked`
(`Test/Native/SourceGenFallback.cs`, an init-only property compiled only in
this build) works from Lua through the per-member reflection fallback and logs
its reason once, and that the generated link.xml fragment keeps every lazy or
fallback member the wrappers reach by reflection and every `[ReflectionUse]`
type (`XLUA_SOURCE_GEN_LINKXML`). The binding surface, the bridge sets and the
GCOptimize types of the generator's manifest (`XLua.Manifest.g.cs`) must equal
`Test/Native/expected-surface/suite.json`: what the legacy generator bound for
the same configuration at `24ed1374`, frozen when it was removed, plus
entries copied by hand from the generator's manifest for fixtures added since
(`CaseAssemblyLevelConfig`'s, and on 2026-10-19 `InParams.Gen`,
`ReadonlyArgsCaller`, the `in` delegate bridge of `InPointCallback` and
`RefReadonlyPointCallback`, and `IReadonlyArgs`: real `in` and `ref readonly`
members, which only the Roslyn builds have).
`Test/Native/surface_diff.py` compares constructor, overload and indexer order
as is and other lists as sets, every delegate type with the same
`SignatureKey`, every interface bridge with the same members, every GCOptimize
type with the same kind, size, fields and float groups (`surface-diff.log`);
any difference other than the two documented ones (the fallback fixture, which
mcs cannot compile, and `IInKeyIndexer`'s `in` key, which only Roslyn sees)
fails the run. After a fixture change that alters the surface on purpose,
`--update-surface` writes the manifest to that file; review its diff. It
rewrites the whole file in the generator's order and drops the legacy
header and the documented differences, so to add a fixture's types, copy
only their entries from the manifest instead.
`--source-gen-fallback` runs the same build
again with `XLUA_SG_SKIP_REGISTER`, so that no unit registers, as when the
generator did not run: the suite must pass through reflection and CodeEmit
(`XLUA_MANAGED_SOURCE_GEN_PASS_FALLBACK`). `--source-gen-minimize` adds the
generator's `GEN_CODE_MINIMIZE` mode: `XLua.Mini`
and the test assembly compiled with the define, the suite through the GCM
wrappers (`managed-suite-source-gen-minimize.log`,
`XLUA_MANAGED_SOURCE_GEN_PASS_MINIMIZE`), the launcher's check that
`LuaTestObj`'s wrapper functions are private translator methods that fill the
translator's `CSharpWrapper` table (`XLUA_SOURCE_GEN_GCM`; in the plain mode
it checks that they are native callbacks), and the same surface comparison
(`surface-diff-minimize.log`; the legacy generator bound the same surface with
and without the define). The runs
fail on a generator warning or error and on analyzer load warnings (CS8032,
CS8784, CS8785, CS9057). `--coverage` runs every
Mono process under the coverage profiler (see [C# coverage](#c-coverage)). `--define NAME` (repeatable)
adds a C# symbol to xLua, the tools and the test assemblies in every mode. For
example, `--define XLUA_LOCK_METATABLES` runs the suite with locked bridge
metatables. The `CaseMetatableLock*` cases check the locked behaviour when the
symbol is set and the upstream behaviour when it is not. The C# feature
defines work the same way: `CaseSignatureLoaderFollowsTheDefine`,
`CaseTemplateLibraryFollowsTheDefine`, `CaseEmbeddedUtilFollowsTheDefine` and
`CaseEmbeddedProfilerFollowsTheDefine` check `XLUA_NO_SIGNATURE_LOADER`,
`XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` and `XLUA_NO_PROFILER` in both states
(`LuaTestCommon.NoUtil()` and the like report how the suite was built). The
bridge checks (`XLUA_BRIDGE_CHECKS`, which follow the build type) and the
bridge hardening defines (`XLUA_RESTRICT_GETMETATABLE`,
`XLUA_LOCK_XLUA_TABLE`, `XLUA_RESTRICT_PRIVATE_ACCESS`,
`XLUA_NO_LOAD_ASSEMBLY`) have their cases in `luaBridgeHardening.lua`,
with forged userdata from `BridgeHardening.Natives`
(`xLuaTest/LuaCallCS/BridgeHardeningForLuaCallCS.cs`); `Defines` there
reports how the suite was built. The suite runs as `XLUA_GENERAL`, a debug
build type, so the bridge checks are on; `--define XLUA_NO_BRIDGE_CHECKS`
runs it as a release player would (`CaseCheckObjectsForeignSelf`,
`CaseCheckStructAccessorsForeign` and `CaseCheckObjectCache` then check the
upstream behaviour), and `--define XLUA_BRIDGE_CHECKS` next to it checks
that the force define wins. `CaseRegistryChecks` (2026-10-28, owner decision
Q37) replaces, through `debug.getregistry`, the metatable reference of a
class and of `decimal`, the userdata cache, a metatable stored by type name
(read by `xlua.cast`) and, on LuaJIT, the int64 metatable `registry[8]` with
a number, pushes values while they are replaced and restores them: the
values come without a metatable, the cache misses, the named entry is
removed, and int64 arithmetic raises "was replaced". It runs only with the
bridge checks: a release player crashes there by design. `CaseLightUserdataIsNoObject` passes in every
state: a light userdata is never read as an object (it failed before
`xlua_tocsobj_fast` checked the value type, with checks off). Run the suite
once without the defines and once with each, or all together. `CaseStrictStructTables` (`luaCallCsUpstream.lua`)
checks that a table setting none of a struct's fields raises (Tencent/xLua#751,
every preset). Lua's integer rule has its cases in
`luaCallCs.lua` (`CaseInteger*`, fixtures `IntegerSlots`, emitted or
generated, and `IntegerSlotsReflect`, always reflection, in
`xLuaTest/LuaTestObj.cs`): parameters, fields, overloads, array elements and
indices, tables converted to arrays, collections and structs, `params`,
nullable integers and enums; `CaseIntegerResultRule` runs
`IntegerSlotResults`, which reads delegate, interface, `LuaFunction` and
`LuaTable` results and checks the Lua stack top after every failed
conversion. `CaseIntegerRangeFollowsTheDefine` checks
`--define XLUA_STRICT_INT_RANGE` in both states. `--define XLUA_TEST_PLAYER_BLACKLIST`
(a test define) gives `BlackListRules` the players' rules outside Unity: only the
attribute forms of `[BlackList]`, no lists or filters of static classes.
`CaseBlackListAttributes` (`luaCallCsCoverage.lua`, fixture `Coverage.AttributeListed`)
checks them in every run, and with `--define FORCE_REFLECTION` next to it the
suite binds as an IL2CPP player without generated code does; the list and
filter cases skip themselves then. The binding modes must behave the same:
`CaseParity*` (`luaCallCsCoverage.lua`, 2026-10-28) run the same calls on
`Coverage.Parity` (`[LuaCallCSharp]`: generated wrappers in the
`--source-gen` passes, emitted ones in the first pass and the fallback) and
on `Coverage.ParityReflected`, the same class without configuration
(reflection wrappers in the `--source-gen` passes), and check the results and
error texts: operators, indexers, readonly fields, events, `self` of another
class, the error texts, and `CaseParitySameResults` compares some thirty
calls of both types one by one. `CaseParityObsolete` checks that `[Obsolete]`
members are left out in every mode, reflection included, and
`CaseParityStructArgs` that a struct parameter or field given nil, a number
or nothing raises the same `InvalidCastException` everywhere (owner
decisions of 2026-10-28). `CaseParityEnumArgs` does the same for enum
parameters and fields (Q34), `CaseParityStructOtherObject` for a C# object
of another class passed for a struct (Q36), and `CaseParityStructResults`
checks that a struct C# reads back from a delegate or `LuaFunction.Func`
where Lua returns nothing is `default(S)` (Q35), through `Coverage.ParityResults`;
it also checks that `LuaTable.Get<T>` of a missing key raises for every value
type and that `ContainsKey` and `Get<object>` do not (Q55).
`CaseParityStructResultKinds` checks that a struct read back from a
delegate, an interface bridge (`Coverage.IParityResults`), `LuaFunction.Func`
or `LuaTable.Get` given a number, a string, a boolean, a function or a C#
object of another class raises `S expected, got <kind>` (Q56), and that a
`Nullable<S>` read back the same ways raises the same while nil gives null
(Q59). `CaseParityStructParams` checks that a nil element of a `params`
array of structs raises the struct argument error in every mode, also for the
`[GCOptimize]` structs, that `Nullable<S>` elements still take nil, and that
enum elements follow the enum argument rule (Q60, Q34, Q63).
`CaseParityNullableStructArgs` checks that a `Nullable<S>` parameter or
field given a value that is no `S` raises `S expected, got <kind>` in every
mode while nil and nothing pass null (Q62). `CaseLazyBlackListed` and
`CaseObsoleteOperator` cover `[DoNotGen]` with `[BlackList]` and `[Obsolete]`
operators and indexers in generated code. The modes run in separate
processes, so a case compares a mode with an expected value or with the
other type of the same process, not two modes directly. `--define XLUA_PRESET_MINIMAL` builds everything
with the minimal preset, whose block at the top of each file
(`Tools/presets.py`) defines the defines of its column (thirteen:
`GEN_CODE_MINIMIZE` and `XLUA_NO_REFLECTION` joined and the bridge checks
moved to the build type on 2026-10-19);
`CasePresetImpliesItsDefines` checks that every one of them is on, and the
cases above check what each changes. In every build it also checks that xLua
declares `XLua.CSharpWrapper` exactly when the suite has `GEN_CODE_MINIMIZE`.
With the preset the reflection run and the `--source-gen` passes therefore use
the GCM wrappers (the launcher prints `XLUA_SOURCE_GEN_GCM` in the plain pass
too), and the generator must not warn XLSG013; `--define XLUA_NO_GCM` next to
the preset keeps the plain wrappers in xLua and in the generator. In the
source-gen passes without reflection the generator must warn XLSG014
(a `[GCOptimize]` struct without bindings, since 2026-10-28) for exactly
`Coverage.MultiListedVec` and `Coverage.MultiListedPair`, the structs of
`CaseMultiTypeLists`, which is in `needs_reflection.lua`
(`EXPECTED_XLSG014`), and in no other pass. One of them, `XLUA_NO_REFLECTION`, turns
off the reflection fallback and CodeEmit
([presets.md](../docs/design/presets.md#reflection-fallback-and-codeemit-as-implemented)):
then only the source-gen passes run without it. The first pass binds through
reflection and `--source-gen-fallback` registers nothing, so both add
`XLUA_REFLECTION` (the preset's opt-in) and get xLua and test assemblies of
their own; `--define XLUA_REFLECTION` turns it back on for every pass, and
`--define XLUA_NO_REFLECTION` alone (without the preset) works the same way.
Without reflection the cases listed in
`StreamingAssets/needs_reflection.lua`, which use the suite's reflection
fixtures (`*Reflect`, `NoGen*`) and .NET types it does not configure, or
convert Lua tables to classes and structs without `[GCOptimize]`, must fail
with the error of a type without bindings (`has no generated bindings`), of a
table conversion without a generated converter (`has no generated table
converter`, since 2026-10-28) or of a
missing bridge (`This type must add to CSharpCallLua`); `luaunit_compat.lua`
counts that as a pass, and a listed case that passes, or fails another way,
fails, as does an entry that names no case of the run. An entry `"private_accessible"` or `"util"` applies only where the build
has `xlua.private_accessible` or `xlua.util`. `RAISE_NO_BINDINGS(pcall(...))`
raises such an error again in a case that catches it. `CaseNoReflection*`
(fixtures in `xLuaTest/LuaCallCS/NoReflectionForLuaCallCS.cs`) check the error
texts, base classes with bindings, enums, `pairs`, bridges,
`xlua.private_accessible` and table conversions in both states. With a preset define `run_managed.py` also
sets the `XLUA_EXPECT_*` variables of the preset's library
(`Tools/presets.py --expect-env`), unless they are set already, so run it
against a library built with `XLUA_PRESET=minimal`. `--define XLUA_UTIL` next
to it keeps `xlua.util`. The
runner copies no Lua modules from `Assets`: `require("xlua.util")` and
`require("perf.profiler")` load the copies embedded in the xLua assembly, so a
pass also shows that they need no file. The memory diagnostics
(`LuaEnv.TakeMemorySnapshot()`) are compiled only with `UNITY_EDITOR`,
`DEVELOPMENT_BUILD` or `XLUA_MEMORY_DIAGNOSTICS`:
`CaseMemoryDiagnosticsFollowsTheDefine` checks that without the define neither
the types, the `LuaDLL` import nor `xlua.memory` exist, and with
`--define XLUA_MEMORY_DIAGNOSTICS` the other `CaseMemory*` cases (helpers in
`xLuaTest/LuaCallCS/MemoryForLuaCallCS.cs`) take snapshots from C# code that Lua
called and from `__gc` finalizers, and check Lua paths, a diff, registry
references and the `xlua.memory` module. `sanitize.sh` runs the suite with that
define. `CaseStringPackVersionedMessage` runs
the `string.pack` example from [docs/ECOSYSTEM.md](../docs/ECOSYSTEM.md#stringpack-and-stringunpack)
(it does nothing on LuaJIT, which has no `string.pack`).

**`THREAD_SAFE` and `HOTFIX_ENABLE` in CI (owner decision Q71,
2026-10-28).** `native-build.yml`'s `linux / Lua 5.5.1` job runs the suite
twice more after its main run, with `--define THREAD_SAFE` and with
`--define HOTFIX_ENABLE`, each with `--source-gen --source-gen-minimize
--source-gen-fallback`, against the plugin the job built (logs under
`define-<NAME>/` in the `tests-lua-5.5.1-linux` artifact). Until then no job
used these defines, and the suite stopped compiling under both: `LuaTable.GetKeys`
is `[Obsolete(..., true)]` there (CS0619, fixed in `d0e846c9`; see the
[verification round](#verification-round-of-2026-10-28)). The defines change
only the C# side, so one Lua version is enough. `hotfix_check.py`, in the same
job, also compiles xLua with `HOTFIX_ENABLE`, but it runs its own fixture and
the injector, not the suite. Each run took 43 to 50 seconds on macOS (Lua 5.5.1,
Mono 6.14.1) and 73 seconds on the hosted runner, so the step adds 146 seconds
and the job went from 8:10 (run 40694841486) to 11:02 (run 40728029574, all
four passes 522 tests in both modes with each define).

The suite also runs against a LuaJIT plugin built with `-DUINT_ESPECIALLY=ON`.
There a C# `ulong` is a separate unsigned userdata kind that prints with a `U`
suffix, and a `ulong` returned to C# as `object` stays a `ulong`. On every other
build it shares the signed representation (`UInt64.MaxValue` prints `-1`).
`luaunit_compat.lua` sets `DISTINCT_UINT64` for that build, and
`ULONG_TOSTRING(signed, unsigned)` returns the text a `ulong` prints on the
running build. CI does not build this variant; three wrong expectations
for it went unnoticed until 2026-10-18
([apicheck-runs.md](../docs/research/apicheck-runs.md)). To run it on Linux:

```sh
VERSIONS=luajit-uint Test/Native/sanitize.sh /absolute/path/empty-dir
```

or build with `-DUSING_LUAJIT=ON -DUINT_ESPECIALLY=ON` and pass that plugin to
`run_managed.py`.

`Tools/build_source_generator.py --output <dir> --test` builds the source
generator (`General/SourceGenerator/XLua.SourceGenerator`, netstandard2.0,
Microsoft.CodeAnalysis.CSharp 4.3.1) with the .NET SDK from `mise.toml` and
runs its unit tests (`XLua.SourceGenerator.Tests`, a console program that
drives the generator with `CSharpGeneratorDriver` 4.3.1 over sources that
reference the xLua runtime, compiled in memory from the `XLua.Mini` sources).
They load the generated registration into the test process and compare each
name table entry with `Type.ToString()`, cover one case per diagnostic
(XLSG001, 002, 003, 005, 006, 007, 009, 011), byte-identical output for the
same configuration in other files, incremental runs (an unrelated edit leaves
every output step cached or unchanged, a configuration edit re-emits), the
Unity registration attributes, a `SignatureKey` table (which signatures share
a delegate bridge, rule by rule, and the hotfix reduction), the delegate and
interface bridges (folding, registration, delegates closed over the bridge,
XLSG004 for types that cannot be bridged), the GCOptimize selection (pushed,
packed, table-read and boxed types, float groups, `[AdditionalProperties]`,
types configured by a referenced assembly, XLSG010), the per-member fallback
with its reason, the link.xml fragment and `XLUA_SG_SKIP_REGISTER`, and that
the generator references Roslyn 4.3 at most. NuGet restores from nuget.org only, in locked mode against each
project's `packages.lock.json`, into `Tools/.cache/nuget` (or
`$XLUA_TOOLS_CACHE/nuget`); `--update-lock` rewrites the lock files.
`--install <folder>` copies the DLL into a Unity project with its
`RoslynAnalyzer` `.meta`.

`Test/Native/signature_check.py --output <empty dir>` builds the signing tools,
creates a key, signs a script, and checks that `SignatureLoader` accepts it
and rejects tampered, truncated and legacy SHA-1 files.

`Test/Native/luacats_check.py --plugin <libxlua> --output <dir>` (needs the
.NET SDK, as `run_managed.py --source-gen`) builds `XLuaGenerate.exe` and the
source generator, compiles `Test/LuaCATS/Sample.cs` with the generator and
`Test/LuaCATS/SampleAttributes.cs`, its configuration (`roslyn_fixtures.py`
rewrites the mcs spellings of `in` and `ref readonly`), writes LuaCATS
definitions from the manifest the generator wrote into it (milestone M10;
configuration lists are not read since M11) and compares them with `Test/LuaCATS/expected` and the `.luarc.json` beside them
with `expected-luarc.json` (`--update` rewrites them). It compiles
`Sample.cs` a second time with `LUACATS_NO_REF_READONLY` at C# 9, which
leaves out its C# 12 `ref readonly` members as Roslyn 4.3.1 (Unity 2021.3 to
6000.3) needs, and compares those definitions with the overlay
`Test/LuaCATS/expected-no-ref-readonly` over `expected` (`CS.lua` without the
`Gauge` delegate, `Circle` without `Gauger` and `Stretch`; Lua 5.x plugins
only). `Test/UnityMatrix/luacats_unity.py` compares an editor's output with
the same files ([below](#luacats-from-the-editor-and-the-strict-minimal-player-2026-10-28)). It checks strict mode
against `expected-strict`, the file suffix, the `.luarc.json` ownership rules,
Clear and Generate, byte-identical output on a second run, under the tr-TR
culture and from CRLF sources (`Test/LuaCATS/CultureLauncher.cs` sets tr-TR
and runs the generator in its process, because Mono on macOS ignores `LANG`
and `LC_ALL`), and `XLuaGenerate --validate` with
`Test/LuaCATS/SetupSample.cs` (configuration lists, built by mcs: each is
reported as not read, and its stale `BlackList` entries, which the Editor's
reflection wrappers still read), a missing plugin, a stub plugin with another
bridge version and a project tree with old xLua Lua modules in `Resources`
folders (`--resources-root`): a shipped `perf/memory.lua.txt`
(`Test/LuaCATS/stale-resources`, also with CRLF and a BOM), edited
`xlua/util` and `perf/profiler` copies, and files that must not be reported
(a lone `.meta`, other modules, `Samples~` and hidden folders, folders not
named `Resources`). It also generates definitions for `XLuaUnitTest.exe`,
compiled with the generator and the suite's converted configuration. With
`--download-luals` (Linux or macOS, lua-language-server 3.19.1, checked by SHA-256) or
`--luals <executable>`, it runs lua-language-server with the generated
`.luarc.json` over `Test/LuaCATS/good.lua` and `good_strict.lua`, which must
have no diagnostics, and `bad.lua` and `bad_strict.lua`, whose diagnostics must
match their `-- expect:` comments. Use a plugin built with Lua 5.3 or later
(pass `--lua-version` unless it is 5.5), or LuaJIT with `--luajit`.

A second build adds `Test/LuaCATS/SampleVariant.cs`: a
`BlackList` entry on a wrapped type and on a base class, `DoNotGen` members,
a constructed generic `[ReflectionUse]` type, and a stale entry that the
compiler reports (`XLSG003`); the definitions must follow the manifest.
Validate Setup then gets output of the removed Generate Code in the Gen
folder, an assembly with configuration but no generated bindings
(`Test/LuaCATS/NoBindingsSample.cs`, compiled by mcs) and the stale lists of
`SetupSample.cs`. (`--source-gen`, which turned the manifest run on before M11,
is accepted and does nothing.)

`Test/Native/hotfix_check.py --plugin <libxlua> --output <dir>` (needs the .NET
SDK) checks hotfix injection end to end. It builds the tools, compiles
`Test/Native/HotfixFixture.cs` with the xLua sources and the xLua source
generator, whose `HotfixBridges` (bridges and IntKey slots, milestone M7) the
injector calls; their signatures must equal
`Test/Native/expected-surface/hotfix-fixture.json`, the legacy generator's
hotfix bridges for the same fixture, frozen when it was removed (M11). Mono's
mcs then compiles the fixture with the generator's files (C# 9, which mcs reads
once `#nullable` is left out) for the cases below, which need mcs's `.mdb`
and long branches; the run injects it with `XLuaHotfixInject` and runs it under
`mono --debug`. The fixture covers
instance and static methods, a constructor, a property, `ref`/`out` parameters,
a struct, a generic class, `base(self)`, a type selected by a `[Hotfix]` list
(injected through another type's bridge of the same signature), an `IntKey`
type (through the fixture's own slots: the id map names the assembly), and
constructors whose branches cross the short-branch limits
once the hotfix is injected before each `ret`. mcs writes only long branches,
so `Test/Native/ShortBranches.cs` first shortens those constructors as Roslyn
would, and after injection the run checks that every short branch still fits
and that the ones just past the limits became long. `Test/Native/HotfixRegions.cs`
adds constructors that end in a try/finally, a try/catch or a filter (a
foreach over a `List<T>` and an `IEnumerable<T>`, a struct, a generic class,
a switch with an early `return`) and methods that start with a try block or
a loop head, and runs them before patching, patched and restored.
`ShortBranches.exe --check` checks the IL of every fixture method before and
after injection as a verifier would (short branches fit, `ret` and branches
respect the exception handling regions, no region falls through at its end):
Mono runs such IL, IL2CPP does not convert it (Tencent/xLua#1064). It also calls
`GC.WaitForPendingFinalizers` from Lua with dropped `LuaTable` and
`LuaFunction` references, which must return (a watchdog ends the run after
30 seconds). It checks the original results, the results after
`xlua.hotfix`, and the original results again after `xlua.hotfix(..., nil)`.
This runs for an assembly with a portable `.pdb`, with the `.mdb` that
`mcs -debug` writes, without symbols, and with an unreadable `.pdb`; line
numbers must survive injection, and a second injection must print
`had injected!` and leave the file unchanged. Another case passes the
assemblies and the id map as bare file names relative to the working
directory (the injector threw for an id map path without a directory part
before 2026-10-18). `HotfixFixture.Second.dll`
(`Test/Native/HotfixFixtureSecond.cs`), built by mcs without the generator,
has a type that only a `[Hotfix]` list names: there are no bridges for it, and
the injector must stop with the hint to convert the list
(`HOTFIX_NO_BRIDGES_PASS`). The asmdef layout case compiles xLua alone into
`XLua.dll` and the fixture with the generator against it, injects the fixture
with `XLua.dll` as xLua's assembly and patches its `IntKey` type through its
own slots. After injection the fixture also checks that every
`__Hotfix` field and the `__XLUA_GEN_FLAG` type carry
`[Obfuscation(Exclude = true)]`. The fixture as csc compiles it (Roslyn's IL,
as Unity's) is then injected, its IL checked and run, and a second assembly built with the generator,
`HotfixFixture.SecondSG.dll` (`Test/Native/HotfixFixtureSecondSG.cs`),
configured with `[Hotfix]` on its types and at assembly level (one type per
attribute, and `[assembly: Hotfix(typeof(A), typeof(B))]` and
`[assembly: Hotfix(HotfixFlag.IntKey, typeof(A), typeof(B))]`), must inject
without the field-mode warning and patch an `IntKey` type, an `Inline` type,
a generic method, a struct and a plain method by name, after
`XLuaHotfixInject --merge-id-maps` merged the two id maps and
`xlua.util.auto_id_map()` read them (it looks the id map up with
`xlua.typename`). The second assembly, compiled at C# 12, also has the
hotfix cases of C# `in` and `ref readonly` parameters, which mcs cannot
declare (`RemoteReadonlyArgs`, `RemoteReadonlyArgsKeyed` and
`RemoteReadonlyArgsInline`): plain and virtual methods in the default,
`IntKey` and `Inline` modes, patched with a function that returns an extra
result, which must reach the `ref` control method's argument only. The
virtual ones have `modreq(InAttribute)` parameters; injection stopped at
them with "can not find delegate" before 2026-10-19. Until M11 these cases
used Generate Code's bridges, and the second assembly's `IntKey` type fell
back to field mode. Under
`--define XLUA_PRESET_MINIMAL`, which leaves out `xlua.util`, the
two-assembly cases set the ids with `HotfixDelegateBridge.Set` and `SetSlot`
from the id maps instead of calling `auto_id_map()`. `Tools/build_tools.py` downloads
Mono.Cecil from nuget.org once into the gitignored `Tools/.cache`; set
`XLUA_TOOLS_CACHE` to use another directory (for a read-only checkout).
`--cecil-dir <folder>` builds and runs the injector with the Cecil of Unity's
`com.unity.nuget.mono-cecil` 1.11.5/1.11.6 package instead (see
[hotfix.md](../docs/research/hotfix.md#unitys-cecil-package-spike-2026-10-18)).
`--define NAME` works as in `run_managed.py`. For example,
`--define XLUA_LOCK_METATABLES` checks `xlua.hotfix` and `base()` with locked
metatables.

`Test/Native/obfuscation_check.py --plugin <libxlua> --output <dir>` runs
xLua against renamed types, as after an obfuscator
([source-generator.md](../docs/design/source-generator.md), milestone M8).
It compiles `Test/Native/ObfuscationFixture.cs`, a game assembly, with the
.NET SDK's csc and the source generator against `XLua.Mini.dll` (xLua in an
assembly of its own, as with the XLua asmdef), writes the obfuz rule file for
it with `Test/Native/ObfuzRulesTool.cs` (the Editor adapter's
`Assets/XLua/Src/Editor/ObfuzRules.cs` on the generator's fragment), and
renames it twice with `Test/Native/ObfuscationRenamer.cs`, a small Cecil
renamer: once with the rule file, applied as obfuz 4.0.0 applies rules, once
honouring only `[Obfuscation(Exclude = true)]`, as attribute-driven tools do.
Types, namespaces, non-virtual methods, fields, properties and events are
renamed (`$N0.$32`); references through generic instances and `typeof` in
attribute blobs follow. The plain build and both renamed builds then run 17
checks from Lua with the original names: `CS.Game.Shop`, members, statics,
events, a nested and a generic type, enums, a GCOptimize struct, a delegate
and an interface from Lua, `xlua.typename`, the metatable key, a
`[ReflectionUse]` type, a `[DoNotGen]` member, an init-only setter left to
reflection, an enum with more than 20 values used by name, and a type without
configuration. In the plain build all pass. With the rules only the type
without configuration fails (it is not in the name table); with the attribute
alone the four reflection uses fail too, which shows what the rules keep.
Any other result fails the run. `--define` works as in `run_managed.py`; each
Mono run has a timeout (`--timeout`, 300 s) and its process group is killed
after it.

### Windows hosts: the .NET Framework and Mono (2026-10-28)

`run_managed.py --runtime netfx --compiler <csc.exe>` runs the first pass
(reflection and CodeEmit) on a Windows host's .NET Framework instead of Mono:
the compiler is a Roslyn `csc.exe` (Visual Studio's or the Build Tools'),
which gets the fixtures rewritten for Roslyn (`roslyn_fixtures.py`) and
`-define:XLUA_TEST_NO_SPAN` (the .NET Framework has no `Span<T>` outside the
System.Memory package, so the `Span` members of `Upstream.SpanOverloads` are
left out), and the test program runs without a host. The source generator
passes and `--coverage` need Mono and are refused. The .NET Framework
marshals `string` parameters in the ANSI code page, so this is the run that
checks the UTF-8 imports of `XLUA_GENERAL`
([Tencent/xLua#1081](https://github.com/Tencent/xLua/issues/1081)).
On Mono for Windows the ordinary runner works with `--mono <Mono>/bin/mono.exe
--compiler <Mono>/lib/mono/4.5/mcs.exe --mono-version 6.12.0` (the Windows
build reports 6.12.0).

`python Test/Native/ansi_control.py --compiler <csc.exe> --plugin xlua.dll
--output DIR` is the control for that run: it turns the UTF-8 imports of
`Assets/XLua/Src/LuaDLLBridge.cs`, then of `Assets/XLua/Core/LuaDLL.cs`, back
to ANSI (the code before the fixes; the file's bytes are restored), runs the
suite on the .NET Framework, and requires `CaseUtf8StringParameters`, then
`CaseUtf8TypeNames`, not to pass while the other passes. With
`LuaDLL.cs` in ANSI an `XLUA_APICHECK` build aborts in Lua's API check
before `CaseUtf8TypeNames` reports, which counts as not passing.

`windows-host.yml` runs both, with the native checks and
`apicheck_probe.py`, on MSVC `XLUA_APICHECK` builds of Lua 5.3.6, 5.4.9,
5.5.1 and LuaJIT (GC64)
([.github/CI.md](../.github/CI.md), [windows-host.md](../docs/research/windows-host.md)).
Run [40645581045](https://github.com/emindeniz99/xlua-ng/actions/runs/40645581045)
passed: 496 tests in each of two modes on the .NET Framework and on Mono
6.12.0 for every leg (then five, with LuaJIT without GC64), and the control
on 5.5.1. The LuaJIT leg also builds a `lua51.lib` without GC64 and
requires CMake to refuse it. Since 2026-10-28 the 5.5.1
leg also runs the .NET Framework pass with `--define NATIVE_LUA_PUSHSTRING`,
whose `lua_pushstring` import takes UTF-8 under `XLUA_GENERAL` (owner
decision of 2026-10-28), and `ansi_control.py` has a third variant: that
define with only this import in ANSI (the `#if XLUA_GENERAL //
NATIVE_LUA_PUSHSTRING` line), where `CaseUtf8StringParameters` must not pass.
Run [40669037845](https://github.com/emindeniz99/xlua-ng/actions/runs/40669037845)
(still five legs) passed: 508 tests in each mode on every leg, with the
define too, and in the control 13 cases failed with the ANSI import before
`CaseUtf8TypeNames` aborted. With the define, `CaseUtf8EmbeddedNul` expects a
C# string to end at its first NUL (`Defines.NativeLuaPushString()`), on every
runtime. Three cases came from these
runs: `CaseUtf8TypeNames` (two types with one-character CJK names; it fails
without the `luaL_newmetatable` fix on the .NET Framework only),
`CaseErrorsThroughCSharpCallbacks` (`Upstream.CallbackErrors`: Lua errors
raised from C# functions, C# exceptions and errors through nested Lua, C# and
`LuaFunction.Call` frames, in coroutines and with a C# message handler, for
the SEH unwinding of LuaJIT on Win64), and the dynamic-assembly expectations
of `CaseEnvScanReused` and `CaseEnvScanSeesLaterAssembly`: the .NET Framework
lists the runtime assembly behind an `AssemblyBuilder`, which the
`[CSharpCallLua]` scan included and therefore did not keep, so each later
env scanned again; Mono lists the builder, which the scan left out. Owner
decision of 2026-10-28: the scan skips every dynamic assembly on every
runtime and stays cached, so both cases expect no rescans everywhere again,
and `CaseEnvScanReused` also makes a dynamic assembly with a
`[CSharpCallLua]` interface, which must cause no scan and not be found
(without the change the .NET Framework would scan again, as `CaseEnvScanReused`
did in run 40642149114; not rerun). On .NET (`coverage_cs_dotnet.py`) the
case failed in the coroutine pass of run 40672755047 with two scans: the
name of its dynamic assembly used `Process.GetCurrentProcess().Id`, which
loaded `System.Diagnostics.Process` (Mono has it in `System.dll`), and the
first env's scan of that assembly loaded four more, so the second env
scanned too. The name is now a counter. Not run on
Windows: the source generator passes, .NET (Core), Unity.

For the original Unity unit scene, copy `Test/UnitTest/xLuaTest` and
`Test/UnitTest/StreamingAssets` into a Unity project's `Assets` folder and run
`xLuaTest/main.unity`. This is a manual path, not the current CI entry point;
the Android test code still uses a legacy `/sdcard/luatest/` result path.
The runner writes the test names and the failed tests of each mode to
`luaunit_case_list[_co].txt` and `luaunit_case_failed[_co].txt` under
`LuaTestCommon.resultPath`.

## String and Unicode fixtures

The bridge suites already cover ASCII strings (for example `boo123`, `abc`,
and struct string fields), empty and long strings, escape sequences, and
multiline text. They also retain simplified and traditional Chinese strings,
stylized Chinese characters, and mixed punctuation/symbol fixtures. These are
functional encoding and marshaling data, not maintained language translations.

Keep those fixtures and their expected values intact. Add cases for new encoding
behavior or a regression, rather than duplicating an existing case merely to
change its language. The documentation language policy does not restrict the
character sets exercised by the tests.

## Native checks

```sh
python3 Test/Native/check_exports.py /absolute/path/libxlua.so
python3 Test/Native/profiler_smoke.py /absolute/path/libxlua.so
python3 Test/Native/native_regression.py /absolute/path/libxlua.so
```

These check the managed import symbols, GC bridge opcodes, memory profiler
graph traversal, and regressions for native defects found by the analysis
below. Run them against a library built for the host architecture. LuaJIT
builds have no internal-header leak checker, so `profiler_smoke.py` does not apply to
them; a Lua 5.x library built with `-DXLUA_LEAK_CHECKER=OFF` passes it with only
the `xlua_gc` checks. Since 2026-10-28 `profiler_smoke.py` also walks after
every small collection step of a cycle full of garbage whose parts are newer
than their owners, and from a C function with a full stack
([#1149](https://github.com/Tencent/xLua/issues/1149)); the old walker failed
it under `sanitize.sh` (crash-inventory.md, item 11). `check_exports.py` also fails when `build/xlua_exports.txt`, the export
list of `-DXLUA_EXPORTS=managed`, no longer matches `LuaDLL.cs`;
`python3 Test/Native/managed_imports.py --write` regenerates it.
`native_regression.py` also calls exports that `LuaDLL.cs` does not import
(`check_exported_helpers`, `print_top`), so it needs an `XLUA_EXPORTS=all`
library. Its `global_state_key` check (2026-10-28) requires `xlua_gl`, the
key by which C# finds an env, to stay the same for a state and its threads
across collections and to differ for another state. Its `registry_checks`
check (2026-10-28) calls the `*_checked` push exports of the bridge checks
(`xlua_pushcsobj_checked`, `xlua_pushstruct_checked`,
`xlua_pushcstable_checked`, `xlua_tryget_cachedud_checked`,
`lua_pushint64_checked`, `lua_pushuint64_checked`) with intact references and
with a metatable reference and a cache replaced by a number, and checks
`xlua_check_registry_refs`: on LuaJIT the int64 metamethods and the `uint64`
functions raise once `registry[8]` is not a table, and work again once it is
restored. Its `traceback_levels` check
(2026-10-28) calls `debug.traceback` with levels near `INT_MIN` and
`INT_MAX`, on the main thread, deep in a call chain and on a coroutine: the
text must be the message and "stack traceback:" alone, and under UBSan
(`sanitize.sh`) the unpatched `luaL_traceback` of Lua 5.3.6, 5.4.9 and 5.5.1
stops with a signed overflow ([LUA_PATCHES.md](../build/LUA_PATCHES.md)).
Since the owner's decision Q57 (2026-10-28) it also calls `debug.getinfo`,
`debug.getlocal`, `error(msg, level)` and, on LuaJIT, `getfenv` with those
levels, and `sanitize.sh` compiles LuaJIT's C files with
`-fsanitize=signed-integer-overflow`, under which unpatched LuaJIT stops in
`lj_debug_frame`. Its `shift_overflow` check (2026-10-28, owner decision
Q52) shifts by `math.mininteger` at run time and in a folded constant: the
result is 0, and under UBSan the unpatched Lua 5.3.6 stops with "negation of
-9223372036854775808 cannot be represented" (`OP_SHR` and `luaO_arith`). LuaJIT
has no `>>` and skips it.

`python3 Test/Native/apicheck_probe.py [--luajit] PLUGIN` checks that a
library built with `-DXLUA_APICHECK=ON` has Lua's C API checks: a child
process misuses the API once (`lua_pushvalue(L, 100)` on an empty stack; on
LuaJIT `lua_rawgeti` on a number) and must abort with the check's message,
and a correct call must return. A Release build without the checks fails
it (checked on macOS). `windows-host.yml` runs it on the MSVC builds, where
`XLUA_APICHECK` also sends a failed check to stderr instead of a message box
(`build/xlua_apicheck_msvc.c`).

`python3 Test/Native/crash_repro.py --plugin /absolute/path/libxlua.dylib --output DIR [case ...]`
runs the reproductions of [crash-inventory.md](../docs/research/crash-inventory.md)
(`Test/Native/CrashRepro.cs`, compiled with mcs against xLua's sources), each
in its own process group with a timeout (30 s; `--timeout`), and prints each
case's exit status (exit code, signal or TIMEOUT) and first lines. Nothing
is asserted: some cases still crash by design or on one runtime, and the
inventory records which. Run it through the test slot script like the other
native suites.

`python3 Test/Native/repro_check.py --work DIR [-- CMAKE_ARGS]` builds the
same configuration in two checkouts at different paths and requires the same
bytes; `--script make_android_lua55.sh` (or another `make_*` script) compares
everything the script writes. See
[build/README.md](../build/README.md#reproducible-builds) for what it found.
Run it through the test slot script; each build has a timeout (`--timeout`).

`python3 Test/Native/check_apple_minos.py [--platform macos|ios] FILE...`
reads the minimum OS (`LC_BUILD_VERSION` `minos`) of every architecture of a
macOS bundle or dylib and every object of an iOS `libxlua.a` with `otool -l`,
and fails above macOS 12.0 or iOS 15.0, the deployment targets in
[build/README.md](../build/README.md#macos-and-ios-deployment-targets), or
for an image without a version or for another platform. CI runs it after the
macOS and iOS builds (`native-build.yml`, `build.yml`, `publish.yml`).

`native_regression.py`'s `core_state` check drives the core layer
(`build/xlua_core.c`) from C: `xlua_core_newstate` with limits too small for
a state, refusals that `pcall` catches, a limit below the current use,
overdrafts from C callbacks (with a recorded error, nested under a raw
`lua_callk`, with the limit removed meanwhile, and the collection that clears
garbage left over the limit), the error handler's 16 KB reserve, and equal and
different hash seeds. `core_host_oom` makes `realloc` fail under an
address-space limit (Linux without ASan only; it prints a skip elsewhere).
`core_limits` covers the instruction and time limits (`xlua_core_limits.c`,
Lua 5.4 and 5.5): loops, `pcall`, `xpcall` and `__close` handlers that try
to continue after a stop, work spread over coroutines, an exact count that
small slices and a budget over 2^31 do not change, coroutines suspended under
an earlier limit, the message handler's traceback after a stop, `Interrupt`
from a C callback, the WebGL clock check, a watchdog thread (also inside a
coroutine and against back-to-back calls), the C budget of patterns, plain
`find` and `table.move`, and the profiler on the same hook.
`core_stop_after_last_instruction` covers a call stopped after its last Lua
instruction, which `lua_pcall` fails after its body (`build/xlua.c`): the
chunk returns through a tail call into a C function that interrupts the call
or polls `xlua_core_watch` past a 1 ns deadline, the C function is called
directly, from a coroutine or a nested entry, or a `pcall` caught the
instruction limit right before the return. No thread and no timing; each case
checks the status, the message, a balanced stack and the next call
([core-stop-latency.md](../docs/research/core-stop-latency.md#the-stop-after-the-last-instruction-tested-2026-10-28)).
`core_watchdog_race` runs 3,000 calls that only the watchdog can stop under
a 1 ms time limit, with a watchdog thread that sleeps between polls as
`LuaWatchdog` does; a call still running after 10 s ends the run with exit
code 3 instead of hanging it. Before the fix of 2026-10-18 the stop was
lost about once in a few hundred calls on macOS arm64
([watchdog-lost-stop.md](../docs/research/watchdog-lost-stop.md));
`XLUA_NR_WATCHDOG_CALLS` changes the count.
`core_stop_latency_env` (and `core_stop_latency` of `core_regression.py` for
LuaScript's entry, both in `Test/Native/stop_latency.py`) checks that a 50 ms
time limit and an `Interrupt` stop endless loops, calls, coroutines and tail
calls within 100 ms, without and with instruction limits, and that
`XLUA_LIMITS_INTERRUPT` lets `Interrupt` stop a call without limits. Before
the fix of 2026-10-18 the stops came 0.1 to 5 s late
([core-stop-latency.md](../docs/research/core-stop-latency.md)).
`XLUA_STOP_SLACK_MS` raises that bound for both the Python checks and
`Test/Core`'s `StopSlackMs` (an integer from 100 to 1000; `native-build.yml`
sets 250, since hosted runners stopped single calls 101 to 110 ms late).
A hosted CI runner can still miss the bound once in a while: with
`XLUA_RETRY_STOP_LATENCY=1` in the environment both Python checks run once
more when they fail, and `run_core.py --retry-stop-latency` runs the tests that
compare a stop with `StopSlackMs` (`StopLatencyTests.cs`, `CoroutineTests.cs`)
once more when only they failed. Each retry prints a `::warning::` line (it
shows in the run's summary), and a check that fails twice fails the run; a
stop that is really lost comes seconds late in every run. `native-build.yml`
sets all three ([.github/CI.md](../.github/CI.md)).
`Test/Native/core_stop_latency.c` measures the same with many runs and prints
the distribution per case; it opens the library with `dlopen`, so one binary
serves every build:

```sh
cc -std=c17 -O2 Test/Native/core_stop_latency.c -o core_stop_latency -lpthread   # -ldl on Linux
./core_stop_latency /abs/core/libxlua.dylib --entry core --runs 20
./core_stop_latency /abs/legacy/libxlua.dylib --entry pcall --runs 20       # LuaEnv's lua_pcall entry
```

It fails a stop later than `--slack-ms` (50) and ends with exit code 3 when a
call runs for `--cap-ms` (10 s). Latencies are noisy while other processes
load the machine; repeat on a quiet one before reading a failure as a bug.
Each late run is printed with the CPU time the running thread got after its
stop: almost none means the scheduler, not the library, was late. An
`Interrupt` that finds no call running yet is tried again (the "early"
column; `--interrupt-once 1` tries once). `--demote main` lowers the calling
thread's priority for runs under load. With a library built with
`-DXLUA_LIMITS_TEST_POINTS` (a compile definition, never a release),
`--point NAME[:N] --pause-ms P` holds the thread at a point of
`build/xlua_core_limits.c` while the other thread interrupts
([core-stop-latency.md](../docs/research/core-stop-latency.md#missed-stops-under-load-2026-10-18-second-investigation)).
Since 2026-10-18 `core_stop_latency_env` also covers a call back into Lua from
a coroutine on the main thread and a stop while `xluaL_loadbuffer` compiles,
and `core_stop_latency` a stop caught by `pcall` right before the call returns
and an `Interrupt` during `XLUA_CALL_COMPILE`.
The managed side is `CaseLuaOptions*` in `luaCallCsCoverage.lua`
(`xLuaTest/LuaCallCS/LuaOptionsForLuaCallCS.cs`), which runs through
`LuaEnv(LuaOptions)` in every binding mode; on Lua 5.3 and LuaJIT it checks
the `NotSupportedException` paths. `CaseLuaOptionsLibraries` checks
`LuaOptions.Libraries` against what a plain `LuaEnv` of the same library has:
`Default` and `All`, the safe set with `package` and `debug` (no `io`, `os` or
LuaJIT's libraries, also not through `require`), the reduced `debug` table
(`traceback` and `getinfo` only, errors with a traceback, `setfenv` and
`getfenv`, `xlua.util`), `require` and a custom loader without `package`, and
strings without `string`. `CaseDebugLibraryFollowsTheDefine` checks that
`XLUA_REDUCED_DEBUG` reduces the default only in players: the suite runs as
an `XLUA_GENERAL` tool and keeps the whole library, so the reduced default
itself is only compiled here (`editor_compile_check.py`'s `*-minimal*` player
configurations), not run.

### The core preset

A library built with `XLUA_PRESET=core` (LuaScript's sandbox without the
bridge, [core-luascript.md](../docs/design/core-luascript.md), section 8.4)
has none of the bridge functions, so the checks take it differently:

```sh
python3 Test/Native/check_exports.py --preset core /absolute/path/libxlua.so
python3 Test/Native/native_regression.py /absolute/path/libxlua.so
cc -std=c17 Test/Native/core_smoke.c -L<dir> -lxlua -o core_smoke && ./core_smoke Test/Native/core_sandbox_cases.txt
```

- **`Test/Native/core_sandbox_cases.txt`** holds the sandbox rules as cases
  for `xlua_core_call` (chunks; `xlua_core_run` before milestone 6): the absent libraries and base functions, the frozen
  base and the locked environment and string metatables, environments that
  do not see each other's globals or library copies, text-only `load` and its
  default environment, the `__gc` ban, `collectgarbage("count")`, coroutines,
  the instruction limit and the C budget, the memory limit, error messages
  and tracebacks, and `require` (its cache, missing and failing modules, a
  syntax or runtime error that is not cached, chunk names, loops, binary
  modules, limits inside a module). The file's header explains the format.
  The cases marked `--- libs: buffer` (milestone 10) run in a second state
  with LuaScript's `buffer` library and are skipped in a build without it
  (CMake `XLUA_BUFFER`): every function, bounds at every edge, byte order,
  integer and float edges (low bits, NaN, infinities, FLT_MAX rounding),
  overlapping copies, the locked metatable, the memory limit and the C
  budget under the instruction limit.
- **`native_regression.py`** finds no `luaopen_xlua` in a core library and runs
  the checks of `Test/Native/core_regression.py` only: `core_sandbox` (every
  case of the file), `core_sandbox_abi` (the configuration and memory errors of
  `xlua_core_open`, library subsets, the hash seed, environment handles,
  `xlua_gc`, `Interrupt`, the watchdog entry and nested calls from inside a
  call, `require` without a resolver or switched off, and the overdraft rule
  around the resolver), `core_buffer` (the buffer exports, buffer handles in
  `xlua_core_call`'s arguments and results and their release, the memory
  limit, a state without the library; `XLUA_EXPECT_BUFFER=ON` or `OFF` says
  whether the build has it), `core_host_oom` (Linux without ASan) and
  `core_lua_api`, which needs the Lua API exported (`XLUA_EXPORTS=all`): the
  bytecode stubs behind `lua_load` with modes `b`, `bt` and Lua 5.5's `B`,
  and `lua_dump`, states without the sandbox, and handles that are not
  environments; and `core_trusted_bytecode`, the modes of
  `xlua_core_call`'s chunks (`xlua_core_run_mode` before milestone 6): without CMake `XLUA_TRUSTED_BYTECODE` the binary mode
  is refused before anything runs; with it two chunks precompiled for Lua 5.4
  and 5.5 (in `core_regression.py` and `core_smoke.c`) run in the default and
  a new environment, text and binary keep to their modes, a truncated chunk and
  one of the other version fail in Lua's loader, and a script's `load` still
  refuses the same bytes. `XLUA_EXPECT_TRUSTED_BYTECODE=ON` or `OFF` says which
  build it is (unset, the library decides); and `core_coroutines`
  (milestone 9): `xlua_core_call`'s `XLUA_CALL_START`, `_RESUME` and `_CLOSE`,
  coroutine handles with their status, and the limits per step
  (`core_smoke.c`'s `check_coroutines` has the full list); and `core_raw_api`
  (2026-10-28, `XLUA_EXPORTS=all`), the branches of `xlua_core.c` and
  `xlua_core_limits.c` that in a core library only a host using the Lua C API
  reaches (a bridge library reaches them through the bridge): the limit
  exports and the C budget on a state without the core allocator, a host
  function's argument that is light userdata or, on Lua 5.5, a 2 GB external
  string over untouched memory, a hook that is not core's own, and a
  coroutine resumed with `lua_resume` instead of `coroutine.resume`; and
  `core_sandbox_edges` (2026-10-28), the edges of `xlua_core_sandbox.c` and
  `xlua_core_buffer.c` that the others leave (the file's docstring lists
  them), among them a coroutine error whose `__tostring` raises (the plain
  call's text, the coroutine's traceback, the error object as the error
  handle; a memory error or a stop in it is the call's), the same for a
  plain call (owner decision Q54, 2026-10-28: the error object stays the
  error handle, where the message handler ran again for what `__tostring`
  raised before) and, with the Lua
  API exported, `xlua_core_call`, `_handle`, `_register`, `_set_require` and
  `_unref` from a host function whose stack is exactly full, where a push
  before the export's own stack check fails Lua's API check (an abort with
  `XLUA_APICHECK`); and `core_set_limits` (2026-10-28), the first
  `xlua_core_set_limits` of a state, which creates the stop messages, on an
  exactly full stack and with a memory limit of one byte: `XLUA_ERRMEM`,
  nothing pushed and the limits unchanged, where it used to fail the API
  check or panic (`native_regression.py`'s `core_limits` runs the same on
  `xlua_core_newstate`'s states). A bridge library built with
  `XLUA_LUASCRIPT=ON` runs the same checks after its own.
- **`Test/Native/core_smoke.c`** drives the same file and edges through the
  core exports only, without Python, so it works with the managed export
  list and on cross-compiled targets. Its `xlua_core_call` checks cover typed
  arguments and results (64-bit integers, embedded NULs), more results than
  room, dotted and missing names, errors with the traceback apart, argument
  errors and the other kinds, the anchoring of string results across a full
  collection and their release by the next call, a result passed back, the
  instruction count and a memory error; `check_buffer` does the same for the
  buffer exports and handles. `check_coverage_edges` (2026-10-28) takes the
  edges of `xlua_core.c` and `xlua_core_limits.c` that the bridge's checks
  reach through `luaopen_xlua` in a core state: host function frames with a
  NULL or empty message and a result of an unknown type, the clock check
  (`XLUA_LIMITS_CLOCK_CHECK`) with a time limit only and with both limits,
  the C budget with a time limit only and after a stop that `pcall` caught,
  `Interrupt` and the watchdog's poll from a message handler after the stop
  fired, a coroutine whose hook an earlier limited call armed, and the edges
  of the coroutine switches.
- **`check_exports.py --preset core`** (and `check_android_elf.py --preset
  core`) checks the exports of `build/xlua_core_exports.txt`, exactly those
  with `XLUA_EXPORTS=managed`, none of `io`, `os`, `debug`, `package`,
  LuaSocket and the bridge, and no text of Lua's bytecode loader in the file
  (with `XLUA_EXPECT_TRUSTED_BYTECODE=ON` the loader must be there). Since
  2026-10-28 (owner decision Q50) `check_exports.py` and
  `check_android_elf.py`, with or without `--preset core`, fail a library
  that exports the test points of `xlua_core_limits.c`
  (`xlua_core_set_test_point`, `xlua_core_debug_state`, built with
  `-DXLUA_LIMITS_TEST_POINTS`) unless `XLUA_EXPECT_TEST_POINTS=ON` says it is
  that test build, which then must export both (`coverage.sh`'s `core-tp`
  runtime).
  `managed_imports.py --write` writes the list from `CORE_EXPORTS`: the
  `xlua_core_*` imports LuaScript needs, `xlua_gc` and five non-raising Lua
  API functions (23 with milestones 7 and 10). With every export, `check_exports.py --preset core` also
  checks that the library defines every import of `Assets/XLua/Core`, which
  an iOS or WebGL player links by name.
- **`sanitize.sh`** with `core-trusted-5.4.9` or `core-trusted-5.5.1` in
  `VERSIONS` runs the core builds with `-DXLUA_TRUSTED_BYTECODE=ON`.

### Bytecode-only libraries

A library built with `-DXLUA_BYTECODE_ONLY=ON` has no Lua compiler
([build/README.md](../build/README.md#bytecode-options)), so the bridge checks,
which compile text, cannot run on it:

```sh
XLUA_EXPECT_BYTECODE_ONLY=ON XLUA_COMPILER_LIB=/absolute/path/default/libxlua.so \
    python3 Test/Native/native_regression.py /absolute/path/bytecode-only/libxlua.so
XLUA_EXPECT_BYTECODE_ONLY=ON python3 Test/Native/check_exports.py /absolute/path/bytecode-only/libxlua.so
python3 Test/Native/bytecode_luaenv.py --plugin /absolute/path/bytecode-only/libxlua.so \
    --compiler-plugin /absolute/path/default/libxlua.so --output /absolute/path/empty-dir
```

- **`native_regression.py`** loads `return 1` first; when the library refuses
  it with the stub's message, it runs the checks of
  `Test/Native/bytecode_only.py` only. Their chunks are compiled by the
  library `XLUA_COMPILER_LIB` names, any build of the same Lua version and
  bytecode format with the compiler (Lua 5.3 with the same
  `LUAC_COMPATIBLE_FORMAT`). The checks: text chunks refused through the C API,
  `load` (also from a reader function) and `require`'s file searcher, with the
  chunk name in the message; precompiled chunks with modes nil, `"b"` and
  `"bt"`, and refused with `"t"`; `string.dump` and `load` of its result inside
  the library; `lua_setfenv` on a loaded chunk with and without debug
  information, closures sharing `_ENV`, functions without `_ENV`, C functions,
  and out of memory while it copies the function (this check fails on a
  bytecode-only library whose `lua_setfenv` still loads the empty text chunk);
  and `require` of a precompiled file. `XLUA_EXPECT_BYTECODE_ONLY=ON` fails a
  library that compiles text, `OFF` one that refuses it.
- **LuaEnv's built-in chunks** (`bytecode_only.py`, `builtin_chunks`): the
  loader `luaopen_xlua` puts into the registry must give every chunk of the
  `<xlua-chunk>` markers in `Assets/XLua/Src`, which the check finds and hashes
  itself (not with `build/xlua_chunkc.c`), so the library and the C# text are
  checked to come from the same source; each chunk's source name must be its C#
  chunk name, another hash and an unknown name must be refused with their
  messages. `XLUA_EXPECT_BYTECODE_MODULES` (`util,profiler,memory`, or fewer:
  CMake `XLUA_BYTECODE_UTIL` ...) makes the optional modules exact. A library
  with the compiler must have no such loader (`native_regression.py`, every
  library).
- **`bytecode_luaenv.py`** runs `LuaEnv` on the library
  (`Test/Native/BytecodeOnlyLuaEnv.cs`, the scripts of
  `Test/Native/bytecode_luaenv/` precompiled by `--compiler-plugin`): `new
  LuaEnv()` and `new LuaEnv(LuaOptions)` with the precompiled `init_xlua` and
  library removal, text refused, the `CS.*` bridge (types, members, namespace
  errors, generic types), `pairs` over C# collections, a Lua function as a
  delegate, `setfenv`, `require` from a custom loader, `xlua.util`,
  `perf.profiler` and `xlua.memory` (or their "no precompiled chunk" message
  when `XLUA_EXPECT_BYTECODE_MODULES` leaves them out), and a runtime error's
  line. Four modes: reflection, `source-gen` and `source-gen-minimize` (the
  source generator's wrapper and bridge, compiled by the .NET SDK's csc;
  `gen-code` and `gen-minimize` until milestone M11), and `locked`
  (`XLUA_LOCK_METATABLES`, `XLUA_LOCK_XLUA_TABLE`, `XLUA_RESTRICT_PRIVATE_ACCESS`,
  which change `init_xlua`'s arguments). The managed suite cannot run on such a
  library: its tests and Lua files are text.
- **`check_exports.py`** and **`check_android_elf.py`** with
  `XLUA_EXPECT_BYTECODE_ONLY=ON` fail a library that contains messages of Lua's
  lexer and parser (`managed_imports.PARSER_TEXT`) or lacks the built-in chunks
  (`BUILTIN_CHUNK_TEXT`), `OFF` one without the parser or with the chunks.
- **`sanitize.sh`** with `bytecode-5.3.6`, `bytecode-5.4.9` or `bytecode-5.5.1`
  in `VERSIONS` builds the bytecode-only library under the sanitizers and an
  uninstrumented compiler library next to it, and runs the three checks
  (`XLUA_EXPECT_BYTECODE_MODULES` from the preset unless set). The profiler test
  and the managed suite compile text and do not apply.
- **`Test/Native/chunkc_check.py <xlua_chunkc> <work dir>`** (2026-10-28)
  checks the chunk compiler `build/xlua_chunkc.c` itself: the runtime's
  chunks with every option, with none and stripped; every argument error;
  files it cannot open or read, a NUL byte; a chunk with every accepted form
  (attribute keys inside other words, `""`, CR LF lines, tabs, a backslash in
  the name, an empty string, a marker on the last line); each broken marker
  rule with its message; a chunk name twice; a header it cannot write, flush
  (`/dev/full`) or rename; and out of memory (`/dev/zero` under `RLIMIT_AS`).
  The last two are Linux only. `coverage.sh` runs it on the instrumented
  program of its bytecode-only build.
- **Lua's test suites** (`lua_tests.py`) compile text and do not apply either.
  The interpreter they build (`XLUA_LUA_INTERPRETER`) keeps the compiler with
  the option on, so they still pass with `XLUA_BYTECODE_ONLY=ON` in the
  environment (5.4.9 and 5.5.1, all three modes, 2026-10-18).

`native_regression.py`'s `native_modules` check covers the C modules that
register themselves in `package.preload` (`-DXLUA_LUASOCKET`, `-DXLUA_MIME`):
with LuaSocket, `socket.core` and `socket` are preload functions and `require`
returns the module; without it, neither name is in `package.preload` and
`require` fails with "module 'socket' not found" (`package.path` and
`package.cpath` are emptied for the check, so no installed module answers).
The same for `mime`. `XLUA_EXPECT_NATIVE_MODULES` (`socket,mime`, `socket` or
empty) says what the library was built with; without it the library's
exports decide. The managed suite's `CaseNativeModulesFollowTheBuild` checks
the same through `LuaEnv` and calls `LuaDLL.Lua.luaopen_socket_core`, now a C#
method, as a built-in loader; `CaseLuaSocketLoopback` runs only with
LuaSocket. `run_managed.py --native-modules ''` (or the variable) runs the
suite against a library built without LuaSocket; the default is
`socket,mime`, which the Unity editor suite also assumes. `sanitize.sh` with
`XLUA_LUASOCKET=OFF` (or `XLUA_MIME=OFF`) in the environment builds every
runtime that way and sets the expectation.

The `memory_snapshot_*` checks of `native_regression.py` (skipped, as
`check_exports.py` allows the two exports to be missing, for a library built
with `-DXLUA_MEMORY_SNAPSHOT=OFF`; `XLUA_EXPECT_MEMORY_SNAPSHOT=ON` or `OFF`
makes either script fail when the library was built the other way) cover the
memory snapshot walk (`xlua_memory_snapshot`) on every runtime, LuaJIT
included: keys of every type, cycles, weak tables, userdata with user values
or environments and xLua's C# object and struct userdata, objects with `__gc`,
upvalues, coroutine stacks and locals, registry references, a 200000-table
chain, `max_objects`, snapshots from a C function Lua called, from finalizers,
inside a coroutine and with a hook set, and an allocator that fails during the
walk.

The parts of `os`, `package` and the base library that the CMake options
`XLUA_OS_FULL`, `XLUA_LOADLIB` and `XLUA_DOFILE` leave out
([build/README.md](../build/README.md#parts-of-os-package-and-the-base-library))
have their own expectations, `XLUA_EXPECT_OS_FULL`, `XLUA_EXPECT_LOADLIB` and
`XLUA_EXPECT_DOFILE` (`ON` or `OFF`; `Tools/presets.py --expect-env` sets them,
and `sanitize.sh` passes the options of its environment on):
`native_regression.py`'s `library_parts` check and the managed
`CaseLibraryPartsFollowTheBuild` require each part whole or absent and as
expected (the case reads `os` as the library opened it, because
`luaunit_compat.lua` then gives the harness an `os.getenv` from C# when the
library has none), and `check_exports.py` and `check_android_elf.py` require
that a PUC Lua library with `XLUA_EXPECT_LOADLIB=OFF` imports no `dlopen`.
`hotfix_check.py`'s fixture loads its id map with `load`, as the minimal
library has no `dofile`.

The optional parts of the library, the CMake options `XLUA_MEMORY_SNAPSHOT`,
`XLUA_LEAK_CHECKER`, `XLUA_UINT64`, `XLUA_LUASOCKET`, `XLUA_MIME` and `XLUA_LIBS`
([build/README.md](../build/README.md#the-export-list-follows-the-options)),
follow one rule in the checks: each part is there with all of its exports or
with none (`managed_imports.py`, `OPTION_EXPORTS` and `LIB_EXPORTS`), and when
`XLUA_EXPECT_<OPTION>` (`ON` or `OFF`, for example
`XLUA_EXPECT_LEAK_CHECKER=OFF`), `XLUA_EXPECT_NATIVE_MODULES` or
`XLUA_EXPECT_LIBS` (`all` or a list such as `package,coroutine,table,string,math,debug`)
is set, the build must match it. `check_exports.py`, `check_android_elf.py`,
`profiler_smoke.py` and `native_regression.py` read them. Unset, the library's
exports decide. `native_regression.py`'s `uint64_table` check expects the
global `uint64` exactly when the library has it and checks that
`lua_pushuint64`/`lua_touint64` keep all 64 bits without it; without the table
the checks that only need `uint64.parse` to make a 64-bit value get a Lua
stand-in, and `parse` is skipped. Its `std_libraries` check expects exactly the
selected libraries as globals and in `package.loaded` after `luaopen_xlua`. The
checks tell LuaJIT from Lua 5.x by the global `jit`, so a LuaJIT library under
test needs `jit` in `XLUA_LIBS`. The managed suite's
`CaseUint64TableFollowsTheBuild` and `CaseStdLibrariesFollowTheBuild` read
`XLUA_EXPECT_UINT64` and `XLUA_EXPECT_LIBS` from the environment
(`run_managed.py` passes it on), and the `uint64` cases return early without
the table. The suite's own harness (luaunit) needs `os`, `string`, `table` and
`math`, and many cases `coroutine` and `debug`; without `io` it writes its case
lists through C#, and `testGetFuncToDelegate_Step_7_1`, which needs a file
handle, returns early. So `io`, `utf8` (and LuaJIT's `bit` and `ffi`) can be
left out for a managed run. `sanitize.sh`
passes `XLUA_LEAK_CHECKER`, `XLUA_UINT64` and `XLUA_LIBS` from its environment
to CMake and sets the expectations; it builds LuaJIT with the uint64 table.
With `XLUA_PRESET=minimal` it builds each runtime with the minimal preset's
native defaults (exports `all`, because the native checks call Lua functions
outside the managed list), takes the expectations from `Tools/presets.py`, and
runs the managed suite with `--define XLUA_PRESET_MINIMAL`.

`python3 Test/Native/plugin_variants_check.py --output <dir>` compiles the
selection rules of the native plugin variant selector
(`Assets/XLua/Core/Editor/PluginVariants.cs`, without UnityEditor) with `mcs`
and runs them with Mono: path parsing of `xlua-variants/<name>/`, the order
`XLUA_PLUGIN_<NAME>`, `XLUA_CORE_ONLY`, Development Build, the preset define
`XLUA_PRESET_MINIMAL`, default, the preset define errors, and groups without
a default plugin.

`python3 Test/Native/presets_check.py --output <dir>` checks the presets
without building a library: `Tools/presets.py --check` (the `<xlua-preset>`
blocks of the C# files are current); CMake configured for the host without a
preset, with `legacy` and with `minimal` from `-D` and from the environment, on
Lua 5.5.1 and LuaJIT, and with `core` on Lua 5.5.1 and 5.4.9, against the
table in `Tools/presets.py`; options from `-D` and the environment winning over
the preset; `core` on LuaJIT and Lua 5.3, `core` with a part of the bridge or
`io`, `XLUA_LUASCRIPT` on Lua 5.3, an unknown preset and a build directory
reconfigured with another preset refused; the bytecode options, off in every
preset: `XLUA_BYTECODE_ONLY` accepted from `-D` and the environment in legacy
and minimal (also on Lua 5.3) and refused on LuaJIT, in core and with
`XLUA_LUASCRIPT=ON`, and `XLUA_TRUSTED_BYTECODE` accepted with core and
`XLUA_LUASCRIPT=ON` and refused without the sandbox; and, with the .NET
SDK and Mono, `XLua.Mini` compiled deterministically with `XLUA_PRESET_MINIMAL`
equal byte for byte to `XLua.Mini` compiled with the defines it implies
(`--skip-assemblies` leaves that out). The Unity side compiles in `editor_compile_check.py`;
no Unity build has run it yet.

`python3 Test/Native/check_pinvoke_abi.py` needs no library: it compares every
`DllImport` in `Core/LuaDLL.cs`, `LuaDLLBridge.cs` and `DelegateBridge.cs` with the
C definition (the `xlua_core*.c` and bridge sources and the Lua 5.3.6, 5.4.9 and 5.5.1 headers), reducing each parameter and
result to pointer-sized, 32-bit, 64-bit, float, double or void. Native ABIs
tolerate an ignored result or an `int` passed for a `size_t`; WebAssembly links
by signature and traps instead, and on WebAssembly64 (Unity 6.6 builds with more
than 4 GB of maximum memory) pointers and `size_t` become 64-bit while `int`
stays 32-bit.

OpenHarmony has no host-architecture library, so `Test/Native/ohos_check.sh
<OpenHarmony native SDK> <output dir>` builds libxlua for each OpenHarmony ABI
and runs `Test/Native/plugin_smoke.c` (Lua core, `uint64`, struct packing,
`os.tmpname`, `io.popen`, file seeks) on Linux. The SDK's dynamic loader only
works on an OpenHarmony system, so the smoke test is linked statically from the
object files CMake compiled for `libxlua.so`. ABIs the host cannot execute run
through qemu-user. armeabi-v7a does not run under qemu-arm (a static
OpenHarmony hello world already stops with SIGILL), so CI runs x86_64 and
arm64-v8a only. The SDK's x86_64 `libc.a` reads the stack canary through FS
before it sets FS, so any static x86_64 program crashes at address 0x28 before
`main`; the x86_64 build therefore starts at `Test/Native/ohos_x86_64_entry.c`,
which gives FS a scratch thread block first (the file explains why). Loading
the `.so` itself needs a device or the emulator.

### LuaScript's C# tests

`Test/Core/CoreTests.cs` tests `LuaScript` (milestone 6 of
[core-luascript.md](../docs/design/core-luascript.md), section 8.5):
lifecycle and options, `LuaValue`, `Load`, syntax errors and chunk names,
`Call` with every argument shape and result type, dotted lookups, multiple
results, runtime errors and tracebacks, the instruction, time, interrupt and
memory limits, the thread check, two scripts on two threads, the hash seed,
`require`, `Dispose` inside a call, and an allocation probe: 70,000 calls with
primitive arguments and results must allocate 0 bytes
(`GC.GetAllocatedBytesForCurrentThread`, which the test first checks counts).
`Test/Core/RegisterTests.cs` tests the host functions of milestone 7
(section 8.6): `Register`, every `LuaCall` getter and result, Lua's argument
error messages, exceptions and their `InnerException`, nested calls (also in a
coroutine and under a limit), `Unregister`, dotted names across environments,
the typed shorthands, `LuaCall.Yield`, the limits and the memory rule around a
host function, the thread check, and a second allocation probe (80,000 host
function calls with primitive arguments and results, 0 bytes). It prints the
cost of a call from Lua to a host function.
`Test/Core/BufferTests.cs` (milestone 10, section 8.9) tests `LuaLibs.Buffer`
and `LuaBufferRef`: buffers from Lua and C#, the layout through every accessor
and `AsSpan`, bounds, handles and their disposal, the release of buffer results
on failed reads, the limits and an allocation probe; without the buffer library
(`XLUA_BUFFER=OFF`) each checks that buffers are refused, and
`XLUA_EXPECT_BUFFER=ON` or `OFF` makes the build explicit.
`Test/Core/TableTests.cs` (milestone 8, section 8.7) tests the handles:
`LuaTableRef` reads and writes by every key form and type, raw access,
`Pairs` and `IPairs` with borrowed values, handle lifetimes and generations,
`ToDictionary`, `LuaKey`, `LuaFunctionRef`, `Globals`, `NewEnvironment`,
`Compile`, `Register` into a table, `ErrorValue`, borrowed handles of host
functions, and a third allocation probe (`Pairs` over 10,000 keys and 6,000
typed reads and writes, 0 bytes). It prints the cost of the table operations.
`Test/Core/CoroutineTests.cs` (milestone 9, section 8.8) tests `Start` and
`LuaCoroutine`: steps and values, yields across `pcall`, metamethods and host
functions, errors and tracebacks, `Close` and `__close`, coroutines as
handles (script-made ones, borrowed values, `Retain`, script disposal), the
limits per step (instructions, time, `Interrupt`, memory), steps nested in host
functions, the thread check, the `IEnumerator` driver and the `Task` driver on a
single-thread `SynchronizationContext` (with cancellation), and a fourth
allocation probe (30,000 resumes with primitive values, 0 bytes). It prints
what `Start` and the drivers allocate and the cost of a step.
`Test/Core/LubanFixtureTests.cs` (milestone 11, section 8.10) loads the luban
`lua-lua` fixture of `Test/Core/Fixtures/luban-lua-lua` (four tables that
luban v5.1.0 generated, 50 KB of Lua; `SOURCE.md` there says how
`regenerate.py` makes them) through `require` under a 16 MB memory and a
50,000,000-instruction limit, and checks record counts, a hash of every
table and single values against `expected.txt`, which comes from luban's JSON
export of the same data. It prints the instruction count, memory peak and
load time. `run_core.py` passes the folder in `XLUA_CORE_FIXTURES`.

```sh
python3 Test/Native/run_core.py --plugin /path/core/libxlua.dylib --output /tmp/core-tests
python3 Test/Native/run_core.py --bridge --plugin /path/legacy-with-luascript/libxlua.dylib --output /tmp/core-bridge
```

`run_core.py` compiles the tests with the .NET SDK's Roslyn (mise.toml) from
`Assets/XLua/Core` alone, with `XLUA_GENERAL` and `XLUA_LUASCRIPT`, so nothing
in Core may need the bridge, and runs them on Mono (against Mono's 4.5
profile) and on .NET (against the SDK's reference pack), each with a timeout
that kills the process group and names the test that hung. Every run must pass
`EXPECTED_TESTS` (87; 91 with `--bridge`). The library needs the sandbox: the core preset's, or a
bridge library built with `XLUA_LUASCRIPT=ON`. `--bridge` compiles them with
the whole runtime (the `XLua.Mini` sources) and adds
`TestLuaEnvAndLuaScriptSideBySide` (both APIs in one process),
`TestLuaEnvStopsOnTimeWithAnInstructionLimit` and `TestLuaEnvAllowInterrupt`
(`LuaOptions.AllowInterrupt` on a `LuaEnv`), which need a bridge library
with `XLUA_LUASCRIPT=ON`. `--runtime mono|dotnet` runs one.
The tests do not run in the Linux container: its Mono declares
`ReadOnlySpan<T>`'s `ref readonly` members without the `modreq` that the
macOS build has, which Roslyn refuses.

## Lua's official test suites

`Test/Native/lua_tests.py` runs the test suites from <https://www.lua.org/tests/>
against the vendored Lua trees, with xLua's patches and build options:

```sh
python3 Test/Native/lua_tests.py --output /absolute/path/empty-dir
```

For each runtime the script downloads the archive pinned in its `SUITES`
table and stops unless the size and SHA-256 match. `--archives <dir>` takes
the archives from a directory instead (still checked). It then configures
`build/CMakeLists.txt` with `-DXLUA_LUA_INTERPRETER=ON`, which adds a
stand-alone `lua` target. That target compiles the same Lua core sources as
libxlua with the same definitions (`LUA_USE_LINUX` or `LUA_USE_MACOSX`,
`LUA_IDSIZE` 120, `LUAC_COMPATIBLE_FORMAT`, `XLUA_SANITIZE`), plus the vendored
`lua.c` and readline. It runs three modes, each in a fresh copy of the suite:

| Mode | Command | Interpreter |
|---|---|---|
| basic | `lua -e"_U=true" all.lua` (portable subset) | plain |
| complete | `lua all.lua`, with the C modules in `libs/` built by the script | plain |
| internal | `lua all.lua` with the `T` (testC) library and internal assertions | `-DXLUA_LUA_LTESTS_DIR=<suite>/ltests`: adds `ltests.c` and `LUA_USER_H="ltests.h"` to this target only |

A run passes when `lua` exits with 0 and prints `final OK`; the script prints
`XLUA_LUA_TESTS_PASS` when every run passes. `--versions`, `--modes`,
`--timeout` (per step, default 1800 s; on timeout the script kills the process
group and names the last file started), `--sanitize address,undefined` and
`--compatible-format` (Lua 5.3.6 with `LUAC_COMPATIBLE_FORMAT`) select the
rest. Linux needs `libreadline-dev`.

| Runtime | Suite | Size | SHA-256 |
|---|---|---|---|
| 5.3.6 | `lua-5.3.4-tests.tar.gz` | 103,438 | `b80771238271c72565e5a1183292ef31bd7166414cd0d43a8eb79845fa7f599f` |
| 5.4.9 | `lua-5.4.9-tests.tar.gz` | 137,260 | `7d971845f545ffc09fbb3128a86b2c6524161c70d0fdf0154a16e8c00c343fca` |
| 5.5.1 | `lua-5.5.1-tests.tar.gz` | 150,553 | `da07b543872dc0bb2ff12aabd0c248578d78df3eb6b67efdc537a46d455c7f31` |

lua.org publishes no 5.3.5 or 5.3.6 suite, and its page says to use the suite
of the closest release with the same minor version, so Lua 5.3.6 runs the
5.3.4 suite (its `ltests.c` also builds against 5.3.6).

The suites are test data: only the interpreters built here run them, the
script compiles `libs/*.c` itself instead of running their makefile, and it
puts the built `lua` first on `PATH` because `files.lua` starts a plain `lua`
through the shell. The runs get an empty stdin pipe (`files.lua` expects
`io.stdin:seek` to fail) and `LC_ALL=C`.

Four xLua patches change what the suites see, and the script adjusts for them
(`OS_EXECUTE_SHIM` and `ADJUST` in the script; nothing else in the suites
changes):

- **`os.execute` is removed** in every PUC runtime (build/LUA_PATCHES.md).
  The complete and internal modes first assert that `os.execute` is nil, then
  define a replacement on `io.popen` whose `close()` returns the same values
  (`luaL_execresult`). `main.lua` and the `files.lua` exit-status tests run
  through it. The basic mode needs no replacement.
- **`LUA_IDSIZE` is 120**, not upstream's 60. `errors.lua` checks the length
  of truncated source names against a hard-coded 60; the script changes the
  limit and the border cases to 120, so the test checks our limit.
- **`LUAC_COMPATIBLE_FORMAT`** (opt-in, 5.3): `calls.lua` checks the bytecode
  header byte by byte. With `--compatible-format` the expected header has
  format 1 and no `sizeof(size_t)` byte. `all.lua` runs most test files
  through `string.dump` and `load`, so this mode also exercises the custom
  format on the whole suite.
- **Errors in `__gc` are reported, not raised** (5.3.6, since 2026-10-28;
  build/LUA_PATCHES.md): `gc.lua` expects `collectgarbage()` to fail when a
  finalizer raises, and `api.lua` (internal mode) counts ten such failures.
  The script expects the collection to succeed, every finalizer to have run
  and no failure.

On macOS the script also skips four platform differences that stock Lua
shows too: `main.lua` (macOS readline is libedit, whose REPL does not echo
piped input as `main.lua`'s `-i` tests expect), the `/dev/full` checks in
5.5.1's `files.lua` (macOS has no `/dev/full`), `sh -c 'kill -s HUP $$'`
(macOS `sh` execs the last command, so the status is a signal, not an exit),
and one out-of-range `os.time` check in 5.4.9 and 5.5.1 (macOS `mktime`
accepts year 2^31 + 1899).

Results (2026-10-17): every mode of every runtime prints `final OK`, with no
failure left that our patches cause.

| Platform | 5.3.6 | 5.3.6 compatible format | 5.4.9 | 5.5.1 |
|---|---|---|---|---|
| Linux arm64, ubuntu:26.04 container (`xlua-analysis:26.04`), GCC 15.2 | 3 of 3 | 3 of 3 | 3 of 3 | 3 of 3 |
| Same, ASan + UBSan (`RelWithDebInfo`) | 3 of 3 | 3 of 3 | 3 of 3 | 3 of 3 |
| macOS 27 arm64, Apple clang, with the macOS skips above | 3 of 3 | 3 of 3 | 3 of 3 | 3 of 3 |

Each run takes a few seconds (up to 14 s under ASan). The internal runs print
no "testC not active" skips; the complete runs skip only the testC parts.
The `lua-tests` job in `.github/workflows/analysis.yml` (manual) runs the
Linux rows on `ubuntu-26.04` for 5.3.6, 5.3.6 with the compatible format,
5.4.9 and 5.5.1, first plain and then under ASan and UBSan. It has not run
on a hosted runner yet.

lua.org's suites target PUC Lua; LuaJIT has its own, below.

## LuaJIT's test suite

`Test/Native/luajit_tests.py` runs LuaJIT's test suite against
`build/luajit-2.1` in several build modes:

```sh
python3 Test/Native/luajit_tests.py --output /absolute/path/empty-dir
```

LuaJIT's repository has no tests. Mike Pall's tests are collected in
[LuaJIT/LuaJIT-test-cleanup](https://github.com/LuaJIT/LuaJIT-test-cleanup),
whose README says it is a workspace for tests that are not cleaned up yet.
The script pins its newest commit, `014708bceb70550a3ab8d539cff14d9085ca9cb8`
(2016-10-19; the repository has had no commit since). It downloads GitHub's
tarball of that commit and stops unless the size (112,237 bytes) and SHA-256
(`6003d032d74e48516526a5e81f1e40ea13072395af85c79b63aeffd415756fba`) match;
`--archives <dir>` takes it from a directory instead (still checked). The
README puts Mike Pall's tests in the public domain. Nothing is vendored: the
archive is test data, only the interpreters built here run it, and the script
compiles the suite's C helpers itself. Two other copies exist:
[tarantool/luajit](https://github.com/tarantool/luajit) keeps an adapted copy
in `test/LuaJIT-tests` for its fork and CMake build, and
[openresty/luajit2-test-suite](https://github.com/openresty/luajit2-test-suite)
targets OpenResty's fork. The script runs the upstream copy and uses
tarantool's as the reference when a test needs an update for current LuaJIT.

For each mode the script copies `build/luajit-2.1`, builds the stand-alone
`luajit` with LuaJIT's own Makefile and the flags of our build scripts (as
`make_linux64_luajit.sh` (`CFLAGS=-fPIC`) and `make_osx_luajit.sh`
(`BUILDMODE=static XCFLAGS=-fPIC`, macOS 12.0) do), and runs the suite in a
fresh copy:

| Mode | Extra `XCFLAGS` | Matches |
|---|---|---|
| default | none: GC64 on 64-bit, JIT, FFI | the Linux, macOS, Android and Windows ARM64 plugins |
| assert | `-DLUA_USE_ASSERT -DLUA_USE_APICHECK` (internal assertions) | a debug build |
| nojit | `-DLUAJIT_DISABLE_JIT -DLUAJIT_NO_UNWIND` | the interpreter-only VM that `lj_arch.h` selects for iOS |
| nx | `-DLUAJIT_USE_SYSMALLOC -DLUAJIT_DISABLE_JIT -DLUAJIT_DISABLE_FFI` | `make_nx64_luajit_gc64.bat`, without the console target |

A sixth mode, `nogc64` (`-DLUAJIT_DISABLE_GC64`, x86_64 only), matched the
old `make_win64_luajit.bat`; it was removed on 2026-10-28, when xLua stopped
supporting a 64-bit LuaJIT without GC64 (build/README.md, "LuaJIT GC64").

Without `--modes` it runs every mode the host can build: `nx` does
not run on Linux arm64. There GC64 keeps object pointers in 47 bits
(`checkptrGC`, `lj_def.h`) and glibc's `malloc` returns addresses above 2^47,
because the heap follows the PIE base at `0xaaaa...` in a 48-bit address
space. `lua_newstate` then fails with "not enough memory". LuaJIT's own
allocator asks `mmap` for low addresses instead.

Each mode has two parts:

- **runner:** `luajit test.lua +slow` in `test/`, the suite's own runner. It
  runs every `do --- name` block that the `index` files reach and that the
  build's tags allow, so `+jit` tests skip without the JIT and `+ffi` tests
  without the FFI. The script lists all 528 blocks and reports each one as
  run or as skipped with the tags that excluded it.
- **standalone:** the 65 files the suite has not converted to the runner
  (`misc/`, `lib/ffi/ffi_*.lua`, `lib/table/misc.lua`, `sysdep/`,
  `unportable/`), each run as `luajit <file>` in its own directory; exit 0
  passes. They expect an older layout: the script builds `test/src/ctest.c`
  and `cpptest.cpp` (`require("ctest")`, `ffi.load("../clib/ctest")`) and
  copies `test/common` to `lib/common` for `dofile("../common/ffi_util.inc")`.

`results.tsv` in each mode's directory lists every test with its status and
detail: PASS, FAIL, CRASH, TIMEOUT, SKIP, XFAIL (a known failure), XPASS (a
known failure that passed) or NOTRUN (after the runner stopped). A failure
must be in the script's `KNOWN_FAILURES` with its cause; any other failure,
or an XPASS, fails the run. The script prints `XLUA_LUAJIT_TESTS_PASS` when
every mode passes. `--timeout` (default 900 s) limits each build step and
runner run, and `--file-timeout` (default 300 s) each standalone file; on a
timeout the script kills the process group and names the last test started.
`--cross` and `--exec-wrapper` build for another architecture and run under an
emulator (see the x86_64 row below).

Tests not run, each with its reason in `results.tsv`:

- **By the runner's tags**, as the build decides: 18 in the default mode (12
  need `LUAJIT_ENABLE_LUA52COMPAT`, which our builds do not set; 5 need Lua
  5.2; and `lib/contents.lua --- ffi 2.1` is tagged `+fii`, a typo upstream, so
  it never runs), 140 without the JIT, 222 without the JIT and the FFI.
- **`sysdep/`**, which the suite's README leaves unresolved:
  `ffi_include_gtk.lua` (needs GTK headers), `ffi_include_std.lua` (runs
  `cc -E` and writes `/tmp/__tmp.c`) and `ffi_lib_z.lua` (zlib by name).
- **`sysdep/catch_cpp.lua` in nojit:** with `LUAJIT_NO_UNWIND` LuaJIT has no
  C++ exception interoperability (`doc/extensions.html`), so the C++ throw
  through Lua frames ends in `std::terminate`, as on iOS.
- **FFI files in nx**, which disables the FFI (22 files).

The suite's last commit predates some LuaJIT 2.1 changes. Where a test checks
the old behaviour, the script changes the expectation in its copy (`ADJUST`
in the script; tarantool/luajit made the same changes to its copy):

| File | Change | Upstream LuaJIT change |
|---|---|---|
| `lib/contents.lua` | `math.mod` and `string.gfind` are absent; `table` has `move` | de5568e0 (2017-03-17) removed the Lua 5.0 aliases; c1981676 (2017-01-17) added `table.move` |
| `misc/lightud.lua` (x64 only) | `lightud(2^47)` and `lightud(2^64-2048)` succeed | e9af1abe (2020-09-29), full-range 64-bit lightuserdata |
| `misc/stackov.lua` | the plain recursion checks only for "stack overflow", not a traceback | defe61a5 (2024-02-04) may leave no stack for the `xpcall` handler on overflow (interpreter; x64 also with the JIT) |
| `misc/debug_gc.lua` (runs only without the JIT) | no `debug.getinfo` checks inside the finalizer | 5c647754 (2025-11-10) runs finalizers on a separate `lua_State` |
| `sysdep/ffi_lib_c.lua` (Linux) | `ffi.load("pthread.so.0")` | none: glibc 2.34+ systems have no unversioned `libpthread.so` |

Known failures (`KNOWN_FAILURES`), neither caused by our build:

- **`lib/ffi/ffi_callback.lua` on macOS arm64**, an upstream LuaJIT defect.
  Apple's ARM64 ABI packs stack arguments by their size. `lj_ccall.c` does
  that for calls (`CCALL_PACK_STACKARG`, [LuaJIT#205](https://github.com/LuaJIT/LuaJIT/issues/205),
  fixed 2023-08-29), but `lj_ccallback.c` still reads every stack-passed
  callback argument from an 8-byte slot. The 10th `int` argument of a
  callback reads as 0 and the assertion at line 40 fails. With that one
  assertion removed the rest of the file passes. No LuaJIT issue covers
  callbacks. It affects FFI callbacks with more than eight integer arguments
  on macOS and iOS; xLua does not use the FFI.
- **`unportable/ffi_arith_int64.lua` on arm64**, as the directory name says:
  its checksums assume single-number mode (x86, x64), and ARM64 LuaJIT uses
  dual numbers (`LJ_NUMMODE_DUAL` in `lj_arch.h`), where `-1 * 0` is the
  integer 0 and prints `+0`, not `-0`. It passes on x86_64.

`build/luajit-2.1` carried no source patch until 2026-10-28; since then it
has one, the stack level clamp in `lj_debug.c` (see build/LUA_PATCHES.md),
so the run tests upstream LuaJIT with that patch and our build options.

Results (2026-10-18), runner tests passed / standalone files passed, plus
known failures:

| Platform | default | assert | nojit | nx |
|---|---|---|---|---|
| macOS 27 arm64, Apple clang 21 | 510 / 60 + 2 | 510 / 60 + 2 | 388 / 59 + 2 | 306 / 40 |
| Linux arm64, `xlua-analysis:26.04`, GCC 15.2 | 510 / 61 + 1 | 510 / 61 + 1 | 388 / 60 + 1 | not available |
| Linux x86_64 under qemu-user 10.2.1 in that container, `x86_64-linux-gnu-gcc` 15.2 | 510 / 62 | 510 / 62 | 388 / 61 | not valid |
| Linux x86_64, GitHub `ubuntu-26.04` runner (`analysis.yml` `luajit-tests`, run 39073496732, 2026-10-18) | 510 / 62 | 510 / 62 | 388 / 61 | 306 / 40 |

The removed `nogc64` mode passed on that runner too (510 / 62).

Every run takes a few seconds (about 7 s under qemu). The x86_64 row is a
functional check only. qemu-user, like Rosetta in OrbStack's Linux VM, gives
the guest the host's arm64 addresses (`malloc` blocks above 2^47) and ignores
`MAP_32BIT`, so `nx` crashes there. Under
Rosetta the assert build also caught `lj_mem_realloc` returning
`0xffff...`, so the Rosetta runs were discarded. The default, assert and
nojit modes use LuaJIT's allocator, which places its blocks itself, and ran. On a real x86_64 machine (the CI row) every mode passes with no
known failure, so `nx` works where it is used.
Real x86_64 results, including `nx`, come from the
`luajit-tests` job in `.github/workflows/analysis.yml` (manual; Linux x86_64
on `ubuntu-26.04` in every mode, macOS arm64 on `macos-26`), which has not
run on a hosted runner yet.

Not covered: sanitizers (LuaJIT under ASan needs `LUAJIT_USE_SYSMALLOC` and
more), Windows (`msvcbuild.bat`), 32-bit targets, the iOS, Android and
Switch targets themselves (nojit and nx only reproduce their VM options on a
desktop), the suite's `bench/` directory, and the libxlua bridge: the suite
runs the stand-alone `luajit`, built from the same tree and flags as the
`libluajit.a` that libxlua links.

## C# coverage

`Test/Native/coverage_cs.py --plugin <libxlua> --output <empty dir>` measures
line and method coverage of xLua's C# code. It runs `run_managed.py --coverage
--source-gen --source-gen-minimize`, which compiles every assembly that mcs
builds with `mcs -debug` (the source generator passes' test assembly, which
the .NET SDK's csc compiles, with `-debug:portable`) and runs each Mono process
under Mono's coverage profiler (`mono --profile=coverage`), then runs
`XLuaGenerate --luacats` on the source-generated test assembly the same way.
The profiler lists only the methods that were compiled, so
`Test/Native/CoverageLines.cs` reads each assembly's symbols, the `.mdb` with
`Mono.CompilerServices.SymbolWriter` or the portable `.pdb` with the pinned
Mono.Cecil of the tools build (Mono's own `System.Reflection.Metadata` is
missing from Debian's Mono), and the methods that never ran count as
uncovered. An assembly without either fails the run. A line is covered when one of its sequence points ran. The
profiler and the symbol reader ship with Mono; the script downloads nothing
beyond the Mono.Cecil package that `run_managed.py` already
fetches for the tools. The whole run takes about 20 seconds.

The reports go to `<dir>/coverage`:

- `reflection.txt`, `sourcegen.txt`, `sourcegen-minimize.txt`: one suite run each;
- `suite.txt`: the three suite runs together;
- `tools.txt`: the `XLuaGenerate` runs (`--migrate` on the suite's lists, once
  per source generator pass, and `--luacats`);
- `combined.txt`: all of them, and `summary.json` with every number;
- `annotated/`: each source with its hit count per line, `#####` for a line
  that never ran;
- `methods.txt`: every method with its missed lines, most missed first;
  `uncovered-methods.txt`: the methods that never ran.

Files are grouped as runtime (`Assets/XLua/Src` outside `Editor`), editor
(`Assets/XLua/Src/Editor`) and generated: the source generator's files of the
`sourcegen` and `sourcegen-minimize` passes (`<dir>/source-gen*/generated`),
which the reports total without a row per file (`summary.json` has the files).
Until milestone M11 the generated group counted Generate Code's output. Test code (`Test/`, `General/Src`) is excluded. `--report-only`
rebuilds the reports from an earlier run; `--define NAME` is passed to
`run_managed.py`.

What it does not measure:

- **Branches.** Mono's profiler counts sequence points only; see
  [branch coverage on .NET](#branch-coverage-on-net) below.
- **Code that is not compiled.** Regions under symbols that `run_managed.py`
  does not define (`HOTFIX_ENABLE`, `THREAD_SAFE`, `UNITY_*`,
  `XLUA_LOCK_METATABLES` and so on) are not in the totals, and neither are the
  Unity-only paths of the Editor code.
- **The other managed checks.** Only the suite and `XLuaGenerate` are
  profiled. `signature_check.py` (`SignatureLoader.cs`), `hotfix_check.py`
  (the hotfix runtime) and `luacats_check.py --validate` (`SetupValidator.cs`)
  test those files, but they show 0% here.

### Branch coverage on .NET

`Test/Native/coverage_cs_dotnet.py --plugin <libxlua> --output <empty dir>`
runs the same suite in the same three modes (reflection, `sourcegen`,
`sourcegen-minimize`), and the `XLuaGenerate` runs (`--migrate`, `--luacats`),
on .NET under
[coverlet](https://github.com/coverlet-coverage/coverlet), which counts each
outcome of every conditional branch (both sides of an `if`, each `switch`
case) as well as lines. It uses the .NET SDK given with `--dotnet`, else the
one pinned in the root `mise.toml` (`mise install dotnet`; found with
`mise which dotnet`), which must report exactly the pinned version, else
`dotnet` on PATH; any SDK 8.0 or later works there, and the script prints the
one it chose (`XLUA_DOTNET_SDK:`). It downloads
`coverlet.console` 10.1.0 once from nuget.org
into `Tools/.cache` (or `$XLUA_TOOLS_CACHE`), checked against its SHA-512;
the package is run with `dotnet`, not installed as a tool. The whole run
takes about 20 seconds.

What the .NET run needs, and how the script provides it:

- **Build:** SDK-style projects, written into the output directory, compile
  the source lists of `General/vs2013` (Debug, portable PDBs) for the SDK's
  own target framework (`--framework` to change it), the source generator
  passes with `XLua.SourceGenerator.dll` as an `Analyzer` item and its files
  written into `<mode>/generated`.
- **P/Invoke:** `LuaDLL` imports `xlua`, which .NET finds as `libxlua.dylib`
  or `libxlua.so` next to the assembly, as Mono does.
- **No Unity stubs:** `XLUA_GENERAL` compiles out every `UnityEngine` use.
- **Test runner:** `XLuaUnitTest` runs as a console app from `<dir>/General`,
  through reflection and through the generator's bindings, which bind .NET's
  base library here. The script fails unless both luaunit modes pass all
  `EXPECTED_TESTS` tests, as `run_managed.py` does.
- **Fixtures written for mcs:** `InParamsForLuaCallCS.cs` and
  `UpstreamForLuaCallCS.cs` spell out the attributes Roslyn emits for `in`
  and `ref readonly`, which Roslyn rejects in source (CS8335). The script
  compiles copies that use the keywords, which give the same metadata.
- **Culture:** .NET follows the system locale and Mono does not, and
  `tostring()` of a C# `decimal` or `float` uses the current culture (a comma
  on a Turkish or German system), so the suite runs with
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

The reports go to `<dir>/coverage` with the same names as above
(`reflection.txt` to `combined.txt`, `summary.json`), with lines and branches
per file; `branches.txt` lists every runtime and editor line with a branch
that never ran in any process, and each process keeps coverlet's
`coverage.json` and `coverage.cobertura.xml` in its own folder. The modes
compile the same sources into other assemblies and with other symbols, so
`suite` and `combined` match a branch by file, line and its order on that
line. coverlet counts every line a sequence point spans, where Mono counts
the first one, so its line totals are higher.

### Numbers

Measured on 2026-10-18 with the legacy generator's modes (`gencode`,
`gencode-minimize`), before milestone M11 replaced them with the source
generator's.

On macOS 27 arm64 with a Lua 5.5.1 plugin: lines and methods from
`coverage_cs.py` (Mono 6.14.1), branches from `coverage_cs_dotnet.py`
(.NET SDK 10.0.401 from `mise.toml`, coverlet 10.1.0). "Before" is main at
72715b4 (2026-10-18; the same numbers as at 4b8d6d7 on 2026-10-17, two runs
identical), with 349 tests. "After" adds `luaCallCsCoverage.lua` and its
fixes (2026-10-18, one run), with 391 tests; they pass in both luaunit modes
in every mode on both runtimes, and under Mono also with Lua 5.3.6, 5.4.9 and
LuaJIT plugins.

| Runtime, `Assets/XLua/Src` | Lines (Mono) | Methods (Mono) | Branches (.NET) | Lines (.NET) |
|---|---|---|---|---|
| reflection | 64.1% → 80.5% (5946/7383) | 66.7% → 81.6% (453/555) | 56.0% → 70.7% (1937/2741) | 65.5% → 81.1% (6295/7764) |
| gencode | 50.9% → 67.3% (4980/7398) | 64.5% → 79.6% (441/554) | 41.0% → 56.1% (1539/2741) | 53.0% → 68.4% (5310/7764) |
| gencode-minimize | 51.1% → 67.4% (5016/7438) | 64.8% → 79.8% (446/559) | 41.1% → 56.2% (1543/2745) | 53.2% → 68.5% (5346/7804) |
| suite, all three | 70.3% → **88.3%** (6584/7458) | 76.2% → 91.1% (511/561) | 62.1% → **78.4%** (2159/2753) | 71.9% → 88.7% (6943/7824) |
| combined, with the generator runs | 71.4% → 88.9% (6627/7458) | 77.8% → 92.3% (518/561) | 63.1% → 78.8% (2170/2753) | 72.9% → 89.3% (6987/7824) |

The totals differ per mode because each mode compiles other `#if` regions;
the fixes added a few lines. In the generator runs, whose input now includes
the new fixtures (`[DoNotGen]`, `[BlackList]`, a `GCOptimize` struct, an
interface with an indexer and an event):

| Editor, generator runs | Lines (Mono) | Methods (Mono) | Branches (.NET) |
|---|---|---|---|
| `Generator.cs` | 61.2% → 70.0% (856/1222) | 166 → 169/201 | 47.2% → 57.3% (456/796) |
| `LuaCATSGenerator.cs` | 80.9% → 82.1% (610/743) | 106 → 109/122 | 69.7% → 71.3% (442/620) |
| `SetupValidator.cs` | 0% (0/226), tested by `luacats_check.py` | 0/29 | 0% (0/128) |

The suite runs 66.3% of the generated wrapper lines (7824/11804; 65.4%
before) and 72.2% of the minimized ones (6933/9596; 71.2%), and 24.2%
(677/2792; 23.2%) and 45.7% (648/1418; 44.0%) of their branches. The new
fixtures added wrappers, so these totals grew too.

Per runtime file, suite (the three modes together), after, with the value
before when it changed:

| File | Lines (Mono) | Methods (Mono) | Branches (.NET) |
|---|---|---|---|
| `CodeEmit.cs` | 96.4% (1542/1599), was 86.9% | 48/49, was 47 | 91.5% (551/602), was 83.2% |
| `CopyByValue.cs` | 96.3% (79/82) | 23/23 | 5/6 |
| `DelegateBridge.cs` | 60.9% (42/69) | 6/10 | 54.5% (12/22) |
| `EmbeddedModules.cs` | 100% (9/9) | 1/1 | 2/2 |
| `GenAttributes.cs` | 38.1% (8/21) | 2/6 | none |
| `GenericDelegateBridge.cs` | 86.6% (142/164), was 0% | 9/10, was 0 | 55.0% (11/20), was 0% |
| `InternalGlobals.cs` | 100% (24/24) | 1/1 | none |
| `LuaBase.cs` | 100% (60/60), was 65.0% | 10/10, was 8 | 92.9% (13/14), was 50.0% |
| `LuaDLL.cs` | 84.3% (258/306) | 28/32 | 75.4% (95/126) |
| `LuaEnv.cs` | 83.3% (219/263), was 73.4% | 27/30, was 20 | 58.7% (27/46) |
| `LuaException.cs` | 100% (2/2) | 1/1 | none |
| `LuaFunction.cs` | 96.9% (125/129), was 36.5% | 11/11, was 6 | 100% (22/22), was 40.9% |
| `LuaTable.cs` | 97.6% (206/211), was 39.8% | 23/23, was 11 | 93.8% (30/32), was 37.5% |
| `MethodWarpsCache.cs` | 84.8% (336/396), was 82.1% | 17/19 | 79.3% (165/208), was 78.8% |
| `ObjectCasters.cs` | 85.3% (440/516), was 76.9% | 66/70, was 54 | 65.5% (169/258), was 58.5% |
| `ObjectPool.cs` | 100% (100/100), was 62.0% | 11/11, was 8 | 90.9% (40/44), was 36.4% |
| `ObjectTranslator.cs` | 89.7% (1049/1169), was 75.7% | 97/105, was 87 | 82.0% (351/428), was 62.0% |
| `ObjectTranslatorPool.cs` | 97.7% (43/44) | 6/6 | 8/8, was 7/8 |
| `RawObject.cs` | 100% (37/37), was 0% | 12/12, was 0 | none |
| `SignatureLoader.cs` | 0% (0/45), tested by `signature_check.py` | 0/6 | 0% (0/14) |
| `StaticLuaCallbacks.cs` | 83.0% (670/807), was 39.6% | 35/38, was 26 | 73.4% (229/312), was 24.2% |
| `TemplateEngine/TemplateEngine.cs` | 80.7% (113/140) | 18/22 | 73.1% (19/26) |
| `TypeExtensions.cs` | 100% (66/66), was 60.6% | 13/13 | 100% (22/22), was 4.5% |
| `Utils.cs` | 84.6% (1014/1199), was 73.9% | 46/52, was 43 | 71.7% (388/541), was 63.2% |

Fewest branches covered after the new suite, apart from `SignatureLoader.cs`
(tested by `signature_check.py`, not profiled):

- `DelegateBridge.cs`, 12 of 22, and `GenericDelegateBridge.cs`, 11 of 20:
  the hotfix bridge list (`HotfixDelegateBridge`, compiled but used only
  with `HOTFIX_ENABLE`), `GetDelegateByType` (overridden by generated code),
  the error branch of each generic `Action`/`Func` arity but the one-parameter
  ones, and the non-generic `Action()`, which a `void()` delegate never
  reaches because `System.Action` always has a bridge;
- `LuaEnv.cs`, 27 of 46: the error branches of the constructor, `DoString`,
  `LoadString` and `Dispose`, and the `UNITY_*` and `THREAD_SAFE` paths that
  are compiled out;
- `ObjectCasters.cs`, 169 of 258: the other alternatives of the generated
  type checks (a userdata of a struct type with no C# object, `nil` for each
  kind of type) and some caster fallbacks;
- `Utils.cs`, 388 of 541, and `StaticLuaCallbacks.cs`, 229 of 312: mostly
  the `catch` blocks that turn a C# exception into a Lua error, one per
  callback, and the private-access and reflection-wrapper branches for member
  kinds the fixtures do not have.

The untested areas are listed in the
[backlog](../docs/design/backlog.md#tests-and-ci).

## Static analysis, coverage and fuzzing

These cover xLua's own C files in `build/` (the bridge `xlua.c`,
`i64lib.c` and `memory_leak_checker.c`; the core layer `xlua_core.c`,
`xlua_core.h`, `xlua_core_limits.c`, `xlua_core_sandbox.c`,
`xlua_core_buffer.c` and `xlua_core_nobc.c`; `xlua_libs.c`,
`xlua_noparser.c` and the host program `xlua_chunkc.c`); the vendored Lua,
LuaJIT and LuaSocket code is not analyzed. Each script takes an empty output
directory and runs on Linux with Clang. `Test/Native/analyze.sh` runs
clang-tidy (`build/.clang-tidy`) and `clang --analyze` for Lua 5.3.6, 5.4.9,
5.5.1 and LuaJIT, and fails on any finding; suppress an intentional pattern
with `NOLINT(check): reason` on the line.

`Test/Native/coverage.sh` builds with `-DXLUA_COVERAGE=ON` and Lua's C API
checks (`-DXLUA_APICHECK=ON`, `LUA_USE_APICHECK` with `assert` kept on, like
`sanitize.sh` and the fuzz targets), runs the tests of each build, and prints
llvm-cov line, function and branch coverage per runtime; `branches.txt` in
each runtime's directory lists every branch, an untaken side as `True: 0` or
`False: 0`. It needs Mono like `run_managed.py`. Each runtime measures the
files its build compiles (`VERSIONS`, default all ten):

| Runtime | Build | Tests | Files |
|---|---|---|---|
| `5.3.6`, `5.4.9`, `5.5.1` | legacy | native checks, profiler test, managed suite | `xlua.c`, `xlua_core.c`, `xlua_core.h`, `xlua_core_limits.c`, `i64lib.c`, `memory_leak_checker.c` |
| `core-5.4.9`, `core-5.5.1` | `XLUA_PRESET=core`, every export | `check_exports.py --preset core`, `native_regression.py` (the core checks), `core_smoke.c` | `xlua_core.c`, `xlua_core.h`, `xlua_core_limits.c`, `xlua_core_sandbox.c`, `xlua_core_buffer.c`, `xlua_core_nobc.c` |
| `core-tp-5.5.1` (since 2026-10-28) | the same with `-DXLUA_LIMITS_TEST_POINTS` | the same, with `XLUA_EXPECT_TEST_POINTS=ON`; `core_regression.py`'s `check_core_stop_window` runs | the same |
| `minimal-5.3.6`, `minimal-5.4.9`, `minimal-5.5.1` | `XLUA_PRESET=minimal`, every export | `check_exports.py`, `native_regression.py` | `xlua_libs.c` (a reduced `XLUA_LIBS` builds it; the legacy runs measure the rest) |
| `bytecode-5.5.1` | `XLUA_BYTECODE_ONLY=ON` | the bytecode-only native checks, `bytecode_luaenv.py`, `chunkc_check.py` | `xlua_noparser.c`, `xlua_chunkc.c` (instrumented too); `xlua.c` reported, not gated |

**The gate (since 2026-10-28):** `coverage.sh` fails when a file's branch
coverage is below its minimum, after every runtime has been measured, and
prints an `XLUA_COVERAGE_GATE` line per runtime and file
(`Test/Native/coverage_gate.py` reads llvm-cov's `summary.json`; a file the
build does not have fails the gate, `FILE@RUNTIME=PERCENT` sets one
runtime's minimum). Each kind of runtime has its variable; empty turns that
kind's gate off:

| Variable | Default |
|---|---|
| `MIN_BRANCH_COVERAGE` (legacy) | `xlua.c=100 xlua_core.c=100 xlua_core_limits.c=100 xlua_core.h=100 i64lib.c=100 memory_leak_checker.c=83` |
| `CORE_MIN_BRANCH_COVERAGE` | `xlua_core.c=100 xlua_core.c@core-5.4.9=99 xlua_core_limits.c=100 xlua_core_limits.c@core-tp-5.5.1=84 xlua_core.h=80 xlua_core_sandbox.c=97 xlua_core_sandbox.c@core-tp-5.5.1=100 xlua_core_buffer.c=100 xlua_core_buffer.c@core-5.4.9=99 xlua_core_nobc.c=100` |
| `MINIMAL_MIN_BRANCH_COVERAGE` | `xlua_libs.c=100` |
| `BYTECODE_MIN_BRANCH_COVERAGE` | `xlua_noparser.c=100 xlua_chunkc.c=100` |

The analysis workflow's `coverage` job runs every runtime with the defaults
and uploads `report.txt`, `functions.txt`, `branches.txt`, `summary.json`
and the HTML. Measured on 2026-10-28 with `coverage.sh` in
`xlua-analysis:26.04` (`docker run --memory 6g`, Linux arm64, Clang 21.1.8,
Mono 6.14.1, reflection only, no .NET SDK), branches taken of all:

| File | 5.3.6 | 5.4.9 | 5.5.1 |
|---|---|---|---|
| `xlua.c` (legacy) | 342 of 342 | 356 of 356 | 356 of 356 |
| `xlua_core.c` (legacy) | 72 of 72 | 82 of 82 | 94 of 94 |
| `xlua_core.h` (legacy) | 6 of 6 | 20 of 20 | 20 of 20 |
| `xlua_core_limits.c` (legacy) | no branches (stubs) | 150 of 150 | 150 of 150 |
| `i64lib.c` | 8 of 8 | 8 of 8 | 8 of 8 |
| `memory_leak_checker.c` | 253 of 302 (83.8%) | 254 of 304 (83.6%) | 252 of 300 (84.0%) |
| `xlua_core.c` (core) | | 123 of 124 | 136 of 136 |
| `xlua_core.h` (core) | | 16 of 20 | 16 of 20 |
| `xlua_core_limits.c` (core) | | 152 of 152 | 152 of 152 |
| `xlua_core_sandbox.c` | | 556 of 570 (97.5%) | 557 of 570 (97.7%) |
| `xlua_core_buffer.c` | | 101 of 102 | 102 of 102 |
| `xlua_core_nobc.c` | | no branches | no branches |
| `xlua_libs.c` (minimal) | 2 of 2 | 2 of 2 | 6 of 6 |
| `xlua_noparser.c` (bytecode) | | | no branches |
| `xlua_chunkc.c` (bytecode) | | | 166 of 166 |

Every file is at 100% of functions except `xlua.c` (98.3%: `xlua_objlen`
and `xlua_pushcstable`, exports that no test calls) and the stubs of
`xlua_core_limits.c` on 5.3.6. The bytecode-only code of `xlua.c` (the
`lua_setfenv` copy and the built-in chunk loader) has every branch taken in
the bytecode run; the rest of `xlua.c` there is the bridge, which the
managed suite cannot reach in a library without a compiler.

What is below 100, and why:

- **`xlua_core.c` on core 5.4.9, 1 branch:** the trampoline passes a string
  argument of 2 GB or more as its first 2 GB - 1 (`read_arg`). Lua 5.5 makes
  such a string without memory (`lua_pushexternalstring` over an untouched
  `mmap`, `core_raw_api`); Lua 5.4 has no external strings, and the test
  would need 2 GB of real memory. **Owner decision of 2026-10-28 (Q51):**
  these 2 GB-string branches of Lua 5.4 (here, in the buffer library and in
  the sandbox) stay gated at 99: Lua 5.4 has no external strings, and Lua
  5.5 runs the same code at 100.
- **`xlua_core_buffer.c` on core 5.4.9, 1 branch:** `buffer.fromstring` of
  a string over 1 GB, for the same reason.
- **`xlua_core_sandbox.c`, 13 branches on both versions, and one more on
  5.4.9:** the 13 are `xlua_core_call`'s handling of a stop that arrives
  after `call_body`'s last stop check and before the call ends: no Lua code,
  hook or finalizer runs in that window, so only another thread's Interrupt
  or watchdog reaches it. `check_core_stop_window` takes all of them in a
  build with `-DXLUA_LIMITS_TEST_POINTS`. **Owner decision of 2026-10-28
  (Q50):** the coverage run builds it too, as the `core-tp-5.5.1` runtime,
  and gates the sandbox at 100 there (570 of 570 on 2026-10-28); the 97 of
  the other core runtimes stays, for the window. The one on 5.4.9 is a result
  string of 2 GB (as above). The two fixes of the same day (a coroutine error whose
  `__tostring` raises, and the exports on an exactly full stack) brought
  branches that are all taken, and removed the two non-string message checks
  of `xlua_core_call` that only the first bug reached: 540 and 541 of 554
  before, the same 14 and 13 missed after. The plain call's protected
  `__tostring` (owner decision Q54, the same day) kept the count at 570.
- **`xlua_core_limits.c` on `core-tp-5.5.1`, 31 branches:** the test
  points' own code, which only that build compiles: the null check of each
  point that no check holds (the hook's, the switch's, the stop check's) and
  `xlua_core_debug_state`, which only `core_stop_latency.c` prints. Gated at
  84 there (179 of 210 after the merge with main of 2026-10-28, 173 of 204
  before it); the file's own branches are at 100 in `core-5.5.1`.
  **Owner decision of 2026-10-28 (Q61):** the gate stays at 84 for
  `core-tp-5.5.1`. The missed branches are test-point-only code, and
  `core-5.5.1` holds the file at 100, so no test is added for them.
- **`xlua_core.h` in the core runs, 4 branches:** `xlua_core_enter` and
  `xlua_core_leave` on a state without the core allocator, and a nested
  entry on another thread than the running one. Every entry of a core
  library is the sandbox's, on a core state and on the running thread
  (`active_thread`); the bridge reaches both (the legacy runs: 20 of 20).
- **`memory_leak_checker.c`, about 50 branches:** the native allocation
  failures and size limits of the memory snapshot walk, a thread without
  stack space, argument checks, and one check inside the vendored
  `api_incr_top` macro (`LUA_USE_APICHECK`).
- **Not measured:** LuaJIT (no LuaJIT runtime in `coverage.sh`: the LuaJIT
  parts of `xlua.c`, `xlua_libs.c` and `i64lib.c`), `xlua_libs.c`'s LuaJIT
  half, the `XLUA_TRUSTED_BYTECODE` build of the sandbox and `xlua_core_nobc.c`,
  and `build/xlua_apicheck_msvc.c`, an MSVC-only initializer without branches
  (`XLUA_APICHECK` test builds on Windows), which no Clang coverage build
  compiles.

How the files got there (2026-10-28): `xlua_core_check_stop` is built only
with the sandbox (`XLUA_SANDBOX`), the one place that calls it, so the
bridge runs no longer show its ten branches; `native_regression.py`'s
`csharp_wrapper_error` raises an error through the `GEN_CODE_MINIMIZE`
trampoline, which only the source generator's GCM pass reached before, so
the gate needs no .NET SDK and the container and the hosted runner agree;
`open_selected_libs` passes load and preload masks to Lua 5.5's
`luaL_openselectedlibs`; `core_smoke.c`'s `check_coverage_edges`,
`core_regression.py`'s `core_raw_api` and `core_sandbox_edges`, and
`chunkc_check.py` take the edges of the core and bytecode-only builds (see
their entries above). Checks that nothing could fail went: `xlua_chunkc.c`'s
allocation checks now share one (`checked_realloc`, which Lua's allocator
uses too), the bytecode-only `lua_setfenv` copy's dump checks, and five in
the sandbox (`type_of`'s range, a trampoline without an upvalue, a full
coroutine stack, a text chunk without `_ENV`, and an ERRRUN that is not a
string). Before that day the gate was `xlua.c=100 xlua_core.c=98
xlua_core_limits.c=93` for the legacy runs only, from these values
(reflection only): `xlua.c` 338, 352 and 352 of all; `xlua_core.c` 63 of 64,
73 of 74, 85 of 86; `xlua_core_limits.c` 152 of 162 on 5.4.9 and 5.5.1; and
the core runs (no gate) had `xlua_core.c` 113 of 124 and 125 of 136,
`xlua_core_limits.c` 122 of 154 and 121 of 154, `xlua_core_sandbox.c` 522 and
521 of 566 and `xlua_core_buffer.c` 95 of 102.

Other notes: `xlua.c`'s branch tests are mostly in `native_regression.py`
(struct helpers and accessors, indexer closures and the limit of their
`BaseType` walk, path access, the C# function wrappers, `lua_setfenv`,
`xlua_touint`, the integer rule, and the checked accessors and cache lookup
of the bridge checks); the rest are managed cases in `luaCallCs.lua`. The
`entry.stop != 0` side of `lua_pcall`'s check for a call stopped after its
last Lua instruction is taken without threads by
`core_stop_after_last_instruction` and
`CaseLuaOptionsStopAfterLastInstruction`
([core-stop-latency.md](../docs/research/core-stop-latency.md#the-stop-after-the-last-instruction-tested-2026-10-28)).
`xlua_core.h`'s line coverage is low because llvm-cov counts every
translation unit's copy of its inline functions, and some files compile
copies they never call. On macOS with Apple clang, `i64lib.c` shows one more
branch, from the fortified `snprintf` macro in `uint64_tostring`. One branch
that the gate counts is not deterministic: the retry of
`xlua_core_limits.c`'s spinlock (`core_lock`) is taken only when another
thread holds the lock. The stop-latency checks, whose watchdog and Interrupt
threads poll while the owner thread's hook and C budget take the lock, take
it in every run so far (2026-10-28: 72k and 200k times in the legacy runs of
5.4.9 and 5.5.1, 95 and 55 times in the core runs); a run that never meets
the lock fails the gate on that branch alone.

The `coverage` job also runs by itself, on pushes to `main` and on pull
requests that change `build/`, `coverage.sh`, `coverage_gate.py`, the Mono
installer `.github/scripts/mono.sh` or `analysis.yml` (owner decision
2026-10-28); the workflow's other jobs stay manual (`.github/CI.md`). Its
path filter is narrower than the tests it runs: a change that lowers
coverage only through them shows in the next run. With a .NET SDK the source
generator's passes run too; the gate does not need them. Since
`build/CMakeLists.txt` records sources as `build/...`
([reproducible builds](../build/README.md#reproducible-builds)),
`coverage.sh` passes `-path-equivalence=build,<checkout>/build` to llvm-cov.

`Test/Fuzz/run.sh
<seconds>` builds four libFuzzer targets (five on Lua 5.4 and 5.5) with ASan and UBSan: Lua chunks loaded
through `xluaL_loadbuffer` in a sandbox env that also exposes the native path,
global, type id and `uint` helpers; the `uint64` library and the C helpers
that marshal `long`, `ulong`, `uint` and `int`, with the integer rule of
`xlua_tointeger64`; struct pack/unpack (up to
`float6` and `decimal`), accessors given values of every Lua type (setters
accept only numbers and numeric strings, and integer fields only those with
an integer value) and
arguments that are not structs (with the tag-checking accessors of
`xlua_check_struct_accessors` when the last input byte is odd, which also
refuse a struct header without the tag; since 2026-10-28 also a userdata whose
header claims more bytes than its block and one shorter than the header),
and the type id helpers with `xlua_tostructbuffer`; and dotted-path,
single-key and global table access, also on values that are not tables. The Lua
chunk target also calls the memory snapshot walk from the fuzzed chunk
(`snapshot()`) and after it, and checks the layout of the result. The `core`
target (`fuzz_core.c`, Lua 5.4 and 5.5) is LuaScript's sandbox on the core
preset's sources (no bridge, the bytecode stubs, the C budget hooks): each input
is a chunk for `xlua_core_call` in a fresh sandbox with a 1 MB memory limit and a
100,000-instruction limit, patterns, `load` and `require` included (the resolver
returns the module name as its text); the first byte picks a new environment,
`require`, a time limit, small slices and no `buffer` library (open otherwise),
and a global `f` the chunk defines is called with the chunk's results, buffer
handles included, which must have their length and are released at the end. It fails on a status
`xlua_core_call` does not document and on a peak above the limit plus the 16 KB
error reserve, and a libFuzzer timeout is a limit bug. 180 seconds each on Lua
5.5.1 and 5.4.9 (2026-10-18, Linux arm64 container): about 409,000 and 303,000
runs, no finding; libFuzzer's peak RSS reached 1.0 and 1.8 GB of its 2 GB limit.
The host function `hset` (bit 5) sets a key of a new table through
`xlua_core_handle` from inside the host function, so a nil or NaN key raises
there; it aborts if that runs Lua code or moves the host function's frame
(core-luascript.md, section 8.12). The seeds `len_loops.lua` and
`host_handle_errors.lua` start the fuzzer on the C loops a `__len` lengthens
and on raising handle operations under `xpcall`. After them, 180 seconds each
(2026-10-18, same container): about 237,000 runs on 5.5.1 and 252,000 on
5.4.9, no finding, peak RSS 1.1 GB. `FUZZ_OUT` and
`LUA_VERSION` select the output directory and runtime; `LUA_VERSION=luajit` (or
`luajit-uint`, built with `UINT_ESPECIALLY`) runs only the `uint64` target, which
there also checks the LuaJIT-only helpers and `..` on the int64 userdata. A crash leaves a
reproducer in `$FUZZ_OUT/<target>/`. Since 2026-10-28 `run.sh replay` (in place of the
seconds) runs each target once over its committed seeds (`-runs=0`) and fails on the first
seed that crashes, `run.sh build` only builds and prepares the seeds, `FUZZ_JOBS=N` runs N
libFuzzer processes per target (since the same day in fork mode, `-fork=N
-ignore_crashes=0 -ignore_timeouts=0 -ignore_ooms=0`; before, `-jobs=N -workers=N`),
`FUZZ_RSS_MB` sets `-rss_limit_mb` (default 2048), `FUZZ_MERGE=1` merges the grown corpus
with `-merge=1` into a fresh directory that replaces it, and `FUZZ_INSTRUMENT=coverage`
builds with source-based coverage instead of the sanitizers (`corpus_coverage.sh`, below).
`run_bridge.sh` takes the same modes and variables.

`Test/Fuzz/run_bridge.sh <seconds>` runs the adversarial bridge target
(`fuzz_bridge.c`, the crash sweep of the
[backlog](../docs/design/backlog.md#tests-and-ci)) on one preset and runtime:
`XLUA_PRESET` (legacy, minimal or core) and `LUA_VERSION` (5.3.6, 5.4.9,
5.5.1 or luajit; core needs 5.4 or 5.5). It builds libxlua with CMake as
the preset builds it, plus ASan, UBSan, Lua's API checks and libFuzzer's
coverage, and links the target against it. A Lua driver reads each input as
a list of operations: it builds a pool of values of every kind (boundary
and huge integers, NaN and infinities, numeric and odd strings, tables with
metatables, light userdata, other libraries' userdata, xLua's struct and C#
object userdata, LuaJIT's int64 userdata, functions, coroutines, earlier
results), calls native functions with arguments from the pool, runs Lua
operators and the `debug` library on the pool, collects garbage and
replaces globals. The natives of the bridge presets are `xlua.*`, the
accessors `xlua.genaccessor` returns (default and, with bit 1 of the first
byte, checked), the `uint64` table and LuaJIT's int64 metamethods, the
helpers C# calls (through C wrappers that call them as `LuaDLLBridge.cs`
does: value readers, the protected table helpers, `xlua_pnext`,
`xlua_plen`, the userdata cache, the push functions, `CopyByValue`'s pack
and unpack, `lua_setfenv`, `xlua_getloaders`, `pcall_prepare`, `xlua_gc`,
the memory snapshot), the class and object indexers, and the C# function
trampolines around C functions that do what C# functions do. Bit 0 of the
first byte makes the state a core state with a memory limit and, on Lua 5.4
and 5.5, an instruction limit, as `LuaEnv(LuaOptions)` does. In the core
preset the driver runs in LuaScript's sandbox, and the natives are the
buffer library, the sandbox's coroutine functions and host functions whose
C dispatcher returns every frame status, invalid results, yields and
strings from its own memory, and runs handle operations on its arguments.
Besides crashes, sanitizer reports, API check failures and hangs, the target
fails when a helper that C# calls outside a protected call raises an error
other than a memory error, when a helper leaves the stack other than its
caller expects, and when a struct header changes. A hang is a libFuzzer
timeout (`-timeout=20`): the state's instruction limit is a count hook, and
the driver wraps `debug.sethook` and `xlua.sethook` so that a hook the input
sets still charges a budget of 10^6 and removing the profiler puts the limit
back; on LuaJIT the JIT compiler is turned off once a profiler function is set
(compiled code calls no hook), so the compiler is not fuzzed. An endless loop
inside a hook function still runs without a limit (hooks are off there). The known crashes of
[crash-inventory.md](../docs/research/crash-inventory.md) (items 8, 10, 18,
22 and 23: foreign userdata with a struct header, `debug.getregistry`,
`debug.setupvalue` on a trampoline's C function and on a class indexer's
fields, `__gc` errors on Lua 5.3, `debug.setmetatable` on io files on
LuaJIT) are left out unless
`FUZZ_TRICKS=1`; `FUZZ_BRIDGE_TRACE=1` (2, 3) on a reproducer says how the
driver ended (and lists every call). The seeds are 16 fixed random inputs
of 1 KB, which the script writes, and `Test/Fuzz/corpus/bridge`: the
reduced input of crash-inventory item 27 (on legacy Lua 5.3.6 it replaces the
metatable of an all-weak `_G` between GC steps), the two timeouts of the
first weekly run (`call-chain-cycle`, item 30, from minimal 5.4.9, and
`profiler-call-loop`, from legacy LuaJIT, not reduced) and `shr-mininteger`
(2026-10-28, 9 bytes reduced with `-minimize_crash=1` from 1,039: on legacy
Lua 5.3.6 before the `OP_SHR` patch, UBSan's "negation of
-9223372036854775808" at `lvm.c:986`), and since 2026-10-28 the nine `ext-*`
seeds of the extended operations (below). Every native-build replays them on
legacy 5.5.1 and LuaJIT, and since the same day on legacy 5.3.6 and minimal
5.4.9 too, where three of the reproducers came from.
It runs for 60 seconds in every native-build (`sanitizers` job, legacy,
Lua 5.5.1, after the replay of every target's seeds), and for 30 minutes every Monday in `analysis.yml`'s `fuzz-bridge`
job (owner decision 2026-10-28) on legacy 5.5.1, legacy LuaJIT, minimal 5.4.9
and core 5.5.1, with four libFuzzer processes since the decisions below (in fork mode
since 2026-10-28). The weekly job keeps the corpus it grows, merged, in the Actions cache
(one entry per configuration), so each week starts from the last; a crash
uploads its reproducer and the logs as an artifact for 30 days. Add a minimised
reproducer of every fixed crash to `native_regression.py` or the seed corpus.
On 2026-10-28 (Linux arm64 container, Clang 21.1.8) the target ran for about
2 hours 40 minutes of CPU in fork mode: legacy 15 minutes on each runtime,
minimal 10, core 15 on 5.4.9 and 5.5.1, plus re-runs after the fixes. It
found five crashes that were fixed (items 25 to 29 of
[crash-inventory.md](../docs/research/crash-inventory.md): `xlua_getloaders`
raising, a host function's type error past the frame, Lua 5.3.6's
use after free for the metatable of an all-weak table, a light userdata read
as LuaJIT's int64, and LuaEnv's message handler recursing on LuaJIT), and
an overflow in Lua's `debug.traceback` (item 24, an owner question). Each
input takes 10 to 80 ms (up to 400 calls): 30 to 60 runs per second on the
bridge presets with Lua 5.x, 120 to 140 on the core preset and about 250 on
LuaJIT. The corpora reached 79%
of `xlua.c`'s functions and 71% of its branches on legacy Lua 5.5.1, 83%
and 75% on LuaJIT. A 60-second run of legacy Lua 5.5.1 is part of the
sanitizer job in `native-build.yml`. The first weekly run (2026-10-28) timed out
on minimal 5.4.9 (item 30: a cycle of `__call` metamethods hung Lua 5.4 in
C, now limited in the vendored 5.4.9) and on legacy LuaJIT (the input
replaced the instruction limit's hook with `xlua.sethook`'s, then ran an
endless tail call loop; the driver now keeps a limit on the hooks it sets).
After the fixes, 10 minutes each (Linux arm64 container): 31,984 and 96,048
runs, no finding. The many "ERROR in finalizer" lines in LuaJIT's `fuzz.log`
are errors of the `__gc` metamethods the driver sets from its pool (LuaJIT
prints them and goes on), not a finding; on arm64 that configuration's RSS
reached 1.6 GB in 10 minutes, with the old driver too.

**Extended operations (2026-10-28).** The corpus coverage of the decisions
below showed whole areas the driver had no operation for (the list at the
end of "Fuzzing in CI and corpus coverage"). Inputs whose first byte has bits
2 and 6 set (`(b & 0x44) == 0x44`) now get four more driver operations and
ten more natives; every other input reads its bytes as before, so the
committed reproducers keep their meaning (the three from Lua 5.3.6 and
5.4.9 make the same calls as with the old driver in their own
configurations). The
operations: the limits and stop API (`xlua_core_interrupt`, once or twice,
`xlua_core_watch`, `xlua_core_get_stats` against the limits the harness set,
`xlua_core_set_memory_limit` from inside a C# function, which then
allocates over the new limit so that the trampoline raises the overdraft,
and `xlua_core_set_limits` while a call runs, as C#'s InstructionLimit
setter allows); `CopyByValue`'s pack and unpack of all 12 field kinds
(`float3` to `float5` included) at any offset, which must succeed exactly
when the field fits, return what the struct holds and round-trip; the
`_checked` push functions, also with a registry reference that holds a
number, `xlua_tostruct` and `xlua_ref_indirect`/`xlua_getref_indirect`;
`lua_setfenv` on functions whose `_ENV` is not upvalue 1 and on stripped
copies (the harness's own `lua_dump` with strip); and the C budget: long
`gsub`, `find` (quadratic and plain), `match` with backtracking and
`table.move` loops, with `gsub` calling `xlua_core_interrupt` between two
charges. Bits 3 and 4 pick a core state's limits: the instruction limit
alone, a one-hour time limit with `XLUA_LIMITS_INTERRUPT`, a time limit
that has run out at once (the hook checks the instruction limit first, so
only the watchdog's poll and the C budget see it, and runs stay
deterministic), or a one-hour limit with WebGL's clock check. In the core
preset the extended inputs also call `load` (with a reader function),
`require` (a resolver that returns the name as the module's text),
`h.limits` (the same API and `xlua_gc` from inside a host function) and
`h.call` (`xlua_core_call` from inside a host function: by dotted name, as
a coroutine that is then resumed and closed, through a function handle, a
chunk and its compile; the status must be a documented one). Bit 1 now
also runs `xlua_check_registry_refs` and the `_checked` push functions, as
LuaEnv does with the bridge checks. Two harness bugs were fixed on the way:
the host function's `NEXT` and `INEXT` handle operations never started an
iteration (so `next_body` never ran), and on Lua 5.3 and LuaJIT the
`uint64` and int64 natives were taken in `lua_next` order, which the random
string hash seed changes from run to run (three runs of one input gave
three call traces), so reproducers of those runtimes did not replay; they
are sorted by name there now (`profiler-call-loop` therefore no longer
replays the calls of the run that found it). Nine seeds, `ext-*` in
`Test/Fuzz/corpus/bridge`, start the fuzzer on the new operations.
After the change, 10 minutes per configuration (one process, Docker
`xlua-analysis:26.04` with `--memory 6g`, Linux arm64, Clang 21.1.8):
30,756 runs on legacy 5.5.1, 37,528 on legacy LuaJIT, 19,372 on minimal
5.4.9 and 32,507 on core 5.5.1, no finding; peak RSS 0.6 to 1.4 GB. The
coverage is in "What the corpora reach" below.

`Test/Native/check_webgl_exports.py` does the import check for the Unity WebGL
plugin. It reads `llvm-nm --defined-only` output for the object file that
Emscripten builds from `Assets/Plugins/WebGL/xlua_webgl.cpp`; see the WebGL
section of `build/README.md`. `Test/Native/webgl_compile_check.py --output DIR
[--package] [--emscripten DIR]` compiles that file (and, with `--package`, the
UPM package's copy) with an installed editor's own Emscripten and Unity
6000.6's flags, then runs the import check; no licence or editor launch is
needed. It caught `<emscripten.h>` inside the wrapper's `extern "C"` block on
2026-10-18 (both copies failed with "templates must have C++ linkage"); with
6000.6.4f1's Emscripten both pass with 131 imports and no warning.
`--emscripten` also takes the `install/` folder of an official Emscripten
release archive; with 4.0.20 (macOS arm64, the archive `native-build.yml`'s
`webgl` job uses for Linux) both copies pass the same way (2026-10-18).

### Fuzzing in CI and corpus coverage

**Owner decisions of 2026-10-28**, from a review of Trail of Bits' Testing
Handbook ([appsec.guide](https://appsec.guide/)):

- **Q64: every native-build replays every target's seeds.** The sanitizer
  job of `native-build.yml` runs `run.sh replay` and `run_bridge.sh replay`
  on Lua 5.5.1 (the six targets, the bridge on the legacy preset) and on
  LuaJIT (i64 and the bridge), before the bridge's 60-second run. Each
  committed seed and each fixed crash's reproducer in `Test/Fuzz/corpus`
  runs once (`-runs=0`) on every push and pull request.
- **Q65: the weekly jobs run four libFuzzer processes** (`FUZZ_JOBS=4`,
  `-jobs=4 -workers=4` at first, fork mode since the change below) with `-rss_limit_mb=2048` each: 8 GB at most on the
  16 GB, 4-core public runner, so the limit stays at 2048 (the largest peaks
  measured so far were 1.6 to 1.8 GB). No `-ignore_crashes`: a job stops at
  its first crash and the step fails; the other jobs run to the end of
  their time. Besides the bridge's 30 minutes, `analysis.yml`'s `fuzz` job
  now runs the other five targets every Monday, 5 minutes each on Lua 5.5.1
  (and the i64 target on LuaJIT), with cached corpora like `fuzz-bridge`.
- **Fork mode (2026-10-28, the coordinator's choice under the owner's
  instruction "do what is best").** `FUZZ_JOBS=N` runs `-fork=N
  -ignore_crashes=0 -ignore_timeouts=0 -ignore_ooms=0` instead of `-jobs=N
  -workers=N`: the parent runs the starting corpus once and hands parts of
  it to N child processes in turn, and the first crash, timeout or
  out-of-memory of any of them stops the run, which fits "stop at the first
  crash" and saves the 5 minutes per 30 in which every job ran the cached
  corpus (below). Fork mode ignores timeouts and out-of-memory unless told
  otherwise, hence the two flags; and its parent leaks 32 bytes of its own,
  which LeakSanitizer reported at exit as a crash after a clean run, so the
  parent runs with `leak_check_at_exit=0` (the children still check each
  input for leaks). Its parent prints no `stat::` lines; the scripts print
  its last progress line. Locally (legacy 5.5.1, 60 seconds, four
  processes): 6,735 runs, 1,402 inputs merged into 1,201, which reach 356
  of `xlua.c`'s 434 branches; `run.sh`'s path target for 30 seconds the
  same way. The corpus merge and `corpus_coverage.sh` steps are unchanged.
  In CI ([40728596136](https://github.com/emindeniz99/xlua-ng/actions/runs/40728596136),
  `fuzz-seconds` 60, the four bridge legs from their cached corpora, all
  passed): the legacy 5.5.1 leg's run took 220 seconds, about 160 of them
  for the start over 1,975 cached inputs and the seeds in one process
  while the other three waited, then merged 2,926 inputs into 1,915, which
  reach 365 of `xlua.c`'s 434 branches (84.1%, against 294 for the old
  driver's corpus); minimal 5.4.9 took 117 seconds and reached 358 of 432
  (82.9%). So the start still costs about 3 minutes of each 30, once
  instead of four times; the saving is CPU more than wall time.
- **Q66: measure what the corpora reach.** `Test/Fuzz/corpus_coverage.sh
  <output dir> [TARGET=DIR ...]` builds the targets with Clang source-based
  coverage instead of the sanitizers (`FUZZ_INSTRUMENT=coverage`:
  `-fprofile-instr-generate -fcoverage-mapping` on xLua's own files, Lua's
  API checks, Debug for the bridge's CMake build, as `coverage.sh`),
  replays a corpus through each target with `-runs=0` (the seeds by
  default, or `TARGET=DIR`), and prints llvm-cov's function and branch
  report for `xlua.c`, `xlua_core*.c` and `i64lib.c`; `unreached.txt` lists
  the functions no input called, by branch count. An input that crashes or
  hangs is skipped (one process per input after a failed whole-corpus run)
  and listed in `skipped.txt`. Every weekly leg runs it on its merged corpus
  and uploads the report with the corpus (no gate). `TARGETS`,
  `LUA_VERSION` and `XLUA_PRESET` select the targets and configuration.
- **Q68: the weekly corpus is merged before it is saved** (`FUZZ_MERGE=1`:
  `-merge=1` into a fresh directory that replaces the corpus).
- **Q69 (recorded only): no Semgrep for now.** clang-tidy, CodeQL,
  `LUA_USE_APICHECK` and the fuzzers cover the same bug classes.

**Cost (2026-10-28).** In `native-build.yml` run
[40707787116](https://github.com/emindeniz99/xlua-ng/actions/runs/40707787116)
the replay step took 46 s (both builds and both LuaJIT builds included; the
bridge's 60-second step after it, which reuses the build, 62 s). Locally
(Docker, Linux arm64) `run.sh replay` takes 19 s on 5.5.1 and 12 s on
LuaJIT, and `run_bridge.sh replay` 5 to 13 s per configuration. With four
jobs, every job first runs the whole corpus it starts from: in the trial
run of `analysis.yml`
([40707794073](https://github.com/emindeniz99/xlua-ng/actions/runs/40707794073),
`fuzz-seconds` 60), the legacy 5.5.1 bridge's step took 6.5 minutes,
build included: each of the four jobs ran the 2,917 inputs of the cached
corpus once and stopped there (about 8 inputs per second per job, past
its 60 seconds); the merge took 3 minutes (2,917 inputs
into 1,975) and `corpus_coverage.sh` 1.5 minutes. In a 30-minute week, the
four jobs repeat that start, about 5 minutes each. The core bridge merged
3,228 inputs into 1,416. Fork mode (above) runs the corpus once.

**What the corpora reach (2026-10-28).** Functions and branches called of
all, from `corpus_coverage.sh`. The seeds column is the committed corpus
(local, Docker `xlua-analysis:26.04`, Linux arm64, Clang 21.1.8). The CI column is
the merged corpus of the trial run above (Linux x86-64): for the bridge
that is the cache of the 30-minute run
[40675156144](https://github.com/emindeniz99/xlua-ng/actions/runs/40675156144)
plus 60 seconds of four jobs; the other targets had no cache yet, so theirs
is 60 seconds of four jobs. The same CI corpus replayed locally gives a few
branches more or fewer (the core bridge's sandbox: 282 locally, 279 in CI), from
timing and memory limits.

| Target and configuration | File | Seeds | CI corpus |
|---|---|---|---|
| bridge, legacy 5.5.1 | `xlua.c` | 80 of 126 functions, 164 of 434 branches (37.8%) | 101 of 126, 294 of 434 (67.7%), 1,975 inputs |
| | `xlua_core.c` | 18 of 23, 62 of 94 | 19 of 23, 65 of 94 |
| | `xlua_core_limits.c` | 16 of 28, 49 of 156 | 19 of 28, 60 of 156 |
| | `i64lib.c` | 12 of 15, 6 of 8 | 12 of 15, 8 of 8 |
| bridge, legacy LuaJIT | `xlua.c` | 77 of 122, 148 of 430 (34.4%) | 99 of 122, 294 of 430 (68.4%), 1,206 inputs |
| | `i64lib.c` | 34 of 41, 84 of 150 | 34 of 41, 113 of 150 |
| | `xlua_core.c` | 12 of 18, 23 of 42 | 13 of 18, 28 of 42 |
| bridge, minimal 5.4.9 | `xlua.c` | 81 of 124, 175 of 432 (40.5%) | 97 of 124, 281 of 432 (65.0%), 1,641 inputs |
| bridge, core 5.5.1 | `xlua_core_sandbox.c` | 30 of 42, 207 of 570 (36.3%) | 31 of 42, 279 of 570 (48.9%), 1,416 inputs |
| | `xlua_core.c` | 8 of 14, 78 of 136 | 8 of 14, 90 of 136 |
| | `xlua_core_buffer.c` | 20 of 27, 39 of 102 | 26 of 27, 63 of 102 |
| | `xlua_core_limits.c` | 18 of 28, 48 of 158 | 19 of 28, 58 of 158 |
| lua, i64, struct and path together, 5.5.1 | `xlua.c` | 76 of 125, 203 of 432 (47.0%) | 76 of 125, 240 of 432 (55.6%) |
| | `i64lib.c` | 12 of 15, 8 of 8 | 12 of 15, 8 of 8 |
| core, 5.5.1 | `xlua_core_sandbox.c` | 42 of 42, 354 of 586 (60.4%) | 42 of 42, 363 of 586 (61.9%) |
| | `xlua_core.c` | 10 of 14, 70 of 136 | 10 of 14, 71 of 136 |
| | `xlua_core_buffer.c` | 25 of 27, 62 of 102 | 26 of 27, 76 of 102 |
| | `xlua_core_limits.c` | 23 of 28, 86 of 162 | 23 of 28, 92 of 162 |
| i64, LuaJIT | `i64lib.c` | 23 of 41, 88 of 150 | 23 of 41, 89 of 150 |

The totals differ from `coverage.sh`'s (356 branches of `xlua.c` on 5.5.1)
because the fuzz builds compile other options: every export
(`XLUA_EXPORTS=all`) for the bridge, and `run.sh`'s own build without
CMake's defines for the other targets. Compare within this table only. Of
the run.sh targets, `struct` reaches the most of `xlua.c` (193 branches),
then `lua` (92); `xlua_core.c` is not used by them (0).

**Largest functions no input reached** (`unreached.txt` of the CI
corpora above, before the extended operations of 2026-10-28): the C budget
and stop API of `xlua_core_limits.c` (`xlua_core_budget`, `xlua_core_watch`,
`xlua_core_interrupt`, and in `xlua_core.c` `xlua_core_get_stats`,
`xlua_core_set_memory_limit` and `raise_overdraft`); `xlua_pack_float3` to
`float5` and their unpack functions (the driver's `kind % 9` never chose
them), `xlua_tostruct` and the `_checked` push functions; in the core
preset's sandbox `co_body`, `core_require`, `push_global`, `next_body`,
`xlua_gc` and `core_reader`; and `env_upvalue`, `set_registry_metatable`,
`xlua_ref_indirect` and `xlua_check_registry_refs`.

**After the extended operations (2026-10-28).** Functions and branches of
`corpus_coverage.sh`, local (Docker `xlua-analysis:26.04`, Linux arm64,
Clang 21.1.8). Seeds before: the committed corpus and the 16 random seeds
with the old driver (20 inputs); seeds after: the same with the new driver
and the nine `ext-*` seeds (29 inputs); 10 minutes: the corpus of one
10-minute process with the new driver, unmerged. The CI corpus column above
(old driver, a 30-minute run with four jobs plus 60 seconds) is the
comparison for the last column.

| Configuration | File | Seeds before | Seeds after | 10 minutes, new driver |
|---|---|---|---|---|
| legacy 5.5.1 | `xlua.c` | 80 of 126, 164 of 434 (37.8%) | 105 of 126, 273 of 434 (62.9%) | 117 of 126, 393 of 434 (90.6%), 2,931 inputs |
| | `xlua_core.c` | 18 of 23, 62 of 94 | 21 of 23, 75 of 94 | 22 of 23, 81 of 94 |
| | `xlua_core_limits.c` | 16 of 28, 49 of 156 | 24 of 28, 98 of 156 | 27 of 28, 112 of 156 |
| | `i64lib.c` | 12 of 15, 6 of 8 | 13 of 15, 6 of 8 | 13 of 15, 8 of 8 |
| legacy LuaJIT | `xlua.c` | 77 of 122, 147 of 430 (34.2%) | 103 of 122, 257 of 430 (59.8%) | 112 of 122, 370 of 430 (86.0%), 1,481 inputs |
| | `i64lib.c` | 34 of 41, 82 of 150 | 37 of 41, 92 of 150 | 37 of 41, 119 of 150 |
| | `xlua_core.c` | 12 of 18, 23 of 42 | 14 of 18, 25 of 42 | 15 of 18, 30 of 42 |
| minimal 5.4.9 | `xlua.c` | 81 of 124, 175 of 432 (40.5%) | 106 of 124, 280 of 432 (64.8%) | 115 of 124, 382 of 432 (88.4%), 2,250 inputs |
| | `xlua_core.c` | 19 of 24, 52 of 82 | 22 of 24, 65 of 82 | 23 of 24, 71 of 82 |
| | `xlua_core_limits.c` | 16 of 28, 49 of 156 | 24 of 28, 98 of 156 | 27 of 28, 112 of 156 |
| core 5.5.1 | `xlua_core_sandbox.c` | 28 of 42, 182 of 570 (31.9%) | 37 of 42, 288 of 570 (50.5%) | 41 of 42, 418 of 570 (73.3%), 2,210 inputs |
| | `xlua_core.c` | 8 of 14, 72 of 136 | 11 of 14, 88 of 136 | 12 of 14, 112 of 136 |
| | `xlua_core_buffer.c` | 19 of 27, 36 of 102 | 16 of 27, 29 of 102 | 24 of 27, 53 of 102 |
| | `xlua_core_limits.c` | 18 of 28, 48 of 158 | 26 of 28, 93 of 158 | 27 of 28, 110 of 158 |

The seeds of the core preset reach slightly less of the buffer library
than before: some of the random seeds are extended inputs now and read
their bytes differently. After 10 minutes, what no input reached is the
`print_*` debug helpers and exports without a C# caller and without
branches (`xlua_gl`, `load_error_func`, `xlua_pushlstring` ...) on the
bridge presets; on LuaJIT also a few i64lib conversions
(`push_int64_kind`, `number_to_uint64`) and `xlua_core_newstate` (no core
state there); in the core preset three buffer helpers (`to_float`,
`check_count`, `store_le`) and `raise_overdraft`, which only an
allocation inside a host function under a lowered limit reaches (the
bridge presets reach it). The weekly job's merged corpora will show what
30 minutes with four processes add.

### Linux container

`Test/Docker/Dockerfile` builds the Linux image these scripts were run in:
Ubuntu 26.04, pinned by digest, with Clang and libFuzzer, clang-tidy, GCC,
CMake and Mono 6.14.1 (as built on 2026-10-17: Clang 21.1.8, GCC 15.2, CMake
4.2.3). macOS has no libFuzzer runtime in Apple clang and no clang-tidy, so use
it there. Mount the checkout and an output directory outside it:

```sh
docker build -t xlua-analysis:26.04 Test/Docker
docker run --rm -v "$PWD":/src -v /path/to/empty-dir:/out -w /src xlua-analysis:26.04 \
    Test/Native/analyze.sh /out
```

`coverage.sh`, `sanitize.sh` and `Test/Fuzz/run.sh` run the same way. The
image is about 1.8 GB; OrbStack and Docker Desktop keep it in their data
directory, which can be moved off the startup disk.

## Unity .meta files

`python3 Tools/check_meta.py` (about a second; the `meta.yml` workflow
runs it) checks the `.meta` files of `Assets/`, `Test/UnitTest` and
`Test/PrefTest`, and of the UPM package that `Tools/make_upm_package.py`
assembles from a placeholder for every plugin:

- every file and folder has a `.meta`, except the names Unity does not import
  (starting with `.` or ending with `~`, `cvs`, `*.tmp`);
- no `.meta` is orphaned, except those of the native plugins, which are built
  and not committed;
- `fileFormatVersion: 2` and one well-formed GUID per `.meta`, no GUID twice;
- every plugin under `Assets/Plugins` (and in the package, the variants
  included) has the platforms, CPU and Editor OS that the script's `PLUGINS`
  table lists, nothing else enabled, and every plugin of
  `make_upm_package.py`'s `RELEASE_PLUGINS` has a `.meta`. A new plugin needs
  a row there. An enabled Editor must name a CPU of the Editor's enum
  (`AnyCPU`, `x86_64`, `ARM64`), and the 32-bit `x86` desktop plugins must
  have the Editor off.

The file list comes from git (tracked files, and untracked ones that are not
ignored). Run on 2026-10-28, it found these, now fixed:

- `Assets/Plugins/arm64.meta` (the folder of the arm64 dylib and DLL) and the
  `.meta` files of `Test/UnitTest/xLuaTest/LuaCallCS/LuaOptionsForLuaCallCS.cs`
  and `NoReflectionForLuaCallCS.cs` were missing; they now have new GUIDs;
- upstream's desktop plugins were enabled for players of another OS:
  `x86_64/libxlua.so` for Win64, `x86/libxlua.so` for Win (32-bit),
  `x86_64/xlua.dll` for Linux64 (and the old OSXIntel64), `x86/xlua.dll` for
  the old Linux and OSXIntel. Those players got a library they cannot load;
- the Editor OS of `x86_64/libxlua.so` and `x86/libxlua.so` was AnyOS (now
  Linux) and of `arm64/libxlua.dylib` AnyOS (now OSX).

**Owner decision (2026-10-28): CPU names in Unity's spelling.** The check
compares CPU and OS names exactly, and the files use the spelling Unity
writes. That spelling depends on the platform, read from the importers'
enums in Unity 2021.3.45f2 and 6000.6.6f1 (`UnityEditor.dll` and
`UnityEditor.CoreModule.dll`, `UnityEditor.Android.Extensions.dll` of
2021.3.45f2 and 6000.6.4f1, disassembled with `ikdasm`) and confirmed by the
`.meta` files those editors ship and re-saved in our test projects:

| Importer | CPU names | Source |
|---|---|---|
| Android | `ARMv7`, `ARM64`, `X86`, `X86_64` | `AndroidPluginCPUArchitecture` (2021.3), `AndroidArchitecture` (6000.6) |
| Standalone (Win, Win64, Linux64, OSXUniversal) | `None`, `AnyCPU`, `x86`, `x86_64`, `ARM64` | `DesktopPluginCPUArchitecture`, the same in both |
| Editor | `AnyCPU`, `x86_64`, `ARM64` | `EditorPluginCPUArchitecture`, the same in both |

So only the Android `x86` library changed (`x86` to `X86`); the desktop
names were already Unity's. Two things were left as they are: the WSA names
(`ARM`, `ARM64`, `x64`, `x86`, upstream's), since no Universal Windows
Platform module was installed to read its importer; and the Editor CPU `x86`
of the 32-bit `x86/xlua.dll` and `x86/libxlua.so`, which is not a value of
the Editor's enum in either version (no 32-bit Editor exists).
**Owner decision (2026-10-28, Q40): the Editor is off for those two.** They
stay enabled for 32-bit Windows and Linux players; the check fails for an
enabled Editor there, and for any enabled Editor with a CPU outside the
Editor's enum (both findings on the old files, checked). Checked by
breaking a copy of the repository: removing the
Android x86_64 `.meta` files (the gap closed on 2026-10-19), a copied GUID,
an orphan `.meta` with a malformed GUID and a wrong Android CPU each fail it,
and a `.hidden` file and a `New~` folder do not. `make_upm_package.py` now
leaves such hidden names out of the package, as Unity does, instead of
stopping on their missing `.meta`. Not checked in a Unity editor.

## Unity players and upstream examples

- [Mobile smoke checks](MobileSmoke/README.md): runtime initialization, GC,
  coroutines, close semantics and disposal in Unity/IL2CPP, plus the integer
  rule, Lua functions cast to C# delegates (the source generator's static
  bridges with `prepare.py --source-gen`; `--gen`, Generate Code, until
  milestone M11) and the C# feature defines. Run on 2026-10-18 on main
  at 227ed4c: Android ARM64 APKs from Unity 6000.3.21f1 and 2021.3.45f2 on an
  Android 16 emulator with 16 KB pages, and a Unity 6000.6.4f1 WebGL player in
  headless Chrome, each with and without generated code and with all four
  defines; every player passed. The same day the minimal preset
  (`--android-minimal-plugin`, the plugin selector choosing the library)
  passed in release and Development APKs, one Development APK with the
  source generator, on that emulator and on a moto g play 2024 (Android 14).
  Also on 2026-10-18, the first iOS players in the iOS 27.1 Simulator
  (Unity 6000.3.21f1, arm64): legacy, minimal (Development and release), the
  source generator and core-only, with simulator builds of the plugin
  (`XLUA_IOS_SDK=simulator`); `Test/MobileSmoke/ios_sim_smoke.py` builds,
  installs and runs an export
  ([MobileSmoke/README.md](MobileSmoke/README.md#2026-10-18-ios-players-in-the-simulator)).
  After milestone M11, release ARM64 APKs with the source generator and its
  GCM mode passed on that emulator (Unity 6000.3.21f1), and the generator
  player's APK, `libil2cpp.so` and metadata were within 0.01% of the same
  player from before M11
  ([MobileSmoke/README.md](MobileSmoke/README.md#2026-10-18-without-the-legacy-generator-milestone-m11)). On 2026-10-28 minimal release and Development
  players (the release one with the reduced `debug` library), release
  players with the generator and its GCM mode, and one with the libraries of
  every ABI passed there from main at `d37515c2`
  ([MobileSmoke/README.md](MobileSmoke/README.md#2026-10-28-the-minimal-presets-reduced-debug-library-and-main-after-m11)).
- [Upstream Android examples](Examples/README.md): original C#↔Lua tutorial
  scenes, Unity update callbacks and UI events with the source generator's
  IL2CPP bridges.
- [Artifact CI](../.github/CI.md): manual native builds and test artifacts with
  no release creation. The real-editor matrix covers Unity 2021.3 LTS, 2022.3
  LTS, 6.0 LTS, 6.3 LTS, and the current 6.6 line when license secrets are
  configured. Unity device examples are run locally, outside CI.

## Unity editor tests

`Test/UnityMatrix/prepare.py --editor-tools` and `editor_tests.py run` (see
[.github/CI.md](../.github/CI.md)) run the EditMode tests in one editor launch,
in the mode `prepare.py` writes into `XluaEditorTestMode.txt`:

- **`sourcegen`** (`prepare.py --source-gen BUILD_DIR`): the xLua source
  generator, built with `Tools/build_source_generator.py`, is installed in
  `Assets/XLua/Src/Analyzers` (with `--no-asmdef` in `Assets/XLua/Analyzers`,
  with `--package` in the package's `Runtime/Analyzers`) and binds the suite's
  types from the configuration attributes in `XluaEditorTestTarget.cs`;
  `XluaSourceGenTests` must pass too, lua-language-server checks `good.lua`
  and `bad.lua` against the LuaCATS files, and the editor log must have no
  analyzer load warning, no XLSG error, no XLSG011 (a configuration list: the
  suite has none) and the fixture's XLSG003;
- **`reflection`** (no `--source-gen`): no generated bindings; the Editor
  emits type wrappers and `[CSharpCallLua]` bridges at run time (CodeEmit),
  LuaCATS declares only xLua's own API (the LuaCATS tests check that, LuaLS is
  skipped), and the log must have no XLSG diagnostic.

Until milestone M11 removed the legacy generator, `run` had four launches:
Generate Code, the tests in mode `generated`, Clear Generated Code, and the
tests in mode `cleared` (what `reflection` checks now); the suite's
configuration was lists (`XluaEditorTest.Config`) and the source generator
runs copied them as attributes (`SourceGen/XluaSourceGenSuiteConfig.cs`, removed).
`XluaGeneratedCodeTests.GeneratedWrappersFollowGenCodeMinimize` checks the
generator's GCM wrappers with `--define GEN_CODE_MINIMIZE`.
`XluaGeneratedCodeTests.LuaIntegerArgumentsFollowLuasRule` checks the integer
rule of [MIGRATING.md](../docs/MIGRATING.md) through the generated or emitted
wrappers: a constructor, a static method with an `out` parameter, an instance
field and a field of a `[GCOptimize]` struct. The LuaLS step fails when
lua-language-server writes no report; before 2026-10-17 a binary that did not
start (such as a mise shim with no version set) passed `good.lua`.

`XluaSmokeTests.FeatureDefinesInTheEditor` checks the C# feature defines as the
Editor sees them; `prepare.py --define NAME` adds each to `Assets/csc.rsp`.
With `--define XLUA_PRESET_MINIMAL` and no `--define XLUA_REFLECTION` the
Editor has no reflection fallback and no CodeEmit
(`XLUA_NO_REFLECTION`): `XluaSmokeTests.UnboundTypesFollowTheReflectionDefine`
checks the error for a type without bindings, and
`XluaGeneratedCodeTests.CodeEmitIsCompiledIntoTheEditor` that CodeEmit is not
compiled. Mode `reflection` then binds nothing, so it needs
`--define XLUA_REFLECTION`; in mode `sourcegen` the tests in
`editor_tests.py`'s `NEEDS_REFLECTION` (the smoke API and UnityEngine types the
suite does not configure, a non-public fixture, the play mode host's
`MonoBehaviour.StartCoroutine`) must fail with `has no generated bindings`,
as `needs_reflection.lua` does for the managed suite. The smoke player
(`Test/MobileSmoke/XluaSmoke.cs`) with `XLUA_SMOKE_DEFINES=XLUA_PRESET_MINIMAL`
and without the source generator needs `XLUA_REFLECTION` in the same list.
Without `Assets/XLua/Resources` (prepare.py copies only `Assets/XLua/Src`),
`require "xlua.util"` and `require "perf.profiler"` load the embedded modules.
With `XLUA_NO_UTIL` the first fails, `XLUA_NO_SIGNATURE_LOADER` removes
`SignatureLoader`, and the Editor keeps the `template` library and
`perf.profiler` under `XLUA_NO_TEMPLATE` and `XLUA_NO_PROFILER`.

`Test/UnityMatrix/hotfix_intkey.py` checks IntKey hotfix with two assemblies,
which the suite does not cover. It builds a project with `prepare.py
--editor-tools --hotfix --source-gen` (the hotfix bridges come only from the
generator since M11), the injector from `Tools/build_tools.py` and the
fixture in `Test/UnityMatrix/HotfixIntKey`: `HotfixIntKeyLocal` in
Assembly-CSharp with `[assembly: Hotfix(typeof(HotfixIntKeyLocal),
HotfixFlag.IntKey)]`, and `Game.Shop` in an asmdef assembly `Game`. Then it
runs XLua/Validate Setup (it must report no problem) and `HotfixIntKeyTests`.
That test runs Hotfix Inject In Editor, which must write
`hotfix_id_map_Assembly-CSharp`, `hotfix_id_map_Game` and the merged
`hotfix_id_map` with no IntKey fallback warning. Then it enters play mode,
where the domain reload loads the injected assemblies, and checks that
`auto_id_map()` and `xlua.hotfix` patch and restore both types, and
`HotfixIntKeyLocal`'s methods with a `ref`, an `in` and a virtual `in`
parameter: a patch's extra result reaches the `ref` argument only.
`--csharp12` compiles with `-langversion:12` and adds two methods with
`ref readonly` parameters (Unity 6000.6's Roslyn only). Details per
layout are under "asmdef layout, source generator and hotfix players" below.
Run `python3 Tools/build_tools.py` first. The run records below that predate
M11 used an Editor list for both types and Generate Code's bridges.

Run on 2026-10-18 (before M11), macOS ARM64, Lua 5.5.1 plugin and tools built
from main at `4e76aee` plus the delegate test fix below, with
`--fast-enter-play-mode` (and `--csharp12` on 6000.6). "Feature defines" is
`--define` with `XLUA_NO_SIGNATURE_LOADER`, `XLUA_NO_TEMPLATE`,
`XLUA_NO_UTIL` and `XLUA_NO_PROFILER`. Every launch passed, and LuaLS reported
0 diagnostics for `good.lua` and the 10 expected for `bad.lua` in each run:

| Unity | Variant | Generated | Cleared |
|---|---|---|---|
| 2021.3.45f2 | default | 32/32 | 32/32 |
| 2021.3.45f2 | `--hotfix` | 32/32 | 32/32 |
| 2021.3.45f2 | feature defines | 32/32 | 32/32 |
| 6000.3.26f1 | default | 32/32 | 32/32 |
| 6000.3.26f1 | `--hotfix` | 32/32 | 32/32 |
| 6000.6.6f1 | default | 36/36 | 36/36 |

`hotfix_intkey.py` passed on 2021.3.45f2 and 6000.3.26f1, and Validate Setup
reported no problem with the rebuilt tools.

Before the fix, `LuaFunctionsBecomeCSharpCallLuaDelegates` failed with
generated code in every run, with "try to dispose a LuaEnv with C# callback!".
The cause was in the test, not the runtime. Since the source generator M0,
generated delegate bridges are static methods of
`XLua.CSObjectWrap.DelegatesGensBridge`, closed over the `DelegateBridge`,
and no longer instance methods of `DelegateBridge`, so the test's assertion
on the declaring type failed. The `using` block then disposed the `LuaEnv`
while the delegate was still alive, and Dispose's exception replaced the
assertion's message. The delegate now lives in a separate method: Unity's Mono
scans stacks conservatively, so a delegate left in a slot of the test method
kept its bridge alive even after it was set to null. Dispose runs before the
assertions.

With the `[Hotfix]` list in Assembly-CSharp instead of an Editor folder,
`Game.Shop` is not injected: the injector reads the configuration from the
assembly it injects and from the Editor assembly
(`General/Src/XLuaHotfixInject.cs`, `Hotfix.cs`, `hotfix_cfg_in_editor.data`).
Game.dll then gets an empty id map and no warning (2021.3.45f2). Hotfix_EN.md
asks for an Editor folder in this case.

**The minimal preset in the Editor (2026-10-18).** `prepare.py
--editor-tools --fast-enter-play-mode --define XLUA_PRESET_MINIMAL
--minimal-plugin <minimal libxlua.dylib>` on main at `82c570f9` (with the
shared `XLuaCodeEmit` assembly): the minimal C# with every hardening define,
and the minimal macOS library as the variant
`Plugins/arm64/xlua-variants/minimal/libxlua.dylib` (Editor off) next to the
default one. `XluaPluginVariantTests` checks that the selector groups the two,
picks the variant for `XLUA_PRESET_MINIMAL` (release and Development) and the
default plugin otherwise, and that the Editor loaded the default library (`io`
and LuaSocket present). Then `SmokeBuild.BuildMac` with
`XLUA_SMOKE_DEFINES=XLUA_PRESET_MINIMAL` built an ARM64 Mono player: the
build log has `XLua plugin variants: Assets/Plugins/arm64/libxlua.dylib ->
Assets/Plugins/arm64/xlua-variants/minimal/libxlua.dylib`,
`Contents/PlugIns` holds only the minimal dylib (the same bytes), and the
player printed `XLUA_NG_SMOKE_PASS` with `io off, LuaSocket off`. LuaLS gave
0 diagnostics for `good.lua` and the 10 expected for `bad.lua`:

| Unity | Generated | Cleared | macOS player |
|---|---|---|---|
| 2021.3.45f2 | 34/34 | 34/34 | passed, minimal library |
| 6000.6.6f1 | 34/34 | 34/34 | passed, minimal library |

The 34 include the five `XluaPlayModeTests` (with the new check that no play
session adds an `XLuaCodeEmit` assembly, run here in Unity for the first time)
and the two `XluaPluginVariantTests`.

2022.3 and 6000.0 were not run this time (they ran later the same day, see
"2022.3, 6000.0, IL2CPP targets and hotfix with the generator" below). Players (where `XLUA_NO_TEMPLATE`
and `XLUA_NO_PROFILER` take effect, and build-time injection), the Linux
editors and the Windows editors are not covered locally.

### asmdef layout, source generator and hotfix players

- **The asmdef layout is the default** of `prepare.py` (and of
  `Test/MobileSmoke/prepare.py`) since 2026-10-18, as of the repository: xLua
  goes to `Assets/XLua/Core` and `Assets/XLua/Src` with their four asmdefs
  (`--editor-tools` adds the two Editor ones) and the folders' `.meta` files,
  so `Assets/XLua/Gen` compiles into Assembly-CSharp. `--asmdef` is still
  accepted and changes nothing. **`--no-asmdef`** gives the layout of XLua >
  Project Layout > Use Assembly-CSharp: xLua in Assembly-CSharp from the
  historical `Assets/XLua/*.cs`, without asmdefs. Before 2026-10-18 the
  asmdef layout put the asmdef in `Assets/XLua`, which also took `Gen`, and
  the generated code failed with 42 CS0246 errors (it cannot see the
  project types in XLua.dll).
- **`layout_switch.py --unity EXE --unity-version V --plugin DYLIB --project DIR --out DIR`**
  exercises XLua > Project Layout in a real editor, one launch at a time:
  `XLua.ProjectLayout.Report` (Asmdefs, xLua's C API in XLua.Core),
  `UseAssemblyCSharp` (the four asmdefs and `.meta` files must arrive
  unchanged in `Assets/XLua/Asmdef~`), `Report` (AssemblyCSharp,
  Assembly-CSharp), `UseAsmdefs` (back unchanged, `Asmdef~` removed),
  `Report`, and finally `XLUA_CORE_ONLY` in the Player Settings, after which
  the project's own scripts must fail with CS0246 for xLua's types: a failing
  define constraint drops the code instead of moving it to Assembly-CSharp.
  Since M11 the project has the source generator (without the SgGame asmdef,
  which cannot compile once XLua.asmdef is parked), `UseAssemblyCSharp` must
  log that the generator now runs in every assembly, and
  `XluaEditorTestSteps.ReportBindings` checks the generator's units after
  each switch: Assembly-CSharp and XLua in the asmdef layout, one unit in
  Assembly-CSharp with xLua's defaults and the suite's types without asmdefs.
  Until then the switch refused while the generator was installed, and
  Generate Code made the bindings of the layout without asmdefs. Prints
  `XLUA_LAYOUT_SWITCH_PASS`.
- **`prepare.py --source-gen BUILD_DIR`** (with `--editor-tools`)
  builds the source generator, installs it in `Assets/XLua/Src/Analyzers`
  (with `--no-asmdef` in `Assets/XLua/Analyzers`) and adds
  `Test/UnityMatrix/SourceGen`: `SgLocal` and assembly attributes in
  Assembly-CSharp, `SgGame.Shop` and a delegate in an asmdef `SgGame` (with
  `--no-asmdef` in Assembly-CSharp, without the asmdef, since an asmdef
  cannot reference Assembly-CSharp), an interface, a deliberate XLSG003,
  and, with `--roslyn-sdk`, the `XluaRefReadonlyLib` types. `editor_tests.py
  run` runs the suite in mode `sourcegen`: wrappers and bridges come from
  `XLua.Generated.*`, `Gen_Flag` is on, and `XluaSourceGenTests` checks a
  `GeneratedBindings` unit in both assemblies (XLua's has no types, only its
  default GCOptimize pushers; without asmdefs one unit in Assembly-CSharp has
  everything), registration by `[InitializeOnLoadMethod]` in edit mode
  without the first-`LuaEnv` scan, by `[RuntimeInitializeOnLoadMethod]` at
  each play mode start with domain reload off (after the registrations are
  removed) and after a domain reload, calls that arrive from the `W_`
  wrappers and the `SgGame` and Assembly-CSharp bridges. The logs must not
  contain CS8032, CS8784, CS8785, CS9057 or an XLSG error, and must contain
  the fixture's XLSG003 with file and line.
- **`hotfix_intkey.py`** runs the IntKey fixture in the asmdef layout by
  default, `--no-asmdef` without asmdefs, `--package` from the UPM package,
  always with the source generator (`--source-gen BUILD_DIR`, default
  `<out>/source-generator`). In the asmdef and package layouts `Game.dll`
  references XLua and XLua.Core, both types are configured with
  `[assembly: Hotfix(typeof(T), HotfixFlag.IntKey)]` in their own assemblies
  (`HotfixIntKey/LocalHotfix.cs`, `HotfixIntKey/Game/GameHotfix.cs`), and each
  assembly must use its generated `HotfixBridges` slots (milestone M7): no
  fallback warning, no `__Hotfix` fields, and id maps with `["<assembly>"]`.
  Without asmdefs xLua is in Assembly-CSharp, which `Game.asmdef` cannot
  reference, so `Game` cannot carry `[Hotfix]` or get bridges: an Editor list
  marks `Game.Shop` `HotfixFlag.Inline` (`HotfixIntKey/NoAsmdef`, define
  `XLUA_TEST_HOTFIX_GAME_INLINE`), which injects it without bridges in field
  mode, while `HotfixIntKeyLocal` uses Assembly-CSharp's slots. Until M11 an
  Editor list configured both types for Generate Code's bridges and
  `Game.Shop` fell back to field mode with a warning; since M11 the injector
  refuses a type only a list names unless it is Inline (`hotfix_check.py`
  checks the refusal on the host). In the asmdef and package layouts `Game`
  also has `HotfixIntKey/Game/GameShapes.cs`: `[Hotfix]` on a type
  (`TaggedShop`), an `Inline` type (`InlineShop`) and a generic method
  (`GenericShop.Echo<T>`), which `HotfixShapes.cs` patches and restores after
  injection (`XLUA_INTKEY_SHAPES: before 3,4,a patched 30,40,echo a restored
  3,4,a`). Upstream's injector did nothing for any of them outside the
  assembly that holds xLua (fixed in milestone M7); with that behaviour put
  back, `hotfix_player.py` fails at `TaggedShop` and at `InlineShop`
  (2026-10-28).
- **`hotfix_player.py [--no-asmdef|--package] [--no-assembly-csharp] [--source-gen BUILD_DIR] [--arch ARM64|x64|x64ARM64] [--bundle DIR]`**
  builds a macOS Mono player (ARM64 by default) with
  `HOTFIX_ENABLE`, where xLua's post-build step injects the player's script
  assemblies, runs it in batch mode and checks that `HotfixPlayerProbe`
  patches and restores both IntKey fixture types from Lua. The plugins keep
  the repository's import settings. An x64 player runs under Rosetta, a
  universal one natively and under Rosetta; `--bundle` adds a universal
  `xlua.bundle` (from `make_osx_lua55.sh`) next to the arm64 dylib.
  The fixture is configured as `hotfix_intkey.py` configures it; the script
  runs Hotfix Inject In Editor for the id map, then the build, and expects no
  IntKey fallback warning and IntKey through each assembly's own slots in the
  player (`fields 0,0 idmap True`; without asmdefs `fields 0,1`, Game.Shop
  Inline), and, in the asmdef and package layouts, the `GameShapes.cs` types
  patched (`shapes before 3,4,a ...`). `--no-assembly-csharp` (asmdef or
  package layout) moves every runtime script outside an asmdef, the
  fixture's and `prepare.py`'s, into an asmdef `Main`, so the player has
  `Main.dll` and no `Assembly-CSharp.dll` (the script checks both). The
  post-build step threw there until 2026-10-18 (`files.Single(...
  "Assembly-CSharp.dll")`); with that line put back the build logs
  `InvalidOperationException: Sequence contains no matching element` and the
  player is not injected. Likewise, passing `Assembly-CSharp.dll` as xLua's
  assembly (the Editor's argument from Unity 2019.1 until 2026-10-18) stops
  Hotfix Inject In Editor in the asmdef layout with that exception for
  Assembly-CSharp.dll and Game.dll.
  `--platform linux` (since 2026-10-28) gives the Editor `x86_64/libxlua.so`
  and builds a Linux x86_64 Mono player (`HotfixPlayerSteps.BuildLinux`);
  `--prepare-only` and `--verify-only` split the run as in
  `hotfix_il2cpp.py` below, the checks reading the `build.log` that the build
  method writes with `-xluaBuildLog`. Owner decision of 2026-10-28:
  `unity-matrix.yml`'s `hotfix-player` job runs it that way on 6000.3.26f1
  with `--no-assembly-csharp` and with `--package` (Hotfix Inject In Editor
  and the build in unity-builder steps, the player in the editor image). Only
  `--prepare-only` has run so far (macOS, no editor): the job is skipped
  without the Unity secrets.
- **`hotfix_il2cpp.py --android-plugin SO [--development] [--check-tool EXE]`**
  builds an Android ARM64 IL2CPP player with `HOTFIX_ENABLE` and the shapes
  of `Test/Native/HotfixRegions.cs` in Assembly-CSharp (release by default,
  so Roslyn compiles them with optimizations). The build must succeed with
  no "use of undeclared label" in IL2CPP's C++; `--check-tool` (the
  `ShortBranches.exe` that `hotfix_check.py` builds) also checks the injected
  Assembly-CSharp.dll. Install and start the APK yourself
  (`org.xluang.hotfixregions`); `HotfixRegionsProbe` logs
  `XLUA_HOTFIX_REGIONS_PASS` with the number of checks. `--prepare-only`
  stops after preparing the project and `--verify-only` checks a build
  launched elsewhere, from the `build.log` that `HotfixRegionsSteps` writes
  with `-xluaBuildLog`; `--platform linux` gives the Editor
  `x86_64/libxlua.so`. `unity-matrix.yml`'s `hotfix-il2cpp` job runs it that
  way, with GameCI's unity-builder for the two editor launches. The same
  sequence passed locally on 2026-10-18 with Unity 6000.3.21f1 and the
  stripped arm64-v8a library (injection shown, `ShortBranches.exe --check`:
  31 methods, 0 problems). Since M11 the project has the source generator
  (`--source-gen BUILD_DIR`, default `<out>/source-generator`), which writes
  the hotfix bridges of `HotfixRegions.cs`'s `[Hotfix]` types; the Generate
  Code launch before the build is gone.
- **`compile_time.py`** measures script compile times with the generator (in
  an `XLuaBindings` asmdef and in Assembly-CSharp); its comparison with
  legacy generated code went with Generate Code in M11, the results are in
  [build/SOURCEGEN.md](../build/SOURCEGEN.md).

Last local run: 2026-10-18, macOS ARM64, Lua 5.5.1 plugin built from
`2698d42`, C# from this branch after merging main at `650e32d` (source
generator M3), with `--fast-enter-play-mode` (and `--csharp12` on 6000.6;
the source generator runs with `--roslyn-sdk`, 6000.6's .NET SDK). LuaLS
reported 0 and 10 diagnostics in each run:

| Unity | Variant | Generated | Cleared |
|---|---|---|---|
| 2021.3.45f2 | `--asmdef` | 32/32 | 32/32 |
| 2021.3.45f2 | `--asmdef --hotfix` | 32/32 | 32/32 |
| 2021.3.45f2 | Assets layout (control) | 32/32 | 32/32 |
| 6000.3.26f1 | `--asmdef` | 32/32 | 32/32 |
| 6000.3.26f1 | `--asmdef --hotfix` | 32/32 | 32/32 |
| 6000.6.6f1 | `--asmdef` | 36/36 | 36/36 |
| 6000.6.6f1 | `--asmdef --hotfix` | 36/36 | 36/36 |
| 2021.3.45f2 | `--asmdef --define FORCE_REFLECTION` (no play mode tests) | 27/27 | 27/27 |

| Unity | `--source-gen` (mode `sourcegen`) | Generator branch |
|---|---|---|
| 2021.3.45f2 | 42/42 | Roslyn 4.3.1: `ref readonly` virtual members and `Measure`/`IGauge` bridges left out (XLSG004), `Dial`'s indexer through its accessors |
| 6000.3.26f1 | 42/42 | Roslyn 4.3.1, as 2021.3 |
| 6000.6.6f1 | 46/46 | Roslyn 4.10: all members and bridges |

No CS8032 or CS9057 in any log; the XLSG003, XLSG004, XLSG008 and XLSG011
lines appear with file and line (XLSG008 and XLSG004 for types from
metadata without one).

Hotfix: `hotfix_intkey.py --asmdef` passed on 2021.3.45f2 and 6000.3.26f1
(both types in field mode, patched and restored in play mode), and without
`--asmdef` on 2021.3.45f2. `hotfix_player.py --asmdef` passed on
2021.3.45f2 and 6000.3.26f1 (post-build injection of Assembly-CSharp and
Game, IntKey fallback warning for both, `fields 1,1 ... patched 20,10
restored 2,1`), and in the Assets layout on 2021.3.45f2 (`fields 0,1
idmap True`, IntKey for Assembly-CSharp).

Hotfix with xLua outside Assembly-CSharp, 2026-10-28, Unity 6000.3.21f1,
macOS ARM64, Lua 5.5.1 plugin from `b3adc8e8`, the injector built from the
same commit:

| Script | Layout | Result |
|---|---|---|
| `hotfix_player.py` | asmdef | `fields 0,0 idmap True ... shapes before 3,4,a patched 30,40,echo a restored 3,4,a` |
| `hotfix_player.py --no-assembly-csharp` | asmdef | the same; `Main.dll`, no `Assembly-CSharp.dll` |
| `hotfix_player.py --package` | package | the same |
| `hotfix_player.py --package --no-assembly-csharp` | package | the same |
| `hotfix_player.py --no-asmdef` | Assets | `fields 0,1 idmap True`, no shapes |
| `hotfix_intkey.py` | asmdef, package | pass, with the shapes |
| `hotfix_intkey.py --no-asmdef` | Assets | pass |
| `editor_tests.py run` (`prepare.py --hotfix --source-gen`) | asmdef, package | 39/39, LuaLS and log checks pass |
| `hotfix_il2cpp.py` (release) | asmdef | build pass, `ShortBranches.exe --check` 31 methods, 0 problems; `XLUA_HOTFIX_REGIONS_PASS: 51 of 51` on the `xlua16k` AVD |

Each upstream behaviour, put back alone, fails a run above:
`injectGenericMethod`'s early return (`InlineShop`) and the attribute
compared by reference (`TaggedShop`) in the injector, the post-build hook's
`Single(... "Assembly-CSharp.dll")` (`--no-assembly-csharp`), and
`Assembly-CSharp.dll` as xLua's assembly (Hotfix Inject In Editor, asmdef
layout). The first two also fail `hotfix_check.py`'s asmdef layout case.

Failures found and fixed on the way:

1. The asmdef layout of `prepare.py` (above); `XluaGeneratedCodeTests` also
   looked for the generated wrappers in LuaEnv's assembly.
2. CS9198 on 6000.6 in the source generator's registration, for an extension
   method with a `ref readonly` parameter (`RefReadonlyExtensions`): the
   registration now disables it as the legacy template does.
3. With the generator installed, M3's `Gen_Flag` turned CodeEmit off, and
   list-only configuration went to reflection: 8 failures (the play mode
   host's `IEnumerator` coroutine had no bridge). The suite's lists are now
   also given in attribute form; a project that installs the generator
   while keeping lists hits the same, see the design's M3 question 1.
4. Hotfix in the asmdef layout: the injector found no bridges for a game
   asmdef ("can not find delegate for Game.Shop.Price"), because the
   generated code is in Assembly-CSharp. Fixed in `Hotfix.cs`;
   `hotfix_check.py` covers it.
5. In the mixed run of item 3, `InIndexed` (an indexer with an `in` key)
   failed through reflection with "cannot set (null), no such field". The
   suite with `--asmdef --define FORCE_REFLECTION` showed it in the cleared
   run (2021.3.45f2): reflection matched the key against the by-reference
   parameter type. Fixed in `Utils.cs`; that run then passed 27/27 in both
   modes. IL2CPP players without a generated wrapper take this path.

Found then and fixed on 2026-10-18 (below, "macOS plugin import settings"):
with the repository's `libxlua.dylib.meta` (macOS, CPU ARM64) Unity
2021.3.45f2 copied the plugin to `Contents/PlugIns/ARM64` of an ARM64 Mono
player, which then failed with `DllNotFoundException`; 6000.3.26f1 loaded
it from there. In 6000.6.6f1 the wrappers of `UnityEngine.PhysicsScene2D`
did not compile with either generator (CS0121, see
[build/SOURCEGEN.md](../build/SOURCEGEN.md)); fixed the same day.

Host checks of the same tree: `run_managed.py --gen-code --gen-code-minimize
--gen-assembly --source-gen` (415 tests in every mode, surface and bridge
diffs equal), `hotfix_check.py` (with the new asmdef layout case),
`build_source_generator.py --test` (22 tests) and `asmdef_compile_check.py
--hotfix` against 2021.3.45f2 and 6000.6.6f1.

Not run then: 2022.3 and 6000.0, IL2CPP players with the source generator
(run on 2026-10-18, see
[MobileSmoke/README.md](MobileSmoke/README.md#2026-10-18-the-source-generator-in-il2cpp-players)),
a hotfix player on 6000.6 (run on 2026-10-18 below) and in IL2CPP, the
Linux and Windows editors.

#### macOS plugin import settings (2026-10-18)

`Assets/Plugins/arm64/libxlua.dylib.meta` now enables the macOS standalone
with CPU `AnyCPU` (was `ARM64`); the dylib is arm64 only
(`make_osx_silicon_*.sh`, `lipo -info`), and the universal
`Plugins/xlua.bundle` (`make_osx_*.sh`: x86_64 and arm64) keeps its
settings. `hotfix_player.py` no longer changes the import settings in its
project; `--arch x64ARM64` builds a universal player and runs it natively
and under Rosetta (`arch -x86_64`), and `--bundle` adds the universal
bundle next to the dylib, as the release plugins ship. Lua 5.5.1 plugins
built with the Xcode generator (Release) on macOS 27 arm64, Assets layout,
Mono players; every run printed `XLUA_HOTFIX_PLAYER_ALL_PASS`:

| Unity | Player | Plugins in the project | `Contents/PlugIns` of the player | Runs |
| --- | --- | --- | --- | --- |
| 2021.3.45f2 | ARM64 | dylib | `libxlua.dylib` | arm64 |
| 2022.3.62f3 | ARM64 | dylib | `libxlua.dylib` | arm64 |
| 6000.3.26f1 | ARM64 | dylib | `libxlua.dylib` | arm64 |
| 6000.6.6f1 | ARM64 | dylib | `libxlua.dylib` | arm64 |
| 2021.3.45f2 | universal | dylib and bundle | `libxlua.dylib`, `xlua.bundle` | arm64, x86_64 (Rosetta) |
| 6000.3.26f1 | universal | dylib and bundle | `libxlua.dylib`, `xlua.bundle` | arm64, x86_64 (Rosetta) |

The x86_64 runs can only have loaded the bundle (the dylib has no x86_64
slice), so Mono goes on to the next candidate after a library of the wrong
architecture; the player logs do not name the file it loaded. Not run: an
Intel Mac, IL2CPP macOS players, LuaJIT, a player with the dylib only on
an Intel or universal build (it has no x86_64 code, so it cannot work
there). Both plugins built here require macOS 27, the build machine's
version: `make_osx_lua5*.sh` and `make_osx_silicon_lua5*.sh` set no
deployment target (the LuaJIT scripts set 12.0); not changed.

The editor suite in mode `sourcegen` (`prepare.py --editor-tools --asmdef
--source-gen --fast-enter-play-mode`, no `--roslyn-sdk`) passed 39/39 on
2021.3.45f2 with the link.xml test added on 2026-10-18
(`MergedLinkXmlReadsAsUtf8`); with the old `SourceGenChecks.cs` that test
fails (38/39).

### UPM package layout (source generator M6)

`prepare.py --package` builds xLua with `Tools/make_upm_package.py` into
`LocalPackages/` of the project and installs it through
`Packages/manifest.json`: by default from a `file:` tarball, which Unity
extracts into `Library/PackageCache` read-only, as a registry install;
`--package folder` uses a `file:` folder. The package has `Runtime` with
`XLua.asmdef`, `Editor` with `XLua.Editor.asmdef`, `Core` with the two
`XLua.Core` asmdefs (the asmdefs of the Assets folder), the plugin in
`Plugins`, and with `--source-gen` the
generator in `Runtime/Analyzers`. The test scripts stay in `Assets` (Generate
Code, until M11, wrote into `Assets/XLua/Gen`). `hotfix_player.py
--package` builds its hotfix player from the package, and
`Test/MobileSmoke/prepare.py --package` the IL2CPP smoke player
([MobileSmoke/README.md](MobileSmoke/README.md#2026-10-18-xlua-from-the-upm-package-in-il2cpp-players)).
`Assets/csc.rsp` reaches the package's assemblies: the compiler response
files of `XLua.dll` under `Library/Bee` had `HOTFIX_ENABLE` and the test
defines in every run below.

Last local run: 2026-10-18, macOS ARM64, Lua 5.5.1 plugin, every run with
`--fast-enter-play-mode`, legacy runs on 6000.6 with `--csharp12`, every
source generator run with `--roslyn-sdk` (6000.6's .NET SDK). LuaLS reported 0
and 10 diagnostics in each run, and the source generator runs had no CS8032,
CS8784, CS8785, CS9057 or XLSG error and the fixture's XLSG003:

| Unity | `--package` (generated / cleared) | `--package --hotfix` | `--package --source-gen` (sourcegen) | `--package --source-gen --hotfix` | `hotfix_player.py --package` |
| --- | --- | --- | --- | --- | --- |
| 2021.3.45f2 | 32/32, 32/32 | 32/32, 32/32 | 43/43 | 43/43 | pass |
| 6000.3.26f1 | 32/32, 32/32 | 32/32, 32/32 | 43/43 | 43/43 | pass |
| 6000.6.6f1 | 36/36, 36/36 | 36/36, 36/36 | 43/43 | 43/43 | pass |

`--package folder --source-gen` on 6000.3.26f1: 43/43. The hotfix
players injected Assembly-CSharp and Game after the build, warned about the
IntKey fallback for both (xLua is in `XLua.dll`, as in the asmdef layout) and
printed `fields 1,1 ... patched 20,10 restored 2,1`.

Every result in the table is from `976cbe1e` (main at `f8e6210d`, core
LuaScript M5, merged into this branch with the fixes below), with the plugin
built from that tree, except the hotfix players on 2021.3.45f2 and
6000.6.6f1, which ran on `7c51094`. An earlier pass on `7c51094` (main at `35ecb23`) gave
the same legacy and hotfix player results and found the failures below.

Failures found and fixed on the way:

1. **LuaCATS after Generate Code** (every layout, since milestone M10): the
   Generate Code units carry `[assembly: GeneratedBindings]` since M0, and the
   M10 manifest reader took them for the source generator's, so LuaCATS left
   Assembly-CSharp's types out ("has bindings from the xLua source generator
   but no manifest") and Validate Setup switched to the generator checks.
   `LuaCATSDescribesUnityAndTestTypes` and
   `LuaCATSTreatsInParametersAsArguments` failed. Markers whose bindings are
   in `XLua.CSObjectWrap` are now ignored there; `luacats_check.py` covers it
   with `Test/LuaCATS/LegacyGenMarker.cs`.
2. **CS9198 on 6000.6** in the source generator's `XLua.Bindings.g.cs` for the
   `ref readonly` delegate `XluaRefReadonlyLib.Measure`: the pragma was only in
   the Bridges file, while the delegate conversion is in the registration. The
   generator unit test `RefReadonlyDelegatesDisableCS9198InTheRegistration`
   covers it.
3. **Two tests that predate later milestones:**
   `XluaRefReadonlyLibTests.GenerationSkipsMembersTheCompilerCannotUse`
   expected LuaCATS to read Assembly-CSharp's lists in mode `sourcegen` (M10
   reads the manifest instead), and
   `XluaSourceGenRefReadonlyTests.WrappersFollowTheEditorsCompiler` expected
   `Gauge.Scaled(ref readonly int)` to be unreachable before Roslyn 4.10; since
   M5 it goes through the reflection fallback, which this was the first Unity
   run of (25, with one fallback warning, on 2021.3.45f2 and 6000.3.26f1).

Not run in the package layout then: 2022.3 and 6000.0, a hotfix player with
the source generator and `hotfix_intkey.py` (all run on 2026-10-18, next
section), the Linux and Windows editors and importing the samples (run on
2026-10-19, below).

#### A release-shaped tarball and its samples (2026-10-19)

`prepare.py --package-tarball FILE.tgz` installs a finished tarball, such as
the release file `com.xlua-ng.xlua-<version>.tgz`, as it is: with its own
plugins (no `--plugin`), and with `--source-gen` only after checking that the
tarball holds `Runtime/Analyzers/XLua.SourceGenerator.dll`.
`Test/MobileSmoke/prepare.py --package-tarball` does the same for IL2CPP
players. `--samples` imports the package's samples as Package Manager's
Import button does, into `Assets/Samples/<displayName>/<version>/<sample>`,
adds the modules they use (uGUI and the built-in UI, IMGUI, animation, asset
bundle, particle system, physics and web request modules) and
`XluaSampleTests.cs`, which `editor_tests.py` then requires:
`SignatureLoaderExampleRunsFromTheImportedSample` runs the SignatureLoader
example from the imported folder and expects its three signed modules to
print. With the samples, the Examples' `NoGc.IntParam` gives Assembly-CSharp
a generated int(int) bridge, so `XluaSmoke.cs` (define `XLUA_SMOKE_SAMPLES`)
expects `Func<int, int>` to bind by signature.

The tarball: `make_upm_package.py --release` with the Lua 5.5.1 plugins
built on this Mac (macOS bundle and dylib, Android with the `minimal` and
`core` variants, iOS device and simulator with both variants; NDK r30,
Xcode 27.1) and **placeholder files** for the Windows and Linux plugins
(`x86_64/xlua.dll`, `x86/xlua.dll`, `arm64/xlua.dll`, `x86_64/libxlua.so`
hold a line of text), 4,265,066 bytes. Placeholders do for a check on macOS,
where nothing loads them; a release package needs the real files, which
`publish.yml` builds. A second assembly with a fresh source generator build
gave the same SHA-256.

`--platform macos --editor-tools --source-gen BUILD --fast-enter-play-mode
--package-tarball TGZ --samples`, mode sourcegen, LuaLS 0 and 10
diagnostics, no XLSG error (the Examples' configuration adds XLSG003 infos
for `BlackList` entries that name members missing on these editors):

| Unity | Editor suite |
| --- | --- |
| 2021.3.45f2 | 44/44 |
| 6000.3.21f1 | 44/44 |

With the example of before the fix (reading `Assets/XLua/Examples`),
`SignatureLoaderExampleRunsFromTheImportedSample` fails on 2021.3.45f2
(43/44): the example's loader finds nothing, `require` falls through to the
Resources loader, which returns the signed bytes unchecked, and Lua reports
"signatured1.lua:1: syntax error near '}'". `editor_compile_check.py` with
`editor-osx-tests` and `editor-osx-minimal-tests` compiled the test sources
(`XluaSmoke.cs` with its new define) on all seven local editors.

IL2CPP Android player from the same tarball (`Test/MobileSmoke/prepare.py
--package-tarball TGZ --source-gen BUILD`, Unity 6000.3.21f1, Development,
ARM64, the `xlua16k` AVD with 16 KB pages): the APK (19,669,747 bytes)
printed `XLUA_NG_SMOKE_PASS: Lua 5.5, ..., Func<int, int> by signature, ...,
io on, LuaSocket on, source generator (7 generated types, bridge
XLua.Generated.Assembly_CSharp_b37fdd7b.Bridges.__Gen_Delegate_Imp0),
disposal`; the selector took the default (legacy) plugin over the package's
`minimal` and `core` variants.

Not run from the tarball: the Windows and Linux plugins (placeholders),
iOS players, release (non-Development) players, other editors than these
two, `--hotfix` and a hotfix player, and a git URL install (no `upm`
branch exists).

### 2022.3, 6000.0, IL2CPP targets and hotfix with the generator (2026-10-18)

The editor suite on the two LTS editors that had not run it with the current
main, in all three layouts, a run with an IL2CPP build target active, and
the hotfix runs M6 and M7 left. macOS ARM64, Lua 5.5.1 plugin
(`BUILD_SILICON`, Release, Xcode generator) and C# from main at `65f7ea17`
(core LuaScript M6) with the fixes below; every editor suite run with
`--fast-enter-play-mode`, every source generator run with `--roslyn-sdk`
(6000.6.6f1's .NET SDK), the package from a `file:` tarball. Unity 2022.3's
Package Manager server is x86_64 and ran under Rosetta 2. LuaLS reported 0
and 10 diagnostics in every run that writes LuaCATS, the source generator runs
had no CS8032, CS8784, CS8785, CS9057 or XLSG error and the fixture's
XLSG003, and the compiler response files of `XLua.dll` and `XLua.Core.dll`
had `HOTFIX_ENABLE` and the test defines in the package runs:

| Unity | Layout | Legacy (generated / cleared) | `--hotfix` (generated / cleared) | `--source-gen` (sourcegen) | `--source-gen --hotfix` |
| --- | --- | --- | --- | --- | --- |
| 2022.3.62f3 | Assets | 33/33, 33/33 | 33/33, 33/33 | | |
| 2022.3.62f3 | asmdef | | | 44/44 | 44/44 |
| 2022.3.62f3 | package | 33/33, 33/33 | 33/33, 33/33 | 44/44 | 44/44 |
| 6000.0.85f1 | Assets | 33/33, 33/33 | 33/33, 33/33 | | |
| 6000.0.85f1 | asmdef | | | 44/44 | 44/44 |
| 6000.0.85f1 | package | 33/33, 33/33 | 33/33, 33/33 | 44/44 | 44/44 |
| 6000.3.21f1, iOS target | Assets | 33/33, 33/33 | | | |
| 6000.6.4f1, WebGL target | package | 33/33, 33/33 | | | |

The counts are one higher than in the tables above: the new
`XluaGeneratedCodeTests.EditorEmitsWithEveryBuildTarget`. Both editors
compile with Roslyn 4.3.1, as 2021.3 and 6000.3 do, so the `ref readonly`
members of `XluaRefReadonlyLib` are left out with XLSG004.

**Reflection.Emit in the editor.** `EditorEmitsWithEveryBuildTarget` reads
the active build target and its scripting backend, checks them against
`XluaEditorTestBackend.txt` (written by `editor_tests.py`: `Mono2x`, or
`IL2CPP` with `--build-target iOS|WebGL`) and, without generated code, looks
in the `XLuaCodeEmit` assembly for an emitted `Target` wrapper and an emitted
`[CSharpCallLua]` delegate bridge. Unity 6.0 (6000.0.85f1, cleared runs)
emits both, as the other editors do. With an IL2CPP target active the
expectation in the ROADMAP (type wrapping by reflection, delegate emit on)
was wrong: Unity compiles editor scripts with `ENABLE_MONO` and without
`ENABLE_IL2CPP` whatever the target. The Bee response files of
Assembly-CSharp and XLua had `ENABLE_MONO` and `UNITY_IOS` (6000.3.21f1, iOS)
or `UNITY_WEBGL` (6000.6.4f1, WebGL), and the cleared runs logged
`iOS IL2CPP, ENABLE_IL2CPP False ... 1 Target wrappers, 1 delegate bridges`.
So ObjectTranslator's `!ENABLE_IL2CPP` branch never applies in the editor;
types go through reflection only in IL2CPP players without generated code.
The test's first version asserted the ROADMAP's expectation and failed on
both targets.

**Hotfix** (`hotfix_intkey.py`, `hotfix_player.py`; injector built with
`Tools/build_tools.py` from the same tree):

| Unity | Layout | Run | Result |
| --- | --- | --- | --- |
| 2021.3.45f2 | package | `hotfix_intkey.py --package` | pass, field mode for both (`fields 1,1`), both fallback warnings |
| 6000.3.26f1 | package | `hotfix_intkey.py --package` | pass, field mode for both (`fields 1,1`), both fallback warnings |
| 2021.3.45f2 | package | `hotfix_intkey.py --package --source-gen` | pass, `fields 0,0`, no fallback warning |
| 6000.3.26f1 | package | `hotfix_intkey.py --package --source-gen` | pass, `fields 0,0`, no fallback warning |
| 6000.3.26f1 | asmdef | `hotfix_intkey.py --asmdef --source-gen` | pass, `fields 0,0`, no fallback warning |
| 2021.3.45f2 | package | `hotfix_player.py --package --source-gen` | pass, `fields 0,0 idmap True ... patched 20,10 restored 2,1` |
| 6000.3.26f1 | package | `hotfix_player.py --package --source-gen` | pass, the same |
| 6000.3.26f1 | asmdef | `hotfix_player.py --asmdef --source-gen` | pass, the same |
| 6000.6.6f1 | package | `hotfix_player.py --package --source-gen` | pass, the same |

These are the first Unity runs of milestone M7's IntKey slots: Hotfix Inject
In Editor (and the player build's post-build step) injected Assembly-CSharp
and Game through their own `HotfixBridges`, wrote
`hotfix_id_map_Assembly-CSharp` and `hotfix_id_map_Game` with ids from 0 and
`["<assembly>"] = "Assembly-CSharp"` / `"Game"`, and `auto_id_map()` patched
and restored both types in play mode and in the Mono player.

Failures found and fixed on the way:

1. **Every asmdef on the source generator stopped compiling with core M6.**
   The generated wrappers name `XLua.MonoPInvokeCallback` and
   `XLua.LuaDLL.lua_CSFunction`, which core LuaScript M6 moved into
   `XLua.Core`; Unity does not pass `XLua`'s references on, so the fixture's
   `SgGame` asmdef (`"references": ["XLua"]`) failed with 27 CS0234 errors on
   2022.3.62f3 in the asmdef and the package layout (main at `65f7ea17`). The
   generator now reports XLSG012 once for such an assembly and writes nothing
   (Unity 6000.0.85f1 showed the single error), a generator unit test covers
   it, the fixtures and the migration tool's `XLuaBindings.asmdef` reference
   `XLua.Core`, and [MIGRATING.md](../docs/MIGRATING.md#xluacore-and-luascript)
   says so.
2. **The WebGL plugin source did not compile since the core time limits**
   (in both layouts; see [MobileSmoke/README.md](MobileSmoke/README.md)):
   fixed in `xlua_webgl.cpp`, and `Test/Native/webgl_compile_check.py`
   compiles it with the editor's own Emscripten.
3. The emit-mode test above (its first version).

After merging main at `34636bff` (the reflection member cache), the Assets
legacy and package `sourcegen` runs on 2022.3.62f3, the package legacy run
on 6000.0.85f1, the iOS-target run and the package hotfix player with the
generator on 6000.3.26f1 passed again with the same counts. After main at
`4b0aa92d` (core LuaScript M10, the buffer library), with the plugin
rebuilt, the 2022.3.62f3 Assets legacy and package `sourcegen` runs and the
WebGL player from the package passed again.

Host checks of the same tree: `build_source_generator.py --test` (31
tests), `run_managed.py --gen-code --source-gen` (448 tests in each mode,
surface and bridge diffs equal), `hotfix_check.py`, `asmdef_compile_check.py
--source-gen --hotfix` against 2021.3.45f2 and 6000.6.6f1,
`webgl_compile_check.py --package` and `editor_compile_check.py`.

Not run: the asmdef layout without the source generator on 2022.3 and
6000.0 (the package runs cover XLua.dll with Generate Code), `--csharp12`
on these editors (Roslyn 4.3.1 cannot), hotfix players in IL2CPP, the Linux
and Windows editors, importing the samples.

### The asmdef layout as the default (2026-10-18)

macOS ARM64, Lua 5.5.1 plugins built from this tree, the hotfix tools from
`Tools/build_tools.py`, one editor at a time. The default layout is the
asmdef one; "no asmdefs" is `prepare.py --no-asmdef`. Trees: main at
`ce869b94` (6000.3.21f1 editor suite) and `0f103ed8` (everything else)
merged into the branch.

| Unity | Run | Result |
|---|---|---|
| 2021.3.45f2 | editor suite, asmdefs | 35/35 generated, 35/35 cleared |
| 2021.3.45f2 | editor suite, asmdefs, `--hotfix` | 35/35, 35/35 |
| 2021.3.45f2 | editor suite, asmdefs, `--source-gen` | 42/42 (`sourcegen`) |
| 2021.3.45f2 | editor suite, no asmdefs | 35/35, 35/35 |
| 2021.3.45f2 | editor suite, no asmdefs, `--hotfix` | 33/33, 33/33 (main at `9a4de919`) |
| 6000.3.21f1 | editor suite, asmdefs; `--hotfix`; `--source-gen` | 35/35 and 35/35 each; 42/42 |
| 6000.3.21f1 | editor suite, no asmdefs; with `--hotfix` | 35/35 and 35/35 each |
| 2021.3.45f2, 6000.3.21f1 | `layout_switch.py` | `XLUA_LAYOUT_SWITCH_PASS` (23 CS0246 errors outside `Assets/XLua` with `XLUA_CORE_ONLY` on 2021.3) |
| 2021.3.45f2, 6000.3.21f1 | `hotfix_intkey.py` (asmdefs) | pass, `fields 0,1 ... patched 20,10 restored 2,1`: IntKey in Assembly-CSharp, field mode with the warning in Game.dll only |
| 2021.3.45f2 | `hotfix_intkey.py --no-asmdef` | pass, the same |
| 6000.3.21f1 | `Test/MobileSmoke/prepare.py --gen` (asmdefs), Generate Code, release ARM64 IL2CPP APK | `XLUA_NG_SMOKE_PASS` (delegates generated) on the Android emulator with 16 KB pages; `XLua.dll` and `XLua.Core.dll` in the player |

LuaLS reported 0 diagnostics for `good.lua` and the 10 expected for
`bad.lua` in each generated run. Before the IntKey fix in `Hotfix.cs`,
`hotfix_intkey.py --asmdef` expected (and got) field mode in Assembly-CSharp
too. Not run: 2022.3, 6000.0 and 6000.6 editors, `hotfix_player.py` (macOS
player) in the new default, iOS and WebGL players, the Linux and Windows
editors, and the menu in a project whose `Assets/XLua` was moved.

### Without the legacy generator (milestone M11, 2026-10-18)

The first runs after M11 removed Generate Code: the suite in modes `sourcegen`
and `reflection` only, the configuration as attributes, the generator also in
the layout without asmdefs. macOS ARM64, the Lua 5.5.1 plugin built from main
at the native code of `24ed1374` (M11 changed no native code), the generator
built from the same tree, the hotfix tools from `Tools/build_tools.py`, one
editor at a time, every suite run with `--fast-enter-play-mode` (no
`--roslyn-sdk` or `--csharp12`). C# at `b80e165c` (the M11 branch) with this
harness, except where noted. Projects under `/Volumes/T7Data/ext-ai-projects/xlua-ng/m11/u`.

```sh
python3 Test/UnityMatrix/prepare.py --output P --plugin libxlua.dylib --unity-version V \
  --test-framework TF --platform macos --editor-tools --fast-enter-play-mode \
  --source-gen SG_BUILD [--no-asmdef] [--hotfix] [--define GEN_CODE_MINIMIZE]
python3 Test/UnityMatrix/editor_tests.py run --unity UNITY --project P --out O --luals LUALS
```

| Unity | Layout | Run | Result |
| --- | --- | --- | --- |
| 2021.3.45f2 | asmdefs | `--source-gen` | 43/43 (`sourcegen`) |
| 2021.3.45f2 | asmdefs | `--source-gen --hotfix` | 43/43 |
| 2021.3.45f2 | no asmdefs | `--source-gen --no-asmdef` | 43/43 |
| 2021.3.45f2 | no asmdefs | `--source-gen --no-asmdef --hotfix` | 43/43 |
| 2021.3.45f2 | asmdefs | `--source-gen --define GEN_CODE_MINIMIZE` | 43/43, GCM wrappers |
| 2021.3.45f2 | asmdefs | no `--source-gen` | 36/36 (`reflection`), at `a5e3882e`; 32/36 before it |
| 6000.3.21f1 | asmdefs | `--source-gen` | 43/43 |
| 6000.3.21f1 | asmdefs | `--source-gen --hotfix` | 43/43 |
| 6000.3.21f1 | no asmdefs | `--source-gen --no-asmdef` | 43/43 |
| 6000.3.21f1 | no asmdefs | `--source-gen --no-asmdef --hotfix` | 43/43 |
| 6000.3.21f1 | asmdefs | `--source-gen --define GEN_CODE_MINIMIZE` | 43/43, GCM wrappers |
| 2021.3.45f2 | asmdefs | `hotfix_intkey.py` | pass, `fields 0,0 ... patched 20,10 restored 2,1`, no fallback warning, maps with `["<assembly>"]` |
| 2021.3.45f2 | no asmdefs | `hotfix_intkey.py --no-asmdef` | pass, `fields 0,1` (Game.Shop Inline, field mode), no fallback warning |
| 2021.3.45f2 | asmdefs | `hotfix_player.py` | pass, `fields 0,0 idmap True before 2,1 patched 20,10 restored 2,1` |
| 2021.3.45f2 | no asmdefs | `hotfix_player.py --no-asmdef` | pass, `fields 0,1 idmap True ...` |
| 2021.3.45f2 | asmdefs, then none, then asmdefs | `layout_switch.py` | `XLUA_LAYOUT_SWITCH_PASS` |
| 6000.3.21f1 | asmdefs | `hotfix_il2cpp.py` (Android ARM64 IL2CPP, release) | `XLUA_HOTFIX_IL2CPP_BUILD_PASS` (injection shown, no undeclared label); on the `xlua16k` emulator `XLUA_HOTFIX_REGIONS_PASS: 51 of 51 checks` |

The 43 are the 29 of `REQUIRED`, `TableIsNotAUnityObject` and
`EmptyTableAsVector3Raises`, the five play mode tests and the seven
`XluaSourceGenTests`; the reflection run has no `XluaSourceGenTests`. LuaLS
reported 0 diagnostics for `good.lua` and the 10 expected for `bad.lua` in
every `sourcegen` run; every log had the fixture's XLSG003 and no analyzer
warning, XLSG error or XLSG011. `GeneratedWrappersFollowGenCodeMinimize`
logged `(ObjectTranslator, IntPtr, int)` functions with `GEN_CODE_MINIMIZE`
and `(IntPtr)` callbacks without it.

**The generator without asmdefs.** This is the first real-editor run of the
generator in the layout without asmdefs: in `Assets/XLua/Analyzers` with no
asmdef above it, Unity passed `-analyzer:.../XLua.SourceGenerator.dll` to
every assembly it compiled (the Bee response files of Assembly-CSharp,
Assembly-CSharp-Editor, UnityEngine.TestRunner and UnityEditor.TestRunner on
2021.3.45f2). Assembly-CSharp, which then holds xLua, got one unit
(`XLua.Generated.Assembly_CSharp_b37fdd7b.Bindings`) with the suite's types,
SgLocal, SgGame.Shop and xLua's default GCOptimize pushers; Lua called
through its `W_` wrappers, the delegates through its `Bridges`, and LuaCATS
read Assembly-CSharp's manifest (19 wrapped types). No other assembly got a
unit. `layout_switch.py` saw the same after Use Assembly-CSharp (Assembly-CSharp
13 types, no XLua assembly), and Assembly-CSharp's and XLua's units again
after Use asmdefs; the menu logged that the generator now runs in every
assembly. With `XLUA_CORE_ONLY` the project's scripts failed with 45 CS0246
errors outside `Assets/XLua` (the attributes in `XluaEditorTestTarget.cs` are
xLua types too).

**Hotfix.** Every bridge came from the generator's `HotfixBridges`.
`Game.asmdef` in the layout without asmdefs cannot reference xLua (it is in
Assembly-CSharp), so `Game.Shop` is selected by an Editor list with
`HotfixFlag.Inline`, injected without bridges in field mode and patched by
name; Validate Setup reports that list as one the generator does not read
(true, but its advice to convert and delete it does not fit an assembly that
cannot reference xLua), which `hotfix_intkey.py --no-asmdef` accepts.

**Failure found:** in the reflection run, before `a5e3882e`, the four play
mode tests that start a Lua coroutine failed with "This type must add to
CSharpCallLua: System.Collections.IEnumerator": the Editor's emitted bridges
read `[CSharpCallLua]` on types and in lists, not
`[assembly: CSharpCallLua(typeof(T))]`, so the form the migration tool writes
left a project without the generator without its emitted bridges. Fixed in
`a5e3882e` (the reflection paths read the assembly-level forms); the rerun
passed 36/36.

Not run: 2022.3, 6000.0 and 6000.6 editors, `--package`, `--roslyn-sdk` and
`--csharp12` (the ref readonly tests were updated, not run), `--build-target`,
`compile_time.py` (updated, not run), the Linux and Windows editors, the
device examples (`Test/Examples`, updated, not run).

### `in` and `ref readonly` parameters (2026-10-19)

Hotfix of methods with C# `in` and `ref readonly` parameters, and Lua
callbacks with them, which until then had run only in Unity's editor suite
(callbacks) or nowhere (hotfix). macOS ARM64, Lua 5.5.1 plugin
(`cmake -DBUILD_SILICON=ON`, Release), Mono 6.14.1, .NET SDK 10.0.401, the
hotfix tools from `Tools/build_tools.py`; main at `58bd6940` with the
branch's changes. After merging main at `c23e8e0e` (the minimal preset's GCM
default, the profiler fix in `build/xlua.c`), with the plugin rebuilt,
`run_managed.py`, `hotfix_check.py` and `coverage_cs_dotnet.py` passed
again with the same counts.

| Run | Result |
| --- | --- |
| `hotfix_check.py` | `HOTFIX_CHECK_PASS`; the second assembly's run has 200 checks, 45 of them new (`RemoteReadonlyArgs*` before, patched and restored). Before the injector fix it stopped with "can not find delegate for HotfixFixture.SecondSG.RemoteReadonlyArgs.InVirtual: its HotfixBridges has no bridge with this signature" |
| `run_managed.py --source-gen --source-gen-minimize --source-gen-fallback` | 473 tests in each mode; the callbacks' parameters are `in,ref readonly` in the Roslyn builds and `value,value` under mcs; the surface diffs pass with the 3 documented differences |
| `coverage_cs_dotnet.py` | 473 tests in each of `reflection`, `sourcegen` and `sourcegen-minimize` on .NET, the callbacks' parameters `in,ref readonly`; runtime branches 78.8% in the suite, lines 87.6% |
| Unity 6000.6.6f1, `hotfix_intkey.py --csharp12` (asmdef layout) | `XLUA_HOTFIX_INTKEY_PASS`, Validate Setup without problems, no fallback warning; `XLUA_INTKEY_BYREF: before 5,5 5,5 5,5 5,5 5,5 \| patched 50,99 50,5 50,5 50,5 50,5 \| restored 5,5 5,5 5,5 5,5 5,5` (Counted, Scaled, ScaledVirtual, ScaledRefReadonly, ScaledRefReadonlyVirtual) and `fields 0,0 before 2,1 patched 20,10 restored 2,1` |
| `editor_compile_check.py` | 7 editors x 82 configurations: 566 ok, 7 skipped (C# 12 on Roslyn 4.3.1), 1 Mono csc crash (exit -6, `assets-player-android-il2cpp-minimal` on 6000.3.21f1) that passed when rerun |

Not run: `hotfix_intkey.py` on an editor with Roslyn 4.3.1 (2021.3 to
6000.3, `in` only), `hotfix_player.py` (whose player has the same
`HotfixIntKeyLocal`), the editor suite, and Lua 5.3.6, 5.4.9 and LuaJIT.

### After M11 on every local editor (2026-10-28)

The first editor suite runs since main changed under it: the minimal preset
without the reflection fallback (`XLUA_NO_REFLECTION`, `XLUA_REFLECTION`
opts back in), overload selection by best fit, the bridge checks by build
type (`XLUA_BRIDGE_CHECKS`), `ObjectDisposedException` for a disposed env
([#999](https://github.com/Tencent/xLua/issues/999)) and the native bridge
version 110. macOS ARM64, the Lua 5.5.1 plugin
(`build/make_osx_silicon_lua55.sh`) and the generator built from the tree,
LuaLS 3.19.1, one editor at a time, every run with `--fast-enter-play-mode`.
Main at `b3adc8e8` first; the table is the rerun after the migration menu fix
below, on main at `6c3b0095` with this branch (one more test in every run).
"Strict minimal" is `--source-gen --define XLUA_PRESET_MINIMAL`: the twelve
tests of `NEEDS_REFLECTION` must fail with "has no generated bindings", and
did. Every `sourcegen` run had LuaLS 0 diagnostics for `good.lua` and the 10
expected for `bad.lua`, the fixture's XLSG003 and no XLSG error.

| Unity | Run (`prepare.py`) | Result |
| --- | --- | --- |
| 6000.3.21f1 | reflection (no `--source-gen`) | 38/38 |
| 6000.3.21f1 | `--source-gen` | 45/45 |
| 6000.3.21f1 | strict minimal | 33 passed, the 12 expected failures |
| 6000.3.21f1 | `--define XLUA_PRESET_MINIMAL --define XLUA_REFLECTION` (reflection) | 38/38 |
| 6000.3.21f1 | `--package --source-gen`; `--package` | 45/45; 38/38 |
| 6000.3.21f1 | `--roslyn-sdk` (6000.6.6f1's .NET SDK) with and without `--source-gen` | 48/48; 40/40 |
| 6000.3.21f1 | reflection, `editor_tests.py --build-target iOS` | 38/38 (`iOS IL2CPP, ENABLE_IL2CPP False, mode reflection, type wrappers emitted`) |
| 6000.6.6f1 | `--source-gen`; reflection | 45/45; 38/38 |
| 6000.6.6f1 | `--csharp12` with and without `--source-gen` | 49/49; 42/42 |
| 6000.6.6f1 | `--source-gen --langversion latest` | 45/45 |
| 6000.0.85f1 | `--source-gen`; reflection | 45/45; 38/38 |
| 2022.3.62f3 | `--source-gen`; reflection | 45/45; 38/38 |
| 2021.3.45f2 | `--no-asmdef --source-gen`; `--no-asmdef` | 45/45; 38/38 |

- **The #999 change in play mode:** `FastEnterPlayModeDisposesEachSession`
  and `FastEnterPlayModeWithoutSceneReload` expect `ObjectDisposedException`
  from the previous session's `LuaFunction` and `LuaTable` since `0a7c2ea5`;
  that ran in Unity for the first time here and passed in every run above
  except strict minimal, where the play mode host's
  `MonoBehaviour.StartCoroutine` has no bindings (expected).
- **`ref readonly` through reflection** on Roslyn 4.3.1: with `--roslyn-sdk`
  on 6000.3.21f1 the suite logged `XluaRefReadonlyLib.Gauge.Scaled is reached
  through reflection: it has ref readonly parameters, which this C# compiler
  cannot call (CS0570)`, and `XluaRefReadonlyLibTests` and
  `XluaSourceGenRefReadonlyTests` passed.
- **2022.3.62f3** ran its x86_64 Package Manager server under Rosetta 2;
  2021.3.45f2 likewise.
- **The narrower plugin import settings** (`36303557`: `arm64/libxlua.dylib`
  for the macOS Editor only, the desktop libraries for their own OS): after
  merging main at `d37515c2`, with the plugin rebuilt, nine of the runs
  above passed again with the same counts (6000.3.21f1 reflection,
  `--source-gen`, strict minimal and `--package --source-gen`; 6000.6.6f1
  reflection and `--source-gen --csharp12`; 6000.0.85f1 and 2021.3.45f2
  `--no-asmdef` with the generator; 2022.3.62f3 reflection), so every editor
  still loads the macOS ARM64 library (`Lua55BridgeRunsInTheEditor`,
  `LuarcMatchesThePlugin`).

**Source generator M10 and M9 in Unity.** M10 on 6000.6.6f1: a project with
the asmdef layout, the generator in `Assets/XLua/Src/Analyzers` and
`Test/LuaCATS/Sample.cs` (the Roslyn copy that `luacats_check.py
--source-gen` compiles, from `Test/Native/roslyn_fixtures.py`, with
`-langversion:12` for its `ref readonly` parameters) and `SampleAttributes.cs`
in Assembly-CSharp. XLua > Generate LuaCATS Definitions logged "the
configuration of Assembly-CSharp, XLua comes from the xLua source generator's
manifest (8 wrapped types)", and `LuaTypes/` equalled `Test/LuaCATS/expected`
file for file. Validate Setup found no problem; without the `RoslynAnalyzer`
label on `XLua.SourceGenerator.dll` it reported the label and "has xLua
configuration but no generated bindings" for Assembly-CSharp and XLua; with
the label back and an `Assets/XLua/Gen/XLuaGenAutoRegister.cs`, the Generate
Code output problem; without that file, none. M9 on 2021.3.45f2 (the
[design's](../docs/design/source-generator.md#m9-as-implemented-2026-10-18)
steps 1 and 2): `ConfigMigrationMenu.MigrateWithBindingsAsmdef` in a project
without asmdefs that has the examples and tutorials with the lists they
carried until M11 (`Test/Native/MigrationSample/ExampleLists`) next to their
converted `Assets/XLua/Examples/XLuaConfig.cs`, and the
`Assets/XLua/Editor` lists (`MigrationSample/DefaultConfig`): one
`Assets/XLuaConfig/XLuaConfig.cs` with `ExampleConfig.BlackList` (20 entries
by name), the examples' lists reported as already converted, the
`HotfixTestCfg` types as also `[Hotfix]` on the type, no `XLuaBindings`
folder, a clean compile, and a second run that changed no file. In the
asmdef layout with the generator, a `Game` asmdef with a `[LuaCallCSharp]`
list of its types and `UnityEngine.Rect`, and an Editor list of
`UnityEditor.EditorWindow`: `Assets/Game/XLuaConfig.cs` (Game.Shop,
Game.Item), `Assets/XLuaConfig/XLuaBindings/` with `Rect` and an asmdef
that references XLua and XLua.Core, `EditorWindow` in
`Assets/XLuaConfig/Editor/`, no wrong-assembly error, and after the reload
`W_Game_Shop_*`, `W_UnityEngine_Rect_*` and `W_UnityEditor_EditorWindow_*`
in `Game.dll`, `XLuaBindings.dll` and `Assembly-CSharp-Editor.dll`, with
Validate Setup reporting only the two lists left to delete.

**Failure found and fixed:** the first M9 run without asmdefs copied the
`[LuaCallCSharp]`, `[CSharpCallLua]` and `[GCOptimize]` attributes on the
examples' types into `XLuaConfig.cs` (33 of its 53 attributes; 20 belong),
reported the eight `HotfixTestCfg` types as "not one the tool writes into",
and its second run logged "was not rewritten because it was converted from
[LuaCallCSharp] on the types of Assembly-CSharp, which no longer exists". The
menu refused to write into xLua's own assembly, which is Assembly-CSharp in
that layout, and `MigrationResult.Lists` left out the lists made from type
attributes. New `XluaEditorToolsTests.MigrationMenuLeavesTypeAttributesWhereTheGeneratorRuns`
failed on each cause alone (with the old rules put back into a
`--no-asmdef --source-gen` project on 2021.3.45f2) and passes with the fix
([MIGRATING.md](../docs/MIGRATING.md#converting-configuration-lists)).

Host checks of the same tree: `migration_check.py` (all five
configurations; it had stopped at Assembly-CSharp since `aa39b15f`, whose
SignatureLoader example uses `UnityEditor` in the Editor, and now compiles
Assembly-CSharp with `UnityEditor.dll` as Unity does).

Not run: `compile_time.py`, the device examples (`Test/Examples`),
`--build-target WebGL` (no WebGL module for 6000.3.21f1 or 6000.6.6f1), the
hotfix variants (another lane ran them the same day, above), M10's LuaCATS
step on 2021.3.45f2 (`Sample.cs` has `ref readonly` parameters, which
Roslyn 4.3.1 cannot compile; ran later the same day, below), the Linux and
Windows editors.

### LuaCATS from the editor and the strict minimal player (2026-10-28)

`Test/UnityMatrix/luacats_unity.py --unity EXE --unity-version V --plugin
LIBXLUA.dylib --output EMPTY_DIR [--no-ref-readonly]` repeats M10's LuaCATS
step in a real editor: an asmdef-layout project with the generator in
`Assets/XLua/Src/Analyzers`, `Test/LuaCATS/Sample.cs` (the Roslyn copy) and
`SampleAttributes.cs` in Assembly-CSharp, XLua > Generate LuaCATS Definitions
in batch mode, and `LuaTypes/` and `.luarc.json` compared with the files
`luacats_check.py` compares on the host. By default it compiles with
`-langversion:12` against `Test/LuaCATS/expected` (Unity 6000.6); with
`--no-ref-readonly` it defines `LUACATS_NO_REF_READONLY` and compares with
`expected-no-ref-readonly` over `expected` (Unity 2021.3 to 6000.3, Roslyn
4.3.1). Main at `aadd1d87` with this branch, macOS ARM64, the Lua 5.5.1
Editor plugin built from `d37515c2` (the native code is the same), one
editor at a time:

| Unity | Fixture | Result |
| --- | --- | --- |
| 2021.3.45f2 (Package Manager under Rosetta 2) | `--no-ref-readonly` | 15 files equal, `.luarc.json` equal |
| 6000.3.21f1 | `--no-ref-readonly` | 15 files equal, `.luarc.json` equal |
| 6000.6.6f1 | `-langversion:12` | 15 files equal `expected`, `.luarc.json` equal |
| 6000.3.21f1 | `-langversion:12` (control) | fails as expected: `error CS1617: Invalid option '12' for /langversion` |

Each log has "the configuration of Assembly-CSharp, XLua comes from the
xLua source generator's manifest (8 wrapped types)". `luacats_check.py`
passed on the host with the new overlay (`LUACATS_GOLDEN_PASS:
no-ref-readonly, 15 files match`), and its `--update` left every other
expected file unchanged.

The strict minimal smoke player (the minimal preset without
`XLUA_REFLECTION`, with the generator binding what the smoke calls) passed
in release and Development builds on the `xlua16k` emulator from Unity
6000.3.21f1
([MobileSmoke/README.md](MobileSmoke/README.md#2026-10-28-strict-minimal-players)),
and `size_report.py --configs strict` measured the preset as shipped
([build/SIZE.md](../build/SIZE.md#reflection-off-in-the-minimal-preset)).

Not run: LuaCATS on 2022.3.62f3 and 6000.0.85f1 (Roslyn 4.3.1 as well),
LuaLS over the editor's output in this script (the editor suite does that
with its own fixture), the Linux and Windows editors.

`--platform linux` (2026-10-28) installs `x86_64/libxlua.so` as the Editor
plugin, and `--prepare-only` / `--verify-only` split the run around a launch
made elsewhere: `LuaCATSStep.Run` writes what it logs to the file given by
`-xluaBuildLog`, and `--verify-only` checks that file (the compiler's own
messages, XLSG included, are not in it) and the definitions. Owner decision
of 2026-10-28: `unity-matrix.yml`'s `luacats` job runs it on 2021.3.45f2
(`--no-ref-readonly`) and 6000.6.6f1 with a unity-builder step. Not run yet:
the job is skipped without the Unity secrets.

### Compile check without Unity

`Test/Native/editor_compile_check.py --output <dir>` compiles the xLua C#
against the reference assemblies of installed editors, without a licence and
without starting Unity (it only reads the editor's files, so it can run while
a phone benchmark holds adb). It splits the sources as Unity does. In the
asmdef layout (the default since 2026-10-18): `XLua.Core` (`Assets/XLua/Core`
without `Editor/`) and `XLua` (`Assets/XLua/Src` without `Editor/`, against
`XLua.Core`) against .NET Standard 2.1 and the UnityEngine modules, in Editor
configurations `XLua.Core.Editor` and `XLua.Editor` (the `Editor` folders)
against `unity-4.8-api`, UnityEngine and UnityEditor, and the test sources in
`Assembly-CSharp` and `Assembly-CSharp-Editor` against all of them. The
`assets-*` configurations repeat 15 of them (the editors of macOS and
Windows, hotfix, the feature defines, the tests, the minimal preset,
LuaScript, and the Android, iOS and WebGL players) without asmdefs, as after
XLua > Project Layout > Use Assembly-CSharp: `Assembly-CSharp` with
`Assets/XLua/Core` and `Assets/XLua/Src` without `Editor/`, and
`Assembly-CSharp-Editor` with their `Editor` folders, which references
`Assembly-CSharp`. The `UNITY_*` version defines follow each editor
(`UNITY_6000_0_OR_NEWER` and so on).

`--list` prints the 85 configurations (68 in the asmdef layout and the 17
`assets-*` ones), their layout and their defines (since
2026-10-18 `editor-osx-luascript`, `player-android-il2cpp-luascript`,
`player-ios-luascript` and `player-webgl-luascript` compile `LuaScript` with
`XLUA_LUASCRIPT`; the eight `*source-gen` configurations below run the source
generator, `editor-osx-minimal-source-gen` and
`player-android-il2cpp-minimal-source-gen` the minimal preset as meant, without
reflection since 2026-10-19; `editor-osx-minimal-reflection` compiles the
preset with its `XLUA_REFLECTION` opt-in):

- **Editor** (both assemblies): the macOS, Windows and Linux editor defines;
  with `HOTFIX_ENABLE`, with the thirteen feature defines (`XLUA_NO_SIGNATURE_LOADER`,
  `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL`, `XLUA_NO_PROFILER`,
  `XLUA_NO_STREAMING_ASSETS_LOADER`, `XLUA_LOCK_METATABLES`, the bridge
  checks' `XLUA_BRIDGE_CHECKS` and `XLUA_NO_BRIDGE_CHECKS`, the bridge
  hardening defines `XLUA_RESTRICT_GETMETATABLE`, `XLUA_LOCK_XLUA_TABLE`,
  `XLUA_RESTRICT_PRIVATE_ACCESS` and `XLUA_NO_LOAD_ASSEMBLY`, and
  `XLUA_STRICT_INT_RANGE`), `THREAD_SAFE`,
  `GEN_CODE_MINIMIZE`, the old options (`FORCE_REFLECTION`, `GENERIC_SHARING`,
  `DISABLE_OBSOLETE_WARNING`, `NOT_GEN_WARNING`), and Android, iOS and WebGL as
  the active build target.
- **Editor with tests**: also the sources that `prepare.py --editor-tools
  --fast-enter-play-mode` copies (`Test/UnityMatrix`, `Test/MobileSmoke/XluaSmoke.cs`
  and `SmokeBuild.cs`), plain, with `HOTFIX_ENABLE` and with the feature
  defines, against the nunit and test-framework packages the editor bundles
  (compiled from their sources first; skipped with a note when the editor
  bundles none). `editor-osx-csharp12-tests` adds the C# 12 fixture of
  `--csharp12` with `-langversion:12`, and is skipped when the compiler does
  not know C# 12. `XluaRefReadonlyLib*` (`--roslyn-sdk`) is not compiled.
- **Player** (the runtime assemblies only, no UNITY_EDITOR and no UnityEditor
  assemblies): Standalone macOS Mono and Windows IL2CPP, Android Mono, IL2CPP and
  `XLUA_ANDROID_SOURCE_PLUGIN`, iOS, tvOS, WebGL, Switch and UWP IL2CPP; with
  `HOTFIX_ENABLE`; Android IL2CPP with each feature define, `GEN_CODE_MINIMIZE`,
  `THREAD_SAFE`, `DEVELOPMENT_BUILD`, all feature defines and all of them
  together, `XLUA_MEMORY_DIAGNOSTICS` with `XLUA_NO_UTIL`, `XLUA_NO_PROFILER`,
  `SINGLE_ENV` and `THREAD_SAFE`; iOS with the feature defines and hotfix; `NATIVE_LUA_PUSHSTRING`,
  `SINGLE_ENV`, `GENERIC_SHARING` and `NOT_GEN_WARNING`.
- **The bridge checks:** with `ikdasm` or `monodis` (Mono) on the PATH, every configuration
  also disassembles the runtime assembly (`XLua`, or `Assembly-CSharp` in the
  Assets layout) and checks that it calls `xlua_tryget_cachedud_checked` and
  `xlua_check_struct_accessors` exactly when the bridge checks belong in that
  build: Editor and `DEVELOPMENT_BUILD` configurations unless
  `XLUA_NO_BRIDGE_CHECKS`, and any configuration with `XLUA_BRIDGE_CHECKS`
  (`player-android-il2cpp-xlua-bridge-checks` is the release player that
  forces them). A release player that calls them, or a Development player
  that does not, fails the run.
- **The minimal preset** (`XLUA_PRESET_MINIMAL` alone): the macOS Editor, with
  and without the tests; Android IL2CPP, also with `XLUA_UTIL` and with
  `DEVELOPMENT_BUILD`; iOS with hotfix and `GEN_CODE_MINIMIZE`; WebGL. Since
  2026-10-19 these compile xLua's GCM mode, which the preset implies.
- **The source generator** (`editor-osx-source-gen`, `editor-osx-gen-code-minimize-source-gen`,
  `player-android-il2cpp-source-gen`, `player-android-il2cpp-gen-code-minimize-source-gen`,
  and for the minimal preset `editor-osx-minimal-source-gen`,
  `player-android-il2cpp-minimal-source-gen` and their `-minimal-no-gcm-`
  twins with `XLUA_NO_GCM`):
  `Assembly-CSharp` also gets `XluaEditorTestTarget.cs` with the editor suite's
  configuration attributes and is compiled
  with the editor's own Roslyn and `-analyzer` (built once into `--output`). The run fails
  when the generator does not load or writes no bindings, or warns XLSG013, and checks the wrappers: native
  callbacks, or with `GEN_CODE_MINIMIZE` (or `XLUA_PRESET_MINIMAL` without
  `XLUA_NO_GCM`, `Tools/presets.py`'s `gcm()`) the generator's GCM functions without them.
  `assets-editor-osx-source-gen` and `assets-player-android-il2cpp-source-gen`
  run the generator in the Assets layout's `Assembly-CSharp`, which holds xLua
  itself: the only generated bindings the way back has since the legacy
  generator was removed (milestone M11).

Without `--unity` it uses every editor under `/Volumes/T7Data/Unity/Editors`,
`/Applications/Unity/Hub/Editor`, `~/Unity/Hub/Editor` and `/opt/unity`
(`Unity.app` on macOS, `Editor/Data` on Linux; pass `--unity-version` when the
path does not name the version). `--compiler system` (the default when `csc`
is on `PATH`) uses Homebrew Mono's Roslyn csc 3.9 (C# 9, Unity's language
version); `--compiler unity` runs each editor's own Roslyn with its bundled
`dotnet`. `--config 'editor-*'` selects configurations. Errors fail the run;
warnings are counted (`--warnings` lists them). Response files and compiler
logs stay under `--output`. A full run over seven editors takes about two
minutes with either compiler.

`Test/Native/asmdef_compile_check.py --unity <Unity.app> --unity-version
2021.3 --output <dir> [--hotfix]` covers the asmdef layout together with the
examples and tutorials: `XLua`
(for the Editor and, as `XLua.Player.dll`, for an Android IL2CPP player),
`XLua.Editor`, `Assembly-CSharp` from the examples and tutorials and
`Assembly-CSharp-Editor` from their `Editor` folders, each with only the
references Unity gives it (the editor-only ones against `unity-4.8-api`, the
others against .NET Standard 2.1), with the .NET SDK's csc at C# 9. It also
checks the asmdef files (names, platforms, references, `autoReferenced`),
that `Assets/XLua` has exactly the four asmdefs in their folders and that
their `.meta` files keep their GUIDs. `--no-asmdef` compiles the way back
instead: xLua, the examples and the tutorial in `Assembly-CSharp` (also as an
IL2CPP player assembly) and every `Editor` folder in
`Assembly-CSharp-Editor`, as upstream. Since milestone 6 of the core preset it compiles `XLua.Core`
(`Assets/XLua/Core`) and
`XLua.Core.Editor` first, and `XLua` against `XLua.Core`; the player builds
keep the assemblies' names, since `XLua.Core` grants `XLua` its internals.
With `--source-gen` it also checks the mode of `Assembly-CSharp`'s wrappers
against the defines (`--define XLUA_PRESET_MINIMAL` gives GCM functions,
`--define XLUA_NO_GCM` next to it native callbacks) and prints their count.
`--core-only` is the editor-level core-only check: it defines `XLUA_CORE_ONLY`,
requires the define constraints to leave out `XLua` and `XLua.Editor` and
keep `XLua.Core`, and compiles `Test/Core/UnitySample/LuaScriptSample.cs`
against `XLua.Core` alone for the Editor and an IL2CPP player. `--luascript`
defines `XLUA_LUASCRIPT` and compiles the sample next to the bridge.
`Test/UnityMatrix/core_only_check.py --unity <Unity executable>
--unity-version <version> --plugin <core libxlua.dylib> --output <dir>` runs
the same check in a real editor: the UPM package with the core library as the
Editor plugin in an empty project, `XLUA_CORE_ONLY` put into Player Settings by
a first batch launch, then a launch that compiles the sample and the probe
`Test/UnityMatrix/CoreOnly/XluaCoreOnlyProbe.cs`, which checks the loaded
assemblies, the plugin selector's choice and a `LuaScript` call, then drives
coroutines from `EditorApplication.update` (milestone 9): the `IEnumerator`
driver, the `Task` driver on Unity's `SynchronizationContext` and, on Unity
2023.1 and later, the `Awaitable` driver with completion sources, a
cancellation and a script error. It passes
when `Library/ScriptAssemblies` has XLua.Core, XLua.Core.Editor and
Assembly-CSharp but no XLua or XLua.Editor (6000.3.26f1 and 2021.3.45f2, 2026-10-18). A
use of an xLua `internal` member from outside `Assets/XLua/Src` fails it.
`--source-gen` also runs the source generator in the four assemblies, as Unity
does next to `XLua.asmdef` (a generator warning or error fails the run), and
requires `XLua.dll` to carry the pushers of xLua's UnityEngine GCOptimize
defaults. With `--no-asmdef` it runs the generator in `Assembly-CSharp`,
`Assembly-CSharp-Player` and `Assembly-CSharp-Editor`, as Unity runs it when
no asmdef scopes it, and requires the pushers of those defaults and the
examples' wrappers in `Assembly-CSharp`, where xLua then is.
`07_AsyncTest` is left out (it needs the uGUI package). It passes against
2021.3.45f2, 2022.3.62f3, 6000.3.26f1 and 6000.6.6f1; the plain, `--core-only`,
`--luascript`, `--source-gen` and `--hotfix` runs pass against 2021.3.45f2 and
6000.3.26f1 (2026-10-18). `Test/UnityMatrix/prepare.py` copies the asmdefs
into the test project, so the editor suite runs in that layout;
`--no-asmdef` runs it without them.

`Test/Native/migration_check.py --plugin <libxlua> --output <dir> [--unity
<Unity.app>] [--update]` checks the configuration migration tool
(`XLuaGenerate --migrate`, [Converting configuration lists](../Assets/XLua/Doc/Migrating_Configuration.md))
on Unity code without starting Unity. It compiles com.unity.ugui from the
editor's built-in packages, `Assets/XLua/Src` with the examples and tutorials
and the lists the examples carried until milestone M11
(`Test/Native/MigrationSample/ExampleLists`) into `Assembly-CSharp` (the
Assets layout), their `Editor` folders and the configuration that
`Assets/XLua/Editor` shipped until M11 (`Test/Native/MigrationSample/DefaultConfig`)
into assemblies of their own, against the editor's
UnityEngine DLLs and Mono's .NET assemblies (`--bcl unity` takes Unity's
reference assemblies instead). Then, for five configurations, it runs the
tool on the assemblies with the lists and the source generator on one
compilation of the same sources with the tool's attributes, and compares the
generator's manifest with `surface_diff.py` against
`Test/Native/MigrationSample/expected/<configuration>.json`: the surface,
bridges, hotfix bridge signatures and GCOptimize types that the legacy
generator bound from the same lists (`XLUA_GEN_MANIFEST`, with
`--hotfix-assemblies`), frozen with Unity 2021.3.45f2 and `--bcl mono` when it
was removed. With another editor or `--bcl unity` the frozen surfaces do not
apply and only the grouped configurations are compared with the plain ones.
`--update-surfaces` writes the manifests there after a change that alters a
surface on purpose:

- `examples`: the examples' lists; the tool's output must equal the checked-in
  `Assets/XLua/Examples/XLuaConfig.cs`, the examples' configuration (`--update` writes it);
- `sample`: `Test/Native/MigrationSample/ExampleConfigSample.cs`,
  `ExampleConfig.cs` with its LINQ queries switched on (about 290 wrapped types);
- `edge`: `Test/Native/MigrationSample/MigrationEdgeCases.cs`, the cases the
  tool must spell or report; the report must contain a line for each;
- `examples-grouped` and `edge-grouped`: the same with `--migrate --group`,
  which writes several types of a list, attribute, flags and namespace in one
  attribute (`[assembly: XLua.Hotfix(XLua.HotfixFlag.IgnoreProperty |
  XLua.HotfixFlag.IntKey, typeof(...), typeof(...))]`); the grouped examples
  file must name the same types as the checked-in one.

Allowed differences, which the conversion does not cause, are listed in the
script: array types (not wrapped by the source generator) and the overload
order of `TrailRenderer.AddPositions` (an `[Out]` parameter that is not
`out`). `--only NAME` runs one configuration.

What it cannot cover: generated wrappers (run the editor suite for those),
analyzers and source generators that Unity adds, `asmdef` layouts (see
`asmdef_compile_check.py` above), the UWP
`Windows.Security.Cryptography` branch of `SignatureLoader` (it needs the
Windows SDK's `Windows.winmd`, so the UWP configuration sets
`XLUA_NO_SIGNATURE_LOADER`), UWP without IL2CPP, `USE_UNI_LUA` and the
platform-specific UnityEngine assemblies of each player (all configurations
use the editor's). Defines that Unity derives from project settings, such as
`ENABLE_IL2CPP` in the Editor, follow the configuration names, not a real
project.

`unity-matrix.yml` runs it with `--compiler unity` inside each editor image
before the editor launches.

Last local run: 2026-10-18, macOS ARM64, with both compilers. All 40
configurations compiled on every editor with 0 errors, except
`editor-osx-csharp12-tests`, which was skipped where the compiler does not know
C# 12 (every editor with Mono's csc, and 2021.3 to 6000.3 with their own
Roslyn). After the bridge hardening defines joined the feature list (47
configurations), Mono's csc on the same seven editors gave 322 ok and 7
skips (2026-10-18, the editor's Roslyn not rerun):

| Unity | Test packages (bundled) | Mono csc 3.9 | Editor's Roslyn |
|---|---|---|---|
| 2021.3.45f2 | nunit 1.0.6, test-framework 1.1.33 | 39 ok, 1 skip | 39 ok, 1 skip |
| 2022.3.62f3 | nunit 1.0.6, test-framework 1.1.33 | 39 ok, 1 skip | 39 ok, 1 skip |
| 6000.0.85f1 | nunit 2.0.5, test-framework 1.6.0 | 39 ok, 1 skip | 39 ok, 1 skip |
| 6000.3.21f1 | nunit 2.0.5, test-framework 1.6.0 | 39 ok, 1 skip | 39 ok, 1 skip |
| 6000.3.26f1 | nunit 2.0.5, test-framework 1.6.0 | 39 ok, 1 skip | 39 ok, 1 skip |
| 6000.6.4f1 | nunit 2.1.0, test-framework 1.8.0 | 39 ok, 1 skip | 40 ok |
| 6000.6.6f1 | nunit 2.1.0, test-framework 1.8.0 | 39 ok, 1 skip | 40 ok |

Warnings: `WWW` is obsolete (`StaticLuaCallbacks.cs:724`, Android players
without `XLUA_NO_STREAMING_ASSETS_LOADER`). On 6000.6
`AndroidSdkVersions.AndroidApiLevel25` was obsolete (Unity 6.6 builds for API
26 and up), so `SmokeBuild.cs` now uses API 26 there. A deliberate UnityEditor call in runtime code
failed every player configuration, and an error under `UNITY_6000_0_OR_NEWER`
in an Editor file failed only on 6000.x. The Linux `Editor/Data` lookup was
checked only with a macOS editor linked into that layout, and the CI step has
not run yet.

## Verification round of 2026-10-28

After about forty branches landed on 2026-10-28 (overload scoring, the owner
thread, binding-mode parity, the struct, enum and `Nullable` argument and
result rules, strict-minimal table conversion, disposed `LuaBase`, registry
checks, binding API 2, XLSG014, native struct bounds and `set_limits`, the
vendored Lua patches, LuaJIT GC64 only, native bridge version 112,
`EXPECTED_TESTS` 522), most of them tested on Lua 5.5.1 and LuaJIT on macOS
only, one round ran everything below on main at `a2f8ad12`, then again what
the merge of `fe4f71dc` (`xlua_gc` on Lua 5.4) touches. Host: macOS 27
arm64, Apple clang, Mono 6.14.1, .NET SDK 10.0.401, each run through
`native-test-slot.sh` with a timeout. Container: `xlua-analysis:26.04`,
`docker run --memory 6g`, Linux arm64, on a copy of the tree at `a2f8ad12`.
Unity: one editor at a time under `unity.lock`. Logs:
`final-verify-2026-10-28/` on the build disk.

| Configuration | Platform | Result |
| --- | --- | --- |
| `run_managed.py --source-gen --source-gen-minimize --source-gen-fallback`, legacy, Lua 5.3.6, 5.4.9, 5.5.1, LuaJIT | macOS | 522 tests in each mode of each pass (LuaJIT after the fix below) |
| the same with `--define XLUA_PRESET_MINIMAL`, minimal libraries, 5.4.9 and 5.5.1 | macOS | pass |
| the same with `--define THREAD_SAFE`, and with `--define HOTFIX_ENABLE`, 5.5.1 | macOS | pass after the fix below |
| `check_exports.py`, `native_regression.py` on every library (legacy 5.3.6, 5.4.9, 5.5.1, LuaJIT; minimal 5.4.9, 5.5.1; core 5.4.9, 5.5.1; legacy 5.5.1 with `XLUA_LUASCRIPT`) | macOS | pass |
| `profiler_smoke.py` on every Lua 5.x bridge library | macOS | pass |
| `core_smoke` (core 5.4.9, 5.5.1) | macOS | 147 cases, 13 modules |
| `run_core.py` core 5.4.9, 5.5.1; `--bridge` | macOS, Mono and .NET | 87 tests; 91 |
| `hotfix_check.py` (5.5.1) | macOS | 200 checks, every case |
| `crash_repro.py` (5.3.6, 5.4.9, 5.5.1, LuaJIT) | macOS | as recorded in crash-inventory.md; `panic_unprotected_rawset` caught and the env usable on all four |
| `lua_tests.py` (5.3.6, 5.4.9, 5.5.1, three modes each); `luajit_tests.py` (four modes) | macOS | `final OK` everywhere; 510 / 60 + 2, 388 / 59 + 2, 306 / 40 (as on 2026-10-18) |
| `editor_compile_check.py` (seven editors) | macOS | 588 ok, 7 skips (`editor-osx-csharp12-tests`) |
| `presets_check.py`, `check_meta.py`, `build_source_generator.py --test` | macOS | pass; 275 `.meta` files; 50 tests |
| `asmdef_compile_check.py` (2021.3.45f2 plain, `--hotfix`, `--source-gen`, `--no-asmdef --source-gen`; 6000.6.6f1 `--source-gen`) | macOS | pass after the fix below |
| `migration_check.py` (2021.3.45f2) | macOS | pass after the fix below |
| `sanitize.sh`: legacy 5.3.6, 5.4.9, 5.5.1, LuaJIT; core 5.4.9, 5.5.1; `XLUA_PRESET=minimal` 5.3.6, 5.4.9, 5.5.1, LuaJIT | container | pass (reflection only, no .NET SDK in the image) |
| `coverage.sh`, default `VERSIONS` | container | every gate passes. Changed from the table in "Static analysis, coverage and fuzzing": `xlua.c` 420 of 420, 434 of 434 and 434 of 434 branches; `xlua_core_limits.c` 156 of 156 (legacy 5.4.9, 5.5.1) and 158 of 158 (core); `memory_leak_checker.c` 262 of 312, 263 of 314 and 261 of 310. The rest as there |
| `analyze.sh` | container | clean |
| editor suite, 6000.3.21f1: `--source-gen`; reflection; strict minimal; `--hotfix --source-gen` | macOS | 45/45; 38/38; 33 and the 12 expected failures; 45/45. LuaLS 0 and 10 diagnostics |
| editor suite `--source-gen`, 2021.3.45f2 (Rosetta 2) and 6000.6.6f1 | macOS | 45/45; 45/45 |
| `hotfix_player.py` (asmdef layout), 6000.3.21f1 | macOS ARM64 Mono player | `fields 0,0 idmap True ... shapes ... restored 3,4,a` |
| Android smoke players (MobileSmoke/README.md) | `xlua16k` AVD; moto g play 2024 | every player passed |

**Failures, all fixed:**

1. `run_managed.py --define THREAD_SAFE` and `--define HOTFIX_ENABLE` stopped
   at mcs with CS0619: the disposed `LuaBase` case (`1391dc30`) calls
   `LuaTable.GetKeys`, which is `[Obsolete(..., true)]` under both defines.
   Nothing ran these defines in CI (since Q71 the 5.5.1 Linux job does). The call is now left out there.
2. `CaseOwnerThreadCheck` failed once in the LuaJIT source generator pass
   ("the caller was not finalized"). The object is made on a thread that
   ends, but `Join` returns before Mono unregisters that thread, so one
   collection can still scan its stack. The two finalizer checks now
   collect until the finalizer ran (at most 20 times).
3. `asmdef_compile_check.py` failed in every mode with CS0234: the
   SignatureLoader example uses `UnityEditor` under `UNITY_EDITOR`
   (`aa39b15f`), and the check compiled the Editor's Assembly-CSharp without
   `UnityEditor.dll`, which Unity gives it. The same fix as `ee1c0306` for
   `migration_check.py`.
4. `migration_check.py`'s `sample` configuration found a delegate bridge for
   `XLua.Binding.StructOps`, a public delegate of binding API 2
   (`64d847f3`): the sample's `AllDelegate` list takes every delegate a
   public member of Assembly-CSharp uses, and there Assembly-CSharp holds
   xLua. The frozen `expected/sample.json` has that entry now.

**Harness note:** the first launches of 6000.3.21f1 and 6000.6.6f1 stopped
at "Scripts have compiler errors" without a compiler error: Bee's IPC
threw `ArgumentOutOfRangeException: SocketAddress.Size` because the runner had
set `TMPDIR` to a long folder on the build disk, and the Unix socket path
under it is longer than macOS allows. 2021.3.45f2 was not affected. Leave
`TMPDIR` at the system default for editor launches.

Not run: the Windows and Linux editors, iOS and WebGL players, the fuzz
targets (another lane), `coverage_cs.py` and `coverage_cs_dotnet.py`,
`obfuscation_check.py`, `luacats_check.py`, `bytecode_luaenv.py` on the host
(the container's `coverage.sh` ran it), and the source generator passes
under the sanitizers (the image has no .NET SDK). All of them except the
Windows and Linux editors, the players and the fuzz targets ran later the
same day, below.

### The checks the round left out (2026-10-28)

Main at `a9e5425e`, the same host and container image as above, the Lua
5.5.1 libraries built from that tree (`-DBUILD_SILICON=ON`, Release; the
Editor plugin with `build/make_osx_silicon_lua55.sh`). Host checks through
`native-test-slot.sh` with a timeout, Unity under `unity.lock`, every
`prepare.py` run with `--platform macos --editor-tools
--fast-enter-play-mode`. Logs: `verify2-2026-10-28/` on the build disk.

| Configuration | Platform | Result |
| --- | --- | --- |
| editor suite `--source-gen`; reflection, 2022.3.62f3 (arm64 editor, its Package Manager server x86_64 under Rosetta 2) | macOS | 45/45; 38/38. LuaLS 0 and 10 diagnostics |
| editor suite `--source-gen`; reflection, 6000.0.85f1 | macOS | 45/45; 38/38. LuaLS 0 and 10 diagnostics |
| editor suite `--package --source-gen`; `--package`, 6000.3.21f1 | macOS | 45/45; 38/38 |
| `luacats_unity.py`, 6000.6.6f1 (`-langversion:12`) | macOS | 15 files equal `expected`, `.luarc.json` equal |
| `luacats_unity.py --no-ref-readonly`, 2022.3.62f3 and 6000.0.85f1 (first runs on these editors) | macOS | 15 files equal `expected` + `expected-no-ref-readonly`, `.luarc.json` equal |
| `obfuscation_check.py` | macOS, Mono | plain, obfuz rules and `[Obfuscation]` only: 17 checks each, 0, 1 and 5 failing as expected |
| `luacats_check.py --luals` (and again with `--source-gen`, which has done nothing since M11) | macOS, Mono | default, `no-ref-readonly` and strict: 15 files match each; LuaLS `good.lua` 0, `bad.lua` 18, `good_strict.lua` 0, `bad_strict.lua` 4 expected diagnostics |
| `coverage_cs.py` | macOS, Mono | 522 tests in every mode; runtime lines 88.3% in the suite, 88.7% combined (reflection 80.0%, sourcegen 68.5%, sourcegen-minimize 68.6%); generated lines 62.7% and 71.2% |
| `coverage_cs_dotnet.py` | macOS, .NET 10.0.401, coverlet 10.1.0 | 522 tests in every mode; runtime branches 81.2% in the suite (reflection 73.4%, both source generator modes 61.2%), 81.3% combined; runtime lines 88.1% |
| `bytecode_luaenv.py` (bytecode-only 5.5.1) | macOS | reflection, `source-gen`, `source-gen-minimize`, `locked` |
| `signature_check.py` | macOS, Mono | pass |
| `sanitize.sh` with the .NET SDK (below): legacy 5.5.1, 5.3.6, 5.4.9, LuaJIT, `bytecode-5.5.1` | container | pass; the suite with reflection, `--source-gen` and `--source-gen-minimize` (522 tests each, 114 generated wrappers), `bytecode_luaenv.py` in all four modes |
| the same with `XLUA_PRESET=minimal`: 5.5.1, 5.3.6, 5.4.9, LuaJIT | container | pass; the suite in the same three modes, 522 tests each |

**The source generator passes under the sanitizers.** `sanitize.sh` runs them
when it finds `dotnet`; the image has none. This run mounted the pinned SDK
read-only: `dotnet-sdk-10.0.401-linux-arm64.tar.gz` from
`builds.dotnet.microsoft.com`, whose SHA-512 equals the one
`Tools/dependencies.py` records, extracted outside the tree:

```sh
docker run --rm --memory 6g -v "$COPY":/src -v "$OUT":/out -v "$SDK":/opt/dotnet:ro \
    -e PATH=/opt/dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
    -e DOTNET_ROOT=/opt/dotnet -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    -e VERSIONS="5.5.1 5.3.6 5.4.9 luajit bytecode-5.5.1" -w /src xlua-analysis:26.04 \
    Test/Native/sanitize.sh /out/sanitize
```

`$COPY` is a copy of the checkout: the LuaJIT runs rebuild
`build/luajit-2.1` in place. Without `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`
the first `dotnet restore` aborts ("Couldn't find a valid ICU package"): the
image has no `libicu`. The dotnet processes run without the preloaded ASan
runtime (`build_source_generator.dotnet_environment`), so the generator and
csc are not instrumented; the native library and Mono are, as in the
reflection pass.

To have this in the image itself, the cheapest change is a build stage in
`Test/Docker/Dockerfile` that downloads that archive, checks it with
`sha512sum -c` against the pin, and extracts it to `/opt/dotnet`, with
`ENV DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:$PATH
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`: about 230 MB compressed, and one
more place to update when `mise.toml`'s pin moves. Ubuntu's own `dotnet-sdk`
package would follow the archive's version instead of the pin. Not changed
here: the image is the owner's decision (open question 6 of
[source-generator.md](../docs/design/source-generator.md)).

**Harness note:** `prepare.py`'s `--platform` defaults to `linux`, which
installs the plugin as `Plugins/x86_64/libxlua.so`. On macOS without
`--platform macos` every test that touches Lua fails with
`DllNotFoundException: xlua` (37 of 45, 34 of 38); the first launches here
did that and were rerun.

Not run: the Windows and Linux editors, players, the fuzz targets,
`luacats_check.py` with LuaJIT or Lua 5.4 plugins, `coverage.sh` and the core
preset under the sanitizers with the SDK (the core preset runs no managed
code).

## Performance tests

`Test/PrefTest` contains the upstream benchmark scene. Copy its directories into
`Assets` and run `xLuaPerfTest/Main.unity`. It is a benchmark, not a correctness
gate, and has not been rerun as part of the runtime upgrade validation.

Keep build/backend settings comparable when benchmarking. `HOTFIX_ENABLE` and
`THREAD_SAFE` can add bridge locking overhead. Unity API work and overloaded
method resolution also affect timings; a bridge benchmark should keep the
called method's own work small. Vector3 performance includes the actual C#
Unity struct calls rather than an equivalent implementation entirely in Lua.

## Benchmarks outside Unity

`Test/Native/benchmark.py` measures the bridge, the Lua runtimes, allocations
and plugin size under Mono, so a change can be compared with an earlier result.
It is not part of CI and asserts nothing. `Test/Native/benchmark.sh <output dir>`
builds a Release `libxlua.so` for each runtime in `VERSIONS` (default
`5.5.1 5.4.9 5.3.6 luajit`, Linux only, builds LuaJIT in place like
`sanitize.sh`) and runs it:

```sh
Test/Native/benchmark.sh /absolute/path/bench-out
python3 Test/Native/benchmark.py --plugin 5.5.1=/absolute/path/libxlua.so --output /absolute/path/bench-out
python3 Test/Native/benchmark.py --compare old/benchmark.json new/benchmark.json
```

`Test/Native/Benchmark.cs` holds the cases. Each case runs a fixed number of
iterations: one warm-up run, five timed runs (`--runs`, `--warmup`; `--scale`
multiplies the counts) and, after an untimed pass that lets xLua's object pool
and Lua's tables grow, an allocation pass with the Lua collector stopped. The
Lua loops return or store their results so that LuaJIT cannot remove the work.
The result file (`benchmark.json`, or `--json`) has min and median ns/op,
ops/s, managed bytes/op (`GC.GetAllocatedBytesForCurrentThread`, or the heap
size when the runtime lacks it, see `managed_bytes_exact`), Lua bytes/op
(`collectgarbage("count")`), Gen0 collections during the timed runs, the Lua
version, the initial Lua heap, host, Mono version and commit
(`XLUA_BENCH_COMMIT` overrides `git rev-parse HEAD`; `XLUA_BENCH_HOST` adds a
free-text host note, for example the machine behind a container). Stdout gets a table per
runtime with `median ns/op, managed B/op, Lua B/op`. `--compare` prints the
relative change per case and size; positive means slower or larger.

- `lua_cs`: Lua calling a static method, an instance method, a property and a
  field, string arguments and returns, and passing, creating and reading a
  struct with `[GCOptimize]` and the same struct without it (boxed);
  returning an object Lua already holds (a userdata cache hit), the
  `xlua.genaccessor` getter and setter on a decimal, and reading a field of
  the global `xlua` table. `--define NAME` (repeatable) builds xLua and the
  cases with a C# define, such as one of the bridge hardening defines.
- `cs_lua`: a `[CSharpCallLua]` delegate, `LuaFunction.Call` (boxes and returns
  `object[]`), `LuaFunction.Func<,,>`, string arguments and struct round trips.
- `table`: `LuaTable.Get`/`Set` with integer and string keys.
- `env`: creating and disposing a `LuaEnv`.
- `lua`: pure Lua loops (function and method calls, array and hash tables,
  string concatenation and `string.format`, closures, integer and 64-bit
  integer arithmetic; LuaJIT uses xLua's `int64` userdata for the latter). They
  do not use the bridge, so they run once per runtime (mode `any`).

`--modes` selects how the bridge is built: `reflection` (`FORCE_REFLECTION`,
like an IL2CPP player without generated code), `emit` (wrappers emitted at run
time, the editor path), and `sourcegen` and `sourcegen-min`, the source
generator's wrappers, plain and in its GCM mode, compiled with the .NET SDK's
Roslyn. The default is `reflection,emit,sourcegen`. The legacy generator's
modes (`gencode`, `gencode-min` and their `-csc` builds) were removed with it
in milestone M11; [build/SOURCEGEN.md](../build/SOURCEGEN.md) has the results
that compared them. `[GCOptimize]`
only removes allocations in the generated modes. Mono JIT timings are not
IL2CPP timings; compare results from the same host, image and settings.

Every `--plugin` is also recorded by size: raw, stripped (a stripped copy,
`llvm-strip` or `strip --strip-unneeded` for ELF, `strip -x` for Mach-O) and
gzip -9 of the stripped file. `--size LABEL=PATH` adds other plugins, for
example Android `.so` files from `build/make_android_lua55.sh`. Player sizes
need Unity builds and are not measured by CI or `benchmark.sh`; pass them with
`--player LABEL=PATH` (APK, AAB, IPA or zip: total size and the xLua entries
with compressed size; a directory such as a WebGL build: total size, largest
files and xLua files). Without `--plugin` only sizes are recorded.

### Compiler and linker options

`Test/Native/flag_sweep.py` builds `libxlua` with other compiler and linker
options and measures each build; `build/FLAGS.md` holds the last result. It does
not change the release scripts: each variant configures `build/CMakeLists.txt`
like `build/android_common.sh` and adds its options through CMake cache
variables or a compiler launcher for per-file optimisation levels. These
variants turn the release build's `XLUA_SIZE_FLAGS` off, so the older tables
stay reproducible; the `real*` variants are the release build itself
(`XLUA_SIZE_FLAGS` on) with options added on top, such as `real23-harden` or
`real23-cheap` (`-fno-delete-null-pointer-checks` everywhere, zeroed locals in
xLua's own three files only, through a second launcher).
`python3 Test/Native/flag_sweep.py list --output DIR` prints the variants.

```sh
python3 Test/Native/flag_sweep.py android --output DIR --ndk NDK [--abis arm64-v8a,armeabi-v7a] [--variants O3,Oz,...]
python3 Test/Native/flag_sweep.py bench --output DIR --ndk NDK --rounds 5   # needs a device on adb
python3 Test/Native/flag_sweep.py report --output DIR [--base real23] [--tag .harden]
python3 Test/Native/flag_sweep.py ios --output DIR                         # macOS with Xcode
python3 Test/Native/flag_sweep.py host --test --output DIR                 # run_managed.py and the native checks per variant
```

`android` records the stripped size, gzip -9 and the deflated size in a zip (what
the library costs in a compressed APK). `bench` builds `Test/Native/flag_bench.c`,
a runner that opens a library with `dlopen` and looks up only the functions
`LuaDLL.cs` imports, pushes it and the libraries to `/data/local/tmp/xlua-bench`,
smoke-tests every library, then runs all variants in a shuffled order per round
with a cooldown and a temperature limit, and removes the directory at the end.
`--cpus` sets the cores the runner pins itself to (default `4,5,6,7`, the big
cluster of the phone in `build/FLAGS.md`; `0,1,2,3` for its little cores). Each
run records the CPUs the runner was on and its allowed set, from the runner
itself and from `/proc/<pid>/status`, so a report can show that the pinning held.
Every `adb` command is logged to `DIR/adb-commands.log`. The runner's cases are
the pure Lua cases of `Benchmark.cs`, more Lua workloads (recursion, floats,
string patterns, `table.sort`, coroutines, GC) and the C API paths of the C#
bridge (Lua calling C, C calling Lua, `xlua_rawgeti`, `xlua_pgettable`,
`xlua_pushcsobj`, compiling, creating a state). Each case's result must be the
same for every variant; `report` flags any difference. `--base` names the
variant that `bench` always runs and `report` compares the others with
(default `O3`); `--tag` keeps separate bench runs apart. `profile` records the
baseline with the device's `simpleperf` and ranks the source files.
`ios` compiles `libxlua.a` with the flags of the Xcode project
`make_ios_lua55.sh` generates and links it with `-dead_strip` into a stub that
references every iOS import; its `release-split` variant is the file split of
`XLUA_SIZE_FLAGS`, which the Xcode generator cannot build.
`split --platform macos|linux` builds the platform's release library with
`XLUA_SIZE_FLAGS` off (the default there) and on (macOS with Ninja, plus the
Xcode build of the default as a control and, with `--layouts N`, layout
controls linked with shuffled ld64 order files; Linux inside the container,
with `CC` choosing the compiler and `--tag` naming the run), `split-bench`
times the host libraries with `flag_bench.c` over `--rounds` in a shuffled
order (`--max-load` waits for a quieter host) and `split-report` prints both
tables (`--stat min_ns` uses each process's fastest run):

```sh
python3 Test/Native/flag_sweep.py ios --output DIR --variants O3,release-split
python3 Test/Native/flag_sweep.py split --platform macos --layouts 2 --output DIR
python3 Test/Native/flag_sweep.py split-bench --platform macos --rounds 9 --max-load 12 --output DIR
docker run --rm -v "$PWD":/src:ro -v DIR:/out -w /out xlua-analysis:26.04 \
    python3 /src/Test/Native/flag_sweep.py split --platform linux --tag=.arm64-gcc --output /out
``` `host --test` holds the `--lock` directory while it
runs the suites.
`android --src DIR --suffix=-pre` builds the variants from another checkout's
`build/` (such as an archive of `build/` at the commit before a merge) under
another name, so that `bench` times both trees in one run. A variant can
carry runner options: `real23-core` times the release build with states from
`xlua_core_newstate` (`flag_bench.c --core`, as `LuaEnv(LuaOptions)`), and
`real23-ilimit` and `real23-tlimit` add an instruction limit of 10^15 and a
time limit of one hour (`--instruction-limit`, `--time-limit-ns`), which no
case reaches; their smoke run prints the instruction count of its limited
call. "Core limits on the protected-call path" in `build/FLAGS.md` used them.

## Player size

`Test/MobileSmoke/size_report.py` measures what xLua adds to a release IL2CPP
player and compares upstream Tencent/xLua (commit `59bf426` by default, taken
from git history with its prebuilt plugins) with xlua-ng. The last results and
what drives them are in the "Player size" section of `build/README.md`.

```sh
python3 Test/MobileSmoke/size_report.py run --work /external/player-size \
  --plugins /external/native \
  --unity-mobile /path/to/6000.3.x/Unity.app/Contents/MacOS/Unity \
  --unity-webgl /path/to/6000.6.x/Unity.app/Contents/MacOS/Unity
python3 Test/MobileSmoke/size_report.py report --work /external/player-size \
  --size "ng 5.5.1 arm64-v8a=/external/native/plugin_lua55/Plugins/Android/libs/arm64-v8a/libxlua.so"
```

`--plugins` is the `XLUA_BUILD_ROOT` of `build/make_android_lua55.sh`,
`make_android_lua54.sh`, `make_android_lua53.sh`, `make_android_luajit.sh` and
the `make_ios_*.sh` scripts (`plugin_lua55/Plugins/...`). `upstream+gen` also
needs the macOS arm64 Editor plugin in `plugin_lua55/Plugins/arm64`.
Each variant is an empty project with `SizeProbe.cs` (copied as
`Assets/XluaSmoke.cs`): the baseline does not use xLua; the other variants create
a `LuaEnv`, run a chunk, call a C# static method from Lua and a Lua function from
C#. They get `Assets/XLua/{Src,Resources,Editor}` (`Resources` only for upstream
and for commits before 2026-10-17, when it was removed) and the plugins, not the
examples. `+pkg` installs xLua as the UPM package of `Tools/make_upm_package.py`
from a `file:` tarball instead of `Assets/XLua`, and `+pkg+sg` puts the source
generator into the package and `SizeSgConfig.cs` (the probe, `Vector3`,
`GameObject`, `Transform`, `Debug` and two delegates, as assembly attributes)
into Assembly-CSharp; `+pkg+sg+gcm` compiles that with `GEN_CODE_MINIMIZE`,
the generator's GCM mode. `--configs sg-minimal,sg-high` builds the baseline,
`ng-5.5+pkg`, `+pkg+sg` and `+pkg+sg+gcm` with Minimal and High stripping.
Until milestone M11 removed Generate Code, `+gen` and `+gen+gcm` built the
same configuration with Generate Code (`SizeGenConfig.cs`) and its
`GEN_CODE_MINIMIZE` output, in both layouts; their results stay in
[build/SIZE.md](../build/SIZE.md#generated-bindings-generate-code-gcm-and-the-source-generator).
`+gen` is now accepted only for `upstream` (upstream's own Generate Code with
`SizeGenConfig.cs`, in `--configs default`) and `+at-REF` trees from before M11.
`+game` swaps the small configuration for a game-sized one
(`SizeGameSgConfig.cs`: 264 UnityEngine and UnityEngine.UI types as
attributes, converted from the former `SizeGameGenConfig.cs` lists), adds
`com.unity.ugui` to every `+game` project, the baseline too, and makes the
probe log `XLUA_SIZE_TIMING` lines for six Lua calls into wrapped types;
`--configs game-minimal,game-medium,game-high` builds the baseline and the
source generator, plain and in its GCM mode, from the package (results, with
the Generate Code rows measured before M11, in [build/SIZE.md](../build/SIZE.md#a-game-sized-configuration-2026-10-18)).
The `+game` lists are computed from the Editor's assemblies, so another Unity
version may need more `EditorOnlyFilter` entries and a new `SizeGameSgConfig.cs`. A `+nosa` variant compiles with `XLUA_NO_STREAMING_ASSETS_LOADER`;
`+nosig`, `+notemplate`, `+noutil` and `+noprofiler` add `XLUA_NO_SIGNATURE_LOADER`,
`XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` and `XLUA_NO_PROFILER` (not in `CASES`; pass
them with `--variants`). `+minimal` puts `XLUA_PRESET_MINIMAL` into the Player
Settings scripting defines and copies the plugin from
`Plugins/<platform>/xlua-variants/minimal/` under `--plugins` (built with
`XLUA_VARIANT=minimal XLUA_PRESET=minimal`) next to the default one, so the
plugin selector (`Editor/PluginVariants.cs`) picks it during the build, as in a
project; `--configs minimal` builds `baseline`, `ng-5.5` and `ng-5.5+minimal`
for Android (Minimal stripping) and iOS, and the report's JSON says, per player,
whether the packed `libxlua.so` or the iOS link map has `luaopen_io` and
`luaopen_socket_core` (the minimal library has neither). `+minimal` has no
reflection fallback and nothing bound, so its probe does not run:
`+minimal+reflection` adds `XLUA_REFLECTION`, and `+minimal+strict` installs
the source generator in `Assets/XLua/Src/Analyzers` and binds what the probe
calls (`SizeProbe.cs` with `XLUA_SIZE_STRICT`), the preset as shipped;
`--configs strict` builds the baseline and the three for Android. Results in
[build/SIZE.md](../build/SIZE.md#presets). Keep `--work` short: Unity's build
pipes live in `TMPDIR` (`WORK/tmp`): with a 69-character `--work` Unity
6000.3.21f1 failed before compiling with `SocketAddress.Size` from
`NamedPipeServerStream` (macOS limits a socket path to 104 bytes), while a
`TMPDIR` of 68 characters worked for the smoke builds. A
`+uwr` variant's probe also calls `UnityWebRequest.Get` once, as a project that
downloads anything does; the report measures it against `baseline+uwr`.
`--configs default` builds only the named configurations of `CASES`.
`SmokeBuild.cs` builds them with `XLUA_SMOKE_RELEASE=1` (no Development flag)
and the managed stripping level, Android ABIs, AAB and WebGL compression
named by `XLUA_SMOKE_*` variables; `CASES` in the script lists the matrix. iOS
exports are compiled with `xcodebuild -sdk iphoneos -destination generic/platform=iOS
CODE_SIGNING_ALLOWED=NO LD_GENERATE_MAP_FILE=YES DEPLOYMENT_POSTPROCESSING=YES`,
so the binary is stripped as in an archive and the linker map gives each static
library's share of `UnityFramework`. Nothing is signed or installed.

`run` skips builds that already have `build-info.json`, so an interrupted run
resumes; delete a player directory to rebuild it. Managed stripping persists in
the project, so the Android configurations after the first name their level.
Set `GRADLE_USER_HOME` and `TMPDIR` on the external disk (the script points
them into `--work`; `GRADLE_RO_DEP_CACHE` can name an existing Gradle cache).
After each Android build the script stops the Gradle daemon Unity leaves
running. `--lock DIR` holds a `mkdir` lock while Unity or xcodebuild runs, and
`--timeout` kills the process group of a run that hangs and stops the script.
`report` writes `size-report.json` and `size-report.md` in the work directory:
package and component sizes with deltas against the baseline of the same
platform and settings, the iOS link map split, and every `--size` plugin raw,
stripped and gzip -9 (as `benchmark.py --size`; set `STRIP` to the NDK
`llvm-strip` for ELF files).

`Test/MobileSmoke/size_breakdown.py --work DIR` splits those builds into parts and
writes `size-breakdown.json` and `size-breakdown.md`; `build/SIZE.md` holds the
last result. It needs `ANDROID_NDK` for `llvm-nm`, `llvm-readelf` and
`llvm-strip`.

- Native: each `--android-map LABEL=SO:MAP` is a `libxlua.so` linked again with
  `-Wl,-Map` (the command is in the script's help). The lld map gives each
  object file's bytes; the script reads those bytes from the library and
  gzips them. `--ios-variant 5.5.1=ng-5.5` does the same for the `libxlua.a`
  members in that iOS player's linker map.
- C#: the symbols of `libil2cpp.dbg.so` in `Library/Bee/artifacts/Android` of
  the variant and baseline projects (`--variant`, `--baseline`). The generated
  C++ under `il2cppOutput/cpp` tells which assembly defines each symbol;
  xLua's symbols are matched to the types declared in `Assets/XLua/Src`, and
  class library symbols are grouped by type name. `--ios-il2cpp` applies the
  same owners to `libGameAssembly.a` in the iOS linker maps; `--medium
  VARIANT:BASELINE` adds a column for projects last built with Medium
  stripping.
- Metadata: the `global-metadata.dat` header gives each section's size.
- Engine: the symbols the variant's unstripped `libunity.so` has and the
  baseline's lacks.

Library holds only a project's last build, so build the configuration you want
to split last. `size_report.py` builds the AAB last, which uses the same
stripping and ABI as the default APK.

`--configs core,core-high` measures `LuaScript` (core-luascript.md, sections 6
and 8.10) with Minimal and High stripping, on Android and, with `--platforms
ios`, on iOS (device SDK; the iOS libraries come from `make_ios_lua55.sh` and
`XLUA_VARIANT=core XLUA_PRESET=core`, and `xcodebuild` compiles each export
unsigned): `baseline`, `ng-5.5+pkg`, `core-5.5`
(the package with the core library from
`plugin_lua55/Plugins/Android/xlua-variants/core/` under `--plugins`, built with
`XLUA_VARIANT=core XLUA_PRESET=core`, as its plugin, `XLUA_CORE_ONLY`, and
`SizeProbeCore.cs`), `core-5.5+nodefine` (the same with `XLUA_LUASCRIPT`, so
the `XLua` assembly is compiled but unreferenced) and `mixed-5.5` (the bridge
library from `xlua-variants/luascript/`, built with `XLUA_VARIANT=luascript
XLUA_LUASCRIPT=ON XLUA_BUFFER=ON`, and both probes). Their defines go into the
Player Settings in a launch of their own (`SmokeBuild.ApplyDefines`) before
the build. `--one-unity` waits before each Unity launch while another editor
runs on the machine (a process named `Unity`, `pgrep -x Unity`: until
2026-10-19 the check matched the editor's path anywhere in a command line, so
size_report.py, whose `--unity-mobile` names it, waited for itself). Results in
[build/SIZE.md](../build/SIZE.md#luascript-in-players).
`Test/MobileSmoke/core_only_player_check.py PLAYER --project DIR` then checks a
player for bridge code: `XLua.dll` in `ScriptingAssemblies.json`, `XLua` types
in `global-metadata.dat`, bridge exports of `libxlua.so`, `XLua.dll` among the
stripped assemblies (and the iOS link map with `--link-map`); `--report` only
prints the findings.

`size_report.py run --variants LIST` builds the listed variants in each
configuration of `--configs` instead of the lists in `CASES`. The variant
flags `+at-REF` (Assets/XLua from a commit), `+gsize` (IL2CPP Code Generation
"Faster (smaller) builds"), `+srcplugin` (the Lua, xLua and LuaSocket C
sources in `Assets/Plugins/Android/xlua` or `Assets/Plugins/iOS/xlua` instead
of the prebuilt library), `+forceinternal`, `+master`, `+cflags`, `+alloc`
(the allocation probe) and `+sim` (iOS Simulator) are described at the top of
the script; `build/UNITY_SPIKE.md` has what they measured. The `select-*`
configurations build `baseline+select` and `baseline+constraints`, which
choose between two prebuilt Android libraries at build time;
`plugin_select_report.py` says which one each APK holds.
`size_compare.py --pair LABEL=A:B` prints the APK, `libil2cpp.so`, metadata
and plugin deltas between two Android projects with the symbol split of
`size_breakdown.py`, and `--generics` and `--linq` report generic code and
System.Linq in one project.

`alloc_probe.py` measures managed allocations and time of `new LuaEnv()` and
of the first and later access to C# types in reflection mode
(`AllocProbe.cs`): `editor` in a macOS Editor project, from the checkout or
from `--ref`, and `sim` in an iOS Simulator player built with
`size_report.py` (`ios` variant `...+alloc+srcplugin+sim`); `compare` prints
the steps of two runs side by side.
