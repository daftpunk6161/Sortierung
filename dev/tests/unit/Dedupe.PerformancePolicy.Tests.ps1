#requires -Modules Pester

Describe 'Dedupe Performance Policy' {
    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Dedupe.ps1')
    }

    Context 'Get-AdaptiveWorkerCount' {
        It 'begrenzt NAS Hash Worker auf maximal 2' {
            $workers = Get-AdaptiveWorkerCount -Task Hash -Roots @('\\nas\roms') -ItemCount 5000
            $workers | Should -BeLessOrEqual 2
            $workers | Should -BeGreaterThan 0
        }

        It 'liefert mindestens 1 Worker bei kleinen Mengen' {
            $workers = Get-AdaptiveWorkerCount -Task Classify -Roots @('C:\') -ItemCount 1
            $workers | Should -BeGreaterOrEqual 1
        }
    }

    Context 'Get-HashPathsByPriority' {
        It 'priorisiert CHD vor BIN bei gleicher Größe' {
            $binPath = Join-Path $TestDrive 'game.bin'
            $chdPath = Join-Path $TestDrive 'game.chd'
            [System.IO.File]::WriteAllBytes($binPath, (1..512 | ForEach-Object { [byte]1 }))
            [System.IO.File]::WriteAllBytes($chdPath, (1..512 | ForEach-Object { [byte]1 }))
            $files = @((Get-Item $binPath), (Get-Item $chdPath))

            $sorted = Get-HashPathsByPriority -Files $files
            $sorted[0].Name | Should -Be 'game.chd'
            $sorted[1].Name | Should -Be 'game.bin'
        }

        It 'bevorzugt kleinere Dateien innerhalb gleicher Extension' {
            $smallPath = Join-Path $TestDrive 'a.iso'
            $largePath = Join-Path $TestDrive 'b.iso'
            [System.IO.File]::WriteAllBytes($smallPath, (1..128 | ForEach-Object { [byte]2 }))
            [System.IO.File]::WriteAllBytes($largePath, (1..4096 | ForEach-Object { [byte]3 }))
            $files = @((Get-Item $largePath), (Get-Item $smallPath))

            $sorted = Get-HashPathsByPriority -Files $files
            $sorted[0].Name | Should -Be 'a.iso'
        }
    }
}
