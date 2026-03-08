# ================================================================
#  LruCache.ps1 - Generic LRU Cache Wrapper
#  Provides a reusable LRU-eviction cache to replace inline
#  implementations in Tools.ps1 (Archive) and Dat.ps1 (FileHash).
# ================================================================

function New-LruCache {
  <#
  .SYNOPSIS
    Erstellt eine neue LRU-Cache-Instanz.
  .PARAMETER MaxEntries
    Maximale Anzahl Einträge (Standard: 5000).
  .PARAMETER Name
    Optionaler Name für Statistik-Reporting.
  #>
  param(
    [int]$MaxEntries = 5000,
    [string]$Name = 'LruCache'
  )

  # Order: LinkedList<string>  - head=LRU, tail=MRU
  # Nodes: Dictionary<string, LinkedListNode<string>> - O(1) node lookup for touch
  $order = [System.Collections.Generic.LinkedList[string]]::new()
  $nodes = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.LinkedListNode[string]]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
  )

  return [pscustomobject]@{
    Name       = [string]$Name
    MaxEntries = [int][math]::Max(2, $MaxEntries)
    Data       = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    Order      = $order
    Nodes      = $nodes
    Hits       = [long]0
    Misses     = [long]0
    Evictions  = [long]0
  }
}

function Get-LruCacheValue {
  <#
  .SYNOPSIS
    Liest einen Wert aus dem Cache und aktualisiert die LRU-Reihenfolge.
  .OUTPUTS
    Den gecachten Wert oder $null bei Cache-Miss.
  #>
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Cache,
    [Parameter(Mandatory=$true)][AllowEmptyString()][string]$Key
  )

  if ([string]::IsNullOrWhiteSpace($Key) -or -not $Cache.Data.ContainsKey($Key)) {
    $Cache.Misses++
    return $null
  }

  # O(1) touch: remove existing node, re-add as MRU tail
  $node = $null
  if ($Cache.Nodes.TryGetValue($Key, [ref]$node) -and $node) {
    $Cache.Order.Remove($node)                    # O(1) - node reference known
    $newNode = $Cache.Order.AddLast($Key)         # O(1)
    $Cache.Nodes[$Key] = $newNode
  }
  $Cache.Hits++
  return $Cache.Data[$Key]
}

function Set-LruCacheValue {
  <#
  .SYNOPSIS
    Setzt einen Wert im Cache. Bei Überschreitung des Limits wird das älteste Element entfernt.
  #>
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Cache,
    [Parameter(Mandatory=$true)][AllowEmptyString()][string]$Key,
    [Parameter(Mandatory=$true)][AllowNull()]$Value
  )

  if ([string]::IsNullOrWhiteSpace($Key)) { return }  # ignore empty/whitespace keys
  $Cache.Data[$Key] = $Value

  # O(1) touch: if key already present, remove its existing node first
  $existingNode = $null
  if ($Cache.Nodes.TryGetValue($Key, [ref]$existingNode) -and $existingNode) {
    $Cache.Order.Remove($existingNode)            # O(1) - node reference known
  }
  $newNode = $Cache.Order.AddLast($Key)           # O(1)
  $Cache.Nodes[$Key] = $newNode

  # Evict LRU entries if over limit - O(1) per eviction
  while ($Cache.Data.Count -gt $Cache.MaxEntries -and $Cache.Order.Count -gt 0) {
    $lruNode = $Cache.Order.First
    if (-not $lruNode) { break }
    $oldest = $lruNode.Value
    $Cache.Order.RemoveFirst()                    # O(1)
    [void]$Cache.Nodes.Remove($oldest)
    if (-not [string]::IsNullOrWhiteSpace($oldest) -and $Cache.Data.ContainsKey($oldest)) {
      [void]$Cache.Data.Remove($oldest)
      $Cache.Evictions++
    }
  }
}

function Test-LruCacheContains {
  <#
  .SYNOPSIS
    Prüft ob ein Schlüssel im Cache vorhanden ist (ohne Touch).
  #>
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Cache,
    [Parameter(Mandatory=$true)][AllowEmptyString()][string]$Key
  )

  return $Cache.Data.ContainsKey($Key)
}

function Reset-LruCache {
  <#
  .SYNOPSIS
    Leert den gesamten Cache und setzt Statistiken zurück.
  #>
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Cache
  )

  $Cache.Data.Clear()
  $Cache.Order.Clear()
  $Cache.Nodes.Clear()
  $Cache.Hits = [long]0
  $Cache.Misses = [long]0
  $Cache.Evictions = [long]0
}

function Get-LruCacheStatistics {
  <#
  .SYNOPSIS
    Gibt Cache-Statistiken zurück: Größe, Hits, Misses, Hit-Rate, Evictions.
  #>
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Cache
  )

  $total = $Cache.Hits + $Cache.Misses
  $hitRate = if ($total -gt 0) { [math]::Round(($Cache.Hits / $total) * 100, 1) } else { 0.0 }

  return [pscustomobject]@{
    Name       = [string]$Cache.Name
    Count      = [int]$Cache.Data.Count
    MaxEntries = [int]$Cache.MaxEntries
    Hits       = [long]$Cache.Hits
    Misses     = [long]$Cache.Misses
    HitRate    = [double]$hitRate
    Evictions  = [long]$Cache.Evictions
  }
}

# ================================================================
#  GLOBAL CACHE REGISTRY - für zentrale Statistik-Abfrage
# ================================================================

$script:LRU_CACHE_REGISTRY = New-Object System.Collections.Generic.List[pscustomobject]

function Register-LruCache {
  <#
  .SYNOPSIS
    Registriert einen LRU-Cache für zentrale Statistik-Abfrage.
  #>
  param(
    [Parameter(Mandatory=$true)][pscustomobject]$Cache
  )

  # Duplikate vermeiden
  $existing = $script:LRU_CACHE_REGISTRY | Where-Object { $_.Name -eq $Cache.Name } | Select-Object -First 1
  if (-not $existing) {
    [void]$script:LRU_CACHE_REGISTRY.Add($Cache)
  }
}

function Get-AllCacheStatistics {
  <#
  .SYNOPSIS
    Gibt Statistiken aller registrierten LRU-Caches zurück.
  .DESCRIPTION
    Sammelt Hit/Miss/Size-Daten aller per Register-LruCache angemeldeten Caches.
    Ideal für Diagnostics-Dashboard.
  #>
  $results = New-Object System.Collections.Generic.List[pscustomobject]
  foreach ($cache in $script:LRU_CACHE_REGISTRY) {
    if ($cache) {
      [void]$results.Add((Get-LruCacheStatistics -Cache $cache))
    }
  }
  return @($results)
}
