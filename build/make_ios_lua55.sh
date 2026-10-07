#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(dirname -- "${BASH_SOURCE[0]}")"
if [[ "${SCRIPT_DIR}" != /* ]]; then
    SCRIPT_DIR="${PWD}/${SCRIPT_DIR}"
fi
BUILD_ROOT="${XLUA_BUILD_ROOT:-${SCRIPT_DIR}}"
BUILD_DIR="${BUILD_ROOT}/build_ios_55"
PLUGIN_DIR="${BUILD_ROOT}/plugin_lua55/Plugins/iOS"

cmake -S "${SCRIPT_DIR}" -B "${BUILD_DIR}" \
    -DLUA_VERSION=5.5.1 \
    -DCMAKE_TOOLCHAIN_FILE="${SCRIPT_DIR}/cmake/ios.toolchain.cmake" \
    -DPLATFORM=OS64 \
    -DDEPLOYMENT_TARGET="${IOS_DEPLOYMENT_TARGET:-15.0}" \
    -DENABLE_BITCODE=OFF -G Xcode
cmake --build "${BUILD_DIR}" --config Release --parallel 4
mkdir -p "${PLUGIN_DIR}"
cp "${BUILD_DIR}/Release-iphoneos/libxlua.a" "${PLUGIN_DIR}/libxlua.a"
