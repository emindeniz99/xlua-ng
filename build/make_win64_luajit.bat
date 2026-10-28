@echo off
@rem LuaJIT in GC64 mode (64-bit GC references, no 2 GB heap limit), msvcbuild's default
@rem on x64 and the mode of every 64-bit LuaJIT plugin. xlua.c and CMake refuse a 64-bit
@rem LuaJIT without GC64 (build/README.md, "LuaJIT GC64"). Bytecode from 32-bit LuaJIT,
@rem which has only the other mode, does not load here; luajit -b -X writes GC64 bytecode.

call "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat"

echo Swtich to x64 build env
cd %~dp0\luajit-2.1\src
call msvcbuild_mt.bat mixed
cd ..\..

mkdir build_lj64 & pushd build_lj64
cmake -DUSING_LUAJIT=ON -G "Visual Studio 16 2019" -A x64 ..
IF %ERRORLEVEL% NEQ 0 cmake -DUSING_LUAJIT=ON -G "Visual Studio 16 2019" -A x64 ..
popd
cmake --build build_lj64 --config Release
md plugin_luajit\Plugins\x86_64
copy /Y build_lj64\Release\xlua.dll plugin_luajit\Plugins\x86_64\xlua.dll
pause