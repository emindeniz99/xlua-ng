
mkdir build32 & pushd build32
cmake -DLUA_VERSION=5.3.6 -A Win32 ..
popd
cmake --build build32 --config Release
md plugin_lua53\Plugins\x86
copy /Y build32\Release\xlua.dll plugin_lua53\Plugins\x86\xlua.dll
pause