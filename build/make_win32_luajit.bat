@echo off
@rem Windows x86 LuaJIT plugin. Output: plugin_luajit\Plugins\x86\xlua.dll
@rem 32-bit LuaJIT has only the non-GC64 mode; its bytecode does not load in the 64-bit
@rem plugins (build/README.md, "LuaJIT GC64").
@rem Run it from an x86 developer prompt, or let it open one for the newest Visual Studio
@rem with the C++ x64/x86 build tools (vswhere). CMake picks its newest Visual Studio
@rem generator. The script stops with exit code 1 at the first failed step.
setlocal
cd /d "%~dp0"

if /I "%VSCMD_ARG_TGT_ARCH%"=="x86" goto :luajit
set "__VSWhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "__VSPATH="
if exist "%__VSWhere%" (
	for /f "tokens=*" %%p in (
		'"%__VSWhere%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath'
	) do set "__VSPATH=%%p"
)
if not defined __VSPATH (
	echo No x86 developer prompt, and vswhere found no Visual Studio with the C++ x64/x86 build tools.
	exit /b 1
)
call "%__VSPATH%\VC\Auxiliary\Build\vcvarsall.bat" x86
if /I not "%VSCMD_ARG_TGT_ARCH%"=="x86" (
	echo vcvarsall.bat x86 failed.
	exit /b 1
)

:luajit
echo Building LuaJIT for Windows x86
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
if exist build_lj32 rmdir /s /q build_lj32
cmake -S . -B build_lj32 -DUSING_LUAJIT=ON -A Win32
if errorlevel 1 exit /b 1
cmake --build build_lj32 --config Release
if errorlevel 1 exit /b 1
if not exist plugin_luajit\Plugins\x86 md plugin_luajit\Plugins\x86
copy /Y build_lj32\Release\xlua.dll plugin_luajit\Plugins\x86\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
