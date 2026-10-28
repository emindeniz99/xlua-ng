#!/bin/bash
# The adversarial bridge target (Test/Fuzz/fuzz_bridge.c): builds libxlua for one
# preset and Lua version with CMake, as the preset builds it, plus AddressSanitizer,
# UndefinedBehaviorSanitizer, Lua's C API checks and libFuzzer's coverage, links the
# target against it and runs it for <seconds>, or once over its seed corpus.
#
# Linux with clang (the Test/Docker image). Usage:
#   Test/Fuzz/run_bridge.sh <seconds>   fuzz for <seconds>
#   Test/Fuzz/run_bridge.sh replay      run once over the seed corpus (-runs=0): the
#                                       16 random seeds and Test/Fuzz/corpus/bridge
#   Test/Fuzz/run_bridge.sh build       build and prepare the seeds only (corpus_coverage.sh)
# Environment:
#   FUZZ_OUT      output directory (default: ./fuzz-out); builds, corpus, logs, crashes
#   LUA_VERSION   5.3.6, 5.4.9, 5.5.1 or luajit (default: 5.5.1); LuaJIT itself is
#                 built without sanitizers (with its API checks), as in run.sh
#   XLUA_PRESET   legacy, minimal or core (default: legacy); core needs Lua 5.4 or 5.5
#                 and fuzzes LuaScript's sandbox (no bridge)
#   FUZZ_TRICKS   1: also the known crashes of docs/research/crash-inventory.md that
#                 the target leaves out otherwise (fuzz_bridge.c, first comment)
#   FUZZ_JOBS     parallel libFuzzer processes when fuzzing (default 1): fork mode,
#                 -fork=N, which runs the starting corpus once and stops at the first
#                 crash, timeout or out-of-memory of any process
#   FUZZ_RSS_MB   -rss_limit_mb of each libFuzzer process (default 2048)
#   FUZZ_MERGE    1: after fuzzing, minimise the corpus with -merge=1 into a fresh
#                 directory that replaces it (the weekly job's cached corpus)
#   FUZZ_INSTRUMENT  sanitizers (default: ASan, UBSan and libFuzzer's coverage) or
#                 coverage (Clang source-based coverage of xLua's own files, as
#                 coverage.sh builds them, no sanitizers; corpus_coverage.sh)
#   CC            compiler (default: clang)
# The seed corpus in Test/Fuzz/corpus/bridge is copied, never written to. Exits
# non-zero on a crash, a sanitizer report or a hang; the reproducer is in
# $FUZZ_OUT/bridge-<preset>-<version>/. Rerun one with:
#   $FUZZ_OUT/bin/fuzz_bridge-<preset>-<version> <file>
set -euo pipefail
mode="${1:?usage: run_bridge.sh <seconds> | replay | build}"
case "$mode" in
    replay|build) ;;
    *[!0-9]*|'') echo "usage: run_bridge.sh <seconds> | replay | build" >&2; exit 2 ;;
esac
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${FUZZ_OUT:-fuzz-out}" && cd "${FUZZ_OUT:-fuzz-out}" && pwd)"
version="${LUA_VERSION:-5.5.1}"
preset="${XLUA_PRESET:-legacy}"
cc="${CC:-clang}"
jobs="${FUZZ_JOBS:-1}"
rss_mb="${FUZZ_RSS_MB:-2048}"
name="$preset-$version"
build="$out/build-$name"
mkdir -p "$out/bin" "$build"

cmake_args=(-DCMAKE_C_COMPILER="$cc" -DXLUA_PRESET="$preset" -DXLUA_EXPORTS=all -DXLUA_APICHECK=ON
            -DCMAKE_C_FLAGS="-fsanitize=fuzzer-no-link")
case "${FUZZ_INSTRUMENT:-sanitizers}" in
    sanitizers)
        cmake_args+=(-DCMAKE_BUILD_TYPE=RelWithDebInfo -DXLUA_SANITIZE=address,undefined -DXLUA_COVERAGE=OFF)
        flags=(-g -O1 -fno-omit-frame-pointer -fsanitize=fuzzer,address,undefined -fno-sanitize-recover=all
               -DLUA_USE_APICHECK) ;;
    coverage)
        # coverage.sh's build: Debug, XLUA_COVERAGE (xLua's own files only), the API checks.
        cmake_args+=(-DCMAKE_BUILD_TYPE=Debug -DXLUA_COVERAGE=ON -DXLUA_SANITIZE=)
        flags=(-g -O1 -fno-omit-frame-pointer -fsanitize=fuzzer -fprofile-instr-generate -DLUA_USE_APICHECK) ;;
    *) echo "FUZZ_INSTRUMENT must be sanitizers or coverage" >&2; exit 2 ;;
esac
if [[ "$version" == luajit ]]; then
    [[ "$preset" != core ]] || { echo "the core preset needs Lua 5.4 or 5.5" >&2; exit 2; }
    # Built in a copy to keep the source tree clean.
    lj="$out/luajit-src"
    if [[ ! -f "$lj/src/libluajit.a" ]]; then
        rm -rf "$lj"
        cp -a "$repo/build/luajit-2.1" "$lj"
        make -C "$lj" BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK" >"$out/luajit-make.log" 2>&1
    fi
    cmake_args+=(-DUSING_LUAJIT=ON -DLUAJIT_STATIC_LIB="$lj/src/libluajit.a")
    flags+=(-DUSING_LUAJIT -I"$repo/build/luajit-2.1/src")
else
    lua_src="$repo/build/lua-$version/src"
    test -d "$lua_src" || { echo "unknown LUA_VERSION $version" >&2; exit 2; }
    cmake_args+=(-DLUA_VERSION="$version")
    flags+=(-I"$build" -I"$lua_src")
    # Lua 5.5 ships a plain luaconf.h; CMake passes LUA_IDSIZE as a definition there.
    [[ -f "$lua_src/luaconf.h.in" ]] || flags+=(-DLUA_IDSIZE=120)
fi
case "$preset" in
    core) flags+=(-DFUZZ_BRIDGE_CORE -DXLUA_SANDBOX) ;;
    legacy) flags+=(-DFUZZ_SNAPSHOT) ;; # XLUA_MEMORY_SNAPSHOT is on for Linux in legacy only
esac
flags+=(-I"$repo/build" -I"$repo/Test/Fuzz")

cmake -S "$repo/build" -B "$build" "${cmake_args[@]}" >"$build/cmake.log" 2>&1 \
    || { tail -20 "$build/cmake.log"; exit 1; }
cmake --build "$build" --parallel 4 >"$build/build.log" 2>&1 || { tail -40 "$build/build.log"; exit 1; }
bin="$out/bin/fuzz_bridge-$name"
"$cc" "${flags[@]}" "$repo/Test/Fuzz/fuzz_bridge.c" -L"$build" -lxlua -Wl,-rpath,"$build" -lm -o "$bin"

work="$out/bridge-$name"
# Replay and build start from the seeds alone; fuzzing adds them to the corpus it
# keeps (the weekly job restores that from the Actions cache).
corpus="$work/corpus"
if [[ "$mode" == replay || "$mode" == build ]]; then
    corpus="$work/seeds"
    rm -rf "$corpus"
fi
mkdir -p "$corpus"
cp -n "$repo/Test/Fuzz/corpus/bridge"/* "$corpus/" 2>/dev/null || true
# Each operation of the driver takes a few bytes, so start from long random inputs
# (a fixed seed: the same seeds every run) and let libFuzzer use the whole length.
python3 - "$corpus" <<'EOF'
import os, random, sys
rng = random.Random(20261028)
for i in range(16):
    path = os.path.join(sys.argv[1], "random-%02d" % i)
    if not os.path.exists(path):
        open(path, "wb").write(bytes(rng.randrange(256) for _ in range(1024)))
EOF
if [[ "$mode" == build ]]; then
    echo "XLUA_FUZZ_BUILT: bridge on $preset, Lua $version: $bin"
    exit 0
fi
args=(-timeout=20 -rss_limit_mb="$rss_mb" -max_len=2048 -len_control=0 -print_final_stats=1
      -artifact_prefix="$work/")
if [[ "$mode" == replay ]]; then
    args+=(-runs=0)
    what="replay of $(find "$corpus" -type f | wc -l) seeds"
else
    args+=(-max_total_time="$mode")
    # Fork mode: the parent runs the corpus once and hands out parts of it to N
    # child processes in turn; it ignores timeouts and out-of-memory by default.
    if [[ "$jobs" -gt 1 ]]; then args+=(-fork="$jobs" -ignore_crashes=0 -ignore_timeouts=0 -ignore_ooms=0); fi
    what="${mode}s, $jobs job(s)"
fi
echo "fuzz_bridge ($preset, Lua $version, tricks ${FUZZ_TRICKS:-0}): $what"
status=0
if (cd "$work" && FUZZ_BRIDGE_TRICKS="${FUZZ_TRICKS:-0}" ASAN_OPTIONS="detect_leaks=1:abort_on_error=1" \
    UBSAN_OPTIONS="print_stacktrace=1" "$bin" "${args[@]}" "$corpus" >"$work/fuzz.log" 2>&1); then
    # fork mode's parent prints no stat:: lines: its last progress line instead
    grep -E '^stat::number_of_executed_units|^stat::peak_rss_mb|^stat::average_exec_per_sec' "$work/fuzz.log" \
        || grep -E '^#[0-9]+: cov:' "$work/fuzz.log" | tail -1 || true
    echo "XLUA_FUZZ_PASS: bridge on $preset, Lua $version, $what"
else
    echo "fuzz_bridge: crash or failure, see $work/fuzz.log"
    grep -E 'ERROR|runtime error|SUMMARY|fuzz_bridge:|Assertion|crash-|timeout-|oom-' "$work/fuzz.log" | head -20 || true
    status=1
fi
if [[ "${FUZZ_MERGE:-0}" == 1 && "$mode" != replay ]]; then
    # Keeps only inputs that add coverage, so the cached corpus and its replay stay small.
    rm -rf "$work/corpus.merged"
    mkdir "$work/corpus.merged"
    if (cd "$work" && FUZZ_BRIDGE_TRICKS="${FUZZ_TRICKS:-0}" ASAN_OPTIONS="detect_leaks=1:abort_on_error=1" \
        "$bin" -merge=1 -timeout=20 -rss_limit_mb="$rss_mb" -max_len=2048 -artifact_prefix="$work/merge-" \
        "$work/corpus.merged" "$corpus" >"$work/merge.log" 2>&1); then
        echo "fuzz_bridge: merged $(find "$corpus" -type f | wc -l) inputs into $(find "$work/corpus.merged" -type f | wc -l)"
        rm -rf "$corpus"
        mv "$work/corpus.merged" "$corpus"
    else
        echo "fuzz_bridge: merge failed, the corpus is kept as it is (see $work/merge.log)"
        status=1
    fi
fi
exit "$status"
