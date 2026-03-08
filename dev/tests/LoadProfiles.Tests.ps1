#requires -Modules Pester

<#
.SYNOPSIS
  Formalized load profile benchmarks for 10k/50k/100k ROM datasets.
.DESCRIPTION
  Tests end-to-end pipeline throughput and memory behavior with realistic
  synthetic datasets at standardized sizes (10k, 50k, 100k).
  Activated via ROM_BENCHMARK=1 environment variable.
  Results are persisted for trend analysis.
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'loadprofile_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'Formalized Load Profiles (10k/50k/100k)' -Tag 'Benchmark' {

    BeforeAll {
        $script:ProfileResults = New-Object System.Collections.Generic.List[psobject]

        function New-SyntheticRomDataset {
            <#
            .SYNOPSIS  Generates a list of synthetic ROM candidates for load testing.
            #>
            param([int]$Count)

            $consoles = @('PS1','PS2','PSP','GameCube','Wii','NES','SNES','N64','GBA','NDS','Genesis','Saturn','Dreamcast','3DO','TG16')
            $regions  = @('Europe','USA','Japan','World')
            $exts     = @('.chd','.iso','.zip','.7z','.rvz','.cso')

            $items = New-Object System.Collections.Generic.List[psobject]
            for ($i = 0; $i -lt $Count; $i++) {
                $console = $consoles[$i % $consoles.Count]
                $region  = $regions[$i % $regions.Count]
                $ext     = $exts[$i % $exts.Count]
                $gameNum = [Math]::Floor($i / 4)  # ~4 regions per game
                $name    = ('Game Title {0} ({1}) (Disc {2}){3}' -f $gameNum, $region, (($i % 3) + 1), $ext)

                $gameKey = ''
                if (Get-Command ConvertTo-GameKey -ErrorAction SilentlyContinue) {
                    $gameKey = ConvertTo-GameKey -BaseName $name
                }

                $regionTag = ''
                if (Get-Command Get-RegionTag -ErrorAction SilentlyContinue) {
                    $regionTag = Get-RegionTag -Name $name
                }

                [void]$items.Add([pscustomobject]@{
                    BaseName       = [System.IO.Path]::GetFileNameWithoutExtension($name)
                    Extension      = $ext
                    MainPath       = ('C:\ROMs\{0}\{1}' -f $console, $name)
                    SizeBytes      = [long](100MB + ($i % 500MB))
                    GameKey        = $gameKey
                    RegionTag      = $regionTag
                    Console        = $console
                    Category       = 'GAME'
                    RegionScore    = 1000 - ($i % 16)
                    FormatScore    = 900 - ($i % 6)
                })
            }
            return $items
        }

        function Invoke-LoadProfileBenchmark {
            param(
                [string]$ProfileName,
                [int]$DatasetSize,
                [double]$MaxMemoryMB,
                [double]$MaxElapsedSec
            )

            [System.GC]::Collect()
            [System.GC]::WaitForPendingFinalizers()
            [System.GC]::Collect()
            $beforeBytes = [double][System.GC]::GetTotalMemory($true)

            $sw = [System.Diagnostics.Stopwatch]::StartNew()

            # Generate dataset
            $items = New-SyntheticRomDataset -Count $DatasetSize

            # Group phase
            $grouped = @($items | Group-Object -Property GameKey)

            # Sort phase (within each group, pick winners)
            $sorted = @($items | Sort-Object -Property `
                @{Expression='RegionScore';Descending=$true},
                @{Expression='FormatScore';Descending=$true},
                @{Expression='MainPath';Descending=$false})

            # Simulate select phase (pick first per group)
            $winners = New-Object System.Collections.Generic.List[psobject]
            foreach ($g in $grouped) {
                $groupSorted = @($g.Group | Sort-Object -Property @{Expression='RegionScore';Descending=$true} | Select-Object -First 1)
                if ($groupSorted.Count -gt 0) { [void]$winners.Add($groupSorted[0]) }
            }

            $sw.Stop()

            [System.GC]::Collect()
            [System.GC]::WaitForPendingFinalizers()
            $afterBytes = [double][System.GC]::GetTotalMemory($true)
            $deltaMB = [Math]::Round([Math]::Max(0, ($afterBytes - $beforeBytes) / 1MB), 2)
            $elapsedSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
            $throughput = if ($elapsedSec -gt 0) { [Math]::Round($DatasetSize / $elapsedSec, 1) } else { 0 }

            $result = [pscustomobject]@{
                Profile     = $ProfileName
                DatasetSize = $DatasetSize
                Groups      = $grouped.Count
                Winners     = $winners.Count
                ElapsedSec  = $elapsedSec
                MemoryDeltaMB = $deltaMB
                Throughput  = $throughput
                MaxMemoryMB = $MaxMemoryMB
                MaxElapsedSec = $MaxElapsedSec
                Passed      = ($deltaMB -lt $MaxMemoryMB) -and ($elapsedSec -lt $MaxElapsedSec)
            }
            [void]$script:ProfileResults.Add($result)

            Write-Host ("[LoadProfile] {0}: {1} items, {2} groups, {3} winners | {4:N1}s | {5:N0} MB | {6:N0} items/s" -f `
                $ProfileName, $DatasetSize, $grouped.Count, $winners.Count, $elapsedSec, $deltaMB, $throughput)

            return $result
        }
    }

    It '10k load profile should complete within guardrails' -Skip:(-not $env:ROM_BENCHMARK) {
        $result = Invoke-LoadProfileBenchmark -ProfileName '10k' -DatasetSize 10000 -MaxMemoryMB 150 -MaxElapsedSec 30
        $result.Passed | Should -BeTrue -Because "10k profile: ${($result.ElapsedSec)}s / ${($result.MemoryDeltaMB)} MB"
        $result.Groups | Should -BeGreaterThan 0
        $result.Winners | Should -BeGreaterThan 0
    }

    It '50k load profile should complete within guardrails' -Skip:(-not $env:ROM_BENCHMARK) {
        $result = Invoke-LoadProfileBenchmark -ProfileName '50k' -DatasetSize 50000 -MaxMemoryMB 350 -MaxElapsedSec 90
        $result.Passed | Should -BeTrue -Because "50k profile: ${($result.ElapsedSec)}s / ${($result.MemoryDeltaMB)} MB"
        $result.Groups | Should -BeGreaterThan 0
        $result.Winners | Should -BeGreaterThan 0
    }

    It '100k load profile should complete within guardrails' -Skip:(-not $env:ROM_BENCHMARK) {
        $result = Invoke-LoadProfileBenchmark -ProfileName '100k' -DatasetSize 100000 -MaxMemoryMB 600 -MaxElapsedSec 180
        $result.Passed | Should -BeTrue -Because "100k profile: ${($result.ElapsedSec)}s / ${($result.MemoryDeltaMB)} MB"
        $result.Groups | Should -BeGreaterThan 0
        $result.Winners | Should -BeGreaterThan 0
    }

    AfterAll {
        # Persist load profile results for trend analysis
        if ($script:ProfileResults.Count -gt 0) {
            $reportsDir = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'reports'
            if (Test-Path -LiteralPath $reportsDir) {
                $timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
                $report = [ordered]@{
                    Timestamp = $timestamp
                    Profiles  = @($script:ProfileResults)
                }
                $reportPath = Join-Path $reportsDir "load-profile-$timestamp.json"
                $latestPath = Join-Path $reportsDir 'load-profile-latest.json'
                try {
                    $json = $report | ConvertTo-Json -Depth 5
                    $json | Set-Content -LiteralPath $reportPath -Encoding UTF8 -Force
                    $json | Set-Content -LiteralPath $latestPath -Encoding UTF8 -Force
                    Write-Host "[LoadProfile] Report: $reportPath"
                } catch {
                    Write-Warning "Could not write load profile report: $($_.Exception.Message)"
                }
            }
        }
    }
}
