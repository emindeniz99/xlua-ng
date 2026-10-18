#!/bin/bash
# Build libxlua with AddressSanitizer, UndefinedBehaviorSanitizer and Lua's C API
# checks (XLUA_APICHECK, LUA_USE_APICHECK), then run the export check, native
# regression checks, profiler smoke test (not on LuaJIT) and the managed suite
# (reflection, and with a .NET SDK on PATH or through mise also the source
# generator's plain and GEN_CODE_MINIMIZE bindings, with the memory diagnostics
# compiled in) against it. The container image (Test/Docker) has no .NET SDK, so
# there the suite runs through reflection only, and the script says so.
# Linux only (GCC or Clang). Usage: Test/Native/sanitize.sh <empty output dir>
# VERSIONS selects runtimes (default: 5.3.6 5.4.9 5.5.1 luajit). luajit-uint is
# LuaJIT built with -DUINT_ESPECIALLY=ON (ulong as a separate unsigned kind).
# MONO_VERSION is passed to run_managed.py (default 6.14.1; "any" skips the check).
# XLUA_WERROR=ON in the environment turns warnings in xLua's C files into errors.
# XLUA_LUASOCKET=OFF builds without LuaSocket, XLUA_MIME=OFF without mime.c; the
# checks then expect require "socket" (or "mime") to fail with "module not found".
# XLUA_LEAK_CHECKER=OFF, XLUA_UINT64=OFF and XLUA_LIBS=<list> (build/README.md) reach
# CMake through the environment, and the checks expect what they select. LuaJIT
# always builds the uint64 table and never the leak checker. The managed suite's
# harness needs os, string, table and math in XLUA_LIBS, and the checks find
# LuaJIT by its jit library (Lua 5.x builds ignore the name jit).
# XLUA_OS_FULL=OFF, XLUA_LOADLIB=OFF and XLUA_DOFILE=OFF work the same way.
# XLUA_PRESET=minimal builds with the minimal preset's native defaults
# (docs/design/presets.md), expects what Tools/presets.py says it contains, and
# compiles the managed suite with XLUA_PRESET_MINIMAL. Options set in the
# environment still win over the preset.
# core-5.4.9 and core-5.5.1 in VERSIONS build XLUA_PRESET=core (LuaScript's
# sandbox without the bridge) twice: with every export, for check_exports.py
# --preset core, native_regression.py and core_smoke.c, and with the managed
# export list, for the exact export check and core_smoke.c again. The bridge's
# checks and the managed suite do not apply there. core-trusted-5.4.9 and
# core-trusted-5.5.1 do the same with -DXLUA_TRUSTED_BYTECODE=ON.
# bytecode-5.3.6, bytecode-5.4.9 and bytecode-5.5.1 build -DXLUA_BYTECODE_ONLY=ON
# (no Lua compiler) and an uninstrumented library of the same version that
# compiles the chunks of native_regression.py's bytecode-only checks
# (XLUA_COMPILER_LIB). The managed suite and the profiler test compile text and
# do not apply; Test/Native/bytecode_luaenv.py runs LuaEnv on the library instead
# (its precompiled built-in chunks, the CS.* bridge, xlua.util, perf.profiler) in
# its four modes, with scripts and generated code from the uninstrumented library.
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

preset="${XLUA_PRESET:-legacy}"
managed_defines=(--define XLUA_MEMORY_DIAGNOSTICS)
# The source generator's passes need the .NET SDK's csc (run_managed.py --source-gen).
managed_modes=()
bytecode_modes=()
if command -v dotnet >/dev/null 2>&1 || mise which dotnet >/dev/null 2>&1; then
    managed_modes=(--source-gen --source-gen-minimize)
else
    bytecode_modes=(--mode reflection --mode locked)
    echo "XLUA_SANITIZE_NOTE: no .NET SDK, so the managed suite runs through reflection only (no generated bindings)"
fi
if [[ "$preset" != legacy ]]; then
    # CMake reads XLUA_PRESET and the options from the environment; the expectations
    # follow the preset, and an option set here changes them as for legacy below.
    eval "$(python3 "$repo/Tools/presets.py" --expect-env "$preset")"
    # The native checks call Lua API functions outside the managed list, so the
    # library exports everything here; check_android_elf.py --managed-exports
    # checks the preset's export list.
    export XLUA_EXPORTS="${XLUA_EXPORTS:-all}"
    managed_defines+=(--define "XLUA_PRESET_${preset^^}")
    module_flags=()
    luasocket="${XLUA_LUASOCKET:-OFF}" mime="${XLUA_MIME:-OFF}"
    if [[ -n "${XLUA_LIBS:-}" ]]; then export XLUA_EXPECT_LIBS="$XLUA_LIBS"; fi
else
    module_flags=(-DXLUA_LUASOCKET="${XLUA_LUASOCKET:-ON}" -DXLUA_MIME="${XLUA_MIME:-ON}")
    luasocket="${XLUA_LUASOCKET:-ON}" mime="${XLUA_MIME:-ON}"
    export XLUA_EXPECT_LIBS="${XLUA_LIBS:-all}"
fi
# XLUA_OS_FULL, XLUA_LOADLIB and XLUA_DOFILE from the environment win over the preset.
for part in OS_FULL LOADLIB DOFILE; do
    option="XLUA_$part"
    if [[ -n "${!option:-}" ]]; then
        export "XLUA_EXPECT_$part=${!option}"
    elif [[ "$preset" == legacy ]]; then
        export "XLUA_EXPECT_$part=ON"
    fi
done
if [[ "$luasocket" == OFF ]]; then
    export XLUA_EXPECT_NATIVE_MODULES=""
elif [[ "$mime" == OFF ]]; then
    export XLUA_EXPECT_NATIVE_MODULES="socket"
else
    export XLUA_EXPECT_NATIVE_MODULES="socket,mime"
fi

# XLUA_PRESET=core for one Lua version (core-<version> in VERSIONS). A subshell:
# the expectations of the bridge builds stay as they are.
sanitize_core() (
    local version="${1#core-}" exports build smoke trusted=OFF
    if [[ "$version" == trusted-* ]]; then
        version="${version#trusted-}"
        trusted=ON
    fi
    export XLUA_EXPECT_LIBS="${XLUA_LIBS:-coroutine,table,string,math,utf8}"
    export XLUA_EXPECT_TRUSTED_BYTECODE="$trusted" XLUA_EXPECT_BUFFER=ON
    for exports in all managed; do
        build="$out/$1-$exports"
        env -u XLUA_PRESET -u XLUA_LUASOCKET -u XLUA_MIME -u XLUA_LEAK_CHECKER -u XLUA_UINT64 \
            -u XLUA_MEMORY_SNAPSHOT -u XLUA_EXPORTS -u XLUA_LUASCRIPT -u XLUA_OS_FULL -u XLUA_LOADLIB -u XLUA_DOFILE \
            -u XLUA_TRUSTED_BYTECODE -u XLUA_BUFFER \
            cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION="$version" \
            -DXLUA_PRESET=core -DXLUA_EXPORTS="$exports" -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON \
            -DXLUA_TRUSTED_BYTECODE="$trusted"
        cmake --build "$build" --parallel 4
        plugin="$build/libxlua.so"
        LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/check_exports.py" --preset core "$plugin"
        if [[ "$exports" == all ]]; then
            LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/native_regression.py" "$plugin"
        fi
        smoke="$build/core_smoke"
        "$cc" -std=c17 -Wall -Wextra -Werror -g -fsanitize="$sanitizers" -fno-sanitize-recover=all \
            "$repo/Test/Native/core_smoke.c" -L"$build" -lxlua -Wl,-rpath,"$build" -o "$smoke"
        timeout 300 "$smoke" "$repo/Test/Native/core_sandbox_cases.txt"
    done
    echo "XLUA_SANITIZE_PASS: $1 (core)"
)

# -DXLUA_BYTECODE_ONLY=ON for one Lua version (bytecode-<version> in VERSIONS).
sanitize_bytecode_only() (
    local version="${1#bytecode-}" build="$out/$1" compiler="$out/$1-compiler"
    cmake -S "$repo/build" -B "$compiler" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION="$version" "${module_flags[@]}"
    cmake --build "$compiler" --parallel 4
    cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION="$version" \
        -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON -DXLUA_BYTECODE_ONLY=ON "${module_flags[@]}"
    cmake --build "$build" --parallel 4
    export XLUA_EXPECT_BYTECODE_ONLY=ON XLUA_COMPILER_LIB="$compiler/libxlua.so"
    # The precompiled modules of the preset (CMake XLUA_BYTECODE_UTIL ...), unless set already.
    if [[ -z "${XLUA_EXPECT_BYTECODE_MODULES+set}" ]]; then
        if [[ "$preset" == minimal ]]; then
            export XLUA_EXPECT_BYTECODE_MODULES=""
        else
            export XLUA_EXPECT_BYTECODE_MODULES="util,profiler,memory"
        fi
    fi
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/check_exports.py" "$build/libxlua.so"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/native_regression.py" "$build/libxlua.so"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/bytecode_luaenv.py" --plugin "$build/libxlua.so" \
        --compiler-plugin "$compiler/libxlua.so" --output "$build/luaenv" "${bytecode_modes[@]}"
    echo "XLUA_SANITIZE_PASS: $1 ($preset)"
)

for version in ${VERSIONS:-5.3.6 5.4.9 5.5.1 luajit}; do
    if [[ "$version" == core-* ]]; then
        sanitize_core "$version"
        continue
    fi
    if [[ "$version" == bytecode-* ]]; then
        sanitize_bytecode_only "$version"
        continue
    fi
    build="$out/$version"
    option_flags=()
    if [[ "$version" == luajit* ]]; then
        option_flags=(-DXLUA_UINT64=ON)
        export XLUA_EXPECT_UINT64=ON
        unset XLUA_EXPECT_LEAK_CHECKER
    else
        export XLUA_EXPECT_UINT64="${XLUA_UINT64:-ON}"
        if [[ "$preset" == legacy ]]; then
            export XLUA_EXPECT_LEAK_CHECKER="${XLUA_LEAK_CHECKER:-ON}"
        else
            export XLUA_EXPECT_LEAK_CHECKER="${XLUA_LEAK_CHECKER:-OFF}"
        fi
    fi
    if [[ "$version" == luajit || "$version" == luajit-uint ]]; then
        make -C "$repo/build/luajit-2.1" clean >/dev/null
        # LuaJIT compiles its API checks into libluajit.a.
        make -C "$repo/build/luajit-2.1" BUILDMODE=static XCFLAGS="-fPIC -DLUA_USE_APICHECK" >/dev/null
        uint=OFF
        if [[ "$version" == luajit-uint ]]; then uint=ON; fi
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DUSING_LUAJIT=ON -DUINT_ESPECIALLY="$uint" \
            -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON "${module_flags[@]}" "${option_flags[@]}"
    else
        cmake -S "$repo/build" -B "$build" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DLUA_VERSION="$version" \
            -DXLUA_SANITIZE="$sanitizers" -DXLUA_APICHECK=ON "${module_flags[@]}"
    fi
    cmake --build "$build" --parallel 4
    plugin="$build/libxlua.so"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/check_exports.py" "$plugin"
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/native_regression.py" "$plugin"
    if [[ "$version" != luajit* ]]; then
        # The internal-header leak checker the profiler test uses is not built for LuaJIT.
        LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/profiler_smoke.py" "$plugin"
    fi
    # XLUA_MEMORY_DIAGNOSTICS: the memory snapshot cases run too, so the walk also runs under the sanitizers from C#.
    LD_PRELOAD="$asan_runtime" python3 "$repo/Test/Native/run_managed.py" "${managed_modes[@]}" \
        "${managed_defines[@]}" \
        --mono-version "${MONO_VERSION:-6.14.1}" --plugin "$plugin" --output "$build/managed"
    echo "XLUA_SANITIZE_PASS: $version ($preset)"
done
