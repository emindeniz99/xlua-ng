#!/bin/bash
# Measure line, function and branch coverage of xLua's own C files with Clang
# source-based coverage (-DXLUA_COVERAGE=ON), with Lua's C API checks
# (XLUA_APICHECK), in each configuration that compiles them; prints an llvm-cov
# report per runtime and writes an HTML view and a per-branch listing
# (branches.txt). Test/README.md, "Static analysis, coverage and fuzzing".
#
# Linux with clang, llvm-profdata, llvm-cov, cmake, Mono and mcs on PATH. With a
# .NET SDK on PATH (or through mise) the managed suite also runs through the
# source generator's plain and GEN_CODE_MINIMIZE bindings; without one, through
# reflection only, and the script says so. The gate does not depend on those
# passes: every branch it requires is reached without them.
# Usage: Test/Native/coverage.sh <output dir>
#
# VERSIONS selects the runtimes (default: all of them, as the analysis workflow runs):
#  - 5.3.6, 5.4.9, 5.5.1: the legacy preset (the bridge), with the native checks,
#    the profiler test and the managed suite; measures xlua.c, xlua_core.c,
#    xlua_core_limits.c, i64lib.c and memory_leak_checker.c;
#  - core-5.4.9, core-5.5.1: XLUA_PRESET=core (every export), with
#    check_exports.py --preset core, native_regression.py and core_smoke.c;
#    measures xlua_core.c, xlua_core_limits.c, xlua_core_sandbox.c,
#    xlua_core_buffer.c and xlua_core_nobc.c;
#  - core-tp-5.5.1: the same with xlua_core_limits.c's test points
#    (-DXLUA_LIMITS_TEST_POINTS, a test build only; check_exports.py expects their
#    two exports), where core_regression.py's check_core_stop_window stops a call
#    after call_body's last stop check, which in a real build only another
#    thread does: the sandbox's gate is 100 there (owner decision of 2026-10-28);
#  - minimal-5.3.6, minimal-5.4.9, minimal-5.5.1: XLUA_PRESET=minimal (every
#    export), with the native checks; measures xlua_libs.c, which builds with a
#    reduced XLUA_LIBS only (the legacy runs measure the rest);
#  - bytecode-5.5.1: -DXLUA_BYTECODE_ONLY=ON, with the bytecode-only native
#    checks, bytecode_luaenv.py and chunkc_check.py (an uninstrumented library of
#    the same version compiles their chunks); measures xlua_noparser.c and the
#    host program xlua_chunkc.c.
# MONO_VERSION is passed to run_managed.py (default 6.14.1; "any" skips the check).
#
# The gate: MIN_BRANCH_COVERAGE (legacy), CORE_MIN_BRANCH_COVERAGE,
# MINIMAL_MIN_BRANCH_COVERAGE and BYTECODE_MIN_BRANCH_COVERAGE, each
# "FILE=PERCENT ..." (defaults below, owner decisions of 2026-10-28), are checked
# for each runtime of that kind; FILE@RUNTIME=PERCENT sets one runtime's minimum.
# When a file's branch coverage is below its percentage, or the build lacks the
# file, the script fails, after every runtime has been measured; it prints
# XLUA_COVERAGE_GATE lines either way. An empty value turns that kind's gate off.
# The minimums below 100 and why (2 GB strings on Lua 5.4, the stop window that
# only another thread reaches outside core-tp, the core build's entries, the
# snapshot walk's native allocation failures, the test points' own code) are in
# Test/README.md.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${1:?usage: coverage.sh <output dir>}" && cd "$1" && pwd)"
clang_bin="${CLANG:-clang}"
managed_modes=()
bytecode_modes=()
if command -v dotnet >/dev/null 2>&1 || mise which dotnet >/dev/null 2>&1; then
    managed_modes=(--source-gen --source-gen-minimize)
else
    bytecode_modes=(--mode reflection --mode locked)
    echo "XLUA_COVERAGE_NOTE: no .NET SDK, so the managed suite runs through reflection only (no generated bindings)"
fi
profdata="${LLVM_PROFDATA:-llvm-profdata}"
cov="${LLVM_COV:-llvm-cov}"
# build/CMakeLists.txt records the sources as build/... (-ffile-prefix-map, build/README.md
# "Reproducible builds"); this maps them back to the checkout for the filters and the HTML.
cov_paths=(-path-equivalence="build,$repo/build")
gate_legacy="${MIN_BRANCH_COVERAGE-xlua.c=100 xlua_core.c=100 xlua_core_limits.c=100 xlua_core.h=100 i64lib.c=100 memory_leak_checker.c=83}"
gate_core="${CORE_MIN_BRANCH_COVERAGE-xlua_core.c=100 xlua_core.c@core-5.4.9=99 xlua_core_limits.c=100 xlua_core_limits.c@core-tp-5.5.1=84 xlua_core.h=80 xlua_core_sandbox.c=97 xlua_core_sandbox.c@core-tp-5.5.1=100 xlua_core_buffer.c=100 xlua_core_buffer.c@core-5.4.9=99 xlua_core_nobc.c=100}"
gate_minimal="${MINIMAL_MIN_BRANCH_COVERAGE-xlua_libs.c=100}"
gate_bytecode="${BYTECODE_MIN_BRANCH_COVERAGE-xlua_noparser.c=100 xlua_chunkc.c=100}"
gate_failed=0
b="$repo/build"

# The tests of one runtime, in a subshell so that the expectations of one kind
# stay out of the next. LLVM_PROFILE_FILE is set by the caller.
run_tests() (
    local kind="$1" version="$2" build="$3" plugin="$3/libxlua.so"
    case "$kind" in
    core)
        if [[ "$test_points" == ON ]]; then
            export XLUA_EXPECT_TEST_POINTS=ON
        fi
        python3 "$repo/Test/Native/check_exports.py" --preset core "$plugin"
        python3 "$repo/Test/Native/native_regression.py" "$plugin"
        "$clang_bin" -std=c17 -Wall -Wextra -Werror "$repo/Test/Native/core_smoke.c" -L"$build" -lxlua \
            -Wl,-rpath,"$build" -o "$build/core_smoke"
        timeout 300 "$build/core_smoke" "$repo/Test/Native/core_sandbox_cases.txt"
        ;;
    minimal)
        eval "$(python3 "$repo/Tools/presets.py" --expect-env minimal)"
        python3 "$repo/Test/Native/check_exports.py" "$plugin"
        python3 "$repo/Test/Native/native_regression.py" "$plugin"
        ;;
    bytecode)
        export XLUA_EXPECT_BYTECODE_ONLY=ON XLUA_COMPILER_LIB="$build/compiler/libxlua.so"
        export XLUA_EXPECT_BYTECODE_MODULES="util,profiler,memory"
        python3 "$repo/Test/Native/check_exports.py" "$plugin"
        python3 "$repo/Test/Native/native_regression.py" "$plugin"
        python3 "$repo/Test/Native/bytecode_luaenv.py" --plugin "$plugin" \
            --compiler-plugin "$build/compiler/libxlua.so" --output "$build/luaenv" "${bytecode_modes[@]}"
        python3 "$repo/Test/Native/chunkc_check.py" "$build/xlua_chunkc" "$build/chunkc"
        ;;
    *)
        python3 "$repo/Test/Native/check_exports.py" "$plugin"
        python3 "$repo/Test/Native/native_regression.py" "$plugin"
        python3 "$repo/Test/Native/profiler_smoke.py" "$plugin"
        python3 "$repo/Test/Native/run_managed.py" "${managed_modes[@]}" \
            --mono-version "${MONO_VERSION:-6.14.1}" --plugin "$plugin" --output "$build/managed"
        ;;
    esac
)

for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1 core-5.4.9 core-5.5.1 core-tp-5.5.1 minimal-5.3.6 minimal-5.4.9 minimal-5.5.1 bytecode-5.5.1}; do
    build="$out/$version"
    test_points=OFF
    case "$version" in
        core-tp-*) kind=core test_points=ON ;;
        core-*) kind=core ;;
        minimal-*) kind=minimal ;;
        bytecode-*) kind=bytecode ;;
        *) kind=legacy ;;
    esac
    lua="${version##*-}"
    rm -rf "$build/profiles"
    mkdir -p "$build/profiles"
    options=(-DCMAKE_C_COMPILER="$clang_bin" -DCMAKE_BUILD_TYPE=Debug -DLUA_VERSION="$lua" -DXLUA_COVERAGE=ON -DXLUA_APICHECK=ON)
    objects=()
    case "$kind" in
    core)
        options+=(-DXLUA_PRESET=core -DXLUA_EXPORTS=all)
        if [[ "$test_points" == ON ]]; then
            options+=(-DCMAKE_C_FLAGS=-DXLUA_LIMITS_TEST_POINTS)
        fi
        sources=("$b/xlua_core.c" "$b/xlua_core_limits.c" "$b/xlua_core.h"
                 "$b/xlua_core_sandbox.c" "$b/xlua_core_buffer.c" "$b/xlua_core_nobc.c")
        gate="$gate_core" ;;
    minimal)
        options+=(-DXLUA_PRESET=minimal -DXLUA_EXPORTS=all)
        sources=("$b/xlua_libs.c")
        gate="$gate_minimal" ;;
    bytecode)
        options+=(-DXLUA_BYTECODE_ONLY=ON)
        # The compiler of the checks' chunks: the same version with Lua's compiler, uninstrumented.
        cmake -S "$b" -B "$build/compiler" -DCMAKE_C_COMPILER="$clang_bin" -DCMAKE_BUILD_TYPE=Release -DLUA_VERSION="$lua"
        cmake --build "$build/compiler" --parallel 4
        # xlua.c is reported, not gated: the managed suite cannot run here (Test/README.md).
        sources=("$b/xlua_noparser.c" "$b/xlua_chunkc.c" "$b/xlua.c")
        objects=(-object "$build/xlua_chunkc")
        gate="$gate_bytecode" ;;
    *)
        sources=("$b/xlua_core.c" "$b/xlua_core_limits.c" "$b/xlua_core.h" "$b/xlua.c" "$b/i64lib.c" "$b/memory_leak_checker.c")
        gate="$gate_legacy" ;;
    esac
    cmake -S "$b" -B "$build" "${options[@]}"
    # %p keeps one raw profile per process (Python checks, the Mono runner and,
    # in the bytecode build, xlua_chunkc as the build runs it).
    export LLVM_PROFILE_FILE="$build/profiles/%p.profraw"
    cmake --build "$build" --parallel 4
    run_tests "$kind" "$lua" "$build"
    unset LLVM_PROFILE_FILE

    plugin="$build/libxlua.so"
    "$profdata" merge -sparse "$build"/profiles/*.profraw -o "$build/xlua.profdata"
    show=("$plugin" "${objects[@]}" -instr-profile="$build/xlua.profdata" "${cov_paths[@]}" "${sources[@]}")
    "$cov" report "${show[@]}" | tee "$build/report.txt"
    "$cov" report -show-functions "${show[@]}" > "$build/functions.txt"
    "$cov" show -format=html -output-dir="$build/html" "${show[@]}"
    # Per-branch counts; an untaken side shows as "True: 0" or "False: 0".
    "$cov" show -show-branches=count "${show[@]}" > "$build/branches.txt"
    # The gate reads llvm-cov's per-file summary (branch counts), not the report's columns.
    "$cov" export -summary-only "${show[@]}" > "$build/summary.json"
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
    echo "XLUA_COVERAGE_GATE: branch coverage below the gate (legacy: $gate_legacy; core: $gate_core;" \
         "minimal: $gate_minimal; bytecode: $gate_bytecode); see the FAIL lines above and branches.txt" >&2
    exit 1
fi
