$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param([string]$FilePath,[string[]]$CommandArgs = @())
    & $FilePath @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE." }
}

& "$PSScriptRoot/test-business-gate.ps1"
& "$PSScriptRoot/check-business-gate.ps1"
& "$PSScriptRoot/check-source-size.ps1"
& "$PSScriptRoot/test-module-layer-boundaries.ps1"
& "$PSScriptRoot/check-architecture.ps1"
& "$PSScriptRoot/check-placeholders.ps1"
& "$PSScriptRoot/check-work-item-docs.ps1"
& "$PSScriptRoot/verify-server-tls-certificate.ps1"
& "$PSScriptRoot/verify-trust-server-certificate.ps1"
& "$PSScriptRoot/publish-deployment-payloads.ps1" -ValidateOnly

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -ne $dotnetCommand -and -not [string]::IsNullOrWhiteSpace($dotnetCommand.Source)) {
    $dotnetPath = $dotnetCommand.Source
}
else {
    $dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
}
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "The required dotnet SDK executable was not found on PATH or under Program Files."
}
Write-Host "Using dotnet: $dotnetPath"

Invoke-Checked $dotnetPath @("--info")
Invoke-Checked $dotnetPath @("tool","restore")
Invoke-Checked $dotnetPath @("restore","GameNet.slnx")

Write-Host "Scanning restored direct and transitive NuGet packages for known vulnerabilities."
$vulnerabilityOutput = & $dotnetPath @(
    "package", "list", "--project", "GameNet.slnx",
    "--vulnerable", "--include-transitive", "--format", "json", "--output-version", "1", "--no-restore"
)
if ($LASTEXITCODE -ne 0) {
    throw "NuGet vulnerability scan failed; refusing to accept an unverified dependency graph."
}

try {
    $vulnerabilityReport = ($vulnerabilityOutput -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100
}
catch {
    throw "NuGet vulnerability scan returned invalid JSON; refusing to accept an unverified dependency graph."
}

if ($vulnerabilityReport.version -ne 1 -or @($vulnerabilityReport.projects).Count -eq 0) {
    throw "NuGet vulnerability scan returned an unsupported or incomplete report; refusing to accept an unverified dependency graph."
}
if ($vulnerabilityReport.PSObject.Properties.Name -contains "problems") {
    $scanProblems = @()
    if ($null -ne $vulnerabilityReport.problems) {
        $scanProblems = @($vulnerabilityReport.problems)
    }
    if ($scanProblems.Count -gt 0) {
        $scanProblems | ConvertTo-Json -Depth 10 | Write-Host
        throw "NuGet vulnerability scan reported source or analysis problems; refusing to accept an unverified dependency graph."
    }
}

$projectReports = @($vulnerabilityReport.projects)
foreach ($project in $projectReports) {
    $projectProperties = @($project.PSObject.Properties.Name)
    if ($projectProperties -contains "frameworks") {
        continue
    }

    # With --vulnerable, the version-1 JSON format can emit a project with only
    # its path when that project has no vulnerability findings. Treat this as an
    # empty finding set only for the exact path-only shape; any other missing or
    # unexpected structure remains a hard failure.
    if ($projectProperties.Count -ne 1 -or
        $projectProperties[0] -ne "path" -or
        [string]::IsNullOrWhiteSpace([string]$project.path)) {
        $projectProperties | ConvertTo-Json -Depth 4 | Write-Host
        throw "NuGet vulnerability report contains an unsupported project shape; refusing to accept an unverified dependency graph."
    }
}

$vulnerablePackages = @()
foreach ($project in $projectReports) {
    if (@($project.PSObject.Properties.Name) -notcontains "frameworks") {
        continue
    }
    foreach ($framework in @($project.frameworks)) {
        foreach ($packageCollectionName in @("topLevelPackages", "transitivePackages")) {
            foreach ($package in @($framework.$packageCollectionName)) {
                if ($null -eq $package) {
                    throw "NuGet vulnerability report contains an empty package entry; refusing to accept an unverified dependency graph."
                }
                if (@($package.PSObject.Properties.Name) -notcontains "vulnerabilities") {
                    throw "NuGet vulnerability report omitted package vulnerability details; refusing to accept an unverified dependency graph."
                }
                $packageVulnerabilities = @($package.vulnerabilities)
                if ($packageVulnerabilities.Count -eq 0) {
                    throw "NuGet vulnerability report returned a package without findings under --vulnerable; refusing to accept an unverified dependency graph."
                }
                foreach ($vulnerability in $packageVulnerabilities) {
                    if ($null -eq $vulnerability -or [string]::IsNullOrWhiteSpace([string]$vulnerability.severity)) {
                        throw "NuGet vulnerability report contains a finding without severity; refusing to accept an unverified dependency graph."
                    }
                    if ([string]::IsNullOrWhiteSpace([string]$vulnerability.advisoryurl)) {
                        throw "NuGet vulnerability report contains a finding without an advisory URL; refusing to accept an unverified dependency graph."
                    }

                    $vulnerablePackages += [pscustomobject]@{
                        Project = $project.path
                        Framework = $framework.framework
                        Package = $package.id
                        ResolvedVersion = $package.resolvedVersion
                        Severity = $vulnerability.severity
                        AdvisoryUrl = $vulnerability.advisoryurl
                    }
                }
            }
        }
    }
}

if ($vulnerablePackages.Count -gt 0) {
    $vulnerablePackages |
        Sort-Object Severity, Package, ResolvedVersion |
        Format-Table Project, Framework, Package, ResolvedVersion, Severity, AdvisoryUrl -AutoSize |
        Out-String |
        Write-Host
    throw "Known vulnerable NuGet packages were detected. Update affected dependencies; do not suppress NuGet Audit."
}

Write-Host "DEPENDENCY_VULNERABILITY_SCAN_PASSED=true"
Invoke-Checked $dotnetPath @("build","GameNet.slnx","--configuration","Release","--no-restore")
Invoke-Checked $dotnetPath @("test","GameNet.slnx","--configuration","Release","--no-build","--no-restore")
Write-Host "FOUNDATION BUILD/TEST VERIFICATION PASSED."
