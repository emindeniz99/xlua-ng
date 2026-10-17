
mkdir build64 & pushd build64
cmake -DLUA_VERSION=5.3.6 -DLUAC_COMPATIBLE_FORMAT=ON -A x64 ..
popd
cmake --build build64 --config Release
pause