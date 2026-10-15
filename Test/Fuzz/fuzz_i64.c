/* libFuzzer target: the uint64 library from build/i64lib.c.
 *
 * The input is split at the first NUL into two strings a and b. Both go
 * through uint64.parse, and the results are checked against strtoull,
 * through uint64.tostring (and a reparse round trip), uint64.compare, and
 * uint64.divide/remainder (q * b + r == a, errors only for b == 0). The
 * same functions are also called with the Lua numbers and raw strings a and b
 * convert to, which exercises the non-integer argument paths. */
#include <stdio.h>

#include "fuzz_common.h"

static void call(lua_State *L, const char *fn, int nargs, int nresults) {
    /* stack: args...; calls uint64[fn](args) and leaves nresults or error */
    lua_getglobal(L, "uint64");
    lua_getfield(L, -1, fn);
    lua_remove(L, -2);
    lua_insert(L, -1 - nargs);
    if (lua_pcall(L, nargs, nresults, 0) != LUA_OK) {
        lua_pop(L, 1);
        lua_pushnil(L);
        if (nresults > 1) {
            abort(); /* not used */
        }
    }
}

static int parse(lua_State *L, const char *s, uint64_t *out) {
    lua_pushstring(L, s);
    call(L, "parse", 1, 1);
    if (lua_isnil(L, -1)) {
        lua_pop(L, 1);
        return 0;
    }
    *out = (uint64_t)lua_tointeger(L, -1);
    lua_pop(L, 1);
    return 1;
}

static void push_u64(lua_State *L, uint64_t v) {
    lua_pushinteger(L, (lua_Integer)v);
}

static void check_pair(lua_State *L, uint64_t a, uint64_t b) {
    char expected[32];
    const char *got;
    uint64_t q, r, back;

    snprintf(expected, sizeof(expected), "%llu", (unsigned long long)a);
    push_u64(L, a);
    call(L, "tostring", 1, 1);
    got = lua_tostring(L, -1);
    if (got == NULL || strcmp(got, expected) != 0 || !parse(L, got, &back) || back != a) {
        abort();
    }
    lua_pop(L, 1);

    push_u64(L, a);
    push_u64(L, b);
    call(L, "compare", 2, 1);
    if (lua_tointeger(L, -1) != (a == b ? 0 : (a < b ? -1 : 1))) {
        abort();
    }
    lua_pop(L, 1);

    push_u64(L, a);
    push_u64(L, b);
    call(L, "divide", 2, 1);
    if (lua_isnil(L, -1) != (b == 0)) {
        abort();
    }
    q = (uint64_t)lua_tointeger(L, -1);
    lua_pop(L, 1);
    push_u64(L, a);
    push_u64(L, b);
    call(L, "remainder", 2, 1);
    if (lua_isnil(L, -1) != (b == 0)) {
        abort();
    }
    r = (uint64_t)lua_tointeger(L, -1);
    lua_pop(L, 1);
    if (b != 0 && (q * b + r != a || r >= b)) {
        abort();
    }
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    char *a, *b;
    const char *nul;
    uint64_t va, vb;
    lua_State *L;
    static const char *const fns[] = {"tostring", "compare", "divide", "remainder", "parse"};
    size_t i;

    if (size > 256) {
        return -1;
    }
    nul = memchr(data, 0, size);
    a = strndup((const char *)data, nul ? (size_t)(nul - (const char *)data) : size);
    b = strndup(nul ? nul + 1 : "", nul ? size - (size_t)(nul - (const char *)data) - 1 : 0);
    L = fuzz_new_state();

    if (parse(L, a, &va) && parse(L, b, &vb)) {
        if (va != strtoull(a, NULL, 0) || vb != strtoull(b, NULL, 0)) {
            abort();
        }
        check_pair(L, va, vb);
        check_pair(L, vb, va);
    }
    /* Floats, numeric strings and other conversions of the raw text. */
    for (i = 0; i < sizeof(fns) / sizeof(fns[0]); i++) {
        if (lua_stringtonumber(L, a) == 0) {
            lua_pushstring(L, a);
        }
        if (lua_stringtonumber(L, b) == 0) {
            lua_pushstring(L, b);
        }
        call(L, fns[i], 2, 1);
        lua_settop(L, 0);
    }
    lua_close(L);
    free(a);
    free(b);
    return 0;
}
