namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// TASK-121: Immutable snapshot of active UI banner visibility state.
/// </summary>
public sealed record BannerProjection(
    bool ShowDryRunBanner,
    bool ShowMoveCompleteBanner,
    bool ShowConfigChangedBanner)
{
    public static BannerProjection None { get; } = new(false, false, false);
}
