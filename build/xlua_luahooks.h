/*
 * xlua-ng: hooks that xLua's build compiles into the vendored Lua 5.4 and
 * 5.5. Their luaconf.h (luaconf.h.in for 5.4) includes this file in its
 * "Local configuration" space when XLUA_LUA_HOOKS is defined, which
 * build/CMakeLists.txt does for libxlua only (not for the stand-alone test
 * interpreter). See build/LUA_PATCHES.md and docs/design/core-luascript.md,
 * sections 3.10 and 3.11. Licensed under the MIT License, like the rest of
 * xLua.
 *
 * luaconf.h comes before lua.h declares lua_State, hence struct lua_State.
 */
#ifndef XLUA_LUAHOOKS_H
#define XLUA_LUAHOOKS_H

struct lua_State;

/* Not exported (LUAI_FUNC is not defined yet in Lua 5.5's luaconf.h). */
#if defined(__GNUC__) && (defined(__ELF__) || defined(__APPLE__))
#define XLUA_HOOK_API __attribute__((visibility("hidden"))) extern
#else
#define XLUA_HOOK_API extern
#endif

/* Lua 5.4 only (its luaconf.h.in defines XLUA_HOOK_MAKESEED; 5.5's
 * lua_newstate takes the seed): the string hash seed of a new state
 * (lstate.c, lua_newstate), the seed of a core state created with
 * LuaOptions.HashSeed, otherwise upstream's mix of the time and addresses.
 * Defined in build/xlua_core.c. */
#if defined(XLUA_HOOK_MAKESEED)
XLUA_HOOK_API unsigned int xlua_core_makeseed (struct lua_State *L);
#define luai_makeseed(L)	xlua_core_makeseed(L)
#endif

/* The C budget (lstrlib.c match and lmemfind, ltablib.c tmove): charges n
 * instruction-equivalents to a limited call and raises when it is over its
 * instruction or time limit or was interrupted. Defined in build/xlua_core.c;
 * returns at once for a state without limits. */
XLUA_HOOK_API void xlua_core_budget (struct lua_State *L, int n);
#define luai_budget(L,n)	xlua_core_budget(L, n)

#endif
