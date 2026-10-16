#!/bin/bash
# Build a Release libxlua for each runtime, then run Test/Native/benchmark.py
# against all of them (bridge, pure Lua, GC and plugin size).
# Linux with cmake, a C compiler, Mono and mcs. Builds LuaJIT in place like
# sanitize.sh, so run it on a writable copy of the checkout.
# Usage: Test/Native/benchmark.sh <output dir> [benchmark.py options]
# VERSIONS selects runtimes (default: 5.5.1 5.4.9 5.3.6 luajit).
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${1:?usage: benchmark.sh <output dir> [benchmark.py options]}" && cd "$1" && pwd)"
shift
plugins=()
for version in ${VERSIONS:-5.5.1 5.4.9 5.3.6 luajit}; do
    build="$out/build-$version"
    if [[ "$version" == luajit ]]; then
        make -C "$repo/build/luajit-2.1" clean >/dev/null
        make -C "$repo/build/luajit-2.1" BUILDMODE=static XCFLAGS=-fPIC >/dev/null
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=Release -DUSING_LUAJIT=ON >/dev/null
    else
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=Release -DLUA_VERSION="$version" >/dev/null
    fi
    cmake --build "$build" --parallel 4 >/dev/null
    plugins+=(--plugin "$version=$build/libxlua.so")
done
python3 "$repo/Test/Native/benchmark.py" "${plugins[@]}" --output "$out" "$@"
