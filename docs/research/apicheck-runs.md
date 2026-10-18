# Lua C API checks on every runtime and preset (2026-10-18)

The ROADMAP item "Upstream follow-ups" said that a `LUA_USE_APICHECK` build
passed the managed suite on Lua 5.5.1 (macOS) only. This note records the
runs on the other runtimes and presets. It also covers the Linux sanitizer
runs and Lua's own suites with the checks on. Earlier work:
[bridge-audit.md](bridge-audit.md) item 14 (the 5.5.1 run and its three
fixes) and [c17-setup.md](c17-setup.md) (`XLUA_APICHECK` in `sanitize.sh`,
`coverage.sh` and the fuzz targets).

Marks: [C] confirmed here, [S] secondary, [U] unconfirmed.

## Result

- No C API assertion fired on any runtime or preset. Nothing in `xlua.c`,
  `i64lib.c`, `xlua_core*.c` or the C# bridge needed a fix. [C]
- One configuration failed: the LuaJIT build with `UINT_ESPECIALLY`
  (`luajit-uint`). The cause was three wrong test expectations, not an API
  check (below). Fixed. [C]
- `XLUA_APICHECK` did nothing on LuaJIT unless `libluajit.a` had been made
  with `-DLUA_USE_APICHECK`. CMake now checks this (below). [C]

## How the checks are turned on

- PUC Lua: `LUA_USE_APICHECK` defines `luai_apicheck` (5.3, 5.4) or
  `api_check` (5.5) as `assert` (`build/lua-5.3.6/src/luaconf.h.in:718`,
  `build/lua-5.4.9/src/luaconf.h.in:737`, `build/lua-5.5.1/src/lapi.h:15-16`).
  `XLUA_APICHECK` defines it and removes `NDEBUG`
  (`build/CMakeLists.txt`, the `if (XLUA_APICHECK)` block). [C]
- LuaJIT: `lj_checkapi` is active with `LUA_USE_APICHECK` or
  `LUA_USE_ASSERT` (`build/luajit-2.1/src/lj_def.h:355-359`). It reports
  through `lj_assert_fail`, which `lj_assert.c` compiles only under the same
  condition (`build/luajit-2.1/src/lj_assert.c:9,15`). The checks are in
  `lj_api.c`, which LuaJIT's make compiles into `libluajit.a` before CMake
  runs. Defining the macro for `xlua.c` changes nothing on LuaJIT. [C]
- **Gap, fixed:** with `-DUSING_LUAJIT=ON -DXLUA_APICHECK=ON`, a
  `libluajit.a` made without the define linked and passed silently. make does
  not rebuild when only `XCFLAGS` change, so an earlier plain build in the
  tree was enough to lose the checks. Configure now runs `nm` on
  `LUAJIT_STATIC_LIB` and stops unless it finds `lj_assert_fail`. It warns
  when the library or `nm` is missing. Checked on macOS and in the Linux
container (binutils `nm`): it refuses a plain
  `libluajit.a` and accepts one made with
  `XCFLAGS="-fPIC -DLUA_USE_APICHECK"`. Builds without `XLUA_APICHECK` do not
  change. The sanitizer builds on Linux passed the check. [C]
- **Probe:** each build was misused once from Python `ctypes` in a child
  process (`lua_pushvalue(L, 100)` on an empty stack; on LuaJIT,
  `lua_rawgeti` on a number). Every APICHECK build aborted. 5.3.6 aborted in
  `index2addr` (`lapi.c:64`), 5.4.9 and 5.5.1 in `index2value` (`lapi.c:64`
  and `:62`), and LuaJIT with "LuaJIT ASSERT lj_api.c:825: lua_rawgeti:
  stack slot 1 is not a table". The probe ran on the 5.3.6, 5.4.9 and 5.5.1
  legacy builds, both LuaJIT builds (legacy and minimal) and core 5.5.1. [C]

## macOS runs

macOS 27.0.1 arm64, Apple clang 21.0.0, CMake 4.4.4, Mono 6.14.1.
`RelWithDebInfo` with `-DXLUA_APICHECK=ON` and `-DBUILD_SILICON=ON`, which
exports everything. For LuaJIT, a copy of `build/` was used, with
`make BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK"`. Main at
`35165d02`.

- **Managed suite:**
  - `run_managed.py --gen-code --gen-code-minimize --gen-assembly
    --source-gen --define XLUA_MEMORY_DIAGNOSTICS`.
  - Each run covers reflection, generated code, minimized generated code,
    both passes from their own `XLuaGen.dll`, and the source generator: six
    builds, 427 tests in each of two modes.
  - Also checked: the migration, surface, bridge and struct diffs.
- **Native checks:**
  - `check_exports.py`, `native_regression.py` and `profiler_smoke.py`
    (except on LuaJIT).
  - Core builds: `check_exports.py --preset core`, `native_regression.py`
    (the `core_*` checks) and `core_smoke.c` with
    `core_sandbox_cases.txt`.

| Configuration | Native checks | Managed suite |
|---|---|---|
| legacy, Lua 5.3.6 | pass | 6 × 427, pass |
| legacy, Lua 5.4.9 | pass | 6 × 427, pass |
| legacy, Lua 5.5.1 | pass | 6 × 427, pass |
| legacy, LuaJIT 2.1 | pass | 6 × 427, pass |
| legacy, LuaJIT with `LUA_USE_ASSERT` as well (internal assertions) | pass | 6 × 427, pass |
| legacy, LuaJIT with `UINT_ESPECIALLY` | pass | 6 × 427, pass after the test fix |
| minimal, Lua 5.5.1 (`--define XLUA_PRESET_MINIMAL`) | pass | 6 × 427, pass |
| minimal, LuaJIT | pass | 6 × 427, pass |
| core, Lua 5.4.9 | pass (with `core_smoke`) | n/a |
| core, Lua 5.5.1 | pass (with `core_smoke`) | n/a |
| legacy + `XLUA_LUASCRIPT=ON`, Lua 5.4.9 | pass (bridge and core checks) | 6 × 427, pass |
| legacy + `XLUA_LUASCRIPT=ON`, Lua 5.5.1 | pass (bridge and core checks) | 6 × 427, pass |

All the bridge hardening defines were also run together:
`XLUA_LOCK_METATABLES`, `XLUA_CHECK_OBJECTS`,
`XLUA_CHECK_STRUCT_ACCESSORS`, `XLUA_RESTRICT_GETMETATABLE`,
`XLUA_CHECK_OBJECT_CACHE`, `XLUA_LOCK_XLUA_TABLE`,
`XLUA_RESTRICT_PRIVATE_ACCESS`, `XLUA_NO_LOAD_ASSEMBLY` and
`XLUA_MEMORY_DIAGNOSTICS`. They ran with `--gen-code --gen-code-minimize` on
the legacy 5.3.6, 5.4.9, 5.5.1 and LuaJIT builds. All passed: three builds,
427 tests in each of two modes. Their cases feed forged userdata into the
error paths. [C]

**Lua's own suites:**

- `lua_tests.py --modes internal`: 5.3.6, 5.4.9 and 5.5.1 all print
  `final OK`.
  - The interpreter is built `Release`, but the API checks are still on.
    For 5.3, `ltests.h` itself does `#undef NDEBUG` and defines
    `lua_assert` as `assert`. For 5.4 and 5.5, it defines `LUAI_ASSERT`,
    and `llimits.h` then does the same (`build/lua-5.4.9/src/llimits.h:103-106`,
    `build/lua-5.5.1/src/llimits.h:102-105`).
  - Without `LUA_USE_APICHECK`, `luai_apicheck` and `api_check` fall back
    to `lua_assert`, so they are active too.
  - Each interpreter references `__assert_rtn`. [C]
- `luajit_tests.py --modes assert` (`LUA_USE_ASSERT` and
  `LUA_USE_APICHECK`): runner 510 pass and 18 skip; standalone 60 pass,
  3 skip and 2 expected failures. Prints `XLUA_LUAJIT_TESTS_PASS`. [C]

## Linux runs (ASan, UBSan and API checks)

Image `xlua-analysis:26.04` (`Test/Docker/Dockerfile`), Linux arm64 under
OrbStack, GCC 15.2. Settings: `CC=gcc`, `XLUA_WERROR=ON`, as in
`native-build.yml`. `sanitize.sh` always builds with
`-DXLUA_APICHECK=ON`. Its LuaJIT builds make `libluajit.a` with
`-DLUA_USE_APICHECK`. Each version runs `check_exports.py`,
`native_regression.py`, `profiler_smoke.py` (Lua 5.x) and `run_managed.py
--gen-code --gen-code-minimize --define XLUA_MEMORY_DIAGNOSTICS`. Each run
used a copy of the worktree without `.git`.

| Run | Result |
|---|---|
| `VERSIONS="5.3.6 5.4.9 5.5.1 luajit luajit-uint"` | the first four pass; `luajit-uint` fails 2 cases in every mode (before the fix) |
| `VERSIONS=luajit-uint`, after the fix | pass |
| `VERSIONS="core-5.4.9 core-5.5.1"` | pass: both exports lists, `native_regression.py` and `core_smoke` |
| `XLUA_PRESET=minimal VERSIONS="5.3.6 5.4.9 5.5.1 luajit"` | pass (4 of 4) |
| `XLUA_LUASCRIPT=ON VERSIONS="5.4.9 5.5.1"` | pass: the bridge checks, then the `core_*` checks |

## The `luajit-uint` failure

`luajit-uint` is LuaJIT with `-DUINT_ESPECIALLY=ON`, where a `ulong` prints
with a `U` suffix. `ULONG_TOSTRING(signed, unsigned)` adds the suffix itself
(`Test/UnitTest/StreamingAssets/luaunit_compat.lua:101-109`). [C]

- **`CasePrimitiveArrays`:** expected `"1UU"` and `"6UU"`, because the call
  passed `"1U"` and `"6U"` as the unsigned text
  (`luaCallCsCoverage.lua:342,344`; commit `81710882`). [C]
- **`CaseIntegerArgumentRule`:** `slotText()` removes the suffix from the
  result but not from the expected text
  (`luaCallCs.lua:3251-3253,3310`; commit `423e7d62`). [C]
- **Why it went unnoticed:** CI does not build this variant (Test/README.md),
  and sanitize.sh's default `VERSIONS` leave it out. [C]
- **Fix:** test-only. `ULONG_TOSTRING("1")` and `ULONG_TOSTRING("6")`, and
  `slotText(ULONG_TOSTRING(...))`. The other builds get the same text as
  before. [C]

## Not run

- Windows (MSVC `/UNDEBUG`), Android, iOS, WebGL and the other cross targets.
  The checks are a host test-build option. [U]
- ASan on macOS. Under Mono it stops with "Interceptors are not working"
  ([bridge-audit.md](bridge-audit.md) item 14). The macOS runs use the
  checks without sanitizers. [S]
- Lua 5.3.6 with `LUAC_COMPATIBLE_FORMAT`, and the minimal preset on 5.3.6
  and 5.4.9, on macOS. The minimal preset on those versions ran on Linux
  only.
- Unity editors and players. The checks are not part of any Unity build.

## Reproducing

```sh
# macOS or Linux, Lua 5.x:
cmake -S build -B out -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION=5.4.9 -DXLUA_APICHECK=ON
# LuaJIT: make the static library with the checks first (configure refuses it otherwise).
make -C build/luajit-2.1 clean
make -C build/luajit-2.1 BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK"
cmake -S build -B out-lj -DCMAKE_BUILD_TYPE=RelWithDebInfo -DUSING_LUAJIT=ON -DXLUA_APICHECK=ON
# Linux, with ASan and UBSan:
docker run --rm -v "$PWD":/src -v /abs/out:/out -w /src -e CC=gcc \
    -e VERSIONS="5.3.6 5.4.9 5.5.1 luajit luajit-uint core-5.4.9 core-5.5.1" \
    xlua-analysis:26.04 Test/Native/sanitize.sh /out
```

On macOS, add `-DBUILD_SILICON=ON` to get a `libxlua.dylib` for
`run_managed.py`.
