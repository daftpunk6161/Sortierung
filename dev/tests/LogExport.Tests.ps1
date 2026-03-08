#requires -Modules Pester

BeforeAll {
    $projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    . (Join-Path $projectRoot 'dev\modules\FileOps.ps1')
    . (Join-Path $projectRoot 'dev\modules\Settings.ps1')
    . (Join-Path $projectRoot 'dev\modules\AppState.ps1')
    . (Join-Path $projectRoot 'dev\modules\Logging.ps1')
    Initialize-AppState
}

Describe 'Logging export helpers' {
    It 'Write-OperationJsonlLog should emit warning and disable path when jsonl file is locked' {
        $logPath = Join-Path $env:TEMP ("operation-locked-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        $lockStream = $null
        try {
            '' | Out-File -LiteralPath $logPath -Encoding utf8 -Force
            $lockStream = [System.IO.File]::Open($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)

            Set-Variable -Scope Script -Name OperationJsonlPath -Value $logPath -Force
            [void](Set-AppStateValue -Key 'OperationJsonlPath' -Value $logPath)
            [void](Set-AppStateValue -Key 'CurrentPhase' -Value 'Test')
            [void](Set-AppStateValue -Key 'QuickTone' -Value 'idle')

            Mock -CommandName Write-Warning -MockWith { param($Message) }

            Write-OperationJsonlLog -Timestamp (Get-Date) -MessageText 'INFO: lock test' -Level 'Info'

            Assert-MockCalled -CommandName Write-Warning -Times 1 -Exactly
            [string](Get-AppStateValue -Key 'OperationJsonlPath' -Default '') | Should -BeNullOrEmpty
            [string]$script:OperationJsonlPath | Should -BeNullOrEmpty
        } finally {
            if ($lockStream) { $lockStream.Dispose() }
            Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Export-LogSnapshotText writes all lines to txt file' {
        $path = Join-Path $env:TEMP ("log-export-" + [guid]::NewGuid().ToString('N') + '.txt')
        try {
            $written = Export-LogSnapshotText -Lines @('[10:00:00] a','[10:00:01] b') -Path $path
            $written | Should -Be $path
            Test-Path -LiteralPath $path -PathType Leaf | Should -BeTrue
            $raw = Get-Content -LiteralPath $path -Raw
            $raw | Should -Match '10:00:00'
            $raw | Should -Match '10:00:01'
        } finally {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Export-LogSnapshotJsonl copies existing jsonl source when available' {
        $src = Join-Path $env:TEMP ("log-src-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        $dst = Join-Path $env:TEMP ("log-dst-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        try {
            '{"ts":"2026-02-22T10:00:00Z","phase":"","tone":"","message":"x"}' | Out-File -LiteralPath $src -Encoding utf8 -Force
            $written = Export-LogSnapshotJsonl -Lines @('[10:00:00] x') -Path $dst -SourceJsonlPath $src
            $written | Should -Be $dst
            Test-Path -LiteralPath $dst -PathType Leaf | Should -BeTrue
            (Get-Content -LiteralPath $dst -Raw) | Should -Match '"message":"x"'
        } finally {
            Remove-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $dst -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Export-LogSnapshotJsonl creates fallback jsonl from plain log lines' {
        $dst = Join-Path $env:TEMP ("log-fallback-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        try {
            $null = Export-LogSnapshotJsonl -Lines @('[11:12:13] one','plain two') -Path $dst -SourceJsonlPath $null
            Test-Path -LiteralPath $dst -PathType Leaf | Should -BeTrue
            $lines = @(Get-Content -LiteralPath $dst)
            $lines.Count | Should -BeGreaterOrEqual 2
            $obj = $lines[0] | ConvertFrom-Json
            [string]$obj.ts | Should -Not -BeNullOrEmpty
            [string]$obj.message | Should -Not -BeNullOrEmpty
        } finally {
            Remove-Item -LiteralPath $dst -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-JsonlLogRotation archives oversized file and enforces retention' {
        $basePath = Join-Path $env:TEMP ("log-rotate-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        try {
            'abcdefghij' | Out-File -LiteralPath $basePath -Encoding utf8 -Force
            Start-Sleep -Milliseconds 20
            [void](Invoke-JsonlLogRotation -Path $basePath -MaxBytes 1 -KeepFiles 1)
            Test-Path -LiteralPath $basePath -PathType Leaf | Should -BeFalse

            'klmnopqrst' | Out-File -LiteralPath $basePath -Encoding utf8 -Force
            Start-Sleep -Milliseconds 20
            [void](Invoke-JsonlLogRotation -Path $basePath -MaxBytes 1 -KeepFiles 1)

            $dir = Split-Path -Parent $basePath
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($basePath)
            $ext = [System.IO.Path]::GetExtension($basePath)
            $archives = @(Get-ChildItem -LiteralPath $dir -Filter ("{0}-*{1}" -f $baseName, $ext) -File -ErrorAction SilentlyContinue)
            $archives.Count | Should -Be 1
        } finally {
            Remove-Item -LiteralPath $basePath -Force -ErrorAction SilentlyContinue
            $dir = Split-Path -Parent $basePath
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($basePath)
            $ext = [System.IO.Path]::GetExtension($basePath)
            Get-ChildItem -LiteralPath $dir -Filter ("{0}-*{1}" -f $baseName, $ext) -File -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Write-OperationJsonlLog rotates when max size is reached' {
        $logPath = Join-Path $env:TEMP ("operation-rotate-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        try {
            [void](Set-AppStateValue -Key 'CurrentPhase' -Value 'Test')
            [void](Set-AppStateValue -Key 'QuickTone' -Value 'idle')
            [void](Set-AppStateValue -Key 'OperationJsonlMaxBytes' -Value 1)
            [void](Set-AppStateValue -Key 'OperationJsonlKeepFiles' -Value 1)

            Set-Variable -Scope Script -Name OperationJsonlPath -Value $logPath -Force
            Write-OperationJsonlLog -Timestamp (Get-Date) -MessageText 'INFO: first line' -Level 'Info'
            Write-OperationJsonlLog -Timestamp (Get-Date) -MessageText 'INFO: second line' -Level 'Info'

            Test-Path -LiteralPath $logPath -PathType Leaf | Should -BeTrue
            $currentRows = @(Get-Content -LiteralPath $logPath -ErrorAction SilentlyContinue)
            $currentRows.Count | Should -BeGreaterThan 0

            $dir = Split-Path -Parent $logPath
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($logPath)
            $ext = [System.IO.Path]::GetExtension($logPath)
            $archives = @(Get-ChildItem -LiteralPath $dir -Filter ("{0}-*{1}" -f $baseName, $ext) -File -ErrorAction SilentlyContinue)
            $gzArchives = @(Get-ChildItem -LiteralPath $dir -Filter ("{0}-*{1}.gz" -f $baseName, $ext) -File -ErrorAction SilentlyContinue)
            ($archives.Count + $gzArchives.Count) | Should -Be 1
        } finally {
            Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
            $dir = Split-Path -Parent $logPath
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($logPath)
            $ext = [System.IO.Path]::GetExtension($logPath)
            Get-ChildItem -LiteralPath $dir -Filter ("{0}-*{1}" -f $baseName, $ext) -File -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
            Get-ChildItem -LiteralPath $dir -Filter ("{0}-*{1}.gz" -f $baseName, $ext) -File -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Write-Log writes structured jsonl error context fields' {
        $logPath = Join-Path $env:TEMP ("operation-ctx-" + [guid]::NewGuid().ToString('N') + '.jsonl')
        try {
            [void](Set-AppStateValue -Key 'CurrentPhase' -Value 'Run')
            [void](Set-AppStateValue -Key 'QuickTone' -Value 'busy')
            Set-Variable -Scope Script -Name OperationJsonlPath -Value $logPath -Force

            [void](Write-Log -Level Error -Message 'operation failed' -CorrelationId 'op-123' -Module 'Dat' -Action 'LoadIndex' -Root 'C:\\roms' -ErrorClass 'Recoverable')

            $rows = @(Get-Content -LiteralPath $logPath -ErrorAction SilentlyContinue)
            $rows.Count | Should -BeGreaterThan 0

            $first = $rows[0] | ConvertFrom-Json
            [string]$first.correlationId | Should -Be 'op-123'
            [string]$first.module | Should -Be 'Dat'
            [string]$first.action | Should -Be 'LoadIndex'
            [string]$first.root | Should -Be 'C:\\roms'
            [string]$first.errorClass | Should -Be 'Recoverable'
        } finally {
            Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
        }
    }
}
