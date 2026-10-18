/*
 * Lua standard library selection (CMake option XLUA_LIBS).
 *
 * build/CMakeLists.txt compiles this file in place of Lua's linit.c (LuaJIT:
 * lib_init.c) when XLUA_LIBS names fewer than all libraries. linit.c lists
 * every library in a static table, so linking it pulls them all in; this file
 * references only the libraries the build contains, which CMake defines as
 * XLUA_LIB_<NAME>. It defines the function the bridge calls to open them:
 *
 *  - Lua 5.3 and 5.4, LuaJIT: luaL_openlibs, which opens the base library and
 *    the selected ones in linit.c's (lib_init.c's) order;
 *  - Lua 5.5: luaL_openselectedlibs, whose masks keep the LUA_*LIBK bits of
 *    lualib.h; bits of libraries the build does not contain are ignored.
 *    xlua.c's luaL_openlibs symbol calls it with every bit set.
 *
 * With every library selected CMake builds linit.c as before and not this file.
 *
 * The tables and loops follow linit.c of Lua 5.3, 5.4 and 5.5 (Copyright (C)
 * 1994-2026 Lua.org, PUC-Rio) and lib_init.c of LuaJIT 2.1 (Copyright (C)
 * 2005-2026 Mike Pall), both under the MIT licence; see the notice at the end
 * of each version's src/lua.h and build/luajit-2.1/COPYRIGHT.
 */

#define LUA_LIB

#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#if defined(USING_LUAJIT)
#include "lj_arch.h" /* LJ_HASFFI */
#endif

#if defined(USING_LUAJIT)

static const luaL_Reg xlua_loaded_libs[] = {
	{"", luaopen_base},
#ifdef XLUA_LIB_PACKAGE
	{LUA_LOADLIBNAME, luaopen_package},
#endif
#ifdef XLUA_LIB_TABLE
	{LUA_TABLIBNAME, luaopen_table},
#endif
#ifdef XLUA_LIB_IO
	{LUA_IOLIBNAME, luaopen_io},
#endif
#ifdef XLUA_LIB_OS
	{LUA_OSLIBNAME, luaopen_os},
#endif
#ifdef XLUA_LIB_STRING
	{LUA_STRLIBNAME, luaopen_string},
#endif
#ifdef XLUA_LIB_MATH
	{LUA_MATHLIBNAME, luaopen_math},
#endif
#ifdef XLUA_LIB_DEBUG
	{LUA_DBLIBNAME, luaopen_debug},
#endif
#ifdef XLUA_LIB_BIT
	{LUA_BITLIBNAME, luaopen_bit},
#endif
#ifdef XLUA_LIB_JIT
	{LUA_JITLIBNAME, luaopen_jit},
#endif
	{NULL, NULL}
};

/* lib_init.c's luaL_openlibs with the selected libraries. ffi goes into
 * package.preload, as LuaJIT does, so it is loaded by require "ffi". */
LUALIB_API void luaL_openlibs(lua_State *L) {
	const luaL_Reg *lib;
	for (lib = xlua_loaded_libs; lib->func; lib++) {
		lua_pushcfunction(L, lib->func);
		lua_pushstring(L, lib->name);
		lua_call(L, 1, 0);
	}
#if defined(XLUA_LIB_FFI) && LJ_HASFFI
	luaL_findtable(L, LUA_REGISTRYINDEX, "_PRELOAD", 1);
	lua_pushcfunction(L, luaopen_ffi);
	lua_setfield(L, -2, LUA_FFILIBNAME);
	lua_pop(L, 1);
#endif
}

#elif LUA_VERSION_NUM >= 505

static const struct {
	const char *name;
	lua_CFunction func;
	int bit;
} xlua_std_libs[] = {
	{LUA_GNAME, luaopen_base, LUA_GLIBK},
#ifdef XLUA_LIB_PACKAGE
	{LUA_LOADLIBNAME, luaopen_package, LUA_LOADLIBK},
#endif
#ifdef XLUA_LIB_COROUTINE
	{LUA_COLIBNAME, luaopen_coroutine, LUA_COLIBK},
#endif
#ifdef XLUA_LIB_DEBUG
	{LUA_DBLIBNAME, luaopen_debug, LUA_DBLIBK},
#endif
#ifdef XLUA_LIB_IO
	{LUA_IOLIBNAME, luaopen_io, LUA_IOLIBK},
#endif
#ifdef XLUA_LIB_MATH
	{LUA_MATHLIBNAME, luaopen_math, LUA_MATHLIBK},
#endif
#ifdef XLUA_LIB_OS
	{LUA_OSLIBNAME, luaopen_os, LUA_OSLIBK},
#endif
#ifdef XLUA_LIB_STRING
	{LUA_STRLIBNAME, luaopen_string, LUA_STRLIBK},
#endif
#ifdef XLUA_LIB_TABLE
	{LUA_TABLIBNAME, luaopen_table, LUA_TABLIBK},
#endif
#ifdef XLUA_LIB_UTF8
	{LUA_UTF8LIBNAME, luaopen_utf8, LUA_UTF8LIBK},
#endif
	{NULL, NULL, 0}
};

/* linit.c's luaL_openselectedlibs over the libraries the build contains. */
LUALIB_API void luaL_openselectedlibs(lua_State *L, int load, int preload) {
	int i;
	luaL_getsubtable(L, LUA_REGISTRYINDEX, LUA_PRELOAD_TABLE);
	for (i = 0; xlua_std_libs[i].name != NULL; i++) {
		if (load & xlua_std_libs[i].bit) {
			luaL_requiref(L, xlua_std_libs[i].name, xlua_std_libs[i].func, 1);
			lua_pop(L, 1);
		} else if (preload & xlua_std_libs[i].bit) {
			lua_pushcfunction(L, xlua_std_libs[i].func);
			lua_setfield(L, -2, xlua_std_libs[i].name);
		}
	}
	lua_pop(L, 1);
}

#else /* Lua 5.3 and 5.4 */

static const luaL_Reg xlua_loaded_libs[] = {
	{"_G", luaopen_base},
#ifdef XLUA_LIB_PACKAGE
	{LUA_LOADLIBNAME, luaopen_package},
#endif
#ifdef XLUA_LIB_COROUTINE
	{LUA_COLIBNAME, luaopen_coroutine},
#endif
#ifdef XLUA_LIB_TABLE
	{LUA_TABLIBNAME, luaopen_table},
#endif
#ifdef XLUA_LIB_IO
	{LUA_IOLIBNAME, luaopen_io},
#endif
#ifdef XLUA_LIB_OS
	{LUA_OSLIBNAME, luaopen_os},
#endif
#ifdef XLUA_LIB_STRING
	{LUA_STRLIBNAME, luaopen_string},
#endif
#ifdef XLUA_LIB_MATH
	{LUA_MATHLIBNAME, luaopen_math},
#endif
#ifdef XLUA_LIB_UTF8
	{LUA_UTF8LIBNAME, luaopen_utf8},
#endif
#ifdef XLUA_LIB_DEBUG
	{LUA_DBLIBNAME, luaopen_debug},
#endif
#if defined(LUA_COMPAT_BITLIB)
	{LUA_BITLIBNAME, luaopen_bit32},
#endif
	{NULL, NULL}
};

/* linit.c's luaL_openlibs with the selected libraries. */
LUALIB_API void luaL_openlibs(lua_State *L) {
	const luaL_Reg *lib;
	for (lib = xlua_loaded_libs; lib->func; lib++) {
		luaL_requiref(L, lib->name, lib->func, 1);
		lua_pop(L, 1);
	}
}

#endif
