# Source generator measurements

Measurements for [docs/design/source-generator.md](../docs/design/source-generator.md),
section 7 and milestones M2 and M3. The spike that came before is in
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
- **Not measured yet:** Unity's script compile in 2021.3 and 6000.3 with real
  UnityEngine lists (needs a Unity run, design "M2 as implemented").

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
