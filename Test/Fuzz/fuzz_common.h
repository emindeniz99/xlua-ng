/* Shared setup for the libFuzzer harnesses in Test/Fuzz (Lua 5.3, 5.4, 5.5;
 * fuzz_i64.c also LuaJIT).
 * Each input gets a fresh lua_State with a memory cap, an instruction cap and
 * the libraries xLua's LuaEnv opens (luaopen_xlua, luaopen_i64lib). */
#ifndef XLUA_FUZZ_COMMON_H
#define XLUA_FUZZ_COMMON_H

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"

/* Exported by build/xlua.c and build/i64lib.c without a public header. */
LUA_API void luaopen_xlua(lua_State *L);
LUALIB_API int luaopen_i64lib(lua_State *L);
LUA_API void *xlua_tag(void);
LUA_API void *xlua_pushstruct(lua_State *L, unsigned int size, int meta_ref);
LUA_API int lua_setfenv(lua_State *L, int idx);
LUALIB_API int xluaL_loadbuffer(lua_State *L, const char *buff, int size, const char *name);

#define FUZZ_MEMORY_LIMIT ((size_t)256 << 20)
#define FUZZ_HOOK_PERIOD 1000
#define FUZZ_HOOK_LIMIT 1000 /* times FUZZ_HOOK_PERIOD VM instructions */

typedef struct {
    size_t used;
} FuzzAlloc;

static FuzzAlloc fuzz_alloc_state;
static int fuzz_hook_calls;

static void *fuzz_alloc(void *ud, void *ptr, size_t osize, size_t nsize) {
    FuzzAlloc *a = (FuzzAlloc *)ud;
    size_t old = ptr ? osize : 0;
    if (nsize == 0) {
        free(ptr);
        a->used -= old;
        return NULL;
    }
    if (nsize > old && a->used + (nsize - old) > FUZZ_MEMORY_LIMIT) {
        return NULL; /* Lua raises a memory error; growth only, shrinking never fails */
    }
    void *p = realloc(ptr, nsize);
    if (p != NULL) {
        a->used = a->used - old + nsize;
    }
    return p;
}

/* Raise an error every FUZZ_HOOK_PERIOD instructions once the budget is spent.
 * Switching to a period of 1 makes the error escape loops that pcall it. */
static void fuzz_count_hook(lua_State *L, lua_Debug *ar) {
    (void)ar;
    if (++fuzz_hook_calls > FUZZ_HOOK_LIMIT) {
        lua_sethook(L, fuzz_count_hook, LUA_MASKCOUNT, 1);
        luaL_error(L, "fuzz instruction limit");
    }
}

static lua_State *fuzz_new_state(void) {
    lua_State *L;
    fuzz_alloc_state.used = 0;
    fuzz_hook_calls = 0;
#if LUA_VERSION_NUM == 501
    /* LuaJIT (GC64) needs its objects below 2^47, which the sanitizer heap
     * does not promise, so it keeps its own allocator and has no memory cap. */
    L = luaL_newstate();
#elif LUA_VERSION_NUM >= 505
    L = lua_newstate(fuzz_alloc, &fuzz_alloc_state, 0); /* fixed seed: reproducible */
#else
    L = lua_newstate(fuzz_alloc, &fuzz_alloc_state);
#endif
    if (L == NULL) {
        abort();
    }
    luaopen_xlua(L);    /* luaL_openlibs plus the xlua table, as LuaEnv does */
    luaopen_i64lib(L);  /* the uint64 table */
    lua_sethook(L, fuzz_count_hook, LUA_MASKCOUNT, FUZZ_HOOK_PERIOD);
    return L;
}

/* A struct userdata as ObjectTranslator creates it: the metatable carries the
 * xlua tag (checked by xlua.structclone) and a type id at [1]. */
static void *fuzz_push_struct(lua_State *L, unsigned int size) {
    void *css;
    lua_newtable(L);
    lua_pushlightuserdata(L, xlua_tag());
    lua_pushboolean(L, 1);
    lua_rawset(L, -3);
    lua_pushinteger(L, 1);
    lua_rawseti(L, -2, 1);
    int ref = luaL_ref(L, LUA_REGISTRYINDEX);
    css = xlua_pushstruct(L, size, ref);
    luaL_unref(L, LUA_REGISTRYINDEX, ref);
    return css;
}

/* Reads little-endian values from the fuzz input; returns 0 when exhausted. */
typedef struct {
    const uint8_t *data;
    size_t size;
} FuzzInput;

static uint64_t fuzz_take(FuzzInput *in, size_t bytes) {
    uint64_t value = 0;
    size_t i;
    for (i = 0; i < bytes && in->size > 0; i++) {
        value |= (uint64_t)in->data[0] << (8 * i);
        in->data++;
        in->size--;
    }
    return value;
}

#endif
