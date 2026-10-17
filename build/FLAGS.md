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

- iOS speed (no device), and speed on other Android cores. The A53 cores and
  newer big cores were not measured; the runner pins the A73 cores.
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
