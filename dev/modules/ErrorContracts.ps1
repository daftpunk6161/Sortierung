function Resolve-OperationErrorCategory {
  <# Maps error codes to stable categories for structured error hierarchy. #>
  param(
    [string]$ErrorCode,
    [string]$Fallback = 'General'
  )

  $code = [string]$ErrorCode
  if ([string]::IsNullOrWhiteSpace($code)) { return [string]$Fallback }

  if ($code -like 'GUI-*') { return 'UI' }
  if ($code -like 'DAT-*') { return 'Data' }
  if ($code -like 'TOOL-*') { return 'Tooling' }
  if ($code -like 'IO-*') { return 'FileSystem' }
  if ($code -like 'CFG-*') { return 'Configuration' }
  if ($code -like 'SEC-*') { return 'Security' }
  if ($code -like 'RUN-*') { return 'Pipeline' }

  switch ($code) {
    'UNHANDLED_EXCEPTION' { return 'General' }
    'RUN_ERROR' { return 'Pipeline' }
    default { return [string]$Fallback }
  }
}

function New-OperationError {
  <# Creates a normalized operation error object contract. #>
  param(
    [string]$ErrorCode = 'GENERIC_ERROR',
    [string]$Category,
    [ValidateSet('Info','Warning','Error','Critical')]
    [string]$Severity = 'Error',
    [Parameter(Mandatory=$true)][string]$Message,
    [string]$ActionHint = 'Bitte prüfe Eingaben/Tools und versuche es erneut.',
    [AllowNull()][object]$Context = $null,
    [AllowNull()][System.Exception]$Exception = $null,
    [AllowNull()][string]$ScriptStackTrace = $null
  )

  $resolvedCategory = if ([string]::IsNullOrWhiteSpace($Category)) {
    Resolve-OperationErrorCategory -ErrorCode $ErrorCode
  } else {
    [string]$Category
  }

  return [pscustomobject]@{
    ErrorCode = [string]$ErrorCode
    Category = [string]$resolvedCategory
    Severity = [string]$Severity
    Message = [string]$Message
    ActionHint = [string]$ActionHint
    Context = $Context
    ExceptionType = if ($Exception) { [string]$Exception.GetType().FullName } else { $null }
    ExceptionMessage = if ($Exception) { [string]$Exception.Message } else { $null }
    ScriptStackTrace = $ScriptStackTrace
    TimestampUtc = (Get-Date).ToUniversalTime().ToString('o')
  }
}

function ConvertTo-OperationError {
  <# Converts an exception into the normalized operation error contract. #>
  param(
    [AllowNull()][System.Exception]$Exception,
    [string]$ErrorCode = 'UNHANDLED_EXCEPTION',
    [string]$Category,
    [ValidateSet('Info','Warning','Error','Critical')]
    [string]$Severity = 'Error',
    [string]$ActionHint = 'Bitte prüfe Eingaben/Tools und versuche es erneut.',
    [AllowNull()][object]$Context = $null,
    [AllowNull()][string]$ScriptStackTrace = $null
  )

  $message = if ($Exception -and $Exception.Message) { [string]$Exception.Message } else { 'Unbekannter Fehler.' }
  return (New-OperationError -ErrorCode $ErrorCode -Category $Category -Severity $Severity -Message $message -ActionHint $ActionHint -Context $Context -Exception $Exception -ScriptStackTrace $ScriptStackTrace)
}
