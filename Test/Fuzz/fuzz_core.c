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
 * state opens LuaLibs.Buffer otherwise, milestone 10), bit 5 host functions
 * through a C dispatcher (xlua_core_register, milestone 7): echo (primitive
 * arguments back), fail (an error at the caller's position), yield (yields
 * where it can), host.many (12 results from the host's memory) and int (a
 * type error for anything but an integer). After the chunk, the global
 * function f (if the chunk defined one) is called with the chunk's results
 * (tables, functions and buffers as their owning handles, milestone 8, released
 * afterwards); a table result also gets a few raw steps of xlua_core_handle's
 * NEXT (bit 6: INEXT) and a raw GET of t[1] (bit 7: a SET of t[1]).
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
LUA_API int xlua_core_handle(lua_State *L, int32_t op, xlua_value *v);
LUA_API int xlua_core_call(lua_State *L, xlua_core_call_t *c);
LUA_API void xlua_core_set_resolver(xlua_core_resolver fn);
LUA_API int xlua_core_set_require(lua_State *L, int32_t enabled);
LUA_API void xlua_core_set_dispatcher(xlua_core_dispatch fn);
LUA_API int xlua_core_register(lua_State *L, int32_t env, const char *name, int32_t name_len, int32_t fn_id, int32_t *old_id);
LUA_API int xlua_core_unref(lua_State *L, int32_t env);

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

static xlua_value many[12];

/* The host functions of bit 4. */
static void dispatch(void *host, int32_t id, xlua_core_frame *f) {
    int32_t i;
    (void)host;
    switch (id) {
    case 1:
        for (i = 0; i < f->nargs && i < f->results_cap; i++) {
            f->results[i] = f->args[i];
            if (f->results[i].type > XLUA_T_STR) {
                f->results[i].type = XLUA_T_NIL;  /* arguments carry stack indices, not handles */
            }
        }
        f->nresults = i;
        break;
    case 2:
        f->status = XLUA_FRAME_ERROR_WHERE;
        f->error = "host error";
        f->error_len = 10;
        break;
    case 3:
        f->results[0].type = XLUA_T_INT;
        f->results[0].u.i = f->nargs;
        f->nresults = 1;
        f->status = f->yieldable ? XLUA_FRAME_YIELD : XLUA_FRAME_RETURN;
        break;
    case 4:
        for (i = 0; i < 12; i++) {
            many[i].type = XLUA_T_INT;
            many[i].u.i = i;
        }
        f->results = many;
        f->results_cap = 12;
        f->nresults = 12;
        break;
    default:
        if (f->nargs < 1 || f->args[0].type != XLUA_T_INT) {
            f->status = XLUA_FRAME_TYPEERROR;
            f->error_arg = 1;
            f->error = "number";
        }
        break;
    }
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    xlua_core_config cfg;
    xlua_core_stats stats;
    int32_t status, env = 0, i, n;
    xlua_core_call_t c;
    xlua_value results[4];
    int32_t handles[16];
    xlua_value v[4];
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
    if (flags & 1) {
        memset(v, 0, sizeof(v));
        if (xlua_core_handle(L, XLUA_HANDLE_NEWENV, v) != XLUA_OK) {
            abort();
        }
        env = (int32_t)v[0].u.i;
    }
    if ((flags & 2) && xlua_core_set_require(L, 1) != XLUA_OK) {
        abort();
    }
    if (flags & 32) {
        static const char *const names[] = {"echo", "fail", "yield", "host.many", "int"};
        int32_t old;
        xlua_core_set_dispatcher(dispatch);
        for (i = 0; i < 5; i++) {
            if (xlua_core_register(L, 0, names[i], (int32_t)strlen(names[i]), i + 1, &old) != XLUA_OK) {
                abort();
            }
        }
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
        if (c.error_ref != 0 && (status != XLUA_ERRRUN || xlua_core_unref(L, c.error_ref) != 1)) {
            abort();  /* an error value handle comes with runtime errors only */
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
                int32_t len;
                const char *p;
                memset(v, 0, sizeof(v));
                v[0].u.i = results[j].u.i;
                if (xlua_core_handle(L, XLUA_HANDLE_BUFFER, v) != XLUA_OK || v[2].len != results[j].len) {
                    abort();  /* a buffer result is an owning handle with its length */
                }
                p = v[2].u.s;
                len = v[2].len;
                if (len > 0) {
                    touch ^= p[len - 1];
                }
                handles[nhandles++] = (int32_t)results[j].u.i;  /* passed back to f, released at the end */
            }
            else if (results[j].type == XLUA_T_TABLE || results[j].type == XLUA_T_FUNC) {
                /* An owning handle: a few raw steps through a table, then passed back to f. */
                int32_t it = 0, k;
                if (results[j].u.i <= 0) {
                    abort();
                }
                handles[nhandles++] = (int32_t)results[j].u.i;
                memset(v, 0, sizeof(v));
                v[0] = results[j];
                v[3].len = 1;
                for (k = 0; k < 4 && results[j].type == XLUA_T_TABLE; k++) {
                    int st = xlua_core_handle(L, (flags & 64) ? XLUA_HANDLE_INEXT : XLUA_HANDLE_NEXT, v);
                    if (st != XLUA_OK && st != XLUA_ERRMEM) {
                        abort();  /* nothing changes the table meanwhile */
                    }
                    it = (int32_t)v[3].u.i;
                    v[3].len = 0;
                    if (st != XLUA_OK || v[1].type == XLUA_T_NIL) {
                        break;
                    }
                    if (v[1].type == XLUA_T_STR && v[1].len > 0) {
                        touch ^= v[1].u.s[v[1].len - 1];
                    }
                }
                if (it > 0 && xlua_core_unref(L, it) != 1) {
                    abort();
                }
                if (results[j].type == XLUA_T_TABLE) {
                    memset(v, 0, sizeof(v));
                    v[0] = results[j];
                    v[1].type = XLUA_T_INT;
                    v[1].u.i = 1;
                    v[2].type = XLUA_T_STR;
                    v[2].u.s = "fuzz";
                    v[2].len = 4;
                    if (xlua_core_handle(L, (flags & 128) ? XLUA_HANDLE_SET : XLUA_HANDLE_GET, v) > XLUA_ERRMEM) {
                        abort();
                    }
                    if ((flags & 128) == 0 && v[2].type > XLUA_T_STR && v[2].type != XLUA_T_OTHER
                        && (nhandles == 16 || (handles[nhandles++] = (int32_t)v[2].u.i) <= 0)) {
                        abort();
                    }
                }
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
