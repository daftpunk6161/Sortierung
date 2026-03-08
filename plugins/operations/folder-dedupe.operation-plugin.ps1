function Invoke-RomCleanupOperationPlugin {
  param(
    [string]$Phase,
    [hashtable]$Context
  )

  if ([string]::IsNullOrWhiteSpace([string]$Phase) -or $Phase -ne 'folder-dedupe') { return $null }
  if (-not $Context) { return $null }

  $roots = @()
  if ($Context.ContainsKey('Roots')) { $roots = @($Context['Roots']) }
  if ($roots.Count -eq 0) {
    return [pscustomobject]@{
      PluginHandled = $true
      TotalFolders  = 0
      DupeGroups    = 0
      Moved         = 0
      Skipped       = 0
      Errors        = 0
      Mode          = 'DryRun'
      Actions       = @()
    }
  }

  $dupeRoot = $null
  if ($Context.ContainsKey('DupeRoot')) { $dupeRoot = [string]$Context['DupeRoot'] }
  $mode = 'DryRun'
  if ($Context.ContainsKey('Mode')) { $mode = [string]$Context['Mode'] }
  $log = $null
  if ($Context.ContainsKey('Log')) { $log = $Context['Log'] }

  $result = Invoke-FolderDedupeByBaseName -Roots $roots -DupeRoot $dupeRoot -Mode $mode -Log $log
  if (-not $result) {
    return [pscustomobject]@{
      PluginHandled = $true
      TotalFolders  = 0
      DupeGroups    = 0
      Moved         = 0
      Skipped       = 0
      Errors        = 0
      Mode          = $mode
      Actions       = @()
    }
  }

  $result | Add-Member -NotePropertyName PluginHandled -NotePropertyValue $true -Force
  return $result
}
