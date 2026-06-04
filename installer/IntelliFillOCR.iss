#define AppName "IntelliFill OCR"
#define AppExeName "IntelliFillOCR.exe"
#ifndef AppVersion
#define AppVersion "3.0.1"
#endif
#ifndef SourceDir
#define SourceDir "..\dist\IntelliFillOCR"
#endif
#ifndef OutputDir
#define OutputDir "out"
#endif
#ifndef PrerequisitesFile
#define PrerequisitesFile "prerequisites.txt"
#endif
#ifndef IconFile
#define IconFile "..\assets\app.ico"
#endif
#define TesseractInstallerUrl "https://github.com/tesseract-ocr/tesseract/releases/download/5.5.0/tesseract-ocr-w64-setup-5.5.0.20241111.exe"
#define TesseractInstallerName "tesseract-ocr-w64-setup-5.5.0.20241111.exe"
#define TesseractInstallerSize 21381872
#define TesseractInstallerHash "f3fc4236425b690c8be756f35793f77394ee004be0a6460a440c754d892f68bc"

[Setup]
AppId={{2F63263B-67BA-4FD4-9FA9-3E72F3328970}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=IntelliFill OCR
AppPublisherURL=https://github.com/Abijspy/intellifill-ocr
AppSupportURL=https://github.com/Abijspy/intellifill-ocr/issues
AppUpdatesURL=https://github.com/Abijspy/intellifill-ocr/releases
AppContact=https://github.com/Abijspy/intellifill-ocr/issues
AppComments=Offline OCR table filling desktop application
AppCopyright=Copyright (C) IntelliFill OCR
MinVersion=6.1
DefaultDirName={autopf}\IntelliFill OCR
DefaultGroupName=IntelliFill OCR
DisableProgramGroupPage=yes
InfoBeforeFile={#PrerequisitesFile}
OutputDir={#OutputDir}
OutputBaseFilename=IntelliFillOCR-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
SetupIconFile={#IconFile}
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
UsePreviousSetupType=yes
UsePreviousTasks=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
AlwaysShowComponentsList=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany=IntelliFill OCR
VersionInfoDescription=Offline OCR table filling desktop application
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
SignToolRetryCount=3
#endif
#ifdef InstallerPassword
Password={#InstallerPassword}
Encryption=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Minimal installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "main"; Description: "IntelliFill OCR application files"; Types: full compact custom; Flags: fixed
Name: "tesseract"; Description: "Download and install Tesseract OCR 5.5.0 (optional, internet required)"; Types: full custom; Check: ShouldOfferTesseractInstall

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: main
Source: "{#TesseractInstallerUrl}"; DestDir: "{tmp}"; DestName: "{#TesseractInstallerName}"; ExternalSize: {#TesseractInstallerSize}; Hash: "{#TesseractInstallerHash}"; Flags: external download ignoreversion deleteafterinstall; Components: tesseract; Check: ShouldOfferTesseractInstall

[Icons]
Name: "{group}\IntelliFill OCR"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "IntelliFillOCR.Desktop"
Name: "{autodesktop}\IntelliFill OCR"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "IntelliFillOCR.Desktop"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\IntelliFillOCR"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekeyifempty; Check: IsPerUserInstall
Root: HKCU; Subkey: "Software\IntelliFillOCR"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"; Flags: uninsdeletekeyifempty; Check: IsPerUserInstall
Root: HKCU; Subkey: "Software\IntelliFillOCR"; ValueType: string; ValueName: "InstallMode"; ValueData: "{code:GetInstallModeName}"; Flags: uninsdeletekeyifempty; Check: IsPerUserInstall
Root: HKLM; Subkey: "Software\IntelliFillOCR"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekeyifempty; Check: IsAllUsersInstall
Root: HKLM; Subkey: "Software\IntelliFillOCR"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"; Flags: uninsdeletekeyifempty; Check: IsAllUsersInstall
Root: HKLM; Subkey: "Software\IntelliFillOCR"; ValueType: string; ValueName: "InstallMode"; ValueData: "{code:GetInstallModeName}"; Flags: uninsdeletekeyifempty; Check: IsAllUsersInstall

[INI]
Filename: "{app}\install.ini"; Section: "Install"; Key: "Application"; String: "{#AppName}"
Filename: "{app}\install.ini"; Section: "Install"; Key: "Version"; String: "{#AppVersion}"
Filename: "{app}\install.ini"; Section: "Install"; Key: "InstallMode"; String: "{code:GetInstallModeName}"
Filename: "{app}\install.ini"; Section: "Dependencies"; Key: "TesseractInstaller"; String: "{#TesseractInstallerName}"

[Run]
Filename: "{tmp}\{#TesseractInstallerName}"; Description: "Install Tesseract OCR 5.5.0"; StatusMsg: "Running Tesseract OCR setup..."; Flags: waituntilterminated skipifdoesntexist skipifsilent; Components: tesseract; Check: ShouldOfferTesseractInstall
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,IntelliFill OCR}"; StatusMsg: "Starting IntelliFill OCR..."; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\install.ini"

[Code]
var
  InstallOutputHeader: TNewStaticText;
  InstallOutputMemo: TNewMemo;
  LastInstallerMessage: String;

function IsTesseractInstalled: Boolean;
var
  InstallDir: String;
begin
  Result :=
    FileExists(ExpandConstant('{pf}\Tesseract-OCR\tesseract.exe')) or
    FileExists(ExpandConstant('{pf32}\Tesseract-OCR\tesseract.exe')) or
    FileExists(ExpandConstant('{localappdata}\Programs\Tesseract-OCR\tesseract.exe'));

  if (not Result) and RegQueryStringValue(HKLM, 'Software\Tesseract-OCR', 'InstallDir', InstallDir) then
    Result := FileExists(AddBackslash(InstallDir) + 'tesseract.exe');

  if (not Result) and RegQueryStringValue(HKCU, 'Software\Tesseract-OCR', 'InstallDir', InstallDir) then
    Result := FileExists(AddBackslash(InstallDir) + 'tesseract.exe');
end;

function ShouldOfferTesseractInstall: Boolean;
begin
  Result := (not WizardSilent) and (not IsTesseractInstalled);
end;

function IsPerUserInstall: Boolean;
begin
  Result := not IsAdminInstallMode;
end;

function IsAllUsersInstall: Boolean;
begin
  Result := IsAdminInstallMode;
end;

function GetInstallModeName(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := 'AllUsers'
  else
    Result := 'CurrentUser';
end;

procedure AppendInstallerOutput(Message: String);
begin
  if WizardSilent or (InstallOutputMemo = nil) then
    Exit;

  InstallOutputMemo.Lines.Add(Message);
  InstallOutputMemo.SelStart := Length(InstallOutputMemo.Text);
end;

procedure ShowInstallerStatus(Message: String);
begin
  if Message = LastInstallerMessage then begin
    if not WizardSilent then
      WizardForm.StatusLabel.Caption := Message;
    Exit;
  end;

  LastInstallerMessage := Message;
  Log(Message);

  if not WizardSilent then begin
    WizardForm.StatusLabel.Caption := Message;
    AppendInstallerOutput(Message);
  end;
end;

procedure CreateInstallerOutputWindow();
var
  DetailsTop: Integer;
begin
  if WizardSilent then
    Exit;

  DetailsTop := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(12);

  InstallOutputHeader := TNewStaticText.Create(WizardForm);
  InstallOutputHeader.Parent := WizardForm.InstallingPage;
  InstallOutputHeader.Left := WizardForm.ProgressGauge.Left;
  InstallOutputHeader.Top := DetailsTop;
  InstallOutputHeader.Width := WizardForm.ProgressGauge.Width;
  InstallOutputHeader.Caption := 'Installation details';

  InstallOutputMemo := TNewMemo.Create(WizardForm);
  InstallOutputMemo.Parent := WizardForm.InstallingPage;
  InstallOutputMemo.Left := WizardForm.ProgressGauge.Left;
  InstallOutputMemo.Top := InstallOutputHeader.Top + InstallOutputHeader.Height + ScaleY(4);
  InstallOutputMemo.Width := WizardForm.ProgressGauge.Width;
  InstallOutputMemo.Height := WizardForm.InstallingPage.Height - InstallOutputMemo.Top - ScaleY(8);
  InstallOutputMemo.ReadOnly := True;
  InstallOutputMemo.ScrollBars := ssVertical;
  InstallOutputMemo.WordWrap := True;
end;

procedure InitializeWizard();
begin
  CreateInstallerOutputWindow();
  ShowInstallerStatus('Installer initialized. Install mode: ' + GetInstallModeName(''));
  if ShouldOfferTesseractInstall then
    ShowInstallerStatus('Tesseract OCR was not detected. Optional Tesseract install component is available.')
  else if IsTesseractInstalled then
    ShowInstallerStatus('Tesseract OCR was detected. Optional Tesseract install component is hidden.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then begin
    if WizardIsComponentSelected('tesseract') then
      ShowInstallerStatus('Installing IntelliFill OCR files and preparing optional Tesseract OCR setup...')
    else
      ShowInstallerStatus('Installing IntelliFill OCR application files...');
  end else if CurStep = ssPostInstall then begin
    ShowInstallerStatus('Finalizing shortcuts, registry entries, INI metadata, and selected post-install actions...');
  end else if CurStep = ssDone then begin
    Log('IntelliFill OCR installation completed.');
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if CurProgress = 0 then
    ShowInstallerStatus('Preparing selected installer operations...')
  else if WizardIsComponentSelected('tesseract') and (CurProgress > (MaxProgress div 2)) then
    ShowInstallerStatus('Downloading and verifying Tesseract OCR installer if required...')
  else
    ShowInstallerStatus('Copying IntelliFill OCR files, shortcuts, registry entries, and install metadata...');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    Log('Removing IntelliFill OCR files, shortcuts, registry entries, and install metadata...');
  end else if CurUninstallStep = usPostUninstall then begin
    Log('Finalizing IntelliFill OCR uninstall...');
  end else if CurUninstallStep = usDone then begin
    Log('IntelliFill OCR uninstall completed.');
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '';
  if MemoDirInfo <> '' then
    Result := Result + MemoDirInfo + NewLine + NewLine;
  if MemoTypeInfo <> '' then
    Result := Result + MemoTypeInfo + NewLine + NewLine;
  if MemoComponentsInfo <> '' then
    Result := Result + MemoComponentsInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then
    Result := Result + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + MemoTasksInfo + NewLine + NewLine;

  Result := Result + 'Operations:' + NewLine;
  Result := Result + Space + 'Install IntelliFill OCR application files' + NewLine;
  Result := Result + Space + 'Create Start Menu and optional desktop shortcuts' + NewLine;
  Result := Result + Space + 'Write uninstall, registry, and install.ini metadata' + NewLine;

  if WizardIsComponentSelected('tesseract') then begin
    Result := Result + Space + 'Download, verify, and run Tesseract OCR 5.5.0 setup' + NewLine;
    Result := Result + Space + 'Tesseract download URL: {#TesseractInstallerUrl}' + NewLine;
  end else if IsTesseractInstalled then begin
    Result := Result + Space + 'Tesseract OCR already detected; optional setup will be skipped' + NewLine;
  end else begin
    Result := Result + Space + 'Tesseract OCR not selected; configure it later from Actions > Settings' + NewLine;
  end;
end;

