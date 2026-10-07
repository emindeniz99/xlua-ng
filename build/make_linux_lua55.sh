mkdir -p build_linux64_55 && cd build_linux64_55
cmake -DLUA_VERSION=5.5.1 ../
cd ..
cmake --build build_linux64_55 --config Release
mkdir -p plugin_lua55/Plugins/x86_64/
cp build_linux64_55/libxlua.so plugin_lua55/Plugins/x86_64/libxlua.so
