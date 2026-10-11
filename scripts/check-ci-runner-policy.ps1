$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$workflowRoot = Join-Path $repoRoot ".github/workflows"
if (-not (Test-Path -LiteralPath $workflowRoot -PathType Container)) {
    throw "The GitHub Actions workflow directory is missing."
}

$files = @(Get-ChildItem -LiteralPath $workflowRoot -Recurse -File |
    Where-Object { $_.Extension -in @(".yml", ".yaml") } |
    Sort-Object FullName)
if ($files.Count -eq 0) {
    throw "No GitHub Actions workflow files were found to validate."
}

$assignmentCount = 0
foreach ($file in $files) {
    $source = [System.IO.File]::ReadAllText($file.FullName)
    $matches = [regex]::Matches($source, '(?im)^\s*runs-on\s*:\s*(?<runner>[^\r\n]+)')
    foreach ($match in $matches) {
        $assignmentCount++
        $runner = $match.Groups["runner"].Value.Trim().Trim('"', "'")
        if ($runner -ne "windows-2025") {
            $relative = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName)
            throw "Unapproved CI runner '$runner' in '$relative'. Automated workflows currently require windows-2025. This guard is defence in depth only; GitHub-side Runner access settings must also prevent untrusted PR code from using self-hosted runners."
        }
    }
}
if ($assignmentCount -eq 0) {
    throw "No explicit runner assignments were found; dynamic or unreviewed runner selection is not allowed."
}

Write-Host "CI_RUNNER_POLICY_PASSED=true"
Write-Host "CI_WORKFLOW_FILES=$($files.Count)"
Write-Host "CI_RUNNER_ASSIGNMENTS=$assignmentCount"
