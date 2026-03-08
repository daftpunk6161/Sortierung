#requires -Modules Pester

Describe 'Phase2 smoke' {
    BeforeAll {
        $script:root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    }

    It 'core architecture files should exist' {
        Test-Path -LiteralPath (Join-Path $script:root 'simple_sort.ps1') -PathType Leaf | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $script:root 'dev\modules\RomCleanup.psm1') -PathType Leaf | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $script:root 'dev\modules\RomCleanup.psd1') -PathType Leaf | Should -BeTrue
    }

    It 'module files should parse without syntax errors' {
        $modFiles = @(
            'Core.ps1','Classification.ps1','FormatScoring.ps1','SetParsing.ps1','FileOps.ps1','RunHelpers.ps1'
        )
        foreach ($name in $modFiles) {
            $path = Join-Path $script:root ("dev\\modules\\{0}" -f $name)
            $tokens = $null
            $errors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
            @($errors).Count | Should -Be 0 -Because "$name should parse"
        }
    }

    It 'test pipeline script should exist and be callable' {
        $pipeline = Join-Path $script:root 'dev\tools\pipeline\Invoke-TestPipeline.ps1'
        Test-Path -LiteralPath $pipeline -PathType Leaf | Should -BeTrue
    }

    It 'new pipeline parameters should be present' {
        $pipeline = Join-Path $script:root 'dev\tools\pipeline\Invoke-TestPipeline.ps1'
        $cmd = Get-Command -Name $pipeline -ErrorAction Stop
        $cmd.Parameters.ContainsKey('Coverage') | Should -BeTrue
        $cmd.Parameters.ContainsKey('CoverageTarget') | Should -BeTrue
        $cmd.Parameters.ContainsKey('BenchmarkGate') | Should -BeTrue
    }
}
