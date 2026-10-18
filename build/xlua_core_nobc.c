/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* Core builds only: replaces Lua's lundump.c and ldump.c, so the library
 * contains neither the bytecode loader nor the dumper
 * (docs/design/core-luascript.md, section 3.9). LuaScript loads text with
 * mode "t" everywhere; this is the defence behind that: even a load with
 * mode "b" or "B" reaches no bytecode reader. Lua 5.4 and 5.5, whose
 * luaU_undump signatures differ (lundump.h:29 in 5.4.9, 33 in 5.5.1).
 * With CMake XLUA_TRUSTED_BYTECODE the build keeps Lua's lundump.c for the
 * host's own precompiled chunks (xlua_core_run_mode) and this file only
 * replaces the dumper. */

#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "ldo.h"
#include "lobject.h"
#include "lundump.h"
#endif

#if !defined(XLUA_TRUSTED_BYTECODE)
/* Raises the error lua_load reports, as ldo.c's checkmode does for a chunk
 * of the wrong kind. */
#if LUA_VERSION_NUM >= 505
LClosure *luaU_undump(lua_State *L, ZIO *Z, Table *anchor, const char *name, int fixed) {
	(void)anchor;
	(void)fixed;
#else
LClosure *luaU_undump(lua_State *L, ZIO *Z, const char *name) {
#endif
	(void)Z;
	(void)name;
	luaO_pushfstring(L, "binary chunks are not supported");
	luaD_throw(L, LUA_ERRSYNTAX);  /* does not return */
}
#endif

/* lua_dump returns this as its status: string.dump, if a host put it back,
 * fails with "unable to dump given function". */
int luaU_dump(lua_State *L, const Proto *f, lua_Writer w, void *data, int strip) {
	(void)L;
	(void)f;
	(void)w;
	(void)data;
	(void)strip;
	return 1;
}
