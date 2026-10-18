/* libFuzzer target: run a text chunk through xLua's loader path, the way
 * LuaEnv.LoadString/DoString does with an env table: xluaL_loadbuffer, then
 * xLua's lua_setfenv to install a sandbox, then lua_pcall.
 *
 * The sandbox exposes the xLua surface (uint64.*, xlua.genaccessor,
 * xlua.structclone and struct userdata from newstruct(size)), the native
 * helpers the managed bridge calls, as functions of any arguments (getpath,
 * setpath, pget, pset, getglobal, setglobal, typeid, csobj, tostruct,
 * touint, eqstr), snapshot() (xlua_memory_snapshot, so that the walk also
 * runs from inside coroutines, pcall and finalizers on whatever the chunk
 * built), plus base, string, table, math and coroutine functions. Each run
 * also ends with a snapshot of what is left. Left out: io, os, package,
 * debug, load/dofile/require (escape the sandbox), xlua.sethook (would remove
 * the instruction limit) and string pattern functions (exponential
 * backtracking in vendored Lua runs inside one C call that the limit cannot
 * interrupt). Binary chunks are skipped: Lua does not verify bytecode, so
 * loading untrusted bytecode is unsafe by design. */
#include "fuzz_common.h"

static const char sandbox_setup[] =
    "local env = {}\n"
    "for _, k in ipairs({'assert','error','ipairs','next','pairs','pcall','rawequal','rawget',\n"
    "  'rawlen','rawset','select','setmetatable','getmetatable','tonumber','tostring','type',\n"
    "  'collectgarbage','uint64','newstruct','getpath','setpath','pget','pset','getglobal','setglobal',\n"
    "  'typeid','csobj','tostruct','touint','eqstr','snapshot'}) do env[k] = _G[k] end\n"
    "env.xlua = {genaccessor = xlua.genaccessor, structclone = xlua.structclone}\n"
    "env.math = {} for k, v in pairs(math) do env.math[k] = v end\n"
    "env.table = {} for k, v in pairs(table) do env.table[k] = v end\n"
    "env.coroutine = {} for k, v in pairs(coroutine) do env.coroutine[k] = v end\n"
    "env.string = {}\n"
    "for _, k in ipairs({'byte','char','format','len','lower','upper','rep','reverse','sub'}) do\n"
    "  env.string[k] = string[k] end\n"
    "getmetatable('').__index = env.string\n"
    "env._G = env\n"
    "return env\n";

static int newstruct(lua_State *L) {
    lua_Integer size = luaL_checkinteger(L, 1);
    if (size < 0 || size > 4096) {
        return luaL_error(L, "struct size out of range");
    }
    fuzz_push_struct(L, (unsigned int)size);
    return 1;
}

LUA_API int xlua_pgettable_bypath(lua_State *L, int idx, const char *path);
LUA_API int xlua_psettable_bypath(lua_State *L, int idx, const char *path);
LUA_API int xlua_pgettable(lua_State *L, int idx);
LUA_API int xlua_psettable(lua_State *L, int idx);
LUA_API int xlua_getglobal(lua_State *L, const char *name);
LUA_API int xlua_setglobal(lua_State *L, const char *name);
LUA_API int xlua_gettypeid(lua_State *L, int idx);
LUA_API int xlua_tocsobj_safe(lua_State *L, int idx);
LUA_API void *xlua_tostruct(lua_State *L, int idx, int meta_ref);
LUA_API uint32_t xlua_touint(lua_State *L, int idx);
LUA_API int xlua_is_eq_str(lua_State *L, int idx, const char *str, int str_len);
LUA_API int xlua_memory_snapshot(lua_State *L, int max_objects, void **out, int *out_size);
LUA_API void xlua_memory_snapshot_free(void *buffer);

/* Takes a snapshot and checks that the buffer is consistent: the layout in
 * build/memory_leak_checker.c (header, 40-byte nodes, 32-byte edges, string
 * pool), every edge between existing nodes, every node reached by an edge to
 * it, every string offset inside the pool. A Lua out-of-memory error is the
 * only failure allowed. */
static int check_snapshot(lua_State *L, int max_objects) {
    void *buffer = NULL;
    int size = 0, status = xlua_memory_snapshot(L, max_objects, &buffer, &size);
    int32_t header[10];
    const unsigned char *base;
    int32_t i, nodes, edges, strings;
    if (status != 0) {
        if (status != 3 || buffer != NULL || size != 0) abort();
        return status;
    }
    if (size < 64) abort();
    memcpy(header, buffer, sizeof(header));
    nodes = header[5];
    edges = header[6];
    strings = header[7];
    if (header[0] != 0x534D4C58 || header[2] != 64 || header[3] != 40 || header[4] != 32) abort();
    if (nodes < 0 || edges < nodes || strings < 0
        || (int64_t)size != 64 + (int64_t)nodes * 40 + (int64_t)edges * 32 + strings) abort();
    if (max_objects > 0 && nodes > max_objects) abort();
    base = (const unsigned char *)buffer + 64;
    for (i = 0; i < edges; i++) {
        int32_t e[4];
        int64_t key;
        memcpy(e, base + (size_t)nodes * 40 + (size_t)i * 32, sizeof(e));
        memcpy(&key, base + (size_t)nodes * 40 + (size_t)i * 32 + 16, sizeof(key));
        if (e[0] < -1 || e[0] >= nodes || e[1] < 0 || e[1] >= nodes) abort();
        if (e[3] == 1 && (key < 0 || key + 4 > strings)) abort();
    }
    for (i = 0; i < nodes; i++) {
        int32_t n[6], to;
        memcpy(n, base + (size_t)i * 40 + 16, sizeof(n));
        if (n[5] < 0 || n[5] >= edges) abort();
        memcpy(&to, base + (size_t)nodes * 40 + (size_t)n[5] * 32 + 4, sizeof(to));
        if (to != i) abort();
        if (n[4] >= 0 && n[4] + 4 > strings) abort();
    }
    xlua_memory_snapshot_free(buffer);
    return 0;
}

/* snapshot([max_objects]): returns the status. */
static int snapshot(lua_State *L) {
    lua_Integer max = luaL_optinteger(L, 1, 0);
    lua_pushinteger(L, check_snapshot(L, max < 0 || max > 1000000 ? 0 : (int)max));
    return 1;
}

/* The helpers return a status as LuaTable does; raise it as an error. */
static int status_result(lua_State *L, int status, int nresults) {
    if (status != LUA_OK) {
        return lua_error(L);
    }
    return nresults;
}

/* getpath(t, path): LuaTable.GetInPath on any first argument. */
static int getpath(lua_State *L) {
    const char *path = luaL_checkstring(L, 2);
    lua_settop(L, 2);
    return status_result(L, xlua_pgettable_bypath(L, 1, path), 1);
}

/* setpath(t, path, v): LuaTable.SetInPath. */
static int setpath(lua_State *L) {
    const char *path = luaL_checkstring(L, 2);
    lua_settop(L, 3);
    return status_result(L, xlua_psettable_bypath(L, 1, path), 0);
}

/* pget(t, k) and pset(t, k, v): LuaTable.Get/Set with metamethods. */
static int pget(lua_State *L) {
    lua_settop(L, 2);
    return status_result(L, xlua_pgettable(L, 1), 1);
}

static int pset(lua_State *L) {
    lua_settop(L, 3);
    return status_result(L, xlua_psettable(L, 1), 0);
}

/* getglobal(name) and setglobal(name, v): LuaEnv.Global through the helpers.
 * They reach the real globals, so the name gets a "fuzz_" prefix: getglobal
 * of "os" or "load" would escape the sandbox. */
static int getglobal(lua_State *L) {
    luaL_checkstring(L, 1);
    lua_settop(L, 1);
    return status_result(L, xlua_getglobal(L, lua_pushfstring(L, "fuzz_%s", lua_tostring(L, 1))), 1);
}

static int setglobal(lua_State *L) {
    const char *name;
    luaL_checkstring(L, 1);
    lua_settop(L, 2);
    name = lua_pushfstring(L, "fuzz_%s", lua_tostring(L, 1));
    lua_insert(L, 2); /* name, prefixed name, value */
    return status_result(L, xlua_setglobal(L, name), 0);
}

/* typeid(v), csobj(v), tostruct(v, ref) and touint(v) as the bridge reads
 * arguments; eqstr(v, s) as generated enum and operator code compares. */
static int typeid_(lua_State *L) {
    lua_pushinteger(L, xlua_gettypeid(L, 1));
    return 1;
}

static int csobj(lua_State *L) {
    lua_pushinteger(L, xlua_tocsobj_safe(L, 1));
    return 1;
}

static int tostruct(lua_State *L) {
    lua_pushboolean(L, xlua_tostruct(L, 1, (int)luaL_optinteger(L, 2, 1)) != NULL);
    return 1;
}

static int touint(lua_State *L) {
    lua_pushinteger(L, (lua_Integer)xlua_touint(L, 1));
    return 1;
}

static int eqstr(lua_State *L) {
    size_t len;
    const char *s = luaL_checklstring(L, 2, &len);
    lua_pushboolean(L, xlua_is_eq_str(L, 1, s, (int)len));
    return 1;
}

static const luaL_Reg helpers[] = {
    {"newstruct", newstruct}, {"getpath", getpath}, {"setpath", setpath}, {"pget", pget},
    {"pset", pset}, {"getglobal", getglobal}, {"setglobal", setglobal}, {"typeid", typeid_},
    {"csobj", csobj}, {"tostruct", tostruct}, {"touint", touint}, {"eqstr", eqstr},
    {"snapshot", snapshot}, {NULL, NULL}};

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    lua_State *L;
    if (size > 0 && data[0] == LUA_SIGNATURE[0]) {
        return -1; /* binary chunk: not a supported untrusted input */
    }
    const luaL_Reg *reg;
    L = fuzz_new_state();
    for (reg = helpers; reg->name != NULL; reg++) {
        lua_pushcfunction(L, reg->func);
        lua_setglobal(L, reg->name);
    }
    if (luaL_loadstring(L, sandbox_setup) != LUA_OK || lua_pcall(L, 0, 1, 0) != LUA_OK) {
        abort(); /* harness bug */
    }
    if (xluaL_loadbuffer(L, (const char *)data, (int)size, "=fuzz") == LUA_OK) {
        lua_pushvalue(L, -2);
        lua_setfenv(L, -2);
        (void)lua_pcall(L, 0, 0, 0);
    }
    (void)check_snapshot(L, 0);
    lua_close(L);
    return 0;
}
