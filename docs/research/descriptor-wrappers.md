# Descriptor wrappers, generic virtual methods and auto-properties in IL2CPP players

Date: 2026-10-19; phone and emulator call cost 2026-10-28; the per-call
overhead experiment 2026-10-29. Spike approved by the owner (question 89): prototype and
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
  the first thing a full version must measure. Measured on 2026-10-28:
  see the next section.

## Call cost on the phone and the emulator (2026-10-28)

Step 1 of the owner's plan of 2026-10-28 ([backlog](../design/backlog.md)):
the prototype's call cost in IL2CPP players on the test phone and the
`xlua16k` emulator, against GCM and plain generated code, for the owner's
decision.

**Players** **[C]**: the spike branch (`spike/descriptor-wrappers` at
`90c51ef2`, unchanged C# and generator, not merged with main) with one
addition, the player bench: `Test/MobileSmoke/PlayerBench.cs` and the
`+pbench` variant of `size_report.py` (committed on main with this note;
on the spike tree the same file plus the variant
`ng-5.5+pkg+sg+gcm+desc+pbench`). Three release players, Unity
6000.3.21f1, Android ARM64 IL2CPP, Minimal stripping, xLua as the UPM
package with the source generator, the spike's Lua 5.5.1 plugin
(`ultra/plugins`, the same library as the spike's size players):

| mode | variant | defines | `W_XLuaBench_BenchTarget` methods |
| --- | --- | --- | ---: |
| plain | `ng-5.5+pkg+sg+sub+pbench` | `XLUA_SPIKE_SUBSET` | 19 |
| GCM | `ng-5.5+pkg+sg+gcm+sub+pbench` | `GEN_CODE_MINIMIZE`, `XLUA_SPIKE_SUBSET` | 19 |
| descriptor | `ng-5.5+pkg+sg+gcm+desc+pbench` | `GEN_CODE_MINIMIZE`, `XLUA_SPIKE_DESC` | 2 (switch and registration) |

All three wrap the same members: the generator's `SPIKE001` reported 9
wrapped types, 239 of 251 Lua functions and 305 of 361 overloads in each
build. Each player logs its mode and the wrapper's method count at start,
so a mixed-up APK would show.

**Cases**: the Lua -> C# cases of `Test/Native/Benchmark.cs` that the
prototype covers, with the same Lua chunks and names (static and instance
calls, property and field get and set, string argument and result,
GCOptimize and boxed structs passed, created and read, an object result,
class and `UnityEngine.Object` arguments, four overload sets), plus seven
UnityEngine calls: `Vector3(x, y, z)`, `transform.position` get and set
(a GCOptimize `Vector3` result and argument), `v.x` of a `Vector3`,
`Mathf.Max(int, int)` and `Mathf.Max(float, float)` (overloads, the
`params` ones left out by the subset), and `go:GetInstanceID()`.
`lua_loop` is an empty Lua loop. Every number is ns per call with the Lua
loop included, as on the host.

**Method**: a player runs 7 rounds; a round runs every case once with
100,000 calls after a full Lua and C# collection, and a case's result of
one launch is the median of its 7 rounds. The three APKs are installed
side by side (packages `org.xluang.pbench.plain`, `.gcm`, `.desc`) and
launched in rotating order (plain, GCM, descriptor; GCM, descriptor,
plain; ...): 8 launches per mode on the phone (15 s apart), 10 on the
emulator. The tables give **the median of the per-launch medians, and in
parentheses the minimum of them**. Nothing on the devices was pinned or
changed: no CPU affinity, governor or setting; the players keep the screen
on with `Screen.sleepTimeout` while in front.

**Devices**:

- **Phone**: moto g play 2024 (XT2413V), Snapdragon 680 (SM6225, 4x
  Cortex-A73 and 4x Cortex-A53), Android 14, 4 KB pages. Thermal status 0
  before and after every launch; the per-launch medians of a case differ
  by about 1% (the median and minimum columns are close).
- **Emulator**: `xlua16k` (Android 16, API 36, 16 KB pages, arm64, 4
  cores) on the M2 Pro host, which other jobs were also using. It
  scatters: a case's per-launch medians differ by up to 2x, so read its
  differences under about 20 ns as noise.

### Phone (moto g play 2024) **[C]**

ns per call; "host" is the descriptor-minus-GCM difference of the
2026-10-19 host table above (Mono, M2 Pro), and the last column is the
phone's difference over the host's.

| case | plain | GCM | descriptor | descriptor - GCM | vs GCM | host | phone / host |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `lua_loop` | 11.2 (11.2) | 11.2 (11.2) | 11.2 (11.2) | +0.0 (+0.0) | 0% | - | - |
| `static_call` | 159.0 (157.3) | 212.4 (210.9) | 254.0 (253.4) | +41.6 (+42.5) | +20% | +16.4 | 2.5x |
| `instance_call` | 338.9 (335.8) | 361.0 (359.6) | 405.6 (400.4) | +44.6 (+40.7) | +12% | +2.4 | 18.6x |
| `property_get` | 391.1 (387.9) | 427.8 (427.2) | 469.8 (468.9) | +42.1 (+41.7) | +10% | +8.1 | 5.2x |
| `property_set` | 343.3 (333.1) | 371.2 (368.6) | 426.0 (421.4) | +54.8 (+52.8) | +15% | +11.2 | 4.9x |
| `field_get` | 394.0 (388.7) | 427.7 (425.7) | 473.0 (471.8) | +45.3 (+46.1) | +11% | +5.6 | 8.1x |
| `field_set` | 334.4 (331.8) | 371.7 (368.2) | 426.5 (424.5) | +54.9 (+56.3) | +15% | +6.2 | 8.9x |
| `string_arg` | 389.3 (382.8) | 436.0 (433.4) | 498.8 (497.3) | +62.8 (+64.0) | +14% | +11.4 | 5.5x |
| `string_return` | 307.9 (305.6) | 353.4 (351.0) | 424.1 (423.5) | +70.8 (+72.5) | +20% | +10.9 | 6.5x |
| `struct_gcopt_pass` | 776.0 (758.3) | 782.2 (768.4) | 844.7 (827.1) | +62.5 (+58.7) | +8% | +16.6 | 3.8x |
| `struct_boxed_pass` | 2953.1 (2850.5) | 2855.5 (2833.5) | 2513.0 (2441.1) | -342.4 (-392.4) | -12% | -90.6 | - |
| `struct_gcopt_new` | 774.1 (746.9) | 765.7 (762.9) | 853.4 (845.6) | +87.7 (+82.7) | +11% | +33.0 | 2.7x |
| `struct_gcopt_field_get` | 440.2 (439.8) | 454.1 (453.9) | 482.0 (479.0) | +27.9 (+25.1) | +6% | +4.4 | 6.3x |
| `struct_boxed_field_get` | 923.7 (897.5) | 911.5 (889.6) | 495.8 (490.0) | -415.7 (-399.6) | -46% | -94.0 | - |
| `object_return_cached` | 1450.0 (1429.4) | 1444.6 (1435.0) | 1502.1 (1491.6) | +57.5 (+56.6) | +4% | +11.2 | 5.1x |
| `unity_object_arg` | 553.7 (547.3) | 532.0 (528.4) | 408.9 (401.6) | -123.1 (-126.7) | -23% | +9.4 | - |
| `class_object_arg` | 493.2 (467.2) | 498.0 (493.8) | 346.9 (344.5) | -151.2 (-149.3) | -30% | +14.0 | - |
| `overload_disjoint` | 178.1 (177.2) | 212.8 (212.4) | 256.1 (255.7) | +43.3 (+43.3) | +20% | - | - |
| `overload_num_integer` | 153.4 (148.9) | 189.1 (188.6) | 234.8 (234.6) | +45.8 (+46.0) | +24% | - | - |
| `overload_num_float` | 156.8 (153.3) | 188.8 (188.1) | 234.6 (234.0) | +45.8 (+45.9) | +24% | - | - |
| `overload_obj_class` | 1035.5 (994.2) | 982.8 (980.1) | 1075.0 (1052.8) | +92.2 (+72.8) | +9% | - | - |
| `overload_obj_derived` | 1026.2 (1003.6) | 1046.2 (1038.3) | 1202.0 (1190.1) | +155.8 (+151.8) | +15% | - | - |
| `unity_vector3_new` | 703.7 (685.4) | 704.1 (701.6) | 838.3 (829.5) | +134.2 (+127.9) | +19% | - | - |
| `unity_position_get` | 876.1 (860.0) | 891.1 (868.6) | 968.3 (956.7) | +77.2 (+88.1) | +9% | - | - |
| `unity_position_set` | 534.2 (519.4) | 552.0 (544.8) | 660.4 (634.4) | +108.4 (+89.6) | +20% | - | - |
| `unity_vector3_field_get` | 497.0 (488.6) | 515.4 (512.0) | 592.8 (587.4) | +77.4 (+75.5) | +15% | - | - |
| `unity_mathf_max_int` | 178.3 (175.2) | 215.6 (215.0) | 277.2 (276.1) | +61.5 (+61.1) | +29% | - | - |
| `unity_mathf_max_float` | 173.4 (169.6) | 210.5 (209.5) | 271.5 (270.6) | +61.0 (+61.1) | +29% | - | - |
| `unity_instance_method` | 3044.2 (2908.2) | 2964.9 (2925.3) | 2864.4 (2758.1) | -100.5 (-167.1) | -3% | - | - |

### Emulator (`xlua16k`) **[C]**

| case | plain | GCM | descriptor | descriptor - GCM | vs GCM | host | emulator / host |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `lua_loop` | 2.3 (2.2) | 2.2 (2.2) | 2.3 (2.2) | +0.0 (+0.0) | +2% | - | - |
| `static_call` | 37.5 (34.1) | 60.2 (46.4) | 78.4 (58.4) | +18.2 (+12.0) | +30% | +16.4 | 1.1x |
| `instance_call` | 80.5 (67.7) | 91.2 (75.6) | 98.9 (77.6) | +7.7 (+2.0) | +8% | +2.4 | 3.2x |
| `property_get` | 98.1 (89.8) | 105.8 (84.8) | 93.9 (86.0) | -12.0 (+1.1) | -11% | +8.1 | - |
| `property_set` | 74.7 (65.7) | 91.1 (72.0) | 86.1 (77.3) | -5.1 (+5.3) | -6% | +11.2 | - |
| `field_get` | 107.0 (82.8) | 98.5 (90.8) | 109.8 (95.9) | +11.4 (+5.1) | +12% | +5.6 | 2.0x |
| `field_set` | 78.8 (68.9) | 74.5 (69.5) | 89.4 (73.0) | +14.9 (+3.5) | +20% | +6.2 | 2.4x |
| `string_arg` | 309.2 (162.0) | 260.9 (159.1) | 329.5 (215.2) | +68.7 (+56.1) | +26% | +11.4 | 6.0x |
| `string_return` | 76.4 (61.8) | 78.6 (68.4) | 111.2 (85.0) | +32.6 (+16.6) | +41% | +10.9 | 3.0x |
| `struct_gcopt_pass` | 157.2 (133.2) | 149.3 (130.3) | 153.6 (134.6) | +4.4 (+4.3) | +3% | +16.6 | 0.3x |
| `struct_boxed_pass` | 668.3 (629.3) | 714.0 (630.3) | 594.4 (546.9) | -119.7 (-83.4) | -17% | -90.6 | - |
| `struct_gcopt_new` | 130.1 (115.1) | 122.4 (116.7) | 151.3 (133.9) | +28.9 (+17.3) | +24% | +33.0 | 0.9x |
| `struct_gcopt_field_get` | 97.5 (91.0) | 119.9 (89.8) | 116.7 (91.5) | -3.2 (+1.7) | -3% | +4.4 | - |
| `struct_boxed_field_get` | 194.1 (166.1) | 197.2 (181.7) | 112.0 (94.0) | -85.1 (-87.7) | -43% | -94.0 | - |
| `object_return_cached` | 317.1 (268.4) | 345.5 (288.5) | 318.7 (293.8) | -26.8 (+5.2) | -8% | +11.2 | - |
| `unity_object_arg` | 135.7 (115.3) | 138.0 (109.8) | 106.4 (76.6) | -31.6 (-33.2) | -23% | +9.4 | - |
| `class_object_arg` | 118.9 (97.1) | 126.1 (101.8) | 88.0 (73.1) | -38.1 (-28.8) | -30% | +14.0 | - |
| `overload_disjoint` | 44.6 (36.9) | 47.7 (45.2) | 69.9 (56.3) | +22.3 (+11.1) | +47% | - | - |
| `overload_num_integer` | 38.5 (34.5) | 42.6 (39.9) | 52.6 (46.0) | +10.0 (+6.1) | +24% | - | - |
| `overload_num_float` | 41.5 (30.5) | 41.2 (37.1) | 56.0 (46.0) | +14.8 (+8.9) | +36% | - | - |
| `overload_obj_class` | 230.2 (200.6) | 225.5 (196.2) | 229.5 (202.3) | +4.0 (+6.1) | +2% | - | - |
| `overload_obj_derived` | 258.5 (221.6) | 242.4 (202.7) | 284.2 (232.6) | +41.8 (+29.9) | +17% | - | - |
| `unity_vector3_new` | 147.2 (119.9) | 145.5 (106.9) | 189.0 (151.1) | +43.4 (+44.1) | +30% | - | - |
| `unity_position_get` | 192.6 (146.9) | 168.2 (137.7) | 206.5 (157.3) | +38.3 (+19.7) | +23% | - | - |
| `unity_position_set` | 122.6 (96.3) | 127.1 (100.0) | 148.0 (112.2) | +20.9 (+12.2) | +16% | - | - |
| `unity_vector3_field_get` | 122.7 (100.5) | 129.5 (113.3) | 153.2 (120.3) | +23.7 (+7.0) | +18% | - | - |
| `unity_mathf_max_int` | 53.0 (39.2) | 54.0 (47.5) | 72.4 (66.1) | +18.4 (+18.6) | +34% | - | - |
| `unity_mathf_max_float` | 45.9 (38.7) | 55.1 (43.4) | 75.1 (60.5) | +20.0 (+17.1) | +36% | - | - |
| `unity_instance_method` | 842.8 (765.6) | 839.0 (776.8) | 814.7 (682.8) | -24.3 (-93.9) | -3% | - | - |

### What the numbers say

- **On the phone the descriptor mode is 28 to 156 ns slower per call than
  GCM** in every case where it reads its arguments the way GCM does: about
  +42 to +46 ns for a call with zero to two integer arguments (static,
  instance, property, field, the overload sets) and +60 to +134 ns with
  strings, `Vector3` or a GCOptimize struct, which is **+6% to +29%** over
  GCM **[C]**. The cost is mostly fixed per call, not per argument: the
  static call with two integers, the instance call with one and the
  property get with none all pay about 42 ns. On the host the same cases
  paid 2 to 17 ns.
- **Ratio to the host**: over the 11 hot-path cases measured on both, the
  phone's GCM call costs 4.5x the host's (457 against 102 ns), and its
  descriptor-minus-GCM difference 3.0x (+34.6 against +11.7 ns). Relative to
  GCM the descriptor penalty is about the same on both (+8% here, +12% on
  the host, for those cases); in nanoseconds it is **well above the host's
  2 to 17 ns**, the measure the backlog set for this step. The emulator is
  close to the host (+5.4 ns on the same cases, +13 ns over the whole mix
  below), so it does not predict the phone **[C]**.
- **Where the descriptor mode is faster** (phone): boxed structs (-342 and
  -416 ns, the `GetObject` path already seen on the host) and class or
  `UnityEngine.Object` arguments (-123 and -151 ns). The last two were
  slower on the host (+9 and +14 ns), so IL2CPP pays more for the plain and
  GCM wrappers' argument read there; not traced **[U]**. `object_return_cached`,
  `overload_obj_*` and `unity_instance_method` are dominated by other work
  (the translator's object cache, assignability checks;
  `GetInstanceID` is declared on `UnityEngine.Object`, which this
  configuration does not wrap, so that call goes through the reflection
  wrapper in every mode).
- **GCM itself** costs on the phone +53 ns (`static_call`) and +22 to +37
  ns per call (instance, properties, fields, overloads) over plain wrappers,
  about +24 ns over the mix below (+5%) **[C]**. The minimal preset implies
  GCM, so its players already pay this.
- **Why** (not profiled) **[U]**: per call the core
  (`Assets/XLua/Src/SpikeDescriptorCore.cs` on the branch) enters the
  group through the `CSharpWrapper` delegate as GCM does, then indexes the
  type's reused buffer (`owner.buffer[0]`, line 204), loops over the
  argument kinds with two switches each (`SetL`/`SetD`/`SetO`, from line
  307), calls the type's switch through a second delegate (`invoker`, line
  227) and switches over the result kind. IL2CPP adds null and bounds
  checks to each of these and does not inline across the delegate. A full
  version would need a profile on the phone (simpleperf) to tell which of
  them matters.

### Frame cost

A hot-path mix of 18 cases (the static, instance, property and field cases,
`string_return`, the three GCOptimize struct cases, `class_object_arg`,
`overload_disjoint`, `overload_num_integer` and the five UnityEngine
`Vector3`, `position` and `Mathf.Max(float)` cases), mean ns per call on
the phone: **plain 437, GCM 461, descriptor 513**: the descriptor mode is
+52 ns (+11%) over GCM **[C]**. At 120 fps (8.33 ms per frame):

| calls per frame | plain | GCM | descriptor | descriptor - GCM | share of the frame |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1,000 | 0.44 ms | 0.46 ms | 0.51 ms | +0.05 ms | 0.6% |
| 10,000 | 4.37 ms | 4.61 ms | 5.13 ms | +0.52 ms | 6.2% |

On the emulator the same mix is 98, 102 and 115 ns (+0.13 ms per 10,000
calls, 1.6% of the frame). A game that makes 10,000 Lua -> C# calls per
frame on this phone already spends more than half of a 120 fps frame in the
bridge; the descriptor mode adds about half a millisecond to that, or
about 6% of the frame.

### Recommendation

**Go with changes, not with the plan as written.** The size gain stands
(-443 KB of APK at game size, about 70% of what GCM saved), but the phone
shows a fixed cost of about 42 ns per call over GCM (+11% over the mix),
3x the host's in nanoseconds, so the mode should not become GCM's
successor or the minimal preset's default:

1. **An opt-in size mode next to GCM, not its replacement**, with a
   per-type way back to GCM or plain wrappers (an attribute or a list in
   the configuration) for the types a game calls every frame. Both modes
   register through the same trampoline, so one assembly can mix them; the
   generator already decides per type. About +1 day on the 12 **[U]**.
2. **First, 2 days on the per-call overhead**, with this player bench on
   the phone as the gate before the rest of the work: a simpleperf profile,
   then the obvious candidates (the second delegate call into the type's
   switch, the buffer access, the per-argument switches; for example
   generated readers for the most common argument shapes). If the fixed
   cost does not come down to about 20 ns per call on the phone, the mode
   is still worth shipping as the opt-in size mode of point 1, for
   projects that bind many types and call few of them per frame **[U]**.
3. **Not "drop"**: the cost is confined to the types a project puts in
   that mode, it falls on calls that are already 4.5x slower on this phone
   than on the host, and for most projects the bound API is large while
   the per-frame hot set is small.

Total about 15 working days (12 + 1 for the per-type choice + 2 for the
overhead work) **[U]**.

The owner chose the 2 days of point 2 on 2026-10-29 (question 81, option b),
with the decision on the full mode after the first release; they are done, see
[Per-call overhead on the phone](#per-call-overhead-on-the-phone-2026-10-29).

### Not run

- High or Medium stripping, Development players, `armeabi-v7a`, x86_64.
- Other Lua versions (5.4.9, 5.3.6, LuaJIT); only Lua 5.5.1.
- iOS and WebGL players; any device but this phone and this emulator.
- The game-sized configuration (264 types) as a timing player: the bench
  binds 9 types. Call cost should not depend on the number of bound types
  (registration is per type and lazy) **[U]**; the spike's game players
  (`ultra/size/players`) have their own timing lines and were not run.
- The stack-buffer variant (`XLUA_SPIKE_DESC_STACK`), C# -> Lua calls
  (the descriptor mode does not change them), allocations in the players,
  and a CPU profile.
- Main's current runtime: the spike tree is from 2026-10-19, so changes to
  the translator since then (for example the bridge checks) are not in
  these numbers; they apply to all three modes alike.

## Per-call overhead on the phone (2026-10-29)

Owner's decision of 2026-10-29 (question 81, option b): a time-boxed
experiment of about 2 working days on the per-call overhead of generated
wrappers on the phone; the full descriptor mode is decided after the first
release, and if the work turns out complicated it stops and goes into the
roadmap in detail. The facts it started from (the section above): on the moto
g play 2024 the descriptor mode costs about +52 ns per call over GCM (about
42 ns fixed per call), and GCM itself about +24 ns over plain wrappers (+53
for `static_call`). The minimal preset implies GCM, and the descriptor mode
runs on GCM's trampoline, so a saving in GCM's entry helps every minimal
project now.

### Method

**Players** **[C]**: the player bench of 2026-10-28 (`Test/MobileSmoke/PlayerBench.cs`,
`size_report.py +pbench`), Unity 6000.3.21f1, release ARM64 IL2CPP,
Minimal stripping, Lua 5.5.1, with three additions on main (commit
`test(tests): profile mode and package suffix for the player bench`): the
APKs are profileable from the shell, a launch can run chosen cases only
(`pbench_profile`), and `XLUA_PBENCH_SUFFIX` gives the players of another
tree their own package, so before and after a change are installed side
by side. Main (`fd4a43ce`) in plain and GCM mode with this checkout's Lua
5.5.1 plugin; the spike tree of 2026-10-28 (`spike/descriptor-wrappers`
at `90c51ef2` plus the bench) in GCM and descriptor mode with the
spike's plugin (`ultra/plugins`, as on 2026-10-28). Every candidate change
is a player of its own, built from a copy of the tree with only that
change.

**Profiler: not used.** The plan was `simpleperf record --app` on the
profileable players. On this phone `perf_event_paranoid` is -1, and
simpleperf's own limit check (`environment.cpp`, `CheckPerfEventLimit`)
returns before it would set a property, but on Android 11 and later
simpleperf's `main` sets `security.perf_harden` to 0 whenever it runs from
the shell and the property is not 0 already (`main.cpp`,
`AndroidSecurityCheck`) **[C]** (AOSP `android14-release`; the phone's
property read 1 before and 0 after one `simpleperf --version` at
2026-10-28T23:39:30Z). That is a device setting, which the phone's rules
do not allow, so simpleperf was not run again and no profile was recorded.
About 70 s later the phone installed a system update and rebooted
(`reboot,ota` at 23:41:39Z, build `U1TFS34.100-35-14-1-21` to `-1-23`,
security patch 2026-08-01 to 2026-10-01), which reset the property: it read
1 in every later snapshot and at 02:25Z. Both are in the phone's log
(playground `projects/moto-g-play-2024/CHANGES.md`). Instead: the IL2CPP C++
of the players (`Library/Bee/artifacts/Android/il2cppOutput/cpp`) and
`llvm-objdump` of their `libil2cpp.so` with the symbols of
`libil2cpp.sym.so`, and players with one change each, timed on the phone.

**Timing** **[C]**: as on 2026-10-28, a launch runs 7 rounds of 100,000
calls per case and gives the median per case; launches rotate between the
APKs (15 s apart, Thermal Status 0 before and after each); the tables give
the median of the per-launch medians, 6 launches per APK for the
candidates, 8 for the final check and the spike. No Unity editor ran
during the sessions used here (the script checks before every launch and
held xlua-ng's Unity lock); a first attempt that other agents' builds kept
waiting was stopped and is not used.

### Where the time goes

**GCM over plain** (main, phone, `static_call` +57 ns, the mix +18 ns) **[C]**:

- **A full memory barrier per call.** `InternalGlobals.objectTranslatorPool`
  was `static volatile` (`Assets/XLua/Src/InternalGlobals.cs:58`), and
  IL2CPP compiles every read of a volatile field to the load and
  `il2cpp_codegen_memory_barrier()` (`ObjectTranslatorPool_get_Instance` in
  the generated `XLua__1.cpp`), which is `il2cpp::vm::Thread::FullMemoryBarrier`,
  a `dmb ish` (`libil2cpp/vm/Thread.cpp:938`; in the GCM player's
  `libil2cpp.so` at `0x49800c`). `ObjectTranslatorPool.Instance` runs on
  every `Find`: GCM's C# entry (`StaticLuaCallbacks.CSharpWrapperCallerImpl`)
  calls it for every call, and plain wrappers for every member that needs
  the translator. The field is set once in the type initializer and never
  written again.
- **The translator lookup where plain needs none.** A plain wrapper of a
  static method whose arguments and result are Lua types does not look the
  translator up (`Wrappers.cs`, `Method`: `FindTranslatorLine` only when the
  call needs it); GCM's entry needs the translator for its wrapper table on
  every call. This is why `static_call` paid +57 ns and the property and
  field cases, which look it up in both modes, about +30 (`instance_call`
  was within the launches' scatter).
- **The rest of the entry**, per call: the native trampoline reads the
  wrapper id and the argument count through two Lua API calls
  (`lua_tointegerx` on upvalue 1, `lua_gettop`; `build/xlua_core.c`,
  `csharp_function_wrapper_wrapper`) where the plain one reads one
  (`lua_tocfunction`); a C# frame more (`CSharpWrapperCallerImpl`, 16
  instructions on its path) with `Find` (`xlua_gl` through P/Invoke, the
  cache compare); `ObjectTranslator.CallCSharpWrapper`, an out-of-line call
  in another C++ file (18 instructions); and the delegate call itself, one
  indirect call: IL2CPP binds a closed delegate's `invoke_impl` straight to
  the method (`libil2cpp/vm/Type.cpp`, `SetClosedDelegateInvokeMethod`, and
  the generated `CSharpWrapper__ctor`). The wrappers' bodies are the same in
  both modes (GCM's have no `try`, which costs nothing until a throw).

**Candidates, one player each** (6 launches each, ns per call) **[C]**:

- **A**: `objectTranslatorPool` `static readonly` instead of `static volatile`;
- **B**: `Find` returns the cached translator without `xlua_gl` when the
  state passed is the cached main state (calls from the main thread);
- **C**: the GCM entry reads the pool field and the wrapper table directly
  (no property getter, no `CallCSharpWrapper`).

| case | plain | plain A | GCM | GCM A | GCM A+B+C | GCM - plain | A (plain) | A (GCM) | B (GCM) | B+C after A (GCM) | GCM A+B+C - plain A+B |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `static_call` | 143.5 | 143.8 | 200.1 | 191.3 | 188.4 | +56.6 | +0.3 | -8.8 | +1.5 | -3.0 | +41.3 |
| `instance_call` | 375.9 | 350.6 | 366.2 | 346.4 | 343.7 | -9.6 | -25.2 | -19.9 | -2.6 | -2.7 | -12.0 |
| `property_get` | 397.6 | 375.4 | 427.8 | 410.4 | 409.3 | +30.2 | -22.2 | -17.3 | -10.0 | -1.1 | +31.0 |
| `property_set` | 325.7 | 320.4 | 360.9 | 353.3 | 352.5 | +35.2 | -5.3 | -7.6 | -0.3 | -0.8 | +25.8 |
| `field_get` | 397.5 | 378.7 | 427.2 | 413.0 | 408.2 | +29.7 | -18.8 | -14.2 | -5.2 | -4.7 | +25.1 |
| `field_set` | 326.8 | 324.2 | 360.7 | 355.1 | 351.7 | +33.9 | -2.6 | -5.6 | +0.7 | -3.3 | +23.1 |
| `string_arg` | 398.2 | 396.3 | 439.2 | 437.9 | 431.4 | +41.0 | -2.0 | -1.4 | +0.7 | -6.5 | +33.6 |
| `string_return` | 292.7 | 290.3 | 334.4 | 332.8 | 329.7 | +41.8 | -2.4 | -1.7 | +1.1 | -3.1 | +37.9 |
| `struct_gcopt_pass` | 785.6 | 746.3 | 776.2 | 765.7 | 762.8 | -9.4 | -39.3 | -10.5 | +5.1 | -3.0 | +14.7 |
| `struct_boxed_pass` | 3027.3 | 2994.1 | 3167.1 | 2992.6 | 2966.5 | +139.8 | -33.2 | -174.5 | -49.8 | -26.1 | -32.6 |
| `struct_gcopt_new` | 730.4 | 716.3 | 742.4 | 734.3 | 727.1 | +12.0 | -14.1 | -8.0 | -3.5 | -7.2 | +11.6 |
| `struct_gcopt_field_get` | 458.5 | 428.8 | 456.7 | 449.8 | 441.5 | -1.8 | -29.7 | -6.9 | +1.3 | -8.3 | +15.2 |
| `struct_boxed_field_get` | 1036.2 | 1017.6 | 1125.5 | 1001.6 | 1001.7 | +89.2 | -18.6 | -123.8 | -28.9 | +0.1 | -36.0 |
| `object_return_cached` | 1431.5 | 1384.9 | 1411.0 | 1443.7 | 1424.8 | -20.4 | -46.6 | +32.6 | +9.8 | -18.8 | +7.7 |
| `unity_object_arg` | 527.9 | 511.2 | 517.8 | 521.0 | 518.7 | -10.1 | -16.7 | +3.2 | +11.2 | -2.3 | +14.2 |
| `class_object_arg` | 508.1 | 499.1 | 527.9 | 511.6 | 507.4 | +19.8 | -8.9 | -16.3 | +0.2 | -4.2 | +10.6 |
| `overload_disjoint` | 161.9 | 160.4 | 201.2 | 196.4 | 195.2 | +39.3 | -1.5 | -4.8 | +1.6 | -1.2 | +30.5 |
| `overload_num_integer` | 194.4 | 192.7 | 230.8 | 223.7 | 223.2 | +36.4 | -1.8 | -7.1 | -0.3 | -0.6 | +30.2 |
| `overload_num_float` | 201.0 | 198.8 | 238.9 | 230.1 | 227.9 | +37.9 | -2.2 | -8.8 | -4.2 | -2.2 | +29.5 |
| `overload_obj_class` | 1423.1 | 1430.7 | 1460.2 | 1435.5 | 1438.5 | +37.2 | +7.7 | -24.7 | +3.0 | +3.0 | +5.0 |
| `overload_obj_derived` | 1498.5 | 1506.1 | 1571.5 | 1616.0 | 1606.5 | +73.0 | +7.6 | +44.5 | +2.9 | -9.5 | +101.1 |
| `unity_vector3_new` | 675.4 | 664.2 | 686.1 | 680.7 | 674.5 | +10.7 | -11.2 | -5.4 | +0.1 | -6.2 | +9.7 |
| `unity_position_get` | 873.2 | 840.6 | 888.9 | 868.9 | 874.1 | +15.7 | -32.6 | -20.0 | -2.3 | +5.2 | +15.4 |
| `unity_position_set` | 576.5 | 539.8 | 563.5 | 560.1 | 564.6 | -13.0 | -36.7 | -3.4 | +6.1 | +4.5 | +17.0 |
| `unity_vector3_field_get` | 525.7 | 489.9 | 520.9 | 504.8 | 503.5 | -4.8 | -35.9 | -16.1 | +2.3 | -1.3 | +6.1 |
| `unity_mathf_max_int` | 298.9 | 289.5 | 302.5 | 300.9 | 296.8 | +3.7 | -9.4 | -1.6 | +1.1 | -4.1 | +8.6 |
| `unity_mathf_max_float` | 529.7 | 519.6 | 528.6 | 531.3 | 520.9 | -1.1 | -10.0 | +2.7 | +1.4 | -10.4 | +1.9 |
| `unity_instance_method` | 3910.1 | 4012.0 | 4200.6 | 4227.5 | 4220.0 | +290.5 | +101.9 | +26.9 | -9.0 | -7.5 | +207.0 |
| hot-path mix (18 cases) | **459.9** | **443.4** | **477.8** | **468.3** | **465.5** | **+17.9** | **-16.5** | **-9.5** | **-0.2** | **-2.9** | **+18.6** |

- **A is the win**: -9.5 ns over the GCM mix (-2.0%) and -16.5 ns over the
  plain mix (-3.6%). In GCM -1 to -20 ns for the single calls (`static_call`
  -9, `instance_call` -20, property and field gets -14 to -17, sets -6 to
  -8, strings -1 to -2), -124 and -175 ns for the boxed struct cases, which
  look the translator up more than once per call; in plain up to -25 ns for
  the instance members and -30 to -39 for the GCOptimize and `Vector3`
  struct cases. `static_call` in plain does not change (+0.3): it never
  looked the translator up. Two cases bound by allocation and the object
  cache got slower in GCM (`object_return_cached` +33, `overload_obj_derived`
  +45), and `unity_instance_method`, which goes through the reflection
  wrapper in every mode, moves by 100 to 300 ns between builds; see the
  build-to-build note under the spike results below.
- **B gives nothing measurable** (-0.2 ns over the mix): the `xlua_gl`
  P/Invoke is cheap, IL2CPP calls a cached function pointer. Not kept.
- **C adds about -3 ns** (B+C after A: -2.9 over the mix, lower in 19 of
  27 cases), at the edge of what one build can show; kept, as it only
  removes a call and a getter.
- What A and C leave of GCM's entry is structural: the extra C# frame,
  the translator lookup, the wrapper table, the delegate and the two
  native API calls, about +41 ns for `static_call` and +19 ns over the mix
  against plain with A (the last column; plain A+B is the plain player with
  A and B). Removing them means per-member native entry points again, which
  is what GCM gives up to save size.

**On main** (commit `perf(runtime): no memory barrier on the translator
lookup of every call`): A and C, built from the commit itself and timed
against the players of `fd4a43ce`, 8 launches each **[C]**:

| case | plain | plain new | GCM | GCM new | plain: new - old | GCM: new - old | GCM - plain, old | GCM - plain, new |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `static_call` | 143.8 | 143.9 | 201.1 | 190.7 | +0.1 | -10.4 | +57.3 | +46.8 |
| `instance_call` | 350.8 | 334.4 | 367.3 | 344.8 | -16.4 | -22.5 | +16.4 | +10.4 |
| `property_get` | 388.8 | 374.0 | 421.4 | 408.0 | -14.8 | -13.3 | +32.5 | +34.0 |
| `property_set` | 328.5 | 321.1 | 360.0 | 351.5 | -7.3 | -8.5 | +31.5 | +30.3 |
| `field_get` | 390.0 | 378.3 | 423.4 | 409.6 | -11.7 | -13.7 | +33.3 | +31.3 |
| `field_set` | 329.5 | 323.4 | 361.3 | 353.2 | -6.2 | -8.1 | +31.8 | +29.9 |
| `string_arg` | 393.3 | 396.4 | 438.9 | 427.2 | +3.1 | -11.6 | +45.6 | +30.9 |
| `string_return` | 292.8 | 292.1 | 334.1 | 328.5 | -0.7 | -5.6 | +41.3 | +36.4 |
| `struct_gcopt_pass` | 776.7 | 745.4 | 780.5 | 759.8 | -31.4 | -20.7 | +3.7 | +14.4 |
| `struct_boxed_pass` | 3022.0 | 3081.4 | 3100.4 | 3018.9 | +59.4 | -81.4 | +78.4 | -62.4 |
| `struct_gcopt_new` | 728.4 | 715.7 | 740.0 | 726.3 | -12.7 | -13.7 | +11.6 | +10.6 |
| `struct_gcopt_field_get` | 445.8 | 426.0 | 458.7 | 442.4 | -19.8 | -16.3 | +12.9 | +16.4 |
| `struct_boxed_field_get` | 1028.5 | 1083.3 | 1088.2 | 993.4 | +54.8 | -94.8 | +59.7 | -90.0 |
| `object_return_cached` | 1449.1 | 1432.9 | 1404.6 | 1427.5 | -16.2 | +22.9 | -44.5 | -5.4 |
| `unity_object_arg` | 527.1 | 518.9 | 520.5 | 537.0 | -8.2 | +16.4 | -6.6 | +18.0 |
| `class_object_arg` | 504.1 | 498.5 | 526.2 | 507.6 | -5.6 | -18.7 | +22.1 | +9.0 |
| `overload_disjoint` | 159.9 | 162.0 | 201.6 | 196.3 | +2.1 | -5.3 | +41.7 | +34.3 |
| `overload_num_integer` | 194.2 | 193.9 | 229.7 | 222.5 | -0.3 | -7.2 | +35.5 | +28.6 |
| `overload_num_float` | 201.3 | 198.3 | 233.6 | 226.9 | -3.0 | -6.6 | +32.2 | +28.7 |
| `overload_obj_class` | 1420.9 | 1440.5 | 1455.3 | 1425.3 | +19.6 | -29.9 | +34.3 | -15.1 |
| `overload_obj_derived` | 1492.2 | 1508.9 | 1564.7 | 1551.4 | +16.6 | -13.2 | +72.4 | +42.6 |
| `unity_vector3_new` | 674.9 | 668.9 | 686.6 | 672.2 | -6.0 | -14.4 | +11.7 | +3.3 |
| `unity_position_get` | 862.2 | 862.1 | 888.4 | 864.5 | -0.1 | -23.9 | +26.1 | +2.3 |
| `unity_position_set` | 560.0 | 540.5 | 565.5 | 548.7 | -19.5 | -16.8 | +5.5 | +8.2 |
| `unity_vector3_field_get` | 512.7 | 500.2 | 524.0 | 500.3 | -12.6 | -23.7 | +11.2 | +0.1 |
| `unity_mathf_max_int` | 299.1 | 287.5 | 302.5 | 295.1 | -11.6 | -7.4 | +3.4 | +7.6 |
| `unity_mathf_max_float` | 529.0 | 518.2 | 528.7 | 519.5 | -10.8 | -9.2 | -0.4 | +1.3 |
| `unity_instance_method` | 3937.7 | 4103.1 | 4180.0 | 4165.4 | +165.3 | -14.6 | +242.3 | +62.3 |
| hot-path mix (18 cases) | **454.0** | **444.4** | **477.7** | **463.7** | **-9.6** | **-14.0** | **+23.7** | **+19.3** |

- **GCM: -14.0 ns per call over the mix (-2.9%)**, -10 ns for
  `static_call`, -8 to -23 ns for the instance, property and field cases,
  -81 and -95 ns for the boxed struct cases. **Plain: -9.6 ns over the mix
  (-2.1%)**, -6 to -16 ns for the instance members, which look the
  translator up; `static_call` unchanged. GCM's lead over plain falls from
  +23.7 to +19.3 ns over the mix and from +57 to +47 ns for `static_call`.
- In plain the boxed struct cases came out +55 and +59 ns slower here and
  -19 and -33 ns faster in the candidate run (`plain A` above), and the
  reflection-bound `unity_instance_method` moves by 100 to 300 ns in both
  directions between runs; they are the cases with the widest scatter
  between launches.
- All of today's sessions ran on the phone's new system build (it installed
  an update and rebooted at 2026-10-28T23:40Z, below); compare absolute
  numbers with the 2026-10-28 tables with care. Plain `static_call` read
  143.8 ns today and 159.0 on 2026-10-28 (on the spike tree).

**Descriptor over GCM** (the spike, `static_call`) **[C]**: per call the core
(`Assets/XLua/Src/SpikeDescriptorCore.cs` on the branch) adds a second
dispatch and a general argument loop. In the player's `libil2cpp.so`
`DescGroup.Call` is 400 instructions with a prologue that saves 12
registers and `d8`; per argument it loads the kind, switches on it and
calls `SetL`/`SetD`/`SetO` (34 instructions each, a switch on the
argument's position, not inlined); it reaches the buffer through the type
(`owner.buffer[0]`), calls the type's switch method through a second
delegate (`__DescInvoke`, 384 instructions, a jump table) and switches over
the result kind. GCM's `_m_StaticAdd` is 45 instructions and reads its two
integers in line.

**On the spike only** (not merged; the copies are `spike2-src` and
`spike3-src` in `overhead-2026-10-29/` on the build disk) **[C]**:

- **D2**: `DescGroup.Call` with its hot path kept small: one overload goes
  straight to the argument loop, overload selection and the error texts are
  methods of their own (`Select`, `Fail`); the buffer is cached in the group;
  integer and float arguments are stored by index through a pointer into
  `l0..l7` and `d0..d7` (consecutive fields; IL2CPP keeps the declared
  order) instead of a switch on the position, and `SetL`/`SetD`/`SetO` are
  `AggressiveInlining`. `Call` shrinks from 400 to 304 instructions; `SetL`
  is a single store. Inlined, `SetO` made clang compute the eight object
  slot addresses before the loop on every call.
- **D3**: D2 with `SetO` not inlined: `Call` is 261 instructions.

GCM, the descriptor of 2026-10-28 and D2 and D3, interleaved, 8 launches
each, ns per call:

| case | GCM | descriptor | D2 | D3 | descriptor - GCM | D2 - GCM | D3 - GCM |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `static_call` | 207.5 | 255.1 | 242.2 | 251.7 | +47.6 | +34.7 | +44.3 |
| `instance_call` | 360.1 | 418.7 | 406.1 | 428.5 | +58.6 | +46.0 | +68.4 |
| `property_get` | 426.7 | 484.8 | 477.1 | 511.0 | +58.0 | +50.4 | +84.2 |
| `property_set` | 366.8 | 428.2 | 420.0 | 443.0 | +61.3 | +53.2 | +76.2 |
| `field_get` | 426.5 | 487.7 | 478.0 | 513.7 | +61.2 | +51.5 | +87.1 |
| `field_set` | 370.1 | 430.0 | 421.3 | 444.7 | +59.9 | +51.1 | +74.5 |
| `string_arg` | 440.8 | 501.2 | 514.5 | 516.6 | +60.4 | +73.7 | +75.9 |
| `string_return` | 357.0 | 424.2 | 427.9 | 435.6 | +67.2 | +70.9 | +78.6 |
| `struct_gcopt_pass` | 775.1 | 840.4 | 842.6 | 857.1 | +65.2 | +67.4 | +81.9 |
| `struct_boxed_pass` | 2906.2 | 2608.5 | 2553.7 | 2580.9 | -297.7 | -352.5 | -325.3 |
| `struct_gcopt_new` | 763.9 | 865.7 | 841.9 | 862.0 | +101.8 | +78.0 | +98.1 |
| `struct_gcopt_field_get` | 456.7 | 504.7 | 481.7 | 498.5 | +48.0 | +25.0 | +41.8 |
| `struct_boxed_field_get` | 896.3 | 499.0 | 499.4 | 512.9 | -397.3 | -397.0 | -383.4 |
| `object_return_cached` | 1434.9 | 1533.9 | 1561.7 | 1549.1 | +99.0 | +126.8 | +114.2 |
| `unity_object_arg` | 522.2 | 414.0 | 413.7 | 426.6 | -108.2 | -108.5 | -95.6 |
| `class_object_arg` | 515.9 | 349.4 | 344.6 | 357.1 | -166.5 | -171.4 | -158.8 |
| `overload_disjoint` | 208.6 | 257.4 | 256.4 | 264.3 | +48.8 | +47.7 | +55.6 |
| `overload_num_integer` | 187.7 | 235.9 | 233.9 | 241.5 | +48.2 | +46.2 | +53.8 |
| `overload_num_float` | 187.5 | 236.1 | 233.4 | 241.0 | +48.6 | +45.9 | +53.6 |
| `overload_obj_class` | 998.3 | 1072.6 | 1094.9 | 1078.5 | +74.3 | +96.7 | +80.2 |
| `overload_obj_derived` | 992.2 | 1085.6 | 1094.5 | 1077.4 | +93.4 | +102.3 | +85.2 |
| `unity_vector3_new` | 712.5 | 847.8 | 828.5 | 849.5 | +135.3 | +116.0 | +137.0 |
| `unity_position_get` | 875.6 | 982.2 | 973.5 | 1002.1 | +106.6 | +98.0 | +126.5 |
| `unity_position_set` | 543.5 | 664.0 | 657.4 | 679.2 | +120.6 | +113.9 | +135.7 |
| `unity_vector3_field_get` | 515.8 | 610.4 | 588.7 | 611.6 | +94.6 | +72.9 | +95.8 |
| `unity_mathf_max_int` | 213.4 | 283.3 | 275.0 | 282.3 | +69.9 | +61.6 | +68.9 |
| `unity_mathf_max_float` | 208.7 | 276.3 | 268.6 | 276.5 | +67.7 | +59.9 | +67.8 |
| `unity_instance_method` | 2896.3 | 2691.0 | 2773.9 | 2723.4 | -205.3 | -122.4 | -172.9 |
| hot-path mix (18 cases) | **459.9** | **520.2** | **510.6** | **529.3** | **+60.2** | **+50.6** | **+69.4** |

- **D2 takes 1 to 24 ns off the descriptor's fixed cost** (`static_call`
  -13, `instance_call` -13, properties and fields -8 to -10, the GCOptimize
  struct cases -19 to -24), -9.6 ns over the mix: the gap to GCM is now
  +50.6 ns over the mix (+11.0%) and +35 ns for `static_call`. String and
  object arguments got slower (+13, +28): the inlined `SetO`.
- **D3, the same code with one fewer inlined method, is slower than both**:
  +69.4 over the mix, +44 to +87 ns for the single calls. Its hot path has
  fewer instructions than D2's; what differs is code placement in the
  11 MB `libil2cpp.so`. **Between two builds that differ in one attribute,
  a call on this phone moves by 10 to 35 ns**, as much as the changes
  measured here. Changes of a few ns cannot be told apart in one build;
  they need several builds or a profile.
- **The fixed cost is structural.** What is left after D2 is the second
  dispatch (the closed `DescInvoker` delegate into the type's switch), the
  general argument loop (load the kind, switch, store into the buffer; the
  stub loads it back), the result switch, and a frame that saves 10
  registers and `d8`. GCM's per-member method reads its arguments into
  registers and calls the member directly. Getting to about 20 ns per call
  (the target the 2026-10-28 recommendation set) needs a different design,
  for example generated readers per argument shape shared between members
  of the same signature, or a stub that reads its own arguments; both give
  back part of the size the mode saves (about 100 bytes of APK per Lua
  function against GCM). Not attempted: it is more than the 2 days, so this
  stops here, as the owner asked, and the plan is in the
  [backlog](../design/backlog.md) and [ROADMAP.md](../../ROADMAP.md).
- The descriptor mode stays faster where it skips work GCM does: boxed
  structs (-298 to -397 ns) and class or `UnityEngine.Object` arguments
  (-108 to -171 ns), as on 2026-10-28.
- The profile mode itself ran on the phone once (GCM with A+B+C:
  `XLUA_PBENCH_PROFILE_START static_call,instance_call 5`, then 190.0 and
  342.3 ns per call over 9.4 million calls each, matching the timing
  rounds).

### Not run

- A CPU profile (simpleperf; see Method). The emulator could take one
  without the phone's rule, but the emulator does not predict the phone
  (the section above).
- The final change on Lua versions other than 5.5.1 in a player; the host
  suites ran it on 5.5.1 and LuaJIT (below).
- Other cases of volatile reads on hot paths: `LuaIndexes.registryIndex`
  (`Assets/XLua/Core/LuaTypes.cs:65`, read where the bridge pushes Lua
  references and new objects) is `static volatile` too and set per env;
  not measured.

### Tests of the change on main

- `Test/Native/run_managed.py --source-gen --source-gen-minimize
  --source-gen-fallback` on macOS arm64 (Mono 6.14.1) with plugins built
  from this checkout: Lua 5.5.1 and LuaJIT 2.1, each legacy and minimal
  (`XLUA_PRESET=minimal`, `--define XLUA_PRESET_MINIMAL`): 523 tests in
  each of two modes, in every pass, on all four **[C]**.
- `Tools/build_source_generator.py --test`: 51 tests **[C]** (the generator
  did not change).
- The phone players above (plain and GCM, Lua 5.5.1, Minimal stripping)
  ran the bench's cases without an error **[C]**.
- Not run: the Unity editor suite, `sanitize.sh`, the native checks (the
  native code did not change), Lua 5.4.9 and 5.3.6, iOS, WebGL.

### Reproducing

Scripts and raw logs are in `overhead-2026-10-29/` on the build disk:
`build_players.sh` (size_report.py with `XLUA_PBENCH_SUFFIX` and a source
tree per candidate), `pbench_run.py` and `phone_session.py` (interleaved
launches, the phone's snapshot and log), `aggregate.py` and `mdtable.py`
(the tables), `disas.py` and `cppfn.py` (instruction counts and the
IL2CPP C++ of a method), `realflock.py` (a lock on the file the lock path
names now: `/usr/bin/lockf` without `-k` removes the lock file when its
command ends, and a process that waited on the removed file then holds a
lock no newcomer sees, which let other agents' Unity builds run during
this session's phone runs until the scripts used it).

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
   differ here. (Measured on 2026-10-28: 28 to 156 ns over GCM on the
   phone; the recommendation changed to an opt-in size mode, see
   [the phone section](#recommendation-1).) It uses the GCM trampoline, so it needs `GEN_CODE_MINIMIZE`
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

The players of 2026-10-28: on a copy of the spike tree, add
`Test/MobileSmoke/PlayerBench.cs` and the `+pbench` variant from main
(plus `ng-5.5+pkg+sg+gcm+desc+pbench` in a `pbench-minimal` configuration),
then `size_report.py run --platforms android --configs pbench-minimal
--plugins PLUGINS` with `XLUA_SIZE_TMPDIR` set to a short directory, install
the three APKs and launch them in rotating order, reading the `XLUA_PBENCH`
lines from `adb logcat -s Unity`. The device script and the raw logs of this
run are in `descriptor-phone-2026-10-28/` on the build disk
(`scripts/pbench_device.py`, `scripts/aggregate.py`, `scripts/tables.py`,
`phone/`, `emu/`).
