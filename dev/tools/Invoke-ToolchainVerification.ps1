[CmdletBinding()]
param(
  [string[]]$ToolPaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ToolPaths -or $ToolPaths.Count -eq 0) {
  $ToolPaths = @('chdman.exe','7z.exe','dolphin-tool.exe','ciso.exe','pbp2chd.exe')
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($tool in $ToolPaths) {
  $resolved = $null
  try { $resolved = (Get-Command $tool -ErrorAction Stop).Source } catch { }
  if (-not $resolved) {
    [void]$rows.Add([pscustomobject]@{ Tool=$tool; Found=$false; Path=''; SHA256=''; Signature='n/a' })
    continue
  }

  $hash = ''
  $signature = 'unknown'
  try { $hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256 -ErrorAction Stop).Hash } catch { }
  try {
    $sig = Get-AuthenticodeSignature -FilePath $resolved -ErrorAction Stop
    $signature = [string]$sig.Status
  } catch { }

  [void]$rows.Add([pscustomobject]@{ Tool=$tool; Found=$true; Path=$resolved; SHA256=$hash; Signature=$signature })
}

[pscustomobject]@{
  Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  Tools = $rows.ToArray()
}
