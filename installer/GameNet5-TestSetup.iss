#ifndef SetupVersion
  #define SetupVersion "0.1.0-test"
#endif
#define PayloadRoot GetEnv("GAMENET_TEST_SETUP_PAYLOAD_ROOT")
#define SetupOutputDir GetEnv("GAMENET_TEST_SETUP_OUTPUT_DIR")

[Setup]
AppId={{97B7505B-9343-4A12-9F58-2E306B0E251A}
AppName=GameNet 5 Temporary Test Setup
AppVersion={#SetupVersion}
AppPublisher=GameNet 5 Test Build
DefaultDirName={autopf}\GameNet 5 Test
DefaultGroupName=GameNet 5 Test
DisableProgramGroupPage=yes
DisableDirPage=no
DisableReadyPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
SetupLogging=yes
Uninstallable=yes
OutputDir={#SetupOutputDir}
OutputBaseFilename=GameNet5-TestSetup
VersionInfoVersion=0.1.0.0
VersionInfoProductVersion=0.1.0.0
VersionInfoDescription=Temporary isolated test setup; not for production
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=GameNet 5 Temporary Test Setup

[Types]
Name: "manager"; Description: "Manager / Server + Desktop (first test PC)"
Name: "agent"; Description: "Dedicated Agent client (second test PC)"

[Components]
Name: "manager"; Description: "Manager / Server + Desktop"; Types: manager; Flags: fixed
Name: "agent"; Description: "Agent client"; Types: agent; Flags: fixed

[Dirs]
Name: "{app}\Setup"
Name: "{app}\Scripts"
Name: "{app}\artifacts"

[Files]
Source: "{#PayloadRoot}\Server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: manager
Source: "{#PayloadRoot}\Desktop\*"; DestDir: "{app}\Desktop"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: manager
Source: "{#PayloadRoot}\Agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: agent

Source: "{#PayloadRoot}\release-manifest.json"; DestDir: "{app}\Setup"; Flags: ignoreversion
Source: "{#PayloadRoot}\Setup\PowerShell\*"; DestDir: "{app}\Setup\PowerShell"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadRoot}\README-TestSetup.txt"; DestDir: "{app}\Setup"; Flags: ignoreversion
Source: "{#PayloadRoot}\Install-TestRuntime.ps1"; DestDir: "{app}\Setup"; Flags: ignoreversion
Source: "{#PayloadRoot}\Remove-TestRuntime.ps1"; DestDir: "{app}\Setup"; Flags: ignoreversion

Source: "..\scripts\configure-server-tls.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\trust-server-certificate.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\configure-runtime-endpoint.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\write-protected-server-settings.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\finalize-server-setup.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\bootstrap-admin.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\configure-agent-runtime-state.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\provision-agent-enrollment.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\test-runtime-endpoint.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\modules\ServerTlsCertificate.psm1"; DestDir: "{app}\Scripts\modules"; Flags: ignoreversion

[Icons]
Name: "{group}\GameNet Manager (TEST)"; Filename: "{app}\Desktop\GameNet.Manager.Desktop.exe"; Components: manager
Name: "{group}\Temporary Setup Instructions"; Filename: "{app}\Setup\README-TestSetup.txt"

[Run]
Filename: "{app}\Setup\PowerShell\pwsh.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup\Install-TestRuntime.ps1"" -InstallRoot ""{app}"" -Mode ""{code:GetSetupModeArg}"""; Flags: waituntilterminated

[UninstallRun]
Filename: "{app}\Setup\PowerShell\pwsh.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup\Remove-TestRuntime.ps1"" -InstallRoot ""{app}"""; Flags: runhidden waituntilterminated

[Code]
function GetSetupModeArg(Param: String): String;
begin
  if WizardIsComponentSelected('manager') then
    Result := 'Manager'
  else
    Result := 'Agent';
end;
