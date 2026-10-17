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
