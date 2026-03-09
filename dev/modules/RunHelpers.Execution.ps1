# NOTE:
# Extracted from RunHelpers.ps1 to isolate run orchestration execution paths.

function New-OperationResult {
  <# Create a consistent operation result contract for handler orchestration. #>
  param(
    [ValidateSet('ok','completed','skipped','blocked','error','continue')]
    [string]$Status = 'continue',
    [AllowNull()][string]$Reason = $null,
    [AllowNull()][object]$Value = $null,
    [AllowNull()][hashtable]$Meta = $null,
    [AllowNull()][object[]]$Warnings = $null,
    [AllowNull()][hashtable]$Metrics = $null,
    [AllowNull()][hashtable]$Artifacts = $null
  )

  if (-not $Meta) { $Meta = @{} }
  if (-not $Warnings) { $Warnings = @() }
  if (-not $Metrics) { $Metrics = @{} }
  if (-not $Artifacts) { $Artifacts = @{} }
  $statusNormalized = [string]$Status
  $outcome = switch ($statusNormalized) {
    'skipped' { 'SKIP' }
    'blocked' { 'ERROR' }
    'error' { 'ERROR' }
    default { 'OK' }
  }

  return [pscustomobject]@{
    Status = $statusNormalized
    Outcome = $outcome
    Reason = $Reason
    Value = $Value
    Meta = $Meta
    Warnings = @($Warnings)
    Metrics = $Metrics
    Artifacts = $Artifacts
    ShouldReturn = ($statusNormalized -eq 'completed')
  }
}

function Get-StandaloneDiscExtensionSet {
  <# Resolve disc extensions with fallback when Core helper isn't loaded. #>
  if (Get-Command Get-DiscExtensionSet -ErrorAction SilentlyContinue) {
    try { return (Get-DiscExtensionSet) } catch { }
  }

  $set = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  $raw = $null
  try {
    if (Get-Variable -Name ALL_ROM_EXTENSIONS -Scope Script -ErrorAction SilentlyContinue) {
      $raw = [string]$script:ALL_ROM_EXTENSIONS
    }
  } catch { }

  if (-not [string]::IsNullOrWhiteSpace($raw)) {
    foreach ($token in ($raw -split ',')) {
      $ext = [string]$token
      if ([string]::IsNullOrWhiteSpace($ext)) { continue }
      $ext = $ext.Trim().ToLowerInvariant()
      if (-not $ext.StartsWith('.')) { $ext = '.' + $ext }
      [void]$set.Add($ext)
    }
  } else {
    # REF-EXT-01: Disc-Extensions aus zentraler Quelle, plus Container-Formate
    $discExts = $null
    if (Get-Command Get-DiscExtensionSet -ErrorAction SilentlyContinue) {
      $discExts = Get-DiscExtensionSet
    }
    if ($discExts) {
      foreach ($ext in $discExts) { [void]$set.Add($ext) }
    }
    foreach ($ext in @('.zip','.7z')) { [void]$set.Add($ext) }
  }

  return $set
}

function Invoke-RunPreflight {
  <# Run preflight with shared parameters. #>
  param(
    [string[]]$Roots,
    [string[]]$Exts,
    [bool]$UseDat,
    [string]$DatRoot,
    [hashtable]$DatMap,
    [bool]$DoConvert,
    [hashtable]$ToolOverrides,
    [string]$AuditRoot,
    [scriptblock]$Log
  )

  $ok = (Invoke-PreflightRun -Roots $Roots -Exts $Exts -UseDat $UseDat -DatRoot $DatRoot `
    -DatMap $DatMap -DoConvert $DoConvert -ToolOverrides $ToolOverrides -AuditRoot $AuditRoot -Log $Log)
  if ($ok) {
    return (New-OperationResult -Status 'ok' -Reason $null -Value $true -Meta @{ Phase = 'Preflight' })
  }
  return (New-OperationResult -Status 'blocked' -Reason 'preflight-failed' -Value $false -Meta @{ Phase = 'Preflight' })
}

function Invoke-OptionalConsoleSort {
  <# Run console sort before dedupe when enabled. #>
  param(
    [bool]$Enabled,
    [string]$Mode,
    [string[]]$Roots,
    [string[]]$Extensions,
    [bool]$UseDat,
    [string]$DatRoot,
    [string]$DatHashType,
    [hashtable]$DatMap,
    [hashtable]$ToolOverrides,
    [ValidateSet('None','PS1PS2')][string]$ZipSortStrategy = 'None',
    [scriptblock]$Log
  )

  if (-not $Enabled -or $Mode -ne 'Move') {
    # BUG-DD-01: Im DryRun wird ConsoleSort uebersprungen. Hinweis loggen,
    # damit der Benutzer weiss dass das Move-Ergebnis anders aussehen kann.
    if ($Enabled -and $Mode -eq 'DryRun' -and $Log) {
      & $Log 'HINWEIS: Konsolen-Sortierung ist aktiviert, wird aber erst im Move-Modus ausgefuehrt. DryRun-Preview basiert auf der aktuellen Ordnerstruktur.'
    }
    return (New-OperationResult -Status 'skipped' -Reason 'disabled-or-non-move' -Value $null -Meta @{ Phase = 'ConsoleSort' })
  }

  if ($Log) {
    & $Log ""
    & $Log "=== Konsolen-Sortierung (vor Dedupe) ==="
  }

  $sortExts = @($Extensions + @('.chd','.iso','.gcm','.wbfs','.wia','.wbf1','.zip','.7z','.img','.bin','.cue','.gdi','.ccd','.cso','.pbp','.nsp','.xci','.rvz','.gcz')) | Select-Object -Unique
  $sortResult = Invoke-ConsoleSort -Roots $Roots -UseDat $UseDat -DatRoot $DatRoot -DatHashType $DatHashType `
    -DatMap $DatMap -ToolOverrides $ToolOverrides -ZipSortStrategy $ZipSortStrategy -IncludeExtensions $sortExts -Mode $Mode -Log $Log
  if ($sortResult -and $sortResult.UnknownReasons) {
    return (New-OperationResult -Status 'ok' -Reason 'sorted' -Value $sortResult.UnknownReasons -Meta @{ Phase = 'ConsoleSort'; UnknownReasons = @($sortResult.UnknownReasons.Keys).Count })
  }
  return (New-OperationResult -Status 'ok' -Reason 'sorted' -Value $null -Meta @{ Phase = 'ConsoleSort'; UnknownReasons = 0 })
}

function Invoke-ConvertPreviewDryRun {
  <# Log conversion preview when in DryRun mode. #>
  param(
    [bool]$Enabled,
    [string]$Mode,
    [pscustomobject]$Result,
    [scriptblock]$Log
  )

  if (-not $Enabled -or $Mode -ne 'DryRun') {
    return (New-OperationResult -Status 'skipped' -Reason 'disabled-or-non-dryrun' -Value $null -Meta @{ Phase = 'ConvertPreview' })
  }
  if (-not $Result) {
    return (New-OperationResult -Status 'skipped' -Reason 'no-result' -Value $null -Meta @{ Phase = 'ConvertPreview' })
  }

  if ($Log) {
    & $Log ""
    & $Log "=== Format-Konvertierung (DryRun Preview) ==="
  }
  $previewCount = 0
  $previewSkip  = 0
  foreach ($w in $Result.Winners) {
    if (-not $w.MainPath) { continue }
    $wItem = $Result.ItemByMain[$w.MainPath]
    if (-not $wItem) { continue }
    # PERF-02: Console aus Report-Row nutzen statt erneut Get-ConsoleType aufrufen
    $console = if ($w.PSObject.Properties.Name -contains 'Console') { [string]$w.Console } else {
      Get-ConsoleType -RootPath $wItem.Root -FilePath $wItem.MainPath -Extension ([IO.Path]::GetExtension($wItem.MainPath))
    }
    if (-not $script:BEST_FORMAT.ContainsKey($console)) { continue }
    $targetExt = $script:BEST_FORMAT[$console].Ext
    $sourceExt = [IO.Path]::GetExtension($wItem.MainPath).ToLowerInvariant()
    if ($sourceExt -eq $targetExt) { continue }
    if ($wItem.Type -in @('CUESET','GDISET') -and $targetExt -eq '.chd') {
    } elseif ($wItem.Type -eq 'FILE') {
      if ($sourceExt -eq '.chd' -or $sourceExt -eq '.rvz') { continue }
      if (($sourceExt -eq '.zip' -or $sourceExt -eq '.7z') -and $targetExt -eq '.zip') { continue }
    } else {
      $previewSkip++
      continue
    }
    $previewCount++
    if ($Log) { & $Log ("  [{0}] {1} -> {2}: {3}" -f $console, $sourceExt, $targetExt, $wItem.BaseName) }
  }
  if ($Log) {
    & $Log ("{0} Dateien wuerden konvertiert werden." -f $previewCount)
    if ($previewSkip -gt 0) {
      & $Log ("{0} Sets uebersprungen (M3U/CCD nicht direkt konvertierbar)." -f $previewSkip)
    }
  }

  return (New-OperationResult -Status 'completed' -Reason 'preview-generated' -Value $previewCount -Meta @{ Phase = 'ConvertPreview'; PreviewCount = $previewCount; SkippedSets = $previewSkip })
}

function Invoke-WinnerConversionMove {
  <# Convert winners after dedupe in Move mode. #>
  param(
    [bool]$Enabled,
    [string]$Mode,
    [pscustomobject]$Result,
    [string[]]$Roots,
    [hashtable]$ToolOverrides,
    [scriptblock]$Log,
    [scriptblock]$SetQuickPhase,
    [scriptblock]$OnProgress
  )

  if (-not $Enabled -or $Mode -ne 'Move') {
    return (New-OperationResult -Status 'skipped' -Reason 'disabled-or-non-move' -Value $null -Meta @{ Phase = 'WinnerConversion' })
  }
  if (-not $Result) {
    return (New-OperationResult -Status 'skipped' -Reason 'no-result' -Value $null -Meta @{ Phase = 'WinnerConversion' })
  }
  if (-not $Result.AllItems -or @($Result.AllItems).Count -le 0) {
    return (New-OperationResult -Status 'skipped' -Reason 'no-items' -Value 0 -Meta @{ Phase = 'WinnerConversion'; CandidateCount = 0 })
  }

  if ($SetQuickPhase) { & $SetQuickPhase 'Konvertierung' }

  $winnerItems = New-Object System.Collections.Generic.List[psobject]
  foreach ($w in $Result.Winners) {
    $fullItem = if ($w.MainPath) { $Result.ItemByMain[$w.MainPath] } else { $null }
    if ($fullItem) { [void]$winnerItems.Add($fullItem) }
  }
  if ($winnerItems.Count -gt 0) {
    Invoke-FormatConversion -Winners $winnerItems -Roots $Roots -Log $Log -ToolOverrides $ToolOverrides -OnProgress $OnProgress
    return (New-OperationResult -Status 'completed' -Reason 'conversion-invoked' -Value $winnerItems.Count -Meta @{ Phase = 'WinnerConversion'; CandidateCount = $winnerItems.Count })
  }

  return (New-OperationResult -Status 'completed' -Reason 'no-convertable-winners' -Value 0 -Meta @{ Phase = 'WinnerConversion'; CandidateCount = 0 })
}

function Get-StandaloneConversionPreview {
  <# Build a conversion preview and enforce optional root allowlist. #>
  param(
    [string[]]$Roots,
    [string[]]$AllowedRoots,
    [int]$PreviewLimit = 12,
    [scriptblock]$Log
  )

  $allowedRootSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  if ($AllowedRoots) {
    foreach ($entry in $AllowedRoots) {
      $normalized = Resolve-NormalizedPath -Path $entry
      if ($normalized) { [void]$allowedRootSet.Add($normalized) }
    }
  }

  $discExts = Get-StandaloneDiscExtensionSet

  $previewItems = New-Object System.Collections.Generic.List[psobject]
  $blockedRoots = New-Object System.Collections.Generic.List[string]
  $acceptedRoots = New-Object System.Collections.Generic.List[string]
  $scannedFilesByRoot = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $candidateCount = 0

  foreach ($root in @($Roots)) {
    $normalizedRoot = Resolve-NormalizedPath -Path $root
    if (-not $normalizedRoot) { continue }

    if ($allowedRootSet.Count -gt 0 -and -not $allowedRootSet.Contains($normalizedRoot)) {
      [void]$blockedRoots.Add($root)
      if ($Log) { & $Log ("  SKIP (nicht in Allowlist): {0}" -f $root) }
      continue
    }
    [void]$acceptedRoots.Add($root)

    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    $allFiles = @(Get-FilesSafe -Root $root -AllowedExtensions $discExts -Responsive)
    if ($allFiles.Count -eq 0) {
      try {
        $probeItems = @(Get-ChildItem -LiteralPath $root -Force -ErrorAction Stop | Select-Object -First 1)
        if ($probeItems.Count -eq 0 -and $Log) {
          & $Log ("  HINWEIS: Root leer oder keine direkt lesbaren Einträge: {0}" -f $root)
        }
      } catch {
        if ($Log) {
          $hint = 'Prüfe Berechtigung/Verfügbarkeit (insb. Netzlaufwerk oder gemapptes Laufwerk im Admin-Kontext).'
          & $Log ("  WARN: Root nicht lesbar: {0} ({1}) - {2}" -f $root, $_.Exception.Message, $hint)
        }
      }
    }
    $scanList = New-Object System.Collections.Generic.List[string]
    foreach ($f in $allFiles) {
      if ($f -and $f.FullName) { [void]$scanList.Add([string]$f.FullName) }
      $ext = $f.Extension.ToLowerInvariant()
      if ($ext -in @('.cue','.gdi','.ccd')) { continue }
      $candidateCount++
      if ($previewItems.Count -lt $PreviewLimit) {
        [void]$previewItems.Add([pscustomobject]@{
          Root = $root
          Name = $f.Name
          Extension = $ext
          FullPath = $f.FullName
        })
      }
    }
    $scannedFilesByRoot[$normalizedRoot] = @($scanList)
  }

  return [pscustomobject]@{
    CandidateCount = $candidateCount
    PreviewItems = @($previewItems)
    AcceptedRoots = @($acceptedRoots)
    BlockedRoots = @($blockedRoots)
    ScannedFilesByRoot = $scannedFilesByRoot
  }
}

function Invoke-StandaloneConversion {
  <# Converts all ROM files in roots to target format without dedupe first. #>
  param(
    [string[]]$Roots,
    [string[]]$AllowedRoots,
    [hashtable]$PreScannedFilesByRoot,
    [hashtable]$ToolOverrides,
    [scriptblock]$Log,
    [scriptblock]$SetQuickPhase,
    [scriptblock]$OnProgress
  )

  if (-not $Roots -or $Roots.Count -eq 0) {
    & $Log "Keine ROM-Ordner angegeben."
    return
  }

  Reset-ArchiveEntryCache
  Reset-ClassificationCaches

  $allowedRootSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  if ($AllowedRoots) {
    foreach ($entry in $AllowedRoots) {
      $normalized = Resolve-NormalizedPath -Path $entry
      if ($normalized) { [void]$allowedRootSet.Add($normalized) }
    }
  }

  if ($SetQuickPhase) { & $SetQuickPhase 'Konvertierung (Scan)' }
  & $Log ""
  & $Log "=== Standalone-Konvertierung ==="
  & $Log ("Scanne {0} Ordner..." -f $Roots.Count)

  $discExts = Get-StandaloneDiscExtensionSet

  $items = New-Object System.Collections.Generic.List[psobject]
  $setMemberPaths = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)

  foreach ($root in $Roots) {
    Test-CancelRequested
    $normalizedRoot = Resolve-NormalizedPath -Path $root
    if ($allowedRootSet.Count -gt 0 -and (-not $normalizedRoot -or -not $allowedRootSet.Contains($normalizedRoot))) {
      & $Log ("  SKIP (Root nicht in Allowlist): {0}" -f $root)
      continue
    }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
      & $Log ("  SKIP (Ordner existiert nicht): {0}" -f $root)
      continue
    }

    $allFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    $usedPreScanned = $false
    if ($PreScannedFilesByRoot) {
      $preScanKey = $normalizedRoot
      if (-not [string]::IsNullOrWhiteSpace($preScanKey) -and $PreScannedFilesByRoot.ContainsKey($preScanKey)) {
        $preScannedPaths = @($PreScannedFilesByRoot[$preScanKey])
        foreach ($path in $preScannedPaths) {
          if ([string]::IsNullOrWhiteSpace([string]$path)) { continue }
          try {
            $fileItem = Get-Item -LiteralPath ([string]$path) -ErrorAction Stop
            if ($fileItem -and -not $fileItem.PSIsContainer) {
              $allFiles.Add([System.IO.FileInfo]$fileItem)
            }
          } catch {
          }
        }
        $usedPreScanned = $true
      }
    }

    if (-not $usedPreScanned) {
      $allFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new([System.IO.FileInfo[]]@(Get-FilesSafe -Root $root -AllowedExtensions $discExts -Responsive))
    }
    if ($allFiles.Count -eq 0) {
      try {
        $probeItems = @(Get-ChildItem -LiteralPath $root -Force -ErrorAction Stop | Select-Object -First 1)
        if ($probeItems.Count -eq 0) {
          & $Log ("  HINWEIS: Root leer oder keine direkt lesbaren Einträge: {0}" -f $root)
        }
      } catch {
        $hint = 'Prüfe Berechtigung/Verfügbarkeit (insb. Netzlaufwerk oder gemapptes Laufwerk im Admin-Kontext).'
        & $Log ("  WARN: Root nicht lesbar: {0} ({1}) - {2}" -f $root, $_.Exception.Message, $hint)
      }
    }
    & $Log ("  {0}: {1} Dateien gefunden" -f $root, $allFiles.Count)

    $byExt = @{}
    foreach ($f in $allFiles) {
      $ext = $f.Extension.ToLowerInvariant()
      if (-not $byExt.ContainsKey($ext)) { $byExt[$ext] = [System.Collections.Generic.List[System.IO.FileInfo]]::new() }
      [void]$byExt[$ext].Add($f)
    }

    if ($byExt.ContainsKey('.cue')) {
      foreach ($cue in $byExt['.cue']) {
        Test-CancelRequested
        if ($setMemberPaths.Contains($cue.FullName)) { continue }
        $related = @(Get-CueRelatedFiles -CuePath $cue.FullName -RootPath $root)
        foreach ($r in $related) { [void]$setMemberPaths.Add($r) }
        $size = [long]0; foreach ($r in $related) { try { $size += (Get-Item -LiteralPath $r -ErrorAction SilentlyContinue).Length } catch {} }
        [void]$items.Add([pscustomobject]@{
          MainPath  = $cue.FullName
          Root      = $root
          Type      = 'CUESET'
          Paths     = $related
          SizeBytes = $size
          BaseName  = [IO.Path]::GetFileNameWithoutExtension($cue.Name)
        })
      }
    }

    if ($byExt.ContainsKey('.gdi')) {
      foreach ($gdi in $byExt['.gdi']) {
        Test-CancelRequested
        if ($setMemberPaths.Contains($gdi.FullName)) { continue }
        $related = @(Get-GdiRelatedFiles -GdiPath $gdi.FullName -RootPath $root)
        foreach ($r in $related) { [void]$setMemberPaths.Add($r) }
        $size = [long]0; foreach ($r in $related) { try { $size += (Get-Item -LiteralPath $r -ErrorAction SilentlyContinue).Length } catch {} }
        [void]$items.Add([pscustomobject]@{
          MainPath  = $gdi.FullName
          Root      = $root
          Type      = 'GDISET'
          Paths     = $related
          SizeBytes = $size
          BaseName  = [IO.Path]::GetFileNameWithoutExtension($gdi.Name)
        })
      }
    }

    foreach ($f in $allFiles) {
      Test-CancelRequested
      if ($setMemberPaths.Contains($f.FullName)) { continue }
      $ext = $f.Extension.ToLowerInvariant()
      if ($ext -in @('.cue','.gdi','.ccd')) { continue }
      [void]$items.Add([pscustomobject]@{
        MainPath  = $f.FullName
        Root      = $root
        Type      = 'FILE'
        Paths     = @($f.FullName)
        SizeBytes = $f.Length
        BaseName  = [IO.Path]::GetFileNameWithoutExtension($f.Name)
      })
    }
  }

  & $Log ("{0} konvertierbare Elemente gefunden ({1} CUE/GDI Sets, {2} Einzeldateien)" -f `
    $items.Count,
    @($items | Where-Object { $_.Type -in @('CUESET','GDISET') }).Count,
    @($items | Where-Object { $_.Type -eq 'FILE' }).Count)

  if ($items.Count -eq 0) {
    & $Log "Nichts zu konvertieren."
    return
  }

  if ($SetQuickPhase) { & $SetQuickPhase 'Konvertierung' }
  Invoke-FormatConversion -Winners $items -Roots $Roots -Log $Log -ToolOverrides $ToolOverrides -OnProgress $OnProgress
}

function Invoke-MovePhase {
  <# Execute move phase and return early result when needed. #>
  param(
    [Parameter(Mandatory=$true)][System.Collections.Generic.List[psobject]]$Report,
    [System.Collections.Generic.List[psobject]]$JunkItems,
    [System.Collections.Generic.List[psobject]]$BiosItems,
    [System.Collections.Generic.List[psobject]]$AllItems,
    [hashtable]$ItemByMain,
    [string[]]$Roots,
    [string]$TrashRoot,
    [string]$AuditRoot,
    [bool]$RequireConfirmMove,
    [scriptblock]$ConfirmMove,
    [string]$CsvPath,
    [string]$HtmlPath,
    [int]$TotalDupes,
    [double]$SavedMB,
    [bool]$IncludeConversionPreview = $false,
    [scriptblock]$Log
  )

  $moveSw = [System.Diagnostics.Stopwatch]::StartNew()
  try {
    $getMovePaths = {
      param(
        [AllowNull()][object]$Item,
        [AllowNull()][string]$FallbackMainPath
      )

      $paths = New-Object System.Collections.Generic.List[string]
      if ($Item) {
        $hasPathsProp = $Item.PSObject.Properties.Name -contains 'Paths'
        if ($hasPathsProp -and $Item.Paths) {
          foreach ($candidate in @($Item.Paths)) {
            $candidatePath = [string]$candidate
            if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
              [void]$paths.Add($candidatePath)
            }
          }
        }

        if ($paths.Count -eq 0) {
          $mainPath = [string]$Item.MainPath
          if (-not [string]::IsNullOrWhiteSpace($mainPath)) {
            [void]$paths.Add($mainPath)
          }
        }
      }

      if ($paths.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($FallbackMainPath)) {
        [void]$paths.Add([string]$FallbackMainPath)
      }

      return @($paths)
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $auditByRoot = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    $auditPaths = New-Object System.Collections.Generic.List[string]
    $unknownRootAuditWarned = $false

    # BUG RUN-001 FIX: Determine audit root early so incremental flushes can write there
    $auditRootPathResolved = $AuditRoot
    if ([string]::IsNullOrWhiteSpace($auditRootPathResolved)) {
      $auditRootPathResolved = Join-Path (Get-Location) 'audit-logs'
    }
    if (-not (Test-Path -LiteralPath $auditRootPathResolved)) {
      try { Assert-DirectoryExists -Path $auditRootPathResolved } catch {}
    }
    if (-not (Test-Path -LiteralPath $auditRootPathResolved)) {
      $auditRootPathResolved = Get-Location
    }
    # Track partial audit file paths per root for incremental flush
    $auditPartialPaths = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    $auditFlushedCounts = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

    $addAudit = {
      param([string]$rootPath, [string]$action, [string]$source, [string]$dest)
      $key = $rootPath
      if ([string]::IsNullOrWhiteSpace($key)) {
        $key = '__UNKNOWN_ROOT__'
        if (-not $unknownRootAuditWarned) {
          if ($Log) { & $Log "WARN: Audit ohne gueltigen Root-Pfad erkannt. Verwende 'unknown-root' fuer Audit-Dateiname." }
          $unknownRootAuditWarned = $true
        }
      }
      if (-not $auditByRoot.ContainsKey($key)) {
        $auditByRoot[$key] = New-Object System.Collections.Generic.List[psobject]
      }
      $size = $null
      if (Test-Path -LiteralPath $source) {
        try { $size = (Get-Item -LiteralPath $source -ErrorAction SilentlyContinue).Length } catch {}
      } elseif (Test-Path -LiteralPath $dest) {
        try { $size = (Get-Item -LiteralPath $dest -ErrorAction SilentlyContinue).Length } catch {}
      }
      [void]$auditByRoot[$key].Add([pscustomobject]@{
        Time      = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        Action    = $action
        Source    = $source
        Dest      = $dest
        SizeBytes = $size
      })

      # BUG RUN-001 FIX: Incremental flush every 50 entries to prevent data loss on crash
      $totalInRoot = $auditByRoot[$key].Count
      $flushed = if ($auditFlushedCounts.ContainsKey($key)) { $auditFlushedCounts[$key] } else { 0 }
      $unflushed = $totalInRoot - $flushed
      if ($unflushed -ge 50) {
        try {
          $rootLeaf = if ([string]::IsNullOrWhiteSpace($key) -or $key -eq '__UNKNOWN_ROOT__') { 'unknown-root' } else { Split-Path -Leaf $key }
          # BUG RUN-013 FIX: Append hash of full root path to avoid filename collisions
          $rootHash = [Math]::Abs(([string]$key).GetHashCode()).ToString('X8')
          $rootName = ConvertTo-SafeFileName -Name ('{0}-{1}' -f $rootLeaf, $rootHash)
          $partialPath = Join-Path $auditRootPathResolved ("rom-move-audit-{0}-{1}.partial.csv" -f $timestamp, $rootName)
          $auditPartialPaths[$key] = $partialPath
          $newRows = @($auditByRoot[$key])[$flushed..($totalInRoot - 1)]
          if ($flushed -eq 0) {
            # First flush: write with header
            $newRows |
              Select-Object Time, Action,
                            @{N='Source';E={ConvertTo-SafeCsvValue $_.Source}},
                            @{N='Dest';E={ConvertTo-SafeCsvValue $_.Dest}},
                            SizeBytes |
              Export-Csv -NoTypeInformation -Encoding UTF8 -Path $partialPath
          } else {
            # Append without header
            $newRows |
              Select-Object Time, Action,
                            @{N='Source';E={ConvertTo-SafeCsvValue $_.Source}},
                            @{N='Dest';E={ConvertTo-SafeCsvValue $_.Dest}},
                            SizeBytes |
              ConvertTo-Csv -NoTypeInformation |
              Select-Object -Skip 1 |
              Add-Content -Path $partialPath -Encoding UTF8
          }
          $auditFlushedCounts[$key] = $totalInRoot
        } catch {
          # Incremental flush is best-effort; final write will still happen
        }
      }
    }

    $toTrash = @($Report | Where-Object { $_.Action -in @("MOVE","JUNK") })
    $toBios  = @($Report | Where-Object { $_.Action -eq "BIOS-MOVE" })
    $movePlanPath = $null
    $movePlanRows = New-Object System.Collections.Generic.List[psobject]

    if ($toTrash.Count -eq 0 -and $toBios.Count -eq 0) {
      if ($Log) {
        & $Log ""
        & $Log "Nichts zu verschieben - Set ist bereits sauber!"
      }
      return (New-OperationResult -Status 'completed' -Reason 'no-move-required' -Value ([pscustomobject]@{
        CsvPath    = $CsvPath
        HtmlPath   = $HtmlPath
        Winners    = @($Report | Where-Object { $_.Action -eq 'KEEP' -and $_.Category -eq 'GAME' })
        AllItems   = $AllItems
        ItemByMain = $ItemByMain
      }) -Meta @{ Phase = 'Move'; Moved = 0 })
    }

    # F-06 FIX: Pre-flight audit directory write-test.
    # Before any file is moved, verify that the audit log directory is writable.
    # If the test fails, the move is aborted - no files are touched.
    # This guarantees that a rollback CSV will always exist after a successful Move.
    $auditRootPrecheck = $AuditRoot
    if ([string]::IsNullOrWhiteSpace($auditRootPrecheck)) {
      $auditRootPrecheck = Join-Path (Get-Location) 'audit-logs'
    }
    try {
      if (-not (Test-Path -LiteralPath $auditRootPrecheck)) {
        [void](New-Item -ItemType Directory -Path $auditRootPrecheck -Force -ErrorAction Stop)
      }
      $probeFile = Join-Path $auditRootPrecheck ('.audit-write-probe-{0}.tmp' -f [System.Guid]::NewGuid().ToString('N'))
      [System.IO.File]::WriteAllText($probeFile, 'probe')
      [System.IO.File]::Delete($probeFile)
    } catch {
      $auditBlockMsg = ('F-06: Audit-Verzeichnis nicht beschreibbar [{0}]: {1}. Move wird abgebrochen - kein Rollback moeglich ohne Audit-CSV.' -f $auditRootPrecheck, $_.Exception.Message)
      if ($Log) { & $Log $auditBlockMsg }
      if (Get-Command Write-SecurityAuditEvent -ErrorAction SilentlyContinue) {
        Write-SecurityAuditEvent -Domain 'Move' -Action 'Blocked' -Actor 'MoveEngine' -Target ([string]$auditRootPrecheck) -Outcome 'Deny' -Detail $auditBlockMsg -Source 'Invoke-WinnerConversionMove' -Severity 'High'
      }
      Add-RunError -Category 'AuditPrecheck' -Message $auditBlockMsg
      throw [System.InvalidOperationException]::new($auditBlockMsg)
    }

    $previewRows = New-Object System.Collections.Generic.List[psobject]

    # BUG-MV-04: Blocklist bereits im Plan-Build laden, damit Preview
    # keine Eintraege zeigt die beim Move blockiert werden.
    $planBlocklist = @()
    if (Get-Command Get-MovePathBlocklist -ErrorAction SilentlyContinue) {
      $planBlocklist = @(Get-MovePathBlocklist)
    }

      foreach ($row in $toTrash) {
        if (-not $row.MainPath) { continue }
        $item = $ItemByMain[$row.MainPath]
        if (-not $item) { continue }
        $root = [string]$item.Root
        $rootNorm = Resolve-RootPath -Path $root
        $trashBase = if ([string]::IsNullOrWhiteSpace($TrashRoot)) {
          Join-Path $root "_TRASH_REGION_DEDUPE"
        } else { $TrashRoot }

        if ($row.Category -eq "JUNK") {
          $trashBase = Join-Path $trashBase "_JUNK"
        }

        foreach ($p in (& $getMovePaths -Item $item -FallbackMainPath ([string]$row.MainPath))) {
          if (-not $p -or -not (Test-Path -LiteralPath $p)) { continue }
          $relative = Get-RelativePathSafe -Path $p -Root $rootNorm
          if ([string]::IsNullOrWhiteSpace($relative)) { continue }
          $dest = Join-Path $trashBase $relative
          # BUG-MV-04: Blocklist-Check im Plan-Build (mit Root-Exemption wie im Move-Loop)
          if ($planBlocklist.Count -gt 0 -and (Get-Command Test-PathBlockedByBlocklist -ErrorAction SilentlyContinue)) {
            $isExempt = $false
            try { $isExempt = Test-PathWithinRoot -Path $dest -Root $root -DisallowReparsePoints } catch { $isExempt = $false }
            if ((-not $isExempt) -and -not [string]::IsNullOrWhiteSpace($TrashRoot)) {
              try { $isExempt = Test-PathWithinRoot -Path $dest -Root $TrashRoot -DisallowReparsePoints } catch { $isExempt = $false }
            }
            if ((-not $isExempt) -and (Test-PathBlockedByBlocklist -Path $dest -Blocklist $planBlocklist)) { continue }
          }
          $size = $null
          try { $size = (Get-Item -LiteralPath $p -ErrorAction SilentlyContinue).Length } catch { }

          [void]$movePlanRows.Add([pscustomobject]@{
            Action = [string]$row.Action
            Source = [string]$p
            Dest = [string]$dest
            SizeBytes = $size
          })
        }
      }

      foreach ($row in $toBios) {
        if (-not $row.MainPath) { continue }
        $item = $ItemByMain[$row.MainPath]
        if (-not $item) { continue }
        $root = [string]$item.Root
        $rootNorm = Resolve-RootPath -Path $root
        $biosBase = Join-Path $root "_BIOS"

        foreach ($p in (& $getMovePaths -Item $item -FallbackMainPath ([string]$row.MainPath))) {
          if (-not $p -or -not (Test-Path -LiteralPath $p)) { continue }
          $relative = Get-RelativePathSafe -Path $p -Root $rootNorm
          if ([string]::IsNullOrWhiteSpace($relative)) { continue }
          $dest = Join-Path $biosBase $relative
          # BUG-MV-04: Blocklist-Check im Plan-Build (mit Root-Exemption wie im Move-Loop)
          if ($planBlocklist.Count -gt 0 -and (Get-Command Test-PathBlockedByBlocklist -ErrorAction SilentlyContinue)) {
            $isExempt = $false
            try { $isExempt = Test-PathWithinRoot -Path $dest -Root $root -DisallowReparsePoints } catch { $isExempt = $false }
            if ((-not $isExempt) -and (Test-PathBlockedByBlocklist -Path $dest -Blocklist $planBlocklist)) { continue }
          }
          $size = $null
          try { $size = (Get-Item -LiteralPath $p -ErrorAction SilentlyContinue).Length } catch { }

          [void]$movePlanRows.Add([pscustomobject]@{
            Action = 'BIOS-MOVE'
            Source = [string]$p
            Dest = [string]$dest
            SizeBytes = $size
          })
        }
      }

      foreach ($planRow in @($movePlanRows)) {
        [void]$previewRows.Add([pscustomobject]@{
          Action = [string]$planRow.Action
          Source = [string]$planRow.Source
          Target = [string]$planRow.Dest
        })
      }

      if ($IncludeConversionPreview) {
        $winners = @($Report | Where-Object { $_.Action -eq 'KEEP' -and $_.Category -eq 'GAME' })
        foreach ($winner in $winners) {
          if (-not $winner.MainPath) { continue }
          $item = $ItemByMain[$winner.MainPath]
          if (-not $item) { continue }

          # PERF-02: Console aus Report-Row nutzen statt erneut Get-ConsoleType aufrufen
          $console = if ($winner.PSObject.Properties.Name -contains 'Console') { [string]$winner.Console } else {
            Get-ConsoleType -RootPath $item.Root -FilePath $item.MainPath -Extension ([IO.Path]::GetExtension($item.MainPath))
          }
          if (-not $script:BEST_FORMAT.ContainsKey($console)) { continue }
          $targetExt = [string]$script:BEST_FORMAT[$console].Ext
          $sourceExt = [IO.Path]::GetExtension($item.MainPath).ToLowerInvariant()
          if ($sourceExt -eq $targetExt) { continue }

          if ($item.Type -in @('CUESET','GDISET') -and $targetExt -eq '.chd') {
          } elseif ($item.Type -eq 'FILE') {
            if ($sourceExt -eq '.chd' -or $sourceExt -eq '.rvz') { continue }
            if (($sourceExt -eq '.zip' -or $sourceExt -eq '.7z') -and $targetExt -eq '.zip') { continue }
          } else {
            continue
          }

          [void]$previewRows.Add([pscustomobject]@{
            Action = 'CONVERT'
            Source = [string]$item.MainPath
            Target = [IO.Path]::ChangeExtension([string]$item.MainPath, $targetExt)
          })
        }
      }

      $previewTotal = $previewRows.Count
      $previewRowsTrimmed = @($previewRows | Select-Object -First 20)

      $reportsDir = Join-Path (Get-Location) 'reports'
      try { Assert-DirectoryExists -Path $reportsDir } catch { }
      if (Test-Path -LiteralPath $reportsDir) {
        $movePlanPath = Join-Path $reportsDir ('move-plan-{0}.json' -f $timestamp)
        $movePlanPayload = [ordered]@{
          schemaVersion = 'move-plan-v1'
          generatedAtUtc = [DateTime]::UtcNow.ToString('o')
          mode = 'Move'
          rows = @($movePlanRows)
        }
        try {
          Write-JsonFile -Path $movePlanPath -Data $movePlanPayload -Depth 8
          if ($Log) { & $Log ("Move-Plan: {0}" -f $movePlanPath) }
        } catch {
          $movePlanPath = $null
          if ($Log) { & $Log ("WARN: Move-Plan konnte nicht geschrieben werden: {0}" -f $_.Exception.Message) }
        }
      }

      $summary = [pscustomobject]@{
        TrashCount = $toTrash.Count
        BiosCount  = $toBios.Count
        JunkCount  = $JunkItems.Count
        DupeCount  = $TotalDupes
        SavedMB    = $SavedMB
        ReportCsv  = $CsvPath
        ReportHtml = $HtmlPath
        MovePlanPath = $movePlanPath
        PreviewRows = $previewRowsTrimmed
        PreviewTotal = $previewTotal
        PreviewTruncated = ($previewTotal -gt $previewRowsTrimmed.Count)
      }
      if ($RequireConfirmMove) {
        $confirmOk = $true
        if ($ConfirmMove) {
          $confirmOk = & $ConfirmMove $summary
        } else {
          if ($Log) {
            & $Log 'WARNUNG: RequireConfirmMove aktiv, aber kein ConfirmMove-Callback bereitgestellt. Move wird aus Sicherheitsgruenden abgebrochen.'
          }
          $confirmOk = $false
        }
        if (-not $confirmOk) {
          if ($Log) { & $Log "Abbruch vor Move: Benutzer hat abgelehnt." }
          return (New-OperationResult -Status 'completed' -Reason 'user-declined-confirm-move' -Value ([pscustomobject]@{
            CsvPath    = $CsvPath
            HtmlPath   = $HtmlPath
            MovePlanPath = $movePlanPath
            Winners    = @($Report | Where-Object { $_.Action -eq 'KEEP' -and $_.Category -eq 'GAME' })
            AllItems   = $AllItems
            ItemByMain = $ItemByMain
          }) -Meta @{ Phase = 'Move'; Confirmed = $false })
        }
      }

    $moveBlocklist = @()
    if (Get-Command Get-MovePathBlocklist -ErrorAction SilentlyContinue) {
      $moveBlocklist = @(Get-MovePathBlocklist)
    }

    $moveCount = 0
    if ($toTrash.Count -gt 0) {
      if ($Log) {
        & $Log ""
        & $Log ("Verschiebe {0} Eintraege nach _TRASH..." -f $toTrash.Count)
      }

      foreach ($row in $toTrash) {
        Test-CancelRequested
        if (-not $row.MainPath) { continue }
        $it = $ItemByMain[$row.MainPath]
        if (-not $it) { continue }
        $moveCount++

        $root = $it.Root
        $rootNorm = Resolve-RootPath -Path $root
        $trashBase = if ([string]::IsNullOrWhiteSpace($TrashRoot)) {
          Join-Path $root "_TRASH_REGION_DEDUPE"
        } else { $TrashRoot }

        if ($row.Category -eq "JUNK") {
          $trashBase = Join-Path $trashBase "_JUNK"
        }

        # BUG RUN-002+010 FIX: Track moved files per set for atomic rollback on partial failure
        $setMovedPairs = [System.Collections.Generic.List[pscustomobject]]::new()
        $setFailed = $false

        foreach ($p in (& $getMovePaths -Item $it -FallbackMainPath ([string]$row.MainPath))) {
          if (-not (Test-Path -LiteralPath $p)) { continue }
          $relative = Get-RelativePathSafe -Path $p -Root $rootNorm
          if ([string]::IsNullOrWhiteSpace($relative)) {
            if ($Log) { & $Log ("WARNUNG: Move ausserhalb Root uebersprungen: {0}" -f $p) }
            continue
          }
          $dest = Join-Path $trashBase $relative
          if (Get-Command Test-PathBlockedByBlocklist -ErrorAction SilentlyContinue) {
            $isExempt = $false
            try { $isExempt = Test-PathWithinRoot -Path $dest -Root $root -DisallowReparsePoints } catch { $isExempt = $false }
            if ((-not $isExempt) -and -not [string]::IsNullOrWhiteSpace($TrashRoot)) {
              try { $isExempt = Test-PathWithinRoot -Path $dest -Root $TrashRoot -DisallowReparsePoints } catch { $isExempt = $false }
            }
            if ((-not $isExempt) -and (Test-PathBlockedByBlocklist -Path $dest -Blocklist $moveBlocklist)) {
              $blockedMsg = ("F-Blocklist: Zielpfad blockiert (Trash): {0}" -f $dest)
              if ($Log) { & $Log ("WARNUNG: {0}" -f $blockedMsg) }
              if (Get-Command Write-SecurityAuditEvent -ErrorAction SilentlyContinue) {
                Write-SecurityAuditEvent -Domain 'Move' -Action 'Blocked' -Actor 'MoveEngine' -Target ([string]$dest) -Outcome 'Deny' -Detail $blockedMsg -Source 'Invoke-WinnerConversionMove' -Severity 'High'
              }
              Add-RunError -Category 'MoveBlockedPath' -Message $blockedMsg
              continue
            }
          }
          try {
            $final = Invoke-RootSafeMove -Source $p -Dest $dest -SourceRoot $root -DestRoot $trashBase
            [void]$setMovedPairs.Add([pscustomobject]@{ Source = $p; Dest = $final })
            & $addAudit $root $row.Action $p $final
          } catch {
            $setFailed = $true
            if ($Log) { & $Log ("FEHLER: Move fehlgeschlagen: {0} ({1})" -f $p, $_.Exception.Message) }
            Add-RunError -Category 'Move' -Message ('{0} ({1})' -f $p, $_.Exception.Message)
          }
        }

        # BUG RUN-010 FIX: Rollback previously moved files if any file in the set failed
        if ($setFailed -and $setMovedPairs.Count -gt 0) {
          if ($Log) { & $Log ('  Rollback: {0} Dateien im Set zurueckverschieben.' -f $setMovedPairs.Count) }
          foreach ($pair in $setMovedPairs) {
            try {
              if (Test-Path -LiteralPath $pair.Dest) {
                Move-Item -LiteralPath $pair.Dest -Destination $pair.Source -Force -ErrorAction Stop
              }
            } catch {
              if ($Log) { & $Log ("  WARNUNG: Rollback fehlgeschlagen: {0} ({1})" -f $pair.Dest, $_.Exception.Message) }
            }
          }
        }

        if ($moveCount % 10 -eq 0) {
          $movePct = if ($toTrash.Count -gt 0) { [math]::Round(($moveCount / $toTrash.Count) * 100, 1) } else { 100 }
          $moveFileName = try { [System.IO.Path]::GetFileName([string]$row.MainPath) } catch { '–' }
          if ($Log) { & $Log ("  ... {0}/{1} ({2}%) – {3}" -f $moveCount, $toTrash.Count, $movePct, $moveFileName) }
        }
      }
      if ($Log) { & $Log ("{0} Eintraege in _TRASH verschoben." -f $moveCount) }

      foreach ($root in $Roots) {
        Test-CancelRequested
        Remove-EmptyDirectories -Path $root
      }
    }

    if ($toBios.Count -gt 0) {
      if ($Log) {
        & $Log ""
        & $Log ("Verschiebe {0} BIOS/Firmware in _BIOS..." -f $toBios.Count)
      }
      $biosCount = 0

      foreach ($row in $toBios) {
        Test-CancelRequested
        if (-not $row.MainPath) { continue }
        $it = $ItemByMain[$row.MainPath]
        if (-not $it) { continue }
        $biosCount++

        $root = $it.Root
        $rootNorm = Resolve-RootPath -Path $root
        $biosBase = Join-Path $root "_BIOS"

        foreach ($p in (& $getMovePaths -Item $it -FallbackMainPath ([string]$row.MainPath))) {
          if (-not (Test-Path -LiteralPath $p)) { continue }
          $relative = Get-RelativePathSafe -Path $p -Root $rootNorm
          if ([string]::IsNullOrWhiteSpace($relative)) {
            if ($Log) { & $Log ("WARNUNG: Move ausserhalb Root uebersprungen: {0}" -f $p) }
            continue
          }
          $dest = Join-Path $biosBase $relative
          if (Get-Command Test-PathBlockedByBlocklist -ErrorAction SilentlyContinue) {
            $isExempt = $false
            try { $isExempt = Test-PathWithinRoot -Path $dest -Root $root -DisallowReparsePoints } catch { $isExempt = $false }
            if ((-not $isExempt) -and (Test-PathBlockedByBlocklist -Path $dest -Blocklist $moveBlocklist)) {
              $blockedMsg = ("F-Blocklist: Zielpfad blockiert (BIOS): {0}" -f $dest)
              if ($Log) { & $Log ("WARNUNG: {0}" -f $blockedMsg) }
              if (Get-Command Write-SecurityAuditEvent -ErrorAction SilentlyContinue) {
                Write-SecurityAuditEvent -Domain 'Move' -Action 'Blocked' -Actor 'MoveEngine' -Target ([string]$dest) -Outcome 'Deny' -Detail $blockedMsg -Source 'Invoke-WinnerConversionMove' -Severity 'High'
              }
              Add-RunError -Category 'MoveBlockedPath' -Message $blockedMsg
              continue
            }
          }
          try {
            $final = Invoke-RootSafeMove -Source $p -Dest $dest -SourceRoot $root -DestRoot $root
            & $addAudit $root "BIOS-MOVE" $p $final
          } catch {
            if ($Log) { & $Log ("FEHLER: Move fehlgeschlagen: {0} ({1})" -f $p, $_.Exception.Message) }
            Add-RunError -Category 'BIOS-Move' -Message ('{0} ({1})' -f $p, $_.Exception.Message)
          }
        }
      }
      if ($Log) { & $Log ("{0} BIOS/Firmware in _BIOS verschoben." -f $biosCount) }
    }

    if ($auditByRoot.Count -gt 0) {
      # BUG RUN-001 FIX: Use the already-resolved audit root (determined at start for incremental flush)
      foreach ($rootKey in $auditByRoot.Keys) {
        $rootLeaf = if ([string]::IsNullOrWhiteSpace($rootKey) -or $rootKey -eq '__UNKNOWN_ROOT__') {
          'unknown-root'
        } else {
          Split-Path -Leaf $rootKey
        }
        # BUG RUN-013 FIX: Append hash of full root path to avoid filename collisions
        $rootHash = [Math]::Abs(([string]$rootKey).GetHashCode()).ToString('X8')
        $rootName = ConvertTo-SafeFileName -Name ('{0}-{1}' -f $rootLeaf, $rootHash)
        $auditPath = Join-Path $auditRootPathResolved ("rom-move-audit-{0}-{1}.csv" -f $timestamp, $rootName)

        $auditRows = @($auditByRoot[$rootKey])
        $metricRows = New-Object System.Collections.Generic.List[psobject]
        $metricTime = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        $actionCounts = @($auditRows | Group-Object -Property Action)
        foreach ($group in $actionCounts) {
          [void]$metricRows.Add([pscustomobject]@{
            Time = $metricTime
            Action = 'PERF-METRIC'
            Source = ('metric:ActionCount:{0}' -f [string]$group.Name)
            Dest = [string]$group.Count
            SizeBytes = $null
          })
        }

        [void]$metricRows.Add([pscustomobject]@{
          Time = $metricTime
          Action = 'PERF-METRIC'
          Source = 'metric:MoveDurationSeconds'
          Dest = ('{0:N3}' -f $moveSw.Elapsed.TotalSeconds)
          SizeBytes = $null
        })

        if (Get-Command Get-OperationCorrelationId -ErrorAction SilentlyContinue) {
          $correlationId = [string](Get-OperationCorrelationId)
          if (-not [string]::IsNullOrWhiteSpace($correlationId)) {
            [void]$metricRows.Add([pscustomobject]@{
              Time = $metricTime
              Action = 'PERF-METRIC'
              Source = 'metric:CorrelationId'
              Dest = $correlationId
              SizeBytes = $null
            })
          }
        }

        $auditRowsWithMetrics = @($auditRows + @($metricRows))

        $auditRowsWithMetrics | 
          Select-Object Time, Action,
                        @{N='Source';E={ConvertTo-SafeCsvValue $_.Source}},
                        @{N='Dest';E={ConvertTo-SafeCsvValue $_.Dest}},
                        SizeBytes |
          Export-Csv -NoTypeInformation -Encoding UTF8 -Path $auditPath

        [void](Write-AuditMetadataSidecar -AuditCsvPath $auditPath -Rows $auditRowsWithMetrics -Log $Log)
        [void]$auditPaths.Add($auditPath)
        if ($Log) { & $Log ("Audit: {0}" -f $auditPath) }

        # BUG RUN-001 FIX: Remove the incremental partial file now that the final signed CSV exists
        if ($auditPartialPaths.ContainsKey($rootKey)) {
          $partialFile = $auditPartialPaths[$rootKey]
          if ($partialFile -and (Test-Path -LiteralPath $partialFile)) {
            Remove-Item -LiteralPath $partialFile -Force -ErrorAction SilentlyContinue
          }
        }
      }

      Add-AuditLinksToHtml -HtmlPath $HtmlPath -AuditPaths $auditPaths
    }

    if ($Log) {
      & $Log ""
      & $Log "Fertig. Alles verschoben (nichts geloescht)."
    }
  } finally {
    $moveSw.Stop()
    if ($Log) { & $Log ("{0:N1}s Move-Zeit" -f $moveSw.Elapsed.TotalSeconds) }
  }

  return (New-OperationResult -Status 'continue' -Reason 'move-finished' -Value @{ CsvPath = $CsvPath; MoveCount = $moveCount } -Meta @{ Phase = 'Move' })
}
