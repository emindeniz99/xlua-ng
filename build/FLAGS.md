# Compiler and linker options

Size and speed of `libxlua` (Lua 5.5.1) built with other compiler and linker
options than the release scripts use. Measured on 2026-10-17 with NDK r30
(clang 21, lld), Xcode 27.1 beta (Apple clang 21) and a moto g play 2024
(Snapdragon 680 class SM6225, 4x Cortex-A73 at 2.4 GHz and 4x Cortex-A53,
Android 14). Nothing here changes the release build:
`build/make_android_lua55.sh` and `build/make_ios_lua55.sh` still build `-O3`
at API 21. `Test/Native/flag_sweep.py` builds and measures every variant; see
"Compiler and linker options" in `Test/README.md`.

## Summary

Android arm64-v8a, the stripped library as Unity packs it:

| build | stripped | APK (deflate) | Lua speed vs `-O3` |
| --- | ---: | ---: | --- |
| release (`-O3`, API 21) | 418,272 | 169,426 | |
| `bestnwc21`: per-file levels, exports trimmed, API 21 | 364,656 (-53,616) | 153,033 (-16,393) | geomean +0.9%, no case outside layout noise |
| `bestnwc23`: the same at API 23 with packed relocations | 346,400 (-71,872) | 150,575 (-18,851) | geomean +0.8%, no case outside layout noise |
| `bestnw21`: the compiler and loader at `-Oz` too, API 21 | 346,096 (-72,176) | 144,915 (-24,511) | geomean +0.5%; compiling Lua source 5 to 8% slower |
| `bestnw23`: the same at API 23 | 327,840 (-90,432) | 142,480 (-26,946) | geomean +0.1 to +1.2%; compiling Lua source 4 to 6% slower |

Recommended: `bestnwc21` while the plugin supports API 21 and 22, `bestnwc23`
for a minimum of API 23 or 24. If 5 to 8% slower `require` of Lua source is
acceptable, `bestnw21`/`bestnw23` save another 8 KB in the APK. armeabi-v7a
shows the same order (`bestnwc23` -40,880 stripped, -16,256 APK, geomean +1.1%).
Proposed for the owner to decide: add `xlua.c` to the `-O3` list on both ABIs
(`bestnwc23-xluaO3`, 308 to 474 bytes more in the APK); see "`xlua.c` at `-O3`".

The `bestnwc*` build is:

- `-O3` for the 18 files of `HOT_FILES_LOAD` in `flag_sweep.py`: the VM
  (`lvm.c`, `ldo.c`, `ltable.c`, `lgc.c`, `lstring.c`, `lapi.c`, `lfunc.c`,
  `lobject.c`), the library code the profile ranks next (`lstrlib.c`,
  `lauxlib.c`, `lmem.c`, `ltm.c`, `ltablib.c`: string patterns and formatting,
  `luaL_Buffer`, allocation, metamethods, `table.sort`) and the compiler and
  loader (`llex.c`, `lparser.c`, `lcode.c`, `lundump.c`, `lzio.c`).
  `bestnw*` keeps only the first 13 (`HOT_FILES_WIDE`) at `-O3`.
- `-Oz -mllvm -enable-machine-outliner=never` for everything else: the debug
  interface, bytecode dump, the other standard libraries, LuaSocket, `xlua.c`
  and `i64lib.c`.
- `-fvisibility=hidden` with `LUA_API` changed to
  `__attribute__((visibility("default"))) extern` in `luaconf.h`.
- `-Wl,--version-script` exporting only the 117 functions `LuaDLL.cs` imports.
- `-Wl,--icf=all`.
- API 23 only: `-Wl,--pack-dyn-relocs=android`. The NDK already switches to
  `--hash-style=gnu` at API 23.

The geomean is over the 26 cases of `Test/Native/flag_bench.c`, median of five
interleaved rounds (three for `bestnwc*`, in a separate run). Section-order
controls (below) show that where the linker puts the hot code moves single
cases by up to 7% and the geomean by 0.3% in an `-O3` build, and some cases
switch between two speeds from one process to the next. Against that noise
floor `bestnwc*` is as fast as `-O3` in every case. Its small geomean gain comes
from the version script, which removes the PLT calls between exported functions.

iOS (no device, size only): the same split with Apple's default `-Oz`
(outliner on) links 257,096 bytes of `__TEXT` for `hybridwc-O3` (the
`bestnwc*` file list) and 235,584 for `hybridw-O3` (the `bestnw*` list),
against 285,877 for `-O3`: -28,781 (-10.1%) and -50,293 (-17.6%).

What does not work:

- The machine outliner on `-O2`/`-O3` code is 18 to 25% slower and makes the
  Android library larger after compression, because every outlined sequence gets
  its own unwind entry.
- `-Oz` as Android uses it (outliner on) is larger after compression than `-Os`
  and 29% slower. Without the outliner, `-Oz` is the smallest build in every
  measure, and 21% slower.
- LTO at `-O3` grows the library by 159 KB (+38%). ThinLTO is the fastest build
  (+7.6% geomean) but costs 61 KB in the APK. LTO with the hybrid split is no
  smaller than `-O3` after compression.
- `-O2` alone saves 6.9 KB in the APK and is 6% slower (`table_array_read`
  -33%).

Hardening (`-DXLUA_HARDEN=ON`, [below](#xlua_harden)): +2.0 KB in the arm64-v8a
APK and 4% slower, +2.3 KB and 1.7% slower on armeabi-v7a. On this ARMv8.0
phone half of the arm64 cost is PAC/BTI, which only newer cores enforce, and
most of the rest is zeroed locals on Lua's protected-call path. It stays off
by default until the owner decides.

## Android arm64-v8a

`APK (deflate)` is the stripped file compressed with zip deflate level 6, which
is how the Android Gradle plugin stores native libraries in a compressed APK; for
the release build it matches the 169,426 bytes measured in the APK (`SIZE.md`).
Speed is the geomean over all cases of the median of five rounds, then the case
with the largest loss. Deltas are against `O3`.

| variant | stripped | gzip -9 | APK (deflate) | speed, geomean | largest loss |
| --- | ---: | ---: | ---: | ---: | --- |
| `O3` | 418,272 (0) | 169,040 (0) | 169,426 (0) | | |
| `O2` | 396,720 (-21,552) | 162,218 (-6,822) | 162,559 (-6,867) | -6.0% | `table_array_read` -33.1% |
| `Os` | 351,592 (-66,680) | 143,693 (-25,347) | 144,103 (-25,323) | -14.1% | `table_array_read` -36.3% |
| `Oz` | 333,752 (-84,520) | 146,879 (-22,161) | 147,044 (-22,382) | -29.0% | `int_math` -43.7% |
| `Oz-noout` | 326,200 (-92,072) | 136,714 (-32,326) | 137,024 (-32,402) | -20.7% | `int_math` -38.2% |
| `hybrid-O3` | 363,928 (-54,344) | 156,481 (-12,559) | 156,703 (-12,723) | -2.5% | `string_pattern` -20.8% |
| `hybrid-O2` | 353,288 (-64,984) | 153,826 (-15,214) | 154,021 (-15,405) | | |
| `hybridn-O3` | 358,232 (-60,040) | 147,806 (-21,234) | 148,177 (-21,249) | -1.9% | `string_pattern` -17.4% |
| `hybridn-O2` | 347,608 (-70,664) | 145,510 (-23,530) | 145,658 (-23,768) | -6.4% | `empty_loop` -18.7% |
| `hybridnw-O3` | 371,136 (-47,136) | 152,772 (-16,268) | 153,109 (-16,317) | -0.7% | `c_compile` -7.4% |
| `hybridw-O3` | 375,616 (-42,656) | 159,846 (-9,194) | 160,062 (-9,364) | | |
| `hybridOs-O3` | 377,528 (-40,744) | 152,366 (-16,674) | 152,743 (-16,683) | | |
| `thinlto-O3` | 577,008 (+158,736) | 230,045 (+61,005) | 230,585 (+61,159) | +7.6% | `function_call` -1.6% |
| `fulllto-O3` | 578,648 (+160,376) | 231,591 (+62,551) | 232,075 (+62,649) | +4.5% | `table_array_read` -8.3% |
| `thinlto-hybrid` | 397,280 (-20,992) | 171,991 (+2,951) | 172,215 (+2,789) | -0.4% | `string_pattern` -22.4% |
| `fulllto-hybrid` | 383,760 (-34,512) | 172,328 (+3,288) | 172,429 (+3,003) | -3.2% | `string_pattern` -26.7% |
| `outliner-O3` | 422,096 (+3,824) | 187,467 (+18,427) | 187,771 (+18,345) | -24.6% | `int_math` -51.9% |
| `outliner-O2` | 400,944 (-17,328) | 178,311 (+9,271) | 178,643 (+9,217) | -18.4% | `int_math` -42.9% |
| `icf` | 417,104 (-1,168) | 168,763 (-277) | 169,162 (-264) | -0.4% | `table_array_read` -6.5% |
| `icf-safe` | 418,272 (0) | 169,040 (0) | 169,427 (+1) | | |
| `vscript` | 392,552 (-25,720) | 160,231 (-8,809) | 160,570 (-8,856) | +0.4% | `table_array_read` -6.4% |
| `hidden` | 416,168 (-2,104) | 168,291 (-749) | 168,681 (-745) | +0.4% | `string_format` -5.9% |
| `hidden-vscript` | 392,528 (-25,744) | 160,236 (-8,804) | 160,581 (-8,845) | | |
| `api21-gnuhash` | 414,960 (-3,312) | 167,944 (-1,096) | 168,346 (-1,080) | | |
| `api23` | 415,136 (-3,136) | 167,968 (-1,072) | 168,368 (-1,058) | +0.3% | `method_call` -1.8% |
| `api24` | 415,088 (-3,184) | 167,982 (-1,058) | 168,376 (-1,050) | | |
| `api23-aps2` | 399,168 (-19,104) | 165,965 (-3,075) | 166,391 (-3,035) | +0.2% | `string_format` -3.6% |
| `api24-aps2` | 399,120 (-19,152) | 165,962 (-3,078) | 166,381 (-3,045) | | |
| `api28-relr` | 397,656 (-20,616) | 165,942 (-3,098) | 166,342 (-3,084) | -0.0% | `method_call` -1.9% |
| `bestn21` | 333,272 (-85,000) | 139,941 (-29,099) | 140,183 (-29,243) | -0.7% | `string_pattern` -16.4% |
| `bestn23` | 315,032 (-103,240) | 137,454 (-31,586) | 137,710 (-31,716) | +0.1% | `string_pattern` -16.7% |
| `bestnw21` | 346,096 (-72,176) | 144,645 (-24,395) | 144,915 (-24,511) | +0.5% | `c_compile` -7.5% |
| `bestnw23` | 327,840 (-90,432) | 142,194 (-26,846) | 142,480 (-26,946) | +0.6% | `c_compile` -6.3% |
| `bestnw24` | 327,840 (-90,432) | 142,194 (-26,846) | 142,481 (-26,945) | | |
| `bestnwc21` | 364,656 (-53,616) | 152,789 (-16,251) | 153,033 (-16,393) | +0.9% | `fib_recursive` -3.7% |
| `bestnwc23` | 346,400 (-71,872) | 150,322 (-18,718) | 150,575 (-18,851) | +0.8% | `fib_recursive` -2.8% |

Variant names: `hybrid` puts the 8 VM files at `-O3` (or `-O2`) and the rest at
`-Oz`; `hybridn` does the same with the outliner off in the `-Oz` files;
`hybridnw` uses the 13 files of `HOT_FILES_WIDE`; `best*` adds hidden
visibility, the version script and `--icf=all`, and `best*23` adds packed
relocations at API 23. `vscript` is only the version script; `hidden` only the
visibility change. `api21-gnuhash` links an API 21 library with only a GNU hash
table, which an API 21 or 22 device cannot load.

Per case (+ is faster than `-O3`, in percent; spread is (max - min) / median
of the five `-O3` rounds):

| case | `O3` ns/op | spread | `O2` | `Os` | `Oz` | `Oz-noout` | `hybridn-O3` | `hybridnw-O3` | `thinlto-O3` | `outliner-O3` | `vscript` | `api23-aps2` | `bestnw21` | `bestnw23` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 5.0 | 0.2% | -4.1 | -14.3 | -29.6 | -29.4 | -0.1 | -0.1 | -0.2 | -14.4 | -0.1 | -0.1 | -0.2 | -0.1 |
| `function_call` | 48.2 | 0.3% | -8.8 | -20.2 | -31.2 | -30.8 | -3.4 | -0.1 | -1.6 | -22.3 | -3.1 | +0.0 | +0.0 | -0.2 |
| `method_call` | 106.8 | 2.4% | -12.1 | -13.0 | -40.4 | -26.3 | -1.2 | -0.9 | +10.2 | -40.6 | -3.3 | -2.0 | -2.1 | -1.9 |
| `table_array_insert` | 28.4 | 1.1% | -4.0 | -13.1 | -37.1 | -20.8 | +0.4 | +0.3 | -1.4 | -2.9 | -0.7 | +0.3 | -0.2 | +0.4 |
| `table_array_read` | 41.5 | 0.3% | -33.1 | -36.3 | -36.5 | -35.7 | -5.7 | -4.7 | +1.0 | -33.5 | -6.4 | -0.0 | -4.9 | -2.0 |
| `table_hash_set_get` | 86.9 | 8.7% | -6.8 | -19.5 | -38.8 | -24.5 | -2.6 | +0.4 | +7.3 | -35.5 | -2.0 | +2.0 | +1.0 | +0.4 |
| `string_concat` | 967.7 | 4.1% | -0.6 | -4.8 | -10.1 | -8.1 | -0.0 | +0.2 | +2.3 | -4.7 | -2.8 | -0.0 | +0.4 | +0.7 |
| `string_format` | 1,041 | 3.4% | -1.8 | -10.2 | -24.0 | -12.0 | -6.0 | -3.0 | +11.3 | -17.6 | -0.3 | -3.6 | +1.5 | -0.0 |
| `closure_create_call` | 695.7 | 0.8% | -4.2 | -4.8 | -8.9 | -8.2 | -0.8 | -1.2 | +1.8 | -7.0 | -0.5 | -0.2 | -0.9 | +0.3 |
| `int_math` | 26.7 | 0.3% | -15.1 | -31.5 | -43.7 | -38.2 | -0.5 | +0.2 | -0.0 | -51.9 | -0.5 | -0.3 | -0.1 | -0.6 |
| `int64_math` | 29.4 | 40.0% | -5.0 | -28.4 | -38.3 | -36.2 | +1.6 | +1.7 | +3.3 | -46.2 | +0.4 | +0.3 | +1.7 | +0.3 |
| `fib_recursive` | 24,632 | 3.9% | -10.2 | -18.0 | -26.4 | -20.0 | +0.4 | +1.4 | +6.1 | -22.0 | +0.8 | +0.5 | +0.8 | -1.2 |
| `float_math` | 57.0 | 1.3% | -6.8 | -23.1 | -36.1 | -31.6 | +0.1 | -0.5 | +6.3 | -32.9 | -0.2 | +0.1 | +4.0 | +3.9 |
| `string_pattern` | 6,253 | 0.5% | -13.3 | -16.3 | -26.1 | -19.0 | -17.4 | +0.2 | +3.5 | -10.0 | +2.1 | +0.2 | +0.1 | +2.1 |
| `table_sort` | 150,290 | 2.3% | -3.5 | -12.5 | -34.7 | -18.5 | -2.8 | +0.5 | +9.5 | -27.0 | +4.0 | +0.2 | +4.4 | +3.8 |
| `table_concat` | 2,938 | 0.7% | -0.0 | -2.2 | -27.9 | -3.6 | -3.5 | +0.6 | +29.3 | -40.6 | +4.2 | -0.7 | +4.0 | +3.5 |
| `coroutine_switch` | 320.8 | 3.9% | -1.1 | -5.1 | -24.1 | -17.0 | +2.6 | +2.1 | +13.7 | -17.8 | +4.4 | +3.0 | +3.6 | +4.0 |
| `gc_small_tables` | 1,052 | 0.7% | -2.6 | -4.9 | -10.5 | -7.6 | -0.3 | -0.1 | +2.7 | -7.3 | +0.2 | -0.3 | -0.3 | +0.3 |
| `oop_inherit` | 122.2 | 0.1% | -6.6 | -7.9 | -36.2 | -27.2 | -0.0 | +0.6 | +18.0 | -40.1 | -0.4 | +3.1 | +0.6 | +3.1 |
| `c_lua_calls_c` | 75.0 | 0.1% | -2.5 | -12.3 | -28.7 | -24.2 | -1.7 | -2.7 | +7.1 | -24.1 | -0.7 | +1.2 | -0.1 | -4.4 |
| `c_c_calls_lua` | 123.0 | 0.3% | -0.4 | -18.1 | -38.6 | -23.4 | +0.0 | +0.1 | +2.6 | -30.3 | +1.2 | +0.0 | +1.2 | +1.1 |
| `c_table_rawgeti` | 28.7 | 0.6% | +1.2 | -14.3 | -28.7 | -17.2 | +1.3 | -0.5 | +17.9 | -15.4 | +6.1 | +0.1 | +5.9 | +6.3 |
| `c_table_pgettable` | 207.7 | 1.3% | -3.3 | -10.7 | -36.7 | -19.9 | -2.2 | -2.6 | +35.9 | -27.3 | +4.3 | -0.0 | -0.6 | +0.3 |
| `c_push_csobj` | 369.0 | 0.7% | -2.1 | -5.8 | -12.3 | -7.5 | +0.2 | -0.8 | +10.5 | -8.8 | +1.7 | +0.1 | +0.9 | +1.1 |
| `c_compile` | 8,924,453 | 1.9% | -2.1 | -4.7 | -18.3 | -9.1 | -7.2 | -7.4 | +3.6 | -17.5 | +1.2 | -0.3 | -7.5 | -6.3 |
| `c_env_create` | 159,504 | 1.0% | +0.8 | -2.6 | -11.4 | -6.5 | +0.9 | -0.2 | +6.5 | -7.7 | +1.5 | +0.8 | +1.7 | +1.5 |
| **geomean** | | | **-6.0** | **-14.1** | **-29.0** | **-20.7** | **-1.9** | **-0.7** | **+7.6** | **-24.6** | **+0.4** | **+0.2** | **+0.5** | **+0.6** |

### Layout controls

Each case's result was identical in every variant. The rounds agree within 1%
for most cases, so the remaining differences between builds come from the
builds. Some of them come only from where the linker puts the hot code: the
interpreter loop is sensitive to its alignment, and lld places functions on
4-byte boundaries. `O3-shuf1` to `O3-shuf4` link the `-O3` objects with
`--shuffle-sections` (three rounds each, a separate run):

| case | `O3` ns/op | spread | `O3-shuf1` | `O3-shuf2` | `O3-shuf3` | `O3-shuf4` | `bestnw23` | `bestnw23-shuf1` | `bestnw23-shuf2` | `bestnw21` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 5.0 | 0.1% | -0.1 | -0.2 | -0.0 | +0.0 | -0.1 | -0.1 | -0.1 | -0.0 |
| `function_call` | 48.2 | 0.1% | -0.8 | +0.1 | +0.1 | -0.1 | -0.1 | +0.1 | -4.2 | +0.0 |
| `method_call` | 106.8 | 1.9% | +0.0 | +1.2 | -1.9 | +0.4 | -0.0 | -0.3 | -2.5 | -0.8 |
| `table_array_insert` | 28.3 | 1.4% | +0.2 | -0.1 | -0.3 | -0.0 | -0.2 | -0.6 | -1.5 | -1.0 |
| `table_array_read` | 41.5 | 0.2% | -0.0 | -1.8 | -1.9 | -1.8 | -1.8 | +0.0 | -5.8 | -4.9 |
| `table_hash_set_get` | 86.0 | 9.5% | +0.4 | -0.3 | -0.8 | +1.6 | +0.2 | -0.2 | -1.4 | +0.5 |
| `string_concat` | 999.9 | 3.5% | +2.3 | +2.1 | +2.4 | +3.0 | +4.0 | +3.1 | +3.0 | +3.7 |
| `string_format` | 1,069 | 3.4% | -1.8 | -7.2 | -3.1 | +0.5 | +5.1 | -1.3 | -0.7 | +2.5 |
| `closure_create_call` | 696.7 | 0.3% | -0.9 | -0.8 | -0.6 | -0.1 | +0.7 | -0.2 | -0.4 | -1.0 |
| `int_math` | 26.7 | 0.6% | -0.5 | -0.4 | +0.1 | -0.3 | -0.3 | +0.1 | +0.1 | +0.1 |
| `int64_math` | 29.4 | 39.8% | +0.2 | +0.1 | +0.2 | +0.1 | -0.0 | +0.2 | -13.4 | +1.6 |
| `fib_recursive` | 23,733 | 3.4% | -3.8 | -3.4 | -4.1 | -3.6 | -3.7 | -3.2 | -2.5 | -2.6 |
| `float_math` | 57.0 | 0.0% | +0.0 | +3.8 | +3.8 | +3.9 | +3.8 | -0.1 | +0.0 | +3.9 |
| `string_pattern` | 6,224 | 0.5% | -0.3 | -2.0 | -2.4 | +0.3 | +1.7 | -1.8 | -0.2 | -0.7 |
| `table_sort` | 149,953 | 0.8% | +0.5 | +0.2 | -0.2 | -0.0 | +3.8 | +3.4 | +2.8 | +4.4 |
| `table_concat` | 2,944 | 0.7% | -0.2 | +0.3 | -0.1 | -0.1 | +3.6 | +2.4 | +0.1 | +4.1 |
| `coroutine_switch` | 324.0 | 0.2% | +3.0 | +4.3 | +4.8 | +4.9 | +5.2 | +4.1 | +6.0 | +5.0 |
| `gc_small_tables` | 1,052 | 0.3% | +0.1 | +0.3 | -0.2 | -0.2 | +0.3 | +0.1 | -0.1 | -0.3 |
| `oop_inherit` | 122.2 | 3.0% | +3.1 | +3.5 | +0.3 | +1.3 | +3.0 | +1.0 | +3.0 | +0.7 |
| `c_lua_calls_c` | 75.0 | 0.2% | +1.6 | +0.6 | +1.1 | -0.1 | +3.8 | +3.4 | +0.3 | -0.0 |
| `c_c_calls_lua` | 122.6 | 0.3% | -0.2 | -1.2 | -0.2 | -0.2 | +0.8 | +0.8 | +0.9 | +0.8 |
| `c_table_rawgeti` | 28.7 | 0.7% | +1.3 | +1.7 | +1.2 | +1.5 | +6.1 | +4.8 | +5.7 | +5.9 |
| `c_table_pgettable` | 207.9 | 0.1% | +1.4 | +0.5 | +0.3 | +0.8 | +0.8 | -6.1 | +1.0 | -0.4 |
| `c_push_csobj` | 369.9 | 0.8% | -0.4 | +0.9 | +0.6 | +0.2 | +1.2 | +0.0 | +0.7 | +1.2 |
| `c_compile` | 8,863,438 | 2.7% | -4.4 | -5.0 | -5.3 | -4.1 | -6.5 | -11.6 | -11.9 | -7.9 |
| `c_env_create` | 158,928 | 0.7% | -1.1 | -0.3 | -1.0 | -0.4 | +2.1 | +0.5 | +0.5 | +1.4 |
| **geomean** | | | **-0.0** | **-0.1** | **-0.3** | **+0.3** | **+1.2** | **-0.1** | **-0.9** | **+0.6** |

So a single case moving by up to 5 or 7% (`fib_recursive`, `float_math`,
`coroutine_switch`, `string_format`, `c_compile`) is within the layout noise of
`-O3` itself, and a geomean within ±0.3% is no change.

Some cases also switch between two speeds from one process to the next, in
every build, probably with the addresses that ASLR picks: `int64_math` of `O3`
ran at 29 or 41 ns in 2 of 11 processes, and `c_lua_calls_c` of `bestnw23` at
72 or 89 ns in 6 of 11. A median of three or five rounds hides the slow mode
when it is rare and shows it when it is common, which is why `bestnw23`'s
`c_lua_calls_c` reads -4.4% in the main run, +3.8% here and -15.1% in the next
table. `bestnwc21` and `bestnwc23` did not show a slow mode in their three
processes.

Shuffled sections also compress 13 KB worse: lld's default order keeps each
file's code together.

### Compiler and loader at `-O3`

`bestnwc21` and `bestnwc23` add `llex.c`, `lparser.c`, `lcode.c`, `lundump.c`
and `lzio.c` to the `-O3` files (`HOT_FILES_LOAD`). A separate run, three
rounds:

| case | `O3` ns/op | spread | `bestnw23` | `bestnwc21` | `bestnwc23` |
| --- | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 5.0 | 0.2% | +0.1 | -0.0 | +0.1 |
| `function_call` | 48.1 | 0.5% | -0.0 | -0.1 | -0.9 |
| `method_call` | 108.9 | 2.0% | -0.0 | +1.0 | -0.8 |
| `table_array_insert` | 28.4 | 0.7% | +0.2 | +0.2 | -0.4 |
| `table_array_read` | 41.5 | 0.1% | -1.9 | -1.8 | +0.1 |
| `table_hash_set_get` | 86.6 | 1.5% | +0.4 | -0.1 | +1.2 |
| `string_concat` | 968.6 | 0.4% | +0.5 | +0.7 | +0.1 |
| `string_format` | 1,085 | 4.4% | +4.2 | +4.5 | +2.2 |
| `closure_create_call` | 695.7 | 0.1% | +0.1 | +0.1 | -0.4 |
| `int_math` | 26.8 | 0.7% | +0.2 | +0.2 | +0.6 |
| `int64_math` | 29.3 | 0.2% | -0.2 | -0.1 | -0.1 |
| `fib_recursive` | 23,732 | 3.3% | -3.8 | -3.7 | -2.8 |
| `float_math` | 57.0 | 1.2% | +3.9 | +3.8 | +0.0 |
| `string_pattern` | 6,242 | 0.5% | +1.9 | +0.6 | +1.8 |
| `table_sort` | 150,102 | 0.7% | +4.1 | +4.1 | +3.5 |
| `table_concat` | 2,941 | 0.7% | +3.4 | +4.7 | +3.6 |
| `coroutine_switch` | 317.8 | 4.6% | +2.9 | +2.9 | +3.8 |
| `gc_small_tables` | 1,051 | 0.3% | -0.0 | -0.1 | +0.1 |
| `oop_inherit` | 118.4 | 3.4% | -0.1 | -1.1 | -0.7 |
| `c_lua_calls_c` | 75.0 | 0.1% | -15.1 | +1.8 | +0.6 |
| `c_c_calls_lua` | 122.9 | 0.4% | +1.1 | -1.9 | +1.0 |
| `c_table_rawgeti` | 28.8 | 0.6% | +6.7 | +6.4 | +6.3 |
| `c_table_pgettable` | 207.6 | 0.2% | +0.6 | -0.8 | +1.0 |
| `c_push_csobj` | 369.1 | 0.6% | +0.9 | -0.1 | +1.6 |
| `c_compile` | 8,928,901 | 1.1% | -5.9 | +1.4 | -0.5 |
| `c_env_create` | 158,488 | 1.3% | +1.9 | +0.8 | +0.8 |
| **geomean** | | | **+0.1** | **+0.9** | **+0.8** |

### Profile

`flag_sweep.py profile` recorded the `-O3` library with the phone's
`simpleperf` (cpu-clock, all cases at scale 0.3). Share of the samples in
`libxlua.so`:

| file | share | | file | share |
| --- | ---: | --- | --- | ---: |
| `lvm.c` | 37.3% | | `lauxlib.c` | 3.5% |
| `lstrlib.c` | 17.0% | | `lstring.c` | 2.2% |
| `lapi.c` | 14.0% | | `ltablib.c` | 1.3% |
| `ldo.c` | 8.9% | | `lmem.c` | 1.2% |
| `ltable.c` | 5.3% | | `lfunc.c` | 0.7% |
| `lgc.c` | 4.3% | | `lcorolib.c`, `xlua.c`, `ltm.c`, `lobject.c` | 1.2% |

3.2% of the samples have no symbol; 2.8% are in the PLT: with the default
exports, calls between exported functions (`luaL_*` calling `lua_*`) go through
the PLT, which the version script and hidden visibility remove (`vscript` and
`hidden` are 0.4% faster). With only the 8 VM files at `-O3`, `string_pattern`
loses 17%, `string_format` 6% and `table_sort` 3%, which is why `bestnw*` adds
the five library files.

## Android armeabi-v7a

| variant | stripped | gzip -9 | APK (deflate) | speed, geomean | largest loss |
| --- | ---: | ---: | ---: | ---: | --- |
| `O3` | 273,144 (0) | 148,493 (0) | 148,538 (0) | | |
| `O2` | 265,868 (-7,276) | 143,977 (-4,516) | 144,023 (-4,515) | | |
| `Os` | 232,096 (-41,048) | 126,182 (-22,311) | 126,209 (-22,329) | | |
| `Oz` | 222,452 (-50,692) | 119,918 (-28,575) | 119,940 (-28,598) | -5.9% | `c_table_pgettable` -25.5% |
| `Oz-noout` | 222,452 (-50,692) | 119,917 (-28,576) | 119,938 (-28,600) | | |
| `hybrid-O3` | 239,476 (-33,668) | 129,475 (-19,018) | 129,513 (-19,025) | | |
| `hybridn-O3` | 239,476 (-33,668) | 129,476 (-19,017) | 129,513 (-19,025) | | |
| `hybridn-O2` | 235,972 (-37,172) | 127,392 (-21,101) | 127,414 (-21,124) | | |
| `hybridnw-O3` | 246,040 (-27,104) | 133,645 (-14,848) | 133,675 (-14,863) | -0.1% | `c_compile` -5.0% |
| `thinlto-O3` | 384,464 (+111,320) | 210,165 (+61,672) | 210,345 (+61,807) | | |
| `fulllto-hybrid` | 264,568 (-8,576) | 145,458 (-3,035) | 145,489 (-3,049) | | |
| `outliner-O3` | 262,392 (-10,752) | 154,500 (+6,007) | 154,529 (+5,991) | | |
| `icf` | 272,520 (-624) | 148,391 (-102) | 148,445 (-93) | | |
| `icf-safe` | 273,144 (0) | 148,494 (+1) | 148,541 (+3) | | |
| `vscript` | 255,228 (-17,916) | 141,812 (-6,681) | 141,873 (-6,665) | | |
| `hidden` | 271,460 (-1,684) | 147,710 (-783) | 147,760 (-778) | | |
| `api23` | 269,888 (-3,256) | 147,334 (-1,159) | 147,372 (-1,166) | | |
| `api24` | 269,840 (-3,304) | 147,393 (-1,100) | 147,422 (-1,116) | | |
| `api23-aps2` | 264,008 (-9,136) | 146,019 (-2,474) | 146,049 (-2,489) | | |
| `api24-aps2` | 263,960 (-9,184) | 146,032 (-2,461) | 146,055 (-2,483) | | |
| `api28-relr` | 263,988 (-9,156) | 146,048 (-2,445) | 146,069 (-2,469) | | |
| `bestn21` | 221,172 (-51,972) | 122,478 (-26,015) | 122,521 (-26,017) | | |
| `bestn23` | 212,948 (-60,196) | 120,446 (-28,047) | 120,465 (-28,073) | | |
| `bestnw21` | 227,768 (-45,376) | 126,483 (-22,010) | 126,509 (-22,029) | +0.9% | `c_compile` -4.7% |
| `bestnw23` | 219,656 (-53,488) | 124,580 (-23,913) | 124,598 (-23,940) | +0.8% | `c_compile` -4.0% |
| `bestnw24` | 219,624 (-53,520) | 124,578 (-23,915) | 124,588 (-23,950) | | |
| `bestnwc21` | 240,392 (-32,752) | 134,416 (-14,077) | 134,461 (-14,077) | | |
| `bestnwc23` | 232,264 (-40,880) | 132,264 (-16,229) | 132,282 (-16,256) | +1.1% | `method_call` -1.8% |

Speed on the same phone in 32-bit mode, three rounds:

| case | `O3` ns/op | spread | `Oz` | `hybridnw-O3` | `bestnw21` | `bestnw23` | `bestnwc23` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 7.1 | 2.7% | +4.9 | +0.9 | +1.4 | +1.4 | +1.4 |
| `function_call` | 55.2 | 0.5% | -3.5 | +0.3 | -0.1 | -0.4 | -0.4 |
| `method_call` | 131.4 | 2.9% | -8.7 | -2.0 | -1.8 | -1.7 | -1.8 |
| `table_array_insert` | 33.7 | 0.9% | -1.3 | +0.2 | +0.6 | +0.9 | +1.3 |
| `table_array_read` | 49.6 | 0.7% | -1.6 | -0.2 | -0.5 | +0.0 | -0.1 |
| `table_hash_set_get` | 108.2 | 2.1% | -1.1 | -0.1 | +0.4 | +2.7 | +0.1 |
| `string_concat` | 1,239 | 0.5% | -2.6 | +0.2 | +0.4 | +0.6 | +0.8 |
| `string_format` | 1,211 | 0.2% | -7.7 | +0.9 | +3.0 | +0.8 | +4.2 |
| `closure_create_call` | 782.6 | 0.2% | -4.0 | +0.4 | +0.3 | +0.5 | +0.8 |
| `int_math` | 65.0 | 0.5% | -2.9 | +0.2 | -0.1 | +0.1 | -0.1 |
| `int64_math` | 34.9 | 0.9% | +2.4 | +0.4 | +0.2 | +0.9 | +0.5 |
| `fib_recursive` | 25,622 | 1.1% | -3.1 | +0.2 | -0.3 | -0.0 | -0.4 |
| `float_math` | 68.7 | 1.1% | +6.6 | +0.9 | +0.8 | +0.6 | +0.7 |
| `string_pattern` | 6,760 | 0.6% | -14.2 | +0.4 | +0.5 | +0.6 | -1.1 |
| `table_sort` | 198,731 | 1.5% | -15.3 | -1.0 | +4.5 | +5.4 | +2.4 |
| `table_concat` | 3,556 | 0.6% | +2.3 | -0.7 | +4.5 | -1.7 | +4.1 |
| `coroutine_switch` | 333.4 | 4.2% | -9.5 | +1.6 | +3.7 | +3.8 | +3.4 |
| `gc_small_tables` | 1,235 | 0.6% | -3.4 | +0.1 | -0.4 | -0.1 | -0.5 |
| `oop_inherit` | 146.8 | 0.8% | -9.1 | +2.4 | -0.4 | +0.1 | +0.2 |
| `c_lua_calls_c` | 99.1 | 0.3% | -8.7 | -0.5 | +3.1 | +3.0 | +3.0 |
| `c_c_calls_lua` | 152.9 | 0.3% | -5.0 | +0.0 | +2.4 | +2.5 | +2.3 |
| `c_table_rawgeti` | 38.4 | 2.0% | -8.8 | -0.3 | +6.4 | +5.9 | +6.2 |
| `c_table_pgettable` | 213.7 | 1.0% | -25.5 | -3.1 | -1.6 | -2.5 | -1.7 |
| `c_push_csobj` | 395.7 | 0.4% | -6.2 | +0.6 | +1.8 | +0.8 | +2.1 |
| `c_compile` | 13,178,977 | 1.1% | -8.4 | -5.0 | -4.7 | -4.0 | +0.8 |
| `c_env_create` | 190,171 | 2.2% | -11.7 | +0.1 | +1.0 | +1.2 | +0.7 |
| **geomean** | | | **-5.9** | **-0.1** | **+0.9** | **+0.8** | **+1.1** |

On ARM32 `-Oz` does not turn the outliner on, so `Oz` and `Oz-noout` are the
same build. Thumb-2 `-Oz` code is only 5.9% slower than `-O3` here (29% on
arm64-v8a), but `c_table_pgettable` loses 26% and `string_pattern` and
`table_sort` 14 to 15%, so the per-file split still applies.

## Little cores

The phone's cpu0 to cpu3 are Kryo Silver cores (Cortex-A53 class, in order,
1.9 GHz). Phones run background work and some game threads there, so the
same six builds were timed on them as well: `O3`, `Oz`, `hybrid-O3` (the 8 VM
files at `-O3`), `bestnwc23`, `bestnw23` and `thinlto-O3`, rebuilt on
2026-10-17 from the sources after `build/xlua.c` and `i64lib.c` changed (sizes
differ from the tables above by up to 96 bytes). The runner pinned itself with
`--cpus 0,1,2,3`. Five interleaved rounds on the little cores, then five on the
big cores with the same libraries, then five for armeabi-v7a on the little
cores. "Earlier run" is the big-core result from the tables above.

arm64-v8a (+ is faster than `-O3`; geomean over the 26 cases):

| variant | stripped | APK (deflate) | little cores | little, largest loss | big cores, same build | big cores, earlier run |
| --- | ---: | ---: | ---: | --- | ---: | ---: |
| `O3` | 418,352 | 169,411 | | | | |
| `Oz` | 333,848 (-84,504) | 147,076 (-22,335) | -19.7% | `table_sort` -31.1% | -29.2% | -29.0% |
| `hybrid-O3` | 364,008 (-54,344) | 156,727 (-12,684) | -4.0% | `string_pattern` -21.7% | -3.2% | -2.5% |
| `bestnwc23` | 346,480 (-71,872) | 150,635 (-18,776) | +0.1% | `gc_small_tables` -4.7% | +0.7% | +0.8% |
| `bestnw23` | 327,904 (-90,448) | 142,503 (-26,908) | +1.2% | `c_compile` -3.7% | +0.7% | +0.6% |
| `thinlto-O3` | 577,104 (+158,752) | 230,657 (+61,246) | +7.0% | `fib_recursive` -2.7% | +7.0% | +7.6% |

The little cores take 2.6 times as long as the big cores for the same `-O3`
library (geomean of the cases). `-Oz` hurts less there (-20% against -29%)
because the in-order core is slower everywhere, but the per-file split keeps
its order: `hybrid-O3` loses 22% on `string_pattern` and 19% on
`c_table_pgettable`, cases that run through `lstrlib.c` and `xlua.c`, both
`-Oz` in that build, and
`bestnwc23` and `bestnw23` match `-O3`.

The rounds on the little cores spread more: the median (max - min) / median of
a case is 4.0% against 0.9% on the big cores, because Android schedules other
work on cpu0 to cpu3 too. A layout control on the little cores (five more
rounds, `O3-shuf1` and `O3-shuf2` as in "Layout controls") moved the geomean
by -0.8% and single cases by up to -12% (`coroutine_switch`). In that run
`bestnwc23` was +1.2% and its largest loss `c_compile` -3.1%; its
`gc_small_tables` loss of 4.7% above came back as +1%, so that case is noise.

Per case, arm64-v8a on the little cores (five rounds):

| case | `O3` ns/op | spread | `Oz` | `hybrid-O3` | `bestnwc23` | `bestnw23` | `thinlto-O3` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 13.7 | 2.0% | -7.0 | +0.1 | -0.9 | -0.1 | +1.3 |
| `function_call` | 124.9 | 2.0% | -18.0 | -0.2 | -1.1 | +0.3 | -2.4 |
| `method_call` | 272.4 | 11.5% | -21.0 | -0.9 | -1.7 | -0.5 | +6.1 |
| `table_array_insert` | 69.3 | 3.2% | -20.3 | -1.5 | -2.2 | -1.5 | +0.6 |
| `table_array_read` | 104.4 | 2.5% | -22.2 | -2.0 | -1.3 | -1.2 | -0.0 |
| `table_hash_set_get` | 217.4 | 4.2% | -22.2 | -1.4 | -0.9 | -1.0 | +5.5 |
| `string_concat` | 2,757 | 5.1% | -6.5 | -0.7 | +1.4 | +3.9 | +8.0 |
| `string_format` | 3,045 | 9.6% | -20.1 | -14.1 | +0.6 | +0.3 | +15.4 |
| `closure_create_call` | 1,934 | 2.3% | -6.4 | -5.0 | -4.3 | -2.2 | -0.6 |
| `int_math` | 72.3 | 2.9% | -25.0 | -0.0 | +0.1 | +0.4 | +1.7 |
| `int64_math` | 70.8 | 2.5% | -28.9 | -1.0 | -1.8 | -0.2 | +0.4 |
| `fib_recursive` | 55,546 | 2.0% | -20.3 | -2.5 | -2.8 | -0.5 | -2.7 |
| `float_math` | 150.7 | 2.1% | -22.8 | +2.6 | +1.5 | +3.3 | +1.2 |
| `string_pattern` | 15,654 | 0.9% | -24.0 | -21.7 | +2.3 | +3.0 | +6.4 |
| `table_sort` | 395,124 | 3.4% | -31.1 | -7.7 | +0.8 | +3.7 | +9.9 |
| `table_concat` | 8,291 | 1.1% | -18.0 | -6.9 | +2.5 | +5.0 | +29.7 |
| `coroutine_switch` | 676.6 | 2.2% | -27.8 | -7.7 | +1.4 | +0.7 | +10.7 |
| `gc_small_tables` | 2,982 | 3.4% | -7.8 | -0.7 | -4.7 | -0.5 | -2.4 |
| `oop_inherit` | 312.8 | 4.1% | -18.9 | +0.6 | +0.8 | +2.6 | +14.5 |
| `c_lua_calls_c` | 192.0 | 2.5% | -27.1 | -1.0 | +1.5 | +3.4 | +10.3 |
| `c_c_calls_lua` | 331.4 | 3.2% | -19.6 | +3.0 | +5.6 | +3.1 | +9.7 |
| `c_table_rawgeti` | 68.7 | 1.5% | -27.7 | -2.5 | +2.2 | +4.5 | +15.1 |
| `c_table_pgettable` | 469.7 | 12.6% | -29.1 | -18.9 | +2.8 | +3.7 | +29.0 |
| `c_push_csobj` | 1,058 | 7.2% | -4.3 | -1.3 | +3.2 | +4.4 | +11.8 |
| `c_compile` | 27,794,604 | 11.2% | -12.1 | -6.0 | -0.9 | -3.7 | +3.0 |
| `c_env_create` | 463,088 | 3.3% | -13.7 | -2.1 | +0.3 | +0.6 | +7.8 |
| **geomean** | | | **-19.7** | **-4.0** | **+0.1** | **+1.2** | **+7.0** |

armeabi-v7a on the little cores (five rounds; the big-core column is the
three-round armeabi-v7a run above, which did not include `hybrid-O3` and
`thinlto-O3`):

| variant | stripped | APK (deflate) | little cores | little, largest loss | big cores, earlier run |
| --- | ---: | ---: | ---: | --- | ---: |
| `O3` | 273,208 | 148,547 | | | |
| `Oz` | 222,516 (-50,692) | 119,952 (-28,595) | -7.0% | `c_table_pgettable` -27.5% | -5.9% |
| `hybrid-O3` | 239,556 (-33,652) | 129,524 (-19,023) | -3.6% | `table_sort` -14.9% | |
| `bestnwc23` | 232,344 (-40,864) | 132,314 (-16,233) | +2.2% | `c_table_pgettable` -6.9% | +1.1% |
| `bestnw23` | 219,736 (-53,472) | 124,627 (-23,920) | +1.0% | `c_table_pgettable` -7.3% | +0.8% |
| `thinlto-O3` | 384,544 (+111,336) | 210,420 (+61,873) | +4.6% | `c_compile` -1.6% | |

`c_table_pgettable` (`xlua_pgettable`, a protected `lua_gettable`) is the one
case that `bestnwc23` loses on both 32-bit runs (-1.7% on the big cores, -6.9%
here, with a 5.2% spread); `xlua_pgettable` is in `xlua.c`, which `bestnw*`
builds at `-Oz`. On arm64-v8a the same case is +2.8% on the little cores.
"`xlua.c` at `-O3`" below measures moving `xlua.c` to the `-O3` list.

Pinning held. The runner records the CPU it is on at the end of every timed run
(`sched_getcpu`) and its allowed set (`sched_getaffinity`), and `flag_sweep.py`
reads `Cpus_allowed_list` and the last CPU from `/proc/<pid>/stat` while it
runs: every little-core run reported `0-3` and only CPUs 0 to 3, on both ABIs.
A `simpleperf record` of the runner with `--cpus 0,1,2,3` put all 3,401
samples on cpu0 to cpu3 and none on cpu4 to cpu7 (`simpleperf report --cpu`).
On the big cores the allowed list read `4-7` or a subset such as `4,6` while
the kernel had paused a core, and the runs stayed on CPUs 4 to 7. The governors stayed `walt`
on both clusters and the thermal status 0; the CPU frequency was not sampled
during the timed runs.

So the recommendation holds on the little cores: `bestnwc23` is as fast as
`-O3` within the noise on arm64-v8a (+0.1% and +1.2% in two runs) and faster
on armeabi-v7a (+2.2%), apart from the `xlua_pgettable` case on 32-bit.

## `xlua.c` at `-O3`

`bestnwc23-xluaO3` is `bestnwc23` with `build/xlua.c` added to the `-O3` list
(19 files). It was built on 2026-10-17 together with `O3`, `bestnwc23` and
`bestnw23` from the same sources (sizes differ from the tables above by up to
560 bytes), and timed in five interleaved rounds per run: armeabi-v7a on the
big cores (`--cpus 4,5,6,7`), armeabi-v7a on the little cores (`--cpus
0,1,2,3`) and arm64-v8a on the big cores. Every case returned the same result
in every variant and round.

| ABI | variant | stripped | APK (deflate) | geomean vs `-O3` | `c_table_pgettable` | largest loss |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| armeabi-v7a | `O3` | 273,656 | 148,891 | | 213.6 ns big, 534.7 ns little | |
| | `bestnwc23` | 232,664 (-40,992) | 132,540 (-16,351) | big +0.7%, little +0.5% | big -8.4%, little -11.1% | `c_table_pgettable` |
| | `bestnwc23-xluaO3` | 233,008 (-40,648) | 132,848 (-16,043) | big +0.7%, little +0.7% | big -5.2%, little -3.4% | `c_table_pgettable` |
| | `bestnw23` | 220,040 (-53,616) | 124,837 (-24,054) | big +0.8%, little +0.0% | big -2.3%, little -8.7% | big `c_compile` -5.1%, little `c_table_pgettable` |
| arm64-v8a | `O3` | 418,912 | 169,651 | | 205.6 ns big | |
| | `bestnwc23` | 346,896 (-72,016) | 150,845 (-18,806) | big +0.7% | big -2.0% | `c_table_pgettable` -2.0% |
| | `bestnwc23-xluaO3` | 349,048 (-69,864) | 151,319 (-18,332) | big +0.7% | big +3.1% | `float_math` -3.7% (`O3` spread 13%) |
| | `bestnw23` | 328,320 (-90,592) | 142,700 (-26,951) | big -0.3% | big -3.0% | `c_compile` -6.4% |

`xlua.c` at `-O3` costs 344 bytes stripped and 308 bytes of APK on
armeabi-v7a, and 2,152 and 474 bytes on arm64-v8a. The geomean does not move
in any of the three runs, and no other case changes outside the layout noise
of the earlier sections.

The rounds of `c_table_pgettable` on the big cores agree within 1%, so the
-8.4% of `bestnwc23` is a property of the build, not noise. It is not the
`-Oz` code alone: `bestnw23` compiles `xlua_pgettable` to the same 88 bytes
of `-Oz` code and loses only 2.3%, and the earlier `bestnwc23` build from
older sources lost 1.7%. A layout control separates the two. It linked
`O3`, `bestnwc23` and `bestnwc23-xluaO3` with `--shuffle-sections` (seeds 1
to 3, two for `O3`) and ran only `c_table_rawgeti` and `c_table_pgettable`,
five rounds on each cluster (`c_table_pgettable` vs `-O3` in percent; the
first column is the normal link):

| armeabi-v7a | normal | shuf1 | shuf2 | shuf3 |
| --- | ---: | ---: | ---: | ---: |
| big: `O3` | 0 (213.1 ns) | +0.4 | -0.0 | |
| big: `bestnwc23` | -8.3 | -2.1 | -2.4 | -2.2 |
| big: `bestnwc23-xluaO3` | -5.5 | +1.4 | +0.4 | +1.1 |
| little: `O3` | 0 (541.7 ns) | -1.2 | -3.4 | |
| little: `bestnwc23` | -8.4 | -7.7 | -9.8 | -11.9 |
| little: `bestnwc23-xluaO3` | -2.2 | -3.9 | -0.5 | -13.4 |

On the big cores the normal link of both builds is an unlucky layout for
this case (about 6 points worse than any shuffled link; no alignment pattern
of the functions on the path explains it), and `xlua.c` at `-O3` is about 3
points faster in every layout: -2.2% becomes +1.0% (medians of the shuffled
links). On the little cores `bestnwc23` loses 8 to 12% in all four layouts;
with `xlua.c` at `-O3`, three of four layouts fall inside the band of
`O3`'s own layouts (0 to -3.4%) and one loses 13.4%. The round spread there
is 2 to 9%. `c_table_rawgeti` moved by at most 1.4 points between the two
builds in any layout. At `-Oz`, `xlua_pgettable` calls `push_helper` out of
line, `lua_remove` through the PLT and `xlua.c`'s exported `lua_pcall`
wrapper through a long-branch thunk; at `-O3` all three are inlined and
`lua_pcallk` is a direct call (`llvm-objdump` of the armeabi-v7a builds).

Recommendation for the owner: compile `xlua.c` at `-O3` on both ABIs, that is
`bestnwc23-xluaO3` (and `bestnwc21` with the same list) in place of
`bestnwc23`. On armeabi-v7a it removes most of the `xlua_pgettable` loss on
both clusters for 308 bytes of APK. On arm64-v8a it changes nothing beyond
layout noise (+3.1% against -2.0% on this one layout) for 474 bytes, but one
file list for both ABIs keeps the build simple. Keeping `-Oz` for `xlua.c` on
arm64-v8a only would save those 474 bytes at the cost of a per-ABI list.
Not measured: `bestnwc21` with `xlua.c` at `-O3`, arm64-v8a on the little
cores, and a layout control on arm64-v8a.

## API levels and linker options

What the minimum API level changes in the library (both ABIs, `-O3`):

| | API 21 | API 23 | API 24 | API 28 |
| --- | --- | --- | --- | --- |
| hash table (NDK default) | `--hash-style=both` (`DT_HASH` and `DT_GNU_HASH`) | `gnu` | `gnu` | `gnu` |
| relocations (NDK default) | `RELA` / `REL` | same | same | `--pack-dyn-relocs=relr` (`DT_ANDROID_RELR`) |
| `--pack-dyn-relocs=android` (APS2) | not loadable | loadable | loadable | loadable |
| stdio | `__sF` | `stdin`, `stdout`, `stderr` | same | same |
| armeabi-v7a `io` file offsets | 32-bit `fseeko`/`ftello` (`_FILE_OFFSET_BITS=32` in `CMakeLists.txt`) | same | 64-bit `fseeko64`/`ftello64` | same |
| arm64-v8a stripped size | 418,272 | 415,136 | 415,088 | 397,656 |
| armeabi-v7a stripped size | 273,144 | 269,888 | 269,840 | 263,988 |

API 23 against 24: on arm64-v8a the libraries import the same symbols and
differ by 48 bytes; on armeabi-v7a, API 24 gives `io` 64-bit file offsets
(files over 2 GB) and is 48 bytes smaller. APS2 saves the same at both levels
(arm64-v8a 15,968 bytes stripped, 1,977 in the APK; armeabi-v7a 5,880 and
1,323). RELR (API 28) is only 1.5 KB smaller than APS2 for arm64-v8a and is out
of reach below API 28. None of these changes the speed (`api23`, `api23-aps2`,
`api28-relr` are within ±0.3%).

Other linker options:

- `--icf=all` saves 1,168 bytes (264 compressed). It folds identical
  functions: `lua_pushint64`/`lua_pushuint64`, `lua_isint64`/`lua_isuint64`,
  `lua_toint64`/`lua_touint64`, `xlua_unpack_int32_t`/`xlua_unpack_float`,
  `xlua_unpack_int64_t`/`xlua_unpack_double`, the 8- and 16-bit
  `xlua_struct_set_*` pairs and some LuaSocket helpers. After folding, two of
  those C functions compare equal in Lua. `--icf=safe` folds nothing here.
- The version script (`-Wl,--version-script`, 117 exports instead of 280) saves
  25,720 bytes (8,856 compressed): the symbol tables shrink, and
  `--gc-sections` drops the C API functions nothing references. Native Lua C
  modules loaded at run time could then not link against the Lua API, and
  `General/LuaMemoryLeakChecker` needs its functions added to the list.
- `-fvisibility=hidden` alone saves 2,104 bytes; LuaSocket's internal functions
  stop being exported. It needs `LUA_API` changed in `luaconf.h`, because Lua
  defines `LUA_API` as plain `extern`. With the version script it adds nothing
  to the size.

## iOS arm64

`libxlua.a` compiled with the flags of the Xcode project `make_ios_lua55.sh`
generates (`-O3 -fpascal-strings -fstrict-aliasing`, iOS 15.0); the `-O3`
archive's `__TEXT` equals the release script's 294,891 bytes. "Linked" is the
`__TEXT` the library adds to an executable that references every iOS import of
`LuaDLL.cs` and links with `-dead_strip`, as UnityFramework does.

| variant | archive `__TEXT` | linked `__TEXT` |
| --- | ---: | ---: |
| `O3` | 294,891 (0) | 285,877 (0) |
| `O2` | 275,759 (-19,132) | 267,445 (-18,432) |
| `Os` | 221,772 (-73,119) | 216,135 (-69,742) |
| `Oz` | 193,864 (-101,027) | 190,295 (-95,582) |
| `Oz-noout` | 209,324 (-85,567) | 204,535 (-81,342) |
| `hybrid-O3` | 226,728 (-68,163) | 222,543 (-63,334) |
| `hybrid-O2` | 217,964 (-76,927) | 213,835 (-72,042) |
| `hybridn-O3` | 240,036 (-54,855) | 234,687 (-51,190) |
| `hybridn-O2` | 231,272 (-63,619) | 225,979 (-59,898) |
| `hybridw-O3` | 239,911 (-54,980) | 235,584 (-50,293) |
| `hybridnw-O3` | 251,199 (-43,692) | 245,732 (-40,145) |
| `hybridwc-O3` | 261,681 (-33,210) | 257,096 (-28,781) |
| `outliner-O3` | 257,219 (-37,672) | 250,329 (-35,548) |
| `thinlto-O3` | bitcode | 432,458 (+146,581) |
| `fulllto-O3` | bitcode | 383,861 (+97,984) |
| `fulllto-hybridn` | bitcode | 230,050 (-55,827) |

Apple's `-Oz` also runs the outliner, but Mach-O's compact unwind info makes
outlined code cheap, so on iOS the outliner shrinks the code (`Oz` is 14 KB
smaller than `Oz-noout`), unlike on Android. The LTO rows are for reference
only: an archive of LTO objects holds bitcode that only the same or a newer
clang reads, so a `libxlua.a` built with one Xcode can fail to link in an older
one. iOS speed was not measured; the Android results for the same split suggest
it, but the A-series cores and Apple clang can differ.

## XLUA_HARDEN

`-DXLUA_HARDEN=ON` (off by default; see "Warnings, hardening and test
options" in [README.md](README.md)) measured on 2026-10-17 with the same NDK,
phone and runner as above, Lua 5.5.1, against `O3` (the release build). The
NDK already passes `-fstack-protector-strong` and `-D_FORTIFY_SOURCE=2`, so
on Android the option adds `-fno-delete-null-pointer-checks`,
`-ftrivial-auto-var-init=zero` and, on arm64-v8a only,
`-mbranch-protection=standard`. `O3-bti`, `O3-autoinit` and `O3-nulldel` add
one of them each.

Size:

| ABI | variant | stripped | gzip -9 | APK (deflate) |
| --- | --- | ---: | ---: | ---: |
| arm64-v8a | `O3` | 420,800 | 169,946 | 170,263 |
| arm64-v8a | `O3-harden` | 435,456 (+14,656, +3.5%) | 171,779 (+1,833) | 172,304 (+2,041, +1.2%) |
| arm64-v8a | `O3-bti` | 433,424 (+12,624) | 170,830 (+884) | 171,194 (+931) |
| arm64-v8a | `O3-autoinit` | 422,896 (+2,096) | 170,913 (+967) | 171,193 (+930) |
| arm64-v8a | `O3-nulldel` | 420,992 (+192) | 170,048 (+102) | 170,360 (+97) |
| arm64-v8a | `bestnwc23` | 347,424 | 150,902 | 151,182 |
| arm64-v8a | `bestnwc23-harden` | 359,728 (+12,304) | 152,877 (+1,975) | 153,169 (+1,987) |
| armeabi-v7a | `O3` | 274,944 | 149,455 | 149,509 |
| armeabi-v7a | `O3-harden` | 278,240 (+3,296, +1.2%) | 151,789 (+2,334) | 151,844 (+2,335, +1.6%) |
| armeabi-v7a | `O3-autoinit` | 278,048 (+3,104) | 151,644 (+2,189) | 151,695 (+2,186) |
| armeabi-v7a | `O3-nulldel` | 275,200 (+256) | 149,742 (+287) | 149,799 (+290) |
| armeabi-v7a | `bestnwc23` | 233,136 | 132,837 | 132,850 |
| armeabi-v7a | `bestnwc23-harden` | 235,776 (+2,640) | 134,671 (+1,834) | 134,682 (+1,832) |

`-mbranch-protection` signs the return address in every function that saves
it and puts a `bti` landing pad where an indirect call can arrive (808
`paciasp` and 402 `bti` in `O3-bti`). They compress well: 12.6 KB stripped,
0.9 KB in the APK.

Host and iOS builds (Release `-O3`, code size because the file sizes round to
pages; gzip -9 of the stripped file):

| platform | runtime | code (`__text`/`.text`), plain | code, XLUA_HARDEN | gzip -9 stripped, plain | gzip -9, XLUA_HARDEN |
| --- | --- | ---: | ---: | ---: | ---: |
| macOS arm64, Apple clang 21 | 5.3.6 | 221,480 | 231,464 (+9,984, +4.5%) | 121,260 | 124,772 (+3,512, +2.9%) |
| Linux arm64, GCC 15.2 | 5.3.6 | 256,916 | 263,740 (+6,824, +2.7%) | 163,132 | 165,710 (+2,578, +1.6%) |
| Linux arm64, Clang 21 | 5.3.6 | 217,388 | 239,920 (+22,532, +10.4%) | 136,439 | 142,428 (+5,989, +4.4%) |
| macOS arm64, Apple clang 21 | 5.4.9 | 268,788 | 279,604 (+10,816, +4.0%) | 143,658 | 147,787 (+4,129, +2.9%) |
| Linux arm64, GCC 15.2 | 5.4.9 | 304,548 | 316,428 (+11,880, +3.9%) | 189,470 | 192,459 (+2,989, +1.6%) |
| Linux arm64, Clang 21 | 5.4.9 | 262,496 | 286,816 (+24,320, +9.3%) | 157,999 | 165,245 (+7,246, +4.6%) |
| macOS arm64, Apple clang 21 | 5.5.1 | 282,264 | 293,568 (+11,304, +4.0%) | 146,657 | 151,261 (+4,604, +3.1%) |
| Linux arm64, GCC 15.2 | 5.5.1 | 317,252 | 334,576 (+17,324, +5.5%) | 198,811 | 203,256 (+4,445, +2.2%) |
| Linux arm64, Clang 21 | 5.5.1 | 277,056 | 301,596 (+24,540, +8.9%) | 167,485 | 173,585 (+6,100, +3.6%) |
| macOS arm64, Apple clang 21 | luajit | 522,284 | 526,540 (+4,256, +0.8%) | 314,820 | 315,903 (+1,083, +0.3%) |
| Linux arm64, GCC 15.2 | luajit | 493,500 | 492,700 (-800, -0.2%) | 332,077 | 332,470 (+393, +0.1%) |
| Linux arm64, Clang 21 | luajit | 477,596 | 486,684 (+9,088, +1.9%) | 325,533 | 327,984 (+2,451, +0.8%) |

iOS arm64 (`make_ios_lua55.sh`, Xcode Release, deployment target 15.0):
`__TEXT` of `libxlua.a` 296,661 → 307,983 bytes (+11,322, +3.8%).

Speed on the phone's big cores (`--cpus 4,5,6,7`, five rounds; + is faster
than `O3`). arm64-v8a ran twice: `O3` against `O3-harden` alone, then with
the single flags and a layout control (`O3-shuf1`, the `O3` objects in
another section order):

| ABI | variant | geomean | largest loss |
| --- | --- | ---: | --- |
| arm64-v8a | `O3-harden` (first run) | -3.9% | `c_c_calls_lua` -9.4% |
| arm64-v8a | `O3-harden` (second run) | -4.1% | `c_c_calls_lua` -9.4% |
| arm64-v8a | `O3-bti` | -2.2% | `c_table_rawgeti` -7.6% |
| arm64-v8a | `O3-autoinit` | -1.7% | `c_c_calls_lua` -9.0% |
| arm64-v8a | `O3-nulldel` | -0.3% | `c_table_pgettable` -1.5% |
| arm64-v8a | `O3-shuf1` (layout control) | -0.7% | `c_compile` -10.0% |
| armeabi-v7a | `O3-harden` | -1.7% | `c_c_calls_lua` -13.7% |
| armeabi-v7a | `O3-autoinit` | -1.4% | `c_c_calls_lua` -13.9% |

Per case, second arm64-v8a run and armeabi-v7a:

| case | arm64 `O3` ns/op | `O3-harden` | `O3-bti` | `O3-autoinit` | `O3-nulldel` | `O3-shuf1` | v7a `O3` ns/op | v7a `O3-harden` | v7a `O3-autoinit` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 5.0 | -4.0 | -4.0 | +0.0 | +0.1 | -0.1 | 6.9 | +0.1 | -0.2 |
| `function_call` | 48.3 | -5.5 | -3.1 | -1.5 | +0.2 | -0.6 | 55.1 | -0.2 | -0.1 |
| `method_call` | 108.8 | -4.1 | +0.3 | -2.2 | +0.3 | -0.4 | 134.5 | +0.1 | +2.5 |
| `table_array_insert` | 28.4 | +0.3 | +1.5 | +0.1 | -0.2 | +0.0 | 33.7 | +1.2 | +0.9 |
| `table_array_read` | 43.6 | -2.8 | -1.9 | -0.0 | -0.0 | +5.0 | 49.6 | -0.1 | +0.0 |
| `table_hash_set_get` | 85.6 | -5.7 | -2.8 | -0.8 | -0.4 | +1.5 | 108.2 | +0.5 | +0.7 |
| `string_concat` | 961.7 | -2.5 | -1.3 | -1.3 | -0.1 | -0.9 | 1,236.8 | -1.1 | -1.0 |
| `string_format` | 1,068.8 | -7.8 | -1.3 | -2.5 | +0.7 | -1.9 | 1,207.3 | -2.9 | -2.5 |
| `closure_create_call` | 692.7 | -2.6 | -2.5 | -2.0 | -0.7 | -1.6 | 781.6 | -1.9 | -1.6 |
| `int_math` | 26.6 | -2.5 | -2.6 | -0.2 | -0.3 | -0.5 | 64.9 | -0.3 | -0.4 |
| `int64_math` | 29.2 | -5.8 | -4.3 | +0.0 | +1.3 | -0.2 | 34.8 | +0.3 | +0.5 |
| `fib_recursive` | 24,498 | -0.8 | +2.2 | +0.1 | +0.1 | -0.7 | 25,599 | -0.3 | -0.5 |
| `float_math` | 54.9 | -7.1 | -4.4 | -0.0 | +0.0 | -3.6 | 68.2 | -1.8 | -1.9 |
| `string_pattern` | 6,188 | -3.5 | -3.0 | -1.7 | -0.0 | -1.5 | 6,748 | -1.1 | -0.2 |
| `table_sort` | 149,323 | -1.8 | -2.0 | -0.4 | -0.1 | -0.2 | 197,914 | +2.9 | +3.1 |
| `table_concat` | 2,921.7 | -5.8 | -3.7 | -1.8 | -0.2 | -0.5 | 3,560.7 | +1.0 | +0.9 |
| `coroutine_switch` | 312.7 | -3.7 | -0.7 | -2.7 | -0.7 | +1.5 | 328.3 | -9.0 | -7.0 |
| `gc_small_tables` | 1,052.6 | -1.3 | -0.5 | -0.1 | -0.2 | -0.3 | 1,233.4 | -0.4 | -0.2 |
| `oop_inherit` | 120.6 | -2.7 | -0.7 | -1.3 | -0.7 | -1.0 | 143.4 | -2.1 | -1.9 |
| `c_lua_calls_c` | 76.7 | -4.7 | -2.1 | +0.0 | -0.5 | +2.8 | 99.5 | -0.1 | -0.1 |
| `c_c_calls_lua` | 122.9 | -9.4 | -2.7 | -9.0 | -0.0 | -0.0 | 153.2 | -13.7 | -13.9 |
| `c_table_rawgeti` | 28.3 | -6.9 | -7.6 | -7.4 | -0.7 | -0.4 | 37.9 | -0.6 | -1.4 |
| `c_table_pgettable` | 204.9 | -8.6 | -4.1 | -4.2 | -1.5 | -0.6 | 210.4 | -11.2 | -11.3 |
| `c_push_csobj` | 364.3 | -1.8 | -0.9 | -1.3 | -1.0 | -1.7 | 396.7 | -0.6 | -0.1 |
| `c_compile` | 8,749,362 | -3.4 | -2.7 | -2.2 | -1.2 | -10.0 | 13,196,555 | +0.4 | +1.2 |
| `c_env_create` | 157,300 | -1.5 | -1.8 | -1.4 | -1.1 | -2.4 | 189,809 | -0.3 | +0.0 |

What the numbers say:

- The cost is real: both arm64 runs give about -4%, against -0.7% for the
  layout control, and the losses are spread over most cases instead of one.
- **`-mbranch-protection`** costs about 2% on this phone and protects nothing
  here: the Cortex-A73 and A53 are ARMv8.0, where the PAC and BTI
  instructions execute as NOPs. They take effect only on ARMv8.3 (PAC) and
  ARMv8.5 (BTI) cores, and BTI only when the loader maps the library with
  `PROT_BTI`. **[S]** The cost on such a core was not measured.
- **`-ftrivial-auto-var-init=zero`** costs 1.4 to 1.7% overall, concentrated
  in the cases that cross a protected call: `c_c_calls_lua` (`lua_pcall`),
  `c_table_pgettable` (the getter runs under `lua_pcall`) and
  `coroutine_switch` lose 7 to 14% on armeabi-v7a. The likely cause is that
  every protected call now zeroes the `jmp_buf` in `luaD_rawrunprotected`
  (`struct lua_longjmp`, `ldo.c`) **[U]**; the profile was not taken.
- **`-fno-delete-null-pointer-checks`** is within noise (-0.3%).
- `bestnwc23-harden` adds about the same bytes to `bestnwc23` as
  `O3-harden` does to `O3`.

The results of every case were identical across variants (`report` found no
mismatch). The runner stayed on CPUs 4 to 7, the big-core zones read 33.9 to
40.1 °C (at most 38.6 °C at the start of a run), and the CPU cooling devices
stayed at 0. Files went only to `/data/local/tmp/xlua-bench`; the run log is
`c-setup-2026-10-17/sweep/adb-commands.log` on the build disk.

Recommendation for the default (the owner decides):

- Keep `XLUA_HARDEN` off by default in the release build for now. The 4%
  on arm64-v8a and 1.7% on armeabi-v7a are more than any option recommended
  above costs, and half of the arm64 cost buys nothing on ARMv8.0 phones.
- If hardening should become the default, the cheapest split is
  `-fno-delete-null-pointer-checks` everywhere (free), zeroed locals for
  xLua's own three files only (where bridge input arrives; the measured cost
  is in Lua's `ldo.c`), and `-mbranch-protection` kept for a later decision
  measured on an ARMv8.5 device. This split was not built or measured.
- Use `XLUA_HARDEN=ON` for debug, QA and security-sensitive builds now; the
  option is available on every platform and preset build.

## Correctness

- Host (macOS arm64, Mono 6.14.1): for `O3`, `O2`, `Os`, `Oz`, `Oz-noout`,
  `hybridn-O3`, `hybridnw-O3`, `thinlto-O3`, `fulllto-O3`, `thinlto-hybrid`,
  `fulllto-hybrid`, `outliner-O3`, `outliner-O2`, `hidden` and
  `hybridnw-hidden` (the compiler half of `bestnw*`), `run_managed.py` passed
  323 tests in both modes, plus `--gen-code` for `O3` and `hybridnw-hidden`;
  `check_exports.py`, `native_regression.py` and the runner's smoke script
  passed, and every case of the runner returned the same result as `O3`.
  The linker options are lld-only and were not tested on the host.
- Device: every library that was timed (33 arm64-v8a, 6 armeabi-v7a, API 21
  to 28) loaded with `dlopen` on the phone, passed the runner's smoke script (numbers, strings,
  `string.pack`, `utf8`, coroutines, errors, metatables, `string.dump`, weak
  tables, file I/O, `io.popen`, `uint64`) and returned the same result in every
  timed case.
- `check_android_elf.py` passes for `bestnw21` and `bestnw23` on both ABIs.

## Method

- One runner per ABI (`flag_bench.c`, built with the NDK at API 21) opens each
  library with `dlopen`, so the timed code is the stripped library as it
  ships, including its PLT and relocations. It pins itself to the four
  Cortex-A73 cores.
- Each case runs in a fresh `lua_State`: one warm-up run and three timed runs
  per process, median taken. A round runs every variant once in a shuffled
  order, after 5 s idle and only when the big-core thermal zones read below
  45 °C. Five rounds (three for armeabi-v7a, the layout controls and
  `bestnwc*`); the tables report the median of the round medians.
- The big-core zones read 35 to 50 °C (at most 45 °C at the start of a run),
  the CPU cooling devices (`cpufreq-cpu0`, `cpufreq-cpu4`, `cpu-cluster0`,
  `cpu-cluster1`) stayed at state 0 after every run, and
  `dumpsys thermalservice` reported status 0 before and after each bench.
- Device state before and after (read only): `ro.product.model` "moto g play -
  2024", `ro.board.platform` "bengal", governors `walt` on both clusters,
  thermal status 0. Files went only to `/data/local/tmp/xlua-bench`, which the
  script removes after each run; `adb-commands.log` in the output directory
  lists every command.

## Not verified

- iOS speed (no device), and speed on other Android cores. The A73 and the
  A53-class little cores of this phone were measured; newer big cores (A7x,
  X-series) were not.
- Unity players: no APK or Xcode project was built with these libraries, and
  IL2CPP's call overhead per P/Invoke is not in the runner.
- Whether a Unity version's Gradle template stores native libraries compressed;
  at `minSdk` 23 and above, the Android Gradle plugin's default
  (`useLegacyPackaging` false) stores them uncompressed, which makes the APK
  carry the stripped size instead of the deflated one.
- The lowest minimum API level each Unity version in the editor matrix allows
  (from memory, not checked: API 22 for 2021.3 and 2022.3, API 23 for Unity 6).
  A library with packed relocations or only a GNU hash table does not load
  below API 23, so it fits only projects whose minimum is 23 or higher.
- `bestnwc*` ran three rounds (three processes) each. A rare slow mode like
  the ones above could show up with more.
- Lua 5.4, 5.3 and LuaJIT, x86 and x86_64: not built with these options.
- `XLUA_HARDEN`: speed only on this ARMv8.0 phone, Lua 5.5.1, the two ARM
  ABIs; no ARMv8.3+ core, no iOS or desktop speed, no Unity player. Sizes
  for Linux x86_64 (`-fcf-protection`, `-fstack-clash-protection`),
  OpenHarmony and MSVC (`/guard:cf`) were not measured.
