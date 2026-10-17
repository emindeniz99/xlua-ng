# C17 build setup for xlua-ng's own C files

Researched 2026-10-17 for `build/xlua.c`, `build/i64lib.c` and
`build/memory_leak_checker.c` (2,266 lines). Vendored Lua, LuaJIT and
LuaSocket are left as they are. Measurements: Apple clang 21 (arm64) and
NDK r27 clang 18. NDK r30, GCC, MSVC, emcc and the OpenHarmony toolchain
were not available, so claims about them are secondary. Confidence:
**[C]** confirmed or measured, **[S]** secondary, **[U]** unconfirmed.

Primary source for hardening: the [OpenSSF Compiler Options Hardening Guide
for C and C++](https://best.openssf.org/Compiler-Hardening-Guides/Compiler-Options-Hardening-Guide-for-C-and-C++.html)
(2026-08-20).

## Warnings

`build/CMakeLists.txt` sets no warning flags today. Measured hits with
`-std=c17` on Lua 5.5.1: **[C]**

| Flag set | Hits |
|---|---|
| `-Wall -Wextra` | 2 (unused parameters) |
| + `-Wpedantic` | 21 (16 are `-Wextra-semi`) |
| `-Wmissing-prototypes` | 115, because the only declarations live in `LuaDLL.cs` |
| `-Wconversion` | 55, all sign conversions, mostly in xlua.c 283–391 and 945–1013 |

- **Already clean:** `-Wshadow`, `-Wvla`, `-Wformat=2`, `-Wcast-align`,
  `-Wcast-qual`, `-Wwrite-strings`, `-Wold-style-definition`,
  `-Wimplicit-fallthrough`, `-Wnull-dereference`, `-Wcomma`,
  `-Wconditional-uninitialized`, `-Wc++-compat`. The last one matters because
  `xlua_webgl.cpp` compiles the same code as C++.
- **Small real findings:**
  - `int xlua_tag()` and similar use `()` instead of `(void)` (xlua.c:37, 42, 46);
  - `#if USING_LUAJIT` should use `defined()` (xlua.c:23);
  - one `-Wsign-compare` (1212), one `-Wdouble-promotion` (1305);
  - `Test/Fuzz/fuzz_common.h:20` declares `xlua_tag(void)` while xlua.c
    defines `xlua_tag()`, a mismatch that nothing catches.
- **Worth a look:** `xlua_newstruct(int size)` computes
  `size + sizeof(int) + sizeof(unsigned int)`, so a negative `size` becomes a
  huge `size_t` (xlua.c:951–953). The C# callers were not checked.

Recommendation:

- **Clang and GCC:**
  - `-Wall -Wextra -Wpedantic`, `-Wformat -Wformat=2`;
  - `-Wshadow -Wvla -Wundef -Wstrict-prototypes -Wold-style-definition`;
  - `-Wcast-qual -Wwrite-strings -Wcast-align -Wimplicit-fallthrough`;
  - `-Wdouble-promotion -Wnull-dereference -Wc++-compat -Wdate-time`;
  - `-Wconversion -Wno-sign-conversion` until the 55 sign conversions are
    triaged by hand. Do not mass-cast.
- **Always errors:** `-Werror=return-type`,
  `-Werror=implicit-function-declaration`, `-Werror=int-conversion` and
  `-Werror=incompatible-pointer-types`, as the NDK guide and OpenSSF
  recommend. **[C]**
- **MSVC:** `/W4 /sdl /utf-8` plus selected `/we` codes. **[S]**
- **Scope:** our three files only, with
  `set_property(SOURCE ${XLUA_CORE} APPEND PROPERTY COMPILE_OPTIONS ...)`.
  It must append, so that the coverage options stay.
- **`-Werror` in CI only,** through an `XLUA_WERROR` option. OpenSSF advises
  against blanket `-Werror` in distributed source. **[C]**
- **`-Wmissing-prototypes`:** comes after a private `xlua.h` that declares
  every export. Tests and fuzzers then share it instead of re-declaring
  exports by hand.

## Hardening

| Flag | Verdict |
|---|---|
| `-fstack-protector-strong` | Yes. +0.9% `__text` in xlua.c, +3.4% in lparser.c. Android's toolchain file already adds it. Apple's default is plain `-fstack-protector`. |
| `-mbranch-protection=standard` (arm64) | Yes, on Android and OHOS arm64 (+4.5% in xlua.c). Its instructions are NOPs on older CPUs. Verify the ELF note in `check_android_elf.py`. |
| `-ftrivial-auto-var-init=zero` | Yes, after measuring. Off in MSan and valgrind builds. |
| `-fno-delete-null-pointer-checks` | Yes, free |
| `_FORTIFY_SOURCE` | Level 2 (bionic and Apple cap at 2; level 3 only on Linux glibc). Not in sanitizer builds. |
| `-fwrapv`, `-fno-strict-aliasing` | Optional; measure with `Test/Native/flag_sweep.py` |
| `-fcf-protection`, `-fstack-clash-protection` | Linux x86_64 only, low value: no VLAs or `alloca` here |
| MSVC | `/guard:cf`; `/Qspectre` needs the Spectre libraries |

Notes:

- Speed was not measured. Layout noise alone moves the geomean by about
  0.3% ([FLAGS.md](../../build/FLAGS.md)).
- Architecture-specific flags are hard errors on the wrong target, so gate
  them with `check_c_compiler_flag`.
- LuaJIT is built by its own Makefile, which passes `-U_FORTIFY_SOURCE
  -fno-stack-protector` (`luajit-2.1/src/Makefile:226,317-318`). The CMake
  flags do not reach it.
- Unity's WebGL build compiles `xlua_webgl.cpp` with Unity's own emcc,
  outside CMake.

## Sanitizers and tools

- **`float-cast-overflow` on GCC:** GCC does not include it in
  `-fsanitize=undefined` (Clang does). A GCC run of `sanitize.sh` would have
  missed the uint32 conversion bug. Add it explicitly. **[C]**
- **`LUA_USE_APICHECK`:** nothing in the repo enables it. Turn it on in the
  sanitizer, fuzz and coverage builds; it found bridge item #14.
- **MSan:** worth a job for the libFuzzer targets only. Struct userdata
  memory is not initialised before C# fills it.
- **GCC `-fanalyzer`:** worth a nightly, non-blocking job;
  `memory_leak_checker.c` is the best target.
- **clang-format:** only for our three files, and only on changed lines
  (`git clang-format`).
- **Fuzz corpus:** run the committed corpus as a regression (`-runs=0`) and
  periodically merge new finds.
- **CodeQL:** consider the `security-extended` query suite.
- **Not worth it here:** scan-build (duplicates `clang --analyze`), IWYU,
  TSan (Lua states are single-threaded), valgrind, cppcheck as a gate, and
  Frama-C in CI.

## C17 language notes

- **What C17 is:** a bug-fix release of C11. Of the C11 features,
  `_Static_assert` is the useful one here. Pin `sizeof(CSharpStruct)`,
  `offsetof(CSharpStruct, data)` and `sizeof(lua_Number)`, which the struct
  size arithmetic relies on.
- **Struct hack:** `CSharpStruct` ends in `char data[1]`. That conflicts with
  `-fstrict-flex-arrays`, `-fsanitize=bounds` and the `_FORTIFY_SOURCE=3`
  object-size checks. The accesses use `&css->data[0] + offset`, which avoids
  most issues. `char data[]` is not valid C++, which the WebGL build needs, so
  test any change there.
- **Light userdata keys:** a function pointer cast to `void*`
  (xlua.c:297, 302, LuaJIT branch) is non-ISO but deliberate. Mark it.
- **MSVC:** `/std:c17` exists since VS 2019 16.8. It has no VLAs, complex
  numbers or `<threads.h>`.
- **C23:** the code stays on C17 while Unity 2021.3's WebGL clang 13 is
  supported.

## Reproducible builds

- `-ffile-prefix-map=${CMAKE_SOURCE_DIR}=.` and the same for the build
  directory. Without it, the same source built in two directories gave two
  object hashes; with it, one. **[C]**
- `-Wdate-time` keeps `__DATE__` and `__TIME__` out of the code.
- The NDK's `--build-id=sha1` is content-derived.
- MSVC: `/Brepro`. Apple: `ZERO_AR_DATE=1` for archives. **[S]**

## Implemented (2026-10-17)

What the notes above led to, on the same day. Evidence is from builds and
runs with Apple clang 21 (macOS 27 arm64, Ninja and Xcode generators), NDK
r30 clang 21 (arm64-v8a, armeabi-v7a), and GCC 15.2 and Clang 21 in the
`xlua-analysis:26.04` container (Linux arm64). Logs are under
`c-setup-2026-10-17/` on the build disk.

### Warnings

- The recommended set is on, for our three files only (`build/README.md`,
  "Warnings, hardening and test options"). Our files now compile without a
  warning, also with `XLUA_WERROR=ON` and `XLUA_HARDEN=ON`: all four
  runtimes on macOS (Apple clang) and Linux arm64 (GCC and Clang); Lua 5.3.6,
  5.4.9 and 5.5.1 on both Android ABIs, LuaJIT on arm64-v8a; Lua 5.5.1 for
  iOS and through the Xcode generator. **[C]**
- Fixed as the notes said: the `()` prototypes, `#if defined(USING_LUAJIT)`,
  the `-Wsign-compare` in `xlua_is_eq_str` and the `-Wdouble-promotion` in
  the float field getter, the 16 extra semicolons after macro invocations,
  and the unused parameters. The `fuzz_common.h` mismatch is gone because
  `xlua_tag` is now defined with `(void)`. **[C]**
- Found only once the set ran on more compilers and runtimes: **[C]**
  - LuaJIT branch (Apple clang `-Wshorten-64-to-32`): `lua_objlen` and the
    `lua_rawgeti`/`lua_rawseti` index narrowed implicitly; they now narrow
    with explicit casts.
  - GCC `-Wpedantic` on the LuaJIT branch: the function-pointer registry
    key in `push_helper` now goes through `uintptr_t`, with a
    `NOLINT(performance-no-int-to-ptr)`; that is the "mark it" item from the
    C17 notes.
  - armeabi-v7a `-Wcast-align` in `memory_leak_checker.c`: Lua's own
    `gco2*` macros cast to `union GCUnion *` (8-byte alignment). The leak
    checker alone gets `-Wno-cast-align`.
  - LuaJIT's and Lua's headers gave 20 `-Wundef` hits (`lj_arch.h`) inside
    our files. The vendored include directories are now `SYSTEM`
    directories; the search order is unchanged.
  - `i64lib.c` used `sprintf` for the int64 text, which MSVC reports at
    `/W4` (C4996); it uses `snprintf` now.
- **`xlua_newstruct`:** no C# code calls it; `LuaDLL.cs` has no import for
  it. **[C]** Sizes -8 to -1 allocated fewer bytes than the 8-byte header the
  function writes, with a length near 2^32. A negative size now pushes `nil`
  and returns `NULL`. `native_regression.py` checks it and fails on the old
  build. **[C]**
- **MSVC:** `/W4 /utf-8` and `/we4013 /we4024 /we4047 /we4133 /we4716`.
  `/sdl` was left out: it turns C4996 and other warnings into errors in every
  build, not only under `XLUA_WERROR`. Nothing was compiled with MSVC, so CI
  keeps `XLUA_WERROR` off on Windows until a run shows the `/W4` output. **[U]**
- **Not done:** `-Wmissing-prototypes` with a private `xlua.h`,
  `_Static_assert` for `CSharpStruct`, and the triage of the 55 sign
  conversions (`-Wno-sign-conversion` stays).

### Sanitizers and tools

- **Correction:** `Test/Fuzz/run.sh` already compiled the PUC runtimes with
  `-DLUA_USE_APICHECK`. `sanitize.sh`, `coverage.sh` and every LuaJIT build
  did not. **[C]**
- `XLUA_APICHECK` defines `LUA_USE_APICHECK` and removes `NDEBUG`, because
  Lua implements the checks with `assert` and `RelWithDebInfo` defines
  `NDEBUG`. `sanitize.sh` and `coverage.sh` use it; their LuaJIT builds and
  the fuzz script's pass `-DLUA_USE_APICHECK` to LuaJIT's make. A probe
  confirmed the checks run: `lua_pushvalue(L, 100)` on an empty Lua 5.5.1
  stack aborts in `index2value` with the option and returns nil without it;
  `lua_rawgeti` on a number aborts in LuaJIT with "stack slot 1 is not a
  table" and crashes (SIGSEGV) without it. **[C]**
- `sanitize.sh` adds `float-cast-overflow` when the compiler is not Clang.
  With GCC 15.2, `XLUA_WERROR=ON` and the API checks, all four runtimes
  pass (exports, native regression, profiler smoke, managed suite in three
  modes). **[C]**
- `analyze.sh` (clang-tidy and `clang --analyze`) is clean for all four
  runtimes; the new flags reach it through `compile_commands.json`. A
  60-second `Test/Fuzz/run.sh` per target found nothing on Lua 5.5.1 (four
  targets) and LuaJIT (`i64`). **[C]**
- **Found, not fixed:** `CC=clang Test/Native/sanitize.sh` fails before any
  test: it preloads GCC's `libasan.so`, but a Clang-built `libxlua.so` needs
  Clang's runtime (`undefined symbol: __ubsan_handle_builtin_unreachable`).
  This is older than these changes; CI uses GCC. **[C]**
- Still follow-ups: MSan for the fuzz targets, a GCC `-fanalyzer` job,
  `git clang-format` on changed lines, the corpus regression run, CodeQL
  `security-extended`.

### Hardening

`XLUA_HARDEN` (off) enables what the hardening table above recommends, each
flag checked with `check_c_compiler_flag`. What it adds, as configured: **[C]**

| Toolchain | Flags added |
|---|---|
| NDK r30, arm64-v8a | `-fstack-protector-strong -fno-delete-null-pointer-checks -ftrivial-auto-var-init=zero -mbranch-protection=standard` |
| NDK r30, armeabi-v7a | the same without `-mbranch-protection` |
| Apple clang 21, macOS and iOS | `-fstack-protector-strong -fno-delete-null-pointer-checks -ftrivial-auto-var-init=zero -D_FORTIFY_SOURCE=2` |
| GCC 15.2, Linux arm64 | as NDK arm64; Ubuntu's GCC already sets `_FORTIFY_SOURCE=3`, which is kept |
| Clang 21, Linux arm64 | as NDK arm64, plus `-D_FORTIFY_SOURCE=2` |

- The NDK already passes `-fstack-protector-strong` and `-D_FORTIFY_SOURCE=2`,
  so on Android the option adds three flags. **[C]**
- PUC Lua libraries for arm64-v8a carry the `BTI, PAC` property note
  (`check_android_elf.py --expect-branch-protection` passes). A LuaJIT build
  does not: lld writes the note only when every object has it, and
  `libluajit.a` comes from LuaJIT's Makefile. LuaJIT supports BTI when built
  with `-mbranch-protection=standard -DLUAJIT_ENABLE_CET_BR`
  (`luajit-2.1/src/lj_arch.h`); PAC only on arm64e. **[C]**
- Not built: Linux x86_64 (`-fcf-protection`, `-fstack-clash-protection`),
  OpenHarmony, MSVC (`/guard:cf`), Switch. **[U]**
- **Cost** (Lua 5.5.1, NDK r30, moto g play 2024 big cores, details in
  [FLAGS.md](../../build/FLAGS.md#xlua_harden)): arm64-v8a +2,041 bytes in
  the APK and -3.9% / -4.1% geomean in two runs (layout control -0.7%);
  armeabi-v7a +2,335 bytes and -1.7%. PAC/BTI is about -2.2% and does nothing
  on this ARMv8.0 phone; zeroed locals are about -1.7% and up to -14% on
  protected calls; `-fno-delete-null-pointer-checks` is free. The "speed
  not measured" note above is now answered: the cost is well outside the
  0.3% layout noise. **[C]**
- **Recommendation:** keep `XLUA_HARDEN` off by default for now; the owner
  decides. A cheaper default to measure next: the null-check flag everywhere,
  zeroed locals for our three files only, and PAC/BTI decided on an ARMv8.5
  device.

### LuaJIT and the cheaper split (2026-10-17, later)

Evidence: NDK r30 builds of the real scripts and the moto g play 2024
(Cortex-A73 big cores, ARMv8.0); logs under `android-flags-2026-10-17/` on
the build disk.

- **LuaJIT now gets the flags.** `build/luajit_harden.sh` passes them to
  LuaJIT's Makefile as `TARGET_CFLAGS`, which comes after the Makefile's own
  `-U_FORTIFY_SOURCE -fno-stack-protector` (`luajit-2.1/src/Makefile:226-231,
  317-318`). On arm64 `-mbranch-protection=standard -DLUAJIT_ENABLE_CET_BR`
  sets `__ARM_FEATURE_BTI_DEFAULT`, so `lj_arch.h:291-295` defines
  `LJ_ABI_BRANCH_TRACK`, the Makefile passes `-D BRANCH_TRACK` to DynASM
  (`Makefile:446-449`), the interpreter gets `bti` landing pads
  (`vm_arm64.dasc:95-105`) and buildvm writes a BTI `.note.gnu.property`
  (`vm_arm64.dasc:4290-4302`). **[C]**
- **BTI only, no PAC note.** That note has the BTI bit alone; PAC in the
  interpreter exists only for arm64e (`lj_arch.h:288-290`). lld ANDs the
  notes, so the arm64-v8a LuaJIT library is marked BTI while its C code
  still has 1,250 `paciasp` (34 without the flags). `check_android_elf.py
  --expect-branch-protection` asks LuaJIT libraries for BTI only. **[C]**
- **Still passes:** hardened macOS arm64 LuaJIT: `check_exports.py`,
  `native_regression.py`, managed suite in three modes (349 tests). The
  hardened Android arm64 LuaJIT library was built and checked with
  `check_android_elf.py`, not run on the phone (the phone runner needs Lua
  5.3+). armeabi-v7a LuaJIT needs a 32-bit host compiler, which macOS lacks;
  not built. **[C]**
- **Size:** arm64-v8a LuaJIT with `XLUA_HARDEN=ON` is 32,968 bytes larger
  stripped and 9,191 bytes larger in the APK than without (670,112 and
  332,179). **[C]**
- **The cheaper split is measured** ([FLAGS.md](../../build/FLAGS.md#a-cheaper-hardening-split)):
  `-fno-delete-null-pointer-checks` everywhere plus zeroed locals in our
  three files costs +0.2% on armeabi-v7a and -0.5% / -1.5% on arm64-v8a
  (two runs, inside the -1.1 to -1.5% of the layout controls), and 176 to
  244 bytes of APK. Full `XLUA_HARDEN` on the same build: -2.9 to -3.2% and
  -1.7%. The protected-call losses (9 to 13%) do not appear with the cheap
  set, which fits the `jmp_buf` explanation above but was not profiled.
  **[C]** for the numbers, **[U]** for the cause.
- **Recommendation** (for the owner; nothing changed): the cheap set on by
  default in every preset, the rest behind `XLUA_HARDEN`, PAC/BTI decided
  on an ARMv8.5 device.

