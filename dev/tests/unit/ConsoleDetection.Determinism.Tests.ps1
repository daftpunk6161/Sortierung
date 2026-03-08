#requires -Modules Pester

<#
  TEST-02 + TEST-10: Determinismus und Stabilitaet der Detection Pipeline
  Validiert:
    - Gleicher Input -> stets identisches Ergebnis (Get-ConsoleType)
    - Wiederholte Aufrufe mit Cache-Reset -> selbes Ergebnis
    - Mehrfache Aufrufe ohne Cache-Reset -> Cache-Hit liefert identisch
#>

BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
        $root = Split-Path -Parent $root
    }
    . (Join-Path $root 'dev\modules\Settings.ps1')
    . (Join-Path $root 'dev\modules\LruCache.ps1')
    . (Join-Path $root 'dev\modules\AppState.ps1')
    . (Join-Path $root 'dev\modules\Tools.ps1')
    . (Join-Path $root 'dev\modules\SetParsing.ps1')
    . (Join-Path $root 'dev\modules\Core.ps1')
    . (Join-Path $root 'dev\modules\Classification.ps1')
    . (Join-Path $root 'dev\modules\FileOps.ps1')
}

Describe 'TEST-02/10: Detection Determinismus und Stabilitaet' {

    BeforeEach {
        $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        if (Get-Variable -Name ISO_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue) {
            $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        }
    }

    Context 'Get-ConsoleType Determinismus (TEST-02)' {

        $extCases = @(
            @{ Ext = '.gba'; Expected = 'GBA' }
            @{ Ext = '.nes'; Expected = 'NES' }
            @{ Ext = '.sfc'; Expected = 'SNES' }
            @{ Ext = '.nds'; Expected = 'NDS' }
            @{ Ext = '.n64'; Expected = 'N64' }
            @{ Ext = '.xci'; Expected = 'SWITCH' }
            @{ Ext = '.vpk'; Expected = 'VITA' }
            @{ Ext = '.wbfs'; Expected = 'WII' }
            @{ Ext = '.gen'; Expected = 'MD' }
            @{ Ext = '.pce'; Expected = 'PCE' }
        )

        It '<Ext> liefert bei 20 Wiederholungen stets <Expected>' -TestCases $extCases {
            param($Ext, $Expected)
            $tempDir = Join-Path $env:TEMP "pester_det_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $filePath = Join-Path $tempDir "game$Ext"
            try {
                $results = [System.Collections.Generic.List[string]]::new()
                for ($i = 0; $i -lt 20; $i++) {
                    $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
                    $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
                    $results.Add((Get-ConsoleType -RootPath $tempDir -FilePath $filePath -Extension $Ext))
                }
                $unique = @($results | Sort-Object -Unique)
                $unique.Count | Should -Be 1
                $unique[0] | Should -Be $Expected
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Folder-basierte Detection ist deterministic' {
            $tempDir = Join-Path $env:TEMP "pester_det_folder_$(Get-Random)"
            $subDir = Join-Path $tempDir 'dreamcast'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $filePath = Join-Path $subDir 'game.bin'
            try {
                $results = [System.Collections.Generic.List[string]]::new()
                for ($i = 0; $i -lt 20; $i++) {
                    $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
                    $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
                    $results.Add((Get-ConsoleType -RootPath $tempDir -FilePath $filePath -Extension '.bin'))
                }
                $unique = @($results | Sort-Object -Unique)
                $unique.Count | Should -Be 1
                $unique[0] | Should -Be 'DC'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'UNKNOWN bleibt bei unbekannter Extension deterministic' {
            $tempDir = Join-Path $env:TEMP "pester_det_unk_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $filePath = Join-Path $tempDir 'file.xyz'
            try {
                $results = [System.Collections.Generic.List[string]]::new()
                for ($i = 0; $i -lt 20; $i++) {
                    $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
                    $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
                    $results.Add((Get-ConsoleType -RootPath $tempDir -FilePath $filePath -Extension '.xyz'))
                }
                $unique = @($results | Sort-Object -Unique)
                $unique.Count | Should -Be 1
                $unique[0] | Should -Be 'UNKNOWN'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Cache-Stabilitaet (TEST-10)' {

        It 'Cache-Hit und Cache-Miss liefern identisches Ergebnis' {
            $tempDir = Join-Path $env:TEMP "pester_cache_stab_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            $filePath = Join-Path $tempDir 'game.gba'
            try {
                # Erster Aufruf (Cache-Miss)
                $result1 = Get-ConsoleType -RootPath $tempDir -FilePath $filePath -Extension '.gba'
                # Zweiter Aufruf (Cache-Hit)
                $result2 = Get-ConsoleType -RootPath $tempDir -FilePath $filePath -Extension '.gba'
                $result1 | Should -Be 'GBA'
                $result2 | Should -Be $result1
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Folder-Cache-Hit liefert identisches Ergebnis fuer andere Datei im selben Ordner' {
            $tempDir = Join-Path $env:TEMP "pester_fcache_$(Get-Random)"
            $subDir = Join-Path $tempDir 'ps1'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            try {
                $result1 = Get-ConsoleType -RootPath $tempDir -FilePath (Join-Path $subDir 'game1.bin') -Extension '.bin'
                $result2 = Get-ConsoleType -RootPath $tempDir -FilePath (Join-Path $subDir 'game2.bin') -Extension '.bin'
                $result1 | Should -Be 'PS1'
                $result2 | Should -Be $result1
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'GetConsoleFromArchiveEntries: identische Entries -> stets gleiches Ergebnis' {
            $entries = @('folder/game1.gba', 'folder/game2.gba', 'folder/game3.gba')
            $results = [System.Collections.Generic.List[string]]::new()
            for ($i = 0; $i -lt 10; $i++) {
                $results.Add([string](Get-ConsoleFromArchiveEntries -EntryPaths $entries))
            }
            $unique = @($results | Sort-Object -Unique)
            $unique.Count | Should -Be 1
            $unique[0] | Should -Be 'GBA'
        }
    }

    Context 'Disc Header Determinismus' {

        It 'Gleiche ISO liefert stets gleiche Konsole' {
            # GC magic
            $path = Join-Path ([System.IO.Path]::GetTempPath()) ("det-gc-{0}.iso" -f [Guid]::NewGuid().ToString('N'))
            $buffer = New-Object byte[] 0x8100
            $buffer[0x1C] = 0xC2; $buffer[0x1D] = 0x33; $buffer[0x1E] = 0x9F; $buffer[0x1F] = 0x3D
            [System.IO.File]::WriteAllBytes($path, $buffer)
            try {
                $results = [System.Collections.Generic.List[string]]::new()
                for ($i = 0; $i -lt 10; $i++) {
                    $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
                    $results.Add((Get-DiscHeaderConsole -Path $path))
                }
                $unique = @($results | Sort-Object -Unique)
                $unique.Count | Should -Be 1
                $unique[0] | Should -Be 'GC'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
