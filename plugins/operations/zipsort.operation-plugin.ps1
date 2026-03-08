function Invoke-RomCleanupOperationPlugin {
  param(
    [string]$Phase,
    [hashtable]$Context
  )

  if ([string]::IsNullOrWhiteSpace([string]$Phase) -or $Phase -ne 'zip-sort') { return $null }
  if (-not $Context) { return $null }

  $strategy = if ($Context.ContainsKey('Strategy')) { [string]$Context['Strategy'] } else { 'PS1PS2' }
  if ($strategy -ne 'PS1PS2') { return $null }

  $roots = @()
  if ($Context.ContainsKey('Roots')) { $roots = @($Context['Roots']) }
  if ($roots.Count -eq 0) {
    return [pscustomobject]@{ PluginHandled = $true; Total = 0; Moved = 0; Skipped = 0; Errors = 0 }
  }

  $log = $null
  if ($Context.ContainsKey('Log')) { $log = $Context['Log'] }

  $result = Invoke-ZipSortPS1PS2 -Roots $roots -Log $log
  if (-not $result) {
    return [pscustomobject]@{ PluginHandled = $true; Total = 0; Moved = 0; Skipped = 0; Errors = 0 }
  }

  $result | Add-Member -NotePropertyName PluginHandled -NotePropertyValue $true -Force
  return $result
}
