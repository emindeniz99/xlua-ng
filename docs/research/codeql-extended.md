# CodeQL security-extended and Trail of Bits' queries (2026-10-28)

The owner decided on 2026-10-28 (Q67) that `codeql.yml` runs CodeQL's
`security-extended` suite for C/C++ and C#, and that Trail of Bits' C/C++ query
pack is added if its queries fit a C library and it can be pinned. This note
records what the pack offers, what both found, and how each result was
classified. The workflow is described in [.github/CI.md](../../.github/CI.md)
("CodeQL").

Marks: [C] confirmed here, [S] secondary, [U] unconfirmed.

## Result

- `security-extended` on both languages, and Trail of Bits'
  `trailofbits/cpp-queries` 0.3.0 (its security suite) on C/C++. [C]
- The extended run found one real bug in our code, fixed:
  `xlua_gc` on Lua 5.4 passed one argument where `lua_gc` reads two or three
  (below). [C]
- Every other result is a false positive in our code (4 after the fix, one
  of them the `lua_gc` call again, now on the Lua 5.3 build) or is in vendored
  Lua (126 in the first run, 106 without the crypto queries). Vendored results
  are now dropped before the upload. [C]
- C#: nothing new. The one result is the alert dismissed before
  (`cs/insufficient-key-size`, `Test/Native/SignatureCheck.cs:54`). [C]

## Runs

On branch `ci/codeql-extended`, CodeQL CLI 2.27.3 from `github/codeql-action`
v4.39.0, with `upload: true`:

| Run | Configuration | C/C++ results | C# results |
|---|---|---|---|
| [40704937882](https://github.com/emindeniz99/xlua-ng/actions/runs/40704937882) | `security-extended` + `trailofbits/cpp-queries@0.3.0` (default suite: security and crypto) | 130 (126 vendored) | 1 |
| [40707032703](https://github.com/emindeniz99/xlua-ng/actions/runs/40707032703) | `security-extended` + `tob-cpp-security.qls` only, with the `lua_gc` fix; vendored results dropped before the upload | 110 (106 vendored), 4 uploaded | 1 |

The digest check passed in run 40707032703, and its upload closed the 126
vendored alerts that run 40704937882 had opened on the branch (state
"fixed"). [C]

Before this change `main` had 0 C/C++ results and the same single C# result
with the default suite (code scanning analyses of 2026-10-28). [C]

## Trail of Bits' pack

- **Name and version.** `trailofbits/cpp-queries` on GHCR
  (`ghcr.io/trailofbits/cpp-queries`), tags 0.0.1, 0.1.0, 0.2.1 and 0.3.0;
  0.3.0 is the latest, released on 2026-04-17 from
  [trailofbits/codeql-queries](https://github.com/trailofbits/codeql-queries)
  (tag v0.3.0). Its manifest digest is
  `sha256:126366bdcadbc91f0d17ce837267c734dce39af1b8f7ab233f293cbe6e4d56e5`.
  The pack bundles `codeql/cpp-all` 9.0.0 (`cpp/src/codeql-pack.lock.yml`) and
  ran under CLI 2.27.3, which uses `codeql/cpp-all` 12.3.0 for its own
  queries. [C]
- **Pinning.** CodeQL pins a pack by version, not by digest, and a registry
  tag can be pushed again. The workflow therefore checks the manifest digest
  with an anonymous GHCR token before `init` downloads the pack. [C]
- **Licence.** AGPL-3.0 (`qlpack.yml`). We run the queries and do not ship or
  modify them; running them puts no obligation on xLua's code. [S]
- **Queries** (`doc/QUERIES.md`, metadata of each `.ql` at v0.3.0). None is
  tagged experimental. [C]
  - Security suite (kept): `tob/cpp/dec-overflow-when-comparing` (unsigned
    `n-- > 0` style wrap), `tob/cpp/no-null-terminator`,
    `tob/cpp/cstrnfinder` (wrong size to `strn*`),
    `tob/cpp/unsafe-implicit-conversions` (truncating or sign-changing
    implicit casts), `tob/cpp/inconsistent-retval-handling`,
    `tob/cpp/async-unsafe-signal-handler`, `tob/cpp/iterator-invalidation`
    (C++ only). The first four match the bridge's integer and string code.
  - Crypto suite (left out): 13 queries for OpenSSL and BIGNUM APIs. We use
    neither; `tob/cpp/use-of-legacy-algorithm` matched "DES" in Lua's
    `getlocalvardesc` 20 times in run 40704937882.
- **Decision.** Added, security suite only
  (`trailofbits/cpp-queries@0.3.0:codeql-suites/tob-cpp-security.qls`):
  its queries look for the integer and string mistakes a C bridge makes, it
  can be pinned to a published version and checked by digest, and in our
  code it produced 3 false positives and no real bug so far (the real bug came from `security-extended`). If its
  `inconsistent-retval-handling` keeps producing false positives, it can be
  excluded with a `query-filters` entry.

## Vendored code

CodeQL's `paths-ignore` applies to languages analysed without a build; for
a traced build GitHub's documentation says to limit what is built instead
(`workflow-configuration-options.md`, "Specifying directories to scan").
[S] Our libxlua build compiles the vendored Lua into the same library, so the
workflow now runs `analyze` with `upload: never`,
`.github/scripts/codeql-drop-vendored.py` removes results under
`build/lua-<version>/`, `build/luajit-2.1/` and `build/luasocket/`, and
`upload-sarif` sends the rest. The database still holds the vendored code,
so data flow through the Lua C API is unchanged. The unfiltered SARIF stays
in the `codeql-<language>-sarif` artifact. [C]

Vendored results of run 40704937882 (category (c), upstream code, not
patched):

| Rule | Count | Where |
|---|---|---|
| `tob/cpp/dec-overflow-when-comparing` | 59 | `llex.c` of 5.3.6, 5.4.9, 5.5.1: `zgetc`'s `((z)->n--) > 0` (`lzio.h:20`) expanded at each `next(ls)`, upstream's buffered reader, not analysed further [U]; `lstrlib.c:1653` (5.5.1), `while (ntoalign-- > 0)` padding loop |
| `tob/cpp/inconsistent-retval-handling` | 39 | `lapi.c`, `lauxlib.c`, `lbaselib.c`, `liolib.c`, `lstrlib.c`, `ltable.c`, `lutf8lib.c`, `ldblib.c` |
| `tob/cpp/use-of-legacy-algorithm` | 20 | `lparser.c` (`getlocalvardesc`); the crypto suite is no longer run |
| `tob/cpp/unsafe-implicit-conversions` | 7 | `lauxlib.c`, `lmem.c` (5.3.6), `loadlib.c` (5.4.9) |
| `cpp/unterminated-variadic-call` | 1 | `lbaselib.c:182` (5.3.6), see the next section |

## Results in our code

Alert numbers are those of `refs/heads/ci/codeql-extended`.

| Alert | Rule | Location | Class | Reason |
|---|---|---|---|---|
| #3 (run 1) | `cpp/unterminated-variadic-call` | `build/xlua_core.c:475` | (a) real bug, fixed | See below. The query's reading (a missing 0 terminator) is wrong; `lua_gc` has no terminator, but on Lua 5.4 the call passed too few arguments. |
| #3 (run 2) | `cpp/unterminated-variadic-call` | `build/xlua_core.c:480` | (b) false positive | After the fix the alert moved to the Lua 5.3 call, `lua_gc(L, what, data)`. 5.3's `lua_gc` takes exactly three arguments (`build/lua-5.3.6/src/lua.h:311`); the database holds the 5.3, 5.4 and 5.5 builds and the query takes 5.4's variadic declaration for every `lua_gc`. The vendored `lbaselib.c:182` (5.3.6) result is the same case. |
| #102 | `tob/cpp/inconsistent-retval-handling` | `build/xlua.c:1608` | (b) false positive | `accessor_field` compares `lua_touserdata`'s result with `NULL` first (`css == NULL ||`); the query counts the later arithmetic comparison. |
| #103 | `tob/cpp/inconsistent-retval-handling` | `build/xlua.c:787` | (b) false positive | `xlua_tryget_cachedud_checked` dereferences `lua_touserdata` only after `is_cs_data` (a full userdata with the bridge tag) and `userdata_size(L, -1) >= sizeof(int)`; a light userdata has size 0. |
| #104 | `tob/cpp/inconsistent-retval-handling` | `build/xlua_core.c:517` (522 after the fix) | (b) false positive | `errorfunc` compares `lua_tostring`'s result with `NULL` because a non-string error object is valid; that is the correct handling, the "usual" one the query compares with is code that already knows the value is a string. |

### The `lua_gc` fix

`xlua_gc` (`build/xlua_core.c`) forwarded `lua_gc(L, what, data)` on Lua 5.3
and 5.4. Lua 5.4's `lua_gc` is variadic: `LUA_GCGEN` (10) reads two ints and
`LUA_GCINC` (11) three (`build/lua-5.4.9/src/lapi.c:1200-1222`), so the
missing ones were whatever the registers or the stack held, and the
generational major multiplier or the incremental step multiplier and step
size took undefined values. `LuaGCOptions` (`Assets/XLua/Core/LuaTypes.cs`)
names 0 to 7 only, but `LuaDLL.Lua.lua_gc` is public and a cast reaches 10
and 11. [C]

The 5.4 branch now calls `lua_gc(L, what, data, 0, 0)`: 0 means "leave
unchanged" to both operations, and the others ignore extra arguments. 5.3 and
LuaJIT declare a fixed three-argument `lua_gc`, and the 5.5 branch already maps
every operation. The `value_helpers` check of
`Test/Native/native_regression.py` sets the step multiplier, switches to
generational and back with `LUA_GCINC`, and reads the multiplier back; it
failed before the fix on macOS arm64. Recorded in
[MIGRATING.md](../MIGRATING.md). [C]

Tested on macOS arm64 (Release dylibs): `native_regression.py` on Lua 5.3.6,
5.4.9 and 5.5.1, and `run_managed.py --source-gen` (522 tests in each of two
modes) on 5.4.9 and 5.5.1. Not tested: LuaJIT (its code path did not change),
Linux, Windows. [C]

## Left for the owner

- Dismiss alerts #3, #102, #103 and #104 as false positives (reasons above)
  when they appear on `main` after the merge (they may get new numbers there).
  They are open on the branch ref only so far; nothing was dismissed.
- The vendored alerts (#2 to #131 except the four above) were opened on the
  branch ref by the first run and closed by the second. They never reach
  `main`.
