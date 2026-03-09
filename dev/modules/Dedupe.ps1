function Invoke-ClassifyFile {
  <# Shared classification kernel for a single file/set entry.
     Returns a result object with: Category, Region, Console, IsCorrupt,
     GameKey, DatMatched, VersionScore, CrcErrorPath, CrcErrorMessage.
     Returns $null when the file is filtered out by ConsoleFilterKeys. #>
  param(
    [string]$BaseName = '',
    [string]$FilePath = '',
    [string]$Extension = '',
    [string]$Root,
    [string[]]$CrcPaths,
    [string[]]$GameKeyPaths,
    [bool]$AggressiveJunk,
    [bool]$CrcVerifyScan,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [hashtable]$HashCache,
    [string]$DatHashType,
    [bool]$DatFallback,
    [bool]$AliasEditionKeying,
    [string]$SevenZipPath,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash,
    [hashtable]$PreferredRegionKeys,
    [bool]$JpOnlyForSelectedConsoles,
    [hashtable]$JpKeepKeys,
    [hashtable]$ParentCloneParentMap,
    [hashtable]$ConsoleFilterKeys
  )

  if ([string]::IsNullOrWhiteSpace($BaseName)) {
    $BaseName = if ($FilePath) { [IO.Path]::GetFileNameWithoutExtension($FilePath) } else { '__unknown__' }
  }
  if ([string]::IsNullOrWhiteSpace($BaseName)) { $BaseName = '__unknown__' }

  $category  = Get-FileCategory -BaseName $BaseName -AggressiveJunk $AggressiveJunk
  $region    = Get-RegionTag -Name $BaseName
  $console   = Get-ConsoleType -RootPath $Root -FilePath $FilePath -Extension $Extension
  $isCorrupt = $false
  $crcErrorPath    = $null
  $crcErrorMessage = $null

  # Optional console filter - return $null when file should be skipped
  if ($ConsoleFilterKeys -and $ConsoleFilterKeys.Count -gt 0) {
    if ([string]::IsNullOrWhiteSpace($console)) { return $null }
    $norm = $console.Trim().ToUpperInvariant()
    $mapped = Get-DatConsoleKey -Name $console
    if ($mapped) { $norm = [string]$mapped }
    if (-not $ConsoleFilterKeys.ContainsKey($norm)) { return $null }
  }

  if ($CrcVerifyScan -and $CrcPaths) {
    foreach ($crcPath in $CrcPaths) {
      try { [void](Get-Crc32Hash -Path $crcPath) }
      catch {
        $category = 'JUNK'
        $isCorrupt = $true
        $crcErrorPath    = $crcPath
        $crcErrorMessage = $_.Exception.Message
        break
      }
    }
  }

  # Region keep policy (data-driven)
  if ($category -eq 'GAME' -and -not [string]::IsNullOrWhiteSpace($region) -and $region -ne 'UNKNOWN') {
    if ($PreferredRegionKeys -and -not $PreferredRegionKeys.ContainsKey($region)) {
      if ($region -eq 'JP') {
        if ($JpOnlyForSelectedConsoles -and $JpKeepKeys -and -not ([string]::IsNullOrWhiteSpace($console) -or $console -eq 'UNKNOWN') -and -not $JpKeepKeys.ContainsKey($console)) {
          $category = 'JUNK'
        }
      } else {
        $category = 'JUNK'
      }
    }
  }

  $keyInfo = Resolve-GameKey -BaseName $BaseName -Paths $GameKeyPaths -Root $Root `
    -MainPath $FilePath -UseDat $UseDat -DatIndex $DatIndex -HashCache $HashCache `
    -HashType $DatHashType -DatFallback $DatFallback `
    -AliasEditionKeying $AliasEditionKeying -SevenZipPath $SevenZipPath `
    -Log $Log -OnDatHash $OnDatHash

  $key = $keyInfo.Key
  if ($ParentCloneParentMap -and $ParentCloneParentMap.Count -gt 0) {
    $key = Resolve-ParentName -GameName $key -ConsoleKey $console -ParentMap $ParentCloneParentMap
  }

  $verScore = Get-VersionScore -BaseName $BaseName

  return [pscustomobject]@{
    Category        = $category
    Region          = $region
    Console         = $console
    IsCorrupt       = $isCorrupt
    GameKey         = $key
    DatMatched      = $keyInfo.DatMatched
    VersionScore    = $verScore
    CrcErrorPath    = $crcErrorPath
    CrcErrorMessage = $crcErrorMessage
  }
}

function Initialize-RegionDedupeContext {
  <# Builds the shared context object for Invoke-RegionDedupe.
     Initialises caches, filter sets, DAT index, tool paths, and
     extension sets.  Returns a [pscustomobject] consumed by
     Invoke-RegionDedupeScanRoot and the orchestrator. #>
  param(
    [string[]]$Roots,
    [string[]]$PreferOrder,
    [string[]]$IncludeExtensions,
    [bool]$RemoveJunk = $true,
    [bool]$AggressiveJunk = $false,
    [bool]$AliasEditionKeying = $false,
    [string[]]$ConsoleFilter = @(),
    [bool]$JpOnlyForSelectedConsoles = $false,
    [string[]]$JpKeepConsoles = @(),
    [bool]$UseDat = $false,
    [string]$DatRoot,
    [ValidateSet('SHA1','MD5','CRC32')][string]$DatHashType = 'SHA1',
    [bool]$CrcVerifyScan = $false,
    [bool]$DatFallback = $true,
    [bool]$Use1G1R = $false,
    [hashtable]$DatMap,
    [hashtable]$ToolOverrides,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash,
    [string[]]$ExcludedCandidatePaths = @()
  )

  $allItems   = New-Object System.Collections.Generic.List[psobject]
  $junkItems  = New-Object System.Collections.Generic.List[psobject]
  $biosItems  = New-Object System.Collections.Generic.List[psobject]
  Reset-ArchiveEntryCache
  Reset-ClassificationCaches

  $jpKeepSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  if ($JpOnlyForSelectedConsoles -and $JpKeepConsoles) {
    foreach ($entry in $JpKeepConsoles) {
      if ([string]::IsNullOrWhiteSpace($entry)) { continue }
      $normalized = $entry.Trim().ToUpperInvariant()
      $mapped = Get-DatConsoleKey -Name $entry
      if ($mapped) { $normalized = [string]$mapped }
      [void]$jpKeepSet.Add($normalized)
    }
  }
  $preferredRegionKeepSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  # Build keep-set from user's preferred regions (BUG-CS-01 fix: was hardcoded EU/US/WORLD)
  [void]$preferredRegionKeepSet.Add('WORLD')
  if ($PreferOrder -and $PreferOrder.Count -gt 0) {
    foreach ($regionKey in $PreferOrder) {
      if (-not [string]::IsNullOrWhiteSpace($regionKey)) {
        [void]$preferredRegionKeepSet.Add($regionKey)
      }
    }
  } else {
    # Fallback when no user preference exists
    [void]$preferredRegionKeepSet.Add('EU')
    [void]$preferredRegionKeepSet.Add('US')
  }

  $consoleFilterSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $excludedCandidateSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($excludedPath in @($ExcludedCandidatePaths)) {
    if ([string]::IsNullOrWhiteSpace([string]$excludedPath)) { continue }
    [void]$excludedCandidateSet.Add([string]$excludedPath)
  }
  if ($ConsoleFilter) {
    foreach ($entry in $ConsoleFilter) {
      if ([string]::IsNullOrWhiteSpace($entry)) { continue }
      $normalized = $entry.Trim().ToUpperInvariant()
      $mapped = Get-DatConsoleKey -Name $entry
      if ($mapped) { $normalized = [string]$mapped }
      [void]$consoleFilterSet.Add($normalized)
    }
  }
  $includeConsole = {
    param([string]$Console)
    if ($consoleFilterSet.Count -eq 0) { return $true }
    if ([string]::IsNullOrWhiteSpace($Console)) { return $false }
    $normalized = $Console.Trim().ToUpperInvariant()
    $mapped = Get-DatConsoleKey -Name $Console
    if ($mapped) { $normalized = [string]$mapped }
    return $consoleFilterSet.Contains($normalized)
  }.GetNewClosure()

  $applyRegionKeepPolicy = {
    param(
      [string]$CurrentCategory,
      [string]$Region,
      [string]$Console
    )

    if ($CurrentCategory -ne 'GAME') { return $CurrentCategory }
    if ([string]::IsNullOrWhiteSpace($Region) -or $Region -eq 'UNKNOWN') { return $CurrentCategory }
    if ($preferredRegionKeepSet.Contains($Region)) { return $CurrentCategory }

    if ($Region -eq 'JP') {
      if ($JpOnlyForSelectedConsoles -and -not ([string]::IsNullOrWhiteSpace($Console) -or $Console -eq 'UNKNOWN') -and -not $jpKeepSet.Contains($Console)) {
        return 'JUNK'
      }
      return $CurrentCategory
    }

    return 'JUNK'
  }.GetNewClosure()

  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $previousAggressiveJunk = [bool](Get-AppStateValue -Key 'UseAggressiveJunk' -Default $false)
  [void](Set-AppStateValue -Key 'UseAggressiveJunk' -Value $AggressiveJunk)

  if (Get-Command Clear-RomCleanupConvertedBackupArtifacts -ErrorAction SilentlyContinue) {
    try {
      $backupRetentionDays = 7
      if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
        $configuredRetention = Get-AppStateValue -Key 'ConversionBackupRetentionDays' -Default $backupRetentionDays
        if ($configuredRetention -is [int] -or $configuredRetention -is [long]) {
          $backupRetentionDays = [int][Math]::Max(1, [int64]$configuredRetention)
        }
      }
      Clear-RomCleanupConvertedBackupArtifacts -Roots @($Roots) -OlderThanDays $backupRetentionDays -Log $Log
    } catch { }
  }

  $datIndex = @{}
  $hashCache = $null
  if (Get-Command Reset-DatArchiveStats -ErrorAction SilentlyContinue) { Reset-DatArchiveStats }
  $datHashTotal = 0
  $datHashCount = 0
  $datHashSkipExts = @('.m3u', '.cue', '.gdi', '.ccd', '.mds')
  $datHashUpdate = $null
  if ($UseDat -and $OnDatHash) {
    $progressCallback = $OnDatHash
    # BUG DEDUPE-001 FIX: Use mutable single-element arrays so .GetNewClosure()
    # captures a reference that stays in sync with the actual values.
    $datHashCountRef = @(0)
    $datHashTotalRef = @(0)
    $datHashUpdate = {
      param([string]$path)
      $datHashCountRef[0]++
      & $progressCallback $datHashCountRef[0] $datHashTotalRef[0] $path
    }.GetNewClosure()
  }
  if ($UseDat -and -not [string]::IsNullOrWhiteSpace($DatRoot)) {
    & $Log ('DAT: lade aus {0} (Hash {1})' -f $DatRoot, $DatHashType)
    $datIndex = Get-DatIndex -DatRoot $DatRoot -HashType $DatHashType -ConsoleMap $DatMap -Log $Log
    $hashCache = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  } elseif ($UseDat) {
    & $Log 'DAT: kein Ordner gesetzt, DAT deaktiviert.'
  }

  $parentCloneIndex = $null
  if ($Use1G1R -and $UseDat -and -not [string]::IsNullOrWhiteSpace($DatRoot)) {
    & $Log '1G1R: lade Parent/Clone-Index ...'
    $parentCloneIndex = Get-DatParentCloneIndex -DatRoot $DatRoot -ConsoleMap $DatMap -Log $Log
    if ($parentCloneIndex -and $parentCloneIndex.ParentMap) {
      & $Log ('1G1R: {0} Eintraege im ParentMap geladen.' -f $parentCloneIndex.ParentMap.Count)
    } else {
      & $Log '1G1R: kein Parent/Clone-Index gefunden, 1G1R deaktiviert.'
      $parentCloneIndex = $null
    }
  }

  $sevenZipPath = $null
  if ($UseDat) {
    if ($ToolOverrides -and $ToolOverrides.ContainsKey('7z')) {
      $sevenZipPath = [string]$ToolOverrides['7z']
      if ([string]::IsNullOrWhiteSpace($sevenZipPath)) { $sevenZipPath = $null }
    }
    if (-not $sevenZipPath) {
      $sevenZipPath = Find-ConversionTool -ToolName '7z'
    }
  }

  $setExts = @('.m3u', '.cue', '.gdi', '.ccd', '.mds')
  $extSet = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  foreach ($e in $IncludeExtensions) {
    if (-not [string]::IsNullOrWhiteSpace([string]$e)) { [void]$extSet.Add([string]$e) }
  }
  $useExtFilter = ($extSet.Count -gt 0)
  if (-not $useExtFilter) {
    & $Log 'Kein Dateitypfilter gesetzt – alle Dateitypen werden gescannt.'
  }
  $setExtSet = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  foreach ($e in $setExts) { [void]$setExtSet.Add($e) }
  $allScanExtSet = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  foreach ($e in $extSet) { [void]$allScanExtSet.Add($e) }
  foreach ($e in $setExtSet) { [void]$allScanExtSet.Add($e) }
  # Set-Payload-Extensions direkt in allScanExtSet mergen (DEAD-03: $setPayloadExtSet eliminiert)
  foreach ($e in @('.bin','.iso','.img','.sub','.raw','.wav','.ape','.flac','.ogg','.mp3','.mp2','.pcm','.ecm','.mdf','.mds')) {
    [void]$allScanExtSet.Add($e)
  }

  if ($UseDat -and ($extSet.Contains('.zip') -or $extSet.Contains('.7z'))) {
    & $Log 'DAT: Hinweis: Hashing von ZIP/7Z entspricht oft nicht No-Intro ROM-Hashes.'
  }

  $addItem = {
    param([pscustomobject]$item)
    switch ($item.Category) {
      'JUNK' { if ($RemoveJunk) { [void]$junkItems.Add($item) } else { [void]$allItems.Add($item) } }
      'BIOS' { [void]$biosItems.Add($item) }
      default { [void]$allItems.Add($item) }
    }
  }.GetNewClosure()

  return [pscustomobject]@{
    AllItems               = $allItems
    JunkItems              = $junkItems
    BiosItems              = $biosItems
    JpKeepSet              = $jpKeepSet
    PreferredRegionKeepSet = $preferredRegionKeepSet
    ConsoleFilterSet       = $consoleFilterSet
    ExcludedCandidateSet   = $excludedCandidateSet
    IncludeConsole         = $includeConsole
    ApplyRegionKeepPolicy  = $applyRegionKeepPolicy
    DatIndex               = $datIndex
    HashCache              = $hashCache
    ParentCloneIndex       = $parentCloneIndex
    SevenZipPath           = $sevenZipPath
    ExtSet                 = $extSet
    SetExtSet              = $setExtSet
    AllScanExtSet          = $allScanExtSet
    UseExtFilter           = $useExtFilter
    AddItem                = $addItem
    DatHashUpdate          = $datHashUpdate
    DatHashTotalRef        = if ($datHashUpdate) { $datHashTotalRef } else { @(0) }
    DatHashCountRef        = if ($datHashUpdate) { $datHashCountRef } else { @(0) }
    DatHashTotal           = $datHashTotal
    # BUG DEDUPE-002 FIX: Removed stale DatHashCount scalar — use DatHashCountRef[0] instead
    DatHashSkipExts        = $datHashSkipExts
    Stopwatch              = $sw
    PreviousAggressiveJunk = $previousAggressiveJunk
    PreferOrder            = $PreferOrder
    AggressiveJunk         = $AggressiveJunk
    AliasEditionKeying     = $AliasEditionKeying
    UseDat                 = $UseDat
    DatHashType            = $DatHashType
    DatFallback            = $DatFallback
    CrcVerifyScan          = $CrcVerifyScan
    JpOnlyForSelectedConsoles = $JpOnlyForSelectedConsoles
  }
}

function Invoke-RegionDedupeScanRoot {
  <# Scans a single root directory, classifies files, and populates
     the context's AllItems/JunkItems/BiosItems lists.
     Called once per root by Invoke-RegionDedupe. #>
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Context,
    [Parameter(Mandatory=$true)][string]$Root,
    [int]$RootIndex,
    [int]$RootTotal,
    [string]$TrashRoot,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash
  )

  if (-not (Test-Path -LiteralPath $Root)) {
    & $Log "WARNUNG: Root nicht gefunden: $Root"
    return
  }

  & $Log ('Scan [{0}/{1}]: {2}' -f $RootIndex, $RootTotal, $Root)

  $excludePaths = @(
    (Join-Path $Root '_TRASH_REGION_DEDUPE') + '\'
    (Join-Path $Root '_BIOS') + '\'
    (Join-Path $Root '_JUNK') + '\'
  )
  if (-not [string]::IsNullOrWhiteSpace($TrashRoot)) {
    $normalTrash = Resolve-RootPath -Path $TrashRoot -WithTrailingSlash
    $rootWithSlash = Resolve-RootPath -Path $Root -WithTrailingSlash
    if ($normalTrash -and $rootWithSlash -and $normalTrash.StartsWith($rootWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
      $excludePaths += $normalTrash
    }
  }
  $scanExtensions = if ($Context.UseExtFilter) { $Context.AllScanExtSet } else { $null }
  $allFiles = @(Get-FilesSafe -Root $Root -ExcludePrefixes $excludePaths -AllowedExtensions $scanExtensions -Responsive)

  $fileInfoByPath = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
  $filesByExt = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $datHashCandidatesInRoot = 0
  foreach ($f in $allFiles) {
    if ($Context.ExcludedCandidateSet.Contains([string]$f.FullName)) { continue }
    $fileInfoByPath[$f.FullName] = $f
    if ($Context.UseDat -and $OnDatHash -and ($Context.DatHashSkipExts -notcontains $f.Extension.ToLowerInvariant())) {
      $datHashCandidatesInRoot++
    }
    $ext = $f.Extension
    if ($Context.UseExtFilter -and -not ($Context.ExtSet.Contains($ext) -or $Context.SetExtSet.Contains($ext))) { continue }
    [void]$files.Add($f)
    if (-not $filesByExt.ContainsKey($ext)) {
      $filesByExt[$ext] = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    }
    [void]$filesByExt[$ext].Add($f)
  }

  if ($Context.UseDat -and $OnDatHash) {
    $Context.DatHashTotal += $datHashCandidatesInRoot
    # BUG DEDUPE-001 FIX: Keep the mutable ref arrays in sync for the closure
    if ($Context.DatHashTotalRef) { $Context.DatHashTotalRef[0] = $Context.DatHashTotal }
    & $OnDatHash $Context.DatHashCountRef[0] $Context.DatHashTotal $null
  }

  & $Log ('  -> {0} Dateien gefunden (inkl. Set-Steuerdateien)' -f $files.Count)
  if ($Context.UseExtFilter -and $allFiles.Count -gt $files.Count) {
    & $Log ('     (Gesamt im Ordner vor Dateitypfilter: {0})' -f $allFiles.Count)
  }

  # PERF-08: Pre-hash all DAT-hashable files in parallel before classification
  if ($Context.UseDat -and $Context.HashCache) {
    $enablePreHash = $false
    try { $enablePreHash = [bool](Get-AppStateValue -Key 'EnableDatPreHash' -Default $false) } catch { }
    try {
      if (-not $enablePreHash -and $env:ROMCLEANUP_ENABLE_PREHASH -eq '1') { $enablePreHash = $true }
    } catch { }

    if ($enablePreHash) {
      $preHash = Invoke-PreHashFiles -Files $allFiles -HashType $Context.DatHashType -SevenZipPath $Context.SevenZipPath -Log $Log
      if ($preHash) {
        foreach ($kv in $preHash.GetEnumerator()) { $Context.HashCache[$kv.Key] = $kv.Value }
      }
    } else {
      & $Log '  Pre-Hash: deaktiviert (Stabilitätsmodus). DAT-Hashes on-demand.'
    }
  }

  $setMemberPaths = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)

  # Convert HashSets to hashtables (required for parallel serialization)
  $consoleFilterHT = @{}
  foreach ($k in $Context.ConsoleFilterSet) { $consoleFilterHT[$k] = $true }
  $preferredRegionHT = @{}
  foreach ($k in $Context.PreferredRegionKeepSet) { $preferredRegionHT[$k] = $true }
  $jpKeepHT = @{}
  foreach ($k in $Context.JpKeepSet) { $jpKeepHT[$k] = $true }
  $parentMap = if ($Context.ParentCloneIndex -and $Context.ParentCloneIndex.ParentMap) { $Context.ParentCloneIndex.ParentMap } else { @{} }

  # Data-driven set scan: M3U -> CUE -> GDI -> CCD
  $setFormats = @(
    @{ Ext = '.m3u'; Type = 'M3USET'
       GetRelated = { param($path,$rootPath) Get-M3URelatedFiles -M3UPath $path -RootPath $rootPath }
       GetMissing = { param($path,$rootPath) Get-M3UMissingFiles -M3UPath $path -RootPath $rootPath } }
    @{ Ext = '.cue'; Type = 'CUESET'
       GetRelated = { param($path,$rootPath) Get-CueRelatedFiles -CuePath $path -RootPath $rootPath }
       GetMissing = { param($path,$rootPath) Get-CueMissingFiles -CuePath $path -RootPath $rootPath } }
    @{ Ext = '.gdi'; Type = 'GDISET'
       GetRelated = { param($path,$rootPath) Get-GdiRelatedFiles -GdiPath $path -RootPath $rootPath }
       GetMissing = { param($path,$rootPath) Get-GdiMissingFiles -GdiPath $path -RootPath $rootPath } }
    @{ Ext = '.ccd'; Type = 'CCDSET'
       GetRelated = { param($path,$rootPath) Get-CcdRelatedFiles -CcdPath $path -RootPath $rootPath }
       GetMissing = { param($path,$rootPath) Get-CcdMissingFiles -CcdPath $path -RootPath $rootPath } }
    @{ Ext = '.mds'; Type = 'MDSSET'
       GetRelated = { param($path,$rootPath) Get-MdsRelatedFiles -MdsPath $path -RootPath $rootPath }
       GetMissing = { param($path,$rootPath) Get-MdsMissingFiles -MdsPath $path -RootPath $rootPath } }
  )
  foreach ($fmt in $setFormats) {
    # BUG DEDUPE-004 FIX: Use List instead of @() += to avoid O(n^2) array append
    $setFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    if ($filesByExt.ContainsKey($fmt.Ext)) {
      foreach ($f in $filesByExt[$fmt.Ext]) {
        if (-not $setMemberPaths.Contains($f.FullName)) { [void]$setFiles.Add($f) }
      }
    }
    Add-SetItemsFromFiles -Root $Root -Files $setFiles -Type $fmt.Type -PreferOrder $Context.PreferOrder `
      -AggressiveJunk $Context.AggressiveJunk -UseDat $Context.UseDat -DatIndex $Context.DatIndex -HashCache $Context.HashCache `
      -DatHashType $Context.DatHashType -DatFallback $Context.DatFallback -AliasEditionKeying $Context.AliasEditionKeying `
      -CrcVerifyScan $Context.CrcVerifyScan `
      -SevenZipPath $Context.SevenZipPath -Log $Log -OnDatHash $Context.DatHashUpdate -FileInfoByPath $fileInfoByPath `
      -SetMemberPaths $setMemberPaths `
      -PreferredRegionKeys $preferredRegionHT -JpOnlyForSelectedConsoles $Context.JpOnlyForSelectedConsoles -JpKeepKeys $jpKeepHT `
      -ConsoleFilterKeys $consoleFilterHT -AddItem $Context.AddItem `
      -GetRelated $fmt.GetRelated -GetMissing $fmt.GetMissing `
      -ParentCloneParentMap $parentMap
  }

  $dosFolderAdded = Add-MsDosFolderItems -Root $Root -AllFiles $allFiles -PreferOrder $Context.PreferOrder `
    -AggressiveJunk $Context.AggressiveJunk -UseDat $Context.UseDat -DatIndex $Context.DatIndex -HashCache $Context.HashCache `
    -DatHashType $Context.DatHashType -DatFallback $Context.DatFallback -AliasEditionKeying $Context.AliasEditionKeying `
    -SevenZipPath $Context.SevenZipPath -Log $Log -OnDatHash $Context.DatHashUpdate -FileInfoByPath $fileInfoByPath `
    -SetMemberPaths $setMemberPaths -ApplyRegionKeepPolicy $Context.ApplyRegionKeepPolicy -AddItem $Context.AddItem `
    -ShouldIncludeConsole $Context.IncludeConsole -ParentCloneIndex $Context.ParentCloneIndex
  if ($dosFolderAdded -gt 0) {
    & $Log ('  -> {0} DOS-Ordnerkandidat(en) als Spiele erfasst' -f $dosFolderAdded)
  }

  $singleIndex = 0

  # Pre-filter files for classification (exclude set-exts and set-member paths)
  $classifyFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
  foreach ($f in $files) {
    if ($Context.UseExtFilter -and -not $Context.ExtSet.Contains($f.Extension)) { continue }
    if ($Context.SetExtSet.Contains($f.Extension)) { continue }
    if ($setMemberPaths.Contains($f.FullName)) { continue }
    [void]$classifyFiles.Add($f)
  }

  # Decide: parallel or sequential classification
  $parallelClassifyThreshold = Get-ParallelClassifyThreshold
  $useParallel = ($classifyFiles.Count -ge $parallelClassifyThreshold)

  if ($useParallel) {
    & $Log ("  {0} Dateien zur Klassifizierung (parallel, Schwelle={1})" -f $classifyFiles.Count, $parallelClassifyThreshold)

    $parallelResult = Invoke-ClassifyFilesParallel `
      -Files $classifyFiles -Root $Root -PreferOrder $Context.PreferOrder `
      -AggressiveJunk $Context.AggressiveJunk -UseDat $Context.UseDat -DatIndex $Context.DatIndex `
      -DatHashType $Context.DatHashType -DatFallback $Context.DatFallback `
      -AliasEditionKeying $Context.AliasEditionKeying -CrcVerifyScan $Context.CrcVerifyScan `
      -SevenZipPath $Context.SevenZipPath -ParentCloneParentMap $parentMap `
      -ConsoleFilterKeys $consoleFilterHT -PreferredRegionKeys $preferredRegionHT `
      -JpOnlyForSelectedConsoles $Context.JpOnlyForSelectedConsoles -JpKeepKeys $jpKeepHT `
      -FileInfoByPath $fileInfoByPath -Log $Log -HashCache $Context.HashCache

    $Context.CrcVerifyCorruptCount += $parallelResult.CorruptCount
    foreach ($item in $parallelResult.Items) {
      & $Context.AddItem $item
    }
  } else {
    foreach ($f in $classifyFiles) {
      Test-CancelRequested
      $singleIndex++
      if (($singleIndex -eq 1) -or ($singleIndex -eq $classifyFiles.Count) -or ($singleIndex % 250 -eq 0)) {
        $pct = if ($classifyFiles.Count -gt 0) { [math]::Round(($singleIndex / $classifyFiles.Count) * 100, 1) } else { 100 }
        & $Log ('  Scan-Fortschritt: {0}/{1} ({2}%)' -f $singleIndex, $classifyFiles.Count, $pct)
      }

      $clf = Invoke-ClassifyFile -BaseName $f.BaseName -FilePath $f.FullName -Extension $f.Extension `
        -Root $Root -CrcPaths @($f.FullName) -GameKeyPaths @($f.FullName) `
        -AggressiveJunk $Context.AggressiveJunk -CrcVerifyScan $Context.CrcVerifyScan `
        -UseDat $Context.UseDat -DatIndex $Context.DatIndex -HashCache $Context.HashCache `
        -DatHashType $Context.DatHashType -DatFallback $Context.DatFallback `
        -AliasEditionKeying $Context.AliasEditionKeying -SevenZipPath $Context.SevenZipPath `
        -Log $Log -OnDatHash $Context.DatHashUpdate `
        -PreferredRegionKeys $preferredRegionHT -JpOnlyForSelectedConsoles $Context.JpOnlyForSelectedConsoles -JpKeepKeys $jpKeepHT `
        -ParentCloneParentMap $parentMap -ConsoleFilterKeys $consoleFilterHT
      if (-not $clf) { continue }
      if ($clf.IsCorrupt) {
        $Context.CrcVerifyCorruptCount++
        if ($Log) { & $Log ('WARN: CRC32 Verify fehlgeschlagen, markiere als JUNK: {0} ({1})' -f $f.FullName, $clf.CrcErrorMessage) }
      }

      $item = New-FileItem -Root $Root -MainPath $f.FullName -Category $clf.Category -Region $clf.Region `
        -PreferOrder $Context.PreferOrder -VersionScore $clf.VersionScore -GameKey $clf.GameKey -DatMatch ([bool]$clf.DatMatched) -IsCorrupt ([bool]$clf.IsCorrupt) `
        -BaseName ([string]$f.BaseName) -FileInfoByPath $fileInfoByPath

      & $Context.AddItem $item
    }
  }
}

function Invoke-RegionDedupe {
  param(
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [ValidateSet('DryRun','Move')][string]$Mode,
    [string[]]$PreferOrder,
    [string]$TrashRoot,
    [string]$AuditRoot,
    [string[]]$IncludeExtensions,
    [bool]$RemoveJunk = $true,
    [bool]$AggressiveJunk = $false,
    [bool]$AliasEditionKeying = $false,
    [string[]]$ConsoleFilter = @(),
    [bool]$JpOnlyForSelectedConsoles = $false,
    [string[]]$JpKeepConsoles = @(),
    [bool]$SeparateBios = $false,
    [bool]$GenerateReportsInDryRun = $true,
    [bool]$UseDat = $false,
    [string]$DatRoot,
    [ValidateSet('SHA1','MD5','CRC32')][string]$DatHashType = 'SHA1',
    [bool]$CrcVerifyScan = $false,
    [bool]$DatFallback = $true,
    [hashtable]$DatMap,
    [hashtable]$ToolOverrides,
    [hashtable]$ConsoleSortUnknownReasons,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash,
    [bool]$RequireConfirmMove = $false,
    [scriptblock]$ConfirmMove,
    [bool]$ConvertChecked = $false,
    [bool]$Use1G1R = $false,
    [hashtable]$ManualWinnerOverrides,
    [string[]]$ExcludedCandidatePaths = @()
  )

  # BUG-DD-03: Preflight-Check — TrashRoot darf kein Unter-/Ueberordner eines Roots sein
  if (-not [string]::IsNullOrWhiteSpace($TrashRoot)) {
    $normalizedTrash = [System.IO.Path]::GetFullPath($TrashRoot).TrimEnd('\', '/')
    foreach ($r in $Roots) {
      if ([string]::IsNullOrWhiteSpace($r)) { continue }
      $normalizedRoot = [System.IO.Path]::GetFullPath($r).TrimEnd('\', '/')
      $trashWithSep = $normalizedTrash + '\'
      $rootWithSep  = $normalizedRoot + '\'
      if ($normalizedTrash -eq $normalizedRoot -or
          $trashWithSep.StartsWith($rootWithSep, [StringComparison]::OrdinalIgnoreCase) -or
          $rootWithSep.StartsWith($trashWithSep, [StringComparison]::OrdinalIgnoreCase)) {
        throw (New-OperationError -ErrorCode 'RUN-PREFLIGHT-OVERLAP' -Message ("TrashRoot '{0}' ueberlappt mit Root '{1}'. TrashRoot darf kein Unter-/Ueberordner eines Roots sein." -f $TrashRoot, $r) -Severity 'Critical')
      }
    }
  }

  # Phase 1: Initialise shared context (caches, filter sets, DAT index, tools)
  $ctx = Initialize-RegionDedupeContext `
    -Roots $Roots -PreferOrder $PreferOrder -IncludeExtensions $IncludeExtensions `
    -RemoveJunk $RemoveJunk -AggressiveJunk $AggressiveJunk -AliasEditionKeying $AliasEditionKeying `
    -ConsoleFilter $ConsoleFilter -JpOnlyForSelectedConsoles $JpOnlyForSelectedConsoles -JpKeepConsoles $JpKeepConsoles `
    -UseDat $UseDat -DatRoot $DatRoot -DatHashType $DatHashType -CrcVerifyScan $CrcVerifyScan `
    -DatFallback $DatFallback -Use1G1R $Use1G1R -DatMap $DatMap -ToolOverrides $ToolOverrides `
    -Log $Log -OnDatHash $OnDatHash -ExcludedCandidatePaths $ExcludedCandidatePaths

  # Track corrupt count on context (mutable counter)
  $ctx | Add-Member -NotePropertyName CrcVerifyCorruptCount -NotePropertyValue 0

  # Phase 2: Scan each root
  $sortedRoots = @($Roots | Sort-Object)
  $rootTotal = $sortedRoots.Count
  $rootIndex = 0
  foreach ($root in $sortedRoots) {
    $rootIndex++
    Test-CancelRequested
    Invoke-RegionDedupeScanRoot -Context $ctx -Root $root `
      -RootIndex $rootIndex -RootTotal $rootTotal `
      -TrashRoot $TrashRoot -Log $Log -OnDatHash $OnDatHash
  }

  # Phase 3: Post-scan summary
  if ($ctx.UseDat -and $OnDatHash -and $ctx.DatHashTotal -gt 0) {
    & $OnDatHash $ctx.DatHashTotal $ctx.DatHashTotal $null
  }

  $totalFound = $ctx.AllItems.Count + $ctx.JunkItems.Count + $ctx.BiosItems.Count
  if ($totalFound -eq 0) {
    & $Log 'Keine passenden Dateien gefunden.'
    [void](Set-AppStateValue -Key 'UseAggressiveJunk' -Value $ctx.PreviousAggressiveJunk)
    return $null
  }

  $ctx.Stopwatch.Stop()
  & $Log ('{0:N1}s Scan-Zeit' -f $ctx.Stopwatch.Elapsed.TotalSeconds)
  if ($CrcVerifyScan) {
    & $Log ('CRC32 Verify: {0} Datei(en) mit Lesefehler/Korruption als JUNK markiert.' -f $ctx.CrcVerifyCorruptCount)
  }
  & $Log ''
  [void](Set-AppStateValue -Key 'UseAggressiveJunk' -Value $ctx.PreviousAggressiveJunk)
  & $Log '=== Klassifizierung ==='
  & $Log ('{0} Games, {1} Junk, {2} BIOS/Firmware' -f $ctx.AllItems.Count, $ctx.JunkItems.Count, $ctx.BiosItems.Count)

  # Phase 4: Pipeline (Classify -> Group -> Select -> Report)
  return (Invoke-RegionDedupePipeline `
    -AllItems $ctx.AllItems -JunkItems $ctx.JunkItems -BiosItems $ctx.BiosItems -Mode $Mode `
    -ManualWinnerOverrides $ManualWinnerOverrides -SeparateBios:$SeparateBios `
    -Roots $Roots -TrashRoot $TrashRoot -AuditRoot $AuditRoot -RequireConfirmMove:$RequireConfirmMove `
    -ConfirmMove $ConfirmMove -ConvertChecked:$ConvertChecked -GenerateReportsInDryRun:$GenerateReportsInDryRun `
    -UseDat:$UseDat -DatIndex $ctx.DatIndex -ConsoleSortUnknownReasons $ConsoleSortUnknownReasons `
    -TotalFound $totalFound -Log $Log)
}

function Invoke-RegionDedupePipeline {
  param(
    [System.Collections.Generic.List[psobject]]$AllItems,
    [System.Collections.Generic.List[psobject]]$JunkItems,
    [System.Collections.Generic.List[psobject]]$BiosItems,
    [ValidateSet('DryRun','Move')][string]$Mode,
    [hashtable]$ManualWinnerOverrides,
    [bool]$SeparateBios,
    [string[]]$Roots,
    [string]$TrashRoot,
    [string]$AuditRoot,
    [bool]$RequireConfirmMove,
    [scriptblock]$ConfirmMove,
    [bool]$ConvertChecked,
    [bool]$GenerateReportsInDryRun,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [hashtable]$ConsoleSortUnknownReasons,
    [int]$TotalFound,
    [scriptblock]$Log
  )

  Invoke-RegionDedupeClassifyPhase -AllItems $AllItems -JunkItems $JunkItems -BiosItems $BiosItems -Log $Log | Out-Null
  $groups = Invoke-RegionDedupeGroupPhase -AllItems $AllItems -Log $Log

  $selection = Invoke-RegionDedupeSelectPhase -Groups $groups -Mode $Mode -ManualWinnerOverrides $ManualWinnerOverrides `
    -JunkItems $JunkItems -BiosItems $BiosItems -SeparateBios:$SeparateBios -Log $Log

  return (Invoke-RegionDedupeReportPhase -Selection $selection -AllItems $AllItems -JunkItems $JunkItems -BiosItems $BiosItems `
    -Roots $Roots -TrashRoot $TrashRoot -AuditRoot $AuditRoot -RequireConfirmMove:$RequireConfirmMove -ConfirmMove $ConfirmMove `
    -ConvertChecked:$ConvertChecked -GenerateReportsInDryRun:$GenerateReportsInDryRun -Mode $Mode -UseDat:$UseDat -DatIndex $DatIndex `
    -ConsoleSortUnknownReasons $ConsoleSortUnknownReasons -TotalFound $TotalFound -Log $Log)
}

function Invoke-RegionDedupeClassifyPhase {
  param(
    [System.Collections.Generic.List[psobject]]$AllItems,
    [System.Collections.Generic.List[psobject]]$JunkItems,
    [System.Collections.Generic.List[psobject]]$BiosItems,
    [scriptblock]$Log
  )

  & $Log ('{0} Games, {1} Junk, {2} BIOS/Firmware' -f $AllItems.Count, $JunkItems.Count, $BiosItems.Count)
  return [pscustomobject]@{
    Games = $AllItems.Count
    Junk = $JunkItems.Count
    Bios = $BiosItems.Count
  }
}

function Invoke-RegionDedupeGroupPhase {
  param(
    [System.Collections.Generic.List[psobject]]$AllItems,
    [scriptblock]$Log
  )

  & $Log '=== Group-Phase ==='
  $groupsByKey = New-Object 'System.Collections.Generic.SortedDictionary[string,System.Collections.Generic.List[psobject]]' ([StringComparer]::OrdinalIgnoreCase)
  $emptyKeyCounter = 0
  foreach ($entry in $AllItems) {
    if ([string]::IsNullOrWhiteSpace($entry.GameKey)) {
      # BUG-CS-03 fix: each empty-key item gets its own group to prevent false competition
      $emptyKeyCounter++
      $groupKey = '__empty_{0}__' -f $emptyKeyCounter
    } else {
      $groupKey = [string]$entry.GameKey
    }
    if (-not $groupsByKey.ContainsKey($groupKey)) {
      $groupsByKey[$groupKey] = New-Object System.Collections.Generic.List[psobject]
    }
    [void]$groupsByKey[$groupKey].Add($entry)
  }

  $groups = New-Object System.Collections.Generic.List[psobject]
  foreach ($kvp in $groupsByKey.GetEnumerator()) {
    $groupItems = if ($kvp.Value -is [System.Collections.Generic.List[psobject]]) {
      @($kvp.Value.ToArray())
    } else {
      @($kvp.Value)
    }
    [void]$groups.Add([pscustomobject]@{ Name = $kvp.Key; Group = [psobject[]]$groupItems })
  }
  return ,$groups
}

function Invoke-RegionDedupeSelectPhase {
  param(
    [System.Collections.Generic.List[psobject]]$Groups,
    [ValidateSet('DryRun','Move')][string]$Mode,
    [hashtable]$ManualWinnerOverrides,
    [System.Collections.Generic.List[psobject]]$JunkItems,
    [System.Collections.Generic.List[psobject]]$BiosItems,
    [bool]$SeparateBios,
    [scriptblock]$Log
  )

  & $Log '=== Select-Phase ==='
  $dedupeSw = [System.Diagnostics.Stopwatch]::StartNew()
  $report = New-Object System.Collections.Generic.List[psobject]
  $dupeGroups = 0
  $totalDupes = 0
  $savedBytes = [long]0
  $smartMergeSetConflicts = 0

  $groupTotal = $Groups.Count
  $groupIndex = 0
  foreach ($g in $Groups) {
    Test-CancelRequested
    $groupIndex++
    if (($groupIndex -eq 1) -or ($groupIndex -eq $groupTotal) -or ($groupIndex % 200 -eq 0)) {
      $pct = if ($groupTotal -gt 0) { [math]::Round(($groupIndex / $groupTotal) * 100, 1) } else { 100 }
      & $Log ('Dedupe-Fortschritt: {0}/{1} Gruppen ({2}%)' -f $groupIndex, $groupTotal, $pct)
    }

    $items = $g.Group
    $winner = $null
    if ($ManualWinnerOverrides -and $ManualWinnerOverrides.ContainsKey([string]$g.Name)) {
      $overridePath = [string]$ManualWinnerOverrides[[string]$g.Name]
      if (-not [string]::IsNullOrWhiteSpace($overridePath)) {
        $candidate = @($items | Where-Object { [string]::Equals([string]$_.MainPath, $overridePath, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1)
        if ($candidate.Count -gt 0) {
          $winner = $candidate[0]
          & $Log ('MANUAL-OVERRIDE: {0} -> {1}' -f [string]$g.Name, [string]$winner.MainPath)
        } else {
          if ($Log) { & $Log ('WARNUNG: Manual-Override Pfad nicht gefunden: {0} (GameKey: {1})' -f $overridePath, [string]$g.Name) }
        }
      }
    }
    if (-not $winner -and $items -and $items.Count -gt 0) { $winner = Select-Winner -Items $items }

    $smartMergeInsight = Get-SmartMergeSetConflictInsight -GameKey $g.Name -Items $items -Winner $winner
    if ($smartMergeInsight) {
      $smartMergeSetConflicts++
      $langText = if ($smartMergeInsight.LanguageHints.Count -gt 0) { $smartMergeInsight.LanguageHints -join ',' } else { '-' }
      $typeText = if ($smartMergeInsight.VariantTypes.Count -gt 0) { $smartMergeInsight.VariantTypes -join ',' } else { '-' }
      & $Log ('SMART-MERGE: Set-Konflikt "{0}" | Varianten={1} ({2}) | Winner={3} | DiffMembers={4} | DiffTracks={5} | Lang={6}' -f `
        $smartMergeInsight.GameKey,
        $smartMergeInsight.VariantCount,
        $typeText,
        ([IO.Path]::GetFileName($smartMergeInsight.WinnerPath)),
        $smartMergeInsight.DiffMemberCount,
        $smartMergeInsight.DiffTrackCount,
        $langText)
    }

    if ($items.Count -gt 1) {
      $dupeGroups++
      $totalDupes += ($items.Count - 1)
    }

    foreach ($it in $items) {
      $isWinner = ($it.MainPath -eq $winner.MainPath)
      if (-not $isWinner) { $savedBytes += $it.SizeBytes }
      $action = if ($isWinner) { 'KEEP' } else { if ($Mode -eq 'Move') { 'MOVE' } else { 'SKIP_DRYRUN' } }
      [void]$report.Add((New-ReportRow -Item $it -GameKey $g.Name -Action $action `
        -WinnerRegion $winner.Region -WinnerPath $winner.MainPath -DatMatch $it.DatMatch -IsCorrupt $it.IsCorrupt))
    }
  }

  foreach ($it in $JunkItems) {
    Test-CancelRequested
    $savedBytes += $it.SizeBytes
    $action = if ($Mode -eq 'Move') { 'JUNK' } else { 'DRYRUN-JUNK' }
    [void]$report.Add((New-ReportRow -Item $it -GameKey $it.GameKey -Action $action -IsCorrupt $it.IsCorrupt))
  }

  foreach ($it in $BiosItems) {
    Test-CancelRequested
    $biosAction = if ($Mode -eq 'Move') {
      if ($SeparateBios) { 'BIOS-MOVE' } else { 'KEEP' }
    } else {
      if ($SeparateBios) { 'DRYRUN-BIOS' } else { 'KEEP' }
    }
    [void]$report.Add((New-ReportRow -Item $it -GameKey $it.GameKey -Action $biosAction -IsCorrupt $it.IsCorrupt))
  }

  $dedupeSw.Stop()
  & $Log ('{0:N1}s Dedupe-Zeit' -f $dedupeSw.Elapsed.TotalSeconds)

  return [pscustomobject]@{
    Report = @($report.ToArray())
    DupeGroups = $dupeGroups
    TotalDupes = $totalDupes
    SavedBytes = $savedBytes
    SmartMergeSetConflicts = $smartMergeSetConflicts
  }
}

function Invoke-RegionDedupeReportPhase {
  param(
    [pscustomobject]$Selection,
    [System.Collections.Generic.List[psobject]]$AllItems,
    [System.Collections.Generic.List[psobject]]$JunkItems,
    [System.Collections.Generic.List[psobject]]$BiosItems,
    [string[]]$Roots,
    [string]$TrashRoot,
    [string]$AuditRoot,
    [bool]$RequireConfirmMove,
    [scriptblock]$ConfirmMove,
    [bool]$ConvertChecked,
    [bool]$GenerateReportsInDryRun,
    [ValidateSet('DryRun','Move')][string]$Mode,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [hashtable]$ConsoleSortUnknownReasons,
    [int]$TotalFound,
    [scriptblock]$Log
  )

  & $Log '=== Report-Phase ==='
  $groupsCount = [int](@($Selection.Report | Where-Object { $_.Action -eq 'KEEP' -and $_.Category -eq 'GAME' }).Count)
  $junkSum = [long]0
  foreach ($ji in $JunkItems) { $junkSum += $ji.SizeBytes }
  $junkMB = [math]::Round($junkSum / 1MB, 1)
  $savedMB = [math]::Round([long]$Selection.SavedBytes / 1MB, 1)

  & $Log ''
  & $Log '=== Ergebnis ==='
  & $Log ('{0} eindeutige Spiele' -f $groupsCount)
  if ($Selection.DupeGroups -gt 0) {
    & $Log ('{0} Duplikat-Gruppen, {1} Duplikate' -f $Selection.DupeGroups, $Selection.TotalDupes)
  }
  if ($Selection.SmartMergeSetConflicts -gt 0) {
    & $Log ('Smart-Merge: {0} Set-Konflikt(e) mit Differenzanalyse erkannt.' -f $Selection.SmartMergeSetConflicts)
  }
  if ($JunkItems.Count -gt 0) {
    & $Log ('{0} Junk-Dateien ({1} MB)' -f $JunkItems.Count, $junkMB)
  }
  & $Log ('{0} BIOS/Firmware' -f $BiosItems.Count)
  & $Log ('{0} MB insgesamt einsparbar' -f $savedMB)
  & $Log ('Status: Scan={0}, Unique={1}, DupeGroups={2}, Junk={3}, BIOS={4}, SavedMB={5}' -f $TotalFound, $groupsCount, $Selection.DupeGroups, $JunkItems.Count, $BiosItems.Count, $savedMB)

  if (Get-Command Get-ArchiveEntryCacheStatistics -ErrorAction SilentlyContinue) {
    try {
      $cacheStats = Get-ArchiveEntryCacheStatistics
      if ($cacheStats) {
        & $Log ('PERF: CacheHitRate={0}% Hits={1} Misses={2} Entries={3}/{4}' -f $cacheStats.HitRate, $cacheStats.Hits, $cacheStats.Misses, $cacheStats.Entries, $cacheStats.MaxEntries)
      }
    } catch { }
  }

  $generateReports = ($Mode -eq 'Move') -or $GenerateReportsInDryRun
  $reportRows = @($Selection.Report)
  $reportResult = Write-Reports -Report $reportRows -DupeGroups $Selection.DupeGroups -TotalDupes $Selection.TotalDupes `
    -SavedBytes $Selection.SavedBytes -JunkCount $JunkItems.Count -JunkBytes $junkSum `
    -BiosCount $BiosItems.Count -UniqueGames $groupsCount -TotalScanned ($AllItems.Count + $JunkItems.Count + $BiosItems.Count) `
    -Mode $Mode -UseDat $UseDat -DatIndex $DatIndex -ConsoleSortUnknownReasons $ConsoleSortUnknownReasons `
    -GenerateReports $generateReports -Log $Log
  $csvPath = $reportResult.CsvPath
  $htmlPath = $reportResult.HtmlPath
  $jsonPath = $reportResult.JsonPath

  $itemByMain = [hashtable]::new($AllItems.Count + $JunkItems.Count + $BiosItems.Count, [StringComparer]::OrdinalIgnoreCase)
  foreach ($it in $JunkItems) { if ($it.MainPath) {
    if ($itemByMain.ContainsKey($it.MainPath)) { & $Log ('WARN: itemByMain Kollision (Junk): {0}' -f $it.MainPath) }
    $itemByMain[$it.MainPath] = $it
  } }
  foreach ($it in $AllItems)  { if ($it.MainPath) {
    if ($itemByMain.ContainsKey($it.MainPath)) { & $Log ('WARN: itemByMain Kollision (Game): {0}' -f $it.MainPath) }
    $itemByMain[$it.MainPath] = $it
  } }
  foreach ($it in $BiosItems) { if ($it.MainPath) {
    if ($itemByMain.ContainsKey($it.MainPath)) { & $Log ('WARN: itemByMain Kollision (BIOS): {0}' -f $it.MainPath) }
    $itemByMain[$it.MainPath] = $it
  } }

  if ($Mode -eq 'Move') {
    $moveResult = Invoke-MovePhase -Report $reportRows -JunkItems $JunkItems -BiosItems $BiosItems -AllItems $AllItems `
      -ItemByMain $itemByMain -Roots $Roots -TrashRoot $TrashRoot -AuditRoot $AuditRoot `
      -RequireConfirmMove $RequireConfirmMove -ConfirmMove $ConfirmMove -CsvPath $csvPath -HtmlPath $htmlPath `
      -TotalDupes $Selection.TotalDupes -SavedMB $savedMB -IncludeConversionPreview:$ConvertChecked -Log $Log
    if ($moveResult -and (($moveResult.PSObject.Properties.Name -contains 'Status' -and [string]$moveResult.Status -eq 'completed') -or [bool]$moveResult.ShouldReturn)) {
      $moveValue = $moveResult.Value
      if ($moveValue) {
        if (-not ($moveValue.PSObject.Properties.Name -contains 'DatCompletenessCsvPath')) {
          Add-Member -InputObject $moveValue -NotePropertyName DatCompletenessCsvPath -NotePropertyValue $reportResult.DatCompletenessCsvPath -Force
        }
        if (-not ($moveValue.PSObject.Properties.Name -contains 'ReportRows')) {
          Add-Member -InputObject $moveValue -NotePropertyName ReportRows -NotePropertyValue @($reportRows) -Force
        }
      }
      return $moveValue
    }
  } else {
    & $Log ''
    & $Log 'DryRun: nichts verschoben. Report prüfen, dann Mode=Move.'
  }

  $pluginResults = @()
  if (Get-Command Invoke-OperationPlugins -ErrorAction SilentlyContinue) {
    $pluginResults = @(Invoke-OperationPlugins -Phase 'post-run' -Context @{
      Mode = $Mode
      CsvPath = $csvPath
      HtmlPath = $htmlPath
      JsonPath = $jsonPath
      DatCompletenessCsvPath = $reportResult.DatCompletenessCsvPath
      ReportRows = @($reportRows)
      SavedBytes = $Selection.SavedBytes
      TotalDupes = $Selection.TotalDupes
    } -Log $Log)
  }

  return [pscustomobject]@{
    CsvPath  = $csvPath
    HtmlPath = $htmlPath
    JsonPath = $jsonPath
    DatCompletenessCsvPath = $reportResult.DatCompletenessCsvPath
    LaunchBoxXmlPath = $(if ($reportResult.PSObject.Properties.Name -contains 'LaunchBoxXmlPath') { $reportResult.LaunchBoxXmlPath } else { $null })
    EmulationStationXmlPath = $(if ($reportResult.PSObject.Properties.Name -contains 'EmulationStationXmlPath') { $reportResult.EmulationStationXmlPath } else { $null })
    ReportRows = @($reportRows)
    Winners  = @($reportRows | Where-Object { $_.Action -eq 'KEEP' -and $_.Category -eq 'GAME' })
    AllItems = $AllItems
    ItemByMain = $itemByMain
    OperationPluginResults = @($pluginResults)
  }
}

# ================================================================
#  SET SCAN HELPER  (extracted from simple_sort.ps1 - Sprint 2)
# ================================================================

function Add-SetItemsFromFiles {
  <# Shared set-scan logic for M3U/CUE/GDI/CCD. #>
  param(
    [string]$Root,
    [System.Collections.Generic.List[System.IO.FileInfo]]$Files,
    [string]$Type,
    [string[]]$PreferOrder,
    [bool]$AggressiveJunk,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [hashtable]$HashCache,
    [string]$DatHashType,
    [bool]$DatFallback,
    [bool]$AliasEditionKeying,
    [bool]$CrcVerifyScan = $false,
    [string]$SevenZipPath,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash,
    [hashtable]$FileInfoByPath,
    [System.Collections.Generic.HashSet[string]]$SetMemberPaths,
    [hashtable]$PreferredRegionKeys,
    [bool]$JpOnlyForSelectedConsoles,
    [hashtable]$JpKeepKeys,
    [hashtable]$ConsoleFilterKeys,
    [scriptblock]$AddItem,
    [scriptblock]$GetRelated,
    [scriptblock]$GetMissing,
    [hashtable]$ParentCloneParentMap = @{}
  )

  if (-not $Files -or $Files.Count -eq 0) { return }

  foreach ($f in $Files) {
    Test-CancelRequested
    $related = @(& $GetRelated $f.FullName $Root)
    $missing = @(& $GetMissing $f.FullName $Root)
    $isComplete = ($missing.Count -eq 0)

    foreach ($r in $related) { [void]$SetMemberPaths.Add($r) }

    $clf = Invoke-ClassifyFile -BaseName $f.BaseName -FilePath $f.FullName -Extension $f.Extension `
      -Root $Root -CrcPaths $related -GameKeyPaths $related `
      -AggressiveJunk $AggressiveJunk -CrcVerifyScan $CrcVerifyScan `
      -UseDat $UseDat -DatIndex $DatIndex -HashCache $HashCache `
      -DatHashType $DatHashType -DatFallback $DatFallback `
      -AliasEditionKeying $AliasEditionKeying -SevenZipPath $SevenZipPath `
      -Log $Log -OnDatHash $OnDatHash `
      -PreferredRegionKeys $PreferredRegionKeys -JpOnlyForSelectedConsoles $JpOnlyForSelectedConsoles -JpKeepKeys $JpKeepKeys `
      -ParentCloneParentMap $ParentCloneParentMap -ConsoleFilterKeys $ConsoleFilterKeys
    if (-not $clf) { continue }
    if ($clf.IsCorrupt -and $Log) {
      & $Log ('WARN: CRC32 Verify fehlgeschlagen, markiere Set als JUNK: {0} ({1})' -f $clf.CrcErrorPath, $clf.CrcErrorMessage)
    }

    $item = New-SetItem -Root $Root -Type $Type -Category $clf.Category `
      -MainPath $f.FullName -Paths $related -Region $clf.Region -PreferOrder $PreferOrder `
      -VersionScore $clf.VersionScore -GameKey $clf.GameKey -DatMatch ([bool]$clf.DatMatched) `
      -BaseName ([string]$f.BaseName) -FileInfoByPath $FileInfoByPath -IsCorrupt ([bool]$clf.IsCorrupt) `
      -IsComplete $isComplete -MissingCount $missing.Count

    & $AddItem $item
  }
}

function Add-MsDosFolderItems {
  <# Build DOS folder candidates (folder-as-game) and exclude member files from FILE scan. #>
  param(
    [string]$Root,
    [System.IO.FileInfo[]]$AllFiles,
    [string[]]$PreferOrder,
    [bool]$AggressiveJunk,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [hashtable]$HashCache,
    [string]$DatHashType,
    [bool]$DatFallback,
    [bool]$AliasEditionKeying,
    [string]$SevenZipPath,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash,
    [hashtable]$FileInfoByPath,
    [System.Collections.Generic.HashSet[string]]$SetMemberPaths,
    [scriptblock]$ApplyRegionKeepPolicy,
    [scriptblock]$AddItem,
    [scriptblock]$ShouldIncludeConsole,
    [pscustomobject]$ParentCloneIndex = $null
  )

  if (-not $AllFiles -or $AllFiles.Count -eq 0) { return 0 }

  $launchExtSet = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  foreach ($e in @('.exe', '.com', '.bat')) { [void]$launchExtSet.Add($e) }

  $rootConsole = Get-ConsoleType -RootPath $Root -FilePath $Root -Extension ''

  $launcherDirSet = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  $candidateDirs = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  foreach ($f in $AllFiles) {
    $parentDir = Split-Path -Parent $f.FullName
    if ([string]::IsNullOrWhiteSpace($parentDir)) { continue }
    [void]$candidateDirs.Add($parentDir)
    if ($launchExtSet.Contains($f.Extension)) {
      [void]$launcherDirSet.Add($parentDir)
    }
  }

  if ($candidateDirs.Count -eq 0) { return 0 }

  $selectedDirs = New-Object System.Collections.Generic.List[string]
  $orderedDirs = @($candidateDirs | Sort-Object @{ Expression = { $_.Length }; Ascending = $true }, @{ Expression = { $_ }; Ascending = $true })
  foreach ($dirPath in $orderedDirs) {
    $hasLauncher = $launcherDirSet.Contains($dirPath)
    $console = Get-ConsoleType -RootPath $Root -FilePath $dirPath -Extension ''
    if ($console -ne 'DOS') { continue }

    # Ohne Launcher nur dann als DOS-Spielordner akzeptieren,
    # wenn der Root selbst klar als DOS erkannt wurde.
    if ((-not $hasLauncher) -and $rootConsole -ne 'DOS') { continue }

    $isNested = $false
    foreach ($existing in $selectedDirs) {
      $existingWithSlash = (Resolve-RootPath -Path $existing -WithTrailingSlash)
      if ($dirPath.StartsWith($existingWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
        $isNested = $true
        break
      }
    }
    if (-not $isNested) {
      [void]$selectedDirs.Add($dirPath)
    }
  }

  if ($selectedDirs.Count -eq 0) { return 0 }

  # BUG DEDUPE-010 FIX: Pre-build parent-directory -> file lookup to avoid O(N*M) scan.
  # Groups files by their parent directory for O(1) lookup per unique dir instead of
  # iterating all files per selected directory.
  $filesByParentDir = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($f in $AllFiles) {
    $parentDir = [IO.Path]::GetDirectoryName($f.FullName)
    if ($null -eq $parentDir) { continue }
    if (-not $filesByParentDir.ContainsKey($parentDir)) {
      $filesByParentDir[$parentDir] = [System.Collections.Generic.List[string]]::new()
    }
    [void]$filesByParentDir[$parentDir].Add($f.FullName)
  }

  $added = 0
  foreach ($dirPath in $selectedDirs) {
    Test-CancelRequested

    $dirWithSlash = Resolve-RootPath -Path $dirPath -WithTrailingSlash
    $related = [System.Collections.Generic.List[string]]::new()
    foreach ($parentDir in $filesByParentDir.Keys) {
      if ($parentDir.Equals($dirPath, [StringComparison]::OrdinalIgnoreCase) -or
          $parentDir.StartsWith($dirWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
        $related.AddRange($filesByParentDir[$parentDir])
      }
    }
    if ($related.Count -eq 0) { continue }
    foreach ($r in $related) { [void]$SetMemberPaths.Add($r) }

    $folderName = Split-Path -Leaf $dirPath
    if ([string]::IsNullOrWhiteSpace($folderName)) { continue }

    $category = Get-FileCategory -BaseName $folderName -AggressiveJunk $AggressiveJunk
    $region   = Get-RegionTag -Name $folderName
    $console  = Get-ConsoleType -RootPath $Root -FilePath $dirPath -Extension ''
    if ($ShouldIncludeConsole -and -not (& $ShouldIncludeConsole $console)) { continue }

    $category = & $ApplyRegionKeepPolicy -CurrentCategory $category -Region $region -Console $console
    $normalizedFolderKey = ConvertTo-GameKey -BaseName $folderName -AliasEditionKeying $AliasEditionKeying -ConsoleType 'DOS'
    $normalizedFolderKeyStrict = [regex]::Replace([string]$normalizedFolderKey, '[^a-z0-9]+', '')
    if (-not [string]::IsNullOrWhiteSpace($normalizedFolderKeyStrict)) {
      $normalizedFolderKey = $normalizedFolderKeyStrict
    }

    $keyInfo  = $null
    if ($UseDat) {
      $keyInfo = Resolve-GameKey -BaseName $folderName -Paths $related -Root $Root -MainPath $dirPath -UseDat $UseDat -DatIndex $DatIndex -HashCache $HashCache -HashType $DatHashType -DatFallback $DatFallback -AliasEditionKeying $AliasEditionKeying -SevenZipPath $SevenZipPath -Log $Log -OnDatHash $OnDatHash
      if (-not $keyInfo.DatMatched) {
        $keyInfo = [pscustomobject]@{ Key = $normalizedFolderKey; DatMatched = $false }
      }
    } else {
      $keyInfo = [pscustomobject]@{ Key = $normalizedFolderKey; DatMatched = $false }
    }

    $key      = $keyInfo.Key
    if ($ParentCloneIndex -and $ParentCloneIndex.ParentMap) {
      $key = Resolve-ParentName -GameName $key -ConsoleKey $console -ParentMap $ParentCloneIndex.ParentMap
    }
    $verScore = Get-VersionScore -BaseName $folderName

    $item = New-SetItem -Root $Root -Type 'DOSDIR' -Category $category `
      -MainPath $dirPath -Paths $related -Region $region -PreferOrder $PreferOrder `
      -VersionScore $verScore -GameKey $key -DatMatch $keyInfo.DatMatched `
      -BaseName $folderName -FileInfoByPath $FileInfoByPath -IsCorrupt $false `
      -IsComplete $true -MissingCount 0

    & $AddItem $item
    $added++
  }

  return $added
}

function Get-SmartMergeSetConflictInsight {
  param(
    [string]$GameKey,
    [psobject[]]$Items,
    [psobject]$Winner
  )

  if (-not $Items -or $Items.Count -lt 2 -or -not $Winner) { return $null }

  $setItems = @($Items | Where-Object {
      $_ -and
      $_.PSObject.Properties.Name -contains 'Type' -and
      ([string]$_.Type -match 'SET$') -and
      $_.PSObject.Properties.Name -contains 'Paths'
    })
  if ($setItems.Count -lt 2) { return $null }

  $languagePattern = '(?<![a-z])(en|eng|de|ger|fr|fre|es|spa|it|ita|jp|jpn)(?![a-z])'
  $trackPattern = '(track\s*\d+|audio|bonus|extra|sample|disc\s*\d+|cd\s*\d+)'

  $variants = New-Object System.Collections.Generic.List[psobject]
  foreach ($item in $setItems) {
    $memberNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $trackNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    $paths = @()
    if ($item.Paths) {
      foreach ($path in @($item.Paths)) {
        if ([string]::IsNullOrWhiteSpace([string]$path)) { continue }
        $name = [IO.Path]::GetFileName([string]$path)
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        [void]$memberNames.Add($name.ToLowerInvariant())
        if ($name.ToLowerInvariant() -match $trackPattern) {
          [void]$trackNames.Add($name.ToLowerInvariant())
        }
      }
      $paths = @($item.Paths)
    }

    if ($memberNames.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace([string]$item.MainPath)) {
      $mainName = [IO.Path]::GetFileName([string]$item.MainPath)
      if (-not [string]::IsNullOrWhiteSpace($mainName)) {
        [void]$memberNames.Add($mainName.ToLowerInvariant())
      }
    }

    [void]$variants.Add([pscustomobject]@{
      Item = $item
      MainPath = [string]$item.MainPath
      Paths = $paths
      MemberNames = $memberNames
      TrackNames = $trackNames
    })
  }

  if ($variants.Count -lt 2) { return $null }

  $winnerVariant = @($variants | Where-Object { $_.MainPath -eq [string]$Winner.MainPath } | Select-Object -First 1)
  if (-not $winnerVariant) { $winnerVariant = @($variants | Select-Object -First 1) }
  if (-not $winnerVariant) { return $null }
  $winnerVariant = $winnerVariant[0]

  $diffMembers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $diffTracks = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

  foreach ($variant in $variants) {
    if ($variant.MainPath -eq $winnerVariant.MainPath) { continue }

    foreach ($name in $variant.MemberNames) {
      if (-not $winnerVariant.MemberNames.Contains($name)) {
        [void]$diffMembers.Add($name)
      }
    }
    foreach ($name in $winnerVariant.MemberNames) {
      if (-not $variant.MemberNames.Contains($name)) {
        [void]$diffMembers.Add($name)
      }
    }

    foreach ($name in $variant.TrackNames) {
      if (-not $winnerVariant.TrackNames.Contains($name)) {
        [void]$diffTracks.Add($name)
      }
    }
    foreach ($name in $winnerVariant.TrackNames) {
      if (-not $variant.TrackNames.Contains($name)) {
        [void]$diffTracks.Add($name)
      }
    }
  }

  $languageHints = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($name in $diffMembers) {
    foreach ($match in [regex]::Matches($name, $languagePattern, 'IgnoreCase')) {
      if ($match -and $match.Groups.Count -gt 1) {
        [void]$languageHints.Add($match.Groups[1].Value.ToUpperInvariant())
      }
    }
  }

  $hasConflict = ($diffMembers.Count -gt 0) -or ($diffTracks.Count -gt 0)
  if (-not $hasConflict) { return $null }

  return [pscustomobject]@{
    GameKey = if ([string]::IsNullOrWhiteSpace($GameKey)) { '__empty__' } else { [string]$GameKey }
    VariantCount = [int]$variants.Count
    WinnerPath = [string]$winnerVariant.MainPath
    DiffMemberCount = [int]$diffMembers.Count
    DiffTrackCount = [int]$diffTracks.Count
    LanguageHints = @($languageHints | Sort-Object)
    VariantTypes = @($setItems | ForEach-Object { [string]$_.Type } | Sort-Object -Unique)
  }
}

# ================================================================
#  Parallel Classification (PERF-07)
# ================================================================

$script:PARALLEL_CLASSIFY_THRESHOLD = 2000
$script:PARALLEL_HASH_THRESHOLD = 200

function Get-ParallelThreshold {
  <# Returns the effective parallel threshold for the given AppState key.
     Reads from AppState with a configurable minimum floor. #>
  param(
    [Parameter(Mandatory)][int]$ScriptDefault,
    [Parameter(Mandatory)][string]$Key,
    [int]$Min = 10
  )

  $threshold = $ScriptDefault
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    $configured = Get-AppStateValue -Key $Key -Default $threshold
    if ($configured -is [int] -or $configured -is [long]) {
      $threshold = [int][math]::Max($Min, [int64]$configured)
    }
  }
  return $threshold
}

function Get-ParallelClassifyThreshold {
  return Get-ParallelThreshold -ScriptDefault ([int]$script:PARALLEL_CLASSIFY_THRESHOLD) -Key 'ParallelClassifyThreshold' -Min 100
}

function Get-ParallelHashThreshold {
  return Get-ParallelThreshold -ScriptDefault ([int]$script:PARALLEL_HASH_THRESHOLD) -Key 'ParallelHashThreshold' -Min 10
}

function Get-AdaptiveWorkerCount {
  <# Berechnet die optimale Worker-Anzahl fuer parallele Operationen.
     NAS-Pfade (UNC) werden auf maximal 2 Worker begrenzt, da Netzwerk-IO
     bei zu vielen parallelen Zugriffen einbricht. #>
  param(
    [ValidateSet('Hash','Classify')][string]$Task = 'Hash',
    [string[]]$Roots = @(),
    [int]$ItemCount = 0
  )

  $cpuCount = [Environment]::ProcessorCount
  $maxWorkers = [Math]::Max(1, [Math]::Min($cpuCount, 8))

  # NAS-Erkennung: UNC-Pfade begrenzen auf max 2 Worker
  $isNas = $false
  foreach ($r in $Roots) {
    if (-not [string]::IsNullOrWhiteSpace($r) -and $r.StartsWith('\\')) {
      $isNas = $true
      break
    }
  }
  if ($isNas) { $maxWorkers = [Math]::Min($maxWorkers, 2) }

  # Bei wenigen Items nicht mehr Worker als Items
  if ($ItemCount -gt 0) {
    $maxWorkers = [Math]::Min($maxWorkers, $ItemCount)
  }

  return [Math]::Max(1, $maxWorkers)
}

function Get-HashPathsByPriority {
  <# Sortiert Dateien fuer Hash-Berechnung nach Prioritaet:
     1. Komprimierte Formate (CHD) zuerst (kleiner, schneller zu hashen)
     2. Innerhalb gleicher Extension: kleinere Dateien zuerst #>
  param(
    [System.IO.FileInfo[]]$Files
  )

  if (-not $Files -or $Files.Count -eq 0) { return @() }

  # Extension-Prioritaet: komprimierte Formate zuerst
  $extPriority = @{
    '.chd' = 0
    '.rvz' = 1
    '.gcz' = 1
    '.cso' = 1
    '.zso' = 1
    '.7z'  = 2
    '.zip' = 2
  }

  $sorted = @($Files | Sort-Object -Property @(
    @{ Expression = {
      $ext = $_.Extension.ToLowerInvariant()
      if ($extPriority.ContainsKey($ext)) { $extPriority[$ext] } else { 5 }
    }; Ascending = $true },
    @{ Expression = { $_.Length }; Ascending = $true }
  ))

  return $sorted
}

# Runspace lifecycle functions consolidated into RunspaceLifecycle.ps1
# (Stop-RunspaceWorkerJobsShared, Remove-RunspacePoolResourcesShared)

function New-ClassificationRunspacePool {
  <# Creates a RunspacePool with the full RomCleanup module for parallel file classification. #>
  param(
    [int]$MaxParallel,
    [System.Threading.ManualResetEventSlim]$CancelEvent,
    [System.Collections.Concurrent.ConcurrentQueue[string]]$LogQueue
  )

  $modulePath = Join-Path $PSScriptRoot 'RomCleanup.psd1'
  if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
    throw ('Klassifizierungs-Runspace kann Modulmanifest nicht finden: {0}' -f $modulePath)
  }

  $iss = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
  $iss.ImportPSModule(@($modulePath))

  # Override Test-CancelRequested: check shared ManualResetEventSlim instead of AppState
  $cancelDef = @'
if ($script:_CancelEvent -and $script:_CancelEvent.IsSet) {
  throw [System.OperationCanceledException]::new("Abbruch durch Benutzer")
}
'@
  $iss.Commands.Add(
    [System.Management.Automation.Runspaces.SessionStateFunctionEntry]::new('Test-CancelRequested', $cancelDef)
  )

  # Override Wait-ProcessResponsive: no UI pumping in worker runspace
  $waitDef = @'
param([System.Diagnostics.Process]$Process, [int]$PollMs = 200)
$maxWaitMs = 300000
$timer = [System.Diagnostics.Stopwatch]::StartNew()
try {
  while (-not $Process.WaitForExit($PollMs)) {
    if ($script:_CancelEvent -and $script:_CancelEvent.IsSet) {
      try { $Process.Kill() } catch { }
      throw [System.OperationCanceledException]::new("Abbruch durch Benutzer")
    }
    if ($maxWaitMs -gt 0 -and $timer.ElapsedMilliseconds -ge $maxWaitMs) {
      try { $Process.Kill() } catch { }
      try { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue } catch { }
      throw [System.TimeoutException]::new("Tool-Timeout nach $maxWaitMs ms")
    }
  }
} catch [System.OperationCanceledException] { throw }
catch [System.TimeoutException] { throw }
catch { if (-not $Process.HasExited) { try { $Process.Kill() } catch { } }; throw }
'@
  $iss.Commands.Add(
    [System.Management.Automation.Runspaces.SessionStateFunctionEntry]::new('Wait-ProcessResponsive', $waitDef)
  )

  $iss.Variables.Add(
    [System.Management.Automation.Runspaces.SessionStateVariableEntry]::new('_CancelEvent', $CancelEvent, $null)
  )
  $iss.Variables.Add(
    [System.Management.Automation.Runspaces.SessionStateVariableEntry]::new('_LogQueue', $LogQueue, $null)
  )

  $pool = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspacePool(1, $MaxParallel, $iss, $Host)
  $pool.Open()
  return $pool
}

function Invoke-ClassifyFilesParallel {
  <# Classifies files in parallel using a RunspacePool.
     Returns @{ Items = [psobject[]]; CorruptCount = [int] }. #>
  param(
    [System.Collections.Generic.List[System.IO.FileInfo]]$Files,
    [string]$Root,
    [string[]]$PreferOrder,
    [bool]$AggressiveJunk,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [string]$DatHashType,
    [bool]$DatFallback,
    [bool]$AliasEditionKeying,
    [bool]$CrcVerifyScan,
    [string]$SevenZipPath,
    [hashtable]$ParentCloneParentMap,
    [hashtable]$ConsoleFilterKeys,
    [hashtable]$PreferredRegionKeys,
    [bool]$JpOnlyForSelectedConsoles,
    [hashtable]$JpKeepKeys,
    [hashtable]$FileInfoByPath,
    [scriptblock]$Log,
    [hashtable]$HashCache
  )

  # BUG DEDUPE-005 FIX: Use Get-AdaptiveWorkerCount to respect NAS/UNC worker limit
  $maxParallel = Get-AdaptiveWorkerCount -Task 'Classify' -Roots @($Root) -ItemCount $Files.Count
  $logQueue    = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
  $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
  $pool        = $null
  $jobs        = [System.Collections.Generic.List[hashtable]]::new()

  # Pre-init lazy regex caches: call ConvertTo-GameKey once to trigger compilation
  [void](ConvertTo-GameKey -BaseName 'PERF07_INIT_DUMMY')

  try {
    $pool = New-ClassificationRunspacePool -MaxParallel $maxParallel -CancelEvent $cancelEvent -LogQueue $logQueue

    # Split files into chunks (one per worker)
    $chunkSize = [math]::Max(100, [math]::Ceiling($Files.Count / $maxParallel))
    $chunks = [System.Collections.Generic.List[string[]]]::new()
    for ($i = 0; $i -lt $Files.Count; $i += $chunkSize) {
      $end = [math]::Min($i + $chunkSize, $Files.Count)
      $paths = [string[]]::new($end - $i)
      for ($j = $i; $j -lt $end; $j++) {
        $paths[$j - $i] = $Files[$j].FullName
      }
      [void]$chunks.Add($paths)
    }

    & $Log ("  Parallel: {0} Dateien in {1} Chunks à ~{2} ({3} Worker)" -f $Files.Count, $chunks.Count, $chunkSize, $maxParallel)

    # Worker script
    $workerScript = {
      param(
        [string[]]$FilePaths, [string]$Root, [string[]]$PreferOrder,
        [bool]$AggressiveJunk, [bool]$UseDat, [hashtable]$DatIndex,
        [string]$DatHashType, [bool]$DatFallback, [bool]$AliasEditionKeying,
        [bool]$CrcVerifyScan, [string]$SevenZipPath,
        [hashtable]$ParentCloneParentMap, [hashtable]$ConsoleFilterKeys,
        [hashtable]$PreferredRegionKeys, [bool]$JpOnlyForSelectedConsoles,
        [hashtable]$JpKeepKeys, [hashtable]$PreHashCache
      )

      # Promote ISS variables to script scope (intentional cross-runspace communication)
      $script:_CancelEvent = $_CancelEvent
      $script:_LogQueue    = $_LogQueue

      $logCb = { param([string]$msg); if ($script:_LogQueue) { $script:_LogQueue.Enqueue($msg) } }
      Reset-ClassificationCaches

      $hashCache = if ($UseDat) {
        if ($PreHashCache -and $PreHashCache.Count -gt 0) {
          $c = [hashtable]::new($PreHashCache.Count, [StringComparer]::OrdinalIgnoreCase)
          foreach ($kv in $PreHashCache.GetEnumerator()) { $c[$kv.Key] = $kv.Value }
          $c
        } else {
          [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        }
      } else { $null }

      # Build local fileInfoByPath for this chunk
      $localFileInfo = [hashtable]::new($FilePaths.Count, [StringComparer]::OrdinalIgnoreCase)
      foreach ($p in $FilePaths) {
        try { $localFileInfo[$p] = [System.IO.FileInfo]::new($p) } catch { }
      }

      $results = [System.Collections.Generic.List[psobject]]::new()
      $corruptCount = 0

      foreach ($path in $FilePaths) {
        Test-CancelRequested

        $fi = $localFileInfo[$path]
        if (-not $fi) { continue }

        $clf = Invoke-ClassifyFile -BaseName $fi.BaseName -FilePath $path -Extension $fi.Extension `
          -Root $Root -CrcPaths @($path) -GameKeyPaths @($path) `
          -AggressiveJunk $AggressiveJunk -CrcVerifyScan $CrcVerifyScan `
          -UseDat $UseDat -DatIndex $DatIndex -HashCache $hashCache `
          -DatHashType $DatHashType -DatFallback $DatFallback `
          -AliasEditionKeying $AliasEditionKeying -SevenZipPath $SevenZipPath `
          -Log $logCb -OnDatHash $null `
          -PreferredRegionKeys $PreferredRegionKeys -JpOnlyForSelectedConsoles $JpOnlyForSelectedConsoles -JpKeepKeys $JpKeepKeys `
          -ParentCloneParentMap $ParentCloneParentMap -ConsoleFilterKeys $ConsoleFilterKeys
        if (-not $clf) { continue }
        if ($clf.IsCorrupt) {
          $corruptCount++
          & $logCb ('WARN: CRC32 Verify fehlgeschlagen, markiere als JUNK: {0} ({1})' -f $path, $clf.CrcErrorMessage)
        }

        $item = New-FileItem -Root $Root -MainPath $path -Category $clf.Category -Region $clf.Region `
          -PreferOrder $PreferOrder -VersionScore $clf.VersionScore -GameKey $clf.GameKey `
          -DatMatch ([bool]$clf.DatMatched) -IsCorrupt ([bool]$clf.IsCorrupt) -BaseName ([string]$fi.BaseName) -FileInfoByPath $localFileInfo

        [void]$results.Add($item)
      }

      return @{ Items = @($results); CorruptCount = $corruptCount }
    }

    # Dispatch chunks to pool
    foreach ($chunk in $chunks) {
      $ps = [System.Management.Automation.PowerShell]::Create()
      $ps.RunspacePool = $pool
      [void]$ps.AddScript($workerScript)
      [void]$ps.AddArgument($chunk)
      [void]$ps.AddArgument($Root)
      [void]$ps.AddArgument($PreferOrder)
      [void]$ps.AddArgument($AggressiveJunk)
      [void]$ps.AddArgument($UseDat)
      # BUG-017 FIX: Wrap DatIndex in Synchronized hashtable for thread-safe reads across workers
      [void]$ps.AddArgument(([hashtable]::Synchronized($DatIndex)))
      [void]$ps.AddArgument($DatHashType)
      [void]$ps.AddArgument($DatFallback)
      [void]$ps.AddArgument($AliasEditionKeying)
      [void]$ps.AddArgument($CrcVerifyScan)
      [void]$ps.AddArgument($SevenZipPath)
      [void]$ps.AddArgument($ParentCloneParentMap)
      [void]$ps.AddArgument($ConsoleFilterKeys)
      [void]$ps.AddArgument($PreferredRegionKeys)
      [void]$ps.AddArgument($JpOnlyForSelectedConsoles)
      [void]$ps.AddArgument($JpKeepKeys)
      [void]$ps.AddArgument($HashCache)

      $handle = $ps.BeginInvoke()
      [void]$jobs.Add(@{ PS = $ps; Handle = $handle; Done = $false })
    }

    # Poll loop
    $allItems     = [System.Collections.Generic.List[psobject]]::new()
    $totalCorrupt = 0
    $completed    = 0
    $lastPerfLog  = [System.Diagnostics.Stopwatch]::StartNew()

    while ($completed -lt $jobs.Count) {
      # Cancel propagation
      $isCancelled = $false
      try { $isCancelled = [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false) } catch { }
      if ($isCancelled) { $cancelEvent.Set() }

      Invoke-UiPump

      # Drain log queue
      $msg = $null
      while ($logQueue.TryDequeue([ref]$msg)) { & $Log $msg }

      for ($i = 0; $i -lt $jobs.Count; $i++) {
        $job = $jobs[$i]
        if ($job.Done -or -not $job.Handle.IsCompleted) { continue }
        $job.Done = $true
        $completed++

        try {
          $results = $job.PS.EndInvoke($job.Handle)
          $outcome = $results | Select-Object -Last 1
          if ($outcome.Items) {
            foreach ($it in $outcome.Items) { [void]$allItems.Add($it) }
          }
          $totalCorrupt += [int]$outcome.CorruptCount
        } catch [System.OperationCanceledException] {
          throw
        } catch {
          & $Log ("WARN: Parallel-Worker-Fehler: {0}" -f $_.Exception.Message)
        } finally {
          $job.PS.Dispose()
        }

        & $Log ("  Parallel: Chunk {0}/{1} fertig" -f $completed, $jobs.Count)
      }

      if ($lastPerfLog.ElapsedMilliseconds -ge 1000) {
        $activeWorkers = 0
        $queuedWorkers = 0
        foreach ($job in $jobs) {
          if ($job.Done) { continue }
          $state = $null
          try { $state = [string]$job.PS.InvocationStateInfo.State } catch { }
          if ($state -eq 'Running' -or $state -eq 'Stopping') {
            $activeWorkers++
          } elseif ($state -eq 'NotStarted') {
            $queuedWorkers++
          } elseif (-not $job.Handle.IsCompleted) {
            $activeWorkers++
          }
        }

        $util = if ($maxParallel -gt 0) { [math]::Round((100.0 * $activeWorkers / $maxParallel), 1) } else { 0.0 }
        & $Log ('PERF: ParallelActive={0}/{1} ({2}%) Queue={3} Completed={4}/{5}' -f $activeWorkers, $maxParallel, $util, $queuedWorkers, $completed, $jobs.Count)
        $lastPerfLog.Restart()
      }

      Start-Sleep -Milliseconds 80
    }

    # Final drain
    $msg = $null
    while ($logQueue.TryDequeue([ref]$msg)) { & $Log $msg }

    return @{ Items = $allItems; CorruptCount = $totalCorrupt }

  } finally {
    Stop-RunspaceWorkerJobsShared -Jobs $jobs
    Remove-RunspacePoolResourcesShared -Pool $pool -CancelEvent $cancelEvent
  }
}

# ================================================================
#  Parallel DAT Pre-Hashing (PERF-08)
# ================================================================

function Invoke-PreHashFiles {
  <# Pre-hashes all DAT-hashable files.
     F-11: Uses parallel RunspacePool for local drives.
           Falls back to sequential hashing for UNC paths (\\server\share\...)
           to avoid blocking network I/O with multiple concurrent workers.
     Returns a hashtable (path->hash) or $null if below threshold. #>
  param(
    [System.IO.FileInfo[]]$Files,
    [string]$HashType,
    [string]$SevenZipPath,
    [scriptblock]$Log
  )

  $skipExts = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($e in @('.m3u','.cue','.gdi','.ccd','.mds')) { [void]$skipExts.Add($e) }

  $hashablePaths = [System.Collections.Generic.List[string]]::new()
  $skippedArchiveCount = 0
  foreach ($f in $Files) {
    $ext = [string]$f.Extension
    if ($ext -in @('.zip','.7z')) {
      $skippedArchiveCount++
      continue
    }
    if (-not $skipExts.Contains($ext)) {
      [void]$hashablePaths.Add($f.FullName)
    }
  }
  if ($hashablePaths.Count -lt (Get-ParallelHashThreshold)) { return $null }

  # [F-11] Detect UNC paths -> sequential mode to avoid saturating network I/O.
  $uncCount = ($hashablePaths | Where-Object { $_.StartsWith('\\') }).Count
  $useSequential = ($uncCount -gt ($hashablePaths.Count / 2))

  if ($useSequential) {
    & $Log ("  Pre-Hash (sequential/UNC): {0} Dateien werden sequenziell gehasht." -f $hashablePaths.Count)
    if ($skippedArchiveCount -gt 0) {
      & $Log ("  Pre-Hash: {0} Archive (.zip/.7z) werden hier uebersprungen (on-demand im DAT-Match)." -f $skippedArchiveCount)
    }
    $merged = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    $seqCache = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($p in $hashablePaths) {
      try {
        $isCancelled = $false
        try { $isCancelled = [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false) } catch { }
        if ($isCancelled) { break }
        [void](Get-FileHashCached -Path $p -HashType $HashType -Cache $seqCache)
      } catch { }
    }
    foreach ($kv in $seqCache.GetEnumerator()) { $merged[$kv.Key] = $kv.Value }
    & $Log ("  Pre-Hash (sequential/UNC): {0} Hashes vorberechnet." -f $merged.Count)
    return $merged
  }

  # Parallel mode for local drives.
  $maxParallel = [math]::Min(8, [math]::Max(2, [Environment]::ProcessorCount - 1))
  $logQueue    = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
  $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
  $pool        = $null
  $jobs        = [System.Collections.Generic.List[hashtable]]::new()

  try {
    $pool = New-ClassificationRunspacePool -MaxParallel $maxParallel -CancelEvent $cancelEvent -LogQueue $logQueue

    $chunkSize = [math]::Max(50, [math]::Ceiling($hashablePaths.Count / $maxParallel))
    $chunks = [System.Collections.Generic.List[string[]]]::new()
    for ($i = 0; $i -lt $hashablePaths.Count; $i += $chunkSize) {
      $end = [math]::Min($i + $chunkSize, $hashablePaths.Count)
      $paths = [string[]]::new($end - $i)
      for ($j = $i; $j -lt $end; $j++) { $paths[$j - $i] = $hashablePaths[$j] }
      [void]$chunks.Add($paths)
    }

    & $Log ("  Pre-Hash (parallel/lokal): {0} Dateien in {1} Chunks a ~{2} ({3} Worker)" -f $hashablePaths.Count, $chunks.Count, $chunkSize, $maxParallel)
    if ($skippedArchiveCount -gt 0) {
      & $Log ("  Pre-Hash: {0} Archive (.zip/.7z) werden hier uebersprungen (on-demand im DAT-Match)." -f $skippedArchiveCount)
    }

    $workerScript = {
      param([string[]]$Paths, [string]$HashType, [string]$SevenZipPath)

      # Promote ISS variables to script scope (intentional cross-runspace communication)
      $script:_CancelEvent = $_CancelEvent
      $script:_LogQueue    = $_LogQueue

      $cache = [hashtable]::new($Paths.Count, [StringComparer]::OrdinalIgnoreCase)
      foreach ($p in $Paths) {
        Test-CancelRequested
        $ext = ([string][IO.Path]::GetExtension($p)).ToLowerInvariant()
        if ($ext -notin @('.zip', '.7z')) {
          try {
            [void](Get-FileHashCached -Path $p -HashType $HashType -Cache $cache)
          } catch { }
        }
      }
      return $cache
    }

    foreach ($chunk in $chunks) {
      $ps = [System.Management.Automation.PowerShell]::Create()
      $ps.RunspacePool = $pool
      [void]$ps.AddScript($workerScript)
      [void]$ps.AddArgument($chunk)
      [void]$ps.AddArgument($HashType)
      [void]$ps.AddArgument($SevenZipPath)
      $handle = $ps.BeginInvoke()
      [void]$jobs.Add(@{ PS = $ps; Handle = $handle; Done = $false })
    }

    $merged    = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    $completed = 0
    $maxNoProgressMs = 180000
    $lastProgress = [System.Diagnostics.Stopwatch]::StartNew()
    $lastPerfLog = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false

    while ($completed -lt $jobs.Count) {
      $isCancelled = $false
      try { $isCancelled = [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false) } catch { }
      if ($isCancelled) { $cancelEvent.Set() }

      Invoke-UiPump

      $msg = $null
      while ($logQueue.TryDequeue([ref]$msg)) { & $Log $msg }

      for ($i = 0; $i -lt $jobs.Count; $i++) {
        $job = $jobs[$i]
        if ($job.Done -or -not $job.Handle.IsCompleted) { continue }
        $job.Done = $true
        $completed++

        try {
          $results = $job.PS.EndInvoke($job.Handle)
          $workerCache = $results | Select-Object -Last 1
          if ($workerCache -is [hashtable]) {
            foreach ($kv in $workerCache.GetEnumerator()) {
              $merged[$kv.Key] = $kv.Value
            }
          }
        } catch [System.OperationCanceledException] {
          throw
        } catch {
          & $Log ("WARN: Pre-Hash Worker-Fehler: {0}" -f $_.Exception.Message)
        } finally {
          $job.PS.Dispose()
        }

        & $Log ("  Pre-Hash: Chunk {0}/{1} fertig" -f $completed, $jobs.Count)
        $lastProgress.Restart()
      }

      if ($lastPerfLog.ElapsedMilliseconds -ge 1000) {
        $activeWorkers = 0
        $queuedWorkers = 0
        foreach ($job in $jobs) {
          if ($job.Done) { continue }
          $state = $null
          try { $state = [string]$job.PS.InvocationStateInfo.State } catch { }
          if ($state -eq 'Running' -or $state -eq 'Stopping') {
            $activeWorkers++
          } elseif ($state -eq 'NotStarted') {
            $queuedWorkers++
          } elseif (-not $job.Handle.IsCompleted) {
            $activeWorkers++
          }
        }

        $util = if ($maxParallel -gt 0) { [math]::Round((100.0 * $activeWorkers / $maxParallel), 1) } else { 0.0 }
        & $Log ('PERF: PreHashParallelActive={0}/{1} ({2}%) Queue={3} Completed={4}/{5}' -f $activeWorkers, $maxParallel, $util, $queuedWorkers, $completed, $jobs.Count)
        $lastPerfLog.Restart()
      }

      if ($completed -lt $jobs.Count -and $lastProgress.ElapsedMilliseconds -ge $maxNoProgressMs) {
        $cancelEvent.Set()
        foreach ($job in $jobs) {
          if (-not $job.Done -and $job.PS) {
            try { $job.PS.Stop() } catch { }
          }
        }
        & $Log ("WARN: Pre-Hash ohne Fortschritt > {0} ms. Fallback: fahre ohne restlichen Pre-Hash fort." -f $maxNoProgressMs)
        $timedOut = $true
        break
      }

      Start-Sleep -Milliseconds 80
    }

    $msg = $null
    while ($logQueue.TryDequeue([ref]$msg)) { & $Log $msg }

    if ($timedOut) {
      & $Log ("  Pre-Hash (Fallback): {0} Hashes aus fertigen Chunks übernommen" -f $merged.Count)
    } else {
      & $Log ("  Pre-Hash: {0} Hashes vorberechnet" -f $merged.Count)
    }
    return $merged

  } finally {
    Stop-RunspaceWorkerJobsShared -Jobs $jobs
    Remove-RunspacePoolResourcesShared -Pool $pool -CancelEvent $cancelEvent
  }
}
