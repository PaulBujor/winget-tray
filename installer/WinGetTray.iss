; Inno Setup script for WinGetTray. Built in CI with:
;   ISCC.exe /DAppVersion=1.2.0 installer\WinGetTray.iss
; The payload is the self-contained single-file exe in ..\dist (no .NET runtime needed).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceExe
  #define SourceExe "..\dist\WinGetTray.exe"
#endif

#define AppName "WinGetTray"
#define AppExe "WinGetTray.exe"

[Setup]
AppId={{8F2E6B3A-4C1D-4F2B-9E7A-5B3C1D2E4F60}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Paul Bujor
AppPublisherURL=https://github.com/PaulBujor/winget-tray
AppSupportURL=https://github.com/PaulBujor/winget-tray/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; per-user by default (no admin prompt); the wizard can switch to all users
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\src\WinGetTray\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start WinGetTray when I sign in"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--show"
Name: "{autoprograms}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--show"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function KillRunning(): Boolean;
var
  exitCode: Integer;
begin
  { the tray app is normally running, so it would hold the exe open during install/uninstall }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '',
       SW_HIDE, ewWaitUntilTerminated, exitCode);
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  existing: String;
begin
  if CurStep = ssInstall then
  begin
    KillRunning();
  end;

  if CurStep = ssPostInstall then
  begin
    { an older autostart entry may point at a build folder: repoint it at the installed exe }
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}', existing) then
    begin
      RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}',
        '"' + ExpandConstant('{app}\{#AppExe}') + '"');
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    KillRunning();
  end;
end;
