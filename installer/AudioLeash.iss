; AudioLeash Installer Script
; Requires: Inno Setup 6  (https://jrsoftware.org/isinfo.php)
; Build:    .\build-installer.ps1  (from repo root)
;
; Produces a per-user installer (no UAC prompt) that installs to
; %LOCALAPPDATA%\AudioLeash.  Settings are stored separately by the app in
; %APPDATA%\AudioLeash\ and are not touched by this installer.

#define MyAppName      "AudioLeash"
; Version is read at compile time from the published executable, so the
; .csproj <Version> (overridable via build-installer.ps1 -Version) is the
; single source of truth. Requires ..\publish\AudioLeash.exe to exist first —
; build-installer.ps1 runs `dotnet publish` before invoking ISCC.
#define MyAppVersion   GetVersionNumbersString(AddBackslash(SourcePath) + "..\publish\AudioLeash.exe")
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

; No .NET runtime check here: a 32-bit Inno installer reading HKLM gets redirected to
; WOW6432Node, where the x64 desktop runtime isn't listed, so the check false-positived on
; machines that already had it. The framework-dependent apphost shows its own "install .NET"
; prompt (with a download link) on first launch if the runtime is genuinely missing.
