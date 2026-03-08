#requires -Modules Pester
# ================================================================
#  ChdHeaderCache.Tests.ps1  –  F-04
#  Verifies that Get-ChdDiscHeaderConsole caches results and does
#  NOT perform a second file-system read on subsequent calls.
# ================================================================

Describe 'F-04: CHD Header Cache' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\LruCache.ps1')
        . (Join-Path $root 'dev\modules\Tools.ps1')
        . (Join-Path $root 'dev\modules\SetParsing.ps1')
        . (Join-Path $root 'dev\modules\Core.ps1')
        . (Join-Path $root 'dev\modules\Classification.ps1')
    }

    BeforeEach {
        # Clear module-level cache between tests
        if (Get-Variable -Name CHD_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue) {
            $script:CHD_HEADER_CACHE.Clear()
        }
    }

    function script:New-TestChdFile {
        param([string]$Name)

        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("{0}-{1}.chd" -f $Name, [Guid]::NewGuid().ToString('N'))
        $bytes = [System.Text.Encoding]::ASCII.GetBytes('MComprHD DREAMCAST TEST HEADER')
        [System.IO.File]::WriteAllBytes($path, $bytes)
        return $path
    }

    Context 'Cache-Treffer verhindert erneuten I/O' {

        It 'zweiter Aufruf mit gleichem Pfad liest aus Cache' {
            $testPath = New-TestChdFile -Name 'cache-hit'
            try {
                $result1 = Get-ChdDiscHeaderConsole -Path $testPath

                $script:CHD_HEADER_CACHE.ContainsKey($testPath) | Should -BeTrue `
                    -Because 'F-04: Ergebnis muss nach erstem Aufruf im Cache stehen'
                $result1 | Should -Be 'DC'
            } finally {
                Remove-Item -LiteralPath $testPath -Force -ErrorAction SilentlyContinue
            }
        }

        It 'zweiter Aufruf gibt identischen Wert zurück' {
            $testPath = New-TestChdFile -Name 'same-value'
            try {
                $r1 = Get-ChdDiscHeaderConsole -Path $testPath
                $r2 = Get-ChdDiscHeaderConsole -Path $testPath
                $r2 | Should -Be $r1 -Because 'Cache-Treffer muss selben Wert liefern'
            } finally {
                Remove-Item -LiteralPath $testPath -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Cache ist case-insensitive' {
            $testPath = New-TestChdFile -Name 'case-insensitive'
            try {
                $dir = Split-Path -Parent $testPath
                $leaf = Split-Path -Leaf $testPath
                $pathUpper = Join-Path $dir ($leaf.ToUpperInvariant())
                $pathLower = Join-Path $dir ($leaf.ToLowerInvariant())

                [void](Get-ChdDiscHeaderConsole -Path $pathUpper)
                $script:CHD_HEADER_CACHE.ContainsKey($pathLower) | Should -BeTrue `
                    -Because 'OrdinalIgnoreCase-Hashtable muss case-insensitiv matchen'
            } finally {
                Remove-Item -LiteralPath $testPath -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
