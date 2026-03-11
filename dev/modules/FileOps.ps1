# ================================================================
#  FILE OPERATIONS  (extracted from simple_sort.ps1 - ARCH-03)
# ================================================================
# Contains: Path utilities, file/directory enumeration, safe move,
#           directory cleanup.
# Dependencies: Settings.ps1 (Get-AppStateValue)
# ================================================================

# BUG-06 FIX: Cache LongPathsEnabled registry value at module scope (never changes at runtime)
$script:_LongPathsEnabled = $false
try {
  $lpVal = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'LongPathsEnabled' -ErrorAction SilentlyContinue).LongPathsEnabled
  if ($lpVal -eq 1) { $script:_LongPathsEnabled = $true }
} catch { }

# ----------------------------------------------------------------
#  Path Utilities
# ----------------------------------------------------------------

function Resolve-RootPath {
  <# Normalize path (full path + trimmed separators), optional trailing slash. #>
  param(
    [string]$Path,
    [switch]$WithTrailingSlash
  )

  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }

  $inputPath = [string]$Path
  if ($inputPath.StartsWith('\\?\UNC\', [StringComparison]::OrdinalIgnoreCase)) {
    $inputPath = '\\' + $inputPath.Substring(8)
  } elseif ($inputPath.StartsWith('\\?\', [StringComparison]::OrdinalIgnoreCase)) {
    $inputPath = $inputPath.Substring(4)
  }

  $full = [System.IO.Path]::GetFullPath($inputPath)
  if ($full.StartsWith('\\?\UNC\', [StringComparison]::OrdinalIgnoreCase)) {
    $full = '\\' + $full.Substring(8)
  } elseif ($full.StartsWith('\\?\', [StringComparison]::OrdinalIgnoreCase)) {
    $full = $full.Substring(4)
  }
  $normalized = $full.TrimEnd('\','/')
  if ($WithTrailingSlash) {
    return $normalized + '\'
  }
  return $normalized
}

function Resolve-NormalizedPath {
  <# Normalize a path via Resolve-RootPath with fallback to GetFullPath. Returns $null for blank input. #>
  param([string]$Path)
  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }

  if (Get-Command Resolve-RootPath -ErrorAction SilentlyContinue) {
    try { return (Resolve-RootPath -Path $Path) } catch { }
  }
  try { return ([System.IO.Path]::GetFullPath($Path).TrimEnd('\\','/')) } catch { return $null }
}

function Get-NearestExistingDirectory {
  <# Returns nearest existing directory for a path (or $null). #>
  param([string]$Path)
  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
  $current = $Path
  if (Test-Path -LiteralPath $current -PathType Leaf) {
    $current = Split-Path -Parent $current
  }
  while ($current -and -not (Test-Path -LiteralPath $current -PathType Container)) {
    $parent = Split-Path -Parent $current
    if (-not $parent -or $parent -eq $current) { break }
    $current = $parent
  }
  if ($current -and (Test-Path -LiteralPath $current -PathType Container)) { return $current }
  return $null
}

function Test-PathHasReparsePoint {
  <# Detect reparse points in path ancestry (optionally stop at root). #>
  param(
    [string]$Path,
    [string]$StopAtRoot
  )

  if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
  $current = Get-NearestExistingDirectory -Path $Path
  if (-not $current) { return $false }

  $stop = $null
  if (-not [string]::IsNullOrWhiteSpace($StopAtRoot)) {
    $stop = Resolve-RootPath -Path $StopAtRoot
  }

  while ($current) {
    $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
    if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $true }
    if ($stop -and $current.Equals($stop, [StringComparison]::OrdinalIgnoreCase)) { break }
    $parent = Split-Path -Parent $current
    if (-not $parent -or $parent -eq $current) { break }
    $current = $parent
  }

  return $false
}

function Test-PathWithinRoot {
  <# Validates that a path resolves within a root directory (blocks path traversal). #>
  param(
    [string]$Path,
    [string]$Root,
    [switch]$DisallowReparsePoints
  )
  if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($Root)) {
    return $false
  }
  try {
    $resolvedPath = Resolve-RootPath -Path $Path
    $resolvedRoot = Resolve-RootPath -Path $Root -WithTrailingSlash
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
      return $false
    }
    if ($DisallowReparsePoints) {
      if (Test-PathHasReparsePoint -Path $resolvedPath -StopAtRoot ($resolvedRoot.TrimEnd('\'))) {
        return $false
      }
    }
    return $true
  } catch {
    return $false
  }
}

function Get-RelativePathSafe {
  <# Returns relative path if inside root; otherwise $null. #>
  param([string]$Path, [string]$Root)
  if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($Root)) { return $null }
  $resolvedPath = Resolve-RootPath -Path $Path
  $resolvedRoot = Resolve-RootPath -Path $Root -WithTrailingSlash
  if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { return $null }
  return $resolvedPath.Substring($resolvedRoot.Length).TrimStart('\','/')
}

function Resolve-ChildPathWithinRoot {
  <# Resolve a child path under base dir and validate it remains within root. #>
  param(
    [string]$BaseDir,
    [string]$ChildPath,
    [string]$Root
  )

  if ([string]::IsNullOrWhiteSpace($ChildPath)) { return $null }
  try {
    $candidate = if ([IO.Path]::IsPathRooted($ChildPath)) {
      $ChildPath
    } else {
      if ([string]::IsNullOrWhiteSpace($BaseDir)) { return $null }
      Join-Path $BaseDir $ChildPath
    }
    if (-not (Test-PathWithinRoot -Path $candidate -Root $Root -DisallowReparsePoints)) { return $null }
    return $candidate
  } catch {
    return $null
  }
}

function ConvertTo-ProtectedPathList {
  param([string]$Text)
  if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
  $items = @()
  foreach ($part in ($Text -split ',')) {
    $candidate = $part.Trim()
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    try {
      $items += (Resolve-RootPath -Path $candidate)
    } catch { }
  }
  return @($items | Select-Object -Unique)
}

# ----------------------------------------------------------------
#  Directory Helpers
# ----------------------------------------------------------------

function Initialize-Directory {
  <# Alias for Assert-DirectoryExists — kept for backward compatibility. #>
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path)
  Assert-DirectoryExists -Path $Path
}

function Assert-DirectoryExists {
  <# Ensures a directory exists, creating it (and parents) if needed. #>
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    # BUG FILEOPS-001 FIX: New-Item has no -LiteralPath in PS 5.1.
    # Escape wildcard characters ([, ], *, ?) so -Path treats them literally.
    $escapedPath = [WildcardPattern]::Escape($Path)
    [void](New-Item -ItemType Directory -Path $escapedPath -Force)
  }
}

function Read-JsonFile {
  <# Reads and parses a JSON file, returning the parsed object or $null on error. #>
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path)
  try {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $raw = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return ($raw | ConvertFrom-Json -ErrorAction Stop)
  } catch {
    return $null
  }
}

function Write-JsonFile {
  <# Writes an object to a JSON file with configurable depth. #>
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path,
    [Parameter(Mandatory=$true)]$Data,
    [int]$Depth = 8
  )
  $tmpPath = $Path + '.tmp_write'
  $Data | ConvertTo-Json -Depth $Depth | Out-File -LiteralPath $tmpPath -Encoding utf8 -Force
  Move-Item -LiteralPath $tmpPath -Destination $Path -Force
}

function Get-DirectoriesSafe {
  <# Iterative directory enumeration to avoid recursion depth issues. #>
  param([Parameter(Mandatory=$true)][string]$Root)

  $dirs = New-Object System.Collections.Generic.List[System.IO.DirectoryInfo]
  $stack = New-Object System.Collections.Generic.Stack[string]
  $stack.Push($Root)

  while ($stack.Count -gt 0) {
    $dir = $stack.Pop()
    $dirInfo = $null
    try { $dirInfo = [System.IO.DirectoryInfo]::new($dir) } catch { continue }
    if (-not $dirInfo.Exists) { continue }
    $items = @()
    try { $items = @($dirInfo.EnumerateDirectories('*', [System.IO.SearchOption]::TopDirectoryOnly)) } catch { continue }
    foreach ($it in $items) {
      if ($it.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
      [void]$dirs.Add($it)
      $stack.Push($it.FullName)
    }
  }

  return $dirs
}

function Remove-EmptyDirectories {
  <# Entfernt leere Ordner rekursiv (bottom-up). #>
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) { return }

  Get-DirectoriesSafe -Root $Path |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
      $children = @(Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue)
      if ($children.Count -eq 0) {
        Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
      }
    }
}

# ----------------------------------------------------------------
#  File Enumeration
# ----------------------------------------------------------------

if (-not (Get-Variable -Name FILE_SCAN_CACHE -Scope Script -ErrorAction SilentlyContinue)) {
  $script:FILE_SCAN_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

if (-not (Get-Variable -Name FILE_SCAN_WATCHERS -Scope Script -ErrorAction SilentlyContinue)) {
  $script:FILE_SCAN_WATCHERS = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

if (-not (Get-Variable -Name FILE_SCAN_ROOT_VERSION -Scope Script -ErrorAction SilentlyContinue)) {
  $script:FILE_SCAN_ROOT_VERSION = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

if (-not (Get-Variable -Name FILE_SCAN_CHANGED_PATHS -Scope Script -ErrorAction SilentlyContinue)) {
  $script:FILE_SCAN_CHANGED_PATHS = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

if (-not (Get-Variable -Name FILE_SCAN_WATCHER_FAILED -Scope Script -ErrorAction SilentlyContinue)) {
  $script:FILE_SCAN_WATCHER_FAILED = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

function Get-FileScanCacheKey {
  param(
    [string]$Root,
    [string]$Filter,
    [string[]]$ExcludePrefixes,
    [string[]]$AllowedExtensions
  )

  $normalizedRoot = Resolve-RootPath -Path $Root -WithTrailingSlash
  $normalizedFilter = if ([string]::IsNullOrWhiteSpace($Filter)) { '*' } else { [string]$Filter }
  $excludeKey = @($ExcludePrefixes | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object) -join ';'
  $extKey = @($AllowedExtensions | ForEach-Object { ([string]$_).ToLowerInvariant() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object) -join ';'
  return ('{0}|{1}|{2}|{3}' -f $normalizedRoot, $normalizedFilter, $excludeKey, $extKey)
}

function Initialize-FileScanWatcher {
  param([Parameter(Mandatory)][string]$Root)

  $normalizedRoot = Resolve-RootPath -Path $Root
  if ([string]::IsNullOrWhiteSpace($normalizedRoot)) { return }
  if (-not (Test-Path -LiteralPath $normalizedRoot -PathType Container)) { return }
  if ($script:FILE_SCAN_WATCHERS.ContainsKey($normalizedRoot)) { return }

  $watcher = $null
  try {
    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.InternalBufferSize = 65536
    $watcher.Path = $normalizedRoot
    $watcher.IncludeSubdirectories = $true
    $watcher.NotifyFilter = [IO.NotifyFilters]'FileName, DirectoryName, LastWrite, Size'
    $watcher.EnableRaisingEvents = $true

    $hash = [Math]::Abs(([string]$normalizedRoot).GetHashCode())
    $identifierPrefix = ('RomCleanup.ScanWatcher.{0}' -f $hash)

    # BUG FILEOPS-004 FIX: Pass state dictionaries via -MessageData so event handlers
    # (which run in an isolated scope) can access them via $Event.MessageData.
    $messageData = @{
      RootVersion  = $script:FILE_SCAN_ROOT_VERSION
      ChangedPaths = $script:FILE_SCAN_CHANGED_PATHS
    }

    $watchAction = {
        param($watcherSource, $watchEvent)

      try {
        $data = $Event.MessageData
          $rootPath = Resolve-RootPath -Path ([string]$watcherSource.Path)
        if ([string]::IsNullOrWhiteSpace($rootPath)) { return }
        if (-not $data.RootVersion.ContainsKey($rootPath)) {
          $data.RootVersion[$rootPath] = 0
        }
        $data.RootVersion[$rootPath] = [int]$data.RootVersion[$rootPath] + 1

        if (-not $data.ChangedPaths.ContainsKey($rootPath)) {
          $data.ChangedPaths[$rootPath] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        }

        if ($watchEvent -and ($watchEvent.PSObject.Properties.Name -contains 'FullPath') -and -not [string]::IsNullOrWhiteSpace([string]$watchEvent.FullPath)) {
          [void]$data.ChangedPaths[$rootPath].Add([string]$watchEvent.FullPath)
        }
        if ($watchEvent -and ($watchEvent.PSObject.Properties.Name -contains 'OldFullPath') -and -not [string]::IsNullOrWhiteSpace([string]$watchEvent.OldFullPath)) {
          [void]$data.ChangedPaths[$rootPath].Add([string]$watchEvent.OldFullPath)
        }
      } catch { }
    }

    Register-ObjectEvent -InputObject $watcher -EventName Changed -SourceIdentifier ($identifierPrefix + '.Changed') -Action $watchAction -MessageData $messageData | Out-Null
    Register-ObjectEvent -InputObject $watcher -EventName Created -SourceIdentifier ($identifierPrefix + '.Created') -Action $watchAction -MessageData $messageData | Out-Null
    Register-ObjectEvent -InputObject $watcher -EventName Deleted -SourceIdentifier ($identifierPrefix + '.Deleted') -Action $watchAction -MessageData $messageData | Out-Null
    Register-ObjectEvent -InputObject $watcher -EventName Renamed -SourceIdentifier ($identifierPrefix + '.Renamed') -Action $watchAction -MessageData $messageData | Out-Null

    $errorAction = {
      param($watcherSource, $watchError)
      try {
        $data = $Event.MessageData
        $rootPath = Resolve-RootPath -Path ([string]$watcherSource.Path)
        if (-not [string]::IsNullOrWhiteSpace($rootPath) -and $data.RootVersion.ContainsKey($rootPath)) {
          $data.RootVersion[$rootPath] = [int]$data.RootVersion[$rootPath] + 1
        }
        Write-Warning ('[FileScanWatcher] Buffer Overflow fuer Root: {0}' -f $rootPath)
      } catch { }
    }
    Register-ObjectEvent -InputObject $watcher -EventName Error -SourceIdentifier ($identifierPrefix + '.Error') -Action $errorAction -MessageData $messageData | Out-Null

    $script:FILE_SCAN_WATCHERS[$normalizedRoot] = [pscustomobject]@{
      Watcher = $watcher
      IdentifierPrefix = $identifierPrefix
    }

    if (-not $script:FILE_SCAN_ROOT_VERSION.ContainsKey($normalizedRoot)) {
      $script:FILE_SCAN_ROOT_VERSION[$normalizedRoot] = 0
    }
    if (-not $script:FILE_SCAN_CHANGED_PATHS.ContainsKey($normalizedRoot)) {
      $script:FILE_SCAN_CHANGED_PATHS[$normalizedRoot] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    }
  } catch {
    if ($watcher) {
      try { $watcher.EnableRaisingEvents = $false } catch { }
      try { $watcher.Dispose() } catch { }
    }
  }
}

function Save-SqliteFileScanIndex {
  param(
    [Parameter(Mandatory)][string]$Root,
    [string[]]$Paths
  )

  $threshold = [int](Get-AppStateValue -Key 'ScanSqliteThreshold' -Default 10000)
  if (@($Paths).Count -lt [Math]::Max(10000, $threshold)) { return }

  $sqliteCmd = Get-Command sqlite3 -ErrorAction SilentlyContinue
  if (-not $sqliteCmd) { return }

  try {
    $reportsDir = Join-Path (Get-Location).Path 'reports'
    Assert-DirectoryExists -Path $reportsDir

    $dbPath = Join-Path $reportsDir 'scan-files.sqlite'
    $tmpId = [guid]::NewGuid().ToString('N')
    $tmpCsv = Join-Path $env:TEMP ("romcleanup-scan-$tmpId.csv")
    $tmpSql = Join-Path $env:TEMP ("romcleanup-scan-$tmpId.sql")

    $csvLines = New-Object System.Collections.Generic.List[string]
    foreach ($path in @($Paths)) {
      if ([string]::IsNullOrWhiteSpace([string]$path)) { continue }
      try {
        $fi = [System.IO.FileInfo]::new([string]$path)
        if (-not $fi.Exists) { continue }
        # BUG-013 FIX: Reject paths containing newline characters — they would corrupt
        # the CSV format and break SQLite .import (each newline becomes a new CSV row).
        $fullPath = [string]$fi.FullName
        if ($fullPath.Contains("`n") -or $fullPath.Contains("`r")) { continue }
        $escapedPath = $fullPath.Replace('"', '""')
        $escapedExt = ([string]$fi.Extension).Replace('"', '""')
        $line = ('"{0}",{1},{2},"{3}"' -f $escapedPath, [int64]$fi.Length, [int64]$fi.LastWriteTimeUtc.Ticks, $escapedExt)
        [void]$csvLines.Add($line)
      } catch { }
    }

    if ($csvLines.Count -eq 0) { return }
    Set-Content -LiteralPath $tmpCsv -Value @($csvLines.ToArray()) -Encoding UTF8

    # BUG-001 FIX: Strict input validation for SQLite CLI (no parameterized queries available).
    # Allowlist: only permit safe path characters. Reject anything else.
    $rootSql = ([string]$Root).Replace("'", "''")
    $csvSqlPath = ([string]$tmpCsv).Replace("'", "''")
    if ($rootSql -match '[;\x00-\x1f]' -or $csvSqlPath -match '[;\x00-\x1f]') {
      Write-Warning ('[FileOps] Save-SqliteFileScanIndex: skipped due to suspicious path characters in root or csv path.')
      return
    }
    if ($rootSql -match '--' -or $csvSqlPath -match '--') {
      Write-Warning ('[FileOps] Save-SqliteFileScanIndex: skipped due to SQL comment sequence in path.')
      return
    }
    $sqlScript = @(
      'PRAGMA journal_mode=WAL;',
      'CREATE TABLE IF NOT EXISTS file_scan_index (root TEXT NOT NULL, path TEXT NOT NULL PRIMARY KEY, length INTEGER, lastWriteTicks INTEGER, ext TEXT, updatedAtUtc TEXT);',
      'CREATE INDEX IF NOT EXISTS idx_file_scan_root ON file_scan_index(root);',
      ("DELETE FROM file_scan_index WHERE root = '{0}';" -f $rootSql),
      'DROP TABLE IF EXISTS tmp_scan_import;',
      'CREATE TEMP TABLE tmp_scan_import(path TEXT, length INTEGER, lastWriteTicks INTEGER, ext TEXT);',
      '.mode csv',
      (".import '{0}' tmp_scan_import" -f $csvSqlPath),
      ("INSERT OR REPLACE INTO file_scan_index(root, path, length, lastWriteTicks, ext, updatedAtUtc) SELECT '{0}', path, length, lastWriteTicks, ext, datetime('now') FROM tmp_scan_import;" -f $rootSql),
      'DROP TABLE IF EXISTS tmp_scan_import;'
    )
    Set-Content -LiteralPath $tmpSql -Value $sqlScript -Encoding UTF8

    & $sqliteCmd.Source $dbPath ('.read {0}' -f $tmpSql) | Out-Null
  } catch {
    if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
      [void](Write-CatchGuardLog -Module 'FileOps' -Action 'Save-SqliteFileScanIndex' -Root $Root -Exception $_.Exception -Level 'Warning')
    }
  } finally {
    try { if (Test-Path -LiteralPath $tmpCsv -PathType Leaf) { Remove-Item -LiteralPath $tmpCsv -Force -ErrorAction SilentlyContinue } } catch { }
    try { if (Test-Path -LiteralPath $tmpSql -PathType Leaf) { Remove-Item -LiteralPath $tmpSql -Force -ErrorAction SilentlyContinue } } catch { }
  }
}

function Get-FilesSafe {
  <# Iterative file enumeration to avoid recursion depth issues.
     Use -Responsive during GUI operations to run enumeration in a
     background runspace with UI-pump so the window stays responsive. #>
  param(
    [Parameter(Mandatory=$true)][string]$Root,
    [string]$Filter,
    [string[]]$ExcludePrefixes,
    [System.Collections.Generic.HashSet[string]]$AllowedExtensions,
    [switch]$Responsive,
    [int]$PollMs = 25
  )

  $excludeList = @()
  foreach ($ep in @($ExcludePrefixes)) {
    if ([string]::IsNullOrWhiteSpace([string]$ep)) { continue }
    $excludeList += [string]$ep
  }

  $allowedList = @()
  foreach ($ext in @($AllowedExtensions)) {
    if ([string]::IsNullOrWhiteSpace([string]$ext)) { continue }
    $allowedList += ([string]$ext).ToLowerInvariant()
  }

  $scanFilter = if ([string]::IsNullOrWhiteSpace($Filter)) { '*' } else { [string]$Filter }
  $cacheKey = Get-FileScanCacheKey -Root $Root -Filter $scanFilter -ExcludePrefixes @($excludeList) -AllowedExtensions @($allowedList)
  $normalizedRoot = Resolve-RootPath -Path $Root
  if ([string]::IsNullOrWhiteSpace($normalizedRoot)) { return @() }
  Initialize-FileScanWatcher -Root $Root
  $rootVersion = if ($script:FILE_SCAN_ROOT_VERSION.ContainsKey($normalizedRoot)) { [int]$script:FILE_SCAN_ROOT_VERSION[$normalizedRoot] } else { 0 }
  $rootStamp = 0L
  try {
    $rootInfo = Get-Item -LiteralPath $normalizedRoot -ErrorAction Stop
    $rootStamp = [int64]$rootInfo.LastWriteTimeUtc.Ticks
  } catch {
    $rootStamp = 0L
  }

  $testFileCandidate = {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace([string]$Path)) { return $false }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }

    $skipFile = $false
    foreach ($ep in @($excludeList)) {
      if ([string]::IsNullOrWhiteSpace($ep)) { continue }
      $epTrim = $ep.TrimEnd('\','/')
      if ($Path.StartsWith($ep, [StringComparison]::OrdinalIgnoreCase) -or $Path.Equals($epTrim, [StringComparison]::OrdinalIgnoreCase)) {
        $skipFile = $true
        break
      }
    }
    if ($skipFile) { return $false }

    if ($scanFilter -and $scanFilter -ne '*' -and ([System.IO.Path]::GetFileName($Path) -notlike $scanFilter)) {
      return $false
    }

    if ($allowedList.Count -gt 0) {
      $extLower = ([System.IO.Path]::GetExtension($Path)).ToLowerInvariant()
      if ($allowedList -notcontains $extLower) {
        return $false
      }
    }

    return $true
  }

  if ($script:FILE_SCAN_CACHE.ContainsKey($cacheKey)) {
    $entry = $script:FILE_SCAN_CACHE[$cacheKey]

    # Wenn der Watcher ausgefallen ist und der Cache-Eintrag aelter als 60s,
    # Cache verwerfen und vollen Rescan erzwingen.
    $watcherFailed = $script:FILE_SCAN_WATCHER_FAILED.ContainsKey($normalizedRoot) -and $script:FILE_SCAN_WATCHER_FAILED[$normalizedRoot]
    if ($watcherFailed -and $entry -and ($entry.PSObject.Properties.Name -contains 'UpdatedAtUtc')) {
      try {
        $cacheAge = (Get-Date).ToUniversalTime() - [datetime]::Parse($entry.UpdatedAtUtc)
        if ($cacheAge.TotalSeconds -gt 60) {
          $script:FILE_SCAN_CACHE.Remove($cacheKey)
          $entry = $null
        }
      } catch { }
    }
  }

  if ($script:FILE_SCAN_CACHE.ContainsKey($cacheKey)) {
    $entry = $script:FILE_SCAN_CACHE[$cacheKey]
    $sameVersion = ($entry -and ($entry.PSObject.Properties.Name -contains 'RootVersion') -and [int]$entry.RootVersion -eq $rootVersion)
    $sameStamp = ($entry -and ($entry.PSObject.Properties.Name -contains 'RootStamp') -and [int64]$entry.RootStamp -eq $rootStamp)
    if ($sameVersion -and $sameStamp) {
      $cached = New-Object System.Collections.Generic.List[System.IO.FileInfo]
      foreach ($path in @($entry.Paths)) {
        try {
          $fi = [System.IO.FileInfo]::new([string]$path)
          if ($fi.Exists) { [void]$cached.Add($fi) }
        } catch { }
      }
      return $cached
    }

    $canIncremental = $false
    $changedSet = $null
    if ($script:FILE_SCAN_CHANGED_PATHS.ContainsKey($normalizedRoot)) {
      $changedSet = $script:FILE_SCAN_CHANGED_PATHS[$normalizedRoot]
      if ($changedSet -and $changedSet.Count -gt 0 -and $changedSet.Count -le 2000) {
        $canIncremental = $true
      }
    }

    if ($canIncremental) {
      $pathSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
      foreach ($existingPath in @($entry.Paths)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$existingPath)) {
          [void]$pathSet.Add([string]$existingPath)
        }
      }

      $fallbackToFullRescan = $false
      foreach ($changedPath in @($changedSet)) {
        if ([string]::IsNullOrWhiteSpace([string]$changedPath)) { continue }

        if (Test-Path -LiteralPath $changedPath -PathType Container) {
          $fallbackToFullRescan = $true
          break
        }

        if (& $testFileCandidate $changedPath) {
          [void]$pathSet.Add([string]$changedPath)
        } else {
          [void]$pathSet.Remove([string]$changedPath)
        }
      }

      if (-not $fallbackToFullRescan) {
        $paths = [string[]]@($pathSet)
        $script:FILE_SCAN_CACHE[$cacheKey] = [pscustomobject]@{
          Root = $normalizedRoot
          RootVersion = $rootVersion
          RootStamp = $rootStamp
          Paths = $paths
          UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        }
        $changedSet.Clear()

        $incrementalFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
        foreach ($path in $paths) {
          try {
            $fi = [System.IO.FileInfo]::new([string]$path)
            if ($fi.Exists) { [void]$incrementalFiles.Add($fi) }
          } catch { }
        }
        Save-SqliteFileScanIndex -Root $normalizedRoot -Paths $paths
        return $incrementalFiles
      }
    }
  }

  $scanScript = {
    param([string]$ScanRoot, [string]$ScanFilter, [string[]]$ScanExcludePrefixes, [string[]]$ScanAllowedExtensions)
    $paths = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    # Visited-Set verhindert Endlosschleifen bei zirkulären Symlinks
    $visitedDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $stack.Push($ScanRoot)
    while ($stack.Count -gt 0) {
      $dir = $stack.Pop()
      # Zirkuläre Symlinks abfangen
      if (-not $visitedDirs.Add($dir)) { continue }
      if ($ScanExcludePrefixes) {
        $skipDir = $false
        foreach ($ep in $ScanExcludePrefixes) {
          if ([string]::IsNullOrWhiteSpace($ep)) { continue }
          $epTrim = $ep.TrimEnd('\','/')
          if ($dir.StartsWith($ep, [StringComparison]::OrdinalIgnoreCase) -or $dir.Equals($epTrim, [StringComparison]::OrdinalIgnoreCase)) {
            $skipDir = $true
            break
          }
        }
        if ($skipDir) { continue }
      }

      # Get-ChildItem für maximale Kompatibilität mit Netzlaufwerken (SMB/NAS/TrueNAS/unRAID)
      # .NET EnumerateFiles/EnumerateDirectories versagt auf manchen SMB-Shares (z.B. TrueNAS Scale)
      $allItems = $null
      try {
        $allItems = @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop)
      } catch {
        continue
      }

      foreach ($item in $allItems) {
        if ($item -is [System.IO.DirectoryInfo] -or ($item.PSIsContainer)) {
          # Verzeichnis-Symlinks/Junctions werden durch $visitedDirs abgefangen
          $stack.Push($item.FullName)
        } else {
          # Datei: KEIN ReparsePoint-Check – auf SMB/NAS-Shares setzen regulaere
          # Dateien dieses Flag faelschlicherweise, was alle ROMs ausschliessen wuerde.
          $fullPath = [string]$item.FullName
          if ($ScanAllowedExtensions -and $ScanAllowedExtensions.Count -gt 0) {
            $extLower = ([System.IO.Path]::GetExtension($fullPath)).ToLowerInvariant()
            if ($ScanAllowedExtensions -notcontains $extLower) { continue }
          }
          if ($ScanFilter -and $ScanFilter -ne '*' -and ($item.Name -notlike $ScanFilter)) { continue }
          [void]$paths.Add($fullPath)
        }
      }
    }
    return $paths
  }

  $useRunspace = $Responsive -and [bool](Get-AppStateValue -Key 'OperationInProgress' -Default $false)
  $pathResults = @()
  if ($useRunspace) {
    $ps = [System.Management.Automation.PowerShell]::Create()
    $async = $null
    try {
      [void]$ps.AddScript($scanScript).AddArgument($Root).AddArgument($scanFilter).AddArgument(@($excludeList)).AddArgument(@($allowedList))
      $async = $ps.BeginInvoke()
      while (-not $async.IsCompleted) {
        if ([bool](Get-AppStateValue -Key 'CancelRequested' -Default $false)) {
          try { $ps.Stop() } catch { }
          throw [System.OperationCanceledException]::new('Abbruch durch Benutzer')
        }
        Invoke-UiPump
        if ($PollMs -gt 0) { Start-Sleep -Milliseconds $PollMs }
      }
      $pathResults = @($ps.EndInvoke($async))
    } finally {
      if ($ps) { $ps.Dispose() }
    }
  } else {
    $pathResults = @(& $scanScript $Root $scanFilter @($excludeList) @($allowedList))
  }

  $normalizedResults = New-Object System.Collections.Generic.List[string]
  foreach ($p in @($pathResults)) {
    if ([string]::IsNullOrWhiteSpace([string]$p)) { continue }
    [void]$normalizedResults.Add([string]$p)
  }

  $script:FILE_SCAN_CACHE[$cacheKey] = [pscustomobject]@{
    Root = $normalizedRoot
    RootVersion = $rootVersion
    RootStamp = $rootStamp
    Paths = @($normalizedResults.ToArray())
    UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
  }

  if ($script:FILE_SCAN_CHANGED_PATHS.ContainsKey($normalizedRoot)) {
    try { $script:FILE_SCAN_CHANGED_PATHS[$normalizedRoot].Clear() } catch { }
  }

  Save-SqliteFileScanIndex -Root $normalizedRoot -Paths @($normalizedResults.ToArray())

  $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
  foreach ($p in @($pathResults)) {
    if ([string]::IsNullOrWhiteSpace([string]$p)) { continue }
    try {
      $fi = [System.IO.FileInfo]::new([string]$p)
      if ($fi.Exists) { [void]$files.Add($fi) }
    } catch { }
  }

  return $files
}

# ----------------------------------------------------------------
#  Streaming File Enumeration
# ----------------------------------------------------------------

function Get-FilesSafeStreaming {
  <#
  .SYNOPSIS
    Pipeline-compatible streaming file enumeration.
  .DESCRIPTION
    Like Get-FilesSafe but yields files one by one via Write-Output
    instead of collecting into a list. Suitable for pipeline processing
    of large file sets (>50k files) to reduce peak memory usage.
    Does NOT use the scan cache (intended for one-pass pipeline scenarios).
  .PARAMETER Root
    Root directory to scan.
  .PARAMETER Filter
    Wildcard filter for file names. Default: '*'.
  .PARAMETER ExcludePrefixes
    Path prefixes to exclude from scanning.
  .PARAMETER AllowedExtensions
    If specified, only files with these extensions are returned.
  .EXAMPLE
    Get-FilesSafeStreaming -Root 'C:\ROMs' -AllowedExtensions @('.zip','.7z') |
      ForEach-Object { Process-File $_ }
  #>
  param(
    [Parameter(Mandatory=$true)][string]$Root,
    [string]$Filter = '*',
    [string[]]$ExcludePrefixes = @(),
    [string[]]$AllowedExtensions = @()
  )

  $normalizedRoot = Resolve-RootPath -Path $Root
  if ([string]::IsNullOrWhiteSpace($normalizedRoot) -or -not (Test-Path -LiteralPath $normalizedRoot -PathType Container)) { return }

  $allowedLower = @()
  foreach ($ext in $AllowedExtensions) {
    if (-not [string]::IsNullOrWhiteSpace($ext)) { $allowedLower += $ext.ToLowerInvariant() }
  }

  $stack = New-Object System.Collections.Generic.Stack[string]
  $visitedDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $stack.Push($normalizedRoot)

  while ($stack.Count -gt 0) {
    $dir = $stack.Pop()
    if (-not $visitedDirs.Add($dir)) { continue }

    $skipDir = $false
    foreach ($ep in $ExcludePrefixes) {
      if ([string]::IsNullOrWhiteSpace($ep)) { continue }
      $epTrim = $ep.TrimEnd('\','/')
      if ($dir.StartsWith($ep, [StringComparison]::OrdinalIgnoreCase) -or $dir.Equals($epTrim, [StringComparison]::OrdinalIgnoreCase)) {
        $skipDir = $true; break
      }
    }
    if ($skipDir) { continue }

    $allItems = $null
    try {
      $allItems = @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop)
    } catch { continue }

    foreach ($item in $allItems) {
      if ($item -is [System.IO.DirectoryInfo] -or ($item.PSIsContainer)) {
        $stack.Push($item.FullName)
      } else {
        $fullPath = [string]$item.FullName
        if ($allowedLower.Count -gt 0) {
          $extLower = ([System.IO.Path]::GetExtension($fullPath)).ToLowerInvariant()
          if ($allowedLower -notcontains $extLower) { continue }
        }
        if ($Filter -and $Filter -ne '*' -and ($item.Name -notlike $Filter)) { continue }
        Write-Output $item
      }
    }
  }
}

# ----------------------------------------------------------------
#  Safe Move
# ----------------------------------------------------------------

function Move-ItemSafely {
  param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Dest,
    [string]$ValidateSourceWithinRoot = $null,
    [string]$ValidateDestWithinRoot = $null
  )

  # BUG-042 FIX: Pre-check path length to avoid PathTooLongException crash
  $srcLen = ([string]$Source).Length
  $dstLen = ([string]$Dest).Length
  $maxPathLen = if ($script:_LongPathsEnabled) { 32000 } else { 240 }
  if ($srcLen -gt $maxPathLen -or $dstLen -gt $maxPathLen) {
    $maxLen = [Math]::Max($srcLen, $dstLen)
    Write-Warning ('[FileOps] Move-ItemSafely: Pfad zu lang ({0} Zeichen, max {1}): {2}' -f $maxLen, $maxPathLen, [IO.Path]::GetFileName($Source))
    throw ('Move-ItemSafely: Path too long ({0} chars). Source or destination exceeds {1} character limit: {2}' -f $maxLen, $maxPathLen, [IO.Path]::GetFileName($Source))
  }

  try {
    $srcFull = [System.IO.Path]::GetFullPath($Source)
    $destFull = [System.IO.Path]::GetFullPath($Dest)
    if ($srcFull -eq $destFull) {
      throw "Blocked: Source and destination are the same path: $Source"
    }
  } catch [System.IO.PathTooLongException] {
    Write-Warning ('[FileOps] Move-ItemSafely: PathTooLongException: {0}' -f $_.Exception.Message)
    throw
  } catch {
    throw "Blocked: Invalid source/destination path: $Source -> $Dest"
  }

  # Path traversal protection: validate paths stay within intended roots if specified
  if (-not [string]::IsNullOrWhiteSpace($ValidateSourceWithinRoot)) {
    if (-not (Test-PathWithinRoot -Path $Source -Root $ValidateSourceWithinRoot -DisallowReparsePoints)) {
      throw "Path traversal blocked: Source '$Source' is outside root '$ValidateSourceWithinRoot'"
    }
  }
  if (-not [string]::IsNullOrWhiteSpace($ValidateDestWithinRoot)) {
    if (-not (Test-PathWithinRoot -Path $Dest -Root $ValidateDestWithinRoot -DisallowReparsePoints)) {
      throw "Path traversal blocked: Dest '$Dest' is outside root '$ValidateDestWithinRoot'"
    }
  }

  # Block moves involving reparse points (symlink/junction)
  $sourceInfo = Get-Item -LiteralPath $Source -Force -ErrorAction SilentlyContinue
  if ($sourceInfo -and ($sourceInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "Blocked: Source is a reparse point: $Source"
  }

  $destDir = Split-Path -Parent $Dest
  Initialize-Directory $destDir
  $destDirInfo = Get-Item -LiteralPath $destDir -Force -ErrorAction SilentlyContinue
  if ($destDirInfo -and ($destDirInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "Blocked: Destination directory is a reparse point: $destDir"
  }

  $dir  = Split-Path -Parent $Dest
  $name = [IO.Path]::GetFileNameWithoutExtension($Dest)
  $ext  = [IO.Path]::GetExtension($Dest)

  $attempt = 0
  $maxAttempts = 10000
  while ($attempt -lt $maxAttempts) {
    # BUG-043 FIX: Check cancellation between DUP retry iterations (prevents indefinite hang on NAS/UNC)
    if ($attempt -gt 0 -and (Get-Command Test-CancelRequested -ErrorAction SilentlyContinue)) {
      try { if (Test-CancelRequested) { throw 'Move-ItemSafely: Operation abgebrochen (Cancel).'; } } catch [System.Management.Automation.CommandNotFoundException] { }
    }
    $candidate = if ($attempt -eq 0) {
      $Dest
    } else {
      Join-Path $dir ("{0}__DUP{1}{2}" -f $name, $attempt, $ext)
    }

    $tempCandidate = ($candidate + '.tmp_move')

    try {
      if (Test-Path -LiteralPath $tempCandidate) {
        $attempt++
        continue
      }

      Move-Item -LiteralPath $Source -Destination $tempCandidate -ErrorAction Stop

      try {
        Move-Item -LiteralPath $tempCandidate -Destination $candidate -ErrorAction Stop
        return $candidate
      } catch {
        $renameError = $_
        $renameCollision = $false
        if ($renameError.Exception -is [System.IO.IOException] -and $renameError.Exception.Message -match '(?i)already exists|bereits vorhanden|cannot create a file when that file already exists') {
          $renameCollision = $true
        }

        if (Test-Path -LiteralPath $tempCandidate) {
          try {
            if (-not (Test-Path -LiteralPath $Source)) {
              Move-Item -LiteralPath $tempCandidate -Destination $Source -ErrorAction Stop
            }
          } catch {
            # BUG-MV-01 fix: log recovery failure — file may remain as .tmp_move orphan
            $recoveryMsg = ("Move-ItemSafely: Recovery fehlgeschlagen — Datei verbleibt als .tmp_move Waise: {0} (Fehler: {1})" -f $tempCandidate, $_.Exception.Message)
            if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
              [void](Write-CatchGuardLog -Module 'FileOps' -Action 'Move-ItemSafely/Recovery' -Exception $_.Exception -Level 'Error')
            }
            Write-Warning $recoveryMsg
          }
        }

        if ($renameCollision -and (Test-Path -LiteralPath $Source)) {
          $attempt++
          continue
        }

        throw
      }
    } catch {
      $isCollision = $false
      if ($_.Exception -is [System.IO.IOException] -and $_.Exception.Message -match '(?i)already exists|bereits vorhanden|cannot create a file when that file already exists') {
        $isCollision = $true
      }

      if ($isCollision -and (Test-Path -LiteralPath $Source)) {
        $attempt++
        continue
      }
      throw
    }
  }

  throw ("Move-ItemSafely: konnte keinen freien Zielpfad finden nach {0} Versuchen: {1}" -f $maxAttempts, $Dest)
}

function Invoke-RootSafeMove {
  <# Centralized safe move wrapper to enforce consistent root validation. #>
  param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Dest,
    [string]$SourceRoot,
    [string]$DestRoot
  )

  return (Move-ItemSafely -Source $Source -Dest $Dest `
    -ValidateSourceWithinRoot $SourceRoot -ValidateDestWithinRoot $DestRoot)
}

function Find-OrphanedTmpMoveFiles {
  <# BUG-003 FIX: Scans roots for orphaned .tmp_move files left by failed Move-ItemSafely operations. #>
  param(
    [Parameter(Mandatory=$true)][string[]]$Roots
  )

  $orphans = [System.Collections.Generic.List[pscustomobject]]::new()
  foreach ($root in $Roots) {
    if ([string]::IsNullOrWhiteSpace($root)) { continue }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    $files = Get-ChildItem -LiteralPath $root -Filter '*.tmp_move' -File -Recurse -ErrorAction SilentlyContinue
    foreach ($f in $files) {
      $orphans.Add([pscustomobject]@{
        FullName     = $f.FullName
        Root         = $root
        LastWriteUtc = $f.LastWriteTimeUtc
        Length       = $f.Length
      })
    }
  }
  return @($orphans)
}

function Test-IsProtectedPath {
  param(
    [string]$Path,
    [string[]]$ProtectedPaths
  )

  if ([string]::IsNullOrWhiteSpace($Path) -or -not $ProtectedPaths -or $ProtectedPaths.Count -eq 0) { return $false }
  $target = $null
  try { $target = Resolve-RootPath -Path $Path } catch { return $false }
  if (-not $target) { return $false }

  foreach ($protected in $ProtectedPaths) {
    if ([string]::IsNullOrWhiteSpace($protected)) { continue }
    if ($target.Equals($protected, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($target.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
  }

  return $false
}

function Get-MovePathBlocklist {
  <# Returns normalized default blocklist for dangerous move destinations. #>
  $raw = @(
    'C:\Windows',
    'C:\Program Files',
    'C:\Program Files (x86)',
    'C:\Users\*\AppData',
    'C:\Users\*\Desktop',
    [string]$env:SystemRoot,
    [string]$env:ProgramFiles,
    [string]$env:ProgramFilesx86
  )

  $list = New-Object System.Collections.Generic.List[string]
  $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in $raw) {
    if ([string]::IsNullOrWhiteSpace([string]$entry)) { continue }
    $expanded = [Environment]::ExpandEnvironmentVariables([string]$entry).Trim()
    if ([string]::IsNullOrWhiteSpace($expanded)) { continue }
    $normalized = $expanded.TrimEnd('\')
    if ($seen.Add($normalized)) {
      [void]$list.Add($normalized)
    }
  }

  return @($list)
}

function Test-PathBlockedByBlocklist {
  param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string[]]$Blocklist
  )

  if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
  if (-not $Blocklist -or $Blocklist.Count -eq 0) { return $false }

  $target = $null
  try {
    $target = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
  } catch {
    $target = $Path.TrimEnd('\')
  }

  foreach ($pattern in @($Blocklist)) {
    if ([string]::IsNullOrWhiteSpace([string]$pattern)) { continue }
    $expanded = [Environment]::ExpandEnvironmentVariables([string]$pattern).Trim().TrimEnd('\')
    if ([string]::IsNullOrWhiteSpace($expanded)) { continue }

    $escaped = [Regex]::Escape($expanded) -replace '\\\*', '.*'
    $rx = '^' + $escaped + '(\\|$)'
    if ($target -match $rx) {
      return $true
    }
  }

  return $false
}

function Get-FileSetFromPath {
  <# Compatibility helper: returns a deterministic file set for a root path. #>
  param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string[]]$Extensions = @()
  )

  if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @() }

  $allowedExtensions = $null
  if ($Extensions -and $Extensions.Count -gt 0) {
    $allowedExtensions = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @($Extensions)) {
      if ([string]::IsNullOrWhiteSpace([string]$extension)) { continue }
      $normalized = [string]$extension
      if (-not $normalized.StartsWith('.')) { $normalized = '.' + $normalized }
      [void]$allowedExtensions.Add($normalized.ToLowerInvariant())
    }
  }

  $files = @(Get-FilesSafe -Root $Path -AllowedExtensions $allowedExtensions)
  return @($files | Sort-Object FullName)
}
