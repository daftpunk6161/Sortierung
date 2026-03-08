function Get-ScanIndexPath {
  param([string]$Root)
  if (Get-Command Get-ReportsFilePath -ErrorAction SilentlyContinue) {
    return (Get-ReportsFilePath -Root $Root -FileName 'scan-index.json')
  }
  if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Get-Location).Path }
  $reportsDir = Join-Path $Root 'reports'
  return (Join-Path $reportsDir 'scan-index.json')
}

function Import-ScanIndex {
  param([string]$Root)
  $path = Get-ScanIndexPath -Root $Root
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    return [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }

  try {
    $raw = Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $entries = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    if ($raw -and $raw.entries) {
      foreach ($p in $raw.entries.PSObject.Properties) {
        $entries[[string]$p.Name] = $p.Value
      }
    }
    return $entries
  } catch {
    return [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }
}

function Save-ScanIndex {
  param(
    [string]$Root,
    [hashtable]$Entries
  )

  if (-not $Entries) {
    $Entries = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }

  $path = Get-ScanIndexPath -Root $Root
  Assert-DirectoryExists -Path (Split-Path -Parent $path)

  $payload = [ordered]@{
    schemaVersion = 'scan-index-v1'
    updatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    entries = $Entries
  }

  Write-JsonFile -Path $path -Data $payload -Depth 10
  return $path
}

function Get-PathFingerprint {
  param([Parameter(Mandatory=$true)][string]$Path)

  try {
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    return ('{0}|{1}|{2}' -f [string]$file.FullName, [int64]$file.Length, [int64]$file.LastWriteTimeUtc.Ticks)
  } catch {
    return $null
  }
}
