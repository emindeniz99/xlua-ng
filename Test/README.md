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
tests. Its generated-code and TDR-specific suites are
currently commented out upstream; a pass does not mean those optional suites
ran.

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
`sanitize.sh` and `coverage.sh` use all three modes.

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
culture and from CRLF sources, and `XLuaGenerate --validate` with
`Test/LuaCATS/SetupSample.cs`, a missing plugin and a stub plugin with another
bridge version. It also generates definitions for `XLuaUnitTest.exe`. With
`--download-luals` (Linux, lua-language-server 3.19.1, checked by SHA-256) or
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

OpenHarmony has no host-architecture library, so `Test/Native/ohos_check.sh
<OpenHarmony native SDK> <output dir>` builds libxlua for each OpenHarmony ABI
and runs `Test/Native/plugin_smoke.c` (Lua core, `uint64`, struct packing,
`os.tmpname`, `io.popen`, file seeks) on Linux. The SDK's dynamic loader only
works on an OpenHarmony system, so the smoke test is linked statically from the
object files CMake compiled for `libxlua.so`. ABIs the host cannot execute run
through qemu-user. armeabi-v7a does not run under qemu-arm (a static
OpenHarmony hello world already stops with SIGILL), so CI runs x86_64 and
arm64-v8a only. Loading the `.so` itself needs a device or the emulator.

## Static analysis, coverage and fuzzing

These cover xLua's own C files (`build/xlua.c`, `build/i64lib.c`,
`build/memory_leak_checker.c`); the vendored Lua, LuaJIT and LuaSocket code
is not analyzed. Each script takes an empty output directory and runs on Linux
with Clang. `Test/Native/analyze.sh` runs clang-tidy (`build/.clang-tidy`) and
`clang --analyze` for Lua 5.3.6, 5.4.9, 5.5.1 and LuaJIT, and fails on any
finding; suppress an intentional pattern with `NOLINT(check): reason` on the
line. `Test/Native/coverage.sh` builds with `-DXLUA_COVERAGE=ON`, runs the native
checks and the managed suite, and prints llvm-cov line and function coverage
per Lua version (it needs Mono like `run_managed.py`). With all three managed
modes, `xlua.c` reaches about 92% of lines and 86% of functions on Lua 5.5.1
(2026-10-16). The functions never run are upstream's unused debug print
helpers, the `nop` accessor placeholder, and `xlua_csharp_error`, which only
the Unity build's `print` reaches; the rest are rare error paths. `Test/Fuzz/run.sh
<seconds>` builds four libFuzzer targets with ASan and UBSan: Lua chunks loaded
through `xluaL_loadbuffer` in a sandbox env, the `uint64` library, struct
pack/unpack and accessors, and dotted-path table access. `FUZZ_OUT` and
`LUA_VERSION` select the output directory and runtime; `LUA_VERSION=luajit` (or
`luajit-uint`, built with `UINT_ESPECIALLY`) runs only the `uint64` target, which
there also checks the LuaJIT-only helpers and `..` on the int64 userdata. A crash leaves a
reproducer in `$FUZZ_OUT/<target>/`.

`Test/Native/check_webgl_exports.py` does the import check for the Unity WebGL
plugin. It reads `llvm-nm --defined-only` output for the object file that
Emscripten builds from `Assets/Plugins/WebGL/xlua_webgl.cpp`; see the WebGL
section of `build/README.md`.

## Unity players and upstream examples

- [Mobile smoke checks](MobileSmoke/README.md): runtime initialization, GC,
  coroutines, close semantics and disposal in Unity/IL2CPP.
- [Upstream Android examples](Examples/README.md): original C#↔Lua tutorial
  scenes, Unity update callbacks and UI events with generated IL2CPP bridges.
- [Artifact CI](../.github/CI.md): manual native builds and test artifacts with
  no release creation. The real-editor matrix covers Unity 2021.3 LTS, 2022.3
  LTS, 6.0 LTS, 6.3 LTS, and the current 6.6 line when license secrets are
  configured. Unity device examples are run locally, outside CI.

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
C#. They get `Assets/XLua/{Src,Resources,Editor}` and the plugins, not the
examples. A `+gen` variant adds `SizeGenConfig.cs` (the probe, `Vector3`,
`GameObject`, `Transform`, `Debug` and two delegates) and runs XLua/Generate Code
first. `SmokeBuild.cs` builds them with `XLUA_SMOKE_RELEASE=1` (no Development
flag) and the managed stripping level, Android ABIs, AAB and WebGL compression
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
