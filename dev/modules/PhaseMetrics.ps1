<#
.SYNOPSIS
  Standardized phase metrics collector for pipeline runs.
.DESCRIPTION
  Provides Start-PhaseMetric / Complete-PhaseMetric / Get-PhaseMetrics / Write-PhaseMetricsSummary
  for unified timing of application phases (Scan, Classify, Group, Select, Report, Move).
#>

$script:PhaseMetrics = $null

function Initialize-PhaseMetrics {
  <#
  .SYNOPSIS  Resets the metrics collector for a new run.
  #>
  $script:PhaseMetrics = [ordered]@{
    RunId      = [guid]::NewGuid().ToString('N').Substring(0, 12)
    StartedAt  = [DateTime]::UtcNow
    Phases     = New-Object System.Collections.Generic.List[psobject]
    ActivePhase = $null
  }
  return $script:PhaseMetrics
}

function Start-PhaseMetric {
  <#
  .SYNOPSIS  Begins timing a named phase.
  #>
  param(
    [Parameter(Mandatory)][string]$Phase,
    [hashtable]$Meta = @{}
  )

  if (-not $script:PhaseMetrics) { Initialize-PhaseMetrics | Out-Null }

  # Auto-close previous phase if still active
  if ($script:PhaseMetrics.ActivePhase) {
    Complete-PhaseMetric -ItemCount 0
  }

  $entry = [pscustomobject]@{
    Phase     = $Phase
    StartedAt = [DateTime]::UtcNow
    Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    ItemCount = 0
    Meta      = $Meta
    Duration  = $null
    Status    = 'Running'
  }
  $script:PhaseMetrics.ActivePhase = $entry
  return $entry
}

function Complete-PhaseMetric {
  <#
  .SYNOPSIS  Completes the currently active phase and records its duration.
  #>
  param(
    [int]$ItemCount = 0,
    [string]$Status = 'OK'
  )

  if (-not $script:PhaseMetrics -or -not $script:PhaseMetrics.ActivePhase) { return }

  $active = $script:PhaseMetrics.ActivePhase
  $active.Stopwatch.Stop()
  $active.Duration  = $active.Stopwatch.Elapsed
  $active.ItemCount = $ItemCount
  $active.Status    = $Status

  [void]$script:PhaseMetrics.Phases.Add($active)
  $script:PhaseMetrics.ActivePhase = $null
  return $active
}

function Get-PhaseMetrics {
  <#
  .SYNOPSIS  Returns collected phase metrics as structured data.
  #>
  if (-not $script:PhaseMetrics) { return $null }

  $totalElapsed = ([DateTime]::UtcNow - $script:PhaseMetrics.StartedAt)
  $phases = @($script:PhaseMetrics.Phases | ForEach-Object {
    [pscustomobject]@{
      Phase          = $_.Phase
      DurationMs     = [Math]::Round($_.Duration.TotalMilliseconds, 1)
      DurationSec    = [Math]::Round($_.Duration.TotalSeconds, 2)
      ItemCount      = $_.ItemCount
      ItemsPerSec    = if ($_.Duration.TotalSeconds -gt 0 -and $_.ItemCount -gt 0) {
                         [Math]::Round($_.ItemCount / $_.Duration.TotalSeconds, 1)
                       } else { 0 }
      Status         = $_.Status
      PercentOfTotal = if ($totalElapsed.TotalMilliseconds -gt 0) {
                         [Math]::Round(($_.Duration.TotalMilliseconds / $totalElapsed.TotalMilliseconds) * 100, 1)
                       } else { 0 }
    }
  })

  return [pscustomobject]@{
    RunId            = $script:PhaseMetrics.RunId
    StartedAt        = $script:PhaseMetrics.StartedAt
    TotalElapsedSec  = [Math]::Round($totalElapsed.TotalSeconds, 2)
    PhaseCount       = $phases.Count
    Phases           = $phases
  }
}

function Write-PhaseMetricsSummary {
  <#
  .SYNOPSIS  Writes phase metrics summary as formatted log lines.
  .PARAMETER Log  Optional scriptblock logger. Falls back to Write-Host.
  #>
  param(
    [scriptblock]$Log = $null
  )

  $metrics = Get-PhaseMetrics
  if (-not $metrics -or $metrics.PhaseCount -eq 0) { return $metrics }

  $emit = if ($Log) { $Log } else { { param($msg) Write-Host $msg } }

  & $emit '=== Phasenmetriken ==='
  & $emit ('  Run: {0} | Gesamt: {1:N1}s | Phasen: {2}' -f $metrics.RunId, $metrics.TotalElapsedSec, $metrics.PhaseCount)

  foreach ($p in $metrics.Phases) {
    $throughput = if ($p.ItemsPerSec -gt 0) { ('{0:N1} items/s' -f $p.ItemsPerSec) } else { '–' }
    & $emit ('  {0,-16} {1,8:N1}s  {2,7:N1}%  {3,8} items  {4}  [{5}]' -f $p.Phase, $p.DurationSec, $p.PercentOfTotal, $p.ItemCount, $throughput, $p.Status)
  }

  return $metrics
}

function Write-PhaseMetricsToOperationLog {
  <#
  .SYNOPSIS  Writes phase metrics and LRU cache statistics into the operation JSONL log.
  #>
  param(
    [scriptblock]$Log = $null
  )

  $metrics = Get-PhaseMetrics
  if (-not $metrics) { return }

  $emit = if ($Log) { $Log } else { { param($msg) Write-Host $msg } }

  # Write phase metrics to operation log
  if (Get-Command Write-OperationJsonlLog -ErrorAction SilentlyContinue) {
    try {
      $metricsJson = ($metrics | ConvertTo-Json -Compress -Depth 5)
      Write-OperationJsonlLog -Timestamp (Get-Date) -MessageText ('PERF-METRICS: {0}' -f $metricsJson) -Level 'Info' -Module 'PhaseMetrics' -Action 'Summary'
    } catch {
      Write-Verbose ('[PhaseMetrics] Failed to write metrics to JSONL: {0}' -f $_.Exception.Message)
    }
  }

  # Write LRU cache statistics to operation log
  if (Get-Command Get-AllCacheStatistics -ErrorAction SilentlyContinue) {
    $cacheStats = @(Get-AllCacheStatistics)
    if ($cacheStats.Count -gt 0) {
      & $emit '=== Cache-Statistiken ==='
      foreach ($stat in $cacheStats) {
        $line = ('  {0,-20} {1,6}/{2,6} entries  HitRate: {3,5:N1}%  Hits: {4}  Misses: {5}  Evictions: {6}' -f $stat.Name, $stat.Count, $stat.MaxEntries, $stat.HitRate, $stat.Hits, $stat.Misses, $stat.Evictions)
        & $emit $line
      }

      if (Get-Command Write-OperationJsonlLog -ErrorAction SilentlyContinue) {
        try {
          $cacheJson = ($cacheStats | ConvertTo-Json -Compress -Depth 3)
          Write-OperationJsonlLog -Timestamp (Get-Date) -MessageText ('CACHE-STATS: {0}' -f $cacheJson) -Level 'Info' -Module 'LruCache' -Action 'Summary'
        } catch {
          Write-Verbose ('[PhaseMetrics] Failed to write cache stats to JSONL: {0}' -f $_.Exception.Message)
        }
      }
    }
  }
}
