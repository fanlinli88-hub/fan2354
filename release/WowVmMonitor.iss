#ifndef MyAppVersion
  #error MyAppVersion must be provided by the release script.
#endif
#ifndef SourceExe
  #error SourceExe must be provided by the release script.
#endif

#define MyAppName "WowVmMonitor"
#define MyAppExeName "WowVmMonitor.exe"

[Setup]
AppId={{C7AF91D3-2598-4BB7-BEC0-3712BE565857}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=WowVmMonitor
DefaultDirName={localappdata}\Programs\WowVmMonitor
DefaultGroupName=WowVmMonitor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=WowVmMonitor.exe
RestartApplications=no
UsePreviousAppDir=yes
OutputBaseFilename=WowVmMonitor-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
SignedUninstaller=yes
SignTool=local

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: checkedonce

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion restartreplace

[Icons]
Name: "{autoprograms}\WowVmMonitor"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\WowVmMonitor"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 WowVmMonitor"; Flags: nowait postinstall skipifsilent
