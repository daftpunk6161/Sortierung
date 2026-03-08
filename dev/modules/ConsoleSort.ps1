function Invoke-ConsoleSort {
  <# Sortiert ROM-Dateien in Konsolen-Unterordner.
     Fixes:
       - DryRun-Modus (BUG-13)
       - .wia/.wbf1/.nkit.gcz DolphinTool-Support (BUG-03/04)
       - Set-aware Moves: CUE/BIN, GDI, CCD, M3U als Einheit (BUG-10)
       - DAT Hash-Kollision Logging (BUG-14)
       - Console-Key Validation gegen Path-Traversal (SEC-03)
     Refactoring:
       - REF-07: Detection via Invoke-ConsoleDetection entkoppelt
       - REF-01: Strukturiertes DetectionResult
       - REF-12: UNKNOWN-Diagnose via DiagInfo
  #>
  param(
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [bool]$UseDat,
    [string]$DatRoot,
    [ValidateSet('SHA1','MD5','CRC32')][string]$DatHashType = 'SHA1',
    [hashtable]$DatMap,
    [hashtable]$ToolOverrides,
    [ValidateSet('None','PS1PS2')][string]$ZipSortStrategy = 'None',
    [string[]]$IncludeExtensions = @('.chd'),
    [ValidateSet('DryRun','Move')][string]$Mode = 'Move',
    [scriptblock]$Log
  )

  # BUG-MV-03: Null-Guard fuer $Log - verhindert Fehler wenn kein Log-ScriptBlock uebergeben
  if (-not $Log) { $Log = { } }

  $dryRun = ($Mode -eq 'DryRun')
  $datIndex = @{}
  $hashCache = $null
  if (Get-Command Reset-DatArchiveStats -ErrorAction SilentlyContinue) { Reset-DatArchiveStats }
  Reset-ArchiveEntryCache
  Reset-ClassificationCaches
  $sevenZipPath = $null
  $use7z = $false
  $dolphinToolPath = $null
  $useDolphin = $false

  # Console-Key Whitelist Regex - nur gueltige Keys erlauben (SEC-03)
  $consoleKeyRx = [regex]::new('^[A-Z0-9_-]+$')

  if ($dryRun -and $Log) {
    & $Log 'Konsolen-Sortierung: DryRun-Modus - keine Dateien werden verschoben.'
  }

  if ($UseDat -and -not [string]::IsNullOrWhiteSpace($DatRoot)) {
    & $Log ("DAT: lade aus {0} (Hash {1})" -f $DatRoot, $DatHashType)
    $datIndex = Get-DatIndex -DatRoot $DatRoot -HashType $DatHashType -ConsoleMap $DatMap -Log $Log
    $hashCache = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  } elseif ($UseDat) {
    & $Log "DAT: kein Ordner gesetzt, DAT deaktiviert."
    $UseDat = $false
  }

  $datHashToConsole = $null
  if ($UseDat -and $datIndex.Count -gt 0) {
    $datHashToConsole = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($consoleKey in $datIndex.Keys) {
      foreach ($hashKey in $datIndex[$consoleKey].Keys) {
        if (-not $datHashToConsole.ContainsKey($hashKey)) {
          $datHashToConsole[$hashKey] = $consoleKey
        } else {
          # BUG-14: DAT Hash-Kollision loggen
          $existingConsole = [string]$datHashToConsole[$hashKey]
          if ($existingConsole -ne $consoleKey -and $Log) {
            & $Log ("  WARNUNG DAT: Hash {0} existiert in {1} UND {2} - behalte {1}" -f $hashKey, $existingConsole, $consoleKey)
          }
        }
      }
    }
    if ($Log) { & $Log ("DAT: Reverse-Index aufgebaut ({0} Hashes)" -f $datHashToConsole.Count) }
  }

  if ($ToolOverrides -and $ToolOverrides.ContainsKey('7z')) {
    $sevenZipPath = [string]$ToolOverrides['7z']
    if ([string]::IsNullOrWhiteSpace($sevenZipPath)) { $sevenZipPath = $null }
  }
  if (-not $sevenZipPath) {
    $sevenZipPath = Find-ConversionTool -ToolName '7z'
  }
  if ($sevenZipPath -and (Test-Path -LiteralPath $sevenZipPath)) { $use7z = $true }

  if ($ToolOverrides -and $ToolOverrides.ContainsKey('dolphintool')) {
    $dolphinToolPath = [string]$ToolOverrides['dolphintool']
    if ([string]::IsNullOrWhiteSpace($dolphinToolPath)) { $dolphinToolPath = $null }
  }
  if (-not $dolphinToolPath) {
    $dolphinToolPath = Find-ConversionTool -ToolName 'dolphintool'
  }
  if ($dolphinToolPath -and (Test-Path -LiteralPath $dolphinToolPath)) { $useDolphin = $true }

    if ($ZipSortStrategy -eq 'PS1PS2') {
      if ($Log) {
        & $Log '=== ZIP-basierte PS1/PS2-Erkennung in Konsolen-Sortierung integriert ==='
      }
    }

  $total = 0
  $moved = 0
  $skipped = 0
  $unknown = 0
  $setMembersMoved = 0
  $unknownReasons = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

  # Extensions fuer DolphinTool-Erkennung (BUG-03: .wia/.wbf1 hinzugefuegt)
  $dolphinExts = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($de in @('.gcz', '.rvz', '.wbfs', '.wia', '.wbf1')) { [void]$dolphinExts.Add($de) }

  $sortedRoots = @($Roots | Sort-Object)
  $rootTotal = $sortedRoots.Count
  $rootIndex = 0
  foreach ($root in $sortedRoots) {
    $rootIndex++
    Test-CancelRequested
    if (-not (Test-Path -LiteralPath $root)) {
      & $Log "WARNUNG: Root nicht gefunden: $root"
      continue
    }

    & $Log ("Sortiere Spiele nach Konsole [{0}/{1}]: {2}" -f $rootIndex, $rootTotal, $root)

    $rootBase = Resolve-RootPath -Path $root
    $rootBaseLen = $rootBase.Length
    $excludePaths = @(
      (Join-Path $rootBase '_TRASH_REGION_DEDUPE') + '\',
      (Join-Path $rootBase '_BIOS') + '\',
      (Join-Path $rootBase '_JUNK') + '\'
    )

    $extSet = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    foreach ($e in $IncludeExtensions) {
      if (-not [string]::IsNullOrWhiteSpace($e)) {
        $extSet.Add($e.ToLowerInvariant()) | Out-Null
      }
    }

    $files = @(Get-FilesSafe -Root $rootBase -ExcludePrefixes $excludePaths -AllowedExtensions $extSet | Sort-Object FullName)

    & $Log ("  -> {0} Dateien gefunden" -f $files.Count)

    # --- BUG-10: Set-aware Moves - vorab Set-Zugehoerigkeiten ermitteln ---
    # Dependent-Dateien (z.B. BINs einer CUE) werden mit ihrem Primary verschoben.
    $setDependents = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $setPrimaryToMembers = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

    $hasSetParsing = [bool](Get-Command Get-CueRelatedFiles -ErrorAction SilentlyContinue)
    if ($hasSetParsing) {
      foreach ($f in $files) {
        $setExt = $f.Extension.ToLowerInvariant()
        $related = $null
        try {
          switch ($setExt) {
            '.cue' { $related = @(Get-CueRelatedFiles -CuePath $f.FullName -RootPath $rootBase) }
            '.gdi' { $related = @(Get-GdiRelatedFiles -GdiPath $f.FullName -RootPath $rootBase) }
            '.ccd' { $related = @(Get-CcdRelatedFiles -CcdPath $f.FullName -RootPath $rootBase) }
            '.m3u' { $related = @(Get-M3URelatedFiles -M3UPath $f.FullName -RootPath $rootBase) }
            '.mds' { $related = @(Get-MdsRelatedFiles -MdsPath $f.FullName -RootPath $rootBase) }
          }
        } catch { $related = $null }

        if ($related -and $related.Count -gt 1) {
          $members = @($related | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            -not $_.Equals($f.FullName, [StringComparison]::OrdinalIgnoreCase)
          })
          if ($members.Count -gt 0) {
            $setPrimaryToMembers[$f.FullName] = $members
            foreach ($member in $members) {
              [void]$setDependents.Add($member)
            }
          }
        }
      }
      if ($setPrimaryToMembers.Count -gt 0 -and $Log) {
        & $Log ("  Set-Erkennung: {0} Sets mit {1} Dateien" -f $setPrimaryToMembers.Count, $setDependents.Count)
      }
    }
    # --- Ende Set-Erkennung ---

    $fileTotal = $files.Count
    $fileIndex = 0
    $hasNormExt = [bool](Get-Command Get-NormalizedExtension -ErrorAction SilentlyContinue)
    foreach ($f in $files) {
      Test-CancelRequested
      $fileIndex++

      # BUG-10: Skip dependent files - they'll be moved with their primary
      if ($setDependents.Contains($f.FullName)) {
        continue
      }

      $total++
      if (($fileIndex -eq 1) -or ($fileIndex -eq $fileTotal) -or ($fileIndex % 250 -eq 0)) {
        $pct = if ($fileTotal -gt 0) { [math]::Round(($fileIndex / $fileTotal) * 100, 1) } else { 100 }
        & $Log ("  Konsolen-Sort Fortschritt: {0}/{1} ({2}%)" -f $fileIndex, $fileTotal, $pct)
      }

      $ext = $f.Extension.ToLowerInvariant()

      # BUG-04: Double-Extension erkennen (.nkit.iso, .nkit.gcz)
      $normExt = $ext
      if ($hasNormExt) {
        $normExt = Get-NormalizedExtension -FileName $f.Name
      }

      # --- REF-07: All detection via Invoke-ConsoleDetection ---
      $detection = Invoke-ConsoleDetection `
        -FilePath $f.FullName `
        -RootPath $rootBase `
        -Extension $f.Extension `
        -NormalizedExtension $normExt `
        -UseDat $UseDat `
        -DatHashToConsole $datHashToConsole `
        -DatHashType $DatHashType `
        -HashCache $hashCache `
        -Use7z $use7z `
        -SevenZipPath $sevenZipPath `
        -UseDolphin $useDolphin `
        -DolphinToolPath $dolphinToolPath `
        -DolphinExts $dolphinExts `
        -ZipSortStrategy $ZipSortStrategy

      $console = $detection.Console

      if (-not $console -or $console -eq 'UNKNOWN') {
        $console = 'UNKNOWN'
        $unknown++
        $unknownReasonCode = $detection.DiagInfo['REASON_CODE']
        if ($unknownReasonCode) {
          if (-not $unknownReasons.ContainsKey($unknownReasonCode)) { $unknownReasons[$unknownReasonCode] = 0 }
          $unknownReasons[$unknownReasonCode]++
          $unknownReasonLabel = Get-ConsoleUnknownReasonLabel -Code $unknownReasonCode
          # REF-12: Vollstaendige Diagnose ausgeben
          $diagText = $detection.DiagInfo['DIAGNOSIS']
          if ($diagText) {
            & $Log ("  UNBEKANNT: {0} ({1} / {2}) -- {3}" -f $f.Name, $unknownReasonCode, $unknownReasonLabel, $diagText)
          } else {
            & $Log ("  UNBEKANNT: {0} ({1} / {2})" -f $f.Name, $unknownReasonCode, $unknownReasonLabel)
          }
        }
      }

      # SEC-03: Console-Key gegen Whitelist validieren
      if (-not $consoleKeyRx.IsMatch($console)) {
        & $Log ("  FEHLER: ungueltige Console-ID '{0}' fuer {1} - uebersprungen" -f $console, $f.Name)
        $skipped++
        continue
      }

      $rel = $f.FullName.Substring($rootBaseLen).TrimStart('\','/')
      if ($rel.StartsWith($console + '\', [StringComparison]::OrdinalIgnoreCase)) {
        # BUG-MV-02: Datei bereits im richtigen Ordner — nur skipped zaehlen, nicht auch unknown
        if ($console -eq 'UNKNOWN') { $unknown-- }
        $skipped++
        continue
      }

      # --- Move-Phase ---
      if ($dryRun) {
        $dest = Join-Path (Join-Path $rootBase $console) $rel
        & $Log ("  [DryRun] {0} -> {1}" -f $f.Name, $console)
        $moved++
      } else {
        $dest = Join-Path (Join-Path $rootBase $console) $rel
        try {
          $final = Invoke-RootSafeMove -Source $f.FullName -Dest $dest -SourceRoot $rootBase -DestRoot $rootBase
          if ($final) { $moved++ }
        } catch {
          $skipped++
          & $Log ("FEHLER Move fehlgeschlagen: {0} ({1})" -f $f.FullName, $_.Exception.Message)
        }
      }

      # --- BUG-10: Set-Members mitschieben ---
      if ($setPrimaryToMembers.ContainsKey($f.FullName)) {
        foreach ($memberPath in $setPrimaryToMembers[$f.FullName]) {
          if (-not (Test-Path -LiteralPath $memberPath)) {
            # BUG-MV-05: Set-Member nicht mehr vorhanden (ggf. von anderem Primary verschoben)
            & $Log ("WARNUNG: Set-Member nicht gefunden (evtl. Referenz-Konflikt): {0}" -f $memberPath)
            continue
          }
          $memberRel = $memberPath.Substring($rootBaseLen).TrimStart('\','/')
          $memberDest = Join-Path (Join-Path $rootBase $console) $memberRel
          if ($dryRun) {
            & $Log ("  [DryRun] Set-Member: {0} -> {1}" -f [IO.Path]::GetFileName($memberPath), $console)
            $setMembersMoved++
          } else {
            try {
              $memberFinal = Invoke-RootSafeMove -Source $memberPath -Dest $memberDest -SourceRoot $rootBase -DestRoot $rootBase
              if ($memberFinal) { $setMembersMoved++ }
            } catch {
              & $Log ("FEHLER Set-Member Move: {0} ({1})" -f $memberPath, $_.Exception.Message)
            }
          }
        }
      }
    }
  }

  & $Log ("Konsolen-Sortierung: {0} verschoben (+{1} Set-Members), {2} übersprungen, {3} unbekannt" -f $moved, $setMembersMoved, $skipped, $unknown)
  if ($unknownReasons.Count -gt 0) {
    & $Log "Unbekannte Gruende:"
    foreach ($k in ($unknownReasons.Keys | Sort-Object)) {
      $label = Get-ConsoleUnknownReasonLabel -Code $k
      & $Log ("  - {0} ({1}): {2}" -f $k, $label, $unknownReasons[$k])
    }
  }
  # BUG-MV-02: Counter-Invariante sicherstellen
  $counterSum = $moved + $skipped + $unknown
  if ($counterSum -ne $total) {
    & $Log ("WARNUNG: Counter-Invariante verletzt: Total={0} != Moved+Skipped+Unknown={1}" -f $total, $counterSum)
  }
  & $Log ("Status: ConsoleSort Total={0}, Moved={1}, SetMembers={2}, Skipped={3}, Unknown={4}" -f $total, $moved, $setMembersMoved, $skipped, $unknown)
  return [pscustomobject]@{ Total = $total; Moved = $moved; SetMembersMoved = $setMembersMoved; Skipped = $skipped; Unknown = $unknown; UnknownReasons = $unknownReasons }
}
