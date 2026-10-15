cd "$( dirname "${BASH_SOURCE[0]}" )"
export MACOSX_DEPLOYMENT_TARGET=12.0
make -C luajit-2.1 clean
make -C luajit-2.1 BUILDMODE=static XCFLAGS=-fPIC TARGET_FLAGS="-arch arm64"

mkdir -p build_lj_osx && cd build_lj_osx
cmake -DBUILD_SILICON=ON -DUSING_LUAJIT=ON  -GXcode ../
cd ..
cmake --build build_lj_osx --config Release
mkdir -p plugin_luajit/Plugins/xlua.bundle/Contents/MacOS/
cp build_lj_osx/Release/xlua.bundle/Contents/MacOS/xlua plugin_luajit/Plugins/xlua.bundle/Contents/MacOS/xlua
