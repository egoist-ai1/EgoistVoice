#ifndef PayloadInclude
  #error PayloadInclude must be a verified explicit file list
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef AppVersion
  #define AppVersion "2.2.0"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "2.2.0.0"
#endif
#ifdef BundleTextEditor
  #define AppTitle "Egoist Voice"
  #define PackageName "EgoistVoice-Setup-Russian-" + AppVersion + "-win-x64-inner"
#else
  #define AppTitle "Egoist Voice Compact"
  #define PackageName "EgoistVoice-Setup-Compact-RU-" + AppVersion + "-win-x64"
#endif
#define AppExe "Egoist.Voice.exe"

[Setup]
AppId={{5F84E54F-BE2E-46BA-970C-D1A774D3D239}
AppName={#AppTitle}
AppVersion={#AppVersion}
AppVerName={#AppTitle} {#AppVersion}
AppPublisher=EGOIST
DefaultDirName={code:GetDefaultTargetDir}
DefaultGroupName=Egoist Voice Compact
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.18362
OutputDir={#OutputDir}
OutputBaseFilename={#PackageName}
SetupIconFile=..\assets\EgoistVoice.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppTitle}
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=yes
DisableReadyPage=yes
DisableFinishedPage=no
DisableStartupPrompt=yes
WizardResizable=no
ShowLanguageDialog=no
Compression=lzma2/normal
SolidCompression=yes
#ifdef BundleTextEditor
DiskSpanning=yes
DiskSliceSize=2100000000
SlicesPerDisk=1
#else
DiskSpanning=no
#endif
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
UninstallLogMode=append
VersionInfoVersion={#AppFileVersion}
VersionInfoCompany=EGOIST
VersionInfoDescription=Egoist Voice Compact RU - offline CPU
VersionInfoProductName={#AppTitle}
VersionInfoProductVersion={#AppFileVersion}
VersionInfoProductTextVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\assets\installer-microphone-52.bmp"; Flags: dontcopy
#include PayloadInclude

[Icons]
Name: "{autoprograms}\{#AppTitle}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppTitle}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Check: ShouldCreateDesktopIcon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EgoistVoice"; ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue; Check: ShouldAutoStart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppTitle}"; WorkingDir: "{app}"; Flags: nowait skipifsilent; Check: ShouldLaunchApp

[Code]
const
  BackgroundColor    = $00000000; // Black #000000
  SurfaceColor       = $00181414; // Card surface #141418
  InputBoxColor      = $00120F0F; // Input box #0F0F12
  BorderColor        = $002E2626; // Subtle border #26262E
  TrackColor         = $00282424; // Progress track / secondary button #242428
  TrackHoverColor    = $00363030; // Hover on secondary #303036
  PrimaryTextColor   = $00F6F5F5; // High-contrast white #F5F5F6
  SecondaryTextColor = $00A59EA0; // Muted silver-gray #A09EA5
  MutedTextColor     = $006C6666; // Subtle footnote gray #66666C
  AccentColor        = $004824FF; // Scarlet #FF2448
  AccentHoverColor   = $004A3CFF; // Hover crimson #FF3C4A

var
  BrandSurface: TPanel;
  HeaderIcon: TBitmapImage;
  TitleLabel: TNewStaticText;
  VersionLabel: TNewStaticText;
  CloseButton: TPanel;
  CloseLabel: TNewStaticText;
  
  OptionsPanel: TPanel;
  DirCaptionLabel: TNewStaticText;
  PathBox: TPanel;
  DirPathLabel: TNewStaticText;
  BrowseBtn: TPanel;
  BrowseBtnLabel: TNewStaticText;
  
  AutoStartCheck: TNewCheckBox;
  AutoStartText: TNewStaticText;
  DesktopIconCheck: TNewCheckBox;
  DesktopIconText: TNewStaticText;
  LaunchAppCheck: TNewCheckBox;
  LaunchAppText: TNewStaticText;
  
  StatusLabel: TNewStaticText;
  DetailLabel: TNewStaticText;
  PercentLabel: TNewStaticText;
  ProgressTrack: TPanel;
  ProgressFill: TPanel;
  
  PrimaryButton: TPanel;
  PrimaryButtonLabel: TNewStaticText;
  FootnoteLabel: TNewStaticText;
  
  IsInstallStarted: Boolean;
  IsInstallFinished: Boolean;
  TargetInstallDir: String;

function GetDefaultTargetDir(Param: String): String;
begin
  if TargetInstallDir = '' then
    TargetInstallDir := ExpandConstant('{localappdata}\Programs\Egoist Voice Compact');
  Result := TargetInstallDir;
end;

function CreateRoundRectRgn(Left, Top, Right, Bottom, Width, Height: Integer): Integer;
  external 'CreateRoundRectRgn@gdi32.dll stdcall';
function SetWindowRgn(Wnd, Rgn: Integer; Redraw: Boolean): Integer;
  external 'SetWindowRgn@user32.dll stdcall';

procedure ApplyRoundedPanel(PanelControl: TPanel; Radius: Integer);
var
  Region: Integer;
begin
  Region := CreateRoundRectRgn(0, 0, PanelControl.Width + 1, PanelControl.Height + 1, Radius, Radius);
  SetWindowRgn(PanelControl.Handle, Region, True);
end;

function CreateSurfaceLabel: TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  Result.AutoSize := False;
  Result.Parent := BrandSurface;
end;

procedure StyleLabel(LabelControl: TNewStaticText; FontSize: Integer; FontColor: TColor; Bold: Boolean);
begin
  LabelControl.AutoSize := False;
  LabelControl.Font.Name := 'Segoe UI';
  LabelControl.Font.Size := FontSize;
  LabelControl.Font.Color := FontColor;
  if Bold then
    LabelControl.Font.Style := [fsBold]
  else
    LabelControl.Font.Style := [];
end;

function ShouldCreateDesktopIcon: Boolean;
begin
  Result := DesktopIconCheck.Checked;
end;

function ShouldAutoStart: Boolean;
begin
  Result := AutoStartCheck.Checked;
end;

function ShouldLaunchApp: Boolean;
begin
  Result := LaunchAppCheck.Checked;
end;

procedure ToggleAutoStart(Sender: TObject);
begin
  AutoStartCheck.Checked := not AutoStartCheck.Checked;
end;

procedure ToggleDesktopIcon(Sender: TObject);
begin
  DesktopIconCheck.Checked := not DesktopIconCheck.Checked;
end;

procedure ToggleLaunchApp(Sender: TObject);
begin
  LaunchAppCheck.Checked := not LaunchAppCheck.Checked;
end;

procedure SetProgress(Current, Total: Integer);
var
  AvailableWidth: Integer;
  ProgressWidth: Integer;
  ScaleDivisor: Integer;
  ScaledCurrent: Integer;
  ScaledTotal: Integer;
  Percent: Integer;
begin
  AvailableWidth := ProgressTrack.ClientWidth;
  if (Total <= 0) or (Current <= 0) then
  begin
    ProgressWidth := 0;
    Percent := 0;
  end
  else
  begin
    ScaleDivisor := (Total div 1000000) + 1;
    ScaledCurrent := Current div ScaleDivisor;
    ScaledTotal := Total div ScaleDivisor;
    if ScaledTotal <= 0 then
      ScaledTotal := 1;
    if Current >= Total then
    begin
      ProgressWidth := AvailableWidth;
      Percent := 100;
    end
    else
    begin
      ProgressWidth := (AvailableWidth * ScaledCurrent) div ScaledTotal;
      Percent := (100 * ScaledCurrent) div ScaledTotal;
    end;
  end;
  if (Current > 0) and (ProgressWidth < ScaleX(4)) then
    ProgressWidth := ScaleX(4);
  if ProgressWidth > AvailableWidth then
    ProgressWidth := AvailableWidth;
  ProgressFill.Width := ProgressWidth;
  if (Total > 0) and (Current > 0) then
    PercentLabel.Caption := IntToStr(Percent) + '%'
  else
    PercentLabel.Caption := '';
end;

procedure SetPrimaryButton(ACaption: String; Enabled: Boolean; AColor: TColor);
begin
  PrimaryButtonLabel.Caption := ACaption;
  PrimaryButtonLabel.AutoSize := True;
  PrimaryButtonLabel.Left := (PrimaryButton.ClientWidth - PrimaryButtonLabel.Width) div 2;
  PrimaryButton.Color := AColor;
  if Enabled then
  begin
    PrimaryButtonLabel.Font.Color := PrimaryTextColor;
    PrimaryButton.Cursor := crHand;
    PrimaryButtonLabel.Cursor := crHand;
  end
  else
  begin
    PrimaryButtonLabel.Font.Color := MutedTextColor;
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
  CloseButton.Visible := False;
  OptionsPanel.Visible := False;
  
  SetPrimaryButton('Установка…', False, TrackColor);
  SetInstallerState('Установка Egoist Voice…', 'Распаковка компонентов приложения и нейросетей GigaAM…');
  SetProgress(1, 100);
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure CloseInstallerClick(Sender: TObject);
begin
  WizardForm.CancelButton.OnClick(WizardForm.CancelButton);
end;

procedure BrowseFolderClick(Sender: TObject);
var
  SelectedDir: String;
begin
  SelectedDir := GetDefaultTargetDir('');
  if BrowseForFolder('Выберите папку для установки {#AppTitle}:', SelectedDir, False) then
  begin
    TargetInstallDir := SelectedDir;
    DirPathLabel.Caption := SelectedDir;
    try
      WizardForm.DirEdit.Text := SelectedDir;
    except
    end;
  end;
end;

procedure CreateBrandShell;
var
  Radius: Integer;
  Region: Integer;
begin
  WizardForm.Caption := '{#AppTitle} {#AppVersion}';
  WizardForm.BorderStyle := bsNone;
  WizardForm.ClientWidth := ScaleX(600);
  WizardForm.ClientHeight := ScaleY(396);
  WizardForm.Color := BackgroundColor;
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.Font.Size := 9;
  WizardForm.Position := poScreenCenter;

  Radius := ScaleX(22);
  Region := CreateRoundRectRgn(0, 0, WizardForm.Width + 1, WizardForm.Height + 1, Radius, Radius);
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
  ApplyRoundedPanel(BrandSurface, ScaleX(20));

  // Top-right close button
  CloseButton := TPanel.Create(WizardForm);
  CloseButton.Parent := BrandSurface;
  CloseButton.Left := BrandSurface.Width - ScaleX(38);
  CloseButton.Top := ScaleY(12);
  CloseButton.Width := ScaleX(26);
  CloseButton.Height := ScaleY(26);
  CloseButton.Color := BackgroundColor;
  CloseButton.ParentBackground := False;
  CloseButton.BevelOuter := bvNone;
  CloseButton.Cursor := crHand;
  CloseButton.OnClick := @CloseInstallerClick;

  CloseLabel := TNewStaticText.Create(WizardForm);
  CloseLabel.Parent := CloseButton;
  CloseLabel.Left := ScaleX(7);
  CloseLabel.Top := ScaleY(3);
  CloseLabel.Caption := '✕';
  CloseLabel.Cursor := crHand;
  CloseLabel.OnClick := @CloseInstallerClick;
  StyleLabel(CloseLabel, 11, SecondaryTextColor, False);
  CloseLabel.AutoSize := True;

  ExtractTemporaryFile('installer-microphone-52.bmp');

  HeaderIcon := TBitmapImage.Create(WizardForm);
  HeaderIcon.Parent := BrandSurface;
  HeaderIcon.Left := ScaleX(28);
  HeaderIcon.Top := ScaleY(20);
  HeaderIcon.Width := ScaleX(48);
  HeaderIcon.Height := ScaleY(48);
  HeaderIcon.Stretch := True;
  HeaderIcon.Bitmap.LoadFromFile(ExpandConstant('{tmp}\installer-microphone-52.bmp'));

  TitleLabel := CreateSurfaceLabel;
  TitleLabel.Left := ScaleX(88);
  TitleLabel.Top := ScaleY(20);
  TitleLabel.Width := ScaleX(420);
  TitleLabel.Height := ScaleY(28);
  TitleLabel.Caption := '{#AppTitle}';
  StyleLabel(TitleLabel, 16, PrimaryTextColor, True);

  VersionLabel := CreateSurfaceLabel;
  VersionLabel.Left := ScaleX(89);
  VersionLabel.Top := ScaleY(48);
  VersionLabel.Width := ScaleX(420);
  VersionLabel.Height := ScaleY(20);
  VersionLabel.Caption := 'Автономная диктовка и распознавание речи · v{#AppVersion}';
  StyleLabel(VersionLabel, 9, SecondaryTextColor, False);

  // ── Card with Installation Options ──
  OptionsPanel := TPanel.Create(WizardForm);
  OptionsPanel.Parent := BrandSurface;
  OptionsPanel.Left := ScaleX(28);
  OptionsPanel.Top := ScaleY(86);
  OptionsPanel.Width := ScaleX(544);
  OptionsPanel.Height := ScaleY(154);
  OptionsPanel.Color := SurfaceColor;
  OptionsPanel.ParentBackground := False;
  OptionsPanel.BevelOuter := bvNone;
  ApplyRoundedPanel(OptionsPanel, ScaleX(14));

  DirCaptionLabel := TNewStaticText.Create(WizardForm);
  DirCaptionLabel.Parent := OptionsPanel;
  DirCaptionLabel.Left := ScaleX(16);
  DirCaptionLabel.Top := ScaleY(12);
  DirCaptionLabel.Width := ScaleX(200);
  DirCaptionLabel.Height := ScaleY(16);
  DirCaptionLabel.Caption := 'Папка установки:';
  StyleLabel(DirCaptionLabel, 8, SecondaryTextColor, False);

  // Path input container
  PathBox := TPanel.Create(WizardForm);
  PathBox.Parent := OptionsPanel;
  PathBox.Left := ScaleX(16);
  PathBox.Top := ScaleY(30);
  PathBox.Width := ScaleX(416);
  PathBox.Height := ScaleY(30);
  PathBox.Color := InputBoxColor;
  PathBox.ParentBackground := False;
  PathBox.BevelOuter := bvNone;
  ApplyRoundedPanel(PathBox, ScaleX(6));

  DirPathLabel := TNewStaticText.Create(WizardForm);
  DirPathLabel.Parent := PathBox;
  DirPathLabel.Left := ScaleX(10);
  DirPathLabel.Top := ScaleY(7);
  DirPathLabel.Width := ScaleX(396);
  DirPathLabel.Height := ScaleY(18);
  DirPathLabel.Caption := GetDefaultTargetDir('');
  DirPathLabel.ShowAccelChar := False;
  StyleLabel(DirPathLabel, 8, PrimaryTextColor, True);

  // Browse Button
  BrowseBtn := TPanel.Create(WizardForm);
  BrowseBtn.Parent := OptionsPanel;
  BrowseBtn.Left := OptionsPanel.Width - ScaleX(94);
  BrowseBtn.Top := ScaleY(30);
  BrowseBtn.Width := ScaleX(78);
  BrowseBtn.Height := ScaleY(30);
  BrowseBtn.Color := TrackColor;
  BrowseBtn.ParentBackground := False;
  BrowseBtn.BevelOuter := bvNone;
  BrowseBtn.Cursor := crHand;
  BrowseBtn.OnClick := @BrowseFolderClick;
  ApplyRoundedPanel(BrowseBtn, ScaleX(6));

  BrowseBtnLabel := TNewStaticText.Create(WizardForm);
  BrowseBtnLabel.Parent := BrowseBtn;
  BrowseBtnLabel.Left := ScaleX(14);
  BrowseBtnLabel.Top := ScaleY(6);
  BrowseBtnLabel.Width := BrowseBtn.Width;
  BrowseBtnLabel.Height := ScaleY(18);
  BrowseBtnLabel.Caption := 'Обзор…';
  BrowseBtnLabel.Cursor := crHand;
  BrowseBtnLabel.OnClick := @BrowseFolderClick;
  StyleLabel(BrowseBtnLabel, 9, PrimaryTextColor, False);

  // Checkbox 1
  AutoStartCheck := TNewCheckBox.Create(WizardForm);
  AutoStartCheck.Parent := OptionsPanel;
  AutoStartCheck.Left := ScaleX(16);
  AutoStartCheck.Top := ScaleY(70);
  AutoStartCheck.Width := ScaleX(20);
  AutoStartCheck.Height := ScaleY(20);
  AutoStartCheck.Caption := '';
  AutoStartCheck.Checked := True;

  AutoStartText := TNewStaticText.Create(WizardForm);
  AutoStartText.Parent := OptionsPanel;
  AutoStartText.Left := ScaleX(42);
  AutoStartText.Top := ScaleY(72);
  AutoStartText.Width := ScaleX(480);
  AutoStartText.Height := ScaleY(18);
  AutoStartText.Caption := 'Запускать вместе с Windows (автозапуск в трей)';
  AutoStartText.Cursor := crHand;
  AutoStartText.OnClick := @ToggleAutoStart;
  StyleLabel(AutoStartText, 9, PrimaryTextColor, False);

  // Checkbox 2
  DesktopIconCheck := TNewCheckBox.Create(WizardForm);
  DesktopIconCheck.Parent := OptionsPanel;
  DesktopIconCheck.Left := ScaleX(16);
  DesktopIconCheck.Top := ScaleY(96);
  DesktopIconCheck.Width := ScaleX(20);
  DesktopIconCheck.Height := ScaleY(20);
  DesktopIconCheck.Caption := '';
  DesktopIconCheck.Checked := True;

  DesktopIconText := TNewStaticText.Create(WizardForm);
  DesktopIconText.Parent := OptionsPanel;
  DesktopIconText.Left := ScaleX(42);
  DesktopIconText.Top := ScaleY(98);
  DesktopIconText.Width := ScaleX(480);
  DesktopIconText.Height := ScaleY(18);
  DesktopIconText.Caption := 'Создать ярлык на Рабочем столе';
  DesktopIconText.Cursor := crHand;
  DesktopIconText.OnClick := @ToggleDesktopIcon;
  StyleLabel(DesktopIconText, 9, PrimaryTextColor, False);

  // Checkbox 3
  LaunchAppCheck := TNewCheckBox.Create(WizardForm);
  LaunchAppCheck.Parent := OptionsPanel;
  LaunchAppCheck.Left := ScaleX(16);
  LaunchAppCheck.Top := ScaleY(122);
  LaunchAppCheck.Width := ScaleX(20);
  LaunchAppCheck.Height := ScaleY(20);
  LaunchAppCheck.Caption := '';
  LaunchAppCheck.Checked := True;

  LaunchAppText := TNewStaticText.Create(WizardForm);
  LaunchAppText.Parent := OptionsPanel;
  LaunchAppText.Left := ScaleX(42);
  LaunchAppText.Top := ScaleY(124);
  LaunchAppText.Width := ScaleX(480);
  LaunchAppText.Height := ScaleY(18);
  LaunchAppText.Caption := 'Запустить Egoist Voice после завершения установки';
  LaunchAppText.Cursor := crHand;
  LaunchAppText.OnClick := @ToggleLaunchApp;
  StyleLabel(LaunchAppText, 9, PrimaryTextColor, False);

  // Status and Progress Area
  StatusLabel := CreateSurfaceLabel;
  StatusLabel.Left := ScaleX(28);
  StatusLabel.Top := ScaleY(256);
  StatusLabel.Width := ScaleX(450);
  StatusLabel.Height := ScaleY(22);
  StatusLabel.Caption := 'Готово к установке';
  StyleLabel(StatusLabel, 10, PrimaryTextColor, True);

  DetailLabel := CreateSurfaceLabel;
  DetailLabel.Left := ScaleX(28);
  DetailLabel.Top := ScaleY(280);
  DetailLabel.Width := ScaleX(450);
  DetailLabel.Height := ScaleY(18);
#ifdef BundleTextEditor
  DetailLabel.Caption := 'Русская диктовка GigaAM и оформление Qwen включены';
#else
  DetailLabel.Caption := 'Все компоненты и нейросети GigaAM упакованы внутри';
#endif
  StyleLabel(DetailLabel, 9, SecondaryTextColor, False);

  PercentLabel := CreateSurfaceLabel;
  PercentLabel.Left := ScaleX(500);
  PercentLabel.Top := ScaleY(258);
  PercentLabel.Width := ScaleX(72);
  PercentLabel.Height := ScaleY(20);
  PercentLabel.Caption := '';
  StyleLabel(PercentLabel, 9, SecondaryTextColor, True);

  ProgressTrack := TPanel.Create(WizardForm);
  ProgressTrack.Parent := BrandSurface;
  ProgressTrack.Left := ScaleX(28);
  ProgressTrack.Top := ScaleY(304);
  ProgressTrack.Width := ScaleX(544);
  ProgressTrack.Height := ScaleY(5);
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

  // Bottom action bar
  FootnoteLabel := CreateSurfaceLabel;
  FootnoteLabel.Left := ScaleX(28);
  FootnoteLabel.Top := ScaleY(332);
  FootnoteLabel.Width := ScaleX(360);
  FootnoteLabel.Height := ScaleY(18);
  FootnoteLabel.Caption := '100% офлайн · Без интернета · Полная конфиденциальность';
  StyleLabel(FootnoteLabel, 8, MutedTextColor, False);

  PrimaryButton := TPanel.Create(WizardForm);
  PrimaryButton.Parent := BrandSurface;
  PrimaryButton.Left := BrandSurface.Width - ScaleX(188);
  PrimaryButton.Top := ScaleY(322);
  PrimaryButton.Width := ScaleX(160);
  PrimaryButton.Height := ScaleY(40);
  PrimaryButton.ParentBackground := False;
  PrimaryButton.BevelOuter := bvNone;
  PrimaryButton.OnClick := @StartInstallClick;
  ApplyRoundedPanel(PrimaryButton, ScaleX(10));

  PrimaryButtonLabel := TNewStaticText.Create(WizardForm);
  PrimaryButtonLabel.Parent := PrimaryButton;
  PrimaryButtonLabel.Left := 0;
  PrimaryButtonLabel.Top := ScaleY(10);
  PrimaryButtonLabel.Width := PrimaryButton.Width;
  PrimaryButtonLabel.Height := ScaleY(20);
  PrimaryButtonLabel.OnClick := @StartInstallClick;
  StyleLabel(PrimaryButtonLabel, 10, PrimaryTextColor, True);
  SetPrimaryButton('Установить', True, AccentColor);
end;

procedure InitializeWizard;
begin
  IsInstallStarted := False;
  IsInstallFinished := False;
  CreateBrandShell;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if CurPageID = wpInstalling then
    Cancel := False
  else
  begin
    Cancel := True;
    Confirm := False;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpPreparing then
  begin
    IsInstallStarted := True;
    CloseButton.Visible := False;
    OptionsPanel.Visible := False;
    SetPrimaryButton('Подготовка…', False, TrackColor);
    SetInstallerState('Подготовка к установке', 'Проверка свободного места и занятых файлов…');
    BrandSurface.Visible := False;
    WizardForm.OuterNotebook.Visible := True;
    WizardForm.InnerNotebook.Visible := True;
    WizardForm.NextButton.Visible := True;
    WizardForm.CancelButton.Visible := True;
    WizardForm.PreparingMemo.Color := BackgroundColor;
    WizardForm.PreparingMemo.Font.Color := PrimaryTextColor;
  end
  else if CurPageID = wpInstalling then
  begin
    BrandSurface.Visible := True;
    WizardForm.OuterNotebook.Visible := False;
    WizardForm.InnerNotebook.Visible := False;
    WizardForm.NextButton.Visible := False;
    WizardForm.CancelButton.Visible := False;
    IsInstallStarted := True;
    CloseButton.Visible := False;
    OptionsPanel.Visible := False;
    SetPrimaryButton('Установка…', False, TrackColor);
    SetInstallerState('Установка Egoist Voice', 'Распаковка компонентов и оптимизация моделей GigaAM…');
  end
  else if CurPageID = wpFinished then
  begin
    IsInstallFinished := True;
    SetProgress(100, 100);
    SetInstallerState('Установка завершена!', '{#AppTitle} установлен и готов к работе.');
    if ShouldLaunchApp then
      SetPrimaryButton('Запустить', True, AccentColor)
    else
      SetPrimaryButton('Готово', True, AccentColor);
    CloseButton.Visible := False;
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  SetProgress(CurProgress, MaxProgress);
end;
