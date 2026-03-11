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

function Get-ScheduledRunHistory {
  <#
  .SYNOPSIS
    Liest die Historie gespeicherter Scheduler-Laeufe.
  .PARAMETER Last
    Anzahl der letzten Eintraege.
  #>
  param(
    [int]$Last = 20
  )

  $logDir = Join-Path $env:APPDATA 'RomCleanupRegionDedupe' 'scheduler-logs'
  if (-not (Test-Path -LiteralPath $logDir)) {
    return @()
  }

  $logs = Get-ChildItem -LiteralPath $logDir -Filter '*.json' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First $Last

  $results = [System.Collections.Generic.List[hashtable]]::new()
  foreach ($log in $logs) {
    try {
      $entry = Get-Content -LiteralPath $log.FullName -Raw | ConvertFrom-Json
      $results.Add(@{
        Timestamp = $log.LastWriteTime
        FileName  = $log.Name
        Status    = if ($entry.Status) { $entry.Status } else { 'Unknown' }
        ExitCode  = if ($null -ne $entry.ExitCode) { $entry.ExitCode } else { -1 }
        Duration  = if ($entry.Duration) { $entry.Duration } else { '' }
      })
    } catch {
      $results.Add(@{ Timestamp = $log.LastWriteTime; FileName = $log.Name; Status = 'ParseError'; ExitCode = -1 })
    }
  }

  return ,$results.ToArray()
}

function Register-CronScheduledTask {
  <#
  .SYNOPSIS
    Erstellt einen Windows Task Scheduler-Task mit Cron-aehnlichem Schedule.
  .PARAMETER TaskName
    Name des Tasks.
  .PARAMETER CronExpression
    Cron-Ausdruck (Minute Stunde Tag Monat Wochentag), z.B. "0 3 * * 0" = Sonntag 03:00.
  .PARAMETER Roots
    ROM-Root-Pfade.
  .PARAMETER Mode
    DryRun oder Move.
  #>
  param(
    [string]$TaskName = 'RomCleanup-Cron',
    [Parameter(Mandatory)][string]$CronExpression,
    [Parameter(Mandatory)][string[]]$Roots,
    [ValidateSet('DryRun','Move')]
    [string]$Mode = 'DryRun'
  )

  $parts = $CronExpression.Trim() -split '\s+'
  if ($parts.Count -lt 5) {
    return @{ Success = $false; Error = 'Ungueltiger Cron-Ausdruck: erwartet 5 Felder (Min Std Tag Mon Wtag)' }
  }

  $minute     = $parts[0]
  $hour       = $parts[1]
  # $dayOfMonth/$month: reserviert fuer erweiterte Cron-Logik (monatliche Trigger)
  $dayOfWeek  = $parts[4]

  $cronTime = '{0}:{1}' -f $(if ($hour -eq '*') { '00' } else { $hour.PadLeft(2,'0') }),
                            $(if ($minute -eq '*') { '00' } else { $minute.PadLeft(2,'0') })

  $triggers = [System.Collections.Generic.List[object]]::new()

  if ($dayOfWeek -ne '*') {
    $dayMap = @{ '0'='Sunday'; '1'='Monday'; '2'='Tuesday'; '3'='Wednesday'; '4'='Thursday'; '5'='Friday'; '6'='Saturday'; '7'='Sunday' }
    $days = $dayOfWeek -split ','
    foreach ($d in $days) {
      $dName = if ($dayMap.ContainsKey($d)) { $dayMap[$d] } else { $d }
      $triggers.Add((New-ScheduledTaskTrigger -Weekly -DaysOfWeek $dName -At $cronTime))
    }
  } else {
    $triggers.Add((New-ScheduledTaskTrigger -Daily -At $cronTime))
  }

  $rootsArg = ($Roots | ForEach-Object { "`"$_`"" }) -join ','
  $scriptPath = Join-Path $PSScriptRoot '..\..'
  $scriptPath = [System.IO.Path]::GetFullPath((Join-Path $scriptPath 'Invoke-RomCleanup.ps1'))

  $action = New-ScheduledTaskAction -Execute 'pwsh.exe' `
    -Argument "-NoProfile -File `"$scriptPath`" -Roots $rootsArg -Mode $Mode"

  $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -RunOnlyIfNetworkAvailable:$false

  try {
    Register-ScheduledTask -TaskName $TaskName -Trigger $triggers.ToArray() `
      -Action $action -Settings $settings -Force | Out-Null

    $logDir = Join-Path $env:APPDATA 'RomCleanupRegionDedupe' 'scheduler-logs'
    if (-not (Test-Path -LiteralPath $logDir)) { [void](New-Item -Path $logDir -ItemType Directory -Force) }
    $logEntry = @{
      TaskName = $TaskName
      CronExpression = $CronExpression
      Roots = $Roots
      Mode = $Mode
      RegisteredAt = [datetime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 3
    $logFile = Join-Path $logDir "register-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    [System.IO.File]::WriteAllText($logFile, $logEntry, [System.Text.Encoding]::UTF8)

    return @{ Success = $true; TaskName = $TaskName; Triggers = $triggers.Count; CronExpression = $CronExpression }
  } catch {
    return @{ Success = $false; Error = $_.Exception.Message }
  }
}

function Unregister-RomCleanupScheduledTask {
  <#
  .SYNOPSIS
    Entfernt einen registrierten Scheduler-Task.
  #>
  param(
    [string]$TaskName = 'RomCleanup-Scheduled'
  )

  try {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
    return @{ Success = $true; TaskName = $TaskName }
  } catch {
    return @{ Success = $false; Error = $_.Exception.Message }
  }
}

