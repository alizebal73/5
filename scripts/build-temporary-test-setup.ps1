[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDirectory = "artifacts/test-setup/out"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter()][string[]]$Arguments = @())
    & $Path @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Packaging command failed with exit code $LASTEXITCODE." }
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must be a safe preview version such as 0.1.0-test.123.1."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourceCommit = (& git -C $repoRoot rev-parse --verify HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Could not determine the exact source commit."
}
$dirty = @(& git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) {
    throw "Refusing to package a dirty working tree."
}
$shortCommit = $sourceCommit.Substring(0, 12)
$deploymentRoot = Join-Path $repoRoot ("artifacts/deployment/GameNet-5-{0}-{1}-win-x64" -f $Version, $shortCommit)
$deploymentPayload = Join-Path $deploymentRoot "payload"
Invoke-Checked -Path (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe") -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
    (Join-Path $repoRoot "scripts/publish-deployment-payloads.ps1"), "-Version", $Version
)

foreach ($required in @(
    (Join-Path $deploymentPayload "Server\GameNet.Server.exe"),
    (Join-Path $deploymentPayload "Desktop\GameNet.Manager.Desktop.exe"),
    (Join-Path $deploymentPayload "Agent\GameNet.Agent.exe"),
    (Join-Path $deploymentPayload "release-manifest.json")
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "The canonical publisher did not create a required component payload."
    }
}

$sourceManifest = Get-Content -LiteralPath (Join-Path $deploymentPayload "release-manifest.json") -Raw | ConvertFrom-Json -Depth 12
if ($sourceManifest.source.commit -ne $sourceCommit -or $sourceManifest.installerReady -ne $false) {
    throw "The component payload manifest is not bound to this exact source SHA or its expected payload-only status."
}

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
if (Test-Path -LiteralPath $outputRoot) {
    throw "Setup output directory already exists. Use a new clean output path rather than overwriting evidence."
}

$workRoot = Join-Path $repoRoot ("artifacts/test-setup/work-" + $shortCommit)
if (Test-Path -LiteralPath $workRoot) {
    throw "Temporary Setup staging folder already exists; refusing to overwrite it."
}
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
$payloadRoot = Join-Path $workRoot "payload"
$scriptsRoot = Join-Path $payloadRoot "Scripts"
$setupRoot = Join-Path $payloadRoot "Setup"
New-Item -ItemType Directory -Force -Path $payloadRoot, $scriptsRoot, $setupRoot | Out-Null

foreach ($component in @("Server", "Desktop", "Agent")) {
    Copy-Item -LiteralPath (Join-Path $deploymentPayload $component) -Destination $payloadRoot -Recurse
}
Copy-Item -LiteralPath (Join-Path $deploymentPayload "release-manifest.json") -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "installer/Install-TestRuntime.ps1") -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "installer/Remove-TestRuntime.ps1") -Destination $payloadRoot

$scriptNames = @(
    "configure-server-tls.ps1",
    "trust-server-certificate.ps1",
    "configure-runtime-endpoint.ps1",
    "write-protected-server-settings.ps1",
    "finalize-server-setup.ps1",
    "bootstrap-admin.ps1",
    "configure-agent-runtime-state.ps1",
    "provision-agent-enrollment.ps1",
    "test-runtime-endpoint.ps1"
)
foreach ($scriptName in $scriptNames) {
    $scriptPath = Join-Path (Join-Path $repoRoot "scripts") $scriptName
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "A required test Setup helper is missing."
    }
    Copy-Item -LiteralPath $scriptPath -Destination $scriptsRoot
}
New-Item -ItemType Directory -Force -Path (Join-Path $scriptsRoot "modules") | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/modules/ServerTlsCertificate.psm1") -Destination (Join-Path $scriptsRoot "modules")

$readmeText = @'
GAMENET 5 — TEMPORARY ISOLATED TEST SETUP
========================================

This is a temporary test installer, not a production release. Use it only on fresh, dedicated and isolated Windows x64 test PCs/VMs. Never run it on the live GameNet Manager/shop PC or point it at a real business database.

WHAT YOU NEED
- Windows x64 with built-in Windows PowerShell 5.1. Python and a separate .NET SDK are not required on the test PCs.
- PostgreSQL 17 already installed and running on the Manager/Server test PC. This temporary installer does not install PostgreSQL.
- Two clean, isolated Windows test PCs/VMs on the same test LAN: one Manager/Server + Desktop, and one dedicated Agent client.
- Administrator access on both test PCs and a local PostgreSQL administrator password on the Manager test PC.

INSTALL
1. Start this Setup on the Manager/Server test PC and select "Manager / Server + Desktop".
2. Confirm the isolated-test-machine warning. Select the reserved IPv4 address of that PC.
3. Enter the local PostgreSQL administrator name, port and password. Setup creates a new, randomly named gamenet_test_* database and a restricted gamenet_test_app_* login. The generated connection string is passed to the test-only Server secret provisioner over standard input; it is not put in a command line or written into the installer manifest.
4. Enter an initial Owner bootstrap secret of at least 32 characters when prompted. Remember it temporarily; you will re-enter it while creating the first Owner account.
5. Wait for the database migration and HTTPS Server readiness check. Create the initial Owner from the prompted bootstrap helper, then open Start Menu > GameNet Manager (TEST) and sign in. Return to the Setup console and press Enter only after sign-in works.
6. Setup finalizes the one-time bootstrap setting, restarts the Server and checks HTTPS readiness. It prints the Server URL, public certificate path and certificate SHA-256 fingerprint.

AGENT CLIENT
1. Start the same Setup file on a separate clean Agent PC and select "Dedicated Agent client".
2. Copy ONLY the public server-trust .cer file shown by Manager Setup to the Agent PC. Do not copy any private key or Server secret file.
3. Enter the exact HTTPS Server URL, the copied .cer path, and independently verify/paste the 64-character SHA-256 fingerprint shown on the Manager PC.
4. Choose a stable DeviceId such as station-pc-01. Use that same ID when binding the Agent to its PC Station in Manager Desktop.
5. Provide the authorized Owner/operator credentials when the Agent enrollment helper prompts. Setup never prints an enrollment token.
6. In Manager Desktop, verify the Agent is online/leased and bound to the correct test Station before starting a PC Session.

CASH JOURNEY ACCEPTANCE
Create a PC Station and a fixed hourly Tariff in Desktop. Bind the enrolled Agent to that Station. Then test customer/walk-in selection, Start Session, End Session, invoice preview, explicit received-cash amount, confirmation and final paid result. Independently reconcile the persisted Session, Invoice, Payment allocation, Ledger and Audit in the disposable test database. Also test ambiguous checkout response/restart and confirm no duplicate payment is created.

IMPORTANT LIMITS
- The installer refuses known existing GameNet/Manager services and existing %ProgramData%\GameNet Manager state. It does not upgrade or repair an existing install.
- Only the special test secret-provisioning path accepts loopback PostgreSQL with the required gamenet_test_* database and gamenet_test_app_* role naming, non-superuser permissions, an empty database, and pooling disabled.
- Migrations target only the new disposable local test database. Do not connect to a production/shop database.
- The setup configures HTTPS/TLS and checks it normally. It does not disable certificate validation.
- OS-level PC lock/unlock enforcement is not certified. A healthy Agent connection does not prove lock/unlock behavior.
- The package has no updater, repair, rollback certification or production release signature.
- If setup stops after creating Windows service/certificate/database state, do not improvise cleanup on a live machine. Preserve test evidence and revert the isolated VM snapshot.
- Uninstall can remove only services whose executable path matches this exact test install and its test-only firewall rule. ProgramData DPAPI files, the disposable database and certificates are deliberately retained for evidence safety. Revert the VM snapshot for full cleanup.

BUILD ID
The exact tested source SHA and component hashes are in Setup\release-manifest.json. The outer build artifact also includes setup-manifest.json and SHA256SUMS.txt.
'@
[System.IO.File]::WriteAllText(
    (Join-Path $payloadRoot "README-TestSetup.txt"),
    $readmeText,
    [System.Text.UTF8Encoding]::new($true)
)

# Parse every PowerShell helper that ships with this installer before compilation.
foreach ($script in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "installer") -File -Filter "*.ps1") + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "scripts") -Recurse -File | Where-Object { $_.Name -in $scriptNames -or $_.Name -eq "ServerTlsCertificate.psm1" })) {
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "PowerShell syntax error in a bundled setup helper: $($script.Name)"
    }
}

$programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
$isccCandidates = @(
    (Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw "Inno Setup 6 ISCC.exe was not found. Install the pinned Inno Setup compiler on the build machine."
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$env:GAMENET_TEST_SETUP_PAYLOAD_ROOT = $payloadRoot
$env:GAMENET_TEST_SETUP_OUTPUT_DIR = $outputRoot
try {
    $iss = Join-Path $repoRoot "installer/GameNet5-TestSetup.iss"
    Invoke-Checked -Path $iscc -Arguments @("/DSetupVersion=$Version", $iss)
}
finally {
    Remove-Item Env:GAMENET_TEST_SETUP_PAYLOAD_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:GAMENET_TEST_SETUP_OUTPUT_DIR -ErrorAction SilentlyContinue
}

$setupExe = Join-Path $outputRoot "GameNet5-TestSetup.exe"
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Inno Setup did not produce GameNet5-TestSetup.exe."
}
$setupHash = (Get-FileHash -LiteralPath $setupExe -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schemaVersion = 1
    product = "GameNet 5"
    artifactType = "temporary-isolated-test-installer"
    isInstaller = $true
    productionCertified = $false
    version = $Version
    source = [ordered]@{
        repository = "alizebal73/5"
        ref = ((& git -C $repoRoot branch --show-current 2>$null | Out-String).Trim())
        commit = $sourceCommit
    }
    build = [ordered]@{
        target = "Windows x64"
        selfContainedComponents = $true
        powershell = "Windows PowerShell 5.1 built into Windows"
        installerCompiler = "Inno Setup 6"
        builtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    prerequisites = @(
        "Two clean isolated Windows x64 test machines/VMs",
        "PostgreSQL 17 already installed and running on Manager/Server machine",
        "Administrator access on both test machines"
    )
    includedInstaller = [ordered]@{
        file = "GameNet5-TestSetup.exe"
        sha256 = $setupHash
        bytes = (Get-Item -LiteralPath $setupExe).Length
    }
    limitations = @(
        "Not a production installer",
        "No updater, repair or rollback certification",
        "No OS-level PC lock/unlock certification",
        "Uses only a dedicated empty disposable local PostgreSQL database"
    )
}
$manifestPath = Join-Path $outputRoot "setup-manifest.json"
[System.IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject $manifest -Depth 10) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLines = @(
    "$setupHash  GameNet5-TestSetup.exe",
    "$manifestHash  setup-manifest.json"
)
[System.IO.File]::WriteAllLines((Join-Path $outputRoot "SHA256SUMS.txt"), $checksumLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "TEMPORARY_TEST_SETUP_BUILD_PASSED=true"
Write-Host "SOURCE_COMMIT=$sourceCommit"
Write-Host "SETUP_EXE=$setupExe"
Write-Host "SETUP_SHA256=$setupHash"
Write-Host "PRODUCTION_CERTIFIED=false"
