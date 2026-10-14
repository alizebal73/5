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
$projectsWithoutFrameworks = @($projectReports | Where-Object {
    $null -eq $_ -or $_.PSObject.Properties.Name -notcontains "frameworks"
})
if ($projectsWithoutFrameworks.Count -gt 0) {
    $shapeDiagnostics = [pscustomobject]@{
        ReportVersion = $vulnerabilityReport.version
        RootProperties = @($vulnerabilityReport.PSObject.Properties.Name)
        ProjectCount = $projectReports.Count
        ProjectsWithoutFrameworks = @($projectsWithoutFrameworks | Select-Object -First 12 | ForEach-Object {
            [pscustomobject]@{
                Path = if ($_.PSObject.Properties.Name -contains "path") { [string]$_.path } else { $null }
                Properties = @($_.PSObject.Properties.Name)
            }
        })
    }
    $shapeDiagnostics | ConvertTo-Json -Depth 8 | Write-Host
    throw "NuGet vulnerability report contains project entries without framework details; refusing to accept an unverified dependency graph."
}

$vulnerablePackages = @()
foreach ($project in $projectReports) {
    foreach ($framework in @($project.frameworks)) {
        foreach ($packageCollectionName in @("topLevelPackages", "transitivePackages")) {
            foreach ($package in @($framework.$packageCollectionName)) {
                foreach ($vulnerability in @($package.vulnerabilities)) {
                    if ($null -eq $vulnerability -or [string]::IsNullOrWhiteSpace([string]$vulnerability.severity)) {
                        continue
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
