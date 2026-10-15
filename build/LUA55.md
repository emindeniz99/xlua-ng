# Lua 5.5.1 integration

Lua 5.5.1 is the default native runtime. The official source archive SHA256
is `1c4b4068d67061f2a2231ad2b5422e77acea1487ea9890f6320af614f4373dce`.
Unmodified vendor import, platform patches, bridge integration, fixes,
build entry points, and tests are separate commits.

Use `-DLUA_VERSION=5.4.9` or `-DLUA_VERSION=5.3.6` to select older runtimes.
Each runtime needs its own output directory and matching native plugins.
Scripts named `lua53`, `lua54`, and `lua55` select explicit versions.
The repository ships no prebuilt plugin binaries; build them per runtime.

## Managed compatibility

The C# API and native bridge requirement remain version 106. Native
adaptation handles the following differences without changing signatures:

- `xlua_gc` maps the existing managed pause and multiplier operations to
  `LUA_GCPARAM`. Legacy running-state opcode 9 maps to Lua 5.5's opcode 6.
  GC step keeps managed KB units, converting to bytes with a `size_t`
  variadic argument. Reserved legacy opcode 8 stays invalid.
- GC parameters follow the selected runtime's compression rules. In Lua
  5.5, pause request 224 reads back as 212 and multiplier 240 as 237.
  Getters preserve the runtime-normalized values. They do not maintain a
  separate cache that could disagree with Lua.
- `luaL_openlibs` became a macro in Lua 5.5. An exported compatibility
  function calls `luaL_openselectedlibs` so existing IL2CPP imports link.
- `luaL_newstate` remains the initialization path and supplies the new
  hash seed internally; xLua does not directly call the changed
  three-argument `lua_newstate` API.
- The memory profiler uses Lua 5.5 array access macros and updated table
  APIs. A separate regression fix corrects pre-existing Lua 5.4 hash-key
  tag reads through `getnodekey`.
- xLua's embedded hotfix initializer shadows its loop key before changing
  it, because Lua 5.5 loop variables are read-only.

## Unity WebGL

The Unity WebGL plugin (`Assets/Plugins/WebGL/xlua_webgl.cpp`) compiles the
Lua 5.5.1 sources in this directory, so it no longer has a Lua 5.3.4 copy. See
the WebGL section of [the native plugin notes](README.md).

## Lua application migration

C# API compatibility does not make different Lua languages or bytecode
formats interchangeable. Lua 5.5 reserves `global`, makes loop variables
read-only, changes GC options, and uses more precise default number
formatting. Application scripts may need changes. Refer to the
[official incompatibilities](https://www.lua.org/manual/5.5/manual.html#8).
The tests compare numeric values rather than old `tostring` rounding.

Lua 5.5 supports its own standard bytecode. `LUAC_COMPATIBLE_FORMAT` is
xLua's optional Lua 5.3 format extension, not a general bytecode switch.
CMake rejects it for 5.4/5.5. The separate `build/luac` project builds standard `lua`/`luac` for any bundled runtime and the custom format only for 5.3.6.

## Validation on 2026-10-08

- Lua 5.3.6, 5.4.9, and 5.5.1: both host suite modes pass 275 tests each.
- All three host runtimes: 108 managed native imports resolve; profiler
  traversal and GC stop/step/restart/running-state regression checks pass.
- Lua 5.5.1: macOS ARM64, iOS ARM64, and Android armeabi-v7a, arm64-v8a,
  x86, and x86_64 native builds pass. Android exports and 16 KB ELF LOAD
  alignment are checked.
- Unity 6000.3.21f1 IL2CPP: Lua 5.5.1 passes smoke checks on iPhone 14 Pro
  and a physical Android ARM64 device. Checks include Lua version,
  restricted `os.execute`, normalized managed GC settings, GC lifecycle,
  Lua to-be-closed values, coroutine results, and LuaEnv disposal.
- Unity 6000.3.21f1 macOS ARM64 Editor: Lua 5.5.1 smoke checks pass.
- Lua 5.4.9 also passes the physical Android smoke test.
- Unity WebGL source (`xlua_webgl.cpp`), 2026-10-15, in Docker without Unity:
  it compiles without `-Wall` warnings under Emscripten 3.1.38 (the version Unity
  6000.3 uses) and 4.0.19, with default flags, `-fno-exceptions` and Unity's
  WebAssembly 2023 flags. All 107 functions that `LuaDLL.cs` imports on WebGL
  are defined, `xlua_get_lib_version` returns 106, and a smoke program run
  under Node covers `luaL_newstate`, `luaL_dostring`, error handling,
  `LUA_IDSIZE`, coroutines, int64 and `xlua_gc`. A Unity WebGL player build and
  a browser run have not been done. The CMake builds of 5.3.6, 5.4.9 and 5.5.1
  still export all 108 imports on Linux x86-64.

Run `python3 Test/Native/check_exports.py /path/to/libxlua.dylib` and
`python3 Test/Native/profiler_smoke.py /path/to/libxlua.dylib` for the
host-native checks. Follow `Test/MobileSmoke/README.md` for isolated Unity
projects. Keep builds and generated projects outside the checkout.

Windows, UWP, Linux, OHOS, other Unity versions, and legacy iOS runtime
support have not been established by these checks. Platform entry points
are not substitutes for builds and runtime testing on those targets.
