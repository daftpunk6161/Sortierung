[CmdletBinding()]
param(
    [ValidateSet('all','unit','integration','e2e')]
    [string]$Stage = 'all',
    [ValidateSet('None','Normal','Detailed','Diagnostic')]
    [string]$PesterOutput = 'None',
    [switch]$FailFast,
    [switch]$Coverage,
    [ValidateRange(1,100)]
    [int]$CoverageTarget = 34,
    [switch]$BenchmarkGate,
    [switch]$BenchmarkGateNightly,
    [ValidateRange(0,3)]
    [int]$FlakyRetries = 0
)

$legacy = Join-Path (Split-Path -Parent $PSScriptRoot) 'Invoke-TestPipeline.ps1'
& $legacy -Stage $Stage -PesterOutput $PesterOutput -FailFast:$FailFast -Coverage:$Coverage -CoverageTarget $CoverageTarget -BenchmarkGate:$BenchmarkGate -BenchmarkGateNightly:$BenchmarkGateNightly -FlakyRetries $FlakyRetries
exit $LASTEXITCODE
