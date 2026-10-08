$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-NoMatch {
    param(
        [string]$Root,
        [string]$Pattern,
        [string]$Message,
        [string[]]$ExcludePath = @()
    )

    if (-not (Test-Path $Root)) { return }

    $files = Get-ChildItem $Root -Recurse -File -Include *.cs,*.csproj,*.xaml,*.props,*.json |
        Where-Object {
            $full = $_.FullName
            foreach ($excluded in $ExcludePath) {
                if ($full -like $excluded) { return $false }
            }
            return $true
        }

    $matches = $files | Select-String -Pattern $Pattern
    if ($matches) {
        $matches | ForEach-Object {
            Write-Host "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"
        }
        throw $Message
    }
}

# Layer rules.
Assert-NoMatch "src/Server/Modules" "Microsoft.EntityFrameworkCore|GameNet.Server.Persistence" "Business module layers may not access EF/Persistence directly."
Assert-NoMatch "src/Server/Modules" "System.Net.|HttpClient|WebSocket|SignalR|System.IO.|System.Diagnostics.Process" "Domain/Application business code may not own external side effects."

# Cross-module internal references are forbidden. A module may reference Shared and explicit public contracts,
# but never another module's implementation namespace.
$modulesRoot = "src/Server/Modules"
if (Test-Path $modulesRoot) {
    $moduleDirs = Get-ChildItem $modulesRoot -Directory
    foreach ($module in $moduleDirs) {
        $others = $moduleDirs | Where-Object Name -ne $module.Name
        foreach ($other in $others) {
            Assert-NoMatch (Join-Path $modulesRoot $module.Name) ("GameNet\.Server\.Modules\$([regex]::Escape($other.Name))") "Cross-module implementation reference detected: $($module.Name) -> $($other.Name)."
        }
    }
}

# API / application / domain layers must not reach persistence or perform infrastructure work.
Assert-NoMatch "src/Server/Modules/*/Domain" "DbContext|EntityFrameworkCore|Npgsql|GameNet\.Server\.Persistence|Microsoft\.AspNetCore|HttpClient|System\.Net|System\.IO|System\.Diagnostics" "Domain layer contains infrastructure/runtime access."
Assert-NoMatch "src/Server/Modules/*/Application" "DbContext|EntityFrameworkCore|Npgsql|GameNet\.Server\.Persistence" "Application layer contains direct persistence access."
Assert-NoMatch "src/Server/Modules/*/Api" "DbContext|EntityFrameworkCore|Npgsql|GameNet\.Server\.Persistence" "API layer contains direct persistence access."

# Client boundaries.
Assert-NoMatch "src/Desktop" "GameNet\.Server\.(Persistence|Modules|Infrastructure)|Microsoft\.EntityFrameworkCore|Npgsql|DbContext" "Desktop may not reference Server implementation or database."
Assert-NoMatch "src/Client" "GameNet\.Server\.(Persistence|Modules|Infrastructure)|Npgsql|DbContext" "Agent may not reference Server implementation or database."

# Clients may not create duplicate wire contracts. Transport DTOs belong in Shared.Contracts.
foreach ($root in @("src/Desktop","src/Client")) {
    Assert-NoMatch $root "\b(public\s+|internal\s+|private\s+)?(sealed\s+)?(record|class|struct)\s+\w*(Dto|Request|Response|Command)\b" "Client runtime contains a duplicate transport contract. Put wire contracts in Shared.Contracts."
}

# Persistence/time/technology rules.
Assert-NoMatch "src/Server" "DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow" "Server code must use IGameClock/TimeProvider."
Assert-NoMatch "src" "Microsoft\.Data\.Sqlite|UseSqlite|SqliteConnection" "PostgreSQL is the production persistence authority; SQLite is forbidden."

# No fake/demo/mock product paths.
Assert-NoMatch "src/Server" "\b(Mock|Fake|Stub|Demo|SampleData)[A-Za-z0-9_]*\b" "Production Server mock/fake/demo sources are forbidden."
Assert-NoMatch "src/Desktop" "\b(Mock|Fake|Stub|Demo|SampleData)[A-Za-z0-9_]*\b" "Production Desktop mock/fake/demo sources are forbidden."
Assert-NoMatch "src/Client" "\b(Mock|Fake|Stub|Demo|SampleData)[A-Za-z0-9_]*\b" "Production Agent mock/fake/demo sources are forbidden."

# Business modules never call SaveChanges directly. Platform infrastructure owns its own
# explicit transaction where needed (for example Outbox leasing).
if (Test-Path "src/Server/Modules") {
    $saveMatches = Get-ChildItem "src/Server/Modules" -Recurse -File -Filter *.cs |
        Select-String -Pattern "\.SaveChanges(Async)?\s*\("
    if ($saveMatches) {
        $saveMatches | ForEach-Object { Write-Host "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw "Business modules may not call SaveChanges/SaveChangesAsync directly."
    }
}

# Browser runtime is forbidden.
Assert-NoMatch "src/Desktop" "WebView2|Microsoft\.Web\.WebView2|BlazorWebView|System\.Windows\.Controls\.WebBrowser" "Browser/WebView runtime is forbidden in Desktop."
if ((Get-Content "src/Server/Program.cs").Count -gt 120) { throw "Program.cs exceeded the composition-only limit." }
if (Test-Path "src/Dashboard" -PathType Container) { throw "Browser Dashboard is forbidden." }
if (Test-Path "package-lock.json" -PathType Leaf) { throw "Node/browser runtime artifacts are forbidden." }

Write-Host "Architecture guard passed."
