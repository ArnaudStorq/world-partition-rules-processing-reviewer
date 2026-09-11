using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Fixes;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>One recommendation card in the Fix Advisor tab.</summary>
public sealed partial class FixRecommendationViewModel : ObservableObject
{
    private readonly FixAdvisorViewModel _owner;

    public FixRecommendationViewModel(FixRecommendation model, FixAdvisorViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public FixRecommendation Model { get; }

    public string Title => Model.Title;
    public string RootCause => Model.RootCause;
    public string Recommendation => Model.Recommendation;
    public FixKind Kind => Model.Kind;
    public FixRisk Risk => Model.Risk;
    public int Confidence => Model.Confidence;
    public string SignatureId => Model.SignatureId;
    public int AffectedCount => Model.AffectedRecords.Count;
    public string MatchDescription => Model.Match?.Describe() ?? "(no pattern)";
    public IReadOnlyList<FixAction> Actions => Model.Actions;
    public bool HasActions => Model.Actions.Count > 0;

    public string KindLabel => Model.Kind switch
    {
        FixKind.TriageOnly => "Review decision",
        FixKind.NoiseSuppression => "Noise filter",
        FixKind.IniRuleChange => "Rule config change",
        FixKind.ActorDataChange => "Actor data change",
        _ => "Investigate"
    };

    /// <summary>Glyph mirroring how automatable the fix is: applied here, delegated, or informational.</summary>
    public string KindGlyph => Model.Kind switch
    {
        FixKind.TriageOnly => "\uE8FB",        // Accept
        FixKind.NoiseSuppression => "\uE71C",  // Filter
        FixKind.IniRuleChange => "\uE713",     // Settings
        FixKind.ActorDataChange => "\uE8A7",   // OpenInNewWindow
        _ => "\uE946"                          // Info
    };

    /// <summary>True when the tool itself can execute the actions (everything else is delegated).</summary>
    public bool CanApply => Model.IsApplicable && AffectedCount > 0;

    public bool IsDelegated => Model.Kind is FixKind.ActorDataChange or FixKind.IniRuleChange;

    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    private void ApplyBatch() => _owner.ApplyRecommendation(this, Model.AffectedRecords);

    [RelayCommand]
    private void Preview() => _owner.PreviewRecommendation(this);

    [RelayCommand]
    private void Delegate() => _owner.DelegateToCursor(this);

    [RelayCommand]
    private void RevealScope() => _owner.ScopeToRecommendation(this);
}

/// <summary>
/// Smart Analysis tab that turns the session's problem signatures into AI recommendations, and
/// applies the ones the tool is allowed to execute. The AI proposes typed actions; the tool decides
/// what is applicable and always previews before writing anything.
/// </summary>
public sealed partial class FixAdvisorViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IActivityLog _log;
    private readonly IAiAgentService _ai;
    private readonly FixExecutor _executor;
    private readonly FixAdvisorActions _actions;

    private SessionReport? _report;
    private IReadOnlyList<RuleRecord> _issues = Array.Empty<RuleRecord>();
    private IReadOnlyList<RuleRecord> _scopeRecords = Array.Empty<RuleRecord>();

    public FixAdvisorViewModel(AppSettings settings, IActivityLog log, IAiAgentService ai, FixAdvisorActions actions)
    {
        _settings = settings;
        _log = log;
        _ai = ai;
        _actions = actions;
        _executor = new FixExecutor(settings, log);
    }

    public ObservableCollection<FixRecommendationViewModel> Recommendations { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _narrative = string.Empty;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _liveOutput = string.Empty;
    [ObservableProperty] private string _scopeTitle = string.Empty;

    public bool CanUseAi => _ai.IsAvailable && _settings.EnableFixAdvisor;
    public bool HasResults => Recommendations.Count > 0;
    public bool HasNarrative => !string.IsNullOrWhiteSpace(Narrative);
    public bool HasScope => !string.IsNullOrEmpty(ScopeTitle);

    partial void OnNarrativeChanged(string value) => OnPropertyChanged(nameof(HasNarrative));
    partial void OnScopeTitleChanged(string value) => OnPropertyChanged(nameof(HasScope));

    /// <summary>
    /// <paramref name="issues"/> is the errors / warnings subset the panel advises on; the report is
    /// still needed for the session identity and for undoing across the whole log.
    /// </summary>
    public void SetReport(SessionReport report, IReadOnlyList<RuleRecord> issues)
    {
        _report = report;
        _issues = issues;
        _scopeRecords = issues;
        ScopeTitle = string.Empty;
        Recommendations.Clear();
        Narrative = string.Empty;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CanUseAi));
    }

    /// <summary>Restricts the next analysis to one Warning Explorer node.</summary>
    public void SetScope(string title, IReadOnlyList<RuleRecord> records)
    {
        ScopeTitle = title;
        _scopeRecords = records;
    }

    [RelayCommand]
    private void ClearScope()
    {
        ScopeTitle = string.Empty;
        _scopeRecords = _issues;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (_report is null || IsBusy) return;
        if (!CanUseAi)
        {
            Error = "AI is not available. Enable it and set the Cursor agent path in Settings > AI.";
            return;
        }

        IsBusy = true;
        Error = null;
        LiveOutput = string.Empty;
        Recommendations.Clear();
        OnPropertyChanged(nameof(HasResults));

        try
        {
            // Only litigious records are worth diagnosing: expected assignments have no fix to propose.
            var candidates = _scopeRecords
                .Where(r => r.Status is ReviewStatus.Anomaly or ReviewStatus.NeedsReview
                            || r.Category is RecordCategory.Error or RecordCategory.ImportError)
                .ToList();

            if (candidates.Count == 0)
            {
                Narrative = "Nothing to diagnose: no anomaly, error or item needing review in this scope.";
                return;
            }

            var tree = InsightTree.Build(candidates, InsightPreset.ByProblem.Dimensions);
            var buckets = tree.SignatureBuckets();
            var prompt = FixAdvisorPromptBuilder.Build(_report, buckets, _settings,
                HasScope ? ScopeTitle : null);

            var progress = new Progress<string>(chunk => LiveOutput += chunk);
            var result = await _ai.CompleteStreamingAsync(prompt, progress);

            if (!result.Success)
            {
                Error = result.Error;
                return;
            }

            var plan = FixAdvisorResultParser.Parse(result.Content);
            Narrative = plan.Narrative;

            if (plan.Recommendations.Count == 0)
            {
                Error ??= "The agent did not return a usable plan. Its raw answer is shown below.";
                return;
            }

            // Expand each pattern over the scope so the affected rows are the tool's decision, not the AI's.
            foreach (var recommendation in plan.Recommendations)
            {
                recommendation.AffectedRecords = recommendation.Match is null
                    ? MatchBySignature(recommendation, buckets)
                    : MatchRuleEvaluator.Expand(recommendation.Match, candidates);

                Recommendations.Add(new FixRecommendationViewModel(recommendation, this));
            }

            _log.Success($"Fix Advisor produced {Recommendations.Count} recommendation(s).", "AI");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    /// <summary>Fallback when the agent gave a signature id but no pattern.</summary>
    private static IReadOnlyList<RuleRecord> MatchBySignature(FixRecommendation recommendation,
        IReadOnlyList<SignatureBucket> buckets)
    {
        var bucket = buckets.FirstOrDefault(b =>
            string.Equals(b.Signature.Id, recommendation.SignatureId, StringComparison.OrdinalIgnoreCase));
        return bucket?.Records ?? Array.Empty<RuleRecord>();
    }

    /// <summary>Entry point used by the Warning Explorer context menu.</summary>
    public void AnalyzeScope(string title, IReadOnlyList<RuleRecord> records)
    {
        SetScope(title, records);
        _actions.Activate?.Invoke();
        if (!AnalyzeCommand.IsRunning) AnalyzeCommand.Execute(null);
    }

    public void ApplyRecommendation(FixRecommendationViewModel card, IReadOnlyList<RuleRecord> records)
    {
        if (_report is null) return;

        var confirmed = _actions.ConfirmApply?.Invoke(card, records) ?? true;
        if (!confirmed) return;

        var batchId = Guid.NewGuid().ToString("N")[..8];
        var result = _executor.Apply(card.Model, records, _report.SessionName, batchId);

        card.StatusMessage = result.Message;
        card.IsApplied = result.Success;
        if (result.Success) _actions.AfterApply?.Invoke();
    }

    public void PreviewRecommendation(FixRecommendationViewModel card)
        => _actions.Preview?.Invoke(card, card.Model.AffectedRecords);

    public void DelegateToCursor(FixRecommendationViewModel card)
        => _actions.OpenInCursor?.Invoke(card.Title, card.Model.AffectedRecords);

    public void ScopeToRecommendation(FixRecommendationViewModel card)
        => _actions.ScopeToRecords?.Invoke(card.Title, card.Model.AffectedRecords);

    [RelayCommand]
    private void UndoLastBatch()
    {
        if (_report is null) return;
        var result = _executor.UndoLastBatch(_report.Records);
        if (result.Success)
        {
            foreach (var card in Recommendations) card.IsApplied = false;
            _actions.AfterApply?.Invoke();
        }
        else
        {
            _log.Info(result.Message, "Fix");
        }
    }

    [RelayCommand]
    private void OpenJournal() => _actions.OpenJournal?.Invoke(_executor.Journal.FilePath);
}

/// <summary>Callbacks the Fix Advisor needs from the owning session (UI dialogs, navigation, refresh).</summary>
public sealed class FixAdvisorActions
{
    public Func<FixRecommendationViewModel, IReadOnlyList<RuleRecord>, bool>? ConfirmApply { get; init; }
    public Action<FixRecommendationViewModel, IReadOnlyList<RuleRecord>>? Preview { get; init; }
    public Action<string, IReadOnlyList<RuleRecord>>? OpenInCursor { get; init; }
    public Action<string, IReadOnlyList<RuleRecord>>? ScopeToRecords { get; init; }
    public Action? AfterApply { get; init; }
    public Action? Activate { get; init; }
    public Action<string>? OpenJournal { get; init; }
}
