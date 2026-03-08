#requires -Modules Pester

<#
  BUG-CS-08: Folder-Cache darf NUR bei Folder-Matches befuellt werden.
  Extension/Header/Filename-Matches duerfen NICHT auf Sibling-Dateien propagieren.
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

Describe 'BUG-CS-08: Folder-Cache Extension-Propagation' {

    BeforeEach {
        # Caches komplett leeren
        $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $script:LAST_CONSOLE_TYPE_SOURCE = $null
    }

    Context 'Extension-Match propagiert nicht auf Siblings' {
        It '.gba-Datei setzt Folder-Cache NICHT (Extension-Match)' {
            $tempDir = Join-Path $env:TEMP "pester_fc_$(Get-Random)"
            $subDir = Join-Path $tempDir 'mixed_games'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $gbaFile = Join-Path $subDir 'game.gba'

            try {
                $result = Get-ConsoleType -RootPath $tempDir -FilePath $gbaFile -Extension '.gba'
                $result | Should -Be 'GBA'

                # Folder-Cache sollte LEER sein (Extension-Match, kein Folder-Match)
                $folderCacheKey = '{0}|{1}' -f (Resolve-RootPath -Path $tempDir), 'mixed_games'
                $script:CONSOLE_FOLDER_TYPE_CACHE.ContainsKey($folderCacheKey) | Should -BeFalse
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It '.txt im selben Ordner wie .gba -> UNKNOWN (nicht GBA)' {
            $tempDir = Join-Path $env:TEMP "pester_fc_$(Get-Random)"
            $subDir = Join-Path $tempDir 'games'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $gbaFile = Join-Path $subDir 'game.gba'
            $txtFile = Join-Path $subDir 'readme.txt'

            try {
                # Zuerst .gba -> GBA
                $r1 = Get-ConsoleType -RootPath $tempDir -FilePath $gbaFile -Extension '.gba'
                $r1 | Should -Be 'GBA'

                # Dann .txt -> UNKNOWN (nicht GBA!)
                $r2 = Get-ConsoleType -RootPath $tempDir -FilePath $txtFile -Extension '.txt'
                $r2 | Should -Be 'UNKNOWN'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It '.nes im selben Ordner wie .gba -> NES (nicht GBA)' {
            $tempDir = Join-Path $env:TEMP "pester_fc_$(Get-Random)"
            $subDir = Join-Path $tempDir 'roms'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $gbaFile = Join-Path $subDir 'game.gba'
            $nesFile = Join-Path $subDir 'other.nes'

            try {
                $r1 = Get-ConsoleType -RootPath $tempDir -FilePath $gbaFile -Extension '.gba'
                $r1 | Should -Be 'GBA'

                $r2 = Get-ConsoleType -RootPath $tempDir -FilePath $nesFile -Extension '.nes'
                $r2 | Should -Be 'NES'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Folder-Match propagiert korrekt auf Siblings' {
        It 'Ordnername "GBA" -> Folder-Cache gesetzt' {
            $tempDir = Join-Path $env:TEMP "pester_fc_$(Get-Random)"
            $subDir = Join-Path $tempDir 'GBA'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file = Join-Path $subDir 'game.zip'

            try {
                $result = Get-ConsoleType -RootPath $tempDir -FilePath $file -Extension '.zip'
                $result | Should -Be 'GBA'

                # Folder-Cache SOLLTE gesetzt sein
                $rootNorm = Resolve-RootPath -Path $tempDir
                $folderCacheKey = '{0}|{1}' -f $rootNorm, 'gba'
                $script:CONSOLE_FOLDER_TYPE_CACHE.ContainsKey($folderCacheKey) | Should -BeTrue
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Zweite Datei im GBA-Ordner nutzt Folder-Cache' {
            $tempDir = Join-Path $env:TEMP "pester_fc_$(Get-Random)"
            $subDir = Join-Path $tempDir 'GBA'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file1 = Join-Path $subDir 'game1.zip'
            $file2 = Join-Path $subDir 'game2.zip'

            try {
                Get-ConsoleType -RootPath $tempDir -FilePath $file1 -Extension '.zip' | Should -Be 'GBA'
                Get-ConsoleType -RootPath $tempDir -FilePath $file2 -Extension '.zip' | Should -Be 'GBA'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
