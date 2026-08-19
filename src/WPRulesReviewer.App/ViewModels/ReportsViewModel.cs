using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
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

    /// <summary>Filtered/sorted view bound to the list; honours <see cref="SearchText"/>.</summary>
    public ICollectionView ReportsView { get; }

    [ObservableProperty] private SuspiciousReport? _selected;

    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ReportsView.Refresh();

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

        ReportsView = CollectionViewSource.GetDefaultView(Reports);
        ReportsView.Filter = FilterReport;

        Reports.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(IsEmpty));
            CopyAllCommand.NotifyCanExecuteChanged();
        };
    }

    private bool FilterReport(object obj)
    {
        if (obj is not SuspiciousReport r) return false;
        var q = SearchText?.Trim();
        if (string.IsNullOrEmpty(q)) return true;

        bool Has(string? s) => s is not null && s.Contains(q, StringComparison.OrdinalIgnoreCase);

        if (Has(r.Comment) || Has(r.ActorName) || Has(r.ActorPath) || Has(r.SessionName)
            || Has(r.AssignmentType) || Has(r.Value) || Has(r.BuildNumber))
            return true;

        foreach (var a in r.Actors)
            if (Has(a.ActorName) || Has(a.ActorPath) || Has(a.AssignmentType) || Has(a.Value))
                return true;

        return false;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

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
        var result = PromptDialog.ShowWithConfidence(owner, $"Edit {_kind} comment",
            $"Update the comment for \"{Selected.ActorName}\":", Selected.Comment, Selected.Confidence);
        if (result is null) return;

        Selected.Comment = result.Text.Trim();
        Selected.Confidence = result.Confidence;
        Store.Save(Reports);
        _log.Info($"Updated {_kind} report for '{Selected.ActorName}' (confidence {result.Confidence}/5).", "Reports");
        var id = Selected.Id;
        Refresh();
        Selected = Reports.FirstOrDefault(r => r.Id == id);
    }

    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private void Delete() => DeleteReport(Selected);

    /// <summary>Delete a specific report (from the inline "x" on a list card), with confirmation.</summary>
    [RelayCommand]
    private void DeleteReport(SuspiciousReport? report)
    {
        report ??= Selected;
        if (report is null) return;

        var owner = System.Windows.Application.Current?.MainWindow;
        var answer = MessageBox.Show(owner!,
            $"Delete this {_kind} report for \"{report.ActorName}\"?\nThis cannot be undone.",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var name = report.ActorName;
        var wasSelected = ReferenceEquals(report, Selected);
        Reports.Remove(report);
        Store.Save(Reports);
        if (wasSelected) Selected = Reports.FirstOrDefault();
        _log.Info($"Deleted {_kind} report for '{name}'.", "Reports");
    }

    /// <summary>Update the confidence stars of the selected report and persist it.</summary>
    public void SetSelectedConfidence(int value)
    {
        if (Selected is null) return;
        value = Math.Clamp(value, 1, 5);
        if (Selected.Confidence == value) return;

        Selected.Confidence = value;
        Store.Save(Reports);
        _log.Info($"Set confidence to {value}/5 for '{Selected.ActorName}'.", "Reports");

        var id = Selected.Id;
        Refresh();
        Selected = Reports.FirstOrDefault(r => r.Id == id);
    }

    private bool CanCopyAll() => Reports.Count > 0;

    /// <summary>Copy every report to the clipboard as plain text (actor path, assignment, comment),
    /// with "---" separating each report.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyAll))]
    private void CopyAll()
    {
        if (Reports.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Reports.Count; i++)
        {
            var report = Reports[i];

            var actors = report.Actors.Count > 0
                ? report.Actors
                : new List<SuspiciousReportItem> { new()
                    {
                        ActorPath = report.ActorPath,
                        AssignmentType = report.AssignmentType,
                        Value = report.Value
                    } };

            foreach (var a in actors)
            {
                var path = string.IsNullOrWhiteSpace(a.ActorPath) ? a.ActorName : a.ActorPath;
                var assignment = string.IsNullOrWhiteSpace(a.Value)
                    ? a.AssignmentType
                    : $"{a.AssignmentType} = {a.Value}";
                sb.AppendLine($"Actor: {path}");
                sb.AppendLine($"Assignment: {assignment}");
            }

            sb.AppendLine($"Comment: {report.Comment}");

            if (i < Reports.Count - 1)
                sb.AppendLine("---");
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            _log.Info($"Copied {Reports.Count} {_kind} report(s) to the clipboard.", "Reports");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy reports to the clipboard: {ex.Message}", "Reports");
        }
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
