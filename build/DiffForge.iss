; Instalador de DiffForge (Inno Setup 6). Requiere haber corrido antes build/publish.ps1.
; Compilar:  iscc build\DiffForge.iss   →  dist\DiffForge-<versión>-setup.exe
; Instala por usuario (sin pedir administrador), crea acceso en el menú Inicio, asocia
; .dfsession y agrega "Comparar con DiffForge" al menú contextual del Explorador.

#define AppName "DiffForge"
#define AppExe "DiffForge.exe"
#define AppVersion GetVersionNumbersString("..\dist\win-x64\DiffForge.exe")

[Setup]
AppId={{5C7B0E2A-9F3D-4A61-9B7E-2D4F6A1C8E35}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Baltyr
AppPublisherURL=https://github.com/Baltyr/DiffForge
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\src\DiffForge.App\Assets\diffforge.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked
Name: "contextmenu"; Description: "Agregar ""Comparar con DiffForge"" al menú contextual del Explorador"

[Files]
Source: "..\dist\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Sesiones .dfsession
Root: HKCU; Subkey: "Software\Classes\.dfsession"; ValueType: string; ValueData: "DiffForge.Session"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\DiffForge.Session"; ValueType: string; ValueData: "Sesión de DiffForge"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DiffForge.Session\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExe},0"
Root: HKCU; Subkey: "Software\Classes\DiffForge.Session\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
; Menú contextual: abre el archivo en el lado izquierdo de una comparación nueva
Root: HKCU; Subkey: "Software\Classes\*\shell\DiffForge"; ValueType: string; ValueData: "Comparar con DiffForge"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\DiffForge"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe},0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\*\shell\DiffForge\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: contextmenu

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir DiffForge"; Flags: nowait postinstall skipifsilent
