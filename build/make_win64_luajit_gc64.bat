@echo off
@rem LuaJIT in GC64 mode (64-bit GC references, no 2 GB heap limit), the default of
@rem msvcbuild on x64 and the mode of every other 64-bit LuaJIT plugin. make_win64_luajit.bat
@rem builds the non-GC64 mode instead. Bytecode from one mode does not load in the other.
call "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\VC\Auxiliary\Build\vcvars64.bat"

echo Swtich to x64 build env
cd %~dp0\luajit-2.1\src
call msvcbuild_mt.bat mixed
cd ..\..

mkdir build_lj64 & pushd build_lj64
cmake -DUSING_LUAJIT=ON -G "Visual Studio 15 2017 Win64" ..
IF %ERRORLEVEL% NEQ 0 cmake -DUSING_LUAJIT=ON -G "Visual Studio 15 2017 Win64" ..
popd
cmake --build build_lj64 --config Release
md plugin_luajit\Plugins\x86_64
copy /Y build_lj64\Release\xlua.dll plugin_luajit\Plugins\x86_64\xlua.dll
pause