# ================================================================
#  WpfXaml.ps1  –  XAML loader for ROM Cleanup WPF GUI
#  Exposes: $script:RC_XAML_MAIN  (full MainWindow XAML from external file)
#  Theme: Synthwave Dark  (#0D0D1F base, Cyan/Purple neons)
#  Note: Inline XAML is a minimal skeleton fallback only.
#        The full UI is loaded from dev/modules/wpf/MainWindow.xaml
# ================================================================

# Minimal skeleton fallback -- only used when external MainWindow.xaml is missing
$script:RC_XAML_MAIN = @'
<Window
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="ROM Cleanup"
    Width="960" Height="720"
    MinWidth="720" MinHeight="520"
    WindowStartupLocation="CenterScreen"
    FontFamily="Segoe UI" FontSize="13"
    Background="#0D0D1F" Foreground="#E8E8F8">
  <Grid>
    <TextBlock Text="Lade externe XAML-Datei..."
               HorizontalAlignment="Center" VerticalAlignment="Center"
               FontSize="18" Foreground="#9999CC"/>
  </Grid>
</Window>
'@

function Resolve-WpfAssetPath {
  param([Parameter(Mandatory)][string]$FileName)

  $baseDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
  return (Join-Path (Join-Path $baseDir 'wpf') $FileName)
}

function Resolve-WpfComponentAssetPath {
  param(
    [Parameter(Mandatory)][string]$Category,
    [Parameter(Mandatory)][string]$FileName
  )

  $baseDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
  return (Join-Path (Join-Path (Join-Path $baseDir 'wpf') $Category) $FileName)
}

function Get-WpfMainWindowXaml {
  $path = Resolve-WpfComponentAssetPath -Category 'Views' -FileName 'MainWindow.xaml'
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    $path = Resolve-WpfAssetPath -FileName 'MainWindow.xaml'
  }
  if (Test-Path -LiteralPath $path -PathType Leaf) {
    $mainXaml = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $themeXaml = Get-WpfThemeResourceDictionaryXaml
    if (-not [string]::IsNullOrWhiteSpace($themeXaml)) {
      $match = [regex]::Match($themeXaml, '(?s)<ResourceDictionary[^>]*>(.*)</ResourceDictionary>')
      if ($match.Success) {
        $resourceInner = [string]$match.Groups[1].Value
        if ($mainXaml -notmatch '<Window\.Resources>') {
          # No existing resources -- inject a new block after the Window tag
          $mainXaml = [regex]::Replace(
            $mainXaml,
            '(?s)(<Window\b[^>]*>)',
            ('$1' + [Environment]::NewLine + '  <Window.Resources>' + [Environment]::NewLine + $resourceInner + [Environment]::NewLine + '  </Window.Resources>'),
            1
          )
        } else {
          # Existing resources -- prepend theme content inside the existing block
          $mainXaml = [regex]::Replace(
            $mainXaml,
            '(?s)(<Window\.Resources>\s*)',
            ('$1' + [Environment]::NewLine + $resourceInner + [Environment]::NewLine),
            1
          )
        }
      }
    }
    return $mainXaml
  }

  # TD-004: XAML Fallback Policy
  $fallbackMode = 'strict' # default: strict (external MainWindow.xaml is required)
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      $configuredMode = [string](Get-AppStateValue -Key 'XamlFallbackMode' -Default 'strict')
      if ($configuredMode -in @('strict','warn','allow')) { $fallbackMode = $configuredMode }
    } catch { }
  }
  if ([System.Environment]::GetEnvironmentVariable('ROMCLEANUP_XAML_FALLBACK')) {
    $envMode = [string][System.Environment]::GetEnvironmentVariable('ROMCLEANUP_XAML_FALLBACK')
    if ($envMode -in @('strict','warn','allow')) { $fallbackMode = $envMode }
  }

  switch ($fallbackMode) {
    'strict' {
      throw 'External XAML file not found and XamlFallbackMode=strict. Cannot start UI without MainWindow.xaml.'
    }
    'warn' {
      Write-Warning '[WpfXaml] External MainWindow.xaml not found – using inline fallback. Set XamlFallbackMode=strict to enforce external file.'
    }
    # 'allow' – silent fallback
  }

  return [string]$script:RC_XAML_MAIN
}

function Get-WpfThemeResourceDictionaryXaml {
  $themeName = 'SynthwaveDark.xaml'
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      $themeSetting = [string](Get-AppStateValue -Key 'UITheme' -Default 'dark')
      if ($themeSetting -and $themeSetting.Equals('light', [StringComparison]::OrdinalIgnoreCase)) {
        $themeName = 'Light.xaml'
      }
    } catch { }
  }

  $path = Resolve-WpfComponentAssetPath -Category 'Themes' -FileName $themeName
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    # SynthwaveDark.xaml is identical to Theme.Resources.xaml — use fallback directly
    $path = Resolve-WpfAssetPath -FileName 'Theme.Resources.xaml'
  }
  if (Test-Path -LiteralPath $path -PathType Leaf) {
    return (Get-Content -LiteralPath $path -Raw -Encoding UTF8)
  }
  return $null
}

$externalMainWindowXaml = Get-WpfMainWindowXaml
if (-not [string]::IsNullOrWhiteSpace($externalMainWindowXaml)) {
  $script:RC_XAML_MAIN = $externalMainWindowXaml
}

$script:RC_XAML_THEME = Get-WpfThemeResourceDictionaryXaml
