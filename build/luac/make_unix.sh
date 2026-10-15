mkdir -p build_unix && cd build_unix
cmake -DLUA_VERSION=5.3.6 -DLUAC_COMPATIBLE_FORMAT=ON ../
cd ..
cmake --build build_unix --config Release

