@echo off
@rem LuaJIT without GC64 ("nogc64": 32-bit GC references, so the Lua heap is limited to
@rem 2 GB; the upstream xLua default for Windows x64). Every other 64-bit LuaJIT plugin,
@rem make_win64_luajit_gc64.bat included, is GC64.
@rem Bytecode from one mode does not load in the other; luajit -b -W or -X picks the mode.

call "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat"

echo Swtich to x64 build env
cd %~dp0\luajit-2.1\src
call msvcbuild_mt.bat nogc64 mixed
cd ..\..

mkdir build_lj64 & pushd build_lj64
cmake -DUSING_LUAJIT=ON -G "Visual Studio 16 2019" -A x64 ..
IF %ERRORLEVEL% NEQ 0 cmake -DUSING_LUAJIT=ON -G "Visual Studio 16 2019" -A x64 ..
popd
cmake --build build_lj64 --config Release
md plugin_luajit\Plugins\x86_64
copy /Y build_lj64\Release\xlua.dll plugin_luajit\Plugins\x86_64\xlua.dll
pause