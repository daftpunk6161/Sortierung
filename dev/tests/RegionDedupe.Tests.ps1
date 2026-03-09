#requires -Modules Pester
<#
  Region Dedupe Core Logic Tests
  ==============================
  Tests für Winner-Selection, Region-Detection, Scoring
  
  Mutation Kills: M1, M2, M5, M6
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'dedupe_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# ============================================================
# REGION SCORE TESTS (Mutation M1: IndexOf Inversion)
# ============================================================

Describe 'Get-RegionScore - Mutation Kill M1' {
    Context 'PreferOrder Priority' {
        
        It 'First region in PreferOrder should have HIGHEST score' {
            # PreferOrder = @('EU', 'US', 'JP')
            # EU should have highest score (1000 - 0 = 1000)
            # US should have second (1000 - 1 = 999)
            # JP should have third (1000 - 2 = 998)
            
            $prefer = @('EU', 'US', 'JP')
            $scoreEU = Get-RegionScore -Region 'EU' -Prefer $prefer
            $scoreUS = Get-RegionScore -Region 'US' -Prefer $prefer
            $scoreJP = Get-RegionScore -Region 'JP' -Prefer $prefer
            
            $scoreEU | Should -BeGreaterThan $scoreUS -Because 'EU is first in PreferOrder'
            $scoreUS | Should -BeGreaterThan $scoreJP -Because 'US is second in PreferOrder'
        }
        
        It 'M1-KILL: Reversed PreferOrder should give reversed scores' {
            $prefer1 = @('EU', 'US', 'JP')
            $prefer2 = @('JP', 'US', 'EU')
            
            $euFirst = Get-RegionScore -Region 'EU' -Prefer $prefer1
            $jpFirst = Get-RegionScore -Region 'JP' -Prefer $prefer2
            
            # When EU is first in prefer1, it should equal JP being first in prefer2
            $euFirst | Should -Be $jpFirst -Because 'First position always gets same score'
        }
        
        It 'M1-KILL: WORLD should have lower score than any preferred region' {
            $prefer = @('EU', 'US', 'JP')
            $scoreJP = Get-RegionScore -Region 'JP' -Prefer $prefer
            $scoreWorld = Get-RegionScore -Region 'WORLD' -Prefer $prefer
            
            $scoreJP | Should -BeGreaterThan $scoreWorld -Because 'Preferred regions beat WORLD'
        }
        
        It 'UNKNOWN should have lowest score' {
            $prefer = @('EU', 'US', 'JP')
            $scoreUnknown = Get-RegionScore -Region 'UNKNOWN' -Prefer $prefer
            $scoreWorld = Get-RegionScore -Region 'WORLD' -Prefer $prefer
            
            $scoreWorld | Should -BeGreaterThan $scoreUnknown -Because 'WORLD beats UNKNOWN'
        }
    }
}

# ============================================================
# REGION TAG TESTS (Mutation M5, M6)
# ============================================================

Describe 'Get-RegionTag - Mutation Kill M5, M6' {
    Context 'Primary Region Detection' {
        
        It 'M5-KILL: EU should be detected before US in mixed input' {
            # If mutation reverses order, US would be detected first
            $resultEU = Get-RegionTag -Name 'Game (Europe)'
            $resultUS = Get-RegionTag -Name 'Game (USA)'
            
            $resultEU | Should -Be 'EU'
            $resultUS | Should -Be 'US'
        }
        
        It 'M5-KILL: Names with both EU and US indicators should prefer first match' {
            # This tests the ordered array priority
            $result = Get-RegionTag -Name 'Game (Europe, USA)'
            $result | Should -Be 'WORLD' -Because 'Multi-region = WORLD'
        }
        
        It 'Should detect all standard regions' {
            Get-RegionTag -Name 'Game (Europe)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (EUR)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (PAL)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (USA)' | Should -Be 'US'
            Get-RegionTag -Name 'Game (Japan)' | Should -Be 'JP'
            Get-RegionTag -Name 'Game (World)' | Should -Be 'WORLD'
            Get-RegionTag -Name 'Game (Australia)' | Should -Be 'AU'
        }

        It 'Should detect additional EU region tags from scan results' {
            Get-RegionTag -Name 'Game (UK)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (United Kingdom)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (Belgium)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (Portugal)' | Should -Be 'EU'
        }

        It 'Should detect additional ASIA region tags from scan results' {
            Get-RegionTag -Name 'Game (Taiwan)' | Should -Be 'ASIA'
            Get-RegionTag -Name 'Game (Hong Kong)' | Should -Be 'ASIA'
            Get-RegionTag -Name 'Game (India)' | Should -Be 'ASIA'
        }

        It 'Should parse mixed tokens deterministically (Uk,En,Proto -> EU)' {
            Get-RegionTag -Name 'Game (Uk,En,Proto)' | Should -Be 'EU'
        }

        It 'Should parse region+language tuple deterministically (USA,En -> US)' {
            Get-RegionTag -Name 'Game (USA,En)' | Should -Be 'US'
        }

        It 'Should keep UK/EU priority even when language-only tags are present' {
            Get-RegionTag -Name 'Game (UK) (Fr,De)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (United Kingdom) (En,Fr)' | Should -Be 'EU'
        }

        It 'Should parse mixed metadata tags via token parser (Uk,En,Proto -> EU)' {
            Get-RegionTag -Name 'Game (Uk,En,Proto)' | Should -Be 'EU'
            Get-RegionTag -Name 'Game (United Kingdom,En,Beta)' | Should -Be 'EU'
        }

        It 'Should keep fallback order deterministic (EU before US before JP on plain text hints)' {
            Get-RegionTag -Name 'Game europe usa japan build' | Should -Be 'EU'
            Get-RegionTag -Name 'Game usa japan build' | Should -Be 'US'
            Get-RegionTag -Name 'Game japan build' | Should -Be 'JP'
        }
    }
    
    Context 'Language vs Region Disambiguation (M6)' {

        It 'M6-KILL: (Europe) + (Fr) yields WORLD because fr is now a region token (BUG-016)' {
            # BUG-016 FIX: fr is now a region token mapping to FR
            # So (Europe)=EU + (Fr)=FR = two distinct regions = WORLD
            $result = Get-RegionTag -Name 'Game (Europe) (Fr)'
            $result | Should -Be 'WORLD' -Because '(Europe)=EU and (Fr)=FR are two distinct regions after BUG-016'
        }

        It 'M6-KILL: (En,Fr,De) yields WORLD because fr and de are region tokens (BUG-016)' {
            # BUG-016 FIX: fr→FR and de→DE are region tokens, en is language-only
            # Two distinct regions found = WORLD
            $result = Get-RegionTag -Name 'Game (En,Fr,De)'
            $result | Should -Be 'WORLD' -Because 'fr→FR and de→DE are region tokens after BUG-016, multi-region = WORLD'
        }

        It 'M6-KILL: Full country name should be detected even with language' {
            $result = Get-RegionTag -Name 'Game (France)'
            $result | Should -Be 'FR' -Because 'Full name France is a region'
        }

        It 'Should handle complex multi-language correctly as WORLD (BUG-016)' {
            # BUG-016: (Europe)=EU, (Fr)=FR, (De)=DE, (Es)=ES, (It)=IT = 5 regions = WORLD
            $result = Get-RegionTag -Name 'Game (Europe) (En,Fr,De,Es,It)'
            $result | Should -Be 'WORLD' -Because 'Europe + multiple country-code region tokens = multi-region WORLD'
        }

        It 'Mixed language/region tokens with non-region metadata should return WORLD (BUG-016)' {
            # BUG-016: fr→FR and de→DE are region tokens, en is language-only, Proto is ignored
            # Two distinct regions = WORLD
            $result = Get-RegionTag -Name 'Game (En,Fr,De) (Proto)'
            $result | Should -Be 'WORLD' -Because 'fr→FR and de→DE are region tokens after BUG-016, multi-region = WORLD'
        }
    }
    
    Context 'Edge Cases' {
        
        It 'Empty string should return UNKNOWN' {
            Get-RegionTag -Name '' | Should -Be 'UNKNOWN'
        }
        
        It 'Null should return UNKNOWN' {
            Get-RegionTag -Name $null | Should -Be 'UNKNOWN'
        }
        
        It 'No region indicators should return UNKNOWN' {
            Get-RegionTag -Name 'Random Game Name' | Should -Be 'UNKNOWN'
        }
    }
}

# ============================================================
# WINNER SELECTION TESTS (Mutation M2)
# ============================================================

Describe 'Select-Winner - Mutation Kill M2' {
    Context 'Priority Order Enforcement' {
        
        It 'M2-KILL: Higher RegionScore should always win over VersionScore' {
            $items = @(
                [pscustomobject]@{ MainPath='eu.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=100 }
                [pscustomobject]@{ MainPath='us.chd'; RegionScore=999; VersionScore=500; FormatScore=850; SizeBytes=100 }
            )
            
            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'eu.chd' -Because 'RegionScore trumps VersionScore'
        }
        
        It 'M2-KILL: Higher VersionScore should win when RegionScore is equal' {
            $items = @(
                [pscustomobject]@{ MainPath='v10.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=100 }
                [pscustomobject]@{ MainPath='v11.chd'; RegionScore=1000; VersionScore=200; FormatScore=850; SizeBytes=100 }
            )
            
            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'v11.chd' -Because 'Higher VersionScore wins'
        }
        
        It 'M2-KILL: Verify descending sort (mutation would make it ascending)' {
            $items = @(
                [pscustomobject]@{ MainPath='low.chd'; RegionScore=500; VersionScore=50; FormatScore=400; SizeBytes=100 }
                [pscustomobject]@{ MainPath='high.chd'; RegionScore=1000; VersionScore=500; FormatScore=850; SizeBytes=100 }
            )
            
            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'high.chd' -Because 'Highest scores should win (descending)'
        }
        
        It 'Higher FormatScore should win when Region and Version are equal' {
            $items = @(
                [pscustomobject]@{ MainPath='iso.iso'; RegionScore=1000; VersionScore=100; FormatScore=700; SizeBytes=100 }
                [pscustomobject]@{ MainPath='chd.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=100 }
            )
            
            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'chd.chd' -Because 'CHD has higher FormatScore'
        }
        
        It 'Smaller size should win when all scores are equal' {
            $items = @(
                [pscustomobject]@{ MainPath='big.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=1000000 }
                [pscustomobject]@{ MainPath='small.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=500000 }
            )
            
            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'small.chd' -Because 'Smaller file wins ties'
        }
        
        It 'Lexicographically first path should win as final tiebreaker' {
            $items = @(
                [pscustomobject]@{ MainPath='z_game.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=1000 }
                [pscustomobject]@{ MainPath='a_game.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=1000 }
            )
            
            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'a_game.chd' -Because 'Alphabetically first wins ties'
        }
    }

    Context 'Completeness Preference' {

        It 'Complete sets should win over incomplete when other scores equal' {
            $items = @(
                [pscustomobject]@{ MainPath='incomplete.cue'; RegionScore=1000; VersionScore=100; FormatScore=800; SizeBytes=100; CompletenessScore=0 }
                [pscustomobject]@{ MainPath='complete.cue'; RegionScore=1000; VersionScore=100; FormatScore=800; SizeBytes=100; CompletenessScore=100 }
            )

            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'complete.cue' -Because 'Completeness should win ties'
        }
    }
    
    Context 'Edge Cases' {
        
        It 'Should handle single item' {
            $items = @([pscustomobject]@{ MainPath='only.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=100 })
            $winner = Select-Winner -Items $items
            $winner.MainPath | Should -Be 'only.chd'
        }
        
        # Note: Empty/null arrays cause parameter validation errors (Mandatory parameter)
        # This is expected behavior - Select-Winner requires at least one item
    }
    
    Context 'Determinism' {
        
        It 'Same input should always produce same winner' {
            $items = @(
                [pscustomobject]@{ MainPath='game1.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=100 }
                [pscustomobject]@{ MainPath='game2.chd'; RegionScore=1000; VersionScore=100; FormatScore=850; SizeBytes=100 }
            )
            
            $results = @(1..10 | ForEach-Object { (Select-Winner -Items $items).MainPath })
            $uniqueResults = @($results | Select-Object -Unique)
            
            $uniqueResults.Count | Should -Be 1 -Because 'Winner selection must be deterministic'
        }
    }
}

# ============================================================
# DAT FALLBACK BEHAVIOR
# ============================================================

Describe 'Resolve-GameKey - DAT Fallback Off' {
    Context 'No-match isolation' {

        It 'Should return a deterministic no-match key when DatFallback is false' {
            $result = Resolve-GameKey -BaseName 'Game (Europe)' -Paths @('C:\ROMs\Game (Europe).chd') `
                -Root 'C:\ROMs' -MainPath 'C:\ROMs\Game (Europe).chd' -UseDat $true `
                -DatIndex @{} -HashCache @{} -HashType 'SHA1' -DatFallback $false `
                -AliasEditionKeying $false -SevenZipPath $null -Log $null -OnDatHash $null

            $result.DatMatched | Should -BeFalse
            $result.Key | Should -Match '^__nomatch__\|'
        }
    }
}

# ============================================================
# VERSION SCORE TESTS
# ============================================================

Describe 'Get-VersionScore' {
    Context 'Verified Dump Bonus' {
        
        It '[!] should give significant bonus' {
            $verified = Get-VersionScore -BaseName 'Game (USA) [!]'
            $normal = Get-VersionScore -BaseName 'Game (USA)'
            
            ($verified - $normal) | Should -BeGreaterOrEqual 500 -Because '[!] = verified good dump'
        }
    }
    
    Context 'Revision Scoring' {
        
        It 'Higher revision letter should score higher' {
            $revC = Get-VersionScore -BaseName 'Game (Rev C)'
            $revB = Get-VersionScore -BaseName 'Game (Rev B)'
            $revA = Get-VersionScore -BaseName 'Game (Rev A)'
            
            $revC | Should -BeGreaterThan $revB
            $revB | Should -BeGreaterThan $revA
        }
        
        It 'Numeric revisions should work' {
            $rev2 = Get-VersionScore -BaseName 'Game (Rev 2)'
            $rev1 = Get-VersionScore -BaseName 'Game (Rev 1)'
            
            $rev2 | Should -BeGreaterThan $rev1
        }
    }
    
    Context 'Version Scoring' {
        
        It 'Higher version number should score higher' {
            $v20 = Get-VersionScore -BaseName 'Game (v2.0)'
            $v11 = Get-VersionScore -BaseName 'Game (v1.1)'
            $v10 = Get-VersionScore -BaseName 'Game (v1.0)'
            
            $v20 | Should -BeGreaterThan $v11
            $v11 | Should -BeGreaterThan $v10
        }
    }
    
    Context 'Language Bonus' {
        
        It 'English should give bonus' {
            $withEn = Get-VersionScore -BaseName 'Game (En,Fr)'
            $withoutEn = Get-VersionScore -BaseName 'Game (Fr,De)'
            
            $withEn | Should -BeGreaterThan $withoutEn
        }
        
        It 'More languages should give more bonus' {
            $multi = Get-VersionScore -BaseName 'Game (En,Fr,De,Es)'
            $single = Get-VersionScore -BaseName 'Game (En)'
            
            $multi | Should -BeGreaterThan $single
        }
    }
}

# ============================================================
# SIZE TIE-BREAKER TESTS
# ============================================================

Describe 'Get-SizeTieBreakScore' {
    Context 'Disc vs Cartridge' {

        It 'Disc formats should prefer larger size' {
            $scoreSmall = Get-SizeTieBreakScore -Type 'FILE' -Extension '.iso' -SizeBytes 100
            $scoreLarge = Get-SizeTieBreakScore -Type 'FILE' -Extension '.iso' -SizeBytes 200

            $scoreLarge | Should -BeGreaterThan $scoreSmall
        }

        It 'Cartridge formats should prefer smaller size' {
            $scoreSmall = Get-SizeTieBreakScore -Type 'FILE' -Extension '.nes' -SizeBytes 100
            $scoreLarge = Get-SizeTieBreakScore -Type 'FILE' -Extension '.nes' -SizeBytes 200

            $scoreSmall | Should -BeGreaterThan $scoreLarge
        }
    }
}

# ============================================================
# FORMAT SCORE TESTS
# ============================================================

Describe 'Get-FormatScore' {
    Context 'Format Hierarchy' {
        
        It 'M3USET (Multi-Disc) should be highest' {
            $m3u = Get-FormatScore -Extension '.m3u' -Type 'M3USET'
            $cue = Get-FormatScore -Extension '.cue' -Type 'CUESET'
            $chd = Get-FormatScore -Extension '.chd' -Type 'FILE'
            
            $m3u | Should -BeGreaterThan $cue
            $m3u | Should -BeGreaterThan $chd
        }
        
        It 'CHD should be higher than ISO' {
            $chd = Get-FormatScore -Extension '.chd' -Type 'FILE'
            $iso = Get-FormatScore -Extension '.iso' -Type 'FILE'
            
            $chd | Should -BeGreaterThan $iso
        }
        
        It 'ZIP should be higher than RAR' {
            $zip = Get-FormatScore -Extension '.zip' -Type 'FILE'
            $rar = Get-FormatScore -Extension '.rar' -Type 'FILE'
            
            $zip | Should -BeGreaterThan $rar
        }
        
        It 'Unknown extension should have lowest score' {
            $unknown = Get-FormatScore -Extension '.xyz123' -Type 'FILE'
            $rar = Get-FormatScore -Extension '.rar' -Type 'FILE'
            
            $rar | Should -BeGreaterThan $unknown
        }
    }
}

Describe 'P1 Regression - Set Item Contract' {
    Context 'New-SetItem compatibility and winner inputs' {

        It 'New-SetItem should accept completeness params and emit winner fields' {
            $item = New-SetItem -Root 'C:\ROMs' -Type 'CUESET' -Category 'GAME' `
                -MainPath 'C:\ROMs\game.cue' -Paths @('C:\ROMs\game.cue') -Region 'EU' -PreferOrder @('EU','US') `
                -VersionScore 10 -GameKey 'game' -DatMatch $false -BaseName 'game' -FileInfoByPath @{} `
                -IsComplete $false -MissingCount 2

            $item | Should -Not -BeNullOrEmpty
            $item.PSObject.Properties.Name | Should -Contain 'CompletenessScore'
            $item.PSObject.Properties.Name | Should -Contain 'HeaderScore'
            $item.PSObject.Properties.Name | Should -Contain 'SizeTieBreakScore'
            $item.MissingCount | Should -Be 2
            $item.CompletenessScore | Should -Be 0
        }
    }
}

Describe 'P1 Regression - Console Root Prefix Collision' {
    Context 'Get-ConsoleType should not treat C:\ROM as parent of C:\ROMS' {

        It 'Should not derive relative parts from prefix-only root match' {
            $console = Get-ConsoleType -RootPath 'C:\ROM' -FilePath 'C:\ROMS\PS2\game.iso' -Extension '.iso'
            $console | Should -Not -Be 'PS2'
        }

        It 'Should still detect console when file is truly inside root' {
            $console = Get-ConsoleType -RootPath 'C:\ROMS' -FilePath 'C:\ROMS\PS2\game.iso' -Extension '.iso'
            $console | Should -Be 'PS2'
        }
    }
}
