@echo off
setlocal enabledelayedexpansion

echo [1/3] Generating version.h...
(
echo #pragma once
echo #define VERSION "4.0.0"
) > vendor\headsetcontrol\lib\version.h

echo [2/3] Compiling hidapi (C)...
clang -c -O2 -I vendor/hidapi/hidapi -I vendor/hidapi/windows vendor/hidapi/windows/hid.c -o hid.o
if errorlevel 1 (
    echo Error compiling hid.c
    exit /b 1
)

echo [3/3] Compiling HeadsetControl C++ library into standalone static headsetcontrol.dll...
clang++ -std=c++20 -O2 -shared -static -static-libgcc -static-libstdc++ -DHSC_BUILDING_DLL -I vendor/headsetcontrol/lib -I vendor/headsetcontrol/lib/devices -I vendor/hidapi/hidapi -I vendor/hidapi/windows vendor/headsetcontrol/lib/device.cpp vendor/headsetcontrol/lib/device_registry.cpp vendor/headsetcontrol/lib/globals.cpp vendor/headsetcontrol/lib/headsetcontrol.cpp vendor/headsetcontrol/lib/headsetcontrol_c.cpp vendor/headsetcontrol/lib/hid_utility.cpp vendor/headsetcontrol/lib/result_types.cpp vendor/headsetcontrol/lib/utility.cpp vendor/headsetcontrol/lib/devices/hid_device.cpp native_ext.cpp hid.o -lsetupapi -o headsetcontrol.dll
if errorlevel 1 (
    echo Error compiling headsetcontrol.dll
    exit /b 1
)

del hid.o 2>nul
echo Successfully built standalone headsetcontrol.dll!
exit /b 0
