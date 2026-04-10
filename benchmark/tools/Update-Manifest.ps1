<#
.SYNOPSIS
    Regenerates benchmark/manifest.json from ground-truth JSONL datasets.

.DESCRIPTION
    Runs the ManifestCalculator via dotnet test to produce an enriched manifest.json
    with full coverage metrics (fallklasseCounts, datEcosystemCounts, coverageTargets,
    coverageActuals, specialAreaCounts, systemsList, fileChecksums, etc.).

    The manifest is used by CI gates (CoverageGate tests) and coverage gap reports.

    Usage:
      pwsh -NoProfile -File benchmark/tools/Update-Manifest.ps1
      pwsh -NoProfile -File benchmark/tools/Update-Manifest.ps1 -DryRun
      pwsh -NoProfile -File benchmark/tools/Update-Manifest.ps1 -OutputPath ./my-manifest.json

.PARAMETER OutputPath
    Optional override for the output manifest file path.
    Defaults to benchmark/manifest.json.

.PARAMETER DryRun
    If set, calculates and displays the manifest but does not write to disk.

.PARAMETER NoBuild
    Skip dotnet build before running.
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$DryRun,
    [switch]$NoBuild
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
$testProj = Join-Path $repoRoot 'src' 'Romulus.Tests' 'Romulus.Tests.csproj'
$gtDir = Join-Path $repoRoot 'benchmark' 'ground-truth'
$gatesPath = Join-Path $repoRoot 'benchmark' 'gates.json'
$defaultOutput = if ($OutputPath) { $OutputPath } else { Join-Path $repoRoot 'benchmark' 'manifest.json' }

if (-not (Test-Path $testProj)) {
    Write-Error "Test project not found: $testProj"
    exit 1
}

if (-not (Test-Path $gtDir)) {
    Write-Error "Ground-truth directory not found: $gtDir"
    exit 1
}

Write-Host "=== Update Manifest ===" -ForegroundColor Cyan
Write-Host "Ground-truth dir: $gtDir"
Write-Host "Gates file:       $gatesPath"
Write-Host "Output:           $defaultOutput"
Write-Host ""

# --- Load all JSONL entries ---
$entries = @()
$bySet = @{}
$fileChecksums = @{}
$jsonlFiles = Get-ChildItem "$gtDir\*.jsonl" | Sort-Object Name

foreach ($file in $jsonlFiles) {
    $setEntries = @()
    Get-Content $file.FullName -Encoding UTF8 | Where-Object { $_.Trim() -ne '' } | ForEach-Object {
        try {
            $obj = $_ | ConvertFrom-Json
            $setEntries += $obj
        } catch {
            Write-Warning "Parse error in $($file.Name): $_"
        }
    }
    if ($setEntries.Count -gt 0) {
        $bySet[$file.BaseName] = $setEntries.Count
    }
    $entries += $setEntries

    # SHA-256 checksum
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLower()
    $fileChecksums[$file.Name] = $hash
}

Write-Host "Loaded $($entries.Count) entries from $($jsonlFiles.Count) JSONL files."

# --- Platform family classification ---
$arcadeSystems = @("ARCADE","NEOGEO")
$computerSystems = @("A800","AMIGA","ATARIST","C64","CPC","DOS","MSX","PC98","X68K","ZX")
$hybridSystems = @("3DS","PSP","SWITCH","VITA","WIIU")
$discSystems = @("3DO","CD32","CDI","DC","FMTOWNS","GC","JAGCD","NEOCD","PCECD","PCFX","PS1","PS2","PS3","SAT","SCD","WII","X360","XBOX")

function Get-PlatformFamily($consoleKey) {
    if (-not $consoleKey) { return "cartridge" }
    $ck = $consoleKey.ToUpperInvariant()
    if ($arcadeSystems -contains $ck) { return "arcade" }
    if ($computerSystems -contains $ck) { return "computer" }
    if ($hybridSystems -contains $ck) { return "hybrid" }
    if ($discSystems -contains $ck) { return "disc" }
    return "cartridge"
}

$byPlatformFamily = @{ cartridge = 0; disc = 0; arcade = 0; computer = 0; hybrid = 0 }
$systemSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$systemsByFamily = @{}
$byDifficulty = @{}

foreach ($entry in $entries) {
    $ck = Get-SafeProp (Get-SafeProp $entry 'expected') 'consoleKey'
    if ($ck) {
        $family = Get-PlatformFamily $ck
        $byPlatformFamily[$family]++
        $null = $systemSet.Add($ck)
        if (-not $systemsByFamily.ContainsKey($family)) { $systemsByFamily[$family] = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase) }
        $null = $systemsByFamily[$family].Add($ck)
    }
    $diff = Get-SafeProp $entry 'difficulty'
    if ($diff) {
        $byDifficulty[$diff] = ($byDifficulty[$diff] ?? 0) + 1
    }
}

$systemsList = $systemSet | Sort-Object

# --- Fallklasse counts ---
$fcCounts = [ordered]@{}
for ($i = 1; $i -le 20; $i++) { $fcCounts["FC-{0:D2}" -f $i] = 0 }

# Reuse Get-Fallklassen from analyze-gates.ps1 logic
function Get-Fallklassen($tags) {
    $result = @()
    if (-not $tags) { return $result }
    $tagSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$tags, [System.StringComparer]::OrdinalIgnoreCase)

    if ($tagSet.Contains("clean-reference") -or $tagSet.Contains("region-variant") -or $tagSet.Contains("revision-variant")) { $result += "FC-01" }
    if ($tagSet.Contains("wrong-name")) { $result += "FC-02" }
    if ($tagSet.Contains("header-conflict") -or $tagSet.Contains("header-vs-headerless-pair")) { $result += "FC-03" }
    if ($tagSet.Contains("wrong-extension") -or $tagSet.Contains("extension-conflict")) { $result += "FC-04" }
    if ($tagSet.Contains("folder-header-conflict") -or $tagSet.Contains("folder-only-detection") -or $tagSet.Contains("folder-vs-header-conflict")) { $result += "FC-05" }
    if ($tagSet.Contains("dat-exact-match") -or $tagSet.Contains("dat-exact") -or $tagSet.Contains("dat-tosec") -or $tagSet.Contains("dat-nointro") -or $tagSet.Contains("dat-redump")) { $result += "FC-06" }
    if ($tagSet.Contains("dat-weak") -or $tagSet.Contains("dat-none")) { $result += "FC-07" }
    if ($tagSet.Contains("bios") -or $tagSet.Contains("bios-wrong-name") -or $tagSet.Contains("bios-wrong-folder") -or $tagSet.Contains("bios-false-positive") -or $tagSet.Contains("bios-shared")) { $result += "FC-08" }
    if ($tagSet.Contains("parent") -or $tagSet.Contains("clone") -or $tagSet.Contains("arcade-parent") -or $tagSet.Contains("arcade-clone")) { $result += "FC-09" }
    if ($tagSet.Contains("multi-disc")) { $result += "FC-10" }
    if ($tagSet.Contains("multi-file") -or $tagSet.Contains("cue-bin") -or $tagSet.Contains("gdi-tracks") -or $tagSet.Contains("ccd-img") -or $tagSet.Contains("mds-mdf") -or $tagSet.Contains("m3u-playlist")) { $result += "FC-11" }
    if ($tagSet.Contains("archive-inner")) { $result += "FC-12" }
    if ($tagSet.Contains("directory-based")) { $result += "FC-13" }
    if ($tagSet.Contains("expected-unknown") -or $tagSet.Contains("unknown-expected")) { $result += "FC-14" }
    if ($tagSet.Contains("ambiguous")) { $result += "FC-15" }
    if ($tagSet.Contains("negative-control")) { $result += "FC-16" }
    if ($tagSet.Contains("sort-blocked") -or $tagSet.Contains("repair-safety") -or $tagSet.Contains("confidence-low") -or $tagSet.Contains("confidence-borderline") -or $tagSet.Contains("repair-unsafe")) { $result += "FC-17" }
    if ($tagSet.Contains("cross-system") -or $tagSet.Contains("cross-system-ambiguity") -or $tagSet.Contains("gb-gbc-ambiguity") -or $tagSet.Contains("md-32x-ambiguity") -or $tagSet.Contains("ps-disambiguation") -or $tagSet.Contains("arcade-confusion-split-merged") -or $tagSet.Contains("arcade-confusion-merged-nonmerged")) { $result += "FC-18" }
    if ($tagSet.Contains("junk") -or $tagSet.Contains("non-game") -or $tagSet.Contains("demo") -or $tagSet.Contains("homebrew") -or $tagSet.Contains("hack")) { $result += "FC-19" }
    if ($tagSet.Contains("corrupt") -or $tagSet.Contains("truncated") -or $tagSet.Contains("broken-set") -or $tagSet.Contains("corrupt-archive") -or $tagSet.Contains("truncated-rom")) { $result += "FC-20" }

    return ($result | Select-Object -Unique)
}

foreach ($entry in $entries) {
    $fcs = Get-Fallklassen @($entry.tags)
    foreach ($fc in $fcs) {
        $fcCounts[$fc]++
    }
}

# --- DAT ecosystem counts ---
$datEcosystemCounts = @{ "no-intro" = 0; "redump" = 0; "mame" = 0; "tosec" = 0 }
foreach ($entry in $entries) {
    $eco = Get-SafeProp (Get-SafeProp $entry 'expected') 'datEcosystem'
    if ($eco -and $datEcosystemCounts.ContainsKey($eco)) {
        $datEcosystemCounts[$eco]++
    }
}

# --- Special area counts ---
$specialAreas = @{}
$biosSystems = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Inc-Area($area) {
    if (-not $specialAreas.ContainsKey($area)) { $specialAreas[$area] = 0 }
    $specialAreas[$area]++
}

foreach ($entry in $entries) {
    $tags = @($entry.tags)
    $ck = Get-SafeProp (Get-SafeProp $entry 'expected') 'consoleKey'
    $ckStr = if ($ck) { $ck } else { "" }
    $tagSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$tags, [System.StringComparer]::OrdinalIgnoreCase)

    if ($tagSet.Contains("bios")) { Inc-Area "biosTotal"; if ($ck) { $null = $biosSystems.Add($ck) } }
    if ($tagSet.Contains("bios-wrong-name") -or $tagSet.Contains("bios-wrong-folder") -or $tagSet.Contains("bios-false-positive") -or $tagSet.Contains("bios-shared")) { Inc-Area "biosErrorModes" }
    if ($tagSet.Contains("parent") -or $tagSet.Contains("arcade-parent")) { Inc-Area "arcadeParent" }
    if ($tagSet.Contains("clone") -or $tagSet.Contains("arcade-clone")) { Inc-Area "arcadeClone" }
    if ($tagSet.Contains("arcade-bios")) { Inc-Area "arcadeBios" }
    if ($tagSet.Contains("arcade-split") -or $tagSet.Contains("arcade-merged") -or $tagSet.Contains("arcade-non-merged") -or $tagSet.Contains("arcade-nonmerged")) { Inc-Area "arcadeSplitMergedNonMerged" }
    if ($tagSet.Contains("arcade-chd") -or $tagSet.Contains("arcade-game-chd")) { Inc-Area "arcadeChdSupplement" }
    if ($tagSet.Contains("arcade-game-chd")) { Inc-Area "arcadeGameChd" }
    if ($tagSet.Contains("arcade-confusion-split-merged") -or $tagSet.Contains("arcade-confusion-merged-nonmerged")) { Inc-Area "arcadeConfusion" }
    if ($tagSet.Contains("multi-disc")) { Inc-Area "multiDisc" }
    if ($tagSet.Contains("multi-file")) { Inc-Area "multiFileSets" }
    if ($tagSet.Contains("cue-bin")) { Inc-Area "cueBin" }
    if ($tagSet.Contains("gdi-tracks")) { Inc-Area "gdiTracks" }
    if ($tagSet.Contains("ccd-img") -or $tagSet.Contains("mds-mdf")) { Inc-Area "ccdMds" }
    if ($tagSet.Contains("m3u-playlist")) { Inc-Area "m3uPlaylist" }
    if ($tagSet.Contains("serial-number")) { Inc-Area "serialNumber" }
    if ($tagSet.Contains("header-vs-headerless-pair")) { Inc-Area "headerVsHeaderlessPairs" }
    if ($tagSet.Contains("container-cso") -or $tagSet.Contains("container-wia") -or $tagSet.Contains("container-rvz") -or $tagSet.Contains("container-wbfs")) { Inc-Area "containerVariants" }
    if ($tagSet.Contains("chd-raw-sha1") -or $tagSet.Contains("chd-single")) { Inc-Area "chdRawSha1" }
    if ($tagSet.Contains("no-intro") -or $tagSet.Contains("dat-nointro")) { Inc-Area "datNoIntro" }
    if ($tagSet.Contains("redump") -or $tagSet.Contains("dat-redump")) { Inc-Area "datRedump" }
    if ($tagSet.Contains("mame") -or $tagSet.Contains("dat-mame")) { Inc-Area "datMame" }
    if ($tagSet.Contains("tosec") -or $tagSet.Contains("dat-tosec")) { Inc-Area "datTosec" }
    if ($tagSet.Contains("directory-based")) { Inc-Area "directoryBased" }
    if ($tagSet.Contains("keyword-detection")) { Inc-Area "keywordOnly" }
    if ($tagSet.Contains("headerless")) { Inc-Area "headerless" }
    if (($tagSet.Contains("cross-system") -or $tagSet.Contains("cross-system-ambiguity") -or $tagSet.Contains("ps-disambiguation")) -and $ckStr -in @("PS1","PS2","PS3","PSP")) { Inc-Area "psDisambiguation" }
    if (($tagSet.Contains("cross-system") -or $tagSet.Contains("cross-system-ambiguity") -or $tagSet.Contains("gb-gbc-ambiguity")) -and $ckStr -in @("GB","GBC")) { Inc-Area "gbGbcCgb" }
    if (($tagSet.Contains("cross-system") -or $tagSet.Contains("cross-system-ambiguity") -or $tagSet.Contains("md-32x-ambiguity")) -and $ckStr -in @("MD","32X")) { Inc-Area "md32x" }
    if (($tagSet.Contains("cross-system-ambiguity") -or $tagSet.Contains("sat-dc-disambiguation")) -and $ckStr -in @("SAT","DC")) { Inc-Area "satDcDisambiguation" }
    if (($tagSet.Contains("cross-system-ambiguity") -or $tagSet.Contains("pce-pcecd-disambiguation")) -and $ckStr -in @("PCE","PCECD")) { Inc-Area "pcePcecdDisambiguation" }
}
$specialAreas["biosSystems"] = $biosSystems.Count

# --- Holdout count ---
$holdoutDir = Join-Path $repoRoot 'benchmark' 'holdout'
$holdoutCount = 0
if (Test-Path $holdoutDir) {
    Get-ChildItem "$holdoutDir\*.jsonl" | ForEach-Object {
        Get-Content $_.FullName -Encoding UTF8 | Where-Object { $_.Trim() -ne '' } | ForEach-Object {
            try { $null = $_ | ConvertFrom-Json; $holdoutCount++ } catch {}
        }
    }
}

# --- Coverage targets from gates.json ---
$coverageTargets = @{}
$coverageActuals = @{}

if (Test-Path $gatesPath) {
    $gates = Get-Content $gatesPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $s1 = $gates.s1

    function Add-Target($key, $obj) {
        if ($obj -and $null -ne $obj.target) {
            $coverageTargets[$key] = @{
                target   = [int]$obj.target
                hardFail = [int]$obj.hardFail
            }
        }
    }

    Add-Target "totalEntries" $s1.totalEntries
    Add-Target "systemsCovered" $s1.systemsCovered
    Add-Target "fallklassenCovered" $s1.fallklassenCovered

    if ($s1.platformFamily) {
        $s1.platformFamily.PSObject.Properties | ForEach-Object {
            Add-Target "platformFamily.$($_.Name)" $_.Value
        }
    }
    if ($s1.caseClasses) {
        $s1.caseClasses.PSObject.Properties | ForEach-Object {
            Add-Target "caseClasses.$($_.Name)" $_.Value
        }
    }
    if ($s1.specialAreas) {
        $s1.specialAreas.PSObject.Properties | ForEach-Object {
            Add-Target "specialAreas.$($_.Name)" $_.Value
        }
    }
}

# Build coverage actuals (same keys as targets)
$fcNonZero = ($fcCounts.Values | Where-Object { $_ -gt 0 }).Count

$coverageActuals["totalEntries"] = $entries.Count
$coverageActuals["systemsCovered"] = $systemSet.Count
$coverageActuals["fallklassenCovered"] = $fcNonZero

foreach ($family in $byPlatformFamily.Keys) {
    $coverageActuals["platformFamily.$family"] = $byPlatformFamily[$family]
}
foreach ($fc in $fcCounts.Keys) {
    $coverageActuals["caseClasses.$fc"] = $fcCounts[$fc]
}
foreach ($area in $specialAreas.Keys) {
    $coverageActuals["specialAreas.$area"] = $specialAreas[$area]
}

# --- Build systemsCoveredByFamily as sorted lists ---
$systemsCoveredByFamilyOut = @{}
foreach ($family in $systemsByFamily.Keys) {
    $systemsCoveredByFamilyOut[$family] = @($systemsByFamily[$family] | Sort-Object)
}

# --- Assemble manifest object ---
$manifest = [ordered]@{
    _meta = [ordered]@{
        description        = "Benchmark dataset manifest - auto-generated by Update-Manifest.ps1"
        version            = "5.0.0"
        groundTruthVersion = "2.1.0"
        lastModified       = (Get-Date -Format "yyyy-MM-dd")
    }
    totalEntries          = $entries.Count
    holdoutEntries        = $holdoutCount
    systemsCovered        = $systemSet.Count
    systemsList           = @($systemsList)
    bySet                 = $bySet
    byPlatformFamily      = $byPlatformFamily
    byDifficulty          = $byDifficulty
    systemsCoveredByFamily = $systemsCoveredByFamilyOut
    fallklasseCounts      = $fcCounts
    datEcosystemCounts    = $datEcosystemCounts
    specialAreaCounts     = $specialAreas
    coverageTargets       = $coverageTargets
    coverageActuals       = $coverageActuals
    fileChecksums         = $fileChecksums
}

$json = $manifest | ConvertTo-Json -Depth 10

if ($DryRun) {
    Write-Host ""
    Write-Host "=== DRY RUN - Manifest Preview ===" -ForegroundColor Yellow
    Write-Host $json
    Write-Host ""
    Write-Host "Would write to: $defaultOutput" -ForegroundColor Yellow
}
else {
    $json | Set-Content -Path $defaultOutput -Encoding UTF8 -NoNewline
    Write-Host ""
    Write-Host "=== Manifest written to $defaultOutput ===" -ForegroundColor Green
    Write-Host "  Total entries:    $($entries.Count)"
    Write-Host "  Systems covered:  $($systemSet.Count)"
    Write-Host "  JSONL files:      $($jsonlFiles.Count)"
    Write-Host "  FC covered:       $fcNonZero / 20"
}
