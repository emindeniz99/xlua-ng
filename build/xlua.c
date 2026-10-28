/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

#define LUA_LIB

/* XLUA_AMALGAMATION: the Unity WebGL build compiles all sources as one translation
   unit and has no include paths, so it includes the Lua headers before this file. */
#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#endif

#include <string.h>
#include <stdint.h>
#include "i64lib.h"

#if defined(USING_LUAJIT)
#include "lj_obj.h"
/* xlua_gl reads lua_State through lj_obj.h, whose layout follows LJ_GC64. A
 * 64-bit LuaJIT is supported in GC64 mode only, the mode of every 64-bit
 * plugin (build/README.md, "LuaJIT GC64"); 32-bit targets have one mode. This
 * catches LUAJIT_DISABLE_GC64 passed to this file; CMakeLists.txt checks the
 * library itself. */
#if LJ_64 && !LJ_GC64
#error "xLua needs a 64-bit LuaJIT in GC64 mode: build LuaJIT and xlua.c without LUAJIT_DISABLE_GC64 (msvcbuild_mt.bat without nogc64)"
#endif
#elif !defined(XLUA_AMALGAMATION)
#include "lstate.h"
#endif

#include "xlua_core.h"

/*
** stdcall C function support
*/

static int tag = 0;
/* Event names for the xlua.sethook profiler. Event 4 is LUA_HOOKTAILRET in
 * Lua 5.1 and LuaJIT (which never reports it) and LUA_HOOKTAILCALL since Lua
 * 5.2: the entry of a function reached through `return f()`. Upstream named it
 * "tail return" everywhere, so perf.profiler took a tail call for a return. */
#if defined(USING_LUAJIT)
static const char *const hooknames[] = {"call", "return", "line", "count", "tail return"};
#else
static const char *const hooknames[] = {"call", "return", "line", "count", "tail call"};
#endif
static int hook_index = -1;

LUA_API void *xlua_tag(void)
{
	return &tag;
}

LUA_API int xlua_get_registry_index(void) {
	return LUA_REGISTRYINDEX;
}

LUA_API int xlua_get_lib_version(void) {
	return 110;
}

#if LUA_VERSION_NUM >= 505
/* Lua 5.5 made this a macro; managed P/Invoke still needs the symbol. */
#undef luaL_openlibs
LUA_API void luaL_openlibs(lua_State *L) {
    luaL_openselectedlibs(L, ~0, 0);
}
#endif

#if defined(_MSC_VER)
#define XLUA_NOINLINE __declspec(noinline)
#elif defined(__GNUC__)
#define XLUA_NOINLINE __attribute__((noinline))
#else
#define XLUA_NOINLINE
#endif

/* A full userdata whose metatable holds the bridge tag: a C# object or
 * struct that xLua pushed. lua_getmetatable and lua_rawget ignore
 * __metatable, so XLUA_LOCK_METATABLES does not hide the tag from it. */
static XLUA_NOINLINE int is_cs_data(lua_State *L, int idx) {
	if (LUA_TUSERDATA == lua_type(L, idx) && lua_getmetatable(L, idx)) {
		lua_pushlightuserdata(L, &tag);
		lua_rawget(L,-2);
		if (!lua_isnil (L,-1)) {
			lua_pop (L, 2);
			return 1;
		}
		lua_pop (L, 2);
	}
	return 0;
}

#if LUA_VERSION_NUM >= 502
#define userdata_size(L, idx) lua_rawlen(L, idx)
#else
#define userdata_size(L, idx) lua_objlen(L, idx)
#endif

/* Both readers take the index only from a full userdata. A light userdata
 * (a C# IntPtr, or a native pointer from a C module) holds an address, not a
 * block: reading an int there is undefined, and obj.Method(ptr), a `.` where
 * `:` was meant, used to do it. The fast reader also needs room for the int,
 * which a zero-sized userdata (LuaJIT's newproxy) does not have. A tagged
 * userdata is always one of xLua's, at least an int long. Foreign full
 * userdata still pass the fast reader; the bridge checks of debug builds
 * (XLUA_BRIDGE_CHECKS, C#) use the safe one. */
LUA_API int xlua_tocsobj_safe(lua_State *L,int index) {
	int *udata = LUA_TUSERDATA == lua_type(L, index) ? (int *)lua_touserdata(L, index) : NULL;
	if (udata != NULL) {
		if (lua_getmetatable(L,index)) {
		    lua_pushlightuserdata(L, &tag);
			lua_rawget(L,-2);
			if (!lua_isnil (L,-1)) {
				lua_pop (L, 2);
				return *udata;
			}
			lua_pop (L, 2);
		}
	}
	return -1;
}

LUA_API int xlua_tocsobj_fast (lua_State *L,int index) {
	/* lua_touserdata is NULL for anything but userdata, and the length of a
	 * light userdata is 0: two calls instead of three with lua_type. */
	int *udata = (int *)lua_touserdata(L, index);
	if (udata != NULL && userdata_size(L, index) >= sizeof(int))
		return *udata;
	return -1;
}

/* A float as a C# uint for xlua_touint: truncated toward zero, then wrapped
 * modulo 2^32 like an integer (-1.5 and -1 both give 4294967295). NaN and
 * values outside the 64-bit range give 0. A plain (uint32_t) cast is undefined
 * for negative and too large values, and gave 0 on ARM64 but 4294967295 on
 * x86-64 for -1.5. C# arguments and struct fields now use xlua_tointeger64,
 * which rejects a float without an integer value; xlua_touint stays for array
 * elements and for wrappers generated before that change. */
static uint32_t number_to_uint32(lua_Number n) {
	if (n >= -9223372036854775808.0 && n < 9223372036854775808.0) {
		return (uint32_t)(int64_t)n;
	}
	return 0;
}

#if LUA_VERSION_NUM == 501
#undef lua_getglobal
LUA_API int lua_getglobal (lua_State *L, const char *name) {
	lua_getfield(L, LUA_GLOBALSINDEX, name);
	return 0;
}

#undef lua_setglobal
LUA_API void lua_setglobal (lua_State *L, const char *name) {
	lua_setfield(L, LUA_GLOBALSINDEX, name);
}

LUA_API int lua_isinteger (lua_State *L, int idx) {
	(void)L;
	(void)idx;
	return 0;
}

LUA_API uint32_t xlua_objlen (lua_State *L, int idx) {
	return (uint32_t)lua_objlen (L, idx); /* the managed side reads a uint */
}

LUA_API uint32_t xlua_touint (lua_State *L, int idx) {
	return number_to_uint32(lua_tonumber(L, idx));
}

LUA_API void xlua_pushuint (lua_State *L, uint32_t n) {
	lua_pushnumber(L, n);
}
#endif

#if LUA_VERSION_NUM >= 503
/* The upvalue that holds a Lua function's environment: the one named _ENV,
 * which need not be the first. Without debug information (stripped bytecode)
 * every name reads "(no name)" ("(*no name)" in 5.3); a main chunk's only
 * upvalue is _ENV, so upvalue 1 is used then. 0 if the function has none. */
static int env_upvalue(lua_State *L, int idx) {
	const char *name;
	int i;
	for (i = 1; (name = lua_getupvalue(L, idx, i)) != NULL; i++) {
		lua_pop(L, 1);
		if (strcmp(name, "_ENV") == 0) {
			return i;
		}
		if (name[0] == '(') { /* no debug information: no name is known */
			return 1;
		}
	}
	return 0;
}

/* Lua 5.1's lua_setfenv for LuaEnv.LoadString/DoString and LuaFunction.SetEnv:
 * pops the table on top and makes it the environment of the Lua function at
 * idx; returns 1 if it did, 0 for C functions, functions that use no globals
 * and other values. A closure shares its _ENV upvalue with the chunk that
 * defined it and that chunk's other closures, so the function first gets an
 * upvalue of its own (the _ENV of a fresh empty chunk) instead of having the
 * shared one overwritten, which changed all their globals. */
#if defined(XLUA_BYTECODE_ONLY)
/* A bytecode-only build (CMake XLUA_BYTECODE_ONLY) cannot compile the empty
 * chunk. A copy of the function, dumped and loaded again, has fresh upvalues
 * as every loaded chunk has; its _ENV upvalue then serves the same purpose.
 * setfenv_copy raises only memory errors and runs protected, because C#
 * calls lua_setfenv directly. */
typedef struct setfenv_dump {
	int init;
	int done;
	luaL_Buffer b;
} setfenv_dump;

/* As lstrlib.c's string.dump writer: the buffer starts in the writer, so
 * that the function stays on top for lua_dump (luaL_buffinit pushes a
 * placeholder in 5.4 and 5.5), and 5.5's final NULL block moves the result
 * to slot 1, because 5.5's lua_dump restores the stack top. */
static int setfenv_writer(lua_State *L, const void *p, size_t sz, void *ud) {
	setfenv_dump *d = (setfenv_dump *)ud;
	if (!d->init) {
		d->init = 1;
		luaL_buffinit(L, &d->b);
	}
	if (p == NULL) {
		luaL_pushresult(&d->b);
		lua_replace(L, 1);
		d->done = 1;
	}
	else {
		luaL_addlstring(&d->b, (const char *)p, sz);
	}
	return 0;
}

/* function -> a copy of it, loaded from its stripped dump. */
static int setfenv_copy(lua_State *L) {
	setfenv_dump d;
	const char *s;
	size_t len;
	d.init = 0;
	d.done = 0;
	lua_settop(L, 1);
	lua_pushvalue(L, 1);  /* on top for lua_dump; slot 1 receives the dump */
	if (lua_dump(L, setfenv_writer, &d, 1) != 0 || !d.init) {
		return luaL_error(L, "unable to dump the function");
	}
	if (!d.done) {  /* 5.3 and 5.4 */
		luaL_pushresult(&d.b);
		lua_replace(L, 1);
	}
	lua_settop(L, 1);
	s = lua_tolstring(L, 1, &len);
	if (luaL_loadbufferx(L, s, len, "=setfenv", "b") != LUA_OK) {
		return lua_error(L);
	}
	return 1;
}

/* lua_setfenv's second half for the function at idx, whose _ENV is upvalue
 * up (0: none), with the copy's upvalue up as the fresh one. */
static int setfenv_from_copy(lua_State *L, int idx, int up) {
	if (up != 0) {
		push_helper(L, setfenv_copy);
		lua_pushvalue(L, idx);
	}
	if (up == 0 || lua_pcall(L, 1, 1, 0) != LUA_OK) {
		lua_pop(L, up == 0 ? 1 : 2); /* the table, and the error if any */
		return 0;
	}
	lua_pushvalue(L, -2);
	lua_setupvalue(L, -2, up);
	lua_upvaluejoin(L, idx, up, -1, up);
	lua_pop(L, 2);
	return 1;
}
#endif

LUA_API int lua_setfenv(lua_State *L, int idx)
{
	int up = 0;
	idx = lua_absindex(L, idx);
	if (lua_type(L, idx) == LUA_TFUNCTION && !lua_iscfunction(L, idx)) {
		up = env_upvalue(L, idx);
	}
#if defined(XLUA_BYTECODE_ONLY)
	return setfenv_from_copy(L, idx, up);
#else
	if (up == 0 || luaL_loadbuffer(L, "", 0, "=setfenv") != LUA_OK) {
		lua_pop(L, up == 0 ? 1 : 2); /* the table, and the load error if any */
		return 0;
	}
	lua_pushvalue(L, -2);
	lua_setupvalue(L, -2, 1);
	lua_upvaluejoin(L, idx, up, -1, 1);
	lua_pop(L, 2);
	return 1;
#endif
}

LUA_API uint32_t xlua_objlen (lua_State *L, int idx) {
	return (uint32_t)luaL_len (L, idx);
}

LUA_API uint32_t xlua_touint (lua_State *L, int idx) {
	return lua_isinteger(L, idx) ? (uint32_t)lua_tointeger(L, idx) : number_to_uint32(lua_tonumber(L, idx));
}

LUA_API void xlua_pushuint (lua_State *L, uint32_t n) {
	lua_pushinteger(L, n);
}

#undef lua_insert
LUA_API void lua_insert (lua_State *L, int idx) {
    lua_rotate(L, idx, 1);
}

#undef lua_remove
LUA_API void lua_remove (lua_State *L, int idx) {
	lua_rotate(L, idx, -1);
	lua_pop(L, 1);
}

#undef lua_replace
LUA_API void lua_replace (lua_State *L, int idx) {
	lua_copy(L, -1, idx);
	lua_pop(L, 1);
}

#undef lua_pcall
/* Also the protected entry of a core state (xlua_core.h): every C# call into
 * Lua and the protected helpers below come through here. */
LUA_API int lua_pcall (lua_State *L, int nargs, int nresults, int errfunc) {
	xlua_core_entry entry;
	int status;
#if XLUA_CORE_LIMITS
	int base = lua_gettop(L) - nargs - 1;
#endif
	xlua_core_enter(L, &entry);
	status = lua_pcallk(L, nargs, nresults, errfunc, 0, NULL);
	xlua_core_leave(&entry);
#if XLUA_CORE_LIMITS
	if (status == LUA_OK && entry.xc != NULL && entry.stop != 0) {
		status = xlua_core_fail_stopped(L, entry.stop, base);  /* stopped after its last instruction */
	}
#endif
	return status;
}

#undef lua_tonumber
LUA_API lua_Number lua_tonumber (lua_State *L, int idx) {
	return lua_tonumberx(L, idx, NULL);
}

#endif

/* Since Lua 5.4, lua_newuserdata reserves one user value per userdata. xLua
 * stores none in its object handles and structs, so ask for zero slots. */
#if LUA_VERSION_NUM >= 504
#define new_userdata(L, size) lua_newuserdatauv(L, size, 0)
#else
#define new_userdata(L, size) lua_newuserdata(L, size)
#endif

#if LUA_VERSION_NUM < 503
#define lua_absindex(L, index) (((index) > 0 || (index) <= LUA_REGISTRYINDEX) ? (index) : lua_gettop(L) + (index) + 1)
#endif

LUA_API void xlua_getloaders (lua_State *L) {
	lua_getglobal(L, "package");
#if LUA_VERSION_NUM == 501
    lua_getfield(L, -1, "loaders");
#else
	lua_getfield(L, -1, "searchers");
#endif
	lua_remove(L, -2);
}

LUA_API void xlua_rawgeti (lua_State *L, int idx, int64_t n) {
#if LUA_VERSION_NUM == 501
	lua_rawgeti(L, idx, (int)n); /* LuaJIT's lua_rawgeti takes an int; managed callers pass int indices */
#else
	lua_rawgeti(L, idx, (lua_Integer)n);
#endif
}

LUA_API void xlua_rawseti (lua_State *L, int idx, int64_t n) {
#if LUA_VERSION_NUM == 501
	lua_rawseti(L, idx, (int)n); /* LuaJIT's lua_rawseti takes an int; managed callers pass int indices */
#else
	lua_rawseti(L, idx, (lua_Integer)n);
#endif
}

LUA_API int xlua_ref_indirect(lua_State *L, int indirectRef) {
	int ret = 0;
	lua_rawgeti(L, LUA_REGISTRYINDEX, indirectRef);
	lua_pushvalue(L, -2);
	ret = luaL_ref(L, -2);
	lua_pop(L, 2);
	return ret;
}

LUA_API void xlua_getref_indirect(lua_State *L, int indirectRef, int reference) {
	lua_rawgeti(L, LUA_REGISTRYINDEX, indirectRef);
	lua_rawgeti(L, -1, reference);
	lua_remove(L, -2);
}

LUA_API int xlua_tointeger (lua_State *L, int idx) {
	return (int)lua_tointeger(L, idx);
}

LUA_API void xlua_pushinteger (lua_State *L, int n) {
	lua_pushinteger(L, n);
}

/* Lua 5.3's rule for a value that goes into an integer slot (luaL_checkinteger),
 * applied on every runtime: an integer, a float with an integer value in the
 * 64-bit range, or a string that converts to one of these. LuaJIT has only
 * doubles, so the same test is made on them, and an int64/uint64 userdata of
 * i64lib gives its 64 bits. Returns 1 and stores the value, or 0 for a number
 * or numeric string without an integer representation (a fraction, NaN, an
 * infinity, or outside the 64-bit range). Any other value gives 1 and 0: the
 * callers decide whether to accept it. */
static int value_to_integer(lua_State *L, int idx, int64_t *out) {
#if LUA_VERSION_NUM >= 503
	int isnum;
	lua_Integer v = lua_tointegerx(L, idx, &isnum);
	*out = isnum ? v : 0;
	return isnum || !lua_isnumber(L, idx);
#else
	lua_Number d;
	*out = 0;
	if (lua_isint64_or_uint64(L, idx)) {
		*out = lua_toint64(L, idx);
		return 1;
	}
	if (!lua_isnumber(L, idx)) {
		return 1;
	}
	d = lua_tonumber(L, idx);
	/* NaN fails the range test; the cast is defined inside the range. */
	if (!(d >= -9223372036854775808.0 && d < 9223372036854775808.0) || d != (lua_Number)(int64_t)d) {
		return 0;
	}
	*out = (int64_t)d;
	return 1;
#endif
}

/* For the C# integer casters (LuaDLL.cs xlua_checkinteger): the value at idx
 * by value_to_integer, and in *valid whether it has an integer representation.
 * The narrower C# types take the low bits of the result. */
LUA_API int64_t xlua_tointeger64(lua_State *L, int idx, int *valid) {
	int64_t v;
	*valid = value_to_integer(L, idx, &v);
	return v;
}

/* For the C# overload checks of integer parameters: a number (not a string)
 * with an integer representation. */
LUA_API int xlua_isinteger_number(lua_State *L, int idx) {
	int64_t v;
	return lua_type(L, idx) == LUA_TNUMBER && value_to_integer(L, idx, &v);
}

/* For the C# overload selection (ObjectCasters.cs OverloadScore): what kind of
 * number the value at idx is, so that an integer prefers an integer overload
 * and a float a floating one. 0: not a number (strings included); 1: a float
 * without an integer representation (a fraction, NaN, an infinity, outside the
 * 64-bit range); 2 and 3: a float with an integer value (Lua 5.3+ only, such as
 * 3.0), outside or inside the int32 range; 4 and 5: an integer outside or
 * inside the int32 range. LuaJIT has only doubles: a value with an integer
 * representation counts as an integer there (4 or 5). */
LUA_API int xlua_number_kind(lua_State *L, int idx) {
	int64_t v;
	int integer;
	if (lua_type(L, idx) != LUA_TNUMBER) {
		return 0;
	}
#if LUA_VERSION_NUM >= 503
	integer = lua_isinteger(L, idx);
#else
	integer = 1;
#endif
	if (!value_to_integer(L, idx, &v)) {
		return 1;
	}
	return (integer ? 4 : 2) + (v >= INT32_MIN && v <= INT32_MAX);
}

/* Pushes the message luaL_argerror(L, arg, extramsg) raises for an argument
 * of the running C function, without raising it: the C# bridge cannot raise a
 * Lua error from C#, so it throws an exception with this text instead. A
 * function whose name Lua does not know is named '?' (luaL_argerror would
 * also search the loaded modules). */
LUA_API void xlua_pushargerror(lua_State *L, int arg, const char *extramsg) {
	lua_Debug ar;
	if (!lua_getstack(L, 0, &ar)) {
		lua_pushfstring(L, "bad argument #%d (%s)", arg, extramsg);
		return;
	}
	lua_getinfo(L, "n", &ar);
	luaL_where(L, 1);
	if (strcmp(ar.namewhat, "method") == 0) {
		arg--; /* self does not count */
		if (arg == 0) {
			lua_pushfstring(L, "calling '%s' on bad self (%s)", ar.name, extramsg);
			lua_concat(L, 2);
			return;
		}
	}
	lua_pushfstring(L, "bad argument #%d to '%s' (%s)", arg, ar.name != NULL ? ar.name : "?", extramsg);
	lua_concat(L, 2);
}

LUA_API void xlua_pushlstring (lua_State *L, const char *s, int len) {
	lua_pushlstring(L, s, len);
}

/* A protected entry of a core state (xlua_core.h): the parser runs under
 * lua_load's own protection, so a refused allocation returns LUA_ERRMEM. */
LUALIB_API int xluaL_loadbuffer (lua_State *L, const char *buff, int size,
                                const char *name) {
	xlua_core_entry entry;
	int status;
	xlua_core_enter(L, &entry);
	status = luaL_loadbuffer(L, buff, size, name);
	xlua_core_leave(&entry);
#if XLUA_CORE_LIMITS
	if (status == LUA_OK && entry.xc != NULL && entry.stop != 0) {
		/* an Interrupt or a deadline during the compile fails DoString here,
		 * instead of ending with this entry before the chunk runs */
		status = xlua_core_fail_stopped(L, entry.stop, lua_gettop(L) - 1);
	}
#endif
	return status;
}

static int c_lua_gettable(lua_State* L) {    
    lua_gettable(L, 1);    
    return 1;
}

LUA_API int xlua_pgettable(lua_State* L, int idx) {
    int top = lua_gettop(L);
    idx = lua_absindex(L, idx);
    push_helper(L, c_lua_gettable);
    lua_pushvalue(L, idx);
    lua_pushvalue(L, top);
    lua_remove(L, top);
    return lua_pcall(L, 2, 1, 0);
}

static int c_lua_gettable_bypath(lua_State* L) {
	size_t len = 0;
	const char * pos = NULL;
	const char * path = lua_tolstring(L, 2, &len);
	lua_pushvalue(L, 1);
	do {
		pos = strchr(path, '.');
		if (NULL == pos) {
			lua_pushlstring(L, path, len);
		} else {
			lua_pushlstring(L, path, pos - path);
			len = len - (pos - path + 1);
			path = pos + 1;
		}
		lua_gettable(L, -2);
		if (lua_type(L, -1) != LUA_TTABLE) {
			if (NULL != pos) { // not found in path
				lua_pushnil(L);
			}
			break;
		}
		lua_remove(L, -2);
	} while(pos);
    return 1;
}

LUA_API int xlua_pgettable_bypath(lua_State* L, int idx, const char *path) {
	idx = lua_absindex(L, idx);
	push_helper(L, c_lua_gettable_bypath);
	lua_pushvalue(L, idx);
	lua_pushstring(L, path);
	return lua_pcall(L, 2, 1, 0);
}

static int c_lua_settable(lua_State* L) {
    lua_settable(L, 1);
    return 0;
}

LUA_API int xlua_psettable(lua_State* L, int idx) {
    int top = lua_gettop(L);
    idx = lua_absindex(L, idx);
    push_helper(L, c_lua_settable);
    lua_pushvalue(L, idx);
    lua_pushvalue(L, top - 1);
    lua_pushvalue(L, top);
    lua_remove(L, top);
    lua_remove(L, top - 1);
    return lua_pcall(L, 3, 0, 0);
}

static int c_lua_settable_bypath(lua_State* L) {
    size_t len = 0;
	const char * pos = NULL;
	const char * path = lua_tolstring(L, 2, &len);
	lua_pushvalue(L, 1);
	for (;;) { /* ends at the last key or raises */
		pos = strchr(path, '.');
		if (NULL == pos) { // last
			lua_pushlstring(L, path, len);
			lua_pushvalue(L, 3);
			lua_settable(L, -3);
			lua_pop(L, 1);
			return 0;
		}
		lua_pushlstring(L, path, pos - path);
		len = len - (pos - path + 1);
		path = pos + 1;
		lua_gettable(L, -2);
		if (lua_type(L, -1) != LUA_TTABLE) {
			return luaL_error(L, "can not set value to %s", lua_tostring(L, 2));
		}
		lua_remove(L, -2);
	}
}

LUA_API int xlua_psettable_bypath(lua_State* L, int idx, const char *path) {
    int top = lua_gettop(L);
    idx = lua_absindex(L, idx);
    push_helper(L, c_lua_settable_bypath);
    lua_pushvalue(L, idx);
    lua_pushstring(L, path);
    lua_pushvalue(L, top);
    lua_remove(L, top);
    return lua_pcall(L, 3, 0, 0);
}

static int c_lua_getglobal(lua_State* L) {
	lua_getglobal(L, lua_tostring(L, 1));
	return 1;
}

LUA_API int xlua_getglobal (lua_State *L, const char *name) {
	push_helper(L, c_lua_getglobal);
	lua_pushstring(L, name);
	return lua_pcall(L, 1, 1, 0);
}

static int c_lua_setglobal(lua_State* L) {
	lua_setglobal(L, lua_tostring(L, 1));
	return 0;
}

LUA_API int xlua_setglobal (lua_State *L, const char *name) {
	int top = lua_gettop(L);
	push_helper(L, c_lua_setglobal);
	lua_pushstring(L, name);
	lua_pushvalue(L, top);
	lua_remove(L, top);
	return lua_pcall(L, 2, 0, 0);
}

/* lua_next under lua_pcall, for LuaTable.ForEach and GetKeys: C# code runs
 * between two steps and may change the table so that the key is gone, and
 * lua_next then raises "invalid key to 'next'" with no handler (Lua panics)
 * or through managed frames. Takes the key from the top. Returns 1 with the
 * key and the value pushed, 0 at the end with the key popped, or -1 with the
 * error object in place of the key. */
static int c_lua_next(lua_State *L) {
	return lua_next(L, 1) ? 2 : 0;
}

LUA_API int xlua_pnext(lua_State *L, int idx) {
	int top = lua_gettop(L);
	idx = lua_absindex(L, idx);
	push_helper(L, c_lua_next);
	lua_pushvalue(L, idx);
	lua_pushvalue(L, top);
	lua_remove(L, top);
	if (lua_pcall(L, 2, LUA_MULTRET, 0) != 0) {
		return -1;
	}
	return lua_gettop(L) == top + 1 ? 1 : 0;
}

#if LUA_VERSION_NUM >= 503
static int c_lua_len(lua_State *L) {
	lua_pushinteger(L, luaL_len(L, 1));
	return 1;
}
#endif

/* The length of the value at idx as Lua's # operator gives it, for C#: Lua
 * 5.3 and later call a __len metamethod, which can raise (an error, or a
 * result that is not an integer), so that case runs under lua_pcall; a value
 * without __len takes no call. LuaJIT reads the raw length. Returns 0 with
 * *len set, or the status with the error object pushed. xlua_objlen raised
 * from C#, with no handler or through managed frames. */
LUA_API int xlua_plen(lua_State *L, int idx, uint32_t *len) {
#if LUA_VERSION_NUM >= 503
	int status;
	if (luaL_getmetafield(L, idx, "__len") == LUA_TNIL) {
		*len = (uint32_t)lua_rawlen(L, idx);
		return 0;
	}
	lua_pop(L, 1);
	idx = lua_absindex(L, idx);
	push_helper(L, c_lua_len);
	lua_pushvalue(L, idx);
	status = lua_pcall(L, 1, 1, 0);
	if (status == 0) {
		*len = (uint32_t)lua_tointeger(L, -1);
		lua_pop(L, 1);
	}
	return status;
#else
	*len = (uint32_t)lua_objlen(L, idx);
	return 0;
#endif
}

/* 1 while a function runs on L (Lua code, a C# callback, coroutine.resume
 * on the way to a coroutine), 0 when no call is active. LuaEnv.Dispose
 * refuses in the first case: lua_close would free the frames and the stack
 * that the call returns to. */
LUA_API int xlua_running(lua_State *L) {
	lua_Debug ar;
	return lua_getstack(L, 0, &ar);
}

LUA_API int xlua_tryget_cachedud(lua_State *L, int key, int cache_ref) {
	lua_rawgeti(L, LUA_REGISTRYINDEX, cache_ref);
	lua_rawgeti(L, -1, key);
	if (!lua_isnil(L, -1))
	{
		lua_remove(L, -2);
		return 1;
	}
	lua_pop(L, 2);
	return 0;
}

/* The C# bridge checks (XLUA_BRIDGE_CHECKS: the Editor, Development Builds,
 * XLUA_GENERAL) call this instead of xlua_tryget_cachedud. The userdata
 * cache is a plain table in the registry,
 * so a script with debug.getregistry can store any value under an object
 * index, and every later push of that C# object then returns that value.
 * This accepts only a C# object userdata (bridge tag, at least an int of
 * payload) that holds the index it is stored under; anything else is a miss,
 * and the caller pushes a new userdata, which replaces the entry. */
LUA_API int xlua_tryget_cachedud_checked(lua_State *L, int key, int cache_ref) {
	lua_rawgeti(L, LUA_REGISTRYINDEX, cache_ref);
	lua_rawgeti(L, -1, key);
	if (is_cs_data(L, -1) && userdata_size(L, -1) >= sizeof(int) && *(int *)lua_touserdata(L, -1) == key)
	{
		lua_remove(L, -2);
		return 1;
	}
	lua_pop(L, 2);
	return 0;
}

static void cacheud(lua_State *L, int key, int cache_ref) {
	lua_rawgeti(L, LUA_REGISTRYINDEX, cache_ref);
	lua_pushvalue(L, -2);
	lua_rawseti(L, -2, key);
	lua_pop(L, 1);
}


LUA_API void xlua_pushcsobj(lua_State *L, int key, int meta_ref, int need_cache, int cache_ref) {
	int* pointer = (int*)new_userdata(L, sizeof(int));
	*pointer = key;
	
	if (need_cache) cacheud(L, key, cache_ref);

    lua_rawgeti(L, LUA_REGISTRYINDEX, meta_ref);

	lua_setmetatable(L, -2);
}

void print_top(lua_State *L) {
	lua_getglobal(L, "print");
	lua_pushvalue(L, -2);
	lua_call(L, 1, 0);
}

void print_str(lua_State *L, char *str) {
	lua_getglobal(L, "print");
	lua_pushstring(L, str);
	lua_call(L, 1, 0);
}

void print_value(lua_State *L,  char *str, int idx) {
	idx = lua_absindex(L, idx);
	lua_getglobal(L, "print");
	lua_pushstring(L, str);
	lua_pushvalue(L, idx);
	lua_call(L, 2, 0);
}

/* The BaseType walk of the indexer closures below: from the type in the
 * upvalue at base up its BaseType chain to the first type with an entry in
 * the index function table at funcs, which is stored in the upvalue at found
 * (left nil when no type has one); base is cleared, so the walk runs once
 * per closure. Real System.Type chains end after a few types, but the debug
 * library can put a table whose BaseType leads back to itself into the
 * closure (debug.setupvalue), and that walk looped here in C, where the
 * instruction count hook cannot stop it (docs/research/bridge-audit.md,
 * "BaseType cycle"). The walk now stops with an error after
 * XLUA_MAX_BASE_TYPES types and leaves the closure as it was. */
#define XLUA_MAX_BASE_TYPES 1000

static void find_base_func(lua_State *L, int base, int funcs, int found) {
	int depth = 0;
	lua_pushvalue(L, base);
	while (!lua_isnil(L, -1)) {
		if (++depth > XLUA_MAX_BASE_TYPES) {
			luaL_error(L, "the BaseType chain is longer than %d types (a cycle?)", XLUA_MAX_BASE_TYPES);
		}
		lua_pushvalue(L, -1);
		lua_gettable(L, funcs);
		if (!lua_isnil(L, -1)) {
			lua_replace(L, found);
			break;
		}
		lua_pop(L, 1);
		lua_getfield(L, -1, "BaseType");
		lua_remove(L, -2);
	}
	lua_pop(L, 1);
	lua_pushnil(L);
	lua_replace(L, base);
}

//upvalue --- [1]: methods, [2]:getters, [3]:csindexer, [4]:base, [5]:indexfuncs, [6]:arrayindexer, [7]:baseindex
//param   --- [1]: obj, [2]: key
LUA_API int obj_indexer(lua_State *L) {	
	if (!lua_isnil(L, lua_upvalueindex(1))) {
		lua_pushvalue(L, 2);
		lua_gettable(L, lua_upvalueindex(1));
		if (!lua_isnil(L, -1)) {//has method
			return 1;
		}
		lua_pop(L, 1);
	}
	
	if (!lua_isnil(L, lua_upvalueindex(2))) {
		lua_pushvalue(L, 2);
		lua_gettable(L, lua_upvalueindex(2));
		if (!lua_isnil(L, -1)) {//has getter
			lua_pushvalue(L, 1);
			lua_call(L, 1, 1);
			return 1;
		}
		lua_pop(L, 1);
	}
	
	
	if (!lua_isnil(L, lua_upvalueindex(6)) && lua_type(L, 2) == LUA_TNUMBER) {
		lua_pushvalue(L, lua_upvalueindex(6));
		lua_pushvalue(L, 1);
		lua_pushvalue(L, 2);
		lua_call(L, 2, 1);
		return 1;
	}
	
	if (!lua_isnil(L, lua_upvalueindex(3))) {
		lua_pushvalue(L, lua_upvalueindex(3));
		lua_pushvalue(L, 1);
		lua_pushvalue(L, 2);
		lua_call(L, 2, 2);
		if (lua_toboolean(L, -2)) {
			return 1;
		}
		lua_pop(L, 2);
	}
	
	if (!lua_isnil(L, lua_upvalueindex(4))) {
		find_base_func(L, lua_upvalueindex(4), lua_upvalueindex(5), lua_upvalueindex(7));
	}
	
	if (!lua_isnil(L, lua_upvalueindex(7))) {
		lua_settop(L, 2);
		lua_pushvalue(L, lua_upvalueindex(7));
		lua_insert(L, 1);
		lua_call(L, 2, 1);
		return 1;
	} else {
		return 0;
	}
}

LUA_API int gen_obj_indexer(lua_State *L) {
	lua_pushnil(L);
	lua_pushcclosure(L, obj_indexer, 7);
	return 0;
}

//upvalue --- [1]:setters, [2]:csnewindexer, [3]:base, [4]:newindexfuncs, [5]:arrayindexer, [6]:basenewindex
//param   --- [1]: obj, [2]: key, [3]: value
LUA_API int obj_newindexer(lua_State *L) {
	if (!lua_isnil(L, lua_upvalueindex(1))) {
		lua_pushvalue(L, 2);
		lua_gettable(L, lua_upvalueindex(1));
		if (!lua_isnil(L, -1)) {//has setter
			lua_pushvalue(L, 1);
			lua_pushvalue(L, 3);
			lua_call(L, 2, 0);
			return 0;
		}
		lua_pop(L, 1);
	}
	
	if (!lua_isnil(L, lua_upvalueindex(2))) {
		lua_pushvalue(L, lua_upvalueindex(2));
		lua_pushvalue(L, 1);
		lua_pushvalue(L, 2);
		lua_pushvalue(L, 3);
		lua_call(L, 3, 1);
		if (lua_toboolean(L, -1)) {
			return 0;
		}
	}
	
	if (!lua_isnil(L, lua_upvalueindex(5)) && lua_type(L, 2) == LUA_TNUMBER) {
		lua_pushvalue(L, lua_upvalueindex(5));
		lua_pushvalue(L, 1);
		lua_pushvalue(L, 2);
		lua_pushvalue(L, 3);
		lua_call(L, 3, 0);
		return 0;
	}
	
	if (!lua_isnil(L, lua_upvalueindex(3))) {
		find_base_func(L, lua_upvalueindex(3), lua_upvalueindex(4), lua_upvalueindex(6));
	}
	
	if (!lua_isnil(L, lua_upvalueindex(6))) {
		lua_settop(L, 3);
		lua_pushvalue(L, lua_upvalueindex(6));
		lua_insert(L, 1);
		lua_call(L, 3, 0);
		return 0;
	} else {
		return luaL_error(L, "cannot set %s, no such field", lua_tostring(L, 2));
	}
}

LUA_API int gen_obj_newindexer(lua_State *L) {
	lua_pushnil(L);
	lua_pushcclosure(L, obj_newindexer, 6);
	return 0;
}

//upvalue --- [1]:getters, [2]:feilds, [3]:base, [4]:indexfuncs, [5]:baseindex
//param   --- [1]: obj, [2]: key
LUA_API int cls_indexer(lua_State *L) {	
	if (!lua_isnil(L, lua_upvalueindex(1))) {
		lua_pushvalue(L, 2);
		lua_gettable(L, lua_upvalueindex(1));
		if (!lua_isnil(L, -1)) {//has getter
			lua_call(L, 0, 1);
			return 1;
		}
		lua_pop(L, 1);
	}
	
	if (!lua_isnil(L, lua_upvalueindex(2))) {
		lua_pushvalue(L, 2);
		lua_rawget(L, lua_upvalueindex(2));
		if (!lua_isnil(L, -1)) {//has feild
			return 1;
		}
		lua_pop(L, 1);
	}
	
	if (!lua_isnil(L, lua_upvalueindex(3))) {
		find_base_func(L, lua_upvalueindex(3), lua_upvalueindex(4), lua_upvalueindex(5));
	}
	
	if (!lua_isnil(L, lua_upvalueindex(5))) {
		lua_settop(L, 2);
		lua_pushvalue(L, lua_upvalueindex(5));
		lua_insert(L, 1);
		lua_call(L, 2, 1);
		return 1;
	} else {
		lua_pushnil(L);
		return 1;
	}
}

LUA_API int gen_cls_indexer(lua_State *L) {
	lua_pushnil(L);
	lua_pushcclosure(L, cls_indexer, 5);
	return 0;
}

//upvalue --- [1]:setters, [2]:base, [3]:indexfuncs, [4]:baseindex
//param   --- [1]: obj, [2]: key, [3]: value
LUA_API int cls_newindexer(lua_State *L) {	
	if (!lua_isnil(L, lua_upvalueindex(1))) {
		lua_pushvalue(L, 2);
		lua_gettable(L, lua_upvalueindex(1));
		if (!lua_isnil(L, -1)) {//has setter
		    lua_pushvalue(L, 3);
			lua_call(L, 1, 0);
			return 0;
		}
	}
	
	if (!lua_isnil(L, lua_upvalueindex(2))) {
		find_base_func(L, lua_upvalueindex(2), lua_upvalueindex(3), lua_upvalueindex(4));
	}
	
	if (!lua_isnil(L, lua_upvalueindex(4))) {
		lua_settop(L, 3);
		lua_pushvalue(L, lua_upvalueindex(4));
		lua_insert(L, 1);
		lua_call(L, 3, 0);
		return 0;
	} else {
		return luaL_error(L, "no static field %s", lua_tostring(L, 2));
	}
}

LUA_API int gen_cls_newindexer(lua_State *L) {
	lua_pushnil(L);
	lua_pushcclosure(L, cls_newindexer, 4);
	return 0;
}

LUA_API int load_error_func(lua_State *L, int ref) {
	lua_rawgeti(L, LUA_REGISTRYINDEX, ref);
	return lua_gettop(L);
}

LUA_API int pcall_prepare(lua_State *L, int error_func_ref, int func_ref) {
	lua_rawgeti(L, LUA_REGISTRYINDEX, error_func_ref);
	lua_rawgeti(L, LUA_REGISTRYINDEX, func_ref);
	return lua_gettop(L) - 1;
}

/* The xlua.sethook profiler's hook: calls the profiler function with the
 * event and the function, and for the return of a C# function also "[C#]".
 * PUC Lua reports the return of the C# function trampolines (xlua_core.c)
 * here, as of any C function; LuaJIT reports no return of a C function, so
 * there the trampolines report it through xlua_profiler_return. Each C# call
 * so gives one call and one return event on every runtime; PUC Lua used to
 * get both returns, and perf.profiler counted every C# call twice. */
static void hook(lua_State *L, lua_Debug *ar)
{
	int event;
	int nargs = 2;

	lua_pushlightuserdata(L, &hook_index);
	lua_rawget(L, LUA_REGISTRYINDEX);

	event = ar->event;
	lua_pushstring(L, hooknames[event]);

	lua_getinfo(L, "nS", ar);
	if (*(ar->what) == 'C') {
		lua_pushfstring(L, "[?%s]", ar->name);
#if !defined(USING_LUAJIT)
		if (event == LUA_HOOKRET) {
			int csharp;
			lua_getinfo(L, "f", ar);
			csharp = xlua_is_csharp_trampoline(lua_tocfunction(L, -1));
			lua_pop(L, 1);
			if (csharp) {
				lua_pushliteral(L, "[C#]");
				nargs = 3;
			}
		}
#endif
	} else {
		lua_pushfstring(L, "%s:%d", ar->short_src, ar->linedefined > 0 ? ar->linedefined : 0);
	}

	lua_call(L, nargs, 0);
}

#if defined(USING_LUAJIT)
/* Reports a C# function's return to the xlua.sethook profiler (LuaJIT only,
 * see hook). The trampolines in xlua_core.c check lua_gethook first. A hook
 * set by debug.sethook, also one that replaced the profiler's, is left alone. */
void xlua_profiler_return(lua_State *L) {
	lua_Debug ar;

	if (lua_gethook(L) != hook) {
		return;
	}
	lua_getstack(L, 0, &ar);
	lua_getinfo(L, "n", &ar);

	lua_pushlightuserdata(L, &hook_index);
	lua_rawget(L, LUA_REGISTRYINDEX);

	if (lua_type(L, -1) != LUA_TFUNCTION){
		lua_pop(L, 1);
		return;
	}

	lua_pushliteral(L, "return");
	lua_pushfstring(L, "[?%s]", ar.name);
	lua_pushliteral(L, "[C#]");

	/* LuaJIT has no core limits (XLUA_CORE_LIMITS), so the profiler owns the hook. */
	lua_sethook(L, 0, 0, 0);
	lua_call(L, 3, 0);
	lua_sethook(L, hook, LUA_MASKCALL | LUA_MASKRET, 0);
}
#endif

/* In a core state the profiler goes through core's hook (xlua_core.c), which
 * also runs the instruction and time limits; otherwise it owns the hook. */
static void set_profiler_hook(lua_State *L, lua_Hook fn) {
#if XLUA_CORE_LIMITS
	if (xlua_core_of(L) != NULL) {
		xlua_core_set_profiler(L, xlua_core_of(L), fn);
		return;
	}
#endif
	lua_sethook(L, fn, fn != NULL ? LUA_MASKCALL | LUA_MASKRET : 0, 0);
}

static int profiler_set_hook(lua_State *L) {
	if (lua_isnoneornil(L, 1)) {
		lua_pushlightuserdata(L, &hook_index);
		lua_pushnil(L);
		lua_rawset(L, LUA_REGISTRYINDEX);
			
		set_profiler_hook(L, NULL);
	} else {
		luaL_checktype(L, 1, LUA_TFUNCTION);
		lua_pushlightuserdata(L, &hook_index);
		lua_pushvalue(L, 1);
		lua_rawset(L, LUA_REGISTRYINDEX);
		set_profiler_hook(L, hook);
	}
	return 0;
}

typedef struct {
	int fake_id;
    unsigned int len;
	char data[1];
} CSharpStruct;

LUA_API void *xlua_pushstruct(lua_State *L, unsigned int size, int meta_ref) {
	CSharpStruct *css = (CSharpStruct *)new_userdata(L, size + sizeof(int) + sizeof(unsigned int));
	css->fake_id = -1;
	css->len = size;
    lua_rawgeti(L, LUA_REGISTRYINDEX, meta_ref);
	lua_setmetatable(L, -2);
	return css;
}

LUA_API void xlua_pushcstable(lua_State *L, unsigned int size, int meta_ref) {
	lua_createtable(L, 0, size); // NOLINT(bugprone-narrowing-conversions): size is only a preallocation hint; Lua ignores non-positive hints
    lua_rawgeti(L, LUA_REGISTRYINDEX, meta_ref);
	lua_setmetatable(L, -2);
}

/* A negative size used to wrap around in size_t: -8 to -1 allocated less than
 * the 8 header bytes written below and set a length near 2^32, so the struct
 * accessors could also write far past the userdata; smaller values raised a
 * memory error. It now pushes nil and returns NULL (the stack still grows by
 * one), like xlua_tostruct's failure result. No Lua error is raised, because a
 * C# caller cannot catch one. */
LUA_API void *xlua_newstruct(lua_State *L, int size, int meta_ref) {
	CSharpStruct *css;
	if (size < 0) {
		lua_pushnil(L);
		return NULL;
	}
	css = (CSharpStruct *)new_userdata(L, (size_t)size + sizeof(int) + sizeof(unsigned int));
	css->fake_id = -1;
	css->len = size;
    lua_rawgeti(L, LUA_REGISTRYINDEX, meta_ref);
	lua_setmetatable(L, -2);
	return css->data;
}

LUA_API void *xlua_tostruct(lua_State *L, int idx, int meta_ref) {
	CSharpStruct *css = (CSharpStruct *)lua_touserdata(L, idx);
	if (NULL != css) {
		if (lua_getmetatable (L, idx)) {
			lua_rawgeti(L, -1, 1);
			if (lua_type(L, -1) == LUA_TNUMBER && (int)lua_tointeger(L, -1) == meta_ref) {
				lua_pop(L, 2);
				return css->data; 
			}
			lua_pop(L, 2);
		}
	}
	return NULL;
}

LUA_API int xlua_gettypeid(lua_State *L, int idx) {
	int type_id = -1;
	if (lua_type(L, idx) == LUA_TUSERDATA) {
		if (lua_getmetatable (L, idx)) {
			lua_rawgeti(L, -1, 1);
			if (lua_type(L, -1) == LUA_TNUMBER) {
				type_id = (int)lua_tointeger(L, -1);
			}
			lua_pop(L, 2);
		}
	}
	return type_id;
}

/* Every struct access rejects offset < 0 first: offset + sizeof(x) is computed
 * as size_t, so offsets from -sizeof(x) to -1 used to wrap to a small value,
 * pass the length check and overwrite the fake_id/len header. */
#define PACK_UNPACK_OF(type) \
LUALIB_API int xlua_pack_##type(void *p, int offset, type field) {\
	CSharpStruct *css = (CSharpStruct *)p;\
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(field)) {\
		return 0;\
	} else {\
		memcpy((&(css->data[0]) + offset), &field, sizeof(field));\
		return 1;\
	}\
}\
\
LUALIB_API int xlua_unpack_##type(void *p, int offset, type *pfield) { /* NOLINT(bugprone-macro-parentheses): type is a type name */ \
	CSharpStruct *css = (CSharpStruct *)p;\
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(*pfield)) {\
		return 0;\
	} else {\
		memcpy(pfield, (&(css->data[0]) + offset), sizeof(*pfield));\
		return 1;\
	}\
}\

PACK_UNPACK_OF(int8_t)
PACK_UNPACK_OF(int16_t)
PACK_UNPACK_OF(int32_t)
PACK_UNPACK_OF(int64_t)
PACK_UNPACK_OF(float)
PACK_UNPACK_OF(double)

/* The float and decimal helpers copy with memcpy: PackUnpack.tpl.txt lays
 * fields out by cumulative size without padding, so a float or decimal often
 * sits at an unaligned offset (for example after a byte field). Accessing it
 * through float * or DECIMAL * was undefined behaviour and can fault on
 * 32-bit ARM. */
LUALIB_API int xlua_pack_float2(void *p, int offset, float f1, float f2) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 2) {
		return 0;
	} else {
		float pos[2];
		pos[0] = f1;
		pos[1] = f2;
		memcpy(&(css->data[0]) + offset, pos, sizeof(pos));
		return 1;
	}
}

LUALIB_API int xlua_unpack_float2(void *p, int offset, float *f1, float *f2) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 2) {
		return 0;
	} else {
		float pos[2];
		memcpy(pos, &(css->data[0]) + offset, sizeof(pos));
		*f1 = pos[0];
		*f2 = pos[1];
		return 1;
	}
}

LUALIB_API int xlua_pack_float3(void *p, int offset, float f1, float f2, float f3) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 3) {
		return 0;
	} else {
		float pos[3];
		pos[0] = f1;
		pos[1] = f2;
		pos[2] = f3;
		memcpy(&(css->data[0]) + offset, pos, sizeof(pos));
		return 1;
	}
}

LUALIB_API int xlua_unpack_float3(void *p, int offset, float *f1, float *f2, float *f3) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 3) {
		return 0;
	} else {
		float pos[3];
		memcpy(pos, &(css->data[0]) + offset, sizeof(pos));
		*f1 = pos[0];
		*f2 = pos[1];
		*f3 = pos[2];
		return 1;
	}
}

LUALIB_API int xlua_pack_float4(void *p, int offset, float f1, float f2, float f3, float f4) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 4) {
		return 0;
	} else {
		float pos[4];
		pos[0] = f1;
		pos[1] = f2;
		pos[2] = f3;
		pos[3] = f4;
		memcpy(&(css->data[0]) + offset, pos, sizeof(pos));
		return 1;
	}
}

LUALIB_API int xlua_unpack_float4(void *p, int offset, float *f1, float *f2, float *f3, float *f4) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 4) {
		return 0;
	} else {
		float pos[4];
		memcpy(pos, &(css->data[0]) + offset, sizeof(pos));
		*f1 = pos[0];
		*f2 = pos[1];
		*f3 = pos[2];
		*f4 = pos[3];
		return 1;
	}
}

LUALIB_API int xlua_pack_float5(void *p, int offset, float f1, float f2, float f3, float f4, float f5) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 5) {
		return 0;
	} else {
		float pos[5];
		pos[0] = f1;
		pos[1] = f2;
		pos[2] = f3;
		pos[3] = f4;
		pos[4] = f5;
		memcpy(&(css->data[0]) + offset, pos, sizeof(pos));
		return 1;
	}
}

LUALIB_API int xlua_unpack_float5(void *p, int offset, float *f1, float *f2, float *f3, float *f4, float *f5) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 5) {
		return 0;
	} else {
		float pos[5];
		memcpy(pos, &(css->data[0]) + offset, sizeof(pos));
		*f1 = pos[0];
		*f2 = pos[1];
		*f3 = pos[2];
		*f4 = pos[3];
		*f5 = pos[4];
		return 1;
	}
}

LUALIB_API int xlua_pack_float6(void *p, int offset, float f1, float f2, float f3, float f4, float f5, float f6) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 6) {
		return 0;
	} else {
		float pos[6];
		pos[0] = f1;
		pos[1] = f2;
		pos[2] = f3;
		pos[3] = f4;
		pos[4] = f5;
		pos[5] = f6;
		memcpy(&(css->data[0]) + offset, pos, sizeof(pos));
		return 1;
	}
}

LUALIB_API int xlua_unpack_float6(void *p, int offset, float *f1, float *f2, float *f3, float *f4, float *f5, float *f6) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(float) * 6) {
		return 0;
	} else {
		float pos[6];
		memcpy(pos, &(css->data[0]) + offset, sizeof(pos));
		*f1 = pos[0];
		*f2 = pos[1];
		*f3 = pos[2];
		*f4 = pos[3];
		*f5 = pos[4];
		*f6 = pos[5];
		return 1;
	}
}

LUALIB_API int xlua_pack_decimal(void *p, int offset, const int * decimal) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(int) * 4) {
		return 0;
	} else {
		memcpy(&(css->data[0]) + offset, decimal, sizeof(int) * 4);
		return 1;
	}
}

typedef struct tagDEC {
    uint16_t    wReserved;
    uint8_t     scale;
    uint8_t     sign;
    int         Hi32;
    uint64_t    Lo64;
} DECIMAL;

LUALIB_API int xlua_unpack_decimal(void *p, int offset, uint8_t *scale, uint8_t *sign, int *hi32, uint64_t *lo64) {
	CSharpStruct *css = (CSharpStruct *)p;
	if (offset < 0 || css->fake_id != -1 || css->len < offset + sizeof(int) * 4) {
		return 0;
	} else {
		DECIMAL dec;
		memcpy(&dec, &(css->data[0]) + offset, sizeof(dec));
		*scale = dec.scale;
		*sign = dec.sign;
		*hi32 = dec.Hi32;
		*lo64 = dec.Lo64;
		return 1;
	}
}

LUA_API int xlua_is_eq_str(lua_State *L, int idx, const char* str, int str_len) {
	size_t lmsg;
    const char *msg;
	if (lua_type(L, idx) == LUA_TSTRING) {
        msg = lua_tolstring(L, idx, &lmsg);
		return str_len >= 0 && lmsg == (size_t)str_len && memcmp(msg, str, lmsg) == 0;
	} else {
		return 0;
	}
}

#define T_INT8   0
#define T_UINT8  1
#define T_INT16  2
#define T_UINT16 3
#define T_INT32  4
#define T_UINT32 5
#define T_INT64  6
#define T_UINT64 7
#define T_FLOAT  8
#define T_DOUBLE 9

/* A struct field setter takes a number or a string Lua converts to one
 * (lua_isnumber); anything else raises "number expected, got <type>". Upstream
 * converted any value, so nil, a boolean, a table or a non-numeric string
 * stored 0. Numbers keep their conversion rules (see the setters below). */
static void check_field_number(lua_State *L) {
	if (!lua_isnumber(L, 2)) {
		luaL_checknumber(L, 2); /* raises */
	}
}

#if LUA_VERSION_NUM == 501
/* LuaJIT: a 64-bit field also takes the int64/uint64 userdata of i64lib. */
static void check_field_int64(lua_State *L) {
	if (!lua_isint64_or_uint64(L, 2)) {
		check_field_number(L);
	}
}
#else
#define check_field_int64 check_field_number
#endif

/* The field an accessor reads or writes: the data of the struct userdata at
 * argument 1 at the offset in upvalue 1, or NULL when argument 1 is not a
 * struct (header fake_id -1), the offset is negative or the field does not
 * fit in len, or, for the checked accessors (the C# bridge checks),
 * the metatable lacks the bridge tag. One function for all accessors: the
 * checks inlined into each cost about 2.8 KB of code. */
static char *accessor_field(lua_State *L, size_t width, int checked) {
	CSharpStruct *css = (CSharpStruct *)lua_touserdata(L, 1);
	int offset = xlua_tointeger(L, lua_upvalueindex(1));
	if (css == NULL || lua_type(L, 1) != LUA_TUSERDATA || offset < 0 || css->fake_id != -1 || css->len < offset + width
			|| (checked && !is_cs_data(L, 1))) {
		return NULL;
	}
	return &(css->data[0]) + offset;
}

/* Each field type gets a default and a checked getter and setter, thin
 * wrappers around one body, so the default accessors read no flag. */
#define DIRECT_ACCESS(type, push_func, to_func, check_func) \
static XLUA_NOINLINE int struct_get_##type(lua_State *L, int checked) {\
	char *field = accessor_field(L, sizeof(type), checked);\
	type val;\
	if (field == NULL) {\
		return luaL_error(L, "invalid c# struct!");\
	}\
	memcpy(&val, field, sizeof(type));\
	push_func(L, val);\
	return 1;\
}\
\
static XLUA_NOINLINE int struct_set_##type(lua_State *L, int checked) {\
	char *field = accessor_field(L, sizeof(type), checked);\
	type val;\
	if (field == NULL) {\
		return luaL_error(L, "invalid c# struct!");\
	}\
	check_func(L);\
	val = (type)to_func(L, 2);\
	memcpy(field, &val, sizeof(type));\
	return 0;\
}\
\
int xlua_struct_get_##type(lua_State *L) { return struct_get_##type(L, 0); }\
int xlua_struct_set_##type(lua_State *L) { return struct_set_##type(L, 0); }\
static int struct_get_checked_##type(lua_State *L) { return struct_get_##type(L, 1); }\
static int struct_set_checked_##type(lua_State *L) { return struct_set_##type(L, 1); }\

/* An integer field's value by Lua 5.3's rule (value_to_integer) on every
 * runtime: a number or numeric string without an integer representation
 * raises luaL_checkinteger's "number has no integer representation". Upstream
 * stored 0 for it on Lua 5.3+ and the truncated value on LuaJIT, and uint32
 * truncated and wrapped everywhere. check_field_number has already rejected
 * other values. */
static int64_t field_integer(lua_State *L, int idx) {
	int64_t v;
	if (!value_to_integer(L, idx, &v)) {
		luaL_argerror(L, idx, "number has no integer representation");
	}
	return v;
}

/* A float field widens to lua_Number explicitly (-Wdouble-promotion). */
static void push_float(lua_State *L, float v) {
	lua_pushnumber(L, (lua_Number)v);
}

/* Integer fields wrap an integral value to their width like a C cast (uint32:
 * modulo 2^32; a uint64 keeps the 64 bits). */
DIRECT_ACCESS(int8_t, xlua_pushinteger, field_integer, check_field_number)
DIRECT_ACCESS(uint8_t, xlua_pushinteger, field_integer, check_field_number)
DIRECT_ACCESS(int16_t, xlua_pushinteger, field_integer, check_field_number)
DIRECT_ACCESS(uint16_t, xlua_pushinteger, field_integer, check_field_number)
DIRECT_ACCESS(int32_t, xlua_pushinteger, field_integer, check_field_number)
DIRECT_ACCESS(uint32_t, xlua_pushuint, field_integer, check_field_number)
DIRECT_ACCESS(int64_t, lua_pushint64, field_integer, check_field_int64)
DIRECT_ACCESS(uint64_t, lua_pushuint64, field_integer, check_field_int64)
DIRECT_ACCESS(float, push_float, lua_tonumber, check_field_number)
DIRECT_ACCESS(double, lua_pushnumber, lua_tonumber, check_field_number)

static const lua_CFunction direct_getters[10] = {
	xlua_struct_get_int8_t,
	xlua_struct_get_uint8_t,
	xlua_struct_get_int16_t,
	xlua_struct_get_uint16_t,
	xlua_struct_get_int32_t,
	xlua_struct_get_uint32_t,
	xlua_struct_get_int64_t,
	xlua_struct_get_uint64_t,
	xlua_struct_get_float,
	xlua_struct_get_double
};

static const lua_CFunction direct_setters[10] = {
	xlua_struct_set_int8_t,
	xlua_struct_set_uint8_t,
	xlua_struct_set_int16_t,
	xlua_struct_set_uint16_t,
	xlua_struct_set_int32_t,
	xlua_struct_set_uint32_t,
	xlua_struct_set_int64_t,
	xlua_struct_set_uint64_t,
	xlua_struct_set_float,
	xlua_struct_set_double
};

static const lua_CFunction checked_getters[10] = {
	struct_get_checked_int8_t,
	struct_get_checked_uint8_t,
	struct_get_checked_int16_t,
	struct_get_checked_uint16_t,
	struct_get_checked_int32_t,
	struct_get_checked_uint32_t,
	struct_get_checked_int64_t,
	struct_get_checked_uint64_t,
	struct_get_checked_float,
	struct_get_checked_double
};

static const lua_CFunction checked_setters[10] = {
	struct_set_checked_int8_t,
	struct_set_checked_uint8_t,
	struct_set_checked_int16_t,
	struct_set_checked_uint16_t,
	struct_set_checked_int32_t,
	struct_set_checked_uint32_t,
	struct_set_checked_int64_t,
	struct_set_checked_uint64_t,
	struct_set_checked_float,
	struct_set_checked_double
};

int nop(lua_State *L) {
	(void)L;
	return 0;
}

/* Upvalue 1 of the accessors is the offset. The checked accessors also
 * require the bridge tag (is_cs_data), as css_clone does; the default ones
 * trust the struct header, which any full userdata can carry. gen_css_access
 * hands out the checked ones when its own upvalue 1 is true: none (false) for
 * the xlua.genaccessor that luaopen_xlua registers, true after
 * xlua_check_struct_accessors. */
LUA_API int gen_css_access(lua_State *L) {
	int offset = xlua_tointeger(L, 1);
	int type = xlua_tointeger(L, 2);
	int checked = lua_toboolean(L, lua_upvalueindex(1));
	if (offset < 0) {
		return luaL_error(L, "offset must larger than 0");
	}
	if (type < T_INT8 || type > T_DOUBLE) {
		return luaL_error(L, "unknow tag[%d]", type);
	}
	lua_pushvalue(L, 1);
	lua_pushcclosure(L, (checked ? checked_getters : direct_getters)[type], 1);
	lua_pushvalue(L, 1);
	lua_pushcclosure(L, (checked ? checked_setters : direct_setters)[type], 1);
	lua_pushcclosure(L, nop, 0);
	return 3;
}

/* The C# bridge checks (XLUA_BRIDGE_CHECKS) call this right after
 * luaopen_xlua: it replaces xlua.genaccessor with one whose accessors require
 * the bridge tag. Returns 0, and changes nothing, if the global xlua is not a
 * table. */
LUA_API int xlua_check_struct_accessors(lua_State *L) {
	lua_getglobal(L, "xlua");
	if (lua_type(L, -1) != LUA_TTABLE) {
		lua_pop(L, 1);
		return 0;
	}
	lua_pushboolean(L, 1);
	lua_pushcclosure(L, gen_css_access, 1);
	lua_setfield(L, -2, "genaccessor");
	lua_pop(L, 1);
	return 1;
}

LUA_API int css_clone(lua_State *L) {
	CSharpStruct *from = (CSharpStruct *)lua_touserdata(L, 1);
	CSharpStruct *to = NULL;
	if (!is_cs_data(L, 1) || from->fake_id != -1) {
		return luaL_error(L, "invalid c# struct!");
	}
	
	to = (CSharpStruct *)new_userdata(L, from->len + sizeof(int) + sizeof(unsigned int));
	to->fake_id = -1;
	to->len = from->len;
	memcpy(&(to->data[0]), &(from->data[0]), from->len);
    lua_getmetatable(L, 1);
	lua_setmetatable(L, -2);
	return 1;
}

/* An identifier of L's global state, the same for all its threads; the C#
 * side finds an env's translator by it (ObjectTranslatorPool). */
LUA_API void* xlua_gl(lua_State *L) {
	/* One load. On LuaJIT this needs the library in the mode lj_obj.h was
	 * compiled for: the #error above and CMake's library check keep both GC64. */
	return G(L); // NOLINT(bugprone-casting-through-void,performance-no-int-to-ptr): LuaJIT's G() decodes its own pointer encoding
}

static const luaL_Reg xlualib[] = {
	{"sethook", profiler_set_hook},
	{"genaccessor", gen_css_access},
	{"structclone", css_clone},
	{NULL, NULL}
};

/*
 * C modules compiled into this library put their loaders into package.preload
 * here, so the managed code imports nothing for them and is the same whether a
 * module is built or not. CMake defines XLUA_LUASOCKET and XLUA_MIME for the
 * sources it compiles (options of the same name); without them, as in the WebGL
 * build, nothing is registered. luaopen_xlua calls this, so a static library
 * (iOS) needs no constructor and the linker pulls the module in through this
 * reference. Same loader under both names, as upstream's C# registration had.
 */
#if defined(XLUA_LUASOCKET) || defined(XLUA_MIME)
/* LUASOCKET_API from build/luasocket/luasocket.h, so the declarations match the definitions. */
#ifdef _WIN32
#define XLUA_MODULE_API __declspec(dllexport)
#else
#define XLUA_MODULE_API __attribute__ ((visibility ("default")))
#endif
#ifdef XLUA_LUASOCKET
XLUA_MODULE_API int luaopen_socket_core(lua_State *L);
#endif
#ifdef XLUA_MIME
XLUA_MODULE_API int luaopen_mime_core(lua_State *L);
#endif

static const luaL_Reg xlua_preloaded_modules[] = {
#ifdef XLUA_LUASOCKET
	{"socket.core", luaopen_socket_core},
	{"socket", luaopen_socket_core},
#endif
#ifdef XLUA_MIME
	{"mime.core", luaopen_mime_core},
	{"mime", luaopen_mime_core},
#endif
	{NULL, NULL}
};

static void preload_native_modules(lua_State *L) {
	const luaL_Reg *module;
	/* registry._PRELOAD is package.preload on every supported runtime. */
#if LUA_VERSION_NUM >= 503
	luaL_getsubtable(L, LUA_REGISTRYINDEX, LUA_PRELOAD_TABLE);
#else
	luaL_findtable(L, LUA_REGISTRYINDEX, "_PRELOAD", 4);
#endif
	for (module = xlua_preloaded_modules; module->name != NULL; module++) {
		lua_pushcfunction(L, module->func);
		lua_setfield(L, -2, module->name);
	}
	lua_pop(L, 1);
}
#endif

/*
 * The built-in chunks of a bytecode-only build (CMake XLUA_BYTECODE_ONLY,
 * build/README.md, "Bytecode-only builds"). LuaEnv runs Lua source of its own
 * (init_xlua, xlua.util ...), which this library cannot compile. The build
 * compiles those C# strings with build/xlua_chunkc.c into
 * xlua_builtin_chunks.h, and luaopen_xlua puts a loader for them into the
 * registry, where LuaEnv.LoadBuiltinChunk looks for it. Without the option
 * nothing is registered and C# loads the text, so the export list is the same
 * in every build.
 */
#if defined(XLUA_BYTECODE_ONLY)
typedef struct xlua_builtin_chunk {
	const char *name;       /* LuaEnv's name for it */
	const char *chunkname;  /* the chunk name C# loads the text with */
	uint32_t hash;          /* LuaEnv.BuiltinChunkHash of the text */
	const unsigned char *data;
	size_t size;
} xlua_builtin_chunk;

#include "xlua_builtin_chunks.h"

/* The bytecode loads only in a library whose format matches the one
 * xlua_chunkc wrote (lundump.c's checkHeader); a host compiler of another
 * format fails here rather than at run time. Lua 5.3's format also has
 * sizeof(size_t), unless LUAC_COMPATIBLE_FORMAT leaves it out. */
#if XLUA_BUILTIN_CHUNKS_VERSION_NUM != LUA_VERSION_NUM
#error "xlua_builtin_chunks.h was compiled for another Lua version"
#endif
_Static_assert(sizeof(int) == XLUA_BUILTIN_CHUNKS_SIZEOF_INT, "xlua_builtin_chunks.h: another sizeof(int)");
_Static_assert(sizeof(lua_Integer) == XLUA_BUILTIN_CHUNKS_SIZEOF_INTEGER, "xlua_builtin_chunks.h: another sizeof(lua_Integer)");
_Static_assert(sizeof(lua_Number) == XLUA_BUILTIN_CHUNKS_SIZEOF_NUMBER, "xlua_builtin_chunks.h: another sizeof(lua_Number)");
#if LUA_VERSION_NUM == 503
#if defined(LUAC_COMPATIBLE_FORMAT) && LUAC_COMPATIBLE_FORMAT
#if !XLUA_BUILTIN_CHUNKS_COMPATIBLE_FORMAT
#error "xlua_builtin_chunks.h was compiled without LUAC_COMPATIBLE_FORMAT"
#endif
#else
#if XLUA_BUILTIN_CHUNKS_COMPATIBLE_FORMAT
#error "xlua_builtin_chunks.h was compiled with LUAC_COMPATIBLE_FORMAT"
#endif
_Static_assert(sizeof(size_t) == XLUA_BUILTIN_CHUNKS_SIZEOF_SIZE_T,
               "xlua_builtin_chunks.h: another sizeof(size_t); Lua 5.3 bytecode for a 32-bit target needs LUAC_COMPATIBLE_FORMAT");
#endif
#endif

#define XLUA_BUILTIN_CHUNK_LOADER "xlua.builtin_chunk"

/* registry["xlua.builtin_chunk"](name, hash) -> the loaded chunk, or nil and
 * a message: for a chunk this library does not have, or one compiled from
 * other C# text than the caller's (another hash). */
static int builtin_chunk(lua_State *L) {
	const char *name = luaL_checkstring(L, 1);
	uint32_t hash = (uint32_t)luaL_checkinteger(L, 2);
	const xlua_builtin_chunk *c;
	for (c = xlua_builtin_chunks; c->name != NULL; c++) {
		if (strcmp(c->name, name) != 0) {
			continue;
		}
		if (c->hash != hash) {
			lua_pushnil(L);
			lua_pushfstring(L, "the precompiled chunk '%s' of this bytecode-only library was built from other C# sources "
			                   "than the ones running; build the native library again from the same checkout", name);
			return 2;
		}
		if (luaL_loadbufferx(L, (const char *)c->data, c->size, c->chunkname, "b") != LUA_OK) {
			lua_pushnil(L);
			lua_insert(L, -2);
			return 2;
		}
		return 1;
	}
	lua_pushnil(L);
	lua_pushfstring(L, "this bytecode-only library has no precompiled chunk '%s' (build/README.md, \"Bytecode-only builds\": "
	                   "XLUA_BYTECODE_UTIL, XLUA_BYTECODE_PROFILER, XLUA_BYTECODE_MEMORY)", name);
	return 2;
}
#endif

LUA_API void luaopen_xlua(lua_State *L) {
	luaL_openlibs(L);
	xlua_core_open_coroutines(L);  /* core states: the budget follows coroutine switches */
#if defined(XLUA_LUASOCKET) || defined(XLUA_MIME)
	preload_native_modules(L);
#endif
#if defined(XLUA_BYTECODE_ONLY)
	lua_pushcfunction(L, builtin_chunk);
	lua_setfield(L, LUA_REGISTRYINDEX, XLUA_BUILTIN_CHUNK_LOADER);
#endif

#if LUA_VERSION_NUM >= 503
	luaL_newlib(L, xlualib);
	lua_setglobal(L, "xlua");
#else
	luaL_register(L, "xlua", xlualib);
    lua_pop(L, 1);
#endif
}

