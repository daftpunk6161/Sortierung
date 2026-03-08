# --- Run Error Tracker ---
$script:RUN_ERRORS = [ordered]@{}

function Reset-RunErrors {
  <# Setzt den Fehler-Tracker für einen neuen Lauf zurück. #>
  $script:RUN_ERRORS = [ordered]@{}
}

function Add-RunError {
  <# Registriert einen kategorisierten Fehler für die Zusammenfassung. #>
  param(
    [Parameter(Mandatory=$true)][string]$Category,
    [Parameter(Mandatory=$true)][string]$Message,
    [string]$ErrorCode = 'RUN_ERROR',
    [ValidateSet('Info','Warning','Error','Critical')]
    [string]$Severity = 'Error',
    [string]$ActionHint = 'Bitte prüfe den Laufkontext und wiederhole den Schritt.',
    [AllowNull()][object]$Context = $null,
    [AllowNull()][System.Exception]$Exception = $null,
    [AllowNull()][string]$ScriptStackTrace = $null
  )
  if (-not $script:RUN_ERRORS.Contains($Category)) {
    $script:RUN_ERRORS[$Category] = New-Object System.Collections.Generic.List[object]
  }

  $entry = if (Get-Command New-OperationError -ErrorAction SilentlyContinue) {
    New-OperationError -ErrorCode $ErrorCode -Severity $Severity -Message $Message -ActionHint $ActionHint -Context $Context -Exception $Exception -ScriptStackTrace $ScriptStackTrace
  } else {
    [pscustomobject]@{ Message = $Message }
  }

  [void]$script:RUN_ERRORS[$Category].Add($entry)
}

function Get-RunErrorCount {
  <# Gibt die Gesamtzahl aller gesammelten Fehler zurück. #>
  $total = 0
  foreach ($key in $script:RUN_ERRORS.Keys) {
    $total += $script:RUN_ERRORS[$key].Count
  }
  return $total
}

function Write-RunErrorSummary {
  <# Schreibt eine kompakte Fehler-Zusammenfassung ins Log. #>
  param([scriptblock]$Log)
  if (-not $Log) { return }
  $total = Get-RunErrorCount
  if ($total -eq 0) {
    & $Log ''
    & $Log '=== Fehler-Zusammenfassung: Keine Fehler ==='
    return
  }
  & $Log ''
  & $Log ("=== Fehler-Zusammenfassung: {0} Fehler ===" -f $total)
  foreach ($cat in $script:RUN_ERRORS.Keys) {
    $items = $script:RUN_ERRORS[$cat]
    & $Log ("  [{0}] {1} Fehler" -f $cat, $items.Count)
    $shown = [math]::Min($items.Count, 5)
    for ($i = 0; $i -lt $shown; $i++) {
      $item = $items[$i]
      if ($item -is [string]) {
        & $Log ("    - {0}" -f $item)
      } elseif ($item -and ($item.PSObject.Properties.Name -contains 'Message')) {
        $code = if ($item.PSObject.Properties.Name -contains 'ErrorCode') { [string]$item.ErrorCode } else { 'RUN_ERROR' }
        $sev = if ($item.PSObject.Properties.Name -contains 'Severity') { [string]$item.Severity } else { 'Error' }
        & $Log ("    - [{0}/{1}] {2}" -f $sev, $code, [string]$item.Message)
      } else {
        & $Log ("    - {0}" -f [string]$item)
      }
    }
    if ($items.Count -gt 5) {
      & $Log ("    ... und {0} weitere" -f ($items.Count - 5))
    }
  }
  & $Log ''
}
