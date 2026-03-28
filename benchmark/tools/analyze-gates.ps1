# Analyze ground-truth JSONL data against gates.json dimensions
# Outputs actual counts for every gate category

$gtDir = "C:\Code\Sortierung\benchmark\ground-truth"
$entries = @()

Get-ChildItem "$gtDir\*.jsonl" | ForEach-Object {
    $file = $_
    Get-Content $file.FullName | Where-Object { $_.Trim() -ne '' } | ForEach-Object {
        try {
            $obj = $_ | ConvertFrom-Json
            $obj | Add-Member -NotePropertyName '_sourceFile' -NotePropertyValue $file.BaseName -Force
            $entries += $obj
        } catch {
            Write-Warning "Parse error in $($file.Name): $_"
        }
    }
}

Write-Host "=== TOTAL ENTRIES: $($entries.Count) ==="

# === Platform Family Classification ===
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

$familyCounts = @{ cartridge=0; disc=0; arcade=0; computer=0; hybrid=0 }
$systemCounts = @{}
$fcCounts = @{}
for ($i = 1; $i -le 20; $i++) { $fcCounts["FC-{0:D2}" -f $i] = 0 }
$specialAreas = @{}

# FC tag mappings (mirrors FallklasseClassifier.cs)
function Get-Fallklassen($tags) {
    $result = @()
    if (-not $tags) { return $result }
    $tagSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$tags, [System.StringComparer]::OrdinalIgnoreCase)
    
    if ($tagSet.Contains("clean-reference") -or $tagSet.Contains("region-variant") -or $tagSet.Contains("revision-variant")) { $result += "FC-01" }
    if ($tagSet.Contains("wrong-name")) { $result += "FC-02" }
    if ($tagSet.Contains("header-conflict")) { $result += "FC-03" }
    if ($tagSet.Contains("header-vs-headerless-pair")) { $result += "FC-03" }
    if ($tagSet.Contains("wrong-extension")) { $result += "FC-04" }
    if ($tagSet.Contains("extension-conflict")) { $result += "FC-04" }
    if ($tagSet.Contains("folder-header-conflict") -or $tagSet.Contains("folder-only-detection") -or $tagSet.Contains("folder-vs-header-conflict")) { $result += "FC-05" }
    if ($tagSet.Contains("dat-exact-match") -or $tagSet.Contains("dat-exact") -or $tagSet.Contains("dat-tosec") -or $tagSet.Contains("dat-nointro") -or $tagSet.Contains("dat-redump")) { $result += "FC-06" }
    if ($tagSet.Contains("dat-weak") -or $tagSet.Contains("dat-none")) { $result += "FC-07" }
    if ($tagSet.Contains("bios")) { $result += "FC-08" }
    if ($tagSet.Contains("bios-wrong-name") -or $tagSet.Contains("bios-wrong-folder") -or $tagSet.Contains("bios-false-positive") -or $tagSet.Contains("bios-shared")) { $result += "FC-08" }
    if ($tagSet.Contains("parent") -or $tagSet.Contains("clone")) { $result += "FC-09" }
    if ($tagSet.Contains("arcade-parent") -or $tagSet.Contains("arcade-clone")) { $result += "FC-09" }
    if ($tagSet.Contains("multi-disc")) { $result += "FC-10" }
    if ($tagSet.Contains("multi-file")) { $result += "FC-11" }
    if ($tagSet.Contains("cue-bin") -or $tagSet.Contains("gdi-tracks") -or $tagSet.Contains("ccd-img") -or $tagSet.Contains("mds-mdf") -or $tagSet.Contains("m3u-playlist")) { $result += "FC-11" }
    if ($tagSet.Contains("archive-inner")) { $result += "FC-12" }
    if ($tagSet.Contains("directory-based")) { $result += "FC-13" }
    if ($tagSet.Contains("expected-unknown") -or $tagSet.Contains("unknown-expected")) { $result += "FC-14" }
    if ($tagSet.Contains("ambiguous")) { $result += "FC-15" }
    if ($tagSet.Contains("negative-control")) { $result += "FC-16" }
    if ($tagSet.Contains("sort-blocked") -or $tagSet.Contains("repair-safety") -or $tagSet.Contains("confidence-low") -or $tagSet.Contains("confidence-borderline") -or $tagSet.Contains("repair-unsafe")) { $result += "FC-17" }
    if ($tagSet.Contains("cross-system") -or $tagSet.Contains("cross-system-ambiguity") -or $tagSet.Contains("gb-gbc-ambiguity") -or $tagSet.Contains("md-32x-ambiguity") -or $tagSet.Contains("ps-disambiguation")) { $result += "FC-18" }
    if ($tagSet.Contains("arcade-confusion-split-merged") -or $tagSet.Contains("arcade-confusion-merged-nonmerged")) { $result += "FC-18" }
    if ($tagSet.Contains("junk") -or $tagSet.Contains("non-game") -or $tagSet.Contains("demo") -or $tagSet.Contains("homebrew") -or $tagSet.Contains("hack")) { $result += "FC-19" }
    if ($tagSet.Contains("corrupt") -or $tagSet.Contains("truncated") -or $tagSet.Contains("broken-set") -or $tagSet.Contains("corrupt-archive") -or $tagSet.Contains("truncated-rom")) { $result += "FC-20" }
    
    return ($result | Select-Object -Unique)
}

function Inc-SpecialArea($area) {
    if (-not $specialAreas.ContainsKey($area)) { $specialAreas[$area] = 0 }
    $specialAreas[$area]++
}

$uniqueSystems = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$coveredFC = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$biosSystems = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($entry in $entries) {
    $ck = $entry.expected.consoleKey
    if ($ck) {
        $family = Get-PlatformFamily $ck
        $familyCounts[$family]++
        $null = $uniqueSystems.Add($ck)
        $systemCounts[$ck] = ($systemCounts[$ck] ?? 0) + 1
    }

    $tags = @($entry.tags)
    $fcs = Get-Fallklassen $tags
    foreach ($fc in $fcs) {
        $fcCounts[$fc]++
        $null = $coveredFC.Add($fc)
    }

    $tagSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$tags, [System.StringComparer]::OrdinalIgnoreCase)

    # Special areas
    if ($tagSet.Contains("bios")) { 
        Inc-SpecialArea "biosTotal"
        if ($ck) { $null = $biosSystems.Add($ck) }
    }
    if ($tagSet.Contains("parent")) { Inc-SpecialArea "arcadeParent" }
    if ($tagSet.Contains("clone")) { Inc-SpecialArea "arcadeClone" }
    if ($tagSet.Contains("arcade-split") -or $tagSet.Contains("arcade-merged") -or $tagSet.Contains("arcade-non-merged") -or $tagSet.Contains("arcade-nonmerged")) { Inc-SpecialArea "arcadeSplitMergedNonMerged" }
    if ($tagSet.Contains("arcade-bios")) { Inc-SpecialArea "arcadeBios" }
    if ($tagSet.Contains("arcade-chd") -or $tagSet.Contains("arcade-game-chd")) { Inc-SpecialArea "arcadeChdSupplement" }
    if ($tagSet.Contains("bios-wrong-name") -or $tagSet.Contains("bios-wrong-folder") -or $tagSet.Contains("bios-false-positive") -or $tagSet.Contains("bios-shared")) { Inc-SpecialArea "biosErrorModes" }
    if ($tagSet.Contains("arcade-confusion-split-merged") -or $tagSet.Contains("arcade-confusion-merged-nonmerged")) { Inc-SpecialArea "arcadeConfusion" }
    if ($tagSet.Contains("cross-system") -or $tagSet.Contains("cross-system-ambiguity")) {
        if ($ck -in @("PS1","PS2","PS3","PSP")) { Inc-SpecialArea "psDisambiguation" }
        if ($ck -in @("GB","GBC")) { Inc-SpecialArea "gbGbcCgb" }
        if ($ck -in @("MD","32X")) { Inc-SpecialArea "md32x" }
        if ($ck -in @("SAT","DC")) { Inc-SpecialArea "satDcDisambiguation" }
        if ($ck -in @("PCE","PCECD")) { Inc-SpecialArea "pcePcecdDisambiguation" }
    }
    if ($tagSet.Contains("multi-file")) { Inc-SpecialArea "multiFileSets" }
    if ($tagSet.Contains("multi-disc")) { Inc-SpecialArea "multiDisc" }
    if ($tagSet.Contains("chd-raw-sha1")) { Inc-SpecialArea "chdRawSha1" }
    if ($tagSet.Contains("no-intro") -or $tagSet.Contains("dat-nointro")) { Inc-SpecialArea "datNoIntro" }
    if ($tagSet.Contains("redump") -or $tagSet.Contains("dat-redump")) { Inc-SpecialArea "datRedump" }
    if ($tagSet.Contains("mame") -or $tagSet.Contains("dat-mame")) { Inc-SpecialArea "datMame" }
    if ($tagSet.Contains("tosec") -or $tagSet.Contains("dat-tosec")) { Inc-SpecialArea "datTosec" }
    if ($tagSet.Contains("directory-based")) { Inc-SpecialArea "directoryBased" }
    if ($tagSet.Contains("headerless")) { Inc-SpecialArea "headerless" }
    if ($tagSet.Contains("cue-bin")) { Inc-SpecialArea "cueBin" }
    if ($tagSet.Contains("gdi-tracks")) { Inc-SpecialArea "gdiTracks" }
    if ($tagSet.Contains("ccd-img") -or $tagSet.Contains("mds-mdf")) { Inc-SpecialArea "ccdMds" }
    if ($tagSet.Contains("m3u-playlist")) { Inc-SpecialArea "m3uPlaylist" }
    if ($tagSet.Contains("serial-number")) { Inc-SpecialArea "serialNumber" }
    if ($tagSet.Contains("header-vs-headerless-pair")) { Inc-SpecialArea "headerVsHeaderlessPairs" }
    if ($tagSet.Contains("container-cso") -or $tagSet.Contains("container-wia") -or $tagSet.Contains("container-rvz") -or $tagSet.Contains("container-wbfs")) { Inc-SpecialArea "containerVariants" }
    if ($tagSet.Contains("keyword-detection")) { Inc-SpecialArea "keywordOnly" }
}

$specialAreas["biosSystems"] = $biosSystems.Count

Write-Host ""
Write-Host "=== PLATFORM FAMILIES ==="
$familyCounts.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }

Write-Host ""
Write-Host "=== UNIQUE SYSTEMS: $($uniqueSystems.Count) ==="
Write-Host "=== COVERED FC: $($coveredFC.Count) ==="

Write-Host ""
Write-Host "=== CASE CLASSES (FC) ==="
$fcCounts.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }

Write-Host ""
Write-Host "=== SPECIAL AREAS ==="
$specialAreas.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }

Write-Host ""
Write-Host "=== SYSTEM ENTRY COUNTS ==="
$systemCounts.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }

# Tier analysis
$tier1Systems = @("NES","SNES","N64","GBA","GB","GBC","MD","PS1","PS2")
$tier2Systems = @("32X","PSP","SAT","DC","GC","WII","SMS","GG","PCE","LYNX","A78","A26","NDS","3DS","SWITCH","AMIGA")

Write-Host ""
Write-Host "=== TIER DEPTH ==="
Write-Host "  Tier1 systems:"
$tier1Min = [int]::MaxValue
foreach ($s in $tier1Systems) {
    $c = $systemCounts[$s] ?? 0
    Write-Host "    $s : $c"
    if ($c -lt $tier1Min) { $tier1Min = $c }
}
Write-Host "  Tier1 minimum: $tier1Min"

Write-Host "  Tier2 systems:"
$tier2Min = [int]::MaxValue
foreach ($s in $tier2Systems) {
    $c = $systemCounts[$s] ?? 0
    Write-Host "    $s : $c"
    if ($c -lt $tier2Min) { $tier2Min = $c }
}
Write-Host "  Tier2 minimum: $tier2Min"
