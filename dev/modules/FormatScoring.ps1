# ================================================================
#  FORMAT SCORING  (extracted from simple_sort.ps1 - ARCH-02)
# ================================================================
# Contains: Get-FormatScore, Get-RegionScore, Get-SizeTieBreakScore
# Dependency: none (self-contained)
# ================================================================

# Disc-image extensions for size tie-break logic
# REF-EXT-01: Single source of truth for disc extensions.
# Other modules access via Get-DiscExtensionSet.
$script:DISC_EXT_SET = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
foreach ($ext in @('.iso','.bin','.img','.cue','.gdi','.ccd','.chd','.rvz','.gcz','.wbfs','.wia','.wbf1','.cso','.pbp','.nrg','.mdf','.mds','.cdi')) {
  [void]$script:DISC_EXT_SET.Add($ext)
}

function Get-DiscExtensionSet {
  <# Returns the canonical HashSet of disc-image extensions. #>
  return $script:DISC_EXT_SET
}

function Get-FormatScore {
  <#
    Bewertet das Dateiformat / Set-Typ.
    Hoeher = besser für Emulator-Kompatibilitaet.
    CHD     = verlustfrei komprimiert, hash-verifizierbar, von MAME/RetroArch bevorzugt
    ISO     = unkomprimiert, breit unterstuetzt
    CUE/GDI/CCD = Disc-Image-Sets, vollstaendig
    M3U     = Multi-Disc Playlist (Bonus wenn mehrere Discs enthalten)
    ZIP/7Z  = komprimiertes ROM, meist Cartridge-Spiele
    RAR     = veraltet, weniger kompatibel
  #>
  param(
    [string]$Extension,
    [string]$Type
  )

  $ext = $Extension.ToLowerInvariant()

  # Set-Typen bekommen ihren Score über den Set-Typ
  switch ($Type) {
    'M3USET' { return 900 }  # Multi-Disc = vollstaendigstes Format
    'GDISET' { return 800 }  # Dreamcast GDI
    'CUESET' { return 800 }  # CUE/BIN
    'CCDSET' { return 750 }  # CloneCD
  }

  # Einzeldateien nach Extension
  switch ($ext) {
    '.chd'  { return 850 }  # MAME CHD, verlustfrei, verifizierbar
    '.iso'  { return 700 }  # Standard Disc Image
    '.cso'  { return 680 }  # Compressed ISO (PSP)
    '.pbp'  { return 680 }  # PSP EBOOT
    '.gcz'  { return 680 }  # GameCube compressed
    '.rvz'  { return 680 }  # Dolphin format
    '.wia'  { return 670 }  # Wii/GC compressed (WIA)
    '.wbf1' { return 660 }  # Wii/GC compressed (WBF1)
    '.wbfs' { return 650 }  # Wii Backup
    '.nrg'  { return 620 }  # Nero Image
    '.mdf'  { return 610 }  # Alcohol 120% Image
    '.cdi'  { return 610 }  # DiscJuggler Image
    '.nsp'  { return 650 }  # Nintendo Switch
    '.xci'  { return 650 }  # Nintendo Switch cartridge
    '.3ds'  { return 650 }  # Nintendo 3DS
    '.cia'  { return 640 }  # 3DS installable
    '.nds'  { return 600 }  # Nintendo DS
    '.gba'  { return 600 }  # Game Boy Advance
    '.gbc'  { return 600 }  # Game Boy Color
    '.gb'   { return 600 }  # Game Boy
    '.nes'  { return 600 }  # NES
    '.sfc'  { return 600 }  # SNES
    '.smc'  { return 600 }  # SNES
    '.n64'  { return 600 }  # N64
    '.z64'  { return 600 }  # N64
    '.v64'  { return 600 }  # N64
    '.md'   { return 600 }  # Mega Drive
    '.gen'  { return 600 }  # Genesis
    '.sms'  { return 600 }  # Master System
    '.gg'   { return 600 }  # Game Gear
    '.pce'  { return 600 }  # PC Engine
    '.fds'  { return 600 }  # Famicom Disk System
    '.32x'  { return 600 }  # Sega 32X
    '.a26'  { return 600 }  # Atari 2600
    '.a52'  { return 600 }  # Atari 5200
    '.a78'  { return 600 }  # Atari 7800
    '.ecm'  { return 550 }  # ECM-komprimiertes Disc Image
    '.mds'  { return 610 }  # Alcohol 120% Descriptor
    '.dax'  { return 650 }  # PSP DAX komprimiert
    '.jso'  { return 650 }  # PSP JSO komprimiert
    '.zso'  { return 650 }  # PSP ZSO komprimiert
    '.nsz'  { return 640 }  # Switch NSP komprimiert
    '.xcz'  { return 640 }  # Switch XCI komprimiert
    '.zip'  { return 500 }  # Komprimiert, breit unterstuetzt
    '.7z'   { return 480 }  # Besser komprimiert, aber langsamer
    '.rar'  { return 400 }  # Veraltet
    default { return 300 }  # Unbekannt
  }
}

# ================================================================
#  REGION SCORING
# ================================================================

function Get-RegionScore {
  param([string]$Region, [string[]]$Prefer)

  $idx = [Array]::IndexOf($Prefer, $Region)
  if ($idx -ge 0) { return 1000 - $idx }

  switch ($Region) {
    "WORLD"   { return 500 }
    "UNKNOWN" { return 100 }
    default   { return 200 }
  }
}

function Get-SizeTieBreakScore {
  <# Prefer larger sizes for disc images, smaller for cartridge formats. #>
  param(
    [string]$Type,
    [string]$Extension,
    [long]$SizeBytes
  )

  $ext = if ($Extension) { $Extension.ToLowerInvariant() } else { '' }
  if ($Type -in @('M3USET','GDISET','CUESET','CCDSET')) { return $SizeBytes }
  if ($Type -eq 'DOSDIR') { return $SizeBytes }
  if ($script:DISC_EXT_SET.Contains($ext)) { return $SizeBytes }
  return (-1 * $SizeBytes)
}

function Get-HeaderVariantScore {
  <# Prefer headered dumps over headerless when both variants exist. #>
  param(
    [string]$Root,
    [string]$MainPath
  )

  $hint = ('{0} {1}' -f $Root, $MainPath).ToLowerInvariant()
  if ($hint -match '\bheadered\b') { return 10 }
  if ($hint -match '\bheaderless\b') { return -10 }
  return 0
}
