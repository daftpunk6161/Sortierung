[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$TaskName,
  [Parameter(Mandatory=$true)][string[]]$Roots,
  [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun',
  [string[]]$Prefer = @('EU','US','WORLD','JP'),
  [string]$Time = '03:00'
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$moduleRoot = Join-Path $repoRoot 'dev\modules'
$loaderPath = Join-Path $moduleRoot 'RomCleanupLoader.ps1'

if (-not (Test-Path -LiteralPath $loaderPath -PathType Leaf)) {
  throw "Loader nicht gefunden: $loaderPath"
}

$script:_RomCleanupModuleRoot = $moduleRoot
. $loaderPath

Register-SchedulerTaskAdapter -TaskName $TaskName -Roots $Roots -Mode $Mode -Prefer $Prefer -Time $Time -WorkingDirectory $repoRoot | Out-Null
Write-Output ("Scheduled Task registriert: {0} ({1})" -f $TaskName, $Time)
