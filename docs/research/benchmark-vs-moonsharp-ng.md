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
  objects, xlua-ng's take several P/Invoke calls. **[C]** Since 2026-10-30
  xlua-ng's costs 254 ns (2.7 times MoonSharp-ng's), and strings cross the
  bridge 17 to 45% faster: [C# table access and strings](#c-table-access-and-strings-2026-10-30).
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

## C# table access and strings (2026-10-30)

The tables above show the one bridge case where MoonSharp-ng was faster: C#
reading a Lua table field (`cs.table.get`, `LuaTable.Get<string, int>`), 93
ns against xlua-ng's 1.29 µs on the phone. This section finds where that
time went, step by step, for `LuaTable.Get`, `ContainsKey`, `Set`,
`GetInPath` and `ForEach`, measures five changes that take most of it
off, and looks at the strings that cross the bridge, which allocate more
managed memory in xlua-ng than in MoonSharp-ng. Behaviour does not change:
the same values, errors, metamethods and stack (tests below).

- **Where it went** **[C]**: in the managed code around the native calls.
  On the emulator a third of a `Get<string, int>` was IL2CPP's Type lookups
  (three `typeof` per read, each a hash map read under a mutex), a tenth
  `Dictionary<Type, ...>` lookups for the key's push function and the
  value's get function, a tenth the UTF-8 encoder for the key, and a
  quarter Lua itself: seven P/Invokes and a `lua_pcall` per read, also on a
  table without a metatable.
- **After the changes** (phone, plain) **[C]**: a C# read of a number by
  string key costs 240 to 256 ns instead of 1.24 µs (−79 to −81%),
  `cs.table.get` 254 ns instead of 1.30 µs (−81%; 248 to 255 ns in all
  three modes), a string read 396 ns instead of 1.57 µs, `ContainsKey` 189
  instead of 761 ns, `Set` 252 to 303 ns instead of 1.08 to 1.30 µs, a
  `ForEach` element 553 instead of 1374 ns, and `GetInPath` 471 ns instead
  of 1.14 µs. Strings crossing the bridge cost 17 to 45% less (C# → Lua with
  strings 0.55x). Calls without tables or strings do not move beyond the
  scatter of a build. MoonSharp-ng's 93 ns is still 2.7 times faster for
  `cs.table.get`; it was 14 times.
- **Strings** **[C]**: a Lua string that reaches C# costs exactly one new C#
  string, as before, and nothing else; encoding and decoding ASCII no longer
  go through `Encoding.UTF8`. After the changes the collector is the largest
  part of a string read (40% on the emulator). Fewer allocations would need a
  cache of decoded strings, which changes their identity: a question for the
  owner (What is left).

### Method

- **Players** **[C]**: the player bench (`Test/MobileSmoke/PlayerBench.cs`,
  `size_report.py +pbench`), which now also times fifteen `LuaTable`
  operations from C# (`TableCases`: `Get` of int, double, long, bool, string,
  object and `LuaTable` values by string key, by integer key and through an
  `__index` table, `GetInPath`, `ContainsKey`, `Set` of an int and a string,
  `Length`, and `ForEach` per element), and the vs-bench (`+vsbench`). Unity
  6000.3.21f1, release ARM64 IL2CPP, Minimal stripping, the Lua 5.5.1 plugin
  built from the same tree with NDK r30, as on 2026-10-29. The base is main at
  `4bc5dd6e` with the bench commit `d8b5f464`; each candidate tree adds one
  change to the one before (c1 to c4 below), copies of the base with the
  change's files, which equal the commits. Unity ran with
  `ANDROID_ADB_SERVER_PORT=5038`, so that its adb 36.0.0 left the shared adb
  37.0.1 server alone.
- **Timing** **[C]**: 8 launches per APK in rotating order, 15 s apart,
  Thermal Status 0 before and after every launch. The phone had about 3.5 GB
  free, so each launch installed its APK, ran it once and uninstalled it; at
  most one test package was installed at a time. From the 26th install on,
  Google Play Protect asked on most installs whether to send the app for a
  security check; it was answered "Don't send" 26 times, once by hand and
  then by a watcher that looked only while an install waited
  (`scripts/protect_watch.py`). All 120 launches gave their result on the
  first try. The tables give the median of the per-launch medians in ns per
  operation, the C# or Lua loop included.
- **Profiler** **[C]**: `simpleperf record --app` on the `xlua16k` emulator
  only (never on the phone), cpu-clock at 4000 Hz, the player bench's profile
  mode with one case at a time for 8 s. The self samples of the main thread
  are grouped by the step they belong to (`scripts/categories.py`) and given
  as ns per operation of the case. The emulator shows where the time goes; it
  does not predict the phone's costs.
- **Reading** **[C]**: the players' IL2CPP C++ (`LuaTable.Get`, `Fetch`,
  `ObjectTranslator.Get<int>`) and their `libil2cpp.so` with symbols.

### Where the time went

The emulator's profile of each case, its main thread's self samples grouped
by step, in ns per operation of the case (base → after C1 to C4) **[C]**:

| step | `Get<string, int>` | `ContainsKey` | `Set` (int) | `GetInPath<int>` | `Get<string, string>` |
| --- | ---: | ---: | ---: | ---: | ---: |
| Type lookups | 94 → 0 | 32 → 0 | 60 → 0 | 31 → 0 | 124 → 0 |
| `Dictionary<Type, ...>` lookups | 32 → 0 | 16 → 0 | 30 → 0 | 15 → 0 | 43 → 0 |
| UTF-8 encoder and decoder | 27 → 0 | 27 → 0 | 25 → 0 | 1 → 0 | 66 → 11 |
| Lua (`libxlua.so`, `lua_pcall`) | 73 → 56 | 62 → 41 | 92 → 53 | 91 → 83 | 86 → 102 |
| P/Invoke wrappers | 19 → 17 | 15 → 10 | 14 → 10 | 13 → 13 | 24 → 32 |
| casts, shared generics, write barriers | 18 → 0 | 8 → 0 | 15 → 0 | 7 → 0 | 22 → 4 |
| atomics | 9 → 0 | 4 → 0 | 7 → 0 | 8 → 5 | 14 → 7 |
| bridge code (`LuaTable`, translator, delegates) | 14 → 26 | 6 → 9 | 10 → 16 | 10 → 13 | 16 → 42 |
| allocation and collector | 1 → 0 | 0 → 0 | 1 → 0 | 0 → 0 | 32 → 80 |
| other (marshaling, `tgkill`, PLT) | 21 → 6 | 13 → 5 | 14 → 5 | 65 → 63 | 97 → 151 |
| **total** | **307 → 104** | **184 → 65** | **268 → 84** | **241 → 177** | **525 → 429** |

`GetInPath`'s "other" is the path's marshaling, which C5 removes (below).

What each step was, in the base **[C]**:

- **Type lookups.** Three per `Get<string, int>`: `typeof(TValue)` for the
  nil check (`LuaTable.cs:53` at `4bc5dd6e`), and `typeof(T)` in
  `ObjectTranslator.PushByType<T>` and `Get<T>` (`ObjectTranslator.cs:1401`
  and `:1382`), each compiled by IL2CPP to `Type_GetTypeFromHandle`, which
  looks the `Type` object up in a hash map under a mutex
  (`il2cpp::vm::Reflection::GetTypeObject`, as found on 2026-10-29). `TKey`
  and the reference-type `TValue`s are shared generics in IL2CPP, so their
  `typeof` also goes through the method's runtime generic context first.
- **`Dictionary<Type, Delegate>` lookups.** Two per read
  (`tryGetPushFuncByType` and `tryGetGetFuncByType`): `EqualityComparer<Type>.Default`,
  `RuntimeType.GetHashCode` and `Type.op_Inequality`, then a cast of the
  delegate to `Action<IntPtr, T>` or `Func<IntPtr, int, T>`. A `LuaTable` or
  `object` result added `ObjectCasters.GetCaster`'s lookups
  (`Nullable.GetUnderlyingType`, `IsByRef`, `castersMap`), and `ForEach`'s
  key check `ObjectCheckers.GetChecker`'s.
- **The UTF-8 encoder.** `lua_pushstring` (`LuaDLLBridge.cs:527`) called
  `Encoding.UTF8.GetByteCount` and then `GetBytes` for every key; each reads
  `Encoding.UTF8`, which IL2CPP compiles to a load and a full memory barrier
  (`il2cpp::vm::Thread::FullMemoryBarrier`, 6 ns of the 27).
- **Lua and the protected call.** Seven P/Invokes per read (`lua_gettop`,
  `xlua_rawgeti` of the reference, `xlua_pushlstring`, `xlua_pgettable`,
  `lua_type`, `xlua_tointeger64`, `lua_settop`), and in `xlua_pgettable`
  (`xlua.c:566`) a `lua_pcall` of a C helper: `sigsetjmp`, the call
  machinery and the helper's `lua_gettable`. The key's interning
  (`internshrstr`) and the table lookup (`luaH_get`) are a few ns each.
- **`GetInPath`'s path** (the "other" of its column): IL2CPP's marshaling of
  the `string` parameter (`LuaDLLBridge.cs:896`), a UTF-16 to UTF-8
  conversion into a `std::string`, `malloc` and `free` per call.
- **A string result**: a new C# string per read (the decoder's `GetString`,
  after `Marshal.Copy` into the shared buffer), and the collections it brings:
  32 ns of allocation and collector code and 46 ns in `tgkill`, Boehm stopping
  the other threads for a collection.

Not the cost **[C]**: the env lock (compiled only with `THREAD_SAFE` or
`HOTFIX_ENABLE`); the owner thread check (only with the bridge checks, which
release players leave out); `LuaBase.State` and `LuaEnv.L` (two loads and a
compare); the registry index (no memory barrier since 2026-10-29); and the
stack balance (one `lua_settop`).

After C1 to C4 a `Get<string, int>` on the emulator is half Lua (56 of 104
ns: the key's interning, the table lookup, the three P/Invokes' entries, and
10.9 ns in `libxlua.so`'s PLT stubs) and a quarter bridge code (the type's
operations, `Fetch`, `Get<T>`); no Type lookup is left. A string read now
spends 172 of its 429 ns in the collector (92 ns in `tgkill`, 80 ns marking
and allocating).

### The changes

| | commit | what |
| --- | --- | --- |
| C1 | `perf(runtime): find a type's get, push, cast and check functions once` | `ObjectTranslator` keeps, per translator and type, what `Get<T>`, `PushByType<T>` and `Assignable<T>` looked up on every call (the get and push functions, `GetObject`'s caster, the checker), in an array indexed by a per-type slot; registering a get or push function, caster or checker clears it. `LuaTable`'s nil check reads `IsValueType` from a static field per type |
| C2 | `perf(native): read and write a referenced table raw when no metamethod runs`, `perf(runtime): LuaTable.Get, ContainsKey and Set in one native call` | `xlua_getref_get`, `_getfield`, `_set` and `_setfield` (`build/xlua.c`): the reference, the key (a string key as bytes) and `lua_gettable`'s or `lua_settable`'s result in one P/Invoke, raw where no metamethod can run (the raw value is not nil, or a read from a table without a metatable), through `lua_pcall` otherwise |
| C3 | `perf(runtime): copy ASCII strings to Lua without the UTF-8 encoder` | `lua_pushstring` (every C# string the bridge pushes, table keys included) copies an ASCII string into the shared buffer in one pass; other strings go to the encoder as before |
| C4 | `perf(runtime): decode ASCII Lua strings without the UTF-8 decoder` | `lua_tostring` widens a Lua string of up to 256 ASCII bytes on the stack into the new C# string; other strings go to the decoder as before |
| C5 | `perf(runtime): pass an ASCII GetInPath path without the string marshaler` | `GetInPath` and `SetInPath` pass an ASCII path as a C string from the shared buffer; other paths go through the marshaler as before |

**Why the raw read is the same.** In Lua 5.1 to 5.5 and LuaJIT,
`lua_gettable` on a table returns the raw value when it is not nil and
consults `__index` only when it is, and `lua_settable` stores into a present key
without `__newindex`; a table without a metatable has no metamethod at all.
`lua_rawget` and `lua_rawset` on a present key allocate nothing and raise
nothing, so they need no protection. A missing key of a table with a
metatable, a value that is not a table (a `LuaTable` made from a reference
to a string, say) and a write of a new key (which can allocate) go through
`lua_pcall` as before. The key is pushed outside the protected call, as it
was. What can differ is internal: a `debug.sethook` hook with "c" no longer
sees the call and return of the protected call's helper for a raw access,
and a key or value whose push threw used to leave the table on the Lua
stack; both are in [MIGRATING.md](../MIGRATING.md).

**Why the caches are the same.** The get and push functions of a type can
only be added (`RegisterPushAndGetAndUpdate` and `RegisterStruct` throw for
a type that has one), casters and checkers only replaced through
`AddCaster` and `AddChecker`; each of these clears the cache, so a type's
next call looks everything up again. The caster and checker are still
resolved only where the old code resolved them (after the C# object and
struct tests), and `ObjectCasters.GetCaster` caches what it creates itself.

### Player bench on the phone **[C]**

**The changes in turn** (pb1; plain, each change added to the one before;
8 launches each). The table cases, the two string cases of the Lua → C#
list, and the controls (ns per operation, the median of the per-launch
medians; the last columns are differences):

| case | base | +C1 | +C2 | +C3 | +C4 | Δ +C1 | Δ +C2 | Δ +C3 | Δ +C4 | Δ all |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `table_get_int` | 1242.7 | 508.5 | 375.0 | 254.5 | 256.3 | -734.2 | -133.4 | -120.5 | +1.8 | -986.4 (-79%) |
| `table_get_double` | 1236.9 | 497.1 | 357.1 | 243.9 | 247.5 | -739.9 | -139.9 | -113.2 | +3.6 | -989.4 (-80%) |
| `table_get_long` | 1257.7 | 507.0 | 362.4 | 253.5 | 253.5 | -750.6 | -144.7 | -108.9 | +0.0 | -1004.2 (-80%) |
| `table_get_bool` | 1239.1 | 488.8 | 348.4 | 241.5 | 240.3 | -750.3 | -140.4 | -106.9 | -1.3 | -998.9 (-81%) |
| `table_get_string` | 1570.5 | 805.1 | 605.7 | 496.1 | 395.5 | -765.4 | -199.4 | -109.6 | -100.6 | -1175.0 (-75%) |
| `table_get_object` | 1753.6 | 797.1 | 656.3 | 501.3 | 498.0 | -956.4 | -140.9 | -155.0 | -3.2 | -1255.5 (-72%) |
| `table_get_table` | 2414.2 | 1206.1 | 1126.7 | 917.1 | 949.1 | -1208.1 | -79.4 | -209.6 | +32.0 | -1465.1 (-61%) |
| `table_get_index` | 1005.6 | 331.8 | 230.9 | 230.7 | 230.7 | -673.7 | -100.9 | -0.2 | +0.0 | -774.8 (-77%) |
| `table_get_via_index` | 1286.9 | 562.0 | 603.6 | 454.8 | 455.4 | -724.8 | +41.6 | -148.8 | +0.6 | -831.4 (-65%) |
| `table_get_in_path` | 1135.8 | 822.6 | 867.8 | 846.8 | 850.0 | -313.2 | +45.2 | -21.0 | +3.3 | -285.8 (-25%) |
| `table_contains_key` | 761.1 | 437.4 | 304.8 | 188.1 | 189.2 | -323.8 | -132.5 | -116.7 | +1.0 | -572.0 (-75%) |
| `table_set_int` | 1078.4 | 488.2 | 355.3 | 247.6 | 252.1 | -590.1 | -132.9 | -107.7 | +4.5 | -826.3 (-77%) |
| `table_set_string` | 1303.8 | 652.8 | 514.7 | 303.3 | 302.8 | -651.0 | -138.1 | -211.4 | -0.6 | -1001.1 (-77%) |
| `table_length` | 78.4 | 78.5 | 78.5 | 78.8 | 78.3 | +0.1 | +0.0 | +0.3 | -0.6 | -0.1 (-0%) |
| `table_for_each` | 1373.5 | 688.3 | 664.9 | 665.1 | 552.5 | -685.2 | -23.5 | +0.2 | -112.6 | -821.0 (-60%) |
| `string_arg` | 406.4 | 429.5 | 411.2 | 411.8 | 313.3 | +23.1 | -18.3 | +0.6 | -98.4 | -93.0 (-23%) |
| `string_return` | 292.6 | 290.7 | 292.6 | 181.8 | 184.4 | -1.9 | +1.9 | -110.8 | +2.6 | -108.2 (-37%) |
| `struct_boxed_field_get` | 993.8 | 573.3 | 564.0 | 568.5 | 566.1 | -420.4 | -9.3 | +4.5 | -2.4 | -427.7 (-43%) |
| `instance_call` | 332.7 | 334.8 | 342.1 | 343.5 | 335.0 | +2.1 | +7.3 | +1.4 | -8.5 | +2.3 (+1%) |
| `unity_instance_method` | 3843.3 | 4050.9 | 3964.4 | 3929.7 | 4031.3 | +207.6 | -86.5 | -34.7 | +101.6 | +188.0 (+5%) |

- **C1** (the per-type caches) takes 313 to 1208 ns off every table
  operation but `Length` (−28 to −67%): the Type and dictionary lookups of
  the key's push and the value's read. It also takes 420 ns (−43%) off
  `struct_boxed_field_get`, a reflection wrapper's struct field, which reads
  and pushes through the same `Get<T>` and `PushByType<T>`.
- **C2** (one native call, raw where no metamethod runs) takes 101 to 199 ns
  off each `Get`, `ContainsKey` and `Set` of a number, a boolean, a string or
  an object (−18 to −30%), and 79 ns off a `LuaTable` value (−7%). A key
  missing from a table whose metatable's `__index` answers
  (`table_get_via_index`) costs 42 ns more: the raw read and the metatable
  test come before the protected call. `GetInPath`, which C2 does not
  touch, moved by +45 ns between these two builds.
- **C3** (the ASCII encoder) takes 107 to 211 ns off every operation with a
  string key (−18 to −41%) and 111 ns (−38%) off `string_return`, a C# method
  that returns a string to Lua.
- **C4** (the ASCII decoder) takes 101 ns (−20%) off reading a string value,
  113 ns (−17%) off `ForEach` (its keys are strings) and 98 ns (−24%) off
  `string_arg`, a Lua string passed to C#.
- **All four:** a numeric read by string key costs 240 to 256 ns instead of
  1.24 to 1.26 µs (−79 to −81%), a string read 396 ns instead of 1.57 µs,
  `ContainsKey` 189 instead of 761 ns, `Set` 252 to 303 instead of 1078 to
  1304 ns, a `ForEach` element 553 instead of 1374 ns. The other 27 cases
  move by −1.8 to +3.3% (hot-path mix 429.2 → 425.9 ns, −0.8%), except the
  reflection wrapper `unity_instance_method` (+188 ns, +4.9%), which moved by
  up to 300 ns between builds on 2026-10-29 too.

**C5, the `GetInPath` path** (pb3; C4 and C5 in plain, 8 launches each):

| case | C4 | +C5 | Δ all |
| --- | ---: | ---: | ---: |
| `table_get_in_path` | 847.4 | 470.8 | -376.6 (-44%) |
| `table_get_int` | 255.2 | 253.6 | -1.7 (-1%) |
| `table_get_string` | 394.9 | 392.3 | -2.6 (-1%) |
| `table_contains_key` | 188.6 | 189.1 | +0.5 (+0%) |
| `table_set_int` | 255.8 | 251.0 | -4.7 (-2%) |
| `table_for_each` | 554.3 | 552.7 | -1.6 (-0%) |
| `string_arg` | 318.5 | 314.5 | -4.0 (-1%) |
| `string_return` | 184.5 | 184.6 | +0.1 (+0%) |
| `instance_call` | 335.6 | 334.7 | -1.0 (-0%) |

C5 takes 377 ns (−44%) off `GetInPath<int>("a.b")`, the marshaling of the
path; with C1 to C4 it costs 471 ns instead of 1.14 µs (−59%). C5's emulator
profile has no marshaling left (its total, 177 ns, is not comparable: the
host was busy with the container checks during that run). The other
cases move by −1.9 to +2.9% (hot-path mix 426.0 → 427.9 ns).

**GCM, before and after** (pb2; the base and the C4 tree in GCM, 8 launches
each):

| case | GCM | GCM after | Δ all |
| --- | ---: | ---: | ---: |
| `table_get_int` | 1227.6 | 255.1 | -972.5 (-79%) |
| `table_get_double` | 1499.0 | 243.4 | -1255.7 (-84%) |
| `table_get_long` | 1240.1 | 252.4 | -987.6 (-80%) |
| `table_get_bool` | 1294.7 | 238.3 | -1056.4 (-82%) |
| `table_get_string` | 1510.1 | 388.1 | -1122.0 (-74%) |
| `table_get_object` | 1770.0 | 499.2 | -1270.8 (-72%) |
| `table_get_table` | 2359.1 | 933.3 | -1425.8 (-60%) |
| `table_get_index` | 1019.5 | 230.4 | -789.0 (-77%) |
| `table_get_via_index` | 1272.9 | 454.9 | -818.0 (-64%) |
| `table_get_in_path` | 1153.1 | 844.8 | -308.2 (-27%) |
| `table_contains_key` | 734.2 | 188.1 | -546.1 (-74%) |
| `table_set_int` | 1045.8 | 251.9 | -794.0 (-76%) |
| `table_set_string` | 1230.7 | 307.1 | -923.6 (-75%) |
| `table_length` | 78.3 | 78.4 | +0.1 (+0%) |
| `table_for_each` | 1439.4 | 559.0 | -880.4 (-61%) |
| `string_arg` | 458.3 | 354.8 | -103.5 (-23%) |
| `string_return` | 355.0 | 224.1 | -130.9 (-37%) |
| `struct_boxed_field_get` | 1015.4 | 596.9 | -418.5 (-41%) |
| `instance_call` | 343.2 | 346.8 | +3.6 (+1%) |
| `unity_instance_method` | 3933.8 | 3830.9 | -102.9 (-3%) |

GCM gains what plain gains: −72 to −84% on reads of numbers, strings and
objects by string key, −60 to −77% on the other reads (`GetInPath` −27%,
without C5), −74 to −76% on `ContainsKey` and `Set`, −61% per `ForEach`
element. The other 27 cases move by −2.6 to +4.5% (the largest,
`struct_boxed_pass`, takes a path these changes do not touch), the hot-path
mix 450.9 → 446.9 ns (−0.9%).

### vs-bench on the phone **[C]**

**Before and after, every mode** (vs1; the base players and the C4 tree's,
plain, GCM and strict minimal, 8 launches each, one APK installed at a time).
The string cases, `cs.table.get` and the two Vector3 cases that moved (the
last three columns are differences):

| case | plain | plain after | GCM | GCM after | minimal | minimal after | Δ plain | Δ GCM | Δ minimal |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `lua2cs.instance.method.string.3arg.ret` | 1185.1 | 775.7 | 1252.8 | 812.1 | 1248.0 | 788.8 | -409.3 (-35%) | -440.7 (-35%) | -459.2 (-37%) |
| `lua2cs.instance.prop.get.string` | 571.3 | 451.9 | 664.7 | 498.0 | 603.0 | 485.7 | -119.4 (-21%) | -166.7 (-25%) | -117.4 (-19%) |
| `lua2cs.instance.prop.set.string` | 584.5 | 484.7 | 620.5 | 510.7 | 592.4 | 492.1 | -99.7 (-17%) | -109.9 (-18%) | -100.3 (-17%) |
| `lua2cs.instance.method.Vector3.3arg.ret` | 1225.9 | 1296.3 | 1284.8 | 1381.8 | 1256.9 | 1305.4 | +70.4 (+6%) | +97.0 (+8%) | +48.5 (+4%) |
| `lua2cs.instance.prop.set.Vector3` | 485.4 | 515.9 | 513.8 | 553.7 | 502.0 | 487.1 | +30.5 (+6%) | +39.9 (+8%) | -14.9 (-3%) |
| `cs2lua.string.3arg.ret` | 981.9 | 548.4 | 987.7 | 546.2 | 990.6 | 542.5 | -433.6 (-44%) | -441.5 (-45%) | -448.1 (-45%) |
| `cs.table.get` | 1303.6 | 254.1 | 1314.2 | 254.5 | 1230.0 | 247.5 | -1049.5 (-81%) | -1059.8 (-81%) | -982.6 (-80%) |

The groups (geometric mean of the cases' medians):

| group | plain | plain after | GCM | GCM after | minimal | minimal after | Δ plain | Δ GCM | Δ minimal |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Lua → C#: static calls, property and field (int) | 136 | 136 | 183 | 183 | 178 | 179 | 1.00x | 1.00x | 1.01x |
| Lua → C#: instance calls (int, long, double) | 367 | 363 | 413 | 386 | 371 | 374 | 0.99x | 0.93x | 1.01x |
| Lua → C#: instance properties and fields (int, enum) | 365 | 366 | 410 | 403 | 381 | 387 | 1.00x | 0.98x | 1.02x |
| Lua → C#: strings | 734 | 554 | 802 | 591 | 764 | 573 | 0.75x | 0.74x | 0.75x |
| Lua → C#: Vector3 and struct | 812 | 828 | 847 | 864 | 818 | 830 | 1.02x | 1.02x | 1.01x |
| Lua → C#: objects (new class, object and class arguments) | 1009 | 1031 | 1079 | 1039 | 1023 | 1016 | 1.02x | 0.96x | 0.99x |
| C# → Lua: int, long, double | 221 | 218 | 220 | 222 | 216 | 217 | 0.99x | 1.01x | 1.00x |
| C# → Lua: string | 982 | 548 | 988 | 546 | 991 | 542 | 0.56x | 0.55x | 0.55x |
| C# → Lua: Vector3 and struct | 1611 | 1609 | 1616 | 1611 | 1630 | 1657 | 1.00x | 1.00x | 1.02x |
| C# → Lua: object and class | 709 | 717 | 705 | 709 | 685 | 696 | 1.01x | 1.01x | 1.02x |
| Lua alone (loop, table) | 16 | 16 | 16 | 16 | 16 | 16 | 1.00x | 1.00x | 1.01x |

- **`cs.table.get`** (`LuaTable.Get<string, int>` from C#) costs 248 to 255 ns
  instead of 1.23 to 1.31 µs in all three modes (−80 to −81%).
- **Strings across the bridge** cost 17 to 45% less in every mode: three
  string arguments and a string result −409 to −459 ns (−35 to −37%), a
  string property −117 to −167 ns, setting one −100 to −110 ns, and a C# →
  Lua call with three strings and a string result −434 to −448 ns (−44 to
  −45%). The groups: Lua → C# strings 0.74x to 0.75x, C# → Lua strings
  0.55x to 0.56x.
- **Allocations do not change** (the allocation pass, every case and mode
  within 2 bytes): 45 bytes per Lua string that reaches C# (the C# string
  itself), 141 for three; none for `cs.table.get` or a string pushed to Lua.
- **Without strings or tables** the groups stay within 0.93x to 1.02x. Two
  Vector3 cases read +4 to +8% (`lua2cs.instance.method.Vector3.3arg.ret`
  +48 to +97 ns, `lua2cs.instance.prop.set.Vector3` −15 to +40 ns); their
  wrappers compile to the same C++ in both plain players (compared, the
  hashed names aside) and call nothing these changes touch, so this is code
  placement between builds, the same case that moved by up to 69 ns between
  two builds of one tree on 2026-10-29. GCM's instance calls read −5 to −10%,
  likewise without a code change.

### Tests

- **New cases** (`test(runtime): LuaTable's raw and protected paths, and type
  caches`, `test(runtime): GetInPath and SetInPath with ASCII and other
  paths`): `CaseLuaTableRawPaths` (`Get`, `ContainsKey` and `Set` with string
  and integer keys on present and missing keys, with and without a
  metatable, through `__index` and `__newindex`, a metatable without them, a
  key with a NUL and one that is not ASCII, a `LuaTable` whose value is a
  string, failing metamethods; `GetInPath` and `SetInPath` with ASCII, other,
  NUL-cut and long paths; the stack after each) and
  `CaseTypeOpsFollowRegistrations` (a caster, a checker, and get and push
  functions registered after a type was read, walked or written are used
  from then on). Both passed on the unchanged runtime first (Lua 5.5.1,
  legacy, all four suite passes; the path cases on the tree before C5)
  **[C]**. `EXPECTED_TESTS` is 530. `native_regression.py`'s new
  `ref_table_access` checks the four exports against `lua_gettable` and
  `lua_settable` (results, errors, stack) on every path.
- `Test/Native/run_managed.py --source-gen --source-gen-minimize
  --source-gen-fallback` on macOS arm64 (Mono 6.14.1), plugins built from
  this tree: Lua 5.5.1 and LuaJIT 2.1, each legacy and minimal (`--define
  XLUA_PRESET_MINIMAL`), 530 tests in each of two modes in every pass; and
  the same four with `--define THREAD_SAFE`. After C4, after C5, and after
  merging main (`95c58c28`) **[C]**. In the last round one `THREAD_SAFE` run
  (Lua 5.5.1, legacy, the GCM pass's coroutine mode) failed
  `CaseBridgeAfterDisposeThrows` with "the holder was not finalized", and the
  same run passed twice afterwards: the test makes an object on a thread
  that ends and expects one `GC.Collect` to finalize it, the race that
  Test/README.md describes for `CaseOwnerThreadCheck` (Mono can still scan
  the ended thread). It is main's test and these changes do not touch its
  path **[U]** for the cause.
- `native_regression.py` and `check_exports.py` on the four host libraries;
  `check_pinvoke_abi.py` (153 imports) **[C]**.
- `sanitize.sh` in `xlua-analysis:26.04` (`--memory 6g`, Linux arm64, with
  the image's .NET SDK): legacy and `XLUA_PRESET=minimal`, each on 5.3.6,
  5.4.9, 5.5.1 and LuaJIT, the suite with reflection and both source
  generator modes (530 tests each) **[C]**.
- `coverage.sh` in the same image, default `VERSIONS`: every gate passes;
  `xlua.c` 432 of 432 branches on 5.3.6 and 446 of 446 on 5.4.9 and 5.5.1
  (420, 434 and 434 before: the new functions' branches are all reached)
  **[C]**.
- `Test/UnityMatrix/il2cpp_suite.py all` on `xlua16k` (Unity 6000.3.21f1,
  arm64), after C4, after C5 and after the merge: plain and gcm 530/530 in both modes
  with no difference from the host; strict 530/530 with the 8 differences per
  mode that [il2cpp-suite.md](il2cpp-suite.md) expects (cases that need
  reflection on one side only) **[C]**.
- The Unity editor suite (`Test/UnityMatrix/prepare.py --editor-tools` and
  `editor_tests.py run`, 6000.3.21f1, macOS arm64) after the merge: mode
  sourcegen 48/48 (LuaLS: `good.lua` 0 and `bad.lua` 10 expected
  diagnostics; the log checks pass), mode reflection 41/41 **[C]**. The first
  sourcegen launch passed its tests but stopped at LuaLS, which was given a
  mise shim without a version; the second was given the binary.
- Every phone player of every session ran all its cases; the vs-bench
  players printed the same check line **[C]**.
- Not run: Lua 5.4.9 and 5.3.6 on the host and in players (the container's
  `sanitize.sh` and `coverage.sh` ran them), `NATIVE_LUA_PUSHSTRING` (the
  Windows workflow's .NET Framework pass), `HOTFIX_ENABLE`, iOS, WebGL,
  `fuzz_bridge.c`.

### What is left

After the changes, on the emulator **[C]**:

- **A numeric `Get`** is half Lua: the string key's interning
  (`internshrstr`) and the table lookup, the three P/Invokes' entries, and
  10.9 of 104 ns in `libxlua.so`'s PLT stubs, which its calls to its own
  exported `lua_*` functions go through **[U]** (`-fno-semantic-interposition`
  or hidden aliases would remove them for every bridge call; not tried).
  A `Get` of a number or a boolean in one P/Invoke that also converts and pops
  the value would save two more P/Invokes and the generic `Get<T>`; not tried
  **[U]**. Both done on 2026-10-31 (F2 and F3 in
  "[Three follow-ups](#three-follow-ups-struct-arguments-the-plt-and-one-call-reads-2026-10-31)").
- **A string read** spends 40% in the collector. Each read makes one C#
  string (as it must, unless strings are shared): a per-env cache of
  decoded short strings, keyed by the Lua string's address and checked by
  content, would return the same C# string for the same Lua string. That
  changes what `ReferenceEquals` says about two reads, which nothing in xLua
  relies on but user code could: **owner's decision** (not built).
- **`GetInPath`** keeps its `lua_pcall`: its path segments are new Lua
  strings, which a raw walk would allocate outside the protected call.
- **`ForEach`** keeps a `lua_pcall` per element (`xlua_pnext`): the action may
  change the table between two steps.
- **`Get<string, LuaTable>`** is a new `LuaTable` per read (a reference and a
  finalizer); unchanged.
- **`ObjectTranslator.GetStructArg<T>`** (a struct argument without a
  generated reader, such as the player bench's `struct_boxed_pass`, 1.76 µs)
  still makes a `typeof(T)` and a dictionary lookup per call
  (`ObjectTranslator.cs:1356` at `4bc5dd6e`); C1's cache would apply as it
  does to `Get<T>`. Not done here: outside the table path. Done on
  2026-10-31 (F1, below).

The size of the changes **[C]**: `libxlua.so` (arm64) +1,456 bytes (+506
compressed), the minimal variant +1,472 (+424); `libil2cpp.so` +26 KB in each
vs-bench player (the per-type caches' generic code); APKs +5.8 KB (plain),
+9.5 KB (GCM), +7.4 KB (strict minimal).

### Reproducing

Scripts, raw logs and the kept binaries are in `tablepath-2026-10-29/` on the
build disk: `scripts/` (`build_players.sh` with `mktree.py` for the candidate
trees, `unity_port.sh` for Unity's adb port, `bench_run.py` with
`phone_session.py` and `devlib.py` for the launches, one APK installed at a
time, `phone_all.sh` and `phone_pb3.sh` for the sessions, `aggregate.py`,
`pb_table.py` and `vs_tables.py` for the tables, `emu_prof.py`,
`categories.py` and `steps_table.py` for the emulator profiles,
`host_tests.sh`, `il2cpp_suite.sh`, `container.sh` and `editor_suite.sh` for
the tests); `pb1/`, `vs1/`, `pb2/`, `pb3/` (launches and results);
`prof-base/`, `prof-c4/`, `prof-c5/` (simpleperf records) with the
`symfs-*` directories; `base/`, `c1/` to `c5/`, `final/` (`il2cpp/` keeps each
player's `libil2cpp.so`, its symbols and the IL2CPP C++ of the bridge and the
generic methods); `phone/` (the device command log and four snapshots);
`host-tests/`, `il2cpp-suite/` to `il2cpp-suite3/`, `container-out/`,
`editor-*/` and `editor2-*/`; `protect_watch.py` answered the Play Protect
prompts. The candidate trees were copies of `d8b5f464` (main
`4bc5dd6e` with the bench commit) with each change's files; they match the
commits: C1 `1ec6e19c`, C2 `85b29942` and `51ffab62`, C3 `810cd03a`, C4
`5a3cb3bb`, C5 `32554010`. The `final` players (vs-bench in three modes,
player bench in GCM) were built from the C4 tree: C5 changes only
`GetInPath` and `SetInPath`, which no vs-bench case calls.

## Three follow-ups: struct arguments, the PLT and one-call reads (2026-10-31)

The section above left three changes untried ("What is left"): the per-type
cache for `GetStructArg<T>`, calls from `libxlua.so` to its own exports
through the PLT, and a number or boolean read from a `LuaTable` in one
P/Invoke. This section measures each and keeps what the numbers support.
Measured on the `xlua16k` emulator only; the phone was not used.

- **F1, struct arguments** **[C]**: `GetStructArg<T>` takes the get
  function from the type's cache and recognises a C# object holding a `T` by
  `obj is T`, without a Type lookup. A struct passed to C# and returned
  (`struct_boxed_pass`) costs 227 ns instead of 339 on the emulator (-33%,
  -35% in GCM); its Type and `Dictionary<Type, ...>` lookups fell from 15.7%
  to 5.2% of the case, the one left being `Push`'s `GetType()` of the
  returned struct.
- **F2, the PLT** **[C]**: confirmed with `llvm-readelf`: the Lua 5.5.1
  arm64-v8a library had 144 `JUMP_SLOT` relocations for its own functions
  (36 with `XLUA_EXPORTS=managed`), and the PLT stubs were 7.9 of the 81 ns
  of a profiled `Get<string, int>`. Linking with `-Wl,-Bsymbolic-functions`
  removes all of them and 7,504 bytes of the stripped library (899
  deflated); the exported surface is identical. The player bench's 45 cases
  are 3.4 to 5.9% faster (geomean of two sessions), `flag_bench.c` 2.3%
  (`table_sort` -15%, `table_concat` -11%, `string_format` -6%).
- **F3, a number or boolean in one P/Invoke** **[C]**: `LuaTable.Get` of an
  `int`, `long`, `uint`, `ulong`, `double`, `float` or `bool` reads,
  converts and pops the value in one native call instead of three, and
  skips `ObjectTranslator.Get<T>`: -4 to -10% per read on top of F2 (1.9 to
  4.1 ns). The same one-call form for `ContainsKey` measured 2 to 3 ns
  slower and was not kept.
- **All three** **[C]**: the player bench's `table_get_int` (the vs-bench's
  `cs.table.get`) costs 42.4 instead of 48.9 ns (-13%; GCM -20%), and the 45
  cases are 8.9% faster (geomean; GCM 10.3%, 44 of 45 faster in both); the
  vs-bench's `cs.table.get` 41.1 instead of 50.5 ns (-19%), its bridge cases
  4.0% faster. `libxlua.so` is 6,688 bytes smaller (584 deflated); the
  players' `libil2cpp.so` grows by 20 to 28 KB and their APKs by about 7 KB
  (Sizes below).

### Method

- **Trees** **[C]**: `base` is main at `102b059b`; F1, F2 and F3 are added
  in that order (F1 `954d1188`; F2 and F3 as committed after it). F2 changes
  only the plugin, so the F2 players are F1's C# with the F2 plugin. The
  players are the player bench (`size_report.py +pbench`, plain and GCM) and
  the vs-bench (`+vsbench`, plain), built as on 2026-10-30 (Unity
  6000.3.21f1, release ARM64 IL2CPP, Minimal stripping, Lua 5.5.1 plugin
  built with NDK r30 from the same tree, `ANDROID_ADB_SERVER_PORT=5038`).
  `f3` was F3's first form (separate per-type statics, `ContainsKey` in one
  call); `f3b` merges the statics into one class (`ValueOf<T>`, -3 KB of
  `libil2cpp.so`) and is the form timed in sessions 2 and 3; the committed
  form also reads `ContainsKey` as before (below).
- **Timing** **[C]**: `bench_emu.py`: every APK installed once on the
  emulator, launches in rotating order, 5 s apart, the median of the
  per-launch medians in ns per operation (each launch's median of 7 rounds of
  100,000). Session 1: base, F1, F2, F3 (plain), base and F3 (GCM), base and
  F3 (vs-bench), 8 launches each. Session 2: base and F3b in the same three
  players, 8 launches each. Session 3: F1, F2 and F3b (plain), 10 launches
  each, for the steps of F2 and F3 alone. The emulator runs about five times
  faster than the phone and is noisier: one APK's case moves by 5 to 10%
  between launches, and builds that differ only in unrelated code read 2 to
  4% apart in a session. A change counts here when it shows in every session
  and in nearly all of the cases it touches.
- **Profiles** **[C]**: `simpleperf record` on the emulator, the player
  bench's profile mode, one case at a time (16 s, recording 8), the main
  thread's self samples grouped by step (`categories.py`), in ns per
  operation of the profile-mode run.
- **`flag_bench.c`** **[C]**: the release library of the base tree and the
  same with `-Bsymbolic-functions` (`flag_sweep.py android --variants
  real23`), pushed to the emulator's `/data/local/tmp`, 5 interleaved rounds
  of 5 runs (`flagbench_emu.py`).
- **Sizes** **[C]**: `llvm-readelf -r` and `--dyn-syms` of the linked
  libraries, stripped size, raw deflate at level 6 as in an APK, and the
  players' `libil2cpp.so` and APKs.

### F1: struct arguments

A struct without a generated reader (not `[GCOptimize]`) reaches a
generated wrapper's parameter through `BindingApi.GetStructArg<T>`, which
called `ObjectTranslator.GetStructArg<T>`: `typeof(T)` and a
`Dictionary<Type, Delegate>` lookup for a get function, then, as there was
none, `GetStructArg(L, index, typeof(T))`: another `typeof(T)`,
`Nullable.GetUnderlyingType` and `Type.IsInstanceOfType`. The emulator's
profile of `struct_boxed_pass` (`v = T.ScaleVecBoxed(v, 1)`) at main: 69.5 ns
of Type lookups and 19.9 ns of dictionary lookups in a 570 ns operation
(profile mode), and 149 ns in Boehm's collector **[C]**.

Now `GetStructArg<T>` takes the get function from `TypeOps<T>` (the cache of
`Get<T>` since 2026-10-30) and tests a C# object with `obj is T`: `T` is a
struct or an enum, which has no subclasses, so the class test decides what
`IsInstanceOfType` decided; IL2CPP compiles it to a comparison of the
object's class. Nil, other values, tables and objects of another class take
the old path with its errors. `GetParams<T>` finds its integer reader once
per type (a static of `IntegerArgFunc<T>`; the table never changes), and
`GetParams<T>` and `GetDelegate<T>` read `typeof(T)` from `TypeOf<T>`.

| case | base | F1 | Δ | GCM base | GCM after all |
| --- | ---: | ---: | ---: | ---: | ---: |
| `struct_boxed_pass` (session 1) | 343.3 | 228.7 | -114.6 (-33%) | 335.8 | 224.9 (-33%) |
| `struct_boxed_pass` (session 2, base and all three) | 338.6 | 227.4 | -111.2 (-33%) | 333.8 | 215.4 (-35%) |

The other cases of session 1 move by -9 to +6% between base and F1 (noise
of the emulator; geomean of the 44 others -1.9%). The profile after the
change (F3b, 9 s): Type lookups 4.6% and dictionary lookups 0.6% of the case
(12.2% and 3.5% before); the rest of the Type lookups is
`ObjectTranslator.Push(object)`'s `GetType()` of the returned struct, which
the generated wrapper boxes and pushes as an object (What is left). The
collector's share rose from 26% to 36%.

F1's 16 s profile-mode run was killed by the emulator's low-memory killer at
1.5 GB of resident memory and 1.45 GB of swap: Lua runs the userdata
finalizers more slowly than the case creates userdata, so the object pool
and the boxed structs pile up, as `object_new` did on 2026-10-29
([benchmark-vs-puerts-zlua.md](benchmark-vs-puerts-zlua.md#what-is-left)).
At main the same run survived with fewer objects in the same 16 s. The timed
rounds, with a full collection before each, do not show it **[C]**.

### F2: the library's calls to its own exports

**The cause** **[C]**. `llvm-readelf -r` of the Lua 5.5.1 arm64-v8a library
at main: 265 `JUMP_SLOT` relocations, 144 of them for functions the library
defines (`lua_gettop`, `lua_rawgeti`, `lua_pushlstring`, `luaL_checkstack`
...). In an ELF shared library every exported function can be interposed,
so the linker routes the library's own calls to them through the PLT: an
`adrp`, a load of the target from the GOT and an indirect branch. The
minimal variant's version script keeps only the C# imports global, which
leaves 36 such slots. The profiles at main put 6.6 to 7.9 ns of each
`LuaTable` read in `libxlua.so`'s `@plt` (10 to 12% of the read), and 10.9 ns
of `struct_boxed_pass`.

**The fix** **[C]**. `-Wl,-Bsymbolic-functions` (Android and Linux,
`build/CMakeLists.txt`) binds every reference to a function the library
defines at link time; lld then emits direct calls. `-fno-semantic-interposition`
alone would not do: it lets clang bind calls inside one source file, but
`xlua.c` calls `lapi.c`'s functions. Hidden visibility with explicit
exports would have needed an alias per exported function. The result:

| library | `JUMP_SLOT` | to its own functions | stripped | deflated |
| --- | ---: | ---: | ---: | ---: |
| legacy (`XLUA_EXPORTS=all`), main | 265 | 144 | 389,592 | 166,116 |
| legacy with the flag | 121 | 0 | 382,088 (-7,504) | 165,217 (-899) |
| minimal (`XLUA_EXPORTS=managed`), main | 101 | 36 | 313,648 | 137,688 |
| minimal with the flag | 65 | 0 | 311,552 (-2,096) | 137,286 (-402) |

The dynamic symbol tables are identical entry by entry (432 and 211
entries: name, type, binding, visibility, defined or not), `DT_FLAGS`
keeps `BIND_NOW` and gains no `SYMBOLIC`, and the AArch64 property notes are
unchanged. `check_exports.py`, `check_pinvoke_abi.py` and
`check_android_elf.py` pass; `check_android_elf.py` and, for an ELF
library, `check_exports.py` now fail a library with a `JUMP_SLOT` for one of
its own functions, which main's libraries do.

**Speed** **[C]**. Player bench, plain, F1 → F2 (only the plugin differs):

| case | session 1 F1 | F2 | Δ | session 3 F1 | F2 | Δ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `table_get_int` | 52.3 | 45.8 | -6.5 (-12%) | 48.5 | 45.4 | -3.1 (-6%) |
| `table_get_index` | 44.6 | 40.4 | -4.1 (-9%) | 43.5 | 39.9 | -3.6 (-8%) |
| `table_get_via_index` | 91.1 | 81.2 | -10.0 (-11%) | 92.2 | 81.3 | -11.0 (-12%) |
| `static_call` | 36.1 | 32.7 | -3.4 (-10%) | 35.8 | 33.5 | -2.3 (-7%) |
| `instance_call` | 76.5 | 73.0 | -3.5 (-5%) | 76.2 | 69.3 | -6.9 (-9%) |
| hot-path mix (18 cases) | 84.8 | 79.6 | -6.2% | 82.0 | 80.3 | -2.0% |
| geomean of the 45 cases | | | -5.9% (42 faster) | | | -3.4% (35 faster) |

`flag_bench.c` on the emulator, the release library without and with the
flag: geomean -2.3% over 26 cases; `table_sort` -15.3% (`ltablib.c` calls
`lua_geti`, `lua_seti` and `lua_compare` per step), `table_concat` -11.3%,
`string_format` -6.1%, `string_pattern` -4.1%, `c_table_pgettable` -5.8%,
`c_push_csobj` -5.8%, `c_lua_calls_c` -4.6%; the VM-only cases -1.9 to
+2.3%. The vs-bench's pure Lua set agrees (`table_sort` -15 to -18%,
`table_concat` -10 to -12%, both sessions).

**Interposition** **[C]** for what the linker does, **[S]** for the loaders.
The flag changes one thing beyond speed: a library loaded before
`libxlua.so` that defines functions of the same names no longer replaces
them for the calls `libxlua.so` makes to itself. On Android that is a
`wrap.sh` preload of a debuggable app or a library loaded with
`RTLD_GLOBAL`; on Linux also `LD_PRELOAD`. Before, another Lua runtime in the
same process with exported `lua_*` functions could take over those calls,
with `libxlua.so`'s `lua_State`. A tool that hooks `lua_*` by interposition
still sees calls from outside the library. C#'s P/Invokes were never
interposed: they resolve through the library's own handle. Recorded in
[MIGRATING.md](../MIGRATING.md#native-plugins).

**Other platforms** **[C]**. The macOS arm64 `libxlua.dylib` calls
`_lua_gettop` with a direct `bl` (`objdump -d`; its 124 stubs are libc's):
Apple's ld64 binds a call inside one image directly, as the MSVC linker does
in a DLL, and iOS, WebGL and the consoles link a static library. OpenHarmony
links an ELF library with the same clang and lld and should gain the same;
nothing ran there, so it keeps the PLT **[U]**.

### F3: a number or boolean in one P/Invoke

`LuaTable.Get<TKey, int>` made three P/Invokes for the value
(`xlua_getref_getfield`, the get function's `xlua_tointeger64`, and
`lua_settop`) and ran `ObjectTranslator.Get<int>` and its `Ops<int>` cache
read in between (8.4 of 81 ns in the profile at main). Now
`xlua_getref_getvalue` and `xlua_getref_getfieldvalue` (`build/xlua.c`) do
the read of `xlua_getref_get` and `_getfield`, convert the value with the
same functions as before (`value_to_integer` with its validity for the
integer types, `lua_tonumber` for `double` and `float`, `lua_toboolean`) and
pop it; the value comes back in an `out long` (a double as its bits), and
C# applies the get function's rule and exceptions
(`LuaDLL.Bridge.int32Value` and the others: "number has no integer
representation", `XLUA_STRICT_INT_RANGE`). The types are the seven whose get
functions are the bridge's own and cannot be replaced
(`RegisterPushAndGetAndUpdate` refuses a type that has one); every other
type reads as before. 64-bit integers keep their bits, LuaJIT's int64 and
uint64 userdata included. Errors and the stack are as before.

| case | session 3 F2 | F3b | Δ | session 1 F2 | F3 | Δ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `table_get_int` | 45.4 | 41.8 | -3.6 (-8%) | 45.8 | 45.1 | -0.7 (-2%) |
| `table_get_double` | 42.0 | 40.1 | -1.9 (-5%) | 42.0 | 40.9 | -1.1 (-3%) |
| `table_get_long` | 44.9 | 41.8 | -3.1 (-7%) | 44.7 | 41.2 | -3.5 (-8%) |
| `table_get_bool` | 45.0 | 41.4 | -3.6 (-8%) | 44.8 | 41.0 | -3.9 (-9%) |
| `table_get_index` | 39.9 | 35.8 | -4.1 (-10%) | 40.4 | 36.7 | -3.8 (-9%) |
| `table_get_via_index` | 81.3 | 76.4 | -4.9 (-6%) | 81.2 | 82.1 | +0.9 (+1%) |
| `table_contains_key` (one call) | 34.2 | 36.3 | +2.1 (+6%) | 33.5 | 36.9 | +3.3 (+10%) |

In session 1 the whole F3 player read 2% slower than F2's in the cases F3
does not touch (geomean +2.0%), against -2.6% in session 3; the number reads
gained in both. The profile of `Get<string, int>` after all three changes
(65 ns in profile mode against 81 at main): `Ops<int>`, `Get<int>`, the
`xlua_getint32` and `xlua_tointeger64` wrappers and `lua_settop`'s wrapper
are gone, and `getref_value` adds 4.2 ns.

`ContainsKey` in one call (the type only, `conv` 0) was 2 to 3 ns slower
than `xlua_getref_getfield` and `lua_settop` in both sessions; its profile
shows why: the second P/Invoke's wrapper costs 1.6 ns, `getref_value` with its
out values 2.9 ns. `ContainsKey` stays as it was; the native functions keep
`conv` 0 for the type alone.

### All three

Session 2, base against F3b, every player (ns; GCM in the last columns):

| case | base | after | Δ | GCM base | GCM after | Δ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `struct_boxed_pass` | 338.6 | 227.4 | -111.2 (-33%) | 333.8 | 215.4 | -118.4 (-35%) |
| `table_get_int` | 48.9 | 42.4 | -6.5 (-13%) | 52.1 | 41.6 | -10.5 (-20%) |
| `table_get_double` | 47.3 | 40.0 | -7.2 (-15%) | 47.2 | 40.4 | -6.9 (-15%) |
| `table_get_long` | 46.8 | 41.5 | -5.3 (-11%) | 47.7 | 40.7 | -7.0 (-15%) |
| `table_get_bool` | 45.9 | 41.1 | -4.7 (-10%) | 46.6 | 40.7 | -5.9 (-13%) |
| `table_get_index` | 44.0 | 36.2 | -7.8 (-18%) | 42.8 | 35.7 | -7.1 (-17%) |
| `table_get_via_index` | 91.4 | 76.6 | -14.8 (-16%) | 93.0 | 77.0 | -16.0 (-17%) |
| `table_get_string` | 103.7 | 98.4 | -5.4 (-5%) | 105.3 | 95.1 | -10.2 (-10%) |
| `table_set_int` | 49.0 | 44.8 | -4.3 (-9%) | 47.4 | 45.1 | -2.3 (-5%) |
| `table_for_each` | 136.0 | 124.9 | -11.0 (-8%) | 133.2 | 124.5 | -8.7 (-7%) |
| `static_call` | 35.9 | 33.8 | -2.1 (-6%) | 46.5 | 42.2 | -4.3 (-9%) |
| `unity_instance_method` | 777.1 | 683.2 | -93.9 (-12%) | 708.0 | 682.3 | -25.7 (-4%) |
| hot-path mix (18 cases) | 85.9 | 79.4 | -7.5% | 88.3 | 79.8 | -9.7% |
| geomean of the 45 cases | | | -8.9% (44 faster) | | | -10.3% (44 faster) |

The vs-bench, plain: `cs.table.get` 50.5 → 41.1 ns (-19%; session 1: 47.5 →
41.2, -13%), the 38 bridge cases -4.0% (geomean; session 1 -5.8%), the 20
pure Lua cases -1.9% (-1.6%), with `table_sort` -15% and `table_concat`
-10%.

### Sizes **[C]**

The plugin (Lua 5.5.1, arm64-v8a, NDK r30) and the players; "final" is the
committed tree, built again after the timing:

| | base | F1 | F2 | final | Δ |
| --- | ---: | ---: | ---: | ---: | ---: |
| `libxlua.so` legacy, stripped | 389,592 | 389,592 | 382,088 | 382,904 | -6,688 |
| deflated | 166,116 | 166,116 | 165,217 | 165,532 | -584 |
| `libxlua.so` minimal, stripped | 313,648 | 313,648 | 311,552 | 312,416 | -1,232 |
| deflated | 137,688 | 137,688 | 137,286 | 137,542 | -146 |
| player bench `libil2cpp.so` | 11,513,192 | 11,522,064 | 11,522,064 | 11,541,472 | +28,280 |
| player bench APK | 13,482,622 | 13,485,102 | 13,484,210 | 13,489,398 | +6,776 |
| player bench GCM `libil2cpp.so` | 11,344,280 | | | 11,371,824 | +27,544 |
| player bench GCM APK | 13,438,262 | | | 13,445,918 | +7,656 |
| vs-bench `libil2cpp.so` | 11,137,728 | | | 11,157,664 | +19,936 |
| vs-bench APK | 13,345,102 | | | 13,352,026 | +6,924 |

`libxlua.so`: F2 takes off the PLT entries, GOT slots and `JUMP_SLOT`
relocations (above), F3 adds its two functions (+816 bytes stripped). In
`libil2cpp.so` (functions, `llvm-nm --print-size`, +19.7 KB of the player
bench's +28.3 KB; the rest is metadata and tables): F1 adds about 400 bytes
of `GetStructArg<T>` per struct type that reaches it (ten in the player
bench) and a type initializer per params element type (`IntegerArgFunc<T>`,
1.9 KB in all); F3 the seven readers, `ValueOf<T>`'s type initializers
(3.2 KB, against 1.0 KB for the class it replaces), and the scalar branch of
every `Get<TKey, TValue>` (about 240 bytes per instantiation; the player
bench has 15 `LuaTable` cases). F3's first form, with two classes of
per-type statics, was 3 KB larger.

### Tests

- **New** **[C]**: `CaseLuaTableNumberReads` (in F3's runtime commit):
  `LuaTable.Get` of the seven types against `GetInPath`,
  which still reads through `ObjectTranslator.Get<T>`, for 22 values (NaN,
  the infinities, 2^63, 1e300, numeric and other strings, booleans, a
  table, a function, `long.MinValue`, `long.MaxValue`, `ulong.MaxValue`,
  2^53 + 1 and a missing key), by string and integer key, raw and through
  `__index`: the same values and the same exception types and messages,
  `ContainsKey` likewise, the 64-bit values exact and the stack unchanged. It
  passed on the runtime before F3 (Lua 5.5.1 and LuaJIT, the four suite
  passes) **[C]**. `EXPECTED_TESTS` is 531. `native_regression.py`'s
  `ref_table_values` compares the two native functions with the separate
  calls for every value kind and conversion, raw, through `__index` and with
  a failing `__index`. `check_android_elf.py` and `check_exports.py` (ELF)
  fail a library with a PLT call to itself: main's Android libraries fail
  it (144 and 36 slots), F2's pass. F1 changes no behaviour; the struct and
  enum argument cases (`CaseParityStructArgs`, `CaseParityEnumArgs`, the
  `ParityStruct` cases of other classes) cover its paths.
- `Test/Native/run_managed.py --source-gen --source-gen-minimize
  --source-gen-fallback` on macOS arm64 (Mono 6.14.1): Lua 5.5.1 and LuaJIT
  2.1, legacy and minimal, each also with `--define THREAD_SAFE`, 531 tests
  in each of two modes in every pass: after F1 alone (530 tests; its
  `THREAD_SAFE` LuaJIT minimal run once more after a run that had
  compiled an unfinished F3 test file), after F3, and on the committed tree
  **[C]**. Again after the merge (below).
- `native_regression.py` (all checks) and `profiler_smoke.py` on the Lua
  5.5.1 and LuaJIT legacy host libraries, `check_exports.py` on all four,
  `check_pinvoke_abi.py` (155 imports) **[C]**.
- `check_android_elf.py` on the committed tree's Android libraries: Lua
  5.5.1 for armeabi-v7a, arm64-v8a, x86 and x86_64, and arm64-v8a of 5.4.9,
  5.3.6 and LuaJIT: all pass, with no PLT call to the library itself (121 to
  138 `JUMP_SLOT`s, all to libc, libm and libdl) **[C]**. Built only; only
  arm64-v8a Lua 5.5.1 ran (the emulator).
- `sanitize.sh` in `xlua-analysis:26.04` (`--memory 6g`, Linux arm64, so the
  libraries link with `-Bsymbolic-functions` too): legacy and
  `XLUA_PRESET=minimal`, each on 5.3.6, 5.4.9, 5.5.1 and LuaJIT, the suite
  with reflection and both source generator modes (531 tests each), and
  `check_exports.py`'s PLT check on every library **[C]**.
- `coverage.sh` in the same image, default `VERSIONS`: every gate passes;
  `xlua.c` 440 of 440 branches on 5.3.6 and 454 of 454 on 5.4.9 and 5.5.1
  (432, 446 and 446 before: the new branches are all reached) **[C]**.
- `Test/UnityMatrix/il2cpp_suite.py all` on `xlua16k` (Unity 6000.3.21f1,
  arm64), the committed tree: plain and gcm 531/531 in both modes with no
  difference from the host; strict 531/531 with the 8 differences per mode
  that [il2cpp-suite.md](il2cpp-suite.md) expects **[C]**.
- Not run: the phone; Lua 5.4.9 and 5.3.6 in players and on the macOS host
  (the container ran them); `HOTFIX_ENABLE`; `NATIVE_LUA_PUSHSTRING`; the
  Unity editor suite (no editor code changed); iOS, WebGL, Windows,
  OpenHarmony; the 32-bit and x86 Android libraries ran nothing.

### What is left

- **A struct returned to Lua** (`translator.Push(L, object)` in the
  generated wrapper) still boxes it and looks its `Type` up with
  `GetType()`: 4.6% of `struct_boxed_pass` after F1. A generic push with a
  per-type type id would need a source generator change; not done.
- **Lua's finalizers fall behind** a loop that creates userdata as fast as
  it can (F1's profile run, `object_new` on 2026-10-29): memory grows until
  a full collection. Not looked into.
- **OpenHarmony** keeps the PLT until someone runs its tests with
  `-Bsymbolic-functions` (`Test/Native/ohos_check.sh`).
- **The phone**: these changes were measured on the emulator only.

### Reproducing

Scripts, raw logs and kept binaries are in `followups-2026-10-31/` on the
build disk: `scripts/` (`build_plugin.sh`, `build_players.sh`,
`tree_from_worktree.py` for the candidate trees, `bench_emu.py` and
`timing.sh`, `after_timing.sh`, `timing3.sh` for the sessions,
`aggregate.py`, `pb_table.py`, `pb_geo.py` and `vs_geo.py` for the tables,
`emu_prof.py`, `cat_all.sh`, `categories.py` and `slice_prof.py` for the
profiles, `plt_count.sh`, `dynsym_diff.sh`, `symdiff.py` and `sizes.py` for
the libraries, `flagbench_emu.py`, `host_plugins.sh`, `host_tests.sh`,
`native_checks.sh`, `container.sh` and `il2cpp_suite.sh` for the tests);
`emu1/` to `emu3/` (launches and results), `prof-base/`, `prof-f1/`,
`prof-f3b/`, `prof-f3b-struct/` with the `symfs-*` directories,
`flagbench-emu/`, `plugins-*/` (the plugins with symbols), `base/`, `f1/`,
`f2/`, `f3/`, `f3b/` (`il2cpp/` keeps each player's `libil2cpp.so`, its
symbols and the IL2CPP C++), `host-tests/`, `container-out/` and
`il2cpp-suite/`.

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
>   against 1.29 µs; no P/Invoke). After xLua-NG's changes of 2026-10-30,
>   2.7 times (93 against 254 ns), and xLua-NG's string calls are 17 to 45%
>   faster than in the tables above. The changes of 2026-10-31 took another
>   19% off `cs.table.get` and 4% off the bridge cases on the emulator; the
>   phone was not measured again.
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
