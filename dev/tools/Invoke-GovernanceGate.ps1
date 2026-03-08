<#
.SYNOPSIS
  Enforces module-boundary and complexity governance rules.
.DESCRIPTION
  Checks all PowerShell module files for:
  - Maximum file size (lines)
  - Maximum function length (lines)
  - Maximum function count per file
  Reports violations and exits with non-zero code if any hard limits are exceeded.
  Designed to run in CI as a blocking gate or locally as a pre-commit check.
.EXAMPLE
  pwsh -NoProfile -File dev/tools/Invoke-GovernanceGate.ps1
  pwsh -NoProfile -File dev/tools/Invoke-GovernanceGate.ps1 -WarnOnly
#>
[CmdletBinding()]
param(
  [string]$ModulesDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'dev' 'modules'),
  [string]$ReportsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'reports'),
  [int]$MaxFileLinesHard = 4000,
  [int]$MaxFileLinesWarn = 2000,
  [int]$MaxFunctionLinesHard = 500,
  [int]$MaxFunctionLinesWarn = 200,
  [int]$MaxFunctionsPerFileHard = 60,
  [int]$MaxFunctionsPerFileWarn = 30,
  [switch]$WarnOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Collect all .ps1 files ───────────────────────────────────────────────────
$files = @(Get-ChildItem -Path $ModulesDir -Filter '*.ps1' -Recurse -File -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
  Write-Host '[Governance] No module files found.' -ForegroundColor Yellow
  exit 0
}

$violations = New-Object System.Collections.Generic.List[psobject]
$warnings   = New-Object System.Collections.Generic.List[psobject]
$fileStats  = New-Object System.Collections.Generic.List[psobject]

foreach ($file in $files) {
  $lines = @(Get-Content -LiteralPath $file.FullName -ErrorAction SilentlyContinue)
  $lineCount = $lines.Count
  $relativePath = $file.FullName.Replace($ModulesDir, '').TrimStart('\', '/')

  # ── Parse function boundaries ──────────────────────────────────────────
  $functions = New-Object System.Collections.Generic.List[psobject]
  $currentFunc = $null
  $braceDepth = 0

  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]

    if ($null -eq $currentFunc -and $line -match '^\s*function\s+([A-Za-z0-9_-]+)') {
      $currentFunc = @{ Name = $Matches[1]; StartLine = $i + 1; BraceDepth = 0 }
    }

    if ($null -ne $currentFunc) {
      $opens  = ([regex]::Matches($line, '\{')).Count
      $closes = ([regex]::Matches($line, '\}')).Count
      $currentFunc.BraceDepth += ($opens - $closes)

      if ($currentFunc.BraceDepth -le 0 -and $i -gt $currentFunc.StartLine) {
        $funcLength = ($i + 1) - $currentFunc.StartLine
        [void]$functions.Add([pscustomobject]@{
          Name      = $currentFunc.Name
          StartLine = $currentFunc.StartLine
          EndLine   = $i + 1
          Lines     = $funcLength
        })
        $currentFunc = $null
      }
    }
  }

  $funcCount = $functions.Count
  $longestFunc = if ($functions.Count -gt 0) { ($functions | Sort-Object Lines -Descending | Select-Object -First 1) } else { $null }
  $longestFuncLines = if ($longestFunc) { $longestFunc.Lines } else { 0 }
  $longestFuncName  = if ($longestFunc) { $longestFunc.Name } else { '–' }

  [void]$fileStats.Add([pscustomobject]@{
    File             = $relativePath
    TotalLines       = $lineCount
    FunctionCount    = $funcCount
    LongestFunction  = $longestFuncName
    LongestFuncLines = $longestFuncLines
  })

  # ── Check file-level limits ────────────────────────────────────────────
  if ($lineCount -gt $MaxFileLinesHard) {
    [void]$violations.Add([pscustomobject]@{
      Level   = 'ERROR'
      File    = $relativePath
      Rule    = 'MaxFileLines'
      Value   = $lineCount
      Limit   = $MaxFileLinesHard
      Detail  = "File has $lineCount lines (hard limit: $MaxFileLinesHard)"
    })
  } elseif ($lineCount -gt $MaxFileLinesWarn) {
    [void]$warnings.Add([pscustomobject]@{
      Level   = 'WARN'
      File    = $relativePath
      Rule    = 'MaxFileLines'
      Value   = $lineCount
      Limit   = $MaxFileLinesWarn
      Detail  = "File has $lineCount lines (warn limit: $MaxFileLinesWarn)"
    })
  }

  # ── Check function count ───────────────────────────────────────────────
  if ($funcCount -gt $MaxFunctionsPerFileHard) {
    [void]$violations.Add([pscustomobject]@{
      Level   = 'ERROR'
      File    = $relativePath
      Rule    = 'MaxFunctionsPerFile'
      Value   = $funcCount
      Limit   = $MaxFunctionsPerFileHard
      Detail  = "File has $funcCount functions (hard limit: $MaxFunctionsPerFileHard)"
    })
  } elseif ($funcCount -gt $MaxFunctionsPerFileWarn) {
    [void]$warnings.Add([pscustomobject]@{
      Level   = 'WARN'
      File    = $relativePath
      Rule    = 'MaxFunctionsPerFile'
      Value   = $funcCount
      Limit   = $MaxFunctionsPerFileWarn
      Detail  = "File has $funcCount functions (warn limit: $MaxFunctionsPerFileWarn)"
    })
  }

  # ── Check individual function lengths ──────────────────────────────────
  foreach ($fn in $functions) {
    if ($fn.Lines -gt $MaxFunctionLinesHard) {
      [void]$violations.Add([pscustomobject]@{
        Level   = 'ERROR'
        File    = $relativePath
        Rule    = 'MaxFunctionLines'
        Value   = $fn.Lines
        Limit   = $MaxFunctionLinesHard
        Detail  = "Function '$($fn.Name)' has $($fn.Lines) lines (hard limit: $MaxFunctionLinesHard)"
      })
    } elseif ($fn.Lines -gt $MaxFunctionLinesWarn) {
      [void]$warnings.Add([pscustomobject]@{
        Level   = 'WARN'
        File    = $relativePath
        Rule    = 'MaxFunctionLines'
        Value   = $fn.Lines
        Limit   = $MaxFunctionLinesWarn
        Detail  = "Function '$($fn.Name)' has $($fn.Lines) lines (warn limit: $MaxFunctionLinesWarn)"
      })
    }
  }
}

# ── Report ───────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '=== Governance Gate Report ===' -ForegroundColor Cyan
Write-Host ("Files scanned: {0}" -f $files.Count)
Write-Host ''

# Top-5 largest files
Write-Host 'Top-5 largest files:' -ForegroundColor Cyan
$fileStats | Sort-Object TotalLines -Descending | Select-Object -First 5 | ForEach-Object {
  $color = if ($_.TotalLines -gt $MaxFileLinesHard) { 'Red' } elseif ($_.TotalLines -gt $MaxFileLinesWarn) { 'Yellow' } else { 'DarkGray' }
  Write-Host ("  {0,5} lines | {1,3} funcs | {2}" -f $_.TotalLines, $_.FunctionCount, $_.File) -ForegroundColor $color
}

if ($warnings.Count -gt 0) {
  Write-Host ''
  Write-Host ("Warnings: {0}" -f $warnings.Count) -ForegroundColor Yellow
  foreach ($w in $warnings) {
    Write-Host ("  [WARN] {0}: {1}" -f $w.File, $w.Detail) -ForegroundColor Yellow
  }
}

if ($violations.Count -gt 0) {
  Write-Host ''
  Write-Host ("Violations: {0}" -f $violations.Count) -ForegroundColor Red
  foreach ($v in $violations) {
    Write-Host ("  [ERROR] {0}: {1}" -f $v.File, $v.Detail) -ForegroundColor Red
  }
}

# ── Export JSON report ───────────────────────────────────────────────────────
$reportPath = Join-Path $ReportsDir 'governance-gate-latest.json'
try {
  $report = [ordered]@{
    Timestamp        = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss')
    FilesScanned     = $files.Count
    ViolationCount   = $violations.Count
    WarningCount     = $warnings.Count
    Limits           = [ordered]@{
      MaxFileLinesHard       = $MaxFileLinesHard
      MaxFileLinesWarn       = $MaxFileLinesWarn
      MaxFunctionLinesHard   = $MaxFunctionLinesHard
      MaxFunctionLinesWarn   = $MaxFunctionLinesWarn
      MaxFunctionsPerFileHard = $MaxFunctionsPerFileHard
      MaxFunctionsPerFileWarn = $MaxFunctionsPerFileWarn
    }
    Files            = @($fileStats)
    Violations       = @($violations)
    Warnings         = @($warnings)
  }
  $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8 -Force
  Write-Host ''
  Write-Host "Report: $reportPath" -ForegroundColor DarkGray
} catch {
  Write-Warning "Could not write governance report: $($_.Exception.Message)"
}

# ── Exit code ────────────────────────────────────────────────────────────────
Write-Host ''
if ($violations.Count -gt 0 -and -not $WarnOnly) {
  Write-Host "[FAIL] $($violations.Count) governance violation(s) found." -ForegroundColor Red
  exit 1
} else {
  $status = if ($warnings.Count -gt 0) { 'PASS (with warnings)' } else { 'PASS' }
  Write-Host "[OK] Governance gate: $status" -ForegroundColor Green
  exit 0
}
