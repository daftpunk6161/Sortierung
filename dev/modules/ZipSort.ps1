function Get-ZipEntryExtensions {
  <# Return distinct extensions of files inside a ZIP. #>
  param([Parameter(Mandatory=$true)][string]$ZipPath)

  # BUG ZIPSORT-004 FIX: Ensure System.IO.Compression assemblies are loaded (required for PS 5.1)
  Add-Type -AssemblyName System.IO.Compression    -ErrorAction SilentlyContinue
  Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

  $exts = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  $zip = $null
  try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    foreach ($entry in $zip.Entries) {
      if (-not $entry.Name) { continue }
      $ext = [IO.Path]::GetExtension($entry.Name)
      if ($ext) { [void]$exts.Add($ext.ToLowerInvariant()) }
    }
  } finally {
    if ($zip) { $zip.Dispose() }
  }

  return @($exts)
}

function New-ZipFromFolder {
  <# Create zip archive from a source folder (7z preferred, .NET fallback). #>
  param(
    [Parameter(Mandatory=$true)][string]$SourceFolder,
    [Parameter(Mandatory=$true)][string]$ZipPath,
    [string]$SevenZipPath,
    [scriptblock]$Log
  )

  $tempZip = $ZipPath + '.tmp_' + [guid]::NewGuid().ToString('N')
  try {
    $zipCreated = $false
    if ($SevenZipPath -and (Test-Path -LiteralPath $SevenZipPath -PathType Leaf)) {
      $parent = Split-Path -Parent $SourceFolder
      $leaf = Split-Path -Leaf $SourceFolder
      # BUG-010 FIX: Pre-quote path args — Invoke-ExternalToolProcess no longer re-quotes
      $toolArgs = @('a', '-tzip', '-mx=5', (ConvertTo-QuotedArg $tempZip), (ConvertTo-QuotedArg $leaf))
      $tempFiles = [System.Collections.Generic.List[string]]::new()
      $procResult = Invoke-ExternalToolProcess -ToolPath $SevenZipPath -ToolArgs $toolArgs -WorkingDirectory $parent -TempFiles $tempFiles -Log $Log -ErrorLabel '7z'
      if ($procResult.Success -and (Test-Path -LiteralPath $tempZip -PathType Leaf)) {
        $zipCreated = $true
      } else {
        # ZIPSORT-002 FIX: Include 7z error details so users can diagnose issues
        $errDetail = ''
        if ($procResult.ExitCode) { $errDetail += " ExitCode=$($procResult.ExitCode)" }
        if ($procResult.ErrorText) { $errDetail += " Stderr=$($procResult.ErrorText.Trim())" }
        if ($Log) { & $Log ("  WARNUNG: 7z fehlgeschlagen ({0}), versuche .NET ZipFile Fallback." -f $errDetail.Trim()) }
        if ($tempZip -and (Test-Path -LiteralPath $tempZip)) { Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue }
      }
    }

    if (-not $zipCreated) {
      try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue | Out-Null } catch { }
      [System.IO.Compression.ZipFile]::CreateFromDirectory($SourceFolder, $tempZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
      $zipCreated = (Test-Path -LiteralPath $tempZip -PathType Leaf)
    }

    # ZIPSORT-001: Zip integrity check — verify the archive is not empty or corrupt
    if ($zipCreated) {
      try {
        $checkZip = [System.IO.Compression.ZipFile]::OpenRead($tempZip)
        try {
          if ($checkZip.Entries.Count -eq 0) {
            $zipCreated = $false
            if ($Log) { & $Log '  WARNUNG: Erstelltes ZIP ist leer, verwerfe.' }
          }
        } finally {
          $checkZip.Dispose()
        }
      } catch {
        $zipCreated = $false
        if ($Log) { & $Log ("  WARNUNG: ZIP-Integritaetspruefung fehlgeschlagen: {0}" -f $_.Exception.Message) }
      }
    }

    if (-not $zipCreated) {
      if ($tempZip -and (Test-Path -LiteralPath $tempZip)) { Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue }
      return $false
    }

    # ZIPSORT-003 FIX: Atomic move without TOCTOU race — let Move-Item fail if destination exists
    try {
      Move-Item -LiteralPath $tempZip -Destination $ZipPath -ErrorAction Stop
    } catch {
      # Destination appeared between creation and move (race), or already existed
      if ($tempZip -and (Test-Path -LiteralPath $tempZip)) { Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue }
      return $false
    }
    return $true
  } catch {
    if ($tempZip -and (Test-Path -LiteralPath $tempZip)) { Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue }
    if ($Log) { & $Log ("  FEHLER ZIP fehlgeschlagen: {0}" -f $_.Exception.Message) }
    return $false
  }
}

function Invoke-ZipSortPS1PS2 {
  <# Sort ZIPs into PS1/PS2 based on contained file extensions. #>
  param(
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [scriptblock]$Log
  )

  # BUG-05 fix: .bin/.cue/.img sind multi-platform (SAT, DC, SCD, PCECD, etc.)
  # und duerfen NICHT als PS1-exklusiv behandelt werden.
  # Nur PS1-spezifische Formate: .pbp (PS1-Classic), .ccd+.sub (CloneCD, haeufig PS1)
  $ps1Exts = @('.ccd', '.sub', '.pbp')
  # .cso aus PS2 entfernt (ist PSP). .bin/.cue/.img entfernt (ambig).
  # BUG-CS-06: .iso entfernt - ist multi-plattform (DC/SAT/3DO/Xbox etc.)
  # BUG-CS-09: .gz entfernt - generisches Kompressionsformat, falsch-positiv-anfaellig
  $ps2Exts = @('.nrg', '.mdf', '.mds')

  $total = 0
  $moved = 0
  $skipped = 0
  $errors = 0

  foreach ($root in $Roots) {
    Test-CancelRequested
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
      if ($Log) { & $Log "WARNUNG: Root nicht gefunden: $root" }
      continue
    }

    $ps1Dir = Join-Path $root 'PS1'
    $ps2Dir = Join-Path $root 'PS2'
    Initialize-Directory $ps1Dir
    Initialize-Directory $ps2Dir

    $zips = @(Get-ChildItem -LiteralPath $root -Filter '*.zip' -File -ErrorAction SilentlyContinue)
    if ($Log) { & $Log ("ZIP Scan: {0} Dateien in {1}" -f $zips.Count, $root) }

    foreach ($zip in $zips) {
      Test-CancelRequested
      $total++
      try {
        $innerExts = @(Get-ZipEntryExtensions -ZipPath $zip.FullName)
        if ($innerExts.Count -eq 0) {
          $skipped++
          if ($Log) { & $Log ("  ÜBERSPRUNGEN (leer/unbekannt): {0}" -f $zip.Name) }
          continue
        }
        # BUG-CS-08: Bei Ueberlappung (Exts matchen PS1 UND PS2) ueberspringen
        $hasPs1 = ($innerExts | Where-Object { $ps1Exts -contains $_ } | Select-Object -First 1)
        $hasPs2 = ($innerExts | Where-Object { $ps2Exts -contains $_ } | Select-Object -First 1)
        if ($hasPs1 -and $hasPs2) {
          $skipped++
          if ($Log) { & $Log ("  ÜBERSPRUNGEN (PS1+PS2 ambig): {0}" -f $zip.Name) }
        } elseif ($hasPs1) {
          $dest = Join-Path $ps1Dir $zip.Name
          try {
            [void](Invoke-RootSafeMove -Source $zip.FullName -Dest $dest -SourceRoot $root -DestRoot $root)
            $moved++
            if ($Log) { & $Log ("  PS1: {0}" -f $zip.Name) }
          } catch {
            $errors++
            if ($Log) { & $Log ("  FEHLER {0}: {1}" -f $zip.Name, $_.Exception.Message) }
          }
        } elseif ($hasPs2) {
          $dest = Join-Path $ps2Dir $zip.Name
          try {
            [void](Invoke-RootSafeMove -Source $zip.FullName -Dest $dest -SourceRoot $root -DestRoot $root)
            $moved++
            if ($Log) { & $Log ("  PS2: {0}" -f $zip.Name) }
          } catch {
            $errors++
            if ($Log) { & $Log ("  FEHLER {0}: {1}" -f $zip.Name, $_.Exception.Message) }
          }
        } else {
          $skipped++
          if ($Log) { & $Log ("  ÜBERSPRUNGEN (nicht erkannt): {0}" -f $zip.Name) }
        }
      } catch {
        $errors++
        if ($Log) { & $Log ("  FEHLER {0}: {1}" -f $zip.Name, $_.Exception.Message) }
      }
    }
  }

  if ($Log) {
    & $Log ("ZIP Sort: {0} gescannt, {1} verschoben, {2} übersprungen, {3} Fehler" -f $total, $moved, $skipped, $errors)
  }
  return [pscustomobject]@{ Total = $total; Moved = $moved; Skipped = $skipped; Errors = $errors }
}

function Invoke-ZipSort {
  <# Compatibility wrapper for strategy-based ZIP sorting. #>
  param(
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [ValidateSet('PS1PS2')][string]$Strategy = 'PS1PS2',
    [scriptblock]$Log
  )

  return (Invoke-WithPluginOverride -Phase 'zip-sort' -Context @{
    Roots    = @($Roots)
    Strategy = [string]$Strategy
    Log      = $Log
  } -CoreAction {
    switch ($Strategy) {
      'PS1PS2' { return (Invoke-ZipSortPS1PS2 -Roots $Roots -Log $Log) }
    }
  } -Log $Log)
}
