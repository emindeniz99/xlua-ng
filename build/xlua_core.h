/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* The internal interface between xlua_core.c, the native layer every preset
 * builds, and xlua.c, the C# bridge that the legacy and minimal presets build
 * on top of it (docs/design/core-luascript.md, section 3.1). This is not a
 * public header and nothing declared here is exported from the library.
 * Include it after the Lua headers. */
#ifndef XLUA_CORE_H
#define XLUA_CORE_H

#include <stdint.h>

/* A function that several of xLua's own C files share but the library does
 * not export, like Lua's LUAI_FUNC: hidden visibility on ELF and Mach-O, a
 * plain declaration elsewhere (Windows DLLs export only what LUA_API marks). */
#if defined(__GNUC__) && (defined(__ELF__) || defined(__APPLE__))
#define XLUA_INTERNAL __attribute__((visibility("hidden"))) extern
#else
#define XLUA_INTERNAL extern
#endif

/* Keeps a rarely taken path out of its callers. */
#if defined(__GNUC__)
#define XLUA_NOINLINE __attribute__((noinline))
#elif defined(_MSC_VER)
#define XLUA_NOINLINE __declspec(noinline)
#else
#define XLUA_NOINLINE
#endif

/* Pushes one of the static helpers of a protected call (xlua_pgettable and
 * the other protected exports). Lua 5.2+ pushes a light C function, which
 * allocates nothing. Lua 5.1 (LuaJIT) creates a new C closure on every
 * lua_pushcfunction, so there each helper is created once per state and kept
 * in the registry under its own address. ISO C has no conversion from a
 * function pointer to void *; through uintptr_t it is implementation-defined
 * and keeps the address on every supported target, which is all a unique key
 * needs (-Wpedantic). An inline function, so that xlua.c and xlua_core.c
 * compile it into each caller as before the split. */
static inline void push_helper(lua_State *L, lua_CFunction fn) {
#if LUA_VERSION_NUM == 501
	lua_pushlightuserdata(L, (void *)(uintptr_t)fn); // NOLINT(performance-no-int-to-ptr): a registry key, never dereferenced
	lua_rawget(L, LUA_REGISTRYINDEX);
	if (lua_isfunction(L, -1)) return;
	lua_pop(L, 1);
	lua_pushcfunction(L, fn);
	lua_pushlightuserdata(L, (void *)(uintptr_t)fn); // NOLINT(performance-no-int-to-ptr): a registry key, never dereferenced
	lua_pushvalue(L, -2);
	lua_rawset(L, LUA_REGISTRYINDEX);
#else
	lua_pushcfunction(L, fn);
#endif
}

/* Defined in xlua.c: reports the return of a C# function to the xlua.sethook
 * profiler. The C# function trampolines in xlua_core.c call it when a hook is
 * set. It moves behind core's hook owner with the instruction limits
 * (core-luascript.md, sections 3.5 and 8, milestone 3). */
XLUA_INTERNAL void xlua_profiler_return(lua_State *L);

/* The ABI of the core exports, mirrored by LuaDLL.cs (XLua.LuaDLL) and
 * Test/Native/native_regression.py. xlua_core_version changes with it. */
#define XLUA_CORE_VERSION 1

enum {                     /* status of the core exports */
	XLUA_OK = 0,
	XLUA_ERRMEM = 4,       /* LUA_ERRMEM in every Lua version */
	XLUA_ERRCONFIG = 23    /* this build does not support an option (LuaJIT, Lua 5.3) */
};

typedef struct xlua_core_config {
	int64_t memory_limit;  /* bytes; 0 = unlimited */
	uint32_t seed;         /* the string hash seed, used when has_seed is set */
	int32_t has_seed;      /* 0: a random seed per state, as luaL_newstate */
	int32_t libs;          /* reserved for the library selection (milestone 5); pass 0 */
	int32_t sandbox;       /* reserved for the core sandbox (milestone 5); pass 0 */
} xlua_core_config;

typedef struct xlua_core_stats {
	int64_t memory_used;   /* bytes the allocator holds for the state */
	int64_t memory_peak;
	int64_t memory_limit;  /* 0 = unlimited */
	int64_t refusals;      /* allocations the limit refused */
	int64_t overdrafts;    /* allocations granted over the limit under a managed frame */
	int64_t host_oom;      /* allocations the C library could not satisfy */
	int64_t instructions;  /* VM instructions of the last top-level call (milestone 3); 0 */
} xlua_core_stats;

/* The core state of a lua_State created by xlua_core_newstate: the
 * allocator's accounting and the rule that keeps a refused allocation from
 * raising through managed frames (core-luascript.md, section 3.4). It is the
 * allocator's user data, so every thread of the state reaches it. */
typedef struct xlua_core_t {
	size_t used;          /* bytes the allocator holds for the state, this struct included */
	size_t peak;          /* highest 'used' */
	size_t limit;         /* bytes; 0 = unlimited */
	size_t reserve;       /* extra room while the error handler builds a traceback */
	int unprotected;      /* > 0 while C# runs above the innermost protected entry */
	int overdraft;        /* a grant over the limit happened under a managed frame */
	unsigned int seed;    /* the hash seed, when has_seed is set */
	int has_seed;
	int64_t refusals;     /* allocations the limit refused */
	int64_t overdrafts;   /* allocations over the limit granted under a managed frame */
	int64_t host_oom;     /* allocations the C library could not satisfy */
} xlua_core_t;

/* Room above the limit while errorfunc builds a traceback, so that a runtime
 * error near the limit still gets one. */
#define XLUA_CORE_ERROR_RESERVE ((size_t)16 * 1024)

#if defined(USING_LUAJIT)
/* LuaJIT states use LuaJIT's own allocator: there is never a core state. */
static inline xlua_core_t *xlua_core_of(lua_State *L) {
	(void)L;
	return NULL;
}
#else
XLUA_INTERNAL void *xlua_core_alloc(void *ud, void *ptr, size_t osize, size_t nsize);

/* The core state behind any thread of L, or NULL for a state that
 * xlua_core_newstate did not create (luaL_newstate, LuaEnv()). Needs
 * lstate.h; reads two fields instead of calling lua_getallocf, because the
 * C# function trampolines call it on every call. */
static inline xlua_core_t *xlua_core_of(lua_State *L) {
	return G(L)->frealloc == xlua_core_alloc ? (xlua_core_t *)G(L)->ud : NULL;
}
#endif

/* A protected entry from C#: the body runs Lua code under lua_pcall or
 * lua_load, so a refused allocation returns as a status and never raises
 * through a managed frame. The entry clears 'unprotected' for its body, which
 * enables refusals there, also inside a C# callback (a nested entry), and
 * restores the outer values afterwards. Inline: out of line, the saved values
 * must go through memory, which made every protected helper larger. */
typedef struct xlua_core_entry {
	xlua_core_t *xc;
	int unprotected;
	int overdraft;
	size_t reserve;
} xlua_core_entry;

static inline void xlua_core_enter(lua_State *L, xlua_core_entry *e) {
	e->xc = xlua_core_of(L);
	e->unprotected = 0;
	e->overdraft = 0;
	e->reserve = 0;
	if (e->xc != NULL) {
		e->unprotected = e->xc->unprotected;
		e->overdraft = e->xc->overdraft;
		e->reserve = e->xc->reserve;
		e->xc->unprotected = 0;
		e->xc->overdraft = 0;
	}
}

static inline void xlua_core_leave(const xlua_core_entry *e) {
	if (e->xc != NULL) {
		e->xc->unprotected = e->unprotected;
		e->xc->overdraft = e->overdraft;
		e->xc->reserve = e->reserve;
	}
}

#endif
