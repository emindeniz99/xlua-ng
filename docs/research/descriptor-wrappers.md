# Descriptor wrappers, generic virtual methods and auto-properties in IL2CPP players

Date: 2026-10-19. Spike approved by the owner (question 89): prototype and
measure, no product feature. The prototype is on the local branch
`spike/descriptor-wrappers` (not pushed; commits `ef22a5ee`, `69f09107`,
`90c51ef2` on top of main `b6500f95`). Nothing of it is in the product tree.

Marks: **[C]** confirmed here by a build, a run or the binary;
**[S]** secondary (another project's report or documentation);
**[U]** unconfirmed estimate.

## Question

The source generator's GCM mode (`GEN_CODE_MINIMIZE`) already folds the
per-member native entry points into one trampoline
([source-generator.md](../design/source-generator.md#gcm-mode-as-implemented-2026-10-18)).
Each wrapped member is still a C# method of its own that reads its
arguments, checks overloads, converts integers by Lua 5.3's rule, pushes its
result and is registered with a delegate. Would a descriptor-based mode,
where each member is only a few bytes of data plus one switch case, shrink
IL2CPP players further? And two cheap checks: do generic virtual methods
pull extra IL2CPP runtime code into xLua's players, and are auto-properties
worth turning into fields?

## Sources

- The owner's sqlitehost project, read as data at commit `09c5a8c`
  ([repository](https://github.com/emindeniz99/sqlitehost)):
  [docs/csharp-api.md](https://github.com/emindeniz99/sqlitehost/blob/09c5a8cae8e5209621af361aab4cdb993c720677/docs/csharp-api.md)
  ("Compact descriptor API", "Ultra descriptor API") and
  [docs/reports/il2cpp-size-report.md](https://github.com/emindeniz99/sqlitehost/blob/09c5a8cae8e5209621af361aab4cdb993c720677/docs/reports/il2cpp-size-report.md).
  Its IL2CPP numbers (Unity 2022.3.9f1, ARM64, High stripping) **[S]**:
  marginal cost per method 9.09 KB classic, 4.88 KB compact, 1.80 KB ultra
  (uncompressed `libil2cpp.so` + metadata); fixed cost 259, 232 and 275 KB;
  ultra beats compact above about 14 methods (report section 2, lines 61-65,
  and section 3 "H-PROFILES", lines 102-122). One generic virtual method
  costs +2,880 bytes of `libil2cpp.so` under IL2CPP, about 1% of its
  NativeAOT cost; the "~250 KB" figure is NativeAOT's (section 3 "H-GVM",
  lines 73-89). Fields instead of auto-properties saved 14,840 bytes of
  `libil2cpp.so` and 16,872 of metadata on a 50-method host ("H-FIELDS",
  lines 91-100).
- This repository: `General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs`
  (plain and GCM emitters: `Callback` and `GcmParameters` at lines 119-129,
  `Function` and the GCM helpers from line 994, `Method` at 1344-1441), `Assets/XLua/Src/ObjectTranslator.cs:1735-1776`
  (the `CSharpWrapper` table), `StaticLuaCallbacks.cs:149-161`
  (`CSharpWrapperCallerImpl`), `Utils.cs:1622-1700` (registration),
  [build/SIZE.md](../../build/SIZE.md#a-game-sized-configuration-2026-10-18)
  (the game-sized configuration) and
  [Trimming the core-only player](../../build/SIZE.md#trimming-the-core-only-player).

## The prototype

**Shape** (branch files `General/SourceGenerator/XLua.SourceGenerator/SpikeDescriptors.cs`
and `Assets/XLua/Src/SpikeDescriptorCore.cs`). Per wrapped type the generator
writes:

- one descriptor string: per Lua-visible function its slot (method, getter,
  setter, static or not, constructor), flags, name and overloads, and per
  overload its switch case, the argument kinds (bool, the eight integer
  types, float, double, string, class or interface of type *n*, UnityEngine
  object of type *n*, `object`, boxed struct or enum of type *n*,
  GCOptimize struct of type *n*) and the result kind;
- one switch method that performs the calls on an argument buffer, for
  example `case 3: { a.rl = ((UnityEngine.Transform)a.self).childCount; } return;`
  or, for a GCOptimize value, `{ var L = a.L; UnityEngine.Vector3 p0;
  Structs.Get_2(translator, L, a.start + 0, out p0); ((UnityEngine.Transform)a.self).position = p0; }`;
- a registration that hands the string, a `typeof` array and the switch to
  `XLua.DescType`, which registers one `CSharpWrapper` per Lua function.

The runtime core (about 300 lines, compiled only with `GEN_CODE_MINIMIZE`
and `XLUA_SPIKE_DESC`) runs on GCM's trampoline: it picks the overload by
count and kind (the checks the plain wrappers write), reads the arguments
into the buffer (`xlua_check*` for integers, `GetObject`,
`GetUnityObjectArg`), calls the switch and pushes the result. Exceptions
reach Lua through GCM's entry as before. The buffer is reused per wrapped
type: a call consumes its arguments before the C# member runs and writes
its result after it returns, so nested calls do not clash, and the stub
copies `L` before the call. A buffer on the stack is the other variant
measured (`XLUA_SPIKE_DESC_STACK`).

**Covered**: methods, static methods, constructors (with the default value
of a struct), fields and properties get and set, instance members of
structs (copy, call, `Update`), parameters and results of the types above,
overloads and optional parameters. **Not covered**, left out in every
spike mode by `XLUA_SPIKE_SUBSET`: `out`, `ref` and `in` parameters,
`params`, events, indexers, operators, generic and extension methods,
delegate, `byte[]`, `IntPtr`, `decimal`, `Nullable`, `LuaTable` and
`LuaFunction` parameters or results, more than 8 arguments, enum wrappers
(they stay GCM's).

**Same member set in every mode.** With `XLUA_SPIKE_SUBSET` the plain and
GCM emitters wrap exactly the members the descriptor mode wraps (a diagnostic,
`SPIKE001`, prints the counts). The game-sized configuration
(`Test/MobileSmoke/SizeGameSgConfig.cs`, 264 types) keeps **4,308 of 4,414
Lua functions and 5,270 of 5,702 overloads** **[C]**; the five-type
configuration (`SizeSgConfig.cs`) 160 of 170 functions and 219 of 265
overloads.

**Behaviour** (`Test/Native/spike_desc_check.py` on the branch): 111 Lua
probes over five fixture types (every argument and result kind, Lua 5.3's
integer rule, out-of-range and wrong-type arguments, overload selection by
count and kind, optional parameters, constructor errors, a C# exception,
struct copies and updates, GCOptimize values, interface results, nested
calls into the same member through Lua) print the same results and error
messages in plain, GCM and descriptor mode **[C]** (Mono 6.14.1, macOS
arm64, Lua 5.5.1). The full `run_managed.py` suite was not run in
descriptor mode: its fixtures use members the prototype does not cover.

## Player size

`Test/MobileSmoke/size_report.py run --platforms android --configs
spike-game-minimal,spike-game-high,spike-small-minimal,spike-small-high`
(the spike branch's variants `+sub` and `+desc`), Unity 6000.3.21f1,
release ARM64 IL2CPP APKs, xLua as the UPM package, Lua 5.5.1 plugin
(identical in every variant, 163,902 bytes in the APK). APK delta over the
baseline (12,287,148 / 11,534,224 bytes with uGUI for the game rows,
12,279,056 / 11,526,124 without), `libil2cpp.so` and `global-metadata.dat`
deltas compressed in the APK / uncompressed **[C]**:

| configuration | stripping | mode | APK delta | `libil2cpp.so` | metadata |
| --- | --- | --- | ---: | ---: | ---: |
| game (264 types, 4,308 functions) | Minimal | source generator | +6,028,146 | +3,660,211 / +14,228,672 | +557,841 / +1,954,020 |
| | | GCM | +5,400,366 | +3,012,898 / +11,720,080 | +577,377 / +2,034,064 |
| | | **descriptor** | **+4,957,262** | +2,632,943 / +9,927,384 | +514,162 / +1,690,740 |
| | High | source generator | +5,811,402 | +3,523,841 / +13,779,344 | +476,757 / +1,707,556 |
| | | GCM | +5,184,398 | +2,875,914 / +11,270,152 | +497,684 / +1,787,328 |
| | | **descriptor** | **+4,737,142** | +2,492,630 / +9,476,864 | +433,653 / +1,442,788 |
| five types (160 functions) | Minimal | source generator | +1,627,590 | +737,771 / +2,597,904 | +150,874 / +443,084 |
| | | GCM | +1,605,462 | +714,341 / +2,505,800 | +152,180 / +447,316 |
| | | **descriptor** | **+1,590,458** | +701,479 / +2,449,832 | +149,971 / +435,832 |
| | High | source generator | +1,502,222 | +660,130 / +2,374,944 | +101,935 / +328,400 |
| | | GCM | +1,475,650 | +632,444 / +2,282,096 | +103,047 / +332,368 |
| | | **descriptor** | **+1,464,070** | +623,239 / +2,225,832 | +100,607 / +319,756 |

`libunity.so` is the same in all three modes of a configuration (within 5
bytes).

- **Descriptor against GCM, game size: -443,104 bytes of APK with Minimal
  stripping (-8.2% of GCM's delta) and -447,256 with High (-8.6%)**;
  `libil2cpp.so` -380 / -383 KB compressed (-1.79 MB uncompressed), the
  metadata -63 / -64 KB (-343 KB uncompressed). GCM itself saves 627 to 628
  KB over the plain source generator on the same members, so the descriptor
  mode adds about 70% of GCM's saving again **[C]**.
- **Five types: -15,004 (Minimal) and -11,580 (High)** against GCM **[C]**.
- **Where it comes from** (`llvm-nm` on `libil2cpp.sym.so`, game, High)
  **[C]**: the wrapper classes' code is 3,078,192 bytes in 4,643 functions
  plus 336,056 bytes of reverse P/Invoke wrappers (4,419) in plain mode,
  1,812,272 bytes in 4,643 functions with GCM, and 782,228 bytes in 599
  functions in descriptor mode: 528,020 in the 264 switch methods (about
  100 bytes per case), 246,256 in the 264 registrations (GCM's
  registrations are 474,620 bytes: a delegate per member) and the rest in
  the GCM-style `__CreateInstance` of types without a covered constructor.
  The core adds 18 functions, 9,536 bytes. The C++ IL2CPP writes for
  Assembly-CSharp is 35.2 MB, 22.1 MB and 11.7 MB; the stripped
  Assembly-CSharp (Minimal) 1,358,848, 1,121,280 and 758,272 bytes;
  `XLua.dll` grows by 5,120 bytes (the core).

**Slope and intercept** from the two configurations (cost = intercept +
slope x Lua functions; the intercept also holds the engine code and uGUI
that the game configuration pulls in, so only its difference between modes
means something):

| stripping | mode | APK per function | `libil2cpp.so` uncompressed per function | metadata uncompressed per function | intercept against GCM (APK) |
| --- | --- | ---: | ---: | ---: | ---: |
| Minimal | source generator | 1,061 | 2,804 | 364 | -1,234 |
| | GCM | 915 | 2,221 | 383 | 0 |
| | descriptor | 812 | 1,803 | 303 | +1,509 |
| High | source generator | 1,039 | 2,749 | 333 | +3,412 |
| | GCM | 894 | 2,167 | 351 | 0 |
| | descriptor | 789 | 1,748 | 271 | +5,225 |

- The descriptor mode costs **103 to 105 bytes of APK less per Lua function**
  than GCM (418 to 419 bytes of uncompressed `libil2cpp.so`, 80 bytes of
  metadata), for a fixed cost of 1.5 to 5.2 KB. By the straight lines it
  breaks even with GCM at about 15 functions (Minimal) or 50 (High)
  **[U]** (two points per line; the five-type configuration is already
  above both and wins by 11 to 15 KB **[C]**).
- **Against sqlitehost** **[S]/[C]**: sqlitehost's ultra profile pays
  43 KB more fixed cost than its compact one; here the core is small
  because it reuses GCM's trampoline, the translator's readers and its
  error handling, so the descriptor mode has no size penalty for small
  configurations. The per-function costs are not comparable one to one:
  ours include the engine code each bound member keeps alive.

Not measured: iOS, WebGL, Medium stripping, the full game configuration
(the 3% of functions and 8% of overloads the prototype does not cover would
stay GCM functions in a full version), and Lua versions other than 5.5.1
(the C# is the same).

## Call cost

Host only: `Test/Native/benchmark.py` executables (the spike's
`spike_bench.py` builds each mode once and runs eight interleaved rounds),
Mono 6.14.1, macOS arm64 (Apple M2 Pro), Lua 5.5.1, the subset in every mode,
minimum of the per-round medians, ns per call **[C]**:

| case | source generator | GCM | descriptor | descriptor, stack buffer |
| --- | ---: | ---: | ---: | ---: |
| `static_call` | 53.3 | 65.1 | 81.5 | 100.6 |
| `instance_call` | 84.5 | 101.5 | 103.9 | 117.8 |
| `property_get` | 91.0 | 95.2 | 103.3 | 121.9 |
| `property_set` | 78.7 | 77.8 | 89.0 | 108.7 |
| `field_get` | 92.4 | 96.4 | 102.0 | 134.9 |
| `string_return` | 85.9 | 97.5 | 108.4 | 126.5 |
| `struct_gcopt_pass` (GCOptimize struct in and out) | 161.4 | 164.7 | 181.3 | 200.4 |
| `struct_boxed_field_get` | 176.2 | 191.4 | 97.4 | 120.8 |
| `class_object_arg` | 64.9 | 67.9 | 81.9 | 98.3 |

- The descriptor mode is **2 to 17 ns slower per call than GCM** (+2% to
  +25%); GCM is -1 to +17 ns against plain. Allocations are the same in
  every case. Zeroing a 224-byte buffer per call cost Mono another 15 to
  33 ns (the stack column), hence the reused buffer.
- `struct_boxed_field_get` is twice as fast because the core reads a
  non-GCOptimize struct `self` with `GetObject` where the plain and GCM
  wrappers call the generic `translator.Get<T>`.
- IL2CPP compiles the switch and the core to C++, where the costs are
  different. **Not measured on a device or the emulator** **[U]**; that is
  the first thing a full version must measure.

## Generic virtual methods

**Method:** a Mono.Cecil scan (`Test/Native/spike/IlScan.cs` on the branch)
of the assemblies each player compiles (`Library/Bee/PlayerScriptAssemblies`,
the Minimal set) and of the stripped ones (`ManagedStripped`, High), for
methods with their own type parameters that are virtual, abstract or on an
interface, and for call sites to them; and a diff of the IL2CPP runtime
functions (`il2cpp::*`, `il2cpp_*`) in `libil2cpp.sym.so` between each
player and the empty project. Players: the core-only player (`core-5.5`,
`XLUA_CORE_ONLY`) and the minimal preset player (`ng-5.5+minimal`), both
with Minimal and High stripping, and the game-sized descriptor player.

- **XLua.Core, XLua and the generated Assembly-CSharp declare no generic
  virtual or interface method and call none** **[C]** (0 declarations, 0
  call sites, Minimal and High).
- **The empty project already has the machinery** **[C]**: its stripped
  assemblies keep two generic virtual call sites of Unity's own
  (`RenderPipeline.ProcessRenderRequests<T>` in UnityEngine.CoreModule,
  `ITypeVisitor.Visit<T>` in UnityEngine.PropertiesModule), and its
  `libil2cpp.so` has the generic method runtime: 38 functions, 4,680 bytes
  (`GenericMethod::GetGenericVirtualMethod`, `CreateMethodLocked` at 1,056
  bytes, `GenericMetadata::Inflate*`, `GlobalMetadata::GetGenericInstanceMethod`).
- **xLua's players add no runtime function of that kind** **[C]**: the core-only
  player adds 23 runtime functions (1,856 bytes, `Marshal.AllocHGlobal`,
  `StringToHGlobalAnsi`, `GCHandle.GetAddrOfPinnedObject`), the minimal
  player 57 (4,748 bytes, reflection over assemblies: `Image::GetTypes`,
  `Assembly.LoadFrom`, `AppDomain.GetAssemblies`), the game player 413
  (34,968 bytes, mostly `FinallyHelper` instances); none is generic virtual
  dispatch or inflation. The game player's one extra generic virtual call
  site is `Enumerable.ThenBy` inside UnityEngine.TextCoreTextEngineModule,
  which uGUI keeps, not xLua.
- IL2CPP has no separate dynamic type loader to pull in; NativeAOT does,
  which is where sqlitehost's 250 KB figure comes from **[S]** (its IL2CPP
  measurement of one such method is +2.9 KB). **There is nothing to remove,
  so no scratch build without them was made.**

## Auto-properties

Survivors (the same scan), all public API **[C]**:

| player | stripping | XLua.Core | XLua |
| --- | --- | --- | --- |
| core-only | Minimal | 14: `LuaOptions.HashSeed`, `AllowInterrupt`, `Libraries`; `LuaException.LuaStackTrace`; `Limit`, `InstructionLimit`, `TimeLimit`, `ErrorValue`, `ChunkName`, `Expected`, `Actual`, `MemoryLimit`, `MemoryUsed`, `HostOutOfMemory` of the exception types | - |
| core-only | High | 5 (`LuaOptions`' three, `LuaStackTrace`, the setter of `ErrorValue`) | - |
| minimal preset | Minimal | 7 (`LuaOptions`' three, `LuaStackTrace`, the three limit properties) | 1 (`OverloadMethodWrap.HasDefalutValue`) |
| minimal preset | High | 4 | 1 |

The generated code has none. **Measured** in a scratch copy with all 14
XLua.Core auto-properties as public fields (`Test/Native/spike/patch_fields.py`),
core-only player, the same method as above: **-524 bytes of APK (Minimal)
and -556 (High)** (`libil2cpp.so` +96 / -315 bytes compressed, -1,944 /
-840 uncompressed; metadata -612 / -236 compressed) **[C]**. About 40 bytes
of APK per property. sqlitehost saw more (about 0.6 KB per method of a
host with DTOs full of auto-properties **[S]**); xLua has few of them. Every one of them is public API, and a field instead of a
property is a binary break. **Not worth doing.**

## Recommendation

1. **Descriptor mode: worth implementing, opt-in, as GCM's successor
   rather than a fourth mode.** At game size it removes another 443 to
   447 KB of APK (8.2% to 8.6% of xLua's cost in that player, about 70% of
   what GCM saved), the same order as the GCM gap that made the owner add
   GCM to the generator; it never loses at small sizes; and the host call
   cost over GCM is 2 to 17 ns. Before it becomes a preset default, call
   cost must be measured on the emulator and a phone, since IL2CPP and Mono
   differ here. It uses the GCM trampoline, so it needs `GEN_CODE_MINIMIZE`
   anyway; a separate define (or a generator option) would select it.
2. **Effort for a full version: about 2.5 weeks (12 working days)** **[U]**:
   - generator, the remaining member kinds (3 to 4 days): `out`/`ref`/`in`
     with results pushed after the call and structs updated, `params`
     (stub-side `GetParams<T>`), events, indexers (`__CSIndexer` and
     `__NewIndexer` semantics), operators (both operand orders and the
     error), extension and closed generic methods, delegate parameters
     (stub-side `GetDelegate<T>`), `byte[]`, `IntPtr`, `decimal`,
     `Nullable`, `LuaTable`/`LuaFunction`, more than 8 arguments and more
     than 10 results, enum wrappers;
   - runtime core (2 days): buffer per translator with the references
     cleared after a call, the nested-call and coroutine rules written down
     and tested, error texts for nested type names (the core uses the
     generator's display name), struct `self` paths;
   - integration (2 days): mode selection, diagnostics (an XLSG013-style
     mismatch check), the model cache key, manifest, MIGRATING.md, the
     preset question;
   - tests (3 to 4 days): generator unit tests, `run_managed.py` with a
     descriptor pass (the 458 Lua tests and the surface, bridge, hotfix and
     struct diffs), `hotfix_check.py`, the Unity editor suite, IL2CPP smoke
     players on the `xlua16k` emulator, the size run again and emulator
     timings;
   - documentation (1 day).
3. **Smaller follow-ups the spike points at** **[U]**: the descriptor
   registrations are still 246 KB of code for 264 types (the `typeof`
   arrays, constant fields registered one by one, lazy members); moving
   those into the descriptor data too would cut more. Inside plain GCM, a
   data-driven registration without one delegate creation per member would
   take part of the 228 KB difference between GCM's and the descriptor
   registrations without the rest of the mode (not measured).
4. **Generic virtual methods: no fix.** xLua has none, and IL2CPP's machinery
   for them is in every player anyway.
5. **Auto-properties: no fix.** 14 survive, all public API; fields would save
   about 0.5 KB of APK in the core-only player.

## Reproducing

On `spike/descriptor-wrappers`: `Test/Native/spike_desc_check.py --output
DIR --plugin libxlua.dylib` (behaviour), `Test/Native/spike/spike_bench.py
REPO OUT PLUGIN 8 sourcegen-sub,sourcegen-min-sub,sourcegen-desc,sourcegen-desc-stack`
(call cost), the `size_report.py` command above, then
`Test/Native/spike/analyze.py WORK/size-report.json`; the core and minimal
players with `size_report.py run --configs core,core-high --variants
baseline,core-5.5,ng-5.5+minimal`, and `IlScan.cs`, `symdiff.py` and
`symsum.py` on their `Library` folders.
