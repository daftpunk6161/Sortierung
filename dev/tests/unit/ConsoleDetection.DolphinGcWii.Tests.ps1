#requires -Modules Pester

<#
  TEST-04: GC-vs-WII Unterscheidung fuer .rvz/.gcz/.wia/.wbf1
  Validiert, dass DolphinTool + DiscID-Fallback korrekt zwischen GC und WII unterscheiden.
#>

BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
        $root = Split-Path -Parent $root
    }
    . (Join-Path $root 'dev\modules\Settings.ps1')
    . (Join-Path $root 'dev\modules\LruCache.ps1')
    . (Join-Path $root 'dev\modules\AppState.ps1')
    . (Join-Path $root 'dev\modules\FileOps.ps1')
    . (Join-Path $root 'dev\modules\Tools.ps1')
    . (Join-Path $root 'dev\modules\SetParsing.ps1')
    . (Join-Path $root 'dev\modules\Core.ps1')
    . (Join-Path $root 'dev\modules\Classification.ps1')
}

Describe 'TEST-04: GC vs WII Disc ID Erkennung' {

    Context 'Get-ConsoleFromDiscId Mapping' {
        It 'G-Prefix -> GC (GameCube)' {
            Get-ConsoleFromDiscId -DiscId 'GALE01' | Should -Be 'GC'
        }
        It 'R-Prefix -> WII' {
            Get-ConsoleFromDiscId -DiscId 'RMCE01' | Should -Be 'WII'
        }
        It 'S-Prefix -> WII' {
            Get-ConsoleFromDiscId -DiscId 'SMNE01' | Should -Be 'WII'
        }
        It 'W-Prefix -> WII' {
            Get-ConsoleFromDiscId -DiscId 'WBKE01' | Should -Be 'WII'
        }
        It 'Z-Prefix -> WII (WiiWare)' {
            Get-ConsoleFromDiscId -DiscId 'ZHAE01' | Should -Be 'WII'
        }
        It 'D-Prefix -> WII (Demo Disc)' {
            Get-ConsoleFromDiscId -DiscId 'DAKE01' | Should -Be 'WII'
        }
        It 'P-Prefix -> GC (Promo Disc)' {
            Get-ConsoleFromDiscId -DiscId 'PZLE01' | Should -Be 'GC'
        }
        It 'H-Prefix -> WII (Channel)' {
            Get-ConsoleFromDiscId -DiscId 'HAXA01' | Should -Be 'WII'
        }
        It 'X-Prefix -> WII (Expansion)' {
            Get-ConsoleFromDiscId -DiscId 'XABE01' | Should -Be 'WII'
        }
        It 'C-Prefix -> WII (Fitness)' {
            Get-ConsoleFromDiscId -DiscId 'CAFE01' | Should -Be 'WII'
        }
        It 'Leerer DiscId -> $null' {
            Get-ConsoleFromDiscId -DiscId '' | Should -BeNullOrEmpty
        }
        It 'Null DiscId -> $null' {
            Get-ConsoleFromDiscId -DiscId $null | Should -BeNullOrEmpty
        }
        It 'Unbekannter Prefix -> $null' {
            Get-ConsoleFromDiscId -DiscId 'Q12345' | Should -BeNullOrEmpty
        }
    }

    Context 'Get-ConsoleFromDiscIdInFileName Fallback' {
        It 'Erkennt GC DiscID in Klammern: [GALE01]' {
            $tempDir = Join-Path $env:TEMP "pester_discid_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $path = Join-Path $tempDir 'Zelda Wind Waker [GALE01].rvz'
            try {
                Get-ConsoleFromDiscIdInFileName -Path $path | Should -Be 'GC'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Erkennt WII DiscID in Klammern: [RMGE01]' {
            Get-ConsoleFromDiscIdInFileName -Path 'Mario Galaxy [RMGE01].rvz' | Should -Be 'WII'
        }

        It 'Erkennt WII DiscID in Klammern: [SMNE01]' {
            Get-ConsoleFromDiscIdInFileName -Path 'New SMB [SMNE01].gcz' | Should -Be 'WII'
        }

        It 'Erkennt GC DiscID ohne Klammern' {
            Get-ConsoleFromDiscIdInFileName -Path 'Melee GALE01.rvz' | Should -Be 'GC'
        }

        It 'Kein DiscID im Dateinamen -> $null' {
            Get-ConsoleFromDiscIdInFileName -Path 'mein-lieblingsspiel.rvz' | Should -BeNullOrEmpty
        }

        It 'Leerer Pfad -> $null' {
            Get-ConsoleFromDiscIdInFileName -Path '' | Should -BeNullOrEmpty
        }

        It 'Null Pfad -> $null' {
            Get-ConsoleFromDiscIdInFileName -Path $null | Should -BeNullOrEmpty
        }
    }

    Context 'Ambige Extensions nicht in ExtMap' {
        # .rvz/.gcz sollten NICHT in CONSOLE_EXT_MAP sein (BUG-01/02 Fix)
        It '.rvz ist NICHT in CONSOLE_EXT_MAP' {
            $script:CONSOLE_EXT_MAP.ContainsKey('.rvz') | Should -BeFalse
        }
        It '.gcz ist NICHT in CONSOLE_EXT_MAP' {
            $script:CONSOLE_EXT_MAP.ContainsKey('.gcz') | Should -BeFalse
        }
        It '.rvz ist NICHT in CONSOLE_EXT_MAP_UNIQUE' {
            $script:CONSOLE_EXT_MAP_UNIQUE.ContainsKey('.rvz') | Should -BeFalse
        }
        It '.gcz ist NICHT in CONSOLE_EXT_MAP_UNIQUE' {
            $script:CONSOLE_EXT_MAP_UNIQUE.ContainsKey('.gcz') | Should -BeFalse
        }
        It '.rvz ist NICHT in CONSOLE_EXT_MAP_AMBIG' {
            $script:CONSOLE_EXT_MAP_AMBIG.ContainsKey('.rvz') | Should -BeFalse
        }
        It '.gcz ist NICHT in CONSOLE_EXT_MAP_AMBIG' {
            $script:CONSOLE_EXT_MAP_AMBIG.ContainsKey('.gcz') | Should -BeFalse
        }
    }

    Context 'DolphinTool-faehige Extensions' {
        # Diese Extensions sollten via DolphinTool erkannt werden, nicht via ExtMap
        $testCases = @(
            @{ Ext = '.rvz';      Name = 'RVZ' }
            @{ Ext = '.gcz';      Name = 'GCZ' }
            @{ Ext = '.wia';      Name = 'WIA' }
            @{ Ext = '.wbf1';     Name = 'WBF1' }
        )

        It '<Name> (<Ext>) ohne DolphinTool und ohne DiscID -> UNKNOWN' -TestCases $testCases {
            param($Ext, $Name)
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $tempDir = Join-Path $env:TEMP "pester_dolphin_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $filePath = Join-Path $tempDir "game$Ext"
            try {
                $result = Get-ConsoleType -RootPath $tempDir -FilePath $filePath -Extension $Ext
                $result | Should -Be 'UNKNOWN'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        # BUG-CS-01/02: Invoke-ConsoleDetection liefert NEEDS_DOLPHIN_TOOL Reason-Code
        It '<Name> (<Ext>) via Invoke-ConsoleDetection -> NEEDS_DOLPHIN_TOOL' -TestCases $testCases {
            param($Ext, $Name)
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $tempDir = Join-Path $env:TEMP "pester_dolphin_rc_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $filePath = Join-Path $tempDir "game$Ext"
            try {
                $result = Invoke-ConsoleDetection `
                    -FilePath $filePath `
                    -RootPath $tempDir `
                    -Extension $Ext `
                    -UseDolphin $false
                $result.Console | Should -Be 'UNKNOWN'
                $result.DiagInfo['REASON_CODE'] | Should -Be 'NEEDS_DOLPHIN_TOOL'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        It 'WBFS (.wbfs) ist eindeutig WII via ExtMap' {
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $tempDir = Join-Path $env:TEMP "pester_dolphin_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $filePath = Join-Path $tempDir 'game.wbfs'
            try {
                $result = Get-ConsoleType -RootPath $tempDir -FilePath $filePath -Extension '.wbfs'
                $result | Should -Be 'WII'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Get-ConsoleFromDolphinTool Integration' {
        BeforeEach {
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        }

        It 'GC Disc via DolphinTool -> GC' {
            Mock -CommandName Invoke-DolphinToolInfoLines -MockWith {
                return @('Game ID: GALE01', 'Title: Zelda TWW')
            }
            $result = Get-ConsoleFromDolphinTool -Path 'C:\roms\zelda.rvz' -ToolPath 'C:\tools\dolphintool.exe'
            $result | Should -Be 'GC'
        }

        It 'WII Disc via DolphinTool -> WII' {
            Mock -CommandName Invoke-DolphinToolInfoLines -MockWith {
                return @('Game ID: RMGE01', 'Title: Mario Galaxy')
            }
            $result = Get-ConsoleFromDolphinTool -Path 'C:\roms\mario.rvz' -ToolPath 'C:\tools\dolphintool.exe'
            $result | Should -Be 'WII'
        }

        It 'Kein DiscID in DolphinTool-Output -> $null' {
            Mock -CommandName Invoke-DolphinToolInfoLines -MockWith {
                return @('Some other output', 'No game id here')
            }
            $result = Get-ConsoleFromDolphinTool -Path 'C:\roms\unknown.rvz' -ToolPath 'C:\tools\dolphintool.exe'
            $result | Should -BeNullOrEmpty
        }

        It 'DolphinTool nicht verfuegbar -> $null' {
            Mock -CommandName Invoke-DolphinToolInfoLines -MockWith { return $null }
            $result = Get-ConsoleFromDolphinTool -Path 'C:\roms\game.rvz' -ToolPath ''
            $result | Should -BeNullOrEmpty
        }
    }
}
