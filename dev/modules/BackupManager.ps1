# ================================================================
#  BACKUP MANAGER – Inkrementelle Backups (MF-25)
#  Dependencies: FileOps.ps1, Settings.ps1
# ================================================================

function New-BackupConfig {
  <#
  .SYNOPSIS
    Erstellt eine Backup-Konfiguration.
  .PARAMETER BackupRoot
    Basis-Verzeichnis fuer Backups.
  .PARAMETER RetentionDays
    Aufbewahrungsdauer in Tagen.
  .PARAMETER MaxSizeGB
    Maximale Gesamtgroesse in GB.
  #>
  param(
    [Parameter(Mandatory)][string]$BackupRoot,
    [int]$RetentionDays = 30,
    [int]$MaxSizeGB = 50
  )

  return @{
    Enabled       = $true
    BackupRoot    = $BackupRoot
    RetentionDays = $RetentionDays
    MaxSizeGB     = $MaxSizeGB
  }
}

function New-BackupSession {
  <#
  .SYNOPSIS
    Erstellt eine neue Backup-Session (ein Verzeichnis pro Run).
  .PARAMETER Config
    Backup-Konfiguration.
  .PARAMETER Label
    Optionales Label fuer die Session.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [string]$Label = ''
  )

  $timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
  $sessionName = if ($Label) { "$timestamp-$Label" } else { $timestamp }
  $sessionDir = Join-Path $Config.BackupRoot $sessionName

  return @{
    SessionId   = $sessionName
    SessionDir  = $sessionDir
    Created     = (Get-Date).ToString('o')
    Files       = @()
    TotalSize   = 0
    Status      = 'Open'
  }
}

function Add-FileToBackup {
  <#
  .SYNOPSIS
    Merkt eine Datei fuer Backup vor (DryRun-faehig).
  .PARAMETER Session
    Backup-Session.
  .PARAMETER SourcePath
    Original-Dateipfad.
  .PARAMETER Mode
    DryRun oder Move.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Session,
    [Parameter(Mandatory)][string]$SourcePath,
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun'
  )

  if (-not (Test-Path -LiteralPath $SourcePath)) {
    return @{ Status = 'Error'; Reason = 'SourceNotFound'; Path = $SourcePath }
  }

  $fileInfo = Get-Item -LiteralPath $SourcePath
  $relativePath = $fileInfo.Name
  $targetPath = Join-Path $Session.SessionDir $relativePath

  $entry = @{
    SourcePath = $SourcePath
    TargetPath = $targetPath
    Size       = $fileInfo.Length
    Status     = 'Pending'
  }

  if ($Mode -eq 'Move') {
    $dir = [System.IO.Path]::GetDirectoryName($targetPath)
    if (-not (Test-Path -LiteralPath $dir)) {
      New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    Copy-Item -LiteralPath $SourcePath -Destination $targetPath -Force
    $entry.Status = 'Backed'
  } else {
    $entry.Status = 'DryRun'
  }

  $Session.Files += $entry
  $Session.TotalSize += $fileInfo.Length

  return @{ Status = $entry.Status; Entry = $entry }
}

function Invoke-BackupRetention {
  <#
  .SYNOPSIS
    Entfernt alte Backups basierend auf Retention-Policy.
  .PARAMETER Config
    Backup-Konfiguration.
  .PARAMETER Mode
    DryRun oder Move.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun'
  )

  if (-not (Test-Path -LiteralPath $Config.BackupRoot)) {
    return @{ Removed = @(); Kept = @(); Status = 'NoBackupDir' }
  }

  $cutoff = (Get-Date).AddDays(-$Config.RetentionDays)
  $dirs = Get-ChildItem -Path $Config.BackupRoot -Directory | Sort-Object CreationTime

  $removed = @()
  $kept = @()

  foreach ($dir in $dirs) {
    if ($dir.CreationTime -lt $cutoff) {
      if ($Mode -eq 'Move') {
        Remove-Item -Path $dir.FullName -Recurse -Force
      }
      $removed += @{ Path = $dir.FullName; Age = [math]::Round(((Get-Date) - $dir.CreationTime).TotalDays, 1) }
    } else {
      $kept += $dir.FullName
    }
  }

  return @{
    Removed = $removed
    Kept    = $kept
    Status  = if ($Mode -eq 'DryRun') { 'DryRun' } else { 'Applied' }
  }
}

function Get-BackupSizeTotal {
  <#
  .SYNOPSIS
    Berechnet die Gesamtgroesse aller Backups.
  .PARAMETER BackupRoot
    Backup-Basisverzeichnis.
  #>
  param(
    [Parameter(Mandatory)][string]$BackupRoot
  )

  if (-not (Test-Path -LiteralPath $BackupRoot)) {
    return @{ TotalBytes = 0; TotalGB = 0; SessionCount = 0 }
  }

  $totalBytes = 0
  $files = Get-ChildItem -Path $BackupRoot -Recurse -File
  foreach ($f in $files) { $totalBytes += $f.Length }

  $sessionCount = (Get-ChildItem -Path $BackupRoot -Directory).Count

  return @{
    TotalBytes   = $totalBytes
    TotalGB      = [math]::Round($totalBytes / 1GB, 2)
    SessionCount = $sessionCount
  }
}

function Test-BackupSpaceAvailable {
  <#
  .SYNOPSIS
    Prueft ob genug Platz fuer ein Backup vorhanden ist.
  .PARAMETER Config
    Backup-Konfiguration.
  .PARAMETER NeededBytes
    Benoetigter Speicher in Bytes.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [long]$NeededBytes = 0
  )

  $current = Get-BackupSizeTotal -BackupRoot $Config.BackupRoot
  $maxBytes = [long]$Config.MaxSizeGB * 1GB
  $available = $maxBytes - $current.TotalBytes

  return @{
    Available    = ($available -ge $NeededBytes)
    CurrentGB    = $current.TotalGB
    MaxGB        = $Config.MaxSizeGB
    RemainingGB  = [math]::Round(($available / 1GB), 2)
    NeededGB     = [math]::Round(($NeededBytes / 1GB), 2)
  }
}
