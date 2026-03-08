#requires -Modules Pester

<#
  TEST-06: ZipSlip und Reparse-Point Tests fuer Archive
  Validiert, dass unsichere Archiv-Pfade erkannt und blockiert werden.
#>

BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
        $root = Split-Path -Parent $root
    }
    . (Join-Path $root 'dev\modules\FileOps.ps1')
    . (Join-Path $root 'dev\modules\Settings.ps1')
    . (Join-Path $root 'dev\modules\LruCache.ps1')
    . (Join-Path $root 'dev\modules\AppState.ps1')
    . (Join-Path $root 'dev\modules\Tools.ps1')
}

Describe 'TEST-06: ZipSlip und Archive Security' {

    Context 'Test-ArchiveEntryPathsSafe - Traversal Erkennung' {
        It 'Akzeptiert einfache Dateinamen' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('game.bin') | Should -BeTrue
        }

        It 'Akzeptiert Pfade mit Unterordnern (Forward Slash)' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('folder/game.bin') | Should -BeTrue
        }

        It 'Akzeptiert Pfade mit Unterordnern (Backslash)' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('folder\game.bin') | Should -BeTrue
        }

        It 'Akzeptiert verschachtelte Pfade' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('a/b/c/game.gba', 'a/b/d/save.sav') | Should -BeTrue
        }

        It 'Blockiert ../evil.txt (Unix-Traversal)' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('../evil.txt') | Should -BeFalse
        }

        It 'Blockiert ..\evil.txt (Windows-Traversal)' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('..\evil.txt') | Should -BeFalse
        }

        It 'Blockiert tief verschachtelte Traversal' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('folder\..\..\..\evil.txt') | Should -BeFalse
        }

        It 'Blockiert Traversal mitten im Pfad' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('games/../../../etc/passwd') | Should -BeFalse
        }

        It 'Blockiert absoluten Windows-Pfad' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('C:\evil.txt') | Should -BeFalse
        }

        It 'Blockiert absoluten Unix-Pfad' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('/etc/passwd') | Should -BeFalse
        }

        It 'Blockiert UNC-Pfad' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('\\server\share\evil.txt') | Should -BeFalse
        }

        It 'Mixed Array: ein unsicherer Pfad -> gesamtes Array unsicher' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('game.bin', 'folder/ok.bin', '../evil.txt') | Should -BeFalse
        }

        It 'Leere Entry-Liste ist sicher' {
            Test-ArchiveEntryPathsSafe -EntryPaths @() | Should -BeTrue
        }

        It 'Whitespace-Eintraege werden uebersprungen' {
            Test-ArchiveEntryPathsSafe -EntryPaths @('', '  ', 'game.bin') | Should -BeTrue
        }
    }

    Context 'Get-ArchiveEntryPaths - ZipSlip Integration' {
        BeforeEach {
            Reset-ArchiveEntryCache
        }

        It 'Gibt leeres Array zurueck bei Traversal-Entries' {
            $tmpArchive = Join-Path $env:TEMP ("pester_zipslip_{0}.zip" -f (Get-Random))
            [System.IO.File]::WriteAllBytes($tmpArchive, [byte[]]@(0x50, 0x4B))
            try {
                Mock -CommandName Invoke-7z -MockWith {
                    [pscustomobject]@{
                        ExitCode = 0
                        StdOut   = @"
Path = $tmpArchive
Type = zip
Physical Size = 42

----------

Path = ../../../etc/passwd
Size = 100

Path = game.bin
Size = 200
"@
                        StdErr   = ''
                    }
                }

                $entries = @(Get-ArchiveEntryPaths -ArchivePath $tmpArchive -SevenZipPath 'C:\tools\7z.exe' -TempFiles $null)
                $entries.Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tmpArchive -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Gibt leeres Array zurueck bei absoluten Pfaden im Archiv' {
            $tmpArchive = Join-Path $env:TEMP ("pester_zipslip_{0}.zip" -f (Get-Random))
            [System.IO.File]::WriteAllBytes($tmpArchive, [byte[]]@(0x50, 0x4B))
            try {
                Mock -CommandName Invoke-7z -MockWith {
                    [pscustomobject]@{
                        ExitCode = 0
                        StdOut   = @"
Path = $tmpArchive
Type = zip

----------

Path = C:\Windows\evil.dll
Size = 999
"@
                        StdErr   = ''
                    }
                }

                $entries = @(Get-ArchiveEntryPaths -ArchivePath $tmpArchive -SevenZipPath 'C:\tools\7z.exe' -TempFiles $null)
                $entries.Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tmpArchive -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Sichere Entries werden korrekt zurueckgegeben' {
            $tmpArchive = Join-Path $env:TEMP ("pester_zipslip_{0}.zip" -f (Get-Random))
            [System.IO.File]::WriteAllBytes($tmpArchive, [byte[]]@(0x50, 0x4B))
            try {
                Mock -CommandName Invoke-7z -MockWith {
                    [pscustomobject]@{
                        ExitCode = 0
                        StdOut   = @"
Path = $tmpArchive
Type = zip

----------

Path = game.gba
Size = 123

Path = saves/game.sav
Size = 456
"@
                        StdErr   = ''
                    }
                }

                $entries = @(Get-ArchiveEntryPaths -ArchivePath $tmpArchive -SevenZipPath 'C:\tools\7z.exe' -TempFiles $null)
                $entries.Count | Should -Be 2
                $entries | Should -Contain 'game.gba'
                $entries | Should -Contain 'saves/game.sav'
            } finally {
                Remove-Item -LiteralPath $tmpArchive -Force -ErrorAction SilentlyContinue
            }
        }

        It '7z Fehler (ExitCode != 0) -> leeres Array' {
            $tmpArchive = Join-Path $env:TEMP ("pester_zipslip_{0}.zip" -f (Get-Random))
            [System.IO.File]::WriteAllBytes($tmpArchive, [byte[]]@(0x50, 0x4B))
            try {
                Mock -CommandName Invoke-7z -MockWith {
                    [pscustomobject]@{
                        ExitCode = 2
                        StdOut   = ''
                        StdErr   = 'Error: corrupted archive'
                    }
                }

                $entries = @(Get-ArchiveEntryPaths -ArchivePath $tmpArchive -SevenZipPath 'C:\tools\7z.exe' -TempFiles $null)
                $entries.Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tmpArchive -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
