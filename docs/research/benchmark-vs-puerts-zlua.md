# xlua-ng against PuerTS-Lua and zlua on a phone (2026-10-28)

Backlog item: "A device benchmark against PuerTS-Lua and zlua with the same
workload (zlua's case list)" ([backlog.md](../design/backlog.md),
[focus-creative-games.md](focus-creative-games.md#lessons)). This note
measures xlua-ng, PuerTS's Lua backend and zlua with one workload, in
release IL2CPP players on the test phone and, for reference, on the
`xlua16k` emulator. It is for the project's own decisions.

Marks: **[C]** confirmed here by a build, a run or the binary;
**[S]** secondary (a vendor's report or documentation);
**[U]** unconfirmed (an estimate or an explanation that was not profiled).

## Summary

- **zlua is the fastest for Lua → C# calls of numbers, enums, Vector3 and
  structs.** For instance properties and fields it is about 4 times faster
  than xlua-ng (95 against 374 ns on the phone, geometric mean). For
  instance calls it is 2.3 times faster, and for Vector3 and structs 2.6
  times faster. **[C]**
- **xlua-ng is the fastest for C# → Lua calls with numbers and strings.**
  It is about 3 times faster than both others (221 against 614 to 698 ns),
  and 1.5 times faster with strings. Its plain static call (75 ns) is also
  the fastest Lua → C# call measured. **[C]**
- **xlua-ng is the slowest with C# objects.** Passing or returning a class
  instance costs it 2.4 µs (Lua → C#, three arguments and a result) and
  3.8 µs (C# → Lua), against 0.3 to 1.4 µs for the others. **[C]**
- **PuerTS's Lua backend** matches xlua-ng on instance members. It is slower
  on static calls (265 against 75 ns) and much slower on Vector3 and structs
  (1.5 to 5.8 µs, 1.8 to 3.4 times xlua-ng). It is faster with objects. **[C]**
- **Size over an empty project** (APK, the bench included):
  - xlua-ng strict minimal adds 0.57 MB, the smallest;
  - PuerTS adds 0.97 MB, 0.44 MB without its WebSocket addon;
  - xlua-ng legacy adds 1.68 to 1.72 MB;
  - zlua adds 2.62 MB.
  zlua's call stubs cover every method of every assembly in the player,
  not only the bench. **[C]**
- **The Lua versions differ:** xlua-ng 5.5.1, PuerTS 5.4.8, zlua 5.5.0
  (patched). So do the binding approaches. Both other libraries put
  generated C++ into libil2cpp, which xlua-ng's design rules out
  ([AGENTS.md](../../AGENTS.md)). Read the numbers as "this workload, these
  builds, this phone".

## Libraries and versions

| | xlua-ng | PuerTS Lua backend | zlua |
| --- | --- | --- | --- |
| Version | this branch on main `fd4a43ce` (2026-10-28) | Unity release `Unity_v3.0.3` (2026-09-05), packages `com.tencent.puerts.core` and `com.tencent.puerts.lua` 3.0.3 | tag `v1.1.0` (commit `f9de12dd`), package `com.code-philosophy.zlua` 1.1.0 |
| Artifact and SHA-256 | built here: `libxlua.so` from `build/make_android_lua55.sh` (NDK r30), legacy and `XLUA_VARIANT=minimal XLUA_PRESET=minimal` | `PuerTS_Core_3.0.3.tar.gz` `feafc11bf2f9dbdb7e5fd2546124a771ea69b0f7c1a194740d47cfffb721cea0`, `PuerTS_Lua_3.0.3.tar.gz` `bd7b055832831116a17d95ba7f09b3d7e08e9c8f007d99e28432fff373e91961` (the release's own digests) | `codeload.github.com/focus-creative-games/zlua/tar.gz/refs/tags/v1.1.0` `968cb17e1ab0dd0790bb4f34615141c8fc7367f256447f10a49d31fd6bccf48a`; Lua `lua-5.5.0.tar.gz` from lua.org `57ccc32bbbd005cab75bcc52444052535af691789dba2b9016d5c50640d68b3d` (lua.org's published SHA-256) |
| Lua | 5.5.1, in `libxlua.so` | 5.4.8, in `libPapiLua.so` (prebuilt in the release) | 5.5.0 with zlua's "FastMetatable" patch (`ZLua~/patches/lua/lua-5.5/5.5.0.patch`), compiled into `libil2cpp.so` by IL2CPP |
| Binding in the player | C# wrappers from the source generator, calls through P/Invoke into `libxlua.so` | "xIl2cpp": generated C++ compiled into libil2cpp calls method pointers; Lua is reached through the pesapi function table of `libPuertsCore.so` | C++ call stubs and delegate thunks generated from the stripped assemblies, compiled into a patched libil2cpp |
| Configurations | legacy plain wrappers; legacy GCM (`GEN_CODE_MINIMIZE`); strict minimal (the minimal preset with generated bindings and no reflection fallback, GCM implied) | the default xIl2cpp mode, wrappers for the `[Binding]` types | the default (Lua 5.5.0, no HybridCLR) |

Only the release archives were used. PuerTS's archives match the digests
GitHub lists for the release. zlua publishes no release archive, only the
tag; the tag's source archive is what its installation docs install, by
git URL. The other libraries' repositories were read, and none of their
build scripts were run. The packages' own Editor code ran in the Unity
editor of their bench projects, as it does in any project that installs
them. **[C]**

zlua's installer is that Editor code. It copies the editor's il2cpp tree
into `Library/ZLua` and patches libil2cpp with
`patches/libil2cpp/6000/6000.0.0.patch`; on Unity 6000.3 for macOS it
takes the tree from `PlaybackEngines/iOSSupport/il2cpp`. It also patches
Lua, and Unity then compiles both into the player. That is how zlua is
shipped; there is no prebuilt form. It downloads the Lua tarball from
lua.org without a checksum (`Editor/Utils/LuaSourceCache.cs:238-250`), so
the bench put the hash-checked tarball into the installer's cache first
(`Library/ZLua/LuaSrcCache/downloads/`, which `LuaSourceCache.cs:241-243`
reuses). Nothing was downloaded during the build. The patch's README
says it was verified on 6000.3.16. It applied cleanly to 6000.3.21f1 with
macOS's `/usr/bin/patch`. **[C]**

The third-party projects are outside the repository
(`bench-vs-2026-10-28/tp/` on the build disk); this repository has only
the harness.

## Method

**Players** **[C]**: Unity 6000.3.21f1, Android, IL2CPP, ARM64 only,
release, Minimal managed stripping, IL2CPP code generation "Faster
runtime", C++ configuration Release. Every player is built by
`Test/MobileSmoke/SmokeBuild.cs` from a project with the same module list.

| player | built by | package |
| --- | --- | --- |
| empty project (size reference) | `size_report.py --configs vsbench`, variant `baseline` | `org.xluang.smoke` |
| xlua-ng plain | same, `ng-5.5+pkg+sg+vsbench` | `org.xluang.vs.plain` |
| xlua-ng GCM | same, `ng-5.5+pkg+sg+gcm+vsbench` | `org.xluang.vs.gcm` |
| xlua-ng strict minimal | same, `ng-5.5+minimal+strict+vsbench` | `org.xluang.vs.minimal` |
| PuerTS Lua | `vs_bench.py build --libs puerts` | `org.xluang.vs.puerts` |
| zlua | `vs_bench.py build --libs zlua` | `org.xluang.vs.zlua` |

**Bindings, as each library's users configure them:**

- **xlua-ng:** `[LuaCallCSharp]` for the four test types and
  `UnityEngine.Vector3`, `[GCOptimize]` on `TestStruct`, and
  `[CSharpCallLua]` for the twelve delegate types. This is the same
  configuration zlua-benchmark gives xLua
  (`xlua-benchmark/Assets/Benchmark/Editor/BenchmarkGenConfig.Generated.cs`,
  `Shared/TestStruct.XLua.cs`).
- **PuerTS:** the same types and delegates in a `[Configure]` class's
  `[Binding]` list. Before the build, `CSharpFileExporter.GenAll(..., onlyConfigure: true, noWrapper: false)`
  runs. That is the "Minimal Bridge" menu item with wrappers, so the bound
  types get generated wrappers as in xlua-ng. PuerTS's "Static Wrapper
  Mode" menu item, which wraps every public type, and its reflection
  modes were not measured.
- **zlua:** nothing to configure. `PrebuildCommand.GenerateAll` builds a
  throwaway player and writes stubs for every method in its stripped
  assemblies.

**Workload** **[C]**: `Test/MobileSmoke/VsBench/VsBench.cs`. It uses 38 of
the 287 cases of zlua-benchmark's own list
(`github.com/focus-creative-games/zlua-benchmark` at `ef18f1e9`,
2026-07-28):

- `shared/case_manifest.txt`, SHA-256 `1901e7bb…e8d3`;
- the Lua bodies of `shared/Lua/bench_lua2cs.lua` and `bench_cs2lua.lua`;
- the C# targets of `shared/CSharp/TestTargets.Generated.cs` and
  `TestTypes.cs`.

Each Lua → C# case runs its operation 10 times per loop iteration, as
zlua-benchmark does (`Lua2CsCallsPerLoop`, `shared/CSharp/BenchmarkConfig.cs:19`).
Each C# → Lua case calls a Lua function through a C# delegate in a C#
loop. Two Lua-only cases and one C# table read were added; they are not
in zlua's list. The mapping is below.

Every library runs the same Lua text. Only a three-line prelude differs,
the one that names the types:

| library | prelude (types) |
| --- | --- |
| xlua-ng | `CS.VsBench.TestTargets`, `CS.UnityEngine.Vector3` |
| PuerTS | `local CS = require('csharp')`, then the same names |
| zlua | `CSharp['Assembly-CSharp']['VsBench.TestTargets']`, `CSharp['UnityEngine.CoreModule']['UnityEngine.Vector3']` |

The C# side differs in how a Lua function becomes a delegate:

- **xlua-ng:** `LuaTable.Get<string, T>` on the module table;
- **PuerTS:** `ScriptEnv.Eval<T>("return __vsbench[name]")`;
- **zlua:** `LuaAppDomain.GetFunction<T>("vsbench", name)`. zlua has no
  `DoString`, so its loader serves the module.

**Deviations from zlua-benchmark**:

- **Return values:** the C# targets that return a string, object or class
  return their first argument instead of `null`, and `Prop_String` and
  `Prop_Class` start with a value. This way those results are marshalled
  rather than being `nil`.
- **Vector3 literals:** Vector3 is built from `1.0, 2.0, 3.0`. PuerTS's Lua
  backend rejects Lua integers for float parameters (`is_double`,
  `Puerts_il2cpp.cpp.txt:1833`).
- **Delegate types:** C# → Lua uses named delegate types, such as
  `RetInt3`, instead of `Func<...>`.
- **Timing loop:** the timing loop is the one of
  [descriptor-wrappers.md](descriptor-wrappers.md#call-cost-on-the-phone-and-the-emulator-2026-10-28),
  not zlua-benchmark's:
  - **zlua-benchmark:** 100 or 1,000 operations per round, 5 rounds, and
    garbage collectors stopped for allocating cases.
  - **Here:**
    - 7 rounds per launch, each running every case once with 100,000
      operations;
    - a full Lua and C# collection before each case;
    - collectors running during the timing, so allocating cases include
      their collection cost.
  - **Result:** a case's value per launch is the median of its rounds.

**Launches** **[C]**:

- **Phone:** the five players were installed side by side and launched in
  rotating order, 8 launches per player, 15 s apart.
- **Emulator:** the same, 8 launches per player, 10 s apart.
- **Values:** the tables give **the median of the per-launch medians, and
  in parentheses the minimum of them**, in ns per operation with the Lua or
  C# loop included.
- **Device settings:** nothing on the devices was pinned or changed. The
  players keep the screen on with `Screen.sleepTimeout` while in front.

**Devices**:

- **Phone:** moto g play 2024 (XT2413V), Snapdragon 680 (4x Cortex-A73,
  4x Cortex-A53), Android 14, 4 KB pages.
  - Thermal status was 0 before and after every launch, and every launch
    finished.
  - A case's per-launch medians mostly differ by 1 to 3% (compare the
    median and minimum columns); a few zlua and PuerTS cases spread up to
    about 15%.
  - The device log is `bench-vs-2026-10-28/phone/` on the build disk.
- **Emulator:** `xlua16k` (Android 16, API 36, 16 KB pages, arm64, 4
  cores) on the M2 Pro host. As in the descriptor measurement, it
  scatters more than the phone.

**Checks** **[C]**: every launch ended with a check line:

- the values the set cases left in C# (`Field_Int`, `Prop_Int` and
  `StaticField_Int` at 42, `Prop_Vector3` at (1, 2, 3));
- one call of each C# → Lua delegate (an int, a long of 2^40, a string, a
  Vector3, the same class instance, a struct).

All five players printed the same check line on both devices.

## Case mapping

`line` is the line in zlua-benchmark's `shared/case_manifest.txt`; `o` is
`TT.Instance`. Lua → C# rows give the operation that runs 10 times per
loop iteration; C# → Lua rows give the Lua function and the C# call.

| case | line | Lua → C# operation / C# → Lua call | C# member |
| --- | ---: | --- | --- |
| `lua2cs.static.method.0arg.void` | 57 | `TT.StaticNoRet()` | `static void StaticNoRet()` |
| `lua2cs.static.method.0arg.ret` | 58 | `local _ = TT.StaticRet()` | `static int StaticRet()` |
| `lua2cs.static.prop.get.int` | 83 | `local _ = TT.StaticProp_Int` | `static int StaticProp_Int { get; set; }` |
| `lua2cs.static.field.set.int` | 86 | `TT.StaticField_Int = v` (v = 42) | `static int StaticField_Int` |
| `lua2cs.ctor.class.3int` | 143 | `TC(1, 2, 3)` | `TestClass(int, int, int)` |
| `lua2cs.ctor.Vector3.full` | 146 | `V3(1.0, 2.0, 3.0)` | `Vector3(float, float, float)` |
| `lua2cs.instance.method.0arg.void` | 148 | `o:MethodVoidIntArity0()` | `void MethodVoidIntArity0()` |
| `lua2cs.instance.method.3arg.ret` | 155 | `local _ = o:MethodRetIntArity3(1, 2, 3)` | `int MethodRetIntArity3(int, int, int)` |
| `lua2cs.instance.method.6arg.ret` | 161 | `local _ = o:MethodRetIntArity6(1, 2, 3, 4, 5, 6)` | six `int` |
| `lua2cs.instance.prop.get.int` / `.set.int` | 200, 201 | `local _ = o.Prop_Int` / `o.Prop_Int = v` | `int Prop_Int { get; set; }` |
| `lua2cs.instance.field.get.int` / `.set.int` | 202, 203 | `local _ = o.Field_Int` / `o.Field_Int = v` | `int Field_Int` |
| `lua2cs.instance.method.long.3arg.ret` | 211 | `local _ = o:MethodRet_Long3(v, v, v)` (v = 999999) | `long MethodRet_Long3(long, long, long)` |
| `lua2cs.instance.method.double.3arg.ret` | 229 | same with v = 2.718 | `double MethodRet_Double3(...)` |
| `lua2cs.instance.method.object.3arg.ret` | 235 | same with v = 42 (a Lua integer as `object`) | `object MethodRet_Object3(object, object, object)` returns `a` |
| `lua2cs.instance.method.string.3arg.ret` | 241 | same with v = `'bench'` | `string MethodRet_String3(...)` returns `a` |
| `lua2cs.instance.prop.get.string` / `.set.string` | 242, 243 | `o.Prop_String` | `string Prop_String { get; set; }` = `"bench"` |
| `lua2cs.instance.method.Vector3.3arg.ret` | 259 | same with v = `V3(1.0, 2.0, 3.0)` | `Vector3 MethodRet_Vector33(...)` |
| `lua2cs.instance.prop.get.Vector3` / `.set.Vector3` | 260, 261 | `o.Prop_Vector3` | `Vector3 Prop_Vector3 { get; set; }` |
| `lua2cs.instance.field.get.Vector3` | 262 | `local _ = o.Field_Vector3` | `Vector3 Field_Vector3` |
| `lua2cs.instance.prop.get.enum` | 272 | `local _ = o.Prop_Enum` | `TestEnum Prop_Enum { get; set; }` |
| `lua2cs.instance.method.class.3arg.ret` | 277 | same with v = `TC(1, 2, 3)` | `TestClass MethodRet_Class3(...)` returns `a` |
| `lua2cs.instance.prop.get.class` | 278 | `local _ = o.Prop_Class` | `TestClass Prop_Class { get; set; }` (not null) |
| `lua2cs.instance.method.struct.3arg.ret` | 283 | same with v = `TS(1, 2, 3)` | `TestStruct MethodRet_Struct3(...)` (three ints) |
| `cs2lua.int.arity0.void` | 1 | `function() end`; `f()` | delegate `Void0` |
| `cs2lua.int.3arg.void` | 27 | `function(a, b, c) end`; `f(1, 2, 3)` | `VoidInt3` |
| `cs2lua.int.3arg.ret`, `long`, `double` | 28, 32, 38 | `function(a, b, c) return a end`; `f(i, 2, 3)` | `RetInt3`, `RetLong3`, `RetDouble3` |
| `cs2lua.object.3arg.ret` | 40 | same; three boxed ints | `RetObject3` |
| `cs2lua.string.3arg.ret` | 42 | same; `"bench"` | `RetString3` |
| `cs2lua.Vector3.3arg.ret` | 48 | same; `Vector3(1, 2, 3)` | `RetVector33` |
| `cs2lua.class.3arg.ret` | 54 | same; one `TestClass` instance | `RetClass3` |
| `cs2lua.struct.3arg.ret` | 56 | same; `TestStruct(1, 2, 3)` | `RetStruct3` |
| `lua.loop` (not in zlua's list) | – | `s = s + 1` | none: the Lua VM alone |
| `lua.table.rw` (not in zlua's list) | – | `t.x = t.x + 1` on a Lua table | none: table access inside Lua |
| `cs.table.get` (not in zlua's list) | – | C# reads `M.tbl.x` 100,000 times | xlua-ng `LuaTable.Get<string, int>`; PuerTS's Lua backend and zlua have no Lua table API in C# |

## Results on the phone (moto g play 2024) **[C]**

ns per operation, the median of 8 per-launch medians (the minimum in
parentheses).

| case | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | PuerTS Lua | zlua |
| --- | ---: | ---: | ---: | ---: | ---: |
| `lua2cs.static.method.0arg.void` | 75.0 (74.3) | 132.1 (129.6) | 125.1 (122.0) | 264.9 (260.9) | 93.1 (80.8) |
| `lua2cs.static.method.0arg.ret` | 83.4 (83.0) | 152.1 (151.2) | 147.2 (142.4) | 297.1 (286.4) | 92.1 (85.8) |
| `lua2cs.static.prop.get.int` | 237.2 (224.0) | 269.4 (266.3) | 262.9 (261.5) | 234.9 (230.9) | 96.9 (96.3) |
| `lua2cs.static.field.set.int` | 242.4 (241.0) | 296.7 (294.7) | 296.6 (294.3) | 235.2 (231.5) | 114.4 (105.3) |
| `lua2cs.ctor.class.3int` | 2794.8 (2765.2) | 2716.1 (2631.6) | 2767.4 (2740.0) | 1655.3 (1623.9) | 2153.9 (2123.5) |
| `lua2cs.ctor.Vector3.full` | 605.4 (602.7) | 636.8 (634.7) | 608.5 (607.4) | 1549.8 (1538.5) | 531.5 (530.0) |
| `lua2cs.instance.method.0arg.void` | 275.4 (274.6) | 298.5 (297.6) | 289.5 (288.5) | 285.3 (281.9) | 85.4 (85.2) |
| `lua2cs.instance.method.3arg.ret` | 384.3 (383.4) | 403.7 (403.0) | 398.2 (395.0) | 374.3 (369.5) | 160.5 (159.4) |
| `lua2cs.instance.method.6arg.ret` | 486.4 (483.0) | 503.8 (502.6) | 501.7 (495.9) | 442.3 (440.6) | 241.9 (239.8) |
| `lua2cs.instance.prop.get.int` | 378.2 (374.6) | 406.2 (405.7) | 398.7 (397.4) | 406.1 (402.6) | 94.0 (93.3) |
| `lua2cs.instance.prop.set.int` | 329.6 (329.2) | 361.7 (359.9) | 356.4 (355.9) | 342.9 (337.4) | 95.1 (93.1) |
| `lua2cs.instance.field.get.int` | 381.5 (375.8) | 408.4 (405.4) | 400.3 (397.9) | 396.4 (393.9) | 91.1 (90.4) |
| `lua2cs.instance.field.set.int` | 332.3 (329.4) | 363.4 (361.1) | 359.2 (356.9) | 328.7 (325.7) | 99.0 (98.5) |
| `lua2cs.instance.method.long.3arg.ret` | 386.7 (383.0) | 407.2 (402.1) | 404.7 (397.6) | 367.3 (366.1) | 174.8 (170.8) |
| `lua2cs.instance.method.double.3arg.ret` | 347.5 (343.6) | 369.4 (366.9) | 364.9 (361.3) | 379.0 (373.1) | 168.6 (166.1) |
| `lua2cs.instance.method.object.3arg.ret` | 2121.4 (2108.1) | 2166.1 (2137.9) | 2082.6 (2069.1) | 1091.6 (1079.1) | 2902.3 (2799.7) |
| `lua2cs.instance.method.string.3arg.ret` | 1243.0 (1201.8) | 1243.5 (1224.5) | 1250.3 (1236.7) | 1871.6 (1850.0) | 2272.8 (2240.7) |
| `lua2cs.instance.prop.get.string` | 574.6 (566.5) | 612.2 (597.4) | 603.6 (599.7) | 488.8 (482.0) | 157.5 (154.7) |
| `lua2cs.instance.prop.set.string` | 606.3 (592.9) | 618.0 (613.2) | 615.5 (611.2) | 836.5 (828.0) | 837.3 (825.3) |
| `lua2cs.instance.method.Vector3.3arg.ret` | 1246.3 (1216.8) | 1258.8 (1238.3) | 1237.9 (1226.2) | 2176.2 (2123.7) | 413.1 (412.1) |
| `lua2cs.instance.prop.get.Vector3` | 782.2 (775.4) | 789.8 (787.0) | 795.5 (783.7) | 1820.8 (1800.5) | 326.1 (322.6) |
| `lua2cs.instance.prop.set.Vector3` | 492.0 (488.5) | 526.7 (511.0) | 523.8 (505.8) | 436.7 (430.2) | 108.9 (108.3) |
| `lua2cs.instance.field.get.Vector3` | 788.8 (779.8) | 800.8 (794.9) | 805.1 (790.7) | 1803.1 (1776.3) | 315.4 (311.2) |
| `lua2cs.instance.prop.get.enum` | 461.4 (455.1) | 486.3 (485.4) | 480.5 (477.1) | 406.4 (401.9) | 94.7 (93.6) |
| `lua2cs.instance.method.class.3arg.ret` | 2433.7 (2394.2) | 2356.5 (2325.0) | 2386.1 (2358.1) | 1003.6 (970.8) | 315.4 (314.1) |
| `lua2cs.instance.prop.get.class` | 386.5 (378.5) | 408.9 (407.2) | 401.7 (396.5) | 408.4 (403.8) | 99.1 (94.8) |
| `lua2cs.instance.method.struct.3arg.ret` | 1369.4 (1344.2) | 1383.2 (1371.1) | 1374.7 (1352.0) | 2133.7 (2085.0) | 406.3 (399.8) |
| `lua.loop` | 7.0 (7.0) | 7.0 (7.0) | 7.2 (7.2) | 7.2 (7.2) | 9.8 (9.8) |
| `lua.table.rw` | 36.2 (36.0) | 36.2 (36.0) | 35.0 (34.6) | 32.5 (32.4) | 38.2 (38.1) |
| `cs2lua.int.arity0.void` | 191.7 (179.4) | 190.7 (177.9) | 197.3 (176.9) | 659.8 (656.2) | 585.8 (584.6) |
| `cs2lua.int.3arg.void` | 208.0 (206.5) | 209.8 (208.4) | 204.2 (201.6) | 698.6 (696.5) | 596.5 (596.0) |
| `cs2lua.int.3arg.ret` | 243.3 (242.7) | 240.7 (240.4) | 238.3 (235.8) | 708.8 (707.0) | 631.2 (631.1) |
| `cs2lua.long.3arg.ret` | 242.1 (241.5) | 240.4 (240.0) | 240.7 (238.5) | 715.2 (705.7) | 629.2 (629.0) |
| `cs2lua.double.3arg.ret` | 224.7 (224.4) | 226.2 (225.7) | 224.2 (222.5) | 708.3 (705.3) | 629.7 (629.4) |
| `cs2lua.object.3arg.ret` | 1501.4 (1458.6) | 1501.5 (1489.8) | 1484.5 (1474.5) | 1087.1 (1075.7) | 899.8 (899.1) |
| `cs2lua.string.3arg.ret` | 981.1 (970.3) | 994.1 (982.6) | 1009.8 (1003.0) | 1569.8 (1528.5) | 1507.9 (1479.2) |
| `cs2lua.Vector3.3arg.ret` | 1617.7 (1609.7) | 1609.2 (1599.8) | 1580.7 (1577.1) | 5777.6 (5733.3) | 1823.0 (1791.9) |
| `cs2lua.class.3arg.ret` | 3770.0 (3737.7) | 3722.0 (3686.1) | 3851.2 (3834.2) | 1363.6 (1316.0) | 889.1 (888.0) |
| `cs2lua.struct.3arg.ret` | 1735.9 (1720.3) | 1738.9 (1720.2) | 1719.7 (1704.1) | 5590.3 (5542.4) | 1792.8 (1771.3) |
| `cs.table.get` | 1254.5 (1238.5) | 1285.5 (1238.2) | 1284.2 (1272.7) | n/a | n/a |

By group, the geometric mean of the cases' medians and its ratio to
xlua-ng plain:

| group | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | PuerTS Lua | zlua |
| --- | ---: | ---: | ---: | ---: | ---: |
| Lua → C#: static calls, property and field (int) | 138 (1.00x) | 200 (1.45x) | 195 (1.41x) | 257 (1.86x) | 99 (0.72x) |
| Lua → C#: instance calls (int, long, double) | 370 (1.00x) | 391 (1.06x) | 386 (1.04x) | 366 (0.99x) | 158 (0.43x) |
| Lua → C#: instance properties and fields (int, enum) | 374 (1.00x) | 403 (1.08x) | 397 (1.06x) | 375 (1.00x) | 95 (0.25x) |
| Lua → C#: strings | 757 (1.00x) | 778 (1.03x) | 774 (1.02x) | 915 (1.21x) | 669 (0.88x) |
| Lua → C#: Vector3 and struct | 824 (1.00x) | 847 (1.03x) | 838 (1.02x) | 1475 (1.79x) | 316 (0.38x) |
| Lua → C#: objects (new class, object and class arguments) | 1537 (1.00x) | 1543 (1.00x) | 1533 (1.00x) | 928 (0.60x) | 665 (0.43x) |
| C# → Lua: int, long, double | 221 (1.00x) | 221 (1.00x) | 220 (1.00x) | 698 (3.16x) | 614 (2.78x) |
| C# → Lua: string | 981 (1.00x) | 994 (1.01x) | 1010 (1.03x) | 1570 (1.60x) | 1508 (1.54x) |
| C# → Lua: Vector3 and struct | 1676 (1.00x) | 1673 (1.00x) | 1649 (0.98x) | 5683 (3.39x) | 1808 (1.08x) |
| C# → Lua: object and class | 2379 (1.00x) | 2364 (0.99x) | 2391 (1.01x) | 1218 (0.51x) | 894 (0.38x) |
| Lua alone (loop, table) | 16 (1.00x) | 16 (1.00x) | 16 (1.00x) | 15 (0.96x) | 19 (1.22x) |

## Results on the emulator (`xlua16k`) **[C]**

Same layout, 8 launches per player. The emulator ran alone, under xlua-ng's
emulator and Unity locks (an earlier attempt stopped after 9 launches when
another job's Unity editor started, and is not counted).

| case | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | PuerTS Lua | zlua |
| --- | ---: | ---: | ---: | ---: | ---: |
| `lua2cs.static.method.0arg.void` | 19.3 (18.3) | 29.1 (27.0) | 27.4 (25.5) | 56.1 (54.2) | 19.9 (18.6) |
| `lua2cs.static.method.0arg.ret` | 22.7 (21.9) | 32.9 (30.1) | 36.2 (29.7) | 67.6 (61.5) | 22.1 (20.6) |
| `lua2cs.static.prop.get.int` | 48.0 (45.3) | 58.6 (52.0) | 60.6 (56.3) | 52.7 (47.3) | 22.9 (21.5) |
| `lua2cs.static.field.set.int` | 51.9 (49.3) | 59.0 (57.0) | 58.4 (54.4) | 51.5 (47.1) | 25.0 (24.2) |
| `lua2cs.ctor.class.3int` | 552.2 (535.2) | 572.9 (482.0) | 573.6 (546.5) | 551.9 (512.9) | 760.7 (727.4) |
| `lua2cs.ctor.Vector3.full` | 106.9 (92.9) | 111.4 (97.2) | 100.7 (94.3) | 360.4 (343.0) | 97.2 (92.6) |
| `lua2cs.instance.method.0arg.void` | 62.4 (58.0) | 64.6 (60.3) | 61.5 (58.5) | 60.0 (57.8) | 22.5 (21.0) |
| `lua2cs.instance.method.3arg.ret` | 84.3 (77.7) | 92.5 (81.8) | 84.4 (81.0) | 92.0 (77.1) | 44.6 (40.8) |
| `lua2cs.instance.method.6arg.ret` | 105.2 (99.2) | 103.2 (98.0) | 103.1 (97.9) | 102.9 (89.0) | 60.1 (56.7) |
| `lua2cs.instance.prop.get.int` | 85.5 (82.7) | 87.0 (82.9) | 85.5 (81.8) | 88.7 (81.5) | 24.9 (23.6) |
| `lua2cs.instance.prop.set.int` | 72.5 (67.3) | 78.1 (70.3) | 77.8 (70.2) | 71.6 (64.9) | 24.1 (22.8) |
| `lua2cs.instance.field.get.int` | 84.2 (82.2) | 87.7 (82.5) | 86.0 (82.8) | 89.5 (81.7) | 23.1 (22.0) |
| `lua2cs.instance.field.set.int` | 69.5 (66.8) | 73.5 (72.7) | 75.6 (68.8) | 71.0 (64.1) | 27.5 (24.7) |
| `lua2cs.instance.method.long.3arg.ret` | 83.9 (79.6) | 90.1 (82.9) | 81.8 (81.2) | 90.7 (79.3) | 42.9 (40.4) |
| `lua2cs.instance.method.double.3arg.ret` | 76.5 (73.4) | 80.0 (72.1) | 79.5 (70.3) | 91.9 (76.8) | 42.2 (40.8) |
| `lua2cs.instance.method.object.3arg.ret` | 493.3 (463.4) | 499.8 (434.9) | 494.2 (456.2) | 466.0 (426.9) | 1197.3 (1123.4) |
| `lua2cs.instance.method.string.3arg.ret` | 377.4 (369.8) | 370.3 (338.1) | 362.2 (347.6) | 579.5 (541.4) | 1590.3 (1535.8) |
| `lua2cs.instance.prop.get.string` | 122.0 (113.9) | 129.1 (116.5) | 124.9 (120.0) | 106.8 (101.4) | 38.4 (35.5) |
| `lua2cs.instance.prop.set.string` | 151.5 (126.7) | 146.7 (129.0) | 146.9 (135.2) | 277.1 (255.2) | 622.5 (553.6) |
| `lua2cs.instance.method.Vector3.3arg.ret` | 230.7 (218.2) | 235.6 (212.1) | 233.8 (212.9) | 543.4 (514.1) | 91.5 (78.4) |
| `lua2cs.instance.prop.get.Vector3` | 142.4 (127.7) | 135.3 (133.0) | 138.7 (126.0) | 435.8 (390.9) | 65.4 (60.2) |
| `lua2cs.instance.prop.set.Vector3` | 106.4 (94.3) | 103.5 (95.2) | 103.6 (97.3) | 90.4 (83.3) | 28.1 (26.3) |
| `lua2cs.instance.field.get.Vector3` | 141.2 (135.6) | 142.1 (138.8) | 137.5 (134.6) | 446.5 (405.4) | 69.2 (59.6) |
| `lua2cs.instance.prop.get.enum` | 107.2 (95.6) | 99.0 (95.9) | 96.9 (94.8) | 86.6 (82.5) | 24.2 (22.9) |
| `lua2cs.instance.method.class.3arg.ret` | 463.8 (438.2) | 436.3 (426.7) | 454.8 (435.1) | 243.7 (225.8) | 82.0 (78.8) |
| `lua2cs.instance.prop.get.class` | 86.8 (83.6) | 90.0 (83.4) | 86.0 (83.8) | 94.1 (83.8) | 25.5 (24.4) |
| `lua2cs.instance.method.struct.3arg.ret` | 268.6 (234.2) | 284.5 (252.3) | 267.0 (240.4) | 569.3 (513.0) | 90.1 (82.7) |
| `lua.loop` | 3.9 (3.7) | 3.8 (3.7) | 3.7 (3.7) | 3.7 (3.6) | 3.8 (3.7) |
| `lua.table.rw` | 7.5 (7.0) | 7.8 (7.1) | 7.3 (7.1) | 6.8 (6.6) | 11.1 (10.8) |
| `cs2lua.int.arity0.void` | 40.1 (35.4) | 38.8 (35.4) | 37.7 (33.9) | 211.6 (195.4) | 181.3 (174.7) |
| `cs2lua.int.3arg.void` | 43.8 (41.2) | 43.6 (41.6) | 44.0 (40.6) | 222.4 (202.1) | 182.6 (175.9) |
| `cs2lua.int.3arg.ret` | 53.0 (50.3) | 51.8 (48.3) | 52.1 (48.4) | 221.3 (208.6) | 187.7 (184.0) |
| `cs2lua.long.3arg.ret` | 52.3 (50.7) | 51.0 (47.0) | 51.4 (47.9) | 222.4 (203.9) | 189.2 (183.9) |
| `cs2lua.double.3arg.ret` | 50.6 (46.8) | 47.7 (43.7) | 49.6 (46.0) | 217.0 (201.5) | 187.6 (184.4) |
| `cs2lua.object.3arg.ret` | 336.0 (300.1) | 349.7 (311.5) | 353.2 (327.6) | 423.7 (387.8) | 260.3 (249.7) |
| `cs2lua.string.3arg.ret` | 269.1 (232.6) | 283.0 (257.2) | 269.3 (245.0) | 493.2 (457.1) | 896.9 (852.6) |
| `cs2lua.Vector3.3arg.ret` | 249.7 (230.9) | 243.2 (221.3) | 259.9 (242.5) | 1352.6 (1281.3) | 367.0 (353.9) |
| `cs2lua.class.3arg.ret` | 725.2 (692.7) | 690.0 (665.1) | 733.1 (700.6) | 328.6 (320.8) | 266.4 (248.7) |
| `cs2lua.struct.3arg.ret` | 272.5 (246.6) | 274.3 (249.8) | 266.8 (253.9) | 1262.0 (1183.8) | 383.8 (362.9) |
| `cs.table.get` | 250.6 (240.5) | 246.7 (230.2) | 249.2 (232.4) | n/a | n/a |

| group | xlua-ng plain | xlua-ng GCM | xlua-ng strict minimal | PuerTS Lua | zlua |
| --- | ---: | ---: | ---: | ---: | ---: |
| Lua → C#: static calls, property and field (int) | 32 (1.00x) | 43 (1.32x) | 43 (1.34x) | 57 (1.75x) | 22 (0.69x) |
| Lua → C#: instance calls (int, long, double) | 81 (1.00x) | 85 (1.05x) | 81 (1.00x) | 86 (1.06x) | 41 (0.50x) |
| Lua → C#: instance properties and fields (int, enum) | 83 (1.00x) | 85 (1.02x) | 84 (1.02x) | 81 (0.98x) | 25 (0.30x) |
| Lua → C#: strings | 191 (1.00x) | 191 (1.00x) | 188 (0.98x) | 258 (1.35x) | 336 (1.76x) |
| Lua → C#: Vector3 and struct | 156 (1.00x) | 157 (1.01x) | 152 (0.98x) | 354 (2.27x) | 68 (0.44x) |
| Lua → C#: objects (new class, object and class arguments) | 324 (1.00x) | 326 (1.01x) | 324 (1.00x) | 277 (0.86x) | 209 (0.65x) |
| C# → Lua: int, long, double | 48 (1.00x) | 46 (0.97x) | 47 (0.98x) | 219 (4.59x) | 186 (3.89x) |
| C# → Lua: string | 269 (1.00x) | 283 (1.05x) | 269 (1.00x) | 493 (1.83x) | 897 (3.33x) |
| C# → Lua: Vector3 and struct | 261 (1.00x) | 258 (0.99x) | 263 (1.01x) | 1307 (5.01x) | 375 (1.44x) |
| C# → Lua: object and class | 494 (1.00x) | 491 (1.00x) | 509 (1.03x) | 373 (0.76x) | 263 (0.53x) |
| Lua alone (loop, table) | 5 (1.00x) | 5 (1.01x) | 5 (0.96x) | 5 (0.93x) | 6 (1.20x) |

## Sizes **[C]**

APK bytes of each bench player, and what it adds to the empty project
(the same `SmokeBuild.cs` settings, no Lua library and no bench code). The
library columns give uncompressed bytes in the APK, with their `PT_LOAD`
alignment where it is not 16 KB.

| player | APK | over empty | `libil2cpp.so` over empty | `libunity.so` over empty | metadata over empty | Lua libraries |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| empty project | 12,279,052 | – | (8,652,488) | (10,037,792) | (1,416,516) | – |
| xlua-ng plain | 13,995,990 | +1,716,938 | +2,760,136 | +1,169,664 | +485,228 | `libxlua.so` 388,136 |
| xlua-ng GCM | 13,959,274 | +1,680,222 | +2,622,432 | +1,169,664 | +490,576 | `libxlua.so` 388,136 |
| xlua-ng strict minimal | 12,844,786 | **+565,734** | +1,247,912 | +16,592 | +196,432 | `libxlua.so` 312,176 (minimal variant) |
| PuerTS Lua | 13,247,262 | +968,210 | +825,176 | +16,592 | +152,504 | `libPapiLua.so` 342,736 (**4 KB aligned**), `libPuertsCore.so` 39,912, `libWSPPAddon.so` 1,782,768 |
| zlua | 14,899,420 | +2,620,368 | **+6,841,536** (Lua included) | +513,376 | +955,856 | none (Lua is in `libil2cpp.so`) |

Notes:

- **Compressed sizes:** in the APK, `libWSPPAddon.so` takes 524,795 bytes
  compressed. PuerTS without it would add about **443 KB**. It is
  PuerTS's WebSocket client for scripts ([puerts.md](puerts.md)), which
  the Core package enables for Android; the bench does not use it.
- **What xlua-ng legacy adds to `libunity.so`:** legacy xlua-ng makes
  `libunity.so` 1.17 MB bigger (574 KB compressed): engine modules that
  legacy code references, kept by engine code stripping. Strict minimal
  and PuerTS add 16 KB there, zlua 0.5 MB. That is about a third of what
  legacy xlua-ng adds to the APK. Which module is kept was not traced
  here; `size_report.py` notes xLua's use of `UnityEngine.WWW` in
  StaticLuaCallbacks as one such reference. **[U]**
- **zlua's `libil2cpp.so`:**
  - **What it holds:** zlua's runtime, Lua 5.5.0 and the generated stubs.
    `MethodBridgeStub.cpp` is 3.6 MB of C++ and `DelegateBridgeStub.cpp`
    215 KB.
  - **Why it is large:** it binds every method of every stripped assembly,
    whether Lua uses it or not. This is the cost of its "zero
    configuration".
  - **How it would change:** it grows with the project's code. Under High
    stripping it would shrink, and members reached only from Lua would
    then be lost ([focus-creative-games.md](focus-creative-games.md)).
    **[U]**
- **16 KB pages:** PuerTS's `libPapiLua.so` has 4 KB `PT_LOAD`
  alignment. On the 16 KB emulator, Android 16 showed the "This app isn't
  16 KB compatible" dialog in front of the PuerTS player at its first
  launch. The player waited behind it until OK was tapped (vs_bench.py
  now taps it on the emulator). This confirms the gap noted in
  [puerts.md](puerts.md). Every other library in these APKs is 16 KB
  aligned. **[C]**

## What the numbers say

**zlua.**

- **Where it is fast:** its Lua → C# calls with numbers, enums, Vector3
  and structs are the fastest by far. Instance property and field access
  costs about 95 ns, and an instance call 85 to 240 ns. **[C]**
- **Where it is slow:**
  - strings: 2.3 µs for three string arguments and a string result, and
    0.84 µs to set a string property;
  - a Lua integer passed as `object`: 2.9 µs;
  - the C# → Lua calls: 586 to 631 ns, about 2.8 times xlua-ng.
- **Its own report:** it shows the opposite for C# → Lua: on Win64,
  xLua's arity-0 call costs the same as zlua's, 27.3 against 27.5 ns
  (zlua-benchmark `reports/comparison_20260728_121554.md:299`). **[S]**
  That is upstream xLua with Lua 5.3 on a desktop. Why zlua's delegate
  thunks cost this much on ARM64 was not profiled. **[U]**
- **Its Lua loop:** it is about 40% slower than the other two on the phone
  (9.8 against 7.0 ns per `s = s + 1`) but not on the emulator (3.7 against
  3.7). The likely causes are IL2CPP's compiler flags for libil2cpp, or
  the FastMetatable patch. **[U]**

**xlua-ng.**

- **Where it leads:**
  - **Static calls:** the plain static call is the fastest Lua → C# call
    measured, 75 ns. GCM adds 57 ns to it, as already measured in
    [descriptor-wrappers.md](descriptor-wrappers.md).
  - **C# → Lua:** it leads for numbers and strings, and ties with zlua for
    Vector3 and structs.
- **Instance members:** they cost 275 to 490 ns, against 85 to 240 ns for
  zlua. The gap to the static call (75 ns) is the instance path:
  - the `__index` lookup through the type's metatable;
  - the `GetObject` of the userdata.
  This path is the largest lever for xlua-ng's Lua → C# speed. **[U]**
- **C# objects:**
  - **Lua → C#:** passing three class instances and getting one back
    costs 2.4 µs.
  - **C# → Lua:** the same costs 3.8 µs.
  - **The cost per object:** about 0.4 µs per object argument and about
    1 µs per object result, consistent with `class_object_arg` and
    `object_return_cached` in the descriptor measurement.
  - **The others:** PuerTS takes 1.0 and 1.4 µs, zlua 0.3 and 0.9 µs.
  - **Where the time goes** **[U]**: the translator's object pool and its
    reverse map in C#, plus the userdata cache in Lua. Each costs several
    P/Invoke round trips.
- **Configurations:** strict minimal is as fast as GCM, or slightly
  faster. GCM costs 2 to 8% over plain for instance members and 45% for
  the static cases.
- **Table read from C#:** `LuaTable.Get<string, int>` costs 1.25 µs per
  read. The other two have no such API.

**PuerTS's Lua backend.**

- **Instance members:** about xlua-ng plain's cost, 285 to 440 ns.
- **Static calls:** 265 ns, 3.5 times xlua-ng plain.
- **Vector3 and structs:** 1.5 to 2.2 µs Lua → C# and 5.6 to 5.8 µs
  C# → Lua. These are the slowest results in the bench. [puerts.md](puerts.md)
  records a malloc per blittable struct; why the cost reaches this level
  was not profiled. **[U]**
- **Objects:** the fastest after zlua.

**The emulator** **[C]**: costs are 3 to 5 times lower than on the phone
and the large differences keep their direction. The ratios move:

- **Static calls:** xlua-ng plain and zlua tie (19 to 23 ns).
- **Objects:** xlua-ng's gap is smaller (1.5 times zlua, against 2.3 times
  on the phone).
- **C# → Lua:** xlua-ng's lead is larger (3.9 to 4.6 times).
- **zlua's strings:** they are relatively slower.
- **Lua alone:** the loop costs the same for all three, and zlua's table
  read-modify-write is the slowest (11.1 against 6.8 to 7.8 ns).

As in the descriptor measurement, the emulator does not predict the
phone's costs.

**Frame cost, for scale** **[U]**: 10,000 instance property reads per
frame on this phone cost:

| library | ms per frame | share of a 120 fps frame |
| --- | ---: | ---: |
| xlua-ng | 3.8 | 45% |
| PuerTS | 4.1 | 49% |
| zlua | 0.95 | 11% |

## Caveats

- **Different Lua versions.** xlua-ng runs 5.5.1, PuerTS 5.4.8 and zlua
  5.5.0 with its own patch. The Lua-only cases show how much the VMs
  differ on this phone: 7.0 to 9.8 ns per loop step, 32.5 to 38.2 ns per
  table read-modify-write.
- **Different binding approaches and different rules.**
  - **PuerTS and zlua:** they generate C++ against libil2cpp internals.
    zlua also patches libil2cpp and installs a modified il2cpp tree per
    project. That is how they avoid P/Invoke and reach managed code
    through method pointers.
  - **xlua-ng:** its rules allow only public, documented Unity mechanisms
    ([AGENTS.md](../../AGENTS.md), [presets.md](../design/presets.md)), so
    part of the Lua → C# gap is the price of that choice.
  - **What was not tried:** whether the gap closes with fewer P/Invoke
    transitions per call, for example native helpers that push or fetch
    several values at once, was not tried
    ([focus-creative-games.md](focus-creative-games.md#lessons)).
- **Different features.**
  - **zlua:** one Lua state per process, no `DoString`, no table API from
    C#, no hotfix and no sandbox. A Lua module comes from a loader.
  - **PuerTS's Lua backend:** no hotfix and no limits.
  - **xlua-ng:** several environments, hotfix, the core preset's limits
    and lossless 64-bit integers. zlua turns a `ulong` above 2^63 into a
    float.
  - **What the bench covers:** none of these differences, only call
    costs.
- **Configuration choices.**
  - **PuerTS:** its wrappers were generated for the bound types only, a
    custom call of its exporter that no menu item offers. Its menu items
    give either every public type, or reflection with minimal bridges.
  - **xlua-ng:** got the configuration zlua-benchmark gives xLua.
  - **zlua:** has nothing to configure.
- **Subset of cases.** 38 of zlua's 287 cases. The 249 others cover more
  primitive types (bool, char, byte, short, uint, ulong, float), Vector2
  and Vector4, `int[]`, and static members of the other types. The groups
  here cover each kind once or more.
- **GC included.** Collectors run during the timing; zlua-benchmark stops
  them for allocating cases. Cases that allocate (strings, objects,
  structs in PuerTS, new `TestClass`) include collection work. That is
  closer to a game, but it is not zlua's method.
- **One phone, one emulator.** No iOS, no other Android device, no High
  stripping, no Development builds.

## Not run

- **zlua:** other Lua versions (5.4, LuaJIT) and HybridCLR mode.
- **PuerTS:** "Static Wrapper Mode" (every public type) and the reflection
  modes.
- **xlua-ng:** Lua 5.4.9, 5.3.6 and LuaJIT, and the descriptor prototype
  (it is on its own branch).
- **The rest of zlua's case list:** the remaining 249 cases. Also not
  measured: allocations per call and a CPU profile (simpleperf) of the
  slow paths named above.
- **Upstream Tencent/xLua:** not in this bench.
  [descriptor-wrappers.md](descriptor-wrappers.md) and
  [build/SIZE.md](../../build/SIZE.md) compare it with xlua-ng.

## Reproducing

1. **xlua-ng players:** with plugins from `build/make_android_lua55.sh`
   (and the minimal variant) in `PLUGINS`, and `XLUA_SIZE_TMPDIR` set to a
   short directory, run:

   ```
   size_report.py run --work WORK/work --plugins PLUGINS --platforms android --configs vsbench
   ```

2. **Third-party players:** with the release archives above, downloaded
   and checked against their SHA-256, run:

   ```
   vs_bench.py build --work WORK --unity UNITY --puerts-core PuerTS_Core_3.0.3.tar.gz --puerts-lua PuerTS_Lua_3.0.3.tar.gz --zlua zlua-v1.1.0.tar.gz --lua-tarball lua-5.5.0.tar.gz
   ```

3. **Device runs:** `vs_bench.py device phone|emu --work WORK --sdk SDK --rounds 8`,
   under xlua-ng's phone or emulator lock, with no Unity editor running.
4. **Tables:** `vs_bench.py report --work WORK` writes `vs-report.json`
   and `vs-report.md`.

The raw logs of this run are in `bench-vs-2026-10-28/` on the build disk:
`phone/`, `emu/`, the trial runs `emu-trial*`, and an emulator run stopped
when another job's Unity editor started (`emu-part1-stopped`).
