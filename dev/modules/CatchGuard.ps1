function Resolve-CatchErrorClass {
	param(
		[AllowNull()][System.Exception]$Exception,
		[string]$ErrorCode,
		[ValidateSet('Transient','Recoverable','Critical')]
		[string]$Default = 'Recoverable'
	)

	if (-not [string]::IsNullOrWhiteSpace([string]$ErrorCode)) {
		$normalizedCode = [string]$ErrorCode
		if ($normalizedCode -like 'SEC-*') { return 'Critical' }
		if ($normalizedCode -like 'AUTH-*') { return 'Critical' }
		if ($normalizedCode -like 'IO-LOCK*') { return 'Transient' }
		if ($normalizedCode -like 'NET-*') { return 'Transient' }
	}

	if ($null -eq $Exception) { return [string]$Default }

	if ($Exception -is [System.TimeoutException] -or
			$Exception -is [System.Net.WebException] -or
			$Exception -is [System.IO.IOException] -or
			$Exception -is [System.OperationCanceledException]) {
		return 'Transient'
	}

	if ($Exception -is [System.UnauthorizedAccessException] -or
			$Exception -is [System.Security.SecurityException] -or
			$Exception -is [System.OutOfMemoryException] -or
			$Exception -is [System.AccessViolationException] -or
			$Exception -is [System.StackOverflowException]) {
		return 'Critical'
	}

	return 'Recoverable'
}

function ConvertTo-OperationErrorSeverity {
	<# Maps CatchGuard ErrorClass to OperationError Severity (DUP-210). #>
	param([string]$ErrorClass)
	switch ($ErrorClass) {
		'Critical'    { return 'Critical' }
		'Transient'   { return 'Warning' }
		'Recoverable' { return 'Error' }
		default       { return 'Error' }
	}
}

function New-CatchGuardRecord {
	param(
		[Parameter(Mandatory = $true)][string]$Module,
		[Parameter(Mandatory = $true)][string]$Action,
		[string]$Root,
		[string]$OperationId,
		[AllowNull()][System.Exception]$Exception,
		[string]$Message,
		[string]$ErrorCode,
		[ValidateSet('Transient','Recoverable','Critical')]
		[string]$ErrorClass
	)

	$resolvedOperationId = [string]$OperationId
	if ([string]::IsNullOrWhiteSpace($resolvedOperationId)) {
		if (Get-Command Get-OperationCorrelationId -ErrorAction SilentlyContinue) {
			$resolvedOperationId = [string](Get-OperationCorrelationId)
		}
		if ([string]::IsNullOrWhiteSpace($resolvedOperationId)) {
			$resolvedOperationId = [guid]::NewGuid().ToString('N')
		}
	}

	$resolvedMessage = [string]$Message
	if ([string]::IsNullOrWhiteSpace($resolvedMessage) -and $Exception) {
		$resolvedMessage = [string]$Exception.Message
	}
	if ([string]::IsNullOrWhiteSpace($resolvedMessage)) {
		$resolvedMessage = 'Unhandled operation error.'
	}

	$resolvedClass = [string]$ErrorClass
	if ([string]::IsNullOrWhiteSpace($resolvedClass)) {
		$resolvedClass = Resolve-CatchErrorClass -Exception $Exception -ErrorCode $ErrorCode
	}

	# DUP-210: Produce OperationError-compatible output when ErrorContracts is loaded
	if (Get-Command New-OperationError -ErrorAction SilentlyContinue) {
		$severity = ConvertTo-OperationErrorSeverity -ErrorClass $resolvedClass
		$opError = New-OperationError -ErrorCode ([string]$ErrorCode) -Severity $severity -Message $resolvedMessage -Exception $Exception -Context ([pscustomobject]@{ Module = [string]$Module; Action = [string]$Action; Root = [string]$Root; OperationId = [string]$resolvedOperationId })
		# Add CatchGuard-specific properties for backward compatibility
		$opError | Add-Member -NotePropertyName 'ErrorClass'   -NotePropertyValue ([string]$resolvedClass) -Force
		$opError | Add-Member -NotePropertyName 'Module'       -NotePropertyValue ([string]$Module) -Force
		$opError | Add-Member -NotePropertyName 'OperationId'  -NotePropertyValue ([string]$resolvedOperationId) -Force
		$opError | Add-Member -NotePropertyName 'Root'         -NotePropertyValue ([string]$Root) -Force
		$opError | Add-Member -NotePropertyName 'Action'       -NotePropertyValue ([string]$Action) -Force
		return $opError
	}

	return [pscustomobject]@{
		TimestampUtc = (Get-Date).ToUniversalTime().ToString('o')
		ErrorClass = [string]$resolvedClass
		Module = [string]$Module
		OperationId = [string]$resolvedOperationId
		Root = [string]$Root
		Action = [string]$Action
		ErrorCode = [string]$ErrorCode
		ExceptionType = if ($Exception) { [string]$Exception.GetType().FullName } else { $null }
		Message = [string]$resolvedMessage
	}
}

function Write-CatchGuardLog {
	param(
		[Parameter(Mandatory = $true)][string]$Module,
		[Parameter(Mandatory = $true)][string]$Action,
		[string]$Root,
		[AllowNull()][System.Exception]$Exception,
		[string]$Message,
		[string]$ErrorCode,
		[string]$OperationId,
		[ValidateSet('Error','Warning')]
		[string]$Level = 'Error'
	)

	$record = New-CatchGuardRecord -Module $Module -Action $Action -Root $Root -OperationId $OperationId -Exception $Exception -Message $Message -ErrorCode $ErrorCode
	$errorClass = if ($record.PSObject.Properties.Name -contains 'ErrorClass') { $record.ErrorClass } else { 'Recoverable' }
	$line = ('[{0}] {1}/{2}: {3}' -f $errorClass, $Module, $Action, $record.Message)

	if (Get-Command Write-Log -ErrorAction SilentlyContinue) {
		[void](Write-Log -Level $Level -Message $line -CorrelationId $record.OperationId -Module $Module -Action $Action -Root $Root -ErrorClass $errorClass)
	} else {
		Write-Warning $line
	}

	# DUP-210: Feed OperationError-compatible records into DiagnosticsService
	if ($Level -eq 'Error' -and (Get-Command Add-RunError -ErrorAction SilentlyContinue)) {
		$category = if ($record.PSObject.Properties.Name -contains 'Category') { [string]$record.Category } else { $Module }
		[void](Add-RunError -Category $category -Message $record.Message -ErrorCode ([string]$ErrorCode) -Severity (ConvertTo-OperationErrorSeverity -ErrorClass $errorClass) -Exception $Exception)
	}

	return $record
}

function Invoke-SafeCatch {
	<# Convenience wrapper for catch blocks that should log but not rethrow.
	   Use in place of empty catch { } blocks to ensure visibility. #>
	param(
		[Parameter(Mandatory = $true)][string]$Module,
		[Parameter(Mandatory = $true)][string]$Action,
		[AllowNull()][System.Management.Automation.ErrorRecord]$ErrorRecord,
		[ValidateSet('Error','Warning')]
		[string]$Level = 'Warning'
	)

	$ex = if ($ErrorRecord -and $ErrorRecord.Exception) { $ErrorRecord.Exception } else { $null }
	$msg = if ($ex) { [string]$ex.Message } else { 'Unknown error' }

	if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
		[void](Write-CatchGuardLog -Module $Module -Action $Action -Exception $ex -Message $msg -Level $Level)
	} else {
		Write-Verbose ('[{0}/{1}] {2}' -f $Module, $Action, $msg)
	}
}
