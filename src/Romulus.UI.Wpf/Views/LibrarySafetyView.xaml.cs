using System.IO;
using System.Windows;
using System.Windows.Controls;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Views;

public partial class LibrarySafetyView : UserControl
{
    public LibrarySafetyView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnMainVmPropertyChanged;
            oldVm.Run.PropertyChanged -= OnRunVmPropertyChanged;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += OnMainVmPropertyChanged;
            newVm.Run.PropertyChanged += OnRunVmPropertyChanged;
            RefreshLists(newVm);
        }
    }

    private void OnMainVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.LastCandidates) or nameof(MainViewModel.HasRunData))
        {
            if (sender is MainViewModel vm)
                Dispatcher.BeginInvoke(() => RefreshLists(vm));
        }
    }

    private void OnRunVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RunViewModel.LastCandidates) or nameof(RunViewModel.HasRunData))
        {
            if (DataContext is MainViewModel vm)
                Dispatcher.BeginInvoke(() => RefreshLists(vm));
        }
    }

    private void RefreshLists(MainViewModel vm)
    {
        var candidates = vm.Run.LastCandidates;
        var blocked = new List<SafetyListItem>();
        var review = new List<SafetyListItem>();
        var unknown = new List<SafetyListItem>();

        foreach (var c in candidates)
        {
            var reason = !string.IsNullOrWhiteSpace(c.MatchEvidence.Reasoning)
                ? c.MatchEvidence.Reasoning
                : c.ClassificationReasonCode;
            var item = new SafetyListItem(
                Path.GetFileName(c.MainPath),
                c.ConsoleKey,
                c.MatchEvidence.Level.ToString(),
                reason);

            switch (c.SortDecision)
            {
                case SortDecision.Blocked:
                case SortDecision.Unknown:
                    blocked.Add(item);
                    break;
                case SortDecision.Review:
                    review.Add(item);
                    break;
            }

            if (string.IsNullOrWhiteSpace(c.ConsoleKey) ||
                c.ConsoleKey.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                unknown.Add(item);
            }
        }

        BlockedList.ItemsSource = blocked;
        ReviewList.ItemsSource = review;
        UnknownList.ItemsSource = unknown;

        BlockedCount.Text = $"({blocked.Count})";
        ReviewCount.Text = $"({review.Count})";
        UnknownCount.Text = $"({unknown.Count})";

        BlockedEmpty.Visibility = blocked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewEmpty.Visibility = review.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UnknownEmpty.Visibility = unknown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BlockedList.Visibility = blocked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewList.Visibility = review.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnknownList.Visibility = unknown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

internal sealed record SafetyListItem(string FileName, string ConsoleKey, string MatchLevel, string Reason);
