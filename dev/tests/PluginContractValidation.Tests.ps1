#requires -Modules Pester

Describe 'Plugin contract validation' {
    BeforeAll {
        $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:ValidationScript = Join-Path $script:RepoRoot 'dev\tools\Invoke-PluginManifestValidation.ps1'
    }

    It 'validates all plugin manifests against schema contracts' {
        Test-Path -LiteralPath $script:ValidationScript -PathType Leaf | Should -BeTrue

        $result = & $script:ValidationScript
        $result | Should -Not -BeNullOrEmpty
        [int]$result.Invalid | Should -Be 0
    }
}
