#!/bin/bash
# Build libxlua with AddressSanitizer, UndefinedBehaviorSanitizer and Lua's C API
# checks (XLUA_APICHECK, LUA_USE_APICHECK), then run the export check, native
# regression checks, profiler smoke test (not on LuaJIT) and the managed suite
# (reflection, generated and minimized generated code, with the memory
# diagnostics compiled in) against it.
# Linux only (GCC or Clang). Usage: Test/Native/sanitize.sh <empty output dir>
# VERSIONS selects runtimes (default: 5.3.6 5.4.9 5.5.1 luajit). luajit-uint is
# LuaJIT built with -DUINT_ESPECIALLY=ON (ulong as a separate unsigned kind).
# MONO_VERSION is passed to run_managed.py (default 6.14.1; "any" skips the check).
# XLUA_WERROR=ON in the environment turns warnings in xLua's C files into errors.
# XLUA_LUASOCKET=OFF builds without LuaSocket, XLUA_MIME=OFF without mime.c; the
# checks then expect require "socket" (or "mime") to fail with "module not found".
# XLUA_LEAK_CHECKER=OFF, XLUA_UINT64=OFF and XLUA_LIBS=<list> (build/README.md) reach
# CMake through the environment, and the checks expect what they select. LuaJIT
# always builds the uint64 table and never the leak checker. The managed suite's
# harness needs io, os, string, table and math in XLUA_LIBS, and the checks find
# LuaJIT by its jit library (Lua 5.x builds ignore the name jit).
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${1:?usage: sanitize.sh <output dir>}" && cd "$1" && pwd)"
cc="${CC:-cc}"
# Mono and Python are not instrumented, so the ASan runtime must be preloaded.
asan_runtime="$("$cc" -print-file-name=libasan.so)"
test -f "$asan_runtime" || { echo "libasan.so not found for $cc" >&2; exit 1; }
# Mono turns SIGSEGV into NullReferenceException; leave those signals to it.
export ASAN_OPTIONS="detect_leaks=0:handle_segv=0:allow_user_segv_handler=1:abort_on_error=1"
export UBSAN_OPTIONS="print_stacktrace=1:halt_on_error=1"
# Clang's -fsanitize=undefined includes float-cast-overflow; GCC's does not, and
# without it a GCC run misses out-of-range float to integer conversions.
sanitizers=address,undefined
if ! "$cc" -dM -E -x c /dev/null | grep -q __clang__; then
    sanitizers+=,float-cast-overflow
fi

module_flags=(-DXLUA_LUASOCKET="${XLUA_LUASOCKET:-ON}" -DXLUA_MIME="${XLUA_MIME:-ON}")
if [[ "${XLUA_LUASOCKET:-ON}" == OFF ]]; then
    export XLUA_EXPECT_NATIVE_MODULES=""
elif [[ "${XLUA_MIME:-ON}" == OFF ]]; then
    export XLUA_EXPECT_NATIVE_MODULES="socket"
else
    export XLUA_EXPECT_NATIVE_MODULES="socket,mime"
fi

export XLUA_EXPECT_LIBS="${XLUA_LIBS:-all}"
for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1 luajit}; do
    build="$out/$version"
    option_flags=()
    if [[ "$version" == luajit* ]]; then
        option_flags=(-DXLUA_UINT64=ON)
        export XLUA_EXPECT_UINT64=ON
        unset XLUA_EXPECT_LEAK_CHECKER
    else
        export XLUA_EXPECT_UINT64="${XLUA_UINT64:-ON}"
        export XLUA_EXPECT_LEAK_CHECKER="${XLUA_LEAK_CHECKER:-ON}"
    fi
    if [[ "$version" == luajit || "$version" == luajit-uint ]]; then
        make -C "$repo/build/luajit-2.1" clean >/dev/null
        # LuaJIT compiles its API checks into libluajit.a.
        make -C "$repo/build/luajit-2.1" BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK" >/dev/null
        uint=OFF
        if [[ "$version" == luajit-uint ]]; then uint=ON; fi
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DUSING_LUAJIT=ON -DUINT_ESPECIALLY="$uint" \
            -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON "${module_flags[@]}" "${option_flags[@]}"
    else
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION="$version" \
            -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON "${module_flags[@]}"
    fi
    cmake --build "$build" --parallel 4
    plugin="$build/libxlua.so"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/check_exports.py" "$plugin"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/native_regression.py" "$plugin"
    if [[ "$version" != luajit* ]]; then
        # The internal-header leak checker the profiler test uses is not built for LuaJIT.
        LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/profiler_smoke.py" "$plugin"
    fi
    # XLUA_MEMORY_DIAGNOSTICS: the memory snapshot cases run too, so the walk also runs under the sanitizers from C#.
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/run_managed.py" --gen-code --gen-code-minimize \
        --define XLUA_MEMORY_DIAGNOSTICS \
        --mono-version "${MONO_VERSION:-6.14.1}" --plugin "$plugin" --output "$build/managed"
    echo "XLUA_SANITIZE_PASS: $version"
done
