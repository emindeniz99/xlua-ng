cd "$( dirname "${BASH_SOURCE[0]}" )"
# The oldest macOS the plugin loads on, for libluajit.a and xlua (build/README.md,
# "macOS and iOS deployment targets").
export MACOSX_DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-12.0}"
source ./luajit_harden.sh  # XLUA_HARDEN=ON hardens libluajit.a as well
make -C luajit-2.1 clean
make -C luajit-2.1 BUILDMODE=static XCFLAGS=-fPIC TARGET_FLAGS="-arch arm64" \
    TARGET_CFLAGS="$(xlua_luajit_harden_cflags "cc -arch arm64" other)"

mkdir -p build_lj_osx_silicon && cd build_lj_osx_silicon
cmake -DBUILD_SILICON=ON -DUSING_LUAJIT=ON -DCMAKE_OSX_DEPLOYMENT_TARGET="$MACOSX_DEPLOYMENT_TARGET" -GXcode ../
cd ..
cmake --build build_lj_osx_silicon --config Release
# BUILD_SILICON builds a plain shared library, like make_osx_silicon_lua55.sh.
mkdir -p plugin_luajit/Plugins/arm64
cp build_lj_osx_silicon/Release/libxlua.dylib plugin_luajit/Plugins/arm64/
