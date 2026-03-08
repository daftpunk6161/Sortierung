function Invoke-CliRunAdapter {
  <# CLI adapter: orchestrates sort + dedupe + conversion via application services. #>
  param(
    [bool]$SortConsole,
    [string]$Mode,
    [string[]]$Roots,
    [string[]]$Extensions,
    [bool]$UseDat,
    [string]$DatRoot,
    [string]$DatHashType,
    [hashtable]$DatMap,
    [hashtable]$ToolOverrides,
    [scriptblock]$Log,
    [bool]$Convert,
    [hashtable]$DedupeParams,
    [hashtable]$Ports
  )

  $consoleSortUnknownReasons = $null
  if ($SortConsole -and $Mode -eq 'Move') {
    $sortResult = Invoke-RunSortService -Enabled $true -Mode $Mode -Roots $Roots -Extensions $Extensions -UseDat $UseDat -DatRoot $DatRoot -DatHashType $DatHashType -DatMap $DatMap -ToolOverrides $ToolOverrides -Log $Log -Ports $Ports
    if ($sortResult -and $sortResult.Value) {
      $consoleSortUnknownReasons = $sortResult.Value
    }
  }

  if ($consoleSortUnknownReasons) {
    $DedupeParams['ConsoleSortUnknownReasons'] = $consoleSortUnknownReasons
  }

  $result = Invoke-RunDedupeService -Parameters $DedupeParams -Ports $Ports

  # Auto folder-level dedupe (PS3/DOS/AMIGA etc.) - runs after region dedupe
  try {
    $folderDedupeResult = Invoke-RunFolderDedupeService `
      -Roots $Roots `
      -Mode $Mode `
      -Log $Log `
      -Ports $Ports

    if ($result -and $folderDedupeResult) {
      $result | Add-Member -NotePropertyName FolderDedupeResult -NotePropertyValue $folderDedupeResult -Force
    }
  } catch {
    if ($Log) { & $Log ("Auto folder-dedupe error: {0}" -f $_.Exception.Message) }
  }

  if ($Convert -and $Mode -eq 'Move' -and $result) {
    Invoke-RunConversionService -Operation 'WinnerMove' -Parameters @{
      Enabled = $true
      Mode = $Mode
      Result = $result
      Roots = $Roots
      ToolOverrides = $ToolOverrides
      Log = $Log
    } -Ports $Ports | Out-Null
  }

  if ($Convert -and $Mode -eq 'DryRun' -and $result) {
    Invoke-RunConversionService -Operation 'Preview' -Parameters @{
      Enabled = $true
      Mode = $Mode
      Result = $result
      Log = $Log
    } -Ports $Ports | Out-Null
  }

  return $result
}

function Register-SchedulerTaskAdapter {
  <# Scheduler adapter: forwards registration to scheduler module. #>
  param(
    [Parameter(Mandatory=$true)][string]$TaskName,
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun',
    [string[]]$Prefer = @('EU','US','WORLD','JP'),
    [string]$Time = '03:00',
    [string]$WorkingDirectory = (Get-Location).Path
  )

  return (Register-RomCleanupScheduledTask -TaskName $TaskName -Roots $Roots -Mode $Mode -Prefer $Prefer -Time $Time -WorkingDirectory $WorkingDirectory)
}


