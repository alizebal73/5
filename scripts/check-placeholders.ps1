$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
foreach ($root in @("src/Server","src/Client","src/Desktop")) {
  if (-not (Test-Path $root)) { continue }
  $matches = Get-ChildItem $root -Recurse -File -Include *.cs,*.xaml,*.json | Select-String -Pattern "\bTODO\b|\bFIXME\b|\bplaceholder\b"
  if ($matches) { $matches | ForEach-Object { Write-Host "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }; throw "Runtime placeholder markers are forbidden." }
}
Write-Host "Placeholder guard passed."
