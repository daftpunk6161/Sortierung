#requires -Modules Pester
# ================================================================
#  SetParsing.EdgeCase.Tests.ps1  –  F-02
#  Verifies that Resolve-SetReferencePathCompat emits a Verbose
#  audit trail when RootPath is empty (legacy unguarded code path).
# ================================================================

Describe 'F-02: SetParsing – leerer RootPath emittiert Verbose' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\SetParsing.ps1')
    }

    Context 'Wenn RootPath leer ist' {

        It 'gibt Verbose-Meldung mit Dateinamen aus' {
            $verboseMessages = [System.Collections.Generic.List[string]]::new()

            $old = $VerbosePreference
            $VerbosePreference = 'Continue'
            try {
                Resolve-SetReferencePathCompat -Reference 'disc1.bin' -RootPath '' 4>&1 |
                    ForEach-Object {
                        if ($_ -is [System.Management.Automation.VerboseRecord]) {
                            $verboseMessages.Add($_.Message)
                        }
                    }
            } finally {
                $VerbosePreference = $old
            }

            $relevant = $verboseMessages | Where-Object { $_ -match 'SetParsing.*RootPath leer' }
            $relevant | Should -Not -BeNullOrEmpty -Because 'F-02: ungeschützter Pfad muss im Verbose-Stream sichtbar sein'
        }

        It 'enthält den Dateinamen in der Verbose-Meldung' {
            $verboseMessages = [System.Collections.Generic.List[string]]::new()
            $testRef = 'mydisc.cue'

            $old = $VerbosePreference
            $VerbosePreference = 'Continue'
            try {
                Resolve-SetReferencePathCompat -Reference $testRef -RootPath '' 4>&1 |
                    ForEach-Object {
                        if ($_ -is [System.Management.Automation.VerboseRecord]) {
                            $verboseMessages.Add($_.Message)
                        }
                    }
            } finally {
                $VerbosePreference = $old
            }

            $relevant = $verboseMessages | Where-Object { $_ -match [regex]::Escape($testRef) }
            $relevant | Should -Not -BeNullOrEmpty -Because 'Verbose-Meldung muss den Dateinamen enthalten'
        }
    }

    Context 'Wenn RootPath gesetzt ist' {

        It 'emittiert keine Verbose-Warnung für geschützten Pfad' {
            $verboseMessages = [System.Collections.Generic.List[string]]::new()
            $tmpDir = [System.IO.Path]::GetTempPath()

            $old = $VerbosePreference
            $VerbosePreference = 'Continue'
            try {
                Resolve-SetReferencePathCompat -Reference 'test.bin' -RootPath $tmpDir 4>&1 |
                    ForEach-Object {
                        if ($_ -is [System.Management.Automation.VerboseRecord]) {
                            $verboseMessages.Add($_.Message)
                        }
                    }
            } finally {
                $VerbosePreference = $old
            }

            $relevant = $verboseMessages | Where-Object { $_ -match 'RootPath leer' }
            $relevant | Should -BeNullOrEmpty -Because 'mit RootPath keine Warn-Verbose'
        }
    }
}
