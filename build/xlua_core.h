/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* The internal interface between xlua_core.c, the native layer every preset
 * builds, and xlua.c, the C# bridge that the legacy and minimal presets build
 * on top of it (docs/design/core-luascript.md, section 3.1). This is not a
 * public header and nothing declared here is exported from the library.
 * Include it after the Lua headers. */
#ifndef XLUA_CORE_H
#define XLUA_CORE_H

#include <stdint.h>

/* A function that several of xLua's own C files share but the library does
 * not export, like Lua's LUAI_FUNC: hidden visibility on ELF and Mach-O, a
 * plain declaration elsewhere (Windows DLLs export only what LUA_API marks). */
#if defined(__GNUC__) && (defined(__ELF__) || defined(__APPLE__))
#define XLUA_INTERNAL __attribute__((visibility("hidden"))) extern
#else
#define XLUA_INTERNAL extern
#endif

/* Pushes one of the static helpers of a protected call (xlua_pgettable and
 * the other protected exports). Lua 5.2+ pushes a light C function, which
 * allocates nothing. Lua 5.1 (LuaJIT) creates a new C closure on every
 * lua_pushcfunction, so there each helper is created once per state and kept
 * in the registry under its own address. ISO C has no conversion from a
 * function pointer to void *; through uintptr_t it is implementation-defined
 * and keeps the address on every supported target, which is all a unique key
 * needs (-Wpedantic). An inline function, so that xlua.c and xlua_core.c
 * compile it into each caller as before the split. */
static inline void push_helper(lua_State *L, lua_CFunction fn) {
#if LUA_VERSION_NUM == 501
	lua_pushlightuserdata(L, (void *)(uintptr_t)fn); // NOLINT(performance-no-int-to-ptr): a registry key, never dereferenced
	lua_rawget(L, LUA_REGISTRYINDEX);
	if (lua_isfunction(L, -1)) return;
	lua_pop(L, 1);
	lua_pushcfunction(L, fn);
	lua_pushlightuserdata(L, (void *)(uintptr_t)fn); // NOLINT(performance-no-int-to-ptr): a registry key, never dereferenced
	lua_pushvalue(L, -2);
	lua_rawset(L, LUA_REGISTRYINDEX);
#else
	lua_pushcfunction(L, fn);
#endif
}

/* Defined in xlua.c: reports the return of a C# function to the xlua.sethook
 * profiler. The C# function trampolines in xlua_core.c call it when a hook is
 * set. It moves behind core's hook owner with the instruction limits
 * (core-luascript.md, sections 3.5 and 8, milestone 3). */
XLUA_INTERNAL void xlua_profiler_return(lua_State *L);

#endif
