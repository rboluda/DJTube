; Instalador de DJTube (Inno Setup 6). Lo genera build/publish.ps1, que pasa AppVersion, SourceDir y OutputDir.
; Se instala por usuario (sin permisos de administrador) en %LOCALAPPDATA%\Programs\DJTube:
; así "Actualizar yt-dlp" puede escribir en su carpeta tools.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\DJTube"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{8F3C2A1E-6B7D-4C59-9E21-5D0A7B3C4E61}
AppName=DJTube
AppVersion={#AppVersion}
AppVerName=DJTube {#AppVersion}
AppPublisher=Raúl Boluda
AppCopyright=© 2026 Raúl Boluda
AppPublisherURL=https://github.com/rboluda/DJTube
AppSupportURL=https://github.com/rboluda/DJTube/issues
AppUpdatesURL=https://github.com/rboluda/DJTube/releases
DefaultDirName={autopf}\DJTube
DefaultGroupName=DJTube
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=DJTube-Setup-{#AppVersion}
SetupIconFile=..\src\DJTube.App\Assets\djtube.ico
UninstallDisplayIcon={app}\DJTube.exe
UninstallDisplayName=DJTube
VersionInfoVersion={#AppVersion}
VersionInfoProductName=DJTube
VersionInfoCompany=Raúl Boluda
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 o superior (lo exige la interfaz).
MinVersion=10.0.19041
CloseApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\DJTube"; Filename: "{app}\DJTube.exe"
Name: "{autodesktop}\DJTube"; Filename: "{app}\DJTube.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DJTube.exe"; Description: "{cm:LaunchProgram,DJTube}"; Flags: nowait postinstall skipifsilent
; Actualización automática desde DJTube (instalador en modo silencioso): volver a abrir el programa.
Filename: "{app}\DJTube.exe"; Flags: nowait postinstall skipifnotsilent
