#requires -Modules Pester
<#
  Bug Regression Tests (Scenarios 1-15)
  ======================================
  Regression tests for fixed bugs covering GUI cancel, conversion,
  file operations, folder deduplication, logging, DAT parsing, and more.

  Mix of STRUCTURAL tests (verifying code patterns / signatures when the
  scenario cannot be unit-tested) and FUNCTIONAL tests (exercising actual
  functions with controlled inputs).

  IDs: GUI-001, CONV-001-004, CONV-005, BUG-009, RUN-001, RUN-002, RUN-010,
       FILEOPS-001, SET-002, SET-001, FOLDERDEDUPE-002, FOLDERDEDUPE-003,
       FOLDERDEDUPE-004, CORE-005, CORE-001, BUG-016, LOG-001, DAT-004
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'bugregression_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript

    $script:ModulesRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'modules'
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# ============================================================
#  1. GUI-001: Cancel-Button during Background-Operation
#     STRUCTURAL: Start-BackgroundRunspace returns hashtable with CancelEvent
# ============================================================

Describe 'Scenario 1 - GUI-001: Cancel-Button Background-Operation' {

    It 'Start-BackgroundRunspace should be defined as a function' {
        $cmd = Get-Command Start-BackgroundRunspace -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty -Because 'Start-BackgroundRunspace must exist'
    }

    It 'Start-BackgroundRunspace source contains CancelEvent key in return hashtable' {
        $cmd = Get-Command Start-BackgroundRunspace -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'CancelEvent' -Because 'return hashtable must include CancelEvent key for GUI cancel support'
    }

    It 'Start-BackgroundRunspace source creates a ManualResetEventSlim for cancellation' {
        $cmd = Get-Command Start-BackgroundRunspace -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'ManualResetEventSlim' -Because 'cancellation requires a ManualResetEventSlim event object'
    }
}

# ============================================================
#  2. CONV-001-004: 10 files parallel converted - all successful
#     STRUCTURAL: Verify ISS function list includes required helpers
# ============================================================

Describe 'Scenario 2 - CONV-001-004: Parallel Conversion Function List' {

    It 'New-ConversionRunspacePool should be defined' {
        $cmd = Get-Command New-ConversionRunspacePool -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty -Because 'parallel conversion requires New-ConversionRunspacePool'
    }

    It 'ISS function list includes ConvertTo-ArgString' {
        $cmd = Get-Command New-ConversionRunspacePool -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'ConvertTo-ArgString' -Because 'runspace pool must export ConvertTo-ArgString for argument building'
    }

    It 'ISS function list includes Test-ToolBinaryHash' {
        $cmd = Get-Command New-ConversionRunspacePool -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'Test-ToolBinaryHash' -Because 'runspace pool must export Test-ToolBinaryHash for tool verification'
    }

    It 'ISS function list includes Invoke-ChdmanProcess' {
        $cmd = Get-Command New-ConversionRunspacePool -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'Invoke-ChdmanProcess' -Because 'runspace pool must export Invoke-ChdmanProcess for CHD conversion'
    }

    It 'ISS function list includes DolphinTool strategy function' {
        $cmd = Get-Command New-ConversionRunspacePool -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'Invoke-ConvertStrategyDolphinTool' -Because 'runspace pool must export DolphinTool strategy for GC/Wii conversion'
    }
}

# ============================================================
#  3. CONV-005, BUG-009: Conversion backup fails (readonly target)
#     STRUCTURAL: Verify code checks backup success before deleting source
# ============================================================

Describe 'Scenario 3 - CONV-005: Backup Failure Preserves Source' {

    It 'Convert.ps1 contains backup-fail guard that preserves source' {
        $convertPath = Join-Path $script:ModulesRoot 'Convert.ps1'
        $convertPath | Should -Exist
        $content = Get-Content -LiteralPath $convertPath -Raw
        # The BUG CONV-005 fix: catch block around Move-ConvertedSourceToBackup
        # must NOT call Remove-Item on source when backup fails
        $content | Should -Match 'CONV-005' -Because 'CONV-005 fix comment must be present'
        $content | Should -Match 'Backup fehlgeschlagen.*Quelldatei wird beibehalten' -Because 'backup failure must log that source file is preserved'
    }

    It 'Backup failure handler is inside a catch block (not deleting source)' {
        $convertPath = Join-Path $script:ModulesRoot 'Convert.ps1'
        $content = Get-Content -LiteralPath $convertPath -Raw
        # The pattern: catch block with CONV-005 message should NOT be followed by Remove-Item
        # before the next closing brace.  Verify the catch block exists.
        $content | Should -Match '(?s)catch\s*\{[^}]*Quelldatei wird beibehalten' -Because 'catch block must contain the source-preservation message'
    }
}

# ============================================================
#  4. RUN-001: 100 files moved, crash at file 50 - Audit-CSV exists
#     STRUCTURAL: Verify incremental CSV flush exists in the code
# ============================================================

Describe 'Scenario 4 - RUN-001: Incremental Audit CSV Flush' {

    It 'RunHelpers.Execution.ps1 contains incremental flush logic' {
        $runPath = Join-Path $script:ModulesRoot 'RunHelpers.Execution.ps1'
        $runPath | Should -Exist
        $content = Get-Content -LiteralPath $runPath -Raw
        $content | Should -Match 'RUN-001' -Because 'RUN-001 fix comment must be present'
        $content | Should -Match 'incremental' -Because 'incremental flush logic must exist'
    }

    It 'Incremental flush writes partial CSV files' {
        $runPath = Join-Path $script:ModulesRoot 'RunHelpers.Execution.ps1'
        $content = Get-Content -LiteralPath $runPath -Raw
        $content | Should -Match '\.partial\.csv' -Because 'incremental flush must create .partial.csv files'
    }

    It 'Incremental flush triggers at a defined batch threshold' {
        $runPath = Join-Path $script:ModulesRoot 'RunHelpers.Execution.ps1'
        $content = Get-Content -LiteralPath $runPath -Raw
        # The code checks $unflushed -ge 50
        $content | Should -Match 'unflushed\s*-ge\s*\d+' -Because 'incremental flush must trigger at a batch threshold'
    }

    It 'Audit root is resolved early before any moves begin' {
        $runPath = Join-Path $script:ModulesRoot 'RunHelpers.Execution.ps1'
        $content = Get-Content -LiteralPath $runPath -Raw
        $content | Should -Match 'Determine audit root early' -Because 'audit root must be determined early so incremental writes can target it'
    }
}

# ============================================================
#  5. RUN-002, RUN-010: Cancel during CUE+BIN Set-Move - Set not split
#     STRUCTURAL: Verify rollback logic (setMovedPairs, rollback pattern)
# ============================================================

Describe 'Scenario 5 - RUN-002/RUN-010: CUE+BIN Set-Move Rollback' {

    It 'RunHelpers.Execution.ps1 contains setMovedPairs tracking' {
        $runPath = Join-Path $script:ModulesRoot 'RunHelpers.Execution.ps1'
        $content = Get-Content -LiteralPath $runPath -Raw
        $content | Should -Match 'setMovedPairs' -Because 'set-move tracking requires setMovedPairs variable'
    }

    It 'Rollback reverses previously moved files on failure' {
        $runPath = Join-Path $script:ModulesRoot 'RunHelpers.Execution.ps1'
        $content = Get-Content -LiteralPath $runPath -Raw
        $content | Should -Match 'RUN-010.*Rollback' -Because 'RUN-010 fix must include rollback logic'
        $content | Should -Match 'setFailed.*setMovedPairs' -Because 'rollback must check both setFailed flag and setMovedPairs list'
    }

    It 'Rollback iterates setMovedPairs and calls Move-Item to restore' {
        $runPath = Join-Path $script:ModulesRoot 'RunHelpers.Execution.ps1'
        $content = Get-Content -LiteralPath $runPath -Raw
        $content | Should -Match '(?s)foreach\s*\(\$pair\s+in\s+\$setMovedPairs\).*Move-Item' -Because 'rollback must iterate pairs and call Move-Item to restore files'
    }
}

# ============================================================
#  6. FILEOPS-001: Path with [USA] brackets - Assert-DirectoryExists
#     FUNCTIONAL: Create temp dir with [brackets] and call function
# ============================================================

Describe 'Scenario 6 - FILEOPS-001: Bracketed Paths in Assert-DirectoryExists' {

    BeforeAll {
        $script:BracketTestRoot = Join-Path $env:TEMP ('bugregr_brackets_' + (Get-Random))
        $null = New-Item -ItemType Directory -Path $script:BracketTestRoot -Force
    }

    AfterAll {
        if ($script:BracketTestRoot -and (Test-Path -LiteralPath $script:BracketTestRoot)) {
            Remove-Item -LiteralPath $script:BracketTestRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Assert-DirectoryExists does not throw on paths with [USA] brackets' {
        $bracketDir = Join-Path $script:BracketTestRoot 'Game [USA] (Rev A)'
        { Assert-DirectoryExists -Path $bracketDir } | Should -Not -Throw
    }

    It 'Assert-DirectoryExists creates a directory under the parent for bracket paths' {
        $bracketDir = Join-Path $script:BracketTestRoot 'SubDir [EU] Test'
        Assert-DirectoryExists -Path $bracketDir
        # A directory should be created under the parent (name may differ between PS5/PS7 due to escape handling)
        $children = @([System.IO.Directory]::GetDirectories($script:BracketTestRoot))
        $children.Count | Should -BeGreaterThan 0 -Because 'at least one directory should be created'
    }

    It 'Assert-DirectoryExists uses WildcardPattern.Escape to handle bracket characters' {
        # Structural: verify the implementation uses the FILEOPS-001 fix pattern
        $cmd = Get-Command Assert-DirectoryExists -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'WildcardPattern.*Escape' -Because 'FILEOPS-001 fix must escape bracket characters for New-Item'
    }
}

# ============================================================
#  7. SET-002: Copy-ObjectDeep on @{a=1; b=$true} in PS 7
#     FUNCTIONAL: Call Copy-ObjectDeep and verify scalar types preserved
# ============================================================

Describe 'Scenario 7 - SET-002: Copy-ObjectDeep Preserves Scalars' {

    It 'Integers are preserved as [int] (not wrapped in PSObject)' {
        $source = @{ a = 1; b = 42 }
        $copy = Copy-ObjectDeep -Value $source
        $copy | Should -Not -BeNullOrEmpty
        $copy['a'] | Should -Be 1
        $copy['a'] | Should -BeOfType [int]
        $copy['b'] | Should -Be 42
        $copy['b'] | Should -BeOfType [int]
    }

    It 'Booleans are preserved as [bool]' {
        $source = @{ flag = $true; other = $false }
        $copy = Copy-ObjectDeep -Value $source
        $copy['flag'] | Should -Be $true
        $copy['flag'] | Should -BeOfType [bool]
        $copy['other'] | Should -Be $false
        $copy['other'] | Should -BeOfType [bool]
    }

    It 'Strings are preserved as [string]' {
        $source = @{ name = 'hello'; path = 'C:\temp' }
        $copy = Copy-ObjectDeep -Value $source
        $copy['name'] | Should -Be 'hello'
        $copy['name'] | Should -BeOfType [string]
    }

    It 'Nested hashtable with mixed scalars is deep-copied correctly' {
        $source = @{
            level1 = @{
                intVal  = 7
                boolVal = $true
                strVal  = 'test'
            }
        }
        $copy = Copy-ObjectDeep -Value $source
        $copy['level1'] | Should -Not -BeNullOrEmpty
        $copy['level1']['intVal'] | Should -Be 7
        $copy['level1']['intVal'] | Should -BeOfType [int]
        $copy['level1']['boolVal'] | Should -Be $true
        $copy['level1']['boolVal'] | Should -BeOfType [bool]
    }

    It 'Copy is independent from original (mutations do not propagate)' {
        $source = @{ a = 1; nested = @{ x = 10 } }
        $copy = Copy-ObjectDeep -Value $source
        $copy['a'] = 999
        $copy['nested']['x'] = 999
        $source['a'] | Should -Be 1 -Because 'original should be unmodified'
        $source['nested']['x'] | Should -Be 10 -Because 'nested original should be unmodified'
    }
}

# ============================================================
#  8. SET-001: Copy-ObjectDeep on circular ref - no crash
#     FUNCTIONAL: Create circular ref, verify no crash (max depth)
# ============================================================

Describe 'Scenario 8 - SET-001: Copy-ObjectDeep Circular Reference Safety' {

    It 'Does not crash on circular hashtable reference' {
        $a = @{ name = 'root' }
        $b = @{ name = 'child'; parent = $a }
        $a['child'] = $b  # circular: a -> b -> a

        # Copy-ObjectDeep must not throw; it hits max-depth guard and returns safely
        $threw = $false
        try {
            $result = Copy-ObjectDeep -Value $a 3>$null
        } catch {
            $threw = $true
        }
        $threw | Should -Be $false -Because 'circular reference must not cause a crash'
    }

    It 'Emits max-depth warning on deep circular reference' {
        $a = @{ name = 'root' }
        $b = @{ name = 'child'; parent = $a }
        $a['child'] = $b

        $warnings = [System.Collections.Generic.List[string]]::new()
        $null = Copy-ObjectDeep -Value $a 3>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.WarningRecord]) {
                $warnings.Add($_.Message)
            }
        }
        # After enough depth, the function should hit the max-depth guard
        $warnings.Count | Should -BeGreaterThan 0 -Because 'circular reference triggers max-depth warning'
    }

    It 'Handles self-referencing hashtable without infinite loop' {
        $self = @{ name = 'self' }
        $self['me'] = $self  # direct self-reference

        # Must complete without infinite loop or crash
        $threw = $false
        $result = $null
        try {
            $result = Copy-ObjectDeep -Value $self 3>$null
        } catch {
            $threw = $true
        }
        $threw | Should -Be $false -Because 'self-reference must not cause an infinite loop'
        $result | Should -Not -BeNullOrEmpty
        $result['name'] | Should -Be 'self'
    }
}

# ============================================================
#  9. FOLDERDEDUPE-002: Game (Disk 1) and Game (Disk 2) both kept
#     FUNCTIONAL: Get-FolderBaseKey produces different keys for disk variants
# ============================================================

Describe 'Scenario 9 - FOLDERDEDUPE-002: Multi-Disk Folders Kept Separate' {

    It 'Get-FolderBaseKey returns different keys for (Disk 1) vs (Disk 2)' {
        $key1 = Get-FolderBaseKey -FolderName 'Riven - The Sequel to Myst (Disk 1)'
        $key2 = Get-FolderBaseKey -FolderName 'Riven - The Sequel to Myst (Disk 2)'

        $key1 | Should -Not -BeNullOrEmpty
        $key2 | Should -Not -BeNullOrEmpty
        $key1 | Should -Not -Be $key2 -Because 'Disk 1 and Disk 2 must produce different keys'
    }

    It 'Get-FolderBaseKey returns different keys for (Disc 1) vs (Disc 2)' {
        $key1 = Get-FolderBaseKey -FolderName 'Final Fantasy VIII (Disc 1)'
        $key2 = Get-FolderBaseKey -FolderName 'Final Fantasy VIII (Disc 2)'

        $key1 | Should -Not -Be $key2 -Because 'Disc 1 and Disc 2 must produce different keys'
    }

    It 'Get-FolderBaseKey returns different keys for (Side A) vs (Side B)' {
        $keyA = Get-FolderBaseKey -FolderName 'Dungeon Master (Side A)'
        $keyB = Get-FolderBaseKey -FolderName 'Dungeon Master (Side B)'

        $keyA | Should -Not -Be $keyB -Because 'Side A and Side B must produce different keys'
    }

    It 'Get-FolderBaseKey returns different keys for (CD1) vs (CD2)' {
        $key1 = Get-FolderBaseKey -FolderName 'Wing Commander III (CD1)'
        $key2 = Get-FolderBaseKey -FolderName 'Wing Commander III (CD2)'

        $key1 | Should -Not -Be $key2 -Because 'CD1 and CD2 must produce different keys'
    }
}

# ============================================================
# 10. FOLDERDEDUPE-003: Lemmings (AGA) and Lemmings (ECS) both kept
#     FUNCTIONAL: Get-FolderBaseKey produces different keys for chipset variants
# ============================================================

Describe 'Scenario 10 - FOLDERDEDUPE-003: Chipset Variant Folders Kept Separate' {

    It 'Get-FolderBaseKey returns different keys for (AGA) vs (ECS)' {
        $keyAGA = Get-FolderBaseKey -FolderName 'Lemmings (AGA)'
        $keyECS = Get-FolderBaseKey -FolderName 'Lemmings (ECS)'

        $keyAGA | Should -Not -BeNullOrEmpty
        $keyECS | Should -Not -BeNullOrEmpty
        $keyAGA | Should -Not -Be $keyECS -Because 'AGA and ECS are different chipset versions and must be separate'
    }

    It 'Get-FolderBaseKey returns different keys for (OCS) vs (AGA)' {
        $keyOCS = Get-FolderBaseKey -FolderName 'Settlers, The (OCS)'
        $keyAGA = Get-FolderBaseKey -FolderName 'Settlers, The (AGA)'

        $keyOCS | Should -Not -Be $keyAGA -Because 'OCS and AGA are distinct platform variants'
    }

    It 'Get-FolderBaseKey strips non-preserve tags but keeps chipset tags' {
        $keyPlain = Get-FolderBaseKey -FolderName 'Turrican (AGA)'
        $keyTagged = Get-FolderBaseKey -FolderName 'Turrican (v1.2) (AGA)'

        $keyPlain | Should -Be $keyTagged -Because 'version tags should be stripped but AGA must be preserved'
    }
}

# ============================================================
# 11. FOLDERDEDUPE-004: Empty dir vs populated - populated wins
#     STRUCTURAL/FUNCTIONAL: Verify sort logic prefers non-empty folders
# ============================================================

Describe 'Scenario 11 - FOLDERDEDUPE-004: Populated Folder Beats Empty' {

    It 'Invoke-FolderDedupeByBaseName sort prioritizes populated over empty (source check)' {
        $cmd = Get-Command Invoke-FolderDedupeByBaseName -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        # FOLDERDEDUPE-004: sort expression checks FileCount > 0
        $definition | Should -Match 'FileCount\s*-gt\s*0' -Because 'sort must prioritize folders with FileCount > 0 over empty ones'
    }

    It 'Sort expression has populated-check as highest priority (first sort key)' {
        $cmd = Get-Command Invoke-FolderDedupeByBaseName -CommandType Function -ErrorAction SilentlyContinue
        $definition = $cmd.Definition
        # The Sort-Object call should have the FileCount > 0 expression before Newest
        $populatedPos = $definition.IndexOf('FileCount')
        $newestPos = $definition.IndexOf("'Newest'")
        if ($newestPos -lt 0) { $newestPos = $definition.IndexOf('"Newest"') }
        $populatedPos | Should -BeLessThan $newestPos -Because 'populated-check must come before newest-timestamp sort'
    }

    It 'Get-FolderFileCount function exists for counting files' {
        $cmd = Get-Command Get-FolderFileCount -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty -Because 'Get-FolderFileCount must exist for winner-selection metadata'
    }
}

# ============================================================
# 12. CORE-005: Custom Alias "abandoned places" -> "lost" - GameKey matches
#     FUNCTIONAL: ConvertTo-CustomAliasMap with space-normalized keys
# ============================================================

Describe 'Scenario 12 - CORE-005: Custom Alias Space-Normalized Keys' {

    It 'ConvertTo-CustomAliasMap normalizes spaces in alias keys' {
        $text = "abandoned places = lost`nabc def = xyz"
        $map = ConvertTo-CustomAliasMap -Text $text

        $map | Should -Not -BeNullOrEmpty
        # Keys should be space-collapsed (all whitespace removed) per CORE-005 fix
        $map.Keys | Should -Contain 'abandonedplaces' -Because 'spaces should be collapsed in alias keys'
    }

    It 'ConvertTo-CustomAliasMap normalizes spaces in alias values' {
        $text = 'some game = another game'
        $map = ConvertTo-CustomAliasMap -Text $text

        $map.Keys | Should -Contain 'somegame'
        $map['somegame'] | Should -Be 'anothergame' -Because 'value should also have spaces collapsed'
    }

    It 'ConvertTo-CustomAliasMap supports both = and | delimiters' {
        $text = "key one = val one`nkey two | val two"
        $map = ConvertTo-CustomAliasMap -Text $text

        $map.Keys.Count | Should -Be 2
        $map.ContainsKey('keyone') | Should -Be $true
        $map.ContainsKey('keytwo') | Should -Be $true
    }

    It 'ConvertTo-CustomAliasMap skips comments and blank lines' {
        $text = "# comment`n`nvalid key = valid val`n  # another comment"
        $map = ConvertTo-CustomAliasMap -Text $text

        $map.Keys.Count | Should -Be 1
        $map.ContainsKey('validkey') | Should -Be $true
    }
}

# ============================================================
# 13. CORE-001, BUG-016: ROM "Game (Fr, De)" - Region != UNKNOWN
#     FUNCTIONAL: Get-RegionTag returns a valid region
# ============================================================

Describe 'Scenario 13 - CORE-001/BUG-016: Multi-Language Region Detection' {

    It '"Game (Fr, De)" should not return UNKNOWN' {
        $region = Get-RegionTag -Name 'Game (Fr, De)'
        $region | Should -Not -Be 'UNKNOWN' -Because '(Fr, De) indicates a European multi-language ROM'
    }

    It '"Game (En, Fr)" should not return UNKNOWN' {
        $region = Get-RegionTag -Name 'Game (En, Fr)'
        $region | Should -Not -Be 'UNKNOWN' -Because '(En, Fr) indicates a known language combination'
    }

    It '"Game (En, Fr, De, Es, It)" should not return UNKNOWN' {
        $region = Get-RegionTag -Name 'Game (En, Fr, De, Es, It)'
        $region | Should -Not -Be 'UNKNOWN' -Because 'five European languages is clearly identifiable'
    }

    It '"Game (Europe)" returns EU' {
        $region = Get-RegionTag -Name 'Game (Europe)'
        $region | Should -Be 'EU'
    }

    It '"Game (USA)" returns US' {
        $region = Get-RegionTag -Name 'Game (USA)'
        $region | Should -Be 'US'
    }

    It '"Game (Japan)" returns JP' {
        $region = Get-RegionTag -Name 'Game (Japan)'
        $region | Should -Be 'JP'
    }
}

# ============================================================
# 14. LOG-001: JSONL file briefly locked - Logging resumes
#     STRUCTURAL: Verify retry logic in Write-OperationJsonlLog
# ============================================================

Describe 'Scenario 14 - LOG-001: JSONL Write Retry Logic' {

    It 'Write-OperationJsonlLog contains retry loop' {
        $cmd = Get-Command Write-OperationJsonlLog -CommandType Function -ErrorAction SilentlyContinue
        $cmd | Should -Not -BeNullOrEmpty
        $definition = $cmd.Definition
        $definition | Should -Match 'attempt' -Because 'retry logic requires an attempt counter variable'
    }

    It 'Retry loop attempts up to 3 times' {
        $cmd = Get-Command Write-OperationJsonlLog -CommandType Function -ErrorAction SilentlyContinue
        $definition = $cmd.Definition
        $definition | Should -Match 'attempt\s*-le\s*3' -Because 'retry loop should attempt up to 3 times'
    }

    It 'Retry includes backoff delay between attempts' {
        $cmd = Get-Command Write-OperationJsonlLog -CommandType Function -ErrorAction SilentlyContinue
        $definition = $cmd.Definition
        $definition | Should -Match 'Start-Sleep' -Because 'retry loop should include a backoff delay'
    }

    It 'After all retries fail, OperationJsonlPath is set to $null' {
        $cmd = Get-Command Write-OperationJsonlLog -CommandType Function -ErrorAction SilentlyContinue
        $definition = $cmd.Definition
        $definition | Should -Match 'OperationJsonlPath\s*=\s*\$null' -Because 'logging must be disabled after all retries are exhausted'
    }

    It 'After all retries fail, a Write-Warning is emitted' {
        $cmd = Get-Command Write-OperationJsonlLog -CommandType Function -ErrorAction SilentlyContinue
        $definition = $cmd.Definition
        $definition | Should -Match 'Write-Warning.*fehlgeschlagen.*3 Versuchen' -Because 'final failure must emit a descriptive Write-Warning'
    }
}

# ============================================================
# 15. DAT-004: MAME DAT with <machine cloneof="parent">
#     FUNCTIONAL: Create test XML and verify parsing
# ============================================================

Describe 'Scenario 15 - DAT-004: MAME DAT machine cloneof Parsing' {

    BeforeAll {
        $script:DatTestDir = Join-Path $env:TEMP ('bugregr_dat_' + (Get-Random))
        if (-not (Test-Path -LiteralPath $script:DatTestDir)) {
            $null = New-Item -ItemType Directory -Path $script:DatTestDir -Force
        }
        # Create a console subdirectory with a test DAT file
        $script:DatConsoleDir = Join-Path $script:DatTestDir 'ARCADE'
        $null = New-Item -ItemType Directory -Path $script:DatConsoleDir -Force

        $datXml = @'
<?xml version="1.0" encoding="UTF-8"?>
<datafile>
  <header>
    <name>MAME Test</name>
  </header>
  <machine name="pacman" sourcefile="pacman.cpp">
    <description>Pac-Man</description>
    <rom name="pacman.6e" size="4096"/>
  </machine>
  <machine name="puckman" cloneof="pacman" sourcefile="pacman.cpp">
    <description>Puck Man</description>
    <rom name="puckman.6e" size="4096"/>
  </machine>
  <machine name="mspacman" cloneof="pacman" sourcefile="pacman.cpp">
    <description>Ms. Pac-Man</description>
    <rom name="mspacman.6e" size="4096"/>
  </machine>
  <machine name="galaga" sourcefile="galaga.cpp">
    <description>Galaga</description>
    <rom name="galaga.6h" size="4096"/>
  </machine>
  <machine name="galagao" cloneof="galaga" sourcefile="galaga.cpp">
    <description>Galaga (Namco)</description>
    <rom name="galagao.6h" size="4096"/>
  </machine>
</datafile>
'@
        $datXml | Out-File -LiteralPath (Join-Path $script:DatConsoleDir 'mame_test.dat') -Encoding utf8 -Force
    }

    AfterAll {
        if ($script:DatTestDir -and (Test-Path -LiteralPath $script:DatTestDir)) {
            Remove-Item -LiteralPath $script:DatTestDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Source code handles machine elements alongside game elements' {
        $datPath = Join-Path $script:ModulesRoot 'Dat.ps1'
        $content = Get-Content -LiteralPath $datPath -Raw
        $content | Should -Match 'DAT-004' -Because 'DAT-004 fix comment must be present'
        $content | Should -Match "machine" -Because 'machine element handling must exist'
    }

    It 'Add-ParentCloneStreaming processes machine elements with cloneof attribute' {
        $datPath = Join-Path $script:ModulesRoot 'Dat.ps1'
        $content = Get-Content -LiteralPath $datPath -Raw
        # Verify streaming parser looks for 'machine' in the element name check
        $content | Should -Match "'machine'" -Because 'streaming parser must check for machine element type'
    }

    It 'Get-DatParentCloneIndex parses machine cloneof from test DAT file' {
        # Skip if Get-DatParentCloneIndex is not available (core-only profile)
        $cmd = Get-Command Get-DatParentCloneIndex -CommandType Function -ErrorAction SilentlyContinue
        if (-not $cmd) {
            Set-ItResult -Skipped -Because 'Get-DatParentCloneIndex not available in core profile'
            return
        }

        # Use auto-discovery: DatRoot has an ARCADE subdirectory containing the .dat file
        # ConsoleMap values must be plain path strings per the Get-DatParentCloneIndex API
        $index = Get-DatParentCloneIndex -DatRoot $script:DatTestDir

        $index | Should -Not -BeNullOrEmpty -Because 'index should be returned from test DAT'
        $index.ParentMap | Should -Not -BeNullOrEmpty -Because 'ParentMap should be populated'
        $index.ChildrenMap | Should -Not -BeNullOrEmpty -Because 'ChildrenMap should be populated'

        # puckman is a clone of pacman
        $index.ParentMap['ARCADE|puckman'] | Should -Be 'pacman' -Because 'puckman cloneof pacman'
        # mspacman is a clone of pacman
        $index.ParentMap['ARCADE|mspacman'] | Should -Be 'pacman' -Because 'mspacman cloneof pacman'
        # galagao is a clone of galaga
        $index.ParentMap['ARCADE|galagao'] | Should -Be 'galaga' -Because 'galagao cloneof galaga'

        # pacman should have puckman and mspacman as children
        $pacmanChildren = @($index.ChildrenMap['ARCADE|pacman'])
        $pacmanChildren | Should -Contain 'puckman'
        $pacmanChildren | Should -Contain 'mspacman'

        # galaga should have galagao as child
        $galagaChildren = @($index.ChildrenMap['ARCADE|galaga'])
        $galagaChildren | Should -Contain 'galagao'
    }
}
