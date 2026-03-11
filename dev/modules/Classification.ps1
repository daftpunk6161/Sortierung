# ================================================================
#  CLASSIFICATION – File category & console detection
#  Extracted from simple_sort.ps1 (ARCH-01)
# ================================================================

# [F-04] CHD disc-header scan cache (path → console key or $null).
# Prevents re-reading 64 KB per .chd on repeated classification calls.
if (-not (Get-Variable -Name CHD_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue)) {
  $script:CHD_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

# ISO/IMG/BIN disc-header scan cache (path → console key or $null).
# Prevents re-reading 128 KB per binary disc image on repeated classification calls.
if (-not (Get-Variable -Name ISO_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue)) {
  $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

# BUG-17: Last disc-header IO error (path + message) for reason-code propagation
$script:LAST_DISC_HEADER_ERROR = $null

function Get-FileCategory {
  <#
    Klassifiziert eine ROM-Datei anhand ihres Namens.
    Returns: GAME, BIOS, JUNK
  #>
  param(
    [string]$BaseName,
    [bool]$AggressiveJunk = $false
  )

  if ($script:RX_BIOS.IsMatch($BaseName))       { return "BIOS" }
  if ($script:RX_JUNK_TAGS.IsMatch($BaseName))   { return "JUNK" }
  if ($script:RX_JUNK_WORDS.IsMatch($BaseName))  { return "JUNK" }
  if ($AggressiveJunk -or $script:UseAggressiveJunk) {
    if ($script:RX_JUNK_TAGS_AGGRESSIVE.IsMatch($BaseName))  { return "JUNK" }
    if ($script:RX_JUNK_WORDS_AGGRESSIVE.IsMatch($BaseName)) { return "JUNK" }
  }

  return "GAME"
}

# ================================================================
#  CONSOLE DETECTION
# ================================================================
# ── Console-Map Layering Precedence (R-012) ──────────────────────
# Console maps (CONSOLE_FOLDER_MAP, CONSOLE_EXT_MAP, CONSOLE_RX_MAP_BASE, etc.)
# are populated from three sources. The effective precedence for any given key
# is determined by load order and overwrite semantics:
#
#   Layer 1 (lowest) : Hardcoded maps below         -- baseline definitions
#   Layer 2          : consoles.json via             -- additive only: new keys
#                      Import-ConsoleRegistry()         are added, existing
#                                                       hardcoded keys preserved
#   Layer 3          : console-maps.json via         -- REPLACES entire maps
#                      Import-RomCleanupJsonData()      when present (full override)
#   Layer 4 (highest): Console plugins via           -- free overwrite per key,
#                      Import-ConsolePlugins()          loaded last, wins always
#
# Summary for a single key:
#   Plugin > console-maps.json > Hardcoded > consoles.json
#
# Rebuild-ClassificationCompiledMaps must be called after any layer changes.
# ─────────────────────────────────────────────────────────────────

$script:CONSOLE_FOLDER_MAP = @{
  # Sony
  'ps1'='PS1'; 'psx'='PS1'; 'playstation'='PS1'; 'playstation1'='PS1'
  'ps2'='PS2'; 'playstation2'='PS2'; 'playstation 2'='PS2'
  'psp'='PSP'; 'playstation portable'='PSP'
  'ps3'='PS3'; 'playstation3'='PS3'; 'playstation 3'='PS3'
  'psvita'='VITA'; 'ps vita'='VITA'; 'vita'='VITA'; 'playstation vita'='VITA'
  # Sega
  'dreamcast'='DC'; 'dc'='DC'
  'saturn'='SAT'; 'sega saturn'='SAT'; 'ss'='SAT'
  'segacd'='SCD'; 'sega cd'='SCD'; 'mega-cd'='SCD'; 'megacd'='SCD'; 'mega cd'='SCD'
  'megadrive'='MD'; 'mega drive'='MD'; 'genesis'='MD'; 'md'='MD'; 'gen'='MD'
  'mastersystem'='SMS'; 'master system'='SMS'; 'sms'='SMS'
  'gamegear'='GG'; 'game gear'='GG'; 'gg'='GG'
  '32x'='32X'; 'sega 32x'='32X'; 'sega32x'='32X'; 'super 32x'='32X'
  'sg-1000'='SG1000'; 'sg1000'='SG1000'
  # Nintendo
  'nes'='NES'; 'famicom'='NES'; 'fc'='NES'; 'nintendo'='NES'
  'snes'='SNES'; 'super nintendo'='SNES'; 'super famicom'='SNES'; 'sfc'='SNES'
  'n64'='N64'; 'nintendo 64'='N64'; 'nintendo64'='N64'
  'gamecube'='GC'; 'gc'='GC'; 'ngc'='GC'; 'nintendo gamecube'='GC'
  'wii'='WII'
  'wiiu'='WIIU'; 'wii u'='WIIU'
  'gb'='GB'; 'gameboy'='GB'; 'game boy'='GB'
  'gbc'='GBC'; 'game boy color'='GBC'; 'gameboy color'='GBC'
  'gba'='GBA'; 'game boy advance'='GBA'; 'gameboy advance'='GBA'
  'nds'='NDS'; 'ds'='NDS'; 'nintendo ds'='NDS'
  '3ds'='3DS'; 'nintendo 3ds'='3DS'; 'n3ds'='3DS'
  'switch'='SWITCH'; 'nx'='SWITCH'; 'nintendo switch'='SWITCH'
  'virtual boy'='VB'; 'virtualboy'='VB'; 'vb'='VB'
  'pokemini'='POKEMINI'; 'pokemon mini'='POKEMINI'
  # NEC
  'pcengine'='PCE'; 'pc engine'='PCE'; 'pce'='PCE'; 'turbografx'='PCE'; 'turbografx-16'='PCE'
  'pcenginecd'='PCECD'; 'pc engine cd'='PCECD'; 'pcecd'='PCECD'; 'turbografx-cd'='PCECD'
  'pc-fx'='PCFX'; 'pcfx'='PCFX'
  # SNK
  'neogeo'='NEOGEO'; 'neo geo'='NEOGEO'; 'neo-geo'='NEOGEO'; 'ng'='NEOGEO'
  'neogeocd'='NEOCD'; 'neo geo cd'='NEOCD'; 'neo-geo cd'='NEOCD'; 'ngcd'='NEOCD'
  'neo geo pocket'='NGP'; 'neogeo pocket'='NGP'; 'ngp'='NGP'
  'neo geo pocket color'='NGPC'; 'neogeo pocket color'='NGPC'; 'ngpc'='NGPC'
  # Bandai
  'wonderswan'='WS'; 'wonder swan'='WS'; 'ws'='WS'
  'wonderswan color'='WSC'; 'wonderswancolor'='WSC'; 'wsc'='WSC'
  # Atari
  'atari 2600'='A26'; 'a26'='A26'; 'vcs'='A26'
  'atari 5200'='A52'; 'a52'='A52'
  'atari 7800'='A78'; 'a78'='A78'
  'atari lynx'='LYNX'; 'lynx'='LYNX'
  'atari st'='ATARIST'; 'atarist'='ATARIST'
  'atari jaguar'='JAG'; 'jaguar'='JAG'; 'jaguarcd'='JAGCD'; 'jaguar cd'='JAGCD'
  # Andere Konsolen
  'colecovision'='COLECO'; 'coleco'='COLECO'
  'intellivision'='INTV'; 'intv'='INTV'
  'vectrex'='VECTREX'
  'odyssey2'='ODYSSEY2'; 'odyssey 2'='ODYSSEY2'; 'videopac'='ODYSSEY2'
  'channelf'='CHANNELF'; 'channel f'='CHANNELF'; 'fairchild'='CHANNELF'
  'supervision'='SUPERVISION'; 'watara'='SUPERVISION'
  # Multimedia / CD
  '3do'='3DO'
  'cd-i'='CDI'; 'cdi'='CDI'; 'philips cd-i'='CDI'; 'philips cdi'='CDI'
  # Computer
  'dos'='DOS'; 'msdos'='DOS'; 'ms-dos'='DOS'; 'pc dos'='DOS'; 'ibm pc'='DOS'
  'msx'='MSX'; 'msx2'='MSX'
  'amiga'='AMIGA'; 'commodore amiga'='AMIGA'
  'c64'='C64'; 'commodore 64'='C64'
  'zx spectrum'='ZX'; 'zxspectrum'='ZX'; 'spectrum'='ZX'
  'amstrad cpc'='CPC'; 'cpc'='CPC'
  'pc-98'='PC98'; 'pc98'='PC98'
  'x68000'='X68K'; 'x68k'='X68K'; 'sharp x68000'='X68K'
  # Fujitsu
  'fm towns'='FMTOWNS'; 'fmtowns'='FMTOWNS'; 'fm-towns'='FMTOWNS'
  'fm towns marty'='FMTOWNS'; 'marty'='FMTOWNS'
  # Commodore CD
  'cd32'='CD32'; 'amiga cd32'='CD32'; 'commodore cd32'='CD32'
  # Microsoft
  'xbox'='XBOX'; 'xbox360'='X360'; 'xbox 360'='X360'; 'x360'='X360'
  # Arcade
  'arcade'='ARCADE'; 'mame'='ARCADE'; 'fbneo'='ARCADE'; 'fba'='ARCADE'
}

# UNIQUE_EXT_MAP: Extensions die eindeutig einer Plattform zugeordnet werden (Confidence hoch).
$script:CONSOLE_EXT_MAP_UNIQUE = @{
  '.gba'='GBA'; '.gbc'='GBC'; '.gb'='GB'
  '.nes'='NES'; '.sfc'='SNES'; '.smc'='SNES'
  '.n64'='N64'; '.z64'='N64'; '.v64'='N64'
  '.nds'='NDS'; '.3ds'='3DS'; '.cia'='3DS'
  '.nsp'='SWITCH'; '.xci'='SWITCH'
  '.md'='MD'; '.gen'='MD'; '.sms'='SMS'; '.gg'='GG'
  '.pce'='PCE'
  '.wbfs'='WII'; '.wad'='WII'
  # .gcz/.rvz entfernt: koennen GC ODER WII sein -> Erkennung via DolphinTool/Disc-ID
  '.wux'='WIIU'; '.rpx'='WIIU'
  # .pbp entfernt: kann PSP-Native ODER PS1-Classic (EBOOT.PBP via psxtract) sein
  '.vpk'='VITA'
  # Sega erweitert
  '.32x'='32X'; '.sg'='SG1000'; '.sc'='SG1000'
  # Atari
  '.a26'='A26'; '.a52'='A52'; '.a78'='A78'
  '.lnx'='LYNX'; '.st'='ATARIST'; '.stx'='ATARIST'
  '.j64'='JAG'
  # Andere Konsolen
  '.col'='COLECO'; '.int'='INTV'
  '.vb'='VB'; '.min'='POKEMINI'; '.vec'='VECTREX'
  # .ngc entfernt: kollidiert mit Folder-Alias 'ngc'->GC
  '.ngp'='NGP'
  '.ws'='WS'; '.wsc'='WSC'
  '.pcfx'='PCFX'; '.cdi'='CDI'
  '.gdi'='DC'    # GDI ist exklusiv Dreamcast
  # Computer
  '.mx1'='MSX'; '.mx2'='MSX'
  '.adf'='AMIGA'; '.d64'='C64'; '.t64'='C64'
  # .tap/.dsk entfernt: ambig (ZX/C64 bzw. CPC/MSX/AtariST/Amiga)
  '.tzx'='ZX'
  '.o2'='ODYSSEY2'
}

# AMBIG_EXT_MAP: Extensions die mehrere Plattformen abdecken KOENNEN.
# Werden nur als Fallback verwendet (nach Name-Regex), Confidence niedrig.
$script:CONSOLE_EXT_MAP_AMBIG = @{
  '.cso'='PSP'   # CSO ist typischerweise PSP, aber theoretisch generische ISO-Kompression
}

# Kombinierte Map fuer Rueckwaertskompatibilitaet (Plugins, JSON-Override, DatMapping)
$script:CONSOLE_EXT_MAP = @{}
foreach ($e in $script:CONSOLE_EXT_MAP_UNIQUE.GetEnumerator()) { $script:CONSOLE_EXT_MAP[$e.Key] = $e.Value }
foreach ($e in $script:CONSOLE_EXT_MAP_AMBIG.GetEnumerator())  { $script:CONSOLE_EXT_MAP[$e.Key] = $e.Value }

# REF-10: Console Registry -- Single Source of Truth
$script:CONSOLE_REGISTRY = $null

function Get-ConsoleRegistry {
  <#
  .SYNOPSIS
    Returns the console registry loaded from data/consoles.json.
  .DESCRIPTION
    Loads and caches the structured console definitions from consoles.json.
    Each entry has: Key, DisplayName, DiscBased, UniqueExts, AmbigExts, FolderAliases.
  #>
  if ($script:CONSOLE_REGISTRY) { return $script:CONSOLE_REGISTRY }

  $jsonPath = $null
  $candidates = @(
    (Join-Path $PSScriptRoot '..\..\data\consoles.json'),
    (Join-Path (Get-Location).Path 'data\consoles.json')
  )
  foreach ($c in $candidates) {
    if (Test-Path -LiteralPath $c -PathType Leaf) { $jsonPath = $c; break }
  }
  if (-not $jsonPath) { return @() }

  try {
    $raw = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 -ErrorAction Stop
    $data = $raw | ConvertFrom-Json -ErrorAction Stop
    if ($data.consoles) {
      $script:CONSOLE_REGISTRY = @($data.consoles)
      return $script:CONSOLE_REGISTRY
    }
  } catch { }
  return @()
}

function Get-SupportedConsolesList {
  <#
  .SYNOPSIS
    Returns a formatted list of all supported consoles from consoles.json.
  .DESCRIPTION
    Generates a list of supported consoles with their keys, display names,
    unique extensions, and whether they are disc-based. Useful for documentation
    and GUI help display.
  .PARAMETER Format
    Output format: 'Object' (default), 'Markdown', 'Text'.
  #>
  param(
    [ValidateSet('Object','Markdown','Text')][string]$Format = 'Object'
  )

  $registry = Get-ConsoleRegistry
  if (-not $registry -or $registry.Count -eq 0) { return $null }

  $list = foreach ($c in $registry) {
    $exts = @()
    if ($c.uniqueExts) { $exts += @($c.uniqueExts) }
    if ($c.ambigExts) { $exts += @($c.ambigExts) }
    [pscustomobject]@{
      Key         = [string]$c.key
      DisplayName = [string]$c.displayName
      Extensions  = ($exts -join ', ')
      DiscBased   = [bool]$c.discBased
      Aliases     = if ($c.folderAliases) { ($c.folderAliases -join ', ') } else { '' }
    }
  }

  switch ($Format) {
    'Markdown' {
      $lines = @('| Key | Konsole | Endungen | Disc | Ordner-Aliase |', '|-----|---------|----------|------|--------------|')
      foreach ($item in $list) {
        $disc = if ($item.DiscBased) { 'Ja' } else { '' }
        $lines += ('| {0} | {1} | {2} | {3} | {4} |' -f $item.Key, $item.DisplayName, $item.Extensions, $disc, $item.Aliases)
      }
      return ($lines -join "`n")
    }
    'Text' {
      $lines = foreach ($item in $list) {
        $exts = if ($item.Extensions) { " ($($item.Extensions))" } else { '' }
        '{0,-10} {1}{2}' -f $item.Key, $item.DisplayName, $exts
      }
      return ($lines -join "`n")
    }
    default { return @($list) }
  }
}

function Import-ConsoleRegistry {
  <#
  .SYNOPSIS
    Imports console definitions from consoles.json into the existing script-scope maps.
  .DESCRIPTION
    DUP-CS-01: consoles.json ist die autoritaere Quelle (Single Source of Truth).
    JSON-Eintraege ueberschreiben hardcodierte Werte. Hardcode dient nur als Fallback
    fuer den Fall, dass consoles.json fehlt oder unvollstaendig ist.
  #>
  $registry = Get-ConsoleRegistry
  if (-not $registry -or $registry.Count -eq 0) { return }

  foreach ($console in $registry) {
    $key = [string]$console.key
    if ([string]::IsNullOrWhiteSpace($key)) { continue }

    # Folder aliases — JSON ist autoritaer, ueberschreibt Hardcode
    if ($console.folderAliases) {
      foreach ($alias in @($console.folderAliases)) {
        $aliasLower = ([string]$alias).ToLowerInvariant()
        $script:CONSOLE_FOLDER_MAP[$aliasLower] = $key
      }
    }

    # Unique extensions — JSON ist autoritaer
    if ($console.uniqueExts) {
      foreach ($ext in @($console.uniqueExts)) {
        $extLower = ([string]$ext).ToLowerInvariant()
        $script:CONSOLE_EXT_MAP_UNIQUE[$extLower] = $key
        $script:CONSOLE_EXT_MAP[$extLower] = $key
      }
    }

    # Ambiguous extensions
    if ($console.ambigExts) {
      foreach ($ext in @($console.ambigExts)) {
        $extLower = ([string]$ext).ToLowerInvariant()
        if ($script:CONSOLE_EXT_MAP_AMBIG.ContainsKey($extLower)) {
          # Extension already mapped to a DIFFERENT console -> truly multi-platform
          # (e.g. .rvz/.gcz/.wia/.wbf1 shared by GC and WII).
          # Remove from all maps so Get-ConsoleType falls through to UNKNOWN
          # and requires DolphinTool/DiscID for resolution.
          if ($script:CONSOLE_EXT_MAP_AMBIG[$extLower] -ne $key) {
            $script:CONSOLE_EXT_MAP_AMBIG.Remove($extLower)
            if ($script:CONSOLE_EXT_MAP.ContainsKey($extLower) -and $script:CONSOLE_EXT_MAP[$extLower] -ne $key) {
              $script:CONSOLE_EXT_MAP.Remove($extLower)
            }
          }
        } else {
          $script:CONSOLE_EXT_MAP_AMBIG[$extLower] = $key
          if (-not $script:CONSOLE_EXT_MAP.ContainsKey($extLower)) {
            $script:CONSOLE_EXT_MAP[$extLower] = $key
          }
        }
      }
    }
  }
}

# Auto-import on module load
Import-ConsoleRegistry

function Resolve-ConsoleFromDiscText {
  <#
  .SYNOPSIS
    [R-002] Shared platform detection from printable-ASCII disc text.
  .DESCRIPTION
    Takes a string of printable ASCII extracted from disc image bytes
    (non-printable bytes replaced by spaces) and returns the console key
    for the first matching platform signature, or $null.
    Used by both Get-DiscHeaderConsole and Get-ChdDiscHeaderConsole to
    avoid duplicating the platform detection matrix.
  .PARAMETER Text
    Printable-ASCII text extracted from disc data.
  #>
  param([string]$Text)
  if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

  # --- Sega disc systems (IP.BIN / header strings) ---
  if     ($Text -match '(?i)SEGA.SEGAKATANA|SEGA.?DREAMCAST|SEGA\s*KATANA|DREAMCAST') { return 'DC'  }
  elseif ($Text -match '(?i)SEGA.SATURN|SEGASATURN|SEGA\s*SATURN')                    { return 'SAT' }
  elseif ($Text -match '(?i)SEGADISCSYSTEM|SEGA.MEGA.?CD|SEGA\s*CD')                  { return 'SCD' }
  # --- SNK Neo Geo CD ---
  elseif ($Text -match '(?i)NEOGEO\s*CD|NEO.?GEO')                                    { return 'NEOCD' }
  # --- NEC PC-FX (before PC Engine to avoid substring overlap) ---
  elseif ($Text -match '(?i)PC-FX:Hu_CD|PC-FX|NEC.*PC-FX')                            { return 'PCFX' }
  # --- NEC PC Engine CD ---
  elseif ($Text -match '(?i)PC\s*Engine|NEC\s*HOME\s*ELECTRONICS|TURBOGRAFX')          { return 'PCECD' }
  # --- Atari Jaguar CD ---
  elseif ($Text -match '(?i)ATARI\s*JAGUAR')                                           { return 'JAGCD' }
  # --- Amiga CD32 ---
  elseif ($Text -match '(?i)AMIGA\s*BOOT|CDTV|CD32')                                  { return 'CD32' }
  # --- Fujitsu FM Towns ---
  elseif ($Text -match '(?i)FM\s*TOWNS')                                               { return 'FMTOWNS' }
  # --- Sony PlayStation family ---
  elseif ($Text -match '(?i)Sony\s*Computer\s*Entertainment|PLAYSTATION') {
    if     ($Text -match '(?i)PSP\s*GAME')         { return 'PSP' }
    elseif ($Text -match '(?i)BOOT2\s*=|cdrom0:')  { return 'PS2' }
    elseif ($Text -match '(?i)playstation\s*2')     { return 'PS2' }
    return 'PS1'
  }
  # --- Microsoft Xbox ---
  elseif ($Text -match '(?i)MICROSOFT\*XBOX\*MEDIA')                                   { return 'XBOX' }

  return $null
}

function Get-DiscHeaderConsole {
  <#
  .SYNOPSIS
    Determine console by binary disc header signatures in ISO/GCM/IMG/BIN files.
  .DESCRIPTION
    Reads up to 128 KB from the disc image and probes for platform-specific
    magic bytes and metadata strings.
    Supported detections:
      GC     - magic 0xC2339F3D at offset 0x1C
      WII    - magic 0x5D1C9EA3 at offset 0x18
      3DO    - Opera filesystem signature (0x01 0x5A×5) at offset 0x00
      XBOX   - XDVDFS "MICROSOFT*XBOX*MEDIA" at offset 0x10000
      DC     - "SEGA SEGAKATANA" / "SEGA DREAMCAST" at sector 0
      SAT    - "SEGA SATURN" at sector 0
      SCD    - "SEGADISCSYSTEM" / "SEGA MEGA CD" at sector 0
      NEOCD  - "NEO-GEO" keyword in first 8 KB
      PCECD  - "PC Engine" / "NEC HOME ELECTRONICS" in first 8 KB
      PCFX   - "PC-FX" keyword in first 8 KB
      JAGCD  - "ATARI JAGUAR" keyword in first 8 KB
      CD32   - "AMIGA BOOT" / "CDTV" / "CD32" in first 8 KB
      FMTOWNS- "FM TOWNS" in first 8 KB or ISO9660 PVD system identifier
      PS1    - ISO9660 PVD with "PLAYSTATION" system identifier
      PS2    - ISO9660 PVD with "PLAYSTATION" + BOOT2 marker
      PSP    - ISO9660 PVD with "PLAYSTATION" + PSP GAME marker
    Handles both 2048 and 2352 bytes/sector layouts for CD-based platforms.
    Returns $null when no platform can be determined.
  .PARAMETER Path
    Full path to the disc image file (.iso, .gcm, .img, .bin).
  #>
  param([string]$Path)

  if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }

  # SEC-CS-06: Reset IO-Error-State vom vorherigen Aufruf, damit kein Stale-Error propagiert
  $script:LAST_DISC_HEADER_ERROR = $null

  # Return cached result if available (avoids re-reading 128 KB per call).
  if ($script:ISO_HEADER_CACHE.ContainsKey($Path)) {
    return $script:ISO_HEADER_CACHE[$Path]
  }

  $isoKey = $null
  try {
    $fs = [System.IO.File]::OpenRead($Path)
    try {
      if ($fs.Length -lt 32) { $isoKey = $null; return $null }

      # PERF: Read only first 32 bytes for magic-number pre-check.
      # If no early match (GC/Wii/3DO), read the full 128 KB buffer.
      $preBuffer = New-Object byte[] 32
      $preRead = $fs.Read($preBuffer, 0, 32)
      if ($preRead -lt 32) { $isoKey = $null; return $null }

      # ── GC magic at offset 0x1C: C2 33 9F 3D ──
      if ($preBuffer[0x1C] -eq 0xC2 -and $preBuffer[0x1D] -eq 0x33 -and
          $preBuffer[0x1E] -eq 0x9F -and $preBuffer[0x1F] -eq 0x3D) {
        $isoKey = 'GC'; return 'GC'
      }
      # ── Wii magic at offset 0x18: 5D 1C 9E A3 ──
      if ($preBuffer[0x18] -eq 0x5D -and $preBuffer[0x19] -eq 0x1C -and
          $preBuffer[0x1A] -eq 0x9E -and $preBuffer[0x1B] -eq 0xA3) {
        $isoKey = 'WII'; return 'WII'
      }
      # ── 3DO: Opera filesystem - record type 0x01 + five 0x5A sync bytes ──
      if ($preBuffer[0] -eq 0x01 -and $preBuffer[1] -eq 0x5A -and $preBuffer[2] -eq 0x5A -and
          $preBuffer[3] -eq 0x5A -and $preBuffer[4] -eq 0x5A -and $preBuffer[5] -eq 0x5A) {
        $isoKey = '3DO'; return '3DO'
      }

      # No early match -- read remaining bytes up to 128 KB for full scan
      $scanSize = [Math]::Min(131072, $fs.Length)
      $buffer = New-Object byte[] $scanSize
      [Array]::Copy($preBuffer, 0, $buffer, 0, 32)
      if ($scanSize -gt 32) {
        [void]$fs.Read($buffer, 32, ($scanSize - 32))
      }
      $read = $scanSize

      # ── Xbox / Xbox 360: XDVDFS signature "MICROSOFT*XBOX*MEDIA" at offset 0x10000 ──
      if ($read -ge (0x10000 + 20)) {
        $xboxSig = [System.Text.Encoding]::ASCII.GetString($buffer, 0x10000, 20)
        if ($xboxSig -eq 'MICROSOFT*XBOX*MEDIA') {
          $isoKey = 'XBOX'; return 'XBOX'
        }
      }

      # ── Sega IP.BIN detection (DC / SAT / SCD) ──
      # Check at offset 0x0000 (2048-byte sector ISO) and 0x0010 (2352-byte raw sector)
      # PERF-02: Verwende char-Array statt StringBuilder (vermeidet Allokation pro Aufruf)
      foreach ($dataOff in @(0x0000, 0x0010)) {
        if ($read -ge ($dataOff + 48)) {
          $ipChars = [char[]]::new(48)
          for ($i = 0; $i -lt 48; $i++) {
            $b = $buffer[$dataOff + $i]
            $ipChars[$i] = if ($b -ge 32 -and $b -le 126) { [char]$b } else { ' ' }
          }
          $ipStr = [string]::new($ipChars)
          if ($ipStr -match 'SEGA.SEGAKATANA|SEGA.DREAMCAST') { $isoKey = 'DC';  return 'DC'  }
          if ($ipStr -match 'SEGA.SATURN|SEGASATURN')          { $isoKey = 'SAT'; return 'SAT' }
          if ($ipStr -match 'SEGADISCSYSTEM|SEGA.MEGA.CD')     { $isoKey = 'SCD'; return 'SCD' }
        }
      }

      # ── Boot-sector keyword scan for remaining disc-based platforms ──
      # Scan the first 8 KB for platform-specific strings.
      # Only reached if GC, Wii, 3DO, Xbox, and Sega IP.BIN checks did not match.
      # PERF-02: Verwende char-Array statt StringBuilder (vermeidet Allokation pro Aufruf)
      $bootScanLen = [Math]::Min(8192, $read)
      if ($bootScanLen -gt 0) {
        $bootChars = [char[]]::new($bootScanLen)
        for ($i = 0; $i -lt $bootScanLen; $i++) {
          $b = $buffer[$i]
          $bootChars[$i] = if ($b -ge 32 -and $b -le 126) { [char]$b } else { ' ' }
        }
        $bootText = [string]::new($bootChars)
        # [R-002] Shared platform detection from boot-sector text
        $bootResult = Resolve-ConsoleFromDiscText -Text $bootText
        if ($bootResult) { $isoKey = $bootResult; return $bootResult }
      }

      # ── PS1/PS2/PSP via ISO9660 Primary Volume Descriptor ──
      # PVD at different offsets depending on sector size:
      #   2048 bytes/sector: sector 16 → offset 0x8000
      #   2352 bytes/sector Mode 1:     sector 16 → 16*2352 + 16 = 0x9310
      #   2352 bytes/sector Mode 2/XA:  sector 16 → 16*2352 + 24 = 0x9318
      foreach ($pvdOff in @(0x8000, 0x9310, 0x9318)) {
        if ($read -ge ($pvdOff + 0x28)) {
          # PVD magic: type byte 0x01 + "CD001"
          if ($buffer[$pvdOff]   -eq 0x01 -and
              $buffer[$pvdOff+1] -eq 0x43 -and   # C
              $buffer[$pvdOff+2] -eq 0x44 -and   # D
              $buffer[$pvdOff+3] -eq 0x30 -and   # 0
              $buffer[$pvdOff+4] -eq 0x30 -and   # 0
              $buffer[$pvdOff+5] -eq 0x31) {     # 1
            # System Identifier at PVD+8 (32 bytes field)
            $sysIdLen = [Math]::Min(32, $read - ($pvdOff + 8))
            $sysId = [System.Text.Encoding]::ASCII.GetString($buffer, $pvdOff + 8, $sysIdLen).Trim()
            if ($sysId -match '(?i)PLAYSTATION') {
              # Scan remaining buffer for PS2/PSP distinguishing markers
              $scanStart = $pvdOff
              $scanLen = [Math]::Min($read - $scanStart, 65536)
              $pvdText = [System.Text.Encoding]::ASCII.GetString($buffer, $scanStart, $scanLen)
              if ($pvdText -match '(?i)PSP\s*GAME')         { $isoKey = 'PSP'; return 'PSP' }
              if ($pvdText -match '(?i)BOOT2\s*=|cdrom0:')  { $isoKey = 'PS2'; return 'PS2' }
              $isoKey = 'PS1'; return 'PS1'
            }
            # FM Towns: PVD system identifier contains "FM TOWNS"
            if ($sysId -match '(?i)FM.?TOWNS') {
              $isoKey = 'FMTOWNS'; return 'FMTOWNS'
            }
          }
        }
      }

      # MISS-CS-03: NRG (Nero Image) Footer-Scan — Signatur am Ende der Datei
      # "NER5" (v5.5+) oder "NERO" (aeltere Versionen) in den letzten 12 Bytes
      if ($fs.Length -ge 12) {
        $fs.Position = $fs.Length - 12
        $footer = New-Object byte[] 12
        $footerRead = $fs.Read($footer, 0, 12)
        if ($footerRead -ge 8) {
          $sig = [System.Text.Encoding]::ASCII.GetString($footer, 0, 4)
          $sig2 = if ($footerRead -ge 12) { [System.Text.Encoding]::ASCII.GetString($footer, 4, 4) } else { '' }
          if ($sig -eq 'NER5' -or $sig -eq 'NERO' -or $sig2 -eq 'NER5' -or $sig2 -eq 'NERO') {
            # NRG is a generic disc image format — mark as disc but cannot determine console
            # Return $null but set diagnostic info for downstream
            $script:LAST_DISC_HEADER_ERROR = [pscustomobject]@{
              Path  = $Path
              Error = 'NRG disc image detected but console cannot be determined from NRG header alone'
              Type  = 'NRG_NO_CONSOLE'
            }
          }
        }
      }

      return $null
    } finally {
      $fs.Dispose()
    }
  } catch {
    # BUG-17: IO-Fehler als Reason-Code propagieren statt still verschlucken
    $script:LAST_DISC_HEADER_ERROR = [pscustomobject]@{
      Path    = $Path
      Error   = $_.Exception.Message
      Type    = 'IO_ERROR'
    }
    return $null
  } finally {
    # Cache result (including $null = no match) to skip re-scan on subsequent calls
    $script:ISO_HEADER_CACHE[$Path] = $isoKey
  }
}

function Get-ChdDiscHeaderConsole {
  <#
  .SYNOPSIS
    [F15] Determines the console type for a .chd disc image by scanning
    the CHD metadata section for known platform identifiers.
  .DESCRIPTION
    Reads up to 64 KB from the start of the CHD file and converts
    printable ASCII ranges to a string. Matches platform-specific
    keywords (e.g. "DREAMCAST", "SEGASATURN", PlayStation disc serials)
    to infer the target console without requiring chdman.
    NOTE: Wii/GC use proprietary disc formats (.wbfs/.rvz/.gcz), NOT .chd.
    Returns $null when no platform can be determined.
  .PARAMETER Path
    Full path to the .chd file.
  #>
  param([string]$Path)

  if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }

  # [F-04] Return cached result if available (avoids re-reading 64 KB per call).
  # $null is stored when no console was detected, to prevent redundant re-scans.
  if ($script:CHD_HEADER_CACHE.ContainsKey($Path)) {
    return $script:CHD_HEADER_CACHE[$Path]
  }

  $chdKey = $null
  try {
    $fs = [System.IO.File]::OpenRead($Path)
    try {
      # Read up to 64 KB for metadata scanning
      $scanSize = [Math]::Min(65536, $fs.Length)
      $raw = New-Object byte[] $scanSize
      [void]$fs.Read($raw, 0, $scanSize)

      # Verify CHD magic "MComprHD"
      $magic = [System.Text.Encoding]::ASCII.GetString($raw, 0, [Math]::Min(8, $scanSize))
      if ($magic -ne 'MComprHD') { $chdKey = $null }
      else {
        # Convert printable ASCII bytes to a searchable string
        # DEAD-07/PERF-06: char[] statt StringBuilder — vermeidet Resizing-Overhead
        $chars = New-Object char[] $scanSize
        for ($i = 0; $i -lt $scanSize; $i++) {
          $b = $raw[$i]
          $chars[$i] = if ($b -ge 32 -and $b -le 126) { [char]$b } else { ' ' }
        }
        $meta = New-Object string (,$chars)

        # [R-002] Platform detection via shared helper
        $chdKey = Resolve-ConsoleFromDiscText -Text $meta
      }
    } finally {
      $fs.Dispose()
    }
  } catch {
    $chdKey = $null
  }
  # [F-04] Cache result (including $null = no match) to skip re-scan on subsequent calls
  $script:CHD_HEADER_CACHE[$Path] = $chdKey
  return $chdKey
}

function Get-DiscHeaderConsoleBatch {
  <#
  .SYNOPSIS
    Batch-processes disc header scans for multiple files.
  .DESCRIPTION
    Scans an array of file paths and returns a hashtable mapping each path
    to its detected console key. Uses the cached single-file functions
    internally but provides a single-call interface for batch workflows.
  .PARAMETER Paths
    Array of file paths to scan.
  .OUTPUTS
    Hashtable of @{ [string]Path = [string]ConsoleKey }.
  #>
  param([string[]]$Paths)

  $results = @{}
  if (-not $Paths -or $Paths.Count -eq 0) { return $results }

  foreach ($p in $Paths) {
    if ([string]::IsNullOrWhiteSpace($p)) { continue }
    $ext = [System.IO.Path]::GetExtension($p).ToLowerInvariant()
    $key = $null
    if ($ext -eq '.chd') {
      $key = Get-ChdDiscHeaderConsole -Path $p
    } elseif ($ext -in @('.iso','.gcm','.img','.bin')) {
      $key = Get-DiscHeaderConsole -Path $p
    }
    $results[$p] = $key
  }
  return $results
}

$script:CONSOLE_RX_MAP_BASE = @(
  # Sony
  @{ Key='PSP';   Rx='playstation\s*portable|\bpsp\b' }
  @{ Key='PS2';   Rx='playstation\s*2|\bps2\b' }
  @{ Key='PS3';   Rx='playstation\s*3|\bps3\b' }
  @{ Key='PS1';   Rx='playstation(?!\s*[2-9]|\s*p|\s*v)|\bps1\b|\bpsx\b' }
  @{ Key='VITA';  Rx='\bvita\b|\bpsvita\b|playstation\s*vita' }
  # Sega
  @{ Key='DC';    Rx='dreamcast|\bdc\b' }
  @{ Key='SAT';   Rx='saturn|sega\s*saturn|\bss\b' }
  @{ Key='SCD';   Rx='sega\s*cd|mega-?cd|segacd' }
  @{ Key='32X';   Rx='\b32x\b|sega\s*32x|super\s*32x' }
  @{ Key='SG1000';Rx='\bsg-?1000\b' }
  # Nintendo
  @{ Key='GC';    Rx='gamecube|\bgc\b|\bngc\b' }
  @{ Key='WII';   Rx='\bwii\b(?!\s*u)' }
  @{ Key='WIIU';  Rx='wii\s*u|\bwiiu\b' }
  @{ Key='SWITCH';Rx='switch|\bnx\b' }
  @{ Key='NDS';   Rx='nintendo\s*ds|\bnds\b' }
  @{ Key='3DS';   Rx='nintendo\s*3ds|\b3ds\b' }
  @{ Key='GBA';   Rx='game\s*boy\s*advance|\bgba\b' }
  @{ Key='GBC';   Rx='game\s*boy\s*color|\bgbc\b' }
  @{ Key='GB';    Rx='game\s*boy(?!\s*a|\s*c)|\bgb\b' }
  @{ Key='VB';    Rx='virtual\s*boy|\bvb\b' }
  @{ Key='POKEMINI';Rx='\bpokemini\b|pokemon\s*mini' }
  @{ Key='SNES';  Rx='super\s*nintendo|super\s*famicom|\bsnes\b|\bsfc\b' }
  @{ Key='NES';   Rx='famicom(?!.*disk)|\bnes\b' }
  @{ Key='N64';   Rx='nintendo\s*64|\bn64\b' }
  # Sega cartridge
  @{ Key='MD';    Rx='megadrive|mega\s*drive|genesis|\bmd\b' }
  @{ Key='SMS';   Rx='master\s*system|\bsms\b' }
  @{ Key='GG';    Rx='game\s*gear|\bgg\b' }
  # NEC
  @{ Key='PCE';   Rx='pc\s*engine(?!\s*cd)|\bpce\b(?!cd)|turbografx(?!-?cd)' }
  @{ Key='PCECD'; Rx='pc\s*engine\s*cd|turbografx-?cd|\bpcecd\b' }
  @{ Key='PCFX';  Rx='\bpc-?fx\b' }
  # SNK
  @{ Key='NEOGEO';Rx='neogeo(?!\s*cd|\s*pocket)|neo\s*geo(?!\s*cd|\s*pocket)|neo-?geo(?!\s*cd|\s*pocket)' }
  @{ Key='NEOCD'; Rx='neogeo\s*cd|neo\s*geo\s*cd|neo-?geo\s*cd|\bngcd\b' }
  @{ Key='NGPC';  Rx='neo\s*geo\s*pocket\s*color|\bngpc\b' }
  @{ Key='NGP';   Rx='neo\s*geo\s*pocket|\bngp\b' }
  # Bandai
  @{ Key='WS';    Rx='wonderswan(?!\s*color)|\bws\b(?!c)' }
  @{ Key='WSC';   Rx='wonderswan\s*color|\bwsc\b' }
  # Atari
  @{ Key='A26';   Rx='atari\s*2600|\ba26\b|\bvcs\b' }
  @{ Key='A52';   Rx='atari\s*5200|\ba52\b' }
  @{ Key='A78';   Rx='atari\s*7800|\ba78\b' }
  @{ Key='LYNX';  Rx='\blynx\b|atari\s*lynx' }
  @{ Key='ATARIST';Rx='atari\s*st\b' }
  # Andere Konsolen
  @{ Key='COLECO';Rx='colecovision|\bcoleco\b' }
  @{ Key='INTV';  Rx='intellivision|\bintv\b' }
  @{ Key='VECTREX';Rx='\bvectrex\b' }
  @{ Key='ODYSSEY2';Rx='odyssey\s*2|\bodyssey2\b|\bvideopac\b' }
  @{ Key='CHANNELF';Rx='channel\s*f|\bchannelf\b|fairchild' }
  @{ Key='SUPERVISION';Rx='\bsupervision\b|\bwatara\b' }
  # Multimedia / CD
  @{ Key='3DO';   Rx='\b3do\b' }
  @{ Key='CDI';   Rx='\bcd-?i\b|philips\s*cd' }
  @{ Key='JAG';   Rx='jaguar(?!\s*cd)' }
  @{ Key='JAGCD'; Rx='jaguar\s*cd|\bjagcd\b' }
  # Computer
  @{ Key='MSX';   Rx='\bmsx2?\b' }
  @{ Key='AMIGA'; Rx='\bamiga(?!\s*cd)|\bcommodore\s*amiga\b' }
  @{ Key='CD32';  Rx='\bcd32\b|amiga\s*cd\s*32|commodore\s*cd\s*32' }
  @{ Key='C64';   Rx='\bc64\b|commodore\s*64' }
  @{ Key='ZX';    Rx='zx\s*spectrum|\bspectrum\b' }
  @{ Key='CPC';   Rx='amstrad\s*cpc|\bcpc\b' }
  @{ Key='PC98';  Rx='\bpc-?98\b|nec\s*pc-?98' }
  @{ Key='X68K';  Rx='\bx68000\b|\bx68k\b|sharp\s*x68' }
  @{ Key='FMTOWNS';Rx='\bfm.?towns\b|\bmarty\b' }
  # Microsoft
  @{ Key='XBOX';  Rx='\bxbox(?!\s*360|\s*one|\s*series)\b' }
  @{ Key='X360';  Rx='xbox\s*360|\bx360\b' }
  # DOS / Arcade
  @{ Key='DOS';   Rx='\bms-?dos\b|\bpc\s*dos\b|\bibm\s*pc\b|\bdos\b' }
  @{ Key='ARCADE';Rx='arcade|mame|fbneo|fba' }
)

$script:CONSOLE_FOLDER_RX_MAP = @($script:CONSOLE_RX_MAP_BASE | ForEach-Object {
  @{ Key = $_.Key; Rx = $_.Rx; RxObj = [regex]::new($_.Rx, 'IgnoreCase') }
})
# BUG-CS-07: Fuer Filename-Regex kurze 2-Buchstaben-Patterns entfernen,
# die zu False-Positives fuehren (z.B. "Washington DC" -> DC, "GG Allin" -> GG).
# Folder-Regex behaelt alle Patterns, da Ordnernamen i.d.R. bewusst benannt sind.
# BUG-CS-06: Auch 3-Buchstaben-Codes 'dos' und 'arcade' filtern –
# 'dos' matched in Dateinamen (z.B. "Shadow of the Colossus"), 'arcade' ist zu generisch.
$script:_ShortAmbigCodes = [System.Collections.Generic.HashSet[string]]::new(
  [string[]]@('dc', 'gg', 'md', 'gb', 'vb', 'ss', 'ws', 'dos'),
  [StringComparer]::OrdinalIgnoreCase
)
# Patterns die aus dem Filename-Regex komplett entfernt werden (zu viele False Positives)
$script:_NameRxStripPatterns = [System.Collections.Generic.HashSet[string]]::new(
  [string[]]@('\bdos\b', 'arcade', 'mame', 'fbneo', 'fba'),
  [StringComparer]::OrdinalIgnoreCase
)
$script:CONSOLE_NAME_RX_MAP = @($script:CONSOLE_RX_MAP_BASE | ForEach-Object {
  $rx = $_.Rx
  # BUG-CS-06: Entferne kurze/ambige Patterns aus dem Filename-Regex
  if ($script:_ShortAmbigCodes.Contains($_.Key)) {
    $parts = @($rx -split '\|' | Where-Object {
      $p = $_.Trim()
      # Kurze \bXX\b oder \bXXX\b Patterns entfernen
      if ($p -match '^\\b[a-z]{2,3}\\b$') { return $false }
      # Explizit gelistete Strip-Patterns entfernen
      if ($script:_NameRxStripPatterns.Contains($p)) { return $false }
      return $true
    })
    if ($parts.Count -gt 0) {
      $rx = $parts -join '|'
    }
  }
  @{ Key = $_.Key; Rx = $rx; RxObj = [regex]::new($rx, 'IgnoreCase') }
})
# DUP-CS-04: DAT_CONSOLE_RX_MAP direkt auf CONSOLE_RX_MAP_BASE referenzieren.
# BUG-CS-07: Whitelist-Filter war No-Op — alle Keys bereits enthalten.
$script:DAT_CONSOLE_RX_MAP = $script:CONSOLE_RX_MAP_BASE

function Rebuild-ClassificationCompiledMaps {
  <# Recompiles CONSOLE_FOLDER_RX_MAP and CONSOLE_NAME_RX_MAP from
     CONSOLE_RX_MAP_BASE. Call after any change to the base map (e.g.
     after plugin loading or JSON data import).
     R-012: Asserts that base data is present after layering. #>

  # R-012 assertion: at least the hardcoded layer must be present
  if (-not $script:CONSOLE_RX_MAP_BASE -or $script:CONSOLE_RX_MAP_BASE.Count -eq 0) {
    Write-Warning 'Rebuild-ClassificationCompiledMaps: CONSOLE_RX_MAP_BASE is empty -- hardcoded layer missing.'
  }
  if (-not $script:CONSOLE_FOLDER_MAP -or $script:CONSOLE_FOLDER_MAP.Count -eq 0) {
    Write-Warning 'Rebuild-ClassificationCompiledMaps: CONSOLE_FOLDER_MAP is empty -- hardcoded layer missing.'
  }

  $script:CONSOLE_FOLDER_RX_MAP = @($script:CONSOLE_RX_MAP_BASE | ForEach-Object {
    @{ Key = $_.Key; Rx = $_.Rx; RxObj = [regex]::new($_.Rx, 'IgnoreCase, Compiled') }
  })
  # BUG-CS-06: Filename-Regex mit gleicher Filterung wie beim Modul-Init
  $script:CONSOLE_NAME_RX_MAP = @($script:CONSOLE_RX_MAP_BASE | ForEach-Object {
    $rx = $_.Rx
    if ($script:_ShortAmbigCodes.Contains($_.Key)) {
      $parts = @($rx -split '\|' | Where-Object {
        $p = $_.Trim()
        if ($p -match '^\\b[a-z]{2,3}\\b$') { return $false }
        if ($script:_NameRxStripPatterns.Contains($p)) { return $false }
        return $true
      })
      if ($parts.Count -gt 0) { $rx = $parts -join '|' }
    }
    @{ Key = $_.Key; Rx = $rx; RxObj = [regex]::new($rx, 'IgnoreCase, Compiled') }
  })
}

if (Get-Command Import-RomCleanupJsonData -ErrorAction SilentlyContinue) {
  $consoleMaps = Import-RomCleanupJsonData -FileName 'console-maps.json'
  if ($consoleMaps -and ($consoleMaps -is [System.Collections.IDictionary])) {
    if ($consoleMaps.Contains('ConsoleFolderMap') -and $consoleMaps.ConsoleFolderMap) {
      $script:CONSOLE_FOLDER_MAP = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      foreach ($entry in $consoleMaps.ConsoleFolderMap.GetEnumerator()) {
        $script:CONSOLE_FOLDER_MAP[[string]$entry.Key] = [string]$entry.Value
      }
    }
    if ($consoleMaps.Contains('ConsoleExtMap') -and $consoleMaps.ConsoleExtMap) {
      # JSON-Override ersetzt die kombinierte Map UND aktualisiert UNIQUE/AMBIG
      $script:CONSOLE_EXT_MAP = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      $script:CONSOLE_EXT_MAP_UNIQUE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      foreach ($entry in $consoleMaps.ConsoleExtMap.GetEnumerator()) {
        $script:CONSOLE_EXT_MAP[[string]$entry.Key] = [string]$entry.Value
        $script:CONSOLE_EXT_MAP_UNIQUE[[string]$entry.Key] = [string]$entry.Value
      }
      # Ambige Map wird geleert wenn JSON-Override aktiv (alle Eintraege gelten als UNIQUE)
      $script:CONSOLE_EXT_MAP_AMBIG = @{}
    }
    if ($consoleMaps.Contains('ConsoleExtMapAmbig') -and $consoleMaps.ConsoleExtMapAmbig) {
      $script:CONSOLE_EXT_MAP_AMBIG = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      foreach ($entry in $consoleMaps.ConsoleExtMapAmbig.GetEnumerator()) {
        $script:CONSOLE_EXT_MAP_AMBIG[[string]$entry.Key] = [string]$entry.Value
        $script:CONSOLE_EXT_MAP[[string]$entry.Key] = [string]$entry.Value
      }
    }
    if ($consoleMaps.Contains('ConsoleRxMapBase') -and $consoleMaps.ConsoleRxMapBase) {
      $script:CONSOLE_RX_MAP_BASE = @($consoleMaps.ConsoleRxMapBase)
    }
    if ($consoleMaps.Contains('DatConsoleRxMap') -and $consoleMaps.DatConsoleRxMap) {
      $script:DAT_CONSOLE_RX_MAP = @($consoleMaps.DatConsoleRxMap)
    }

    Rebuild-ClassificationCompiledMaps
  }
}

# --- Double-Extension Normalisierung ---
# [IO.Path]::GetExtension('game.nkit.iso') gibt '.iso' zurueck, was .nkit.iso nicht erkennt.
# Diese Regex-basierte Funktion erkennt bekannte Double-Extensions.
# REF-DET-04: Erweiterte Double-Extension-Erkennung fuer alle bekannten Formate.
$script:DOUBLE_EXT_RX = [regex]::new('\.(nkit\.(iso|gcz)|ecm\.(bin|img)|wia\.gcz)$', 'IgnoreCase, Compiled')

function Get-NormalizedExtension {
  <# Returns the effective extension, including known double-extensions like .nkit.iso. #>
  param([string]$FileName)

  if ([string]::IsNullOrWhiteSpace($FileName)) { return '' }
  if ($script:DOUBLE_EXT_RX.IsMatch($FileName)) {
    $m = $script:DOUBLE_EXT_RX.Match($FileName)
    return $m.Value.ToLowerInvariant()
  }
  $ext = [IO.Path]::GetExtension($FileName)
  if ($ext) { return $ext.ToLowerInvariant() }
  return ''
}

# --- PBP Header Detection (PS1-Classic vs PSP-Native) ---
function Get-PbpConsole {
  <# Detect whether a .pbp file is a PS1 Classic or PSP native title.
     PBP format: Magic "\0PBP" at offset 0, followed by version and SFO offset.
     If the SFO section contains 'PS1' category or DISC_ID with SLUS/SCUS/SLES/SCES
     prefix, it's a PS1 classic repackaged for PSP. Otherwise assume PSP native. #>
  param([string]$Path)

  if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
  try {
    $fs = [System.IO.File]::OpenRead($Path)
    try {
      if ($fs.Length -lt 40) { return 'PSP' }
      $header = New-Object byte[] 40
      $read = $fs.Read($header, 0, 40)
      if ($read -lt 40) { return 'PSP' }
      # PBP Magic: 0x00 0x50 0x42 0x50 ("\0PBP")
      if ($header[0] -ne 0x00 -or $header[1] -ne 0x50 -or
          $header[2] -ne 0x42 -or $header[3] -ne 0x50) {
        return 'PSP'
      }
      # SFO offset at bytes 8-11 (little-endian)
      $sfoOff = [BitConverter]::ToUInt32($header, 8)
      # Icon0 offset at bytes 12-15 - the SFO section is between these two
      $icon0Off = [BitConverter]::ToUInt32($header, 12)
      if ($sfoOff -ge $fs.Length -or $sfoOff -ge $icon0Off) { return 'PSP' }
      $sfoLen = [Math]::Min([int]($icon0Off - $sfoOff), 8192)
      if ($sfoLen -lt 16) { return 'PSP' }
      $null = $fs.Seek($sfoOff, [System.IO.SeekOrigin]::Begin)
      $sfoData = New-Object byte[] $sfoLen
      $sfoRead = $fs.Read($sfoData, 0, $sfoLen)
      if ($sfoRead -lt 16) { return 'PSP' }
      $sfoText = [System.Text.Encoding]::ASCII.GetString($sfoData, 0, $sfoRead)
      # PS1 classics typically have DISC_ID with PlayStation serial prefixes
      if ($sfoText -match '(?i)SL[UE][SCP]-\d{5}|SC[UE][SCP]-\d{5}|PAPX-\d{5}') {
        return 'PS1'
      }
      return 'PSP'
    } finally {
      $fs.Dispose()
    }
  } catch {
    return 'PSP'
  }
}

$script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
$script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
# REF-CS-01: Paralleler Source-Cache — speichert Source-Infos zum CONSOLE_TYPE_CACHE
$script:CONSOLE_TYPE_SOURCE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

# OPT-02: Max cache size to prevent unbounded growth with large ROM collections
$script:CONSOLE_TYPE_CACHE_MAX = 50000

# PERF-CS-01: Root-Normalisierung Cache — vermeidet redundante Resolve-RootPath Aufrufe
$script:ROOT_PATH_NORM_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

function Get-NormalizedRootPathCached {
  <# Returns Resolve-RootPath result from cache, computing once per unique input. #>
  param([string]$RootPath)
  if ([string]::IsNullOrWhiteSpace($RootPath)) { return '' }
  if ($script:ROOT_PATH_NORM_CACHE.ContainsKey($RootPath)) {
    return $script:ROOT_PATH_NORM_CACHE[$RootPath]
  }
  $norm = Resolve-RootPath -Path $RootPath
  $script:ROOT_PATH_NORM_CACHE[$RootPath] = $norm
  return $norm
}

# REF-01: Detection-Result Cache (FilePath -> DetectionResult pscustomobject)
$script:DETECTION_RESULT_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

function Reset-ClassificationCaches {
  <# Clears console-type lookup caches. Call before each major operation
     (dedupe, console-sort, etc.) so stale mappings don't persist. #>
  $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:CONSOLE_TYPE_SOURCE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:DETECTION_RESULT_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:ROOT_PATH_NORM_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

# ================================================================
#  REF-01: Invoke-ConsoleDetection – Structured Detection Pipeline
#  Returns a DetectionResult pscustomobject with Console, Confidence,
#  Reason, Evidence, Source, Tried, DiagInfo.
#  REF-07: Decouples detection logic from Invoke-ConsoleSort.
#  REF-12: Complete UNKNOWN diagnosis via DiagInfo.
#  BUG-17: IO-Error Reason-Code propagation.
# ================================================================

function New-DetectionResult {
  <# Creates a new DetectionResult pscustomobject. #>
  param(
    [string]$Console = 'UNKNOWN',
    [int]$Confidence = 0,
    [string]$Reason = '',
    [string]$Evidence = '',
    [string]$Source = 'NONE',
    [string[]]$Tried = @(),
    [hashtable]$DiagInfo = @{}
  )
  return [pscustomobject]@{
    Console    = $Console
    Confidence = $Confidence
    Reason     = $Reason
    Evidence   = $Evidence
    Source     = $Source
    Tried      = $Tried
    DiagInfo   = $DiagInfo
  }
}

function Invoke-ConsoleDetection {
  <#
  .SYNOPSIS
    Zentrale Detection-Pipeline fuer ROM-Dateien.
    Gibt ein strukturiertes DetectionResult zurueck.
  .DESCRIPTION
    Fuehrt alle Detection-Stufen in Reihenfolge fallender Konfidenz aus:
      1. DAT Hash Match          (Confidence 100, Source 'DAT')
      2. Archive Content Ext     (Confidence 70,  Source 'ARCHIVE_EXT')
      3. Archive Disc Header     (Confidence 95,  Source 'ARCHIVE_HEADER')
      4. DolphinTool Disc-ID     (Confidence 90,  Source 'TOOL')
      4b. Disc-ID from Filename  (Confidence 85,  Source 'FILENAME_DISC_ID')
      5. Get-ConsoleType Heuristik:
         5a. Folder Map          (Confidence 50,  Source 'FOLDER')
         5b. Disc Header         (Confidence 95,  Source 'DISC_HEADER')
         5c. Unique Ext Map      (Confidence 60,  Source 'EXT_MAP')
         5d. Filename Regex      (Confidence 30,  Source 'FILENAME_RX')
         5e. Ambig Ext Map       (Confidence 40,  Source 'EXT_MAP_AMBIG')
      6. UNKNOWN                 (Confidence 0,   Source 'NONE')
  .PARAMETER FilePath
    Full path to the file.
  .PARAMETER RootPath
    Root directory for relative path computation.
  .PARAMETER Extension
    File extension (lowercase, dot-prefixed).
  .PARAMETER NormalizedExtension
    Normalized extension (handles double-extensions like .nkit.iso).
  .PARAMETER UseDat
    Whether DAT hash lookup is enabled.
  .PARAMETER DatHashToConsole
    Reverse hash-to-console hashtable.
  .PARAMETER DatHashType
    Hash algorithm for DAT lookup (SHA1, MD5, CRC32).
  .PARAMETER HashCache
    Cache hashtable for file hashes.
  .PARAMETER Use7z
    Whether 7-Zip is available.
  .PARAMETER SevenZipPath
    Path to the 7z executable.
  .PARAMETER UseDolphin
    Whether DolphinTool is available.
  .PARAMETER DolphinToolPath
    Path to the dolphintool executable.
  .PARAMETER DolphinExts
    HashSet of extensions eligible for DolphinTool.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$FilePath,
    [string]$RootPath,
    [string]$Extension,
    [string]$NormalizedExtension,
    [bool]$UseDat = $false,
    [hashtable]$DatHashToConsole,
    [string]$DatHashType = 'SHA1',
    [hashtable]$HashCache,
    [bool]$Use7z = $false,
    [string]$SevenZipPath,
    [bool]$UseDolphin = $false,
    [string]$DolphinToolPath,
    $DolphinExts,
    [ValidateSet('None','PS1PS2')][string]$ZipSortStrategy = 'None'
  )

  $ext = if ($Extension) { $Extension.ToLowerInvariant() } else { '' }
  $normExt = if ($NormalizedExtension) { $NormalizedExtension } else { $ext }
  $tried = [System.Collections.Generic.List[string]]::new()
  $diagInfo = @{}
  $console = $null
  $confidence = 0
  $reason = ''
  $evidence = ''
  $source = 'NONE'
  $reasonCode = $null

  # --- Stufe 1: DAT Hash Match (hoechste Konfidenz) ---
  if ($UseDat -and $DatHashToConsole -and $DatHashToConsole.Count -gt 0) {
    $tried.Add('DAT')
    $hash = $null
    if (Get-Command Get-FileHashCached -ErrorAction SilentlyContinue) {
      $hash = Get-FileHashCached -Path $FilePath -HashType $DatHashType -Cache $HashCache
    }
    if ($hash -and $DatHashToConsole.ContainsKey($hash)) {
      $console = [string]$DatHashToConsole[$hash]
      $confidence = 100
      $reason = 'DAT-Hash-Match'
      $evidence = '{0}:{1}' -f $DatHashType, $hash
      $source = 'DAT'
      $diagInfo['DAT'] = 'MATCH'
    } else {
      $diagInfo['DAT'] = if ($hash) { 'NO_MATCH' } else { 'HASH_FAILED' }
    }
  }

  # --- Stufe 1b: ZIP PS1/PS2 Detection (ZipSort-Strategie) ---
  if (-not $console -and $ZipSortStrategy -eq 'PS1PS2' -and $ext -eq '.zip') {
    $tried.Add('ZIP_PS1PS2')
    try {
      if (Get-Command Get-ZipEntryExtensions -ErrorAction SilentlyContinue) {
        $innerExts = @(Get-ZipEntryExtensions -ZipPath $FilePath)
        if ($innerExts.Count -gt 0) {
          $zipPs1Exts = @('.ccd', '.sub', '.pbp')
          # BUG-CS-06: .iso entfernt - ist multi-plattform (DC/SAT/3DO/Xbox etc.)
          $zipPs2Exts = @('.nrg', '.mdf', '.mds', '.gz')
          if ($innerExts | Where-Object { $zipPs1Exts -contains $_ } | Select-Object -First 1) {
            $console = 'PS1'
            $confidence = 75
            $reason = 'ZIP-Inhalt enthaelt PS1-typische Dateien'
            $evidence = 'ZipSort PS1 Exts: ' + (($innerExts | Where-Object { $zipPs1Exts -contains $_ }) -join ', ')
            $source = 'ZIP_PS1PS2'
            $diagInfo['ZIP_PS1PS2'] = 'MATCH PS1'
          } elseif ($innerExts | Where-Object { $zipPs2Exts -contains $_ } | Select-Object -First 1) {
            $console = 'PS2'
            $confidence = 75
            $reason = 'ZIP-Inhalt enthaelt PS2-typische Dateien'
            $evidence = 'ZipSort PS2 Exts: ' + (($innerExts | Where-Object { $zipPs2Exts -contains $_ }) -join ', ')
            $source = 'ZIP_PS1PS2'
            $diagInfo['ZIP_PS1PS2'] = 'MATCH PS2'
          } else {
            $diagInfo['ZIP_PS1PS2'] = 'NO_MATCH'
          }
        } else {
          $diagInfo['ZIP_PS1PS2'] = 'EMPTY_ARCHIVE'
        }
      }
    } catch {
      $diagInfo['ZIP_PS1PS2'] = 'ERROR: ' + $_.Exception.Message
    }
  }

  # --- Stufe 2: Archive Content (ZIP/7Z) ---
  if (-not $console -and $ext -in @('.zip', '.7z')) {
    if ($Use7z -and $SevenZipPath) {
      $tried.Add('ARCHIVE_EXT')
      $entryPaths = @()
      if (Get-Command Get-ArchiveEntryPaths -ErrorAction SilentlyContinue) {
        $entryPaths = @(Get-ArchiveEntryPaths -ArchivePath $FilePath -SevenZipPath $SevenZipPath -TempFiles $null)
      }
      if (Get-Command Get-ConsoleFromArchiveEntries -ErrorAction SilentlyContinue) {
        $archConsole = Get-ConsoleFromArchiveEntries -EntryPaths $entryPaths
      }
      if (-not $archConsole -and $entryPaths) {
        $hasWbfs = $entryPaths | Where-Object { $_ -match '\.wbfs$' } | Select-Object -First 1
        if ($hasWbfs) { $archConsole = 'WII' }
      }
      if ($archConsole) {
        $console = $archConsole
        $confidence = 70
        $reason = 'Archive-Entry-Extensions'
        $evidence = '{0} Entries' -f $entryPaths.Count
        $source = 'ARCHIVE_EXT'
        $diagInfo['ARCHIVE_EXT'] = 'MATCH'
      } else {
        # BUG-CS-03: Bessere Diagnostik bei Disc-Set-Archiven
        if ($script:LAST_ARCHIVE_HAS_DISC_SET) {
          $reasonCode = 'ARCHIVE_DISC_SET_NEEDS_HEADER'
          $diagInfo['ARCHIVE_EXT'] = 'DISC_SET_FOUND'
        } elseif ($script:LAST_ARCHIVE_MIXED_CONSOLES -and $script:LAST_ARCHIVE_MIXED_CONSOLES.Count -gt 1) {
          # MISS-CS-06: Mixed-Content Archive — welche Konsolen wurden gefunden?
          $reasonCode = 'ARCHIVE_MIXED_CONTENT'
          $diagInfo['ARCHIVE_EXT'] = 'MIXED: ' + ($script:LAST_ARCHIVE_MIXED_CONSOLES -join ', ')
        } else {
          $reasonCode = 'ARCHIVE_AMBIGUOUS_EXT'
          $diagInfo['ARCHIVE_EXT'] = 'NO_MATCH'
        }
      }
    } else {
      $reasonCode = 'ARCHIVE_TOOL_MISSING'
      $diagInfo['ARCHIVE_EXT'] = '7Z_UNAVAILABLE'
    }
  }

  # --- Stufe 3: Archive Disc Header ---
  if (-not $console -and $ext -in @('.zip', '.7z') -and $Use7z -and $SevenZipPath) {
    $tried.Add('ARCHIVE_HEADER')
    $archHeaderConsole = $null
    if (Get-Command Get-ArchiveDiscHeaderConsole -ErrorAction SilentlyContinue) {
      $archHeaderConsole = Get-ArchiveDiscHeaderConsole -ArchivePath $FilePath -SevenZipPath $SevenZipPath
    }
    if ($archHeaderConsole) {
      $console = $archHeaderConsole
      $confidence = 95
      $reason = 'Archive-Disc-Header-Match'
      $evidence = $archHeaderConsole
      $source = 'ARCHIVE_HEADER'
      $diagInfo['ARCHIVE_HEADER'] = 'MATCH'
    } else {
      if (-not $reasonCode) { $reasonCode = 'ARCHIVE_DISC_HEADER_MISSING' }
      $diagInfo['ARCHIVE_HEADER'] = 'NO_MATCH'
    }
  }

  # --- Stufe 4: DolphinTool (GC/Wii Disc-ID) ---
  if (-not $console -and $UseDolphin -and $DolphinToolPath) {
    $isDolphinEligible = $false
    if ($DolphinExts) { $isDolphinEligible = $DolphinExts.Contains($ext) }
    if (-not $isDolphinEligible -and $normExt -in @('.nkit.iso', '.nkit.gcz')) {
      $isDolphinEligible = $true
    }
    if ($isDolphinEligible) {
      $tried.Add('TOOL')
      $toolConsole = $null
      if (Get-Command Get-ConsoleFromDolphinTool -ErrorAction SilentlyContinue) {
        $toolConsole = Get-ConsoleFromDolphinTool -Path $FilePath -ToolPath $DolphinToolPath
      }
      if ($toolConsole) {
        $console = $toolConsole
        $confidence = 90
        $reason = 'DolphinTool-Disc-ID'
        $evidence = $toolConsole
        $source = 'TOOL'
        $diagInfo['TOOL'] = 'MATCH'
      } else {
        if (-not $reasonCode) { $reasonCode = 'DOLPHIN_DISC_ID_MISSING' }
        $diagInfo['TOOL'] = 'NO_MATCH'
      }
    }
  }

  # --- Stufe 4b: Disc-ID aus Dateiname Fallback ---
  if (-not $console) {
    $isDolphinEligible2 = $false
    if ($DolphinExts) { $isDolphinEligible2 = $DolphinExts.Contains($ext) }
    if (-not $isDolphinEligible2 -and $normExt -in @('.nkit.iso', '.nkit.gcz')) {
      $isDolphinEligible2 = $true
    }
    if ($isDolphinEligible2) {
      $tried.Add('FILENAME_DISC_ID')
      if (Get-Command Get-ConsoleFromDiscIdInFileName -ErrorAction SilentlyContinue) {
        $fnConsole = Get-ConsoleFromDiscIdInFileName -Path $FilePath
        if ($fnConsole) {
          $console = $fnConsole
          $confidence = 85
          $reason = 'Disc-ID im Dateinamen'
          $evidence = [IO.Path]::GetFileNameWithoutExtension($FilePath)
          $source = 'FILENAME_DISC_ID'
          $diagInfo['FILENAME_DISC_ID'] = 'MATCH'
        } else {
          $diagInfo['FILENAME_DISC_ID'] = 'NO_MATCH'
        }
      }
    }
  }

  # --- BUG-CS-01/02: Spezifischer Reason-Code fuer GC/Wii-Formate ohne Tool ---
  if (-not $console -and -not $reasonCode) {
    if ($ext -in @('.gcz', '.rvz', '.wia', '.wbf1') -or $normExt -in @('.nkit.iso', '.nkit.gcz')) {
      $reasonCode = 'NEEDS_DOLPHIN_TOOL'
    }
  }

  # --- Stufe 5: Get-ConsoleType Heuristik (Folder, Header, Extension, Filename) ---
  if (-not $console) {
    $tried.Add('HEURISTIC')
    # BUG-17: Clear IO error marker before probing
    $script:LAST_DISC_HEADER_ERROR = $null
    $heuristicConsole = Get-ConsoleType -RootPath $RootPath -FilePath $FilePath -Extension $Extension
    if ($heuristicConsole -and $heuristicConsole -ne 'UNKNOWN') {
      $console = $heuristicConsole
      # REF-CS-01/02: Source-Info kommt jetzt immer aus LAST_CONSOLE_TYPE_SOURCE
      # (Cache-Hits, ScanIndex-Hits und frische Detections setzen Source zuverlaessig)
      $heuristicSource = $script:LAST_CONSOLE_TYPE_SOURCE
      if ($heuristicSource) {
        $confidence = $heuristicSource.Confidence
        $reason = $heuristicSource.Reason
        $evidence = $heuristicSource.Evidence
        $source = $heuristicSource.Source
      } else {
        $confidence = 50
        $reason = 'Heuristik'
        $source = 'HEURISTIC'
      }
      $diagInfo['HEURISTIC'] = 'MATCH ({0})' -f $source
    } else {
      # BUG-17: Wenn Disc-Header IO-Fehler aufgetreten, spezifischen Reason-Code setzen
      if ($script:LAST_DISC_HEADER_ERROR -and
          $script:LAST_DISC_HEADER_ERROR.Path -eq $FilePath) {
        $reasonCode = 'DISC_HEADER_IO_ERROR'
        $diagInfo['DISC_HEADER_IO'] = $script:LAST_DISC_HEADER_ERROR.Error
      }
      if (-not $reasonCode) { $reasonCode = 'HEURISTIC_NO_MATCH' }
      $diagInfo['HEURISTIC'] = 'NO_MATCH'
    }
  }

  # --- UNKNOWN: Zusammenfassung ---
  if (-not $console) {
    $console = 'UNKNOWN'
    if (-not $reasonCode) { $reasonCode = 'HEURISTIC_NO_MATCH' }
    $reason = Get-ConsoleUnknownReasonLabel -Code $reasonCode
    $diagInfo['RESULT'] = 'UNKNOWN'
    $diagInfo['REASON_CODE'] = $reasonCode

    # REF-12: Vollstaendige Diagnose aller geprueften Stufen
    $diagParts = [System.Collections.Generic.List[string]]::new()
    foreach ($stage in @($tried)) {
      $stageResult = if ($diagInfo.ContainsKey($stage)) { [string]$diagInfo[$stage] } else { '?' }
      $diagParts.Add($stage + '(' + $stageResult + ')')
    }
    $diagInfo['DIAGNOSIS'] = 'Geprueft: ' + ($diagParts -join ', ')
    $diagInfo['FILE_EXT'] = $ext
    $diagInfo['FILE_NAME'] = [IO.Path]::GetFileName($FilePath)
  }

  return New-DetectionResult `
    -Console $console `
    -Confidence $confidence `
    -Reason $reason `
    -Evidence $evidence `
    -Source $source `
    -Tried @($tried) `
    -DiagInfo $diagInfo
}

function Get-ConsoleTypeSource {
  <# DEPRECATED (REF-CS-02): Get-ConsoleType setzt LAST_CONSOLE_TYPE_SOURCE jetzt zuverlaessig
     auf allen Code-Pfaden (inkl. Cache-Hits und ScanIndex). Invoke-ConsoleDetection nutzt
     ausschliesslich LAST_CONSOLE_TYPE_SOURCE. Diese Funktion bleibt nur fuer Compat/Tests. #>
  param(
    [string]$RootPath,
    [string]$FilePath,
    [string]$Extension,
    [string]$Result
  )

  $ext = if ($Extension) { $Extension.ToLowerInvariant() } else { '' }

  # Check folder map
  $rootNorm = if ($RootPath) { Get-NormalizedRootPathCached -RootPath $RootPath } else { '' }
  if (-not [string]::IsNullOrWhiteSpace($FilePath) -and -not [string]::IsNullOrWhiteSpace($rootNorm)) {
    $relativePath = Get-RelativePathSafe -Path $FilePath -Root $rootNorm
    if ($null -ne $relativePath) {
      $relDir = Split-Path -Parent $relativePath
      if ($relDir) {
        $parts = @($relDir.Replace('/', '\').Split('\\') | Where-Object { $_ -ne '' })
        foreach ($part in $parts) {
          $key = $part.Trim().ToLowerInvariant()
          if ($script:CONSOLE_FOLDER_MAP.ContainsKey($key) -and $script:CONSOLE_FOLDER_MAP[$key] -eq $Result) {
            return @{ Confidence = 50; Reason = 'Folder-Map Match'; Evidence = $key; Source = 'FOLDER' }
          }
        }
        foreach ($part in $parts) {
          $p = $part.Trim().ToLowerInvariant()
          foreach ($entry in $script:CONSOLE_FOLDER_RX_MAP) {
            if ($entry.Key -eq $Result -and $entry.RxObj -and $entry.RxObj.IsMatch($p)) {
              return @{ Confidence = 50; Reason = 'Folder-Regex Match'; Evidence = $p; Source = 'FOLDER' }
            }
          }
        }
      }
    }
  }

  # Check disc header
  if ($ext -in @('.iso', '.gcm', '.img', '.bin', '.chd', '.pbp')) {
    return @{ Confidence = 95; Reason = 'Disc-Header Match'; Evidence = $ext; Source = 'DISC_HEADER' }
  }

  # Check unique ext map
  if ($script:CONSOLE_EXT_MAP_UNIQUE.ContainsKey($ext) -and $script:CONSOLE_EXT_MAP_UNIQUE[$ext] -eq $Result) {
    return @{ Confidence = 60; Reason = 'Extension-Map Match (UNIQUE)'; Evidence = $ext; Source = 'EXT_MAP' }
  }

  # Check ambig ext map
  if ($script:CONSOLE_EXT_MAP_AMBIG.ContainsKey($ext) -and $script:CONSOLE_EXT_MAP_AMBIG[$ext] -eq $Result) {
    return @{ Confidence = 40; Reason = 'Extension-Map Match (AMBIG)'; Evidence = $ext; Source = 'EXT_MAP_AMBIG' }
  }

  # Check filename regex
  if ($FilePath) {
    $name = [IO.Path]::GetFileNameWithoutExtension($FilePath)
    if ($name) {
      $name = $name.ToLowerInvariant()
      foreach ($entry in $script:CONSOLE_NAME_RX_MAP) {
        if ($entry.Key -eq $Result -and $entry.RxObj -and $entry.RxObj.IsMatch($name)) {
          return @{ Confidence = 30; Reason = 'Filename-Regex Match'; Evidence = $name; Source = 'FILENAME_RX' }
        }
      }
    }
  }

  # DOS fallback or unknown source
  if ($Result -eq 'DOS') {
    return @{ Confidence = 50; Reason = 'DOS-Folder-Fallback'; Evidence = $FilePath; Source = 'FOLDER' }
  }

  return @{ Confidence = 50; Reason = 'Heuristik'; Evidence = $ext; Source = 'HEURISTIC' }
}

function Get-ConsoleUnknownReasonLabel {
  <# Translates internal reason codes to human-readable labels (German). #>
  param([string]$Code)

  switch ($Code) {
    'ARCHIVE_AMBIGUOUS_EXT'       { return 'Archive: keine eindeutigen Extensions' }
    'ARCHIVE_TOOL_MISSING'        { return 'Archive: 7z nicht verfuegbar' }
    'ARCHIVE_DISC_HEADER_MISSING' { return 'Archive: kein Disc-Header gefunden' }
    'DOLPHIN_DISC_ID_MISSING'     { return 'DolphinTool: keine Disc-ID' }
    'HEURISTIC_NO_MATCH'          { return 'Heuristik: kein Match' }
    'NEEDS_DOLPHIN_TOOL'          { return 'GC/Wii-Format: DolphinTool oder Disc-ID im Dateinamen benoetigt' }
    'DISC_HEADER_IO_ERROR'        { return 'Disc-Header: Datei nicht lesbar (IO)' }
    'EXT_NOT_SCANNED'             { return 'Extension nicht im Scan-Set' }
    'FILE_TOO_SMALL'              { return 'Datei zu klein fuer Disc-Header-Analyse' }
    'ARCHIVE_EMPTY'               { return 'Archive: keine Eintraege gefunden' }
    'ARCHIVE_MIXED_CONTENT'       { return 'Archive: gemischte Plattform-Extensions' }
    'NO_FOLDER_MATCH'             { return 'Kein Ordner-Match gefunden' }
    'DAT_NO_MATCH'                { return 'DAT: kein Hash-Treffer' }
    'FILENAME_NO_DISC_ID'         { return 'Dateiname: keine Disc-ID erkannt' }
    default {
      if ([string]::IsNullOrWhiteSpace($Code)) { return 'Unbekannt' }
      return $Code
    }
  }
}

function Get-ConsoleType {
  <#
    Erkennt die Konsole aus:
    1. Ordnername im Pfad (Root oder Unterordner)
    2. Dateiendung
    3. Fallback: UNKNOWN
  #>
  param(
    [string]$RootPath,
    [string]$FilePath,
    [string]$Extension
  )

  $ext = if ($Extension) { $Extension.ToLowerInvariant() } else { '' }
  $folderCache = $script:CONSOLE_FOLDER_TYPE_CACHE
  $cache = $script:CONSOLE_TYPE_CACHE

  # PERF-03: Einzelner Cache-Key aus den rohen Inputs (RootPath|FilePath|ext).
  # Diese Kombination ist bereits eindeutig pro Datei — die fruehere zweite
  # Normalisierung (Resolve-RootPath + GetFileNameWithoutExtension + relDir)
  # war redundant und erzeugte unnoetige String-Allokationen.
  $cacheKey = $null
  if ($cache) {
    $cacheKey = '{0}|{1}|{2}' -f ($RootPath, $FilePath, $ext)
    if ($cache.ContainsKey($cacheKey)) {
      # REF-CS-01: Source-Info aus parallelem Cache restaurieren
      if ($script:CONSOLE_TYPE_SOURCE_CACHE.ContainsKey($cacheKey)) {
        $script:LAST_CONSOLE_TYPE_SOURCE = $script:CONSOLE_TYPE_SOURCE_CACHE[$cacheKey]
      }
      return $cache[$cacheKey]
    }
  }

  $rootNorm = if ($RootPath) { Get-NormalizedRootPathCached -RootPath $RootPath } else { '' }
  $relativeDirNorm = ''
  if (-not [string]::IsNullOrWhiteSpace($FilePath) -and -not [string]::IsNullOrWhiteSpace($rootNorm)) {
    $relativePathPre = Get-RelativePathSafe -Path $FilePath -Root $rootNorm
    if ($null -ne $relativePathPre) {
      $relDirPre = Split-Path -Parent $relativePathPre
      if ($relDirPre) { $relativeDirNorm = $relDirPre.Replace('/', '\').Trim('\').ToLowerInvariant() }
    }
  }

  $folderCacheKey = ('{0}|{1}' -f $rootNorm, $relativeDirNorm)
  if ($folderCache -and $folderCache.ContainsKey($folderCacheKey)) {
    # REF-CS-01: Folder-Cache-Hits sind immer Source=FOLDER
    $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 50; Reason = 'Folder-Map Match (cached)'; Evidence = $folderCacheKey; Source = 'FOLDER' }
    return [string]$folderCache[$folderCacheKey]
  }

  # Scan-Index: persisted classification from previous runs
  $scanIndexKey = $null
  if ($cacheKey -and (Get-Command Get-ScanIndexCache -ErrorAction SilentlyContinue)) {
    $scanIndexKey = 'CONSOLE|{0}' -f $cacheKey
    try {
      $scanIndex = Get-ScanIndexCache
      if ($scanIndex.ContainsKey($scanIndexKey)) {
        $entry = $scanIndex[$scanIndexKey]
        if ($entry -and ($entry.PSObject.Properties.Name -contains 'Console') -and
            -not [string]::IsNullOrWhiteSpace([string]$entry.Console)) {
          $scanResult = [string]$entry.Console
          if ($cache) {
            if ($cache.Count -ge $script:CONSOLE_TYPE_CACHE_MAX) { $cache.Clear(); $script:CONSOLE_TYPE_SOURCE_CACHE.Clear() }
            $cache[$cacheKey] = $scanResult
          }
          # REF-CS-01: Source-Info aus ScanIndex rekonstruieren
          $scanSource = if ($entry.PSObject.Properties.Name -contains 'Source') { [string]$entry.Source } else { '' }
          $scanConf = switch ($scanSource) { 'DISC_HEADER' {95}; 'FOLDER' {50}; 'EXT_MAP' {60}; 'EXT_MAP_AMBIG' {40}; 'FILENAME_RX' {30}; default {50} }
          $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = $scanConf; Reason = "ScanIndex ($scanSource)"; Evidence = ''; Source = $scanSource }
          if ($cacheKey) { $script:CONSOLE_TYPE_SOURCE_CACHE[$cacheKey] = $script:LAST_CONSOLE_TYPE_SOURCE }
          # BUG-CS-08: Folder-Cache nur bei Folder-Detection befuellen (auch via ScanIndex)
          if ($folderCache -and -not [string]::IsNullOrWhiteSpace($folderCacheKey) -and $scanResult -ne 'UNKNOWN') {
            if ($scanSource -eq 'FOLDER') {
              $folderCache[$folderCacheKey] = $scanResult
            }
          }
          return $scanResult
        }
      }
    } catch { }
  }

  $result = $null
  # BUG-CS-04: Track actual detection source directly
  $script:LAST_CONSOLE_TYPE_SOURCE = $null

  # 1. Ordnernamen durchsuchen (NUR relative Pfad-Teile innerhalb Root)
  # Root-Pfad-Komponenten werden NICHT geprueft, weil z.B. Root='D:\dc\games'
  # sonst jede Datei als DC klassifizieren wuerde.
  $parts = @()
  if (-not [string]::IsNullOrWhiteSpace($FilePath) -and -not [string]::IsNullOrWhiteSpace($rootNorm)) {
    $relativePath = Get-RelativePathSafe -Path $FilePath -Root $rootNorm
    if ($null -ne $relativePath) {
      $relDir = Split-Path -Parent $relativePath
      if ($relDir) {
        $parts = @($relDir.Replace('/', '\').Split('\\') | Where-Object { $_ -ne '' })
      }
    }
  }

  # REF-DET-01: Disc-Header-Probe VOR Folder-Map ausfuehren.
  # Binaere Evidenz (Header) ist staerker als Ordnername.
  # 1. Disc header probe for ISO/GCM/IMG/BIN (GC, Wii, DC, SAT, SCD, 3DO, PS1, PS2, PSP)
  # PERF-01: Nur proben wenn Datei >= 1 MB (kleine .bin sind meist CUE-Tracks, kein Disc-Image)
  if ($FilePath -and ($ext -in @('.iso', '.gcm', '.img', '.bin'))) {
    $probeFile = $true
    if (-not $script:ISO_HEADER_CACHE.ContainsKey($FilePath)) {
      try {
        $fi = [System.IO.FileInfo]::new($FilePath)
        if ($fi.Exists -and $fi.Length -lt 1048576) { $probeFile = $false }
      } catch { }
    }
    if ($probeFile) {
      $headerConsole = Get-DiscHeaderConsole -Path $FilePath
      if ($headerConsole) {
        $result = $headerConsole
        $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 95; Reason = 'Disc-Header Match'; Evidence = $ext; Source = 'DISC_HEADER' }
      }
    }
  }

  # 1b. CHD disc header probe - scan metadata for platform keywords
  if (-not $result -and $FilePath -and $ext -eq '.chd') {
    $chdConsole = Get-ChdDiscHeaderConsole -Path $FilePath
    if ($chdConsole) {
      $result = $chdConsole
      $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 95; Reason = 'CHD-Header Match'; Evidence = $ext; Source = 'DISC_HEADER' }
    }
  }

  # 1c. PBP header probe - distinguish PS1-Classic from PSP-Native
  if (-not $result -and $FilePath -and $ext -eq '.pbp') {
    $pbpConsole = Get-PbpConsole -Path $FilePath
    if ($pbpConsole) {
      $result = $pbpConsole
      $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 90; Reason = 'PBP-Header Match'; Evidence = $ext; Source = 'DISC_HEADER' }
    }
  }

  # 2. Ordnernamen durchsuchen (NUR relative Pfad-Teile innerhalb Root)
  if (-not $result) {
    foreach ($part in $parts) {
      $key = $part.Trim().ToLowerInvariant()
      if ($script:CONSOLE_FOLDER_MAP.ContainsKey($key)) {
        $result = $script:CONSOLE_FOLDER_MAP[$key]
        $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 50; Reason = 'Folder-Map Match'; Evidence = $key; Source = 'FOLDER' }
        break
      }
    }
  }

  if (-not $result) {
    foreach ($part in $parts) {
      $p = $part.Trim().ToLowerInvariant()
      foreach ($entry in $script:CONSOLE_FOLDER_RX_MAP) {
        $rxObj = $entry.RxObj
        if ($rxObj -and $rxObj.IsMatch($p)) {
          $result = $entry.Key
          $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 50; Reason = 'Folder-Regex Match'; Evidence = $p; Source = 'FOLDER' }
          break
        }
      }
      if ($result) { break }
    }
  }

  # 3. Dateiendung — eindeutige Extensions (.gba, .nes, .nds etc.)
  if (-not $result -and $script:CONSOLE_EXT_MAP_UNIQUE.ContainsKey($ext)) {
    $candidate = $script:CONSOLE_EXT_MAP_UNIQUE[$ext]
    # DATA-03: .md Collision Guard – skip if file looks like Markdown text
    if ($ext -eq '.md' -and $FilePath -and (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
      try {
        $fileSize = (Get-Item -LiteralPath $FilePath -ErrorAction SilentlyContinue).Length
        if ($fileSize -lt 524288) {  # < 512 KB → likely text, not ROM
          $head = [System.IO.File]::ReadAllBytes($FilePath) | Select-Object -First 256
          $isText = $true
          foreach ($b in $head) { if ($b -eq 0) { $isText = $false; break } }
          if ($isText) { $candidate = $null }  # skip – likely Markdown
        }
      } catch { }
    }
    if ($candidate) {
      $result = $candidate
      $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 60; Reason = 'Extension-Map Match (UNIQUE)'; Evidence = $ext; Source = 'EXT_MAP' }
    }
  }

  # 4. Dateiname nach Konsolen-Keywords durchsuchen (niedrigste Heuristik-Prioritaet,
  # da kurze Patterns wie \bdc\b oder \bgg\b zu False-Positives fuehren koennen)
  if (-not $result -and $FilePath) {
    $name = [IO.Path]::GetFileNameWithoutExtension($FilePath)
    if ($name) {
      $name = $name.ToLowerInvariant()
      foreach ($entry in $script:CONSOLE_NAME_RX_MAP) {
        $rxObj = $entry.RxObj
        if ($rxObj -and $rxObj.IsMatch($name)) {
          $result = $entry.Key
          $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 30; Reason = 'Filename-Regex Match'; Evidence = $name; Source = 'FILENAME_RX' }
          break
        }
      }
    }
  }

  # 5. Dateiendung — ambige Extensions als Fallback (.cso etc.)
  if (-not $result -and $script:CONSOLE_EXT_MAP_AMBIG.ContainsKey($ext)) {
    $result = $script:CONSOLE_EXT_MAP_AMBIG[$ext]
    $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 40; Reason = 'Extension-Map Match (AMBIG)'; Evidence = $ext; Source = 'EXT_MAP_AMBIG' }
  }

  # 6. Ordner-basierter DOS-Fallback:
  # Wenn ein Verzeichnis direkt Launcher-Dateien enthält, als DOS behandeln.
  if (-not $result -and [string]::IsNullOrWhiteSpace($ext) -and $FilePath) {
    try {
      if (Test-Path -LiteralPath $FilePath -PathType Container) {
        $launcher = @(Get-ChildItem -LiteralPath $FilePath -File -ErrorAction SilentlyContinue |
          Where-Object { $_.Extension -in @('.exe', '.com', '.bat') } |
          Select-Object -First 1)
        if ($launcher.Count -gt 0) {
          $result = 'DOS'
          $script:LAST_CONSOLE_TYPE_SOURCE = @{ Confidence = 50; Reason = 'DOS-Folder-Fallback'; Evidence = $FilePath; Source = 'FOLDER' }
        }
      }
    } catch { }
  }

  if (-not $result) { $result = 'UNKNOWN' }
  # BUG-CS-08: Folder-Cache nur bei Folder-Match befuellen, nicht bei Extension/Header/Filename.
  # Sonst propagiert z.B. .gba->GBA auf alle Dateien im selben Ordner.
  if ($folderCache -and -not [string]::IsNullOrWhiteSpace($folderCacheKey) -and $result -ne 'UNKNOWN') {
    $src = $script:LAST_CONSOLE_TYPE_SOURCE
    if ($src -and $src.Source -eq 'FOLDER') {
      $folderCache[$folderCacheKey] = $result
    }
  }
  if ($cache -and $cacheKey) {
    if ($cache.Count -ge $script:CONSOLE_TYPE_CACHE_MAX) { $cache.Clear(); $script:CONSOLE_TYPE_SOURCE_CACHE.Clear() }
    $cache[$cacheKey] = $result
    # REF-CS-01: Source-Info parallel cachen
    if ($script:LAST_CONSOLE_TYPE_SOURCE) {
      $script:CONSOLE_TYPE_SOURCE_CACHE[$cacheKey] = $script:LAST_CONSOLE_TYPE_SOURCE
    }
  }

  # Persist non-UNKNOWN classification to scan-index
  if ($result -ne 'UNKNOWN' -and $scanIndexKey) {
    try {
      $si = Get-ScanIndexCache
      $si[$scanIndexKey] = [pscustomobject]@{
        Console      = [string]$result
        Source       = if ($script:LAST_CONSOLE_TYPE_SOURCE) { [string]$script:LAST_CONSOLE_TYPE_SOURCE.Source } else { '' }
        UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
      }
      $script:SCAN_INDEX_DIRTY = $true
      if ($null -ne $script:SCAN_INDEX_WRITE_COUNTER) {
        $script:SCAN_INDEX_WRITE_COUNTER++
        # PERF-CS-05: Threshold auf 1000 erhoeht (vorher 200) — weniger Disk-IO
        if ($script:SCAN_INDEX_WRITE_COUNTER % 1000 -eq 0) { Save-ScanIndexCacheIfNeeded }
      }
    } catch { }
  }

  return $result
}
