/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* The native layer that every preset builds (docs/design/core-luascript.md,
 * section 3.1). xlua.c, the C# bridge of the legacy and minimal presets, is
 * built on it. From xlua.c came the GC options, the C# function trampolines
 * with their deferred error and the error handler (push_helper is in
 * xlua_core.h). New here: states with a memory limit and a hash seed
 * (LuaOptions, milestone 2 of the design); the instruction and time limits
 * are in xlua_core_limits.c. */

#define LUA_LIB

/* XLUA_AMALGAMATION: the Unity WebGL build compiles all sources as one translation
   unit and has no include paths, so it includes the Lua headers before this file. */
#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#endif

#include <stdlib.h>
#include <string.h>
#include <time.h>

#if !defined(USING_LUAJIT) && !defined(XLUA_AMALGAMATION)
#include "lstate.h"
#include "ldo.h"
#include "lstring.h"
#endif

#include "xlua_core.h"

LUA_API int xlua_core_version(void) {
	return XLUA_CORE_VERSION;
}

#if !defined(USING_LUAJIT)
/* The allocator of core states (core-luascript.md, section 3.4). Lua passes a
 * type tag, not a size, in osize when ptr is NULL. A growth over the limit is
 * refused, and Lua then runs an emergency collection and asks again; if that
 * fails too it raises "not enough memory", which pcall can catch. While C#
 * runs above the innermost protected entry, a refusal would raise through
 * managed frames, so the request is granted and remembered as an overdraft
 * instead; the C# function trampoline raises the memory error once C# has
 * returned. A shrink is passed to realloc as Lua's own allocator does
 * (luaL_newstate); a failure leaves the block and the count unchanged. */
void *xlua_core_alloc(void *ud, void *ptr, size_t osize, size_t nsize) {
	xlua_core_t *xc = (xlua_core_t *)ud;
	size_t old = ptr != NULL ? osize : 0;
	void *block;
	if (nsize == 0) {
		free(ptr);
		xc->used -= old;
		return NULL;
	}
	if (nsize > old && xc->limit != 0) {
		size_t budget = xc->limit + xc->reserve;
		if (xc->used > budget || nsize - old > budget - xc->used) {
			if (xc->unprotected == 0) {
				xc->refusals++;
				return NULL;
			}
			xc->overdraft = 1;
			xc->overdrafts++;
		}
	}
	block = realloc(ptr, nsize);
	if (block == NULL) {
		xc->host_oom++;
		return NULL;
	}
	xc->used = xc->used - old + nsize;
	if (xc->used > xc->peak) {
		xc->peak = xc->used;
	}
	return block;
}

/* A limit in bytes as the allocator keeps it: 0 (or less) is unlimited. A
 * 32-bit size_t cannot hold every int64_t; there a larger limit is capped
 * where limit plus the error reserve still fits, which no allocation can
 * reach anyway. */
static size_t to_limit(int64_t bytes) {
#if SIZE_MAX < INT64_MAX
	if (bytes > (int64_t)(SIZE_MAX - XLUA_CORE_ERROR_RESERVE)) {
		return SIZE_MAX - XLUA_CORE_ERROR_RESERVE;
	}
#endif
	return bytes > 0 ? (size_t)bytes : 0;
}

/* After an overdraft, once no managed frame is left above the innermost
 * protected entry (unprotected is back to 0): like Lua's own retry after a
 * refusal, a full collection first, then Lua's memory error if the state is
 * still over the limit. Without the collection, garbage left over the limit
 * by one C# call (a large result a script dropped) would fail every later C#
 * call that allocates, until the GC happened to run. lua_gc does nothing
 * when the collector is already running (a C# finalizer). */
static XLUA_NOINLINE void raise_overdraft(lua_State *L, xlua_core_t *xc) {
	xc->overdraft = 0;
	lua_gc(L, LUA_GCCOLLECT, 0);
	if (xc->limit != 0 && xc->used > xc->limit + xc->reserve) {
		luaD_throw(L, LUA_ERRMEM);
	}
}

static void check_overdraft(lua_State *L, xlua_core_t *xc) {
	if (xc->overdraft && xc->unprotected == 0) {
		raise_overdraft(L, xc);  /* rare: out of line, so the trampolines stay small */
	}
}
#endif

#if LUA_VERSION_NUM == 504 && !defined(USING_LUAJIT)
/* luai_makeseed for Lua 5.4 (build/xlua_luahooks.h, through the luaconf.h.in
 * hook): the configured seed of a core state, otherwise upstream's mix of the
 * time and a few addresses (lstate.c:58-80 in 5.4.9, copied with its
 * comments' intent). lua_newstate sets the allocator before it asks for the
 * seed (lstate.c:383-388), so the core state is known here. */
unsigned int xlua_core_makeseed(lua_State *L) {
	xlua_core_t *xc = xlua_core_of(L);
	char buff[3 * sizeof(size_t)];
	unsigned int h;
	size_t t;
	if (xc != NULL && xc->has_seed) {
		return xc->seed;
	}
	h = (unsigned int)time(NULL);
	t = (size_t)L;  /* heap variable */
	memcpy(buff, &t, sizeof(t));
	t = (size_t)&h;  /* local variable */
	memcpy(buff + sizeof(t), &t, sizeof(t));
	t = (size_t)&lua_newstate;  /* public function */
	memcpy(buff + 2 * sizeof(t), &t, sizeof(t));
	return luaS_hash(buff, sizeof(buff), h);
}
#endif

/* A new state with the core allocator and, where the Lua version allows,
 * the configured hash seed: Lua 5.5 takes it in lua_newstate, Lua 5.4 gets it
 * through luai_makeseed. Nothing is opened; the caller opens the libraries
 * (LuaEnv does what it does for luaL_newstate). The core struct itself is
 * allocated first, through the same allocator and limit, so a limit too
 * small for a state fails cleanly. Returns NULL and sets *status to
 * XLUA_ERRMEM, or to XLUA_ERRCONFIG when this build cannot honour an option:
 * LuaJIT has no core allocator, and Lua 5.3 (or a 5.4 build without the
 * luaconf.h hook) cannot take a seed. */
LUA_API lua_State *xlua_core_newstate(const xlua_core_config *cfg, int32_t *status) {
#if defined(USING_LUAJIT)
	lua_State *L;
	if (cfg->memory_limit != 0 || cfg->has_seed) {
		*status = XLUA_ERRCONFIG;
		return NULL;
	}
	L = luaL_newstate();
	*status = L != NULL ? XLUA_OK : XLUA_ERRMEM;
	return L;
#else
	xlua_core_t boot, *xc;
	lua_State *L;
#if LUA_VERSION_NUM < 504 || (LUA_VERSION_NUM == 504 && !defined(XLUA_LUA_HOOKS))
	if (cfg->has_seed) {
		*status = XLUA_ERRCONFIG;
		return NULL;
	}
#endif
	memset(&boot, 0, sizeof(boot));
	boot.limit = to_limit(cfg->memory_limit);
	boot.seed = cfg->seed;
	boot.has_seed = cfg->has_seed != 0;
	xc = (xlua_core_t *)xlua_core_alloc(&boot, NULL, 0, sizeof(xlua_core_t));
	if (xc == NULL) {
		*status = XLUA_ERRMEM;
		return NULL;
	}
	*xc = boot;
#if LUA_VERSION_NUM >= 505
	L = lua_newstate(xlua_core_alloc, xc, xc->has_seed ? xc->seed : luaL_makeseed(NULL));
#else
	L = lua_newstate(xlua_core_alloc, xc);
#endif
	if (L == NULL) {
		free(xc);
		*status = XLUA_ERRMEM;
		return NULL;
	}
	*status = XLUA_OK;
	return L;
#endif
}

/* Closes any state; for a core state also frees the core struct, after
 * lua_close has returned every block through the allocator. */
LUA_API void xlua_core_close(lua_State *L) {
	xlua_core_t *xc = xlua_core_of(L);
	lua_close(L);
	free(xc);
}

/* Returns 0 for a state without the core allocator. A limit below the
 * current use makes the next growth fail. */
LUA_API int xlua_core_set_memory_limit(lua_State *L, int64_t bytes) {
	xlua_core_t *xc = xlua_core_of(L);
	if (xc == NULL) {
		return 0;
	}
#if !defined(USING_LUAJIT)
	xc->limit = to_limit(bytes);
#else
	(void)bytes;
#endif
	return 1;
}

/* Fills *out and returns 1 for a core state; returns 0 and zeroes *out for
 * any other state. */
LUA_API int xlua_core_get_stats(lua_State *L, xlua_core_stats *out) {
	xlua_core_t *xc = xlua_core_of(L);
	memset(out, 0, sizeof(*out));
	if (xc == NULL) {
		return 0;
	}
	out->memory_used = (int64_t)xc->used;
	out->memory_peak = (int64_t)xc->peak;
	out->memory_limit = (int64_t)xc->limit;
	out->refusals = xc->refusals;
	out->overdrafts = xc->overdrafts;
	out->host_oom = xc->host_oom;
	out->instructions = xc->last_instructions;
	out->stop_reason = xc->depth > 0 && xc->entry_active ? xc->stop : xc->last_stop;
	out->instruction_limit = xc->instruction_limit;
	out->time_limit_ns = xc->time_limit_ns;
	return 1;
}

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
 * LuaFunction.Call): the message with the global debug.traceback. In a core
 * state the limit is raised by XLUA_CORE_ERROR_RESERVE meanwhile, so an error
 * near the limit still gets its traceback; if the traceback itself fails, the
 * protected entry restores the reserve. */
LUA_API int errorfunc(lua_State *L) {
	xlua_core_t *xc = xlua_core_of(L);
	size_t reserve = 0;
	if (xc != NULL) {
		reserve = xc->reserve;
		xc->reserve = XLUA_CORE_ERROR_RESERVE;
	}
	lua_getglobal(L, "debug");
	lua_getfield(L, -1, "traceback");
	lua_remove(L, -2);
	lua_pushvalue(L, 1);
	lua_pushnumber(L, 2);
	lua_call(L, 2, 1);
	if (xc != NULL) {
		xc->reserve = reserve;
	}
    return 1;
}

LUA_API int get_error_func_ref(lua_State *L) {
	lua_pushcclosure(L, errorfunc, 0);
	return luaL_ref(L, LUA_REGISTRYINDEX);
}

/* Around the call of a C# function: in a core state, a managed frame is now
 * on the C stack, so the allocator must not refuse (see xlua_core_alloc). */
static xlua_core_t *managed_enter(lua_State *L) {
	xlua_core_t *xc = xlua_core_of(L);
#if !defined(USING_LUAJIT)
	if (xc != NULL) {
		xc->unprotected++;
	}
#endif
	return xc;
}

/* After the C# function has returned: raises the memory error of an
 * overdraft, unless more managed frames are still on the stack. */
static void managed_leave(lua_State *L, xlua_core_t *xc) {
#if !defined(USING_LUAJIT)
	if (xc != NULL) {
		xc->unprotected--;
		check_overdraft(L, xc);
	}
#else
	(void)L;
	(void)xc;
#endif
}

/* C# functions (lua_CSFunction) are called through a trampoline closure. C#
 * cannot raise a Lua error: a longjmp through managed frames is undefined
 * behaviour. It records the error instead (xlua_csharp_str_error,
 * xlua_csharp_error set upvalue 2 to true and push the message) and returns;
 * the trampoline raises the error once the managed frames are gone. A memory
 * error from an overdraft (xlua_core_alloc) wins over the recorded error;
 * the flag is reset first either way. */
static int csharp_function_wrap(lua_State *L) {
	lua_CFunction fn = (lua_CFunction)lua_tocfunction(L, lua_upvalueindex(1));
	xlua_core_t *xc;
	int ret;
	/* debug.setupvalue can replace the C function with any value. */
	if (fn == NULL) {
		return luaL_error(L, "invalid c# function: upvalue 1 is not a C function");
	}
	xc = managed_enter(L);
    ret = fn(L);

    if (lua_toboolean(L, lua_upvalueindex(2)))
    {
        lua_pushboolean(L, 0);
        lua_replace(L, lua_upvalueindex(2));
        managed_leave(L, xc);
        return lua_error(L);
    }
    managed_leave(L, xc);

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
	xlua_core_t *xc;

	if (g_csharp_wrapper_caller == NULL) {
		return luaL_error(L, "g_csharp_wrapper_caller not set");
	}

	xc = managed_enter(L);
	ret = g_csharp_wrapper_caller(L, (int)lua_tointeger(L, lua_upvalueindex(1)), lua_gettop(L));

    if (lua_toboolean(L, lua_upvalueindex(2)))
    {
        lua_pushboolean(L, 0);
        lua_replace(L, lua_upvalueindex(2));
        managed_leave(L, xc);
        return lua_error(L);
    }
    managed_leave(L, xc);

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
