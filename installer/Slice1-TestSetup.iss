#ifndef SetupVersion
  #define SetupVersion "0.1.0-test"
#endif
#define PayloadRoot GetEnv("GAMENET_TEST_SETUP_PAYLOAD_ROOT")
#define SetupOutputDir GetEnv("GAMENET_TEST_SETUP_OUTPUT_DIR")

[Setup]
AppId={{81F9DA17-1B30-4FD2-9BA1-7F3149DBE8F1}
AppName=GameNet 5 Slice 1 Test Launcher
AppVersion={#SetupVersion}
AppPublisher=GameNet 5 Test Build
DefaultDirName={localappdata}\Programs\GameNet 5 Test
DefaultGroupName=GameNet 5 Test
DisableProgramGroupPage=yes
DisableDirPage=no
DisableReadyPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
SetupLogging=yes
Uninstallable=yes
OutputDir={#SetupOutputDir}
OutputBaseFilename=GameNet5-TestSetup
VersionInfoVersion=0.1.0.0
VersionInfoProductVersion=0.1.0.0
VersionInfoDescription=Temporary loopback functional test launcher; not for production
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=GameNet 5 Slice 1 Test Launcher

[Dirs]
Name: "{app}"

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\GameNet 5 Test Launcher"; Filename: "{app}\Launcher\GameNet5.TestLauncher.exe"; WorkingDir: "{app}"; Comment: "Run the isolated GameNet 5 Slice 1 acceptance test"
Name: "{group}\GameNet 5 Test Launcher"; Filename: "{app}\Launcher\GameNet5.TestLauncher.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall GameNet 5 Test Launcher"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Launcher\GameNet5.TestLauncher.exe"; Description: "Launch GameNet 5 Test Launcher"; Flags: postinstall nowait skipifsilent
