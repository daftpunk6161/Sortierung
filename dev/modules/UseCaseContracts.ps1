<#
.SYNOPSIS
  Versionierte UseCase-Contracts (Input/Output) für alle Kern-UseCases.
  Referenz: docs/implementation/ARCHITECTURE_MAP.md (Abschnitt 4)

.DESCRIPTION
  Jeder UseCase hat typisierte Input/Output-Factories und Validierungsfunktionen.
  Version: v1

.NOTES
  Owner: Core Team
  Layer: Contracts
#>

# ── Contract Version ──
$script:UseCaseContractVersion = 'v1'

# ── RunDedupe Contract ──

function New-RunDedupeInput {
  <# Creates a validated RunDedupe input contract (v1). #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string[]]$Roots,
    [Parameter(Mandatory)][ValidateSet('DryRun','Move')][string]$Mode,
    [string[]]$Prefer = @('EU','US','WORLD','JP'),
    [string[]]$Extensions = @(),
    [bool]$UseDat = $false,
    [string]$DatRoot = '',
    [string]$DatHashType = 'SHA1',
    [hashtable]$DatMap = @{},
    [hashtable]$ToolOverrides = @{}
  )

  if (-not $Roots -or $Roots.Count -eq 0) {
    throw 'RunDedupeInput: Roots darf nicht leer sein.'
  }

  return [pscustomobject]@{
    ContractType    = 'UseCaseContract.RunDedupeInput'
    ContractVersion = $script:UseCaseContractVersion
    Roots         = $Roots
    Mode          = $Mode
    Prefer        = $Prefer
    Extensions    = $Extensions
    UseDat        = $UseDat
    DatRoot       = $DatRoot
    DatHashType   = $DatHashType
    DatMap        = $DatMap
    ToolOverrides = $ToolOverrides
  }
}

function New-RunDedupeOutput {
  <# Creates a RunDedupe output contract (v1). #>
  [CmdletBinding()]
  param(
    [bool]$Success = $false,
    [int]$TotalFiles = 0,
    [int]$WinnerCount = 0,
    [int]$LoserCount = 0,
    [int]$JunkCount = 0,
    [int]$MoveCount = 0,
    [object[]]$Errors = @(),
    [string]$AuditCsvPath = '',
    [timespan]$Duration = [timespan]::Zero,
    [hashtable]$PhaseMetrics = @{}
  )

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.RunDedupeOutput'
    ContractVersion = $script:UseCaseContractVersion
    Success         = $Success
    TotalFiles      = $TotalFiles
    WinnerCount     = $WinnerCount
    LoserCount      = $LoserCount
    JunkCount       = $JunkCount
    MoveCount       = $MoveCount
    Errors          = $Errors
    AuditCsvPath    = $AuditCsvPath
    Duration        = $Duration
    PhaseMetrics    = $PhaseMetrics
  }
}

# ── Preflight Contract ──

function New-PreflightInput {
  <# Creates a validated Preflight input contract (v1). #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string[]]$Roots,
    [Parameter(Mandatory)][ValidateSet('DryRun','Move')][string]$Mode,
    [bool]$UseDat = $false,
    [string]$DatRoot = ''
  )

  if (-not $Roots -or $Roots.Count -eq 0) {
    throw 'PreflightInput: Roots darf nicht leer sein.'
  }

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.PreflightInput'
    ContractVersion = $script:UseCaseContractVersion
    Roots           = $Roots
    Mode            = $Mode
    UseDat          = $UseDat
    DatRoot         = $DatRoot
  }
}

function New-PreflightOutput {
  <# Creates a Preflight output contract (v1). #>
  [CmdletBinding()]
  param(
    [bool]$Valid = $false,
    [object[]]$Checks = @(),
    [object[]]$Errors = @(),
    [string[]]$Warnings = @()
  )

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.PreflightOutput'
    ContractVersion = $script:UseCaseContractVersion
    Valid           = $Valid
    Checks          = $Checks
    Errors          = $Errors
    Warnings        = $Warnings
  }
}

function New-PreflightCheck {
  <# Creates a single preflight check result. #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string]$Name,
    [bool]$Passed = $false,
    [string]$Message = '',
    [string]$FixSuggestion = ''
  )

  return [pscustomobject]@{
    ContractType    = 'UseCaseContract.PreflightCheck'
    Name          = $Name
    Passed        = $Passed
    Message       = $Message
    FixSuggestion = $FixSuggestion
  }
}

# ── Conversion Contract ──

function New-ConversionInput {
  <# Creates a validated Conversion input contract (v1). #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][ValidateSet('WinnerMove','Preview','Standalone')][string]$Operation,
    [bool]$Enabled = $true,
    [Parameter(Mandatory)][ValidateSet('DryRun','Move')][string]$Mode,
    [object]$Result = $null,
    [string[]]$Roots = @(),
    [hashtable]$ToolOverrides = @{},
    [scriptblock]$Log = $null
  )

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.ConversionInput'
    ContractVersion = $script:UseCaseContractVersion
    Operation       = $Operation
    Enabled         = $Enabled
    Mode            = $Mode
    Result          = $Result
    Roots           = $Roots
    ToolOverrides   = $ToolOverrides
    Log             = $Log
  }
}

function New-ConversionOutput {
  <# Creates a Conversion output contract (v1). #>
  [CmdletBinding()]
  param(
    [bool]$Success = $false,
    [int]$ConvertedCount = 0,
    [int]$SkippedCount = 0,
    [int]$FailedCount = 0,
    [object[]]$Errors = @(),
    [timespan]$Duration = [timespan]::Zero
  )

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.ConversionOutput'
    ContractVersion = $script:UseCaseContractVersion
    Success         = $Success
    ConvertedCount  = $ConvertedCount
    SkippedCount    = $SkippedCount
    FailedCount     = $FailedCount
    Errors          = $Errors
    Duration        = $Duration
  }
}

# ── Rollback Contract ──

function New-RollbackInput {
  <# Creates a validated Rollback input contract (v1). #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string]$AuditCsvPath,
    [Parameter(Mandatory)][string[]]$AllowedRestoreRoots,
    [Parameter(Mandatory)][string[]]$AllowedCurrentRoots,
    [bool]$DryRun = $true
  )

  if (-not $AllowedRestoreRoots -or $AllowedRestoreRoots.Count -eq 0) {
    throw 'RollbackInput: AllowedRestoreRoots darf nicht leer sein.'
  }
  if (-not $AllowedCurrentRoots -or $AllowedCurrentRoots.Count -eq 0) {
    throw 'RollbackInput: AllowedCurrentRoots darf nicht leer sein.'
  }

  return [pscustomobject]@{
    ContractType         = 'UseCaseContract.RollbackInput'
    ContractVersion    = $script:UseCaseContractVersion
    AuditCsvPath       = $AuditCsvPath
    AllowedRestoreRoots = $AllowedRestoreRoots
    AllowedCurrentRoots = $AllowedCurrentRoots
    DryRun             = $DryRun
  }
}

function New-RollbackOutput {
  <# Creates a Rollback output contract (v1). #>
  [CmdletBinding()]
  param(
    [bool]$Success = $false,
    [int]$RestoredCount = 0,
    [int]$SkippedCount = 0,
    [int]$FailedCount = 0,
    [object[]]$Errors = @()
  )

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.RollbackOutput'
    ContractVersion = $script:UseCaseContractVersion
    Success         = $Success
    RestoredCount   = $RestoredCount
    SkippedCount    = $SkippedCount
    FailedCount     = $FailedCount
    Errors          = $Errors
  }
}

# ── Report Contract ──

function New-ReportInput {
  <# Creates a validated Report input contract (v1). #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][ValidateSet('Summary','Diff','Audit','KPI')][string]$Type,
    [Parameter(Mandatory)][object]$SourceData,
    [string]$OutputPath = '',
    [ValidateSet('JSON','Markdown','CSV')][string]$Format = 'JSON'
  )

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.ReportInput'
    ContractVersion = $script:UseCaseContractVersion
    Type            = $Type
    SourceData      = $SourceData
    OutputPath      = $OutputPath
    Format          = $Format
  }
}

function New-ReportOutput {
  <# Creates a Report output contract (v1). #>
  [CmdletBinding()]
  param(
    [bool]$Success = $false,
    [string]$OutputPath = '',
    [object[]]$Errors = @()
  )

  return [pscustomobject]@{
    ContractType      = 'UseCaseContract.ReportOutput'
    ContractVersion = $script:UseCaseContractVersion
    Success         = $Success
    OutputPath      = $OutputPath
    Errors          = $Errors
  }
}

# ── Contract Validation Helpers ──

function Test-UseCaseContract {
  <# Validates that an object matches a UseCase contract type. #>
  [CmdletBinding()]
  param(
    [object]$Object,
    [Parameter(Mandatory)][string]$ExpectedType
  )

  if (-not $Object) {
    return [pscustomobject]@{ Valid = $false; Reason = 'Object is null' }
  }

  $typeName = ''
  if ($Object.PSObject.Properties.Name -contains 'ContractType') {
    $typeName = $Object.ContractType
  }
  if ($typeName -ne $ExpectedType) {
    return [pscustomobject]@{ Valid = $false; Reason = "Expected type '$ExpectedType', got '$typeName'" }
  }

  $version = ''
  if ($Object.PSObject.Properties.Name -contains 'ContractVersion') {
    $version = $Object.ContractVersion
  }
  if ($version -ne $script:UseCaseContractVersion) {
    return [pscustomobject]@{ Valid = $false; Reason = "Contract version mismatch: expected '$($script:UseCaseContractVersion)', got '$version'" }
  }

  return [pscustomobject]@{ Valid = $true; Reason = '' }
}

function Assert-UseCaseContract {
  <# Validates a UseCase contract and throws on mismatch. #>
  [CmdletBinding()]
  param(
    [object]$Object,
    [Parameter(Mandatory)][string]$ExpectedType
  )

  $result = Test-UseCaseContract -Object $Object -ExpectedType $ExpectedType
  if (-not $result.Valid) {
    throw "UseCase-Contract-Verletzung: $($result.Reason)"
  }
}

function Get-UseCaseContractVersion {
  <# Returns the current contract version. #>
  return $script:UseCaseContractVersion
}

