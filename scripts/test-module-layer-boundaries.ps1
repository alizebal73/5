$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$checker = Join-Path $PSScriptRoot "check-module-layer-boundaries.ps1"
if (-not (Test-Path -LiteralPath $checker -PathType Leaf)) {
    throw "Module layer-boundary checker is missing: $checker"
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gamenet-architecture-guard-" + [guid]::NewGuid().ToString("N"))
$moduleRoot = Join-Path $fixtureRoot "Probe"
foreach ($layer in @("Domain", "Application", "Api", "Infrastructure")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $moduleRoot $layer) | Out-Null
}

function Assert-Rejected {
    param([string]$CaseName)

    $rejected = $false
    try {
        & $checker -ModulesRoot $fixtureRoot
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw "Architecture guard regression: expected rejection for '$CaseName'."
    }
    Write-Host "PASS: forbidden dependency rejected ($CaseName)."
}

try {
    # Infrastructure is allowed to use persistence and outbound integration libraries.
    Set-Content -LiteralPath (Join-Path $moduleRoot "Infrastructure\PersistenceAdapter.cs") -Value @(
        "using Microsoft.EntityFrameworkCore;",
        "using System.Net.Http;",
        "using System.IO;",
        "using System.Net;",
        "using System.Diagnostics;",
        "namespace GameNet.Server.Modules.Probe.Infrastructure;"
    )

    # API may use inbound ASP.NET types, but must not access persistence directly.
    Set-Content -LiteralPath (Join-Path $moduleRoot "Api\ProbeEndpoint.cs") -Value @(
        "using Microsoft.AspNetCore.Http;",
        "namespace GameNet.Server.Modules.Probe.Api;"
    )
    & $checker -ModulesRoot $fixtureRoot
    Write-Host "PASS: valid Infrastructure persistence/network and API transport access are allowed."

    foreach ($layer in @("Domain", "Application", "Api")) {
        $badFile = Join-Path $moduleRoot "$layer\ForbiddenPersistence.cs"
        Set-Content -LiteralPath $badFile -Value "using GameNet.Server.Persistence;"
        Assert-Rejected "$layer direct persistence"
        Remove-Item -LiteralPath $badFile -Force
    }

    $forbiddenImports = @(
        "using System.Net.Http;",
        "using System.Net;",
        "using System.IO;",
        "using System.Diagnostics;"
    )

    foreach ($layer in @("Domain", "Application")) {
        foreach ($forbiddenImport in $forbiddenImports) {
            $fileName = "ForbiddenSideEffect-$([guid]::NewGuid().ToString('N')).cs"
            $badFile = Join-Path $moduleRoot "$layer\$fileName"
            Set-Content -LiteralPath $badFile -Value $forbiddenImport
            Assert-Rejected "$layer external side effect import '$forbiddenImport'"
            Remove-Item -LiteralPath $badFile -Force
        }
    }

    foreach ($forbiddenImport in $forbiddenImports) {
        $fileName = "ForbiddenSideEffect-$([guid]::NewGuid().ToString('N')).cs"
        $apiBadFile = Join-Path $moduleRoot "Api\$fileName"
        Set-Content -LiteralPath $apiBadFile -Value $forbiddenImport
        Assert-Rejected "Api outbound side effect import '$forbiddenImport'"
        Remove-Item -LiteralPath $apiBadFile -Force
    }

    Write-Host "MODULE LAYER-BOUNDARY REGRESSION TESTS PASSED."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
