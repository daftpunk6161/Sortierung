function Initialize-WpfMainViewModelType {
  if ('RomCleanup.Wpf.MainViewModel' -as [type]) { return }

  Add-Type -TypeDefinition @"
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RomCleanup.Wpf {
  public sealed class MainViewModel : INotifyPropertyChanged {
    public event PropertyChangedEventHandler PropertyChanged;

    private string _trashRoot = string.Empty;
    private string _datRoot = string.Empty;
    private string _auditRoot = string.Empty;
    private string _ps3DupesRoot = string.Empty;
    private string _toolChdman = string.Empty;
    private string _toolDolphin = string.Empty;
    private string _tool7z = string.Empty;
    private string _toolPsxtract = string.Empty;
    private string _toolCiso = string.Empty;
    private bool _sortConsole = true;
    private bool _aliasKeying;
    private bool _useDat;
    private bool _datFallback;
    private bool _dryRun = true;
    private bool _convertEnabled;
    private bool _confirmMove;
    private bool _aggressiveJunk;
    private bool _crcVerifyScan;
    private bool _crcVerifyDat;
    private bool _safetyStrict;
    private bool _safetyPrompts;
    private bool _jpOnlySelected;
    private string _protectedPaths = string.Empty;
    private string _safetySandbox = string.Empty;
    private string _jpKeepConsoles = string.Empty;
    private string _logLevel = string.Empty;
    private string _locale = string.Empty;
    private string _datHashType = string.Empty;
    private readonly ObservableCollection<string> _roots;

    public MainViewModel() {
      _roots = new ObservableCollection<string>();
    }

    public ObservableCollection<string> Roots {
      get { return _roots; }
    }

    private void SetString(ref string field, string value, string propertyName) {
      var v = value ?? string.Empty;
      if (StringComparer.Ordinal.Equals(field, v)) { return; }
      field = v;
      OnPropertyChanged(propertyName);
    }

    private void SetBool(ref bool field, bool value, string propertyName) {
      if (field == value) { return; }
      field = value;
      OnPropertyChanged(propertyName);
    }

    public string TrashRoot {
      get { return _trashRoot; }
      set { SetString(ref _trashRoot, value, "TrashRoot"); }
    }

    public string DatRoot {
      get { return _datRoot; }
      set { SetString(ref _datRoot, value, "DatRoot"); }
    }

    public string AuditRoot {
      get { return _auditRoot; }
      set { SetString(ref _auditRoot, value, "AuditRoot"); }
    }

    public string Ps3DupesRoot {
      get { return _ps3DupesRoot; }
      set { SetString(ref _ps3DupesRoot, value, "Ps3DupesRoot"); }
    }

    public string ToolChdman {
      get { return _toolChdman; }
      set { SetString(ref _toolChdman, value, "ToolChdman"); }
    }

    public string ToolDolphin {
      get { return _toolDolphin; }
      set { SetString(ref _toolDolphin, value, "ToolDolphin"); }
    }

    public string Tool7z {
      get { return _tool7z; }
      set { SetString(ref _tool7z, value, "Tool7z"); }
    }

    public string ToolPsxtract {
      get { return _toolPsxtract; }
      set { SetString(ref _toolPsxtract, value, "ToolPsxtract"); }
    }

    public string ToolCiso {
      get { return _toolCiso; }
      set { SetString(ref _toolCiso, value, "ToolCiso"); }
    }

    public bool SortConsole {
      get { return _sortConsole; }
      set { SetBool(ref _sortConsole, value, "SortConsole"); }
    }

    public bool AliasKeying {
      get { return _aliasKeying; }
      set { SetBool(ref _aliasKeying, value, "AliasKeying"); }
    }

    public bool UseDat {
      get { return _useDat; }
      set { SetBool(ref _useDat, value, "UseDat"); }
    }

    public bool DatFallback {
      get { return _datFallback; }
      set { SetBool(ref _datFallback, value, "DatFallback"); }
    }

    public bool DryRun {
      get { return _dryRun; }
      set { SetBool(ref _dryRun, value, "DryRun"); }
    }

    public bool ConvertEnabled {
      get { return _convertEnabled; }
      set { SetBool(ref _convertEnabled, value, "ConvertEnabled"); }
    }

    public bool ConfirmMove {
      get { return _confirmMove; }
      set { SetBool(ref _confirmMove, value, "ConfirmMove"); }
    }

    public bool AggressiveJunk {
      get { return _aggressiveJunk; }
      set { SetBool(ref _aggressiveJunk, value, "AggressiveJunk"); }
    }

    public bool CrcVerifyScan {
      get { return _crcVerifyScan; }
      set { SetBool(ref _crcVerifyScan, value, "CrcVerifyScan"); }
    }

    public bool CrcVerifyDat {
      get { return _crcVerifyDat; }
      set { SetBool(ref _crcVerifyDat, value, "CrcVerifyDat"); }
    }

    public bool SafetyStrict {
      get { return _safetyStrict; }
      set { SetBool(ref _safetyStrict, value, "SafetyStrict"); }
    }

    public bool SafetyPrompts {
      get { return _safetyPrompts; }
      set { SetBool(ref _safetyPrompts, value, "SafetyPrompts"); }
    }

    public bool JpOnlySelected {
      get { return _jpOnlySelected; }
      set { SetBool(ref _jpOnlySelected, value, "JpOnlySelected"); }
    }

    public string ProtectedPaths {
      get { return _protectedPaths; }
      set { SetString(ref _protectedPaths, value, "ProtectedPaths"); }
    }

    public string SafetySandbox {
      get { return _safetySandbox; }
      set { SetString(ref _safetySandbox, value, "SafetySandbox"); }
    }

    public string JpKeepConsoles {
      get { return _jpKeepConsoles; }
      set { SetString(ref _jpKeepConsoles, value, "JpKeepConsoles"); }
    }

    public string LogLevel {
      get { return _logLevel; }
      set { SetString(ref _logLevel, value, "LogLevel"); }
    }

    public string Locale {
      get { return _locale; }
      set { SetString(ref _locale, value, "Locale"); }
    }

    public string DatHashType {
      get { return _datHashType; }
      set { SetString(ref _datHashType, value, "DatHashType"); }
    }

    private void OnPropertyChanged(string propertyName) {
      var handler = PropertyChanged;
      if (handler != null) {
        handler(this, new PropertyChangedEventArgs(propertyName));
      }
    }
  }
}
"@ -Language CSharp -ErrorAction Stop

}

function New-WpfMainViewModel {
  Initialize-WpfMainViewModelType
  return [RomCleanup.Wpf.MainViewModel]::new()
}

function Get-WpfViewModel {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if ($Ctx.ContainsKey('__vm') -and $Ctx['__vm']) {
    return $Ctx['__vm']
  }
  return $null
}

function Set-WpfViewModel {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)]$ViewModel
  )

  $Ctx['__vm'] = $ViewModel
}

function Set-WpfViewModelProperty {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][string]$Name,
    [Parameter()][AllowNull()]$Value
  )

  $vm = Get-WpfViewModel -Ctx $Ctx
  if (-not $vm) { return $false }

  $prop = $vm.PSObject.Properties[$Name]
  if (-not $prop) { return $false }

  try {
    $vm.$Name = $Value
    return $true
  } catch {
    return $false
  }
}

function Connect-WpfMainViewModelBindings {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  $vm = Get-WpfViewModel -Ctx $Ctx
  if (-not $vm) {
    $vm = New-WpfMainViewModel
    Set-WpfViewModel -Ctx $Ctx -ViewModel $vm
  }

  $Window.DataContext = $vm

  $bindText = {
    param([string]$ControlName, [string]$PropertyName)
    if (-not $Ctx.ContainsKey($ControlName)) { return }
    $ctrl = $Ctx[$ControlName]
    if (-not $ctrl) { return }

    $binding = [System.Windows.Data.Binding]::new($PropertyName)
    $binding.Mode = [System.Windows.Data.BindingMode]::TwoWay
    $binding.UpdateSourceTrigger = [System.Windows.Data.UpdateSourceTrigger]::PropertyChanged
    [System.Windows.Data.BindingOperations]::SetBinding($ctrl, [System.Windows.Controls.TextBox]::TextProperty, $binding) | Out-Null
  }

  $bindBool = {
    param([string]$ControlName, [string]$PropertyName)
    if (-not $Ctx.ContainsKey($ControlName)) { return }
    $ctrl = $Ctx[$ControlName]
    if (-not $ctrl) { return }

    $binding = [System.Windows.Data.Binding]::new($PropertyName)
    $binding.Mode = [System.Windows.Data.BindingMode]::TwoWay
    $binding.UpdateSourceTrigger = [System.Windows.Data.UpdateSourceTrigger]::PropertyChanged
    [System.Windows.Data.BindingOperations]::SetBinding($ctrl, [System.Windows.Controls.Primitives.ToggleButton]::IsCheckedProperty, $binding) | Out-Null
  }

  & $bindText 'txtTrash' 'TrashRoot'
  & $bindText 'txtDatRoot' 'DatRoot'
  & $bindText 'txtAuditRoot' 'AuditRoot'
  & $bindText 'txtPs3Dupes' 'Ps3DupesRoot'
  & $bindText 'txtChdman' 'ToolChdman'
  & $bindText 'txtDolphin' 'ToolDolphin'
  & $bindText 'txt7z' 'Tool7z'
  & $bindText 'txtPsxtract' 'ToolPsxtract'
  & $bindText 'txtCiso' 'ToolCiso'
  & $bindText 'txtSafetyScope' 'ProtectedPaths'
  & $bindText 'txtJpKeepConsoles' 'JpKeepConsoles'

  & $bindBool 'chkSortConsole' 'SortConsole'
  & $bindBool 'chkAliasKeying' 'AliasKeying'
  & $bindBool 'chkDatUse' 'UseDat'
  & $bindBool 'chkDatFallback' 'DatFallback'
  & $bindBool 'chkReportDryRun' 'DryRun'
  & $bindBool 'chkConvert' 'ConvertEnabled'
  & $bindBool 'chkConfirmMove' 'ConfirmMove'
  & $bindBool 'chkJunkAggressive' 'AggressiveJunk'
  & $bindBool 'chkCrcVerifyScan' 'CrcVerifyScan'
  & $bindBool 'chkSafetyMode' 'SafetyStrict'
  & $bindBool 'chkJpOnlySelected' 'JpOnlySelected'

  if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
    $listBinding = [System.Windows.Data.Binding]::new('Roots')
    $listBinding.Mode = [System.Windows.Data.BindingMode]::OneWay
    [System.Windows.Data.BindingOperations]::SetBinding($Ctx['listRoots'], [System.Windows.Controls.ItemsControl]::ItemsSourceProperty, $listBinding) | Out-Null
  }

  return $vm
}

function Sync-WpfViewModelRootsFromControl {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $vm = Get-WpfViewModel -Ctx $Ctx
  if (-not $vm) { return }
  if (-not $Ctx.ContainsKey('listRoots') -or -not $Ctx['listRoots']) { return }

  $currentRoots = New-Object System.Collections.Generic.List[string]
  foreach ($item in $Ctx['listRoots'].Items) {
    $s = if ($item -is [string]) { [string]$item } else { [string]$item.Content }
    if (-not [string]::IsNullOrWhiteSpace($s)) {
      [void]$currentRoots.Add($s.Trim())
    }
  }

  $vm.Roots.Clear()
  foreach ($root in $currentRoots) {
    $vm.Roots.Add([string]$root)
  }
}
