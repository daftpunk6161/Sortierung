<#
.SYNOPSIS
    Mutation Testing Light - ROM Cleanup Test Suite
    
.DESCRIPTION
    Führt definierte Code-Mutationen aus und prüft ob die Tests diese erkennen.
    Eine "überlebende" Mutation = schwacher Test = Lücke in der Test-Coverage.
    
.EXAMPLE
    .\Invoke-MutationLight.ps1
    
.EXAMPLE
    .\Invoke-MutationLight.ps1 -Verbose
    
.OUTPUTS
    Exit 0: Alle Mutationen wurden gekillt (Tests sind gut)
    Exit 1: Mindestens eine Mutation hat überlebt (Tests verbessern!)
#>
[CmdletBinding()]
param(
    [string]$MutationsFile = (Join-Path $PSScriptRoot 'mutations.json'),
    [string]$ScriptPath = (Join-Path $PSScriptRoot '..\..\simple_sort.ps1'),
    [string]$TestsPath = (Join-Path $PSScriptRoot '..\tests'),
    [switch]$StopOnSurvived
)

$ErrorActionPreference = 'Stop'

function Write-Status {
    param([string]$Message, [string]$Type = 'Info')
    $color = switch ($Type) {
        'Success' { 'Green' }
        'Error'   { 'Red' }
        'Warning' { 'Yellow' }
        default   { 'Cyan' }
    }
    Write-Host "[$Type] $Message" -ForegroundColor $color
}

# ============================================================
# MAIN
# ============================================================

Write-Status "Mutation Testing Light - ROM Cleanup"
Write-Status "===================================="

# Load mutations
if (-not (Test-Path -LiteralPath $MutationsFile)) {
    Write-Status "mutations.json nicht gefunden: $MutationsFile" -Type Error
    exit 1
}

$mutationData = Get-Content -LiteralPath $MutationsFile -Raw | ConvertFrom-Json
$mutations = $mutationData.mutations

Write-Status "Gefunden: $($mutations.Count) Mutationen"
Write-Status "Skript: $ScriptPath"
Write-Status "Tests: $TestsPath"
Write-Status ""

# Load original script content
$originalContent = Get-Content -LiteralPath $ScriptPath -Raw

# Results tracking
$results = [System.Collections.Generic.List[pscustomobject]]::new()
$killed = 0
$survived = 0
$errors = 0

foreach ($mutation in $mutations) {
    $id = $mutation.id
    $description = $mutation.description
    $find = $mutation.find
    $replace = $mutation.replace
    $expectedTests = $mutation.killedBy -join ', '
    
    Write-Status "--- Mutation ${id}: $description ---"
    
    # Apply mutation
    if (-not ($originalContent -match [regex]::Escape($find))) {
        Write-Status "  SKIP: Pattern nicht gefunden" -Type Warning
        $results.Add([pscustomobject]@{
            Id = $id
            Description = $description
            Status = 'SKIP'
            Reason = 'Pattern not found'
        })
        continue
    }
    
    $mutatedContent = $originalContent.Replace($find, $replace)
    
    # Create temp workspace
    $tempDir = Join-Path $env:TEMP "mutation_${id}_$(Get-Random)"
    $tempScript = Join-Path $tempDir 'simple_sort.ps1'
    
    try {
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $mutatedContent | Out-File -LiteralPath $tempScript -Encoding utf8 -Force
        
        # Copy tests
        Copy-Item -Path $TestsPath -Destination $tempDir -Recurse -Force
        
        # Run Pester
        $pesterResult = Invoke-Pester -Path (Join-Path $tempDir 'tests\*.Tests.ps1') `
            -PassThru -Output None
        
        $failedCount = $pesterResult.FailedCount
        # $passedCount available if needed: $pesterResult.PassedCount
        
        if ($failedCount -gt 0) {
            Write-Status "  KILLED: $failedCount Tests failed" -Type Success
            $killed++
            $results.Add([pscustomobject]@{
                Id = $id
                Description = $description
                Status = 'KILLED'
                FailedTests = $failedCount
                ExpectedBy = $expectedTests
            })
        } else {
            Write-Status "  SURVIVED! Keine Tests haben diese Mutation erkannt!" -Type Error
            $survived++
            $results.Add([pscustomobject]@{
                Id = $id
                Description = $description
                Status = 'SURVIVED'
                FailedTests = 0
                ExpectedBy = $expectedTests
            })
            
            if ($StopOnSurvived) {
                Write-Status "Stopping on first survived mutation" -Type Warning
                break
            }
        }
    } catch {
        Write-Status "  ERROR: $($_.Exception.Message)" -Type Error
        $errors++
        $results.Add([pscustomobject]@{
            Id = $id
            Description = $description
            Status = 'ERROR'
            Reason = $_.Exception.Message
        })
    } finally {
        # Cleanup
        if (Test-Path -LiteralPath $tempDir) {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================
# REPORT
# ============================================================

Write-Status ""
Write-Status "============ MUTATION REPORT ============"
Write-Status "Killed:   $killed" -Type Success
Write-Status "Survived: $survived" -Type $(if ($survived -gt 0) { 'Error' } else { 'Success' })
Write-Status "Errors:   $errors" -Type $(if ($errors -gt 0) { 'Warning' } else { 'Info' })
Write-Status ""

# Details table
$results | Format-Table -Property Id, Status, Description, FailedTests, ExpectedBy -AutoSize

# Generate JSON report
$reportPath = Join-Path $PSScriptRoot "mutation-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$results | ConvertTo-Json -Depth 3 | Out-File -LiteralPath $reportPath -Encoding utf8

Write-Status "Report saved: $reportPath"

# Exit code
if ($survived -gt 0) {
    Write-Status ""
    Write-Status "!!! $survived MUTATION(S) SURVIVED !!!" -Type Error
    Write-Status "Tests verbessern um diese Mutationen zu killen:" -Type Warning
    $results | Where-Object { $_.Status -eq 'SURVIVED' } | ForEach-Object {
        Write-Status "  - M$($_.Id): $($_.Description) (expected: $($_.ExpectedBy))" -Type Warning
    }
    exit 1
} else {
    Write-Status ""
    Write-Status "Alle Mutationen wurden gekillt. Tests sind robust!" -Type Success
    exit 0
}
