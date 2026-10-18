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
    "src/Desktop/Features",
    "src/Server/Application",
    "src/Server/Domain"
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

$certifiedCommit = [string]$marker.certifiedCommit
if ($certifiedCommit -notmatch "^[0-9a-fA-F]{40}$") {
    throw "Foundation certification marker has an invalid certifiedCommit SHA. A full 40-character Git commit SHA is required."
}

$headOutput = & git rev-parse --verify "HEAD^{commit}" 2>$null
$headExitCode = $LASTEXITCODE
if ($headExitCode -ne 0 -or [string]::IsNullOrWhiteSpace([string]$headOutput)) {
    throw "Cannot resolve HEAD as a Git commit; Foundation ancestry cannot be verified."
}
$headSha = ([string]$headOutput).Trim()

$certifiedOutput = & git rev-parse --verify "${certifiedCommit}^{commit}" 2>$null
$certifiedResolveExitCode = $LASTEXITCODE
if ($certifiedResolveExitCode -ne 0 -or [string]::IsNullOrWhiteSpace([string]$certifiedOutput)) {
    throw "Foundation certifiedCommit '$certifiedCommit' is not available in this checkout. Fetch complete Git history; the marker must not be accepted without its commit object."
}

& git merge-base --is-ancestor $certifiedCommit $headSha
$ancestryExitCode = $LASTEXITCODE
if ($ancestryExitCode -eq 1) {
    throw "Foundation certifiedCommit '$certifiedCommit' is not an ancestor of HEAD '$headSha'. Rebase or rebuild the feature branch from the certified Foundation checkpoint before adding business implementation."
}
if ($ancestryExitCode -ne 0) {
    throw "Git could not determine whether Foundation certifiedCommit '$certifiedCommit' is an ancestor of HEAD '$headSha'."
}

Write-Host "Business gate passed: certified Foundation commit $certifiedCommit is an ancestor of HEAD $headSha."
