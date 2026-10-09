$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:TestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gamenet-business-gate-" + [Guid]::NewGuid().ToString("N"))
$script:GatePath = Join-Path $script:TestRoot "scripts/check-business-gate.ps1"
$script:MarkerPath = Join-Path $script:TestRoot "docs/operations/foundation-certification.json"

function Invoke-TestGit {
    param([string[]]$GitArguments)

    $output = & git -C $script:TestRoot @GitArguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Test Git command failed (exit $exitCode): git $($GitArguments -join ' '). Output: $($output -join ' ')"
    }
    return ($output -join [Environment]::NewLine).Trim()
}

function Write-TestMarker {
    param([string]$CertifiedSha)

    $marker = [ordered]@{
        status = "certified"
        certifiedCommit = $CertifiedSha
        certifiedAtUtc = [DateTime]::UtcNow.ToString("O")
        evidenceFile = "test-only"
    }
    $json = $marker | ConvertTo-Json
    [System.IO.File]::WriteAllText($script:MarkerPath, $json, [System.Text.UTF8Encoding]::new($false))
}

function Invoke-GateExpectSuccess {
    Push-Location -LiteralPath $script:TestRoot
    try {
        & $script:GatePath | Out-Null
    }
    finally {
        Pop-Location
    }
}

function Invoke-GateExpectFailure {
    param([string]$ExpectedMessage)

    Push-Location -LiteralPath $script:TestRoot
    try {
        $failedAsExpected = $false
        try {
            & $script:GatePath | Out-Null
        }
        catch {
            $failedAsExpected = $true
            if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
                throw "Gate failed for an unexpected reason. Expected '$ExpectedMessage'; actual '$($_.Exception.Message)'."
            }
        }

        if (-not $failedAsExpected) {
            throw "Gate unexpectedly passed; expected a rejection containing '$ExpectedMessage'."
        }
    }
    finally {
        Pop-Location
    }
}

try {
    New-Item -ItemType Directory -Force -Path $script:TestRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $script:TestRoot "scripts") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $script:TestRoot "docs/operations") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $script:TestRoot "src/Server/Modules/Identity") | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "check-business-gate.ps1") -Destination $script:GatePath

    Invoke-TestGit @("init", "--initial-branch=main") | Out-Null
    Invoke-TestGit @("config", "user.name", "GameNet Gate Test") | Out-Null
    Invoke-TestGit @("config", "user.email", "gamenet-gate-test@example.invalid") | Out-Null

    Set-Content -LiteralPath (Join-Path $script:TestRoot "README.md") -Value "Temporary gate test repository." -Encoding utf8
    Invoke-TestGit @("add", "--all") | Out-Null
    Invoke-TestGit @("commit", "-m", "temporary baseline") | Out-Null
    $certifiedSha = Invoke-TestGit @("rev-parse", "HEAD")

    Write-TestMarker -CertifiedSha $certifiedSha
    Set-Content -LiteralPath (Join-Path $script:TestRoot "src/Server/Modules/Identity/Feature.cs") -Value "// temporary business file" -Encoding utf8
    Invoke-TestGit @("add", "--all") | Out-Null
    Invoke-TestGit @("commit", "-m", "temporary certified business commit") | Out-Null

    # Positive case: a certified ancestor must pass.
    Invoke-GateExpectSuccess

    # Create a real commit object on disconnected history; it must not be accepted as the Foundation ancestor.
    Invoke-TestGit @("checkout", "--orphan", "unrelated-certification") | Out-Null
    Set-Content -LiteralPath (Join-Path $script:TestRoot "README.md") -Value "Unrelated certification history." -Encoding utf8
    Invoke-TestGit @("add", "--all") | Out-Null
    Invoke-TestGit @("commit", "-m", "unrelated certification commit") | Out-Null
    $unrelatedSha = Invoke-TestGit @("rev-parse", "HEAD")
    Invoke-TestGit @("switch", "main") | Out-Null

    Write-TestMarker -CertifiedSha $unrelatedSha
    Invoke-GateExpectFailure -ExpectedMessage "is not an ancestor of HEAD"

    Write-Host "Business gate ancestry regression tests passed (certified ancestor accepted; unrelated commit rejected)."
}
finally {
    if (Test-Path -LiteralPath $script:TestRoot) {
        Remove-Item -LiteralPath $script:TestRoot -Recurse -Force
    }
}
