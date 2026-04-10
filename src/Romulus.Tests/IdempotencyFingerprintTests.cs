using System.Reflection;
using System.Text.Json;
using Romulus.Api;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Verifies that BuildRequestFingerprint in RunLifecycleManager includes
/// all request fields — specifically EnableDatAudit and EnableDatRename.
/// Audit finding A02 (P0): fingerprint must differ when these flags change.
/// </summary>
public sealed class IdempotencyFingerprintTests
{
    private static readonly MethodInfo FingerprintMethod =
        typeof(RunLifecycleManager).GetMethod("BuildRequestFingerprint",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRequestFingerprint method not found");

    [Fact]
    public void DifferentEnableDatAudit_ProducesDifferentFingerprint()
    {
        var a = BuildFingerprint(req => req.EnableDatAudit = false);
        var b = BuildFingerprint(req => req.EnableDatAudit = true);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentEnableDatRename_ProducesDifferentFingerprint()
    {
        var a = BuildFingerprint(req => req.EnableDatRename = false);
        var b = BuildFingerprint(req => req.EnableDatRename = true);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void IdenticalRequests_ProduceSameFingerprint()
    {
        var a = BuildFingerprint(_ => { });
        var b = BuildFingerprint(_ => { });

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentConvertOnly_ProducesDifferentFingerprint()
    {
        var a = BuildFingerprint(req => req.ConvertOnly = false);
        var b = BuildFingerprint(req => req.ConvertOnly = true);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentRemoveJunk_ProducesDifferentFingerprint()
    {
        var a = BuildFingerprint(req => req.RemoveJunk = false);
        var b = BuildFingerprint(req => req.RemoveJunk = true);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentPreferRegionsOrder_ProducesDifferentFingerprint()
    {
        var a = BuildFingerprint(req => req.PreferRegions = ["EUR", "USA", "JPN"]);
        var b = BuildFingerprint(req => req.PreferRegions = ["USA", "EUR", "JPN"]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentConvertFormat_ProducesDifferentFingerprint()
    {
        var a = BuildFingerprint(req => req.ConvertFormat = "chd");
        var b = BuildFingerprint(req => req.ConvertFormat = "rvz");

        Assert.NotEqual(a, b);
    }

    private static string BuildFingerprint(Action<RunRequest> configure)
    {
        var request = new RunRequest
        {
            Roots = [@"C:\TestRoot"],
            Mode = "DryRun",
            PreferRegions = ["EUR", "USA"],
            RemoveJunk = true,
            AggressiveJunk = false,
            SortConsole = false,
            EnableDat = false,
            EnableDatAudit = false,
            EnableDatRename = false,
            OnlyGames = false,
            KeepUnknownWhenOnlyGames = true,
            ConvertOnly = false,
        };

        configure(request);

        return (string)FingerprintMethod.Invoke(null, [request, "DryRun"])!;
    }
}
