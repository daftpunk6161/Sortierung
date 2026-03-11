# REF-PORT-03: Port-Interfaces dienen als Migration-Vorbereitung fuer C# .NET 8 (v2.0).
# Sie werden aktuell NICHT aktiv in der Codebase genutzt, sondern definieren die
# Ziel-Contracts fuer die C#-Migration (Strangler Fig Pattern, s. copilot-instructions.md).
# Validierung via Test-PortContract in Tests. Aktivierung geplant fuer v2.0.

function New-FileSystemPort {
  <# Creates FileSystem port contract backed by existing module functions. #>
  [CmdletBinding(SupportsShouldProcess = $true)]
  param()

  return [pscustomobject]@{
    Name = 'FileSystem'
    TestPath = {
      param([string]$LiteralPath, [string]$PathType = 'Any')
      return (Test-Path -LiteralPath $LiteralPath -PathType $PathType)
    }
    EnsureDirectory = {
      param([string]$Path)
      if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
      if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return (New-Item -ItemType Directory -Path $Path -Force)
      }
      return (Get-Item -LiteralPath $Path)
    }
    GetFilesSafe = {
      param([string]$Root, [object]$AllowedExtensions, [switch]$Responsive)
      return (Get-FilesSafe -Root $Root -AllowedExtensions $AllowedExtensions -Responsive:$Responsive)
    }
    MoveItemSafely = {
      param([string]$Source, [string]$Dest)
      return (Move-ItemSafely -Source $Source -Dest $Dest)
    }
    ResolveChildPathWithinRoot = {
      param([string]$RootPath, [string]$RelativePath)
      return (Resolve-ChildPathWithinRoot -BaseDir $RootPath -ChildPath $RelativePath -Root $RootPath)
    }
  }
}

function New-ToolRunnerPort {
  <# Creates ToolRunner port contract backed by existing module functions. #>
  [CmdletBinding(SupportsShouldProcess = $true)]
  param()

  return [pscustomobject]@{
    Name = 'ToolRunner'
    FindTool = {
      param([string]$ToolName)
      return (Find-ConversionTool -ToolName $ToolName)
    }
    InvokeProcess = {
      param([string]$FilePath, [string[]]$Arguments, [System.Collections.Generic.List[string]]$TempFiles, [scriptblock]$Log, [string]$ErrorLabel)
      return (Invoke-ExternalToolProcess -ToolPath $FilePath -ToolArgs $Arguments -TempFiles $TempFiles -Log $Log -ErrorLabel $ErrorLabel)
    }
    Invoke7z = {
      param([string]$SevenZipPath, [string[]]$Arguments, [System.Collections.Generic.List[string]]$TempFiles)
      return (Invoke-7z -SevenZipPath $SevenZipPath -Arguments $Arguments -TempFiles $TempFiles)
    }
  }
}

function New-DatRepositoryPort {
  <# Creates DatRepository port contract backed by existing module functions. #>
  [CmdletBinding(SupportsShouldProcess = $true)]
  param()

  return [pscustomobject]@{
    Name = 'DatRepository'
    GetDatIndex = {
      param([string]$DatRoot, [hashtable]$ConsoleMap, [string]$HashType = 'SHA1', [scriptblock]$Log)
      return (Get-DatIndex -DatRoot $DatRoot -ConsoleMap $ConsoleMap -HashType $HashType -Log $Log)
    }
    GetDatGameKey = {
      param([string]$GameName, [string]$Console)
      return (Get-DatGameKey -GameName $GameName -Console $Console)
    }
    GetDatParentCloneIndex = {
      param([string]$DatPath)
      return (Get-DatParentCloneIndex -DatPath $DatPath)
    }
    ResolveParentName = {
      param([string]$GameName, [hashtable]$ParentMap)
      return (Resolve-ParentName -GameName $GameName -ParentMap $ParentMap)
    }
  }
}

function New-AuditStorePort {
  <# Creates AuditStore port contract backed by existing module functions. #>
  [CmdletBinding(SupportsShouldProcess = $true)]
  param()

  return [pscustomobject]@{
    Name = 'AuditStore'
    WriteMetadataSidecar = {
      param([string]$AuditCsvPath, [hashtable]$Metadata)
      return (Write-AuditMetadataSidecar -AuditCsvPath $AuditCsvPath -Metadata $Metadata)
    }
    TestMetadataSidecar = {
      param([string]$AuditCsvPath)
      return (Test-AuditMetadataSidecar -AuditCsvPath $AuditCsvPath)
    }
    Rollback = {
      param(
        [string]$AuditCsvPath,
        [string[]]$AllowedRestoreRoots,
        [string[]]$AllowedCurrentRoots,
        [switch]$DryRun,
        [scriptblock]$Log
      )
      return (Invoke-AuditRollback -AuditCsvPath $AuditCsvPath -AllowedRestoreRoots $AllowedRestoreRoots -AllowedCurrentRoots $AllowedCurrentRoots -DryRun:$DryRun -Log $Log)
    }
  }
}

function New-AppStatePort {
  <# Creates AppState/EventBus port contract backed by AppStore helpers.
     Provides both store-level (Get/Set/Watch/Undo/Redo) and key-level
     (GetValue/SetValue/TestCancel) access for dependency injection. #>
  [CmdletBinding(SupportsShouldProcess = $true)]
  param()

  return [pscustomobject]@{
    Name = 'AppState'
    Get = {
      return (Get-AppStore)
    }
    Set = {
      param([hashtable]$Patch, [string]$Reason = 'port-update')
      return (Set-AppStore -Patch $Patch -Reason $Reason)
    }
    Watch = {
      param([scriptblock]$Handler)
      return (Watch-AppStoreChange -Handler $Handler)
    }
    Undo = {
      return (Undo-AppStore)
    }
    Redo = {
      return (Redo-AppStore)
    }
    GetValue = {
      param([string]$Key, $Default = $null)
      return (Get-AppStateValue -Key $Key -Default $Default)
    }
    SetValue = {
      param([string]$Key, $Value)
      return (Set-AppStateValue -Key $Key -Value $Value)
    }
    TestCancel = {
      return (Test-CancelRequested)
    }
  }
}

function Test-PortContract {
  <# Validates that a port object implements all required members.
     Returns @{ IsValid = $true/$false; Missing = @(...) } #>
  param(
    [Parameter(Mandatory)][pscustomobject]$Port,
    [Parameter(Mandatory)][string[]]$RequiredMembers
  )

  $missing = New-Object System.Collections.Generic.List[string]
  foreach ($member in $RequiredMembers) {
    $prop = $Port.PSObject.Properties[$member]
    if (-not $prop -or $null -eq $prop.Value) {
      [void]$missing.Add($member)
    }
  }

  return [pscustomobject]@{
    IsValid = ($missing.Count -eq 0)
    PortName = [string]$Port.Name
    Missing = @($missing)
  }
}

function Assert-OperationPorts {
  <# Validates all ports in a ports hashtable against their contracts.
     Throws on any missing required member. #>
  param(
    [Parameter(Mandatory)][hashtable]$Ports
  )

  $contracts = @{
    FileSystem    = @('TestPath','EnsureDirectory','GetFilesSafe','MoveItemSafely','ResolveChildPathWithinRoot')
    ToolRunner    = @('FindTool','InvokeProcess','Invoke7z')
    DatRepository = @('GetDatIndex','GetDatGameKey','GetDatParentCloneIndex','ResolveParentName')
    AuditStore    = @('WriteMetadataSidecar','TestMetadataSidecar','Rollback')
    AppState      = @('Get','Set','Watch','Undo','Redo','GetValue','SetValue','TestCancel')
    RegionDedupe  = @('Invoke')
  }

  $allErrors = New-Object System.Collections.Generic.List[string]

  foreach ($portName in $contracts.Keys) {
    if (-not $Ports.ContainsKey($portName) -or -not $Ports[$portName]) {
      [void]$allErrors.Add("Port '$portName' fehlt.")
      continue
    }
    $result = Test-PortContract -Port $Ports[$portName] -RequiredMembers $contracts[$portName]
    if (-not $result.IsValid) {
      [void]$allErrors.Add(("Port '{0}' fehlt: {1}" -f $portName, ($result.Missing -join ', ')))
    }
  }

  if ($allErrors.Count -gt 0) {
    throw ("Port-Contract-Verletzung:`n" + ($allErrors -join "`n"))
  }

  return $true
}

function New-OperationPorts {
  <# Creates the default ports contract object for application services.
     Validates all contracts before returning. #>
  [CmdletBinding(SupportsShouldProcess = $true)]
  param([switch]$SkipValidation)

  $ports = @{
    FileSystem = (New-FileSystemPort)
    ToolRunner = (New-ToolRunnerPort)
    DatRepository = (New-DatRepositoryPort)
    AuditStore = (New-AuditStorePort)
    AppState = (New-AppStatePort)
    RegionDedupe = [pscustomobject]@{
      Name = 'RegionDedupe'
      Invoke = {
        param([hashtable]$Parameters)
        return (Invoke-RegionDedupe @Parameters)
      }
    }
  }

  if (-not $SkipValidation) {
    [void](Assert-OperationPorts -Ports $ports)
  }

  return $ports
}
