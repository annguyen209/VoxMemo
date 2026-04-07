[Setup]
AppName=VoxMemo
AppVersion=1.0.0
AppPublisher=Anzdev4life
AppPublisherURL=https://github.com/annguyen209/VoxMemo
DefaultDirName={autopf}\VoxMemo
DefaultGroupName=VoxMemo
OutputDir=publish
OutputBaseFilename=VoxMemo-v1.0.0-Setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=Assets\voxmemo-icon.ico
UninstallDisplayIcon={app}\VoxMemo.exe
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\VoxMemo"; Filename: "{app}\VoxMemo.exe"
Name: "{group}\Uninstall VoxMemo"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VoxMemo"; Filename: "{app}\VoxMemo.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\VoxMemo.exe"; Description: "Launch VoxMemo"; Flags: nowait postinstall skipifsilent
