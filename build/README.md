# Native plugin compatibility

The managed sources require native bridge version **106**. Rebuild native
plugins before using these sources. The checked-in prebuilt plugins still
use the upstream bridge and will fail the version check; they are not
updated by a source build. Copy the rebuilt plugin into the matching Unity
plugin directory when testing a Unity project.

Version 106 adds `xlua_gc`, a fixed-signature wrapper around `lua_gc`.
Lua 5.4 declares `lua_gc` as variadic. Managed callers must use the wrapper
so GC parameters follow the native calling convention on Apple ARM64.
The same wrapper supports Lua 5.3 and LuaJIT. The WebGL bridge source also
exports it; its bundled Lua source remains on the existing version.

## macOS ARM64 source builds

Keep output directories outside the checkout, for example on an external SSD:

```sh
cmake -S build -B /path/to/builds/lua54 -DBUILD_SILICON=ON -DLUA_VERSION=5.4.9 -DCMAKE_BUILD_TYPE=Release
cmake --build /path/to/builds/lua54 --parallel 4
```

Select `-DLUA_VERSION=5.3.5` and a separate output directory to build Lua 5.3.
The Lua 5.4 platform scripts select 5.4.9; the default CMake build remains 5.3.5.

## Regression coverage

The unit suite includes `testGcPauseRoundTrip` and `testGcStepmulRoundTrip`.
They set GC parameters through C#, read them back through Lua, and check that
C# getters preserve the settings. Follow `Test/README.md` for the Unity scene
runner, or the standalone project list in `General/vs2013/XLuaUnitTest.csproj`.
