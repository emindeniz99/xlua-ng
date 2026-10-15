/* libFuzzer target: dotted-path table access in build/xlua.c, used by
 * LuaTable.GetInPath/SetInPath (xlua_pgettable_bypath, xlua_psettable_bypath).
 *
 * The input is a path string (up to the first NUL). It is set on a table
 * holding nested tables, then read back. Both calls run under lua_pcall in
 * xLua, so errors are allowed; the checks are that the stack stays balanced
 * and that a successful set followed by a get returns the stored value. */
#include "fuzz_common.h"

LUA_API int xlua_pgettable_bypath(lua_State *L, int idx, const char *path);
LUA_API int xlua_psettable_bypath(lua_State *L, int idx, const char *path);

static const char setup[] =
    /* No aliasing: a set through one path must not replace a table that the
     * same path traverses, or reading it back legitimately fails. */
    "return {a = {b = {c = 1}}, [''] = {['']= {}}, x = 1, ['1'] = {}}\n";

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    char *path;
    lua_State *L;
    if (size > 512) {
        return -1;
    }
    path = strndup((const char *)data, size);
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
    lua_close(L);
    free(path);
    return 0;
}
