[CmdletBinding()]
param(
  [int]$Port = 7878,
  [string]$ApiKey = $null,
  [int]$PollIntervalMs = 250,
  [int]$RateLimitRequestsPerWindow = 120,
  [int]$RateLimitWindowSeconds = 60,
  [ValidateSet('custom','local-dev','strict-local')]
  [string]$CorsMode = 'custom',
  [string]$CorsAllowOrigin = '*'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
  $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
}

$moduleDir = Join-Path $scriptDir 'dev\modules'
if (-not (Test-Path -LiteralPath $moduleDir -PathType Container)) {
  throw "Module directory not found: $moduleDir"
}

$script:_RomCleanupModuleRoot = $moduleDir
. (Join-Path $moduleDir 'RomCleanupLoader.ps1')

Initialize-AppState

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
  $ApiKey = [System.Environment]::GetEnvironmentVariable('ROM_CLEANUP_API_KEY')
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
  throw 'API key missing. Use -ApiKey or set ROM_CLEANUP_API_KEY.'
}

Write-Host ("ROM Cleanup API listening on http://127.0.0.1:{0}/" -f $Port)
Write-Host 'Press Ctrl+C to stop.'

Start-RomCleanupApiServer -Port $Port -ApiKey $ApiKey -PollIntervalMs $PollIntervalMs -RateLimitRequestsPerWindow $RateLimitRequestsPerWindow -RateLimitWindowSeconds $RateLimitWindowSeconds -CorsMode $CorsMode -CorsAllowOrigin $CorsAllowOrigin
