Set-StrictMode -Version Latest

$stateVar = $null
try { $stateVar = Get-Variable -Scope Script -Name PreflightState -ErrorAction Stop } catch { $stateVar = $null }
if (-not $stateVar -or -not $stateVar.Value) {
    $script:PreflightState = [pscustomobject]@{
        Errors   = New-Object System.Collections.Generic.List[string]
        Warnings = New-Object System.Collections.Generic.List[string]
    }
}

try { [void](Get-Variable -Scope Script -Name Roots -ErrorAction Stop) } catch { $script:Roots = @() }
try { [void](Get-Variable -Scope Script -Name TrashRoot -ErrorAction Stop) } catch { $script:TrashRoot = $null }
try { [void](Get-Variable -Scope Script -Name AuditRoot -ErrorAction Stop) } catch { $script:AuditRoot = $null }
try { [void](Get-Variable -Scope Script -Name ToolPaths -ErrorAction Stop) } catch { $script:ToolPaths = @{} }
try { [void](Get-Variable -Scope Script -Name DatRoot -ErrorAction Stop) } catch { $script:DatRoot = $null }
try { [void](Get-Variable -Scope Script -Name DatMap -ErrorAction Stop) } catch { $script:DatMap = @{} }

function Reset-PreflightState {
    $script:PreflightState.Errors.Clear()
    $script:PreflightState.Warnings.Clear()
}

function Get-PreflightState {
    return $script:PreflightState
}

function Add-PreflightError {
    param([string]$Message)
    if (-not [string]::IsNullOrWhiteSpace($Message)) { [void]$script:PreflightState.Errors.Add($Message) }
}

function Add-PreflightWarning {
    param([string]$Message)
    if (-not [string]::IsNullOrWhiteSpace($Message)) { [void]$script:PreflightState.Warnings.Add($Message) }
}

function Get-PreflightInput {
    param(
        [string]$Name,
        $Default
    )

    for ($scope = 1; $scope -le 10; $scope++) {
        $fromCaller = $null
        try { $fromCaller = Get-Variable -Scope $scope -Name $Name -ErrorAction Stop } catch { $fromCaller = $null }
        if ($fromCaller) { return $fromCaller.Value }
    }

    $fromScript = $null
    try { $fromScript = Get-Variable -Scope Script -Name $Name -ErrorAction Stop } catch { $fromScript = $null }
    if ($fromScript) { return $fromScript.Value }

    $fromGlobal = $null
    try { $fromGlobal = Get-Variable -Scope Global -Name $Name -ErrorAction Stop } catch { $fromGlobal = $null }
    if ($fromGlobal) { return $fromGlobal.Value }

    return $Default
}

function Resolve-NormalizedPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if (Get-Command Resolve-RootPath -ErrorAction SilentlyContinue) {
        try {
            $resolvedRootPath = Resolve-RootPath -Path $Path
            if (-not [string]::IsNullOrWhiteSpace([string]$resolvedRootPath)) {
                return $resolvedRootPath
            }
        } catch { }
    }
    try {
        $resolved = [System.IO.Path]::GetFullPath($Path)
        return $resolved.TrimEnd('\','/')
    } catch {
        return $null
    }
}

function Test-RootsFolders {
    Reset-PreflightState
    $rootsLocal = @(Get-PreflightInput -Name 'Roots' -Default @())

    if (-not $rootsLocal -or $rootsLocal.Count -eq 0) {
        Add-PreflightError 'No roots configured.'
        return
    }

    $normalizedRoots = New-Object System.Collections.Generic.List[string]

    foreach ($root in $rootsLocal) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            Add-PreflightError 'Root path is empty.'
            continue
        }

        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            Add-PreflightError ("Root not found: {0}" -f $root)
            continue
        }

        $norm = Resolve-NormalizedPath $root
        if ($norm) { [void]$normalizedRoots.Add($norm) }
    }

    for ($i = 0; $i -lt $normalizedRoots.Count; $i++) {
        for ($j = $i + 1; $j -lt $normalizedRoots.Count; $j++) {
            $a = $normalizedRoots[$i]
            $b = $normalizedRoots[$j]
            if ($a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or
                $a.StartsWith($b + '\', [StringComparison]::OrdinalIgnoreCase) -or
                $b.StartsWith($a + '\', [StringComparison]::OrdinalIgnoreCase)) {
                Add-PreflightError ("Overlapping roots detected: {0} <-> {1}" -f $a, $b)
            }
        }
    }
}

function Test-TrashRoot {
    Reset-PreflightState
    $trashRootLocal = [string](Get-PreflightInput -Name 'TrashRoot' -Default $null)
    $rootsLocal = @(Get-PreflightInput -Name 'Roots' -Default @())

    if ([string]::IsNullOrWhiteSpace($trashRootLocal)) { return }

    $parent = Split-Path -Parent $trashRootLocal
    if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $parent -PathType Container)) {
        Add-PreflightError 'TrashRoot parent does not exist.'
        return
    }

    $trashNorm = Resolve-NormalizedPath $trashRootLocal
    foreach ($root in @($rootsLocal)) {
        $rootNorm = Resolve-NormalizedPath $root
        if (-not $rootNorm -or -not $trashNorm) { continue }
        if ($trashNorm.StartsWith($rootNorm + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $rootNorm.StartsWith($trashNorm + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $rootNorm.Equals($trashNorm, [StringComparison]::OrdinalIgnoreCase)) {
            Add-PreflightError 'TrashRoot must not overlap with Root paths.'
            break
        }
    }
}

function Test-AuditRoot {
    Reset-PreflightState
    $auditRootLocal = [string](Get-PreflightInput -Name 'AuditRoot' -Default $null)
    if ([string]::IsNullOrWhiteSpace($auditRootLocal)) { return }

    $parent = Split-Path -Parent $auditRootLocal
    if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $parent -PathType Container)) {
        Add-PreflightWarning 'AuditRoot parent does not exist.'
    }
}

function Test-DatConfiguration {
    Reset-PreflightState
    $datRootLocal = [string](Get-PreflightInput -Name 'DatRoot' -Default $null)
    $datMapLocal = Get-PreflightInput -Name 'DatMap' -Default @{}
    if (-not [string]::IsNullOrWhiteSpace($datRootLocal) -and -not (Test-Path -LiteralPath $datRootLocal -PathType Container)) {
        Add-PreflightError 'DatRoot does not exist.'
    }

    $known = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($k in @('PS1','PS2','PSP','GC','WII','NES','SNES','N64','GB','GBC','GBA','NDS','MD','SMS','GG','PCE','NEOGEO','ARCADE','DOS')) {
        [void]$known.Add($k)
    }

    if ($datMapLocal) {
        foreach ($k in $datMapLocal.Keys) {
            if (-not $known.Contains([string]$k)) {
                Add-PreflightWarning ("Unrecognized console key: {0}" -f $k)
            }
        }
    }
}

function Test-ConversionTools {
    Reset-PreflightState
    $toolPathsLocal = Get-PreflightInput -Name 'ToolPaths' -Default @{}
    if (-not $toolPathsLocal) { return }

    foreach ($k in $toolPathsLocal.Keys) {
        $path = [string]$toolPathsLocal[$k]
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Add-PreflightError ("Tool path invalid: {0} -> {1}" -f $k, $path)
        }
    }
}

function Test-ReparsePointsInRoots {
    Reset-PreflightState
    $rootsLocal = @(Get-PreflightInput -Name 'Roots' -Default @())
    foreach ($root in @($rootsLocal)) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        try {
            $dirs = Get-ChildItem -LiteralPath $root -Directory -Recurse -ErrorAction Stop
            foreach ($d in $dirs) {
                if ($d.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                    Add-PreflightError ("ReparsePoint detected in root: {0}" -f $d.FullName)
                    return
                }
            }
        } catch {
            Add-PreflightError ("Failed to inspect root for reparse points: {0}" -f $root)
        }
    }
}

function Test-PathSubstringEdgeCases {
    Reset-PreflightState
    $rootsLocal = @(Get-PreflightInput -Name 'Roots' -Default @())
    foreach ($root in @($rootsLocal)) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        try {
            $norm = (Resolve-NormalizedPath $root)
            if ($norm) {
                [void]$norm.Substring([Math]::Min($norm.Length, $norm.TrimEnd('\','/').Length))
            }
        } catch {
            Add-PreflightError ("Path substring edge case failed: {0}" -f $root)
        }
    }
}
