#requires -Modules Pester

<#
  TEST-08: Disc Header IO-Fehler Tests
  Validiert, dass Get-DiscHeaderConsole bei defektem Input kein Crash
  verursacht und korrekt $null zurueckgibt.
#>

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

    function script:New-DiscTestFile {
        param(
            [string]$Name,
            [string]$Ext = '.iso',
            [int]$Size = 0x8100,
            [hashtable]$BytePatches
        )
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("{0}-{1}{2}" -f $Name, [Guid]::NewGuid().ToString('N'), $Ext)
        $buffer = New-Object byte[] $Size
        if ($BytePatches) {
            foreach ($kv in $BytePatches.GetEnumerator()) {
                $off = [int]$kv.Key
                $bytes = [byte[]]$kv.Value
                [Array]::Copy($bytes, 0, $buffer, $off, $bytes.Length)
            }
        }
        [System.IO.File]::WriteAllBytes($path, $buffer)
        return $path
    }
}

Describe 'TEST-08: Disc Header IO-Fehler Handling' {

    BeforeEach {
        if (Get-Variable -Name ISO_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue) {
            $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        }
    }

    Context 'Fehlende oder ungueltige Dateien' {
        It 'Nicht-existierende Datei -> $null' {
            Get-DiscHeaderConsole -Path 'C:\nonexistent\does_not_exist.iso' | Should -BeNullOrEmpty
        }

        It 'Leerer Pfad -> $null' {
            Get-DiscHeaderConsole -Path '' | Should -BeNullOrEmpty
        }

        It 'Null Pfad -> $null' {
            Get-DiscHeaderConsole -Path $null | Should -BeNullOrEmpty
        }

        It 'Datei kleiner als 32 Bytes -> $null' {
            $path = New-DiscTestFile -Name 'tiny' -Size 16
            try {
                Get-DiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Genau 0 Bytes -> $null' {
            $path = Join-Path ([System.IO.Path]::GetTempPath()) ("empty-{0}.iso" -f [Guid]::NewGuid().ToString('N'))
            [System.IO.File]::WriteAllBytes($path, [byte[]]@())
            try {
                Get-DiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Korrupte Header (zufaellige Bytes)' {
        It 'Zufallsbytes -> $null (kein Platform-Match)' {
            $rng = [System.Random]::new(42)
            $buffer = New-Object byte[] 0x8100
            $rng.NextBytes($buffer)
            $path = Join-Path ([System.IO.Path]::GetTempPath()) ("random-{0}.iso" -f [Guid]::NewGuid().ToString('N'))
            [System.IO.File]::WriteAllBytes($path, $buffer)
            try {
                Get-DiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Alle Nullbytes -> $null' {
            $path = New-DiscTestFile -Name 'zeros' -Size 0x8100
            try {
                Get-DiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Alle 0xFF Bytes -> $null' {
            $path = Join-Path ([System.IO.Path]::GetTempPath()) ("ff-{0}.iso" -f [Guid]::NewGuid().ToString('N'))
            $buffer = New-Object byte[] 0x8100
            for ($i = 0; $i -lt $buffer.Length; $i++) { $buffer[$i] = 0xFF }
            [System.IO.File]::WriteAllBytes($path, $buffer)
            try {
                Get-DiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Partial Magic (ungueltige Header-Fragmente)' {
        It 'Partial GC magic (3 von 4 Bytes) -> kein GC Match' {
            # GC magic at 0x1C: C2 33 9F 3D - hier nur 3 Bytes
            $path = New-DiscTestFile -Name 'partial-gc' -Size 0x8100 -BytePatches @{
                0x1C = [byte[]]@(0xC2, 0x33, 0x9F, 0x00)
            }
            try {
                Get-DiscHeaderConsole -Path $path | Should -Not -Be 'GC'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Partial Wii magic (3 von 4 Bytes) -> kein WII Match' {
            # Wii magic at 0x18: 5D 1C 9E A3 - hier nur 3 Bytes
            $path = New-DiscTestFile -Name 'partial-wii' -Size 0x8100 -BytePatches @{
                0x18 = [byte[]]@(0x5D, 0x1C, 0x9E, 0x00)
            }
            try {
                Get-DiscHeaderConsole -Path $path | Should -Not -Be 'WII'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Partial 3DO magic (4 von 6 Bytes) -> kein 3DO Match' {
            # 3DO: 01 5A 5A 5A 5A 5A - hier nur 4 Bytes
            $path = New-DiscTestFile -Name 'partial-3do' -Size 0x8100 -BytePatches @{
                0x00 = [byte[]]@(0x01, 0x5A, 0x5A, 0x5A, 0x00, 0x00)
            }
            try {
                Get-DiscHeaderConsole -Path $path | Should -Not -Be '3DO'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Korrekte Header (Positiv-Kontrolle)' {
        It 'GC magic -> GC' {
            $path = New-DiscTestFile -Name 'gc-valid' -Size 0x8100 -BytePatches @{
                0x1C = [byte[]]@(0xC2, 0x33, 0x9F, 0x3D)
            }
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'GC'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Wii magic -> WII' {
            $path = New-DiscTestFile -Name 'wii-valid' -Size 0x8100 -BytePatches @{
                0x18 = [byte[]]@(0x5D, 0x1C, 0x9E, 0xA3)
            }
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'WII'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It '3DO magic -> 3DO' {
            $path = New-DiscTestFile -Name '3do-valid' -Size 0x8100 -BytePatches @{
                0x00 = [byte[]]@(0x01, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A)
            }
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be '3DO'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Header Caching' {
        It 'Zweiter Aufruf nutzt Cache (schneller als erster)' {
            $path = New-DiscTestFile -Name 'cache-test' -Size 0x8100 -BytePatches @{
                0x1C = [byte[]]@(0xC2, 0x33, 0x9F, 0x3D)
            }
            try {
                $result1 = Get-DiscHeaderConsole -Path $path
                $result1 | Should -Be 'GC'

                # Zweiter Aufruf — Cache-Hit (Datei existiert noch)
                $result2 = Get-DiscHeaderConsole -Path $path
                $result2 | Should -Be 'GC'

                # Cache sollte den Eintrag enthalten
                $script:ISO_HEADER_CACHE.ContainsKey($path) | Should -BeTrue
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Cache-Reset erzwingt Neu-Lesen' {
            $path = New-DiscTestFile -Name 'cache-reset' -Size 0x8100 -BytePatches @{
                0x1C = [byte[]]@(0xC2, 0x33, 0x9F, 0x3D)
            }
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'GC'

                # Cache leeren
                $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

                # Datei aendern zu Wii
                $buffer = New-Object byte[] 0x8100
                $buffer[0x18] = 0x5D; $buffer[0x19] = 0x1C; $buffer[0x1A] = 0x9E; $buffer[0x1B] = 0xA3
                [System.IO.File]::WriteAllBytes($path, $buffer)

                Get-DiscHeaderConsole -Path $path | Should -Be 'WII'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'CHD Header Fehler' {
        It 'Nicht-existierende CHD-Datei -> $null' {
            Get-ChdDiscHeaderConsole -Path 'C:\nonexistent\fake.chd' | Should -BeNullOrEmpty
        }

        It 'Leerer Pfad -> $null' {
            Get-ChdDiscHeaderConsole -Path '' | Should -BeNullOrEmpty
        }

        It 'Datei ohne CHD-Platform-Keywords -> $null' {
            $path = Join-Path ([System.IO.Path]::GetTempPath()) ("nochd-{0}.chd" -f [Guid]::NewGuid().ToString('N'))
            $buffer = New-Object byte[] 1024
            [System.IO.File]::WriteAllBytes($path, $buffer)
            try {
                Get-ChdDiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
