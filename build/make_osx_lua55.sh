#!/usr/bin/env bash
# macOS plugin bundle, universal (x86_64 and arm64), Lua 5.5.1.
# MACOSX_DEPLOYMENT_TARGET (default 12.0) is the oldest macOS the plugin loads on
# (build/README.md, "macOS and iOS deployment targets"). XLUA_BUILD_ROOT puts the
# build directory and plugin_lua55/ elsewhere (default: this directory).
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
BUILD_ROOT="${XLUA_BUILD_ROOT:-${SCRIPT_DIR}}"
BUILD_DIR="${BUILD_ROOT}/build_osx_55"

cmake -S "${SCRIPT_DIR}" -B "${BUILD_DIR}" -DLUA_VERSION=5.5.1 \
    -DCMAKE_OSX_DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-12.0}" -G Xcode
cmake --build "${BUILD_DIR}" --config Release
mkdir -p "${BUILD_ROOT}/plugin_lua55/Plugins/xlua.bundle/Contents/MacOS"
cp "${BUILD_DIR}/Release/xlua.bundle/Contents/MacOS/xlua" "${BUILD_ROOT}/plugin_lua55/Plugins/xlua.bundle/Contents/MacOS/xlua"
