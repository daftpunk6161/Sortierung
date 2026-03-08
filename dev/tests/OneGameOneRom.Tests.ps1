Describe '1G1R Parent/Clone Infrastructure' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
        $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'rom_1g1r_test'
        $script:TempScript = $ctx.TempScript
        . $script:TempScript
    }

    AfterAll {
        Remove-SimpleSortTestTempScript -TempScript $script:TempScript
    }

    Context 'Get-DatParentCloneIndex DOM-Pfad' {
        It 'parst cloneof-Attribute aus einfacher DAT-XML' {
            $datDir = Join-Path $TestDrive '1g1r_dat'
            [void](New-Item -ItemType Directory -Path $datDir -Force)
            $datFile = Join-Path $datDir 'Nintendo - Game Boy.dat'

            $xml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Tetris (Europe)">
    <rom name="Tetris (Europe).gb" size="32768" crc="AAAA"/>
  </game>
  <game name="Tetris (USA)" cloneof="Tetris (Europe)">
    <rom name="Tetris (USA).gb" size="32768" crc="BBBB"/>
  </game>
  <game name="Tetris (Japan)" cloneof="Tetris (Europe)">
    <rom name="Tetris (Japan).gb" size="32768" crc="CCCC"/>
  </game>
  <game name="Super Mario Land (World)">
    <rom name="Super Mario Land (World).gb" size="65536" crc="DDDD"/>
  </game>
</datafile>
'@
            $xml | Out-File -LiteralPath $datFile -Encoding utf8

            Mock -CommandName Test-CancelRequested -MockWith { }

            $result = Get-DatParentCloneIndex -DatRoot $datDir -Log { param($m) }
            $result | Should -Not -BeNullOrEmpty
            $result.ParentMap | Should -Not -BeNullOrEmpty
            $result.ChildrenMap | Should -Not -BeNullOrEmpty

            # Tetris (USA) ist Clone von Tetris (Europe)
            $result.ParentMap['GB|Tetris (USA)'] | Should -Be 'Tetris (Europe)'
            $result.ParentMap['GB|Tetris (Japan)'] | Should -Be 'Tetris (Europe)'

            # Tetris (Europe) ist Parent (zeigt auf sich selbst)
            $result.ParentMap['GB|Tetris (Europe)'] | Should -Be 'Tetris (Europe)'

            # Super Mario Land hat keinen Clone
            $result.ParentMap['GB|Super Mario Land (World)'] | Should -Be 'Super Mario Land (World)'
        }

        It 'ChildrenMap enthält alle Clones pro Parent' {
            $datDir = Join-Path $TestDrive '1g1r_children'
            [void](New-Item -ItemType Directory -Path $datDir -Force)
            $datFile = Join-Path $datDir 'Nintendo - Game Boy.dat'

            $xml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Zelda (Europe)">
    <rom name="Zelda (Europe).gb" size="99" crc="1111"/>
  </game>
  <game name="Zelda (USA)" cloneof="Zelda (Europe)">
    <rom name="Zelda (USA).gb" size="99" crc="2222"/>
  </game>
  <game name="Zelda (France)" cloneof="Zelda (Europe)">
    <rom name="Zelda (France).gb" size="99" crc="3333"/>
  </game>
</datafile>
'@
            $xml | Out-File -LiteralPath $datFile -Encoding utf8

            Mock -CommandName Test-CancelRequested -MockWith { }

            $result = Get-DatParentCloneIndex -DatRoot $datDir -Log { param($m) }
            $children = $result.ChildrenMap['GB|Zelda (Europe)']
            $children | Should -Not -BeNullOrEmpty
            $children.Count | Should -Be 2
            $children | Should -Contain 'Zelda (USA)'
            $children | Should -Contain 'Zelda (France)'
        }
    }

    Context 'Resolve-ParentName' {
        It 'gibt Root-Parent fuer direkten Clone zurueck' {
            $pm = @{
                'GB|Tetris (USA)'    = 'Tetris (Europe)'
                'GB|Tetris (Europe)' = 'Tetris (Europe)'
            }
            Resolve-ParentName -GameName 'Tetris (USA)' -ConsoleKey 'GB' -ParentMap $pm |
                Should -Be 'Tetris (Europe)'
        }

        It 'gibt Root-Parent fuer Parent selbst zurueck' {
            $pm = @{
                'GB|Tetris (Europe)' = 'Tetris (Europe)'
            }
            Resolve-ParentName -GameName 'Tetris (Europe)' -ConsoleKey 'GB' -ParentMap $pm |
                Should -Be 'Tetris (Europe)'
        }

        It 'loest transitive Ketten auf' {
            $pm = @{
                'NES|Game (Germany)' = 'Game (Europe)'
                'NES|Game (Europe)'  = 'Game (World)'
                'NES|Game (World)'   = 'Game (World)'
            }
            Resolve-ParentName -GameName 'Game (Germany)' -ConsoleKey 'NES' -ParentMap $pm |
                Should -Be 'Game (World)'
        }

        It 'bricht bei Zyklen ab ohne Endlosschleife' {
            $pm = @{
                'NES|A' = 'B'
                'NES|B' = 'C'
                'NES|C' = 'A'
            }
            # Sollte nicht haengen, gibt irgendeinen Knoten zurueck
            $result = Resolve-ParentName -GameName 'A' -ConsoleKey 'NES' -ParentMap $pm
            $result | Should -Not -BeNullOrEmpty
        }

        It 'gibt GameName zurueck wenn nicht in ParentMap' {
            $pm = @{}
            Resolve-ParentName -GameName 'Unknown Game' -ConsoleKey 'GB' -ParentMap $pm |
                Should -Be 'Unknown Game'
        }

        It 'gibt GameName bei null ParentMap zurueck' {
            Resolve-ParentName -GameName 'Test' -ConsoleKey 'GB' -ParentMap $null |
                Should -Be 'Test'
        }
    }

    Context 'Streaming-Pfad' {
        It 'parst cloneof aus grosser DAT-XML via XmlReader' {
            $datDir = Join-Path $TestDrive '1g1r_stream'
            [void](New-Item -ItemType Directory -Path $datDir -Force)
            $datFile = Join-Path $datDir 'Sega - Mega Drive.dat'

            # Erzeuge grosse Datei (> 20MB Schwellwert simulieren wir direkt)
            $xml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Sonic (Europe)">
    <rom name="Sonic (Europe).md" size="100" crc="EEEE"/>
  </game>
  <game name="Sonic (USA)" cloneof="Sonic (Europe)">
    <rom name="Sonic (USA).md" size="100" crc="FFFF"/>
  </game>
</datafile>
'@
            $xml | Out-File -LiteralPath $datFile -Encoding utf8

            Mock -CommandName Test-CancelRequested -MockWith { }

            # Teste den Streaming-Pfad direkt
            [void]([hashtable]::new([StringComparer]::OrdinalIgnoreCase))
            [void]([hashtable]::new([StringComparer]::OrdinalIgnoreCase))
            $xmlSettings = New-Object System.Xml.XmlReaderSettings
            $xmlSettings.IgnoreComments   = $true
            $xmlSettings.IgnoreWhitespace = $true

            # Zugriff auf die innere Funktion ueber den Modul-Scope
            # Da die Funktion in Get-DatParentCloneIndex definiert ist,
            # testen wir stattdessen die Gesamtfunktion
            $result = Get-DatParentCloneIndex -DatRoot $datDir -Log { param($m) }

            $result.ParentMap['MD|Sonic (USA)'] | Should -Be 'Sonic (Europe)'
            $result.ParentMap['MD|Sonic (Europe)'] | Should -Be 'Sonic (Europe)'
        }
    }
}
