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
| 3 | Struct header (`fake_id`, `len`) trusted inside any full userdata | A | proposal (one-line fix) | on | on | n/a |
| 4 | `xlua.cast` sets a type metatable on any value | A/B | proposal | opt-in | on | n/a |
| 5 | `xlua.genaccessor` accessors take any struct-shaped userdata | A | proposal (covered by 3; stronger check optional) | on / opt-in | on | n/a |
| 6 | `xlua.metatable_operation` (`xlua.getmetatable`, `xlua.setmetatable`) | B | proposal | unchanged | restricted | n/a |
| 7 | Registry and cache tables (`debug.getregistry`) | B (A with 3) | proposal | unchanged | reduced `debug` | no `debug` |
| 8 | C closure upvalues (`debug.setupvalue`) | A | proposal (NULL check) | on | on | no `debug` |
| 9 | `xlua` table and class tables are plain writable tables | B | proposal | opt-in | on | n/a |
| 10 | `xlua.private_accessible`, `xlua.access`, `xlua.tofunction`, `xlua.get_generic_method` | C | proposal | unchanged | hotfix only | n/a |
| 11 | `xlua.hotfix` | C | proposal | unchanged | with `HOTFIX_ENABLE` | n/a |
| 12 | `CS.*`: any public type, reflection, `xlua.load_assembly` | C | proposal | unchanged | type allowlist | n/a |
| 13 | Lua standard library and binary chunks | C | planned in presets | unchanged | trimmed | trimmed |
| 14 | Lua stack growth in C# callbacks | A | [U], needs an audit | | | |

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

## 3. Struct header trusted inside any full userdata

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

**Hardening.** Zero the `Integer64` block before filling it, or set its
`len` bytes explicitly. Then `len` is 0 or 1 and every access stays inside
the block.

**Cost.** One `memset` of 16 bytes per int64 push.

**Compatibility.** None.

**Default.** A bug fix, so on in every preset, without a flag. The owner
decides whether it needs one.

## 4. `xlua.cast` sets a type metatable on any value

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

**Hardening.** Require argument 1 to be a C# object
(`xlua_tocsobj_safe != -1`), and require
`type.IsInstanceOfType(obj)` or an interface the object implements.

**Cost.** One check per cast; casts are rare.

**Compatibility.** Casts of non-objects stop working. Upstream documents only
object casts.

**Default.** Opt-in in legacy, on in minimal.

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

## 6. `xlua.metatable_operation`

**Evidence.**
- `StaticLuaCallbacks.XLuaMetatableOperation`
  (`StaticLuaCallbacks.cs:1062-1104`) returns the raw type metatable on get.
- On set it stores any table as the new type metatable. It writes `[1]`, and
  now `__metatable` when the flag is on. It does not copy the tag or `__gc`.
  [C]

**Consequence.**
- Get bypasses the lock in 1, including `__gc`. [C]
- After a set, objects pushed later have no tag, so the safe checks no longer
  recognise them, and no `__gc`, so their pool slots are never freed. [U]

**Hardening.**
- On set, copy the tag key and `__gc` from the old metatable when the new
  one lacks them.
- In minimal, drop get (or return a copy without `__gc`), and keep set for
  `ReImplementInLua`-style overrides.

**Cost.** None at run time.

**Compatibility.** Code that reads the type metatable to patch it
(issue 1169) has to use set.

**Default.** Legacy unchanged; restricted in minimal.

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

## 8. C closure upvalues

**Evidence.**
- `debug.setupvalue` also writes the upvalues of C closures
  (`aux_upvalue`, `build/lua-5.5.1/src/lapi.c:1373-1383`). [C]
- `csharp_function_wrap` (`xlua.c:795-811`) calls
  `lua_tocfunction(L, lua_upvalueindex(1))` and calls the result without a
  NULL check. [C]
- The other trampolines index managed lists, which throw a C# exception
  instead (`FixCSFunction`, `StaticLuaCallbacks.cs:104`;
  `CallCSharpWrapper`, `ObjectTranslator.cs:1451`). [C]

**Consequence.**
- A script with `debug` can replace that upvalue with a non-C value. The
  wrapper then calls a NULL function pointer. [U]
- The other C closures (`obj_indexer`, `cls_indexer`) keep their tables in
  upvalues, so a script can redirect lookups for a whole type. That is
  corruption, not unsafety, because the C# getters check types. [U]

**Hardening.** In `csharp_function_wrap`, raise a Lua error when
`lua_tocfunction` returns NULL. The reduced `debug` table from 7 closes the
rest.

**Cost.** One branch per wrapped call.

**Compatibility.** None.

**Default.** NULL check in every preset; reduced `debug` as in 7.

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

## 14. Lua stack growth in C# callbacks (needs an audit)

- Lua guarantees `LUA_MINSTACK` (20) free slots when a C function starts.
- The bridge calls `lua_checkstack` in 14 places in `Assets/XLua/Src`, for
  example `ReflectionWrap` and `MakePrivateAccessible`.
- Reflection wrappers that push one value per `out`/`ref` parameter, and
  `params` expansion, were not checked against that limit. [U]
- Recursion depth across Lua → C# → Lua is bounded by Lua's C-call limit
  (`LUAI_MAXCCALLS` = 200, `build/lua-5.5.1/src/ldo.h:62`), which raises a
  Lua error rather than overflowing the native stack. [C]
- Next step: run the suite with `LUA_USE_APICHECK`; ROADMAP.md lists an
  existing assertion that has to be fixed first.

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
