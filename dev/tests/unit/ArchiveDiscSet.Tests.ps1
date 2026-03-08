#requires -Modules Pester

<#
  T-03: Archive CUE+BIN / CCD+IMG / GDI+BIN → LAST_ARCHIVE_HAS_DISC_SET Flag
  BUG-CS-03: Get-ConsoleFromArchiveEntries muss Disc-Image-Sets erkennen.
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

Describe 'T-03: Archive Disc-Set Erkennung (BUG-CS-03)' {

    BeforeEach {
        $script:LAST_ARCHIVE_HAS_DISC_SET = $false
    }

    Context 'CUE+BIN Archive → Disc-Set erkannt' {
        It 'CUE+BIN ohne gemappte Extensions → $null, aber DISC_SET=true' {
            $entries = @('game.cue', 'game.bin')
            $result = Get-ConsoleFromArchiveEntries -EntryPaths $entries
            $result | Should -BeNullOrEmpty
            $script:LAST_ARCHIVE_HAS_DISC_SET | Should -BeTrue
        }

        It 'CUE+BIN+Track-Dateien → DISC_SET=true' {
            $entries = @('game.cue', 'game (Track 1).bin', 'game (Track 2).bin', 'game (Track 3).bin')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Out-Null
            $script:LAST_ARCHIVE_HAS_DISC_SET | Should -BeTrue
        }
    }

    Context 'CCD+IMG Archive → Disc-Set erkannt' {
        It 'CCD+IMG+SUB → DISC_SET=true' {
            $entries = @('game.ccd', 'game.img', 'game.sub')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Out-Null
            $script:LAST_ARCHIVE_HAS_DISC_SET | Should -BeTrue
        }
    }

    Context 'GDI Archive → DC erkannt (GDI ist unique Extension)' {
        It 'GDI+RAW+BIN → DC (GDI ist in EXT_MAP, kein Disc-Set-Fallback noetig)' {
            $entries = @('disc.gdi', 'track01.raw', 'track02.bin', 'track03.bin')
            $result = Get-ConsoleFromArchiveEntries -EntryPaths $entries
            $result | Should -Be 'DC'
        }
    }

    Context 'CUE-only (ohne gemappte Hauptextension) → Disc-Set erkannt' {
        It 'CUE+BIN in Unterordnern → DISC_SET=true' {
            $entries = @('game/track01.bin', 'game/track02.bin', 'game/game.cue')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Out-Null
            $script:LAST_ARCHIVE_HAS_DISC_SET | Should -BeTrue
        }
    }

    Context 'Kein Disc-Set → Flag bleibt false' {
        It 'Nur GBA-ROMs → DISC_SET=false' {
            $entries = @('game1.gba', 'game2.gba')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Out-Null
            $script:LAST_ARCHIVE_HAS_DISC_SET | Should -BeFalse
        }

        It 'Nur ISO → DISC_SET=false' {
            $entries = @('game.iso')
            Get-ConsoleFromArchiveEntries -EntryPaths $entries | Out-Null
            $script:LAST_ARCHIVE_HAS_DISC_SET | Should -BeFalse
        }

        It 'Leere Entries → DISC_SET=false' {
            Get-ConsoleFromArchiveEntries -EntryPaths @() | Out-Null
            $script:LAST_ARCHIVE_HAS_DISC_SET | Should -BeFalse
        }
    }

    Context 'Homogenes Archiv MIT Disc-Set (z.B. NES + CUE)' {
        It 'NES + CUE → NES returned, DISC_SET=false (NES gewinnt durch counts)' {
            # Wenn counts.Count == 1 (NES), wird DISC_SET-Check GAR NICHT erreicht
            $entries = @('game.nes', 'bonus.cue')
            $result = Get-ConsoleFromArchiveEntries -EntryPaths $entries
            $result | Should -Be 'NES'
            # Bei early return wird DISC_SET nicht gesetzt
        }
    }
}
