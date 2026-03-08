# ================================================================
#  NOTIFICATIONS - Email & Webhook report delivery
#  Used after scheduled runs or on-demand report distribution.
# ================================================================

function Show-ToastNotification {
  <#
  .SYNOPSIS
    Zeigt eine lokale Windows-Benachrichtigung (Balloon/Tray) an.
  .PARAMETER Message
    Nachrichtentext.
  .PARAMETER Title
    Titel der Benachrichtigung.
  .PARAMETER Type
    Info/Warning/Error steuert das Symbol.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$Message,
    [string]$Title = 'ROM Cleanup',
    [ValidateSet('Info','Warning','Error')][string]$Type = 'Info'
  )

  if ([string]::IsNullOrWhiteSpace($Message)) { return }

  try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction SilentlyContinue
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

    $escapedTitle = [System.Security.SecurityElement]::Escape([string]$Title)
    $escapedMessage = [System.Security.SecurityElement]::Escape([string]$Message)
    $xml = @"
<toast>
  <visual>
    <binding template='ToastGeneric'>
      <text>$escapedTitle</text>
      <text>$escapedMessage</text>
    </binding>
  </visual>
</toast>
"@

    $xmlDoc = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xmlDoc.LoadXml($xml)

    $toast = [Windows.UI.Notifications.ToastNotification]::new($xmlDoc)
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('ROMCleanup')
    $notifier.Show($toast)
  } catch {
    try { Write-Host ("[{0}] {1}: {2}" -f $Type, $Title, $Message) } catch { }
  }
}

function Send-ReportEmail {
  <#
  .SYNOPSIS
    Sends report summary via SMTP email.
  .PARAMETER To
    Recipient email address(es).
  .PARAMETER SmtpServer
    SMTP server hostname.
  .PARAMETER SmtpPort
    SMTP port (default 587).
  .PARAMETER From
    Sender email address.
  .PARAMETER Credential
    PSCredential for SMTP auth (optional).
  .PARAMETER UseSsl
    Use TLS/SSL (default $true).
  .PARAMETER Subject
    Email subject line.
  .PARAMETER Body
    Email body (HTML supported).
  .PARAMETER Attachments
    Optional file paths to attach.
  #>
  [CmdletBinding(SupportsShouldProcess=$true)]
  param(
    [Parameter(Mandatory=$true)][string[]]$To,
    [Parameter(Mandatory=$true)][string]$SmtpServer,
    [int]$SmtpPort = 587,
    [string]$From = 'romcleanup@localhost',
    [pscredential]$Credential,
    [bool]$UseSsl = $true,
    [string]$Subject = 'ROM Cleanup Report',
    [Parameter(Mandatory=$true)][string]$Body,
    [string[]]$Attachments = @()
  )

  if (-not $PSCmdlet.ShouldProcess(($To -join ', '), 'Send report email')) { return $false }

  $params = @{
    To          = $To
    From        = $From
    Subject     = $Subject
    Body        = $Body
    SmtpServer  = $SmtpServer
    Port        = $SmtpPort
    BodyAsHtml  = $true
    ErrorAction = 'Stop'
  }
  if ($UseSsl) { $params['UseSsl'] = $true }
  if ($Credential) { $params['Credential'] = $Credential }
  if ($Attachments.Count -gt 0) {
    $valid = @($Attachments | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($valid.Count -gt 0) { $params['Attachments'] = $valid }
  }

  try {
    Send-MailMessage @params
    return $true
  } catch {
    Write-Warning "Email send failed: $_"
    return $false
  }
}

function Invoke-ReportWebhook {
  <#
  .SYNOPSIS
    Sends report summary to a webhook URL (Discord, Slack, Teams, or generic JSON).
  .PARAMETER Url
    Webhook endpoint URL.
  .PARAMETER Format
    One of: discord, slack, teams, json (default: json).
  .PARAMETER Summary
    Short text summary.
  .PARAMETER ReportPath
    Optional path to the HTML report for link inclusion.
  .PARAMETER ExtraData
    Optional hashtable of additional data fields.
  #>
  [CmdletBinding(SupportsShouldProcess=$true)]
  param(
    [Parameter(Mandatory=$true)][string]$Url,
    [ValidateSet('discord','slack','teams','json')][string]$Format = 'json',
    [Parameter(Mandatory=$true)][string]$Summary,
    [string]$ReportPath = '',
    [hashtable]$ExtraData = @{}
  )

  if (-not $PSCmdlet.ShouldProcess($Url, 'Send webhook')) { return $false }

  $payload = switch ($Format) {
    'discord' {
      @{ content = $Summary } | ConvertTo-Json -Depth 3
    }
    'slack' {
      @{ text = $Summary } | ConvertTo-Json -Depth 3
    }
    'teams' {
      @{
        '@type'    = 'MessageCard'
        summary    = 'ROM Cleanup Report'
        themeColor = '0076D7'
        sections   = @(@{
          activityTitle = 'ROM Cleanup Report'
          text          = $Summary
        })
      } | ConvertTo-Json -Depth 5
    }
    default {
      $data = @{
        summary   = $Summary
        timestamp = (Get-Date).ToUniversalTime().ToString('o')
      }
      if ($ReportPath) { $data['reportPath'] = $ReportPath }
      foreach ($k in $ExtraData.Keys) { $data[$k] = $ExtraData[$k] }
      $data | ConvertTo-Json -Depth 5
    }
  }

  try {
    $params = @{
      Uri         = $Url
      Method      = 'POST'
      Body        = $payload
      ContentType = 'application/json; charset=utf-8'
      ErrorAction = 'Stop'
    }
    Invoke-RestMethod @params | Out-Null
    return $true
  } catch {
    Write-Warning "Webhook failed: $_"
    return $false
  }
}

function Send-ScheduledRunNotification {
  <#
  .SYNOPSIS
    Convenience wrapper: sends notification after a scheduled run.
    Reads email/webhook config from AppState keys:
      NotificationEmail  = @{ Enabled=$true; To='a@b.com'; SmtpServer='smtp.example.com'; SmtpPort=587; From='rom@x.com'; UseSsl=$true }
      NotificationWebhook = @{ Enabled=$true; Url='https://hooks.slack.com/...'; Format='slack' }
  .PARAMETER Summary
    Summary text (e.g. "5 Spiele, 3 Duplikate, 1 Junk").
  .PARAMETER ReportPath
    Path to the generated report file.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$Summary,
    [string]$ReportPath = ''
  )

  $sent = 0

  # Email notification
  try {
    $emailCfg = Get-AppStateValue -Key 'NotificationEmail' -Default $null
    if ($emailCfg -and $emailCfg.Enabled -and $emailCfg.To -and $emailCfg.SmtpServer) {
      $subject = if (Get-Command Get-UIString -ErrorAction SilentlyContinue) {
        Get-UIString 'Scheduler.EmailSubject' (Get-Date -Format 'yyyy-MM-dd')
      } else {
        "ROM Cleanup Report - $(Get-Date -Format 'yyyy-MM-dd')"
      }
      $body = "<html><body><h2>$subject</h2><p>$Summary</p>"
      if ($ReportPath -and (Test-Path -LiteralPath $ReportPath)) {
        $body += "<p>Report: $ReportPath</p>"
      }
      $body += '</body></html>'

      $smtpPort = if ($emailCfg.PSObject.Properties.Name -contains 'SmtpPort') { [int]$emailCfg.SmtpPort } else { 587 }
      $from     = if ($emailCfg.PSObject.Properties.Name -contains 'From') { $emailCfg.From } else { 'romcleanup@localhost' }
      $useSsl   = if ($emailCfg.PSObject.Properties.Name -contains 'UseSsl') { [bool]$emailCfg.UseSsl } else { $true }

      $result = Send-ReportEmail -To @($emailCfg.To) -SmtpServer $emailCfg.SmtpServer `
        -SmtpPort $smtpPort -From $from -Subject $subject -Body $body -UseSsl $useSsl
      if ($result) { $sent++ }
    }
  } catch { }

  # Webhook notification
  try {
    $webhookCfg = Get-AppStateValue -Key 'NotificationWebhook' -Default $null
    if ($webhookCfg -and $webhookCfg.Enabled -and $webhookCfg.Url) {
      $format = if ($webhookCfg.PSObject.Properties.Name -contains 'Format') { $webhookCfg.Format } else { 'json' }
      $result = Invoke-ReportWebhook -Url $webhookCfg.Url -Format $format `
        -Summary $Summary -ReportPath $ReportPath
      if ($result) { $sent++ }
    }
  } catch { }

  return $sent
}

