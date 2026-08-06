#ifndef SourceDir
  #define SourceDir "..\artifacts\release\installer-staging"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif
#ifndef ModelSourceDir
  #define ModelSourceDir "..\artifacts\release\model-staging"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "2.1.0"
#endif

#define MyAppName "Egoist Voice"
#define MyAppExeName "Egoist.Voice.exe"

[Setup]
AppId={{79A42D80-A0E3-45CA-BBBC-E6B2E48EBBE2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=EGOIST
; LicenseFile здесь задавать НЕЛЬЗЯ. Он вставляет в мастер страницу wpLicense, на которой кнопка
; «Далее» заблокирована, пока не отмечен переключатель «принимаю условия». Брендовая оболочка
; прячет и notebook со страницами, и NextButton, поэтому отметить его нечем: установщик намертво
; замирает на «Подготавливаю установку» с 1%. Тексты лицензий кладутся в {app} через [Files] —
; MIT требует, чтобы уведомление сопровождало копии, а не чтобы его прокликивали.
DefaultDirName={localappdata}\Programs\Egoist Voice
DefaultGroupName=Egoist Voice
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutputDir}
OutputBaseFilename=EgoistVoice-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\assets\EgoistVoice.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=yes
DisableReadyPage=yes
DisableFinishedPage=no
DisableStartupPrompt=yes
WizardResizable=no
ShowLanguageDialog=no
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallLogMode=overwrite
AllowNoIcons=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=EGOIST
VersionInfoDescription=Egoist Voice Installer
VersionInfoProductName=Egoist Voice
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\assets\installer-microphone-52.bmp"; Flags: dontcopy
Source: "..\assets\installer-text-26.bmp"; Flags: dontcopy
Source: "..\assets\installer-privacy-26.bmp"; Flags: dontcopy
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ModelSourceDir}\*"; DestDir: "{localappdata}\EgoistVoice\Models"; Flags: ignoreversion recursesubdirs createallsubdirs onlyifdoesntexist

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"; Check: IsSafeAppDirectory
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Models\Language"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Models\Qwen"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Models\LLM"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Models\Text"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Data"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Temp"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Logs"
Type: files; Name: "{localappdata}\EgoistVoice\history.bin"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Temp"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Logs"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Models"
Type: filesandordirs; Name: "{localappdata}\EgoistVoice\Data"
Type: files; Name: "{localappdata}\EgoistVoice\settings.json"
Type: files; Name: "{localappdata}\EgoistVoice\activation.json"
Type: dirifempty; Name: "{localappdata}\EgoistVoice"
Type: dirifempty; Name: "{app}"

[Icons]
Name: "{group}\Egoist Voice"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EgoistVoice"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить Egoist Voice"; Flags: nowait skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  BackgroundColor = $00030202;
  TrackColor = $002A2525;
  PrimaryTextColor = $00F8F7F7;
  SecondaryTextColor = $00AAA3A3;
  DisabledTextColor = $00787070;
  AccentColor = $003426FF;

type
  TEgoistSystemTime = record
    Year: Word;
    Month: Word;
    DayOfWeek: Word;
    Day: Word;
    Hour: Word;
    Minute: Word;
    Second: Word;
    Milliseconds: Word;
  end;

var
  BrandSurface: TPanel;
  HeaderIcon: TBitmapImage;
  TextIcon: TBitmapImage;
  PrivacyIcon: TBitmapImage;
  TitleLabel: TNewStaticText;
  VersionLabel: TNewStaticText;
  SubtitleLabel: TNewStaticText;
  FeatureLabel1: TNewStaticText;
  FeatureLabel2: TNewStaticText;
  StatusLabel: TNewStaticText;
  DetailLabel: TNewStaticText;
  PercentLabel: TNewStaticText;
  ProgressTrack: TPanel;
  ProgressFill: TPanel;
  PrimaryButton: TPanel;
  PrimaryButtonLabel: TNewStaticText;
  CloseLabel: TNewStaticText;
  FootnoteLabel: TNewStaticText;
  IsInstallStarted: Boolean;
  IsInstallFinished: Boolean;

procedure GetSystemTime(var SystemTime: TEgoistSystemTime);
  external 'GetSystemTime@kernel32.dll stdcall';

function CreateRoundRectRgn(Left, Top, Right, Bottom, Width, Height: Integer): Integer;
  external 'CreateRoundRectRgn@gdi32.dll stdcall';
function SetWindowRgn(Wnd, Rgn: Integer; Redraw: Boolean): Integer;
  external 'SetWindowRgn@user32.dll stdcall';
procedure ApplyRoundedPanel(PanelControl: TPanel; Radius: Integer);
var
  Region: Integer;
begin
  Region := CreateRoundRectRgn(
    0,
    0,
    PanelControl.Width + 1,
    PanelControl.Height + 1,
    Radius,
    Radius);
  SetWindowRgn(PanelControl.Handle, Region, True);
end;

function CreateSurfaceLabel: TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  { Disable AutoSize before assigning any caption. Otherwise the default-font
    caption overwrites the intended layout width before StyleLabel runs. }
  Result.AutoSize := False;
  Result.Parent := BrandSurface;
end;

procedure StyleLabel(LabelControl: TNewStaticText; FontSize: Integer; FontColor: TColor; Bold: Boolean);
begin
  { Freeze the layout slot before changing the font. TNewStaticText starts with
    AutoSize enabled, so font changes would otherwise shrink the control to its
    initial caption and clip longer runtime statuses. }
  LabelControl.AutoSize := False;
  LabelControl.Font.Name := 'Segoe UI';
  LabelControl.Font.Size := FontSize;
  LabelControl.Font.Color := FontColor;
  if Bold then
    LabelControl.Font.Style := [fsBold]
  else
    LabelControl.Font.Style := [];
end;

procedure SetProgress(Current, Total: Integer);
var
  AvailableWidth: Integer;
  ProgressWidth: Integer;
begin
  AvailableWidth := ProgressTrack.ClientWidth;
  if (Total <= 0) or (Current <= 0) then
    ProgressWidth := 0
  else
    ProgressWidth := (AvailableWidth * Current) div Total;
  if (Current > 0) and (ProgressWidth < ScaleX(3)) then
    ProgressWidth := ScaleX(3);
  if ProgressWidth > AvailableWidth then
    ProgressWidth := AvailableWidth;
  ProgressFill.Width := ProgressWidth;
  if (Total > 0) and (Current > 0) then
    PercentLabel.Caption := IntToStr((Current * 100) div Total) + '%'
  else
    PercentLabel.Caption := '';
end;

procedure SetPrimaryButton(ACaption: String; Enabled: Boolean);
begin
  PrimaryButtonLabel.Caption := ACaption;
  PrimaryButtonLabel.AutoSize := True;
  PrimaryButtonLabel.Left := (PrimaryButton.ClientWidth - PrimaryButtonLabel.Width) div 2;
  if Enabled then
  begin
    PrimaryButton.Color := AccentColor;
    PrimaryButtonLabel.Font.Color := PrimaryTextColor;
    PrimaryButton.Cursor := crHand;
    PrimaryButtonLabel.Cursor := crHand;
  end
  else
  begin
    PrimaryButton.Color := TrackColor;
    PrimaryButtonLabel.Font.Color := DisabledTextColor;
    PrimaryButton.Cursor := crDefault;
    PrimaryButtonLabel.Cursor := crDefault;
  end;
end;

procedure SetInstallerState(AStatus, ADetail: String);
begin
  StatusLabel.Caption := AStatus;
  DetailLabel.Caption := ADetail;
end;

procedure StartInstallClick(Sender: TObject);
begin
  if IsInstallFinished then
  begin
    WizardForm.NextButton.OnClick(WizardForm.NextButton);
    exit;
  end;
  if IsInstallStarted then
    exit;

  IsInstallStarted := True;
  CloseLabel.Visible := False;
  SetPrimaryButton('Устанавливаю…', False);
  SetInstallerState('Подготавливаю установку', 'Останавливаю старую версию и проверяю файлы');
  SetProgress(1, 100);
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure CloseInstallerClick(Sender: TObject);
begin
  WizardForm.CancelButton.OnClick(WizardForm.CancelButton);
end;

procedure CreateBrandShell;
var
  Radius: Integer;
  Region: Integer;
begin
  WizardForm.Caption := 'Egoist Voice {#MyAppVersion}';
  WizardForm.BorderStyle := bsNone;
  WizardForm.ClientWidth := ScaleX(580);
  WizardForm.ClientHeight := ScaleY(318);
  WizardForm.Color := AccentColor;
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.Font.Size := 9;
  WizardForm.Position := poScreenCenter;

  Radius := ScaleX(22);
  Region := CreateRoundRectRgn(0, 0, WizardForm.Width + 1, WizardForm.Height + 1,
    Radius, Radius);
  SetWindowRgn(WizardForm.Handle, Region, True);

  WizardForm.OuterNotebook.Visible := False;
  WizardForm.InnerNotebook.Visible := False;
  WizardForm.Bevel.Visible := False;
  WizardForm.BeveledLabel.Visible := False;
  WizardForm.NextButton.Visible := False;
  WizardForm.BackButton.Visible := False;
  WizardForm.CancelButton.Visible := False;

  BrandSurface := TPanel.Create(WizardForm);
  BrandSurface.Parent := WizardForm;
  BrandSurface.Left := ScaleX(1);
  BrandSurface.Top := ScaleY(1);
  BrandSurface.Width := WizardForm.ClientWidth - ScaleX(2);
  BrandSurface.Height := WizardForm.ClientHeight - ScaleY(2);
  BrandSurface.Color := BackgroundColor;
  BrandSurface.ParentBackground := False;
  BrandSurface.BevelOuter := bvNone;
  BrandSurface.Anchors := [akLeft, akTop, akRight, akBottom];
  ApplyRoundedPanel(BrandSurface, ScaleX(20));

  CloseLabel := CreateSurfaceLabel;
  CloseLabel.Left := BrandSurface.Width - ScaleX(38);
  CloseLabel.Top := ScaleY(14);
  CloseLabel.Width := ScaleX(28);
  CloseLabel.Height := ScaleY(28);
  CloseLabel.Caption := '×';
  CloseLabel.Cursor := crHand;
  CloseLabel.OnClick := @CloseInstallerClick;
  StyleLabel(CloseLabel, 15, SecondaryTextColor, False);
  CloseLabel.AutoSize := True;

  ExtractTemporaryFile('installer-microphone-52.bmp');
  ExtractTemporaryFile('installer-text-26.bmp');
  ExtractTemporaryFile('installer-privacy-26.bmp');

  HeaderIcon := TBitmapImage.Create(WizardForm);
  HeaderIcon.Parent := BrandSurface;
  HeaderIcon.Left := ScaleX(28);
  HeaderIcon.Top := ScaleY(22);
  HeaderIcon.Width := ScaleX(52);
  HeaderIcon.Height := ScaleY(52);
  HeaderIcon.Stretch := True;
  HeaderIcon.Bitmap.LoadFromFile(ExpandConstant('{tmp}\installer-microphone-52.bmp'));

  TitleLabel := CreateSurfaceLabel;
  TitleLabel.Left := ScaleX(94);
  TitleLabel.Top := ScaleY(22);
  TitleLabel.Width := ScaleX(310);
  TitleLabel.Height := ScaleY(36);
  TitleLabel.Caption := 'Egoist Voice';
  StyleLabel(TitleLabel, 18, PrimaryTextColor, True);

  VersionLabel := CreateSurfaceLabel;
  VersionLabel.Left := ScaleX(95);
  VersionLabel.Top := ScaleY(53);
  VersionLabel.Width := ScaleX(300);
  VersionLabel.Height := ScaleY(20);
  VersionLabel.Caption := 'Локальная диктовка · версия {#MyAppVersion}';
  StyleLabel(VersionLabel, 9, SecondaryTextColor, False);

  SubtitleLabel := CreateSurfaceLabel;
  SubtitleLabel.Left := ScaleX(28);
  SubtitleLabel.Top := ScaleY(92);
  SubtitleLabel.Width := ScaleX(524);
  SubtitleLabel.Height := ScaleY(28);
  SubtitleLabel.Caption := 'Голос превращается в чистый текст.';
  StyleLabel(SubtitleLabel, 11, PrimaryTextColor, True);

  TextIcon := TBitmapImage.Create(WizardForm);
  TextIcon.Parent := BrandSurface;
  TextIcon.Left := ScaleX(29);
  TextIcon.Top := ScaleY(128);
  TextIcon.Width := ScaleX(26);
  TextIcon.Height := ScaleY(26);
  TextIcon.Stretch := True;
  TextIcon.Bitmap.LoadFromFile(ExpandConstant('{tmp}\installer-text-26.bmp'));

  FeatureLabel1 := CreateSurfaceLabel;
  FeatureLabel1.Left := ScaleX(65);
  FeatureLabel1.Top := ScaleY(133);
  FeatureLabel1.Width := ScaleX(190);
  FeatureLabel1.Height := ScaleY(20);
  FeatureLabel1.Caption := 'Пунктуация и абзацы';
  StyleLabel(FeatureLabel1, 9, PrimaryTextColor, True);

  PrivacyIcon := TBitmapImage.Create(WizardForm);
  PrivacyIcon.Parent := BrandSurface;
  PrivacyIcon.Left := ScaleX(298);
  PrivacyIcon.Top := ScaleY(128);
  PrivacyIcon.Width := ScaleX(26);
  PrivacyIcon.Height := ScaleY(26);
  PrivacyIcon.Stretch := True;
  PrivacyIcon.Bitmap.LoadFromFile(ExpandConstant('{tmp}\installer-privacy-26.bmp'));

  FeatureLabel2 := CreateSurfaceLabel;
  FeatureLabel2.Left := ScaleX(334);
  FeatureLabel2.Top := ScaleY(133);
  FeatureLabel2.Width := ScaleX(200);
  FeatureLabel2.Height := ScaleY(20);
  FeatureLabel2.Caption := 'Полностью локально';
  StyleLabel(FeatureLabel2, 9, PrimaryTextColor, True);

  StatusLabel := CreateSurfaceLabel;
  StatusLabel.Left := ScaleX(28);
  StatusLabel.Top := ScaleY(178);
  StatusLabel.Width := ScaleX(424);
  StatusLabel.Height := ScaleY(23);
  StatusLabel.Caption := 'Готово к установке';
  StyleLabel(StatusLabel, 11, PrimaryTextColor, True);

  DetailLabel := CreateSurfaceLabel;
  DetailLabel.Left := ScaleX(28);
  DetailLabel.Top := ScaleY(202);
  DetailLabel.Width := ScaleX(524);
  DetailLabel.Height := ScaleY(20);
  DetailLabel.Caption := 'Модели и runtime уже внутри';
  StyleLabel(DetailLabel, 9, SecondaryTextColor, False);

  PercentLabel := CreateSurfaceLabel;
  PercentLabel.Left := ScaleX(468);
  PercentLabel.Top := ScaleY(180);
  PercentLabel.Width := ScaleX(84);
  PercentLabel.Height := ScaleY(20);
  PercentLabel.Caption := '';
  StyleLabel(PercentLabel, 9, SecondaryTextColor, True);

  ProgressTrack := TPanel.Create(WizardForm);
  ProgressTrack.Parent := BrandSurface;
  ProgressTrack.Left := ScaleX(28);
  ProgressTrack.Top := ScaleY(230);
  ProgressTrack.Width := ScaleX(524);
  ProgressTrack.Height := ScaleY(4);
  ProgressTrack.Color := TrackColor;
  ProgressTrack.ParentBackground := False;
  ProgressTrack.BevelOuter := bvNone;
  ApplyRoundedPanel(ProgressTrack, ScaleX(4));

  ProgressFill := TPanel.Create(WizardForm);
  ProgressFill.Parent := ProgressTrack;
  ProgressFill.Left := 0;
  ProgressFill.Top := 0;
  ProgressFill.Width := ScaleX(3);
  ProgressFill.Height := ProgressTrack.Height;
  ProgressFill.Color := AccentColor;
  ProgressFill.ParentBackground := False;
  ProgressFill.BevelOuter := bvNone;

  PrimaryButton := TPanel.Create(WizardForm);
  PrimaryButton.Parent := BrandSurface;
  PrimaryButton.Left := BrandSurface.Width - ScaleX(196);
  PrimaryButton.Top := ScaleY(252);
  PrimaryButton.Width := ScaleX(168);
  PrimaryButton.Height := ScaleY(40);
  PrimaryButton.ParentBackground := False;
  PrimaryButton.BevelOuter := bvNone;
  PrimaryButton.OnClick := @StartInstallClick;
  ApplyRoundedPanel(PrimaryButton, ScaleX(12));

  PrimaryButtonLabel := TNewStaticText.Create(WizardForm);
  PrimaryButtonLabel.Parent := PrimaryButton;
  PrimaryButtonLabel.Left := 0;
  PrimaryButtonLabel.Top := ScaleY(10);
  PrimaryButtonLabel.Width := PrimaryButton.Width;
  PrimaryButtonLabel.Height := ScaleY(22);
  PrimaryButtonLabel.OnClick := @StartInstallClick;
  StyleLabel(PrimaryButtonLabel, 10, PrimaryTextColor, True);
  SetPrimaryButton('Установить', True);

  FootnoteLabel := CreateSurfaceLabel;
  FootnoteLabel.Left := ScaleX(28);
  FootnoteLabel.Top := ScaleY(264);
  FootnoteLabel.Width := ScaleX(330);
  FootnoteLabel.Height := ScaleY(20);
  FootnoteLabel.Caption := 'Windows 10/11 x64 · офлайн · 1,3 ГБ';
  StyleLabel(FootnoteLabel, 8, DisabledTextColor, False);
end;

procedure InitializeWizard;
begin
  IsInstallStarted := False;
  IsInstallFinished := False;
  CreateBrandShell;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if IsInstallStarted and not IsInstallFinished then
    Cancel := False
  else
  begin
    Cancel := True;
    Confirm := False;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Страница лицензии в брендовой оболочке — тупик: Inno держит NextButton выключенной, пока не
  // отмечен переключатель «принимаю», а оболочка прячет и переключатель, и саму кнопку. Один раз
  // это уже стоило зависания на «Подготавливаю установку» с 1%. LicenseFile из [Setup] убран, но
  // если его вернут — принимаем сами и едем дальше, а не замираем без единого сообщения.
  if CurPageID = wpLicense then
  begin
    WizardForm.LicenseAcceptedRadio.Checked := True;
    WizardForm.NextButton.Enabled := True;
    WizardForm.NextButton.OnClick(WizardForm.NextButton);
    exit;
  end;

  if CurPageID = wpPreparing then
  begin
    IsInstallStarted := True;
    CloseLabel.Visible := False;
    SetPrimaryButton('Подготавливаю…', False);
    SetInstallerState('Подготавливаю установку', 'Проверяю свободное место и завершаю старую версию');
  end
  else if CurPageID = wpInstalling then
  begin
    IsInstallStarted := True;
    CloseLabel.Visible := False;
    SetPrimaryButton('Устанавливаю…', False);
    SetInstallerState('Устанавливаю Egoist Voice', 'Распаковываю приложение, модели и системные компоненты');
  end
  else if CurPageID = wpFinished then
  begin
    IsInstallFinished := True;
    SetProgress(100, 100);
    SetInstallerState('Готово', 'Egoist Voice установлен и уже запускается в фоне');
    SetPrimaryButton('Готово', True);
    CloseLabel.Visible := False;
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  SetProgress(CurProgress, MaxProgress);
end;

function IsSafeAppDirectory: Boolean;
var
  AppPath: String;
  ProgramsPath: String;
begin
  AppPath := AddBackslash(ExpandConstant('{app}'));
  ProgramsPath := AddBackslash(ExpandConstant('{localappdata}\Programs'));
  Result := (Length(AppPath) > Length(ProgramsPath)) and
    (CompareText(Copy(AppPath, 1, Length(ProgramsPath)), ProgramsPath) = 0);
end;

procedure CleanupLegacyState;
begin
  RegDeleteValue(HKCU, RunKey, 'EgoistVoice');
  RegDeleteValue(HKCU, RunKey, 'Egoist Voice');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Egoist Voice');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\EgoistVoice');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\EGOIST\Egoist Voice');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\EGOIST\EgoistVoice');
  DelTree(ExpandConstant('{localappdata}\EgoistVoice\Models\Language'), True, True, True);
  DelTree(ExpandConstant('{localappdata}\EgoistVoice\Models\Qwen'), True, True, True);
  DelTree(ExpandConstant('{localappdata}\EgoistVoice\Models\LLM'), True, True, True);
  DelTree(ExpandConstant('{localappdata}\EgoistVoice\Models\Text'), True, True, True);
  DelTree(ExpandConstant('{localappdata}\EgoistVoice\Data'), True, True, True);
  DelTree(ExpandConstant('{localappdata}\EgoistVoice\Temp'), True, True, True);
end;

function JsonEscape(Value: String): String;
var
  Index: Integer;
  CodeUnit: Integer;
begin
  { SaveStringToFile is available in the pinned Inno compiler. Keep the file
    byte-safe ASCII by emitting every non-ASCII UTF-16 code unit as JSON \uXXXX. }
  Result := '';
  for Index := 1 to Length(Value) do
  begin
    CodeUnit := Ord(Value[Index]);
    if Value[Index] = '\' then
      Result := Result + '\\'
    else if Value[Index] = '"' then
      Result := Result + '\"'
    else if (CodeUnit < 32) or (CodeUnit > 126) then
      Result := Result + Format('\u%.4x', [CodeUnit])
    else
      Result := Result + Value[Index];
  end;
end;

function SharedEngineOwnerPath: String;
begin
  Result := ExpandConstant('{localappdata}\EGOIST\TranslationEngine\owners\egoist-voice.owner.json');
end;

function GetUtcTimestamp: String;
var
  SystemTime: TEgoistSystemTime;
begin
  GetSystemTime(SystemTime);
  Result := Format('%.4d-%.2d-%.2dT%.2d:%.2d:%.2dZ', [
    SystemTime.Year,
    SystemTime.Month,
    SystemTime.Day,
    SystemTime.Hour,
    SystemTime.Minute,
    SystemTime.Second]);
end;

procedure RegisterSharedEngineOwner;
var
  OwnerRoot: String;
  OwnerJson: String;
  ClaimedUtc: String;
begin
  OwnerRoot := ExtractFileDir(SharedEngineOwnerPath);
  if not ForceDirectories(OwnerRoot) then
    RaiseException('Не удалось зарегистрировать Egoist Voice как владельца общего движка.');

  ClaimedUtc := GetUtcTimestamp;
  OwnerJson :=
    '{"schemaVersion":1,' +
    '"ownerId":"egoist-voice",' +
    '"ownerVersion":"{#MyAppVersion}",' +
    '"ownerInstallPath":"' + JsonEscape(ExpandConstant('{app}')) + '",' +
    '"ownerUninstallKey":"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{79A42D80-A0E3-45CA-BBBC-E6B2E48EBBE2}_is1",' +
    '"contractVersion":"v1",' +
    '"minEngineVersion":"1.0.0",' +
    '"claimedUtc":"' + ClaimedUtc + '"}';

  if not SaveStringToFile(SharedEngineOwnerPath, OwnerJson, False) then
    RaiseException('Не удалось записать owner-файл общего движка.');
end;

procedure RemoveSharedEngineOwner;
begin
  { Удаляем только собственный owner-файл. Host, модель и чужие owners не трогаем. }
  DeleteFile(SharedEngineOwnerPath);
end;

function StopRunningApplication: Boolean;
var
  ResultCode: Integer;
  ApplicationPath: String;
begin
  Result := True;
  ApplicationPath := ExpandConstant('{app}\{#MyAppExeName}');
  if FileExists(ApplicationPath) then
  begin
    Result := Exec(ApplicationPath, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
      (ResultCode = 0);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if StopRunningApplication then
    Result := ''
  else
    Result := 'Не удалось корректно завершить Egoist Voice. Закройте приложение и повторите установку.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    CleanupLegacyState
  else if CurStep = ssPostInstall then
    RegisterSharedEngineOwner;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    if not StopRunningApplication then
      RaiseException('Не удалось корректно завершить Egoist Voice. Закройте приложение и повторите удаление.');
    RemoveSharedEngineOwner;
    CleanupLegacyState;
  end;
end;
