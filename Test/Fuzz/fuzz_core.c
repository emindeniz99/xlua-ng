/* libFuzzer target: LuaScript's sandbox (build/xlua_core_sandbox.c,
 * docs/design/core-luascript.md section 7.1) on the core preset's sources:
 * each input is a text chunk for xlua_core_call in a fresh sandbox state with
 * a 1 MB memory limit and a 100,000-instruction limit, the C budget of the
 * string and table libraries on, and require through a resolver that returns
 * the module name as the module's text (an empty name is not found, a name
 * starting with '-' fails). Unlike fuzz_lua.c, patterns and load are in: the
 * limits and the sandbox are what is tested.
 *
 * The first byte of the input picks the options: bit 0 a new environment
 * instead of the default one, bit 1 require, bit 2 a time limit as well, bit
 * 3 the instruction limit in small slices, bit 4 no buffer library (the
 * state opens LuaLibs.Buffer otherwise, milestone 10). After the chunk, the
 * global function f (if the chunk defined one) is called with the chunk's
 * results that can be passed back (nil, booleans, numbers, strings and
 * buffers, whose handles are released afterwards).
 *
 * Oracles: no crash or sanitizer report; every entry returns (a libFuzzer
 * timeout is a limit bug); the status is one xlua_core_call documents; the
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
LUA_API int xlua_core_call(lua_State *L, xlua_core_call_t *c);
LUA_API void xlua_core_set_resolver(xlua_core_resolver fn);
LUA_API int xlua_core_set_require(lua_State *L, int32_t enabled);
LUA_API int xlua_core_unref(lua_State *L, int32_t env);
LUA_API void *xlua_core_tobuffer(lua_State *L, int32_t ref, int32_t *len);

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
    int32_t status, env = 0, i, n;
    xlua_core_call_t c;
    xlua_value results[4];
    int32_t handles[8];
    int nhandles = 0;
    volatile char touch = 0;
    lua_State *L;
    unsigned flags;
    if (size == 0 || size > 65536) {
        return 0;
    }
    flags = data[0];
    memset(&cfg, 0, sizeof(cfg));
    cfg.memory_limit = FUZZ_CORE_MEMORY;
    cfg.sandbox = 1;
#if defined(XLUA_BUFFER)
    if (!(flags & 16)) {
        cfg.libs = XLUA_LUALIBS_SAFE | XLUA_LUALIBS_BUFFER;
    }
#endif
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
    memset(&c, 0, sizeof(c));
    c.kind = XLUA_CALL_CHUNK;
    c.env = env;
    c.code = (const char *)data + 1;
    c.code_len = (int32_t)(size - 1);
    c.name = "fuzz";
    c.name_len = 4;
    c.results = results;
    c.results_cap = 4;
    for (i = 0; i < 2; i++) {
        status = xlua_core_call(L, &c);
        switch (status) {
        case XLUA_OK: case XLUA_ERRRUN: case XLUA_ERRSYNTAX: case XLUA_ERRMEM: case XLUA_ERRERR:
        case XLUA_LIMIT_INSTR: case XLUA_LIMIT_TIME:
            break;
        default:
            abort();
        }
        if (c.error != NULL && c.error_len > 0) {
            touch ^= c.error[c.error_len - 1];
        }
        if (status != XLUA_OK) {
            break;
        }
        n = c.nresults < 4 ? c.nresults : 4;
        for (int32_t j = 0; j < n; j++) {
            if (results[j].type == XLUA_T_STR && results[j].len > 0) {
                touch ^= results[j].u.s[results[j].len - 1];  /* the anchored bytes are readable */
            }
            else if (results[j].type == XLUA_T_BUFFER) {
                int32_t len = -1;
                const char *p = (const char *)xlua_core_tobuffer(L, (int32_t)results[j].u.i, &len);
                if (p == NULL || len != results[j].len) {
                    abort();  /* a buffer result is an owning handle with its length */
                }
                if (len > 0) {
                    touch ^= p[len - 1];
                }
                handles[nhandles++] = (int32_t)results[j].u.i;  /* passed back to f, released at the end */
            }
            else if (results[j].type > XLUA_T_STR) {
                results[j].type = XLUA_T_NIL;  /* no handles yet */
            }
        }
        c.kind = XLUA_CALL_GLOBAL;
        c.name = "f";
        c.name_len = 1;
        c.args = results;
        c.nargs = n;
    }
    for (i = 0; i < nhandles; i++) {
        if (xlua_core_unref(L, handles[i]) != 1) {
            abort();
        }
    }
    xlua_core_get_stats(L, &stats);
    if (stats.memory_peak > FUZZ_CORE_MEMORY + (int64_t)XLUA_CORE_ERROR_RESERVE || stats.overdrafts != 0) {
        abort();
    }
    xlua_core_close(L);
    return 0;
}
