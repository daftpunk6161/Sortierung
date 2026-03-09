# ================================================================
#  LOGGING  (extracted from simple_sort.ps1 - ARCH-09)
# ================================================================
# Contains: Write-OperationJsonlLog,
#           Resolve-OperationJsonlSourcePath, Export-LogSnapshotText,
#           Export-LogSnapshotJsonl
# Dependencies: Get-AppStateValue, Set-AppStateValue (Settings.ps1)
# ================================================================

function ConvertTo-LogLevel {
  param([string]$MessageText)

  $text = [string]$MessageText
  if ([string]::IsNullOrWhiteSpace($text)) { return 'Info' }

  if ($text -match '^(DEBUG|DBG)\s*[:\-\]]') { return 'Debug' }
  if ($text -match '^(INFO)\s*[:\-\]]') { return 'Info' }
  if ($text -match '^(WARN|WARNING|WARNUNG)\s*[:\-\]]') { return 'Warning' }
  if ($text -match '^(ERROR|ERR|FEHLER)\s*[:\-\]]') { return 'Error' }

  return 'Info'
}

function ConvertTo-LogMessageText {
  param([Parameter(ValueFromPipeline = $true)]$Value)

  if ($null -eq $Value) { return '' }

  if ($Value -is [System.Exception]) {
    return [string]$Value.Message
  }

  if ($Value -is [System.Array]) {
    $parts = @($Value | ForEach-Object { ConvertTo-LogMessageText $_ })
    return ($parts -join '; ')
  }

  return [string]$Value
}

function Get-LogLevelRank {
  param([string]$Level)

  switch ([string]$Level) {
    'Debug'   { return 10 }
    'Info'    { return 20 }
    'Warning' { return 30 }
    'Error'   { return 40 }
    default   { return 20 }
  }
}

function Get-EffectiveLogLevel {
  $configured = [string](Get-AppStateValue -Key 'LogLevel' -Default 'Info')
  if ([string]::IsNullOrWhiteSpace($configured)) { return 'Info' }

  switch ($configured.ToLowerInvariant()) {
    'debug'   { return 'Debug' }
    'info'    { return 'Info' }
    'warning' { return 'Warning' }
    'error'   { return 'Error' }
    default   { return 'Info' }
  }
}

function Test-ShouldWriteLogMessage {
  param([string]$MessageText)

  $messageLevel = ConvertTo-LogLevel -MessageText $MessageText
  $threshold = Get-EffectiveLogLevel
  return ((Get-LogLevelRank -Level $messageLevel) -ge (Get-LogLevelRank -Level $threshold))
}

function Get-OperationCorrelationId {
  $existing = ConvertTo-LogMessageText (Get-AppStateValue -Key 'OperationCorrelationId' -Default '')
  if (-not [string]::IsNullOrWhiteSpace($existing)) { return $existing }

  $newId = [guid]::NewGuid().ToString('N')
  [void](Set-AppStateValue -Key 'OperationCorrelationId' -Value $newId)
  return $newId
}

function Invoke-JsonlLogRotation {
    param(
      [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Path,
      [long]$MaxBytes = 0,
      [int]$KeepFiles = 0,
      [switch]$Compress
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return $Path }
    if ($MaxBytes -le 0) { return $Path }

    try {
      if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $Path }

      $fileInfo = Get-Item -LiteralPath $Path -ErrorAction Stop
      if (-not $fileInfo -or [long]$fileInfo.Length -lt [long]$MaxBytes) { return $Path }

      $parent = Split-Path -Parent $Path
      if (-not [string]::IsNullOrWhiteSpace($parent)) {
        Assert-DirectoryExists -Path $parent
      }

      $baseName = [System.IO.Path]::GetFileNameWithoutExtension($Path)
      $extension = [System.IO.Path]::GetExtension($Path)
      $archiveName = '{0}-{1}{2}' -f $baseName, (Get-Date -Format 'yyyyMMdd-HHmmssfff'), $extension
      $archivePath = Join-Path $parent $archiveName

      Move-Item -LiteralPath $Path -Destination $archivePath -Force -ErrorAction Stop

      # GZIP-compress rotated log file
      if ($Compress) {
        $gzPath = Compress-LogFileGzip -SourcePath $archivePath
        if ($gzPath -and (Test-Path -LiteralPath $gzPath -PathType Leaf)) {
          $archivePath = $gzPath
          $extension = '{0}.gz' -f $extension
        }
      }

      if ($KeepFiles -gt 0 -and -not [string]::IsNullOrWhiteSpace($parent)) {
        $pattern = '{0}-*{1}' -f $baseName, $extension
        $archives = @(Get-ChildItem -LiteralPath $parent -Filter $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending)
        # Also include uncompressed archives in cleanup
        if ($Compress) {
          $patternPlain = '{0}-*{1}' -f $baseName, ([System.IO.Path]::GetExtension($Path))
          $plainArchives = @(Get-ChildItem -LiteralPath $parent -Filter $patternPlain -File -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTimeUtc -Descending)
          $archives = @(($archives + $plainArchives) | Sort-Object LastWriteTimeUtc -Descending | Select-Object -Unique)
        }

        if ($archives.Count -gt $KeepFiles) {
          $toDelete = @($archives | Select-Object -Skip $KeepFiles)
          foreach ($stale in $toDelete) {
            Remove-Item -LiteralPath $stale.FullName -Force -ErrorAction SilentlyContinue
          }
        }
      }
    } catch {
      # Log rotation is best-effort; errors are logged via CatchGuard when available.
      if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
        [void](Write-CatchGuardLog -Module 'Logging' -Action 'Invoke-JsonlLogRotation' -Exception $_.Exception -Level 'Warning')
      }
    }

    return $Path
}

function Compress-LogFileGzip {
  <#
  .SYNOPSIS
    Compresses a log file using GZIP and removes the original.
  .DESCRIPTION
    Reads the source file, writes a .gz compressed version, and deletes
    the uncompressed original on success.
  .PARAMETER SourcePath
    Path to the log file to compress.
  .OUTPUTS
    [string] Path to the compressed .gz file, or $null on failure.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$SourcePath
  )

  if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) { return $null }

  $gzPath = '{0}.gz' -f $SourcePath
  try {
    $sourceStream = [System.IO.File]::OpenRead($SourcePath)
    try {
      $destStream = [System.IO.File]::Create($gzPath)
      try {
        $gzStream = [System.IO.Compression.GZipStream]::new($destStream, [System.IO.Compression.CompressionMode]::Compress)
        try {
          $sourceStream.CopyTo($gzStream)
        } finally {
          $gzStream.Dispose()
        }
      } finally {
        $destStream.Dispose()
      }
    } finally {
      $sourceStream.Dispose()
    }
    # Remove uncompressed original after successful compression
    Remove-Item -LiteralPath $SourcePath -Force -ErrorAction SilentlyContinue
    return $gzPath
  } catch {
    if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
      [void](Write-CatchGuardLog -Module 'Logging' -Action 'Compress-LogFileGzip' -Exception $_.Exception -Level 'Warning')
    }
    # Clean up partial .gz if exists
    if (Test-Path -LiteralPath $gzPath -PathType Leaf) {
      Remove-Item -LiteralPath $gzPath -Force -ErrorAction SilentlyContinue
    }
    return $null
  }
}

function Write-OperationJsonlLog {
    param(
      [datetime]$Timestamp,
      [string]$MessageText,
      [ValidateSet('Debug','Info','Warning','Error')][string]$Level = 'Info',
      [string]$OperationId,
      [string]$Module,
      [string]$Action,
      [string]$Root,
      [ValidateSet('Transient','Recoverable','Critical')][string]$ErrorClass
    )

    if ([string]::IsNullOrWhiteSpace($script:OperationJsonlPath)) { return }

    try {
      $maxBytes = [long](Get-AppStateValue -Key 'OperationJsonlMaxBytes' -Default 5242880)
      $keepFiles = [int](Get-AppStateValue -Key 'OperationJsonlKeepFiles' -Default 10)
      [void](Invoke-JsonlLogRotation -Path $script:OperationJsonlPath -MaxBytes $maxBytes -KeepFiles $keepFiles -Compress)

      $phase = ConvertTo-LogMessageText (Get-AppStateValue -Key 'CurrentPhase' -Default '')
      $tone = ConvertTo-LogMessageText (Get-AppStateValue -Key 'QuickTone' -Default '')
      $correlationId = [string]$OperationId
      if ([string]::IsNullOrWhiteSpace($correlationId)) {
        $correlationId = Get-OperationCorrelationId
      }
      $record = [ordered]@{
        ts = $Timestamp.ToString('o')
        correlationId = $correlationId
        level = [string]$Level
        phase = $phase
        tone = $tone
        module = [string]$Module
        action = [string]$Action
        root = [string]$Root
        errorClass = [string]$ErrorClass
        message = (ConvertTo-LogMessageText $MessageText)
      }
      $json = ($record | ConvertTo-Json -Compress -Depth 3)
      # BUG LOG-001 FIX: Retry up to 3 times with backoff before disabling logging
      $writeOk = $false
      for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
          Add-Content -LiteralPath $script:OperationJsonlPath -Value $json -Encoding utf8 -ErrorAction Stop
          $writeOk = $true
          break
        } catch {
          if ($attempt -lt 3) {
            Start-Sleep -Milliseconds (100 * $attempt)
          } else {
            Write-Warning ('Write-OperationJsonlLog fehlgeschlagen nach 3 Versuchen [{0}]: {1}' -f $script:OperationJsonlPath, $_.Exception.Message)
            $script:OperationJsonlPath = $null
            [void](Set-AppStateValue -Key 'OperationJsonlPath' -Value $null)
          }
        }
      }
    } catch {
      # F-05 FIX: JSONL log write failure is no longer silently swallowed.
      Write-Warning ('Write-OperationJsonlLog fehlgeschlagen [{0}]: {1}' -f $script:OperationJsonlPath, $_.Exception.Message)
      $script:OperationJsonlPath = $null
      [void](Set-AppStateValue -Key 'OperationJsonlPath' -Value $null)
    }
}

function Resolve-OperationJsonlSourcePath {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath) -and (Test-Path -LiteralPath $PreferredPath -PathType Leaf)) {
      return [string]$PreferredPath
    }

    $activePath = [string](Get-AppStateValue -Key 'OperationJsonlPath' -Default $null)
    if (-not [string]::IsNullOrWhiteSpace($activePath) -and (Test-Path -LiteralPath $activePath -PathType Leaf)) {
      return $activePath
    }

    $lastPath = [string](Get-AppStateValue -Key 'lastJsonlLog' -Default $null)
    if (-not [string]::IsNullOrWhiteSpace($lastPath) -and (Test-Path -LiteralPath $lastPath -PathType Leaf)) {
      return $lastPath
    }

    return $null
}

function Export-LogSnapshotText {
    param(
      [string[]]$Lines,
      [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path
    )

    $targetDir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($targetDir)) {
      Assert-DirectoryExists -Path $targetDir
    }

    $content = if ($Lines -and $Lines.Count -gt 0) {
      ($Lines | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    } else {
      ''
    }

    $content | Out-File -LiteralPath $Path -Encoding utf8 -Force
    return $Path
}

function Export-LogSnapshotJsonl {
    param(
      [string[]]$Lines,
      [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path,
      [string]$SourceJsonlPath
    )

    $targetDir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($targetDir)) {
      Assert-DirectoryExists -Path $targetDir
    }

    $sourcePath = Resolve-OperationJsonlSourcePath -PreferredPath $SourceJsonlPath
    if (-not [string]::IsNullOrWhiteSpace($sourcePath)) {
      Copy-Item -LiteralPath $sourcePath -Destination $Path -Force
      return $Path
    }

    $nowDate = Get-Date
    $jsonLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($Lines)) {
      if ([string]::IsNullOrWhiteSpace([string]$line)) { continue }
      $text = [string]$line
      $timestamp = $nowDate
      $message = $text

      if ($text -match '^\[(\d{2}:\d{2}:\d{2})\]\s*(.*)$') {
        $timePart = [string]$Matches[1]
        $message = [string]$Matches[2]
        try {
          $timestamp = [datetime]::ParseExact(("{0} {1}" -f $nowDate.ToString('yyyy-MM-dd'), $timePart), 'yyyy-MM-dd HH:mm:ss', $null)
        } catch {
          $timestamp = $nowDate
        }
      }

      $record = [ordered]@{
        ts = $timestamp.ToString('o')
        correlationId = ''
        level = (ConvertTo-LogLevel -MessageText $message)
        phase = ''
        tone = ''
        message = (ConvertTo-LogMessageText $message)
      }
      [void]$jsonLines.Add(($record | ConvertTo-Json -Compress -Depth 3))
    }

    if ($jsonLines.Count -eq 0) {
      $emptyRecord = [ordered]@{
        ts = $nowDate.ToString('o')
        correlationId = ''
        level = 'Info'
        phase = ''
        tone = ''
        message = ''
      }
      [void]$jsonLines.Add(($emptyRecord | ConvertTo-Json -Compress -Depth 3))
    }

    $jsonLines | Out-File -LiteralPath $Path -Encoding utf8 -Force
    return $Path
}

function Write-Log {
    param(
      [ValidateSet('Debug','Info','Warning','Error')][string]$Level = 'Info',
      [Parameter(Mandatory=$true)][string]$Message,
      [string]$CorrelationId,
      [string]$Module,
      [string]$Action,
      [string]$Root,
      [ValidateSet('Transient','Recoverable','Critical')][string]$ErrorClass
    )

    $timestamp = Get-Date
    $resolvedCorrelationId = [string]$CorrelationId
    if ([string]::IsNullOrWhiteSpace($resolvedCorrelationId)) {
      $resolvedCorrelationId = Get-OperationCorrelationId
    }

    $line = ('[{0}] [{1}] [{2}] {3}' -f $timestamp.ToString('HH:mm:ss'), $Level.ToUpperInvariant(), $resolvedCorrelationId, [string]$Message)

    if (Test-ShouldWriteLogMessage -MessageText $line) {
      try { Write-Host $line } catch { }
    }

    try {
      if (-not [string]::IsNullOrWhiteSpace($script:OperationJsonlPath)) {
        Write-OperationJsonlLog -Timestamp $timestamp -MessageText $line -Level $Level -OperationId $resolvedCorrelationId -Module $Module -Action $Action -Root $Root -ErrorClass $ErrorClass
      }
    } catch { }

    return $line
}

