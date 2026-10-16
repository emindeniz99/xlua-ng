# Lua vendor patch audit

This audit covers the retained PUC Lua 5.3.6 and 5.4.9 vendor patches.
It does not audit every bundled third-party library or LuaJIT change.
Commit IDs below refer to the retained Tencent/xLua Git history.

| Patch | Evidence and purpose | Current decision |
| --- | --- | --- |
| UWP process/environment guards | `4025950` (2017-08-02) explicitly says Windows Phone cannot use `getenv`, `_popen`, `_pclose`, or `system`. `1100b18` and `0fc1102` also disable the DLL loader on UWP. | Retain. Selected branches have host checks; real Windows SDK/UWP build and runtime remain unverified. |
| Android decimal point | `1100b18` (2017-09-08), titled platform compatibility changes, forces `lua_getlocaledecpoint()` to `'.'` on Android. | Retain existing parsing behavior. The commit does not identify a specific failing Android version or device. Native compilation does not prove behavior under every device locale. |
| `os.execute` removal | `62ea5f5` (2017-09-29), titled iOS 11 compatibility, removes the implementation and registration. | Retain. The original reason was iOS compatibility; its unconditional application also removes the API on desktop. Do not describe it as a complete security sandbox. |
| Configuration template | `f7a3f72` (2017-09-07) introduces `luaconf.h.in` and configurable `LUA_IDSIZE`; current CMake defaults to 120 rather than upstream 60. | Retain to preserve generated configuration and the source-description buffer size. |
| Plain `luaconf.h` in Lua 5.5.1 | `luaconf.h.in` was renamed to `luaconf.h`, with `LUA_IDSIZE` defaulting to 120 and `-DLUA_IDSIZE` as an override (CMake passes its `LUA_IDSIZE` option that way). A CMake-generated header cannot be found by the Unity WebGL build, which compiles the sources without CMake or include paths. The configured value is unchanged. | Retain for 5.5.1. Lua 5.3.6 and 5.4.9 keep the template. |
| Optional Lua 5.3 bytecode format | `3836179` (2018-09-14) introduces `LUAC_COMPATIBLE_FORMAT`. `Assets/XLua/Doc/compatible_bytecode.md` explicitly describes sharing bytecode between 32-bit and 64-bit runtimes. The patch uses a fixed 32-bit string length and removes the `size_t` header check. | Retain as opt-in for 5.3. Compiler and runtime must both enable it. It is not a promise of compatibility across Lua versions, all byte orders, or every architecture. |
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

Lua 5.5-specific bridge adaptation and device results are recorded in
[Lua 5.5 integration](LUA55.md). The Lua 5.3 bytecode extension remains
limited to that runtime family.
