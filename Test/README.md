# Test coverage

The repository has a bridge unit suite, performance examples, native regression
checks, and Unity player checks. It does not yet have an exhaustive test matrix
for every supported Unity version, platform, backend, or optional extension.

## General C# bridge suite

`Test/UnitTest` exercises Lua-to-C# calls, the explicit reflection cases, `in` and `ref readonly` parameters,
regression tests for upstream issues listed in `UPSTREAM.md`
(`StreamingAssets/luaCallCsUpstream.lua`), the code that the
[coverage runs](#c-coverage) found untested (`StreamingAssets/luaCallCsCoverage.lua`,
fixtures in `xLuaTest/LuaCallCS/CoverageForLuaCallCS.cs`), the opt-in bridge
hardening defines (`StreamingAssets/luaBridgeHardening.lua`) and C#-to-Lua calls. The Lua
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
turns into a C# exception.

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
With `--gen-code` it then builds the tools, runs `XLuaGenerate` on the test
assembly, compiles `XLuaUnitTestGenCode` from the freshly generated wrappers
(not the checked-in `General/Gen2` snapshot) and runs the same tests again
through generated code (`managed-suite-gencode.log`). `--gen-code-minimize` adds
a third run with `GEN_CODE_MINIMIZE` (`managed-suite-gencode-minimize.log`), which
uses the smaller generated code and the native C# function wrapper helpers. CI,
`sanitize.sh` and `coverage.sh` use all three modes. `--gen-assembly` also
compiles each generated-code pass's wrappers into an assembly of their own,
`XLuaGen.dll`, next to `XLua.Mini.dll` and `XLuaUnitTest.exe`, the layout of
xLua in its own asmdef, and runs the suite through
`Test/Native/GenAssemblyLauncher.cs` (`managed-suite-gen-assembly*.log`). The
compile fails if generated code uses anything of xLua's that is not public.
The launcher then checks that the bindings were registered from that assembly
through `XLua.Binding`: a configured delegate, a delegate type no configuration
lists (found by signature) and a class wrapper. `--source-gen` builds the
source generator (`Tools/build_source_generator.py`, see below), compiles
`XLuaUnitTest` again with the .NET SDK's csc and `-analyzer`, against the Mono
assemblies `mcs` uses and at C# 12 (Mono's own csc, Roslyn 3.9, cannot load a
generator built against 4.3.1; `Test/Native/roslyn_fixtures.py` rewrites the
`in`/`ref readonly` fixtures for Roslyn, as for `coverage_cs_dotnet.py`), and
runs the suite on Mono through `Test/Native/SourceGenLauncher.cs`
(`managed-suite-source-gen.log`). `Test/Native/SourceGenConfig.cs` holds the
suite's configuration lists in the `[assembly: ...]` form. The generator
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
(`XLUA_SOURCE_GEN_STRUCT_ALLOC`). The run
fails on a generator warning or error and on analyzer load warnings (CS8032,
CS8784, CS8785, CS9057). With `--gen-code` it also compares the binding
surfaces, the bridge sets and the GCOptimize types: `XLuaGenerate` writes the
legacy generator's member model, bridge groups and pushed, packed and
table-read types to `legacy-surface.json` (`XLUA_GEN_MANIFEST`),
and `Test/Native/surface_diff.py` compares it with the source generator's
`XLua.Manifest.g.cs` (constructor, overload and indexer order as is, other
lists as sets; every delegate type with the same `SignatureKey`, every
interface bridge with the same members, every GCOptimize type with the same
kind, size, fields and float groups; `surface-diff.log`); any difference
fails the run. `--coverage` runs every
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
bridge hardening defines (`XLUA_CHECK_OBJECTS`, `XLUA_CHECK_STRUCT_ACCESSORS`,
`XLUA_RESTRICT_GETMETATABLE`, `XLUA_CHECK_OBJECT_CACHE`,
`XLUA_LOCK_XLUA_TABLE`, `XLUA_RESTRICT_PRIVATE_ACCESS`,
`XLUA_NO_LOAD_ASSEMBLY`) have one case each in `luaBridgeHardening.lua`,
with forged userdata from `BridgeHardening.Natives`
(`xLuaTest/LuaCallCS/BridgeHardeningForLuaCallCS.cs`); `Defines` there
reports how the suite was built. Run the suite once without them and once
with each, or all together. The
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

The suite also runs against a LuaJIT plugin built with `-DUINT_ESPECIALLY=ON`.
There a C# `ulong` is a separate unsigned userdata kind that prints with a `U`
suffix, and a `ulong` returned to C# as `object` stays a `ulong`. On every other
build it shares the signed representation (`UInt64.MaxValue` prints `-1`).
`luaunit_compat.lua` sets `DISTINCT_UINT64` for that build, and
`ULONG_TOSTRING(signed, unsigned)` returns the text a `ulong` prints on the
running build. CI does not build this variant. To run it on Linux:

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
types configured by a referenced assembly, XLSG010), and that the generator
references Roslyn 4.3 at most. NuGet restores from nuget.org only, in locked mode against each
project's `packages.lock.json`, into `Tools/.cache/nuget` (or
`$XLUA_TOOLS_CACHE/nuget`); `--update-lock` rewrites the lock files.
`--install <folder>` copies the DLL into a Unity project with its
`RoslynAnalyzer` `.meta`.

`Test/Native/signature_check.py --output <empty dir>` builds the signing tools,
creates a key, signs a script, and checks that `SignatureLoader` accepts it
and rejects tampered, truncated and legacy SHA-1 files.

`Test/Native/luacats_check.py --plugin <libxlua> --output <dir>` builds
`XLuaGenerate.exe`, writes LuaCATS definitions for `Test/LuaCATS/Sample.cs` and
compares them with `Test/LuaCATS/expected` and the `.luarc.json` beside them
with `expected-luarc.json` (`--update` rewrites them). It checks strict mode
against `expected-strict`, the file suffix, the `.luarc.json` ownership rules,
Clear and Generate, byte-identical output on a second run, under the tr-TR
culture and from CRLF sources (`Test/LuaCATS/CultureLauncher.cs` sets tr-TR
and runs the generator in its process, because Mono on macOS ignores `LANG`
and `LC_ALL`), and `XLuaGenerate --validate` with
`Test/LuaCATS/SetupSample.cs`, a missing plugin, a stub plugin with another
bridge version and a project tree with old xLua Lua modules in `Resources`
folders (`--resources-root`): a shipped `perf/memory.lua.txt`
(`Test/LuaCATS/stale-resources`, also with CRLF and a BOM), edited
`xlua/util` and `perf/profiler` copies, and files that must not be reported
(a lone `.meta`, other modules, `Samples~` and hidden folders, folders not
named `Resources`). It also generates definitions for `XLuaUnitTest.exe`. With
`--download-luals` (Linux or macOS, lua-language-server 3.19.1, checked by SHA-256) or
`--luals <executable>`, it runs lua-language-server with the generated
`.luarc.json` over `Test/LuaCATS/good.lua` and `good_strict.lua`, which must
have no diagnostics, and `bad.lua` and `bad_strict.lua`, whose diagnostics must
match their `-- expect:` comments. Use a plugin built with Lua 5.3 or later
(pass `--lua-version` unless it is 5.5), or LuaJIT with `--luajit`.

`Test/Native/hotfix_check.py --plugin <libxlua> --output <dir>` checks hotfix
injection end to end. It builds the tools, compiles `Test/Native/HotfixFixture.cs`
with the xLua sources and the hotfix bridges that `XLuaGenerate` writes, injects
it with `XLuaHotfixInject` and runs it under `mono --debug`. The fixture covers
instance and static methods, a constructor, a property, `ref`/`out` parameters,
a struct, a generic class, `base(self)`, a type selected by a `[Hotfix]` list,
an `IntKey` type, and constructors whose branches cross the short-branch limits
once the hotfix is injected before each `ret`. mcs writes only long branches,
so `Test/Native/ShortBranches.cs` first shortens those constructors as Roslyn
would, and after injection the run checks that every short branch still fits
and that the ones just past the limits became long. It also calls
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
before 2026-10-18). A last case injects two
assemblies as the Editor does when several have hotfix types: the fixture,
which holds xLua, and `HotfixFixture.Second.dll`
(`Test/Native/HotfixFixtureSecond.cs`), each with its own id map, merged with
`XLuaHotfixInject --merge-id-maps`. The second assembly's `IntKey` type must
fall back to field mode with a warning, and `xlua.util.auto_id_map()` must
patch both assemblies by name. `Tools/build_tools.py` downloads
Mono.Cecil from nuget.org once into the gitignored `Tools/.cache`; set
`XLUA_TOOLS_CACHE` to use another directory (for a read-only checkout).
`--cecil-dir <folder>` builds and runs the injector with the Cecil of Unity's
`com.unity.nuget.mono-cecil` 1.11.5/1.11.6 package instead (see
[hotfix.md](../docs/research/hotfix.md#unitys-cecil-package-spike-2026-10-18)).
`--define NAME` works as in `run_managed.py`. For example,
`--define XLUA_LOCK_METATABLES` checks `xlua.hotfix` and `base()` with locked
metatables.

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
the `xlua_gc` checks. `check_exports.py` also fails when `build/xlua_exports.txt`, the export
list of `-DXLUA_EXPORTS=managed`, no longer matches `LuaDLL.cs`;
`python3 Test/Native/managed_imports.py --write` regenerates it.
`native_regression.py` also calls exports that `LuaDLL.cs` does not import
(`check_exported_helpers`, `print_top`), so it needs an `XLUA_EXPORTS=all`
library.

`native_regression.py`'s `core_state` check drives the core layer
(`build/xlua_core.c`) from C: `xlua_core_newstate` with limits too small for
a state, refusals that `pcall` catches, a limit below the current use,
overdrafts from C callbacks (with a recorded error, nested under a raw
`lua_callk`, with the limit removed meanwhile, and the collection that clears
garbage left over the limit), the error handler's 16 KB reserve, and equal and
different hash seeds. `core_host_oom` makes `realloc` fail under an
address-space limit (Linux without ASan only; it prints a skip elsewhere).
The managed side is `CaseLuaOptions*` in `luaCallCsCoverage.lua`
(`xLuaTest/LuaCallCS/LuaOptionsForLuaCallCS.cs`), which runs through
`LuaEnv(LuaOptions)` in every binding mode; on Lua 5.3 and LuaJIT it checks
the `NotSupportedException` paths.

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
the table. The suite's own harness (luaunit) needs `io`, `os`, `string`,
`table` and `math`, and many cases `coroutine` and `debug`, so only `utf8` (and
LuaJIT's `bit` and `ffi`) can be left out for a managed run. `sanitize.sh`
passes `XLUA_LEAK_CHECKER`, `XLUA_UINT64` and `XLUA_LIBS` from its environment
to CMake and sets the expectations; it builds LuaJIT with the uint64 table.

`python3 Test/Native/plugin_variants_check.py --output <dir>` compiles the
selection rules of the native plugin variant selector
(`Assets/XLua/Src/Editor/PluginVariants.cs`, without UnityEditor) with `mcs`
and runs them with Mono: path parsing of `xlua-variants/<name>/`, the order
`XLUA_PLUGIN_<NAME>`, `XLUA_CORE_ONLY`, Development Build, default, and groups
without a default plugin. The Unity side compiles in `editor_compile_check.py`;
no Unity build has run it yet.

`python3 Test/Native/check_pinvoke_abi.py` needs no library: it compares every
`DllImport` in `LuaDLL.cs` and `DelegateBridge.cs` with the C definition (bridge
sources and the Lua 5.3.6, 5.4.9 and 5.5.1 headers), reducing each parameter and
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

Three xLua patches change what the suites see, and the script adjusts for them
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
| nogc64 | `-DLUAJIT_DISABLE_GC64` (x86_64 only) | `make_win64_luajit.bat` |

Without `--modes` it runs every mode the host can build: `nogc64` needs
x86_64 Linux (macOS requires GC64, ARM64 has no other mode), and `nx` does
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

`build/luajit-2.1` carries no source patch (see build/LUA_PATCHES.md), so
the run tests upstream LuaJIT with our build options.

Results (2026-10-18), runner tests passed / standalone files passed, plus
known failures:

| Platform | default | assert | nojit | nx | nogc64 |
|---|---|---|---|---|---|
| macOS 27 arm64, Apple clang 21 | 510 / 60 + 2 | 510 / 60 + 2 | 388 / 59 + 2 | 306 / 40 | not available |
| Linux arm64, `xlua-analysis:26.04`, GCC 15.2 | 510 / 61 + 1 | 510 / 61 + 1 | 388 / 60 + 1 | not available | not available |
| Linux x86_64 under qemu-user 10.2.1 in that container, `x86_64-linux-gnu-gcc` 15.2 | 510 / 62 | 510 / 62 | 388 / 61 | not valid | not valid |
| Linux x86_64, GitHub `ubuntu-26.04` runner (`analysis.yml` `luajit-tests`, run 39073496732, 2026-10-18) | 510 / 62 | 510 / 62 | 388 / 61 | 306 / 40 | 510 / 62 |

Every run takes a few seconds (about 7 s under qemu). The x86_64 row is a
functional check only. qemu-user, like Rosetta in OrbStack's Linux VM, gives
the guest the host's arm64 addresses (`malloc` blocks above 2^47) and ignores
`MAP_32BIT`, so `nogc64` cannot create a state there and `nx` crashes. Under
Rosetta the assert build also caught `lj_mem_realloc` returning
`0xffff...`, so the Rosetta runs were discarded. The default, assert and
nojit modes use LuaJIT's allocator, which places its blocks itself, and ran. On a real x86_64 machine (the CI row) all five modes pass with no
known failure, so `nx` and `nogc64` work where they are used.
Real x86_64 results, including `nogc64` and `nx`, come from the
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
--gen-code --gen-code-minimize`, which compiles every assembly with
`mcs -debug` and runs each Mono process under Mono's coverage profiler
(`mono --profile=coverage`), then runs `XLuaGenerate --luacats` on the test
assembly the same way. The profiler lists only the methods that were
compiled, so `Test/Native/CoverageLines.cs` reads each `.mdb` (with
`Mono.CompilerServices.SymbolWriter`) and the methods that never ran count as
uncovered. A line is covered when one of its sequence points ran. The
profiler and the symbol reader ship with Mono; the script downloads nothing
beyond the Mono.Cecil package that `run_managed.py --gen-code` already
fetches for the tools. The whole run takes about 20 seconds.

The reports go to `<dir>/coverage`:

- `reflection.txt`, `gencode.txt`, `gencode-minimize.txt`: one suite run each;
- `suite.txt`: the three suite runs together;
- `generator.txt`: the three `XLuaGenerate` runs (both wrapper sets and LuaCATS);
- `combined.txt`: all six processes, and `summary.json` with every number;
- `annotated/`: each source with its hit count per line, `#####` for a line
  that never ran;
- `methods.txt`: every method with its missed lines, most missed first;
  `uncovered-methods.txt`: the methods that never ran.

Files are grouped as runtime (`Assets/XLua/Src` outside `Editor`), editor
(`Assets/XLua/Src/Editor`) and generated: the wrappers `XLuaGenerate` wrote
from the templates, which shows how much of the template output the suite
runs. Test code (`Test/`, `General/Src`) is excluded. `--report-only`
rebuilds the reports from an earlier run; `--define NAME` is passed to
`run_managed.py`.

What it does not measure:

- **Branches.** Mono's profiler counts sequence points only; see
  [branch coverage on .NET](#branch-coverage-on-net) below.
- **Code that is not compiled.** Regions under symbols that `run_managed.py`
  does not define (`HOTFIX_ENABLE`, `THREAD_SAFE`, `UNITY_*`,
  `XLUA_LOCK_METATABLES` and so on) are not in the totals, and neither are the
  Unity-only paths of `Generator.cs`.
- **The other managed checks.** Only the suite and `XLuaGenerate` are
  profiled. `signature_check.py` (`SignatureLoader.cs`), `hotfix_check.py`
  (the hotfix runtime) and `luacats_check.py --validate` (`SetupValidator.cs`)
  test those files, but they show 0% here.

### Branch coverage on .NET

`Test/Native/coverage_cs_dotnet.py --plugin <libxlua> --output <empty dir>`
runs the same suite in the same three modes, and the same three
`XLuaGenerate` runs, on .NET under
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
  own target framework (`--framework` to change it). `XLuaGenerate` gets its
  templates inline, since the `.resx` file references are in gb2312, which
  .NET's resource compiler cannot read.
- **P/Invoke:** `LuaDLL` imports `xlua`, which .NET finds as `libxlua.dylib`
  or `libxlua.so` next to the assembly, as Mono does.
- **No Unity stubs:** `XLUA_GENERAL` compiles out every `UnityEngine` use.
- **Test runner:** `XLuaUnitTest` and `XLuaUnitTestGenCode` run as console
  apps from `<dir>/General`; `XLuaGenerate` writes the wrappers for .NET's
  base library. The script fails unless both luaunit modes pass all
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

These cover xLua's own C files (`build/xlua_core.c`, `build/xlua.c`,
`build/i64lib.c`, `build/memory_leak_checker.c`); the vendored Lua, LuaJIT and LuaSocket code
is not analyzed. Each script takes an empty output directory and runs on Linux
with Clang. `Test/Native/analyze.sh` runs clang-tidy (`build/.clang-tidy`) and
`clang --analyze` for Lua 5.3.6, 5.4.9, 5.5.1 and LuaJIT, and fails on any
finding; suppress an intentional pattern with `NOLINT(check): reason` on the
line. `Test/Native/coverage.sh` builds with `-DXLUA_COVERAGE=ON`, runs the native
checks and the managed suite, and prints llvm-cov line, function and branch
coverage per Lua version (it needs Mono like `run_managed.py`); `branches.txt`
in each version's directory lists every branch, an untaken side as `True: 0`
or `False: 0`. With all three managed modes, on Lua 5.3.6, 5.4.9 and 5.5.1
(2026-10-17, Linux arm64):

| File | Lines | Functions | Branches |
|---|---|---|---|
| `xlua.c` | 100% | 100% | 100% (380 on 5.5.1) |
| `i64lib.c` | 100% | 100% | 100% |
| `memory_leak_checker.c` | 100% | 100% | 100% on 5.5.1, 92% on 5.3.6, 88% on 5.4.9 |

After the `xlua_core.c` split and `LuaOptions` (milestones 1 and 2 of
[core-luascript.md](../docs/design/core-luascript.md), 2026-10-18, Linux
arm64 container, with `XLUA_APICHECK`): `xlua_core.c` and `xlua.c` are at
100% of lines, functions and branches on Lua 5.3.6, 5.4.9 and 5.5.1
(`xlua_core.c` 64, 66 and 78 branches; `xlua.c` 320). `xlua_core.h` reports
all 6 branches taken, but 70% of lines: llvm-cov counts every
translation unit's copy of its inline functions, and `xlua_core.c` compiles
`push_helper`, `xlua_core_enter` and `xlua_core_leave` without calling them;
the copies in `xlua.c` are at 100%.

After the integer conversion change (bridge audit C4), the same run on macOS
27 arm64 with Apple clang (2026-10-17) keeps `xlua.c` at 100% of lines,
functions and branches on all three versions (410 branches on 5.3.6 and
5.4.9, 424 on 5.5.1). There `i64lib.c` shows one untaken branch, the
`snprintf` in `uint64_tostring`, which macOS's fortified `snprintf` macro
adds; it is not in the source.

Before the 2026-10-17 branch work, `xlua.c` was at 92% of lines, 86% of
functions and 60% of branches on Lua 5.5.1. Most of the branch tests are in
`native_regression.py` (struct helpers and accessors, indexer closures, path
access, the C# function wrappers, `lua_setfenv`, `xlua_touint`, the
integer rule: `xlua_tointeger64`, `xlua_isinteger_number`, `xlua_pushargerror`,
and the checked accessors and cache lookup of `XLUA_CHECK_STRUCT_ACCESSORS`
and `XLUA_CHECK_OBJECT_CACHE`); the rest
are managed cases in `luaCallCs.lua`. The leak checker's untaken branches on
5.3.6 and 5.4.9 are checks inside the vendored Lua macros it uses
(`setclLvalue`, `getnodekey`). `coverage.sh` and `sanitize.sh` build with
Lua's C API checks (`-DXLUA_APICHECK=ON`, `LUA_USE_APICHECK` with `assert`
kept on), like the fuzz targets, and `sanitize.sh` adds
`-fsanitize=float-cast-overflow` under GCC, whose `undefined` group lacks it.
The API check inside `api_incr_top` adds one more untaken branch to the leak
checker (2026-10-17, Linux arm64: 2 of 46 on 5.5.1, 3 of 46 on 5.3.6, 4 of 48
on 5.4.9); `xlua.c` stays at 100% of branches (390, 390 and 404).
`Test/Fuzz/run.sh
<seconds>` builds four libFuzzer targets with ASan and UBSan: Lua chunks loaded
through `xluaL_loadbuffer` in a sandbox env that also exposes the native path,
global, type id and `uint` helpers; the `uint64` library and the C helpers
that marshal `long`, `ulong`, `uint` and `int`, with the integer rule of
`xlua_tointeger64`; struct pack/unpack (up to
`float6` and `decimal`), accessors given values of every Lua type (setters
accept only numbers and numeric strings, and integer fields only those with
an integer value) and
arguments that are not structs (with the tag-checking accessors of
`xlua_check_struct_accessors` when the last input byte is odd, which also
refuse a struct header without the tag), and the type id helpers; and dotted-path,
single-key and global table access, also on values that are not tables. The Lua
chunk target also calls the memory snapshot walk from the fuzzed chunk
(`snapshot()`) and after it, and checks the layout of the result. `FUZZ_OUT` and
`LUA_VERSION` select the output directory and runtime; `LUA_VERSION=luajit` (or
`luajit-uint`, built with `UINT_ESPECIALLY`) runs only the `uint64` target, which
there also checks the LuaJIT-only helpers and `..` on the int64 userdata. A crash leaves a
reproducer in `$FUZZ_OUT/<target>/`.

`Test/Native/check_webgl_exports.py` does the import check for the Unity WebGL
plugin. It reads `llvm-nm --defined-only` output for the object file that
Emscripten builds from `Assets/Plugins/WebGL/xlua_webgl.cpp`; see the WebGL
section of `build/README.md`.

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

## Unity players and upstream examples

- [Mobile smoke checks](MobileSmoke/README.md): runtime initialization, GC,
  coroutines, close semantics and disposal in Unity/IL2CPP, plus the integer
  rule, Lua functions cast to C# delegates (generated static bridges with
  `prepare.py --gen`) and the C# feature defines. Last run 2026-10-18 on main
  at 227ed4c: Android ARM64 APKs from Unity 6000.3.21f1 and 2021.3.45f2 on an
  Android 16 emulator with 16 KB pages, and a Unity 6000.6.4f1 WebGL player in
  headless Chrome, each with and without generated code and with all four
  defines; every player passed.
- [Upstream Android examples](Examples/README.md): original C#↔Lua tutorial
  scenes, Unity update callbacks and UI events with generated IL2CPP bridges.
- [Artifact CI](../.github/CI.md): manual native builds and test artifacts with
  no release creation. The real-editor matrix covers Unity 2021.3 LTS, 2022.3
  LTS, 6.0 LTS, 6.3 LTS, and the current 6.6 line when license secrets are
  configured. Unity device examples are run locally, outside CI.

## Unity editor tests

`Test/UnityMatrix/prepare.py --editor-tools` and `editor_tests.py run` (see
[.github/CI.md](../.github/CI.md)) run the EditMode tests with generated code
and again after Clear Generated Code, where xLua emits wrappers at run time.
`XluaGeneratedCodeTests.LuaIntegerArgumentsFollowLuasRule` checks the integer
rule of [MIGRATING.md](../docs/MIGRATING.md) through those wrappers: a
constructor, a static method with an `out` parameter, an instance field and
a field of a `[GCOptimize]` struct. The LuaLS step fails when
lua-language-server writes no report; before 2026-10-17 a binary that did not
start (such as a mise shim with no version set) passed `good.lua`.

`XluaSmokeTests.FeatureDefinesInTheEditor` checks the C# feature defines as the
Editor sees them; `prepare.py --define NAME` adds each to `Assets/csc.rsp`.
Without `Assets/XLua/Resources` (prepare.py copies only `Assets/XLua/Src`),
`require "xlua.util"` and `require "perf.profiler"` load the embedded modules.
With `XLUA_NO_UTIL` the first fails, `XLUA_NO_SIGNATURE_LOADER` removes
`SignatureLoader`, and the Editor keeps the `template` library and
`perf.profiler` under `XLUA_NO_TEMPLATE` and `XLUA_NO_PROFILER`.

`Test/UnityMatrix/hotfix_intkey.py` checks IntKey hotfix with two assemblies,
which the suite does not cover. It builds a project with `prepare.py
--editor-tools --hotfix`, the injector from `Tools/build_tools.py` and the
fixture in `Test/UnityMatrix/HotfixIntKey`: `HotfixIntKeyLocal` in
Assembly-CSharp, `Game.Shop` in an asmdef assembly `Game`, and an IntKey
`[Hotfix]` list for both under an Editor folder. Then it runs Generate Code,
XLua/Validate Setup (it must report no problem) and `HotfixIntKeyTests`. That
test runs Hotfix Inject In Editor, which must warn "IntKey falls back to field
mode in Game.dll for 1 type: Game.Shop" and write
`hotfix_id_map_Assembly-CSharp`, `hotfix_id_map_Game` and the merged
`hotfix_id_map`. Then it enters play mode, where the domain reload loads the
injected assemblies, and checks that `auto_id_map()` and `xlua.hotfix` patch
and restore both types: `HotfixIntKeyLocal` through its id, `Game.Shop`
through its `__Hotfix` field. Run `python3 Tools/build_tools.py` first.

Last local run: 2026-10-18, macOS ARM64, Lua 5.5.1 plugin and tools built
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

2022.3 and 6000.0 were not run this time. Players (where `XLUA_NO_TEMPLATE`
and `XLUA_NO_PROFILER` take effect, and build-time injection), the Linux
editors and the Windows editors are not covered locally.

### Compile check without Unity

`Test/Native/editor_compile_check.py --output <dir>` compiles the xLua C#
against the reference assemblies of installed editors, without a licence and
without starting Unity (it only reads the editor's files, so it can run while
a phone benchmark holds adb). It splits the sources as Unity does:
`Assembly-CSharp` (`Assets/XLua/Src` without `Editor/`) against .NET Standard
2.1 and the UnityEngine modules, and `Assembly-CSharp-Editor` against
`unity-4.8-api`, UnityEngine, UnityEditor and `Assembly-CSharp`. The `UNITY_*`
version defines follow each editor (`UNITY_6000_0_OR_NEWER` and so on).

`--list` prints the 48 configurations and their defines:

- **Editor** (both assemblies): the macOS, Windows and Linux editor defines;
  with `HOTFIX_ENABLE`, with the thirteen feature defines (`XLUA_NO_SIGNATURE_LOADER`,
  `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL`, `XLUA_NO_PROFILER`,
  `XLUA_NO_STREAMING_ASSETS_LOADER`, `XLUA_LOCK_METATABLES` and the bridge
  hardening defines `XLUA_CHECK_OBJECTS`, `XLUA_CHECK_STRUCT_ACCESSORS`,
  `XLUA_RESTRICT_GETMETATABLE`, `XLUA_CHECK_OBJECT_CACHE`,
  `XLUA_LOCK_XLUA_TABLE`, `XLUA_RESTRICT_PRIVATE_ACCESS`,
  `XLUA_NO_LOAD_ASSEMBLY`), `THREAD_SAFE`,
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
- **Player** (`Assembly-CSharp` only, no UNITY_EDITOR and no UnityEditor
  assemblies): Standalone macOS Mono and Windows IL2CPP, Android Mono, IL2CPP and
  `XLUA_ANDROID_SOURCE_PLUGIN`, iOS, tvOS, WebGL, Switch and UWP IL2CPP; with
  `HOTFIX_ENABLE`; Android IL2CPP with each feature define, `GEN_CODE_MINIMIZE`,
  `THREAD_SAFE`, `DEVELOPMENT_BUILD`, all feature defines and all of them
  together, `XLUA_MEMORY_DIAGNOSTICS` with `XLUA_NO_UTIL`, `XLUA_NO_PROFILER`,
  `SINGLE_ENV` and `THREAD_SAFE`; iOS with the feature defines and hotfix; `NATIVE_LUA_PUSHSTRING`,
  `SINGLE_ENV`, `GENERIC_SHARING` and `NOT_GEN_WARNING`.

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
2021.3 --output <dir> [--hotfix]` covers the opt-in asmdef layout instead
(the asmdefs in `Assets/XLua/Asmdef~/Src`, as if copied into `Assets/XLua/Src`): `XLua`
(for the Editor and, as `XLua.Player.dll`, for an Android IL2CPP player),
`XLua.Editor`, `Assembly-CSharp` from the examples and tutorials and
`Assembly-CSharp-Editor` from their `Editor` folders, each with only the
references Unity gives it (the editor-only ones against `unity-4.8-api`, the
others against .NET Standard 2.1), with the .NET SDK's csc at C# 9. It also
checks the asmdef files (names, platforms, references, `autoReferenced`) and
that no asmdef is under `Assets/XLua/Src`, where it would change the default
layout. A
use of an xLua `internal` member from outside `Assets/XLua/Src` fails it.
`--source-gen` also runs the source generator in the four assemblies, as Unity
does next to `XLua.asmdef` (a generator warning or error fails the run), and
requires `XLua.dll` to carry the pushers of xLua's UnityEngine GCOptimize
defaults.
`07_AsyncTest` is left out (it needs the uGUI package). It passes against
2021.3.45f2, 2022.3.62f3, 6000.3.26f1 and 6000.6.6f1. `Test/UnityMatrix/prepare.py
--asmdef` copies the asmdefs into the test project for an editor run in that
layout; without it the editor suite stays in the Assets layout.

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
time, the editor path), `gencode` (wrappers from `XLuaGenerate`, as
`run_managed.py --gen-code` builds them) and `gencode-min`
(`GEN_CODE_MINIMIZE`). The default is `reflection,emit,gencode`. `[GCOptimize]`
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
references every iOS import. `host --test` holds the `--lock` directory while it
runs the suites.

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
the `make_ios_*.sh` scripts (`plugin_lua55/Plugins/...`). The code generation
variants also need the macOS arm64 Editor plugin in `plugin_lua55/Plugins/arm64`.
Each variant is an empty project with `SizeProbe.cs` (copied as
`Assets/XluaSmoke.cs`): the baseline does not use xLua; the other variants create
a `LuaEnv`, run a chunk, call a C# static method from Lua and a Lua function from
C#. They get `Assets/XLua/{Src,Resources,Editor}` (`Resources` only for upstream
and for commits before 2026-10-17, when it was removed) and the plugins, not the
examples. A `+gen` variant adds `SizeGenConfig.cs` (the probe, `Vector3`,
`GameObject`, `Transform`, `Debug` and two delegates) and runs XLua/Generate Code
first. A `+nosa` variant compiles with `XLUA_NO_STREAMING_ASSETS_LOADER`;
`+nosig`, `+notemplate`, `+noutil` and `+noprofiler` add `XLUA_NO_SIGNATURE_LOADER`,
`XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` and `XLUA_NO_PROFILER` (not in `CASES`; pass
them with `--variants`). A
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
