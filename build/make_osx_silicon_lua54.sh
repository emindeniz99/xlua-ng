#!/usr/bin/env bash
# macOS arm64 dylib (Plugins/arm64/libxlua.dylib), Lua 5.4.9.
# MACOSX_DEPLOYMENT_TARGET (default 12.0) is the oldest macOS the plugin loads on
# (build/README.md, "macOS and iOS deployment targets"). XLUA_BUILD_ROOT puts the
# build directory and plugin_lua54/ elsewhere (default: this directory).
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
BUILD_ROOT="${XLUA_BUILD_ROOT:-${SCRIPT_DIR}}"
BUILD_DIR="${BUILD_ROOT}/build_osx_54_silicon"

cmake -S "${SCRIPT_DIR}" -B "${BUILD_DIR}" -DLUA_VERSION=5.4.9 -DBUILD_SILICON=ON \
    -DCMAKE_OSX_DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-12.0}" -G Xcode
cmake --build "${BUILD_DIR}" --config Release
mkdir -p "${BUILD_ROOT}/plugin_lua54/Plugins/arm64"
cp "${BUILD_DIR}/Release/libxlua.dylib" "${BUILD_ROOT}/plugin_lua54/Plugins/arm64/"
