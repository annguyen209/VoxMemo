[Setup]
AppName=VoxMemo
AppVersion=1.5.0
AppPublisher=AnsCodeLab
AppPublisherURL=https://github.com/AnsCodeLab/VoxMemo
DefaultDirName={autopf}\VoxMemo
DefaultGroupName=VoxMemo
OutputDir=publish
OutputBaseFilename=VoxMemo-v1.5.0-Setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=Assets\voxmemo-icon.ico
UninstallDisplayIcon={app}\VoxMemo.exe
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
; Start Menu shortcut with AppUserModelID for modern toast notifications
Name: "{group}\VoxMemo"; Filename: "{app}\VoxMemo.exe"; AppUserModelID: "AnsCodeLab.VoxMemo"
Name: "{group}\Uninstall VoxMemo"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VoxMemo"; Filename: "{app}\VoxMemo.exe"; Tasks: desktopicon; AppUserModelID: "AnsCodeLab.VoxMemo"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\VoxMemo.exe"; Description: "Launch VoxMemo"; Flags: nowait postinstall skipifsilent
