# Presets, size flags and the bridge-less core

Status: **design, agreed in discussion on 2026-10-17. `legacy` and `minimal`
are implemented (2026-10-18, [below](#implemented-legacy-and-minimal)); `core` is
not.** Values marked *provisional* wait for the owner's decision.
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
| Reflection fallback and CodeEmit | ✓ | opt-in (not implemented: still on) | – | |
| StreamingAssets searcher (`XLUA_NO_STREAMING_ASSETS_LOADER`, done) | ✓ | ✗ | ✗ | |
| Locked bridge metatables (`XLUA_LOCK_METATABLES`, done) | opt-in | ✓ | – | |
| Tag check on C# objects read from Lua (`XLUA_CHECK_OBJECTS`, done) | opt-in | ✓ | – | |
| Tag check in `xlua.genaccessor` accessors (`XLUA_CHECK_STRUCT_ACCESSORS`, done) | opt-in | ✓ | – | |
| `xlua.getmetatable` without `__gc` and the tag (`XLUA_RESTRICT_GETMETATABLE`, done) | opt-in | ✓ | – | |
| Checked userdata cache hits (`XLUA_CHECK_OBJECT_CACHE`, done) | opt-in | ✓ | – | |
| Read-only `xlua` table (`XLUA_LOCK_XLUA_TABLE`, done) | opt-in | ✓ | – | |
| Private member helpers only for hotfix (`XLUA_RESTRICT_PRIVATE_ACCESS`, done) | opt-in | ✓ | – | |
| `xlua.load_assembly` (`XLUA_NO_LOAD_ASSEMBLY` removes it, done) | ✓ | ✗ | – | |
| Type allowlist for `CS.*` and reflection ([bridge-hardening.md](bridge-hardening.md), not implemented) | ✗ | ✓ | – | |
| Reduced `debug` library (`traceback`, `getinfo`; [bridge-hardening.md](bridge-hardening.md), not implemented) | ✗ | release builds (not implemented: full) | ✗ | full |
| SignatureLoader (`XLUA_NO_SIGNATURE_LOADER`, done) | ✓ | ✗ | opt-in | |
| `template` library at run time, Editor always has it (`XLUA_NO_TEMPLATE`, done) | ✓ | ✗ | ✗ | |
| `xlua.util`, embedded in C# instead of Resources (`XLUA_NO_UTIL`, done) | ✓ | opt-in (`XLUA_UTIL`) | ✗ | |
| `perf.profiler`, embedded in C# (`XLUA_NO_PROFILER`, done) | ✓ | ✗ | ✗ | on |
| LuaSocket (CMake `XLUA_LUASOCKET`, done) | ✓ | ✗ | ✗ | on |
| `mime.c` (CMake `XLUA_MIME`, done) | ✓ (provisional) | ✗ | ✗ | |
| Memory leak checker (CMake `XLUA_LEAK_CHECKER`, done) | ✓ (provisional) | ✗ | ✗ | on |
| Memory snapshot walk for `LuaEnv.TakeMemorySnapshot()` (CMake `XLUA_MEMORY_SNAPSHOT`, done) | desktop and iOS on; Android, OpenHarmony, UWP, Switch off | ✗ | ✗ | on |
| uint64 helpers (i64lib on 5.3+; CMake `XLUA_UINT64`, done; LuaJIT always) | ✓ | ✓ | ✗ | |
| `io` library (CMake `XLUA_LIBS`, done) | ✓ | ✗ | ✗ | |
| `os` library (`XLUA_LIBS` drops it whole; the subset is not implemented) | ✓ | `time`, `clock`, `date`, `difftime` (not implemented: whole, provisional) | ✗ | |
| `utf8`; LuaJIT's `bit`, `jit` and `ffi` (`XLUA_LIBS`) | ✓ | ✓ (provisional) | | |
| `debug` library (`XLUA_LIBS`; the bridge needs it) | ✓ | ✓ | ✗ | |
| `package.loadlib`, `dofile`, `loadfile` | ✓ | ✗ (not implemented: still there) | ✗ | |
| Exported native symbols (`XLUA_EXPORTS`, the list follows the options, done) | all Lua API | used by C# + listed extras (`managed` for ELF shared libraries, `all` elsewhere; provisional) | used by C# | |
| Identical code folding (CMake `XLUA_ICF`, Android and OpenHarmony) | ✓ (provisional) | ✓ (provisional) | | |
| Compiler hardening (CMake `XLUA_HARDEN`; the cheap set of [FLAGS.md](../../build/FLAGS.md#a-cheaper-hardening-split) is not applied) | ✗ (provisional) | ✗ (provisional) | | |
| Lua versions | 5.3, 5.4, 5.5, LuaJIT | 5.3, 5.4, 5.5, LuaJIT | 5.4, 5.5 | |
| Android minimum API | 23 | 23 | 23 | |

Notes on individual features:

- **Removed in every preset:** `tdr` (see [research/tdr.md](../research/tdr.md))
  and `perf/memory.lua.txt`, a stub whose functions only raise errors. Done
  2026-10-17; `Assets/XLua/Resources` no longer exists.
- **C# feature defines (done 2026-10-17):** each one is an opt-out define, so
  that without presets the default stays `legacy`. The minimal preset sets the
  defines of the features it leaves out (done 2026-10-18). `XLUA_NO_PROFILER` keeps the profiler
  when `UNITY_EDITOR` or `DEVELOPMENT_BUILD` is defined; `XLUA_NO_TEMPLATE`
  keeps the library and the engine in the Editor (and the engine in
  `XLUA_GENERAL` tools).
- **LINQ:** already removed from the runtime (−54 KB APK).
- **Bridge hardening defines (done 2026-10-18):** opt-in C# defines, so legacy
  is unchanged; minimal sets them all. Whether the three checks become
  unconditional in every preset ([decision A](bridge-hardening.md#a-make-the-three-checks-unconditional))
  is open; until then they stay opt-in in legacy (provisional). Costs, tests and the items left
  for the owner are in [bridge-hardening.md](bridge-hardening.md) and
  [bridge-audit.md](../research/bridge-audit.md). The `debug` row above stays
  ✓ for minimal until the reduced `debug` table lands.
- **The uint64 helpers in minimal:** they stay on because C# `ulong` values
  above 2^63 would otherwise show up negative in Lua. A flag turns them off:
  `XLUA_UINT64=OFF` (done 2026-10-18) leaves the `uint64` table out on Lua 5.3
  and later; the values keep all 64 bits either way. LuaJIT refuses it, because
  its int64 userdata needs the table ([build/README.md](../../build/README.md#the-uint64-table-and-64-bit-integers)).
- **Native options (done 2026-10-18):** `XLUA_LEAK_CHECKER`, `XLUA_UINT64` and
  `XLUA_LIBS` (the standard libraries the library contains and opens; `all` by
  default, and the bridge presets need `package` and `debug`), each also read
  from the environment, with measured sizes in
  [SIZE.md](../../build/SIZE.md#leak-checker-uint64-table-and-standard-libraries).
  On Android arm64-v8a, Lua 5.5.1: 1.4 KB stripped each for the first two, 17.6
  KB (7.4 KB of APK) for leaving `io`, `os` and `utf8` out, and 65 KB (26 KB of
  APK) for those three, LuaSocket and both helpers together. `minimal`'s `os`
  subset and the removal of `package.loadlib`, `dofile` and `loadfile` are
  still to do; they trim the tables at run time rather than leave code out.
- **Exported symbols:** the export list is computed from the enabled features
  (`Test/Native/managed_imports.py`). A project that loads its own native Lua
  modules either lists the symbols those modules need or turns on full Lua API
  export. Implemented 2026-10-17 as the CMake option `XLUA_EXPORTS`: `all`
  (default, legacy) or `managed` (hidden visibility and a version script from
  `build/xlua_exports.txt`, the functions `LuaDLL.cs` imports), plus
  `XLUA_EXPORTS_EXTRA` for the listed extras. Since 2026-10-18 the list follows
  the native options: `build/xlua_exports.txt` names the option behind each
  optional function, CMake leaves the functions of a part that is off out, and
  `check_exports.py` and `check_android_elf.py` check every part against the
  `XLUA_EXPECT_*` variables ([build/README.md](../../build/README.md#the-export-list-follows-the-options)).
  The presets' lists will differ further through the C# defines that remove
  imports.
- **Size flags (done 2026-10-17):** every preset builds Android and
  OpenHarmony with `XLUA_SIZE_FLAGS` (per-file `-O3`/`-Oz`, identical code
  folding, packed relocations at API 23); see
  [build/README.md](../../build/README.md#android-size-flags).

## Mechanism

- **C# features** are controlled by scripting defines and cost nothing to
  switch.
- **Native features** need a different plugin build. xlua-ng ships prebuilt
  `legacy`, `minimal` and `core` plugins. An Editor
  `PluginImporter.SetIncludeInBuildDelegate` picks one by define and by
  Development Build. Define Constraints do not work for native plugins; see
  [research/platform-levels.md](../research/platform-levels.md). Any other
  combination is built with `build/make_*.sh` and CMake options.
  - **The selector (done 2026-10-18):** `Assets/XLua/Src/Editor/PluginVariants.cs`.
    A variant is the default plugin's path with `xlua-variants/<name>/`
    inserted; per plugin the build takes the variant a scripting define
    `XLUA_PLUGIN_<NAME>` names, else `core` under `XLUA_CORE_ONLY`, else
    `development` in a Development Build, else the preset's variant
    (`minimal` under `XLUA_PRESET_MINIMAL`, since 2026-10-18), else the default
    plugin ([build/README.md](../../build/README.md#plugin-variants)).
    `XLUA_VARIANT` makes the Android and iOS scripts build into that layout:
    `XLUA_VARIANT=minimal XLUA_PRESET=minimal build/make_android_lua55.sh`.
    Not yet run in a Unity build. The repository ships no prebuilt plugins
    ([build/README.md](../../build/README.md)), and the `native-build.yml`
    artifacts do not contain the `minimal` variant yet.
- **Lua sources from Resources** (`xlua.util`, the profiler) move into C#
  string constants under `#if`. A define can then include or exclude them,
  while `require` keeps working. Done: `EmbeddedModules.cs`, found by a
  searcher right after the Resources searcher.
- **Opt-in source plugin:** IL2CPP compiles the C sources into the player.
  This saved 48 KB on Android and 31 KB on iOS, but Unity's compiler flags
  cannot be chosen per file; see [UNITY_SPIKE.md](../../build/UNITY_SPIKE.md).
- **Test matrix:** each preset, plus "legacy with one feature off" for every
  feature. Not every combination.

## Native layers

```text
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
  passes the seed to `lua_newstate`; Lua 5.4 takes it at run time through a
  `luaconf.h.in` hook (done 2026-10-18 with `MemoryLimit`, for
  `LuaEnv(LuaOptions)`; see core-luascript.md, section 8.2).
- **Known limit:** the instruction hook does not fire inside a single C
  function. A pattern such as `("a"):rep(1e5):find(".-.-.-.-b")` can run for a
  long time. This is documented rather than disabling the pattern functions.
  - **Proposed fix:** a small `lstrlib.c` patch that checks the budget in
    `match()`, as Luau does ([research/luau.md](../research/luau.md)).
- **`TimeLimit`:** a wall-clock limit next to `InstructionLimit`, checked in
  the hook. A watchdog thread may arm the hook; `lua_sethook` is
  signal-safe.

## Core preset: `LuaScript`

The full design (API, native ABI, sandbox, tests, milestones) is in
[core-luascript.md](core-luascript.md); where the two differ, that file is
newer.

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
- **Packaging:** one UPM package with two asmdefs, `XLua.Core` (a separate
  `Core/` folder that shares `LuaDLL.cs`) and `XLua`, which references it
  (decided 2026-10-17). A project that defines `XLUA_CORE_ONLY` does not
  compile the `XLua` assembly (asmdef `defineConstraints`), and
  `SetIncludeInBuildDelegate` ships the core native plugin. Without the
  define both APIs work in one player on the full plugin. Details, and the
  size test that checks a core-only player contains no `XLua` code, are in
  [core-luascript.md](core-luascript.md).
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
   - SignatureLoader (done);
   - `template` (done);
   - embedded `xlua.util` and profiler (done);
   - removing `tdr` and `perf/memory` (done);
   - LuaSocket registration (done 2026-10-18: the native library registers
     it in `package.preload`, C# imports nothing).
2. **Native options:**
   - LuaSocket and `mime` (done 2026-10-18), the leak checker and the uint64
     helpers (done 2026-10-18: `XLUA_LEAK_CHECKER`, `XLUA_UINT64`);
   - choosing the standard libraries (done 2026-10-18: `XLUA_LIBS`, at build
     time; `minimal`'s `os` subset and the run-time `LuaOptions.Libraries` are
     not done);
   - the computed export list (done 2026-10-18: the `XLUA_EXPORTS` list
     follows the native options);
   - API 23 linker features and the size flags chosen by the device benchmark
     (done 2026-10-17 for Android and OpenHarmony);
   - plugin variant selection (done 2026-10-18: `PluginVariants.cs`, not yet
     run in a Unity build).
3. **`LuaOptions` limits and `xlua_core.c`.** The split, `MemoryLimit` and
   `HashSeed` are done (2026-10-18); the instruction and time limits are not.
4. **Source-generated bindings**, with a minimum of Unity 2021.3, a 4.3.1
   generator and a reflection fallback when no registration ran (see
   [SOURCEGEN_SPIKE.md](../../build/SOURCEGEN_SPIKE.md)).
5. **Core preset and `LuaScript`.**
6. **Docs:** for every flag, its saving and what it breaks; measured APK and
   IPA size per preset. The native library per preset is measured
   (2026-10-18, [below](#sizes)); a player per preset is not.

## Implemented: legacy and minimal

Done 2026-10-18. `core` stays refused: CMake stops with "XLUA_PRESET=core is not
implemented yet", and the plugin selector refuses `XLUA_PRESET_CORE`.

### Selecting a preset

| Part | legacy (default) | minimal |
|---|---|---|
| C# | nothing | the scripting define `XLUA_PRESET_MINIMAL`, in Player Settings so that the plugin selector sees it |
| Native library | the default plugin | CMake `XLUA_PRESET=minimal` (`-D` or the environment); `XLUA_VARIANT=minimal XLUA_PRESET=minimal build/make_android_lua55.sh` (also `lua53`, `lua54`, `luajit`, and `make_ios_lua54.sh`, `make_ios_lua55.sh`) builds the `minimal` variant |
| Plugin in the player | the default plugin | `PluginVariants.cs` takes the `minimal` variant where a platform has one; a Development Build takes `development` first; without a `minimal` variant the build warns and uses the default plugin |

The minimal C# also runs on the legacy library, which has every function the C#
imports. A project can switch the define first and build the plugins later; until
then it ships the legacy library's size.

### C#: one define per preset

A C# `#define` holds only for the file it is in, so a preset define cannot imply
other defines for the whole project by itself. Two ways were considered:

- **The preset define checked next to each feature define**
  (`#if XLUA_NO_TEMPLATE || XLUA_PRESET_MINIMAL`). Every use site names every
  preset, negated features need `!(XLUA_NO_UTIL || XLUA_PRESET_MINIMAL)`, an
  opt-in inside a preset lengthens each expression, and a forgotten site
  silently keeps the feature.
- **An Editor tool that writes the expanded defines into Player Settings or
  `csc.rsp`.** It works only inside the Unity Editor, so the command-line tools,
  `run_managed.py` and other builds would need their own expansion. A define
  change needs a script recompile before a build sees it, so batch builds that
  set defines and build in one session miss it. Removing the preset must remove
  only the defines the tool added, and Player Settings no longer show which
  defines the user chose.

Implemented instead: **a generated block at the top of each C# file that tests a
feature define** expands the preset define in that file:

```csharp
// <xlua-preset> generated by Tools/presets.py from docs/design/presets.md; do not edit
#if XLUA_PRESET_MINIMAL
#define XLUA_NO_STREAMING_ASSETS_LOADER
...
#if !XLUA_UTIL
#define XLUA_NO_UTIL
#endif
...
#endif
// </xlua-preset>
```

The code below the block keeps testing one symbol per feature, and the expansion
is the same in Unity (Player Settings or `csc.rsp`), the command-line tools,
`run_managed.py`, IDEs and the source generator's compilation.

- **One source:** the table `CSHARP` in `Tools/presets.py`. `--write` updates the
  blocks (15 files, xLua's and the tests'). `--check` fails when a file that
  tests a feature define in `#if` or `#elif` has no current block, when a block
  is no longer needed, and when a `[Conditional]` method depends on a feature
  define without also depending on the preset define. That condition is
  evaluated where the method is called, so `Utils.LockMetatable` carries both.
- **Proof:** `Test/Native/presets_check.py` compiles `XLua.Mini` deterministically
  with `XLUA_PRESET_MINIMAL` and with the thirteen defines, and the two
  assemblies are equal byte for byte. The Unity `Assembly-CSharp` and
  `Assembly-CSharp-Editor` of `editor_compile_check.py` (6000.3.26f1, Android
  IL2CPP player and macOS Editor) were equal too. Legacy's `XLua.Mini` differs
  from main's only by the second `[Conditional]` attribute on the internal
  `Utils.LockMetatable`.
- **Limit:** a project's own code that tests a feature define must also test
  `XLUA_PRESET_MINIMAL`, or copy the block.

### The minimal column as implemented

C#: `XLUA_PRESET_MINIMAL` defines `XLUA_NO_STREAMING_ASSETS_LOADER`,
`XLUA_NO_SIGNATURE_LOADER`, `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` (unless
`XLUA_UTIL`, the positive define for the table's "opt-in"), `XLUA_NO_PROFILER`
(the Editor and Development Builds keep the profiler, as before),
`XLUA_NO_LOAD_ASSEMBLY`, `XLUA_LOCK_METATABLES`, `XLUA_CHECK_OBJECTS`,
`XLUA_CHECK_STRUCT_ACCESSORS`, `XLUA_RESTRICT_GETMETATABLE`,
`XLUA_CHECK_OBJECT_CACHE`, `XLUA_LOCK_XLUA_TABLE` and
`XLUA_RESTRICT_PRIVATE_ACCESS`. `XLUA_MEMORY_DIAGNOSTICS` stays off.

Native, in `build/xlua_presets.cmake`. An option set with `-D`, in the cache or in
the environment wins over the preset:

| Option | legacy | minimal |
|---|---|---|
| `XLUA_LUASOCKET`, `XLUA_MIME` | `ON` | `OFF` |
| `XLUA_LEAK_CHECKER` | `ON` (provisional) | `OFF` |
| `XLUA_UINT64` | `ON` | `ON` (LuaJIT cannot turn it off) |
| `XLUA_MEMORY_SNAPSHOT` | desktop and iOS `ON`; Android, OpenHarmony, UWP, Switch `OFF` | `OFF` |
| `XLUA_LIBS` | `all` | without `io`: `package;coroutine;table;os;string;math;utf8;debug`, LuaJIT `package;table;os;string;math;debug;bit;jit;ffi` |
| `XLUA_EXPORTS` | `all` | `managed` on Android, OpenHarmony and Linux; `all` elsewhere (provisional) |
| `XLUA_HARDEN` | `OFF` (provisional) | `OFF` (provisional) |
| `XLUA_ICF` | `ON` (provisional) | `ON` (provisional) |
| `XLUA_SIZE_FLAGS` | Android and OpenHarmony | the same |

Not implemented, so minimal still has them: the reduced `os` table, the removal of
`package.loadlib`, `dofile` and `loadfile`, the reduced `debug` table, the type
allowlist and an opt-in reflection fallback. The WebGL build (`xlua_webgl.cpp`)
and the Android source plugin do not go through CMake and keep every native part;
the C# define works there.

### Provisional values (owner decisions)

Each is the table's current value or, where the table has none, the conservative
choice:

1. **The three bridge checks in every preset** (bridge-hardening.md, decision A):
   on in minimal, opt-in in legacy.
2. **`XLUA_ICF`:** on in both, as before. The backlog notes that folded functions
   share an address; it saves 0.3 to 0.5 KB of APK.
3. **`mime` in legacy:** on, as before. The backlog asks whether it should default
   to off (+2.6 KB of APK with managed exports, +5.5 KB of iOS code).
4. **The cheap hardening set** (FLAGS.md recommendation): applied in neither.
5. **uint64 on LuaJIT:** always on (CMake refuses `OFF`). Minimal has it on every
   runtime.
6. **Leak checker in legacy:** on, as before.
7. **`XLUA_EXPORTS` for minimal:** the table's "used by C#" where the linker takes
   a version script. A project that loads native Lua modules sets
   `XLUA_EXPORTS=all` or lists the functions in `XLUA_EXPORTS_EXTRA`.
8. **Choices made here, not yet asked:** minimal keeps the whole `os` (the subset
   does not exist), `utf8`, and LuaJIT's `bit`, `jit` and `ffi`; a minimal
   Development Build takes the `development` variant before `minimal`.

### Sizes

Native library, NDK r30, API 23, the release scripts, stripped with
`llvm-strip --strip-unneeded`, "APK" zip deflate level 6
([SIZE.md](../../build/SIZE.md#presets)):

| Lua, ABI | legacy stripped | minimal | legacy APK | minimal |
|---|---:|---:|---:|---:|
| 5.5.1 arm64-v8a | 383,368 | 312,440 (−70,928) | 163,547 | 137,097 (−26,450) |
| 5.5.1 armeabi-v7a | 256,980 | 209,180 (−47,800) | 143,511 | 120,603 (−22,908) |
| LuaJIT arm64-v8a | 674,432 | 609,040 (−65,392) | 333,509 | 308,471 (−25,038) |

- **iOS arm64** (Lua 5.5.1, a probe linked with `-dead_strip`): `__text` 267,996
  to 226,124 (−41,872).
- **C#,** summed from the per-define player measurements (Unity 6000.3.21f1,
  Minimal stripping, release ARM64 APK), not a minimal player: −262,952 bytes
  for `XLUA_NO_SIGNATURE_LOADER`, `XLUA_NO_TEMPLATE`, `XLUA_NO_UTIL` and
  `XLUA_NO_PROFILER`, and −569,044 for `XLUA_NO_STREAMING_ASSETS_LOADER` in a
  project that does not use `UnityWebRequest` otherwise. The hardening defines
  and `XLUA_NO_LOAD_ASSEMBLY` are not measured.
- **Legacy:** the stripped Android library is main's byte for byte once the
  build ID note is removed.

### Tests

- `Test/Native/presets_check.py`: CMake defaults per preset, overrides,
  refusals, the C# blocks and the assembly comparison.
- `run_managed.py --define XLUA_PRESET_MINIMAL` against a minimal library; it
  sets the preset's `XLUA_EXPECT_*` variables. `CasePresetImpliesItsDefines`
  checks the expansion, and the feature and hardening cases check each
  behaviour.
- `plugin_variants_check.py`: the preset in the selection order.
- `editor_compile_check.py`: the `*-minimal*` configurations.
- `sanitize.sh` with `XLUA_PRESET=minimal`.

## Open questions

- **Device benchmark:** answered for Android in
  [FLAGS.md](../../build/FLAGS.md) and applied; iOS speed is not measured.
- **Hotfix:** keep Cecil, try ILPostProcessor, or document HybridCLR? Research
  is in progress.
- **PuerTS:** lessons from its design; research is in progress.
