<#
.SYNOPSIS
    Runs a real headless API smoke against the built Romulus API.

.DESCRIPTION
    Starts the built Romulus.Api assembly with remote/headless safeguards enabled
    and verifies the documented release-critical flows:
      - anonymous health and dashboard bootstrap
      - authenticated dashboard summary
      - dashboard shell delivery
      - AllowedRoots enforcement for /runs and /convert

    The smoke keeps APPDATA/LOCALAPPDATA isolated in a temp directory so it does
    not touch the operator's normal collection index or profile store.

    Usage:
      pwsh -NoProfile -File deploy/smoke/Invoke-HeadlessSmoke.ps1
      pwsh -NoProfile -File deploy/smoke/Invoke-HeadlessSmoke.ps1 -Configuration Debug
      pwsh -NoProfile -File deploy/smoke/Invoke-HeadlessSmoke.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [int]$StartupTimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [hashtable]$Headers,
        $Body
    )

    $requestParams = @{
        Method             = $Method
        Uri                = $Uri
        Headers            = $Headers
        SkipHttpErrorCheck = $true
    }

    if ($PSBoundParameters.ContainsKey('Body')) {
        $requestParams['Body'] = ($Body | ConvertTo-Json -Depth 10 -Compress)
        $requestParams['ContentType'] = 'application/json'
    }

    return Invoke-WebRequest @requestParams
}

function Assert-StatusCode {
    param(
        [Parameter(Mandatory = $true)] $Response,
        [Parameter(Mandatory = $true)] [int]$Expected,
        [Parameter(Mandatory = $true)] [string]$Step
    )

    if ([int]$Response.StatusCode -ne $Expected) {
        throw "$Step returned HTTP $($Response.StatusCode), expected $Expected.`n$($Response.Content)"
    }
}

function Wait-ForHealth {
    param(
        [Parameter(Mandatory = $true)] [string]$Uri,
        [Parameter(Mandatory = $true)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)] [datetime]$Deadline
    )

    do {
        if ($Process.HasExited) {
            throw "Headless API exited before health probe succeeded."
        }

        try {
            $response = Invoke-WebRequest -Uri $Uri -Method Get -SkipHttpErrorCheck
            if ([int]$response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            # endpoint not up yet
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $Deadline)

    throw "Timed out waiting for health endpoint at $Uri."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$apiProjectDir = Join-Path $repoRoot 'src' 'Romulus.Api'
$apiProject = Join-Path $apiProjectDir 'Romulus.Api.csproj'
$apiDll = Join-Path $apiProjectDir 'bin' $Configuration 'net10.0' 'Romulus.Api.dll'

if (-not $SkipBuild -or -not (Test-Path $apiDll)) {
    Write-Host "=== Building Romulus.Api ($Configuration) ===" -ForegroundColor Cyan
    & dotnet build $apiProject --configuration $Configuration -nologo -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $apiProject"
    }
}

if (-not (Test-Path $apiDll)) {
    throw "Built API assembly not found: $apiDll"
}

$port = Get-FreeTcpPort
$apiKey = 'headless-smoke-api-key'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Romulus_HeadlessSmoke_" + [Guid]::NewGuid().ToString('N'))
$stdoutPath = Join-Path $tempRoot 'api.stdout.log'
$stderrPath = Join-Path $tempRoot 'api.stderr.log'
$appData = Join-Path $tempRoot 'AppData'
$localAppData = Join-Path $tempRoot 'LocalAppData'
$allowedRomRoot = Join-Path $tempRoot 'allowed-roms'
$allowedExportRoot = Join-Path $tempRoot 'allowed-exports'
$blockedRoot = Join-Path $tempRoot 'blocked'
$blockedInput = Join-Path $blockedRoot 'blocked.iso'

$process = $null

try {
    New-Item -ItemType Directory -Force -Path $tempRoot, $appData, $localAppData, $allowedRomRoot, $allowedExportRoot, $blockedRoot | Out-Null
    Set-Content -Path $blockedInput -Value 'blocked-demo' -Encoding UTF8

    $environment = @{
        'ROM_CLEANUP_API_KEY' = $apiKey
        'AllowRemoteClients'  = 'true'
        'PublicBaseUrl'       = 'https://romulus-smoke.local'
        'BindAddress'         = '127.0.0.1'
        'Port'                = [string]$port
        'AllowedRoots__0'     = $allowedRomRoot
        'AllowedRoots__1'     = $allowedExportRoot
        'DashboardEnabled'    = 'true'
        'TrustForwardedFor'   = 'false'
        'APPDATA'             = $appData
        'LOCALAPPDATA'        = $localAppData
    }

    Write-Host "=== Starting headless API smoke host ===" -ForegroundColor Cyan
    $process = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @($apiDll) `
        -WorkingDirectory $apiProjectDir `
        -PassThru `
        -NoNewWindow `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -Environment $environment

    $baseUri = "http://127.0.0.1:$port"
    Wait-ForHealth -Uri "$baseUri/healthz" -Process $process -Deadline (Get-Date).AddSeconds($StartupTimeoutSeconds)

    Write-Host "=== Verifying anonymous endpoints ===" -ForegroundColor Cyan
    $health = Invoke-WebRequest -Uri "$baseUri/healthz" -Method Get -SkipHttpErrorCheck
    Assert-StatusCode -Response $health -Expected 200 -Step 'GET /healthz'
    $healthJson = $health.Content | ConvertFrom-Json
    if ($healthJson.status -ne 'ok') {
        throw "GET /healthz returned unexpected status payload: $($health.Content)"
    }

    $bootstrap = Invoke-WebRequest -Uri "$baseUri/dashboard/bootstrap" -Method Get -SkipHttpErrorCheck
    Assert-StatusCode -Response $bootstrap -Expected 200 -Step 'GET /dashboard/bootstrap'
    $bootstrapJson = $bootstrap.Content | ConvertFrom-Json
    if (-not $bootstrapJson.requiresApiKey) {
        throw "Dashboard bootstrap must advertise requiresApiKey=true."
    }
    if ($bootstrapJson.dashboardPath -ne '/dashboard/') {
        throw "Dashboard bootstrap returned unexpected dashboardPath: $($bootstrapJson.dashboardPath)"
    }

    $dashboard = Invoke-WebRequest -Uri "$baseUri/dashboard/" -Method Get -SkipHttpErrorCheck
    Assert-StatusCode -Response $dashboard -Expected 200 -Step 'GET /dashboard/'
    if ($dashboard.Content -notmatch 'Headless Control Surface') {
        throw "Dashboard shell did not contain the expected marker text."
    }

    Write-Host "=== Verifying authenticated dashboard summary ===" -ForegroundColor Cyan
    $authHeaders = @{ 'X-Api-Key' = $apiKey }
    $summary = Invoke-WebRequest -Uri "$baseUri/dashboard/summary" -Method Get -Headers $authHeaders -SkipHttpErrorCheck
    Assert-StatusCode -Response $summary -Expected 200 -Step 'GET /dashboard/summary'
    $summaryJson = $summary.Content | ConvertFrom-Json
    foreach ($property in 'recentRuns', 'datStatus', 'trends') {
        if (-not ($summaryJson.PSObject.Properties.Name -contains $property)) {
            throw "Dashboard summary is missing property '$property'."
        }
    }

    $summaryUnauthorized = Invoke-WebRequest -Uri "$baseUri/dashboard/summary" -Method Get -SkipHttpErrorCheck
    Assert-StatusCode -Response $summaryUnauthorized -Expected 401 -Step 'GET /dashboard/summary without auth'

    Write-Host "=== Verifying AllowedRoots enforcement ===" -ForegroundColor Cyan
    $runResponse = Invoke-JsonRequest `
        -Method Post `
        -Uri "$baseUri/runs" `
        -Headers $authHeaders `
        -Body @{
            roots = @($blockedRoot)
            mode = 'DryRun'
        }
    Assert-StatusCode -Response $runResponse -Expected 400 -Step 'POST /runs outside AllowedRoots'
    if ($runResponse.Content -notmatch 'SEC-OUTSIDE-ALLOWED-ROOTS') {
        throw "POST /runs outside AllowedRoots did not return SEC-OUTSIDE-ALLOWED-ROOTS.`n$($runResponse.Content)"
    }

    $convertResponse = Invoke-JsonRequest `
        -Method Post `
        -Uri "$baseUri/convert" `
        -Headers $authHeaders `
        -Body @{
            input = $blockedInput
        }
    Assert-StatusCode -Response $convertResponse -Expected 400 -Step 'POST /convert outside AllowedRoots'
    if ($convertResponse.Content -notmatch 'SEC-OUTSIDE-ALLOWED-ROOTS') {
        throw "POST /convert outside AllowedRoots did not return SEC-OUTSIDE-ALLOWED-ROOTS.`n$($convertResponse.Content)"
    }

    Write-Host "=== HEADLESS SMOKE PASSED ===" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "=== HEADLESS SMOKE FAILED ===" -ForegroundColor Red
    if (Test-Path $stdoutPath) {
        Write-Host "--- api.stdout.log ---" -ForegroundColor Yellow
        Get-Content $stdoutPath
    }
    if (Test-Path $stderrPath) {
        Write-Host "--- api.stderr.log ---" -ForegroundColor Yellow
        Get-Content $stderrPath
    }
    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        try {
            $process.Kill($true)
            $process.WaitForExit(5000) | Out-Null
        }
        catch {
            # best effort
        }
    }

    if (Test-Path $tempRoot) {
        try {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
        catch {
            # best effort
        }
    }
}
