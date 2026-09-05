#ifndef PayloadInclude
  #error PayloadInclude must be a verified explicit file list
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#define AppVersion "2.2.0"

[Setup]
AppId={{5F84E54F-BE2E-46BA-970C-D1A774D3D239}
AppName=Egoist Voice Compact
AppVersion={#AppVersion}
AppVerName=Egoist Voice Compact {#AppVersion}
AppPublisher=EGOIST
DefaultDirName={localappdata}\Programs\Egoist Voice Compact
DefaultGroupName=Egoist Voice Compact
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.18362
OutputDir={#OutputDir}
OutputBaseFilename=EgoistVoice-Setup-Compact-RU-2.2.0-win-x64
SetupIconFile=..\assets\EgoistVoice.ico
UninstallDisplayIcon={app}\Egoist.Voice.exe
UninstallDisplayName=Egoist Voice Compact
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
ShowLanguageDialog=no
Compression=lzma2/normal
SolidCompression=yes
DiskSpanning=no
CloseApplications=no
RestartApplications=no
AppMutex=Local\Egoist.Voice.SingleInstance
UninstallLogMode=append
VersionInfoVersion=2.2.0.0
VersionInfoDescription=Egoist Voice Compact RU - offline CPU

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
#include PayloadInclude

[Icons]
Name: "{autoprograms}\Egoist Voice Compact"; Filename: "{app}\Egoist.Voice.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Egoist Voice Compact"; Filename: "{app}\Egoist.Voice.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Egoist.Voice.exe"; Description: "{cm:LaunchProgram,Egoist Voice Compact}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

; Data is created by the application and is deliberately absent from [Files] and [UninstallDelete].
; The per-user uninstaller removes its tracked program/models and shortcuts, preserving user data.
