using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Reporting;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>One open review tab: a single analyzed Sundance.log with filters, steps, AI report and export.</summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IActivityLog _log;
    private readonly IAiAgentService _ai;
    private readonly RecordInspectorViewModel _inspector;

    private SessionReport? _report;
    private ListCollectionView? _view;
    private string[]? _sourceLines;

    public string Title { get; }
    public ObservableCollection<PipelineStepViewModel> Steps { get; } = new();

    /// <summary>Shared app settings (used by the view to restore/persist the Smart panel width).</summary>
    public AppSettings Settings => _settings;

    /// <summary>True while the log is being processed; the tab shows a spinner and is disabled.</summary>
    [ObservableProperty] private bool _isProcessing = true;

    /// <summary>Inverse of <see cref="IsProcessing"/>; the tab is clickable only once processing is done.</summary>
    public bool IsReady => !IsProcessing;

    partial void OnIsProcessingChanged(bool value) => OnPropertyChanged(nameof(IsReady));

    public SessionViewModel(string title, AppSettings settings, IActivityLog log, IAiAgentService ai,
        RecordInspectorViewModel inspector)
    {
        Title = title;
        _settings = settings;
        _log = log;
        _ai = ai;
        _inspector = inspector;

        foreach (var s in ReviewPipeline.CreateSteps())
            Steps.Add(new PipelineStepViewModel(s));

        HlodLayerFilter = new AssignmentValueFilter("HLODLayer", AssignmentType.HLODLayer, RefreshView);
        IncludeInHlodFilter = new AssignmentValueFilter("IncludeInHLOD", AssignmentType.IncludeInHLOD, RefreshView);
        DataLayerFilter = new AssignmentValueFilter("DataLayer", AssignmentType.DataLayer, RefreshView);
        RuntimeGridFilter = new AssignmentValueFilter("RuntimeGrid", AssignmentType.RuntimeGrid, RefreshView);
        AssignmentFilters = new[] { HlodLayerFilter, IncludeInHlodFilter, DataLayerFilter, RuntimeGridFilter };
    }

    // ---- Value filters (mirror the web "Filter by ..." checkbox dropdowns) --
    public AssignmentValueFilter HlodLayerFilter { get; }
    public AssignmentValueFilter IncludeInHlodFilter { get; }
    public AssignmentValueFilter DataLayerFilter { get; }
    public AssignmentValueFilter RuntimeGridFilter { get; }
    public IReadOnlyList<AssignmentValueFilter> AssignmentFilters { get; }

    private void BuildValueFilters(SessionReport report)
    {
        foreach (var f in AssignmentFilters)
        {
            var items = report.Records
                .Where(r => r.Category == RecordCategory.Applied && r.AssignmentType == f.Type)
                .GroupBy(r => r.Value ?? string.Empty)
                .Select(g => new { Value = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ValueFilterItem(
                    x.Value,
                    string.IsNullOrEmpty(x.Value) ? "None" : x.Value,
                    x.Count,
                    f.NotifyItemChanged));
            f.SetItems(items);
        }
    }

    // ---- Live pipeline updates --------------------------------------------
    public void ApplyStep(PipelineStep step)
    {
        var vm = Steps.FirstOrDefault(s => s.Key == step.Key);
        vm?.Update(step);
    }

    public void SetReport(SessionReport report)
    {
        _report = report;
        _sourceLines = null;

        // Processing date shown as a badge on the tab. Prefer the TeamCity build's start date
        // (matches the builds list on the left); fall back to the log timestamp for local files.
        var dt = (_processingDateOverride ?? report.LogTime)?.LocalDateTime;
        ProcessingDateLabel = dt.HasValue
            ? dt.Value.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        OnPropertyChanged(nameof(ProcessingDateLabel));
        OnPropertyChanged(nameof(HasProcessingDate));

        BuildValueFilters(report);
        _view = new ListCollectionView(report.Records) { Filter = FilterRecord };
        OnPropertyChanged(nameof(Records));
        RefreshCounts();
        RebuildPage();
        IsProcessing = false;
    }

    /// <summary>TeamCity processing date for the tab badge (empty for local logs without a timestamp).</summary>
    public string ProcessingDateLabel { get; private set; } = string.Empty;
    public bool HasProcessingDate => !string.IsNullOrEmpty(ProcessingDateLabel);

    private DateTimeOffset? _processingDateOverride;

    /// <summary>Explicit processing date (the TeamCity build start date) used for the tab badge.</summary>
    public void SetProcessingDate(DateTimeOffset? date) => _processingDateOverride = date;

    /// <summary>
    /// True once the user ran Auto-resolve reading and marked rows as read. The grid's "AI check"
    /// column stays hidden until then, since AI notes only exist after that pass.
    /// </summary>
    [ObservableProperty] private bool _aiReviewApplied;

    public ICollectionView? Records => _view;

    // ---- Pagination --------------------------------------------------------
    private int PageSize => UnlimitedPage ? int.MaxValue : Math.Max(1, _settings.ItemsPerPage);

    /// <summary>When enabled, show every filtered row on a single page (no paging).</summary>
    [ObservableProperty] private bool _unlimitedPage;

    partial void OnUnlimitedPageChanged(bool value)
    {
        CurrentPage = 1;
        RebuildPage();
    }

    /// <summary>Rows of the current page bound to the grid. Reassigned as a whole (single
    /// collection reset) so the DataGrid does one refresh instead of one event per row.</summary>
    [ObservableProperty] private IReadOnlyList<RuleRecord> _pagedRecords = System.Array.Empty<RuleRecord>();

    [ObservableProperty] private int _currentPage = 1;

    public int TotalPages => UnlimitedPage ? 1 : Math.Max(1, (ShowingCount + PageSize - 1) / PageSize);
    public string PageLabel => $"Page {CurrentPage} of {TotalPages}";

    private void RebuildPage()
    {
        var all = _view?.Cast<RuleRecord>().ToList() ?? new List<RuleRecord>();
        if (_showingCount != all.Count)
        {
            _showingCount = all.Count;
            OnPropertyChanged(nameof(ShowingCount));
        }
        var totalPages = UnlimitedPage ? 1 : Math.Max(1, (all.Count + PageSize - 1) / PageSize);
        if (CurrentPage > totalPages) CurrentPage = totalPages;
        if (CurrentPage < 1) CurrentPage = 1;

        PagedRecords = UnlimitedPage
            ? all
            : all.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageLabel));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoPrevious() => CurrentPage > 1;
    private bool CanGoNext() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        if (CurrentPage <= 1) return;
        CurrentPage--;
        RebuildPage();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage()
    {
        if (CurrentPage >= TotalPages) return;
        CurrentPage++;
        RebuildPage();
    }
    public SessionReport? Report => _report;

    // ---- Side "Smart Analysis" lists (our improvements) --------------------
    public IReadOnlyList<RuleRecord> AnomalyRecords =>
        _report?.Records.Where(r => r.Status == ReviewStatus.Anomaly)
            .OrderByDescending(r => r.Severity).ToList() ?? new List<RuleRecord>();

    public IReadOnlyList<RuleRecord> NeedsReviewRecords =>
        _report?.Records.Where(r => r.Status == ReviewStatus.NeedsReview)
            .OrderByDescending(r => r.Severity).ToList() ?? new List<RuleRecord>();

    public IReadOnlyList<RuleRecord> ImportErrorRecords =>
        _report?.Records.Where(r => r.Category == RecordCategory.ImportError).ToList() ?? new List<RuleRecord>();

    // ---- Grouped Smart Analysis (collapse identical problems, expand for actors/assets) ----
    public IReadOnlyList<InsightGroup> AnomalyGroups => GroupInsights(AnomalyRecords);
    public IReadOnlyList<InsightGroup> NeedsReviewGroups => GroupInsights(NeedsReviewRecords);
    public IReadOnlyList<InsightGroup> ImportErrorGroups => GroupInsights(ImportErrorRecords);

    private static IReadOnlyList<InsightGroup> GroupInsights(IEnumerable<RuleRecord> records) =>
        records
            .GroupBy(GroupKey)
            .Select(g => new InsightGroup(
                g.Key,
                g.OrderBy(r => r.DisplayActor, StringComparer.OrdinalIgnoreCase).ToList(),
                g.Max(r => r.Severity)))
            .OrderByDescending(g => g.Severity)
            .ThenByDescending(g => g.Count)
            .ToList();

    /// <summary>Identity of a "problem": the triage reason, then the raw reason, then the value.</summary>
    private static string GroupKey(RuleRecord r)
    {
        var raw =
            !string.IsNullOrWhiteSpace(r.StatusReason) ? r.StatusReason!
            : !string.IsNullOrWhiteSpace(r.Reason) ? r.Reason!
            : !string.IsNullOrWhiteSpace(r.Value) ? r.Value
            : r.CategoryLabel;
        return NormalizeReason(raw);
    }

    // Collapse messages that only differ by a specific quoted identifier, e.g.
    // "Expected DataLayer 'DL_WE_A10_Fireflies_1' ..." and "... 'DL_WE_A10_Fireflies_7' ..."
    // become the same "Expected DataLayer '…' ..." group.
    private static readonly Regex QuotedValue = new("'[^']*'", RegexOptions.Compiled);

    private static string NormalizeReason(string reason)
        => QuotedValue.Replace(reason, "'…'").Trim();

    /// <summary>Selection in a side list; mirrors into the shared inspector without touching the main grid.</summary>
    [ObservableProperty] private RuleRecord? _sideSelectedRecord;

    partial void OnSideSelectedRecordChanged(RuleRecord? value)
    {
        if (value is null) return;
        _inspector.Show(value, GetSourceLines());
    }

    /// <summary>Raised when a grouped record should be scrolled into view in the main grid.</summary>
    public event Action<RuleRecord>? RecordRevealRequested;

    /// <summary>
    /// Show one grouped record in the inspector (used by the expandable Smart Analysis rows) and
    /// point directly to the matching row in the main grid (switch tab, page and select it).
    /// </summary>
    [RelayCommand]
    private void ShowRecord(RuleRecord? record)
    {
        if (record is null) return;
        _inspector.Show(record, GetSourceLines());
        RevealInGrid(record);
    }

    /// <summary>
    /// Open a brand new Cursor agent thread pre-loaded with the full English context of one anomaly
    /// group, so the agent can investigate and fix it. The complete context is also saved to disk.
    /// </summary>
    [RelayCommand]
    private void OpenInCursor(InsightGroup? group)
    {
        if (group is null || group.Items.Count == 0) return;
        try
        {
            var full = BuildCursorContext(group, null);

            // Persist the complete context so nothing is lost and long lists stay accessible.
            var folder = Core.Persistence.AppPaths.EnsureSub("CursorContext");
            var file = Path.Combine(folder, $"{SanitizeName(group.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(file, full);

            // The deeplink itself carries a bounded prompt; it points to the file for the complete list.
            var prompt = BuildCursorPrompt(group, file);
            var deeplink = "cursor://anysphere.cursor-deeplink/prompt?text=" + Uri.EscapeDataString(prompt);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(deeplink) { UseShellExecute = true });
            _log.Success($"Opened a new Cursor agent for: {group.Title} ({group.Count} actor(s)). Context saved to {file}", "Cursor");
        }
        catch (Exception ex)
        {
            _log.Error($"Could not open Cursor: {ex.Message}", "Cursor");
        }
    }

    /// <summary>
    /// Short prompt embedded in the deeplink. The Cursor prompt deeplink has a tight length limit
    /// (a long URL fails with "Error handling deep link: Invalid text"), so we keep the inline text
    /// minimal and point the agent to the full context file written to disk.
    /// </summary>
    private string BuildCursorPrompt(InsightGroup group, string contextFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Investigate and create a plan to fix a World Partition Rules issue in the 'Sundance' Unreal Engine project (Perforce workspace: D:\\Sun).");
        sb.AppendLine($"Issue: {group.Title} - {group.Count} affected actor(s)/asset(s), severity {group.Severity}.");
        sb.AppendLine("The full context (run info, every affected actor, raw log lines) is in this file - open and read it first:");
        sb.AppendLine(contextFile);
        sb.AppendLine("Then produce a clear step-by-step PLAN to fix it (root cause, where the fix belongs, concrete changes, how to validate). Do not modify any files yet.");
        // Cursor prompt deeplinks drop everything after a literal '&'; keep the text ampersand-free.
        return sb.ToString().Replace("&", "and");
    }

    private string BuildCursorContext(InsightGroup group, int? maxItems)
    {
        var first = group.Items[0];
        var sb = new StringBuilder();
        sb.AppendLine("# World Partition Rules - anomaly to investigate and fix");
        sb.AppendLine();
        sb.AppendLine("You are an expert Unreal Engine 5 World Partition engineer working on the 'Sundance' project.");
        sb.AppendLine("The Perforce workspace for Sundance / UnrealEngine is at D:\\Sun.");
        sb.AppendLine("World Partition rules are configured in DefaultEditor.ini, section [/Script/WorldBuildingEditor.WorldPartitionRuleSettings].");
        if (!string.IsNullOrWhiteSpace(_settings.DefaultEditorIniPath))
            sb.AppendLine($"Rule oracle file: {_settings.DefaultEditorIniPath}");
        sb.AppendLine();
        sb.AppendLine("## Run context");
        sb.AppendLine($"- Session: {_report?.SessionName}");
        if (!string.IsNullOrWhiteSpace(_report?.World)) sb.AppendLine($"- World: {_report!.World}");
        if (!string.IsNullOrWhiteSpace(_report?.BuildNumber)) sb.AppendLine($"- TeamCity build: #{_report!.BuildNumber}");
        if (!string.IsNullOrWhiteSpace(_report?.WebUrl)) sb.AppendLine($"- TeamCity report: {_report!.WebUrl}");
        if (!string.IsNullOrWhiteSpace(_report?.SourcePath)) sb.AppendLine($"- Downloaded log file: {_report!.SourcePath}");
        sb.AppendLine();
        sb.AppendLine("## Problem");
        sb.AppendLine($"- Category: {first.CategoryLabel}");
        sb.AppendLine($"- Severity: {group.Severity}");
        sb.AppendLine($"- Issue: {group.Title}");
        sb.AppendLine($"- Affected actors/assets: {group.Count}");
        sb.AppendLine();

        var items = maxItems is int max && group.Items.Count > max
            ? group.Items.Take(max).ToList()
            : group.Items.ToList();

        sb.AppendLine($"## Affected actors/assets{(items.Count < group.Count ? $" (showing first {items.Count} of {group.Count})" : string.Empty)}");
        foreach (var r in items)
        {
            sb.AppendLine($"- Actor: {r.DisplayActor}");
            if (!string.IsNullOrWhiteSpace(r.Value)) sb.AppendLine($"    - Value: {r.Value}");
            if (!string.IsNullOrWhiteSpace(r.ExpectedValue)) sb.AppendLine($"    - Expected: {r.ExpectedValue}");
            if (!string.IsNullOrWhiteSpace(r.StatusReason)) sb.AppendLine($"    - Triage reason: {r.StatusReason}");
            if (!string.IsNullOrWhiteSpace(r.Reason) && r.Reason != r.StatusReason) sb.AppendLine($"    - Detail: {r.Reason}");
            if (!string.IsNullOrWhiteSpace(r.RawLine)) sb.AppendLine($"    - Log line {r.LineNumber}: {r.RawLine}");
        }
        sb.AppendLine();
        sb.AppendLine("## Your task");
        sb.AppendLine("Investigate this issue on your own using the context above and the project files, then produce a clear PLAN to fix it. Do NOT change any files yet - I only want the plan at this stage.");
        sb.AppendLine("The plan should cover:");
        sb.AppendLine("1. The most likely root cause, based on your own investigation of the codebase and the data referenced above.");
        sb.AppendLine("2. Where the fix most likely belongs: the World Partition rule config (DefaultEditor.ini), the actor/asset data, or the level setup.");
        sb.AppendLine("3. The concrete step-by-step changes you would make, and any files or rules you would touch.");
        sb.AppendLine("4. How to validate the fix on the next World Partition rules pass.");
        sb.AppendLine("Feel free to explore the repository to gather any extra context you need before writing the plan.");
        return sb.ToString();
    }

    private void RevealInGrid(RuleRecord record)
    {
        // Pick the grid tab able to display this record's category.
        var tab = record.Category switch
        {
            RecordCategory.Applied => "Applied",
            RecordCategory.Warning or RecordCategory.Error => "WarningsErrors",
            RecordCategory.Skipped => "Skipped",
            _ => null
        };
        if (tab is null) return; // e.g. ImportError is not shown in the grid

        if (SelectedTab != tab) SelectedTab = tab;

        // Make sure the row is not hidden by the visibility toggles.
        if (record.IsRead && !ShowRead) ShowRead = true;
        if (record.Status == ReviewStatus.Expected && !ShowExpected) ShowExpected = true;
        if (record.Status == ReviewStatus.KnownNoise && !ShowKnownNoise) ShowKnownNoise = true;

        // Navigate to the page holding the record inside the filtered view.
        var filtered = _view?.Cast<RuleRecord>().ToList() ?? new List<RuleRecord>();
        var index = filtered.IndexOf(record);
        if (index < 0) return;

        var page = index / PageSize + 1;
        if (CurrentPage != page) { CurrentPage = page; RebuildPage(); }

        SelectedRecord = record;
        RecordRevealRequested?.Invoke(record);
    }

    // ---- Row selection: echo the original log line(s) to the activity log --
    [ObservableProperty] private RuleRecord? _selectedRecord;

    partial void OnSelectedRecordChanged(RuleRecord? value)
    {
        if (value is null) return;
        _inspector.Show(value, GetSourceLines());
    }

    /// <summary>Current multi-selection from the grid (pushed by the view on SelectionChanged).</summary>
    private IReadOnlyList<RuleRecord> _selectedRecords = System.Array.Empty<RuleRecord>();

    public void SetSelectedRecords(IEnumerable<RuleRecord> records)
        => _selectedRecords = records.ToList();

    /// <summary>Records a per-row action should target: the whole selection if the clicked row is
    /// part of it, otherwise just the clicked row.</summary>
    private IReadOnlyList<RuleRecord> TargetRecords(RuleRecord? clicked)
    {
        if (clicked is null) return _selectedRecords.Count > 0 ? _selectedRecords : System.Array.Empty<RuleRecord>();
        if (_selectedRecords.Count > 1 && _selectedRecords.Contains(clicked)) return _selectedRecords;
        return new[] { clicked };
    }

    private string[]? GetSourceLines()
    {
        if (_sourceLines is not null) return _sourceLines;
        var path = _report?.SourcePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try { _sourceLines = File.ReadAllLines(path); } catch { _sourceLines = null; }
        return _sourceLines;
    }

    // ---- Summary counts ----------------------------------------------------
    public int AppliedCount => _report?.AppliedCount ?? 0;
    public int WarningCount => _report?.WarningCount ?? 0;
    public int ErrorCount => _report?.ErrorCount ?? 0;
    public int WarningErrorCount => WarningCount + ErrorCount;
    public int ImportErrorCount => _report?.ImportErrorCount ?? 0;
    public int SkippedCount => _report?.SkippedCount ?? 0;
    public int AnomalyCount => _report?.AnomalyCount ?? 0;
    public int ExpectedCount => _report?.ExpectedCount ?? 0;
    public int KnownNoiseCount => _report?.KnownNoiseCount ?? 0;
    public int NeedsReviewCount => _report?.NeedsReviewCount ?? 0;

    // ---- Include-filter counts, scoped to the active tab so the 3 boxes partition it ----------
    private bool InCurrentTab(RuleRecord r) => SelectedTab switch
    {
        "Applied" => r.Category == RecordCategory.Applied,
        "WarningsErrors" => r.Category is RecordCategory.Warning or RecordCategory.Error,
        "Skipped" => r.Category == RecordCategory.Skipped,
        _ => true
    };
    private int CountInTab(Func<RuleRecord, bool> pred) =>
        _report?.Records.Where(InCurrentTab).Count(pred) ?? 0;

    public int FilterAnomalyCount => CountInTab(r => r.Status is ReviewStatus.Anomaly or ReviewStatus.NeedsReview);
    public int FilterExpectedCount => CountInTab(r => r.Status == ReviewStatus.Expected);
    public int FilterKnownNoiseCount => CountInTab(r => r.Status == ReviewStatus.KnownNoise);
    public int TotalCount => _report?.TotalCount ?? 0;

    // ---- Assignment scope counts (Applied only) ---------------------------
    public int HlodLayerCount => CountApplied(AssignmentType.HLODLayer);
    public int IncludeInHlodCount => CountApplied(AssignmentType.IncludeInHLOD);
    public int DataLayerCount => CountApplied(AssignmentType.DataLayer);
    public int RuntimeGridCount => CountApplied(AssignmentType.RuntimeGrid);
    private int CountApplied(AssignmentType t) =>
        _report?.Records.Count(r => r.Category == RecordCategory.Applied && r.AssignmentType == t) ?? 0;

    // ---- Read workflow -----------------------------------------------------
    public int ReadCount => _report?.Records.Count(r => r.IsRead) ?? 0;

    // Cached count of the filtered view; recomputed once per RebuildPage to avoid
    // re-running the filter predicate over the whole collection on every read.
    private int _showingCount;
    public int ShowingCount => _showingCount;

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(AppliedCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningErrorCount));
        OnPropertyChanged(nameof(ImportErrorCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(AnomalyCount));
        OnPropertyChanged(nameof(ExpectedCount));
        OnPropertyChanged(nameof(KnownNoiseCount));
        OnPropertyChanged(nameof(NeedsReviewCount));
        OnPropertyChanged(nameof(FilterAnomalyCount));
        OnPropertyChanged(nameof(FilterExpectedCount));
        OnPropertyChanged(nameof(FilterKnownNoiseCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HlodLayerCount));
        OnPropertyChanged(nameof(IncludeInHlodCount));
        OnPropertyChanged(nameof(DataLayerCount));
        OnPropertyChanged(nameof(RuntimeGridCount));
        OnPropertyChanged(nameof(ReadCount));
        OnPropertyChanged(nameof(ShowingCount));
        OnPropertyChanged(nameof(AnomalyRecords));
        OnPropertyChanged(nameof(NeedsReviewRecords));
        OnPropertyChanged(nameof(ImportErrorRecords));
        OnPropertyChanged(nameof(AnomalyGroups));
        OnPropertyChanged(nameof(NeedsReviewGroups));
        OnPropertyChanged(nameof(ImportErrorGroups));
    }

    private void RefreshView()
    {
        _view?.Refresh();
        CurrentPage = 1;
        OnPropertyChanged(nameof(ReadCount));
        RebuildPage();
    }

    // ---- Filters -----------------------------------------------------------
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>Active category tab: Applied, WarningsErrors, Skipped.</summary>
    [ObservableProperty] private string _selectedTab = "Applied";

    /// <summary>Assignment scope filter: All, HLODLayer, IncludeInHLOD, DataLayer, RuntimeGrid.</summary>
    [ObservableProperty] private string _selectedScope = "HLODLayer";

    /// <summary>When false, rows marked as read are hidden.</summary>
    [ObservableProperty] private bool _showRead;

    // Extra visibility toggles kept as switches on top of the active tab. All on by default.
    [ObservableProperty] private bool _showAnomalies = true;
    [ObservableProperty] private bool _showExpected = true;
    [ObservableProperty] private bool _showKnownNoise = true;

    public bool IsAppliedTab => SelectedTab == "Applied";

    partial void OnSearchTextChanged(string value) => RefreshView();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    partial void OnSelectedTabChanged(string value)
    {
        if (value != "Applied") SelectedScope = "HLODLayer";   // scope only applies to Applied
        OnPropertyChanged(nameof(IsAppliedTab));
        OnPropertyChanged(nameof(FilterAnomalyCount));
        OnPropertyChanged(nameof(FilterExpectedCount));
        OnPropertyChanged(nameof(FilterKnownNoiseCount));
        RefreshView();
    }

    partial void OnSelectedScopeChanged(string value) => RefreshView();
    partial void OnShowReadChanged(bool value) => RefreshView();
    partial void OnShowAnomaliesChanged(bool value) => RefreshView();
    partial void OnShowExpectedChanged(bool value) => RefreshView();
    partial void OnShowKnownNoiseChanged(bool value) => RefreshView();

    [RelayCommand]
    private void SelectTab(string? tab) => SelectedTab = string.IsNullOrEmpty(tab) ? "All" : tab;

    [RelayCommand]
    private void SelectScope(string? scope) => SelectedScope = string.IsNullOrEmpty(scope) ? "All" : scope;

    [RelayCommand]
    private void MarkPageRead()
    {
        if (PagedRecords.Count == 0) return;
        var count = PagedRecords.Count;
        foreach (var r in PagedRecords.ToList()) r.IsRead = true;
        _log.Info($"Marked {count} row(s) on this page as read.", "Review");
        RefreshView();
    }

    [RelayCommand]
    private void ClearRead()
    {
        if (_report is null) return;
        foreach (var r in _report.Records) r.IsRead = false;
        _log.Info("Cleared all read flags.", "Review");
        RefreshView();
    }

    /// <summary>Toggle the read flag for the current selection (or the clicked row).</summary>
    [RelayCommand]
    private void ToggleRead(RuleRecord? record)
    {
        var targets = TargetRecords(record);
        if (targets.Count == 0) return;

        // Toggle relative to the clicked row so the whole selection ends in the same state.
        var newState = record is not null ? !record.IsRead : !targets[0].IsRead;
        foreach (var r in targets) r.IsRead = newState;

        _log.Info(newState
            ? $"Marked {targets.Count} row(s) as read."
            : $"Marked {targets.Count} row(s) as unread.", "Review");
        RefreshView();
    }

    /// <summary>Raised after a report is persisted. True = approved journal, false = suspicious journal.</summary>
    public event Action<bool>? ReportSaved;

    /// <summary>Ask for a free-text note and persist it (covers the whole selection).</summary>
    [RelayCommand]
    private void ReportSuspicious(RuleRecord? record)
    {
        var targets = TargetRecords(record);
        if (targets.Count == 0 || _report is null) return;

        var title = targets.Count > 1 ? $"Flag {targets.Count} suspicious actors" : "Flag a suspicious actor";
        var prompt = targets.Count > 1
            ? $"Describe why these {targets.Count} actors look wrong:"
            : $"Describe why this looks wrong for \"{targets[0].DisplayActor}\":";
        var initial = record?.SuspiciousComment ?? string.Empty;

        var owner = System.Windows.Application.Current?.MainWindow;
        var result = Services.PromptDialog.ShowWithConfidence(owner, title, prompt, initial);
        if (result is null || string.IsNullOrWhiteSpace(result.Text)) return;
        var comment = result.Text.Trim();

        try
        {
            var store = new SuspiciousReportStore(_settings.AppDataFolder, SuspiciousReportStore.SuspiciousFileName);
            var report = SuspiciousReport.FromRecords(targets, _report, comment);
            report.Confidence = result.Confidence;
            store.Add(report);
            foreach (var r in targets) { r.IsReported = true; r.SuspiciousComment = comment; }
            _log.Info($"Flagged {targets.Count} actor(s) as suspicious -> {store.FilePath}", "Review");
            RefreshView();
            ReportSaved?.Invoke(false);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to save the suspicious report: {ex.Message}", "Review");
        }
    }

    /// <summary>Ask why the operation is valid and persist it (covers the whole selection).</summary>
    [RelayCommand]
    private void Approve(RuleRecord? record)
    {
        var targets = TargetRecords(record);
        if (targets.Count == 0 || _report is null) return;

        var title = targets.Count > 1 ? $"Approve {targets.Count} operations" : "Approve this operation";
        var prompt = targets.Count > 1
            ? $"Explain why these {targets.Count} assignments are correct:"
            : $"Explain why this assignment is correct for \"{targets[0].DisplayActor}\":";
        var initial = record?.ApprovalComment ?? string.Empty;

        var owner = System.Windows.Application.Current?.MainWindow;
        var result = Services.PromptDialog.ShowWithConfidence(owner, title, prompt, initial);
        if (result is null || string.IsNullOrWhiteSpace(result.Text)) return;
        var comment = result.Text.Trim();

        try
        {
            var store = new SuspiciousReportStore(_settings.AppDataFolder, SuspiciousReportStore.ApprovedFileName);
            var report = SuspiciousReport.FromRecords(targets, _report, comment);
            report.Confidence = result.Confidence;
            store.Add(report);
            foreach (var r in targets) { r.IsApproved = true; r.ApprovalComment = comment; }
            _log.Info($"Approved {targets.Count} operation(s) -> {store.FilePath}", "Review");
            RefreshView();
            ReportSaved?.Invoke(true);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to save the approval: {ex.Message}", "Review");
        }
    }

    /// <summary>Reset the scope and all value filters to their default (everything visible).</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var f in AssignmentFilters) f.Reset();
        SelectedScope = "All";
        RefreshView();
        _log.Info("Filters cleared.", "Review");
    }

    private bool FilterRecord(object obj)
    {
        if (obj is not RuleRecord r) return false;

        // Read workflow: hide read rows unless "Show read" is on.
        if (r.IsRead && !ShowRead) return false;

        // Status visibility. Anomalies also covers "needs review" so the three toggles fully
        // partition the current tab (Anomaly + NeedsReview + Expected + KnownNoise = tab total).
        if (r.Status is ReviewStatus.Anomaly or ReviewStatus.NeedsReview && !ShowAnomalies) return false;
        if (r.Status == ReviewStatus.Expected && !ShowExpected) return false;
        if (r.Status == ReviewStatus.KnownNoise && !ShowKnownNoise) return false;

        var tabOk = SelectedTab switch
        {
            "Applied" => r.Category == RecordCategory.Applied,
            "WarningsErrors" => r.Category is RecordCategory.Warning or RecordCategory.Error,
            "Skipped" => r.Category == RecordCategory.Skipped,
            _ => true
        };
        if (!tabOk) return false;

        // Scope (assignment type) only makes sense on the Applied tab.
        if (r.Category == RecordCategory.Applied)
        {
            var scopeOk = SelectedScope switch
            {
                "HLODLayer" => r.AssignmentType == AssignmentType.HLODLayer,
                "IncludeInHLOD" => r.AssignmentType == AssignmentType.IncludeInHLOD,
                "DataLayer" => r.AssignmentType == AssignmentType.DataLayer,
                "RuntimeGrid" => r.AssignmentType == AssignmentType.RuntimeGrid,
                _ => true
            };
            if (!scopeOk) return false;
        }

        // Value-level filters only apply to Applied rows (others have no assignment type).
        if (r.Category == RecordCategory.Applied)
        {
            foreach (var f in AssignmentFilters)
                if (!f.Allows(r)) return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim();
            var hit = r.DisplayActor.Contains(s, StringComparison.OrdinalIgnoreCase)
                      || r.Value.Contains(s, StringComparison.OrdinalIgnoreCase)
                      || (r.StatusReason?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (r.Reason?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hit) return false;
        }
        return true;
    }

    // ---- AI ----------------------------------------------------------------
    [ObservableProperty] private string _aiReportText = string.Empty;
    [ObservableProperty] private string? _aiError;
    [ObservableProperty] private bool _isAiBusy;
    public bool CanUseAi => _ai.IsAvailable;

    /// <summary>Safety cap on unread rows considered in one auto-resolve pass. High on purpose: the AI
    /// now returns match rules (not per-row indices), and the rows live in a file, so all candidates can
    /// be covered without bloating the prompt.</summary>
    private const int MaxAutoResolveCandidates = 20000;

    /// <summary>
    /// Open the "Auto-resolve reading" dialog: confront the displayed unread rows with the saved
    /// approved/suspicious reports through the AI, preview the proposed groups, then mark as read.
    /// </summary>
    [RelayCommand]
    private void AutoResolveReading()
    {
        if (_report is null) return;

        if (!_ai.IsAvailable)
        {
            _log.Warning("AI is not available. Enable it and set the Cursor agent path in Settings > AI.", "AI");
            return;
        }

        // Resolve across ALL applied assignment types (HLODLayer, IncludeInHLOD, DataLayer,
        // RuntimeGrid), independent of the current tab/scope/filters.
        AutoResolveViewModel vm;
        if (_autoResolveVm is not null && _autoResolveVm.HasPreviousResults)
        {
            // Reopen straight on the last collected plan, pruning rows read in the meantime.
            vm = _autoResolveVm;
            vm.RestorePreviousResults();
        }
        else
        {
            var candidates = _report.Records
                .Where(r => r.Category == RecordCategory.Applied && !r.IsRead)
                .Take(MaxAutoResolveCandidates)
                .ToList();

            var reports = LoadKnownReports();
            vm = new AutoResolveViewModel(_report, candidates, reports, _ai, _settings, _log);
            _autoResolveVm = vm;
        }

        var dialog = new Views.AutoResolveDialog
        {
            DataContext = vm,
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dialog.ShowDialog();

        // Reveal the "AI check" column only once the user actually marked selected rows as read.
        if (vm.AppliedCount > 0)
            AiReviewApplied = true;

        // Refresh when rows were marked read, or when the AI attached "AI Check" notes to deferred rows.
        if (vm.AppliedCount > 0 || vm.DeferredCount > 0)
            RefreshView();
    }

    /// <summary>Show the AI's "I leave this to the human" reasoning attached to a row (AI Check button).</summary>
    [RelayCommand]
    private void ShowAiThought(RuleRecord? record)
    {
        if (record is null || !record.HasAiReviewNote) return;

        var dialog = new Views.AiThoughtDialog(record, LoadKnownReports())
        {
            Owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    /// <summary>Cached across dialog sessions so reopening lands on the last collected plan.</summary>
    private AutoResolveViewModel? _autoResolveVm;

    private IReadOnlyList<AutoResolveKnownReport> LoadKnownReports()
    {
        var list = new List<AutoResolveKnownReport>();

        void AddAll(string fileName, string kind, string prefix)
        {
            var store = new SuspiciousReportStore(_settings.AppDataFolder, fileName);
            int n = 1;
            foreach (var r in store.Load())
            {
                var actors = (r.Actors.Count > 0
                        ? r.Actors.Select(a => string.IsNullOrWhiteSpace(a.Value)
                            ? $"{a.ActorPath} | {a.AssignmentType}"
                            : $"{a.ActorPath} | {a.AssignmentType}={a.Value}")
                        : new[] { $"{r.ActorPath} | {r.AssignmentType}={r.Value}" })
                    .ToList();

                list.Add(new AutoResolveKnownReport
                {
                    Id = $"{prefix}{n++}",
                    Kind = kind,
                    Confidence = r.Confidence,
                    Comment = r.Comment,
                    Actors = actors,
                    Source = r
                });
            }
        }

        AddAll(SuspiciousReportStore.ApprovedFileName, "APPROVED", "A");
        AddAll(SuspiciousReportStore.SuspiciousFileName, "SUSPICIOUS", "S");
        return list;
    }

    [RelayCommand]
    private async Task RunAiAsync()
    {
        if (_report is null || IsAiBusy) return;
        IsAiBusy = true;
        AiError = null;
        try
        {
            var result = await _ai.GenerateReportAsync(_report);
            if (result.Success) AiReportText = result.Content;
            else AiError = result.Error;
        }
        catch (Exception ex) { AiError = ex.Message; }
        finally { IsAiBusy = false; }
    }

    public bool HasAiReport => !string.IsNullOrWhiteSpace(AiReportText);
    partial void OnAiReportTextChanged(string value) => OnPropertyChanged(nameof(HasAiReport));

    [RelayCommand]
    private void CopyAiMarkdown()
    {
        if (string.IsNullOrWhiteSpace(AiReportText)) return;
        TrySetClipboard(AiReportText);
        _log.Info("AI report copied as Markdown.", "AI");
    }

    [RelayCommand]
    private void CopyAiText()
    {
        if (string.IsNullOrWhiteSpace(AiReportText)) return;
        TrySetClipboard(MarkdownToPlainText(AiReportText));
        _log.Info("AI report copied as plain text.", "AI");
    }

    // ---- AI: Assignment analysis ------------------------------------------
    [ObservableProperty] private string _assignmentReportText = string.Empty;
    [ObservableProperty] private string? _assignmentError;
    [ObservableProperty] private bool _isAssignmentBusy;

    public bool HasAssignmentReport => !string.IsNullOrWhiteSpace(AssignmentReportText);
    partial void OnAssignmentReportTextChanged(string value) => OnPropertyChanged(nameof(HasAssignmentReport));

    [RelayCommand]
    private async Task RunAssignmentAnalysisAsync()
    {
        if (_report is null || IsAssignmentBusy) return;
        IsAssignmentBusy = true;
        AssignmentError = null;
        try
        {
            var result = await _ai.GenerateAssignmentAnalysisAsync(_report);
            if (result.Success) AssignmentReportText = result.Content;
            else AssignmentError = result.Error;
        }
        catch (Exception ex) { AssignmentError = ex.Message; }
        finally { IsAssignmentBusy = false; }
    }

    [RelayCommand]
    private void CopyAssignmentMarkdown()
    {
        if (string.IsNullOrWhiteSpace(AssignmentReportText)) return;
        TrySetClipboard(AssignmentReportText);
        _log.Info("Assignment analysis copied as Markdown.", "AI");
    }

    [RelayCommand]
    private void CopyAssignmentText()
    {
        if (string.IsNullOrWhiteSpace(AssignmentReportText)) return;
        TrySetClipboard(MarkdownToPlainText(AssignmentReportText));
        _log.Info("Assignment analysis copied as plain text.", "AI");
    }

    /// <summary>Best-effort conversion of the Markdown report to readable plain text.</summary>
    private static string MarkdownToPlainText(string md)
    {
        var text = md;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^```.*$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);            // fenced code markers
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s*", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);            // headings
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)"); // links
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*[-*+]\s+", "\u2022 ",
            System.Text.RegularExpressions.RegexOptions.Multiline);            // bullets
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*>\s?", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);            // blockquotes
        text = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
        return text.Trim();
    }

    // ---- Copy / export -----------------------------------------------------
    [RelayCommand]
    private void CopyMarkdown()
    {
        if (_report is null) return;
        TrySetClipboard(ReportBuilder.BuildAnomalyMarkdown(_report, ShowExpected));
        _log.Info("Report (Markdown) copied to clipboard.", "Report");
    }

    [RelayCommand]
    private void CopyVisible()
    {
        if (_view is null) return;
        var visible = _view.Cast<RuleRecord>();
        TrySetClipboard(ReportBuilder.BuildPlainText(visible));
        _log.Info("Visible rows copied to clipboard.", "Report");
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (_report is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"{SanitizeName(_report.SessionName)}_review.csv",
            InitialDirectory = ResolveExportFolder()
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, ReportBuilder.BuildCsv(_report.Records, _settings.CsvDelimiter));
            _log.Success($"CSV exported to {dlg.FileName}", "Report");
        }
        catch (Exception ex) { _log.Error($"CSV export failed: {ex.Message}", "Report"); }
    }

    [RelayCommand]
    private void ExportMarkdown()
    {
        if (_report is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Markdown file (*.md)|*.md",
            FileName = $"{SanitizeName(_report.SessionName)}_review.md",
            InitialDirectory = ResolveExportFolder()
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, ReportBuilder.BuildAnomalyMarkdown(_report, ShowExpected));
            _log.Success($"Markdown exported to {dlg.FileName}", "Report");
        }
        catch (Exception ex) { _log.Error($"Markdown export failed: {ex.Message}", "Report"); }
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        var path = _report?.SourcePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _log.Warning("Downloaded log file was not found on disk.", "Report");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
            _log.Info($"Revealed in Explorer: {path}", "Report");
        }
        catch (Exception ex) { _log.Error($"Could not open Explorer: {ex.Message}", "Report"); }
    }

    private string ResolveExportFolder() =>
        string.IsNullOrWhiteSpace(_settings.ExportFolder)
            ? Core.Persistence.AppPaths.Reports
            : _settings.ExportFolder;

    private static string SanitizeName(string s) =>
        string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));

    private static void TrySetClipboard(string text)
    {
        try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); }
        catch { /* clipboard busy */ }
    }
}

/// <summary>
/// One "problem" in Smart Analysis: identical triage reason shared by many actors/assets.
/// Expandable to reveal every affected record.
/// </summary>
public sealed class InsightGroup
{
    public InsightGroup(string title, IReadOnlyList<RuleRecord> items, AnomalySeverity severity)
    {
        Title = title;
        Items = items;
        Severity = severity;
    }

    public string Title { get; }
    public IReadOnlyList<RuleRecord> Items { get; }
    public AnomalySeverity Severity { get; }
    public int Count => Items.Count;
}

/// <summary>One selectable value inside an <see cref="AssignmentValueFilter"/> (e.g. "DL_LIGHTING" with a count).</summary>
public sealed partial class ValueFilterItem : ObservableObject
{
    private readonly Action _onChanged;

    public ValueFilterItem(string value, string display, int count, Action onChanged)
    {
        Value = value;
        Display = display;
        Count = count;
        _onChanged = onChanged;
    }

    /// <summary>Raw log value used for matching (may be empty for "None").</summary>
    public string Value { get; }

    /// <summary>User-facing label (empty value shown as "None").</summary>
    public string Display { get; }

    public int Count { get; }

    [ObservableProperty] private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value) => _onChanged();
}

/// <summary>
/// Value-level filter for one assignment type (HLODLayer / IncludeInHLOD / DataLayer / RuntimeGrid),
/// mirroring the "Filter by ..." checkbox dropdowns of the original web report.
/// </summary>
public sealed partial class AssignmentValueFilter : ObservableObject
{
    private readonly Action _onChanged;
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppress;

    public AssignmentValueFilter(string title, AssignmentType type, Action onChanged)
    {
        Title = title;
        Type = type;
        _onChanged = onChanged;
    }

    public string Title { get; }
    public AssignmentType Type { get; }
    public ObservableCollection<ValueFilterItem> Items { get; } = new();

    public string ButtonLabel => $"Filter by {Title}";
    public bool HasValues => Items.Count > 0;

    /// <summary>True when at least one value is unchecked (the filter is narrowing the view).</summary>
    public bool IsActive => _excluded.Count > 0;

    public void SetItems(IEnumerable<ValueFilterItem> items)
    {
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        RebuildExcluded();
        RaiseState();
    }

    /// <summary>Callback handed to every child item so a checkbox toggle refreshes the view.</summary>
    public void NotifyItemChanged()
    {
        RebuildExcluded();
        RaiseState();
        if (!_suppress) _onChanged();
    }

    /// <summary>A record passes this filter unless it is of this type and its value is unchecked (O(1) lookup).</summary>
    public bool Allows(RuleRecord r)
    {
        if (_excluded.Count == 0 || r.AssignmentType != Type) return true;
        return !_excluded.Contains(r.Value ?? string.Empty);
    }

    [RelayCommand] private void SelectAll() => SetAll(true);
    [RelayCommand] private void SelectNone() => SetAll(false);

    /// <summary>Re-check every value without firing the change callback (caller refreshes once).</summary>
    public void Reset()
    {
        _suppress = true;
        foreach (var i in Items) i.IsChecked = true;
        _suppress = false;
        RebuildExcluded();
        RaiseState();
    }

    private void SetAll(bool value)
    {
        _suppress = true;
        foreach (var i in Items) i.IsChecked = value;
        _suppress = false;
        RebuildExcluded();
        RaiseState();
        _onChanged();
    }

    private void RebuildExcluded()
    {
        _excluded.Clear();
        foreach (var i in Items)
            if (!i.IsChecked) _excluded.Add(i.Value);
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(HasValues));
    }
}
