namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// T-W1-LAYOUT-P8: Identifies which Shell-owned modal layer wants focus.
/// CommandPalette lebt im MainViewModel und subscribed auf <see cref="ShellViewModel.ModalOpening"/>.
/// </summary>
public enum ShellModalLayer
{
    None,
    FirstRunWizard,
    ShortcutSheet,
    CommandPalette
}
