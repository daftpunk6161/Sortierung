[CmdletBinding()]
param(
  [int]$ReadinessIterations = 30,
  [int]$DuplicateGroups = 5000,
  [int]$ResponsivenessDurationMs = 2200,
  [switch]$NoLatest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$testsRoot = Join-Path $repoRoot 'dev\tests'

. (Join-Path $testsRoot 'TestScriptLoader.ps1')
$ctx = New-SimpleSortTestScript -TestsRoot $testsRoot -TempPrefix 'gui_kpi_snapshot'

$tempRoot = $null
$tempScript = $ctx.TempScript

function New-FakeTextControl {
  param([string]$Text = '')
  return [pscustomobject]@{ Text = $Text }
}

function New-FakeCheckControl {
  param([bool]$Checked = $false)
  return [pscustomobject]@{ Checked = $Checked }
}

function New-FakeRadioControl {
  param([bool]$Checked = $false)
  return [pscustomobject]@{ Checked = $Checked }
}

function New-FakeLabelControl {
  return [pscustomobject]@{ Text = ''; ForeColor = $null }
}

try {
  . $tempScript
  Initialize-AppState

  $tempRoot = Join-Path $env:TEMP ('gui-kpi-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

  $readinessRoot = Join-Path $tempRoot 'ReadinessRoot'
  New-Item -ItemType Directory -Path $readinessRoot -Force | Out-Null

  $listRoots = [pscustomobject]@{ Items = New-Object System.Collections.ArrayList }
  [void]$listRoots.Items.Add($readinessRoot)

  $ui = @{
    ListRoots = $listRoots
    TxtExt = (New-FakeTextControl -Text '.chd,.cue,.iso')
    ChkDatUse = (New-FakeCheckControl -Checked $false)
    TxtDatRoot = (New-FakeTextControl -Text '')
    RbMove = (New-FakeRadioControl -Checked $false)
    ChkSafetyStrict = (New-FakeCheckControl -Checked $true)
    ChkConvert = (New-FakeCheckControl -Checked $false)
    TxtChdman = (New-FakeTextControl -Text '')
    TxtDolphin = (New-FakeTextControl -Text '')
    Txt7z = (New-FakeTextControl -Text '')
    TxtPsxtract = (New-FakeTextControl -Text '')
    TxtCiso = (New-FakeTextControl -Text '')
    LblReadinessScore = (New-FakeLabelControl)
    LblRunSummary = (New-FakeLabelControl)
  }

  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  for ($i = 0; $i -lt $ReadinessIterations; $i++) {
    Update-RunReadinessSummary -UiControls $ui
  }
  $sw.Stop()
  $readinessTotalMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
  $readinessAvgMs = [math]::Round(($sw.Elapsed.TotalMilliseconds / [math]::Max(1, $ReadinessIterations)), 2)

  $dupeRoot = Join-Path $tempRoot 'DuplicateRoot'
  New-Item -ItemType Directory -Path $dupeRoot -Force | Out-Null

  for ($g = 0; $g -lt $DuplicateGroups; $g++) {
    $folder = Join-Path $dupeRoot ('Batch{0}' -f ($g % 100))
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
      New-Item -ItemType Directory -Path $folder -Force | Out-Null
    }

    $nameEu = Join-Path $folder ('Game{0} (EU).chd' -f $g)
    $nameUs = Join-Path $folder ('Game{0} (US).chd' -f $g)
    'x' | Out-File -LiteralPath $nameEu -Encoding ascii -Force
    'x' | Out-File -LiteralPath $nameUs -Encoding ascii -Force
  }

  $ext = @('.chd')

  $sw.Restart()
  $rowsCold = @(Get-DuplicateInspectorRows -Roots @($dupeRoot) -Extensions $ext -AliasEditionKeying:$false)
  $sw.Stop()
  $dupeColdMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)

  $sw.Restart()
  $rowsWarm = @(Get-DuplicateInspectorRows -Roots @($dupeRoot) -Extensions $ext -AliasEditionKeying:$false)
  $sw.Stop()
  $dupeWarmMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)

  $result = [ordered]@{
    Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    Host = $env:COMPUTERNAME
    Pwsh = $PSVersionTable.PSVersion.ToString()
    Readiness = [ordered]@{
      Iterations = $ReadinessIterations
      TotalMs = $readinessTotalMs
      AvgMs = $readinessAvgMs
      ThresholdMs = 2000
      Passed = ($readinessAvgMs -lt 2000)
    }
    DuplicateInspector = [ordered]@{
      Groups = $DuplicateGroups
      CandidateCount = $rowsWarm.Count
      ColdMs = $dupeColdMs
      WarmMs = $dupeWarmMs
      ThresholdMs = 5000
      PassedCached = ($dupeWarmMs -lt 5000)
    }
    UiResponsiveness = [ordered]@{
      DurationMs = 0
      TickIntervalMs = 50
      FreezeThresholdMs = 200
      MaxStallMs = 0
      StallEvents = 0
      Samples = 0
      Passed = $false
    }
  }

  $resp = Measure-UiResponsivenessProbe -DurationMs $ResponsivenessDurationMs -TickIntervalMs 50 -FreezeThresholdMs 200
  $result.UiResponsiveness = [ordered]@{
    DurationMs = [double]$resp.DurationMs
    TickIntervalMs = [int]$resp.TickIntervalMs
    FreezeThresholdMs = [int]$resp.FreezeThresholdMs
    MaxStallMs = [double]$resp.MaxStallMs
    StallEvents = [int]$resp.StallEvents
    Samples = [int]$resp.Samples
    Passed = [bool]$resp.Passed
  }

  $reportsDir = Join-Path $repoRoot 'reports'
  if (-not (Test-Path -LiteralPath $reportsDir)) {
    New-Item -ItemType Directory -Path $reportsDir -Force | Out-Null
  }

  $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  $jsonPath = Join-Path $reportsDir ("gui-kpi-snapshot-{0}.json" -f $stamp)
  $mdPath = Join-Path $reportsDir ("gui-kpi-snapshot-{0}.md" -f $stamp)
  $latestJsonPath = Join-Path $reportsDir 'gui-kpi-snapshot-latest.json'
  $latestMdPath = Join-Path $reportsDir 'gui-kpi-snapshot-latest.md'

  $result | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $jsonPath -Encoding utf8 -Force

  $lines = New-Object System.Collections.Generic.List[string]
  [void]$lines.Add('# GUI KPI Snapshot')
  [void]$lines.Add('')
  [void]$lines.Add(('Timestamp: {0}' -f $result.Timestamp))
  [void]$lines.Add(('Host: {0}' -f $result.Host))
  [void]$lines.Add(('PowerShell: {0}' -f $result.Pwsh))
  [void]$lines.Add('')
  [void]$lines.Add('## Readiness')
  [void]$lines.Add(('- Avg: {0} ms (Iterations={1})' -f $result.Readiness.AvgMs, $result.Readiness.Iterations))
  [void]$lines.Add(('- Threshold: < {0} ms' -f $result.Readiness.ThresholdMs))
  [void]$lines.Add(('- Passed: {0}' -f $result.Readiness.Passed))
  [void]$lines.Add('')
  [void]$lines.Add('## Duplicate Inspector (cached)')
  [void]$lines.Add(('- Cold: {0} ms' -f $result.DuplicateInspector.ColdMs))
  [void]$lines.Add(('- Warm: {0} ms' -f $result.DuplicateInspector.WarmMs))
  [void]$lines.Add(('- Candidates: {0}' -f $result.DuplicateInspector.CandidateCount))
  [void]$lines.Add(('- Threshold: < {0} ms' -f $result.DuplicateInspector.ThresholdMs))
  [void]$lines.Add(('- Passed cached: {0}' -f $result.DuplicateInspector.PassedCached))
  [void]$lines.Add('')
  [void]$lines.Add('## UI Responsiveness')
  [void]$lines.Add(('- Duration: {0} ms' -f $result.UiResponsiveness.DurationMs))
  [void]$lines.Add(('- Max stall: {0} ms' -f $result.UiResponsiveness.MaxStallMs))
  [void]$lines.Add(('- Stall events (> {0} ms): {1}' -f $result.UiResponsiveness.FreezeThresholdMs, $result.UiResponsiveness.StallEvents))
  [void]$lines.Add(('- Threshold: max stall <= {0} ms' -f $result.UiResponsiveness.FreezeThresholdMs))
  [void]$lines.Add(('- Passed: {0}' -f $result.UiResponsiveness.Passed))
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
}
finally {
  if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  Remove-SimpleSortTestTempScript -TempScript $tempScript
}
