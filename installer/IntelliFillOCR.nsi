!include "MUI2.nsh"
!include "LogicLib.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "0.0.0"
!endif

!ifndef APP_FILE_VERSION
  !define APP_FILE_VERSION "0.0.0.0"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\release\avalonia-win-x64\publish"
!endif

!ifndef OUTPUT_EXE
  !define OUTPUT_EXE "out\IntelliFillOCR-${APP_VERSION}-setup-win-x64.exe"
!endif

!define APP_NAME "IntelliFill OCR"
!define APP_PUBLISHER "IntelliFill OCR"
!define APP_EXE "IntelliFillOCR.exe"
!define APP_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\IntelliFillOCR"
!define TESSERACT_URL "https://github.com/tesseract-ocr/tesseract/releases/download/5.5.0/tesseract-ocr-w64-setup-5.5.0.20241111.exe"

Name "${APP_NAME}"
OutFile "${OUTPUT_EXE}"
InstallDir "$LOCALAPPDATA\Programs\IntelliFill OCR"
InstallDirRegKey HKCU "${APP_REG_KEY}" "InstallLocation"
RequestExecutionLevel user
Unicode true
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${APP_FILE_VERSION}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "OriginalFilename" "IntelliFillOCR-${APP_VERSION}-setup-win-x64.exe"
VIAddVersionKey "LegalCopyright" "Copyright 2026 IntelliFill OCR"

!define MUI_ABORTWARNING
!define MUI_ICON "..\assets\app.ico"
!define MUI_UNICON "..\assets\app.ico"
!define MUI_WELCOMEPAGE_TITLE "Install ${APP_NAME}"
!define MUI_WELCOMEPAGE_TEXT "This wizard installs ${APP_NAME} ${APP_VERSION}.$\r$\n$\r$\nTesseract OCR is required for OCR extraction. You can install it separately, configure its path in Settings, or select the optional Tesseract component below."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "${APP_NAME} application" SecApp
  SectionIn RO
  DetailPrint "Stopping running IntelliFill OCR instances if needed..."
  nsExec::ExecToLog 'taskkill /IM "${APP_EXE}" /F'

  DetailPrint "Installing application files..."
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  DetailPrint "Creating shortcuts..."
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\Assets\app.ico" 0
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\Assets\app.ico" 0

  DetailPrint "Registering uninstaller..."
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${APP_REG_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${APP_REG_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${APP_REG_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${APP_REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${APP_REG_KEY}" "DisplayIcon" "$INSTDIR\Assets\app.ico"
  WriteRegStr HKCU "${APP_REG_KEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKCU "${APP_REG_KEY}" "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
  WriteRegDWORD HKCU "${APP_REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${APP_REG_KEY}" "NoRepair" 1

  DetailPrint "Setup complete. Update-package cleanup is handled after setup exits."
SectionEnd

Section /o "Install Tesseract OCR 5.5.0" SecTesseract
  StrCpy $0 "$TEMP\tesseract-ocr-w64-setup-5.5.0.exe"
  Delete "$0"

  DetailPrint "Downloading Tesseract OCR 5.5.0 with Windows PowerShell..."
  nsExec::ExecToLog 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $$ProgressPreference = ''SilentlyContinue''; Invoke-WebRequest -Uri ''${TESSERACT_URL}'' -OutFile ''$0'' -UseBasicParsing"'
  Pop $1

  ${If} $1 == 0
  ${AndIf} ${FileExists} "$0"
    DetailPrint "Running Tesseract OCR installer..."
    ExecWait '"$0" /S'
  ${Else}
    DetailPrint "Tesseract download failed. PowerShell exit code: $1"
    MessageBox MB_ICONEXCLAMATION "Tesseract OCR could not be downloaded. The app is still installed. Configure Tesseract manually from Settings after installation."
  ${EndIf}
SectionEnd

Section "Uninstall"
  DetailPrint "Stopping IntelliFill OCR..."
  nsExec::ExecToLog 'taskkill /IM "${APP_EXE}" /F'

  DetailPrint "Removing shortcuts..."
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DetailPrint "Removing application files..."
  RMDir /r "$INSTDIR"

  DetailPrint "Removing uninstall registry entries..."
  DeleteRegKey HKCU "${APP_REG_KEY}"
SectionEnd
