[CmdletBinding()]
param(
  [int]$LookbackDays = 30,
  [bool]$UseSyntheticWhenMissing = $true,
  [switch]$NoLatest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$reportsDir = Join-Path $repoRoot 'reports'
$testsRoot = Join-Path $repoRoot 'dev\tests'

function Get-JsonlEvents {
  param([object[]]$Paths)
  $events = New-Object System.Collections.Generic.List[object]
  $normalizedPaths = New-Object System.Collections.Generic.List[string]
  foreach ($candidate in $Paths) {
    if ($null -eq $candidate) { continue }
    $pathText = [string]$candidate
    if ([string]::IsNullOrWhiteSpace($pathText)) { continue }
    [void]$normalizedPaths.Add($pathText)
  }

  foreach ($path in $normalizedPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    foreach ($line in (Get-Content -LiteralPath $path -ErrorAction SilentlyContinue)) {
      if ([string]::IsNullOrWhiteSpace([string]$line)) { continue }
      try { [void]$events.Add(($line | ConvertFrom-Json)) } catch { }
    }
  }
  return $events.ToArray()
}

function Get-DryRunProbeMinutes {
  param([string]$TestsRoot)

  . (Join-Path $TestsRoot 'TestScriptLoader.ps1')
  $ctx = New-SimpleSortTestScript -TestsRoot $TestsRoot -TempPrefix 'ux_kpi_snapshot'
  $tempRoot = $null
  $tempScript = $ctx.TempScript
  try {
    . $tempScript

    $tempRoot = Join-Path $env:TEMP ('ux-kpi-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $sample = Join-Path $tempRoot 'Smoke Game (Europe).chd'
    'x' | Out-File -LiteralPath $sample -Encoding ascii -Force

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    [void](Invoke-RegionDedupe -Roots @($tempRoot) -Mode 'DryRun' -PreferOrder @('EU','US','WORLD','JP') -IncludeExtensions @('.chd') -RemoveJunk $true -SeparateBios $false -GenerateReportsInDryRun $true -UseDat $false -Log { param($m) })
    $sw.Stop()

    return [math]::Round(($sw.Elapsed.TotalMinutes), 3)
  }
  finally {
    if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
      Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-SimpleSortTestTempScript -TempScript $tempScript
  }
}

$sinceUtc = (Get-Date).ToUniversalTime().AddDays(-1 * [math]::Abs($LookbackDays))
$reportFiles = @(Get-ChildItem -Path $reportsDir -Filter 'rom-cleanup-report-*.json' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
$telemetryFiles = @(Get-ChildItem -Path $reportsDir -Filter 'ui-telemetry-*.jsonl' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)

$reportRows = New-Object System.Collections.Generic.List[object]
foreach ($file in $reportFiles) {
  try {
    $json = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if (-not $json.generatedAtUtc) { continue }
    $ts = [datetime]$json.generatedAtUtc
    if ($ts.ToUniversalTime() -lt $sinceUtc) { continue }
    [void]$reportRows.Add([pscustomobject]@{
      TimestampUtc = $ts.ToUniversalTime()
      Mode = [string]$json.mode
      Summary = $json.summary
      File = $file.Name
    })
  } catch { }
}

$telemetryPaths = New-Object System.Collections.Generic.List[string]
foreach ($telemetryFile in $telemetryFiles) {
  $pathText = [string]$telemetryFile.FullName
  if ([string]::IsNullOrWhiteSpace($pathText)) { continue }
  [void]$telemetryPaths.Add($pathText)
}

$events = @(Get-JsonlEvents -Paths $telemetryPaths.ToArray() | Where-Object {
  try { ([datetime]$_.timestamp).ToUniversalTime() -ge $sinceUtc } catch { $false }
})

$runIntentEvents = @($events | Where-Object { [string]$_.event -eq 'run_intent' })
$runCompletedEvents = @($events | Where-Object { [string]$_.event -eq 'run_completed' })
$blockedEvents = @($events | Where-Object { [string]$_.event -in @('pre_run_blocked','preflight_blocked','validation_error') })
$abortEvents = @($events | Where-Object { [string]$_.event -in @('run_aborted_user','operation_cancelled') })
$rollbackEvents = @($events | Where-Object { [string]$_.event -eq 'rollback_completed' })

$firstDryRunMinutes = $null
$firstDryRunSource = 'telemetry'
if ($runIntentEvents.Count -gt 0 -and $runCompletedEvents.Count -gt 0) {
  $firstIntent = [datetime](($runIntentEvents | Sort-Object { [datetime]$_.timestamp } | Select-Object -First 1).timestamp)
  $firstDryRunComplete = $runCompletedEvents |
    Where-Object { [string]$_.data.mode -eq 'DryRun' -and [bool]$_.data.success } |
    Sort-Object { [datetime]$_.timestamp } |
    Select-Object -First 1
  if ($firstDryRunComplete) {
    $firstDone = [datetime]$firstDryRunComplete.timestamp
    $firstDryRunMinutes = [math]::Round((($firstDone.ToUniversalTime() - $firstIntent.ToUniversalTime()).TotalMinutes), 3)
  }
}

if ($null -eq $firstDryRunMinutes) {
  if ($UseSyntheticWhenMissing) {
    $firstDryRunMinutes = Get-DryRunProbeMinutes -TestsRoot $testsRoot
    $firstDryRunSource = 'synthetic-probe'
  } else {
    $firstDryRunMinutes = 0
    $firstDryRunSource = 'no-data'
  }
}

$runIntentCount = [int]$runIntentEvents.Count
if ($runIntentCount -le 0) { $runIntentCount = [int]$reportRows.Count }

$abortRatePct = if ($runIntentCount -gt 0) { [math]::Round(((@($abortEvents).Count * 100.0) / $runIntentCount), 2) } else { 0.0 }
$misconfigRatePct = if ($runIntentCount -gt 0) { [math]::Round(((@($blockedEvents).Count * 100.0) / $runIntentCount), 2) } else { 0.0 }

$moveRunCount = [int](@($runCompletedEvents | Where-Object { [string]$_.data.mode -eq 'Move' -and [bool]$_.data.success }).Count)
if ($moveRunCount -le 0) { $moveRunCount = [int](@($reportRows | Where-Object { [string]$_.Mode -eq 'Move' }).Count) }
$rollbackRatePct = if ($moveRunCount -gt 0) { [math]::Round(((@($rollbackEvents).Count * 100.0) / $moveRunCount), 2) } else { 0.0 }

$result = [ordered]@{
  Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  LookbackDays = [int]$LookbackDays
  Data = [ordered]@{
    ReportCount = [int]$reportRows.Count
    TelemetryFiles = [int]$telemetryFiles.Count
    TelemetryEvents = [int]$events.Count
    RunIntents = [int]$runIntentEvents.Count
    RunCompletions = [int]$runCompletedEvents.Count
  }
  Kpi = [ordered]@{
    TimeToFirstSuccessfulDryRunMinutes = [double]$firstDryRunMinutes
    TimeToFirstSuccessfulDryRunSource = $firstDryRunSource
    TimeToFirstSuccessfulDryRunTargetMinutes = 3.0
    TimeToFirstSuccessfulDryRunPassed = ([double]$firstDryRunMinutes -lt 3.0)

    SetupAbortRatePercent = [double]$abortRatePct
    SetupAbortRateTargetPercent = 15.0
    SetupAbortRatePassed = ([double]$abortRatePct -lt 15.0)

    PreRunMisconfigurationRatePercent = [double]$misconfigRatePct
    PreRunMisconfigurationRateTargetPercent = 10.0
    PreRunMisconfigurationRatePassed = ([double]$misconfigRatePct -lt 10.0)

    RollbackAfterMoveRatePercent = [double]$rollbackRatePct
    RollbackAfterMoveRateTargetPercent = 2.0
    RollbackAfterMoveRatePassed = ([double]$rollbackRatePct -lt 2.0)
  }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$jsonPath = Join-Path $reportsDir ("ux-kpi-snapshot-{0}.json" -f $stamp)
$mdPath = Join-Path $reportsDir ("ux-kpi-snapshot-{0}.md" -f $stamp)
$latestJsonPath = Join-Path $reportsDir 'ux-kpi-snapshot-latest.json'
$latestMdPath = Join-Path $reportsDir 'ux-kpi-snapshot-latest.md'

$result | ConvertTo-Json -Depth 8 | Out-File -LiteralPath $jsonPath -Encoding utf8 -Force

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add('# UX KPI Snapshot')
[void]$lines.Add('')
[void]$lines.Add(('Timestamp: {0}' -f $result.Timestamp))
[void]$lines.Add(('Lookback: {0} Tage' -f $result.LookbackDays))
[void]$lines.Add(('Reports: {0} | Telemetry files: {1} | Telemetry events: {2}' -f $result.Data.ReportCount, $result.Data.TelemetryFiles, $result.Data.TelemetryEvents))
[void]$lines.Add('')
[void]$lines.Add('## KPI')
[void]$lines.Add(('- Time-to-first-successful-dryrun: {0} min (Quelle={1}, Ziel<3, Passed={2})' -f $result.Kpi.TimeToFirstSuccessfulDryRunMinutes, $result.Kpi.TimeToFirstSuccessfulDryRunSource, $result.Kpi.TimeToFirstSuccessfulDryRunPassed))
[void]$lines.Add(('- Abbruchquote Setup: {0}% (Ziel<15, Passed={1})' -f $result.Kpi.SetupAbortRatePercent, $result.Kpi.SetupAbortRatePassed))
[void]$lines.Add(('- Fehlkonfigurationsrate vor Run: {0}% (Ziel<10, Passed={1})' -f $result.Kpi.PreRunMisconfigurationRatePercent, $result.Kpi.PreRunMisconfigurationRatePassed))
[void]$lines.Add(('- Move-Runs mit Rollback: {0}% (Ziel<2, Passed={1})' -f $result.Kpi.RollbackAfterMoveRatePercent, $result.Kpi.RollbackAfterMoveRatePassed))
[void]$lines.Add('')
[void]$lines.Add(('JSON: {0}' -f $jsonPath))
[void]$lines.Add(('MD: {0}' -f $mdPath))
$lines | Out-File -LiteralPath $mdPath -Encoding utf8 -Force

if (-not $NoLatest) {
  Copy-Item -LiteralPath $jsonPath -Destination $latestJsonPath -Force
  Copy-Item -LiteralPath $mdPath -Destination $latestMdPath -Force
}

[pscustomobject]@{
  JsonPath = $jsonPath
  MdPath = $mdPath
  LatestJsonPath = $(if ($NoLatest) { $null } else { $latestJsonPath })
  LatestMdPath = $(if ($NoLatest) { $null } else { $latestMdPath })
  Result = $result
}
