#requires -Modules Pester

BeforeAll {
    Add-Type -AssemblyName System.Windows.Forms

    $moduleRoot = Join-Path $PSScriptRoot '..\modules'
    . (Join-Path $moduleRoot 'LruCache.ps1')
    . (Join-Path $moduleRoot 'Tools.ps1')
    . (Join-Path $moduleRoot 'FileOps.ps1')
    . (Join-Path $moduleRoot 'Report.ps1')
    . (Join-Path $moduleRoot 'Core.ps1')
    . (Join-Path $moduleRoot 'Sets.ps1')
    . (Join-Path $moduleRoot 'RunHelpers.ps1')

    # Collect disposable WinForms controls for cleanup
    $script:DisposableControls = [System.Collections.Generic.List[System.IDisposable]]::new()

    # Core.ps1 now defines these directly (no more *Impl wrappers needed)

    if (-not (Get-Command Get-FilesSafe -ErrorAction SilentlyContinue)) {
        function Get-FilesSafe { param([string]$Root, [string[]]$ExcludePrefixes, [object]$AllowedExtensions) return @() }
    }
    if (-not (Get-Command Get-ConsoleType -ErrorAction SilentlyContinue)) {
        function Get-ConsoleType { param([string]$RootPath, [string]$FilePath, [string]$Extension) return 'UNKNOWN' }
    }
}

AfterAll {
    foreach ($d in @($script:DisposableControls)) {
        try { $d.Dispose() } catch {}
    }
}

Describe 'RunHelpers feature additions' {
    It 'New-OperationResult should expose normalized Outcome contract' {
        (New-OperationResult -Status 'completed' -Reason 'ok').Outcome | Should -Be 'OK'
        (New-OperationResult -Status 'skipped' -Reason 'skip').Outcome | Should -Be 'SKIP'
        (New-OperationResult -Status 'blocked' -Reason 'failed').Outcome | Should -Be 'ERROR'

        $extended = New-OperationResult -Status 'ok' -Reason 'done' -Warnings @('w1') -Metrics @{ elapsedMs = 12 } -Artifacts @{ report = 'a.html' }
        @($extended.Warnings).Count | Should -Be 1
        [int]$extended.Metrics.elapsedMs | Should -Be 12
        [string]$extended.Artifacts.report | Should -Be 'a.html'

        $defaults = New-OperationResult -Status 'continue'
        @($defaults.Warnings).Count | Should -Be 0
        $defaults.Metrics.GetType().Name | Should -Be 'Hashtable'
        $defaults.Artifacts.GetType().Name | Should -Be 'Hashtable'
    }

    It 'Get-DuplicateInspectorRows should return duplicate rows with one winner' {
        $root = Join-Path $env:TEMP ("dup-inspector-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $f1 = Join-Path $root 'Game (EU).chd'
        $f2 = Join-Path $root 'Game (US).chd'
        Set-Content -LiteralPath $f1 -Value 'a' -Encoding ascii
        Set-Content -LiteralPath $f2 -Value 'b' -Encoding ascii

        try {
            Mock -CommandName Get-FilesSafe -MockWith { param($Root, $ExcludePrefixes, $AllowedExtensions) @(Get-ChildItem -LiteralPath $Root -File) }
            Mock -CommandName ConvertTo-GameKey -MockWith { 'game-key' }
            Mock -CommandName Get-ConsoleType -MockWith { 'PS1' }
            Mock -CommandName Get-RegionTag -MockWith {
                param([string]$Name)
                if ($Name -like '*EU*') { return 'EU' }
                return 'US'
            }
            Mock -CommandName Get-VersionScore -MockWith { 0 }
            Mock -CommandName New-FileItem -MockWith {
                param($Root, $MainPath, $Category, $Region, $PreferOrder, $VersionScore, $GameKey, $DatMatch, $BaseName, $FileInfoByPath)
                return [pscustomobject]@{
                    Root = $Root
                    Type = 'FILE'
                    Category = $Category
                    MainPath = $MainPath
                    Region = $Region
                    RegionScore = if ($Region -eq 'EU') { 100 } else { 80 }
                    HeaderScore = 0
                    VersionScore = 0
                    FormatScore = 0
                    GameKey = $GameKey
                    DatMatch = $false
                    SizeBytes = 1
                    BaseName = $BaseName
                    CompletenessScore = 100
                    SizeTieBreakScore = 1
                }
            }

            $rows = @(Get-DuplicateInspectorRows -Roots @($root) -Extensions @('.chd'))
            $rows.Count | Should -Be 2
            (@($rows | Where-Object { $_.Winner }).Count) | Should -Be 1
            (@($rows | Select-Object -ExpandProperty GameKey -Unique).Count) | Should -Be 1
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-ToolSelfTest should return fixed tool result set' {
        $result = Invoke-ToolSelfTest -ToolOverrides @{ chdman = 'Z:\__missing__\chdman.exe' }
        @($result.Results).Count | Should -Be 5
        $chd = @($result.Results | Where-Object { $_.Name -eq 'chdman' })[0]
        $chd.Source | Should -Be 'override'
        $chd.Found | Should -BeFalse
    }

    It 'Invoke-ToolchainDoctor should derive actionable status from tool test result' {
        Mock -CommandName Invoke-ToolSelfTest -MockWith {
            [pscustomobject]@{
                Results = @(
                    [pscustomobject]@{ Name='chdman'; Label='chdman'; Found=$true; Healthy=$true; Version='1'; Path='C:\tools\chdman.exe' },
                    [pscustomobject]@{ Name='dolphintool'; Label='DolphinTool'; Found=$false; Healthy=$false; Version=$null; Path=$null },
                    [pscustomobject]@{ Name='7z'; Label='7z'; Found=$false; Healthy=$false; Version=$null; Path=$null },
                    [pscustomobject]@{ Name='psxtract'; Label='psxtract'; Found=$true; Healthy=$true; Version='1'; Path='C:\tools\psxtract.exe' },
                    [pscustomobject]@{ Name='ciso'; Label='ciso'; Found=$true; Healthy=$true; Version='1'; Path='C:\tools\ciso.exe' }
                )
                HealthyCount = 3
                MissingCount = 2
                WarningCount = 0
            }
        }

        $doctor = Invoke-ToolchainDoctor -ToolOverrides @{} -UseDat:$true -ConvertEnabled:$true
        [int]$doctor.Score | Should -BeLessThan 80
        @($doctor.Recommendations).Count | Should -BeGreaterThan 0
        @($doctor.Recommendations -join ' | ') | Should -Match '7z|Fehlende Tools'
    }

    It 'Invoke-RuleImpactSimulation should report changed winners and restore rule overrides' {
        $script:RX_REGION_ORDERED_OVERRIDE = [pscustomobject]@{ Marker = 'ordered-old' }
        $script:RX_REGION_2LETTER_OVERRIDE = [pscustomobject]@{ Marker = 'two-old' }
        $script:RX_LANG_OVERRIDE = [pscustomobject]@{ Marker = 'lang-old' }

        $script:simCall = 0
        Mock -CommandName Get-DuplicateInspectorRows -MockWith {
            $script:simCall++
            if ($script:simCall -eq 1) {
                return @(
                    [pscustomobject]@{ GameKey = 'gk1'; Winner = $true; MainPath = 'C:\roms\A.chd' }
                )
            }
            return @(
                [pscustomobject]@{ GameKey = 'gk1'; Winner = $true; MainPath = 'C:\roms\B.chd' }
            )
        }
        Mock -CommandName Set-RegionRulesOverride -MockWith {
            [pscustomobject]@{ Success = $true; Errors = @() }
        }

        $result = Invoke-RuleImpactSimulation `
            -Roots @('C:\roms') `
            -Extensions @('.chd') `
            -OrderedText 'EU | \(EU\)' `
            -TwoLetterText 'US | \(US\)' `
            -LangPattern '\((?:En|De)\)'

        $result.Success | Should -BeTrue
        [int]$result.GroupCount | Should -Be 1
        [int]$result.ChangedWinners | Should -Be 1
        [int]@($result.ChangedSamples).Count | Should -Be 1

        ([string]$script:RX_REGION_ORDERED_OVERRIDE.Marker) | Should -Be 'ordered-old'
        ([string]$script:RX_REGION_2LETTER_OVERRIDE.Marker) | Should -Be 'two-old'
        ([string]$script:RX_LANG_OVERRIDE.Marker) | Should -Be 'lang-old'
    }

    It 'Get-IncrementalDryRunDelta should compute added/resolved entries and persist snapshot' {
        $snapshotPath = Join-Path $env:TEMP ("dryrun-delta-test-" + [guid]::NewGuid().ToString('N') + '.json')
        try {
            $previous = [pscustomobject]@{
                schemaVersion = 'dryrun-delta-v1'
                timestampUtc = '2026-01-01T00:00:00.0000000Z'
                entries = @('gk1|C:\roms\A.chd','gk2|C:\roms\B.chd')
            }
            ($previous | ConvertTo-Json -Depth 6) | Out-File -LiteralPath $snapshotPath -Encoding UTF8 -Force

            $result = [pscustomobject]@{
                ReportRows = @(
                    [pscustomobject]@{ Category='GAME'; Action='MOVE'; GameKey='gk1'; MainPath='C:\roms\A.chd' },
                    [pscustomobject]@{ Category='GAME'; Action='MOVE'; GameKey='gk3'; MainPath='C:\roms\C.chd' }
                )
            }

            $delta = Get-IncrementalDryRunDelta -Result $result -SnapshotPath $snapshotPath -MaxSamples 10
            [int]$delta.CurrentCount | Should -Be 2
            [int]$delta.PreviousCount | Should -Be 2
            [int]$delta.AddedCount | Should -Be 1
            [int]$delta.ResolvedCount | Should -Be 1
            [int]@($delta.AddedSamples).Count | Should -Be 1
            [int]@($delta.ResolvedSamples).Count | Should -Be 1
            Test-Path -LiteralPath $snapshotPath | Should -BeTrue
        } finally {
            Remove-Item -LiteralPath $snapshotPath -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Get-DatCoverageHeatmapRows should parse completeness CSV and build heat bars' {
        $csvPath = Join-Path $env:TEMP ("dat-coverage-test-" + [guid]::NewGuid().ToString('N') + '.csv')
        try {
            @(
                [pscustomobject]@{ Console='PS1'; Matched=80; Expected=100; Missing=20; Coverage=80.0 },
                [pscustomobject]@{ Console='PS2'; Matched=10; Expected=50; Missing=40; Coverage=20.0 }
            ) | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $csvPath

            $result = [pscustomobject]@{ DatCompletenessCsvPath = $csvPath }
            $rows = @(Get-DatCoverageHeatmapRows -Result $result -Top 5)

            [int]$rows.Count | Should -Be 2
            [string]$rows[0].Console | Should -Be 'PS1'
            [double]$rows[0].Coverage | Should -Be 80
            ([string]$rows[0].Heat).Length | Should -Be 10
        } finally {
            Remove-Item -LiteralPath $csvPath -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Get-CrossCollectionDedupHints should return keys seen across multiple roots' {
        Mock -CommandName Get-DuplicateInspectorRows -MockWith {
            return @(
                [pscustomobject]@{ GameKey='gk-shared'; Winner=$true; MainPath='C:\romsA\Game (EU).chd' },
                [pscustomobject]@{ GameKey='gk-shared'; Winner=$false; MainPath='C:\romsB\Game (US).chd' },
                [pscustomobject]@{ GameKey='gk-local'; Winner=$true; MainPath='C:\romsA\Other.chd' }
            )
        }

        $hints = @(Get-CrossCollectionDedupHints -Roots @('C:\romsA','C:\romsB') -Extensions @('.chd') -Top 10)
        [int]$hints.Count | Should -Be 1
        [string]$hints[0].GameKey | Should -Be 'gk-shared'
        [int]$hints[0].RootCount | Should -Be 2
        [int]$hints[0].CandidateCount | Should -Be 2
        [string]$hints[0].WinnerPath | Should -Be 'C:\romsA\Game (EU).chd'
    }

    It 'Invoke-SafetyPolicyProfileApply should apply strict/path defaults' {
        $balanced = Invoke-SafetyPolicyProfileApply -Profile 'Balanced'
        $balanced.Name | Should -Be 'Balanced'
        $balanced.Strict | Should -BeTrue
        $balanced.ProtectedPathsText | Should -Match 'C:\\Windows'

        $expert = Invoke-SafetyPolicyProfileApply -Profile 'Expert'
        $expert.Name | Should -Be 'Expert'
        $expert.Strict | Should -BeFalse
    }

    It 'Invoke-SafetySandboxRun should report blockers for missing roots and protected paths' {
        Mock -CommandName Find-ConversionTool -MockWith { $null }

        $sandbox = Invoke-SafetySandboxRun `
            -Roots @('C:\Windows\System32') `
            -TrashRoot '' `
            -AuditRoot '' `
            -StrictSafety $true `
            -ProtectedPathsText 'C:\Windows,C:\Program Files' `
            -UseDat $true `
            -DatRoot 'C:\__missing_dat_root__' `
            -ConvertEnabled $true `
            -ToolOverrides @{} `
            -Extensions @('.chd')

        [string]$sandbox.Status | Should -Be 'Blocked'
        [int]$sandbox.BlockerCount | Should -BeGreaterThan 0
        (@($sandbox.Blockers) -join ' | ') | Should -Match 'Safety-Policy blockiert Pfad|DAT-Ordner nicht gefunden'
        @($sandbox.Recommendations).Count | Should -BeGreaterThan 0
    }

    It 'Export-DuplicateInspectorCsv should write csv file' {
        $tmp = Join-Path $env:TEMP ("dup-export-" + [guid]::NewGuid().ToString('N') + '.csv')
        try {
            $rows = @(
                [pscustomobject]@{
                    GameKey = 'gk'
                    Winner = $true
                    Region = 'EU'
                    Type = 'FILE'
                    SizeMB = 1.2
                    RegionScore = 100
                    HeaderScore = 10
                    VersionScore = 5
                    FormatScore = 3
                    CompletenessScore = 100
                    SizeTieBreakScore = 10
                    MainPath = 'C:\\roms\\game.chd'
                }
            )

            Export-DuplicateInspectorCsv -Rows $rows -Path $tmp
            Test-Path -LiteralPath $tmp | Should -BeTrue
            (Get-Content -LiteralPath $tmp -Raw) | Should -Match 'GameKey'
        } finally {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
    }
}
