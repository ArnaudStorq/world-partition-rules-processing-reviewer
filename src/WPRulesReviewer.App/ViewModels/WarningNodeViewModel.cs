using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>
/// One row of the Warning Explorer tree. Children are materialized the first time the node is
/// expanded, mirroring the lazy <see cref="InsightNode"/> underneath, so opening a session with
/// thousands of records only builds the branches the user actually looks at.
///
/// The tree item template drives its expander from <see cref="HasChildren"/> rather than from
/// <c>TreeViewItem.HasItems</c>, which is what lets the children stay unbuilt until expansion.
/// </summary>
public sealed partial class WarningNodeViewModel : ObservableObject
{
    private readonly InsightNode _node;
    private bool _childrenLoaded;

    public WarningNodeViewModel(InsightNode node) => _node = node;

    public InsightNode Node => _node;

    public string Title => _node.Title;
    public int Count => _node.Count;
    public AnomalySeverity MaxSeverity => _node.MaxSeverity;
    public IReadOnlyList<RuleRecord> Records => _node.Records;
    public GroupDimension? Dimension => _node.Dimension;
    public ProblemSignature? Signature => _node.Signature;
    public RuleRecord? LeafRecord => _node.LeafRecord;

    public ObservableCollection<WarningNodeViewModel> Children { get; } = new();

    public bool HasChildren => _childrenLoaded ? Children.Count > 0 : _node.MayHaveChildren;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) LoadChildren();
    }

    public void LoadChildren()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;

        foreach (var child in _node.Children)
            Children.Add(new WarningNodeViewModel(child));

        OnPropertyChanged(nameof(HasChildren));
    }

    // ---- Presentation -------------------------------------------------------
    public string Glyph => _node.Dimension switch
    {
        GroupDimension.OutlinerPath => "\uE8B7",       // Folder
        GroupDimension.Actor => "\uE7C3",              // Page
        GroupDimension.ProblemSignature => "\uEA39",   // Problem marker
        GroupDimension.AssignmentType or GroupDimension.Value => "\uE8EC", // Tag
        GroupDimension.Severity or GroupDimension.WarningKind => "\uE7BA", // Warning
        _ => "\uE8FD"                                   // Bulleted list
    };

    /// <summary>Share of the subtree already marked as read, driving the thin progress bar.</summary>
    public double ReadRatio => _node.Count == 0 ? 0 : (double)_node.ReadCount / _node.Count;

    public bool IsFullyRead => _node.Count > 0 && _node.ReadCount == _node.Count;

    public string CountTooltip =>
        $"{_node.Count} record(s)  -  {_node.AnomalyCount} anomaly, {_node.NeedsReviewCount} needs review, " +
        $"{_node.ExpectedCount} expected, {_node.KnownNoiseCount} known noise  -  {_node.ReadCount} read";

    /// <summary>Refreshes the read-progress visuals after rows were marked read elsewhere.</summary>
    public void RefreshProgress()
    {
        OnPropertyChanged(nameof(ReadRatio));
        OnPropertyChanged(nameof(IsFullyRead));
        OnPropertyChanged(nameof(CountTooltip));
        foreach (var child in Children) child.RefreshProgress();
    }

    /// <summary>Expands this node and its descendants down to <paramref name="depth"/> levels.</summary>
    public void ExpandTo(int depth)
    {
        if (depth <= 0 || !HasChildren) return;
        IsExpanded = true;
        foreach (var child in Children) child.ExpandTo(depth - 1);
    }
}
