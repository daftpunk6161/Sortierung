#requires -Modules Pester

Describe 'API OpenAPI drift' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:yamlPath = Join-Path $repoRoot 'docs\openapi.yaml'
        $script:jsonPath = Join-Path $repoRoot 'docs\openapi.generated.json'
    }

    It 'keeps openapi.yaml and openapi.generated.json aligned on version headers' {
        Test-Path -LiteralPath $script:yamlPath -PathType Leaf | Should -BeTrue
        Test-Path -LiteralPath $script:jsonPath -PathType Leaf | Should -BeTrue

        $yamlContent = Get-Content -LiteralPath $script:yamlPath -Raw -Encoding UTF8
        $jsonContent = Get-Content -LiteralPath $script:jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json

        $yamlOpenApi = [regex]::Match($yamlContent, '(?im)^openapi:\s*([^\r\n]+)\s*$').Groups[1].Value.Trim('"','''',' ')
        $yamlInfoVersion = [regex]::Match($yamlContent, '(?im)^\s{2}version:\s*([^\r\n]+)\s*$').Groups[1].Value.Trim('"','''',' ')

        [string]$yamlOpenApi | Should -Not -BeNullOrEmpty
        [string]$yamlInfoVersion | Should -Not -BeNullOrEmpty

        [string]$jsonContent.openapi | Should -Be $yamlOpenApi
        [string]$jsonContent.info.version | Should -Be $yamlInfoVersion
    }

    It 'documents all MVP endpoints in generated OpenAPI artifact' {
        $json = Get-Content -LiteralPath $script:jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $paths = @($json.paths.PSObject.Properties.Name)

        $paths | Should -Contain '/health'
        $paths | Should -Contain '/runs'
        $paths | Should -Contain '/runs/{runId}'
        $paths | Should -Contain '/runs/{runId}/result'
        $paths | Should -Contain '/runs/{runId}/cancel'
        $paths | Should -Contain '/openapi'
    }
}
