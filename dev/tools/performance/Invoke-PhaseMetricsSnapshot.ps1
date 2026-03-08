[CmdletBinding()]
param(
  [string]$ReportsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'reports')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$latest = Join-Path $ReportsDir 'test-pipeline-latest.json'
if (-not (Test-Path -LiteralPath $latest -PathType Leaf)) {
  [pscustomobject]@{ Timestamp=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); Available=$false; Message='No pipeline report found' }
  return
}

$json = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
$stageRows = New-Object System.Collections.Generic.List[object]

if ($json.PSObject.Properties.Name -contains 'Stages' -and $json.Stages) {
  foreach ($stage in @($json.Stages)) {
    [void]$stageRows.Add([pscustomobject]@{
      Stage = [string]$stage.Name
      DurationSeconds = [double]($stage.DurationSeconds)
      Passed = [bool]($stage.Passed)
      Files = [int]($stage.FileCount)
    })
  }
} else {
  $summary = $json.Summary
  $duration = 0.0
  $files = 0
  $passed = $true
  try { $duration = [double]$summary.DurationSeconds } catch { }
  try { $files = [int]$summary.TotalFiles } catch { }
  try { $passed = [int]$summary.FailedCount -eq 0 } catch { }

  [void]$stageRows.Add([pscustomobject]@{
    Stage = [string]$json.Stage
    DurationSeconds = $duration
    Passed = $passed
    Files = $files
  })
}

[pscustomobject]@{
  Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  Available = $true
  Metrics = [pscustomobject]@{
    FilesPerSecond = 0
    HashPerSecond = 0
    QueueLatencyMs = 0
    Note = 'Scaffold metrics in place; wire runtime counters for live values.'
  }
  Stages = $stageRows.ToArray()
}
