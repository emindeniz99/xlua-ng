/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* The native layer that every preset builds (docs/design/core-luascript.md,
 * section 3.1). xlua.c, the C# bridge of the legacy and minimal presets, is
 * built on it. The code below moved here unchanged from xlua.c: the GC
 * options, the C# function trampolines with their deferred error and the
 * error handler; push_helper is in xlua_core.h. */

#define LUA_LIB

/* XLUA_AMALGAMATION: the Unity WebGL build compiles all sources as one translation
   unit and has no include paths, so it includes the Lua headers before this file. */
#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#endif

#include "xlua_core.h"

/* Keep the managed GC operation numbers and KB step units across runtimes. */
LUA_API int xlua_gc(lua_State *L, int what, int data) {
#if LUA_VERSION_NUM >= 505
    switch (what) {
        case 5: /* managed LUA_GCSTEP: Lua 5.5 takes size_t bytes */
            return lua_gc(L, LUA_GCSTEP, (size_t)data * 1024u);
        case 6: /* managed LUA_GCSETPAUSE */
            return lua_gc(L, LUA_GCPARAM, LUA_GCPPAUSE, data);
        case 7: /* managed LUA_GCSETSTEPMUL */
            return lua_gc(L, LUA_GCPARAM, LUA_GCPSTEPMUL, data);
        case 9: /* Lua 5.3/5.4 LUA_GCISRUNNING */
            return lua_gc(L, LUA_GCISRUNNING);
        default:
            return (what >= 0 && what <= 4) ? lua_gc(L, what) : -1;
    }
#else
    return lua_gc(L, what, data);
#endif
}

/* The error handler of the C# side's protected calls (LuaEnv.DoString,
 * LuaFunction.Call): the message with the global debug.traceback. */
LUA_API int errorfunc(lua_State *L) {
	lua_getglobal(L, "debug");
	lua_getfield(L, -1, "traceback");
	lua_remove(L, -2);
	lua_pushvalue(L, 1);
	lua_pushnumber(L, 2);
	lua_call(L, 2, 1);
    return 1;
}

LUA_API int get_error_func_ref(lua_State *L) {
	lua_pushcclosure(L, errorfunc, 0);
	return luaL_ref(L, LUA_REGISTRYINDEX);
}

/* C# functions (lua_CSFunction) are called through a trampoline closure. C#
 * cannot raise a Lua error: a longjmp through managed frames is undefined
 * behaviour. It records the error instead (xlua_csharp_str_error,
 * xlua_csharp_error set upvalue 2 to true and push the message) and returns;
 * the trampoline raises the error once the managed frames are gone. */
static int csharp_function_wrap(lua_State *L) {
	lua_CFunction fn = (lua_CFunction)lua_tocfunction(L, lua_upvalueindex(1));
	int ret;
	/* debug.setupvalue can replace the C function with any value. */
	if (fn == NULL) {
		return luaL_error(L, "invalid c# function: upvalue 1 is not a C function");
	}
    ret = fn(L);

    if (lua_toboolean(L, lua_upvalueindex(2)))
    {
        lua_pushboolean(L, 0);
        lua_replace(L, lua_upvalueindex(2));
        return lua_error(L);
    }

	if (lua_gethook(L)) {
		xlua_profiler_return(L);
	}

    return ret;
}

LUA_API void xlua_push_csharp_function(lua_State* L, lua_CFunction fn, int n)
{
    lua_pushcfunction(L, fn);
	if (n > 0) {
		lua_insert(L, -1 - n);
	}
	lua_pushboolean(L, 0);
	if (n > 0) {
		lua_insert(L, -1 - n);
	}
    lua_pushcclosure(L, csharp_function_wrap, 2 + (n > 0 ? n : 0));
}

/* GEN_CODE_MINIMIZE: one process-wide C# dispatcher for every generated
 * wrapper, called with the wrapper id kept in upvalue 1. */
typedef int (*lua_CSWrapperCaller) (lua_State *L, int wrapperid, int top);

static lua_CSWrapperCaller g_csharp_wrapper_caller = NULL;

LUA_API void xlua_set_csharp_wrapper_caller(lua_CSWrapperCaller wrapper_caller)
{
	g_csharp_wrapper_caller = wrapper_caller;
}

static int csharp_function_wrapper_wrapper(lua_State *L) {
    int ret = 0;

	if (g_csharp_wrapper_caller == NULL) {
		return luaL_error(L, "g_csharp_wrapper_caller not set");
	}

	ret = g_csharp_wrapper_caller(L, (int)lua_tointeger(L, lua_upvalueindex(1)), lua_gettop(L));

    if (lua_toboolean(L, lua_upvalueindex(2)))
    {
        lua_pushboolean(L, 0);
        lua_replace(L, lua_upvalueindex(2));
        return lua_error(L);
    }

	if (lua_gethook(L)) {
		xlua_profiler_return(L);
	}

    return ret;
}

LUA_API void xlua_push_csharp_wrapper(lua_State* L, int wrapperid)
{
	lua_pushinteger(L, wrapperid);
	lua_pushboolean(L, 0);
    lua_pushcclosure(L, csharp_function_wrapper_wrapper, 2);
}

/* The C# function's own upvalues start after the trampoline's two. */
LUALIB_API int xlua_upvalueindex(int n) {
	return lua_upvalueindex(2 + n);
}

LUALIB_API int xlua_csharp_str_error(lua_State* L, const char* msg)
{
    lua_pushboolean(L, 1);
    lua_replace(L, lua_upvalueindex(2));
    lua_pushstring(L, msg);
    return 1;
}

LUALIB_API int xlua_csharp_error(lua_State* L)
{
    lua_pushboolean(L, 1);
    lua_replace(L, lua_upvalueindex(2));
    return 1;
}
