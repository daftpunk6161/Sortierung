#requires -Modules Pester

Describe 'Phase3 smoke' {
    BeforeAll {
        $script:root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
        $script:modulePath = Join-Path $script:root 'dev\modules\RomCleanup.psd1'
    }

    It 'RomCleanup module manifest should import' {
        { Import-Module -Name $script:modulePath -Force -ErrorAction Stop } | Should -Not -Throw
    }

    It 'key exported commands should be available after import' {
        Import-Module -Name $script:modulePath -Force -ErrorAction Stop
        Get-Command Get-FileCategory -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
        Get-Command Get-FormatScore -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
        Get-Command Get-M3URelatedFiles -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
        Get-Command Move-ItemSafely -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
    }

    It 'module can be re-imported without parse/runtime errors' {
        { Import-Module -Name $script:modulePath -Force -ErrorAction Stop } | Should -Not -Throw
    }
}
