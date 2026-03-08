#requires -Modules Pester

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    . (Join-Path $PSScriptRoot 'FixtureFactory.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'e2e_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'E2E Smoke - Region Dedupe Flow' {
    It 'Fixture generator should build reusable CUE/GDI/CCD/M3U/Archive/Junk/Bios dataset' {
        $root = Join-Path $TestDrive 'e2e_fixture_factory_root'
        $fixture = New-E2ESmokeFixtureSet -Root $root

        $fixture | Should -Not -BeNullOrEmpty
        Test-Path -LiteralPath $fixture.Paths.Cue | Should -BeTrue
        Test-Path -LiteralPath $fixture.Paths.Gdi | Should -BeTrue
        Test-Path -LiteralPath $fixture.Paths.Ccd | Should -BeTrue
        Test-Path -LiteralPath $fixture.Paths.M3u | Should -BeTrue
        Test-Path -LiteralPath $fixture.Paths.ArchiveZip | Should -BeTrue
        Test-Path -LiteralPath $fixture.Paths.Junk | Should -BeTrue
        Test-Path -LiteralPath $fixture.Paths.Bios | Should -BeTrue

        @(Get-CueRelatedFiles -CuePath $fixture.Paths.Cue -RootPath $root).Count | Should -Be 3
        @(Get-GdiRelatedFiles -GdiPath $fixture.Paths.Gdi -RootPath $root).Count | Should -Be 3
        @(Get-CcdRelatedFiles -CcdPath $fixture.Paths.Ccd -RootPath $root).Count | Should -Be 3
        @(Get-M3URelatedFiles -M3UPath $fixture.Paths.M3u -RootPath $root).Count | Should -BeGreaterThan 5

        Get-FileCategory -BaseName ([System.IO.Path]::GetFileNameWithoutExtension($fixture.Paths.Junk)) | Should -Be 'JUNK'
        Get-FileCategory -BaseName ([System.IO.Path]::GetFileNameWithoutExtension($fixture.Paths.Bios)) | Should -Be 'BIOS'
    }

    It 'DryRun should select preferred region winner and mark duplicate for move' {
        $root = Join-Path $TestDrive 'e2e_dryrun_root'
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        $eu = Join-Path $root 'Mega Game (Europe).chd'
        $us = Join-Path $root 'Mega Game (USA).chd'
        'eu-rom' | Out-File -LiteralPath $eu -Encoding ascii -Force
        'us-rom' | Out-File -LiteralPath $us -Encoding ascii -Force

        $result = Invoke-RegionDedupe `
            -Roots @($root) `
            -Mode 'DryRun' `
            -PreferOrder @('EU','US','WORLD','JP') `
            -IncludeExtensions @('.chd') `
            -RemoveJunk $true `
            -SeparateBios $false `
            -GenerateReportsInDryRun $false `
            -UseDat $false `
            -Log { param($m) }

        $result | Should -Not -BeNullOrEmpty
        @($result.Winners).Count | Should -Be 1
        $result.Winners[0].MainPath | Should -Be $eu
        @($result.AllItems).Count | Should -Be 2
        Test-Path -LiteralPath $eu | Should -BeTrue
        Test-Path -LiteralPath $us | Should -BeTrue
    }

    It 'Move + Rollback should restore original location from audit log' {
        $root = Join-Path $TestDrive 'e2e_move_root'
        $trash = Join-Path $TestDrive 'e2e_move_trash'
        $audit = Join-Path $TestDrive 'e2e_move_audit'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        New-Item -ItemType Directory -Path $trash -Force | Out-Null
        New-Item -ItemType Directory -Path $audit -Force | Out-Null

        $eu = Join-Path $root 'Rollback Game (Europe).chd'
        $us = Join-Path $root 'Rollback Game (USA).chd'
        'eu-rom' | Out-File -LiteralPath $eu -Encoding ascii -Force
        'us-rom' | Out-File -LiteralPath $us -Encoding ascii -Force

        $result = Invoke-RegionDedupe `
            -Roots @($root) `
            -Mode 'Move' `
            -PreferOrder @('EU','US','WORLD','JP') `
            -TrashRoot $trash `
            -AuditRoot $audit `
            -IncludeExtensions @('.chd') `
            -RemoveJunk $true `
            -SeparateBios $false `
            -GenerateReportsInDryRun $false `
            -UseDat $false `
            -Log { param($m) }

        $result | Should -Not -BeNullOrEmpty
        Test-Path -LiteralPath $eu | Should -BeTrue
        Test-Path -LiteralPath $us | Should -BeFalse

        $auditCsv = Get-ChildItem -LiteralPath $audit -Filter 'rom-move-audit-*.csv' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        $auditCsv | Should -Not -BeNullOrEmpty

        Invoke-AuditRollback -AuditCsvPath $auditCsv.FullName -AllowedRestoreRoots @($root) -AllowedCurrentRoots @($trash) -Log { param($m) } | Out-Null

        Test-Path -LiteralPath $us | Should -BeTrue
    }

    It 'Rollback DryRun should plan restore without mutating filesystem' {
        $root = Join-Path $TestDrive 'e2e_rollback_dryrun_root'
        $trash = Join-Path $TestDrive 'e2e_rollback_dryrun_trash'
        $audit = Join-Path $TestDrive 'e2e_rollback_dryrun_audit'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        New-Item -ItemType Directory -Path $trash -Force | Out-Null
        New-Item -ItemType Directory -Path $audit -Force | Out-Null

        $eu = Join-Path $root 'DryRun Rollback Game (Europe).chd'
        $us = Join-Path $root 'DryRun Rollback Game (USA).chd'
        'eu-rom' | Out-File -LiteralPath $eu -Encoding ascii -Force
        'us-rom' | Out-File -LiteralPath $us -Encoding ascii -Force

        $null = Invoke-RegionDedupe `
            -Roots @($root) `
            -Mode 'Move' `
            -PreferOrder @('EU','US','WORLD','JP') `
            -TrashRoot $trash `
            -AuditRoot $audit `
            -IncludeExtensions @('.chd') `
            -RemoveJunk $true `
            -SeparateBios $false `
            -GenerateReportsInDryRun $false `
            -UseDat $false `
            -Log { param($m) }

        $auditCsv = Get-ChildItem -LiteralPath $audit -Filter 'rom-move-audit-*.csv' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        $auditCsv | Should -Not -BeNullOrEmpty

        $rollback = Invoke-AuditRollback -AuditCsvPath $auditCsv.FullName -AllowedRestoreRoots @($root) -AllowedCurrentRoots @($trash) -DryRun -Log { param($m) }

        $rollback | Should -Not -BeNullOrEmpty
        [int]$rollback.DryRunPlanned | Should -BeGreaterThan 0
        [int]$rollback.RolledBack | Should -Be 0

        Test-Path -LiteralPath $eu | Should -BeTrue
        Test-Path -LiteralPath $us | Should -BeFalse
    }

    It 'Convert-only preview + run should honor allowlist and avoid changes outside allowed roots' {
        $rootAllowed = Join-Path $TestDrive 'e2e_convert_allowed'
        $rootBlocked = Join-Path $TestDrive 'e2e_convert_blocked'
        New-Item -ItemType Directory -Path $rootAllowed -Force | Out-Null
        New-Item -ItemType Directory -Path $rootBlocked -Force | Out-Null

        $allowedIso = Join-Path $rootAllowed 'Allowed Game.iso'
        $blockedIso = Join-Path $rootBlocked 'Blocked Game.iso'
        'allowed' | Out-File -LiteralPath $allowedIso -Encoding ascii -Force
        'blocked' | Out-File -LiteralPath $blockedIso -Encoding ascii -Force

        $blockedMarker = Join-Path $rootBlocked 'outside.marker'
        'baseline' | Out-File -LiteralPath $blockedMarker -Encoding ascii -Force

        $preview = Get-StandaloneConversionPreview -Roots @($rootAllowed, $rootBlocked) -AllowedRoots @($rootAllowed) -PreviewLimit 10 -Log { param($m) }
        $preview | Should -Not -BeNullOrEmpty
        [int]$preview.CandidateCount | Should -Be 1
        @($preview.BlockedRoots).Count | Should -Be 1

        $script:convertedRoots = @()
        Mock -CommandName Invoke-FormatConversion -MockWith {
            param($Winners)
            $script:convertedRoots = @($Winners | ForEach-Object { [string]$_.Root })
            foreach ($winner in @($Winners)) {
                $marker = Join-Path ([string]$winner.Root) 'convert-only.marker'
                'converted' | Out-File -LiteralPath $marker -Encoding ascii -Force
            }
        }

        Invoke-StandaloneConversion -Roots @($rootAllowed, $rootBlocked) -AllowedRoots @($rootAllowed) -PreScannedFilesByRoot $preview.ScannedFilesByRoot -ToolOverrides @{} -Log { param($m) } -SetQuickPhase $null -OnProgress $null

        @($script:convertedRoots).Count | Should -Be 1
        @($script:convertedRoots | Select-Object -Unique) | Should -Be @($rootAllowed)

        Test-Path -LiteralPath (Join-Path $rootAllowed 'convert-only.marker') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $rootBlocked 'convert-only.marker') | Should -BeFalse
        Get-Content -LiteralPath $blockedMarker -Raw | Should -Be "baseline`r`n"
    }

    It 'No temp leaks: conversion should cleanup temporary extraction directory' {
        $root = Join-Path $TestDrive 'e2e_temp_cleanup_root'
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        $sourceArchive = Join-Path $root 'Temp Cleanup Game.zip'
        'archive-payload' | Out-File -LiteralPath $sourceArchive -Encoding ascii -Force

        $tempExtractDir = Join-Path $env:TEMP ('rom_dedupe_extract_e2e_' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempExtractDir -Force | Out-Null
        $tempDisc = Join-Path $tempExtractDir 'game.iso'
        'iso-payload' | Out-File -LiteralPath $tempDisc -Encoding ascii -Force

        Mock -CommandName Expand-ArchiveToTemp -MockWith { $tempExtractDir }
        Mock -CommandName Find-DiscImageInDir -MockWith { @(Get-Item -LiteralPath $tempDisc) }
        Mock -CommandName Invoke-ExternalToolProcess -MockWith {
            param($ToolPath, $ToolArgs)
            $targetIndex = [Array]::IndexOf($ToolArgs, '-o')
            if ($targetIndex -ge 0 -and ($targetIndex + 1) -lt $ToolArgs.Count) {
                $targetPath = [string]$ToolArgs[$targetIndex + 1]
                $targetPath = $targetPath.Trim('"')
                'converted' | Out-File -LiteralPath $targetPath -Encoding ascii -Force
            }
            return [pscustomobject]@{ Success = $true; ExitCode = 0; ErrorText = $null }
        }
        Mock -CommandName Test-ConvertedOutputVerified -MockWith { [pscustomobject]@{ Success = $true; Reason = $null } }

        $item = [pscustomobject]@{
            MainPath = $sourceArchive
            Root = $root
            Paths = @($sourceArchive)
            Type = 'FILE'
            SizeBytes = 1
        }
        $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

        $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -SevenZipPath 'C:\tools\7z.exe' -Log { param($m) }

        $result | Should -Not -BeNullOrEmpty
        $result.Status | Should -Be 'OK'
        Test-Path -LiteralPath $tempExtractDir | Should -BeFalse
    }

    It 'Determinism: repeated DryRun should produce identical winner set' {
        $root = Join-Path $TestDrive 'e2e_determinism_root'
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        'eu-a' | Out-File -LiteralPath (Join-Path $root 'Determinism Game A (Europe).chd') -Encoding ascii -Force
        'us-a' | Out-File -LiteralPath (Join-Path $root 'Determinism Game A (USA).chd') -Encoding ascii -Force
        'eu-b' | Out-File -LiteralPath (Join-Path $root 'Determinism Game B (Europe).chd') -Encoding ascii -Force
        'jp-b' | Out-File -LiteralPath (Join-Path $root 'Determinism Game B (Japan).chd') -Encoding ascii -Force

        $run1 = Invoke-RegionDedupe `
            -Roots @($root) `
            -Mode 'DryRun' `
            -PreferOrder @('EU','US','WORLD','JP') `
            -IncludeExtensions @('.chd') `
            -RemoveJunk $true `
            -SeparateBios $false `
            -GenerateReportsInDryRun $false `
            -UseDat $false `
            -Log { param($m) }

        $run2 = Invoke-RegionDedupe `
            -Roots @($root) `
            -Mode 'DryRun' `
            -PreferOrder @('EU','US','WORLD','JP') `
            -IncludeExtensions @('.chd') `
            -RemoveJunk $true `
            -SeparateBios $false `
            -GenerateReportsInDryRun $false `
            -UseDat $false `
            -Log { param($m) }

        $w1 = @($run1.Winners | ForEach-Object { [string]$_.MainPath } | Sort-Object)
        $w2 = @($run2.Winners | ForEach-Object { [string]$_.MainPath } | Sort-Object)

        @($w1).Count | Should -Be 2
        @($w2).Count | Should -Be 2
        ($w1 -join '|') | Should -Be ($w2 -join '|')
    }
}
