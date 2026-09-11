using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>Actions the explorer delegates to the owning session (review workflow, AI, inspector).</summary>
public sealed class ExplorerActions
{
    public Action<IReadOnlyList<RuleRecord>>? MarkRead { get; init; }
    public Action<IReadOnlyList<RuleRecord>>? Approve { get; init; }
    public Action<IReadOnlyList<RuleRecord>>? Flag { get; init; }
    public Action<WarningNodeViewModel>? OpenInCursor { get; init; }
    public Action<WarningNodeViewModel>? AskAi { get; init; }
    public Action<RuleRecord>? ShowRecord { get; init; }
}

/// <summary>
/// Left sidebar of a session: the warnings as a browsable hierarchy. Selecting a node scopes the
/// main grid to that subtree; the grouping order is user-controlled through presets.
/// </summary>
public sealed partial class WarningExplorerViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ExplorerActions _actions;
    private IReadOnlyList<RuleRecord> _allRecords = Array.Empty<RuleRecord>();
    private InsightTree? _tree;

    public WarningExplorerViewModel(AppSettings settings, ExplorerActions actions)
    {
        _settings = settings;
        _actions = actions;
        _selectedPreset = InsightPreset.All.FirstOrDefault(p => p.Name == settings.ExplorerDefaultPreset)
                          ?? InsightPreset.ByProblem;
    }

    public IReadOnlyList<InsightPreset> Presets => InsightPreset.All;

    public ObservableCollection<WarningNodeViewModel> Roots { get; } = new();

    /// <summary>Raised when the selected subtree changes; null means "no scope, show everything".</summary>
    public event Action<WarningNodeViewModel?>? ScopeChanged;

    [ObservableProperty] private InsightPreset _selectedPreset;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private WarningNodeViewModel? _selectedNode;
    [ObservableProperty] private bool _isEmpty = true;

    partial void OnSelectedPresetChanged(InsightPreset value)
    {
        _settings.ExplorerDefaultPreset = value.Name;
        Rebuild();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnSelectedNodeChanged(WarningNodeViewModel? value)
    {
        OnPropertyChanged(nameof(HasScope));
        OnPropertyChanged(nameof(ScopeLabel));
        OnPropertyChanged(nameof(ScopeCount));
        ClearScopeCommand.NotifyCanExecuteChanged();
        ScopeChanged?.Invoke(value);

        // Selecting a single record is the natural cue to show it in the inspector.
        if (value?.LeafRecord is { } record) _actions.ShowRecord?.Invoke(record);
    }

    public bool HasScope => SelectedNode is not null;
    public string ScopeLabel => SelectedNode?.Title ?? string.Empty;
    public int ScopeCount => SelectedNode?.Count ?? 0;

    public void SetRecords(IEnumerable<RuleRecord> records)
    {
        _allRecords = records as IReadOnlyList<RuleRecord> ?? records.ToList();
        Rebuild();
    }

    /// <summary>Rebuilds the whole tree. Cheap: only the first level is materialized.</summary>
    public void Rebuild()
    {
        var scoped = Filtered(_allRecords);
        _tree = InsightTree.Build(scoped, SelectedPreset.Dimensions);

        SelectedNode = null;
        Roots.Clear();
        foreach (var child in _tree.Root.Children)
            Roots.Add(new WarningNodeViewModel(child));

        ExpandDefault();

        IsEmpty = Roots.Count == 0;
        OnPropertyChanged(nameof(TotalCount));
    }

    public int TotalCount => _tree?.Root.Count ?? 0;

    /// <summary>
    /// Rows opened automatically. Past this the tree stops being a summary, and expanding every
    /// branch of a big session would materialize thousands of nodes for nothing.
    /// </summary>
    private const int AutoExpandBudget = 250;

    /// <summary>
    /// Opens the hierarchy breadth-first down to the problem family names, and stops there: the
    /// levels below a family are its individual occurrences, which is what the grid is for.
    /// </summary>
    private void ExpandDefault()
    {
        var budget = AutoExpandBudget;
        var level = Roots.ToList();
        var isRoot = true;

        while (level.Count > 0 && budget > 0)
        {
            var next = new List<WarningNodeViewModel>();
            foreach (var node in level)
            {
                if (budget <= 0) break;
                if (!node.HasChildren) continue;
                // The root level always opens, even under a preset that starts on a detail dimension,
                // otherwise the tree would greet the user fully collapsed.
                if (!isRoot && IsDetailLevel(node.Dimension)) continue;

                node.IsExpanded = true;
                budget -= node.Children.Count;
                next.AddRange(node.Children);
            }
            level = next;
            isRoot = false;
        }
    }

    /// <summary>Levels that enumerate occurrences rather than name a group of them.</summary>
    private static bool IsDetailLevel(GroupDimension? dimension) =>
        dimension is GroupDimension.ProblemSignature or GroupDimension.OutlinerPath or GroupDimension.Actor;

    /// <summary>Signature buckets for the current scope, shared with the Overview charts.</summary>
    public IReadOnlyList<SignatureBucket> Buckets(IEnumerable<RuleRecord>? scope = null)
        => _tree?.SignatureBuckets(scope) ?? Array.Empty<SignatureBucket>();

    private IReadOnlyList<RuleRecord> Filtered(IReadOnlyList<RuleRecord> records)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return records;

        var needle = SearchText.Trim();
        return records.Where(r =>
                r.DisplayActor.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.Value.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (r.StatusReason?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Reason?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    /// <summary>Refreshes read-progress bars after the review state changed elsewhere.</summary>
    public void RefreshProgress()
    {
        foreach (var root in Roots) root.RefreshProgress();
    }

    // ---- Commands -----------------------------------------------------------
    private bool CanClearScope() => SelectedNode is not null;

    [RelayCommand(CanExecute = nameof(CanClearScope))]
    private void ClearScope() => SelectedNode = null;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var root in Roots) Collapse(root);

        static void Collapse(WarningNodeViewModel node)
        {
            node.IsExpanded = false;
            foreach (var child in node.Children) Collapse(child);
        }
    }

    [RelayCommand]
    private void ExpandTopLevel()
    {
        foreach (var root in Roots) root.ExpandTo(2);
    }

    [RelayCommand]
    private void MarkSubtreeRead(WarningNodeViewModel? node)
    {
        if (node is null) return;
        _actions.MarkRead?.Invoke(node.Records);
        RefreshProgress();
    }

    [RelayCommand]
    private void ApproveSubtree(WarningNodeViewModel? node)
    {
        if (node is not null) _actions.Approve?.Invoke(node.Records);
    }

    [RelayCommand]
    private void FlagSubtree(WarningNodeViewModel? node)
    {
        if (node is not null) _actions.Flag?.Invoke(node.Records);
    }

    [RelayCommand]
    private void OpenNodeInCursor(WarningNodeViewModel? node)
    {
        if (node is not null) _actions.OpenInCursor?.Invoke(node);
    }

    [RelayCommand]
    private void AskAiAboutNode(WarningNodeViewModel? node)
    {
        if (node is not null) _actions.AskAi?.Invoke(node);
    }
}
