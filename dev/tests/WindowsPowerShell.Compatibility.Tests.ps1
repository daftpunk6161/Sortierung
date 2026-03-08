#requires -Modules Pester

Describe 'Windows PowerShell compatibility gate' {
    BeforeAll {
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root '..\..\simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }
        if (-not $script:root) {
            throw 'Workspace root could not be resolved.'
        }
        $script:root = (Resolve-Path (Join-Path $script:root '..\..')).Path

        $script:windowsPowerShell = (Get-Command powershell.exe -ErrorAction SilentlyContinue)
    }

    It 'powershell.exe should be available' {
        $script:windowsPowerShell | Should -Not -BeNullOrEmpty
    }

    It 'critical scripts should keep UTF-8 BOM for Windows PowerShell stability' {
        $criticalFiles = @(
            'simple_sort.ps1',
            'dev/modules/Core.ps1',
            'dev/modules/DatSources.ps1',
            'dev/modules/Report.ps1',
            'dev/modules/RunHelpers.ps1'
        )

        foreach ($relativePath in $criticalFiles) {
            $fullPath = Join-Path $script:root ($relativePath -replace '/', '\\')
            Test-Path -LiteralPath $fullPath -PathType Leaf | Should -BeTrue -Because "$relativePath must exist"

            $bytes = [System.IO.File]::ReadAllBytes($fullPath)
            $bytes.Length | Should -BeGreaterThan 3 -Because "$relativePath should not be empty"
            ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) |
                Should -BeTrue -Because "$relativePath must use UTF-8 BOM for stable parsing in powershell.exe"
        }
    }

    It 'module loader should parse and expose required commands in powershell.exe' {
        $loaderPath = Join-Path $script:root 'dev\modules\RomCleanupLoader.ps1'
        Test-Path -LiteralPath $loaderPath -PathType Leaf | Should -BeTrue

        $command = @"
`$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath '$script:root'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

`$modFiles = Get-ChildItem -LiteralPath '.\dev\modules' -Filter '*.ps1' | ForEach-Object { `$_.FullName }
foreach (`$file in `$modFiles) {
  `$tok = `$null
  `$err = `$null
  [System.Management.Automation.Language.Parser]::ParseFile(`$file, [ref]`$tok, [ref]`$err) | Out-Null
  if (`$err -and `$err.Count -gt 0) {
    throw ('Parse error in {0}: {1}' -f `$file, (`$err[0].Message))
  }
}

. '.\dev\modules\RomCleanupLoader.ps1'

`$required = @(
  'ConvertTo-SafeCsvValue',
  'Write-Reports',
  'Invoke-MovePhase',
  'Invoke-AuditRollback',
  'Resolve-GameKey',
  'Get-DatIndex',
  'New-SetItem'
)
`$missing = @(`$required | Where-Object { -not (Get-Command -Name `$_ -ErrorAction SilentlyContinue) })
if (`$missing.Count -gt 0) {
  throw ('Missing commands after loader import: ' + (`$missing -join ', '))
}

Write-Output 'OK'
"@

        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = 'powershell.exe'
        $psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"$command`""
        $psi.WorkingDirectory = $script:root
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $proc = [System.Diagnostics.Process]::Start($psi)
        $finished = $proc.WaitForExit(60000)
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()

        $finished | Should -BeTrue -Because 'powershell.exe compatibility probe must finish'
        if (-not $finished) {
            try { $proc.Kill() } catch { }
        }

        if ($proc.ExitCode -ne 0) {
            throw ("powershell.exe compatibility probe failed.`nSTDOUT:`n{0}`nSTDERR:`n{1}" -f $stdout, $stderr)
        }

        $stdout | Should -Match 'OK'
    }
}
