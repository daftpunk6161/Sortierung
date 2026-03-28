<#
.SYNOPSIS
    Validates that manifest.json checksums match the actual JSONL files on disk.

.DESCRIPTION
    Reads benchmark/manifest.json and verifies the fileChecksums section against
    the actual SHA-256 hashes of JSONL files in benchmark/ground-truth/.
    This ensures the manifest was regenerated after dataset changes.

    Usage:
      pwsh -NoProfile -File benchmark/tools/Test-ManifestIntegrity.ps1
      pwsh -NoProfile -File benchmark/tools/Test-ManifestIntegrity.ps1 -Verbose

.PARAMETER ManifestPath
    Optional override for manifest.json path.
#>
[CmdletBinding()]
param(
    [string]$ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestFile = if ($ManifestPath) { $ManifestPath } else { Join-Path $repoRoot 'benchmark' 'manifest.json' }
$gtDir = Join-Path $repoRoot 'benchmark' 'ground-truth'

if (-not (Test-Path $manifestFile)) {
    Write-Error "Manifest not found: $manifestFile. Run Update-Manifest.ps1 first."
    exit 1
}

$manifest = Get-Content $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json

Write-Host "=== Manifest Integrity Check ===" -ForegroundColor Cyan
Write-Host "Manifest: $manifestFile"
Write-Host ""

$errors = @()
$checks = 0

# --- 1. File checksum verification ---
Write-Host "--- File Checksums ---"
$jsonlFiles = Get-ChildItem "$gtDir\*.jsonl" | Sort-Object Name

if (-not $manifest.fileChecksums) {
    $errors += "manifest.json has no fileChecksums section"
    Write-Host "  SKIP: No fileChecksums in manifest" -ForegroundColor Yellow
}
else {
    foreach ($file in $jsonlFiles) {
        $checks++
        $actualHash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLower()
        $manifestHash = $manifest.fileChecksums.$($file.Name)

        if (-not $manifestHash) {
            $errors += "Missing checksum for $($file.Name)"
            Write-Host "  FAIL: $($file.Name) - not in manifest" -ForegroundColor Red
        }
        elseif ($actualHash -ne $manifestHash) {
            $errors += "Checksum mismatch for $($file.Name): manifest=$manifestHash actual=$actualHash"
            Write-Host "  FAIL: $($file.Name) - hash mismatch (manifest stale)" -ForegroundColor Red
        }
        else {
            Write-Verbose "  OK: $($file.Name)"
        }
    }

    # Check for stale entries in manifest that no longer exist on disk
    $diskFileNames = $jsonlFiles | ForEach-Object { $_.Name }
    $manifest.fileChecksums.PSObject.Properties | ForEach-Object {
        $checks++
        if ($_.Name -notin $diskFileNames) {
            $errors += "Stale manifest entry: $($_.Name) not found on disk"
            Write-Host "  FAIL: $($_.Name) - in manifest but not on disk" -ForegroundColor Red
        }
    }
}

# --- 2. Entry count consistency ---
Write-Host ""
Write-Host "--- Entry Count Consistency ---"
$diskEntryCount = 0
foreach ($file in $jsonlFiles) {
    $lineCount = (Get-Content $file.FullName -Encoding UTF8 | Where-Object { $_.Trim() -ne '' }).Count
    $diskEntryCount += $lineCount
}

$checks++
$manifestTotal = [int]$manifest.totalEntries
if ($manifestTotal -ne $diskEntryCount) {
    $errors += "totalEntries mismatch: manifest=$manifestTotal disk=$diskEntryCount"
    Write-Host "  FAIL: totalEntries manifest=$manifestTotal vs disk=$diskEntryCount" -ForegroundColor Red
}
else {
    Write-Host "  OK: totalEntries=$manifestTotal" -ForegroundColor Green
}

# --- 3. bySet sum consistency ---
$checks++
if ($manifest.bySet) {
    $setSum = 0
    $manifest.bySet.PSObject.Properties | ForEach-Object { $setSum += [int]$_.Value }
    if ($setSum -ne $manifestTotal) {
        $errors += "bySet sum ($setSum) != totalEntries ($manifestTotal)"
        Write-Host "  FAIL: bySet sum=$setSum != totalEntries=$manifestTotal" -ForegroundColor Red
    }
    else {
        Write-Host "  OK: bySet sum=$setSum matches totalEntries" -ForegroundColor Green
    }
}

# --- 4. JSONL file count ---
$checks++
$manifestSetCount = if ($manifest.bySet) { @($manifest.bySet.PSObject.Properties).Count } else { 0 }
$diskSetCount = $jsonlFiles.Count
if ($manifestSetCount -ne $diskSetCount) {
    $errors += "Set count mismatch: manifest=$manifestSetCount disk=$diskSetCount"
    Write-Host "  FAIL: set count manifest=$manifestSetCount vs disk=$diskSetCount" -ForegroundColor Red
}
else {
    Write-Host "  OK: set count=$diskSetCount" -ForegroundColor Green
}

# --- 5. SystemsCovered consistency ---
$checks++
if ($manifest.systemsList -and $manifest.systemsCovered) {
    $listCount = $manifest.systemsList.Count
    $coveredCount = [int]$manifest.systemsCovered
    if ($listCount -ne $coveredCount) {
        $errors += "systemsList.Count ($listCount) != systemsCovered ($coveredCount)"
        Write-Host "  FAIL: systemsList=$listCount != systemsCovered=$coveredCount" -ForegroundColor Red
    }
    else {
        Write-Host "  OK: systemsCovered=$coveredCount" -ForegroundColor Green
    }
}

# --- Summary ---
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "  Checks run: $checks"

if ($errors.Count -gt 0) {
    Write-Host "  Errors: $($errors.Count)" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Manifest is STALE. Run Update-Manifest.ps1 to regenerate." -ForegroundColor Yellow
    exit 1
}
else {
    Write-Host "  All checks passed." -ForegroundColor Green
    exit 0
}
