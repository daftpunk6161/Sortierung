#requires -Modules Pester
# ================================================================
#  ToolHash.Mandatory.Tests.ps1  –  F-01
#  Verifies that Test-ToolBinaryHash emits a WARN via the $Log
#  callback when tool-hashes.json is absent/empty (instead of
#  silently trusting the binary).
# ================================================================

Describe 'F-01: ToolHash – fehlende tool-hashes.json' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\AppState.ps1')
        . (Join-Path $root 'dev\modules\LruCache.ps1')
        . (Join-Path $root 'dev\modules\Tools.ps1')
    }

    Context 'Wenn $allow leer ist (kein tool-hashes.json)' {

        It 'ruft $Log mit WARN [SEC] auf' {
            $captured = [System.Collections.Generic.List[string]]::new()
            $logCallback = { param($msg) $captured.Add($msg) }

            $tmpTool = [System.IO.Path]::GetTempFileName()
            try {
                if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
                    [void](Set-AppStateValue -Key 'AllowInsecureToolHashBypass' -Value $true)
                }
                if (Get-Command Clear-ToolHashVerdictCache -ErrorAction SilentlyContinue) {
                    Clear-ToolHashVerdictCache
                }
                Mock -CommandName Get-ToolHashAllowList -MockWith { return $null }
                [void](Test-ToolBinaryHash -ToolPath $tmpTool -Log $logCallback)
            } finally {
                Remove-Item -LiteralPath $tmpTool -Force -ErrorAction SilentlyContinue
            }

            $warnLine = $captured | Where-Object { $_ -match 'WARN \[SEC\]' }
            $warnLine | Should -Not -BeNullOrEmpty -Because 'F-01 erfordert WARN wenn hash-map fehlt'
        }

        It 'gibt $true zurück (binary wird akzeptiert, aber gewarnt)' {
            $tmpTool = [System.IO.Path]::GetTempFileName()
            try {
                if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
                    [void](Set-AppStateValue -Key 'AllowInsecureToolHashBypass' -Value $true)
                }
                if (Get-Command Clear-ToolHashVerdictCache -ErrorAction SilentlyContinue) {
                    Clear-ToolHashVerdictCache
                }
                Mock -CommandName Get-ToolHashAllowList -MockWith { return $null }
                $result = Test-ToolBinaryHash -ToolPath $tmpTool -Log { }
                $result | Should -BeTrue
            } finally {
                Remove-Item -LiteralPath $tmpTool -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Wenn $allow befüllt ist' {

        It 'emittiert kein WARN für bekannte Hash-Map' {
            $captured = [System.Collections.Generic.List[string]]::new()
            $tmpTool = [System.IO.Path]::GetTempFileName()
            try {
                if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
                    [void](Set-AppStateValue -Key 'AllowInsecureToolHashBypass' -Value $false)
                }
                if (Get-Command Clear-ToolHashVerdictCache -ErrorAction SilentlyContinue) {
                    Clear-ToolHashVerdictCache
                }
                # Non-null allow-map suppresses the "missing allowlist" warning path.
                Mock -CommandName Get-ToolHashAllowList -MockWith { return @{ 'dummy.exe' = 'ABCDEF' } }
                [void](Test-ToolBinaryHash -ToolPath $tmpTool -Log { param($msg) $captured.Add($msg) })
            } finally {
                Remove-Item -LiteralPath $tmpTool -Force -ErrorAction SilentlyContinue
            }

            $warnLine = $captured | Where-Object { $_ -match 'WARN \[SEC\].*fehlt' }
            $warnLine | Should -BeNullOrEmpty -Because 'keine WARN wenn allow-map vorhanden'
        }
    }
}
