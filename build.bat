@echo on
setlocal

rem Find csc.exe
set CSC_PATH=
for /d %%D in ("%WINDIR%\Microsoft.NET\Framework\v4.0.30319", "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319") do (
    if exist "%%~D\csc.exe" set "CSC_PATH=%%~D\csc.exe"
)

if "%CSC_PATH%"=="" (
    echo Error: Could not find csc.exe
    exit /b 1
)

echo Building HeadsetControlTaskbarBatteryIndicator.exe...
"%CSC_PATH%" /nologo /target:winexe /out:HeadsetControlTaskbarBatteryIndicator.exe /reference:PresentationFramework.dll,PresentationCore.dll,WindowsBase.dll,System.Xaml.dll,System.Drawing.dll,System.Windows.Forms.dll OverlayApp.cs
if errorlevel 1 exit /b 1

echo Building HeadsetControlTaskbarBatteryIndicatorConsole.exe...
"%CSC_PATH%" /nologo /target:exe /out:HeadsetControlTaskbarBatteryIndicatorConsole.exe /reference:PresentationFramework.dll,PresentationCore.dll,WindowsBase.dll,System.Xaml.dll,System.Drawing.dll,System.Windows.Forms.dll OverlayApp.cs
if errorlevel 1 exit /b 1

echo Build successful!
exit /b 0
