cd "$( dirname "${BASH_SOURCE[0]}" )"
# The bundle is universal (arm64 + x86_64), so LuaJIT is built per arch and merged.
export MACOSX_DEPLOYMENT_TARGET=12.0
for arch in arm64 x86_64; do
    make -C luajit-2.1 clean
    make -C luajit-2.1 BUILDMODE=static XCFLAGS=-fPIC TARGET_FLAGS="-arch $arch"
    mv luajit-2.1/src/libluajit.a luajit-2.1/src/libluajit-$arch.a
done
lipo -create luajit-2.1/src/libluajit-arm64.a luajit-2.1/src/libluajit-x86_64.a -output luajit-2.1/src/libluajit.a

mkdir -p build_lj_osx && cd build_lj_osx
cmake -DUSING_LUAJIT=ON  -GXcode ../
cd ..
cmake --build build_lj_osx --config Release
mkdir -p plugin_luajit/Plugins/xlua.bundle/Contents/MacOS/
cp build_lj_osx/Release/xlua.bundle/Contents/MacOS/xlua plugin_luajit/Plugins/xlua.bundle/Contents/MacOS/xlua
