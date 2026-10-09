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
& "$PSScriptRoot/check-architecture.ps1"
& "$PSScriptRoot/check-placeholders.ps1"
& "$PSScriptRoot/check-work-item-docs.ps1"

$dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}
Write-Host "Using dotnet: $dotnetPath"

Invoke-Checked $dotnetPath @("--info")
Invoke-Checked $dotnetPath @("tool","restore")
Invoke-Checked $dotnetPath @("restore","GameNet.slnx")
Invoke-Checked $dotnetPath @("build","GameNet.slnx","--configuration","Release","--no-restore")
Invoke-Checked $dotnetPath @("test","GameNet.slnx","--configuration","Release","--no-build","--no-restore")
Write-Host "FOUNDATION BUILD/TEST VERIFICATION PASSED."
