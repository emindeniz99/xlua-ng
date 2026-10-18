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
 * set. */
XLUA_INTERNAL void xlua_profiler_return(lua_State *L);

/* The ABI of the core exports, mirrored by LuaDLL.cs (XLua.LuaDLL) and
 * Test/Native/native_regression.py. xlua_core_version changes with it. */
#define XLUA_CORE_VERSION 2

enum {                     /* status of the core exports, stop reasons */
	XLUA_OK = 0,
	XLUA_ERRMEM = 4,       /* LUA_ERRMEM in every Lua version */
	XLUA_LIMIT_INSTR = 10, /* the instruction limit stopped the call */
	XLUA_LIMIT_TIME = 11,  /* the time limit stopped the call */
	XLUA_INTERRUPTED = 12, /* xlua_core_interrupt stopped the call */
	XLUA_ERRCONFIG = 23    /* this build does not support an option (LuaJIT, Lua 5.3) */
};

enum {                     /* flags of xlua_core_set_limits */
	XLUA_LIMITS_CLOCK_CHECK = 1,  /* no watchdog (WebGL): the hook reads the clock every 10,000 instructions */
	XLUA_LIMITS_SMALL_SLICES = 2  /* tests: count slices of 1,000 instead of 2^30 instructions */
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
	int64_t instructions;  /* VM instructions of the last top-level call with an instruction limit */
	int64_t stop_reason;   /* XLUA_LIMIT_* of the running call, else of the last one; 0 = none */
	int64_t instruction_limit;
	int64_t time_limit_ns;
} xlua_core_stats;

/* Instruction and time limits: Lua 5.4 and 5.5, which have the lstate.h
 * fields and the lua_resume signature the switch accounting uses. */
#if !defined(USING_LUAJIT) && LUA_VERSION_NUM >= 504
#define XLUA_CORE_LIMITS 1
#else
#define XLUA_CORE_LIMITS 0
#endif

/* The lock between the owner thread and the watchdog or Interrupt
 * (core-luascript.md, section 3.6). Nothing to lock on WebGL without threads. */
#if defined(_MSC_VER)
typedef volatile long xlua_core_lock_t;
#else
typedef int xlua_core_lock_t;
#endif

/* The core state of a lua_State created by xlua_core_newstate: the
 * allocator's accounting and the rule that keeps a refused allocation from
 * raising through managed frames (core-luascript.md, section 3.4), and the
 * state of the instruction and time limits (section 3.5). It is the
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
	/* limits */
	int depth;            /* protected entries now active */
	int limited;          /* an instruction or time limit is configured */
	int entry_active;     /* the outermost entry began with limits */
	int flags;            /* XLUA_LIMITS_* */
	int stop;             /* XLUA_LIMIT_* once the running call is stopped, else 0 */
	int stop_pending;     /* another thread armed the stop hook and it has not fired yet */
	int last_stop;        /* stop of the last outermost entry */
	int64_t instruction_limit;   /* per outermost entry; 0 = unlimited */
	int64_t time_limit_ns;       /* per outermost entry; 0 = unlimited */
	int64_t remaining;           /* instructions not yet handed to a thread's count slice */
	int64_t deadline_ns;         /* monotonic; 0 = none */
	int64_t last_instructions;
	lua_State *entry_L;          /* the thread of the outermost entry */
	lua_State *running;          /* the thread that runs Lua code now */
	lua_Hook prof_hook;          /* the xlua.sethook profiler's hook, called for call and return events */
	xlua_core_lock_t lock;
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

/* The outermost entry with limits (xlua_core.c): arm the budget, the deadline
 * and the hook; at its end, disarm and record the count and the stop. */
XLUA_INTERNAL void xlua_core_begin(lua_State *L, xlua_core_t *xc);
XLUA_INTERNAL void xlua_core_end(xlua_core_t *xc);
/* The allocator frees a CallInfo-sized block through this while a call with
 * limits runs (xlua_core_limits.c). */
XLUA_INTERNAL void xlua_core_free_frame(xlua_core_t *xc, void *ptr);
/* For the profiler (xlua.c): set or clear its hook on a core state, and
 * leave the thread's hook alone while the profiler's own Lua function runs. */
XLUA_INTERNAL void xlua_core_set_profiler(lua_State *L, xlua_core_t *xc, lua_Hook hook);
XLUA_INTERNAL void xlua_core_pause_hook(lua_State *L, xlua_core_t *xc);
XLUA_INTERNAL void xlua_core_resume_hook(lua_State *L, xlua_core_t *xc);
/* Replaces coroutine.resume, wrap and close of a core state with versions
 * that move the budget between threads (luaopen_xlua calls it). */
XLUA_INTERNAL void xlua_core_open_coroutines(lua_State *L);

/* A protected entry from C#: the body runs Lua code under lua_pcall or
 * lua_load, so a refused allocation returns as a status and never raises
 * through a managed frame. The entry clears 'unprotected' for its body, which
 * enables refusals there, also inside a C# callback (a nested entry), and
 * restores the outer values afterwards. The outermost entry of a state with
 * limits also begins and ends a limited call (section 3.5); nested entries
 * keep its budget and deadline. Inline: out of line, the saved values must go
 * through memory, which made every protected helper larger. */
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
#if XLUA_CORE_LIMITS
		if (e->xc->depth++ == 0 && e->xc->limited) {
			xlua_core_begin(L, e->xc);
		}
#endif
	}
}

static inline void xlua_core_leave(const xlua_core_entry *e) {
	if (e->xc != NULL) {
		e->xc->unprotected = e->unprotected;
		e->xc->overdraft = e->overdraft;
		e->xc->reserve = e->reserve;
#if XLUA_CORE_LIMITS
		if (--e->xc->depth == 0 && e->xc->entry_active) {
			xlua_core_end(e->xc);
		}
#endif
	}
}

#endif
