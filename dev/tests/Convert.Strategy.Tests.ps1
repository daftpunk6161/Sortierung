#requires -Modules Pester

Describe 'Convert strategy dispatch' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $modulesRoot = Join-Path $repoRoot 'dev\modules'

        . (Join-Path $modulesRoot 'RunspaceLifecycle.ps1')
        . (Join-Path $modulesRoot 'Convert.ps1')
    }

    It 'Get-ConvertToolStrategy should resolve known tools' {
        (Get-ConvertToolStrategy -Tool 'chdman') | Should -Be 'Invoke-ConvertStrategyChdman'
        (Get-ConvertToolStrategy -Tool 'dolphintool') | Should -Be 'Invoke-ConvertStrategyDolphinTool'
        (Get-ConvertToolStrategy -Tool '7z') | Should -Be 'Invoke-ConvertStrategy7z'
        (Get-ConvertToolStrategy -Tool 'psxtract') | Should -Be 'Invoke-ConvertStrategyPsxtract'
    }

    It 'Get-ConvertToolStrategy should return null for unknown tools' {
        (Get-ConvertToolStrategy -Tool 'unknown-tool') | Should -BeNullOrEmpty
    }

    It 'Invoke-ConvertItem should use strategy dispatch and return strategy outcome' {
        $tempDir = Join-Path $env:TEMP ("pester_convert_strategy_dispatch_{0}" -f (Get-Random))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $sourcePath = Join-Path $tempDir 'game.iso'
        'iso-data' | Out-File -LiteralPath $sourcePath -Encoding ascii -Force

        try {
            Mock -CommandName Get-ConvertToolStrategy -MockWith { return 'Invoke-ConvertStrategyChdman' }
            Mock -CommandName Invoke-ConvertStrategyChdman -MockWith {
                param([hashtable]$Context)
                return [pscustomobject]@{ Outcome = [pscustomobject]@{ Status = 'SKIP'; Path = $null; Reason = 'strategy-dispatch-test'; Details = @{} } }
            }

            $item = [pscustomobject]@{
                MainPath = $sourcePath
                Root = $tempDir
                Paths = @($sourcePath)
                Type = 'FILE'
            }
            $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

            $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { param($m) } -SevenZipPath $null

            $result.Status | Should -Be 'SKIP'
            $result.Reason | Should -Be 'strategy-dispatch-test'
            Assert-MockCalled -CommandName Get-ConvertToolStrategy -Times 1 -Exactly
            Assert-MockCalled -CommandName Invoke-ConvertStrategyChdman -Times 1 -Exactly
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
