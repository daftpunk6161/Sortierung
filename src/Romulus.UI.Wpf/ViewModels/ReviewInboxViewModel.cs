using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// T-W4-REVIEW-INBOX (wave 5) — Inbox-Workflow VM.
/// Drei Lanes (Safe/Review/Blocked) werden per <see cref="ReviewInboxProjection"/>
/// (SoT) berechnet. Die VM dupliziert keine Scoring/DAT/Winner-Logik.
/// Destruktive Bulk-Aktionen (Quarantine) erfordern den
/// <see cref="IDialogService.DangerConfirm"/> Token-Gate.
/// Decision-Explainer-Sprung pro Zeile via Callback (kein direktes Wissen
/// ueber DecisionExplainerProjection).
/// </summary>
public sealed class ReviewInboxViewModel : ObservableObject
{
    private readonly IDialogService _dialog;
    private readonly ILocalizationService _loc;
    private readonly Action<IReadOnlyList<ReviewInboxRow>>? _quarantineCallback;
    private readonly Action<ReviewInboxRow>? _openExplainerCallback;

    public ReviewInboxViewModel(
        IDialogService dialog,
        ILocalizationService loc,
        Action<IReadOnlyList<ReviewInboxRow>>? quarantineCallback,
        Action<ReviewInboxRow>? openExplainerCallback)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(loc);
        _dialog = dialog;
        _loc = loc;
        _quarantineCallback = quarantineCallback;
        _openExplainerCallback = openExplainerCallback;

        QuarantineSelectedReviewCommand = new RelayCommand(QuarantineSelectedReview, CanQuarantine);
        OpenExplainerCommand = new RelayCommand<ReviewInboxRow?>(OpenExplainer, CanOpenExplainer);
    }

    public ObservableCollection<ReviewInboxRow> Safe { get; } = [];
    public ObservableCollection<ReviewInboxRow> Review { get; } = [];
    public ObservableCollection<ReviewInboxRow> Blocked { get; } = [];

    public IRelayCommand QuarantineSelectedReviewCommand { get; }
    public IRelayCommand<ReviewInboxRow?> OpenExplainerCommand { get; }

    public void Load(IReadOnlyList<RomCandidate> candidates)
    {
        var lanes = ReviewInboxProjection.Project(candidates);
        Safe.Clear();
        foreach (var r in lanes.Safe) Safe.Add(r);
        Review.Clear();
        foreach (var r in lanes.Review) Review.Add(r);
        Blocked.Clear();
        foreach (var r in lanes.Blocked) Blocked.Add(r);

        QuarantineSelectedReviewCommand.NotifyCanExecuteChanged();
        OpenExplainerCommand.NotifyCanExecuteChanged();
    }

    private bool CanQuarantine() => _quarantineCallback is not null;

    private void QuarantineSelectedReview()
    {
        if (_quarantineCallback is null) return;

        var selected = Review.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var title = _loc["ReviewInbox.Quarantine.Title"];
        var message = _loc.Format("ReviewInbox.Quarantine.Message", selected.Count);
        var token = _loc["ReviewInbox.Quarantine.ConfirmText"];
        var button = _loc["ReviewInbox.Quarantine.ButtonLabel"];

        if (!_dialog.DangerConfirm(title, message, token, button))
            return;

        _quarantineCallback(selected);
    }

    private bool CanOpenExplainer(ReviewInboxRow? row)
        => row is not null && _openExplainerCallback is not null;

    private void OpenExplainer(ReviewInboxRow? row)
    {
        if (row is null || _openExplainerCallback is null) return;
        _openExplainerCallback(row);
    }
}
