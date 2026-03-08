#requires -Modules Pester
<#
  LruCache Tests  [F18/F10]
  =========================
  Verifies correctness of the O(1) LRU eviction implementation
  (LinkedList + Node-Dictionary).  Tests cover:
  - Eviction order (true LRU: oldest evicted first)
  - O(1) touch: repeated Get calls must not degrade performance
  - Stress insert/evict with 5000+ entries
  - Reset, Statistics, Registry
#>

BeforeAll {
    $script:LruCachePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\LruCache.ps1'
    . $script:LruCachePath
}

Describe 'LruCache Eviction Order' {

    It 'evicts the least-recently-used entry, not the least-recently-inserted' {
        $c = New-LruCache -MaxEntries 3 -Name 'OrderTest'
        Set-LruCacheValue -Cache $c -Key 'A' -Value 1   # [A]
        Set-LruCacheValue -Cache $c -Key 'B' -Value 2   # [A,B]
        Set-LruCacheValue -Cache $c -Key 'C' -Value 3   # [A,B,C]

        # Touch A — A moves to MRU tail: [B,C,A]
        $null = Get-LruCacheValue -Cache $c -Key 'A'

        # Insert D — capacity exceeded; B (LRU head) must be evicted: [C,A,D]
        Set-LruCacheValue -Cache $c -Key 'D' -Value 4

        $c.Data.ContainsKey('B') | Should -Be $false -Because 'B was LRU and must be evicted'
        $c.Data.ContainsKey('A') | Should -Be $true  -Because 'A was touched and must stay'
        $c.Data.ContainsKey('C') | Should -Be $true  -Because 'C was not touched but inserted after B, stays'
        $c.Data.ContainsKey('D') | Should -Be $true  -Because 'D was just inserted'
        $c.Evictions | Should -Be 1
    }

    It 'evicts entries in true LRU order under sequential access' {
        $c = New-LruCache -MaxEntries 2 -Name 'SeqTest'
        Set-LruCacheValue -Cache $c -Key 'X' -Value 10  # [X]
        Set-LruCacheValue -Cache $c -Key 'Y' -Value 20  # [X,Y]

        # Overflow: Z evicts X (oldest)
        Set-LruCacheValue -Cache $c -Key 'Z' -Value 30  # [Y,Z]
        $c.Data.ContainsKey('X') | Should -Be $false
        $c.Data.ContainsKey('Y') | Should -Be $true
        $c.Data.ContainsKey('Z') | Should -Be $true

        # Overflow: W evicts Y (now oldest)
        Set-LruCacheValue -Cache $c -Key 'W' -Value 40  # [Z,W]
        $c.Data.ContainsKey('Y') | Should -Be $false
        $c.Data.ContainsKey('Z') | Should -Be $true
        $c.Data.ContainsKey('W') | Should -Be $true
    }

    It 'updates existing key without changing entry count' {
        $c = New-LruCache -MaxEntries 3 -Name 'UpdateTest'
        Set-LruCacheValue -Cache $c -Key 'A' -Value 1
        Set-LruCacheValue -Cache $c -Key 'B' -Value 2
        Set-LruCacheValue -Cache $c -Key 'A' -Value 99  # update, not insert

        $c.Data.Count | Should -Be 2                    # still 2 entries
        $c.Data['A']   | Should -Be 99
        $c.Evictions   | Should -Be 0
    }
}

Describe 'LruCache O(1) Performance — no degradation at scale' {

    It 'inserting 10000 entries stays under 10 seconds (O(1) expected)' {
        $c = New-LruCache -MaxEntries 5000 -Name 'PerfTest'

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        for ($i = 0; $i -lt 10000; $i++) {
            Set-LruCacheValue -Cache $c -Key "key_$i" -Value $i
        }
        $sw.Stop()

        $sw.Elapsed.TotalSeconds | Should -BeLessThan 10 `
            -Because "10000 inserts with eviction must not take more than 10s (O(1) expected, got $($sw.Elapsed.TotalMilliseconds)ms)"

        $c.Data.Count    | Should -Be 5000
        $c.Evictions     | Should -Be 5000
    }

    It 'reading 5000 entries with frequent touch stays under 5 seconds' {
        $c = New-LruCache -MaxEntries 5000 -Name 'ReadPerfTest'
        for ($i = 0; $i -lt 5000; $i++) {
            Set-LruCacheValue -Cache $c -Key "key_$i" -Value $i
        }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        for ($i = 0; $i -lt 5000; $i++) {
            $null = Get-LruCacheValue -Cache $c -Key "key_$($i % 1000)"
        }
        $sw.Stop()

        $sw.Elapsed.TotalSeconds | Should -BeLessThan 5 `
            -Because "5000 cache reads must not take more than 5s (O(1) expected, got $($sw.Elapsed.TotalMilliseconds)ms)"

        $c.Hits   | Should -BeGreaterThan 0
    }
}

Describe 'LruCache Correctness' {

    It 'Get-LruCacheValue returns null for missing key' {
        $c = New-LruCache -MaxEntries 10 -Name 'NullTest'
        $result = Get-LruCacheValue -Cache $c -Key 'nonexistent'
        $result | Should -BeNullOrEmpty
        $c.Misses | Should -Be 1
    }

    It 'Test-LruCacheContains returns true only for present keys' {
        $c = New-LruCache -MaxEntries 10 -Name 'ContainsTest'
        Set-LruCacheValue -Cache $c -Key 'present' -Value 'yes'
        Test-LruCacheContains -Cache $c -Key 'present'   | Should -Be $true
        Test-LruCacheContains -Cache $c -Key 'absent'    | Should -Be $false
    }

    It 'Reset-LruCache clears all data, order, nodes, and stats' {
        $c = New-LruCache -MaxEntries 10 -Name 'ResetTest'
        Set-LruCacheValue -Cache $c -Key 'K1' -Value 1
        Set-LruCacheValue -Cache $c -Key 'K2' -Value 2
        $null = Get-LruCacheValue -Cache $c -Key 'K1'

        Reset-LruCache -Cache $c

        $c.Data.Count   | Should -Be 0
        $c.Order.Count  | Should -Be 0
        $c.Nodes.Count  | Should -Be 0 -Because 'Nodes dict must be cleared on reset'
        $c.Hits         | Should -Be 0
        $c.Misses       | Should -Be 0
        $c.Evictions    | Should -Be 0
    }

    It 'empty-string key is silently ignored (not stored, no crash)' {
        $c = New-LruCache -MaxEntries 10 -Name 'EmptyKeyTest'
        # Set-LruCacheValue silently returns when key is whitespace/empty
        { Set-LruCacheValue -Cache $c -Key '' -Value 'ignored' } | Should -Not -Throw
        $c.Data.Count | Should -Be 0 -Because 'empty key must not be stored'
        { Get-LruCacheValue -Cache $c -Key '' } | Should -Not -Throw
    }
}

Describe 'LruCache Statistics' {

    It 'Get-LruCacheStatistics returns correct hit rate' {
        $c = New-LruCache -MaxEntries 10 -Name 'StatsTest'
        Set-LruCacheValue -Cache $c -Key 'A' -Value 1
        $null = Get-LruCacheValue -Cache $c -Key 'A'   # hit
        $null = Get-LruCacheValue -Cache $c -Key 'B'   # miss

        $stats = Get-LruCacheStatistics -Cache $c
        $stats.Hits    | Should -Be 1
        $stats.Misses  | Should -Be 1
        $stats.HitRate | Should -Be 50.0
        $stats.Count   | Should -Be 1
    }

    It 'Registry captures all registered caches' {
        $c1 = New-LruCache -MaxEntries 5 -Name 'Reg1'
        $c2 = New-LruCache -MaxEntries 5 -Name 'Reg2'
        Register-LruCache -Cache $c1
        Register-LruCache -Cache $c2

        $all = Get-AllCacheStatistics
        $names = @($all | Where-Object { $_.Name -in @('Reg1','Reg2') } | Select-Object -ExpandProperty Name)
        $names | Should -Contain 'Reg1'
        $names | Should -Contain 'Reg2'
    }

    It 'Register-LruCache prevents duplicates' {
        $c = New-LruCache -MaxEntries 5 -Name 'NoDupe'
        Register-LruCache -Cache $c
        Register-LruCache -Cache $c  # second registration should be ignored

        $matches = @(Get-AllCacheStatistics | Where-Object Name -eq 'NoDupe')
        $matches.Count | Should -Be 1
    }
}
