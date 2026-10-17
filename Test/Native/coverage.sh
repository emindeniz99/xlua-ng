#!/bin/bash
# Measure line, function and branch coverage of xLua's own C files (xlua.c, i64lib.c,
# memory_leak_checker.c) with Clang source-based coverage (-DXLUA_COVERAGE=ON).
# Runs the native checks and the managed suite against the instrumented
# libxlua, then prints an llvm-cov report per runtime and writes an HTML view
# and a per-branch listing (branches.txt).
#
# Linux with clang, llvm-profdata, llvm-cov, cmake, Mono and mcs on PATH.
# Usage: Test/Native/coverage.sh <output dir>
# VERSIONS selects runtimes (default: 5.3.6 5.4.9 5.5.1).
# MONO_VERSION is passed to run_managed.py (default 6.14.1; "any" skips the check).
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${1:?usage: coverage.sh <output dir>}" && cd "$1" && pwd)"
clang_bin="${CLANG:-clang}"
profdata="${LLVM_PROFDATA:-llvm-profdata}"
cov="${LLVM_COV:-llvm-cov}"

for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1}; do
    build="$out/$version"
    rm -rf "$build/profiles"
    mkdir -p "$build/profiles"
    cmake -S "$repo/build" -B "$build" -DCMAKE_C_COMPILER="$clang_bin" \
        -DCMAKE_BUILD_TYPE=Debug -DLUA_VERSION="$version" -DXLUA_COVERAGE=ON
    cmake --build "$build" --parallel 4
    plugin="$build/libxlua.so"
    # %p keeps one raw profile per process (Python checks and the Mono runner).
    export LLVM_PROFILE_FILE="$build/profiles/%p.profraw"
    python3 "$repo/Test/Native/check_exports.py" "$plugin"
    python3 "$repo/Test/Native/native_regression.py" "$plugin"
    python3 "$repo/Test/Native/profiler_smoke.py" "$plugin"
    python3 "$repo/Test/Native/run_managed.py" --gen-code --gen-code-minimize \
        --mono-version "${MONO_VERSION:-6.14.1}" --plugin "$plugin" --output "$build/managed"
    unset LLVM_PROFILE_FILE

    "$profdata" merge -sparse "$build"/profiles/*.profraw -o "$build/xlua.profdata"
    sources=("$repo/build/xlua.c" "$repo/build/i64lib.c" "$repo/build/memory_leak_checker.c")
    "$cov" report "$plugin" -instr-profile="$build/xlua.profdata" "${sources[@]}" | tee "$build/report.txt"
    "$cov" report -show-functions "$plugin" -instr-profile="$build/xlua.profdata" "${sources[@]}" > "$build/functions.txt"
    "$cov" show -format=html -output-dir="$build/html" "$plugin" -instr-profile="$build/xlua.profdata" "${sources[@]}"
    # Per-branch counts; an untaken side shows as "True: 0" or "False: 0".
    "$cov" show -show-branches=count "$plugin" -instr-profile="$build/xlua.profdata" "${sources[@]}" > "$build/branches.txt"
    echo "Uncovered functions ($version):"
    # Columns: name, regions, missed regions, cover, lines, missed lines, cover, ...
    awk '$1 !~ /^(Name|---|File|TOTAL)/ && NF >= 7 && $5 > 0 && $5 == $6 { print "  " $1 }' "$build/functions.txt" | sort -u
    echo "XLUA_COVERAGE_DONE: $version (HTML in $build/html)"
done
