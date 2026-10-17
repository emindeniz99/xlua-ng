/*
 * xlua-ng: hooks that xLua's build compiles into the vendored Lua 5.4.
 * luaconf.h.in includes this file in its "Local configuration" space when
 * XLUA_LUA_HOOKS is defined, which build/CMakeLists.txt does for libxlua only
 * (not for the stand-alone test interpreter). See build/LUA_PATCHES.md and
 * docs/design/core-luascript.md, section 3.10. Licensed under the MIT License,
 * like the rest of xLua.
 *
 * luaconf.h comes before lua.h declares lua_State, hence struct lua_State.
 */
#ifndef XLUA_LUAHOOKS_H
#define XLUA_LUAHOOKS_H

/* The string hash seed of a new state (lstate.c, lua_newstate): the seed of
 * a core state created with LuaOptions.HashSeed, otherwise upstream's
 * mix of the time and addresses. Defined in build/xlua_core.c. */
struct lua_State;
LUAI_FUNC unsigned int xlua_core_makeseed (struct lua_State *L);
#define luai_makeseed(L)	xlua_core_makeseed(L)

#endif
