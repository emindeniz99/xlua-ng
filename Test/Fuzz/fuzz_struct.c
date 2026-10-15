/* libFuzzer target: copy-by-value struct storage in build/xlua.c.
 *
 * Creates a struct userdata of a fuzzed size and applies a sequence of
 * fuzzed operations: the exported xlua_pack_* / xlua_unpack_* pairs that
 * CopyByValue.cs calls, and the Lua-side accessors returned by
 * xlua.genaccessor(offset, type) plus xlua.structclone. Invariants checked
 * after every step: the fake_id/len header in front of the data is unchanged,
 * an access succeeds exactly when it fits, and unpack returns what pack
 * stored. AddressSanitizer catches accesses outside the userdata. */
#include "fuzz_common.h"

typedef struct {
    int fake_id;
    unsigned int len;
    unsigned char data[1];
} Header; /* layout of CSharpStruct in build/xlua.c */

#define PACK_DECL(name, type) \
    LUALIB_API int xlua_pack_##name(void *p, int offset, type field); \
    LUALIB_API int xlua_unpack_##name(void *p, int offset, type *field);
PACK_DECL(int8_t, int8_t)
PACK_DECL(int16_t, int16_t)
PACK_DECL(int32_t, int32_t)
PACK_DECL(int64_t, int64_t)
PACK_DECL(float, float)
PACK_DECL(double, double)
LUALIB_API int xlua_pack_float2(void *p, int offset, float f1, float f2);
LUALIB_API int xlua_unpack_float2(void *p, int offset, float *f1, float *f2);
LUALIB_API int xlua_pack_float3(void *p, int offset, float f1, float f2, float f3);
LUALIB_API int xlua_unpack_float3(void *p, int offset, float *f1, float *f2, float *f3);
LUALIB_API int xlua_pack_float4(void *p, int offset, float f1, float f2, float f3, float f4);
LUALIB_API int xlua_unpack_float4(void *p, int offset, float *f1, float *f2, float *f3, float *f4);
LUALIB_API int xlua_pack_decimal(void *p, int offset, const int *decimal);
LUALIB_API int xlua_unpack_decimal(void *p, int offset, uint8_t *scale, uint8_t *sign, int *hi32, uint64_t *lo64);

static Header *css;
static unsigned int css_len;

static void check_header(void) {
    if (css->fake_id != -1 || css->len != css_len) {
        abort();
    }
}

static int fits(int offset, size_t width) {
    return offset >= 0 && (size_t)offset + width <= css_len;
}

static void expect(int ok, int offset, size_t width) {
    if (ok != fits(offset, width)) {
        abort();
    }
    check_header();
}

/* Floats are compared bitwise so NaN payloads also round-trip. */
static void same(const void *a, const void *b, size_t n) {
    if (memcmp(a, b, n) != 0) {
        abort();
    }
}

static void scalar_op(int kind, int offset, uint64_t bits) {
    switch (kind) {
#define SCALAR(n, name, type) \
    case n: { \
        type in, out; \
        memcpy(&in, &bits, sizeof(in)); \
        int ok = xlua_pack_##name(css, offset, in); \
        expect(ok, offset, sizeof(type)); \
        expect(xlua_unpack_##name(css, offset, &out), offset, sizeof(type)); \
        if (ok) same(&in, &out, sizeof(type)); \
        break; \
    }
    SCALAR(0, int8_t, int8_t)
    SCALAR(1, int16_t, int16_t)
    SCALAR(2, int32_t, int32_t)
    SCALAR(3, int64_t, int64_t)
    SCALAR(4, float, float)
    SCALAR(5, double, double)
#undef SCALAR
    case 6: {
        float in[4], out[4];
        memcpy(in, &bits, 8);
        memcpy(in + 2, &bits, 8);
        int ok = xlua_pack_float2(css, offset, in[0], in[1]);
        expect(ok, offset, 8);
        expect(xlua_unpack_float2(css, offset, &out[0], &out[1]), offset, 8);
        if (ok) same(in, out, 8);
        ok = xlua_pack_float3(css, offset, in[0], in[1], in[2]);
        expect(ok, offset, 12);
        expect(xlua_unpack_float3(css, offset, &out[0], &out[1], &out[2]), offset, 12);
        if (ok) same(in, out, 12);
        ok = xlua_pack_float4(css, offset, in[0], in[1], in[2], in[3]);
        expect(ok, offset, 16);
        expect(xlua_unpack_float4(css, offset, &out[0], &out[1], &out[2], &out[3]), offset, 16);
        if (ok) same(in, out, 16);
        break;
    }
    default: {
        /* System.Decimal: flags word (scale in byte 2, sign in byte 3), hi32, lo64 */
        int dec[4];
        uint8_t scale, sign;
        int hi32;
        uint64_t lo64;
        memcpy(dec, &bits, 8);
        memcpy(dec + 2, &bits, 8);
        int ok = xlua_pack_decimal(css, offset, dec);
        expect(ok, offset, 16);
        expect(xlua_unpack_decimal(css, offset, &scale, &sign, &hi32, &lo64), offset, 16);
        if (ok && (scale != (uint8_t)(dec[0] >> 16) || sign != (uint8_t)(dec[0] >> 24) || hi32 != dec[1])) {
            abort();
        }
        break;
    }
    }
}

/* xlua.genaccessor(offset, type) returns getter, setter, nop. */
static void accessor_op(lua_State *L, int offset, int type, lua_Integer value) {
    static const size_t widths[] = {1, 1, 2, 2, 4, 4, 8, 8, 4, 8};
    lua_getglobal(L, "xlua");
    lua_getfield(L, -1, "genaccessor");
    lua_pushinteger(L, offset);
    lua_pushinteger(L, type);
    if (lua_pcall(L, 2, 3, 0) != LUA_OK) {
        if (offset >= 0 && type >= 0 && type <= 9) {
            abort(); /* valid arguments must produce accessors */
        }
        lua_settop(L, 1);
        return;
    }
    int ok_expected = type >= 0 && type <= 9 && fits(offset, widths[type]);
    lua_pushvalue(L, -2); /* setter */
    lua_pushvalue(L, 1);  /* the struct */
    if (type == 8 || type == 9) {
        lua_pushnumber(L, (lua_Number)value / 7);
    } else {
        lua_pushinteger(L, value);
    }
    if ((lua_pcall(L, 2, 0, 0) == LUA_OK) != ok_expected) {
        abort();
    }
    lua_settop(L, 5);
    lua_pushvalue(L, -3); /* getter */
    lua_pushvalue(L, 1);
    if ((lua_pcall(L, 1, 1, 0) == LUA_OK) != ok_expected) {
        abort();
    }
    check_header();
    lua_settop(L, 1);
}

static void clone_op(lua_State *L) {
    lua_getglobal(L, "xlua");
    lua_getfield(L, -1, "structclone");
    lua_pushvalue(L, 1);
    if (lua_pcall(L, 1, 1, 0) != LUA_OK) {
        abort();
    }
    Header *copy = (Header *)lua_touserdata(L, -1);
    if (copy == NULL || copy->fake_id != -1 || copy->len != css_len ||
        memcmp(copy->data, css->data, css_len) != 0) {
        abort();
    }
    lua_settop(L, 1);
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    FuzzInput in = {data, size};
    lua_State *L;
    if (size > 1024) {
        return -1;
    }
    L = fuzz_new_state();
    css_len = (unsigned int)fuzz_take(&in, 1);
    css = (Header *)fuzz_push_struct(L, css_len); /* stack[1] */
    check_header();
    while (in.size > 0) {
        unsigned op = (unsigned)fuzz_take(&in, 1);
        /* Offsets near the size boundary and negative values matter most. */
        int offset = (int)(int16_t)fuzz_take(&in, 2);
        if (op & 0x80) {
            offset = (int)(css_len - (unsigned)(offset & 0x1f));
        }
        uint64_t bits = fuzz_take(&in, 8);
        switch (op & 0x0f) {
        case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7:
            scalar_op((int)(op & 0x0f), offset, bits);
            break;
        case 8: case 9: case 10:
            accessor_op(L, offset, (int)((op >> 4) & 0x07) + (op & 0x0f) - 9, (lua_Integer)bits);
            break;
        case 11:
            accessor_op(L, offset, (int)(bits % 12) - 1, (lua_Integer)(bits >> 8));
            break;
        default:
            clone_op(L);
            break;
        }
    }
    lua_close(L);
    return 0;
}
