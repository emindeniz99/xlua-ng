/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* The instruction and time limits of the core layer (LuaOptions.InstructionLimit
 * and TimeLimit, docs/design/core-luascript.md, sections 3.5, 3.6 and 3.11,
 * milestones 3 and 4). A file of its own so that the size flags compile it at
 * -Oz: it runs once per call, slice or coroutine switch, never per
 * instruction, unlike the allocator and trampolines of xlua_core.c. */

#define LUA_LIB

#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#endif

#include <stdint.h>
#include <time.h>
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif
#if defined(_MSC_VER)
#include <intrin.h>
#endif
#if defined(__EMSCRIPTEN__)
#include <emscripten.h>
#endif

#if !defined(USING_LUAJIT) && !defined(XLUA_AMALGAMATION)
#include "lstate.h"
#endif

#include "xlua_core.h"

/* ------------------------------------------------------------------------
 * Instruction and time limits (core-luascript.md, sections 3.5 and 3.6).
 *
 * The outermost protected entry of a state with limits (xlua_core_begin)
 * gives the call its instruction budget and deadline and arms the hook on the
 * entry thread. The budget is handed to threads in count slices: a thread's
 * count hook fires when its slice is used up, and the unused rest goes back
 * at every thread switch (core_resume, core_wrap, core_close) and at the end,
 * so the budget is global across coroutines and the count is exact. When a
 * limit trips, 'stop' is set and the stop hook (every event, count 1) raises
 * the prebuilt message again on every event until the outermost entry ends,
 * so pcall, xpcall handlers and __close handlers cannot keep the call going.
 * A watchdog (LuaWatchdog.cs) or Interrupt arms the stop hook from another
 * thread under the lock (xlua_core_watch, xlua_core_interrupt).
 * ------------------------------------------------------------------------ */
#if XLUA_CORE_LIMITS

#define MAX_SLICE ((int64_t)1 << 30)   /* the hook count is an int */
#define SMALL_SLICE ((int64_t)1000)    /* XLUA_LIMITS_SMALL_SLICES */
#define CLOCK_SLICE ((int64_t)10000)   /* XLUA_LIMITS_CLOCK_CHECK */

#if defined(_MSC_VER)
static void core_lock(xlua_core_t *xc) {
	while (_InterlockedExchange(&xc->lock, 1) != 0) {
	}
}
static void core_unlock(xlua_core_t *xc) {
	_InterlockedExchange(&xc->lock, 0);
}
#elif defined(__EMSCRIPTEN__) && !defined(__EMSCRIPTEN_PTHREADS__)
static void core_lock(xlua_core_t *xc) { (void)xc; }
static void core_unlock(xlua_core_t *xc) { (void)xc; }
#else
static void core_lock(xlua_core_t *xc) {
	while (__atomic_exchange_n(&xc->lock, 1, __ATOMIC_ACQUIRE) != 0) {
	}
}
static void core_unlock(xlua_core_t *xc) {
	__atomic_store_n(&xc->lock, 0, __ATOMIC_RELEASE);
}
#endif

/* A full memory barrier (arm_stop_remote). */
static void core_fence(void) {
#if defined(_MSC_VER)
	MemoryBarrier();
#elif defined(__EMSCRIPTEN__) && !defined(__EMSCRIPTEN_PTHREADS__)
#else
	__atomic_thread_fence(__ATOMIC_SEQ_CST);
#endif
}

/* A monotonic clock in nanoseconds. */
static int64_t now_ns(void) {
#if defined(_WIN32)
	LARGE_INTEGER count, freq;
	QueryPerformanceCounter(&count);
	QueryPerformanceFrequency(&freq);
	return (int64_t)((double)count.QuadPart * 1e9 / (double)freq.QuadPart);
#elif defined(__EMSCRIPTEN__)
	return (int64_t)(emscripten_get_now() * 1e6);
#else
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (int64_t)ts.tv_sec * 1000000000 + ts.tv_nsec;
#endif
}

/* Registry keys of the stop messages, created by xlua_core_set_limits so
 * that raising them allocates nothing. */
static const char stop_keys[3] = {0, 0, 0};
static const char *const stop_messages[3] = {
	"instruction limit exceeded", "time limit exceeded", "interrupted"
};

static void core_hook(lua_State *L, lua_Debug *ar);

/* Raises the stop message; lua_error does not return. From a hook, Lua has
 * turned hooks off (luaD_hook clears allowhook until the hook returns), and
 * the error's message handler would run without them: an xpcall handler
 * that loops would never stop. Hooks are turned back on first, as they are
 * when a hook returns. */
static XLUA_NOINLINE void raise_stop(lua_State *L, int reason) {
	L->allowhook = 1;
	lua_rawgetp(L, LUA_REGISTRYINDEX, &stop_keys[reason - XLUA_LIMIT_INSTR]);
	(void)lua_error(L);
}

/* Gives the unused rest of L's slice back to the budget. Under the lock. */
static void give_back(xlua_core_t *xc, lua_State *L) {
	if (xc->instruction_limit > 0 && !xc->stop && L->hook == core_hook) {  /* armed with a count by arm() */
		xc->remaining += L->hookcount;
	}
}

/* Sets L's hook: for the running call when 'active', otherwise for no call
 * (the profiler only). Under the lock. */
static XLUA_NOINLINE void arm_thread(xlua_core_t *xc, lua_State *L, int active) {
	int mask = 0;
	int64_t count = 0;
	if (active) {
		if (xc->stop) {
			lua_sethook(L, core_hook, LUA_MASKCOUNT, 1);
			return;
		}
		if (xc->instruction_limit > 0) {
			int64_t cap = (xc->flags & XLUA_LIMITS_SMALL_SLICES) ? SMALL_SLICE : MAX_SLICE;
			if (xc->time_limit_ns > 0 && (xc->flags & XLUA_LIMITS_CLOCK_CHECK)) {
				cap = CLOCK_SLICE;
			}
			count = xc->remaining < cap ? xc->remaining : cap;
			xc->remaining -= count;
			count += count == 0;  /* exhausted (a hook count of 0 never fires): the next instruction stops */
			mask = LUA_MASKCOUNT;
		}
		else if (xc->flags & XLUA_LIMITS_CLOCK_CHECK) {  /* a time limit only (the call is limited) */
			count = CLOCK_SLICE;
			mask = LUA_MASKCOUNT;
		}
	}
	if (xc->prof_hook != NULL) {
		mask |= LUA_MASKCALL | LUA_MASKRET;
	}
	lua_sethook(L, mask != 0 ? core_hook : NULL, mask, (int)count);
}

static void arm(xlua_core_t *xc, lua_State *L) {
	arm_thread(xc, L, xc->entry_active);
}

/* Stops the running call from its own thread (the hook, the C budget), which
 * raises right after: the stop hook on the running thread. Under the lock. */
static void stop_call(xlua_core_t *xc, int reason) {
	xc->stop = reason;
	arm(xc, xc->running);
}

/* Arms the stop hook on the running thread from another thread (the
 * watchdog, Interrupt), or again while it has not fired. Under the lock.
 *
 * lua_sethook is safe in a signal handler on the thread it hooks
 * (ldebug.c:124-132 in 5.4.9), not from another thread: it stores 'hookmask'
 * and then the 'trap' flag of every Lua frame (settraps), and the running
 * thread, which reads only 'trap' in a loop, may see the flag before the mask
 * on a weakly ordered CPU (AArch64). luaG_traceexec then takes the trap for a
 * spurious one, clears it and returns (ldebug.c:933-936 in 5.4.9, 941-944 in
 * 5.5.1), and a loop without calls never looks at the mask again: the call
 * ran forever (docs/design/core-luascript.md, section 8.3). The mask goes
 * first, then a full fence, then lua_sethook sets the traps. 'stop_pending'
 * stays set until the hook fires, so that the watchdog arms again on its next
 * poll should an arming still be lost. */
static XLUA_NOINLINE void arm_stop_remote(xlua_core_t *xc) {
	lua_State *L = xc->running;
	L->hook = core_hook;
	L->basehookcount = 1;
	L->hookcount = 1;
	L->hookmask = LUA_MASKCOUNT;
	core_fence();
	lua_sethook(L, core_hook, LUA_MASKCOUNT, 1);
	xc->stop_pending = 1;
}

/* The only lua_Hook of a core state. Count events run the limits: with
 * 'stop' set (count 1) every instruction raises again; otherwise the end of a
 * slice charges the budget and checks the deadline. Call and return events go
 * to the profiler. A thread that an earlier call armed (a suspended
 * coroutine) is disarmed. The stop hook counts instructions only, so C
 * functions such as errorfunc and debug.traceback still run while the error
 * travels to C#; Lua code, a message handler or __close handler included,
 * cannot run a single instruction. */
static void core_hook(lua_State *L, lua_Debug *ar) {
	xlua_core_t *xc = xlua_core_of(L);
	int reason = 0;
	if (ar->event != LUA_HOOKCOUNT) {
		if (xc->prof_hook != NULL) {  /* call and return events: no line events are armed */
			xc->prof_hook(L, ar);
		}
		return;
	}
	core_lock(xc);
	if (!xc->entry_active || L != xc->running) {
		arm_thread(xc, L, 0);
	}
	else if (xc->stop) {
		reason = xc->stop;
		xc->stop_pending = 0;
	}
	else {
		if (xc->instruction_limit > 0 && xc->remaining == 0) {
			reason = XLUA_LIMIT_INSTR;
		}
		else if (xc->deadline_ns != 0 && now_ns() >= xc->deadline_ns) {
			reason = XLUA_LIMIT_TIME;
		}
		if (reason != 0) {
			stop_call(xc, reason);
		}
		else {
			arm(xc, L);
		}
	}
	core_unlock(xc);
	if (reason != 0) {
		raise_stop(L, reason);
	}
}

void xlua_core_begin(lua_State *L, xlua_core_t *xc) {
	core_lock(xc);
	xc->entry_active = 1;
	xc->stop = 0;
	xc->stop_pending = 0;
	xc->entry_L = L;
	xc->running = L;
	xc->remaining = xc->instruction_limit;
	xc->deadline_ns = xc->time_limit_ns > 0 ? now_ns() + xc->time_limit_ns : 0;
	arm(xc, L);
	core_unlock(xc);
}

void xlua_core_end(xlua_core_t *xc) {
	core_lock(xc);
	give_back(xc, xc->running);
	xc->last_instructions = xc->instruction_limit > 0 ? xc->instruction_limit - xc->remaining : 0;
	xc->last_stop = xc->stop;
	xc->entry_active = 0;
	xc->stop = 0;
	xc->stop_pending = 0;
	xc->deadline_ns = 0;
	arm(xc, xc->entry_L);
	xc->running = NULL;
	core_unlock(xc);
}


void xlua_core_set_profiler(lua_State *L, xlua_core_t *xc, lua_Hook hook) {
	core_lock(xc);
	xc->prof_hook = hook;
	give_back(xc, L);
	arm(xc, L);
	core_unlock(xc);
}

void xlua_core_pause_hook(lua_State *L, xlua_core_t *xc) {
	core_lock(xc);
	give_back(xc, L);
	lua_sethook(L, NULL, 0, 0);
	core_unlock(xc);
}

void xlua_core_resume_hook(lua_State *L, xlua_core_t *xc) {
	core_lock(xc);
	arm(xc, L);
	core_unlock(xc);
}

#if !defined(XLUA_NO_COROUTINE_LIB)
/* A switch from 'from' to 'to' (resume) or back (yield, return, error). */
static XLUA_NOINLINE void switch_thread(xlua_core_t *xc, lua_State *from, lua_State *to) {
	core_lock(xc);
	if (xc->entry_active) {
		give_back(xc, from);
		xc->running = to;
		arm(xc, to);
	}
	core_unlock(xc);
}

/*
** The coroutine switches. auxresume, luaB_coresume and luaB_auxwrap are
** Lua's own (lcorolib.c in 5.4.9 and 5.5.1, identical there), with the switch
** accounting around lua_resume.
** Copyright (C) 1994-2025 Lua.org, PUC-Rio. See the copyright notice in lua.h.
*/
static int core_auxresume(lua_State *L, lua_State *co, int narg) {
	xlua_core_t *xc = xlua_core_of(L);
	int status, nres;
	if (l_unlikely(!lua_checkstack(co, narg))) {
		lua_pushliteral(L, "too many arguments to resume");
		return -1;  /* error flag */
	}
	lua_xmove(L, co, narg);
	switch_thread(xc, L, co);
	status = lua_resume(co, L, narg, &nres);
	switch_thread(xc, co, L);
	if (l_likely(status == LUA_OK || status == LUA_YIELD)) {
		if (l_unlikely(!lua_checkstack(L, nres + 1))) {
			lua_pop(co, nres);  /* remove results anyway */
			lua_pushliteral(L, "too many results to resume");
			return -1;  /* error flag */
		}
		lua_xmove(co, L, nres);  /* move yielded values */
		return nres;
	}
	else {
		lua_xmove(co, L, 1);  /* move error message */
		return -1;  /* error flag */
	}
}

static int core_resume(lua_State *L) {
	lua_State *co = lua_tothread(L, 1);
	int r;
	luaL_argexpected(L, co, 1, "coroutine");
	r = core_auxresume(L, co, lua_gettop(L) - 1);
	if (l_unlikely(r < 0)) {
		lua_pushboolean(L, 0);
		lua_insert(L, -2);
		return 2;  /* return false + error message */
	}
	else {
		lua_pushboolean(L, 1);
		lua_insert(L, -(r + 1));
		return r + 1;  /* return true + 'resume' returns */
	}
}

static int core_auxwrap(lua_State *L) {
	lua_State *co = lua_tothread(L, lua_upvalueindex(1));
	int r = core_auxresume(L, co, lua_gettop(L));
	if (l_unlikely(r < 0)) {  /* error? */
		int stat = lua_status(co);
		if (stat != LUA_OK && stat != LUA_YIELD) {  /* error in the coroutine? */
			xlua_core_t *xc = xlua_core_of(L);
			switch_thread(xc, L, co);
			stat = lua_closethread(co, L);  /* close its tbc variables */
			switch_thread(xc, co, L);
			lua_xmove(co, L, 1);  /* move error message to the caller */
		}
		if (stat != LUA_ERRMEM &&  /* not a memory error and ... */
		    lua_type(L, -1) == LUA_TSTRING) {  /* ... error object is a string? */
			luaL_where(L, 1);  /* add extra info, if available */
			lua_insert(L, -2);
			lua_concat(L, 2);
		}
		return lua_error(L);  /* propagate error */
	}
	return r;
}

static int core_wrap(lua_State *L) {
	lua_State *NL;
	luaL_checktype(L, 1, LUA_TFUNCTION);
	NL = lua_newthread(L);
	lua_pushvalue(L, 1);  /* move function to top */
	lua_xmove(L, NL, 1);  /* move function from L to NL */
	lua_pushcclosure(L, core_auxwrap, 1);
	return 1;
}

/* coroutine.close runs the pending __close handlers on the coroutine: Lua's
 * own function (upvalue 1), with the switch around it when it closes a
 * suspended or dead coroutine; in every other case it raises before any code
 * runs. */
static int core_close(lua_State *L) {
	lua_CFunction close = lua_tocfunction(L, lua_upvalueindex(1));
	lua_State *co = lua_tothread(L, 1);
	lua_Debug ar;
	int r;
	if (co == NULL || co == L || (lua_status(co) == LUA_OK && lua_getstack(co, 0, &ar))) {
		return close(L);  /* not a coroutine, the running one or a normal one */
	}
	switch_thread(xlua_core_of(L), L, co);
	r = close(L);
	switch_thread(xlua_core_of(L), co, L);
	return r;
}

#endif

void xlua_core_open_coroutines(lua_State *L) {
#if defined(XLUA_NO_COROUTINE_LIB)
	/* XLUA_LIBS without coroutine: scripts cannot switch threads. */
	(void)L;
#else
	if (xlua_core_of(L) == NULL) {
		return;
	}
	luaL_getsubtable(L, LUA_REGISTRYINDEX, LUA_LOADED_TABLE);
	luaL_getsubtable(L, -1, LUA_COLIBNAME);  /* opened by luaL_openlibs before */
	lua_pushcfunction(L, core_resume);
	lua_setfield(L, -2, "resume");
	lua_pushcfunction(L, core_wrap);
	lua_setfield(L, -2, "wrap");
	lua_getfield(L, -1, "close");
	lua_pushcclosure(L, core_close, 1);
	lua_setfield(L, -2, "close");
	lua_pop(L, 2);
#endif
}

/* luai_budget (build/xlua_luahooks.h): a long C function of the string or
 * table library charges n instruction-equivalents and checks the stop and
 * the deadline (core-luascript.md, section 3.11). */
void xlua_core_budget(lua_State *L, int n) {
	xlua_core_t *xc = xlua_core_of(L);
	int reason = 0;
	if (xc == NULL || !xc->entry_active) {
		return;
	}
	core_lock(xc);
	if (xc->stop) {
		reason = xc->stop;
	}
	else {
		if (xc->instruction_limit > 0) {
			if (xc->remaining >= n) {
				xc->remaining -= n;
			}
			else if (L->hook == core_hook && L->hookcount > n - xc->remaining) {
				L->hookcount -= (int)(n - xc->remaining);
				xc->remaining = 0;
			}
			else {
				reason = XLUA_LIMIT_INSTR;
			}
		}
		if (reason == 0 && xc->deadline_ns != 0 && now_ns() >= xc->deadline_ns) {
			reason = XLUA_LIMIT_TIME;
		}
		if (reason != 0) {
			stop_call(xc, reason);
		}
	}
	core_unlock(xc);
	if (reason != 0) {
		raise_stop(L, reason);
	}
}
#else
void xlua_core_begin(lua_State *L, xlua_core_t *xc) {
	(void)L;
	(void)xc;
}
void xlua_core_end(xlua_core_t *xc) {
	(void)xc;
}
void xlua_core_open_coroutines(lua_State *L) {
	(void)L;
}
#endif

/* Sets the limits of each outermost call; returns 0 where the build has
 * none (Lua 5.3, LuaJIT, a state without the core allocator). Creates the
 * stop messages, so call it outside a running call. */
LUA_API int xlua_core_set_limits(lua_State *L, int64_t instructions, int64_t time_ns, int32_t flags) {
#if XLUA_CORE_LIMITS
	xlua_core_t *xc = xlua_core_of(L);
	int i;
	if (xc == NULL) {
		return 0;
	}
	for (i = 0; i < 3; i++) {
		lua_pushstring(L, stop_messages[i]);
		lua_rawsetp(L, LUA_REGISTRYINDEX, &stop_keys[i]);
	}
	core_lock(xc);
	xc->instruction_limit = instructions > 0 ? instructions : 0;
	/* a deadline is now + time_ns: keep the sum in range */
	xc->time_limit_ns = time_ns > 0 ? (time_ns < INT64_MAX / 4 ? time_ns : INT64_MAX / 4) : 0;
	xc->flags = flags;
	xc->limited = xc->instruction_limit > 0 || xc->time_limit_ns > 0;
	xc->last_instructions = 0;  /* the count and stop belong to the earlier limits */
	xc->last_stop = 0;
	core_unlock(xc);
	return 1;
#else
	(void)L;
	(void)instructions;
	(void)time_ns;
	(void)flags;
	return 0;
#endif
}

/* Any thread: stops the running call with XLUA_INTERRUPTED. Returns 1 if a
 * call was running, 0 otherwise. */
LUA_API int xlua_core_interrupt(lua_State *L) {
#if XLUA_CORE_LIMITS
	xlua_core_t *xc = xlua_core_of(L);
	int stopped = 0;
	if (xc == NULL) {
		return 0;
	}
	core_lock(xc);
	if (xc->entry_active && !xc->stop) {
		xc->stop = XLUA_INTERRUPTED;
		arm_stop_remote(xc);
		stopped = 1;
	}
	else if (xc->entry_active && xc->stop_pending) {
		arm_stop_remote(xc);  /* stopped, the hook has not fired yet: arm again */
	}
	core_unlock(xc);
	return stopped;
#else
	(void)L;
	return 0;
#endif
}

/* The watchdog thread (LuaWatchdog.cs): stops a call past its deadline, and
 * arms the stop hook again while a stop (also Interrupt's) has not reached
 * the running thread (arm_stop_remote). Returns the nanoseconds left to the
 * running call's deadline, or -1 when no call with a deadline runs. */
LUA_API int64_t xlua_core_watch(lua_State *L) {
#if XLUA_CORE_LIMITS
	xlua_core_t *xc = xlua_core_of(L);
	int64_t left = -1;
	if (xc == NULL) {
		return -1;
	}
	core_lock(xc);
	if (xc->entry_active && xc->deadline_ns != 0) {
		if (!xc->stop) {
			left = xc->deadline_ns - now_ns();
			if (left <= 0) {
				xc->stop = XLUA_LIMIT_TIME;
				arm_stop_remote(xc);
				left = -1;
			}
		}
		else if (xc->stop_pending) {
			arm_stop_remote(xc);
		}
	}
	core_unlock(xc);
	return left;
#else
	(void)L;
	return -1;
#endif
}

