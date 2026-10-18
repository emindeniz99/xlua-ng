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
 *    xlua_core_newenv, every required module) with shallow copies of the
 *    libraries, _G pointing to itself, its own load, and the shared locked
 *    metatable { __index = B, __metatable = false }. */

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

/* Pushes a new environment (section 4). */
static void push_env(lua_State *L) {
	lua_createtable(L, 0, 8);
	lua_rawgetp(L, LUA_REGISTRYINDEX, &libs_key);
	lua_pushnil(L);
	while (lua_next(L, -2)) {  /* E, libs, name, library */
		push_copy(L);
		lua_remove(L, -2);
		lua_pushvalue(L, -2);
		lua_insert(L, -2);
		lua_rawset(L, -5);
	}
	lua_pop(L, 1);
	lua_pushvalue(L, -1);
	lua_setfield(L, -2, LUA_GNAME);
	lua_pushvalue(L, -1);
	lua_pushcclosure(L, core_load, 1);
	lua_setfield(L, -2, "load");
	lua_rawgetp(L, LUA_REGISTRYINDEX, &envmt_key);
	lua_setmetatable(L, -2);
}

/* Pushes the environment of a handle: 0 is the default environment, a
 * positive one a registry reference from xlua_core_newenv. Returns 0 (and
 * pushes nothing) for anything else. Non-raising. */
static int push_handle(lua_State *L, int32_t env) {
	if (env == 0) {
		lua_rawgetp(L, LUA_REGISTRYINDEX, &globals_key);
		return 1;
	}
	if (env < 0) {
		return 0;
	}
	if (lua_rawgeti(L, LUA_REGISTRYINDEX, env) == LUA_TTABLE && lua_getmetatable(L, -1)) {
		int is_env;
		lua_rawgetp(L, LUA_REGISTRYINDEX, &envmt_key);
		is_env = lua_rawequal(L, -1, -2);
		lua_pop(L, 2);
		if (is_env) {
			return 1;
		}
	}
	lua_pop(L, 1);
	return 0;
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
	push_env(L);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &globals_key);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &base_key);  /* last: the state is a sandbox now */
	return 0;
}

/* A state for LuaScript: xlua_core_newstate's allocator and seed, then the
 * libraries of cfg->libs (XLUA_LUALIBS_* bits; 0 is every safe library this
 * build contains) and the sandbox. cfg->sandbox must be 1: the plain globals
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
	args.libs = cfg->libs != 0 ? cfg->libs : built;
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

static int newenv_body(lua_State *L) {
	int32_t *env = (int32_t *)lua_touserdata(L, 1);
	push_env(L);
	*env = luaL_ref(L, LUA_REGISTRYINDEX);
	return 0;
}

/* Runs a protected body with one light userdata argument as a memory entry;
 * returns XLUA_OK or XLUA_ERRMEM, the only error the bodies raise. */
static int protected_body(lua_State *L, lua_CFunction body, void *ud) {
	xlua_core_entry entry;
	int status;
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

/* A new environment over the base; *env is its handle, released with
 * xlua_core_unref. */
LUA_API int xlua_core_newenv(lua_State *L, int32_t *env) {
	*env = 0;
	if (!is_sandbox(L)) {
		return XLUA_ERRCONFIG;
	}
	return protected_body(L, newenv_body, env);
}

/* Releases an environment handle of xlua_core_newenv. Returns 1, or 0 for
 * anything that is not one (the default environment 0 included). Non-raising.
 * Milestone 8 extends it to the other handles. */
LUA_API int xlua_core_unref(lua_State *L, int32_t env) {
	if (env <= 0 || !is_sandbox(L) || !push_handle(L, env)) {
		return 0;
	}
	lua_pop(L, 1);
	luaL_unref(L, LUA_REGISTRYINDEX, env);
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

/* The message handler of xlua_core_call: the message (or a description of a
 * non-string error) as the new error value, as lua.c's msghandler makes it,
 * and the traceback in the registry (trace_key), so that C# gets the two
 * apart (LuaException.LuaStackTrace). The limit is raised by the error
 * reserve meanwhile; if the handler fails, the entry restores it. */
static int call_msgh(lua_State *L) {
	xlua_core_t *xc = xlua_core_of(L);
	size_t reserve = xc->reserve;
	int msg = 1;
	xc->reserve = XLUA_CORE_ERROR_RESERVE;
	if (lua_tostring(L, 1) == NULL) {  /* is error object not a string? */
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

/* Copies the result at index idx into *out. A string is also anchored in the
 * table at index 2, at *nstr + 1, so that its bytes outlive the stack. */
static void get_result(lua_State *L, int idx, xlua_value *out, int *nstr) {
	size_t len;
	out->len = 0;
	out->u.i = 0;
	switch (lua_type(L, idx)) {
	case LUA_TNIL:
		out->type = XLUA_T_NIL;
		break;
	case LUA_TBOOLEAN:
		out->type = XLUA_T_BOOL;
		out->u.i = lua_toboolean(L, idx);
		break;
	case LUA_TNUMBER:
		if (lua_isinteger(L, idx)) {
			out->type = XLUA_T_INT;
			out->u.i = (int64_t)lua_tointeger(L, idx);
		}
		else {
			out->type = XLUA_T_NUM;
			out->u.n = (double)lua_tonumber(L, idx);
		}
		break;
	case LUA_TSTRING:
		out->type = XLUA_T_STR;
		out->u.s = lua_tolstring(L, idx, &len);
		if (len > (size_t)INT32_MAX) {
			luaL_error(L, "result %d is a string of more than 2 GB", idx - 4);
		}
		out->len = (int32_t)len;
		lua_pushvalue(L, idx);
		lua_rawseti(L, 2, ++*nstr);
		break;
	case LUA_TTABLE:
		out->type = XLUA_T_TABLE;
		break;
	case LUA_TFUNCTION:
		out->type = XLUA_T_FUNC;
		break;
	default:
		out->type = XLUA_T_OTHER;
		break;
	}
}

/* Pushes the arguments of c; 0 with a message pushed for one that cannot be. */
static int push_args(lua_State *L, call_args *a) {
	const xlua_core_call_t *c = a->c;
	int i;
	if (c->nargs < 0 || (c->nargs > 0 && c->args == NULL) || c->nargs > INT32_MAX - 2 || !lua_checkstack(L, c->nargs + 2)) {
		call_fail(L, a, XLUA_ERRSTACK, "too many arguments");
		return 0;
	}
	for (i = 0; i < c->nargs; i++) {
		const xlua_value *v = &c->args[i];
		if (v->type == XLUA_T_NIL) {
			lua_pushnil(L);
		}
		else if (v->type == XLUA_T_BOOL) {
			lua_pushboolean(L, v->u.i != 0);
		}
		else if (v->type == XLUA_T_INT) {
			lua_pushinteger(L, (lua_Integer)v->u.i);
		}
		else if (v->type == XLUA_T_NUM) {
			lua_pushnumber(L, (lua_Number)v->u.n);
		}
		else if (v->type == XLUA_T_STR && v->len >= 0) {
			lua_pushlstring(L, v->u.s, (size_t)v->len);
		}
		else {
			lua_pushfstring(L, "argument %d cannot be passed (type %d)", i + 1, (int)v->type);
			a->status = XLUA_ERRTYPE;
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
	if (!push_handle(L, c->env)) {  /* 3 */
		return call_fail(L, a, XLUA_ERRHANDLE, "invalid environment handle");
	}
	lua_pushcfunction(L, call_msgh);  /* 4 */
	if (c->kind == XLUA_CALL_GLOBAL) {
		if (!push_global(L, a, c->name != NULL ? c->name : "", c->name != NULL ? len_of(c->name_len) : 0)) {  /* 5 */
			return 1;
		}
	}
	else if (c->kind == XLUA_CALL_CHUNK) {
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
		return call_fail(L, a, XLUA_ERRCONFIG, "this kind of call is not supported yet");
	}
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
		return 1;
	}
	n = lua_gettop(L) - 4;
	for (i = 0; i < n && i < c->results_cap && c->results != NULL; i++) {
		get_result(L, 5 + i, &c->results[i], &nstr);
	}
	c->nresults = n;
	return 0;
}

/* One entry from C# (core-luascript.md, sections 2.5 to 2.7 and 3.5): calls
 * the function c->name of an environment (XLUA_CALL_GLOBAL), or loads the text
 * chunk c->code as "@" + c->name into it and runs it (XLUA_CALL_CHUNK; with
 * c->mode XLUA_CORE_MODE_BINARY also the host's own precompiled chunks, in a
 * build with XLUA_TRUSTED_BYTECODE only, else XLUA_ERRCONFIG), with
 * the arguments of c, as one entry with the state's limits. Returns XLUA_OK
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
	int top = lua_gettop(L);
	int status, outermost;
	size_t len = 0;
	c->instructions = 0;
	c->error = NULL;
	c->traceback = NULL;
	c->nresults = 0;
	c->error_len = 0;
	c->traceback_len = 0;
	c->error_ref = 0;
	if (!is_sandbox(L)) {
		return XLUA_ERRCONFIG;
	}
	a.c = c;
	a.status = LUA_OK;
	outermost = xc->depth == 0;
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
