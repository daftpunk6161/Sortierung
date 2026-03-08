[CmdletBinding()]
param(
  [string]$PluginsRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'plugins')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$settingsModule = Join-Path $repoRoot 'dev\modules\Settings.ps1'
if (Test-Path -LiteralPath $settingsModule -PathType Leaf) {
  . $settingsModule
}
$contractsModule = Join-Path $repoRoot 'dev\modules\DataContracts.ps1'
if (Test-Path -LiteralPath $contractsModule -PathType Leaf) {
  . $contractsModule
}

$result = [ordered]@{
  Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  PluginsRoot = $PluginsRoot
  Files = @()
  Valid = 0
  Invalid = 0
}

if (-not (Test-Path -LiteralPath $PluginsRoot -PathType Container)) {
  [pscustomobject]$result
  return
}

$files = @(Get-ChildItem -Path $PluginsRoot -Filter '*.json' -Recurse -File -ErrorAction SilentlyContinue)
foreach ($file in $files) {
  $status = 'Valid'
  $errors = New-Object System.Collections.Generic.List[string]
  try {
    $json = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if (Get-Command Get-RomCleanupSchema -ErrorAction SilentlyContinue -CommandType Function) {
      $schemaName = if ($file.Name -like '*.console-plugin.json') { 'console-plugin-v1' } else { 'plugin-manifest-v1' }
      $schema = Get-RomCleanupSchema -Name $schemaName
      if ($schema) {
        $check = Test-JsonPayloadSchema -Payload $json -Schema $schema
        foreach ($err in @($check.Errors)) { [void]$errors.Add([string]$err) }
      }
    } else {
      if (-not $json.version) { [void]$errors.Add('missing: version') }
      if (-not $json.schemaVersion) { [void]$errors.Add('missing: schemaVersion') }
      if (-not $json.capabilities) { [void]$errors.Add('missing: capabilities') }
      if (-not $json.id) { [void]$errors.Add('missing: id') }
    }
    if ($errors.Count -gt 0) { $status = 'Invalid' }
  } catch {
    $status = 'Invalid'
    [void]$errors.Add($_.Exception.Message)
  }

  if ($status -eq 'Valid') { $result.Valid++ } else { $result.Invalid++ }
  $result.Files += [pscustomobject]@{
    File = $file.FullName
    Status = $status
    Errors = @($errors)
  }
}

[pscustomobject]$result
