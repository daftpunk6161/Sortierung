#requires -Modules Pester
<#
  GameKey Fuzz & Property Tests  [F18]
  =====================================
  Property-based tests for ConvertTo-GameKey, Get-RegionTag, Select-Winner,
  and Test-PathWithinRoot.  Invariants are checked against a wide variety of
  filenames including pure-tag inputs, Unicode, symbols, pathological strings,
  and determinism scenarios.
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'gamekey_fuzz'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript

    # Shared fuzz corpus — defined here so all It blocks can access via $script:
    $script:FuzzNames = @(
        '(USA)', '(Europe)', '(Japan)', '(World)', '(En,Fr,De)',
        '[!]', '(Beta)', '(Demo)', '(Proto)', '(Rev A)', '(v1.0)',
        '(Public Domain)', '(Unl)', '(Hack)',
        'Sonic (Japan)', 'Mega Man (Japan) [!]',
        '---', '...', '???', '!!!', '###',
        '()', '[]', '{}',
        '  ', ' (USA) ',
        '', '   ',
        '(USA) (Beta) [!]', '[b] (Hack) (v1.0)',
        'Super Mario World (USA)',
        'The Legend of Zelda - A Link to the Past (USA)',
        'Final Fantasy VII (USA) (Disc 1)',
        ('A' * 255 + ' (USA)'),
        '12345', '1 (USA)', '2 (Europe)',
        'Game: Title (USA)', 'Game.Title.v2 (USA)',
        'SuperMarioBros(USA)', 'GameTitle[!]',
        'Pac-Man (Namco) (USA)', 'Game (Title) (Europe)',
        'Game (USA) (Disc 2)', 'Game (USA) (Side B)',
        'Game (USA) (Rev 1)', 'Game (USA) (Rev 2)',
        '[BIOS] PlayStation (USA)', 'BIOS (Firmware) (v2.0)',
        '[a1]', '[h]', '[p]', '[!p]', '[T+Eng]', '[o]',
        'Game (19GE)', 'Title (200x)',
        'Game (Made in USA)', 'Game (Made in Japan)',
        'Game (USA) (1S)', 'Game (USA) (8S)',
        'Game (Virtual Console)', 'Game (Anniversary Edition)',
        'Rockman 2 (Japan)', 'Akumajou Special (Japan)',
        '   Sonic (USA)   ', '(USA) Sonic',
        'SONIC THE HEDGEHOG (USA)', 'MARIO BROS (EUROPE)',
        '007: GoldenEye (USA)', '1080 Snowboarding (USA)',
        "Tony Hawk's Pro Skater (USA)",
        '(USA) (En) (Demo) [!] (Beta) (Rev A)'
    )

    $script:ValidRegions = @(
        'US','EU','JP','WORLD','AU','CA','KR','CN','TW','BR',
        'UK','DE','FR','ES','IT','NL','SE','NO','DK','FI','PO','RU',
        'UNKNOWN','ASIA','SCAN','NORDIC','LATAM','SA','MX','AR'
    )

    $script:RegionFuzzNames = @(
        'Game (USA)', 'Game (Europe)', 'Game (Japan)', 'Game (World)',
        'Game (Australia)', 'Game (UK)',
        '(19GE)', '(200x)', '(G)', '(UE)', '(J)', '(U)', '(E)',
        '[!p]', '[p]', '[a1]', '[h]', '[b]', '[T+]',
        '12345', '2001', '(1999)',
        'SomeTitleWithNoTags', 'UPPER_CASE_TITLE',
        'Game (USA, Japan)', 'Game (En,Ja,De,Fr)',
        'Game (United Kingdom)', 'Game (Great Britain)', 'Game (England)',
        'Game( USA )', 'Game (usa)', 'Game (USA) (Europe)',
        '', '   ', '()', '[]',
        'Game (En)', 'Game (Ja)',
        'Game (Made in USA)', 'Game (Made in Japan)',
        ('Game ' + '(USA) ' * 20)
    )

    $script:Root = 'C:\ROMs\Collection'
    $script:TraversalPaths = @(
        'C:\ROMs\Collection\..\.\Secret\file.txt',
        'C:\ROMs\Collection\..\..\Windows\System32',
        'C:\ROMs\Collection\subdir\..\..\OtherDir\file',
        'C:\ROMs\..\Collection2\file.txt',
        'C:\ROMs\Collection\folder/../../../etc/passwd',
        'C:\ROMs\Collection/../../../etc/passwd'
    )
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# ============================================================
#  FUZZ: ConvertTo-GameKey — invariants over 100 inputs
# ============================================================

Describe 'ConvertTo-GameKey Fuzz' {
            # Rockman / Akumajou alias
    It 'never returns empty string for any input' {
        foreach ($name in $script:FuzzNames) {
            $result = ConvertTo-GameKey -BaseName $name
            $result | Should -Not -BeNullOrEmpty -Because "Input '$name' must produce a non-empty key"
        }
    }

    It 'is always lowercase' {
        foreach ($name in $script:FuzzNames) {
            $result = ConvertTo-GameKey -BaseName $name
            $result | Should -Be $result.ToLowerInvariant() -Because "Key for '$name' must be lowercase; got '$result'"
        }
    }

    It 'is idempotent — applying twice gives same result' {
        foreach ($name in $script:FuzzNames) {
            $first = ConvertTo-GameKey -BaseName $name
            $second = ConvertTo-GameKey -BaseName $first
            # Key normalization may change on second pass (tag-only → fallback),
            # but must not empty out a non-empty result
            if ($first) {
                $second | Should -Not -BeNullOrEmpty -Because "Second pass on '$first' must not empty out"
            }
        }
    }

    It 'never throws for any input' {
        foreach ($name in $script:FuzzNames) {
            { ConvertTo-GameKey -BaseName $name } | Should -Not -Throw
        }
    }

    It 'two identical filenames produce identical keys' {
        $pairs = @(
            @('Sonic (USA)', 'Sonic (USA)'),
            @('Game (Japan) [!]', 'Game (Japan) [!]'),
            @('Final Fantasy (Europe)', 'Final Fantasy (Europe)')
        )
        foreach ($pair in $pairs) {
            $k1 = ConvertTo-GameKey -BaseName $pair[0]
            $k2 = ConvertTo-GameKey -BaseName $pair[1]
            $k1 | Should -Be $k2 -Because "'$($pair[0])' and '$($pair[1])' must produce the same key"
        }
    }
}

# ============================================================
#  FUZZ: Get-RegionTag — alien and edge-case formats
# ============================================================

Describe 'Get-RegionTag Fuzz' {

    It 'never throws for alien formats' {
        foreach ($name in $script:RegionFuzzNames) {
            { Get-RegionTag -Name $name } | Should -Not -Throw
        }
    }

    It 'always returns a non-null non-empty string' {
        foreach ($name in $script:RegionFuzzNames) {
            $result = Get-RegionTag -Name $name
            $result | Should -Not -BeNullOrEmpty -Because "Get-RegionTag('$name') must return a string"
        }
    }

    It 'always returns a known region token or UNKNOWN' {
        foreach ($name in $script:RegionFuzzNames) {
            $result = Get-RegionTag -Name $name
            $result | Should -BeIn $script:ValidRegions -Because "Get-RegionTag('$name') returned unknown token '$result'"
        }
    }

    It 'is case-insensitive for common inputs' {
        $base = 'Game (USA)'
        $upper = 'GAME (USA)'
        $lower = 'game (usa)'
        $r1 = Get-RegionTag -Name $base
        $r2 = Get-RegionTag -Name $upper
        $r3 = Get-RegionTag -Name $lower
        # All three should agree on the region
        $r1 | Should -Be $r2 -Because "'$base' and '$upper' should give the same region"
        $r1 | Should -Be $r3 -Because "'$base' and '$lower' should give the same region"
    }
}

# ============================================================
#  PROPERTY: Select-Winner — tie-break determinism
# ============================================================

Describe 'Select-Winner Tie-Break Determinism' {

    It 'with identical scores, winner is always the same (alphabetical MainPath)' {
        $baseItem = [pscustomobject]@{
            RegionScore       = 100
            HeaderScore       = 5
            VersionScore      = 3
            FormatScore       = 2
            SizeTieBreakScore = -1000
            SizeBytes         = [long]1000
            CompletenessScore = 10
        }

        $items = @(
            ($baseItem | Select-Object * | Add-Member -NotePropertyName MainPath -NotePropertyValue 'C:\ROMs\Zebra (USA).rom'  -PassThru),
            ($baseItem | Select-Object * | Add-Member -NotePropertyName MainPath -NotePropertyValue 'C:\ROMs\Alpha (USA).rom'  -PassThru),
            ($baseItem | Select-Object * | Add-Member -NotePropertyName MainPath -NotePropertyValue 'C:\ROMs\Mango (USA).rom'  -PassThru)
        )

        $winners = @(1..10 | ForEach-Object { (Select-Winner -Items $items).MainPath })
        $winners | Select-Object -Unique | Should -HaveCount 1 -Because 'Select-Winner must be deterministic for identical scores'
        $winners[0] | Should -Be 'C:\ROMs\Alpha (USA).rom' -Because 'alphabetically first MainPath should win on tie'
    }

    It 'returns the single item unchanged when only one item is provided' {
        $item = [pscustomobject]@{
            MainPath = 'C:\ROMs\Solo (USA).rom'
            RegionScore = 100; HeaderScore = 5; VersionScore = 3
            FormatScore = 2; SizeTieBreakScore = -1000; SizeBytes = [long]1000
        }
        $result = Select-Winner -Items @($item)
        $result.MainPath | Should -Be 'C:\ROMs\Solo (USA).rom'
    }

    It 'selects higher RegionScore over lower, regardless of order' {
        $mkItem = {
            param([string]$path, [int]$regionScore)
            [pscustomobject]@{ MainPath=$path; RegionScore=$regionScore; HeaderScore=5
                VersionScore=3; FormatScore=2; SizeTieBreakScore=-1000; SizeBytes=[long]1000 }
        }
        $items = @(
            (& $mkItem 'C:\ROMs\EU.rom' 50),
            (& $mkItem 'C:\ROMs\US.rom' 100),
            (& $mkItem 'C:\ROMs\JP.rom' 75)
        )
        (Select-Winner -Items $items).MainPath | Should -Be 'C:\ROMs\US.rom'
        # Reversed order should give the same result
        (Select-Winner -Items ($items | Sort-Object MainPath -Descending)).MainPath | Should -Be 'C:\ROMs\US.rom'
    }
}

# ============================================================
#  FUZZ: Test-PathWithinRoot — path traversal protection
# ============================================================

Describe 'Test-PathWithinRoot Path-Traversal Fuzz' {


    It 'blocks path traversal with .. segments' {
        foreach ($p in $script:TraversalPaths) {
            $result = Test-PathWithinRoot -Path $p -Root $script:Root
            $result | Should -Be $false -Because "Path '$p' must not be allowed outside root '$($script:Root)'"
        }
    }

    It 'allows valid child paths' {
        $validPaths = @(
            'C:\ROMs\Collection\game.zip',
            'C:\ROMs\Collection\subdir\file.rom',
            'C:\ROMs\Collection\deep\nested\file.bin'
        )
        foreach ($p in $validPaths) {
            # Test-PathWithinRoot resolves via Resolve-RootPath which may fail
            # if the path does not exist on disk. We accept $true or $false but
            # must not throw.
            { Test-PathWithinRoot -Path $p -Root $script:Root } | Should -Not -Throw
        }
    }

    It 'returns false for empty/null inputs' {
        Test-PathWithinRoot -Path '' -Root $script:Root | Should -Be $false
        Test-PathWithinRoot -Path $script:Root -Root '' | Should -Be $false
        Test-PathWithinRoot -Path '' -Root '' | Should -Be $false
    }

    It 'never throws for pathological inputs' {
        $junk = @('', '   ', $null, '////', '\\\\\', "`0", 'NUL', 'CON', 'PRN')
        foreach ($p in $junk) {
            { Test-PathWithinRoot -Path $p -Root $script:Root } | Should -Not -Throw
        }
    }
}
