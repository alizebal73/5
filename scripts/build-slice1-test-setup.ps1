[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDirectory = "artifacts/test-setup/out",
    [string]$InnoSetupCompilerPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-CheckedDotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit $LASTEXITCODE): $($Arguments -join ' ')"
    }
}

function Write-Utf8File {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Text)
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must be a safe preview version such as 0.1.0-test.123.1."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourceSha = ((& git -C $repoRoot rev-parse --verify HEAD 2>$null) -join "").Trim()
if ($LASTEXITCODE -ne 0 -or $sourceSha -notmatch '^[0-9a-f]{40}$') {
    throw "Could not determine the exact source SHA for the test installer."
}
if (-not [string]::IsNullOrWhiteSpace($env:GAMENET_EXPECTED_SOURCE_SHA) -and
    $sourceSha -ne $env:GAMENET_EXPECTED_SOURCE_SHA) {
    throw "The test installer source SHA does not match the exact expected candidate."
}

$repoDirty = @(& git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0 -or $repoDirty.Count -gt 0) {
    throw "Refusing to package a dirty source checkout."
}

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
if (Test-Path -LiteralPath $outputRoot) {
    throw "Output directory already exists; choose a clean artifact output path instead of overwriting evidence."
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$shortSha = $sourceSha.Substring(0, 12)
$workBase = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$workRoot = Join-Path $workBase ("gamenet5-slice1-test-setup-" + $shortSha)
if (Test-Path -LiteralPath $workRoot) {
    throw "Temporary staging directory already exists; refusing to overwrite it: $workRoot"
}
$payloadRoot = Join-Path $workRoot "payload"
New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null

try {
    $serverOutput = Join-Path $payloadRoot "Server"
    $agentOutput = Join-Path $payloadRoot "Agent"
    $desktopOutput = Join-Path $payloadRoot "Desktop"
    $launcherOutput = Join-Path $payloadRoot "Launcher"
    $toolsOutput = Join-Path $payloadRoot "Tools"
    $powerShellOutput = Join-Path $payloadRoot "Setup\PowerShell"
    New-Item -ItemType Directory -Force -Path $serverOutput, $agentOutput, $desktopOutput, $launcherOutput, $toolsOutput, $powerShellOutput | Out-Null

    Invoke-CheckedDotNet @("publish", "src/Server/GameNet.Server.csproj", "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "--no-restore", "-p:PublishSingleFile=false", "--output", $serverOutput)
    Invoke-CheckedDotNet @("publish", "src/Client/GameNet.Agent.csproj", "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "--no-restore", "-p:PublishSingleFile=false", "--output", $agentOutput)
    Invoke-CheckedDotNet @("publish", "src/Desktop/GameNet.Desktop.csproj", "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "--no-restore", "-p:PublishSingleFile=false", "--output", $desktopOutput)
    Invoke-CheckedDotNet @("publish", "tools/TestLauncher/GameNet5.TestLauncher.csproj", "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "--no-restore", "-p:PublishSingleFile=false", "--output", $launcherOutput)

    foreach ($component in @(
        @{ path = $serverOutput; file = "GameNet.Server.exe" },
        @{ path = $agentOutput; file = "GameNet.Agent.exe" },
        @{ path = $desktopOutput; file = "GameNet.Manager.Desktop.exe" },
        @{ path = $launcherOutput; file = "GameNet5.TestLauncher.exe" }
    )) {
        $required = Join-Path $component.path $component.file
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Published test component is incomplete: $($component.file)"
        }
    }

    $launcherExe = Join-Path $launcherOutput "GameNet5.TestLauncher.exe"
    & $launcherExe --self-test
    if ($LASTEXITCODE -ne 0) {
        throw "The GUI test launcher failed its DPAPI protected-settings self-test."
    }
    Write-Host "TEST_LAUNCHER_DPAPI_SELF_TEST=PASS"

    Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/run-slice1-process-acceptance.ps1") -Destination $toolsOutput
    Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/inspect-manager-runtime-state.ps1") -Destination $toolsOutput
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs/operations/slice1-process-acceptance.md") -Destination (Join-Path $toolsOutput "slice1-process-acceptance.md")
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs/operations/manual-cash-sale-reversal-acceptance.md") -Destination (Join-Path $toolsOutput "manual-cash-sale-reversal-acceptance.md")
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs/operations/manual-session-timing-recovery-acceptance.md") -Destination (Join-Path $toolsOutput "manual-session-timing-recovery-acceptance.md")

    # The installed test PCs do not need a separate PowerShell installation.
    $powerShellVersion = "7.6.4"
    $powerShellArchive = Join-Path $workRoot "PowerShell-7.6.4-win-x64.zip"
    $powerShellUrl = "https://github.com/PowerShell/PowerShell/releases/download/v7.6.4/PowerShell-7.6.4-win-x64.zip"
    $powerShellExpectedSha256 = "80832551c52809301e6071c8bac977beb5a2f1ec953eb4db9f94deb953333793"
    Invoke-WebRequest -Uri $powerShellUrl -OutFile $powerShellArchive
    $powerShellActualSha256 = (Get-FileHash -LiteralPath $powerShellArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($powerShellActualSha256 -ne $powerShellExpectedSha256) {
        throw "The bundled PowerShell archive did not match the pinned SHA-256."
    }
    Expand-Archive -LiteralPath $powerShellArchive -DestinationPath $powerShellOutput
    $pwsh = Join-Path $powerShellOutput "pwsh.exe"
    if (-not (Test-Path -LiteralPath $pwsh -PathType Leaf)) {
        throw "The bundled PowerShell archive did not contain pwsh.exe."
    }
    $bundledVersion = (& $pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $bundledVersion -ne $powerShellVersion) {
        throw "The bundled PowerShell runtime failed its version self-check."
    }
    Remove-Item -LiteralPath $powerShellArchive -Force

    # Record each installed payload file; manifest.json itself is intentionally excluded from its own hashes.
    $files = @(
        Get-ChildItem -LiteralPath $payloadRoot -Recurse -File |
            Sort-Object FullName |
            ForEach-Object {
                $relative = [System.IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace([string][char]92, "/")
                [ordered]@{
                    path = $relative
                    sizeBytes = $_.Length
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
    )
    if ($files.Count -eq 0) { throw "No test payload files were published." }

    $payloadManifest = [ordered]@{
        formatVersion = 1
        product = "GameNet 5"
        artifactType = "temporary-runtime-lan-smoke-payload"
        isInstaller = $false
        productionCertified = $false
        databaseIncluded = $false
        credentialsIncluded = $false
        target = [ordered]@{
            operatingSystem = "Windows"
            architecture = "x64"
            runtimeIdentifier = "win-x64"
            configuration = "Release"
            selfContained = $true
        }
        source = [ordered]@{
            repository = "alizebal73/5"
            testedCheckoutSha = $sourceSha
        }
        files = $files
    }
    $manifestPath = Join-Path $payloadRoot "manifest.json"
    Write-Utf8File -Path $manifestPath -Text (ConvertTo-Json -InputObject $payloadManifest -Depth 8)

    if ([string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) {
        $isccCandidates = [System.Collections.Generic.List[string]]::new()
        $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
        if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
            $isccCandidates.Add((Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"))
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $isccCandidates.Add((Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"))
        }
        $iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    else {
        $iscc = [System.IO.Path]::GetFullPath($InnoSetupCompilerPath)
    }
    if ([string]::IsNullOrWhiteSpace($iscc) -or -not (Test-Path -LiteralPath $iscc -PathType Leaf)) {
        throw "Pinned Inno Setup 6.4.3 compiler was not found at the supplied path or default compiler locations."
    }
    $probeRoot = Join-Path $workRoot "inno-compiler-version-probe"
    $probeOutput = Join-Path $probeRoot "output"
    $probeScript = Join-Path $probeRoot "compiler-probe.iss"
    New-Item -ItemType Directory -Force -Path $probeOutput | Out-Null
    $probeSource = @"
[Setup]
AppName=GameNet Inno Compiler Version Probe
AppVersion=1.0
DefaultDirName={tmp}\GameNetInnoCompilerVersionProbe
OutputDir=$probeOutput
OutputBaseFilename=InnoCompilerProbe
Uninstallable=no
"@
    Write-Utf8File -Path $probeScript -Text $probeSource
    $probeLog = @(& $iscc $probeScript 2>&1)
    $probeExitCode = $LASTEXITCODE
    $probeText = $probeLog -join [Environment]::NewLine
    if ($probeExitCode -ne 0 -or -not $probeText.Contains("Compiler engine version: Inno Setup 6.4.3")) {
        throw "The selected compiler did not prove Inno Setup 6.4.3. Refusing to package with an unverified compiler. Output: $probeText"
    }
    Write-Host "INNO_SETUP_PINNED_VERSION=6.4.3"
    $env:GAMENET_TEST_SETUP_PAYLOAD_ROOT = $payloadRoot
    $env:GAMENET_TEST_SETUP_OUTPUT_DIR = $outputRoot
    try {
        & $iscc "/DSetupVersion=$Version" (Join-Path $repoRoot "installer/Slice1-TestSetup.iss")
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
    }
    finally {
        Remove-Item Env:\GAMENET_TEST_SETUP_PAYLOAD_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:\GAMENET_TEST_SETUP_OUTPUT_DIR -ErrorAction SilentlyContinue
    }

    $setupPath = Join-Path $outputRoot "GameNet5-TestSetup.exe"
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        throw "Inno Setup did not create GameNet5-TestSetup.exe."
    }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $outputRoot "manifest.json")
    $setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $setupManifest = [ordered]@{
        formatVersion = 1
        product = "GameNet 5 Slice 1 Test Setup"
        purpose = "Issue #98 bounded PC Session-to-invoice process acceptance"
        source = [ordered]@{
            repository = "alizebal73/5"
            branch = if ([string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) { "local" } else { $env:GITHUB_REF_NAME }
            testedCheckoutSha = $sourceSha
        }
        safety = [ordered]@{
            productionRelease = $false
            registersWindowsServices = $false
            changesMachineProgramData = $false
            changesMachineCertificatesFirewallOrRegistry = $false
            changesSystemTimeOrLan = $false
            usesLocalLoopbackPostgreSqlOnly = $true
        }
        payloadManifest = [ordered]@{
            fileName = "manifest.json"
            sha256 = $manifestHash
            fileCount = $files.Count
        }
        installer = [ordered]@{
            fileName = "GameNet5-TestSetup.exe"
            sizeBytes = (Get-Item -LiteralPath $setupPath).Length
            sha256 = $setupHash
        }
    }
    Write-Utf8File -Path (Join-Path $outputRoot "setup-manifest.json") -Text (ConvertTo-Json -InputObject $setupManifest -Depth 8)
    $setupManifestHash = (Get-FileHash -LiteralPath (Join-Path $outputRoot "setup-manifest.json") -Algorithm SHA256).Hash.ToLowerInvariant()
    $sumLines = @(
        "$setupHash  GameNet5-TestSetup.exe"
        "$manifestHash  manifest.json"
        "$setupManifestHash  setup-manifest.json"
    )
    Write-Utf8File -Path (Join-Path $outputRoot "SHA256SUMS.txt") -Text (($sumLines -join [Environment]::NewLine) + [Environment]::NewLine)

    Write-Host "TEST_SETUP_READY=true"
    Write-Host "TESTED_SOURCE_SHA=$sourceSha"
    Write-Host "SETUP_EXE=$setupPath"
    Write-Host "SETUP_EXE_SHA256=$setupHash"
    Write-Host "PAYLOAD_FILE_COUNT=$($files.Count)"
    Write-Host "TEST_SETUP_IS_PRODUCTION=false"
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
