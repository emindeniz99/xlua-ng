# Lua vendor patch audit

This audit covers the retained PUC Lua 5.3.6 and 5.4.9 vendor patches.
It does not audit every bundled third-party library. LuaJIT carries no source
patch; see [LuaJIT test suite](#luajit-test-suite).
Commit IDs below refer to the retained Tencent/xLua Git history.

| Patch | Evidence and purpose | Current decision |
| --- | --- | --- |
| UWP process/environment guards | `4025950` (2017-08-02) explicitly says Windows Phone cannot use `getenv`, `_popen`, `_pclose`, or `system`. `1100b18` and `0fc1102` also disable the DLL loader on UWP. | Retain. Selected branches have host checks; real Windows SDK/UWP build and runtime remain unverified. |
| Android decimal point | `1100b18` (2017-09-08), titled platform compatibility changes, forces `lua_getlocaledecpoint()` to `'.'` on Android. | Retain existing parsing behavior. The commit does not identify a specific failing Android version or device. Native compilation does not prove behavior under every device locale. |
| `os.execute` removal | `62ea5f5` (2017-09-29), titled iOS 11 compatibility, removes the implementation and registration. | Retain. The original reason was iOS compatibility; its unconditional application also removes the API on desktop. Do not describe it as a complete security sandbox. |
| Configuration template | `f7a3f72` (2017-09-07) introduces `luaconf.h.in` and configurable `LUA_IDSIZE`; current CMake defaults to 120 rather than upstream 60. | Retain to preserve generated configuration and the source-description buffer size. |
| Plain `luaconf.h` in Lua 5.5.1 | `luaconf.h.in` was renamed to `luaconf.h`, with `LUA_IDSIZE` defaulting to 120 and `-DLUA_IDSIZE` as an override (CMake passes its `LUA_IDSIZE` option that way). A CMake-generated header cannot be found by the Unity WebGL build, which compiles the sources without CMake or include paths. The configured value is unchanged. | Retain for 5.5.1. Lua 5.3.6 and 5.4.9 keep the template. |
| Optional Lua 5.3 bytecode format | `3836179` (2018-09-14) introduces `LUAC_COMPATIBLE_FORMAT`. `Assets/XLua/Doc/compatible_bytecode.md` explicitly describes sharing bytecode between 32-bit and 64-bit runtimes. The patch uses a fixed 32-bit string length and removes the `size_t` header check. | Retain as opt-in for 5.3. Compiler and runtime must both enable it. It is not a promise of compatibility across Lua versions, all byte orders, or every architecture. |
| `LUA_API` with default visibility (5.3.6, 5.4.9, 5.5.1) | Added 2026-10-17. With GCC or Clang on ELF and Mach-O targets, `luaconf.h` (`luaconf.h.in` for 5.3 and 5.4) defines `LUA_API` as `__attribute__((visibility("default"))) extern`, as LuaJIT's `luaconf.h` does. Lua's own `luaconf.h` invites this change ("CHANGE them if you need to define those functions in some special way"). `-DXLUA_EXPORTS=managed` compiles with `-fvisibility=hidden`, which would otherwise hide the Lua API from the version script. Builds without `-fvisibility=hidden` are unchanged: default visibility is the default. `LUAI_FUNC` and `LUA_BUILD_AS_DLL` are untouched. | Retain while `XLUA_EXPORTS=managed` uses hidden visibility. |
| Lua 5.4 `luaconf.h.in` and 5.5 `luaconf.h` hook include | Added 2026-10-18 for `LuaOptions.HashSeed` (owner decision 11 in [core-luascript.md](../docs/design/core-luascript.md#decisions-owner-2026-10-17)) and the C budget below. Lua 5.4's `lua_newstate` takes no seed; it calls the `luai_makeseed(L)` macro, which `lstate.c` defines only when the build has not (`lstate.c:58-80` in 5.4.9). The "Local configuration" space of `luaconf.h.in` (5.4) and `luaconf.h` (5.5) includes `build/xlua_luahooks.h` when `XLUA_LUA_HOOKS` is defined; 5.5 includes it by the relative path `../../xlua_luahooks.h`, because the Unity WebGL build has no include paths. The header defines `luai_budget(L,n)` as `xlua_core_budget` for both versions, and, where 5.4's `luaconf.h.in` sets `XLUA_HOOK_MAKESEED` first, `luai_makeseed(L)` as `xlua_core_makeseed(L)` (`build/xlua_core.c`): the configured seed of a core state, otherwise upstream's mix of time and addresses (5.5's `lua_newstate` takes the seed, and its `lauxlib.c` uses a `luai_makeseed()` without arguments). CMake defines `XLUA_LUA_HOOKS` for libxlua only and `Assets/Plugins/WebGL/xlua_webgl.cpp` defines it too, so the stand-alone test interpreter and any build without it keep upstream's code. 5.3 has neither. | Retain while `HashSeed` and the C budget are supported. |
| C budget in `lstrlib.c` and `ltablib.c` (5.4.9, 5.5.1) | Added 2026-10-18 for the instruction and time limits (core-luascript.md, section 3.11). The count hook cannot stop a long C function: a backtracking pattern (`("a"):rep(1e5):find(".-.-.-.-b")`), a worst-case plain `find`, or `table.move` over a huge range of nils run for minutes. Both files define `luai_budget(L,n)` as a no-op unless the hooks header defined it, and `BUDGET_TICK` 4096. `MatchState` gets a `tick` field that `prepstate` clears; `match()` counts a step at its `init:` label and charges `BUDGET_TICK` every 4,096 steps, which is safe because `match()` already raises "pattern too complex" there; `lmemfind` takes `L` and charges the bytes it compares, 4,096 at a time; `tmove` charges every 4,096 moved elements in both loops. Since a second pass on 2026-10-18 (core-luascript.md, section 8.12), a `budget_step` macro does the same in the loops of `tinsert`, `tremove` and `tconcat` and in `partition`'s loops for `sort` (a counter that `sort` passes down `auxsort`): their length comes from `luaL_len`, which honours a script's `__len`, so `table.remove(setmetatable({}, {__len = ...}), 1)` looped up to 2^63 times over nils that allocate nothing. About 20 lines per file and version, and about 25 more in `ltablib.c` for the second pass, each marked `xlua-ng`. Results do not change: the official 5.4.9 and 5.5.1 suites (`pm.lua`, `strings.lua`, `sort.lua` included) pass on the patched trees, where the macro does nothing, and `sort.lua`, `nextvar.lua`, `pm.lua` and `strings.lua` also pass inside libxlua with an instruction limit that keeps the budget active. | Retain while the limits are supported. |
| `string.rep` of two empty strings (5.4.9) | Added 2026-10-18 (core-luascript.md, section 8.12). Upstream 5.4.9's `str_rep` checks only the result size, so `string.rep("", math.maxinteger)` makes 2^63 - 1 empty copies in C, which no limit stops. Lua 5.5.1 returns `""` when both the string and the separator are empty (`lstrlib.c:152`); 5.4.9's `str_rep` gets the same test, one changed line marked `xlua-ng`. The result is the same `""`. Applies to every 5.4.9 build, not only libxlua. | Retain until a 5.4 release has the check. |
| Library part guards in `loslib.c`, `loadlib.c` and `lbaselib.c` (5.3.6, 5.4.9, 5.5.1) | Added 2026-10-18 for the `minimal` preset ([presets.md](../docs/design/presets.md)), which keeps only `os.time`, `clock`, `date` and `difftime` and has no `package.loadlib`, C searchers, `dofile` or `loadfile`. Removing fields at run time would keep their code and `dlopen` in the library, so each file gets `#if` guards, marked `xlua-ng`, around the functions and their `luaL_Reg` entries: `XLUA_OS_TIME_ONLY` (`os_remove`, `os_rename`, `os_tmpname`, `os_getenv`, `os_setlocale`, `os_exit`), `XLUA_NO_LOADLIB` (`lookforfunc`, `ll_loadlib`, `loadfunc`, `searcher_C`, `searcher_Croot`, the `loadlib` entry and the two searchers; it also undefines `LUA_USE_DLOPEN` and `LUA_DL_DLL` after the includes, so the file's own stub branch replaces `dlopen` and `LoadLibrary`) and `XLUA_NO_DOFILE` (`luaB_loadfile`, `dofilecont`, `luaB_dofile` and their entries). About 12 to 17 lines per file and version. CMake defines the macros for libxlua only when `XLUA_OS_FULL`, `XLUA_LOADLIB` or `XLUA_DOFILE` is off ([README.md](README.md#parts-of-os-package-and-the-base-library)); without them the preprocessed code is unchanged, and the stripped Android legacy libraries are equal to main's byte for byte once the build ID is removed. LuaJIT is not patched: `xlua_libs.c` removes the same fields at run time there. | Retain while the minimal preset has these options. |
| Metatable of an all-weak table (5.3.6) | Added 2026-10-28, found by `Test/Fuzz/fuzz_bridge.c` (docs/research/crash-inventory.md, item 27). Upstream bug "New metatable in an all-weak table can fool the GC" (lua.org/bugs.html, reported 2025-06-12, present since 5.3.0): `traversetable` put a table with `__mode = "kv"` met during propagation into `allweak`, which the atomic phase only clears, and left it gray; a metatable set after that got no barrier, was never marked and was freed while in use (heap use after free, from `setmetatable` and `collectgarbage` alone). Lua 5.4's fix, which 5.4.9 and 5.5.1 contain, is copied into 5.3.6's `lgc.c`: during propagation the table goes to `grayagain` and is visited again in the atomic phase. Five lines, marked `xlua-ng`. | Retain; 5.3.6 is the last 5.3 release. |
| Lua 5.3 signature check spelling | `lundump.c` spells the skipped first signature byte as `&LUA_SIGNATURE[1]`, as Lua 5.4 and 5.5 do, instead of `LUA_SIGNATURE + 1`. The pointer is identical; the old form triggers clang `-Wstring-plus-int`. Reported upstream in 2019 (lua-l, 2019-04 msg 186); 5.3.6 is the final 5.3 release. | Retain. Behavior-neutral warning fix. |
| Lua 5.3 `log2` optimization removal | The 5.3.5 import `225dfc5` already lacks upstream's base-2 fast path. The initial 5.3.3 import `4eb59f8` also uses generic logarithms. No commit explaining the deliberate omission was found. | Retain existing behavior for now; avoiding a `log2` dependency is a plausible effect, not a verified historical motive. `math.log(8, 2)` is covered by the native regression check. |

## Upgrade-specific checks and changes

Lua 5.3.6 changed `io.popen` mode validation and bytecode loading GC
barriers/rooting. Restored xLua patches preserve both upstream changes.
Standard and custom-format loading pass long-string/nested-prototype tests
that force GC from the incremental reader. Both host suite modes pass the
managed suite; this does not replace cross-architecture bytecode execution.

The fixed-signature `xlua_gc` bridge addresses the separately reproduced
managed-to-variadic C ABI mismatch on Apple ARM64. It is exercised by
managed regression tests and iPhone IL2CPP smoke tests with Lua 5.4.9 and
5.3.6. It is a bridge fix rather than a Lua vendor patch.

Moving the default from 5.3.6 to 5.4.9 (it is now 5.5.1) exposed two
configuration dependencies:

- Lua 5.3 and legacy UWP build scripts must explicitly select 5.3.6.
- `LUAC_COMPATIBLE_FORMAT=ON` must not silently do nothing with 5.4.
  CMake now rejects this combination. The separate `build/luac` project
  builds standard `lua`/`luac` for any bundled runtime (default 5.5.1) and
  accepts `LUAC_COMPATIBLE_FORMAT` only with 5.3.6.

Lua 5.4 uses a different bytecode encoding. Do not copy the Lua 5.3 format
patch into it merely because both runtimes have a dump/undump subsystem.
Any future format extension requires its own design and tests.

Remaining validation gaps include actual UWP execution, Lua 5.3.6 Android
player execution, legacy iOS targets, and 32-bit/64-bit custom-bytecode interchange.
No claim that the entire xLua dependency tree is defect-free follows from
this audit.

## Lua test suites

On 2026-10-17 the patched trees passed lua.org's official test suites (5.3.4
for 5.3.6, 5.4.9, 5.5.1) in the basic, complete and internal (`ltests`)
modes on Linux and macOS, also under ASan and UBSan on Linux, and 5.3.6 also
with `LUAC_COMPATIBLE_FORMAT`. `Test/Native/lua_tests.py` builds the
interpreter from the libxlua core sources and options; see
[Test/README.md](../Test/README.md#luas-official-test-suites).

Three patches change what the suites see. Each is an intended difference,
and the runner adjusts the suite for it instead of changing Lua:

- `os.execute` removal: `main.lua` and `files.lua` need `os.execute` in the
  complete mode. The runner asserts that it is nil, then supplies an
  `io.popen` equivalent for the run.
- `LUA_IDSIZE` 120: `errors.lua` hard-codes upstream's 60 for the length of
  truncated source names. The runner moves the limit and border cases to 120.
- `LUAC_COMPATIBLE_FORMAT`: `calls.lua` expects format 0 and a
  `sizeof(size_t)` header byte. The custom format writes 1 and drops that
  byte, as designed. With the header expectation changed, the rest of the
  suite, which `all.lua` runs through `string.dump` and `load`, passes.

The suites found no defect in the patches. They do not exercise the UWP
guards or the Android decimal-point patch, which need those platforms.

## LuaJIT test suite

`build/luajit-2.1` is upstream LuaJIT v2.1 at 2a8e017e without source
changes: on 2026-10-18 `diff -r` against GitHub's tarball of that commit
differed only by the added `src/msvcbuild_mt.bat` (the Windows build with the
static C runtime). Our differences are build options, which
`Test/Native/luajit_tests.py` reproduces as modes. On 2026-10-18 it ran
LuaJIT-test-cleanup (commit 014708bc) against the tree; see
[Test/README.md](../Test/README.md#luajits-test-suite) for the method, the
skips and the per-test lists.

| Platform | default | assert | nojit (iOS VM options) | nx (Switch flags) |
| --- | --- | --- | --- | --- |
| macOS 27 arm64, Apple clang 21 | pass, 2 known | pass, 2 known | pass, 2 known | pass |
| Linux arm64 container, GCC 15.2 | pass, 1 known | pass, 1 known | pass, 1 known | not available on Linux arm64 |
| Linux x86_64 under qemu-user | pass | pass | pass | not valid under emulation |
| Linux x86_64, GitHub runner (CI, 2026-10-18) | pass | pass | pass | pass |

A `nogc64` mode (`LUAJIT_DISABLE_GC64`) also passed on the GitHub runner; it
was removed on 2026-10-28 with xLua's support for a 64-bit LuaJIT without
GC64 ([README.md](README.md#luajit-gc64)).

"Pass" means all 510 runner tests (388 without the JIT, 306 without the
JIT and FFI) and the standalone files pass. No failure comes from our build
options, so nothing was fixed:

- Known failure on macOS arm64: an upstream FFI defect. Callbacks read
  stack-passed arguments narrower than 8 bytes from 8-byte slots
  (`lj_ccallback.c`), although Apple's ABI packs them.
- Known failure on ARM64: `unportable/ffi_arith_int64.lua`, whose checksums
  assume single-number mode (ARM64 LuaJIT uses dual numbers).
- Five tests check behaviour that LuaJIT changed after the suite's last
  commit (2016). The runner updates their expectations as tarantool/luajit's
  copy does.
- One environment difference: glibc 2.34 and later has no unversioned
  `libpthread.so`.

Not run: x86_64 on real hardware (the `luajit-tests` CI job, manual, not run
yet), Windows, 32-bit, the iOS, Android and Switch targets themselves, and
sanitizers.

Lua 5.5-specific bridge adaptation and device results are recorded in
[Lua 5.5 integration](LUA55.md). The Lua 5.3 bytecode extension remains
limited to that runtime family.
