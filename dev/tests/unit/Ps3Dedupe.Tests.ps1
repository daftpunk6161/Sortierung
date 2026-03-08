#requires -Modules Pester
# ================================================================
#  Ps3Dedupe.Tests.ps1
#  Unit tests for PS3 folder grouping and multi-disc detection.
# ================================================================

Describe 'Ps3Dedupe – Ordner-Gruppierung' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')
    }

    Context 'Gruppe-Erkennung aus Ordnernamen' {

        It 'erkennt identischen Titel als eine Gruppe' {
            $items = @(
                [pscustomobject]@{ Name = 'Game Title'; DiscIndex = 1 }
                [pscustomobject]@{ Name = 'Game Title'; DiscIndex = 2 }
            )
            $groups = Group-Ps3TitleFolders -Items $items
            $groups.Count | Should -Be 1
            $groups[0].Items.Count | Should -Be 2
        }

        It 'trennt unterschiedliche Titel in separate Gruppen' {
            $items = @(
                [pscustomobject]@{ Name = 'Alpha Game'; DiscIndex = 1 }
                [pscustomobject]@{ Name = 'Beta Game'; DiscIndex = 1 }
            )
            $groups = Group-Ps3TitleFolders -Items $items
            $groups.Count | Should -Be 2
        }

        It 'leere Eingabe ergibt leeres Ergebnis' {
            $result = Group-Ps3TitleFolders -Items @()
            @($result).Count | Should -Be 0
        }
    }

    Context 'Multi-Disc Erkennung' {

        It 'erkennt Disc-1/Disc-2-Muster' {
            $folderName = 'Resistance Fall of Man [Disc 2]'
            $isMulti = Test-Ps3MultidiscFolder -FolderName $folderName
            $isMulti | Should -BeTrue
        }

        It 'ignoriert Ordner ohne Disc-Muster' {
            $folderName = 'Gran Turismo 5'
            $isMulti = Test-Ps3MultidiscFolder -FolderName $folderName
            $isMulti | Should -BeFalse
        }
    }

    Context 'Dupe-Score' {

        It 'Duplikat hat niedrigeren Score als Original' {
            if (-not (Get-Command Get-Ps3DupeScore -ErrorAction SilentlyContinue)) {
                Set-ItResult -Skipped -Because 'Get-Ps3DupeScore nicht verfügbar'
                return
            }
            $orig = [pscustomobject]@{ Name = 'Game'; Type = 'Original'; DiscCount = 2 }
            $dupe = [pscustomobject]@{ Name = 'Game'; Type = 'Backup';   DiscCount = 1 }
            $scoreOrig = Get-Ps3DupeScore -Item $orig
            $scoreDupe = Get-Ps3DupeScore -Item $dupe
            $scoreOrig | Should -BeGreaterThan $scoreDupe
        }
    }
}
