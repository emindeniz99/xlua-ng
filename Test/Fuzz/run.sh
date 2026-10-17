#!/bin/bash
# Build the libFuzzer targets in Test/Fuzz against xLua's C files and one
# vendored Lua runtime, then run each target for <seconds>.
#
# Linux with clang (libFuzzer, ASan, UBSan runtimes). Usage:
#   Test/Fuzz/run.sh <seconds>
# Environment:
#   FUZZ_OUT     output directory (default: ./fuzz-out); corpora, logs, crashes
#   LUA_VERSION  5.3.6, 5.4.9, 5.5.1, luajit or luajit-uint (default: 5.5.1);
#                luajit-uint is LuaJIT with UINT_ESPECIALLY. LuaJIT runs the
#                i64 target only, and LuaJIT itself is built without sanitizers
#                (but with its API checks).
#   TARGETS      subset of: lua i64 struct path (default: all four; i64 on LuaJIT)
#   CC           compiler (default: clang)
# The seed corpus in Test/Fuzz/corpus is copied, never written to. Exits
# non-zero if any target reports a crash; reproducers are in
# $FUZZ_OUT/<target>/crash-*. Rerun one with: $FUZZ_OUT/bin/fuzz_<target> <file>
set -euo pipefail
seconds="${1:?usage: run.sh <seconds>}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${FUZZ_OUT:-fuzz-out}" && cd "${FUZZ_OUT:-fuzz-out}" && pwd)"
version="${LUA_VERSION:-5.5.1}"
cc="${CC:-clang}"
targets="${TARGETS:-lua i64 struct path}"

# Lua API checks turn C API misuse in xlua_core.c and xlua.c into assertion failures.
cflags=(-g -O1 -fno-omit-frame-pointer -DLUA_USE_APICHECK
        -fsanitize=fuzzer-no-link,address,undefined -fno-sanitize-recover=all)
obj="$out/obj-$version"
mkdir -p "$obj" "$out/bin"
libs=(-lm)
if [[ "$version" == luajit || "$version" == luajit-uint ]]; then
    targets="${TARGETS:-i64}"
    [[ "$targets" == i64 ]] || { echo "only the i64 target runs on LuaJIT" >&2; exit 2; }
    # Built in a copy to keep the source tree clean. xlua.c needs LuaJIT's
    # internal headers; the memory-leak checker is not built for LuaJIT.
    rm -rf "$obj/luajit"
    cp -a "$repo/build/luajit-2.1" "$obj/luajit"
    # LUA_USE_APICHECK compiles LuaJIT's own C API checks into libluajit.a.
    make -C "$obj/luajit" BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK" >"$obj/luajit-make.log" 2>&1
    lua_src="$obj/luajit/src"
    cflags+=(-DUSING_LUAJIT)
    if [[ "$version" == luajit-uint ]]; then cflags+=(-DUINT_ESPECIALLY); fi
    sources=("$repo/build/xlua_core.c" "$repo/build/xlua.c" "$repo/build/i64lib.c")
    libs=("$lua_src/libluajit.a" -lm -ldl)
else
    lua_src="$repo/build/lua-$version/src"
    test -d "$lua_src" || { echo "unknown LUA_VERSION $version" >&2; exit 2; }
    # Lua 5.3 and 5.4 generate luaconf.h from a CMake template; Lua 5.5.1 ships a
    # plain luaconf.h with the same LUA_IDSIZE default.
    if [[ -f "$lua_src/luaconf.h.in" ]]; then
        sed 's/^#cmakedefine LUA_IDSIZE.*/#define LUA_IDSIZE 120/' "$lua_src/luaconf.h.in" >"$obj/luaconf.h"
    fi
    sources=("$lua_src"/*.c "$repo/build/xlua_core.c" "$repo/build/xlua.c" "$repo/build/i64lib.c" "$repo/build/memory_leak_checker.c")
fi
objects=()
for src in "${sources[@]}"; do
    case "$(basename "$src")" in lua.c|luac.c) continue ;; esac
    o="$obj/$(basename "$src" .c).o"
    "$cc" "${cflags[@]}" -I"$obj" -I"$lua_src" -I"$repo/build" -c "$src" -o "$o"
    objects+=("$o")
done

status=0
for target in $targets; do
    bin="$out/bin/fuzz_$target"
    "$cc" "${cflags[@]}" -fsanitize=fuzzer -I"$obj" -I"$lua_src" -I"$repo/build" \
        "$repo/Test/Fuzz/fuzz_$target.c" "${objects[@]}" "${libs[@]}" -o "$bin"
    work="$out/$target"
    mkdir -p "$work/corpus"
    cp -n "$repo/Test/Fuzz/corpus/$target"/* "$work/corpus/" 2>/dev/null || true
    args=(-max_total_time="$seconds" -timeout=10 -rss_limit_mb=2048 -print_final_stats=1
          -artifact_prefix="$work/")
    if [[ "$target" == lua ]]; then args+=(-dict="$repo/Test/Fuzz/lua.dict" -max_len=4096); fi
    echo "fuzz_$target (Lua $version): ${seconds}s"
    if ASAN_OPTIONS="detect_leaks=1:abort_on_error=1" UBSAN_OPTIONS="print_stacktrace=1" \
        "$bin" "${args[@]}" "$work/corpus" >"$work/fuzz.log" 2>&1; then
        grep -E '^stat::number_of_executed_units|^stat::peak_rss_mb' "$work/fuzz.log" || true
    else
        echo "fuzz_$target: crash or failure, see $work/fuzz.log"
        grep -E 'ERROR|runtime error|SUMMARY|crash-|timeout-|oom-' "$work/fuzz.log" | head -20 || true
        status=1
    fi
done
if [[ "$status" == 0 ]]; then
    echo "XLUA_FUZZ_PASS: $targets on Lua $version for ${seconds}s each"
fi
exit "$status"
