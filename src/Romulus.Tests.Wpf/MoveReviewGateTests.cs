using Romulus.Contracts.Models;
using Romulus.Tests.TestFixtures;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// M6 (UX-Redesign Phase 2): Review-Gate vor Move.
///
/// Pflichtinvarianten:
/// - Ohne RunResult laeuft das Gate transparent durch (kein Dialog, return true).
/// - ConvertBlockedCount &gt; 0 fordert <see cref="StubDialogService.DangerConfirmCalls"/>
///   (typed confirm) und blockiert den Move bei Ablehnung.
/// - Reines ConvertReviewCount &gt; 0 (ohne Blocked) fordert nur einfachen Confirm
///   und blockiert den Move bei Ablehnung.
/// - Bestaetigt der Nutzer, gibt das Gate true zurueck und der bestehende Move-Pfad laeuft weiter.
/// </summary>
public class MoveReviewGateTests
{
    private static string LocStub(string key) =>
        // Format-Strings muessen zumindest "{0}" enthalten, damit
        // string.Format weiterhin funktioniert.
        key switch
        {
            "Dialog.MoveReviewGate.BlockedMessage" => "Blocked: {0}",
            "Dialog.MoveReviewGate.ReviewMessage" => "Review: {0}",
            _ => key
        };

    [Fact]
    public void Gate_PassesThrough_WhenNoRunResult()
    {
        var dialog = new StubDialogService();

        var result = MoveReviewGate.EvaluateBeforeMove(
            lastRunResult: null,
            dialog,
            LocStub);

        Assert.True(result, "Ohne RunResult darf das Gate nicht blockieren");
        Assert.Empty(dialog.ConfirmCalls);
        Assert.Empty(dialog.DangerConfirmCalls);
    }

    [Fact]
    public void Gate_PassesThrough_WhenNoBlockedAndNoReview()
    {
        var dialog = new StubDialogService();
        var run = new RunResult
        {
            ConvertBlockedCount = 0,
            ConvertReviewCount = 0
        };

        var result = MoveReviewGate.EvaluateBeforeMove(run, dialog, LocStub);

        Assert.True(result);
        Assert.Empty(dialog.ConfirmCalls);
        Assert.Empty(dialog.DangerConfirmCalls);
    }

    [Fact]
    public void Gate_BlocksMove_WhenBlockedCountPositive_AndUserDeclinesDangerConfirm()
    {
        var dialog = new StubDialogService { DangerConfirmResult = false };
        var run = new RunResult { ConvertBlockedCount = 3 };
        string? logged = null;

        var result = MoveReviewGate.EvaluateBeforeMove(
            run,
            dialog,
            LocStub,
            (msg, _) => logged = msg);

        Assert.False(result, "Bei abgelehntem DangerConfirm muss der Move blockiert werden");
        Assert.Single(dialog.DangerConfirmCalls);
        Assert.Empty(dialog.ConfirmCalls);
        Assert.Equal("Log.MoveReviewGate.Cancelled", logged);
    }

    [Fact]
    public void Gate_PassesThrough_WhenBlockedCountPositive_AndUserConfirmsDanger()
    {
        var dialog = new StubDialogService { DangerConfirmResult = true };
        var run = new RunResult { ConvertBlockedCount = 5 };

        var result = MoveReviewGate.EvaluateBeforeMove(run, dialog, LocStub);

        Assert.True(result, "Bestaetigter DangerConfirm muss den Move passieren lassen");
        Assert.Single(dialog.DangerConfirmCalls);
        Assert.Empty(dialog.ConfirmCalls);
    }

    [Fact]
    public void Gate_BlocksMove_WhenOnlyReviewCountPositive_AndUserDeclines()
    {
        var dialog = new StubDialogService { ConfirmResult = false };
        var run = new RunResult
        {
            ConvertBlockedCount = 0,
            ConvertReviewCount = 7
        };
        string? logged = null;

        var result = MoveReviewGate.EvaluateBeforeMove(
            run,
            dialog,
            LocStub,
            (msg, _) => logged = msg);

        Assert.False(result);
        Assert.Single(dialog.ConfirmCalls);
        Assert.Empty(dialog.DangerConfirmCalls);
        Assert.Equal("Log.MoveReviewGate.Cancelled", logged);
    }

    [Fact]
    public void Gate_PassesThrough_WhenOnlyReviewCountPositive_AndUserConfirms()
    {
        var dialog = new StubDialogService { ConfirmResult = true };
        var run = new RunResult { ConvertReviewCount = 2 };

        var result = MoveReviewGate.EvaluateBeforeMove(run, dialog, LocStub);

        Assert.True(result);
        Assert.Single(dialog.ConfirmCalls);
        Assert.Empty(dialog.DangerConfirmCalls);
    }

    [Fact]
    public void Gate_PrefersDangerConfirm_OverPlainReview_WhenBothPositive()
    {
        // Wenn sowohl Blocked als auch Review > 0 sind, muss das schaerfere Gate (DangerConfirm)
        // greifen — kein doppelter Dialog, keine Eskalations-Verwischung.
        var dialog = new StubDialogService { DangerConfirmResult = true, ConfirmResult = true };
        var run = new RunResult
        {
            ConvertBlockedCount = 1,
            ConvertReviewCount = 4
        };

        var result = MoveReviewGate.EvaluateBeforeMove(run, dialog, LocStub);

        Assert.True(result);
        Assert.Single(dialog.DangerConfirmCalls);
        Assert.Empty(dialog.ConfirmCalls);
    }
}
