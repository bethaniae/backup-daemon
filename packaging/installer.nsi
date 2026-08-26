!include "MUI2.nsh"

Name "Backup Manager"
OutFile "BackupManager-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\BackupManager"
RequestExecutionLevel user
SetCompressor /SOLID lzma

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "publish/win-x64"
!endif

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}"

  CreateDirectory "$SMPROGRAMS"
  CreateShortcut "$SMPROGRAMS\BackupManager.lnk" "$INSTDIR\BackupManager.exe"
  CreateShortcut "$DESKTOP\BackupManager.lnk" "$INSTDIR\BackupManager.exe"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
  RMDir /r "$INSTDIR"
  Delete "$SMPROGRAMS\BackupManager.lnk"
  Delete "$DESKTOP\BackupManager.lnk"
SectionEnd
