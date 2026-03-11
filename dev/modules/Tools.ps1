# Archive-Entry-Cache: delegates to generic LruCache.ps1
$script:ARCHIVE_ENTRY_LRU = New-LruCache -MaxEntries 5000 -Name 'ArchiveEntry'
Register-LruCache -Cache $script:ARCHIVE_ENTRY_LRU
$script:TOOL_CACHE                = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
$script:TOOL_HASH_VERDICT_CACHE   = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
$script:TOOL_HASH_BYPASS_WARNED   = $false

if (-not (Get-Command -Name New-TemporaryFile -ErrorAction SilentlyContinue)) {
  function New-TemporaryFile {
    $tempPath = [System.IO.Path]::GetTempFileName()
    return (Get-Item -LiteralPath $tempPath -ErrorAction Stop)
  }
}

function Clear-RomCleanupConvertedBackupArtifacts {
  <# Remove stale *.converted_backup* files beneath active roots. #>
  param(
    [string[]]$Roots,
    [int]$OlderThanDays = 7,
    [scriptblock]$Log
  )

  if (-not $Roots -or $Roots.Count -eq 0) { return }
  if ($OlderThanDays -lt 1) { $OlderThanDays = 1 }
  $cutoff = (Get-Date).AddDays(-1 * [double]$OlderThanDays)

  foreach ($root in @($Roots)) {
    if ([string]::IsNullOrWhiteSpace([string]$root)) { continue }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }

    foreach ($entry in @(Get-ChildItem -LiteralPath $root -Filter '*.converted_backup*' -File -Recurse -ErrorAction SilentlyContinue)) {
      if ($entry.LastWriteTime -gt $cutoff) { continue }
      try {
        Remove-Item -LiteralPath $entry.FullName -Force -ErrorAction Stop
        if ($Log) { & $Log ('Backup-GC entfernt: {0}' -f $entry.FullName) }
      } catch {
        if ($Log) { & $Log ('Backup-GC Skip (in use): {0}' -f $entry.FullName) }
      }
    }
  }
}

function Get-ToolHashAllowList {
  <# Load optional SHA256 allowlist from data/tool-hashes.json. #>
  if (-not (Get-Command Import-RomCleanupJsonData -ErrorAction SilentlyContinue)) { return $null }
  $payload = Import-RomCleanupJsonData -FileName 'tool-hashes.json'
  if (-not $payload) { return $null }

  if ($payload -is [System.Collections.IDictionary] -and $payload.Contains('Tools')) {
    return $payload.Tools
  }
  if ($payload -is [System.Collections.IDictionary]) { return $payload }
  return $null
}

function Test-ToolBinaryHash {
  param(
    [Parameter(Mandatory=$true)][string]$ToolPath,
    [scriptblock]$Log
  )

  if (-not (Test-Path -LiteralPath $ToolPath -PathType Leaf)) { return $false }
  # BUG-035 FIX: Include LastWriteTime in cache key so binary changes invalidate the cache
  $lwt = try { (Get-Item -LiteralPath $ToolPath -ErrorAction Stop).LastWriteTimeUtc.Ticks } catch { 0 }
  $cacheKey = ('{0}|{1}' -f [string]$ToolPath, $lwt)
  if ($script:TOOL_HASH_VERDICT_CACHE.ContainsKey($cacheKey)) {
    return [bool]$script:TOOL_HASH_VERDICT_CACHE[$cacheKey]
  }

  $allow = Get-ToolHashAllowList
  $allowInsecureBypass = [bool](Get-AppStateValue -Key 'AllowInsecureToolHashBypass' -Default $false)

  # SEC-04: Einmalige Session-Warnung wenn Insecure-Bypass aktiv
  if ($allowInsecureBypass -and -not $script:TOOL_HASH_BYPASS_WARNED) {
    $script:TOOL_HASH_BYPASS_WARNED = $true
    # BUG-038 FIX: In GUI mode, show confirmation dialog for InsecureToolHashBypass
    $guiConfirmed = $true
    try {
      if ([System.Threading.Thread]::CurrentThread.ApartmentState -eq [System.Threading.ApartmentState]::STA -and
          [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'PresentationFramework' }) {
        $confirmResult = [System.Windows.MessageBox]::Show(
          ('AllowInsecureToolHashBypass ist aktiviert!{0}{0}Tool-Binaries werden NICHT auf Integritaet geprueft.{0}Dies ist ein Sicherheitsrisiko.{0}{0}Fortfahren ohne Hash-Verifizierung?' -f [Environment]::NewLine),
          'Sicherheitswarnung: Tool-Hash-Bypass',
          [System.Windows.MessageBoxButton]::YesNo,
          [System.Windows.MessageBoxImage]::Warning
        )
        if ($confirmResult -ne [System.Windows.MessageBoxResult]::Yes) {
          $guiConfirmed = $false
          Set-AppStateValue -Key 'AllowInsecureToolHashBypass' -Value $false
        }
      }
    } catch { }
    if (-not $guiConfirmed) {
      $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = $false
      return $false
    }
    Write-Warning '[SEC] AllowInsecureToolHashBypass ist aktiv! Tool-Binaries werden NICHT auf Integritaet geprueft. Nur in vertrauenswuerdigen Umgebungen verwenden.'
    if (Get-Command Write-SecurityAuditEvent -ErrorAction SilentlyContinue) {
      try {
        Write-SecurityAuditEvent -Domain 'ToolHash' -Action 'InsecureBypass' -Actor 'System' -Target 'AllTools' -Outcome 'Warn' -Detail 'AllowInsecureToolHashBypass ist aktiviert. Tool-Binaries werden ohne Hash-Verifikation ausgefuehrt.' -Source 'Test-ToolBinaryHash' -Severity 'High'
      } catch { }
    }
  }

  if (-not $allow) {
    if ($allowInsecureBypass) {
      if ($Log) { & $Log ('  WARN [SEC] tool-hashes.json fehlt/leer – Insecure-Bypass aktiv für {0}' -f [System.IO.Path]::GetFileName($ToolPath)) }
      $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = $true
      return $true
    }
    if ($Log) { & $Log ('  FEHLER [SEC] tool-hashes.json fehlt/leer – {0} blockiert' -f [System.IO.Path]::GetFileName($ToolPath)) }
    $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = $false
    return $false
  }

  $name = [System.IO.Path]::GetFileName($ToolPath)
  if (-not $allow.Contains([string]$name)) {
    if ($allowInsecureBypass) {
      if ($Log) { & $Log ('  WARN [SEC] {0} nicht in Allowlist – Insecure-Bypass aktiv' -f $name) }
      $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = $true
      return $true
    }
    if ($Log) { & $Log ('  FEHLER [SEC] {0} nicht in Allowlist – Ausführung blockiert' -f $name) }
    $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = $false
    return $false
  }

  $expected = [string]$allow[[string]$name]
  if ([string]::IsNullOrWhiteSpace($expected)) {
    if ($allowInsecureBypass) {
      if ($Log) { & $Log ('  WARN [SEC] Allowlist-Eintrag leer für {0} – Insecure-Bypass aktiv' -f $name) }
      $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = $true
      return $true
    }
    if ($Log) { & $Log ('  FEHLER [SEC] Allowlist-Eintrag leer für {0} – Ausführung blockiert' -f $name) }
    $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = $false
    return $false
  }

  $actual = [string](Get-FileHash -LiteralPath $ToolPath -Algorithm SHA256).Hash
  $ok = ($actual.ToLowerInvariant() -eq $expected.Trim().ToLowerInvariant())
  if (-not $ok -and $Log) {
    & $Log ('  FEHLER Tool-Hash ungültig: {0}' -f $ToolPath)
  }
  $script:TOOL_HASH_VERDICT_CACHE[$cacheKey] = [bool]$ok
  return [bool]$ok
}

function Reset-ArchiveEntryCache {
  Reset-LruCache -Cache $script:ARCHIVE_ENTRY_LRU
}

function Get-ArchiveEntryCacheStatistics {
  $stats = Get-LruCacheStatistics -Cache $script:ARCHIVE_ENTRY_LRU
  return [pscustomobject]@{
    Hits       = [long]$stats.Hits
    Misses     = [long]$stats.Misses
    Total      = [long]($stats.Hits + $stats.Misses)
    HitRate    = [double]$stats.HitRate
    Entries    = [int]$stats.Count
    MaxEntries = [int](Get-ArchiveEntryCacheMaxEntries)
  }
}

function Get-ArchiveEntryCacheMaxEntries {
  $defaultMax = 5000
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      $cfg = Get-AppStateValue -Key 'ArchiveEntryCacheMaxEntries' -Default $defaultMax
      if ($cfg -is [int] -or $cfg -is [long]) {
        return [int][math]::Max(100, [int64]$cfg)
      }
    } catch { }
  }
  return $defaultMax
}

function Set-ArchiveEntryCacheValue {
  param(
    [Parameter(Mandatory=$true)][string]$Key,
    [Parameter(Mandatory=$true)][object]$Value
  )
  $script:ARCHIVE_ENTRY_LRU.MaxEntries = [int](Get-ArchiveEntryCacheMaxEntries)
  Set-LruCacheValue -Cache $script:ARCHIVE_ENTRY_LRU -Key $Key -Value $Value
}

function Get-ArchiveEntryCacheValue {
  param([string]$Key)
  return (Get-LruCacheValue -Cache $script:ARCHIVE_ENTRY_LRU -Key $Key)
}

function Stop-ExternalProcessTree {
  param([Parameter(Mandatory=$true)][System.Diagnostics.Process]$Process)

  try {
    if (-not $Process.HasExited) {
      # TOOLS-008 FIX: Kill($true) (kill process tree) is only available in .NET Core+.
      # On .NET Framework (PS 5.1), use taskkill /T for tree-kill, then fall back to Kill().
      if ($PSVersionTable.PSVersion.Major -ge 7) {
        try { $Process.Kill($true) } catch { $Process.Kill() }
      } else {
        try { & taskkill /PID $Process.Id /T /F 2>$null | Out-Null } catch { }
        try { $Process.Kill() } catch { }
      }
    }
  } catch { }
  try { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue } catch { }
  try {
    if (-not $Process.HasExited) {
      & taskkill /PID $Process.Id /T /F 2>$null | Out-Null
    }
  } catch { }
  try { $Process.WaitForExit(3000) } catch { }
}

function Invoke-NativeProcess {
  <# Shared process runner: temp-file redirection, Start-Process, Wait-ProcessResponsive, read output, cleanup.
     Returns [pscustomobject]@{ ExitCode; StdOut; StdErr }. #>
  param(
    [Parameter(Mandatory=$true)][string]$ExePath,
    [string[]]$ArgumentList,
    [string]$WorkingDirectory,
    [System.Collections.Generic.List[string]]$TempFiles
  )

  $outFile = New-TemporaryFile
  $errFile = New-TemporaryFile
  if ($TempFiles) {
    [void]$TempFiles.Add($outFile.FullName)
    [void]$TempFiles.Add($errFile.FullName)
  }

  $startArgs = @{
    FilePath               = $ExePath
    PassThru               = $true
    WindowStyle            = 'Hidden'
    RedirectStandardOutput = $outFile
    RedirectStandardError  = $errFile
  }

  if ($ArgumentList -and $ArgumentList.Count -gt 0) { $startArgs['ArgumentList'] = $ArgumentList }
  if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $startArgs['WorkingDirectory'] = $WorkingDirectory
  }

  $proc = $null
  try {
    $proc = Start-Process @startArgs

    Wait-ProcessResponsive -Process $proc

    return [pscustomobject]@{
      ExitCode = $proc.ExitCode
      StdOut   = (Get-Content -LiteralPath $outFile -Raw -ErrorAction SilentlyContinue)
      StdErr   = (Get-Content -LiteralPath $errFile -Raw -ErrorAction SilentlyContinue)
    }
  } finally {
    if ($proc) {
      try {
        if (-not $proc.HasExited) { Stop-ExternalProcessTree -Process $proc }
      } catch { }
      try { $proc.Dispose() } catch { }
    }
    if (-not $TempFiles) {
      if ($outFile -and (Test-Path -LiteralPath $outFile)) { Remove-Item -LiteralPath $outFile -Force -ErrorAction SilentlyContinue }
      if ($errFile -and (Test-Path -LiteralPath $errFile)) { Remove-Item -LiteralPath $errFile -Force -ErrorAction SilentlyContinue }
    }
  }
}

function Invoke-WithPluginOverride {
  <# Checks for operation plugins and falls back to CoreAction if no plugin handled the operation. #>
  param(
    [Parameter(Mandatory=$true)][string]$Phase,
    [Parameter(Mandatory=$true)][hashtable]$Context,
    [Parameter(Mandatory=$true)][scriptblock]$CoreAction,
    [scriptblock]$Log
  )

  if (Get-Command Invoke-OperationPlugins -ErrorAction SilentlyContinue) {
    try {
      $pluginResults = @(Invoke-OperationPlugins -Phase $Phase -Context $Context -Log $Log)
      $handled = @($pluginResults | Where-Object {
        $_.Status -eq 'OK' -and $_.Result -and
        ($_.Result.PSObject.Properties.Name -contains 'PluginHandled') -and
        [bool]$_.Result.PluginHandled
      } | Select-Object -First 1)
      if ($handled.Count -gt 0) {
        return $handled[0].Result
      }
    } catch {
      if ($Log) { & $Log ("WARNUNG: {0}-Plugin konnte nicht ausgefuehrt werden ({1}). Fallback auf Core-Implementierung." -f $Phase, $_.Exception.Message) }
    }
  }

  return (& $CoreAction)
}

function Invoke-ExternalToolProcess {
  <# Runs external tool with redirected stdout/stderr and unified error reporting.
     Uses a non-blocking poll loop so the WinForms UI stays responsive and
  AppState cancel signaling can interrupt long-running conversions. #>
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ToolPath,
    [string[]]$ToolArgs,
    [string]$WorkingDirectory,
    [System.Collections.Generic.List[string]]$TempFiles,
    [scriptblock]$Log,
    [string]$ErrorLabel
  )

  if (-not (Test-ToolBinaryHash -ToolPath $ToolPath -Log $Log)) {
    return [pscustomobject]@{ Success = $false; ExitCode = -1; ErrorText = 'tool-hash-mismatch' }
  }

  # BUG-010 FIX: Avoid dual-quoting — callers are expected to pre-quote path arguments
  # via ConvertTo-QuotedArg. Join directly instead of re-quoting through ConvertTo-ArgString
  # which would apply ConvertTo-QuotedArg a second time, risking malformed command lines
  # for paths containing embedded quotes or trailing backslashes.
  $argLine = if ($ToolArgs -and $ToolArgs.Count -gt 0) {
    ($ToolArgs | ForEach-Object { [string]$_ }) -join ' '
  } else { $null }
  $nativeArgs = @{ ExePath = $ToolPath; TempFiles = $TempFiles }
  if (-not [string]::IsNullOrWhiteSpace($argLine)) { $nativeArgs['ArgumentList'] = @($argLine) }
  if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $nativeArgs['WorkingDirectory'] = $WorkingDirectory }

  $run = Invoke-NativeProcess @nativeArgs

  if ($run.ExitCode -ne 0) {
    if ($Log) {
      if ($run.StdErr) {
        & $Log ("  FEHLER {0} ({1}): {2}" -f $ErrorLabel, $run.ExitCode, $run.StdErr.Trim())
      } else {
        & $Log ("  FEHLER {0} ({1})" -f $ErrorLabel, $run.ExitCode)
      }
    }
    return [pscustomobject]@{ Success = $false; ExitCode = $run.ExitCode; ErrorText = $run.StdErr }
  }

  return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
}

function Wait-ProcessResponsive {
  <# Wartet auf einen Prozess, pumpt dabei DoEvents und prüft Cancel. #>
  param([Parameter(Mandatory=$true)][System.Diagnostics.Process]$Process, [int]$PollMs = 150)

  $getCancelRequested = {
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      try {
        return [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false)
      } catch {
        return $false
      }
    }
    return $false
  }

  $maxWaitMs = 300000
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      $cfg = Get-AppStateValue -Key 'ExternalToolTimeoutMs' -Default 300000
      if ($cfg -is [int] -or $cfg -is [long]) {
        $maxWaitMs = [int][math]::Max(0, [int64]$cfg)
      }
    } catch { }
  }

  $timer = [System.Diagnostics.Stopwatch]::StartNew()
  try {
    while (-not $Process.WaitForExit($PollMs)) {
      # OPT-05: Only pump UI if running in STA thread (GUI mode)
      if ([System.Threading.Thread]::CurrentThread.ApartmentState -eq [System.Threading.ApartmentState]::STA) {
        Invoke-UiPump
      }
      if (& $getCancelRequested) {
        Stop-ExternalProcessTree -Process $Process
        throw [System.OperationCanceledException]::new("Abbruch durch Benutzer")
      }
      if ($maxWaitMs -gt 0 -and $timer.ElapsedMilliseconds -ge $maxWaitMs) {
        Stop-ExternalProcessTree -Process $Process
        throw [System.TimeoutException]::new("Tool-Timeout nach $maxWaitMs ms")
      }
    }
  } catch [System.OperationCanceledException] {
    throw
  } catch [System.TimeoutException] {
    throw
  } catch {
    if (-not $Process.HasExited) {
      Stop-ExternalProcessTree -Process $Process
    }
    throw
  }
}

function Find-ConversionTool {
  <# Find conversion tools in PATH or known locations. #>
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ToolName)

  $found = Get-Command $ToolName -ErrorAction SilentlyContinue
  if ($found) { return $found.Source }

  # BUG TOOLS-001 FIX: Only search in non-user-writable locations to prevent binary planting.
  # User-writable paths (Downloads, LOCALAPPDATA, USERPROFILE) removed.
  # Hash verification via Test-ToolBinaryHash provides defense-in-depth.
  $candidates = switch ($ToolName) {
    'chdman' {
      @(
        "$env:ProgramFiles\MAME\chdman.exe",
        "${env:ProgramFiles(x86)}\MAME\chdman.exe",
        "C:\MAME\chdman.exe"
      )
    }
    'dolphintool' {
      @(
        "$env:ProgramFiles\Dolphin\DolphinTool.exe",
        "${env:ProgramFiles(x86)}\Dolphin\DolphinTool.exe"
      )
    }
    '7z' {
      @(
        "$env:ProgramFiles\7-Zip\7z.exe",
        "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
      )
    }
    'psxtract' {
      @(
        "C:\tools\conversion\psxtract.exe"
      )
    }
    'ciso' {
      @(
        "C:\tools\conversion\ciso.exe"
      )
    }
    default { @() }
  }

  foreach ($c in $candidates) {
    if ($c -and (Test-Path -LiteralPath $c)) { return $c }
  }

  return $null
}

function ConvertTo-QuotedArg {
  <# Quote paths for native tools; preserve existing quotes. #>
  param([string]$Value)

  # BUG-004 FIX: Return empty quoted string for null/empty input instead of bare empty string
  if ($null -eq $Value) { return '""' }
  $s = [string]$Value
  if ($s -eq '') { return '""' }
  if ($s.Length -ge 2 -and $s.StartsWith('"') -and $s.EndsWith('"')) { return $s }
  if ($s -notmatch '[\s"]' -and -not $s.StartsWith('-')) { return $s }

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.Append('"')
  $bsCount = 0
  foreach ($ch in $s.ToCharArray()) {
    if ($ch -eq '\') {
      $bsCount++
      continue
    }
    if ($ch -eq '"') {
      [void]$sb.Append(('\' * ($bsCount * 2 + 1)))
      [void]$sb.Append('"')
      $bsCount = 0
      continue
    }
    if ($bsCount -gt 0) {
      [void]$sb.Append(('\' * $bsCount))
      $bsCount = 0
    }
    [void]$sb.Append($ch)
  }
  if ($bsCount -gt 0) {
    [void]$sb.Append(('\' * ($bsCount * 2)))
  }
  [void]$sb.Append('"')
  return $sb.ToString()
}

function ConvertTo-ArgString {
  <# Build a single command-line string with proper quoting. #>
  param([string[]]$ArgList)

  if (-not $ArgList -or $ArgList.Count -eq 0) { return $null }
  return ($ArgList | ForEach-Object { ConvertTo-QuotedArg $_ }) -join ' '
}

function Invoke-7z {
  <# Run 7z with standard output/error capture and normalized result object. #>
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$SevenZipPath,
    [Parameter(Mandatory=$true)][string[]]$Arguments,
    [System.Collections.Generic.List[string]]$TempFiles
  )

  # BUG TOOLS-003 FIX: Verify binary hash before execution
  if (-not (Test-ToolBinaryHash -ToolPath $SevenZipPath)) {
    Write-Warning ('[SEC] 7z binary hash verification failed: {0}' -f $SevenZipPath)
    return [pscustomobject]@{ ExitCode = -1; StdOut = ''; StdErr = 'Hash verification failed' }
  }

  return (Invoke-NativeProcess -ExePath $SevenZipPath -ArgumentList $Arguments -TempFiles $TempFiles)
}

function Test-ArchiveEntryPathsSafe {
  <# Detect zip-slip patterns in archive entry paths. #>
  param([string[]]$EntryPaths)

  foreach ($p in $EntryPaths) {
    if ([string]::IsNullOrWhiteSpace($p)) { continue }
    if ([IO.Path]::IsPathRooted($p)) { return $false }
    if ($p -match '(^|[\\/])\.\.([\\/]|$)') { return $false }
  }
  return $true
}

function Get-ArchiveEntryPaths {
  <# List archive entries using 7z -slt output. #>
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$ArchivePath,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$SevenZipPath,
    [System.Collections.Generic.List[string]]$TempFiles
  )

  $cacheKey = $null
  try {
    # PERF-04: FileInfo statt Get-Item (vermeidet PowerShell-Provider-Overhead)
    $fi = [System.IO.FileInfo]::new($ArchivePath)
    if (-not $fi.Exists) { return @() }
    $cacheKey = "{0}|{1}|{2}" -f $fi.FullName, $fi.Length, $fi.LastWriteTimeUtc.Ticks
  } catch {
    return @()
  }

  $cachedEntries = Get-ArchiveEntryCacheValue -Key $cacheKey
  if ($null -ne $cachedEntries) {
    return @($cachedEntries)
  }

  $toolArgs = @('l', '-slt', (ConvertTo-QuotedArg $ArchivePath))
  $run = Invoke-7z -SevenZipPath $SevenZipPath -Arguments $toolArgs -TempFiles $TempFiles

  if ($run.ExitCode -ne 0) {
    return @()
  }

  $lines = @([string]$run.StdOut -split "`r?`n")
  $paths = New-Object System.Collections.Generic.List[string]
  $collectEntries = $false
  $foundSeparator = $false
  foreach ($line in $lines) {
    Test-CancelRequested
    if ($line -match '^-{5,}$') {
      $collectEntries = $true
      $foundSeparator = $true
      continue
    }

    if ($line -match '^Path = (.+)$') {
      if (-not $collectEntries) { continue }
      [void]$paths.Add($Matches[1])
    }
  }

  # Fallback for variants without separator: parse Path lines and remove archive metadata path.
  if ($paths.Count -eq 0 -and -not $foundSeparator) {
    $archiveFull = $null
    $archiveLeaf = $null
    try {
      $archiveFull = (Get-Item -LiteralPath $ArchivePath -ErrorAction Stop).FullName
      $archiveLeaf = [IO.Path]::GetFileName($archiveFull)
    } catch { }

    $lineIdx = 0
    foreach ($line in $lines) {
      # PERF-05: Cancel-Check alle 100 Zeilen statt pro Zeile
      if ((++$lineIdx % 100) -eq 0) { Test-CancelRequested }
      if ($line -notmatch '^Path = (.+)$') { continue }
      $candidate = $Matches[1]
      if ($archiveFull -and $candidate.Equals($archiveFull, [StringComparison]::OrdinalIgnoreCase)) { continue }
      if ($archiveLeaf -and $candidate.Equals($archiveLeaf, [StringComparison]::OrdinalIgnoreCase)) { continue }
      [void]$paths.Add($candidate)
    }
  }

  $result = @($paths)

  # SEC-01: ZipSlip-Pruefung VOR dem Caching - unsichere Pfade nicht cachen
  if ($result.Count -gt 0 -and -not (Test-ArchiveEntryPathsSafe -EntryPaths $result)) {
    return @()
  }

  Set-ArchiveEntryCacheValue -Key $cacheKey -Value $result
  return $result
}

function Get-ConsoleFromArchiveEntries {
  <# Try to infer console from archive entry file extensions. #>
  param([string[]]$EntryPaths)

  if (-not $EntryPaths -or $EntryPaths.Count -eq 0) { return $null }
  $counts = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:LAST_ARCHIVE_MIXED_CONSOLES = $null
  $entryIdx = 0

  foreach ($p in $EntryPaths) {
    # PERF-05: Cancel-Check alle 100 Entries statt pro Entry
    if ((++$entryIdx % 100) -eq 0) { Test-CancelRequested }
    if ([string]::IsNullOrWhiteSpace($p)) { continue }
    $name = [IO.Path]::GetFileName($p)
    if (-not $name) { continue }
    $ext = [IO.Path]::GetExtension($name).ToLowerInvariant()
    if (-not $ext) { continue }

    $console = $null
    if ($script:CONSOLE_EXT_MAP.ContainsKey($ext)) {
      $console = $script:CONSOLE_EXT_MAP[$ext]
    }

    if (-not $console) { continue }
    if (-not $counts.ContainsKey($console)) { $counts[$console] = 0 }
    $counts[$console]++
    # PERF-CS-04: Early-Exit wenn genau 1 Konsole und bereits unique Extension gefunden
    if ($counts.Count -eq 1 -and $counts[$console] -ge 2) {
      # Mindestens 2 Treffer derselben Konsole — hinreichend sicher
      $script:LAST_ARCHIVE_HAS_DISC_SET = $false
      return $console
    }
  }

  if ($counts.Count -eq 1) {
    return $counts.Keys | Select-Object -First 1
  }

  # MISS-CS-06: Mixed-Content Archive — Diagnose welche Konsolen gefunden
  if ($counts.Count -gt 1) {
    $script:LAST_ARCHIVE_MIXED_CONSOLES = @($counts.Keys)
  }

  # BUG-CS-03: Disc-Set-Marker erkennen (.cue/.ccd → Disc-Image, .gdi → DC via EXT_MAP)
  $script:LAST_ARCHIVE_HAS_DISC_SET = $false
  foreach ($p2 in $EntryPaths) {
    if ([string]::IsNullOrWhiteSpace($p2)) { continue }
    $n2 = [IO.Path]::GetFileName($p2)
    if (-not $n2) { continue }
    $e2 = [IO.Path]::GetExtension($n2).ToLowerInvariant()
    if ($e2 -in @('.cue', '.ccd', '.gdi', '.mds')) {
      $script:LAST_ARCHIVE_HAS_DISC_SET = $true
      break
    }
  }

  return $null
}

function Get-ConsoleFromDiscId {
  param([string]$DiscId)
  if ([string]::IsNullOrWhiteSpace($DiscId)) { return $null }
  $first = $DiscId.Substring(0, 1).ToUpperInvariant()
  switch ($first) {
    # BUG-CS-05: Erweiterte Disc-ID-Prefixes (Nintendo Disc-ID-Registrierung)
    'G' { return 'GC' }
    'D' { return 'WII' }   # Wii Demo Discs
    'R' { return 'WII' }
    'S' { return 'WII' }
    'W' { return 'WII' }
    'Z' { return 'WII' }
    'P' { return 'GC' }    # GC Promotional / Promo Discs
    'H' { return 'WII' }   # Wii Channels
    'X' { return 'WII' }   # Wii Expansion / Demo Discs
    'C' { return 'WII' }   # Wii Fitness Disc
    default { return $null }
  }
}

function Invoke-DolphinToolInfoLines {
  <# Executes 'dolphintool info -i <path>' and returns output lines or $null on failure. #>
  param(
    [string]$Path,
    [string]$ToolPath
  )

  if (-not $ToolPath -or -not (Test-Path -LiteralPath $ToolPath)) { return $null }
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }

  # BUG TOOLS-005 FIX: Verify binary hash before execution
  if (-not (Test-ToolBinaryHash -ToolPath $ToolPath)) {
    Write-Warning ('[SEC] DolphinTool binary hash verification failed: {0}' -f $ToolPath)
    return $null
  }

  try {
    $toolArgs = @('info', '-i', (ConvertTo-QuotedArg $Path))
    $run = Invoke-NativeProcess -ExePath $ToolPath -ArgumentList $toolArgs

    if ($run.ExitCode -ne 0) { return $null }
    if ([string]::IsNullOrEmpty($run.StdOut)) { return @() }
    return @($run.StdOut -split "`r?`n")
  } catch {
    return $null
  }
}

function Get-DiscIdFromDolphinTool {
  <# Extract Game ID using DolphinTool info. #>
  param(
    [string]$Path,
    [string]$ToolPath
  )

  $lines = @(Invoke-DolphinToolInfoLines -Path $Path -ToolPath $ToolPath)
  if ($lines.Count -eq 0) { return $null }

  foreach ($line in $lines) {
    Test-CancelRequested
    if ($line -match 'Game\s*ID\s*[:=]\s*([A-Z0-9]{6})') { return $Matches[1] }
    if ($line -match 'GameID\s*[:=]\s*([A-Z0-9]{6})') { return $Matches[1] }
    if ($line -match '^ID\s*[:=]\s*([A-Z0-9]{6})') { return $Matches[1] }
    if ($line -match 'Title\s*ID\s*[:=]\s*([A-Z0-9]{6})') { return $Matches[1] }
  }

  return $null
}

function Get-ConsoleFromDolphinTool {
  param(
    [string]$Path,
    [string]$ToolPath
  )

  $discId = Get-DiscIdFromDolphinTool -Path $Path -ToolPath $ToolPath
  return Get-ConsoleFromDiscId -DiscId $discId
}

function Get-ConsoleFromDiscIdInFileName {
  <# Fallback: infer GC/Wii from disc ID token in filename (e.g. [RZDE01]). #>
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }

  $name = [IO.Path]::GetFileNameWithoutExtension($Path)
  if ([string]::IsNullOrWhiteSpace($name)) { return $null }

  $upper = $name.ToUpperInvariant()

  if ($upper -match '\[([A-Z0-9]{6})\]') {
    $discId = [string]$Matches[1]
    return Get-ConsoleFromDiscId -DiscId $discId
  }

  if ($upper -match '(?:^|[^A-Z0-9])([GRSWZ][A-Z0-9]{5})(?:[^A-Z0-9]|$)') {
    $discId = [string]$Matches[1]
    return Get-ConsoleFromDiscId -DiscId $discId
  }

  return $null
}

function Test-WbfsAccurateFromDolphinTool {
  <# Returns $true only when DolphinTool reports Data Size Type = Accurate. #>
  param(
    [string]$Path,
    [string]$ToolPath
  )

  $lines = @(Invoke-DolphinToolInfoLines -Path $Path -ToolPath $ToolPath)
  if ($lines.Count -eq 0) { return $false }

  foreach ($line in $lines) {
    Test-CancelRequested
    if ($line -match 'Data\s*Size\s*Type\s*[:=]\s*Accurate\b') { return $true }
    if ($line -match 'Data\s*Size\s*Type\s*[:=]\s*(.+)$') { return $false }
  }

  return $false
}

function Get-ArchiveDiscHeaderConsole {
  <# Read disc header from ISO/GCM inside archive using 7z -so.
     BUG-11 fix: reads 128KB instead of 6 bytes to detect PS1/PS2/DC/SAT/SCD/etc.
     Falls back to 6-byte DiscID detection for GC/Wii if full header scan fails. #>
  param(
    [string]$ArchivePath,
    [string]$SevenZipPath
  )

  if (-not $SevenZipPath -or -not (Test-Path -LiteralPath $SevenZipPath)) { return $null }
  if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) { return $null }

  # BUG TOOLS-004 FIX: Verify binary hash before execution
  if (-not (Test-ToolBinaryHash -ToolPath $SevenZipPath)) {
    Write-Warning ('[SEC] 7z binary hash verification failed (ArchiveDiscHeader): {0}' -f $SevenZipPath)
    return $null
  }

  $entryPaths = @(Get-ArchiveEntryPaths -ArchivePath $ArchivePath -SevenZipPath $SevenZipPath -TempFiles $null)
  if (-not $entryPaths -or $entryPaths.Count -eq 0) { return $null }

  # SEC-02: ZipSlip-Check vor Zugriff auf Entry-Pfade
  if (-not (Test-ArchiveEntryPathsSafe -EntryPaths $entryPaths)) { return $null }

  # BUG-CS-03: Auch .bin als Disc-Image-Kandidat (CUE/BIN-Sets enthalten Disc-Daten)
  $target = $entryPaths | Where-Object {
    $name = [IO.Path]::GetFileName($_)
    $ext = [IO.Path]::GetExtension($name).ToLowerInvariant()
    if ($ext -in @('.iso', '.gcm', '.bin')) { return $true }
    if ($name -match '\.nkit\.iso$') { return $true }
    if ($name -match '\.nkit\.gcz$') { return $true }
    return $false
  } | Select-Object -First 1

  if (-not $target) { return $null }

  $proc = $null
  try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $SevenZipPath
    $psi.Arguments = ConvertTo-ArgString @('x', '-so', $ArchivePath, $target)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()

    # BUG-11 fix: Read up to 128KB for full disc header detection
    $scanSize = 131072
    $buffer = New-Object byte[] $scanSize
    $totalRead = 0
    while ($totalRead -lt $scanSize) {
      $chunk = $proc.StandardOutput.BaseStream.Read($buffer, $totalRead, ($scanSize - $totalRead))
      if ($chunk -le 0) { break }
      $totalRead += $chunk
    }

    try { if (-not $proc.HasExited) { Stop-ExternalProcessTree -Process $proc } } catch { }

    if ($totalRead -lt 6) { return $null }

    # Try full disc header detection (same logic as Get-DiscHeaderConsole)
    if ($totalRead -ge 32 -and (Get-Command Get-DiscHeaderConsole -ErrorAction SilentlyContinue)) {
      # Write buffer to temp file for header detection
      $tempHeaderFile = Join-Path $env:TEMP ("rom_archdr_" + [guid]::NewGuid().ToString("N") + ".bin")
      try {
        [System.IO.File]::WriteAllBytes($tempHeaderFile, $buffer[0..([Math]::Min($totalRead, $scanSize) - 1)])
        $headerResult = Get-DiscHeaderConsole -Path $tempHeaderFile
        if ($headerResult) { return $headerResult }
      } finally {
        if (Test-Path -LiteralPath $tempHeaderFile) {
          Remove-Item -LiteralPath $tempHeaderFile -Force -ErrorAction SilentlyContinue
        }
      }
    }

    # Fallback: 6-byte DiscID detection (GC/Wii)
    $id = [System.Text.Encoding]::ASCII.GetString($buffer, 0, [Math]::Min(6, $totalRead))
    return Get-ConsoleFromDiscId -DiscId $id
  } catch {
    return $null
  } finally {
    if ($proc) {
      try {
        if (-not $proc.HasExited) { Stop-ExternalProcessTree -Process $proc }
      } catch { }
      try { $proc.Dispose() } catch { }
    }
  }
}

function Expand-ArchiveToTemp {
  <# Extract ZIP/7Z to a temp folder using 7z. #>
  param(
    [string]$ArchivePath,
    [string]$SevenZipPath,
    [scriptblock]$Log
  )

  if (-not $SevenZipPath -or -not (Test-Path -LiteralPath $SevenZipPath)) {
    if ($Log) { & $Log "  FEHLER 7z: 7z.exe nicht gefunden fuer Entpacken." }
    return $null
  }

  $tempDir = Join-Path $env:TEMP ("rom_dedupe_extract_" + [guid]::NewGuid().ToString("N"))
  [void](New-Item -ItemType Directory -Path $tempDir)
  $tempFiles = [System.Collections.Generic.List[string]]::new()
  $ext = [IO.Path]::GetExtension($ArchivePath).ToLowerInvariant()

  try {
    if ($ext -ne '.gz') {
      $entryPaths = @(Get-ArchiveEntryPaths -ArchivePath $ArchivePath -SevenZipPath $SevenZipPath -TempFiles $tempFiles)
      if ($entryPaths.Count -gt 0 -and -not (Test-ArchiveEntryPathsSafe -EntryPaths $entryPaths)) {
        if ($Log) { & $Log ("  SKIP (ZIP Slip erkannt): {0}" -f $ArchivePath) }
        if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
          Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        return 'SKIP'
      }
      # BUG-031 FIX: Check archive bomb — file count limit and decompressed size estimate
      $maxEntryCount = 10000
      if ($entryPaths.Count -gt $maxEntryCount) {
        if ($Log) { & $Log ("  SKIP (Archive Bomb: {0} Eintraege > {1} Limit): {2}" -f $entryPaths.Count, $maxEntryCount, $ArchivePath) }
        if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
          Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        return 'SKIP'
      }
      # BUG-031 FIX (cont.): Estimate decompressed size from 7z listing
      $maxDecompressedBytes = 50GB
      try {
        $sizeArgs = @('l', '-slt', (ConvertTo-QuotedArg $ArchivePath))
        $sizeRun = Invoke-7z -SevenZipPath $SevenZipPath -Arguments $sizeArgs -TempFiles $null
        if ($sizeRun.ExitCode -eq 0) {
          $totalSize = [int64]0
          foreach ($sizeLine in @([string]$sizeRun.StdOut -split "`r?`n")) {
            if ($sizeLine -match '^Size = (\d+)$') {
              $totalSize += [int64]$Matches[1]
              if ($totalSize -gt $maxDecompressedBytes) { break }
            }
          }
          if ($totalSize -gt $maxDecompressedBytes) {
            $sizeMB = [Math]::Round($totalSize / 1MB, 0)
            if ($Log) { & $Log ("  SKIP (Archive Bomb: {0} MB entpackt > {1} GB Limit): {2}" -f $sizeMB, [Math]::Round($maxDecompressedBytes / 1GB, 0), $ArchivePath) }
            if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
              Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            return 'SKIP'
          }
        }
      } catch { }
    }

    # TOOLS-007 FIX: Quote the output path to handle spaces in TEMP path
    $outArg = '-o"{0}"' -f ([System.IO.Path]::GetFullPath($tempDir))
    $toolArgs = @('x', '-y', $outArg, (ConvertTo-QuotedArg $ArchivePath))
    $run = Invoke-7z -SevenZipPath $SevenZipPath -Arguments $toolArgs -TempFiles $tempFiles
    $exitCode = $run.ExitCode
    if ($exitCode -ne 0) {
      $err = $run.StdErr
      $out = $run.StdOut
      $details = ($err + "`n" + $out)
      if ($details) {
        $details = (($details -replace '[\r\n]+', ' ').Trim())
        if ($details.Length -gt 220) { $details = $details.Substring(0, 220) + '…' }
      }
      if ($Log) {
        if ($details) {
          & $Log ("  SKIP (ZIP unlesbar): {0} {1}" -f $ArchivePath, $details)
        } else {
          & $Log ("  SKIP (ZIP unlesbar): {0}" -f $ArchivePath)
        }
      }
      if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
      return 'SKIP'
    }

    $extracted = @(Get-ChildItem -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue)
    foreach ($ef in $extracted) {
      Test-CancelRequested
      if (-not (Test-PathWithinRoot -Path $ef.FullName -Root $tempDir)) {
        if ($Log) { & $Log ("  SKIP (ZIP Slip nach Entpacken): {0}" -f $ArchivePath) }
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        return 'SKIP'
      }
      if ($ef.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        if ($Log) { & $Log ("  SKIP (ReparsePoint im Archiv): {0}" -f $ArchivePath) }
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        return 'SKIP'
      }
    }
  } catch {
    if ($Log) { & $Log ("  SKIP (ZIP unlesbar): {0} {1}" -f $ArchivePath, $_.Exception.Message) }
    if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
      Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    return 'SKIP'
  } finally {
    foreach ($tf in $tempFiles) {
      if ($tf -and (Test-Path -LiteralPath $tf)) {
        Remove-Item -LiteralPath $tf -Force -ErrorAction SilentlyContinue
      }
    }
  }

  return $tempDir
}

function Find-DiscImageInDir {
  <# Pick a single disc image file from extracted content. #>
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$RootPath)

  $files = @(Get-FilesSafe -Root $RootPath)
  if (-not $files -or $files.Count -eq 0) { return @() }

  $cue = @($files | Where-Object { $_.Extension -eq '.cue' } | Sort-Object FullName)
  if ($cue.Count -gt 0) { return $cue }

  $gdi = @($files | Where-Object { $_.Extension -eq '.gdi' } | Sort-Object FullName)
  if ($gdi.Count -gt 0) { return $gdi }

  $iso = @($files | Where-Object { $_.Extension -eq '.iso' } | Sort-Object FullName)
  if ($iso.Count -gt 0) { return $iso }

  return @()
}
