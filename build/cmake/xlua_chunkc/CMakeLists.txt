# Tencent is pleased to support the open source community by making xLua available.
# Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
# Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
# http://opensource.org/licenses/MIT
# Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.

# The host chunk compiler (build/xlua_chunkc.c) of a cross-compiled
# XLUA_BYTECODE_ONLY build. build/CMakeLists.txt configures and builds this
# project with the host's compiler when it configures for another system
# (Android, iOS, OpenHarmony, UWP, Windows ARM64 on x64), and passes the
# settings that decide the bytecode format: LUA_VERSION and
# LUAC_COMPATIBLE_FORMAT. It is built from the same vendored Lua sources and
# luaconf.h as the library. A native build makes the same program as a target
# of the library's own project instead (build/README.md, "Bytecode-only builds").

cmake_minimum_required(VERSION 3.22...4.4)
project(xlua_chunkc C)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
if (MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif ()
get_property(xlua_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
if (NOT xlua_multi_config AND NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type (default Release)" FORCE)
endif ()

if (NOT XLUA_SOURCE_DIR OR NOT LUA_VERSION)
    message(FATAL_ERROR "build/CMakeLists.txt configures this project with XLUA_SOURCE_DIR and LUA_VERSION")
endif ()
option(LUAC_COMPATIBLE_FORMAT "Lua 5.3's 32/64-bit bytecode format, as the library" OFF)
set(LUA_IDSIZE 120 CACHE STRING "LUA_IDSIZE of the library")
set(lua_src ${XLUA_SOURCE_DIR}/lua-${LUA_VERSION}/src)

if (EXISTS ${lua_src}/luaconf.h.in)
    configure_file(${lua_src}/luaconf.h.in ${CMAKE_CURRENT_BINARY_DIR}/luaconf.h)
else ()
    add_definitions(-DLUA_IDSIZE=${LUA_IDSIZE})
endif ()
include_directories(${lua_src} ${CMAKE_CURRENT_BINARY_DIR})

aux_source_directory(${lua_src} lua_core)
list(REMOVE_ITEM lua_core ${lua_src}/lua.c ${lua_src}/luac.c)
add_executable(xlua_chunkc ${XLUA_SOURCE_DIR}/xlua_chunkc.c ${lua_core})
if (LUAC_COMPATIBLE_FORMAT)
    target_compile_definitions(xlua_chunkc PRIVATE LUAC_COMPATIBLE_FORMAT)
endif ()
if (NOT WIN32)
    target_link_libraries(xlua_chunkc m)
endif ()
# One path for every generator: $<0:> keeps multi-config generators from adding Release/.
set_target_properties(xlua_chunkc PROPERTIES RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/bin$<0:>)
