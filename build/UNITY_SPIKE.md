# Unity spike: LINQ, generic sharing, source plugins

Measurements from 2026-10-17 for three questions about the Unity side of xlua-ng:
what LINQ costs at run time and in the player, what IL2CPP full generic sharing
does to xLua's code, and whether Unity can compile the Lua sources into the player
instead of shipping a prebuilt library. The players are the `size_report.py` empty
projects (see "Player size" in `Test/README.md`): Unity 6000.3.21f1, IL2CPP, ARM64,
release build, managed stripping Minimal, Lua 5.5.1, no generated code.

"HEAD" is commit 9052b82, before this branch. "Rewrite" is this branch, where the
runtime C# has no LINQ. Every number is measured unless a cell says *estimate*.
Byte deltas read "compressed in the APK / uncompressed".

Prototypes on this branch, in separate commits so they can be taken or left:

- the LINQ rewrite in `Assets/XLua/Src` (`Utils.cs`, `ObjectTranslator.cs`,
  `TypeExtensions.cs`, `TemplateEngine.cs`);
- the opt-in define `XLUA_ANDROID_SOURCE_PLUGIN` in `LuaDLL.cs`. Without it nothing
  changes.

Tools, all in `Test/MobileSmoke`: new variant flags and configurations in
`size_report.py`, `size_compare.py`, `srcplugin_report.py`,
`plugin_select_report.py`, `alloc_probe.py` with `AllocProbe.cs` and
`AllocProbeGeneral.cs`, and `PluginVariantSelector.cs`. The work directory is
`/Volumes/T7Data/ext-ai-projects/xlua-ng/unity-size-q`.

## Q1: LINQ in the runtime

### Where it was

| file | LINQ in a player | what replaced it |
| --- | --- | --- |
| `Utils.cs` | `GetAllTypes` (`Where`), indexer filter (`Where.ToArray`), extension method map (`SelectMany`, `GroupBy`, `ToDictionary`), event name check (`Any`), non-public method list (`Join`), lazy method wrap (`Concat`, `Where`, `Cast`, `ToArray`), `IsUnsupportedType` and `HasUnsupportedType` (`Any`) | loops into `List<T>` and arrays; `Join` keeps its duplicates and order |
| `ObjectTranslator.cs` | generic delegate creator (`Where`, `OrderBy`, `Select`, `Concat`, `ToArray`), delegate method search (`Where.ToArray`) | loops; a stable insertion sort for the five `Action`/`Func` overloads |
| `TypeExtensions.cs` | `GetFriendlyName` (`Select.ToArray`) | a `string[]` loop |
| `TemplateEngine.cs` | `Parser.Parse` (`Cast`, `Select`, `Concat`, `OrderBy`, `ToList`) | captures into one list, `List.Sort` on (position, order of the group), the same order as the stable `OrderBy` |
| `MethodWarpsCache.cs` | none: its `ToArray` calls are `List<T>.ToArray` | unchanged |
| `CodeEmit.cs`, `ObjectTranslator.initCSharpCallLua` | Editor and `XLUA_GENERAL` only (`XLUA_CODE_EMIT`) | unchanged; `ObjectTranslator.cs` imports `System.Linq` under `XLUA_CODE_EMIT` only |

The UWP (.NET scripting backend) branch of `GetAllTypes` was rewritten too but not
compiled: nothing here builds UWP.

### Player size

Android ARM64 APK, Lua 5.5.1, the same probe in every project.

| player | APK | delta | `libil2cpp.so` | `global-metadata.dat` |
| --- | --- | --- | --- | --- |
| baseline (no xLua) | 12,279,048 | | | |
| xlua-ng HEAD | 13,676,194 | +1,397,146 | +559,508 / +1,912,024 | +116,360 / +329,240 |
| xlua-ng rewrite | 13,622,382 | +1,343,334 | +512,178 / +1,694,536 | +109,891 / +310,620 |
| **rewrite against HEAD** | | **-53,812** | **-47,330 / -217,488** | **-6,469 / -18,620** |

Where the 217,488 bytes of `libil2cpp.so` went (symbols of `libil2cpp.dbg.so`,
`size_compare.py`):

| part | bytes |
| --- | --- |
| class library: LINQ | -57,016 |
| class library: other generic instances | -9,520 |
| class library: collections | -1,984 |
| class library: other (`System.Core`, streams) | -88 |
| xLua's own C# (`Utils` -1,624, `ObjectTranslator` -892, `TemplateEngine` -816, `TypeExtensions` -192) | -3,516 |
| tables, relocations, unwind data | -54,686 |
| not in a symbol (the rest of the stripped file) | -90,678 |

xLua no longer adds any System.Linq code: the rewrite has the same
112 LINQ symbols (20,872 bytes) as the empty baseline, which UnityEngine modules
pull in. `System.Core.dll` stays (38,912 bytes stripped, baseline 17,408, HEAD
56,320) because `HashSet<T>` lives there.

### Allocations and time

The probe ran on two runtimes, each run in a new process, and was built for a third:

- **Mono (SGen)**: `AllocProbeGeneral.cs` with `XLUA_GENERAL` on Mono 6.14.1.
  `GC.GetAllocatedBytesForCurrentThread` counts every byte, so these are the exact
  numbers. Class library types stand in for UnityEngine ones.
- **Unity Editor (Mono, Boehm)**: `AllocProbe.cs`. Unity's Mono returns 0 from
  `GetAllocatedBytesForCurrentThread`, so the probe reads `GC.GetTotalMemory`
  around each step and drops a row when a collection ran during it.
- **IL2CPP: not run.** `size_report.py` built two release IL2CPP iOS Simulator
  players with the probe (`ios` variants `ng-5.5+at-9052b82+alloc+srcplugin+sim`
  and `ng-5.5+alloc+srcplugin+sim`, arm64, the Lua sources compiled by Xcode),
  but the root disk had 25.5 to 26 GB free and a simulator device needs about
  0.5 GB of it, so the watchdog stopped the run before the first launch.
  `alloc_probe.py sim --app .../XluaNgSmoke.app` runs them. IL2CPP does not
  implement `GetAllocatedBytesForCurrentThread`; the probe turns the GC off for
  each step there and reads `GC.GetTotalMemory`. *Estimate*: the same byte deltas
  as on Mono, since IL2CPP runs the same class library code; IL2CPP times are
  likely lower for both.

The probe sets `DelegateBridge.Gen_Flag` in the Editor and on Mono, so xLua wraps
types and builds delegates by reflection as in a player instead of emitting code.

Mono (SGen), bytes allocated, median of 5 runs (every run gave the same bytes):

| step | HEAD | rewrite | delta | ms HEAD | ms rewrite |
| --- | --- | --- | --- | --- | --- |
| `new LuaEnv()`, first in the process | 2,791,152 | 2,780,024 | -11,128 | 57.2 | 53.2 |
| first type wrapped (`StringBuilder`; scans every type for extension methods) | 157,112 | 151,544 | -5,568 | 1.40 | 1.00 |
| `List<int>`: first access, indexer | 126,392 | 123,672 | -2,720 | 1.35 | 1.23 |
| `Version`: first access | 50,120 | 49,000 | -1,120 | 0.37 | 0.34 |
| `Target` (overloads, indexer, extension methods, event): first access | 31,784 | 30,816 | -968 | 0.67 | 0.53 |
| `Target`: overloads, indexer, extension methods, field, property: first calls | 7,376 | 7,376 | 0 | 1.37 | 1.35 |
| `Target`: event, first Lua function to `Action<string>` | 16,168 | 13,064 | -3,104 | 3.61 | 1.64 |
| first `Func<string, string>` from Lua | 2,512 | 1,864 | -648 | 1.05 | 0.10 |
| every step again in the same `LuaEnv` | 4,136 | 4,136 | 0 | 0.21 | 0.18 |
| hot loop, 1,000 iterations of member calls | 515,312 | 515,312 | 0 | 6.34 | 6.29 |
| `new LuaEnv()`, warm | 632,736 | 627,992 | -4,744 | 5.04 | 4.99 |
| the same types in a second `LuaEnv` (sum of the first-access rows) | 306,904 | 297,312 | -9,592 | 0.80 | 0.77 |

Unity Editor (Mono, Boehm), 5 runs. `GC.GetTotalMemory` moves in 4 KB steps here,
so a delta under 4 KB does not show and the numbers carry ±4 KB. "GC" marks a step
where a collection ran in every run:

| step | HEAD | rewrite | delta | ms HEAD | ms rewrite |
| --- | --- | --- | --- | --- | --- |
| `new LuaEnv()`, first in the process | GC | GC | | 144 | 139 |
| `GameObject`: first access (first type; extension method scan) | 266,240 | 253,952 | -12,288 | 1.93 | 1.91 |
| `Transform`: first access | 380,928 | 368,640 | -12,288 | 1.66 | 1.60 |
| `Vector3`: first access, constructor | 649,216 | 620,544 | -28,672 | 3.40 | 3.35 |
| `Target`: first access | 45,056 | 49,152 | +4,096 | 0.99 | 0.96 |
| first calls: constructor, overloads, indexer, extension methods, field, property | 0 | 0 | 0 | 1.85 | 1.91 |
| `Target`: event, first Lua function to `Action<string>` | 20,480 | 8,192 | -12,288 | 2.58 | 2.34 |
| first `Func<string, string>` from Lua | 4,096 | 4,096 | 0 | 0.88 | 0.13 |
| every step again in the same `LuaEnv` | 0 | 0 | 0 | 0.21 | 0.22 |
| hot loop, 1,000 iterations | 499,712 | 499,712 | 0 | 8.06 | 9.92 |
| `new LuaEnv()`, warm | 1,413,120 | 1,417,216 | +4,096 | 27.6 | 29.0 |
| `GameObject` in a second `LuaEnv` | 163,840 | 143,360 | -20,480 | 0.73 | 0.69 |
| `Transform` in a second `LuaEnv` | 278,528 | 262,144 | -16,384 | 1.01 | 0.92 |

The hot loop's 8.06 against 9.92 ms is noise: the Mono run, with exact counts,
shows the same bytes and 6.34 against 6.29 ms, and the code it runs did not change.


LINQ costs allocations only where xLua builds a wrapper: the first access to a
type (2 to 4% of what that step allocates), the first delegate of a type, and
`new LuaEnv()`. Calls through a wrapper that exists allocate the same before and
after, and so does the hot loop. The largest single gains are the first Lua
function turned into a delegate (`getDelegate` filtered `GetMethods` with
`Where.ToArray`, and `getCreatorUsingGeneric` built type arguments with
`Select.Concat.ToArray`): 0.65 to 3.1 KB and 1 to 2 ms on its first call.

### Tests on the rewrite

| suite | result |
| --- | --- |
| Unity editor tests (`Test/UnityMatrix/prepare.py --editor-tools --fast-enter-play-mode`, `editor_tests.py run`, 6000.3.21f1, macOS ARM64) | generated code 29/29 passed, cleared (reflection and emit) 29/29 passed; LuaLS good.lua 0 diagnostics, bad.lua the 10 expected |
| General Mono suite (`run_managed.py --gen-code --gen-code-minimize`) | 323 tests in each of two modes, in all three builds: reflection, generated, generated minimized |
| `TemplateEngine.Parser.Parse` on the 11 code generator templates, HEAD against rewrite | identical chunk lists (3,101 lines) |

## Q2: IL2CPP generic sharing

Player Settings > IL2CPP Code Generation: "Faster runtime" is
`Il2CppCodeGeneration.OptimizeSpeed` (the default), "Faster (smaller) builds" is
`OptimizeSize`. Both with xlua-ng HEAD.

| | Faster runtime | Faster (smaller) builds | difference |
| --- | --- | --- | --- |
| baseline APK | 12,279,048 | 11,903,216 | -375,832 |
| xlua-ng APK | 13,676,194 | 13,183,950 | -492,244 |
| **what xLua adds: APK** | **+1,397,146** | **+1,280,734** | **-116,412** |
| what xLua adds: `libil2cpp.so` | +559,508 / +1,912,024 | +443,421 / +1,402,264 | -116,087 / -509,760 |
| what xLua adds: metadata | +116,360 / +329,240 | +116,035 / +328,480 | -325 / -760 |

What xLua adds to `libil2cpp.so`, by part:

| part | Faster runtime | Faster (smaller) builds |
| --- | --- | --- |
| xLua's own C# (incl. generic instances of xLua's types) | 239,248 | 232,420 |
| class library: collections and arrays | 208,376 | 33,152 |
| class library: LINQ | 57,016 | 35,360 |
| class library: delegates | 7,648 | 292 |
| class library: other generic instances | 34,728 | 15,116 |
| class library: cryptography, regex, streams, `Uri`, other | 409,864 | 383,264 |
| tables and data | 325,803 | 233,068 |

How IL2CPP shares generics: one compiled body serves every instance whose type
arguments are reference types, while each value-type instance gets its own body
unless full generic sharing ("Faster (smaller) builds") is on, which compiles one
body for every type argument and passes values through `Il2CppFullySharedGenericAny`.

The generated C++ shows it. In the xlua-ng player with "Faster runtime",
`Dictionary<TKey,TValue>.TryGetValue` has 12 compiled bodies: one for
`<RuntimeObject*, RuntimeObject*>` shared by every reference-type pair, others for
value-type layouts such as `<MethodKey, RuntimeObject*>` (xLua's
`Utils.MethodKey` struct), `<RuntimeObject*, int32_t>`, `<int32_t, …>`,
`<intptr_t, …>`, `<Guid, …>`, `<ValueTuple_2, …>`, and one fully shared body that
Unity 6 always keeps for instances created at run time. With "Faster (smaller)
builds" only the fully shared body is left (2 in the generated C++, 1 in the
linked binary). The same holds for `List<T>.Add` (9 bodies to 1), `List<T>.ToArray`
(9 to 1) and the array sort helpers (50 to 5). The setting changes almost nothing
in xLua's own code (-6,828 bytes): xLua's cost is in the class library generics it
instantiates.

Not measured: the run time cost. Fully shared code handles value types through
pointers and looks up their size at run time, and Unity's Player Settings
documentation warns that the option can make generic code slower; in xLua that is
value-type generics such as `Dictionary<MethodKey, …>`, `Dictionary<Type, int>` and
`List<int>`. The setting is project-wide, so a game decides it for all its code.

## Q3: Lua sources compiled with the player

### Android

`+srcplugin` copies the Lua 5.5.1 sources, `xlua.c`, `i64lib.c`,
`memory_leak_checker.c` and LuaSocket (`usocket.c`, not `wsocket.c`) as separate
files into one directory, `Assets/Plugins/Android/xlua`. Quoted `#include`s find
their headers next to the file, so no include path is needed. `luaconf.h` gets the
definitions CMake passes with `-D` at its top (`LUA_USE_LINUX`,
`LUA_COMPAT_APIINTCASTS`). The project defines `XLUA_ANDROID_SOURCE_PLUGIN`, which
makes `LuaDLL.LUADLL` `"__Internal"` on Android.

The player builds. IL2CPP copies every plugin file, headers included, flat into
`il2cppOutput/cpp`, compiles each `.c` with `-x c` and links it into
`libil2cpp.so`. The generated P/Invoke wrappers then call `lua_gettop` and the rest
directly, without the lazy `dlsym` lookup of `libxlua.so`. The APK has no
`libxlua.so`. Not run on a device: no Android device or emulator was used here.

| player | APK | `libxlua.so` | `libil2cpp.so` |
| --- | --- | --- | --- |
| prebuilt `libxlua.so` (rewrite) | 13,622,382 | 169,426 / 418,272 | |
| sources in the player (`+srcplugin`) | 13,574,256 | - | +121,408 / +272,344 |
| **difference** | **-48,126** | -169,426 / -418,272 | |
| sources, `+forceinternal` (see below) | 13,574,244 | - | +121,408 / +272,344 |
| sources, C++ Compiler Configuration Master | 13,574,244 | - | same as Release |
| sources, `--compiler-flags="-O2"` | 13,784,632 | - | +331,796 / +776,592 |

The sources take 272,344 bytes in `libil2cpp.so` against 418,272 for the prebuilt
library. The difference comes from `-Os` (the prebuilt library is CMake Release,
`-O3`; an `-Os` libxlua.so saves 66,680 bytes, see `build/SIZE.md`), from losing
the dynamic symbol table of 280 exports (21,110 bytes) and from IL2CPP's
`--icf=safe` and `--gc-sections`.

Dead stripping works but removes little: 261 of the 279 functions `libxlua.so`
exports stay in `libil2cpp.so`. The 18 that go are `luaL_loadstring`,
`lua_isuserdata`, `lua_rawgetp`, `lua_rawsetp`, `lua_setallocf`, `print_str`,
`print_top`, `print_value`, `xlua_getref_indirect`, `xlua_global_pointer`,
`xlua_newstruct`, `xlua_push_csharp_wrapper`, `xlua_ref_indirect`,
`xlua_registry_pointer`, `xlua_report_object_relationship`,
`xlua_report_table_size`, `xlua_set_csharp_wrapper_caller` and `xlua_tostruct`.
`LuaDLL.cs` imports most of the C API, and the `luaL_Reg` tables of the standard
libraries and LuaSocket keep the rest reachable.

**Compiler flags.** Unity compiles plugin sources with exactly the flags of its
generated C++, minus `-std=c++17 -fno-rtti` (`srcplugin_report.py`):
Unity's NDK r27c `clang++ -x c`, `-Os -g -fvisibility=hidden -fexceptions
-ffunction-sections -fdata-sections -fstack-protector -funwind-tables
-fno-strict-overflow -fstrict-aliasing`, `-target aarch64-linux-android25`, `-I`
for `il2cppOutput/cpp` and libil2cpp. The link adds `--gc-sections --icf=safe
--no-undefined -z max-page-size=16384`. Master gives the same compiler and link
flags as Release and an APK 12 bytes smaller. There is no per-plugin setting. Player Settings' Additional IL2CPP Arguments
(`PlayerSettings.SetAdditionalIl2CppArgs("--compiler-flags=\"...\"")`) appends to
every compile: `-O2` reached the plugin files after `-Os`, and also every
generated file, which grew `libil2cpp.so` by 504,248 bytes. Our own `-D` flags have
to go into the sources (as `luaconf.h` and `xlua_webgl.cpp` do).

**IL2CPP's own switch.** The generated wrapper for a `DllImport("xlua")` is
wrapped in `#if !FORCE_PINVOKE_INTERNAL && !FORCE_PINVOKE_xlua_INTERNAL`.
`+forceinternal` builds the sources without the C# define and passes
`--compiler-flags="-DFORCE_PINVOKE_xlua_INTERNAL"` instead: the same direct calls,
the same APK size (13,574,244), and no change to `LuaDLL.cs` at all. It applies
the define to every IL2CPP file, which is harmless because only xLua's wrappers
test it.

**Build time.** The 49 plugin files take 6.7 s of compiler time, against 139.8 s
for IL2CPP's other C and C++ in the same build, and Bee runs them in parallel with
the rest (12.1 s from the first plugin compile to the last). Bee caches the objects,
so later builds skip them. Unity wall times in `build-info.json` include waiting
for the lock and are not comparable.

### iOS

`+srcplugin` on iOS puts the same files into `Assets/Plugins/iOS/xlua` (with
`LUA_USE_IOS`). Unity copies them to `Libraries/Plugins/iOS/xlua` in the Xcode
project and Xcode compiles them into UnityFramework. Export build, `xcodebuild`
unsigned with a linker map (as `size_report.py` does):

| player | `.app` zip | UnityFramework | xLua's native code in the link map |
| --- | --- | --- | --- |
| baseline | 6,628,027 | 13,820,432 | |
| prebuilt `libxlua.a` | 7,320,141 | 15,452,000 | `libxlua.a` 288,515 |
| sources compiled by Xcode | 7,289,146 | 15,372,336 | loose objects +223,962 |
| **difference** | **-30,995** | **-79,664** | **-64,553** |

Xcode compiles them with its Release settings, `-Os -g -fno-common`, with dead
code stripping on. The linker already dead-strips `libxlua.a`, so the gain is
`-Os` against CMake's `-O3`.

The iOS Simulator players of Q1 use the same layout and link, but did not run
(see Q1). Neither the Android nor the iOS source-plugin player ran on a device or
simulator: the measurements here are of builds.

### Choosing a prebuilt plugin at build time

Two prebuilt `libxlua.so` in `Plugins/Android/full` (Lua 5.5, 418,272 bytes) and
`Plugins/Android/minimal` (Lua 5.3, 344,072 bytes), same name, same ABI. Builds of
the empty project, the APK checked with `plugin_select_report.py`:

| mechanism | no define, release | `XLUA_FULL_PLUGIN` in Player Settings | Development build |
| --- | --- | --- | --- |
| `PluginImporter.SetIncludeInBuildDelegate` (`PluginVariantSelector.cs`), 6000.3.21f1 | minimal | full | full |
| the same, 2021.3.45f2 | minimal | full | full |
| Define Constraints in the `.meta` (`XLUA_FULL_PLUGIN \|\| DEVELOPMENT_BUILD` and `!XLUA_FULL_PLUGIN`, `!DEVELOPMENT_BUILD`), 6000.3.21f1 | build fails | build fails | build fails |

With Define Constraints the build fails in all three cases: "Cannot include plugin
'Assets/Plugins/Android/minimal/libxlua.so' (Key: libxlua.so;ARM64) since plugin
with the same name and architecture was already added". Unity ignored the
constraints and included both libraries; the `.meta` files still held them after
import. Define constraints apply to managed plugins only.

Unity 2021.3 packs the libraries unstripped (2,158,192 and 1,710,952 bytes), 6000.3
strips them; the sizes still tell the two apart.

`PluginVariantSelector.cs` registers the delegate from an `[InitializeOnLoad]`
editor class. The delegate reads the Player Settings scripting defines
(`PlayerSettings.GetScriptingDefineSymbols`) and
`EditorUserBuildSettings.development` when the build asks, so a define in
`csc.rsp` does not count. `SmokeBuild.cs` now sets
`EditorUserBuildSettings.development` from its release flag, because
`BuildOptions.Development` alone leaves that property unchanged.

## Recommendations

1. **Take the LINQ rewrite.** It removes 53,812 bytes from the APK (217,488 from
   `libil2cpp.so`) and System.Linq beyond what an empty Unity project has, cuts
   allocations of type registration by a few percent and the first delegate
   creation by up to 3 KB and 2 ms, and changes nothing measurable on calls.
   Tests pass. Keep `CodeEmit.cs` on LINQ; it never ships.
2. **Do not change IL2CPP Code Generation for xLua.** "Faster (smaller) builds"
   saves 116,412 bytes of what xLua adds and 375,832 bytes of the empty project,
   at a run time cost for value-type generics that this spike did not measure.
   Document it as a project option in the size section of `build/README.md`.
3. **Offer the Android source plugin as an opt-in, through
   `FORCE_PINVOKE_xlua_INTERNAL` rather than a new C# define.** It saves 48,126
   bytes of APK (30,995 of zipped `.app` on iOS), drops `libxlua.so` and its exports, and costs
   about 7 s of compiler time on a clean build. Before it ships: run it on a
   device, give it a script or menu item that copies the sources (the flat layout
   and the `luaconf.h` header block of `copy_source_plugin` in `size_report.py`),
   and decide about LuaSocket. If a C# define is preferred, the
   `XLUA_ANDROID_SOURCE_PLUGIN` prototype works the same.
   Most of the size gain is `-Os`; shipping an `-Os` `libxlua.so` gets 25,324 of
   the 48,126 bytes without any source plugin.
4. **Select prebuilt variants with `SetIncludeInBuildDelegate`, not Define
   Constraints**, if xlua-ng ever ships two libraries per ABI.

## Notes on the runs

- The lock `/Volumes/T7Data/ext-ai-projects/xlua-ng/native-test.lock` was held for
  every Unity, xcodebuild, Mono test and Simulator run, except about a minute early
  on: a test driver script was stopped and its exit trap released the lock while
  its Unity child still ran, so a second Unity started next to it. That Unity was
  killed; the allocation run that overlapped it failed and was repeated, and the
  size build that overlapped it reports sizes, which do not depend on timing.
- `size_report.py` timings in `build-info.json` include time spent waiting for the
  lock.
- The root disk went from 32 to 25.5 GB free during the session. Builds, Library
  folders and the `TMPDIR` of `size_report.py` were on T7Data; the Editor runs of
  `alloc_probe.py` and `editor_tests.py` kept the default temp directory, and swap
  files on the root volume held 13 GB. The Simulator run was skipped to keep the
  disk above 25 GB.
