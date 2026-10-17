
mkdir build32 & pushd build32
cmake -DLUA_VERSION=5.3.6 -DLUAC_COMPATIBLE_FORMAT=ON -A Win32 ..
popd
cmake --build build32 --config Release
pause