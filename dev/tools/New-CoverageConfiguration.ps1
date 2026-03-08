# ================================================================
#  New-CoverageConfiguration.ps1 – Pester Coverage Config Helper
# ================================================================
# Centralizes Pester coverage configuration for reuse across
# Invoke-TestPipeline.ps1 and ad-hoc developer runs.
# ================================================================

function New-CoverageConfiguration {
  <#
  .SYNOPSIS
    Creates a PesterConfiguration object pre-configured for code coverage.
  .DESCRIPTION
    Returns a ready-to-use PesterConfiguration with CodeCoverage enabled,
    targeting the project's module files. Supports custom test paths,
    coverage targets, output formats, and path filters.
  .PARAMETER TestPaths
    Array of test file paths to include in the coverage run.
    Defaults to all *.Tests.ps1 under dev\tests.
  .PARAMETER CoveragePaths
    Array of source file glob patterns to measure coverage against.
    Defaults to dev\modules\*.ps1.
  .PARAMETER CoverageTarget
    Minimum coverage percentage (1-100). Default: 34.
  .PARAMETER OutputFormat
    Pester coverage output format: JaCoCo or CoverageGutters. Default: JaCoCo.
  .PARAMETER OutputPath
    Path for the coverage report file. If empty, defaults to reports\coverage-latest.xml.
  .PARAMETER PesterVerbosity
    Pester output verbosity: None, Normal, Detailed, Diagnostic. Default: None.
  .EXAMPLE
    $cfg = New-CoverageConfiguration -CoverageTarget 50
    $result = Invoke-Pester -Configuration $cfg
  .EXAMPLE
    $cfg = New-CoverageConfiguration -TestPaths @('dev\tests\LruCache.Tests.ps1') -CoveragePaths @('dev\modules\LruCache.ps1')
    $result = Invoke-Pester -Configuration $cfg
  #>
  [CmdletBinding()]
  param(
    [string[]]$TestPaths = @(),
    [string[]]$CoveragePaths = @(),
    [ValidateRange(1,100)]
    [int]$CoverageTarget = 34,
    [ValidateSet('JaCoCo','CoverageGutters')]
    [string]$OutputFormat = 'JaCoCo',
    [string]$OutputPath = '',
    [ValidateSet('None','Normal','Detailed','Diagnostic')]
    [string]$PesterVerbosity = 'None'
  )

  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

  # Default test paths: all test files
  if ($TestPaths.Count -eq 0) {
    $testsRoot = Join-Path $repoRoot 'dev\tests'
    $TestPaths = @(Get-ChildItem -Path $testsRoot -Filter '*.Tests.ps1' -Recurse | Select-Object -ExpandProperty FullName)
  }

  # Default coverage paths: all module source files
  if ($CoveragePaths.Count -eq 0) {
    $CoveragePaths = @((Join-Path $repoRoot 'dev\modules\*.ps1'))
  }

  # Default output path
  if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $reportsDir = Join-Path $repoRoot 'reports'
    if (-not (Test-Path -LiteralPath $reportsDir)) {
      [void](New-Item -ItemType Directory -Path $reportsDir -Force)
    }
    $OutputPath = Join-Path $reportsDir 'coverage-latest.xml'
  }

  $cfg = New-PesterConfiguration
  $cfg.Run.Path = @($TestPaths)
  $cfg.Run.PassThru = $true
  $cfg.Output.Verbosity = $PesterVerbosity
  $cfg.CodeCoverage.Enabled = $true
  $cfg.CodeCoverage.Path = @($CoveragePaths)
  $cfg.CodeCoverage.OutputFormat = $OutputFormat
  $cfg.CodeCoverage.OutputPath = $OutputPath
  $cfg.CodeCoverage.CoveragePercentTarget = [decimal]$CoverageTarget

  return $cfg
}

function Invoke-CoverageCheck {
  <#
  .SYNOPSIS
    Runs Pester with coverage and returns a pass/fail result.
  .DESCRIPTION
    Convenience wrapper that creates a coverage configuration,
    runs Pester, and evaluates coverage against the target threshold.
  .PARAMETER CoverageTarget
    Minimum coverage percentage. Default: 34.
  .PARAMETER TestPaths
    Optional test file paths. Defaults to all tests.
  .PARAMETER CoveragePaths
    Optional source paths. Defaults to all modules.
  #>
  [CmdletBinding()]
  param(
    [ValidateRange(1,100)]
    [int]$CoverageTarget = 34,
    [string[]]$TestPaths = @(),
    [string[]]$CoveragePaths = @()
  )

  $cfg = New-CoverageConfiguration -CoverageTarget $CoverageTarget -TestPaths $TestPaths -CoveragePaths $CoveragePaths
  $result = Invoke-Pester -Configuration $cfg

  $coveragePercent = 0
  if ($result -and $result.CodeCoverage) {
    $coveragePercent = [decimal]$result.CodeCoverage.CoveragePercent
  }

  $passed = $coveragePercent -ge [decimal]$CoverageTarget

  return [pscustomobject]@{
    Passed              = [bool]$passed
    CoveragePercent     = $coveragePercent
    CoverageTarget      = $CoverageTarget
    CommandsAnalyzed    = if ($result.CodeCoverage) { [int64]$result.CodeCoverage.CommandsAnalyzedCount } else { 0 }
    CommandsExecuted    = if ($result.CodeCoverage) { [int64]$result.CodeCoverage.CommandsExecutedCount } else { 0 }
    CommandsMissed      = if ($result.CodeCoverage) { [int64]$result.CodeCoverage.CommandsMissedCount } else { 0 }
    FilesAnalyzed       = if ($result.CodeCoverage) { [int64]$result.CodeCoverage.FilesAnalyzedCount } else { 0 }
    TestsPassed         = [int]$result.PassedCount
    TestsFailed         = [int]$result.FailedCount
    PesterResult        = $result
  }
}
