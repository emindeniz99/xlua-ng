# xlua-ng against MoonSharp-ng on a phone (2026-10-29)

The owner asked for a comparison of his MoonSharp fork ("MoonSharp-ng",
[github.com/emindeniz99/moonsharp](https://github.com/emindeniz99/moonsharp))
with xlua-ng, as a Unity user would use each. This note measures both in
release IL2CPP players on the test phone and on the `xlua16k` emulator, with
two workloads: the pure Lua set of the fork's own
[`docs/comparison-xlua-ng.md`](https://github.com/emindeniz99/moonsharp/blob/master/docs/comparison-xlua-ng.md),
and the C# bridge cases of [benchmark-vs-puerts-zlua.md](benchmark-vs-puerts-zlua.md)
(zlua's case list). It also repeats the pure Lua set on this Mac under .NET
and Mono, and compares APK sizes.

Marks: **[C]** confirmed here by a build, a run or the binary;
**[S]** secondary (the fork's documentation or code comments);
**[U]** unconfirmed (an estimate or an explanation that was not profiled).

## Summary

- **Pure Lua on the phone: MoonSharp-ng is 4.8 times slower** than xlua-ng
  (geometric mean of 18 workloads; from 0.8 times for `string_concat` to 13.8
  times for `table_array_insert`). Loops, calls and table access cost it 6 to
  14 times as much; work that spends its time in library code (string
  formatting and patterns, `table.concat`, `table.sort`) 1.5 to 3.6 times.
  The emulator gives the same picture (4.6 times). **[C]**
- **On the Mac the gap is smaller: 2.9 times under .NET 8, 8.0 times under
  Mono.** The fork's earlier desktop comparison of upstream MoonSharp gave
  11.3 times (Linux, .NET 8). IL2CPP on the phone sits between the two
  managed runtimes, and the fork's work since then shows. **[C]**
- **The C# bridge on the phone: MoonSharp-ng is 1.5 to 5.4 times slower for
  Lua → C#** (group geometric means: strings 1.5, instance properties 3.1,
  instance calls 4.0, Vector3 and structs 4.4, objects 4.3, static members
  5.4), **and 2.1 to 12.9 times slower for C# → Lua** (strings 2.1, numbers
  7.8, Vector3 and structs 7.6, objects 12.9). **[C]**
- **One case is MoonSharp-ng's: C# reading a Lua table field** costs it 93 ns
  against xlua-ng's 1.29 µs (14 times faster): MoonSharp's tables are C#
  objects, xlua-ng's take several P/Invoke calls. **[C]**
- **Allocations:** MoonSharp-ng allocates managed memory where xlua-ng does
  not: about 27 bytes per property or field read, 97 bytes per Vector3 or
  struct value, 68 bytes per class result, and 340 to 430 bytes per C# → Lua
  call with three Vector3, struct or class arguments. xlua-ng allocates in
  Lua's own heap instead for structs (52 bytes per Vector3 userdata), and
  more than MoonSharp-ng for strings crossing the bridge (a new C# string per
  Lua string). Numbers and plain calls allocate nothing in either. **[C]**
- **Size over an empty project:** MoonSharp-ng adds 1.37 MB of APK, all of it
  managed code and metadata (`libil2cpp.so` +3.93 MB uncompressed, no native
  library). xlua-ng strict minimal adds 0.58 MB, legacy 1.60 to 1.62 MB
  (1.11 MB of which is the UnityWebRequest and TLS modules that legacy's
  StreamingAssets searcher keeps in `libunity.so`). **[C]**
- **Read the numbers as "a C# interpreter against a native VM".** MoonSharp-ng
  runs Lua 5.2 semantics in safe C# and needs no native plugin; xlua-ng runs
  Lua 5.5.1 in C. The comparison is for choosing between them, not a verdict
  on either project's code.

## Libraries and versions

| | xlua-ng | MoonSharp-ng |
| --- | --- | --- |
| Version | main `7c2037fd` (2026-10-29), merged into this branch at `18b7d077` with the bench changes; the players were built from `18b7d077` | `master` `75864ce` (2026-10-29 06:11 UTC), "Merge branch 'next'" |
| Lua | 5.5.1 in `libxlua.so`, built here by `build/make_android_lua55.sh` (NDK r30), legacy and `XLUA_VARIANT=minimal XLUA_PRESET=minimal` | MoonSharp 3.0.0.0, Lua 5.2 semantics, interpreter in C# |
| Package | xlua-ng's UPM package from `Tools/make_upm_package.py`, with the source generator | `org.moonsharp.moonsharp` staged by the fork's `tools/upm/stage-local-package.sh` (version string `3.0.0-ng75864ce`): the interpreter sources in `Runtime/`, `link.xml` from `ILLink.Descriptors.xml`, and `MoonSharp.SourceGenerator.dll` in `Runtime/Analyzers` with the `RoslynAnalyzer` label |
| Binding in the player | C# wrappers from xlua-ng's source generator; calls through P/Invoke into `libxlua.so` | userdata descriptors from MoonSharp-ng's source generator (one `switch` per type), typed delegate factories; everything managed |
| Configurations | legacy plain wrappers; legacy GCM (`GEN_CODE_MINIMIZE`); strict minimal (the minimal preset, generated bindings, no reflection fallback) | the source generator, its fastest documented IL2CPP setup ([S]: the fork's `docs/source-generator.md`, "It matters most where MoonSharp cannot compile its fast paths at run time: IL2CPP ...") |

The fork was cloned at `origin/master` into the build disk and built from
there; its own working copy was not touched. Its package's Editor code is
the source generator alone; nothing else of the fork ran in the editor. **[C]**

## Method

**Players** **[C]**: Unity 6000.3.21f1, Android, IL2CPP, ARM64 only, release,
Minimal managed stripping, IL2CPP code generation "Faster runtime", C++
configuration Release, built by `Test/MobileSmoke/SmokeBuild.cs` as in
[benchmark-vs-puerts-zlua.md](benchmark-vs-puerts-zlua.md#method).

| player | built by | package |
| --- | --- | --- |
| empty project (size reference) | `size_report.py --configs vsbench`, variant `baseline` | `org.xluang.smoke` |
| xlua-ng plain | same, `ng-5.5+pkg+sg+vsbench` | `org.xluang.vs.plain` |
| xlua-ng GCM | same, `ng-5.5+pkg+sg+gcm+vsbench` | `org.xluang.vs.gcm` |
| xlua-ng strict minimal | same, `ng-5.5+minimal+strict+vsbench` | `org.xluang.vs.minimal` |
| MoonSharp-ng | `vs_bench.py build --libs moonsharp --moonsharp <staged package>` | `org.xluang.vs.moonsharp` |

**Bindings, as each library's users configure them** **[C]**:

- **xlua-ng:** as in the earlier note: `[LuaCallCSharp]` for the four test
  types and `UnityEngine.Vector3`, `[GCOptimize]` on `TestStruct`,
  `[CSharpCallLua]` for every delegate type.
- **MoonSharp-ng** (`Test/MobileSmoke/VsBench/VsBenchMoonSharp.cs`): the same
  types and delegate types in `[assembly: MoonSharpUserDataTypes(...)]`, and
  `GeneratedUserData.RegisterAll()` once before the first `Script`, as the
  fork's `docs/source-generator.md` says. The types reach Lua as static
  userdata in globals (`UserData.CreateStatic<T>()`). A MoonSharp type has no
  call metamethod; a constructor is the static member `__new`, so the
  prelude keeps `TC.__new` as `TC`, and `TC(1, 2, 3)` calls it. Lua
  functions become delegates with `Closure.ToDelegate<T>()`; the generated
  factories make them typed calls (`Closure.Call<T1..T4, TResult>`).
  `new Script()` with the default modules.
- **The generator's warnings:** it reports MSUD001 for 11 members it binds by
  reflection: `Vector3`'s setters and mutating methods, and `TestStruct`'s
  field setters, because generated code cannot change a boxed struct in
  place. The bench calls none of them. **[C]**

**Workloads** **[C]**: `Test/MobileSmoke/VsBench/VsBench.cs`, the same Lua text
for every library apart from the prelude that names the types.

- **The bridge cases:** the 38 cases of
  [benchmark-vs-puerts-zlua.md](benchmark-vs-puerts-zlua.md#case-mapping)
  plus `cs.table.get`. Since that note, `TestTargets.SampleClass` is
  initialized before `Instance`, so `lua2cs.instance.prop.get.class` now
  returns an object (it pushed nil on 2026-10-28). Method unchanged: 7 rounds
  per launch, each running every case once with 100,000 operations after a
  full Lua and C# collection; a case's value per launch is the median of its
  rounds.
- **The pure Lua set:** the 18 workloads of the fork's comparison, with its
  Lua sources and iteration counts (those of `Test/Native/flag_bench.c`, as
  the fork's `src/DevTools/Benchmarks/LuaWorkloads.cs` copies them).
  `table_sort` is that file's `table_sort_52`, which copies with a loop
  because Lua 5.2 has no `table.move`; both libraries run that variant. C#
  calls the workload function once with the full count; 5 rounds per launch,
  each running every workload once after both collections; ns per
  iteration, the median of the rounds. Each result is checked against the
  value in `LuaWorkloads.cs` (`method_call`, which depends on earlier runs,
  and `table_sort`, whose seed arithmetic differs between doubles and 5.3+
  integers, are reported but not checked).
- **The allocation pass,** after the timing: each case once more with fewer
  operations (10,000 per bridge case, a hundredth of the iterations per
  workload), with both collectors stopped (`GarbageCollector.GCMode =
  Disabled`, `collectgarbage('stop')`). Managed bytes come from
  `GC.GetTotalMemory(false)` before and after, which grows by what was
  allocated, rounded to Boehm's size classes; IL2CPP has no per-thread
  counter (as in `Test/MobileSmoke/AllocProbe.cs`). Lua bytes come from
  `collectgarbage('count')`. MoonSharp-ng's `collectgarbage('count')` reports
  the managed heap, so only its managed column is shown.

**Launches** **[C]**:

- **Phone:** the four players installed side by side and launched in
  rotating order, 8 launches each, 15 s apart; the xlua-ng launches took 53
  to 56 s, the MoonSharp-ng ones 183 to 189 s.
- **Emulator:** the same, 10 s apart. The emulator process ended by itself at
  07:54:33Z, in the seventh round (its log ends without an error message; the host was short
  of memory, with 10 GB of swap in use, from other jobs **[U]**). The tables
  use the 27 launches before it: 7 per player, 6 for GCM.
- **Values:** the median of the per-launch medians, and in parentheses the
  minimum of them; ns per operation (bridge) or per iteration (pure Lua),
  with the Lua or C# loop included.
- **Device settings:** nothing was pinned or changed. The players keep the
  screen on with `Screen.sleepTimeout` while in front.

**Devices**:

- **Phone:** moto g play 2024 (XT2413V), Snapdragon 680 (4x Cortex-A73,
  4x Cortex-A53), Android 14, 4 KB pages. Thermal status 0 before and after
  every launch; all 32 launches finished. The device log is
  `vs-moonsharp-2026-10-29/phone/` on the build disk.
- **Emulator:** `xlua16k` (Android 16, API 36, 16 KB pages, arm64, 4 cores)
  on the M2 Pro host, run at the same time as the phone.

**Checks** **[C]**: every launch printed the same check line in all four
players: the values the set cases left in C# (42, Vector3 (1, 2, 3), the
string and the class property), and one call of each C# → Lua delegate
(int, a long of 2^40, string, Vector3, the same class instance, struct).
Every checked pure Lua result matched `LuaWorkloads.cs`; `table_sort` gave
486000 in xlua-ng and 960000 in MoonSharp-ng, as the fork's note on doubles
predicts, and `method_call` 5100000 in all four.

## Pure Lua on the phone **[C]**

| workload | iterations | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | MoonSharp-ng | MoonSharp-ng / xlua-ng plain |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 4M | 5.1 (5.0) | 5.1 (5.1) | 5.1 (5.1) | 47.8 (47.8) | 9.4x |
| `function_call` | 1M | 48.8 (48.7) | 48.8 (48.7) | 48.4 (48.3) | 524.9 (508.0) | 10.8x |
| `method_call` | 1M | 109.5 (108.5) | 109.6 (108.2) | 107.6 (106.2) | 863.0 (859.9) | 7.9x |
| `table_array_insert` | 1M | 29.4 (29.1) | 29.4 (28.9) | 29.0 (28.7) | 406.1 (402.8) | 13.8x |
| `table_array_read` | 1M | 41.7 (41.7) | 41.7 (41.7) | 42.5 (42.4) | 362.0 (361.7) | 8.7x |
| `table_hash_set_get` | 1M | 86.1 (84.9) | 86.4 (85.4) | 85.5 (84.5) | 889.7 (881.9) | 10.3x |
| `string_concat` | 500K | 1,137.9 (1,133.3) | 1,137.8 (1,136.1) | 1,124.7 (1,120.2) | 921.8 (913.9) | 0.8x |
| `string_format` | 500K | 1,130.2 (1,126.4) | 1,126.7 (1,124.6) | 1,170.4 (1,104.3) | 1,750.2 (1,742.0) | 1.5x |
| `closure_create_call` | 1M | 651.7 (649.5) | 652.7 (649.4) | 644.8 (643.5) | 1,142.5 (1,129.0) | 1.8x |
| `int_math` | 2M | 26.8 (26.7) | 26.8 (26.7) | 26.9 (26.8) | 197.1 (197.0) | 7.4x |
| `fib_recursive` | 20K | 24,714 (24,680) | 24,679 (24,545) | 24,734 (24,646) | 221,743 (221,247) | 9.0x |
| `float_math` | 2M | 57.1 (57.0) | 57.1 (57.0) | 55.0 (55.0) | 399.0 (398.8) | 7.0x |
| `string_pattern` | 100K | 6,422.8 (6,414.0) | 6,435.2 (6,420.4) | 6,449.3 (6,441.1) | 21,710 (21,653) | 3.4x |
| `table_sort` | 2K | 118,706 (118,585) | 118,815 (118,701) | 113,893 (113,612) | 427,604 (426,787) | 3.6x |
| `table_concat` | 50K | 2,801.2 (2,793.7) | 2,801.9 (2,798.5) | 2,728.5 (2,725.4) | 5,035.3 (4,983.3) | 1.8x |
| `coroutine_switch` | 500K | 308.8 (306.2) | 308.9 (306.4) | 302.2 (299.5) | 1,500.8 (1,490.0) | 4.9x |
| `gc_small_tables` | 500K | 972.1 (970.2) | 973.2 (971.0) | 983.7 (981.3) | 2,607.1 (2,579.2) | 2.7x |
| `oop_inherit` | 1M | 122.9 (119.1) | 121.1 (119.1) | 119.7 (117.4) | 735.2 (718.5) | 6.0x |
| geometric mean of the ratios | | | | | | 4.8x |

## The bridge on the phone **[C]**

| case | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | MoonSharp-ng | MoonSharp-ng / xlua-ng plain |
| --- | ---: | ---: | ---: | ---: | ---: |
| `lua2cs.static.method.0arg.void` | 74.1 (73.8) | 107.1 (105.7) | 102.7 (102.0) | 498.2 (497.1) | 6.72x |
| `lua2cs.static.method.0arg.ret` | 84.3 (83.8) | 136.7 (133.5) | 132.1 (127.0) | 791.9 (791.1) | 9.39x |
| `lua2cs.static.prop.get.int` | 226.1 (224.2) | 259.8 (258.0) | 258.8 (253.5) | 1,059.2 (1,054.9) | 4.68x |
| `lua2cs.static.field.set.int` | 240.7 (236.5) | 293.1 (287.8) | 284.1 (282.6) | 664.9 (661.6) | 2.76x |
| `lua2cs.ctor.class.3int` | 1,582.4 (1,556.7) | 1,632.3 (1,593.2) | 1,628.9 (1,573.3) | 4,271.7 (4,239.8) | 2.70x |
| `lua2cs.ctor.Vector3.full` | 598.2 (596.1) | 608.0 (606.1) | 598.8 (597.4) | 4,553.8 (4,527.5) | 7.61x |
| `lua2cs.instance.method.0arg.void` | 272.8 (268.1) | 284.0 (283.3) | 285.0 (281.4) | 549.7 (546.6) | 2.02x |
| `lua2cs.instance.method.3arg.ret` | 381.2 (373.9) | 382.5 (381.8) | 388.1 (384.6) | 1,689.6 (1,684.2) | 4.43x |
| `lua2cs.instance.method.6arg.ret` | 480.2 (479.0) | 487.0 (485.4) | 487.7 (483.7) | 2,510.2 (2,503.2) | 5.23x |
| `lua2cs.instance.prop.get.int` | 370.5 (369.6) | 391.9 (391.6) | 392.5 (385.6) | 1,026.1 (998.4) | 2.77x |
| `lua2cs.instance.prop.set.int` | 322.4 (319.7) | 346.7 (346.4) | 342.1 (340.3) | 632.0 (618.1) | 1.96x |
| `lua2cs.instance.field.get.int` | 370.0 (367.6) | 394.4 (391.2) | 398.9 (386.7) | 1,007.2 (1,003.3) | 2.72x |
| `lua2cs.instance.field.set.int` | 325.6 (319.2) | 346.9 (346.3) | 343.1 (340.0) | 628.8 (627.5) | 1.93x |
| `lua2cs.instance.method.long.3arg.ret` | 388.8 (377.1) | 387.8 (386.4) | 386.7 (381.9) | 1,812.2 (1,800.7) | 4.66x |
| `lua2cs.instance.method.double.3arg.ret` | 347.6 (334.9) | 350.1 (349.2) | 357.6 (349.1) | 1,607.1 (1,605.0) | 4.62x |
| `lua2cs.instance.method.object.3arg.ret` | 1,462.0 (1,443.7) | 1,455.0 (1,438.7) | 1,474.0 (1,440.1) | 4,541.4 (4,525.8) | 3.11x |
| `lua2cs.instance.method.string.3arg.ret` | 1,200.0 (1,182.4) | 1,226.2 (1,208.2) | 1,251.0 (1,236.4) | 1,717.7 (1,579.2) | 1.43x |
| `lua2cs.instance.prop.get.string` | 574.9 (565.1) | 632.1 (618.9) | 604.1 (591.7) | 1,053.0 (1,039.5) | 1.83x |
| `lua2cs.instance.prop.set.string` | 584.4 (580.5) | 601.5 (597.3) | 594.2 (587.5) | 784.7 (751.8) | 1.34x |
| `lua2cs.instance.method.Vector3.3arg.ret` | 1,242.6 (1,226.1) | 1,275.6 (1,264.7) | 1,251.6 (1,230.4) | 5,534.8 (5,445.6) | 4.45x |
| `lua2cs.instance.prop.get.Vector3` | 777.1 (767.0) | 789.0 (780.7) | 785.0 (765.3) | 3,753.5 (3,718.0) | 4.83x |
| `lua2cs.instance.prop.set.Vector3` | 488.2 (483.9) | 495.6 (491.9) | 510.2 (495.4) | 1,059.1 (1,026.4) | 2.17x |
| `lua2cs.instance.field.get.Vector3` | 782.1 (773.3) | 794.1 (785.4) | 783.5 (771.0) | 3,750.9 (3,727.9) | 4.80x |
| `lua2cs.instance.prop.get.enum` | 446.7 (443.3) | 475.5 (472.5) | 463.9 (461.3) | 4,140.6 (4,107.4) | 9.27x |
| `lua2cs.instance.method.class.3arg.ret` | 879.7 (868.2) | 887.9 (880.3) | 885.0 (878.0) | 5,178.7 (5,134.7) | 5.89x |
| `lua2cs.instance.prop.get.class` | 523.1 (515.5) | 534.5 (530.0) | 528.3 (525.3) | 3,549.5 (3,524.4) | 6.79x |
| `lua2cs.instance.method.struct.3arg.ret` | 1,339.4 (1,328.0) | 1,361.2 (1,346.0) | 1,339.1 (1,323.0) | 5,797.6 (5,738.6) | 4.33x |
| `lua.loop` | 7.0 (7.0) | 7.0 (7.0) | 7.2 (7.2) | 53.5 (53.4) | 7.64x |
| `lua.table.rw` | 36.3 (35.9) | 36.2 (35.9) | 35.0 (34.8) | 333.8 (329.9) | 9.20x |
| `cs2lua.int.arity0.void` | 185.1 (179.6) | 197.6 (181.8) | 192.2 (177.9) | 1,042.6 (1,038.7) | 5.63x |
| `cs2lua.int.3arg.void` | 206.7 (205.5) | 213.3 (211.3) | 202.5 (197.7) | 1,971.7 (1,968.4) | 9.54x |
| `cs2lua.int.3arg.ret` | 244.4 (242.1) | 241.8 (241.0) | 238.7 (236.7) | 2,018.0 (2,013.8) | 8.26x |
| `cs2lua.long.3arg.ret` | 241.2 (240.2) | 243.5 (243.2) | 236.7 (234.3) | 1,933.8 (1,930.0) | 8.02x |
| `cs2lua.double.3arg.ret` | 226.7 (225.6) | 224.6 (223.6) | 225.8 (224.1) | 1,893.7 (1,888.2) | 8.35x |
| `cs2lua.object.3arg.ret` | 707.5 (701.5) | 700.7 (693.4) | 697.2 (688.8) | 7,320.4 (7,307.2) | 10.35x |
| `cs2lua.string.3arg.ret` | 1,000.9 (976.6) | 982.2 (977.0) | 998.5 (983.1) | 2,146.0 (1,991.6) | 2.14x |
| `cs2lua.Vector3.3arg.ret` | 1,576.2 (1,558.8) | 1,568.2 (1,556.7) | 1,587.5 (1,571.0) | 12,097 (12,011) | 7.67x |
| `cs2lua.class.3arg.ret` | 716.1 (712.2) | 714.2 (712.5) | 684.3 (683.4) | 11,509 (11,465) | 16.07x |
| `cs2lua.struct.3arg.ret` | 1,672.7 (1,665.0) | 1,682.0 (1,671.2) | 1,684.9 (1,659.5) | 12,532 (12,442) | 7.49x |
| `cs.table.get` | 1,294.2 (1,281.5) | 1,261.8 (1,238.8) | 1,220.0 (1,201.0) | 92.8 (92.5) | 0.07x |

By group, the geometric mean of the cases' medians and its ratio to xlua-ng
plain:

| group | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | MoonSharp-ng |
| --- | ---: | ---: | ---: | ---: |
| Lua → C#: static calls, property and field (int) | 135.8 (1.00x) | 182.7 (1.35x) | 177.7 (1.31x) | 726.0 (5.35x) |
| Lua → C#: instance calls (int, long, double) | 368.0 (1.00x) | 372.6 (1.01x) | 375.4 (1.02x) | 1,466.8 (3.99x) |
| Lua → C#: instance properties and fields (int, enum) | 364.4 (1.00x) | 388.4 (1.07x) | 385.6 (1.06x) | 1,112.0 (3.05x) |
| Lua → C#: strings | 738.7 (1.00x) | 775.4 (1.05x) | 765.8 (1.04x) | 1,123.8 (1.52x) |
| Lua → C#: Vector3 and struct | 816.1 (1.00x) | 830.4 (1.02x) | 824.8 (1.01x) | 3,600.6 (4.41x) |
| Lua → C#: objects (new class, object and class arguments, class property) | 1,015.8 (1.00x) | 1,030.4 (1.01x) | 1,029.3 (1.01x) | 4,345.5 (4.28x) |
| C# → Lua: int, long, double | 219.7 (1.00x) | 223.5 (1.02x) | 218.4 (0.99x) | 1,723.1 (7.84x) |
| C# → Lua: string | 1,000.9 (1.00x) | 982.2 (0.98x) | 998.5 (1.00x) | 2,146.0 (2.14x) |
| C# → Lua: Vector3 and struct | 1,623.7 (1.00x) | 1,624.1 (1.00x) | 1,635.5 (1.01x) | 12,313 (7.58x) |
| C# → Lua: object and class | 711.8 (1.00x) | 707.4 (0.99x) | 690.7 (0.97x) | 9,178.8 (12.90x) |
| Lua alone (loop, table) | 15.9 (1.00x) | 15.9 (1.00x) | 15.9 (1.00x) | 133.6 (8.38x) |
| C# reads a Lua table field | 1,294.2 (1.00x) | 1,261.8 (0.97x) | 1,220.0 (0.94x) | 92.8 (0.07x) |
| Pure Lua: the 18 workloads | 352.8 (1.00x) | 352.7 (1.00x) | 350.0 (0.99x) | 1,686.9 (4.78x) |

## Allocations per operation **[C]**

Bytes per operation with both collectors stopped, the median of the phone's
launches (managed / Lua heap for xlua-ng, managed for MoonSharp-ng). The
emulator gave the same bytes to within a few percent.

| case | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | MoonSharp-ng |
| --- | ---: | ---: | ---: | ---: |
| `lua2cs.static.method.0arg.void` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.static.method.0arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.static.prop.get.int` | 0 / 0 | 0 / 0 | 0 / 0 | 27 |
| `lua2cs.static.field.set.int` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.ctor.class.3int` | 45 / 36 | 46 / 36 | 46 / 36 | 112 |
| `lua2cs.ctor.Vector3.full` | 0 / 52 | 0 / 52 | 0 / 52 | 97 |
| `lua2cs.instance.method.0arg.void` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.instance.method.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.instance.method.6arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.instance.prop.get.int` | 0 / 0 | 0 / 0 | 0 / 0 | 27 |
| `lua2cs.instance.prop.set.int` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.instance.field.get.int` | 0 / 0 | 0 / 0 | 0 / 0 | 27 |
| `lua2cs.instance.field.set.int` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.instance.method.long.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 27 |
| `lua2cs.instance.method.double.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua2cs.instance.method.object.3arg.ret` | 96 / 0 | 96 / 0 | 95 / 0 | 119 |
| `lua2cs.instance.method.string.3arg.ret` | 141 / 0 | 142 / 0 | 142 / 0 | 0 |
| `lua2cs.instance.prop.get.string` | 0 / 0 | 0 / 0 | 0 / 0 | 27 |
| `lua2cs.instance.prop.set.string` | 45 / 0 | 46 / 0 | 46 / 0 | 27 |
| `lua2cs.instance.method.Vector3.3arg.ret` | 0 / 52 | 0 / 52 | 0 / 52 | 97 |
| `lua2cs.instance.prop.get.Vector3` | 0 / 52 | 0 / 52 | 0 / 52 | 97 |
| `lua2cs.instance.prop.set.Vector3` | 0 / 0 | 0 / 0 | 0 / 0 | 27 |
| `lua2cs.instance.field.get.Vector3` | 0 / 52 | 0 / 52 | 0 / 52 | 97 |
| `lua2cs.instance.prop.get.enum` | 0 / 0 | 0 / 0 | 0 / 0 | 28 |
| `lua2cs.instance.method.class.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 68 |
| `lua2cs.instance.prop.get.class` | 0 / 0 | 0 / 0 | 0 / 0 | 68 |
| `lua2cs.instance.method.struct.3arg.ret` | 0 / 52 | 0 / 52 | 0 / 52 | 97 |
| `lua.loop` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `lua.table.rw` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `cs2lua.int.arity0.void` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `cs2lua.int.3arg.void` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `cs2lua.int.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `cs2lua.long.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `cs2lua.double.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `cs2lua.object.3arg.ret` | 32 / 0 | 32 / 0 | 31 / 0 | 152 |
| `cs2lua.string.3arg.ret` | 45 / 0 | 46 / 0 | 45 / 0 | 0 |
| `cs2lua.Vector3.3arg.ret` | 0 / 156 | 0 / 156 | 0 / 156 | 433 |
| `cs2lua.class.3arg.ret` | 0 / 0 | 0 / 0 | 0 / 0 | 341 |
| `cs2lua.struct.3arg.ret` | 0 / 156 | 0 / 156 | 0 / 156 | 433 |
| `cs.table.get` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.empty_loop` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.function_call` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.method_call` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.table_array_insert` | 0 / 15 | 0 / 15 | 0 / 15 | 82 |
| `pure.table_array_read` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.table_hash_set_get` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.string_concat` | 0 / 84 | 0 / 84 | 0 / 84 | 88 |
| `pure.string_format` | 0 / 38 | 0 / 38 | 0 / 38 | 45 |
| `pure.closure_create_call` | 0 / 80 | 0 / 80 | 0 / 80 | 182 |
| `pure.int_math` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.fib_recursive` | 0 / 4 | 0 / 4 | 0 / 4 | 0 |
| `pure.float_math` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| `pure.string_pattern` | 0 / 85 | 0 / 85 | 0 / 85 | 578 |
| `pure.table_sort` | 0 / 2359 | 0 / 2359 | 0 / 2359 | 24166 |
| `pure.table_concat` | 0 / 119 | 0 / 119 | 0 / 119 | 647 |
| `pure.coroutine_switch` | 0 / 0 | 0 / 0 | 0 / 0 | 1 |
| `pure.gc_small_tables` | 0 / 94 | 0 / 94 | 0 / 94 | 614 |
| `pure.oop_inherit` | 0 / 0 | 0 / 0 | 0 / 0 | 0 |

## The emulator (`xlua16k`) **[C]**

Pure Lua:

| workload | iterations | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | MoonSharp-ng | MoonSharp-ng / xlua-ng plain |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `empty_loop` | 4M | 2.1 (2.0) | 2.2 (2.1) | 2.1 (2.0) | 9.3 (9.1) | 4.4x |
| `function_call` | 1M | 12.2 (11.9) | 12.1 (11.7) | 11.4 (11.0) | 100.1 (99.5) | 8.2x |
| `method_call` | 1M | 25.4 (25.0) | 26.2 (25.7) | 24.6 (23.9) | 165.5 (164.9) | 6.5x |
| `table_array_insert` | 1M | 6.2 (4.6) | 5.9 (5.0) | 6.0 (5.0) | 88.3 (85.2) | 14.2x |
| `table_array_read` | 1M | 10.9 (10.2) | 10.5 (10.1) | 10.6 (10.3) | 70.7 (69.7) | 6.5x |
| `table_hash_set_get` | 1M | 21.7 (20.4) | 21.7 (20.1) | 20.8 (20.3) | 187.2 (184.6) | 8.6x |
| `string_concat` | 500K | 206.2 (204.7) | 204.5 (197.6) | 202.7 (199.4) | 260.4 (255.5) | 1.3x |
| `string_format` | 500K | 223.9 (219.3) | 220.3 (215.1) | 215.7 (212.1) | 400.6 (395.7) | 1.8x |
| `closure_create_call` | 1M | 86.3 (83.3) | 85.3 (82.4) | 83.2 (81.5) | 403.7 (395.1) | 4.7x |
| `int_math` | 2M | 11.0 (10.8) | 11.0 (10.7) | 11.0 (10.7) | 42.6 (42.5) | 3.9x |
| `fib_recursive` | 20K | 5,251.9 (5,188.8) | 5,244.0 (5,205.3) | 5,257.7 (5,197.9) | 46,411 (46,248) | 8.8x |
| `float_math` | 2M | 20.6 (20.3) | 20.7 (20.4) | 20.4 (20.1) | 79.9 (79.2) | 3.9x |
| `string_pattern` | 100K | 1,495.1 (1,492.2) | 1,505.3 (1,491.8) | 1,491.2 (1,470.7) | 5,547.4 (5,494.5) | 3.7x |
| `table_sort` | 2K | 45,816 (44,153) | 45,515 (44,951) | 41,731 (41,271) | 120,693 (119,431) | 2.6x |
| `table_concat` | 50K | 627.6 (608.7) | 646.0 (603.6) | 587.0 (570.4) | 1,967.6 (1,899.5) | 3.1x |
| `coroutine_switch` | 500K | 100.1 (93.4) | 99.1 (93.9) | 95.8 (95.1) | 286.3 (283.0) | 2.9x |
| `gc_small_tables` | 500K | 127.1 (123.4) | 125.8 (120.1) | 124.3 (121.8) | 932.2 (917.5) | 7.3x |
| `oop_inherit` | 1M | 30.1 (28.3) | 29.1 (28.2) | 28.6 (28.4) | 138.4 (136.8) | 4.6x |
| geometric mean of the ratios | | | | | | 4.6x |

The bridge:

| case | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | MoonSharp-ng | MoonSharp-ng / xlua-ng plain |
| --- | ---: | ---: | ---: | ---: | ---: |
| `lua2cs.static.method.0arg.void` | 19.7 (18.5) | 25.7 (23.6) | 25.9 (24.9) | 89.0 (88.0) | 4.52x |
| `lua2cs.static.method.0arg.ret` | 21.6 (21.4) | 27.7 (26.8) | 27.8 (27.4) | 134.3 (131.4) | 6.22x |
| `lua2cs.static.prop.get.int` | 45.6 (44.7) | 51.5 (49.5) | 50.2 (49.2) | 236.9 (227.1) | 5.20x |
| `lua2cs.static.field.set.int` | 49.2 (47.3) | 54.9 (53.8) | 55.0 (54.0) | 118.4 (117.8) | 2.41x |
| `lua2cs.ctor.class.3int` | 343.4 (280.2) | 364.2 (326.6) | 345.3 (337.6) | 973.2 (953.8) | 2.83x |
| `lua2cs.ctor.Vector3.full` | 95.9 (91.5) | 103.7 (98.4) | 101.9 (92.0) | 994.4 (984.1) | 10.37x |
| `lua2cs.instance.method.0arg.void` | 58.2 (54.1) | 59.6 (57.7) | 58.9 (55.6) | 102.7 (101.1) | 1.76x |
| `lua2cs.instance.method.3arg.ret` | 79.2 (73.9) | 85.4 (82.5) | 85.5 (81.0) | 315.3 (312.8) | 3.98x |
| `lua2cs.instance.method.6arg.ret` | 93.2 (90.9) | 101.2 (97.8) | 98.4 (95.5) | 511.4 (505.3) | 5.49x |
| `lua2cs.instance.prop.get.int` | 81.9 (78.9) | 83.5 (79.5) | 82.0 (80.8) | 274.6 (250.6) | 3.35x |
| `lua2cs.instance.prop.set.int` | 66.5 (63.3) | 70.9 (68.6) | 71.9 (69.0) | 111.5 (108.7) | 1.68x |
| `lua2cs.instance.field.get.int` | 77.8 (75.7) | 82.8 (79.2) | 84.6 (80.3) | 266.0 (261.1) | 3.42x |
| `lua2cs.instance.field.set.int` | 65.8 (63.2) | 69.5 (68.3) | 69.6 (68.8) | 115.6 (112.1) | 1.76x |
| `lua2cs.instance.method.long.3arg.ret` | 81.1 (75.5) | 83.5 (81.1) | 82.6 (76.8) | 380.4 (361.3) | 4.69x |
| `lua2cs.instance.method.double.3arg.ret` | 71.4 (66.6) | 79.5 (76.4) | 75.3 (70.9) | 268.5 (266.6) | 3.76x |
| `lua2cs.instance.method.object.3arg.ret` | 377.4 (358.6) | 377.7 (366.0) | 364.6 (314.3) | 955.7 (929.2) | 2.53x |
| `lua2cs.instance.method.string.3arg.ret` | 345.2 (320.8) | 346.6 (334.3) | 345.8 (330.7) | 310.1 (307.8) | 0.90x |
| `lua2cs.instance.prop.get.string` | 115.8 (111.2) | 120.9 (117.4) | 118.9 (114.7) | 283.0 (277.8) | 2.44x |
| `lua2cs.instance.prop.set.string` | 148.7 (120.0) | 142.2 (125.6) | 152.4 (134.2) | 233.6 (215.2) | 1.57x |
| `lua2cs.instance.method.Vector3.3arg.ret` | 219.7 (199.9) | 232.0 (217.9) | 226.8 (216.7) | 1,133.3 (1,099.7) | 5.16x |
| `lua2cs.instance.prop.get.Vector3` | 146.2 (125.0) | 136.6 (130.8) | 135.9 (121.7) | 868.5 (860.0) | 5.94x |
| `lua2cs.instance.prop.set.Vector3` | 94.2 (88.5) | 100.0 (98.9) | 102.6 (97.0) | 272.8 (222.9) | 2.90x |
| `lua2cs.instance.field.get.Vector3` | 136.2 (125.9) | 134.5 (134.0) | 136.3 (133.2) | 870.0 (863.1) | 6.39x |
| `lua2cs.instance.prop.get.enum` | 92.6 (90.6) | 95.8 (93.4) | 94.6 (92.8) | 814.0 (801.7) | 8.79x |
| `lua2cs.instance.method.class.3arg.ret` | 182.4 (177.9) | 191.6 (180.8) | 182.1 (175.8) | 1,049.2 (1,031.5) | 5.75x |
| `lua2cs.instance.prop.get.class` | 101.5 (95.7) | 104.4 (101.5) | 99.8 (98.7) | 798.0 (777.6) | 7.86x |
| `lua2cs.instance.method.struct.3arg.ret` | 237.5 (227.1) | 247.4 (239.0) | 245.0 (225.7) | 1,202.8 (1,187.4) | 5.06x |
| `lua.loop` | 3.7 (3.7) | 3.7 (3.7) | 3.7 (3.7) | 11.2 (11.1) | 3.03x |
| `lua.table.rw` | 7.1 (7.0) | 9.0 (7.1) | 7.1 (7.1) | 62.5 (62.0) | 8.80x |
| `cs2lua.int.arity0.void` | 36.4 (35.0) | 35.5 (34.6) | 38.0 (35.6) | 175.9 (172.5) | 4.83x |
| `cs2lua.int.3arg.void` | 41.2 (40.5) | 43.2 (41.9) | 42.6 (41.9) | 365.6 (360.9) | 8.87x |
| `cs2lua.int.3arg.ret` | 50.0 (46.9) | 51.7 (50.0) | 51.2 (48.5) | 372.1 (369.7) | 7.44x |
| `cs2lua.long.3arg.ret` | 49.4 (47.0) | 52.2 (50.8) | 49.8 (48.2) | 356.6 (348.0) | 7.22x |
| `cs2lua.double.3arg.ret` | 43.6 (43.0) | 49.7 (47.8) | 47.3 (46.4) | 358.9 (345.9) | 8.23x |
| `cs2lua.object.3arg.ret` | 147.8 (139.6) | 160.5 (149.1) | 156.0 (146.2) | 1,594.1 (1,565.9) | 10.79x |
| `cs2lua.string.3arg.ret` | 216.9 (213.5) | 230.5 (215.8) | 216.5 (208.3) | 405.0 (397.6) | 1.87x |
| `cs2lua.Vector3.3arg.ret` | 240.1 (216.4) | 224.6 (222.5) | 228.8 (217.4) | 2,709.0 (2,692.8) | 11.28x |
| `cs2lua.class.3arg.ret` | 143.0 (132.2) | 142.2 (136.3) | 137.8 (133.2) | 2,496.1 (2,483.6) | 17.46x |
| `cs2lua.struct.3arg.ret` | 253.4 (242.2) | 253.0 (248.2) | 255.2 (248.3) | 2,836.7 (2,798.3) | 11.19x |
| `cs.table.get` | 233.5 (226.4) | 245.2 (237.2) | 235.8 (224.8) | 22.2 (21.8) | 0.10x |

| group | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | MoonSharp-ng |
| --- | ---: | ---: | ---: | ---: |
| Lua → C#: static calls, property and field (int) | 31.3 (1.00x) | 37.7 (1.20x) | 37.5 (1.20x) | 135.3 (4.33x) |
| Lua → C#: instance calls (int, long, double) | 75.7 (1.00x) | 80.7 (1.07x) | 79.0 (1.04x) | 279.0 (3.69x) |
| Lua → C#: instance properties and fields (int, enum) | 76.3 (1.00x) | 79.9 (1.05x) | 80.0 (1.05x) | 238.2 (3.12x) |
| Lua → C#: strings | 181.1 (1.00x) | 181.3 (1.00x) | 184.4 (1.02x) | 273.7 (1.51x) |
| Lua → C#: Vector3 and struct | 145.2 (1.00x) | 149.0 (1.03x) | 148.6 (1.02x) | 808.5 (5.57x) |
| Lua → C#: objects (new class, object and class arguments, class property) | 221.3 (1.00x) | 229.0 (1.03x) | 218.7 (0.99x) | 939.4 (4.24x) |
| C# → Lua: int, long, double | 43.8 (1.00x) | 46.0 (1.05x) | 45.5 (1.04x) | 314.2 (7.17x) |
| C# → Lua: string | 216.9 (1.00x) | 230.5 (1.06x) | 216.5 (1.00x) | 405.0 (1.87x) |
| C# → Lua: Vector3 and struct | 246.7 (1.00x) | 238.4 (0.97x) | 241.6 (0.98x) | 2,772.1 (11.24x) |
| C# → Lua: object and class | 145.4 (1.00x) | 151.1 (1.04x) | 146.6 (1.01x) | 1,994.8 (13.72x) |
| Lua alone (loop, table) | 5.1 (1.00x) | 5.8 (1.13x) | 5.1 (1.00x) | 26.5 (5.16x) |
| C# reads a Lua table field | 233.5 (1.00x) | 245.2 (1.05x) | 235.8 (1.01x) | 22.2 (0.10x) |
| Pure Lua: the 18 workloads | 86.6 (1.00x) | 86.2 (1.00x) | 83.8 (0.97x) | 396.4 (4.58x) |

## The host: this Mac, .NET and Mono **[C]**

The fork's earlier comparison ran upstream MoonSharp (`fbd0367`) on .NET 8
against xlua-ng (`3042ef6`) native on a Linux x64 Xeon. Here the same
runners ran on an Apple M2 Pro (10 cores, macOS 27.0.1), 3 interleaved
process rounds, each under a native test slot; the table gives the median of
the 3 per-process medians (each of 7 runs):

- **xlua-ng native:** `Test/Native/flag_bench.c` (`cc -O2`) against
  `libxlua.dylib` (Lua 5.5.1, CMake Release, `-DBUILD_SILICON=ON`), `--runs 7`.
- **MoonSharp-ng on .NET 8.0.32:** the fork's
  `src/DevTools/Benchmarks workloads 7` with the 18 case names.
- **MoonSharp-ng on Mono 6.14.1:** the same `LuaWorkloads.cs` and
  `LuaCSharpScripts.cs`, unchanged, compiled with Mono's `csc` against the
  `net45` build of `MoonSharp.Interpreter.dll` with a three-line `Main`, run
  with `mono --optimize=all`.

`table_sort` is not the same script here (as in the fork's note): native runs
`flag_bench.c`'s `table.move` version, MoonSharp-ng the copy loop. The last
column is the ratio in the fork's earlier table.

| Workload | xlua-ng native (ns) | MoonSharp-ng .NET 8 (ns) | ratio | MoonSharp-ng Mono (ns) | ratio | bytes/iter .NET / Mono | earlier ratio (upstream, Linux) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| empty_loop | 2.0 | 5.7 | 2.9x | 15.3 | 7.8x | 0 / 0 | 18.7x |
| function_call | 10.8 | 72.6 | 6.7x | 201.0 | 18.5x | 0 / 0 | 14.5x |
| method_call | 21.2 | 115.4 | 5.4x | 320.8 | 15.1x | 0 / 0 | 10.6x |
| table_array_insert | 3.7 | 64.5 | 17.3x | 141.9 | 38.1x | 50 / 50 | 79.3x |
| table_array_read | 7.6 | 51.1 | 6.8x | 173.3 | 22.9x | 0 / 0 | 28.5x |
| table_hash_set_get | 19.9 | 148.8 | 7.5x | 462.1 | 23.2x | 0 / 0 | 21.1x |
| string_concat | 153.0 | 103.4 | 0.7x | 299.4 | 2.0x | 85 / 85 | 3.5x |
| string_format | 189.0 | 164.9 | 0.9x | 527.6 | 2.8x | 80 / 80 | 14.5x |
| closure_create_call | 70.5 | 114.2 | 1.6x | 253.3 | 3.6x | 152 / 168 | 7.0x |
| int_math | 10.4 | 23.6 | 2.3x | 101.3 | 9.7x | 0 / 0 | 23.1x |
| fib_recursive | 4,886.9 | 35,987.8 | 7.4x | 90,861.3 | 18.6x | 0 / 0 | 18.7x |
| float_math | 19.6 | 42.7 | 2.2x | 179.6 | 9.2x | 0 / 0 | 19.2x |
| string_pattern | 1,594.7 | 2,257.2 | 1.4x | 4,665.8 | 2.9x | 680 / 696 | 7.9x |
| table_sort | 29,841.0 | 41,906.3 | 1.4x | 124,482.1 | 4.2x | 27112 / 27304 | 2.5x |
| table_concat | 499.7 | 687.4 | 1.4x | 878.9 | 1.8x | 864 / 896 | 1.8x |
| coroutine_switch | 71.8 | 198.1 | 2.8x | 555.8 | 7.7x | 0 / 0 | 7.6x |
| gc_small_tables | 97.2 | 218.9 | 2.3x | 644.5 | 6.6x | 512 / 544 | 9.0x |
| oop_inherit | 25.3 | 96.9 | 3.8x | 263.7 | 10.4x | 0 / 0 | 9.0x |
| geometric mean of the ratios | | | 2.9x | | 8.0x | | 11.3x |

Bytes per iteration are `GC.GetAllocatedBytesForCurrentThread`, exact on both
runtimes. They are close to the phone's managed bytes, which are rounded to
Boehm's size classes (for example `string_pattern` 680 on .NET against 578 on
the phone, `gc_small_tables` 512 against 614, `table_array_insert` 50 against
82).

## Sizes **[C]**

APK bytes of each bench player, and what it adds to the empty project (the
same `SmokeBuild.cs` settings, no Lua library and no bench code). Every
player carries the same bench code (`VsBench.cs`); the library columns are
uncompressed bytes in the APK, compressed in parentheses. Every native
library in these APKs is 16 KB aligned.

| player | APK | over empty | libil2cpp.so over empty (compressed) | libunity.so over empty | metadata over empty (compressed) | Lua library |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| xlua-ng plain | 13,898,386 | +1,619,334 | +2,522,824 (+750,291) | +1,107,840 | +452,564 (+155,871) | `libxlua.so` 388,136 (165,593 compressed) |
| xlua-ng gcm | 13,877,842 | +1,598,790 | +2,454,784 (+728,720) | +1,107,840 | +455,740 (+156,799) | `libxlua.so` 388,136 (165,593 compressed) |
| xlua-ng strict-minimal | 12,861,594 | +582,542 | +1,285,768 (+370,896) | +17,184 | +203,584 (+65,074) | `libxlua.so` 312,176 (137,270 compressed) |
| moonsharp-ng sourcegen | 13,651,432 | +1,372,380 | +3,930,384 (+1,136,642) | +6,448 | +724,852 (+221,532) | none (C#, in `libil2cpp.so`) |
| empty project | 12,279,052 | – | +0 (+0) | +0 | +0 (+0) | none (C#, in `libil2cpp.so`) |

- **MoonSharp-ng's cost is all in IL2CPP output:** 3.93 MB of
  `libil2cpp.so` (1.14 MB compressed) and 0.72 MB of metadata. It has no
  native library and adds 6 KB to `libunity.so`.
- **xlua-ng legacy's `libunity.so` share** (1.11 MB) is the UnityWebRequest and
  TLS modules kept by the StreamingAssets searcher
  ([SIZE.md](../../build/SIZE.md#what-legacy-adds-to-libunityso-2026-10-29)).
  Strict minimal leaves the searcher out and adds 17 KB there.
- **Stripping:** the fork's `link.xml` keeps the reflection-registered
  standard library whole. Whether a higher stripping level would shrink
  MoonSharp-ng was not measured. **[U]**

## What the numbers say

**Pure Lua.**

- **Where MoonSharp-ng is closest:** string building (`string_concat` is
  faster than xlua-ng on the phone, 922 against 1,138 ns, and on .NET), and
  library-heavy work (`string_format` 1.5x, `table_concat` 1.8x,
  `closure_create_call` 1.8x, `gc_small_tables` 2.7x). Both then spend their
  time in library code, and .NET strings are cheap to build. **[C]**
- **Where it is furthest:** the interpreter loop itself. An empty loop step
  costs 47.8 against 5.1 ns, a Lua call 525 against 49 ns, a table read 362
  against 42 ns, `table_array_insert` 406 against 29 ns. That is dispatch and
  value handling in managed code against Lua's C VM. **[C]**
- **Phone against desktop:** the ratio on the phone (4.8x) is above .NET's on
  the Mac (2.9x) and below Mono's (8.0x). IL2CPP's C++ output of the
  interpreter loop runs slower relative to native C than RyuJIT's code
  does on the Mac. Why exactly was not profiled. **[U]**
- **Against the fork's earlier numbers:** upstream MoonSharp was 11.3 times
  slower than native on the Linux desktop; MoonSharp-ng is 2.9 times slower
  on .NET here. The fork's struct values, compiler fusion, inline caches and
  array part (its ROADMAP) account for that. **[S]**

**The bridge.**

- **MoonSharp-ng's cheapest calls:** a Lua → C# instance method with no
  arguments (550 ns, 2.0x xlua-ng) and setting an int property or field
  (630 ns, 1.9 to 2.0x). Arguments and results add more per value than in
  xlua-ng: 3 int arguments and a result cost it 1.69 µs against 0.38 µs.
  **[C]**
- **Reads of properties and fields** allocate a result `DynValue` per read
  (27 bytes), which the fork's ROADMAP lists as known ("the index path has
  no context to return through"). An enum or class property read costs 3.5
  to 4.1 µs, 7 to 9 times xlua-ng. **[C]** for the numbers, **[S]** for the
  cause.
- **Vector3 and structs** are boxed userdata in MoonSharp-ng: 97 bytes and
  3.75 µs per read on the phone (xlua-ng: a 52-byte Lua userdata, 0.78 µs).
  **[C]**
- **Static members:** MoonSharp-ng's static call (498 ns) costs about what
  its instance call does (550 ns), while xlua-ng's plain static call is its
  cheapest (74 ns), so the gap is largest there (6.7 to 9.4 times).
- **C# → Lua:** a typed delegate call with three numbers costs MoonSharp-ng
  about 2 µs on the phone (8x xlua-ng), and 11.5 to 12.5 µs with three class,
  Vector3 or struct arguments (7.5 to 16x), which also allocate 340 to 430
  bytes. Strings are the closest (2.1x): MoonSharp-ng passes the .NET string
  itself, xlua-ng copies it into Lua and back. **[C]**
- **C# reads a Lua table:** 93 ns against 1.29 µs. Code that reads many
  table fields from C# (configuration tables, data-driven content) is
  cheaper with MoonSharp-ng. **[C]**
- **xlua-ng's configurations:** GCM and strict minimal cost 30 to 35% more
  than plain for the static cases and 1 to 7% for the rest, as in the
  earlier notes. The object path changes merged today show: three class
  arguments and a result cost xlua-ng 0.88 µs (2.43 µs on 2026-10-28), and
  C# → Lua with classes 0.72 µs (3.77 µs). **[C]**

**Frame cost, for scale** **[U]**: 10,000 instance property reads per frame
on this phone cost xlua-ng about 3.7 ms and MoonSharp-ng about 10.3 ms, 44%
and 123% of a 120 fps frame; one million Lua loop iterations with a
function call cost about 50 ms against 525 ms.

## Caveats

- **An interpreter in C# against a native VM.** MoonSharp-ng runs on the
  managed heap and in IL2CPP's C++ output; xlua-ng runs a C VM and pays a
  P/Invoke transition per bridge step. The trade-offs are the opposite of
  each other: MoonSharp-ng is slow in the VM and cheap at the boundary for
  data that stays managed (tables read from C#, strings), xlua-ng the other
  way round.
- **Lua 5.2 against 5.5.1.** No integers in MoonSharp-ng (numbers are doubles;
  64-bit integers are an opt-in `int64` type), so `int_math` and
  `table_sort` do floating-point work there and integer work in xlua-ng.
  The 5.3+ features (integers, integer division, bitwise operators, `utf8`,
  `table.move`) are absent in MoonSharp-ng. **[S]** (the fork's README and
  ROADMAP)
- **Feature differences.**
  - **MoonSharp-ng:** no native plugin, so one package for every platform
    (WebGL, consoles, any architecture) and no 16 KB page alignment work; a
    debugger (VS Code); synchronous coroutines as C# iterators; no weak
    tables. **[S]**
  - **xlua-ng:** hotfix, several environments, the core preset's limits,
    lossless 64-bit integers, Lua 5.3/5.4/LuaJIT builds. **[C]** from this
    project.
  - **The bench covers none of these,** only call costs and pure Lua speed.
- **GC behaviour.** Every MoonSharp-ng value that leaves the VM, and every
  Lua table, closure and string, lives on the managed heap, which IL2CPP
  collects with Boehm (non-moving; incremental by default in Unity 6
  **[U]**). xlua-ng's Lua objects live in Lua's own heap with its own
  collector, and only the bridge touches the managed heap. The timings
  include each library's collection work, since the collectors ran during
  them. A game's frame time also depends on how the managed GC's pauses
  grow with a MoonSharp-ng heap full of Lua objects, which this bench did
  not measure. **[U]**
- **Allocation counts** are `GC.GetTotalMemory` deltas rounded to Boehm's size
  classes, over 10,000 operations; read them to about ±4 bytes.
- **Configuration.** MoonSharp-ng got its source generator and explicit
  registration, its fastest IL2CPP setup by its docs; its reflection mode
  (what a project gets without the generator) was not measured and would be
  slower ([S]: the fork's `docs/source-generator.md` gives 2.0 to 2.5 times
  for the interop part under NativeAOT).
- **One phone, one emulator, one Unity version.** No iOS, no other Android
  device, no High stripping, no Development builds, no Mono player.

## Not run

- MoonSharp-ng in reflection mode, with `Script` options other than the
  default, or with the register VM prototype of its branch
  `spike/register-vm-unity` (the fork's ROADMAP waits for exactly this kind
  of IL2CPP measurement for that decision).
- Start-up cost: `new Script()` against `new LuaEnv()`, the first call of each
  binding, and memory per state.
- The rest of zlua's case list (249 cases), a CPU profile of the slow paths,
  and managed GC pause times.
- xlua-ng on Lua 5.4.9, 5.3.6 and LuaJIT.

## Reproducing

1. **MoonSharp-ng package:** in a clone of the fork at the commit above,
   `tools/upm/stage-local-package.sh 3.0.0-ng75864ce` (needs the .NET SDK
   for the source generator); the package is
   `.upm-staging/org.moonsharp.moonsharp`.
2. **xlua-ng players:** with plugins from `build/make_android_lua55.sh` (and
   the minimal variant) in `PLUGINS`, and `XLUA_SIZE_TMPDIR` set to a short
   directory, `size_report.py run --work WORK/work --plugins PLUGINS
   --platforms android --configs vsbench`.
3. **MoonSharp-ng player:** `vs_bench.py build --work WORK --unity UNITY
   --libs moonsharp --moonsharp PACKAGE`.
4. **Device runs,** under xlua-ng's Unity lock and the device's own lock, with
   no Unity editor running: `vs_bench.py device phone --work WORK --sdk SDK
   --rounds 8 --only 'xlua-ng|moonsharp' --serial SERIAL --launch-timeout
   1800`, and `vs_bench.py device emu` with the same options (no
   `--serial`).
5. **Tables:** `vs_bench.py report --work WORK` writes `vs-report.json` and
   `vs-report.md`.
6. **Host:** the commands of "The host" above, each wrapped in
   `native-test-slot.sh timeout`.

The raw logs are in `vs-moonsharp-2026-10-29/` on the build disk: `phone/`,
`emu/`, `host/runs/`, the stopped first start (`phone-aborted1/`,
`emu-aborted1/`, players built before the merge of main) and `logs/`.

## Summary for MoonSharp-ng's comparison doc

The owner may copy this section into the fork's `docs/comparison-xlua-ng.md`.

> **Unity IL2CPP on a phone (2026-10-29).** MoonSharp-ng `master` `75864ce`
> (UPM package with the source generator) against xLua-NG `main` `7c2037fd`
> (Lua 5.5.1; legacy plain, GCM and strict minimal), release IL2CPP players
> (Unity 6000.3.21f1, ARM64, Minimal stripping) on a moto g play 2024
> (Snapdragon 680), 8 interleaved launches per player.
>
> - **Pure Lua (the 18 workloads above):** MoonSharp-ng is 4.8 times slower
>   (geometric mean; 0.8x for `string_concat` to 13.8x for
>   `table_array_insert`). On an Apple M2 Pro the same workloads are 2.9
>   times slower under .NET 8 and 8.0 times under Mono 6.14, against 11.3
>   times for upstream MoonSharp in the earlier desktop run.
> - **Lua → C# (zlua-benchmark's cases):** 1.5 to 5.4 times slower by group
>   (strings 1.5, instance properties 3.1, instance calls 4.0, objects 4.3,
>   Vector3 and structs 4.4, static members 5.4).
> - **C# → Lua:** 2.1 times slower with strings, 7.6 to 7.8 with numbers,
>   Vector3 and structs, 12.9 with class and object arguments.
> - **C# reading a Lua table field:** MoonSharp-ng is 14 times faster (93 ns
>   against 1.29 µs; no P/Invoke).
> - **Allocations:** a property or field read allocates about 27 bytes (the
>   result `DynValue`), a Vector3 or struct value about 97, a class result
>   68, and a C# → Lua call with three struct or class arguments 340 to 430;
>   numeric calls allocate nothing.
> - **APK size over an empty project:** MoonSharp-ng +1.37 MB (`libil2cpp.so`
>   +3.93 MB uncompressed, metadata +0.72 MB, no native library); xLua-NG
>   strict minimal +0.58 MB, legacy +1.60 to 1.62 MB.
>
> Details, the emulator and the method: xLua-NG's
> `docs/research/benchmark-vs-moonsharp-ng.md`.
