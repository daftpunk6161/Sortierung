[CmdletBinding()]
param()
$legacy = Join-Path (Split-Path -Parent $PSScriptRoot) 'Invoke-PerformanceSnapshot.ps1'
& $legacy @args
exit $LASTEXITCODE
