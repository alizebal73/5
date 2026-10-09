[CmdletBinding()]
param(
    [Parameter()][string]$Version = "0.1.0-preview",
    [Parameter()][string]$OutputDirectory = "artifacts/deployment",
    [Parameter()][switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter()][string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Assert-File {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Deployment packaging prerequisite is missing: $Path"
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Version must be a safe SemVer-like value, for example 0.1.0-preview.123."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$requiredFiles = @(
    "GameNet.slnx",
    "src/Server/GameNet.Server.csproj",
    "src/Server/Program.cs",
    "src/Server/Infrastructure/Configuration/ProtectedServerSettings.cs",
    "src/Server/Persistence/GameNetDbContextFactory.cs",
    "src/Client/GameNet.Agent.csproj",
    "src/Client/Program.cs",
    "src/Client/appsettings.json",
    "src/Desktop/GameNet.Desktop.csproj",
    "src/Desktop/appsettings.json"
)

foreach ($relativePath in $requiredFiles) {
    Assert-File (Join-Path $repoRoot $relativePath)
}

$serverProgram = Get-Content -LiteralPath (Join-Path $repoRoot "src/Server/Program.cs") -Raw
$agentProgram = Get-Content -LiteralPath (Join-Path $repoRoot "src/Client/Program.cs") -Raw
if ($serverProgram -notmatch 'UseWindowsService' -or
    $serverProgram -notmatch '--migrate-only' -or
    $serverProgram -notmatch 'ProtectedServerSettings.LoadInto' -or
    $agentProgram -notmatch 'AddWindowsService') {
    throw "Server/Agent Windows Service hosting, protected settings, and explicit migration mode are required before packaging."
}

$migrationFolder = Join-Path $repoRoot "src/Server/Persistence/Migrations"
if (-not (Test-Path -LiteralPath $migrationFolder -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $migrationFolder -File -Filter *.cs | Where-Object { $_.Name -notlike "*Snapshot*" })) {
    throw "At least one versioned EF migration is required before Server payloads can be packaged."
}

$dotnetPath = Join-Path $env:ProgramFiles "dotnet/dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetPath = $dotnetCommand.Source
}
Assert-File $dotnetPath

if ($ValidateOnly) {
    Write-Host "Deployment packaging preflight passed: Server, Desktop, Agent, protected settings and migrations are present."
    return
}

$gitRoot = (& git -C $repoRoot rev-parse --show-toplevel 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [System.IO.Path]::GetFullPath($gitRoot) -ne $repoRoot) {
    throw "Packaging must run from a valid Git checkout."
}
$sourceCommit = (& git -C $repoRoot rev-parse --verify HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Could not resolve the exact source commit for the deployment manifest."
}
$dirtyState = @(& git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirtyState.Count -gt 0) {
    throw "Refusing to package a dirty working tree. Commit or discard changes first."
}

$sourceRef = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) {
    $env:GITHUB_REF_NAME
}
else {
    (& git -C $repoRoot branch --show-current 2>$null | Out-String).Trim()
}
$shortCommit = $sourceCommit.Substring(0, 12)
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
$buildRoot = Join-Path $outputRoot "GameNet-5-$Version-$shortCommit-win-x64"
if (Test-Path -LiteralPath $buildRoot) {
    throw "Output already exists; choose a clean output path rather than overwriting a prior build: $buildRoot"
}

$payloadRoot = Join-Path $buildRoot "payload"
$serverOutput = Join-Path $payloadRoot "Server"
$desktopOutput = Join-Path $payloadRoot "Desktop"
$agentOutput = Join-Path $payloadRoot "Agent"
$databaseOutput = Join-Path $payloadRoot "Database"
New-Item -ItemType Directory -Force -Path $serverOutput,$desktopOutput,$agentOutput,$databaseOutput | Out-Null

try {
    Push-Location $repoRoot
    try {
        Invoke-Checked $dotnetPath @("restore", "GameNet.slnx")
        $publishCommon = @(
            "--configuration", "Release",
            "--runtime", "win-x64",
            "--self-contained", "true",
            "-p:PublishSingleFile=false",
            "-p:UseAppHost=true",
            "-p:Version=$Version"
        )
        Invoke-Checked $dotnetPath (@("publish", "src/Server/GameNet.Server.csproj") + $publishCommon + @("--output", $serverOutput))
        Invoke-Checked $dotnetPath (@("publish", "src/Desktop/GameNet.Desktop.csproj") + $publishCommon + @("--output", $desktopOutput))
        Invoke-Checked $dotnetPath (@("publish", "src/Client/GameNet.Agent.csproj") + $publishCommon + @("--output", $agentOutput))
    }
    finally {
        Pop-Location
    }
}

$migrationInstructionsPath = Join-Path $databaseOutput "MIGRATION-MODE.txt"
$protectedSettingsPath = Join-Path $env:ProgramData "GameNet Manager\Config\server-secrets.bin"
$migrationInstructions = @"
Database deployment for GameNet 5
---------------------------------
Do not run migrations automatically during routine Server service startup.

After protected Server settings are provisioned, the DPAPI LocalMachine-protected file is expected at:
  $protectedSettingsPath

The Server configuration must set GameNet:ProtectedSettings:Enabled=true in
appsettings.Production.json and the protected settings file must have an ACL limited
to SYSTEM and Administrators. The package does not contain database or API secrets.

Run the Server executable explicitly as Administrator/service identity:
  GameNet.Server.exe --migrate-only

The command applies pending EF migrations and exits with a non-zero code on any
connection or migration failure. It does not start the HTTP listener.
"@
[System.IO.File]::WriteAllText($migrationInstructionsPath, $migrationInstructions, [System.Text.UTF8Encoding]::new($false))

$expectedOutputs = @(
    (Join-Path $serverOutput "GameNet.Server.exe"),
    (Join-Path $desktopOutput "GameNet.Manager.Desktop.exe"),
    (Join-Path $agentOutput "GameNet.Agent.exe")
)
foreach ($path in $expectedOutputs) {
    Assert-File $path
}

$noticePath = Join-Path $payloadRoot "PAYLOAD-NOT-SETUP.txt"
$notice = @"
GameNet 5 deployment payload
Source commit: $sourceCommit
Version: $Version
Runtime: win-x64

IMPORTANT: these ZIP files contain published payloads, not Setup.exe/MSI installers.
The installation/commissioning layer is a separate work item and must be completed and
tested before these files are installed on a customer machine.
Server and Agent secrets are intentionally not included. The Server must be configured
with a protected database connection, authentication signing key, provisioning key and
bootstrap secret. Each Agent must be provisioned with its own credential over HTTPS.
"@
[System.IO.File]::WriteAllText($noticePath, $notice, [System.Text.UTF8Encoding]::new($false))

# Fail closed if a secret-bearing configuration value has accidentally entered the payload.
$secretSettingPattern = '(?i)"(?:Password|Secret|ProvisioningKey|SigningKey|DatabaseConnectionString)"\s*:\s*"(?!null)[^"]+"'
foreach ($jsonFile in Get-ChildItem -LiteralPath $payloadRoot -Recurse -File -Filter *.json) {
    $jsonText = Get-Content -LiteralPath $jsonFile.FullName -Raw
    if ($jsonText -match $secretSettingPattern) {
        throw "Potential secret-bearing configuration was found in a published JSON file: $($jsonFile.FullName)"
    }
}

$payloadFiles = @(
    Get-ChildItem -LiteralPath $payloadRoot -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            [ordered]@{
                path = $relative
                sizeBytes = $_.Length
                sha256 = $hash
            }
        }
)

$manifest = [ordered]@{
    schemaVersion = 1
    product = "GameNet Manager 5"
    productVersion = $Version
    source = [ordered]@{
        repository = "alizebal73/5"
        ref = $sourceRef
        commit = $sourceCommit
    }
    build = [ordered]@{
        configuration = "Release"
        runtimeIdentifier = "win-x64"
        selfContained = $true
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        dotnetSdk = ((& $dotnetPath --version 2>&1 | Out-String).Trim())
    }
    components = @(
        [ordered]@{ name = "Server"; entryPoint = "Server/GameNet.Server.exe" },
        [ordered]@{ name = "Desktop"; entryPoint = "Desktop/GameNet.Manager.Desktop.exe" },
        [ordered]@{ name = "Agent"; entryPoint = "Agent/GameNet.Agent.exe" },
        [ordered]@{ name = "DatabaseMigrations"; entryPoint = "Server/GameNet.Server.exe --migrate-only" }
    )
    installerReady = $false
    installerStatus = "Payload-only; not an installer and not certified for end-user installation."
    files = $payloadFiles
}
$manifestPath = Join-Path $payloadRoot "release-manifest.json"
$manifestJson = ConvertTo-Json -InputObject $manifest -Depth 8
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

$serverDesktopZip = Join-Path $buildRoot "GameNet-5-$Version-Server-Desktop-payload-win-x64.zip"
$agentZip = Join-Path $buildRoot "GameNet-5-$Version-Agent-payload-win-x64.zip"
Compress-Archive -Path @(
    $serverOutput,
    $desktopOutput,
    $databaseOutput,
    $manifestPath,
    $noticePath
) -DestinationPath $serverDesktopZip -CompressionLevel Optimal
Compress-Archive -Path @(
    $agentOutput,
    $manifestPath,
    $noticePath
) -DestinationPath $agentZip -CompressionLevel Optimal

$checksumLines = @()
foreach ($file in @($manifestPath, $serverDesktopZip, $agentZip)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLines += "$hash  $([System.IO.Path]::GetFileName($file))"
}
$checksumPath = Join-Path $buildRoot "SHA256SUMS.txt"
[System.IO.File]::WriteAllLines($checksumPath, $checksumLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Deployment payload build passed."
Write-Host "Source commit: $sourceCommit"
Write-Host "Server + Desktop payload: $serverDesktopZip"
Write-Host "Agent payload: $agentZip"
Write-Host "Manifest: $manifestPath"
Write-Host "Notice: payload archives are not Setup.exe/MSI installers."
