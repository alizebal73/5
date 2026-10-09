$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-RequiredText {
    param([Parameter(Mandatory)][string]$RelativePath)
    $fullPath = Join-Path $repoRoot ($RelativePath -replace "/", "\")
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "WORK_ITEM_DOCS_GATE: required file is missing: $RelativePath"
    }
    return Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
}

function Assert-RequiredTerms {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string[]]$Terms
    )
    foreach ($term in $Terms) {
        if ($Text.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "WORK_ITEM_DOCS_GATE: '$RelativePath' is missing required term/heading '$term'."
        }
    }
}

$readyPath = "docs/planning/definition-of-ready.md"
$donePath = "docs/planning/definition-of-done.md"
$processPath = "docs/development/bug-fix-process.md"
$bugTemplatePath = "docs/templates/bug-fix-template.md"
$sliceTemplatePath = "docs/templates/vertical-slice-template.md"
$tracePath = "docs/planning/requirements-traceability.md"
$matrixPath = "docs/planning/capability-matrix.md"
$logPath = "docs/development/bug-fix-log.md"
$planPath = "docs/planning/master-build-plan.md"

$ready = Get-RequiredText $readyPath
$done = Get-RequiredText $donePath
$process = Get-RequiredText $processPath
$bugTemplate = Get-RequiredText $bugTemplatePath
$sliceTemplate = Get-RequiredText $sliceTemplatePath
$trace = Get-RequiredText $tracePath
$matrix = Get-RequiredText $matrixPath
$log = Get-RequiredText $logPath
$plan = Get-RequiredText $planPath

Assert-RequiredTerms $readyPath $ready @(
    "## اطلاعات پایه",
    "## دامنه و مالکیت دامنه",
    "## قرارداد، persistence و تراکنش",
    "## امنیت، عملیات و UX",
    "## برنامهٔ راستی‌آزمایی",
    "## استثنای باگ بحرانی",
    "## تصمیم آمادگی"
)
Assert-RequiredTerms $donePath $done @(
    "## رفتار و پذیرش",
    "## معماری و صحت داده",
    "## شواهد آزمون",
    "## قواعد ویژهٔ باگ",
    "## مستندات و closure",
    "## مرز Done در برابر Release"
)
Assert-RequiredTerms $processPath $process @(
    "## شدت و وضعیت",
    "## جریان اجباری",
    "## قواعد وضعیت",
    "## نگهداری Bug-Fix Log",
    "FIXED_UNVERIFIED",
    "علت ریشه‌ای"
)
Assert-RequiredTerms $bugTemplatePath $bugTemplate @(
    "## Identity and state",
    "## Symptom and impact",
    "## Reproduction and evidence before fix",
    "## Root cause analysis",
    "## Fix and scope",
    "## Regression and verification",
    "## Closure"
)
Assert-RequiredTerms $sliceTemplatePath $sliceTemplate @(
    "## Identity",
    "## User outcome and boundaries",
    "## Ownership, state and contracts",
    "## Security and reliability",
    "## Data and release",
    "## Verification plan",
    "## Results and closure"
)
Assert-RequiredTerms $tracePath $trace @(
    "REQ-ARCH-001",
    "REQ-WAL-002",
    "REQ-SES-003",
    "Definition of Ready / Done"
)
Assert-RequiredTerms $matrixPath $matrix @(
    "requirements-traceability.md",
    "قواعد تجاری‌ای که هر UI باید از Server بگیرد",
    "Server",
    "P0",
    "Deferred"
)
Assert-RequiredTerms $logPath $log @(
    "bug-fix-process.md",
    "bug-fix-template.md",
    "A defect is not closed"
)
Assert-RequiredTerms $planPath $plan @(
    "docs/planning/definition-of-ready.md",
    "docs/planning/definition-of-done.md",
    "docs/development/bug-fix-process.md"
)

$idMatches = [regex]::Matches($trace, '(?m)^\|\s*(REQ-[A-Z0-9-]+)\s*\|')
$ids = @($idMatches | ForEach-Object { $_.Groups[1].Value })
if ($ids.Count -lt 50) {
    throw "WORK_ITEM_DOCS_GATE: traceability matrix has only $($ids.Count) requirement rows; expected at least 50."
}
$duplicates = @($ids | Group-Object | Where-Object { $_.Count -gt 1 })
if ($duplicates.Count -gt 0) {
    $duplicateNames = ($duplicates | ForEach-Object { $_.Name }) -join ", "
    throw "WORK_ITEM_DOCS_GATE: duplicate requirement IDs: $duplicateNames"
}

Write-Host "WORK_ITEM_DOCS_GATE PASSED: Ready/Done policy, bug/slice templates and $($ids.Count) unique requirement IDs validated."
