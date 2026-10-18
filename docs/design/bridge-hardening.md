# Bridge hardening: defines and open decisions

Status: **partly implemented, 2026-10-18.** The cheap proposals of the
[bridge audit](../research/bridge-audit.md) are opt-in C# defines, off by
default so that `legacy` behaves as upstream. The rest needs a decision from
the owner and is described below with its cost and the alternatives.
Evidence, `file:line` references and measurements are in the audit.

## Implemented as opt-in defines

| Audit item | Define | Effect | legacy | minimal | core |
|---|---|---|---|---|---|
| 1 | `XLUA_LOCK_METATABLES` (earlier) | `__metatable` on bridge metatables | opt-in | on | own tables |
| 2 | `XLUA_CHECK_OBJECTS` | C# objects are read from Lua only from a userdata with the bridge tag | opt-in | on | n/a |
| 5 | `XLUA_CHECK_STRUCT_ACCESSORS` | `xlua.genaccessor` accessors require the bridge tag | opt-in | on | n/a |
| 6 | `XLUA_RESTRICT_GETMETATABLE` | `xlua.getmetatable` returns a copy without `__gc` and the tag | opt-in | on | n/a |
| 7 | `XLUA_CHECK_OBJECT_CACHE` | a userdata cache entry counts only if it is that object's userdata | opt-in | on | n/a |
| 9 | `XLUA_LOCK_XLUA_TABLE` | `xlua` is a read-only proxy; xLua's helpers use functions captured at start-up | opt-in | on | n/a |
| 10, 11 | `XLUA_RESTRICT_PRIVATE_ACCESS` | `xlua.access`, `xlua.private_accessible` and `xlua.hotfix` only with `HOTFIX_ENABLE`, `xlua.access` only for `__Hotfix` fields; `xlua.tofunction` and `xlua.get_generic_method` public only | opt-in | on | n/a |
| 12 | `XLUA_NO_LOAD_ASSEMBLY` | no `xlua.load_assembly` | opt-in | on | n/a |

Since 2026-10-18 `XLUA_PRESET_MINIMAL` turns all of them on at once
([presets.md](presets.md#the-minimal-column-as-implemented)).

Two of them need native support, so the plugin's bridge version is 108:
`XLUA_CHECK_OBJECT_CACHE` calls `xlua_tryget_cachedud_checked`, and
`XLUA_CHECK_STRUCT_ACCESSORS` calls `xlua_check_struct_accessors` once per
`LuaEnv`. The same plugin serves every combination of defines; there is no
CMake option.

**Measured cost** (generated code, Lua 5.5.1, macOS 27 arm64, Mono 6.14.1;
method and table in [bridge-audit.md](../research/bridge-audit.md#hardening-defines-2026-10-18)):

| Define | Cost |
|---|---|
| `XLUA_CHECK_OBJECTS` | +10 to 13 ns per instance call, field or property access (+11% to +16% of 80 to 100 ns); static calls unchanged |
| `XLUA_CHECK_STRUCT_ACCESSORS` | +11 ns per `xlua.genaccessor` get, +13 ns per set (13.7 to 24.5, 16.1 to 29.5 ns) |
| `XLUA_CHECK_OBJECT_CACHE` | +14 ns (+8%) per push of an object Lua already holds |
| `XLUA_LOCK_XLUA_TABLE` | +6 ns per `xlua.f` lookup |
| `XLUA_RESTRICT_GETMETATABLE` | one table copy per `xlua.getmetatable` |
| `XLUA_RESTRICT_PRIVATE_ACCESS`, `XLUA_NO_LOAD_ASSEMBLY` | none |
| without any define | none measurable; native `__text` +48 bytes (Lua 5.5.1, 5.4.9), −64 bytes (LuaJIT) |

## Decisions for the owner

### A. Make the three checks unconditional?

`XLUA_CHECK_OBJECTS`, `XLUA_CHECK_STRUCT_ACCESSORS` and
`XLUA_CHECK_OBJECT_CACHE` change behaviour only for values a correct script
never passes: a foreign userdata as `self`, a userdata that merely looks like
a struct, a value planted in the cache. They are opt-in because the audit's
rule makes every behaviour change opt-in in `legacy`. If their measured cost
is acceptable, turning them on in every preset removes three defines and
three test runs from the matrix. Keeping them opt-in costs nothing but the
matrix.

### B. Reduced `debug` library (audit item 7, not implemented)

The audit proposes `debug = {traceback, getinfo}` in minimal release builds.
A C# define is the wrong switch for it, because xLua's own Lua code needs
more of `debug`:

- `init_xlua`'s `setfenv`/`getfenv` emulation for Lua 5.2+ calls
  `debug.getinfo`, `debug.getupvalue` and `debug.upvaluejoin` when it runs
  (`Assets/XLua/Src/LuaEnv.cs`, `init_xlua`); capturing them at start-up
  fixes that.
- `xlua.util`: `state()` uses `debug.getmetatable`/`setmetatable`,
  `print_func_ref_by_csharp` uses `debug.getregistry`, and the module reads
  `debug.getmetatable(...).__bor` when it loads
  (`Assets/XLua/Src/EmbeddedModules.cs`, `enum_or_op`).
- The code generation templates read `debug.getmetatable(...).__bor` and
  `__band` when they load (`LuaEnumWrap.tpl.txt:15`,
  `LuaEnumWrapGCM.tpl.txt:15`, `TemplateCommon.lua.txt:547`). They run in
  the Editor's `LuaEnv`, which a define would reduce as well.
- Debuggers and profilers (EmmyLua, `perf.profiler`) need the full library.

Options:

1. **A `LuaOptions.Libraries` flag** (planned in [presets.md](presets.md),
   "Limits in every preset"): each `LuaEnv` chooses; the Editor, tools and
   development builds keep the full library. xLua's helpers capture what they
   need before the table is reduced. Recommended; it belongs to the
   `LuaOptions` work.
2. **A define active only in release players**
   (`!UNITY_EDITOR && !DEVELOPMENT_BUILD`), plus `xlua.util` changed to take
   `__bor` from `xlua.getmetatable`. Cost: the XLUA_GENERAL test suite would
   need its own switch to test it.

Run-time cost of either: one table built per `LuaEnv`.

### C. Read-only class tables (audit item 9, not implemented)

Finding: **a Lua table cannot refuse `rawset`.** A read-only proxy, as the
audit proposes, stops `t.k = v`, but `rawset(proxy, k, v)` stores a field
that hides the real one. `XLUA_LOCK_XLUA_TABLE` has this limit (documented,
and covered by `CaseLockXLuaTable`); it protects only against scripts that
have no `rawset` and no `debug`, such as a sandbox environment.

Class tables hold static methods as raw fields
(`Utils.RegisterFunc(L, Utils.CLS_IDX, ...)`), so `CS.T.Method = f` replaces
a method for every script without any metamethod running. Options:

1. **Empty front tables.** Register an empty table under `CS` and keep the
   fields in the table that `cls_indexer` already looks up (its upvalue 2,
   `build/xlua.c`). Touches `Utils.BeginClassRegister`, `Utils.ReflectionWrap`,
   `Utils.MakePrivateAccessible`, `LazyReflectionCall` and the enum cache
   below. Cost: every static member access goes through `__index` (a C
   closure call and a getter lookup) instead of a raw hit; `pairs(CS.T)` is
   empty; `rawset` still shadows.
2. **Userdata proxies.** Refuse `rawset`, but `type(CS.T)` becomes
   `"userdata"`: `StaticLuaCallbacks.getType`, the casters for `Type`
   arguments, `Utils.SetCSTable` (nested types are stored in their parent's
   table), `LoadCSTable` callers, `xlua.setclass`, LuaCATS and scripts that
   test `type(x) == "table"` all change. Large.
3. **Leave them writable** and document: scripts of different trust need
   separate `LuaEnv`s.

Recommendation: 3 for `legacy`. For `minimal`, decide together with E: while
scripts reach reflection, protecting tables changes little.

### D. Enum values cached in class tables (fixed 2026-10-18)

Fixed with the source generator's milestone M2: `BindingApi.EnumCacheRef`
now refers to a registry table of its own, so the generated pushers keep
their semantics and scripts cannot reach the cache
(`luaBridgeHardening.lua` `CaseEnumCacheOutOfReach`, which failed in the
generated-code mode before the change). No define; noted in
[MIGRATING.md](../MIGRATING.md). The finding as it was:


For `[GCOptimize]` enums in generated code, the userdata cache of enum
values is the enum's class table itself, keyed by the integer value
(`LuaWrapPusher.tpl.txt:37-38` and `:62-64`,
`Binding.cs` `EnumCacheRefSlow`). No `debug` access is needed to plant a
value there: once a value has been pushed, `CS.E[1] = x` overwrites the raw
entry, and `rawset(CS.E, 1, x)` works before that; C# then pushes `x` for
that enum value. [C] by code reading; no test was run. `XLUA_CHECK_OBJECT_CACHE`
does not cover it, because the key is the enum value, not a pool index.
Options: a separate registry table per enum, like the object cache; or a
checked lookup that requires a tagged userdata whose `[1]` is the enum's
type id (a new export). The template is part of the source generator work.

### E. Type allowlist (audit item 12, not implemented)

Finding: **every C# object leads to reflection.** `obj:GetType()` (and
`typeof(CS.T)`) gives a `System.Type`, whose public members reach every
private member and every type:
`P(1):GetType():GetDeclaredField("secretField"):GetValue(nil)` reads a
private static field with every define on (`CaseRestrictPrivateAccess`). So
`XLUA_RESTRICT_PRIVATE_ACCESS` and `XLUA_NO_LOAD_ASSEMBLY` are defence in
depth only, until this item lands.

A useful allowlist therefore has three parts:

1. a type allowlist in `ObjectTranslator.FindType` and `getTypeId`
   (generated types plus a listed set): one `HashSet` lookup per type, when
   its type id is first created;
2. no reflection members on `System.Type` and `System.Object.GetType` for
   scripts: generated wrappers only, with the reflection fallback off, which
   [presets.md](presets.md) already plans for minimal;
3. `System.Reflection`, `System.IO`, `System.Diagnostics`,
   `System.Runtime.InteropServices` and `UnityEngine.Application.OpenURL`
   (and so on) not on the list.

Cost: none per call. Compatibility: large; scripts that reach arbitrary .NET
types need `legacy`. This needs a design of its own, together with the
source generator's type lists.

### F. The globals themselves

`XLUA_LOCK_XLUA_TABLE` does not stop `xlua = {}` or a script replacing `CS`,
`typeof`, `cast`, `base` or `require` for every other script in the same
`LuaEnv`. Only a separate environment table (or `LuaEnv`) per trust level
does; [Security.md](../../Assets/XLua/Doc/Security.md) says so.
