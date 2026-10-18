#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(dirname -- "${BASH_SOURCE[0]}")"
if [[ "${SCRIPT_DIR}" != /* ]]; then
    SCRIPT_DIR="${PWD}/${SCRIPT_DIR}"
fi
BUILD_ROOT="${XLUA_BUILD_ROOT:-${SCRIPT_DIR}}"
# XLUA_VARIANT=<name>: build a plugin variant into Plugins/iOS/xlua-variants/<name>/
# (build/README.md, "Plugin variants"). XLUA_PRESET (legacy or minimal, docs/design/presets.md)
# and the CMake options XLUA_LUASOCKET, XLUA_LIBS ... come from the environment; a preset
# other than legacy builds in a build directory of its own.
VARIANT="${XLUA_VARIANT:-}"
if [[ -n "${VARIANT}" && ! "${VARIANT}" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "XLUA_VARIANT must be lower-case letters, digits and underscores, starting with a letter." >&2
    exit 1
fi
PRESET_SUFFIX=""
if [[ -n "${XLUA_PRESET:-}" && "${XLUA_PRESET}" != legacy ]]; then
    PRESET_SUFFIX=".preset-${XLUA_PRESET}"
fi
# XLUA_IOS_SDK=simulator: build for the arm64 iOS Simulator instead of devices, as the
# plugin variant "simulator" (or "<variant>_simulator"), which the plugin selector
# takes for a Target SDK = Simulator SDK build (build/README.md, "iOS Simulator").
SDK="${XLUA_IOS_SDK:-device}"
case "${SDK}" in
    device) PLATFORM=OS64; SDK_DIR=iphoneos ;;
    simulator) PLATFORM=SIMULATORARM64; SDK_DIR=iphonesimulator
        VARIANT="${VARIANT:+${VARIANT}_}simulator" ;;
    *) echo "XLUA_IOS_SDK must be device or simulator." >&2; exit 1 ;;
esac
BUILD_DIR="${BUILD_ROOT}/build_ios_54${VARIANT:+.${VARIANT}}${PRESET_SUFFIX}"
PLUGIN_DIR="${BUILD_ROOT}/plugin_lua54/Plugins/iOS${VARIANT:+/xlua-variants/${VARIANT}}"

cmake -S "${SCRIPT_DIR}" -B "${BUILD_DIR}" \
    -DLUA_VERSION=5.4.9 \
    -DCMAKE_TOOLCHAIN_FILE="${SCRIPT_DIR}/cmake/ios.toolchain.cmake" \
    -DPLATFORM="${PLATFORM}" \
    -DDEPLOYMENT_TARGET="${IOS_DEPLOYMENT_TARGET:-15.0}" \
    -DENABLE_BITCODE=OFF -G Xcode
cmake --build "${BUILD_DIR}" --config Release --parallel 4
mkdir -p "${PLUGIN_DIR}"
cp "${BUILD_DIR}/Release-${SDK_DIR}/libxlua.a" "${PLUGIN_DIR}/libxlua.a"
