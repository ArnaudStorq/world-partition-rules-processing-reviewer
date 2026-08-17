using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WPRulesReviewer.App.Services;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Reporting;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>
/// Backs one of the "Approved Reports" / "Suspicious Reports" tabs: loads the JSON journal,
/// lets the user pick, edit (comment) or delete (with confirmation) individual entries.
/// </summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IActivityLog _log;
    private readonly string _fileName;
    private readonly string _kind;

    public string Title { get; }
    public string HeaderGlyph { get; }

    public ObservableCollection<SuspiciousReport> Reports { get; } = new();

    [ObservableProperty] private SuspiciousReport? _selected;

    public bool HasSelection => Selected is not null;
    public bool IsEmpty => Reports.Count == 0;
    public int Count => Reports.Count;

    public ReportsViewModel(AppSettings settings, IActivityLog log, string fileName, string title,
        string headerGlyph, string kind)
    {
        _settings = settings;
        _log = log;
        _fileName = fileName;
        Title = title;
        HeaderGlyph = headerGlyph;
        _kind = kind;

        Reports.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    private SuspiciousReportStore Store => new(_settings.AppDataFolder, _fileName);

    partial void OnSelectedChanged(SuspiciousReport? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void Refresh()
    {
        var keepId = Selected?.Id;
        Reports.Clear();
        foreach (var r in Store.Load().OrderByDescending(r => r.ReportedAt))
            Reports.Add(r);
        Selected = (keepId is not null ? Reports.FirstOrDefault(r => r.Id == keepId) : null)
                   ?? Reports.FirstOrDefault();
    }

    private bool CanEditOrDelete() => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private void Edit()
    {
        if (Selected is null) return;
        var owner = System.Windows.Application.Current?.MainWindow;
        var comment = PromptDialog.Show(owner, $"Edit {_kind} comment",
            $"Update the comment for \"{Selected.ActorName}\":", Selected.Comment);
        if (comment is null) return;

        Selected.Comment = comment.Trim();
        Store.Save(Reports);
        _log.Info($"Updated {_kind} report for '{Selected.ActorName}'.", "Reports");
        var id = Selected.Id;
        Refresh();
        Selected = Reports.FirstOrDefault(r => r.Id == id);
    }

    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private void Delete()
    {
        if (Selected is null) return;
        var owner = System.Windows.Application.Current?.MainWindow;
        var answer = MessageBox.Show(owner!,
            $"Delete this {_kind} report for \"{Selected.ActorName}\"?\nThis cannot be undone.",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var name = Selected.ActorName;
        Reports.Remove(Selected);
        Store.Save(Reports);
        Selected = null;
        _log.Info($"Deleted {_kind} report for '{name}'.", "Reports");
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var path = Store.Folder;
            System.IO.Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open the reports folder: {ex.Message}", "Reports");
        }
    }
}
