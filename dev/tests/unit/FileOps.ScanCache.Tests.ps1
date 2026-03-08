#requires -Modules Pester

Describe 'FileOps Scan Cache mit Watcher' {
    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }

        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\LruCache.ps1')
        . (Join-Path $root 'dev\modules\AppState.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
    }

    It 'aktualisiert Scan-Ergebnis inkrementell bei Dateiänderungen' {
        $scanRoot = Join-Path $TestDrive 'scan-root'
        [void](New-Item -ItemType Directory -Path $scanRoot -Force)

        $f1 = Join-Path $scanRoot 'a.bin'
        [System.IO.File]::WriteAllText($f1, 'a')

        $first = @(Get-FilesSafe -Root $scanRoot)
        $first.FullName | Should -Contain $f1

        $f2 = Join-Path $scanRoot 'b.bin'
        [System.IO.File]::WriteAllText($f2, 'b')

        Start-Sleep -Milliseconds 400

        $second = @(Get-FilesSafe -Root $scanRoot)
        $second.FullName | Should -Contain $f2
    }

    It 'erzwingt Rescan wenn Cache 0 Ergebnisse hat aber Root erreichbar ist' {
        $scanRoot = Join-Path $TestDrive 'rescan-root'
        [void](New-Item -ItemType Directory -Path $scanRoot -Force)

        # Cache manuell mit leeren Paths fuellen
        $cacheKey = Get-FileScanCacheKey -Root $scanRoot -Filter '*' -ExcludePrefixes @() -AllowedExtensions @()
        $script:FILE_SCAN_CACHE[$cacheKey] = [pscustomobject]@{
            Root = (Resolve-RootPath -Path $scanRoot)
            RootVersion = 0
            RootStamp = 0L
            Paths = @()
            UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        }

        # Datei erstellen – Cache hat 0 Paths, also muss Rescan laufen
        $f1 = Join-Path $scanRoot 'test.rom'
        [System.IO.File]::WriteAllText($f1, 'content')

        $result = @(Get-FilesSafe -Root $scanRoot)
        $result.Count | Should -BeGreaterThan 0
        $result.FullName | Should -Contain $f1
    }

    It 'behandelt Watcher-Failed-Flag konservativ' {
        $scanRoot = Join-Path $TestDrive 'watcher-fail-root'
        [void](New-Item -ItemType Directory -Path $scanRoot -Force)

        $f1 = Join-Path $scanRoot 'a.bin'
        [System.IO.File]::WriteAllText($f1, 'a')

        # Normalen Scan durchfuehren
        $first = @(Get-FilesSafe -Root $scanRoot)
        $first.Count | Should -BeGreaterThan 0

        # Watcher-Failed-Flag setzen
        $normalizedRoot = Resolve-RootPath -Path $scanRoot
        $script:FILE_SCAN_WATCHER_FAILED[$normalizedRoot] = $true

        # Cache-Eintrag kuenstlich auf >60s alt setzen
        $cacheKey = Get-FileScanCacheKey -Root $scanRoot -Filter '*' -ExcludePrefixes @() -AllowedExtensions @()
        if ($script:FILE_SCAN_CACHE.ContainsKey($cacheKey)) {
            $entry = $script:FILE_SCAN_CACHE[$cacheKey]
            $entry.UpdatedAtUtc = (Get-Date).ToUniversalTime().AddSeconds(-120).ToString('o')
        }

        # Neue Datei hinzufuegen
        $f2 = Join-Path $scanRoot 'b.bin'
        [System.IO.File]::WriteAllText($f2, 'b')

        # Rescan muss wegen abgelaufenem Cache + Watcher-Failed laufen
        $second = @(Get-FilesSafe -Root $scanRoot)
        $second.FullName | Should -Contain $f2

        # Aufraeumen
        $script:FILE_SCAN_WATCHER_FAILED.Remove($normalizedRoot)
    }
}
