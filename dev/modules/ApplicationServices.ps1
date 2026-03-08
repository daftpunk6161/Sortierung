function Resolve-ApplicationPorts {
  <# Resolve optional ports; fallback to default OperationPorts when available. #>
  param([hashtable]$Ports)

  if ($Ports) { return $Ports }
  if (Get-Command New-OperationPorts -ErrorAction SilentlyContinue) {
    return (New-OperationPorts)
  }
  return @{}
}

function Test-ApplicationPortKeys {
  <# Validate required port keys when a port map is supplied. #>
  param(
    [hashtable]$Ports,
    [string[]]$RequiredKeys
  )

  if (-not $Ports -or $Ports.Count -eq 0 -or -not $RequiredKeys) { return }
  foreach ($key in $RequiredKeys) {
    if (-not $Ports.ContainsKey($key) -or -not $Ports[$key]) {
      throw "Port-Contract verletzt: '$key' fehlt."
    }
  }
}

function Invoke-RunDedupeService {
  <# Application service facade for core region dedupe runs. #>
  param(
    [Parameter(Mandatory = $true)][hashtable]$Parameters,
    [hashtable]$Ports
  )

  $activePorts = Resolve-ApplicationPorts -Ports $Ports
  Test-ApplicationPortKeys -Ports $activePorts -RequiredKeys @('FileSystem','ToolRunner','DatRepository','AuditStore')

  if ($activePorts.ContainsKey('RegionDedupe') -and $activePorts['RegionDedupe']) {
    $regionDedupePort = $activePorts['RegionDedupe']
    if (($regionDedupePort.PSObject.Properties.Name -contains 'Invoke') -and $regionDedupePort.Invoke) {
      return (& $regionDedupePort.Invoke $Parameters)
    }
  }

  return (Invoke-RegionDedupe @Parameters)
}

function Invoke-RunSortService {
  <# Application service facade for optional pre-dedupe console sorting. #>
  param(
    [bool]$Enabled,
    [string]$Mode,
    [string[]]$Roots,
    [string[]]$Extensions,
    [bool]$UseDat,
    [string]$DatRoot,
    [string]$DatHashType,
    [hashtable]$DatMap,
    [hashtable]$ToolOverrides,
    [ValidateSet('None','PS1PS2')][string]$ZipSortStrategy = 'None',
    [scriptblock]$Log,
    [hashtable]$Ports
  )

  $activePorts = Resolve-ApplicationPorts -Ports $Ports
  Test-ApplicationPortKeys -Ports $activePorts -RequiredKeys @('FileSystem','ToolRunner')

  return (Invoke-OptionalConsoleSort -Enabled $Enabled -Mode $Mode -Roots $Roots -Extensions $Extensions -UseDat $UseDat -DatRoot $DatRoot -DatHashType $DatHashType -DatMap $DatMap -ToolOverrides $ToolOverrides -ZipSortStrategy $ZipSortStrategy -Log $Log)
}

function Invoke-RunConversionService {
  <# Application service facade for conversion operations. #>
  param(
    [ValidateSet('WinnerMove', 'Preview', 'Standalone')]
    [string]$Operation,
    [hashtable]$Parameters = @{},
    [hashtable]$Ports
  )

  $activePorts = Resolve-ApplicationPorts -Ports $Ports
  Test-ApplicationPortKeys -Ports $activePorts -RequiredKeys @('FileSystem','ToolRunner')

  switch ($Operation) {
    'WinnerMove' {
      return (Invoke-WinnerConversionMove @Parameters)
    }
    'Preview' {
      return (Invoke-ConvertPreviewDryRun @Parameters)
    }
    'Standalone' {
      return (Invoke-StandaloneConversion @Parameters)
    }
    default {
      throw "Unbekannte Conversion-Operation: $Operation"
    }
  }
}

function Invoke-RunRollbackService {
  <# Application service facade for audit rollback operations. #>
  param(
    [Parameter(Mandatory = $true)][hashtable]$Parameters,
    [hashtable]$Ports
  )

  $activePorts = Resolve-ApplicationPorts -Ports $Ports
  Test-ApplicationPortKeys -Ports $activePorts -RequiredKeys @('AuditStore')

  $auditStore = $null
  if ($activePorts -and $activePorts.ContainsKey('AuditStore')) {
    $auditStore = $activePorts['AuditStore']
  }
  if ($auditStore -and ($auditStore.PSObject.Properties.Name -contains 'Rollback') -and $auditStore.Rollback) {
    return (& $auditStore.Rollback @Parameters)
  }

  return (Invoke-AuditRollback @Parameters)
}

function Invoke-RunFolderDedupeService {
  <# Application service facade for automatic folder-level deduplication.
     Auto-detects console type per root and dispatches to PS3 hash-based
     or base-name folder dedupe as appropriate. #>
  param(
    [Parameter(Mandatory = $true)][string[]]$Roots,
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun',
    [string]$DupeRoot,
    [scriptblock]$Log,
    [hashtable]$Ports
  )

  $activePorts = Resolve-ApplicationPorts -Ports $Ports

  if ($activePorts.ContainsKey('FolderDedupe') -and $activePorts['FolderDedupe']) {
    $port = $activePorts['FolderDedupe']
    if (($port.PSObject.Properties.Name -contains 'Invoke') -and $port.Invoke) {
      return (& $port.Invoke @{ Roots = $Roots; Mode = $Mode; DupeRoot = $DupeRoot; Log = $Log })
    }
  }

  return (Invoke-AutoFolderDedupe -Roots $Roots -Mode $Mode -DupeRoot $DupeRoot -Log $Log)
}

