<#
.SYNOPSIS
    Generates a human-readable coverage gap report comparing manifest.json against gates.json.

.DESCRIPTION
    Reads benchmark/manifest.json and benchmark/gates.json, computes the delta
    between actual coverage and target thresholds, and outputs a structured
    report showing which areas are below target, at hard-fail, or above target.

    Usage:
      pwsh -NoProfile -File benchmark/tools/New-CoverageGapReport.ps1
      pwsh -NoProfile -File benchmark/tools/New-CoverageGapReport.ps1 -OnlyGaps
      pwsh -NoProfile -File benchmark/tools/New-CoverageGapReport.ps1 -Format markdown

.PARAMETER OnlyGaps
    Only show areas below target (suppress passing areas).

.PARAMETER Format
    Output format: 'table' (default) or 'markdown'.

.PARAMETER ManifestPath
    Optional override for manifest.json path.
#>
[CmdletBinding()]
param(
    [switch]$OnlyGaps,
    [ValidateSet('table', 'markdown')]
    [string]$Format = 'table',
    [string]$ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Safe property accessor for PSCustomObjects under strict mode
function Get-SafeProp($obj, [string]$prop) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$prop]
    if ($null -eq $p) { return $null }
    return $p.Value
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestFile = if ($ManifestPath) { $ManifestPath } else { Join-Path $repoRoot 'benchmark' 'manifest.json' }
$gatesFile = Join-Path $repoRoot 'benchmark' 'gates.json'

if (-not (Test-Path $manifestFile)) {
    Write-Error "Manifest not found: $manifestFile. Run Update-Manifest.ps1 first."
    exit 1
}
if (-not (Test-Path $gatesFile)) {
    Write-Error "Gates file not found: $gatesFile"
    exit 1
}

$manifest = Get-Content $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
$gates = Get-Content $gatesFile -Raw -Encoding UTF8 | ConvertFrom-Json
$s1 = $gates.s1

# --- Build comparison rows ---
$rows = @()

function Add-Row($key, $actual, $target, $hardFail) {
    $delta = $actual - $target
    $pct = if ($target -gt 0) { [math]::Round(($actual / $target) * 100, 1) } else { 100.0 }
    $status = if ($actual -lt $hardFail) { "FAIL" }
              elseif ($actual -lt $target) { "WARN" }
              else { "OK" }
    $script:rows += [pscustomobject]@{
        Key      = $key
        Actual   = $actual
        Target   = $target
        HardFail = $hardFail
        Delta    = $delta
        Pct      = $pct
        Status   = $status
    }
}

# Top-level
function Get-ActualFromManifest($key) {
    $actualsObj = Get-SafeProp $manifest 'coverageActuals'
    if ($actualsObj) {
        $val = Get-SafeProp $actualsObj $key
        if ($null -ne $val) { return [int]$val }
    }
    # Fallback to top-level manifest fields
    switch ($key) {
        "totalEntries"      { return [int]$manifest.totalEntries }
        "systemsCovered"    { return [int]$manifest.systemsCovered }
        "fallklassenCovered" {
            $fc = Get-SafeProp $manifest 'fallklasseCounts'
            if ($fc) {
                return @($fc.PSObject.Properties | Where-Object { [int]$_.Value -gt 0 }).Count
            }
            return 0
        }
        default { return 0 }
    }
}

function Process-GateSection($prefix, $section) {
    if (-not $section) { return }
    $section.PSObject.Properties | ForEach-Object {
        $key = if ($prefix) { "$prefix.$($_.Name)" } else { $_.Name }
        $target = [int]$_.Value.target
        $hardFail = [int]$_.Value.hardFail
        $actual = Get-ActualFromManifest $key
        Add-Row $key $actual $target $hardFail
    }
}

# Process all gate sections
foreach ($prop in @("totalEntries", "systemsCovered", "fallklassenCovered")) {
    if ($null -ne $s1.$prop) {
        $target = [int]$s1.$prop.target
        $hardFail = [int]$s1.$prop.hardFail
        $actual = Get-ActualFromManifest $prop
        Add-Row $prop $actual $target $hardFail
    }
}

Process-GateSection "platformFamily" $s1.platformFamily
Process-GateSection "caseClasses" $s1.caseClasses
Process-GateSection "specialAreas" $s1.specialAreas

# Filter if OnlyGaps
if ($OnlyGaps) {
    $rows = $rows | Where-Object { $_.Status -ne "OK" }
}

# --- Output ---
$failCount = @($rows | Where-Object { $_.Status -eq "FAIL" }).Count
$warnCount = @($rows | Where-Object { $_.Status -eq "WARN" }).Count
$okCount = @($rows | Where-Object { $_.Status -eq "OK" }).Count

Write-Host ""
Write-Host "=== Coverage Gap Report ===" -ForegroundColor Cyan
Write-Host "  Manifest: $manifestFile"
Write-Host "  Gates:    $gatesFile"
Write-Host "  Total gates: $($rows.Count)  |  OK: $okCount  |  WARN: $warnCount  |  FAIL: $failCount"
Write-Host ""

if ($Format -eq 'markdown') {
    Write-Host "| Key | Actual | Target | HardFail | Delta | % | Status |"
    Write-Host "|-----|--------|--------|----------|-------|---|--------|"
    foreach ($row in $rows) {
        $statusIcon = switch ($row.Status) { "FAIL" { "🔴" } "WARN" { "🟡" } "OK" { "🟢" } }
        Write-Host "| $($row.Key) | $($row.Actual) | $($row.Target) | $($row.HardFail) | $($row.Delta) | $($row.Pct)% | $statusIcon $($row.Status) |"
    }
}
else {
    # Table format with color
    foreach ($row in $rows) {
        $color = switch ($row.Status) { "FAIL" { "Red" } "WARN" { "Yellow" } "OK" { "Green" } }
        $line = "{0,-45} {1,6} / {2,6}  (HF:{3,5})  Δ:{4,6}  {5,6}%  [{6}]" -f $row.Key, $row.Actual, $row.Target, $row.HardFail, $row.Delta, $row.Pct, $row.Status
        Write-Host $line -ForegroundColor $color
    }
}

# Summary
Write-Host ""
if ($failCount -gt 0) {
    Write-Host "=== $failCount HARD-FAIL GATES - dataset expansion needed ===" -ForegroundColor Red
    $rows | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
        Write-Host "  $($_.Key): need $($_.HardFail - $_.Actual) more entries to clear hard-fail" -ForegroundColor Red
    }
}
if ($warnCount -gt 0) {
    Write-Host ""
    Write-Host "=== $warnCount BELOW-TARGET GATES ===" -ForegroundColor Yellow
    $rows | Where-Object { $_.Status -eq "WARN" } | ForEach-Object {
        Write-Host "  $($_.Key): need $($_.Target - $_.Actual) more entries to reach target" -ForegroundColor Yellow
    }
}
if ($failCount -eq 0 -and $warnCount -eq 0) {
    Write-Host "=== ALL GATES MET ===" -ForegroundColor Green
}

exit $(if ($failCount -gt 0) { 1 } else { 0 })
