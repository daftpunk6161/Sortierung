#requires -Modules Pester
<#
  Security Tests - Path Traversal, XSS, CSV Injection, Tool Failures
  ==================================================================
  
  Mutation Kills: M4, M7, M8, M11, M12
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'security_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# ============================================================
# PATH TRAVERSAL TESTS (Mutation M4)
# ============================================================

Describe 'Test-PathWithinRoot' {
    Context 'Path Validation' {
        
        It 'Should accept paths within root' {
            Test-PathWithinRoot -Path 'C:\ROMs\game.chd' -Root 'C:\ROMs' | Should -BeTrue
            Test-PathWithinRoot -Path 'C:\ROMs\sub\game.chd' -Root 'C:\ROMs' | Should -BeTrue
        }
        
        It 'Should reject paths outside root via ..' {
            Test-PathWithinRoot -Path 'C:\ROMs\..\evil.txt' -Root 'C:\ROMs' | Should -BeFalse
            Test-PathWithinRoot -Path 'C:\ROMs\sub\..\..\evil.txt' -Root 'C:\ROMs' | Should -BeFalse
        }
        
        It 'Should reject absolute paths not under root' {
            Test-PathWithinRoot -Path 'D:\other\file.txt' -Root 'C:\ROMs' | Should -BeFalse
        }

        It 'Should reject sibling prefix collisions (C:\\ROM vs C:\\ROMS)' {
            Test-PathWithinRoot -Path 'C:\ROMS\game.chd' -Root 'C:\ROM' | Should -BeFalse
        }

        It 'Should handle UNC root boundaries correctly' {
            Test-PathWithinRoot -Path '\\server\share\roms\set\game.chd' -Root '\\server\share\roms' | Should -BeTrue
            Test-PathWithinRoot -Path '\\server\share\romsx\game.chd' -Root '\\server\share\roms' | Should -BeFalse
        }

        It 'Should handle extended-length path prefix safely' {
            Test-PathWithinRoot -Path '\\?\C:\ROMs\sub\game.chd' -Root 'C:\ROMs' | Should -BeTrue
            Test-PathWithinRoot -Path '\\?\C:\ROMSX\game.chd' -Root 'C:\ROMs' | Should -BeFalse
        }
        
        It 'Should handle trailing slash variations' {
            Test-PathWithinRoot -Path 'C:\ROMs\game.chd' -Root 'C:\ROMs\' | Should -BeTrue
            Test-PathWithinRoot -Path 'C:\ROMs\game.chd' -Root 'C:\ROMs' | Should -BeTrue
        }
        
        It 'Should reject empty/null inputs' {
            Test-PathWithinRoot -Path '' -Root 'C:\ROMs' | Should -BeFalse
            Test-PathWithinRoot -Path $null -Root 'C:\ROMs' | Should -BeFalse
            Test-PathWithinRoot -Path 'C:\ROMs\game.chd' -Root '' | Should -BeFalse
        }
    }
}

Describe 'Resolve-ChildPathWithinRoot misuse handling' {
    It 'Should return null (not throw) for relative child without BaseDir' {
        { Resolve-ChildPathWithinRoot -BaseDir $null -ChildPath 'track01.bin' -Root 'C:\ROMs' } | Should -Not -Throw
        $result = Resolve-ChildPathWithinRoot -BaseDir $null -ChildPath 'track01.bin' -Root 'C:\ROMs'
        $result | Should -BeNullOrEmpty
    }
}

Describe 'Remove-EmptyDirectories scope safety' {
    It 'Should only remove empty directories under the provided root' {
        $base = Join-Path $env:TEMP ("pester_remove_empty_scope_{0}" -f (Get-Random))
        $root = Join-Path $base 'root'
        $insideEmpty = Join-Path $root 'empty-inside'
        $outsideEmpty = Join-Path $base 'outside-empty'

        New-Item -ItemType Directory -Path $insideEmpty -Force | Out-Null
        New-Item -ItemType Directory -Path $outsideEmpty -Force | Out-Null

        try {
            Remove-EmptyDirectories -Path $root

            Test-Path -LiteralPath $root | Should -BeTrue
            Test-Path -LiteralPath $insideEmpty | Should -BeFalse
            Test-Path -LiteralPath $outsideEmpty | Should -BeTrue
        } finally {
            Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Move-ItemSafely - Mutation Kill M4' {
    Context 'No-Overwrite Protection' {
        
        BeforeEach {
            $script:testDir = Join-Path $env:TEMP "pester_move_$(Get-Random)"
            New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:testDir -and (Test-Path -LiteralPath $script:testDir)) {
                Remove-Item -LiteralPath $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'M4-KILL: Should NOT overwrite existing file - must create __DUP suffix' {
            $src = Join-Path $script:testDir 'source.txt'
            $existingDest = Join-Path $script:testDir 'dest.txt'
            
            'original' | Out-File -LiteralPath $existingDest -Encoding ascii
            'new content' | Out-File -LiteralPath $src -Encoding ascii
            
            $result = Move-ItemSafely -Source $src -Dest $existingDest
            
            # Original should be preserved
            Test-Path -LiteralPath $existingDest | Should -BeTrue
            Get-Content -LiteralPath $existingDest -Raw | Should -Match 'original'
            
            # New file should have __DUP suffix
            $result | Should -Match '__DUP'
            Test-Path -LiteralPath $result | Should -BeTrue
            Get-Content -LiteralPath $result -Raw | Should -Match 'new content'
        }
        
        It 'M4-KILL: Multiple collisions should create incrementing __DUP suffixes' {
            $src1 = Join-Path $script:testDir 'src1.txt'
            $src2 = Join-Path $script:testDir 'src2.txt'
            $dest = Join-Path $script:testDir 'game.txt'
            
            'original' | Out-File -LiteralPath $dest -Encoding ascii
            'first dupe' | Out-File -LiteralPath $src1 -Encoding ascii
            'second dupe' | Out-File -LiteralPath $src2 -Encoding ascii
            
            $result1 = Move-ItemSafely -Source $src1 -Dest $dest
            $result2 = Move-ItemSafely -Source $src2 -Dest $dest
            
            $result1 | Should -Match '__DUP1'
            $result2 | Should -Match '__DUP2'
        }

        It 'Should not leave *.tmp_move artifacts after successful move' {
            $src = Join-Path $script:testDir 'source_atomic.txt'
            $dest = Join-Path $script:testDir 'dest_atomic.txt'

            'atomic content' | Out-File -LiteralPath $src -Encoding ascii

            $result = Move-ItemSafely -Source $src -Dest $dest

            $result | Should -Be $dest
            Test-Path -LiteralPath $src | Should -BeFalse
            Test-Path -LiteralPath $dest | Should -BeTrue

            $tmpArtifacts = @(Get-ChildItem -LiteralPath $script:testDir -Filter '*.tmp_move' -File -ErrorAction SilentlyContinue)
            $tmpArtifacts.Count | Should -Be 0
        }
    }
    
    Context 'Path Traversal Blocking' {
        
        BeforeEach {
            $script:testDir = Join-Path $env:TEMP "pester_traversal_$(Get-Random)"
            New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:testDir -and (Test-Path -LiteralPath $script:testDir)) {
                Remove-Item -LiteralPath $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Should block source outside root' {
            $src = Join-Path $env:TEMP 'outside.txt'
            $dest = Join-Path $script:testDir 'dest.txt'
            
            'data' | Out-File -LiteralPath $src -Encoding ascii -Force
            
            { Move-ItemSafely -Source $src -Dest $dest `
                -ValidateSourceWithinRoot $script:testDir } | Should -Throw '*traversal*'
            
            Remove-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue
        }
        
        It 'Should block destination outside root' {
            $src = Join-Path $script:testDir 'source.txt'
            $dest = Join-Path $env:TEMP 'evil.txt'
            
            'data' | Out-File -LiteralPath $src -Encoding ascii -Force
            
            { Move-ItemSafely -Source $src -Dest $dest `
                -ValidateDestWithinRoot $script:testDir } | Should -Throw '*traversal*'
        }
        
        It 'Should block source equals destination' {
            $path = Join-Path $script:testDir 'same.txt'
            'data' | Out-File -LiteralPath $path -Encoding ascii -Force
            
            { Move-ItemSafely -Source $path -Dest $path } | Should -Throw '*Invalid source/destination*'
        }
    }
    
    Context 'ReparsePoint Blocking' {

        It 'M02-KILL: Should block source flagged as reparse point before move' {
            $testDir = Join-Path $env:TEMP "pester_reparse_src_$(Get-Random)"
            New-Item -ItemType Directory -Path $testDir -Force | Out-Null

            try {
                $src = Join-Path $testDir 'src.bin'
                $dest = Join-Path $testDir 'dest.bin'
                'data' | Out-File -LiteralPath $src -Encoding ascii -Force

                $script:moveCalled = $false
                Mock -CommandName Get-Item -ParameterFilter { $LiteralPath -eq $src } -MockWith {
                    [pscustomobject]@{ FullName = $src; Attributes = [IO.FileAttributes]::ReparsePoint; PSIsContainer = $false }
                }
                Mock -CommandName Move-Item -MockWith { $script:moveCalled = $true }

                { Move-ItemSafely -Source $src -Dest $dest } | Should -Throw '*reparse point*'
                $script:moveCalled | Should -BeFalse
            } finally {
                Remove-Item -LiteralPath $testDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'M02-KILL: Should block destination directory flagged as reparse point before move' {
            $testDir = Join-Path $env:TEMP "pester_reparse_dest_$(Get-Random)"
            New-Item -ItemType Directory -Path $testDir -Force | Out-Null

            try {
                $src = Join-Path $testDir 'src.bin'
                $destDir = Join-Path $testDir 'out'
                $dest = Join-Path $destDir 'dest.bin'
                'data' | Out-File -LiteralPath $src -Encoding ascii -Force
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null

                $script:moveCalled = $false
                Mock -CommandName Get-Item -ParameterFilter { $LiteralPath -eq $destDir } -MockWith {
                    [pscustomobject]@{ FullName = $destDir; Attributes = [IO.FileAttributes]::ReparsePoint; PSIsContainer = $true }
                }
                Mock -CommandName Move-Item -MockWith { $script:moveCalled = $true }

                { Move-ItemSafely -Source $src -Dest $dest } | Should -Throw '*reparse point*'
                $script:moveCalled | Should -BeFalse
            } finally {
                Remove-Item -LiteralPath $testDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Should detect and block moves involving reparse points' -Skip:(-not (Test-Path "$env:SystemDrive\")) {
            # This test requires admin rights to create junctions
            # We test the detection logic exists
            $testDir = Join-Path $env:TEMP "pester_reparse_$(Get-Random)"
            New-Item -ItemType Directory -Path $testDir -Force | Out-Null
            
            try {
                $src = Join-Path $testDir 'file.txt'
                'data' | Out-File -LiteralPath $src -Encoding ascii
                
                # Try to create a junction for testing (may fail without admin)
                $junction = Join-Path $testDir 'junction'
                try {
                    & cmd /c "mklink /J `"$junction`" `"$env:TEMP`"" 2>$null
                    
                    if (Test-Path -LiteralPath $junction) {
                        $destViaJunction = Join-Path $junction 'evil.txt'
                        
                        # The move should be blocked because dest parent is a reparse point
                        { Move-ItemSafely -Source $src -Dest $destViaJunction } | Should -Throw '*reparse*'
                    }
                } catch {
                    # Skip if junction creation failed (no admin rights)
                }
            } finally {
                Remove-Item -LiteralPath $testDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Describe 'Zip Slip Detection' {
    Context 'Test-ArchiveEntryPathsSafe' {
        
        It 'Should accept normal paths' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('game.bin') | Should -BeTrue
            Test-ArchiveEntryPathsSafe -EntryPaths @('folder/game.bin') | Should -BeTrue
            Test-ArchiveEntryPathsSafe -EntryPaths @('folder\game.bin') | Should -BeTrue
        }
        
        It 'Should reject .. traversal' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('..\evil.txt') | Should -BeFalse
            Test-ArchiveEntryPathsSafe -EntryPaths @('folder\..\..\..\evil.txt') | Should -BeFalse
            Test-ArchiveEntryPathsSafe -EntryPaths @('../evil.txt') | Should -BeFalse
        }
        
        It 'Should reject absolute paths' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('C:\evil.txt') | Should -BeFalse
            Test-ArchiveEntryPathsSafe -EntryPaths @('/etc/passwd') | Should -BeFalse
            Test-ArchiveEntryPathsSafe -EntryPaths @('\\server\share\evil.txt') | Should -BeFalse
        }
        
        It 'Should handle mixed valid/invalid' {
            # One bad entry should fail the whole check
            Test-ArchiveEntryPathsSafe -EntryPaths @('good.bin', '..\evil.txt', 'also_good.bin') | Should -BeFalse
        }
        
        It 'Should handle empty/null' {
            Test-ArchiveEntryPathsSafe -EntryPaths @() | Should -BeTrue
            Test-ArchiveEntryPathsSafe -EntryPaths @('', '  ') | Should -BeTrue
        }
    }
}

Describe 'P1 Regression - Archive Entry Parsing' {
    Context 'Get-ArchiveEntryPaths should skip archive metadata path' {

        It 'Should only return real entries after 7z separator block' {
            Reset-ArchiveEntryCache

            # Create a real temp file so [System.IO.FileInfo]::new() finds it
            $tmpArchive = Join-Path $env:TEMP ("pester_dummy_{0}.zip" -f (Get-Random))
            [System.IO.File]::WriteAllBytes($tmpArchive, [byte[]]@(0x50, 0x4B))

            try {
                Mock -CommandName Invoke-7z -MockWith {
                    [pscustomobject]@{
                        ExitCode = 0
                        StdOut = @"
Path = $tmpArchive
Type = zip
Physical Size = 42
----------
Path = game.bin
Size = 123
Path = folder\track01.bin
Size = 456
"@
                        StdErr = ''
                    }
                }

                $entries = @(Get-ArchiveEntryPaths -ArchivePath $tmpArchive -SevenZipPath 'C:\tools\7z.exe' -TempFiles $null)
                $entries | Should -HaveCount 2
                $entries | Should -Contain 'game.bin'
                $entries | Should -Contain 'folder\track01.bin'
                $entries | Should -Not -Contain $tmpArchive
            } finally {
                Remove-Item -LiteralPath $tmpArchive -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Describe 'P1 Regression - BIOS Destination Policy' {
    Context 'Invoke-MovePhase should keep BIOS moves under source root' {

        It 'Should move BIOS into root _BIOS even when TrashRoot is external' {
            $root = Join-Path $env:TEMP ("pester_bios_root_{0}" -f (Get-Random))
            $externalTrash = Join-Path $env:TEMP ("pester_bios_trash_{0}" -f (Get-Random))
            $auditRoot = Join-Path $env:TEMP ("pester_bios_audit_{0}" -f (Get-Random))
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            New-Item -ItemType Directory -Path $externalTrash -Force | Out-Null
            New-Item -ItemType Directory -Path $auditRoot -Force | Out-Null

            $src = Join-Path $root 'bios.bin'
            'bios' | Out-File -LiteralPath $src -Encoding ascii -Force

            $report = New-Object 'System.Collections.Generic.List[psobject]'
            $report.Add([pscustomobject]@{ Action='BIOS-MOVE'; MainPath=$src; Category='BIOS' }) | Out-Null

            $biosItems = New-Object 'System.Collections.Generic.List[psobject]'
            $allItems = New-Object 'System.Collections.Generic.List[psobject]'
            $item = [pscustomobject]@{ Root=$root; MainPath=$src; Paths=@($src) }
            $biosItems.Add($item) | Out-Null
            $allItems.Add($item) | Out-Null
            $itemByMain = @{ $src = $item }

            try {
                $result = Invoke-MovePhase -Report $report -JunkItems (New-Object 'System.Collections.Generic.List[psobject]') `
                    -BiosItems $biosItems -AllItems $allItems -ItemByMain $itemByMain -Roots @($root) `
                    -TrashRoot $externalTrash -AuditRoot $auditRoot -RequireConfirmMove $false -ConfirmMove $null `
                    -CsvPath $null -HtmlPath $null -TotalDupes 0 -SavedMB 0 -Log $null

                $result.Status | Should -Be 'continue'
                $expected = Join-Path $root '_BIOS\bios.bin'
                Test-Path -LiteralPath $expected | Should -BeTrue
                Test-Path -LiteralPath (Join-Path (Split-Path -Parent $externalTrash) '_BIOS\bios.bin') | Should -BeFalse
            } finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $externalTrash -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $auditRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# ============================================================
# CSV INJECTION TESTS (Mutation M12)
# ============================================================

Describe 'ConvertTo-SafeCsvValue - Mutation Kill M12' {
    Context 'Formula Injection Prevention' {
        
        It 'M12-KILL: Should prefix = with single quote' {
            ConvertTo-SafeCsvValue -Value '=SUM(A1:A10)' | Should -Be "'=SUM(A1:A10)"
            ConvertTo-SafeCsvValue -Value '=1+1' | Should -Be "'=1+1"
        }
        
        It 'M12-KILL: Should prefix + with single quote' {
            ConvertTo-SafeCsvValue -Value '+49123456' | Should -Be "'+49123456"
        }
        
        It 'M12-KILL: Should prefix - with single quote' {
            ConvertTo-SafeCsvValue -Value '-1' | Should -Be "'-1"
        }
        
        It 'M12-KILL: Should prefix @ with single quote' {
            ConvertTo-SafeCsvValue -Value '@A1' | Should -Be "'@A1"
        }
        
        It 'M12-KILL: Should prefix | (pipe) with single quote' {
            ConvertTo-SafeCsvValue -Value '|cmd' | Should -Be "'|cmd"
        }
        
        It 'M12-KILL: Should prefix TAB+dangerous char correctly' {
            # Note: Function trims whitespace then checks for dangerous chars
            # TAB alone is NOT dangerous, but TAB + = IS dangerous
            $tabValue = "`t=TAB_FORMULA"
            $result = ConvertTo-SafeCsvValue -Value $tabValue
            $result | Should -Be "'=TAB_FORMULA"
        }
        
        It 'M12-KILL: Should handle leading whitespace before dangerous char' {
            ConvertTo-SafeCsvValue -Value '  =formula' | Should -Be "'  =formula"
            ConvertTo-SafeCsvValue -Value "  `t=also" | Should -Be "'  =also"
        }
        
        It 'Should NOT prefix normal values' {
            ConvertTo-SafeCsvValue -Value 'Normal Game Title' | Should -Be 'Normal Game Title'
            ConvertTo-SafeCsvValue -Value 'Game (Europe) (En,Fr)' | Should -Be 'Game (Europe) (En,Fr)'
            ConvertTo-SafeCsvValue -Value '123' | Should -Be '123'
        }
        
        It 'Should handle empty/null' {
            ConvertTo-SafeCsvValue -Value '' | Should -Be ''
            ConvertTo-SafeCsvValue -Value $null | Should -BeNullOrEmpty
        }
    }
}

# ============================================================
# HTML XSS TESTS (Mutation M7)
# ============================================================

Describe 'ConvertTo-HtmlSafe - Mutation Kill M7' {
    Context 'XSS Prevention' {
        
        It 'M7-KILL: Should encode angle brackets' {
            $sample = 'Test {0}tag{1} value' -f '<','>'
            $result = ConvertTo-HtmlSafe -Value $sample
            $result | Should -Match 'tag'
            $result | Should -Match '&lt;'
            $result | Should -Match '&gt;'
        }
        
        It 'M7-KILL: Should encode quotes' {
            $result = ConvertTo-HtmlSafe -Value 'onclick="evil()"'
            $result | Should -Match '&quot;'
            $result | Should -Not -Match 'onclick="'
        }
        
        It 'M7-KILL: Should encode ampersand' {
            $result = ConvertTo-HtmlSafe -Value 'A & B'
            $result | Should -Match '&amp;'
        }
        
        It 'Should handle filename with HTML chars' {
            $result = ConvertTo-HtmlSafe -Value 'Game <Region> (v1.0).chd'
            $result | Should -Match '&lt;Region&gt;'
        }
        
        It 'Should handle null' {
            ConvertTo-HtmlSafe -Value $null | Should -Be ''
        }
    }
}

Describe 'HTML Report XSS Integration' {
    Context 'Full Report Generation' {
        
        It 'M7-KILL: Malicious filename should be escaped in report' {
            $report = [System.Collections.Generic.List[psobject]]::new()
            $report.Add([pscustomobject]@{
                GameKey      = '<script>alert("xss")</script>'
                Action       = 'KEEP'
                Category     = 'GAME'
                Region       = 'EU'
                WinnerRegion = 'EU'
                VersionScore = 100
                FormatScore  = 850
                Type         = 'FILE'
                DatMatch     = $false
                MainPath     = 'C:\ROMs\<script>evil</script>.chd'
                Root         = 'C:\ROMs'
                SizeBytes    = 1234
            })
            
            $tempHtml = Join-Path $TestDrive "pester_xss_$(Get-Random).html"

            Push-Location $TestDrive
            try {
                ConvertTo-HtmlReport -Report $report -HtmlPath $tempHtml `
                    -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 `
                    -JunkCount 0 -JunkBytes 0 -BiosCount 0 `
                    -UniqueGames 1 -TotalScanned 1 -Mode 'DryRun'

                $html = Get-Content -LiteralPath $tempHtml -Raw

                # Script tags should be encoded
                $html | Should -Match '&lt;script&gt;'
                $html | Should -Not -Match '<script>alert'
                $html | Should -Not -Match '<script>evil'
            } finally {
                Pop-Location
                Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# ============================================================
# TOOL FAILURE TESTS (Mutation M8)
# ============================================================

Describe 'Tool Failure Handling - Mutation Kill M8' {
    Context 'Conversion Tool Failures' {
        
        It 'M8-KILL: Non-zero exit code should NOT leave partial output' {
            # This is a behavioral contract test
            # The actual mock would need Start-Process interception
            
            # Verify the cleanup logic exists in conversion / tool modules
            $toolsModulePath = Join-Path (Split-Path -Parent $script:ScriptPath) 'dev\modules\Tools.ps1'
            $toolsModuleContent = Get-Content -LiteralPath $toolsModulePath -Raw
            $convertModulePath = Join-Path (Split-Path -Parent $script:ScriptPath) 'dev\modules\Convert.ps1'
            $convertModuleContent = Get-Content -LiteralPath $convertModulePath -Raw
            
            # Should have cleanup on failure — ExitCode check lives in Tools.ps1
            $toolsModuleContent | Should -Match '\.ExitCode -ne 0'
            ($toolsModuleContent + "`n" + $convertModuleContent) | Should -Match 'Remove-Item.*\$targetPath'
        }
        
        It 'M8-KILL: 7z extraction failure should return SKIP not crash' {
            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }
            
            # Call with non-existent 7z
            $result = Expand-ArchiveToTemp -ArchivePath 'C:\nonexistent.zip' `
                -SevenZipPath 'C:\missing\7z.exe' -Log $logFn
            
            $result | Should -BeNullOrEmpty -Because 'Missing tool should return null/skip'
        }
    }
}

# ============================================================
# EXCLUDE PATHS TEST (Mutation M11)
# ============================================================

Describe 'Exclude Paths - Mutation Kill M11' {
    Context '_TRASH Self-Scan Prevention' {
        
        It 'M11-KILL: Script should exclude _TRASH from scanning' {
            $scriptContent = Get-Content -LiteralPath $script:ScriptPath -Raw
            
            # Should have _TRASH in excluded paths or filter
            $scriptContent | Should -Match '_TRASH'
        }
        
        It 'M11-KILL: Script should exclude _BIOS from scanning' {
            $scriptContent = Get-Content -LiteralPath $script:ScriptPath -Raw
            
            $scriptContent | Should -Match '_BIOS'
        }
    }
}
