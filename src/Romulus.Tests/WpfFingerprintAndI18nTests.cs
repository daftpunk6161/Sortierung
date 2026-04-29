using System.IO;
using System.Reflection;
using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave-1 audit fixes: Preview→Move fingerprint must be drift-safe and the
/// new Move-Confirm i18n keys must exist in all three locales.
/// </summary>
public sealed class WpfFingerprintAndI18nTests
{
    private static RunConfigurationDraft NewDraft() => new()
    {
        Roots = new[] { @"C:\Roms" },
        Mode = "DryRun",
        WorkflowScenarioId = null,
        ProfileId = null,
        PreferRegions = new[] { "EU", "USA" },
        Extensions = new[] { ".zip", ".7z" },
        RemoveJunk = true,
        OnlyGames = false,
        KeepUnknownWhenOnlyGames = false,
        AggressiveJunk = false,
        SortConsole = true,
        EnableDat = false,
        EnableDatAudit = false,
        EnableDatRename = false,
        DatRoot = null,
        HashType = "sha1",
        ConvertFormat = null,
        ConvertOnly = false,
        ApproveReviews = false,
        ApproveConversionReview = false,
        ConflictPolicy = "PreferExisting",
        TrashRoot = null
    };

    [Fact]
    public void Compute_Stable_SameDraftYieldsSameHash()
    {
        var a = RunConfigurationDraftFingerprint.Compute(NewDraft());
        var b = RunConfigurationDraftFingerprint.Compute(NewDraft());
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length); // SHA-256 hex
    }

    [Fact]
    public void Compute_NormalizesRegionAndExtensionOrder()
    {
        var d1 = NewDraft() with { PreferRegions = new[] { "EU", "USA" } };
        var d2 = NewDraft() with { PreferRegions = new[] { "USA", "EU" } };
        Assert.Equal(
            RunConfigurationDraftFingerprint.Compute(d1),
            RunConfigurationDraftFingerprint.Compute(d2));

        var e1 = NewDraft() with { Extensions = new[] { ".zip", ".7z" } };
        var e2 = NewDraft() with { Extensions = new[] { ".7z", ".zip" } };
        Assert.Equal(
            RunConfigurationDraftFingerprint.Compute(e1),
            RunConfigurationDraftFingerprint.Compute(e2));
    }

    [Fact]
    public void Compute_RootsOrderIsSignificant()
    {
        // Roots are intentionally NOT reordered — RunOptionsBuilder treats roots as
        // an ordered list (first root wins for tie-breaks). Reordering must change
        // the fingerprint to keep the gate honest.
        var a = NewDraft() with { Roots = new[] { @"C:\A", @"C:\B" } };
        var b = NewDraft() with { Roots = new[] { @"C:\B", @"C:\A" } };
        Assert.NotEqual(
            RunConfigurationDraftFingerprint.Compute(a),
            RunConfigurationDraftFingerprint.Compute(b));
    }

    /// <summary>
    /// Drift guard: every public init property of <see cref="RunConfigurationDraft"/>
    /// must influence the fingerprint. If a new property is added to the draft and not
    /// included in <c>Compute</c>'s canonical projection, this test fails — preventing
    /// silent shadow-state in the Preview→Move gate.
    /// </summary>
    [Fact]
    public void Compute_EveryDraftPropertyChangesFingerprint()
    {
        var baseDraft = NewDraft();
        var baseFp = RunConfigurationDraftFingerprint.Compute(baseDraft);

        var props = typeof(RunConfigurationDraft)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => !FingerprintExcludedProperties.Contains(p.Name)) // see below
            .ToArray();

        Assert.NotEmpty(props);

        foreach (var p in props)
        {
            var mutated = MutateProperty(baseDraft, p);
            var fp = RunConfigurationDraftFingerprint.Compute(mutated);
            Assert.True(
                fp != baseFp,
                $"Mutating {p.Name} must change the fingerprint but did not. " +
                "Add the property to RunConfigurationDraftFingerprint.Compute's canonical projection.");
        }
    }

    /// <summary>
    /// T-W5-CONVERSION-SAFETY-ADVISOR pass 2: <see cref="RunConfigurationDraft.AcceptDataLossToken"/>
    /// is an authorization, NOT a configuration property. Including it in the fingerprint
    /// would make the Preview→Move gate self-locking (Preview produces no token, Execute
    /// adds the token, fingerprints differ → gate refuses to unlock). Pin that the
    /// fingerprint stays stable across token mutation.
    /// </summary>
    [Fact]
    public void Compute_AcceptDataLossToken_ExcludedFromHash()
    {
        var baseFp = RunConfigurationDraftFingerprint.Compute(NewDraft());
        var withToken = NewDraft() with { AcceptDataLossToken = "lossy-tok-XYZ" };
        var withDifferentToken = NewDraft() with { AcceptDataLossToken = "different-token" };

        Assert.Equal(baseFp, RunConfigurationDraftFingerprint.Compute(withToken));
        Assert.Equal(baseFp, RunConfigurationDraftFingerprint.Compute(withDifferentToken));
    }

    /// <summary>
    /// Properties intentionally excluded from the fingerprint. Each entry must
    /// have a documented reason and a corresponding pin test (see e.g.
    /// <see cref="Compute_AcceptDataLossToken_ExcludedFromHash"/>).
    /// </summary>
    private static readonly HashSet<string> FingerprintExcludedProperties = new(StringComparer.Ordinal)
    {
        // Authorization, not configuration. See Compute_AcceptDataLossToken_ExcludedFromHash.
        nameof(RunConfigurationDraft.AcceptDataLossToken),
    };

    private static RunConfigurationDraft MutateProperty(RunConfigurationDraft d, PropertyInfo p)
    {
        var t = p.PropertyType;
        var underlying = Nullable.GetUnderlyingType(t) ?? t;

        object? mutation;
        if (underlying == typeof(bool))
        {
            var current = p.GetValue(d);
            mutation = current is bool b ? !b : (object?)true;
        }
        else if (underlying == typeof(string))
        {
            var current = (string?)p.GetValue(d);
            mutation = (current ?? string.Empty) + "_mut";
        }
        else if (underlying == typeof(string[]))
        {
            mutation = new[] { "__MUTATION_MARKER__" };
        }
        else
        {
            throw new InvalidOperationException(
                $"Test does not know how to mutate property {p.Name} of type {t}. " +
                "Extend MutateProperty when adding new property kinds to RunConfigurationDraft.");
        }

        // Clone the record via the synthesized "<Clone>$" method, then patch the
        // backing field of p directly.
        var cloneMethod = typeof(RunConfigurationDraft).GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance)!;
        var copy = (RunConfigurationDraft)cloneMethod.Invoke(d, null)!;

        var backingField = typeof(RunConfigurationDraft).GetField(
            $"<{p.Name}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(backingField);
        backingField!.SetValue(copy, mutation);
        return copy;
    }

    [Theory]
    [InlineData("data/i18n/de.json")]
    [InlineData("data/i18n/en.json")]
    [InlineData("data/i18n/fr.json")]
    public void MoveConfirmAndDangerHintKeysPresentInAllLocales(string relPath)
    {
        var path = LocateRepoFile(relPath);
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        foreach (var key in new[]
        {
            "Dialog.Move.ConfirmTitle",
            "Dialog.Move.ConfirmMessage",
            "Dialog.Move.ConfirmText",
            "Dialog.Move.ConfirmButton",
            "Log.MoveCancelled",
            "Dialog.DangerConfirm.HintFormat",
            "Dialog.DangerConfirm.DefaultButtonLabel"
        })
        {
            Assert.True(
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Length > 0,
                $"Missing or empty i18n key '{key}' in {relPath}");
        }

        // F-03 contract: hint format must contain the {0} placeholder.
        var hint = root.GetProperty("Dialog.DangerConfirm.HintFormat").GetString()!;
        Assert.Contains("{0}", hint);
    }

    private static string LocateRepoFile(string relPath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {relPath} from test base directory.");
    }
}
