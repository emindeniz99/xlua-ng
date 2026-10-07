mkdir -p build_linux32 && cd build_linux32
cmake -DLUA_VERSION=5.3.6 -DCMAKE_C_FLAGS=-m32 -DCMAKE_CXX_FLAGS=-m32 -DCMAKE_SHARED_LINKER_FLAGS=-m32 ../
cd ..
cmake --build build_linux32 --config Release

