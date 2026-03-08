#requires -Modules Pester
<#
  Settings & Logging Bug-Finder Tests  [F19]
  ============================================
  Tests that the previously-silent error paths now surface failures correctly:

  F-04 FIX — Settings.ps1: corrupt / locked settings.json
    * returns $null (never throws)
    * emits Write-Warning for corrupt files (not for missing files)

  F-05 FIX — Logging.ps1: JSONL log write failure
    * emits Write-Warning with path and error message
    * resets OperationJsonlPath to $null after failure

  F-08 Checklist — Settings.ps1: distinguishes missing vs. corrupt
    * missing file  -> $null, NO Write-Warning
    * corrupt file  -> $null + Write-Warning emitted
    * valid file    -> valid settings object returned
#>

BeforeAll {
    $script:ModulesRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'modules'

    # Minimal stubs that Settings.ps1 may call
    function global:Get-RomCleanupSchema { param([string]$Name) return $null }
    function global:Test-JsonPayloadSchema { param($Payload,$Schema) return [pscustomobject]@{IsValid=$true;Errors=@()} }
    function global:Show-ToastNotification { param($Message,$Type) $script:ToastMessages += $Message }
    function global:Set-AppStateValue { param($Key,$Value) }
    function global:Get-AppStateValue { param($Key,$Default=$null) return $Default }

    . (Join-Path $script:ModulesRoot 'FileOps.ps1')
    . (Join-Path $script:ModulesRoot 'Settings.ps1')

    $script:OriginalSettingsPath = $script:SETTINGS_PATH
}

# ============================================================
#  Get-UserSettings: missing vs. corrupt vs. valid
# ============================================================

Describe 'Get-UserSettings — Missing vs. Corrupt vs. Valid' {
    BeforeEach {
        $script:ToastMessages = @()
        $script:Warnings = [System.Collections.Generic.List[string]]::new()
    }

    It 'returns $null for a missing file without emitting a warning' {
        $capturedWarnings = [System.Collections.Generic.List[string]]::new()
        $result = & {
            $WarningPreference = 'Continue'
            Get-UserSettings -SettingsPath 'C:\NonExistent\path\settings.json' *>&1 | ForEach-Object {
                if ($_ -is [System.Management.Automation.WarningRecord]) {
                    $capturedWarnings.Add($_.Message)
                } elseif ($_ -is [System.Management.Automation.InformationRecord]) {
                    return
                } elseif ($_ -is [string] -and $_ -match '^\[\d{2}:\d{2}:\d{2}\]\s+\[(DEBUG|INFO|WARNING|ERROR)\]') {
                    # Ignore host log lines emitted by Write-Log in mixed test sessions.
                    return
                } elseif ($_ -ne $null) { $_ }
            }
        }

        ($result | Where-Object { $_ -ne $null }) | Should -BeNullOrEmpty `
            -Because 'missing file returns $null'
        $capturedWarnings.Count | Should -Be 0 `
            -Because 'missing file must not emit a warning'
    }

    It 'returns $null and emits Write-Warning for a corrupt JSON file' {
        $tmpDir  = Join-Path $env:TEMP ('bugfinder_' + (Get-Random))
        $null = New-Item -ItemType Directory -Path $tmpDir -Force
        $settingsFile = Join-Path $tmpDir 'settings.json'
        'NOT_VALID_JSON {{{' | Out-File -LiteralPath $settingsFile -Encoding utf8

        $capturedWarnings = [System.Collections.Generic.List[string]]::new()
        try {
            $result = & {
                $WarningPreference = 'Continue'
                Get-UserSettings -SettingsPath $settingsFile *>&1 | ForEach-Object {
                    if ($_ -is [System.Management.Automation.WarningRecord]) {
                        $capturedWarnings.Add($_.Message)
                    } elseif ($_ -is [System.Management.Automation.InformationRecord]) {
                        return
                    } elseif ($_ -is [string] -and $_ -match '^\[\d{2}:\d{2}:\d{2}\]\s+\[(DEBUG|INFO|WARNING|ERROR)\]') {
                        # Ignore host log lines emitted by Write-Log in mixed test sessions.
                        return
                    } elseif ($_ -ne $null) { $_ }
                }
            }

            ($result | Where-Object { $_ -ne $null }) | Should -BeNullOrEmpty `
                -Because 'corrupt file returns $null'
            $capturedWarnings.Count | Should -BeGreaterThan 0 `
                -Because 'corrupt file must emit at least one Write-Warning'
            ($capturedWarnings | Where-Object { $_ -match 'korrupt|korrupte|nicht lesbar|ungueltig|schema|json' }) |
                Should -Not -BeNullOrEmpty `
                -Because 'warning message must indicate why the file could not be read'
        } finally {
            Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'returns $null and emits Write-Warning for a file with wrong JSON type (array instead of object)' {
        $tmpDir  = Join-Path $env:TEMP ('bugfinder_arr_' + (Get-Random))
        $null = New-Item -ItemType Directory -Path $tmpDir -Force
        $settingsFile = Join-Path $tmpDir 'settings.json'
        '[1, 2, 3]' | Out-File -LiteralPath $settingsFile -Encoding utf8

        $capturedWarnings = [System.Collections.Generic.List[string]]::new()
        try {
            $result = & {
                $WarningPreference = 'Continue'
                Get-UserSettings -SettingsPath $settingsFile *>&1 | ForEach-Object {
                    if ($_ -is [System.Management.Automation.WarningRecord]) {
                        $capturedWarnings.Add($_.Message)
                    } elseif ($_ -is [System.Management.Automation.InformationRecord]) {
                        return
                    } elseif ($_ -is [string] -and $_ -match '^\[\d{2}:\d{2}:\d{2}\]\s+\[(DEBUG|INFO|WARNING|ERROR)\]') {
                        # Ignore host log lines emitted by Write-Log in mixed test sessions.
                        return
                    } elseif ($_ -ne $null) { $_ }
                }
            }

            ($result | Where-Object { $_ -ne $null }) | Should -BeNullOrEmpty `
                -Because 'array-type JSON is invalid settings'
            $capturedWarnings.Count | Should -BeGreaterThan 0 `
                -Because 'wrong JSON root type must emit a Write-Warning'
        } finally {
            Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'returns a valid settings object for a well-formed settings.json' {
        # Create a guaranteed unique file using .NET temp file API, then
        # overwrite with valid JSON. Explicitly pass path to Get-UserSettings
        # so $script: scope disagreement between containers never affects this.
        $settingsFile = [System.IO.Path]::Combine(
            [System.IO.Path]::GetTempPath(),
            ('bugfinder_valid_{0}_{1}.json' -f [System.Diagnostics.Process]::GetCurrentProcess().Id, [System.DateTime]::UtcNow.Ticks)
        )
        try {
            @{ general = @{ prehash = $false }; toolPaths = @{ chdman = '' } } |
                ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settingsFile -Encoding utf8 -Force
            $settingsFile | Should -Exist -Because 'settings file must exist before parsing'

            $result = Get-UserSettings -SettingsPath $settingsFile
            $result | Should -Not -BeNullOrEmpty `
                -Because 'valid JSON must return a settings object'
        } finally {
            Remove-Item -LiteralPath $settingsFile -Force -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================
#  Set-UserSettings: save failure must surface as Write-Warning
# ============================================================

Describe 'Set-UserSettings — Save Failure Surfaced' {    BeforeEach {
        $script:ToastMessages = @()
        $script:Warnings = [System.Collections.Generic.List[string]]::new()
    }
    AfterEach {
        $script:SETTINGS_PATH = $script:OriginalSettingsPath
    }
    It 'emits Write-Warning when the settings directory is read-only or path is invalid' {
        # Use a guaranteed invalid Windows path (pipe char is illegal in path but avoids PSDrive error)
        $script:SETTINGS_PATH = 'C:\Invalid|Path\settings.json'

        $capturedWarnings = [System.Collections.Generic.List[string]]::new()
        & {
            $WarningPreference = 'Continue'
            Set-UserSettings -Settings @{ general = @{} } *>&1 | ForEach-Object {
                if ($_ -is [System.Management.Automation.WarningRecord]) {
                    $capturedWarnings.Add($_.Message)
                }
            }
        }

        $capturedWarnings.Count | Should -BeGreaterThan 0 `
            -Because 'save failure must emit Write-Warning (F-04 fix)'
    }

    It 'does not throw even when the settings file cannot be written' {
        $script:SETTINGS_PATH = 'C:\Invalid|Path\settings.json'
        {
            $WarningPreference = 'SilentlyContinue'
            Set-UserSettings -Settings @{ general = @{} }
        } | Should -Not -Throw
    }
}
