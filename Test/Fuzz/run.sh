#!/bin/bash
# Build the libFuzzer targets in Test/Fuzz against xLua's C files and one
# vendored Lua runtime, then run each target for <seconds>, or once over its
# seed corpus.
#
# Linux with clang (libFuzzer, ASan, UBSan runtimes). Usage:
#   Test/Fuzz/run.sh <seconds>   fuzz each target for <seconds>
#   Test/Fuzz/run.sh replay      run each target once over its seed corpus
#                                (-runs=0): every seed and every fixed crash's
#                                reproducer in Test/Fuzz/corpus/<target>
#   Test/Fuzz/run.sh build       build the targets and prepare their seed
#                                corpora only (corpus_coverage.sh)
# Environment:
#   FUZZ_OUT     output directory (default: ./fuzz-out); corpora, logs, crashes
#   LUA_VERSION  5.3.6, 5.4.9, 5.5.1, luajit or luajit-uint (default: 5.5.1);
#                luajit-uint is LuaJIT with UINT_ESPECIALLY. LuaJIT runs the
#                i64 target only, and LuaJIT itself is built without sanitizers
#                (but with its API checks).
#   TARGETS      subset of: lua i64 struct path core (default: all five on Lua 5.4
#                and 5.5, the first four on 5.3, i64 on LuaJIT). core is LuaScript's
#                sandbox (fuzz_core.c) on the core preset's sources: no bridge, no
#                io, os, debug or package, the bytecode stubs, the C budget hooks.
#   FUZZ_JOBS    parallel libFuzzer processes per target when fuzzing (default 1):
#                fork mode, -fork=N, which runs the starting corpus once and stops at
#                the first crash, timeout or out-of-memory of any process
#   FUZZ_RSS_MB  -rss_limit_mb of each libFuzzer process (default 2048)
#   FUZZ_MERGE   1: after fuzzing, minimise each corpus with -merge=1 into a fresh
#                directory that replaces it (the weekly job's cached corpus)
#   FUZZ_INSTRUMENT  sanitizers (default: ASan, UBSan and libFuzzer's coverage) or
#                coverage (Clang source-based coverage of xLua's own files, no
#                sanitizers; corpus_coverage.sh)
#   CC           compiler (default: clang)
# The seed corpus in Test/Fuzz/corpus is copied, never written to. Exits
# non-zero if any target reports a crash; reproducers are in
# $FUZZ_OUT/<target>/crash-*. Rerun one with: $FUZZ_OUT/bin/fuzz_<target> <file>
set -euo pipefail
mode="${1:?usage: run.sh <seconds> | replay | build}"
case "$mode" in
    replay|build) ;;
    *[!0-9]*|'') echo "usage: run.sh <seconds> | replay | build" >&2; exit 2 ;;
esac
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mkdir -p "${FUZZ_OUT:-fuzz-out}" && cd "${FUZZ_OUT:-fuzz-out}" && pwd)"
version="${LUA_VERSION:-5.5.1}"
cc="${CC:-clang}"
jobs="${FUZZ_JOBS:-1}"
rss_mb="${FUZZ_RSS_MB:-2048}"
if [[ "$version" == 5.4.* || "$version" == 5.5.* ]]; then
    targets="${TARGETS:-lua i64 struct path core}"
else
    targets="${TARGETS:-lua i64 struct path}"
fi

# Lua API checks turn C API misuse in xlua_core.c and xlua.c into assertion failures.
cflags=(-g -O1 -fno-omit-frame-pointer -DLUA_USE_APICHECK)
# Only xLua's own files get the source-based coverage flags, as in coverage.sh.
own_cflags=()
case "${FUZZ_INSTRUMENT:-sanitizers}" in
    sanitizers) cflags+=(-fsanitize=fuzzer-no-link,address,undefined -fno-sanitize-recover=all) ;;
    coverage) cflags+=(-fsanitize=fuzzer-no-link)
              own_cflags=(-fprofile-instr-generate -fcoverage-mapping) ;;
    *) echo "FUZZ_INSTRUMENT must be sanitizers or coverage" >&2; exit 2 ;;
esac
own_flags_for() {
    case "$(basename "$1")" in
        xlua*.c|i64lib.c|memory_leak_checker.c) echo "${own_cflags[@]}" ;;
    esac
}
obj="$out/obj-$version"
mkdir -p "$obj" "$out/bin"
libs=(-lm)
if [[ "$version" == luajit || "$version" == luajit-uint ]]; then
    targets="${TARGETS:-i64}"
    [[ "$targets" == i64 ]] || { echo "only the i64 target runs on LuaJIT" >&2; exit 2; }
    # Built in a copy to keep the source tree clean. xlua.c needs LuaJIT's
    # internal headers. Only the i64 target runs, so memory_leak_checker.c is not needed.
    rm -rf "$obj/luajit"
    cp -a "$repo/build/luajit-2.1" "$obj/luajit"
    # LUA_USE_APICHECK compiles LuaJIT's own C API checks into libluajit.a.
    make -C "$obj/luajit" BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK" >"$obj/luajit-make.log" 2>&1
    lua_src="$obj/luajit/src"
    cflags+=(-DUSING_LUAJIT)
    if [[ "$version" == luajit-uint ]]; then cflags+=(-DUINT_ESPECIALLY); fi
    sources=("$repo/build/xlua_core.c" "$repo/build/xlua_core_limits.c" "$repo/build/xlua.c" "$repo/build/i64lib.c")
    libs=("$lua_src/libluajit.a" -lm -ldl)
else
    lua_src="$repo/build/lua-$version/src"
    test -d "$lua_src" || { echo "unknown LUA_VERSION $version" >&2; exit 2; }
    # Lua 5.3 and 5.4 generate luaconf.h from a CMake template; Lua 5.5.1 ships a
    # plain luaconf.h with the same LUA_IDSIZE default.
    if [[ -f "$lua_src/luaconf.h.in" ]]; then
        sed 's/^#cmakedefine LUA_IDSIZE.*/#define LUA_IDSIZE 120/' "$lua_src/luaconf.h.in" >"$obj/luaconf.h"
    fi
    sources=("$lua_src"/*.c "$repo/build/xlua_core.c" "$repo/build/xlua_core_limits.c" "$repo/build/xlua.c" "$repo/build/i64lib.c" "$repo/build/memory_leak_checker.c")
fi
link_cflags=("${cflags[@]}" -fsanitize=fuzzer)
[[ ${#own_cflags[@]} == 0 ]] || link_cflags+=(-fprofile-instr-generate)
objects=()
for src in "${sources[@]}"; do
    case "$(basename "$src")" in lua.c|luac.c) continue ;; esac
    o="$obj/$(basename "$src" .c).o"
    # shellcheck disable=SC2046 # one flag per word
    "$cc" "${cflags[@]}" $(own_flags_for "$src") -I"$obj" -I"$lua_src" -I"$repo/build" -c "$src" -o "$o"
    objects+=("$o")
done

# The core target links what XLUA_PRESET=core builds (build/CMakeLists.txt): Lua
# without lundump.c, ldump.c, linit.c and the io, os, debug and package libraries,
# all with the hooks of build/xlua_luahooks.h (the C budget), and the core files.
core_objects=()
if [[ " $targets " == *" core "* ]]; then
    [[ "$version" == 5.4.* || "$version" == 5.5.* ]] || { echo "the core target needs Lua 5.4 or 5.5" >&2; exit 2; }
    core_obj="$out/obj-core-$version"
    mkdir -p "$core_obj"
    if [[ -f "$obj/luaconf.h" ]]; then cp "$obj/luaconf.h" "$core_obj/luaconf.h"; fi
    core_libs=(-DXLUA_LIB_COROUTINE -DXLUA_LIB_TABLE -DXLUA_LIB_STRING -DXLUA_LIB_MATH -DXLUA_LIB_UTF8)
    for src in "$lua_src"/*.c "$repo/build/xlua_core.c" "$repo/build/xlua_core_limits.c" \
               "$repo/build/xlua_core_sandbox.c" "$repo/build/xlua_core_buffer.c" "$repo/build/xlua_core_nobc.c"; do
        case "$(basename "$src")" in
            lua.c|luac.c|lundump.c|ldump.c|linit.c|liolib.c|loslib.c|ldblib.c|loadlib.c) continue ;;
        esac
        o="$core_obj/$(basename "$src" .c).o"
        # shellcheck disable=SC2046 # one flag per word
        "$cc" "${cflags[@]}" $(own_flags_for "$src") -DXLUA_LUA_HOOKS -DXLUA_CORE_ONLY -DXLUA_SANDBOX -DXLUA_BUFFER "${core_libs[@]}" \
            -I"$core_obj" -I"$lua_src" -I"$repo/build" -c "$src" -o "$o"
        core_objects+=("$o")
    done
fi

status=0
for target in $targets; do
    bin="$out/bin/fuzz_$target"
    if [[ "$target" == core ]]; then
        "$cc" "${link_cflags[@]}" -DXLUA_LUA_HOOKS -DXLUA_BUFFER -I"$core_obj" -I"$lua_src" -I"$repo/build" \
            "$repo/Test/Fuzz/fuzz_core.c" "${core_objects[@]}" -lm -o "$bin"
    else
        "$cc" "${link_cflags[@]}" -I"$obj" -I"$lua_src" -I"$repo/build" \
            "$repo/Test/Fuzz/fuzz_$target.c" "${objects[@]}" "${libs[@]}" -o "$bin"
    fi
    work="$out/$target"
    # Replay and build start from the committed seeds alone; fuzzing adds them to
    # the corpus it keeps (the weekly job restores that from the Actions cache).
    corpus="$work/corpus"
    if [[ "$mode" == replay || "$mode" == build ]]; then
        corpus="$work/seeds"
        rm -rf "$corpus"
    fi
    mkdir -p "$corpus"
    cp -n "$repo/Test/Fuzz/corpus/$target"/* "$corpus/" 2>/dev/null || true
    # fuzz_lua.c's corpus is valid input for the core target too (its first byte picks the options).
    if [[ "$target" == core ]]; then cp -n "$repo/Test/Fuzz/corpus/lua"/* "$corpus/" 2>/dev/null || true; fi
    [[ "$mode" != build ]] || continue

    asan="detect_leaks=1:abort_on_error=1"
    args=(-timeout=10 -rss_limit_mb="$rss_mb" -print_final_stats=1 -artifact_prefix="$work/")
    if [[ "$target" == lua || "$target" == core ]]; then args+=(-dict="$repo/Test/Fuzz/lua.dict" -max_len=4096); fi
    if [[ "$mode" == replay ]]; then
        args+=(-runs=0)
        echo "fuzz_$target (Lua $version): replay of $(find "$corpus" -type f | wc -l) seeds"
    else
        args+=(-max_total_time="$mode")
        # Fork mode: the parent runs the corpus once and hands out parts of it to N
        # child processes in turn; it ignores timeouts and out-of-memory by default.
        # The parent leaks a few bytes of its own (FuzzWithFork), which LeakSanitizer
        # reported at its exit as a crash; the children still check each input for leaks.
        if [[ "$jobs" -gt 1 ]]; then
            args+=(-fork="$jobs" -ignore_crashes=0 -ignore_timeouts=0 -ignore_ooms=0)
            asan+=":leak_check_at_exit=0"
        fi
        echo "fuzz_$target (Lua $version): ${mode}s, $jobs job(s)"
    fi
    if (cd "$work" && ASAN_OPTIONS="$asan" UBSAN_OPTIONS="print_stacktrace=1" \
        "$bin" "${args[@]}" "$corpus" >"$work/fuzz.log" 2>&1); then
        # fork mode's parent prints no stat:: lines: its last progress line instead
        grep -E '^stat::number_of_executed_units|^stat::peak_rss_mb' "$work/fuzz.log" \
            || grep -E '^#[0-9]+: cov:' "$work/fuzz.log" | tail -1 || true
    else
        echo "fuzz_$target: crash or failure, see $work/fuzz.log"
        grep -E 'ERROR|runtime error|SUMMARY|crash-|timeout-|oom-' "$work/fuzz.log" | head -20 || true
        status=1
    fi
    if [[ "${FUZZ_MERGE:-0}" == 1 && "$mode" != replay ]]; then
        # Keeps only inputs that add coverage, so the cached corpus and its replay stay small.
        rm -rf "$work/corpus.merged"
        mkdir "$work/corpus.merged"
        merge_args=(-merge=1 -timeout=10 -rss_limit_mb="$rss_mb" -artifact_prefix="$work/merge-")
        if [[ "$target" == lua || "$target" == core ]]; then merge_args+=(-max_len=4096); fi
        if (cd "$work" && ASAN_OPTIONS="detect_leaks=1:abort_on_error=1" \
            "$bin" "${merge_args[@]}" "$work/corpus.merged" "$corpus" >"$work/merge.log" 2>&1); then
            echo "fuzz_$target: merged $(find "$corpus" -type f | wc -l) inputs into $(find "$work/corpus.merged" -type f | wc -l)"
            rm -rf "$corpus"
            mv "$work/corpus.merged" "$corpus"
        else
            echo "fuzz_$target: merge failed, the corpus is kept as it is (see $work/merge.log)"
            status=1
        fi
    fi
done
if [[ "$mode" == build ]]; then
    echo "XLUA_FUZZ_BUILT: $targets on Lua $version in $out/bin"
elif [[ "$status" == 0 && "$mode" == replay ]]; then
    echo "XLUA_FUZZ_PASS: $targets on Lua $version, replay of the seed corpus"
elif [[ "$status" == 0 ]]; then
    echo "XLUA_FUZZ_PASS: $targets on Lua $version for ${mode}s each"
fi
exit "$status"
