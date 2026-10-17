# PuerTS: design lessons for xlua-ng

Researched 2026-10-17 from a read-only clone of
[Tencent/puerts](https://github.com/Tencent/puerts). HEAD was `0ac7d2f1`
(2026-10-15); the latest Unity tag was `Unity_v3.0.3`. Nothing was built or
run. Items marked **[inferred]** were not read directly in code.

Path prefixes:

- `U/` = `unity/`
- `upms/` = `unity/upms/`
- `nat/` = `unity/native/`
- `X/` = `unity/upms/core/Editor/Resources/puerts/xil2cpp/`

## Background

- **Authors:** PuerTS and xLua have the same author. The commit names johnche
  and chexiongsheng share one address.
- **History:** v1.0 shipped in 2020-09. 3.0.0 (2025-12) introduced a new core
  with language backends: V8, QuickJS, Node.js, Python, and **Lua 5.4.8**,
  added in 2025-06.
- **xLua heritage:** the Lua backend's `nat/papi-lua/CMakeLists.txt` still
  carries xLua's licence header, flags and comments.
- **Architecture:** a C function-table ABI, *pesapi*, sits between the C++
  bridge and each VM. It has about 86 function pointers
  (`nat/puerts/include/pesapi.h:230-318`).
  - Each backend exports only an entry point that returns its table
    (`GetLuaFFIApi`, `nat/papi-lua/source/PapiExport.cpp:14-49`).
  - The core library holds the type registry and the P/Invoke trampolines.

## Binding

| Mechanism | How it works |
|---|---|
| Reflection (Mono, Editor, .NET; IL2CPP only with `PUERTS_DISABLE_IL2CPP_OPTIMIZATION`) | Wrappers are `System.Linq.Expressions` trees compiled at run time (`PInvoke/Wrapper/ExpressionsWrap.cs`). |
| Static C# wrappers (3.0.2+) | The **same expression trees are printed as C# source** (`StaticWrapperGenerator.cs:19-24`), so runtime and generated wrappers share one marshalling definition. The registration class is found by name through reflection (`PInvoke/ScriptEnv.cs:190-200`). |
| xIl2cpp (default on IL2CPP in 3.x) | The editor writes C++ that IL2CPP compiles into libil2cpp. It calls `methodPointer` directly with typed arguments, so there is no P/Invoke per call and no `MonoPInvokeCallback`. It also has a GC-free reflection path through `invoker_method` with `alloca` (`X/Puerts_il2cpp.cpp.txt:2081-2100`). |

- **Configuration:** the same model as xLua. Editor classes with
  `[Configure]` and static lists (`[Binding]`, `[Typing]`, `[Filter]`), found
  by scanning the AppDomain (`Editor/Src/Configure.cs`). Filters return a
  binding mode (Fast, Lazy, Slow, Dont). `SetDefaultBindingMode(DontBinding)`
  gives a whitelist.
- **Deduplication:** wrappers and bridges are deduplicated **by signature**
  (`CSharpFileExporter.cs:335`).
- **Fallback per member:** a method without a wrapper for its signature falls
  back to reflection (`X/Puerts_il2cpp.cpp.txt:2556-2569`). Fully shared
  generic instances always use reflection.
- **No compiler tooling:** no Roslyn, ILPostProcessor or Cecil anywhere in
  the history. The generator was JS in V8 inside the editor until 3.0.2 and is
  pure C# since then.
- **No `partial` of user types:** generated code lives in fixed namespaces
  and is found by name.
- **Stripping:** `link.xml` with `preserve="all"` per `[Binding]` type, as in
  xLua. `[Preserve]` on types reached by reflection.

## Packaging and native builds

- **Packages:** one UPM package per backend plus core, all depending on core.
- **Variant selection:** done by choosing packages. There is no
  `SetIncludeInBuildDelegate`. Each backend uses unique library and export
  names, so static variants can coexist on iOS and WebGL.
- **Native libraries for a Lua game:** `libPuertsCore.so`, `libPapiLua.so`
  and a websocket addon. **[inferred]** Each `.so` has its own static libc++.

| Setting | PuerTS (Lua backend unless noted) | xlua-ng today or planned |
|---|---|---|
| Exports | Lua: everything (no visibility or version script); V8: a 15-symbol version script | Minimal export list planned (−26 KB measured) |
| Optimisation | Release `-O3`; C++ only `-fno-exceptions -fno-rtti -fno-(asynchronous-)unwind-tables` | Hybrid `-O3`/`-Oz` under evaluation |
| NDK, Android API | r27d; API 21 (Lua, QuickJS, core), 24 (V8, Node.js) | r30; API 23 planned |
| 16 KB pages | Core, QuickJS and websocket only; **not PapiLua** | All libraries |
| iOS | Static `.a`, deployment target 7.0, bitcode | Static `.a` |
| WebGL | Static `.a` from emsdk 3.1.8; editor edits `PlayerSettings.WebGL.emscriptenArgs` | Source amalgamation |
| OHOS | OHOS SDK 4.1, Tuanjie platforms | SDK 7.0 |

## Other findings

- **Size:** no player-size measurements are published. QuickJS is described
  as "very small", and reflection or minimal-bridge mode as smaller. LINQ is
  still used in the IL2CPP runtime.
- **Performance:** they report about twice xLua's interop speed on Android
  with xIl2cpp and about the same on iOS (`doc/unity/en/performance/index.md`).
  Blittable structs cost a userdata and a malloc each; xLua's in-userdata
  `[GCOptimize]` packing is tighter. xIl2cpp requires the Boehm GC.
- **Hotfix and debugging:** no hotfix and no Lua debugger hooks. V8 has the
  Chrome inspector.
- **Loaders:** synchronous `ILoader` interfaces. The Lua searcher loads text
  only (`load(content, path, 't')`, `BackendLua.cs:220`). A `.lua`
  ScriptedImporter produces TextAssets.
- **Sandbox:** no memory or instruction limits in any backend. Lua opens every
  standard library.
- **Tests:** one C# test corpus runs under dotnet (NUnit) and in players
  (through an NUnit shim whose attributes derive from `PreserveAttribute`).
  CI covers Unity 2022.3 editor tests in three binding modes, an IL2CPP x86_64
  APK on an API 24 emulator, and dotnet on three OSes. iOS, WebGL and OHOS are
  not tested on devices.
- **Unity versions:** the README says "unity 5 ~ latest". In practice the 3.x
  IL2CPP path needs 2020.1+. The generated C++ freezes the editor's version
  macros in a header, which must be regenerated after an engine upgrade.

## Lessons

**Copy:**

1. Deduplicate wrappers by signature, with the reflection fallback chosen per
   member rather than per type. Use this as the key of the
   IIncrementalGenerator.
2. One marshalling model shared by the generator and the reflection path.
   This prevents drift between generated, emitted and reflected behaviour.
3. Find generated registration by name across assemblies instead of using
   `partial`.
4. Lazy type registration through a class-not-found callback, plus a
   whitelist mode.
5. Unique library and export names per plugin variant, so static variants can
   coexist. This complements `SetIncludeInBuildDelegate`.
6. A minimal export list, as PuerTS does for V8 (`papi-v8/CMakeLists.txt:373-425`).
7. One test corpus for dotnet, editor and device, plus an IL2CPP APK on an
   emulator in CI.
8. A `.meta` linter in CI. A wrong `.meta` dropped an arm64 library from the
   PuerTS 3.0.3 package (`U/changelog.md:29`).

**Avoid:**

1. Generated C++ against libil2cpp internals: a Boehm-only assert, iOS header
   patches, frozen version macros. A source plugin of Lua's own C code through
   `FORCE_PINVOKE_xlua_INTERNAL` does not touch libil2cpp.
2. Generating wrappers for every public type in the AppDomain, or scanning IL
   for delegate types.
3. LINQ and expression trees in the runtime; expression trees are interpreted
   on IL2CPP.
4. Failing the build when generated files are missing. Fall back to
   reflection instead.
5. Persistent changes to PlayerSettings.
6. Several `.so` files, each with its own libc++.
7. Their Lua plugin gaps: no 16 KB alignment, every symbol exported, iOS
   target 7.0 with bitcode, every standard library opened.
8. Their usability regressions compared with xLua: indexers only through
   `get_Item`/`set_Item`, `ref`/`out` through tables, and a malloc per struct.

**New ground for xlua-ng:** the bridge-less `LuaScript` with memory and
instruction limits, source-generated bindings, and size-driven presets have
no counterpart in PuerTS.

## Native binaries compared (release `Unity_v3.0.3`)

Measured 2026-10-17 from `PuerTS_Lua_3.0.3.tar.gz` (SHA256 `bd7b0558…1961`)
and `PuerTS_Core_3.0.3.tar.gz` (SHA256 `feafc11b…cea0`). Nothing from the
archives was executed. "Stripped" is `llvm-strip --strip-unneeded`, which
reproduces the xlua-ng numbers in [SIZE.md](../../build/SIZE.md) exactly.

Android arm64-v8a:

| Library | Stripped | gzip -9 | PT_LOAD align | Exports | Notes |
|---|---|---|---|---|---|
| PuerTS `libPapiLua.so` (Lua 5.4.8) | 342,736 | 141,504 | **4 KB** | full Lua API, 123 C++ symbols, 4 entry points | NDK r27, clang 18 |
| PuerTS `libPuertsCore.so` | 39,912 | 15,574 | 16 KB | pesapi trampolines | NEEDED by PapiLua |
| PuerTS `libWSPPAddon.so` (debugger WebSocket) | 1,782,768 | 522,431 | 16 KB | 3,202 | static libc++, websocketpp and asio; enabled for Android by default in Core |
| xlua-ng `libxlua.so` 5.5.1 | 418,272 | 169,063 | 16 KB | 111 `lua_*`, 49 `luaL_*`, xlua_* | NDK r30, clang 22; includes LuaSocket (48 KB) |
| xlua-ng `libxlua.so` 5.4.9 | 399,624 | 159,820 | 16 KB | same | |

Total native bytes a Lua game ships on arm64:

| Configuration | Stripped | gzip -9 |
|---|---|---|
| PuerTS minimum (PapiLua + Core) | 382,648 | 157,078 |
| PuerTS as imported (+ WSPPAddon) | 2,165,416 | 679,509 |
| xlua-ng 5.5.1 | 418,272 | 169,063 |
| xlua-ng 5.5.1 without LuaSocket (estimate) | ~370,000 | ~148,000 |

Notes:

- **Same features, similar size:** for the same feature set, PuerTS's Lua
  backend is about as large as xlua-ng's, or slightly larger.
- **Debugger addon:** unless a project deletes it, the addon multiplies the
  native cost by 4–5×.
- **16 KB alignment:** `libPapiLua.so` is 4 KB aligned on both ABIs, so it is
  not ready for 16 KB page-size devices.
- **armeabi-v7a:** the PuerTS minimum is 260,168 bytes stripped
  (139,798 gzip), against xlua-ng's 273,144 (148,517).
- **iOS:** PuerTS ships `.a` archives with embedded bitcode (774 KB in
  PapiLua) and a minimum iOS of 9.0. The `__TEXT` of PapiLua plus Core is
  255,040 bytes, against xlua-ng's 294,891 with LuaSocket.
- **WebGL:** PuerTS ships wasm32 `.a` archives, 250,833 bytes of code for
  PapiLua plus Core. xlua-ng compiles one translation unit inside the Unity
  build, so the two are not directly comparable.

## Not done

- **Running PuerTS-Lua:** no size or performance run in the xlua-ng probe
  project. The managed C# side, which dominates player size for both
  projects, was not compared.
