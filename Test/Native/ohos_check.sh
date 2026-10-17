#!/bin/bash
# Build libxlua with the OpenHarmony native SDK, then run Test/Native/plugin_smoke.c
# against it on Linux without a device or emulator. The SDK's dynamic loader
# needs an OpenHarmony system (it crashes on plain Linux), so the smoke test is
# linked statically against OpenHarmony's musl libc and the object files CMake
# compiled for libxlua.so: the same code and flags, without the .so loading.
# Linux only. Usage: Test/Native/ohos_check.sh <OpenHarmony native SDK> <output dir>
# ABIS selects ABIs (default: arm64-v8a armeabi-v7a x86_64); LUA_VERSION the
# runtime (default 5.5.1). An ABI the host cannot execute runs through
# qemu-<arch> (qemu-user) when it is on PATH, and is reported as SKIP otherwise.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
sdk="$(cd "${1:?usage: ohos_check.sh <native sdk> <output dir>}" && pwd)"
out="$(mkdir -p "${2:?usage: ohos_check.sh <native sdk> <output dir>}" && cd "$2" && pwd)"
version="${LUA_VERSION:-5.5.1}"
export PATH="$sdk/build-tools/cmake/bin:$PATH"

passed=() skipped=()
for abi in ${ABIS:-arm64-v8a armeabi-v7a x86_64}; do
    case "$abi" in
        arm64-v8a) triple=aarch64-linux-ohos; qemu=qemu-aarch64; flags=() ;;
        armeabi-v7a) triple=arm-linux-ohos; qemu=qemu-arm; flags=(-march=armv7a) ;;
        # The SDK's static x86_64 libc reads the stack canary through FS before
        # it sets FS; ohos_x86_64_entry.c explains the scratch thread block.
        x86_64) triple=x86_64-linux-ohos; qemu=qemu-x86_64
            flags=("$repo/Test/Native/ohos_x86_64_entry.c" -Wl,-e,xlua_ohos_x86_64_entry) ;;
        *) echo "unknown ABI $abi" >&2; exit 2 ;;
    esac
    build="$out/$version/$abi"
    cmake -S "$repo/build" -B "$build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DLUA_VERSION="$version" \
        -DOHOS_STL=c++_shared -DOHOS_ARCH="$abi" -DOHOS_PLATFORM=OHOS \
        -DCMAKE_TOOLCHAIN_FILE="$sdk/build/cmake/ohos.toolchain.cmake" >/dev/null
    cmake --build "$build" >/dev/null
    test -f "$build/libxlua.so"
    mapfile -t objects < <(find "$build/CMakeFiles/xlua.dir" -name '*.o' | sort)
    "$sdk/llvm/bin/clang" --target="$triple" --sysroot="$sdk/sysroot" "${flags[@]}" -std=c17 -Wall -Wextra -Werror \
        -static "$repo/Test/Native/plugin_smoke.c" "${objects[@]}" -lm -o "$build/plugin_smoke"
    run=("$build/plugin_smoke")
    status=0
    "${run[@]}" || status=$?
    if [[ "$status" == 126 ]]; then  # the kernel cannot execute this architecture
        if command -v "$qemu" >/dev/null; then
            status=0
            "$qemu" "${run[@]}" || status=$?
        else
            echo "SKIP $abi: this host cannot run it and $qemu is not installed"
            skipped+=("$abi")
            continue
        fi
    fi
    [[ "$status" == 0 ]] || { echo "FAIL $abi (exit $status)" >&2; exit 1; }
    passed+=("$abi")
done
echo "XLUA_OHOS_CHECK_PASS: Lua $version on ${passed[*]:-no ABI}${skipped[*]:+ (skipped: ${skipped[*]})}"
[[ ${#passed[@]} -gt 0 ]]
