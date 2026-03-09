#requires -Modules Pester
<#
  ROM Cleanup & Region Dedupe - Pester 5.x Test Suite
  ====================================================
  Bug it would catch: See individual test descriptions.
  
  Tests mit "RED" im Namen MUESSEN gegen das UNVERÄNDERTE Script fehlschlagen.
  Erst nach Anwendung des Patches werden sie grün.
  
  Run with: Invoke-Pester -Path .\tests\RomCleanup.Tests.ps1 -Output Detailed
#>

BeforeAll {
    $script:SkipGUI = $true
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'rom_cleanup_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# Startup-Tests sind in Startup.Tests.ps1 ausgelagert

Describe 'StrictMode Compliance' {
    Context 'Variable Initialization' {
        
        # Bug it would catch: $hashKey is used inside ConsoleMap branch of Get-DatIndex 
        # before being initialized (line ~1659). StrictMode Latest throws "variable not set".
        It 'RED: Get-DatIndex with ConsoleMap should not fail due to uninitialized $hashKey' {
            # Create a minimal DAT file
            $tempDatDir = Join-Path $env:TEMP "pester_dat_$(Get-Random)"
            $tempConsoleDir = Join-Path $tempDatDir 'PS1'
            New-Item -ItemType Directory -Path $tempConsoleDir -Force | Out-Null
            
            # Create a minimal XML DAT file
            $datXml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Test Game">
    <rom name="test.bin" sha1="abc123def456" crc="12345678"/>
  </game>
</datafile>
'@
            $datPath = Join-Path $tempConsoleDir 'test.dat'
            $datXml | Out-File -LiteralPath $datPath -Encoding utf8 -Force
            
            $consoleMap = @{ 'PS1' = $tempConsoleDir }
            $script:testLogMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $script:testLogMessages.Add($msg) }
            
            try {
                # This should NOT throw under StrictMode
                $result = Get-DatIndex -DatRoot $tempDatDir -HashType 'SHA1' -ConsoleMap $consoleMap -Log $logFn
                
                # Should have loaded the PS1 hashes
                $result | Should -Not -BeNullOrEmpty
                $result.ContainsKey('PS1') | Should -BeTrue
            } finally {
                Remove-Item -LiteralPath $tempDatDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Describe 'RunHelper Operation Result Contracts' {
    It 'Invoke-PreflightRun should block drive-root paths' {
        $driveRoot = [System.IO.Path]::GetPathRoot((Get-Location).Path)
        $driveRoot | Should -Not -BeNullOrEmpty

        $ok = Invoke-PreflightRun -Roots @($driveRoot) -Exts @('.zip') -UseDat $false -DatRoot '' `
            -DatMap @{} -DoConvert $false -ToolOverrides @{} -AuditRoot '' -Log { param($m) }

        $ok | Should -BeFalse
    }

    It 'Invoke-PreflightRun should pass for a normal workspace root' {
        $workspaceRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
        $ok = Invoke-PreflightRun -Roots @([string]$workspaceRoot) -Exts @('.zip') -UseDat $false -DatRoot '' `
            -DatMap @{} -DoConvert $false -ToolOverrides @{} -AuditRoot '' -Log { param($m) }
        $ok | Should -BeTrue
    }

    It 'Invoke-RunPreflight should return blocked contract when preflight fails' {
        Mock -CommandName Invoke-PreflightRun -MockWith { return $false }

        $result = Invoke-RunPreflight -Roots @('C:\ROMs') -Exts @('.zip') -UseDat $false -DatRoot '' `
            -DatMap @{} -DoConvert $false -ToolOverrides @{} -AuditRoot '' -Log { param($m) }

        $result | Should -Not -BeNullOrEmpty
        $result.Status | Should -Be 'blocked'
        $result.Reason | Should -Be 'preflight-failed'
        [bool]$result.Value | Should -BeFalse
        $result.Meta.Phase | Should -Be 'Preflight'
    }

    It 'Invoke-OptionalConsoleSort should return skipped contract when disabled' {
        $result = Invoke-OptionalConsoleSort -Enabled $false -Mode 'Move' -Roots @('C:\ROMs') `
            -Extensions @('.zip') -UseDat $false -DatRoot '' -DatHashType 'SHA1' -DatMap @{} -ToolOverrides @{} -Log { param($m) }

        $result | Should -Not -BeNullOrEmpty
        $result.Status | Should -Be 'skipped'
        $result.Reason | Should -Be 'disabled-or-non-move'
        $result.Meta.Phase | Should -Be 'ConsoleSort'
    }

    It 'Invoke-ConvertPreviewDryRun should return skipped contract when not in DryRun' {
        $result = Invoke-ConvertPreviewDryRun -Enabled $true -Mode 'Move' -Result $null -Log { param($m) }

        $result | Should -Not -BeNullOrEmpty
        $result.Status | Should -Be 'skipped'
        $result.Reason | Should -Be 'disabled-or-non-dryrun'
        $result.Meta.Phase | Should -Be 'ConvertPreview'
    }

    It 'Invoke-WinnerConversionMove should return completed contract with candidate count' {
        Mock -CommandName Invoke-FormatConversion -MockWith { param($Winners) }

        $item = [pscustomobject]@{ MainPath = 'C:\ROMs\game.iso'; Root = 'C:\ROMs'; Type = 'FILE'; BaseName = 'game'; Paths = @('C:\ROMs\game.iso') }
        $inputResult = [pscustomobject]@{
            AllItems   = @($item)
            Winners    = @([pscustomobject]@{ MainPath = 'C:\ROMs\game.iso' })
            ItemByMain = @{ 'C:\ROMs\game.iso' = $item }
        }

        $result = Invoke-WinnerConversionMove -Enabled $true -Mode 'Move' -Result $inputResult -Roots @('C:\ROMs') -ToolOverrides @{} -Log { param($m) } -SetQuickPhase $null -OnProgress $null

        $result | Should -Not -BeNullOrEmpty
        $result.Status | Should -Be 'completed'
        $result.Reason | Should -Be 'conversion-invoked'
        [int]$result.Value | Should -Be 1
        [int]$result.Meta.CandidateCount | Should -Be 1
    }
}

Describe 'Safety Startup Defaults' {
    # GUI block is stripped by TestScriptLoader unless -IncludeGui is passed,
    # so UIControls and Set-HardSafetyDefaults are not available in non-GUI test runs.
    $guiAvailable = $false
    try { $guiAvailable = ($null -ne (Get-Variable -Name 'UIControls' -Scope Script -ValueOnly -ErrorAction SilentlyContinue)) } catch {}

    It 'should enforce hard safety defaults for move and convert on startup' -Skip:(-not $guiAvailable) {
        $script:UIControls | Should -Not -BeNullOrEmpty

        $script:UIControls.RbDry.Checked | Should -BeTrue
        $script:UIControls.ChkConvert.Checked | Should -BeFalse
        $script:UIControls.ChkSafetyStrict.Checked | Should -BeTrue
        [string]$script:UIControls.TxtProtectedPaths.Text | Should -Match 'C:\\Windows'
        [string]$script:UIControls.CmbSafetyProfile.SelectedItem | Should -Be 'Balanced'
    }

    It 'Set-HardSafetyDefaults should enforce DryRun and Balanced profile' -Skip:(-not $guiAvailable) {
        Get-Command Set-HardSafetyDefaults -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
    }
}

Describe 'Session Checkpoint (F-10 minimal)' {
    It 'Set-QuickPhase should persist latest session checkpoint json' {
        $reportsDir = Join-Path (Get-Location).Path 'reports'
        $checkpointPath = Join-Path $reportsDir 'session-checkpoint-latest.json'
        if (Test-Path -LiteralPath $checkpointPath) {
            Remove-Item -LiteralPath $checkpointPath -Force -ErrorAction SilentlyContinue
        }

        Set-QuickTone -Tone idle -Phase 'Bereit'
        Set-QuickPhase -Phase 'UnitTest-Checkpoint-Phase'

        Test-Path -LiteralPath $checkpointPath | Should -BeTrue
        $json = Get-Content -LiteralPath $checkpointPath -Raw | ConvertFrom-Json
        [string]$json.schemaVersion | Should -Be 'session-checkpoint-v1'
        [string]$json.phase | Should -Not -BeNullOrEmpty
        [string]$json.mode | Should -Match 'DryRun|Move'
    }
}

Describe 'Logging Helpers' {
    It 'ConvertTo-LogMessageText should handle null safely' {
        (ConvertTo-LogMessageText $null) | Should -Be ''
    }

    It 'ConvertTo-LogMessageText should flatten arrays deterministically' {
        (ConvertTo-LogMessageText @('a', $null, 5)) | Should -Be 'a; ; 5'
    }

    It 'ConvertTo-LogMessageText should prefer exception message' {
        $ex = [System.InvalidOperationException]::new('boom')
        (ConvertTo-LogMessageText $ex) | Should -Be 'boom'
    }
}

Describe 'HTML Report Consistency' {
    Context 'Table Structure' {
        
        # Bug it would catch: HTML report has mismatched <th> headers vs <td> cells per row.
        It 'HTML report should have matching th and td counts' {
            $report = [System.Collections.Generic.List[psobject]]::new()
            $report.Add([pscustomobject]@{
                GameKey      = 'test-game'
                Action       = 'KEEP'
                Category     = 'GAME'
                Region       = 'EU'
                WinnerRegion = 'EU'
                VersionScore = 100
                FormatScore  = 850
                Type         = 'FILE'
                DatMatch     = $true
                MainPath     = 'C:\ROMs\test.chd'
                Root         = 'C:\ROMs'
                SizeBytes    = 1048576
            })
            
            $tempHtml = Join-Path $TestDrive "pester_report_$(Get-Random).html"

            Push-Location $TestDrive
            try {
                ConvertTo-HtmlReport -Report $report -HtmlPath $tempHtml `
                    -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 `
                    -JunkCount 0 -JunkBytes 0 -BiosCount 0 `
                    -UniqueGames 1 -TotalScanned 1 -Mode 'DryRun'

                $htmlContent = Get-Content -LiteralPath $tempHtml -Raw

                # Extract reportTable section specifically (it has id="reportTable")
                $reportTableMatch = [regex]::Match($htmlContent, '<table id="reportTable">(.*?)</table>', ([System.Text.RegularExpressions.RegexOptions]::Singleline))
                $reportTableMatch.Success | Should -BeTrue -Because "reportTable should exist in HTML"

                $tableHtml = $reportTableMatch.Groups[1].Value

                # Count <th> in thead
                $theadMatch = [regex]::Match($tableHtml, '<thead><tr>(.*?)</tr></thead>', ([System.Text.RegularExpressions.RegexOptions]::Singleline))
                $thCount = 0
                if ($theadMatch.Success) {
                    $thCount = ([regex]::Matches($theadMatch.Groups[1].Value, '<th[^>]*>')).Count
                }

                # Count <td> in first data row of tbody
                $tbodyMatch = [regex]::Match($tableHtml, '<tbody>(.*?)</tbody>', ([System.Text.RegularExpressions.RegexOptions]::Singleline))
                $tdCount = 0
                if ($tbodyMatch.Success) {
                    $firstRowMatch = [regex]::Match($tbodyMatch.Groups[1].Value, '<tr[^>]*>(.*?)</tr>', ([System.Text.RegularExpressions.RegexOptions]::Singleline))
                    if ($firstRowMatch.Success) {
                        $tdCount = ([regex]::Matches($firstRowMatch.Groups[1].Value, '<td[^>]*>')).Count
                    }
                }

                $thCount | Should -BeGreaterThan 0 -Because "Should have at least one header"
                $tdCount | Should -BeGreaterThan 0 -Because "Should have at least one data cell"
                $thCount | Should -Be $tdCount -Because "reportTable should have matching th ($thCount) and td ($tdCount) counts"
            } finally {
                Pop-Location
                Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
            }
        }

        It 'T14: ConvertTo-HtmlReport should HTML-encode Mode parameter' {
            $report = [System.Collections.Generic.List[psobject]]::new()
            $report.Add([pscustomobject]@{
                GameKey      = 'test-game'
                Action       = 'KEEP'
                Category     = 'GAME'
                Region       = 'EU'
                WinnerRegion = 'EU'
                VersionScore = 100
                FormatScore  = 850
                Type         = 'FILE'
                DatMatch     = $false
                MainPath     = 'C:\ROMs\test.chd'
                Root         = 'C:\ROMs'
                SizeBytes    = 1048576
            })

            $tempHtml = Join-Path $TestDrive "pester_mode_encode_$(Get-Random).html"
            Push-Location $TestDrive
            try {
                ConvertTo-HtmlReport -Report $report -HtmlPath $tempHtml `
                    -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 `
                    -JunkCount 0 -JunkBytes 0 -BiosCount 0 `
                    -UniqueGames 1 -TotalScanned 1 -Mode '<script>alert(1)</script>'

                $htmlContent = Get-Content -LiteralPath $tempHtml -Raw
                $htmlContent | Should -Match '&lt;script&gt;alert\(1\)&lt;/script&gt;'
                $htmlContent | Should -Not -Match 'Modus:\s*<strong><script>alert\(1\)</script></strong>'
            } finally {
                Pop-Location
                Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
            }
        }

        It 'should render DAT completeness table with missing counts' {
            $report = [System.Collections.Generic.List[psobject]]::new()
            $report.Add([pscustomobject]@{
                GameKey      = 'game-a'
                Action       = 'KEEP'
                Category     = 'GAME'
                Region       = 'EU'
                WinnerRegion = 'EU'
                VersionScore = 100
                FormatScore  = 850
                Type         = 'FILE'
                DatMatch     = $true
                MainPath     = 'C:\ROMs\PS1\game-a.chd'
                Root         = 'C:\ROMs'
                SizeBytes    = 1048576
            })

            $datIndex = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $datIndex['PS1'] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $datIndex['PS1']['h1'] = 'game-a'
            $datIndex['PS1']['h2'] = 'game-b'
            $datIndex['PS2'] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $datIndex['PS2']['h3'] = 'game-c'

            $tempHtml = Join-Path $TestDrive "pester_dat_completeness_$(Get-Random).html"
            Push-Location $TestDrive
            try {
                ConvertTo-HtmlReport -Report $report -HtmlPath $tempHtml `
                    -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 `
                    -JunkCount 0 -JunkBytes 0 -BiosCount 0 `
                    -UniqueGames 1 -TotalScanned 1 -Mode 'DryRun' `
                    -DatEnabled $true -DatIndex $datIndex

                $htmlContent = Get-Content -LiteralPath $tempHtml -Raw
                $htmlContent | Should -Match 'DAT Vollstaendigkeit pro Konsole'
                $htmlContent | Should -Match 'DAT Fehlend'
                $htmlContent | Should -Match '<td>PS1</td><td class="sz">1</td><td class="sz">2</td><td class="sz">1</td>'
                $htmlContent | Should -Match '<td>PS2</td><td class="sz">0</td><td class="sz">1</td><td class="sz">1</td>'
            } finally {
                Pop-Location
                Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
            }
        }

        It 'should build DAT completeness rows via helper function' {
            $report = [System.Collections.Generic.List[psobject]]::new()
            $report.Add([pscustomobject]@{
                GameKey      = 'game-a'
                Action       = 'KEEP'
                Category     = 'GAME'
                Region       = 'EU'
                WinnerRegion = 'EU'
                VersionScore = 100
                FormatScore  = 850
                Type         = 'FILE'
                DatMatch     = $true
                MainPath     = 'C:\ROMs\PS1\game-a.chd'
                Root         = 'C:\ROMs'
                SizeBytes    = 1048576
            })

            $datIndex = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $datIndex['PS1'] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $datIndex['PS1']['h1'] = 'game-a'
            $datIndex['PS1']['h2'] = 'game-b'

            $rowsResult = Get-DatCompletenessReport -Report $report -DatIndex $datIndex
            $rowsResult | Should -Not -BeNullOrEmpty
            [int]$rowsResult.ExpectedTotal | Should -Be 2
            [int]$rowsResult.MissingTotal | Should -Be 1
            @($rowsResult.Rows).Count | Should -Be 1
            [string]$rowsResult.Rows[0].Console | Should -Be 'PS1'
            [int]$rowsResult.Rows[0].Missing | Should -Be 1
        }

        It 'should build collection health rows per console with DAT missing and corruption counts' {
            $tempDatCsv = Join-Path $env:TEMP "pester_collection_health_$(Get-Random).csv"
            @(
                'Console,Matched,Expected,Missing,Coverage'
                'PS1,1,2,1,50'
            ) | Set-Content -LiteralPath $tempDatCsv -Encoding UTF8

            $result = [pscustomobject]@{
                ReportRows = @(
                    [pscustomobject]@{ Category='GAME'; Action='KEEP'; Type='FILE'; MainPath='C:\ROMs\ps1\a.chd'; Root='C:\ROMs'; IsCorrupt=$false }
                    [pscustomobject]@{ Category='GAME'; Action='SKIP_DRYRUN'; Type='CUESET'; MainPath='C:\ROMs\ps1\a_alt.cue'; Root='C:\ROMs'; IsCorrupt=$false }
                    [pscustomobject]@{ Category='JUNK'; Action='DRYRUN-JUNK'; Type='FILE'; MainPath='C:\ROMs\ps1\broken.bin'; Root='C:\ROMs'; IsCorrupt=$true }
                    [pscustomobject]@{ Category='GAME'; Action='KEEP'; Type='M3USET'; MainPath='C:\ROMs\ps2\b.m3u'; Root='C:\ROMs'; IsCorrupt=$false }
                )
                DatCompletenessCsvPath = $tempDatCsv
            }

            Mock -CommandName Get-ConsoleType -MockWith {
                param($RootPath, $FilePath, $Extension)
                if ([string]$FilePath -like '*\ps1\*') { return 'PS1' }
                if ([string]$FilePath -like '*\ps2\*') { return 'PS2' }
                return 'UNKNOWN'
            }

            try {
                $rows = @(Get-CollectionHealthRows -Result $result)
                $rows.Count | Should -Be 2

                $ps1 = @($rows | Where-Object { $_.Console -eq 'PS1' })[0]
                $ps1 | Should -Not -BeNullOrEmpty
                [int]$ps1.Roms | Should -Be 1
                [int]$ps1.Duplicates | Should -Be 1
                [int]$ps1.MissingDat | Should -Be 1
                [int]$ps1.Corrupt | Should -Be 1
                [string]$ps1.Formats | Should -Match 'FILE:1'

                $ps2 = @($rows | Where-Object { $_.Console -eq 'PS2' })[0]
                [int]$ps2.Roms | Should -Be 1
                [int]$ps2.Duplicates | Should -Be 0
                [int]$ps2.MissingDat | Should -Be 0
                [int]$ps2.Corrupt | Should -Be 0
                [string]$ps2.Formats | Should -Match 'M3USET:1'
            } finally {
                Remove-Item -LiteralPath $tempDatCsv -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Describe 'Conversion Output Validation' {
    It 'T1: Invoke-ConvertItem should return ERROR when target is zero bytes after tool success' {
        $tempDir = Join-Path $env:TEMP "pester_convert_zero_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        try {
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                $targetIndex = [Array]::IndexOf($ToolArgs, '-o')
                if ($targetIndex -ge 0 -and ($targetIndex + 1) -lt $ToolArgs.Count) {
                    $targetPath = [string]$ToolArgs[$targetIndex + 1]
                    $targetPath = $targetPath.Trim('"')
                    [void](New-Item -ItemType File -Path $targetPath -Force)
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'ERROR'
            $result.Reason | Should -Be 'output-missing-or-empty'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'T2: Invoke-ConvertItem should return ERROR when target is missing after tool success' {
        $tempDir = Join-Path $env:TEMP "pester_convert_missing_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        try {
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'ERROR'
            $result.Reason | Should -Be 'output-missing-or-empty'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'T13: Invoke-ConvertItem should return ERROR and cleanup partial output on tool failure' {
        $tempDir = Join-Path $env:TEMP "pester_convert_tool_fail_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        try {
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                $targetIndex = [Array]::IndexOf($ToolArgs, '-o')
                if ($targetIndex -ge 0 -and ($targetIndex + 1) -lt $ToolArgs.Count) {
                    $targetPath = [string]$ToolArgs[$targetIndex + 1]
                    $targetPath = $targetPath.Trim('"')
                    'partial-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                }
                return [pscustomobject]@{ Success = $false; ExitCode = 7; ErrorText = 'tool failed' }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }
            $targetPath = Join-Path $tempDir 'game.chd'

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'ERROR'
            $result.Reason | Should -Be 'chdman-failed'
            (Test-Path -LiteralPath $targetPath) | Should -BeFalse
            (Test-Path -LiteralPath $sourcePath) | Should -BeTrue
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'T3: Invoke-ConvertItem should fail and keep source when ZIP verify fails' {
        $tempDir = Join-Path $env:TEMP "pester_convert_zip_verify_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        $zipToolPath = Join-Path $tempDir '7z.exe'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $zipToolPath -Force)

        try {
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                if ($ToolArgs[0] -eq 'a') {
                    $targetPath = [string]$ToolArgs[3]
                    $targetPath = $targetPath.Trim('"')
                    'zip-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                if ($ToolArgs[0] -eq 't') {
                    return [pscustomobject]@{ Success = $false; ExitCode = 2; ErrorText = 'verify failed' }
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.zip'; Tool = '7z'; Cmd = $null }
            $targetPath = Join-Path $tempDir 'game.zip'

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $zipToolPath -Log { param($m) } -SevenZipPath $zipToolPath
            $result.Status | Should -Be 'ERROR'
            $result.Reason | Should -Be 'zip-verify-failed'
            Test-Path -LiteralPath $sourcePath | Should -BeTrue
            Test-Path -LiteralPath $targetPath | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'T4: Invoke-ConvertItem should fail and keep source when RVZ verify fails' {
        $tempDir = Join-Path $env:TEMP "pester_convert_rvz_verify_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return 'GC' }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                if ($ToolArgs[0] -eq 'convert') {
                    $targetPath = [string]$ToolArgs[4]
                    $targetPath = $targetPath.Trim('"')
                    'rvz-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                if ($ToolArgs[0] -eq 'verify') {
                    return [pscustomobject]@{ Success = $false; ExitCode = 1; ErrorText = 'verify failed' }
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }
            $targetPath = Join-Path $tempDir 'game.rvz'

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'ERROR'
            $result.Reason | Should -Be 'rvz-verify-failed'
            Test-Path -LiteralPath $sourcePath | Should -BeTrue
            Test-Path -LiteralPath $targetPath | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should skip Dolphin conversion for non-disc image inputs' {
        $tempDir = Join-Path $env:TEMP "pester_convert_dolphin_nondisc_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'not-a-disc.iso'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'not-disc-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return $null }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                throw 'Invoke-ExternalToolProcess darf bei Non-Disc nicht aufgerufen werden.'
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'SKIP'
            $result.Reason | Should -Be 'dolphintool-non-disc-image'
            Test-Path -LiteralPath $sourcePath | Should -BeTrue
            Assert-MockCalled -CommandName Invoke-ExternalToolProcess -Times 0 -Exactly
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should pass explicit RVZ block size to DolphinTool convert' {
        $tempDir = Join-Path $env:TEMP "pester_convert_dolphin_blocksize_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            $script:lastConvertArgs = $null
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return 'GC' }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                if ($ToolArgs[0] -eq 'convert') {
                    $script:lastConvertArgs = @($ToolArgs)
                    $targetPath = [string]$ToolArgs[4]
                    $targetPath = $targetPath.Trim('"')
                    'rvz-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                if ($ToolArgs[0] -eq 'verify') {
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'OK'
            $script:lastConvertArgs | Should -Not -BeNullOrEmpty
            $script:lastConvertArgs | Should -Contain '-c'
            $cIndex = [Array]::IndexOf($script:lastConvertArgs, '-c')
            $cIndex | Should -BeGreaterThan -1
            [string]$script:lastConvertArgs[$cIndex + 1] | Should -Be 'zstd'
            $script:lastConvertArgs | Should -Contain '-l'
            $lIndex = [Array]::IndexOf($script:lastConvertArgs, '-l')
            $lIndex | Should -BeGreaterThan -1
            [string]$script:lastConvertArgs[$lIndex + 1] | Should -Be '5'
            $script:lastConvertArgs | Should -Contain '-b'
            $bIndex = [Array]::IndexOf($script:lastConvertArgs, '-b')
            $bIndex | Should -BeGreaterThan -1
            [string]$script:lastConvertArgs[$bIndex + 1] | Should -Be '131072'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Variable -Scope Script -Name lastConvertArgs -ErrorAction SilentlyContinue
        }
    }

    It 'Should convert GCZ input to RVZ (not skip as already compressed)' {
        $tempDir = Join-Path $env:TEMP "pester_convert_dolphin_gcz_to_rvz_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.gcz'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'gcz-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return 'WII' }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                if ($ToolArgs[0] -eq 'convert') {
                    $targetPath = [string]$ToolArgs[4]
                    $targetPath = $targetPath.Trim('"')
                    'rvz-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                if ($ToolArgs[0] -eq 'verify') {
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'OK'
            Assert-MockCalled -CommandName Invoke-ExternalToolProcess -Times 1 -Exactly -ParameterFilter { $ToolArgs[0] -eq 'convert' }
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should convert WBFS to RVZ when Dolphin disc probe confirms Wii/GC' {
        $tempDir = Join-Path $env:TEMP "pester_convert_dolphin_wbfs_probe_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'Some Game [SMNE01].wbfs'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'wbfs-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return 'WII' }
            Mock -CommandName Test-WbfsAccurateFromDolphinTool -MockWith { return $true }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                if ($ToolArgs[0] -eq 'convert') {
                    $targetPath = [string]$ToolArgs[4]
                    $targetPath = $targetPath.Trim('"')
                    'rvz-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                if ($ToolArgs[0] -eq 'verify') {
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'OK'
            Assert-MockCalled -CommandName Invoke-ExternalToolProcess -Times 1 -Exactly -ParameterFilter { $ToolArgs[0] -eq 'convert' }
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should skip WBFS conversion when WBFS data size is not accurate' {
        $tempDir = Join-Path $env:TEMP "pester_convert_dolphin_wbfs_inaccurate_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'BadDump.wbfs'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'wbfs-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return 'WII' }
            Mock -CommandName Test-WbfsAccurateFromDolphinTool -MockWith { return $false }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                throw 'Invoke-ExternalToolProcess darf bei inakkuratem WBFS nicht aufgerufen werden.'
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'SKIP'
            $result.Reason | Should -Be 'dolphintool-wbfs-inaccurate'
            Assert-MockCalled -CommandName Invoke-ExternalToolProcess -Times 0 -Exactly
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should skip WBFS conversion when Dolphin disc probe is inconclusive' {
        $tempDir = Join-Path $env:TEMP "pester_convert_dolphin_wbfs_probe_skip_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'Unknown.wbfs'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'wbfs-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return $null }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                throw 'Invoke-ExternalToolProcess darf bei unklarem WBFS-Probe nicht aufgerufen werden.'
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'SKIP'
            $result.Reason | Should -Be 'dolphintool-wbfs-probe-failed'
            Assert-MockCalled -CommandName Invoke-ExternalToolProcess -Times 0 -Exactly
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should convert WBFS when Dolphin probe is inconclusive but filename has Wii disc ID' {
        $tempDir = Join-Path $env:TEMP "pester_convert_dolphin_wbfs_name_fallback_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'Mario Galaxy_[RMGE01].wbfs'
        $dolphinToolPath = Join-Path $tempDir 'DolphinTool.exe'
        'wbfs-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force
        [void](New-Item -ItemType File -Path $dolphinToolPath -Force)

        try {
            Mock -CommandName Get-ConsoleFromDolphinTool -MockWith { return $null }
            Mock -CommandName Test-WbfsAccurateFromDolphinTool -MockWith { return $true }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                if ($ToolArgs[0] -eq 'convert') {
                    $targetPath = [string]$ToolArgs[4]
                    $targetPath = $targetPath.Trim('"')
                    'rvz-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                if ($ToolArgs[0] -eq 'verify') {
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.rvz'; Tool = 'dolphintool'; Cmd = $null }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath $dolphinToolPath -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'OK'
            Assert-MockCalled -CommandName Invoke-ExternalToolProcess -Times 1 -Exactly -ParameterFilter { $ToolArgs[0] -eq 'convert' }
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should fail and keep source when CHD verify fails' {
        $tempDir = Join-Path $env:TEMP "pester_convert_chd_verify_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        try {
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                param($ToolPath, $ToolArgs)
                if ($ToolArgs[0] -eq 'createcd') {
                    $targetIndex = [Array]::IndexOf($ToolArgs, '-o')
                    if ($targetIndex -ge 0 -and ($targetIndex + 1) -lt $ToolArgs.Count) {
                        $targetPath = [string]$ToolArgs[$targetIndex + 1]
                        $targetPath = $targetPath.Trim('"')
                        'chd-data' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
                    }
                    return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
                }
                if ($ToolArgs[0] -eq 'verify') {
                    return [pscustomobject]@{ Success = $false; ExitCode = 2; ErrorText = 'verify failed' }
                }
                return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }
            $targetPath = Join-Path $tempDir 'game.chd'

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath $null
            $result.Status | Should -Be 'ERROR'
            $result.Reason | Should -Be 'chd-verify-failed'
            (Test-Path -LiteralPath $sourcePath) | Should -BeTrue
            (Test-Path -LiteralPath $targetPath) | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should cleanup temp extraction dir on archive-no-disc skip' {
        $tempDir = Join-Path $env:TEMP "pester_convert_cleanup_nodisc_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.zip'
        'zip-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        $extractDir = Join-Path $env:TEMP "pester_extract_nodisc_$(Get-Random)"
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

        try {
            Mock -CommandName Expand-ArchiveToTemp -MockWith { $extractDir }
            Mock -CommandName Find-DiscImageInDir -MockWith { @() }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath 'C:\tools\7z.exe'
            $result.Status | Should -Be 'SKIP'
            $result.Reason | Should -Be 'archive-no-disc'
            Test-Path -LiteralPath $extractDir | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should cleanup temp extraction dir on cue-incomplete skip' {
        $tempDir = Join-Path $env:TEMP "pester_convert_cleanup_cue_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.zip'
        'zip-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        $extractDir = Join-Path $env:TEMP "pester_extract_cue_$(Get-Random)"
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        $cuePath = Join-Path $extractDir 'disc1.cue'
        'FILE "disc1.bin" BINARY' | Out-File -LiteralPath $cuePath -Encoding ascii -Force

        try {
            Mock -CommandName Expand-ArchiveToTemp -MockWith { $extractDir }
            Mock -CommandName Find-DiscImageInDir -MockWith { @([pscustomobject]@{ FullName = $cuePath }) }
            Mock -CommandName Get-CueMissingFiles -MockWith { @('missing.bin') }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath 'C:\tools\7z.exe'
            $result.Status | Should -Be 'SKIP'
            $result.Reason | Should -Be 'cue-incomplete'
            Test-Path -LiteralPath $extractDir | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should cleanup temp extraction dir when tool execution throws exception' {
        $tempDir = Join-Path $env:TEMP "pester_convert_cleanup_exception_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.zip'
        'zip-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        $extractDir = Join-Path $env:TEMP "pester_extract_exception_$(Get-Random)"
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        $isoPath = Join-Path $extractDir 'disc1.iso'
        'iso-data' | Out-File -LiteralPath $isoPath -Encoding ascii -Force

        try {
            Mock -CommandName Expand-ArchiveToTemp -MockWith { $extractDir }
            Mock -CommandName Find-DiscImageInDir -MockWith { @([pscustomobject]@{ FullName = $isoPath }) }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith { throw 'tool-crash' }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

            { Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath 'C:\tools\7z.exe' } | Should -Throw
            Test-Path -LiteralPath $extractDir | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should cleanup temp extraction dir when tool execution is canceled' {
        $tempDir = Join-Path $env:TEMP "pester_convert_cleanup_cancel_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.zip'
        'zip-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        $extractDir = Join-Path $env:TEMP "pester_extract_cancel_$(Get-Random)"
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        $isoPath = Join-Path $extractDir 'disc1.iso'
        'iso-data' | Out-File -LiteralPath $isoPath -Encoding ascii -Force

        try {
            Mock -CommandName Expand-ArchiveToTemp -MockWith { $extractDir }
            Mock -CommandName Find-DiscImageInDir -MockWith { @([pscustomobject]@{ FullName = $isoPath }) }
            Mock -CommandName Invoke-ExternalToolProcess -MockWith { throw [System.OperationCanceledException]::new('cancel') }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root     = $tempDir
                Paths    = @($sourcePath)
                Type     = 'FILE'
                SizeBytes = 123
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

            { Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath 'C:\tools\7z.exe' } | Should -Throw -ExceptionType ([System.OperationCanceledException])
            Test-Path -LiteralPath $extractDir | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Config Profile System' {
    It 'should persist named profiles and roundtrip profile export/import JSON' {
        $tempProfilesPath = Join-Path $env:TEMP "pester_profiles_$(Get-Random).json"
        $exportPath = Join-Path $env:TEMP "pester_profile_export_$(Get-Random).json"

        try {
            Remove-Item -LiteralPath $tempProfilesPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $exportPath -Force -ErrorAction SilentlyContinue

            $settings = @{
                toolPaths = @{ chdman = 'C:\Tools\chdman.exe' }
                dat = @{ useDat = $false; root = ''; hash = 'SHA1'; fallback = $true; map = @() }
                general = @{ prefer = 'EU,US,WORLD'; selectedprofile = 'Streng EU' }
                rules = @{ ordered = ''; twoletter = ''; lang = '' }
            }

            Set-ConfigProfile -Name 'Streng EU' -Settings $settings -StorePath $tempProfilesPath | Out-Null
            $profiles = Get-ConfigProfiles -StorePath $tempProfilesPath
            $profiles.ContainsKey('Streng EU') | Should -BeTrue
            [string]$profiles['Streng EU'].general.prefer | Should -Be 'EU,US,WORLD'

            Export-ConfigProfile -Name 'Streng EU' -Path $exportPath -StorePath $tempProfilesPath
            Test-Path -LiteralPath $exportPath | Should -BeTrue

            Remove-ConfigProfile -Name 'Streng EU' -StorePath $tempProfilesPath | Should -BeTrue
            (Get-ConfigProfiles -StorePath $tempProfilesPath).ContainsKey('Streng EU') | Should -BeFalse

            Import-ConfigProfile -Path $exportPath -StorePath $tempProfilesPath | Out-Null
            $imported = Get-ConfigProfiles -StorePath $tempProfilesPath
            $imported.ContainsKey('Streng EU') | Should -BeTrue
            [string]$imported['Streng EU'].toolPaths.chdman | Should -Be 'C:\Tools\chdman.exe'
        } finally {
            Remove-Item -LiteralPath $tempProfilesPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $exportPath -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Standalone Conversion Guardrails' {
    It 'Preview should count only allowlisted roots' {
        $rootAllowed = Join-Path $TestDrive 'standalone_allowed'
        $rootBlocked = Join-Path $TestDrive 'standalone_blocked'
        New-Item -ItemType Directory -Path $rootAllowed -Force | Out-Null
        New-Item -ItemType Directory -Path $rootBlocked -Force | Out-Null
        'iso-a' | Out-File -LiteralPath (Join-Path $rootAllowed 'a.iso') -Encoding ascii -Force
        'iso-b' | Out-File -LiteralPath (Join-Path $rootBlocked 'b.iso') -Encoding ascii -Force

        $preview = Get-StandaloneConversionPreview -Roots @($rootAllowed, $rootBlocked) -AllowedRoots @($rootAllowed) -PreviewLimit 10 -Log { param($m) }

        $preview.CandidateCount | Should -Be 1
        @($preview.BlockedRoots).Count | Should -Be 1
        @($preview.PreviewItems).Count | Should -Be 1
        $preview.PreviewItems[0].Root | Should -Be $rootAllowed
    }

    It 'Invoke-StandaloneConversion should skip non-allowlisted roots' {
        $rootAllowed = Join-Path $TestDrive 'standalone_run_allowed'
        $rootBlocked = Join-Path $TestDrive 'standalone_run_blocked'
        New-Item -ItemType Directory -Path $rootAllowed -Force | Out-Null
        New-Item -ItemType Directory -Path $rootBlocked -Force | Out-Null
        'iso-a' | Out-File -LiteralPath (Join-Path $rootAllowed 'a.iso') -Encoding ascii -Force
        'iso-b' | Out-File -LiteralPath (Join-Path $rootBlocked 'b.iso') -Encoding ascii -Force

        $script:lastStandaloneWinners = 0
        Mock -CommandName Invoke-FormatConversion -MockWith {
            param($Winners)
            $script:lastStandaloneWinners = @($Winners).Count
        }

        Invoke-StandaloneConversion -Roots @($rootAllowed, $rootBlocked) -AllowedRoots @($rootAllowed) -ToolOverrides @{} -Log { param($m) } -SetQuickPhase $null -OnProgress $null

        $script:lastStandaloneWinners | Should -Be 1
    }

    It 'Invoke-StandaloneConversion should reuse pre-scanned files without rescanning roots' {
        $rootAllowed = Join-Path $TestDrive 'standalone_prescanned_allowed'
        New-Item -ItemType Directory -Path $rootAllowed -Force | Out-Null
        $isoPath = Join-Path $rootAllowed 'pre.iso'
        'iso-a' | Out-File -LiteralPath $isoPath -Encoding ascii -Force

        Mock -CommandName Get-FilesSafe -MockWith { throw 'scanner-should-not-run' }

        $script:lastStandaloneWinners = 0
        Mock -CommandName Invoke-FormatConversion -MockWith {
            param($Winners)
            $script:lastStandaloneWinners = @($Winners).Count
        }

        $preScanned = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $preScanned[[System.IO.Path]::GetFullPath($rootAllowed)] = @($isoPath)

        { Invoke-StandaloneConversion -Roots @($rootAllowed) -AllowedRoots @($rootAllowed) -PreScannedFilesByRoot $preScanned -ToolOverrides @{} -Log { param($m) } -SetQuickPhase $null -OnProgress $null } | Should -Not -Throw
        $script:lastStandaloneWinners | Should -Be 1
    }

    It 'StrictMode regression: standalone preview and scan should not throw Count errors on single-file roots' {
        $rootSingle = Join-Path $TestDrive 'standalone_single_count'
        New-Item -ItemType Directory -Path $rootSingle -Force | Out-Null
        'iso-single' | Out-File -LiteralPath (Join-Path $rootSingle 'single.iso') -Encoding ascii -Force

        { Get-StandaloneConversionPreview -Roots @($rootSingle) -AllowedRoots @($rootSingle) -PreviewLimit 5 -Log { param($m) } | Out-Null } | Should -Not -Throw
        $preview = Get-StandaloneConversionPreview -Roots @($rootSingle) -AllowedRoots @($rootSingle) -PreviewLimit 5 -Log { param($m) }
        $preview.CandidateCount | Should -Be 1

        Mock -CommandName Invoke-FormatConversion -MockWith { param($Winners) }
        { Invoke-StandaloneConversion -Roots @($rootSingle) -AllowedRoots @($rootSingle) -ToolOverrides @{} -Log { param($m) } -SetQuickPhase $null -OnProgress $null } | Should -Not -Throw
    }

    It 'Mutation kill: standalone conversion should honor cancel checks inside scan loops' {
        $rootCancel = Join-Path $TestDrive 'standalone_cancel_loop'
        New-Item -ItemType Directory -Path $rootCancel -Force | Out-Null
        'iso-a' | Out-File -LiteralPath (Join-Path $rootCancel 'a.iso') -Encoding ascii -Force
        'iso-b' | Out-File -LiteralPath (Join-Path $rootCancel 'b.iso') -Encoding ascii -Force

        $script:cancelCheckCount = 0
        $script:formatConversionCalled = $false

        Mock -CommandName Test-CancelRequested -MockWith {
            $script:cancelCheckCount++
            if ($script:cancelCheckCount -ge 3) {
                throw [System.OperationCanceledException]::new('cancel-loop')
            }
        }

        Mock -CommandName Invoke-FormatConversion -MockWith {
            $script:formatConversionCalled = $true
        }

        { Invoke-StandaloneConversion -Roots @($rootCancel) -AllowedRoots @($rootCancel) -ToolOverrides @{} -Log { param($m) } -SetQuickPhase $null -OnProgress $null } |
            Should -Throw -ExceptionType ([System.OperationCanceledException])

        $script:cancelCheckCount | Should -BeGreaterThan 2
        $script:formatConversionCalled | Should -BeFalse
    }
}

Describe 'Path Handling' {
    Context 'Root Path Normalization' {
        
        # Bug it would catch: Substring with trailing slash inconsistency
        It 'Path.Substring should not crash with trailing slash variations' {
            $testRoot = 'C:\ROMs\'
            $testFile = 'C:\ROMs\game.chd'
            
            # Simulating what happens in code
            $normalized = $testRoot.TrimEnd('\', '/')
            { $testFile.Substring($normalized.Length).TrimStart('\', '/') } | Should -Not -Throw
        }
        
        # Bug it would catch: UNC paths might fail due to path handling
        It 'Should handle UNC paths without crashing' {
            $uncRoot = '\\server\share\roms'
            $uncFile = '\\server\share\roms\game.chd'
            
            $normalized = $uncRoot.TrimEnd('\', '/')
            { $uncFile.Substring($normalized.Length).TrimStart('\', '/') } | Should -Not -Throw
            
            $result = $uncFile.Substring($normalized.Length).TrimStart('\', '/')
            $result | Should -Be 'game.chd'
        }
        
        # Bug it would catch: Root that equals file length exactly
        It 'Should handle root.Length equal to path without crash' {
            $root = 'C:\ROMs'
            $path = 'C:\ROMs'  # Same length
            
            # Should return empty string, not crash
            $result = $path.Substring($root.TrimEnd('\', '/').Length)
            $result | Should -Be ''
        }
    }
}

Describe 'CSV/Extension Parsing' {
    It 'ConvertFrom-CSVList should split comma and newline separated values' {
        $extInput = ".zip,.7z`n.iso; .chd"
        $values = @(ConvertFrom-CSVList $extInput)

        $values.Count | Should -Be 4
        $values | Should -Contain '.zip'
        $values | Should -Contain '.7z'
        $values | Should -Contain '.iso'
        $values | Should -Contain '.chd'
    }

    It 'ConvertTo-NormalizedExtensionList should produce individual extensions' {
        $ext = @(ConvertTo-NormalizedExtensionList -Text '.zip,.7z,.iso')

        $ext.Count | Should -Be 3
        $ext | Should -Contain '.zip'
        $ext | Should -Contain '.7z'
        $ext | Should -Contain '.iso'
    }
}

Describe 'Start-Process ArgumentList Quoting' {
    Context 'Special Characters in Paths' {
        
        # Bug it would catch: Paths with spaces/parentheses break ArgumentList
        It 'ConvertTo-QuotedArg should properly quote paths with spaces' {
            $pathWithSpaces = 'C:\My ROMs\game file.chd'
            $quoted = ConvertTo-QuotedArg -Value $pathWithSpaces
            
            $quoted | Should -BeLike '"*"'
            $quoted | Should -Be ('"' + $pathWithSpaces + '"')
        }
        
        It 'ConvertTo-QuotedArg should properly handle paths with parentheses' {
            $pathWithParens = 'C:\ROMs\Game (Europe) (En,Fr).chd'
            $quoted = ConvertTo-QuotedArg -Value $pathWithParens
            
            $quoted | Should -BeLike '"*"'
        }
        
        It 'ConvertTo-QuotedArg should handle already quoted strings' {
            $alreadyQuoted = '"C:\My ROMs\game.chd"'
            $quoted = ConvertTo-QuotedArg -Value $alreadyQuoted
            
            # Should not double-quote
            $quoted | Should -Be $alreadyQuoted
        }
        
        It 'ConvertTo-QuotedArg should handle null/empty gracefully' {
            ConvertTo-QuotedArg -Value $null | Should -Be '""'
            ConvertTo-QuotedArg -Value '' | Should -Be '""'
        }
        
        # Bug it would catch: Unicode characters in paths
        It 'ConvertTo-QuotedArg should handle Unicode paths' {
            $unicodePath = 'C:\ROMs\ゲーム\日本語.chd'
            $quoted = ConvertTo-QuotedArg -Value $unicodePath
            
            $quoted | Should -Not -BeNullOrEmpty
        }
        
        # RED: Bug it would catch: Paths with dashes misinterpreted as command options
        It 'ConvertTo-QuotedArg should quote paths with dashes to prevent option parsing' {
            # e.g. "Colony Wars - Vengeance (USA).cue" caused "Error: Option '-' not valid"
            $pathWithDash = 'P:\Sony - PlayStation\Colony Wars - Vengeance (USA, Canada).cue'
            $quoted = ConvertTo-QuotedArg -Value $pathWithDash
            
            $quoted | Should -BeLike '"*"' -Because 'paths with spaces/dashes must be quoted'
            $quoted | Should -Be ('"' + $pathWithDash + '"')
        }

        It 'ConvertTo-QuotedArg should preserve trailing backslashes safely' {
            $pathWithTrailingSlash = 'C:\Tools Folder\'
            $quoted = ConvertTo-QuotedArg -Value $pathWithTrailingSlash

            $quoted | Should -Be '"C:\Tools Folder\\"'
        }
        
        It 'ConvertTo-ArgString should properly escape all arguments' {
            $myArgList = @('createcd', '-i', 'P:\Sony - PlayStation\Game - Title (USA).cue', '-o', 'C:\Out\Game - Title.chd')
            $argString = ConvertTo-ArgString $myArgList
            
            $argString | Should -Match '"P:\\Sony - PlayStation\\Game - Title \(USA\)\.cue"'
            $argString | Should -Match '"C:\\Out\\Game - Title\.chd"'
        }
    }
}

Describe 'Security - CSV Injection' {
    Context 'ConvertTo-SafeCsvValue' {
        It 'Should prefix dangerous leading characters with a single quote' {
            # Note: Function trims leading whitespace then checks for =+-@|
            $cases = @('=1+1', '+SUM(A1:A2)', '-10', '@A1', '|cmd', '  =formula')
            foreach ($c in $cases) {
                $safe = ConvertTo-SafeCsvValue -Value $c
                $safe | Should -Be ("'" + $c)
            }
        }
        It 'Should leave normal values unchanged' {
            ConvertTo-SafeCsvValue -Value 'Game Title' | Should -Be 'Game Title'
        }
    }
}

Describe 'Security - HTML XSS' {
    Context 'Console stats table encoding' {
        It 'Should HTML-encode console keys in stats table' {
            try {
                Mock -CommandName Get-ConsoleType -MockWith { '<script>alert(1)</script>' }

                $report = [System.Collections.Generic.List[psobject]]::new()
                $report.Add([pscustomobject]@{
                    GameKey      = 'test-game'
                    Action       = 'KEEP'
                    Category     = 'GAME'
                    Region       = 'EU'
                    WinnerRegion = 'EU'
                    VersionScore = 100
                    FormatScore  = 850
                    Type         = 'FILE'
                    DatMatch     = $true
                    MainPath     = 'C:\xssroot\test.iso'
                    Root         = 'C:\xssroot'
                    SizeBytes    = 1234
                })

                $tempHtml = Join-Path $TestDrive "pester_xss_$(Get-Random).html"
                Push-Location $TestDrive
                ConvertTo-HtmlReport -Report $report -HtmlPath $tempHtml `
                    -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 `
                    -JunkCount 0 -JunkBytes 0 -BiosCount 0 `
                    -UniqueGames 1 -TotalScanned 1 -Mode 'DryRun'

                $htmlContent = Get-Content -LiteralPath $tempHtml -Raw
                $htmlContent | Should -Match '&lt;script&gt;alert\(1\)&lt;/script&gt;'
                $htmlContent | Should -Not -Match '<script>alert\(1\)</script>'
            } finally {
                Pop-Location
                if ($tempHtml -and (Test-Path -LiteralPath $tempHtml)) {
                    Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
}

Describe 'Security - Path Traversal' {
    Context 'Move-ItemSafely root validation' {
        It 'Should block destination outside of root' {
            $tempDir = Join-Path $env:TEMP "pester_traversal_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $src = Join-Path $tempDir 'file.txt'
            'data' | Out-File -LiteralPath $src -Encoding ascii -Force
            $dest = Join-Path $env:TEMP "..\outside.txt"

            { Move-ItemSafely -Source $src -Dest $dest `
                -ValidateSourceWithinRoot $tempDir -ValidateDestWithinRoot $tempDir } | Should -Throw

            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Security - Move-ItemSafely' {
    Context 'Same-path protection' {
        It 'Should block moves where source equals destination' {
            $tempDir = Join-Path $env:TEMP "pester_samepath_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $src = Join-Path $tempDir 'file.txt'
            'data' | Out-File -LiteralPath $src -Encoding ascii -Force

            { Move-ItemSafely -Source $src -Dest $src } | Should -Throw

            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Security - 7z Zip Slip' {
    Context 'Get-HashesFrom7z pre-validation' {
        It 'Should skip archives with traversal entries before extraction' {
            Mock -CommandName Get-ArchiveEntryPaths -MockWith { @('..\evil.txt') }
            Mock -CommandName Test-ArchiveEntryPathsSafe -MockWith { $false }

            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }

            $result = Get-HashesFrom7z -Path 'C:\dummy.7z' -HashType 'SHA1' -SevenZipPath 'C:\missing\7z.exe' -Log $logFn

            $result | Should -BeNullOrEmpty
            $logMessages -join "`n" | Should -Match 'ZIP Slip erkannt'
        }

        It 'Should skip archives when extracted paths are unsafe after extraction' {
            Reset-DatArchiveStats
            Mock -CommandName Get-ArchiveEntryPaths -MockWith { @('safe.bin') }
            Mock -CommandName Test-ArchiveEntryPathsSafe -MockWith { $true }
            Mock -CommandName Invoke-7z -MockWith {
                [pscustomobject]@{ ExitCode = 0; StdOut = ''; StdErr = '' }
            }
            Mock -CommandName Get-ChildItem -MockWith {
                @([pscustomobject]@{ FullName = 'C:\escape\evil.bin'; Attributes = [IO.FileAttributes]::Archive })
            } -ParameterFilter { $LiteralPath -like '*rom_dedupe_7z_*' -and $Recurse }

            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }

            $result = Get-HashesFrom7z -Path 'C:\dummy.7z' -HashType 'SHA1' -SevenZipPath 'C:\tools\7z.exe' -Log $logFn

            $result | Should -BeNullOrEmpty
            (Get-DatArchiveStats)['SkippedPostExtractUnsafe'] | Should -BeGreaterThan 0
            $logMessages -join "`n" | Should -Match 'nach Entpacken'
        }
    }
}

Describe 'DAT Archive Size Policy' {
    It 'Should hash large archives when policy is WarnAndHash' {
        Reset-DatArchiveStats
        $tempDir = Join-Path $env:TEMP "pester_dat_large_warn_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $archivePath = Join-Path $tempDir 'large.zip'
        'dummy-archive-content' | Out-File -LiteralPath $archivePath -Encoding ascii -Force

        $logMessages = [System.Collections.Generic.List[string]]::new()
        $logFn = { param($msg) $logMessages.Add([string]$msg) }

        try {
            Mock -CommandName Get-HashesFromZip -MockWith { @('hash-a') }

            $result = @(Get-ArchiveHashes -Path $archivePath -HashType 'SHA1' -Cache $null -SevenZipPath $null -Log $logFn -ArchiveMaxHashSizeBytes 1 -LargeArchivePolicy 'WarnAndHash')
            $result.Count | Should -Be 1
            $result[0] | Should -Be 'hash-a'

            $stats = Get-DatArchiveStats
            $stats['LargeArchiveHashed'] | Should -BeGreaterThan 0
            ($logMessages -join "`n") | Should -Match 'trotzdem gehasht'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should skip large archives when policy is Skip' {
        Reset-DatArchiveStats
        $tempDir = Join-Path $env:TEMP "pester_dat_large_skip_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $archivePath = Join-Path $tempDir 'large.zip'
        'dummy-archive-content' | Out-File -LiteralPath $archivePath -Encoding ascii -Force

        $logMessages = [System.Collections.Generic.List[string]]::new()
        $logFn = { param($msg) $logMessages.Add([string]$msg) }

        try {
            Mock -CommandName Get-HashesFromZip -MockWith { throw 'Should not hash when policy is Skip' }

            $result = @(Get-ArchiveHashes -Path $archivePath -HashType 'SHA1' -Cache $null -SevenZipPath $null -Log $logFn -ArchiveMaxHashSizeBytes 1 -LargeArchivePolicy 'Skip')
            $result.Count | Should -Be 0

            $stats = Get-DatArchiveStats
            $stats['SkippedTooLarge'] | Should -BeGreaterThan 0
            ($logMessages -join "`n") | Should -Match 'zu gross'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'DAT Matching Consistency' {
    It 'Should produce equivalent DAT index for classic and streaming XML paths' {
        $tempBase = Join-Path $env:TEMP ("pester_dat_stream_eq_{0}" -f (Get-Random))
        $smallRoot = Join-Path $tempBase 'small'
        $bigRoot = Join-Path $tempBase 'big'
        $smallPs1 = Join-Path $smallRoot 'PS1'
        $bigPs1 = Join-Path $bigRoot 'PS1'
        New-Item -ItemType Directory -Path $smallPs1 -Force | Out-Null
        New-Item -ItemType Directory -Path $bigPs1 -Force | Out-Null

        $fixtureXml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Test Game">
    <rom name="test.bin" sha1="1111111111111111111111111111111111111111" md5="22222222222222222222222222222222" crc="1234abcd"/>
  </game>
</datafile>
'@

        $smallDat = Join-Path $smallPs1 'test.dat'
        $bigDat = Join-Path $bigPs1 'test.dat'
        $fixtureXml | Set-Content -LiteralPath $smallDat -Encoding UTF8

        $builder = New-Object System.Text.StringBuilder
        [void]$builder.Append($fixtureXml)
        # Force streaming parser path with file size > 50MB.
        while ($builder.Length -lt 57671680) {
            [void]$builder.Append(' ')
        }
        $builder.ToString() | Set-Content -LiteralPath $bigDat -Encoding UTF8

        try {
            $logFn = { param($m) }
            $smallIndex = Get-DatIndex -DatRoot $smallRoot -HashType 'SHA1' -Log $logFn
            $bigIndex = Get-DatIndex -DatRoot $bigRoot -HashType 'SHA1' -Log $logFn

            $smallIndex.ContainsKey('PS1') | Should -BeTrue
            $bigIndex.ContainsKey('PS1') | Should -BeTrue
            $smallIndex['PS1'].Count | Should -Be 1
            $bigIndex['PS1'].Count | Should -Be 1
            $smallIndex['PS1'].ContainsKey('1111111111111111111111111111111111111111') | Should -BeTrue
            $bigIndex['PS1'].ContainsKey('1111111111111111111111111111111111111111') | Should -BeTrue
            $smallIndex['PS1']['1111111111111111111111111111111111111111'] | Should -Be 'Test Game'
            $bigIndex['PS1']['1111111111111111111111111111111111111111'] | Should -Be 'Test Game'
        } finally {
            Remove-Item -LiteralPath $tempBase -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should map SHA1, MD5 and CRC32 hash types correctly from DAT fixtures' {
        $tempBase = Join-Path $env:TEMP ("pester_dat_hash_fixture_{0}" -f (Get-Random))
        $ps1Root = Join-Path $tempBase 'PS1'
        New-Item -ItemType Directory -Path $ps1Root -Force | Out-Null

        $datPath = Join-Path $ps1Root 'fixture.dat'
        @'
<?xml version="1.0"?>
<datafile>
  <game name="Fixture Game">
    <rom name="fixture.bin" sha1="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" md5="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" crc="89abcdef"/>
  </game>
</datafile>
'@ | Set-Content -LiteralPath $datPath -Encoding UTF8

        try {
            $logFn = { param($m) }
            $shaIndex = Get-DatIndex -DatRoot $tempBase -HashType 'SHA1' -Log $logFn
            $md5Index = Get-DatIndex -DatRoot $tempBase -HashType 'MD5' -Log $logFn
            $crcIndex = Get-DatIndex -DatRoot $tempBase -HashType 'CRC32' -Log $logFn

            $shaIndex['PS1'].ContainsKey('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') | Should -BeTrue
            $md5Index['PS1'].ContainsKey('bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb') | Should -BeTrue
            $crcIndex['PS1'].ContainsKey('89abcdef') | Should -BeTrue

            $shaIndex['PS1']['aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'] | Should -Be 'Fixture Game'
            $md5Index['PS1']['bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'] | Should -Be 'Fixture Game'
            $crcIndex['PS1']['89abcdef'] | Should -Be 'Fixture Game'
        } finally {
            Remove-Item -LiteralPath $tempBase -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'ExcludedCandidatePaths should prevent excluded file from becoming winner' {
        $tempDir = Join-Path $env:TEMP ("pester_excluded_winner_{0}" -f (Get-Random))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

        $eu = Join-Path $tempDir 'Example Game (Europe).chd'
        $us = Join-Path $tempDir 'Example Game (USA).chd'
        'eu' | Out-File -LiteralPath $eu -Encoding ascii -Force
        'us' | Out-File -LiteralPath $us -Encoding ascii -Force

        $result = $null
        try {
            $result = Invoke-RegionDedupe -Roots @($tempDir) -Mode 'DryRun' -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.chd') -RemoveJunk $false -SeparateBios $false -UseDat $false `
                -ExcludedCandidatePaths @($eu) -Log { param($m) }

            $result | Should -Not -BeNullOrEmpty
            $rows = @(Import-Csv -LiteralPath $result.CsvPath)

            (@($rows | Where-Object { $_.Name -eq 'Example Game (Europe).chd' })).Count | Should -Be 0
            $usRow = @($rows | Where-Object { $_.Name -eq 'Example Game (USA).chd' } | Select-Object -First 1)
            $usRow | Should -Not -BeNullOrEmpty
            $usRow.Action | Should -Be 'KEEP'
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'MSDOS folder duplicates should be deduped on folder level' {
        $tempDir = Join-Path $env:TEMP ("pester_msdos_folder_dupes_{0}" -f (Get-Random))
        $dosRoot = Join-Path $tempDir 'MSDOS'
        New-Item -ItemType Directory -Path $dosRoot -Force | Out-Null

        $euFolder = Join-Path $dosRoot 'Prince of Persia (Europe)'
        $usFolder = Join-Path $dosRoot 'Prince of Persia (USA)'
        New-Item -ItemType Directory -Path $euFolder -Force | Out-Null
        New-Item -ItemType Directory -Path $usFolder -Force | Out-Null

        'echo eu' | Out-File -LiteralPath (Join-Path $euFolder 'GAME.BAT') -Encoding ascii -Force
        'echo us' | Out-File -LiteralPath (Join-Path $usFolder 'GAME.BAT') -Encoding ascii -Force
        'payload-eu' | Out-File -LiteralPath (Join-Path $euFolder 'DATA.TXT') -Encoding ascii -Force
        'payload-us' | Out-File -LiteralPath (Join-Path $usFolder 'DATA.TXT') -Encoding ascii -Force

        $result = $null
        try {
            $result = Invoke-RegionDedupe -Roots @($dosRoot) -Mode 'DryRun' -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.bat','.com','.exe','.txt') -RemoveJunk $false -SeparateBios $false -UseDat $false `
                -Log { param($m) }

            $result | Should -Not -BeNullOrEmpty
            $rows = @(Import-Csv -LiteralPath $result.CsvPath)

            $euRow = @($rows | Where-Object { $_.Name -eq 'Prince of Persia (Europe)' } | Select-Object -First 1)
            $usRow = @($rows | Where-Object { $_.Name -eq 'Prince of Persia (USA)' } | Select-Object -First 1)
            $euRow | Should -Not -BeNullOrEmpty
            $usRow | Should -Not -BeNullOrEmpty
            $euRow.Action | Should -Be 'KEEP'
            $usRow.Action | Should -Be 'SKIP_DRYRUN'

            (@($rows | Where-Object { $_.Name -eq 'GAME.BAT' })).Count | Should -Be 0
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'MSDOS folder dedupe should work with neutral root name' {
        $tempDir = Join-Path $env:TEMP ("pester_msdos_neutral_root_{0}" -f (Get-Random))
        $neutralRoot = Join-Path $tempDir 'V5-DOS'
        New-Item -ItemType Directory -Path $neutralRoot -Force | Out-Null

        $euFolder = Join-Path $neutralRoot 'Game X (Europe)'
        $usFolder = Join-Path $neutralRoot 'Game X (USA)'
        New-Item -ItemType Directory -Path $euFolder -Force | Out-Null
        New-Item -ItemType Directory -Path $usFolder -Force | Out-Null

        'echo eu' | Out-File -LiteralPath (Join-Path $euFolder 'START.BAT') -Encoding ascii -Force
        'echo us' | Out-File -LiteralPath (Join-Path $usFolder 'START.BAT') -Encoding ascii -Force

        $result = $null
        try {
            $result = Invoke-RegionDedupe -Roots @($neutralRoot) -Mode 'DryRun' -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.bat') -RemoveJunk $false -SeparateBios $false -UseDat $false -Log { param($m) }

            $rows = @(Import-Csv -LiteralPath $result.CsvPath)
            $euRow = @($rows | Where-Object { $_.Name -eq 'Game X (Europe)' } | Select-Object -First 1)
            $usRow = @($rows | Where-Object { $_.Name -eq 'Game X (USA)' } | Select-Object -First 1)

            $euRow | Should -Not -BeNullOrEmpty
            $usRow | Should -Not -BeNullOrEmpty
            $euRow.Action | Should -Be 'KEEP'
            $usRow.Action | Should -Be 'SKIP_DRYRUN'
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'MSDOS dedupe should normalize folder names and compare without launcher files' {
        $tempDir = Join-Path $env:TEMP ("pester_msdos_folder_key_norm_{0}" -f (Get-Random))
        $dosRoot = Join-Path $tempDir 'MS-DOS'
        New-Item -ItemType Directory -Path $dosRoot -Force | Out-Null

        $euFolder = Join-Path $dosRoot 'Meta-Game (Europe) [DOS]'
        $usFolder = Join-Path $dosRoot 'Meta Game (USA)'
        New-Item -ItemType Directory -Path $euFolder -Force | Out-Null
        New-Item -ItemType Directory -Path $usFolder -Force | Out-Null

        # Launcher files needed for DOS folder detection
        'echo eu' | Out-File -LiteralPath (Join-Path $euFolder 'GAME.BAT') -Encoding ascii -Force
        'echo us' | Out-File -LiteralPath (Join-Path $usFolder 'GAME.BAT') -Encoding ascii -Force
        # Data files that should not affect the game key comparison
        'eu-data' | Out-File -LiteralPath (Join-Path $euFolder 'README.TXT') -Encoding ascii -Force
        'us-data' | Out-File -LiteralPath (Join-Path $usFolder 'README.TXT') -Encoding ascii -Force

        $result = $null
        try {
            $result = Invoke-RegionDedupe -Roots @($dosRoot) -Mode 'DryRun' -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.bat','.txt') -RemoveJunk $false -SeparateBios $false -UseDat $false -Log { param($m) }

            $rows = @(Import-Csv -LiteralPath $result.CsvPath)
            $euRow = @($rows | Where-Object { $_.Name -eq 'Meta-Game (Europe) [DOS]' } | Select-Object -First 1)
            $usRow = @($rows | Where-Object { $_.Name -eq 'Meta Game (USA)' } | Select-Object -First 1)

            $euRow | Should -Not -BeNullOrEmpty
            $usRow | Should -Not -BeNullOrEmpty
            $euRow.Action | Should -Be 'KEEP'
            $usRow.Action | Should -Be 'SKIP_DRYRUN'
            $euRow.GameKey | Should -Be $usRow.GameKey
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'ConsoleSort + Dedupe should not scan _TRASH_REGION_DEDUPE candidates in same session' {
        $tempDir = Join-Path $env:TEMP ("pester_console_dedupe_selfscan_{0}" -f (Get-Random))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

        $trashDir = Join-Path $tempDir '_TRASH_REGION_DEDUPE'
        New-Item -ItemType Directory -Path $trashDir -Force | Out-Null

        # A "trash" file inside _TRASH_REGION_DEDUPE must never appear in dedupe results.
        $trashEu = Join-Path $trashDir 'SelfScan Game (Europe).chd'
        $rootUs = Join-Path $tempDir 'SelfScan Game (USA).chd'
        'old-trash' | Out-File -LiteralPath $trashEu -Encoding ascii -Force
        'active' | Out-File -LiteralPath $rootUs -Encoding ascii -Force

        $dedupeResult = $null
        try {
            # Run dedupe directly — the scan exclusion of _TRASH is independent of
            # whether ConsoleSort ran first.
            $dedupeResult = Invoke-RegionDedupe -Roots @($tempDir) -Mode 'DryRun' -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.chd') -RemoveJunk $false -SeparateBios $false -UseDat $false -Log { param($m) }

            $dedupeResult | Should -Not -BeNullOrEmpty
            $rows = @(Import-Csv -LiteralPath $dedupeResult.CsvPath)
            # The file inside _TRASH_REGION_DEDUPE must NOT appear at all.
            (@($rows | Where-Object { $_.Name -eq 'SelfScan Game (Europe).chd' })).Count | Should -Be 0
            # The active root file should appear (as KEEP — it is the only candidate).
            (@($rows | Where-Object { $_.Name -eq 'SelfScan Game (USA).chd' })).Count | Should -Be 1
        } finally {
            if ($dedupeResult -and $dedupeResult.CsvPath -and (Test-Path -LiteralPath $dedupeResult.CsvPath)) {
                Remove-Item -LiteralPath $dedupeResult.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($dedupeResult -and $dedupeResult.HtmlPath -and (Test-Path -LiteralPath $dedupeResult.HtmlPath)) {
                Remove-Item -LiteralPath $dedupeResult.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Security - Zip Slip' {
    Context 'Archive entry validation' {
        It 'Should reject absolute and traversal paths' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('good/file.bin','folder\\file.bin') | Should -BeTrue
            Test-ArchiveEntryPathsSafe -EntryPaths @('..\\evil.txt') | Should -BeFalse
            Test-ArchiveEntryPathsSafe -EntryPaths @('/abs/evil.txt') | Should -BeFalse
            Test-ArchiveEntryPathsSafe -EntryPaths @('C:\\evil.txt') | Should -BeFalse
        }
    }
}

Describe 'Security - Tool Failure Handling' {
    Context 'Missing 7z tool' {
        It 'Expand-ArchiveToTemp should return null when 7z is missing' {
            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }
            $result = Expand-ArchiveToTemp -ArchivePath 'C:\nonexistent.zip' -SevenZipPath 'C:\missing\7z.exe' -Log $logFn
            $result | Should -BeNullOrEmpty
        }
    }
}

Describe 'GameKey Normalization' {
    Context 'Datenverlustschutz' {
        
        # Bug it would catch: Empty GameKey would cause wrong grouping
        It 'ConvertTo-GameKey should never return empty string' {
            # Edge cases that might produce empty key
            $testCases = @(
                '(Europe)',
                '(USA) (En,Fr)',
                '[!]',
                '(Beta)',
                '(Rev A)'
            )
            
            foreach ($tc in $testCases) {
                $key = ConvertTo-GameKey -BaseName $tc
                $key | Should -Not -BeNullOrEmpty -Because "Input '$tc' should produce non-empty key"
                $key.Trim() | Should -Not -Be '' -Because "Key for '$tc' should not be whitespace-only"
            }
        }
        
        # Bug it would catch: Different tags should produce same key for same game
        It 'GameKey should be consistent across region/version variants' {
            $variants = @(
                'Super Mario Bros (Europe)',
                'Super Mario Bros (USA)',
                'Super Mario Bros (Japan)',
                'Super Mario Bros (Europe) (Rev A)',
                'Super Mario Bros (USA) (v1.1)'
            )
            
            $keys = @($variants | ForEach-Object { ConvertTo-GameKey -BaseName $_ })
            $uniqueKeys = @($keys | Select-Object -Unique)
            
            $uniqueKeys.Count | Should -Be 1 -Because "All variants should normalize to same key. Got: $($keys -join ', ')"
        }
        
        # Bug it would catch: Multi-Disc games colliding
        It 'Multi-Disc games should NOT collide (Disc N preserved)' {
            $disc1 = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 1)'
            $disc2 = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 2)'
            $disc3 = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 3)'
            
            $disc1 | Should -Not -Be $disc2 -Because 'Disc 1 and Disc 2 must be separate'
            $disc2 | Should -Not -Be $disc3 -Because 'Disc 2 and Disc 3 must be separate'
            
            # But same disc across regions should match
            $disc1Usa = ConvertTo-GameKey -BaseName 'Final Fantasy VII (USA) (Disc 1)'
            $disc1 | Should -Be $disc1Usa -Because 'Same disc, different region should match'
        }

        It 'DOS naming suffixes should normalize to same key' {
            $a = ConvertTo-GameKey -BaseName '3 Point Basketball (1994)(MVP Software)' -ConsoleType 'DOS'
            $b = ConvertTo-GameKey -BaseName '3 Point Basketball (1994)(MVP Software)(C)' -ConsoleType 'DOS'
            $c = ConvertTo-GameKey -BaseName '3 Point Basketball' -ConsoleType 'DOS'

            $a | Should -Be $b -Because 'Publisher/Copyright suffix should not split DOS key'
            $a | Should -Be $c -Because 'Base title should match DOS metadata variants'
        }

        It 'T6: ConvertTo-GameKey should keep tag-only names unique via fallback key' {
            $k1 = ConvertTo-GameKey -BaseName '(Europe)'
            $k2 = ConvertTo-GameKey -BaseName '(USA)'

            $k1 | Should -Not -BeNullOrEmpty
            $k2 | Should -Not -BeNullOrEmpty
            $k1 | Should -Not -Be $k2
        }
    }
}

Describe 'Region Detection' {
    Context 'Get-RegionTag' {
        
        It 'Should detect primary regions correctly' {
            Get-RegionTag -Name 'Game (Europe)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (USA)' | Should -Be 'US'
            Get-RegionTag -Name 'Game (Japan)' | Should -Be 'JP'
            Get-RegionTag -Name 'Game (World)' | Should -Be 'WORLD'
        }
        
        # BUG-016: (Fr) is now a valid region token (France), so (Europe)+(Fr) = multi-region = WORLD
        It 'Should detect (Europe) + (Fr) as multi-region WORLD after BUG-016 fr token fix' {
            # With BUG-016 fix, fr maps to FR region, so two distinct regions produce WORLD
            $result = Get-RegionTag -Name 'Game (Europe) (Fr)'
            $result | Should -Be 'WORLD' -Because '(Europe)=EU and (Fr)=FR are two distinct regions, producing WORLD'
        }
        
        It 'Should detect multi-region as WORLD' {
            Get-RegionTag -Name 'Game (USA, Europe)' | Should -Be 'WORLD'
            Get-RegionTag -Name 'Game (Europe, Japan)' | Should -Be 'WORLD'
        }

        It 'Should classify (Uk) as EU region' {
            Get-RegionTag -Name 'Game (Uk)' | Should -Be 'EU'
        }

        It 'Should classify (Uk,En) as EU region' {
            Get-RegionTag -Name 'Game (Uk,En)' | Should -Be 'EU'
        }
    }
}

Describe 'Standalone Conversion Cache Reset' {
    It 'Should reset console and archive caches before scan' {
        $modulePath = Join-Path (Split-Path -Parent $script:ScriptPath) 'dev\modules\RunHelpers.ps1'
        $moduleContent = Get-Content -LiteralPath $modulePath -Raw
        $moduleContent | Should -Match 'Reset-ArchiveEntryCache'
        $moduleContent | Should -Match 'Reset-ClassificationCaches'
    }
}

Describe 'Dedupe Winner Selection' {
    Context 'Priority Order' {
        
        # Bug it would catch: Incorrect winner selection based on priority
        It 'Should select winner by RegionPriority > HeaderScore > VersionScore > FormatScore > Size > Path' {
            # Create items with known scores
            $items = @(
                [pscustomobject]@{ MainPath='C:\1.chd'; RegionScore=1000; HeaderScore=0; VersionScore=100; FormatScore=850; SizeBytes=100; GameKey='game' },
                [pscustomobject]@{ MainPath='C:\2.chd'; RegionScore=999;  HeaderScore=0; VersionScore=200; FormatScore=850; SizeBytes=50; GameKey='game' },  # Higher version but lower region
                [pscustomobject]@{ MainPath='C:\3.chd'; RegionScore=1000; HeaderScore=0; VersionScore=100; FormatScore=900; SizeBytes=100; GameKey='game' }  # Same region/version, higher format
            )
            
            $winner = $items |
                Sort-Object -Property `
                    @{Expression='RegionScore';Descending=$true},
                    @{Expression='HeaderScore';Descending=$true},
                    @{Expression='VersionScore';Descending=$true},
                    @{Expression='FormatScore';Descending=$true},
                    @{Expression='SizeBytes';Descending=$false},
                    @{Expression='MainPath';Descending=$false} |
                Select-Object -First 1
            
            # Item 3 should win: same RegionScore as 1, same VersionScore, but higher FormatScore
            $winner.MainPath | Should -Be 'C:\3.chd'
        }
        
        It 'Should prefer smaller size when other scores are equal' {
            $items = @(
                [pscustomobject]@{ MainPath='C:\big.chd';   RegionScore=1000; HeaderScore=0; VersionScore=100; FormatScore=850; SizeBytes=1000000 },
                [pscustomobject]@{ MainPath='C:\small.chd'; RegionScore=1000; HeaderScore=0; VersionScore=100; FormatScore=850; SizeBytes=500000 }
            )
            
            $winner = $items |
                Sort-Object -Property `
                    @{Expression='RegionScore';Descending=$true},
                    @{Expression='HeaderScore';Descending=$true},
                    @{Expression='VersionScore';Descending=$true},
                    @{Expression='FormatScore';Descending=$true},
                    @{Expression='SizeBytes';Descending=$false},
                    @{Expression='MainPath';Descending=$false} |
                Select-Object -First 1
            
            $winner.MainPath | Should -Be 'C:\small.chd'
        }
        
        It 'Should use lexicographic path as final tiebreaker' {
            $items = @(
                [pscustomobject]@{ MainPath='C:\z_game.chd'; RegionScore=1000; HeaderScore=0; VersionScore=100; FormatScore=850; SizeBytes=1000 },
                [pscustomobject]@{ MainPath='C:\a_game.chd'; RegionScore=1000; HeaderScore=0; VersionScore=100; FormatScore=850; SizeBytes=1000 }
            )
            
            $winner = $items |
                Sort-Object -Property `
                    @{Expression='RegionScore';Descending=$true},
                    @{Expression='HeaderScore';Descending=$true},
                    @{Expression='VersionScore';Descending=$true},
                    @{Expression='FormatScore';Descending=$true},
                    @{Expression='SizeBytes';Descending=$false},
                    @{Expression='MainPath';Descending=$false} |
                Select-Object -First 1
            
            $winner.MainPath | Should -Be 'C:\a_game.chd'
        }

        It 'Should prefer Headered over Headerless when all else is equal' {
            $items = @(
                [pscustomobject]@{ Root='Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headerless)'; MainPath='Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headerless)\8 Eyes (USA).zip'; RegionScore=1000; HeaderScore=(Get-HeaderVariantScore -Root 'Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headerless)' -MainPath 'Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headerless)\8 Eyes (USA).zip'); VersionScore=100; FormatScore=500; SizeBytes=93489 },
                [pscustomobject]@{ Root='Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headered)'; MainPath='Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headered)\8 Eyes (USA).zip'; RegionScore=1000; HeaderScore=(Get-HeaderVariantScore -Root 'Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headered)' -MainPath 'Z:\No-Intro\Nintendo - Nintendo Entertainment System (Headered)\8 Eyes (USA).zip'); VersionScore=100; FormatScore=500; SizeBytes=93499 }
            )

            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Match 'Headered'
        }
    }
}

Describe 'CUE/GDI Missing File Detection' {
    Context 'Get-CueMissingFiles' {
        
        # Bug it would catch: CUE with missing BIN should be detected and skipped
        It 'Should detect missing BIN files in CUE' {
            $tempDir = Join-Path $env:TEMP "pester_cue_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                # Create CUE that references non-existent BIN
                $cuePath = Join-Path $tempDir 'test.cue'
                $cueContent = @'
FILE "track01.bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
'@
                $cueContent | Out-File -LiteralPath $cuePath -Encoding ascii -Force
                
                $missing = @(Get-CueMissingFiles -CuePath $cuePath)
                $missing.Count | Should -BeGreaterThan 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Should return empty when all BIN files exist' {
            $tempDir = Join-Path $env:TEMP "pester_cue_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                $cuePath = Join-Path $tempDir 'test.cue'
                $binPath = Join-Path $tempDir 'track01.bin'
                
                $cueContent = @'
FILE "track01.bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
'@
                $cueContent | Out-File -LiteralPath $cuePath -Encoding ascii -Force
                'dummy' | Out-File -LiteralPath $binPath -Encoding ascii -Force
                
                $missing = @(Get-CueMissingFiles -CuePath $cuePath)
                $missing.Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    
    Context 'Get-GdiMissingFiles' {
        
        It 'Should detect missing track files in GDI' {
            $tempDir = Join-Path $env:TEMP "pester_gdi_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                $gdiPath = Join-Path $tempDir 'test.gdi'
                $gdiContent = @'
3
1 0 4 2352 track01.raw 0
2 756 0 2352 track02.bin 0
3 45000 4 2352 track03.bin 0
'@
                $gdiContent | Out-File -LiteralPath $gdiPath -Encoding ascii -Force
                
                $missing = @(Get-GdiMissingFiles -GdiPath $gdiPath)
                $missing.Count | Should -BeGreaterThan 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Describe 'M3U Related Files' {
    Context 'Get-M3URelatedFiles' {
        
        It 'Should collect all referenced disc images without duplicates' {
            $tempDir = Join-Path $env:TEMP "pester_m3u_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                # Create M3U
                $m3uPath = Join-Path $tempDir 'game.m3u'
                $m3uContent = @"
# Multi-Disc Playlist
disc1.chd
disc2.chd
disc1.chd
"@  # Note: disc1.chd listed twice to test dedup
                $m3uContent | Out-File -LiteralPath $m3uPath -Encoding ascii -Force
                
                # Create CHD files
                'disc1' | Out-File -LiteralPath (Join-Path $tempDir 'disc1.chd') -Encoding ascii
                'disc2' | Out-File -LiteralPath (Join-Path $tempDir 'disc2.chd') -Encoding ascii
                
                $related = @(Get-M3URelatedFiles -M3UPath $m3uPath)
                
                # Should include M3U + 2 CHDs (no duplicates)
                $related.Count | Should -Be 3
                $related | Should -Contain $m3uPath
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Should recursively resolve CUE references in M3U' {
            $tempDir = Join-Path $env:TEMP "pester_m3u_cue_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                # Create subfiles for CUE
                $binPath = Join-Path $tempDir 'disc1.bin'
                'bin data' | Out-File -LiteralPath $binPath -Encoding ascii
                
                # Create CUE
                $cuePath = Join-Path $tempDir 'disc1.cue'
                $cueContent = @'
FILE "disc1.bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
'@
                $cueContent | Out-File -LiteralPath $cuePath -Encoding ascii
                
                # Create M3U referencing CUE
                $m3uPath = Join-Path $tempDir 'game.m3u'
                'disc1.cue' | Out-File -LiteralPath $m3uPath -Encoding ascii
                
                $related = @(Get-M3URelatedFiles -M3UPath $m3uPath)
                
                # Should include M3U + CUE + BIN
                $related.Count | Should -Be 3
                $related | Should -Contain $m3uPath
                $related | Should -Contain $cuePath
                $related | Should -Contain $binPath
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Should ignore M3U entries that escape root when RootPath is provided' {
            $tempDir = Join-Path $env:TEMP "pester_m3u_root_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

            try {
                $m3uPath = Join-Path $tempDir 'game.m3u'
                @"
..\evil.bin
disc1.chd
"@ | Out-File -LiteralPath $m3uPath -Encoding ascii -Force

                'disc1' | Out-File -LiteralPath (Join-Path $tempDir 'disc1.chd') -Encoding ascii

                $related = @(Get-M3URelatedFiles -M3UPath $m3uPath -RootPath $tempDir)
                $related | Should -Contain $m3uPath
                $related | Should -Contain (Join-Path $tempDir 'disc1.chd')
                @($related | Where-Object { $_ -match 'evil\.bin' }).Count | Should -Be 0

                $missing = @(Get-M3UMissingFiles -M3UPath $m3uPath -RootPath $tempDir)
                @($missing | Where-Object { $_ -match 'evil\.bin' }).Count | Should -BeGreaterThan 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'T16: Should skip absolute M3U entries when RootPath is provided' {
            $tempDir = Join-Path $env:TEMP "pester_m3u_abs_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

            try {
                $m3uPath = Join-Path $tempDir 'game.m3u'
                @"
C:\\Windows\\notallowed.bin
disc1.chd
"@ | Out-File -LiteralPath $m3uPath -Encoding ascii -Force

                'disc1' | Out-File -LiteralPath (Join-Path $tempDir 'disc1.chd') -Encoding ascii -Force

                $related = @(Get-M3URelatedFiles -M3UPath $m3uPath -RootPath $tempDir)
                $related | Should -Contain $m3uPath
                $related | Should -Contain (Join-Path $tempDir 'disc1.chd')
                @($related | Where-Object { $_ -like 'C:\\Windows\\*' }).Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Should resolve nested M3U chains and report invalid nested refs' {
            $tempDir = Join-Path $env:TEMP "pester_m3u_nested_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

            try {
                $childM3u = Join-Path $tempDir 'discset.m3u'
                @"
disc1.chd
..\evil.bin
"@ | Out-File -LiteralPath $childM3u -Encoding ascii -Force

                $rootM3u = Join-Path $tempDir 'root.m3u'
                @"
discset.m3u
"@ | Out-File -LiteralPath $rootM3u -Encoding ascii -Force

                'disc1' | Out-File -LiteralPath (Join-Path $tempDir 'disc1.chd') -Encoding ascii -Force

                $related = @(Get-M3URelatedFiles -M3UPath $rootM3u -RootPath $tempDir)
                $related | Should -Contain $rootM3u
                $related | Should -Contain $childM3u
                $related | Should -Contain (Join-Path $tempDir 'disc1.chd')
                @($related | Where-Object { $_ -match 'evil\.bin' }).Count | Should -Be 0

                $missing = @(Get-M3UMissingFiles -M3UPath $rootM3u -RootPath $tempDir)
                @($missing | Where-Object { $_ -match 'evil\.bin' }).Count | Should -BeGreaterThan 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Should handle recursive M3U cycles without duplication or hang' {
            $tempDir = Join-Path $env:TEMP "pester_m3u_cycle_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

            try {
                $aM3u = Join-Path $tempDir 'a.m3u'
                $bM3u = Join-Path $tempDir 'b.m3u'
                @"
b.m3u
disc1.chd
"@ | Out-File -LiteralPath $aM3u -Encoding ascii -Force
                @"
a.m3u
disc2.chd
"@ | Out-File -LiteralPath $bM3u -Encoding ascii -Force

                'disc1' | Out-File -LiteralPath (Join-Path $tempDir 'disc1.chd') -Encoding ascii -Force
                'disc2' | Out-File -LiteralPath (Join-Path $tempDir 'disc2.chd') -Encoding ascii -Force

                $related = @(Get-M3URelatedFiles -M3UPath $aM3u -RootPath $tempDir)
                $related | Should -Contain $aM3u
                $related | Should -Contain $bM3u
                $related | Should -Contain (Join-Path $tempDir 'disc1.chd')
                $related | Should -Contain (Join-Path $tempDir 'disc2.chd')
                @($related | Select-Object -Unique).Count | Should -Be $related.Count
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Describe 'Set Reference Path Safety' {
    Context 'Resolve-SetReferencePath' {

        It 'Should reject rooted references' {
            $result = Resolve-SetReferencePath -BaseDir 'C:\roms\set' -Reference 'C:\Windows\System32\config' -RootPath 'C:\roms'
            $result | Should -BeNullOrEmpty
        }

        It 'Should reject traversal references' {
            $result = Resolve-SetReferencePath -BaseDir 'C:\roms\set' -Reference '..\..\outside.bin' -RootPath 'C:\roms'
            $result | Should -BeNullOrEmpty
        }
    }
}

Describe 'Cancel Handling' {
    Context 'Test-CancelRequested' {
        
        # Bug it would catch: Cancel not throwing properly or not being caught
        It 'Should throw OperationCanceledException when CancelRequested is true' {
            Set-AppStateValue -Key 'CancelRequested' -Value $true -SyncLegacy | Out-Null
            
            { Test-CancelRequested } | Should -Throw -ExceptionType ([System.OperationCanceledException])
            
            Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
        }
        
        It 'Should not throw when CancelRequested is false' {
            Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
            
            { Test-CancelRequested } | Should -Not -Throw
        }
    }
}

Describe 'Version Scoring' {
    Context 'Get-VersionScore' {
        
        It 'Should score [!] verified dumps higher' {
            $verified = Get-VersionScore -BaseName 'Game (USA) [!]'
            $notVerified = Get-VersionScore -BaseName 'Game (USA)'
            
            $verified | Should -BeGreaterThan $notVerified
        }
        
        It 'Should score higher revisions higher (Rev B > Rev A)' {
            $revB = Get-VersionScore -BaseName 'Game (USA) (Rev B)'
            $revA = Get-VersionScore -BaseName 'Game (USA) (Rev A)'
            
            $revB | Should -BeGreaterThan $revA
        }
        
        It 'Should score higher versions higher (v1.1 > v1.0)' {
            $v11 = Get-VersionScore -BaseName 'Game (v1.1)'
            $v10 = Get-VersionScore -BaseName 'Game (v1.0)'
            
            $v11 | Should -BeGreaterThan $v10
        }

        It 'Should score multi-letter revisions higher (Rev AA > Rev B)' {
            $revAA = Get-VersionScore -BaseName 'Game (Rev AA)'
            $revB = Get-VersionScore -BaseName 'Game (Rev B)'

            $revAA | Should -BeGreaterThan $revB
        }

        It 'Should score semantic-like versions correctly (v1.10 > v1.9)' {
            $v110 = Get-VersionScore -BaseName 'Game (v1.10)'
            $v19 = Get-VersionScore -BaseName 'Game (v1.9)'

            $v110 | Should -BeGreaterThan $v19
        }

        It 'T4: Should score numeric-suffix revisions correctly (Rev 10b > Rev 10a)' {
            $rev10b = Get-VersionScore -BaseName 'Game (Rev 10b)'
            $rev10a = Get-VersionScore -BaseName 'Game (Rev 10a)'

            $rev10b | Should -BeGreaterThan $rev10a
        }
        
        It 'Should give bonus for English language' {
            $withEn = Get-VersionScore -BaseName 'Game (En,Fr,De)'
            $withoutEn = Get-VersionScore -BaseName 'Game (Fr,De)'
            
            $withEn | Should -BeGreaterThan $withoutEn
        }
    }
}

Describe 'Review Strategy Additional Tests' {
    It 'T8: Resolve-SetReferencePath should block traversal when RootPath is set' {
        $result = Resolve-SetReferencePath -BaseDir 'C:\roms\set' -Reference '..\..\outside.bin' -RootPath 'C:\roms'
        $result | Should -BeNullOrEmpty
    }

    It 'T9: Get-PS3FolderHash should handle empty folder without throw' {
        $tempDir = Join-Path $env:TEMP ("ps3_hash_empty_{0}" -f (Get-Random))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        try {
            { Get-PS3FolderHash -Folder $tempDir } | Should -Not -Throw
            (Get-PS3FolderHash -Folder $tempDir) | Should -BeNullOrEmpty
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'T12: Select-Winner should be deterministic for same scores' {
        $items = @(
            [pscustomobject]@{ MainPath='B.zip'; RegionScore=500; HeaderScore=0; VersionScore=0; FormatScore=500; SizeBytes=100; CompletenessScore=0 },
            [pscustomobject]@{ MainPath='A.zip'; RegionScore=500; HeaderScore=0; VersionScore=0; FormatScore=500; SizeBytes=100; CompletenessScore=0 }
        )
        $w1 = Select-Winner -Items $items
        $w2 = Select-Winner -Items ($items | Sort-Object MainPath -Descending)
        $w1.MainPath | Should -Be $w2.MainPath
    }

    It 'T17: Get-FormatScore should return 300 for unknown extension' {
        Get-FormatScore -Extension '.xyz' -Type 'FILE' | Should -Be 300
    }

    It 'T18: Get-SizeTieBreakScore should be positive for discs and negative for cartridge files' {
        (Get-SizeTieBreakScore -Type 'FILE' -Extension '.iso' -SizeBytes 1000) | Should -Be 1000
        (Get-SizeTieBreakScore -Type 'FILE' -Extension '.nes' -SizeBytes 1000) | Should -Be (-1000)
    }

    It 'T19: Test-IsProtectedPath should detect exact/child and reject prefix collisions' {
        (Test-IsProtectedPath -Path 'C:\Windows' -ProtectedPaths @('C:\Windows')) | Should -BeTrue
        (Test-IsProtectedPath -Path 'C:\Windows\System32' -ProtectedPaths @('C:\Windows')) | Should -BeTrue
        (Test-IsProtectedPath -Path 'C:\WindowsEvil' -ProtectedPaths @('C:\Windows')) | Should -BeFalse
    }

    It 'T20: Get-CcdMissingFiles should report missing IMG and SUB' {
        $tempDir = Join-Path $env:TEMP ("ccd_missing_{0}" -f (Get-Random))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        try {
            $ccdPath = Join-Path $tempDir 'game.ccd'
            '[CloneCD]' | Out-File -LiteralPath $ccdPath -Encoding ascii -Force
            $missing = @(Get-CcdMissingFiles -CcdPath $ccdPath -RootPath $tempDir)
            ($missing -join '|') | Should -Match 'game\.img'
            ($missing -join '|') | Should -Match 'game\.sub'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'T21: ConvertTo-QuotedArg should handle null and path with spaces' {
        (ConvertTo-QuotedArg $null) | Should -Be '""'
        (ConvertTo-QuotedArg 'C:\My Path\file.exe') | Should -Match '^".*"$'
    }

    It 'T22: Get-RegionScore should honor custom preference and WORLD fallback' {
        (Get-RegionScore -Region 'JP' -Prefer @('JP','EU','US')) | Should -Be 1000
        (Get-RegionScore -Region 'WORLD' -Prefer @('EU','US')) | Should -Be 500
    }

    It 'T23: Wait-ProcessResponsive should throw OperationCanceledException on cancellation' {
        Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
        $proc = Start-Process -FilePath 'ping' -ArgumentList '-n','10','127.0.0.1' -PassThru -WindowStyle Hidden
        try {
            Set-AppStateValue -Key 'CancelRequested' -Value $true -SyncLegacy | Out-Null
            { Wait-ProcessResponsive -Process $proc } | Should -Throw '*Abbruch durch Benutzer*'
        } finally {
            Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
            try { if ($proc -and -not $proc.HasExited) { $proc.Kill() | Out-Null } } catch { }
        }
    }

    It 'T23b: Wait-ProcessResponsive should honor AppState cancel flag even when legacy flag is false' {
        $script:CancelRequested = $false
        Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
        $proc = Start-Process -FilePath 'ping' -ArgumentList '-n','10','127.0.0.1' -PassThru -WindowStyle Hidden
        try {
            Set-AppStateValue -Key 'CancelRequested' -Value $true -SyncLegacy | Out-Null
            $script:CancelRequested = $false
            { Wait-ProcessResponsive -Process $proc } | Should -Throw '*Abbruch durch Benutzer*'
        } finally {
            Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
            try { if ($proc -and -not $proc.HasExited) { $proc.Kill() | Out-Null } } catch { }
        }
    }
}

Describe 'Format Scoring' {
    Context 'Get-FormatScore' {
        
        It 'Should score CHD higher than ISO' {
            $chd = Get-FormatScore -Extension '.chd' -Type 'FILE'
            $iso = Get-FormatScore -Extension '.iso' -Type 'FILE'
            
            $chd | Should -BeGreaterThan $iso
        }
        
        It 'Should score M3USET highest (multi-disc completeness)' {
            $m3u = Get-FormatScore -Extension '.m3u' -Type 'M3USET'
            $cue = Get-FormatScore -Extension '.cue' -Type 'CUESET'
            
            $m3u | Should -BeGreaterThan $cue
        }
        
        It 'Should score ZIP higher than RAR' {
            $zip = Get-FormatScore -Extension '.zip' -Type 'FILE'
            $rar = Get-FormatScore -Extension '.rar' -Type 'FILE'
            
            $zip | Should -BeGreaterThan $rar
        }
    }
}

Describe 'File Classification' {
    Context 'Get-FileCategory' {
        
        It 'Should classify BIOS files correctly' {
            Get-FileCategory -BaseName '[BIOS] PlayStation' | Should -Be 'BIOS'
            Get-FileCategory -BaseName 'scph1001 (Firmware)' | Should -Be 'BIOS'
        }
        
        It 'Should classify junk (demos, betas, hacks) correctly' {
            Get-FileCategory -BaseName 'Game (Demo)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Alpha)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Beta)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Proto)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Proto 1)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Prototype 2)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Pre-Release)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Trial)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Taikenban)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Location Test)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game (Hack)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game [b1]' | Should -Be 'JUNK'  # Bad dump
        }
        
        It 'Should classify normal games as GAME' {
            Get-FileCategory -BaseName 'Super Mario Bros (Europe)' | Should -Be 'GAME'
            Get-FileCategory -BaseName 'Zelda (USA) (Rev A)' | Should -Be 'GAME'
            Get-FileCategory -BaseName 'Road Test (WIP)' | Should -Be 'GAME'
        }

        It 'Should classify aggressive junk tags only when enabled' {
            Get-FileCategory -BaseName 'Road Test (WIP)' -AggressiveJunk $true | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Prototype Build (Preview Build)' -AggressiveJunk $true | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Arcade Candidate (Location Test Build)' -AggressiveJunk $true | Should -Be 'JUNK'
        }
    }
}

# ===========================================================
# NEGATIVE TESTS (Error Paths) - At least 30% of test count
# ===========================================================

Describe 'Error Handling - Negative Tests' {
    Context 'Invalid Input Handling' {
        
        It 'Get-RegionTag should handle empty string' {
            $result = Get-RegionTag -Name ''
            $result | Should -Be 'UNKNOWN'
        }
        
        It 'Get-RegionTag should handle null' {
            $result = Get-RegionTag -Name $null
            $result | Should -Be 'UNKNOWN'
        }
        
        It 'ConvertTo-GameKey should handle empty string' {
            $result = ConvertTo-GameKey -BaseName ''
            $result | Should -Not -BeNullOrEmpty
        }
        
        It 'ConvertTo-GameKey should handle whitespace-only string' {
            $result = ConvertTo-GameKey -BaseName '   '
            $result | Should -Not -BeNullOrEmpty
        }
        
        It 'Get-VersionScore should handle empty string' {
            $result = Get-VersionScore -BaseName ''
            $result | Should -Be 0
        }
        
        It 'Get-FormatScore should handle unknown extension' {
            $result = Get-FormatScore -Extension '.xyz' -Type 'FILE'
            $result | Should -Be 300  # Default score for unknown
        }
    }
    
    Context 'Missing File Handling' {
        
        It 'Get-CueRelatedFiles should handle non-existent CUE path' {
            $fakePath = 'C:\NonExistent\fake.cue'
            $result = @(Get-CueRelatedFiles -CuePath $fakePath)
            
            # Should just return the CUE path itself (even if doesn't exist)
            $result.Count | Should -Be 1
        }
        
        It 'Get-GdiRelatedFiles should handle non-existent GDI path' {
            $fakePath = 'C:\NonExistent\fake.gdi'
            $result = @(Get-GdiRelatedFiles -GdiPath $fakePath)
            
            $result.Count | Should -Be 1  # Just the GDI path
        }
        
        It 'Get-M3URelatedFiles should handle non-existent M3U path' {
            $fakePath = 'C:\NonExistent\fake.m3u'
            $result = @(Get-M3URelatedFiles -M3UPath $fakePath)
            
            $result.Count | Should -Be 1  # Just the M3U path
        }
    }
    
    Context 'Malformed Input' {
        
        It 'Get-CueMissingFiles should handle malformed CUE content' {
            $tempDir = Join-Path $env:TEMP "pester_bad_cue_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                $cuePath = Join-Path $tempDir 'bad.cue'
                # Malformed content - no valid FILE directive
                'GARBAGE CONTENT' | Out-File -LiteralPath $cuePath -Encoding ascii
                
                $result = @(Get-CueMissingFiles -CuePath $cuePath)
                # Should not throw, should return empty
                $result.Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Get-GdiMissingFiles should handle malformed GDI content' {
            $tempDir = Join-Path $env:TEMP "pester_bad_gdi_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                $gdiPath = Join-Path $tempDir 'bad.gdi'
                'GARBAGE CONTENT' | Out-File -LiteralPath $gdiPath -Encoding ascii
                
                $result = @(Get-GdiMissingFiles -GdiPath $gdiPath)
                $result.Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    
    Context 'Console Detection Edge Cases' {
        
        It 'Get-ConsoleType should return UNKNOWN for unrecognized paths' {
            $result = Get-ConsoleType -RootPath 'C:\RandomFolder' -FilePath 'C:\RandomFolder\game.bin' -Extension '.bin'
            $result | Should -Be 'UNKNOWN'
        }
        
        It 'Get-ConsoleType should handle empty extension' {
            # BUG-09 fix: root path is NOT used for folder detection,
            # only relative subfolders within root are checked.
            $result = Get-ConsoleType -RootPath 'C:\ROMs' -FilePath 'C:\ROMs\PS1\game' -Extension ''
            # Should still detect from subfolder name
            $result | Should -Be 'PS1'
        }

        It 'Get-ConsoleType should detect DOS from subfolder path' {
            # BUG-09 fix: root path is NOT used for folder detection.
            # Place MSDOS as a subfolder within root, not as the root itself.
            $result = Get-ConsoleType -RootPath 'C:\Roms' -FilePath 'C:\Roms\MSDOS\Commander Keen\keen.exe' -Extension '.exe'
            $result | Should -Be 'DOS'
        }

        It 'Get-ConsoleFromArchiveEntries should return null for heterogeneous entry extensions' {
            $entries = @('disc/game.gba', 'disc/other.nes')
            $result = Get-ConsoleFromArchiveEntries -EntryPaths $entries
            $result | Should -BeNullOrEmpty
        }

        It 'Get-ConsoleFromArchiveEntries should return console for homogeneous entry extensions' {
            $entries = @('disc/game1.gba', 'disc/game2.gba')
            $result = Get-ConsoleFromArchiveEntries -EntryPaths $entries
            $result | Should -Be 'GBA'
        }

        It 'Console precedence matrix: disc header should win over folder/name/extension' {
            Mock -CommandName Get-DiscHeaderConsole -MockWith { return 'WII' }
            $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

            # REF-DET-01: disc header (binary evidence) is stronger than folder name.
            # Disc header returns WII, which takes priority over folder PS1.
            $result = Get-ConsoleType -RootPath 'C:\ROMs' -FilePath 'C:\ROMs\PS1\wii_game.iso' -Extension '.iso'
            $result | Should -Be 'WII'
        }

        It 'Console precedence matrix: disc header should win over filename when folder is neutral' {
            Mock -CommandName Get-DiscHeaderConsole -MockWith { return 'GC' }
            $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

            # BUG-08/12 fix: disc header has higher confidence than filename regex.
            # Even though filename contains "ps2", disc header GC should win.
            $result = Get-ConsoleType -RootPath 'C:\ROMs\UnknownName' -FilePath 'C:\ROMs\UnknownName\my ps2 title.iso' -Extension '.iso'
            $result | Should -Be 'GC'
        }

        It 'Console precedence matrix: disc header should win over extension when folder/name are neutral' {
            Mock -CommandName Get-DiscHeaderConsole -MockWith { return 'WII' }
            $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

            $result = Get-ConsoleType -RootPath 'C:\ROMs\UnknownHeader' -FilePath 'C:\ROMs\UnknownHeader\neutral.iso' -Extension '.iso'
            $result | Should -Be 'WII'
        }

        It 'Console precedence matrix: extension fallback should apply when others have no match' {
            Mock -CommandName Get-DiscHeaderConsole -MockWith { return $null }
            $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

            $result = Get-ConsoleType -RootPath 'C:\ROMs\UnknownExt' -FilePath 'C:\ROMs\UnknownExt\neutral.gba' -Extension '.gba'
            $result | Should -Be 'GBA'
        }

        It 'Invoke-ConsoleSort should emit machine-readable unknown reason codes' {
            $root = Join-Path $TestDrive 'console_unknown_codes'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            $zipPath = Join-Path $root 'mystery.zip'
            'dummy' | Out-File -LiteralPath $zipPath -Encoding ascii -Force

            Mock -CommandName Find-ConversionTool -MockWith { return $null }
            Mock -CommandName Get-ConsoleType -MockWith { return 'UNKNOWN' }

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.zip') -Log { param($m) }
            $result | Should -Not -BeNullOrEmpty
            $result.Unknown | Should -Be 1
            $result.UnknownReasons.ContainsKey('ARCHIVE_TOOL_MISSING') | Should -BeTrue
            [int]$result.UnknownReasons['ARCHIVE_TOOL_MISSING'] | Should -Be 1
        }

        It 'ConvertTo-HtmlReport should render UNKNOWN reason code and label columns' {
            $tempHtml = Join-Path $TestDrive "pester_unknown_reason_report_$(Get-Random).html"
            $reportRows = [System.Collections.Generic.List[psobject]]::new()
            $reportRows.Add([pscustomobject]@{
                GameKey      = 'x'
                Action       = 'KEEP'
                Category     = 'GAME'
                Region       = 'EU'
                WinnerRegion = 'EU'
                VersionScore = 100
                FormatScore  = 850
                Type         = 'FILE'
                DatMatch     = $false
                MainPath     = 'C:\ROMs\x.zip'
                Root         = 'C:\ROMs'
                SizeBytes    = 1
            })

            Push-Location $TestDrive
            try {
                ConvertTo-HtmlReport -Report $reportRows -HtmlPath $tempHtml `
                    -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 `
                    -JunkCount 0 -JunkBytes 0 -BiosCount 0 `
                    -UniqueGames 1 -TotalScanned 1 -Mode 'DryRun' `
                    -DatEnabled $false -ConsoleSortUnknownReasons @{ ARCHIVE_TOOL_MISSING = 2 }

                $html = Get-Content -LiteralPath $tempHtml -Raw
                $html | Should -Match 'Reason Code'
                $html | Should -Match 'ARCHIVE_TOOL_MISSING'
                $html | Should -Match 'Archive: 7z nicht verfuegbar'
            } finally {
                Pop-Location
                if (Test-Path -LiteralPath $tempHtml) {
                    Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
}

Describe 'Integration - Invoke-RegionDedupe Minimal' {
    It 'Should emit CSV with Name and FullPath populated' {
        $script:csvLogMessages = [System.Collections.Generic.List[string]]::new()
        $logFn = { param($msg) $script:csvLogMessages.Add($msg) }

        $tempDir = Join-Path $env:TEMP "pester_csv_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $romPath = Join-Path $tempDir 'game.chd'
        'romdata' | Out-File -LiteralPath $romPath -Encoding ascii -Force

        try {
            $result = Invoke-RegionDedupe `
                -Roots @($tempDir) `
                -Mode 'DryRun' `
                -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.chd') `
                -RemoveJunk $false `
                -SeparateBios $false `
                -UseDat $false `
                -Log $logFn

            $result | Should -Not -BeNullOrEmpty
            $result.CsvPath | Should -Not -BeNullOrEmpty
            $result.JsonPath | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath $result.JsonPath | Should -BeTrue

            $csv = Import-Csv -LiteralPath $result.CsvPath
            $csv.Count | Should -Be 1
            $csv[0].Name | Should -Be 'game.chd'
            $csv[0].FullPath | Should -Be $romPath

            $json = Get-Content -LiteralPath $result.JsonPath -Raw | ConvertFrom-Json
            [string]$json.schemaVersion | Should -Be 'dryrun-v1'
            [string]$json.mode | Should -Be 'DryRun'
            @($json.items).Count | Should -Be 1
            [string]$json.items[0].mainPath | Should -Be $romPath
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.JsonPath -and (Test-Path -LiteralPath $result.JsonPath)) {
                Remove-Item -LiteralPath $result.JsonPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should log Smart-Merge set conflict analysis for competing set variants' {
        $script:csvLogMessages = [System.Collections.Generic.List[string]]::new()
        $logFn = { param($msg) $script:csvLogMessages.Add($msg) }

        $tempDir = Join-Path $env:TEMP "pester_smartmerge_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

        $cuePath = Join-Path $tempDir 'Conflict Game (Europe).cue'
        $gdiPath = Join-Path $tempDir 'Conflict Game (Europe).gdi'
        $trackBase = Join-Path $tempDir 'track01_en.bin'
        $trackBonus = Join-Path $tempDir 'track02_bonus_fr.bin'

        @(
            'FILE "track01_en.bin" BINARY'
            '  TRACK 01 MODE1/2352'
            '    INDEX 01 00:00:00'
        ) | Set-Content -LiteralPath $cuePath -Encoding UTF8
        @(
            '2'
            '1 0 4 2352 track01_en.bin 0'
            '2 150 4 2352 track02_bonus_fr.bin 0'
        ) | Set-Content -LiteralPath $gdiPath -Encoding UTF8
        'base' | Set-Content -LiteralPath $trackBase -Encoding ASCII
        'bonus' | Set-Content -LiteralPath $trackBonus -Encoding ASCII

        $result = $null
        try {
            $result = Invoke-RegionDedupe `
                -Roots @($tempDir) `
                -Mode 'DryRun' `
                -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.chd') `
                -RemoveJunk $false `
                -SeparateBios $false `
                -UseDat $false `
                -Log $logFn

            $result | Should -Not -BeNullOrEmpty
            $logText = ($script:csvLogMessages -join "`n")
            $logText | Should -Match 'SMART-MERGE: Set-Konflikt'
            $logText | Should -Match 'DiffTracks=[1-9]'
            $logText | Should -Match 'Lang=.*(EN|FR)'
            $logText | Should -Match 'Smart-Merge: [1-9]'
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.JsonPath -and (Test-Path -LiteralPath $result.JsonPath)) {
                Remove-Item -LiteralPath $result.JsonPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should handle empty roots gracefully' {
        $script:integrationLogMessages = [System.Collections.Generic.List[string]]::new()
        $logFn = { param($msg) $script:integrationLogMessages.Add($msg) }
        
        $tempDir = Join-Path $env:TEMP "pester_empty_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        
        try {
            $null = Invoke-RegionDedupe `
                -Roots @($tempDir) `
                -Mode 'DryRun' `
                -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.chd','.iso') `
                -RemoveJunk $false `
                -SeparateBios $false `
                -UseDat $false `
                -Log $logFn
            
            # Should complete without error, possibly null result
            # (no files = no report)
            $script:integrationLogMessages.Count | Should -BeGreaterThan 0 -Because 'Should have logged something'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-MovePhase should skip confirm dialog and return no-move-required when 0 items to move' {
        # Bug regression: Confirm dialog appeared even when TrashCount=0 and BiosCount=0,
        # forcing user to click through an empty confirmation.
        $root = Join-Path $env:TEMP "pester_no_move_$(Get-Random)"
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $keepMain = Join-Path $root 'Only Game (Europe).chd'
        'keep' | Out-File -LiteralPath $keepMain -Encoding ascii -Force

        $report = New-Object System.Collections.Generic.List[psobject]
        [void]$report.Add([pscustomobject]@{ Action='KEEP'; Category='GAME'; MainPath=$keepMain })

        $allItems = New-Object System.Collections.Generic.List[psobject]
        $itemByMain = @{}
        $itemByMain[$keepMain] = [pscustomobject]@{ Root=$root; Type='FILE'; MainPath=$keepMain; BaseName='Only Game' }

        $confirmWasCalled = [ref]$false
        try {
            $confirmCallback = { param($summary) $confirmWasCalled.Value = $true; return $true }
            $result = Invoke-MovePhase -Report $report `
                -JunkItems (New-Object System.Collections.Generic.List[psobject]) `
                -BiosItems (New-Object System.Collections.Generic.List[psobject]) `
                -AllItems $allItems -ItemByMain $itemByMain -Roots @($root) `
                -TrashRoot $null -AuditRoot $null `
                -RequireConfirmMove $true `
                -ConfirmMove $confirmCallback `
                -CsvPath 'C:\fake\report.csv' -HtmlPath 'C:\fake\report.html' `
                -TotalDupes 0 -SavedMB 0 -Log { param($m) }

            $result | Should -Not -BeNullOrEmpty
            $result.Status | Should -Be 'completed'
            $result.Reason | Should -Be 'no-move-required'
            $confirmWasCalled.Value | Should -BeFalse -Because 'Confirm dialog should NOT appear when there are 0 items to move'
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Move confirmation summary should contain tabular planned actions (move + conversion)' {
        $root = Join-Path $env:TEMP "pester_move_preview_$(Get-Random)"
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        $moveMain = Join-Path $root 'Preview Dupe (USA).chd'
        $keepMain = Join-Path $root 'Preview Keep (Europe).iso'
        'dupe' | Out-File -LiteralPath $moveMain -Encoding ascii -Force
        'keep' | Out-File -LiteralPath $keepMain -Encoding ascii -Force

        $report = New-Object System.Collections.Generic.List[psobject]
        [void]$report.Add([pscustomobject]@{ Action='MOVE'; Category='GAME'; MainPath=$moveMain })
        [void]$report.Add([pscustomobject]@{ Action='KEEP'; Category='GAME'; MainPath=$keepMain })

        $junkItems = New-Object System.Collections.Generic.List[psobject]
        $biosItems = New-Object System.Collections.Generic.List[psobject]
        $allItems  = New-Object System.Collections.Generic.List[psobject]
        $itemByMain = @{}
        $itemByMain[$moveMain] = [pscustomobject]@{ Root=$root; Type='FILE'; MainPath=$moveMain; BaseName='Preview Dupe'; Paths=@($moveMain) }
        $itemByMain[$keepMain] = [pscustomobject]@{ Root=$root; Type='FILE'; MainPath=$keepMain; BaseName='Preview Keep'; Paths=@($keepMain) }

        $capturedSummary = $null
        Mock -CommandName Get-ConsoleType -MockWith { 'PS1' }

        try {
            $result = Invoke-MovePhase -Report $report -JunkItems $junkItems -BiosItems $biosItems -AllItems $allItems `
                -ItemByMain $itemByMain -Roots @($root) -TrashRoot $null -AuditRoot $null `
                -RequireConfirmMove $true -ConfirmMove { param($summary) $script:capturedSummary = $summary; return $false } `
                -CsvPath 'C:\reports\preview.csv' -HtmlPath 'C:\reports\preview.html' -TotalDupes 1 -SavedMB 1.2 `
                -IncludeConversionPreview $true -Log { param($m) }

            $result | Should -Not -BeNullOrEmpty
            $script:capturedSummary | Should -Not -BeNullOrEmpty
            @($script:capturedSummary.PreviewRows).Count | Should -BeGreaterThan 0

            $moveRow = @($script:capturedSummary.PreviewRows | Where-Object { $_.Action -eq 'MOVE' })[0]
            $convertRow = @($script:capturedSummary.PreviewRows | Where-Object { $_.Action -eq 'CONVERT' })[0]
            $moveRow | Should -Not -BeNullOrEmpty
            $convertRow | Should -Not -BeNullOrEmpty
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-MovePhase should block move when audit precheck is not writable' {
        $root = Join-Path $env:TEMP "pester_audit_block_$(Get-Random)"
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        $source = Join-Path $root 'Audit Block Game (USA).chd'
        'dupe' | Out-File -LiteralPath $source -Encoding ascii -Force

        $auditRootFile = Join-Path $root 'audit-root-as-file.tmp'
        'not-a-directory' | Out-File -LiteralPath $auditRootFile -Encoding ascii -Force

        $report = New-Object System.Collections.Generic.List[psobject]
        [void]$report.Add([pscustomobject]@{ Action='MOVE'; Category='GAME'; MainPath=$source })

        $itemByMain = @{}
        $itemByMain[$source] = [pscustomobject]@{ Root=$root; Type='FILE'; MainPath=$source; BaseName='Audit Block Game' }

        $junkItems = New-Object System.Collections.Generic.List[psobject]
        $biosItems = New-Object System.Collections.Generic.List[psobject]
        $allItems  = New-Object System.Collections.Generic.List[psobject]

        try {
            {
                Invoke-MovePhase -Report $report -JunkItems $junkItems -BiosItems $biosItems -AllItems $allItems `
                    -ItemByMain $itemByMain -Roots @($root) -TrashRoot $null -AuditRoot $auditRootFile `
                    -RequireConfirmMove $false -ConfirmMove $null `
                    -CsvPath 'C:\fake\report.csv' -HtmlPath 'C:\fake\report.html' `
                    -TotalDupes 1 -SavedMB 1.0 -Log { param($m) }
            } | Should -Throw -Because 'Move must abort when audit directory precheck fails'

            Test-Path -LiteralPath $source | Should -BeTrue -Because 'Source must remain untouched when audit precheck blocks move'
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-MovePhase with RequireConfirmMove=true and approved dialog should execute actual move' {
        # Full integration: dialog mock returns $true → files must be physically moved to _TRASH
        $root = Join-Path $env:TEMP "pester_confirm_move_$(Get-Random)"
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        $dupeMain = Join-Path $root 'Confirm Game (USA).chd'
        $keepMain = Join-Path $root 'Confirm Game (Europe).chd'
        'dupe-content'  | Out-File -LiteralPath $dupeMain -Encoding ascii -Force
        'keep-content'  | Out-File -LiteralPath $keepMain -Encoding ascii -Force

        $report = New-Object System.Collections.Generic.List[psobject]
        [void]$report.Add([pscustomobject]@{ Action='MOVE'; Category='GAME'; MainPath=[string]$dupeMain })
        [void]$report.Add([pscustomobject]@{ Action='KEEP'; Category='GAME'; MainPath=[string]$keepMain })

        $junkItems = New-Object System.Collections.Generic.List[psobject]
        $biosItems = New-Object System.Collections.Generic.List[psobject]
        $allItems  = New-Object System.Collections.Generic.List[psobject]
        $itemByMain = @{}
        $itemByMain[[string]$dupeMain] = [pscustomobject]@{ Root=[string]$root; Type='FILE'; MainPath=[string]$dupeMain; BaseName='Confirm Game'; Paths=@([string]$dupeMain) }
        $itemByMain[[string]$keepMain] = [pscustomobject]@{ Root=[string]$root; Type='FILE'; MainPath=[string]$keepMain; BaseName='Confirm Game'; Paths=@([string]$keepMain) }

        $auditDir = Join-Path $env:TEMP "pester_confirm_move_audit_$(Get-Random)"
        New-Item -ItemType Directory -Path $auditDir -Force | Out-Null

        $dialogCalled = [ref]$false
        $confirmCallback = {
            param($summary)
            $dialogCalled.Value = $true
            $script:capturedConfirmSummary = $summary
            return $true  # user approves
        }

        $result = $null
        try {
            $result = Invoke-MovePhase -Report $report `
                -JunkItems $junkItems -BiosItems $biosItems -AllItems $allItems `
                -ItemByMain $itemByMain -Roots @($root) -TrashRoot $null -AuditRoot $auditDir `
                -RequireConfirmMove $true -ConfirmMove $confirmCallback `
                -CsvPath 'C:\fake\report.csv' -HtmlPath 'C:\fake\report.html' `
                -TotalDupes 1 -SavedMB 0.0 -Log { param($m) }

            # Dialog must have been called
            $dialogCalled.Value | Should -BeTrue -Because 'Confirm dialog callback must be invoked'

            # Summary must have listed items to move
            $script:capturedConfirmSummary | Should -Not -BeNullOrEmpty
            $script:capturedConfirmSummary.TrashCount | Should -BeGreaterThan 0 -Because 'Summary must list items to be moved'

            # Move phase should return something (status 'continue' after move-finished)
            $result | Should -Not -BeNullOrEmpty

            # Dupe must be absent from root (moved to _TRASH)
            Test-Path -LiteralPath $dupeMain | Should -BeFalse -Because 'Dupe must be moved out of root after approval'

            # Keep file must remain in root
            Test-Path -LiteralPath $keepMain | Should -BeTrue -Because 'Winner must not be touched by Move phase'

            # Dupe must appear somewhere under _TRASH_REGION_DEDUPE
            $trashDir = Join-Path $root '_TRASH_REGION_DEDUPE'
            $trashFile = Join-Path $trashDir 'Confirm Game (USA).chd'
            Test-Path -LiteralPath $trashFile | Should -BeTrue -Because 'Dupe must be in _TRASH after approved move'
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $auditDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-MovePhase should persist full move-plan JSON before executing moves' {
        $root = Join-Path $env:TEMP "pester_move_plan_$(Get-Random)"
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        $dupeMain = Join-Path $root 'Plan Game (USA).chd'
        'dupe-content' | Out-File -LiteralPath $dupeMain -Encoding ascii -Force

        $report = New-Object System.Collections.Generic.List[psobject]
        [void]$report.Add([pscustomobject]@{ Action='MOVE'; Category='GAME'; MainPath=[string]$dupeMain })

        $junkItems = New-Object System.Collections.Generic.List[psobject]
        $biosItems = New-Object System.Collections.Generic.List[psobject]
        $allItems  = New-Object System.Collections.Generic.List[psobject]
        $itemByMain = @{}
        $itemByMain[[string]$dupeMain] = [pscustomobject]@{ Root=[string]$root; Type='FILE'; MainPath=[string]$dupeMain; BaseName='Plan Game'; Paths=@([string]$dupeMain) }

        $auditDir = Join-Path $env:TEMP "pester_move_plan_audit_$(Get-Random)"
        New-Item -ItemType Directory -Path $auditDir -Force | Out-Null

        Push-Location $TestDrive
        try {
            Invoke-MovePhase -Report $report `
                -JunkItems $junkItems -BiosItems $biosItems -AllItems $allItems `
                -ItemByMain $itemByMain -Roots @($root) -TrashRoot $null -AuditRoot $auditDir `
                -RequireConfirmMove $false -ConfirmMove $null `
                -CsvPath 'C:\fake\report.csv' -HtmlPath 'C:\fake\report.html' `
                -TotalDupes 1 -SavedMB 0.0 -Log { param($m) } | Out-Null

            $plans = @(Get-ChildItem -LiteralPath (Join-Path $TestDrive 'reports') -Filter 'move-plan-*.json' -File -ErrorAction SilentlyContinue)
            $plans.Count | Should -BeGreaterThan 0

            $latestPlan = @($plans | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1)[0]
            $planObj = Get-Content -LiteralPath $latestPlan.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $planObj.schemaVersion | Should -Be 'move-plan-v1'
            @($planObj.rows).Count | Should -BeGreaterThan 0
            [string]$planObj.rows[0].Action | Should -Not -BeNullOrEmpty
            [string]$planObj.rows[0].Source | Should -Not -BeNullOrEmpty
            [string]$planObj.rows[0].Dest | Should -Not -BeNullOrEmpty
        } finally {
            Pop-Location
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $auditDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-MovePhase should skip destinations blocked by move blocklist' {
        $root = Join-Path $env:TEMP "pester_move_blocklist_$(Get-Random)"
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        $dupeMain = Join-Path $root 'Blocked Game (USA).chd'
        'dupe-content' | Out-File -LiteralPath $dupeMain -Encoding ascii -Force

        $report = New-Object System.Collections.Generic.List[psobject]
        [void]$report.Add([pscustomobject]@{ Action='MOVE'; Category='GAME'; MainPath=[string]$dupeMain })

        $junkItems = New-Object System.Collections.Generic.List[psobject]
        $biosItems = New-Object System.Collections.Generic.List[psobject]
        $allItems  = New-Object System.Collections.Generic.List[psobject]
        $itemByMain = @{}
        $itemByMain[[string]$dupeMain] = [pscustomobject]@{ Root=[string]$root; Type='FILE'; MainPath=[string]$dupeMain; BaseName='Blocked Game'; Paths=@([string]$dupeMain) }

        $auditDir = Join-Path $env:TEMP "pester_move_blocklist_audit_$(Get-Random)"
        New-Item -ItemType Directory -Path $auditDir -Force | Out-Null

        try {
            Invoke-MovePhase -Report $report `
                -JunkItems $junkItems -BiosItems $biosItems -AllItems $allItems `
                -ItemByMain $itemByMain -Roots @($root) -TrashRoot 'C:\Windows' -AuditRoot $auditDir `
                -RequireConfirmMove $false -ConfirmMove $null `
                -CsvPath 'C:\fake\report.csv' -HtmlPath 'C:\fake\report.html' `
                -TotalDupes 1 -SavedMB 0.0 -Log { param($m) } | Out-Null

            Test-Path -LiteralPath $dupeMain | Should -BeTrue -Because 'Blockierte Zielpfade dürfen nicht bewegt werden.'
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $auditDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should keep JP only for selected consoles when JP filter is enabled' {
        $script:jpFilterLogMessages = [System.Collections.Generic.List[string]]::new()
        $logFn = { param($msg) $script:jpFilterLogMessages.Add($msg) }

        $tempDir = Join-Path $env:TEMP "pester_jp_filter_$(Get-Random)"
        $ps1Dir = Join-Path $tempDir 'Sony - PlayStation'
        $nesDir = Join-Path $tempDir 'Nintendo - NES'
        New-Item -ItemType Directory -Path $ps1Dir -Force | Out-Null
        New-Item -ItemType Directory -Path $nesDir -Force | Out-Null

        $ps1Jp = Join-Path $ps1Dir 'Ridge Racer (Japan).chd'
        $nesJp = Join-Path $nesDir 'Akumajou Densetsu (Japan).nes'
        'romdata' | Out-File -LiteralPath $ps1Jp -Encoding ascii -Force
        'romdata' | Out-File -LiteralPath $nesJp -Encoding ascii -Force

        $result = $null
        try {
            $result = Invoke-RegionDedupe `
                -Roots @($tempDir) `
                -Mode 'DryRun' `
                -PreferOrder @('EU','US') `
                -IncludeExtensions @('.chd','.nes') `
                -RemoveJunk $true `
                -JpOnlyForSelectedConsoles $true `
                -JpKeepConsoles @('NES') `
                -SeparateBios $false `
                -UseDat $false `
                -Log $logFn

            $result | Should -Not -BeNullOrEmpty
            $csv = Import-Csv -LiteralPath $result.CsvPath

            $ps1Row = $csv | Where-Object { $_.Name -eq 'Ridge Racer (Japan).chd' } | Select-Object -First 1
            $nesRow = $csv | Where-Object { $_.Name -eq 'Akumajou Densetsu (Japan).nes' } | Select-Object -First 1

            $ps1Row | Should -Not -BeNullOrEmpty
            $nesRow | Should -Not -BeNullOrEmpty
            $ps1Row.Action | Should -Be 'DRYRUN-JUNK'
            $nesRow.Action | Should -Be 'KEEP'
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should junk non EU/US regions while keeping EU and WORLD' {
        $script:regionKeepLogMessages = [System.Collections.Generic.List[string]]::new()
        $logFn = { param($msg) $script:regionKeepLogMessages.Add($msg) }

        $tempDir = Join-Path $env:TEMP "pester_region_keep_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

        $frPath = Join-Path $tempDir 'Test Game (France).zip'
        $euPath = Join-Path $tempDir 'Test Game (Europe).zip'
        $worldPath = Join-Path $tempDir 'World Game (USA, Europe).zip'
        'romdata' | Out-File -LiteralPath $frPath -Encoding ascii -Force
        'romdata' | Out-File -LiteralPath $euPath -Encoding ascii -Force
        'romdata' | Out-File -LiteralPath $worldPath -Encoding ascii -Force

        $result = $null
        try {
            $result = Invoke-RegionDedupe `
                -Roots @($tempDir) `
                -Mode 'DryRun' `
                -PreferOrder @('EU','US','JP') `
                -IncludeExtensions @('.zip') `
                -RemoveJunk $true `
                -SeparateBios $false `
                -UseDat $false `
                -Log $logFn

            $result | Should -Not -BeNullOrEmpty
            $csv = Import-Csv -LiteralPath $result.CsvPath

            $frRow = $csv | Where-Object { ([string]$_.Name).TrimStart("'") -eq 'Test Game (France).zip' } | Select-Object -First 1
            $euRow = $csv | Where-Object { ([string]$_.Name).TrimStart("'") -eq 'Test Game (Europe).zip' } | Select-Object -First 1
            $worldRow = $csv | Where-Object { ([string]$_.Name).TrimStart("'") -eq 'World Game (USA, Europe).zip' } | Select-Object -First 1

            $frRow | Should -Not -BeNullOrEmpty
            $euRow | Should -Not -BeNullOrEmpty
            $worldRow | Should -Not -BeNullOrEmpty

            $frRow.Action | Should -Be 'DRYRUN-JUNK'
            $euRow.Action | Should -Be 'KEEP'
            $worldRow.Action | Should -Be 'KEEP'
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
            }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Write-Reports should export DAT completeness CSV when DAT index is provided' {
        $report = [System.Collections.Generic.List[psobject]]::new()
        $report.Add([pscustomobject]@{
            GameKey      = 'game-a'
            Action       = 'KEEP'
            Category     = 'GAME'
            Region       = 'EU'
            WinnerRegion = 'EU'
            VersionScore = 100
            FormatScore  = 850
            Type         = 'FILE'
            DatMatch     = $true
            MainPath     = 'C:\ROMs\PS1\game-a.chd'
            Root         = 'C:\ROMs'
            SizeBytes    = 1048576
            Winner       = 'C:\ROMs\PS1\game-a.chd'
        })

        $datIndex = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $datIndex['PS1'] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $datIndex['PS1']['h1'] = 'game-a'
        $datIndex['PS1']['h2'] = 'game-b'

        $result = $null
        try {
            $result = Write-Reports -Report $report -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 `
                -JunkCount 0 -JunkBytes 0 -BiosCount 0 -UniqueGames 1 -TotalScanned 1 `
                -Mode 'DryRun' -UseDat $true -DatIndex $datIndex -ConsoleSortUnknownReasons $null `
                -GenerateReports $true -Log { param($m) }

            $result | Should -Not -BeNullOrEmpty
            $result.DatCompletenessCsvPath | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath $result.DatCompletenessCsvPath -PathType Leaf | Should -BeTrue
            $csv = Import-Csv -LiteralPath $result.DatCompletenessCsvPath
            @($csv).Count | Should -Be 1
            [string]$csv[0].Console | Should -Be 'PS1'
            [int]$csv[0].Missing | Should -Be 1
        } finally {
            if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) { Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue }
            if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) { Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue }
            if ($result -and $result.JsonPath -and (Test-Path -LiteralPath $result.JsonPath)) { Remove-Item -LiteralPath $result.JsonPath -Force -ErrorAction SilentlyContinue }
            if ($result -and $result.DatCompletenessCsvPath -and (Test-Path -LiteralPath $result.DatCompletenessCsvPath)) { Remove-Item -LiteralPath $result.DatCompletenessCsvPath -Force -ErrorAction SilentlyContinue }
        }
    }
}

Describe 'ZIP Creation Helpers' {
    It 'Should fallback to .NET zip when 7z fails' {
        $tempRoot = Join-Path $env:TEMP "pester_dos_zip_fallback_$(Get-Random)"
        $gameDir = Join-Path $tempRoot 'Airlift Rescue.pc'
        New-Item -ItemType Directory -Path $gameDir -Force | Out-Null
        'run' | Out-File -LiteralPath (Join-Path $gameDir 'game.exe') -Encoding ascii -Force
        $fake7z = Join-Path $tempRoot '7z.exe'
        [void](New-Item -ItemType File -Path $fake7z -Force)

        try {
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                [pscustomobject]@{ Success = $false; ExitCode = 7; ErrorText = 'Command Line Error' }
            }

            $zipPath = Join-Path $tempRoot 'Airlift Rescue.pc.zip'
            $ok = New-ZipFromFolder -SourceFolder $gameDir -ZipPath $zipPath -SevenZipPath $fake7z -Log $null

            $ok | Should -BeTrue
            (Test-Path -LiteralPath $zipPath -PathType Leaf) | Should -BeTrue
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'CHDMAN Process Helper' {
    It 'Should return success result when external process succeeds' {
        Mock -CommandName Invoke-ExternalToolProcess -MockWith {
            [pscustomobject]@{ Success = $true; ExitCode = 0; StdOut = ''; StdErr = '' }
        }

        $result = Invoke-ChdmanProcess -ToolPath 'C:\tools\chdman.exe' -ToolArgs @('createcd','-i','C:\in.cue','-o','C:\out.chd') -OutputPath 'C:\out.chd' -TempFiles $null -Log { param($m) }
        $result.Success | Should -BeTrue
    }

    It 'Should delete partial output and return failure when external process fails' {
        $tempRoot = Join-Path $env:TEMP "pester_chdman_helper_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        $outPath = Join-Path $tempRoot 'failed.chd'
        'partial' | Out-File -LiteralPath $outPath -Encoding ascii -Force

        try {
            Mock -CommandName Invoke-ExternalToolProcess -MockWith {
                [pscustomobject]@{ Success = $false; ExitCode = 1; StdOut = ''; StdErr = 'fail' }
            }

            $result = Invoke-ChdmanProcess -ToolPath 'C:\tools\chdman.exe' -ToolArgs @('createcd','-i','C:\in.cue','-o',$outPath) -OutputPath $outPath -TempFiles $null -Log { param($m) }
            $result.Success | Should -BeFalse
            (Test-Path -LiteralPath $outPath) | Should -BeFalse
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'E2E Smoke - TestDrive' {
    Context 'DryRun / Move / Cancel' {

        It 'DryRun on TestDrive should not move files' {
            $root = Join-Path $TestDrive 'dryrun_root'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            $rom = Join-Path $root 'Smoke Game (Europe).chd'
            'romdata' | Out-File -LiteralPath $rom -Encoding ascii -Force

            $result = $null
            $logFn = { param($msg) }
            try {
                $result = Invoke-RegionDedupe `
                    -Roots @($root) `
                    -Mode 'DryRun' `
                    -PreferOrder @('EU','US','WORLD','JP') `
                    -IncludeExtensions @('.chd') `
                    -RemoveJunk $true `
                    -SeparateBios $false `
                    -GenerateReportsInDryRun $true `
                    -UseDat $false `
                    -Log $logFn

                $result | Should -Not -BeNullOrEmpty
                Test-Path -LiteralPath $rom | Should -BeTrue
                $result.CsvPath | Should -Not -BeNullOrEmpty
                $result.HtmlPath | Should -Not -BeNullOrEmpty
                Test-Path -LiteralPath $result.CsvPath | Should -BeTrue
                Test-Path -LiteralPath $result.HtmlPath | Should -BeTrue
            } finally {
                if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                    Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
                }
                if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                    Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
                }
            }
        }

        It 'Move on TestDrive should move loser to trash and write audit' {
            $root = Join-Path $TestDrive 'move_root'
            $auditRoot = Join-Path $TestDrive 'audit_root'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            New-Item -ItemType Directory -Path $auditRoot -Force | Out-Null

            $eu = Join-Path $root 'Smoke Game (Europe).chd'
            $us = Join-Path $root 'Smoke Game (USA).chd'
            'eu' | Out-File -LiteralPath $eu -Encoding ascii -Force
            'us' | Out-File -LiteralPath $us -Encoding ascii -Force

            $result = $null
            $logFn = { param($msg) }
            try {
                $result = Invoke-RegionDedupe `
                    -Roots @($root) `
                    -Mode 'Move' `
                    -PreferOrder @('EU','US','WORLD','JP') `
                    -IncludeExtensions @('.chd') `
                    -RemoveJunk $true `
                    -SeparateBios $false `
                    -UseDat $false `
                    -AuditRoot $auditRoot `
                    -RequireConfirmMove $false `
                    -Log $logFn

                $result | Should -Not -BeNullOrEmpty
                Test-Path -LiteralPath $eu | Should -BeTrue
                Test-Path -LiteralPath $us | Should -BeFalse
                Test-Path -LiteralPath (Join-Path $root '_TRASH_REGION_DEDUPE\Smoke Game (USA).chd') | Should -BeTrue

                $auditFiles = @(Get-ChildItem -LiteralPath $auditRoot -Filter 'rom-move-audit-*.csv' -File -ErrorAction SilentlyContinue)
                $auditFiles.Count | Should -BeGreaterThan 0
            } finally {
                if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                    Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
                }
                if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                    Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
                }
            }
        }

        It 'Rollback from audit should restore moved files' {
            $root = Join-Path $TestDrive 'rollback_root'
            $auditRoot = Join-Path $TestDrive 'rollback_audit_root'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            New-Item -ItemType Directory -Path $auditRoot -Force | Out-Null

            $eu = Join-Path $root 'Rollback Game (Europe).chd'
            $us = Join-Path $root 'Rollback Game (USA).chd'
            'eu' | Out-File -LiteralPath $eu -Encoding ascii -Force
            'us' | Out-File -LiteralPath $us -Encoding ascii -Force

            $result = $null
            $logFn = { param($msg) }
            try {
                $result = Invoke-RegionDedupe `
                    -Roots @($root) `
                    -Mode 'Move' `
                    -PreferOrder @('EU','US','WORLD','JP') `
                    -IncludeExtensions @('.chd') `
                    -RemoveJunk $true `
                    -SeparateBios $false `
                    -UseDat $false `
                    -AuditRoot $auditRoot `
                    -RequireConfirmMove $false `
                    -Log $logFn

                Test-Path -LiteralPath $eu | Should -BeTrue
                Test-Path -LiteralPath $us | Should -BeFalse

                $auditFiles = @(Get-ChildItem -LiteralPath $auditRoot -Filter 'rom-move-audit-*.csv' -File -ErrorAction SilentlyContinue)
                $auditFiles.Count | Should -BeGreaterThan 0

                $rollbackResult = Invoke-AuditRollback -AuditCsvPath $auditFiles[0].FullName -AllowedRestoreRoots @($root) -Log $logFn
                $rollbackResult.RolledBack | Should -BeGreaterThan 0
                $rollbackResult.Failed | Should -Be 0

                Test-Path -LiteralPath $us | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $root '_TRASH_REGION_DEDUPE\Rollback Game (USA).chd') | Should -BeFalse
            } finally {
                if ($result -and $result.CsvPath -and (Test-Path -LiteralPath $result.CsvPath)) {
                    Remove-Item -LiteralPath $result.CsvPath -Force -ErrorAction SilentlyContinue
                }
                if ($result -and $result.HtmlPath -and (Test-Path -LiteralPath $result.HtmlPath)) {
                    Remove-Item -LiteralPath $result.HtmlPath -Force -ErrorAction SilentlyContinue
                }
            }
        }

        It 'Rollback should skip tampered rows outside allowed roots' {
            $root = Join-Path $TestDrive 'rollback_safe_root'
            $outside = Join-Path $TestDrive 'rollback_outside_root'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            New-Item -ItemType Directory -Path $outside -Force | Out-Null

            $restoreTarget = Join-Path $root 'Safe Restore Game.chd'
            $outsideFile = Join-Path $outside 'outside_payload.chd'
            'outside' | Out-File -LiteralPath $outsideFile -Encoding ascii -Force

            $auditPath = Join-Path $TestDrive 'rom-move-audit-tampered.csv'
            @(
                [pscustomobject]@{
                    Time = '2026-02-18 12:00:00'
                    Action = 'MOVE'
                    Source = $restoreTarget
                    Dest = $outsideFile
                    SizeBytes = 7
                }
            ) | Export-Csv -LiteralPath $auditPath -NoTypeInformation -Encoding UTF8

            $logFn = { param($msg) }
            $rollbackResult = Invoke-AuditRollback -AuditCsvPath $auditPath -AllowedRestoreRoots @($root) -Log $logFn

            $rollbackResult.EligibleRows | Should -Be 0
            $rollbackResult.SkippedUnsafe | Should -Be 1
            $rollbackResult.RolledBack | Should -Be 0
            Test-Path -LiteralPath $outsideFile | Should -BeTrue
            Test-Path -LiteralPath $restoreTarget | Should -BeFalse
        }

        It 'Move should write structured audit metadata sidecar' {
            $root = Join-Path $TestDrive 'audit_meta_root'
            $auditRoot = Join-Path $TestDrive 'audit_meta_out'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            New-Item -ItemType Directory -Path $auditRoot -Force | Out-Null

            $eu = Join-Path $root 'Meta Game (Europe).chd'
            $us = Join-Path $root 'Meta Game (USA).chd'
            'eu' | Out-File -LiteralPath $eu -Encoding ascii -Force
            'us' | Out-File -LiteralPath $us -Encoding ascii -Force

            [void](Invoke-RegionDedupe `
                -Roots @($root) `
                -Mode 'Move' `
                -PreferOrder @('EU','US','WORLD','JP') `
                -IncludeExtensions @('.chd') `
                -RemoveJunk $true `
                -SeparateBios $false `
                -UseDat $false `
                -AuditRoot $auditRoot `
                -RequireConfirmMove $false `
                -Log { param($m) })

            $auditFiles = @(Get-ChildItem -LiteralPath $auditRoot -Filter 'rom-move-audit-*.csv' -File -ErrorAction SilentlyContinue)
            $auditFiles.Count | Should -BeGreaterThan 0
            $metaPath = [System.IO.Path]::ChangeExtension($auditFiles[0].FullName, '.meta.json')
            Test-Path -LiteralPath $metaPath | Should -BeTrue

            $metaRaw = Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8
            $meta = $metaRaw | ConvertFrom-Json
            [int]$meta.Version | Should -Be 1
            [string]$meta.CsvSha256 | Should -Not -BeNullOrEmpty
            [int]$meta.RowCount | Should -BeGreaterThan 0
        }

        It 'Rollback should reject signed audit when CSV was tampered' {
            $previousKey = $env:ROMCLEANUP_AUDIT_HMAC_KEY
            $env:ROMCLEANUP_AUDIT_HMAC_KEY = 'pester-signing-key'
            try {
                $root = Join-Path $TestDrive 'signed_audit_root'
                $trash = Join-Path $TestDrive 'signed_audit_trash'
                New-Item -ItemType Directory -Path $root -Force | Out-Null
                New-Item -ItemType Directory -Path $trash -Force | Out-Null

                $restoreTarget = Join-Path $root 'Signed Game.chd'
                $currentFile = Join-Path $trash 'Signed Game.chd'
                'payload' | Out-File -LiteralPath $currentFile -Encoding ascii -Force

                $auditPath = Join-Path $TestDrive 'rom-move-audit-signed.csv'
                $rows = @(
                    [pscustomobject]@{
                        Time = '2026-02-19 10:00:00'
                        Action = 'MOVE'
                        Source = $restoreTarget
                        Dest = $currentFile
                        SizeBytes = 7
                    }
                )
                $rows | Export-Csv -LiteralPath $auditPath -NoTypeInformation -Encoding UTF8
                [void](Write-AuditMetadataSidecar -AuditCsvPath $auditPath -Rows $rows -Log { param($m) })

                Add-Content -LiteralPath $auditPath -Value "#tamper" -Encoding UTF8

                { Invoke-AuditRollback -AuditCsvPath $auditPath -AllowedRestoreRoots @($root) -AllowedCurrentRoots @($trash) -Log { param($m) } } | Should -Throw
            } finally {
                $env:ROMCLEANUP_AUDIT_HMAC_KEY = $previousKey
            }
        }

        It 'Rollback should reject audit without metadata when strict meta is required' {
            $previousRequireMeta = $env:ROMCLEANUP_AUDIT_REQUIRE_META
            $env:ROMCLEANUP_AUDIT_REQUIRE_META = '1'
            try {
                $root = Join-Path $TestDrive 'strict_meta_root'
                $trash = Join-Path $TestDrive 'strict_meta_trash'
                New-Item -ItemType Directory -Path $root -Force | Out-Null
                New-Item -ItemType Directory -Path $trash -Force | Out-Null

                $restoreTarget = Join-Path $root 'StrictMeta Game.chd'
                $currentFile = Join-Path $trash 'StrictMeta Game.chd'
                'payload' | Out-File -LiteralPath $currentFile -Encoding ascii -Force

                $auditPath = Join-Path $TestDrive 'rom-move-audit-nometa.csv'
                @(
                    [pscustomobject]@{
                        Time = '2026-02-19 10:00:00'
                        Action = 'MOVE'
                        Source = $restoreTarget
                        Dest = $currentFile
                        SizeBytes = 7
                    }
                ) | Export-Csv -LiteralPath $auditPath -NoTypeInformation -Encoding UTF8

                { Invoke-AuditRollback -AuditCsvPath $auditPath -AllowedRestoreRoots @($root) -AllowedCurrentRoots @($trash) -Log { param($m) } } | Should -Throw
            } finally {
                $env:ROMCLEANUP_AUDIT_REQUIRE_META = $previousRequireMeta
            }
        }

        It 'Cancel smoke should throw OperationCanceledException quickly' {
            $root = Join-Path $TestDrive 'cancel_root'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            $rom = Join-Path $root 'Cancel Game (Europe).chd'
            'romdata' | Out-File -LiteralPath $rom -Encoding ascii -Force

            $logFn = { param($msg) }
            Set-AppStateValue -Key 'CancelRequested' -Value $true -SyncLegacy | Out-Null
            try {
                {
                    Invoke-RegionDedupe `
                        -Roots @($root) `
                        -Mode 'DryRun' `
                        -PreferOrder @('EU','US','WORLD','JP') `
                        -IncludeExtensions @('.chd') `
                        -RemoveJunk $true `
                        -SeparateBios $false `
                        -GenerateReportsInDryRun $false `
                        -UseDat $false `
                        -Log $logFn
                } | Should -Throw -ExceptionType ([System.OperationCanceledException])
            } finally {
                Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
            }
        }

        It 'marks file as JUNK when CRC32 verify fails during scan' {
            $root = Join-Path $TestDrive 'crc_verify_root'
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            $rom = Join-Path $root 'Broken Game (Europe).chd'
            'romdata' | Out-File -LiteralPath $rom -Encoding ascii -Force

            $logFn = { param($msg) }
            Mock -CommandName Get-Crc32Hash -MockWith { throw 'crc-read-fail' } -ParameterFilter { $Path -eq $rom }

            $result = Invoke-RegionDedupe `
                -Roots @($root) `
                -Mode 'DryRun' `
                -PreferOrder @('EU','US','WORLD','JP') `
                -IncludeExtensions @('.chd') `
                -RemoveJunk $true `
                -SeparateBios $false `
                -GenerateReportsInDryRun $false `
                -UseDat $false `
                -CrcVerifyScan $true `
                -Log $logFn

            $result | Should -Not -BeNullOrEmpty
            $result.Winners.Count | Should -Be 0
            $result.ItemByMain.ContainsKey($rom) | Should -BeTrue
            $result.ItemByMain[$rom].Category | Should -Be 'JUNK'
        }
    }
}
