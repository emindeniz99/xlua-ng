# Tencent is pleased to support the open source community by making xLua available.
# Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
# Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
# http://opensource.org/licenses/MIT
# Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.

cmake_minimum_required(VERSION 3.22...4.4)

# macOS plugins run on macOS 12.0 and later (build/README.md, "macOS and iOS
# deployment targets"). Without a target, Xcode and clang build for the macOS
# of the build machine, and Unity cannot load the plugin on anything older.
# 12.0 is the lowest target Xcode 27 builds, and what Unity 6000.3 requires.
# A -DCMAKE_OSX_DEPLOYMENT_TARGET or MACOSX_DEPLOYMENT_TARGET in the
# environment wins; toolchain files (iOS, Android) set their own. FORCE
# replaces the empty value that a build directory configured before this
# default has in its cache. Test/Native/check_apple_minos.py checks the result.
if (CMAKE_HOST_APPLE AND NOT CMAKE_TOOLCHAIN_FILE AND NOT CMAKE_SYSTEM_NAME
        AND "${CMAKE_OSX_DEPLOYMENT_TARGET}" STREQUAL "" AND "$ENV{MACOSX_DEPLOYMENT_TARGET}" STREQUAL "")
    set(CMAKE_OSX_DEPLOYMENT_TARGET 12.0 CACHE STRING "Minimum macOS version of the plugin" FORCE)
endif ()

project(XLua)

# The make_*.sh scripts run `cmake --build ... --config Release`, which only
# multi-config generators (Visual Studio, Xcode) honour. Without this default a
# Makefile or Ninja build has no CMAKE_BUILD_TYPE and compiles without -O.
get_property(XLUA_MULTI_CONFIG GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
if (NOT XLUA_MULTI_CONFIG AND NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type (default Release)" FORCE)
endif ()

# All C sources (xLua's own and the vendored libraries) compile as C17. GNU
# extensions stay on (gnu17), because Lua's POSIX build needs declarations
# such as popen and mkstemp. Assets/Plugins/WebGL/xlua_webgl.cpp includes the
# same files in a C++ translation unit, so xLua's own C must also stay valid C++.
# C17 rather than C23: Unity 2021.3 builds WebGL with Emscripten 2.0.19 (clang
# 13), which cannot compile C23.
set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Desktop Windows links the static C runtime, like LuaJIT's msvcbuild_mt.bat.
if (MSVC AND NOT CMAKE_SYSTEM_NAME STREQUAL "WindowsStore")
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif ()


option ( UINT_ESPECIALLY "using custom ulong" OFF )
option ( USING_LUAJIT "using luajit" OFF )
option ( LUAC_COMPATIBLE_FORMAT "compatible format" OFF )
set ( XLUA_SANITIZE "" CACHE STRING "Comma-separated -fsanitize list for test builds, e.g. address,undefined" )
option ( XLUA_COVERAGE "Clang source-based coverage of xLua's own C files for test builds" OFF )
option ( XLUA_APICHECK "Lua C API argument checks (LUA_USE_APICHECK, with assert kept on) for test builds" OFF )
# Presets (docs/design/presets.md): legacy, the default, and minimal build the
# C# bridge on top of xlua_core.c; core builds xlua_core.c and LuaScript's
# sandbox without the bridge (docs/design/core-luascript.md). A preset sets the defaults of the
# native options below (build/xlua_presets.cmake); the file selection
# (XLUA_SOURCES) follows it too. Like the options, it takes its default from the
# environment variable of the same name (XLUA_PRESET=minimal
# build/make_android_lua55.sh).
if (DEFINED ENV{XLUA_PRESET} AND NOT "$ENV{XLUA_PRESET}" STREQUAL "")
    set (XLUA_PRESET_DEFAULT "$ENV{XLUA_PRESET}")
else ()
    set (XLUA_PRESET_DEFAULT legacy)
endif ()
set ( XLUA_PRESET "${XLUA_PRESET_DEFAULT}" CACHE STRING "legacy (default), minimal or core: the native file set and the defaults of the native options (docs/design/presets.md)" )
set_property ( CACHE XLUA_PRESET PROPERTY STRINGS legacy minimal core )
if (NOT XLUA_PRESET MATCHES "^(legacy|minimal|core)$")
    message(FATAL_ERROR "XLUA_PRESET must be legacy, minimal or core, not ${XLUA_PRESET}")
endif ()
# The options keep the values the cache has, so a build directory stays with the
# preset it was first configured with.
if (DEFINED XLUA_PRESET_CONFIGURED AND NOT XLUA_PRESET STREQUAL XLUA_PRESET_CONFIGURED)
    message(FATAL_ERROR "This build directory was configured with XLUA_PRESET=${XLUA_PRESET_CONFIGURED}; its cached options keep that preset's values. Use a fresh build directory for XLUA_PRESET=${XLUA_PRESET}.")
endif ()
if (DEFINED XLUA_PRESET_CONFIGURED AND DEFINED ENV{XLUA_PRESET} AND NOT "$ENV{XLUA_PRESET}" STREQUAL ""
        AND NOT "$ENV{XLUA_PRESET}" STREQUAL XLUA_PRESET)
    message(FATAL_ERROR "The environment asks for XLUA_PRESET=$ENV{XLUA_PRESET}, but this build directory was configured with XLUA_PRESET=${XLUA_PRESET}. Use a fresh build directory (the make_* scripts: XLUA_VARIANT=<name>).")
endif ()
set ( XLUA_PRESET_CONFIGURED "${XLUA_PRESET}" CACHE INTERNAL "The preset this build directory was configured with" )
message (STATUS "Preset: ${XLUA_PRESET}")
include (${CMAKE_CURRENT_SOURCE_DIR}/xlua_presets.cmake)
set (xlua_preset_XLUA_WERROR OFF)

# XLUA_WERROR and XLUA_HARDEN take their default from the environment variable of
# the same name, so CI and the make_* scripts can switch them without editing
# every cmake call. An explicit -D or an existing cache entry wins.
xlua_option_default (XLUA_WERROR)
xlua_option_default (XLUA_HARDEN)
option ( XLUA_WERROR "Turn warnings in xLua's own C files into errors (CI)" ${XLUA_WERROR_DEFAULT} )
option ( XLUA_HARDEN "Compiler hardening: stack protector, branch protection, auto-var-init, fortify, CFG" ${XLUA_HARDEN_DEFAULT} )
# Size flags measured in build/FLAGS.md: on for Android and OpenHarmony (clang
# and lld), off elsewhere until they are measured there.
if (ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "OHOS")
    set (XLUA_SIZE_FLAGS_DEFAULT ON)
else ()
    set (XLUA_SIZE_FLAGS_DEFAULT OFF)
endif ()
option ( XLUA_SIZE_FLAGS "-O3 for XLUA_HOT_FILES and -Oz for the rest, identical code folding, packed relocations (Android API 23 to 27)" ${XLUA_SIZE_FLAGS_DEFAULT} )
set ( XLUA_HOT_FILES
    # The VM, calls, tables, GC, strings, the C API, closures, numbers.
    lvm.c ldo.c ltable.c lgc.c lstring.c lapi.c lfunc.c lobject.c
    # String patterns and formatting, luaL_Buffer, allocation, metamethods, table.sort.
    lstrlib.c lauxlib.c lmem.c ltm.c ltablib.c
    # The compiler and the bytecode loader.
    llex.c lparser.c lcode.c lundump.c lzio.c
    # The bridge and the core layer under it, which holds the C# function
    # trampolines (compiled at -O3 with xlua.c until the split, build/FLAGS.md).
    xlua.c xlua_core.c
    CACHE STRING "Source file names that XLUA_SIZE_FLAGS compiles at -O3 (build/FLAGS.md)" )
mark_as_advanced ( XLUA_HOT_FILES )
option ( XLUA_ICF "With XLUA_SIZE_FLAGS: identical code folding (-Wl,--icf=all)" ${xlua_preset_XLUA_ICF} )
# XLUA_EXPORTS takes its default from the preset and the environment variable of
# the same name.
if (DEFINED ENV{XLUA_EXPORTS} AND NOT "$ENV{XLUA_EXPORTS}" STREQUAL "")
    set (XLUA_EXPORTS_DEFAULT "$ENV{XLUA_EXPORTS}")
else ()
    set (XLUA_EXPORTS_DEFAULT "${xlua_preset_XLUA_EXPORTS}")
endif ()
set ( XLUA_EXPORTS "${XLUA_EXPORTS_DEFAULT}" CACHE STRING "all: export the whole Lua and xLua API; managed: only what LuaDLL.cs imports (build/xlua_exports.txt; the core preset: build/xlua_core_exports.txt) and XLUA_EXPORTS_EXTRA" )
set_property ( CACHE XLUA_EXPORTS PROPERTY STRINGS all managed )
set ( XLUA_EXPORTS_EXTRA "" CACHE STRING "With XLUA_EXPORTS=managed: more functions to export, for native plugins that call them" )
# C modules that register themselves in package.preload from luaopen_xlua
# (build/xlua.c), so LuaDLL.cs imports nothing for them. Off leaves the sources
# out of the library and nothing is registered; require "socket" then fails
# with "module 'socket' not found". Both are on unless the environment variable
# of the same name is OFF (or another false value), or the preset turns them off
# (minimal), so the make_* scripts take them from the environment; an explicit -D
# or an existing cache entry wins.
# XLUA_LEAK_CHECKER and XLUA_UINT64 (below) take their default the same way.
foreach (xlua_env_option XLUA_LUASOCKET XLUA_MIME XLUA_LEAK_CHECKER XLUA_UINT64)
    xlua_option_default (${xlua_env_option})
endforeach ()
option ( XLUA_LUASOCKET "Build LuaSocket and register socket.core and socket in package.preload" ${XLUA_LUASOCKET_DEFAULT} )
option ( XLUA_MIME "With XLUA_LUASOCKET: also build LuaSocket's mime.c and register mime.core and mime" ${XLUA_MIME_DEFAULT} )
# The leak checker of memory_leak_checker.c (xlua_report_table_size,
# xlua_report_object_relationship, xlua_registry_pointer, xlua_global_pointer),
# which General/LuaMemoryLeakChecker imports and Assets/XLua never calls. It walks
# Lua's internal object list, so it exists on Lua 5.3 to 5.5 only; LuaJIT never
# had it. Independent of XLUA_MEMORY_SNAPSHOT.
option ( XLUA_LEAK_CHECKER "Build the internal-header leak checker (xlua_report_*, General/LuaMemoryLeakChecker; Lua 5.3 to 5.5)" ${XLUA_LEAK_CHECKER_DEFAULT} )
# The uint64 table of i64lib.c (uint64.tostring, compare, divide, remainder, parse;
# on LuaJIT also the bitwise, shift and floor division helpers), which
# luaopen_i64lib sets as a global. OFF leaves the table out; the push and get
# functions the C# side imports (lua_pushint64, lua_touint64 ...) stay, so 64-bit
# integers keep all 64 bits either way. LuaJIT refuses OFF: there the int64
# userdata needs the table for unsigned text, comparison and division, and LuaEnv
# finds the userdata's metatable through uint64.parse (docs/MIGRATING.md).
option ( XLUA_UINT64 "Register the uint64 helper table from luaopen_i64lib (OFF is refused on LuaJIT)" ${XLUA_UINT64_DEFAULT} )
# The Lua standard libraries the library contains and luaL_openlibs (and with it
# luaopen_xlua) opens: "all", or a list of names from package, coroutine, table,
# io, os, string, math, utf8 and debug (LuaJIT: package, table, io, os, string,
# math, debug, bit, jit and ffi). The base library is always opened. A list leaves
# the other libraries' sources and Lua's linit.c out (xlua_libs.c opens the list
# instead), so their luaopen_* functions are not in the library. The legacy
# preset needs package (LuaEnv's searchers) and debug (the error handler's
# debug.traceback). The environment variable of the same name gives the default,
# with commas or semicolons: XLUA_LIBS=package,debug,string,table,math
# build/make_android_lua55.sh.
if (DEFINED ENV{XLUA_LIBS} AND NOT "$ENV{XLUA_LIBS}" STREQUAL "")
    string (REPLACE "," ";" XLUA_LIBS_DEFAULT "$ENV{XLUA_LIBS}")
else ()
    set (XLUA_LIBS_DEFAULT "${xlua_preset_XLUA_LIBS}")
endif ()
set ( XLUA_LIBS "${XLUA_LIBS_DEFAULT}" CACHE STRING "Lua standard libraries to build and open: all, or a list such as package;debug;string;table;math (base is always opened)" )
# The memory snapshot walk (xlua_memory_snapshot in memory_leak_checker.c) behind
# LuaEnv.TakeMemorySnapshot(), which only the Editor and development builds
# compile. On for the desktop plugins the Editor loads (macOS, Windows, Linux)
# and for static libraries (iOS, tvOS, visionOS), whose players drop the walk
# when nothing calls it; off for the shared libraries that ship in players
# (Android, OpenHarmony, UWP) and for Switch, where it would cost about 7.5 KB
# in every release build (build/SIZE.md). The minimal preset turns it off
# everywhere. The environment variable of the same name overrides the default
# (XLUA_MEMORY_SNAPSHOT=ON build/make_android_lua55.sh for a development Android
# build); an explicit -D or an existing cache entry wins.
xlua_option_default (XLUA_MEMORY_SNAPSHOT)
option ( XLUA_MEMORY_SNAPSHOT "Build the memory snapshot walk for LuaEnv.TakeMemorySnapshot() (Editor and development builds)" ${XLUA_MEMORY_SNAPSHOT_DEFAULT} )

find_path(XLUA_PROJECT_DIR NAMES SConstruct
    PATHS 
    ${CMAKE_SOURCE_DIR}
    NO_DEFAULT_PATH
    )

MARK_AS_ADVANCED(XLUA_PROJECT_DIR)

if (NOT LUA_VERSION)
    set(LUA_VERSION "5.5.1")
endif()

if (XLUA_PRESET STREQUAL "core" AND (USING_LUAJIT OR NOT LUA_VERSION MATCHES "^5[.][45][.]"))
    message(FATAL_ERROR "XLUA_PRESET=core needs Lua 5.4 or 5.5 (-DLUA_VERSION=5.5.1 or 5.4.9); LuaJIT and Lua 5.3 have no core preset")
endif ()
# LuaScript's native sandbox (build/xlua_core_sandbox.c: xlua_core_open and the
# other sandbox exports, core-luascript.md section 4). The core preset is that
# layer and always builds it; the bridge presets add it with this option, so that
# LuaScript and LuaEnv can share one plugin. Off in legacy and minimal for now
# (provisional), which keeps their libraries as they were. Lua 5.4 and 5.5 only.
xlua_option_default (XLUA_LUASCRIPT)
option ( XLUA_LUASCRIPT "Build LuaScript's native sandbox (xlua_core_open ...) into the bridge presets; always on in the core preset (Lua 5.4 and 5.5)" ${XLUA_LUASCRIPT_DEFAULT} )
if (XLUA_PRESET STREQUAL "core")
    set (XLUA_LUASCRIPT ON)
    # The core preset has no bridge, so the options of the bridge's parts must stay off.
    foreach (xlua_bridge_option XLUA_LUASOCKET XLUA_MIME XLUA_LEAK_CHECKER XLUA_UINT64 XLUA_MEMORY_SNAPSHOT)
        if (${xlua_bridge_option})
            message(FATAL_ERROR "XLUA_PRESET=core has no C# bridge: ${xlua_bridge_option} must be OFF")
        endif ()
    endforeach ()
endif ()
if (XLUA_LUASCRIPT AND (USING_LUAJIT OR NOT LUA_VERSION MATCHES "^5[.][45][.]"))
    message(FATAL_ERROR "XLUA_LUASCRIPT needs Lua 5.4 or 5.5 (-DLUA_VERSION=5.5.1 or 5.4.9)")
endif ()

if (LUAC_COMPATIBLE_FORMAT AND NOT USING_LUAJIT AND NOT LUA_VERSION MATCHES "^5[.]3[.]")
    message(FATAL_ERROR "LUAC_COMPATIBLE_FORMAT requires Lua 5.3; select -DLUA_VERSION=5.3.6")
endif()

set(LUA_SRC_PATH lua-${LUA_VERSION}/src)
set(LUAJIT_SRC_PATH luajit-2.1/src)
# Static LuaJIT library to link. Cross builds (make_android_luajit.sh) point this at a per-target copy.
set(LUAJIT_STATIC_LIB "${CMAKE_SOURCE_DIR}/${LUAJIT_SRC_PATH}/libluajit.a" CACHE FILEPATH "LuaJIT static library to link when USING_LUAJIT is ON")

if (USING_LUAJIT)
	# Vendored headers are SYSTEM headers, so the warnings set on xLua's own
	# files (below) reports only xLua's code. The search order is unchanged.
	include_directories(${CMAKE_SOURCE_DIR})
	include_directories(SYSTEM
	  ${LUAJIT_SRC_PATH}
	  ${CMAKE_CURRENT_BINARY_DIR}
	  ${THIRDPART_INC}
	)

	set ( LUA_CORE )
	set_property( SOURCE xlua.c xlua_core.c xlua_core_limits.c APPEND PROPERTY COMPILE_DEFINITIONS USING_LUAJIT )
	set ( LUA_LIB )
else ()
	set ( LUA_IDSIZE 120 CACHE STRING "gives the maximum size for the description of the source." )

	# Lua 5.3 and 5.4 ship a luaconf.h.in template. Lua 5.5 ships a plain luaconf.h
	# (default LUA_IDSIZE 120) so Unity's WebGL build can compile the same sources
	# without CMake. LUA_IDSIZE is then passed as a compile definition instead.
	if (EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/${LUA_SRC_PATH}/luaconf.h.in)
		configure_file ( ${LUA_SRC_PATH}/luaconf.h.in ${CMAKE_CURRENT_BINARY_DIR}/luaconf.h )
	else ()
		add_definitions ( -DLUA_IDSIZE=${LUA_IDSIZE} )
	endif ()

	# SYSTEM for the same reason as LuaJIT's headers above.
	include_directories(${CMAKE_SOURCE_DIR})
	include_directories(SYSTEM
		${LUA_SRC_PATH}
		${CMAKE_CURRENT_BINARY_DIR}
		${THIRDPART_INC}
	)

    aux_source_directory(${LUA_SRC_PATH} LUA_CORE)
    list(REMOVE_ITEM LUA_CORE ${LUA_SRC_PATH}/lua.c ${LUA_SRC_PATH}/luac.c)
	set ( LUA_LIB )

	# set ( LUA_CORE ${LUA_SRC_PATH}/lapi.c ${LUA_SRC_PATH}/lcode.c ${LUA_SRC_PATH}/lctype.c ${LUA_SRC_PATH}/ldebug.c ${LUA_SRC_PATH}/ldo.c ${LUA_SRC_PATH}/ldump.c
	#   ${LUA_SRC_PATH}/lfunc.c ${LUA_SRC_PATH}/lgc.c ${LUA_SRC_PATH}/llex.c ${LUA_SRC_PATH}/lmem.c ${LUA_SRC_PATH}/lobject.c ${LUA_SRC_PATH}/lopcodes.c ${LUA_SRC_PATH}/lparser.c
	#   ${LUA_SRC_PATH}/lstate.c ${LUA_SRC_PATH}/lstring.c ${LUA_SRC_PATH}/ltable.c ${LUA_SRC_PATH}/ltm.c ${LUA_SRC_PATH}/lundump.c ${LUA_SRC_PATH}/lvm.c ${LUA_SRC_PATH}/lzio.c )
	# set ( LUA_LIB ${LUA_SRC_PATH}/lauxlib.c ${LUA_SRC_PATH}/lbaselib.c ${LUA_SRC_PATH}/lbitlib.c ${LUA_SRC_PATH}/lcorolib.c ${LUA_SRC_PATH}/ldblib.c
	#   ${LUA_SRC_PATH}/liolib.c ${LUA_SRC_PATH}/lmathlib.c ${LUA_SRC_PATH}/loslib.c ${LUA_SRC_PATH}/lstrlib.c ${LUA_SRC_PATH}/ltablib.c ${LUA_SRC_PATH}/linit.c
	#   ${LUA_SRC_PATH}/lutf8lib.c ${LUA_SRC_PATH}/loadlib.c )
endif ()
# The test interpreter (XLUA_LUA_INTERPRETER) keeps every library.
set ( LUA_CORE_ALL ${LUA_CORE} )

# XLUA_LIBS: the standard libraries, their source files (PUC Lua) and the
# definition xlua_libs.c opens each one under.
if (USING_LUAJIT)
    set (xlua_known_libs package table io os string math debug bit jit ffi)
    set (xlua_other_libs coroutine utf8) # coroutine is part of LuaJIT's base library; utf8 does not exist
else ()
    set (xlua_known_libs package coroutine table io os string math utf8 debug)
    set (xlua_other_libs bit jit ffi)
endif ()
set (xlua_lib_file_package loadlib.c)
set (xlua_lib_file_coroutine lcorolib.c)
set (xlua_lib_file_table ltablib.c)
set (xlua_lib_file_io liolib.c)
set (xlua_lib_file_os loslib.c)
set (xlua_lib_file_string lstrlib.c)
set (xlua_lib_file_math lmathlib.c)
set (xlua_lib_file_utf8 lutf8lib.c)
set (xlua_lib_file_debug ldblib.c)
if (XLUA_LIBS MATCHES "^[ ]*[Aa][Ll][Ll][ ]*$")
    set (XLUA_LIBS_SELECTED ${xlua_known_libs})
    set (XLUA_LIBS_REDUCED OFF)
else ()
    set (XLUA_LIBS_SELECTED)
    foreach (xlua_lib ${XLUA_LIBS})
        string (STRIP "${xlua_lib}" xlua_lib)
        string (TOLOWER "${xlua_lib}" xlua_lib)
        if (xlua_lib STREQUAL "" OR xlua_lib STREQUAL "base" OR xlua_lib STREQUAL "_g")
            continue () # always opened
        elseif (xlua_lib IN_LIST xlua_known_libs)
            list (APPEND XLUA_LIBS_SELECTED ${xlua_lib})
        elseif (xlua_lib IN_LIST xlua_other_libs)
            message (STATUS "XLUA_LIBS: ${xlua_lib} does not apply to this runtime, ignored")
        else ()
            message (FATAL_ERROR "XLUA_LIBS: unknown library '${xlua_lib}'; use all or names from ${xlua_known_libs}")
        endif ()
    endforeach ()
    list (REMOVE_DUPLICATES XLUA_LIBS_SELECTED)
    # A list that names every library builds what "all" builds.
    set (XLUA_LIBS_REDUCED OFF)
    foreach (xlua_lib ${xlua_known_libs})
        if (NOT xlua_lib IN_LIST XLUA_LIBS_SELECTED)
            set (XLUA_LIBS_REDUCED ON)
        endif ()
    endforeach ()
endif ()
if (NOT XLUA_PRESET STREQUAL "core")
    foreach (xlua_lib package debug)
        if (NOT xlua_lib IN_LIST XLUA_LIBS_SELECTED)
            message (FATAL_ERROR "XLUA_LIBS: the ${XLUA_PRESET} preset needs ${xlua_lib} (LuaEnv's searchers use package, its error handler debug.traceback)")
        endif ()
    endforeach ()
else ()
    # LuaScript never opens these (core-luascript.md, section 3.8), so the core
    # library does not contain them.
    foreach (xlua_lib package io os debug)
        if (xlua_lib IN_LIST XLUA_LIBS_SELECTED)
            message (FATAL_ERROR "XLUA_LIBS: the core preset cannot contain ${xlua_lib}; use names from coroutine, table, string, math and utf8")
        endif ()
    endforeach ()
    # Core loads text only: the bytecode loader and dumper are replaced by
    # xlua_core_nobc.c (core-luascript.md, section 3.9).
    list (REMOVE_ITEM LUA_CORE ${LUA_SRC_PATH}/lundump.c ${LUA_SRC_PATH}/ldump.c)
endif ()
if (XLUA_LIBS_REDUCED)
    # xlua_libs.c replaces linit.c, which references every library.
    set (XLUA_LIBS_DEFINITIONS)
    foreach (xlua_lib ${XLUA_LIBS_SELECTED})
        string (TOUPPER "${xlua_lib}" xlua_lib_upper)
        list (APPEND XLUA_LIBS_DEFINITIONS XLUA_LIB_${xlua_lib_upper})
    endforeach ()
    if (NOT USING_LUAJIT)
        list (REMOVE_ITEM LUA_CORE ${LUA_SRC_PATH}/linit.c)
        foreach (xlua_lib ${xlua_known_libs})
            if (NOT xlua_lib IN_LIST XLUA_LIBS_SELECTED)
                list (REMOVE_ITEM LUA_CORE ${LUA_SRC_PATH}/${xlua_lib_file_${xlua_lib}})
            endif ()
        endforeach ()
    endif ()
endif ()
list (JOIN XLUA_LIBS_SELECTED ", " xlua_libs_text)
message (STATUS "Lua standard libraries: base, ${xlua_libs_text}")

set ( LUA_SOCKET
    luasocket/auxiliar.c
    luasocket/buffer.c
    luasocket/compat.c
    luasocket/except.c
    luasocket/inet.c
    luasocket/io.c
    luasocket/luasocket.c
    luasocket/mime.c
    luasocket/options.c
    luasocket/select.c
    luasocket/tcp.c
    luasocket/timeout.c
    luasocket/udp.c 
)

if ( WIN32 AND NOT CYGWIN )
    list (APPEND LUA_SOCKET luasocket/wsocket.c)
else ( )
    list (APPEND LUA_SOCKET luasocket/usocket.c)
endif ( )

# The Switch build never compiled LuaSocket.
if (NOT XLUA_LUASOCKET OR "${CMAKE_SYSTEM_NAME}" STREQUAL "Switch")
    set ( LUA_SOCKET )
    set ( XLUA_LUASOCKET OFF )
    set ( XLUA_MIME OFF )
else ()
    set_property( SOURCE xlua.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_LUASOCKET )
    if (XLUA_MIME)
        set_property( SOURCE xlua.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_MIME )
    else ()
        list (REMOVE_ITEM LUA_SOCKET luasocket/mime.c)
    endif ()
endif ()
message (STATUS "LuaSocket: ${XLUA_LUASOCKET}, mime: ${XLUA_MIME}")
if (NOT XLUA_MEMORY_SNAPSHOT)
    set_property( SOURCE memory_leak_checker.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_NO_MEMORY_SNAPSHOT )
endif ()
message (STATUS "Memory snapshot: ${XLUA_MEMORY_SNAPSHOT}")
if (USING_LUAJIT)
    set (XLUA_LEAK_CHECKER OFF) # needs PUC Lua's internal headers
endif ()
if (NOT XLUA_LEAK_CHECKER)
    set_property( SOURCE memory_leak_checker.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_NO_LEAK_CHECKER )
endif ()
message (STATUS "Leak checker: ${XLUA_LEAK_CHECKER}")
if (NOT XLUA_UINT64)
    if (USING_LUAJIT)
        message (FATAL_ERROR "XLUA_UINT64=OFF is not available on LuaJIT: its int64 userdata needs the uint64 table (build/README.md)")
    endif ()
    set_property( SOURCE i64lib.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_NO_UINT64 )
endif ()
message (STATUS "uint64 table: ${XLUA_UINT64}")

# xLua's own C files. XLUA_CORE_SOURCES is the native layer every preset
# builds; XLUA_BRIDGE_SOURCES is the C# bridge on top of it, which the legacy
# and minimal presets add (docs/design/core-luascript.md, section 3.1).
# XLUA_SOURCES is the set this preset compiles; the warning, coverage and size
# flags below apply to it.
set ( XLUA_CORE_SOURCES
    xlua_core.c
    xlua_core_limits.c
)
# LuaScript's sandbox, which opens the libraries the build contains (XLUA_LIB_<NAME>).
if (XLUA_LUASCRIPT)
    list (APPEND XLUA_CORE_SOURCES xlua_core_sandbox.c)
    set (xlua_sandbox_definitions)
    foreach (xlua_lib ${XLUA_LIBS_SELECTED})
        string (TOUPPER "${xlua_lib}" xlua_lib_upper)
        list (APPEND xlua_sandbox_definitions XLUA_LIB_${xlua_lib_upper})
    endforeach ()
    set_property( SOURCE xlua_core_sandbox.c APPEND PROPERTY COMPILE_DEFINITIONS ${xlua_sandbox_definitions} XLUA_SANDBOX )
    set_property( SOURCE xlua_core.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_SANDBOX )
endif ()
if (XLUA_PRESET STREQUAL "core")
    # The bytecode stubs, and xlua_core.c without the bridge's trampolines and handler.
    list (APPEND XLUA_CORE_SOURCES xlua_core_nobc.c)
    set_property( SOURCE xlua_core.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_CORE_ONLY )
endif ()
# With a reduced XLUA_LIBS, xlua_libs.c defines luaL_openlibs (Lua 5.5:
# luaL_openselectedlibs) for the libraries the build contains. The core preset
# opens its libraries in xlua_core_sandbox.c and has no luaL_openlibs.
if (XLUA_LIBS_REDUCED)
    if (NOT XLUA_PRESET STREQUAL "core")
        list (APPEND XLUA_CORE_SOURCES xlua_libs.c)
        set_property( SOURCE xlua_libs.c APPEND PROPERTY COMPILE_DEFINITIONS ${XLUA_LIBS_DEFINITIONS} )
    endif ()
    # Without the coroutine library there is no coroutine.resume to replace
    # (xlua_core_limits.c, the switch accounting of the limits).
    if (NOT "coroutine" IN_LIST XLUA_LIBS_SELECTED)
        set_property( SOURCE xlua_core_limits.c APPEND PROPERTY COMPILE_DEFINITIONS XLUA_NO_COROUTINE_LIB )
    endif ()
    if (USING_LUAJIT)
        set_property( SOURCE xlua_libs.c APPEND PROPERTY COMPILE_DEFINITIONS USING_LUAJIT )
    endif ()
endif ()

# memory_leak_checker.c holds the internal-header leak checker (Lua 5.3 to 5.5
# only, by #if) and the memory snapshot walk (every runtime, LuaJIT too, unless
# XLUA_MEMORY_SNAPSHOT is off).
set ( XLUA_BRIDGE_SOURCES
    i64lib.c
    xlua.c
    memory_leak_checker.c
)

set ( XLUA_SOURCES ${XLUA_CORE_SOURCES} )
if (NOT XLUA_PRESET STREQUAL "core")
    list (APPEND XLUA_SOURCES ${XLUA_BRIDGE_SOURCES})
endif ()

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

source_group_by_dir(${CMAKE_CURRENT_SOURCE_DIR} LUA_SOCKET)
source_group_by_dir(${CMAKE_CURRENT_SOURCE_DIR} LUA_CORE)
source_group_by_dir(${CMAKE_CURRENT_SOURCE_DIR} LUA_LIB)
source_group_by_dir(${CMAKE_CURRENT_SOURCE_DIR} XLUA_SOURCES)

# Linux, Android and OpenHarmony (musl) get Lua's POSIX build like macOS
# (LUA_USE_MACOSX below): mkstemp for os.tmpname, io.popen, and dlopen for
# package.loadlib. Without a define Lua falls back to ISO C there (tmpnam, no
# popen, no C modules).
if (ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "Linux" OR CMAKE_SYSTEM_NAME STREQUAL "OHOS")
    ADD_DEFINITIONS(-DLUA_USE_LINUX)
    # Lua asks for 64-bit off_t, but 32-bit bionic only has fseeko64/ftello64
    # from API 24; below that keep the 32-bit fseeko/ftello.
    if (ANDROID AND CMAKE_SIZEOF_VOID_P EQUAL 4 AND ANDROID_PLATFORM_LEVEL LESS 24)
        ADD_DEFINITIONS(-D_FILE_OFFSET_BITS=32)
    endif ()
endif ()

if (APPLE)
    if (IOS)
        ADD_DEFINITIONS(-DLUA_USE_IOS) # Despite claiming to be ISO C, iOS does not implement 'system'.(iOS11)
        set(CMAKE_OSX_ARCHITECTURES "$(ARCHS_STANDARD)")
        add_library(xlua STATIC
           ${LUA_CORE}
           ${LUA_LIB}
           ${LUA_SOCKET}
           ${XLUA_SOURCES}
           ${THIRDPART_SRC}
        )
		# Use the deployment target selected by the toolchain.
    else ()
        ADD_DEFINITIONS(-DLUA_USE_MACOSX) #osx platform emmylua debugger must have this option or can not load cpath
        if (BUILD_SILICON)
           set(CMAKE_OSX_ARCHITECTURES arm64)

            add_library(xlua SHARED
                ${LUA_CORE}
                ${LUA_LIB}
                ${LUA_SOCKET}
                ${XLUA_SOURCES}
                ${THIRDPART_SRC}
            )
        else ()
            set(CMAKE_OSX_ARCHITECTURES "$(ARCHS_STANDARD_64_BIT)")
            add_library(xlua MODULE
                ${LUA_CORE}
                ${LUA_LIB}
                ${LUA_SOCKET}
                ${XLUA_SOURCES}
                ${THIRDPART_SRC}
            )
            set_target_properties ( xlua PROPERTIES BUNDLE TRUE )
            #set_target_properties ( xlua PROPERTIES FRAMEWORK TRUE )
            #set_target_properties ( xlua PROPERTIES MACOSX_RPATH TRUE )
        endif ()
    endif ()
elseif ("${CMAKE_SYSTEM_NAME}" STREQUAL "Switch")
    add_library(xlua STATIC
        ${LUA_CORE}
        ${LUA_LIB}
        ${XLUA_SOURCES}
        ${THIRDPART_SRC}
    )
    target_compile_options(xlua PRIVATE -m64 -mcpu=cortex-a57+fp+simd+crypto+crc -fno-common -fno-short-enums -ffunction-sections -fdata-sections -fPIC -fms-extensions)
else ( )
    add_library(xlua SHARED
        ${LUA_CORE}
        ${LUA_LIB}
        ${LUA_SOCKET}
        ${XLUA_SOURCES}
        ${THIRDPART_SRC}
    )
endif ( )

if ( WIN32 AND NOT CYGWIN )
    target_compile_definitions (xlua PRIVATE LUA_BUILD_AS_DLL)
endif ( )

if (LUAC_COMPATIBLE_FORMAT AND NOT USING_LUAJIT)
    target_compile_definitions (xlua PRIVATE LUAC_COMPATIBLE_FORMAT)
endif ()

# The hooks of build/xlua_luahooks.h, which Lua 5.4's luaconf.h.in includes
# (LuaOptions.HashSeed). libxlua only: the test interpreter keeps upstream Lua.
if (NOT USING_LUAJIT)
    target_compile_definitions (xlua PRIVATE XLUA_LUA_HOOKS)
endif ()

if (XLUA_SANITIZE)
    target_compile_options (xlua PRIVATE -fsanitize=${XLUA_SANITIZE} -fno-omit-frame-pointer -fno-sanitize-recover=all -g)
    target_link_options (xlua PRIVATE -fsanitize=${XLUA_SANITIZE})
endif ()

if (XLUA_COVERAGE)
    if (NOT CMAKE_C_COMPILER_ID MATCHES "Clang")
        message(FATAL_ERROR "XLUA_COVERAGE requires Clang (source-based coverage)")
    endif ()
    # Only xLua's own files are instrumented; the vendored runtimes are not measured.
    set_property(SOURCE ${XLUA_SOURCES} APPEND PROPERTY COMPILE_OPTIONS -fprofile-instr-generate -fcoverage-mapping)
    target_link_options (xlua PRIVATE -fprofile-instr-generate)
endif ()

if (XLUA_APICHECK)
    # Lua turns luai_apicheck into assert(), so NDEBUG must not be defined
    # (RelWithDebInfo and Release define it). LuaJIT's checks are compiled into
    # libluajit.a: build it with XCFLAGS=-DLUA_USE_APICHECK as well.
    target_compile_definitions (xlua PRIVATE LUA_USE_APICHECK)
    if (MSVC)
        target_compile_options (xlua PRIVATE /UNDEBUG)
    else ()
        target_compile_options (xlua PRIVATE -UNDEBUG)
    endif ()
endif ()

include(CheckCCompilerFlag)
include(CheckCSourceCompiles)

# Adds each flag the C compiler accepts to the list named by out_var. Flags that
# a compiler does not know are skipped, so older or other toolchains still build.
function (xlua_supported_flags out_var)
    set (supported ${${out_var}})
    foreach (flag ${ARGN})
        string (MAKE_C_IDENTIFIER "XLUA_HAS${flag}" var)
        check_c_compiler_flag ("${flag}" ${var})
        if (${var})
            list (APPEND supported "${flag}")
        endif ()
    endforeach ()
    set (${out_var} ${supported} PARENT_SCOPE)
endfunction ()

# Warnings for xLua's own C files only; the vendored Lua, LuaJIT and LuaSocket
# sources are left as upstream wrote them. APPEND keeps the coverage options.
# See docs/research/c17-setup.md for the measured hits behind this set.
if (MSVC)
    # C4013 implicit declaration, C4024/C4047/C4133 incompatible pointer and
    # integer conversions, C4716 missing return value: always errors, like the
    # -Werror= set below.
    set (XLUA_WARNING_FLAGS /W4 /utf-8 /we4013 /we4024 /we4047 /we4133 /we4716)
    if (XLUA_WERROR)
        list (APPEND XLUA_WARNING_FLAGS /WX)
    endif ()
else ()
    # Known to GCC and Clang for many years.
    set (XLUA_WARNING_FLAGS -Wall -Wextra -Wpedantic -Wformat=2 -Wshadow -Wundef
        -Wstrict-prototypes -Wold-style-definition -Wcast-qual -Wwrite-strings
        -Wcast-align -Wconversion -Wno-sign-conversion
        -Werror=return-type -Werror=implicit-function-declaration)
    # The 55 sign conversions (-Wsign-conversion) are left for a manual
    # triage; casting them all would hide the real ones.
    xlua_supported_flags (XLUA_WARNING_FLAGS -Wvla -Wimplicit-fallthrough
        -Wdouble-promotion -Wnull-dereference -Wc++-compat -Wdate-time
        -Werror=int-conversion -Werror=incompatible-pointer-types)
    if (XLUA_WERROR)
        list (APPEND XLUA_WARNING_FLAGS -Werror)
    endif ()
endif ()
set_property(SOURCE ${XLUA_SOURCES} APPEND PROPERTY COMPILE_OPTIONS ${XLUA_WARNING_FLAGS})
if (NOT MSVC AND NOT USING_LUAJIT)
    # Lua's gco2* macros cast GCObject * to union GCUnion *, which needs 8-byte
    # alignment on armeabi-v7a; Lua's allocator provides it. Only the leak
    # checker walks Lua's internal objects.
    set_property(SOURCE memory_leak_checker.c APPEND PROPERTY COMPILE_OPTIONS -Wno-cast-align)
endif ()
# The limits' spinlock (xlua_core_limits.c) is one atomic exchange. On AArch64
# clang calls out-of-line helpers for it, which link compiler-rt's CPU feature
# detection (about 1 KB); the inline exclusive-load loop works on every ARMv8.
set (XLUA_ATOMIC_FLAGS)
if (CMAKE_SYSTEM_PROCESSOR MATCHES "^(aarch64|arm64|ARM64)$" AND NOT APPLE AND NOT MSVC)
    xlua_supported_flags (XLUA_ATOMIC_FLAGS -mno-outline-atomics)
endif ()
if (XLUA_ATOMIC_FLAGS)
    set_property(SOURCE xlua_core_limits.c APPEND PROPERTY COMPILE_OPTIONS ${XLUA_ATOMIC_FLAGS})
endif ()

# Hardening for the whole library (vendored sources too). Off by default until
# its cost is decided; build/FLAGS.md has the measurements. LuaJIT's own
# Makefile builds libluajit.a with -U_FORTIFY_SOURCE -fno-stack-protector;
# with XLUA_HARDEN set in the environment, the make_* scripts pass the same
# flags to it (build/luajit_harden.sh).
if (XLUA_HARDEN)
    set (XLUA_HARDEN_FLAGS)
    if (MSVC)
        list (APPEND XLUA_HARDEN_FLAGS /guard:cf)
        target_link_options (xlua PRIVATE /guard:cf)
    else ()
        xlua_supported_flags (XLUA_HARDEN_FLAGS -fstack-protector-strong -fno-delete-null-pointer-checks)
        # Zero-initialised locals hide the uninitialised reads MSan and valgrind look for.
        if (NOT XLUA_SANITIZE MATCHES "memory")
            xlua_supported_flags (XLUA_HARDEN_FLAGS -ftrivial-auto-var-init=zero)
        endif ()
        # PAC and BTI on arm64. Their instructions are NOPs on CPUs without them.
        if (CMAKE_SYSTEM_PROCESSOR MATCHES "^(aarch64|arm64|ARM64)$"
                AND (ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "Linux" OR CMAKE_SYSTEM_NAME STREQUAL "OHOS"))
            xlua_supported_flags (XLUA_HARDEN_FLAGS -mbranch-protection=standard)
        endif ()
        # CET and stack clash protection on desktop Linux x86_64.
        if (CMAKE_SYSTEM_PROCESSOR MATCHES "^(x86_64|AMD64|amd64)$" AND CMAKE_SYSTEM_NAME STREQUAL "Linux")
            xlua_supported_flags (XLUA_HARDEN_FLAGS -fcf-protection -fstack-clash-protection)
        endif ()
        # _FORTIFY_SOURCE=2 (bionic and Apple support no higher level), only in
        # optimised builds and not under sanitizers, whose interceptors replace
        # the same functions. A level the toolchain already sets is kept: the
        # NDK passes 2 and Ubuntu's GCC 3.
        if (NOT XLUA_SANITIZE)
            set (CMAKE_REQUIRED_FLAGS -O2)
            check_c_source_compiles ("#ifndef _FORTIFY_SOURCE\n#error not set\n#endif\nint main(void) { return 0; }" XLUA_FORTIFY_PREDEFINED)
            unset (CMAKE_REQUIRED_FLAGS)
            if (NOT XLUA_FORTIFY_PREDEFINED)
                list (APPEND XLUA_HARDEN_FLAGS $<$<NOT:$<CONFIG:Debug>>:-D_FORTIFY_SOURCE=2>)
            endif ()
        endif ()
    endif ()
    target_compile_options (xlua PRIVATE ${XLUA_HARDEN_FLAGS})
    message (STATUS "XLUA_HARDEN flags: ${XLUA_HARDEN_FLAGS}")
endif ()

if (ANDROID AND ANDROID_PLATFORM_LEVEL LESS 23)
    message (WARNING "xlua-ng supports Android API 23 and later; this build is for API ${ANDROID_PLATFORM_LEVEL} (set ANDROID_PLATFORM=android-23)")
endif ()

# Size flags, measured in build/FLAGS.md. Debug builds keep their -O0.
if (XLUA_SIZE_FLAGS AND MSVC)
    message (STATUS "XLUA_SIZE_FLAGS: not available for MSVC, ignored")
elseif (XLUA_SIZE_FLAGS)
    include (CheckLinkerFlag)
    set (XLUA_HOT_SOURCES)
    set (XLUA_COLD_SOURCES)
    foreach (xlua_src ${LUA_CORE} ${LUA_LIB} ${LUA_SOCKET} ${XLUA_SOURCES})
        get_filename_component (xlua_name ${xlua_src} NAME)
        if (xlua_name IN_LIST XLUA_HOT_FILES)
            list (APPEND XLUA_HOT_SOURCES ${xlua_src})
        else ()
            list (APPEND XLUA_COLD_SOURCES ${xlua_src})
        endif ()
    endforeach ()
    xlua_supported_flags (XLUA_COLD_FLAGS -Oz)
    if (NOT XLUA_COLD_FLAGS)
        set (XLUA_COLD_FLAGS -Os) # GCC before 12
    endif ()
    # Clang's -Oz runs the machine outliner on AArch64. In an ELF library every
    # outlined sequence gets its own unwind entry, which costs more than it
    # saves; Apple's compact unwind makes the outliner pay, so it stays there.
    if (CMAKE_C_COMPILER_ID MATCHES "Clang" AND NOT APPLE)
        list (APPEND XLUA_COLD_FLAGS -mllvm -enable-machine-outliner=never)
    endif ()
    set_property (SOURCE ${XLUA_HOT_SOURCES} APPEND PROPERTY COMPILE_OPTIONS "$<$<NOT:$<CONFIG:Debug>>:-O3>")
    set_property (SOURCE ${XLUA_COLD_SOURCES} APPEND PROPERTY COMPILE_OPTIONS "$<$<NOT:$<CONFIG:Debug>>:${XLUA_COLD_FLAGS}>")
    # Identical code folding (lld, gold). Folded functions share an address,
    # so two such C functions compare equal in Lua.
    check_linker_flag (C "-Wl,--icf=all" XLUA_HAS_ICF)
    if (XLUA_ICF AND XLUA_HAS_ICF)
        target_link_options (xlua PRIVATE -Wl,--icf=all)
    endif ()
    # Android's packed relocations need API 23. From API 28 the NDK already
    # packs relative relocations as RELR, which is smaller; keep that.
    if (ANDROID AND NOT ANDROID_PLATFORM_LEVEL LESS 23 AND ANDROID_PLATFORM_LEVEL LESS 28)
        target_link_options (xlua PRIVATE -Wl,--pack-dyn-relocs=android)
    endif ()
    message (STATUS "XLUA_SIZE_FLAGS: -O3 for ${XLUA_HOT_SOURCES}; ${XLUA_COLD_FLAGS} for the rest")
endif ()

# Exported functions. "all" exports every non-static function, as upstream.
# "managed" compiles with hidden visibility and links with a version script
# from build/xlua_exports.txt (Test/Native/managed_imports.py keeps it in sync
# with the C# imports, Assets/XLua/Core and Src/LuaDLLBridge.cs); LUA_API in luaconf.h, LuaJIT's LUA_API and LUASOCKET_API
# keep default visibility, so the version script decides what is exported.
# Native plugins that call other Lua or xLua functions need "all" or their
# functions in XLUA_EXPORTS_EXTRA.
if (XLUA_EXPORTS STREQUAL "managed")
    get_target_property (xlua_type xlua TYPE)
    if (APPLE OR WIN32 OR NOT xlua_type STREQUAL "SHARED_LIBRARY")
        message (FATAL_ERROR "XLUA_EXPORTS=managed needs an ELF shared library (Android, OpenHarmony, Linux)")
    endif ()
    # The core preset exports what LuaScript imports (build/xlua_core_exports.txt);
    # a bridge preset with XLUA_LUASCRIPT exports both lists.
    if (XLUA_PRESET STREQUAL "core")
        set (xlua_export_lists ${CMAKE_CURRENT_SOURCE_DIR}/xlua_core_exports.txt)
    elseif (XLUA_LUASCRIPT)
        set (xlua_export_lists ${CMAKE_CURRENT_SOURCE_DIR}/xlua_exports.txt ${CMAKE_CURRENT_SOURCE_DIR}/xlua_core_exports.txt)
    else ()
        set (xlua_export_lists ${CMAKE_CURRENT_SOURCE_DIR}/xlua_exports.txt)
    endif ()
    # A line is "name" or "name OPTION": the function exists only when that
    # CMake option is on, so the list follows the options of this build.
    set (xlua_export_lines)
    foreach (xlua_export_list ${xlua_export_lists})
        file (STRINGS ${xlua_export_list} xlua_export_list_lines REGEX "^[A-Za-z_]")
        list (APPEND xlua_export_lines ${xlua_export_list_lines})
    endforeach ()
    set (xlua_export_names)
    foreach (xlua_export_line ${xlua_export_lines})
        string (REGEX REPLACE "[ \t]+" ";" xlua_export_fields "${xlua_export_line}")
        list (GET xlua_export_fields 0 xlua_export_name)
        list (LENGTH xlua_export_fields xlua_export_field_count)
        if (xlua_export_field_count GREATER 1)
            list (GET xlua_export_fields 1 xlua_export_option)
            if (NOT DEFINED ${xlua_export_option})
                message (FATAL_ERROR "build/xlua_exports.txt or xlua_core_exports.txt: ${xlua_export_name} names an unknown option ${xlua_export_option}")
            endif ()
            if (NOT ${xlua_export_option})
                continue ()
            endif ()
        endif ()
        list (APPEND xlua_export_names ${xlua_export_name})
    endforeach ()
    list (APPEND xlua_export_names ${XLUA_EXPORTS_EXTRA})
    list (REMOVE_DUPLICATES xlua_export_names)
    list (JOIN xlua_export_names ";\n    " xlua_export_body)
    set (xlua_version_script ${CMAKE_CURRENT_BINARY_DIR}/xlua_exports.map)
    file (CONFIGURE OUTPUT ${xlua_version_script} CONTENT "{\n  global:\n    ${xlua_export_body};\n  local:\n    *;\n};\n" @ONLY)
    set_property (DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS ${xlua_export_lists})
    set_target_properties (xlua PROPERTIES C_VISIBILITY_PRESET hidden)
    target_link_options (xlua PRIVATE "-Wl,--version-script=${xlua_version_script}")
    set_property (TARGET xlua APPEND PROPERTY LINK_DEPENDS ${xlua_version_script})
elseif (NOT XLUA_EXPORTS STREQUAL "all")
    message (FATAL_ERROR "XLUA_EXPORTS must be all or managed, not ${XLUA_EXPORTS}")
endif ()

if (LUA_SOCKET)
    set_property(
	SOURCE ${LUA_SOCKET}
	APPEND
	PROPERTY COMPILE_DEFINITIONS
	LUA_COMPAT_APIINTCASTS LUA_LIB _CRT_SECURE_NO_WARNINGS _WINSOCK_DEPRECATED_NO_WARNINGS
    )
endif ()

if(UINT_ESPECIALLY)
    ADD_DEFINITIONS(-DUINT_ESPECIALLY)
endif()
	
if ( WIN32 AND NOT CYGWIN )
    if (USING_LUAJIT)
        target_link_libraries(xlua
            ${CMAKE_SOURCE_DIR}/${LUAJIT_SRC_PATH}/lua51.lib
            ws2_32
            ${THIRDPART_LIB} 
        )
    else ()
        target_link_libraries(xlua
            ws2_32
            ${THIRDPART_LIB} 
        )
    endif()
else ( )
    if (USING_LUAJIT AND NOT IOS)
		target_link_libraries(xlua
			${LUAJIT_STATIC_LIB}
			m
            ${THIRDPART_LIB} 
		)
    else ()
        # dlopen is for package.loadlib, which the core preset does not build.
        if (XLUA_PRESET STREQUAL "core")
            set (xlua_dl_libs)
        else ()
            set (xlua_dl_libs ${CMAKE_DL_LIBS})
        endif ()
        target_link_libraries(xlua
            m
            ${xlua_dl_libs}
            ${THIRDPART_LIB} 
        )
    endif()
endif ( )

if ( ANDROID )
    target_link_options(xlua PRIVATE -Wl,-z,max-page-size=16384 -Wl,-z,common-page-size=16384) # android 16K Pagesize
endif ( )

# Test-only stand-alone interpreter for Lua's official test suites
# (Test/Native/lua_tests.sh). It compiles the same core sources with the same
# definitions as libxlua (LUA_USE_*, LUA_IDSIZE, LUAC_COMPATIBLE_FORMAT,
# XLUA_SANITIZE) plus the vendored lua.c. XLUA_LUA_LTESTS_DIR points at the
# suite's ltests/ directory and builds the interpreter for the internal tests:
# ltests.c is added and LUA_USER_H="ltests.h" applies to this target only.
option ( XLUA_LUA_INTERPRETER "Also build a stand-alone lua interpreter for Lua's test suites" OFF )
set ( XLUA_LUA_LTESTS_DIR "" CACHE PATH "ltests/ directory of a Lua test suite (internal-test interpreter)" )
if (XLUA_LUA_INTERPRETER)
    if (USING_LUAJIT)
        message(FATAL_ERROR "XLUA_LUA_INTERPRETER builds PUC Lua only")
    endif ()
    set ( XLUA_LUA_SOURCES ${LUA_CORE_ALL} ${LUA_SRC_PATH}/lua.c )
    if (XLUA_LUA_LTESTS_DIR)
        list (APPEND XLUA_LUA_SOURCES ${XLUA_LUA_LTESTS_DIR}/ltests.c)
    endif ()
    add_executable (xlua_lua ${XLUA_LUA_SOURCES})
    set_target_properties (xlua_lua PROPERTIES OUTPUT_NAME lua ENABLE_EXPORTS ON)
    if (XLUA_LUA_LTESTS_DIR)
        target_include_directories (xlua_lua BEFORE PRIVATE ${XLUA_LUA_LTESTS_DIR})
        target_compile_definitions (xlua_lua PRIVATE [=[LUA_USER_H="ltests.h"]=])
    endif ()
    if (LUAC_COMPATIBLE_FORMAT)
        target_compile_definitions (xlua_lua PRIVATE LUAC_COMPATIBLE_FORMAT)
    endif ()
    if (XLUA_SANITIZE)
        target_compile_options (xlua_lua PRIVATE -fsanitize=${XLUA_SANITIZE} -fno-omit-frame-pointer -fno-sanitize-recover=all -g)
        target_link_options (xlua_lua PRIVATE -fsanitize=${XLUA_SANITIZE})
    endif ()
    if ( WIN32 AND NOT CYGWIN )
        target_link_libraries (xlua_lua ${THIRDPART_LIB})
    else ()
        target_link_libraries (xlua_lua m ${CMAKE_DL_LIBS} ${THIRDPART_LIB})
    endif ()
    # The REPL uses readline as upstream's makefiles build it: luaconf.h turns
    # LUA_USE_READLINE on with LUA_USE_MACOSX (5.3, 5.5) and LUA_USE_LINUX
    # (5.3); for 5.4 it comes from the linux-readline and macosx targets, and
    # main.lua's prompt tests expect readline's echo of piped input. Lua 5.5
    # on Linux loads libreadline.so with dlopen instead. Only lua.c reads the
    # macro, and lua.c is not part of libxlua.
    if (LUA_VERSION MATCHES "^5[.]4[.]")
        target_compile_definitions (xlua_lua PRIVATE LUA_USE_READLINE)
    endif ()
    if (NOT WIN32 AND (APPLE OR LUA_VERSION MATCHES "^5[.][34][.]"))
        find_library (XLUA_READLINE_LIB readline)
        if (NOT XLUA_READLINE_LIB)
            message(FATAL_ERROR "XLUA_LUA_INTERPRETER needs libreadline (Debian/Ubuntu: libreadline-dev)")
        endif ()
        target_link_libraries (xlua_lua ${XLUA_READLINE_LIB})
    endif ()
endif ()