[CmdletBinding()]
param(
    [int]$ReadinessIterations = 30,
    [int]$DuplicateGroups = 5000,
    [int]$ResponsivenessDurationMs = 2200,
    [switch]$NoLatest
)

$impl = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\performance\Invoke-GuiKpiSnapshot.ps1'
& $impl -ReadinessIterations $ReadinessIterations -DuplicateGroups $DuplicateGroups -ResponsivenessDurationMs $ResponsivenessDurationMs -NoLatest:$NoLatest
exit $LASTEXITCODE
