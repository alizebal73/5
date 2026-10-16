[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$InstallRoot)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$root = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$serverName = "GameNet 5 Server"
$agentName = "GameNet 5 Agent"
$serverExe = [System.IO.Path]::GetFullPath((Join-Path $root "Server\GameNet.Server.exe"))
$agentExe = [System.IO.Path]::GetFullPath((Join-Path $root "Agent\GameNet.Agent.exe"))
$sc = Join-Path $env:SystemRoot "System32\sc.exe"

function Get-ServiceExecutable {
    param([Parameter(Mandatory = $true)][string]$PathName)
    if ($PathName -match '^\s*"(?<path>[^"]+\.exe)"') {
        return [System.IO.Path]::GetFullPath($Matches.path)
    }
    if ($PathName -match '^\s*(?<path>.+?\.exe)(?:\s|$)') {
        return [System.IO.Path]::GetFullPath($Matches.path.Trim())
    }
    return $null
}

function Remove-OnlyOwnedService {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutable
    )
    $service = Get-CimInstance -ClassName Win32_Service |
        Where-Object { $_.Name -eq $Name -or $_.DisplayName -eq $Name } |
        Select-Object -First 1
    if ($null -eq $service) {
        return $false
    }

    $actual = Get-ServiceExecutable -PathName ([string]$service.PathName)
    if ([string]::IsNullOrWhiteSpace($actual) -or
        -not [string]::Equals($actual, $ExpectedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning "Service '$Name' no longer points to this exact test installation. It was left unchanged."
        return $false
    }

    if ($service.State -ne "Stopped") {
        try { Stop-Service -Name $service.Name -Force -ErrorAction Stop } catch {
            Write-Warning "Could not stop test service '$Name'. It was not deleted."
            return $false
        }
        try {
            $serviceController = Get-Service -Name $service.Name
            $serviceController.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(15))
        }
        catch {
            Write-Warning "Test service '$Name' did not stop cleanly. It was not deleted."
            return $false
        }
    }

    & $sc delete $service.Name | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Windows could not remove test service '$Name'."
        return $false
    }
    Write-Host "Removed the test service '$Name'."
    return $true
}

$ownsServerService = Remove-OnlyOwnedService -Name $serverName -ExpectedExecutable $serverExe
$null = Remove-OnlyOwnedService -Name $agentName -ExpectedExecutable $agentExe

if ($ownsServerService) {
    $rule = Get-NetFirewallRule -DisplayName "GameNet 5 Test Server HTTPS 5080" -ErrorAction SilentlyContinue
    if ($null -ne $rule) {
        Remove-NetFirewallRule -InputObject $rule
        Write-Host "Removed the test-only inbound firewall rule."
    }
}

Write-Host ""
Write-Host "Temporary application uninstall check finished."
Write-Host "For safety, the disposable database, ProgramData state, DPAPI files and test certificates were not automatically deleted."
Write-Host "The recommended cleanup is to revert the isolated VM snapshot after preserving the test evidence."
