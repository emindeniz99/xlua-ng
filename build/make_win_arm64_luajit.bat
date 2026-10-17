@echo off
@rem Windows ARM64 LuaJIT plugin (Unity Standalone Windows, ARM 64-bit; Unity 2023.1 and later).
@rem LuaJIT's msvcbuild supports ARM64 from an ARM64 developer prompt, or cross from x64 after
@rem "vcvarsall.bat x64_arm64" (luajit-2.1/doc/install.html). Run this script from such a
@rem prompt, or let it open the newest Visual Studio that has the MSVC ARM64 build tools.
@rem ARM64 LuaJIT is always GC64, like every 64-bit LuaJIT except make_win64_luajit.bat.
@rem Output: plugin_luajit\Plugins\arm64\xlua.dll
setlocal
cd /d "%~dp0"

set "__VSWhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "__VSPATH="
if exist "%__VSWhere%" (
	for /f "tokens=*" %%p in (
		'"%__VSWhere%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath'
	) do set "__VSPATH=%%p"
)

if /I "%VSCMD_ARG_TGT_ARCH%"=="arm64" goto :luajit
if not defined __VSPATH (
	echo No ARM64 developer prompt, and vswhere found no Visual Studio with the MSVC ARM64 build tools.
	exit /b 1
)
set "__VCVARS=x64_arm64"
if /I "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "__VCVARS=arm64"
call "%__VSPATH%\VC\Auxiliary\Build\vcvarsall.bat" %__VCVARS%
if /I not "%VSCMD_ARG_TGT_ARCH%"=="arm64" exit /b 1

:luajit
echo Building LuaJIT for Windows ARM64
pushd luajit-2.1\src
if exist lua51.lib del /q lua51.lib
call msvcbuild_mt.bat mixed
if not exist lua51.lib (
	popd
	echo LuaJIT build failed.
	exit /b 1
)
popd

cmake -S . -B build_lj_arm64 -DUSING_LUAJIT=ON -A ARM64
if errorlevel 1 exit /b 1
cmake --build build_lj_arm64 --config Release
if errorlevel 1 exit /b 1
md plugin_luajit\Plugins\arm64
copy /Y build_lj_arm64\Release\xlua.dll plugin_luajit\Plugins\arm64\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
