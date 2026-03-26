<#
.SYNOPSIS
    Runs the CoverageGate xUnit tests and reports pass/fail summary.

.DESCRIPTION
    Executes all tests with [Trait("Category", "CoverageGate")] from the test project.
    Returns exit code 0 if all structural + gate tests pass, 1 otherwise.

    Usage:
      pwsh -NoProfile -File benchmark/tools/Invoke-CoverageGate.ps1
      pwsh -NoProfile -File benchmark/tools/Invoke-CoverageGate.ps1 -Filter "Gate_TotalEntries"
      pwsh -NoProfile -File benchmark/tools/Invoke-CoverageGate.ps1 -Verbose

.PARAMETER Filter
    Optional additional test name filter (dotnet test --filter syntax).

.PARAMETER NoBuild
    Skip build before running tests.
#>
[CmdletBinding()]
param(
    [string]$Filter,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testProj = Join-Path $repoRoot 'src' 'RomCleanup.Tests' 'RomCleanup.Tests.csproj'

if (-not (Test-Path $testProj)) {
    Write-Error "Test project not found: $testProj"
    exit 1
}

$filterExpr = 'Category=CoverageGate'
if ($Filter) {
    $filterExpr = "$filterExpr&FullyQualifiedName~$Filter"
}

$dotnetArgs = @(
    'test', $testProj,
    '--filter', $filterExpr,
    '--nologo',
    '--verbosity', 'normal'
)

if ($NoBuild) {
    $dotnetArgs += '--no-build'
}

Write-Host "=== Benchmark Coverage Gate ===" -ForegroundColor Cyan
Write-Host "Filter: $filterExpr"
Write-Host "Project: $testProj"
Write-Host ""

& dotnet @dotnetArgs
$exitCode = $LASTEXITCODE

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "=== ALL COVERAGE GATES PASSED ===" -ForegroundColor Green
}
else {
    Write-Host "=== COVERAGE GATES FAILED (exit code $exitCode) ===" -ForegroundColor Red
    Write-Host "Expand the ground-truth JSONL files to meet gate thresholds." -ForegroundColor Yellow
}

exit $exitCode
