[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerBaseUrl,

    [ValidateSet("Desktop", "Agent", "Both")]
    [string]$Component = "Both"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$uri = $null
if (-not [Uri]::TryCreate($ServerBaseUrl, [UriKind]::Absolute, [ref]$uri)) {
    throw "ServerBaseUrl must be an absolute URL."
}

if (-not [string]::IsNullOrEmpty($uri.UserInfo) -or
    -not [string]::IsNullOrEmpty($uri.Query) -or
    -not [string]::IsNullOrEmpty($uri.Fragment) -or
    $uri.AbsolutePath -ne "/") {
    throw "ServerBaseUrl must contain only the scheme, host and optional port."
}

$isHttps = $uri.Scheme -ieq "https"
$isLoopbackHttp = ($uri.Scheme -ieq "http") -and $uri.IsLoopback
$isWildcard = $uri.Host -in @("0.0.0.0", "::", "[::]")
if ((-not $isHttps -and -not $isLoopbackHttp) -or ($isHttps -and $isWildcard)) {
    throw "Use HTTPS for a network server. HTTP is accepted only for loopback development/certification."
}

$common = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$configDirectory = Join-Path (Join-Path $common "GameNet Manager") "Config"
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null

$acl = [System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new("S-1-5-18"), [System.Security.AccessControl.FileSystemRights]::FullControl, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-544"), [System.Security.AccessControl.FileSystemRights]::FullControl, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-545"), [System.Security.AccessControl.FileSystemRights]::ReadAndExecute, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new("NT AUTHORITY\NETWORK SERVICE", [System.Security.AccessControl.FileSystemRights]::ReadAndExecute, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $configDirectory -AclObject $acl

$encoding = [System.Text.UTF8Encoding]::new($false)
if ($Component -in @("Desktop", "Both")) {
    $desktopPath = Join-Path $configDirectory "desktop.json"
    $desktopConfig = [ordered]@{
        GameNet = [ordered]@{
            Server = [ordered]@{
                BaseUrl = $uri.AbsoluteUri.TrimEnd("/")
            }
        }
    }
    [System.IO.File]::WriteAllText($desktopPath, ($desktopConfig | ConvertTo-Json -Depth 8) + [Environment]::NewLine, $encoding)
    Write-Host "Desktop endpoint written to $desktopPath"
}

if ($Component -in @("Agent", "Both")) {
    $agentPath = Join-Path $configDirectory "agent.json"
    $agentConfig = [ordered]@{
        GameNet = [ordered]@{
            AgentTransport = [ordered]@{
                ServerBaseUrl = $uri.AbsoluteUri.TrimEnd("/")
            }
        }
    }
    [System.IO.File]::WriteAllText($agentPath, ($agentConfig | ConvertTo-Json -Depth 8) + [Environment]::NewLine, $encoding)
    Write-Host "Agent endpoint written to $agentPath"
}

Write-Host "Endpoint configuration complete. Restart Desktop and/or the GameNet 5 Agent service to load it."
