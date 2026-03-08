#requires -Modules Pester

Describe 'ApiServer unit helpers' {
  BeforeAll {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    . (Join-Path $repoRoot 'dev\modules\FileOps.ps1')
    . (Join-Path $repoRoot 'dev\modules\ApiServer.ps1')
  }

  It 'parses query parameters case-insensitively' {
    $q = ConvertTo-ApiQueryDictionary -Query '?wait=true&Mode=Move'
    [string]$q['wait'] | Should -Be 'true'
    [string]$q['mode'] | Should -Be 'Move'
  }

  It 'resolves known routes with run ids' {
    $r1 = Resolve-ApiRoute -Method 'GET' -Path '/health'
    $r2 = Resolve-ApiRoute -Method 'GET' -Path '/runs/abc123/result'
    $r3 = Resolve-ApiRoute -Method 'GET' -Path '/docs'
    $r4 = Resolve-ApiRoute -Method 'GET' -Path '/runs/abc123/stream'

    [string]$r1.Name | Should -Be 'health'
    [string]$r2.Name | Should -Be 'get-run-result'
    [string]$r2.RunId | Should -Be 'abc123'
    [string]$r3.Name | Should -Be 'docs'
    [string]$r4.Name | Should -Be 'stream-run'
    [string]$r4.RunId | Should -Be 'abc123'
  }

  It 'validates run payload roots and mode' {
    $errNoRoots = Test-ApiRunPayload -Payload ([pscustomobject]@{ mode = 'DryRun'; roots = @() })
    $errMode = Test-ApiRunPayload -Payload ([pscustomobject]@{ mode = 'BadMode'; roots = @('C:\ROMs') })
    $ok = Test-ApiRunPayload -Payload ([pscustomobject]@{ mode = 'Move'; roots = @('C:\ROMs') })

    [string]$errNoRoots | Should -Match 'root'
    [string]$errMode | Should -Match 'DryRun or Move'
    $ok | Should -BeNullOrEmpty
  }

  It 'builds CLI arguments with required summary flags' {
    $payload = [pscustomobject]@{
      mode = 'DryRun'
      roots = @('C:\ROMs')
      notifyAfterRun = $true
    }

    $args = ConvertTo-ApiCliArgumentList -Payload $payload -SummaryJsonPath 'C:\temp\summary.json'

    $args | Should -Contain '-EmitJsonSummary'
    $args | Should -Contain '-SummaryJsonPath'
    $args | Should -Contain '-NotifyAfterRun'
  }

  It 'resolves client identifier from forwarded header when present' {
    $request = [pscustomobject]@{
      Headers = @{ 'X-Forwarded-For' = '203.0.113.7, 10.0.0.1' }
      RemoteEndPoint = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 12345)
    }

    $id = Get-ApiClientIdentifier -Request $request
    [string]$id | Should -Be '203.0.113.7'
  }

  It 'enforces in-memory rate limit per client within the active window' {
    $state = [ordered]@{
      RateLimitRequestsPerWindow = 3
      RateLimitWindowSeconds = 3600
      RateLimitBuckets = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    }
    $request = [pscustomobject]@{
      Headers = @{}
      RemoteEndPoint = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 23456)
    }

    (Test-ApiRateLimit -Request $request -State $state) | Should -BeTrue
    (Test-ApiRateLimit -Request $request -State $state) | Should -BeTrue
    (Test-ApiRateLimit -Request $request -State $state) | Should -BeTrue
    (Test-ApiRateLimit -Request $request -State $state) | Should -BeFalse
  }

  It 'compares API keys using fixed-time helper semantics' {
    (Test-ApiFixedTimeEquals -Left 'abc123' -Right 'abc123') | Should -BeTrue
    (Test-ApiFixedTimeEquals -Left 'abc123' -Right 'abc124') | Should -BeFalse
    (Test-ApiFixedTimeEquals -Left 'abc123' -Right 'abc1234') | Should -BeFalse
  }

  It 'resolves strict-local CORS origin profile' {
    $origin = Resolve-ApiCorsOrigin -State ([ordered]@{ CorsMode = 'strict-local'; CorsAllowOrigin = '*' })
    [string]$origin | Should -Be 'http://127.0.0.1'
  }

  It 'resolves custom CORS origin profile' {
    $origin = Resolve-ApiCorsOrigin -State ([ordered]@{ CorsMode = 'custom'; CorsAllowOrigin = 'https://localhost:5173' })
    [string]$origin | Should -Be 'https://localhost:5173'
  }
}
