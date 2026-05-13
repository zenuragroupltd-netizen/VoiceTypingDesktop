; ============================================================
;  VoiceTypingDesktop.iss
;  Inno Setup script for Voice Typing Desktop.
;  Download Inno Setup (free): https://jrsoftware.org/isdl.php
;  Build:
;     "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Installer\VoiceTypingDesktop.iss
;  or just run Tools\package.ps1 which does everything in one go.
; ============================================================

; ------------------------------------------------------------
;  Edit these 5 fields when you rebrand or release a new version.
;  Everything else in this script reads from them via #define.
; ------------------------------------------------------------
#define MyAppName          "Voice Typing Desktop"
#define MyAppExeName       "VoiceTypingDesktop.exe"
#define MyAppPublisher     "Muinol Islam"
#define MyAppURL           "https://kinetimart.com/"
#define MyAppVersion       "1.1.0"

[Setup]
AppId={{A7F2C3D6-5B18-4F53-8D3A-51E6C2AA7F11}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Use a non-Desktop location so the output files are never hit by
; Smart App Control heuristics. This "build artifact" folder is simple.
OutputDir=..\Installer\Output
OutputBaseFilename=VoiceTypingDesktop-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequiredOverridesAllowed=dialog
; User-mode install by default (no UAC prompt, per-user):
PrivilegesRequired=lowest
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";   Description: "Create a &desktop shortcut";   GroupDescription: "Additional shortcuts:"; Flags: checkedonce
Name: "startupicon";   Description: "Start with &Windows (recommended)"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; All files from the publish folder go into the install directory.
; We mark the exe as the main app so it can be launched post-install.
Source: "..\Installer\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";         Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";   Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}";   Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
