# Compatible Bytecode Format

This page translates the historical upstream guide, with its build instructions scoped to the currently retained Lua 5.3.6 implementation.

Some projects compile Lua source with `luac` before loading it. Standard Lua bytecode depends on the word size: a 32-bit Lua runtime can run bytecode compiled by a 32-bit `luac`, while a 64-bit runtime needs bytecode compiled by a 64-bit `luac`.

xLua includes an optional modification to Lua 5.3.6 that allows one custom-format bytecode build to be used across 32-bit and 64-bit runtimes. This feature is specific to Lua 5.3.6. It does not apply to the Lua 5.4.9 or 5.5.1 runtimes, and it does not promise compatibility across Lua versions, byte orders, or every architecture. The default CMake runtime is Lua 5.5.1, so select Lua 5.3.6 explicitly when enabling the option. CMake rejects `LUAC_COMPATIBLE_FORMAT` for Lua versions other than 5.3.

## Important notes

- When this modification is enabled, xLua cannot load bytecode produced by the unmodified official `luac` format. The modified runtime and the matching custom-format compiler must be used together.
- The original guide reported that the change had run successfully in one project for more than a month as of 2018-09-14. That report was not a guarantee for every project or use case.

## Build and use

### 1. Build the xLua plug-in with the custom format

Add `-DLUAC_COMPATIBLE_FORMAT=ON` to the CMake command in the platform build script. For example, a Windows 64-bit build using Visual Studio 2015 can be configured as follows:

```bat
mkdir build64 & pushd build64
cmake -DLUA_VERSION=5.3.6 -DLUAC_COMPATIBLE_FORMAT=ON -G "Visual Studio 14 2015 Win64" ..
popd
cmake --build build64 --config Release
md plugin_lua53\Plugins\x86_64
copy /Y build64\Release\xlua.dll plugin_lua53\Plugins\x86_64\xlua.dll
pause
```

Rebuild the xLua library for each target platform with the modified build script, then replace the corresponding files in the `Plugins` directory.

### 2. Build the matching `luac`

Build the custom-format `luac` from [`build/luac`](../../../build/luac/). Run `make_win64.bat` for Windows, or `make_unix.sh` for macOS or Linux. From this point on, use this specific compiler only with plug-ins built in step 1.

### 3. Load the bytecode

Load the bytecode through `CustomLoader`; see the tutorial for details. Read the file as binary data. Loading it through a text encoding can corrupt the Lua bytecode format.

## Changing opcodes

If a project needs a dedicated bytecode format, modify the Lua source (currently Lua 5.3.6) and repeat steps 1 and 2.
