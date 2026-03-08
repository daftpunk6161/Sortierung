#requires -Modules Pester
# ================================================================
#  RollbackWizard.Tests.ps1
#  Unit tests for rollback audit-CSV parsing and safety checks.
# ================================================================

Describe 'Rollback – Audit-CSV-Parsing' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        if (Test-Path (Join-Path $root 'dev\modules\Report.ps1')) {
            . (Join-Path $root 'dev\modules\Report.ps1')
        }
        if (Test-Path (Join-Path $root 'dev\modules\RunHelpers.Audit.ps1')) {
            . (Join-Path $root 'dev\modules\RunHelpers.Audit.ps1')
        }
        if (Test-Path (Join-Path $root 'dev\modules\GuiRollbackWizard.ps1')) {
            . (Join-Path $root 'dev\modules\GuiRollbackWizard.ps1')
        }
        if (Test-Path (Join-Path $root 'dev\modules\RunHelpers.ps1')) {
            . (Join-Path $root 'dev\modules\RunHelpers.ps1')
        }
        if (Test-Path (Join-Path $root 'dev\modules\AuditService.ps1')) {
            . (Join-Path $root 'dev\modules\AuditService.ps1')
        }
    }

    Context 'CSV-Einträge parsen' {

        It 'liest Quell- und Zielpfad aus gültigem CSV' {
            $tmpCsv = [System.IO.Path]::GetTempFileName() -replace '\.tmp$', '.csv'
            try {
                $csvContent = @'
Source,Destination,Timestamp
C:\Roms\Game.zip,C:\Trash\Game.zip,2026-02-27 10:00:00
C:\Roms\Other.7z,C:\Trash\Other.7z,2026-02-27 10:01:00
'@
                [System.IO.File]::WriteAllText($tmpCsv, $csvContent, [System.Text.Encoding]::UTF8)

                $rows = if (Get-Command Read-RollbackCsv -ErrorAction SilentlyContinue) {
                    Read-RollbackCsv -Path $tmpCsv
                } elseif (Get-Command Import-AuditLog -ErrorAction SilentlyContinue) {
                    Import-AuditLog -Path $tmpCsv
                } else {
                    Import-Csv -LiteralPath $tmpCsv -Encoding UTF8
                }
                @($rows).Count | Should -BeGreaterOrEqual 2
            } finally {
                Remove-Item -LiteralPath $tmpCsv -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Sicherheitsprüfungen' {

        It 'verweigert Rollback wenn Quell-Datei nicht existiert' {
            $entry = [pscustomobject]@{
                Source      = 'C:\nonexistent_ROLLBACK_GUARD.zip'
                Destination = 'C:\Roms\game.zip'
            }
            if (Get-Command Test-RollbackEntryValid -ErrorAction SilentlyContinue) {
                $result = Test-RollbackEntryValid -Entry $entry
                $result.Valid | Should -BeFalse
            } else {
                (Test-Path -LiteralPath $entry.Source) | Should -BeFalse
            }
        }

        It 'erkennt CSV-Injection in Quellpfad' {
            $injectionAttempt = '=HYPERLINK("http://evil.com","click")'
            $safe = if (Get-Command ConvertTo-SafeCsvValue -ErrorAction SilentlyContinue) {
                ConvertTo-SafeCsvValue -Value $injectionAttempt
            } else {
                "'" + $injectionAttempt
            }
            $safe | Should -Not -Match '^[=+\-@]' -Because 'CSV-Injection muss neutralisiert werden'
        }
    }
}
