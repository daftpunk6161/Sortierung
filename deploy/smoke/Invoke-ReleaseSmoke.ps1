<#
.SYNOPSIS
    Runs the release-oriented stabilization smoke matrix for Romulus.

.DESCRIPTION
    Executes the reproducible release gates that complement the normal build/test
    pipeline:
      - solution build
      - full regression suite (optional if already run upstream)
      - benchmark manifest integrity + coverage gate
      - targeted reach / conversion / accessibility regression slices
      - real headless smoke against the built API

    Usage:
      pwsh -NoProfile -File deploy/smoke/Invoke-ReleaseSmoke.ps1
      pwsh -NoProfile -File deploy/smoke/Invoke-ReleaseSmoke.ps1 -Configuration Release -SkipBuild -SkipFullTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$SkipFullTests,
    [switch]$SkipCoverageGate,
    [switch]$SkipReachSlice,
    [switch]$SkipConversionSlice,
    [switch]$SkipAccessibilitySmoke,
    [switch]$SkipHeadlessSmoke
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CommandStep {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$FilePath,
        [Parameter(Mandatory = $true)] [string[]]$Arguments
    )

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-TestSlice {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$Filter,
        [Parameter(Mandatory = $true)] [string]$Project,
        [Parameter(Mandatory = $true)] [string]$Configuration
    )

    Invoke-CommandStep -Name $Name -FilePath 'dotnet' -Arguments @(
        'test',
        $Project,
        '--configuration', $Configuration,
        '--no-build',
        '--nologo',
        '--filter', $Filter
    )
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solution = Join-Path $repoRoot 'src' 'Romulus.sln'
$testProject = Join-Path $repoRoot 'src' 'Romulus.Tests' 'Romulus.Tests.csproj'
$coverageGateScript = Join-Path $repoRoot 'benchmark' 'tools' 'Invoke-CoverageGate.ps1'
$manifestIntegrityScript = Join-Path $repoRoot 'benchmark' 'tools' 'Test-ManifestIntegrity.ps1'
$headlessSmokeScript = Join-Path $repoRoot 'deploy' 'smoke' 'Invoke-HeadlessSmoke.ps1'

if (-not $SkipBuild) {
    Invoke-CommandStep -Name 'Build solution' -FilePath 'dotnet' -Arguments @(
        'build',
        $solution,
        '--configuration', $Configuration,
        '-nologo',
        '-clp:ErrorsOnly'
    )
}

if (-not $SkipFullTests) {
    Invoke-CommandStep -Name 'Full regression suite' -FilePath 'dotnet' -Arguments @(
        'test',
        $testProject,
        '--configuration', $Configuration,
        '--no-build',
        '--nologo'
    )
}

if (-not $SkipCoverageGate) {
    Invoke-CommandStep -Name 'Benchmark manifest integrity' -FilePath 'pwsh' -Arguments @(
        '-NoProfile',
        '-File', $manifestIntegrityScript
    )

    Invoke-CommandStep -Name 'Benchmark coverage gate' -FilePath 'pwsh' -Arguments @(
        '-NoProfile',
        '-File', $coverageGateScript,
        '-NoBuild'
    )
}

if (-not $SkipReachSlice) {
    Invoke-TestSlice -Name 'Reach regression slice' `
        -Filter 'FullyQualifiedName~ApiReachIntegrationTests|FullyQualifiedName~OpenApiReachTests' `
        -Project $testProject `
        -Configuration $Configuration
}

if (-not $SkipConversionSlice) {
    Invoke-TestSlice -Name 'Conversion safety slice' `
        -Filter 'FullyQualifiedName~ConversionRegistrySchemaTests|FullyQualifiedName~ReachInvokerTests|FullyQualifiedName~ConversionExecutorHardeningTests|FullyQualifiedName~ToolRunnerAdapterTests' `
        -Project $testProject `
        -Configuration $Configuration
}

if (-not $SkipAccessibilitySmoke) {
    Invoke-TestSlice -Name 'Accessibility structural smoke' `
        -Filter 'FullyQualifiedName~Phase8ThemeAccessibilityTests|FullyQualifiedName~WpfProductizationTests' `
        -Project $testProject `
        -Configuration $Configuration
}

if (-not $SkipHeadlessSmoke) {
    Invoke-CommandStep -Name 'Real headless smoke' -FilePath 'pwsh' -Arguments @(
        '-NoProfile',
        '-File', $headlessSmokeScript,
        '-Configuration', $Configuration,
        '-SkipBuild'
    )
}

Write-Host ""
Write-Host "=== RELEASE SMOKE MATRIX PASSED ===" -ForegroundColor Green
Write-Host "Operator spot-check for keyboard/Narrator remains documented in docs/ux/narrator-testplan.md." -ForegroundColor DarkGray
