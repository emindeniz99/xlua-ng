/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

#define LUA_LIB

#include "i64lib.h"
#include <string.h>
#include <math.h>
#include <stdlib.h>

#if ( defined (_WIN32) ||  defined (_WIN64) ) && !defined (__MINGW32__) && !defined (__MINGW64__)

#if !defined(PRId64)
# if __WORDSIZE == 64  
#  define PRId64    "ld"   
# else  
#  define PRId64    "lld"  
# endif 
#endif

#if !defined(PRIu64)
# if __WORDSIZE == 64  
#  define PRIu64    "lu"   
# else  
#  define PRIu64    "llu"  
# endif
#endif

#else
#include <inttypes.h>
#endif

#if LUA_VERSION_NUM == 501

#define INT64_META_REF 8

enum IntegerType {
	Int,
	UInt,
	Num
};

/* fake_id is -1, the CSharpStruct marker (xlua.c), and the four bytes after
 * it (type and padding) are where a CSharpStruct keeps its length. The push
 * functions zero the whole block, so on little-endian targets that length is
 * the type, 0 or 1, and a struct accessor handed an int64 stays inside the
 * block instead of reading uninitialised padding as a large length. */
typedef struct {
	int fake_id;
	int8_t type;
    union {
		int64_t i64;
		uint64_t u64;
	} data;
} Integer64;

/* Signed overflow and casting NaN or an out-of-range double to an integer are
 * undefined behaviour. The operators below wrap through uint64_t, and these
 * helpers saturate double results to the target range (NaN becomes 0). */
static int64_t number_to_int64(lua_Number d) {
	if (d != d) return 0;
	if (d >= 9223372036854775808.0) return INT64_MAX;
	if (d < -9223372036854775808.0) return INT64_MIN;
	return (int64_t)d;
}

static uint64_t number_to_uint64(lua_Number d) {
	if (!(d > 0)) return 0;
	if (d >= 18446744073709551616.0) return UINT64_MAX;
	return (uint64_t)d;
}

LUALIB_API void lua_pushint64(lua_State* L, int64_t n) {
	Integer64* p = (Integer64*)lua_newuserdata(L, sizeof(Integer64));
	memset(p, 0, sizeof(Integer64)); /* see Integer64 */
	p->fake_id = -1;
	p->data.i64 = n;
	p->type = Int;
	lua_rawgeti(L, LUA_REGISTRYINDEX, INT64_META_REF);
	lua_setmetatable(L, -2);            
}

LUALIB_API int lua_isint64(lua_State* L, int pos) {
	int equal;
    Integer64* p = (Integer64*)lua_touserdata(L, pos);

    if (p != NULL) {
        if (lua_getmetatable(L, pos)) {            
			lua_rawgeti(L, LUA_REGISTRYINDEX, INT64_META_REF);
            equal = lua_rawequal(L, -1, -2);
            lua_pop(L, 2);  

            return equal && (p->type == Int);
        }
    }

    return 0;
}

LUALIB_API int lua_isint64_or_uint64(lua_State* L, int pos) {
	int equal;
    Integer64* p = (Integer64*)lua_touserdata(L, pos);

    if (p != NULL) {
        if (lua_getmetatable(L, pos)) {            
			lua_rawgeti(L, LUA_REGISTRYINDEX, INT64_META_REF);
            equal = lua_rawequal(L, -1, -2);
            lua_pop(L, 2);  

            return equal;
        }
    }

    return 0;
}

LUALIB_API int64_t lua_toint64(lua_State* L, int pos) {
    int64_t n = 0;
    int type = lua_type(L, pos);
    
    switch(type) {
        case LUA_TNUMBER:
            n = number_to_int64(lua_tonumber(L, pos));
            break;
        case LUA_TUSERDATA:
		    if (lua_isint64_or_uint64(L, pos)) {
				n = ((Integer64*)lua_touserdata(L, pos))->data.i64;
			}
			break;
        default:
            break;
    }
    
    return n;
}

#if defined(UINT_ESPECIALLY)
LUALIB_API void lua_pushuint64(lua_State* L, uint64_t n) {
	Integer64* p = (Integer64*)lua_newuserdata(L, sizeof(Integer64));
	memset(p, 0, sizeof(Integer64)); /* see Integer64 */
	p->fake_id = -1;
	p->data.u64 = n;
	p->type = UInt;
	lua_rawgeti(L, LUA_REGISTRYINDEX, INT64_META_REF);
	lua_setmetatable(L, -2);            
}


LUALIB_API int lua_isuint64(lua_State* L, int pos) {
	int equal;
    Integer64* p = (Integer64*)lua_touserdata(L, pos);

    if (p != NULL) {
        if (lua_getmetatable(L, pos)) {            
			lua_rawgeti(L, LUA_REGISTRYINDEX, INT64_META_REF);
            equal = lua_rawequal(L, -1, -2);
            lua_pop(L, 2);  

            return equal && (p->type == UInt);
        }
    }

    return 0;
}

LUALIB_API uint64_t lua_touint64(lua_State* L, int pos) {
    uint64_t n = 0;
    int type = lua_type(L, pos);
    
    switch(type) {
        case LUA_TNUMBER:
            n = number_to_int64(lua_tonumber(L, pos));
            break;
        case LUA_TUSERDATA:
		    if (lua_isint64_or_uint64(L, pos)) {
				n = ((Integer64*)lua_touserdata(L, pos))->data.u64;
			}
			break;
        default:
            break;
    }
    
    return n;
}
#else
LUALIB_API void lua_pushuint64(lua_State* L, uint64_t n) {
	lua_pushint64(L, (int64_t)n);
}

LUALIB_API int lua_isuint64(lua_State* L, int pos) {
    return lua_isint64(L, pos);
}

LUALIB_API uint64_t lua_touint64(lua_State* L, int pos) {
    return (uint64_t)lua_toint64(L, pos);
}
#endif

LUALIB_API int lua_isinteger64(lua_State* L, int pos) {
	int equal;
    Integer64* p = (Integer64*)lua_touserdata(L, pos);

    if (p != NULL) {
        if (lua_getmetatable(L, pos)) {            
			lua_rawgeti(L, LUA_REGISTRYINDEX, INT64_META_REF);
            equal = lua_rawequal(L, -1, -2);
            lua_pop(L, 2);  

            return equal;
        }
    }

    return 0;
}

static Integer64 lua_checkinteger64(lua_State* L, int pos) {
    Integer64 n = {0};
    int type = lua_type(L, pos);
    
    switch(type) {
        case LUA_TNUMBER:
            n.data.i64 = number_to_int64(lua_tonumber(L, pos));
			n.type = Num;
            break;
        case LUA_TUSERDATA:
		    if (lua_isinteger64(L, pos)) {
				n = *(Integer64*)lua_touserdata(L, pos);
				break;
			} else {
				luaL_typerror(L, pos, "Integer64");
				return n;
			}
        default:
            luaL_typerror(L, pos, "Integer64");
			return n;
    }
    
    return n;
}

/* The kind of a binary operation's result: a number takes the kind of the
 * other operand, and two numbers give an Int64. Raises when one operand is an
 * Int64 and the other a UInt64 (UINT_ESPECIALLY only); returns 1 for UInt64. */
static int int64_result_kind(lua_State* L, const Integer64* lhs, const Integer64* rhs) {
	if (lhs->type != rhs->type && lhs->type != Num && rhs->type != Num) {
		luaL_error(L, "type not match, lhs is %s, rhs is %s", lhs->type == Int ? "Int64" : "UInt64", rhs->type == Int ? "Int64" : "UInt64");
	}
	return lhs->type == UInt || rhs->type == UInt;
}

static void int64_push_kind(lua_State* L, int is_unsigned, uint64_t bits) {
	if (is_unsigned) {
		lua_pushuint64(L, bits);
	} else {
		lua_pushint64(L, (int64_t)bits);
	}
}

static int int64_add(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);
	int64_push_kind(L, int64_result_kind(L, &lhs, &rhs), lhs.data.u64 + rhs.data.u64);
    return 1;
}

static int int64_sub(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);
	int64_push_kind(L, int64_result_kind(L, &lhs, &rhs), lhs.data.u64 - rhs.data.u64);
    return 1;
}


static int int64_mul(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);
	int64_push_kind(L, int64_result_kind(L, &lhs, &rhs), lhs.data.u64 * rhs.data.u64);
    return 1;
}

static int int64_div(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);

	if (rhs.data.i64 == 0) {
        return luaL_error(L, "div by zero");
    }

	if (int64_result_kind(L, &lhs, &rhs)) {
		lua_pushuint64(L, lhs.data.u64 / rhs.data.u64);
	} else {
		/* INT64_MIN / -1 overflows (SIGFPE on x86); wrap like the other operators. */
		lua_pushint64(L, rhs.data.i64 == -1 ? (int64_t)(0 - lhs.data.u64) : lhs.data.i64 / rhs.data.i64);
	}
    return 1;
}

static int int64_mod(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);

    if (rhs.data.i64 == 0) {
        return luaL_error(L, "mod by zero");
    }

	if (int64_result_kind(L, &lhs, &rhs)) {
		lua_pushuint64(L, lhs.data.u64 % rhs.data.u64);
	} else {
		lua_pushint64(L, rhs.data.i64 == -1 ? 0 : lhs.data.i64 % rhs.data.i64);
	}
    return 1;
}

static int int64_unm(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    lua_pushint64(L, (int64_t)(0 - lhs.data.u64));
    return 1;
}

static int int64_pow(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);

	if (int64_result_kind(L, &lhs, &rhs)) {
		lua_pushuint64(L, number_to_uint64(pow((double)lhs.data.u64, (double)rhs.data.u64)));
	} else {
		lua_pushint64(L, number_to_int64(pow((double)lhs.data.i64, (double)rhs.data.i64)));
	}
    return 1;
}

static int int64_eq(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);

	if (int64_result_kind(L, &lhs, &rhs)) {
		lua_pushboolean(L, lhs.data.u64 == rhs.data.u64);
	} else {
		lua_pushboolean(L, lhs.data.i64 == rhs.data.i64);
	}
    return 1;
}

static int int64_lt(lua_State* L) {
    Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);

	if (int64_result_kind(L, &lhs, &rhs)) {
		lua_pushboolean(L, lhs.data.u64 < rhs.data.u64);
	} else {
		lua_pushboolean(L, lhs.data.i64 < rhs.data.i64);
	}
    return 1;
}

static int int64_le(lua_State* L) {
	Integer64 lhs = lua_checkinteger64(L, 1);
    Integer64 rhs = lua_checkinteger64(L, 2);

	if (int64_result_kind(L, &lhs, &rhs)) {
		lua_pushboolean(L, lhs.data.u64 <= rhs.data.u64);
	} else {
		lua_pushboolean(L, lhs.data.i64 <= rhs.data.i64);
	}
    return 1;
}

/* The tostring text: the signed digits, or the unsigned digits and "U" for a
 * UInt64. */
static void int64_push_text(lua_State* L, const Integer64* n) {
    char temp[72];
	if (n->type == UInt) {
		sprintf(temp, "%" PRIu64 "U", n->data.u64); // NOLINT(cert-err33-c): 72 bytes hold any 64-bit integer
	} else {
		sprintf(temp, "%" PRId64, n->data.i64); // NOLINT(cert-err33-c): 72 bytes hold any 64-bit integer
	}
    lua_pushstring(L, temp);
}

static int int64_tostring(lua_State* L) {
	Integer64 lhs = lua_checkinteger64(L, 1);
	int64_push_text(L, &lhs);
    return 1;
}

/* "a" .. x and x .. "b": an Int64 operand turns into its tostring text, a
 * string or number is concatenated as Lua does, and any other value raises
 * the error Lua's own concatenation raises. */
static int int64_concat(lua_State* L) {
	int i;
	for (i = 1; i <= 2; i++) {
		if (lua_isinteger64(L, i)) {
			int64_push_text(L, (const Integer64*)lua_touserdata(L, i));
		} else if (lua_type(L, i) == LUA_TSTRING || lua_type(L, i) == LUA_TNUMBER) {
			lua_pushvalue(L, i);
		} else {
			return luaL_error(L, "attempt to concatenate a %s value", luaL_typename(L, i));
		}
	}
	lua_concat(L, 2);
	return 1;
}

/* The LuaJIT-only uint64 helpers with two arguments share uint64_op; its
 * upvalue is the index of the name here. */
static const char* const uint64_op_names[] = {"band", "bor", "bxor", "idiv", "lshift", "rshift", "arshift"};

/* band, bor, bxor and idiv: operands and the result kind follow the
 * arithmetic operators. idiv is floor division like Lua 5.3's // (plain
 * unsigned division for a UInt64); n // 0 raises and INT64_MIN // -1 wraps to
 * INT64_MIN, as in Lua 5.3.
 * lshift, rshift (logical) and arshift (copies bit 63) shift x by n bits and
 * return the kind of x. As in Lua 5.3, a negative n shifts the other way and
 * shifting by 64 or more bits leaves 0 (arshift to the right leaves the fill:
 * 0, or -1 when bit 63 is set). */
static int uint64_op(lua_State* L) {
	int op = (int)lua_tointeger(L, lua_upvalueindex(1));
	Integer64 lhs = lua_checkinteger64(L, 1);
	Integer64 rhs = lua_checkinteger64(L, 2);
	uint64_t a = lhs.data.u64, b = rhs.data.u64;
	int is_unsigned;
	if (op >= 4) {
		uint64_t fill = op == 6 && (a >> 63) ? UINT64_MAX : 0;
		int64_t count = rhs.type == UInt && b > 64 ? 64 : rhs.data.i64;
		if (count > 64) {
			count = 64;
		} else if (count < -64) {
			count = -64;
		}
		if (op != 4) {
			count = -count; /* from here on, a positive count shifts left */
		}
		if (count >= 64) {
			a = 0;
		} else if (count <= -64) {
			a = fill;
		} else if (count >= 0) {
			a <<= count;
		} else {
			a = ((a ^ fill) >> -count) ^ fill;
		}
		is_unsigned = lhs.type == UInt;
	} else {
		is_unsigned = int64_result_kind(L, &lhs, &rhs);
		if (op == 0) {
			a &= b;
		} else if (op == 1) {
			a |= b;
		} else if (op == 2) {
			a ^= b;
		} else if (rhs.data.i64 == 0) {
			return luaL_error(L, "attempt to perform 'n//0'");
		} else if (is_unsigned) {
			a /= b;
		} else if (rhs.data.i64 == -1) {
			a = 0 - a; /* INT64_MIN / -1 overflows */
		} else {
			int64_t q = lhs.data.i64 / rhs.data.i64;
			if (lhs.data.i64 % rhs.data.i64 != 0 && (lhs.data.i64 < 0) != (rhs.data.i64 < 0)) {
				q -= 1; /* C truncates; round toward minus infinity */
			}
			a = (uint64_t)q;
		}
	}
	int64_push_kind(L, is_unsigned, a);
	return 1;
}

static int uint64_bnot(lua_State* L) {
	Integer64 n = lua_checkinteger64(L, 1);
	int64_push_kind(L, n.type == UInt, ~n.data.u64);
	return 1;
}

#endif

#if LUA_VERSION_NUM >= 503
LUALIB_API void lua_pushint64(lua_State* L, int64_t n) {
	lua_pushinteger(L, n);
}
LUALIB_API void lua_pushuint64(lua_State* L, uint64_t n) {
	lua_pushinteger(L, n); // NOLINT(bugprone-narrowing-conversions): uint64 keeps its 64 bits in a lua_Integer; the uint64 library reinterprets them
}

LUALIB_API int lua_isint64(lua_State* L, int pos) {
	return lua_isinteger(L, pos);
}

LUALIB_API int lua_isuint64(lua_State* L, int pos) {
	return lua_isinteger(L, pos);
}

LUALIB_API int64_t lua_toint64(lua_State* L, int pos) {
	return lua_tointeger(L, pos);
}

LUALIB_API uint64_t lua_touint64(lua_State* L, int pos) {
	return lua_tointeger(L, pos);
}
#endif

static int uint64_tostring(lua_State* L) {
	char temp[72];
	uint64_t n = lua_touint64(L, 1);
#if ( defined (_WIN32) ||  defined (_WIN64) ) && !defined (__MINGW32__) && !defined (__MINGW64__)
	sprintf_s(temp, sizeof(temp), "%" PRIu64, n);
#else
	snprintf(temp, sizeof(temp), "%" PRIu64, n); // NOLINT(cert-err33-c): 72 bytes hold any 64-bit integer
#endif
	
	lua_pushstring(L, temp);
	
	return 1;
}

static int uint64_compare(lua_State* L) {
	uint64_t lhs = lua_touint64(L, 1);
	uint64_t rhs = lua_touint64(L, 2);
	lua_pushinteger(L, lhs == rhs ? 0 : (lhs < rhs ? -1 : 1));
	return 1;
}

static int uint64_divide(lua_State* L) {
	uint64_t lhs = lua_touint64(L, 1);
	uint64_t rhs = lua_touint64(L, 2);
	if (rhs == 0) {
        return luaL_error(L, "div by zero");
    }
	lua_pushuint64(L, lhs / rhs);
	return 1;
}

static int uint64_remainder(lua_State* L) {
	uint64_t lhs = lua_touint64(L, 1);
	uint64_t rhs = lua_touint64(L, 2);
	if (rhs == 0) {
        return luaL_error(L, "div by zero");
    }
	lua_pushuint64(L, lhs % rhs);
	return 1;
}

LUALIB_API int uint64_parse(lua_State* L)
{
    /* lua_tostring returns NULL for non-strings, and strtoull(NULL) crashes. */
    const char* str = luaL_checkstring(L, 1);
    lua_pushuint64(L, strtoull(str, NULL, 0));
    return 1;
}

LUALIB_API int luaopen_i64lib(lua_State* L)
{
#if LUA_VERSION_NUM == 501
    lua_newtable(L);
	
    lua_pushcfunction(L, int64_add);
    lua_setfield(L, -2, "__add");

    lua_pushcfunction(L, int64_sub);
    lua_setfield(L, -2, "__sub");

    lua_pushcfunction(L, int64_mul);
    lua_setfield(L, -2, "__mul");

    lua_pushcfunction(L, int64_div);
    lua_setfield(L, -2, "__div");

    lua_pushcfunction(L, int64_mod);
    lua_setfield(L, -2, "__mod");

    lua_pushcfunction(L, int64_unm);
    lua_setfield(L, -2, "__unm");

    lua_pushcfunction(L, int64_pow);
    lua_setfield(L, -2, "__pow");    

    lua_pushcfunction(L, int64_tostring);
    lua_setfield(L, -2, "__tostring");        

    lua_pushcfunction(L, int64_concat);
    lua_setfield(L, -2, "__concat");

    lua_pushcfunction(L, int64_eq);
    lua_setfield(L, -2, "__eq");  

    lua_pushcfunction(L, int64_lt);
    lua_setfield(L, -2, "__lt"); 

    lua_pushcfunction(L, int64_le);
    lua_setfield(L, -2, "__le");
	
	lua_rawseti(L, LUA_REGISTRYINDEX, INT64_META_REF);
#endif
    lua_newtable(L);
	
	lua_pushcfunction(L, uint64_tostring);
	lua_setfield(L, -2, "tostring");
	
	lua_pushcfunction(L, uint64_compare);
	lua_setfield(L, -2, "compare");
	
	lua_pushcfunction(L, uint64_divide);
	lua_setfield(L, -2, "divide");
	
	lua_pushcfunction(L, uint64_remainder);
	lua_setfield(L, -2, "remainder");
	
	lua_pushcfunction(L, uint64_parse);
	lua_setfield(L, -2, "parse");
#if LUA_VERSION_NUM == 501
	{
		int i;
		for (i = 0; i < (int)(sizeof(uint64_op_names) / sizeof(uint64_op_names[0])); i++) {
			lua_pushinteger(L, i);
			lua_pushcclosure(L, uint64_op, 1);
			lua_setfield(L, -2, uint64_op_names[i]);
		}
	}
	lua_pushcfunction(L, uint64_bnot);
	lua_setfield(L, -2, "bnot");
#endif
	
	lua_setglobal(L, "uint64");
	return 0;
}

