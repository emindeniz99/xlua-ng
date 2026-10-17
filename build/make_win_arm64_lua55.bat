@rem Windows ARM64 (Unity Standalone Windows, ARM 64-bit; Unity 2023.1 and later).
@rem Builds natively on Windows on Arm or cross-compiles on x64; either way Visual Studio
@rem needs the "MSVC ARM64 build tools" component. Output: plugin_lua55\Plugins\arm64\xlua.dll
@rem (the folder Unity maps to ARM64; Assets/Plugins/arm64/xlua.dll.meta has the settings).
setlocal
cd /d "%~dp0"

set "__VS=Visual Studio 16 2019"
set "__VSWhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "__VSDISPLAY="
set "__VSVER="

if exist "%__VSWhere%" (
	for /f "tokens=*" %%p in (
		'"%__VSWhere%" -latest -property catalog_productLineVersion'
	) do set __VSDISPLAY=%%p

	for /f "tokens=*" %%p in (
		'"%__VSWhere%" -latest -property catalog_productDisplayVersion'
	) do set __VSVER=%%p
)

if "%__VSVER%" neq "" (
	set __VS=Visual Studio %__VSVER:~0,2% %__VSDisplay%
)

cmake -S . -B build_arm64_55 -DLUA_VERSION=5.5.1 -G "%__VS%" -A ARM64
if errorlevel 1 exit /b 1
cmake --build build_arm64_55 --config Release
if errorlevel 1 exit /b 1
md plugin_lua55\Plugins\arm64
copy /Y build_arm64_55\Release\xlua.dll plugin_lua55\Plugins\arm64\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
