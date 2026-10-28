@rem Windows x64 plugin of Lua 5.3.6. Output: plugin_lua53\Plugins\x86_64\xlua.dll
@rem CMake picks its newest Visual Studio generator.
@rem The script stops with exit code 1 at the first failed step.
setlocal
cd /d "%~dp0"

cmake -S . -B build64 -DLUA_VERSION=5.3.6 -A x64
if errorlevel 1 exit /b 1
cmake --build build64 --config Release
if errorlevel 1 exit /b 1
if not exist plugin_lua53\Plugins\x86_64 md plugin_lua53\Plugins\x86_64
copy /Y build64\Release\xlua.dll plugin_lua53\Plugins\x86_64\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
