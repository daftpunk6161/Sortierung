using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using RomCleanup.UI.Wpf.Models;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // ═══ EXTENSION FILTERS (UX-004: VM-bound, replaces code-behind x:Name checkboxes) ═══
    public ObservableCollection<ExtensionFilterItem> ExtensionFilters { get; } = [];

    /// <summary>Grouped view for XAML binding with category headers.
    /// V2-WPF-L01: Initialized via InitExtensionFilters() called from constructor — null! is safe.</summary>
    public ICollectionView ExtensionFiltersView { get; private set; } = null!;

    /// <summary>Returns checked extensions, or empty array if none selected (= scan all).</summary>
    public string[] GetSelectedExtensions() =>
        ExtensionFilters.Where(e => e.IsChecked).Select(e => e.Extension).ToArray();

    private void InitExtensionFilters()
    {
        var items = new (string ext, string cat, string tip)[]
        {
            (".chd", "Disc-Images", "CHD Disk-Image"),
            (".iso", "Disc-Images", "ISO-Abbild"),
            (".cue", "Disc-Images", "CUE Steuerdatei"),
            (".gdi", "Disc-Images", "GDI (Dreamcast)"),
            (".img", "Disc-Images", "IMG Disk-Image"),
            (".bin", "Disc-Images", "BIN (CD-Image)"),
            (".cso", "Disc-Images", "Compressed ISO (PSP)"),
            (".pbp", "Disc-Images", "PBP-Paket (PSP)"),
            (".zip", "Archive", "ZIP-Archiv"),
            (".7z",  "Archive", "7-Zip-Archiv"),
            (".rar", "Archive", "RAR-Archiv"),
            (".nes", "Cartridge / Modern", "NES ROM"),
            (".gba", "Cartridge / Modern", "Game Boy Advance ROM"),
            (".nds", "Cartridge / Modern", "Nintendo DS ROM"),
            (".nsp", "Cartridge / Modern", "NSP (Nintendo Switch)"),
            (".xci", "Cartridge / Modern", "XCI Cartridge-Image"),
            (".wbfs","Cartridge / Modern", "WBFS (Wii Backup)"),
            (".rvz", "Cartridge / Modern", "RVZ (GC/Wii, Dolphin)"),
        };
        foreach (var (ext, cat, tip) in items)
            ExtensionFilters.Add(new ExtensionFilterItem { Extension = ext, Category = cat, ToolTip = tip });

        ExtensionFiltersView = CollectionViewSource.GetDefaultView(ExtensionFilters);
        ExtensionFiltersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExtensionFilterItem.Category)));
    }

    // ═══ CONSOLE FILTERS (VM-bound, replaces code-behind x:Name checkboxes) ═══
    public ObservableCollection<ConsoleFilterItem> ConsoleFilters { get; } = [];

    /// <summary>Grouped view for XAML binding with category headers (Sony, Nintendo, Sega, Andere).</summary>
    public ICollectionView ConsoleFiltersView { get; private set; } = null!;

    /// <summary>Returns checked console keys, or empty array if none selected (= all consoles).</summary>
    public string[] GetSelectedConsoles() =>
        ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToArray();

    private void InitConsoleFilters()
    {
        var items = new (string key, string display, string cat)[]
        {
            ("PS1",    "PlayStation",               "Sony"),
            ("PS2",    "PlayStation 2",             "Sony"),
            ("PS3",    "PlayStation 3",             "Sony"),
            ("PSP",    "PSP",                       "Sony"),
            ("NES",    "NES / Famicom",             "Nintendo"),
            ("SNES",   "SNES / Super Famicom",      "Nintendo"),
            ("N64",    "Nintendo 64",               "Nintendo"),
            ("GC",     "GameCube",                  "Nintendo"),
            ("WII",    "Wii",                       "Nintendo"),
            ("WIIU",   "Wii U",                     "Nintendo"),
            ("SWITCH", "Nintendo Switch",           "Nintendo"),
            ("GB",     "Game Boy",                  "Nintendo"),
            ("GBC",    "Game Boy Color",            "Nintendo"),
            ("GBA",    "Game Boy Advance",          "Nintendo"),
            ("NDS",    "Nintendo DS",               "Nintendo"),
            ("3DS",    "Nintendo 3DS",              "Nintendo"),
            ("MD",     "Mega Drive / Genesis",      "Sega"),
            ("SCD",    "Mega-CD / Sega CD",         "Sega"),
            ("SAT",    "Saturn",                    "Sega"),
            ("DC",     "Dreamcast",                 "Sega"),
            ("SMS",    "Master System",             "Sega"),
            ("GG",     "Game Gear",                 "Sega"),
            ("ARCADE", "Arcade / MAME / FBNeo",     "Andere"),
            ("NEOGEO", "Neo Geo",                   "Andere"),
            ("NEOCD",  "Neo Geo CD",                "Andere"),
            ("PCE",    "PC Engine / TurboGrafx-16", "Andere"),
            ("PCECD",  "PC Engine CD",              "Andere"),
            ("DOS",    "DOS / PC",                  "Andere"),
            ("3DO",    "3DO",                       "Andere"),
            ("JAG",    "Atari Jaguar",              "Andere"),
        };
        foreach (var (key, display, cat) in items)
            ConsoleFilters.Add(new ConsoleFilterItem { Key = key, DisplayName = display, Category = cat });

        ConsoleFiltersView = CollectionViewSource.GetDefaultView(ConsoleFilters);
        ConsoleFiltersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConsoleFilterItem.Category)));
        ConsoleFiltersView.Filter = FilterConsoleItem;
    }

    // GUI-087: Console filter search text
    private string _consoleFilterText = string.Empty;
    public string ConsoleFilterText
    {
        get => _consoleFilterText;
        set
        {
            if (_consoleFilterText == value) return;
            _consoleFilterText = value;
            OnPropertyChanged();
            ConsoleFiltersView.Refresh();
        }
    }

    private bool FilterConsoleItem(object obj)
    {
        if (string.IsNullOrWhiteSpace(_consoleFilterText)) return true;
        if (obj is not ConsoleFilterItem item) return false;
        return item.DisplayName.Contains(_consoleFilterText, StringComparison.OrdinalIgnoreCase)
            || item.Key.Contains(_consoleFilterText, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ TOOL ITEMS (delegated to ToolsViewModel) ══════════════════════
    public ObservableCollection<ToolItem> ToolItems => Tools.ToolItems;
    public ICollectionView ToolItemsView => Tools.ToolItemsView;
    public ObservableCollection<ToolCategory> ToolCategories => Tools.ToolCategories;
    public ObservableCollection<ToolItem> QuickAccessItems => Tools.QuickAccessItems;
    public ObservableCollection<ToolItem> RecentToolItems => Tools.RecentToolItems;

    public bool IsToolSearchActive => Tools.IsToolSearchActive;

    public string ToolFilterText
    {
        get => Tools.ToolFilterText;
        set
        {
            if (Tools.ToolFilterText == value)
                return;

            Tools.ToolFilterText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsToolSearchActive));
        }
    }

    public bool HasRecentTools => Tools.HasRecentTools;

    private void InitToolItems()
    {
        // Intentionally empty: ToolsViewModel initializes tool catalog in its constructor.
    }

    public void RecordToolUsage(string toolKey)
    {
        Tools.RecordToolUsage(toolKey);
        OnPropertyChanged(nameof(HasRecentTools));
    }

    public void ToggleToolPin(string toolKey) => Tools.ToggleToolPin(toolKey);

    public void RefreshToolLockState() => Tools.RefreshToolLockState(HasRunResult);
}
