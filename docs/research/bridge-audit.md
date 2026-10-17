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
| 2 | Metamethods accept any first argument (`FastGetCSObj`) | B | proposal | opt-in | on | n/a |
| 3 | Struct header (`fake_id`, `len`) trusted inside any full userdata | A | **done**: LuaJIT `Integer64` zeroed on push | on | on | n/a |
| 4 | `xlua.cast` sets a type metatable on any value | A/B | **done**: only C# objects, only to types they are instances of | on (owner decided 2026-10-17: no flag) | on | n/a |
| 5 | `xlua.genaccessor` accessors take any struct-shaped userdata | A | proposal (covered by 3; stronger check optional) | on / opt-in | on | n/a |
| 6 | `xlua.metatable_operation` (`xlua.getmetatable`, `xlua.setmetatable`) | B | **set done**: keeps the tag and `__gc`; get is a proposal | set fixed, get unchanged | get restricted | n/a |
| 7 | Registry and cache tables (`debug.getregistry`) | B (A with 3) | proposal | unchanged | reduced `debug` | no `debug` |
| 8 | C closure upvalues (`debug.setupvalue`) | A | **done**: NULL check in `csharp_function_wrap` | on | on | no `debug` |
| 9 | `xlua` table and class tables are plain writable tables | B | proposal | opt-in | on | n/a |
| 10 | `xlua.private_accessible`, `xlua.access`, `xlua.tofunction`, `xlua.get_generic_method` | C | proposal | unchanged | hotfix only | n/a |
| 11 | `xlua.hotfix` | C | proposal | unchanged | with `HOTFIX_ENABLE` | n/a |
| 12 | `CS.*`: any public type, reflection, `xlua.load_assembly` | C | proposal | unchanged | type allowlist | n/a |
| 13 | Lua standard library and binary chunks | C | planned in presets | unchanged | trimmed | trimmed |
| 14 | Lua stack growth in C# callbacks | A | **done**: APICHECK run clean (init handler, push slack, room for type registration) | on | on | n/a |

Classes: **A** crash or memory safety, **B** bridge-state corruption,
**C** capability by design.

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

## 11. `xlua.hotfix` (capability)

**Evidence.**
- `xlua.hotfix` (`LuaEnv.cs:564`) replaces the body of any injected method.
- Injection is limited to `[Hotfix]` types at build time. [C]

**Hardening.**
- Keep it, behind `HOTFIX_ENABLE`.
- Protect it with 9, so another script cannot replace it.
- Sign patches with `SignatureLoader`.

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
- Lua 5.3, 5.4 and LuaJIT were not run with API checks. The APICHECK run is
  manual; no script or CI job runs it.

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
