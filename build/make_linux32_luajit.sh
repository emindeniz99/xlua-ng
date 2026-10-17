#!/usr/bin/env bash
cd "$( dirname "${BASH_SOURCE[0]}" )"
source ./luajit_harden.sh  # XLUA_HARDEN=ON hardens libluajit.a as well
cd luajit-2.1
make clean
make CC="gcc -m32" TARGET_CFLAGS="$(xlua_luajit_harden_cflags "gcc -m32" other)"
cd ..
mkdir -p build_linux32_lj && cd build_linux32_lj
cmake -DUSING_LUAJIT=ON -DCMAKE_C_FLAGS=-m32 -DCMAKE_CXX_FLAGS=-m32 -DCMAKE_SHARED_LINKER_FLAGS=-m32 ../
cd ..
cmake --build build_linux32_lj --config Release

