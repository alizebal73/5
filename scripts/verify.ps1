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
$dotnetCandidates = @(
    (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
    (Join-Path $env:ProgramW6432 "dotnet\dotnet.exe")
)
$dotnetPath = $dotnetCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
if (-not $dotnetPath) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}
Write-Host "DOTNET PATH: $dotnetPath"
Write-Host "DOTNET ROOT: $env:DOTNET_ROOT"
Write-Host "PROGRAM FILES: $env:ProgramFiles"
Write-Host "PROGRAM W6432: $env:ProgramW6432"
Write-Host "PATH: $env:PATH"
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "Resolved dotnet executable does not exist: $dotnetPath"
}
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dotnetPath)
Write-Host "DOTNET FILE VERSION: $($fileVersion.FileVersion)"
Write-Host "DOTNET PRODUCT VERSION: $($fileVersion.ProductVersion)"
Invoke-Checked $dotnetPath @("--info")
Invoke-Checked $dotnetPath @("tool","restore")
Invoke-Checked $dotnetPath @("restore","GameNet.slnx")
Invoke-Checked $dotnetPath @("build","GameNet.slnx","--configuration","Release","--no-restore")
Invoke-Checked $dotnetPath @("test","GameNet.slnx","--configuration","Release","--no-build","--no-restore")
Write-Host "FOUNDATION BUILD/TEST VERIFICATION PASSED."
