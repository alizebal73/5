$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$markerPath = "docs/operations/foundation-certification.json"
if (-not (Test-Path $markerPath -PathType Leaf)) {
    throw "Foundation certification marker is missing."
}

$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
$businessFiles = @()

foreach ($root in @(
    "src/Server/Modules",
    "src/Desktop/Features"
)) {
    if (-not (Test-Path $root -PathType Container)) { continue }

    $businessFiles += Get-ChildItem $root -Recurse -File |
        Where-Object {
            $_.Name -notin @("README.md", ".gitkeep") -and
            $_.FullName -notmatch "[\\/]bin[\\/]|[\\/]obj[\\/]"
        }
}

if ($businessFiles.Count -eq 0) {
    Write-Host "Business gate passed: no business implementation is present."
    exit 0
}

if (-not [string]::Equals([string]$marker.status, "certified", [StringComparison]::OrdinalIgnoreCase)) {
    $businessFiles | ForEach-Object {
        Write-Host "Blocked business file: $($_.FullName)"
    }
    throw "Business implementation is present before Foundation Certification. Merge/coding must remain blocked until the certified marker is recorded and the feature branch is rebased onto that checkpoint."
}

Write-Host "Business gate passed: Foundation certification marker is active."
