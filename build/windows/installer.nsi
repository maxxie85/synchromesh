; Windows installer for Synchromesh. Built by build/windows/package.sh with makensis (NSIS 3), on Linux or
; Windows. Installs per user (no admin rights) into %LOCALAPPDATA%\Programs, adds a Start menu entry and an entry in
; Settings > Apps, and registers .torrent files and magnet: links (as default only when no other app has them).
;
; Defined by package.sh: VERSION (1.2.0-beta.1), VERSION_NUMERIC (1.2.0), ARCH (x64 | arm64), SOURCE (the published
; app folder), ICON (AppIcon.ico) and OUTFILE.

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"

!define APP_NAME "Synchromesh"
!define APP_ID "nl.vinch.synchromesh"
!define APP_EXE "Synchromesh.exe"
!define PUBLISHER "Max Vinken"
!define APP_KEY "Software\${APP_ID}"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
!define CAPABILITIES_KEY "${APP_KEY}\Capabilities"
!define TORRENT_PROGID "Synchromesh.torrent"
!define MAGNET_PROGID "Synchromesh.magnet"
; What the app registers itself for notifications (WindowsNotifier); removed on uninstall.
!define NOTIFICATION_KEY "Software\Classes\AppUserModelId\${APP_ID}"

Name "${APP_NAME}"
OutFile "${OUTFILE}"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "${APP_KEY}" "InstallDir"

VIProductVersion "${VERSION_NUMERIC}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION_NUMERIC}.0"
VIAddVersionKey "FileDescription" "${APP_NAME} setup"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 Max Vinken. MIT License."

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APP_NAME}"

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  SetShellVarContext current
  !if "${ARCH}" == "arm64"
    ${IfNot} ${IsNativeARM64}
      MessageBox MB_ICONSTOP "This installer is for Windows on ARM. Download the x64 installer for this PC."
      Abort
    ${EndIf}
  !else
    ; x64 runs natively on x64 and through emulation on Windows on ARM; not on 32-bit Windows.
    ${IfNot} ${IsNativeAMD64}
    ${AndIfNot} ${IsNativeARM64}
      MessageBox MB_ICONSTOP "${APP_NAME} needs 64-bit Windows."
      Abort
    ${EndIf}
  !endif
FunctionEnd

Function un.onInit
  SetShellVarContext current
FunctionEnd

Section "Install"

  ; An update replaces the previous version completely, so no old files linger. Only a folder that holds our own
  ; app and uninstaller is cleared, never a folder the user picked that holds other files.
  ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
  ${AndIf} ${FileExists} "$INSTDIR\uninstall.exe"
    RMDir /r "$INSTDIR"
  ${EndIf}

  SetOutPath "$INSTDIR"
  File /r "${SOURCE}/*"
  File "/oname=AppIcon.ico" "${ICON}"
  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "${APP_KEY}" "InstallDir" "$INSTDIR"

  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\AppIcon.ico" 0

  ; Settings > Apps
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\AppIcon.ico"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1

  ; .torrent files: our file type, offered in "Open with", default only when no app has the extension yet.
  WriteRegStr HKCU "Software\Classes\${TORRENT_PROGID}" "" "BitTorrent file"
  WriteRegStr HKCU "Software\Classes\${TORRENT_PROGID}\DefaultIcon" "" "$INSTDIR\AppIcon.ico"
  WriteRegStr HKCU "Software\Classes\${TORRENT_PROGID}\shell\open\command" "" '"$INSTDIR\${APP_EXE}" "%1"'
  WriteRegStr HKCU "Software\Classes\.torrent\OpenWithProgids" "${TORRENT_PROGID}" ""
  ReadRegStr $0 HKCR ".torrent" ""
  ${If} $0 == ""
    WriteRegStr HKCU "Software\Classes\.torrent" "" "${TORRENT_PROGID}"
  ${EndIf}

  ; magnet: links: our handler for Default Apps, and the magnet: key itself only when nothing handles it yet.
  WriteRegStr HKCU "Software\Classes\${MAGNET_PROGID}" "" "URL:Magnet link"
  WriteRegStr HKCU "Software\Classes\${MAGNET_PROGID}" "URL Protocol" ""
  WriteRegStr HKCU "Software\Classes\${MAGNET_PROGID}\DefaultIcon" "" "$INSTDIR\AppIcon.ico"
  WriteRegStr HKCU "Software\Classes\${MAGNET_PROGID}\shell\open\command" "" '"$INSTDIR\${APP_EXE}" "%1"'
  ReadRegStr $0 HKCR "magnet\shell\open\command" ""
  ${If} $0 == ""
    WriteRegStr HKCU "Software\Classes\magnet" "" "URL:Magnet link"
    WriteRegStr HKCU "Software\Classes\magnet" "URL Protocol" ""
    WriteRegStr HKCU "Software\Classes\magnet\DefaultIcon" "" "$INSTDIR\AppIcon.ico"
    WriteRegStr HKCU "Software\Classes\magnet\shell\open\command" "" '"$INSTDIR\${APP_EXE}" "%1"'
  ${EndIf}

  ; Default Apps: lets the user pick Synchromesh for .torrent and magnet: in Settings.
  WriteRegStr HKCU "${CAPABILITIES_KEY}" "ApplicationName" "${APP_NAME}"
  WriteRegStr HKCU "${CAPABILITIES_KEY}" "ApplicationDescription" "Remote control for Transmission servers"
  WriteRegStr HKCU "${CAPABILITIES_KEY}\FileAssociations" ".torrent" "${TORRENT_PROGID}"
  WriteRegStr HKCU "${CAPABILITIES_KEY}\URLAssociations" "magnet" "${MAGNET_PROGID}"
  WriteRegStr HKCU "Software\RegisteredApplications" "${APP_NAME}" "${CAPABILITIES_KEY}"

  ; Tell Explorer the associations changed.
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  RMDir /r "$INSTDIR"

  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegValue HKCU "Software\RegisteredApplications" "${APP_NAME}"
  DeleteRegKey HKCU "${APP_KEY}"
  DeleteRegKey HKCU "Software\Classes\${TORRENT_PROGID}"
  DeleteRegKey HKCU "Software\Classes\${MAGNET_PROGID}"
  DeleteRegValue HKCU "Software\Classes\.torrent\OpenWithProgids" "${TORRENT_PROGID}"
  ReadRegStr $0 HKCU "Software\Classes\.torrent" ""
  ${If} $0 == "${TORRENT_PROGID}"
    DeleteRegValue HKCU "Software\Classes\.torrent" ""
  ${EndIf}
  ; The magnet: key only if it still points at us.
  ReadRegStr $0 HKCU "Software\Classes\magnet\shell\open\command" ""
  ${If} $0 == '"$INSTDIR\${APP_EXE}" "%1"'
    DeleteRegKey HKCU "Software\Classes\magnet"
  ${EndIf}
  DeleteRegKey HKCU "${NOTIFICATION_KEY}"

  ; Settings and saved passwords (%APPDATA%\Synchromesh, Credential Manager) stay, as Windows apps usually do.
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd
