@rem Windows x64 and x86 plugins of Lua 5.4.9. Output: plugin_lua54\Plugins\x86_64\xlua.dll
@rem and plugin_lua54\Plugins\x86\xlua.dll. CMake picks its newest Visual Studio generator.
@rem The script stops with exit code 1 at the first failed step.
setlocal
cd /d "%~dp0"

cmake -S . -B build64_54 -DLUA_VERSION=5.4.9 -A x64
if errorlevel 1 exit /b 1
cmake --build build64_54 --config Release
if errorlevel 1 exit /b 1
if not exist plugin_lua54\Plugins\x86_64 md plugin_lua54\Plugins\x86_64
copy /Y build64_54\Release\xlua.dll plugin_lua54\Plugins\x86_64\xlua.dll
if errorlevel 1 exit /b 1

cmake -S . -B build32_54 -DLUA_VERSION=5.4.9 -A Win32
if errorlevel 1 exit /b 1
cmake --build build32_54 --config Release
if errorlevel 1 exit /b 1
if not exist plugin_lua54\Plugins\x86 md plugin_lua54\Plugins\x86
copy /Y build32_54\Release\xlua.dll plugin_lua54\Plugins\x86\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
