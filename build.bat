@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo Building HeadsetControl Taskbar Battery Indicator
echo ===================================================

rem 1. Check if headsetcontrol.dll exists or build it
if not exist "headsetcontrol.dll" (
    echo Building headsetcontrol.dll with Clang...
    call build_native.bat
    if errorlevel 1 (
        echo [ERROR] Failed to build headsetcontrol.dll
        exit /b 1
    )
)

rem 2. Find csc.exe and WPF library directory
set CSC_PATH=
set WPF_PATH=
for /d %%D in ("%WINDIR%\Microsoft.NET\Framework64\v4.0.30319", "%WINDIR%\Microsoft.NET\Framework\v4.0.30319") do (
    if exist "%%~D\csc.exe" (
        set "CSC_PATH=%%~D\csc.exe"
        if exist "%%~D\WPF" set "WPF_PATH=%%~D\WPF"
    )
)

if "%CSC_PATH%"=="" (
    echo [ERROR] Could not find csc.exe
    exit /b 1
)

set REFS=/reference:PresentationFramework.dll,PresentationCore.dll,WindowsBase.dll,System.Xaml.dll,System.Drawing.dll,System.Windows.Forms.dll
if not "%WPF_PATH%"=="" set REFS=/lib:"%WPF_PATH%" %REFS%

echo.
echo Building HeadsetControlTaskbarBatteryIndicator.exe...
"%CSC_PATH%" /nologo /target:winexe /optimize+ /out:HeadsetControlTaskbarBatteryIndicator.exe %REFS% HeadsetControlNative.cs HeadsetService.cs OverlayApp.cs
if errorlevel 1 (
    echo [ERROR] C# GUI compilation failed.
    exit /b 1
)

echo Building HeadsetControlTaskbarBatteryIndicatorDebug.exe...
"%CSC_PATH%" /nologo /target:exe /optimize+ /define:DEBUG_LOG /out:HeadsetControlTaskbarBatteryIndicatorDebug.exe %REFS% HeadsetControlNative.cs HeadsetService.cs OverlayApp.cs
if errorlevel 1 (
    echo [ERROR] C# Debug Console compilation failed.
    exit /b 1
)

echo.
echo [SUCCESS] Build completed successfully!
exit /b 0
