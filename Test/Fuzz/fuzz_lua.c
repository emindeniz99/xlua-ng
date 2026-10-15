/* libFuzzer target: run a text chunk through xLua's loader path, the way
 * LuaEnv.LoadString/DoString does with an env table: xluaL_loadbuffer, then
 * xLua's lua_setfenv to install a sandbox, then lua_pcall.
 *
 * The sandbox exposes the xLua surface (uint64.*, xlua.genaccessor,
 * xlua.structclone and struct userdata from newstruct(size)) plus base,
 * string, table, math and coroutine functions. Left out: io, os, package,
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
    "  'collectgarbage','uint64','newstruct'}) do env[k] = _G[k] end\n"
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

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    lua_State *L;
    if (size > 0 && data[0] == LUA_SIGNATURE[0]) {
        return -1; /* binary chunk: not a supported untrusted input */
    }
    L = fuzz_new_state();
    lua_pushcfunction(L, newstruct);
    lua_setglobal(L, "newstruct");
    if (luaL_loadstring(L, sandbox_setup) != LUA_OK || lua_pcall(L, 0, 1, 0) != LUA_OK) {
        abort(); /* harness bug */
    }
    if (xluaL_loadbuffer(L, (const char *)data, (int)size, "=fuzz") == LUA_OK) {
        lua_pushvalue(L, -2);
        lua_setfenv(L, -2);
        (void)lua_pcall(L, 0, 0, 0);
    }
    lua_close(L);
    return 0;
}
