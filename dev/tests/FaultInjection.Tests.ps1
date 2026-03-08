#requires -Modules Pester
<#
  Fault Injection & Error Handling Tests
  ======================================
  Tests mit Mocking für Tool-Failures, File-System-Errors, etc.
  
  Mutation Kills: M8, M9, M10, M11
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'fault_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# ============================================================
# DAT INDEX ERROR HANDLING (Mutation M10)
# ============================================================

Describe 'Get-DatIndex Error Handling - Mutation Kill M10' {
    Context 'Invalid XML Handling' {
        
        BeforeEach {
            $script:datTestDir = Join-Path $env:TEMP "pester_dat_$(Get-Random)"
            New-Item -ItemType Directory -Path $script:datTestDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:datTestDir -and (Test-Path -LiteralPath $script:datTestDir)) {
                Remove-Item -LiteralPath $script:datTestDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'M10-KILL: Non-XML DAT file should NOT crash' {
            $datPath = Join-Path $script:datTestDir 'clrmamepro.dat'
            
            # ClrMamePro format (not XML)
            $clrContent = @'
clrmamepro (
    name "Test Set"
    description "Test Description"
    version "1.0"
)
game (
    name "Test Game"
    rom ( name "test.bin" size 12345 crc ABCDEF12 )
)
'@
            $clrContent | Out-File -LiteralPath $datPath -Encoding ascii
            
            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }
            
            # Should not throw, should log and skip
            { 
                $null = Get-DatIndex -DatRoot $script:datTestDir -HashType 'CRC' -Log $logFn 
            } | Should -Not -Throw
            
            # Should have logged skip message
            ($logMessages -join "`n") | Should -Match 'ueberspringe|skip|clrmamepro|Nicht-XML'
        }
        
        It 'M10-KILL: Empty DAT file should NOT crash' {
            $datPath = Join-Path $script:datTestDir 'empty.dat'
            '' | Out-File -LiteralPath $datPath -Encoding ascii
            
            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }
            
            { Get-DatIndex -DatRoot $script:datTestDir -HashType 'SHA1' -Log $logFn } | Should -Not -Throw
        }
        
        It 'M10-KILL: Corrupted XML should NOT crash' {
            $datPath = Join-Path $script:datTestDir 'corrupt.dat'
            
            # Malformed XML
            $corrupt = '<?xml version="1.0"?><datafile><game name="test"><rom'
            $corrupt | Out-File -LiteralPath $datPath -Encoding ascii
            
            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }
            
            { Get-DatIndex -DatRoot $script:datTestDir -HashType 'SHA1' -Log $logFn } | Should -Not -Throw
        }
        
        It 'Valid XML DAT should be parsed without throwing' {
            $datPath = Join-Path $script:datTestDir 'valid.dat'
            
            $validXml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Test Game">
    <rom name="test.bin" sha1="abc123def456789" crc="12345678"/>
  </game>
</datafile>
'@
            $validXml | Out-File -LiteralPath $datPath -Encoding utf8
            
            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }
            
            # Should not throw - may return empty hashtable if no console mapping
            { Get-DatIndex -DatRoot $script:datTestDir -HashType 'SHA1' -Log $logFn } | Should -Not -Throw
        }
    }
    
    Context 'hashKey Initialization (StrictMode Bug)' {
        
        BeforeEach {
            $script:datTestDir = Join-Path $env:TEMP "pester_hashkey_$(Get-Random)"
            $script:consoleDir = Join-Path $script:datTestDir 'PS1'
            New-Item -ItemType Directory -Path $script:consoleDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:datTestDir -and (Test-Path -LiteralPath $script:datTestDir)) {
                Remove-Item -LiteralPath $script:datTestDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Should work with ConsoleMap parameter without variable errors' {
            $datPath = Join-Path $script:consoleDir 'ps1.dat'
            
            $datXml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Test PS1 Game">
    <rom name="test.bin" sha1="abc123" crc="12345678"/>
  </game>
</datafile>
'@
            $datXml | Out-File -LiteralPath $datPath -Encoding utf8
            
            $consoleMap = @{ 'PS1' = $script:consoleDir }
            $logMessages = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($msg) $logMessages.Add($msg) }
            
            { 
                $null = Get-DatIndex -DatRoot $script:datTestDir -HashType 'SHA1' `
                    -ConsoleMap $consoleMap -Log $logFn 
            } | Should -Not -Throw
        }
    }
}

# ============================================================
# CUE/GDI MISSING TRACKS (Mutation M9)
# ============================================================

Describe 'CUE/GDI Missing Track Detection - Mutation Kill M9' {
    Context 'Missing BIN Files' {
        
        BeforeEach {
            $script:cueTestDir = Join-Path $env:TEMP "pester_cue_$(Get-Random)"
            New-Item -ItemType Directory -Path $script:cueTestDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:cueTestDir -and (Test-Path -LiteralPath $script:cueTestDir)) {
                Remove-Item -LiteralPath $script:cueTestDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'M9-KILL: Should detect missing BIN file and return non-empty list' {
            $cuePath = Join-Path $script:cueTestDir 'game.cue'
            
            $cueContent = @'
FILE "track01.bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
FILE "track02.bin" BINARY
  TRACK 02 AUDIO
    INDEX 00 00:00:00
    INDEX 01 00:02:00
'@
            $cueContent | Out-File -LiteralPath $cuePath -Encoding ascii
            
            # Only create track01, not track02
            'dummy' | Out-File -LiteralPath (Join-Path $script:cueTestDir 'track01.bin') -Encoding ascii
            
            $missing = @(Get-CueMissingFiles -CuePath $cuePath)
            
            $missing.Count | Should -Be 1 -Because 'track02.bin is missing'
            $missing[0] | Should -Match 'track02\.bin'
        }
        
        It 'M9-KILL: Should return empty list when all tracks exist' {
            $cuePath = Join-Path $script:cueTestDir 'complete.cue'
            
            $cueContent = @'
FILE "data.bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
'@
            $cueContent | Out-File -LiteralPath $cuePath -Encoding ascii
            'data' | Out-File -LiteralPath (Join-Path $script:cueTestDir 'data.bin') -Encoding ascii
            
            $missing = @(Get-CueMissingFiles -CuePath $cuePath)
            $missing.Count | Should -Be 0
        }
        
        It 'M9-KILL: Should handle quoted filenames with spaces' {
            $cuePath = Join-Path $script:cueTestDir 'quoted.cue'
            
            $cueContent = @'
FILE "Track 01 - Audio.bin" BINARY
  TRACK 01 AUDIO
    INDEX 01 00:00:00
'@
            $cueContent | Out-File -LiteralPath $cuePath -Encoding ascii
            
            $missing = @(Get-CueMissingFiles -CuePath $cuePath)
            $missing.Count | Should -Be 1
            $missing[0] | Should -Match 'Track 01 - Audio\.bin'
        }
    }
    
    Context 'GDI Missing Track Detection' {
        
        BeforeEach {
            $script:gdiTestDir = Join-Path $env:TEMP "pester_gdi_$(Get-Random)"
            New-Item -ItemType Directory -Path $script:gdiTestDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:gdiTestDir -and (Test-Path -LiteralPath $script:gdiTestDir)) {
                Remove-Item -LiteralPath $script:gdiTestDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'M9-KILL: Should detect missing GDI track files' {
            $gdiPath = Join-Path $script:gdiTestDir 'game.gdi'
            
            $gdiContent = @'
3
1 0 4 2352 track01.raw 0
2 756 0 2352 track02.raw 0
3 45000 4 2352 track03.bin 0
'@
            $gdiContent | Out-File -LiteralPath $gdiPath -Encoding ascii
            
            # Only create track01
            'data' | Out-File -LiteralPath (Join-Path $script:gdiTestDir 'track01.raw') -Encoding ascii
            
            $missing = @(Get-GdiMissingFiles -GdiPath $gdiPath)
            
            $missing.Count | Should -Be 2 -Because 'track02 and track03 are missing'
        }
    }
}

# ============================================================
# FILE SYSTEM FAULT INJECTION
# ============================================================

Describe 'File System Error Handling' {
    Context 'Get-FilesSafe ReparsePoint Handling' {
        
        It 'Should skip reparse points during enumeration' {
            $fileOpsPath = Join-Path (Split-Path -Parent $script:ScriptPath) 'dev\modules\FileOps.ps1'
            $fileOpsContent = Get-Content -LiteralPath $fileOpsPath -Raw
            
            # Verify reparse point filtering exists in FileOps module
            $fileOpsContent | Should -Match 'ReparsePoint'
            $fileOpsContent | Should -Match '\.Attributes\s*-band\s*\[IO\.FileAttributes\]::ReparsePoint'
        }
    }
    
    Context 'Get-DirectoriesSafe' {
        
        BeforeEach {
            $script:dirTestDir = Join-Path $env:TEMP "pester_dirs_$(Get-Random)"
            New-Item -ItemType Directory -Path $script:dirTestDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:dirTestDir -and (Test-Path -LiteralPath $script:dirTestDir)) {
                Remove-Item -LiteralPath $script:dirTestDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Should enumerate nested directories' {
            # Create nested structure
            New-Item -ItemType Directory -Path (Join-Path $script:dirTestDir 'a\b\c') -Force | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $script:dirTestDir 'd') -Force | Out-Null
            
            $dirs = @(Get-DirectoriesSafe -Root $script:dirTestDir)
            
            $dirs.Count | Should -Be 4 # a, a\b, a\b\c, d
        }
        
        It 'Should handle empty directory' {
            $dirs = @(Get-DirectoriesSafe -Root $script:dirTestDir)
            $dirs.Count | Should -Be 0
        }
    }
}

# ============================================================
# CANCEL HANDLING
# ============================================================

Describe 'Cancellation Handling' {
    Context 'Test-CancelRequested' {
        
        It 'Should throw OperationCanceledException when cancelled' {
            Set-AppStateValue -Key 'CancelRequested' -Value $true -SyncLegacy | Out-Null
            
            try {
                { Test-CancelRequested } | Should -Throw
            } finally {
                Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
            }
        }
        
        It 'Should not throw when not cancelled' {
            Set-AppStateValue -Key 'CancelRequested' -Value $false -SyncLegacy | Out-Null
            
            { Test-CancelRequested } | Should -Not -Throw
        }
    }
}

# ============================================================
# EMPTY/EDGE CASE INPUTS
# ============================================================

Describe 'Edge Case Input Handling' {
    Context 'Empty Collections' {
        
        It 'Get-CueRelatedFiles should handle non-existent file' {
            $result = @(Get-CueRelatedFiles -CuePath 'C:\NonExistent\fake.cue')
            $result.Count | Should -Be 1 # Returns the path itself
        }
        
        It 'Get-GdiRelatedFiles should handle non-existent file' {
            $result = @(Get-GdiRelatedFiles -GdiPath 'C:\NonExistent\fake.gdi')
            $result.Count | Should -Be 1
        }
        
        It 'Get-M3URelatedFiles should handle non-existent file' {
            $result = @(Get-M3URelatedFiles -M3UPath 'C:\NonExistent\fake.m3u')
            $result.Count | Should -Be 1
        }
    }
    
    Context 'Malformed Content' {
        
        BeforeEach {
            $script:malformTestDir = Join-Path $env:TEMP "pester_malform_$(Get-Random)"
            New-Item -ItemType Directory -Path $script:malformTestDir -Force | Out-Null
        }
        
        AfterEach {
            if ($script:malformTestDir -and (Test-Path -LiteralPath $script:malformTestDir)) {
                Remove-Item -LiteralPath $script:malformTestDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        
        It 'Malformed CUE should not crash' {
            $cuePath = Join-Path $script:malformTestDir 'bad.cue'
            'GARBAGE NOT A CUE FILE!!!###' | Out-File -LiteralPath $cuePath -Encoding ascii
            
            { Get-CueMissingFiles -CuePath $cuePath } | Should -Not -Throw
            { Get-CueRelatedFiles -CuePath $cuePath } | Should -Not -Throw
        }
        
        It 'Malformed GDI should not crash' {
            $gdiPath = Join-Path $script:malformTestDir 'bad.gdi'
            'GARBAGE NOT A GDI FILE ###' | Out-File -LiteralPath $gdiPath -Encoding ascii
            
            { Get-GdiMissingFiles -GdiPath $gdiPath } | Should -Not -Throw
            { Get-GdiRelatedFiles -GdiPath $gdiPath } | Should -Not -Throw
        }
        
        It 'M3U with only comments should work' {
            $m3uPath = Join-Path $script:malformTestDir 'comments.m3u'
            @'
# This is a comment
# Another comment
'@ | Out-File -LiteralPath $m3uPath -Encoding ascii
            
            $result = @(Get-M3URelatedFiles -M3UPath $m3uPath)
            $result.Count | Should -Be 1 # Just the M3U itself
        }
    }
}

# ============================================================  
# CONSOLE DETECTION EDGE CASES
# ============================================================

Describe 'Console Detection Edge Cases' {
    Context 'Fallback Behavior' {
        
        It 'Should return UNKNOWN for unrecognized folder structure' {
            $result = Get-ConsoleType -RootPath 'C:\RandomGames' -FilePath 'C:\RandomGames\game.bin' -Extension '.bin'
            $result | Should -Be 'UNKNOWN'
        }
        
        It 'Should detect from folder name even with empty extension' {
            # BUG-09: only relative subfolders within root are checked, not root itself
            $result = Get-ConsoleType -RootPath 'C:\ROMs' -FilePath 'C:\ROMs\PS1\game' -Extension ''
            $result | Should -Be 'PS1'
        }
        
        It 'Should detect from extension when folder is unknown' {
            $result = Get-ConsoleType -RootPath 'C:\MyGames' -FilePath 'C:\MyGames\game.gba' -Extension '.gba'
            $result | Should -Be 'GBA'
        }
    }
}

# ============================================================
# SAFE FILENAME CONVERSION
# ============================================================

Describe 'ConvertTo-SafeFileName' {
    Context 'Invalid Character Removal' {
        
        It 'Should replace invalid characters with underscore' {
            $result = ConvertTo-SafeFileName -Name 'Game:Title<>|?.chd'
            $result | Should -Not -Match '[:<>|?]'
            $result | Should -Match '_'
        }
        
        It 'Should handle null/empty input' {
            ConvertTo-SafeFileName -Name '' | Should -Be 'root'
            ConvertTo-SafeFileName -Name $null | Should -Be 'root'
        }
        
        It 'Should preserve valid characters' {
            $result = ConvertTo-SafeFileName -Name 'Game Title (Europe)'
            $result | Should -Be 'Game Title (Europe)'
        }
    }
}
