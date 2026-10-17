#!/usr/bin/env bash
# Shared helpers for the make_android_*.sh scripts. Source this file; do not run it.
#
# Environment (all optional except the NDK path):
#   ANDROID_NDK, ANDROID_NDK_HOME or ANDROID_NDK_ROOT  Android NDK root (r30 is the tested release)
#   XLUA_BUILD_ROOT     where build.* and plugin_* directories go (default: this directory)
#   ANDROID_ABIS        space-separated ABIs (default: armeabi-v7a arm64-v8a x86 x86_64)
#   ANDROID_API_LEVEL   minimum Android API level (default: 21, the lowest the NDK supports)
#   XLUA_JOBS           parallel build jobs (default: 4)
#
# The scripts need CMake 3.22 or newer and Linux or macOS. Building LuaJIT for the 32-bit
# ABIs also needs a 32-bit host C compiler (gcc-multilib on Debian/Ubuntu).

XLUA_ANDROID_SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
XLUA_BUILD_ROOT="${XLUA_BUILD_ROOT:-${XLUA_ANDROID_SCRIPT_DIR}}"
XLUA_JOBS="${XLUA_JOBS:-4}"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-21}"
read -r -a XLUA_ANDROID_ABIS <<< "${ANDROID_ABIS:-armeabi-v7a arm64-v8a x86 x86_64}"

NDK_ROOT="${ANDROID_NDK:-${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}}"
NDK_TOOLCHAIN_FILE="${NDK_ROOT}/build/cmake/android.toolchain.cmake"
if [[ -z "${NDK_ROOT}" || ! -f "${NDK_TOOLCHAIN_FILE}" ]]; then
    echo "Set ANDROID_NDK to an installed Android NDK directory." >&2
    exit 1
fi

# Configure, build and install one Lua version for every requested ABI.
# usage: android_build_lua <lua version> <plugin directory name> <build directory prefix>
android_build_lua() {
    local lua_version="$1" plugin_name="$2" build_prefix="$3" abi build_dir plugin_dir
    for abi in "${XLUA_ANDROID_ABIS[@]}"; do
        build_dir="${XLUA_BUILD_ROOT}/${build_prefix}.Android.${abi}"
        plugin_dir="${XLUA_BUILD_ROOT}/${plugin_name}/Plugins/Android/libs/${abi}"
        cmake -S "${XLUA_ANDROID_SCRIPT_DIR}" -B "${build_dir}" \
            -DLUA_VERSION="${lua_version}" \
            -DANDROID_ABI="${abi}" \
            -DANDROID_PLATFORM="android-${ANDROID_API_LEVEL}" \
            -DCMAKE_BUILD_TYPE=Release \
            -DCMAKE_TOOLCHAIN_FILE="${NDK_TOOLCHAIN_FILE}"
        cmake --build "${build_dir}" --config Release --parallel "${XLUA_JOBS}"
        mkdir -p "${plugin_dir}"
        cp "${build_dir}/libxlua.so" "${plugin_dir}/libxlua.so"
    done
}

# Build LuaJIT's static library for every requested ABI with the NDK's clang, following
# "Cross-compiling for Android" in luajit-2.1/doc/install.html, then link libxlua.so with
# CMake and the NDK toolchain file. LuaJIT is compiled in a per-ABI copy of the source tree
# so the vendored luajit-2.1 directory stays untouched.
android_build_luajit() {
    local prebuilt ndk_bin triple clang_triple abi host_cc target_cc src_copy build_dir plugin_dir
    prebuilt=("${NDK_ROOT}"/toolchains/llvm/prebuilt/*)
    if [[ ${#prebuilt[@]} -ne 1 || ! -d "${prebuilt[0]}/bin" ]]; then
        echo "Expected exactly one toolchains/llvm/prebuilt/<host> directory in the NDK." >&2
        return 1
    fi
    ndk_bin="${prebuilt[0]}/bin"

    for abi in "${XLUA_ANDROID_ABIS[@]}"; do
        case "${abi}" in
            armeabi-v7a) triple=arm-linux-androideabi;  clang_triple=armv7a-linux-androideabi; host_cc="gcc -m32" ;;
            arm64-v8a)   triple=aarch64-linux-android;  clang_triple=aarch64-linux-android;    host_cc="gcc" ;;
            x86)         triple=i686-linux-android;     clang_triple=i686-linux-android;       host_cc="gcc -m32" ;;
            x86_64)      triple=x86_64-linux-android;   clang_triple=x86_64-linux-android;     host_cc="gcc" ;;
            *) echo "Unsupported ABI: ${abi}" >&2; return 1 ;;
        esac
        # LuaJIT needs a host compiler with the same pointer size as the target.
        if ! echo 'int main(void){return 0;}' | ${host_cc} -x c - -o /dev/null 2>/dev/null; then
            echo "Host compiler '${host_cc}' cannot build executables; ${abi} needs it (32-bit targets need gcc-multilib)." >&2
            return 1
        fi
        target_cc="${ndk_bin}/${clang_triple}${ANDROID_API_LEVEL}-clang"
        if [[ ! -x "${target_cc}" ]]; then
            echo "Missing ${target_cc}; is API level ${ANDROID_API_LEVEL} supported by this NDK?" >&2
            return 1
        fi

        src_copy="${XLUA_BUILD_ROOT}/buildlj-src.Android.${abi}"
        build_dir="${XLUA_BUILD_ROOT}/buildlj.Android.${abi}"
        plugin_dir="${XLUA_BUILD_ROOT}/plugin_luajit/Plugins/Android/libs/${abi}"
        rm -rf "${src_copy}"
        mkdir -p "${src_copy}"
        cp -R "${XLUA_ANDROID_SCRIPT_DIR}/luajit-2.1/." "${src_copy}/"
        # Objects from a host build in the vendored tree (make_osx_*_luajit.sh
        # builds in place) would otherwise look up to date and end up in libluajit.a.
        make -C "${src_copy}" clean TARGET_SYS=Linux >/dev/null

        make -C "${src_copy}" -j "${XLUA_JOBS}" \
            BUILDMODE=static \
            HOST_CC="${host_cc}" \
            CROSS="${ndk_bin}/${triple}-" \
            TARGET_SYS=Linux \
            STATIC_CC="${target_cc} -fPIC" \
            DYNAMIC_CC="${target_cc} -fPIC" \
            TARGET_LD="${target_cc}" \
            TARGET_AR="${ndk_bin}/llvm-ar rcus" \
            TARGET_STRIP="${ndk_bin}/llvm-strip"

        cmake -S "${XLUA_ANDROID_SCRIPT_DIR}" -B "${build_dir}" \
            -DUSING_LUAJIT=ON \
            -DLUAJIT_STATIC_LIB="${src_copy}/src/libluajit.a" \
            -DANDROID_ABI="${abi}" \
            -DANDROID_PLATFORM="android-${ANDROID_API_LEVEL}" \
            -DCMAKE_BUILD_TYPE=Release \
            -DCMAKE_TOOLCHAIN_FILE="${NDK_TOOLCHAIN_FILE}"
        cmake --build "${build_dir}" --config Release --parallel "${XLUA_JOBS}"
        mkdir -p "${plugin_dir}"
        cp "${build_dir}/libxlua.so" "${plugin_dir}/libxlua.so"
    done
}
