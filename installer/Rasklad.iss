#ifndef AppVersion
  #define AppVersion "1.0.32"
#endif
#ifndef PackageDir
  #error PackageDir is required
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required
#endif

[Setup]
AppId={{D79B9E12-650B-4B1A-8E3C-781F694EF33C}
AppName=Rasklad
AppVersion={#AppVersion}
AppPublisher=DK-custom
AppPublisherURL=https://github.com/DK-custom/Rasklad-Windows
DefaultDirName={localappdata}\Programs\Rasklad
DefaultGroupName=Rasklad
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.18362
WizardStyle=modern dynamic
WizardSizePercent=100
SetupIconFile=..\assets\Rasklad.ico
UninstallDisplayIcon={app}\Rasklad.exe
LicenseFile=..\LICENSE
OutputDir={#ReleaseDir}
OutputBaseFilename=Rasklad-{#AppVersion}-windows-x64-Setup
Compression=lzma2
SolidCompression=yes
AppMutex=Local\PuntoWinSpeller
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
Uninstallable=yes
UninstallDisplayName=Rasklad
VersionInfoVersion={#AppVersion}.0
ChangesAssociations=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Rasklad"; Filename: "{app}\Rasklad.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,Rasklad}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Rasklad"; Filename: "{app}\Rasklad.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\Rasklad"; Filename: "{app}\Rasklad.exe"; WorkingDir: "{app}"; Tasks: autostart

[Run]
Filename: "{app}\Rasklad.exe"; Description: "{cm:LaunchProgram,Rasklad}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
russian.AutoStart=Запускать Rasklad при входе в Windows
english.AutoStart=Start Rasklad when signing in to Windows
russian.FrameworkMissing=Для Rasklad требуется .NET Framework 4.8 или новее. Установите его с сайта Microsoft и повторите установку.
english.FrameworkMissing=Rasklad requires .NET Framework 4.8 or later. Install it from Microsoft and run Setup again.

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox(CustomMessage('FrameworkMissing'), mbError, MB_OK);
end;
