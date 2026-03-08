[CmdletBinding()]
param(
  [string]$ModulesDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'dev' 'modules'),
  [string]$ReportsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'reports'),
  [switch]$WarnOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$files = @(Get-ChildItem -Path $ModulesDir -Filter '*.ps1' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike 'Wpf*' })
if ($files.Count -eq 0) {
  Write-Host '[SilentCatchGate] No module files found.' -ForegroundColor Yellow
  exit 0
}

function Test-CatchBlockAllowed {
  param([string]$CatchBody)

  $body = [string]$CatchBody
  if ([string]::IsNullOrWhiteSpace($body)) { return $false }

  if ($body -match 'Write-CatchGuardLog|Write-Log|Write-Warning|throw\b') { return $true }
  if ($body -match '#\s*CATCH-GUARD-ALLOW') { return $true }
  if ($body -match 'Kill\(|Stop-Process|\.Stop\(|\.Dispose\(|\.Close\(') { return $true }
  if ($body -match 'Remove-Item|Remove-Variable') { return $true }

  return $false
}

$violations = New-Object System.Collections.Generic.List[psobject]

foreach ($file in $files) {
  $lines = @(Get-Content -LiteralPath $file.FullName -ErrorAction SilentlyContinue)
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = [string]$lines[$i]
    if ($line -notmatch '\bcatch\b') { continue }

    $startLine = $i + 1
    $openIndex = $line.IndexOf('{')
    if ($openIndex -lt 0) { continue }

    $capture = New-Object System.Collections.Generic.List[string]
    $depth = 0
    $started = $false

    for ($j = $i; $j -lt $lines.Count; $j++) {
      $current = [string]$lines[$j]
      if (-not $started) {
        $bracePos = $current.IndexOf('{')
        if ($bracePos -lt 0) { continue }
        $started = $true
      }

      [void]$capture.Add($current)
      $depth += ([regex]::Matches($current, '\{')).Count
      $depth -= ([regex]::Matches($current, '\}')).Count

      if ($started -and $depth -le 0) {
        $i = $j
        break
      }
    }

    $body = ($capture -join [Environment]::NewLine)
    if (-not (Test-CatchBlockAllowed -CatchBody $body)) {
      $relativePath = $file.FullName.Replace($ModulesDir, '').TrimStart([char[]]@([char]'\', [char]'/'))
      [void]$violations.Add([pscustomobject]@{
        File = $relativePath
        Line = $startLine
        Rule = 'NoSilentCatchCorePaths'
        Detail = 'Catch block without explicit logging/throw or approved best-effort marker.'
      })
    }
  }
}

if (-not (Test-Path -LiteralPath $ReportsDir -PathType Container)) {
  [void](New-Item -ItemType Directory -Path $ReportsDir -Force)
}

$reportPath = Join-Path $ReportsDir 'silent-catch-gate-latest.json'
$report = [ordered]@{
  Timestamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss')
  FilesScanned = $files.Count
  ViolationCount = $violations.Count
  Violations = @($violations)
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8 -Force

if ($violations.Count -gt 0) {
  Write-Host ("[SilentCatchGate] Violations: {0}" -f $violations.Count) -ForegroundColor Yellow
  foreach ($v in $violations | Select-Object -First 20) {
    Write-Host ("  [WARN] {0}:{1} {2}" -f $v.File, $v.Line, $v.Detail) -ForegroundColor Yellow
  }
  Write-Host ("Report: {0}" -f $reportPath) -ForegroundColor DarkGray

  if (-not $WarnOnly) { exit 1 }
  exit 0
}

Write-Host '[SilentCatchGate] PASS' -ForegroundColor Green
Write-Host ("Report: {0}" -f $reportPath) -ForegroundColor DarkGray
exit 0
