[CmdletBinding()]
param()
$legacy = Join-Path (Split-Path -Parent $PSScriptRoot) 'Invoke-MutationLight.ps1'
& $legacy @args
exit $LASTEXITCODE
