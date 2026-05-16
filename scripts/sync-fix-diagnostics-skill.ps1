<#
.SYNOPSIS
  Overwrites the Chat Customizations Evaluations extension skill file with the
  Romulus repo version.

.DESCRIPTION
  VS Code reinstalls extensions into version-suffixed folders on update, which
  wipes any local modifications. Run this script after each extension update to
  re-apply the repo's SKILL.md.

.PARAMETER ExtensionsRoot
  Optional. Defaults to %USERPROFILE%\.vscode\extensions.

.EXAMPLE
  pwsh -File scripts/sync-fix-diagnostics-skill.ps1
#>

[CmdletBinding()]
param(
    [string]$ExtensionsRoot = (Join-Path $env:USERPROFILE '.vscode\extensions')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source   = Join-Path $repoRoot '.claude\skills\fix-customization-evaluation-diagnostics\SKILL.md'

if (-not (Test-Path $source)) {
    throw "Repo skill source not found: $source"
}

if (-not (Test-Path $ExtensionsRoot)) {
    throw "Extensions folder not found: $ExtensionsRoot"
}

$candidates = Get-ChildItem -Path $ExtensionsRoot -Directory `
    -Filter 'ms-vscode.vscode-chat-customizations-evaluations-*' |
    Sort-Object Name -Descending

if (-not $candidates) {
    throw "Extension 'ms-vscode.vscode-chat-customizations-evaluations' not installed under $ExtensionsRoot"
}

$target = Join-Path $candidates[0].FullName 'skills\fix-customization-evaluation-diagnostics\SKILL.md'

if (-not (Test-Path $target)) {
    throw "Target skill file not found in extension: $target"
}

$sourceHash = (Get-FileHash $source -Algorithm SHA256).Hash
$targetHash = (Get-FileHash $target -Algorithm SHA256).Hash

if ($sourceHash -eq $targetHash) {
    Write-Host "Already in sync: $target"
    exit 0
}

Copy-Item -Path $source -Destination $target -Force
Write-Host "Synced repo skill -> $target"
