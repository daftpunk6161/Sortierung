#requires -Modules Pester

Describe 'Naming — Script variable conventions' {
    It 'uses only UPPER_SNAKE_CASE or PascalCase for $script: variables' {
        $modulesRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'modules'
        $files = @(Get-ChildItem -LiteralPath $modulesRoot -Filter '*.ps1' -File -ErrorAction Stop)

        $allowedPattern = '^(?:_[A-Z][A-Za-z0-9]*|[A-Z][A-Za-z0-9]*|[A-Z][A-Z0-9_]*|[a-z][A-Za-z0-9]*)$'
        $offenders = New-Object System.Collections.Generic.List[string]

        foreach ($file in $files) {
            $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
            $matches = [regex]::Matches($content, '\$script:([A-Za-z_][A-Za-z0-9_]*)')
            foreach ($match in $matches) {
                $name = [string]$match.Groups[1].Value
                if (-not [regex]::IsMatch($name, $allowedPattern)) {
                    [void]$offenders.Add(("{0}:{1}" -f $file.Name, $name))
                }
            }
        }

        $offenders | Should -BeNullOrEmpty -Because 'script variable names should follow one consistent convention family'
    }
}
