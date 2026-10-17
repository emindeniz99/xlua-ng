@rem Windows ARM64 (Unity Standalone Windows, ARM 64-bit; Unity 2023.1 and later).
@rem Builds natively on Windows on Arm or cross-compiles on x64; either way Visual Studio
@rem needs the "MSVC ARM64 build tools" component. Output: plugin_lua54\Plugins\arm64\xlua.dll
@rem (the folder Unity maps to ARM64; Assets/Plugins/arm64/xlua.dll.meta has the settings).
setlocal
cd /d "%~dp0"


cmake -S . -B build_arm64_54 -DLUA_VERSION=5.4.9 -A ARM64
if errorlevel 1 exit /b 1
cmake --build build_arm64_54 --config Release
if errorlevel 1 exit /b 1
md plugin_lua54\Plugins\arm64
copy /Y build_arm64_54\Release\xlua.dll plugin_lua54\Plugins\arm64\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
