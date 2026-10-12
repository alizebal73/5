param(
    [string]$ModulesRoot = "src/Server/Modules"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-NoMatchInLayer {
    param(
        [string]$LayerPath,
        [string]$Pattern,
        [string]$Message
    )

    if (-not (Test-Path -LiteralPath $LayerPath -PathType Container)) {
        return
    }

    $files = Get-ChildItem -LiteralPath $LayerPath -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".csproj", ".xaml", ".props", ".json") }
    if (-not $files) {
        return
    }

    $matches = $files | Select-String -Pattern $Pattern
    if ($matches) {
        $matches | ForEach-Object {
            Write-Host "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"
        }
        throw $Message
    }
}

if (-not (Test-Path -LiteralPath $ModulesRoot -PathType Container)) {
    Write-Host "No module root found; layer-boundary check skipped."
    exit 0
}

# Persistence is available only to Infrastructure. Domain, Application, and API code
# must use module-owned interfaces/services instead of reaching directly into EF/PostgreSQL.
# Permit formatting whitespace around namespace separators so the guard cannot be
# bypassed by legal but unusual C# formatting (for example, "using Microsoft . EntityFrameworkCore;").
$persistencePattern = "\bMicrosoft\s*\.\s*EntityFrameworkCore\b|\bDbContext\b|\bNpgsql\b|\bGameNet\s*\.\s*Server\s*\.\s*Persistence\b"

# Domain and Application code may not perform network, filesystem, process, or web-framework
# work. API is an inbound transport boundary, so ASP.NET types are expected there; API still
# may not access persistence directly. Infrastructure is intentionally allowed those capabilities.
# Match fully qualified references, namespace imports, aliases, and whitespace around dots.
$networkNamespacePattern = "\bSystem\s*\.\s*Net(?:\s*\.|\s*;|\s*=)|\bHttpClient\b|\bWebClient\b|\bHttpWebRequest\b|\bHttpWebResponse\b|\bWebSocket\b|\bSignalR\b|\bSocket\b|\bTcpClient\b|\bUdpClient\b"
$fileSystemNamespacePattern = "\bSystem\s*\.\s*IO(?:\s*\.|\s*;|\s*=)|\bFileStream\b|\bFileInfo\b|\bDirectoryInfo\b|\bStreamReader\b|\bStreamWriter\b"
$diagnosticsNamespacePattern = "\bSystem\s*\.\s*Diagnostics(?:\s*\.|\s*;|\s*=)|\bProcessStartInfo\b|\bProcess\b"
$externalSideEffectPattern = "$networkNamespacePattern|$fileSystemNamespacePattern|$diagnosticsNamespacePattern|\bMicrosoft\s*\.\s*AspNetCore\b"
$apiSideEffectPattern = "$networkNamespacePattern|$fileSystemNamespacePattern|$diagnosticsNamespacePattern"

foreach ($module in Get-ChildItem -LiteralPath $ModulesRoot -Directory) {
    foreach ($layerName in @("Domain", "Application", "Api")) {
        $layerPath = Join-Path $module.FullName $layerName
        Assert-NoMatchInLayer $layerPath $persistencePattern "$($module.Name) $layerName layer contains direct persistence access."
    }

    foreach ($layerName in @("Domain", "Application")) {
        $layerPath = Join-Path $module.FullName $layerName
        Assert-NoMatchInLayer $layerPath $externalSideEffectPattern "$($module.Name) $layerName layer contains infrastructure/external side effects."
    }

    # API may use ASP.NET request/response types, but outbound clients and direct
    # filesystem/process/network integrations still belong in Infrastructure.
    $apiPath = Join-Path $module.FullName "Api"
    Assert-NoMatchInLayer $apiPath $apiSideEffectPattern "$($module.Name) API layer contains outbound infrastructure side effects."
}

Write-Host "Module layer-boundary check passed."
