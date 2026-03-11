if (-not (Get-Variable -Name DAT_ARCHIVE_STATS -Scope Script -ErrorAction SilentlyContinue)) {
  $script:DAT_ARCHIVE_STATS = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}

if (-not (Get-Variable -Name SCAN_INDEX_CACHE -Scope Script -ErrorAction SilentlyContinue)) {
  $script:SCAN_INDEX_CACHE = $null
  $script:SCAN_INDEX_DIRTY = $false
  $script:SCAN_INDEX_WRITE_COUNTER = 0
}

# FileHash-Cache: delegates to generic LruCache.ps1 when available
if (-not (Get-Variable -Name FILE_HASH_LRU -Scope Script -ErrorAction SilentlyContinue) -or -not $script:FILE_HASH_LRU) {
  if (Get-Command New-LruCache -ErrorAction SilentlyContinue) {
    $script:FILE_HASH_LRU = New-LruCache -MaxEntries 20000 -Name 'FileHash'
    if (Get-Command Register-LruCache -ErrorAction SilentlyContinue) {
      Register-LruCache -Cache $script:FILE_HASH_LRU
    }
  } else {
    # Lightweight fallback when LruCache.ps1 is not loaded (e.g. isolated tests)
    $script:FILE_HASH_LRU = [pscustomobject]@{
      Name = 'FileHash'; MaxEntries = 20000
      Data = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      Order = [System.Collections.Generic.LinkedList[string]]::new()
      Nodes = [System.Collections.Generic.Dictionary[string,System.Collections.Generic.LinkedListNode[string]]]::new([StringComparer]::OrdinalIgnoreCase)
      Hits = [long]0; Misses = [long]0; Evictions = [long]0
    }
  }
}

function Get-ScanIndexCache {
  if ($null -ne $script:SCAN_INDEX_CACHE) { return $script:SCAN_INDEX_CACHE }
  if (Get-Command Import-ScanIndex -ErrorAction SilentlyContinue) {
    $script:SCAN_INDEX_CACHE = Import-ScanIndex -Root (Get-Location).Path
  } else {
    $script:SCAN_INDEX_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }
  return $script:SCAN_INDEX_CACHE
}

function Save-ScanIndexCacheIfNeeded {
  if (-not $script:SCAN_INDEX_DIRTY) { return }
  if (-not (Get-Command Save-ScanIndex -ErrorAction SilentlyContinue)) { return }
  try {
    Save-ScanIndex -Root (Get-Location).Path -Entries $script:SCAN_INDEX_CACHE | Out-Null
    $script:SCAN_INDEX_DIRTY = $false
  } catch {
    Write-Warning ('ScanIndex-Cache speichern fehlgeschlagen: {0}' -f $_.Exception.Message)
  }
}

function New-SecureXmlReaderSettings {
  # XXE-Schutz: DtdProcessing=Ignore ignoriert DTDs komplett (keine Entity-Expansion).
  # XmlResolver=$null verhindert externe Ressourcen-Aufloesung (SSRF-Schutz).
  # Ignore statt Prohibit, da reale DATs (No-Intro, Redump) DOCTYPE-Deklarationen enthalten.
  $s = New-Object System.Xml.XmlReaderSettings
  $s.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
  $s.XmlResolver = $null
  $s.IgnoreComments = $true
  $s.IgnoreWhitespace = $true
  return $s
}

function Reset-DatArchiveStats {
  $script:DAT_ARCHIVE_STATS = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $script:DAT_ARCHIVE_STATS['SkippedTooLarge'] = 0
  $script:DAT_ARCHIVE_STATS['LargeArchiveHashed'] = 0
  $script:DAT_ARCHIVE_STATS['SkippedNoTool'] = 0
  $script:DAT_ARCHIVE_STATS['SkippedZipSlip'] = 0
  $script:DAT_ARCHIVE_STATS['SkippedPostExtractUnsafe'] = 0
  $script:DAT_ARCHIVE_STATS['Error7z'] = 0
}

function Add-DatArchiveStat {
  param([string]$Key)
  if ([string]::IsNullOrWhiteSpace($Key)) { return }
  if (-not $script:DAT_ARCHIVE_STATS) { Reset-DatArchiveStats }
  if (-not $script:DAT_ARCHIVE_STATS.ContainsKey($Key)) { $script:DAT_ARCHIVE_STATS[$Key] = 0 }
  $script:DAT_ARCHIVE_STATS[$Key] = [int]$script:DAT_ARCHIVE_STATS[$Key] + 1
}

function Get-DatArchiveStats {
  if (-not $script:DAT_ARCHIVE_STATS) { Reset-DatArchiveStats }
  $copy = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($k in $script:DAT_ARCHIVE_STATS.Keys) { $copy[$k] = $script:DAT_ARCHIVE_STATS[$k] }
  return $copy
}

# BUG-DAT-01 fix: Alias map for ConsoleKey mismatches between dat-catalog/DatSources and consoles.json
$script:DAT_CONSOLE_KEY_ALIASES = @{
  'PSX'     = 'PS1'
  'MCD'     = 'SCD'
  'FMT'     = 'FMTOWNS'
  'AJCD'    = 'JAGCD'
  'NSW'     = 'SWITCH'
  'SVISION' = 'SUPERVISION'
  'NEO'     = 'NEOGEO'
  'SG1K'    = 'SG1000'
  'TG16'    = 'PCE'
  'VEC'     = 'VECTREX'
  'VF'      = 'CHANNELF'
}

function Resolve-DatConsoleKeyAlias {
  param([string]$Key)
  if ([string]::IsNullOrWhiteSpace($Key)) { return $Key }
  $upper = $Key.ToUpperInvariant()
  if ($script:DAT_CONSOLE_KEY_ALIASES.ContainsKey($upper)) {
    return $script:DAT_CONSOLE_KEY_ALIASES[$upper]
  }
  return $upper
}

function Get-DatConsoleKey {
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Name)

  $name = $Name.ToLowerInvariant()
  if ($script:CONSOLE_FOLDER_MAP.ContainsKey($name)) {
    return (Resolve-DatConsoleKeyAlias $script:CONSOLE_FOLDER_MAP[$name])
  }

  foreach ($entry in $script:DAT_CONSOLE_RX_MAP) {
    if ($name -match $entry.Rx) { return (Resolve-DatConsoleKeyAlias $entry.Key) }
  }

  return $null
}

function Get-DatIndexCachePath {
  param([string]$Root)

  if (Get-Command Get-ReportsFilePath -ErrorAction SilentlyContinue) {
    return (Get-ReportsFilePath -Root $Root -FileName 'dat-index-cache.json')
  }
  if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Get-Location).Path }
  $reportsDir = Join-Path $Root 'reports'
  return (Join-Path $reportsDir 'dat-index-cache.json')
}

function Get-DatIndexCacheFingerprint {
  param(
    [string]$DatRoot,
    [string]$HashType,
    [hashtable]$ConsoleMap
  )

  $hashInput = New-Object System.Collections.Generic.List[string]
  [void]$hashInput.Add(('hashType={0}' -f [string]$HashType))
  [void]$hashInput.Add(('datRoot={0}' -f [string]$DatRoot))

  if ($ConsoleMap) {
    foreach ($entry in @($ConsoleMap.GetEnumerator() | Sort-Object Key)) {
      [void]$hashInput.Add(('map={0}=>{1}' -f [string]$entry.Key, [string]$entry.Value))
    }
  }

  $datFiles = @()
  if (-not [string]::IsNullOrWhiteSpace($DatRoot) -and (Test-Path -LiteralPath $DatRoot -PathType Container)) {
    $datFiles = @(Get-ChildItem -LiteralPath $DatRoot -Filter *.dat -File -Recurse -ErrorAction SilentlyContinue)
  }

  [void]$hashInput.Add(('fileCount={0}' -f $datFiles.Count))
  foreach ($file in @($datFiles | Sort-Object FullName)) {
    [void]$hashInput.Add(('{0}|{1}|{2}' -f [string]$file.FullName, [int64]$file.Length, [int64]$file.LastWriteTimeUtc.Ticks))
  }

  $combined = ($hashInput -join "`n")
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($combined)
    $digest = $sha.ComputeHash($bytes)
    return ([BitConverter]::ToString($digest) -replace '-', '').ToLowerInvariant()
  } finally {
    $sha.Dispose()
  }
}

function Import-DatIndexCache {
  param(
    [string]$Root,
    [string]$ExpectedFingerprint
  )

  $path = Get-DatIndexCachePath -Root $Root
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }

  try {
    $raw = Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if (-not $raw) { return $null }
    if ([string]$raw.Fingerprint -ne [string]$ExpectedFingerprint) { return $null }

    $index = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    if ($raw.Index) {
      foreach ($consoleProp in $raw.Index.PSObject.Properties) {
        $inner = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        if ($consoleProp.Value) {
          foreach ($hashProp in $consoleProp.Value.PSObject.Properties) {
            $inner[[string]$hashProp.Name] = [string]$hashProp.Value
          }
        }
        $index[[string]$consoleProp.Name] = $inner
      }
    }
    return $index
  } catch {
    return $null
  }
}

function Save-DatIndexCache {
  param(
    [string]$Root,
    [string]$Fingerprint,
    [hashtable]$Index
  )

  if (-not $Index) { return }

  $path = Get-DatIndexCachePath -Root $Root
  Assert-DirectoryExists -Path (Split-Path -Parent $path)

  $payload = [ordered]@{
    SchemaVersion = 'dat-index-cache-v1'
    UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Fingerprint = [string]$Fingerprint
    Index = $Index
  }

  Write-JsonFile -Path $path -Data $payload -Depth 10
}

function Get-DatIndex {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$DatRoot,
    [ValidateSet('SHA1','MD5','CRC32','CRC')][string]$HashType,
    [hashtable]$ConsoleMap,
    [scriptblock]$Log
  )

  # BUG-MV-03: Null-Guard fuer $Log
  if (-not $Log) { $Log = { } }

  function Import-DatXmlSafe {
    param(
      [string]$Path,
      [scriptblock]$Log
    )

    try {
      $raw = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
      if ([string]::IsNullOrWhiteSpace($raw)) {
        if ($Log) { & $Log ("DAT: Leer, ueberspringe {0}" -f (Split-Path -Leaf $Path)) }
        return $null
      }
      $trim = $raw.TrimStart()
      # [F-05] Strip UTF-8 BOM (U+FEFF) if present before XML start-check
      if ($trim.Length -ge 1 -and [int][char]$trim[0] -eq 0xFEFF) {
        $trim = $trim.Substring(1).TrimStart()
      }
      if (-not $trim.StartsWith('<')) {
        if ($Log) { & $Log ("DAT: Nicht-XML (clrmamepro?), ueberspringe {0}" -f (Split-Path -Leaf $Path)) }
        return $null
      }

      $xmlSettings = New-SecureXmlReaderSettings

      $stringReader = New-Object System.IO.StringReader($raw)
      try {
        $xmlReader = [System.Xml.XmlReader]::Create($stringReader, $xmlSettings)
        try {
          $doc = New-Object System.Xml.XmlDocument
          $doc.XmlResolver = $null
          $doc.Load($xmlReader)
          return $doc
        } finally {
          if ($xmlReader) { $xmlReader.Dispose() }
        }
      } finally {
        $stringReader.Dispose()
      }
    } catch {
      if ($Log) { & $Log ("DAT: Fehler beim Laden {0}: {1}" -f (Split-Path -Leaf $Path), $_.Exception.Message) }
      return $null
    }
  }

  function Set-DatIndexEntryDeterministic {
    param(
      [hashtable]$Target,
      [string]$Hash,
      [string]$GameName
    )

    if ([string]::IsNullOrWhiteSpace($Hash) -or [string]::IsNullOrWhiteSpace($GameName)) { return }
    if (-not $Target.ContainsKey($Hash)) {
      $Target[$Hash] = $GameName
      return
    }

    $existing = [string]$Target[$Hash]
    $candidate = [string]$GameName
    if ([string]::IsNullOrWhiteSpace($existing)) {
      $Target[$Hash] = $candidate
      return
    }

    # BUG-DAT-02: Bei Hash-Kollision (mehrere DAT-Eintraege mit gleichem Hash)
    # waehlen wir deterministisch den kuerzesten Namen, bei Gleichstand
    # alphabetisch den ersten. Kuerzere Namen sind i.d.R. die kanonischen
    # Titel ohne Region-/Version-Suffixe. Determinismus ist kritisch fuer
    # reproduzierbare Ergebnisse (gleicher Input = gleicher Output).
    if ($candidate.Length -lt $existing.Length) {
      $Target[$Hash] = $candidate
      return
    }
    if ($candidate.Length -eq $existing.Length -and ([string]::Compare($candidate, $existing, [StringComparison]::OrdinalIgnoreCase) -lt 0)) {
      $Target[$Hash] = $candidate
    }
  }

  function Add-DatEntriesFromXml {
    param(
      [xml]$Xml,
      [string]$ConsoleKey,
      [string]$HashKey,
      [hashtable]$Index
    )

    $games = @()
    $df = $Xml.datafile
    if ($df) {
      $gp = $df.PSObject.Properties['game']
      if ($gp -and $gp.Value) { $games += @($gp.Value) }
      $mp = $df.PSObject.Properties['machine']
      if ($mp -and $mp.Value) { $games += @($mp.Value) }
      $sp = $df.PSObject.Properties['software']
      if ($sp -and $sp.Value) { $games += @($sp.Value) }
    }
    foreach ($g in $games) {
      Test-CancelRequested
      $gameName = $g.name
      $nodes = @()
      $rp = $g.PSObject.Properties['rom']
      if ($rp -and $rp.Value) { $nodes += @($rp.Value) }
      $dp = $g.PSObject.Properties['disk']
      if ($dp -and $dp.Value) { $nodes += @($dp.Value) }

      foreach ($n in $nodes) {
        $hash = $null
        if ($n -and (Get-Member -InputObject $n -Name 'GetAttribute' -MemberType Method -ErrorAction SilentlyContinue)) {
          $hash = $n.GetAttribute($HashKey)
        }
        if (-not $hash) {
          $hash = $n.$HashKey
        }
        if (-not $hash) { continue }
        $h = $hash.ToLowerInvariant()
        Set-DatIndexEntryDeterministic -Target $Index[$ConsoleKey] -Hash $h -GameName $gameName
      }
    }
  }

  function Add-DatEntriesStreaming {
    param(
      [string]$Path,
      [string]$ConsoleKey,
      [string]$HashKey,
      [hashtable]$Index,
      [scriptblock]$Log,
      [System.Xml.XmlReaderSettings]$XmlSettings
    )

    try {
      $reader = [System.Xml.XmlReader]::Create($Path, $XmlSettings)

      $currentName = $null
      $stack = New-Object System.Collections.Generic.Stack[string]

      while ($reader.Read()) {
        if ($reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
          $nodeName = $reader.Name
          if ($nodeName -in @('game','machine','software')) {
            $currentName = $reader.GetAttribute('name')
            $stack.Push($nodeName)
          } elseif ($nodeName -in @('rom','disk')) {
            if ($currentName) {
              $hash = $reader.GetAttribute($HashKey)
              if ($hash) {
                $h = $hash.ToLowerInvariant()
                Set-DatIndexEntryDeterministic -Target $Index[$ConsoleKey] -Hash $h -GameName $currentName
              }
            }
          }
        } elseif ($reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement) {
          if ($reader.Name -in @('game','machine','software')) {
            if ($stack.Count -gt 0) { $null = $stack.Pop() }
            if ($stack.Count -eq 0) { $currentName = $null }
          }
        }
      }
    } catch {
      if ($Log) { & $Log ("DAT: Fehler beim Streaming {0}: {1}" -f (Split-Path -Leaf $Path), $_.Exception.Message) }
    } finally {
      # BUG DAT-001 FIX: Ensure XmlReader is disposed even if an exception occurs
      if ($reader) { $reader.Dispose() }
    }
  }

  $index = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $xmlReaderSettings = New-SecureXmlReaderSettings
  # BUG-045 FIX: Reduced from 500MB to 100MB (still 2x largest known DAT file)
  $maxCharsInDoc = 100MB
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      $configuredMaxChars = Get-AppStateValue -Key 'DatXmlMaxCharactersInDocument' -Default $maxCharsInDoc
      if ($configuredMaxChars -is [int] -or $configuredMaxChars -is [long]) {
        $maxCharsInDoc = [int64][Math]::Max(1048576, [int64]$configuredMaxChars)
      }
    } catch {
      if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
        [void](Write-CatchGuardLog -Module 'Dat' -Action 'Get-DatIndex/MaxCharsConfig' -Exception $_.Exception -Level 'Warning')
      }
    }
  }
  $xmlReaderSettings.MaxCharactersInDocument = [int64]$maxCharsInDoc

  $hashKey = $HashType.ToLowerInvariant()
  if ($hashKey -eq 'crc32') { $hashKey = 'crc' }

  $cacheEnabled = [bool](Get-AppStateValue -Key 'DatIndexCacheEnabled' -Default $true)
  $cacheFingerprint = $null
  if ($cacheEnabled) {
    try {
      $cacheFingerprint = Get-DatIndexCacheFingerprint -DatRoot $DatRoot -HashType $HashType -ConsoleMap $ConsoleMap
      $cachedIndex = Import-DatIndexCache -Root (Get-Location).Path -ExpectedFingerprint $cacheFingerprint
      if ($cachedIndex -and $cachedIndex.Count -gt 0) {
        if ($Log) { & $Log ('DAT: persistenten Index-Cache geladen ({0} Konsolen).' -f $cachedIndex.Count) }
        return $cachedIndex
      }
    } catch {
      if ($Log) { & $Log ('DAT: Cache-Lookup fehlgeschlagen: {0}' -f $_.Exception.Message) }
    }
  }

  $streamThresholdBytes = 20MB
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    $configuredThreshold = Get-AppStateValue -Key 'DatStreamThresholdBytes' -Default $streamThresholdBytes
    if ($configuredThreshold -is [int] -or $configuredThreshold -is [long]) {
      $streamThresholdBytes = [int64][math]::Max(1048576, [int64]$configuredThreshold)
    }
  }

  if ($ConsoleMap -and $ConsoleMap.Count -gt 0) {
    & $Log "DAT: nutze GUI-Mapping"
    foreach ($entry in $ConsoleMap.GetEnumerator()) {
      Test-CancelRequested
      $mapKey = [string]$entry.Key
      $mapPath = [string]$entry.Value
      if ([string]::IsNullOrWhiteSpace($mapPath)) { continue }
      if (-not (Test-Path -LiteralPath $mapPath)) {
        & $Log ("DAT: Ordner nicht gefunden: {0}" -f $mapPath)
        continue
      }

      $consoleKey = Get-DatConsoleKey -Name $mapKey
      if (-not $consoleKey -and $script:BEST_FORMAT.ContainsKey($mapKey.ToUpperInvariant())) {
        $consoleKey = Resolve-DatConsoleKeyAlias $mapKey
      }
      if (-not $consoleKey) {
        & $Log ("DAT: Unbekannte Konsole im Mapping: {0}" -f $mapKey)
        continue
      }

      if (-not $index.ContainsKey($consoleKey)) {
        $index[$consoleKey] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      }

      $files = @(Get-ChildItem -LiteralPath $mapPath -Filter *.dat -File -ErrorAction SilentlyContinue)
      foreach ($f in $files) {
        Test-CancelRequested
        $fileInfo = Get-Item -LiteralPath $f.FullName -ErrorAction SilentlyContinue
        if ($fileInfo -and $fileInfo.Length -gt $streamThresholdBytes) {
          Add-DatEntriesStreaming -Path $f.FullName -ConsoleKey $consoleKey -HashKey $hashKey -Index $index -Log $Log -XmlSettings $xmlReaderSettings
        } else {
          $xml = Import-DatXmlSafe -Path $f.FullName -Log $Log
          if (-not $xml) { continue }
          Add-DatEntriesFromXml -Xml $xml -ConsoleKey $consoleKey -HashKey $hashKey -Index $index
        }
      }

      & $Log ("DAT: {0} -> {1} Hashes" -f $consoleKey, $index[$consoleKey].Count)
    }

    if ($cacheEnabled -and -not [string]::IsNullOrWhiteSpace($cacheFingerprint)) {
      try { Save-DatIndexCache -Root (Get-Location).Path -Fingerprint $cacheFingerprint -Index $index } catch { }
    }
    return $index
  }

  if ([string]::IsNullOrWhiteSpace($DatRoot)) { return $index }
  if (-not (Test-Path -LiteralPath $DatRoot)) {
    & $Log ("DAT: Ordner nicht gefunden: {0}" -f $DatRoot)
    return $index
  }

  $subDirs = @(Get-ChildItem -LiteralPath $DatRoot -Directory -ErrorAction SilentlyContinue)
  $useFolders = $subDirs.Count -gt 0

  if ($useFolders) {
    & $Log "DAT: nutze Ordner pro Konsole"
    foreach ($dir in $subDirs) {
      Test-CancelRequested
      $consoleKey = Get-DatConsoleKey -Name $dir.Name
      if (-not $consoleKey) { continue }

      if (-not $index.ContainsKey($consoleKey)) {
        $index[$consoleKey] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      }

      $files = @(Get-ChildItem -LiteralPath $dir.FullName -Filter *.dat -File -ErrorAction SilentlyContinue)
      foreach ($f in $files) {
        Test-CancelRequested
        $fileInfo = Get-Item -LiteralPath $f.FullName -ErrorAction SilentlyContinue
        if ($fileInfo -and $fileInfo.Length -gt $streamThresholdBytes) {
          Add-DatEntriesStreaming -Path $f.FullName -ConsoleKey $consoleKey -HashKey $hashKey -Index $index -Log $Log -XmlSettings $xmlReaderSettings
        } else {
          $xml = Import-DatXmlSafe -Path $f.FullName -Log $Log
          if (-not $xml) { continue }
          Add-DatEntriesFromXml -Xml $xml -ConsoleKey $consoleKey -HashKey $hashKey -Index $index
        }
      }

      & $Log ("DAT: {0} -> {1} Hashes" -f $consoleKey, $index[$consoleKey].Count)
    }
  } else {
    $files = @(Get-ChildItem -LiteralPath $DatRoot -Filter *.dat -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) {
      & $Log ("DAT: Keine .dat Dateien gefunden in {0}" -f $DatRoot)
      return $index
    }

    & $Log "DAT: nutze Dateinamen-Mapping"
    foreach ($f in $files) {
      Test-CancelRequested
      $consoleKey = Get-DatConsoleKey -Name $f.Name
      if (-not $consoleKey) { continue }

      if (-not $index.ContainsKey($consoleKey)) {
        $index[$consoleKey] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      }

      $fileInfo = Get-Item -LiteralPath $f.FullName -ErrorAction SilentlyContinue
      if ($fileInfo -and $fileInfo.Length -gt $streamThresholdBytes) {
        Add-DatEntriesStreaming -Path $f.FullName -ConsoleKey $consoleKey -HashKey $hashKey -Index $index -Log $Log -XmlSettings $xmlReaderSettings
      } else {
        try {
          $xml = Import-DatXmlSafe -Path $f.FullName -Log $Log
        } catch { $xml = $null }
        if (-not $xml) { continue }
        Add-DatEntriesFromXml -Xml $xml -ConsoleKey $consoleKey -HashKey $hashKey -Index $index
      }

      & $Log ("DAT: {0} -> {1} Hashes" -f $consoleKey, $index[$consoleKey].Count)
    }
  }

  if ($cacheEnabled -and -not [string]::IsNullOrWhiteSpace($cacheFingerprint)) {
    try { Save-DatIndexCache -Root (Get-Location).Path -Fingerprint $cacheFingerprint -Index $index } catch { }
  }
  return $index
}

function Get-FileHashCached {
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path,
    [Parameter(Mandatory=$true)][ValidateSet('SHA1','MD5','CRC32')][string]$HashType,
    [hashtable]$Cache
  )

  $sharedCacheMode = $false
  $cacheKey = $Path
  $fingerprint = $null

  if (-not $Cache) {
    $Cache = $script:FILE_HASH_LRU.Data
    $cacheKey = ('{0}|{1}' -f [string]$HashType, [string]$Path)
    $sharedCacheMode = $true

    # OPT-04: Check LRU cache first (fast in-memory lookup) before ScanIndex (disk I/O)
    if ($Cache.ContainsKey($cacheKey)) {
      return (Get-LruCacheValue -Cache $script:FILE_HASH_LRU -Key $cacheKey)
    }

    if (Get-Command Get-PathFingerprint -ErrorAction SilentlyContinue) {
      $fingerprint = Get-PathFingerprint -Path $Path
      if (-not [string]::IsNullOrWhiteSpace($fingerprint)) {
        $scanIndex = Get-ScanIndexCache
        if ($scanIndex.ContainsKey($cacheKey)) {
          $entry = $scanIndex[$cacheKey]
          if ($entry -and ($entry.PSObject.Properties.Name -contains 'Fingerprint') -and ($entry.PSObject.Properties.Name -contains 'Hash')) {
            if ([string]$entry.Fingerprint -eq $fingerprint -and -not [string]::IsNullOrWhiteSpace([string]$entry.Hash)) {
              Set-LruCacheValue -Cache $script:FILE_HASH_LRU -Key $cacheKey -Value ([string]$entry.Hash)
              return [string]$entry.Hash
            }
          }
        }
      }
    }
  }

  if ($Cache.ContainsKey($cacheKey)) {
    if ($sharedCacheMode) {
      return (Get-LruCacheValue -Cache $script:FILE_HASH_LRU -Key $cacheKey)
    }
    return $Cache[$cacheKey]
  }

  try {
    if ($HashType -in @('CRC32','CRC')) {
      $hash = Get-Crc32Hash -Path $Path
    } else {
      $hash = (Get-FileHash -LiteralPath $Path -Algorithm $HashType).Hash.ToLowerInvariant()
    }

    if ($sharedCacheMode) {
      # Update max entries from config, then set (handles eviction automatically)
      $maxEntries = 20000
      if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
        try {
          $cfg = Get-AppStateValue -Key 'FileHashCacheMaxEntries' -Default $maxEntries
          if ($cfg -is [int] -or $cfg -is [long]) {
            $maxEntries = [int][math]::Max(500, [int64]$cfg)
          }
        } catch {
          if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
            [void](Write-CatchGuardLog -Module 'Dat' -Action 'Get-FileHashCached/CacheConfig' -Exception $_.Exception -Level 'Warning')
          }
        }
      }
      $script:FILE_HASH_LRU.MaxEntries = $maxEntries
      Set-LruCacheValue -Cache $script:FILE_HASH_LRU -Key $cacheKey -Value $hash

      if ([string]::IsNullOrWhiteSpace($fingerprint) -and (Get-Command Get-PathFingerprint -ErrorAction SilentlyContinue)) {
        $fingerprint = Get-PathFingerprint -Path $Path
      }
      if (-not [string]::IsNullOrWhiteSpace($fingerprint)) {
        $scanIndex = Get-ScanIndexCache
        $scanIndex[$cacheKey] = [pscustomobject]@{
          Fingerprint = [string]$fingerprint
          Hash = [string]$hash
          UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        }
        $script:SCAN_INDEX_DIRTY = $true
        $script:SCAN_INDEX_WRITE_COUNTER++
        if ($script:SCAN_INDEX_WRITE_COUNTER % 200 -eq 0) {
          Save-ScanIndexCacheIfNeeded
        }
      }
    } else {
      $Cache[$cacheKey] = $hash
    }

    return $hash
  } catch {
    return $null
  }
}

# --- Inline C# CRC32 (compiled once, ~50-100x faster than PS byte loop) ---
if (-not ([System.Management.Automation.PSTypeName]'RomCleanup.Crc32').Type) {
  Add-Type -TypeDefinition @'
using System;
using System.IO;

namespace RomCleanup {
    public static class Crc32 {
        private static readonly uint[] Table = InitTable();

        private static uint[] InitTable() {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++) {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static string HashFile(string path) {
            using (var fs = File.OpenRead(path)) {
                return HashStream(fs);
            }
        }

        public static string HashStream(Stream s) {
            uint crc = 0xFFFFFFFFu;
            byte[] buf = new byte[1048576];
            int read;
            while ((read = s.Read(buf, 0, buf.Length)) > 0) {
                for (int i = 0; i < read; i++)
                    crc = Table[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            }
            crc ^= 0xFFFFFFFFu;
            return crc.ToString("x8");
        }
    }
}
'@ -Language CSharp -ErrorAction Stop
}

function Get-Crc32Hash {
  param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path)
  return [RomCleanup.Crc32]::HashFile($Path)
}

function Get-Crc32FromStream {
  param([System.IO.Stream]$Stream)
  return [RomCleanup.Crc32]::HashStream($Stream)
}

function Get-HashFromStream {
  param(
    [Parameter(Mandatory=$true)][System.IO.Stream]$Stream,
    [Parameter(Mandatory=$true)][ValidateSet('SHA1','MD5','CRC32','CRC')][string]$HashType
  )

  if ($HashType -in @('CRC32','CRC')) {
    return Get-Crc32FromStream -Stream $Stream
  }

  $algo = [System.Security.Cryptography.HashAlgorithm]::Create($HashType)
  if (-not $algo) { return $null }
  $hashBytes = $algo.ComputeHash($Stream)
  $algo.Dispose()
  return ([BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
}

function Get-HashesFromZip {
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path,
    [Parameter(Mandatory=$true)][ValidateSet('SHA1','MD5','CRC32')][string]$HashType,
    [scriptblock]$Log
  )

  Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue | Out-Null
  $hashes = New-Object System.Collections.Generic.List[string]

  try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
  } catch {
    if ($Log) { & $Log ("DAT: ZIP fehlerhaft, ueberspringe: {0} ({1})" -f $Path, $_.Exception.Message) }
    return @()
  }

  try {
    if ($Log) { & $Log ("DAT: ZIP Hashing {0} ({1} entries)" -f $Path, $zip.Entries.Count) }
    foreach ($entry in $zip.Entries) {
      if ($entry.Length -le 0) { continue }
      try {
        $stream = $entry.Open()
        try {
          $h = Get-HashFromStream -Stream $stream -HashType $HashType
          if ($h) { [void]$hashes.Add($h) }
        } finally {
          $stream.Dispose()
        }
      } catch {
        if ($Log) { & $Log ("DAT: ZIP Eintrag fehlerhaft, ueberspringe: {0} ({1})" -f $Path, $_.Exception.Message) }
        continue
      }
    }
    if ($Log) { & $Log ("DAT: ZIP Hashing fertig: {0}" -f $Path) }
  } finally {
    $zip.Dispose()
  }

  return $hashes
}

function Get-HashesFrom7z {
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path,
    [Parameter(Mandatory=$true)][ValidateSet('SHA1','MD5','CRC32')][string]$HashType,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$SevenZipPath,
    [scriptblock]$Log
  )

  if (-not $SevenZipPath) {
    Add-DatArchiveStat -Key 'SkippedNoTool'
    if ($Log) { & $Log "DAT: 7z.exe nicht gefunden fuer .7z Hashing" }
    return @()
  }

  $tempDir = Join-Path $env:TEMP ("rom_dedupe_7z_" + [guid]::NewGuid().ToString("N"))
  [void](New-Item -ItemType Directory -Path $tempDir)

  try {
    $entryPaths = @(Get-ArchiveEntryPaths -ArchivePath $Path -SevenZipPath $SevenZipPath -TempFiles $null)
    if ($entryPaths.Count -gt 0 -and -not (Test-ArchiveEntryPathsSafe -EntryPaths $entryPaths)) {
      Add-DatArchiveStat -Key 'SkippedZipSlip'
      if ($Log) { & $Log ("DAT: ZIP Slip erkannt, ueberspringe: {0}" -f $Path) }
      return @()
    }
    if ($entryPaths.Count -eq 0) {
      if ($Log) { & $Log ("DAT: 7z ohne Eintraege, ueberspringe: {0}" -f $Path) }
      return @()
    }
    if ($Log) { & $Log ("DAT: 7z Entpacken {0}" -f $Path) }
    $outArg = "-o{0}" -f ([System.IO.Path]::GetFullPath($tempDir))
    $toolArgs = @('x','-y',$outArg,(ConvertTo-QuotedArg $Path))
    try {
      $run = Invoke-7z -SevenZipPath $SevenZipPath -Arguments $toolArgs -TempFiles $null
    } catch {
      Add-DatArchiveStat -Key 'Error7z'
      if ($Log) { & $Log ("DAT: 7z Aufruf fehlgeschlagen/Timeout bei {0}: {1}" -f $Path, $_.Exception.Message) }
      return @()
    }
    $exitCode = $run.ExitCode
    if ($exitCode -ne 0) {
      Add-DatArchiveStat -Key 'Error7z'
      if ($Log) {
        $details = (([string]$run.StdErr) + "`n" + ([string]$run.StdOut)).Trim()
        if ($details) {
          & $Log ("DAT: 7z Fehler ({0}) bei {1}: {2}" -f $exitCode, $Path, $details)
        } else {
          & $Log ("DAT: 7z Fehler ({0}) bei {1}" -f $exitCode, $Path)
        }
      }
      return @()
    }

    $extracted = @(Get-ChildItem -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue)
    foreach ($ef in $extracted) {
      if (-not (Test-PathWithinRoot -Path $ef.FullName -Root $tempDir)) {
        Add-DatArchiveStat -Key 'SkippedPostExtractUnsafe'
        if ($Log) { & $Log ("DAT: ZIP Slip nach Entpacken erkannt, ueberspringe: {0}" -f $Path) }
        return @()
      }
      if ($ef.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Add-DatArchiveStat -Key 'SkippedPostExtractUnsafe'
        if ($Log) { & $Log ("DAT: ReparsePoint im entpackten Archiv erkannt, ueberspringe: {0}" -f $Path) }
        return @()
      }
    }

    $hashes = New-Object System.Collections.Generic.List[string]
    $files = @(Get-FilesSafe -Root $tempDir)
    foreach ($f in $files) {
      $h = $null
      try {
        if ($HashType -in @('CRC32','CRC')) {
          $h = Get-Crc32Hash -Path $f.FullName
        } else {
          $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm $HashType).Hash.ToLowerInvariant()
        }
      } catch { }
      if ($h) { [void]$hashes.Add($h) }
    }

    if ($Log) { & $Log ("DAT: 7z Hashing fertig: {0}" -f $Path) }

    return $hashes
  } finally {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
  }
}

function Get-ArchiveHashes {
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Path,
    [Parameter(Mandatory=$true)][ValidateSet('SHA1','MD5','CRC32')][string]$HashType,
    [hashtable]$Cache,
    [string]$SevenZipPath,
    [scriptblock]$Log,
    [long]$ArchiveMaxHashSizeBytes = 100MB,
    [ValidateSet('WarnAndHash','Skip')][string]$LargeArchivePolicy = 'WarnAndHash'
  )

  $key = "ARCHIVE|{0}|{1}" -f $HashType, $Path
  if ($Cache -and $Cache.ContainsKey($key)) { return $Cache[$key] }

  $ext = [IO.Path]::GetExtension($Path).ToLowerInvariant()
  try {
    $archiveSize = (Get-Item -LiteralPath $Path -ErrorAction Stop).Length
    if ($archiveSize -gt $ArchiveMaxHashSizeBytes) {
      $thresholdMB = [math]::Round(($ArchiveMaxHashSizeBytes / 1MB), 2)
      $archiveMB = [math]::Round(($archiveSize / 1MB), 2)
      if ($LargeArchivePolicy -eq 'Skip') {
        Add-DatArchiveStat -Key 'SkippedTooLarge'
        if ($Log) { & $Log ("DAT: Archiv zu gross fuer Hashing ({0} MB > {1} MB), ueberspringe: {2}" -f $archiveMB, $thresholdMB, $Path) }
        if ($Cache) { $Cache[$key] = @() }
        return @()
      }

      Add-DatArchiveStat -Key 'LargeArchiveHashed'
      if ($Log) {
        & $Log ("DAT: Archiv groesser als Schwellwert ({0} MB > {1} MB), wird trotzdem gehasht: {2}" -f $archiveMB, $thresholdMB, $Path)
      }
    }
  } catch { }

  $hashes = @()
  if ($ext -eq '.zip') {
    $hashes = Get-HashesFromZip -Path $Path -HashType $HashType -Log $Log
  } elseif ($ext -eq '.7z') {
    $hashes = Get-HashesFrom7z -Path $Path -HashType $HashType -SevenZipPath $SevenZipPath -Log $Log
  }

  if ($Cache) { $Cache[$key] = $hashes }
  return $hashes
}

function Get-DatGameKey {
  param(
    [Parameter(Mandatory=$true)][string[]]$Paths,
    [Parameter(Mandatory=$true)][hashtable]$ConsoleIndex,
    [Parameter(Mandatory=$true)][ValidateSet('SHA1','MD5','CRC32')][string]$HashType,
    [hashtable]$HashCache,
    [string]$SevenZipPath,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash
  )

  if (-not $ConsoleIndex) { return $null }
  $skipExts = @('.m3u', '.cue', '.gdi', '.ccd')

  foreach ($p in $Paths) {
    $ext = [IO.Path]::GetExtension($p).ToLowerInvariant()
    if ($skipExts -contains $ext) { continue }
    if ($OnDatHash) { & $OnDatHash $p }
    if ($ext -in @('.zip','.7z')) {
      $hashes = Get-ArchiveHashes -Path $p -HashType $HashType -Cache $HashCache -SevenZipPath $SevenZipPath -Log $Log
      foreach ($h in $hashes) {
        if ($h -and $ConsoleIndex.ContainsKey($h)) { return $ConsoleIndex[$h] }
      }
    } else {
      $hash = Get-FileHashCached -Path $p -HashType $HashType -Cache $HashCache
      if ($hash -and $ConsoleIndex.ContainsKey($hash)) {
        return $ConsoleIndex[$hash]
      }
    }
  }

  return $null
}

function Get-NoMatchKey {
  <# Build a deterministic key for non-DAT-matching items to avoid accidental grouping. #>
  param(
    [string]$MainPath,
    [string]$BaseName
  )

  $raw = if (-not [string]::IsNullOrWhiteSpace($MainPath)) { $MainPath } else { $BaseName }
  if ([string]::IsNullOrWhiteSpace($raw)) { $raw = '__unknown__' }
  return ('__nomatch__|' + $raw.Trim().ToLowerInvariant())
}

function Resolve-GameKey {
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$BaseName,
    [string[]]$Paths,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Root,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$MainPath,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [hashtable]$HashCache,
    [string]$HashType,
    [bool]$DatFallback,
    [bool]$AliasEditionKeying,
    [string]$SevenZipPath,
    [scriptblock]$Log,
    [scriptblock]$OnDatHash
  )

  $console = Get-ConsoleType -RootPath $Root -FilePath $MainPath -Extension ([IO.Path]::GetExtension($MainPath))

  if ($UseDat -and $DatIndex) {
    if ($DatIndex.ContainsKey($console)) {
      $datKey = Get-DatGameKey -Paths $Paths -ConsoleIndex $DatIndex[$console] -HashType $HashType -HashCache $HashCache -SevenZipPath $SevenZipPath -Log $Log -OnDatHash $OnDatHash
      if ($datKey) {
        return [pscustomobject]@{ Key = $datKey.Trim().ToLowerInvariant(); DatMatched = $true }
      }
    }
    # DAT was ON, lookup was attempted but no match found
    if ($DatFallback) {
      return [pscustomobject]@{ Key = (ConvertTo-GameKey -BaseName $BaseName -AliasEditionKeying $AliasEditionKeying -ConsoleType $console); DatMatched = $false }
    }
    # No fallback → path-based key prevents accidental grouping of unmatched items
    return [pscustomobject]@{ Key = (Get-NoMatchKey -MainPath $MainPath -BaseName $BaseName); DatMatched = $false }
  }

  # DAT is OFF → always use name-based grouping for format deduplication
  return [pscustomobject]@{ Key = (ConvertTo-GameKey -BaseName $BaseName -AliasEditionKeying $AliasEditionKeying -ConsoleType $console); DatMatched = $false }
}

# ================================================================
#  1G1R - Parent/Clone Index (No-Intro DAT XML)
# ================================================================

function Get-DatParentCloneIndex {
  <# Parst No-Intro Parent/Clone DAT-XML und liefert:
       @{ ParentMap = @{consoleKey|gameName → parentName}; ChildrenMap = @{consoleKey|parent → @(clone1,...)} }
     Nutzt cloneof-Attribut auf <game>-Elementen. #>
  [CmdletBinding()]
  param(
    [ValidateNotNullOrEmpty()][string]$DatRoot,
    [hashtable]$ConsoleMap,
    [scriptblock]$Log
  )

  function Add-ParentCloneFromXml {
    param(
      [xml]$Xml,
      [string]$ConsoleKey,
      [hashtable]$ParentMap,
      [hashtable]$ChildrenMap
    )

    $games = @()
    $df = $Xml.datafile
    if ($df) {
      # BUG DAT-004 FIX: Also collect <machine> and <software> elements for parent/clone
      foreach ($tagName in @('game','machine','software')) {
        $gp = $df.PSObject.Properties[$tagName]
        if ($gp -and $gp.Value) { $games += @($gp.Value) }
      }
    }

    foreach ($g in $games) {
      Test-CancelRequested
      $gameName = $g.name
      if ([string]::IsNullOrWhiteSpace($gameName)) { continue }
      $cloneOf = $null
      try { $cloneOf = $g.GetAttribute('cloneof') } catch { }
      if ([string]::IsNullOrWhiteSpace($cloneOf)) {
        try { $cloneOf = $g.cloneof } catch { }
      }

      $mapKey = $ConsoleKey + '|' + $gameName
      if (-not [string]::IsNullOrWhiteSpace($cloneOf)) {
        $parentKey = $ConsoleKey + '|' + $cloneOf
        $ParentMap[$mapKey] = $cloneOf
        if (-not $ChildrenMap.ContainsKey($parentKey)) {
          $ChildrenMap[$parentKey] = [System.Collections.Generic.List[string]]::new()
        }
        [void]$ChildrenMap[$parentKey].Add($gameName)
      } else {
        if (-not $ParentMap.ContainsKey($mapKey)) {
          $ParentMap[$mapKey] = $gameName
        }
      }
    }
  }

  function Add-ParentCloneStreaming {
    param(
      [string]$Path,
      [string]$ConsoleKey,
      [hashtable]$ParentMap,
      [hashtable]$ChildrenMap,
      [scriptblock]$Log,
      [System.Xml.XmlReaderSettings]$XmlSettings
    )

    try {
      $reader = [System.Xml.XmlReader]::Create($Path, $XmlSettings)

      while ($reader.Read()) {
        # BUG DAT-004 FIX: Also handle <machine> and <software> elements which use the same cloneof attribute
        if ($reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $reader.Name -in @('game','machine','software')) {
          $gameName = $reader.GetAttribute('name')
          $cloneOf  = $reader.GetAttribute('cloneof')

          if ([string]::IsNullOrWhiteSpace($gameName)) { continue }

          $mapKey = $ConsoleKey + '|' + $gameName
          if (-not [string]::IsNullOrWhiteSpace($cloneOf)) {
            $parentKey = $ConsoleKey + '|' + $cloneOf
            $ParentMap[$mapKey] = $cloneOf
            if (-not $ChildrenMap.ContainsKey($parentKey)) {
              $ChildrenMap[$parentKey] = [System.Collections.Generic.List[string]]::new()
            }
            [void]$ChildrenMap[$parentKey].Add($gameName)
          } else {
            if (-not $ParentMap.ContainsKey($mapKey)) {
              $ParentMap[$mapKey] = $gameName
            }
          }
        }
      }
    } catch {
      if ($Log) { & $Log ("1G1R: Fehler beim Streaming {0}: {1}" -f (Split-Path -Leaf $Path), $_.Exception.Message) }
    } finally {
      # BUG DAT-002 FIX: Ensure XmlReader is disposed even if an exception occurs
      if ($reader) { $reader.Dispose() }
    }
  }

  $parentMap   = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $childrenMap = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  $xmlSettings = New-SecureXmlReaderSettings
  $streamThresholdBytes = 20MB
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    $configuredThreshold = Get-AppStateValue -Key 'DatStreamThresholdBytes' -Default $streamThresholdBytes
    if ($configuredThreshold -is [int] -or $configuredThreshold -is [long]) {
      $streamThresholdBytes = [int64][math]::Max(1048576, [int64]$configuredThreshold)
    }
  }

  $datDirs = @()
  if ($ConsoleMap -and $ConsoleMap.Count -gt 0) {
    foreach ($entry in $ConsoleMap.GetEnumerator()) {
      $consoleKey = Get-DatConsoleKey -Name ([string]$entry.Key)
      if (-not $consoleKey -and $script:BEST_FORMAT.ContainsKey(([string]$entry.Key).ToUpperInvariant())) {
        $consoleKey = ([string]$entry.Key).ToUpperInvariant()
      }
      if (-not $consoleKey) { continue }
      $datDirs += [pscustomobject]@{ ConsoleKey = $consoleKey; Path = [string]$entry.Value }
    }
  } elseif (-not [string]::IsNullOrWhiteSpace($DatRoot) -and (Test-Path -LiteralPath $DatRoot)) {
    $subDirs = @(Get-ChildItem -LiteralPath $DatRoot -Directory -ErrorAction SilentlyContinue)
    if ($subDirs.Count -gt 0) {
      foreach ($dir in $subDirs) {
        $consoleKey = Get-DatConsoleKey -Name $dir.Name
        if ($consoleKey) { $datDirs += [pscustomobject]@{ ConsoleKey = $consoleKey; Path = $dir.FullName } }
      }
    } else {
      $files = @(Get-ChildItem -LiteralPath $DatRoot -Filter *.dat -File -ErrorAction SilentlyContinue)
      foreach ($f in $files) {
        $consoleKey = Get-DatConsoleKey -Name $f.Name
        if ($consoleKey) { $datDirs += [pscustomobject]@{ ConsoleKey = $consoleKey; Path = $DatRoot; SingleFile = $f.FullName } }
      }
    }
  }

  $totalParents = 0
  $totalClones  = 0
  foreach ($dd in $datDirs) {
    Test-CancelRequested
    $ck = $dd.ConsoleKey
    $files = @()
    if ($dd.PSObject.Properties['SingleFile'] -and $dd.SingleFile) {
      $files = @(Get-Item -LiteralPath $dd.SingleFile -ErrorAction SilentlyContinue)
    } else {
      $p = [string]$dd.Path
      if (Test-Path -LiteralPath $p) {
        $files = @(Get-ChildItem -LiteralPath $p -Filter *.dat -File -ErrorAction SilentlyContinue)
      }
    }

    foreach ($f in $files) {
      Test-CancelRequested
      $fi = Get-Item -LiteralPath $f.FullName -ErrorAction SilentlyContinue
      if ($fi -and $fi.Length -gt $streamThresholdBytes) {
        Add-ParentCloneStreaming -Path $f.FullName -ConsoleKey $ck -ParentMap $parentMap -ChildrenMap $childrenMap -Log $Log -XmlSettings $xmlSettings
      } else {
        try {
          $raw = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction Stop
          if ([string]::IsNullOrWhiteSpace($raw) -or -not $raw.TrimStart().StartsWith('<')) { continue }
          # Use safe XML parsing to prevent XXE attacks (DTD disabled, XmlResolver nulled)
          $safeSettings = New-SecureXmlReaderSettings
          $stringReader = New-Object System.IO.StringReader($raw)
          $xmlReader = [System.Xml.XmlReader]::Create($stringReader, $safeSettings)
          $xml = New-Object System.Xml.XmlDocument
          $xml.XmlResolver = $null
          $xml.Load($xmlReader)
          $xmlReader.Dispose()
          $stringReader.Dispose()
          Add-ParentCloneFromXml -Xml $xml -ConsoleKey $ck -ParentMap $parentMap -ChildrenMap $childrenMap
        } catch { continue }
      }
    }

    $consoleClones = 0
    foreach ($ck2 in $childrenMap.Keys) {
      if ($ck2.StartsWith($ck + '|', [StringComparison]::OrdinalIgnoreCase)) {
        $consoleClones += $childrenMap[$ck2].Count
      }
    }
    $totalClones += $consoleClones
  }

  $totalParents = $parentMap.Count
  if ($Log -and ($totalParents -gt 0 -or $totalClones -gt 0)) {
    & $Log ("1G1R: {0} Eintraege, davon {1} Clones" -f $totalParents, $totalClones)
  }

  return [pscustomobject]@{
    ParentMap   = $parentMap
    ChildrenMap = $childrenMap
  }
}

function Resolve-ParentName {
  <# Loest transitive cloneof-Ketten auf und gibt den Root-Parent zurueck.
     Schutz gegen Zyklen durch max. 20 Iterationen. #>
  param(
    [string]$GameName,
    [string]$ConsoleKey,
    [hashtable]$ParentMap
  )

  if ([string]::IsNullOrWhiteSpace($GameName) -or -not $ParentMap) { return $GameName }

  $current = $GameName
  $maxDepth = 20
  $visited  = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

  for ($i = 0; $i -lt $maxDepth; $i++) {
    $mapKey = $ConsoleKey + '|' + $current
    if (-not $ParentMap.ContainsKey($mapKey)) { return $current }

    $parent = $ParentMap[$mapKey]
    if ([string]::IsNullOrWhiteSpace($parent)) { return $current }
    if ($parent.Equals($current, [StringComparison]::OrdinalIgnoreCase)) { return $current }
    if (-not $visited.Add($parent)) { return $current }  # Zyklus erkannt

    $current = $parent
  }

  return $current
}
