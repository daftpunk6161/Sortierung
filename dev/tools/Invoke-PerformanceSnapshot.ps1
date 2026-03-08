param(
  [int]$GameKeySamples = 50000,
  [int]$ConsoleSamples = 50000,
  [int]$ScanFiles = 6000,
  [int]$LargeCandidateSamples = 100000,
  [int]$LargeCandidateMemoryGateMB = 450,
  [switch]$SkipScan,
  [switch]$NoLatest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testsRoot = Join-Path $repoRoot 'dev\tests'

. (Join-Path $testsRoot 'TestScriptLoader.ps1')
$ctx = New-SimpleSortTestScript -TestsRoot $testsRoot -TempPrefix 'perf_snapshot'

$result = [ordered]@{
  Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  Host = $env:COMPUTERNAME
  Pwsh = $PSVersionTable.PSVersion.ToString()
  Samples = [ordered]@{
    GameKey = $GameKeySamples
    Console = $ConsoleSamples
    ScanFiles = $ScanFiles
  }
}

$scanRoot = $null
$tempScript = $ctx.TempScript

try {
  . $tempScript

  $nameSeeds = @(
    'Final Fantasy VII (Europe) (Disc 1)',
    'Castlevania III - Dracula''s Curse (USA, Europe) (USA Wii Virtual Console, Wii U Virtual Console)',
    'Mega Man 2 (USA) (Capcom Town)',
    'Akumajou Densetsu (World) (Ja) (Castlevania Anniversary Collection)',
    '3 Point Basketball (1994)(MVP Software)(C)',
    'Animal Life - Africa (Europe) (En,Fr,De,Es,It) (NDSi Enhanced)',
    'Battletoads (USA) (iam8bit)',
    'Ghosts''n Goblins (Europe)',
    'Abandoned Places - Zeit fuer Helden (Germany) (AGA)',
    'Rockman VI (Taiwan) (En) (Rockman 123)'
  )

  $gameKeyInputs = New-Object System.Collections.Generic.List[string]
  for ($i = 0; $i -lt $GameKeySamples; $i++) {
    $seed = $nameSeeds[$i % $nameSeeds.Count]
    $gameKeyInputs.Add(('{0} (Rev {1})' -f $seed, ($i % 27))) | Out-Null
  }

  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  foreach ($name in $gameKeyInputs) {
    [void](ConvertTo-GameKey -BaseName $name -AliasEditionKeying:$false)
  }
  $sw.Stop()
  $gameKeyStdMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)

  $sw.Restart()
  foreach ($name in $gameKeyInputs) {
    [void](ConvertTo-GameKey -BaseName $name -AliasEditionKeying:$true)
  }
  $sw.Stop()
  $gameKeyAliasMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)

  $result.GameKey = [ordered]@{
    StandardMs = $gameKeyStdMs
    AliasModeMs = $gameKeyAliasMs
    StandardPerSec = [math]::Round(($GameKeySamples / [math]::Max(0.001, ($gameKeyStdMs / 1000.0))), 1)
    AliasPerSec = [math]::Round(($GameKeySamples / [math]::Max(0.001, ($gameKeyAliasMs / 1000.0))), 1)
  }

  $consoleRoot = 'C:\ROMS\Sony\PlayStation\Collection'
  $consolePaths = New-Object System.Collections.Generic.List[psobject]
  for ($i = 0; $i -lt $ConsoleSamples; $i++) {
    $sub = ('Series{0}\Disc{1}' -f ($i % 60), ($i % 7))
    $path = Join-Path $consoleRoot ($sub + ('\Game{0}.chd' -f $i))
    $consolePaths.Add([pscustomobject]@{ Root = $consoleRoot; Path = $path; Ext = '.chd' }) | Out-Null
  }

  $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

  $sw.Restart()
  foreach ($row in $consolePaths) {
    [void](Get-ConsoleType -RootPath $row.Root -FilePath $row.Path -Extension $row.Ext)
  }
  $sw.Stop()
  $consoleColdMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)

  $sw.Restart()
  foreach ($row in $consolePaths) {
    [void](Get-ConsoleType -RootPath $row.Root -FilePath $row.Path -Extension $row.Ext)
  }
  $sw.Stop()
  $consoleWarmMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)

  $result.ConsoleType = [ordered]@{
    ColdMs = $consoleColdMs
    WarmMs = $consoleWarmMs
    SpeedupX = [math]::Round(($consoleColdMs / [math]::Max(1.0, $consoleWarmMs)), 2)
  }

  [System.GC]::Collect()
  [System.GC]::WaitForPendingFinalizers()
  [System.GC]::Collect()
  $largeBeforeBytes = [double][System.GC]::GetTotalMemory($true)

  $sw.Restart()
  $largeRows = New-Object System.Collections.Generic.List[psobject]
  for ($i = 0; $i -lt $LargeCandidateSamples; $i++) {
    $region = switch ($i % 4) { 0 { 'Europe' } 1 { 'USA' } 2 { 'Japan' } default { 'World' } }
    $largeRows.Add([pscustomobject]@{
      Key = ('game_{0}' -f ($i % 20000))
      RegionScore = 1000 - ($i % 10)
      FormatScore = 800 + ($i % 5)
      MainPath = ('C:\ROMs\Set{0}\Game{1} ({2}).chd' -f ($i % 400), $i, $region)
    }) | Out-Null
  }
  $largeGrouped = @($largeRows | Group-Object -Property Key)
  $largeSorted = @($largeRows | Sort-Object -Property @{Expression='RegionScore';Descending=$true}, @{Expression='FormatScore';Descending=$true}, @{Expression='MainPath';Descending=$false})
  $sw.Stop()

  $largeAfterBytes = [double][System.GC]::GetTotalMemory($true)
  $largeDeltaMB = [math]::Round([math]::Max(0, ($largeAfterBytes - $largeBeforeBytes) / 1MB), 2)
  $largeElapsedMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
  $largeGatePassed = ($largeDeltaMB -le $LargeCandidateMemoryGateMB)

  $result.LargeCandidateSet = [ordered]@{
    Samples = $LargeCandidateSamples
    ElapsedMs = $largeElapsedMs
    MemoryDeltaMB = $largeDeltaMB
    MemoryGateMB = $LargeCandidateMemoryGateMB
    MemoryGatePassed = $largeGatePassed
    GroupCount = $largeGrouped.Count
    SortedCount = $largeSorted.Count
  }

  if (-not $SkipScan) {
    $scanRoot = Join-Path $env:TEMP ('perf_scan_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scanRoot -Force | Out-Null

    $folders = 80
    $filesPerFolder = [math]::Max(1, [math]::Floor($ScanFiles / $folders))
    for ($f = 0; $f -lt $folders; $f++) {
      $dir = Join-Path $scanRoot ('ConsolePS1\Batch{0}' -f $f)
      New-Item -ItemType Directory -Path $dir -Force | Out-Null

      for ($i = 0; $i -lt $filesPerFolder; $i++) {
        $idx = ($f * $filesPerFolder) + $i
        if ($idx % 10 -eq 0) {
          $cue = Join-Path $dir ('Game{0} (Europe).cue' -f $idx)
          $bin = Join-Path $dir ('Game{0} (Europe).bin' -f $idx)
          'FILE "track.bin" BINARY' | Out-File -LiteralPath $cue -Encoding ascii -Force
          'x' | Out-File -LiteralPath $bin -Encoding ascii -Force
        } else {
          $chd = Join-Path $dir ('Game{0} (Europe).chd' -f $idx)
          'x' | Out-File -LiteralPath $chd -Encoding ascii -Force
        }
      }
    }

    $ext = @('.chd', '.cue', '.gdi', '.ccd', '.bin')
    $log = { param($m) }

    $sw.Restart()
    $scanResult = Invoke-RegionDedupe -Roots @($scanRoot) -Mode 'DryRun' -PreferOrder @('EU','US','WORLD','JP') `
      -IncludeExtensions $ext -RemoveJunk $true -SeparateBios $false -GenerateReportsInDryRun $false -UseDat $false -Log $log
    $sw.Stop()

    $result.Scan = [ordered]@{
      DryRunMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
      Winners = $(if ($scanResult -and $scanResult.Winners) { $scanResult.Winners.Count } else { 0 })
      FilesRequested = $ScanFiles
    }
  }

  $reportsDir = Join-Path $repoRoot 'reports'
  if (-not (Test-Path -LiteralPath $reportsDir)) {
    New-Item -ItemType Directory -Path $reportsDir -Force | Out-Null
  }

  $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  $jsonPath = Join-Path $reportsDir ("performance-snapshot-{0}.json" -f $stamp)
  $mdPath = Join-Path $reportsDir ("performance-snapshot-{0}.md" -f $stamp)
  $latestJsonPath = Join-Path $reportsDir 'performance-snapshot-latest.json'
  $latestMdPath = Join-Path $reportsDir 'performance-snapshot-latest.md'

  $result | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $jsonPath -Encoding utf8 -Force

  $lines = New-Object System.Collections.Generic.List[string]
  $lines.Add("# Performance Snapshot") | Out-Null
  $lines.Add("") | Out-Null
  $lines.Add(("Timestamp: {0}" -f $result.Timestamp)) | Out-Null
  $lines.Add(("Host: {0}" -f $result.Host)) | Out-Null
  $lines.Add(("PowerShell: {0}" -f $result.Pwsh)) | Out-Null
  $lines.Add("") | Out-Null
  $lines.Add("## GameKey") | Out-Null
  $lines.Add(("- Samples: {0}" -f $result.Samples.GameKey)) | Out-Null
  $lines.Add(("- Standard: {0} ms ({1}/s)" -f $result.GameKey.StandardMs, $result.GameKey.StandardPerSec)) | Out-Null
  $lines.Add(("- AliasMode: {0} ms ({1}/s)" -f $result.GameKey.AliasModeMs, $result.GameKey.AliasPerSec)) | Out-Null
  $lines.Add("") | Out-Null
  $lines.Add("## Console Detection") | Out-Null
  $lines.Add(("- Samples: {0}" -f $result.Samples.Console)) | Out-Null
  $lines.Add(("- Cold cache: {0} ms" -f $result.ConsoleType.ColdMs)) | Out-Null
  $lines.Add(("- Warm cache: {0} ms" -f $result.ConsoleType.WarmMs)) | Out-Null
  $lines.Add(("- Speedup: {0}x" -f $result.ConsoleType.SpeedupX)) | Out-Null

  $lines.Add("") | Out-Null
  $lines.Add("## 100k Candidate Set") | Out-Null
  $lines.Add(("- Samples: {0}" -f $result.LargeCandidateSet.Samples)) | Out-Null
  $lines.Add(("- Runtime: {0} ms" -f $result.LargeCandidateSet.ElapsedMs)) | Out-Null
  $lines.Add(("- Memory delta: {0} MB" -f $result.LargeCandidateSet.MemoryDeltaMB)) | Out-Null
  $lines.Add(("- Memory gate: <= {0} MB ({1})" -f $result.LargeCandidateSet.MemoryGateMB, $(if ($result.LargeCandidateSet.MemoryGatePassed) { 'PASS' } else { 'FAIL' }))) | Out-Null
  $lines.Add(("- Grouped keys: {0}" -f $result.LargeCandidateSet.GroupCount)) | Out-Null
  $lines.Add(("- Sorted rows: {0}" -f $result.LargeCandidateSet.SortedCount)) | Out-Null

  if ($result.Contains('Scan')) {
    $lines.Add("") | Out-Null
    $lines.Add("## DryRun Scan") | Out-Null
    $lines.Add(("- Files requested: {0}" -f $result.Scan.FilesRequested)) | Out-Null
    $lines.Add(("- Runtime: {0} ms" -f $result.Scan.DryRunMs)) | Out-Null
    $lines.Add(("- Winners: {0}" -f $result.Scan.Winners)) | Out-Null
  }

  $lines.Add("") | Out-Null
  $lines.Add(("JSON: {0}" -f $jsonPath)) | Out-Null
  $lines.Add(("MD: {0}" -f $mdPath)) | Out-Null

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
  if ($scanRoot -and (Test-Path -LiteralPath $scanRoot)) {
    Remove-Item -LiteralPath $scanRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  Remove-SimpleSortTestTempScript -TempScript $tempScript
}
