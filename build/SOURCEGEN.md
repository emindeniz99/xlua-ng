# Source generator measurements

Measurements for [docs/design/source-generator.md](../docs/design/source-generator.md),
section 7 and milestones M2 to M4. The spike that came before is in
[SOURCEGEN_SPIKE.md](SOURCEGEN_SPIKE.md).

## Bridge speed: source-generated against legacy wrappers (M2, 2026-10-18)

Setup:

- macOS 27 arm64, Lua 5.5.1 (Release plugin from `16183dd`), Mono 6.14.1;
- `Test/Native/benchmark.py --modes gencode-csc` and `--modes sourcegen`, `--no-lua
  --runs 5`, three rounds, one mode at a time and alternating;
- the table gives the lowest of the three medians (ns per call) and the
  managed and Lua bytes allocated per call.

Both modes compile `Test/Native/Benchmark.cs` with the .NET SDK 10.0.401 csc
against Mono's `lib/mono/4.5`: `gencode-csc` with the legacy generator's
wrappers, `sourcegen` with the source generator's. The machine was shared
with other agents' builds (load average 20 to 70), so differences under
about 10% are noise.

| Case | legacy ns | source generator ns | change | B/op (managed + Lua), legacy / source |
|---|---:|---:|---:|---|
| `lua_cs.static_call` | 50.3 | 50.0 | -1% | 0+0 / 0+0 |
| `lua_cs.instance_call` | 83.5 | 82.1 | -2% | 0+0 / 0+0 |
| `lua_cs.property_get` | 91.4 | 92.7 | +1% | 0+0 / 0+0 |
| `lua_cs.property_set` | 74.9 | 78.4 | +5% | 0+0 / 0+0 |
| `lua_cs.field_get` | 91.6 | 92.9 | +1% | 0+0 / 0+0 |
| `lua_cs.field_set` | 77.9 | 75.2 | -3% | 0+0 / 0+0 |
| `lua_cs.string_arg` | 85.9 | 82.8 | -4% | 48+0 / 48+0 |
| `lua_cs.string_return` | 86.4 | 85.5 | -1% | 0+0 / 0+0 |
| `lua_cs.struct_gcopt_pass` | 164.2 | 298.1 | +82% | 0+52 / 32+36 |
| `lua_cs.struct_boxed_pass` | 327.6 | 307.9 | -6% | 32+36 / 32+36 |
| `lua_cs.struct_gcopt_new` | 151.3 | 276.6 | +83% | 0+52 / 32+36 |
| `lua_cs.struct_boxed_new` | 244.1 | 281.8 | +15% | 32+36 / 32+36 |
| `lua_cs.struct_gcopt_field_get` | 117.3 | 179.5 | +53% | 0+0 / 0+0 |
| `lua_cs.struct_boxed_field_get` | 183.1 | 179.5 | -2% | 0+0 / 0+0 |
| `lua_cs.object_return_cached` | 170.6 | 168.6 | -1% | 0+0 / 0+0 |
| `lua_cs.struct_accessor_get` | 14.0 | 14.3 | +2% | 0+0 / 0+0 |
| `lua_cs.struct_accessor_set` | 16.6 | 16.6 | 0% | 0+0 / 0+0 |
| `lua_cs.xlua_field_lookup` | 5.9 | 5.9 | 0% | 0+0 / 0+0 |
| `cs_lua.delegate_int` | 66.0 | 73.6 | +12% | 0+0 / 0+0 |
| `cs_lua.luafunction_call` | 315.8 | 323.7 | +3% | 264+0 / 264+0 |
| `cs_lua.luafunction_func` | 222.1 | 219.0 | -1% | 0+0 / 0+0 |
| `cs_lua.delegate_string_arg` | 125.2 | 127.5 | +2% | 0+0 / 0+0 |
| `cs_lua.delegate_struct_gcopt` | 154.1 | 275.4 | +79% | 0+52 / 32+36 |
| `cs_lua.delegate_struct_boxed` | 267.6 | 302.9 | +13% | 32+36 / 32+36 |
| `table.*` (4 cases) | 194 to 262 | 188 to 266 | -3% to +1% | 0 / 0 |
| `env.create_dispose` | 3,276,975 | 12,004,800 | +266% | 193,816 / 758,952 |

Reading:

- **Member calls through wrappers** (calls, fields, properties, strings,
  cached objects) are within ±5% of the legacy wrappers: the generated
  bodies are the same.
- **`[GCOptimize]` structs** are 53% to 83% slower and allocate 32 managed
  bytes per call: until milestone M4 the source generator has no struct
  pushers, so these structs are boxed like any other struct
  (`struct_*_boxed` shows the same numbers).
- **C# calling Lua** (`cs_lua.*`) goes through CodeEmit's bridges in the
  `sourcegen` mode, since generated delegate bridges are M3.
- **`env.create_dispose`** is 3.7 times slower: without generated delegate
  bridges `DelegateBridge.Gen_Flag` stays off, so each `LuaEnv` runs CodeEmit's
  `initCSharpCallLua`, which scans every loaded type for `CSharpCallLua`
  configuration. It goes back to the legacy number once M3 registers
  bridges.

The five-mode run (`reflection,emit,gencode,gencode-csc,sourcegen`, 7 runs)
before the interleaved rounds is in the output folder; it was taken under
heavier load and only confirms the ordering (reflection 4 to 8 times slower
than any generated mode for calls).

## Generator and compile time by number of bound types (M2, 2026-10-18)

`XLua.SourceGenerator.Tests <repo> --timing 100 500 2000`: the generator in
`CSharpGeneratorDriver` 4.3.1 on .NET 10, with N public, non-generic,
non-obsolete BCL types (classes, structs, interfaces, enums, in name order) in
`[assembly: LuaCallCSharp]`, as a stand-in for a UnityEngine list. Times in ms,
same machine and load as above:

| Types | Generated lines | Generator, empty cache | Generator, new driver (cache filled) | After an unrelated edit | of which member models | Compile of the output | Compile without wrappers | Errors |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 14,414 | 232 | 38 | 43 | 14 | 271 | 15 | 0 |
| 500 | 82,312 | 541 | 177 | 190 | 60 | 1,319 | 14 | 0 |
| 2,000 | 378,761 | 2,524 | 1,853 | 1,698 | 364 | 5,054 | 56 | 0 |

- **Output:** about 190 lines per type; the wrappers of all 2,000 types compile
  without errors.
- **The member models** cost about 1.2 ms per type; the static cache of models
  of metadata types (`Wrappers.cs`, keyed by module version id) brings that
  to about 0.2 ms per type for a later compilation, which is what Unity's
  compiler server does (a new driver per compilation, the generator kept
  loaded). The rest of the generator time is Roslyn adding the sources.
- **Compiling the generated code dominates:** about 2.5 ms per type. A
  2,000-type list adds about 5 s to every compile of the assembly that holds
  it, which is why the owner's decision 4 puts engine and package bindings in
  an `XLuaBindings` asmdef.
- Unity's script compile with real UnityEngine lists: see "Unity compile
  times" below.

## Delegate bridges and LuaEnv start-up (M3, 2026-10-18)

Same setup as the M2 bridge speed table above (macOS 27 arm64, Lua 5.5.1 Release plugin
built from `2698d42`, Mono 6.14.1, .NET SDK 10.0.401 csc for both modes,
`--modes gencode-csc` and `--modes sourcegen`, `--no-lua --runs 5`, three
rounds alternating the modes), after the source generator started writing
delegate and interface bridges. Load average 12 to 22 from other agents, so
differences under about 10% are noise; one `sourcegen` round had outliers of
two to four times in single cases, which the lowest median hides.

| Case | legacy ns | source generator ns | change | B/op (managed + Lua), legacy / source | M2 change |
|---|---:|---:|---:|---|---:|
| `cs_lua.delegate_int` | 64.1 | 65.1 | +2% | 0+0 / 0+0 | +12% |
| `cs_lua.delegate_string_arg` | 120.5 | 121.4 | +1% | 0+0 / 0+0 | +2% |
| `cs_lua.delegate_struct_gcopt` | 154.7 | 287.0 | +86% | 0+52 / 32+36 | +79% |
| `cs_lua.delegate_struct_boxed` | 262.3 | 317.4 | +21% | 32+36 / 32+36 | +13% |
| `cs_lua.luafunction_call` | 303.9 | 308.0 | +1% | 264+0 / 264+0 | +3% |
| `cs_lua.luafunction_func` | 218.4 | 229.6 | +5% | 0+0 / 0+0 | -1% |
| `env.create_dispose` | 3,042,210 | 3,353,785 | +10% | 193,816 / 190,072 | +266% |
| `lua_cs.*` calls, fields, properties, strings (8 cases) | 48.0 to 90.7 | 48.1 to 91.8 | -1% to +2% | equal | -4% to +5% |
| `table.*` (4 cases) | 188 to 246 | 188 to 250 | -2% to +2% | 0 / 0 | -3% to +1% |

Reading:

- **`env.create_dispose` is back at the legacy figure** (3.0 to 3.6 ms in
  the legacy rounds, 3.4 to 3.7 ms in the two quiet `sourcegen` rounds;
  12.0 ms in M2) and allocates the same 190 KB per `LuaEnv` (759 KB in M2): the
  unit registers delegate bridges, so `DelegateBridge.Gen_Flag` is set and
  CodeEmit's `initCSharpCallLua` no longer scans every loaded type.
- **Delegate calls** go through generated bridges whose bodies are the
  legacy ones (`delegate_int` +2%, was +12% through CodeEmit).
- **Structs through delegates** are still slower: `[GCOptimize]` structs are
  boxed until the M4 pushers (`delegate_struct_gcopt`, 32 managed bytes per
  call), and `delegate_struct_boxed` runs the same generated code as legacy
  (compared line by line) but shows the same gap as `lua_cs.struct_boxed_*`
  (+5% to +13%), which involve no bridge; the M4 struct registrations are the
  likely difference and are measured there.

## GCOptimize structs (M4, 2026-10-18)

Same setup as the M3 table (plugin from `2698d42`; the native code has not
changed since), after the source generator started writing the GCOptimize
pushers, packing and array accessors. Three rounds alternating the modes,
load average 15 to 16; the lowest median of three per case.

| Case | legacy ns | source generator ns | change | B/op (managed + Lua), legacy / source | M3 change |
|---|---:|---:|---:|---|---:|
| `lua_cs.struct_gcopt_pass` | 152.7 | 156.7 | +3% | 0+52 / 0+52 | +99% |
| `lua_cs.struct_gcopt_new` | 150.4 | 149.3 | -1% | 0+52 / 0+52 | +77% |
| `lua_cs.struct_gcopt_field_get` | 114.6 | 113.6 | -1% | 0+0 / 0+0 | +52% |
| `lua_cs.struct_boxed_pass` | 315.1 | 309.2 | -2% | 32+36 / 32+36 | +5% |
| `lua_cs.struct_boxed_new` | 240.0 | 239.9 | 0% | 32+36 / 32+36 | +13% |
| `lua_cs.struct_boxed_field_get` | 176.8 | 176.9 | 0% | 0+0 / 0+0 | +1% |
| `cs_lua.delegate_struct_gcopt` | 153.8 | 156.0 | +1% | 0+52 / 0+52 | +86% |
| `cs_lua.delegate_struct_boxed` | 266.2 | 261.6 | -2% | 32+36 / 32+36 | +21% |
| `cs_lua.delegate_int` | 64.8 | 65.0 | 0% | 0+0 / 0+0 | +2% |
| `env.create_dispose` | 2,914,255 | 3,223,315 | +11% | 193,816 / 193,816 | +10% |
| other `lua_cs`, `cs_lua`, `table` cases (19) | | | -1% to +4% | equal | |

Reading:

- **`[GCOptimize]` structs are back at the legacy figures,** and allocate the
  same: no managed bytes, 52 Lua bytes for the struct userdata. They were
  boxed until now (32 managed bytes per call).
- **The boxed-struct gap of M3** (`delegate_struct_boxed` +21%,
  `struct_boxed_new` +13%) is gone as well, with no change to the code those
  cases run. The M3 executables, run again with only the boxed cases
  (`--filter struct_boxed`, two runs each), give equal times in both modes:
  `delegate_struct_boxed` 265 and 295 ns legacy, 268 and 287 ns source
  generator; `struct_boxed_new` 254 and 260 against 267 and 260. So the gap
  came from the boxed `struct_gcopt_*` cases that run before them in the
  same process (boxed values in the object pool and more Lua garbage), not
  from the boxed path itself **[inferred from the two runs]**.
- **`env.create_dispose`** stays at the legacy level within the noise of
  this machine (2.9 to 3.2 ms legacy, 3.2 to 4.1 ms source generator, the same
  bytes).

The per-value allocation is also asserted by the host suite:
`run_managed.py --source-gen` fails when 5,000 `TestStruct` values made and
read in Lua allocate 5,000 managed bytes or more (`SourceGenLauncher.cs`).
`Test/MobileSmoke/alloc_probe.py` measures `new LuaEnv()` and type access in
reflection mode, which M4 does not change; it was not run.

## Unity compile times: source generator against Generate Code (2026-10-18)

`Test/UnityMatrix/compile_time.py` (macOS 27 arm64, the C# of this branch
after merging main at `650e32d`, Lua 5.5.1 plugin, opt-in asmdef layout).
The type list is the "Lua-only scripting" reference configuration of
`Assets/XLua/Editor/ExampleConfig.cs`: the `UnityEngine` namespace with its
exclude list, without delegates, interfaces, enums, generic definitions,
obsolete and compiler-generated types, from the modules the project
enables (16 common built-in modules): 370 types in 2021.3.45f2, 417 in
6000.3.26f1, 423 in 6000.6.6f1. `--wide` adds every `UnityEngine.*`
namespace (950 types in 2021.3). Variants:

- **legacy:** the list as a `[LuaCallCSharp]` list, Generate Code, so
  `Assets/XLua/Gen` (47,641 lines for 100 types, 184,031 for 370 in 2021.3)
  compiles into Assembly-CSharp;
- **asmdef:** `[assembly: LuaCallCSharp(typeof(...))]` in an asmdef
  `XLuaBindings` that references XLua (decision 4);
- **Assembly-CSharp:** the same file in Assembly-CSharp.

Each figure is the median of three compiles after a one-line edit of a game
script in Assembly-CSharp ("game edit") or of the bindings file ("bindings
edit"), in an editor started in batch mode. **Editor** is the duration of
the assembly's Csc node in Unity's Bee trace (compiler and generators, no
domain reload); 2021.3 rounds it up to whole seconds, so its rows also give
**replay**, the same compiler command run again outside the editor with the
editor's compiler server (median of the last five of six runs). The machine
was shared with other agents (load average 5 to 20), and repeated 2021.3
runs differed by up to a factor of three in the replay of the large lists;
read the tables for their order of magnitude.

Unity 6000.3.26f1 and 6000.6.6f1, editor (ms):

| Types | Unity | baseline (no bindings) | legacy, game edit | asmdef, game edit | asmdef, bindings edit | Assembly-CSharp, game edit |
|---:|---|---:|---:|---:|---:|---:|
| 100 | 6000.3 | 558 / 747 | 1,333 | 802 | 1,611 | 1,653 |
| 417 | 6000.3 | 558 / 747 | 2,978 | 764 | 3,987 | 5,416 |
| 100 | 6000.6 | 94 / 109 | 441 | 102 | 395 | 397 |
| 422 | 6000.6 | 94 / 109 | 1,796 | 99 | 1,619 | 1,732 |

Unity 2021.3.45f2, editor (s, rounded up) and replay (ms):

| Types | baseline | legacy, game edit | asmdef, game edit | asmdef, bindings edit | Assembly-CSharp, game edit |
|---:|---|---|---|---|---|
| 100 | 1 s, 99 / 115 | 2 s, 429 | 1 s, 105 | 2 s, 464 | 2 s, 468 |
| 370 | 1 s, 99 / 115 | 3 s, 1,127 | 1 s, 111 | 4 s, 1,126 | 3 s, 1,153 |
| 950 (wide) | 1 s, 85 / 92 | 3 s, 1,558 | 1 s, 110 | 5 s, 2,101 | 5 s, 1,769 |

The baseline is the legacy project / the project with the generator
installed but nothing configured (the generator still runs in every
assembly that references XLua). Without the generator's `-analyzer`
argument the replayed Assembly-CSharp compile takes 79 to 92 ms in every
2021.3 variant, and the replayed `XLuaBindings` 76 to 83 ms (only the
attribute file).

Reading:

- **`XLuaBindings` asmdef:** a game edit costs what it costs without
  bindings (764 against 747 ms with 417 types in 6000.3, 99 ms in 6000.6,
  about 110 ms replayed in 2021.3): Assembly-CSharp only references the
  compiled bindings. The bindings cost their compile time only when the
  bindings file changes (4.0 s for 417 types in 6000.3).
- **Bindings in Assembly-CSharp** pay the generator and the wrappers on
  every game edit, as legacy generated code does: in 2021.3 and 6000.6 about
  the same as legacy (1,153 against 1,127 ms replayed for 370 types; 1,732
  against 1,796 ms for 422), in 6000.3 1.8 times legacy (5.4 against 3.0 s
  for 417 types).
- **Unity 6 seems to run the generator from scratch at every compile:**
  with the bindings in Assembly-CSharp a game edit costs as much as the
  first compile (5.4 against 5.3 s for 417 types in 6000.3, 1.7 against
  1.5 s for 422 in 6000.6), so the static model cache of section 7 does not
  seem to carry over there. 6000.3 (Roslyn 4.3.1) is about three times
  slower than 6000.6 (Roslyn 4.10) for the same list. In 2021.3 the replays
  fall from 2.5 s to 1.0 s over six runs against one compiler server. Not
  investigated further.
- **2021.3 starts a new compiler server (VBCSCompiler) per editor launch**;
  each idles for some minutes. Dozens were alive at once during these runs
  (25 to 300 MB each).
- **All 423 types did not compile in 6000.6 with either generator:** the
  `PhysicsScene2D` wrapper's call of `GetRayIntersection` was ambiguous
  (CS0121) between `(Ray, float, int)` and Unity 6.6's new
  `(Ray, float, int, Allocator)`; the 6000.6 row leaves `PhysicsScene2D`
  out (`--exclude`). Fixed on 2026-10-18: both generators now write the
  left-out defaults of such calls out (`Generator.cs`
  `explicitDefaultArguments`, `Wrappers.cs` `ExplicitDefaults`). A
  `compile_time.py --counts all --edits 1` run on 6000.6.6f1 with all 423
  types, `PhysicsScene2D` included, compiled with no error in every variant
  (legacy Generate Code with 215,733 generated lines, the `XLuaBindings`
  asmdef, Assembly-CSharp). Its single-edit figures (ms): baseline 94 / 137,
  legacy game edit 1,731, asmdef game edit 124 and bindings edit 1,577,
  Assembly-CSharp game edit 1,483, in line with the 422-type row above.


## IL2CPP player size: source generator, Generate Code and GCM (M6, 2026-10-18)

Release ARM64 APKs from Unity 6000.3.21f1 with one configuration (five
wrapped types, two delegates; `size_report.py --configs sg-minimal,sg-high`),
xLua from the UPM package. The full table is in
[SIZE.md](SIZE.md#generated-bindings-generate-code-gcm-and-the-source-generator).
Deltas against Generate Code in the same layout:

| bindings | APK, Minimal | APK, High | `libil2cpp.so` uncompressed, Minimal / High |
| --- | ---: | ---: | ---: |
| Generate Code | 0 | 0 | 0 / 0 |
| `GEN_CODE_MINIMIZE` | -31,348 | -37,080 | -115,088 / -115,472 |
| source generator | -20,180 | -20,992 | -52,320 / -51,520 |

The source generator lands between the two legacy modes: GCM's APK is 11 KB
(Minimal) to 16 KB (High) smaller than the source generator's for this
configuration. This is the IL2CPP number the GCM question of
[M10](../docs/design/source-generator.md#m10-as-implemented-2026-10-18) asked
for; the host IL proxy had the source generator 14% above GCM.
