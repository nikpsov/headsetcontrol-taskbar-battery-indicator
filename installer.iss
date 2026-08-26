#define MyAppName "HeadsetControl Taskbar Battery Indicator"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "nikpsov"
#define MyAppURL "https://github.com/nikpsov/headsetcontrol-taskbar-battery-indicator"
#define MyAppExeName "HeadsetControlTaskbarBatteryIndicator.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{D9B2B6A3-C7B4-4B39-9523-28F7969E4B20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; Uncomment the following line to run in non administrative install mode (install for current user only.)
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=HeadsetControlTaskbarBatteryIndicatorSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Run at Windows startup"; GroupDescription: "Additional tasks:"

[Files]
Source: "HeadsetControlTaskbarBatteryIndicator.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "HeadsetControlTaskbarBatteryIndicatorDebug.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "headsetcontrol.dll"; DestDir: "{app}"; Flags: ignoreversion
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HeadsetControlTaskbarBatteryIndicator"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[InstallDelete]
Type: files; Name: "{userstartup}\{#MyAppName}.lnk"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/im ""HeadsetControlTaskbarBatteryIndicator.exe"" /f /t"; Flags: runhidden; RunOnceId: "Kill GUI exe"
Filename: "taskkill"; Parameters: "/im ""HeadsetControlTaskbarBatteryIndicatorDebug.exe"" /f /t"; Flags: runhidden; RunOnceId: "Kill Debug exe"

[Code]
function KillProcessByName(ProcessName: string): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('taskkill.exe', '/F /IM "' + ProcessName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillProcessByName('HeadsetControlTaskbarBatteryIndicator.exe');
  KillProcessByName('HeadsetControlTaskbarBatteryIndicatorDebug.exe');
  KillProcessByName('headsetcontrol.exe');
  Result := '';
end;
