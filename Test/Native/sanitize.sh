#!/bin/bash
# Build libxlua with AddressSanitizer, UndefinedBehaviorSanitizer and Lua's C API
# checks (XLUA_APICHECK, LUA_USE_APICHECK), then run the export check, native
# regression checks, profiler smoke test (not on LuaJIT) and the managed suite
# (reflection, generated and minimized generated code) against it.
# Linux only (GCC or Clang). Usage: Test/Native/sanitize.sh <empty output dir>
# VERSIONS selects runtimes (default: 5.3.6 5.4.9 5.5.1 luajit). luajit-uint is
# LuaJIT built with -DUINT_ESPECIALLY=ON (ulong as a separate unsigned kind).
# MONO_VERSION is passed to run_managed.py (default 6.14.1; "any" skips the check).
# XLUA_WERROR=ON in the environment turns warnings in xLua's C files into errors.
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

for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1 luajit}; do
    build="$out/$version"
    if [[ "$version" == luajit || "$version" == luajit-uint ]]; then
        make -C "$repo/build/luajit-2.1" clean >/dev/null
        # LuaJIT compiles its API checks into libluajit.a.
        make -C "$repo/build/luajit-2.1" BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK" >/dev/null
        uint=OFF
        if [[ "$version" == luajit-uint ]]; then uint=ON; fi
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DUSING_LUAJIT=ON -DUINT_ESPECIALLY="$uint" \
            -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON
    else
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION="$version" \
            -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON
    fi
    cmake --build "$build" --parallel 4
    plugin="$build/libxlua.so"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/check_exports.py" "$plugin"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/native_regression.py" "$plugin"
    if [[ "$version" != luajit* ]]; then
        # LuaJIT builds exclude the memory-leak checker the profiler test uses.
        LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/profiler_smoke.py" "$plugin"
    fi
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/run_managed.py" --gen-code --gen-code-minimize \
        --mono-version "${MONO_VERSION:-6.14.1}" --plugin "$plugin" --output "$build/managed"
    echo "XLUA_SANITIZE_PASS: $version"
done
