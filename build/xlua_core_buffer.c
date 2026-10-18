/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* The buffer library of LuaScript (docs/design/core-luascript.md, section
 * 2.10; milestone 10): a fixed-size, mutable array of bytes as a full
 * userdata, with the function names of Luau's buffer library
 * (https://luau.org/library#buffer-library; the names only, this is xlua-ng's
 * own implementation). LuaOptions.Libraries opens it with LuaLibs.Buffer.
 *
 * Rules:
 *  - offsets are 0-based integers, sizes and counts integers >= 0; every access
 *    is bounds-checked and fails with "buffer access out of bounds";
 *  - multi-byte values are little-endian and may be unaligned;
 *  - integer reads return Lua integers (readu32 up to 4294967295); integer
 *    writes take Lua integers (or floats with an exact integer value, as every
 *    Lua library function) and keep the low 8, 16 or 32 bits; readi64 and
 *    writei64 (not in Luau) move a whole 64-bit Lua integer, so nothing is lost;
 *  - writef32 rounds to the nearest float; values beyond its range become an
 *    infinity, as IEEE 754 does;
 *  - a buffer holds at most XLUA_BUFFER_MAX bytes (1 GiB, Luau's limit). Its
 *    memory goes through the state's allocator, so MemoryLimit counts it;
 *  - operations on 1 KiB or more charge one instruction-equivalent per 32
 *    bytes to the C budget (section 3.11), 128 KiB at a time, so that
 *    InstructionLimit, TimeLimit and Interrupt stop a large fill or copy;
 *  - the metatable is locked (__metatable = false) and has only __name, so
 *    tostring shows "buffer: 0x..." and error messages say "buffer value";
 *    type(b) is "userdata", and buffer.isbuffer(v) (not in Luau) tells buffers
 *    apart.
 *
 * xlua_core_handle (xlua_core_sandbox.c) creates buffers for C# and reads
 * their memory (LuaBufferRef, section 2.10) through the functions at the end.
 * The CMake option XLUA_BUFFER builds the library (on in the core preset);
 * without it those operations report XLUA_ERRCONFIG, so the export list does
 * not depend on the option. */

#define LUA_LIB

#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "lualib.h"
#include "lauxlib.h"
#endif

#include <math.h>
#include <string.h>

#if !defined(XLUA_AMALGAMATION)
#include "lstate.h"
#endif

#include "xlua_core.h"

#if defined(XLUA_BUFFER)

/* Registry key (its address): the metatable of every buffer of the state. */
static const char buffer_mt_key = 0;

#define BUFFER_CHUNK ((size_t)128 * 1024)  /* bytes between two checks of the C budget */
#define BUFFER_CHARGE_MIN ((size_t)1024)   /* smaller operations cost like an instruction */

/* Charges an operation on n bytes to the C budget: raises when the call is over
 * its instruction or time limit or was interrupted. */
static void charge(lua_State *L, size_t n) {
	if (n >= BUFFER_CHARGE_MIN) {
		xlua_core_budget(L, (int)(n >> 5));  /* n <= XLUA_BUFFER_MAX: at most 2^25 */
	}
}

/* memset in chunks, each charged first. L NULL: no charge (the C exports). */
static void fill_bytes(lua_State *L, unsigned char *p, int value, size_t n) {
	while (n > 0) {
		size_t k = n < BUFFER_CHUNK ? n : BUFFER_CHUNK;
		if (L != NULL) {
			charge(L, k);
		}
		memset(p, value, k);
		p += k;
		n -= k;
	}
}

/* memmove in chunks, each charged first; overlapping ranges are copied as
 * through a temporary buffer (backwards when the target is above the source). */
static void move_bytes(lua_State *L, unsigned char *dst, const unsigned char *src, size_t n) {
	/* As integers: C leaves the order of pointers into two buffers undefined. */
	uintptr_t d = (uintptr_t)dst, s = (uintptr_t)src;
	if (d == s || n == 0) {
		return;
	}
	if (d < s || d - s >= n) {
		while (n > 0) {
			size_t k = n < BUFFER_CHUNK ? n : BUFFER_CHUNK;
			charge(L, k);
			memmove(dst, src, k);
			dst += k;
			src += k;
			n -= k;
		}
	}
	else {
		while (n > 0) {
			size_t k = n < BUFFER_CHUNK ? n : BUFFER_CHUNK;
			charge(L, k);
			n -= k;
			memmove(dst + n, src + n, k);
		}
	}
}

/* The bytes of the buffer at idx and its length, or NULL when the value is not
 * a buffer of this state. Non-raising; needs two free stack slots. */
void *xlua_core_buffer_at(lua_State *L, int idx, size_t *len) {
	void *p;
	int same;
	if (lua_type(L, idx) != LUA_TUSERDATA || !lua_getmetatable(L, idx)) {
		return NULL;
	}
	lua_rawgetp(L, LUA_REGISTRYINDEX, &buffer_mt_key);
	same = lua_rawequal(L, -1, -2);
	lua_pop(L, 2);
	if (!same) {
		return NULL;
	}
	p = lua_touserdata(L, idx);
	*len = lua_rawlen(L, idx);
	return p;
}

/* The buffer argument arg of a library function, whose upvalue 1 is the
 * metatable (faster than the registry). */
static unsigned char *check_buffer(lua_State *L, int arg, size_t *len) {
	if (lua_type(L, arg) == LUA_TUSERDATA && lua_getmetatable(L, arg)) {
		int same = lua_rawequal(L, -1, lua_upvalueindex(1));
		lua_pop(L, 1);
		if (same) {
			*len = lua_rawlen(L, arg);
			return (unsigned char *)lua_touserdata(L, arg);
		}
	}
	luaL_typeerror(L, arg, "buffer");
	return NULL;
}

/* A count argument: an integer >= 0 (opt: its default when absent or nil). */
static lua_Integer check_count(lua_State *L, int arg, lua_Integer def, int opt) {
	lua_Integer n = opt ? luaL_optinteger(L, arg, def) : luaL_checkinteger(L, arg);
	if (n < 0) {
		luaL_argerror(L, arg, "count must not be negative");
	}
	return n;
}

/* The offset off (argument arg) for n bytes of a buffer of len bytes. */
static size_t check_range(lua_State *L, int arg, lua_Integer off, size_t len, lua_Integer n) {
	if (off < 0 || (lua_Unsigned)off > (lua_Unsigned)len || (lua_Unsigned)n > (lua_Unsigned)(len - (size_t)off)) {
		luaL_argerror(L, arg, lua_pushfstring(L, "buffer access out of bounds (offset %I and %I bytes in a buffer of %I bytes)",
		                                      off, n, (lua_Integer)len));
	}
	return (size_t)off;
}

/* Pushes a new buffer of n bytes (n <= XLUA_BUFFER_MAX), zero-filled; the
 * metatable is at index mt. */
static unsigned char *new_buffer(lua_State *L, size_t n, int mt, int charged) {
	unsigned char *p = (unsigned char *)lua_newuserdatauv(L, n, 0);
	lua_pushvalue(L, mt);
	lua_setmetatable(L, -2);
	fill_bytes(charged ? L : NULL, p, 0, n);
	return p;
}

static size_t check_size(lua_State *L, int arg) {
	lua_Integer n = luaL_checkinteger(L, arg);
	if (n < 0 || (lua_Unsigned)n > (lua_Unsigned)XLUA_BUFFER_MAX) {
		luaL_argerror(L, arg, lua_pushfstring(L, "size out of range (0 to %I bytes)", (lua_Integer)XLUA_BUFFER_MAX));
	}
	return (size_t)n;
}

/* buffer.create(size) */
static int buf_create(lua_State *L) {
	size_t n = check_size(L, 1);
	new_buffer(L, n, lua_upvalueindex(1), 1);
	return 1;
}

/* buffer.fromstring(s) */
static int buf_fromstring(lua_State *L) {
	size_t n;
	const char *s = luaL_checklstring(L, 1, &n);
	unsigned char *p;
	if (n > XLUA_BUFFER_MAX) {
		luaL_argerror(L, 1, lua_pushfstring(L, "string too long for a buffer (more than %I bytes)", (lua_Integer)XLUA_BUFFER_MAX));
	}
	p = (unsigned char *)lua_newuserdatauv(L, n, 0);
	lua_pushvalue(L, lua_upvalueindex(1));
	lua_setmetatable(L, -2);
	move_bytes(L, p, (const unsigned char *)s, n);
	return 1;
}

/* buffer.tostring(b) */
static int buf_tostring(lua_State *L) {
	size_t n;
	const unsigned char *p = check_buffer(L, 1, &n);
	charge(L, n);  /* one copy inside Lua: charged in advance */
	lua_pushlstring(L, (const char *)p, n);
	return 1;
}

/* buffer.isbuffer(v): whether v is a buffer (of this state). */
static int buf_isbuffer(lua_State *L) {
	int same = 0;
	luaL_checkany(L, 1);
	if (lua_type(L, 1) == LUA_TUSERDATA && lua_getmetatable(L, 1)) {
		same = lua_rawequal(L, -1, lua_upvalueindex(1));
	}
	lua_pushboolean(L, same);
	return 1;
}

/* buffer.len(b) */
static int buf_len(lua_State *L) {
	size_t n;
	check_buffer(L, 1, &n);
	lua_pushinteger(L, (lua_Integer)n);
	return 1;
}

/* Little-endian loads and stores of 1 to 8 bytes: byte by byte, so any
 * alignment and host byte order works; compilers merge them into one access. */
static uint64_t load_le(const unsigned char *p, int size) {
	uint64_t v = 0;
	int i;
	for (i = size - 1; i >= 0; i--) {
		v = (v << 8) | p[i];
	}
	return v;
}

static void store_le(unsigned char *p, uint64_t v, int size) {
	int i;
	for (i = 0; i < size; i++) {
		p[i] = (unsigned char)(v & 0xff);
		v >>= 8;
	}
}

/* The integer and float accessors, by width and signedness. */
enum { K_I8, K_U8, K_I16, K_U16, K_I32, K_U32, K_F32, K_F64, K_I64 };
static const unsigned char kind_size[] = {1, 1, 2, 2, 4, 4, 4, 8, 8};

/* The float nearest to d (round to nearest even), an infinity beyond the
 * float range as IEEE 754 rounds, without the out-of-range conversion that
 * C leaves undefined. 0x1.ffffffp127 is FLT_MAX plus half its ulp, the first
 * value that rounds to infinity. */
static float to_float(double d) {
	if (d >= 0x1.ffffffp127) {
		return (float)HUGE_VAL;
	}
	if (d <= -0x1.ffffffp127) {
		return (float)-HUGE_VAL;
	}
	return (float)d;  /* NaN stays NaN */
}

static int buf_read(lua_State *L, int kind) {
	size_t len, off;
	const unsigned char *p = check_buffer(L, 1, &len);
	int size = kind_size[kind];
	uint64_t u;
	off = check_range(L, 2, luaL_checkinteger(L, 2), len, size);
	u = load_le(p + off, size);
	switch (kind) {
	case K_I8: case K_I16: case K_I32: {
		uint64_t sign = (uint64_t)1 << (size * 8 - 1);
		/* two's complement without an implementation-defined conversion */
		lua_Integer v = (u & sign) ? -(lua_Integer)((sign << 1) - u) : (lua_Integer)u;
		lua_pushinteger(L, v);
		break;
	}
	case K_U8: case K_U16: case K_U32:
		lua_pushinteger(L, (lua_Integer)u);
		break;
	case K_I64:  /* all 64 bits; Lua's own unsigned-to-signed cast (l_castU2S) */
		lua_pushinteger(L, (lua_Integer)(lua_Unsigned)u);
		break;
	case K_F32: {
		uint32_t bits = (uint32_t)u;
		float f;
		memcpy(&f, &bits, sizeof(f));
		lua_pushnumber(L, (lua_Number)f);
		break;
	}
	default: {
		double d;
		memcpy(&d, &u, sizeof(d));
		lua_pushnumber(L, (lua_Number)d);
		break;
	}
	}
	return 1;
}

static int buf_write(lua_State *L, int kind) {
	size_t len, off;
	unsigned char *p = check_buffer(L, 1, &len);
	int size = kind_size[kind];
	uint64_t u;
	off = check_range(L, 2, luaL_checkinteger(L, 2), len, size);
	if (kind == K_F32) {
		float f = to_float((double)luaL_checknumber(L, 3));
		uint32_t bits;
		memcpy(&bits, &f, sizeof(bits));
		u = bits;
	}
	else if (kind == K_F64) {
		double d = (double)luaL_checknumber(L, 3);
		memcpy(&u, &d, sizeof(u));
	}
	else {
		u = (uint64_t)(lua_Unsigned)luaL_checkinteger(L, 3);  /* the low bits are stored */
	}
	store_le(p + off, u, size);
	return 0;
}

#define ACCESSORS(name, kind) \
	static int buf_read##name(lua_State *L) { return buf_read(L, kind); } \
	static int buf_write##name(lua_State *L) { return buf_write(L, kind); }
ACCESSORS(i8, K_I8)
ACCESSORS(u8, K_U8)
ACCESSORS(i16, K_I16)
ACCESSORS(u16, K_U16)
ACCESSORS(i32, K_I32)
ACCESSORS(u32, K_U32)
ACCESSORS(f32, K_F32)
ACCESSORS(f64, K_F64)
ACCESSORS(i64, K_I64)
#undef ACCESSORS

/* buffer.readstring(b, offset, count) */
static int buf_readstring(lua_State *L) {
	size_t len, off;
	const unsigned char *p = check_buffer(L, 1, &len);
	lua_Integer n = check_count(L, 3, 0, 0);
	off = check_range(L, 2, luaL_checkinteger(L, 2), len, n);
	charge(L, (size_t)n);
	lua_pushlstring(L, (const char *)p + off, (size_t)n);
	return 1;
}

/* buffer.writestring(b, offset, s [, count]): count is at most #s. */
static int buf_writestring(lua_State *L) {
	size_t len, off, slen;
	unsigned char *p = check_buffer(L, 1, &len);
	const char *s = luaL_checklstring(L, 3, &slen);
	lua_Integer n = check_count(L, 4, (lua_Integer)slen, 1);
	if ((lua_Unsigned)n > (lua_Unsigned)slen) {
		luaL_argerror(L, 4, lua_pushfstring(L, "count %I is larger than the string (%I bytes)", n, (lua_Integer)slen));
	}
	off = check_range(L, 2, luaL_checkinteger(L, 2), len, n);
	move_bytes(L, p + off, (const unsigned char *)s, (size_t)n);
	return 0;
}

/* buffer.copy(target, targetOffset, source [, sourceOffset [, count]]):
 * sourceOffset defaults to 0, count to the rest of the source. */
static int buf_copy(lua_State *L) {
	size_t tlen, slen, toff, soff;
	unsigned char *t = check_buffer(L, 1, &tlen);
	const unsigned char *s = check_buffer(L, 3, &slen);
	lua_Integer so = luaL_optinteger(L, 4, 0);
	lua_Integer n;
	if (so < 0 || (lua_Unsigned)so > (lua_Unsigned)slen) {
		luaL_argerror(L, 4, lua_pushfstring(L, "buffer access out of bounds (offset %I in a buffer of %I bytes)", so, (lua_Integer)slen));
	}
	n = check_count(L, 5, (lua_Integer)(slen - (size_t)so), 1);
	soff = check_range(L, 4, so, slen, n);
	toff = check_range(L, 2, luaL_checkinteger(L, 2), tlen, n);
	move_bytes(L, t + toff, s + soff, (size_t)n);
	return 0;
}

/* buffer.fill(b, offset, value [, count]): value's low 8 bits, count defaults
 * to the rest of the buffer. */
static int buf_fill(lua_State *L) {
	size_t len, off;
	unsigned char *p = check_buffer(L, 1, &len);
	lua_Integer o = luaL_checkinteger(L, 2);
	int value = (int)((lua_Unsigned)luaL_checkinteger(L, 3) & 0xff);
	lua_Integer n;
	if (o < 0 || (lua_Unsigned)o > (lua_Unsigned)len) {
		luaL_argerror(L, 2, lua_pushfstring(L, "buffer access out of bounds (offset %I in a buffer of %I bytes)", o, (lua_Integer)len));
	}
	n = check_count(L, 4, (lua_Integer)(len - (size_t)o), 1);
	off = check_range(L, 2, o, len, n);
	fill_bytes(L, p + off, value, (size_t)n);
	return 0;
}

static const luaL_Reg buffer_funcs[] = {
	{"create", buf_create},
	{"fromstring", buf_fromstring},
	{"tostring", buf_tostring},
	{"len", buf_len},
	{"readi8", buf_readi8}, {"readu8", buf_readu8},
	{"readi16", buf_readi16}, {"readu16", buf_readu16},
	{"readi32", buf_readi32}, {"readu32", buf_readu32},
	{"readf32", buf_readf32}, {"readf64", buf_readf64},
	{"readi64", buf_readi64}, {"writei64", buf_writei64},
	{"isbuffer", buf_isbuffer},
	{"writei8", buf_writei8}, {"writeu8", buf_writeu8},
	{"writei16", buf_writei16}, {"writeu16", buf_writeu16},
	{"writei32", buf_writei32}, {"writeu32", buf_writeu32},
	{"writef32", buf_writef32}, {"writef64", buf_writef64},
	{"readstring", buf_readstring},
	{"writestring", buf_writestring},
	{"copy", buf_copy},
	{"fill", buf_fill},
	{NULL, NULL}
};

/* luaopen for the sandbox (xlua_core_open with XLUA_LUALIBS_BUFFER): the
 * library table; its functions share the metatable as upvalue 1, which the
 * registry also holds for the C exports and xlua_core_call. */
int xlua_core_open_buffer(lua_State *L) {
	luaL_newlibtable(L, buffer_funcs);
	lua_createtable(L, 0, 2);
	lua_pushliteral(L, XLUA_BUFFERLIBNAME);
	lua_setfield(L, -2, "__name");
	lua_pushboolean(L, 0);
	lua_setfield(L, -2, "__metatable");
	lua_pushvalue(L, -1);
	lua_rawsetp(L, LUA_REGISTRYINDEX, &buffer_mt_key);
	luaL_setfuncs(L, buffer_funcs, 1);
	return 1;
}

/* A new buffer for C# (XLUA_HANDLE_NEWBUFFER, LuaScript.NewBuffer): pushes it
 * and returns its bytes, or returns NULL and pushes nothing in a state without
 * the library. Not charged to the C budget. */
void *xlua_core_new_buffer(lua_State *L, size_t len) {
	if (lua_rawgetp(L, LUA_REGISTRYINDEX, &buffer_mt_key) != LUA_TTABLE) {
		lua_pop(L, 1);
		return NULL;
	}
	return new_buffer(L, len, lua_gettop(L), 0);
}

#else  /* !XLUA_BUFFER: nothing; xlua_core_handle refuses the buffer operations */
typedef int xlua_core_buffer_off;  /* ISO C wants a declaration in every file */
#endif
