@rem Windows x86 plugin of Lua 5.3.6. Output: plugin_lua53\Plugins\x86\xlua.dll
@rem CMake picks its newest Visual Studio generator.
@rem The script stops with exit code 1 at the first failed step.
setlocal
cd /d "%~dp0"

cmake -S . -B build32 -DLUA_VERSION=5.3.6 -A Win32
if errorlevel 1 exit /b 1
cmake --build build32 --config Release
if errorlevel 1 exit /b 1
if not exist plugin_lua53\Plugins\x86 md plugin_lua53\Plugins\x86
copy /Y build32\Release\xlua.dll plugin_lua53\Plugins\x86\xlua.dll
if errorlevel 1 exit /b 1

if not defined CI pause
