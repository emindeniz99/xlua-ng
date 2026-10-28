#!/bin/bash
# The adversarial bridge target (Test/Fuzz/fuzz_bridge.c): builds libxlua for one
# preset and Lua version with CMake, as the preset builds it, plus AddressSanitizer,
# UndefinedBehaviorSanitizer, Lua's C API checks and libFuzzer's coverage, links the
# target against it and runs it for <seconds>.
#
# Linux with clang (the Test/Docker image). Usage:
#   Test/Fuzz/run_bridge.sh <seconds>
# Environment:
#   FUZZ_OUT      output directory (default: ./fuzz-out); builds, corpus, logs, crashes
#   LUA_VERSION   5.3.6, 5.4.9, 5.5.1 or luajit (default: 5.5.1); LuaJIT itself is
#                 built without sanitizers (with its API checks), as in run.sh
#   XLUA_PRESET   legacy, minimal or core (default: legacy); core needs Lua 5.4 or 5.5
#                 and fuzzes LuaScript's sandbox (no bridge)
#   FUZZ_TRICKS   1: also the known crashes of docs/research/crash-inventory.md that
#                 the target leaves out otherwise (fuzz_bridge.c, first comment)
#   CC            compiler (default: clang)
# The seed corpus in Test/Fuzz/corpus/bridge is copied, never written to. Exits
# non-zero on a crash, a sanitizer report or a hang; the reproducer is in
# $FUZZ_OUT/bridge-<preset>-<version>/. Rerun one with:
#   $FUZZ_OUT/bin/fuzz_bridge-<preset>-<version> <file>
set -euo pipefail
seconds="${1:?usage: run_bridge.sh <seconds>}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${FUZZ_OUT:-fuzz-out}" && cd "${FUZZ_OUT:-fuzz-out}" && pwd)"
version="${LUA_VERSION:-5.5.1}"
preset="${XLUA_PRESET:-legacy}"
cc="${CC:-clang}"
name="$preset-$version"
build="$out/build-$name"
mkdir -p "$out/bin" "$build"

cmake_args=(-DCMAKE_C_COMPILER="$cc" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DXLUA_PRESET="$preset"
            -DXLUA_EXPORTS=all -DXLUA_SANITIZE=address,undefined -DXLUA_APICHECK=ON
            -DCMAKE_C_FLAGS="-fsanitize=fuzzer-no-link")
flags=(-g -O1 -fno-omit-frame-pointer -fsanitize=fuzzer,address,undefined -fno-sanitize-recover=all
       -DLUA_USE_APICHECK)
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
mkdir -p "$work/corpus"
cp -n "$repo/Test/Fuzz/corpus/bridge"/* "$work/corpus/" 2>/dev/null || true
# Each operation of the driver takes a few bytes, so start from long random inputs
# (a fixed seed: the same seeds every run) and let libFuzzer use the whole length.
python3 - "$work/corpus" <<'EOF'
import os, random, sys
rng = random.Random(20261028)
for i in range(16):
    path = os.path.join(sys.argv[1], "random-%02d" % i)
    if not os.path.exists(path):
        open(path, "wb").write(bytes(rng.randrange(256) for _ in range(1024)))
EOF
args=(-max_total_time="$seconds" -timeout=20 -rss_limit_mb=2048 -max_len=2048 -len_control=0 -print_final_stats=1
      -artifact_prefix="$work/")
echo "fuzz_bridge ($preset, Lua $version, tricks ${FUZZ_TRICKS:-0}): ${seconds}s"
if FUZZ_BRIDGE_TRICKS="${FUZZ_TRICKS:-0}" ASAN_OPTIONS="detect_leaks=1:abort_on_error=1" \
    UBSAN_OPTIONS="print_stacktrace=1" "$bin" "${args[@]}" "$work/corpus" >"$work/fuzz.log" 2>&1; then
    grep -E '^stat::number_of_executed_units|^stat::peak_rss_mb|^stat::average_exec_per_sec' "$work/fuzz.log" || true
    echo "XLUA_FUZZ_PASS: bridge on $preset, Lua $version for ${seconds}s"
else
    echo "fuzz_bridge: crash or failure, see $work/fuzz.log"
    grep -E 'ERROR|runtime error|SUMMARY|fuzz_bridge:|Assertion|crash-|timeout-|oom-' "$work/fuzz.log" | head -20 || true
    exit 1
fi
