@echo off
@rem Windows x64 LuaJIT plugin. Output: plugin_luajit\Plugins\x86_64\xlua.dll
@rem LuaJIT in GC64 mode (64-bit GC references, no 2 GB heap limit), msvcbuild's default
@rem on x64 and the mode of every 64-bit LuaJIT plugin. xlua.c and CMake refuse a 64-bit
@rem LuaJIT without GC64 (build/README.md, "LuaJIT GC64"). Bytecode from 32-bit LuaJIT,
@rem which has only the other mode, does not load here; luajit -b -X writes GC64 bytecode.
@rem Run it from an x64 developer prompt, or let it open one for the newest Visual Studio
@rem with the C++ x64/x86 build tools (vswhere). CMake picks its newest Visual Studio
@rem generator. The script stops with exit code 1 at the first failed step.
setlocal
cd /d "%~dp0"

if /I "%VSCMD_ARG_TGT_ARCH%"=="x64" goto :luajit
set "__VSWhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "__VSPATH="
if exist "%__VSWhere%" (
	for /f "tokens=*" %%p in (
		'"%__VSWhere%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath'
	) do set "__VSPATH=%%p"
)
if not defined __VSPATH (
	echo No x64 developer prompt, and vswhere found no Visual Studio with the C++ x64/x86 build tools.
	exit /b 1
)
call "%__VSPATH%\VC\Auxiliary\Build\vcvarsall.bat" x64
if /I not "%VSCMD_ARG_TGT_ARCH%"=="x64" (
	echo vcvarsall.bat x64 failed.
	exit /b 1
)

:luajit
echo Building LuaJIT for Windows x64 (GC64)
pushd luajit-2.1\src
if exist lua51.lib del /q lua51.lib
call msvcbuild_mt.bat mixed
if not exist lua51.lib (
	popd
	echo LuaJIT build failed.
	exit /b 1
)
popd

@rem A fresh build directory: one left by the old script names the Visual Studio 2019
@rem generator in its cache, and CMake keeps a cached generator.
if exist build_lj64 rmdir /s /q build_lj64
cmake -S . -B build_lj64 -DUSING_LUAJIT=ON -A x64
if errorlevel 1 exit /b 1
cmake --build build_lj64 --config Release
if errorlevel 1 exit /b 1
if not exist plugin_luajit\Plugins\x86_64 md plugin_luajit\Plugins\x86_64
copy /Y build_lj64\Release\xlua.dll plugin_luajit\Plugins\x86_64\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
