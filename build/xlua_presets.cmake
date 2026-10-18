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

if (XLUA_PRESET STREQUAL "minimal")
    # The minimal column of the feature table in docs/design/presets.md. Values
    # marked provisional there wait for the owner's decision.
    set (xlua_preset_XLUA_LUASOCKET OFF)
    set (xlua_preset_XLUA_MIME OFF)
    set (xlua_preset_XLUA_LEAK_CHECKER OFF)
    set (xlua_preset_XLUA_UINT64 ON)
    set (xlua_preset_XLUA_MEMORY_SNAPSHOT OFF)
    # Without io. os stays whole until its run-time subset (time, clock, date,
    # difftime) exists; utf8 stays (the table does not leave it out); LuaJIT keeps
    # jit, which scripts test to detect LuaJIT, and ffi (provisional).
    if (USING_LUAJIT)
        set (xlua_preset_XLUA_LIBS package table os string math debug bit jit ffi)
    else ()
        set (xlua_preset_XLUA_LIBS package coroutine table os string math utf8 debug)
    endif ()
    # "used by C# + listed extras": a version script where the linker takes one
    # (ELF shared libraries); static libraries (iOS, Switch) and Mach-O and PE
    # libraries keep every export.
    if (ANDROID OR CMAKE_SYSTEM_NAME MATCHES "^(OHOS|Linux)$")
        set (xlua_preset_XLUA_EXPORTS managed)
    endif ()
    # XLUA_HARDEN stays off and XLUA_ICF on, as in legacy (provisional).
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
