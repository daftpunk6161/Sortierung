#requires -Modules Pester

Describe 'CatchGuard' {
    BeforeAll {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
        . (Join-Path $repoRoot 'dev\modules\FileOps.ps1')
        . (Join-Path $repoRoot 'dev\modules\Settings.ps1')
        . (Join-Path $repoRoot 'dev\modules\AppState.ps1')
        . (Join-Path $repoRoot 'dev\modules\Logging.ps1')
        . (Join-Path $repoRoot 'dev\modules\CatchGuard.ps1')
        Initialize-AppState
    }

    It 'classifies timeout exceptions as Transient' {
        $ex = [System.TimeoutException]::new('timeout')
        (Resolve-CatchErrorClass -Exception $ex) | Should -Be 'Transient'
    }

    It 'creates standardized record fields' {
        $ex = [System.InvalidOperationException]::new('boom')
        $record = New-CatchGuardRecord -Module 'Dat' -Action 'LoadIndex' -Root 'C:\roms' -Exception $ex -ErrorCode 'DAT-LOAD'

        [string]$record.Module | Should -Be 'Dat'
        [string]$record.Action | Should -Be 'LoadIndex'
        [string]$record.Root | Should -Be 'C:\roms'
        [string]$record.OperationId | Should -Not -BeNullOrEmpty
        [string]$record.ExceptionType | Should -Match 'InvalidOperationException'
        [string]$record.Message | Should -Be 'boom'
        [string]$record.ErrorClass | Should -Be 'Recoverable'
    }
}
