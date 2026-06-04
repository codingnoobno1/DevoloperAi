[Setup]
AppName=Syncro Desktop
AppVersion=1.0.0
DefaultDirName={autopf}\SyncroDesktop
DefaultGroupName=Syncro Desktop
OutputBaseFilename=SyncroSetup
Compression=lzma
SolidCompression=yes

[Files]
Source: "bin\Release\net9.0-windows10.0.19041.0\win10-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\Syncro Desktop"; Filename: "{app}\Syncro.Desktop.exe"

[Run]
Filename: "{app}\Syncro.Desktop.exe"; Description: "Launch Syncro"; Flags: nowait postinstall skipifsilent
