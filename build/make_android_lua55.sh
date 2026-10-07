#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(dirname -- "${BASH_SOURCE[0]}")"
if [[ "${SCRIPT_DIR}" != /* ]]; then
    SCRIPT_DIR="${PWD}/${SCRIPT_DIR}"
fi
BUILD_ROOT="${XLUA_BUILD_ROOT:-${SCRIPT_DIR}}"
NDK_ROOT="${ANDROID_NDK:-${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}}"
if [[ ! -f "${NDK_ROOT}/build/cmake/android.toolchain.cmake" ]]; then
    echo "Set ANDROID_NDK to an installed Android NDK directory." >&2
    exit 1
fi

read -r -a ABIS <<< "${ANDROID_ABIS:-armeabi-v7a arm64-v8a x86 x86_64}"
for ABI in "${ABIS[@]}"; do
    BUILD_DIR="${BUILD_ROOT}/build54.Android.${ABI}"
    PLUGIN_DIR="${BUILD_ROOT}/plugin_lua55/Plugins/Android/libs/${ABI}"
    cmake -S "${SCRIPT_DIR}" -B "${BUILD_DIR}" \
        -DLUA_VERSION=5.5.1 \
        -DANDROID_ABI="${ABI}" \
        -DANDROID_PLATFORM="android-${ANDROID_API_LEVEL:-21}" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_TOOLCHAIN_FILE="${NDK_ROOT}/build/cmake/android.toolchain.cmake"
    cmake --build "${BUILD_DIR}" --config Release --parallel 4
    mkdir -p "${PLUGIN_DIR}"
    cp "${BUILD_DIR}/libxlua.so" "${PLUGIN_DIR}/libxlua.so"
done
