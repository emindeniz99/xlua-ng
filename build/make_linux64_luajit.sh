#!/usr/bin/env bash
cd "$( dirname "${BASH_SOURCE[0]}" )"
source ./luajit_harden.sh  # XLUA_HARDEN=ON hardens libluajit.a as well
case "$(uname -m)" in aarch64|arm64) harden_target=arm64-elf ;; x86_64) harden_target=x86_64-linux ;; *) harden_target=other ;; esac
cd luajit-2.1
make clean
make CFLAGS=-fPIC TARGET_CFLAGS="$(xlua_luajit_harden_cflags gcc "$harden_target")"
cd ..
mkdir -p build_linux64_lj && cd build_linux64_lj
cmake -DUSING_LUAJIT=ON ../
cd ..
cmake --build build_linux64_lj --config Release
mkdir -p plugin_luajit/Plugins/x86_64/
cp build_linux64_lj/libxlua.so plugin_luajit/Plugins/x86_64/libxlua.so 

