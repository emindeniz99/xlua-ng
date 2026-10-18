/* libFuzzer target: LuaScript's sandbox (build/xlua_core_sandbox.c,
 * docs/design/core-luascript.md section 7.1) on the core preset's sources:
 * each input is a text chunk for xlua_core_run in a fresh sandbox state with
 * a 1 MB memory limit and a 100,000-instruction limit, the C budget of the
 * string and table libraries on, and require through a resolver that returns
 * the module name as the module's text (an empty name is not found, a name
 * starting with '-' fails). Unlike fuzz_lua.c, patterns and load are in: the
 * limits and the sandbox are what is tested.
 *
 * The first byte of the input picks the options: bit 0 a new environment
 * instead of the default one, bit 1 require, bit 2 a time limit as well, bit
 * 3 the instruction limit in small slices.
 *
 * Oracles: no crash or sanitizer report; every entry returns (a libFuzzer
 * timeout is a limit bug); the status is one xlua_core_run documents; the
 * peak memory stays within the limit plus the error reserve. */
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "lua.h"
#include "lstate.h"
#include "xlua_core.h"

LUA_API lua_State *xlua_core_open(const xlua_core_config *cfg, void *host, int32_t *status);
LUA_API int xlua_core_get_stats(lua_State *L, xlua_core_stats *out);
LUA_API int xlua_core_set_limits(lua_State *L, int64_t instructions, int64_t time_ns, int32_t flags);
LUA_API int xlua_core_newenv(lua_State *L, int32_t *env);
LUA_API int xlua_core_run(lua_State *L, int32_t env, const char *code, int32_t code_len, const char *name,
                          int32_t name_len, const char **out, int32_t *out_len);
LUA_API void xlua_core_set_resolver(xlua_core_resolver fn);
LUA_API int xlua_core_set_require(lua_State *L, int32_t enabled);

#define FUZZ_CORE_MEMORY ((int64_t)1 << 20)
#define FUZZ_CORE_INSTRUCTIONS 100000

static int32_t resolver(void *host, const char *name, int32_t len, xlua_core_source *out) {
    (void)host;
    if (len == 0) {
        return 0;
    }
    out->code = name;
    out->code_len = len;
    if (name[0] == '-') {
        return -1;
    }
    return 1;
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    xlua_core_config cfg;
    xlua_core_stats stats;
    int32_t status, env = 0, out_len;
    const char *out;
    lua_State *L;
    unsigned flags;
    if (size == 0 || size > 65536) {
        return 0;
    }
    flags = data[0];
    memset(&cfg, 0, sizeof(cfg));
    cfg.memory_limit = FUZZ_CORE_MEMORY;
    cfg.sandbox = 1;
    L = xlua_core_open(&cfg, NULL, &status);
    if (L == NULL) {
        abort();  /* 1 MB is enough for a sandbox */
    }
    xlua_core_set_resolver(resolver);
    if ((flags & 1) && xlua_core_newenv(L, &env) != XLUA_OK) {
        abort();
    }
    if ((flags & 2) && xlua_core_set_require(L, 1) != XLUA_OK) {
        abort();
    }
    xlua_core_set_limits(L, FUZZ_CORE_INSTRUCTIONS, (flags & 4) ? 2000000000 : 0,
                         (flags & 8) ? XLUA_LIMITS_SMALL_SLICES : 0);
    status = xlua_core_run(L, env, (const char *)data + 1, (int32_t)(size - 1), "fuzz", 4, &out, &out_len);
    switch (status) {
    case XLUA_OK: case XLUA_ERRRUN: case XLUA_ERRSYNTAX: case XLUA_ERRMEM: case XLUA_ERRERR:
    case XLUA_LIMIT_INSTR: case XLUA_LIMIT_TIME:
        break;
    default:
        abort();
    }
    xlua_core_get_stats(L, &stats);
    if (stats.memory_peak > FUZZ_CORE_MEMORY + (int64_t)XLUA_CORE_ERROR_RESERVE || stats.overdrafts != 0) {
        abort();
    }
    xlua_core_close(L);
    return 0;
}
