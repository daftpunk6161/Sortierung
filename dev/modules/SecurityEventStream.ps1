# ================================================================
#  SecurityEventStream.ps1 – TD-010: Centralized Security Events
# ================================================================
# Provides Write-SecurityEvent for structured, persistent security
# event logging with forensic correlation. Integrates with EventBus.
# ================================================================

$script:SecurityEventLog = $null
$script:SecurityEventPath = $null
$script:SecurityCorrelationId = $null
$script:SecurityEventLogMaxEntries = 10000

function Initialize-SecurityEventStream {
  [CmdletBinding()]
  param(
    [string]$Path,
    [string]$CorrelationId
  )

  if (-not $script:SecurityEventLog) {
    $script:SecurityEventLog = New-Object System.Collections.Generic.List[object]
  }

  $resolvedPath = [string]$Path
  if ([string]::IsNullOrWhiteSpace($resolvedPath) -and (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue)) {
    try {
      $resolvedPath = [string](Get-AppStateValue -Key 'SecurityEventPath' -Default $null)
    } catch {
      $resolvedPath = $null
    }
  }
  if ([string]::IsNullOrWhiteSpace($resolvedPath)) {
    $resolvedPath = Join-Path (Join-Path (Get-Location).Path 'reports') 'security-events-latest.jsonl'
  }

  try {
    $parent = Split-Path -Parent $resolvedPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
      Assert-DirectoryExists -Path $parent
    }
  } catch {
    # Directory creation for security log path is best-effort.
    Write-Warning ('[SecurityEvent] Verzeichnis-Erstellung fehlgeschlagen: {0}' -f $_.Exception.Message)
  }
  $script:SecurityEventPath = $resolvedPath

  $resolvedCorrelationId = [string]$CorrelationId
  if ([string]::IsNullOrWhiteSpace($resolvedCorrelationId) -and (Get-Command Get-OperationCorrelationId -ErrorAction SilentlyContinue)) {
    try {
      $resolvedCorrelationId = [string](Get-OperationCorrelationId)
    } catch {
      $resolvedCorrelationId = $null
    }
  }
  if (-not [string]::IsNullOrWhiteSpace($resolvedCorrelationId)) {
    $script:SecurityCorrelationId = $resolvedCorrelationId
  }

  return [pscustomobject]@{
    Path = [string]$script:SecurityEventPath
    CorrelationId = [string]$script:SecurityCorrelationId
  }
}

function Write-SecurityAuditEvent {
  [CmdletBinding()]
  param(
    [ValidateSet('Auth','Move','Plugin','ToolHash')][string]$Domain,
    [Parameter(Mandatory=$true)][string]$Action,
    [string]$Actor = '',
    [string]$Target = '',
    [ValidateSet('Allow','Deny','Caught','Error','Info','Warn')][string]$Outcome = 'Info',
    [string]$Detail = '',
    [string]$Source = '',
    [ValidateSet('Low','Medium','High','Critical')][string]$Severity = 'Medium'
  )

  $domainPrefix = switch ([string]$Domain) {
    'Auth' { 'Auth' }
    'Move' { 'Move' }
    'Plugin' { 'Plugin' }
    'ToolHash' { 'ToolHash' }
    default { 'Security' }
  }

  $eventType = '{0}.{1}' -f $domainPrefix, ([string]$Action -replace '\s+', '')
  Write-SecurityEvent -EventType $eventType -Actor $Actor -Target $Target -Outcome $Outcome -Detail $Detail -Source $Source -Severity $Severity
}

function Write-SecurityEvent {
  <#
    .SYNOPSIS
      Writes a structured security event to the centralized stream.
    .PARAMETER EventType
      Category of security event. Examples:
        Auth.ApiKeyReject, Auth.ApiKeyAccept,
        Plugin.Rejected, Plugin.Loaded,
        Move.Blocked, Move.Completed,
        Hash.Mismatch, Hash.Validated,
        Tool.Untrusted, Tool.Validated,
        Dat.SignatureInvalid, Dat.SignatureValid,
        CatchGuard.Security, Audit.IntegrityFail
    .PARAMETER Actor
      Who/what triggered the event (e.g. user, plugin name, API client).
    .PARAMETER Target
      What was affected (e.g. file path, plugin id, API endpoint).
    .PARAMETER Outcome
      Allow / Deny / Caught / Error / Info
    .PARAMETER Detail
      Human-readable description.
    .PARAMETER Source
      Code module / function that emitted the event.
    .PARAMETER Severity
      Low / Medium / High / Critical
  #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)]
    [string]$EventType,

    [string]$Actor = '',
    [string]$Target = '',

    [ValidateSet('Allow','Deny','Caught','Error','Info','Warn')]
    [string]$Outcome = 'Info',

    [string]$Detail = '',
    [string]$Source = '',

    [ValidateSet('Low','Medium','High','Critical')]
    [string]$Severity = 'Medium'
  )

  if ($null -eq $script:SecurityEventLog -or [string]::IsNullOrWhiteSpace($script:SecurityEventPath)) {
    [void](Initialize-SecurityEventStream)
  }

  $event = [pscustomobject]@{
    TimestampUtc   = (Get-Date).ToUniversalTime().ToString('o')
    CorrelationId  = if ($script:SecurityCorrelationId) { $script:SecurityCorrelationId } else { '' }
    EventType      = [string]$EventType
    Actor          = [string]$Actor
    Target         = [string]$Target
    Outcome        = [string]$Outcome
    Severity       = [string]$Severity
    Detail         = [string]$Detail
    Source         = [string]$Source
    ProcessId      = [int]$PID
  }

  # In-memory buffer (ring-buffer: trim oldest entries when exceeding max)
  if ($null -ne $script:SecurityEventLog) {
    [void]$script:SecurityEventLog.Add($event)
    if ($script:SecurityEventLog.Count -gt $script:SecurityEventLogMaxEntries) {
      $excess = $script:SecurityEventLog.Count - $script:SecurityEventLogMaxEntries
      $script:SecurityEventLog.RemoveRange(0, $excess)
    }
  }

  # Persistent JSONL append
  if (-not [string]::IsNullOrWhiteSpace($script:SecurityEventPath)) {
    try {
      $json = $event | ConvertTo-Json -Depth 5 -Compress
      [System.IO.File]::AppendAllText($script:SecurityEventPath, $json + [Environment]::NewLine)
    } catch {
      Write-Warning ('[SecurityEvent] Failed to persist event: {0}' -f $_.Exception.Message)
    }
  }

  # Publish to EventBus if available
  if (Get-Command Publish-RomEvent -ErrorAction SilentlyContinue) {
    try {
      $topic = 'Security.{0}' -f ($EventType -replace '\.', '_')
      Publish-RomEvent -Topic $topic -Data @{
        EventType = $EventType
        Actor = $Actor
        Target = $Target
        Outcome = $Outcome
        Severity = $Severity
      } -Source $Source -ContinueOnError
    } catch { } # EventBus failure must not break security flow
  }
}

function Get-SecurityEventLog {
  <# Returns all in-memory security events since initialization. #>
  if ($null -eq $script:SecurityEventLog) { return @() }
  return $script:SecurityEventLog.ToArray()
}

# ================================================================
#  CSP – Content Security Policy for HTML Reports
# ================================================================

function Get-HtmlReportSecurityHeaders {
  <# Returns a Content-Security-Policy meta tag and nonce for HTML reports.
     BUG REPORT-010 FIX: Replaced 'unsafe-inline' with nonce-based script-src.
     Returns a PSCustomObject with CspTag and Nonce properties. #>
  $nonceBytes = [byte[]]::new(16)
  $rng = [System.Security.Cryptography.RNGCryptoServiceProvider]::new()
  try { $rng.GetBytes($nonceBytes) } finally { $rng.Dispose() }
  $nonce = [Convert]::ToBase64String($nonceBytes)
  return [pscustomobject]@{
    CspTag = ('<meta http-equiv="Content-Security-Policy" content="default-src ''none''; style-src ''unsafe-inline''; script-src ''nonce-{0}''; img-src data:; font-src data:;">' -f $nonce)
    Nonce  = $nonce
  }
}

