## What & Why

The original upstream tutorial lists these built-in extension libraries:

> This list describes the upstream package at the time of writing. Availability in this fork depends on the native sources and build configuration; it does not guarantee that all of these extensions are shipped.

* LuaJIT support for 64-bit integers;
* Positioning tool for function call times and memory leaks;
* LuaSocket library for supporting ZeroBraneStudio;
* tdr 4 lua (removed in xlua-ng: it needed a native module that was never published; see [ECOSYSTEM.md](../../../docs/ECOSYSTEM.md#data-and-serialization) for alternatives);

With the increasing extensiveness and intensiveness of project use, the current extension libraries have been unable to meet the project team needs. Since various projects require very different extension libraries, and since mobile phone platforms are sensitive to the size of the installation package, xLua is unable to meet these needs through pre-integration. That is why we are offering this tutorial.

In this tutorial, we will use lua-rapidjson as an example to explain step by step how to add C/C++ extensions to xLua. Once you know how to add them, you will also know how to delete them naturally. The project team can delete those pre-integrated extensions if they are not used any more.

## How it is done

There are three steps:

1. Modify the build file and project settings. Compile the extensions you want to integrate into the XLua Plugin directory.
2. Call the C# APIs on xLua so that the extensions can be loaded as needed (when required in the Lua code).
3. (Optional) If you need to use 64-bit integers in your extensions, you can use xLua's 64-bit extension library to work with C#.

### First, add extensions & compile.

Preparations

1. Extract the xLua’s C source code package to the same level directory as the Assets of your Unity project.

   Download the lua-rapidjson code and place it anywhere you like. In this tutorial, we place the rapidjson header file in the $UnityProj\build\lua-rapidjson\include directory, and place the extended source code rapidjson.cpp in the $UnityProj\build\lua-rapidjson\source directory (Note: $UnityProj refers to your project directory).

2. Add extensions to CMakeLists.txt

   xLua’s platform Plugins are compiled using CMake. The advantage of this is that the compilations of all platforms are written in a makefile, and most compilation processing logic is cross-platform.

   XLua's CMakeLists.txt provides extension points (all lists) for third-party extensions:
   1. THIRDPART_INC: Third-party extension header search path.
   2. THIRDPART_SRC: Third-party extended source code.
   3. THIRDPART_LIB: The library on which third-party extensions rely.

   The following is added with RapidJSON:

   ```cmake
   #begin lua-rapidjson
   set (RAPIDJSON_SRC lua-rapidjson/source/rapidjson.cpp)
   set_property(
   SOURCE ${RAPIDJSON_SRC}
   APPEND
   PROPERTY COMPILE_DEFINITIONS
   LUA_LIB
   )
   list(APPEND THIRDPART_INC  lua-rapidjson/include)
   set (THIRDPART_SRC ${THIRDPART_SRC} ${RAPIDJSON_SRC})
   #end lua-rapidjson
   ```

   The upstream article refers to a complete-code attachment. This Markdown file does not include that attachment; the CMake fragment above shows the required integration points.

3. Compile platforms

   All compiled scripts are named with this format: make_platform_lua version.extension name.

   For example, the name of the Windows 64-bit Lua 5.3 version is make_win64_lua53.bat, and the name of the Android LuaJIT version is make_android_luajit.sh. you can execute the corresponding script to compile the target version.

   The compiled native libraries are automatically copied to the plugin_lua53 or plugin_luajit directory. The former is for Lua 5.3 and the latter for LuaJIT.

   The Android scripts (`make_android_lua53.sh`, `make_android_lua54.sh`, `make_android_lua55.sh`, `make_android_luajit.sh`) need an installed Android NDK r30 (the current LTS): set `ANDROID_NDK` to its directory (`ANDROID_NDK_HOME` and `ANDROID_NDK_ROOT` also work). Each builds armeabi-v7a, arm64-v8a, x86 and x86_64 at API 23, the lowest level xlua-ng supports; `ANDROID_ABIS`, `ANDROID_API_LEVEL` and `XLUA_BUILD_ROOT` override the defaults. Building LuaJIT for the 32-bit ABIs also needs a 32-bit host compiler (`gcc-multilib`). Lua 5.4 and 5.5 builds use their corresponding plugin_lua54 and plugin_lua55 output directories.

### Second, C# side integration:

Each C extension library on Lua will provide a function, luaopen_xxx, where xxx is the name of the dynamic library. For example, the function for the Lua-RapidJSON library is luaopen_rapidjson. Such functions are automatically called by the Lua virtual machine when loading the dynamic library. In the mobile platform, we cannot load the dynamic library due to iOS restrictions. They are compiled directly into the process instead.

For this purpose, xLua provides an API to replace this feature (LuaEnv's member methods):

```csharp
public void AddBuildin(string name, LuaCSFunction initer)
```

Parameters:

- `Name`: name of the buildin module, a parameter entered during require.
- `initer`: the initialization function. Its prototype is `public delegate int lua_CSFunction(IntPtr L)`. This must be a static function and be modified with the property `MonoPInvokeCallbackAttribute`. This API will check these two conditions.

We use calling luaopen_rapidjson to show how to use it.

Extend the LuaDLL.Lua type, export luaopen_rapidjson to C# via pinvoke, and then write a static function that satisfies the definition of lua_CSFunction. You can write initialization work in it, such as calling luaopen_rapidjson. Here is the complete code:

```csharp
namespace XLua.LuaDLL
{
public partial class Lua
{
[DllImport(LUADLL, CallingConvention = CallingConvention.Cdecl)]
public static extern int luaopen_rapidjson(System.IntPtr L);

[MonoPInvokeCallback(typeof(LuaDLL.lua_CSFunction))]
public static int LoadRapidJson(System.IntPtr L)
{
return luaopen_rapidjson(L);
}
}
}
```

Then call AddBuildin:

```csharp
luaenv.AddBuildin("rapidjson", XLua.LuaDLL.Lua.LoadRapidJson);
```

After this, it should work properly. Try the extension in the Lua code:

```lua
local rapidjson = require('rapidjson')
local t = rapidjson.decode('{"a":123}')
print(t.a)
t.a = 456
local s = rapidjson.encode(t)
print('json', s)
```

### Third, 64-bit transformation

Include the i64lib.h file in a file that requires a 64-bit transformation.
The header file include these APIs:

```c
// Push an int64/uint64 onto the stack
void lua_pushint64(lua_State* L, int64_t n);
void lua_pushuint64(lua_State* L, uint64_t n);
// Test whether the value at stack position pos is an int64/uint64
int lua_isint64(lua_State* L, int pos);
int lua_isuint64(lua_State* L, int pos);
// Read an int64/uint64 from stack position pos
int64_t lua_toint64(lua_State* L, int pos);
uint64_t lua_touint64(lua_State* L, int pos);
```

Use these APIs where the extension exchanges 64-bit values with Lua. The upstream article refers to an attached rapidjson.cpp example; that attachment is not included in this Markdown file.

### Removing an extension

Reverse the integration steps: remove its CMake include paths, sources, and libraries; remove its C# initializer registration and P/Invoke declarations; then rebuild every native plugin used by the project. Remove Lua require calls that depend on the extension.

The upstream source ends with a caption for build-project modifications, but includes no additional code or attachment here.

