using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>A single line rendered in the inspector.</summary>
public sealed class InspectorLine
{
    public required string Text { get; init; }
    public bool IsHeader { get; init; }
    public bool IsMatch { get; init; }
    public bool IsMono { get; init; }
}

/// <summary>
/// Shared side panel that shows a rich, human-readable explanation of the currently
/// selected record plus the single Sundance.log line it comes from. Keeps this detail out of the
/// activity log so the log stays clean.
/// </summary>
public sealed partial class RecordInspectorViewModel : ObservableObject
{
    public ObservableCollection<InspectorLine> Lines { get; } = new();

    [ObservableProperty] private string _title = "Details";
    [ObservableProperty] private bool _hasContent;

    /// <summary>True when a record is shown and its actor name / outliner path can be copied.</summary>
    [ObservableProperty] private bool _hasActor;

    private RuleRecord? _current;

    private void Add(string text, bool header = false, bool match = false, bool mono = false)
        => Lines.Add(new InspectorLine { Text = text, IsHeader = header, IsMatch = match, IsMono = mono });

    [RelayCommand]
    public void Clear()
    {
        Lines.Clear();
        Title = "Details";
        HasContent = false;
        HasActor = false;
        _current = null;
    }

    [RelayCommand]
    private void CopyOutlinerPath()
    {
        var path = _current?.DisplayActor;
        if (string.IsNullOrEmpty(path)) return;
        try { Clipboard.SetText(path); } catch { /* clipboard busy */ }
    }

    [RelayCommand]
    private void CopyActorName()
    {
        var name = _current?.ActorName;
        if (string.IsNullOrEmpty(name)) name = _current?.DisplayActor;
        if (string.IsNullOrEmpty(name)) return;
        try { Clipboard.SetText(name); } catch { /* clipboard busy */ }
    }

    [RelayCommand]
    private void CopyAll()
    {
        if (Lines.Count == 0) return;
        var text = string.Join(Environment.NewLine, Lines.Select(l => l.Text));
        try { Clipboard.SetText(text); }
        catch { /* clipboard busy */ }
    }

    /// <summary>Populate the inspector for a record, including the single original log line it comes from.</summary>
    public void Show(RuleRecord r, string[]? sourceLines)
    {
        Lines.Clear();
        _current = r;
        HasActor = !string.IsNullOrEmpty(r.DisplayActor);

        var head = new StringBuilder();
        head.Append(r.CategoryLabel);
        if (!string.IsNullOrEmpty(r.Value)) head.Append("  '").Append(r.Value).Append('\'');
        if (r.Forced) head.Append("  (forced)");
        if (r.Occurrences > 1) head.Append("  x").Append(r.Occurrences);
        Title = head.ToString();
        Add(head.ToString(), header: true);

        if (!string.IsNullOrEmpty(r.DisplayActor))
            Add($"Actor:  {r.DisplayActor}");

        var status = new StringBuilder();
        status.Append("Status:  ").Append(r.Status);
        if (r.Severity != AnomalySeverity.None) status.Append("      Severity:  ").Append(r.Severity);
        Add(status.ToString());

        if (!string.IsNullOrEmpty(r.StatusReason)) Add($"Reason:  {r.StatusReason}");
        if (!string.IsNullOrEmpty(r.ExpectedValue)) Add($"Expected:  {r.ExpectedValue}");
        if (r.MatchedRules.Count > 0) Add($"Rules:  {string.Join(", ", r.MatchedRules)}");
        if (!string.IsNullOrEmpty(r.Reason) && r.Reason != r.StatusReason) Add(r.Reason);

        AddOriginalLine(r, sourceLines);

        HasContent = Lines.Count > 0;
    }

    private void AddOriginalLine(RuleRecord r, string[]? lines)
    {
        string? original = null;
        if (lines is not null && r.LineNumber > 0 && r.LineNumber <= lines.Length)
            original = lines[r.LineNumber - 1]; // LineNumber is 1-based
        else if (!string.IsNullOrEmpty(r.RawLine))
            original = r.RawLine;

        if (original is null) return;
        Add($"{r.LineNumber}:  {original}", match: true, mono: true);
    }
}
