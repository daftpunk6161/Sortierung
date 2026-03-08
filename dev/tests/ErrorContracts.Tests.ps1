#requires -Modules Pester

Describe 'ErrorContracts' {
    BeforeAll {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
        . (Join-Path $repoRoot 'dev\modules\ErrorContracts.ps1')
    }

    It 'maps error code prefixes to categories' {
        (Resolve-OperationErrorCategory -ErrorCode 'GUI-E001') | Should -Be 'UI'
        (Resolve-OperationErrorCategory -ErrorCode 'IO-READ') | Should -Be 'FileSystem'
        (Resolve-OperationErrorCategory -ErrorCode 'RUN_ERROR') | Should -Be 'Pipeline'
    }

    It 'includes category in converted operation errors' {
        $ex = [System.InvalidOperationException]::new('x')
        $err = ConvertTo-OperationError -Exception $ex -ErrorCode 'GUI-E010'

        [string]$err.ErrorCode | Should -Be 'GUI-E010'
        [string]$err.Category | Should -Be 'UI'
        [string]$err.Message | Should -Be 'x'
    }
}
