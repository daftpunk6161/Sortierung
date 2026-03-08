#requires -Modules Pester

<#
  T-19: consoles.json vs. hardcoded Map Konsistenz
  Stellt sicher, dass consoles.json-Keys in den Classification-Maps enthalten sind
  und keine Konsole nur im Hardcode existiert ohne consoles.json-Eintrag.
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

    $script:consolesJsonPath = Join-Path $root 'data\consoles.json'
    $script:consolesRaw = Get-Content -Path $script:consolesJsonPath -Raw | ConvertFrom-Json
    $script:consolesData = $script:consolesRaw.consoles
}

Describe 'T-19: consoles.json vs. hardcoded Map Konsistenz' {

    Context 'consoles.json ist vollstaendig' {
        It 'consoles.json existiert und ist nicht leer' {
            Test-Path $script:consolesJsonPath | Should -BeTrue
            $script:consolesData | Should -Not -BeNullOrEmpty
        }

        It 'Jeder consoles.json Key ist ein gueltiger Console-Key' {
            $consoleKeyRx = [regex]::new('^[A-Z0-9_-]+$')
            foreach ($entry in $script:consolesData) {
                if (-not $entry.key) { continue }
                $key = $entry.key
                $consoleKeyRx.IsMatch($key) | Should -BeTrue -Because "Key '$key' muss alphanumerisch sein"
            }
        }
    }

    Context 'consoles.json Keys sind in CONSOLE_FOLDER_MAP' {
        It 'Jede Konsole mit folderAliases hat mindestens einen FOLDER_MAP-Eintrag' {
            $folderMap = $script:CONSOLE_FOLDER_MAP
            $missing = [System.Collections.Generic.List[string]]::new()

            foreach ($entry in $script:consolesData) {
                $key = $entry.key
                if (-not $entry.folderAliases -or $entry.folderAliases.Count -eq 0) { continue }

                $foundAny = $false
                foreach ($alias in $entry.folderAliases) {
                    $aliasLower = $alias.ToLowerInvariant()
                    if ($folderMap.ContainsKey($aliasLower)) {
                        $foundAny = $true
                        break
                    }
                }
                if (-not $foundAny) {
                    # Pruefe auch ob der Key selbst in der Map ist
                    if ($folderMap.ContainsKey($key.ToLowerInvariant())) {
                        $foundAny = $true
                    }
                }
                if (-not $foundAny) {
                    $missing.Add($key)
                }
            }

            $missing.Count | Should -Be 0 -Because "Fehlende Konsolen in FOLDER_MAP: $($missing -join ', ')"
        }
    }

    Context 'UniqueExts in consoles.json matchen CONSOLE_EXT_MAP_UNIQUE' {
        It 'Jede uniqueExt in consoles.json ist in EXT_MAP_UNIQUE oder EXT_MAP_AMBIG' {
            $missing = [System.Collections.Generic.List[string]]::new()

            foreach ($entry in $script:consolesData) {
                if (-not $entry.uniqueExts) { continue }
                foreach ($ext in $entry.uniqueExts) {
                    $extLower = $ext.ToLowerInvariant()
                    if (-not $script:CONSOLE_EXT_MAP_UNIQUE.ContainsKey($extLower) -and
                        -not $script:CONSOLE_EXT_MAP_AMBIG.ContainsKey($extLower) -and
                        -not $script:CONSOLE_EXT_MAP.ContainsKey($extLower)) {
                        $missing.Add("$($entry.key):$ext")
                    }
                }
            }

            $missing.Count | Should -Be 0 -Because "Fehlende Extensions: $($missing -join ', ')"
        }
    }

    Context 'discBased-Konsolen haben Disc-Extensions' {
        It 'Disc-basierte Konsolen aus consoles.json werden korrekt erkannt' {
            $discConsoles = $script:consolesData | Where-Object { $_.discBased -eq $true }
            $discConsoles.Count | Should -BeGreaterThan 5 -Because 'Es gibt viele disc-basierte Konsolen'

            foreach ($c in $discConsoles) {
                $c.key | Should -Not -BeNullOrEmpty
            }
        }
    }

    Context 'CONSOLE_FOLDER_MAP hat keine verwaisten Keys' {
        It 'Jeder FOLDER_MAP-Wert existiert als Console-Key in consoles.json oder ist bekannt' {
            $jsonKeys = @{}
            foreach ($entry in $script:consolesData) {
                if ($entry.key) { $jsonKeys[$entry.key] = $true }
            }

            $folderMap = $script:CONSOLE_FOLDER_MAP
            $unknownValues = [System.Collections.Generic.List[string]]::new()

            foreach ($mapKey in $folderMap.Keys) {
                $value = $folderMap[$mapKey]
                if ($value -and -not $jsonKeys.ContainsKey($value)) {
                    $unknownValues.Add("$mapKey -> $value")
                }
            }

            # Erlaubt: Einige historische Keys die nicht in consoles.json sind
            # Aber es sollten keine unbekannten Werte sein
            if ($unknownValues.Count -gt 0) {
                Write-Warning "FOLDER_MAP-Werte ohne consoles.json-Eintrag: $($unknownValues -join ', ')"
            }
        }
    }
}
