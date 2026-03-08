[CmdletBinding()]
param(
  [int]$LookbackDays = 30,
  [bool]$UseSyntheticWhenMissing = $true,
  [switch]$NoLatest
)

$impl = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\performance\Invoke-UxKpiSnapshot.ps1'
& $impl -LookbackDays $LookbackDays -UseSyntheticWhenMissing $UseSyntheticWhenMissing -NoLatest:$NoLatest
exit $LASTEXITCODE
