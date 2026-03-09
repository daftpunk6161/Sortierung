#requires -Modules Pester

Describe 'ApiServer integration' {
    BeforeAll {
        $script:repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:moduleRoot = Join-Path $script:repoRoot 'dev\modules'
        $script:apiKey = 'integration-test-key'
        $script:apiScript = Join-Path $script:repoRoot 'Invoke-RomCleanupApi.ps1'

        $socket = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $socket.Start()
        $script:port = ([int]$socket.LocalEndpoint.Port)
        $socket.Stop()

        $script:testRoot = Join-Path $env:TEMP ("romcleanup_api_integration_{0}" -f ([guid]::NewGuid().ToString('N')))
        [void](New-Item -ItemType Directory -Path $script:testRoot -Force)

        $engine = Get-Command pwsh -ErrorAction SilentlyContinue
        if (-not $engine) { $engine = Get-Command powershell.exe -ErrorAction SilentlyContinue }
        if (-not $engine) { throw 'No PowerShell engine found for API integration test.' }

        $argList = @(
            '-NoProfile',
            '-File',
            $script:apiScript,
            '-Port',
            [string]$script:port,
            '-ApiKey',
            $script:apiKey,
            '-PollIntervalMs',
            '50'
        )
        $script:apiProcess = Start-Process -FilePath $engine.Source -ArgumentList $argList -WorkingDirectory $script:repoRoot -WindowStyle Hidden -PassThru

        $script:baseUri = "http://127.0.0.1:$($script:port)"

        $headers = @{ 'X-Api-Key' = $script:apiKey }
        $healthy = $false
        for ($i = 0; $i -lt 40; $i++) {
            try {
                $probe = Invoke-RestMethod -Uri "$($script:baseUri)/health" -Method Get -Headers $headers -ErrorAction Stop
                if ($probe.status -eq 'ok') {
                    $healthy = $true
                    break
                }
            } catch {
                Start-Sleep -Milliseconds 250
            }
        }

        if (-not $healthy) {
            throw 'API server did not become healthy within timeout.'
        }
    }

    AfterAll {
        if ($script:apiProcess) {
            try {
                if (-not $script:apiProcess.HasExited) {
                    $script:apiProcess.Kill()
                    $script:apiProcess.WaitForExit(5000) | Out-Null
                }
            } catch { }
        }

        if ($script:testRoot -and (Test-Path -LiteralPath $script:testRoot)) {
            Remove-Item -LiteralPath $script:testRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'returns 401 without API key' {
        $params = @{
            Uri = "$($script:baseUri)/health"
            Method = 'GET'
            ErrorAction = 'Stop'
        }
        if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('SkipHttpErrorCheck')) {
            $params['SkipHttpErrorCheck'] = $true
            $response = Invoke-WebRequest @params
            [int]$response.StatusCode | Should -Be 401
            return
        }

        {
            Invoke-WebRequest @params | Out-Null
        } | Should -Throw
    }

    It 'returns health payload with valid API key' {
        $response = Invoke-RestMethod -Uri "$($script:baseUri)/health" -Method Get -Headers @{ 'X-Api-Key' = $script:apiKey }
        [string]$response.status | Should -Be 'ok'
    }

    It 'returns CORS headers on authenticated health request' {
        $response = Invoke-WebRequest -Uri "$($script:baseUri)/health" -Method Get -Headers @{ 'X-Api-Key' = $script:apiKey } -ErrorAction Stop
        [string]$response.Headers['Access-Control-Allow-Origin'] | Should -Not -BeNullOrEmpty
        [string]$response.Headers['Access-Control-Allow-Methods'] | Should -Match 'OPTIONS'
    }

    It 'accepts OPTIONS preflight without API key and returns CORS headers' {
        $response = Invoke-WebRequest -Uri "$($script:baseUri)/runs" -Method Options -ErrorAction Stop
        [int]$response.StatusCode | Should -Be 204
        [string]$response.Headers['Access-Control-Allow-Origin'] | Should -Not -BeNullOrEmpty
        [string]$response.Headers['Access-Control-Allow-Headers'] | Should -Match 'X-Api-Key'
    }

    It 'executes a dry-run via POST /runs?wait=true and returns CLI summary payload' {
        $payload = [ordered]@{
            mode = 'DryRun'
            roots = @($script:testRoot)
            useDat = $false
            removeJunk = $false
        }

        $response = Invoke-RestMethod -Uri "$($script:baseUri)/runs?wait=true" -Method Post -Headers @{ 'X-Api-Key' = $script:apiKey } -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 6)

        $response | Should -Not -BeNullOrEmpty
        $response.run | Should -Not -BeNullOrEmpty
        $response.result | Should -Not -BeNullOrEmpty
        [string]$response.run.status | Should -Match 'completed|failed|cancelled'
        [string]$response.result.schemaVersion | Should -Be 'romcleanup-cli-result-v1'
    }
}
