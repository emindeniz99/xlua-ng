cd "$( dirname "${BASH_SOURCE[0]}" )"
export MACOSX_DEPLOYMENT_TARGET=12.0
source ./luajit_harden.sh  # XLUA_HARDEN=ON hardens libluajit.a as well
make -C luajit-2.1 clean
make -C luajit-2.1 BUILDMODE=static XCFLAGS=-fPIC TARGET_FLAGS="-arch arm64" \
    TARGET_CFLAGS="$(xlua_luajit_harden_cflags "cc -arch arm64" other)"

mkdir -p build_lj_osx && cd build_lj_osx
cmake -DBUILD_SILICON=ON -DUSING_LUAJIT=ON  -GXcode ../
cd ..
cmake --build build_lj_osx --config Release
# BUILD_SILICON builds a plain shared library, like make_osx_silicon_lua55.sh.
mkdir -p plugin_luajit/Plugins/arm64
cp build_lj_osx/Release/libxlua.dylib plugin_luajit/Plugins/arm64/
