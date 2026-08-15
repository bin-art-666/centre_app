#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

[Setup]
AppId={{897D267B-C228-4607-B473-B26005752BF2}
AppName=Centre
AppVersion={#MyAppVersion}
AppPublisher=bin-art-666
AppPublisherURL=https://github.com/bin-art-666/centre_app
DefaultDirName={localappdata}\Programs\Centre
DefaultGroupName=Centre
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=Centre-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\Assets\Centre.ico
UninstallDisplayIcon={app}\Centre.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Centre"; Filename: "{app}\Centre.exe"
Name: "{autodesktop}\Centre"; Filename: "{app}\Centre.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Centre.exe"; Description: "启动 Centre"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if MsgBox('是否同时删除 Centre 的应用列表、设置和图标缓存？', mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Centre'), True, True, True);
end;
