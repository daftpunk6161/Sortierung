namespace Romulus.UI.Wpf.Models;

/// <summary>
/// How to handle file name collisions during Move operations.
/// Replaces the unsafe YesNoCancel dialog mapping (UX-007).
/// </summary>
public enum ConflictPolicy
{
    /// <summary>Append _1, _2 etc. suffix (safest default).</summary>
    Rename,
    /// <summary>Skip conflicting files.</summary>
    Skip,
    /// <summary>Overwrite existing files (dangerous).</summary>
    Overwrite
}
