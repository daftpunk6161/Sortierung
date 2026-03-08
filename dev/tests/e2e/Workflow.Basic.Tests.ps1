#requires -Modules Pester

BeforeAll {
    . (Join-Path (Split-Path -Parent $PSScriptRoot) 'TestScriptLoader.ps1')
    . (Join-Path (Split-Path -Parent $PSScriptRoot) 'FixtureFactory.ps1')

    $ctx = New-SimpleSortTestScript -TestsRoot (Split-Path -Parent $PSScriptRoot) -TempPrefix 'e2e_basic'
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'E2E Folder - Basic workflow' {
    It 'runs a deterministic DryRun from e2e test folder' {
        $root = Join-Path $TestDrive 'e2e_folder_root'
        New-Item -ItemType Directory -Path $root -Force | Out-Null

        'eu' | Out-File -LiteralPath (Join-Path $root 'Folder Test (Europe).chd') -Encoding ascii -Force
        'us' | Out-File -LiteralPath (Join-Path $root 'Folder Test (USA).chd') -Encoding ascii -Force

        $result = Invoke-RegionDedupe `
            -Roots @($root) `
            -Mode 'DryRun' `
            -PreferOrder @('EU','US','WORLD','JP') `
            -IncludeExtensions @('.chd') `
            -RemoveJunk $true `
            -SeparateBios $false `
            -GenerateReportsInDryRun $false `
            -UseDat $false `
            -Log { param($m) }

        $result | Should -Not -BeNullOrEmpty
        @($result.Winners).Count | Should -Be 1
        [string]$result.Winners[0].MainPath | Should -Match 'Europe'
    }
}
