function Register-RomCleanupScheduledTask {
  <#
  .SYNOPSIS
    Registriert einen täglichen Task für Headless-ROM-Cleanup.
  .PARAMETER TaskName
    Name des Scheduled Tasks.
  .PARAMETER Roots
    ROM-Roots für den Lauf.
  .PARAMETER Mode
    DryRun oder Move.
  .PARAMETER Time
    Startzeit im Format HH:mm.
  .EXAMPLE
    Register-RomCleanupScheduledTask -TaskName RomCleanup-Nightly -Roots 'D:\ROMs' -Mode DryRun -Time '03:00'
  #>
  param(
    [Parameter(Mandatory=$true)][string]$TaskName,
    [Parameter(Mandatory=$true)][string[]]$Roots,
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun',
    [string[]]$Prefer = @('EU','US','WORLD','JP'),
    [string]$Time = '03:00',
    [string]$WorkingDirectory = (Get-Location).Path
  )

  $invokePath = Join-Path $WorkingDirectory 'Invoke-RomCleanup.ps1'
  if (-not (Test-Path -LiteralPath $invokePath -PathType Leaf)) {
    throw "Invoke-RomCleanup.ps1 nicht gefunden: $invokePath"
  }

  $safeRoots = @($Roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($safeRoots.Count -eq 0) { throw 'Mindestens ein Root ist erforderlich.' }

  $args = New-Object System.Collections.Generic.List[string]
  [void]$args.Add('-NoProfile')
  [void]$args.Add('-File')
  [void]$args.Add(('"{0}"' -f $invokePath))
  [void]$args.Add('-Mode')
  [void]$args.Add($Mode)
  [void]$args.Add('-SkipConfirm')
  [void]$args.Add('-NotifyAfterRun')
  foreach ($r in $safeRoots) {
    [void]$args.Add('-Roots')
    [void]$args.Add(('"{0}"' -f $r))
  }
  foreach ($p in @($Prefer)) {
    [void]$args.Add('-Prefer')
    [void]$args.Add($p)
  }

  $action = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument ($args -join ' ')
  $trigger = New-ScheduledTaskTrigger -Daily -At $Time
  $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
  $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

  Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
  return $true
}

