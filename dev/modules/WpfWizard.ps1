# ================================================================
#  ISS-001: First-Start Wizard  (WpfWizard.ps1)
# ================================================================
#  6-step guided wizard shown on first launch (no settings file).
#  Steps: Intent → Root folders → Preflight → DryRun → Results → Confirm Move
#  Uses separate WPF Window via New-WpfWindowFromXaml.
# ================================================================

$script:WPF_WIZARD_XAML = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ROM Cleanup – Ersteinrichtung"
        Width="680" Height="520"
        MinWidth="580" MinHeight="440"
        WindowStartupLocation="CenterScreen"
        FontFamily="Segoe UI" FontSize="13"
        Background="{DynamicResource BrushBackground}"
        Foreground="{DynamicResource BrushTextPrimary}"
        ResizeMode="CanResizeWithGrip"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True">
  <Grid Margin="24">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <!-- ═══ STEP INDICATOR BAR ════════════════════════════════════════════ -->
    <Border Grid.Row="0" Background="{DynamicResource BrushSurface}"
            BorderBrush="{DynamicResource BrushBorder}" BorderThickness="1"
            CornerRadius="8" Padding="16 10" Margin="0 0 0 16">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <StackPanel Grid.Column="0" Orientation="Horizontal" HorizontalAlignment="Center">
          <Ellipse x:Name="wizDot1" Width="20" Height="20"
                   Stroke="{DynamicResource BrushAccentCyan}" StrokeThickness="2"
                   Fill="{DynamicResource BrushAccentCyan}"
                   VerticalAlignment="Center" Margin="0 0 6 0"/>
          <TextBlock x:Name="wizLabel1" Text="1 · Aktion" FontWeight="SemiBold"
                     FontSize="11" Foreground="{DynamicResource BrushTextPrimary}"
                     VerticalAlignment="Center"/>
        </StackPanel>

        <Border Grid.Column="1" Height="2" Width="28" VerticalAlignment="Center"
                Background="{DynamicResource BrushBorder}" Margin="4 0"/>

        <StackPanel Grid.Column="2" Orientation="Horizontal" HorizontalAlignment="Center">
          <Ellipse x:Name="wizDot2" Width="20" Height="20"
                   Stroke="{DynamicResource BrushBorder}" StrokeThickness="2"
                   Fill="Transparent"
                   VerticalAlignment="Center" Margin="0 0 6 0"/>
          <TextBlock x:Name="wizLabel2" Text="2 · Ordner" FontWeight="SemiBold"
                     FontSize="11" Foreground="{DynamicResource BrushTextMuted}"
                     VerticalAlignment="Center"/>
        </StackPanel>

        <Border Grid.Column="3" Height="2" Width="28" VerticalAlignment="Center"
                Background="{DynamicResource BrushBorder}" Margin="4 0"/>

        <StackPanel Grid.Column="4" Orientation="Horizontal" HorizontalAlignment="Center">
          <Ellipse x:Name="wizDot3" Width="20" Height="20"
                   Stroke="{DynamicResource BrushBorder}" StrokeThickness="2"
                   Fill="Transparent"
                   VerticalAlignment="Center" Margin="0 0 6 0"/>
          <TextBlock x:Name="wizLabel3" Text="3 · Check" FontWeight="SemiBold"
                     FontSize="11" Foreground="{DynamicResource BrushTextMuted}"
                     VerticalAlignment="Center"/>
        </StackPanel>
      </Grid>
    </Border>

    <!-- ═══ STEP PANELS (only one visible at a time) ═════════════════════ -->

    <!-- ── STEP 1: Intent Selection ──────────────────────────────────────── -->
    <Grid x:Name="wizStep1" Grid.Row="1" Visibility="Visible">
      <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" MaxWidth="440">
        <TextBlock Text="Was möchtest du tun?" FontSize="22" FontWeight="Bold"
                   Foreground="{DynamicResource BrushAccentCyan}"
                   HorizontalAlignment="Center" Margin="0 0 0 8"/>
        <TextBlock Text="Wähle eine Aktion für den Schnellstart."
                   Foreground="{DynamicResource BrushTextMuted}"
                   HorizontalAlignment="Center" Margin="0 0 0 24"/>

        <Button x:Name="wizIntentCleanup" Margin="0 0 0 10" Padding="16 14"
                HorizontalContentAlignment="Left">
          <StackPanel Orientation="Horizontal">
            <TextBlock Text="🧹" FontSize="20" VerticalAlignment="Center" Margin="0 0 12 0"/>
            <StackPanel>
              <TextBlock Text="Sammlung aufräumen" FontWeight="SemiBold" FontSize="14"
                         Foreground="{DynamicResource BrushTextPrimary}"/>
              <TextBlock Text="Deduplizierung + Konsolen-Sortierung + Junk-Entfernung"
                         Foreground="{DynamicResource BrushTextMuted}" FontSize="11"/>
            </StackPanel>
          </StackPanel>
        </Button>

        <Button x:Name="wizIntentSort" Margin="0 0 0 10" Padding="16 14"
                HorizontalContentAlignment="Left">
          <StackPanel Orientation="Horizontal">
            <TextBlock Text="📂" FontSize="20" VerticalAlignment="Center" Margin="0 0 12 0"/>
            <StackPanel>
              <TextBlock Text="Nur Konsolen sortieren" FontWeight="SemiBold" FontSize="14"
                         Foreground="{DynamicResource BrushTextPrimary}"/>
              <TextBlock Text="ROMs nach Konsole in Unterordner einsortieren"
                         Foreground="{DynamicResource BrushTextMuted}" FontSize="11"/>
            </StackPanel>
          </StackPanel>
        </Button>

        <Button x:Name="wizIntentConvert" Margin="0 0 0 10" Padding="16 14"
                HorizontalContentAlignment="Left">
          <StackPanel Orientation="Horizontal">
            <TextBlock Text="🔄" FontSize="20" VerticalAlignment="Center" Margin="0 0 12 0"/>
            <StackPanel>
              <TextBlock Text="Nur konvertieren" FontWeight="SemiBold" FontSize="14"
                         Foreground="{DynamicResource BrushTextPrimary}"/>
              <TextBlock Text="CUE/BIN → CHD, ISO → RVZ, etc."
                         Foreground="{DynamicResource BrushTextMuted}" FontSize="11"/>
            </StackPanel>
          </StackPanel>
        </Button>
      </StackPanel>
    </Grid>

    <!-- ── STEP 2: Root Folder Selection ─────────────────────────────────── -->
    <Grid x:Name="wizStep2" Grid.Row="1" Visibility="Collapsed">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
        <RowDefinition Height="Auto"/>
      </Grid.RowDefinitions>

      <TextBlock Grid.Row="0" Text="ROM-Ordner auswählen" FontSize="18" FontWeight="Bold"
                 Foreground="{DynamicResource BrushAccentCyan}" Margin="0 0 0 12"/>

      <!-- Drag-Drop Zone + Folder List -->
      <Grid Grid.Row="1">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <Border x:Name="wizDropZone" Grid.Row="0"
                Background="{DynamicResource BrushSurface}"
                BorderBrush="{DynamicResource BrushBorder}" BorderThickness="2"
                CornerRadius="8" Padding="24 20" Margin="0 0 0 12"
                AllowDrop="True">
          <StackPanel HorizontalAlignment="Center">
            <TextBlock Text="📁  Ordner hierher ziehen" FontSize="14"
                       Foreground="{DynamicResource BrushTextMuted}"
                       HorizontalAlignment="Center" Margin="0 0 0 8"/>
            <Button x:Name="wizBtnAddFolder" Content="Ordner hinzufügen …"
                    HorizontalAlignment="Center" Padding="16 6"/>
          </StackPanel>
        </Border>

        <ListBox x:Name="wizFolderList" Grid.Row="1"
                 Background="{DynamicResource BrushSurface}"
                 Foreground="{DynamicResource BrushTextPrimary}"
                 BorderBrush="{DynamicResource BrushBorder}" BorderThickness="1"
                 Padding="4"/>
      </Grid>

      <!-- Trash folder -->
      <Grid Grid.Row="2" Margin="0 12 0 0">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="Papierkorb:" VerticalAlignment="Center"
                   Foreground="{DynamicResource BrushTextMuted}" Margin="0 0 8 0"/>
        <TextBox x:Name="wizTxtTrash" Grid.Column="1" IsReadOnly="True"
                 VerticalAlignment="Center"/>
        <Button x:Name="wizBtnTrash" Grid.Column="2" Content="…" Padding="8 4"
                Margin="6 0 0 0" VerticalAlignment="Center"/>
      </Grid>
    </Grid>

    <!-- ── STEP 3: Preflight Check ───────────────────────────────────────── -->
    <Grid x:Name="wizStep3" Grid.Row="1" Visibility="Collapsed">
      <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" MaxWidth="440">
        <TextBlock Text="Preflight-Check" FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource BrushAccentCyan}"
                   HorizontalAlignment="Center" Margin="0 0 0 16"/>

        <!-- Check items -->
        <Border Background="{DynamicResource BrushSurface}"
                BorderBrush="{DynamicResource BrushBorder}" BorderThickness="1"
                CornerRadius="8" Padding="16" Margin="0 0 0 16">
          <StackPanel>
            <StackPanel Orientation="Horizontal" Margin="0 0 0 10">
              <Ellipse x:Name="wizChkFolders" Width="14" Height="14"
                       Fill="{DynamicResource BrushTextMuted}"
                       VerticalAlignment="Center" Margin="0 0 10 0"/>
              <TextBlock x:Name="wizChkFoldersText" Text="Ordner existieren …"
                         Foreground="{DynamicResource BrushTextPrimary}"
                         VerticalAlignment="Center"/>
            </StackPanel>

            <StackPanel Orientation="Horizontal" Margin="0 0 0 10">
              <Ellipse x:Name="wizChkFiles" Width="14" Height="14"
                       Fill="{DynamicResource BrushTextMuted}"
                       VerticalAlignment="Center" Margin="0 0 10 0"/>
              <TextBlock x:Name="wizChkFilesText" Text="ROMs gefunden …"
                         Foreground="{DynamicResource BrushTextPrimary}"
                         VerticalAlignment="Center"/>
            </StackPanel>

            <StackPanel Orientation="Horizontal" Margin="0 0 0 10">
              <Ellipse x:Name="wizChkTrash" Width="14" Height="14"
                       Fill="{DynamicResource BrushTextMuted}"
                       VerticalAlignment="Center" Margin="0 0 10 0"/>
              <TextBlock x:Name="wizChkTrashText" Text="Papierkorb-Pfad …"
                         Foreground="{DynamicResource BrushTextPrimary}"
                         VerticalAlignment="Center"/>
            </StackPanel>

            <StackPanel Orientation="Horizontal">
              <Ellipse x:Name="wizChkTools" Width="14" Height="14"
                       Fill="{DynamicResource BrushTextMuted}"
                       VerticalAlignment="Center" Margin="0 0 10 0"/>
              <TextBlock x:Name="wizChkToolsText" Text="Externe Tools …"
                         Foreground="{DynamicResource BrushTextPrimary}"
                         VerticalAlignment="Center"/>
            </StackPanel>
          </StackPanel>
        </Border>

        <!-- Overall status -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0 0 0 8">
          <Ellipse x:Name="wizOverallDot" Width="18" Height="18"
                   Fill="{DynamicResource BrushTextMuted}"
                   VerticalAlignment="Center" Margin="0 0 8 0"/>
          <TextBlock x:Name="wizOverallStatus" Text="Prüfung läuft …"
                     FontSize="14" FontWeight="SemiBold"
                     Foreground="{DynamicResource BrushTextPrimary}"
                     VerticalAlignment="Center"/>
        </StackPanel>

        <!-- Warning/error message area -->
        <TextBlock x:Name="wizPreflightMsg" Text="" TextWrapping="Wrap"
                   Foreground="{DynamicResource BrushWarning}"
                   HorizontalAlignment="Center" Margin="0 8 0 0"
                   FontSize="11"/>
      </StackPanel>
    </Grid>

    <!-- ═══ NAVIGATION BAR ═══════════════════════════════════════════════ -->
    <Grid Grid.Row="2" Margin="0 16 0 0">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>

      <Button x:Name="wizBtnSkip" Grid.Column="0" Content="Überspringen"
              Foreground="{DynamicResource BrushTextMuted}" Padding="12 6"/>

      <Button x:Name="wizBtnBack" Grid.Column="2" Content="← Zurück"
              Padding="12 6" Margin="0 0 8 0" Visibility="Collapsed"/>

      <Button x:Name="wizBtnNext" Grid.Column="3" Content="Weiter →"
              Padding="16 8" FontWeight="SemiBold"
              BorderBrush="{DynamicResource BrushAccentCyan}"
              Foreground="{DynamicResource BrushAccentCyan}" IsEnabled="False"/>
    </Grid>
  </Grid>
</Window>
'@

function Show-FirstStartWizard {
  <#
  .SYNOPSIS
    Shows the first-start wizard if no settings file exists.
    Returns a hashtable with wizard results or $null if skipped/cancelled.
  .PARAMETER OwnerWindow
    The main WPF window (for modal parenting).
  .PARAMETER BrowseFolder
    Scriptblock that shows a folder browser dialog.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$OwnerWindow,
    [Parameter(Mandatory)][scriptblock]$BrowseFolder
  )

  $wizardResult = @{
    Completed    = $false
    Intent       = $null      # 'cleanup' | 'sort' | 'convert'
    Roots        = [System.Collections.Generic.List[string]]::new()
    TrashRoot    = $null
    AutoStartDryRun = $false
  }

  # ── Build wizard window ──────────────────────────────────────────────────
  $wizWindow = New-WpfWindowFromXaml -Xaml $script:WPF_WIZARD_XAML
  $wizWindow.Owner = $OwnerWindow

  # Inject theme
  if (Get-Command Add-WpfResourceDictionary -ErrorAction SilentlyContinue) {
    $themeXaml = $null
    if (Get-Command Get-WpfThemeResourceDictionaryXaml -ErrorAction SilentlyContinue) {
      $themeXaml = Get-WpfThemeResourceDictionaryXaml
    } elseif (Get-Variable -Name RC_XAML_THEME -Scope Script -ErrorAction SilentlyContinue) {
      $themeXaml = [string]$script:RC_XAML_THEME
    }
    Add-WpfResourceDictionary -Window $wizWindow -ResourceDictionaryXaml $themeXaml
  }

  $wiz = Get-WpfNamedElements -Window $wizWindow
  $currentStep = 1

  # ── Helper: Update step indicator ────────────────────────────────────────
  $updateStepIndicator = {
    param([int]$Step)

    $dots   = @($wiz['wizDot1'],   $wiz['wizDot2'],   $wiz['wizDot3'])
    $labels = @($wiz['wizLabel1'], $wiz['wizLabel2'], $wiz['wizLabel3'])

    for ($i = 0; $i -lt 3; $i++) {
      $stepNum = $i + 1
      if ($stepNum -lt $Step) {
        # Completed
        $dots[$i].Fill   = $dots[$i].TryFindResource('BrushSuccess')
        $dots[$i].Stroke = $dots[$i].TryFindResource('BrushSuccess')
        $labels[$i].Foreground = $labels[$i].TryFindResource('BrushTextPrimary')
      } elseif ($stepNum -eq $Step) {
        # Current
        $dots[$i].Fill   = $dots[$i].TryFindResource('BrushAccentCyan')
        $dots[$i].Stroke = $dots[$i].TryFindResource('BrushAccentCyan')
        $labels[$i].Foreground = $labels[$i].TryFindResource('BrushTextPrimary')
      } else {
        # Future
        $dots[$i].Fill   = [System.Windows.Media.Brushes]::Transparent
        $dots[$i].Stroke = $dots[$i].TryFindResource('BrushBorder')
        $labels[$i].Foreground = $labels[$i].TryFindResource('BrushTextMuted')
      }
    }
  }

  # ── Helper: Show/hide step panels ────────────────────────────────────────
  $showStep = {
    param([int]$Step)

    $wiz['wizStep1'].Visibility = $(if ($Step -eq 1) { 'Visible' } else { 'Collapsed' })
    $wiz['wizStep2'].Visibility = $(if ($Step -eq 2) { 'Visible' } else { 'Collapsed' })
    $wiz['wizStep3'].Visibility = $(if ($Step -eq 3) { 'Visible' } else { 'Collapsed' })

    $wiz['wizBtnBack'].Visibility = $(if ($Step -gt 1) { 'Visible' } else { 'Collapsed' })

    if ($Step -eq 3) {
      $wiz['wizBtnNext'].Content = 'Fertig ✓'
    } else {
      $wiz['wizBtnNext'].Content = 'Weiter →'
    }

    & $updateStepIndicator $Step
  }

  # ── Helper: Validate current step ────────────────────────────────────────
  $validateStep = {
    switch ($currentStep) {
      1 { return ($null -ne $wizardResult.Intent) }
      2 { return ($wizardResult.Roots.Count -gt 0) }
      3 { return $true }
      default { return $true }
    }
  }

  $updateNextButton = {
    $wiz['wizBtnNext'].IsEnabled = [bool](& $validateStep)
  }

  # ── Helper: Run preflight checks ────────────────────────────────────────
  $runPreflightChecks = {
    $allGreen = $true
    $warnings = @()

    $brushSuccess = $wiz['wizChkFolders'].TryFindResource('BrushSuccess')
    $brushDanger  = $wiz['wizChkFolders'].TryFindResource('BrushDanger')
    $brushWarning = $wiz['wizChkFolders'].TryFindResource('BrushWarning')

    # Check 1: Folders exist
    $foldersOk = $true
    $missingFolders = @()
    foreach ($root in $wizardResult.Roots) {
      if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        $foldersOk = $false
        $missingFolders += $root
      }
    }
    $wiz['wizChkFolders'].Fill = $(if ($foldersOk) { $brushSuccess } else { $brushDanger })
    if ($foldersOk) {
      $wiz['wizChkFoldersText'].Text = "$($wizardResult.Roots.Count) Ordner OK"
    } else {
      $wiz['wizChkFoldersText'].Text = "Ordner nicht gefunden: $($missingFolders -join ', ')"
      $allGreen = $false
    }

    # Check 2: Files found
    $totalFiles = 0
    foreach ($root in $wizardResult.Roots) {
      if (Test-Path -LiteralPath $root -PathType Container) {
        try {
          $count = @(Get-ChildItem -LiteralPath $root -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 500).Count
          $totalFiles += $count
        } catch { }
      }
    }
    if ($totalFiles -gt 0) {
      $wiz['wizChkFiles'].Fill = $brushSuccess
      $displayCount = $(if ($totalFiles -ge 500) { '500+' } else { $totalFiles })
      $wiz['wizChkFilesText'].Text = "$displayCount Dateien gefunden"
    } else {
      $wiz['wizChkFiles'].Fill = $brushDanger
      $wiz['wizChkFilesText'].Text = 'Keine Dateien gefunden'
      $warnings += 'Keine ROMs im angegebenen Ordner gefunden.'
      $allGreen = $false
    }

    # Check 3: Trash path
    $trashPath = [string]$wizardResult.TrashRoot
    if (-not [string]::IsNullOrWhiteSpace($trashPath)) {
      $wiz['wizChkTrash'].Fill = $brushSuccess
      $wiz['wizChkTrashText'].Text = "Papierkorb: $trashPath"
    } else {
      $wiz['wizChkTrash'].Fill = $brushWarning
      $wiz['wizChkTrashText'].Text = 'Kein Papierkorb gewählt (Standard wird verwendet)'
      $warnings += 'Kein expliziter Papierkorb-Pfad. Standard _trash wird genutzt.'
    }

    # Check 4: External tools
    $toolsOk = $true
    $toolMsg = 'Externe Tools OK'
    if (Get-Command Find-ExternalTool -ErrorAction SilentlyContinue) {
      $missingTools = @()
      foreach ($toolName in @('7z')) {
        try {
          $found = Find-ExternalTool -Name $toolName -ErrorAction SilentlyContinue
          if (-not $found) { $missingTools += $toolName }
        } catch { $missingTools += $toolName }
      }
      if ($missingTools.Count -gt 0) {
        $toolsOk = $false
        $toolMsg = "Nicht gefunden: $($missingTools -join ', ')"
      }
    } else {
      $toolMsg = 'Tool-Prüfung übersprungen'
    }
    $wiz['wizChkTools'].Fill = $(if ($toolsOk) { $brushSuccess } else { $brushWarning })
    $wiz['wizChkToolsText'].Text = $toolMsg

    # Overall status
    if ($allGreen) {
      $wiz['wizOverallDot'].Fill = $brushSuccess
      $wiz['wizOverallStatus'].Text = 'Bereit'
      $wiz['wizPreflightMsg'].Text = ''
    } elseif ($foldersOk) {
      $wiz['wizOverallDot'].Fill = $brushWarning
      $wiz['wizOverallStatus'].Text = 'Bereit mit Warnungen'
      $wiz['wizPreflightMsg'].Text = ($warnings -join "`n")
    } else {
      $wiz['wizOverallDot'].Fill = $brushDanger
      $wiz['wizOverallStatus'].Text = 'Blockiert'
      $wiz['wizPreflightMsg'].Text = ($warnings -join "`n")
    }
  }

  # ── Helper: Add folder to list ───────────────────────────────────────────
  $addFolderToList = {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $normalizedPath = [string]$Path
    foreach ($existing in $wizardResult.Roots) {
      if ([string]$existing -ieq $normalizedPath) { return }
    }
    $wizardResult.Roots.Add($normalizedPath)
    $wiz['wizFolderList'].Items.Add($normalizedPath)

    # Auto-set trash if not yet set – MUST be outside root to avoid PREFLIGHT-OVERLAP
    if ([string]::IsNullOrWhiteSpace([string]$wizardResult.TrashRoot)) {
      $parentDir = [System.IO.Path]::GetDirectoryName($normalizedPath)
      $rootName  = [System.IO.Path]::GetFileName($normalizedPath)
      $defaultTrash = [System.IO.Path]::Combine($parentDir, ($rootName + '_trash'))
      $wizardResult.TrashRoot = $defaultTrash
      $wiz['wizTxtTrash'].Text = $defaultTrash
    }

    & $updateNextButton
  }

  # ══════════════════════════════════════════════════════════════════════════
  #  EVENT WIRING
  # ══════════════════════════════════════════════════════════════════════════

  # ── Step 1: Intent buttons ───────────────────────────────────────────────
  $selectIntent = {
    param([string]$Intent)
    $wizardResult.Intent = $Intent
    & $updateNextButton
    # Auto-advance to step 2
    Set-Variable -Name currentStep -Value 2 -Scope 1
    & $showStep 2
    & $updateNextButton
  }

  $wiz['wizIntentCleanup'].add_Click({ & $selectIntent 'cleanup' }.GetNewClosure())
  $wiz['wizIntentSort'].add_Click({ & $selectIntent 'sort' }.GetNewClosure())
  $wiz['wizIntentConvert'].add_Click({ & $selectIntent 'convert' }.GetNewClosure())

  # ── Step 2: Add folder button ────────────────────────────────────────────
  $wiz['wizBtnAddFolder'].add_Click({
    $path = & $BrowseFolder 'ROM-Ordner auswählen' ''
    if (-not [string]::IsNullOrWhiteSpace([string]$path)) {
      & $addFolderToList ([string]$path)
    }
  }.GetNewClosure())

  # ── Step 2: Drag-Drop ───────────────────────────────────────────────────
  $wiz['wizDropZone'].add_DragOver({
    param($dragSource, $e)
    if ($e.Data.GetDataPresent([System.Windows.DataFormats]::FileDrop)) {
      $e.Effects = [System.Windows.DragDropEffects]::Link
    } else {
      $e.Effects = [System.Windows.DragDropEffects]::None
    }
    $e.Handled = $true
  }.GetNewClosure())

  $wiz['wizDropZone'].add_Drop({
    param($dropSource, $e)
    if ($e.Data.GetDataPresent([System.Windows.DataFormats]::FileDrop)) {
      $paths = $e.Data.GetData([System.Windows.DataFormats]::FileDrop)
      foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p -PathType Container) {
          & $addFolderToList ([string]$p)
        }
      }
    }
    $e.Handled = $true
  }.GetNewClosure())

  # ── Step 2: Trash folder browse ──────────────────────────────────────────
  $wiz['wizBtnTrash'].add_Click({
    $initial = [string]$wiz['wizTxtTrash'].Text
    $path = & $BrowseFolder 'Papierkorb-Verzeichnis auswählen' $initial
    if (-not [string]::IsNullOrWhiteSpace([string]$path)) {
      $wizardResult.TrashRoot = [string]$path
      $wiz['wizTxtTrash'].Text = [string]$path
    }
  }.GetNewClosure())

  # ── Navigation: Back ─────────────────────────────────────────────────────
  $wiz['wizBtnBack'].add_Click({
    if ($currentStep -gt 1) {
      Set-Variable -Name currentStep -Value ($currentStep - 1) -Scope 1
      & $showStep $currentStep
      & $updateNextButton
    }
  }.GetNewClosure())

  # ── Navigation: Next / Finish ────────────────────────────────────────────
  $wiz['wizBtnNext'].add_Click({
    if ($currentStep -lt 3) {
      Set-Variable -Name currentStep -Value ($currentStep + 1) -Scope 1
      & $showStep $currentStep

      # Run preflight when entering step 3
      if ($currentStep -eq 3) {
        & $runPreflightChecks
      }
      & $updateNextButton
    } else {
      # Step 3 → Finish
      $wizardResult.Completed = $true
      $wizardResult.AutoStartDryRun = $true
      $wizWindow.DialogResult = $true
      $wizWindow.Close()
    }
  }.GetNewClosure())

  # ── Navigation: Skip ─────────────────────────────────────────────────────
  $wiz['wizBtnSkip'].add_Click({
    $wizWindow.DialogResult = $false
    $wizWindow.Close()
  }.GetNewClosure())

  # ── Initialize step 1 ───────────────────────────────────────────────────
  & $showStep 1
  & $updateNextButton

  # ── Show modal ───────────────────────────────────────────────────────────
  $dialogResult = $wizWindow.ShowDialog()

  if ($dialogResult -eq $true -and $wizardResult.Completed) {
    return $wizardResult
  }
  return $null
}

function Invoke-WpfFirstStartWizard {
  <#
  .SYNOPSIS
    Checks if first-start wizard should run and shows it.
    Applies wizard results to the main window context.
  .PARAMETER Window
    The main WPF window.
  .PARAMETER Ctx
    The main window's named-element context hashtable.
  .PARAMETER BrowseFolder
    Scriptblock for folder browser dialogs.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$BrowseFolder
  )

  # ── Skip conditions (align with Invoke-WpfQuickOnboarding) ──────────────
  try {
    $skipFlagRaw = [string]$env:ROM_CLEANUP_SKIP_ONBOARDING
    if (-not [string]::IsNullOrWhiteSpace($skipFlagRaw)) {
      if (@('1','true','yes','on') -contains $skipFlagRaw.Trim().ToLowerInvariant()) { return }
    }
  } catch { }

  try {
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      if ([bool](Get-AppStateValue -Key 'SkipOnboardingWizard' -Default $false)) { return }
    }
  } catch { }

  try {
    if (Get-Command Test-RomCleanupAutomatedTestMode -ErrorAction SilentlyContinue) {
      if ([bool](Test-RomCleanupAutomatedTestMode)) { return }
    }
  } catch { }

  # ── Check if first start (no settings file) ─────────────────────────────
  $isFirstStart = $false
  if (Get-Command Get-UserSettings -ErrorAction SilentlyContinue) {
    $existingSettings = Get-UserSettings
    $isFirstStart = ($null -eq $existingSettings)
  } else {
    $isFirstStart = $true
  }

  # Also check if roots/trash already configured
  $vm = $null
  if (Get-Command Get-WpfViewModel -ErrorAction SilentlyContinue) {
    $vm = Get-WpfViewModel -Ctx $Ctx
  }
  $rootList = @()
  if ($vm -and $vm.Roots) {
    $rootList = @($vm.Roots)
  } elseif ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
    $rootList = @($Ctx['listRoots'].Items)
  }
  $hasRoots = $rootList.Count -gt 0
  $hasTrash = -not [string]::IsNullOrWhiteSpace([string]$Ctx['txtTrash'].Text)

  if (-not $isFirstStart -and $hasRoots -and $hasTrash) { return }

  # ── Show wizard ──────────────────────────────────────────────────────────
  $result = Show-FirstStartWizard -OwnerWindow $Window -BrowseFolder $BrowseFolder
  if ($null -eq $result) { return }

  # ── Apply wizard results to main window (v3 – self-contained) ─────────────
  # Guarantee a working roots collection exists. Create fresh if needed.
  $rootsCollection = $Ctx['__rootsCollection']
  if (-not $rootsCollection -and $vm -and $vm.Roots) {
    $rootsCollection = $vm.Roots
  }
  if (-not $rootsCollection) {
    $rootsCollection = New-Object 'System.Collections.ObjectModel.ObservableCollection[string]'
  }
  # Always store and bind – ensures listRoots and StatusBar see the collection
  $Ctx['__rootsCollection'] = $rootsCollection
  if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
    try { $Ctx['listRoots'].ItemsSource = $rootsCollection } catch { }
  }

  # Add wizard roots to collection
  foreach ($root in $result.Roots) {
    $normalized = ([string]$root).Trim()
    $duplicate = $false
    foreach ($existing in @($rootsCollection)) {
      if ([string]$existing -ieq $normalized) { $duplicate = $true; break }
    }
    if (-not $duplicate) { [void]$rootsCollection.Add($normalized) }
  }

  # Sync ViewModel roots if needed
  if (Get-Command Sync-WpfViewModelRootsFromControl -ErrorAction SilentlyContinue) {
    try { Sync-WpfViewModelRootsFromControl -Ctx $Ctx } catch { }
  }

  # Log result (v3 marker)
  if (Get-Command Add-WpfLogLine -ErrorAction SilentlyContinue) {
    Add-WpfLogLine -Ctx $Ctx -Line ("WIZ-v3: roots={0}, listItems={1}" -f $rootsCollection.Count, $(if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) { $Ctx['listRoots'].Items.Count } else { -1 })) -Level 'DEBUG'
  }

  # Set trash
  if (-not [string]::IsNullOrWhiteSpace([string]$result.TrashRoot)) {
    if (Get-Command Set-WpfViewModelProperty -ErrorAction SilentlyContinue) {
      if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'TrashRoot' -Value ([string]$result.TrashRoot))) {
        $Ctx['txtTrash'].Text = [string]$result.TrashRoot
      }
    } else {
      $Ctx['txtTrash'].Text = [string]$result.TrashRoot
    }
  }

  # Apply intent-based defaults
  switch ($result.Intent) {
    'cleanup' {
      # Full cleanup: DryRun + SortConsole + AliasKeying + Dedupe
      if (Get-Command Set-WpfViewModelProperty -ErrorAction SilentlyContinue) {
        Set-WpfViewModelProperty -Ctx $Ctx -Name 'DryRun' -Value $true | Out-Null
        Set-WpfViewModelProperty -Ctx $Ctx -Name 'SortConsole' -Value $true | Out-Null
        Set-WpfViewModelProperty -Ctx $Ctx -Name 'AliasKeying' -Value $true | Out-Null
      } else {
        if ($Ctx.ContainsKey('chkReportDryRun')) { $Ctx['chkReportDryRun'].IsChecked = $true }
        if ($Ctx.ContainsKey('chkSortConsole')) { $Ctx['chkSortConsole'].IsChecked = $true }
        if ($Ctx.ContainsKey('chkAliasKeying')) { $Ctx['chkAliasKeying'].IsChecked = $true }
      }
    }
    'sort' {
      # Sort only: DryRun + SortConsole, no dedupe
      if (Get-Command Set-WpfViewModelProperty -ErrorAction SilentlyContinue) {
        Set-WpfViewModelProperty -Ctx $Ctx -Name 'DryRun' -Value $true | Out-Null
        Set-WpfViewModelProperty -Ctx $Ctx -Name 'SortConsole' -Value $true | Out-Null
      } else {
        if ($Ctx.ContainsKey('chkReportDryRun')) { $Ctx['chkReportDryRun'].IsChecked = $true }
        if ($Ctx.ContainsKey('chkSortConsole')) { $Ctx['chkSortConsole'].IsChecked = $true }
      }
    }
    'convert' {
      # Convert only: DryRun enabled
      if (Get-Command Set-WpfViewModelProperty -ErrorAction SilentlyContinue) {
        Set-WpfViewModelProperty -Ctx $Ctx -Name 'DryRun' -Value $true | Out-Null
      } else {
        if ($Ctx.ContainsKey('chkReportDryRun')) { $Ctx['chkReportDryRun'].IsChecked = $true }
      }
    }
  }

  # Update status bar and step indicator
  if (Get-Command Update-WpfStatusBar -ErrorAction SilentlyContinue) {
    Update-WpfStatusBar -Ctx $Ctx
  }
  if (Get-Command Update-WpfStepIndicator -ErrorAction SilentlyContinue) {
    Update-WpfStepIndicator -Ctx $Ctx
  }

  # Persist roots so they survive restart
  if (Get-Command Save-WpfToSettings -ErrorAction SilentlyContinue) {
    try { Save-WpfToSettings -Ctx $Ctx } catch { }
  }

  # Log completion
  if (Get-Command Add-WpfLogLine -ErrorAction SilentlyContinue) {
    $intentLabel = switch ($result.Intent) {
      'cleanup' { 'Sammlung aufräumen' }
      'sort'    { 'Konsolen sortieren' }
      'convert' { 'Konvertieren' }
      default   { $result.Intent }
    }
    Add-WpfLogLine -Ctx $Ctx -Line ("Wizard abgeschlossen: {0}, {1} Ordner konfiguriert." -f $intentLabel, $result.Roots.Count) -Level 'INFO'
  }

  # Mark onboarding as done (prevent re-show next time)
  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    try { Set-AppStateValue -Key 'SkipOnboardingWizard' -Value $true } catch { }
  }

  # Auto-start DryRun if wizard requested it
  if ($result.AutoStartDryRun) {
    if ($Ctx.ContainsKey('btnRunGlobal') -and $Ctx['btnRunGlobal'] -and $Ctx['btnRunGlobal'].IsEnabled) {
      Add-WpfLogLine -Ctx $Ctx -Line 'Wizard: Starte automatischen DryRun...' -Level 'INFO'
      $Ctx['btnRunGlobal'].RaiseEvent(
        [System.Windows.RoutedEventArgs]::new([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)
      )
    }
  }
}
