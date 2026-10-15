#!/bin/bash
# Build libxlua with AddressSanitizer and UndefinedBehaviorSanitizer, then run
# the export check, profiler smoke test and managed suite against it.
# Linux only (GCC or Clang). Usage: Test/Native/sanitize.sh <empty output dir>
# VERSIONS selects runtimes (default: 5.3.6 5.4.9 5.5.1 luajit).
# MONO_VERSION is passed to run_managed.py (default 6.14.1; "any" skips the check).
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

for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1 luajit}; do
    build="$out/$version"
    if [[ "$version" == luajit ]]; then
        make -C "$repo/build/luajit-2.1" clean >/dev/null
        make -C "$repo/build/luajit-2.1" BUILDMODE=static XCFLAGS=-fPIC >/dev/null
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DUSING_LUAJIT=ON -DXLUA_SANITIZE=address,undefined
    else
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION="$version" -DXLUA_SANITIZE=address,undefined
    fi
    cmake --build "$build" --parallel 4
    plugin="$build/libxlua.so"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/check_exports.py" "$plugin"
    if [[ "$version" != luajit ]]; then
        # LuaJIT builds exclude the memory-leak checker the profiler test uses.
        LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/profiler_smoke.py" "$plugin"
    fi
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/run_managed.py" \
        --mono-version "${MONO_VERSION:-6.14.1}" --plugin "$plugin" --output "$build/managed"
    echo "XLUA_SANITIZE_PASS: $version"
done
