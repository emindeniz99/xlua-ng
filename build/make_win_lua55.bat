
mkdir build64_55 & pushd build64_55
cmake -DLUA_VERSION=5.5.1 -A x64  ..
popd
cmake --build build64_55 --config Release
md plugin_lua55\Plugins\x86_64
copy /Y build64_55\Release\xlua.dll plugin_lua55\Plugins\x86_64\xlua.dll

mkdir build32_55 & pushd build32_55
cmake -DLUA_VERSION=5.5.1 -A Win32 ..
popd
cmake --build build32_55 --config Release
md plugin_lua55\Plugins\x86
copy /Y build32_55\Release\xlua.dll plugin_lua55\Plugins\x86\xlua.dll

pause
