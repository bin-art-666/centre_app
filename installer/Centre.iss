#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

[Setup]
AppId={{897D267B-C228-4607-B473-B26005752BF2}
AppName=应用中心
AppVersion={#MyAppVersion}
AppVerName=应用中心 {#MyAppVersion}
AppPublisher=bin-art-666
AppPublisherURL=https://github.com/bin-art-666/centre_app
AppSupportURL=https://github.com/bin-art-666/centre_app/issues
AppUpdatesURL=https://github.com/bin-art-666/centre_app/releases
DefaultDirName={localappdata}\Programs\应用中心
DefaultGroupName=应用中心
DisableProgramGroupPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=应用中心-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\Assets\Centre.ico
UninstallDisplayName=应用中心
UninstallDisplayIcon={app}\应用中心.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=bin-art-666
VersionInfoDescription=应用中心安装程序
VersionInfoProductName=应用中心
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\应用中心"; Filename: "{app}\应用中心.exe"
Name: "{autodesktop}\应用中心"; Filename: "{app}\应用中心.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\应用中心.exe"; Description: "启动应用中心"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if MsgBox('是否同时删除应用中心的应用列表、设置和图标缓存？', mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Centre'), True, True, True);
end;
