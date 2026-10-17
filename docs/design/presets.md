# Presets, size flags and the bridge-less core

Status: **design, agreed in discussion on 2026-10-17, not implemented**.
Measurements behind it are in [SIZE.md](../../build/SIZE.md),
[UNITY_SPIKE.md](../../build/UNITY_SPIKE.md) and [research](../research/README.md).

## Goals

- A small library: every optional feature can be turned off, and a project
  pays only for what it uses.
- Only public, documented Unity mechanisms are used: P/Invoke,
  `__Internal`, source plugins, IL2CPP's `FORCE_PINVOKE_*`, Roslyn analyzers
  and the documented build callbacks. There are no patches to libil2cpp and
  no generated code against its internals. Those approaches (PuerTS
  xIl2cpp, zlua, HybridCLR) break on custom engine builds, Tuanjie and every
  Unity upgrade.
- Breaking changes are allowed. xlua-ng has no existing users. The upstream
  behaviour stays available through the `legacy` preset.
- Every change is covered by tests: the native suite, the Unity editor
  suite and player size runs.

## Presets

Three presets. `legacy` is the default for now; switching the default to
`minimal` later means legacy users add one scripting define.

| Feature | legacy (default) | minimal | core | Development build |
|---|---|---|---|---|
| C# bridge (`CS.*`) | ✓ | ✓ | ✗ | |
| Source-generated bindings | ✓ | ✓ | – | |
| Reflection fallback and CodeEmit | ✓ | opt-in | – | |
| StreamingAssets searcher (`XLUA_NO_STREAMING_ASSETS_LOADER`, done) | ✓ | ✗ | ✗ | |
| Locked bridge metatables (`XLUA_LOCK_METATABLES`, done) | opt-in | ✓ | – | |
| SignatureLoader | ✓ | ✗ | opt-in | |
| `template` library at run time (Editor always has it) | ✓ | ✗ | ✗ | |
| `xlua.util`, embedded in C# instead of Resources | ✓ | opt-in | ✗ | |
| `perf.profiler` | ✓ | ✗ | ✗ | on |
| LuaSocket | ✓ | ✗ | ✗ | on |
| `mime.c` | ✓ | ✗ | ✗ | |
| Memory leak checker | ✓ | ✗ | ✗ | on |
| uint64 helpers (i64lib on 5.3+) | ✓ | ✓ | ✗ | |
| `io` library | ✓ | ✗ | ✗ | |
| `os` library | ✓ | `time`, `clock`, `date`, `difftime` | ✗ | |
| `debug` library | ✓ | ✓ | ✗ | |
| `package.loadlib`, `dofile`, `loadfile` | ✓ | ✗ | ✗ | |
| Exported native symbols | all Lua API | used by C# + listed extras | used by C# | |
| Lua versions | 5.3, 5.4, 5.5, LuaJIT | 5.3, 5.4, 5.5, LuaJIT | 5.4, 5.5 | |
| Android minimum API | 23 | 23 | 23 | |

Notes on individual features:

- **Removed in every preset:** `tdr` (see [research/tdr.md](../research/tdr.md))
  and `perf/memory.lua.txt`, a stub whose functions only raise errors.
- **LINQ:** already removed from the runtime (−54 KB APK).
- **The uint64 helpers in minimal:** they stay on because C# `ulong` values
  above 2^63 would otherwise show up negative in Lua. A flag turns them off.
- **Exported symbols:** the export list is computed from the enabled features
  (`Test/Native/managed_imports.py`). A project that loads its own native Lua
  modules either lists the symbols those modules need or turns on full Lua API
  export.

## Mechanism

- **C# features** are controlled by scripting defines and cost nothing to
  switch.
- **Native features** need a different plugin build. xlua-ng ships prebuilt
  `legacy`, `minimal` and `core` plugins. An Editor
  `PluginImporter.SetIncludeInBuildDelegate` picks one by define and by
  Development Build. Define Constraints do not work for native plugins; see
  [research/platform-levels.md](../research/platform-levels.md). Any other
  combination is built with `build/make_*.sh` and CMake options.
- **Lua sources from Resources** (`xlua.util`, the profiler) move into C#
  string constants under `#if`. A define can then include or exclude them,
  while `require` keeps working.
- **Opt-in source plugin:** IL2CPP compiles the C sources into the player.
  This saved 48 KB on Android and 31 KB on iOS, but Unity's compiler flags
  cannot be chosen per file; see [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md).
- **Test matrix:** each preset, plus "legacy with one feature off" for every
  feature. Not every combination.

## Native layers

```
xlua_core.c   shared by every preset: limited allocator, instruction hook,
              safe C# callback trampoline, library selection, primitive push/get, hash seed
xlua.c        legacy and minimal only: the C# bridge (object handles, indexers, struct packing)
```

The core preset builds `xlua_core.c` alone; the other presets build both files.

## Limits in every preset (`LuaOptions`)

```csharp
new LuaEnv(new LuaOptions {
    MemoryLimit      = 64 << 20,   // 0 = unlimited
    InstructionLimit = 0,          // 0 = unlimited
    Libraries        = LuaLibs.Default,
    HashSeed         = null,       // null = random; fixes pairs() order when set
});
```

- **Cost:** when the limits are off, they cost nothing. The cost when they are
  on is to be measured.
- **`HashSeed`:** this is the one source of non-determinism a script cannot
  control. Scripts already control `math.randomseed` themselves. Lua 5.5
  passes the seed to `lua_newstate`; Lua 5.4 sets it at build time.
- **Known limit:** the instruction hook does not fire inside a single C
  function. A pattern such as `("a"):rep(1e5):find(".-.-.-.-b")` can run for a
  long time. This is documented rather than disabling the pattern functions.
  - **Proposed fix:** a small `lstrlib.c` patch that checks the budget in
    `match()`, as Luau does ([research/luau.md](../research/luau.md)).
- **`TimeLimit`:** a wall-clock limit next to `InstructionLimit`, checked in
  the hook. A watchdog thread may arm the hook; `lua_sethook` is
  signal-safe.

## Core preset: `LuaScript`

The core preset is Lua without the C# bridge. It runs untrusted or remote Lua
logic: values in, values out.

```csharp
using var lua = new LuaScript(new LuaOptions {
    MemoryLimit = 4 << 20, InstructionLimit = 10_000_000,
    Libraries = LuaLibs.String | LuaLibs.Table | LuaLibs.Math });
lua.Register("log", args => Debug.Log(args.GetString(0)));  // only what C# allows
lua.Load(remoteCode, "remote");                              // text only, no bytecode
long reward = lua.Call<long>("calcReward", level, "gold");
```

- **Values:** `nil`, `bool`, `long`, `double`, `string` and `byte[]`. Tables
  are converted to and from `Dictionary`/`List` trees by a small converter;
  ObjectTranslator is not used. The `long`, `double` and `bool` paths allocate
  nothing.
- **Coroutines:** a script can `coroutine.yield(request)`. C# resumes it from
  a Unity coroutine (`IEnumerator`) or from `async`/`await` (Task, Awaitable,
  UniTask). Everything runs on the main thread.
- **Sandbox:**
  - no `io`, `os`, `debug`, `package.loadlib`, `dofile` or `loadfile`;
  - text chunks only;
  - memory and instruction limits;
  - SignatureLoader is optional, for checking where code came from.
- **Packaging:** a separate `Core/` folder and asmdef that shares `LuaDLL.cs`
  and ships its own core native plugin. With the UPM layout this becomes two
  packages, `xlua-ng-core` and `xlua-ng`, and the full package depends on core.
- **Lua versions:** 5.4 and 5.5. LuaJIT's JIT is not allowed on iOS, and 5.3
  no longer receives fixes.
- **Size:** estimated at about 250–290 KB stripped for the native library
  (about 110–120 KB gzip) plus a few KB of C#. Full xlua-ng adds 1.36 MB to an
  APK today.

## Obfuscation

See [research/obfuscators.md](../research/obfuscators.md).

- **Name table:** the source generator's compile-time name table (literals
  plus `typeof`) replaces every runtime `Type.FullName` and name lookup,
  including hotfix fields and `hotfix_id_map`.
- **`[Obfuscation(Exclude)]`:** emitted on the XLua assembly and on injected
  members, and read at Editor time together with the tool-specific attributes.
- **Callbacks:** use `AOT.MonoPInvokeCallbackAttribute` under Unity.
- **Name mapping:** `IXLuaNameMapper` maps Type to original name, plus an
  Editor-side `IObfuscatorAdapter`.
- **obfuz:** adapter and CI test. GuardingPear, Beebyte and others get
  attributes and docs. Metadata and native protectors get docs only.

## Lua version guidance

Lua 5.5.1 stays the default. For conservative production use, the docs
recommend 5.4: it has been in use since 2020, has had nine bugfix releases,
and has a mature bug list. Lua 5.5 is newer, and adds global declarations,
compact arrays and the new incremental GC.

## Legacy code generation goes before the first public release

Decided 2026-10-17: the partial-class code generation (the "Generate Code"
menu, the `.tpl.txt` templates, the `Gen/` folder) is removed before the
first public release, so that release ships as a UPM package with source
generation only. Two conditions:

1. **The source generator reaches parity:**
   - wrappers;
   - C#-to-Lua delegate and interface bridges;
   - GCOptimize struct packing;
   - hotfix bridges (the `Inline` path or a bridge class per assembly);
   - a public binding API that generated code in user assemblies calls,
     because XLua moves into its own asmdef.
2. **A migration tool is ready.** In the Editor it evaluates the existing
   configuration (`LuaCallCSharp`, `CSharpCallLua`, `Hotfix`, `ReflectionUse`,
   `GCOptimize`, `BlackList`, including reflection-based `by_property` lists)
   and writes the resulting type lists as attributes or a generated config
   file that the source generator reads. It also scans the project and Lua
   sources and reports what it cannot convert:
   - custom generation templates;
   - legacy-only features in use (StreamingAssets loader, metatable
     patching, `tdr`, `perf.memory`);
   - the defines the project needs.

## Order of work

1. **C# flags:**
   - SignatureLoader;
   - `template`;
   - embedded `xlua.util` and profiler;
   - removing `tdr` and `perf/memory`;
   - LuaSocket registration.
2. **Native options:**
   - LuaSocket, `mime`, the leak checker, the uint64 helpers;
   - choosing the standard libraries;
   - the computed export list;
   - API 23 linker features and the size flags chosen by the device benchmark;
   - plugin variant selection.
3. **`LuaOptions` limits and `xlua_core.c`.**
4. **Source-generated bindings**, with a minimum of Unity 2021.3, a 4.3.1
   generator and a reflection fallback when no registration ran (see
   [SOURCEGEN_SPIKE.md](../../build/SOURCEGEN_SPIKE.md)).
5. **Core preset and `LuaScript`.**
6. **Docs:** for every flag, its saving and what it breaks; measured APK and
   IPA size per preset.

## Open questions

- **Device benchmark:** which `-O`/LTO/linker combination wins?
- **Hotfix:** keep Cecil, try ILPostProcessor, or document HybridCLR? Research
  is in progress.
- **PuerTS:** lessons from its design; research is in progress.
