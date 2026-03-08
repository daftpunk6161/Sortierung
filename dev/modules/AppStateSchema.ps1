# ================================================================
#  AppStateSchema.ps1 – TD-007: Typed AppState Contracts
# ================================================================
# Defines the typed schema for all AppState keys, enforces type
# validation on Set, and adds transition-reason tracking to
# Set-AppStateValue.
# ================================================================

function Get-AppStateSchema {
  <#
    .SYNOPSIS
      Returns the typed schema definition for all known AppState keys.
    .DESCRIPTION
      Each entry defines: Type (PowerShell type name), Default, ReadOnly,
      and optional ValidValues for enum-like keys.
  #>
  return [ordered]@{
    RunState                    = @{ Type = 'string';   Default = 'Idle'; ValidValues = @('Idle','Starting','Running','Canceling','Completed','Failed','Canceled') }
    CancelRequested             = @{ Type = 'bool';     Default = $false }
    OperationInProgress         = @{ Type = 'bool';     Default = $false }
    HeartbeatFrame              = @{ Type = 'int';      Default = 0 }
    CurrentPhase                = @{ Type = 'string';   Default = 'Bereit' }
    QuickTone                   = @{ Type = 'string';   Default = 'idle' }
    OperationJsonlPath          = @{ Type = 'string';   Default = $null }
    UseAggressiveJunk           = @{ Type = 'bool';     Default = $false }
    ModuleLoadError             = @{ Type = 'object';   Default = $null }
    LogMaxLines                 = @{ Type = 'int';      Default = 2000 }
    LogTrimTo                   = @{ Type = 'int';      Default = 1500 }
    OperationJsonlMaxBytes      = @{ Type = 'int';      Default = 5242880 }
    OperationJsonlKeepFiles     = @{ Type = 'int';      Default = 10 }
    UiTelemetryMaxBytes         = @{ Type = 'int';      Default = 5242880 }
    UiTelemetryKeepFiles        = @{ Type = 'int';      Default = 7 }
    DatStreamThresholdBytes     = @{ Type = 'int';      Default = 20971520 }
    DatIndexCacheEnabled        = @{ Type = 'bool';     Default = $true }
    GameKeyCacheMaxEntries      = @{ Type = 'int';      Default = 50000 }
    ScanSqliteThreshold         = @{ Type = 'int';      Default = 10000 }
    ParallelClassifyThreshold   = @{ Type = 'int';      Default = 2000 }
    ParallelHashThreshold       = @{ Type = 'int';      Default = 200 }
    HtmlReportStreamingThresholdRows = @{ Type = 'int'; Default = 100000 }
    HtmlReportFlushEveryRows    = @{ Type = 'int';      Default = 2000 }
    UILocale                    = @{ Type = 'string';   Default = 'de'; ValidValues = @('de','en') }
    ConversionBackupEnabled     = @{ Type = 'bool';     Default = $true }
    ConversionBackupRetentionDays = @{ Type = 'int';    Default = 7 }
    DatXmlMaxCharactersInDocument = @{ Type = 'int';    Default = 524288000 }
    UITheme                     = @{ Type = 'string';   Default = 'dark'; ValidValues = @('dark','light') }
    PluginTrustMode             = @{ Type = 'string';   Default = 'trusted-only'; ValidValues = @('compat','trusted-only','signed-only') }
    PluginExecutionTimeoutMs    = @{ Type = 'int';      Default = 15000 }
    PluginMaxResultBytes        = @{ Type = 'int';      Default = 1048576 }
    SecurityEventPath           = @{ Type = 'string';   Default = $null }
    AllowInsecureToolHashBypass = @{ Type = 'bool';     Default = $false }
    WpfOperationState           = @{ Type = 'object';   Default = $null }
    LastUnhandledWpfException   = @{ Type = 'string';   Default = $null }
  }
}

function Assert-AppStateValue {
  <#
    .SYNOPSIS
      Validates and optionally coerces a value for an AppState key.
    .DESCRIPTION
      Returns the (possibly coerced) value, or throws on invalid type.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$Key,
    [AllowNull()]$Value
  )

  $schema = Get-AppStateSchema
  if (-not $schema.Contains($Key)) { return $Value }

  $spec = $schema[$Key]
  if ($null -eq $Value) { return $Value }

  $expectedType = [string]$spec.Type

  switch ($expectedType) {
    'string' {
      $coerced = [string]$Value
      if ($spec.ContainsKey('ValidValues') -and $spec.ValidValues.Count -gt 0) {
        if ($spec.ValidValues -notcontains $coerced) {
          throw ("AppState key '{0}': value '{1}' not in allowed set ({2})" -f $Key, $coerced, ($spec.ValidValues -join ', '))
        }
      }
      return $coerced
    }
    'bool' {
      if ($Value -is [bool]) { return $Value }
      return [bool]$Value
    }
    'int' {
      if ($Value -is [int]) { return $Value }
      return [int]$Value
    }
    default {
      return $Value
    }
  }
}
