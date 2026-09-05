#ifndef PayloadInclude
  #error A verified explicit payload is required
#endif

[Setup]
AppId={{79A42D80-A0E3-45CA-BBBC-E6B2E48EBBE2}
AppName=Egoist Voice
AppVersion=2.2.0
AppVerName=Egoist Voice 2.2.0 Preview 2 · Full + Qwen
AppPublisher=EGOIST
DefaultDirName={localappdata}\Programs\Egoist Voice
DefaultGroupName=Egoist Voice
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.18362
OutputDir={#OutputDir}
OutputBaseFilename=EgoistVoice-Full-2.2.0-preview.2-inner
SetupIconFile=..\assets\EgoistVoice.ico
UninstallDisplayIcon={app}\Egoist.Voice.exe
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=no
ShowLanguageDialog=no
Compression=lzma2/normal
SolidCompression=yes
DiskSpanning=yes
DiskSliceSize=2000000000
CloseApplications=no
RestartApplications=no
AppMutex=Local\Egoist.Voice.SingleInstance
UninstallLogMode=append
VersionInfoVersion=2.2.0.0
VersionInfoDescription=Egoist Voice Full + Qwen Preview 2

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
#include PayloadInclude

[Icons]
Name: "{autoprograms}\Egoist Voice"; Filename: "{app}\Egoist.Voice.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Egoist Voice"; Filename: "{app}\Egoist.Voice.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Remove only this application's legacy startup entry on uninstall. No new startup entry.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "EgoistVoice"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Egoist.Voice.exe"; Description: "{cm:LaunchProgram,Egoist Voice}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function Quoted(const Value: String): String;
begin
  Result := '"' + Value + '"';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  Args: String;
begin
  if CurStep = ssPostInstall then
  begin
    Args := '-NoProfile -ExecutionPolicy Bypass -File ' +
      Quoted(ExpandConstant('{app}\setup\translation-engine\invoke-engine-bootstrap.ps1')) +
      ' -Action InstallOwner -OwnerId egoist-voice -OwnerVersion 2.2.0 -EngineVersion 1.0.1' +
      ' -OwnerInstallPath ' + Quoted(ExpandConstant('{app}')) +
      ' -OwnerUninstallKey "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{79A42D80-A0E3-45CA-BBBC-E6B2E48EBBE2}_is1"' +
      ' -HostPayload ' + Quoted(ExpandConstant('{tmp}\egoist-voice-engine\host-payload')) +
      ' -LocalPackRoot ' + Quoted(ExpandConstant('{tmp}\egoist-voice-engine\offline-pack')) +
      ' -LogPath ' + Quoted(ExpandConstant('{localappdata}\EGOIST\TranslationEngine\state\setup-voice.log'));
    if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
    begin
      MsgBox('Не удалось установить локальный движок. Повторите установку. Код: ' + IntToStr(Code), mbError, MB_OK);
      Abort;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
  Args: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if FileExists(ExpandConstant('{app}\Egoist.Voice.exe')) then
      Exec(ExpandConstant('{app}\Egoist.Voice.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Args := '-NoProfile -ExecutionPolicy Bypass -File ' +
      Quoted(ExpandConstant('{app}\setup\translation-engine\invoke-engine-bootstrap.ps1')) +
      ' -Action RemoveOwner -OwnerId egoist-voice -CleanupIfLast' +
      ' -LogPath ' + Quoted(ExpandConstant('{localappdata}\EGOIST\TranslationEngine\state\setup-voice.log'));
    if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
      MsgBox('Общий движок сохранён. Его состояние можно проверить в настройках; код: ' + IntToStr(Code), mbInformation, MB_OK);
  end;
end;

// User settings, history, dictionaries and reusable model caches are preserved.
// The engine lifecycle removes only the Voice owner and preserves another owner.
