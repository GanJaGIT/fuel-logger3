; Установщик FuelLog (Inno Setup 6). Сначала выполните build.ps1 — он соберёт программу в папку publish.
#ifndef AppVersion
  #define AppVersion "4.1.0"
#endif

[Setup]
AppId={{6F2B8C41-5E7A-4F0B-9C55-2D7E4B1A9F30}
AppName=FuelLog
AppVersion={#AppVersion}
AppVerName=FuelLog {#AppVersion}
AppPublisher=Ганжа А.А. ВА РВСН
DefaultDirName={autopf}\FuelLog
DefaultGroupName=FuelLog
UninstallDisplayIcon={app}\FuelLog.exe
OutputDir=..\dist
OutputBaseFilename=FuelLog_{#AppVersion}_Setup
SetupIconFile=..\src\FuelLog.App\Assets\fuel.ico
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes
; Программа не требует сети и ничего не загружает.

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"
Name: "fileassociation"; Description: "Открывать файлы проектов (*.gsmproject) в FuelLog"; GroupDescription: "Дополнительно:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\FuelLog"; Filename: "{app}\FuelLog.exe"
Name: "{group}\Удалить FuelLog"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FuelLog"; Filename: "{app}\FuelLog.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.gsmproject"; ValueType: string; ValueName: ""; ValueData: "FuelLog.Project"; Flags: uninsdeletevalue; Tasks: fileassociation
Root: HKA; Subkey: "Software\Classes\FuelLog.Project"; ValueType: string; ValueName: ""; ValueData: "Проект FuelLog"; Flags: uninsdeletekey; Tasks: fileassociation
Root: HKA; Subkey: "Software\Classes\FuelLog.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\FuelLog.exe,0"; Tasks: fileassociation
Root: HKA; Subkey: "Software\Classes\FuelLog.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FuelLog.exe"" ""%1"""; Tasks: fileassociation

[Run]
Filename: "{app}\FuelLog.exe"; Description: "Запустить FuelLog"; Flags: postinstall nowait skipifsilent
