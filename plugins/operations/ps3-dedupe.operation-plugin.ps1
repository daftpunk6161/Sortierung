function Invoke-RomCleanupOperationPlugin {
  param(
    [string]$Phase,
    [hashtable]$Context
  )

  if ([string]::IsNullOrWhiteSpace([string]$Phase) -or $Phase -ne 'ps3-dedupe') { return $null }
  if (-not $Context) { return $null }

  $roots = @()
  if ($Context.ContainsKey('Roots')) { $roots = @($Context['Roots']) }
  if ($roots.Count -eq 0) {
    return [pscustomobject]@{ PluginHandled = $true; Total = 0; Dupes = 0; Moved = 0; Skipped = 0 }
  }

  $dupeRoot = $null
  if ($Context.ContainsKey('DupeRoot')) { $dupeRoot = [string]$Context['DupeRoot'] }
  $log = $null
  if ($Context.ContainsKey('Log')) { $log = $Context['Log'] }

  $result = Invoke-PS3FolderDedupe -Roots $roots -DupeRoot $dupeRoot -Log $log
  if (-not $result) {
    return [pscustomobject]@{ PluginHandled = $true; Total = 0; Dupes = 0; Moved = 0; Skipped = 0 }
  }

  $result | Add-Member -NotePropertyName PluginHandled -NotePropertyValue $true -Force
  return $result
}
