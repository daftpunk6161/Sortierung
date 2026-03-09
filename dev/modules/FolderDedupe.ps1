# ================================================================
#  FOLDER DEDUPE  (folder-level deduplication)
# ================================================================
# Consolidated module for all folder-level deduplication strategies:
#
# 1. Base-name matching (DOS, AMIGA, PC-98, etc.)
#    Normalises folder names, picks winner by newest file timestamp,
#    moves losers to a trash/dupe directory.
#
# 2. PS3 hash-based matching
#    Hashes PS3 key files (PS3_DISC.SFB, PARAM.SFO, EBOOT.BIN)
#    to detect true duplicates regardless of folder name.
#
# Invoke-AutoFolderDedupe auto-detects the console type per root
# and dispatches to the appropriate strategy.
#
# Dependencies: FileOps.ps1 (Invoke-RootSafeMove, Initialize-Directory,
#               Resolve-RootPath, Test-PathWithinRoot)
#               Classification.ps1 (CONSOLE_FOLDER_MAP)
# ================================================================

# Console keys that use folder-based game storage (each folder = one game).
# PS3 has its own hash-based dedupe; these use base-name matching.
$script:FOLDER_DEDUPE_CONSOLE_KEYS = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@('DOS', 'AMIGA', 'CD32', 'C64', 'PC98', 'X68K', 'MSX',
  'ATARIST', 'ZX', 'CPC', 'FMTOWNS',
  'XBOX', 'X360',
  '3DO', 'CDI') | ForEach-Object { [void]$script:FOLDER_DEDUPE_CONSOLE_KEYS.Add($_) }

# Console keys that use PS3-style hash-based dedupe
$script:PS3_DEDUPE_CONSOLE_KEYS = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$script:PS3_DEDUPE_CONSOLE_KEYS.Add('PS3')

# ================================================================
#  PS3 HASH-BASED DEDUPE (consolidated from Ps3Dedupe.ps1)
# ================================================================

function Get-PS3FolderHash {
  <# Hash PS3 game folder based on key files (PS3_DISC.SFB, PARAM.SFO, EBOOT.BIN). #>
  param(
    [Parameter(Mandatory=$true)][string]$Folder,
    [string[]]$ImportantFiles
  )

  if (-not $ImportantFiles -or $ImportantFiles.Count -eq 0) {
    $ImportantFiles = @('PS3_DISC.SFB', 'PARAM.SFO', 'EBOOT.BIN')
  }

  $files = @(Get-ChildItem -LiteralPath $Folder -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $ImportantFiles -contains $_.Name } |
    Sort-Object FullName)

  if ($files.Count -eq 0) { return $null }

  $md5 = [System.Security.Cryptography.MD5]::Create()
  try {
    $buffer = New-Object byte[] 8192
    foreach ($f in $files) {
      $stream = [IO.File]::OpenRead($f.FullName)
      try {
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
          [void]$md5.TransformBlock($buffer, 0, $read, $buffer, 0)
        }
      } finally {
        $stream.Dispose()
      }
    }
    [void]$md5.TransformFinalBlock($buffer, 0, 0)
    return ($md5.Hash | ForEach-Object { $_.ToString('x2') }) -join ''
  } finally {
    $md5.Dispose()
  }
}

function Test-Ps3MultidiscFolder {
  <# Detects common multi-disc markers in PS3 folder names. #>
  param([string]$FolderName)

  if ([string]::IsNullOrWhiteSpace($FolderName)) { return $false }
  $name = [string]$FolderName

  return ($name -match '(?i)(\[\s*disc\s*\d+\s*\]|\(\s*disc\s*\d+\s*\)|\bdisc\s*\d+\b|\bcd\s*\d+\b|\bdisk\s*\d+\b)')
}

function Group-Ps3TitleFolders {
  <# Groups PS3 folder-like items by normalized title (without disc markers). #>
  param([object[]]$Items)

  if (-not $Items -or @($Items).Count -eq 0) { return @() }

  $prepared = @(
    foreach ($item in @($Items)) {
      if (-not $item) { continue }
      $name = [string]$item.Name
      if ([string]::IsNullOrWhiteSpace($name)) { continue }

      $normalized = $name -replace '(?i)(\[\s*disc\s*\d+\s*\]|\(\s*disc\s*\d+\s*\)|\bdisc\s*\d+\b|\bcd\s*\d+\b|\bdisk\s*\d+\b)', ''
      $normalized = ($normalized -replace '\s+', ' ').Trim()
      if ([string]::IsNullOrWhiteSpace($normalized)) { $normalized = $name.Trim() }

      [pscustomobject]@{
        Key = $normalized.ToLowerInvariant()
        Title = $normalized
        Item = $item
      }
    }
  )

  if (@($prepared).Count -eq 0) { return @() }

  $result = @()
  foreach ($group in @($prepared | Group-Object -Property Key)) {
    $rows = @($group.Group)
    $result += [pscustomobject]@{
      Title = [string]$rows[0].Title
      Items = @($rows | ForEach-Object { $_.Item })
      Count = @($rows).Count
    }
  }

  return ,@($result)
}

function Get-Ps3DupeScore {
  <# Returns a score where higher means more likely the original/preferred item. #>
  param([Parameter(Mandatory=$true)][psobject]$Item)

  if (-not $Item) { return 0 }

  $score = 0
  $type = ([string]$Item.Type).Trim().ToLowerInvariant()
  if ($type -eq 'original') { $score += 100 }
  elseif ($type -eq 'backup') { $score += 10 }
  else { $score += 50 }

  $discCount = 0
  try { $discCount = [int]$Item.DiscCount } catch { $discCount = 0 }
  if ($discCount -gt 0) { $score += $discCount }

  return [int]$score
}

function Invoke-PS3FolderDedupe {
  <# Find PS3 duplicate folders by hashing key files and move duplicates. #>
  param(
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [string]$DupeRoot,
    [scriptblock]$Log
  )

  $total = 0
  $dupes = 0
  $moved = 0
  $skipped = 0

  foreach ($root in $Roots) {
    Test-CancelRequested
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
      if ($Log) { & $Log "WARNUNG: Root nicht gefunden: $root" }
      continue
    }

    $dupeBase = if ([string]::IsNullOrWhiteSpace($DupeRoot)) {
      Join-Path $root 'PS3_DUPES'
    } else {
      Join-Path $DupeRoot (Split-Path -Leaf $root)
    }
    # BUG FOLDERDEDUPE-007 FIX: Normalize dupeBase path to match FullName format from Get-ChildItem
    $dupeBase = [System.IO.Path]::GetFullPath($dupeBase)
    Initialize-Directory $dupeBase

    $folders = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
      Where-Object { [System.IO.Path]::GetFullPath($_.FullName) -ne $dupeBase })

    if ($Log) { & $Log ("PS3 Scan: {0} Ordner in {1}" -f $folders.Count, $root) }

    $hashes = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    $idx = 0
    foreach ($folder in $folders) {
      Test-CancelRequested
      $idx++
      $total++
      if ($Log) { & $Log ("  [{0}/{1}] {2}" -f $idx, $folders.Count, $folder.Name) }
      $hash = Get-PS3FolderHash -Folder $folder.FullName
      if (-not $hash) {
        $skipped++
        if ($Log) { & $Log "    ÜBERSPRUNGEN (keine PS3-Schlüsseldateien gefunden)" }
        continue
      }
      if ($hashes.ContainsKey($hash)) {
        $dupes++
        # BUG FOLDERDEDUPE-005 FIX: Quality comparison instead of first-seen-wins.
        # Prefer folder with more files, then alphabetically for determinism.
        $existingPath = $hashes[$hash]
        $existingCount = @(Get-ChildItem -LiteralPath $existingPath -Recurse -File -ErrorAction SilentlyContinue).Count
        $newCount      = @(Get-ChildItem -LiteralPath $folder.FullName -Recurse -File -ErrorAction SilentlyContinue).Count
        $loserPath = $folder.FullName
        if ($newCount -gt $existingCount -or
           ($newCount -eq $existingCount -and [string]::Compare($folder.FullName, $existingPath, [StringComparison]::OrdinalIgnoreCase) -lt 0)) {
          # New folder wins — swap: move the previous winner to dupes
          $loserPath = $existingPath
          $hashes[$hash] = $folder.FullName
        }
        $loserName = Split-Path -Leaf $loserPath
        $dest = Join-Path $dupeBase $loserName
        try {
          $final = Invoke-RootSafeMove -Source $loserPath -Dest $dest -SourceRoot $root -DestRoot $dupeBase
          if ($final) { $moved++ }
          if ($Log) { & $Log ("    DUP -> {0}" -f $final) }
        } catch {
          if ($Log) { & $Log ("    FEHLER Move fehlgeschlagen: {0}" -f $_.Exception.Message) }
        }
      } else {
        $hashes[$hash] = $folder.FullName
      }
    }
  }

  if ($Log) {
    & $Log ("PS3 Dedupe: {0} gescannt, {1} Duplikate, {2} verschoben, {3} übersprungen" -f $total, $dupes, $moved, $skipped)
  }
  return [pscustomobject]@{ Total = $total; Dupes = $dupes; Moved = $moved; Skipped = $skipped }
}

function Invoke-Ps3Dedupe {
  <# Compatibility wrapper with plugin integration for PS3 folder deduplication. #>
  param(
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [string]$DupeRoot,
    [scriptblock]$Log
  )

  return (Invoke-WithPluginOverride -Phase 'ps3-dedupe' -Context @{
    Roots    = @($Roots)
    DupeRoot = $DupeRoot
    Log      = $Log
  } -CoreAction {
    Invoke-PS3FolderDedupe -Roots $Roots -DupeRoot $DupeRoot -Log $Log
  } -Log $Log)
}

# ================================================================
#  BASE-NAME FOLDER DEDUPE
# ================================================================

function Get-FolderBaseKey {
  <# Normalise a folder name into a grouping key for deduplication.
     Strips parenthetical suffixes, bracket tags, version numbers,
     and collapses whitespace.  Returns lower-case invariant string.

     BUG FOLDERDEDUPE-002 FIX: Preserves disk/disc/side markers so
     multi-disk games are NOT grouped as duplicates.
     BUG FOLDERDEDUPE-003 FIX: Preserves platform-variant tags
     (AGA/ECS/OCS/NTSC/PAL) so distinct chipset versions are kept. #>
  param(
    [Parameter(Mandatory=$true)]
    [string]$FolderName
  )

  if ([string]::IsNullOrWhiteSpace($FolderName)) { return '' }

  $base = [string]$FolderName

  # BUG FOLDERDEDUPE-006 FIX: Unicode normalization — fold accented characters
  # (e.g. e vs e, u vs u) so equivalent folder names produce the same key.
  if (Get-Command ConvertTo-AsciiFold -ErrorAction SilentlyContinue) {
    $base = ConvertTo-AsciiFold -Text $base
  } else {
    $base = $base.Normalize([System.Text.NormalizationForm]::FormC)
  }

  # Tags that must be PRESERVED in the key (not stripped):
  # - Disk/Disc/CD/Side markers (multi-disk games)
  # - Platform variants (AGA/ECS/OCS for Amiga, NTSC/PAL video standards)
  # Regex: match parenthetical content that does NOT contain a preserve-worthy token
  $preservePattern = '(?:Disk|Disc|CD|Side)\s*[\dA-Z]|AGA|ECS|OCS|NTSC|PAL|WHDLoad|ADF'

  # BUG FOLDERDEDUPE-001 FIX: Strip ALL parenthetical groups that are NOT preserve-worthy.
  # Previous code used a trailing-only while loop with $ anchor that stopped at the first
  # preserved tag, leaving non-preserved tags before it.  Now uses a single-pass approach:
  # collect preserved tags, strip all parens, re-append preserved ones.
  $preservedTags = [System.Collections.Generic.List[string]]::new()
  foreach ($m in [regex]::Matches($base, '\([^)]*\)')) {
    if ($m.Value -match $preservePattern) {
      [void]$preservedTags.Add($m.Value)
    }
  }
  $base = [regex]::Replace($base, '\s*\([^)]*\)', '')
  if ($preservedTags.Count -gt 0) {
    $base = $base.TrimEnd() + ' ' + ($preservedTags -join ' ')
  }

  # Strip ALL trailing bracket groups iteratively
  while ($base -match '\s*\[[^\]]*\]\s*$') {
    $base = $base -replace '\s*\[[^\]]*\]\s*$', ''
  }

  # Strip common version-like suffixes: "Game v1.2", "Game 1.0"
  $base = $base -replace '\s+v?\d+(\.\d+)+\s*$', ''

  # Collapse multiple spaces and trim
  $base = ($base -replace '\s{2,}', ' ').Trim()

  if ([string]::IsNullOrWhiteSpace($base)) { return $FolderName.Trim().ToLowerInvariant() }

  return $base.ToLowerInvariant()
}

function Get-NewestFileTimestamp {
  <# Returns the newest LastWriteTimeUtc of any file inside a directory (recursive).
     Falls back to directory's own timestamp when the folder is empty. #>
  param(
    [Parameter(Mandatory=$true)]
    [System.IO.DirectoryInfo]$Directory
  )

  $newest = $null
  try {
    # Use EnumerateFiles for memory efficiency on large trees
    $enumerator = $Directory.EnumerateFiles('*', [System.IO.SearchOption]::AllDirectories)
    foreach ($file in $enumerator) {
      if ($null -eq $newest -or $file.LastWriteTimeUtc -gt $newest) {
        $newest = $file.LastWriteTimeUtc
      }
    }
  } catch {
    # Permission or I/O errors - ignore and fall back
  }

  if ($null -ne $newest) { return [datetime]$newest }
  return [datetime]$Directory.LastWriteTimeUtc
}

function Get-FolderFileCount {
  <# Returns the number of files in a directory (recursive). #>
  param(
    [Parameter(Mandatory=$true)]
    [System.IO.DirectoryInfo]$Directory
  )

  $count = 0
  try {
    $enumerator = $Directory.EnumerateFiles('*', [System.IO.SearchOption]::AllDirectories)
    foreach ($_ in $enumerator) { $count++ }
  } catch { }
  return [int]$count
}

function Invoke-FolderDedupeByBaseName {
  <# Deduplicate first-level sub-folders under $Roots by normalised base name.
     For each group of duplicates the folder containing the newest file wins;
     all others are moved to $DupeRoot (or <root>\_FOLDER_DUPES by default).

     Mode 'DryRun' reports what would happen without touching the file system.
     Mode 'Move' actually relocates loser folders. #>
  param(
    [Parameter(Mandatory=$true)]
    [string[]]$Roots,

    [string]$DupeRoot,

    [ValidateSet('DryRun','Move')]
    [string]$Mode = 'DryRun',

    [scriptblock]$Log
  )

  $totalFolders   = 0
  $dupeGroups     = 0
  $movedFolders   = 0
  $skippedFolders = 0
  $errorCount     = 0
  $actions        = [System.Collections.Generic.List[psobject]]::new()

  foreach ($rootPath in $Roots) {
    # Cancel support
    if (Get-Command Test-CancelRequested -ErrorAction SilentlyContinue) {
      Test-CancelRequested
    }

    # Validate root
    if ([string]::IsNullOrWhiteSpace($rootPath)) { continue }
    $normalizedRoot = Resolve-RootPath -Path $rootPath
    if (-not $normalizedRoot -or -not (Test-Path -LiteralPath $normalizedRoot -PathType Container)) {
      if ($Log) { & $Log "WARNING: Root not found or not a directory: $rootPath" }
      continue
    }

    # Determine dupe destination
    $dupeBase = if ([string]::IsNullOrWhiteSpace($DupeRoot)) {
      Join-Path $normalizedRoot '_FOLDER_DUPES'
    } else {
      $DupeRoot -replace '/', '\'
    }
    $dupeBaseFull = Resolve-RootPath -Path $dupeBase

    # Ensure dupe directory exists (even in DryRun for validation)
    if ($Mode -eq 'Move') {
      Initialize-Directory $dupeBaseFull
    }

    # Enumerate first-level directories, excluding the dupe target itself
    $folders = @(
      Get-ChildItem -LiteralPath $normalizedRoot -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object {
          $p = Resolve-RootPath -Path $_.FullName
          $p -ne $dupeBaseFull
        }
    )

    if ($null -eq $folders -or $folders.Count -eq 0) {
      if ($Log) { & $Log "No sub-folders found in: $normalizedRoot" }
      continue
    }

    $totalFolders += $folders.Count
    if ($Log) { & $Log ("Folder-Dedupe scan: {0} folders in {1}" -f $folders.Count, $normalizedRoot) }

    # Group by normalised base key
    $groups = $folders | Group-Object { Get-FolderBaseKey -FolderName $_.Name }

    foreach ($group in $groups) {
      if (Get-Command Test-CancelRequested -ErrorAction SilentlyContinue) {
        Test-CancelRequested
      }

      if ([string]::IsNullOrWhiteSpace($group.Name)) { continue }
      if ($group.Count -le 1) { continue }

      $dupeGroups++

      # Build candidate list with metadata for winner selection
      $candidates = [System.Collections.Generic.List[psobject]]::new()
      foreach ($dir in $group.Group) {
        $newest   = Get-NewestFileTimestamp -Directory $dir
        $fileCount = Get-FolderFileCount -Directory $dir
        $candidates.Add([pscustomobject]@{
          Dir       = $dir
          Newest    = $newest
          FileCount = $fileCount
        })
      }

      # Winner: populated folders beat empty ones (FOLDERDEDUPE-004), then most recent file,
      # then most files, then shortest name (tie-break)
      $sorted = $candidates |
        Sort-Object @{Expression={ if ($_.FileCount -gt 0) {1} else {0} };Descending=$true},
                    @{Expression='Newest';Descending=$true},
                    @{Expression='FileCount';Descending=$true},
                    @{Expression={ $_.Dir.Name.Length };Descending=$false},
                    @{Expression={ $_.Dir.FullName };Descending=$false}

      $winner = $sorted | Select-Object -First 1

      if ($Log) {
        & $Log ("  Key '{0}' -> {1} folders | KEEP: {2} (newest: {3:u}, files: {4})" -f `
          $group.Name, $group.Count, $winner.Dir.Name, $winner.Newest, $winner.FileCount)
      }

      foreach ($candidate in $sorted | Select-Object -Skip 1) {
        $srcPath  = $candidate.Dir.FullName
        $destPath = Join-Path $dupeBaseFull $candidate.Dir.Name

        $action = [pscustomobject]@{
          Key       = $group.Name
          Source    = $srcPath
          Dest      = $destPath
          Winner    = $winner.Dir.FullName
          Action    = ''
          Error     = ''
        }

        if ($Mode -eq 'DryRun') {
          $action.Action = 'DRYRUN-MOVE'
          if ($Log) { & $Log ("    DRYRUN -> {0}" -f $candidate.Dir.Name) }
        } else {
          # Path traversal check
          if (-not (Test-PathWithinRoot -Path $srcPath -Root $normalizedRoot -DisallowReparsePoints)) {
            $action.Action = 'BLOCKED'
            $action.Error  = 'Source outside root or crosses reparse point'
            $errorCount++
            if ($Log) { & $Log ("    BLOCKED: {0} - outside root or reparse point" -f $candidate.Dir.Name) }
            $actions.Add($action)
            continue
          }

          try {
            $finalDest = Invoke-RootSafeMove -Source $srcPath -Dest $destPath `
              -SourceRoot $normalizedRoot -DestRoot $dupeBaseFull
            $action.Action = 'MOVED'
            $action.Dest   = $finalDest
            $movedFolders++
            if ($Log) { & $Log ("    MOVED -> {0}" -f $finalDest) }
          } catch {
            $action.Action = 'ERROR'
            $action.Error  = $_.Exception.Message
            $errorCount++
            if ($Log) { & $Log ("    ERROR moving {0}: {1}" -f $candidate.Dir.Name, $_.Exception.Message) }
          }
        }

        $actions.Add($action)
      }
    }
  }

  $summary = [pscustomobject]@{
    TotalFolders  = $totalFolders
    DupeGroups    = $dupeGroups
    Moved         = $movedFolders
    Skipped       = $skippedFolders
    Errors        = $errorCount
    Mode          = $Mode
    Actions       = $actions.ToArray()
  }

  if ($Log) {
    & $Log ("Folder-Dedupe complete: {0} scanned, {1} dupe groups, {2} moved, {3} errors (mode: {4})" -f `
      $totalFolders, $dupeGroups, $movedFolders, $errorCount, $Mode)
  }

  return $summary
}

function Invoke-FolderDedupe {
  <# Entry point with plugin integration (mirrors Invoke-Ps3Dedupe pattern). #>
  param(
    [Parameter(Mandatory=$true)]
    [string[]]$Roots,

    [string]$DupeRoot,

    [ValidateSet('DryRun','Move')]
    [string]$Mode = 'DryRun',

    [scriptblock]$Log
  )

  return (Invoke-WithPluginOverride -Phase 'folder-dedupe' -Context @{
    Roots    = @($Roots)
    DupeRoot = $DupeRoot
    Mode     = $Mode
    Log      = $Log
  } -CoreAction {
    Invoke-FolderDedupeByBaseName -Roots $Roots -DupeRoot $DupeRoot -Mode $Mode -Log $Log
  } -Log $Log)
}

function Get-ConsoleKeyForRoot {
  <# Detect the console key for a root path by scanning folder name segments
     against the CONSOLE_FOLDER_MAP.  Returns $null when no match is found. #>
  param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath
  )

  $normalizedRoot = Resolve-RootPath -Path $RootPath
  if ([string]::IsNullOrWhiteSpace($normalizedRoot)) { return $null }

  $parts = @($normalizedRoot.Replace('/', '\').Split('\') | Where-Object { $_ -ne '' })

  # Walk segments bottom-up (leaf folder is most specific)
  for ($i = $parts.Count - 1; $i -ge 0; $i--) {
    $key = $parts[$i].Trim().ToLowerInvariant()
    if ($script:CONSOLE_FOLDER_MAP -and $script:CONSOLE_FOLDER_MAP.ContainsKey($key)) {
      return [string]$script:CONSOLE_FOLDER_MAP[$key]
    }
  }

  # Try regex map if available
  if ($script:CONSOLE_FOLDER_RX_MAP) {
    for ($i = $parts.Count - 1; $i -ge 0; $i--) {
      $p = $parts[$i].Trim().ToLowerInvariant()
      foreach ($entry in $script:CONSOLE_FOLDER_RX_MAP) {
        $rxObj = $entry.RxObj
        if ($rxObj -and $rxObj.IsMatch($p)) {
          return [string]$entry.Key
        }
      }
    }
  }

  return $null
}

function Test-RootNeedsFolderDedupe {
  <# Returns $true when the root's console key is in the folder-dedupe set. #>
  param([Parameter(Mandatory=$true)][string]$RootPath)

  $consoleKey = Get-ConsoleKeyForRoot -RootPath $RootPath
  if (-not $consoleKey) { return $false }
  return $script:FOLDER_DEDUPE_CONSOLE_KEYS.Contains($consoleKey)
}

function Test-RootNeedsPs3Dedupe {
  <# Returns $true when the root's console key is PS3. #>
  param([Parameter(Mandatory=$true)][string]$RootPath)

  $consoleKey = Get-ConsoleKeyForRoot -RootPath $RootPath
  if (-not $consoleKey) { return $false }
  return $script:PS3_DEDUPE_CONSOLE_KEYS.Contains($consoleKey)
}

function Invoke-AutoFolderDedupe {
  <# Automatically detects which roots need folder-level deduplication
     (PS3 hash-based or base-name) and dispatches accordingly.
     Called post region-dedupe in the main pipeline.
     Returns a summary object with per-root results. #>
  param(
    [Parameter(Mandatory=$true)]
    [string[]]$Roots,

    [ValidateSet('DryRun','Move')]
    [string]$Mode = 'DryRun',

    [string]$DupeRoot,

    [scriptblock]$Log
  )

  $ps3Roots    = [System.Collections.Generic.List[string]]::new()
  $folderRoots = [System.Collections.Generic.List[string]]::new()
  $results     = [System.Collections.Generic.List[psobject]]::new()

  foreach ($root in $Roots) {
    if ([string]::IsNullOrWhiteSpace($root)) { continue }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }

    $consoleKey = Get-ConsoleKeyForRoot -RootPath $root

    if ($consoleKey -and $script:PS3_DEDUPE_CONSOLE_KEYS.Contains($consoleKey)) {
      $ps3Roots.Add($root)
      if ($Log) { & $Log ("Auto-dedupe: {0} -> PS3 hash-based dedupe" -f $root) }
    }
    elseif ($consoleKey -and $script:FOLDER_DEDUPE_CONSOLE_KEYS.Contains($consoleKey)) {
      $folderRoots.Add($root)
      if ($Log) { & $Log ("Auto-dedupe: {0} -> folder base-name dedupe ({1})" -f $root, $consoleKey) }
    }
    # else: file-based console, region-dedupe already handled it
  }

  # PS3 hash-based dedupe
  if ($ps3Roots.Count -gt 0 -and (Get-Command Invoke-Ps3Dedupe -ErrorAction SilentlyContinue)) {
    if ($Log) { & $Log ("Auto-dedupe: running PS3 dedupe on {0} root(s)..." -f $ps3Roots.Count) }
    try {
      # PS3 dedupe has no Mode param - it always moves.  In DryRun we skip it.
      if ($Mode -eq 'Move') {
        $ps3Result = Invoke-Ps3Dedupe -Roots $ps3Roots.ToArray() -DupeRoot $DupeRoot -Log $Log
        $results.Add([pscustomobject]@{
          Type   = 'PS3'
          Roots  = $ps3Roots.ToArray()
          Result = $ps3Result
        })
      } else {
        if ($Log) { & $Log "Auto-dedupe: PS3 dedupe skipped in DryRun mode (hash-based dedupe is destructive-only)" }
      }
    } catch {
      if ($Log) { & $Log ("Auto-dedupe: PS3 dedupe error: {0}" -f $_.Exception.Message) }
    }
  }

  # Folder base-name dedupe
  if ($folderRoots.Count -gt 0) {
    if ($Log) { & $Log ("Auto-dedupe: running folder dedupe on {0} root(s)..." -f $folderRoots.Count) }
    try {
      $folderResult = Invoke-FolderDedupe -Roots $folderRoots.ToArray() -DupeRoot $DupeRoot -Mode $Mode -Log $Log
      $results.Add([pscustomobject]@{
        Type   = 'FolderBaseName'
        Roots  = $folderRoots.ToArray()
        Result = $folderResult
      })
    } catch {
      if ($Log) { & $Log ("Auto-dedupe: folder dedupe error: {0}" -f $_.Exception.Message) }
    }
  }

  if ($ps3Roots.Count -eq 0 -and $folderRoots.Count -eq 0) {
    if ($Log) { & $Log "Auto-dedupe: no roots detected that need folder-level deduplication" }
  }

  return [pscustomobject]@{
    Ps3Roots       = $ps3Roots.ToArray()
    FolderRoots    = $folderRoots.ToArray()
    Mode           = $Mode
    Results        = $results.ToArray()
  }
}
