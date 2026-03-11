#  ARCADE ROM MERGE/SPLIT (LF-07)
#  Non-Merged / Split / Merged-Set-Konvertierung fuer MAME/FBNEO.

function Get-ArcadeSetTypes {
  <#
  .SYNOPSIS
    Gibt die unterstuetzten Arcade-Set-Typen zurueck.
  #>
  return @(
    @{ Key = 'non-merged'; Name = 'Non-Merged'; Description = 'Jedes ROM-Set enthaelt alle benoetigten Dateien' }
    @{ Key = 'split';      Name = 'Split';      Description = 'Clones enthalten nur eigene Dateien, Parent separat' }
    @{ Key = 'merged';     Name = 'Merged';     Description = 'Parent + Clones in einer ZIP-Datei' }
  )
}

function Read-ArcadeDatParentClone {
  <#
  .SYNOPSIS
    Parsed Parent/Clone-Beziehungen aus einem DAT-Index.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$DatIndex
  )

  $parentMap = @{}
  $cloneMap  = @{}

  foreach ($key in $DatIndex.Keys) {
    $entry = $DatIndex[$key]
    $parent = if ($entry.ContainsKey('CloneOf')) { $entry.CloneOf } else { '' }

    if ($parent -and $parent -ne $key) {
      # Clone
      if (-not $cloneMap.ContainsKey($parent)) { $cloneMap[$parent] = [System.Collections.Generic.List[string]]::new() }
      $cloneMap[$parent].Add($key)
    } else {
      # Parent
      if (-not $parentMap.ContainsKey($key)) { $parentMap[$key] = $entry }
    }
  }

  return @{
    Parents  = $parentMap
    Clones   = $cloneMap
    ParentCount = $parentMap.Count
    CloneCount  = ($cloneMap.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
  }
}

function Get-ArcadeSetInfo {
  <#
  .SYNOPSIS
    Analysiert ein ZIP-Archiv und gibt die ROM-Dateien darin zurueck.
  #>
  param(
    [Parameter(Mandatory)][string]$ZipPath
  )

  if (-not (Test-Path -LiteralPath $ZipPath)) {
    return @{ Valid = $false; Error = 'Datei nicht gefunden'; Files = @() }
  }

  $name = [System.IO.Path]::GetFileNameWithoutExtension($ZipPath)

  # ZIP-Inhalt lesen via System.IO.Compression
  $files = [System.Collections.Generic.List[hashtable]]::new()
  try {
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    foreach ($entry in $zip.Entries) {
      if ($entry.Length -gt 0) {
        $files.Add(@{
          Name           = $entry.Name
          FullName       = $entry.FullName
          CompressedSize = $entry.CompressedLength
          Size           = $entry.Length
          Crc32          = $entry.Crc32.ToString('X8')
        })
      }
    }
    $zip.Dispose()
  } catch {
    return @{ Valid = $false; Error = $_.Exception.Message; Files = @() }
  }

  return @{
    Valid     = $true
    SetName   = $name
    Path      = $ZipPath
    SizeBytes = (Get-Item -LiteralPath $ZipPath).Length
    Files     = ,$files.ToArray()
  }
}

function New-MergeOperation {
  <#
  .SYNOPSIS
    Erstellt eine Merge-Operation fuer Arcade-Sets.
  #>
  param(
    [Parameter(Mandatory)][string]$SourceType,
    [Parameter(Mandatory)][string]$TargetType,
    [Parameter(Mandatory)][array]$Sets,
    [hashtable]$ParentCloneMap = @{}
  )

  return @{
    SourceType     = $SourceType
    TargetType     = $TargetType
    Sets           = $Sets
    ParentCloneMap = $ParentCloneMap
    Status         = 'Pending'
    Processed      = 0
    Errors         = [System.Collections.Generic.List[string]]::new()
  }
}

function Get-MergePlan {
  <#
  .SYNOPSIS
    Erstellt einen Merge/Split-Plan basierend auf DAT-Daten.
  #>
  param(
    [Parameter(Mandatory)][string]$SourceType,
    [Parameter(Mandatory)][string]$TargetType,
    [Parameter(Mandatory)][array]$Sets,
    [Parameter(Mandatory)][hashtable]$DatIndex
  )

  $pcMap = Read-ArcadeDatParentClone -DatIndex $DatIndex

  $actions = [System.Collections.Generic.List[hashtable]]::new()

  foreach ($set in $Sets) {
    $setName = $set.SetName
    $isParent = $pcMap.Parents.ContainsKey($setName)
    $isClone = $false
    $parentName = ''

    foreach ($pKey in $pcMap.Clones.Keys) {
      if ($pcMap.Clones[$pKey] -contains $setName) {
        $isClone = $true
        $parentName = $pKey
        break
      }
    }

    $actions.Add(@{
      SetName    = $setName
      IsParent   = $isParent
      IsClone    = $isClone
      ParentName = $parentName
      Action     = "$SourceType->$TargetType"
    })
  }

  return @{
    Actions     = ,$actions.ToArray()
    SourceType  = $SourceType
    TargetType  = $TargetType
    TotalSets   = $Sets.Count
    ParentSets  = @($actions | Where-Object { $_.IsParent }).Count
    CloneSets   = @($actions | Where-Object { $_.IsClone }).Count
  }
}

# ── ZIP-basierte Merge/Split-Operationen ────────────────────────────────

function Invoke-ArcadeMerge {
  <#
  .SYNOPSIS
    Merged Clone-ZIPs in das Parent-ZIP (Non-Merged/Split → Merged).
  .PARAMETER ParentZip
    Pfad zur Parent-ZIP.
  .PARAMETER CloneZips
    Array von Pfaden zu Clone-ZIPs.
  .PARAMETER OutputDir
    Ausgabe-Verzeichnis fuer das gemergte ZIP.
  .PARAMETER DryRun
    Nur Plan erstellen, keine Aenderung.
  #>
  param(
    [Parameter(Mandatory)][string]$ParentZip,
    [Parameter(Mandatory)][string[]]$CloneZips,
    [string]$OutputDir = '',
    [switch]$DryRun
  )

  if (-not (Test-Path -LiteralPath $ParentZip)) {
    return @{ Success = $false; Error = "Parent-ZIP nicht gefunden: $ParentZip"; FilesAdded = 0 }
  }

  Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
  Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

  $parentName = [System.IO.Path]::GetFileNameWithoutExtension($ParentZip)
  $targetPath = if ($OutputDir) { Join-Path $OutputDir "$parentName.zip" } else { $ParentZip }
  $filesAdded = 0
  $errors = [System.Collections.Generic.List[string]]::new()

  if ($DryRun) {
    # Nur zaehlen was hinzugefuegt wuerde
    foreach ($cloneZip in $CloneZips) {
      if (Test-Path -LiteralPath $cloneZip) {
        $info = Get-ArcadeSetInfo -ZipPath $cloneZip
        $filesAdded += $info.Files.Count
      }
    }
    return @{ Success = $true; DryRun = $true; FilesAdded = $filesAdded; TargetPath = $targetPath; Errors = @() }
  }

  # Kopiere Parent zum Ziel falls noetig
  if ($OutputDir -and $targetPath -ne $ParentZip) {
    if (-not (Test-Path -LiteralPath $OutputDir)) { [void](New-Item -Path $OutputDir -ItemType Directory -Force) }
    Copy-Item -LiteralPath $ParentZip -Destination $targetPath -Force
  }

  try {
    $zip = [System.IO.Compression.ZipFile]::Open($targetPath, [System.IO.Compression.ZipArchiveMode]::Update)
    $existingEntries = @($zip.Entries | ForEach-Object { $_.FullName })

    foreach ($cloneZip in $CloneZips) {
      if (-not (Test-Path -LiteralPath $cloneZip)) {
        $errors.Add("Clone-ZIP nicht gefunden: $cloneZip")
        continue
      }
      try {
        $cloneSrc = [System.IO.Compression.ZipFile]::OpenRead($cloneZip)
        $cloneName = [System.IO.Path]::GetFileNameWithoutExtension($cloneZip)

        foreach ($entry in $cloneSrc.Entries) {
          if ($entry.Length -eq 0) { continue }
          $targetEntry = "$cloneName/$($entry.FullName)"
          if ($existingEntries -contains $targetEntry) { continue }

          $newEntry = $zip.CreateEntry($targetEntry, [System.IO.Compression.CompressionLevel]::Optimal)
          $srcStream = $entry.Open()
          $dstStream = $newEntry.Open()
          $srcStream.CopyTo($dstStream)
          $dstStream.Close()
          $srcStream.Close()
          $filesAdded++
        }
        $cloneSrc.Dispose()
      } catch {
        $errors.Add("Fehler bei Clone $cloneZip : $($_.Exception.Message)")
      }
    }

    $zip.Dispose()
  } catch {
    return @{ Success = $false; Error = $_.Exception.Message; FilesAdded = $filesAdded; Errors = ,$errors.ToArray() }
  }

  return @{ Success = $true; DryRun = $false; FilesAdded = $filesAdded; TargetPath = $targetPath; Errors = ,$errors.ToArray() }
}

function Invoke-ArcadeSplit {
  <#
  .SYNOPSIS
    Splittet ein Merged-ZIP in separate Parent- und Clone-ZIPs.
  .PARAMETER MergedZip
    Pfad zum gemergten ZIP.
  .PARAMETER ParentCloneMap
    Ergebnis von Read-ArcadeDatParentClone.
  .PARAMETER OutputDir
    Ausgabe-Verzeichnis.
  .PARAMETER DryRun
    Nur Plan erstellen.
  #>
  param(
    [Parameter(Mandatory)][string]$MergedZip,
    [Parameter(Mandatory)][hashtable]$ParentCloneMap,
    [Parameter(Mandatory)][string]$OutputDir,
    [switch]$DryRun
  )

  if (-not (Test-Path -LiteralPath $MergedZip)) {
    return @{ Success = $false; Error = "Merged-ZIP nicht gefunden: $MergedZip"; SetsCreated = 0 }
  }

  Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
  Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

  $parentName = [System.IO.Path]::GetFileNameWithoutExtension($MergedZip)

  # Analysiere Inhalt: Dateien in Unterordnern = Clone-Sets
  $setFiles = @{}
  try {
    $srcZip = [System.IO.Compression.ZipFile]::OpenRead($MergedZip)
    foreach ($entry in $srcZip.Entries) {
      if ($entry.Length -eq 0) { continue }
      $parts = $entry.FullName -split '/', 2
      $setKey = if ($parts.Count -gt 1) { $parts[0] } else { $parentName }
      $fileName = if ($parts.Count -gt 1) { $parts[1] } else { $parts[0] }
      if (-not $setFiles.ContainsKey($setKey)) { $setFiles[$setKey] = [System.Collections.Generic.List[string]]::new() }
      $setFiles[$setKey].Add($fileName)
    }
    $srcZip.Dispose()
  } catch {
    return @{ Success = $false; Error = $_.Exception.Message; SetsCreated = 0 }
  }

  if ($DryRun) {
    return @{ Success = $true; DryRun = $true; SetsCreated = $setFiles.Count; Sets = @($setFiles.Keys) }
  }

  if (-not (Test-Path -LiteralPath $OutputDir)) { [void](New-Item -Path $OutputDir -ItemType Directory -Force) }
  $setsCreated = 0

  $srcZip = [System.IO.Compression.ZipFile]::OpenRead($MergedZip)
  foreach ($setKey in $setFiles.Keys) {
    $outPath = Join-Path $OutputDir "$setKey.zip"
    try {
      $outZip = [System.IO.Compression.ZipFile]::Open($outPath, [System.IO.Compression.ZipArchiveMode]::Create)
      foreach ($entry in $srcZip.Entries) {
        if ($entry.Length -eq 0) { continue }
        $parts = $entry.FullName -split '/', 2
        $entrySet = if ($parts.Count -gt 1) { $parts[0] } else { $parentName }
        if ($entrySet -ne $setKey) { continue }
        $entryName = if ($parts.Count -gt 1) { $parts[1] } else { $parts[0] }

        $newEntry = $outZip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $src = $entry.Open()
        $dst = $newEntry.Open()
        $src.CopyTo($dst)
        $dst.Close()
        $src.Close()
      }
      $outZip.Dispose()
      $setsCreated++
    } catch {
      # Cleanup bei Fehler
      if (Test-Path -LiteralPath $outPath) { Remove-Item -LiteralPath $outPath -Force -ErrorAction SilentlyContinue }
    }
  }
  $srcZip.Dispose()

  return @{ Success = $true; DryRun = $false; SetsCreated = $setsCreated; OutputDir = $OutputDir }
}
