/* Drives libxlua through its C API and checks the bridge without Mono or Python.
 * Used where only a cross-compiler is available: Test/Native/ohos_check.sh runs
 * it for the OpenHarmony ABIs on Linux. Lua 5.3 and later (it calls lua_pcallk).
 * Build: cc plugin_smoke.c -L<dir> -lxlua -o plugin_smoke
 */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct lua_State lua_State;
lua_State *luaL_newstate(void);
void luaL_openlibs(lua_State *L);
int luaL_loadstring(lua_State *L, const char *s);
int lua_pcallk(lua_State *L, int nargs, int nresults, int errfunc, intptr_t ctx, void *k);
const char *lua_tolstring(lua_State *L, int idx, size_t *len);
void lua_settop(lua_State *L, int idx);
void lua_close(lua_State *L);
int xlua_get_lib_version(void);
void luaopen_xlua(lua_State *L);
int luaopen_i64lib(lua_State *L);
void *xlua_pushstruct(lua_State *L, unsigned int size, int meta_ref);
int xlua_pack_int32_t(void *p, int offset, int32_t field);
int xlua_unpack_int32_t(void *p, int offset, int32_t *field);
int xlua_pack_float3(void *p, int offset, float f1, float f2, float f3);
int xlua_unpack_float3(void *p, int offset, float *f1, float *f2, float *f3);

#define CHECK(cond) do { if (!(cond)) { fprintf(stderr, "%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #cond); exit(1); } } while (0)

static const char script[] =
    "assert(_VERSION:match('^Lua 5%.[345]$'), _VERSION)\n"
    "assert(os.execute == nil)\n"
    "assert(string.format('%5.2f', math.pi) == ' 3.14')\n"
    "assert(uint64.tostring(uint64.parse('18446744073709551615')) == '18446744073709551615')\n"
    "local name = os.tmpname()\n"
    "local f = assert(io.open(name, 'w+b'))\n"
    "assert(f:write(string.rep('x', 1000)))\n"
    "assert(f:seek('set', 10) == 10 and f:read(3) == 'xxx')\n"
    "f:close()\n"
    "assert(os.remove(name))\n"
    "local p = io.popen('echo xlua')\n"
    "assert(p:read('l') == 'xlua')\n"
    "p:close()\n"
    "local t = {}\n"
    "for i = 1, 100000 do t[i] = tostring(i) end\n"
    "collectgarbage()\n"
    "assert(#t == 100000 and t[100000] == '100000')\n";

int main(void) {
    lua_State *L = luaL_newstate();
    CHECK(L != NULL);
    CHECK(xlua_get_lib_version() == 113);
    luaL_openlibs(L);
    luaopen_xlua(L);
    luaopen_i64lib(L);
    if (luaL_loadstring(L, script) != 0 || lua_pcallk(L, 0, 0, 0, 0, NULL) != 0) {
        fprintf(stderr, "Lua error: %s\n", lua_tolstring(L, -1, NULL));
        return 1;
    }
    void *buff = xlua_pushstruct(L, 24, -2); /* LUA_NOREF: no metatable */
    int32_t i = 0;
    float x = 0, y = 0, z = 0;
    CHECK(xlua_pack_int32_t(buff, 20, -7) == 1 && xlua_unpack_int32_t(buff, 20, &i) == 1 && i == -7);
    CHECK(xlua_pack_int32_t(buff, 21, 1) == 0 && xlua_pack_int32_t(buff, -1, 1) == 0);
    CHECK(xlua_pack_float3(buff, 1, 1.5f, 2.5f, 3.5f) == 1);
    CHECK(xlua_unpack_float3(buff, 1, &x, &y, &z) == 1 && x == 1.5f && y == 2.5f && z == 3.5f);
    lua_settop(L, 0);
    lua_close(L);
    puts("XLUA_PLUGIN_SMOKE_PASS");
    return 0;
}
