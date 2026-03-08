#requires -Modules Pester
# ================================================================
#  DiscHeaderDetect.Tests.ps1
#  Verifies that Get-DiscHeaderConsole correctly identifies console
#  platforms from binary disc image headers (ISO, IMG, BIN).
#  Platforms: GC, WII, 3DO, XBOX, DC, SAT, SCD,
#             NEOCD, PCECD, PCFX, JAGCD, CD32, FMTOWNS,
#             PS1, PS2, PSP
# ================================================================

Describe 'Get-DiscHeaderConsole – disc image header detection' {

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
        # Clear ISO header cache between tests
        if (Get-Variable -Name ISO_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue) {
            $script:ISO_HEADER_CACHE.Clear()
        }
    }

    # ── Helper: create a temp file with specific bytes ──
    function script:New-DiscTestFile {
        param(
            [string]$Name,
            [string]$Ext = '.iso',
            [int]$Size = 0x8100,
            [hashtable]$BytePatches  # offset → byte[]
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

    # ── Helper: write ASCII string at a given offset ──
    function script:Get-AsciiBytes {
        param([string]$Text)
        return [System.Text.Encoding]::ASCII.GetBytes($Text)
    }

    # ── Helper: build a PVD header (type 1 + "CD001") at a given offset ──
    function script:New-PvdBytes {
        param([string]$SystemId)
        # PVD: 0x01 "CD001" 0x01 0x00 + 32-byte System Identifier (padded with spaces)
        $pvd = New-Object byte[] 40
        $pvd[0] = 0x01
        $pvd[1] = 0x43  # C
        $pvd[2] = 0x44  # D
        $pvd[3] = 0x30  # 0
        $pvd[4] = 0x30  # 0
        $pvd[5] = 0x31  # 1
        $pvd[6] = 0x01  # version
        $pvd[7] = 0x00  # unused
        # System Identifier at offset 8, 32 bytes
        $sysBytes = Get-AsciiBytes -Text $SystemId
        $padded = New-Object byte[] 32
        for ($i = 0; $i -lt 32; $i++) { $padded[$i] = 0x20 } # spaces
        [Array]::Copy($sysBytes, 0, $padded, 0, [Math]::Min($sysBytes.Length, 32))
        [Array]::Copy($padded, 0, $pvd, 8, 32)
        return $pvd
    }

    Context 'GameCube detection' {
        It 'detects GC magic at offset 0x1C' {
            $patches = @{
                0x1C = [byte[]]@(0xC2, 0x33, 0x9F, 0x3D)
            }
            $path = New-DiscTestFile -Name 'gc' -BytePatches $patches -Size 64
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'GC'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Wii detection' {
        It 'detects Wii magic at offset 0x18' {
            $patches = @{
                0x18 = [byte[]]@(0x5D, 0x1C, 0x9E, 0xA3)
            }
            $path = New-DiscTestFile -Name 'wii' -BytePatches $patches -Size 64
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'WII'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context '3DO detection' {
        It 'detects Opera filesystem signature (0x01 + 5x 0x5A)' {
            $patches = @{
                0 = [byte[]]@(0x01, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A)
            }
            $path = New-DiscTestFile -Name '3do' -BytePatches $patches -Size 64
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be '3DO'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Dreamcast detection' {
        It 'detects SEGA SEGAKATANA at offset 0x0000 (2048-byte sector)' {
            $patches = @{ 0 = (Get-AsciiBytes 'SEGA SEGAKATANA SEGA ENTERPRISES') }
            $path = New-DiscTestFile -Name 'dc-2048' -BytePatches $patches -Size 256
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'DC'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'detects SEGA DREAMCAST at offset 0x0010 (2352-byte raw sector)' {
            $patches = @{ 0x0010 = (Get-AsciiBytes 'SEGA DREAMCAST  SEGA ENTERPRISES') }
            $path = New-DiscTestFile -Name 'dc-2352' -Ext '.bin' -BytePatches $patches -Size 256
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'DC'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Saturn detection' {
        It 'detects SEGA SATURN at offset 0x0000' {
            $patches = @{ 0 = (Get-AsciiBytes 'SEGA SATURN     SEGA ENTERPRISES') }
            $path = New-DiscTestFile -Name 'sat' -BytePatches $patches -Size 256
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'SAT'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'detects SEGASATURN at offset 0x0010 (2352-byte raw)' {
            $patches = @{ 0x0010 = (Get-AsciiBytes 'SEGASATURN                      ') }
            $path = New-DiscTestFile -Name 'sat-raw' -Ext '.img' -BytePatches $patches -Size 256
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'SAT'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Sega CD detection' {
        It 'detects SEGADISCSYSTEM at offset 0x0000' {
            $patches = @{ 0 = (Get-AsciiBytes 'SEGADISCSYSTEM  SEGA MEGA DRIVE ') }
            $path = New-DiscTestFile -Name 'scd' -BytePatches $patches -Size 256
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'SCD'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'PS1 detection (2048-byte sector ISO)' {
        It 'detects PS1 via PVD at 0x8000 with PLAYSTATION system ID' {
            $pvd = New-PvdBytes -SystemId 'PLAYSTATION'
            # Place "BOOT = cdrom:" after PVD to ensure PS1 (not PS2)
            $bootBytes = Get-AsciiBytes 'BOOT = cdrom:\SLUS_012.34;1'
            $patches = @{
                0x8000 = $pvd
                0x8100 = $bootBytes
            }
            $path = New-DiscTestFile -Name 'ps1-iso' -BytePatches $patches -Size 0x8200
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PS1'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'PS2 detection (2048-byte sector ISO)' {
        It 'detects PS2 via PVD at 0x8000 with PLAYSTATION + BOOT2 marker' {
            $pvd = New-PvdBytes -SystemId 'PLAYSTATION'
            $bootBytes = Get-AsciiBytes 'BOOT2 = cdrom0:\SLUS_201.23;1'
            $patches = @{
                0x8000 = $pvd
                0x8100 = $bootBytes
            }
            $path = New-DiscTestFile -Name 'ps2-iso' -BytePatches $patches -Size 0x8200
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PS2'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'detects PS2 via cdrom0: marker even without explicit BOOT2' {
            $pvd = New-PvdBytes -SystemId 'PLAYSTATION'
            $bootBytes = Get-AsciiBytes 'cdrom0:\SLUS_201.55;1'
            $patches = @{
                0x8000 = $pvd
                0x8100 = $bootBytes
            }
            $path = New-DiscTestFile -Name 'ps2-cdrom0' -BytePatches $patches -Size 0x8200
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PS2'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'PSP detection' {
        It 'detects PSP via PVD with PLAYSTATION + PSP GAME marker' {
            $pvd = New-PvdBytes -SystemId 'PLAYSTATION'
            $pspBytes = Get-AsciiBytes 'PSP GAME\USRDIR\EBOOT.BIN'
            $patches = @{
                0x8000 = $pvd
                0x8100 = $pspBytes
            }
            $path = New-DiscTestFile -Name 'psp-iso' -BytePatches $patches -Size 0x8200
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PSP'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'PS1 detection (2352-byte raw BIN sector)' {
        It 'detects PS1 via PVD at 0x9310 (Mode 1 raw sector)' {
            $pvd = New-PvdBytes -SystemId 'PLAYSTATION'
            $bootBytes = Get-AsciiBytes 'BOOT = cdrom:\SCUS_943.00;1'
            $patches = @{
                0x9310 = $pvd
                0x9400 = $bootBytes
            }
            $path = New-DiscTestFile -Name 'ps1-bin' -Ext '.bin' -BytePatches $patches -Size 0x9500
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PS1'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'detects PS2 via PVD at 0x9318 (Mode 2/XA raw sector)' {
            $pvd = New-PvdBytes -SystemId 'PLAYSTATION'
            $bootBytes = Get-AsciiBytes 'BOOT2 = cdrom0:\SLPS_254.89;1'
            $patches = @{
                0x9318 = $pvd
                0x9400 = $bootBytes
            }
            $path = New-DiscTestFile -Name 'ps2-bin' -Ext '.bin' -BytePatches $patches -Size 0x9500
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PS2'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'IMG extension support' {
        It 'detects Saturn from .img file' {
            $patches = @{ 0 = (Get-AsciiBytes 'SEGA SATURN     SEGA ENTERPRISES') }
            $path = New-DiscTestFile -Name 'sat-img' -Ext '.img' -BytePatches $patches -Size 256
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'SAT'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Xbox detection' {
        It 'detects Xbox via XDVDFS signature at offset 0x10000' {
            $sig = Get-AsciiBytes 'MICROSOFT*XBOX*MEDIA'
            $patches = @{ 0x10000 = $sig }
            $path = New-DiscTestFile -Name 'xbox' -BytePatches $patches -Size 0x10020
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'XBOX'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Neo Geo CD detection' {
        It 'detects Neo Geo CD via NEO-GEO keyword in boot sector' {
            $patches = @{ 0 = (Get-AsciiBytes 'NEO-GEO CD-ROM SYSTEM           ') }
            $path = New-DiscTestFile -Name 'neocd' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'NEOCD'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'detects Neo Geo CD via NEOGEO keyword' {
            $patches = @{ 0x100 = (Get-AsciiBytes 'NEOGEO SYSTEM DISC  ') }
            $path = New-DiscTestFile -Name 'neocd2' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'NEOCD'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'PC Engine CD detection' {
        It 'detects PCE-CD via PC Engine keyword in boot sector' {
            $patches = @{ 0 = (Get-AsciiBytes 'PC Engine CD-ROM SYSTEM         ') }
            $path = New-DiscTestFile -Name 'pcecd' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PCECD'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'detects PCE-CD via NEC HOME ELECTRONICS keyword' {
            $patches = @{ 0x800 = (Get-AsciiBytes 'NEC HOME ELECTRONICS            ') }
            $path = New-DiscTestFile -Name 'pcecd2' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PCECD'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'PC-FX detection' {
        It 'detects PC-FX via PC-FX:Hu_CD header' {
            $patches = @{ 0 = (Get-AsciiBytes 'PC-FX:Hu_CD-ROM                 ') }
            $path = New-DiscTestFile -Name 'pcfx' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'PCFX'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Jaguar CD detection' {
        It 'detects Jaguar CD via ATARI JAGUAR keyword' {
            $patches = @{ 0 = (Get-AsciiBytes 'ATARI JAGUAR CD BOOT SECTOR     ') }
            $path = New-DiscTestFile -Name 'jagcd' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'JAGCD'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Amiga CD32 detection' {
        It 'detects CD32 via AMIGA BOOT keyword' {
            $patches = @{ 0 = (Get-AsciiBytes 'AMIGA BOOT DISC COMMODORE       ') }
            $path = New-DiscTestFile -Name 'cd32' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'CD32'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'detects CD32 via CD32 keyword' {
            $patches = @{ 0x200 = (Get-AsciiBytes 'CD32 GAME DATA                  ') }
            $path = New-DiscTestFile -Name 'cd32-2' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'CD32'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'FM Towns detection (boot sector)' {
        It 'detects FM Towns via FM TOWNS keyword in boot sector' {
            $patches = @{ 0 = (Get-AsciiBytes 'FM TOWNS SYSTEM DISC BOOT       ') }
            $path = New-DiscTestFile -Name 'fmtowns-boot' -BytePatches $patches -Size 8192
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'FMTOWNS'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'FM Towns detection (PVD system identifier)' {
        It 'detects FM Towns via PVD system identifier FM-TOWNS' {
            $pvd = New-PvdBytes -SystemId 'FM-TOWNS'
            $patches = @{ 0x8000 = $pvd }
            $path = New-DiscTestFile -Name 'fmtowns-pvd' -BytePatches $patches -Size 0x8100
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'FMTOWNS'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'No match' {
        It 'returns $null for unrecognised binary content' {
            $patches = @{ 0 = (Get-AsciiBytes 'NOTAKNOWNFORMAT RANDOMDATA12345') }
            $path = New-DiscTestFile -Name 'unknown' -BytePatches $patches -Size 128
            try {
                Get-DiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'returns $null for missing file' {
            Get-DiscHeaderConsole -Path 'C:\nonexistent\fake.iso' | Should -BeNullOrEmpty
        }

        It 'returns $null for file smaller than 32 bytes' {
            $path = Join-Path ([System.IO.Path]::GetTempPath()) ("tiny-{0}.iso" -f [Guid]::NewGuid().ToString('N'))
            [System.IO.File]::WriteAllBytes($path, (New-Object byte[] 16))
            try {
                Get-DiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Cache behaviour' {
        It 'caches detection result in ISO_HEADER_CACHE' {
            $patches = @{ 0 = (Get-AsciiBytes 'SEGA SEGAKATANA SEGA ENTERPRISES') }
            $path = New-DiscTestFile -Name 'cache-test' -BytePatches $patches -Size 256
            try {
                Get-DiscHeaderConsole -Path $path | Should -Be 'DC'
                $script:ISO_HEADER_CACHE.ContainsKey($path) | Should -BeTrue
                $script:ISO_HEADER_CACHE[$path] | Should -Be 'DC'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'caches $null for unrecognised files' {
            $patches = @{ 0 = (Get-AsciiBytes 'RANDOMDATA') }
            $path = New-DiscTestFile -Name 'cache-null' -BytePatches $patches -Size 128
            try {
                $r = Get-DiscHeaderConsole -Path $path
                $r | Should -BeNullOrEmpty
                $script:ISO_HEADER_CACHE.ContainsKey($path) | Should -BeTrue
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'returns cached value on second call' {
            $patches = @{
                0x18 = [byte[]]@(0x5D, 0x1C, 0x9E, 0xA3)
            }
            $path = New-DiscTestFile -Name 'cache-reuse' -BytePatches $patches -Size 64
            try {
                $r1 = Get-DiscHeaderConsole -Path $path
                $r2 = Get-DiscHeaderConsole -Path $path
                $r2 | Should -Be $r1
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Reset-ClassificationCaches clears ISO header cache' {
        It 'clears ISO_HEADER_CACHE' {
            $script:ISO_HEADER_CACHE['test-key'] = 'PS1'
            Reset-ClassificationCaches
            $script:ISO_HEADER_CACHE.Count | Should -Be 0
        }
    }
}

Describe 'Get-ConsoleType – disc header integration for .img/.bin' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\LruCache.ps1')
        . (Join-Path $root 'dev\modules\Tools.ps1')
        . (Join-Path $root 'dev\modules\SetParsing.ps1')
        . (Join-Path $root 'dev\modules\Core.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Dat.ps1')
        . (Join-Path $root 'dev\modules\Classification.ps1')
    }

    BeforeEach {
        Reset-ClassificationCaches
    }

    It 'classifies .img file via disc header when folder gives no hint' {
        Mock -CommandName Get-DiscHeaderConsole -MockWith { return 'SAT' }

        $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gctype-{0}" -f [Guid]::NewGuid().ToString('N'))
        New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null
        $filePath = Join-Path $tempRoot 'SomeGame.img'
        try {
            $result = Get-ConsoleType -RootPath $tempRoot -FilePath $filePath -Extension '.img'
            $result | Should -Be 'SAT'
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'classifies .bin file via disc header when folder gives no hint' {
        Mock -CommandName Get-DiscHeaderConsole -MockWith { return 'PS1' }

        $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gctype-{0}" -f [Guid]::NewGuid().ToString('N'))
        New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null
        $filePath = Join-Path $tempRoot 'CoolGame.bin'
        try {
            $result = Get-ConsoleType -RootPath $tempRoot -FilePath $filePath -Extension '.bin'
            $result | Should -Be 'PS1'
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Get-ChdDiscHeaderConsole – CHD metadata keyword detection' {

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
        if (Get-Variable -Name CHD_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue) {
            $script:CHD_HEADER_CACHE.Clear()
        }
    }

    # ── Helper: create a CHD file with MComprHD magic + embedded text ──
    function script:New-ChdTestFile {
        param(
            [string]$Name,
            [string]$EmbeddedText,
            [int]$Size = 65536
        )
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("{0}-{1}.chd" -f $Name, [Guid]::NewGuid().ToString('N'))
        $buffer = New-Object byte[] $Size
        # CHD magic "MComprHD" at offset 0
        $magic = [System.Text.Encoding]::ASCII.GetBytes('MComprHD')
        [Array]::Copy($magic, 0, $buffer, 0, $magic.Length)
        # Embed platform text at offset 256 (safe padding from header)
        if ($EmbeddedText) {
            $textBytes = [System.Text.Encoding]::ASCII.GetBytes($EmbeddedText)
            $off = [Math]::Min(256, $Size - $textBytes.Length)
            [Array]::Copy($textBytes, 0, $buffer, $off, $textBytes.Length)
        }
        [System.IO.File]::WriteAllBytes($path, $buffer)
        return $path
    }

    Context 'Xbox CHD detection' {
        It 'detects Xbox via MICROSOFT*XBOX*MEDIA in CHD metadata' {
            $path = New-ChdTestFile -Name 'xbox-chd' -EmbeddedText 'MICROSOFT*XBOX*MEDIA'
            try {
                Get-ChdDiscHeaderConsole -Path $path | Should -Be 'XBOX'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'PC-FX CHD detection' {
        It 'detects PC-FX via PC-FX:Hu_CD in CHD metadata' {
            $path = New-ChdTestFile -Name 'pcfx-chd' -EmbeddedText 'PC-FX:Hu_CD-ROM SYSTEM'
            try {
                Get-ChdDiscHeaderConsole -Path $path | Should -Be 'PCFX'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'FM Towns CHD detection' {
        It 'detects FM Towns via FM TOWNS in CHD metadata' {
            $path = New-ChdTestFile -Name 'fmtowns-chd' -EmbeddedText 'FM TOWNS MARTY GAME DISC'
            try {
                Get-ChdDiscHeaderConsole -Path $path | Should -Be 'FMTOWNS'
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'CHD no match' {
        It 'returns $null for CHD without platform keywords' {
            $path = New-ChdTestFile -Name 'unknown-chd' -EmbeddedText 'SOME RANDOM DATA'
            try {
                Get-ChdDiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }

        It 'returns $null for non-CHD file' {
            $path = Join-Path ([System.IO.Path]::GetTempPath()) ("not-chd-{0}.chd" -f [Guid]::NewGuid().ToString('N'))
            $buffer = New-Object byte[] 512
            $wrongMagic = [System.Text.Encoding]::ASCII.GetBytes('NOTACHD!')
            [Array]::Copy($wrongMagic, 0, $buffer, 0, $wrongMagic.Length)
            [System.IO.File]::WriteAllBytes($path, $buffer)
            try {
                Get-ChdDiscHeaderConsole -Path $path | Should -BeNullOrEmpty
            } finally {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
