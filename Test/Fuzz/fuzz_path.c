/* libFuzzer target: dotted-path table access in build/xlua.c, used by
 * LuaTable.GetInPath/SetInPath (xlua_pgettable_bypath, xlua_psettable_bypath),
 * plus the single-key and global helpers LuaTable and LuaEnv use
 * (xlua_pgettable, xlua_psettable, xlua_getglobal, xlua_setglobal).
 *
 * The input is a path string (up to the first NUL). It is set on a table
 * holding nested tables, then read back. Both calls run under lua_pcall in
 * xLua, so errors are allowed; the checks are that the stack stays balanced
 * and that a successful set followed by a get returns the stored value.
 *
 * Bytes after the first NUL, up to the next one, are a key. It is set and
 * read with the single-key helpers on the table, on a table whose
 * metamethods raise, on a number and on a string, and as a global name; the
 * path is also used on the number and the string. Inputs without a NUL run
 * the path checks only. */
#include "fuzz_common.h"

LUA_API int xlua_pgettable_bypath(lua_State *L, int idx, const char *path);
LUA_API int xlua_psettable_bypath(lua_State *L, int idx, const char *path);
LUA_API int xlua_pgettable(lua_State *L, int idx);
LUA_API int xlua_psettable(lua_State *L, int idx);
LUA_API int xlua_getglobal(lua_State *L, const char *name);
LUA_API int xlua_setglobal(lua_State *L, const char *name);

static const char setup[] =
    /* No aliasing: a set through one path must not replace a table that the
     * same path traverses, or reading it back legitimately fails. e raises
     * from both metamethods, so a set or get through it fails. */
    "local e = setmetatable({}, {__index = function(t, k) error('index ' .. tostring(k)) end,\n"
    "                            __newindex = function(t, k, v) error('newindex ' .. tostring(k)) end})\n"
    "return {a = {b = {c = 1}}, [''] = {['']= {}}, x = 1, ['1'] = {}, e = e, s = 'str'}\n";

/* Runs the single-key helpers on the value of field `name` of the table at 1
 * (the table itself when name is NULL); returns whether the set succeeded. */
static int key_ops(lua_State *L, const char *name, const char *key, int set_must, int get_must) {
    int set_ok, get_ok;
    lua_settop(L, 1);
    if (name != NULL) {
        lua_getfield(L, 1, name);
    } else {
        lua_pushvalue(L, 1);
    }
    lua_pushstring(L, key);
    lua_pushliteral(L, "key-value");
    set_ok = xlua_psettable(L, 2) == LUA_OK; /* pops key and value */
    if (lua_gettop(L) != (set_ok ? 2 : 3) || (set_must >= 0 && set_ok != set_must)) {
        abort();
    }
    lua_settop(L, 2);
    lua_pushstring(L, key);
    get_ok = xlua_pgettable(L, 2) == LUA_OK; /* replaces the key with the result */
    if (lua_gettop(L) != 3 || (get_must >= 0 && get_ok != get_must)) {
        abort();
    }
    if (set_ok && name == NULL && (!get_ok || !lua_isstring(L, 3) || strcmp(lua_tostring(L, 3), "key-value") != 0)) {
        abort();
    }
    lua_settop(L, 1);
    return set_ok;
}

/* The path on a value that is not a table: a number cannot be indexed, a
 * string reads through the string library and cannot be written. */
static void path_on(lua_State *L, const char *name, const char *path, int get_must) {
    lua_settop(L, 1);
    lua_getfield(L, 1, name);
    lua_pushliteral(L, "path-value");
    if (xlua_psettable_bypath(L, 2, path) == LUA_OK || lua_gettop(L) != 3) {
        abort();
    }
    lua_settop(L, 2);
    if ((xlua_pgettable_bypath(L, 2, path) == LUA_OK) != get_must || lua_gettop(L) != 3) {
        abort();
    }
    lua_settop(L, 1);
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    char *path, *key = NULL;
    const uint8_t *nul;
    lua_State *L;
    if (size > 512) {
        return -1;
    }
    path = strndup((const char *)data, size);
    nul = memchr(data, 0, size);
    if (nul != NULL) {
        key = strndup((const char *)nul + 1, size - (size_t)(nul + 1 - data));
    }
    L = fuzz_new_state();
    if (luaL_loadstring(L, setup) != LUA_OK || lua_pcall(L, 0, 1, 0) != LUA_OK) {
        abort();
    }
    lua_pushliteral(L, "fuzz-value");
    int set_ok = xlua_psettable_bypath(L, 1, path) == LUA_OK; /* pops the value */
    if (lua_gettop(L) != (set_ok ? 1 : 2)) {
        abort();
    }
    lua_settop(L, 1);
    int get_ok = xlua_pgettable_bypath(L, 1, path) == LUA_OK;
    if (lua_gettop(L) != 2) {
        abort();
    }
    if (set_ok && (!get_ok || !lua_isstring(L, 2) || strcmp(lua_tostring(L, 2), "fuzz-value") != 0)) {
        abort();
    }
    lua_settop(L, 1);

    if (key != NULL) {
        /* The fields may have been replaced by the path set above. */
        lua_getfield(L, 1, "x");
        int x_is_number = lua_type(L, -1) == LUA_TNUMBER;
        lua_getfield(L, 1, "s");
        int s_is_string = lua_type(L, -1) == LUA_TSTRING && strcmp(lua_tostring(L, -1), "str") == 0;
        lua_getfield(L, 1, "e");
        int e_raises = lua_type(L, -1) == LUA_TTABLE && lua_getmetatable(L, -1);
        lua_settop(L, 1);
        if (x_is_number) {
            key_ops(L, "x", key, 0, 0);
            path_on(L, "x", path, 0);
        }
        if (s_is_string) {
            key_ops(L, "s", key, 0, 1);
            path_on(L, "s", path, 1);
        }
        if (e_raises) {
            key_ops(L, "e", key, 0, 0);
        }
        key_ops(L, NULL, key, 1, 1);
        /* A global of any name: set, then read back. */
        lua_pushliteral(L, "global-value");
        if (xlua_setglobal(L, key) != LUA_OK || lua_gettop(L) != 1) {
            abort();
        }
        if (xlua_getglobal(L, key) != LUA_OK || lua_gettop(L) != 2 || !lua_isstring(L, 2) ||
            strcmp(lua_tostring(L, 2), "global-value") != 0) {
            abort();
        }
    }
    lua_close(L);
    free(path);
    free(key);
    return 0;
}
