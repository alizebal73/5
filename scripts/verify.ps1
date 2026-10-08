$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param([string]$FilePath,[string[]]$Args = @())
    & $FilePath @Args
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE." }
}

& "$PSScriptRoot/check-business-gate.ps1"
& "$PSScriptRoot/check-source-size.ps1"
& "$PSScriptRoot/check-architecture.ps1"
& "$PSScriptRoot/check-placeholders.ps1"
Invoke-Checked "dotnet" @("--info")
Invoke-Checked "dotnet" @("tool","restore")
Invoke-Checked "dotnet" @("restore","GameNet.slnx")
Invoke-Checked "dotnet" @("build","GameNet.slnx","--configuration","Release","--no-restore")
Invoke-Checked "dotnet" @("test","GameNet.slnx","--configuration","Release","--no-build","--no-restore")
Write-Host "FOUNDATION BUILD/TEST VERIFICATION PASSED."
