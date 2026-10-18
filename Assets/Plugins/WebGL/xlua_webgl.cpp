/*
 * Unity WebGL (IL2CPP / Emscripten) build of the xLua native bridge.
 *
 * This file compiles the same sources as every other platform (build/CMakeLists.txt),
 * as a single translation unit:
 *   - Lua 5.5.1 core and standard libraries (build/lua-5.5.1/src, all files except lua.c and luac.c)
 *   - xLua's core layer and bridge: build/xlua_core.c, build/xlua_core_limits.c, build/xlua.c,
 *     build/i64lib.c,
 *     build/memory_leak_checker.c
 * luasocket is not built. XLUA_LUASOCKET is not defined, so xlua.c registers nothing in
 * package.preload and require "socket" fails with "module not found"; C# imports nothing for it.
 * Unity binds the DllImport("__Internal") functions to the symbols defined here.
 *
 * WebGL always uses Lua 5.5.1, the default runtime. There is no version switch:
 * Lua 5.3 and 5.4 have a different file list and a CMake-generated luaconf.h.
 * The former WebGLPlugins/ directory (a stale Lua 5.3.4 copy) was removed. See build/README.md.
 *
 * Details:
 *   - Unity gives plugin sources no include paths, so every #include below is
 *     relative to this file. XLUA_AMALGAMATION tells the xLua sources that the
 *     Lua headers are already included, so they do not search for them.
 *   - LUA_IDSIZE is 120 as in CMake builds; build/lua-5.5.1/src/luaconf.h
 *     defaults to it, so no define is needed here.
 *   - This is a C++ file, the Lua sources are C. They are wrapped in extern "C",
 *     so the symbols keep their C names (the DllImport entry points).
 *   - LUA_USE_LONGJMP makes Lua report errors with longjmp as it does on other
 *     platforms, instead of C++ exceptions. C++ exceptions need Player Settings
 *     "Enable Exceptions" to be on; longjmp does not.
 *   - Do not add LUA_USE_POSIX or LUA_USE_DLOPEN: as in the CMake build for
 *     Android, io.popen and dynamic library loading stay disabled.
 */

#define XLUA_AMALGAMATION
#define LUA_USE_LONGJMP
/* libxlua's Lua hooks (build/xlua_luahooks.h, through luaconf.h): the C budget of
   the string and table functions, as in the CMake builds. */
#define XLUA_LUA_HOOKS

#include "../../../build/lua-5.5.1/src/lprefix.h"

/* C library headers first, with C++ linkage rules, so extern "C" below does not wrap them. */
#include <assert.h>
#include <ctype.h>
#include <errno.h>
#include <float.h>
#include <inttypes.h>
#include <limits.h>
#include <locale.h>
#include <math.h>
#include <setjmp.h>
#include <signal.h>
#include <stdarg.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
/* xlua_core_limits.c includes <emscripten.h> for its clock. In C++ that header
   declares templates (em_asm.h), which cannot be inside extern "C", so it is
   included here first and its include guard turns the later #include into
   nothing (Test/Native/webgl_compile_check.py). */
#if defined(__EMSCRIPTEN__)
#include <emscripten.h>
#endif

extern "C" {

/* Headers the xLua sources use (what they would find through -I in a CMake build). */
#include "../../../build/lua-5.5.1/src/lua.h"
#include "../../../build/lua-5.5.1/src/lualib.h"
#include "../../../build/lua-5.5.1/src/lauxlib.h"
#include "../../../build/lua-5.5.1/src/ltable.h"
#include "../../../build/lua-5.5.1/src/lstate.h"
#include "../../../build/lua-5.5.1/src/lobject.h"
#include "../../../build/lua-5.5.1/src/lapi.h"
#include "../../../build/lua-5.5.1/src/lgc.h"
#include "../../../build/lua-5.5.1/src/ldo.h"
#include "../../../build/lua-5.5.1/src/lstring.h"

/* Lua core */
#include "../../../build/lua-5.5.1/src/lapi.c"
#include "../../../build/lua-5.5.1/src/lcode.c"
#include "../../../build/lua-5.5.1/src/lctype.c"
#include "../../../build/lua-5.5.1/src/ldebug.c"
#include "../../../build/lua-5.5.1/src/ldo.c"
#include "../../../build/lua-5.5.1/src/ldump.c"
#include "../../../build/lua-5.5.1/src/lfunc.c"
#include "../../../build/lua-5.5.1/src/lgc.c"
#include "../../../build/lua-5.5.1/src/llex.c"
#include "../../../build/lua-5.5.1/src/lmem.c"
#include "../../../build/lua-5.5.1/src/lobject.c"
#include "../../../build/lua-5.5.1/src/lopcodes.c"
#include "../../../build/lua-5.5.1/src/lparser.c"
#include "../../../build/lua-5.5.1/src/lstate.c"
#include "../../../build/lua-5.5.1/src/lstring.c"
#include "../../../build/lua-5.5.1/src/ltable.c"
#include "../../../build/lua-5.5.1/src/ltm.c"
#include "../../../build/lua-5.5.1/src/lundump.c"
#include "../../../build/lua-5.5.1/src/lvm.c"
#include "../../../build/lua-5.5.1/src/lzio.c"

/* Lua standard libraries */
#include "../../../build/lua-5.5.1/src/lauxlib.c"
#include "../../../build/lua-5.5.1/src/lbaselib.c"
#include "../../../build/lua-5.5.1/src/lcorolib.c"
#include "../../../build/lua-5.5.1/src/ldblib.c"
#include "../../../build/lua-5.5.1/src/linit.c"
#include "../../../build/lua-5.5.1/src/liolib.c"
#include "../../../build/lua-5.5.1/src/lmathlib.c"
#include "../../../build/lua-5.5.1/src/loadlib.c"
#include "../../../build/lua-5.5.1/src/loslib.c"
#include "../../../build/lua-5.5.1/src/lstrlib.c"
#include "../../../build/lua-5.5.1/src/ltablib.c"
#include "../../../build/lua-5.5.1/src/lutf8lib.c"

/* xLua's own files, same list as XLUA_SOURCES in build/CMakeLists.txt (legacy preset):
   the core layer, then the bridge built on it */
#include "../../../build/xlua_core.c"
#include "../../../build/xlua_core_limits.c"
#include "../../../build/i64lib.c"
#include "../../../build/xlua.c"
#include "../../../build/memory_leak_checker.c"

} /* extern "C" */
