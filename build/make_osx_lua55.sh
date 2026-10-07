mkdir -p build_osx_55 && cd build_osx_55
cmake -DLUA_VERSION=5.5.1 -GXcode ../
cd ..
cmake --build build_osx_55 --config Release
mkdir -p plugin_lua55/Plugins/xlua.bundle/Contents/MacOS/
cp build_osx_55/Release/xlua.bundle/Contents/MacOS/xlua plugin_lua55/Plugins/xlua.bundle/Contents/MacOS/xlua
