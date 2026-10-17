# Test coverage

The repository has a bridge unit suite, performance examples, native regression
checks, and Unity player checks. It does not yet have an exhaustive test matrix
for every supported Unity version, platform, backend, or optional extension.

## General C# bridge suite

`Test/UnitTest` exercises Lua-to-C# calls, the explicit reflection cases, `in` and `ref readonly` parameters,
regression tests for upstream issues listed in `UPSTREAM.md`
(`StreamingAssets/luaCallCsUpstream.lua`) and C#-to-Lua calls. The Lua
runner (`StreamingAssets/main.lua`) enables five
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
`sanitize.sh` and `coverage.sh` use all three modes. `--coverage` runs every
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
runner copies no Lua modules from `Assets`: `require("xlua.util")` and
`require("perf.profiler")` load the copies embedded in the xLua assembly, so a
pass also shows that they need no file. `CaseStringPackVersionedMessage` runs
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
`had injected!` and leave the file unchanged. `Tools/build_tools.py` downloads
Mono.Cecil from nuget.org once into the gitignored `Tools/.cache`; set
`XLUA_TOOLS_CACHE` to use another directory (for a read-only checkout).
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
below. Run them against a library built for the host architecture.

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

LuaJIT is not covered: lua.org's suites target PUC Lua. LuaJIT's repository
has no test directory. [LuaJIT-test-cleanup](https://github.com/LuaJIT/LuaJIT-test-cleanup)
holds Mike Pall's tests (last commit 2016-10-19, no licence in the GitHub
metadata); [tarantool/luajit](https://github.com/tarantool/luajit) carries
an adapted copy as `test/LuaJIT-tests` next to patched PUC-Rio 5.1 tests, and
[openresty/luajit2-test-suite](https://github.com/openresty/luajit2-test-suite)
(updated 2026-06) targets OpenResty's fork. None of them was downloaded. A
pinned LuaJIT-tests run against `build/luajit-2.1` is worth adding later,
after checking its licence; it is in the backlog.

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
`mise which dotnet`), else `dotnet` on PATH; any SDK 8.0 or later works, and
the script prints the one it chose (`XLUA_DOTNET_SDK:`). It downloads
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

On macOS 27 arm64 with a Lua 5.5.1 plugin built from main at 4b8d6d7
(2026-10-17): lines and methods from `coverage_cs.py` (Mono 6.14.1),
branches from `coverage_cs_dotnet.py` (.NET SDK 10.0.401, coverlet 10.1.0;
SDK 10.0.402 from `mise.toml` gave the same numbers on 2026-10-18).
The suite passes 349 tests in both luaunit modes in every mode on both
runtimes. Two runs gave identical numbers.

| Runtime, `Assets/XLua/Src` | Lines (Mono) | Methods (Mono) | Branches (.NET) | Lines (.NET) |
|---|---|---|---|---|
| reflection | 64.1% (4717/7358) | 66.7% (369/553) | 56.0% (1529/2729) | 65.5% (5070/7735) |
| gencode | 50.9% (3755/7373) | 64.5% (356/552) | 41.0% (1120/2729) | 53.0% (4100/7735) |
| gencode-minimize | 51.1% (3791/7413) | 64.8% (361/557) | 41.1% (1124/2733) | 53.2% (4136/7775) |
| suite, all three | **70.3%** (5229/7433) | 76.2% (426/559) | **62.1%** (1702/2741) | 71.9% (5603/7795) |
| combined, with the generator runs | 71.4% (5306/7433) | 77.8% (435/559) | 63.1% (1729/2741) | 72.9% (5681/7795) |

The totals differ per mode because each mode compiles other `#if` regions.
In the generator runs:

| Editor, generator runs | Lines (Mono) | Methods (Mono) | Branches (.NET) |
|---|---|---|---|
| `Generator.cs` | 61.2% (748/1222) | 166/201 | 47.2% (376/796) |
| `LuaCATSGenerator.cs` | 80.9% (601/743) | 106/122 | 69.7% (432/620) |
| `SetupValidator.cs` | 0% (0/226), tested by `luacats_check.py` | 0/29 | 0% (0/128) |

The suite runs 65.4% of the generated wrapper lines (7108/10868) and 71.2%
of the minimized ones (6273/8808), and 23.2% (601/2586) and 44.0%
(574/1304) of their branches.

Per runtime file, suite (the three modes together):

| File | Lines (Mono) | Methods (Mono) | Branches (.NET) |
|---|---|---|---|
| `CodeEmit.cs` | 86.9% (1381/1590) | 47/49 | 83.2% (494/594) |
| `CopyByValue.cs` | 96.3% (79/82) | 23/23 | 5/6 |
| `DelegateBridge.cs` | 60.9% (42/69) | 6/10 | 54.5% (12/22) |
| `EmbeddedModules.cs` | 100% (9/9) | 1/1 | 2/2 |
| `GenAttributes.cs` | 38.1% (8/21) | 2/6 | none |
| `GenericDelegateBridge.cs` | 0% (0/164) | 0/10 | 0% (0/20) |
| `InternalGlobals.cs` | 100% (24/24) | 1/1 | none |
| `LuaBase.cs` | 65.0% (39/60) | 8/10 | 50.0% (7/14) |
| `LuaDLL.cs` | 84.3% (258/306) | 28/32 | 75.4% (95/126) |
| `LuaEnv.cs` | 73.4% (193/263) | 20/30 | 58.7% (27/46) |
| `LuaException.cs` | 100% (2/2) | 1/1 | none |
| `LuaFunction.cs` | 36.5% (46/126) | 6/11 | 40.9% (9/22) |
| `LuaTable.cs` | 39.8% (82/206) | 11/23 | 37.5% (12/32) |
| `MethodWarpsCache.cs` | 82.1% (325/396) | 17/19 | 78.8% (164/208) |
| `ObjectCasters.cs` | 76.9% (397/516) | 54/70 | 58.5% (151/258) |
| `ObjectPool.cs` | 62.0% (62/100) | 8/11 | 36.4% (16/44) |
| `ObjectTranslator.cs` | 75.7% (882/1165) | 87/104 | 62.0% (264/426) |
| `ObjectTranslatorPool.cs` | 97.7% (43/44) | 6/6 | 87.5% (7/8) |
| `RawObject.cs` | 0% (0/37) | 0/12 | none |
| `SignatureLoader.cs` | 0% (0/45), tested by `signature_check.py` | 0/6 | 0% (0/14) |
| `StaticLuaCallbacks.cs` | 39.6% (318/803) | 26/37 | 24.2% (75/310) |
| `TemplateEngine/TemplateEngine.cs` | 80.7% (113/140) | 18/22 | 73.1% (19/26) |
| `TypeExtensions.cs` | 60.6% (40/66) | 13/13 | 4.5% (1/22) |
| `Utils.cs` | 73.9% (886/1199) | 43/52 | 63.2% (342/541) |

Fewest branches covered, apart from the files no suite runs
(`GenericDelegateBridge.cs`, `SignatureLoader.cs`):

- `TypeExtensions.cs`, 1 of 22: `GetFriendlyName`'s chain of type checks,
  which only the generator calls (the generator runs cover them);
- `StaticLuaCallbacks.cs`, 75 of 310: the `xlua.*` helpers and the primitive
  array fast paths listed in the backlog, and most error paths;
- `ObjectPool.cs`, 16 of 44: the out-of-range checks of the indexer, `Get`,
  `Remove` and `Replace`, and most of `Check`, the sweep that drops dead
  objects from the reverse map;
- `LuaTable.cs`, 12 of 32, and `LuaFunction.cs`, 9 of 22: the C#-side API;
- `LuaBase.cs`, 7 of 14: `Equals` never runs;
- `ObjectCasters.cs`, 151 of 258: the other alternatives of the type checks
  (`nil`, or a userdata holding a `LuaTable`, `LuaFunction` or `byte[]`) and
  the casters for the less common primitive types.

The untested areas are listed in the
[backlog](../docs/design/backlog.md#tests-and-ci).

## Static analysis, coverage and fuzzing

These cover xLua's own C files (`build/xlua.c`, `build/i64lib.c`,
`build/memory_leak_checker.c`); the vendored Lua, LuaJIT and LuaSocket code
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

After the integer conversion change (bridge audit C4), the same run on macOS
27 arm64 with Apple clang (2026-10-17) keeps `xlua.c` at 100% of lines,
functions and branches on all three versions (410 branches on 5.3.6 and
5.4.9, 424 on 5.5.1). There `i64lib.c` shows one untaken branch, the
`snprintf` in `uint64_tostring`, which macOS's fortified `snprintf` macro
adds; it is not in the source.

Before the 2026-10-17 branch work, `xlua.c` was at 92% of lines, 86% of
functions and 60% of branches on Lua 5.5.1. Most of the branch tests are in
`native_regression.py` (struct helpers and accessors, indexer closures, path
access, the C# function wrappers, `lua_setfenv`, `xlua_touint`, and the
integer rule: `xlua_tointeger64`, `xlua_isinteger_number`, `xlua_pushargerror`); the rest
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
arguments that are not structs, and the type id helpers; and dotted-path,
single-key and global table access, also on values that are not tables. `FUZZ_OUT` and
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
  coroutines, close semantics and disposal in Unity/IL2CPP.
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

Last local run: 2026-10-17, macOS ARM64, Lua 5.5.1 plugin built from main at
`dd32cdb`, with `--fast-enter-play-mode` (and `--csharp12` on 6000.6). "All
four" is `--define` with `XLUA_NO_SIGNATURE_LOADER`, `XLUA_NO_TEMPLATE`,
`XLUA_NO_UTIL` and `XLUA_NO_PROFILER`. Every launch passed, and LuaLS reported
0 diagnostics for `good.lua` and the 10 expected for `bad.lua` in each run:

| Unity | Defines | Generated | Cleared |
|---|---|---|---|
| 2021.3.45f2 | none | 31/31 | 31/31 |
| 2021.3.45f2 | all four | 31/31 | 31/31 |
| 6000.3.26f1 | none | 31/31 | 31/31 |
| 6000.3.26f1 | all four | 31/31 | 31/31 |
| 6000.6.6f1 | none | 35/35 | 35/35 |
| 6000.6.6f1 | all four | 35/35 | 35/35 |

An earlier run the same day at `f515466` (before the feature defines) passed
30/30, 30/30 and 34/34. On 2021.3.45f2 `LuaIntegerArgumentsFollowLuasRule`
fails on main before the integer rule (`38e515d`), with generated code and
with reflection (`Target(2.5)` constructs). 2022.3 and 6000.0 were not run
this time. Players, where `XLUA_NO_TEMPLATE` and `XLUA_NO_PROFILER` take
effect, the Linux editors and the Windows editors are not covered locally.

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
  struct with `[GCOptimize]` and the same struct without it (boxed).
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
variables, a compiler launcher for per-file optimisation levels, or (for
`-fvisibility=hidden`) a patched copy of `build/` in the output directory.
`python3 Test/Native/flag_sweep.py list --output DIR` prints the variants.

```sh
python3 Test/Native/flag_sweep.py android --output DIR --ndk NDK [--abis arm64-v8a,armeabi-v7a] [--variants O3,Oz,...]
python3 Test/Native/flag_sweep.py bench --output DIR --ndk NDK --rounds 5   # needs a device on adb
python3 Test/Native/flag_sweep.py report --output DIR
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
same for every variant; `report` flags any difference. `profile` records the
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
