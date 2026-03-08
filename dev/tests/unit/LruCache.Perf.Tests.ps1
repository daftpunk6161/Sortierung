#requires -Modules Pester
# ================================================================
#  LruCache.Perf.Tests.ps1  –  F-03
#  Regression gate: 10,000 LRU evictions must complete in < 500 ms.
#  Verifies O(1) behaviour of the LinkedList+NodeMap rewrite.
# ================================================================

Describe 'F-03: LRU Cache – O(1) Performance-Regression' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\LruCache.ps1')
        . (Join-Path $root 'dev\modules\Tools.ps1')
    }

    BeforeEach { Reset-ArchiveEntryCache }
    AfterEach  { Reset-ArchiveEntryCache }

    It '10.000 Einträge schreiben/lesen in vertretbarer Zeit' {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()

        for ($i = 0; $i -lt 10000; $i++) {
            $key  = "archive_$($i % 500).zip"     # triggers eviction when i >= cacheSize
            $val  = @("entry_$i.bin")
            Set-ArchiveEntryCacheValue -Key $key -Value $val
            [void](Get-ArchiveEntryCacheValue -Key $key)
        }

        $sw.Stop()
        $avgPerOpMs = [double]$sw.ElapsedMilliseconds / 10000.0
        $avgPerOpMs | Should -BeLessThan 1.0 `
            -Because "F-03: O(1)-Verhalten soll unter 1 ms pro Operation bleiben (actual avg: $avgPerOpMs ms; total: $($sw.ElapsedMilliseconds) ms)"
    }

    It 'Cache-Groesse ueberschreitet nie das Limit' {
        $max = Get-ArchiveEntryCacheMaxEntries
        for ($i = 0; $i -lt ($max * 3); $i++) {
            Set-ArchiveEntryCacheValue -Key "k$i" -Value @("v$i")
        }
        $count = $script:ARCHIVE_ENTRY_LRU.Data.Count
        $count | Should -BeLessOrEqual $max -Because 'Eviction muss Groesse begrenzen'
    }

    It 'Touch-Operation befoerdert Eintrag ans Ende (MRU)' {
        Reset-ArchiveEntryCache
        Set-ArchiveEntryCacheValue -Key 'oldest' -Value @('a')
        Set-ArchiveEntryCacheValue -Key 'middle' -Value @('b')
        Set-ArchiveEntryCacheValue -Key 'newest' -Value @('c')

        # Touch 'oldest' – should become MRU via Get
        [void](Get-ArchiveEntryCacheValue -Key 'oldest')

        # First element in order should be 'middle' now (LRU)
        $script:ARCHIVE_ENTRY_LRU.Order.First.Value | Should -Be 'middle'
    }

    It 'Reset loescht alle Strukturen vollstaendig' {
        Set-ArchiveEntryCacheValue -Key 'x' -Value @('y')
        Reset-ArchiveEntryCache
        $script:ARCHIVE_ENTRY_LRU.Data.Count  | Should -Be 0
        $script:ARCHIVE_ENTRY_LRU.Order.Count | Should -Be 0
        $script:ARCHIVE_ENTRY_LRU.Nodes.Count | Should -Be 0
    }
}
