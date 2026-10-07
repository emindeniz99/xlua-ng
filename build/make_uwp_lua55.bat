mkdir build_uwp_55 & pushd build_uwp_55
cmake -DLUA_VERSION=5.5.1 -G "Visual Studio 16 2019" -A Win32 -DCMAKE_SYSTEM_NAME=WindowsStore -DCMAKE_SYSTEM_VERSION=10.0 ..
popd
cmake --build build_uwp_55 --config Release
md plugin_lua55\Plugins\WSA\x86
copy /Y build_uwp_55\Release\xlua.dll plugin_lua55\Plugins\WSA\x86\xlua.dll

mkdir build_uwp64_55 & pushd build_uwp64_55
cmake -DLUA_VERSION=5.5.1 -G "Visual Studio 16 2019" -A x64 -DCMAKE_SYSTEM_NAME=WindowsStore -DCMAKE_SYSTEM_VERSION=10.0 ..
popd
cmake --build build_uwp64_55 --config Release
md plugin_lua55\Plugins\WSA\x64
copy /Y build_uwp64_55\Release\xlua.dll plugin_lua55\Plugins\WSA\x64\xlua.dll

mkdir build_uwp_arm_55 & pushd build_uwp_arm_55
cmake -DLUA_VERSION=5.5.1 -G "Visual Studio 16 2019" -A ARM -DCMAKE_SYSTEM_NAME=WindowsStore -DCMAKE_SYSTEM_VERSION=10.0 ..
popd
cmake --build build_uwp_arm_55 --config Release
md plugin_lua55\Plugins\WSA\ARM
copy /Y build_uwp_arm_55\Release\xlua.dll plugin_lua55\Plugins\WSA\ARM\xlua.dll

mkdir build_uwp_arm64_55 & pushd build_uwp_arm64_55
cmake -DLUA_VERSION=5.5.1 -G "Visual Studio 16 2019" -A ARM64 -DCMAKE_SYSTEM_NAME=WindowsStore -DCMAKE_SYSTEM_VERSION=10.0 ..
popd
cmake --build build_uwp_arm64_55 --config Release
md plugin_lua55\Plugins\WSA\ARM64
copy /Y build_uwp_arm64_55\Release\xlua.dll plugin_lua55\Plugins\WSA\ARM64\xlua.dll

pause
