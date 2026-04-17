#requires -Modules Pester

Describe 'SecurityEventStream' {
    BeforeAll {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
        . (Join-Path $repoRoot 'dev\modules\FileOps.ps1')
        . (Join-Path $repoRoot 'dev\modules\SecurityEventStream.ps1')
    }

    It 'writes standardized security audit events to memory and jsonl' {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("security-events-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        try {
            [void](Initialize-SecurityEventStream -Path $path -CorrelationId 'sec-corr-1')
            Write-SecurityAuditEvent -Domain 'Auth' -Action 'ApiKeyReject' -Actor 'client' -Target '/api/runs' -Outcome 'Deny' -Detail 'Unauthorized API request.' -Source 'test' -Severity 'Medium'

            $events = @(Get-SecurityEventLog)
            @($events).Count | Should -BeGreaterThan 0
            [string]$events[-1].EventType | Should -Be 'Auth.ApiKeyReject'
            [string]$events[-1].CorrelationId | Should -Be 'sec-corr-1'

            Test-Path -LiteralPath $path -PathType Leaf | Should -BeTrue
            $line = Get-Content -LiteralPath $path -Raw
            $line | Should -Match 'Auth.ApiKeyReject'
        } finally {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }

    It 'recovers when SecurityEvent script state is missing under strict mode' {
        $events = & {
            Set-StrictMode -Version Latest

            Remove-Variable -Scope Script -Name SecurityEventLog -ErrorAction SilentlyContinue
            Remove-Variable -Scope Script -Name SecurityEventPath -ErrorAction SilentlyContinue
            Remove-Variable -Scope Script -Name SecurityCorrelationId -ErrorAction SilentlyContinue
            Remove-Variable -Scope Script -Name SecurityEventLogMaxEntries -ErrorAction SilentlyContinue

            Write-SecurityEvent -EventType 'Plugin.Loaded' -Actor 'test' -Target 'plugin.ps1' -Outcome 'Allow' -Detail 'strict-mode-check' -Source 'test' -Severity 'Low'
            @(Get-SecurityEventLog)
        }

        @($events).Count | Should -BeGreaterThan 0
    }
}
