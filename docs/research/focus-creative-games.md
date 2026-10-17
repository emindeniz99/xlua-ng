# focus-creative-games (HybridCLR authors): zlua, ZenTS, luban, obfuz

Researched 2026-10-17 from read-only clones of the organisation's public repos.
All of them are MIT. HEADs:

- zlua `b608891` (2026-10-17, v1.1.0)
- zen-ts `1b7020d`
- luban `029f714` (v5.1.0)
- obfuz `6912a92` (v4.0.0)

Confidence: **[C]** confirmed in source or docs, **[S]** vendor claim,
**[U]** unconfirmed. HybridCLR itself is covered in [hotfix.md](hotfix.md).

## zlua

[zlua](https://github.com/focus-creative-games/zlua) is a Lua solution
compiled **into libil2cpp**, using the same install trick as HybridCLR.

- **Install:** it copies Unity's IL2CPP tree into the project and patches
  libil2cpp to call `zlua::LuaAppDomain::Initialize()` from `Runtime::Init`.
  It then adds about 15k lines of C++ plus the Lua sources and points
  `UNITY_IL2CPP_PATH` at the copy (`Editor/LocalInstaller.cs:296-312`). **[C]**
- **VM:** stock PUC Lua 5.1–5.5 or LuaJIT, **patched** with "FastMetatable".
  This caches index and newindex dispatch in `Table` and short-circuits
  `luaV_finishget`/`luaV_finishset`. There is one patch per Lua micro-version
  (`ZLua~/patches/lua/lua-5.4/5.4.7.patch`). **[C]**
- **Sources are not vendored:** Lua tarballs are downloaded from lua.org at
  install time with no checksum. LuaJIT is cloned at a branch tip
  (`Editor/Utils/LuaSourceCache.cs:158,203,238-250`). **[C]**
- **Runtimes:** the player is IL2CPP only. The Editor uses a separate Mono
  backend with P/Invoke, available on Windows and macOS only. There is no Mono
  player and no Linux Editor. **[C]**
- **One global state:** `luaL_newstate` and `luaL_openlibs` with every
  library. The C# API is `Initialize`, `Reset` and
  `GetFunction<TDelegate>`. There is no `DoString`, no table API from C#, and
  only one environment. **[C]**
- **Binding:**
  - Lua-to-C# members are bound lazily from IL2CPP metadata, public members
    only.
  - Calls go through C++ stubs generated with dnlib from the **stripped AOT
    assemblies**. Without HybridCLR this needs a throwaway player build.
  - Stubs are deduplicated by reduced signature
    (`Editor/Utils/NameUtil.cs:451-469`).
  - Without a stub, calls fall back to IL2CPP's native `invoker_method`, not
    `MethodInfo.Invoke`.
  - C#-to-Lua uses per-signature delegate thunks.
  - No partial classes, Roslyn or Cecil. **[C]**
- **Stripping:** members reached only from Lua are not preserved, and the
  docs give no stripping guidance. **[C/U]**
- **Performance** (vendor, IL2CPP Win64 desktop, Unity 2022.3, zlua on Lua
  5.4 against xLua's 5.3, xLua with full Generate Code and GCOptimize):
  - Lua→C#: xLua is 2.57× slower on average (instance call 21.6 ns vs
    58.6 ns; property get 21.7 vs 79.0).
  - C#→Lua: xLua is 1.59× slower.
  - Not measured: mobile devices, GC allocations, delegates. Bridge size
    claims are marked "to be measured" in their own docs. **[S]**
- **Missing:** hotfix, sandbox and limits, type definitions, Linux Editor,
  Mono player and several environments.
- **64-bit integers:** `ulong` values above 2^63 become floats
  (`marshal/PrimitiveMarshal.h:88-94`). **[C]**
- **Maturity:** about four months old, one author, private tests, no CI.
- **Positives:**
  - structs with reference fields live in userdata;
  - generics at run time;
  - migration adaptors for xLua, toLua and SLua.

### Lessons

**Copy:**

- **Dedup key:** one canonical "reduced marshal signature" function as the
  dedup key, with its own tests.
- **Every path tested:** run the whole test suite on every binding path
  (generated and reflection fallback).
- **A loud fallback:** in Development builds, log once per member that
  falls back to reflection.
- **Type definitions:** generate them from exactly the binding set, as
  ZenTS does for `.d.ts`.
- **Source plugin:** their fixes for compiling Lua lumped inside IL2CPP
  (`LocalInstaller.cs:542-876`: `getline`, the `luai_*` macros, `tmpnam`)
  are a checklist for our opt-in source plugin.
- **Error-in-callback test:** add one for LuaJIT on Win64 Mono. zlua
  reports SEH crashes when unwinding through reverse P/Invoke frames. **[S]**
- **Benchmark:** a comparable IL2CPP benchmark on device, reusing their
  case list (`zlua-benchmark/shared/case_manifest.txt`). Their speed suggests
  that the number of P/Invoke transitions per call dominates. Native helpers
  that push or fetch several arguments at once might be a cheap win.
  Measure this first. **[U]**

**Avoid:**

- patching libil2cpp or the Lua VM;
- generating bindings from a throwaway stripped build;
- unpinned source downloads;
- exposing every public API without limits.

**Differentiators to keep:**

- the sandboxed core preset with limits;
- several environments;
- hotfix;
- Mono player and Linux Editor;
- generated direct calls that survive High stripping;
- lossless uint64;
- our own compiler flags.

## ZenTS

[ZenTS](https://github.com/focus-creative-games/zen-ts) applies zlua's
architecture to QuickJS. It is 1.0.0-beta.1 with 8 commits and has been
quiet since 2026-08. It has a `.d.ts` generator covering the bound type set,
no memory or interrupt limits (`zents-runtime/jvm/JsEnv.cpp:65-71`), no tests
and no CI. Its `.d.ts` approach is the useful lesson. **[C]**

## luban

[luban](https://github.com/focus-creative-games/luban) is a game
configuration pipeline that turns Excel, JSON and YAML into code and data.
**It generates Lua loaders.** **[C]**

- **`lua-lua`:** Lua-literal data with EmmyLua `---@class` annotations and no
  C# bridge. It is a realistic fixture for the core preset's `LuaScript` and
  its limits.
- **`lua-bin`:** reads binary data through C# `ByteBuf` via xLua `CS.*`
  (luban_examples `Projects/Lua_Unity_xlua_bin`). That is a heavy per-field
  Lua→C# workload: a good benchmark for generated bindings and a candidate
  for an integration sample. The example still uses the old namespace
  `Bright.Serialization`.

## obfuz

[obfuz](https://github.com/focus-creative-games/obfuz) is an IL obfuscator.
It runs at `IPostBuildPlayerScriptDLLs` with order 10000, after xLua's hotfix
injection at order 0. **[C]**

- **It breaks xLua without changes.** xLua keys metatables on runtime
  `Type.FullName`, which obfuz renames (xlua-ng `Utils.cs:729,938,942,1093`).
  The obfuz docs describe hand-patching about ten xLua call sites
  ([work-with-xlua](https://www.obfuz.com/docs/manual/xlua/work-with-xlua)). **[C]**
- **What survives obfuscation:** generated bindings that register types and
  members under compile-time constant strings, never under `Type.FullName` or
  `MemberInfo.Name`. **[U, needs a test]**
- **What still breaks:** the reflection fallback and Lua-side hotfix names
  (`xlua.hotfix(CS.T, 'Method', ...)`), unless the targets carry
  `[ObfuzIgnore]`. This needs a docs page and an Editor warning. **[U]**
- **Already safe:** obfuz keeps `[MonoPInvokeCallback]` methods unrenamed.
  **[C]**

## Others

- **LeanCLR:** a CLR that replaces IL2CPP on WebGL and mini-games. The vendor
  claims 70–90% less AOT code. It supports `MonoPInvokeCallback`. Whether xLua
  works on it is untested; it is a low-priority compatibility target. **[S/U]**
- **LuaTier:** an experimental C++17 Lua 5.3 VM, 8.6× slower than PUC Lua
  5.3.6 by its own report. Not relevant now. **[C]**
