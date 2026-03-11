#  ROM COVER/THUMBNAIL SCRAPER (LF-01)
#  Boxart/Screenshots via ScreenScraper.fr oder IGDB-API herunterladen.

function New-CoverScraperConfig {
  <#
  .SYNOPSIS
    Erstellt eine Scraper-Konfiguration.
  #>
  param(
    [Parameter(Mandatory)][string]$Provider,
    [string]$ApiKey = '',
    [string]$CacheDir = '',
    [ValidateSet('box-2d','box-3d','screenshot','title','wheel')]
    [string]$ImageType = 'box-2d',
    [int]$MaxWidth = 400,
    [int]$TimeoutSeconds = 30
  )

  return @{
    Provider       = $Provider
    ApiKey         = $ApiKey
    CacheDir       = $CacheDir
    ImageType      = $ImageType
    MaxWidth       = $MaxWidth
    TimeoutSeconds = $TimeoutSeconds
    RateLimit      = 1.0
  }
}

function Get-CoverCachePath {
  <#
  .SYNOPSIS
    Gibt den Cache-Pfad fuer ein bestimmtes ROM-Cover zurueck.
  #>
  param(
    [Parameter(Mandatory)][string]$CacheDir,
    [Parameter(Mandatory)][string]$ConsoleKey,
    [Parameter(Mandatory)][string]$GameName,
    [string]$ImageType = 'box-2d'
  )

  $safeName = $GameName -replace '[\\/:*?"<>|]', '_'
  $fileName = "$($safeName)_$($ImageType).jpg"
  return Join-Path $CacheDir (Join-Path $ConsoleKey $fileName)
}

function Test-CoverCached {
  <#
  .SYNOPSIS
    Prueft ob ein Cover bereits im Cache liegt.
  #>
  param(
    [Parameter(Mandatory)][string]$CacheDir,
    [Parameter(Mandatory)][string]$ConsoleKey,
    [Parameter(Mandatory)][string]$GameName,
    [string]$ImageType = 'box-2d'
  )

  $path = Get-CoverCachePath -CacheDir $CacheDir -ConsoleKey $ConsoleKey -GameName $GameName -ImageType $ImageType
  return (Test-Path -LiteralPath $path)
}

function New-CoverScrapeRequest {
  <#
  .SYNOPSIS
    Erstellt ein Scrape-Request-Objekt.
  #>
  param(
    [Parameter(Mandatory)][string]$GameName,
    [Parameter(Mandatory)][string]$ConsoleKey,
    [string]$Hash = '',
    [string]$Region = ''
  )

  return @{
    GameName   = $GameName
    ConsoleKey = $ConsoleKey
    Hash       = $Hash
    Region     = $Region
    Status     = 'Pending'
    ResultPath = ''
    Error      = ''
  }
}

function Invoke-CoverScrape {
  <#
  .SYNOPSIS
    Fuehrt einen Batch-Scrape aus mit echten API-Calls und Cache-Fallback.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][array]$Requests
  )

  $results = [System.Collections.Generic.List[hashtable]]::new()

  foreach ($req in $Requests) {
    $entry = $req.Clone()

    # Pruefe Cache
    if ($Config.CacheDir -and (Test-CoverCached -CacheDir $Config.CacheDir -ConsoleKey $req.ConsoleKey -GameName $req.GameName -ImageType $Config.ImageType)) {
      $entry.Status = 'Cached'
      $entry.ResultPath = Get-CoverCachePath -CacheDir $Config.CacheDir -ConsoleKey $req.ConsoleKey -GameName $req.GameName -ImageType $Config.ImageType
      $results.Add($entry)
      continue
    }

    # Rate-Limiting
    if ($Config.RateLimit -gt 0) {
      Start-Sleep -Milliseconds ([int]($Config.RateLimit * 1000))
    }

    try {
      $imageUrl = $null
      if ($Config.Provider -eq 'screenscraper') {
        $imageUrl = Get-ScreenScraperCoverUrl -Config $Config -GameName $req.GameName -ConsoleKey $req.ConsoleKey -Hash $req.Hash
      } elseif ($Config.Provider -eq 'libretro-thumbnails') {
        $imageUrl = Get-LibretroThumbnailUrl -ConsoleKey $req.ConsoleKey -GameName $req.GameName -ImageType $Config.ImageType
      }

      if ($imageUrl) {
        $targetPath = Get-CoverCachePath -CacheDir $Config.CacheDir -ConsoleKey $req.ConsoleKey -GameName $req.GameName -ImageType $Config.ImageType
        $downloaded = Invoke-CoverDownload -Url $imageUrl -TargetPath $targetPath -TimeoutSeconds $Config.TimeoutSeconds
        if ($downloaded) {
          $entry.Status = 'Found'
          $entry.ResultPath = $targetPath
        } else {
          $entry.Status = 'NotFound'
        }
      } else {
        $entry.Status = 'NotFound'
      }
    } catch {
      $entry.Status = 'Error'
      $entry.Error = $_.Exception.Message
    }

    $results.Add($entry)
  }

  return ,$results.ToArray()
}

function Get-ScreenScraperCoverUrl {
  <#
  .SYNOPSIS
    Baut die ScreenScraper.fr API-URL fuer ein Cover zusammen.
    Gibt die Bild-URL zurueck oder $null bei keinem Match.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][string]$GameName,
    [Parameter(Mandatory)][string]$ConsoleKey,
    [string]$Hash = ''
  )

  if ([string]::IsNullOrWhiteSpace($Config.ApiKey)) { return $null }

  # ScreenScraper System-IDs (Subset — erweiterbar)
  $systemMap = @{
    'NES' = 3; 'SNES' = 4; 'N64' = 14; 'GB' = 9; 'GBC' = 10; 'GBA' = 12
    'NDS' = 15; 'GC' = 13; 'Wii' = 16; 'Genesis' = 1; 'MasterSystem' = 2
    'Saturn' = 22; 'Dreamcast' = 23; 'PS1' = 57; 'PS2' = 58; 'PSP' = 61
    'PCEngine' = 31; 'NeoGeo' = 142; 'Arcade' = 75
  }

  $systemId = if ($systemMap.ContainsKey($ConsoleKey)) { $systemMap[$ConsoleKey] } else { $null }
  if (-not $systemId) { return $null }

  $mediaType = switch ($Config.ImageType) {
    'box-2d'     { 'box-2D' }
    'box-3d'     { 'box-3D' }
    'screenshot' { 'ss' }
    'title'      { 'sstitle' }
    'wheel'      { 'wheel' }
    default      { 'box-2D' }
  }

  $baseUrl = 'https://www.screenscraper.fr/api2'
  $encodedName = [System.Uri]::EscapeDataString($GameName)

  # Hash-basierte Suche bevorzugen (genauer)
  $searchParam = if ($Hash) { "&md5=$Hash" } else { "&romnom=$encodedName" }

  $apiUrl = "${baseUrl}/jeuInfos.php?devid=romcleanup&devpassword=&softname=RomCleanup&ssid=&sspassword=&output=json&systemeid=${systemId}${searchParam}"

  # API-Key als Credentials (ScreenScraper nutzt ssid/sspassword)
  if ($Config.ApiKey -match '^(.+):(.+)$') {
    $apiUrl = $apiUrl -replace 'ssid=&sspassword=', "ssid=$([System.Uri]::EscapeDataString($Matches[1]))&sspassword=$([System.Uri]::EscapeDataString($Matches[2]))"
  }

  try {
    $webClient = [System.Net.WebClient]::new()
    $webClient.Headers.Add('User-Agent', 'RomCleanup/2.0')
    $json = $webClient.DownloadString($apiUrl)
    $webClient.Dispose()

    $data = $json | ConvertFrom-Json
    if ($data -and $data.response -and $data.response.jeu -and $data.response.jeu.medias) {
      $media = @($data.response.jeu.medias | Where-Object { $_.type -eq $mediaType -and $_.region -in @('wor','us','eu','jp','') }) | Select-Object -First 1
      if ($media -and $media.url) { return [string]$media.url }
    }
  } catch { } # API-Fehler non-fatal — NotFound zurueck

  return $null
}

function Get-LibretroThumbnailUrl {
  <#
  .SYNOPSIS
    Baut URL zum libretro-thumbnails GitHub-Repository.
    Kostenlos, kein API-Key noetig, aber nur Named_Boxarts/Snaps/Titles.
  #>
  param(
    [Parameter(Mandatory)][string]$ConsoleKey,
    [Parameter(Mandatory)][string]$GameName,
    [string]$ImageType = 'box-2d'
  )

  $systemMap = @{
    'NES'          = 'Nintendo - Nintendo Entertainment System'
    'SNES'         = 'Nintendo - Super Nintendo Entertainment System'
    'N64'          = 'Nintendo - Nintendo 64'
    'GB'           = 'Nintendo - Game Boy'
    'GBC'          = 'Nintendo - Game Boy Color'
    'GBA'          = 'Nintendo - Game Boy Advance'
    'NDS'          = 'Nintendo - Nintendo DS'
    'GC'           = 'Nintendo - GameCube'
    'Wii'          = 'Nintendo - Wii'
    'Genesis'      = 'Sega - Mega Drive - Genesis'
    'MasterSystem' = 'Sega - Master System - Mark III'
    'Saturn'       = 'Sega - Saturn'
    'Dreamcast'    = 'Sega - Dreamcast'
    'PS1'          = 'Sony - PlayStation'
    'PS2'          = 'Sony - PlayStation 2'
    'PSP'          = 'Sony - PlayStation Portable'
    'PCEngine'     = 'NEC - PC Engine - TurboGrafx 16'
    'Arcade'       = 'MAME'
  }

  $system = if ($systemMap.ContainsKey($ConsoleKey)) { $systemMap[$ConsoleKey] } else { $null }
  if (-not $system) { return $null }

  $subDir = switch ($ImageType) {
    'box-2d'     { 'Named_Boxarts' }
    'screenshot' { 'Named_Snaps' }
    'title'      { 'Named_Titles' }
    default      { 'Named_Boxarts' }
  }

  # libretro-thumbnails erwartet exakt den No-Intro/Redump-Dateinamen
  $safeName = $GameName -replace '[&*/:`<>?\\|]', '_'
  $encodedSystem = [System.Uri]::EscapeDataString($system)
  $encodedName = [System.Uri]::EscapeDataString($safeName)

  return "https://thumbnails.libretro.com/${encodedSystem}/${subDir}/${encodedName}.png"
}

function Invoke-CoverDownload {
  <#
  .SYNOPSIS
    Laedt ein Bild von einer URL herunter und speichert es im Cache.
  #>
  param(
    [Parameter(Mandatory)][string]$Url,
    [Parameter(Mandatory)][string]$TargetPath,
    [int]$TimeoutSeconds = 30
  )

  # SSRF-Schutz: Nur HTTPS-URLs erlauben
  if ($Url -notmatch '^https://') { return $false }

  $dir = Split-Path -Parent $TargetPath
  if (-not (Test-Path -LiteralPath $dir)) {
    [void](New-Item -Path $dir -ItemType Directory -Force)
  }

  try {
    $request = [System.Net.HttpWebRequest]::Create($Url)
    $request.Method = 'GET'
    $request.Timeout = $TimeoutSeconds * 1000
    $request.UserAgent = 'RomCleanup/2.0'

    $response = $request.GetResponse()
    $statusCode = [int]$response.StatusCode

    if ($statusCode -eq 200) {
      $stream = $response.GetResponseStream()
      $fileStream = [System.IO.File]::Create($TargetPath)
      $stream.CopyTo($fileStream)
      $fileStream.Close()
      $stream.Close()
      $response.Close()
      return $true
    }

    $response.Close()
  } catch { } # Download-Fehler non-fatal

  return $false
}

function Get-CoverScrapeReport {
  <#
  .SYNOPSIS
    Zusammenfassung eines Scrape-Durchlaufs.
  #>
  param(
    [Parameter(Mandatory)][array]$Results
  )

  $cached   = @($Results | Where-Object { $_.Status -eq 'Cached' }).Count
  $found    = @($Results | Where-Object { $_.Status -eq 'Found' }).Count
  $notFound = @($Results | Where-Object { $_.Status -eq 'NotFound' }).Count
  $errors   = @($Results | Where-Object { $_.Status -eq 'Error' }).Count

  return @{
    Total    = $Results.Count
    Cached   = $cached
    Found    = $found
    NotFound = $notFound
    Errors   = $errors
  }
}
