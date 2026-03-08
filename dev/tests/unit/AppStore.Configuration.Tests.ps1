#requires -Modules Pester

Describe 'AppStore und Konfigurationssystem' {
    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }

        . (Join-Path $root 'dev\modules\EventBus.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\ConfigProfiles.ps1')
        . (Join-Path $root 'dev\modules\ConfigMerge.ps1')
        . (Join-Path $root 'dev\modules\AppState.ps1')
    }

    It 'Set-AppStore + Undo/Redo sollte deterministisch funktionieren' {
        Initialize-AppState
        [void](Set-AppStore -Patch @{ CurrentPhase = 'Phase-A'; CancelRequested = $false } -Reason 'test-a')
        [void](Set-AppStore -Patch @{ CurrentPhase = 'Phase-B'; CancelRequested = $true } -Reason 'test-b')

        [string](Get-AppStateValue -Key 'CurrentPhase' -Default '') | Should -Be 'Phase-B'
        [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false) | Should -BeTrue

        (Undo-AppStore) | Should -BeTrue
        [string](Get-AppStateValue -Key 'CurrentPhase' -Default '') | Should -Be 'Phase-A'
        [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false) | Should -BeFalse

        (Redo-AppStore) | Should -BeTrue
        [string](Get-AppStateValue -Key 'CurrentPhase' -Default '') | Should -Be 'Phase-B'
        [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false) | Should -BeTrue
    }

    It 'Watch-AppStoreChange sollte EventBus-Benachrichtigung erhalten' {
        Initialize-RomEventBus
        $script:capturedKey = $null

        $subId = Watch-AppStoreChange -Handler {
            param($evt)
            if ($evt -and $evt.Data -and $evt.Data.Key) {
                $script:capturedKey = [string]$evt.Data.Key
            }
        }

        [void](Set-AppStore -Patch @{ UiPumpTick = 99 } -Reason 'watch-test')
        $script:capturedKey | Should -Be 'UiPumpTick'
        Unregister-RomEventSubscriber -SubscriptionId $subId | Out-Null
    }

    It 'Save/Restore-AppStoreRecovery sollte Snapshot persistieren und laden' {
        Initialize-AppState
        [void](Set-AppStore -Patch @{ CurrentPhase = 'Recovery-Phase' } -Reason 'recovery-write')

        $tempPath = Join-Path $TestDrive 'appstore-recovery.json'
        $written = Save-AppStoreRecovery -Path $tempPath
        Test-Path -LiteralPath $written | Should -BeTrue

        [void](Set-AppStore -Patch @{ CurrentPhase = 'Changed-After-Save' } -Reason 'recovery-overwrite')
        (Restore-AppStoreRecovery -Path $tempPath) | Should -BeTrue
        [string](Get-AppStateValue -Key 'CurrentPhase' -Default '') | Should -Be 'Recovery-Phase'
    }

    It 'Get-MergedConfiguration sollte CLI-Overrides über Environment/User/Profile priorisieren' {
        $cfg = Get-MergedConfiguration -CliOverrides @{ mode = 'Move'; logLevel = 'debug' }
        [string]$cfg.mode | Should -Be 'Move'
        [string]$cfg.logLevel | Should -Be 'debug'
    }

    It 'Get-ConfigurationDiff sollte geänderte Keys liefern' {
        $diff = @(Get-ConfigurationDiff -CliOverrides @{ mode = 'Move' })
        @($diff | Where-Object { $_.Key -eq 'mode' }).Count | Should -BeGreaterThan 0
    }
}
