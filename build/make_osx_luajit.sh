cd "$( dirname "${BASH_SOURCE[0]}" )"
# The bundle is universal (arm64 + x86_64), so LuaJIT is built per arch and merged.
# The oldest macOS the plugin loads on, for libluajit.a and xlua (build/README.md,
# "macOS and iOS deployment targets").
export MACOSX_DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-12.0}"
source ./luajit_harden.sh  # XLUA_HARDEN=ON hardens libluajit.a as well
for arch in arm64 x86_64; do
    make -C luajit-2.1 clean
    make -C luajit-2.1 BUILDMODE=static XCFLAGS=-fPIC TARGET_FLAGS="-arch $arch" \
        TARGET_CFLAGS="$(xlua_luajit_harden_cflags "cc -arch $arch" other)"
    mv luajit-2.1/src/libluajit.a luajit-2.1/src/libluajit-$arch.a
done
lipo -create luajit-2.1/src/libluajit-arm64.a luajit-2.1/src/libluajit-x86_64.a -output luajit-2.1/src/libluajit.a

mkdir -p build_lj_osx && cd build_lj_osx
cmake -DUSING_LUAJIT=ON -DCMAKE_OSX_DEPLOYMENT_TARGET="$MACOSX_DEPLOYMENT_TARGET" -GXcode ../
cd ..
cmake --build build_lj_osx --config Release
mkdir -p plugin_luajit/Plugins/xlua.bundle/Contents/MacOS/
cp build_lj_osx/Release/xlua.bundle/Contents/MacOS/xlua plugin_luajit/Plugins/xlua.bundle/Contents/MacOS/xlua
