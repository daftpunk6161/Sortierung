function Resolve-SetReferencePath {
  <# Resolves a referenced child path within a set file context; optionally root-restricted. #>
  param(
    [string]$BaseDir,
    [string]$Reference,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$RootPath
  )

  if ([string]::IsNullOrWhiteSpace($Reference)) { return $null }
  return Resolve-ChildPathWithinRoot -BaseDir $BaseDir -ChildPath $Reference -Root $RootPath
}

function Resolve-SetReferencePathCompat {
  <# Backward-compatible resolver with safe fallback rooting.
     When RootPath is empty, BaseDir is used as implicit root to prevent traversal. #>
  param(
    [string]$BaseDir,
    [string]$Reference,
    [string]$RootPath
  )

  if ([string]::IsNullOrWhiteSpace($Reference)) { return $null }

  if ([string]::IsNullOrWhiteSpace($RootPath)) {
    Write-Verbose ("SetParsing: RootPath leer, verwende Compat-Pfad fuer Referenz '{0}'" -f [string]$Reference)
    if ([string]::IsNullOrWhiteSpace($BaseDir) -or -not (Test-Path -LiteralPath $BaseDir -PathType Container)) {
      return $null
    }
    return Resolve-ChildPathWithinRoot -BaseDir $BaseDir -ChildPath $Reference -Root $BaseDir
  }

  return Resolve-SetReferencePath -BaseDir $BaseDir -Reference $Reference -RootPath $RootPath
}

function Get-SetReferencedFiles {
  <# Shared parser for CUE/GDI referenced files (related or missing mode). #>
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$SetPath,
    [string]$RootPath,
    [Parameter(Mandatory=$true)][ValidateSet('Cue','Gdi')][string]$Format,
    [switch]$MissingOnly
  )

  $dir = Split-Path -Parent $SetPath
  $result = New-Object System.Collections.Generic.List[string]
  $seen = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)

  if (-not $MissingOnly) {
    if ($seen.Add($SetPath)) { [void]$result.Add($SetPath) }
  }

  try {
    $lines = Get-Content -LiteralPath $SetPath -Encoding UTF8 -ErrorAction Stop
    foreach ($line in $lines) {
      $reference = $null
      switch ($Format) {
        'Cue' {
          if ($line -match '^\s*FILE\s+"(.+?)"\s+' -or $line -match '^\s*FILE\s+(\S+)\s+') {
            $reference = [string]$Matches[1]
          }
        }
        'Gdi' {
          if ($line -match '^\s*\d+\s+\d+\s+\d+\s+\d+\s+(?:"([^"]+)"|(.+?))\s+\d+\s*$') {
            $reference = if (-not [string]::IsNullOrWhiteSpace([string]$Matches[1])) {
              [string]$Matches[1]
            } else {
              [string]$Matches[2]
            }
            if ($reference) { $reference = $reference.Trim() }
          }
        }
      }

      if ([string]::IsNullOrWhiteSpace($reference)) { continue }
      $resolved = Resolve-SetReferencePathCompat -BaseDir $dir -Reference $reference -RootPath $RootPath
      $exists = ($resolved -and (Test-Path -LiteralPath $resolved))

      if ($MissingOnly) {
        if (-not $exists) {
          [void]$result.Add($(if ($resolved) { $resolved } else { $reference }))
        }
      } else {
        if ($exists -and $seen.Add($resolved)) {
          [void]$result.Add($resolved)
        }
      }
    }
  } catch {
    Write-Warning ('[SetParsing] Fehler: {0}' -f $_.Exception.Message)
  }

  return $result
}

function Get-CueRelatedFiles {
  <# Parst .cue und sammelt alle referenzierten Dateien (.bin, .img, .wav, etc.) #>
  param(
    [string]$CuePath,
    [string]$RootPath
  )

  return (Get-SetReferencedFiles -SetPath $CuePath -RootPath $RootPath -Format 'Cue')
}

function Get-CueMissingFiles {
  <# Returns missing files referenced by a .cue #>
  param(
    [string]$CuePath,
    [string]$RootPath
  )

  return (Get-SetReferencedFiles -SetPath $CuePath -RootPath $RootPath -Format 'Cue' -MissingOnly)
}

function Get-GdiRelatedFiles {
  <# Parst .gdi (Dreamcast) und sammelt alle Track-Dateien (.bin, .raw) #>
  param(
    [string]$GdiPath,
    [string]$RootPath
  )

  return (Get-SetReferencedFiles -SetPath $GdiPath -RootPath $RootPath -Format 'Gdi')
}

function Get-GdiMissingFiles {
  <# Returns missing files referenced by a .gdi #>
  param(
    [string]$GdiPath,
    [string]$RootPath
  )

  return (Get-SetReferencedFiles -SetPath $GdiPath -RootPath $RootPath -Format 'Gdi' -MissingOnly)
}

function Get-CcdRelatedFiles {
  <# Sammelt CloneCD-Set: .ccd + .img + .sub (gleicher Basisname) #>
  param(
    [string]$CcdPath,
    [string]$RootPath
  )

  $dir  = Split-Path -Parent $CcdPath
  $base = [IO.Path]::GetFileNameWithoutExtension($CcdPath)
  $related = New-Object System.Collections.Generic.List[string]
  $seen = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  if ($seen.Add($CcdPath)) { [void]$related.Add($CcdPath) }

  foreach ($ext in @('.img', '.sub')) {
    $candidate = $base + $ext
    $p = Resolve-SetReferencePathCompat -BaseDir $dir -Reference $candidate -RootPath $RootPath
    if ($p -and (Test-Path -LiteralPath $p)) {
      if ($seen.Add($p)) { [void]$related.Add($p) }
    }
  }

  return $related
}

function Get-CcdMissingFiles {
  <# Returns missing files referenced by a .ccd set (.img/.sub). #>
  param(
    [string]$CcdPath,
    [string]$RootPath
  )

  $dir = Split-Path -Parent $CcdPath
  $base = [IO.Path]::GetFileNameWithoutExtension($CcdPath)
  $missing = New-Object System.Collections.Generic.List[string]

  foreach ($ext in @('.img', '.sub')) {
    $candidate = $base + $ext
    $p = Resolve-SetReferencePathCompat -BaseDir $dir -Reference $candidate -RootPath $RootPath
    if (-not $p -or -not (Test-Path -LiteralPath $p)) {
      [void]$missing.Add($(if ($p) { $p } else { Join-Path $dir $candidate }))
    }
  }

  return $missing
}

# MISS-CS-02: MDF/MDS Set-Parsing (Alcohol 120% Disc-Images)
function Get-MdsRelatedFiles {
  <# Sammelt Alcohol 120% Set: .mds + .mdf (gleicher Basisname) #>
  param(
    [string]$MdsPath,
    [string]$RootPath
  )

  $dir  = Split-Path -Parent $MdsPath
  $base = [IO.Path]::GetFileNameWithoutExtension($MdsPath)
  $related = New-Object System.Collections.Generic.List[string]
  $seen = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  if ($seen.Add($MdsPath)) { [void]$related.Add($MdsPath) }

  $candidate = $base + '.mdf'
  $p = Resolve-SetReferencePathCompat -BaseDir $dir -Reference $candidate -RootPath $RootPath
  if ($p -and (Test-Path -LiteralPath $p)) {
    if ($seen.Add($p)) { [void]$related.Add($p) }
  }

  return $related
}

function Get-MdsMissingFiles {
  <# Returns missing files referenced by a .mds set (.mdf). #>
  param(
    [string]$MdsPath,
    [string]$RootPath
  )

  $dir = Split-Path -Parent $MdsPath
  $base = [IO.Path]::GetFileNameWithoutExtension($MdsPath)
  $missing = New-Object System.Collections.Generic.List[string]

  $candidate = $base + '.mdf'
  $p = Resolve-SetReferencePathCompat -BaseDir $dir -Reference $candidate -RootPath $RootPath
  if (-not $p -or -not (Test-Path -LiteralPath $p)) {
    [void]$missing.Add($(if ($p) { $p } else { Join-Path $dir $candidate }))
  }

  return $missing
}

function Get-M3URelatedFiles {
  <#
    M3U = Multi-Disc Playlist.
    Kann auf .cue, .gdi, .ccd, .chd etc. verweisen.
    Referenzierte Sets werden rekursiv aufgeloest.
  #>
  param(
    [string]$M3UPath,
    [string]$RootPath,
    [System.Collections.Generic.HashSet[string]]$VisitedM3u,
    [int]$MaxDepth = 20
  )

  # BUG-022 FIX: Depth limit prevents stack overflow on deep M3U chains
  if ($MaxDepth -le 0) {
    Write-Warning ('[SetParsing] M3U Rekursionstiefe ueberschritten fuer: {0}' -f $M3UPath)
    return @()
  }

  if (-not $VisitedM3u) {
    $VisitedM3u = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  }
  if (-not $VisitedM3u.Add($M3UPath)) {
    return @()
  }

  $dir = Split-Path -Parent $M3UPath
  $related = New-Object System.Collections.Generic.List[string]
  $seen = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  if ($seen.Add($M3UPath)) { [void]$related.Add($M3UPath) }

  try {
    $lines = Get-Content -LiteralPath $M3UPath -Encoding UTF8 -ErrorAction Stop
    foreach ($l in $lines) {
      $l = $l.Trim()
      if ($l -eq '' -or $l.StartsWith('#')) { continue }
      if ($RootPath) {
        if ([IO.Path]::IsPathRooted($l) -or ($l -match '(^|[\\/])\.\.([\\/]|$)')) { continue }
      }
      $p = Resolve-SetReferencePathCompat -BaseDir $dir -Reference $l -RootPath $RootPath
      if (-not $p -or -not (Test-Path -LiteralPath $p)) { continue }

      $ext = [IO.Path]::GetExtension($p).ToLowerInvariant()

      # Referenzierte Disc-Images rekursiv aufloesen
      $subFiles = switch ($ext) {
        '.m3u' { Get-M3URelatedFiles -M3UPath $p -RootPath $RootPath -VisitedM3u $VisitedM3u -MaxDepth ($MaxDepth - 1) }
        '.cue' { Get-CueRelatedFiles -CuePath $p -RootPath $RootPath }
        '.gdi' { Get-GdiRelatedFiles -GdiPath $p -RootPath $RootPath }
        '.ccd' { Get-CcdRelatedFiles -CcdPath $p -RootPath $RootPath }
        '.mds' { Get-MdsRelatedFiles -MdsPath $p -RootPath $RootPath }
        default { @($p) }
      }
      foreach ($sf in $subFiles) {
        if ($seen.Add($sf)) { [void]$related.Add($sf) }
      }
    }
  } catch {
    Write-Warning ('[SetParsing] Fehler: {0}' -f $_.Exception.Message)
  }

  return $related
}

function Get-M3UMissingFiles {
  <# Returns missing files referenced by an .m3u (incl. missing tracks for cue/gdi/ccd). #>
  param(
    [string]$M3UPath,
    [string]$RootPath,
    [System.Collections.Generic.HashSet[string]]$VisitedM3u,
    [int]$MaxDepth = 20
  )

  # BUG-022 FIX: Depth limit prevents stack overflow on deep M3U chains
  if ($MaxDepth -le 0) {
    Write-Warning ('[SetParsing] M3U Rekursionstiefe ueberschritten fuer: {0}' -f $M3UPath)
    return @()
  }

  if (-not $VisitedM3u) {
    $VisitedM3u = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
  }
  if (-not $VisitedM3u.Add($M3UPath)) {
    return @()
  }

  $dir = Split-Path -Parent $M3UPath
  $missing = New-Object System.Collections.Generic.List[string]

  try {
    $lines = Get-Content -LiteralPath $M3UPath -Encoding UTF8 -ErrorAction Stop
    foreach ($l in $lines) {
      $l = $l.Trim()
      if ($l -eq '' -or $l.StartsWith('#')) { continue }
      if ($RootPath) {
        if ([IO.Path]::IsPathRooted($l) -or ($l -match '(^|[\\/])\.\.([\\/]|$)')) {
          [void]$missing.Add($l)
          continue
        }
      }
      $p = Resolve-SetReferencePathCompat -BaseDir $dir -Reference $l -RootPath $RootPath
      if (-not $p -or -not (Test-Path -LiteralPath $p)) {
        [void]$missing.Add($(if ($p) { $p } else { $l }))
        continue
      }

      $ext = [IO.Path]::GetExtension($p).ToLowerInvariant()
      $subMissing = switch ($ext) {
        '.m3u' { Get-M3UMissingFiles -M3UPath $p -RootPath $RootPath -VisitedM3u $VisitedM3u -MaxDepth ($MaxDepth - 1) }
        '.cue' { Get-CueMissingFiles -CuePath $p -RootPath $RootPath }
        '.gdi' { Get-GdiMissingFiles -GdiPath $p -RootPath $RootPath }
        '.ccd' { Get-CcdMissingFiles -CcdPath $p -RootPath $RootPath }
        '.mds' { Get-MdsMissingFiles -MdsPath $p -RootPath $RootPath }
        default { @() }
      }
      foreach ($m in $subMissing) { [void]$missing.Add($m) }
    }
  } catch { Write-Warning ('[SetParsing] Fehler: {0}' -f $_.Exception.Message) }

  return $missing
}
