$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
function Assert-NoMatch { param([string]$Root,[string]$Pattern,[string]$Message)
  if (-not (Test-Path $Root)) { return }
  $matches = Get-ChildItem $Root -Recurse -File -Include *.cs,*.csproj,*.xaml,*.props | Select-String -Pattern $Pattern
  if ($matches) { $matches | ForEach-Object { Write-Host "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }; throw $Message }
}
Assert-NoMatch "src/Server/Modules" "Microsoft\.EntityFrameworkCore|GameNet\.Server\.Persistence" "Business module layers may not access EF/Persistence directly."
Assert-NoMatch "src/Desktop" "GameNet\.Server\.(Persistence|Modules|Infrastructure)|Microsoft\.EntityFrameworkCore|Npgsql|DbContext" "Desktop may not reference Server implementation or database."
Assert-NoMatch "src/Client" "GameNet\.Server\.(Persistence|Modules|Infrastructure)|Npgsql|DbContext" "Agent may not reference Server implementation or database."
Assert-NoMatch "src/Server" "DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow" "Server code must use IGameClock/TimeProvider."
Assert-NoMatch "src" "Microsoft\.Data\.Sqlite|UseSqlite|SqliteConnection" "PostgreSQL is the production persistence authority; SQLite is forbidden."
Assert-NoMatch "src/Server" "\b(Mock|Fake)[A-Za-z0-9_]*\b" "Production Server mocks/fakes are forbidden."
Assert-NoMatch "src/Desktop" "WebView2|Microsoft\.Web\.WebView2|BlazorWebView|System\.Windows\.Controls\.WebBrowser" "Browser/WebView runtime is forbidden in Desktop."
if ((Get-Content "src/Server/Program.cs").Count -gt 120) { throw "Program.cs exceeded the composition-only limit." }
if (Test-Path "src/Dashboard" -PathType Container) { throw "Browser Dashboard is forbidden." }
if (Test-Path "package-lock.json" -PathType Leaf) { throw "Node/browser runtime artifacts are forbidden." }
Write-Host "Architecture guard passed."
