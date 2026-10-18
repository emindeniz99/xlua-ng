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
static const char out_key = 0;     /* the string that xlua_core_run's *out points into */

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
	lua_pushliteral(L, "");  /* the slot exists, so replacing it allocates nothing */
	lua_rawsetp(L, LUA_REGISTRYINDEX, &out_key);
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

/* The message handler of core entries: the message (or a description of a
 * non-string error) with a traceback, as lua.c's msghandler. The limit is
 * raised by the error reserve meanwhile; if the handler fails, the entry
 * restores it. */
static int core_msgh(lua_State *L) {
	xlua_core_t *xc = xlua_core_of(L);
	size_t reserve = xc->reserve;
	const char *msg;
	xc->reserve = XLUA_CORE_ERROR_RESERVE;
	msg = lua_tostring(L, 1);
	if (msg == NULL) {  /* is error object not a string? */
		if (luaL_callmeta(L, 1, "__tostring") && lua_type(L, -1) == LUA_TSTRING) {
			msg = lua_tostring(L, -1);
		}
		else {
			msg = lua_pushfstring(L, "(error object is a %s value)", luaL_typename(L, 1));
		}
	}
	luaL_traceback(L, L, msg, 1);
	xc->reserve = reserve;
	return 1;
}

typedef struct run_args {
	int32_t env;
	const char *code;
	size_t code_len;
	const char *name;
	size_t name_len;
	const char *mode;  /* lua_load's mode */
	int status;  /* of the load and the call, when the body itself did not fail */
} run_args;

static int run_body(lua_State *L) {
	run_args *a = (run_args *)lua_touserdata(L, 1);
	luaL_Buffer b;
	if (!push_handle(L, a->env)) {  /* 2 */
		a->status = XLUA_ERRHANDLE;
		lua_pushliteral(L, "invalid environment handle");
		return 1;
	}
	luaL_buffinit(L, &b);  /* 3: the chunk name, with '@' (section 2.5) */
	luaL_addchar(&b, '@');
	luaL_addlstring(&b, a->name, a->name_len);
	luaL_pushresult(&b);
	lua_pushcfunction(L, core_msgh);  /* 4 */
	a->status = luaL_loadbufferx(L, a->code, a->code_len, lua_tostring(L, 3), a->mode);
	if (a->status != LUA_OK) {
		return 1;
	}
	lua_pushvalue(L, 2);
	if (lua_setupvalue(L, -2, 1) == NULL) {
		lua_pop(L, 1);  /* a binary chunk of a function without upvalues */
	}
	a->status = lua_pcall(L, 0, 1, 4);
	if (a->status == LUA_OK) {
		luaL_tolstring(L, -1, NULL);
	}
	return 1;
}

/* Loads code (text only) as chunk "@name" into an environment (0: the
 * default one) and runs it as one entry with the state's limits. Returns
 * XLUA_OK with *out the first result as tostring makes it, or an error status
 * (XLUA_ERRSYNTAX, XLUA_ERRRUN, XLUA_ERRMEM, XLUA_ERRERR, XLUA_LIMIT_*,
 * XLUA_INTERRUPTED, XLUA_ERRHANDLE, XLUA_ERRCONFIG for a state without the
 * sandbox) with *out the message and traceback. *out stays valid until the
 * next xlua_core_run on the state, and is NULL when the error is not a
 * string. A test and smoke entry: milestone 6's xlua_core_call, with typed
 * arguments and results, replaces it for LuaScript. */
LUA_API int xlua_core_run_mode(lua_State *L, int32_t env, const char *code, int32_t code_len,
                               const char *name, int32_t name_len, int32_t mode,
                               const char **out, int32_t *out_len);

LUA_API int xlua_core_run(lua_State *L, int32_t env, const char *code, int32_t code_len,
                          const char *name, int32_t name_len, const char **out, int32_t *out_len) {
	return xlua_core_run_mode(L, env, code, code_len, name, name_len, XLUA_CORE_MODE_TEXT, out, out_len);
}

/* A refusal of xlua_core_run_mode before anything runs; the text is static. */
static int run_refused(const char *msg, const char **out, int32_t *out_len) {
	*out = msg;
	*out_len = (int32_t)strlen(msg);
	return XLUA_ERRCONFIG;
}

/* xlua_core_run with the kinds of chunk the host accepts for this code
 * (XLUA_CORE_MODE_* bits; xlua_core_run is XLUA_CORE_MODE_TEXT). Binary
 * chunks are for the host's own precompiled scripts only: Lua does not check
 * bytecode, so a crafted chunk can corrupt memory, and scripts never load it
 * (the sandbox's load stays text only). A build without CMake
 * XLUA_TRUSTED_BYTECODE refuses XLUA_CORE_MODE_BINARY with XLUA_ERRCONFIG and
 * a message in *out, as it refuses a mode without either bit. */
LUA_API int xlua_core_run_mode(lua_State *L, int32_t env, const char *code, int32_t code_len,
                               const char *name, int32_t name_len, int32_t mode,
                               const char **out, int32_t *out_len) {
	xlua_core_t *xc = xlua_core_of(L);
	xlua_core_entry entry;
	run_args a;
	int top = lua_gettop(L);
	int status;
	size_t len = 0;
	*out = NULL;
	*out_len = 0;
	if (!is_sandbox(L)) {
		return XLUA_ERRCONFIG;
	}
	switch (mode) {
	case XLUA_CORE_MODE_TEXT: a.mode = "t"; break;
	case XLUA_CORE_MODE_BINARY: a.mode = "b"; break;
	case XLUA_CORE_MODE_TEXT | XLUA_CORE_MODE_BINARY: a.mode = "bt"; break;
	default: return run_refused("invalid mode: use XLUA_CORE_MODE_TEXT, XLUA_CORE_MODE_BINARY or both", out, out_len);
	}
#if !defined(XLUA_TRUSTED_BYTECODE)
	if (mode & XLUA_CORE_MODE_BINARY) {
		return run_refused("binary chunks are not supported in this build (XLUA_TRUSTED_BYTECODE is off)", out, out_len);
	}
#endif
	a.env = env;
	a.code = code;
	a.code_len = len_of(code_len);
	a.name = name;
	a.name_len = len_of(name_len);
	a.status = LUA_OK;
	xlua_core_enter(L, &entry);
	lua_pushcfunction(L, run_body);
	lua_pushlightuserdata(L, &a);
	status = lua_pcall(L, 1, 1, 0);
	if (status == LUA_OK) {
		status = a.status;
	}
	lua_rawsetp(L, LUA_REGISTRYINDEX, &out_key);  /* an existing key: no allocation */
	xlua_core_leave(&entry);
	if (status != LUA_OK) {
		int stop = xc->depth > 0 ? xc->stop : xc->last_stop;
		if (stop != 0) {
			status = stop;
		}
	}
	if (lua_rawgetp(L, LUA_REGISTRYINDEX, &out_key) == LUA_TSTRING) {
		*out = lua_tolstring(L, -1, &len);
		*out_len = (int32_t)len;
	}
	lua_settop(L, top);
	return status;
}
