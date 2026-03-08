#requires -Modules Pester

Describe 'Ops Bundle Export' {
    BeforeAll {
        $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:ModulesRoot = Join-Path $script:RepoRoot 'dev\modules'

        . (Join-Path $script:ModulesRoot 'FileOps.ps1')
        . (Join-Path $script:ModulesRoot 'Settings.ps1')
        . (Join-Path $script:ModulesRoot 'OpsBundle.ps1')
    }

    It 'exports a zip bundle with manifest and snapshots' {
        $work = Join-Path $env:TEMP ("opsbundle-test-{0}" -f (Get-Random))
        $reportsDir = Join-Path $work 'reports'
        $auditDir = Join-Path $work 'audit-logs'
        $targetZip = Join-Path $work 'bundle.zip'

        try {
            [void](New-Item -ItemType Directory -Path $reportsDir -Force)
            [void](New-Item -ItemType Directory -Path $auditDir -Force)

            'report-data' | Out-File -LiteralPath (Join-Path $reportsDir 'test-report.md') -Encoding utf8 -Force
            '{"ok":true}' | Out-File -LiteralPath (Join-Path $reportsDir 'test-report.json') -Encoding utf8 -Force
            'a,b' | Out-File -LiteralPath (Join-Path $auditDir 'audit.csv') -Encoding utf8 -Force

            $oldLocation = Get-Location
            Set-Location -LiteralPath $work
            try {
                $result = Export-OpsBundle -DestinationZipPath $targetZip -SettingsSnapshot @{ sample = 'value' } -RulesSnapshot @{ OrderedText = 'EU|\(EU\)' }
            } finally {
                Set-Location -LiteralPath $oldLocation
            }

            $result | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath $result.Path | Should -BeTrue
            $result.FileCount | Should -BeGreaterThan 0

            $extractDir = Join-Path $work 'extract'
            Expand-Archive -LiteralPath $result.Path -DestinationPath $extractDir -Force

            $manifest = @(Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'manifest.json' | Select-Object -First 1)
            $manifest.Count | Should -Be 1

            $manifestObj = Get-Content -LiteralPath $manifest[0].FullName -Raw | ConvertFrom-Json
            [string]$manifestObj.schemaVersion | Should -Be 'romcleanup-ops-bundle-v1'
            [int]$manifestObj.fileCount | Should -BeGreaterThan 0

            (@(Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'settings.snapshot.json')).Count | Should -BeGreaterThan 0
            (@(Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'rules.snapshot.json')).Count | Should -BeGreaterThan 0
        } finally {
            if (Test-Path -LiteralPath $work) {
                Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
