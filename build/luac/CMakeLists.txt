# Tencent is pleased to support the open source community by making xLua available.
# Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
# Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
# http://opensource.org/licenses/MIT
# Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.

cmake_minimum_required(VERSION 3.22...4.4)

project(luac)

# All C sources (xLua's own and the vendored libraries) compile as C17. GNU
# extensions stay on (gnu17), because Lua's POSIX build needs declarations
# such as popen and mkstemp. Assets/Plugins/WebGL/xlua_webgl.cpp includes the
# same files in a C++ translation unit, so xLua's own C must also stay valid C++.
set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Desktop Windows links the static C runtime, like LuaJIT's msvcbuild_mt.bat.
if (MSVC AND NOT CMAKE_SYSTEM_NAME STREQUAL "WindowsStore")
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif ()


find_path(LUAC_PROJECT_DIR NAMES SConstruct
    PATHS 
    ${CMAKE_SOURCE_DIR}
    NO_DEFAULT_PATH
    )

MARK_AS_ADVANCED(LUAC_PROJECT_DIR)

# Standard lua/luac for any bundled runtime (default 5.5.1, like the plugin).
# LUAC_COMPATIBLE_FORMAT builds xLua's 32/64-bit portable bytecode, which
# exists only for Lua 5.3; see Assets/XLua/Doc/compatible_bytecode.md.
if (NOT LUA_VERSION)
    set(LUA_VERSION "5.5.1")
endif ()
if (LUAC_COMPATIBLE_FORMAT AND NOT LUA_VERSION MATCHES "^5[.]3[.]")
    message(FATAL_ERROR "LUAC_COMPATIBLE_FORMAT requires Lua 5.3; select -DLUA_VERSION=5.3.6")
endif ()
set(LUA_SRC_PATH ../lua-${LUA_VERSION}/src)

set ( LUA_IDSIZE 120 CACHE STRING "gives the maximum size for the description of the source." )
# Lua 5.3 and 5.4 ship a luaconf.h.in template; Lua 5.5 ships a plain luaconf.h.
if (EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/${LUA_SRC_PATH}/luaconf.h.in)
    configure_file ( ${LUA_SRC_PATH}/luaconf.h.in ${CMAKE_CURRENT_BINARY_DIR}/luaconf.h )
else ()
    add_definitions ( -DLUA_IDSIZE=${LUA_IDSIZE} )
endif ()

include_directories(
	${CMAKE_SOURCE_DIR}
	${LUA_SRC_PATH}
	${CMAKE_CURRENT_BINARY_DIR}
)

aux_source_directory(${LUA_SRC_PATH} LUA_CORE)
list(REMOVE_ITEM LUA_CORE ${LUA_SRC_PATH}/lua.c ${LUA_SRC_PATH}/luac.c)
set ( LUAC ${LUA_SRC_PATH}/luac.c )
set ( LUA ${LUA_SRC_PATH}/lua.c )

macro(source_group_by_dir proj_dir source_files)
    if(MSVC OR APPLE)
        get_filename_component(sgbd_cur_dir ${proj_dir} ABSOLUTE)
        foreach(sgbd_file ${${source_files}})
			get_filename_component(sgbd_abs_file ${sgbd_file} ABSOLUTE)
            file(RELATIVE_PATH sgbd_fpath ${sgbd_cur_dir} ${sgbd_abs_file})
            string(REGEX REPLACE "\(.*\)/.*" \\1 sgbd_group_name ${sgbd_fpath})
            string(COMPARE EQUAL ${sgbd_fpath} ${sgbd_group_name} sgbd_nogroup)
            string(REPLACE "/" "\\" sgbd_group_name ${sgbd_group_name})
            if(sgbd_nogroup)
                set(sgbd_group_name "\\")
            endif(sgbd_nogroup)
            source_group(${sgbd_group_name} FILES ${sgbd_file})
        endforeach(sgbd_file)
    endif(MSVC OR APPLE)
endmacro(source_group_by_dir)

source_group_by_dir(${CMAKE_CURRENT_SOURCE_DIR} LUA_CORE)
source_group_by_dir(${CMAKE_CURRENT_SOURCE_DIR} LUAC)

add_executable (luac ${LUA_CORE} ${LUAC})
add_executable (lua ${LUA_CORE} ${LUA})

if (LUAC_COMPATIBLE_FORMAT)
target_compile_definitions (luac PRIVATE LUAC_COMPATIBLE_FORMAT)
target_compile_definitions (lua PRIVATE LUAC_COMPATIBLE_FORMAT)
endif ()

