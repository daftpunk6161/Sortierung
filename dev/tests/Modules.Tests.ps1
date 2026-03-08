#requires -Modules Pester

Describe 'Module extraction scaffolding' {

    BeforeAll {
        # Derive workspace root robustly — walk up from $PSScriptRoot until we
        # find simple_sort.ps1 (works from dev/tests/ AND dev/tests/integration/).
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root 'simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }

        # Canonical list from central module manifest
        $moduleListPath = Join-Path $script:root 'dev\modules\ModuleFileList.ps1'
        if (-not (Test-Path -LiteralPath $moduleListPath -PathType Leaf)) {
            throw 'ModuleFileList.ps1 not found in dev/modules.'
        }

        . $moduleListPath
        if (-not (Get-Command Get-RomCleanupModuleFiles -ErrorAction SilentlyContinue)) {
            throw 'Get-RomCleanupModuleFiles is not available after loading ModuleFileList.ps1.'
        }

        $script:devModules = @('ModuleFileList.ps1') + @(Get-RomCleanupModuleFiles)
    }

    It 'dev/modules/ files should exist' {
        foreach ($name in $script:devModules) {
            $path = Join-Path $script:root "dev\modules\$name"
            Test-Path -LiteralPath $path -PathType Leaf | Should -BeTrue -Because "dev/modules/$name should exist"
        }
    }

    It 'dev/modules/ files should parse without syntax errors' {
        foreach ($name in $script:devModules) {
            $path = Join-Path $script:root "dev\modules\$name"
            $tokens = $null; $errors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
            @($errors).Count | Should -Be 0 -Because "dev/modules/$name should have no parse errors"
        }
    }
}
