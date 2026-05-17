#define AppName "IntelliFill OCR"
#define AppExeName "IntelliFillOCR.exe"
#ifndef AppVersion
#define AppVersion "2.2.1"
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
InfoBeforeFile={#PrerequisitesFile}
OutputDir={#OutputDir}
OutputBaseFilename=IntelliFillOCR-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile={#IconFile}
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
Name: "{group}\IntelliFill OCR"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "IntelliFillOCR.Desktop"
Name: "{autodesktop}\IntelliFill OCR"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "IntelliFillOCR.Desktop"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,IntelliFill OCR}"; Flags: nowait postinstall skipifsilent
