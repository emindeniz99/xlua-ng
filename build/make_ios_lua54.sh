#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(dirname -- "${BASH_SOURCE[0]}")"
if [[ "${SCRIPT_DIR}" != /* ]]; then
    SCRIPT_DIR="${PWD}/${SCRIPT_DIR}"
fi
BUILD_ROOT="${XLUA_BUILD_ROOT:-${SCRIPT_DIR}}"
# XLUA_VARIANT=<name>: build a plugin variant into Plugins/iOS/xlua-variants/<name>/
# (build/README.md, "Plugin variants"). The CMake options XLUA_LUASOCKET, XLUA_LIBS ...
# come from the environment.
VARIANT="${XLUA_VARIANT:-}"
if [[ -n "${VARIANT}" && ! "${VARIANT}" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "XLUA_VARIANT must be lower-case letters, digits and underscores, starting with a letter." >&2
    exit 1
fi
BUILD_DIR="${BUILD_ROOT}/build_ios_54${VARIANT:+.${VARIANT}}"
PLUGIN_DIR="${BUILD_ROOT}/plugin_lua54/Plugins/iOS${VARIANT:+/xlua-variants/${VARIANT}}"

cmake -S "${SCRIPT_DIR}" -B "${BUILD_DIR}" \
    -DLUA_VERSION=5.4.9 \
    -DCMAKE_TOOLCHAIN_FILE="${SCRIPT_DIR}/cmake/ios.toolchain.cmake" \
    -DPLATFORM=OS64 \
    -DDEPLOYMENT_TARGET="${IOS_DEPLOYMENT_TARGET:-15.0}" \
    -DENABLE_BITCODE=OFF -G Xcode
cmake --build "${BUILD_DIR}" --config Release --parallel 4
mkdir -p "${PLUGIN_DIR}"
cp "${BUILD_DIR}/Release-iphoneos/libxlua.a" "${PLUGIN_DIR}/libxlua.a"
