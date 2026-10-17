/* libFuzzer target: the uint64 library from build/i64lib.c.
 *
 * The input is split at the first NUL into two strings a and b. Both go
 * through uint64.parse, and the results are checked against strtoull,
 * through uint64.tostring (and a reparse round trip), uint64.compare, and
 * uint64.divide/remainder (q * b + r == a, errors only for b == 0). The
 * same functions are also called with the Lua numbers and raw strings a and b
 * convert to, which exercises the non-integer argument paths.
 *
 * On LuaJIT the values are the Int64 userdata, and the target also checks the
 * LuaJIT-only helpers (uint64.band, bor, bxor, bnot, lshift, rshift, arshift,
 * idiv) and the __concat metamethod against reference code, with each operand
 * passed as an Int64, as a UInt64 (UINT_ESPECIALLY builds only) and as a Lua
 * number when the value is one exactly.
 *
 * Both values also go through the C helpers the managed bridge marshals
 * long, ulong, uint and int with (lua_pushint64/lua_toint64,
 * lua_pushuint64/lua_touint64, xlua_pushuint/xlua_touint,
 * xlua_pushinteger/xlua_tointeger), and xlua_touint is checked on the
 * number or string each raw text converts to. */
#include <stdio.h>

#include "fuzz_common.h"

#ifndef LUA_OK
#define LUA_OK 0 /* Lua 5.1 */
#endif

#include "i64lib.h"
#if LUA_VERSION_NUM == 501
LUALIB_API int lua_isinteger64(lua_State *L, int pos);
#endif
LUA_API uint32_t xlua_touint(lua_State *L, int idx);
LUA_API void xlua_pushuint(lua_State *L, uint32_t n);
LUA_API int xlua_tointeger(lua_State *L, int idx);
LUA_API void xlua_pushinteger(lua_State *L, int n);

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

/* The bits of an integer result: a Lua integer, or the Int64 userdata on LuaJIT. */
static uint64_t to_u64(lua_State *L, int idx) {
#if LUA_VERSION_NUM == 501
    if (!lua_isinteger64(L, idx)) {
        abort();
    }
    return lua_touint64(L, idx);
#else
    return (uint64_t)lua_tointeger(L, idx);
#endif
}

static int parse(lua_State *L, const char *s, uint64_t *out) {
    lua_pushstring(L, s);
    call(L, "parse", 1, 1);
    if (lua_isnil(L, -1)) {
        lua_pop(L, 1);
        return 0;
    }
    *out = to_u64(L, -1);
    lua_pop(L, 1);
    return 1;
}

static void push_u64(lua_State *L, uint64_t v) {
#if LUA_VERSION_NUM == 501
    lua_pushuint64(L, v);
#else
    lua_pushinteger(L, (lua_Integer)v);
#endif
}

/* A raw text as Lua would convert it: a number if it reads as one. */
static void push_text(lua_State *L, const char *s) {
#if LUA_VERSION_NUM == 501
    lua_pushstring(L, s);
    if (lua_isnumber(L, -1)) {
        lua_Number n = lua_tonumber(L, -1);
        lua_pop(L, 1);
        lua_pushnumber(L, n);
    }
#else
    if (lua_stringtonumber(L, s) == 0) {
        lua_pushstring(L, s);
    }
#endif
}

/* Round trips through the marshalling helpers. */
static void check_c_helpers(lua_State *L, uint64_t v) {
    lua_pushint64(L, (int64_t)v);
    if (!lua_isint64(L, -1) || lua_toint64(L, -1) != (int64_t)v || lua_touint64(L, -1) != v) {
        abort();
    }
    lua_pop(L, 1);
    lua_pushuint64(L, v);
    if (!lua_isuint64(L, -1) || lua_touint64(L, -1) != v) {
        abort();
    }
    lua_pop(L, 1);
    xlua_pushuint(L, (uint32_t)v);
    if (xlua_touint(L, -1) != (uint32_t)v) {
        abort();
    }
    lua_pop(L, 1);
    xlua_pushinteger(L, (int)(uint32_t)v);
    if (xlua_tointeger(L, -1) != (int)(uint32_t)v) {
        abort();
    }
    lua_pop(L, 1);
}

/* xlua_touint on the value at the top: an integer wraps modulo 2^32; a float
 * truncates and wraps the same way, and NaN or a value outside the 64-bit
 * range gives 0. UBSan reports an undefined float-to-integer cast. */
static void check_touint(lua_State *L) {
    uint32_t want;
#if LUA_VERSION_NUM >= 503
    if (lua_isinteger(L, -1)) {
        want = (uint32_t)lua_tointeger(L, -1);
    } else
#endif
    {
        lua_Number d = lua_tonumber(L, -1);
        want = d >= -9223372036854775808.0 && d < 9223372036854775808.0 ? (uint32_t)(int64_t)d : 0;
    }
    if (xlua_touint(L, -1) != want) {
        abort();
    }
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
    q = b == 0 ? 0 : to_u64(L, -1);
    lua_pop(L, 1);
    push_u64(L, a);
    push_u64(L, b);
    call(L, "remainder", 2, 1);
    if (lua_isnil(L, -1) != (b == 0)) {
        abort();
    }
    r = b == 0 ? 0 : to_u64(L, -1);
    lua_pop(L, 1);
    if (b != 0 && (q * b + r != a || r >= b)) {
        abort();
    }
}

#if LUA_VERSION_NUM == 501
enum { KIND_INT, KIND_UINT, KIND_NUM };

/* Pushes v as the given kind; returns 0 when that kind cannot hold v. */
static int push_kind(lua_State *L, uint64_t v, int kind, int distinct) {
    int64_t s = (int64_t)v;
    if (kind == KIND_INT) {
        lua_pushint64(L, s);
    } else if (kind == KIND_UINT) {
        if (!distinct) {
            return 0;
        }
        lua_pushuint64(L, v);
    } else {
        if (s < -((int64_t)1 << 53) || s > ((int64_t)1 << 53)) {
            return 0;
        }
        lua_pushnumber(L, (lua_Number)s);
    }
    return 1;
}

/* Shift by one bit at a time; Lua 5.3 semantics (negative counts shift the
 * other way) plus an arithmetic right shift that copies bit 63. */
static uint64_t ref_shift(uint64_t x, uint64_t nbits, int nkind, int left, int arithmetic) {
    __int128 c = nkind == KIND_UINT ? (__int128)nbits : (__int128)(int64_t)nbits;
    int i, steps;
    if (!left) {
        c = -c;
    }
    steps = c > 64 ? 64 : c < -64 ? 64 : (int)(c < 0 ? -c : c);
    for (i = 0; i < steps; i++) {
        if (c > 0) {
            x <<= 1;
        } else {
            x = (x >> 1) | (arithmetic ? x & 0x8000000000000000ULL : 0);
        }
    }
    return x;
}

/* q is the floor quotient of a and b (signed) if a == q * b + r with r zero
 * or of b's sign and |r| < |b|; INT64_MIN // -1 wraps to INT64_MIN. */
static int is_floor_quotient(int64_t a, int64_t b, int64_t q) {
    __int128 r;
    if (a == INT64_MIN && b == -1) {
        return q == INT64_MIN;
    }
    r = (__int128)a - (__int128)q * b;
    return b > 0 ? (r >= 0 && r < b) : (r <= 0 && r > b);
}

/* Calls uint64[fn] with a (kind ka) and b (kind kb) and checks the result:
 * an error when expected, else the bits and the result kind. */
static void check_helper(lua_State *L, const char *fn, uint64_t a, int ka, uint64_t b, int kb, int nargs,
                         int distinct) {
    int mixed = (ka == KIND_INT && kb == KIND_UINT) || (ka == KIND_UINT && kb == KIND_INT);
    int is_unsigned = ka == KIND_UINT || kb == KIND_UINT;
    int fails = 0;
    uint64_t want = 0, got;
    if (!push_kind(L, a, ka, distinct)) {
        return;
    }
    if (nargs == 2 && !push_kind(L, b, kb, distinct)) {
        lua_pop(L, 1);
        return;
    }
    if (strcmp(fn, "band") == 0) {
        fails = mixed;
        want = a & b;
    } else if (strcmp(fn, "bor") == 0) {
        fails = mixed;
        want = a | b;
    } else if (strcmp(fn, "bxor") == 0) {
        fails = mixed;
        want = a ^ b;
    } else if (strcmp(fn, "idiv") == 0) {
        fails = mixed || b == 0;
        want = fails || !is_unsigned ? 0 : a / b; /* signed: is_floor_quotient below */
    } else {
        is_unsigned = ka == KIND_UINT; /* unary and shifts: the kind of a */
        if (strcmp(fn, "bnot") == 0) {
            want = ~a;
        } else {
            want = ref_shift(a, b, kb, strcmp(fn, "lshift") == 0, strcmp(fn, "arshift") == 0);
        }
    }
    call(L, fn, nargs, 1);
    if (lua_isnil(L, -1) != fails) {
        abort();
    }
    if (!fails) {
        got = to_u64(L, -1);
        if (distinct && lua_isuint64(L, -1) != is_unsigned) {
            abort();
        }
        if (strcmp(fn, "idiv") == 0 && !is_unsigned) {
            if (!is_floor_quotient((int64_t)a, (int64_t)b, (int64_t)got)) {
                abort();
            }
        } else if (got != want) {
            abort();
        }
    }
    lua_pop(L, 1);
}

/* x .. s and s .. x give the tostring text of x joined with s. */
static void check_concat(lua_State *L, uint64_t v, int kind, const char *s, int distinct) {
    char text[32];
    size_t tlen, slen = strlen(s), len;
    const char *got;
    int order;
    if (kind == KIND_UINT) {
        snprintf(text, sizeof(text), "%lluU", (unsigned long long)v);
    } else {
        snprintf(text, sizeof(text), "%lld", (long long)(int64_t)v);
    }
    tlen = strlen(text);
    for (order = 0; order < 2; order++) {
        if (luaL_loadstring(L, "local a, b = ... return a .. b") != LUA_OK || !push_kind(L, v, kind, distinct)) {
            abort();
        }
        lua_pushstring(L, s);
        if (order) {
            lua_insert(L, -2);
        }
        if (lua_pcall(L, 2, 1, 0) != LUA_OK) {
            abort();
        }
        got = lua_tolstring(L, -1, &len);
        if (got == NULL || len != tlen + slen || memcmp(got + (order ? slen : 0), text, tlen) != 0 ||
            memcmp(got + (order ? 0 : tlen), s, slen) != 0) {
            abort();
        }
        lua_pop(L, 1);
    }
}

static void check_luajit_helpers(lua_State *L, uint64_t a, uint64_t b, const char *text) {
    static const char *const binary[] = {"band", "bor", "bxor", "idiv", "lshift", "rshift", "arshift"};
    int distinct, ka, kb;
    size_t i;
    lua_pushuint64(L, 1);
    distinct = !lua_isint64(L, -1); /* UINT_ESPECIALLY: a separate unsigned kind */
    lua_pop(L, 1);
    for (ka = KIND_INT; ka <= KIND_NUM; ka++) {
        check_helper(L, "bnot", a, ka, 0, KIND_INT, 1, distinct);
        for (kb = KIND_INT; kb <= KIND_NUM; kb++) {
            for (i = 0; i < sizeof(binary) / sizeof(binary[0]); i++) {
                check_helper(L, binary[i], a, ka, b, kb, 2, distinct);
            }
        }
        if (ka != KIND_NUM && (ka != KIND_UINT || distinct)) {
            check_concat(L, a, ka, text, distinct);
        }
    }
}
#endif

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    char *a, *b;
    const char *nul;
    uint64_t va, vb;
    lua_State *L;
    static const char *const fns[] = {"tostring", "compare", "divide", "remainder", "parse",
#if LUA_VERSION_NUM == 501
                                      "band", "bor", "bxor", "bnot", "lshift", "rshift", "arshift", "idiv",
#endif
    };
    size_t i;

    if (size > 256) {
        return -1;
    }
    nul = memchr(data, 0, size);
    a = strndup((const char *)data, nul ? (size_t)(nul - (const char *)data) : size);
    b = strndup(nul ? nul + 1 : "", nul ? size - (size_t)(nul - (const char *)data) - 1 : 0);
#if LUA_VERSION_NUM == 501
    /* One LuaJIT state serves every input: each new state maps memory at a
     * fresh random address, and ASan's shadow for those regions grows the RSS
     * past libFuzzer's limit. The uint64 helpers keep no state between calls. */
    static lua_State *shared;
    if (shared == NULL) {
        shared = fuzz_new_state();
    }
    L = shared;
    fuzz_hook_calls = 0;
#else
    L = fuzz_new_state();
#endif

    if (parse(L, a, &va) && parse(L, b, &vb)) {
        if (va != strtoull(a, NULL, 0) || vb != strtoull(b, NULL, 0)) {
            abort();
        }
        check_pair(L, va, vb);
        check_pair(L, vb, va);
        check_c_helpers(L, va);
        check_c_helpers(L, vb);
#if LUA_VERSION_NUM == 501
        check_luajit_helpers(L, va, vb, b);
        check_luajit_helpers(L, vb, va, a);
#endif
    }
    /* Floats, numeric strings and other conversions of the raw text. */
    push_text(L, a);
    check_touint(L);
    push_text(L, b);
    check_touint(L);
    lua_settop(L, 0);
    for (i = 0; i < sizeof(fns) / sizeof(fns[0]); i++) {
        push_text(L, a);
        push_text(L, b);
        call(L, fns[i], 2, 1);
        lua_settop(L, 0);
    }
#if LUA_VERSION_NUM == 501
    lua_settop(L, 0);
#else
    lua_close(L);
#endif
    free(a);
    free(b);
    return 0;
}
