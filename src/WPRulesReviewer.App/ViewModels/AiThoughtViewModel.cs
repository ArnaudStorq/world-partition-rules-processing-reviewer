using System.Collections.ObjectModel;
using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>
/// Read-only view of the AI's "I leave this actor to the human" reasoning, shown from the grid's
/// "AI Check" button. It surfaces the rationale, the AI confidence and the report(s) the AI cited.
/// </summary>
public sealed class AiThoughtViewModel
{
    public AiThoughtViewModel(RuleRecord record, IReadOnlyList<AutoResolveKnownReport> reports)
    {
        ActorPath = record.DisplayActor;
        ActorName = record.ActorName;
        Assignment = string.IsNullOrWhiteSpace(record.Value)
            ? record.CategoryLabel
            : $"{record.CategoryLabel} = {record.Value}";
        Rationale = record.AiReviewNote ?? string.Empty;
        Confidence = record.AiReviewConfidence;

        var ids = new HashSet<string>(record.AiReviewReportIds, StringComparer.OrdinalIgnoreCase);
        Reports = new ObservableCollection<AutoResolveReportRefViewModel>(
            reports.Where(r => ids.Contains(r.Id)).Select(r => new AutoResolveReportRefViewModel(r)));
    }

    public string ActorPath { get; }
    public string ActorName { get; }
    public string Assignment { get; }
    public string Rationale { get; }

    /// <summary>The AI confidence (1..5); 0 means the AI did not attach one.</summary>
    public int Confidence { get; }
    public bool HasConfidence => Confidence > 0;

    public ObservableCollection<AutoResolveReportRefViewModel> Reports { get; }
    public bool HasReports => Reports.Count > 0;
}
