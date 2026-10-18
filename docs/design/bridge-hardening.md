# Bridge hardening: defines and open decisions

Status: **partly implemented, 2026-10-18; checks moved to the build type
2026-10-19.** The cheap proposals of the
[bridge audit](../research/bridge-audit.md) are implemented. The checks
that only catch script mistakes follow the build type in every preset; the
ones that change what correct scripts see or reach are opt-in C# defines,
off in `legacy` and on in `minimal`. The rest needs a decision from the
owner and is described below with its cost and the alternatives. Evidence,
`file:line` references and measurements are in the audit.

## Classification (owner decision 2026-10-19, 6a)

A `LuaEnv` runs trusted code; it is not a sandbox (`LuaScript` is,
[Security.md](../../Assets/XLua/Doc/Security.md)). A check that only refuses
what a correct script never does, such as `obj.Method(other)` where
`obj:Method()` was meant, is a debugging aid: script bugs show up during
development, and release players should not pay for it
([AGENTS.md](../../AGENTS.md), "no flag without a reason for both sides").
Each item of the 2026-10-18 family falls in one of three classes:

- **(a) Catches mistakes:** only values a correct script never passes
  change behaviour. On in the Editor, Development Builds and the
  `XLUA_GENERAL` tools, off in release players, in every preset.
- **(b) Changes what correct scripts see or reach:** a legacy script may
  depend on the old behaviour, so the preset decides, as before (opt-in in
  `legacy`, on in `minimal`, in every build type).
- **(c) Cheap and prevents undefined behaviour:** on everywhere, no flag.

| Item | Before (2026-10-18) | Class | Why | Cost per operation | Now |
|---|---|---|---|---|---|
| 2 | `XLUA_CHECK_OBJECTS` | a | refuses a foreign userdata as `self` or object argument (`obj.Method(file)`); correct scripts pass xLua's userdata | +10 to 13 ns per instance call, field or property access (+11% to +16%) | `XLUA_BRIDGE_CHECKS` |
| 5 | `XLUA_CHECK_STRUCT_ACCESSORS` | a | refuses a userdata that only looks like a struct in `xlua.genaccessor` accessors | +11 ns get, +13 ns set (13.7 to 24.5, 16.1 to 29.5 ns) | `XLUA_BRIDGE_CHECKS` |
| 7 | `XLUA_CHECK_OBJECT_CACHE` | a | ignores values planted in the userdata cache through `debug.getregistry`, which minimal release players do not even have (`XLUA_REDUCED_DEBUG`) | +14 ns (+8%) per push of an object Lua already holds | `XLUA_BRIDGE_CHECKS` |
| 2 (part) | none: `xlua_tocsobj_fast` read an int from any userdata | c | a light userdata (a C# `IntPtr`, so `obj.Method(ptr)`) made it read the memory the pointer names; a zero-sized one (LuaJIT's `newproxy()`) 4 bytes past its block | +1.8 ns per object read in release (2.5 to 4.3 ns, native) | always |
| 1 | `XLUA_LOCK_METATABLES` | b | `getmetatable(obj)` returns `"locked"`; legacy scripts patch classes through it ([xLua#77](https://github.com/Tencent/xLua/issues/77), [xLua#1169](https://github.com/Tencent/xLua/issues/1169)) | none per call | unchanged |
| 6 | `XLUA_RESTRICT_GETMETATABLE` | b | an edit of the table `xlua.getmetatable` returns no longer changes the type | one table copy per rare call | unchanged |
| 9 | `XLUA_LOCK_XLUA_TABLE` | b | legacy code adds functions to `xlua`; the helpers use the functions captured at start-up, so turning it off in release would change which `xlua.access` hotfix uses | +6 ns per `xlua.f` lookup | unchanged (question 1 below) |
| 10, 11 | `XLUA_RESTRICT_PRIVATE_ACCESS` | b | removes helpers scripts call (`xlua.access` outside hotfix) | none | unchanged |
| 12 | `XLUA_NO_LOAD_ASSEMBLY` | b | removes `xlua.load_assembly` | none | unchanged |

Not in this family and not changed here: `XLUA_REDUCED_DEBUG` already
follows the build type (B below); `XLUA_STRICT_INT_RANGE` refuses values a
script may pass on purpose (300 for a `byte`), so it is a preset question
of its own ([backlog.md](backlog.md)); the struct table rule of
[xLua#751](https://github.com/Tencent/xLua/issues/751) became a fix in
every preset on 2026-10-19.

## The bridge checks as implemented (2026-10-19)

- **One symbol, `XLUA_BRIDGE_CHECKS`,** replaces `XLUA_CHECK_OBJECTS`,
  `XLUA_CHECK_STRUCT_ACCESSORS` and `XLUA_CHECK_OBJECT_CACHE`. Nobody needs
  one of the three without the others: each guards a different entry point
  against the same kind of mistake.
- **Build type at compile time.** The `<xlua-preset>` block that
  `Tools/presets.py` writes into every C# file that tests a preset symbol
  (`BUILD_TYPE`) ends with

  ```csharp
  #if (UNITY_EDITOR || DEVELOPMENT_BUILD || XLUA_GENERAL) && !XLUA_NO_BRIDGE_CHECKS
  #define XLUA_BRIDGE_CHECKS
  #endif
  ```

  Unity defines `DEVELOPMENT_BUILD` for scripts of a Development Build, so a
  release player compiles none of the checks (`Debug.isDebugBuild` would
  keep their code in the player). The code tests `XLUA_BRIDGE_CHECKS` only:
  `ObjectTranslator.toCSObjectIndex` (`xlua_tocsobj_safe` instead of
  `xlua_tocsobj_fast`), `ObjectTranslator.tryGetCachedUserdata`
  (`xlua_tryget_cachedud_checked`) and the `LuaEnv` constructor
  (`xlua_check_struct_accessors`).
- **`XLUA_BRIDGE_CHECKS` as a scripting define** turns the checks on in
  release players too, for a project that wants them there.
- **`XLUA_NO_BRIDGE_CHECKS`** turns them off in the Editor, Development
  Builds and `XLUA_GENERAL`. Who needs it: a Development player profiled for
  call costs (the Unity Profiler attaches to Development Builds only), a
  program that ships on `XLUA_GENERAL` (xLua outside Unity has no release
  build type), and the host suite, which checks the release behaviour with it
  (`run_managed.py --define XLUA_NO_BRIDGE_CHECKS`). `XLUA_BRIDGE_CHECKS` wins
  when both are set.
- **Native:** no change in policy. The checked exports
  (`xlua_tocsobj_safe`, `xlua_tryget_cachedud_checked`,
  `xlua_check_struct_accessors`) are in every plugin and cost nothing until
  C# calls them; no CMake option guards a check, so there is nothing to
  mirror in debug and release native builds. The reader fix (below) changes
  no export, so it needs no bridge version of its own.
- **Class (c):** `xlua_tocsobj_fast` takes the index only from a full
  userdata at least an `int` long (`lua_touserdata` is NULL for other
  values, and `lua_rawlen` is 0 for a light userdata), and
  `xlua_tocsobj_safe` only from a full userdata. Both used to read through
  a light userdata's pointer. Release players use the fast reader for every
  instance call, so the fix matters more now that they run without the tag
  check.

**Measured** (`benchmark.py --modes sourcegen --filter lua_cs --runs 9`,
generated wrappers, Lua 5.5.1 Release plugin, macOS 27 arm64, Mono 6.14.1;
three interleaved rounds, lowest median in ns per operation; details in
[bridge-audit.md](../research/bridge-audit.md#bridge-checks-follow-the-build-type-2026-10-19)):

| Case | minimal before (HEAD `7a1006a3`) | minimal, release player now | minimal, debug build now | legacy before | legacy, release player now |
|---|---|---|---|---|---|
| instance method call | 96.0 | 83.6 | 99.0 | 84.0 | 83.0 |
| field get / set | 102.9 / 87.9 | 92.4 / 75.8 | 104.1 / 89.4 | 97.1 / 75.2 | 92.9 / 77.3 |
| property get / set | 102.8 / 89.1 | 90.2 / 76.1 | 102.0 / 90.6 | 92.2 / 82.6 | 93.2 / 77.1 |
| return an object Lua holds (`o:Self()`) | 196.8 | 174.3 | 198.9 | 166.5 | 170.8 |
| `xlua.genaccessor` get / set | 26.7 / 30.0 | 14.7 / 16.9 | 26.7 / 30.4 | 14.9 / 17.3 | 14.2 / 16.5 |
| `int[]` get / set | 115.1 / 113.8 | 104.6 / 101.8 | 117.0 / 113.5 | 101.3 / 99.1 | 103.3 / 103.3 |

A minimal release player now runs instance calls, fields and properties
11 to 14 ns faster, a pushed object it already holds 22 ns faster, and the
accessors at half the cost; a minimal debug build costs what minimal cost
before. Legacy release players are unchanged within the noise (±5 ns at a
load average of 40 to 100 from other jobs); the reader fix costs 1.8 ns in
a native loop. The `xlua.f` lookup stays 12 ns in minimal (6 to 8 in
legacy), the price of `XLUA_LOCK_XLUA_TABLE`.

## Decisions for the owner

### A. Make the three checks unconditional? (superseded 2026-10-19)

`XLUA_CHECK_OBJECTS`, `XLUA_CHECK_STRUCT_ACCESSORS` and
`XLUA_CHECK_OBJECT_CACHE` change behaviour only for values a correct script
never passes: a foreign userdata as `self`, a userdata that merely looks like
a struct, a value planted in the cache. They were opt-in because the audit's
rule made every behaviour change opt-in in `legacy`.

**Decided 2026-10-18** with the provisional values of the presets
([presets.md](presets.md#owner-decisions-2026-10-18), item 1): they stay
opt-in in legacy and on in minimal. **Replaced 2026-10-19 (owner, 6a):**
they follow the build type in every preset, as `XLUA_BRIDGE_CHECKS`
([above](#the-bridge-checks-as-implemented-2026-10-19)).

### Open questions (2026-10-19)

1. `XLUA_LOCK_XLUA_TABLE` stays class (b): on in every minimal build, +6 ns
   per `xlua.f` lookup. Moving it to the build type in minimal would save
   that in release players, but release and development would then differ
   in which functions `xlua.hotfix` and the `CS` lookup use after a script
   replaces `xlua.access` or `rawset`. Recommendation: keep it.
2. `XLUA_NO_BRIDGE_CHECKS` is public, for profiling Development players
   and `XLUA_GENERAL` programs. If nobody outside the tests needs it, it can
   become a test-only symbol.
3. The struct header: the default `xlua.genaccessor` accessors and the
   pack/unpack helpers trust `len` in any full userdata whose first int is
   -1, so a foreign userdata can still be read or written past its block in
   release players. Comparing `len` with `lua_rawlen` would make it a
   class (c) fix like the reader's; not measured (one more C API call per
   struct field access in generated code).
4. The owner-thread check that
   [crash-inventory.md](../research/crash-inventory.md) proposes for
   `LuaEnv.L` (items 5 and 6: two threads in one `LuaEnv`, finalizers that
   call Lua) catches a mistake too, so it would join these checks rather
   than get a define of its own: `#if XLUA_BRIDGE_CHECKS && !THREAD_SAFE` in
   `LuaEnv.cs`, which already has the `<xlua-preset>` block. It would then
   be on in the Editor, Development Builds and `XLUA_GENERAL`, forced into
   release players by `XLUA_BRIDGE_CHECKS` and left out by
   `XLUA_NO_BRIDGE_CHECKS`. Not added here.

### B. Reduced `debug` library (audit item 7, done 2026-10-18 with option 1)

Implemented as option 1 below, with the define of option 2 deciding the
default: `LuaOptions.Libraries` (`LuaLibs`) selects the libraries per
`LuaEnv`; without `LuaLibs.Debug` the `debug` table has only `traceback` and
`getinfo`. `XLUA_REDUCED_DEBUG`, which `XLUA_PRESET_MINIMAL` implies, makes
`LuaLibs.Default` leave out `Debug` in release players, not in the Editor,
Development Builds or the `XLUA_GENERAL` tools; a `LuaEnv` that needs the
whole library passes `LuaLibs.All`. `init_xlua` captures the functions its
`setfenv` emulation needs, and `xlua.util` uses the `|` operator when
`debug.getmetatable` is missing; `util.state`, `util.print_func_ref_by_csharp`
and, on LuaJIT, `util.createdelegate` still need the whole library. The
native library keeps `ldblib.c`. Details and tests are in
[presets.md](presets.md#library-parts-and-the-run-time-mask). The analysis as
it was:

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
