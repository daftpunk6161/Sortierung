#requires -Modules Pester

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'rule_reg_pack'
    $script:TempScript = $ctx.TempScript
    . $script:TempScript

    $fixturePath = Join-Path $PSScriptRoot 'fixtures\rule-regression-pack.json'
    $script:RuleCases = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'Rule Regression Pack (F-09)' {
    It 'fixture should be present and contain cases' {
        $script:RuleCases | Should -Not -BeNullOrEmpty
        @($script:RuleCases).Count | Should -BeGreaterThan 5
    }

    It 'all fixture names should classify into expected regions and stable keys' {
        foreach ($case in @($script:RuleCases)) {
            $actualRegion = Get-RegionTag -Name ([string]$case.Name)
            $actualRegion | Should -Be ([string]$case.ExpectedRegion)

            $actualKey = ConvertTo-GameKey -BaseName ([string]$case.Name)
            $actualKey | Should -Not -BeNullOrEmpty
            if ($case.ExpectedKeyContains) {
                $expected = ([string]$case.ExpectedKeyContains) -replace '\s+', ''
                $actualKey | Should -BeLike "*${expected}*"
            }
        }
    }
}
