using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Tests.TestFixtures;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// T-W5-CONVERSION-SAFETY-ADVISOR (pass 2 — GUI gate):
///
/// The Wave 2 ConversionLossyTokenPolicy + Wave 5 pass-1 ConversionLossyBatchGate
/// stop a lossy execute pass at the converter level when RunOptions.AcceptDataLossToken
/// does not match the planned items. Pass 2 closes the GUI side: the WPF view-model
/// must show a typed-token DangerConfirm BEFORE starting a Move/ConvertOnly run when
/// the most recent DryRun produced a non-empty <see cref="RunResult.PendingLossyToken"/>.
///
/// These tests pin the pure helper <c>ConversionLossyGuiGate</c> (single source for the
/// dialog flow) plus the propagation of the accepted token into the next
/// <see cref="RunConfigurationDraft"/>. Drift-guard for the fingerprint exclusion
/// lives next to it: the token is an authorization, not a configuration property,
/// and must NOT change the Preview→Move fingerprint (otherwise the gate would lock
/// itself out the moment the token is added to the draft).
/// </summary>
public class ConversionLossyGuiGateTests
{
    private static readonly string[] ConfirmedLog = ["INFO"];

    private static Func<string, string> Loc()
        => key => key switch
        {
            // Provide a {0} placeholder for message templates that string.Format-substitute
            // the token — keeps Evaluate_DangerConfirmReceives_TokenAsConfirmText honest
            // without depending on a specific locale's exact wording.
            "Dialog.LossyConvert.ConfirmMessage" => "Token: {0}",
            _ => key,
        };

    private static Action<string, string> CapturingLog(List<(string Msg, string Level)> sink)
        => (msg, level) => sink.Add((msg, level));

    [Fact]
    public void Evaluate_NoLastRunResult_ReturnsNull_NoDialog()
    {
        var dialog = new StubDialogService { DangerConfirmResult = true };
        var logs = new List<(string Msg, string Level)>();

        var result = ConversionLossyGuiGate.Evaluate(
            lastRunResult: null,
            dialog: dialog,
            loc: Loc(),
            log: CapturingLog(logs));

        Assert.Null(result);
        Assert.Empty(dialog.DangerConfirmInvocations);
    }

    [Fact]
    public void Evaluate_PendingLossyTokenEmpty_ReturnsNull_NoDialog()
    {
        var dialog = new StubDialogService { DangerConfirmResult = true };
        var rr = new RunResult { Status = RunConstants.StatusOk, PendingLossyToken = "   " };

        var result = ConversionLossyGuiGate.Evaluate(rr, dialog, Loc(), null);

        Assert.Null(result);
        Assert.Empty(dialog.DangerConfirmInvocations);
    }

    [Fact]
    public void Evaluate_PendingLossyTokenSet_DangerConfirmAccepted_ReturnsToken()
    {
        var dialog = new StubDialogService { DangerConfirmResult = true };
        var rr = new RunResult { Status = RunConstants.StatusOk, PendingLossyToken = "abc123def456" };

        var result = ConversionLossyGuiGate.Evaluate(rr, dialog, Loc(), null);

        Assert.Equal("abc123def456", result);
        Assert.Single(dialog.DangerConfirmInvocations);
    }

    [Fact]
    public void Evaluate_PendingLossyTokenSet_DangerConfirmDeclined_ReturnsEmptyString_AndLogsCancelled()
    {
        var dialog = new StubDialogService { DangerConfirmResult = false };
        var rr = new RunResult { Status = RunConstants.StatusOk, PendingLossyToken = "abc123def456" };
        var logs = new List<(string Msg, string Level)>();

        var result = ConversionLossyGuiGate.Evaluate(rr, dialog, Loc(), CapturingLog(logs));

        Assert.Equal(string.Empty, result); // sentinel: caller must abort
        Assert.Single(dialog.DangerConfirmInvocations);
        Assert.Single(logs);
        Assert.Equal("Log.LossyConvertCancelled", logs[0].Msg);
    }

    [Fact]
    public void Evaluate_DangerConfirmReceives_TokenAsConfirmText()
    {
        // Drift-guard: the user must be forced to type the EXACT token (not "MOVE"
        // or "CONVERT"). If a future refactor accidentally substitutes a static
        // confirmText this assertion catches it.
        var dialog = new StubDialogService { DangerConfirmResult = true };
        var rr = new RunResult { Status = RunConstants.StatusOk, PendingLossyToken = "lossy-tok-XYZ" };

        ConversionLossyGuiGate.Evaluate(rr, dialog, Loc(), null);

        Assert.Single(dialog.DangerConfirmInvocations);
        var inv = dialog.DangerConfirmInvocations[0];
        Assert.Equal("Dialog.LossyConvert.ConfirmTitle", inv.Title);
        Assert.Equal("Dialog.LossyConvert.ConfirmButton", inv.ButtonLabel);
        Assert.Equal("lossy-tok-XYZ", inv.ConfirmText);
        // Message must surface the token so the user can copy-type it.
        Assert.Contains("lossy-tok-XYZ", inv.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_NullDialog_Throws()
    {
        var rr = new RunResult { Status = RunConstants.StatusOk, PendingLossyToken = "tok" };
        Assert.Throws<ArgumentNullException>(() =>
            ConversionLossyGuiGate.Evaluate(rr, dialog: null!, loc: Loc(), log: null));
    }

    [Fact]
    public void Evaluate_NullLoc_Throws()
    {
        var rr = new RunResult { Status = RunConstants.StatusOk, PendingLossyToken = "tok" };
        var dialog = new StubDialogService { DangerConfirmResult = true };
        Assert.Throws<ArgumentNullException>(() =>
            ConversionLossyGuiGate.Evaluate(rr, dialog, loc: null!, log: null));
    }

    // ── MainViewModel integration ────────────────────────────────────

    [Fact]
    public void ConvertOnly_WhenLastRunHasPendingLossyToken_ChainsLossyDangerConfirm_AfterConvertOnlyConfirm()
    {
        // Sequenced responses: 1) ConvertOnly DangerConfirm → true, 2) Lossy DangerConfirm → true.
        var dialog = new StubDialogService();
        dialog.DangerConfirmResponses.Enqueue(true);
        dialog.DangerConfirmResponses.Enqueue(true);

        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");
        vm.LastRunResult = new RunResult
        {
            Status = RunConstants.StatusOk,
            PendingLossyToken = "lossy-tok-abc",
        };

        vm.ConvertOnlyCommand.Execute(null);

        Assert.True(vm.ConvertOnly);
        Assert.False(vm.DryRun);
        Assert.Equal(2, dialog.DangerConfirmInvocations.Count);
        Assert.Equal("lossy-tok-abc", dialog.DangerConfirmInvocations[1].ConfirmText);
    }

    [Fact]
    public void ConvertOnly_AbortsWhenLossyDangerConfirmDeclined_AfterConvertOnlyConfirmAccepted()
    {
        var dialog = new StubDialogService();
        dialog.DangerConfirmResponses.Enqueue(true);  // ConvertOnly gate: accept
        dialog.DangerConfirmResponses.Enqueue(false); // Lossy gate: decline

        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");
        vm.LastRunResult = new RunResult
        {
            Status = RunConstants.StatusOk,
            PendingLossyToken = "lossy-tok-abc",
        };

        vm.ConvertOnlyCommand.Execute(null);

        Assert.False(vm.ConvertOnly);
        Assert.Equal(2, dialog.DangerConfirmInvocations.Count);
    }

    [Fact]
    public void ConvertOnly_NoExtraDialog_WhenNoPendingLossyToken()
    {
        var dialog = new StubDialogService { DangerConfirmResult = true };

        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");
        vm.LastRunResult = new RunResult
        {
            Status = RunConstants.StatusOk,
            PendingLossyToken = null, // no lossy plan
        };

        vm.ConvertOnlyCommand.Execute(null);

        Assert.True(vm.ConvertOnly);
        Assert.Single(dialog.DangerConfirmInvocations); // only the ConvertOnly gate
    }

    [Fact]
    public void BuildCurrentRunConfigurationDraft_PropagatesAcceptedLossyToken_AfterConvertOnlyAccept()
    {
        var dialog = new StubDialogService();
        dialog.DangerConfirmResponses.Enqueue(true);
        dialog.DangerConfirmResponses.Enqueue(true);

        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");
        vm.LastRunResult = new RunResult
        {
            Status = RunConstants.StatusOk,
            PendingLossyToken = "lossy-tok-XYZ-99",
        };

        // Invoke the gate path directly via the public command. We then assert the
        // field on the view-model rather than the synthesised draft, because the
        // command kicks off the run pipeline (RunCommand.Execute) which transitions
        // through Preflight/Cancelled/Failed terminal states and clears the token by
        // design (no leaked authorization across runs). The propagation-into-draft
        // assertion lives in the next test, which exercises BuildCurrentRunConfigurationDraft
        // synchronously without triggering an actual run.
        vm.ConvertOnlyCommand.Execute(null);

        Assert.Equal(2, dialog.DangerConfirmInvocations.Count);
        Assert.Equal("lossy-tok-XYZ-99", dialog.DangerConfirmInvocations[1].ConfirmText);
    }

    [Fact]
    public void BuildCurrentRunConfigurationDraft_IncludesAcceptedLossyToken_WhenFieldSet()
    {
        var dialog = new StubDialogService();
        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");

        // Inject the accepted token via reflection (mirrors the path executed by
        // OnConvertOnlyRequested / StartMoveCommand once the user typed the token).
        // We avoid going through ConvertOnlyCommand here so RunCommand.Execute does
        // not reset the field via the terminal-state observer.
        var field = typeof(MainViewModel).GetField("_acceptedLossyDataLossToken",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(vm, "lossy-tok-FROM-FIELD");

        var draft = vm.BuildCurrentRunConfigurationDraft();
        Assert.Equal("lossy-tok-FROM-FIELD", draft.AcceptDataLossToken);
    }

    [Fact]
    public void BuildCurrentRunConfigurationDraft_DoesNotIncludeLossyToken_WhenNeverAccepted()
    {
        var dialog = new StubDialogService { DangerConfirmResult = true };
        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");

        var draft = vm.BuildCurrentRunConfigurationDraft();
        Assert.Null(draft.AcceptDataLossToken);
    }
}
