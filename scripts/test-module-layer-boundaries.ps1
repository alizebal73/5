$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$checker = Join-Path $PSScriptRoot "check-module-layer-boundaries.ps1"
$architectureChecker = Join-Path $PSScriptRoot "check-architecture.ps1"
if (-not (Test-Path -LiteralPath $checker -PathType Leaf)) {
    throw "Module layer-boundary checker is missing: $checker"
}
if (-not (Test-Path -LiteralPath $architectureChecker -PathType Leaf)) {
    throw "Architecture checker is missing: $architectureChecker"
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gamenet-architecture-guard-" + [guid]::NewGuid().ToString("N"))
$moduleRoot = Join-Path $fixtureRoot "Probe"
foreach ($layer in @("Domain", "Application", "Api", "Infrastructure")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $moduleRoot $layer) | Out-Null
}
$otherModuleRoot = Join-Path $fixtureRoot "Other"
New-Item -ItemType Directory -Force -Path (Join-Path $otherModuleRoot "Contracts") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $otherModuleRoot "Domain") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $otherModuleRoot "Infrastructure") | Out-Null

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

function Assert-CrossModuleRejected {
    param([string]$CaseName)

    $rejected = $false
    try {
        & $architectureChecker -ModulesRoot $fixtureRoot -OnlyModuleBoundaries
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw "Cross-module architecture regression: expected rejection for '$CaseName'."
    }
    Write-Host "PASS: cross-module implementation dependency rejected ($CaseName)."
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

    # Existing shared cross-cutting contracts are an explicit compatibility allowlist.
    $allowedPlatformContracts = Join-Path $moduleRoot "Application\AllowedPlatformContracts.cs"
    Set-Content -LiteralPath $allowedPlatformContracts -Value @(
        "using GameNet.Server.Infrastructure.Audit;",
        "using GameNet.Server.Infrastructure.Transactions;",
        "using GameNet.Server.Infrastructure.Idempotency;"
    )
    & $checker -ModulesRoot $fixtureRoot
    Write-Host "PASS: explicitly allowlisted cross-cutting contract namespaces remain available."
    Remove-Item -LiteralPath $allowedPlatformContracts -Force

    # Cross-module public contracts are the only permitted namespace reference.
    $contractReference = Join-Path $moduleRoot "Application\AllowedContract.cs"
    Set-Content -LiteralPath $contractReference -Value "using GameNet.Server.Modules.Other.Contracts;"
    & $architectureChecker -ModulesRoot $fixtureRoot -OnlyModuleBoundaries
    Write-Host "PASS: explicit public module Contracts namespace is allowed."
    Remove-Item -LiteralPath $contractReference -Force

    foreach ($forbiddenReference in @(
        "using GameNet.Server.Modules.Other;",
        "using GameNet.Server.Modules.Other.Domain;",
        "using GameNet.Server.Modules.Other.Infrastructure;",
        "using GameNet . Server . Modules . Other . Application ;"
    )) {
        $referenceFile = Join-Path $moduleRoot "Application\CrossModuleReference.cs"
        Set-Content -LiteralPath $referenceFile -Value $forbiddenReference
        Assert-CrossModuleRejected $forbiddenReference
        Remove-Item -LiteralPath $referenceFile -Force
    }

    $forbiddenPersistenceImports = @(
        "using GameNet.Server.Persistence;",
        "using GameNet . Server . Persistence ;",
        "using Microsoft.EntityFrameworkCore;",
        "using Microsoft . EntityFrameworkCore ;"
    )

    foreach ($layer in @("Domain", "Application", "Api")) {
        foreach ($forbiddenImport in $forbiddenPersistenceImports) {
            $badFile = Join-Path $moduleRoot "$layer\ForbiddenPersistence-$([guid]::NewGuid().ToString('N')).cs"
            Set-Content -LiteralPath $badFile -Value $forbiddenImport
            Assert-Rejected "$layer direct persistence import '$forbiddenImport'"
            Remove-Item -LiteralPath $badFile -Force
        }
    }

    $forbiddenInfrastructureReferences = @(
        "using GameNet.Server.Infrastructure;",
        "using GameNet.Server.Infrastructure.Configuration;",
        "using GameNet.Server.Infrastructure.Security;",
        "using GameNet.Server.Modules.Probe.Infrastructure;",
        "using GameNet.Server.Modules.Probe.Infrastructure.Adapters;",
        "using GameNet . Server . Modules . Probe . Infrastructure ;"
    )
    foreach ($layer in @("Domain", "Application", "Api")) {
        foreach ($forbiddenReference in $forbiddenInfrastructureReferences) {
            $fileName = "ForbiddenInfrastructureReference-$([guid]::NewGuid().ToString('N')).cs"
            $badFile = Join-Path $moduleRoot "$layer\$fileName"
            Set-Content -LiteralPath $badFile -Value $forbiddenReference
            Assert-Rejected "$layer direct Infrastructure reference '$forbiddenReference'"
            Remove-Item -LiteralPath $badFile -Force
        }
    }

    $forbiddenImports = @(
        "using System.Net.Http;",
        "using System.Net;",
        "using System.IO;",
        "using System.Diagnostics;",
        "using System . Net . Http ;",
        "using System . Net ;",
        "using System . IO ;",
        "using System . Diagnostics ;"
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

    # Regression cases for SDK implicit/global usings: forbidden dependencies must be
    # rejected even when a layer references short type names without importing a namespace.
    $forbiddenPersistenceTypes = @(
        "DbSet<Station> Stations { get; set; }",
        "IDbContextFactory<GameNetDbContext> Factory { get; set; }"
    )
    foreach ($layer in @("Domain", "Application", "Api")) {
        foreach ($forbiddenType in $forbiddenPersistenceTypes) {
            $fileName = "ForbiddenImplicitPersistence-$([guid]::NewGuid().ToString('N')).cs"
            $badFile = Join-Path $moduleRoot "$layer\$fileName"
            Set-Content -LiteralPath $badFile -Value $forbiddenType
            Assert-Rejected "$layer implicit/global-using persistence type '$forbiddenType'"
            Remove-Item -LiteralPath $badFile -Force
        }
    }

    $forbiddenImplicitSideEffects = @(
        "File.ReadAllText(path);",
        "Directory.CreateDirectory(path);",
        "Path.Combine(root, name);",
        "new FileSystemWatcher(path);",
        "new SmtpClient();",
        "new Ping();"
    )
    foreach ($layer in @("Domain", "Application")) {
        foreach ($forbiddenType in $forbiddenImplicitSideEffects) {
            $fileName = "ForbiddenImplicitSideEffect-$([guid]::NewGuid().ToString('N')).cs"
            $badFile = Join-Path $moduleRoot "$layer\$fileName"
            Set-Content -LiteralPath $badFile -Value $forbiddenType
            Assert-Rejected "$layer implicit/global-using side effect '$forbiddenType'"
            Remove-Item -LiteralPath $badFile -Force
        }
    }

    # Application/Domain cannot become ASP.NET endpoint layers merely by relying on
    # framework implicit usings. API remains the valid inbound HTTP adapter boundary.
    $forbiddenWebFrameworkTypes = @(
        "HttpContext Context { get; set; }",
        "IEndpointRouteBuilder Endpoints { get; set; }",
        "IResult Handle() => Results.Ok();",
        "WebApplication.CreateBuilder();"
    )
    foreach ($layer in @("Domain", "Application")) {
        foreach ($forbiddenType in $forbiddenWebFrameworkTypes) {
            $fileName = "ForbiddenWebFramework-$([guid]::NewGuid().ToString('N')).cs"
            $badFile = Join-Path $moduleRoot "$layer\$fileName"
            Set-Content -LiteralPath $badFile -Value $forbiddenType
            Assert-Rejected "$layer implicit/global-using web framework type '$forbiddenType'"
            Remove-Item -LiteralPath $badFile -Force
        }
    }

    Write-Host "MODULE LAYER-BOUNDARY REGRESSION TESTS PASSED."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
