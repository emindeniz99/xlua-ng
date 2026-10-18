# Presets, size flags and the bridge-less core

Status: **design, agreed in discussion on 2026-10-17. `legacy` and `minimal`
are implemented (2026-10-18, [below](#implemented-legacy-and-minimal)); `core` has
its native library (2026-10-18, [below](#the-core-column-as-implemented-native)),
not yet its C# (`LuaScript`, core-luascript.md milestones 6 to 11).** The owner accepted the values that were
marked *provisional* on 2026-10-18 and added the cheap hardening set to every preset
([owner decisions](#owner-decisions-2026-10-18)).
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
| Type allowlist for `CS.*` and reflection ([bridge-hardening.md](bridge-hardening.md); postponed by the owner on 2026-10-18, full access in every preset for now, [backlog.md](backlog.md)) | ✗ | later | – | |
| Reduced `debug` library (`traceback`, `getinfo`; `XLUA_REDUCED_DEBUG` through `LuaOptions.Libraries`, done; [bridge-hardening.md](bridge-hardening.md)) | ✗ | release builds | ✗ | full |
| SignatureLoader (`XLUA_NO_SIGNATURE_LOADER`, done) | ✓ | ✗ | opt-in | |
| `template` library at run time, Editor always has it (`XLUA_NO_TEMPLATE`, done) | ✓ | ✗ | ✗ | |
| `xlua.util`, embedded in C# instead of Resources (`XLUA_NO_UTIL`, done) | ✓ | opt-in (`XLUA_UTIL`) | ✗ | |
| `perf.profiler`, embedded in C# (`XLUA_NO_PROFILER`, done) | ✓ | ✗ | ✗ | on |
| LuaSocket (CMake `XLUA_LUASOCKET`, done) | ✓ | ✗ | ✗ | on |
| `mime.c` (CMake `XLUA_MIME`, done) | ✓ | ✗ | ✗ | |
| Memory leak checker (CMake `XLUA_LEAK_CHECKER`, done) | ✓ | ✗ | ✗ | on |
| Memory snapshot walk for `LuaEnv.TakeMemorySnapshot()` (CMake `XLUA_MEMORY_SNAPSHOT`, done) | desktop and iOS on; Android, OpenHarmony, UWP, Switch off | ✗; iOS, tvOS and visionOS static libraries keep it (2026-10-18: Development players link it, release players leave it out) | ✗ | on |
| uint64 helpers (i64lib on 5.3+; CMake `XLUA_UINT64`, done; LuaJIT always) | ✓ | ✓ | ✗ | |
| `io` library (CMake `XLUA_LIBS`, done) | ✓ | ✗ | ✗ | |
| `os` library (`XLUA_LIBS` drops it whole; CMake `XLUA_OS_FULL=OFF` keeps the subset, done) | ✓ | `time`, `clock`, `date`, `difftime` | ✗ | |
| `utf8`; LuaJIT's `bit`, `jit` and `ffi` (`XLUA_LIBS`) | ✓ | ✓ | | |
| `debug` library (`XLUA_LIBS`; the bridge needs it; reduced at run time, row above) | ✓ | ✓ | ✗ | |
| `package.loadlib` with the C searchers and `dlopen` (CMake `XLUA_LOADLIB`, done), `dofile`, `loadfile` (CMake `XLUA_DOFILE`, done) | ✓ | ✗ | ✗ | |
| Run-time library mask `LuaOptions.Libraries` (`LuaLibs`, done) | ✓ | ✓ | ✓ (not implemented: `LuaScript`) | |
| Bytecode loader and dumper (`lundump.c`, `ldump.c`, `string.dump`; core: `xlua_core_nobc.c`, done) | ✓ | ✓ | ✗ (the loader for the host's own chunks: opt-in, CMake `XLUA_TRUSTED_BYTECODE`, done) | |
| Lua's compiler (`lparser.c`, `lcode.c`, `llex.c`; CMake `XLUA_BYTECODE_ONLY` leaves it out, done; `LuaEnv` runs on it with [precompiled built-in chunks](#bytecode-options-as-implemented)) | ✓ | ✓ | ✓ | |
| LuaScript's native sandbox (CMake `XLUA_LUASCRIPT`, `xlua_core_sandbox.c`, done) | opt-in | opt-in | ✓ | |
| LuaScript's `buffer` library (CMake `XLUA_BUFFER`, `xlua_core_buffer.c`; scripts get it with `LuaLibs.Buffer`, done) | opt-in, with `XLUA_LUASCRIPT` | opt-in, with `XLUA_LUASCRIPT` | ✓ | |
| Exported native symbols (`XLUA_EXPORTS`, the list follows the options, done) | all Lua API | used by C# + listed extras (`managed` for ELF shared libraries, `all` elsewhere) | used by C# | |
| Identical code folding (CMake `XLUA_ICF`, Android and OpenHarmony) | ✓ | ✓ | ✓ | |
| Cheap hardening (CMake `XLUA_CHEAP_HARDEN`, done 2026-10-18): `-fno-delete-null-pointer-checks` for every C file, `-ftrivial-auto-var-init=zero` for xLua's own files ([FLAGS.md](../../build/FLAGS.md#a-cheaper-hardening-split)) | ✓ | ✓ | ✓ | |
| Full compiler hardening (CMake `XLUA_HARDEN`: also the stack protector, PAC/BTI, zeroed locals everywhere, `_FORTIFY_SOURCE`, CFG) | ✗ | ✗ | ✗ | |
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
  was decided on 2026-10-18 with the other provisional values: they stay opt-in in legacy. Costs, tests and the items left
  for the owner are in [bridge-hardening.md](bridge-hardening.md) and
  [bridge-audit.md](../research/bridge-audit.md). The `debug` row above is ✓
  for minimal because the native library keeps `ldblib.c`; release players get
  the reduced table at run time (`XLUA_REDUCED_DEBUG`, done 2026-10-18).
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
  subset and the removal of `package.loadlib`, `dofile` and `loadfile` are the
  options `XLUA_OS_FULL`, `XLUA_LOADLIB` and `XLUA_DOFILE` (done 2026-10-18,
  [below](#library-parts-and-the-run-time-mask)): guards in the vendored PUC
  Lua sources leave the code out (4,664 bytes stripped, 1,845 of APK on
  arm64-v8a); LuaJIT removes the fields at run time.
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
    artifacts do not contain the `minimal` variant yet. Decided by the owner
    on 2026-10-18: the CI and release artifacts carry the iOS Simulator
    variant (`simulator`) of the Lua 5.x libraries, the UPM package takes any
    variant with its default plugin's import settings and the Editor off,
    and the Android scripts ship stripped libraries with the linked ones in
    `symbols/` for every preset and variant
    ([build/README.md](../../build/README.md#stripped-libraries-and-symbols)).
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
- **`Libraries`** (done 2026-10-18 for `LuaEnv`,
  [below](#library-parts-and-the-run-time-mask)): `LuaLibs` with the bits of
  `xlua_core_open`; `LuaScript` is not implemented.
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

See [research/obfuscators.md](../research/obfuscators.md). Source generator
milestone M8 implemented the name table lookups, the `[Obfuscation]` emission
on injected members and the obfuz adapter for every preset
([source-generator.md](source-generator.md#m8-as-implemented-2026-10-18)); the
plan below is what was decided before it.

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
     time; `minimal`'s `os` subset, `package.loadlib`, `dofile` and `loadfile`
     as `XLUA_OS_FULL`, `XLUA_LOADLIB`, `XLUA_DOFILE`; the run-time
     `LuaOptions.Libraries` for `LuaEnv`, also 2026-10-18);
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

Done 2026-10-18. The plugin selector still refuses `XLUA_PRESET_CORE`; `core`'s
native library is described [below](#the-core-column-as-implemented-native).

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
  with `XLUA_PRESET_MINIMAL` and with the defines it implies (fourteen since
  `XLUA_REDUCED_DEBUG`), and the two
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
`XLUA_CHECK_OBJECT_CACHE`, `XLUA_LOCK_XLUA_TABLE`,
`XLUA_RESTRICT_PRIVATE_ACCESS` and `XLUA_REDUCED_DEBUG` (a release player's
`LuaLibs.Default` reduces `debug` to `traceback` and `getinfo`; the Editor,
Development Builds and the `XLUA_GENERAL` tools keep it whole).
`XLUA_MEMORY_DIAGNOSTICS` stays off.

Native, in `build/xlua_presets.cmake`. An option set with `-D`, in the cache or in
the environment wins over the preset:

| Option | legacy | minimal |
|---|---|---|
| `XLUA_LUASOCKET`, `XLUA_MIME` | `ON` | `OFF` |
| `XLUA_LEAK_CHECKER` | `ON` | `OFF` |
| `XLUA_UINT64` | `ON` | `ON` (LuaJIT cannot turn it off) |
| `XLUA_MEMORY_SNAPSHOT` | desktop and iOS `ON`; Android, OpenHarmony, UWP, Switch `OFF` | `ON` for iOS, tvOS and visionOS (static libraries); `OFF` elsewhere |
| `XLUA_LIBS` | `all` | without `io`: `package;coroutine;table;os;string;math;utf8;debug`, LuaJIT `package;table;os;string;math;debug;bit;jit;ffi` |
| `XLUA_OS_FULL`, `XLUA_LOADLIB`, `XLUA_DOFILE` | `ON` | `OFF`: `os` with `time`, `clock`, `date`, `difftime`; no `package.loadlib`, C searchers or `dlopen`; no `dofile` or `loadfile` |
| `XLUA_EXPORTS` | `all` | `managed` on Android, OpenHarmony and Linux; `all` elsewhere |
| `XLUA_HARDEN` | `OFF` | `OFF` |
| `XLUA_CHEAP_HARDEN` ([below](#owner-decisions-2026-10-18)) | `ON` | `ON` |
| `XLUA_BYTECODE_ONLY` ([below](#bytecode-options-as-implemented)) | `OFF` | `OFF` |
| `XLUA_ICF` | `ON` | `ON` |
| `XLUA_SIZE_FLAGS` | Android and OpenHarmony | the same |

Not implemented, so minimal still has them: the type allowlist and an opt-in
reflection fallback. The WebGL build (`xlua_webgl.cpp`)
and the Android source plugin do not go through CMake and keep every native part;
the C# define works there.

### The core column as implemented (native)

Done 2026-10-18 as milestone 5 of [core-luascript.md](core-luascript.md)
(section 8.4 has the ABI, the sandbox and the tests). `XLUA_PRESET=core`
configures on Lua 5.4 and 5.5 and builds `xlua_core.c` without the bridge's
trampolines, `xlua_core_limits.c`, `xlua_core_sandbox.c` and
`xlua_core_nobc.c`, without `xlua.c`, `i64lib.c`, `memory_leak_checker.c`,
LuaSocket, `linit.c`, `lundump.c`, `ldump.c` or libdl:

| Option | core |
|---|---|
| `XLUA_LUASOCKET`, `XLUA_MIME`, `XLUA_LEAK_CHECKER`, `XLUA_UINT64`, `XLUA_MEMORY_SNAPSHOT` | `OFF`; CMake refuses `ON` |
| `XLUA_LIBS` | `coroutine;table;string;math;utf8` (`LuaLibs.Safe`); a subset works; `io`, `os`, `debug` and `package` are refused |
| `XLUA_EXPORTS` | `managed` on Android, OpenHarmony and Linux, from `build/xlua_core_exports.txt` (20 functions); `all` elsewhere |
| `XLUA_LUASCRIPT` | always `ON` |
| `XLUA_BUFFER` | `ON` (milestone 10); `OFF` leaves the buffer library out |
| `XLUA_HARDEN`, `XLUA_CHEAP_HARDEN`, `XLUA_ICF`, `XLUA_SIZE_FLAGS` | as in legacy |
| `XLUA_TRUSTED_BYTECODE` ([below](#bytecode-options-as-implemented)) | `OFF`; `ON` adds `lundump.c` back |
| `XLUA_BYTECODE_ONLY` | refused (the sandbox loads text) |

LuaJIT and Lua 5.3 stop the configuration. `XLUA_LUASCRIPT=ON` adds the same
sandbox to `legacy` and `minimal`; it stays off there (accepted by the owner
on 2026-10-18 with the other provisional values), so their libraries stay
byte for byte as before.

Native library, NDK r30, API 23, release build, stripped, "APK" zip deflate 6
([SIZE.md](../../build/SIZE.md#the-core-preset)):

| Lua, ABI | core stripped | legacy | core APK | legacy |
|---|---:|---:|---:|---:|
| 5.5.1 arm64-v8a | 253,792 | 383,672 | 115,246 | 163,690 |
| 5.5.1 armeabi-v7a | 171,456 | 257,156 | 101,693 | 143,599 |
| 5.4.9 arm64-v8a | 239,096 | 367,136 | 107,872 | 155,227 |
| 5.4.9 armeabi-v7a | 163,408 | 247,484 | 96,463 | 137,017 |

Not done for core: the C# (`Assets/XLua/Core`), the plugin variant in
`PluginVariants.cs` (it refuses `XLUA_PRESET_CORE`), the iOS and other
platform scripts' core variants (only `XLUA_VARIANT=core XLUA_PRESET=core
build/make_android_lua55.sh` and `lua54` were run), and a player size.

### Bytecode options as implemented

Done 2026-10-18 for the decision of 2026-10-17 ([backlog](backlog.md)).
Both CMake options are off in every preset and read from the environment as
well; [build/README.md](../../build/README.md#bytecode-options) has the details.

- **`XLUA_BYTECODE_ONLY`** (legacy and minimal, PUC Lua 5.3 to 5.5): leaves out
  `lparser.c`, `lcode.c` and `llex.c`; `build/xlua_noparser.c` refuses text
  chunks with "text chunks are not supported in this build (bytecode only)".
  `lua_setfenv` takes the fresh `_ENV` from a reloaded dump of the function
  instead of an empty text chunk. Android, Lua 5.5.1: -56.7 KB stripped and
  -27.3 KB of APK on arm64-v8a, -37.2 KB and -23.6 KB on armeabi-v7a, about the same
  for legacy and minimal ([SIZE.md](../../build/SIZE.md#bytecode-options)).
  Refused on LuaJIT (its parser is inside `libluajit.a`) and with LuaScript's
  sandbox (core, `XLUA_LUASCRIPT=ON`), which loads text.
- **`XLUA_TRUSTED_BYTECODE`** (core, or a bridge preset with
  `XLUA_LUASCRIPT=ON`): the new export `xlua_core_run_mode` (since milestone 6
  the `mode` field of `xlua_core_call`) takes
  `XLUA_CORE_MODE_TEXT`, `XLUA_CORE_MODE_BINARY` or both; without the option
  the binary mode returns `XLUA_ERRCONFIG`. The core preset then contains
  `lundump.c` (+6.5 KB stripped, +2.9 KB of APK on 5.5.1 arm64-v8a). Scripts'
  `load` and `require` stay text only, and `string.dump` stays out. The export
  is in every build (+0.5 KB in the default core library), so LuaScript's C#
  can import it without knowing how the library was built.
- **`LUA_COMPAT_APIINTCASTS`:** removed in every preset. CMake defined it only
  for LuaSocket's files, which use none of its macros; the Android libraries are
  byte for byte the same. No preset defines another `LUA_COMPAT_*` option.

**`LuaEnv` on a bytecode-only library** (done 2026-10-18; owner decision of
the same day, option 2 of the three that were proposed: the native library
carries the chunks). Its constructor runs Lua source: `init_xlua`
(`LuaEnv.cs`), the `OpenLibraries` chunk of `LuaOptions.Libraries`, the pairs
helper of `ObjectTranslator.CreateEnumerablePairs`, and the embedded modules
`xlua.util`, `perf.profiler` and `xlua.memory`. As decided:

- **The chunks are in the native library**, only with `XLUA_BYTECODE_ONLY`,
  only for that build's Lua version: no C# size and no unneeded formats.
- **No generated files are committed.** The build compiles the chunks:
  `build/xlua_chunkc.c` (a `luac` for this purpose, built from the same vendored
  Lua) writes `xlua_builtin_chunks.h` into the build directory. A native build
  makes it a target of the same project; a cross build configures and builds
  `build/cmake/xlua_chunkc` with the host's compiler at configure time. The
  separate configure at configure time was chosen over ExternalProject because
  an ExternalProject step would run inside Xcode's build environment
  (`SDKROOT` of the iPhone SDK) and compile the host program for the target.
- **One source, in C#.** The C# text stays where it was, as verbatim strings
  between `// <xlua-chunk name=... chunkname=...>` and `// </xlua-chunk>`
  markers; `xlua_chunkc` finds them itself. The reverse (`.lua` files that C#
  reads) would need either a build step in Unity or committed generated C#,
  both excluded; extraction in CMake script was possible too but slower and
  less exact than in C (escapes, ASCII check, one literal per marker). Python
  is not needed by the CMake build and was not added. `init_xlua` takes its
  three define-dependent values as arguments, so each chunk has one form.
- **Same source, checked:** `LuaEnv.LoadBuiltinChunk` passes a hash (FNV-1a of
  the text without carriage returns) to the library's loader, which refuses a
  chunk compiled from other text. `bytecode_only.py` finds and hashes the
  markers independently of `xlua_chunkc` and requires each chunk.
- **Export list unchanged:** the loader is a registry entry
  (`xlua.builtin_chunk`) set by `luaopen_xlua`, not a new export. A new export
  only in bytecode-only builds would break the link of every other iOS player
  (IL2CPP links each `__Internal` import), and one in every build would change
  the default libraries; the legacy Android libraries stay byte for byte the
  same once the build ID is removed.
- **Lua 5.3 on 32-bit targets** (bytecode with `sizeof(size_t)`): a cross build
  is refused unless `LUAC_COMPATIBLE_FORMAT=ON`, the simplest correct choice; a
  32-bit host program cannot be built on macOS. Native 32-bit builds compile the
  program for the target and are not affected. The header records the sizes,
  and `xlua.c` stops on a mismatch.
- **The opt-in modules:** a CMake option per module, `XLUA_BYTECODE_UTIL` and
  `XLUA_BYTECODE_PROFILER` (`ON` in legacy, `OFF` in minimal, as the presets'
  C# defines have them) and `XLUA_BYTECODE_MEMORY` (follows
  `XLUA_MEMORY_SNAPSHOT`). A module the library leaves out fails in `require`
  with "this bytecode-only library has no precompiled chunk". The template
  engine makes Lua text at run time and does not work on such a library.
- **Cost** (Android, Lua 5.5.1, [SIZE.md](../../build/SIZE.md#bytecode-options)):
  +15.3 KB stripped and +7.9 KB of APK on arm64-v8a in legacy (all chunks with
  debug information; the saving of the option is 41.4 KB and 19.4 KB), +6.6 KB
  and +3.7 KB in minimal (the three setup chunks).

Further questions: whether `require` through LuaScript's resolver should take
the host's precompiled modules with `XLUA_TRUSTED_BYTECODE` (the resolver
returns text only now; `xlua_core_source` could get a mode field), whether
milestone 6's `xlua_core_call` takes the mode as a field (it does, and
`xlua_core_run_mode` is gone; core-luascript.md, question 21), and whether core
should allow `XLUA_BYTECODE_ONLY` together with `XLUA_TRUSTED_BYTECODE`, a core
library that runs only the host's precompiled scripts (refused now).

### Owner decisions (2026-10-18)

These were the provisional values: the table's value or, where the table had
none, the conservative choice. The owner accepted all of them on 2026-10-18 as
they are, with one change, item 4:

1. **The three bridge checks in every preset** (bridge-hardening.md, decision A):
   on in minimal, opt-in in legacy.
2. **`XLUA_ICF`:** on in both, as before. The backlog notes that folded functions
   share an address; it saves 0.3 to 0.5 KB of APK.
3. **`mime` in legacy:** on, as before (+2.6 KB of APK with managed exports,
   +5.5 KB of iOS code; the backlog had asked whether it should default to off).
4. **The cheap hardening set** (FLAGS.md recommendation): on in every preset,
   legacy, minimal and core, as CMake `XLUA_CHEAP_HARDEN` (done 2026-10-18; read
   from the environment like the other options): `-fno-delete-null-pointer-checks`
   for every C file of the library and `-ftrivial-auto-var-init=zero` for xLua's own
   files (`XLUA_SOURCES`: `xlua.c`, `i64lib.c`, `memory_leak_checker.c`, `xlua_libs.c`,
   `xlua_noparser.c` and the `xlua_core*.c` files), where the compiler takes them.
   MSVC has neither flag and gets nothing; LuaJIT's `libluajit.a` (its own
   Makefile), the host tool `xlua_chunkc` and the WebGL and Android source
   plugins (compiled by Unity) are not covered. Full `XLUA_HARDEN` stays off.
   Cost on Android, Lua 5.5.1: see [Cheap hardening](#cheap-hardening-as-implemented) below.
5. **uint64 on LuaJIT:** always on (CMake refuses `OFF`). Minimal has it on every
   runtime.
6. **Leak checker in legacy:** on, as before.
7. **`XLUA_EXPORTS` for minimal:** the table's "used by C#" where the linker takes
   a version script. A project that loads native Lua modules sets
   `XLUA_EXPORTS=all` or lists the functions in `XLUA_EXPORTS_EXTRA`.
8. **Choices made here without a question before:** minimal keeps `utf8`, and LuaJIT's
   `bit`, `jit` and `ffi`; a minimal Development Build takes the `development`
   variant before `minimal`.
9. **Library parts and the run-time mask** ([below](#library-parts-and-the-run-time-mask)):
   the `os` subset and the removal of `package.loadlib`, `dofile` and `loadfile`
   are native (a build option, the code is not linked) rather than C#; the
   reduced `debug` is C# (`LuaOptions.Libraries`), so the native library keeps
   `ldblib.c`; `LuaEnv` ignores a library the build lacks instead of refusing
   it as `xlua_core_open` does; the Lua file searcher (`package.path`) stays in
   minimal.
10. **The memory snapshot in minimal's Apple static libraries** (iOS, tvOS,
    visionOS): kept (decided 2026-10-18), so that a Development player links;
    release players leave the walk out at link time.
11. **LuaScript's sandbox in legacy and minimal:** opt-in (`XLUA_LUASCRIPT=ON`)
    for now; the owner has not decided yet (core-luascript.md, section 8.4,
    question 1: on would cost every legacy and minimal player about 7-15 KB of
    native code, 3-6 KB of APK, whether it uses LuaScript or not). Its
    `buffer` library comes only with it, opt-in as well (`XLUA_BUFFER=ON`;
    question 22 of core-luascript.md is still open).

### Cheap hardening as implemented

Done 2026-10-18 (item 4). Native library, NDK r30, API 23, the release build,
stripped, Info-ZIP deflate 6, the same tree with `XLUA_CHEAP_HARDEN=OFF` and
`ON` ([SIZE.md](../../build/SIZE.md#cheap-hardening),
[FLAGS.md](../../build/FLAGS.md#the-cheap-set-as-applied-2026-10-18)):

| Preset, Lua, ABI | stripped, off | on | APK, off | on |
|---|---:|---:|---:|---:|
| legacy 5.5.1 arm64-v8a | 384,040 | 384,392 (+352) | 162,636 | 162,763 (+127) |
| legacy 5.5.1 armeabi-v7a | 257,396 | 257,748 (+352) | 143,135 | 143,505 (+370) |
| minimal 5.5.1 arm64-v8a | 308,464 | 308,784 (+320) | 135,291 | 135,388 (+97) |
| minimal 5.5.1 armeabi-v7a | 206,744 | 207,064 (+320) | 119,020 | 119,329 (+309) |
| core 5.5.1 arm64-v8a | 272,088 | 272,584 (+496) | 123,316 | 123,616 (+300) |
| core 5.5.1 armeabi-v7a | 183,704 | 184,216 (+512) | 109,006 | 109,464 (+458) |
| core 5.4.9 arm64-v8a | 257,360 | 257,808 (+448) | 116,015 | 116,196 (+181) |
| core 5.4.9 armeabi-v7a | 175,688 | 176,120 (+432) | 103,725 | 104,023 (+298) |

With the option off, the legacy library equals main's byte for byte once the
build ID note is removed. Tests: `presets_check.py` (the default in every
preset, `-D` and environment overrides, and from `compile_commands.json` which
files get which flag), and the suites listed in FLAGS.md on the libraries with
the option on.

### Sizes

Native library, NDK r30, API 23, the release scripts, stripped with
`llvm-strip --strip-unneeded`, "APK" zip deflate level 6
([SIZE.md](../../build/SIZE.md#presets)):

| Lua, ABI | legacy stripped | minimal | legacy APK | minimal |
|---|---:|---:|---:|---:|
| 5.5.1 arm64-v8a | 383,672 | 308,080 (−75,592) | 163,689 | 135,426 (−28,263) |
| 5.5.1 armeabi-v7a | 257,156 | 206,504 (−50,652) | 143,600 | 119,262 (−24,338) |
| LuaJIT arm64-v8a | 674,432 | 609,600 (−64,832) | 333,512 | 308,694 (−24,818) |

Measured again 2026-10-18 after the [library parts](#library-parts-and-the-run-time-mask)
(main 35165d02 against this branch, the same scripts); the first minimal
measurement was 312,440 and 209,180 bytes stripped, 609,040 for LuaJIT.

- **iOS arm64** (Lua 5.5.1, a probe linked with `-dead_strip`): `__text` 267,996
  to 226,124 (−41,872).
- **Players** (2026-10-18, Unity 6000.3.21f1, release IL2CPP ARM64, Minimal
  stripping, the size probe; the plugin selector chose the library;
  [SIZE.md](../../build/SIZE.md#players)):

  | Player | baseline | legacy | minimal | minimal − legacy |
  |---|---:|---:|---:|---:|
  | Android APK | 12,279,060 | +1,410,978 | +440,458 | −970,520 (−68.8%) |
  | iOS `.app` zip (unsigned, stripped) | 6,628,030 | +765,523 | +394,233 | −371,290 (−48.5%) |

  Most of it is C#. On Android 539,626 bytes are `libunity.so`: without
  xLua's `UnityWebRequest` use (`XLUA_NO_STREAMING_ASSETS_LOADER`) engine code
  stripping drops the module, which a project that downloads anything keeps.
  `libxlua.so` gives 26,415 and `libxlua.a` 46,569 live bytes.
- **Legacy:** the stripped Android library is main's byte for byte once the
  build ID note is removed.

### Library parts and the run-time mask

Done 2026-10-18; its choices are item 9 of the [owner decisions](#owner-decisions-2026-10-18), accepted on 2026-10-18.

- **Native, three CMake options** (`build/CMakeLists.txt`,
  [build/README.md](../../build/README.md#parts-of-os-package-and-the-base-library)),
  `ON` in legacy and core, `OFF` in minimal: `XLUA_OS_FULL` (`os` keeps
  `time`, `clock`, `date`, `difftime`), `XLUA_LOADLIB` (no `package.loadlib`,
  C searchers or `dlopen`), `XLUA_DOFILE` (no `dofile`, `loadfile`). On PUC Lua
  `#if` guards in the vendored `loslib.c`, `loadlib.c` and `lbaselib.c`
  ([LUA_PATCHES.md](../../build/LUA_PATCHES.md)) leave the code out; LuaJIT's
  `libluajit.a` is not patched, so `xlua_libs.c` removes the fields at run time
  there. `require` keeps the preload and Lua searchers and xLua's loaders.
- **C#, `LuaOptions.Libraries`** (`LuaLibs`, `Assets/XLua/Src/LuaOptions.cs`):
  the bits of `xlua_core_open` plus `Bit`, `Jit`, `Ffi`. `LuaEnv` opens what the
  native library contains, then removes what the mask leaves out; a library
  the build lacks stays absent. Because the bridge needs them, `debug` keeps
  `traceback` and `getinfo` without `Debug`, and `require` keeps working
  without `Package`. Done in C# so that the legacy native library stays byte for
  byte the same; it removes no code.
- **The reduced `debug`** (bridge-hardening.md, decision B, option 1):
  `XLUA_REDUCED_DEBUG`, which the minimal preset implies, makes
  `LuaLibs.Default` leave out `Debug` in release players. What the bridge uses
  keeps working: `errorfunc` reads `debug.traceback`; `init_xlua`'s `setfenv`
  emulation captures `getinfo`, `getupvalue` and `upvaluejoin` at start-up;
  `xlua.util` takes the `|` operator when `debug.getmetatable` is missing (Lua
  5.3+); hotfix needs nothing from `debug`, `perf.profiler` only `getinfo` (and
  `os.clock`). Not working with the reduced table: `util.state`,
  `util.print_func_ref_by_csharp`, `util.createdelegate` on LuaJIT, debuggers.
- **Sizes**, Android, Lua 5.5.1, minimal, stripped / APK
  ([SIZE.md](../../build/SIZE.md#parts-of-os-package-and-the-base-library)):
  arm64-v8a −4,664 / −1,845 (`os` −2,008, `loadlib` −2,016, `dofile` −656);
  armeabi-v7a −2,852 / −1,454. LuaJIT minimal +560 bytes (the run-time removal).
  The legacy libraries are main's byte for byte.

### Tests

- `Test/Native/presets_check.py`: CMake defaults per preset, overrides,
  refusals, the C# blocks and the assembly comparison.
- `native_regression.py` (`library_parts`), `check_exports.py` and
  `check_android_elf.py` (no `dlopen` import without `XLUA_LOADLIB`), and the
  managed cases `CaseLibraryPartsFollowTheBuild`, `CaseLuaOptionsLibraries` and
  `CaseDebugLibraryFollowsTheDefine`.
- `run_managed.py --define XLUA_PRESET_MINIMAL` against a minimal library; it
  sets the preset's `XLUA_EXPECT_*` variables. `CasePresetImpliesItsDefines`
  checks the expansion, and the feature and hardening cases check each
  behaviour.
- `plugin_variants_check.py`: the preset in the selection order.
- `editor_compile_check.py`: the `*-minimal*` configurations.
- `sanitize.sh` with `XLUA_PRESET=minimal`.
- Unity players (`Test/MobileSmoke/prepare.py --android-minimal-plugin` and
  the others, `size_report.py --configs minimal`): the selector picks the
  library in a real build, and the smoke checks which one the player loaded.
  Passed 2026-10-18 on Android (an emulator and a phone) and in iOS size
  builds; `Test/MobileSmoke/README.md` has the runs.

## Open questions

- **Device benchmark:** answered for Android in
  [FLAGS.md](../../build/FLAGS.md) and applied; iOS speed is not measured.
- **Hotfix:** keep Cecil, try ILPostProcessor, or document HybridCLR? Research
  is in progress.
- **PuerTS:** lessons from its design; research is in progress.
