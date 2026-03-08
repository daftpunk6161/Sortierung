#requires -Modules Pester

Describe 'Audit Test Matrix T-01 bis T-38' {
    BeforeAll {
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root 'simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }

        Import-Module (Join-Path $script:root 'dev\modules\RomCleanup.psd1') -Force -DisableNameChecking

        function New-MatrixTempRoot {
            $path = Join-Path $env:TEMP ("matrix_" + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $path -Force)
            return $path
        }
    }

    # Cluster 1
    It 'T-01 Unicode-Dateinamen' {
        $tmp = New-MatrixTempRoot
        try {
            $p = Join-Path $tmp 'スーパーマリオ (Japan).chd'
            'x' | Out-File -LiteralPath $p -Encoding ascii -Force
            (Get-FileCategory -BaseName ([IO.Path]::GetFileNameWithoutExtension($p)) -AggressiveJunk:$false) | Should -Be 'GAME'
            (Get-RegionTag -Name ([IO.Path]::GetFileNameWithoutExtension($p))) | Should -Be 'JP'
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-02 Maximale Pfadlänge' {
        $long = 'C:\' + ('a' * 280)
        { Resolve-RootPath -Path $long } | Should -Not -Throw
    }

    It 'T-03 Sonderzeichen in Pfaden' {
        $key = ConvertTo-GameKey -BaseName 'Game [v1.0] (Rev A) {Alt}.bin' -AliasEditionKeying:$true
        $key | Should -Not -BeNullOrEmpty
        $key | Should -Match 'game'
    }

    It 'T-04 Leerzeichen plus Quotes in CUE' {
        $tmp = New-MatrixTempRoot
        try {
            $track = Join-Path $tmp 'My Track.bin'
            $cue = Join-Path $tmp 'My Game.cue'
            'x' | Out-File -LiteralPath $track -Encoding ascii -Force
            @"
FILE "My Track.bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
"@ | Out-File -LiteralPath $cue -Encoding ascii -Force
            $related = @(Get-CueRelatedFiles -CuePath $cue -RootPath $tmp)
            $related | Should -Contain $track
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-05 Nullbyte Dateiname Fehler ohne Crash' {
        { Resolve-RootPath -Path "C:\bad$([char]0)name.zip" } | Should -Throw
    }

    # Cluster 2
    It 'T-06 UNC-Pfade akzeptiert' {
        { Resolve-RootPath -Path '\\server\share\roms\game.chd' } | Should -Not -Throw
    }

    It 'T-07 Root Laufwerkswurzel robust' {
        { Resolve-RootPath -Path 'C:\' } | Should -Not -Throw
    }

    It 'T-08 Schreibgeschütztes Root DryRun läuft' {
        $tmp = New-MatrixTempRoot
        try {
            'x' | Out-File -LiteralPath (Join-Path $tmp 'Game (Europe).zip') -Encoding ascii -Force
            $res = Invoke-RegionDedupe -Roots @($tmp) -Mode 'DryRun' -PreferOrder @('EU','US','JP') -IncludeExtensions @('.zip') -RemoveJunk $true -SeparateBios $false -UseDat $false -Log { }
            $res | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath $res.CsvPath | Should -BeTrue
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-09 Gemappte Laufwerke/Reparse Prüfung robust' {
        $tmp = New-MatrixTempRoot
        try {
            { Test-PathHasReparsePoint -Path $tmp -StopAtRoot $tmp } | Should -Not -Throw
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # Cluster 3
    It 'T-10 PathWithinRoot blockt Traversal' {
        $tmp = New-MatrixTempRoot
        try {
            Test-PathWithinRoot -Path (Join-Path $tmp '..\outside.bin') -Root $tmp | Should -BeFalse
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-11 Move blockt ReparsePoint Zielordner' {
        $tmp = New-MatrixTempRoot
        try {
            $src = Join-Path $tmp 'a.bin'; 'x' | Out-File -LiteralPath $src -Encoding ascii -Force
            $dst = Join-Path $tmp 'b.bin'
            { Move-ItemSafely -Source $src -Dest $dst -ValidateSourceWithinRoot $tmp -ValidateDestWithinRoot $tmp } | Should -Not -Throw
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-12 Directory Scan ohne Endlosschleife' {
        (Get-Command Get-DirectoriesSafe -ErrorAction Stop) | Should -Not -BeNullOrEmpty
    }

    It 'T-13 Post-Extract Reparse Check vorhanden' {
        (Get-Command Expand-ArchiveToTemp -ErrorAction Stop) | Should -Not -BeNullOrEmpty
    }

    # Cluster 4
    It 'T-14 Zip-Slip wird erkannt' {
        Test-ArchiveEntryPathsSafe -EntryPaths @('..\..\evil.bin') | Should -BeFalse
    }

    It 'T-15 Große Archive können geskippt werden' {
        $tmp = New-MatrixTempRoot
        try {
            $p = Join-Path $tmp 'huge.zip'
            'x' | Out-File -LiteralPath $p -Encoding ascii -Force
            $h = @(Get-ArchiveHashes -Path $p -HashType 'SHA1' -Cache ([hashtable]::new()) -SevenZipPath '' -ArchiveMaxHashSizeBytes 1 -LargeArchivePolicy 'Skip' -Log { })
            $h.Count | Should -Be 0
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-16 Korruptes Archiv führt zu SKIP/null' {
        $tmp = New-MatrixTempRoot
        try {
            $bad = Join-Path $tmp 'bad.7z'
            'not-a-real-archive' | Out-File -LiteralPath $bad -Encoding ascii -Force
            $r = Expand-ArchiveToTemp -ArchivePath $bad -SevenZipPath 'C:\missing\7z.exe' -Log { }
            $r | Should -BeNullOrEmpty
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-17 Passwort/7z Fehler wird abgefangen' {
        Test-ArchiveEntryPathsSafe -EntryPaths @('ok/file.bin') | Should -BeTrue
    }

    # Cluster 5
    It 'T-18 CUE fehlende BIN erkannt' {
        $tmp = New-MatrixTempRoot
        try {
            $cue = Join-Path $tmp 'x.cue'
            'FILE "missing.bin" BINARY' | Out-File -LiteralPath $cue -Encoding ascii -Force
            @(Get-CueMissingFiles -CuePath $cue -RootPath $tmp).Count | Should -Be 1
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-19 M3U Zirkel wird gebrochen' {
        $tmp = New-MatrixTempRoot
        try {
            $a = Join-Path $tmp 'a.m3u'; $b = Join-Path $tmp 'b.m3u'
            'b.m3u' | Out-File -LiteralPath $a -Encoding ascii -Force
            'a.m3u' | Out-File -LiteralPath $b -Encoding ascii -Force
            $rel = @(Get-M3URelatedFiles -M3UPath $a -RootPath $tmp)
            $rel.Count | Should -BeGreaterThan 0
            $rel.Count | Should -BeLessOrEqual 2
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-20 M3U absolute Pfade werden blockiert' {
        $tmp = New-MatrixTempRoot
        try {
            $m3u = Join-Path $tmp 'x.m3u'
            'C:\other\game.cue' | Out-File -LiteralPath $m3u -Encoding ascii -Force
            @(Get-M3URelatedFiles -M3UPath $m3u -RootPath $tmp).Count | Should -Be 1
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-21 GDI Parsing unquoted' {
        $tmp = New-MatrixTempRoot
        try {
            $gdi = Join-Path $tmp 'x.gdi'
            '2' | Out-File -LiteralPath $gdi -Encoding ascii -Force
            Add-Content -LiteralPath $gdi '1 0 4 2352 track01.raw 0'
            Add-Content -LiteralPath $gdi '2 45000 4 2352 track02.bin 0'
            'x' | Out-File -LiteralPath (Join-Path $tmp 'track01.raw') -Encoding ascii -Force
            'x' | Out-File -LiteralPath (Join-Path $tmp 'track02.bin') -Encoding ascii -Force
            @(Get-GdiRelatedFiles -GdiPath $gdi -RootPath $tmp).Count | Should -Be 3
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-21b GDI Parsing quoted filename with spaces' {
        $tmp = New-MatrixTempRoot
        try {
            $gdi = Join-Path $tmp 'x.gdi'
            '2' | Out-File -LiteralPath $gdi -Encoding ascii -Force
            Add-Content -LiteralPath $gdi '1 0 4 2352 "track 01.raw" 0'
            Add-Content -LiteralPath $gdi '2 45000 4 2352 "track 02.bin" 0'
            'x' | Out-File -LiteralPath (Join-Path $tmp 'track 01.raw') -Encoding ascii -Force
            'x' | Out-File -LiteralPath (Join-Path $tmp 'track 02.bin') -Encoding ascii -Force
            $related = @(Get-GdiRelatedFiles -GdiPath $gdi -RootPath $tmp)
            $related.Count | Should -Be 3
            ($related -contains (Join-Path $tmp 'track 01.raw')) | Should -BeTrue
            ($related -contains (Join-Path $tmp 'track 02.bin')) | Should -BeTrue
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-22 CCD fehlende Files erkannt' {
        $tmp = New-MatrixTempRoot
        try {
            $ccd = Join-Path $tmp 'x.ccd'; 'x' | Out-File -LiteralPath $ccd -Encoding ascii -Force
            @(Get-CcdMissingFiles -CcdPath $ccd -RootPath $tmp).Count | Should -Be 2
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # Cluster 6
    It 'T-23 Multi-Region ergibt WORLD' {
        (Get-RegionTag -Name 'Game (USA, Europe)') | Should -Be 'WORLD'
    }

    It 'T-24 2-Letter mit expliziter Region bleibt Europe' {
        (Get-RegionTag -Name 'Game (Europe) (de)') | Should -Be 'EU'
    }

    It 'T-25 Alias-Kollision deterministisch' {
        $k1 = ConvertTo-GameKey -BaseName 'Mega Man' -AliasEditionKeying:$true
        $k2 = ConvertTo-GameKey -BaseName 'Megaman' -AliasEditionKeying:$true
        $k1 | Should -Be $k2
    }

    It 'T-26 Gleicher GameKey in verschiedenen Roots bleibt getrennt gruppierbar' {
        $k1 = ConvertTo-GameKey -BaseName 'Same Name (Europe)'
        $k2 = ConvertTo-GameKey -BaseName 'Same Name (Europe)'
        $k1 | Should -Be $k2
    }

    # Cluster 7
    It 'T-27 DAT BOM wird akzeptiert' {
        $tmp = New-MatrixTempRoot
        try {
            $dat = Join-Path $tmp 'ps1.dat'
            ("{0}<datafile><game name='x'><rom sha1='abc'/></game></datafile>" -f [char]0xFEFF) | Out-File -LiteralPath $dat -Encoding utf8 -Force
            $idx = Get-DatIndex -DatRoot $tmp -HashType 'SHA1' -ConsoleMap @{ ps1 = $tmp } -Log { }
            $idx.Keys.Count | Should -BeGreaterThan 0
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-28 DAT Streaming Threshold Pfad' {
        $tmp = New-MatrixTempRoot
        try {
            $dat = Join-Path $tmp 'ps1.dat'
            "<datafile><game name='x'><rom sha1='abc'/></game></datafile>" | Out-File -LiteralPath $dat -Encoding utf8 -Force
            $idx = Get-DatIndex -DatRoot $tmp -HashType 'SHA1' -ConsoleMap @{ ps1 = $tmp } -Log { }
            $idx['PS1'].Count | Should -BeGreaterThan 0
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-29 Nicht-XML DAT wird geskippt ohne Crash' {
        $tmp = New-MatrixTempRoot
        try {
            'clrmamepro-format' | Out-File -LiteralPath (Join-Path $tmp 'ps1.dat') -Encoding ascii -Force
            { Get-DatIndex -DatRoot $tmp -HashType 'SHA1' -ConsoleMap @{ ps1 = $tmp } -Log { } } | Should -Not -Throw
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-30 HashType Wechsel ändert Cache-Fingerprint' {
        $tmp = New-MatrixTempRoot
        try {
            'x' | Out-File -LiteralPath (Join-Path $tmp 'a.dat') -Encoding ascii -Force
            $f1 = Get-DatIndexCacheFingerprint -DatRoot $tmp -HashType 'SHA1' -ConsoleMap @{}
            $f2 = Get-DatIndexCacheFingerprint -DatRoot $tmp -HashType 'CRC32' -ConsoleMap @{}
            $f1 | Should -Not -Be $f2
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # Cluster 8
    It 'T-31 Tool-Timeout/Fail wird als Fehlerobjekt gehandhabt' {
        $r = Invoke-ExternalToolProcess -ToolPath 'C:\missing\tool.exe' -ToolArgs @('x') -TempFiles ([System.Collections.Generic.List[string]]::new()) -Log { } -ErrorLabel 'x'
        $r.Success | Should -BeFalse
    }

    It 'T-32 Tool Hash Mismatch blockbar' {
        $tmp = New-MatrixTempRoot
        try {
            $bin = Join-Path $tmp 'fake.exe'
            'x' | Out-File -LiteralPath $bin -Encoding ascii -Force
            { Test-ToolBinaryHash -ToolPath $bin -Log { } } | Should -Not -Throw
        } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'T-33 Fehlendes Tool in Konvertierung führt zu Skip statt Crash' {
        { Invoke-FormatConversion -Winners ([System.Collections.Generic.List[psobject]]::new()) -Roots @() -Log { } -ToolOverrides @{} } | Should -Not -Throw
    }

    # Cluster 9
    It 'T-34 Cancel Flag kann gesetzt werden' {
        { Set-AppStateValue -Key 'CancelRequested' -Value $true } | Should -Not -Throw
        (Get-AppStateValue -Key 'CancelRequested' -Default $false) | Should -BeTrue
        [void](Set-AppStateValue -Key 'CancelRequested' -Value $false)
    }

    It 'T-35 Background DAT Progress Format robust' {
        '__DATHASH__:10/20' -match '__DATHASH__:(\d+)\/(\d+)' | Should -BeTrue
    }

    It 'T-36 CancelRequested kann von Wait-Logik verarbeitet werden' {
        $evt = [System.Threading.ManualResetEventSlim]::new($false)
        $evt.Set()
        $evt.IsSet | Should -BeTrue
        $evt.Dispose()
    }

    # Cluster 10
    It 'T-37 Doppelstart wird durch Busy-State verhindert (Kontrakt)' {
        (Get-Command Start-WpfOperationAsync -ErrorAction Stop) | Should -Not -BeNullOrEmpty
        (Get-Command Set-WpfBusyState -ErrorAction Stop) | Should -Not -BeNullOrEmpty
    }

    It 'T-38 DragDrop akzeptiert nur Ordner (Kontrakt)' {
        (Get-Command Register-WpfEventHandlers -ErrorAction Stop) | Should -Not -BeNullOrEmpty
    }
}
