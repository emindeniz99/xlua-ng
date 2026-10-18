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

/* The ABI of the core exports, mirrored by Assets/XLua/Core/LuaDLL.cs
 * (XLua.LuaDLL), Test/Native/core_smoke.c, core_regression.py and
 * native_regression.py. xlua_core_version changes with it: 3 since
 * xlua_core_call and its structs replaced xlua_core_run (milestone 6), 4
 * since the host functions of LuaScript.Register (xlua_core_frame, and the
 * flags field of xlua_core_call_t; milestone 7), 5 since handles
 * (milestone 8): tables and functions pass as handles in xlua_value, results
 * of those types are owning handles the caller releases, and
 * xlua_core_handle replaced xlua_core_newenv, _newbuffer and _tobuffer. */
#define XLUA_CORE_VERSION 5

enum {                     /* status of the core exports, stop reasons */
	XLUA_OK = 0,
	XLUA_YIELD = 1,        /* LUA_YIELD: reserved for coroutines (milestone 9) */
	XLUA_ERRRUN = 2,       /* LUA_ERRRUN: a runtime error, or a function xlua_core_call cannot find */
	XLUA_ERRSYNTAX = 3,    /* LUA_ERRSYNTAX: a syntax error, or a binary chunk */
	XLUA_ERRMEM = 4,       /* LUA_ERRMEM in every Lua version */
	XLUA_ERRERR = 5,       /* LUA_ERRERR: the message handler failed */
	XLUA_LIMIT_INSTR = 10, /* the instruction limit stopped the call */
	XLUA_LIMIT_TIME = 11,  /* the time limit stopped the call */
	XLUA_INTERRUPTED = 12, /* xlua_core_interrupt stopped the call */
	XLUA_ERRTYPE = 20,     /* an argument of a type the entry cannot pass (xlua_core_call) */
	XLUA_ERRSTACK = 21,    /* more arguments than the Lua stack can take */
	XLUA_ERRHANDLE = 22,   /* not a valid handle (an environment of xlua_core_call) */
	XLUA_ERRCONFIG = 23    /* this build or state does not support an option (LuaJIT, Lua 5.3, a library, no sandbox) */
};

/* Value types of xlua_value (core-luascript.md, section 3.2): LuaType in C#. */
enum {
	XLUA_T_NIL = 0,
	XLUA_T_BOOL = 1,
	XLUA_T_INT = 2,
	XLUA_T_NUM = 3,
	XLUA_T_STR = 4,
	XLUA_T_TABLE = 5,
	XLUA_T_FUNC = 6,
	XLUA_T_BUFFER = 7,
	XLUA_T_OTHER = 8
};

/* One value between C# and Lua, 16 bytes on every ABI (the union is at
 * offset 8 also where int64_t is 4-byte aligned). Booleans are u.i 0 or 1;
 * a string is u.s and len bytes, UTF-8 or any bytes, or with len -1 the
 * handle in u.i of a string that LuaScript.Key anchored. Tables, functions
 * and buffers carry a handle in u.i (milestone 8; a buffer also its length in
 * len), other values only their type.
 *
 * A handle is an int32_t: 0 the default environment (LuaScript.Globals); above
 * LUA_RIDX_LAST a registry reference (an owning handle, released with
 * xlua_core_unref); below 0 a field of an iterator of xlua_core_handle, -2r
 * its key and -(2r + 1) its value, where r is the iterator's reference
 * (borrowed until the next step). Arguments of host functions carry their
 * stack index in u.i instead (xlua_core_frame). */
typedef struct xlua_value {
	int32_t type;          /* XLUA_T_* */
	int32_t len;           /* bytes of a string */
	union {
		int64_t i;
		double n;
		const char *s;
	} u;
} xlua_value;

enum {                     /* xlua_core_call_t.kind */
	XLUA_CALL_GLOBAL = 0,  /* call the function 'name' (dotted) of environment 'env' */
	XLUA_CALL_REF = 1,     /* call the function of handle 'fn' */
	XLUA_CALL_CHUNK = 2,   /* load 'code' (text only) as chunk "@" + 'name' into 'env' and run it */
	XLUA_CALL_COMPILE = 3  /* load it without running: the function is the one result (an owning handle) */
};

/* One entry of xlua_core_call: the function or chunk, its arguments and room
 * for the results, in one struct so that a call from C# is one P/Invoke
 * (core-luascript.md, section 2.6). The limits are the state's
 * (xlua_core_set_limits), not repeated here. The 64-bit field comes first,
 * then the pointers, then the 32-bit fields: every field has the same offset
 * whether int64_t is 8- or 4-byte aligned. Out fields are set by every call;
 * the strings they point to (and those of string results) stay valid until
 * the next export call on the state. */
typedef struct xlua_core_call_t {
	int64_t instructions;      /* out: VM instructions of an outermost call with an instruction limit, else 0 */
	const char *name;          /* the function (GLOBAL) or the chunk name without '@' (CHUNK) */
	const char *code;          /* CHUNK: the text */
	const xlua_value *args;
	xlua_value *results;       /* room for results_cap values */
	const char *error;         /* out: the message of a failed call, or NULL */
	const char *traceback;     /* out: luaL_traceback of a runtime or limit error, or NULL */
	int32_t kind;              /* XLUA_CALL_* */
	int32_t env;               /* environment: a table handle; 0 = the default environment */
	int32_t fn;                /* function handle of XLUA_CALL_REF */
	int32_t name_len;
	int32_t code_len;
	int32_t nargs;
	int32_t results_cap;
	int32_t nresults;          /* out: how many values the function returned (also beyond results_cap) */
	int32_t error_len;         /* out */
	int32_t traceback_len;     /* out */
	int32_t error_ref;         /* out: an owning handle of a table or function error value (flags say which), or 0 */
	int32_t mode;              /* XLUA_CALL_CHUNK: XLUA_CORE_MODE_* bits, 0 = text; binary needs XLUA_TRUSTED_BYTECODE */
	int32_t flags;             /* out: XLUA_CALLF_* */
} xlua_core_call_t;

enum {                     /* xlua_core_call_t.flags */
	XLUA_CALLF_HOST_ERROR = 1,  /* the error is the one the last host function raised (its C# exception) */
	XLUA_CALLF_ERROR_TABLE = 2, /* error_ref is the table the script raised: error({code = 5}) */
	XLUA_CALLF_ERROR_FUNC = 4   /* error_ref is a function the script raised */
};

/* xlua_core_handle(L, op, v): every operation on handles, in one protected
 * export (milestone 8). v points to four values; the comment says what each
 * operation reads (in) and writes (out). Tables are read and written raw: no
 * metamethod, so no Lua code runs. A table, function or buffer that GET or REF
 * returns is a new owning handle; NEXT and INEXT return borrowed iterator
 * handles instead (xlua_value). Returns XLUA_OK, XLUA_ERRHANDLE (a handle
 * that is not one of the type needed), XLUA_ERRTYPE (a key or value that
 * cannot be passed), XLUA_ERRMEM, XLUA_ERRRUN with the message in v[2] (a nil
 * or NaN key, "invalid key to 'next'", "table overflow") or XLUA_ERRCONFIG. */
enum {
	XLUA_HANDLE_GET = 0,       /* in v[0] table, v[1] key, v[3].u.i 0 (or a handle to report instead of a new one:
	                              the type only); out v[2] = v[0][v[1]] */
	XLUA_HANDLE_SET = 1,       /* in v[0] table, v[1] key, v[2] value: v[0][v[1]] = v[2] */
	XLUA_HANDLE_LEN = 2,       /* in v[0] table; out v[2] the raw length */
	XLUA_HANDLE_NEXT = 3,      /* in v[0] table, v[3].u.i iterator, v[3].len 1 to start (a new iterator for 0, else
	                              that one again); out v[3].u.i the iterator, v[1] the next key (nil at the end), v[2]
	                              its value */
	XLUA_HANDLE_INEXT = 4,     /* as NEXT over the keys 1, 2, ... up to the first nil value */
	XLUA_HANDLE_NEWTABLE = 5,  /* in v[1].u.i array size, v[1].len hash size; out v[0] a new table */
	XLUA_HANDLE_NEWENV = 6,    /* out v[0] a new environment over the base (section 4) */
	XLUA_HANDLE_REF = 7,       /* in v[0] a handle, or with v[0].len > 0 the argument at that stack index of the
	                              running host function; out v[2] a new owning handle of the same value */
	XLUA_HANDLE_KEY = 8,       /* in v[1] a string; out v[2] the string as an anchored key (len -1), never released */
	XLUA_HANDLE_EQUAL = 9,     /* in v[0], v[1] handles; out v[2] a boolean: the same value */
	XLUA_HANDLE_NEWBUFFER = 10,/* in v[1].u.i length; out v[0] a new zero-filled buffer, v[2].u.s its bytes;
	                              XLUA_ERRCONFIG without LuaLibs.Buffer or for a length outside 0 to 1 GiB */
	XLUA_HANDLE_BUFFER = 11    /* in v[0] a buffer handle; out v[2].u.s its bytes, v[2].len its length */
};

/* One call of a host function (LuaScript.Register, core-luascript.md,
 * sections 2.8 and 3.7). The trampoline fills the arguments, the dispatcher
 * runs C# and fills the results or an error, and the trampoline pushes or
 * raises them once C# has returned, so no Lua error ever crosses a managed
 * frame. Pointers first, then the 32-bit fields: the same offsets wherever
 * pointers have their natural alignment. */
typedef struct xlua_core_frame {
	lua_State *L;              /* the thread the host function runs on */
	const xlua_value *args;    /* nargs values; a string or a buffer (XLUA_T_BUFFER, u.s its bytes, len its
	                              length) points into the value, which stays on the stack; results: a
	                              buffer is an owning handle in u.i, as in xlua_core_call */
	xlua_value *results;       /* room for results_cap values; the host may point it at its own memory */
	const char *error;         /* out: the message of an XLUA_FRAME_ERROR*, the extra message of
	                              XLUA_FRAME_ARGERROR or the expected type of XLUA_FRAME_TYPEERROR
	                              (both NUL-terminated); valid until the trampoline raises it */
	int32_t nargs;
	int32_t results_cap;
	int32_t nresults;          /* out */
	int32_t status;            /* out: XLUA_FRAME_* */
	int32_t error_len;         /* out: bytes of error */
	int32_t error_arg;         /* out: the argument (1-based) of XLUA_FRAME_ARGERROR and _TYPEERROR */
	int32_t yieldable;         /* 1 when the host function may yield (XLUA_FRAME_YIELD) */
	int32_t id;                /* serial of the frame, for borrowed handles (milestone 8) */
} xlua_core_frame;

enum {                     /* xlua_core_frame.status */
	XLUA_FRAME_RETURN = 0,       /* return the results */
	XLUA_FRAME_YIELD = 1,        /* yield the results from the calling coroutine */
	XLUA_FRAME_ERROR = 2,        /* raise the message as it is (an error that already has its position) */
	XLUA_FRAME_ERROR_WHERE = 3,  /* raise the message after the caller's position, as luaL_error */
	XLUA_FRAME_ARGERROR = 4,     /* luaL_argerror(error_arg, error) */
	XLUA_FRAME_TYPEERROR = 5,    /* luaL_typeerror(error_arg, error) */
	XLUA_FRAME_MEMERR = 6        /* Lua's memory error */
};

/* How many arguments and results the trampoline keeps on the C stack. */
#define XLUA_FRAME_INLINE 8

/* The process-wide C# dispatcher of host functions (xlua_core_set_dispatcher):
 * host is xlua_core_open's host pointer, fn_id the function's id. */
typedef void (*xlua_core_dispatch)(void *host, int32_t fn_id, xlua_core_frame *frame);

/* The libraries of xlua_core_config.libs: the bits of LuaOptions.Libraries
 * (LuaLibs, core-luascript.md section 2.14). XLUA_LUALIBS_SAFE is what 0
 * selects in xlua_core_open, as far as the build contains it. */
enum {
	XLUA_LUALIBS_COROUTINE = 1 << 0,
	XLUA_LUALIBS_STRING = 1 << 1,
	XLUA_LUALIBS_TABLE = 1 << 2,
	XLUA_LUALIBS_MATH = 1 << 3,
	XLUA_LUALIBS_UTF8 = 1 << 4,
	XLUA_LUALIBS_BUFFER = 1 << 5,    /* opt-in, not in SAFE: only with CMake XLUA_BUFFER (xlua_core_buffer.c) */
	XLUA_LUALIBS_IO = 1 << 6,        /* LuaEnv only: xlua_core_open refuses io, os, debug and package */
	XLUA_LUALIBS_OS = 1 << 7,
	XLUA_LUALIBS_DEBUG = 1 << 8,
	XLUA_LUALIBS_PACKAGE = 1 << 9,
	/* LuaEnv only, LuaJIT's libraries: LuaEnv applies LuaOptions.Libraries in C#
	 * (LuaEnv.cs, OpenLibraries) and uses the same bits; xlua_core_open refuses them. */
	XLUA_LUALIBS_BIT = 1 << 10,
	XLUA_LUALIBS_JIT = 1 << 11,
	XLUA_LUALIBS_FFI = 1 << 12,
	XLUA_LUALIBS_SAFE = XLUA_LUALIBS_COROUTINE | XLUA_LUALIBS_STRING | XLUA_LUALIBS_TABLE
		| XLUA_LUALIBS_MATH | XLUA_LUALIBS_UTF8
};

enum {                     /* xlua_core_call_t.mode for XLUA_CALL_CHUNK (Lua's load modes "t", "b", "bt"); 0 is text */
	XLUA_CORE_MODE_TEXT = 1,
	XLUA_CORE_MODE_BINARY = 2  /* the host's own precompiled chunks: only with CMake XLUA_TRUSTED_BYTECODE */
};

enum {                     /* flags of xlua_core_set_limits */
	XLUA_LIMITS_CLOCK_CHECK = 1,  /* no watchdog (WebGL): the hook reads the clock every 10,000 instructions */
	XLUA_LIMITS_SMALL_SLICES = 2  /* tests: count slices of 1,000 instead of 2^30 instructions */
};

typedef struct xlua_core_config {
	int64_t memory_limit;  /* bytes; 0 = unlimited */
	uint32_t seed;         /* the string hash seed, used when has_seed is set */
	int32_t has_seed;      /* 0: a random seed per state, as luaL_newstate */
	int32_t libs;          /* xlua_core_open: XLUA_LUALIBS_* bits, 0 = the safe set; xlua_core_newstate ignores it */
	int32_t sandbox;       /* xlua_core_open: must be 1 (section 4); xlua_core_newstate ignores it */
} xlua_core_config;

/* What the resolver of require (xlua_core_set_resolver) reports for a module:
 * its text, or the error message when it returns -1. The memory stays the
 * host's and must stay valid until the next call into the state. An empty
 * chunk name means the module name; either gets an '@' prefix. */
typedef struct xlua_core_source {
	const char *code;
	int32_t code_len;
	const char *chunkname;
	int32_t chunkname_len;
} xlua_core_source;

/* Returns 1 with the module's text in *out, 0 if there is no such module, or
 * -1 with an error message in out->code. host is xlua_core_open's. */
typedef int32_t (*xlua_core_resolver)(void *host, const char *name, int32_t name_len, xlua_core_source *out);

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
	/* host functions (LuaScript.Register) */
	void *host;                  /* xlua_core_open's host pointer, for the dispatcher */
	lua_State *callback_L;       /* the thread of the innermost running host function, or NULL */
	uint32_t frames;             /* serial of the last host function frame (wraps; the ids stay positive) */
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

/* Exports of xlua_core.c that xlua_core_sandbox.c builds on. */
LUA_API lua_State *xlua_core_newstate(const xlua_core_config *cfg, int32_t *status);
LUA_API void xlua_core_close(lua_State *L);

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
 * that move the budget between threads (luaopen_xlua and xlua_core_open call it). */
XLUA_INTERNAL void xlua_core_open_coroutines(lua_State *L);
/* After a host function returned (the LuaScript trampoline): raises the stop
 * of an instruction, time or interrupt limit that tripped meanwhile (the
 * watchdog or Interrupt set it), so that a call whose limit tripped never
 * returns normally, also when no Lua instruction follows (xlua_core_limits.c). */
XLUA_INTERNAL void xlua_core_check_stop(lua_State *L, xlua_core_t *xc);
#if defined(XLUA_SANDBOX)
/* After C# returned to a C function of the sandbox (require's resolver, the
 * host function trampoline): the memory error of an overdraft, as the C#
 * function trampolines raise it. Defined in xlua_core.c. */
XLUA_INTERNAL void xlua_core_check_overdraft(lua_State *L, xlua_core_t *xc);
/* The trampoline of every host function (xlua_core.c, a hot file): upvalue 1
 * is the function's id, 0 once unregistered. */
XLUA_INTERNAL int xlua_core_trampoline(lua_State *L);
/* Pushes the value of handle h (xlua_value) and returns its Lua type, or
 * pushes nil and returns LUA_TNONE for a handle that is none. Non-raising; two
 * free stack slots (xlua_core_sandbox.c). */
XLUA_INTERNAL int xlua_core_push_ref(lua_State *L, int32_t h);
/* Pushes a value from C# (an argument, result, key or value): 1, or 0 with
 * nothing pushed for a handle that is not one of v's type, -1 for a type that
 * cannot be passed. Raises only memory errors (a string); three free slots. */
XLUA_INTERNAL int xlua_core_push_value(lua_State *L, const xlua_value *v);
/* The registry key (its address) of the error the last host function raised,
 * for XLUA_CALLF_HOST_ERROR (xlua_core_sandbox.c creates the slot). */
XLUA_INTERNAL char xlua_core_hosterr_key;
#endif

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

/* ---- the buffer library (milestone 10, build/xlua_core_buffer.c) ---- */
#define XLUA_BUFFERLIBNAME "buffer"
#define XLUA_BUFFER_MAX ((size_t)1 << 30)  /* bytes of the largest buffer, Luau's limit */
#if defined(XLUA_BUFFER)
/* The C budget (xlua_core_limits.c, also declared by xlua_luahooks.h). */
XLUA_INTERNAL void xlua_core_budget(lua_State *L, int n);
/* luaopen of the buffer library: xlua_core_open opens it for XLUA_LUALIBS_BUFFER. */
XLUA_INTERNAL int xlua_core_open_buffer(lua_State *L);
/* The bytes and length of the buffer at idx, or NULL for any other value.
 * Non-raising; needs two free stack slots. */
XLUA_INTERNAL void *xlua_core_buffer_at(lua_State *L, int idx, size_t *len);
/* Pushes a new zero-filled buffer of len bytes (len <= XLUA_BUFFER_MAX) and
 * returns its bytes, or returns NULL and pushes nothing in a state without the
 * library. Raises memory errors; not charged to the C budget. */
XLUA_INTERNAL void *xlua_core_new_buffer(lua_State *L, size_t len);
#endif
/* ---- end of the buffer library ---- */

#endif
