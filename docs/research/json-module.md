# An optional JSON module: yyjson, lua-cjson, others, or C#

Researched 2026-10-18 from the repositories and documents listed under
[Sources](#sources), read as data. Nothing downloaded was run. The one
measurement is size: the downloaded C and C++ files were compiled and linked
(never executed) in a scratch directory, with probe bindings written for this
note. Confidence: **[C]** confirmed in a source or by measurement, **[S]**
secondary, **[U]** unconfirmed. Versions and dates are given where they were
read.

Question: what is the best option for the ROADMAP item "a JSON module,
evaluating yyjson: exact 64-bit integers, a null sentinel, strict UTF-8, depth
and size limits, behind a build option"? Constraints are in
[AGENTS.md](../../AGENTS.md): an optional feature is a flag; size is measured
per feature; Lua 5.5.1, 5.4.9, 5.3.6 and LuaJIT 2.1; 64-bit integers stay
lossless; C17 only; no runtime code generation; the core preset keeps memory,
instruction and time limits ([core-luascript.md](../design/core-luascript.md)).

## Bottom line

1. **yyjson is the best native base for the bridge presets** (legacy,
   minimal), but it is not small. Android arm64, `-Oz`, stripped, measured:
   **+38 KB** decode only with libc `strtod`, **+62 KB** decode and encode with
   libc `strtod`, **+104 KB** decode and encode with yyjson's own float
   conversion. Compressed: +17, +31 and +58 KB. **[C]** For comparison:
   lua-cjson +28 KB, lua-rapidjson +508 KB, lua-simdjson +314 KB, a strict
   hand-written decoder probe +5.8 KB.
2. **We must write the binding.** No existing Lua module meets the constraints.
   lua-cjson clamps an integer above 2^63-1 without an error, does not
   validate UTF-8, calls `abort()` when `malloc` fails and uses `malloc`
   rather than the Lua allocator. The only yyjson binding that is more than a
   stub, mah0x211/lua-yyjson, wraps `uint64` values above 2^63-1 to negative
   integers, truncates strings at a NUL byte and uses GNU C `typeof`. **[C]**
3. **The core preset should not take a native JSON module first.** Core is
   268,488 bytes stripped and 121,529 bytes zipped
   ([SIZE.md](../../build/SIZE.md)); the budget is 290 KB and 125 KB, and later
   milestones draw on the same headroom. The zip
   headroom is about 3.5 KB (6.5 KB if the budget means KiB). Pure Lua inside
   the sandbox costs no native bytes, and the memory allocator, `InstructionLimit` and `TimeLimit` already cover it.
   A strict decoder in Lua 5.4/5.5 can use `utf8.len` for validation and
   `math.tointeger` for exact integers. **[C]** that these exist; **[U]** how
   fast it is under a limit.
4. **"Decode in C# and hand tables to Lua" loses.** System.Text.Json is not
   part of Unity's supported player profiles (2021.3 to 6000.4) **[S]**;
   Newtonsoft (Unity package 3.2.1, Json.NET 13.0.2) works on IL2CPP but costs
   managed code and needs `link.xml`; `JsonUtility` cannot produce an untyped
   tree. Each value also crosses the P/Invoke boundary one call at a time,
   and core has no C# object bridge to receive tables.

Ranking, details and the size cost estimate are under
[Recommendation](#recommendation).

## What was measured and how

NDK r27.1.12297006 (the repository builds with r30; the relative sizes
are what matter), `aarch64-linux-android23-clang`, `-Oz -ffunction-sections
-fdata-sections`, linked as a shared library with `--gc-sections --icf=all`,
a version script exporting `luaopen_*` only, `llvm-strip --strip-all`, headers
of `build/lua-5.5.1`. An empty module (`luaopen_empty` returns 0) is 3,440
bytes stripped and 1,072 bytes gzip -9; the table subtracts it ("net").
Probe bindings (my own, 70 to 100 lines, never run) decode into Lua tables
with a null sentinel and, for yyjson, encode through a mutable document. A
finished binding adds options, error text and a proper encoder: expect
+3 to +8 KB on top. Sizes are for Android arm64 only; iOS and armeabi-v7a
were not measured, and 32-bit is smaller for the yyjson variants **[U]**.
Per-file `-O3` for hot files (the release build's `XLUA_SIZE_FLAGS` split)
was not applied.

| Variant (Lua 5.5.1 headers) | stripped, net | gzip -9, net |
|---|---:|---:|
| **yyjson 0.13.0** decode only, libc `strtod` (`DISABLE_FAST_FP_CONV`), no non-standard, no utils, no incremental reader, no file | **37,616** | 17,476 |
| yyjson decode only, with its fast float conversion | 68,104 | 37,631 |
| yyjson decode only, libc `strtod`, non-standard syntax left in | 59,072 | 28,325 |
| yyjson decode and encode, libc `strtod`, same flags as the first row | 61,792 | 31,005 |
| yyjson decode and encode, fast float conversion | 103,936 | 58,426 |
| yyjson, every default (JSON5 flags, utils, incremental reader, writer, fast float) | 128,832 | 71,767 |
| lua-cjson openresty master (2.1.0.20), own `strbuf` and `fpconv`, no `dtoa` | 27,832 | 11,749 |
| lua-cjson mpx master (2.1devel) | 20,928 | 8,515 |
| cJSON 1.7.19, decode shim only | 6,112 | 3,184 |
| jsmn 1.1.0, tokenise plus naive conversion (no string unescape, no number validation) | 3,632 | 1,956 |
| strict hand-written decoder probe (see below), no encoder | 5,800 | 3,030 |
| lua-rapidjson 0.7.2, whole module, `libc++` linked statically | 508,160 | 172,180 |
| lua-simdjson 0.1.0 (simdjson 4.6.4), whole module, `libc++` linked statically | 314,280 | 125,027 |

yyjson's header comments give reductions of about 60% (no reader), 30% (no
writer), 30% (no fast float), 10% (no non-standard) **[C]**; the numbers above
agree. Turning off non-standard syntax saves 21 KB here **[C]**, the single
biggest switch after the float conversion.

The strict decoder probe is 95 lines: RFC 8259 grammar, UTF-8 validation
(overlongs, surrogates, above U+10FFFF), `\u` surrogate pairs, depth limit,
`lua_stringtonumber` for numbers with an error when an integer literal does
not fit 64 bits, and a budget tick every 4,096 values. It has no tests and
is a size probe, not a design. It shows that a decoder with the properties
the ROADMAP asks for is a 6 KB problem, not a 40 KB one. **[C]** for the
size, **[U]** for its speed and correctness.

Pure Lua, compiled with the repository's own Lua 5.5.1 `luac -s` (compile
only):

| Module | source | stripped bytecode | gzip -9 of source |
|---|---:|---:|---:|
| dkjson 2.11 | 25,009 | 12,884 | 7,004 |
| lunajson 1.2.3 decoder | 12,537 | 6,573 | 3,386 |
| lunajson 1.2.3 encoder | 3,459 | 2,426 | 1,243 |
| rxi json.lua 0.1.2 | 10,022 | 5,351 | 3,099 |

For context, `libxlua.so` for Lua 5.5.1 arm64-v8a is about 383 KB stripped
and 162 KB zipped in the legacy preset (SIZE.md), so yyjson decode only is
+10% and decode and encode with fast floats is +27%.

## Candidates

### Native C, as a Lua module

| | yyjson 0.13.0 | lua-cjson (openresty) | lua-cjson (mpx) | lua-rapidjson | lua-simdjson | cJSON / jsmn + binding |
|---|---|---|---|---|---|---|
| **Licence** | MIT **[C]** | MIT **[C]** | MIT **[C]** | MIT; RapidJSON is MIT except `bin/jsonchecker/` (JSON licence, "not evil"), so vendor `include/` only **[C]** | Apache-2.0 (binding and simdjson) **[C]** | MIT both **[C]** |
| **Last release** | 0.13.0, 2026-09-08; 0.12.0 2025-08-18; 0.11.0 2025-05-05 **[C]**. Master pushed 2026-10-13. Pre-1.0: API changes between minors (0.7.0 allocator signature, 0.13.0 `set_int`) | tags 2.1.0.20 (2026-09-06, adds sorted keys), 2.1.0.17 (2026-05-06), 2.1.0.13 (2023-07-20, CVE fix); last GitHub "Release" 2.1.0.9 (2022-10-17) **[C]** | 2.1.0 (2012-03-01). Master has only docs commits, last 2024-04-15 **[C]** | v0.7.2 tag, last commit 2025-08-26; v0.7.1 release 2021-04-14. RapidJSON upstream (Tencent): last release 1.1.0 (2016-08-25), last commit 2025-02-05 **[C]** | 0.1.0 (2026-07-24); 0.0.9 2026-02-10; simdjson 5.0.5 is current (2026-10-14), the binding bundles 4.6.4 **[C]** | cJSON 1.7.19 (2025-09-09); jsmn 1.1.0 (2019-06-23), last commit 2021-10-14 **[C]** |
| **Size, net (arm64, stripped)** | 38 to 104 KB, by flags (table above) | 28 KB | 21 KB | 508 KB | 314 KB (author says about 200 KB on Linux x86-64) | 6 KB / 3.6 KB (decode only) |
| **Language** | C89 header and source ("ANSI C"); builds as C17 **[C]**; MSVC, tcc, freestanding build (0.13.0) | C89/C99 | C89/C99 | C++11, uses `try`; needs a C++ runtime | C++17, `thread_local`, exceptions, 64-bit only **[C]** | C89 |
| **Speed (published)** | twitter.json parse 1.72 GB/s, stringify 1.42 GB/s (EPYC 7R32, gcc 9.3); simdjson DOM 1.52 / 0.61; on iPhone A14 2.39 / 2.01 against 2.19 / 0.80. Benchmark dated 2020-12-12, DOM only **[C]**. No published yyjson to Lua table benchmark **[U]** | About 9x (encode) and 14x (decode) dkjson on Lua 5.3, 500,000 loops, cjson 2.1.0 (2018, [arpalert](https://www.arpalert.org/lua-json-libraries-performances.html)) **[C]**; "13x and 17x" in the cjson `performance.adoc`, Lua 5.1 era **[C]** | same code | README: "slightly faster than cjson" decoding, "always faster" encoding, self-reported **[C]** | README has charts only, no numbers; "tested with LuaJIT 2" **[C]** | not published |
| **64-bit integers** | `int64`/`uint64`/`double` kept apart in the DOM. Integers that fit neither become `double`, or raw text with `BIGNUM_AS_RAW` **[C]**. Writer prints `int64`/`uint64` exactly | Decode: `strtoll` then `lua_pushinteger` on 5.3+. The overflow test `tmpval > PTRDIFF_MAX` is dead on 64-bit, so `9223372036854775808` decodes silently as 9223372036854775807 (read from code, not run) **[C]**. On 5.1/LuaJIT everything is a double. Encode: integers printed exactly on 5.3+; doubles with 14 digits by default | Doubles only | `lua_pushinteger` for int64 and uint64 up to the Lua maximum, else a double; on encode an integral float is written as an integer, so `1.0` becomes `1` **[C]** | int64 exact; uint64 above `LUA_MAXINTEGER` becomes a double (lossy); on 5.1 everything is a double **[C]** | cJSON: `valueint` is a 32-bit `int` (clamped), `valuedouble` is the real value, so lossy above 2^53 **[C]**. jsmn gives text only, so exact |
| **null** | `YYJSON_TYPE_NULL`; the binding chooses | lightuserdata NULL (`cjson.null`) | same | `rapidjson.null`, lightuserdata since 0.7.0 | `simdjson.null` | binding's choice |
| **UTF-8 validation (decode)** | Yes by default; opt out with `ALLOW_INVALID_UNICODE` or the compile flag **[C]** | No: "UTF-8 characters are not validated during decoding" (manual) **[C]** | No | Off unless `kParseValidateEncodingFlag`; the binding passes no flag **[C]** | Yes (simdjson validates) **[S]** | No / No |
| **Depth limit** | Compile-time only, one value for all callers (`YYJSON_READER_DEPTH_LIMIT`, `WRITER_`; 0 = none), new in 0.13.0. The reader is iterative (goto based), so no C stack overflow **[C]** | Runtime `decode_max_depth`, `encode_max_depth`, default 1000 each; recursive | same | Encode depth only; RapidJSON recursion on decode is not limited by the binding **[U]** | `maxDepth` for encode; simdjson's own limit (1024 by default) on decode **[S]** | cJSON `CJSON_NESTING_LIMIT` 1000 (recursive); jsmn none, iterative |
| **Size limit** | The caller knows `len`; DOM memory is bounded by `(len/2)*16*1.5`, 12x the input, "broken JSON may cost more" (`yyjson_read_max_memory_usage`) **[C]** | none beyond memory | none | none | only a `size_t` overflow check ("JSON input is too large") **[C]** | jsmn: size of the token array you supply |
| **Errors** | code, constant message, byte position (`yyjson_read_err`); `yyjson_locate_pos` gives line and column (0.10.0) **[C]** | message with byte offset and token | same | rapidjson parse error text and offset | simdjson error text, no position **[C]** | cJSON: one pointer; jsmn: three codes |
| **Encode** | Writer for its own value tree; you build a mutable document from the Lua table or write your own encoder. Flags: pretty, escape unicode, escape slashes, inf/nan as null or literal, invalid unicode, newline, lowercase hex. Shortest correctly rounded float text, ECMAScript format (`doc/API.md`) **[C]**; whether an integral double prints as `1.0` or `1` was not checked **[U]** | Arrays by `rawget` on 1..n; sparse arrays error ("excessively sparse", ratio 2, safe 10) or convert; empty table = object by default (`encode_empty_table_as_object`), `empty_array` and `array_mt` markers; NaN/Inf error, literal or null; 14 significant digits; sorted keys from 2.1.0.20 **[C]** | Same, without the options added after 2012 | `__jsontype` metatable, `empty_table_as_array`, sorted-key options | `encode` with depth and buffer options; consecutive 1..n is an array, sparse and mixed are objects **[C]** | none / none |
| **Streaming** | Incremental DOM reader from chunks (0.11.0); `STOP_WHEN_DONE` reads one value from larger data. No SAX | none | none | RapidJSON has SAX; the binding does not expose it **[U]** | `parse_many`, on-demand API in simdjson; the binding offers `open` + JSON pointer | jsmn tokenises resumably (`JSMN_ERROR_PART`) |
| **Lua versions** | Any: the binding is ours | Compiled against 5.5.1 headers (warning: `lua_objlen` redefined); built for 5.1 to 5.4 and LuaJIT by design **[C]** | compiled against 5.5.1 | README: LuaJIT 2.0/2.1, 5.1 to 5.3; compiled here against 5.5.1 headers **[C]** (not run) | rockspec `lua >= 5.1, < 5.6` **[C]** | any |
| **Custom allocator** | Per call `yyjson_alc {malloc, realloc(ctx, ptr, old_size, size), free(ctx, ptr), ctx}`. `free` gets no size, but Lua's allocator needs one, so a header word per block or a pool is needed. `yyjson_alc_pool_init` over a Lua userdata buffer of `yyjson_read_max_memory_usage` bytes avoids both **[C]** | `strbuf.c` calls `malloc`/`realloc`; on failure it calls `abort()` (`die`, 7 sites) **[C]**. Patchable, but still leaks on `lua_error` | same | C++ allocator template; patchable, but C++ | `new (std::nothrow)`, process allocator | cJSON: `cJSON_InitHooks`, global to the process; jsmn: no allocation at all |
| **Step budget** | Reader is one C call. Charge `len` before it (one tick per 32 or so bytes, like `buffer`), then charge every 4,096 values in our walk over the DOM; the walk raises, the parser cannot | `json_process_value` is recursive C; ticks go into it, 20 to 40 lines | same | no | no | jsmn: parser state is resumable, so ticks between chunks are trivial |
| **Security** | CVE-2024-25713: double free in the pool allocator, `<= 0.8.0`, fixed in 0.9.0 per advisories; disputed by the maintainers (calling `yyjson_mut_doc_free` twice is documented misuse) **[S]**. 0.13.0 fixes an uninitialised read found by OSS-Fuzz, so it is fuzzed **[C]** | CVE-2022-24834 (Redis, heap overflow from integer overflows in `strbuf`), fixed in 2.1.0.13 (2023-07-20) **[C]**; CVE-2026-46752 (Kvrocks, same family, 2026-06-25) **[C]**; CVE-2022-25757 (duplicate keys: last wins, APISIX) **[C]** | mpx `strbuf.c` has no overflow checks (0 matches for `overflow`, `SIZE_MAX`, `assert`); the openresty copy has them **[C]** | RapidJSON CVE-2024-38517 and CVE-2024-39684 (`ParseNumber` integer under/overflow); Debian lists the second as unfixed, the first inconsistently **[S]** | CVE-2026-8295 (fixed in 4.6.4), CVE-2026-88358 (4.6.1, one-byte over-read) **[C]** | cJSON: CVE-2023-50471, -50472, -53154 (fixed 1.7.17/1.7.18), CVE-2024-31755, CVE-2026-16554 (32-bit, unfixed in Debian's tracker) **[S]**. jsmn: CVE-2026-88369 and -95117 name `example/jsondump.c` and HEAD 25647e6 **[C]** |

Notes on the table:

- **yyjson decodes to its own tree first.** There is no SAX or callback
  parser. A Lua binding therefore walks the tree and builds tables, which
  needs up to 12x the input in transient memory, and the walk can raise (an
  out-of-memory error from `lua_createtable`). The tree must not leak when it
  does. Two safe patterns: run the walk in a function called with `lua_pcall`
  and free the document on the way out, or give yyjson a pool allocator over a
  userdata buffer, which the collector frees. The pool also makes the memory
  limit honest, because the buffer goes through the state's allocator before
  the parse starts and a refusal is the usual "not enough memory", never a
  half-built tree. **[C]** for the API; **[U]** which is cheaper.
- **lua-yyjson (mah0x211, v0.12.0, MIT, pushed 2026-06-12, 11 stars)** bundles
  yyjson 0.12.0 and is the closest thing to an existing binding. Its README
  says it uses `lua_Alloc`; the code allocates with `malloc` and a 16-byte
  header of its own (`do_malloc` in `src/doc.c`). It pushes `uint64` through
  `lua_pushinteger` (values above 2^63-1 wrap negative), builds strings with
  `lua_pushstring` (cut at a NUL), uses `typeof`, and depends on `lauxhlib`,
  `errno` and `error` modules. It is a reference for option names, not a
  base. **[C]** A second binding, thedolphin/lua-yyjson-nd (MIT, 0.1.2, 2026-06),
  exposes the document lazily as userdata. **[S]**
- **simdjson and RapidJSON fail the constraints before size matters**: C++
  runtime and exceptions in a C17 code base, 64-bit only (simdjson), no
  allocator hook, 0.3 to 0.5 MB.
- **cJSON is the wrong shape** for exact 64-bit integers (32-bit `valueint`,
  process-global hooks, recursive, `CJSON_NESTING_LIMIT` is a compile-time
  constant). It decodes and prints smaller than yyjson, but it would need
  patching in exactly the parts that matter.
- **jsmn is a tokeniser**, not a JSON decoder. Strings are not unescaped or
  validated, numbers are not parsed, there is no depth counter. A "thin
  binding" on jsmn grows into the 6 KB hand-written decoder above, with jsmn's
  token array as extra memory. The hand-written decoder is the more honest
  version of the same idea.
- **lua-cjson is the sensible "bring your own" module.** It is what the
  ecosystem uses ([ECOSYSTEM.md](../ECOSYSTEM.md)), it is small and the
  openresty fork is maintained and has fixed its overflow CVE. Its limits for
  our rules: no UTF-8 validation, silent clamp above 2^63-1, `abort()` on
  `malloc` failure, `malloc` instead of the Lua allocator, 14-digit floats by
  default, permissive decoding of `NaN`, `Infinity` and hex numbers by default,
  and a lightuserdata `null` whose address it masks to 47 bits for LuaJIT
  (`json_lightudata_mask`). **[C]**

### Pure Lua

| | dkjson 2.11 | rxi json.lua 0.1.2 | lunajson 1.2.3 |
|---|---|---|---|
| Licence | MIT-style ("same as Lua"; header carries the MIT text) **[C]** | MIT **[C]** | MIT **[C]** |
| Last release | 2.11, 2026-07-27; 2.10 2026-05-13; 2.9 2026-04-29; 2.8 2024-06-17 **[C]** | v0.1.2, 2019-06-21; last commit 2020-06-18 **[C]** | 1.2.3, 2020-09-13; last commit 2026-02-21 **[C]** |
| Lua versions | "5.1 - 5.5" in the header; optional LPeg decoder **[C]** | no statement read **[U]** | README: 5.1 to 5.3 and LuaJIT 2.0; last commit 2026-02-21 **[C]** |
| Speed | 9 to 14x slower than cjson on Lua 5.3 (arpalert, 2018) **[C]** | no benchmark read | README claims it "matches lpeg-based" decoders; SAX parser **[C]** claim, **[U]** result |
| 64-bit integers | `tonumber` on the literal: exact on 5.3+ up to int64, a float beyond (silent) **[S]** | `tonumber`, so the same; encode uses `%.14g` for every number, so an integer above 1e14 loses digits **[C]** | decoder uses `math.tointeger`; encoder `%.17g` for floats **[C]** |
| null | `json.null` | decodes `null` to `nil` (holes) | optional `nullv` argument |
| UTF-8 validation | none found in the decoder **[S]** | none | rejects bad escapes and surrogate order; no byte validation found **[S]** |
| Depth / size limits | none; recursion only | none | none (SAX parser can stop early) |
| Encode | `__jsontype` metatable for the empty-table case, `keyorder` / `__jsonorder`, NaN and Inf as `null`, indentation | errors on sparse arrays, mixed keys, NaN, Inf, cycles | `t[0]` as the array length for arrays with holes; `nullv` marker |
| Security | No NVD entry by keyword. 2.10 and 2.11 fix decoder regressions that allowed an endless loop and an error instead of `nil`; 2.11 adds basic fuzzing **[C]** | none found | none found |

All three run under any limit the state has, since every allocation goes
through the state's allocator and every step is a VM instruction. They are
the only candidates that need nothing from `xlua_core.c`. They also work
unchanged on LuaJIT, whose JIT makes lunajson fast **[U]**.

### C# side: decode in C#, hand the result to Lua

| | System.Text.Json | Newtonsoft (`com.unity.nuget.newtonsoft-json`) | `JsonUtility` |
|---|---|---|---|
| Licence | MIT | MIT | part of the engine |
| Availability in Unity | Not shipped for the supported player profiles in 2021.3 to 6000.4. It runs with the netstandard2.0 build plus `System.Memory`, `System.Text.Encodings.Web`, `System.Runtime.CompilerServices.Unsafe` copied in by hand **[S]**; an IL2CPP build failure was closed "By Design" **[S]**. A Unity bug (UUM-139823) says Unity now requires System.Text.Json for "some upcoming features"; it reproduces from 6000.5.0a7 and not on 6000.0.73f1, 6000.3.13f1 or 6000.4.2f1, and Unity will not support a second copy **[C]**. Whether a 6000.5+ player has it by default is **[U]** | Package 3.2.1 (2023-04-27), Json.NET 13.0.2 (3.1.0, 2023-02-28); an AOT DLL for IL2CPP since 2.0.0 (2020-04-20) **[C]**. The manual page shows 3.2.2 **[C]**; the changelog does not list it | In every Unity version |
| Binary size | not measured | not measured; reflection heavy, so IL2CPP stripping needs `link.xml` or `[Preserve]` **[S]** | none |
| Speed | `Utf8JsonReader` is allocation free and the fastest .NET reader **[S]** | slower than System.Text.Json; Unity's own docs say `JsonUtility` is "significantly faster than popular .NET JSON solutions" **[C]** | fastest, but typed |
| 64-bit | `GetInt64`, `GetUInt64`, `GetDecimal` exact **[S]** | `long`, `BigInteger` **[S]** | `long` fields only **[S]** |
| null, depth | `JsonElement` Null; default `MaxDepth` 64 **[S]** | `JValue` Null; `MaxDepth` 64 by default since 13.0.1 **[S]** | typed fields; no null for classes in lists |
| UTF-8 | Strict on UTF-8 input **[S]** | Works on UTF-16 `string`; invalid input can only be a bad string | n/a |
| Security | CVE-2024-30105 (2024-07-09), CVE-2024-43485 (2024-10-08), both "denial of service" in NVD **[C]** | CVE-2024-21907: stack overflow on deep nesting, before 13.0.1; the Unity package has 13.0.2 **[C]** | none known |
| Fit | A `Utf8JsonReader` loop calling `lua_push*` is the only C# route without a tree, and needs the dependency problem solved | Same with `JsonTextReader` | **Cannot do it.** The manual states that `Dictionary<>`, primitives and arrays are not supported at the top level, and the serializer needs a `[Serializable]` class. No untyped tree. |

Why this route is poor for xlua-ng:

- **Per-value P/Invoke.** The C# reader calls `lua_pushinteger`,
  `lua_rawseti`, `lua_createtable` once per value; the native route does the
  same work in one call. The cost per call on IL2CPP was not measured for this
  note **[U]**.
- **Core has no table bridge.** `LuaScript` hands out `LuaTableRef` handles
  and values, and core-only players have no `XLua` assembly code (SIZE.md).
  A C# decoder would have to write tables through that handle API, which
  is designed around typed reads.
- **No memory accounting on the C# side.** The tree lives on the managed
  heap, outside `MemoryLimit`, unless the C# reader streams into the state.
- **It does help the legacy user who already has Newtonsoft.** Decode to
  `JToken` or a `Dictionary`, and pass strings, buffers or a typed object to
  Lua. That needs no feature from us; it is a recipe for the guides.

## How the candidates meet the core preset

The core preset is Lua 5.4 and 5.5 only (`XLUA_LUASCRIPT`), has a counting
allocator that may refuse, and charges long C loops to the instruction budget
through `luai_budget(L, n)` (core-luascript.md, section 3.11; the `buffer`
library charges one instruction per 32 bytes).

| Requirement | yyjson binding | lua-cjson | hand-written C decoder | pure Lua |
|---|---|---|---|---|
| Memory counted and refused cleanly | Yes, with the pool or header allocator; yyjson returns an error code on `NULL`, no `longjmp` inside it | No (`malloc`, `abort()`) | Yes: tables and `luaL_Buffer` already use the state's allocator | Yes |
| `longjmp` through library code | Only in our walk; guard with `lua_pcall` or the pool | Leaks `strbuf` on `lua_error` | None if every raise is ours and buffers are `luaL_Buffer` | None |
| Step budget inside the loop | Ticks in the DOM walk; the parse itself only up front | 20 to 40 lines, recursive code | Natural: a tick per value, as in the probe | VM instructions |
| Depth | Our counter in the walk, plus the global compile-time limit | `decode_max_depth` | Counter in the probe | Counter in Lua |
| Size | 38 KB or more, past core's compressed headroom | 28 KB, same | about 6 KB for decode, 9 to 10 KB with encode **[U]** | 0 native, 4.6 KB gzip as Lua source |
| Determinism (`HashSeed`) | Same as any table fill | Same | Same | Same |

Encoding in core wants **sorted keys**: `pairs` order is fixed only for one
`HashSeed` and one Lua version, so reproducible output needs a sort. The
openresty fork added this option on 2026-09-06 (2.1.0.20). **[C]**

Time bound for a one-call parse: yyjson parses about 1 byte per nanosecond
(from the README numbers above), and a VM instruction takes about one to
three nanoseconds, so charging one instruction per 2 to 4 input bytes before
the call is plausible. The rate is a choice for the owner, as for `buffer`. **[U]**

## Integer, null and array policy (the design decisions behind "exact")

These apply to any native or Lua implementation. They are not in the sources
above; they follow from the rules in AGENTS.md.

- **Integers.** A JSON integer that fits `int64` becomes a Lua integer on
  5.3 to 5.5. One that fits only `uint64` (9223372036854775808 to
  18446744073709551615) or no 64-bit type cannot become a float without
  losing digits, so the choices are an error (default, matches "lossless"),
  the decimal text as a string, or a float on request. lua-yyjson wraps and
  lua-cjson clamps; both are silent. **[C]** that they are silent.
- **LuaJIT.** A Lua number is a double, so integers above 2^53 need the xLua
  int64 userdata (`i64lib.c`) or an error. Which one is an owner decision.
- **Floats.** Encode the shortest round-trip text, and write an integral
  float with a fraction (`1.0`) so the Lua subtype survives a round trip.
  lua-rapidjson and the cjson default do not; for yyjson the binding may
  have to append the `.0` itself **[U]**.
- **Embedded NUL.** `\u0000` must give a string with a NUL byte, so use
  `lua_pushlstring` with the length.
- **Duplicate keys.** Last wins (what Lua tables give); CVE-2022-25757 shows
  that a validator and a parser that disagree on this is a bypass. Document it.
- **`null`.** One unique lightuserdata sentinel as `json.null`, comparable
  across states. The alternative (`nil`) makes arrays with `null` holes and
  loses keys. Encode treats the sentinel as `null` and `nil` fields as absent.
- **Arrays versus objects on encode.** An empty table is ambiguous. Give the
  module an `json.array()` marker (a metatable or a table field) and encode
  unmarked empty tables as objects (cjson's default, `{}`). Tables with keys
  1..n only are arrays; anything else is an object or an error (sparse,
  mixed), never silently both.
- **Not supported by default:** comments, trailing commas, `NaN`/`Infinity`,
  a BOM. yyjson has flags for all of them; a build with
  `YYJSON_DISABLE_NON_STANDARD` removes them and saves 21 KB.

## Recommendation

### (a) Legacy and minimal `LuaEnv` users

Ranked for the bridge presets, where a user opts in with a build flag
(`XLUA_JSON`, proposed name; default OFF):

1. **yyjson 0.13.0 plus our own binding.** Pin the release (vendor
   `yyjson.c` and `yyjson.h`, record the hash in the SBOM, MIT notice kept).
   Why: only candidate with exact int64/uint64 in the parser, strict UTF-8 by
   default, an allocator hook, a maintained and fuzzed code base, and a
   build-time switch for every optional part. Build it with
   `YYJSON_DISABLE_FILE`, `_NON_STANDARD`, `_UTILS`, `_INCR_READER` and a
   depth limit of 200.
   - **Size:** decode only **+38 KB** stripped, **+17 KB** zipped (libc
     `strtod`); decode and encode **+62 KB** and **+31 KB**; each +30 to
     +42 KB more with yyjson's fast float conversion. This is +10% to +16%
     of the legacy library without fast floats.
   - **Two sub-flags:** `XLUA_JSON_ENCODE` and `XLUA_JSON_FAST_FP` (measure
     the speed gain before offering the second one; libc `strtod` is correctly
     rounded and shares Lua's own locale exposure, which the Android decimal
     point patch already covers).
   - **Cost to build:** about 500 lines of C for decode, the walk, options
     and error text, plus about 300 for an encoder (`luaL_Buffer`, our own
     array/object rules, `yyjson_write_number` or `%.17g` for doubles).
     LuaJIT needs a `lua_pushinteger` fallback and the int64 userdata path.
2. **A strict hand-written decoder and encoder in C (about 6 KB decode, 9 to
   10 KB with encode).** Choose it if size outweighs speed and the owner will
   accept owning a parser: fuzz it, run it against JSONTestSuite. Same
   binding contract as (1), so the choice can wait for the spike. Risk:
   hand-written parsers are where JSON CVEs live.
3. **openresty lua-cjson 2.1.0.20, as documented "bring your own".** +28 KB.
   Needs patches for our rules (strbuf `abort()`, UTF-8, the clamp), which
   makes it a fork we maintain. Keep it in the guides as the quick route, not
   in the build.
4. **Pure Lua (dkjson 2.11 or lunajson 1.2.3)** for users who do not mind
   speed: 0 native bytes, 5 to 7 KB zipped source.
5. **Not recommended:** lua-rapidjson (+508 KB, C++), lua-simdjson (+314 KB,
   C++17, 64-bit only), cJSON (lossy numbers, global hooks), jsmn alone,
   mpx lua-cjson (unpatched against CVE-2022-24834's class of bug), lua-yyjson
   as is.

### (b) The core `LuaScript` sandbox

1. **Pure Lua inside the sandbox first**: a strict decoder and encoder
   written for Lua 5.4/5.5 (lunajson's decoder and encoder are the starting
   point; keep its MIT notice), loaded as an embedded module like
   `xlua.util`. Exact integers by `math.tointeger`, strict UTF-8 by
   `utf8.len` on the input, a depth limit, sorted keys, `json.null`, and no
   global state. **Size: 0 native bytes**; about 4.6 KB gzip of source, which
   as C# string constants or a `TextAsset` is outside the native budget but
   inside the "200 KB APK" budget. Everything is bounded by `MemoryLimit`,
   `InstructionLimit` and `TimeLimit` with no new C. The `string.find` and
   `gsub` calls it makes are already under the C budget.
   **[U]** how many instructions per input byte it needs; measure before
   promising a size limit.
2. **A native fast path later, behind its own option, outside the
   290 KB / 125 KB budget** (or with the owner raising the budget): the 6 KB
   hand-written decoder with `luai_budget` ticks and the state's allocator
   (about +6 KB stripped, +3 KB zipped, so it would fit the current
   headroom only just), or yyjson decode only (+38 KB / +17 KB, which does not
   fit). Same `json` API on both, so scripts do not change.
3. **Do not use any candidate as is** in core: lua-cjson aborts on
   `malloc` failure and bypasses `MemoryLimit`; yyjson's parse is one
   unbudgeted C call.

If the owner wants no JSON in core at all, the host can pass `string` or
`buffer` values it decoded in C#. That is the existing path and needs no work.

### Open questions for the owner

1. Is a native JSON module in the bridge presets worth +38 to +104 KB, or is
   the 6 KB hand-written decoder (we own the parser and its fuzzing) the
   better trade? This is the main decision.
2. A JSON integer above 2^63-1 (or a bigger one): error, string or float? The
   default affects whether "lossless" holds.
3. LuaJIT: int64 userdata for integers above 2^53, or an error?
4. May the core budget grow (125 KB zipped has 3.5 KB left), or must core
   JSON be Lua only?
5. Output format changes: shortest round-trip floats and `1.0` for floats
   differ from lua-cjson's and lua-rapidjson's output. `docs/MIGRATING.md`
   applies if anyone moves from those modules. Do we want a `legacy`-style
   compatibility mode (14 digits, empty table as `{}`)?
6. Module surface: global `json`, or `require "xlua.json"` (preferred for the
   sandbox), and whether `LuaLibs` gets a `Json` bit like `Buffer`.
7. Defaults for depth (200) and for a maximum input size (for example 16 MiB
   in core, none in legacy).
8. Does the module include decode of NDJSON / multiple values (yyjson's
   `STOP_WHEN_DONE`)? Not needed for the ROADMAP list.
9. If the owner prefers the hand-written parser: who maintains it, and is
   JSONTestSuite (MIT, data only) acceptable as a pinned test input?

## What we would measure in a spike

One branch, one agent at a time (the native-test rule in CLAUDE.md applies;
no parallel `swift test`-style fan-out).

1. **Size**, with `Test/MobileSmoke/size_report.py` and the real
   `XLUA_SIZE_FLAGS` split: yyjson decode only / decode+encode, fast float on
   and off, non-standard on and off, against the 6 KB decoder; arm64-v8a,
   armeabi-v7a, iOS arm64, WebGL; legacy and core presets; NDK r30.
2. **Speed to Lua tables**, with `Test/Native/benchmark.py` style cases:
   twitter.json, citm_catalog.json, canada.json (float heavy), a luban
   fixture, a 10 MB array of small objects, string-heavy and number-heavy
   files. Compare yyjson walk, the hand-written decoder, openresty cjson
   2.1.0.20, lunajson and dkjson, on Lua 5.5.1, 5.4.9, 5.3.6 and LuaJIT, on
   macOS arm64 and the moto g play 2024. Report time, bytes per input byte,
   and peak allocator bytes (the DOM's transient 12x).
3. **Fast float on or off:** speed gain on canada.json against the +30 to
   +42 KB.
4. **Memory limit:** a refusal at every allocation point of a decode
   (a fault-injecting allocator that fails the n-th call, for n = 1 to
   the end). No leak (ASan and LeakSanitizer through `sanitize.sh`), no crash,
   the usual memory error. Compare the pool and the pcall guard.
5. **Budget:** a 64 MB document and a 100,000-deep nesting under
   `InstructionLimit`, `TimeLimit` and `Interrupt`: how late the stop is, and
   the cost of the ticks when no limit is set.
6. **Correctness:** JSONTestSuite (`y_`, `n_` and `i_` files, expected
   results recorded), the int64 and uint64 boundaries, `-0`, `1e999`,
   `\u0000`, lone surrogates, overlong UTF-8, a BOM, duplicate keys, a
   10,000-digit number, depth 199, 200 and 201.
7. **Round trip:** a million random doubles and int64 values through
   encode and decode; float subtype; empty tables; sparse arrays; cycles;
   `NaN` and `Inf`; key order with and without sorting under a fixed
   `HashSeed`.
8. **Fuzz:** libFuzzer with ASan and UBSan on decode and encode, seeded from
   JSONTestSuite, for the hand-written parser at least an hour per commit.
9. **Lua versions:** the same suite on 5.3.6, 5.4.9, 5.5.1 and LuaJIT 2.1
   (GC64 lightuserdata for `json.null`; `lua_stringtonumber` is absent on
   LuaJIT); `APICHECK` run as in [apicheck-runs.md](apicheck-runs.md).
10. **Pure Lua in core:** instructions per input byte for lunajson-derived
    code under `InstructionLimit` on 5.5.1, with `utf8.len` validation, to
    set a documented maximum document size.
11. **IL2CPP:** the module as part of the source plugin and as a `.so`;
    `__Internal` binding; stripped `ScriptingAssemblies.json` unchanged for a
    core-only player (`core_only_check.py`).
12. **C# route, one data point:** Newtonsoft `JsonTextReader` pushing to Lua
    through the bridge, for time and managed size, to put a number on the
    claim in this note.

## Sources

Repositories and releases (read 2026-10-18; dates from the GitHub API unless
noted):

- yyjson: <https://github.com/ibireme/yyjson>, tag 0.13.0 (2026-09-08),
  `src/yyjson.h`, `src/yyjson.c`, `CHANGELOG.md`, `doc/`.
- lua-cjson (openresty): <https://github.com/openresty/lua-cjson>, master at
  2.1.0.20 (2026-09-06), `lua_cjson.c`, `strbuf.c`, `manual.adoc`.
- lua-cjson (mpx): <https://github.com/mpx/lua-cjson>, master.
- lua-rapidjson: <https://github.com/xpol/lua-rapidjson>, v0.7.2;
  RapidJSON: <https://github.com/Tencent/rapidjson>, `license.txt`.
- lua-simdjson: <https://github.com/FourierTransformer/lua-simdjson>, 0.1.0;
  simdjson: <https://github.com/simdjson/simdjson>.
- lua-yyjson: <https://github.com/mah0x211/lua-yyjson>, v0.12.0;
  lua-yyjson-nd: <https://github.com/thedolphin/lua-yyjson-nd>.
- cJSON: <https://github.com/DaveGamble/cJSON>, v1.7.19; jsmn:
  <https://github.com/zserge/jsmn>, v1.1.0.
- dkjson 2.11: <http://dkolf.de/dkjson-lua/> (page and `dkjson-2.11.lua`);
  rxi/json.lua v0.1.2: <https://github.com/rxi/json.lua>; lunajson 1.2.3:
  <https://github.com/grafi-tt/lunajson>.
- Benchmarks: [yyjson README](https://github.com/ibireme/yyjson) and
  `yyjson_benchmark` (2020-12-12);
  <https://www.arpalert.org/lua-json-libraries-performances.html> (2018);
  lua-cjson `performance.adoc`.

Vulnerabilities:

- NVD: CVE-2024-25713 (yyjson), CVE-2022-24834, CVE-2026-46752 and
  CVE-2022-25757 (lua-cjson family), CVE-2024-38517 and CVE-2024-39684
  (RapidJSON), CVE-2026-8295 and CVE-2026-88358 (simdjson), CVE-2026-88369
  and CVE-2026-95117 (jsmn), CVE-2024-21907 (Newtonsoft.Json),
  CVE-2024-30105 and CVE-2024-43485 (.NET). NVD API, keyword searches for
  "lua-cjson", "yyjson", "simdjson", "dkjson", "jsmn", "lunajson",
  "rapidjson" (no entry for dkjson or lunajson).
- Debian tracker for CVE-2026-16554 (cJSON, unfixed) and RapidJSON;
  openSUSE advisory for cJSON 1.7.17 and 1.7.18 fixes.
- lua-cjson commit "Bugfix: Lua cjson integer overflow issues
  (CVE-2022-24834)", 2.1.0.13, 2023-07-20.

Unity and .NET:

- [Unity Manual: JSON serialization (6000.3)](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/code-reload-serialization/script-serialization/json-serialization.md).
- [Newtonsoft package manual and changelog](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/changelog/CHANGELOG.html).
- [Unity issue UUM-139823](https://issuetracker.unity.com/issues/22361/cs1705-error-is-thrown-when-bclextensions-assemblies-override-higher-version-nuget-dependencies).
- Unity forum threads on System.Text.Json (<https://discussions.unity.com/t/systen-text-json-net-standard-2-0/868255>,
  <https://discussions.unity.com/t/please-add-system-text-json-support/815652>).

Repository documents used: [AGENTS.md](../../AGENTS.md),
[ROADMAP.md](../../ROADMAP.md), [core-luascript.md](../design/core-luascript.md)
(sections 3.4, 3.11, 6), [presets.md](../design/presets.md),
[SIZE.md](../../build/SIZE.md), [ECOSYSTEM.md](../ECOSYSTEM.md),
[LUA_PATCHES.md](../../build/LUA_PATCHES.md).

Scratch artefacts (not committed): the downloaded sources, the probe bindings
and the link script, in the session scratchpad.
