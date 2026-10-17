/* libFuzzer target: copy-by-value struct storage in build/xlua.c.
 *
 * Creates a struct userdata of a fuzzed size and applies a sequence of
 * fuzzed operations: the exported xlua_pack_* / xlua_unpack_* pairs that
 * CopyByValue.cs calls (scalars, float2 to float6, decimal), the Lua-side
 * accessors returned by xlua.genaccessor(offset, type), also with values of
 * every Lua type and with arguments that are not structs, xlua.structclone,
 * and xlua_gettypeid / xlua_tostruct / xlua_tocsobj_safe. Invariants checked
 * after every step: the fake_id/len header in front of the data is unchanged,
 * an access succeeds exactly when it fits, unpack returns what pack stored,
 * and integer, uint32 and double fields read back the C conversion of the
 * value set. AddressSanitizer catches accesses outside the userdata; UBSan
 * catches undefined conversions. */
#include <stdio.h>

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
LUALIB_API int xlua_pack_float5(void *p, int offset, float f1, float f2, float f3, float f4, float f5);
LUALIB_API int xlua_unpack_float5(void *p, int offset, float *f1, float *f2, float *f3, float *f4, float *f5);
LUALIB_API int xlua_pack_float6(void *p, int offset, float f1, float f2, float f3, float f4, float f5, float f6);
LUALIB_API int xlua_unpack_float6(void *p, int offset, float *f1, float *f2, float *f3, float *f4, float *f5,
                                  float *f6);
LUALIB_API int xlua_pack_decimal(void *p, int offset, const int *decimal);
LUALIB_API int xlua_unpack_decimal(void *p, int offset, uint8_t *scale, uint8_t *sign, int *hi32, uint64_t *lo64);
LUA_API void *xlua_tostruct(lua_State *L, int idx, int meta_ref);
LUA_API int xlua_gettypeid(lua_State *L, int idx);
LUA_API int xlua_tocsobj_safe(lua_State *L, int idx);

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
        float in[6], out[6];
        memcpy(in, &bits, 8);
        memcpy(in + 2, &bits, 8);
        memcpy(in + 4, &bits, 8);
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
        ok = xlua_pack_float5(css, offset, in[0], in[1], in[2], in[3], in[4]);
        expect(ok, offset, 20);
        expect(xlua_unpack_float5(css, offset, &out[0], &out[1], &out[2], &out[3], &out[4]), offset, 20);
        if (ok) same(in, out, 20);
        ok = xlua_pack_float6(css, offset, in[0], in[1], in[2], in[3], in[4], in[5]);
        expect(ok, offset, 24);
        expect(xlua_unpack_float6(css, offset, &out[0], &out[1], &out[2], &out[3], &out[4], &out[5]), offset, 24);
        if (ok) same(in, out, 24);
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

/* Pushes a Lua value of a fuzzed type; returns its kind. */
enum { V_INT, V_FLOAT, V_STRING, V_NIL, V_BOOL, V_TABLE };
static int push_value(lua_State *L, unsigned kind, uint64_t bits) {
    char text[32];
    double d;
    switch (kind % 6) {
    case V_INT:
        lua_pushinteger(L, (lua_Integer)bits);
        return V_INT;
    case V_FLOAT:
        memcpy(&d, &bits, sizeof(d)); /* any double: NaN, infinities, huge, negative */
        lua_pushnumber(L, d);
        return V_FLOAT;
    case V_STRING:
        snprintf(text, sizeof(text), "%lld", (long long)bits);
        lua_pushstring(L, text);
        return V_STRING;
    case V_NIL:
        lua_pushnil(L);
        return V_NIL;
    case V_BOOL:
        lua_pushboolean(L, (int)(bits & 1));
        return V_BOOL;
    default:
        lua_newtable(L);
        return V_TABLE;
    }
}

/* A float as a uint32 field: truncated, then wrapped like an integer; NaN and
 * values outside the 64-bit range give 0 (xlua_touint). */
static uint32_t ref_uint32(double d) {
    return d >= -9223372036854775808.0 && d < 9223372036854775808.0 ? (uint32_t)(int64_t)d : 0;
}

/* A setter given a value of any Lua type raises only when the field does not
 * fit; integer, uint32 and double fields read back the C conversion of the
 * value that was set. */
static void value_op(lua_State *L, int offset, int type, unsigned kind, uint64_t bits) {
    static const size_t widths[] = {1, 1, 2, 2, 4, 4, 8, 8, 4, 8};
    int ok_expected, vkind;
    lua_getglobal(L, "xlua");
    lua_getfield(L, -1, "genaccessor");
    lua_pushinteger(L, offset);
    lua_pushinteger(L, type);
    if (lua_pcall(L, 2, 3, 0) != LUA_OK) {
        if (offset >= 0) {
            abort(); /* valid arguments must produce accessors */
        }
        lua_settop(L, 1);
        return;
    }
    ok_expected = fits(offset, widths[type]);
    lua_pushvalue(L, -2); /* setter */
    lua_pushvalue(L, 1);
    vkind = push_value(L, kind, bits);
    if ((lua_pcall(L, 2, 0, 0) == LUA_OK) != ok_expected) {
        abort();
    }
    lua_settop(L, 5);
    lua_pushvalue(L, -3); /* getter */
    lua_pushvalue(L, 1);
    if ((lua_pcall(L, 1, 1, 0) == LUA_OK) != ok_expected) {
        abort();
    }
    if (ok_expected) {
        lua_Integer i = (lua_Integer)bits;
        double d, got;
        memcpy(&d, &bits, sizeof(d));
        if (vkind == V_INT && type <= 4) {
            int v = (int)i; /* xlua_tointeger, then the field type */
            lua_Integer want = type == 0 ? (int8_t)v : type == 1 ? (uint8_t)v : type == 2 ? (int16_t)v
                             : type == 3 ? (uint16_t)v : v;
            if (lua_tointeger(L, -1) != want) {
                abort();
            }
        } else if (vkind == V_INT && type == 5) {
            if (lua_tointeger(L, -1) != (lua_Integer)(uint32_t)i) {
                abort();
            }
        } else if (vkind == V_FLOAT && type == 5) {
            if (lua_tointeger(L, -1) != (lua_Integer)ref_uint32(d)) {
                abort();
            }
        } else if (vkind == V_INT && (type == 6 || type == 7)) {
            if (lua_tointeger(L, -1) != i) {
                abort();
            }
        } else if (vkind == V_FLOAT && type == 9) {
            got = lua_tonumber(L, -1);
            if (memcmp(&got, &d, sizeof(d)) != 0 && !(d != d && got != got)) {
                abort();
            }
        }
    }
    check_header();
    lua_settop(L, 1);
}

/* Accessors refuse what is not a struct: a number, a light userdata (even one
 * pointing at the struct), a full userdata whose first int is not -1 (a C#
 * object handle), a table, nil; and the struct itself once the accessor's
 * offset upvalue is negative (debug.setupvalue can do that). */
static void foreign_op(lua_State *L, int offset, int type, unsigned kind, uint64_t bits) {
    int setter, i, accessor;
    int positive = offset < 0 ? -(offset + 1) : offset;
    lua_getglobal(L, "xlua");
    lua_getfield(L, -1, "genaccessor");
    lua_pushinteger(L, positive);
    lua_pushinteger(L, type);
    if (lua_pcall(L, 2, 3, 0) != LUA_OK) { /* stack: struct, xlua, getter, setter, nop */
        abort();
    }
    for (setter = 0; setter < 2; setter++) {
        accessor = setter ? 4 : 3;
        for (i = 0; i < 6; i++) {
            int negative = (kind + (unsigned)i) % 6 == 5;
            lua_pushvalue(L, accessor);
            switch ((kind + (unsigned)i) % 6) {
            case 0:
                lua_pushinteger(L, (lua_Integer)bits);
                break;
            case 1:
                lua_pushlightuserdata(L, css);
                break;
            case 2: {
                size_t size = 4 + (size_t)(bits % 21);
                int *u = (int *)lua_newuserdata(L, size);
                memset(u, (int)(bits >> 8) & 0xff, size);
                if (*u == -1) {
                    *u = (int)((bits >> 16) & 0x7fffffff); /* a C# object index */
                }
                break;
            }
            case 3:
                lua_newtable(L);
                break;
            case 4:
                lua_pushnil(L);
                break;
            default:
                /* the struct, with the accessor's offset upvalue made negative */
                lua_pushinteger(L, -1 - positive);
                if (lua_setupvalue(L, accessor, 1) == NULL) {
                    abort();
                }
                lua_pushvalue(L, 1);
                break;
            }
            if (setter) {
                lua_pushinteger(L, 1);
            }
            if (lua_pcall(L, setter ? 2 : 1, setter ? 0 : 1, 0) == LUA_OK) {
                abort(); /* every value above must be refused */
            }
            lua_pop(L, 1);
            if (negative) {
                lua_pushinteger(L, positive);
                lua_setupvalue(L, accessor, 1);
            }
        }
    }
    check_header();
    lua_settop(L, 1);
}

/* The type id helpers the managed bridge applies to struct arguments; the
 * struct's metatable has the type id 1 at [1]. */
static void typeid_op(lua_State *L, int other_id) {
    if (xlua_gettypeid(L, 1) != 1 || xlua_tostruct(L, 1, 1) != css->data || xlua_tostruct(L, 1, other_id) != NULL ||
        xlua_tocsobj_safe(L, 1) != -1) {
        abort(); /* a struct's first int is -1: not an object pool index */
    }
    lua_pushinteger(L, other_id);
    if (xlua_gettypeid(L, -1) != -1 || xlua_tostruct(L, -1, 1) != NULL) {
        abort();
    }
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
        case 13:
            value_op(L, offset, (int)((op >> 4) % 10), (unsigned)fuzz_take(&in, 1), bits); /* + a kind byte */
            break;
        case 14:
            foreign_op(L, offset, (int)((op >> 4) % 10), (unsigned)fuzz_take(&in, 1), bits);
            break;
        case 15:
            typeid_op(L, (int)(bits & 0xff) + 2);
            break;
        default:
            clone_op(L);
            break;
        }
    }
    lua_close(L);
    return 0;
}
