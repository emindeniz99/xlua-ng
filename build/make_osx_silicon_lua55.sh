mkdir -p build_osx_55_silicon && cd build_osx_55_silicon
cmake -DBUILD_SILICON=ON -DLUA_VERSION=5.5.1 -GXcode ../
cd ..
cmake --build build_osx_55_silicon --config Release
mkdir -p plugin_lua55/Plugins/arm64
cp build_osx_55_silicon/Release/libxlua.dylib plugin_lua55/Plugins/arm64/
