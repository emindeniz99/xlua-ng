/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* The native sandbox of LuaScript (docs/design/core-luascript.md, sections
 * 3.8, 3.9 and 4; milestone 5): a state with the selected libraries, a frozen
 * base that no script can reach, environments over it, text-only loading,
 * the wrapped load, setmetatable and collectgarbage, and require through a
 * host resolver. The core preset always builds this file; the bridge presets
 * build it with the CMake option XLUA_LUASCRIPT. Lua 5.4 and 5.5 only.
 *
 * The shape of a sandbox state:
 *  - the base B (LUA_RIDX_GLOBALS): the kept base functions, the wrapped
 *    setmetatable and collectgarbage, require when enabled. No script gets a
 *    reference to it: it has no _G, the environments' metatable is locked,
 *    and load never defaults to it;
 *  - the original library tables, in the registry only; string has no dump,
 *    and the string metatable is locked;
 *  - environments: a table per environment (the default one, every
 *    XLUA_HANDLE_NEWENV, every required module) with shallow copies of the
 *    libraries, _G pointing to itself, its own load, and the shared locked
 *    metatable { __index = B, __metatable = false };
 *  - host functions (xlua_core_register, milestone 7): closures of
 *    xlua_core_trampoline (xlua_core.c) in B, or for "lib.name" in a library
 *    table that every environment copies like a standard library. Weak tables
 *    of the environments and of their library copies let a registration
 *    after an environment was made reach its copy;
 *  - handles (milestone 8, xlua_value in xlua_core.h): registry references of
 *    tables, functions, buffers and keys, and iterators, two-slot tables that
 *    anchor the key and the value of the current step of a traversal. */

#define LUA_LIB

#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#endif

#include <string.h>

#if !defined(XLUA_AMALGAMATION)
#include "lstate.h"
#include "ldo.h"
#endif

#include "xlua_core.h"

#if !XLUA_CORE_LIMITS
#error "the LuaScript sandbox needs Lua 5.4 or 5.5"
#endif

/* Registry keys (their addresses). */
static const char base_key = 0;    /* B */
static const char libs_key = 0;    /* name -> original library table */
static const char envmt_key = 0;   /* the metatable of every environment */
static const char globals_key = 0; /* the default environment (handle 0) */
static const char loaded_key = 0;  /* require's cache: name -> value, or loading_key while it loads */
static char loading_key = 0;  /* not const: pushed as a light userdata */
static const char host_key = 0;    /* the host pointer of xlua_core_open, for the resolver */
static const char anchors_key = 0; /* the string results of the last xlua_core_call, at 1..n */
static const char msg_key = 0;     /* the message that xlua_core_call's error points into */
static const char trace_key = 0;   /* the traceback of the last runtime error, or false */
static const char envs_key = 0;    /* weak keys: every environment -> true */
static const char copies_key = 0;  /* weak keys: every environment's copy of a library -> the library's name */
static const char errv_key = 0;    /* the last error value that is not a string, or false (ErrorValue) */
static char iter_start = 0;        /* not const: pushed as a light userdata, the key of an iterator not started */

static xlua_core_resolver g_resolver = NULL;

/* The libraries a build can contain, with their LuaLibs bits. CMake defines
 * XLUA_LIB_<NAME> for every library of XLUA_LIBS. */
static const struct {
	const char *name;
	lua_CFunction open;
	int bit;
} core_libs[] = {
#if defined(XLUA_LIB_COROUTINE)
	{LUA_COLIBNAME, luaopen_coroutine, XLUA_LUALIBS_COROUTINE},
#endif
#if defined(XLUA_LIB_STRING)
	{LUA_STRLIBNAME, luaopen_string, XLUA_LUALIBS_STRING},
#endif
#if defined(XLUA_LIB_TABLE)
	{LUA_TABLIBNAME, luaopen_table, XLUA_LUALIBS_TABLE},
#endif
#if defined(XLUA_LIB_MATH)
	{LUA_MATHLIBNAME, luaopen_math, XLUA_LUALIBS_MATH},
#endif
#if defined(XLUA_LIB_UTF8)
	{LUA_UTF8LIBNAME, luaopen_utf8, XLUA_LUALIBS_UTF8},
#endif
#if defined(XLUA_BUFFER)  /* milestone 10: opt-in, never in the default set */
	{XLUA_BUFFERLIBNAME, xlua_core_open_buffer, XLUA_LUALIBS_BUFFER},
#endif
	{NULL, NULL, 0}
};

/* The base functions an environment sees as Lua ships them (section 4). */
static const char *const base_kept[] = {
	"assert", "error", "getmetatable", "ipairs", "next", "pairs", "pcall", "rawequal", "rawget",
	"rawlen", "rawset", "select", "tonumber", "tostring", "type", "xpcall", "_VERSION", NULL
};

static int built_libs(void) {
	int i, bits = 0;
	for (i = 0; core_libs[i].name != NULL; i++) {
		bits |= core_libs[i].bit;
	}
	return bits;
}

/* A length from C#: a negative one is empty. */
static size_t len_of(int32_t n) {
	return n > 0 ? (size_t)n : 0;
}

/* Whether L was created by xlua_core_open. Non-raising. */
static int is_sandbox(lua_State *L) {
	int found = lua_rawgetp(L, LUA_REGISTRYINDEX, &base_key) == LUA_TTABLE;
	lua_pop(L, 1);
	return found;
}

/*
** setmetatable, load and their reader follow lbaselib.c of Lua 5.4.9 (the
** reader keeps 5.4's reserved slot, which 5.5's lua_load also accepts).
** Copyright (C) 1994-2025 Lua.org, PUC-Rio. See the copyright notice in lua.h.
*/

/* setmetatable refuses a metatable with a __gc field (section 3.5): Lua runs
 * finalizers with hooks off, so a loop in __gc would escape every limit. Lua
 * marks a table for finalization only when its metatable has __gc at this
 * point, so a field added later does nothing. */
static int core_setmetatable(lua_State *L) {
	int t = lua_type(L, 2);
	luaL_checktype(L, 1, LUA_TTABLE);
	luaL_argexpected(L, t == LUA_TNIL || t == LUA_TTABLE, 2, "nil or table");
	if (l_unlikely(luaL_getmetafield(L, 1, "__metatable") != LUA_TNIL)) {
		return luaL_error(L, "cannot change a protected metatable");
	}
	if (t == LUA_TTABLE) {
		lua_pushliteral(L, "__gc");
		if (lua_rawget(L, 2) != LUA_TNIL) {
			return luaL_argerror(L, 2, "metatables with __gc are not allowed");
		}
	}
	lua_settop(L, 2);
	lua_setmetatable(L, 1);
	return 1;
}

/* collectgarbage: "count" only, as in Luau. The other options would let a
 * script stop the collector or run full collections at will. */
static int core_collectgarbage(lua_State *L) {
	const char *opt = luaL_optstring(L, 1, "collect");
	if (strcmp(opt, "count") != 0) {
		return luaL_argerror(L, 1, lua_pushfstring(L, "option '%s' is not allowed (only \"count\")", opt));
	}
	lua_pushnumber(L, (lua_Number)lua_gc(L, LUA_GCCOUNT, 0) + (lua_Number)lua_gc(L, LUA_GCCOUNTB, 0) / 1024);
	return 1;
}

#define RESERVEDSLOT 5

static const char *core_reader(lua_State *L, void *ud, size_t *size) {
	(void)ud;
	luaL_checkstack(L, 2, "too many nested functions");
	lua_pushvalue(L, 1);  /* get function */
	lua_call(L, 0, 1);  /* call it */
	if (lua_isnil(L, -1)) {
		lua_pop(L, 1);  /* pop result */
		*size = 0;
		return NULL;
	}
	else if (l_unlikely(!lua_isstring(L, -1))) {
		luaL_error(L, "reader function must return a string");
	}
	lua_replace(L, RESERVEDSLOT);  /* save string in reserved slot */
	return lua_tolstring(L, RESERVEDSLOT, size);
}

/* load(chunk [, chunkname [, mode [, env]]]) of one environment (upvalue 1):
 * text only, and without an env argument the chunk gets that environment,
 * never the base. A mode without 't' asks for binary chunks only and fails. */
static int core_load(lua_State *L) {
	int status;
	size_t l;
	const char *s = lua_tolstring(L, 1, &l);
	const char *mode = luaL_optstring(L, 3, "t");
	int env = !lua_isnone(L, 4) ? 4 : lua_upvalueindex(1);
	if (strchr(mode, 't') == NULL) {
		luaL_pushfail(L);
		lua_pushfstring(L, "attempt to load a binary chunk (mode is '%s'): text chunks only", mode);
		return 2;
	}
	if (s != NULL) {  /* loading a string? */
		const char *chunkname = luaL_optstring(L, 2, s);
		status = luaL_loadbufferx(L, s, l, chunkname, "t");
	}
	else {  /* loading from a reader function */
		const char *chunkname = luaL_optstring(L, 2, "=(load)");
		luaL_checktype(L, 1, LUA_TFUNCTION);
		lua_settop(L, RESERVEDSLOT);  /* create reserved slot */
		status = lua_load(L, core_reader, NULL, chunkname, "t");
	}
	if (status != LUA_OK) {
		luaL_pushfail(L);
		lua_insert(L, -2);  /* put before error message */
		return 2;  /* return fail plus error message */
	}
	lua_pushvalue(L, env);
	lua_setupvalue(L, -2, 1);  /* a text chunk always has _ENV as its only upvalue */
	return 1;
}

/* Pushes a shallow copy of the table on top of the stack. */
static void push_copy(lua_State *L) {
	int n = 0;
	lua_pushnil(L);
	while (lua_next(L, -2)) {
		n++;
		lua_pop(L, 1);
	}
	lua_createtable(L, 0, n);
	lua_pushnil(L);
	while (lua_next(L, -3)) {
		lua_pushvalue(L, -2);
		lua_insert(L, -2);
		lua_rawset(L, -4);
	}
}

/* Pushes a new environment (section 4), recorded with its library copies in
 * the weak tables, so that a later registration reaches it. */
static void push_env(lua_State *L) {
	lua_createtable(L, 0, 8);
	lua_rawgetp(L, LUA_REGISTRYINDEX, &copies_key);
	lua_rawgetp(L, LUA_REGISTRYINDEX, &libs_key);
	lua_pushnil(L);
	while (lua_next(L, -2)) {  /* E, copies, libs, name, library */
		push_copy(L);
		lua_remove(L, -2);  /* E, copies, libs, name, copy */
		lua_pushvalue(L, -1);
		lua_pushvalue(L, -3);
		lua_rawset(L, -6);  /* copies[copy] = name */
		lua_pushvalue(L, -2);
		lua_insert(L, -2);
		lua_rawset(L, -6);  /* E[name] = copy */
	}
	lua_pop(L, 2);
	lua_pushvalue(L, -1);
	lua_setfield(L, -2, LUA_GNAME);
	lua_pushvalue(L, -1);
	lua_pushcclosure(L, core_load, 1);
	lua_setfield(L, -2, "load");
	lua_rawgetp(L, LUA_REGISTRYINDEX, &envmt_key);
	lua_setmetatable(L, -2);
	lua_rawgetp(L, LUA_REGISTRYINDEX, &envs_key);
	lua_pushvalue(L, -2);
	lua_pushboolean(L, 1);
	lua_rawset(L, -3);
	lua_pop(L, 1);
}

/* Pushes a new table with weak keys. */
static void push_weak_keys(lua_State *L) {
	lua_newtable(L);
	lua_createtable(L, 0, 1);
	lua_pushliteral(L, "k");
	lua_setfield(L, -2, "__mode");
	lua_setmetatable(L, -2);
}

/* The handles of xlua_value (xlua_core.h): 0 the default environment, a
 * registry reference above LUA_RIDX_LAST, or below 0 the key (-2r) or the
 * value (-(2r + 1)) of iterator r. Pushes nil and returns LUA_TNONE for
 * anything else; callers check the type they need. Non-raising. */
int xlua_core_push_ref(lua_State *L, int32_t h) {
	if (h == 0) {
		return lua_rawgetp(L, LUA_REGISTRYINDEX, &globals_key);
	}
	if (h > LUA_RIDX_LAST) {
		return lua_rawgeti(L, LUA_REGISTRYINDEX, h);
	}
	if (h < 0 && h != INT32_MIN && (-h >> 1) > LUA_RIDX_LAST) {
		if (lua_rawgeti(L, LUA_REGISTRYINDEX, -h >> 1) == LUA_TTABLE) {
			int t = lua_rawgeti(L, -1, 1 + (-h & 1));
			lua_remove(L, -2);
			return t;
		}
		lua_pop(L, 1);
	}
	lua_pushnil(L);
	return LUA_TNONE;
}

/* The XLUA_T_* type of the value at idx. Non-raising; two free stack slots. */
static int type_of(lua_State *L, int idx) {
	static const unsigned char types[] = {
		XLUA_T_NIL, XLUA_T_BOOL, XLUA_T_OTHER, XLUA_T_NUM, XLUA_T_STR, XLUA_T_TABLE, XLUA_T_FUNC, XLUA_T_OTHER, XLUA_T_OTHER
	};
	int t = lua_type(L, idx);
	if (t == LUA_TNUMBER && lua_isinteger(L, idx)) {
		return XLUA_T_INT;
	}
#if defined(XLUA_BUFFER)
	if (t == LUA_TUSERDATA) {
		size_t len;
		return xlua_core_buffer_at(L, idx, &len) != NULL ? XLUA_T_BUFFER : XLUA_T_OTHER;
	}
#endif
	return t >= 0 && t < (int)sizeof(types) ? types[t] : XLUA_T_NIL;
}

int xlua_core_push_value(lua_State *L, const xlua_value *v) {
	switch (v->type) {
	case XLUA_T_NIL: lua_pushnil(L); return 1;
	case XLUA_T_BOOL: lua_pushboolean(L, v->u.i != 0); return 1;
	case XLUA_T_INT: lua_pushinteger(L, (lua_Integer)v->u.i); return 1;
	case XLUA_T_NUM: lua_pushnumber(L, (lua_Number)v->u.n); return 1;
	case XLUA_T_STR:
		if (v->len >= 0) {
			lua_pushlstring(L, v->u.s, (size_t)v->len);
			return 1;
		}
		break;  /* a key of LuaScript.Key, by its handle */
	case XLUA_T_TABLE: case XLUA_T_FUNC: case XLUA_T_BUFFER:
		break;
	default:
		return -1;
	}
	if (v->u.i < INT32_MIN || v->u.i > INT32_MAX) {
		return 0;
	}
	xlua_core_push_ref(L, (int32_t)v->u.i);
	if (type_of(L, -1) == v->type) {
		return 1;
	}
	lua_pop(L, 1);
	return 0;
}

/* The value at idx (absolute) as an xlua_value. A string points into the
 * value, which the caller keeps alive. A table, function or buffer gets a new
 * owning handle, or 'borrow' when it is not 0. Raises memory errors only. */
static void to_value(lua_State *L, int idx, xlua_value *out, int32_t borrow) {
	size_t len = 0;
	out->type = type_of(L, idx);
	out->len = 0;
	out->u.i = 0;
	switch (out->type) {
	case XLUA_T_BOOL:
		out->u.i = lua_toboolean(L, idx);
		return;
	case XLUA_T_INT:
		out->u.i = (int64_t)lua_tointeger(L, idx);
		return;
	case XLUA_T_NUM:
		out->u.n = (double)lua_tonumber(L, idx);
		return;
	case XLUA_T_STR:
		out->u.s = lua_tolstring(L, idx, &len);
		if (len > (size_t)INT32_MAX) {
			luaL_error(L, "a string of more than 2 GB cannot be passed to C#");
		}
		out->len = (int32_t)len;
		return;
#if defined(XLUA_BUFFER)
	case XLUA_T_BUFFER:
		xlua_core_buffer_at(L, idx, &len);
		out->len = (int32_t)len;  /* <= XLUA_BUFFER_MAX */
		break;
#endif
	case XLUA_T_TABLE: case XLUA_T_FUNC:
		break;
	default:
		return;
	}
	if (borrow != 0) {
		out->u.i = borrow;
		return;
	}
	lua_pushvalue(L, idx);
	out->u.i = luaL_ref(L, LUA_REGISTRYINDEX);
}

/* require(name): the cached value, or the resolver's text for name, loaded
 * with mode "t" and run in a fresh environment with (name, chunk name); its
 * result, or true for nil, is cached for the state. A loading module found
 * again is a loop. A failed load is not cached, so a later require tries
 * again, as in Lua 5.4. */
static int core_require(lua_State *L) {
	size_t len;
	const char *name = luaL_checklstring(L, 1, &len);
	xlua_core_t *xc = xlua_core_of(L);
	xlua_core_source src;
	int32_t found;
	int status;
	lua_settop(L, 1);
	lua_rawgetp(L, LUA_REGISTRYINDEX, &loaded_key);  /* 2 */
	lua_pushvalue(L, 1);
	if (lua_rawget(L, 2) != LUA_TNIL) {
		if (lua_touserdata(L, 3) == &loading_key) {
			return luaL_error(L, "loop requiring module '%s'", name);
		}
		return 1;
	}
	if (g_resolver == NULL) {
		return luaL_error(L, "module '%s' not found: no module resolver", name);
	}
	memset(&src, 0, sizeof(src));
	lua_rawgetp(L, LUA_REGISTRYINDEX, &host_key);
	xc->unprotected++;  /* C# runs: no refusal now (xlua_core_alloc) */
	found = g_resolver(lua_touserdata(L, -1), name, (int32_t)len, &src);
	xc->unprotected--;
	xlua_core_check_overdraft(L, xc);
	if (found < 0) {
		lua_pushfstring(L, "error loading module '%s': ", name);
		lua_pushlstring(L, src.code, len_of(src.code_len));
		lua_concat(L, 2);
		return lua_error(L);
	}
	if (found == 0) {
		return luaL_error(L, "module '%s' not found", name);
	}
	lua_settop(L, 2);
	lua_pushvalue(L, 1);
	lua_pushlightuserdata(L, &loading_key);
	lua_rawset(L, 2);
	{
		luaL_Buffer b;  /* 3: the chunk name, '@' and the resolver's name or the module's */
		luaL_buffinit(L, &b);
		luaL_addchar(&b, '@');
		if (src.chunkname_len > 0) {
			luaL_addlstring(&b, src.chunkname, (size_t)src.chunkname_len);
		}
		else {
			luaL_addlstring(&b, name, len);
		}
		luaL_pushresult(&b);
	}
	status = luaL_loadbufferx(L, src.code, len_of(src.code_len), lua_tostring(L, 3), "t");  /* 4 */
	if (status == LUA_OK) {
		push_env(L);
		lua_setupvalue(L, 4, 1);
		lua_pushvalue(L, 1);
		lua_pushvalue(L, 3);
		status = lua_pcall(L, 2, 1, 0);
	}
	if (status != LUA_OK) {  /* the error is on top */
		lua_pushvalue(L, 1);
		lua_pushnil(L);
		lua_rawset(L, 2);  /* not cached */
		if (status == LUA_ERRMEM) {
			luaD_throw(L, LUA_ERRMEM);  /* as Lua raises it: no message handler */
		}
		return lua_error(L);
	}
	if (lua_isnil(L, -1)) {
		lua_pop(L, 1);
		lua_pushboolean(L, 1);
	}
	lua_pushvalue(L, 1);
	lua_pushvalue(L, -2);
	lua_rawset(L, 2);
	return 1;
}

typedef struct open_args {
	int libs;  /* XLUA_LUALIBS_* */
	void *host;
} open_args;

/* Opens the libraries and builds the sandbox (section 4). Raises only memory
 * errors; xlua_core_open runs it protected. */
static int open_body(lua_State *L) {
	const open_args *a = (const open_args *)lua_touserdata(L, 1);
	int i;
	luaL_requiref(L, LUA_GNAME, luaopen_base, 0);  /* 2: Lua's global table */
	lua_createtable(L, 0, 24);  /* 3: B */
	for (i = 0; base_kept[i] != NULL; i++) {
		lua_getfield(L, 2, base_kept[i]);
		lua_setfield(L, 3, base_kept[i]);
	}
	lua_pushcfunction(L, core_setmetatable);
	lua_setfield(L, 3, "setmetatable");
	lua_pushcfunction(L, core_collectgarbage);
	lua_setfield(L, 3, "collectgarbage");
	lua_pushvalue(L, 3);
	lua_rawseti(L, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS);
	luaL_getsubtable(L, LUA_REGISTRYINDEX, LUA_LOADED_TABLE);
	lua_pushvalue(L, 3);
	lua_setfield(L, -2, LUA_GNAME);  /* package.loaded._G: also B, for luaL_traceback's names */
	lua_pop(L, 1);
	lua_createtable(L, 0, 6);  /* 4: the original libraries */
	for (i = 0; core_libs[i].name != NULL; i++) {
		if ((a->libs & core_libs[i].bit) == 0) {
			continue;
		}
		luaL_requiref(L, core_libs[i].name, core_libs[i].open, 0);
		if (core_libs[i].bit == XLUA_LUALIBS_STRING) {
			lua_pushnil(L);
			lua_setfield(L, -2, "dump");  /* also out of reach through ("").dump */
			lua_pushliteral(L, "");
			lua_getmetatable(L, -1);
			lua_pushboolean(L, 0);
			lua_setfield(L, -2, "__metatable");
			lua_pop(L, 2);
		}
		lua_setfield(L, 4, core_libs[i].name);
	}
	if (a->libs & XLUA_LUALIBS_COROUTINE) {
		xlua_core_open_coroutines(L);  /* resume, wrap and close follow the budget */
	}
	lua_rawsetp(L, LUA_REGISTRYINDEX, &libs_key);
	lua_createtable(L, 0, 2);  /* the environments' metatable */
	lua_pushvalue(L, 3);
	lua_setfield(L, -2, "__index");
	lua_pushboolean(L, 0);
	lua_setfield(L, -2, "__metatable");
	lua_rawsetp(L, LUA_REGISTRYINDEX, &envmt_key);
	lua_newtable(L);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &loaded_key);
	lua_pushlightuserdata(L, a->host);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &host_key);
	lua_createtable(L, 8, 0);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &anchors_key);
	lua_pushboolean(L, 0);  /* the slots exist, so replacing them allocates nothing */
	lua_rawsetp(L, LUA_REGISTRYINDEX, &msg_key);
	lua_pushboolean(L, 0);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &trace_key);
	lua_pushboolean(L, 0);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &xlua_core_hosterr_key);
	lua_pushboolean(L, 0);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &errv_key);
	push_weak_keys(L);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &envs_key);
	push_weak_keys(L);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &copies_key);
	push_env(L);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &globals_key);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &base_key);  /* last: the state is a sandbox now */
	return 0;
}

/* A state for LuaScript: xlua_core_newstate's allocator and seed, then the
 * libraries of cfg->libs (XLUA_LUALIBS_* bits; 0 is every safe library this
 * build contains, which leaves out the opt-in buffer library) and the sandbox. cfg->sandbox must be 1: the plain globals
 * of section 3.2's sandbox = 0 are for LuaEnv and not implemented. host is
 * passed to the resolver. Returns NULL with *status XLUA_ERRCONFIG for a
 * library this build does not contain (io, os, debug and package never are),
 * or XLUA_ERRMEM. */
LUA_API lua_State *xlua_core_open(const xlua_core_config *cfg, void *host, int32_t *status) {
	xlua_core_config plain = *cfg;
	xlua_core_entry entry;
	open_args args;
	lua_State *L;
	int built = built_libs();
	int failed;
	args.libs = cfg->libs != 0 ? cfg->libs : (built & XLUA_LUALIBS_SAFE);
	args.host = host;
	if (cfg->sandbox != 1 || (args.libs & ~built) != 0) {
		*status = XLUA_ERRCONFIG;
		return NULL;
	}
	plain.libs = 0;
	plain.sandbox = 0;
	L = xlua_core_newstate(&plain, status);
	if (L == NULL) {
		return NULL;
	}
	xlua_core_of(L)->host = host;
	xlua_core_enter(L, &entry);
	lua_pushcfunction(L, open_body);
	lua_pushlightuserdata(L, &args);
	failed = lua_pcall(L, 1, 0, 0) != LUA_OK;
	xlua_core_leave(&entry);
	if (failed) {  /* only memory can fail */
		xlua_core_close(L);
		*status = XLUA_ERRMEM;
		return NULL;
	}
	return L;
}

/* The thread an export from C# works on: the thread of the running host
 * function, if one runs. A host function in a coroutine that calls back into
 * the script must not run Lua code on the main thread: that thread is not
 * running (it resumed the coroutine), and the limits follow the running
 * thread (section 3.5). */
static lua_State *active_thread(lua_State *L) {
	lua_State *cb = xlua_core_of(L)->callback_L;
	return cb != NULL ? cb : L;
}

/* Runs a protected body with one light userdata argument as a memory entry;
 * returns XLUA_OK or XLUA_ERRMEM, the only error the bodies raise. */
static int protected_body(lua_State *L, lua_CFunction body, void *ud) {
	xlua_core_entry entry;
	int status;
	L = active_thread(L);
	xlua_core_enter(L, &entry);
	lua_pushcfunction(L, body);
	lua_pushlightuserdata(L, ud);
	status = lua_pcall(L, 1, 0, 0);
	xlua_core_leave(&entry);
	if (status != LUA_OK) {
		lua_pop(L, 1);
		return XLUA_ERRMEM;
	}
	return XLUA_OK;
}

/* Releases an owning handle: a registry reference of a table, function or
 * userdata (environments, buffers, iterators). Returns 1, or 0 for anything
 * else (the default environment 0, Lua's own registry slots, a free slot,
 * a key of LuaScript.Key), so a wrong number never frees another slot.
 * Non-raising. */
LUA_API int xlua_core_unref(lua_State *L, int32_t ref) {
	int t;
	if (ref <= LUA_RIDX_LAST || !is_sandbox(L)) {
		return 0;
	}
	L = active_thread(L);
	t = lua_rawgeti(L, LUA_REGISTRYINDEX, ref);
	lua_pop(L, 1);
	if (t != LUA_TTABLE && t != LUA_TFUNCTION && t != LUA_TUSERDATA) {
		return 0;
	}
	luaL_unref(L, LUA_REGISTRYINDEX, ref);
	return 1;
}

static int require_body(lua_State *L) {
	const int32_t *enabled = (const int32_t *)lua_touserdata(L, 1);
	lua_rawgetp(L, LUA_REGISTRYINDEX, &base_key);
	if (*enabled) {
		lua_pushcfunction(L, core_require);
	}
	else {
		lua_pushnil(L);
	}
	lua_setfield(L, -2, "require");
	return 0;
}

/* Puts require into the base (enabled != 0) or takes it out. It asks the
 * process-wide resolver of xlua_core_set_resolver. */
LUA_API int xlua_core_set_require(lua_State *L, int32_t enabled) {
	if (!is_sandbox(L)) {
		return XLUA_ERRCONFIG;
	}
	return protected_body(L, require_body, &enabled);
}

/* The resolver of every state's require; NULL makes require fail with "not
 * found". Set once, like the dispatcher of milestone 7. */
LUA_API void xlua_core_set_resolver(xlua_core_resolver fn) {
	g_resolver = fn;
}

/* ---- host functions (LuaScript.Register, milestone 7) ---- */

typedef struct register_args {
	const char *name;
	size_t len;
	int32_t env;  /* a table handle: the name is its key; 0: the base or a library */
	int32_t id;   /* the new function's id; 0 removes the name */
	int32_t old;  /* out: the id of the host function the name had, or 0 */
} register_args;

/* The host function at idx (absolute), if it is one, is unregistered: its id
 * becomes 0, so the closure fails wherever a script keeps it. Returns its id,
 * or 0 for any other value. */
static int32_t kill_function(lua_State *L, int idx) {
	int32_t id = 0;
	if (lua_tocfunction(L, idx) == xlua_core_trampoline && lua_getupvalue(L, idx, 1) != NULL) {
		id = (int32_t)lua_tointeger(L, -1);
		lua_pop(L, 1);
		lua_pushinteger(L, 0);
		lua_setupvalue(L, idx, 1);
	}
	return id;
}

/* A registration in library 'name' reaches every environment: where an
 * environment still has its copy of the library, the copy's key gets the new
 * value, unless the script put its own value there; an environment without
 * the library gets a copy of a library the registration has just created.
 * All indices are absolute. */
static void update_envs(lua_State *L, int name, int lib, int key, int old, int value, int created) {
	int envs, copies;
	lua_rawgetp(L, LUA_REGISTRYINDEX, &envs_key);
	envs = lua_gettop(L);
	lua_rawgetp(L, LUA_REGISTRYINDEX, &copies_key);
	copies = lua_gettop(L);
	lua_pushnil(L);
	while (lua_next(L, envs)) {  /* E, true */
		int t;
		lua_pop(L, 1);
		lua_pushvalue(L, name);
		t = lua_rawget(L, -2);  /* E, E[name] */
		if (t == LUA_TTABLE) {
			lua_pushvalue(L, -1);
			lua_rawget(L, copies);  /* E, C, copies[C] */
			if (lua_rawequal(L, -1, name)) {
				lua_pushvalue(L, key);
				lua_rawget(L, -3);  /* E, C, name, C[key] */
				if (lua_isnil(L, -1) || lua_rawequal(L, -1, old)) {
					lua_pushvalue(L, key);
					lua_pushvalue(L, value);
					lua_rawset(L, -5);
				}
				lua_pop(L, 1);
			}
			lua_pop(L, 2);
		}
		else if (t == LUA_TNIL && created) {
			lua_pop(L, 1);
			lua_pushvalue(L, lib);
			push_copy(L);
			lua_remove(L, -2);  /* E, copy */
			lua_pushvalue(L, -1);
			lua_pushvalue(L, name);
			lua_rawset(L, copies);
			lua_pushvalue(L, name);
			lua_insert(L, -2);
			lua_rawset(L, -3);
		}
		else {
			lua_pop(L, 1);
		}
	}
	lua_pop(L, 2);
}

/* Stack: 1 the arguments, 2 the new value, 3 the target table (B, or the
 * libraries for a dotted name: then 4 the library's name and 5 the library),
 * then the key and the old value. */
static int register_body(lua_State *L) {
	register_args *a = (register_args *)lua_touserdata(L, 1);
	const char *dot = a->env != 0 ? NULL : (const char *)memchr(a->name, '.', a->len);
	int target = 3, key, created = 0;
	lua_settop(L, 1);
	if (a->id > 0) {
		lua_pushinteger(L, a->id);
		lua_pushcclosure(L, xlua_core_trampoline, 1);
	}
	else {
		lua_pushnil(L);
	}
	if (dot == NULL) {
		if (a->env != 0) {
			xlua_core_push_ref(L, a->env);  /* a table: xlua_core_register checked it */
		}
		else {
			lua_rawgetp(L, LUA_REGISTRYINDEX, &base_key);
		}
		lua_pushlstring(L, a->name, a->len);
	}
	else {
		size_t lib_len = (size_t)(dot - a->name);
		lua_rawgetp(L, LUA_REGISTRYINDEX, &libs_key);
		lua_pushlstring(L, a->name, lib_len);
		lua_pushvalue(L, 4);
		if (lua_rawget(L, 3) != LUA_TTABLE) {
			lua_pop(L, 1);
			if (a->id <= 0) {
				return 0;  /* nothing to remove */
			}
			lua_newtable(L);  /* 5: a new library */
			lua_pushvalue(L, 4);
			lua_pushvalue(L, 5);
			lua_rawset(L, 3);
			luaL_getsubtable(L, LUA_REGISTRYINDEX, LUA_LOADED_TABLE);
			lua_pushvalue(L, 4);
			lua_pushvalue(L, 5);
			lua_rawset(L, -3);  /* package.loaded too, for luaL_traceback's names ("game.log") */
			lua_pop(L, 1);
			created = 1;
		}
		target = 5;
		lua_pushlstring(L, dot + 1, a->len - lib_len - 1);
	}
	key = lua_gettop(L);
	lua_pushvalue(L, key);
	lua_rawget(L, target);  /* key + 1: the old value */
	a->old = kill_function(L, key + 1);
	lua_pushvalue(L, key);
	lua_pushvalue(L, 2);
	lua_rawset(L, target);
	if (dot != NULL) {
		update_envs(L, 4, 5, key, key + 1, 2, created);
	}
	return 0;
}

/* Registers host function fn_id (the C# dispatcher's id, > 0) under 'name'
 * in the base, visible from every environment, or for "lib.name" in library
 * 'lib' (created when needed), which every environment copies like a
 * standard library. fn_id 0 removes the name. A host function the name had is
 * unregistered: its closure fails from now on wherever a script keeps it, and
 * *old_id is its id (also when the call fails with XLUA_ERRMEM), so the host
 * can reuse it. With env != 0, a table handle (LuaScript.Register(LuaTableRef
 * env, ...)), 'name' is a key of that table as it is, dots included.
 * XLUA_ERRCONFIG for a state without the sandbox, or a name that is empty or
 * (in the base) has more than one dot or an empty part; XLUA_ERRHANDLE for an
 * env that is not a table. */
LUA_API int xlua_core_register(lua_State *L, int32_t env, const char *name, int32_t name_len, int32_t fn_id, int32_t *old_id) {
	size_t len = name != NULL ? len_of(name_len) : 0;
	const char *dot = len > 0 && env == 0 ? (const char *)memchr(name, '.', len) : NULL;
	register_args a;
	int status;
	*old_id = 0;
	if (!is_sandbox(L) || len == 0
	    || (dot != NULL && (dot == name || dot == name + len - 1 || memchr(dot + 1, '.', len - (size_t)(dot - name) - 1) != NULL))) {
		return XLUA_ERRCONFIG;
	}
	if (env != 0) {
		lua_State *T = active_thread(L);
		int t = xlua_core_push_ref(T, env);
		lua_pop(T, 1);
		if (t != LUA_TTABLE) {
			return XLUA_ERRHANDLE;
		}
	}
	a.name = name;
	a.len = len;
	a.env = env;
	a.id = fn_id > 0 ? fn_id : 0;
	a.old = 0;
	status = protected_body(L, register_body, &a);
	*old_id = a.old;
	return status;
}

/* The message handler of xlua_core_call: the message (or a description of a
 * non-string error) as the new error value, as lua.c's msghandler makes it,
 * and the traceback in the registry (trace_key), so that C# gets the two
 * apart (LuaException.LuaStackTrace). The limit is raised by the error
 * reserve meanwhile; if the handler fails, the entry restores it. */
static int call_msgh(lua_State *L) {
	xlua_core_t *xc = xlua_core_of(L);
	size_t reserve = xc->reserve;
	int msg = 1, is_string = lua_tostring(L, 1) != NULL;
	xc->reserve = XLUA_CORE_ERROR_RESERVE;
	if (is_string) {
		lua_pushboolean(L, 0);
	}
	else {
		lua_pushvalue(L, 1);
	}
	lua_rawsetp(L, LUA_REGISTRYINDEX, &errv_key);  /* the error value itself (ErrorValue); an existing key */
	if (!is_string) {  /* is error object not a string? */
		if (!luaL_callmeta(L, 1, "__tostring") || lua_type(L, -1) != LUA_TSTRING) {
			lua_pushfstring(L, "(error object is a %s value)", luaL_typename(L, 1));
		}
		msg = lua_gettop(L);
	}
	luaL_traceback(L, L, NULL, 1);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &trace_key);  /* an existing key: no allocation */
	lua_settop(L, msg);
	xc->reserve = reserve;
	return 1;
}

typedef struct call_args {
	xlua_core_call_t *c;
	int status;  /* of the lookup, the load and the call, when the body itself did not fail */
} call_args;

/* The body's failure before any Lua code runs: status and message. */
static int call_fail(lua_State *L, call_args *a, int status, const char *msg) {
	a->status = status;
	lua_pushstring(L, msg);
	return 1;
}

/* Pushes the function 'name' of the environment at index 3 for
 * XLUA_CALL_GLOBAL: raw gets along the dots, the first name in the
 * environment and then in the base (core-luascript.md, section 2.6). Returns
 * 0 with Lua's message for the expression pushed when a name is nil or a step
 * is not a table. */
static int push_global(lua_State *L, call_args *a, const char *name, size_t len) {
	const char *seg = name, *end = name + len;
	int first = 1;
	lua_pushvalue(L, 3);
	for (;;) {
		const char *dot = seg < end ? (const char *)memchr(seg, '.', (size_t)(end - seg)) : NULL;
		int t;
		lua_pushlstring(L, seg, (size_t)((dot != NULL ? dot : end) - seg));  /* table, key */
		lua_pushvalue(L, -1);
		t = lua_rawget(L, -3);  /* table, key, value */
		if (t == LUA_TNIL && first) {
			lua_pop(L, 1);
			lua_rawgetp(L, LUA_REGISTRYINDEX, &base_key);
			lua_pushvalue(L, -2);
			t = lua_rawget(L, -2);  /* table, key, base, value */
			lua_remove(L, -2);
		}
		if (t == LUA_TNIL || (dot != NULL && t != LUA_TTABLE)) {
			lua_pushfstring(L, "attempt to %s a %s value (%s '%s')", dot != NULL ? "index" : "call",
			                luaL_typename(L, -1), first ? "global" : "field", lua_tostring(L, -2));
			a->status = XLUA_ERRRUN;
			return 0;
		}
		lua_replace(L, -3);  /* value, key */
		lua_pop(L, 1);
		if (dot == NULL) {
			return 1;
		}
		first = 0;
		seg = dot + 1;
	}
}

/* Pushes the arguments of c; 0 with a message pushed for one that cannot be. */
static int push_args(lua_State *L, call_args *a) {
	const xlua_core_call_t *c = a->c;
	int i;
	if (c->nargs < 0 || (c->nargs > 0 && c->args == NULL) || c->nargs > INT32_MAX - 3 || !lua_checkstack(L, c->nargs + 3)) {
		call_fail(L, a, XLUA_ERRSTACK, "too many arguments");
		return 0;
	}
	for (i = 0; i < c->nargs; i++) {
		int pushed = xlua_core_push_value(L, &c->args[i]);
		if (pushed <= 0) {
			a->status = pushed == 0 ? XLUA_ERRHANDLE : XLUA_ERRTYPE;
			lua_pushfstring(L, pushed == 0 ? "argument %d is not a valid handle (type %d)" : "argument %d cannot be passed (type %d)",
			                i + 1, (int)c->args[i].type);
			return 0;
		}
	}
	return 1;
}

/* Stack: 1 the arguments, 2 the anchors, 3 the environment, 4 the message
 * handler, 5 the function, then its arguments and results. Returns nothing,
 * or the message of a failure (a->status). */
static int call_body(lua_State *L) {
	call_args *a = (call_args *)lua_touserdata(L, 1);
	xlua_core_call_t *c = a->c;
	int i, n, nstr = 0;
	lua_rawgetp(L, LUA_REGISTRYINDEX, &anchors_key);  /* 2 */
	lua_pushboolean(L, 0);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &trace_key);
	if (xlua_core_push_ref(L, c->env) != LUA_TTABLE) {  /* 3 */
		return call_fail(L, a, XLUA_ERRHANDLE, "invalid environment handle");
	}
	lua_pushcfunction(L, call_msgh);  /* 4 */
	if (c->kind == XLUA_CALL_GLOBAL) {
		if (!push_global(L, a, c->name != NULL ? c->name : "", c->name != NULL ? len_of(c->name_len) : 0)) {  /* 5 */
			return 1;
		}
	}
	else if (c->kind == XLUA_CALL_REF) {
		if (xlua_core_push_ref(L, c->fn) != LUA_TFUNCTION) {  /* 5 */
			return call_fail(L, a, XLUA_ERRHANDLE, "invalid function handle");
		}
	}
	else if (c->kind == XLUA_CALL_CHUNK || c->kind == XLUA_CALL_COMPILE) {
		luaL_Buffer b;  /* the chunk name, with '@' (section 2.5) */
		const char *mode;
		switch (c->mode) {
		case 0: case XLUA_CORE_MODE_TEXT: mode = "t"; break;
		case XLUA_CORE_MODE_BINARY: mode = "b"; break;
		case XLUA_CORE_MODE_TEXT | XLUA_CORE_MODE_BINARY: mode = "bt"; break;
		default:
			return call_fail(L, a, XLUA_ERRCONFIG, "invalid mode: use XLUA_CORE_MODE_TEXT, XLUA_CORE_MODE_BINARY or both");
		}
#if !defined(XLUA_TRUSTED_BYTECODE)
		if (c->mode & XLUA_CORE_MODE_BINARY) {
			return call_fail(L, a, XLUA_ERRCONFIG, "binary chunks are not supported in this build (XLUA_TRUSTED_BYTECODE is off)");
		}
#endif
		luaL_buffinit(L, &b);
		luaL_addchar(&b, '@');
		luaL_addlstring(&b, c->name, c->name != NULL ? len_of(c->name_len) : 0);
		luaL_pushresult(&b);
		a->status = luaL_loadbufferx(L, c->code != NULL ? c->code : "", c->code != NULL ? len_of(c->code_len) : 0, lua_tostring(L, -1), mode);
		lua_remove(L, -2);
		if (a->status != LUA_OK) {
			return 1;
		}
		lua_pushvalue(L, 3);
		if (lua_setupvalue(L, -2, 1) == NULL) {  /* _ENV: a text chunk's only upvalue */
			lua_pop(L, 1);  /* a binary chunk of a function without upvalues */
		}
	}
	else {
		return call_fail(L, a, XLUA_ERRCONFIG, "this kind of call is not supported");
	}
	if (c->kind != XLUA_CALL_COMPILE) {  /* COMPILE: the function itself is the result */
		if (!push_args(L, a)) {
			return 1;
		}
		/* Release the last call's strings only now: an argument may point into one. */
		for (i = 1; lua_rawgeti(L, 2, i) != LUA_TNIL; i++) {
			lua_pop(L, 1);
			lua_pushnil(L);
			lua_rawseti(L, 2, i);
		}
		lua_pop(L, 1);
		a->status = lua_pcall(L, c->nargs, LUA_MULTRET, 4);
		if (a->status != LUA_OK) {
			/* error({code = 5}): the table or function itself as a handle (LuaRuntimeException.ErrorValue) */
			int t = lua_rawgetp(L, LUA_REGISTRYINDEX, &errv_key);
			if (a->status == LUA_ERRRUN && (t == LUA_TTABLE || t == LUA_TFUNCTION)) {
				c->flags |= t == LUA_TTABLE ? XLUA_CALLF_ERROR_TABLE : XLUA_CALLF_ERROR_FUNC;
				c->error_ref = luaL_ref(L, LUA_REGISTRYINDEX);
			}
			else {
				lua_pop(L, 1);
			}
			return 1;  /* the message */
		}
	}
	n = lua_gettop(L) - 4;
	luaL_checkstack(L, 3, "too many results");  /* to_value pushes up to three values meanwhile */
	for (i = 0; i < n && i < c->results_cap && c->results != NULL; i++) {
		xlua_value *r = &c->results[i];
		to_value(L, 5 + i, r, 0);
		if (r->type == XLUA_T_STR) {  /* anchored until the next call */
			lua_pushvalue(L, 5 + i);
			lua_rawseti(L, 2, ++nstr);
		}
	}
	c->nresults = n;
	return 0;
}

/* One entry from C# (core-luascript.md, sections 2.5 to 2.7 and 3.5): calls
 * the function c->name of an environment (XLUA_CALL_GLOBAL), or loads the text
 * chunk c->code as "@" + c->name into it and runs it (XLUA_CALL_CHUNK; with
 * c->mode XLUA_CORE_MODE_BINARY also the host's own precompiled chunks, in a
 * build with XLUA_TRUSTED_BYTECODE only, else XLUA_ERRCONFIG), or calls the
 * function handle c->fn (XLUA_CALL_REF), with the arguments of c, as one
 * entry with the state's limits; XLUA_CALL_COMPILE loads the chunk without
 * running it and returns the function as an owning handle. The environment
 * c->env is any table handle (0: the default environment). Tables, functions
 * and buffers among the results are new owning handles. Returns XLUA_OK
 * with the results in c->results, or an error status: XLUA_ERRSYNTAX,
 * XLUA_ERRRUN (also for a function that is not there), XLUA_ERRMEM,
 * XLUA_ERRERR, XLUA_LIMIT_* or XLUA_INTERRUPTED (from the stop reason),
 * XLUA_ERRHANDLE, XLUA_ERRTYPE, XLUA_ERRSTACK, or XLUA_ERRCONFIG for a state
 * without the sandbox or another kind. Then c->error is the message and, for a
 * runtime or limit error, c->traceback the traceback. The stack of L is left
 * as it was; the strings of the results and the message are anchored in the
 * registry until the next call. */
LUA_API int xlua_core_call(lua_State *L, xlua_core_call_t *c) {
	xlua_core_t *xc = xlua_core_of(L);
	xlua_core_entry entry;
	call_args a;
	int top;
	int status, outermost;
	size_t len = 0;
	c->instructions = 0;
	c->error = NULL;
	c->traceback = NULL;
	c->nresults = 0;
	c->error_len = 0;
	c->traceback_len = 0;
	c->error_ref = 0;
	c->flags = 0;
	if (!is_sandbox(L)) {
		return XLUA_ERRCONFIG;
	}
	L = active_thread(L);  /* from a host function: its thread */
	top = lua_gettop(L);
	a.c = c;
	a.status = LUA_OK;
	outermost = xc->depth == 0;
	if (outermost) {
		lua_pushboolean(L, 0);
		lua_rawsetp(L, LUA_REGISTRYINDEX, &xlua_core_hosterr_key);  /* an existing key: no allocation */
	}
	xlua_core_enter(L, &entry);
	lua_pushcfunction(L, call_body);
	lua_pushlightuserdata(L, &a);
	status = lua_pcall(L, 1, 1, 0);
	if (status == LUA_OK) {
		status = a.status;
	}
	if (status != LUA_OK) {
		if (lua_type(L, -1) != LUA_TSTRING) {
			lua_pushboolean(L, 0);
		}
		lua_rawsetp(L, LUA_REGISTRYINDEX, &msg_key);  /* an existing key: no allocation */
	}
	xlua_core_leave(&entry);
	if (outermost && xc->limited) {
		c->instructions = xc->last_instructions;
	}
	if (status != LUA_OK) {
		int stop = xc->depth > 0 ? xc->stop : xc->last_stop;
		if (stop != 0) {
			status = stop;
		}
		c->nresults = 0;
		if (lua_rawgetp(L, LUA_REGISTRYINDEX, &msg_key) == LUA_TSTRING) {
			c->error = lua_tolstring(L, -1, &len);
			c->error_len = (int32_t)len;
			lua_rawgetp(L, LUA_REGISTRYINDEX, &xlua_core_hosterr_key);
			if (status == XLUA_ERRRUN && lua_rawequal(L, -1, -2)) {
				c->flags |= XLUA_CALLF_HOST_ERROR;  /* the host function's error reached C# unchanged */
				lua_pushboolean(L, 0);
				lua_rawsetp(L, LUA_REGISTRYINDEX, &xlua_core_hosterr_key);
			}
			lua_pop(L, 1);
		}
		if ((status == XLUA_ERRRUN || status == XLUA_LIMIT_INSTR || status == XLUA_LIMIT_TIME
		     || status == XLUA_INTERRUPTED)
		    && lua_rawgetp(L, LUA_REGISTRYINDEX, &trace_key) == LUA_TSTRING) {
			c->traceback = lua_tolstring(L, -1, &len);
			c->traceback_len = (int32_t)len;
		}
	}
	lua_settop(L, top);
	return status;
}

/* ---- handles (LuaTableRef, LuaFunctionRef, LuaKey, LuaBufferRef; milestone 8) ---- */

typedef struct handle_args {
	xlua_value *v;
	int32_t op;
	int top;     /* the caller's top: a host function's arguments are at 1..top */
	int status;  /* of a failure the body found itself */
} handle_args;

/* One step of an iteration: the next key and value after the key that
 * iterator 'it' (at index it, its handle r) holds, both anchored in it until
 * the next step; they come to C# as borrowed handles of the iterator. */
static void next_body(lua_State *L, xlua_value *v, int t, int it, int32_t r, int ipairs) {
	lua_rawgeti(L, it, 1);  /* the key of the last step */
	if (lua_touserdata(L, -1) == &iter_start) {
		lua_pop(L, 1);
		if (ipairs) {
			lua_pushinteger(L, 0);
		}
		else {
			lua_pushnil(L);
		}
	}
	if (ipairs) {
		lua_Integer k = lua_tointeger(L, -1);
		if (k == LUA_MAXINTEGER) {
			v[1].type = XLUA_T_NIL;
			return;
		}
		lua_pop(L, 1);
		lua_pushinteger(L, k + 1);
		if (lua_rawgeti(L, t, k + 1) == LUA_TNIL) {
			v[1].type = XLUA_T_NIL;
			return;
		}
	}
	else if (!lua_next(L, t)) {  /* "invalid key to 'next'" raises */
		lua_pushboolean(L, 0);
		lua_rawseti(L, it, 2);  /* the last value is no longer kept alive */
		v[1].type = XLUA_T_NIL;
		return;
	}
	lua_pushvalue(L, -2);
	lua_rawseti(L, it, 1);  /* existing slots: no allocation */
	lua_pushvalue(L, -1);
	lua_rawseti(L, it, 2);
	to_value(L, it + 1, &v[1], -2 * r);
	to_value(L, it + 2, &v[2], -2 * r - 1);
}

static void handle_body(lua_State *L, void *ud) {
	handle_args *a = (handle_args *)ud;
	xlua_value *v = a->v;
	int t = a->top + 1;
	int32_t h = (int32_t)v[0].u.i;
	switch (a->op) {
	case XLUA_HANDLE_GET: case XLUA_HANDLE_SET: case XLUA_HANDLE_LEN: case XLUA_HANDLE_NEXT: case XLUA_HANDLE_INEXT:
		if (xlua_core_push_ref(L, h) != LUA_TTABLE) {  /* t */
			break;
		}
		if (a->op == XLUA_HANDLE_LEN) {
			v[2].type = XLUA_T_INT;
			v[2].len = 0;
			v[2].u.i = (int64_t)lua_rawlen(L, t);
			return;
		}
		if (a->op >= XLUA_HANDLE_NEXT) {
			int32_t r = v[3].u.i > LUA_RIDX_LAST && v[3].u.i <= INT32_MAX / 2 ? (int32_t)v[3].u.i : 0;
			if (r == 0 || xlua_core_push_ref(L, r) != LUA_TTABLE) {
				if (!v[3].len) {
					break;
				}
				lua_settop(L, t);
				lua_createtable(L, 2, 0);  /* t + 1: a new iterator */
				r = 0;
			}
			if (v[3].len) {  /* (re)start */
				lua_pushlightuserdata(L, &iter_start);
				lua_rawseti(L, t + 1, 1);
				lua_pushboolean(L, 0);
				lua_rawseti(L, t + 1, 2);
				if (r == 0) {
					lua_pushvalue(L, t + 1);
					r = luaL_ref(L, LUA_REGISTRYINDEX);
					v[3].u.i = r;
				}
			}
			next_body(L, v, t, t + 1, r, a->op == XLUA_HANDLE_INEXT);
			return;
		}
		{
			int pushed = xlua_core_push_value(L, &v[1]);
			if (pushed > 0 && a->op == XLUA_HANDLE_SET) {
				pushed = xlua_core_push_value(L, &v[2]);
			}
			if (pushed <= 0) {
				a->status = pushed == 0 ? XLUA_ERRHANDLE : XLUA_ERRTYPE;
				return;
			}
		}
		if (a->op == XLUA_HANDLE_SET) {
			lua_rawset(L, t);  /* a nil or NaN key, or memory, raises */
		}
		else {
			lua_rawget(L, t);
			to_value(L, t + 1, &v[2], (int32_t)v[3].u.i);
		}
		return;
	case XLUA_HANDLE_NEWTABLE:
		lua_createtable(L, v[1].u.i > 0 && v[1].u.i < INT32_MAX ? (int)v[1].u.i : 0, v[1].len > 0 ? (int)v[1].len : 0);
		to_value(L, t, &v[0], 0);
		return;
	case XLUA_HANDLE_NEWENV:
		push_env(L);
		to_value(L, t, &v[0], 0);
		return;
	case XLUA_HANDLE_REF:
		if (v[0].len > 0) {  /* an argument of the running host function */
			if (xlua_core_of(L)->callback_L != L || v[0].len > a->top) {
				break;
			}
			lua_pushvalue(L, v[0].len);
		}
		else {
			xlua_core_push_ref(L, h);
		}
		if (lua_type(L, t) != LUA_TTABLE && lua_type(L, t) != LUA_TFUNCTION && type_of(L, t) != XLUA_T_BUFFER) {
			break;
		}
		to_value(L, t, &v[2], 0);
		return;
	case XLUA_HANDLE_KEY:
		if (v[1].type != XLUA_T_STR || v[1].len < 0) {
			a->status = XLUA_ERRTYPE;
			return;
		}
		lua_pushlstring(L, v[1].u.s, (size_t)v[1].len);
		v[2].type = XLUA_T_STR;
		v[2].len = -1;
		v[2].u.i = luaL_ref(L, LUA_REGISTRYINDEX);
		return;
	case XLUA_HANDLE_EQUAL:
		v[2].type = XLUA_T_BOOL;
		v[2].len = 0;
		v[2].u.i = xlua_core_push_ref(L, h) > LUA_TNIL && xlua_core_push_ref(L, (int32_t)v[1].u.i) > LUA_TNIL
			&& lua_rawequal(L, t, t + 1);
		return;
#if defined(XLUA_BUFFER)
	case XLUA_HANDLE_NEWBUFFER:
		if (v[1].u.i >= 0 && (uint64_t)v[1].u.i <= XLUA_BUFFER_MAX
		    && (v[2].u.s = (const char *)xlua_core_new_buffer(L, (size_t)v[1].u.i)) != NULL) {
			to_value(L, lua_gettop(L), &v[0], 0);
			return;
		}
		a->status = XLUA_ERRCONFIG;
		return;
	case XLUA_HANDLE_BUFFER: {
		size_t len = 0;
		xlua_core_push_ref(L, h);
		v[2].u.s = (const char *)xlua_core_buffer_at(L, t, &len);
		if (v[2].u.s == NULL) {
			break;
		}
		v[2].type = XLUA_T_BUFFER;
		v[2].len = (int32_t)len;  /* <= XLUA_BUFFER_MAX */
		return;
	}
#endif
	default:
		a->status = XLUA_ERRCONFIG;  /* also the buffer operations without the library */
		return;
	}
	a->status = XLUA_ERRHANDLE;
}

/* Every operation on handles (XLUA_HANDLE_*, xlua_core.h), raw and without
 * running Lua code, so it needs no limits: one protected run of the body
 * (luaD_pcall: no function call to set up), with refusals of the memory
 * limit on also inside a host function, as in the other protected exports.
 * Works on the running host function's thread, if one runs. luaD_pcall with
 * no message handler, not luaD_rawrunprotected: inside a host function that
 * a script called through xpcall, L->errfunc is the script's handler, which
 * an error of the body (a nil or NaN key) would run; and an error that
 * unwinds Lua frames must not leave L->ci, the top or allowhook behind
 * (core-luascript.md, section 8.12). The message of XLUA_ERRRUN (v[2])
 * stays valid until the next export call. */
LUA_API int xlua_core_handle(lua_State *L, int32_t op, xlua_value *v) {
	xlua_core_t *xc;
	handle_args a;
	int unprotected, overdraft, status;
	if (!is_sandbox(L)) {
		return XLUA_ERRCONFIG;
	}
	L = active_thread(L);
	xc = xlua_core_of(L);
	a.v = v;
	a.op = op;
	a.top = lua_gettop(L);
	a.status = XLUA_OK;
	if (!lua_checkstack(L, 8)) {
		return XLUA_ERRMEM;
	}
	unprotected = xc->unprotected;
	overdraft = xc->overdraft;
	xc->unprotected = 0;
	xc->overdraft = 0;
	status = luaD_pcall(L, handle_body, &a, savestack(L, L->top.p), 0);
	xc->unprotected = unprotected;
	xc->overdraft = overdraft;
	if (status == LUA_OK) {
		status = a.status;
	}
	else if (status == LUA_ERRRUN && lua_type(L, -1) == LUA_TSTRING) {
		size_t len;
		lua_rawsetp(L, LUA_REGISTRYINDEX, &msg_key);  /* an existing key: no allocation */
		lua_rawgetp(L, LUA_REGISTRYINDEX, &msg_key);
		v[2].type = XLUA_T_STR;
		v[2].u.s = lua_tolstring(L, -1, &len);
		v[2].len = (int32_t)len;
	}
	lua_settop(L, a.top);
	return status;
}
