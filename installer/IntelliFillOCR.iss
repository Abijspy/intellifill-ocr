#define AppName "IntelliFill OCR"
#define AppExeName "IntelliFillOCR.exe"
#ifndef AppVersion
#define AppVersion "1.1.0"
#endif
#ifndef SourceDir
#define SourceDir "..\dist\IntelliFillOCR"
#endif
#ifndef OutputDir
#define OutputDir "out"
#endif

[Setup]
AppId={{2F63263B-67BA-4FD4-9FA9-3E72F3328970}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=IntelliFill OCR
AppPublisherURL=https://github.com/Abijspy/intellifill-ocr
AppSupportURL=https://github.com/Abijspy/intellifill-ocr/issues
AppUpdatesURL=https://github.com/Abijspy/intellifill-ocr/releases
DefaultDirName={localappdata}\Programs\IntelliFill OCR
DefaultGroupName=IntelliFill OCR
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=IntelliFillOCR-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany=IntelliFill OCR
VersionInfoDescription=Offline OCR table filling desktop application
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\IntelliFill OCR"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\IntelliFill OCR"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,IntelliFill OCR}"; Flags: nowait postinstall skipifsilent
