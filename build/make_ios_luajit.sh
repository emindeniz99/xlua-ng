#!/bin/bash
# Build the iOS arm64 LuaJIT plugin. Follows LuaJIT's iOS cross-compile
# instructions (build/luajit-2.1/doc/install.html); needs Xcode, no sudo.
set -euo pipefail
cd "$( dirname "${BASH_SOURCE[0]}" )"
source ./luajit_harden.sh  # XLUA_HARDEN=ON hardens libluajit.a as well

IOS_DEPLOYMENT_TARGET="${IOS_DEPLOYMENT_TARGET:-15.0}"
ISDKP=$(xcrun --sdk iphoneos --show-sdk-path)
ICC=$(xcrun --sdk iphoneos --find clang)
ISDKF="-arch arm64 -isysroot $ISDKP -miphoneos-version-min=$IOS_DEPLOYMENT_TARGET"

make -C luajit-2.1 clean TARGET_SYS=iOS
make -C luajit-2.1 DEFAULT_CC=clang CROSS="$(dirname "$ICC")/" \
    TARGET_FLAGS="$ISDKF" TARGET_SYS=iOS BUILDMODE=static \
    TARGET_CFLAGS="$(xlua_luajit_harden_cflags "$ICC $ISDKF" other)"

mkdir -p build_lj_ios && cd build_lj_ios
cmake -DUSING_LUAJIT=ON -DCMAKE_TOOLCHAIN_FILE=../cmake/ios.toolchain.cmake -DPLATFORM=OS64 -DDEPLOYMENT_TARGET="$IOS_DEPLOYMENT_TARGET" -GXcode ../
cd ..
cmake --build build_lj_ios --config Release

mkdir -p plugin_luajit/Plugins/iOS/
libtool -static -o plugin_luajit/Plugins/iOS/libxlua.a build_lj_ios/Release-iphoneos/libxlua.a luajit-2.1/src/libluajit.a
