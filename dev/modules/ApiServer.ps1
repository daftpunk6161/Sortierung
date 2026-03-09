function Get-RomCleanupRepoRoot {
  $existingRoot = Get-Variable -Name _RomCleanupRepoRoot -Scope Script -ErrorAction SilentlyContinue
  if ($existingRoot -and $existingRoot.Value -and (Test-Path -LiteralPath ([string]$existingRoot.Value) -PathType Container)) {
    return [string]$existingRoot.Value
  }

  $candidates = @()
  if ($script:_RomCleanupModuleRoot) {
    $candidates += Split-Path -Parent $script:_RomCleanupModuleRoot
  }
  if ($PSScriptRoot) {
    $candidates += (Split-Path -Parent $PSScriptRoot)
  }
  $candidates += (Get-Location).Path

  foreach ($candidate in $candidates) {
    if (-not $candidate) { continue }
    $probe = (Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue)
    if (-not $probe) { continue }
    $path = [string]$probe.Path
    $apiScript = Join-Path $path 'Invoke-RomCleanup.ps1'
    if (Test-Path -LiteralPath $apiScript -PathType Leaf) {
      $script:_RomCleanupRepoRoot = $path
      return $path
    }
  }

  throw 'Repository root could not be resolved (Invoke-RomCleanup.ps1 not found).'
}

function ConvertTo-ApiQueryDictionary {
  param([string]$Query)

  $result = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  if ([string]::IsNullOrWhiteSpace($Query)) { return $result }

  $q = $Query.TrimStart('?')
  if ([string]::IsNullOrWhiteSpace($q)) { return $result }

  foreach ($pair in ($q -split '&')) {
    if ([string]::IsNullOrWhiteSpace($pair)) { continue }
    $parts = $pair -split '=', 2
    $key = [System.Uri]::UnescapeDataString([string]$parts[0])
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
    $val = if ($parts.Count -gt 1) { [System.Uri]::UnescapeDataString([string]$parts[1]) } else { '' }
    $result[$key] = $val
  }

  return $result
}

function Resolve-ApiRoute {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$Path
  )

  $cleanPath = if ([string]::IsNullOrWhiteSpace($Path)) { '/' } else { $Path }
  if ($cleanPath.Contains('?')) { $cleanPath = $cleanPath.Split('?')[0] }
  if (-not $cleanPath.StartsWith('/')) { $cleanPath = "/$cleanPath" }
  if ($cleanPath.Length -gt 1 -and $cleanPath.EndsWith('/')) { $cleanPath = $cleanPath.TrimEnd('/') }

  $segments = @($cleanPath.Trim('/') -split '/')
  if ($cleanPath -eq '/') { $segments = @() }

  if ($segments.Count -gt 0 -and $segments[0] -eq 'v1') {
    $segments = @($segments | Select-Object -Skip 1)
    if ($segments.Count -eq 0) {
      $cleanPath = '/'
    } else {
      $cleanPath = '/' + ($segments -join '/')
    }
  }

  # API-Versionierung (5.5.4): /v2 Prefix ebenfalls unterstuetzt
  $apiVersion = 'v1'
  if ($segments.Count -gt 0 -and $segments[0] -match '^v(\d+)$') {
    $apiVersion = $segments[0]
    $segments = @($segments | Select-Object -Skip 1)
    if ($segments.Count -eq 0) {
      $cleanPath = '/'
    } else {
      $cleanPath = '/' + ($segments -join '/')
    }
  }

  $verb = $Method.ToUpperInvariant()
  if ($verb -eq 'GET' -and $cleanPath -eq '/health') {
    return [pscustomobject]@{ Name = 'health'; RunId = $null; ApiVersion = $apiVersion }
  }
  if ($verb -eq 'GET' -and ($cleanPath -eq '/openapi' -or $cleanPath -eq '/openapi.json')) {
    return [pscustomobject]@{ Name = 'openapi'; RunId = $null; ApiVersion = $apiVersion }
  }
  if ($verb -eq 'GET' -and $cleanPath -eq '/docs') {
    return [pscustomobject]@{ Name = 'docs'; RunId = $null; ApiVersion = $apiVersion }
  }
  if ($verb -eq 'POST' -and $cleanPath -eq '/runs') {
    return [pscustomobject]@{ Name = 'create-run'; RunId = $null; ApiVersion = $apiVersion }
  }
  if ($segments.Count -eq 2 -and $segments[0] -eq 'runs' -and $verb -eq 'GET') {
    return [pscustomobject]@{ Name = 'get-run'; RunId = [string]$segments[1]; ApiVersion = $apiVersion }
  }
  if ($segments.Count -eq 3 -and $segments[0] -eq 'runs' -and $segments[2] -eq 'result' -and $verb -eq 'GET') {
    return [pscustomobject]@{ Name = 'get-run-result'; RunId = [string]$segments[1]; ApiVersion = $apiVersion }
  }
  if ($segments.Count -eq 3 -and $segments[0] -eq 'runs' -and $segments[2] -eq 'cancel' -and $verb -eq 'POST') {
    return [pscustomobject]@{ Name = 'cancel-run'; RunId = [string]$segments[1]; ApiVersion = $apiVersion }
  }
  if ($segments.Count -eq 3 -and $segments[0] -eq 'runs' -and $segments[2] -eq 'stream' -and $verb -eq 'GET') {
    return [pscustomobject]@{ Name = 'stream-run'; RunId = [string]$segments[1]; ApiVersion = $apiVersion }
  }

  return [pscustomobject]@{ Name = 'not-found'; RunId = $null; ApiVersion = $apiVersion }
}

function Test-ApiRunPayload {
  param([psobject]$Payload)

  if (-not $Payload) { return 'Request body is required.' }
  if (-not ($Payload.PSObject.Properties.Name -contains 'roots')) { return 'Property "roots" is required.' }

  $roots = @($Payload.roots | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  if ($roots.Count -eq 0) { return 'At least one root path is required.' }

  if ($Payload.PSObject.Properties.Name -contains 'mode') {
    $mode = [string]$Payload.mode
    if ($mode -and $mode -notin @('DryRun','Move')) {
      return 'Property "mode" must be DryRun or Move.'
    }
  }

  # BUG-032 FIX: Validate each root path — must be existing directory, not a system path
  $blockedPrefixes = @(
    [System.Environment]::GetFolderPath('Windows'),
    [System.Environment]::GetFolderPath('ProgramFiles'),
    [System.Environment]::GetFolderPath('ProgramFilesX86'),
    [System.Environment]::GetFolderPath('System')
  ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

  foreach ($root in $roots) {
    $rootStr = [string]$root
    try {
      $normalized = [System.IO.Path]::GetFullPath($rootStr)
    } catch {
      return ('Root path is invalid: {0}' -f $rootStr)
    }
    # BUG API-007 FIX: Block UNC paths and drive roots
    if ($normalized.StartsWith('\\')) {
      return ('UNC/network paths are not allowed: {0}' -f $rootStr)
    }
    if ($normalized.Length -le 3 -and $normalized -match '^[A-Za-z]:\\?$') {
      return ('Drive root paths are not allowed: {0}' -f $rootStr)
    }
    if (-not (Test-Path -LiteralPath $normalized -PathType Container)) {
      return ('Root path does not exist or is not a directory: {0}' -f $rootStr)
    }
    foreach ($blocked in $blockedPrefixes) {
      if ($normalized.StartsWith($blocked, [StringComparison]::OrdinalIgnoreCase)) {
        return ('Root path is in a protected system directory: {0}' -f $rootStr)
      }
    }
  }

  return $null
}

function Write-ApiJsonResponse {
  param(
    [Parameter(Mandatory=$true)]$Context,
    [Parameter(Mandatory=$true)][int]$StatusCode,
    [Parameter(Mandatory=$true)]$Body
  )

  $response = $Context.Response
  $response.StatusCode = $StatusCode
  $response.ContentType = 'application/json; charset=utf-8'
  $response.Headers['Cache-Control'] = 'no-store'
  Set-ApiCorsHeaders -Response $response

  $json = $Body | ConvertTo-Json -Depth 8
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
  $response.ContentLength64 = $bytes.Length
  $response.OutputStream.Write($bytes, 0, $bytes.Length)
  $response.OutputStream.Flush()
  $response.OutputStream.Close()
}

function Write-ApiHtmlResponse {
  param(
    [Parameter(Mandatory=$true)]$Context,
    [Parameter(Mandatory=$true)][int]$StatusCode,
    [Parameter(Mandatory=$true)][string]$Html
  )

  $response = $Context.Response
  $response.StatusCode = $StatusCode
  $response.ContentType = 'text/html; charset=utf-8'
  $response.Headers['Cache-Control'] = 'no-store'
  Set-ApiCorsHeaders -Response $response

  $bytes = [System.Text.Encoding]::UTF8.GetBytes($Html)
  $response.ContentLength64 = $bytes.Length
  $response.OutputStream.Write($bytes, 0, $bytes.Length)
  $response.OutputStream.Flush()
  $response.OutputStream.Close()
}

function Write-ApiSseFrame {
  param(
    [Parameter(Mandatory=$true)]$Response,
    [Parameter(Mandatory=$true)][string]$Event,
    [Parameter(Mandatory=$true)]$Data
  )

  $payload = if ($Data -is [string]) { $Data } else { ($Data | ConvertTo-Json -Depth 8 -Compress) }
  $frame = "event: $Event`ndata: $payload`n`n"
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($frame)
  $Response.OutputStream.Write($bytes, 0, $bytes.Length)
  $Response.OutputStream.Flush()
}

function Write-ApiRunStreamResponse {
  param(
    [Parameter(Mandatory=$true)]$Context,
    [Parameter(Mandatory=$true)][string]$RunId
  )

  $run = Update-ApiRunState -RunId $RunId
  if (-not $run) {
    Write-ApiJsonResponse -Context $Context -StatusCode 404 -Body ([ordered]@{ error = 'Run not found.' })
    return
  }

  $response = $Context.Response
  $response.StatusCode = 200
  $response.ContentType = 'text/event-stream; charset=utf-8'
  $response.Headers['Cache-Control'] = 'no-store'
  $response.Headers['Connection'] = 'keep-alive'
  Set-ApiCorsHeaders -Response $response

  $pollMs = [Math]::Max(100, [int]((Get-ApiServerState).PollIntervalMs))
  $lastSignature = ''
  $startUtc = [DateTime]::UtcNow
  $maxStreamSeconds = 300

  try {
    Write-ApiSseFrame -Response $response -Event 'ready' -Data ([ordered]@{ runId = $RunId; utc = (Get-Date).ToUniversalTime().ToString('o') })

    while ($true) {
      $current = Update-ApiRunState -RunId $RunId
      if (-not $current) {
        Write-ApiSseFrame -Response $response -Event 'error' -Data ([ordered]@{ error = 'Run not found.'; runId = $RunId })
        break
      }

      $snapshot = [ordered]@{
        run = Get-ApiRunResponse -Run $current
      }
      if ($current.result) { $snapshot.result = $current.result }

      $signature = ($snapshot | ConvertTo-Json -Depth 8 -Compress)
      if ($signature -ne $lastSignature) {
        Write-ApiSseFrame -Response $response -Event 'status' -Data $signature
        $lastSignature = $signature
      }

      if ($current.status -ne 'running') {
        Write-ApiSseFrame -Response $response -Event 'completed' -Data $signature
        break
      }

      if (([DateTime]::UtcNow - $startUtc).TotalSeconds -ge $maxStreamSeconds) {
        Write-ApiSseFrame -Response $response -Event 'timeout' -Data ([ordered]@{ runId = $RunId; seconds = $maxStreamSeconds })
        break
      }

      Start-Sleep -Milliseconds $pollMs
    }
  } finally {
    try { $response.OutputStream.Flush() } catch { }
    try { $response.OutputStream.Close() } catch { }
  }
}

function Get-ApiDocsHtml {
  return @"
<!doctype html>
<html lang='de'>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1' />
  <title>ROM Cleanup API Docs</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; background:#0f1220; color:#e8e8f8; }
    h1 { margin-top: 0; }
    .muted { color:#9aa3c7; }
    code { background:#1b2140; padding:2px 6px; border-radius:4px; }
    .card { background:#161b33; border:1px solid #2d3562; border-radius:8px; padding:12px; margin:12px 0; }
    input, button { font: inherit; }
    input { width: 320px; padding:6px; border-radius:6px; border:1px solid #394179; background:#0f1220; color:#e8e8f8; }
    button { padding:6px 10px; border-radius:6px; border:1px solid #00f5ff; background:#00f5ff; color:#0f1220; cursor:pointer; }
    pre { white-space: pre-wrap; background:#0f1220; border:1px solid #2d3562; border-radius:8px; padding:12px; }
  </style>
</head>
<body>
  <h1>ROM Cleanup API</h1>
  <p class='muted'>OpenAPI: <code>/openapi</code> · Stream: <code>/runs/{runId}/stream</code></p>
  <div class='card'>
    <label for='key'>API-Key (Header <code>X-Api-Key</code>): </label>
    <input id='key' placeholder='change-me' />
    <button id='btnLoad'>OpenAPI laden</button>
  </div>
  <pre id='out'>Bereit.</pre>
  <script>
    const out = document.getElementById('out');
    document.getElementById('btnLoad').addEventListener('click', async () => {
      const key = document.getElementById('key').value || '';
      out.textContent = 'Lade /openapi ...';
      try {
        const res = await fetch('/openapi', { headers: { 'X-Api-Key': key } });
        const txt = await res.text();
        out.textContent = txt;
      } catch (e) {
        out.textContent = 'Fehler: ' + e;
      }
    });
  </script>
</body>
</html>
"@
}

function Get-RomCleanupOpenApiSpec {
  $baseUrl = 'http://127.0.0.1:{port}'
  return [ordered]@{
    openapi = '3.0.3'
    info = [ordered]@{
      title = 'ROM Cleanup API'
      version = if (Get-Command Get-RomCleanupVersion -ErrorAction SilentlyContinue) { Get-RomCleanupVersion } else { '1.0.0' }
      description = 'Lokale API zur Steuerung von ROM Cleanup Runs.'
    }
    servers = @(
      [ordered]@{ url = $baseUrl }
    )
    paths = [ordered]@{
      '/health' = [ordered]@{
        get = [ordered]@{ summary = 'Health check'; responses = [ordered]@{ '200' = [ordered]@{ description = 'OK' } } }
      }
      '/runs' = [ordered]@{
        post = [ordered]@{ summary = 'Run erstellen'; responses = [ordered]@{ '202' = [ordered]@{ description = 'Accepted' } } }
      }
      '/runs/{runId}' = [ordered]@{
        get = [ordered]@{ summary = 'Run-Status lesen'; responses = [ordered]@{ '200' = [ordered]@{ description = 'OK' }; '404' = [ordered]@{ description = 'Not found' } } }
      }
      '/runs/{runId}/result' = [ordered]@{
        get = [ordered]@{ summary = 'Run-Ergebnis lesen'; responses = [ordered]@{ '200' = [ordered]@{ description = 'OK' }; '409' = [ordered]@{ description = 'Running' } } }
      }
      '/runs/{runId}/cancel' = [ordered]@{
        post = [ordered]@{ summary = 'Run abbrechen'; responses = [ordered]@{ '200' = [ordered]@{ description = 'Canceled' }; '409' = [ordered]@{ description = 'Not running' } } }
      }
      '/openapi' = [ordered]@{
        get = [ordered]@{ summary = 'OpenAPI-Spezifikation'; responses = [ordered]@{ '200' = [ordered]@{ description = 'OK' } } }
      }
      '/docs' = [ordered]@{
        get = [ordered]@{ summary = 'Swagger UI'; responses = [ordered]@{ '200' = [ordered]@{ description = 'OK' } } }
      }
      '/runs/{runId}/stream' = [ordered]@{
        get = [ordered]@{ summary = 'Run-Status streamen (SSE)'; responses = [ordered]@{ '200' = [ordered]@{ description = 'OK' }; '404' = [ordered]@{ description = 'Not found' } } }
      }
    }
  }
}

function Export-RomCleanupOpenApiSpec {
  param(
    [string]$Path,
    [switch]$Force
  )

  if ([string]::IsNullOrWhiteSpace($Path)) {
    $repoRoot = Get-RomCleanupRepoRoot
    $Path = Join-Path $repoRoot 'docs\openapi.generated.json'
  }

  $parent = Split-Path -Parent $Path
  if (-not [string]::IsNullOrWhiteSpace($parent)) {
    Assert-DirectoryExists -Path $parent
  }

  if ((Test-Path -LiteralPath $Path -PathType Leaf) -and -not $Force) {
    return $Path
  }

  $spec = Get-RomCleanupOpenApiSpec
  Write-JsonFile -Path $Path -Data $spec -Depth 15
  return $Path
}

function Set-ApiCorsHeaders {
  param([Parameter(Mandatory=$true)]$Response)

  $state = Get-ApiServerState
  $origin = Resolve-ApiCorsOrigin -State $state

  if ($null -eq $origin) { return } # CorsMode=none → no CORS headers

  $Response.Headers['Access-Control-Allow-Origin'] = $origin
  $Response.Headers['Access-Control-Allow-Headers'] = 'Content-Type, X-Api-Key'
  $Response.Headers['Access-Control-Allow-Methods'] = 'GET, POST, OPTIONS'
  $Response.Headers['Access-Control-Max-Age'] = '600'
  # BUG-005 FIX: Remove headers that leak server information
  $Response.Headers.Remove('Server')
  $Response.Headers.Remove('X-Powered-By')
  if ($origin -ne '*') {
    $Response.Headers['Vary'] = 'Origin'
  }
}

function Resolve-ApiCorsOrigin {
  param([hashtable]$State)

  $mode = 'custom'
  if ($State -and -not [string]::IsNullOrWhiteSpace([string]$State.CorsMode)) {
    $mode = [string]$State.CorsMode
  }

  switch ($mode.ToLowerInvariant()) {
    'local-dev' { return '*' }
    'strict-local' { return 'http://127.0.0.1' }
    'none' { return $null }
    default {
      if ($State -and -not [string]::IsNullOrWhiteSpace([string]$State.CorsAllowOrigin)) {
        return [string]$State.CorsAllowOrigin
      }
      return '*'
    }
  }
}

function Get-ApiClientIdentifier {
  param([Parameter(Mandatory=$true)]$Request)

  # BUG API-001 FIX: Always use RemoteEndPoint — X-Forwarded-For is spoofable
  if ($Request.RemoteEndPoint -and $Request.RemoteEndPoint.Address) {
    return [string]$Request.RemoteEndPoint.Address.ToString()
  }

  return 'unknown'
}

function Test-ApiRateLimit {
  param(
    [Parameter(Mandatory=$true)]$Request,
    [Parameter(Mandatory=$true)][hashtable]$State
  )

  $maxRequests = [int]$State.RateLimitRequestsPerWindow
  $windowSeconds = [int]$State.RateLimitWindowSeconds
  if ($maxRequests -le 0 -or $windowSeconds -le 0) { return $true }

  if (-not $State.Contains('RateLimitBuckets') -or $null -eq $State.RateLimitBuckets) {
    $State.RateLimitBuckets = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }

  $now = [datetime]::UtcNow
  $clientId = Get-ApiClientIdentifier -Request $Request
  $entry = $null
  if ($State.RateLimitBuckets.ContainsKey($clientId)) {
    $entry = $State.RateLimitBuckets[$clientId]
  }

  if (-not $entry) {
    $State.RateLimitBuckets[$clientId] = [ordered]@{ WindowStartUtc = $now; Count = 1 }
    return $true
  }

  $windowStart = [datetime]$entry.WindowStartUtc
  $elapsed = ($now - $windowStart).TotalSeconds
  if ($elapsed -ge $windowSeconds) {
    $entry.WindowStartUtc = $now
    $entry.Count = 1
    return $true
  }

  $currentCount = [int]$entry.Count
  if ($currentCount -ge $maxRequests) {
    return $false
  }

  $entry.Count = $currentCount + 1

  if ($State.RateLimitBuckets.Count -gt 512) {
    foreach ($key in @($State.RateLimitBuckets.Keys)) {
      $bucket = $State.RateLimitBuckets[$key]
      if (-not $bucket) {
        [void]$State.RateLimitBuckets.Remove([string]$key)
        continue
      }
      $bucketStart = [datetime]$bucket.WindowStartUtc
      if ((($now - $bucketStart).TotalSeconds) -ge ($windowSeconds * 2)) {
        [void]$State.RateLimitBuckets.Remove([string]$key)
      }
    }
  }

  return $true
}

function Read-ApiJsonBody {
  param([Parameter(Mandatory=$true)]$Request)

  if (-not $Request.HasEntityBody) { return $null }

  # BUG API-002 FIX: Enforce body size limit (1 MB) to prevent DoS
  $maxBodyBytes = 1048576
  if ($Request.ContentLength64 -gt $maxBodyBytes) { return $null }

  $reader = New-Object System.IO.StreamReader($Request.InputStream, $Request.ContentEncoding)
  try {
    $raw = $reader.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    if ($raw.Length -gt $maxBodyBytes) { return $null }
    return ($raw | ConvertFrom-Json -ErrorAction Stop)
  } finally {
    $reader.Dispose()
  }
}

function Test-ApiRequestAuthorization {
  param(
    [Parameter(Mandatory=$true)]$Request,
    [Parameter(Mandatory=$true)][string]$ApiKey
  )

  $candidate = [string]$Request.Headers['X-Api-Key']
  if ([string]::IsNullOrWhiteSpace($candidate)) { return $false }
  return (Test-ApiFixedTimeEquals -Left $candidate -Right $ApiKey)
}

function Test-ApiFixedTimeEquals {
  param(
    [Parameter(Mandatory=$true)][string]$Left,
    [Parameter(Mandatory=$true)][string]$Right
  )

  $leftBytes = [System.Text.Encoding]::UTF8.GetBytes($Left)
  $rightBytes = [System.Text.Encoding]::UTF8.GetBytes($Right)

  $maxLength = [Math]::Max($leftBytes.Length, $rightBytes.Length)
  $diff = 0
  for ($index = 0; $index -lt $maxLength; $index++) {
    $leftByte = if ($index -lt $leftBytes.Length) { [int]$leftBytes[$index] } else { 0 }
    $rightByte = if ($index -lt $rightBytes.Length) { [int]$rightBytes[$index] } else { 0 }
    $diff = $diff -bor ($leftByte -bxor $rightByte)
  }

  if ($leftBytes.Length -ne $rightBytes.Length) {
    $diff = $diff -bor 1
  }

  return ($diff -eq 0)
}

function Get-ApiServerState {
  $existing = Get-Variable -Name API_SERVER_STATE -Scope Script -ErrorAction SilentlyContinue
  if (-not $existing -or $null -eq $existing.Value) {
    $script:API_SERVER_STATE = [ordered]@{
      IsRunning = $false
      Listener = $null
      ApiKey = $null
      Runs = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      ActiveRunId = $null
      PollIntervalMs = 250
      CorsAllowOrigin = 'http://127.0.0.1'
      RateLimitRequestsPerWindow = 120
      RateLimitWindowSeconds = 60
      RateLimitBuckets = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
      CorsMode = 'strict-local'
    }
  }
  return $script:API_SERVER_STATE
}

function ConvertTo-ApiBoolean {
  param([object]$Value, [bool]$Default = $false)

  if ($null -eq $Value) { return $Default }
  if ($Value -is [bool]) { return [bool]$Value }
  $s = [string]$Value
  if ([string]::IsNullOrWhiteSpace($s)) { return $Default }
  switch -Regex ($s.Trim().ToLowerInvariant()) {
    '^(1|true|yes|y|on)$' { return $true }
    '^(0|false|no|n|off)$' { return $false }
    default { return $Default }
  }
}

function Get-ApiPayloadValue {
  param(
    [Parameter(Mandatory=$true)][psobject]$Payload,
    [Parameter(Mandatory=$true)][string]$Name,
    [object]$Default = $null
  )

  if ($null -eq $Payload) { return $Default }
  if ($Payload.PSObject.Properties.Name -contains $Name) {
    return $Payload.$Name
  }
  return $Default
}

function ConvertTo-ApiCliArgumentList {
  param(
    [Parameter(Mandatory=$true)][psobject]$Payload,
    [Parameter(Mandatory=$true)][string]$SummaryJsonPath
  )

  $cliArgs = New-Object System.Collections.Generic.List[string]
  [void]$cliArgs.Add('-NoProfile')
  [void]$cliArgs.Add('-File')
  [void]$cliArgs.Add((Join-Path (Get-RomCleanupRepoRoot) 'Invoke-RomCleanup.ps1'))

  [void]$cliArgs.Add('-Mode')
  $mode = if ($Payload.PSObject.Properties.Name -contains 'mode' -and -not [string]::IsNullOrWhiteSpace([string]$Payload.mode)) { [string]$Payload.mode } else { 'DryRun' }
  [void]$cliArgs.Add($mode)

  foreach ($root in @($Payload.roots | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })) {
    [void]$cliArgs.Add('-Roots')
    [void]$cliArgs.Add([string]$root)
  }

  if ($Payload.PSObject.Properties.Name -contains 'prefer') {
    foreach ($p in @($Payload.prefer)) {
      if ([string]::IsNullOrWhiteSpace([string]$p)) { continue }
      [void]$cliArgs.Add('-Prefer')
      [void]$cliArgs.Add([string]$p)
    }
  }

  if ($Payload.PSObject.Properties.Name -contains 'extensions') {
    foreach ($ext in @($Payload.extensions)) {
      if ([string]::IsNullOrWhiteSpace([string]$ext)) { continue }
      [void]$cliArgs.Add('-Extensions')
      [void]$cliArgs.Add([string]$ext)
    }
  }

  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'sortConsole' -Default $false)) { [void]$cliArgs.Add('-SortConsole') }
  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'useDat' -Default $false)) { [void]$cliArgs.Add('-UseDat') }
  # DatFallback and RemoveJunk are [bool] params with default $true.
  # pwsh -File cannot pass bool values - omit to use default ($true),
  # or skip entirely since -File mode can't set them to $false.
  # These params should ideally be [switch] in the CLI; for now, always use defaults.
  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'convert' -Default $false)) { [void]$cliArgs.Add('-Convert') }
  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'aggressiveJunk' -Default $false)) { [void]$cliArgs.Add('-AggressiveJunk') }
  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'aliasEditionKeying' -Default $false)) { [void]$cliArgs.Add('-AliasEditionKeying') }
  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'separateBios' -Default $false)) { [void]$cliArgs.Add('-SeparateBios') }
  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'use1g1r' -Default $false)) { [void]$cliArgs.Add('-Use1G1R') }

  if ($Payload.PSObject.Properties.Name -contains 'datRoot' -and -not [string]::IsNullOrWhiteSpace([string]$Payload.datRoot)) {
    [void]$cliArgs.Add('-DatRoot')
    [void]$cliArgs.Add([string]$Payload.datRoot)
  }
  if ($Payload.PSObject.Properties.Name -contains 'datHashType' -and -not [string]::IsNullOrWhiteSpace([string]$Payload.datHashType)) {
    [void]$cliArgs.Add('-DatHashType')
    [void]$cliArgs.Add([string]$Payload.datHashType)
  }
  if ($Payload.PSObject.Properties.Name -contains 'auditRoot' -and -not [string]::IsNullOrWhiteSpace([string]$Payload.auditRoot)) {
    [void]$cliArgs.Add('-AuditRoot')
    [void]$cliArgs.Add([string]$Payload.auditRoot)
  }
  if ($Payload.PSObject.Properties.Name -contains 'trashRoot' -and -not [string]::IsNullOrWhiteSpace([string]$Payload.trashRoot)) {
    [void]$cliArgs.Add('-TrashRoot')
    [void]$cliArgs.Add([string]$Payload.trashRoot)
  }

  [void]$cliArgs.Add('-SkipConfirm')
  if (ConvertTo-ApiBoolean -Value (Get-ApiPayloadValue -Payload $Payload -Name 'notifyAfterRun' -Default $false)) { [void]$cliArgs.Add('-NotifyAfterRun') }
  [void]$cliArgs.Add('-EmitJsonSummary')
  [void]$cliArgs.Add('-SummaryJsonPath')
  [void]$cliArgs.Add($SummaryJsonPath)

  return @($cliArgs)
}

function New-ApiRunRecord {
  param(
    [Parameter(Mandatory=$true)][string]$RunId,
    [Parameter(Mandatory=$true)][psobject]$Payload,
    [Parameter(Mandatory=$true)][string]$SummaryJsonPath,
    [Parameter(Mandatory=$true)][int]$ProcessId
  )

  return [ordered]@{
    runId = $RunId
    status = 'running'
    startedUtc = (Get-Date).ToUniversalTime().ToString('o')
    completedUtc = $null
    processId = $ProcessId
    summaryJsonPath = $SummaryJsonPath
    request = $Payload
    result = $null
    error = $null
  }
}

function Update-ApiRunState {
  param([Parameter(Mandatory=$true)][string]$RunId)

  $state = Get-ApiServerState
  if (-not $state.Runs.ContainsKey($RunId)) { return $null }

  $run = $state.Runs[$RunId]
  if ($run.status -in @('completed','failed','cancelled')) { return $run }

  $proc = Get-Process -Id ([int]$run.processId) -ErrorAction SilentlyContinue
  if ($proc) { return $run }

  $summaryPath = [string]$run.summaryJsonPath
  if (Test-Path -LiteralPath $summaryPath -PathType Leaf) {
    try {
      $summary = Get-Content -LiteralPath $summaryPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
      $run.result = $summary
      $status = [string]$summary.status
      if ($status -eq 'completed') {
        $run.status = 'completed'
      } elseif ($status -eq 'cancelled') {
        $run.status = 'cancelled'
      } else {
        $run.status = 'failed'
      }
    } catch {
      $run.status = 'failed'
      $run.error = $_.Exception.Message
    }
  } else {
    $run.status = 'failed'
    $run.error = 'Run process finished without summary file.'
  }

  $run.completedUtc = (Get-Date).ToUniversalTime().ToString('o')
  if ($state.ActiveRunId -and $state.ActiveRunId -eq $RunId) {
    $state.ActiveRunId = $null
  }

  return $run
}

function Start-ApiRun {
  param([Parameter(Mandatory=$true)][psobject]$Payload)

  $state = Get-ApiServerState
  # BUG-040 FIX: Synchronize check-and-set of ActiveRunId to prevent TOCTOU race
  if (-not $state.Contains('_SyncRoot')) { $state['_SyncRoot'] = [object]::new() }
  [System.Threading.Monitor]::Enter($state['_SyncRoot'])
  try {
    if ($state.ActiveRunId) {
      $active = Update-ApiRunState -RunId $state.ActiveRunId
      if ($active -and $active.status -eq 'running') {
        throw 'Another run is already active.'
      }
      $state.ActiveRunId = $null
    }

  $runId = [guid]::NewGuid().ToString('N')
  $summaryPath = Join-Path ([System.IO.Path]::GetTempPath()) ("romcleanup-api-run-{0}.json" -f $runId)

  $cliArgs = ConvertTo-ApiCliArgumentList -Payload $Payload -SummaryJsonPath $summaryPath

  $engine = Get-Command pwsh -ErrorAction SilentlyContinue
  if (-not $engine) { $engine = Get-Command powershell.exe -ErrorAction SilentlyContinue }
  if (-not $engine) { throw 'No PowerShell host executable found (pwsh/powershell.exe).' }

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $engine.Source
  $psi.WorkingDirectory = Get-RomCleanupRepoRoot
  $psi.RedirectStandardOutput = $false
  $psi.RedirectStandardError = $false
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.Arguments = (($cliArgs | ForEach-Object { ConvertTo-QuotedArg ([string]$_) }) -join ' ')

  $proc = New-Object System.Diagnostics.Process
  $proc.StartInfo = $psi
  if (-not $proc.Start()) {
    throw 'Run process failed to start.'
  }

  $run = New-ApiRunRecord -RunId $runId -Payload $Payload -SummaryJsonPath $summaryPath -ProcessId $proc.Id
  $state.Runs[$runId] = $run
  $state.ActiveRunId = $runId
  } finally {
    [System.Threading.Monitor]::Exit($state['_SyncRoot'])
  }

  return [pscustomobject]$run
}

function Stop-ApiRun {
  param([Parameter(Mandatory=$true)][string]$RunId)

  $state = Get-ApiServerState
  if (-not $state.Runs.ContainsKey($RunId)) { return $false }

  $run = Update-ApiRunState -RunId $RunId
  if (-not $run) { return $false }
  if ($run.status -ne 'running') { return $false }

  try {
    $proc = Get-Process -Id ([int]$run.processId) -ErrorAction Stop
    $proc.Kill()
    $run.status = 'cancelled'
    $run.completedUtc = (Get-Date).ToUniversalTime().ToString('o')
    $run.error = 'Run cancelled by API request.'
    if ($state.ActiveRunId -eq $RunId) { $state.ActiveRunId = $null }
    return $true
  } catch {
    return $false
  }
}

function Get-ApiRunResponse {
  param([Parameter(Mandatory=$true)][hashtable]$Run)

  return [ordered]@{
    runId = $Run.runId
    status = $Run.status
    startedUtc = $Run.startedUtc
    completedUtc = $Run.completedUtc
    processId = $Run.processId
    error = $Run.error
  }
}

function Invoke-RomCleanupApiRequest {
  param(
    [Parameter(Mandatory=$true)]$Context,
    [Parameter(Mandatory=$true)][string]$ApiKey
  )

  $request = $Context.Request

  if ([string]::Equals([string]$request.HttpMethod, 'OPTIONS', [System.StringComparison]::OrdinalIgnoreCase)) {
    # BUG-005 FIX: Return minimal CORS preflight response (204 No Content, no body)
    $response = $Context.Response
    $response.StatusCode = 204
    Set-ApiCorsHeaders -Response $response
    $response.ContentLength64 = 0
    $response.OutputStream.Close()
    return
  }

  $state = Get-ApiServerState
  if (-not (Test-ApiRateLimit -Request $request -State $state)) {
    Write-ApiJsonResponse -Context $Context -StatusCode 429 -Body ([ordered]@{ error = 'Too many requests.' })
    return
  }

  if (-not (Test-ApiRequestAuthorization -Request $request -ApiKey $ApiKey)) {
    if (Get-Command Write-SecurityAuditEvent -ErrorAction SilentlyContinue) {
      Write-SecurityAuditEvent -Domain 'Auth' -Action 'ApiKeyReject' -Actor ([string]$request.RemoteEndPoint) -Target ([string]$request.Url.AbsolutePath) -Outcome 'Deny' -Detail 'Unauthorized API request.' -Source 'Invoke-RomCleanupApiRequest' -Severity 'Medium'
    }
    Write-ApiJsonResponse -Context $Context -StatusCode 401 -Body ([ordered]@{ error = 'Unauthorized' })
    return
  }

  if (Get-Command Write-SecurityAuditEvent -ErrorAction SilentlyContinue) {
    Write-SecurityAuditEvent -Domain 'Auth' -Action 'ApiKeyAccept' -Actor ([string]$request.RemoteEndPoint) -Target ([string]$request.Url.AbsolutePath) -Outcome 'Allow' -Detail 'Authorized API request.' -Source 'Invoke-RomCleanupApiRequest' -Severity 'Low'
  }

  $route = Resolve-ApiRoute -Method $request.HttpMethod -Path $request.Url.AbsolutePath

  # API-Versionierung: X-Api-Version Response-Header (5.5.4)
  $apiVer = if ($route.PSObject.Properties.Name -contains 'ApiVersion') { [string]$route.ApiVersion } else { 'v1' }
  try { $Context.Response.Headers['X-Api-Version'] = $apiVer } catch { }

  switch ($route.Name) {
    'health' {
      $state = Get-ApiServerState
      $payload = [ordered]@{
        status = 'ok'
        serverRunning = [bool]$state.IsRunning
        activeRunId = $state.ActiveRunId
        utc = (Get-Date).ToUniversalTime().ToString('o')
      }
      Write-ApiJsonResponse -Context $Context -StatusCode 200 -Body $payload
      return
    }

    'openapi' {
      $spec = Get-RomCleanupOpenApiSpec
      Write-ApiJsonResponse -Context $Context -StatusCode 200 -Body $spec
      return
    }

    'docs' {
      Write-ApiHtmlResponse -Context $Context -StatusCode 200 -Html (Get-ApiDocsHtml)
      return
    }

    'create-run' {
      $payload = Read-ApiJsonBody -Request $request
      $validation = Test-ApiRunPayload -Payload $payload
      if ($validation) {
        Write-ApiJsonResponse -Context $Context -StatusCode 400 -Body ([ordered]@{ error = $validation })
        return
      }

      try {
        $run = Start-ApiRun -Payload $payload
      } catch {
        if ($_.Exception.Message -like '*already active*') {
          Write-ApiJsonResponse -Context $Context -StatusCode 409 -Body ([ordered]@{ error = $_.Exception.Message })
        } else {
          Write-ApiJsonResponse -Context $Context -StatusCode 500 -Body ([ordered]@{ error = $_.Exception.Message })
        }
        return
      }

      $query = ConvertTo-ApiQueryDictionary -Query $request.Url.Query
      $wait = ConvertTo-ApiBoolean -Value $query['wait']
      if ($wait) {
        $pollMs = [int]((Get-ApiServerState).PollIntervalMs)
        do {
          Start-Sleep -Milliseconds $pollMs
          $run = Update-ApiRunState -RunId $run.runId
        } while ($run -and $run.status -eq 'running')

        $resultBody = [ordered]@{
          run = Get-ApiRunResponse -Run $run
          result = $run.result
        }
        Write-ApiJsonResponse -Context $Context -StatusCode 200 -Body $resultBody
        return
      }

      Write-ApiJsonResponse -Context $Context -StatusCode 202 -Body ([ordered]@{ run = (Get-ApiRunResponse -Run $run) })
      return
    }

    'get-run' {
      $run = Update-ApiRunState -RunId $route.RunId
      if (-not $run) {
        Write-ApiJsonResponse -Context $Context -StatusCode 404 -Body ([ordered]@{ error = 'Run not found.' })
        return
      }

      Write-ApiJsonResponse -Context $Context -StatusCode 200 -Body ([ordered]@{ run = (Get-ApiRunResponse -Run $run) })
      return
    }

    'get-run-result' {
      $run = Update-ApiRunState -RunId $route.RunId
      if (-not $run) {
        Write-ApiJsonResponse -Context $Context -StatusCode 404 -Body ([ordered]@{ error = 'Run not found.' })
        return
      }
      if ($run.status -eq 'running') {
        Write-ApiJsonResponse -Context $Context -StatusCode 409 -Body ([ordered]@{ error = 'Run is still running.' })
        return
      }
      if (-not $run.result) {
        Write-ApiJsonResponse -Context $Context -StatusCode 500 -Body ([ordered]@{ error = 'No result payload available.' })
        return
      }

      Write-ApiJsonResponse -Context $Context -StatusCode 200 -Body ([ordered]@{ run = (Get-ApiRunResponse -Run $run); result = $run.result })
      return
    }

    'cancel-run' {
      $stopped = Stop-ApiRun -RunId $route.RunId
      if (-not $stopped) {
        $run = Update-ApiRunState -RunId $route.RunId
        if (-not $run) {
          Write-ApiJsonResponse -Context $Context -StatusCode 404 -Body ([ordered]@{ error = 'Run not found.' })
          return
        }
        Write-ApiJsonResponse -Context $Context -StatusCode 409 -Body ([ordered]@{ error = 'Run is not running.' })
        return
      }

      $run = Update-ApiRunState -RunId $route.RunId
      Write-ApiJsonResponse -Context $Context -StatusCode 200 -Body ([ordered]@{ run = (Get-ApiRunResponse -Run $run) })
      return
    }

    'stream-run' {
      Write-ApiRunStreamResponse -Context $Context -RunId $route.RunId
      return
    }

    default {
      Write-ApiJsonResponse -Context $Context -StatusCode 404 -Body ([ordered]@{ error = 'Route not found.' })
      return
    }
  }
}

function Start-RomCleanupApiServer {
  param(
    [int]$Port = 7878,
    [string]$ApiKey = $null,
    [int]$PollIntervalMs = 250,
    [int]$RateLimitRequestsPerWindow = 120,
    [int]$RateLimitWindowSeconds = 60,
    [ValidateSet('custom','local-dev','strict-local','none')]
    [string]$CorsMode = 'strict-local',
    [string]$CorsAllowOrigin = 'http://127.0.0.1',
    [switch]$Https,
    [string]$CertificateThumbprint = ''
  )

  if ($Port -lt 1 -or $Port -gt 65535) {
    throw 'Port must be within 1..65535.'
  }

  $resolvedApiKey = if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $ApiKey } else { [System.Environment]::GetEnvironmentVariable('ROM_CLEANUP_API_KEY') }
  if ([string]::IsNullOrWhiteSpace($resolvedApiKey)) {
    throw 'API key missing. Set -ApiKey or environment variable ROM_CLEANUP_API_KEY.'
  }

  $state = Get-ApiServerState
  if ($state.IsRunning -and $state.Listener) {
    throw 'API server is already running.'
  }

  $scheme = if ($Https) { 'https' } else { 'http' }

  # For HTTPS: bind certificate to port via netsh if thumbprint provided
  if ($Https -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    try {
      $appId = '{2F8B7B4A-1C3D-4E5F-9A8B-0D1E2F3A4B5C}'
      $bindResult = & netsh http add sslcert ipport="0.0.0.0:$Port" certhash="$CertificateThumbprint" appid="$appId" 2>&1
      Write-Verbose ("SSL cert binding: {0}" -f ($bindResult -join ' '))
    } catch {
      Write-Warning ("SSL cert binding failed: {0}. Ensure cert exists in LocalMachine\My store." -f $_.Exception.Message)
    }
  }

  $listener = New-Object System.Net.HttpListener
  [void]$listener.Prefixes.Add(("{0}://127.0.0.1:{1}/" -f $scheme, $Port))
  $listener.Start()

  $state.Listener = $listener
  $state.IsRunning = $true
  $state.ApiKey = $resolvedApiKey
  $state.PollIntervalMs = [Math]::Max(50, $PollIntervalMs)
  $state.RateLimitRequestsPerWindow = [Math]::Max(1, $RateLimitRequestsPerWindow)
  $state.RateLimitWindowSeconds = [Math]::Max(1, $RateLimitWindowSeconds)
  $state.CorsMode = [string]$CorsMode
  if ([string]::IsNullOrWhiteSpace($CorsAllowOrigin)) {
    $state.CorsAllowOrigin = '*'
  } else {
    $state.CorsAllowOrigin = [string]$CorsAllowOrigin
  }
  $state.RateLimitBuckets = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  try { Export-RomCleanupOpenApiSpec -Force | Out-Null } catch { Write-Warning ("OpenAPI export failed: {0}" -f $_.Exception.Message) }

  try {
    while ($state.IsRunning -and $listener.IsListening) {
      $context = $listener.GetContext()
      try {
        Invoke-RomCleanupApiRequest -Context $context -ApiKey $resolvedApiKey
      } catch {
        try {
          Write-ApiJsonResponse -Context $context -StatusCode 500 -Body ([ordered]@{ error = $_.Exception.Message })
        } catch {
          Write-Warning ("API response write failed: {0}" -f $_.Exception.Message)
        }
      }
    }
  } finally {
    Stop-RomCleanupApiServer
  }
}

function Stop-RomCleanupApiServer {
  $state = Get-ApiServerState
  $state.IsRunning = $false

  if ($state.Listener) {
    try { $state.Listener.Stop() } catch { Write-Verbose ("API listener stop warning: {0}" -f $_.Exception.Message) }
    try { $state.Listener.Close() } catch { Write-Verbose ("API listener close warning: {0}" -f $_.Exception.Message) }
  }

  $state.Listener = $null
}
