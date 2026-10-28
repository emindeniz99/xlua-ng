#!/bin/bash
# Measure line, function and branch coverage of xLua's own C files (xlua_core.c, xlua_core_limits.c, xlua.c,
# i64lib.c, memory_leak_checker.c) with Clang source-based coverage (-DXLUA_COVERAGE=ON).
# Runs the native checks and the managed suite against the instrumented
# libxlua (with Lua's C API checks, XLUA_APICHECK), then prints an llvm-cov
# report per runtime and writes an HTML view and a per-branch listing
# (branches.txt).
#
# Linux with clang, llvm-profdata, llvm-cov, cmake, Mono and mcs on PATH. With a
# .NET SDK on PATH (or through mise) the managed suite also runs through the
# source generator's plain and GEN_CODE_MINIMIZE bindings; without one, through
# reflection only, and the script says so.
# Usage: Test/Native/coverage.sh <output dir>
# VERSIONS selects runtimes (default: 5.3.6 5.4.9 5.5.1). core-5.4.9 and core-5.5.1
# build XLUA_PRESET=core (every export) and measure xlua_core.c, xlua_core_limits.c,
# xlua_core_sandbox.c and xlua_core_nobc.c with check_exports.py --preset core,
# native_regression.py and core_smoke.c; the bridge's checks do not apply there.
# MONO_VERSION is passed to run_managed.py (default 6.14.1; "any" skips the check).
# MIN_BRANCH_COVERAGE is the coverage gate: "FILE=PERCENT ..." (default
# "xlua.c=99 xlua_core.c=98 xlua_core_limits.c=93", owner decision 2026-10-28),
# checked for each runtime that measures FILE. When a file's branch coverage is
# below its percentage the script fails, after every runtime has been measured;
# it prints XLUA_COVERAGE_GATE lines either way. An empty value turns the gate off.
# The core-* runtimes take CORE_MIN_BRANCH_COVERAGE instead (default empty: off),
# since their build of xlua_core_limits.c leaves the bridge's hooks to the bridge.
# The analysis workflow's coverage job runs with the default (Test/README.md).
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${1:?usage: coverage.sh <output dir>}" && cd "$1" && pwd)"
clang_bin="${CLANG:-clang}"
managed_modes=()
if command -v dotnet >/dev/null 2>&1 || mise which dotnet >/dev/null 2>&1; then
    managed_modes=(--source-gen --source-gen-minimize)
else
    echo "XLUA_COVERAGE_NOTE: no .NET SDK, so the managed suite runs through reflection only (no generated bindings)"
fi
profdata="${LLVM_PROFDATA:-llvm-profdata}"
cov="${LLVM_COV:-llvm-cov}"
# build/CMakeLists.txt records the sources as build/... (-ffile-prefix-map, build/README.md
# "Reproducible builds"); this maps them back to the checkout for the filters and the HTML.
cov_paths=(-path-equivalence="build,$repo/build")
min_branch_coverage="${MIN_BRANCH_COVERAGE-xlua.c=99 xlua_core.c=98 xlua_core_limits.c=93}"
core_branch_coverage="${CORE_MIN_BRANCH_COVERAGE-}"
gate_failed=0

for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1}; do
    build="$out/$version"
    rm -rf "$build/profiles"
    mkdir -p "$build/profiles"
    if [[ "$version" == core-* ]]; then
        cmake -S "$repo/build" -B "$build" -DCMAKE_C_COMPILER="$clang_bin" -DCMAKE_BUILD_TYPE=Debug \
            -DLUA_VERSION="${version#core-}" -DXLUA_PRESET=core -DXLUA_EXPORTS=all -DXLUA_COVERAGE=ON -DXLUA_APICHECK=ON
    else
        cmake -S "$repo/build" -B "$build" -DCMAKE_C_COMPILER="$clang_bin" \
            -DCMAKE_BUILD_TYPE=Debug -DLUA_VERSION="$version" -DXLUA_COVERAGE=ON -DXLUA_APICHECK=ON
    fi
    cmake --build "$build" --parallel 4
    plugin="$build/libxlua.so"
    # %p keeps one raw profile per process (Python checks and the Mono runner).
    export LLVM_PROFILE_FILE="$build/profiles/%p.profraw"
    if [[ "$version" == core-* ]]; then
        python3 "$repo/Test/Native/check_exports.py" --preset core "$plugin"
        python3 "$repo/Test/Native/native_regression.py" "$plugin"
        "$clang_bin" -std=c17 -Wall -Wextra -Werror "$repo/Test/Native/core_smoke.c" -L"$build" -lxlua \
            -Wl,-rpath,"$build" -o "$build/core_smoke"
        timeout 300 "$build/core_smoke" "$repo/Test/Native/core_sandbox_cases.txt"
    else
        python3 "$repo/Test/Native/check_exports.py" "$plugin"
        python3 "$repo/Test/Native/native_regression.py" "$plugin"
        python3 "$repo/Test/Native/profiler_smoke.py" "$plugin"
        python3 "$repo/Test/Native/run_managed.py" "${managed_modes[@]}" \
            --mono-version "${MONO_VERSION:-6.14.1}" --plugin "$plugin" --output "$build/managed"
    fi
    unset LLVM_PROFILE_FILE

    "$profdata" merge -sparse "$build"/profiles/*.profraw -o "$build/xlua.profdata"
    if [[ "$version" == core-* ]]; then
        sources=("$repo/build/xlua_core.c" "$repo/build/xlua_core_limits.c" "$repo/build/xlua_core.h"
                 "$repo/build/xlua_core_sandbox.c" "$repo/build/xlua_core_buffer.c" "$repo/build/xlua_core_nobc.c")
    else
        sources=("$repo/build/xlua_core.c" "$repo/build/xlua_core_limits.c" "$repo/build/xlua_core.h" "$repo/build/xlua.c" "$repo/build/i64lib.c" "$repo/build/memory_leak_checker.c")
    fi
    "$cov" report "$plugin" -instr-profile="$build/xlua.profdata" "${cov_paths[@]}" "${sources[@]}" | tee "$build/report.txt"
    "$cov" report -show-functions "$plugin" -instr-profile="$build/xlua.profdata" "${cov_paths[@]}" "${sources[@]}" > "$build/functions.txt"
    "$cov" show -format=html -output-dir="$build/html" "$plugin" -instr-profile="$build/xlua.profdata" "${cov_paths[@]}" "${sources[@]}"
    # Per-branch counts; an untaken side shows as "True: 0" or "False: 0".
    "$cov" show -show-branches=count "$plugin" -instr-profile="$build/xlua.profdata" "${cov_paths[@]}" "${sources[@]}" > "$build/branches.txt"
    # The gate reads llvm-cov's per-file summary (branch counts), not the report's columns.
    "$cov" export -summary-only "$plugin" -instr-profile="$build/xlua.profdata" "${cov_paths[@]}" "${sources[@]}" > "$build/summary.json"
    gate="$min_branch_coverage"
    if [[ "$version" == core-* ]]; then gate="$core_branch_coverage"; fi
    # shellcheck disable=SC2086 # one FILE=PERCENT argument per word
    if ! python3 "$repo/Test/Native/coverage_gate.py" "$build/summary.json" "$version" $gate; then
        gate_failed=1
    fi
    echo "Uncovered functions ($version):"
    # Columns: name, regions, missed regions, cover, lines, missed lines, cover, ...
    awk '$1 !~ /^(Name|---|File|TOTAL)/ && NF >= 7 && $5 > 0 && $5 == $6 { print "  " $1 }' "$build/functions.txt" | sort -u
    echo "XLUA_COVERAGE_DONE: $version (HTML in $build/html)"
done
if [[ "$gate_failed" != 0 ]]; then
    echo "XLUA_COVERAGE_GATE: branch coverage below MIN_BRANCH_COVERAGE ($min_branch_coverage) or CORE_MIN_BRANCH_COVERAGE ($core_branch_coverage); see the FAIL lines above and branches.txt" >&2
    exit 1
fi
