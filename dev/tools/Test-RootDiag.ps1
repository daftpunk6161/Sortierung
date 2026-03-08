#requires -version 5.1
<#
  Diagnose-Skript: Testet Root-Collection + Log-Action ohne GUI
#>
$ErrorActionPreference = 'Continue'
$moduleDir = 'C:\Code\Sortierung\dev\modules'

Write-Host '=== Lade Module ===' -ForegroundColor Cyan
foreach ($m in @('WpfXaml.ps1','WpfHost.ps1','WpfSelectionConfig.ps1','WpfMainViewModel.ps1','WpfEventHandlers.ps1','SimpleSort.WpfMain.ps1')) {
  $path = Join-Path $moduleDir $m
  try {
    . $path
    Write-Host "  OK: $m"
  } catch {
    Write-Host "  FEHLER $m : $($_.Exception.Message)" -ForegroundColor Red
  }
}

Write-Host ''
Write-Host '=== ViewModel ===' -ForegroundColor Cyan

$vm = $null
try {
  $vm = New-WpfMainViewModel
  Write-Host "  VM-Typ:   $($vm.GetType().FullName)"
  Write-Host "  Roots-Typ: $($vm.Roots.GetType().FullName)"
  [void]$vm.Roots.Add('C:\TestPath\Roms')
  Write-Host "  Add OK – Count=$($vm.Roots.Count)"
} catch {
  Write-Host "  FEHLER VM: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ''
Write-Host '=== Action-Delegat (wie Add-WpfLogLine) ===' -ForegroundColor Cyan
try {
  $captured = 'Hallo Log'
  $sb = { Write-Host "  [Action-invoke] $captured" }.GetNewClosure()
  $action = [System.Action]$sb
  $action.Invoke()
  Write-Host '  Delegat OK'
} catch {
  Write-Host "  FEHLER Delegat: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ''
Write-Host '=== WPF Dispatcher-BeginInvoke Syntax ===' -ForegroundColor Cyan
try {
  Add-Type -AssemblyName PresentationCore
  $d = [System.Windows.Threading.Dispatcher]::CurrentDispatcher
  $sb2 = { Write-Host '  [BeginInvoke] fired' }.GetNewClosure()
  $act2 = [System.Action]$sb2
  [void]$d.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Normal, $act2)
  # Pump dispatcher once
  $d.Invoke([System.Windows.Threading.DispatcherPriority]::ApplicationIdle, [System.Action]{})
  Write-Host '  BeginInvoke OK'
} catch {
  Write-Host "  FEHLER Dispatcher: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ''
Write-Host '=== Fertig ===' -ForegroundColor Green
