; AudioLeash Installer Script
; Requires: Inno Setup 6  (https://jrsoftware.org/isinfo.php)
; Build:    .\build-installer.ps1  (from repo root)
;
; Produces a per-user installer (no UAC prompt) that installs to
; %LOCALAPPDATA%\AudioLeash.  Settings are stored separately by the app in
; %APPDATA%\AudioLeash\ and are not touched by this installer.

#define MyAppName      "AudioLeash"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "DoctorKomodo"
#define MyAppURL       "https://github.com/DoctorKomodo/AudioLeash"
#define MyAppExeName   "AudioLeash.exe"

[Setup]
; NOTE: The AppId value uniquely identifies this application.
; Do not change it once the installer has been distributed.
AppId={{6F3A2C1D-8B4E-4F0A-9D7C-2E5B1A3C6F8D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Install to %LOCALAPPDATA%\AudioLeash — no UAC prompt required.
DefaultDirName={localappdata}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest

; Installer output
OutputDir=Output
OutputBaseFilename=AudioLeash-Setup
SetupIconFile=..\AudioLeash\Resources\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Shown as a checkbox on the final page of the wizard (checked by default).
; The app's own tray-menu "Start with Windows" toggle manages the same registry
; value, so the user can change this preference at any time after installation.
Name: "startup"; \
  Description: "Start {#MyAppName} automatically when Windows starts"; \
  GroupDescription: "Startup:"

[Files]
; Copies everything produced by `dotnet publish` into {app}.
; The Resources\ subfolder (containing icon.ico) is included via recursesubdirs.
Source: "..\publish\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userstartmenu}\{#MyAppName}";          Filename: "{app}\{#MyAppExeName}"
Name: "{userstartmenu}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Write (or skip) the Run value depending on whether the startup task was selected.
; The value matches exactly what StartupService.Enable() would write, so the
; tray-menu toggle reads the correct state immediately after first launch.
Root: HKCU; \
  Subkey:    "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; \
  ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Flags:     uninsdeletevalue; \
  Tasks:     startup

[Run]
; Offer to launch the app immediately after installation completes.
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Launch {#MyAppName} now"; \
  Flags: postinstall nowait skipifsilent

[UninstallRun]
; Terminate any running instance before the uninstaller removes files.
Filename: "taskkill"; \
  Parameters: "/IM {#MyAppExeName} /F"; \
  Flags: runhidden; \
  RunOnceId: "KillAudioLeash"

[Code]
// ---------------------------------------------------------------------------
// .NET 10 Windows Desktop Runtime check
// ---------------------------------------------------------------------------
// dotnet writes a subkey per installed version under this path (e.g. "10.0.0").
const
  DotNetRegKey =
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

function IsDotNet10Installed(): Boolean;
var
  Names: TArrayOfString;
  I:     Integer;
begin
  Result := False;
  if not RegGetSubkeyNames(HKLM, DotNetRegKey, Names) then Exit;
  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    // Version subkeys start with "10." (e.g. "10.0.0", "10.0.1").
    if Pos('10.', Names[I]) = 1 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet10Installed() then
  begin
    if MsgBox(
        '.NET 10 Windows Desktop Runtime is required but does not appear to be installed.'
        + #13#10#13#10
        + 'Download it from:'
        + #13#10
        + 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0'
        + #13#10#13#10
        + 'Install it first, then run this installer again.'
        + #13#10#13#10
        + 'Continue installation anyway?',
        mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
