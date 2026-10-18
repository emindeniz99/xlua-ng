# Presets (docs/design/presets.md): the defaults of the native options.
#
# build/CMakeLists.txt includes this file before it declares the options. It reads
# XLUA_PRESET and sets xlua_preset_<OPTION> for every option a preset changes; the
# option then takes that value as its default. The environment variable of the same
# name as an option, an explicit -D and an existing cache entry still win, so a
# project can change one option of a preset:
#
#   XLUA_PRESET=minimal XLUA_LUASOCKET=ON build/make_android_lua55.sh
#   cmake -S build -B out -DXLUA_PRESET=minimal -DXLUA_EXPORTS=all
#
# legacy, the default, keeps the defaults the options had before presets existed.
# Tools/presets.py has the same table for the checks (Test/Native/presets_check.py
# configures each preset and compares the cache with it).

# Platform defaults, the same in every preset that leaves them alone.
if (ANDROID OR CMAKE_SYSTEM_NAME MATCHES "^(OHOS|Switch|WindowsStore)$")
    set (xlua_platform_memory_snapshot OFF)
else ()
    set (xlua_platform_memory_snapshot ON)
endif ()

# legacy: upstream's library.
set (xlua_preset_XLUA_LUASOCKET ON)
set (xlua_preset_XLUA_MIME ON)
set (xlua_preset_XLUA_LEAK_CHECKER ON)
set (xlua_preset_XLUA_UINT64 ON)
set (xlua_preset_XLUA_MEMORY_SNAPSHOT ${xlua_platform_memory_snapshot})
set (xlua_preset_XLUA_LIBS all)
set (xlua_preset_XLUA_EXPORTS all)
set (xlua_preset_XLUA_HARDEN OFF)
set (xlua_preset_XLUA_ICF ON)
set (xlua_preset_XLUA_LUASCRIPT OFF)
set (xlua_preset_XLUA_OS_FULL ON)
set (xlua_preset_XLUA_LOADLIB ON)
set (xlua_preset_XLUA_DOFILE ON)

if (XLUA_PRESET STREQUAL "minimal")
    # The minimal column of the feature table in docs/design/presets.md. Values
    # marked provisional there wait for the owner's decision.
    set (xlua_preset_XLUA_LUASOCKET OFF)
    set (xlua_preset_XLUA_MIME OFF)
    set (xlua_preset_XLUA_LEAK_CHECKER OFF)
    set (xlua_preset_XLUA_UINT64 ON)
    set (xlua_preset_XLUA_MEMORY_SNAPSHOT OFF)
    # Without io; os with time, clock, date and difftime only; utf8 stays (the
    # table does not leave it out); LuaJIT keeps jit, which scripts test to
    # detect LuaJIT, and ffi (provisional).
    if (USING_LUAJIT)
        set (xlua_preset_XLUA_LIBS package table os string math debug bit jit ffi)
    else ()
        set (xlua_preset_XLUA_LIBS package coroutine table os string math utf8 debug)
    endif ()
    set (xlua_preset_XLUA_OS_FULL OFF)
    # No C modules (package.loadlib, the C searchers, dlopen) and no dofile or
    # loadfile; require and xLua's loaders stay.
    set (xlua_preset_XLUA_LOADLIB OFF)
    set (xlua_preset_XLUA_DOFILE OFF)
    # "used by C# + listed extras": a version script where the linker takes one
    # (ELF shared libraries); static libraries (iOS, Switch) and Mach-O and PE
    # libraries keep every export.
    if (ANDROID OR CMAKE_SYSTEM_NAME MATCHES "^(OHOS|Linux)$")
        set (xlua_preset_XLUA_EXPORTS managed)
    endif ()
    # XLUA_HARDEN stays off and XLUA_ICF on, as in legacy (provisional).
endif ()

if (XLUA_PRESET STREQUAL "core")
    # The core column (docs/design/presets.md, docs/design/core-luascript.md):
    # no bridge and none of its parts, the safe libraries of LuaScript
    # (LuaLibs.Safe), and the exports LuaScript imports where the linker takes a
    # version script. build/CMakeLists.txt refuses the bridge's options when they
    # are turned on, and the libraries LuaScript never opens.
    set (xlua_preset_XLUA_LUASOCKET OFF)
    set (xlua_preset_XLUA_MIME OFF)
    set (xlua_preset_XLUA_LEAK_CHECKER OFF)
    set (xlua_preset_XLUA_UINT64 OFF)
    set (xlua_preset_XLUA_MEMORY_SNAPSHOT OFF)
    set (xlua_preset_XLUA_LIBS coroutine table string math utf8)
    if (ANDROID OR CMAKE_SYSTEM_NAME MATCHES "^(OHOS|Linux)$")
        set (xlua_preset_XLUA_EXPORTS managed)
    endif ()
    set (xlua_preset_XLUA_LUASCRIPT ON)
endif ()

# xlua_option_default(<option>): sets <option>_DEFAULT to the environment variable of
# that name when it is set and not empty (a CMake boolean: ON, 1, YES ... or OFF, 0,
# NO ...), otherwise to the preset's value.
function (xlua_option_default option)
    if (DEFINED ENV{${option}} AND NOT "$ENV{${option}}" STREQUAL "")
        if ("$ENV{${option}}")
            set (${option}_DEFAULT ON PARENT_SCOPE)
        else ()
            set (${option}_DEFAULT OFF PARENT_SCOPE)
        endif ()
    else ()
        set (${option}_DEFAULT ${xlua_preset_${option}} PARENT_SCOPE)
    endif ()
endfunction ()
