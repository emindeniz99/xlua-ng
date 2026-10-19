# Bridge hardening review

Date: 2026-10-17. Method: code reading of the C#↔Lua bridge at commit
`003c62d` (branch `bridge-lock-metatables`), plus the defensive tests added
with `XLUA_LOCK_METATABLES`. No exploit or crash proofs were written. Markers:
[C] confirmed in the code or by a test, [S] secondary source, [U] unconfirmed
(a consequence inferred from the code, not observed).

Scope: every place where the bridge trusts state that Lua code can reach,
and the capabilities a default `LuaEnv` exposes by design. A default
`LuaEnv` is not a sandbox ([Security.md](../../Assets/XLua/Doc/Security.md)).
This list is the input for the `minimal` and `core` presets
([presets.md](../design/presets.md)).

Rule for defaults: hardening that changes upstream behaviour is opt-in in
`legacy` ("whoever wants it in legacy turns it on") and on by default in
`minimal`. `core` has no C# bridge, so the bridge items do not apply to it;
its own `LuaScript` tables get the same treatment.

## Summary

| # | Where the bridge trusts Lua-reachable state | Class | Status | legacy | minimal | core |
|---|---|---|---|---|---|---|
| 1 | Bridge metatables readable and writable through `getmetatable` | A/B | **done**: `XLUA_LOCK_METATABLES` | opt-in | on | own tables |
| 2 | Metamethods accept any first argument (`FastGetCSObj`) | B | **done**: `XLUA_CHECK_OBJECTS` (opt-in) | opt-in | on | n/a |
| 3 | Struct header (`fake_id`, `len`) trusted inside any full userdata | A | **done**: LuaJIT `Integer64` zeroed on push | on | on | n/a |
| 4 | `xlua.cast` sets a type metatable on any value | A/B | **done**: only C# objects, only to types they are instances of | on (owner decided 2026-10-17: no flag) | on | n/a |
| 5 | `xlua.genaccessor` accessors take any struct-shaped userdata | A | **done**: 3, and the tag check `XLUA_CHECK_STRUCT_ACCESSORS` (opt-in) | on / opt-in | on | n/a |
| 6 | `xlua.metatable_operation` (`xlua.getmetatable`, `xlua.setmetatable`) | B | **done**: set keeps the tag and `__gc`; get without them with `XLUA_RESTRICT_GETMETATABLE` (opt-in) | set fixed, get opt-in | get restricted | n/a |
| 7 | Registry and cache tables (`debug.getregistry`) | B (A with 3) | cache check **done**: `XLUA_CHECK_OBJECT_CACHE` (opt-in); reduced `debug`: design ([bridge-hardening.md](../design/bridge-hardening.md) B) | unchanged / opt-in | reduced `debug` | no `debug` |
| 8 | C closure upvalues (`debug.setupvalue`) | A | **done**: NULL check in `csharp_function_wrap` | on | on | no `debug` |
| 9 | `xlua` table and class tables are plain writable tables | B | `xlua` **done**: `XLUA_LOCK_XLUA_TABLE` (opt-in, `rawset` still shadows); class tables: design (C) | opt-in | on | n/a |
| 10 | `xlua.private_accessible`, `xlua.access`, `xlua.tofunction`, `xlua.get_generic_method` | C | **done**: `XLUA_RESTRICT_PRIVATE_ACCESS` (opt-in); reflection still reaches private members (12) | unchanged | hotfix only | n/a |
| 11 | `xlua.hotfix` | C | **done**: only with `HOTFIX_ENABLE` under `XLUA_RESTRICT_PRIVATE_ACCESS`, kept from replacement by `XLUA_LOCK_XLUA_TABLE`; signing exists (`SignatureLoader`) | unchanged | with `HOTFIX_ENABLE` | n/a |
| 12 | `CS.*`: any public type, reflection, `xlua.load_assembly` | C | `xlua.load_assembly`: **done**, `XLUA_NO_LOAD_ASSEMBLY` (opt-in); allowlist: design (E) | unchanged | type allowlist | n/a |
| 13 | Lua standard library and binary chunks | C | planned in presets | unchanged | trimmed | trimmed |
| 14 | Lua stack growth in C# callbacks | A | **done**: APICHECK run clean (init handler, push slack, room for type registration) | on | on | n/a |

Classes: **A** crash or memory safety, **B** bridge-state corruption,
**C** capability by design.

Since 2026-10-19 items 2, 5 and 7 are the bridge checks, which follow the
build type in every preset (`XLUA_BRIDGE_CHECKS`, [below](#bridge-checks-follow-the-build-type-2026-10-19));
the "opt-in" in their rows describes 2026-10-18. The opt-in defines of
2026-10-18 (items 2, 5, 6, 7, 9 to 12), their costs and the decisions left
for the owner are summarised in
[bridge-hardening.md](../design/bridge-hardening.md); the measurements are in
[Hardening defines (2026-10-18)](#hardening-defines-2026-10-18) below.

## 1. Bridge metatables (done)

**Evidence.**
- Object metatables get `__gc`, `__tostring`, `__index` and `__newindex`:
  `Utils.BeginObjectRegister` (`Assets/XLua/Src/Utils.cs:1106`) and
  `Utils.ReflectionWrap` (`Utils.cs:952`). The function metatable gets `__gc`
  in `ObjectTranslator.createFunctionMetatable`
  (`Assets/XLua/Src/ObjectTranslator.cs:747`). [C]
- Before this change none of them, nor `build/xlua.c`, set `__metatable`. [C]

**Assumption.** Only the bridge calls these functions, and only with the
object the metatable belongs to.

**What a script could do.**
- `getmetatable(obj).__gc(obj)` runs `StaticLuaCallbacks.LuaGC`
  (`StaticLuaCallbacks.cs:159`). That calls `ObjectTranslator.collectObject`
  (`ObjectTranslator.cs:814`), which frees the pool slot while the userdata
  stays alive. [C]
- The next C# object reuses the slot (`ObjectPool.Add`), so the old userdata
  resolves to that new object. [C]
- When the old userdata is finally collected, its `__gc` frees the new
  object's slot as well. The managed side stays memory-safe, because every
  pool access is bounds-checked (`ObjectPool.cs:90-135`) and casts are
  checked. The result is a confused deputy: a script operates on C# objects
  it was never given. [U]
- A script can also read the tag key (a light userdata) out of the
  metatable, call `__index`/`__newindex`/`__tostring` with foreign arguments
  (see 2), or overwrite those fields to change how every object of that type
  dispatches.

**Hardening (implemented).** `XLUA_LOCK_METATABLES`, a C# define:
- `Utils.LockMetatable` (`Utils.cs:55`) sets `__metatable = "locked"`.
- It is called for:
  - object, struct, enum, array, delegate and decimal metatables
    (`BeginObjectRegister`, `ReflectionWrap`);
  - class tables (`BeginClassRegister`, `ReflectionWrap`);
  - the function metatable, the weak userdata cache and the registry
    index-function tables;
  - tables passed to `xlua.setmetatable`.
- `init_xlua` locks the `CS` namespace metatable, `base()` proxies and the
  LuaJIT int64/uint64 metatable (`LuaEnv.cs:466-616`).
- The method is `[Conditional]`, so without the define nothing is compiled
  in.
- The native type checks use `lua_getmetatable`/`lua_rawget`, which ignore
  `__metatable`. `xlua.util`'s `state()` now uses `debug.getmetatable`. The
  generator templates already did.

**Cost.**
- One extra field per metatable, written once per type; no per-call cost.
- Not benchmarked. The managed suite took the same time in both builds
  (0.18–0.33 s per mode on Lua 5.5.1, macOS arm64), which is not a
  measurement.

**Compatibility.**
- Scripts that call `getmetatable` on C# objects or class tables to patch
  them break. They must use `debug.getmetatable`. Upstream users do this:
  https://github.com/Tencent/xLua/issues/77 (rawset into an exported class
  through `getmetatable`) and https://github.com/Tencent/xLua/issues/1169
  (extending a class through `debug.getregistry`). [S]
- luaunit and other code that compares `getmetatable(x)` with `nil` keeps
  working, because the placeholder is a non-nil string.

**Limits.** The lock covers the plain-Lua path only. `debug.getmetatable`,
`debug.setmetatable` (7) and `xlua.metatable_operation` (6) still reach the
raw tables.

**Tests.**
- `CaseMetatableLockUserdata`, `CaseMetatableLockTables` and
  `CaseMetatableLockKeepsUtilState` in
  `Test/UnitTest/StreamingAssets/luaCallCs.lua`.
- `run_managed.py --define XLUA_LOCK_METATABLES` passed in all three binding
  modes on Lua 5.5.1 and LuaJIT 2.1, macOS arm64, Mono 6.14.1.
- `hotfix_check.py --define XLUA_LOCK_METATABLES` passed.
- With the runtime change reverted, the three cases fail. [C]

## 2. Metamethods accept any first argument

**Evidence.**
- `StaticLuaCallbacks.ToString` (`StaticLuaCallbacks.cs:181`), the enum
  operators (`:45`, `:67`), `DelegateCall` and the generated method wrappers
  read the object with `FastGetCSObj` (`ObjectTranslator.cs:1408`).
- That calls `xlua_tocsobj_fast` (`build/xlua.c:94-100`), which returns the
  first `int` behind *any* userdata or light userdata, with no metatable
  check. [C]

**Assumption.** Argument 1 is a C# object, because the function was reached
through that object's metatable.

**Consequence.**
- Once a script holds a metamethod (see 1, 6, 7), it can pass another
  userdata: a LuaSocket handle, an `io` file, the tag light userdata. The
  first four bytes of that block are then used as a pool index. [C]
- The pool lookup is bounds-checked, so the result is the wrong C# object,
  not a native fault. [U]
- `xlua_tocsobj_fast` reads four bytes even from a zero-sized userdata
  (LuaJIT's `newproxy`), one small out-of-bounds read. [U]

**Hardening.**
- Metamethods and other cold paths (`__tostring`, `__eq`, enum operators)
  switch to `SafeGetCSObj`, which checks the tag
  (`xlua_tocsobj_safe`, `xlua.c:78-92`).
- Hot paths (generated method wrappers) keep `FastGetCSObj` but only after 1
  has made them unreachable with foreign arguments.
- Later, a type tag byte in the userdata header would make the check free
  ([luau.md](luau.md) idea 7).

**Cost.** One metatable lookup per call on the cold paths; measure with
`Test/Native/benchmark.py` before turning it on.

**Compatibility.** None for correct scripts.

**Default.** Under the same flag or `XLUA_STRICT_BRIDGE`: opt-in in legacy,
on in minimal.

**Correction (2026-10-18).** The hot paths are reachable with a foreign
argument without any metatable: `obj.Method` returns the method wrapper,
and `obj.Method(other)` calls it with `other` as `self`. The same holds for
field and property getters through `__index`. So the check has to cover the
wrappers too, not only the cold metamethods. [C] (`CaseCheckObjectsForeignSelf`)

**Implementation (2026-10-18): `XLUA_CHECK_OBJECTS`, a C# define.**
- `ObjectTranslator.FastGetCSObj` and `ObjectTranslator.Update` read the
  pool index through `toCSObjectIndex` (`ObjectTranslator.cs:1413`), which is
  `xlua_tocsobj_safe` with the define and `xlua_tocsobj_fast` without it.
- That covers every caller: generated wrappers (through
  `BindingApi.FastGetCSObj`, `Binding.cs:95`), reflection wrappers
  (`MethodWarpsCache.cs:198`, the field accessors in `Utils.cs`), and
  `StaticLuaCallbacks` (`ToString`, enum operators, `DelegateCall`,
  `DelegateCombine`, `DelegateRemove`, array indexers, `StaticCSFunction`).
  Emitted wrappers read `self` through `ObjectTranslator.GetObject` and the
  casters (not changed; whether they already refuse a foreign `self` was
  not checked).
- A foreign userdata then reads as no object: wrappers raise their usual
  `c# exception` error (a `NullReferenceException`), and `__tostring`
  returns `<invalid c# object>`. Structs and decimals (`fake_id == -1`) take
  the same path as before.
- No native change.

**Cost.** See [Hardening defines (2026-10-18)](#hardening-defines-2026-10-18).

**Tests.** `CaseCheckObjectsForeignSelf` in
`Test/UnitTest/StreamingAssets/luaBridgeHardening.lua`, with
`BridgeHardening.Natives.Forger()`
(`Test/UnitTest/xLuaTest/LuaCallCS/BridgeHardeningForLuaCallCS.cs`), which
builds a full userdata without a metatable whose first int is a real
object's pool index. Without the define `__tostring` of that userdata prints
the real object; with it, `__tostring`, a method wrapper and a field getter
refuse it, in all three binding modes. [C]

## 3. Struct header trusted inside any full userdata (done)

**Evidence.**
- `CSharpStruct` is `{int fake_id; unsigned len; char data[]}`
  (`xlua.c:884-888`).
- The pack/unpack helpers (`xlua.c:946-1160`) and the direct accessors
  (`xlua.c:1184-1209`) accept a block when `fake_id == -1` and the offset fits
  in `len`. Both values are read from the block itself. [C]
- On LuaJIT, `Integer64` (`build/i64lib.c:47-55`) is
  `{int fake_id = -1; int8_t type; <3 padding bytes>; union data}`, built by
  `lua_pushint64` (`i64lib.c:73-80`) and the `UINT_ESPECIALLY`
  `lua_pushuint64` (`i64lib.c:137-145`). [C]
- LuaJIT does not clear new userdata (`lj_udata_new` in
  `build/luajit-2.1/src/lj_udata.c`). [C]
- So an int64 userdata looks like a struct whose `len` is the type byte plus
  three uninitialised bytes. [C]

**Assumption.** A block with `fake_id == -1` is a real `CSharpStruct`.

**Consequence.**
- Any path that hands an int64 userdata to a struct helper can read or write
  past the 16-byte block. Two such paths exist: 4 (`xlua.cast`) and 5
  (`xlua.genaccessor`). [U]
- Only LuaJIT is affected. Lua 5.3+ keeps 64-bit integers as numbers.

**Before the fix.** With freed 16-byte blocks filled with `0xFF`, a new
int64 userdata read as a struct header of `fake_id = -1`,
`len = 0xFFFFFF00` (`native_regression.py luajit_int64_header`). In the
managed suite on LuaJIT, `xlua.genaccessor(0, 4)` returned a value for an
int64 instead of raising, and in generated code a `Gen4FloatStruct` field
getter on an int64 passed to `xlua.cast` returned a value in one of the two
runs. [C]

**Fix (implemented).**
- `lua_pushint64` and the `UINT_ESPECIALLY` `lua_pushuint64` zero the whole
  block with one `memset` before filling it (`build/i64lib.c:78-85`,
  `:143-150`; the layout is explained above `Integer64`, `:48-60`).
- On little-endian targets `len` is then the type byte, 0 for Int64 and 1 for
  UInt64, so every struct access on an int64 is rejected or stays inside the
  block. A big-endian target would read the UInt64 type byte as `1 << 24`;
  xLua has no big-endian target. [U]
- Lua 5.3+ pushes 64-bit integers as numbers (`i64lib.c:483-488`), and the
  other userdata xLua allocates (`xlua_pushcsobj`, `xlua_pushstruct`,
  `xlua_newstruct`, `css_clone`) already write the header they are checked
  by, so no other path has this pattern. [C]

**Cost.** One `memset` of 16 bytes per int64 push. Not benchmarked.

**Compatibility.** None.

**Default.** A bug fix, so on in every preset, without a flag.

**Tests.**
- `CaseStructAccessorRejectsInt64` in
  `Test/UnitTest/StreamingAssets/luaCallCs.lua`: it fills freed blocks of
  that size with `0xFF`, then checks that the `xlua.genaccessor` getter and
  setter reject 400 int64 and uint64 values, and, with generated code, that
  a `Gen4FloatStruct` field get and set on a cast int64 raise.
- `check_luajit_int64_header` in `Test/Native/native_regression.py` reads the
  header of 2000 pushed values directly.
- Before the fix both fail on LuaJIT 2.1 (macOS arm64); after it,
  `run_managed.py --gen-code --gen-code-minimize` passes on Lua 5.5.1 and
  LuaJIT 2.1, and `native_regression.py` passes on both. [C]

## 4. `xlua.cast` sets a type metatable on any value (done)

**Evidence.**
- `StaticLuaCallbacks.Cast` (`StaticLuaCallbacks.cs:922-945`) looks up the
  metatable of the target type and calls `lua_setmetatable(L, 1)`. It does
  not check that argument 1 is a C# object or that the object is an instance
  of the type. [C]
- The C API ignores `__metatable`, so the lock in 1 does not stop it. [C]

**Assumption.** Scripts cast C# objects to a base class or an interface.

**Consequence.**
- A table, a struct userdata or a LuaJIT int64 userdata can get the
  metatable of an unrelated class or struct type. [C]
- For struct types this feeds 3: `xlua_tostruct` (`xlua.c:914-927`) checks
  only the metatable's `[1]`. [U]
- For class types the managed casts in the wrappers reject the object, so
  this is corruption, not memory unsafety. [U]

- On a number or boolean, `lua_setmetatable` sets the metatable shared by
  every number or boolean. [C]

**Fix (implemented).** `StaticLuaCallbacks.Cast`
(`StaticLuaCallbacks.cs:934-948`) reads argument 1 with
`xlua_tocsobj_safe` and the object pool, and raises
`xlua.cast: #1 argument must be a C# object that is a <type>, got <...>`
unless the value is a C# object and `type.IsInstanceOfType(obj)`. That
accepts the documented uses: the object's own type, a base class, and an
interface it implements (the hidden-implementation case in
`XLua_Tutorial_EN.md` and the `LuaCallCSharp` tutorial). Struct userdata
(`fake_id == -1`), int64 userdata, tables, numbers, booleans, `nil` and
objects of unrelated types are rejected. The metatable lookup is unchanged
(`luaL_getmetatable` by `FullName`), so a cast to a type whose wrapper has
not been loaded still raises `no gen code for ...`. [C]

**Cost.** One object pool lookup and one `IsInstanceOfType` per cast; casts
are rare. Not benchmarked.

**Compatibility.** Casts of non-objects, and of objects to unrelated types,
now raise. Upstream documents only casts to a type the object implements;
the other casts either changed shared metatables or gave a metatable whose
wrappers then failed on the object. Documented in
`Assets/XLua/Doc/XLua_API_EN.md` (Cast Function).

**Default.** The review proposed opt-in in legacy. This change has no flag,
so it is on in every preset; per AGENTS.md the owner decides whether legacy
needs a flag that restores the upstream behaviour.

**Tests.** `CaseCastRejectsInvalid` in
`Test/UnitTest/StreamingAssets/luaCallCs.lua`:
- casts to a base class (`System.Object`), to an interface (`ITestLuaClass`)
  and to the object's own type still work;
- a table, a class table, an unrelated object, an int64, a struct, `nil`, a
  number and a boolean raise, and the number and boolean metatables are
  unchanged.

It fails before the fix (the table cast succeeds) and passes after it, in
all three binding modes on Lua 5.5.1 and LuaJIT 2.1.
`CaseStructAccessorRejectsInt64` (item 3) now gives the int64 the struct
metatable with `debug.setmetatable`, because `xlua.cast` refuses. [C]

## 5. `xlua.genaccessor` accessors

**Evidence.**
- `gen_css_access` (`xlua.c:1252-1267`) returns closures that take any full
  userdata.
- Light userdata is rejected (existing test
  `CaseGenAccessorRejectsNonStruct`). [C]
- No metatable check: the only guard is the header from 3. [C]

**Hardening.**
- 3 removes the known bad header.
- A stronger option is to require the tag (`is_cs_data`, `xlua.c:1269-1280`)
  in the accessor, as `css_clone` already does.

**Cost.** A metatable lookup per field access. This is the path
`ReImplementInLua` uses to bypass C#, so measure it first.

**Default.** Fix 3 in every preset; the tag check opt-in in legacy, on in
minimal.

**Implementation (2026-10-18): `XLUA_CHECK_STRUCT_ACCESSORS`, a C# define.**
- Each field type has a default and a checked getter and setter, thin
  wrappers around one body; the checked ones also require the bridge tag
  (`is_cs_data`, `build/xlua.c:89`). All of them share one field check,
  `accessor_field` (`xlua.c:1406`). `gen_css_access` (`xlua.c:1542`) hands
  out the checked ones (`checked_getters`, `xlua.c:1505`) when its own
  upvalue 1 is true; the `xlua.genaccessor` that `luaopen_xlua` registers
  has none.
- `xlua_check_struct_accessors` (`xlua.c:1564`, new export) replaces
  `xlua.genaccessor` with a closure whose upvalue is true. `LuaEnv` calls
  it right after `luaopen_xlua` when the define is set (`LuaEnv.cs:89`).
  Accessors created before the call stay unchecked.
- Size (Release, macOS arm64): `__text` grew by 48 bytes on Lua 5.5.1 and
  5.4.9 and shrank by 64 bytes on LuaJIT; the stripped dylib grew by 176
  bytes. Two earlier versions were measured and dropped: the check inlined
  into the twenty accessors (+2,860 bytes of `__text`), and one shared
  check reading the flag from a second upvalue (788 bytes smaller, but the
  default getter went from 13.6 to 15.9 ns).
- Native bridge version 108 (`xlua_get_lib_version`), since the C# side
  needs the new export.

**Tests.**
- `CaseCheckStructAccessorsForeign` (`luaBridgeHardening.lua`), with
  `BridgeHardening.Natives.FakeStruct()`: a userdata with a struct header
  and no metatable. The default accessors read and write it; the checked
  ones refuse it; both accept a decimal.
- `check_checked_accessors` in `Test/Native/native_regression.py`: every
  field type on a tagged, an untagged and a plain-metatable struct, invalid
  `genaccessor` arguments, accessors made before the switch, and the return
  value without a global `xlua` table.
- `Test/Fuzz/fuzz_struct.c` runs with the checked accessors when the last
  input byte is odd, and `foreign_op` adds a struct header without the tag,
  which only the default accessors accept.
- Before the change the managed case fails (the accessors accept the
  forged block) and the native checks have no export to call. [C]

## 6. `xlua.metatable_operation` (set done)

**Evidence.**
- `StaticLuaCallbacks.XLuaMetatableOperation`
  (`StaticLuaCallbacks.cs:1062-1104`) returns the raw type metatable on get.
- On set it stores any table as the new type metatable. It writes `[1]`, and
  now `__metatable` when the flag is on. It does not copy the tag or `__gc`.
  [C]

**Consequence.**
- Get bypasses the lock in 1, including `__gc`. [C]
- After a set, objects pushed later have no tag, so the safe checks no longer
  recognise them, and no `__gc`, so their pool slots are never freed. [C]
  Before the fix the new test case failed: an emitted method wrapper threw
  `NullReferenceException`, because `SafeGetCSObj` returned null.

**Fix (implemented, set).**
- On set, `XLuaMetatableOperation` (`StaticLuaCallbacks.cs:1126-1139`,
  helper `copyFieldIfMissing` at `:1077`) copies the tag key and `__gc` from
  the old type metatable into the new table when the new table lacks them,
  before it stores the table. [C]
- A table that sets its own `__gc` keeps it; such a finalizer has to release
  the object itself.
- Other fields the bridge adds at load time (`__band`/`__bor` for enums,
  `__pairs` for `IEnumerable`) are behaviour, not bookkeeping, and are left
  to the script's table.
- The copy writes into the table the script passed. A Lua table that uses
  that same table as its metatable (the `ReImplementInLua` example 2) gets a
  `__gc` that does nothing for a table, because `xlua_tocsobj_safe` returns
  -1. [C]

**Get (unchanged, documented).** Get returns the raw type metatable with
`xlua_rawgeti`, which ignores `__metatable`. With `XLUA_LOCK_METATABLES` the
returned table carries `__metatable = "locked"`, but its `__gc` and tag are
still reachable, so get still bypasses the lock in 1. That matches the
lock's documented scope (`XLua_API_EN.md`: the lock does not affect
`xlua.metatable_operation`). Restricting get stays a proposal for minimal:
drop it, or return a copy without `__gc`. [C]

**Cost.** Two raw lookups per set; sets are rare.

**Compatibility.** None for set. Code that reads the type metatable to patch
it (issue 1169) can keep using get in legacy.

**Default.** The set fix is on in every preset, without a flag; get is
unchanged in legacy and restricted in minimal.

**Implementation (2026-10-18, get): `XLUA_RESTRICT_GETMETATABLE`, a C#
define.** Get returns a shallow copy of the type metatable without `__gc`
and the tag key (`copyWithoutBookkeeping`, `StaticLuaCallbacks.cs:1142`).
The copy keeps `[1]`, `__index`, `__newindex`, `__tostring` and the other
metamethods, so a script can still read them or build a new metatable from
them; set copies `__gc` and the tag back. Editing the copy in place does not
change the type. Cost: one table copy per get; gets are rare.

**Tests.** `CaseRestrictGetMetatable` (`luaBridgeHardening.lua`): with the
define the result is not the raw metatable, has no `__gc` and no light
userdata key, an edit does not reach live objects, and get, set and new
objects work; without it the raw metatable comes back.
`CaseMetatableOperationSetKeepsBridge` passes in both states. [C]

**Tests.** `CaseMetatableOperationSetKeepsBridge` in `luaCallCs.lua`, with
`MetatableOperationProbe` (`Test/UnitTest/xLuaTest/LuaTestObj.cs:2237`):
- it sets a copy of the type metatable without the tag and `__gc`;
- it creates ten objects and checks that methods, fields and a C# method
  taking `object` see them as C# objects;
- it collects them in Lua and checks with weak references that the C#
  objects were released.

It fails before the fix and passes after it, in all three binding modes on
Lua 5.5.1 and LuaJIT 2.1, and with `--define XLUA_LOCK_METATABLES` on Lua
5.5.1. [C]

## 7. Registry and cache tables

**Evidence.** All of these live in the registry, which
`debug.getregistry()` returns as a plain table. [C]
- Type metatables are keyed by `Type.FullName` (`luaL_getmetatable`,
  `LuaDLL.cs:407`) and by integer type ids (`getTypeId`,
  `ObjectTranslator.cs:1057`).
- The userdata cache (`cacheRef`, `ObjectTranslator.cs:263-270`).
- The index-function tables (`LuaEnv.cs:123-151`).
- `CS`, the main thread, the error function.
- On LuaJIT, the int64 metatable at the fixed slot 8 (`INT64_META_REF`,
  `i64lib.c:41`).

**Assumptions and consequences.**
- `xlua_tryget_cachedud` (`xlua.c:397-407`) returns whatever is stored under
  an object index, without checking that it is a userdata for that index.
  Replacing a cache entry makes every later push of that C# object return a
  value the script chose. [C]
- `BeginObjectRegister` reuses an existing `registry[FullName]` table. A
  table planted there before the type loads becomes the type metatable. [C]
- Replacing `registry[8]` on LuaJIT changes what `lua_isint64` accepts and
  which metatable new int64 values get. [C]

**Hardening.**
- In minimal, ship a reduced `debug` table (`traceback`, `getinfo`) and keep
  the full library for development builds and debuggers; core already has no
  `debug`.
- Independently and cheaply: in `xlua_tryget_cachedud`, accept only a full
  userdata whose stored index equals the key.

**Cost.** None for the reduced table. One type check and compare per cached
push.

**Compatibility.** Debuggers and profilers (EmmyLua, `perf.profiler`,
`xlua.util.print_func_ref_by_csharp`) need the full `debug` library.

**Default.** Legacy unchanged; reduced `debug` in minimal release builds.

**Implementation (2026-10-18, cache): `XLUA_CHECK_OBJECT_CACHE`, a C#
define.** `ObjectTranslator` looks the cache up through
`tryGetCachedUserdata` (`ObjectTranslator.cs:1399`), which calls the new
export `xlua_tryget_cachedud_checked` (`xlua.c:576`) with the define. It
accepts an entry only if it is a full userdata with the bridge tag, at least
an int long, holding the key; anything else is a miss, and `Push` then makes
a new userdata, as for an entry the collector removed.

**Reduced `debug`: not implemented.** xLua's own Lua code (the `setfenv`
emulation, `xlua.util`, the code generation templates) needs more than
`traceback` and `getinfo`, and the Editor's `LuaEnv` would be reduced by a C#
define too. Options and cost: [bridge-hardening.md](../design/bridge-hardening.md) B.

**New finding: the enum cache.** `[GCOptimize]` enums in generated code
cache their userdata in the enum's class table, keyed by the integer value
(`LuaWrapPusher.tpl.txt:37-38`, `:62-64`). A script needs no `debug` to
replace an entry: `CS.E[1] = x` once the value has been pushed. The checked
lookup does not cover it. [C] by code reading, not run. See
[bridge-hardening.md](../design/bridge-hardening.md) D.

**Tests.** `CaseCheckObjectCache` (`luaBridgeHardening.lua`) finds the cache
through `debug.getregistry`, plants another object's userdata, a string and
a table under an object's index, and pushes the object again: without the
define the planted value comes back, with it a userdata for the right
object. `check_checked_cache` (`native_regression.py`) covers a hit, six
kinds of miss (string, table, a userdata holding another index, an untagged
userdata, a zero-sized tagged userdata, a light userdata), an empty slot and
the stack balance, against the unchecked lookup. [C]

## 8. C closure upvalues (done)

**Evidence.**
- `debug.setupvalue` also writes the upvalues of C closures
  (`aux_upvalue`, `build/lua-5.5.1/src/lapi.c:1373-1383`). [C]
- `csharp_function_wrap` (`xlua.c:795-816` after the fix) calls
  `lua_tocfunction(L, lua_upvalueindex(1))` and, before the fix, called the
  result without a NULL check. [C]
- The other trampolines index managed lists, which throw a C# exception
  instead (`FixCSFunction`, `StaticLuaCallbacks.cs:104`;
  `CallCSharpWrapper`, `ObjectTranslator.cs:1451`). [C]

**Consequence.**
- A script with `debug` can replace that upvalue with a non-C value. The
  wrapper then calls a NULL function pointer: the managed suite crashed with
  SIGSEGV in `csharp_function_wrap` on Lua 5.5.1 and LuaJIT 2.1. [C]
- The other C closures (`obj_indexer`, `cls_indexer`) keep their tables in
  upvalues, so a script can redirect lookups for a whole type. That is
  corruption, not unsafety, because the C# getters check types. [U]

**Fix (implemented).** `csharp_function_wrap` raises
`invalid c# function: upvalue 1 is not a C function` with `luaL_error`
when `lua_tocfunction` returns NULL (`xlua.c:797-801`).

**Limits.** Another C function or C closure in that upvalue still runs,
with the wrapper's upvalues instead of its own; a closure that reads its
upvalues, such as `coroutine.wrap`'s, can then misbehave. [U] The reduced
`debug` table from 7 closes the rest.

**Cost.** One branch per wrapped call. Not benchmarked.

**Compatibility.** None.

**Default.** NULL check in every preset, without a flag; reduced `debug` as
in 7.

**Tests.** `CaseCSharpFunctionUpvalueReplaced` in
`Test/UnitTest/StreamingAssets/luaCallCs.lua` replaces the upvalue of a
fresh `StackDumpHelpers.GetDump()` closure with a string and a table, and
checks that the call raises and that another closure still works. Before
the fix the managed suite crashed in this case on Lua 5.5.1 and LuaJIT 2.1;
after it the case passes in all three binding modes on both. [C]

## 9. `xlua` table and class tables are writable

**Evidence.**
- `xlua` is a plain global table (`ObjectTranslator.OpenLib`,
  `ObjectTranslator.cs:703`; `luaopen_xlua`, `xlua.c:1309-1319`).
- `init_xlua` adds Lua functions to it (`LuaEnv.cs:564-605`).
- Class tables are plain tables holding static members and
  `UnderlyingSystemType` (`Utils.BeginClassRegister`, `Utils.cs:1338`).
- Namespace tables cache types with `rawset`. [C]

**Consequence.**
- One script can replace `xlua.hotfix`, `xlua.access`, `typeof`, `cast` or a
  static method for every other script in the same `LuaEnv`. [C]
- Replacing `UnderlyingSystemType` makes the `xlua.*` helpers resolve to
  another type: `getType`, `StaticLuaCallbacks.cs:948`. [C]
- This is expected in Lua, but it matters when scripts of different trust
  share an env.

**Hardening.** In minimal:
- expose `xlua` and class tables through read-only proxies (`__index` to the
  real table, `__newindex` error, `__metatable` lock);
- let `xlua.setclass` write through a C# entry point.

**Cost.** One more `__index` hop per static access; measure.

**Compatibility.** Code that adds functions to `xlua` or `rawset`s into
class tables breaks.

**Default.** Opt-in in legacy, on in minimal.

**Finding (2026-10-18).** A proxy table cannot refuse `rawset`:
`rawset(proxy, k, v)` stores a field that hides the real one. Read-only
proxies therefore protect only against scripts without `rawset` and
`debug`. [C] (`CaseLockXLuaTable`)

**Implementation (2026-10-18, `xlua`): `XLUA_LOCK_XLUA_TABLE`, a C#
define.**
- At the end of `init_xlua` the global `xlua` (and `package.loaded.xlua`
  where LuaJIT put it) becomes an empty proxy: `__index` is the real table,
  `__newindex` raises `xlua is read-only`, `__pairs` iterates the real table
  (Lua 5.2+; LuaJIT ignores it), `__metatable` is `"locked"`
  (`LuaEnv.cs:669-688`).
- `xlua.hotfix`, `xlua.getmetatable`, `xlua.setmetatable`, `xlua.setclass`
  and the `CS` namespace metatable use `xlua.access`,
  `xlua.private_accessible`, `xlua.metatable_operation`, `rawset` and
  `error` as captured at start-up (`LuaEnv.cs:524-529`); without the define they
  look them up when they run, as upstream.
- `xlua.util.auto_id_map` installs its `xlua.hotfix` with `rawset`
  (`EmbeddedModules.cs`), which works on the real table and on the proxy.
- Limits: `rawset` shadows, the global `xlua` can be replaced, and `CS`,
  `typeof`, `cast` and `base` stay writable globals.

**Class tables: not implemented.** Static methods are raw fields of the
class table; the options (front tables, userdata proxies) and their costs
are in [bridge-hardening.md](../design/bridge-hardening.md) C.

**Tests.** `CaseLockXLuaTable` (`luaBridgeHardening.lua`): with the define,
assignments raise, `getmetatable(xlua)` is `"locked"`, `pairs` lists the
fields (not on LuaJIT), and `xlua.getmetatable` keeps working after
`rawset(xlua, "metatable_operation", f)`; without it, assignments work and
the replaced function is used. `hotfix_check.py` with all the defines
passes, including `auto_id_map`. [C]

## 10. Private member access helpers (capability)

**Evidence.**
- `xlua.private_accessible` (`StaticLuaCallbacks.cs:1037`) opens the private
  members of a type and its bases.
- `xlua.access` (`:972`) reads and writes any field or property, including
  private and static ones.
- `xlua.tofunction` (`:1127`) and `xlua.get_generic_method` (`:1179`) wrap
  any `MethodBase`, including non-public generic methods. [C]

**Use.** `xlua.hotfix` needs `access` and `private_accessible`
(`LuaEnv.cs:564-583`).

**Hardening.** In minimal, compile these only with `HOTFIX_ENABLE`, and limit
`xlua.access` to `__Hotfix*` fields.

**Cost.** None.

**Compatibility.** Scripts that use them outside hotfix need legacy or an
opt-in define.

**Implementation (2026-10-18): `XLUA_RESTRICT_PRIVATE_ACCESS`, a C#
define.**
- Without `HOTFIX_ENABLE`, `ObjectTranslator.OpenLib` does not register
  `xlua.access` and `xlua.private_accessible` (`ObjectTranslator.cs:757`),
  and `init_xlua` does not define `xlua.hotfix` (`LuaEnv.cs:613`).
- With `HOTFIX_ENABLE`, `xlua.access` accepts only field names that start
  with `__Hotfix` or `_c__Hotfix` (`StaticLuaCallbacks.cs:1042`), the fields
  the injector adds; `xlua.private_accessible` stays, because hotfix patches
  use private members.
- In both cases `xlua.tofunction` refuses a non-public `MethodBase`
  (`StaticLuaCallbacks.cs:1263`) and `xlua.get_generic_method` lists public
  methods only (`:1326`).

**Limit (2026-10-18).** Reflection reaches the same members without these
helpers: `obj:GetType():GetDeclaredField("secretField"):GetValue(nil)`
reads a private static field with every define on. The restriction matters only
once item 12 closes reflection. [C] (`CaseRestrictPrivateAccess`)

**Tests.** `CaseRestrictPrivateAccess` (`luaBridgeHardening.lua`) in both
states; the existing private-access cases in `luaCallCsUpstream.lua` and the
`xlua.access` checks in `luaCallCs.lua` are skipped when the define leaves
the helpers out. `hotfix_check.py --define XLUA_RESTRICT_PRIVATE_ACCESS`
(with `HOTFIX_ENABLE`) patches and restores every fixture method, and
`xlua.access` refuses an ordinary field (`HotfixFixture.cs`). [C]

## 11. `xlua.hotfix` (capability)

**Evidence.**
- `xlua.hotfix` (`LuaEnv.cs:564`) replaces the body of any injected method.
- Injection is limited to `[Hotfix]` types at build time. [C]

**Hardening.**
- Keep it, behind `HOTFIX_ENABLE`.
- Protect it with 9, so another script cannot replace it.
- Sign patches with `SignatureLoader`.

**Implementation (2026-10-18).** Under `XLUA_RESTRICT_PRIVATE_ACCESS` without
`HOTFIX_ENABLE` there is no `xlua.hotfix` (10). `XLUA_LOCK_XLUA_TABLE` keeps
it from plain replacement and makes it use the `xlua.access` captured at
start-up (9). Signing already exists (`SignatureLoader`,
[signature.md](../../Assets/XLua/Doc/signature.md)).

## 12. `CS.*` and reflection (capability)

**Evidence.**
- `xlua.import_type` (`StaticLuaCallbacks.cs:851`) resolves any type name
  through `ObjectTranslator.FindType` (`ObjectTranslator.cs:767`).
- `xlua.load_assembly` (`:810`) calls `Assembly.Load` with a script-supplied
  name or path.
- The reflection fallback then exposes every public member. [C]
- This includes, for example:
  - `CS.System.IO.File` and `Directory`;
  - `CS.System.Diagnostics.Process`;
  - `CS.System.Type.GetType`, `Activator` and `Assembly`;
  - `CS.System.Runtime.InteropServices.Marshal`, which reads and writes raw
    memory, so memory safety is gone by design once it is reachable;
  - `CS.UnityEngine.Application.OpenURL`. [C]
- Upstream confirms that blacklisting only removes generated code; reflection
  still reaches the type: https://github.com/Tencent/xLua/issues/1035 and
  https://github.com/Tencent/xLua/issues/809. [S]

**Hardening.** In minimal:
- add a type allowlist in `FindType`/`getTypeId` (generated types plus a
  listed set);
- drop `xlua.load_assembly`;
- keep the reflection fallback opt-in, as presets.md already plans.

Core has no bridge.

**Cost.** One dictionary lookup per first use of a type.

**Compatibility.** Large. Scripts that reach arbitrary .NET types need
legacy.

**Implementation (2026-10-18, part): `XLUA_NO_LOAD_ASSEMBLY`, a C# define.**
`OpenLib` does not register `xlua.load_assembly` (`ObjectTranslator.cs:750`).
Reflection still reaches `System.Reflection.Assembly`, so this is defence in
depth. Test: `CaseNoLoadAssembly`.

**Allowlist: not implemented.** Every C# object's `GetType()` returns a
`System.Type`, whose members reach any type and any private member, so a
type allowlist must also take reflection away from scripts. Design and cost:
[bridge-hardening.md](../design/bridge-hardening.md) E.

## 13. Lua standard library and binary chunks (capability)

Already listed in [Security.md](../../Assets/XLua/Doc/Security.md) and
planned in [presets.md](../design/presets.md):
- `io`, `os`, `package.loadlib`, `dofile`, `loadfile` and LuaSocket;
- binary chunks: PUC Lua does not verify bytecode, so a crafted chunk can
  corrupt memory.

No new findings.

## 14. Lua stack growth in C# callbacks (done)

- Lua guarantees `LUA_MINSTACK` (20) free slots when a C function starts.
- Commit ee7141e (Tencent/xLua#1153) already added `Utils.EnsureStack` for:
  wrappers with more than 10 `out`/`ref` results (reflection, emitted,
  generated, `GEN_CODE_MINIMIZE`); `LuaFunction.Call` with many arguments;
  delegate and interface bridges, including `params` arrays
  (`CodeEmit.emitEnsureStack`, `LuaDelegateBridge.tpl.txt`). [C]
- The other input-sized paths push a fixed number of values. `params` and
  varargs from Lua are read in place (`MethodWarpsCache.cs`), and `LuaTable`
  iteration (`LuaTable.cs`, `ObjectCasters.cs`) holds one key and one
  value. [C]
- Recursion depth across Lua → C# → Lua is bounded by Lua's C-call limit
  (`LUAI_MAXCCALLS` = 200, `build/lua-5.5.1/src/ldo.h:62`), which raises a
  Lua error rather than overflowing the native stack. [C]

**Method.**
- Lua 5.5.1 plugin built Debug with `-DLUA_USE_APICHECK -O1` (no `NDEBUG`,
  so `luai_apicheck` asserts); macOS arm64, Mono 6.14.1; managed suite in
  all three binding modes.
- ASan did not work under Mono on macOS. With `DYLD_INSERT_LIBRARIES` set to
  the Xcode 27 ASan runtime, the test process stopped with "Interceptors are
  not working". [C]

**Findings, in the order the asserting build hit them.**
1. *The `lua_pcallk` error-function assertion (ROADMAP) is a real bug, not a
   harness artefact.*
   - `LuaEnv`'s constructor ran `DoString(init_xlua)` while `errorFuncRef`
     was still -1. `load_error_func` pushed `registry[-1]`, which is nil, and
     `lua_pcall` got a nil message handler (`lapi.c:1091`). [C]
   - Without API checks this only mattered if `init_xlua` raised: Lua then
     calls a nil handler and reports an error in error handling instead of
     the message. [U]
   - Fix: create the handler before the first `DoString`
     (`LuaEnv.cs:113-117`). [C]
2. *Type registration on top of pushed results.*
   - A wrapper with 15 `out` values asks for room for 16, then pushes an
     object of a type that is not loaded yet. `getTypeId` and
     `TryDelayWrapLoader` then push the metatable and run the whole
     registration (`luaL_newmetatable`, `Begin/EndObjectRegister`,
     `RegisterFunc`) past `ci->top`. The existing
     `CaseStackRoomRegisterTypeAfterOuts` hit an assertion in
     `lua_pushcclosure`. [C]
   - ASan did not see it, because the stack allocation has slack beyond
     `ci->top`. [U]
   - Fix: `TryDelayWrapLoader` asks for `Utils.RegisterStack` (20) slots
     before it loads a type (`ObjectTranslator.cs:147`), and the first-use
     path of `getTypeId` asks for 3 (`ObjectTranslator.cs:1077`). Nested
     registrations ask again. [C]
3. *Temporaries of one push.*
   - `EnsureStack(L, n)` reserved one slot per value, but `xlua_pushcsobj`
     holds the userdata, the userdata cache table and a copy of the userdata
     at once (`cacheud` in `build/xlua.c`). With 20 object `out` values the
     last pushes went past `ci->top`: the new `CaseStackRoomObjectOuts` hit
     an assertion in `lua_pushvalue`. [C]
   - Fix: `EnsureStack` adds `Utils.PushSlack` (5)
     (`Utils.cs:1641-1651`), and the emitted check does the same
     (`CodeEmit.cs:772`). The generated templates call `EnsureStack`, so
     they get it too. [C]
   - Paths that push 10 values or fewer still skip the check: 10 values plus
     5 slots of slack fit in `LUA_MINSTACK`.

With these fixes the APICHECK build passes the managed suite in all three
binding modes (335 tests per mode). [C]

**Cost.** One `lua_checkstack` per first use of a type, and 5 more slots in
the existing checks. Not benchmarked.

**Tests.**
- `CaseStackRoomObjectOuts` and `CaseStackRoomObjectOutsGen`
  (`Test/UnitTest/StreamingAssets/luaCallCsUpstream.lua:137-149`; helpers
  `StackRoom.ObjectOuts` and `StackRoomGen.ObjectOuts` in
  `Test/UnitTest/xLuaTest/LuaCallCS/UpstreamForLuaCallCS.cs`) return 20
  object `out` values in a new `LuaEnv`.
- The existing `CaseStackRoomRegisterTypeAfterOuts` covers finding 2.
- Release builds pass these cases before and after the fix; the overflows
  show only with `LUA_USE_APICHECK`, or with ASan where it works (Linux,
  `sanitize.sh`).

**Limits.**
- C# code that calls `LuaFunction.Call` or a delegate bridge outside any Lua
  callback relies on the base stack's `LUA_MINSTACK` slots, as before.
- Lua 5.3, 5.4 and LuaJIT were not run with API checks here. Since
  2026-10-18 they have been, with every preset, on macOS and under ASan on
  Linux: [apicheck-runs.md](apicheck-runs.md). `sanitize.sh` builds with the
  checks.

## Hardening defines (2026-10-18)

**Method.** `Test/Native/benchmark.py --modes gencode --filter lua_cs
--runs 9` (generated wrappers, the IL2CPP path), with `--define` for each
define, against the same Lua 5.5.1 Release plugin; the previous plugin and
runtime (`3b53a7e`) as the baseline, with the new benchmark cases copied in.
macOS 27 arm64, Mono 6.14.1. Other jobs ran on the machine (load average
10 to 20), so each configuration ran in three interleaved rounds and the
table gives the lowest median per case, in ns per operation. The accessor
rows come from a second series of three rounds after the accessors were
split (see item 5). [C]

| Case | baseline | no define | `XLUA_CHECK_OBJECTS` | `XLUA_CHECK_STRUCT_ACCESSORS` | `XLUA_CHECK_OBJECT_CACHE` | `XLUA_LOCK_XLUA_TABLE` |
|---|---|---|---|---|---|---|
| instance method call | 81.0 | 80.7 | 93.9 (+13) | 81.1 | 80.6 | 82.3 |
| field get / set | 91.6 / 76.1 | 89.6 / 75.9 | 99.4 / 85.8 (+10) | 90.4 / 74.6 | 89.4 / 76.2 | 90.4 / 75.3 |
| property get / set | 91.6 / 75.5 | 92.3 / 75.2 | 100.1 / 86.0 (+8 / +11) | 92.0 / 74.9 | 89.9 / 75.4 | 92.3 / 76.3 |
| static method call | 48.8 | 48.0 | 48.3 | 48.4 | 47.4 | 47.8 |
| return an object Lua holds (`o:Self()`) | 164.9 | 166.9 | 177.5 (+11, the call) | 165.2 | 181.2 (+14) | 165.8 |
| `xlua.genaccessor` get / set | 13.6 / 15.9 | 13.7 / 16.1 | – | 24.5 / 29.5 (+11 / +13) | – | – |
| `xlua.cast` field lookup | 5.6 | 5.7 | 5.7 | 5.7 | 5.7 | 11.7 (+6) |

- `XLUA_CHECK_OBJECTS` costs one metatable lookup per C# object read: about
  10 to 13 ns on an 80 to 100 ns instance call, field or property access
  (+11% to +16%). Static calls do not read an object.
- `XLUA_CHECK_STRUCT_ACCESSORS` almost doubles the accessor itself (+11 to
  13 ns); the field accesses that generated code makes do not use these
  accessors.
- `XLUA_CHECK_OBJECT_CACHE` adds about 14 ns (+8%) to each push of an
  object Lua already holds.
- `XLUA_LOCK_XLUA_TABLE` adds one `__index` hop (+6 ns) to each `xlua.f`
  lookup; code that keeps `local cast = xlua.cast` pays nothing.
- `XLUA_RESTRICT_GETMETATABLE` copies a table per `xlua.getmetatable` call;
  `XLUA_RESTRICT_PRIVATE_ACCESS` and `XLUA_NO_LOAD_ASSEMBLY` add no work to
  any call. Not benchmarked.
- The managed suite (356 tests per mode) took 0.12 to 0.51 s per mode in
  every configuration; the differences are within the run-to-run noise.
- Plugin size: see item 5; the C# changes are a few hundred bytes of IL per
  define and nothing without it (`#if`). Player size was not measured.

**Verification.**
- `run_managed.py --gen-code --gen-code-minimize` passes 356 tests per mode:
  on Lua 5.5.1 without defines, with each new define and with all of them
  (with and without `XLUA_LOCK_METATABLES`); on LuaJIT 2.1 and Lua 5.4.9
  without defines and with all of them. macOS arm64, Mono 6.14.1.
- With every define set and the runtime and plugin of `3b53a7e`, the seven
  new cases fail and the other 349 pass.
- `native_regression.py` and `check_exports.py` pass on Lua 5.5.1, 5.4.9 and
  LuaJIT; `check_pinvoke_abi.py` passes.
- `hotfix_check.py` passes without defines and with all of them.
- `Test/Native/sanitize.sh` (ASan, UBSan, Lua API checks) passes on Lua
  5.3.6, 5.4.9, 5.5.1 and LuaJIT, and the managed suite with every define
  passes under ASan on 5.5.1 (Linux arm64, `xlua-analysis:26.04`).
  `fuzz_struct` ran 120 s each on 5.5.1 and 5.4.9 without a finding.
- `Test/Native/analyze.sh` (clang-tidy, `clang --analyze`) is clean on all
  four runtimes, and `coverage.sh` keeps `xlua.c` at 100% of lines,
  functions and branches (336 branches on 5.3.6 and 5.4.9, 350 on 5.5.1).
  Same container.
- After merging main (`bf0fb21`, 403 tests per mode), the same runs pass
  again with `--gen-assembly` added (5.5.1 without defines, with each and with
  all; LuaJIT and 5.4.9 with all), as do `hotfix_check.py`,
  `managed_imports.py --check`, `sanitize.sh` on the four runtimes and
  `editor_compile_check.py` with the seven defines in its feature list (seven
  editors, Mono's csc: 322 ok, 7 skipped, no errors). No Unity editor was
  started.
- Not run: Unity (Editor and players), Windows, Android, iOS, WebGL, Lua
  5.3.6 on the host, LuaJIT with `UINT_ESPECIALLY`.

## Bridge checks follow the build type (2026-10-19)

Owner decision 2026-10-19 (6a): a `LuaEnv` runs trusted code, so checks that
only catch script mistakes are debugging aids. Items 2, 5 and 7 became one
symbol, `XLUA_BRIDGE_CHECKS`, defined in the Editor, Development Builds and
`XLUA_GENERAL` ([bridge-hardening.md](../design/bridge-hardening.md#classification-owner-decision-2026-10-19-6a)
has the classification and the decision). Method: code reading, the managed
suite with and without `XLUA_NO_BRIDGE_CHECKS`, a native microbenchmark and
`benchmark.py`. macOS 27 arm64, Mono 6.14.1, Lua 5.5.1 Release plugins, HEAD
`7a1006a3` as the baseline.

**Finding: the fast reader dereferenced light userdata.** [C]
- `xlua_tocsobj_fast` (`build/xlua.c`, before the change) returned
  `*(int *)lua_touserdata(L, index)` for any non-NULL result, and
  `lua_touserdata` returns the stored pointer of a light userdata. xLua
  pushes a C# `IntPtr` as a light userdata (`ObjectTranslator.cs`, the
  `IntPtr` pusher), so `obj.Method(ptr)`, a `.` where `:` was meant, read the
  int at the address `ptr` names. A zero-sized userdata (LuaJIT's
  `newproxy()`) gave a 4-byte read past its block.
- `xlua_tocsobj_safe` read the same way when a light userdata had a
  metatable holding the tag (all light userdata share one metatable).
- `CaseLightUserdataIsNoObject` (`luaBridgeHardening.lua`) builds an `IntPtr`
  to unmanaged memory holding a real object's pool index. With the HEAD
  plugin and `--define XLUA_NO_BRIDGE_CHECKS`, `__tostring` of it printed that
  object (the case failed in both luaunit modes); `check_value_helpers`
  (`native_regression.py`) failed on the first light-userdata assertion.
- Fix, in every build: the fast reader requires a non-NULL
  `lua_touserdata` and `lua_rawlen` (`lua_objlen` on LuaJIT) of at least
  `sizeof(int)`, which is 0 for a light userdata; the safe reader requires
  `lua_type == LUA_TUSERDATA`. Both cases pass after it.

**Cost of the fix.** A loop calling the export on a tagged object userdata
(`tocsobj_bench.c`, five runs of 10^8 calls per library, three interleaved
rounds, load average 36 to 106 from other jobs; the quietest rounds):

| Export | HEAD | now |
|---|---|---|
| `xlua_tocsobj_fast` | 2.4 to 2.5 ns | 4.3 ns |
| `xlua_tocsobj_safe` | 12.6 to 13.0 ns | 13.8 to 15.7 ns |

A first version with `lua_type` as well cost 5.5 to 6.9 ns. `__text` of
the Lua 5.5.1 dylib grew by 76 bytes.

**Cost of the checks by build type.** `benchmark.py --modes sourcegen
--filter lua_cs --runs 9`, three interleaved rounds of five configurations:
HEAD's minimal (`--define XLUA_PRESET_MINIMAL`, HEAD plugin), minimal now as
a release player (`XLUA_NO_BRIDGE_CHECKS`) and as a debug build, HEAD's
legacy, and legacy now as a release player. The table with the lowest
medians is in [bridge-hardening.md](../design/bridge-hardening.md#the-bridge-checks-as-implemented-2026-10-19).
[C] for these runs: a minimal release player saves 11 to 14 ns per instance
call, field and property access (96.0 to 83.6 ns for a call), 22 ns per push
of an object Lua holds and 12 to 13 ns per accessor call; a debug build is
within 3 ns of HEAD's minimal; legacy release players are within the noise
of HEAD (±5 ns; load average 40 to 100).

**Verification.** After merging main (`0a7c2ea5`, 494 tests per mode), macOS
arm64, Mono 6.14.1, host plugins built with `-DBUILD_SILICON=ON`:
- `run_managed.py --source-gen --source-gen-minimize --source-gen-fallback`
  passes in all four modes on Lua 5.5.1 and LuaJIT 2.1, each without a define
  (checks on) and with `--define XLUA_NO_BRIDGE_CHECKS` (release-like); on a
  `XLUA_PRESET=minimal` Lua 5.5.1 library with `--define XLUA_PRESET_MINIMAL`,
  also with `XLUA_NO_BRIDGE_CHECKS` (both include the preset's passes without
  reflection); and, reflection and emit only, with
  `XLUA_PRESET_MINIMAL`, `XLUA_NO_BRIDGE_CHECKS` and `XLUA_BRIDGE_CHECKS`
  together (the force define wins).
- `native_regression.py` passes on the three libraries (the Lua 5.5.1 one
  with `XLUA_RETRY_STOP_LATENCY=1`: one stop of 79 was 223 ms late at a load
  average of 40, in `core_stop_latency_env`, which this change does not
  touch); `check_exports.py`, `presets_check.py`, `hotfix_check.py` (with and
  without `XLUA_NO_BRIDGE_CHECKS`) and `coverage_cs_dotnet.py` pass.
- `editor_compile_check.py` on seven editors (2021.3.45f2 to 6000.6.6f1),
  with the new IL check: 588 ok, 7 skipped.
- Not run: Unity (Editor and players), Windows, Android, iOS, WebGL, Lua
  5.3.6 and 5.4.9 on the host, `sanitize.sh`, `coverage.sh` (the new
  branches of the two readers are covered by `check_value_helpers`).

## Found by coverage work

Date: 2026-10-17. Method: llvm-cov branch coverage of `build/xlua.c`
(`Test/Native/coverage.sh`, Lua 5.3.6, 5.4.9 and 5.5.1, Linux arm64 in Docker), a test for
each untaken branch, and the extended libFuzzer targets in `Test/Fuzz`
under ASan and UBSan. Branch coverage of `xlua.c` went from 59.6% to
100% on Lua 5.3.6, 5.4.9 and 5.5.1; the numbers are in
[Test/README.md](../../Test/README.md#static-analysis-coverage-and-fuzzing).
The new tests exposed two bugs, and two upstream behaviours that were then
changed (C3, C4).

### C1. `lua_setfenv` set upvalue 1 in place (fixed)

**Evidence.**
- On Lua 5.3+, `lua_setfenv` in `xlua.c` emulates Lua 5.1's
  `lua_setfenv` for `LuaEnv.LoadString` and `DoString` with an env
  (`LuaEnv.cs:233`, `:270`) and `LuaFunction.SetEnv`
  (`LuaFunction.cs:211-221`). It called `lua_setupvalue(L, idx, 1)` for any
  function or userdata. [C]
- Upvalue 1 of a closure is its first captured variable, which need not be
  `_ENV`: for `local x; f = function() return x, y end`, `SetEnv(f, env)`
  replaced `x` with the table and left `f`'s globals unchanged. [C]
- A closure shares its `_ENV` upvalue with the chunk that defined it and
  that chunk's other closures, so setting it in place changed the globals of
  all of them. In the native check a sibling closure saw the new table. [C]
- The API guide calls `SetEnv` the equivalent of `setfenv`
  (`Assets/XLua/Doc/XLua_API_EN.md:137-141`), which changes one function.
- It returned 1 for userdata and C functions without setting anything, and
  did not pop the table when `lua_setupvalue` failed. [C]

**Fix.** `lua_setfenv` (`xlua.c:148-187`) finds the upvalue named `_ENV`
(upvalue 1 when stripped bytecode has no names, since a main chunk's only
upvalue is `_ENV`), gives the function an upvalue of its own with
`lua_upvaluejoin` (the `_ENV` of a freshly loaded empty chunk, set to the
table), always pops the table, and returns 0 for C functions, functions
without `_ENV` and other values. Main chunks from `LoadString`/`DoString`
behave as before. Cost: one empty-chunk load per call; not benchmarked.

**Tests.** `check_setfenv` in `Test/Native/native_regression.py` (main
chunk, stripped main chunk, `_ENV` first and shared with a sibling, `_ENV`
second, no `_ENV`, a C closure, non-functions, and an allocation failure)
and `CaseLuaFunctionSetEnv` in
`Test/UnitTest/StreamingAssets/luaCallCs.lua`. Both fail
before the fix (the captured local became the table, and the sibling saw
the new globals). [C]

### C2. `xlua_touint` cast floats with undefined behaviour (fixed)

**Evidence.**
- `xlua_touint` converts a C# `uint` argument (`ObjectCasters.cs:43`,
  `:278`; generated wrappers) and a `uint32` struct field
  (`xlua.genaccessor` type 5, `xlua.c:1261`). [C]
- For a float on Lua 5.3+, and for every number on LuaJIT, it returned
  `(uint32_t)lua_tonumber(L, idx)`. C leaves that undefined for values
  below 0 or at least 2^32. [C]
- On Linux arm64, -1.5 gave 0 on Lua 5.5.1, and on LuaJIT -1 gave 0 and
  4294967296 gave 4294967295 (the conversion saturates); x86-64 truncates
  and wraps instead. [C] for arm64, [S] for x86-64 (instruction semantics,
  not run).
- The extended `fuzz_struct` and `fuzz_i64` targets stop on it under UBSan
  ("-1.5 is outside the range of representable values of type 'unsigned
  int'"). [C]

**Fix.** `number_to_uint32` (`xlua.c:107-112`): a float truncates toward
zero and wraps modulo 2^32 like the integer it truncates to, so -1.5 and -1
both give 4294967295; NaN and values outside the 64-bit range give 0.
Integers on Lua 5.3+ are unchanged. Since C4, C# arguments and `uint32`
struct fields raise for a float without an integer value instead;
`xlua_touint` keeps this rule for `uint[]` elements and older generated code.

**Tests.** `check_uint_conversion` and the type 5 cases of
`check_struct_accessors` in `native_regression.py`, and
`CaseUintArgumentFromFloat` in `luaCallCs.lua`. The native checks fail
before the fix on Lua 5.5.1 and LuaJIT, the managed case on Lua 5.5.1. [C]

### C3. Struct field setters stored 0 for non-numbers (changed)

Decided 2026-10-17 by the owner for every preset; no legacy flag.

**Evidence.**
- The `xlua.genaccessor` setters (`DIRECT_ACCESS`, `xlua.c:1266-1306`)
  converted the value with `lua_tointeger`, `xlua_touint`, `lua_toint64`,
  `lua_touint64` or `lua_tonumber` without a type check. `nil`, a boolean, a
  table, a function, a userdata and a non-numeric string all stored 0, on
  every field type and every Lua version. [C] (probe on the old plugins,
  Linux arm64 in Docker, Lua 5.3.6, 5.4.9, 5.5.1, LuaJIT with and without
  `UINT_ESPECIALLY`.)
- On LuaJIT, `lua_toint64` and `lua_touint64` (`i64lib.c:122-140`,
  `:171-189`) read a string as 0, so `"12"` stored 0 in an int64 or uint64
  field while the 32-bit fields stored 12. [C]
- There is no bool or decimal accessor: `gen_css_access` accepts the tags
  0 to 9 (int8 to double). GCOptimize structs with a bool field are not
  packed (`Generator.cs` `SizeOf` has no size for `bool`), so nothing native
  writes a bool field. [C]
- The `xlua_pack_*` helpers (`xlua.c:991-1200`) take C values that C# has
  already converted; they read no Lua value. A Lua table converted to a
  GCOptimize struct goes through `CopyByValue.UnPack` and
  `ObjectTranslator.Get` in C#. Neither shares the setters' conversion, so
  neither is changed here. [C]

**Change.** `check_field_number` (`xlua.c:1233-1237`) runs after the struct
check: a value for which `lua_isnumber` is false goes to
`luaL_checknumber`, which raises `bad argument #2 to '<name>' (number
expected, got <type>)`. On LuaJIT, `check_field_int64`
(`xlua.c:1243-1261`) also accepts an int64/uint64 userdata and replaces a
numeric string with its number, so `lua_toint64` reads it. A rejected
value leaves the field unchanged. Numbers convert as before, including the
`number_to_uint32` rule from C2; one consequence that stayed: an int8 to
int32 or 64-bit field given a float with a fraction stored 0 on Lua 5.3+
(`lua_tointeger` has no integer for it) and the truncated value on LuaJIT.
C4 changed that: such a value now raises.

**Cost.** One `lua_isnumber` call per set (on LuaJIT 64-bit fields, one
`lua_type` call); no allocation. `.text` of `libxlua.so` grew by 280 bytes
on Lua 5.3.6, 5.4.9 and 5.5.1 and by 384 bytes on LuaJIT (RelWithDebInfo,
Linux arm64). Speed not benchmarked.

**Tests.** The setter cases of `check_struct_accessors` in
`native_regression.py` (nil, booleans, a table, empty, non-numeric and
partly numeric strings, a function, full and light userdata, a thread, no
value; numeric strings, integers and floats accepted; an int64 userdata),
`CaseStructSetterRejectsNonNumber` in `luaCallCs.lua`, and `value_op` in
`fuzz_struct` (a setter succeeds only for numbers and numeric strings, and
a rejected value leaves the field unchanged). The native and managed cases
fail before the change on Lua 5.3.6, 5.4.9, 5.5.1 and LuaJIT; `fuzz_struct`
stops on the old code. [C]

### C4. Floats with a fraction went into integer slots (changed)

Decided 2026-10-17 by the owner for every preset (option A: Lua 5.3's own
rule), no legacy flag. Measured on macOS 27 arm64 (Apple clang, Mono
6.14.1) with a probe case run in all three binding modes, before the change.

**Evidence: what `Foo(2.75)` did.** Every path called the same lenient
functions, so the paths agreed with each other, but the result depended on
the runtime, and nothing raised. [C] (probe; identical output in the
emitted, generated and `GEN_CODE_MINIMIZE` runs and for the reflection
class.)

- Reflection: `ObjectCasters` `intCaster` and the other integer casters
  (`(T)LuaAPI.xlua_tointeger`, `xlua_touint`, `lua_toint64`, `lua_touint64`);
  overload checks `numberCheck` (`lua_type == LUA_TNUMBER`) and
  `int64Check`/`uint64Check`; field setters `Utils.genFieldSetter`
  through `ObjectTranslator.GetObject`.
- Emitted: `CodeEmit` `fixCaster`/`typedCaster` (the same functions plus a
  `Conv_*` opcode); checks through `ObjectTranslator.Assignable`, which ends
  in the reflection checkers.
- Generated: `TemplateCommon.lua.txt` `fixChecker`
  (`LuaTypes.LUA_TNUMBER == LuaAPI.lua_type`), `typedCaster` and `fixCaster`
  (`LuaAPI.xlua_tointeger` with a cast, `xlua_touint`, `lua_toint64`,
  `lua_touint64`).
- Native: `xlua_tointeger` is `(int)lua_tointeger`; on Lua 5.3+ that is 0
  for a float without an integer value, on LuaJIT a plain C cast of the
  double (undefined outside the range). `lua_toint64`/`lua_touint64` in
  `i64lib.c` are `lua_tointeger` on 5.3+ and saturate on LuaJIT, where they
  also read a string as 0. `xlua_touint` truncates and wraps (C2).

| Value | Runtime | int, short, ushort, byte, sbyte, char | uint | long | ulong | Struct field (`xlua.genaccessor`) |
|---|---|---|---|---|---|---|
| 2.75 | 5.4.9, 5.5.1 | 0 | 2 | 0 | 0 | 0 (uint32: 2) |
| 2.75 | LuaJIT | 2 | 2 | 2 | 2 | 2 |
| -2.75 | 5.4.9, 5.5.1 | 0 | 4294967294 | 0 | 0 | 0 (uint32: 4294967294) |
| -2.75 | LuaJIT | -2 (byte 254) | 4294967294 | -2 | -2 | -2 (uint8 254) |
| "2.75" | 5.4.9, 5.5.1 | 0 | 2 | 0 | 0 | 0 (uint32: 2) |
| "2.75" | LuaJIT | 2 | 2 | 0 | 0 | 2 |
| "12" | LuaJIT | 12 | 12 | **0** | **0** | 12 |
| NaN | all | 0 | 0 | 0 | 0 | 0 |
| inf, 2^63 | 5.4.9, 5.5.1 | 0 | 0 | 0 | 0 | 0 |
| inf, 2^63 | LuaJIT | byte 255, int -1 (saturating cast on arm64) | 0 | 2^63 - 1 | 2^63 - 1 | uint8 255, int32 -1, int64 2^63 - 1 |

Overloads: an integer check accepted any number, so `Pick(int)` declared
before `Pick(double)` took 2.75 (and passed 0 or 2), `PickLong(long)` took
2.75 and 2^63, and `PickByte(byte)` took 2.5. [C]

**Change.** One check, `value_to_integer` (`xlua.c:292`): on Lua 5.3+
`lua_tointegerx` plus `lua_isnumber`, on LuaJIT the same test made on the
double (an int64/uint64 userdata gives its bits). An integer, a float with an
integer value in the 64-bit range, or a numeric string that converts to one
is accepted; a fraction, NaN, an infinity or a value outside the 64-bit
range has no integer representation; other values read as 0, as before.

- Struct setters: `field_integer` (`xlua.c:1361`) raises
  `luaL_argerror(L, 2, "number has no integer representation")`, after the
  C3 type check.
- C#: `xlua_tointeger64` (`xlua.c:321`) and `LuaDLL.cs`
  `xlua_checkinteger` (`:263`), which throws a `LuaException` with the text
  `xlua_pushargerror` (`xlua.c:339`) builds as `luaL_argerror` would ("bad
  argument #1 to 'Foo' (number has no integer representation)", self not
  counted in a method call; '?' for a function called from C). C# cannot
  raise a Lua error itself, so the wrappers report it inside their usual
  `c# exception:` text.
- Reflection: `ObjectCasters.GetArgCaster` (`:277`) for parameters,
  `ObjectTranslator.GetArgObject` (`:946`) for field setters, indexers and
  `xlua.access`. Emitted: `CodeEmit` `integerArgConv` (`:102`). Generated:
  `TemplateCommon.lua.txt` `integerArgCaster`. All call
  `LuaAPI.xlua_checkinteger` and cast the long to the C# type.
- Overload checks: `xlua_isinteger_number` (`xlua.c:329`), a number with an
  integer representation, in `ObjectCasters` `integerCheck` (`:79`),
  `int64Check`/`uint64Check` and the generated checks. Numeric strings still
  do not match an integer overload (unchanged).
- Integral values outside the C# type's range keep wrapping like an
  unchecked cast (owner, 2026-10-17): `Byte(300)` is 44, `UInt(-1)` is
  4294967295, a ulong keeps the bits of a negative Lua integer.
- The first declared overload that matches still wins (owner, 2026-10-17):
  `PickDoubleFirst(3)` calls the double overload, so `Mathf.Max(3, 5)`
  stays float. *Replaced on 2026-10-19 by the best match
  ([C5](#c5-the-first-declared-overload-won-changed)).*
- Native bridge version 107 (`xlua_get_lib_version`), since the C# code
  needs the new exports.

**Not changed here** (changed on 2026-10-18, see
[C4, part 2](#c4-part-2-the-other-integer-slots-changed)). Values C# reads from Lua keep the lenient casters:
delegate and interface bridge results and out values
(`GetResultCasterStatement`, `CodeEmit` `luaResult`), `LuaTable.Get` and
`LuaFunction` results (`ObjectTranslator.Get<T>`). The generated bridges cast
after `PCall` without a `try`/`finally`, so a throwing cast would leave the
results on the Lua stack. Also unchanged: table contents converted to C#
structs and collections, array elements (`StaticLuaCallbacks` array setters,
still `xlua_touint` for `uint[]`), `params` arrays, nullable integers, enums
and the array index of generated `__index`/`__newindex`. These are in the
backlog.

**Out-of-range values (proposal, decided).** Wrapping stays in every preset:
upstream and C2 document it, `UInt(-1)` is a common idiom, and a ulong of 2^63
or more is a negative integer on Lua 5.3+, so 64-bit slots must keep the bit
pattern. A strict range check ("value out of range" for int8 to uint32,
never for long and ulong) is a planned flag for `minimal`; it exists since
2026-10-18 as the opt-in define `XLUA_STRICT_INT_RANGE` (part 2).

**Cost.** The C# hot path is still one P/Invoke per integer argument
(`xlua_tointeger64` instead of `xlua_tointeger`), plus a branch; an integer
overload check is one P/Invoke as before. Not benchmarked.

**Tests.** `check_integer_conversion` and `check_argerror` and the integer
cases of `check_struct_accessors` in `native_regression.py`;
`CaseIntegerArgumentRule`, `CaseIntegerFieldRule`, `CaseIntegerOverloadRule`,
`CaseStructSetterRejectsFraction` and the updated `CaseUintArgumentFromFloat`
in `luaCallCs.lua` (classes `IntegerSlots`, emitted or generated, and
`IntegerSlotsReflect`, always reflection); `fuzz_struct` expects integer
fields to take only integral floats, and `fuzz_i64` checks
`xlua_tointeger64` and `xlua_isinteger_number` against the rule on every
runtime. Before the change the managed cases fail in all three modes on Lua
5.4.9, 5.5.1 and LuaJIT (5 failures each), the native struct checks fail on
5.3.6, 5.4.9, 5.5.1, LuaJIT and LuaJIT with `UINT_ESPECIALLY`, and the new
exports do not exist; `fuzz_struct` aborts on the old code. [C]

After the change, on macOS 27 arm64: `native_regression.py` passes on all
five runtimes, `run_managed.py --gen-code --gen-code-minimize` passes 344
tests in each mode on Lua 5.4.9, 5.5.1 and LuaJIT, `xlua.c` keeps 100% line
and branch coverage on Lua 5.3.6, 5.4.9 and 5.5.1, and `clang --analyze`
reports nothing. `fuzz_struct` and `fuzz_i64` ran 120 s each under ASan and
UBSan without a finding (Lua 5.5.1; `fuzz_i64` also on LuaJIT with and
without `UINT_ESPECIALLY`), but with a random-mutation driver instead of
libFuzzer, which Apple clang lacks; clang-tidy was not run. The Linux
Docker image used for the earlier runs was not available. [C]

### C4, part 2: the other integer slots (changed)

2026-10-18. The owner's decision for C4 (option A, every preset) applied to
the slots C4 left unchanged, and the planned range flag as an opt-in define.
Measured on macOS 27 arm64 (Apple clang, Mono 6.14.1, .NET SDK for the
source generator), host plugins built from `build/` with
`-DBUILD_SILICON=ON` (RelWithDebInfo).

**Evidence: what the slots did before.** The new cases below, run against
the runtime of main at 82c570f9, with 2.5 (or another float without an
integer value) in each slot. [C] (the same output in the reflection,
emitted, generated, `GEN_CODE_MINIMIZE` and source-generated runs.)

- Lua 5.5.1: 0 in every slot (`lua_tointeger` has no integer for it),
  except `uint`, which truncated (C2: `uint` result of 1.5 gave 1).
- LuaJIT: the truncated value (2 for 2.5, 0 for -0.5), and 2^63 gave -1 in
  an `int` result (the saturating cast of `xlua_tointeger`).
- `int[]` from `{1, 2.5}`: `1,0`; `List<int>` from `{1, 2.5}`: `1`, the
  value silently left out (since C4, whose overload check rejects 2.5); a
  `Dictionary<string, int>` from `{a = 2.5}`: empty.
- Structs from tables, reflection (`IntegerSlotStruct`) and the generated
  `[GCOptimize]` unpacking (`StaticPusherStructB`): the field became 0.
- A 64-bit enum read a number through `xlua_tointeger`, a 32-bit int:
  `LuaTable.Get<string, LongEnum>` of 2^40 gave 0
  (`ObjectCasters.cs` enum caster; `ObjectTranslator.TranslateToEnumToTop`
  used `(int)lua_tonumber`).
- The array index (`StaticLuaCallbacks` `ArrayIndexer`/`ArrayNewIndexer`,
  generated `__CSIndexer`/`__NewIndexer`): `a[0.5]` was `a[0]`.

**Change.** One rule for every integer slot, through `xlua_tointeger64`
(`xlua.c` `value_to_integer`, unchanged; no native change).

- Typed readers in `LuaDLL.cs`: `xlua_check{sbyte,byte,int16,uint16,char,int32,uint32,uint64}`
  (and the existing `xlua_checkinteger` for `long`) for arguments, with
  `luaL_argerror`'s text; `xlua_get{...}` and `xlua_getinteger` for values,
  which throw `InvalidCastException("number has no integer
  representation")`. A value is not an argument of the running function, so
  an argument number there would name a stack slot.
- Reflection: the `ObjectCasters` integer casters (arrays, `List`,
  `Dictionary`, structs, nullable, enums), the `ObjectTranslator` primitive
  getters (`Get<T>`: `LuaTable`, `LuaFunction`, generic delegate bridges,
  hotfix results), `TryPrimitiveArraySet`, the array index (an `int`
  argument), `GetParams` (the elements are arguments), enums through their
  underlying type (all 64 bits). CodeEmit: `fixCaster` and
  `integerArgCaster`. Templates (`TemplateCommon.lua.txt`, enum and class
  wrappers) and the source generator (`Wrappers.cs`, `Bridges.cs`): the same
  maps.
- A `List` or `Dictionary` value with a fraction raises instead of being
  left out (`ObjectCasters.convertsElement`); values of other types are still
  left out, and keys (`Dictionary` keys, `LuaTable.ForEach`/`GetKeys`) still
  only select entries, so `[1.5]` is not an `int` key.
- **Lua stack.** The delegate and interface bridges read their results
  after `lua_pcall` and then restore the stack. They now read them inside
  `try`/`finally`: CodeEmit (`emitMethodImpl` and the interface property
  getter), `LuaDelegateBridge.tpl.txt`, `LuaInterfaceBridge.tpl.txt` and
  the source generator's `Bridges.cs`; a bridge without results has no `try`.
  `ObjectTranslator.popValues` (`LuaFunction.Call` with return types) got
  one too. `LuaTable` and `LuaFunction.Func` already had theirs (C8, C9).
  A conversion inside a C# function called from Lua needs none: the error
  unwinds that function's frame.
- Generated `Enum.__CastFrom` had no `catch`: the new exception escaped the
  `lua_CFunction` and ended the process (found by the first gencode run).
  It now catches it like the other wrappers.
- **`XLUA_STRICT_INT_RANGE`** (opt-in, in no preset): the 8- to 32-bit
  readers raise "value out of range" (`bad argument #n to 'f' (value out of
  range)` for an argument) for an integral value outside the type, in every
  slot; `long` and `ulong` never do. Without it, values wrap as decided for
  C4.

**Not changed.** Values of `object` slots (`LuaFunction.Call` without return
types, `ForEach<object, ...>`) keep their Lua type. Reflection overload
checks of enums still accept only enum userdata (`ObjectCheckers` enum
checker), as before. Generated wrappers for array types themselves (an
`int[]` in a `[LuaCallCSharp]` list) do not compile, independently of this
change: the constructor template writes `new int[]` with an argument list.
The index code they contain was checked by generating them in a scratch
copy; nothing in the suite lists an array type. *2026-10-18:* Generate Code
now leaves array types out with a warning, as the source generator does, and
the suite lists `int[]` (`Coverage.ArrayTypeConfig`).

**Cost.** No native change: the plugins and the bridge version (107) are
unchanged. Every typed reader is one P/Invoke (`xlua_tointeger64`) and a
branch, as the lenient readers were one P/Invoke; nothing allocates (managed
bytes per operation equal before and after in every case). `XLuaGenerate`
output for the suite grew by 5.5 KB of source (1,034,411 to 1,039,898
bytes; `GEN_CODE_MINIMIZE` 819,048 to 823,103): the `try`/`finally` of
the bridges with results and the typed reader calls; `XLua.Mini.dll` grew by 1.5 KB of IL
(260,608 to 262,144 bytes). IL2CPP code size and speed were not measured.

`Test/Native/benchmark.py`, Lua 5.5.1 and LuaJIT, modes reflection, emit,
gencode and gencode-min, `--runs 7`; three alternating rounds of main at
82c570f9 (with the new benchmark cases) and of this change, the minimum
over rounds of the per-run minimum, in ns per operation. Other agents ran
tests on the machine at the same time, so differences under about 5% are
noise. [C]

| Case | 5.5.1 before → after | LuaJIT before → after |
|---|---|---|
| `lua_cs.static_call` (two `int` arguments), gencode | 48.8 → 48.1 | 56.3 → 55.6 |
| `lua_cs.property_set`, `field_set`, gencode | 77.0 → 75.6, 78.8 → 77.2 | 75.8 → 76.6, 78.4 → 76.7 |
| `lua_cs.int_array_get`, emit / gencode-min | 103.3 → 102.6 / 114.2 → 117.1 | 96.1 → 98.4 / 103.3 → 111.7 |
| `lua_cs.int_array_set`, emit / gencode-min | 100.7 → 97.3 / 111.5 → 113.5 | 94.0 → 105.3 / 105.0 → 117.0 |
| `lua_cs.table_to_int_array` (8 elements), gencode | 842.7 → 868.9 | 838.2 → 851.3 |
| `lua_cs.params_int` (three `int`s), gencode / reflection | 240.2 → 141.8 / 999.6 → 933.1 | 239.7 → 145.8 / 987.7 → 930.0 |
| `cs_lua.delegate_int` (`int` result), emit / gencode | 73.3 → 78.6 / 63.3 → 73.6 | 69.3 → 79.7 / 62.5 → 72.0 |
| `cs_lua.luafunction_func` (`Func<int, int, int>`), gencode | 211.1 → 227.7 | 213.9 → 216.7 |
| `table.get_int_key`, gencode | 179.2 → 189.8 | 189.3 → 187.2 |

- Lua calling C# with integer arguments, fields and properties: unchanged.
- An integer read back from Lua costs about 5 to 10 ns more (7 to 16% of a
  delegate call with an `int` result on 5.5.1, up to 25% on LuaJIT): the
  checked reader instead of `xlua_tointeger`, plus the `try`/`finally`; a
  delegate with a struct result, which only got the `try`/`finally`, moved
  by 0 to 5%.
- Array elements and the index on LuaJIT: up to 12% slower (LuaJIT's
  `value_to_integer` also tests for an int64 userdata and the double's
  range); on Lua 5.5.1 within noise.
- **Accepted by the owner on 2026-10-18** for every preset, legacy
  included, without a flag: the slower integer reads above are the price of
  the rule ([backlog.md](../design/backlog.md#size-and-presets)). [C]
- `params int[]` got faster, by 40% in the emitted and generated wrappers:
  `GetParams<T>` now calls the typed reader directly instead of
  `ObjectTranslator.Get<T>`'s generic dispatch for every element.
- A first version in which each typed reader called `xlua_getinteger` (two
  managed calls per value) measured `int[]` writes and `int` delegate
  results 15 to 30% slower on a busier machine; the readers now call the
  P/Invoke themselves (`perf(runtime)` commit).

**Tests.** `CaseIntegerArrayElementRule`, `CaseIntegerTableContentsRule`,
`CaseIntegerParamsRule`, `CaseIntegerNullableRule`, `CaseIntegerEnumRule`,
`CaseIntegerResultRule` and `CaseIntegerRangeFollowsTheDefine` in
`luaCallCs.lua`, on `IntegerSlots` (emitted or generated) and
`IntegerSlotsReflect` (reflection). `CaseIntegerResultRule` runs
`IntegerSlotResults` (`LuaTestObj.cs`): delegate results of every integer
type, out values, `int[]`, enum and `int?` results, an interface method,
property and out values, `LuaFunction.Func` and `Call`, `LuaTable.Get`
(also into arrays, `List`, `Dictionary`, both struct kinds and enums),
`GetInPath`, `ForEach` and `Cast`, each with a check that the Lua stack top
is unchanged after the failed conversion. [C]

- Before the change six of the seven cases fail in the reflection, gencode,
  gencode-minimize and source-generator runs on Lua 5.5.1 and in the
  reflection run on LuaJIT; the range case fails only with the define,
  which did not exist.
- With CodeEmit's `try`/`finally` removed (a mutation of a scratch copy),
  `CaseIntegerResultRule` reports 16 reads that left 2 or 3 values on the
  stack.
- Under `XLUA_STRICT_INT_RANGE`, three upstream tests that expect wrapped
  values (`testGetBasicDataTypeNumberToSByte`, `ToByte`, `ToInt` in
  `TCForTestCSCallLua.cs`) failed on every runtime; they now expect the
  exception under the define.

After the change, merged with main at 10666945, on macOS 27 arm64 [C]:
`run_managed.py --gen-code --gen-code-minimize --gen-assembly --source-gen
--source-gen-fallback` passes 448 tests in each of its seven runs (reflection
and CodeEmit, gencode, gencode-minimize, both generated-assembly runs,
source generator, source-generator fallback), with the surface, bridge and
struct diffs equal, on Lua 5.5.1, 5.4.9, 5.3.6 and LuaJIT, each without and
with `--define XLUA_STRICT_INT_RANGE`; the generator's 30 unit tests pass
(`Tools/build_source_generator.py --test`); `hotfix_check.py` passes;
`editor_compile_check.py` compiles 385 configurations on 7 editors (7
skipped), including the new `player-android-il2cpp-xlua-strict-int-range`;
`coverage_cs_dotnet.py` reports 88.3% of runtime lines and 79.3% of runtime
branches (combined; 79.0% in the suite). Native code did not change, so
`native_regression.py` and `sanitize.sh` were not rerun. No Unity editor or
player ran; IL2CPP is not covered. The `run_managed.py` matrix (all four runtimes, with and without the
define) and `hotfix_check.py` passed again after merging main at 4eac4ff4,
with plugins rebuilt from that tree.

### C5. The first declared overload won (changed)

Decided 2026-10-19 by the owner for every preset, no legacy flag, replacing
"no overload reordering" (2026-10-17, C4): when several overloads match,
the best match wins, as C# overload resolution would choose for the Lua
value's natural C# type. Measured on macOS 27 arm64 (Apple clang, Mono
6.14.1, .NET SDK 10.0.401), host plugins built from `build/` with
`-DBUILD_SILICON=ON` (RelWithDebInfo).

**Evidence: what the wrappers did.** Each path tried the candidates in a
fixed order and called the first whose checks passed. [C]

- Reflection: `MethodWrap.Call` (`MethodWarpsCache.cs`), in `GetMember`
  order (declaration order); indexers in `Utils.genItemGetter` and
  `genItemSetter`, in property order.
- CodeEmit: `emitMethodWrap` (`CodeEmit.cs`), the same order, one
  `if`-chain per member.
- Source generator (plain and `GEN_CODE_MINIMIZE`): `Wrappers.cs`
  `Method`, `Constructor`, `Indexer`, `NewIndexer` and `Operator`, an
  `if`-chain in `MemberContext`'s order: `OverloadCosting` (`Members.cs`
  `Costing`), overloads with fewer non-primitive parameters first, then
  declaration order.

So the result depended on the declaration order and differed between the
paths. With the suite's new fixture (`OverloadPick` emitted or generated,
`OverloadPickReflect` through reflection), main at d48b78df on Lua 5.5.1
gave, in the reflection and CodeEmit run: `DoubleInt(3)` double,
`LongInt(3)` long, `IntLong(2^40)` int (wrapped), `FloatDouble(2.5)`
float, `ByteInt(7)` byte, `UIntLong(7)` uint, `DecimalDouble(2.5)`
decimal, `ObjectInt(3)` object, `NullableInt(3)` `int?`,
`Two(1, 2)` `double,double`, `BaseDerived(derived)` base,
`ObjectString("x")` object, and the constructor, instance method,
indexer getter and setter and `+` operator with `(double)` declared before
`(int)` all took the double one for 3. The generated code differed where
`OverloadCosting` reordered: it already called `ObjectInt(int)` and
`NullableInt(int)` first, since `object` and `int?` are not primitive. [C]

**Change.** One cost per argument and candidate parameter, lowest total
wins (`ObjectCasters.cs` `OverloadScore`, documented at the class):

| Lua value | Cost by parameter type (lower is better) |
|---|---|
| integer (Lua 5.3+ subtype; LuaJIT: a number with an integer value), inside the int range | `int` 0, `long` 2, other integer types 4, `double` 6, `float` 8, `decimal` 10 |
| integer outside the int range | `long` 0, `ulong` 2, `int` and other integer types 4 (they wrap), then as above |
| float with a fraction, NaN, infinity | `double` 0, `float` 2, `decimal` 4 |
| float with an integer value (`3.0`, Lua 5.3+ only) | `double` 0, `float` 2, `decimal` 4, integer types 6 to 10 |
| string | `string` 0, `byte[]` 2 |
| C# object | its own type 0, base class 2 per level (at most 60), interface 62 |
| any of the above | `object` 64; `T?` costs `T`'s cost plus 1 |
| nil, table, function, thread | 0 for every parameter (the first match wins, as before) |

- A candidate's cost is the sum over its arguments, every `params` element
  included, times 4, plus 2 for a `params` array and 1 when a default value
  is used, so these break ties only. Equal costs keep the wrapper's order;
  a candidate of cost 0 is called at once.
- The cost of a number needs its subtype and range, one new native call
  `xlua_number_kind` (`xlua.c`): 0 not a number, 1 a float without an
  integer value, 2/3 a float with one outside/inside the int range, 4/5 an
  integer outside/inside it. Bridge version 110.
- Only groups that need it are scored: `OverloadScore.Needed` (and the
  generator's `NeedsScore`) compares the Lua types each pair of candidates
  can both accept, position by position, from the checkers' rules
  (`ObjectCheckers`); two unrelated classes never share a userdata. If no
  pair can match the same arguments with different costs, the first match
  is the best one, and the wrapper keeps its plain checks: `Foo(int)` next
  to `Foo(string)` costs nothing more. Pairs with a `params` array are
  always scored.
- Reflection: `OverloadMethodWrap.Score` after `Check`, `MethodWrap.Call`;
  indexers: `Utils.IndexerKeys` (chosen by the key). CodeEmit: a first pass
  of checks and `BindingApi.ArgScore` calls that stores the best index, then
  the calls (`emitOverloadCheck`, `emitOverloadScore`, `emitOverloadCall`).
  Source generator: `Dispatch` writes the same two passes (`gen_pick`,
  `gen_score`) for methods, constructors, indexers and operators, in both
  modes. Generated code calls the public `BindingApi.ArgScore`, `ArgScores`
  and `NumberScore` (additions: `BindingApi.Version` stays 1).
- A numeric argument's `xlua_number_kind` is read once per call and shared
  by the candidates (`NumberScore` with a `gen_n<position>` variable in
  generated code, a local in CodeEmit, an array per `MethodWrap` in
  reflection); the translator is looked up only when a C# object's type is
  needed.
- The self of an extension method in the extended type's wrapper is not
  scored, as instance methods do not score theirs; a setter is chosen by its
  key, as before.

**Decided here (for the owner to confirm).** [C] for what the code does.

1. `3.0` on Lua 5.3+ is a float and prefers `double`; on LuaJIT it is the
   integer 3 (no subtype), as the integer check already treated it. So
   `Foo(6 / 2)` calls `Foo(double)` on Lua 5.4 and `Foo(int)` on LuaJIT.
2. Where C# betterness prefers narrower types, this rule prefers the type
   that holds every value: after `int`, `long` before `short` or `byte`,
   and `double` before `float` for an integer (C# picks `float` over
   `double` for an int literal). Integer types other than `int`, `long`
   and `ulong` share one cost, so `Foo(byte)` and `Foo(short)` keep the old
   order (and 300 can go to `byte` and wrap).
3. Costs add up over the arguments. Where C# reports an ambiguity, one
   overload is still called: `Mixed(1, "x")` with `Mixed(double, string)`
   and `Mixed(int, object)` calls the first (6 against 64).
4. `params` and defaults are tie-breakers, as in C#: `Optional(1)` with
   `Optional(int)` and `Optional(int, int b = 0)` calls the first, and
   `OptionalDouble(1)` with `(double)` and `(int, int b = 0)` calls the
   second.

**Cost.**

`Test/Native/benchmark.py`, Lua 5.5.1 and LuaJIT, `--runs 7`, three
alternating rounds of main at d48b78df (with the new benchmark cases) and of
this change; the minimum over rounds of the per-run minimum, in ns per call.
Other agents ran tests on the machine at the same time, so differences
under about 5% are noise (and up to 20% in the emit column). [C]

| Case | reflection | CodeEmit | source generator | `GEN_CODE_MINIMIZE` |
|---|---|---|---|---|
| `overload_disjoint`: `Disjoint(int)`/`(string)`, never scored, 5.5.1 | 400 → 407 | 155 → 156 | 56.1 → 55.6 | 62.0 → 62.9 |
| `overload_num_integer`: `Num(double)` before `Num(int)`, `Num(i)`, 5.5.1 | 408 → 470 | 151 → 296 | 54.6 → 84.3 | 57.8 → 89.7 |
| the same on LuaJIT | 396 → 484 | 149 → 271 | 54.1 → 95.2 | 57.0 → 97.9 |
| `overload_num_float`: `Num(i + 0.5)`, 5.5.1 | 407 → 448 | 154 → 176 | 54.3 → 71.4 | 57.6 → 77.2 |
| `overload_obj_class`: `Obj(object)` before `Obj(BenchTarget)`, 5.5.1 | 444 → 566 | 119 → 263 | 153 → 262 | 161 → 254 |

- Groups that cannot match one value with different costs keep their code
  and their speed. The other calls pay for the second candidate's check and
  the costs: about 15 to 40 ns per call in generated code for numbers (the
  old code called the first match, here the wrong one, after one check), 60
  to 140 ns in CodeEmit, whose checks go through `ObjectTranslator.Assignable`,
  and about 100 ns for a C# object, whose cost looks up its type
  (`UserdataType`) once more. Nothing allocates (managed bytes per call equal).
- A first version read the number kind for every candidate, with
  `lua_type` before it: 94 ns instead of 84 for `overload_num_integer` in
  generated code (`perf(runtime)` and `perf(generator)` commits).
- Size: `XLua.Mini.dll` (Mono, mcs, plain) 300,032 → 308,736 bytes. The
  suite's generated wrappers, compiled with csc into the test assembly
  (`XLuaUnitTest.exe`, the same fixtures on both sides): 872,960 → 889,856
  bytes (+1.9%) plain, 798,208 → 815,104 (+2.1%) with `GEN_CODE_MINIMIZE`; 45 of the suite's
  wrapper functions are scored, 34 of them in the new fixture `OverloadPick`.
  `libxlua.dylib` (5.5.1, RelWithDebInfo) 488,032 → 488,096 bytes. IL2CPP
  code size and speed were not measured.

**Tests.** `CaseOverloadNumbers`, `CaseOverloadReferenceTypes`,
`CaseOverloadParamsAndDefaults` and `CaseOverloadMembers` in
`luaCallCs.lua` (fixtures `OverloadPick`, `OverloadPickReflect`,
`OverloadBase`, `OverloadDerived`, `IOverloadThing` in `LuaTestObj.cs`):
every pair in both declaration orders, `nil` and tables keeping the first
match, constructors, an instance method, indexer get and set and `+`;
`CaseIntegerOverloadRule` now expects `PickDoubleFirst(3)` to call the int
overload and `3.0` the floating one on Lua 5.3+. `check_integer_conversion`
in `native_regression.py` checks `xlua_number_kind` on every runtime. The
generator unit test `OverlappingOverloadsAreScored` checks which groups are
scored. Before the change the four new cases and `CaseIntegerOverloadRule`
fail in the reflection and CodeEmit run on Lua 5.5.1 (5 of 470); the
generated runs did not run then, since the first run stops the script. [C]

After the change, merged with main at 9c166e9d, on macOS 27 arm64 [C]:
`run_managed.py --source-gen --source-gen-minimize --source-gen-fallback`
passes 482 tests in each of its four runs (reflection and CodeEmit, source
generator, `GEN_CODE_MINIMIZE`, fallback) with the surface, bridge and
struct diffs equal, on Lua 5.5.1, 5.4.9, 5.3.6 and LuaJIT, with plugins
rebuilt from that tree; `native_regression.py` and `check_exports.py` pass
on the four plugins; `build_source_generator.py --test` passes 43 tests;
`hotfix_check.py`, `luacats_check.py` (Lua 5.5.1 and LuaJIT, without
lua-language-server) and `editor_compile_check.py` (7 editors, 85
configurations: 588 ok, 7 skipped) pass; `coverage_cs_dotnet.py` reports
80.3% of runtime branches (combined) and 88.2% of runtime lines in the
suite. `sanitize.sh` (Linux) did not run; the native change is one function
over `value_to_integer`, which the fuzzers already cover. No Unity editor or
player ran; IL2CPP is not covered.

### C# runtime, from the coverage suite (2026-10-18)

Method: Mono line coverage (`coverage_cs.py`) and .NET branch coverage
(`coverage_cs_dotnet.py`) of `Assets/XLua/Src`, a test for each untested
area in `Test/UnitTest/StreamingAssets/luaCallCsCoverage.lua`, run in the
reflection, gencode and gencode-minimize modes on Lua 5.5.1 (Mono and .NET)
and on 5.3.6, 5.4.9 and LuaJIT (Mono), macOS 27 arm64. Line numbers are
those of main at 72715b4. Numbers: [Test/README.md](../../Test/README.md#numbers).
Six bugs, each fixed with a case that fails before the fix:

- **C5. Five-parameter delegates outside Unity.** `getCreatorUsingGeneric`
  checked `parameters.Length > 4` only under `#if !XLUA_GENERAL`
  (`ObjectTranslator.cs:393`), then indexed `genericAction[5]`
  (`:411`): `IndexOutOfRangeException` instead of "This type must add to
  CSharpCallLua". [C] (`CaseNoBridgeFiveParams`)
- **C6. `|` and `&` on `ulong` enums.** `EnumAnd`/`EnumOr` used
  `Convert.ToInt64` (`StaticLuaCallbacks.cs:57`, `:79`), which throws
  `OverflowException` above `long.MaxValue`. [C] (`CaseEnumBitwiseULong`,
  Lua 5.3+)
- **C7. `[BlackList]` in emitted wrappers.** `addToBlackList` resolved the
  type with `Type.GetType(info[0], true)` (`CodeEmit.cs:1245`), which only
  searches xLua's assembly and mscorlib, inside `catch { }`; entries for
  other assemblies were dropped, so emitted wrappers kept members that
  generated code leaves out. [C] (`CaseBlackList`, reflection mode)
- **C8. Lua stack after a nil read into a value type.**
  `LuaTable.Get<TKey, TValue>` and `GetInPath<T>` threw before restoring the
  stack (`LuaTable.cs:56`, `:150`): two slots left on the main thread per
  call, pushed without `lua_checkstack`. [C] (`CaseNilToValueTypeKeepsStack`,
  12 slots after three rounds)
- **C9. Lua stack after a failed `Cast`.** `LuaTable.Cast<T>`
  (`LuaTable.cs:373`) and `LuaFunction.Cast<T>` (`LuaFunction.cs:203`)
  popped the value only when the conversion returned. [C]
  (`CaseFailedCastKeepsStack`, 6 slots)
- **C10. `xlua.get_generic_method` index on LuaJIT.** The index was read
  only when `lua_isinteger` was true (`StaticLuaCallbacks.cs:1292`); the
  LuaJIT build's `lua_isinteger` always returns 0 (`build/xlua.c:128`), so
  the first method was always used. [C] (`CaseGetGenericMethodIndex`, LuaJIT)

Differences between binding modes found by the same suite:

- **Struct instance events** (not changed, owner question in the
  [backlog](../design/backlog.md#tests-and-ci)): a struct's instance event
  is added to a copy by emitted and generated wrappers but to the box by
  the reflection wrapper. [C]
- **`[DoNotGen]` method and an extension method of the same name** (fixed
  later the same day): generated code registered the lazy member
  (`Utils.LazyReflectionCall`) without the extension methods, because
  `XLuaGenAutoRegister` lists extension methods only for types without
  generated wrappers and `getClassInfo` drops the `[DoNotGen]` names from
  the wrapper. `o:Method("x")` returned `"own:0"`, Method(int) with a
  converted argument. [C] (`CaseDoNotGenExtension`, both generated-code
  suites)
- **`[BlackList]` `Func<MemberInfo, bool>` filters** (fixed): applied by
  the generator, ignored by `CodeEmit.initBlackList`, which read only
  `List<List<string>>` entries. The reflection wrappers
  (`Utils.makeReflectionWrap`, `MethodWrapsCache.GetConstructorWrap`)
  applied no `[BlackList]` at all, so in the generated-code suites a type
  without `[LuaCallCSharp]` kept every filtered member. [C]
  (`CaseBlackListFilterGenerated` fails in the reflection suite,
  `CaseBlackListFilterReflected` in all three)
- **`xlua.private_accessible` and extension methods** (fixed): the
  non-public pass of `makeReflectionWrap` added every extension method
  again and replaced the wrapper of a name shared with a public method,
  so `o:Method(1)` called the extension method with `"1"`. [C]
  (`CasePrivateAccessibleExtension`, all three suites)

The fixes and what is left are in the backlog rows of 2026-10-18.

### Not changed

- **BaseType cycle.** The indexer closures walk `BaseType` until `nil`
  (`xlua.c` `obj_indexer`, `obj_newindexer`, `cls_indexer`,
  `cls_newindexer`). Real `System.Type` chains end, but a script with the
  `debug` library can put a table whose `BaseType` is itself into a
  closure's upvalue (`debug.setupvalue`), and the lookup then loops in C,
  where the instruction count hook cannot stop it. [C]: an `obj_indexer`
  closure built with `A.BaseType = A` was still running after 15 seconds
  with a count hook set (Lua 5.5.1). The `debug` library already gives full
  control (items 7 and 8), so this is listed only; a fix would be a step
  limit on the walk.
- **Header of any full userdata.** The accessors and pack helpers still
  read the 8-byte struct header of any full userdata (item 5). The fuzz
  target only passes userdata of at least 4 bytes, the smallest that
  xLua or the standard libraries create.

## Upstream reports

Searched Tencent/xLua issues with `gh search issues` for "getmetatable",
"__gc", "__metatable", "metatable", "metatable_operation",
"setmetatable", "debug.getregistry", "registry", "forged userdata",
"cast", "structclone", "genaccessor", "sandbox", "os.execute", "io.popen",
"Process", "release" and the Chinese terms for security, sandbox and crash.
None reports the early `__gc` release or the struct header confusion. [C]

Related:
- https://github.com/Tencent/xLua/issues/77: patching an exported class
  through `getmetatable` and `rawset` (compatibility impact of 1).
- https://github.com/Tencent/xLua/issues/1169: extending class metatables
  through `debug.getregistry` (7, and why the lock is opt-in in legacy).
- https://github.com/Tencent/xLua/issues/981: `util.state` built on
  `getmetatable`, changed here to `debug.getmetatable`.
- https://github.com/Tencent/xLua/issues/1035 and
  https://github.com/Tencent/xLua/issues/809: which libraries to disable for
  mods, and hiding Unity APIs (12).
- https://github.com/Tencent/xLua/issues/1019: no `lua_getmetatable`
  P/Invoke. This change does not need one.
