function Invoke-RomCleanupOperationPlugin {
  param(
    [string]$Phase,
    [hashtable]$Context
  )

  if ([string]::IsNullOrWhiteSpace($Phase) -or $Phase -ne 'post-run') {
    return [pscustomobject]@{ handled = $false }
  }

  $summary = [ordered]@{
    mode = if ($Context -and $Context.ContainsKey('Mode')) { [string]$Context.Mode } else { '' }
    reportRows = if ($Context -and $Context.ContainsKey('ReportRows')) { [int]@($Context.ReportRows).Count } else { 0 }
    totalDupes = if ($Context -and $Context.ContainsKey('TotalDupes')) { [int]$Context.TotalDupes } else { 0 }
  }

  return [pscustomobject]@{
    handled = $true
    plugin = 'example.operation-plugin'
    summary = $summary
  }
}
