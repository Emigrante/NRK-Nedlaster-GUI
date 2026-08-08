; Inno Setup 6 script for NRK Nedlaster GUI v1.08
; Generert automatisk - installer for Windows (x64)

#define MyAppName "NRK Nedlaster"
#define MyAppVersion "1.08"
#define MyAppPublisher "NRK Nedlaster"
#define MyAppExeName "NRKLastNed.exe"
#define MyAppURL "https://github.com/Emigrante/NRK-Nedlaster-GUI"

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{8D12E065-8FD2-4F7A-B6C1-5E9F7C2A1B3D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=LICENSE
OutputDir=.\DistOut
OutputBaseFilename=NRK-Nedlaster-v{#MyAppVersion}-Setup
SetupIconFile=NRK.ico
Compression=lz4
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
MinVersion=10.0

[Languages]
Name: "norwegian"; MessagesFile: "compiler:Languages\Norwegian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Hoved-applikasjon
Source: "bin\Release\net8.0-windows\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net8.0-windows\*.dll"; DestDir: "{app}"; Flags: ignoreversion

; Ikoner
Source: "NRK.ico"; DestDir: "{app}"; Flags: ignoreversion

; Tools (ffmpeg, ffprobe, yt-dlp)
Source: "Tools\ffmpeg.exe"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "Tools\ffprobe.exe"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "Tools\yt-dlp.exe"; DestDir: "{app}\Tools"; Flags: ignoreversion

; Dokumentasjon
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion; DestName: "LESEMEG.txt"
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\NRK.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\NRK.ico"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\NRK.ico"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Name: "{app}\Tools"; Type: filesandsubdirs
Name: "{app}"; Type: filesandsubdirs

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
	// Eventuell post-installasjon logikk her
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
	// Eventuell post-avinstallasjon logikk her
  end;
end;
