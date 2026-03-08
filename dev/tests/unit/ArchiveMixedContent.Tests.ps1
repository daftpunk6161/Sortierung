#requires -Modules Pester

<#
  TEST-07: Mixed-Content Archive Tests
  Validiert, dass Archive mit gemischtem Inhalt korrekt behandelt werden.
  - Homogene Archive -> korrekte Konsole
  - Heterogene Archive -> $null (ambig)
  - Nur unmapped Extensions -> $null
  - Leere Archive -> $null
#>

BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
        $root = Split-Path -Parent $root
    }
    . (Join-Path $root 'dev\modules\FileOps.ps1')
    . (Join-Path $root 'dev\modules\Settings.ps1')
    . (Join-Path $root 'dev\modules\LruCache.ps1')
    . (Join-Path $root 'dev\modules\AppState.ps1')
    . (Join-Path $root 'dev\modules\Tools.ps1')
    . (Join-Path $root 'dev\modules\SetParsing.ps1')
    . (Join-Path $root 'dev\modules\Core.ps1')
    . (Join-Path $root 'dev\modules\Classification.ps1')
}

Describe 'TEST-07: Archive Mixed Content Erkennung' {

    Context 'Homogene Archive (eine Konsole)' {
        It 'Nur GBA-ROMs -> GBA' {
            $entries = @('game1.gba', 'game2.gba', 'game3.gba')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'GBA'
        }

        It 'Nur NES-ROMs -> NES' {
            $entries = @('rom1.nes', 'rom2.nes')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'NES'
        }

        It 'Nur SNES-ROMs (.sfc) -> SNES' {
            $entries = @('game.sfc', 'game2.sfc', 'readme.txt')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'SNES'
        }

        It 'Nur WBFS -> WII' {
            $entries = @('disc/game.wbfs')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'WII'
        }

        It 'Extensions in Unterordnern -> korrekte Erkennung' {
            $entries = @('folder/subfolder/game1.gba', 'folder/game2.gba')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'GBA'
        }

        It 'ROM + nicht-gemappte Dateien -> Konsole der ROMs' {
            $entries = @('game.nds', 'readme.txt', 'cover.jpg', 'save.sav')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'NDS'
        }
    }

    Context 'Heterogene Archive (mehrere Konsolen)' {
        It 'NES + SNES -> $null (ambig)' {
            $entries = @('game.nes', 'game.sfc')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }

        It 'GBA + NDS -> $null (ambig)' {
            $entries = @('game.gba', 'game.nds')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }

        It 'GBA + GBC + GB -> $null (3 verschiedene Konsolen)' {
            $entries = @('game.gba', 'game.gbc', 'game.gb')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }

        It 'N64 + SNES -> $null (ambig)' {
            $entries = @('mario.z64', 'zelda.sfc')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }
    }

    Context 'Keine gemappten Extensions' {
        It 'Nur .bin/.cue -> $null (multi-platform, nicht in ExtMap)' {
            $entries = @('game.bin', 'game.cue')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }

        It 'Nur .iso -> $null (multi-platform)' {
            $entries = @('game.iso')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }

        It 'Nur .txt/.jpg -> $null (keine ROM-Extensions)' {
            $entries = @('readme.txt', 'cover.jpg', 'manual.pdf')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }

        It 'Nur .chd -> $null (nicht in ExtMap)' {
            $entries = @('disc.chd')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -BeNullOrEmpty
        }
    }

    Context 'Edge Cases' {
        It 'Leere Entry-Liste -> $null' {
            Get-ConsoleFromArchiveEntries -EntryPaths @() | Should -BeNullOrEmpty
        }

        It '$null Entry-Liste -> $null' {
            Get-ConsoleFromArchiveEntries -EntryPaths $null | Should -BeNullOrEmpty
        }

        It 'Nur Whitespace-Entries -> $null' {
            Get-ConsoleFromArchiveEntries -EntryPaths @('', '  ', $null) | Should -BeNullOrEmpty
        }

        It 'Entries ohne Extension -> $null' {
            Get-ConsoleFromArchiveEntries -EntryPaths @('README', 'CHANGELOG', 'Makefile') | Should -BeNullOrEmpty
        }

        It 'Sehr viele homogene Entries -> korrekte Konsole' {
            $entries = 1..200 | ForEach-Object { "game_$_.gba" }
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'GBA'
        }

        It 'Case-Insensitive Extension Matching' {
            $entries = @('GAME.GBA', 'game2.Gba', 'GAME3.gBA')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Should -Be 'GBA'
        }
    }
}
