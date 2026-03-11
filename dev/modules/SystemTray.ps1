# ================================================================
#  SYSTEM TRAY – Mini-Modus / System-Tray-Integration (MF-18)
#  Dependencies: WpfApp.ps1 (System.Windows.Forms fuer NotifyIcon)
# ================================================================

function New-TrayIconConfig {
  <#
  .SYNOPSIS
    Erstellt eine System-Tray-Konfiguration.
  .PARAMETER ToolTip
    Tooltip-Text.
  .PARAMETER IconState
    Status-Icon: Idle, Running, Error, Paused.
  #>
  param(
    [string]$ToolTip = 'RomCleanup',
    [ValidateSet('Idle','Running','Error','Paused')][string]$IconState = 'Idle'
  )

  return @{
    ToolTip       = $ToolTip
    IconState     = $IconState
    Visible       = $true
    MenuItems     = @()
    BalloonTitle  = $null
    BalloonText   = $null
  }
}

function Add-TrayMenuItem {
  <#
  .SYNOPSIS
    Fuegt ein Kontextmenu-Item zur Tray-Konfiguration hinzu.
  .PARAMETER Config
    Tray-Konfiguration.
  .PARAMETER Label
    Menuepunkt-Text.
  .PARAMETER Key
    Eindeutiger Schluessel.
  .PARAMETER IsSeparator
    Ob es ein Trennstrich ist.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][string]$Label,
    [Parameter(Mandatory)][string]$Key,
    [bool]$IsSeparator = $false
  )

  $Config.MenuItems += @{
    Label       = $Label
    Key         = $Key
    IsSeparator = $IsSeparator
  }

  return $Config
}

function Get-DefaultTrayMenu {
  <#
  .SYNOPSIS
    Erstellt das Standard-Kontextmenu fuer den System-Tray.
  #>
  $config = New-TrayIconConfig -ToolTip 'RomCleanup' -IconState 'Idle'
  $config = Add-TrayMenuItem -Config $config -Label 'Fenster anzeigen' -Key 'show'
  $config = Add-TrayMenuItem -Config $config -Label 'DryRun starten' -Key 'dryrun'
  $config = Add-TrayMenuItem -Config $config -Label '-' -Key 'sep1' -IsSeparator $true
  $config = Add-TrayMenuItem -Config $config -Label 'Status' -Key 'status'
  $config = Add-TrayMenuItem -Config $config -Label '-' -Key 'sep2' -IsSeparator $true
  $config = Add-TrayMenuItem -Config $config -Label 'Beenden' -Key 'exit'
  return $config
}

function Set-TrayIconState {
  <#
  .SYNOPSIS
    Aendert den Status des Tray-Icons.
  .PARAMETER Config
    Tray-Konfiguration.
  .PARAMETER IconState
    Neuer Status.
  .PARAMETER ToolTip
    Optionaler neuer Tooltip.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [ValidateSet('Idle','Running','Error','Paused')][string]$IconState,
    [string]$ToolTip
  )

  $Config.IconState = $IconState
  if ($ToolTip) { $Config.ToolTip = $ToolTip }
  return $Config
}

function New-TrayBalloonNotification {
  <#
  .SYNOPSIS
    Erstellt eine Balloon-Benachrichtigung fuer den System-Tray.
  .PARAMETER Title
    Titel der Benachrichtigung.
  .PARAMETER Text
    Text der Benachrichtigung.
  .PARAMETER Icon
    Icon-Typ: Info, Warning, Error, None.
  .PARAMETER TimeoutMs
    Anzeigedauer in Millisekunden.
  #>
  param(
    [Parameter(Mandatory)][string]$Title,
    [Parameter(Mandatory)][string]$Text,
    [ValidateSet('Info','Warning','Error','None')][string]$Icon = 'Info',
    [int]$TimeoutMs = 5000
  )

  return @{
    Title     = $Title
    Text      = $Text
    Icon      = $Icon
    TimeoutMs = $TimeoutMs
  }
}

# ── WPF NotifyIcon Integration ──────────────────────────────────────────

function Initialize-SystemTrayIcon {
  <#
  .SYNOPSIS
    Erstellt und zeigt ein echtes System-Tray-Icon via System.Windows.Forms.NotifyIcon.
    Gibt das NotifyIcon-Objekt zurueck (fuer spaetere Updates/Dispose).
  .PARAMETER Window
    WPF-Hauptfenster (fuer Show/Hide).
  .PARAMETER Config
    Tray-Konfiguration von Get-DefaultTrayMenu.
  .PARAMETER OnMenuAction
    ScriptBlock der bei Menu-Klick aufgerufen wird. Erhaelt $Key als Parameter.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [hashtable]$Config = $null,
    [scriptblock]$OnMenuAction = $null
  )

  if (-not $Config) { $Config = Get-DefaultTrayMenu }

  Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
  Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue

  $notifyIcon = [System.Windows.Forms.NotifyIcon]::new()
  $notifyIcon.Text = $Config.ToolTip
  $notifyIcon.Visible = $true

  # Icon aus Anwendungs-Icon oder generiertes Fallback
  try {
    $exePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $notifyIcon.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exePath)
  } catch {
    # Fallback: generiertes 16x16 Icon
    $bmp = [System.Drawing.Bitmap]::new(16, 16)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(0, 245, 255))
    $g.Dispose()
    $notifyIcon.Icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    $bmp.Dispose()
  }

  # Kontextmenu aufbauen
  $contextMenu = [System.Windows.Forms.ContextMenuStrip]::new()
  foreach ($mi in $Config.MenuItems) {
    if ($mi.IsSeparator) {
      [void]$contextMenu.Items.Add([System.Windows.Forms.ToolStripSeparator]::new())
    } else {
      $menuItem = [System.Windows.Forms.ToolStripMenuItem]::new($mi.Label)
      $itemKey = [string]$mi.Key
      $menuItem.Tag = $itemKey
      $menuItem.add_Click({
        param($sender, $e)
        $clickedKey = [string]$sender.Tag
        if ($clickedKey -eq 'show') {
          $Window.Dispatcher.Invoke({ $Window.Show(); $Window.WindowState = [System.Windows.WindowState]::Normal; $Window.Activate() })
        } elseif ($clickedKey -eq 'exit') {
          $Window.Dispatcher.Invoke({ $Window.Close() })
        } elseif ($OnMenuAction) {
          try { & $OnMenuAction $clickedKey } catch { }
        }
      }.GetNewClosure())
      [void]$contextMenu.Items.Add($menuItem)
    }
  }
  $notifyIcon.ContextMenuStrip = $contextMenu

  # Doppelklick → Fenster anzeigen
  $notifyIcon.add_DoubleClick({
    $Window.Dispatcher.Invoke({ $Window.Show(); $Window.WindowState = [System.Windows.WindowState]::Normal; $Window.Activate() })
  }.GetNewClosure())

  return $notifyIcon
}

function Update-SystemTrayIcon {
  <#
  .SYNOPSIS
    Aktualisiert Status und Tooltip des System-Tray-Icons.
  #>
  param(
    [Parameter(Mandatory)]$NotifyIcon,
    [ValidateSet('Idle','Running','Error','Paused')][string]$IconState = 'Idle',
    [string]$ToolTip = ''
  )

  if (-not $NotifyIcon) { return }

  $stateText = switch ($IconState) {
    'Idle'    { 'Bereit' }
    'Running' { 'Laeuft...' }
    'Error'   { 'Fehler' }
    'Paused'  { 'Pausiert' }
  }

  $newTip = if ($ToolTip) { "RomCleanup - $ToolTip" } else { "RomCleanup - $stateText" }
  if ($newTip.Length -gt 63) { $newTip = $newTip.Substring(0, 63) }
  $NotifyIcon.Text = $newTip
}

function Show-TrayBalloon {
  <#
  .SYNOPSIS
    Zeigt eine Balloon-Benachrichtigung im System-Tray.
  #>
  param(
    [Parameter(Mandatory)]$NotifyIcon,
    [Parameter(Mandatory)][hashtable]$Notification
  )

  if (-not $NotifyIcon) { return }

  $iconType = switch ($Notification.Icon) {
    'Info'    { [System.Windows.Forms.ToolTipIcon]::Info }
    'Warning' { [System.Windows.Forms.ToolTipIcon]::Warning }
    'Error'   { [System.Windows.Forms.ToolTipIcon]::Error }
    default   { [System.Windows.Forms.ToolTipIcon]::None }
  }

  $NotifyIcon.ShowBalloonTip(
    $Notification.TimeoutMs,
    $Notification.Title,
    $Notification.Text,
    $iconType
  )
}

function Remove-SystemTrayIcon {
  <#
  .SYNOPSIS
    Entfernt das System-Tray-Icon und gibt Ressourcen frei.
  #>
  param([Parameter(Mandatory)]$NotifyIcon)

  if ($NotifyIcon) {
    $NotifyIcon.Visible = $false
    $NotifyIcon.Dispose()
  }
}

function Enable-MinimizeToTray {
  <#
  .SYNOPSIS
    Registriert einen Handler, der das Fenster beim Minimieren in den Tray schickt.
  .PARAMETER Window
    WPF-Hauptfenster.
  .PARAMETER NotifyIcon
    NotifyIcon-Objekt von Initialize-SystemTrayIcon.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)]$NotifyIcon
  )

  $Window.add_StateChanged({
    if ($Window.WindowState -eq [System.Windows.WindowState]::Minimized) {
      $Window.Hide()
      Update-SystemTrayIcon -NotifyIcon $NotifyIcon -IconState 'Idle' -ToolTip 'Im Hintergrund'
    }
  }.GetNewClosure())

  # Bei Close: Tray-Icon aufräumen
  $Window.add_Closed({
    Remove-SystemTrayIcon -NotifyIcon $NotifyIcon
  }.GetNewClosure())
}
