#requires -Modules Pester
<#
  GameKey Normalization Tests
  ===========================
  Tests für ConvertTo-GameKey und Datenverlust-Schutz
  
  Mutation Kills: M3
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'gamekey_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# ============================================================
# GAMEKEY EMPTY PROTECTION (Mutation M3)
# ============================================================

Describe 'ConvertTo-GameKey - Mutation Kill M3' {
    Context 'Empty Key Prevention (Data Loss Protection)' {
        
        It 'M3-KILL: Should NEVER return empty string' {
            # These inputs would produce empty key without fallback
            $dangerousInputs = @(
                '(Europe)',
                '(USA) (En,Fr)',
                '[!]',
                '(Beta)',
                '(Rev A)',
                '(v1.0)',
                '(World)',
                '(Japan)',
                '(Hack)',
                '(Demo)'
            )
            
            foreach ($testInput in $dangerousInputs) {
                $result = ConvertTo-GameKey -BaseName $testInput
                $result | Should -Not -BeNullOrEmpty -Because "Input '$testInput' must not produce empty key"
                $result.Trim() | Should -Not -Be '' -Because "Input '$testInput' should have non-whitespace key"
            }
        }
        
        It 'M3-KILL: Empty input should NOT produce empty key' {
            $result = ConvertTo-GameKey -BaseName ''
            $result | Should -Not -BeNullOrEmpty
            $result | Should -Not -Be ''
        }
        
        It 'M3-KILL: Whitespace-only input should NOT produce empty key' {
            $result = ConvertTo-GameKey -BaseName '   '
            $result | Should -Not -BeNullOrEmpty
            $result.Trim() | Should -Not -Be ''
        }
        
        It 'M3-KILL: Tag-only input should fall back to original name' {
            $testInput = '(Europe)'
            $result = ConvertTo-GameKey -BaseName $testInput
            
            # Should fall back to the original lowercase
            $result | Should -Be '(europe)' -Because 'Fallback uses original name'
        }
    }
    
    Context 'Key Normalization' {
        
        It 'Should produce same key for region variants' {
            $variants = @(
                'Super Mario Bros (Europe)',
                'Super Mario Bros (USA)',
                'Super Mario Bros (Japan)'
            )
            
            $keys = $variants | ForEach-Object { ConvertTo-GameKey -BaseName $_ }
            $uniqueKeys = @($keys | Select-Object -Unique)
            
            $uniqueKeys.Count | Should -Be 1 -Because 'All region variants should have same key'
        }
        
        It 'Should produce same key for version variants' {
            $variants = @(
                'Game Title (Europe)',
                'Game Title (Europe) (Rev A)',
                'Game Title (Europe) (Rev B)',
                'Game Title (Europe) (v1.0)',
                'Game Title (Europe) (v1.1)'
            )
            
            $keys = $variants | ForEach-Object { ConvertTo-GameKey -BaseName $_ }
            $uniqueKeys = @($keys | Select-Object -Unique)
            
            $uniqueKeys.Count | Should -Be 1 -Because 'All version variants should have same key'
        }
        
        It 'Should produce same key for language variants' {
            $variants = @(
                'Game Title (Europe)',
                'Game Title (Europe) (En)',
                'Game Title (Europe) (En,Fr)',
                'Game Title (Europe) (En,Fr,De,Es,It)'
            )
            
            $keys = $variants | ForEach-Object { ConvertTo-GameKey -BaseName $_ }
            $uniqueKeys = @($keys | Select-Object -Unique)
            
            $uniqueKeys.Count | Should -Be 1 -Because 'All language variants should have same key'
        }
    }
    
    Context 'Multi-Disc Preservation' {
        
        It 'Should PRESERVE (Disc N) to prevent collisions' {
            $disc1 = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 1)'
            $disc2 = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 2)'
            $disc3 = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 3)'
            
            $disc1 | Should -Not -Be $disc2 -Because 'Disc 1 and Disc 2 must be separate'
            $disc2 | Should -Not -Be $disc3 -Because 'Disc 2 and Disc 3 must be separate'
            $disc1 | Should -Not -Be $disc3 -Because 'Disc 1 and Disc 3 must be separate'
        }
        
        It 'Should match Disc N across regions' {
            $disc1EU = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 1)'
            $disc1US = ConvertTo-GameKey -BaseName 'Final Fantasy VII (USA) (Disc 1)'
            
            $disc1EU | Should -Be $disc1US -Because 'Same disc across regions should match'
        }
        
        It 'Should PRESERVE (Side A/B) for floppy games' {
            $sideA = ConvertTo-GameKey -BaseName 'Game (Europe) (Side A)'
            $sideB = ConvertTo-GameKey -BaseName 'Game (Europe) (Side B)'
            
            $sideA | Should -Not -Be $sideB -Because 'Side A and Side B must be separate'
        }
    }
    
    Context 'Tag Removal' {
        
        It 'Should remove [!] verified tag' {
            $with = ConvertTo-GameKey -BaseName 'Game (Europe) [!]'
            $without = ConvertTo-GameKey -BaseName 'Game (Europe)'
            
            $with | Should -Be $without
        }
        
        It 'Should remove bad dump tags [b], [h], etc.' {
            $normal = ConvertTo-GameKey -BaseName 'Game (Europe)'
            $bad = ConvertTo-GameKey -BaseName 'Game (Europe) [b]'
            $hack = ConvertTo-GameKey -BaseName 'Game (Europe) [h]'
            
            $normal | Should -Be $bad
            $normal | Should -Be $hack
        }
        
        It 'Should remove Virtual Console tags' {
            $vc = ConvertTo-GameKey -BaseName 'Game (Virtual Console)'
            $plain = ConvertTo-GameKey -BaseName 'Game'
            
            $vc | Should -Be $plain
        }

        It 'Should normalize headered and headerless tags to same key' {
            $headered = ConvertTo-GameKey -BaseName 'Game Title (USA) (Headered)'
            $headerless = ConvertTo-GameKey -BaseName 'Game Title (USA) (Headerless)'

            $headered | Should -Be $headerless
        }

        It 'Should keep edition variants separate by default' {
            $plain = ConvertTo-GameKey -BaseName '1869 - Erlebte Geschichte Teil I (Germany)'
            $aga   = ConvertTo-GameKey -BaseName '1869 - Erlebte Geschichte Teil I (Germany) (AGA)'

            $plain | Should -Not -Be $aga
        }

        It 'Should merge edition variants when alias keying is enabled' {
            $plain = ConvertTo-GameKey -BaseName '1869 - Erlebte Geschichte Teil I (Germany)' -AliasEditionKeying $true
            $aga   = ConvertTo-GameKey -BaseName '1869 - Erlebte Geschichte Teil I (Germany) (AGA)' -AliasEditionKeying $true

            $plain | Should -Be $aga
        }

        It 'Should merge known localized alias titles when alias keying is enabled' {
            $english = ConvertTo-GameKey -BaseName 'Abandoned Places - A Time for Heroes (Europe)' -AliasEditionKeying $true
            $german  = ConvertTo-GameKey -BaseName 'Abandoned Places - Zeit fuer Helden (Germany)' -AliasEditionKeying $true

            $english | Should -Be $german
        }

        It 'Should merge Akumajou titles into Castlevania aliases when alias keying is enabled' {
            $castlevania = ConvertTo-GameKey -BaseName 'Castlevania (Europe)' -AliasEditionKeying $true
            $castlevaniaJp = ConvertTo-GameKey -BaseName 'Akumajou Dracula (World) (Castlevania Anniversary Collection)' -AliasEditionKeying $true

            $cv3 = ConvertTo-GameKey -BaseName "Castlevania III - Dracula's Curse (Europe)" -AliasEditionKeying $true
            $cv3Jp = ConvertTo-GameKey -BaseName 'Akumajou Densetsu (World) (Ja) (Castlevania Anniversary Collection)' -AliasEditionKeying $true

            $kid = ConvertTo-GameKey -BaseName 'Kid Dracula (World) (Castlevania Anniversary Collection)' -AliasEditionKeying $true
            $kidJp = ConvertTo-GameKey -BaseName 'Akumajou Special - Boku Dracula-kun (World) (Ja) (Castlevania Anniversary Collection)' -AliasEditionKeying $true

            $castlevaniaJp | Should -Be $castlevania
            $cv3Jp | Should -Be $cv3
            $kidJp | Should -Be $kid
        }

        It 'Should merge Akumajou titles into Castlevania aliases in default mode' {
            $castlevania = ConvertTo-GameKey -BaseName 'Castlevania (Europe)'
            $castlevaniaJp = ConvertTo-GameKey -BaseName 'Akumajou Dracula (World) (Castlevania Anniversary Collection)'

            $cv3 = ConvertTo-GameKey -BaseName "Castlevania III - Dracula's Curse (Europe)"
            $cv3Jp = ConvertTo-GameKey -BaseName 'Akumajou Densetsu (World) (Ja) (Castlevania Anniversary Collection)'

            $kid = ConvertTo-GameKey -BaseName 'Kid Dracula (World) (Castlevania Anniversary Collection)'
            $kidJp = ConvertTo-GameKey -BaseName 'Akumajou Special - Boku Dracula-kun (World) (Ja) (Castlevania Anniversary Collection)'

            $castlevaniaJp | Should -Be $castlevania
            $cv3Jp | Should -Be $cv3
            $kidJp | Should -Be $kid
        }

        It 'Should merge Rockman numbered titles into Mega Man aliases in default mode' {
            $mm2 = ConvertTo-GameKey -BaseName 'Mega Man 2 (Europe)'
            $rm2 = ConvertTo-GameKey -BaseName 'Rockman 2 (Taiwan) (En) (Rockman 123)'

            $mm3 = ConvertTo-GameKey -BaseName 'Mega Man 3 (Europe)'
            $rm3 = ConvertTo-GameKey -BaseName 'Rockman III (Taiwan) (En) (Rockman 123)'

            $mm4 = ConvertTo-GameKey -BaseName 'Mega Man 4 (Europe)'
            $rm4 = ConvertTo-GameKey -BaseName 'Rockman IV (Taiwan) (En) (Rockman 123)'

            $mm5 = ConvertTo-GameKey -BaseName 'Mega Man 5 (Europe)'
            $rm5 = ConvertTo-GameKey -BaseName 'Rockman V (Taiwan) (En) (Rockman 123)'

            $mm6 = ConvertTo-GameKey -BaseName 'Mega Man 6 (USA)'
            $rm6 = ConvertTo-GameKey -BaseName 'Rockman VI (Taiwan) (En) (Rockman 123)'

            $rm2 | Should -Be $mm2
            $rm3 | Should -Be $mm3
            $rm4 | Should -Be $mm4
            $rm5 | Should -Be $mm5
            $rm6 | Should -Be $mm6
        }

        It 'Should merge safe store and collection suffix variants in default mode' {
            $eightEyesBase = ConvertTo-GameKey -BaseName '8 Eyes (USA)'
            $eightEyesDigital = ConvertTo-GameKey -BaseName '8 Eyes (World) (Digital)'
            $eightEyesPixel = ConvertTo-GameKey -BaseName '8 Eyes (USA, Europe) (Pixel Heart)'

            $battletoadsBase = ConvertTo-GameKey -BaseName 'Battletoads (Europe)'
            $battletoadsIam8bit = ConvertTo-GameKey -BaseName 'Battletoads (USA) (iam8bit)'

            $ghostsBase = ConvertTo-GameKey -BaseName "Ghosts'n Goblins (Europe)"
            $ghostsCapcomTown = ConvertTo-GameKey -BaseName "Ghosts'n Goblins (USA) (Capcom Town)"

            $mmBase = ConvertTo-GameKey -BaseName 'Mega Man (Europe)'
            $mmCapcomTown = ConvertTo-GameKey -BaseName 'Mega Man (USA) (Capcom Town)'

            $mm2Base = ConvertTo-GameKey -BaseName 'Mega Man 2 (Europe)'
            $mm2CapcomTown = ConvertTo-GameKey -BaseName 'Mega Man 2 (USA) (Capcom Town)'
            $mm2Iam8bit = ConvertTo-GameKey -BaseName 'Mega Man 2 (USA) (iam8bit)'

            $cv3Base = ConvertTo-GameKey -BaseName "Castlevania III - Dracula's Curse (Europe)"
            $cv3UsVc = ConvertTo-GameKey -BaseName "Castlevania III - Dracula's Curse (USA, Europe) (USA Wii Virtual Console, Wii U Virtual Console)"

            $eightEyesDigital | Should -Be $eightEyesBase
            $eightEyesPixel | Should -Be $eightEyesBase
            $battletoadsIam8bit | Should -Be $battletoadsBase
            $ghostsCapcomTown | Should -Be $ghostsBase
            $mmCapcomTown | Should -Be $mmBase
            $mm2CapcomTown | Should -Be $mm2Base
            $mm2Iam8bit | Should -Be $mm2Base
            $cv3UsVc | Should -Be $cv3Base
        }

        It 'Should merge Nintendo collection tags when alias keying is enabled' {
            $base = ConvertTo-GameKey -BaseName '8 Eyes (USA)'
            $retro = ConvertTo-GameKey -BaseName '8 Eyes (USA) (Retro-Bit Generations)' -AliasEditionKeying $true
            $pixel = ConvertTo-GameKey -BaseName '8 Eyes (USA, Europe) (Pixel Heart)' -AliasEditionKeying $true
            $digital = ConvertTo-GameKey -BaseName '8 Eyes (World) (Digital)' -AliasEditionKeying $true

            $retro | Should -Be $base
            $pixel | Should -Be $base
            $digital | Should -Be $base
        }

        It 'Should normalize Nintendo collection tags in default mode' {
            $base = ConvertTo-GameKey -BaseName '8 Eyes (USA)'
            $retro = ConvertTo-GameKey -BaseName '8 Eyes (USA) (Retro-Bit Generations)'

            $retro | Should -Be $base
        }

        It 'Should normalize Evercade and anniversary collection tags by default' {
            $base = ConvertTo-GameKey -BaseName 'Adventures of Rad Gravity, The (Europe)'
            $evercade = ConvertTo-GameKey -BaseName 'Adventures of Rad Gravity, The (World) (Evercade)'
            $castlevaniaBase = ConvertTo-GameKey -BaseName 'Akumajou Densetsu (World) (Ja)'
            $castlevaniaCollection = ConvertTo-GameKey -BaseName 'Akumajou Densetsu (World) (Ja) (Castlevania Anniversary Collection)'

            $evercade | Should -Be $base
            $castlevaniaCollection | Should -Be $castlevaniaBase
        }

        It 'Should collapse additional NES store/collection tags when alias keying is enabled' {
            $base = ConvertTo-GameKey -BaseName '1943 - The Battle of Midway (USA)' -AliasEditionKeying $true
            $variant1 = ConvertTo-GameKey -BaseName '1943 - The Battle of Midway (USA) (Retro-Bit)' -AliasEditionKeying $true
            $variant2 = ConvertTo-GameKey -BaseName '1943 - The Battle of Midway (USA) (Namco Museum Archives Vol 2)' -AliasEditionKeying $true
            $variant3 = ConvertTo-GameKey -BaseName '1943 - The Battle of Midway (USA) (Wii U Virtual Console)' -AliasEditionKeying $true

            $variant1 | Should -Be $base
            $variant2 | Should -Be $base
            $variant3 | Should -Be $base
        }

        It 'Should collapse generic collection-style tags when alias keying is enabled' {
            $base = ConvertTo-GameKey -BaseName '101 Starships (World)' -AliasEditionKeying $true
            $collection = ConvertTo-GameKey -BaseName "101 Starships (World) (Erwin's Collection)" -AliasEditionKeying $true

            $collection | Should -Be $base
        }

        It 'Should collapse ndsi enhanced tags when alias keying is enabled' {
            $base = ConvertTo-GameKey -BaseName 'Animal Life - Africa (Europe) (En,Fr,De,Es,It)' -AliasEditionKeying $true
            $variant = ConvertTo-GameKey -BaseName 'Animal Life - Africa (Europe) (En,Fr,De,Es,It) (NDSi Enhanced)' -AliasEditionKeying $true

            $variant | Should -Be $base
        }

        It 'Should keep (USA, Europe) key behavior unchanged' {
            $base = ConvertTo-GameKey -BaseName '8 Eyes (USA)'
            $world = ConvertTo-GameKey -BaseName '8 Eyes (USA, Europe)'
            $baseAlias = ConvertTo-GameKey -BaseName '8 Eyes (USA)' -AliasEditionKeying $true
            $worldAlias = ConvertTo-GameKey -BaseName '8 Eyes (USA, Europe)' -AliasEditionKeying $true

            $world | Should -Be $base
            $worldAlias | Should -Be $baseAlias
        }

        It 'Should normalize extended region combinations consistently' {
            $base = ConvertTo-GameKey -BaseName 'Aladdin (Russia)'
            $combo = ConvertTo-GameKey -BaseName 'Aladdin (Europe, Russia)'

            $combo | Should -Be $base
        }

        It 'Should normalize UK and Europe variants to same key' {
            $europe = ConvertTo-GameKey -BaseName 'Game Title (Europe)'
            $uk = ConvertTo-GameKey -BaseName 'Game Title (UK)'

            $uk | Should -Be $europe
        }
    }
    
    Context 'Case Insensitivity' {
        
        It 'Should produce lowercase keys' {
            $result = ConvertTo-GameKey -BaseName 'SUPER MARIO BROS'
            $result | Should -BeExactly $result.ToLowerInvariant()
        }
        
        It 'Should match case-insensitively' {
            $upper = ConvertTo-GameKey -BaseName 'GAME TITLE (EUROPE)'
            $lower = ConvertTo-GameKey -BaseName 'game title (europe)'
            $mixed = ConvertTo-GameKey -BaseName 'Game Title (Europe)'
            
            $upper | Should -Be $lower
            $lower | Should -Be $mixed
        }
    }
    
    Context 'Special Characters' {
        
        It 'Should normalize multiple spaces to single' {
            $multiSpace = ConvertTo-GameKey -BaseName 'Game    Title'
            $singleSpace = ConvertTo-GameKey -BaseName 'Game Title'
            
            $multiSpace | Should -Be $singleSpace
        }
        
        It 'Should normalize dots and underscores to spaces' {
            $dots = ConvertTo-GameKey -BaseName 'Game.Title'
            $underscores = ConvertTo-GameKey -BaseName 'Game_Title'
            $spaces = ConvertTo-GameKey -BaseName 'Game Title'
            
            # All should produce similar normalized key
            $dots | Should -Not -Match '\.'
            $underscores | Should -Not -Match '_'
            $spaces | Should -Not -BeNullOrEmpty
        }
    }
}

Describe 'GameKey Combined Regex Cache' {
    It 'Should have combined regex pre-built after Initialize-RulePatterns' {
        # PERF-09: Combined regex is eagerly built in Initialize-RulePatterns
        $combined = $script:RX_GAMEKEY_COMBINED
        $combined | Should -Not -BeNullOrEmpty
        $combined | Should -BeOfType ([regex])
    }

    It 'Should reuse existing combined regex cache variable when present' {
        $previous = $script:RX_GAMEKEY_COMBINED
        try {
            $custom = [regex]::new('cachecustomtoken', 'IgnoreCase, Compiled')
            $script:RX_GAMEKEY_COMBINED = $custom

            $key = ConvertTo-GameKey -BaseName 'My CacheCustomToken Game (Europe)'
            $key | Should -Not -Match 'cachecustomtoken'
        } finally {
            $script:RX_GAMEKEY_COMBINED = $previous
        }
    }

    It 'Should include StoreTagPattern and Cleanup1 in combined regex' {
        # PERF-09: StoreTagPattern and Cleanup1 merged into combined regex
        $combined = $script:RX_GAMEKEY_COMBINED
        $combined | Should -Not -BeNullOrEmpty
        $pattern = $combined.ToString()
        # Cleanup1 pattern ([\._]+) should be part of the combined regex
        $pattern | Should -Match '\[\\\._\]'
    }

    It 'Should preserve normalized key output with cleanup patterns' {
        # Underscores/dots become spaces, then all spaces are collapsed (space-insensitive keys)
        $key = ConvertTo-GameKey -BaseName 'Cleanup___Test---Game (Europe)'
        $key | Should -Be 'cleanuptest---game'
    }
}

# ============================================================
# INTEGRATION: DUPLICATE GROUPING
# ============================================================

Describe 'GameKey Duplicate Grouping' {
    Context 'Real-World Scenarios' {
        
        It 'Should correctly group typical No-Intro variants' {
            $variants = @(
                'Crash Bandicoot (Europe)',
                'Crash Bandicoot (Europe) (EDC)',
                'Crash Bandicoot (Europe) (No EDC)',
                'Crash Bandicoot (USA)',
                'Crash Bandicoot (USA) (v1.0)',
                'Crash Bandicoot (USA) (v1.1)',
                'Crash Bandicoot (Japan)'
            )
            
            $keys = $variants | ForEach-Object { ConvertTo-GameKey -BaseName $_ }
            $uniqueKeys = @($keys | Select-Object -Unique)
            
            $uniqueKeys.Count | Should -Be 1 -Because 'All are the same game'
        }
        
        It 'Should NOT group different games' {
            $game1 = ConvertTo-GameKey -BaseName 'Crash Bandicoot (Europe)'
            $game2 = ConvertTo-GameKey -BaseName 'Crash Bandicoot 2 (Europe)'
            $game3 = ConvertTo-GameKey -BaseName 'Crash Team Racing (Europe)'
            
            $game1 | Should -Not -Be $game2
            $game2 | Should -Not -Be $game3
            $game1 | Should -Not -Be $game3
        }
        
        It 'Should handle BIOS files correctly' {
            $bios1 = ConvertTo-GameKey -BaseName '[BIOS] PlayStation (Europe)'
            $bios2 = ConvertTo-GameKey -BaseName '[BIOS] PlayStation (USA)'
            
            # BIOS region variants should group
            $bios1 | Should -Be $bios2
        }
    }
}

# ============================================================
# FILE CLASSIFICATION
# ============================================================

Describe 'Get-FileCategory' {
    Context 'BIOS Detection' {
        
        It 'Should detect [BIOS] prefix' {
            Get-FileCategory -BaseName '[BIOS] PlayStation' | Should -Be 'BIOS'
        }
        
        It 'Should detect (BIOS) tag' {
            Get-FileCategory -BaseName 'scph1001 (BIOS)' | Should -Be 'BIOS'
        }
        
        It 'Should detect (Firmware) tag' {
            Get-FileCategory -BaseName 'GBA BIOS (Firmware)' | Should -Be 'BIOS'
        }
    }
    
    Context 'JUNK Detection' {
        
        It 'Should detect (Demo)' {
            Get-FileCategory -BaseName 'Game (Demo)' | Should -Be 'JUNK'
        }
        
        It 'Should detect (Beta)' {
            Get-FileCategory -BaseName 'Game (Beta)' | Should -Be 'JUNK'
        }
        
        It 'Should detect (Proto)' {
            Get-FileCategory -BaseName 'Game (Proto)' | Should -Be 'JUNK'
        }
        
        It 'Should detect (Hack)' {
            Get-FileCategory -BaseName 'Game (Hack)' | Should -Be 'JUNK'
        }
        
        It 'Should detect [b] bad dump' {
            Get-FileCategory -BaseName 'Game [b]' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Game [b1]' | Should -Be 'JUNK'
        }
        
        It 'Should detect (Homebrew)' {
            Get-FileCategory -BaseName 'Cool Homebrew (Homebrew)' | Should -Be 'JUNK'
        }
        
        It 'Should detect (Unlicensed)' {
            Get-FileCategory -BaseName 'Bootleg Game (Unl)' | Should -Be 'JUNK'
        }
    }
    
    Context 'GAME Detection' {
        
        It 'Should classify normal games as GAME' {
            Get-FileCategory -BaseName 'Super Mario Bros (Europe)' | Should -Be 'GAME'
            Get-FileCategory -BaseName 'Zelda (USA) (Rev A)' | Should -Be 'GAME'
            Get-FileCategory -BaseName 'Final Fantasy VII (Japan) (Disc 1)' | Should -Be 'GAME'
        }
        
        It 'Should classify [!] verified as GAME' {
            Get-FileCategory -BaseName 'Game (Europe) [!]' | Should -Be 'GAME'
        }
    }

    Context 'Sampler & Bootleg Junk Detection' {

        It 'Should detect "Bootleg Sampler" as JUNK' {
            Get-FileCategory -BaseName 'Bootleg Sampler (USA)' | Should -Be 'JUNK'
            Get-FileCategory -BaseName 'Bootleg Sampler (Europe) (Made in USA)' | Should -Be 'JUNK'
        }

        It 'Should detect standalone "Sampler" in title as JUNK' {
            Get-FileCategory -BaseName 'PlayStation Sampler (USA)' | Should -Be 'JUNK'
        }

        It 'Should detect (Sampler) tag as JUNK' {
            Get-FileCategory -BaseName 'Some Game (Sampler)' | Should -Be 'JUNK'
        }

        It 'Should NOT flag game titles containing "sample" as substring' {
            Get-FileCategory -BaseName 'Samurai Shodown (USA)' | Should -Be 'GAME'
        }
    }
}

# ============================================================
# SOURCE-DISC TAG NORMALIZATION
# ============================================================

Describe 'Source-Disc Tag Normalization' {

    Context 'Single source-disc tags (1S), (2S), (3S)' {

        It 'Should produce same key for source-disc variants' {
            $base = ConvertTo-GameKey -BaseName 'Astal (USA)'
            $s1   = ConvertTo-GameKey -BaseName 'Astal (USA) (1S)'
            $s2   = ConvertTo-GameKey -BaseName 'Astal (USA) (2S)'
            $s3   = ConvertTo-GameKey -BaseName 'Astal (USA) (3S)'

            $s1 | Should -Be $base -Because '(1S) should be stripped'
            $s2 | Should -Be $base -Because '(2S) should be stripped'
            $s3 | Should -Be $base -Because '(3S) should be stripped'
        }

        It 'Should strip source tags for BlackFire variants' {
            $base = ConvertTo-GameKey -BaseName 'BlackFire (USA)'
            $s2   = ConvertTo-GameKey -BaseName 'BlackFire (USA) (2S)'
            $s3   = ConvertTo-GameKey -BaseName 'BlackFire (USA) (3S)'

            $s2 | Should -Be $base
            $s3 | Should -Be $base
        }
    }

    Context 'Multi source-disc tags (8S, 9S, 17S)' {

        It 'Should strip comma-separated source-disc tags' {
            $base  = ConvertTo-GameKey -BaseName 'Bootleg Sampler (USA)'
            $multi = ConvertTo-GameKey -BaseName 'Bootleg Sampler (USA) (8S, 9S, 17S)'
            $s15   = ConvertTo-GameKey -BaseName 'Bootleg Sampler (USA) (15S)'

            $multi | Should -Be $base -Because '(8S, 9S, 17S) should be stripped'
            $s15   | Should -Be $base -Because '(15S) should be stripped'
        }
    }

    Context '(Made in ...) tag stripping' {

        It 'Should strip (Made in USA) and similar tags' {
            $base    = ConvertTo-GameKey -BaseName 'Bootleg Sampler (Europe)'
            $madeIn  = ConvertTo-GameKey -BaseName 'Bootleg Sampler (Europe) (Made in USA)'

            $madeIn | Should -Be $base -Because '(Made in USA) should be stripped'
        }
    }
}

# ============================================================
# SPACE-INSENSITIVE NORMALIZATION
# ============================================================

Describe 'Space-Insensitive Game Key Normalization' {

    It 'Should produce same key for "Brain Dead 13" vs "BrainDead 13"' {
        $spaced = ConvertTo-GameKey -BaseName 'Brain Dead 13 (USA)'
        $nospace = ConvertTo-GameKey -BaseName 'BrainDead 13 (USA)'

        $spaced | Should -Be $nospace
    }

    It 'Should still differentiate truly different games' {
        $game1 = ConvertTo-GameKey -BaseName 'Super Mario World (USA)'
        $game2 = ConvertTo-GameKey -BaseName 'Super Mario Bros (USA)'

        $game1 | Should -Not -Be $game2
    }

    It 'Should handle hyphenated titles consistently' {
        $k1 = ConvertTo-GameKey -BaseName 'Spider-Man (USA)'
        $k2 = ConvertTo-GameKey -BaseName 'Spider-Man (Europe)'

        $k1 | Should -Be $k2
    }
}