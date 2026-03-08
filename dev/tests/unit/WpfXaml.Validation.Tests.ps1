#requires -Modules Pester

Describe 'WPF XAML statische Validierung' {
    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\WpfXaml.ps1')
        $script:mainWindowPath = Join-Path $root 'dev\modules\wpf\MainWindow.xaml'
        $script:themePath = Join-Path $root 'dev\modules\wpf\Theme.Resources.xaml'
    }

    It 'enthält keine ungültige Attached-Property Grid.Col' {
        $script:RC_XAML_MAIN | Should -Not -Match 'Grid\.Col\s*='
    }

    It 'enthält ein MainWindow-Root mit TabControl' {
        $script:RC_XAML_MAIN | Should -Match '<Window'
        $script:RC_XAML_MAIN | Should -Match '<TabControl'
    }

    It 'lädt MainWindow-XAML aus externer Datei' {
        Test-Path -LiteralPath $script:mainWindowPath -PathType Leaf | Should -BeTrue
        (Get-WpfMainWindowXaml) | Should -Match '<Window'
    }

    It 'lädt Theme-ResourceDictionary aus eigener Datei' {
        Test-Path -LiteralPath $script:themePath -PathType Leaf | Should -BeTrue
        (Get-WpfThemeResourceDictionaryXaml) | Should -Match '<ResourceDictionary'
    }

    It 'Geladene XAML und MainWindow.xaml enthalten identische kritische x:Name-Controls' {
        $inline = [string]$script:RC_XAML_MAIN
        $external = Get-Content -LiteralPath $script:mainWindowPath -Raw -ErrorAction Stop

        $getNames = {
            param([string]$text)
            $names = New-Object System.Collections.Generic.List[string]
            foreach ($m in [regex]::Matches($text, 'x:Name\s*=\s*"([^"]+)"')) {
                $name = [string]$m.Groups[1].Value
                if ($name -match '^(btn|chk|txt|cmb|tab|lbl|prg|list|dg|exp|grp|brd)') {
                    [void]$names.Add($name)
                }
            }
            return @($names | Sort-Object -Unique)
        }

        $inlineNames = & $getNames $inline
        $externalNames = & $getNames $external

        $inlineNames | Should -Be $externalNames
    }
}
