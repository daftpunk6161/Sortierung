#requires -Modules Pester

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'bench_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'Performance benchmark smoke' {
    It 'Benchmark placeholder should be runnable when enabled' -Skip:(-not $env:ROM_BENCHMARK) {
        $root = Join-Path $env:TEMP ("bench_smoke_" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        try {
            1..100 | ForEach-Object {
                Set-Content -LiteralPath (Join-Path $root ("game_{0}.chd" -f $_)) -Value 'x' -Encoding ascii
            }

            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $files = @(Get-ChildItem -LiteralPath $root -File -ErrorAction SilentlyContinue)
            $sw.Stop()

            $files.Count | Should -Be 100
            $maxSec = if ($env:ROM_BENCHMARK_LISTDIR_MAX_SEC) { [double]$env:ROM_BENCHMARK_LISTDIR_MAX_SEC } else { 5.0 }
            $sw.Elapsed.TotalSeconds | Should -BeLessThan $maxSec
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'CRC32 throughput baseline should be measurable when enabled' -Skip:(-not $env:ROM_BENCHMARK) {
        $tempFile = Join-Path $env:TEMP ("bench_crc32_" + [guid]::NewGuid().ToString('N') + '.bin')
        try {
            $sizeBytes = 16MB
            $buffer = New-Object byte[] (1MB)
            for ($i = 0; $i -lt $buffer.Length; $i++) { $buffer[$i] = [byte]($i % 251) }

            $fs = [System.IO.File]::Open($tempFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try {
                for ($written = 0; $written -lt $sizeBytes; $written += $buffer.Length) {
                    $toWrite = [Math]::Min($buffer.Length, $sizeBytes - $written)
                    $fs.Write($buffer, 0, $toWrite)
                }
            } finally {
                $fs.Dispose()
            }

            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $hash = Get-Crc32Hash -Path $tempFile
            $sw.Stop()

            $elapsed = [Math]::Max($sw.Elapsed.TotalSeconds, 0.001)
            $throughputMBps = [Math]::Round(($sizeBytes / 1MB) / $elapsed, 2)
            Write-Host ("CRC32 benchmark: {0} MB/s" -f $throughputMBps)

            $hash | Should -Not -BeNullOrEmpty
            $throughputMBps | Should -BeGreaterThan 0
        } finally {
            Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Archive hashing policy metrics should be measurable when enabled' -Skip:(-not $env:ROM_BENCHMARK) {
        Reset-DatArchiveStats

        $tempDir = Join-Path $env:TEMP ("bench_archive_policy_" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $archivePath = Join-Path $tempDir 'large.zip'
        'dummy' | Out-File -LiteralPath $archivePath -Encoding ascii -Force

        try {
            Mock -CommandName Get-Item -MockWith {
                param($LiteralPath)
                if ($LiteralPath -eq $archivePath) {
                    return [pscustomobject]@{ Length = 200MB; FullName = $archivePath }
                }
                return Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath
            }

            $logFn = { param($msg) }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $null = @(Get-ArchiveHashes -Path $archivePath -HashType 'SHA1' -Cache $null -SevenZipPath $null -Log $logFn -ArchiveMaxHashSizeBytes 100MB -LargeArchivePolicy 'Skip')
            $sw.Stop()

            $stats = Get-DatArchiveStats
            Write-Host ("Archive policy benchmark elapsed: {0} ms" -f [Math]::Round($sw.Elapsed.TotalMilliseconds, 1))

            $stats | Should -Not -BeNullOrEmpty
            ($stats.Keys.Count -gt 0) | Should -BeTrue
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Group-Object and Sort-Object profiling smoke should be measurable when enabled' -Skip:(-not $env:ROM_BENCHMARK) {
        $rows = New-Object System.Collections.Generic.List[psobject]
        for ($i = 0; $i -lt 20000; $i++) {
            $rows.Add([pscustomobject]@{
                Key = ('game_{0}' -f ($i % 1200))
                RegionScore = 1000 - ($i % 10)
                FormatScore = 800 + ($i % 5)
                MainPath = ('C:\ROMs\{0}\file_{1}.chd' -f ($i % 80), $i)
            }) | Out-Null
        }

        $swGroup = [System.Diagnostics.Stopwatch]::StartNew()
        $grouped = @($rows | Group-Object -Property Key)
        $swGroup.Stop()

        $swSort = [System.Diagnostics.Stopwatch]::StartNew()
        $sorted = @($rows | Sort-Object -Property `
            @{Expression='RegionScore';Descending=$true},
            @{Expression='FormatScore';Descending=$true},
            @{Expression='MainPath';Descending=$false})
        $swSort.Stop()

        Write-Host ("Group-Object benchmark: {0} ms, groups={1}" -f [Math]::Round($swGroup.Elapsed.TotalMilliseconds, 1), $grouped.Count)
        Write-Host ("Sort-Object benchmark: {0} ms, rows={1}" -f [Math]::Round($swSort.Elapsed.TotalMilliseconds, 1), $sorted.Count)

        $grouped.Count | Should -BeGreaterThan 0
        $sorted.Count | Should -Be 20000
    }

    It 'GameKey/Region throughput should stay within guardrail when enabled' -Skip:(-not $env:ROM_BENCHMARK) {
        $names = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt 20000; $i++) {
            $region = switch ($i % 4) { 0 { 'Europe' } 1 { 'USA' } 2 { 'Japan' } default { 'World' } }
            $names.Add(('Game Title {0} ({1}) (Disc {2})' -f $i, $region, (($i % 3) + 1))) | Out-Null
        }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $keys = New-Object System.Collections.Generic.List[string]
        $regions = New-Object System.Collections.Generic.List[string]
        foreach ($n in $names) {
            $keys.Add((ConvertTo-GameKey -BaseName $n)) | Out-Null
            $regions.Add((Get-RegionTag -Name $n)) | Out-Null
        }
        $sw.Stop()

        $elapsedMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1)
        $maxMs = if ($env:ROM_BENCHMARK_GAMEKEY_REGION_MAX_MS) { [double]$env:ROM_BENCHMARK_GAMEKEY_REGION_MAX_MS } else { 25000.0 }
        Write-Host ("GameKey/Region benchmark elapsed: {0} ms (max {1} ms)" -f $elapsedMs, $maxMs)

        $keys.Count | Should -Be 20000
        $regions.Count | Should -Be 20000
        $elapsedMs | Should -BeLessThan $maxMs
    }

    It '100k candidate memory gate should stay within guardrail when enabled' -Skip:(-not $env:ROM_BENCHMARK) {
        [System.GC]::Collect()
        [System.GC]::WaitForPendingFinalizers()
        [System.GC]::Collect()

        $beforeBytes = [double][System.GC]::GetTotalMemory($true)
        $sampleCount = if ($env:ROM_BENCHMARK_100K_SAMPLES) { [int]$env:ROM_BENCHMARK_100K_SAMPLES } else { 100000 }
        $maxDeltaMB = if ($env:ROM_BENCHMARK_100K_MAX_MEM_MB) { [double]$env:ROM_BENCHMARK_100K_MAX_MEM_MB } else { 450.0 }
        $maxElapsedMs = if ($env:ROM_BENCHMARK_100K_MAX_MS) { [double]$env:ROM_BENCHMARK_100K_MAX_MS } else { 120000.0 }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $rows = New-Object System.Collections.Generic.List[psobject]
        for ($i = 0; $i -lt $sampleCount; $i++) {
            $region = switch ($i % 4) { 0 { 'Europe' } 1 { 'USA' } 2 { 'Japan' } default { 'World' } }
            $rows.Add([pscustomobject]@{
                Key = ('game_{0}' -f ($i % 20000))
                RegionScore = 1000 - ($i % 10)
                FormatScore = 800 + ($i % 5)
                MainPath = ('C:\ROMs\Set{0}\Game{1} ({2}).chd' -f ($i % 400), $i, $region)
            }) | Out-Null
        }

        $grouped = @($rows | Group-Object -Property Key)
        $sorted = @($rows | Sort-Object -Property @{Expression='RegionScore';Descending=$true}, @{Expression='FormatScore';Descending=$true}, @{Expression='MainPath';Descending=$false})
        $sw.Stop()

        $afterBytes = [double][System.GC]::GetTotalMemory($true)
        $deltaMB = [Math]::Round([Math]::Max(0, ($afterBytes - $beforeBytes) / 1MB), 2)
        $elapsedMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1)
        Write-Host ("100k candidate benchmark: samples={0}, delta={1} MB, elapsed={2} ms" -f $sampleCount, $deltaMB, $elapsedMs)

        $grouped.Count | Should -BeGreaterThan 0
        $sorted.Count | Should -Be $sampleCount
        $deltaMB | Should -BeLessThan $maxDeltaMB
        $elapsedMs | Should -BeLessThan $maxElapsedMs
    }

    It '500k candidate nightly memory gate should stay within guardrail when enabled' -Skip:(-not $env:ROM_BENCHMARK_NIGHTLY) {
        [System.GC]::Collect()
        [System.GC]::WaitForPendingFinalizers()
        [System.GC]::Collect()

        $beforeBytes = [double][System.GC]::GetTotalMemory($true)
        $sampleCount = if ($env:ROM_BENCHMARK_500K_SAMPLES) { [int]$env:ROM_BENCHMARK_500K_SAMPLES } else { 500000 }
        $maxDeltaMB = if ($env:ROM_BENCHMARK_500K_MAX_MEM_MB) { [double]$env:ROM_BENCHMARK_500K_MAX_MEM_MB } else { 2200.0 }
        $maxElapsedMs = if ($env:ROM_BENCHMARK_500K_MAX_MS) { [double]$env:ROM_BENCHMARK_500K_MAX_MS } else { 600000.0 }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $rows = New-Object System.Collections.Generic.List[psobject]
        for ($i = 0; $i -lt $sampleCount; $i++) {
            $region = switch ($i % 4) { 0 { 'Europe' } 1 { 'USA' } 2 { 'Japan' } default { 'World' } }
            $rows.Add([pscustomobject]@{
                Key = ('game_{0}' -f ($i % 90000))
                RegionScore = 1000 - ($i % 10)
                FormatScore = 800 + ($i % 5)
                MainPath = ('C:\ROMs\Set{0}\Game{1} ({2}).chd' -f ($i % 800), $i, $region)
            }) | Out-Null
        }

        $grouped = @($rows | Group-Object -Property Key)
        $sorted = @($rows | Sort-Object -Property @{Expression='RegionScore';Descending=$true}, @{Expression='FormatScore';Descending=$true}, @{Expression='MainPath';Descending=$false})
        $sw.Stop()

        $afterBytes = [double][System.GC]::GetTotalMemory($true)
        $deltaMB = [Math]::Round([Math]::Max(0, ($afterBytes - $beforeBytes) / 1MB), 2)
        $elapsedMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1)
        Write-Host ("500k nightly benchmark: samples={0}, delta={1} MB, elapsed={2} ms" -f $sampleCount, $deltaMB, $elapsedMs)

        $grouped.Count | Should -BeGreaterThan 0
        $sorted.Count | Should -Be $sampleCount
        $deltaMB | Should -BeLessThan $maxDeltaMB
        $elapsedMs | Should -BeLessThan $maxElapsedMs
    }
}
