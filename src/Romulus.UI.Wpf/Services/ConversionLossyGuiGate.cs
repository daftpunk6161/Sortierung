using System.Globalization;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// T-W5-CONVERSION-SAFETY-ADVISOR pass 2 — GUI gate for lossy conversion.
///
/// Single source of truth for the WPF flow that must precede every Move /
/// ConvertOnly run when the most recent DryRun produced a non-empty
/// <see cref="RunResult.PendingLossyToken"/>.
///
/// Wave 5 pass 1 wired the pipeline-side enforcement
/// (<c>ConversionLossyBatchGate</c> + <c>ConversionLossyTokenPolicy</c>):
/// the converter aborts an execute pass when the planned lossy items are not
/// authorized via <see cref="RunOptions.AcceptDataLossToken"/>. That check
/// fires AFTER the user clicked Move and AFTER infra setup, which would surface
/// as a confusing late <see cref="InvalidOperationException"/>. This gate moves
/// the same authorization decision to the GUI, before any infra/pipeline work.
///
/// The user must type the EXACT token displayed in the preview report
/// (returned in <see cref="RunResult.PendingLossyToken"/>); a generic "MOVE"
/// or "CONVERT" string is intentionally not accepted, mirroring CLI's
/// <c>--accept-data-loss &lt;token&gt;</c> contract.
///
/// Pure helper, no fields, no I/O outside the injected dialog/log — keeps
/// GUI/CLI/API capable of sharing the same authorization model without
/// shadow paths.
/// </summary>
internal static class ConversionLossyGuiGate
{
    /// <summary>
    /// Returns the accepted token (caller must propagate it into
    /// <see cref="RunConfigurationDraft.AcceptDataLossToken"/>),
    /// <see langword="null"/> when no gate is required (no pending lossy plan), or
    /// <see cref="string.Empty"/> as a sentinel indicating the user declined and
    /// the caller MUST abort the run.
    /// </summary>
    public static string? Evaluate(
        RunResult? lastRunResult,
        IDialogService dialog,
        Func<string, string> loc,
        Action<string, string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(loc);

        var token = lastRunResult?.PendingLossyToken;
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var title = loc("Dialog.LossyConvert.ConfirmTitle");
        var messageTemplate = loc("Dialog.LossyConvert.ConfirmMessage");
        var message = string.Format(CultureInfo.CurrentCulture, messageTemplate, token);
        var buttonLabel = loc("Dialog.LossyConvert.ConfirmButton");

        var confirmed = dialog.DangerConfirm(title, message, token, buttonLabel);
        if (!confirmed)
        {
            log?.Invoke(loc("Log.LossyConvertCancelled"), "INFO");
            return string.Empty; // sentinel: caller aborts
        }

        return token;
    }
}
