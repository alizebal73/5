$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$limit = 700
foreach ($root in @("src/Server","src/Shared","src/Client","src/Desktop")) {
  if (-not (Test-Path $root)) { continue }
  Get-ChildItem $root -Recurse -File -Include *.cs,*.xaml |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj|Migrations)[\\/]" } |
    ForEach-Object {
      $count = @((Get-Content -LiteralPath $_.FullName)).Count
      if ($count -gt $limit) { throw "Source-size guard failed: $($_.FullName) has $count lines." }
    }
}
Write-Host "Source-size guard passed."
