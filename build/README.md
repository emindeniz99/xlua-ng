# Native plugin compatibility

The managed sources require native bridge version **106**. The repository
does not ship prebuilt plugins: the upstream binaries were version 105 and
failed the version check, so they were removed. Build the plugin from this
directory, or download the `xlua-lua-<version>-<platform>` artifact from the
`native-build.yml` workflow, and copy it into the matching `Assets/Plugins`
directory. The committed `.meta` files there carry the Unity import settings.

Version 106 adds `xlua_gc`, a fixed-signature wrapper around `lua_gc`.
Lua 5.4 declares `lua_gc` as variadic. Managed callers must use the wrapper
so GC parameters follow the native calling convention on Apple ARM64.
The same wrapper supports Lua 5.3 and LuaJIT.

## macOS ARM64 source builds

Keep output directories outside the checkout, for example on an external SSD:

```sh
cmake -S build -B /path/to/builds/lua54 -DBUILD_SILICON=ON -DLUA_VERSION=5.4.9 -DCMAKE_BUILD_TYPE=Release
cmake --build /path/to/builds/lua54 --parallel 4
```

Select `-DLUA_VERSION=5.3.6` and a separate output directory to build Lua 5.3.
The default CMake build selects Lua 5.5.1. Select `-DLUA_VERSION=5.4.9`
for Lua 5.4. Scripts named `lua53` and the
legacy UWP scripts explicitly select 5.3.6. Use a fresh output directory
when changing runtime versions. Bytecode and scripts that rely on
a particular Lua version require an explicit runtime selection.
`LUAC_COMPATIBLE_FORMAT` is a Lua 5.3-only extension; CMake rejects it for
Lua 5.4 or 5.5 instead of silently ignoring the option.

## Unity WebGL

WebGL has no prebuilt plugin and no CMake step. Unity compiles
`Assets/Plugins/WebGL/xlua_webgl.cpp` with Emscripten, and that one file
includes the sources of the default runtime: the Lua 5.5.1 core and standard
libraries from `build/lua-5.5.1/src`, plus `xlua.c`, `i64lib.c` and
`memory_leak_checker.c` from this directory. This is the same list as
`build/CMakeLists.txt`, without LuaSocket (managed code excludes it on WebGL).
There is no separate WebGL copy of Lua or of the bridge, so bridge version 106
and every new export come with the other platforms. WebGL supports Lua 5.5.1
only; Lua 5.3 and 5.4 are not available there.

- Unity gives plugin sources no include paths. The wrapper therefore includes
  every file by a path relative to itself and defines `XLUA_AMALGAMATION`, which
  tells the bridge sources that the Lua headers are already included.
  `build/lua-5.5.1/src/luaconf.h` is a plain header with `LUA_IDSIZE` 120 as
  the default; CMake passes `-DLUA_IDSIZE` instead of generating the header
  from a template (Lua 5.3 and 5.4 still use `luaconf.h.in`).
- The sources are C and the wrapper is C++, so they are included inside
  `extern "C"` and keep their C symbol names, which `DllImport("__Internal")`
  needs. `LUA_USE_LONGJMP` keeps Lua errors on `longjmp` as on other platforms.
  C++ exceptions would require "Enable Exceptions" in the player settings.
- Check the result without Unity: compile the wrapper with the Emscripten
  version Unity uses (2.0.19 for 2021.3, 3.1.8 for 2022.3, 3.1.38 for 2023.2
  up to 6000.4, 4.0.19 from 6000.5; all four compile it without warnings),
  run `llvm-nm --defined-only` on the object file and pass the output to
  `python3 Test/Native/check_webgl_exports.py nm.txt`. It checks every function
  that `LuaDLL.cs` imports when `UNITY_WEBGL` is defined. See `LUA55.md` for the
  last validation.

## Regression coverage

The unit suite includes `testGcPauseRoundTrip` and `testGcStepmulRoundTrip`.
They set GC parameters through C#, read them back through Lua, and check that
C# getters preserve the settings. Follow `Test/README.md` for the Unity scene
runner, or the standalone project list in `General/vs2013/XLuaUnitTest.csproj`.

## iOS device library

Run `build/make_ios_lua54.sh` with `XLUA_BUILD_ROOT` pointing to an external
build directory. The script defaults to iOS 15.0; override
`IOS_DEPLOYMENT_TARGET` when your Xcode SDK and consuming Unity player support
another minimum. Bitcode is disabled. CMake preserves the toolchain's
deployment target instead of forcing iOS 7.0.

Validation: the Lua 5.4.9 ARM64 device archive builds with Xcode 27.1 and the
iOS 27.1 SDK, targeting iOS 15.0. This confirms native compilation, not
Unity/IL2CPP linking or device execution.

## Android libraries

Every Android runtime builds with one NDK: **r30 (30.0.16248370), the current
LTS**, using the NDK's own clang and `build/cmake/android.toolchain.cmake`.
Set `ANDROID_NDK` (or `ANDROID_NDK_HOME` / `ANDROID_NDK_ROOT`) to the NDK
directory and `XLUA_BUILD_ROOT` to an external output directory, then run one
of:

- `build/make_android_lua53.sh`, `make_android_lua54.sh`, `make_android_lua55.sh`
- `build/make_android_luajit.sh`

Each script builds armeabi-v7a, arm64-v8a, x86, and x86_64 at API 21 (the
lowest level NDK r30 supports) and copies `libxlua.so` to
`plugin_<runtime>/Plugins/Android/libs/<abi>/`. Override `ANDROID_ABIS` with a
space-separated list, `ANDROID_API_LEVEL` for another minimum, or `XLUA_JOBS`
for the parallelism. A failed configure or build stops the script before it
copies that ABI's plugin. The shared logic is in `build/android_common.sh`.

LuaJIT follows the Android instructions in `luajit-2.1/doc/install.html`: the
NDK clang builds `libluajit.a` (`BUILDMODE=static`) from a per-ABI copy of the
source tree, and CMake links it with `-DUSING_LUAJIT=ON`. The 32-bit ABIs need
a 32-bit host compiler (`gcc-multilib` on Debian/Ubuntu); the NDK is Linux or
macOS only. The removed `make_android_lua53_arm64.sh` and
`make_android_luajit_arm64.sh` are covered by the scripts above.

Check the result with `python3 Test/Native/check_android_elf.py
<libxlua.so files>`: it uses the NDK's `llvm-readelf` to verify the machine
for the ABI, that every ELF LOAD segment is aligned to at least 16 KB, and that
the dynamic symbol table defines every function `LuaDLL.cs` imports.

Validation (2026-10-15, NDK r30 in a linux/amd64 container): Lua 5.3.6, 5.4.9,
5.5.1 and LuaJIT build for all four ABIs, and every library passes the check
above (108 managed imports, 16 KB alignment). Android execution and Unity
player integration remain unverified. The iOS device archive also links into
a native ARM64 smoke executable; that executable has not run on a device.

## Lua 5.3.6 upgrade validation

The Lua 5.3 vendor tree comes from the official 5.3.6 archive, SHA256
`fc5fd69bb8736323f026672b1b7235da613d7177e72558893a0bdcd320466d60`.
The commit sequence restores unmodified 5.3.5, upgrades to unmodified 5.3.6,
then reapplies xLua integration separately.

Retained patches cover UWP restrictions, Android decimal parsing,
`os.execute` removal, configurable `LUA_IDSIZE`, the existing `log2`
fallback, and optional `LUAC_COMPATIBLE_FORMAT` bytecode. The upstream
5.3.6 `io.popen` mode validation and bytecode loader GC fixes are preserved.

Validation on 2026-10-07:

- macOS ARM64 native plugin and both unit-suite modes: 275 tests per mode.
- Unity 6000.3.21f1 Editor: Lua 5.3 loading, managed GC settings,
  coroutine, full GC, and disposal smoke checks passed.
- iOS ARM64 native archive with Xcode 27.1, deployment target 15.0.
- Android NDK 27.1, API 21 (since rebuilt with r30): armeabi-v7a, arm64-v8a, x86, and x86_64;
  bridge exports and at least 16 KB ELF LOAD alignment verified.
- Standard and custom-format `lua`/`luac`: source and bytecode execution of
  `Test/Native/lua53_regression.lua`, including GC during incremental
  loading of long strings and nested functions. Cross-format loads reject
  incompatible bytecode as expected.

For the Lua/luac tools, configure `build/luac` in separate external output
folders. `-DLUA_VERSION` selects 5.3.6, 5.4.9 or 5.5.1 (default 5.5.1);
`-DLUAC_COMPATIBLE_FORMAT=ON` is accepted only with 5.3.6.
Run each resulting `lua` executable on the regression file, then use its
matching `luac -o` to compile the file and execute the generated bytecode.
These host tests do not establish compatibility between 32-bit and 64-bit
runtimes; that remains a separate device/runtime check.

On 2026-10-08, Lua 5.3.6 also passed the Unity 6000.3.21f1 ARM64 IL2CPP smoke checks on
iPhone 14 Pro: GC setters/getters, coroutine, full GC, and disposal.
Android and UWP player execution remain unverified.

See [the vendor patch audit](LUA_PATCHES.md) for the historical evidence,
retained behavior, and outstanding platform checks.

See [Lua 5.5 integration](LUA55.md) for runtime changes and validation.
