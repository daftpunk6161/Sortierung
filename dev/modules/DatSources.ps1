# =============================================================================
#  DatSources.ps1  –  DAT-Katalog, Download & Versionierung
# =============================================================================
#  Manages a catalog of DAT file sources (Redump, No-Intro, FBNEO etc.),
#  downloads/updates them, and tracks local inventory with version metadata.
# =============================================================================

# --- Paths -------------------------------------------------------------------

$script:DAT_INVENTORY_PATH = Join-Path $env:APPDATA 'RomCleanupRegionDedupe\dat-inventory.json'

# --- No-Intro Love Pack (archive.org) ----------------------------------------
$script:NOINTRO_PACK_METADATA_URL = 'https://archive.org/metadata/no-intro-priv-dat'
$script:NOINTRO_PACK_CACHE_DIR     = Join-Path $env:TEMP 'nointro-dat-cache'

function Get-DatSourcePluginCatalog {
  <# Load DAT source plugin entries from plugins/dat-sources/*.json. #>
  $plugins = New-Object System.Collections.Generic.List[object]
  $root = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'plugins\dat-sources'
  if (-not (Test-Path -LiteralPath $root -PathType Container)) { return @() }

  $files = @(Get-ChildItem -LiteralPath $root -Filter '*.json' -File -ErrorAction SilentlyContinue)
  foreach ($file in $files) {
    try {
      $payload = Copy-ObjectDeep -Value ((Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop) | ConvertFrom-Json -ErrorAction Stop)
      if (-not $payload) { continue }
      if ($payload -is [System.Collections.IDictionary] -and $payload.Contains('entries') -and $payload.entries) {
        foreach ($entry in @($payload.entries)) {
          [void]$plugins.Add($entry)
        }
      } elseif ($payload -is [System.Array]) {
        foreach ($entry in $payload) { [void]$plugins.Add($entry) }
      }
    } catch {
      continue
    }
  }

  return @($plugins)
}

function Test-DownloadedDatSignature {
  <# Optional SHA256 verification using URL.sha256 sidecar. Returns $true if verified or unavailable. #>
  param(
    [string]$SourceUrl,
    [string]$LocalPath,
    [string]$ExpectedSha256,
    [string]$ExpectedSha1,
    [scriptblock]$Log
  )

  if ([string]::IsNullOrWhiteSpace($SourceUrl) -or [string]::IsNullOrWhiteSpace($LocalPath)) { return $true }
  if (-not (Test-Path -LiteralPath $LocalPath -PathType Leaf)) { return $false }

  $expectedSha256Trimmed = [string]$ExpectedSha256
  if (-not [string]::IsNullOrWhiteSpace($expectedSha256Trimmed)) {
    $expectedSha256Trimmed = $expectedSha256Trimmed.Trim().ToLowerInvariant()
    $actualSha256 = [string](Get-FileHash -LiteralPath $LocalPath -Algorithm SHA256).Hash
    if ($actualSha256.ToLowerInvariant() -ne $expectedSha256Trimmed) {
      if ($Log) { & $Log ('DAT-Signatur SHA256 ungueltig: {0}' -f $LocalPath) }
      return $false
    }
    return $true
  }

  $expectedSha1Trimmed = [string]$ExpectedSha1
  if (-not [string]::IsNullOrWhiteSpace($expectedSha1Trimmed)) {
    $expectedSha1Trimmed = $expectedSha1Trimmed.Trim().ToLowerInvariant()
    $actualSha1 = [string](Get-FileHash -LiteralPath $LocalPath -Algorithm SHA1).Hash
    if ($actualSha1.ToLowerInvariant() -ne $expectedSha1Trimmed) {
      if ($Log) { & $Log ('DAT-Signatur SHA1 ungueltig: {0}' -f $LocalPath) }
      return $false
    }
    return $true
  }

  $shaUrl = ('{0}.sha256' -f $SourceUrl)
  try {
    $shaText = (Invoke-WebRequest -Uri $shaUrl -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop).Content
    # BUG DATSRC-002 FIX: Fail-closed — missing or unparseable sidecar = verification failure
    if ([string]::IsNullOrWhiteSpace([string]$shaText)) {
      if ($Log) { & $Log ('DAT-Signatur: sha256-Sidecar leer fuer {0}' -f $SourceUrl) }
      return $false
    }

    $match = [regex]::Match([string]$shaText, '(?i)\b([a-f0-9]{64})\b')
    if (-not $match.Success) {
      if ($Log) { & $Log ('DAT-Signatur: sha256-Sidecar kein gueltiger Hash fuer {0}' -f $SourceUrl) }
      return $false
    }

    $expected = [string]$match.Groups[1].Value.ToLowerInvariant()
    $actual = [string](Get-FileHash -LiteralPath $LocalPath -Algorithm SHA256).Hash
    $actual = $actual.ToLowerInvariant()
    if ($expected -ne $actual) {
      if ($Log) { & $Log ('DAT-Signatur SHA256 ungueltig: {0}' -f $LocalPath) }
      return $false
    }
    return $true
  } catch {
    # BUG DATSRC-002 FIX: Fail-closed — network error = verification failure
    if ($Log) { & $Log ('DAT-Signatur: sha256-Sidecar Download fehlgeschlagen fuer {0}: {1}' -f $SourceUrl, $_.Exception.Message) }
    return $false
  }
}

# --- Built-in Catalog --------------------------------------------------------
# Each entry: id, group, system (display name), url (download), format (zip-dat
# or raw-dat), consoleKey (matches CONSOLE_FOLDER_MAP in Dat.ps1).
# Users can add/edit entries; the catalog is merged with persisted inventory.

function Get-BuiltinDatCatalog {
  <#
  .SYNOPSIS Returns the built-in catalog of known DAT sources.
  #>
  if (Get-Command Import-RomCleanupJsonData -ErrorAction SilentlyContinue) {
    $jsonCatalog = Import-RomCleanupJsonData -FileName 'dat-catalog.json'
    if ($jsonCatalog) {
      $baseEntries = @($jsonCatalog)
      $pluginEntries = @()
      if (Get-Command Get-DatSourcePluginCatalog -ErrorAction SilentlyContinue) {
        $pluginEntries = @(Get-DatSourcePluginCatalog)
      }
      if (@($pluginEntries).Count -gt 0) {
        return @($baseEntries + $pluginEntries)
      }
      return @($baseEntries)
    }
  }

  return @(
    # ----- Redump (public .zip downloads → each contains one .dat) -----------
    @{ Id='redump-3do';        Group='Redump'; System='3DO Interactive Multiplayer';              Url='https://redump.org/datfile/3do/';        Format='zip-dat'; ConsoleKey='3DO'   }
    @{ Id='redump-acd';        Group='Redump'; System='Acorn - Archimedes CD';                   Url='https://redump.org/datfile/acd/';        Format='zip-dat'; ConsoleKey='ACD'   }
    @{ Id='redump-cd32';       Group='Redump'; System='Commodore - Amiga CD32';                  Url='https://redump.org/datfile/cd32/';       Format='zip-dat'; ConsoleKey='CD32'  }
    @{ Id='redump-cdtv';       Group='Redump'; System='Commodore - Amiga CDTV';                  Url='https://redump.org/datfile/cdtv/';       Format='zip-dat'; ConsoleKey='CDTV'  }
    @{ Id='redump-cdi';        Group='Redump'; System='Philips - CD-i';                          Url='https://redump.org/datfile/cdi/';        Format='zip-dat'; ConsoleKey='CDI'   }
    @{ Id='redump-chihiro';    Group='Redump'; System='Sega - Chihiro';                          Url='https://redump.org/datfile/chihiro/';    Format='zip-dat'; ConsoleKey='CHI'   }
    @{ Id='redump-dc';         Group='Redump'; System='Sega - Dreamcast';                        Url='https://redump.org/datfile/dc/';         Format='zip-dat'; ConsoleKey='DC'    }
    @{ Id='redump-fmt';        Group='Redump'; System='Fujitsu - FM Towns';                      Url='https://redump.org/datfile/fmt/';        Format='zip-dat'; ConsoleKey='FMT'   }
    @{ Id='redump-gc';         Group='Redump'; System='Nintendo - GameCube';                     Url='https://redump.org/datfile/gc/';         Format='zip-dat'; ConsoleKey='GC'    }
    @{ Id='redump-hs';         Group='Redump'; System='Matsushita - Video Now Color';            Url='https://redump.org/datfile/hs/';         Format='zip-dat'; ConsoleKey='HS'    }
    @{ Id='redump-ksite';      Group='Redump'; System='Konami - e-Amusement';                    Url='https://redump.org/datfile/ksite/';      Format='zip-dat'; ConsoleKey='KSITE' }
    @{ Id='redump-lindbergh';  Group='Redump'; System='Sega - Lindbergh';                        Url='https://redump.org/datfile/lindbergh/';  Format='zip-dat'; ConsoleKey='LIND'  }
    @{ Id='redump-mac';        Group='Redump'; System='Apple - Macintosh';                       Url='https://redump.org/datfile/mac/';        Format='zip-dat'; ConsoleKey='MAC'   }
    @{ Id='redump-mcd';        Group='Redump'; System='Sega - Mega CD & Sega CD';                Url='https://redump.org/datfile/mcd/';        Format='zip-dat'; ConsoleKey='MCD'   }
    @{ Id='redump-naomi';      Group='Redump'; System='Sega - Naomi';                            Url='https://redump.org/datfile/naomi/';      Format='zip-dat'; ConsoleKey='NAOMI' }
    @{ Id='redump-naomi2';     Group='Redump'; System='Sega - Naomi 2';                          Url='https://redump.org/datfile/naomi2/';     Format='zip-dat'; ConsoleKey='NAO2'  }
    @{ Id='redump-ngcd';       Group='Redump'; System='SNK - Neo Geo CD';                        Url='https://redump.org/datfile/ngcd/';       Format='zip-dat'; ConsoleKey='NEOCD' }
    @{ Id='redump-nuon';       Group='Redump'; System='VM Labs - NUON';                          Url='https://redump.org/datfile/nuon/';       Format='zip-dat'; ConsoleKey='NUON'  }
    @{ Id='redump-palm';       Group='Redump'; System='Palm';                                    Url='https://redump.org/datfile/palm/';       Format='zip-dat'; ConsoleKey='PALM'  }
    @{ Id='redump-pc';         Group='Redump'; System='IBM - PC compatible';                     Url='https://redump.org/datfile/pc/';         Format='zip-dat'; ConsoleKey='IBMPC' }
    @{ Id='redump-pc88';       Group='Redump'; System='NEC - PC-88 series';                      Url='https://redump.org/datfile/pc-88/';      Format='zip-dat'; ConsoleKey='PC88'  }
    @{ Id='redump-pc98';       Group='Redump'; System='NEC - PC-98 series';                      Url='https://redump.org/datfile/pc-98/';      Format='zip-dat'; ConsoleKey='PC98'  }
    @{ Id='redump-pce-cd';     Group='Redump'; System='NEC - PC Engine CD & TurboGrafx CD';      Url='https://redump.org/datfile/pce/';        Format='zip-dat'; ConsoleKey='PCECD' }
    @{ Id='redump-pcfx';       Group='Redump'; System='NEC - PC-FX & PC-FXGA';                   Url='https://redump.org/datfile/pc-fx/';      Format='zip-dat'; ConsoleKey='PCFX'  }
    @{ Id='redump-photo-cd';   Group='Redump'; System='Photo CD';                                Url='https://redump.org/datfile/photo-cd/';   Format='zip-dat'; ConsoleKey='PCD'   }
    @{ Id='redump-pippin';     Group='Redump'; System='Apple - Bandai Pippin';                   Url='https://redump.org/datfile/pippin/';     Format='zip-dat'; ConsoleKey='PIPPIN'}
    @{ Id='redump-ps1';        Group='Redump'; System='Sony - PlayStation';                      Url='https://redump.org/datfile/psx/';        Format='zip-dat'; ConsoleKey='PSX'   }
    @{ Id='redump-ps2';        Group='Redump'; System='Sony - PlayStation 2';                    Url='https://redump.org/datfile/ps2/';        Format='zip-dat'; ConsoleKey='PS2'   }
    @{ Id='redump-ps3';        Group='Redump'; System='Sony - PlayStation 3';                    Url='https://redump.org/datfile/ps3/';        Format='zip-dat'; ConsoleKey='PS3'   }
    @{ Id='redump-psp';        Group='Redump'; System='Sony - PlayStation Portable';             Url='https://redump.org/datfile/psp/';        Format='zip-dat'; ConsoleKey='PSP'   }
    @{ Id='redump-ss';         Group='Redump'; System='Sega - Saturn';                           Url='https://redump.org/datfile/ss/';         Format='zip-dat'; ConsoleKey='SAT'   }
    @{ Id='redump-vflash';     Group='Redump'; System='VTech - V.Flash & V.Smile Pro';           Url='https://redump.org/datfile/vflash/';     Format='zip-dat'; ConsoleKey='VFLASH'}
    @{ Id='redump-wii';        Group='Redump'; System='Nintendo - Wii';                          Url='https://redump.org/datfile/wii/';        Format='zip-dat'; ConsoleKey='WII'   }
    @{ Id='redump-xbox';       Group='Redump'; System='Microsoft - Xbox';                        Url='https://redump.org/datfile/xbox/';       Format='zip-dat'; ConsoleKey='XBOX'  }
    @{ Id='redump-xbox360';    Group='Redump'; System='Microsoft - Xbox 360';                    Url='https://redump.org/datfile/xbox360/';    Format='zip-dat'; ConsoleKey='X360'  }
    @{ Id='redump-ajcd';       Group='Redump'; System='Atari - Jaguar CD';                        Url='https://redump.org/datfile/ajcd/';       Format='zip-dat'; ConsoleKey='AJCD'  }
    @{ Id='redump-gamewave';   Group='Redump'; System='ZAPiT - Game Wave';                        Url='https://redump.org/datfile/gamewave/';   Format='zip-dat'; ConsoleKey='GW'    }
    @{ Id='redump-ite';        Group='Redump'; System='Incredible Technologies - Eagle';           Url='https://redump.org/datfile/ite/';        Format='zip-dat'; ConsoleKey='ITE'   }
    @{ Id='redump-ixl';        Group='Redump'; System='Mattel - Fisher-Price iXL';                Url='https://redump.org/datfile/ixl/';        Format='zip-dat'; ConsoleKey='IXL'   }
    @{ Id='redump-ppc';        Group='Redump'; System='Pocket PC';                                Url='https://redump.org/datfile/ppc/';        Format='zip-dat'; ConsoleKey='PPC'   }
    @{ Id='redump-quizard';    Group='Redump'; System='TAB-Austria - Quizard';                    Url='https://redump.org/datfile/quizard/';    Format='zip-dat'; ConsoleKey='QUIZ'  }
    @{ Id='redump-vis';        Group='Redump'; System='Memorex - Visual Information System';      Url='https://redump.org/datfile/vis/';        Format='zip-dat'; ConsoleKey='VIS'   }
    @{ Id='redump-x68k';       Group='Redump'; System='Sharp - X68000';                           Url='https://redump.org/datfile/x68k/';       Format='zip-dat'; ConsoleKey='X68K'  }

    # ----- No-Intro (Love Pack via archive.org) -------------------------------
    # Format 'nointro-pack': downloads the shared Love Pack zip once, then
    # extracts the matching DAT per system using the PackMatch wildcard.
    @{ Id='nointro-nes';       Group='No-Intro'; System='Nintendo - NES';                  Url=''; Format='nointro-pack'; ConsoleKey='NES';  PackMatch='Nintendo - Nintendo Entertainment System (Headered)*' }
    @{ Id='nointro-snes';      Group='No-Intro'; System='Nintendo - SNES';                 Url=''; Format='nointro-pack'; ConsoleKey='SNES'; PackMatch='Nintendo - Super Nintendo Entertainment System*' }
    @{ Id='nointro-n64';       Group='No-Intro'; System='Nintendo - N64';                  Url=''; Format='nointro-pack'; ConsoleKey='N64';  PackMatch='Nintendo - Nintendo 64 (BigEndian)*' }
    @{ Id='nointro-gba';       Group='No-Intro'; System='Nintendo - Game Boy Advance';     Url=''; Format='nointro-pack'; ConsoleKey='GBA';  PackMatch='Nintendo - Game Boy Advance (Private)*' }
    @{ Id='nointro-gb';        Group='No-Intro'; System='Nintendo - Game Boy';             Url=''; Format='nointro-pack'; ConsoleKey='GB';   PackMatch='Nintendo - Game Boy (Private)*' }
    @{ Id='nointro-gbc';       Group='No-Intro'; System='Nintendo - Game Boy Color';       Url=''; Format='nointro-pack'; ConsoleKey='GBC';  PackMatch='Nintendo - Game Boy Color*' }
    @{ Id='nointro-nds';       Group='No-Intro'; System='Nintendo - DS';                   Url=''; Format='nointro-pack'; ConsoleKey='NDS';  PackMatch='Nintendo - Nintendo DS (Decrypted)*' }
    @{ Id='nointro-md';        Group='No-Intro'; System='Sega - Mega Drive / Genesis';     Url=''; Format='nointro-pack'; ConsoleKey='MD';   PackMatch='Sega - Mega Drive - Genesis*' }
    @{ Id='nointro-sms';       Group='No-Intro'; System='Sega - Master System';            Url=''; Format='nointro-pack'; ConsoleKey='SMS';  PackMatch='Sega - Master System - Mark III*' }
    @{ Id='nointro-pce';       Group='No-Intro'; System='NEC - PC Engine / TurboGrafx-16'; Url=''; Format='nointro-pack'; ConsoleKey='PCE';  PackMatch='NEC - PC Engine - TurboGrafx-16*' }
    @{ Id='nointro-switch';    Group='No-Intro'; System='Nintendo - Switch';                Url=''; Format='nointro-pack'; ConsoleKey='NSW';  PackMatch='Nintendo - Nintendo Switch (20*' }
    @{ Id='nointro-vb';        Group='No-Intro'; System='Nintendo - Virtual Boy';           Url=''; Format='nointro-pack'; ConsoleKey='VB';   PackMatch='Nintendo - Virtual Boy*' }
    @{ Id='nointro-a2600';     Group='No-Intro'; System='Atari - 2600';                    Url=''; Format='nointro-pack'; ConsoleKey='A26';  PackMatch='Atari - Atari 2600*' }
    @{ Id='nointro-a7800';     Group='No-Intro'; System='Atari - 7800';                    Url=''; Format='nointro-pack'; ConsoleKey='A78';  PackMatch='Atari - Atari 7800 (A78)*' }
    @{ Id='nointro-lynx';      Group='No-Intro'; System='Atari - Lynx';                    Url=''; Format='nointro-pack'; ConsoleKey='LYNX'; PackMatch='Atari - Atari Lynx (LNX)*' }
    @{ Id='nointro-coleco';    Group='No-Intro'; System='Coleco - ColecoVision';            Url=''; Format='nointro-pack'; ConsoleKey='COLECO'; PackMatch='Coleco - ColecoVision*' }
    @{ Id='nointro-vectrex';   Group='No-Intro'; System='GCE - Vectrex';                   Url=''; Format='nointro-pack'; ConsoleKey='VEC';  PackMatch='GCE - Vectrex*' }
    @{ Id='nointro-intv';      Group='No-Intro'; System='Mattel - Intellivision';           Url=''; Format='nointro-pack'; ConsoleKey='INTV'; PackMatch='Mattel - Intellivision*' }
    @{ Id='nointro-jaguar';    Group='No-Intro'; System='Atari - Jaguar';                    Url=''; Format='nointro-pack'; ConsoleKey='JAG';    PackMatch='Atari - Atari Jaguar (J64)*' }
    @{ Id='nointro-supervision';Group='No-Intro'; System='Watara - Supervision';              Url=''; Format='nointro-pack'; ConsoleKey='SVISION';PackMatch='Watara - Supervision*' }
    @{ Id='nointro-pocket';    Group='No-Intro'; System='Analogue - Pocket';                 Url=''; Format='nointro-pack'; ConsoleKey='POCKET'; PackMatch='Analogue - Analogue Pocket*' }

    # ----- Non-Redump (extras from Love Pack) ---------------------------------
    @{ Id='nonredump-gc';      Group='Non-Redump'; System='Nintendo - GameCube (Non-Redump)';   Url=''; Format='nointro-pack'; ConsoleKey='GC';  PackMatch='Non-Redump - Nintendo - Nintendo GameCube*' }
    @{ Id='nonredump-dc';      Group='Non-Redump'; System='Sega - Dreamcast (Non-Redump)';     Url=''; Format='nointro-pack'; ConsoleKey='DC';  PackMatch='Non-Redump - Sega - Dreamcast*' }
    @{ Id='nonredump-ps5';     Group='Non-Redump'; System='Sony - PlayStation 5 (Non-Redump)';  Url=''; Format='nointro-pack'; ConsoleKey='PS5'; PackMatch='Non-Redump - Sony - PlayStation 5*' }

    # ----- MAME (progetto-SNAPS, 7z archive) ----------------------------------
    # URL contains version tag - update when new MAME releases arrive.
    @{ Id='mame';              Group='MAME';   System='MAME Full Set';                          Url='https://www.progettosnaps.net/dats/MAME/packs/MAME_Dats_284.7z'; Format='7z-dat'; ConsoleKey='MAME'  }

    # ----- Libretro / Arcade (GitHub-hosted DATs) -----------------------------
    @{ Id='libretro-atomiswave';Group='Libretro'; System='Sammy - Atomiswave';                  Url='https://raw.githubusercontent.com/libretro/libretro-database/master/dat/Atomiswave.dat'; Format='raw-dat'; ConsoleKey='AWAVE' }

    # ----- FBNEO (GitHub-hosted XML DATs) ------------------------------------
    @{ Id='fbneo-arcade';      Group='FBNEO'; System='FinalBurn Neo - Arcade Games';              Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20Arcade%20only).dat'; Format='raw-dat'; ConsoleKey='ARCADE' }
    @{ Id='fbneo-megadrive';   Group='FBNEO'; System='FinalBurn Neo - Megadrive Games';           Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20Megadrive%20only).dat'; Format='raw-dat'; ConsoleKey='MD'     }
    @{ Id='fbneo-pce';         Group='FBNEO'; System='FinalBurn Neo - PC Engine Games';           Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20PC-Engine%20only).dat'; Format='raw-dat'; ConsoleKey='PCE'    }
    @{ Id='fbneo-sms';         Group='FBNEO'; System='FinalBurn Neo - Master System Games';       Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20Master%20System%20only).dat'; Format='raw-dat'; ConsoleKey='SMS'   }
    @{ Id='fbneo-nes';         Group='FBNEO'; System='FinalBurn Neo - NES Games';                 Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20NES%20Games%20only).dat'; Format='raw-dat'; ConsoleKey='NES'    }
    @{ Id='fbneo-coleco';      Group='FBNEO'; System='FinalBurn Neo - ColecoVision Games';        Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20ColecoVision%20only).dat'; Format='raw-dat'; ConsoleKey='COLECO' }
    @{ Id='fbneo-gg';          Group='FBNEO'; System='FinalBurn Neo - Game Gear Games';           Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20Game%20Gear%20only).dat'; Format='raw-dat'; ConsoleKey='GG'     }
    @{ Id='fbneo-snes';        Group='FBNEO'; System='FinalBurn Neo - SNES Games';                Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20SNES%20Games%20only).dat'; Format='raw-dat'; ConsoleKey='SNES'   }
    @{ Id='fbneo-sg1000';      Group='FBNEO'; System='FinalBurn Neo - Sega SG-1000 Games';        Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20Sega%20SG-1000%20only).dat'; Format='raw-dat'; ConsoleKey='SG1K'  }
    @{ Id='fbneo-tg16';        Group='FBNEO'; System='FinalBurn Neo - TurboGrafx-16 Games';       Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20TurboGrafx16%20only).dat'; Format='raw-dat'; ConsoleKey='TG16'  }
    @{ Id='fbneo-neogeo';      Group='FBNEO'; System='FinalBurn Neo - Neo Geo Games';             Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20Neogeo%20only).dat'; Format='raw-dat'; ConsoleKey='NEO'    }
    @{ Id='fbneo-fds';         Group='FBNEO'; System='FinalBurn Neo - FDS Games';                 Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20FDS%20Games%20only).dat'; Format='raw-dat'; ConsoleKey='FDS'    }
    @{ Id='fbneo-spectrum';    Group='FBNEO'; System='FinalBurn Neo - ZX Spectrum Games';          Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20ZX%20Spectrum%20Games%20only).dat'; Format='raw-dat'; ConsoleKey='ZX'    }
    @{ Id='fbneo-msx';         Group='FBNEO'; System='FinalBurn Neo - MSX 1 Games';               Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20MSX%201%20Games%20only).dat'; Format='raw-dat'; ConsoleKey='MSX'   }
    @{ Id='fbneo-ngp';         Group='FBNEO'; System='FinalBurn Neo - Neo Geo Pocket Games';      Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20NeoGeo%20Pocket%20Games%20only).dat'; Format='raw-dat'; ConsoleKey='NGP'   }
    @{ Id='fbneo-sg';          Group='FBNEO'; System='FinalBurn Neo - SuprGrafx Games';           Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20SuprGrafx%20only).dat'; Format='raw-dat'; ConsoleKey='SGX'   }
    @{ Id='fbneo-cv';          Group='FBNEO'; System='FinalBurn Neo - Fairchild Channel F Games'; Url='https://raw.githubusercontent.com/libretro/FBNeo/master/dats/FinalBurn%20Neo%20(ClrMame%20Pro%20XML%2C%20Fairchild%20Channel%20F%20Games%20only).dat'; Format='raw-dat'; ConsoleKey='VF'   }
  )
}

# --- Inventory (persisted metadata on installed DATs) ------------------------

function Get-DatInventory {
  <#
  .SYNOPSIS Loads the local DAT inventory from disk.
  .OUTPUTS  Hashtable  id → metadata object
  #>
  if (-not (Test-Path -LiteralPath $script:DAT_INVENTORY_PATH)) { return @{} }
  try {
    $raw = Get-Content -LiteralPath $script:DAT_INVENTORY_PATH -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) { return @{} }
    $obj = $raw | ConvertFrom-Json -ErrorAction Stop
    $inv = @{}
    foreach ($prop in $obj.PSObject.Properties) {
      $inv[$prop.Name] = $prop.Value
    }
    return $inv
  } catch {
    return @{}
  }
}

function Save-DatInventory {
  <#
  .SYNOPSIS Persists the DAT inventory hashtable to disk.
  #>
  param([hashtable]$Inventory)
  try {
    Assert-DirectoryExists -Path (Split-Path -Parent $script:DAT_INVENTORY_PATH)
    Write-JsonFile -Path $script:DAT_INVENTORY_PATH -Data $Inventory -Depth 4
  } catch {
    Write-Warning ('DAT-Inventory-Speichern fehlgeschlagen ({0}): {1}' -f $script:DAT_INVENTORY_PATH, $_.Exception.Message)
  }
}

# --- No-Intro Love Pack download cache ----------------------------------------

function Get-NoIntroPackPath {
  <#
  .SYNOPSIS Downloads (or returns cached) No-Intro Love Pack zip from archive.org.
  .DESCRIPTION The Love Pack is a single zip containing all No-Intro DAT files.
    It is cached in $env:TEMP\nointro-dat-cache and re-used for 7 days.
  .PARAMETER Log  Optional logging scriptblock.
  .OUTPUTS [string] Full path to the cached zip file.
  #>
  param([scriptblock]$Log)

  try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {}

  $cacheDir = $script:NOINTRO_PACK_CACHE_DIR
  Assert-DirectoryExists -Path $cacheDir

  # Check if a cached pack already exists and is fresh (< 7 days)
  $existing = @(Get-ChildItem -LiteralPath $cacheDir -Filter '*.zip' -File -ErrorAction SilentlyContinue)
  if ($existing.Count -gt 0 -and ((Get-Date) - $existing[0].LastWriteTime).TotalDays -lt 7) {
    return $existing[0].FullName
  }

  # Discover current Love Pack filename from archive.org metadata
  if ($Log) { & $Log 'No-Intro Love Pack: Suche aktuelle Version ...' }
  $meta = Invoke-RestMethod -Uri $script:NOINTRO_PACK_METADATA_URL -UseBasicParsing -TimeoutSec 20
  $packFile = $meta.files | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
  if (-not $packFile) { throw 'No-Intro Love Pack nicht auf archive.org gefunden.' }

  $packUrl   = "https://archive.org/download/no-intro-priv-dat/$([Uri]::EscapeDataString($packFile.name))"
  $localPath = Join-Path $cacheDir $packFile.name

  # Clean old cached packs if the filename changed
  foreach ($old in $existing) {
    if ($old.Name -ne $packFile.name) {
      Remove-Item -LiteralPath $old.FullName -Force -ErrorAction SilentlyContinue
    }
  }

  if (-not (Test-Path -LiteralPath $localPath)) {
    $sizeMB = [math]::Round([long]$packFile.size / 1MB, 0)
    if ($Log) { & $Log "Lade No-Intro Love Pack herunter (~${sizeMB} MB) ..." }
    Invoke-WebRequest -Uri $packUrl -OutFile $localPath -UseBasicParsing -ErrorAction Stop
  }

  $expectedSha1 = [string]$packFile.sha1
  if (-not [string]::IsNullOrWhiteSpace($expectedSha1)) {
    if (-not (Test-DownloadedDatSignature -SourceUrl $packUrl -LocalPath $localPath -ExpectedSha1 $expectedSha1 -Log $Log)) {
      if (Test-Path -LiteralPath $localPath -PathType Leaf) {
        Remove-Item -LiteralPath $localPath -Force -ErrorAction SilentlyContinue
      }
      throw 'No-Intro Love Pack Signaturpruefung fehlgeschlagen.'
    }
  }

  return $localPath
}

function Get-CatalogEntryValue {
  <#
  .SYNOPSIS Reads an entry field safely from hashtable or object under StrictMode.
  #>
  param(
    [Parameter(Mandatory)][object]$Entry,
    [Parameter(Mandatory)][string]$Name,
    [object]$Default = $null
  )

  if ($null -eq $Entry) { return $Default }

  if ($Entry -is [System.Collections.IDictionary]) {
    if ($Entry.Contains($Name)) { return $Entry[$Name] }
    foreach ($key in @($Entry.Keys)) {
      if ([string]::Equals([string]$key, [string]$Name, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Entry[$key]
      }
    }
    return $Default
  }

  if ($Entry.PSObject -and ($Entry.PSObject.Properties.Name -contains $Name)) {
    return $Entry.$Name
  }

  return $Default
}

# --- Merged Catalog View -----------------------------------------------------

function Get-DatCatalogView {
  <#
  .SYNOPSIS Merges built-in catalog with local inventory to produce a display list.
  .DESCRIPTION Returns an array of [pscustomobject] with columns:
    Id, Group, System, ConsoleKey, Url, Format, Status, LocalPath,
    InstalledDate, InstalledSize, InstalledHash
  Status is one of: 'Available', 'Installed', 'Update', 'CustomURL'
  #>
  param(
    [hashtable]$Inventory = $null,
    [hashtable[]]$CustomSources = @()
  )

  if ($null -eq $Inventory) { $Inventory = Get-DatInventory }
  $catalog = Get-BuiltinDatCatalog

  # Add custom sources that aren't in the built-in catalog
  foreach ($cs in $CustomSources) {
    $existing = $catalog | Where-Object { $_.Id -eq $cs.Id }
    if (-not $existing) {
      $catalog += $cs
    }
  }

  $result = New-Object System.Collections.Generic.List[pscustomobject]

  foreach ($entry in $catalog) {
    $id = [string](Get-CatalogEntryValue -Entry $entry -Name 'Id' -Default '')
    $inv = $Inventory[$id]
    $status = 'Available'
    $localPath = ''
    $installedDate = ''
    $installedSize = ''
    $installedHash = ''

    if ($inv) {
      $status = 'Installed'
      $localPath = if ($inv.localPath) { [string]$inv.localPath } else { '' }
      $installedDate = if ($inv.downloadDate) { [string]$inv.downloadDate } else { '' }
      $installedSize = if ($inv.fileSize) { $inv.fileSize } else { '' }
      $installedHash = if ($inv.fileHash) { [string]$inv.fileHash } else { '' }
    }
    # If no URL and not a nointro-pack → user must provide one
    $entryUrl = [string](Get-CatalogEntryValue -Entry $entry -Name 'Url' -Default '')
    $entryFormat = [string](Get-CatalogEntryValue -Entry $entry -Name 'Format' -Default '')
    if ([string]::IsNullOrWhiteSpace($entryUrl) -and $entryFormat -ne 'nointro-pack' -and -not $inv) {
      $status = 'CustomURL'
    }

    [void]$result.Add([pscustomobject]@{
      Id            = $id
      Group         = [string](Get-CatalogEntryValue -Entry $entry -Name 'Group' -Default '')
      System        = [string](Get-CatalogEntryValue -Entry $entry -Name 'System' -Default '')
      ConsoleKey    = [string](Get-CatalogEntryValue -Entry $entry -Name 'ConsoleKey' -Default '')
      Url           = $entryUrl
      Format        = $entryFormat
      PackMatch     = [string](Get-CatalogEntryValue -Entry $entry -Name 'PackMatch' -Default '')
      Status        = $status
      LocalPath     = $localPath
      InstalledDate = $installedDate
      InstalledSize = $installedSize
      InstalledHash = $installedHash
    })
  }

  return $result.ToArray()
}

# --- Download Engine ---------------------------------------------------------

function Invoke-DatDownload {
  <#
  .SYNOPSIS Downloads a single DAT from a URL and stores it locally.
  .PARAMETER Id         Catalog entry ID.
  .PARAMETER Url        Download URL (empty for nointro-pack format).
  .PARAMETER Format     'zip-dat', 'raw-dat', or 'nointro-pack'.
  .PARAMETER TargetDir  Local directory to store the .dat file into.
  .PARAMETER System     Display name for the system (used as filename base).
  .PARAMETER PackMatch  Wildcard pattern to find the DAT inside the Love Pack (nointro-pack only).
  .PARAMETER Log        Optional scriptblock for progress messages.
  .OUTPUTS  [pscustomobject] with: Success, LocalPath, FileSize, FileHash, Error
  #>
  param(
    [Parameter(Mandatory)][string]$Id,
    [Parameter(Mandatory)][AllowEmptyString()][string]$Url,
    [Parameter(Mandatory)][string]$Format,
    [Parameter(Mandatory)][string]$TargetDir,
    [Parameter(Mandatory)][string]$System,
    [string]$PackMatch = '',
    [string]$ExpectedSha256 = '',
    [scriptblock]$Log
  )

  # Ensure TLS 1.2 for modern servers
  try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {}

  $result = [pscustomobject]@{
    Success   = $false
    LocalPath = ''
    FileSize  = 0
    FileHash  = ''
    Error     = ''
  }

  if ([string]::IsNullOrWhiteSpace($Url) -and $Format -ne 'nointro-pack') {
    $result.Error = 'Keine URL angegeben.'
    return $result
  }

  # BUG DATSRC-001 FIX: Only allow HTTPS URLs to prevent SSRF via file:// or http://
  if (-not [string]::IsNullOrWhiteSpace($Url) -and -not $Url.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
    $result.Error = ('Nur HTTPS-URLs erlaubt. Abgelehnt: {0}' -f $Url)
    if ($Log) { & $Log ('[DATSRC] SSRF-Guard: URL abgelehnt: {0}' -f $Url) }
    return $result
  }

  # Ensure target directory exists
  if (-not (Test-Path -LiteralPath $TargetDir)) {
    try {
      [void](New-Item -ItemType Directory -Path $TargetDir -Force)
    } catch {
      $result.Error = "Zielordner konnte nicht erstellt werden: $_"
      return $result
    }
  }

  $safeName = $System -replace '[\\/:*?"<>|]', '_'

  try {
    if ($Log) { & $Log "Download: $System ..." }

    if ($Format -eq 'zip-dat') {
      # Download ZIP to temp, extract .dat file(s)
      $tempZip = Join-Path $env:TEMP "dat_download_$Id.zip"
      try {
        Invoke-WebRequest -Uri $Url -OutFile $tempZip -UseBasicParsing -ErrorAction Stop
      } catch {
        $result.Error = "Download fehlgeschlagen: $_"
        return $result
      }

      # Extract - find .dat files inside the ZIP
      $tempExtract = Join-Path $env:TEMP "dat_extract_$Id"
      if (Test-Path -LiteralPath $tempExtract) {
        Remove-Item -LiteralPath $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
      }
      try {
        Expand-Archive -LiteralPath $tempZip -DestinationPath $tempExtract -Force -ErrorAction Stop
      } catch {
        $result.Error = "ZIP-Entpacken fehlgeschlagen: $_"
        return $result
      }

      $datFiles = @(Get-ChildItem -LiteralPath $tempExtract -Recurse -Filter '*.dat' -File)
      if ($datFiles.Count -eq 0) {
        # Also look for .xml
        $datFiles = @(Get-ChildItem -LiteralPath $tempExtract -Recurse -Filter '*.xml' -File)
      }
      if ($datFiles.Count -eq 0) {
        $result.Error = 'Keine .dat/.xml Datei im ZIP gefunden.'
        return $result
      }

      # Take the first (and usually only) DAT file
      $sourceDat = $datFiles[0].FullName
      $finalName = "$safeName.dat"
      $finalPath = Join-Path $TargetDir $finalName

      Copy-Item -LiteralPath $sourceDat -Destination $finalPath -Force
      # Cleanup temp
      Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue
      Remove-Item -LiteralPath $tempExtract -Recurse -Force -ErrorAction SilentlyContinue

    } elseif ($Format -eq 'raw-dat') {
      # Direct download
      $finalName = "$safeName.dat"
      $finalPath = Join-Path $TargetDir $finalName
      try {
        Invoke-WebRequest -Uri $Url -OutFile $finalPath -UseBasicParsing -ErrorAction Stop
      } catch {
        $result.Error = "Download fehlgeschlagen: $_"
        return $result
      }

    } elseif ($Format -eq 'nointro-pack') {
      # Extract matching DAT from cached No-Intro Love Pack
      if ([string]::IsNullOrWhiteSpace($PackMatch)) {
        $result.Error = 'Kein PackMatch-Muster für No-Intro angegeben.'
        return $result
      }

      $packPath = Get-NoIntroPackPath -Log $Log
      Add-Type -AssemblyName System.IO.Compression.FileSystem
      $zip = [System.IO.Compression.ZipFile]::OpenRead($packPath)
      try {
        $matchingEntry = $zip.Entries | Where-Object {
          $_.FullName -like "*/$PackMatch" -and $_.Name -like '*.dat'
        } | Select-Object -First 1

        if (-not $matchingEntry) {
          $result.Error = "DAT für '$System' nicht im Love Pack gefunden (Muster: $PackMatch)."
          return $result
        }

        $finalName = "$safeName.dat"
        $finalPath = Join-Path $TargetDir $finalName

        $stream = $matchingEntry.Open()
        try {
          $fs = [IO.File]::Create($finalPath)
          try { $stream.CopyTo($fs) } finally { $fs.Close() }
        } finally { $stream.Close() }

        if ($Log) { & $Log "Extrahiert: $($matchingEntry.Name)" }
      } finally {
        $zip.Dispose()
      }

    } elseif ($Format -eq '7z-dat') {
      # Download 7z archive to temp, extract using 7z.exe
      $sevenZip = 'C:\Program Files\7-Zip\7z.exe'
      if (-not (Test-Path -LiteralPath $sevenZip)) {
        # Try PATH fallback
        $found = Get-Command 7z -ErrorAction SilentlyContinue
        if ($found) { $sevenZip = $found.Source } else {
          $result.Error = '7-Zip nicht gefunden. Bitte 7-Zip installieren (https://7-zip.org).'
          return $result
        }
      }

      $temp7z = Join-Path $env:TEMP "dat_download_$Id.7z"
      try {
        Invoke-WebRequest -Uri $Url -OutFile $temp7z -UseBasicParsing -ErrorAction Stop
      } catch {
        $result.Error = "Download fehlgeschlagen: $_"
        return $result
      }

      $tempExtract = Join-Path $env:TEMP "dat_extract_$Id"
      if (Test-Path -LiteralPath $tempExtract) {
        Remove-Item -LiteralPath $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
      }
      [void](New-Item -ItemType Directory -Path $tempExtract -Force)

      $proc = Start-Process -FilePath $sevenZip -ArgumentList 'e', "`"$temp7z`"", "-o`"$tempExtract`"", '-y' -Wait -NoNewWindow -PassThru
      if ($proc.ExitCode -ne 0) {
        $result.Error = '7z-Entpacken fehlgeschlagen (Exit-Code: ' + $proc.ExitCode + ').'
        return $result
      }

      $datFiles = @(Get-ChildItem -LiteralPath $tempExtract -Recurse -Filter '*.dat' -File)
      if ($datFiles.Count -eq 0) {
        $datFiles = @(Get-ChildItem -LiteralPath $tempExtract -Recurse -Filter '*.xml' -File)
      }
      if ($datFiles.Count -eq 0) {
        $result.Error = 'Keine .dat/.xml Datei im 7z-Archiv gefunden.'
        return $result
      }

      # Pick the largest DAT file (main MAME dat vs. sub-dats)
      $sourceDat = ($datFiles | Sort-Object Length -Descending | Select-Object -First 1).FullName
      $finalName = "$safeName.dat"
      $finalPath = Join-Path $TargetDir $finalName

      Copy-Item -LiteralPath $sourceDat -Destination $finalPath -Force
      Remove-Item -LiteralPath $temp7z -Force -ErrorAction SilentlyContinue
      Remove-Item -LiteralPath $tempExtract -Recurse -Force -ErrorAction SilentlyContinue

    } else {
      $result.Error = "Unbekanntes Format: $Format"
      return $result
    }

    if (-not (Test-DownloadedDatSignature -SourceUrl $Url -LocalPath $finalPath -ExpectedSha256 $ExpectedSha256 -Log $Log)) {
      if (Test-Path -LiteralPath $finalPath -PathType Leaf) {
        Remove-Item -LiteralPath $finalPath -Force -ErrorAction SilentlyContinue
      }
      $result.Error = 'DAT-Signaturprüfung fehlgeschlagen.'
      return $result
    }

    # Compute hash and size
    $fileInfo = Get-Item -LiteralPath $finalPath
    $hash = (Get-FileHash -LiteralPath $finalPath -Algorithm SHA256).Hash

    $result.Success   = $true
    $result.LocalPath = $finalPath
    $result.FileSize  = $fileInfo.Length
    $result.FileHash  = $hash

    if ($Log) { & $Log "OK: $System → $finalPath ($([Math]::Round($fileInfo.Length / 1KB, 1)) KB)" }

  } catch {
    $result.Error = "Unerwarteter Fehler: $_"
  }

  return $result
}

function Install-DatFromCatalog {
  <#
  .SYNOPSIS Downloads a DAT from the catalog and updates the inventory.
  .PARAMETER CatalogEntry  One entry from Get-DatCatalogView or Get-BuiltinDatCatalog.
  .PARAMETER DatRoot        Base directory for DAT storage.
  .PARAMETER Inventory      Current inventory hashtable (will be modified in-place).
  .PARAMETER Log            Optional logging scriptblock.
  .OUTPUTS   [bool] $true on success.
  #>
  [CmdletBinding(SupportsShouldProcess=$true)]
  param(
    [Parameter(Mandatory)][psobject]$CatalogEntry,
    [Parameter(Mandatory)][string]$DatRoot,
    [hashtable]$Inventory = @{},
    [scriptblock]$Log
  )

  $entryId = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'Id' -Default '')
  $entryUrl = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'Url' -Default '')
  $entryFormat = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'Format' -Default '')
  $entrySystem = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'System' -Default '')
  $entryPackMatch = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'PackMatch' -Default '')
  $entrySha256 = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'Sha256' -Default '')
  $entryConsoleKey = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'ConsoleKey' -Default '')
  $entryGroup = [string](Get-CatalogEntryValue -Entry $CatalogEntry -Name 'Group' -Default '')

  if ($entryFormat -eq 'nointro-pack' -and [string]::IsNullOrWhiteSpace($entryPackMatch) -and -not [string]::IsNullOrWhiteSpace($entryId)) {
    $builtinEntry = @(
      Get-BuiltinDatCatalog |
        Where-Object { [string](Get-CatalogEntryValue -Entry $_ -Name 'Id' -Default '') -eq $entryId } |
        Select-Object -First 1
    )
    if (@($builtinEntry).Count -gt 0) {
      $entryPackMatch = [string](Get-CatalogEntryValue -Entry $builtinEntry[0] -Name 'PackMatch' -Default '')
      if ([string]::IsNullOrWhiteSpace($entrySystem)) {
        $entrySystem = [string](Get-CatalogEntryValue -Entry $builtinEntry[0] -Name 'System' -Default '')
      }
      if ([string]::IsNullOrWhiteSpace($entryGroup)) {
        $entryGroup = [string](Get-CatalogEntryValue -Entry $builtinEntry[0] -Name 'Group' -Default '')
      }
      if ([string]::IsNullOrWhiteSpace($entryConsoleKey)) {
        $entryConsoleKey = [string](Get-CatalogEntryValue -Entry $builtinEntry[0] -Name 'ConsoleKey' -Default '')
      }
    }
  }

  $group = if (-not [string]::IsNullOrWhiteSpace($entryGroup)) { $entryGroup } else { 'Custom' }
  $targetDir = Join-Path $DatRoot $group

  $dlResult = Invoke-DatDownload `
    -Id        $entryId `
    -Url       $entryUrl `
    -Format    $entryFormat `
    -TargetDir $targetDir `
    -System    $entrySystem `
    -PackMatch $entryPackMatch `
    -ExpectedSha256 $entrySha256 `
    -Log       $Log

  if (-not $dlResult.Success) {
    if ($Log) { & $Log "FEHLER: $entrySystem - $($dlResult.Error)" }
    return $false
  }

  # Update inventory
  $Inventory[$entryId] = @{
    localPath    = $dlResult.LocalPath
    downloadDate = (Get-Date).ToString('o')
    fileSize     = $dlResult.FileSize
    fileHash     = $dlResult.FileHash
    sourceUrl    = $entryUrl
    group        = $group
    system       = $entrySystem
    consoleKey   = $entryConsoleKey
  }

  Save-DatInventory -Inventory $Inventory
  return $true
}

function Update-DatFromCatalog {
  <#
  .SYNOPSIS Re-downloads a DAT and checks if it changed.
  .OUTPUTS   [string] 'Updated', 'Unchanged', or 'Error'
  #>
  [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification='Wrapper uses controlled install/update path.')]
  [CmdletBinding(SupportsShouldProcess)]
  param(
    [Parameter(Mandatory)][psobject]$CatalogEntry,
    [Parameter(Mandatory)][string]$DatRoot,
    [hashtable]$Inventory = @{},
    [scriptblock]$Log
  )

  $oldHash = ''
  $inv = $Inventory[$CatalogEntry.Id]
  if ($inv -and $inv.fileHash) { $oldHash = $inv.fileHash }

  $ok = Install-DatFromCatalog -CatalogEntry $CatalogEntry -DatRoot $DatRoot -Inventory $Inventory -Log $Log
  if (-not $ok) { return 'Error' }

  $newHash = $Inventory[$CatalogEntry.Id].fileHash
  if ($oldHash -eq $newHash) {
    if ($Log) { & $Log "Unverändert: $($CatalogEntry.System)" }
    return 'Unchanged'
  }
  if ($Log) { & $Log "Aktualisiert: $($CatalogEntry.System)" }
  return 'Updated'
}

function Invoke-DatCatalogAction {
  <#
  .SYNOPSIS Unified action API for single DAT catalog operations.
  .PARAMETER Action  Supported: Install, Update.
  .OUTPUTS [pscustomobject] with Action, Success, Status, EntryId, System.
  #>
  [CmdletBinding(SupportsShouldProcess=$true)]
  param(
    [Parameter(Mandatory)][ValidateSet('Install','Update')][string]$Action,
    [Parameter(Mandatory)][psobject]$CatalogEntry,
    [Parameter(Mandatory)][string]$DatRoot,
    [hashtable]$Inventory = @{},
    [scriptblock]$Log
  )

  if ($Action -eq 'Install') {
    $ok = Install-DatFromCatalog -CatalogEntry $CatalogEntry -DatRoot $DatRoot -Inventory $Inventory -Log $Log
    return [pscustomobject]@{
      Action = 'Install'
      Success = [bool]$ok
      Status = if ($ok) { 'Installed' } else { 'Error' }
      EntryId = [string]$CatalogEntry.Id
      System = [string]$CatalogEntry.System
    }
  }

  $result = Update-DatFromCatalog -CatalogEntry $CatalogEntry -DatRoot $DatRoot -Inventory $Inventory -Log $Log
  return [pscustomobject]@{
    Action = 'Update'
    Success = ([string]$result -ne 'Error')
    Status = [string]$result
    EntryId = [string]$CatalogEntry.Id
    System = [string]$CatalogEntry.System
  }
}

function Invoke-DatCatalogBatchAction {
  <#
  .SYNOPSIS Unified batch API for DAT catalog operations.
  .PARAMETER Action  Supported: Install, Update.
  .OUTPUTS [pscustomobject] with Total, Installed, Updated, Unchanged, Failed, Skipped, Errors.
  #>
  [CmdletBinding(SupportsShouldProcess=$true)]
  param(
    [Parameter(Mandatory)][ValidateSet('Install','Update')][string]$Action,
    [Parameter(Mandatory)][psobject[]]$Entries,
    [Parameter(Mandatory)][string]$DatRoot,
    [scriptblock]$Progress,
    [scriptblock]$Log
  )

  $inventory = Get-DatInventory
  $installed = 0
  $updated = 0
  $unchanged = 0
  $failed = 0
  $skipped = 0
  $errors = 0

  for ($i = 0; $i -lt $Entries.Count; $i++) {
    $entry = $Entries[$i]
    if ($Progress) { & $Progress ($i + 1) $Entries.Count $entry.System }

    if ($Action -eq 'Install' -and [string]::IsNullOrWhiteSpace($entry.Url) -and $entry.Format -ne 'nointro-pack') {
      $skipped++
      if ($Log) { & $Log "Übersprungen (keine URL): $($entry.System)" }
      continue
    }

    $single = Invoke-DatCatalogAction -Action $Action -CatalogEntry $entry -DatRoot $DatRoot -Inventory $inventory -Log $Log
    if ($Action -eq 'Install') {
      if ([string]$single.Status -eq 'Installed') { $installed++ } else { $failed++ }
      continue
    }

    switch ([string]$single.Status) {
      'Updated' { $updated++ }
      'Unchanged' { $unchanged++ }
      default { $errors++ }
    }
  }

  return [pscustomobject]@{
    Action = $Action
    Total = $Entries.Count
    Installed = $installed
    Updated = $updated
    Unchanged = $unchanged
    Failed = $failed
    Skipped = $skipped
    Errors = $errors
  }
}

function Invoke-DatInventoryRemoval {
  <#
  .SYNOPSIS Removes a DAT from inventory and optionally deletes the file.
  .NOTES RESERVED – future CLI/API endpoint (see openapi.yaml).
  #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string]$Id,
    [hashtable]$Inventory = @{},
    [switch]$DeleteFile
  )

  $inv = $Inventory[$Id]
  if ($inv -and $DeleteFile -and $inv.localPath -and (Test-Path -LiteralPath $inv.localPath)) {
    Remove-Item -LiteralPath $inv.localPath -Force -ErrorAction SilentlyContinue
  }
  $Inventory.Remove($Id)
  Save-DatInventory -Inventory $Inventory
}

# --- Batch Operations --------------------------------------------------------


function Invoke-DatBatchUpdate {
  <#
  .SYNOPSIS Re-downloads multiple DATs and reports changes.
  .PARAMETER Entries    Array of catalog entries to update (should be installed ones).
  .PARAMETER DatRoot    Base DAT storage directory.
  .PARAMETER Progress   Optional scriptblock called with (current, total, systemName).
  .PARAMETER Log        Optional logging scriptblock.
  .OUTPUTS   [pscustomobject] with Updated, Unchanged, Error counts.
  .NOTES RESERVED – future CLI/API endpoint (see openapi.yaml).
  #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][psobject[]]$Entries,
    [Parameter(Mandatory)][string]$DatRoot,
    [scriptblock]$Progress,
    [scriptblock]$Log
  )

  $result = Invoke-DatCatalogBatchAction -Action Update -Entries $Entries -DatRoot $DatRoot -Progress $Progress -Log $Log
  return [pscustomobject]@{
    Updated   = [int]$result.Updated
    Unchanged = [int]$result.Unchanged
    Errors    = [int]$result.Errors
    Total     = [int]$result.Total
  }
}

Set-Alias -Name Remove-DatFromInventory -Value Invoke-DatInventoryRemoval -Scope Script
Set-Alias -Name Update-DatBatch -Value Invoke-DatBatchUpdate -Scope Script

# --- Custom Source Management ------------------------------------------------

function Add-CustomDatSource {
  <#
  .SYNOPSIS Adds a user-defined DAT source to the inventory.
  .PARAMETER Group       Group name (e.g. 'No-Intro', 'Custom').
  .PARAMETER System      System display name.
  .PARAMETER ConsoleKey  Console key for DAT matching.
  .PARAMETER Url         Download URL (can be empty for local-only).
  .PARAMETER LocalPath   Path to an already-existing local DAT file.
  .PARAMETER Inventory   Inventory hashtable.
  #>
  param(
    [Parameter(Mandatory)][string]$Group,
    [Parameter(Mandatory)][string]$System,
    [Parameter(Mandatory)][string]$ConsoleKey,
    [string]$Url = '',
    [string]$LocalPath = '',
    [string]$Format = 'raw-dat',
    [hashtable]$Inventory = @{}
  )

  $safeName = ($System -replace '[\\/:*?"<>|]', '_').ToLowerInvariant() -replace '\s+', '-'
  $id = "custom-$safeName"

  $meta = @{
    group        = $Group
    system       = $System
    consoleKey   = $ConsoleKey
    sourceUrl    = $Url
    format       = $Format
    downloadDate = (Get-Date).ToString('o')
    localPath    = $LocalPath
    fileSize     = 0
    fileHash     = ''
  }

  # If local file exists, compute hash
  if ($LocalPath -and (Test-Path -LiteralPath $LocalPath)) {
    $fi = Get-Item -LiteralPath $LocalPath
    $meta.fileSize = $fi.Length
    $meta.fileHash = (Get-FileHash -LiteralPath $LocalPath -Algorithm SHA256).Hash
  }

  $Inventory[$id] = $meta
  Save-DatInventory -Inventory $Inventory
  return $id
}

# --- Utility: Format file size for display -----------------------------------

function Format-DatFileSize {
  <#
  .SYNOPSIS Formats a byte count as a human-readable size string.
  #>
  param([long]$Bytes)
  if ($Bytes -lt 1KB) { return "$Bytes B" }
  if ($Bytes -lt 1MB) { return "{0:N1} KB" -f ($Bytes / 1KB) }
  if ($Bytes -lt 1GB) { return "{0:N1} MB" -f ($Bytes / 1MB) }
  return "{0:N2} GB" -f ($Bytes / 1GB)
}
