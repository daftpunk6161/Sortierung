#requires -Modules Pester

Describe 'WPF Unhandled Exception Hooks' {
    BeforeAll {
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root 'simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }

        . (Join-Path $script:root 'dev\modules\WpfHost.ps1')
        . (Join-Path $script:root 'dev\modules\SimpleSort.WpfMain.ps1')

        Initialize-WpfAssemblies
    }

    It 'Invoke-WpfUnhandledExceptionReport setzt AppState ohne Throw' {
        $script:lastState = @{}

        function Set-AppStateValue {
            param([string]$Key, $Value)
            $script:lastState[$Key] = $Value
            return $true
        }

        $env:ROMCLEANUP_TESTMODE = '1'
        {
            Invoke-WpfUnhandledExceptionReport -Exception ([System.Exception]::new('boom')) -Source 'UnitTest' -ShowDialog $true
        } | Should -Not -Throw

        $script:lastState.ContainsKey('LastUnhandledWpfException') | Should -BeTrue
        ([string]$script:lastState['LastUnhandledWpfException']) | Should -Match 'UnitTest'
        ([string]$script:lastState['LastUnhandledWpfException']) | Should -Match 'boom'

        Remove-Item Function:\Set-AppStateValue -ErrorAction SilentlyContinue
        Remove-Item Env:\ROMCLEANUP_TESTMODE -ErrorAction SilentlyContinue
    }

    It 'Register-GlobalWpfDispatcherExceptionHandler registriert Handler-Variablen' {
        $env:ROMCLEANUP_TESTMODE = '1'
        $env:ROMCLEANUP_ENABLE_GLOBAL_UNHANDLED_HOOKS = '1'

        {
            Register-GlobalWpfDispatcherExceptionHandler
        } | Should -Not -Throw

        (Get-Variable -Name WPF_DISPATCHER_UNHANDLED_HANDLER -Scope Script -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Variable -Name WPF_APPDOMAIN_UNHANDLED_HANDLER -Scope Script -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Variable -Name WPF_TASK_UNOBSERVED_HANDLER -Scope Script -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty

        Remove-Item Env:\ROMCLEANUP_TESTMODE -ErrorAction SilentlyContinue
        Remove-Item Env:\ROMCLEANUP_ENABLE_GLOBAL_UNHANDLED_HOOKS -ErrorAction SilentlyContinue
    }

    It 'Test-RomCleanupEnableGlobalUnhandledHooks ist in VSCode-Host standardmäßig aus' {
        $oldTermProgram = $env:TERM_PROGRAM
        $env:TERM_PROGRAM = 'vscode'

        (Test-RomCleanupEnableGlobalUnhandledHooks) | Should -BeFalse

        if ($null -eq $oldTermProgram) {
            Remove-Item Env:\TERM_PROGRAM -ErrorAction SilentlyContinue
        } else {
            $env:TERM_PROGRAM = $oldTermProgram
        }
    }

    It 'Test-RomCleanupEnableGlobalUnhandledHooks ist standardmäßig aus' {
        $oldTermProgram = $env:TERM_PROGRAM
        if ($null -ne $oldTermProgram) {
            Remove-Item Env:\TERM_PROGRAM -ErrorAction SilentlyContinue
        }

        (Test-RomCleanupEnableGlobalUnhandledHooks) | Should -BeFalse

        if ($null -ne $oldTermProgram) {
            $env:TERM_PROGRAM = $oldTermProgram
        }
    }

    It 'Test-WpfSuppressExceptionDialog unterdrückt Win32Exception 1816' {
        Remove-Variable -Name WPF_LAST_UNHANDLED_FINGERPRINT -Scope Script -ErrorAction SilentlyContinue
        Remove-Variable -Name WPF_LAST_UNHANDLED_AT_UTC -Scope Script -ErrorAction SilentlyContinue

        $ex = [System.ComponentModel.Win32Exception]::new(1816)
        (Test-WpfSuppressExceptionDialog -Exception $ex -Source 'DispatcherUnhandledException') | Should -BeTrue
    }

    It 'Test-WpfSuppressExceptionDialog drosselt identische Fehler innerhalb Debounce-Fenster' {
        Remove-Variable -Name WPF_LAST_UNHANDLED_FINGERPRINT -Scope Script -ErrorAction SilentlyContinue
        Remove-Variable -Name WPF_LAST_UNHANDLED_AT_UTC -Scope Script -ErrorAction SilentlyContinue

        $oldDebounce = $env:ROMCLEANUP_UNHANDLED_DIALOG_DEBOUNCE_SECONDS
        $env:ROMCLEANUP_UNHANDLED_DIALOG_DEBOUNCE_SECONDS = '30'

        try {
            $ex = [System.Exception]::new('same-error')
            (Test-WpfSuppressExceptionDialog -Exception $ex -Source 'DispatcherUnhandledException') | Should -BeFalse
            (Test-WpfSuppressExceptionDialog -Exception $ex -Source 'DispatcherUnhandledException') | Should -BeTrue
        } finally {
            if ($null -eq $oldDebounce) {
                Remove-Item Env:\ROMCLEANUP_UNHANDLED_DIALOG_DEBOUNCE_SECONDS -ErrorAction SilentlyContinue
            } else {
                $env:ROMCLEANUP_UNHANDLED_DIALOG_DEBOUNCE_SECONDS = $oldDebounce
            }
        }
    }
}
