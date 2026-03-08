# NOTE:
# This file is now a thin entry point.
# Responsibilities were split into focused modules:
# - RunHelpers.Insights.ps1
# - RunHelpers.Execution.ps1
# - RunHelpers.Audit.ps1

$splitFiles = @(
  'RunHelpers.Insights.ps1',
  'RunHelpers.Execution.ps1',
  'RunHelpers.Audit.ps1'
)

$safetyModulePath = Join-Path $PSScriptRoot 'SafetyToolsService.ps1'
if ((-not (Get-Command Invoke-ToolSelfTest -ErrorAction SilentlyContinue)) -and (Test-Path -LiteralPath $safetyModulePath -PathType Leaf)) {
  . $safetyModulePath
}

foreach ($fileName in $splitFiles) {
  $filePath = Join-Path $PSScriptRoot $fileName
  if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
    throw ('RunHelpers split module missing: {0}' -f $filePath)
  }
  . $filePath
}

if (Get-Command Reset-ArchiveEntryCache -ErrorAction SilentlyContinue) {
  Reset-ArchiveEntryCache
}
if (Get-Command Reset-ClassificationCaches -ErrorAction SilentlyContinue) {
  Reset-ClassificationCaches
}

Remove-Variable -Name splitFiles, fileName, filePath -ErrorAction SilentlyContinue
Remove-Variable -Name safetyModulePath -ErrorAction SilentlyContinue
