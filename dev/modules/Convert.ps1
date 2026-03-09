# ================================================================
#  FORMAT TABLES  (extracted from simple_sort.ps1 - Sprint 2)
# ================================================================

# Best target format per console type
$script:BEST_FORMAT = @{
  # CD-basiert -> CHD (chdman createcd)
  'PS1'    = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  'SAT'    = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  'DC'     = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  'SCD'    = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  'PCECD'  = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  'NEOCD'  = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  '3DO'    = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  'JAGCD'  = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  # DVD-basiert -> CHD (chdman createdvd)
  'PS2'    = @{ Ext='.chd'; Tool='chdman';      Cmd='createdvd' }
  # PSP -> CHD (chdman createcd fuer UMD)
  'PSP'    = @{ Ext='.chd'; Tool='chdman';      Cmd='createcd' }
  # GameCube/Wii -> RVZ (DolphinTool)
  'GC'     = @{ Ext='.rvz'; Tool='dolphintool'; Cmd='convert' }
  'WII'    = @{ Ext='.rvz'; Tool='dolphintool'; Cmd='convert' }
  # Cartridge -> ZIP (7z)
  'NES'    = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'SNES'   = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'N64'    = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'GB'     = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'GBC'    = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'GBA'    = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'NDS'    = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'MD'     = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'SMS'    = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'GG'     = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'PCE'    = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'NEOGEO' = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
  'ARCADE' = @{ Ext='.zip'; Tool='7z';          Cmd='zip' }
}

# Special format: PBP (PSOne Classics) -> BIN/CUE -> CHD
$script:PBP_FORMAT = @{ Ext='.chd'; Tool='psxtract'; Cmd='pbp2chd' }

function Get-TargetFormat {
  <# Determine target conversion format by console + source extension. #>
  param(
    [string]$ConsoleType,
    [string]$Extension
  )

  $ext = if ($Extension) { $Extension.ToLowerInvariant() } else { '' }
  if ($ext -eq '.pbp') { return $script:PBP_FORMAT }

  if ([string]::IsNullOrWhiteSpace($ConsoleType)) { return $null }
  $consoleKey = $ConsoleType.Trim().ToUpperInvariant()
  if ($script:BEST_FORMAT.ContainsKey($consoleKey)) {
    return $script:BEST_FORMAT[$consoleKey]
  }

  return $null
}

# ================================================================

function Invoke-ChdmanProcess {
  <# Shared chdman process runner with consistent output cleanup. #>
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ToolPath,
    [Parameter(Mandatory=$true)][string[]]$ToolArgs,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$OutputPath,
    [System.Collections.Generic.List[string]]$TempFiles,
    [scriptblock]$Log,
    [string]$ErrorLabel = 'chdman'
  )

  $result = Invoke-ExternalToolProcess -ToolPath $ToolPath -ToolArgs $ToolArgs -TempFiles $TempFiles -Log $Log -ErrorLabel $ErrorLabel
  if (-not $result.Success) {
    if (Test-Path -LiteralPath $OutputPath) {
      Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
    }
  }
  return $result
}

function Invoke-CsoToIso {
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ToolPath,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$InputPath,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$OutputPath,
    [System.Collections.Generic.List[string]]$TempFiles,
    [scriptblock]$Log
  )

  $attempts = @(
    @('0', (ConvertTo-QuotedArg $InputPath), (ConvertTo-QuotedArg $OutputPath)),
    @('-d', (ConvertTo-QuotedArg $InputPath), (ConvertTo-QuotedArg $OutputPath))
  )

  foreach ($toolArgs in $attempts) {
    if (Test-Path -LiteralPath $OutputPath) {
      Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
    }

    $result = Invoke-ExternalToolProcess -ToolPath $ToolPath -ToolArgs $toolArgs -TempFiles $TempFiles -Log $Log -ErrorLabel 'ciso'
    if (-not $result.Success) { continue }

    $outItem = Get-Item -LiteralPath $OutputPath -ErrorAction SilentlyContinue
    if ($outItem -and -not $outItem.PSIsContainer -and [long]$outItem.Length -gt 0) {
      return $true
    }
  }

  # BUG-014 FIX: Clean up partial output file after all retry attempts have failed,
  # so we don't leave a zero-byte or corrupt file from the last attempt on disk.
  if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
  }

  return $false
}

function Test-ConvertedOutputVerified {
  param(
    [string]$TargetPath,
    [string]$TargetExt,
    [hashtable]$FormatInfo,
    [string]$ToolPath,
    [string]$SevenZipPath,
    [scriptblock]$Log
  )

  $verifyTempFiles = [System.Collections.Generic.List[string]]::new()
  try {
    if ($TargetExt -eq '.chd') {
      $verifyTool = $null
      if ($FormatInfo.Tool -eq 'chdman') {
        $verifyTool = $ToolPath
      } elseif ($FormatInfo.Tool -eq 'psxtract') {
        if ($script:TOOL_CACHE.ContainsKey('chdman')) {
          $cachedVerify = [string]$script:TOOL_CACHE['chdman']
          if ($cachedVerify -and (Test-Path -LiteralPath $cachedVerify)) { $verifyTool = $cachedVerify }
        }
        if (-not $verifyTool) { $verifyTool = Find-ConversionTool -ToolName 'chdman' }
      }

      if (-not $verifyTool) {
        return [pscustomobject]@{ Success = $false; Reason = 'chd-verify-tool-missing' }
      }

      $verifyArgs = @('verify', '-i', (ConvertTo-QuotedArg $TargetPath))
      $verifyResult = Invoke-ExternalToolProcess -ToolPath $verifyTool -ToolArgs $verifyArgs -TempFiles $verifyTempFiles -Log $Log -ErrorLabel 'chdman verify'
      if (-not $verifyResult.Success) {
        return [pscustomobject]@{ Success = $false; Reason = 'chd-verify-failed' }
      }

      return [pscustomobject]@{ Success = $true; Reason = $null }
    }

    if ($TargetExt -eq '.rvz') {
      if ([string]::IsNullOrWhiteSpace($ToolPath) -or -not (Test-Path -LiteralPath $ToolPath)) {
        return [pscustomobject]@{ Success = $false; Reason = 'rvz-verify-tool-missing' }
      }

      $verifyArgs = @('verify', '-i', (ConvertTo-QuotedArg $TargetPath))
      $verifyResult = Invoke-ExternalToolProcess -ToolPath $ToolPath -ToolArgs $verifyArgs -TempFiles $verifyTempFiles -Log $Log -ErrorLabel 'dolphintool verify'
      if (-not $verifyResult.Success) {
        return [pscustomobject]@{ Success = $false; Reason = 'rvz-verify-failed' }
      }

      return [pscustomobject]@{ Success = $true; Reason = $null }
    }

    if ($TargetExt -eq '.zip') {
      $zipTool = $null
      if (-not [string]::IsNullOrWhiteSpace($ToolPath) -and (Test-Path -LiteralPath $ToolPath)) {
        $zipTool = $ToolPath
      } elseif (-not [string]::IsNullOrWhiteSpace($SevenZipPath) -and (Test-Path -LiteralPath $SevenZipPath)) {
        $zipTool = $SevenZipPath
      }

      if (-not $zipTool) {
        return [pscustomobject]@{ Success = $false; Reason = 'zip-verify-tool-missing' }
      }

      $verifyArgs = @('t', '-y', (ConvertTo-QuotedArg $TargetPath))
      $verifyResult = Invoke-ExternalToolProcess -ToolPath $zipTool -ToolArgs $verifyArgs -TempFiles $verifyTempFiles -Log $Log -ErrorLabel '7z verify'
      if (-not $verifyResult.Success) {
        return [pscustomobject]@{ Success = $false; Reason = 'zip-verify-failed' }
      }

      $entries = @(Get-ArchiveEntryPaths -ArchivePath $TargetPath -SevenZipPath $zipTool -TempFiles $verifyTempFiles)
      if ($entries.Count -eq 0) {
        return [pscustomobject]@{ Success = $false; Reason = 'zip-verify-empty' }
      }
      if (-not (Test-ArchiveEntryPathsSafe -EntryPaths $entries)) {
        return [pscustomobject]@{ Success = $false; Reason = 'zip-verify-unsafe' }
      }

      return [pscustomobject]@{ Success = $true; Reason = $null }
    }

    return [pscustomobject]@{ Success = $false; Reason = 'verify-not-supported' }
  } finally {
    foreach ($vt in $verifyTempFiles) {
      if ($vt -and (Test-Path -LiteralPath $vt)) {
        Remove-Item -LiteralPath $vt -Force -ErrorAction SilentlyContinue
      }
    }
  }
}

function Get-ConvertToolStrategy {
  <# Resolve conversion strategy function name for a tool. #>
  param([string]$Tool)

  if ([string]::IsNullOrWhiteSpace($Tool)) { return $null }

  switch ($Tool.Trim().ToLowerInvariant()) {
    'psxtract'    { return 'Invoke-ConvertStrategyPsxtract' }
    'chdman'      { return 'Invoke-ConvertStrategyChdman' }
    'dolphintool' { return 'Invoke-ConvertStrategyDolphinTool' }
    '7z'          { return 'Invoke-ConvertStrategy7z' }
    default       { return $null }
  }
}

function Invoke-ConvertStrategyPsxtract {
  param(
    [hashtable]$Context
  )

  if ($Context.SourceExt -ne '.pbp') {
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'unknown-tool') }
  }

  $chdmanPath = if ($script:TOOL_CACHE.ContainsKey('chdman')) { [string]$script:TOOL_CACHE['chdman'] } else { $null }
  if (-not $chdmanPath -or -not (Test-Path -LiteralPath $chdmanPath)) { $chdmanPath = Find-ConversionTool -ToolName 'chdman' }
  if (-not $chdmanPath) { return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'missing-chdman') } }

  $pbpDir = Join-Path $env:TEMP ('rom_pbp_extract_' + [guid]::NewGuid().ToString('N'))
  [void](New-Item -ItemType Directory -Path $pbpDir)
  [void]$Context.TempDirs.Add($pbpDir)

  $toolArgs = @(ConvertTo-QuotedArg $Context.SourcePath)
  $procResult = Invoke-ExternalToolProcess -ToolPath $Context.ToolPath -ToolArgs $toolArgs -WorkingDirectory $pbpDir -TempFiles $Context.TempFiles -Log $Context.Log -ErrorLabel 'psxtract'
  if (-not $procResult.Success) {
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'ERROR' $null 'psxtract-failed' @{
      Tool = 'psxtract'
      ExitCode = $procResult.ExitCode
      Stderr = [string]$procResult.ErrorText
      Source = $Context.MainPath
    }) }
  }

  $cueFile = Get-ChildItem -LiteralPath $pbpDir -Filter '*.cue' -File -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $cueFile) { return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'ERROR' $null 'pbp-no-cue') } }

  $toolArgs = @('createcd', '-i', (ConvertTo-QuotedArg $cueFile.FullName), '-o', (ConvertTo-QuotedArg $Context.TargetPath))
  $procResult = Invoke-ExternalToolProcess -ToolPath $chdmanPath -ToolArgs $toolArgs -TempFiles $Context.TempFiles -Log $Context.Log -ErrorLabel 'chdman'
  if (-not $procResult.Success) {
    if (Test-Path -LiteralPath $Context.TargetPath) { Remove-Item -LiteralPath $Context.TargetPath -Force -ErrorAction SilentlyContinue }
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'ERROR' $null 'chdman-failed' @{
      Tool = 'chdman'
      ExitCode = $procResult.ExitCode
      Stderr = [string]$procResult.ErrorText
      Source = $Context.MainPath
    }) }
  }

  return [pscustomobject]@{ Outcome = $null }
}

function Invoke-ConvertStrategyChdman {
  param([hashtable]$Context)

  $toolArgs = @($Context.Cmd, '-i', (ConvertTo-QuotedArg $Context.SourcePath), '-o', (ConvertTo-QuotedArg $Context.TargetPath))
  $procResult = Invoke-ChdmanProcess -ToolPath $Context.ToolPath -ToolArgs $toolArgs -OutputPath $Context.TargetPath -TempFiles $Context.TempFiles -Log $Context.Log -ErrorLabel 'chdman'
  if (-not $procResult.Success) {
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'ERROR' $null 'chdman-failed' @{
      Tool = 'chdman'
      ExitCode = $procResult.ExitCode
      Stderr = [string]$procResult.ErrorText
      Source = $Context.MainPath
    }) }
  }

  return [pscustomobject]@{ Outcome = $null }
}

function Invoke-ConvertStrategyDolphinTool {
  param([hashtable]$Context)

  $sourceExt = [string]$Context.SourceExt
  $allowedDolphinExts = @('.iso', '.gcm', '.wbfs', '.rvz', '.gcz', '.wia')
  if ($sourceExt -notin $allowedDolphinExts) {
    if ($Context.Log) { & $Context.Log ("  SKIP (Dolphin: unpassendes Quellformat): {0}" -f $Context.MainPath) }
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'dolphintool-unsupported-source') }
  }

  if ($sourceExt -eq '.rvz') {
    if ($Context.Log) { & $Context.Log ("  SKIP (Dolphin: bereits RVZ): {0}" -f $Context.MainPath) }
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'dolphintool-already-compressed') }
  }

  if (Get-Command Get-ConsoleFromDolphinTool -ErrorAction SilentlyContinue) {
    $discConsole = Get-ConsoleFromDolphinTool -Path $Context.SourcePath -ToolPath $Context.ToolPath
    if ($discConsole -notin @('GC', 'WII')) {
      if ($sourceExt -eq '.wbfs') {
        if (Get-Command Get-ConsoleFromDiscIdInFileName -ErrorAction SilentlyContinue) {
          $nameConsole = Get-ConsoleFromDiscIdInFileName -Path $Context.SourcePath
          if ($nameConsole -in @('GC', 'WII')) { $discConsole = $nameConsole }
        }
      }
    }

    if ($discConsole -notin @('GC', 'WII')) {
      if ($sourceExt -eq '.wbfs') {
        if ($Context.Log) { & $Context.Log ("  SKIP (Dolphin: WBFS Disc-Check unklar, uebersprungen fuer Stabilitaet): {0}" -f $Context.MainPath) }
        return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'dolphintool-wbfs-probe-failed') }
      }
      if ($Context.Log) { & $Context.Log ("  SKIP (Dolphin: kein gueltiges GC/Wii Disc-Image): {0}" -f $Context.MainPath) }
      return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'dolphintool-non-disc-image') }
    }
  }

  if ($sourceExt -eq '.wbfs' -and (Get-Command Test-WbfsAccurateFromDolphinTool -ErrorAction SilentlyContinue)) {
    $wbfsAccurate = Test-WbfsAccurateFromDolphinTool -Path $Context.SourcePath -ToolPath $Context.ToolPath
    if (-not $wbfsAccurate) {
      if ($Context.Log) { & $Context.Log ("  SKIP (Dolphin: WBFS nicht 'Accurate', uebersprungen fuer Stabilitaet): {0}" -f $Context.MainPath) }
      return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'dolphintool-wbfs-inaccurate') }
    }
  }

  $toolArgs = @('convert', '-i', (ConvertTo-QuotedArg $Context.SourcePath), '-o', (ConvertTo-QuotedArg $Context.TargetPath), '-f', 'rvz', '-c', 'zstd', '-l', '5', '-b', '131072')
  $procResult = Invoke-ExternalToolProcess -ToolPath $Context.ToolPath -ToolArgs $toolArgs -TempFiles $Context.TempFiles -Log $Context.Log -ErrorLabel 'dolphintool'
  if (-not $procResult.Success) {
    if (Test-Path -LiteralPath $Context.TargetPath) { Remove-Item -LiteralPath $Context.TargetPath -Force -ErrorAction SilentlyContinue }
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'ERROR' $null 'dolphintool-failed' @{
      Tool = 'dolphintool'
      ExitCode = $procResult.ExitCode
      Stderr = [string]$procResult.ErrorText
      Source = $Context.MainPath
    }) }
  }

  return [pscustomobject]@{ Outcome = $null }
}

function Invoke-ConvertStrategy7z {
  param([hashtable]$Context)

  $sourceExt = [string]$Context.SourceExt
  if ($sourceExt -eq '.zip') { return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'already-zip') } }

  $packSource = $Context.MainPath
  if ($sourceExt -in @('.7z', '.rar', '.gz')) {
    $tempDir = Expand-ArchiveToTemp -ArchivePath $Context.MainPath -SevenZipPath $Context.SevenZipPath -Log $Context.Log
    if ($tempDir -eq 'SKIP') { return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'SKIP' $null 'archive-expand-skip') } }
    if (-not $tempDir) { return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'ERROR' $null 'archive-expand-error') } }
    [void]$Context.TempDirs.Add($tempDir)
    $packSource = Join-Path $tempDir '*'
  }

  $toolArgs = @('a', '-tzip', '-y', (ConvertTo-QuotedArg $Context.TargetPath), (ConvertTo-QuotedArg $packSource))
  $procResult = Invoke-ExternalToolProcess -ToolPath $Context.ToolPath -ToolArgs $toolArgs -TempFiles $Context.TempFiles -Log $Context.Log -ErrorLabel '7z'
  if (-not $procResult.Success) {
    if (Test-Path -LiteralPath $Context.TargetPath) { Remove-Item -LiteralPath $Context.TargetPath -Force -ErrorAction SilentlyContinue }
    return [pscustomobject]@{ Outcome = (& $Context.NewOutcome 'ERROR' $null '7z-failed' @{
      Tool = '7z'
      ExitCode = $procResult.ExitCode
      Stderr = [string]$procResult.ErrorText
      Source = $Context.MainPath
    }) }
  }

  return [pscustomobject]@{ Outcome = $null }
}

function Get-ConversionBackupSettings {
  $enabled = $true
  $retentionDays = 7

  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try { $enabled = [bool](Get-AppStateValue -Key 'ConversionBackupEnabled' -Default $enabled) } catch { }
    try {
      $configuredDays = Get-AppStateValue -Key 'ConversionBackupRetentionDays' -Default $retentionDays
      if ($configuredDays -is [int] -or $configuredDays -is [long]) {
        $retentionDays = [int][Math]::Max(1, [int64]$configuredDays)
      }
    } catch { }
  }

  return [pscustomobject]@{
    Enabled = [bool]$enabled
    RetentionDays = [int]$retentionDays
  }
}

function Move-ConvertedSourceToBackup {
  param(
    [Parameter(Mandatory=$true)][string]$Path,
    [int]$RetentionDays = 7,
    [scriptblock]$Log
  )

  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
  if ($RetentionDays -lt 1) { $RetentionDays = 1 }

  $backupPath = ($Path + '.converted_backup')
  if (Test-Path -LiteralPath $backupPath) {
    $backupPath = ('{0}.{1}.converted_backup' -f $Path, (Get-Date -Format 'yyyyMMdd-HHmmss'))
  }

  Move-Item -LiteralPath $Path -Destination $backupPath -ErrorAction Stop

  $directory = Split-Path -Parent $Path
  if (-not [string]::IsNullOrWhiteSpace($directory) -and (Test-Path -LiteralPath $directory -PathType Container)) {
    $cutoff = (Get-Date).AddDays(-1 * [double]$RetentionDays)
    foreach ($stale in @(Get-ChildItem -LiteralPath $directory -Filter '*.converted_backup*' -File -ErrorAction SilentlyContinue)) {
      if ($stale.LastWriteTime -gt $cutoff) { continue }
      try {
        Remove-Item -LiteralPath $stale.FullName -Force -ErrorAction Stop
        if ($Log) { & $Log ('  GC: altes Backup entfernt: {0}' -f $stale.FullName) }
      } catch { }
    }
  }

  return $backupPath
}

function Invoke-ConvertItem {
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Item,
    [Parameter(Mandatory=$true)][hashtable]$FormatInfo,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ToolPath,
    [scriptblock]$Log,
    [string]$SevenZipPath
  )

  $newOutcome = {
    param([string]$Status, [string]$Path, [string]$Reason, [hashtable]$Details)
    [pscustomobject]@{ Status = $Status; Path = $Path; Reason = $Reason; Details = $Details }
  }

  $targetExt = $FormatInfo.Ext
  $cmd = $FormatInfo.Cmd
  $mainPath = $Item.MainPath
  $dir = Split-Path -Parent $mainPath
  $baseName = [IO.Path]::GetFileNameWithoutExtension($mainPath)
  $sourceExt = [IO.Path]::GetExtension($mainPath).ToLowerInvariant()
  $targetPath = Join-Path $dir ($baseName + $targetExt)
  $workingTargetPath = ($targetPath + '.converting')

  $tempFiles = [System.Collections.Generic.List[string]]::new()
  $tempDirs = [System.Collections.Generic.List[string]]::new()
  $rootForMissing = $Item.Root
  $sourcePath = $mainPath
  $removePaths = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  if ($mainPath) { [void]$removePaths.Add($mainPath) }
  if ($Item.PSObject.Properties['Paths']) {
    foreach ($p in $Item.Paths) {
      if ($p) { [void]$removePaths.Add($p) }
    }
  }

  try {

    if (-not (Test-Path -LiteralPath $mainPath)) {
      if ($Log) { & $Log ("  SKIP (Quelle fehlt): {0}" -f $mainPath) }
      return (& $newOutcome 'SKIP' $null 'missing-source')
    }

    if (Test-Path -LiteralPath $targetPath) {
      if ($Log) { & $Log ("  SKIP (Ziel existiert): {0}" -f $targetPath) }
      return (& $newOutcome 'SKIP' $null 'target-exists')
    }
    if (Test-Path -LiteralPath $workingTargetPath) {
      if ($Log) { & $Log ("  SKIP (Temp-Ziel existiert): {0}" -f $workingTargetPath) }
      return (& $newOutcome 'SKIP' $null 'target-temp-exists')
    }

    $archiveExts = @('.zip', '.7z', '.rar', '.gz')
    if ($FormatInfo.Tool -ne '7z' -and $sourceExt -in $archiveExts) {
      $tempDir = Expand-ArchiveToTemp -ArchivePath $mainPath -SevenZipPath $SevenZipPath -Log $Log
      if ($tempDir -eq 'SKIP') { return (& $newOutcome 'SKIP' $null 'archive-expand-skip') }
      if (-not $tempDir) { return (& $newOutcome 'ERROR' $null 'archive-expand-error') }
      [void]$tempDirs.Add($tempDir)
      $discFiles = @(Find-DiscImageInDir -RootPath $tempDir)
      if (-not $discFiles -or $discFiles.Count -eq 0) {
        if ($Log) { & $Log ("  SKIP (kein Disc-Image im Archiv): {0}" -f $mainPath) }
        return (& $newOutcome 'SKIP' $null 'archive-no-disc')
      }
      $sourcePath = $discFiles[0].FullName
      $sourceExt = [IO.Path]::GetExtension($sourcePath).ToLowerInvariant()
      $rootForMissing = $null
    }

    if ($sourceExt -eq '.cso') {
      $cisoPath = $null
      if ($script:TOOL_CACHE.ContainsKey('ciso')) {
        $cachedCiso = [string]$script:TOOL_CACHE['ciso']
        if ($cachedCiso -and (Test-Path -LiteralPath $cachedCiso)) { $cisoPath = $cachedCiso }
      }
      if (-not $cisoPath) {
        $cisoPath = Find-ConversionTool -ToolName 'ciso'
        if ($cisoPath) { $script:TOOL_CACHE['ciso'] = $cisoPath }
      }
      if (-not $cisoPath) {
        if ($Log) { & $Log ('  SKIP (ciso fehlt): {0}' -f $mainPath) }
        return (& $newOutcome 'SKIP' $null 'missing-ciso')
      }

      $tempIsoPath = Join-Path $env:TEMP ('rom_cso_unpack_' + [guid]::NewGuid().ToString('N') + '.iso')
      [void]$tempFiles.Add($tempIsoPath)
      if (-not (Invoke-CsoToIso -ToolPath $cisoPath -InputPath $sourcePath -OutputPath $tempIsoPath -TempFiles $tempFiles -Log $Log)) {
        if (Test-Path -LiteralPath $tempIsoPath) { Remove-Item -LiteralPath $tempIsoPath -Force -ErrorAction SilentlyContinue }
        return (& $newOutcome 'ERROR' $null 'ciso-failed' @{ Tool = 'ciso'; Source = $mainPath })
      }

      $sourcePath = $tempIsoPath
      $sourceExt = '.iso'
      $rootForMissing = $null
    }

    if ($sourceExt -eq '.cue') {
      $missing = @(Get-CueMissingFiles -CuePath $sourcePath -RootPath $rootForMissing)
      if ($missing.Count -gt 0) { return (& $newOutcome 'SKIP' $null 'cue-incomplete') }
    } elseif ($sourceExt -eq '.gdi') {
      $missing = @(Get-GdiMissingFiles -GdiPath $sourcePath -RootPath $rootForMissing)
      if ($missing.Count -gt 0) { return (& $newOutcome 'SKIP' $null 'gdi-incomplete') }
    } elseif ($sourceExt -eq '.ccd') {
      $missing = @(Get-CcdMissingFiles -CcdPath $sourcePath -RootPath $rootForMissing)
      if ($missing.Count -gt 0) { return (& $newOutcome 'SKIP' $null 'ccd-incomplete') }
    } elseif ($Item.Type -eq 'M3USET') {
      $missing = @(Get-M3UMissingFiles -M3UPath $sourcePath -RootPath $rootForMissing)
      if ($missing.Count -gt 0) { return (& $newOutcome 'SKIP' $null 'm3u-incomplete') }
    }

    $strategyName = Get-ConvertToolStrategy -Tool $FormatInfo.Tool
    if ([string]::IsNullOrWhiteSpace($strategyName)) {
      return (& $newOutcome 'SKIP' $null 'unknown-tool')
    }

    $strategyContext = @{
      Item = $Item
      FormatInfo = $FormatInfo
      ToolPath = $ToolPath
      SevenZipPath = $SevenZipPath
      SourcePath = $sourcePath
      SourceExt = $sourceExt
      MainPath = $mainPath
      TargetPath = $workingTargetPath
      Cmd = $cmd
      RootForMissing = $rootForMissing
      TempFiles = $tempFiles
      TempDirs = $tempDirs
      Log = $Log
      NewOutcome = $newOutcome
    }

    $strategyResult = & $strategyName -Context $strategyContext
    if ($strategyResult -and $strategyResult.Outcome) {
      return $strategyResult.Outcome
    }

    $targetItem = Get-Item -LiteralPath $workingTargetPath -ErrorAction SilentlyContinue
    if (-not $targetItem -or $targetItem.PSIsContainer -or [long]$targetItem.Length -le 0) {
      if ($Log) { & $Log ("  ERROR: Ziel fehlt/leer nach Konvertierung: {0}" -f $workingTargetPath) }
      if (Test-Path -LiteralPath $workingTargetPath) { Remove-Item -LiteralPath $workingTargetPath -Force -ErrorAction SilentlyContinue }
      if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Force -ErrorAction SilentlyContinue }
      return (& $newOutcome 'ERROR' $null 'output-missing-or-empty' @{ Source = $mainPath; Target = $workingTargetPath })
    }

    $verifyOutcome = Test-ConvertedOutputVerified -TargetPath $workingTargetPath -TargetExt $targetExt `
      -FormatInfo $FormatInfo -ToolPath $ToolPath -SevenZipPath $SevenZipPath -Log $Log
    if (-not $verifyOutcome.Success) {
      if (Test-Path -LiteralPath $workingTargetPath) { Remove-Item -LiteralPath $workingTargetPath -Force -ErrorAction SilentlyContinue }
      if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Force -ErrorAction SilentlyContinue }
      return (& $newOutcome 'ERROR' $null $verifyOutcome.Reason @{ Source = $mainPath; Target = $workingTargetPath })
    }

    try {
      Move-Item -LiteralPath $workingTargetPath -Destination $targetPath -ErrorAction Stop
    } catch {
      if ($Log) { & $Log ("  ERROR: Ziel-Commit fehlgeschlagen: {0} -> {1}" -f $workingTargetPath, $targetPath) }
      if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Force -ErrorAction SilentlyContinue }
      return (& $newOutcome 'ERROR' $null 'target-commit-failed' @{ Source = $mainPath; Target = $targetPath })
    }

    [void]$removePaths.Remove($targetPath)
    [void]$removePaths.Remove($workingTargetPath)

    # BUG-009 FIX: Verify final target BEFORE removing source files (TOCTOU prevention)
    $finalTargetItem = Get-Item -LiteralPath $targetPath -ErrorAction SilentlyContinue
    if (-not $finalTargetItem -or $finalTargetItem.PSIsContainer -or [long]$finalTargetItem.Length -le 0) {
      return (& $newOutcome 'ERROR' $null 'target-missing-after-commit' @{ Source = $mainPath; Target = $targetPath })
    }

    $backupSettings = Get-ConversionBackupSettings

    foreach ($p in $removePaths) {
      if ($p -and (Test-Path -LiteralPath $p)) {
        if ($backupSettings.Enabled) {
          try {
            $backupPath = Move-ConvertedSourceToBackup -Path $p -RetentionDays $backupSettings.RetentionDays -Log $Log
            if ($Log -and $backupPath) { & $Log ('  Backup: {0} -> {1}' -f $p, $backupPath) }
          } catch {
            # BUG CONV-005 FIX: Do NOT delete source when backup fails — preserve the original file
            if ($Log) { & $Log ('  WARN: Backup fehlgeschlagen, Quelldatei wird beibehalten: {0} ({1})' -f $p, $_.Exception.Message) }
          }
        } else {
          Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
        }
      }
    }

    return (& $newOutcome 'OK' $targetPath $null)
  } finally {
    foreach ($tf in $tempFiles) { if ($tf -and (Test-Path -LiteralPath $tf)) { Remove-Item -LiteralPath $tf -Force -ErrorAction SilentlyContinue } }
    foreach ($td in $tempDirs) { if ($td -and (Test-Path -LiteralPath $td)) { Remove-Item -LiteralPath $td -Recurse -Force -ErrorAction SilentlyContinue } }
  }
}

function Format-ConversionErrorDetail {
  <# Formats error detail from conversion outcome for Add-RunError reporting. #>
  param(
    [Parameter(Mandatory=$true)][psobject]$Outcome,
    [Parameter(Mandatory=$true)][string]$MainPath
  )

  $detailText = $null
  if ($Outcome.PSObject.Properties['Details'] -and $Outcome.Details) {
    $d = $Outcome.Details
    $tool = if ($d.ContainsKey('Tool')) { [string]$d.Tool } else { '' }
    $exit = if ($d.ContainsKey('ExitCode')) { [string]$d.ExitCode } else { '' }
    $stderr = if ($d.ContainsKey('Stderr')) { [string]$d.Stderr } else { '' }
    if ($stderr -and $stderr.Length -gt 180) { $stderr = $stderr.Substring(0,180) + [char]0x2026 }
    $parts = @()
    if ($tool) { $parts += ('tool={0}' -f $tool) }
    if ($exit) { $parts += ('exit={0}' -f $exit) }
    if ($stderr) { $parts += ('err={0}' -f ($stderr -replace '[\r\n]+',' ')) }
    if ($parts.Count -gt 0) { $detailText = ($parts -join '; ') }
  }

  if ($detailText) {
    return ('{0} ({1}) [{2}]' -f $MainPath, $Outcome.Reason, $detailText)
  }
  return ('{0} ({1})' -f $MainPath, $Outcome.Reason)
}

function New-ConversionAuditRow {
  <# Builds a single CSV audit row for conversion results (11-field format). #>
  param(
    [string]$Status,
    [string]$ToolName,
    [string]$MainPath,
    [string]$TargetExt,
    [string]$OutputPath,
    [string]$Reason,
    [long]$OldSize,
    [long]$NewSize,
    [long]$Saved
  )

  $srcExt = [IO.Path]::GetExtension($MainPath).ToLowerInvariant()
  # BUG-039 FIX: CSV injection protection for user-controlled path values
  $safeCsvValue = {
    param([string]$v)
    $v = $v -replace '"','""'
    if ($v -match '^[=+\-@\|]') { $v = "'" + $v }
    $v
  }
  return ('"{0}","{1}","{2}","{3}","{4}","{5}","{6}","{7}",{8},{9},{10}' -f `
    (Get-Date).ToString('o'), $Status, $ToolName, $srcExt, $TargetExt,
    (& $safeCsvValue $MainPath),
    (& $safeCsvValue ([string]$OutputPath)),
    (& $safeCsvValue ([string]$Reason)),
    $OldSize, $NewSize, $Saved)
}

function Invoke-ConversionWithRetry {
  <# Retries a conversion when the initial result has a retryable reason. #>
  param(
    [Parameter(Mandatory=$true)][psobject]$InitialResult,
    [Parameter(Mandatory=$true)][scriptblock]$ConvertAction,
    [System.Collections.Generic.HashSet[string]]$RetryableReasons,
    [int]$MaxRetries,
    [int]$RetryDelayMs,
    [string]$FileName,
    [scriptblock]$Log
  )

  $result = $InitialResult
  if ($result.Status -eq 'ERROR' -and $RetryableReasons -and $RetryableReasons.Contains($result.Reason)) {
    for ($retry = 1; $retry -le $MaxRetries; $retry++) {
      if ($Log) { & $Log ("  Wiederholung {0}/{1} fuer: {2} (Grund: {3})" -f $retry, $MaxRetries, $FileName, $result.Reason) }
      Start-Sleep -Milliseconds $RetryDelayMs
      $result = & $ConvertAction
      if ($result.Status -ne 'ERROR' -or -not $RetryableReasons.Contains($result.Reason)) { break }
    }
  }
  return $result
}

function New-ConversionRunspacePool {
  <# Erstellt einen RunspacePool mit allen fuer Invoke-ConvertItem benoetigten Funktionen.
     Wait-ProcessResponsive und Test-CancelRequested werden fuer den Runspace-Kontext ueberschrieben. #>
  param(
    [int]$MaxParallel,
    [hashtable]$ToolPaths,
    [System.Threading.ManualResetEventSlim]$CancelEvent,
    [System.Collections.Concurrent.ConcurrentQueue[string]]$LogQueue
  )

  $iss = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault()

  # Funktionsdefinitionen aus der aktuellen Session sammeln
  $funcNames = @(
    # Convert.ps1
    'Invoke-ConvertItem', 'Invoke-ConversionWithRetry', 'Get-ConvertToolStrategy',
    'Invoke-ConvertStrategyPsxtract', 'Invoke-ConvertStrategyChdman',
    'Invoke-ConvertStrategyDolphinTool', 'Invoke-ConvertStrategy7z',
    'Invoke-CsoToIso', 'Test-ConvertedOutputVerified',
    'Get-ConversionBackupSettings', 'Move-ConvertedSourceToBackup',
    # Convert.ps1 — BUG CONV-001/002/003/004 FIX: transitively called functions
    'Invoke-ChdmanProcess', 'Get-TargetFormat',
    'Format-ConversionErrorDetail', 'New-ConversionAuditRow',
    # Tools.ps1
    'ConvertTo-QuotedArg', 'ConvertTo-ArgString', 'Test-ToolBinaryHash',
    'Find-ConversionTool', 'Invoke-NativeProcess', 'Invoke-7z',
    'Test-ArchiveEntryPathsSafe', 'Get-ArchiveEntryPaths',
    'Get-ConsoleFromDolphinTool', 'Test-WbfsAccurateFromDolphinTool',
    'Expand-ArchiveToTemp', 'Find-DiscImageInDir',
    # simple_sort.ps1
    'Invoke-ExternalToolProcess',
    'Get-CueMissingFiles', 'Get-GdiMissingFiles', 'Get-CcdMissingFiles', 'Get-M3UMissingFiles',
    'Resolve-SetReferencePath', 'Resolve-ChildPathWithinRoot', 'Resolve-RootPath',
    'Test-PathWithinRoot', 'Test-PathHasReparsePoint', 'Get-NearestExistingDirectory',
    'Get-FilesSafe'
  )

  foreach ($fn in $funcNames) {
    $cmd = Get-Command $fn -CommandType Function -ErrorAction SilentlyContinue
    if ($cmd) {
      $entry = [System.Management.Automation.Runspaces.SessionStateFunctionEntry]::new($fn, $cmd.Definition)
      $iss.Commands.Add($entry)
    }
  }

  # Wait-ProcessResponsive: kein DoEvents (kein GUI im Runspace), Cancel ueber shared Event
  $waitDef = Get-WaitProcessResponsiveDefinition
  $iss.Commands.Add(
    [System.Management.Automation.Runspaces.SessionStateFunctionEntry]::new('Wait-ProcessResponsive', $waitDef)
  )

  # Test-CancelRequested: prueft shared Event statt $script:CancelRequested
  $cancelDef = @'
if ($script:_CancelEvent -and $script:_CancelEvent.IsSet) {
  throw [System.OperationCanceledException]::new("Abbruch durch Benutzer")
}
'@
  $iss.Commands.Add(
    [System.Management.Automation.Runspaces.SessionStateFunctionEntry]::new('Test-CancelRequested', $cancelDef)
  )

  # Variablen im ISS setzen
  $iss.Variables.Add(
    [System.Management.Automation.Runspaces.SessionStateVariableEntry]::new('TOOL_CACHE', $ToolPaths, $null)
  )
  $iss.Variables.Add(
    [System.Management.Automation.Runspaces.SessionStateVariableEntry]::new('ARCHIVE_ENTRY_CACHE', @{}, $null)
  )
  $iss.Variables.Add(
    [System.Management.Automation.Runspaces.SessionStateVariableEntry]::new('_CancelEvent', $CancelEvent, $null)
  )
  $iss.Variables.Add(
    [System.Management.Automation.Runspaces.SessionStateVariableEntry]::new('_LogQueue', $LogQueue, $null)
  )

  if (Get-Command New-RunspacePoolShared -ErrorAction SilentlyContinue) {
    return (New-RunspacePoolShared -MinRunspaces 1 -MaxRunspaces $MaxParallel -InitialSessionState $iss -HostObject $Host)
  }

  $pool = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspacePool(1, $MaxParallel, $iss, $Host)
  $pool.Open()
  return $pool
}

function Invoke-ConvertBatchParallel {
  <# Fuehrt Konvertierungen parallel ueber einen RunspacePool aus.
     Gibt @{ ConvertCount; SkipCount; ErrorCount; TotalSaved; AuditRows } zurueck. #>
  param(
    [System.Collections.Generic.List[psobject]]$Candidates,
    [hashtable]$Tools,
    [int]$MaxParallel,
    [scriptblock]$Log,
    [scriptblock]$OnProgress,
    [System.Collections.Generic.HashSet[string]]$RetryableReasons,
    [int]$MaxRetries,
    [int]$RetryDelayMs,
    [string]$AuditCsvPath
  )

  # BUG-MV-03: Null-Guard fuer $Log
  if (-not $Log) { $Log = { } }

  $logQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
  $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
  $pool = $null
  $jobs = [System.Collections.Generic.List[hashtable]]::new()
  $maxNoProgressMs = 180000
  try {
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      $cfg = Get-AppStateValue -Key 'ConversionNoProgressTimeoutMs' -Default 180000
      if ($cfg -is [int] -or $cfg -is [long]) {
        $maxNoProgressMs = [int][math]::Max(30000, [int64]$cfg)
      }
    }
  } catch { }

  try {
    $pool = New-ConversionRunspacePool -MaxParallel $MaxParallel -ToolPaths $Tools -CancelEvent $cancelEvent -LogQueue $logQueue

    $workerScript = {
      param($Item, $FormatInfo, $ToolPath, $SevenZipPath, $RetryableReasons, $MaxRetries, $RetryDelayMs)

      # ISS-Variablen explizit in den script-Scope übernehmen
      $script:TOOL_CACHE = $TOOL_CACHE
      $script:ARCHIVE_ENTRY_CACHE = $ARCHIVE_ENTRY_CACHE
      $script:_CancelEvent = $_CancelEvent
      $script:_LogQueue = $_LogQueue

      $logCb = {
        param([string]$msg)
        if ($script:_LogQueue) { $script:_LogQueue.Enqueue($msg) }
      }

      $result = Invoke-ConvertItem -Item $Item -FormatInfo $FormatInfo -ToolPath $ToolPath -Log $logCb -SevenZipPath $SevenZipPath

      $result = Invoke-ConversionWithRetry -InitialResult $result -ConvertAction {
        Invoke-ConvertItem -Item $Item -FormatInfo $FormatInfo -ToolPath $ToolPath -Log $logCb -SevenZipPath $SevenZipPath
      } -RetryableReasons $RetryableReasons -MaxRetries $MaxRetries -RetryDelayMs $RetryDelayMs `
        -FileName ([IO.Path]::GetFileName($Item.MainPath)) -Log $logCb

      return $result
    }

    # Jobs starten
    foreach ($item in $Candidates) {
      $fmt = $item._TargetFormat
      $toolPath = if ($fmt.Tool -eq 'psxtract') { $Tools['psxtract'] } else { $Tools[$fmt.Tool] }

      $ps = [PowerShell]::Create()
      $ps.RunspacePool = $pool
      [void]$ps.AddScript($workerScript)
      [void]$ps.AddArgument($item)
      [void]$ps.AddArgument($fmt)
      [void]$ps.AddArgument($toolPath)
      [void]$ps.AddArgument($Tools['7z'])
      [void]$ps.AddArgument($RetryableReasons)
      [void]$ps.AddArgument($MaxRetries)
      [void]$ps.AddArgument($RetryDelayMs)

      $handle = $ps.BeginInvoke()
      [void]$jobs.Add(@{
        PS      = $ps
        Handle  = $handle
        Item    = $item
        Format  = $fmt
        Done    = $false
      })
    }

    # Poll-Loop: Log-Queue leeren, Progress, Cancel pruefen
    $convertCount = 0
    $skipCount    = 0
    $errorCount   = 0
    $totalSaved   = [long]0
    $completedCount = 0
    $auditRows    = [System.Collections.Generic.List[string]]::new()
    $lastProgress = [System.Diagnostics.Stopwatch]::StartNew()
    $maxLogDrainPerTick = 60
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      try {
        $cfgDrain = Get-AppStateValue -Key 'LogDrainBatchSize' -Default 60
        if ($cfgDrain -is [int] -or $cfgDrain -is [long]) {
          $maxLogDrainPerTick = [int][Math]::Max(10, [Math]::Min([int64]$cfgDrain, 500))
        }
      } catch { }
    }

    while ($completedCount -lt $jobs.Count) {
      # Cancel pruefen (Hauptthread liest AppState)
      $isCancelled = $false
      if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
        try { $isCancelled = [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false) } catch { }
      }
      if ($isCancelled) { $cancelEvent.Set() }

      # UI-Pump fuer Responsivitaet
      Invoke-UiPump

      # Log-Queue leeren
      $msg = $null
      $drained = 0
      while ($drained -lt $maxLogDrainPerTick -and $logQueue.TryDequeue([ref]$msg)) {
        & $Log $msg
        $lastProgress.Restart()
        $drained++
      }

      # Erledigte Jobs einsammeln
      for ($i = 0; $i -lt $jobs.Count; $i++) {
        $job = $jobs[$i]
        if ($job.Done) { continue }
        if (-not $job.Handle.IsCompleted) { continue }

        $job.Done = $true
        $completedCount++
        $outcome = $null

        try {
          $results = $job.PS.EndInvoke($job.Handle)
          $outcome = $results | Select-Object -Last 1
        } catch {
          $outcome = [pscustomobject]@{ Status = 'ERROR'; Path = $null; Reason = 'runspace-error'; Details = @{ Exception = $_.Exception.Message } }
        } finally {
          $job.PS.Dispose()
        }

        $auditOldSize = [long]$job.Item.SizeBytes
        $auditNewSize = [long]0
        $auditSaved   = [long]0

        switch ($outcome.Status) {
          'OK' {
            $convertCount++
            try { $auditNewSize = [long](Get-Item -LiteralPath $outcome.Path).Length } catch { }
            $auditSaved = [math]::Max([long]0, ($auditOldSize - $auditNewSize))
            $totalSaved += $auditSaved
          }
          'SKIP' { $skipCount++ }
          default {
            $errorCount++
            if (Get-Command Add-RunError -ErrorAction SilentlyContinue) {
              Add-RunError -Category 'Konvertierung' -Message (Format-ConversionErrorDetail -Outcome $outcome -MainPath $job.Item.MainPath)
            }
          }
        }

        # Audit-Zeile sammeln
        $csvLine = New-ConversionAuditRow -Status $outcome.Status -ToolName $job.Format.Tool `
          -MainPath $job.Item.MainPath -TargetExt $job.Format.Ext -OutputPath $outcome.Path `
          -Reason $outcome.Reason -OldSize $auditOldSize -NewSize $auditNewSize -Saved $auditSaved
        [void]$auditRows.Add($csvLine)
        if (-not [string]::IsNullOrWhiteSpace($AuditCsvPath)) {
          try { $csvLine | Out-File -LiteralPath $AuditCsvPath -Encoding UTF8 -Append } catch { }
        }

        if ($OnProgress) { & $OnProgress $completedCount $Candidates.Count $job.Item.MainPath }
        $lastProgress.Restart()

        # BUG CONV-006 FIX: Abort parallel batch if error rate exceeds 80% (systemic ISS failure)
        if ($completedCount -ge 3 -and $errorCount -gt ($completedCount * 0.8)) {
          throw ('Paralleler Modus: {0}/{1} Jobs fehlgeschlagen (>80%) — Fallback auf sequentiell.' -f $errorCount, $completedCount)
        }
      }

      if ($completedCount -lt $jobs.Count -and $lastProgress.ElapsedMilliseconds -ge $maxNoProgressMs) {
        & $Log ("WARN: Konvertierung ohne Fortschritt > {0} ms. Offene Worker werden beendet; Lauf geht weiter." -f $maxNoProgressMs)
        $cancelEvent.Set()

        foreach ($job in $jobs) {
          if ($job.Done) { continue }

          try { if ($job.PS) { $job.PS.Stop() } } catch { }
          try { if ($job.PS) { $job.PS.Dispose() } } catch { }
          $job.Done = $true
          $completedCount++
          $errorCount++

          if (Get-Command Add-RunError -ErrorAction SilentlyContinue) {
            Add-RunError -Category 'Konvertierung' -Message ('{0} (conversion-timeout-no-progress)' -f $job.Item.MainPath)
          }

          $csvLine = New-ConversionAuditRow -Status 'ERROR' -ToolName $job.Format.Tool `
            -MainPath $job.Item.MainPath -TargetExt $job.Format.Ext -OutputPath '' `
            -Reason 'conversion-timeout-no-progress' -OldSize ([long]$job.Item.SizeBytes) -NewSize 0 -Saved 0
          [void]$auditRows.Add($csvLine)
          if (-not [string]::IsNullOrWhiteSpace($AuditCsvPath)) {
            try { $csvLine | Out-File -LiteralPath $AuditCsvPath -Encoding UTF8 -Append } catch { }
          }

          if ($OnProgress) { & $OnProgress $completedCount $Candidates.Count $job.Item.MainPath }
        }

        break
      }

      Start-Sleep -Milliseconds 120
    }

    # Restliche Log-Nachrichten leeren
    $msg = $null
    while ($logQueue.TryDequeue([ref]$msg)) { & $Log $msg }

    return @{
      ConvertCount = $convertCount
      SkipCount    = $skipCount
      ErrorCount   = $errorCount
      TotalSaved   = $totalSaved
      AuditRows    = $auditRows
    }
  } finally {
    Stop-RunspaceWorkerJobsShared -Jobs $jobs
    Remove-RunspacePoolResourcesShared -Pool $pool -CancelEvent $cancelEvent
  }
}

function Invoke-FormatConversion {
  param(
    [System.Collections.Generic.List[psobject]]$Winners,
    [string[]]$Roots,
    [scriptblock]$Log,
    [hashtable]$ToolOverrides,
    [scriptblock]$OnProgress,
    [int]$MaxParallel = 0
  )

  # BUG-MV-03: Null-Guard fuer $Log
  if (-not $Log) { $Log = { } }

  & $Log ''
  & $Log '=== Format-Konvertierung ==='

  $tools = @{}
  foreach ($toolName in @('chdman', 'dolphintool', '7z', 'psxtract', 'ciso')) {
    $path = $null
    if ($script:TOOL_CACHE.ContainsKey($toolName)) {
      $cached = [string]$script:TOOL_CACHE[$toolName]
      if ($cached -and (Test-Path -LiteralPath $cached)) { $path = $cached } else { $script:TOOL_CACHE.Remove($toolName) }
    }
    if ($ToolOverrides -and $ToolOverrides.ContainsKey($toolName)) {
      $overridePath = [string]$ToolOverrides[$toolName]
      if (-not [string]::IsNullOrWhiteSpace($overridePath) -and (Test-Path -LiteralPath $overridePath)) { $path = $overridePath }
    }
    if (-not $path) { $path = Find-ConversionTool -ToolName $toolName }
    if ($path) { $tools[$toolName] = $path; $script:TOOL_CACHE[$toolName] = $path }
  }

  if ($tools.Count -eq 0) {
    & $Log 'Keine Konvertierungstools gefunden. Ueberspringe.'
    return
  }

  $candidates = New-Object System.Collections.Generic.List[psobject]
  foreach ($item in $Winners) {
    $ext = [IO.Path]::GetExtension($item.MainPath).ToLowerInvariant()
    $console = Get-ConsoleType -RootPath $item.Root -FilePath $item.MainPath -Extension $ext
    $fmt = Get-TargetFormat -ConsoleType $console -Extension $ext
    if (-not $fmt) { continue }
    if ($fmt.Tool -eq 'psxtract') {
      if (-not ($tools.ContainsKey('psxtract') -and $tools.ContainsKey('chdman'))) { continue }
    } elseif (-not $tools.ContainsKey($fmt.Tool)) {
      continue
    }
    if ($ext -eq $fmt.Ext) { continue }
    $item | Add-Member -NotePropertyName '_TargetFormat' -NotePropertyValue $fmt -Force
    [void]$candidates.Add($item)
  }

  if ($candidates.Count -eq 0) {
    & $Log 'Keine konvertierbaren Winner gefunden.'
    if ($OnProgress) { & $OnProgress 0 0 $null }
    return
  }

  if ($OnProgress) { & $OnProgress 0 $candidates.Count $null }

  # --- Conversion Audit Trail ---
  $auditCsvPath = $null
  try {
    $reportsDir = Join-Path (Get-Location).Path 'reports'
    Assert-DirectoryExists -Path $reportsDir
    $ts = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $auditCsvPath = Join-Path $reportsDir "convert-audit-$ts.csv"
    '"Timestamp","Status","Tool","SourceExt","TargetExt","SourcePath","TargetPath","Reason","OldSizeBytes","NewSizeBytes","SavedBytes"' |
      Out-File -LiteralPath $auditCsvPath -Encoding UTF8 -Force
  } catch { $auditCsvPath = $null }

  # Retryable error reasons (transiente Tool-Fehler)
  $retryableReasons = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($rr in @('chdman-failed','dolphintool-failed','7z-failed','psxtract-failed',
                     'archive-expand-error','ciso-failed','output-missing-or-empty')) {
    [void]$retryableReasons.Add($rr)
  }
  $maxRetries = 2
  $retryDelayMs = 1500

  # MaxParallel bestimmen: 0 = Auto (min(4, ProcessorCount)), 1 = sequentiell
  if ($MaxParallel -le 0) {
    $MaxParallel = [Math]::Min(4, [Math]::Max(2, [Environment]::ProcessorCount))
  }

  $convertCount = 0
  $skipCount = 0
  $errorCount = 0
  $totalSaved = [long]0

  $useParallel = ($MaxParallel -gt 1 -and $candidates.Count -gt 1)

  if ($useParallel) {
    & $Log ("Parallele Konvertierung: {0} Worker, {1} Kandidaten" -f $MaxParallel, $candidates.Count)
    try {
      $batchResult = Invoke-ConvertBatchParallel -Candidates $candidates -Tools $tools `
        -MaxParallel $MaxParallel -Log $Log -OnProgress $OnProgress `
        -RetryableReasons $retryableReasons -MaxRetries $maxRetries -RetryDelayMs $retryDelayMs `
        -AuditCsvPath $auditCsvPath

      $convertCount = $batchResult.ConvertCount
      $skipCount    = $batchResult.SkipCount
      $errorCount   = $batchResult.ErrorCount
      $totalSaved   = $batchResult.TotalSaved

      # Audit-Zeilen werden im Parallelpfad bereits inkrementell geschrieben.
    } catch {
      & $Log ("Paralleler Modus fehlgeschlagen, Fallback auf sequentiell: {0}" -f $_.Exception.Message)
      $useParallel = $false
    }
  }

  if (-not $useParallel) {
    $index = 0
    foreach ($item in $candidates) {
      Test-CancelRequested
      $index++
      $fmt = $item._TargetFormat
      $toolPath = if ($fmt.Tool -eq 'psxtract') { $tools['psxtract'] } else { $tools[$fmt.Tool] }

      $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $toolPath -Log $Log -SevenZipPath $tools['7z']

      $result = Invoke-ConversionWithRetry -InitialResult $result -ConvertAction {
        Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $toolPath -Log $Log -SevenZipPath $tools['7z']
      } -RetryableReasons $retryableReasons -MaxRetries $maxRetries -RetryDelayMs $retryDelayMs `
        -FileName ([IO.Path]::GetFileName($item.MainPath)) -Log $Log

      $auditOldSize = [long]$item.SizeBytes
      $auditNewSize = [long]0
      $auditSaved   = [long]0

      switch ($result.Status) {
        'OK' {
          $convertCount++
          $auditNewSize = [long](Get-Item -LiteralPath $result.Path).Length
          $auditSaved = [math]::Max([long]0, ($auditOldSize - $auditNewSize))
          $totalSaved += $auditSaved
        }
        'SKIP' { $skipCount++ }
        default {
          $errorCount++
          if (Get-Command Add-RunError -ErrorAction SilentlyContinue) {
            Add-RunError -Category 'Konvertierung' -Message (Format-ConversionErrorDetail -Outcome $result -MainPath $item.MainPath)
          }
        }
      }

      # Write audit row
      if ($auditCsvPath) {
        try {
          $csvLine = New-ConversionAuditRow -Status $result.Status -ToolName $fmt.Tool `
            -MainPath $item.MainPath -TargetExt $fmt.Ext -OutputPath $result.Path `
            -Reason $result.Reason -OldSize $auditOldSize -NewSize $auditNewSize -Saved $auditSaved
          $csvLine | Out-File -LiteralPath $auditCsvPath -Encoding UTF8 -Append
        } catch {
          Write-Warning ('Conversion-Audit-CSV schreiben fehlgeschlagen ({0}): {1}' -f $auditCsvPath, $_.Exception.Message)
        }
      }

      if ($OnProgress) { & $OnProgress $index $candidates.Count $item.MainPath }
    }
  }

  if ($OnProgress) { & $OnProgress $candidates.Count $candidates.Count $null }

  & $Log ''
  & $Log '=== Konvertierung Ergebnis ==='
  & $Log ("Konvertiert: {0}" -f $convertCount)
  & $Log ("Übersprungen: {0}" -f $skipCount)
  & $Log ("Fehler: {0}" -f $errorCount)
  & $Log ("Gespart: {0:N1} MB" -f ($totalSaved / 1MB))
  if ($MaxParallel -gt 1) {
    & $Log ("Modus: parallel ({0} Worker)" -f $MaxParallel)
  }
  if ($auditCsvPath -and (Test-Path -LiteralPath $auditCsvPath)) {
    & $Log ("Audit-Trail: {0}" -f $auditCsvPath)
  }
}
