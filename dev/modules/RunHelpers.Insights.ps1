# NOTE:
# Extracted from RunHelpers.ps1 to reduce god-file size and improve maintainability.

function ConvertTo-NormalizedExtensionList {
  <# Normalize extension list to lowercase with leading dot. #>
  param([string]$Text)
  return @(ConvertFrom-CSVList $Text |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object {
      $e = ([string]$_).ToLowerInvariant()
      if (-not $e.StartsWith('.')) { $e = '.' + $e }
      $e
    } | Select-Object -Unique)
}

function Get-DuplicateInspectorRows {
  <# Build duplicate groups with winner score breakdown for UI preview. #>
  param(
    [string[]]$Roots,
    [string[]]$Extensions,
    [bool]$AliasEditionKeying = $false,
    [string[]]$PreferOrder = @('EU','US','WORLD','JP'),
    [int]$MaxGroups = 250,
    [hashtable]$ManualOverrides,
    [string[]]$ExcludedPaths = @()
  )

  $cacheKeyParts = New-Object System.Collections.Generic.List[string]
  [void]$cacheKeyParts.Add(('roots={0}' -f (@($Roots | Sort-Object) -join ';')))
  [void]$cacheKeyParts.Add(('ext={0}' -f (@($Extensions | Sort-Object) -join ';')))
  [void]$cacheKeyParts.Add(('alias={0}' -f [bool]$AliasEditionKeying))
  [void]$cacheKeyParts.Add(('prefer={0}' -f (@($PreferOrder | Sort-Object) -join ';')))
  [void]$cacheKeyParts.Add(('max={0}' -f [int]$MaxGroups))
  [void]$cacheKeyParts.Add(('excluded={0}' -f (@($ExcludedPaths | Sort-Object) -join ';')))
  if ($ManualOverrides) {
    $overridePairs = @($ManualOverrides.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0}={1}" -f $_.Name, $_.Value })
    [void]$cacheKeyParts.Add(('overrides={0}' -f ($overridePairs -join ';')))
  } else {
    [void]$cacheKeyParts.Add('overrides=')
  }
  $cacheKey = ($cacheKeyParts -join '|')

  $canUseAppStateCache = [bool](Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) -and [bool](Get-Command Set-AppStateValue -ErrorAction SilentlyContinue)
  if ($canUseAppStateCache) {
    $cachePayload = Get-AppStateValue -Key 'DuplicateInspectorRowsCache' -Default $null
    if ($cachePayload -and ($cachePayload.PSObject.Properties.Name -contains 'Key') -and ($cachePayload.PSObject.Properties.Name -contains 'Rows') -and [string]$cachePayload.Key -eq $cacheKey) {
      return @($cachePayload.Rows)
    }
  }

  if (-not $Roots -or $Roots.Count -eq 0) { return @() }
  if ($MaxGroups -lt 1) { $MaxGroups = 1 }

  $extSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($Extensions)) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }
    $ext = $entry.Trim().ToLowerInvariant()
    if (-not $ext.StartsWith('.')) { $ext = '.' + $ext }
    [void]$extSet.Add($ext)
  }

  $allItems = New-Object System.Collections.Generic.List[psobject]
  $excludedPathSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($pathEntry in @($ExcludedPaths)) {
    if ([string]::IsNullOrWhiteSpace([string]$pathEntry)) { continue }
    [void]$excludedPathSet.Add([string]$pathEntry)
  }

  foreach ($root in @($Roots | Sort-Object -Unique)) {
    if ([string]::IsNullOrWhiteSpace($root)) { continue }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }

    $excludePaths = @(
      (Join-Path $root '_TRASH_REGION_DEDUPE') + '\'
      (Join-Path $root '_BIOS') + '\'
      (Join-Path $root '_JUNK') + '\'
    )

    $files = @(Get-FilesSafe -Root $root -ExcludePrefixes $excludePaths -AllowedExtensions $extSet -Responsive | Sort-Object FullName)

    $fileInfoByPath = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($f in $files) {
      if (-not $f) { continue }
      if ($excludedPathSet.Contains([string]$f.FullName)) { continue }
      $fileInfoByPath[$f.FullName] = $f

      $baseName = [IO.Path]::GetFileNameWithoutExtension([string]$f.Name)
      if ([string]::IsNullOrWhiteSpace($baseName)) { continue }

      $region = Get-RegionTag -Name $baseName
      $console = Get-ConsoleType -RootPath $root -FilePath $f.FullName -Extension $f.Extension
      $key = ConvertTo-GameKey -BaseName $baseName -AliasEditionKeying:$AliasEditionKeying -ConsoleType $console
      $versionScore = Get-VersionScore -BaseName $baseName

      $item = New-FileItem -Root $root -MainPath $f.FullName -Category 'GAME' -Region $region `
        -PreferOrder $PreferOrder -VersionScore $versionScore -GameKey $key -DatMatch $false `
        -BaseName $baseName -FileInfoByPath $fileInfoByPath

      [void]$allItems.Add($item)
    }
  }

  if ($allItems.Count -eq 0) { return @() }

  $groupsByKey = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in $allItems) {
    $groupKey = if ([string]::IsNullOrWhiteSpace($entry.GameKey)) { '__empty__' } else { [string]$entry.GameKey }
    if (-not $groupsByKey.ContainsKey($groupKey)) {
      $groupsByKey[$groupKey] = New-Object System.Collections.Generic.List[psobject]
    }
    [void]$groupsByKey[$groupKey].Add($entry)
  }

  $rows = New-Object System.Collections.Generic.List[psobject]
  $groupIndex = 0
  foreach ($groupKey in ($groupsByKey.Keys | Sort-Object)) {
    $group = @($groupsByKey[$groupKey])
    if ($group.Count -le 1) { continue }

    $groupIndex++
    if ($groupIndex -gt $MaxGroups) { break }

    $winner = $null
    $winnerSource = 'auto'
    if ($ManualOverrides -and $ManualOverrides.ContainsKey($groupKey)) {
      $overridePath = [string]$ManualOverrides[$groupKey]
      if (-not [string]::IsNullOrWhiteSpace($overridePath)) {
        $candidate = @($group | Where-Object { [string]$_.MainPath -eq $overridePath } | Select-Object -First 1)
        if ($candidate.Count -gt 0) {
          $winner = $candidate[0]
          $winnerSource = 'manual'
        }
      }
    }
    if (-not $winner) {
      $winner = Select-Winner -Items $group
    }
    $sorted = @($group | Sort-Object `
      @{Expression={ $_.MainPath -eq $winner.MainPath }; Descending=$true},
      @{Expression='RegionScore';Descending=$true},
      @{Expression='HeaderScore';Descending=$true},
      @{Expression='VersionScore';Descending=$true},
      @{Expression='FormatScore';Descending=$true},
      @{Expression='MainPath';Descending=$false})

    foreach ($item in $sorted) {
      $isWinner = ([string]$item.MainPath -eq [string]$winner.MainPath)
      $totalScore = [long]$item.RegionScore + [long]$item.HeaderScore + [long]$item.VersionScore + [long]$item.FormatScore + $(if ($item.PSObject.Properties['CompletenessScore']) { [long]$item.CompletenessScore } else { [long]0 })
      $breakdown = ('R={0}, H={1}, V={2}, F={3}, C={4}, S={5}' -f `
        [long]$item.RegionScore,
        [long]$item.HeaderScore,
        [long]$item.VersionScore,
        [long]$item.FormatScore,
        $(if ($item.PSObject.Properties['CompletenessScore']) { [int]$item.CompletenessScore } else { 0 }),
        $(if ($item.PSObject.Properties['SizeTieBreakScore']) { [long]$item.SizeTieBreakScore } else { [long]0 }))
      [void]$rows.Add([pscustomobject]@{
        GameKey = [string]$groupKey
        Winner = [bool]$isWinner
        WinnerSource = if ($isWinner) { $winnerSource } else { 'candidate' }
        Region = [string]$item.Region
        Type = [string]$item.Type
        SizeMB = [Math]::Round(([double]$item.SizeBytes / 1MB), 2)
        RegionScore = [long]$item.RegionScore
        HeaderScore = [long]$item.HeaderScore
        VersionScore = [long]$item.VersionScore
        FormatScore = [long]$item.FormatScore
        CompletenessScore = $(if ($item.PSObject.Properties['CompletenessScore']) { [long]$item.CompletenessScore } else { [long]0 })
        SizeTieBreakScore = $(if ($item.PSObject.Properties['SizeTieBreakScore']) { [long]$item.SizeTieBreakScore } else { [long]0 })
        TotalScore = [long]$totalScore
        ScoreBreakdown = [string]$breakdown
        MainPath = [string]$item.MainPath
      })
    }
  }

  $resultRows = @($rows)
  if ($canUseAppStateCache) {
    [void](Set-AppStateValue -Key 'DuplicateInspectorRowsCache' -Value ([pscustomobject]@{
      Key = $cacheKey
      Rows = $resultRows
      CachedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }))
  }

  return $resultRows
}

function Export-DuplicateInspectorCsv {
  <# Export duplicate inspector rows to CSV with safe value encoding. #>
  param(
    [psobject[]]$Rows,
    [Parameter(Mandatory=$true)][string]$Path
  )

  $ordered = @($Rows | ForEach-Object {
    $winner = $false
    if ($_.PSObject.Properties['Winner']) { $winner = [bool]$_.Winner }

    $winnerSource = ''
    if ($_.PSObject.Properties['WinnerSource']) { $winnerSource = [string]$_.WinnerSource }

    $completeness = 0
    if ($_.PSObject.Properties['CompletenessScore']) { $completeness = [int]$_.CompletenessScore }

    $sizeTie = [long]0
    if ($_.PSObject.Properties['SizeTieBreakScore']) { $sizeTie = [long]$_.SizeTieBreakScore }

    $totalScore = 0
    if ($_.PSObject.Properties['TotalScore']) { $totalScore = [int]$_.TotalScore }

    $scoreBreakdown = ''
    if ($_.PSObject.Properties['ScoreBreakdown']) { $scoreBreakdown = [string]$_.ScoreBreakdown }

    $mainPath = ''
    if ($_.PSObject.Properties['MainPath']) { $mainPath = [string]$_.MainPath }

    [pscustomobject]@{
      GameKey = ConvertTo-SafeCsvValue ([string]$_.GameKey)
      Winner = $(if ($winner) { 'YES' } else { 'NO' })
      WinnerSource = ConvertTo-SafeCsvValue $winnerSource
      Region = ConvertTo-SafeCsvValue ([string]$_.Region)
      Type = ConvertTo-SafeCsvValue ([string]$_.Type)
      SizeMB = [double]$_.SizeMB
      RegionScore = [long]$_.RegionScore
      HeaderScore = [long]$_.HeaderScore
      VersionScore = [long]$_.VersionScore
      FormatScore = [long]$_.FormatScore
      CompletenessScore = $completeness
      SizeTieBreakScore = $sizeTie
      TotalScore = $totalScore
      ScoreBreakdown = ConvertTo-SafeCsvValue $scoreBreakdown
      MainPath = ConvertTo-SafeCsvValue $mainPath
    }
  })

  $ordered | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $Path
}

function Get-CollectionHealthRows {
  <# Build per-console health dashboard rows from latest run result. #>
  param(
    [pscustomobject]$Result,
    [string]$FilterText = ''
  )

  if (-not $Result) { return @() }

  $reportRows = @()
  if ($Result.PSObject.Properties.Name -contains 'ReportRows' -and $Result.ReportRows) {
    $reportRows = @($Result.ReportRows)
  }
  if ($reportRows.Count -eq 0 -and $Result.PSObject.Properties.Name -contains 'CsvPath' -and -not [string]::IsNullOrWhiteSpace([string]$Result.CsvPath) -and (Test-Path -LiteralPath $Result.CsvPath -PathType Leaf)) {
    try {
      $reportRows = @(Import-Csv -LiteralPath $Result.CsvPath | ForEach-Object {
        [pscustomobject]@{
          Category = [string]$_.Category
          Action = [string]$_.Action
          Type = ''
          Root = ''
          MainPath = [string]$_.FullPath
          IsCorrupt = $false
        }
      })
    } catch {
      $reportRows = @()
    }
  }
  if ($reportRows.Count -eq 0) { return @() }

  $missingByConsole = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  if ($Result.PSObject.Properties.Name -contains 'DatCompletenessCsvPath' -and -not [string]::IsNullOrWhiteSpace([string]$Result.DatCompletenessCsvPath) -and (Test-Path -LiteralPath $Result.DatCompletenessCsvPath -PathType Leaf)) {
    try {
      foreach ($row in @(Import-Csv -LiteralPath $Result.DatCompletenessCsvPath)) {
        $key = [string]$row.Console
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        $missingByConsole[$key] = [int]$row.Missing
      }
    } catch { }
  }

  $statsByConsole = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

  foreach ($row in $reportRows) {
    $path = if ($row.PSObject.Properties.Name -contains 'MainPath') { [string]$row.MainPath } else { '' }
    if ([string]::IsNullOrWhiteSpace($path)) { continue }

    $root = if ($row.PSObject.Properties.Name -contains 'Root') { [string]$row.Root } else { '' }
    $ext = [IO.Path]::GetExtension($path)
    $console = Get-ConsoleType -RootPath $root -FilePath $path -Extension $ext
    if ([string]::IsNullOrWhiteSpace($console)) { $console = 'UNKNOWN' }

    if (-not $statsByConsole.ContainsKey($console)) {
      $statsByConsole[$console] = [pscustomobject]@{
        Console = [string]$console
        Roms = 0
        Duplicates = 0
        MissingDat = 0
        Corrupt = 0
        _FormatCounts = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      }
    }

    $stat = $statsByConsole[$console]
    $category = if ($row.PSObject.Properties.Name -contains 'Category') { [string]$row.Category } else { '' }
    $action = if ($row.PSObject.Properties.Name -contains 'Action') { [string]$row.Action } else { '' }
    $type = if ($row.PSObject.Properties.Name -contains 'Type') { [string]$row.Type } else { '' }
    if ([string]::IsNullOrWhiteSpace($type)) { $type = 'FILE' }

    if ($category -eq 'GAME' -and $action -eq 'KEEP') {
      $stat.Roms++
      if (-not $stat._FormatCounts.ContainsKey($type)) { $stat._FormatCounts[$type] = 0 }
      $stat._FormatCounts[$type] = [int]$stat._FormatCounts[$type] + 1
    } elseif ($category -eq 'GAME') {
      $stat.Duplicates++
    }

    $isCorrupt = $false
    if ($row.PSObject.Properties.Name -contains 'IsCorrupt') {
      try { $isCorrupt = [bool]$row.IsCorrupt } catch { $isCorrupt = $false }
    }
    if ($isCorrupt) { $stat.Corrupt++ }
  }

  foreach ($consoleKey in $missingByConsole.Keys) {
    if (-not $statsByConsole.ContainsKey($consoleKey)) {
      $statsByConsole[$consoleKey] = [pscustomobject]@{
        Console = [string]$consoleKey
        Roms = 0
        Duplicates = 0
        MissingDat = 0
        Corrupt = 0
        _FormatCounts = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      }
    }
    $statsByConsole[$consoleKey].MissingDat = [int]$missingByConsole[$consoleKey]
  }

  $rows = @($statsByConsole.Values | ForEach-Object {
    $formatSummary = '-'
    if ($_._FormatCounts -and $_._FormatCounts.Count -gt 0) {
      $parts = @($_._FormatCounts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { '{0}:{1}' -f $_.Key, $_.Value })
      if ($parts.Count -gt 3) { $parts = @($parts[0..2]) }
      if ($parts.Count -gt 0) { $formatSummary = ($parts -join ', ') }
    }
    [pscustomobject]@{
      Console = [string]$_.Console
      Roms = [int]$_.Roms
      Duplicates = [int]$_.Duplicates
      MissingDat = [int]$_.MissingDat
      Corrupt = [int]$_.Corrupt
      Formats = [string]$formatSummary
    }
  } | Sort-Object Console)

  if (-not [string]::IsNullOrWhiteSpace($FilterText)) {
    $needle = $FilterText.Trim().ToLowerInvariant()
    $rows = @($rows | Where-Object {
      ([string]$_.Console).ToLowerInvariant().Contains($needle) -or
      ([string]$_.Formats).ToLowerInvariant().Contains($needle)
    })
  }

  return @($rows)
}

function Get-IncrementalDryRunDelta {
  <# Compare current dry-run duplicate move set with previous snapshot. #>
  param(
    [pscustomobject]$Result,
    [string]$SnapshotPath = $null,
    [int]$MaxSamples = 12
  )

  if (-not $SnapshotPath) {
    $reportsDir = Join-Path (Get-Location).Path 'reports'
    Assert-DirectoryExists -Path $reportsDir
    $SnapshotPath = Join-Path $reportsDir 'dryrun-delta-latest.json'
  }
  if ($MaxSamples -lt 1) { $MaxSamples = 1 }

  $currentRows = @()
  if ($Result -and $Result.PSObject.Properties.Name -contains 'ReportRows' -and $Result.ReportRows) {
    $currentRows = @($Result.ReportRows | Where-Object { $_.Category -eq 'GAME' -and $_.Action -eq 'MOVE' })
  }

  $currentSet = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($row in $currentRows) {
    $gameKey = if ($row.PSObject.Properties.Name -contains 'GameKey') { [string]$row.GameKey } else { '' }
    $mainPath = if ($row.PSObject.Properties.Name -contains 'MainPath') { [string]$row.MainPath } else { '' }
    if ([string]::IsNullOrWhiteSpace($gameKey) -or [string]::IsNullOrWhiteSpace($mainPath)) { continue }
    $entryKey = ('{0}|{1}' -f $gameKey.Trim(), $mainPath.Trim())
    $currentSet[$entryKey] = $true
  }

  $previousSet = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $previousTimestamp = $null
  if (Test-Path -LiteralPath $SnapshotPath -PathType Leaf) {
    try {
      $payload = Get-Content -LiteralPath $SnapshotPath -Raw | ConvertFrom-Json
      if ($payload -and $payload.entries) {
        foreach ($entry in @($payload.entries)) {
          $value = [string]$entry
          if (-not [string]::IsNullOrWhiteSpace($value)) { $previousSet[$value] = $true }
        }
      }
      if ($payload -and $payload.timestampUtc) {
        $previousTimestamp = [string]$payload.timestampUtc
      }
    } catch { }
  }

  $added = New-Object System.Collections.Generic.List[string]
  foreach ($entry in @($currentSet.Keys)) {
    if (-not $previousSet.ContainsKey([string]$entry)) { [void]$added.Add([string]$entry) }
  }

  $resolved = New-Object System.Collections.Generic.List[string]
  foreach ($entry in @($previousSet.Keys)) {
    if (-not $currentSet.ContainsKey([string]$entry)) { [void]$resolved.Add([string]$entry) }
  }

  $snapshotPayload = [pscustomobject]@{
    schemaVersion = 'dryrun-delta-v1'
    timestampUtc = (Get-Date).ToUniversalTime().ToString('o')
    entries = @($currentSet.Keys)
  }
  Write-JsonFile -Path $SnapshotPath -Data $snapshotPayload -Depth 6

  $toSampleObjects = {
    param([string[]]$Entries)
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($entry in @($Entries | Select-Object -First $MaxSamples)) {
      $parts = [string]$entry -split '\|', 2
      [void]$rows.Add([pscustomobject]@{
        GameKey = if ($parts.Count -gt 0) { [string]$parts[0] } else { '' }
        MainPath = if ($parts.Count -gt 1) { [string]$parts[1] } else { '' }
      })
    }
    return $rows.ToArray()
  }

  return [pscustomobject]@{
    SnapshotPath = $SnapshotPath
    PreviousTimestampUtc = $previousTimestamp
    CurrentCount = $currentSet.Count
    PreviousCount = $previousSet.Count
    AddedCount = $added.Count
    ResolvedCount = $resolved.Count
    AddedSamples = & $toSampleObjects $added.ToArray()
    ResolvedSamples = & $toSampleObjects $resolved.ToArray()
  }
}

function Invoke-OperationPlugins {
  <# Execute custom operation plugins from plugins/operations/*.ps1. #>
  param(
    [string]$Phase = 'post-run',
    [hashtable]$Context,
    [ValidateSet('compat','trusted-only','signed-only')]
    [string]$TrustMode,
    [int]$PluginTimeoutMs = 15000,
    [int]$PluginMaxResultBytes = 1048576,
    [scriptblock]$Log
  )

  if ($PluginTimeoutMs -lt 1000) { $PluginTimeoutMs = 1000 }
  if ($PluginMaxResultBytes -lt 4096) { $PluginMaxResultBytes = 4096 }

  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      if ($PSBoundParameters.Keys -notcontains 'PluginTimeoutMs') {
        $fromStateTimeout = [int](Get-AppStateValue -Key 'PluginExecutionTimeoutMs' -Default $PluginTimeoutMs)
        if ($fromStateTimeout -gt 0) { $PluginTimeoutMs = $fromStateTimeout }
      }
    } catch {}
    try {
      if ($PSBoundParameters.Keys -notcontains 'PluginMaxResultBytes') {
        $fromStateQuota = [int](Get-AppStateValue -Key 'PluginMaxResultBytes' -Default $PluginMaxResultBytes)
        if ($fromStateQuota -gt 0) { $PluginMaxResultBytes = $fromStateQuota }
      }
    } catch {}
  }

  $normalizePluginResult = {
    param([object]$PluginResult)
    if ($null -eq $PluginResult) {
      return [pscustomobject]@{ PluginHandled = $false; Payload = $null }
    }

    $hasHandled = ($PluginResult.PSObject.Properties.Name -contains 'PluginHandled')
    $handled = if ($hasHandled) { [bool]$PluginResult.PluginHandled } else { $false }
    return [pscustomobject]@{
      PluginHandled = $handled
      Payload = $PluginResult
    }
  }

  $invokeIsolatedPlugin = {
    param(
      [Parameter(Mandatory=$true)][System.IO.FileInfo]$PluginFile,
      [Parameter(Mandatory=$true)][string]$Phase,
      [hashtable]$Context,
      [int]$TimeoutMs,
      [int]$MaxResultBytes
    )

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('romcleanup-plugin-' + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $tempRoot -Force)
    $contextPath = Join-Path $tempRoot 'context.json'
    $resultPath = Join-Path $tempRoot 'result.json'
    $runnerPath = Join-Path $tempRoot 'runner.ps1'

    Write-JsonFile -Path $contextPath -Data $Context -Depth 12

    @'
param(
  [Parameter(Mandatory=$true)][string]$PluginPath,
  [Parameter(Mandatory=$true)][string]$Phase,
  [Parameter(Mandatory=$true)][string]$ContextPath,
  [Parameter(Mandatory=$true)][string]$ResultPath
)

$payload = [ordered]@{ Status = 'ERROR'; Error = 'Unknown plugin runner error.'; Result = $null }
try {
      $context = @{}
      try {
        $rawContext = Get-Content -LiteralPath $ContextPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($rawContext -is [System.Collections.IDictionary]) {
          foreach ($k in $rawContext.Keys) { $context[[string]$k] = $rawContext[$k] }
        } elseif ($rawContext -and $rawContext.PSObject -and $rawContext.PSObject.Properties.Count -gt 0) {
          foreach ($p in $rawContext.PSObject.Properties) { $context[[string]$p.Name] = $p.Value }
        }
      } catch {
        $context = @{}
      }

  $result = $null
  $executed = $false

  try {
    $result = & $PluginPath -Phase $Phase -Context $context
    $executed = $true
  } catch {
    try {
      $result = & $PluginPath -Context $context
      $executed = $true
    } catch {
      try {
        $result = & $PluginPath
        $executed = $true
      } catch {
      }
    }
  }

  if (-not $executed) {
    Remove-Item Function:\Invoke-RomCleanupOperationPlugin -ErrorAction SilentlyContinue
    . $PluginPath
    $handler = Get-Command Invoke-RomCleanupOperationPlugin -CommandType Function -ErrorAction SilentlyContinue
    if ($handler) {
      $result = Invoke-RomCleanupOperationPlugin -Phase $Phase -Context $context
    }
  }

  $payload.Status = 'OK'
  $payload.Error = $null
  $payload.Result = $result
} catch {
  $payload.Status = 'ERROR'
  $payload.Error = ('{0} | {1}' -f [string]$_.Exception.Message, [string]$_.InvocationInfo.PositionMessage)
  $payload.Result = $null
} finally {
  Remove-Item Function:\Invoke-RomCleanupOperationPlugin -ErrorAction SilentlyContinue
}

$payload | ConvertTo-Json -Depth 12 | Out-File -LiteralPath $ResultPath -Encoding utf8 -Force
'@ | Out-File -LiteralPath $runnerPath -Encoding utf8 -Force

    $pwsh = 'pwsh'
    try {
      $pwshCmd = Get-Command pwsh -ErrorAction SilentlyContinue
      if ($pwshCmd) { $pwsh = [string]$pwshCmd.Source }
    } catch {}

    $resultObject = $null
    $proc = $null
    try {
      $proc = Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile','-File',$runnerPath,'-PluginPath',$PluginFile.FullName,'-Phase',$Phase,'-ContextPath',$contextPath,'-ResultPath',$resultPath) -PassThru -WindowStyle Hidden
      $finished = $proc.WaitForExit($TimeoutMs)
      if (-not $finished) {
        try {
          if (Get-Command Stop-ExternalProcessTree -ErrorAction SilentlyContinue) {
            Stop-ExternalProcessTree -Process $proc
          } else {
            try { $proc.Kill($true) } catch { $proc.Kill() }
            try { $proc.WaitForExit(3000) } catch {}
          }
        } catch {}
        $resultObject = [pscustomobject]@{ Status = 'ERROR'; Error = ('Plugin timeout after {0} ms' -f $TimeoutMs); Result = $null; Isolated = $true }
      } elseif (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        $resultObject = [pscustomobject]@{ Status = 'ERROR'; Error = 'Plugin did not produce result contract.'; Result = $null; Isolated = $true }
      } else {
        $resultInfo = Get-Item -LiteralPath $resultPath -ErrorAction Stop
        if ([long]$resultInfo.Length -gt [long]$MaxResultBytes) {
          $resultObject = [pscustomobject]@{ Status = 'ERROR'; Error = ('Plugin result exceeds quota ({0} bytes > {1} bytes).' -f [long]$resultInfo.Length, [long]$MaxResultBytes); Result = $null; Isolated = $true }
        } else {
          $payloadRaw = Get-Content -LiteralPath $resultPath -Raw -ErrorAction Stop
          $payload = $payloadRaw | ConvertFrom-Json -ErrorAction Stop
          $resultObject = [pscustomobject]@{ Status = [string]$payload.Status; Error = [string]$payload.Error; Result = $payload.Result; Isolated = $true }
        }
      }
    } finally {
      if ($proc) {
        try {
          if (-not $proc.HasExited) {
            if (Get-Command Stop-ExternalProcessTree -ErrorAction SilentlyContinue) {
              Stop-ExternalProcessTree -Process $proc
            } else {
              try { $proc.Kill($true) } catch { try { $proc.Kill() } catch {} }
              try { $proc.WaitForExit(3000) } catch {}
            }
          }
        } catch {}
        try { $proc.Dispose() } catch {}
      }
      try { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }

    return $resultObject
  }

  if ([string]::IsNullOrWhiteSpace($TrustMode)) {
    $fromEnv = [string]$env:ROMCLEANUP_PLUGIN_TRUST_MODE
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
      $TrustMode = $fromEnv
    } elseif (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      try {
        $fromState = [string](Get-AppStateValue -Key 'PluginTrustMode' -Default 'trusted-only')
        if (-not [string]::IsNullOrWhiteSpace($fromState)) { $TrustMode = $fromState }
      } catch {
        $TrustMode = 'trusted-only'
      }
    } else {
      $TrustMode = 'trusted-only'
    }
  }

  if ([string]::IsNullOrWhiteSpace($TrustMode)) { $TrustMode = 'trusted-only' }
  switch ($TrustMode.ToLowerInvariant()) {
    'compat' { $TrustMode = 'compat' }
    'trusted-only' { $TrustMode = 'trusted-only' }
    'signed-only' { $TrustMode = 'signed-only' }
    default {
      if ($Log) { & $Log ('Operation-Plugin WARNUNG: Ungültiger TrustMode "{0}", fallback auf trusted-only.' -f $TrustMode) }
      $TrustMode = 'trusted-only'
    }
  }

  $testSemVer = {
    param([string]$Version)
    if ([string]::IsNullOrWhiteSpace($Version)) { return $false }
    return ([regex]::IsMatch([string]$Version, '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z\-.]+)?(?:\+[0-9A-Za-z\-.]+)?$'))
  }

  $getManifest = {
    param([System.IO.FileInfo]$PluginFile)
    $manifest = [ordered]@{
      Id = [System.IO.Path]::GetFileNameWithoutExtension([string]$PluginFile.Name)
      Version = '0.0.0'
      Dependencies = @()
      Trusted = $false
      RequireValidSignature = $false
      IsolateWhenUntrusted = $true
    }

    $manifestPath = [System.IO.Path]::ChangeExtension([string]$PluginFile.FullName, '.manifest.json')
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
      return [pscustomobject]$manifest
    }

    try {
      $raw = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop
      if ([string]::IsNullOrWhiteSpace($raw)) { return [pscustomobject]$manifest }
      $data = $raw | ConvertFrom-Json -ErrorAction Stop
      if ($data.id -and -not [string]::IsNullOrWhiteSpace([string]$data.id)) { $manifest.Id = [string]$data.id }
      if ($data.version -and -not [string]::IsNullOrWhiteSpace([string]$data.version)) { $manifest.Version = [string]$data.version }
      if ($data.dependencies) { $manifest.Dependencies = @($data.dependencies | ForEach-Object { [string]$_ }) }
      if ($data.PSObject.Properties.Name -contains 'trusted') {
        $manifest.Trusted = [bool]$data.trusted
      }
      if ($data.PSObject.Properties.Name -contains 'requireValidSignature') {
        $manifest.RequireValidSignature = [bool]$data.requireValidSignature
      }
      if ($data.PSObject.Properties.Name -contains 'isolateWhenUntrusted') {
        $manifest.IsolateWhenUntrusted = [bool]$data.isolateWhenUntrusted
      }
    } catch {
      if ($Log) { & $Log ('Operation-Plugin WARNUNG: Manifest ungültig für {0}: {1}' -f $PluginFile.Name, $_.Exception.Message) }
    }

    return [pscustomobject]$manifest
  }

  $buildExecutionPlan = {
    param([object[]]$Plugins)

    $byId = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($plugin in @($Plugins)) {
      $id = [string]$plugin.Manifest.Id
      if ([string]::IsNullOrWhiteSpace($id)) { continue }
      $byId[$id] = $plugin
    }

    $ordered = New-Object System.Collections.Generic.List[object]
    $pending = New-Object System.Collections.Generic.List[object]
    foreach ($plugin in @($Plugins)) { [void]$pending.Add($plugin) }

    $guard = 0
    while ($pending.Count -gt 0 -and $guard -lt 2000) {
      $guard++
      $moved = $false
      for ($i = $pending.Count - 1; $i -ge 0; $i--) {
        $candidate = $pending[$i]
        $deps = @($candidate.Manifest.Dependencies)
        $resolved = $true
        foreach ($dep in $deps) {
          if ([string]::IsNullOrWhiteSpace([string]$dep)) { continue }
          if (-not $byId.ContainsKey([string]$dep)) { $resolved = $false; break }
          $already = @($ordered | Where-Object { [string]$_.Manifest.Id -eq [string]$dep })
          if ($already.Count -eq 0) { $resolved = $false; break }
        }
        if ($resolved) {
          [void]$ordered.Add($candidate)
          $pending.RemoveAt($i)
          $moved = $true
        }
      }
      if (-not $moved) { break }
    }

    # append unresolved deterministically
    foreach ($left in @($pending | Sort-Object { [string]$_.Manifest.Id })) {
      [void]$ordered.Add($left)
    }

    return $ordered.ToArray()
  }

  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  $pluginsRoot = Join-Path $repoRoot 'plugins\operations'
  if (-not (Test-Path -LiteralPath $pluginsRoot -PathType Container)) { return @() }

  $results = New-Object System.Collections.ArrayList
  $files = @(Get-ChildItem -LiteralPath $pluginsRoot -Filter '*.ps1' -File -ErrorAction SilentlyContinue | Sort-Object Name)
  $descriptors = New-Object System.Collections.Generic.List[object]
  foreach ($file in $files) {
    if ([string]$file.Name -like '*.disabled') { continue }
    $manifest = & $getManifest $file
    if (-not (& $testSemVer ([string]$manifest.Version))) {
      [void]$results.Add(([pscustomobject]@{ Plugin = $file.Name; Status = 'ERROR'; Error = ('Invalid semver: {0}' -f [string]$manifest.Version) }))
      continue
    }
    [void]$descriptors.Add([pscustomobject]@{
      File = $file
      Manifest = $manifest
    })
  }

  $executionPlan = @(& $buildExecutionPlan $descriptors)
  $availableIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($descriptor in $executionPlan) {
    [void]$availableIds.Add([string]$descriptor.Manifest.Id)
  }

  foreach ($descriptor in $executionPlan) {
    $file = $descriptor.File
    $manifest = $descriptor.Manifest

    $signatureStatus = $null
    try {
      $signatureStatus = [string](Get-AuthenticodeSignature -FilePath $file.FullName).Status
    } catch {
      $signatureStatus = 'UnknownError'
    }

    if ($TrustMode -eq 'trusted-only' -and -not [bool]$manifest.Trusted) {
      [void]$results.Add(([pscustomobject]@{
        Plugin = $file.Name
        PluginId = [string]$manifest.Id
        Version = [string]$manifest.Version
        Status = 'SKIPPED'
        Error = 'Trust policy rejected plugin (trusted-only requires manifest.trusted=true).'
      }))
      if (Get-Command Write-SecurityEvent -ErrorAction SilentlyContinue) {
        [void](Write-SecurityEvent -EventType 'Plugin.Rejected' -Target $file.Name -Outcome 'Deny' -Detail 'Not trusted (manifest.trusted=false)' -Source 'Invoke-OperationPlugins' -Severity 'Medium')
      }
      continue
    }

    $requiresSignature = ($TrustMode -eq 'signed-only') -or [bool]$manifest.RequireValidSignature
    if ($requiresSignature -and $signatureStatus -ne 'Valid') {
      [void]$results.Add(([pscustomobject]@{
        Plugin = $file.Name
        PluginId = [string]$manifest.Id
        Version = [string]$manifest.Version
        Status = 'SKIPPED'
        Error = ('Trust policy rejected plugin signature: {0}' -f $signatureStatus)
      }))
      if (Get-Command Write-SecurityEvent -ErrorAction SilentlyContinue) {
        [void](Write-SecurityEvent -EventType 'Plugin.Rejected' -Target $file.Name -Outcome 'Deny' -Detail ('Invalid signature: {0}' -f $signatureStatus) -Source 'Invoke-OperationPlugins' -Severity 'High')
      }
      continue
    }

    $missingDeps = @()
    foreach ($dep in @($manifest.Dependencies)) {
      if ([string]::IsNullOrWhiteSpace([string]$dep)) { continue }
      if (-not $availableIds.Contains([string]$dep)) { $missingDeps += [string]$dep }
    }
    if ($missingDeps.Count -gt 0) {
      [void]$results.Add(([pscustomobject]@{
        Plugin = $file.Name
        Status = 'ERROR'
        Error = ('Missing dependencies: {0}' -f ($missingDeps -join ', '))
      }))
      continue
    }

    try {
      $useIsolatedExecution = ($TrustMode -eq 'compat' -and -not [bool]$manifest.Trusted -and [bool]$manifest.IsolateWhenUntrusted)
      $pluginResult = $null
      $executionMode = 'in-process'
      if ($useIsolatedExecution) {
        $executionMode = 'isolated'
        $isolated = & $invokeIsolatedPlugin -PluginFile $file -Phase $Phase -Context $Context -TimeoutMs $PluginTimeoutMs -MaxResultBytes $PluginMaxResultBytes
        if ([string]$isolated.Status -ne 'OK') {
          [void]$results.Add(([pscustomobject]@{ Plugin = $file.Name; PluginId = [string]$manifest.Id; Version = [string]$manifest.Version; Status = 'ERROR'; Error = [string]$isolated.Error; ExecutionMode = $executionMode }))
          if (Get-Command Write-SecurityAuditEvent -ErrorAction SilentlyContinue) {
            [void](Write-SecurityAuditEvent -Domain 'Plugin' -Action 'Rejected' -Actor ([string]$manifest.Id) -Target $file.Name -Outcome 'Error' -Detail ([string]$isolated.Error) -Source 'Invoke-OperationPlugins' -Severity 'High')
          }
          continue
        }
        $pluginResult = $isolated.Result
      } else {
        Remove-Item Function:\Invoke-RomCleanupOperationPlugin -ErrorAction SilentlyContinue
        . $file.FullName
        $handler = Get-Command Invoke-RomCleanupOperationPlugin -CommandType Function -ErrorAction SilentlyContinue
        if (-not $handler) { continue }
        $pluginResult = Invoke-RomCleanupOperationPlugin -Phase $Phase -Context $Context
      }

      $normalizedResult = & $normalizePluginResult $pluginResult
      [void]$results.Add(([pscustomobject]@{ Plugin = $file.Name; PluginId = [string]$manifest.Id; Version = [string]$manifest.Version; Status = 'OK'; Result = $normalizedResult.Payload; PluginHandled = [bool]$normalizedResult.PluginHandled; ExecutionMode = $executionMode }))
      if ($Log) { & $Log ('Operation-Plugin OK: {0} ({1}@{2}, mode={3})' -f $file.Name, [string]$manifest.Id, [string]$manifest.Version, $executionMode) }
      if (Get-Command Write-SecurityEvent -ErrorAction SilentlyContinue) {
        [void](Write-SecurityEvent -EventType 'Plugin.Loaded' -Target $file.Name -Outcome 'Allow' -Detail ('{0}@{1}' -f [string]$manifest.Id, [string]$manifest.Version) -Source 'Invoke-OperationPlugins' -Severity 'Low')
      }
    } catch {
      [void]$results.Add(([pscustomobject]@{ Plugin = $file.Name; PluginId = [string]$manifest.Id; Version = [string]$manifest.Version; Status = 'ERROR'; Error = [string]$_.Exception.Message }))
      if ($Log) { & $Log ('Operation-Plugin FEHLER: {0} -> {1}' -f $file.Name, $_.Exception.Message) }
    } finally {
      Remove-Item Function:\Invoke-RomCleanupOperationPlugin -ErrorAction SilentlyContinue
    }
  }

  return @($results)
}

function Get-DatCoverageHeatmapRows {
  <# Build DAT coverage heatmap rows from DAT completeness CSV. #>
  param(
    [pscustomobject]$Result,
    [int]$Top = 16
  )

  if ($Top -lt 1) { $Top = 1 }
  if (-not $Result) { return @() }
  if (-not ($Result.PSObject.Properties.Name -contains 'DatCompletenessCsvPath')) { return @() }

  $csvPath = [string]$Result.DatCompletenessCsvPath
  if ([string]::IsNullOrWhiteSpace($csvPath) -or -not (Test-Path -LiteralPath $csvPath -PathType Leaf)) {
    return @()
  }

  $rows = New-Object System.Collections.Generic.List[object]
  foreach ($entry in @(Import-Csv -LiteralPath $csvPath)) {
    $console = [string]$entry.Console
    if ([string]::IsNullOrWhiteSpace($console)) { continue }
    $expected = [int]$entry.Expected
    $missing = [int]$entry.Missing
    $matched = [int]$entry.Matched
    $coverage = if ($expected -gt 0) { [math]::Round(($matched * 100.0) / $expected, 1) } else { 0 }
    $barFilled = [int][math]::Round(($coverage / 100.0) * 10)
    if ($barFilled -lt 0) { $barFilled = 0 }
    if ($barFilled -gt 10) { $barFilled = 10 }
    $bar = ('#' * $barFilled) + ('.' * (10 - $barFilled))

    [void]$rows.Add([pscustomobject]@{
      Console = $console
      Matched = $matched
      Expected = $expected
      Missing = $missing
      Coverage = [double]$coverage
      Heat = $bar
    })
  }

  return @($rows | Sort-Object @{Expression='Coverage';Descending=$true}, Console | Select-Object -First $Top)
}

function Get-CrossCollectionDedupHints {
  <# Build duplicate hints where the same GameKey appears across multiple roots. #>
  param(
    [string[]]$Roots,
    [string[]]$Extensions,
    [bool]$AliasEditionKeying = $false,
    [hashtable]$ManualOverrides,
    [string[]]$ExcludedPaths = @(),
    [int]$Top = 40
  )

  if ($Top -lt 1) { $Top = 1 }
  if (-not $Roots -or @($Roots).Count -eq 0) { return @() }

  $normalizeWithSlash = {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    try {
      $full = [System.IO.Path]::GetFullPath($Path)
      if (-not $full.EndsWith('\')) { $full += '\' }
      return $full
    } catch {
      return $null
    }
  }

  $normalizedRoots = New-Object System.Collections.Generic.List[psobject]
  foreach ($root in @($Roots)) {
    $full = & $normalizeWithSlash ([string]$root)
    if ([string]::IsNullOrWhiteSpace($full)) { continue }
    [void]$normalizedRoots.Add([pscustomobject]@{
      Root = [string]$root
      Full = $full
    })
  }

  if ($normalizedRoots.Count -eq 0) { return @() }

  $rows = @(Get-DuplicateInspectorRows -Roots $Roots -Extensions $Extensions -AliasEditionKeying:$AliasEditionKeying -ManualOverrides $ManualOverrides -ExcludedPaths $ExcludedPaths)
  if ($rows.Count -eq 0) { return @() }

  $result = New-Object System.Collections.Generic.List[psobject]
  $byKey = @($rows | Group-Object -Property GameKey)
  foreach ($group in $byKey) {
    $entries = @($group.Group)
    if ($entries.Count -le 1) { continue }

    $rootsByHit = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
      $mainPath = [string]$entry.MainPath
      if ([string]::IsNullOrWhiteSpace($mainPath)) { continue }
      foreach ($rootInfo in $normalizedRoots) {
        if ($mainPath.StartsWith([string]$rootInfo.Full, [StringComparison]::OrdinalIgnoreCase)) {
          [void]$rootsByHit.Add([string]$rootInfo.Root)
          break
        }
      }
    }

    if ($rootsByHit.Count -le 1) { continue }
    $winnerPath = [string](@($entries | Where-Object { [bool]$_.Winner } | Select-Object -First 1 -ExpandProperty MainPath))
    $rootsSorted = @($rootsByHit | Sort-Object)

    [void]$result.Add([pscustomobject]@{
      GameKey = [string]$group.Name
      RootCount = [int]$rootsByHit.Count
      CandidateCount = [int]$entries.Count
      WinnerPath = $winnerPath
      Roots = @($rootsSorted)
      RootsSummary = ($rootsSorted -join ' | ')
    })
  }

  return @($result | Sort-Object @{Expression='RootCount';Descending=$true}, @{Expression='CandidateCount';Descending=$true}, GameKey | Select-Object -First $Top)
}

function Invoke-ToolchainDoctor {
  <# Build an actionable diagnosis from tool self-test + active run settings. #>
  param(
    [hashtable]$ToolOverrides,
    [bool]$UseDat = $false,
    [bool]$ConvertEnabled = $false,
    [scriptblock]$Log
  )

  $selfTest = Invoke-ToolSelfTest -ToolOverrides $ToolOverrides -Log $null
  $recommendations = New-Object System.Collections.Generic.List[string]
  $score = 100

  $byName = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($selfTest.Results)) {
    $byName[[string]$entry.Name] = $entry
  }

  foreach ($entry in @($selfTest.Results)) {
    if (-not [bool]$entry.Found) {
      $score -= 12
    } elseif (-not [bool]$entry.Healthy) {
      $score -= 6
    }
  }

  if ($UseDat) {
    $sevenZip = $byName['7z']
    if (-not $sevenZip -or -not [bool]$sevenZip.Found) {
      [void]$recommendations.Add('DAT aktiv: 7z.exe fehlt, .7z-Hashing wird reduziert.')
      $score -= 10
    }
  }

  if ($ConvertEnabled) {
    $chdman = $byName['chdman']
    $dolphin = $byName['dolphintool']
    $sevenZip = $byName['7z']
    $hasCoreConvertTool = (($chdman -and [bool]$chdman.Found) -or ($dolphin -and [bool]$dolphin.Found) -or ($sevenZip -and [bool]$sevenZip.Found))
    if (-not $hasCoreConvertTool) {
      [void]$recommendations.Add('Konvertierung aktiv: kein Konvertierungs-Tool gefunden (chdman/dolphintool/7z).')
      $score -= 18
    }
  }

  $missingNames = @($selfTest.Results | Where-Object { -not $_.Found } | ForEach-Object { [string]$_.Label })
  if ($missingNames.Count -gt 0) {
    [void]$recommendations.Add(('Fehlende Tools installieren oder Pfade setzen: {0}' -f ($missingNames -join ', ')))
  }

  $warnNames = @($selfTest.Results | Where-Object { $_.Found -and -not $_.Healthy } | ForEach-Object { [string]$_.Label })
  if ($warnNames.Count -gt 0) {
    [void]$recommendations.Add(('Tool-Probe pruefen (Version/CLI): {0}' -f ($warnNames -join ', ')))
  }

  if ($recommendations.Count -eq 0) {
    [void]$recommendations.Add('Toolchain ist startbereit. Optional: Versionsstaende in Profil dokumentieren.')
  }

  if ($score -lt 0) { $score = 0 }
  if ($score -gt 100) { $score = 100 }

  $status = 'Ready'
  if ($score -lt 50) {
    $status = 'ActionRequired'
  } elseif ($score -lt 80) {
    $status = 'ReviewRecommended'
  }

  if ($Log) {
    & $Log ('Toolchain Doctor: Score={0}/100, Status={1}' -f $score, $status)
    foreach ($rec in @($recommendations)) {
      & $Log ('  Empfehlung: {0}' -f $rec)
    }
  }

  return [pscustomobject]@{
    Score = [int]$score
    Status = [string]$status
    Recommendations = @($recommendations)
    SelfTest = $selfTest
  }
}

function Invoke-RuleImpactSimulation {
  <# Estimate winner changes when applying candidate region rules without persisting them. #>
  param(
    [string[]]$Roots,
    [string[]]$Extensions,
    [bool]$AliasEditionKeying = $false,
    [hashtable]$ManualWinnerOverrides,
    [string]$OrderedText,
    [string]$TwoLetterText,
    [string]$LangPattern,
    [int]$MaxSampleChanges = 20,
    [scriptblock]$Log
  )

  if (-not $Roots -or $Roots.Count -eq 0) {
    return [pscustomobject]@{ Success = $false; Error = 'Keine Roots gesetzt.'; Errors = @('Keine Roots gesetzt.') }
  }
  if ($MaxSampleChanges -lt 1) { $MaxSampleChanges = 1 }

  if (-not $ManualWinnerOverrides) {
    $ManualWinnerOverrides = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }

  $beforeRows = @(Get-DuplicateInspectorRows -Roots $Roots -Extensions $Extensions -AliasEditionKeying:$AliasEditionKeying -ManualOverrides $ManualWinnerOverrides)
  $beforeWinnerByKey = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($row in @($beforeRows | Where-Object { $_.Winner })) {
    $beforeWinnerByKey[[string]$row.GameKey] = [string]$row.MainPath
  }

  $oldOrdered = $script:RX_REGION_ORDERED_OVERRIDE
  $oldTwoLetter = $script:RX_REGION_2LETTER_OVERRIDE
  $oldLang = $script:RX_LANG_OVERRIDE

  $applyResult = $null
  $afterRows = @()
  try {
    $applyResult = Set-RegionRulesOverride -OrderedText $OrderedText -TwoLetterText $TwoLetterText -LangPattern $LangPattern
    if (-not $applyResult.Success) {
      return [pscustomobject]@{ Success = $false; Error = 'Regel-Simulation fehlgeschlagen.'; Errors = @($applyResult.Errors) }
    }

    $afterRows = @(Get-DuplicateInspectorRows -Roots $Roots -Extensions $Extensions -AliasEditionKeying:$AliasEditionKeying -ManualOverrides $ManualWinnerOverrides)
  } finally {
    $script:RX_REGION_ORDERED_OVERRIDE = $oldOrdered
    $script:RX_REGION_2LETTER_OVERRIDE = $oldTwoLetter
    $script:RX_LANG_OVERRIDE = $oldLang
  }

  $afterWinnerByKey = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($row in @($afterRows | Where-Object { $_.Winner })) {
    $afterWinnerByKey[[string]$row.GameKey] = [string]$row.MainPath
  }

  $allKeys = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($k in $beforeWinnerByKey.Keys) { [void]$allKeys.Add([string]$k) }
  foreach ($k in $afterWinnerByKey.Keys) { [void]$allKeys.Add([string]$k) }

  $changed = New-Object System.Collections.Generic.List[object]
  foreach ($k in $allKeys) {
    $beforePath = if ($beforeWinnerByKey.ContainsKey($k)) { [string]$beforeWinnerByKey[$k] } else { '' }
    $afterPath = if ($afterWinnerByKey.ContainsKey($k)) { [string]$afterWinnerByKey[$k] } else { '' }
    if ($beforePath -ne $afterPath) {
      [void]$changed.Add([pscustomobject]@{
        GameKey = [string]$k
        WinnerBefore = $beforePath
        WinnerAfter = $afterPath
      })
    }
  }

  $groupCount = [int]$allKeys.Count
  $changedCount = [int]$changed.Count
  $changeRate = if ($groupCount -gt 0) { [math]::Round(($changedCount * 100.0 / $groupCount), 1) } else { 0 }
  $sample = @($changed | Select-Object -First $MaxSampleChanges)

  if ($Log) {
    & $Log ('Rule Impact Simulator: Gruppen={0}, Gewinnerwechsel={1} ({2}%)' -f $groupCount, $changedCount, $changeRate)
  }

  return [pscustomobject]@{
    Success = $true
    GroupCount = $groupCount
    ChangedWinners = $changedCount
    ChangeRatePercent = $changeRate
    BeforeRows = $beforeRows.Count
    AfterRows = $afterRows.Count
    ChangedSamples = $sample
    Errors = @()
  }
}
