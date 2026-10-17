$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$limit = 700
$sourceRoots = @("src/Server", "src/Shared", "src/Client", "src/Desktop")
$sourceFiles = @(
  foreach ($root in $sourceRoots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem $root -Recurse -File -Include *.cs,*.xaml |
      Where-Object { $_.FullName -notmatch "[\\/](bin|obj|Migrations)[\\/]" }
  }
)

foreach ($file in $sourceFiles) {
  $count = @((Get-Content -LiteralPath $file.FullName)).Count
  if ($count -gt $limit) {
    throw "Source-size guard failed: $($file.FullName) has $count lines (limit $limit)."
  }
}

# Temporary aggregate budgets for known partial-class debt.
# Count every source file that declares the named type so moving code into
# another partial file cannot bypass the budget. These conservative file
# aggregates are not a substitute for syntax-aware class/method counting;
# complete that follow-up under Issue #138 after Slice 1 acceptance.
$aggregateBudgets = @(
  [pscustomobject]@{
    Namespace = "GameNet.Desktop.Features.Sessions"
    TypeName = "SessionWorkflowViewModel"
    MaxAggregateSourceLines = 2200
    TrackingIssue = "#138"
  },
  [pscustomobject]@{
    Namespace = "GameNet.Server.Modules.Billing.Infrastructure"
    TypeName = "BillingService"
    MaxAggregateSourceLines = 750
    TrackingIssue = "#138"
  },
  [pscustomobject]@{
    Namespace = "GameNet.Server.Modules.Sessions.Infrastructure"
    TypeName = "SessionService"
    MaxAggregateSourceLines = 700
    TrackingIssue = "#138"
  }
)

foreach ($budget in $aggregateBudgets) {
  $escapedType = [regex]::Escape($budget.TypeName)
  $declarationPattern = "(?m)^\s*(?:(?:public|internal|private|protected|file)\s+)*(?:(?:abstract|sealed|static|partial|readonly|ref|unsafe|new)\s+)*(?:class|record|struct)\s+$escapedType\b"
  $matchingFiles = @(
    foreach ($file in $sourceFiles) {
      $text = Get-Content -LiteralPath $file.FullName -Raw
      $namespaceMatch = [regex]::Match(
        $text,
        '(?m)^\s*namespace\s+([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)\s*[;{]'
      )
      if ($namespaceMatch.Success -and
          $namespaceMatch.Groups[1].Value -eq $budget.Namespace -and
          [regex]::IsMatch($text, $declarationPattern)) {
        $file
      }
    }
  )

  if ($matchingFiles.Count -eq 0) {
    throw "Aggregate source guard could not find $($budget.Namespace).$($budget.TypeName); review the guard if the type was intentionally moved or renamed."
  }

  $aggregateLineCount = 0
  foreach ($file in $matchingFiles) {
    $aggregateLineCount += @((Get-Content -LiteralPath $file.FullName)).Count
  }

  if ($aggregateLineCount -gt $budget.MaxAggregateSourceLines) {
    $paths = ($matchingFiles | ForEach-Object { $_.FullName }) -join ", "
    throw "Aggregate source guard failed: $($budget.Namespace).$($budget.TypeName) uses $aggregateLineCount source lines across $($matchingFiles.Count) file(s), limit $($budget.MaxAggregateSourceLines). Files: $paths. Track/decompose under $($budget.TrackingIssue); splitting a partial class is not a reduction in class responsibility."
  }

  Write-Host "Aggregate budget passed: $($budget.Namespace).$($budget.TypeName) = $aggregateLineCount / $($budget.MaxAggregateSourceLines) source lines across $($matchingFiles.Count) file(s)."
}

Write-Host "Source-size guard passed."
