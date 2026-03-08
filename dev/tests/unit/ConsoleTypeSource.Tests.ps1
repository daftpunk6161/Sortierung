#requires -Modules Pester

<#
  T-08: Get-ConsoleTypeSource Confidence-Konsistenz
  BUG-CS-04: Source/Confidence muss mit der tatsaechlichen Erkennungsmethode uebereinstimmen.
  Prueft, dass LAST_CONSOLE_TYPE_SOURCE direkt gesetzt wird und Get-ConsoleTypeSource konsistent ist.
#>

BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
        $root = Split-Path -Parent $root
    }
    . (Join-Path $root 'dev\modules\Settings.ps1')
    . (Join-Path $root 'dev\modules\LruCache.ps1')
    . (Join-Path $root 'dev\modules\AppState.ps1')
    . (Join-Path $root 'dev\modules\FileOps.ps1')
    . (Join-Path $root 'dev\modules\Tools.ps1')
    . (Join-Path $root 'dev\modules\SetParsing.ps1')
    . (Join-Path $root 'dev\modules\Core.ps1')
    . (Join-Path $root 'dev\modules\Classification.ps1')
}

Describe 'T-08: ConsoleTypeSource Confidence-Konsistenz (BUG-CS-04)' {

    BeforeEach {
        $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        $script:LAST_CONSOLE_TYPE_SOURCE = $null
    }

    Context 'Extension-Match → Source=EXT_MAP, Confidence=60' {
        It '.gba → Source EXT_MAP mit Confidence 60' {
            $tempDir = Join-Path $env:TEMP "pester_cts_$(Get-Random)"
            $subDir = Join-Path $tempDir 'mixed'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file = Join-Path $subDir 'game.gba'

            try {
                Get-ConsoleType -RootPath $tempDir -FilePath $file -Extension '.gba' | Out-Null
                $script:LAST_CONSOLE_TYPE_SOURCE | Should -Not -BeNullOrEmpty
                $script:LAST_CONSOLE_TYPE_SOURCE.Source | Should -Be 'EXT_MAP'
                $script:LAST_CONSOLE_TYPE_SOURCE.Confidence | Should -Be 60
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It '.nes → Source EXT_MAP, nicht DISC_HEADER' {
            $tempDir = Join-Path $env:TEMP "pester_cts_$(Get-Random)"
            $subDir = Join-Path $tempDir 'games'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file = Join-Path $subDir 'mario.nes'

            try {
                Get-ConsoleType -RootPath $tempDir -FilePath $file -Extension '.nes' | Out-Null
                $script:LAST_CONSOLE_TYPE_SOURCE.Source | Should -Be 'EXT_MAP'
                $script:LAST_CONSOLE_TYPE_SOURCE.Source | Should -Not -Be 'DISC_HEADER'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Folder-Match → Source=FOLDER, Confidence=50' {
        It 'Ordner "SNES" → Source FOLDER mit Confidence 50' {
            $tempDir = Join-Path $env:TEMP "pester_cts_$(Get-Random)"
            $subDir = Join-Path $tempDir 'SNES'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file = Join-Path $subDir 'game.zip'

            try {
                Get-ConsoleType -RootPath $tempDir -FilePath $file -Extension '.zip' | Out-Null
                $script:LAST_CONSOLE_TYPE_SOURCE | Should -Not -BeNullOrEmpty
                $script:LAST_CONSOLE_TYPE_SOURCE.Source | Should -Be 'FOLDER'
                $script:LAST_CONSOLE_TYPE_SOURCE.Confidence | Should -Be 50
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Invoke-ConsoleDetection nutzt LAST_CONSOLE_TYPE_SOURCE' {
        It 'Extension-Match → DetectionResult hat Source EXT_MAP' {
            $tempDir = Join-Path $env:TEMP "pester_cts_$(Get-Random)"
            $subDir = Join-Path $tempDir 'misc'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file = Join-Path $subDir 'game.nds'

            try {
                $result = Invoke-ConsoleDetection `
                    -FilePath $file `
                    -RootPath $tempDir `
                    -Extension '.nds' `
                    -UseDolphin $false
                $result.Console | Should -Be 'NDS'
                $result.Source | Should -Be 'EXT_MAP'
                $result.Confidence | Should -Be 60
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Folder-Match → DetectionResult hat Source FOLDER, nicht DISC_HEADER' {
            $tempDir = Join-Path $env:TEMP "pester_cts_$(Get-Random)"
            $subDir = Join-Path $tempDir 'GBA'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file = Join-Path $subDir 'game.zip'

            try {
                $result = Invoke-ConsoleDetection `
                    -FilePath $file `
                    -RootPath $tempDir `
                    -Extension '.zip' `
                    -UseDolphin $false
                $result.Console | Should -Be 'GBA'
                $result.Source | Should -Be 'FOLDER'
                $result.Confidence | Should -Be 50
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Get-ConsoleTypeSource Fallback-Konsistenz' {
        It 'Folder-Match: Get-ConsoleTypeSource gibt FOLDER zurueck' {
            $tempDir = Join-Path $env:TEMP "pester_cts_fb_$(Get-Random)"
            $subDir = Join-Path $tempDir 'NES'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            $file = Join-Path $subDir 'game.zip'

            try {
                $src = Get-ConsoleTypeSource -RootPath $tempDir -FilePath $file -Extension '.zip' -Result 'NES'
                $src.Source | Should -Be 'FOLDER'
            } finally {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Extension-Match .gba: Get-ConsoleTypeSource gibt EXT_MAP zurueck' {
            $src = Get-ConsoleTypeSource -RootPath 'C:\dummy' -FilePath 'C:\dummy\game.gba' -Extension '.gba' -Result 'GBA'
            $src.Source | Should -Be 'EXT_MAP'
            $src.Confidence | Should -Be 60
        }
    }
}
