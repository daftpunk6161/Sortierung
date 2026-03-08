#requires -Modules Pester

Describe 'Dedupe coverage harness' {
    BeforeAll {
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root 'simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }

        . (Join-Path $script:root 'dev\modules\RunspaceLifecycle.ps1')
        . (Join-Path $script:root 'dev\modules\Dedupe.ps1')

        if (-not (Get-Command Get-FileHashCached -ErrorAction SilentlyContinue)) {
            function Get-FileHashCached {
                param([string]$Path,[string]$HashType,[hashtable]$Cache)
                $Cache[$Path] = 'HASH'
                return 'HASH'
            }
        }
        if (-not (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue)) {
            function Get-AppStateValue { param([string]$Key,$Default) return $Default }
        }
    }

    It 'Get-ParallelClassifyThreshold respects configured lower bound of 100' {
        Mock -CommandName Get-AppStateValue -MockWith { 50 }
        Get-ParallelClassifyThreshold | Should -Be 100
    }

    It 'Get-ParallelHashThreshold respects configured lower bound of 10' {
        Mock -CommandName Get-AppStateValue -MockWith { 1 }
        Get-ParallelHashThreshold | Should -Be 10
    }

    It 'Stop-RunspaceWorkerJobsShared stops active workers and disposes them' {
        $script:stopped = $false
        $script:disposed = $false

        $psObj = New-Object psobject
        Add-Member -InputObject $psObj -MemberType ScriptMethod -Name Stop -Value { $script:stopped = $true }
        Add-Member -InputObject $psObj -MemberType ScriptMethod -Name Dispose -Value { $script:disposed = $true }

        $job = [pscustomobject]@{ PS = $psObj; Handle = $true; Done = $false }
        Stop-RunspaceWorkerJobsShared -Jobs @($job)

        $script:stopped | Should -BeTrue
        $script:disposed | Should -BeTrue
    }

    It 'Remove-RunspacePoolResourcesShared closes and disposes pool and cancel event' {
        $script:poolClosed = $false
        $script:poolDisposed = $false

        $pool = New-Object psobject
        Add-Member -InputObject $pool -MemberType ScriptMethod -Name Close -Value { $script:poolClosed = $true }
        Add-Member -InputObject $pool -MemberType ScriptMethod -Name Dispose -Value { $script:poolDisposed = $true }

        $ev = [System.Threading.ManualResetEventSlim]::new($false)
        Remove-RunspacePoolResourcesShared -Pool $pool -CancelEvent $ev

        $script:poolClosed | Should -BeTrue
        $script:poolDisposed | Should -BeTrue
    }

    It 'Invoke-PreHashFiles uses sequential UNC path when majority are network paths' {
        Mock -CommandName Get-AppStateValue -MockWith {
            param([string]$Key,$Default)
            if ($Key -eq 'ParallelHashThreshold') { return 1 }
            return $false
        }
        Mock -CommandName Get-FileHashCached -MockWith {
            param([string]$Path,[string]$HashType,[hashtable]$Cache)
            $Cache[$Path] = 'HASH'
            return 'HASH'
        }

        $logs = [System.Collections.Generic.List[string]]::new()
        $log = { param([string]$m) $logs.Add($m) }

        $files = @()
        foreach ($i in 1..10) {
            $files += [System.IO.FileInfo]::new("\\server\share\file$i.bin")
        }

        $hashes = Invoke-PreHashFiles -Files $files -HashType 'sha1' -SevenZipPath '' -Log $log

        $hashes | Should -Not -BeNull
        $hashes.Count | Should -Be 10
        (@($logs | Where-Object { $_ -like '*sequential/UNC*' })).Count | Should -BeGreaterThan 0
    }

    It 'Invoke-PreHashFiles returns null below hash-threshold' {
        Mock -CommandName Get-AppStateValue -MockWith {
            param([string]$Key,$Default)
            if ($Key -eq 'ParallelHashThreshold') { return 100 }
            return $Default
        }

        $files = @(
            [System.IO.FileInfo]::new('C:\roms\a.bin'),
            [System.IO.FileInfo]::new('C:\roms\b.iso')
        )

        $hashes = Invoke-PreHashFiles -Files $files -HashType 'sha1' -SevenZipPath '' -Log { }
        $hashes | Should -BeNullOrEmpty
    }

    It 'Get-SmartMergeSetConflictInsight returns conflict summary for differing set variants' {
        $items = @(
            [pscustomobject]@{
                Type = 'CUESET'
                MainPath = 'C:\roms\Game (En).cue'
                Paths = @('C:\roms\Game (En).cue','C:\roms\Track 01.bin','C:\roms\Track 02.bin')
            },
            [pscustomobject]@{
                Type = 'CUESET'
                MainPath = 'C:\roms\Game (JP).cue'
                Paths = @('C:\roms\Game (JP).cue','C:\roms\Track 01.bin','C:\roms\Bonus audio.bin')
            }
        )

        $winner = $items[0]
        $insight = Get-SmartMergeSetConflictInsight -GameKey 'GAME_KEY' -Items $items -Winner $winner

        $insight | Should -Not -BeNullOrEmpty
        $insight.GameKey | Should -Be 'GAME_KEY'
        $insight.VariantCount | Should -Be 2
        $insight.DiffMemberCount | Should -BeGreaterThan 0
        $insight.DiffTrackCount | Should -BeGreaterThan 0
        @($insight.LanguageHints).Count | Should -BeGreaterThan 0
    }

    It 'Get-SmartMergeSetConflictInsight returns null when no conflict exists' {
        $items = @(
            [pscustomobject]@{
                Type = 'CUESET'
                MainPath = 'C:\roms\Game.cue'
                Paths = @('C:\roms\Game.cue','C:\roms\Track 01.bin')
            },
            [pscustomobject]@{
                Type = 'CUESET'
                MainPath = 'C:\roms\Game.cue'
                Paths = @('C:\roms\Game.cue','C:\roms\Track 01.bin')
            }
        )

        $winner = $items[0]
        $insight = Get-SmartMergeSetConflictInsight -GameKey 'GAME_KEY' -Items $items -Winner $winner
        $insight | Should -BeNullOrEmpty
    }
}
