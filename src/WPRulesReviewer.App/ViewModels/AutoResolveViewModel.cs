using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>
/// Drives the "Auto-resolve reading" dialog: it confronts every unread applied assignment with the
/// human review reports (approved / suspicious) using the AI agent, then previews the proposed rows
/// grouped by assignment category so the reviewer can pick which ones to mark as read.
/// </summary>
public sealed partial class AutoResolveViewModel : ObservableObject
{
    private static readonly string[] CategoryOrder = { "HLODLayer", "IncludeInHLOD", "DataLayer", "RuntimeGrid" };

    private readonly SessionReport _report;
    private readonly IReadOnlyList<RuleRecord> _candidateRecords;
    private readonly IReadOnlyList<AutoResolveKnownReport> _reports;
    private readonly IAiAgentService _ai;
    private readonly AppSettings _settings;
    private readonly IActivityLog _log;

    /// <summary>Raised when the dialog should close (after applying, or on request).</summary>
    public event Action? RequestClose;

    /// <summary>Shared app settings (used by the dialog to persist its window bounds).</summary>
    public AppSettings Settings => _settings;

    private string _prompt = string.Empty;

    /// <summary>Candidate DTOs aligned by Index with <see cref="_candidateRecords"/>; used for rule matching.</summary>
    private IReadOnlyList<AutoResolveCandidate> _candidates = System.Array.Empty<AutoResolveCandidate>();

    public AutoResolveViewModel(SessionReport report, IReadOnlyList<RuleRecord> candidateRecords,
        IReadOnlyList<AutoResolveKnownReport> reports, IAiAgentService ai, AppSettings settings, IActivityLog log)
    {
        _report = report;
        _candidateRecords = candidateRecords;
        _reports = reports;
        _ai = ai;
        _settings = settings;
        _log = log;

        PrepareInputFiles();
    }

    // ---- Local artifacts (rows data / prompt / AI log) ---------------------
    [ObservableProperty] private string _rowsFilePath = string.Empty;
    [ObservableProperty] private string _promptFilePath = string.Empty;
    [ObservableProperty] private string _aiLogFilePath = string.Empty;
    [ObservableProperty] private string _reasoningFilePath = string.Empty;

    public bool HasAiLog => !string.IsNullOrEmpty(AiLogFilePath) && File.Exists(AiLogFilePath);
    partial void OnAiLogFilePathChanged(string value) => OnPropertyChanged(nameof(HasAiLog));

    public bool HasReasoning => !string.IsNullOrEmpty(ReasoningFilePath) && File.Exists(ReasoningFilePath);
    partial void OnReasoningFilePathChanged(string value) => OnPropertyChanged(nameof(HasReasoning));

    private string ArtifactsFolder =>
        Path.Combine(string.IsNullOrWhiteSpace(_settings.AppDataFolder) ? @"D:\WorldPartitionRules\" : _settings.AppDataFolder,
            "AutoResolve");

    /// <summary>Build the single prompt sent to the AI and write the rows + prompt to disk up-front,
    /// so the reviewer can inspect exactly what will be analysed.</summary>
    private void PrepareInputFiles()
    {
        var candidates = _candidateRecords
            .Select((r, i) => new AutoResolveCandidate
            {
                Index = i,
                ActorPath = r.DisplayActor,
                AssignmentType = r.CategoryLabel,
                Value = r.Value,
                Category = r.Category.ToString(),
                Status = r.Status.ToString(),
                Reason = r.StatusReason ?? r.Reason
            })
            .ToList();

        _candidates = candidates;

        // The rows file is written first so the prompt can reference it by path instead of inlining the data.
        var rowsPath = Path.Combine(ArtifactsFolder, "AutoResolve_Rows.txt");
        RowsFilePath = rowsPath;

        _prompt = AutoResolvePromptBuilder.Build(_report, _reports, candidates, _settings, rowsPath);

        try
        {
            Directory.CreateDirectory(ArtifactsFolder);

            var sb = new StringBuilder();
            sb.Append("Auto-resolve reading - input rows for session '").Append(_report.SessionName).AppendLine("'.");
            sb.Append("Unread applied assignments across all categories: ").Append(candidates.Count).AppendLine();
            sb.AppendLine("Columns: index | assignmentType=value | category | status | actorPath | reason");
            sb.AppendLine(new string('-', 80));
            foreach (var c in candidates)
            {
                sb.Append(c.Index).Append(" | ")
                  .Append(c.AssignmentType).Append('=').Append(c.Value).Append(" | ")
                  .Append(c.Category).Append(" | ")
                  .Append(c.Status).Append(" | ")
                  .Append(c.ActorPath);
                if (!string.IsNullOrWhiteSpace(c.Reason)) sb.Append(" | ").Append(c.Reason);
                sb.AppendLine();
            }
            File.WriteAllText(rowsPath, sb.ToString());

            var promptPath = Path.Combine(ArtifactsFolder, "AutoResolve_Prompt.txt");
            File.WriteAllText(promptPath, _prompt);
            PromptFilePath = promptPath;
        }
        catch (Exception ex)
        {
            _log.Warning($"Auto-resolve: could not write input files: {ex.Message}", "AI");
        }
    }

    [RelayCommand] private void OpenRowsFile() => OpenInShell(RowsFilePath);
    [RelayCommand] private void OpenPromptFile() => OpenInShell(PromptFilePath);
    [RelayCommand] private void OpenAiLog() => OpenInShell(AiLogFilePath);
    [RelayCommand] private void OpenReasoning() => OpenInShell(ReasoningFilePath);

    private void OpenInShell(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"Could not open '{path}': {ex.Message}", "AI");
        }
    }

    // ---- Phases ------------------------------------------------------------
    public enum ResolvePhase { Intro, Running, Results, Error }

    [ObservableProperty] private ResolvePhase _phase = ResolvePhase.Intro;

    partial void OnPhaseChanged(ResolvePhase value)
    {
        OnPropertyChanged(nameof(IsIntro));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsResults));
        OnPropertyChanged(nameof(IsError));
    }

    public bool IsIntro => Phase == ResolvePhase.Intro;
    public bool IsRunning => Phase == ResolvePhase.Running;
    public bool IsResults => Phase == ResolvePhase.Results;
    public bool IsError => Phase == ResolvePhase.Error;

    // ---- Intro info --------------------------------------------------------
    public int CandidateCount => _candidateRecords.Count;
    public int ReportCount => _reports.Count;
    public string SessionName => _report.SessionName;

    [ObservableProperty] private string _statusText = "Confronting every unread assignment with your review reports...";
    [ObservableProperty] private string _errorText = string.Empty;

    /// <summary>Live agent output shown while the analysis runs, so the user sees the reasoning stream.</summary>
    [ObservableProperty] private string _streamText = string.Empty;

    public bool HasStreamText => !string.IsNullOrEmpty(StreamText);
    partial void OnStreamTextChanged(string value) => OnPropertyChanged(nameof(HasStreamText));

    // ---- Results -----------------------------------------------------------
    public ObservableCollection<AutoResolveGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private string _resultsSummary = string.Empty;

    /// <summary>Filters the results preview by actor path/name, assignment or rationale.</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void ApplyFilter()
    {
        var text = (SearchText ?? string.Empty).Trim();
        bool all = text.Length == 0;
        foreach (var g in Groups)
        {
            int visible = 0;
            foreach (var it in g.Items)
            {
                bool ok = all
                    || it.Actor.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || it.Record.ActorName.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || it.Assignment.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || (it.Rationale?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
                it.MatchesFilter = ok;
                if (ok) visible++;
            }
            g.IsVisibleInFilter = visible > 0;
        }
    }

    /// <summary>Notice shown when rows were marked read manually since the last analysis.</summary>
    [ObservableProperty] private string _manualNoticeText = string.Empty;

    public bool HasManualNotice => !string.IsNullOrEmpty(ManualNoticeText);
    partial void OnManualNoticeTextChanged(string value) => OnPropertyChanged(nameof(HasManualNotice));

    /// <summary>True once a plan has been collected, so the dialog can be reopened on the results.</summary>
    public bool HasPreviousResults => Phase == ResolvePhase.Results;

    public bool HasGroups => Groups.Count > 0;

    /// <summary>Total actors selected (checked) across all groups.</summary>
    public int SelectedTotal => Groups.Sum(g => g.SelectedCount);

    [ObservableProperty] private int _appliedCount;

    private void OnItemSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedTotal));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    // ---- Commands ----------------------------------------------------------
    [RelayCommand]
    private async Task RunAsync()
    {
        if (Phase == ResolvePhase.Running) return;

        if (CandidateCount == 0)
        {
            ErrorText = "There are no unread applied assignments to resolve.";
            Phase = ResolvePhase.Error;
            return;
        }
        if (ReportCount == 0)
        {
            ErrorText = "There are no approved or suspicious reports yet. Flag or approve some actors first, "
                        + "so the AI has a knowledge base to resolve against.";
            Phase = ResolvePhase.Error;
            return;
        }

        Phase = ResolvePhase.Running;
        StreamText = string.Empty;
        StatusText = "The AI agent is reading your reports and confronting every unread assignment. "
                     + "Its live reasoning appears below.";

        // Progress captures the UI SynchronizationContext, so appends marshal back to the UI thread.
        // Chunks are raw text deltas from the agent, so append verbatim (no extra newlines).
        var sb = new StringBuilder();
        var progress = new Progress<string>(chunk =>
        {
            sb.Append(chunk);
            StreamText = sb.ToString();
        });

        try
        {
            var result = await _ai.CompleteStreamingAsync(_prompt, progress).ConfigureAwait(true);

            WriteAiLog(result);
            WriteReasoningLog(result);

            if (!result.Success)
            {
                ErrorText = result.Error ?? "The AI agent returned no result.";
                Phase = ResolvePhase.Error;
                return;
            }

            var plan = AutoResolveResultParser.Parse(result.Content);
            BuildGroups(plan);

            var actors = SelectedTotal;
            ManualNoticeText = string.Empty;
            UpdateResultsSummary();
            Phase = ResolvePhase.Results;
            _log.Info($"Auto-resolve: AI proposed {actors} assignment(s) in {Groups.Count} category(ies).", "AI");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            Phase = ResolvePhase.Error;
        }
    }

    /// <summary>Persist the raw AI output (its reasoning/answer) so the reviewer can inspect it.</summary>
    private void WriteAiLog(AiReportResult result)
    {
        try
        {
            Directory.CreateDirectory(ArtifactsFolder);
            var path = Path.Combine(ArtifactsFolder, "AutoResolve_AiLog.txt");
            var sb = new StringBuilder();
            sb.Append("Auto-resolve reading - AI processing log for session '").Append(_report.SessionName).AppendLine("'.");
            sb.Append("Generated: ").AppendLine(DateTimeOffset.Now.ToString("u"));
            sb.Append("Success: ").AppendLine(result.Success.ToString());
            sb.AppendLine(new string('-', 80));
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                sb.AppendLine("ERROR:");
                sb.AppendLine(result.Error);
                sb.AppendLine();
            }
            sb.AppendLine("AI OUTPUT:");
            sb.AppendLine(string.IsNullOrWhiteSpace(result.Content) ? "(empty)" : result.Content);
            File.WriteAllText(path, sb.ToString());
            AiLogFilePath = path;
        }
        catch (Exception ex)
        {
            _log.Warning($"Auto-resolve: could not write AI log: {ex.Message}", "AI");
        }
    }

    /// <summary>Persist the human-readable part of the agent's answer (the narrative before the JSON).</summary>
    private void WriteReasoningLog(AiReportResult result)
    {
        if (!result.Success) return;
        var narrative = ExtractNarrative(result.Content);
        if (string.IsNullOrWhiteSpace(narrative)) return;

        try
        {
            Directory.CreateDirectory(ArtifactsFolder);
            var path = Path.Combine(ArtifactsFolder, "AutoResolve_Reasoning.txt");
            var sb = new StringBuilder();
            sb.Append("Auto-resolve reading - AI reasoning (human-readable) for session '")
              .Append(_report.SessionName).AppendLine("'.");
            sb.Append("Generated: ").AppendLine(DateTimeOffset.Now.ToString("u"));
            sb.AppendLine(new string('-', 80));
            sb.AppendLine(narrative);
            File.WriteAllText(path, sb.ToString());
            ReasoningFilePath = path;
        }
        catch (Exception ex)
        {
            _log.Warning($"Auto-resolve: could not write reasoning log: {ex.Message}", "AI");
        }
    }

    /// <summary>Strip the trailing machine-readable JSON block, keeping the plain-English narrative.</summary>
    private static string ExtractNarrative(string full)
    {
        if (string.IsNullOrWhiteSpace(full)) return string.Empty;
        int fence = full.IndexOf("```", StringComparison.Ordinal);
        var narrative = fence >= 0 ? full.Substring(0, fence) : full;
        return narrative.Trim();
    }

    /// <summary>Actors the AI examined but deliberately left to the human (annotated with an AI note).</summary>
    [ObservableProperty] private int _deferredCount;

    private void BuildGroups(AutoResolvePlan plan)
    {
        Groups.Clear();
        var reportsById = _reports.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

        AnnotateDeferrals(plan, reportsById);

        // Merge the AI decisions per record: a single row can be backed by several reports.
        var resolved = new Dictionary<int, ResolvedAccum>();
        foreach (var g in plan.Groups)
        {
            var refs = g.ReportIds
                .Select(id => reportsById.TryGetValue(id, out var rep) ? rep : null)
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();

            // Expand the group deterministically: the AI's rule is applied to every candidate so no
            // matching row is missed, then unioned with any explicit indices the AI may have listed.
            var indices = new HashSet<int>(g.Indices);
            foreach (var idx in AutoResolveMatcher.MatchingIndices(g.Match, _candidates))
                indices.Add(idx);

            foreach (var idx in indices)
            {
                if (idx < 0 || idx >= _candidateRecords.Count) continue;
                if (!resolved.TryGetValue(idx, out var acc))
                {
                    acc = new ResolvedAccum { Rationale = g.Rationale, Confidence = g.Confidence };
                    resolved[idx] = acc;
                }
                else if (g.Confidence > acc.Confidence)
                {
                    acc.Confidence = g.Confidence;
                    if (!string.IsNullOrWhiteSpace(g.Rationale)) acc.Rationale = g.Rationale;
                }

                foreach (var rep in refs)
                    if (!acc.Reports.Any(x => string.Equals(x.Id, rep.Id, StringComparison.OrdinalIgnoreCase)))
                        acc.Reports.Add(rep);
            }
        }

        // Group by assignment category in a fixed, meaningful order.
        foreach (var cat in CategoryOrder)
        {
            var items = resolved
                .Where(kv => string.Equals(_candidateRecords[kv.Key].CategoryLabel, cat, StringComparison.OrdinalIgnoreCase))
                .Select(kv => new AutoResolveItemViewModel(_candidateRecords[kv.Key], kv.Value))
                .OrderBy(i => i.Actor, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (items.Count == 0) continue;

            int total = _candidateRecords.Count(r =>
                string.Equals(r.CategoryLabel, cat, StringComparison.OrdinalIgnoreCase));

            Groups.Add(new AutoResolveGroupViewModel(cat, items, OnItemSelectionChanged, total));
        }

        ApplyFilter();
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(SelectedTotal));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Attach the AI's per-actor "I leave this to the human" reasoning to the matching records so the
    /// main grid can surface it via the "AI Check" button. Records auto-resolved into a read group take
    /// precedence and are never annotated as deferred.
    /// </summary>
    private void AnnotateDeferrals(AutoResolvePlan plan, IReadOnlyDictionary<string, AutoResolveKnownReport> reportsById)
    {
        // Reset any note from a previous run so stale reasoning never lingers.
        foreach (var r in _candidateRecords)
        {
            r.AiReviewNote = null;
            r.AiReviewReportIds = System.Array.Empty<string>();
            r.AiReviewConfidence = 0;
        }

        // Rows the AI auto-resolves take priority; never annotate them as "deferred".
        var resolvedIdx = new HashSet<int>();
        foreach (var g in plan.Groups)
        {
            resolvedIdx.UnionWith(g.Indices);
            foreach (var idx in AutoResolveMatcher.MatchingIndices(g.Match, _candidates))
                resolvedIdx.Add(idx);
        }

        int annotated = 0;
        foreach (var g in plan.Deferrals)
        {
            var idx = new HashSet<int>(g.Indices);
            foreach (var i in AutoResolveMatcher.MatchingIndices(g.Match, _candidates))
                idx.Add(i);

            var reportIds = g.ReportIds
                .Where(id => reportsById.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var i in idx)
            {
                if (i < 0 || i >= _candidateRecords.Count) continue;
                if (resolvedIdx.Contains(i)) continue;      // resolved wins over deferred

                var rec = _candidateRecords[i];
                if (rec.HasAiReviewNote) continue;          // first deferral wins (stable)

                rec.AiReviewNote = g.Rationale;
                rec.AiReviewReportIds = reportIds;
                rec.AiReviewConfidence = g.Confidence;
                annotated++;
            }
        }

        DeferredCount = annotated;
    }

    private void UpdateResultsSummary()
    {
        ResultsSummary = HasGroups
            ? $"The AI proposes to mark {SelectedTotal} assignment(s) as read across {Groups.Count} category(ies), "
              + "based on your existing reports. Review and uncheck anything you want to keep unread."
            : "The AI did not find any unread assignment that is clearly covered by an existing report. "
              + "Nothing will be marked as read.";
    }

    /// <summary>
    /// Re-open on the previously collected plan: drop rows that were marked read in the meantime
    /// (manually or by a previous apply) and surface a notice explaining they were removed.
    /// </summary>
    public void RestorePreviousResults()
    {
        if (Phase != ResolvePhase.Results) return;

        AppliedCount = 0;

        int removed = 0;
        for (int i = Groups.Count - 1; i >= 0; i--)
        {
            removed += Groups[i].RemoveReadItems();
            if (Groups[i].Count == 0) Groups.RemoveAt(i);
        }

        ManualNoticeText = removed > 0
            ? $"{removed} row(s) were marked as read manually since the last analysis and were removed from these results."
            : string.Empty;

        if (removed > 0)
            _log.Info($"Auto-resolve: removed {removed} row(s) already read since the last analysis.", "AI");

        ApplyFilter();
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(SelectedTotal));
        UpdateResultsSummary();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() => SelectedTotal > 0;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        int count = 0;
        foreach (var group in Groups)
            foreach (var item in group.Items)
                if (item.IsChecked)
                {
                    item.Record.IsRead = true;
                    count++;
                }

        AppliedCount = count;
        _log.Success($"Auto-resolve: marked {count} assignment(s) as read.", "Review");
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    private void SetAll(bool value)
    {
        foreach (var g in Groups) g.SetAll(value);
        OnItemSelectionChanged();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}

/// <summary>The AI's merged decision attached to one resolved record (may cite several reports).</summary>
public sealed class ResolvedAccum
{
    public string Rationale { get; set; } = string.Empty;
    public int Confidence { get; set; } = 3;
    public List<AutoResolveKnownReport> Reports { get; } = new();
}

/// <summary>A category cluster (HLODLayer / IncludeInHLOD / DataLayer / RuntimeGrid) of resolvable rows.</summary>
public sealed partial class AutoResolveGroupViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private bool _suppress;

    public AutoResolveGroupViewModel(string category, IReadOnlyList<AutoResolveItemViewModel> items, Action onChanged,
        int totalCandidates)
    {
        Category = category;
        Items = new ObservableCollection<AutoResolveItemViewModel>(items);
        _onChanged = onChanged;
        TotalCandidates = totalCandidates;

        foreach (var it in Items) it.SelectionChanged += OnItemChanged;
        _isSelected = true;
        _isExpanded = false;
    }

    public string Category { get; }
    public ObservableCollection<AutoResolveItemViewModel> Items { get; }

    /// <summary>Total unread candidates of this category that were sent to the AI.</summary>
    public int TotalCandidates { get; }

    /// <summary>Candidates of this category the AI did not cover (left unread).</summary>
    public int NotTreatedCount => System.Math.Max(0, TotalCandidates - Count);
    public bool HasNotTreated => NotTreatedCount > 0;
    public string NotTreatedLabel => $"{NotTreatedCount} not covered by AI";

    /// <summary>False when the search box hides every item in this group.</summary>
    [ObservableProperty] private bool _isVisibleInFilter = true;

    [ObservableProperty] private bool _isExpanded;

    /// <summary>Group check state; toggling it cascades to every row.</summary>
    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (_suppress) return;
        SetAll(value);
        _onChanged();
    }

    public int Count => Items.Count;
    public int SelectedCount => Items.Count(i => i.IsChecked);
    public string SelectionLabel => $"{SelectedCount}/{Count} selected";

    public void SetAll(bool value)
    {
        _suppress = true;
        foreach (var it in Items) it.IsChecked = value;
        IsSelected = value;
        _suppress = false;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionLabel));
    }

    /// <summary>Copy this category's preview (currently visible rows) to the clipboard as plain text.</summary>
    [RelayCommand]
    private void CopyPreview()
    {
        var rows = Items.Where(i => i.MatchesFilter).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append(Category).Append("  (").Append(rows.Count).AppendLine(" rows)");
        foreach (var it in rows)
        {
            sb.Append("- ").Append(it.Actor).Append("  |  ").Append(it.Assignment);
            if (it.Reports.Count > 0)
                sb.Append("  |  reports: ").Append(string.Join(", ", it.Reports.Select(r => r.Id)));
            sb.Append("  |  AI ").Append(it.Confidence).Append("/5");
            if (!string.IsNullOrWhiteSpace(it.Rationale)) sb.Append("  |  ").Append(it.Rationale);
            sb.AppendLine();
        }
        try { System.Windows.Clipboard.SetText(sb.ToString()); } catch { /* clipboard busy */ }
    }

    /// <summary>Drop rows whose record has been marked read since the results were collected.</summary>
    public int RemoveReadItems()
    {
        int removed = 0;
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Record.IsRead)
            {
                Items[i].SelectionChanged -= OnItemChanged;
                Items.RemoveAt(i);
                removed++;
            }
        }
        if (removed > 0)
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(NotTreatedCount));
            OnPropertyChanged(nameof(HasNotTreated));
            OnPropertyChanged(nameof(NotTreatedLabel));
        }
        return removed;
    }

    private void OnItemChanged()
    {
        _suppress = true;
        IsSelected = Items.Count > 0 && Items.All(i => i.IsChecked);
        _suppress = false;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionLabel));
        _onChanged();
    }
}

/// <summary>One resolvable row inside a category group, carrying the AI rationale + source report.</summary>
public sealed partial class AutoResolveItemViewModel : ObservableObject
{
    public event Action? SelectionChanged;

    public AutoResolveItemViewModel(RuleRecord record, ResolvedAccum info)
    {
        Record = record;
        Rationale = info.Rationale;
        Confidence = info.Confidence;
        Reports = new ObservableCollection<AutoResolveReportRefViewModel>(
            info.Reports.Select(r => new AutoResolveReportRefViewModel(r)));
    }

    public RuleRecord Record { get; }
    public string Rationale { get; }

    /// <summary>The AI's own confidence (1..5) that this row is correctly covered.</summary>
    public int Confidence { get; }

    /// <summary>Every report the AI relied on for this row (clickable, read-only).</summary>
    public ObservableCollection<AutoResolveReportRefViewModel> Reports { get; }

    public bool HasReports => Reports.Count > 0;

    public string Actor => Record.DisplayActor;
    public string Assignment => string.IsNullOrWhiteSpace(Record.Value)
        ? Record.CategoryLabel
        : $"{Record.CategoryLabel} = {Record.Value}";
    public string StatusLabel => Record.Status.ToString();

    [ObservableProperty] private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value) => SelectionChanged?.Invoke();

    /// <summary>False when hidden by the results search box.</summary>
    [ObservableProperty] private bool _matchesFilter = true;

    [RelayCommand]
    private void CopyOutlinerPath() => TrySetClipboard(Record.ActorPath);

    [RelayCommand]
    private void CopyActorName() => TrySetClipboard(Record.ActorName);

    private static void TrySetClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { System.Windows.Clipboard.SetText(text); } catch { /* clipboard busy */ }
    }
}

/// <summary>A clickable reference to one report the AI cited; opens a read-only view.</summary>
public sealed partial class AutoResolveReportRefViewModel : ObservableObject
{
    private readonly AutoResolveKnownReport _report;

    public AutoResolveReportRefViewModel(AutoResolveKnownReport report) => _report = report;

    public string Id => _report.Id;
    public bool IsApproved => string.Equals(_report.Kind, "APPROVED", StringComparison.OrdinalIgnoreCase);
    public bool IsSuspicious => string.Equals(_report.Kind, "SUSPICIOUS", StringComparison.OrdinalIgnoreCase);
    public string KindLabel => IsApproved ? "Approved" : IsSuspicious ? "Suspicious" : "General";
    public string Badge => $"{KindLabel} · {Id}";
    public string Comment => _report.Comment;

    [RelayCommand]
    private void Open()
    {
        if (_report.Source is null) return;
        var dialog = new Views.ReportViewerDialog(_report.Source, IsApproved)
        {
            Owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }
}
