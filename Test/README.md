# Test coverage

The repository has a bridge unit suite, performance examples, native regression
checks, and Unity player checks. It does not yet have an exhaustive test matrix
for every supported Unity version, platform, backend, or optional extension.

## General C# bridge suite

`Test/UnitTest` exercises Lua-to-C# calls, the explicit reflection cases, and
C#-to-Lua calls. The default Lua runner enables three suites containing 275
tests and runs them in both coroutine and normal modes. Its generated-code and
TDR-specific suites are currently commented out upstream; a pass does not
mean those optional suites ran.

With Mono 6.14.1 and `mcs` installed (`brew install mono` on macOS and Linux),
first build a host-native plugin, then run:

```sh
python3 Test/Native/run_managed.py --plugin /absolute/path/libxlua.so --output /absolute/path/test-output
```

The runner stops unless `mono --version` reports 6.14.1; pass `--mono-version any`
to skip that check. On macOS use a rebuilt `libxlua.dylib`. `--mono` and `--compiler` accept explicit
tool paths; an `.exe` compiler runs through Mono. The runner compiles the
existing project source lists, copies test data to the output directory, saves
`managed-suite.log`, and fails unless both runs report 276 passing tests.

For the original Unity unit scene, copy `Test/UnitTest/xLuaTest` and
`Test/UnitTest/StreamingAssets` into a Unity project's `Assets` folder and run
`xLuaTest/main.unity`. This is a manual path, not the current CI entry point;
the Android test code still uses a legacy `/sdcard/luatest/` result path.

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
```

These check the managed import symbols, GC bridge opcodes and memory profiler
graph traversal. Run them against a library built for the host architecture.

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
