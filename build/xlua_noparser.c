/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* Bytecode-only builds (CMake XLUA_BYTECODE_ONLY): replaces Lua's lparser.c,
 * lcode.c and llex.c, so the library contains no compiler and loads only
 * precompiled chunks (build/README.md, "Bytecode-only builds"). ldo.c's
 * f_parser calls luaY_parser for every text chunk that the load mode lets
 * through, and lstate.c calls luaX_init once per state; nothing else in Lua
 * references the three files. Lua 5.3, 5.4 and 5.5, whose luaY_parser
 * signatures differ (lparser.h:129 in 5.3.6, 167 in 5.4.9, 192 in 5.5.1). */

#include <string.h>

#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "ldo.h"
#include "lobject.h"
#include "lparser.h"
#include "llex.h"
#endif

/* llex.c creates the reserved words and fixes them in memory. Without the
 * lexer nothing looks them up. */
void luaX_init(lua_State *L) {
	(void)L;
}

/* Raises the error lua_load reports, with LUA_ERRSYNTAX as ldo.c's checkmode
 * does for a chunk of the wrong kind; the message starts with the chunk name
 * as Lua's own syntax errors do. */
#if LUA_VERSION_NUM >= 505
LClosure *luaY_parser(lua_State *L, ZIO *z, Table *anchor, Mbuffer *buff, Dyndata *dyd,
                      const char *name, int firstchar) {
	(void)anchor;
#else
LClosure *luaY_parser(lua_State *L, ZIO *z, Mbuffer *buff, Dyndata *dyd,
                      const char *name, int firstchar) {
#endif
	char id[LUA_IDSIZE];
	(void)z;
	(void)buff;
	(void)dyd;
	(void)firstchar;
#if LUA_VERSION_NUM == 503
	luaO_chunkid(id, name, LUA_IDSIZE);
#else
	luaO_chunkid(id, name, strlen(name));
#endif
	luaO_pushfstring(L, "%s: text chunks are not supported in this build (bytecode only)", id);
	luaD_throw(L, LUA_ERRSYNTAX);  /* does not return */
}
