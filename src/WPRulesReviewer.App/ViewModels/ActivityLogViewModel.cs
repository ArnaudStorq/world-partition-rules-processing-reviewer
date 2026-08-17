using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WPRulesReviewer.Core.Logging;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>Bridges the core <see cref="IActivityLog"/> to an observable collection for the bottom panel.</summary>
public sealed partial class ActivityLogViewModel : ObservableObject
{
    private readonly IActivityLog _log;
    private readonly int _maxEntries;

    public ObservableCollection<ActivityLogEntry> Entries { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private ActivityLogEntry? _latest;

    public ActivityLogViewModel(IActivityLog log, int maxEntries)
    {
        _log = log;
        _maxEntries = Math.Max(100, maxEntries);
        _log.EntryAdded += OnEntryAdded;
    }

    private void OnEntryAdded(object? sender, ActivityLogEntry e)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            Entries.Add(e);
            Latest = e;
            while (Entries.Count > _maxEntries) Entries.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void CopyAll()
    {
        try
        {
            var text = string.Join(Environment.NewLine, Entries.Select(e => e.ToPlainLine()));
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        }
        catch { /* clipboard can throw if busy */ }
    }
}
