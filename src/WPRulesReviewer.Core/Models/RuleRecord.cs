using System.Text;

namespace WPRulesReviewer.Core.Models;

/// <summary>
/// One meaningful line parsed from a Sundance.log World Partition rule pass.
/// Covers applied assignments, warnings, errors and skipped actors.
/// </summary>
public sealed class RuleRecord
{
    public int LineNumber { get; init; }
    public DateTimeOffset? Timestamp { get; init; }

    public RecordCategory Category { get; init; }
    public AssignmentType AssignmentType { get; init; }
    public WarningKind WarningKind { get; init; }

    /// <summary>Full Outliner path when available (e.g. LV_Overland/Region/.../SM_Foo).</summary>
    public string ActorPath { get; set; } = string.Empty;

    /// <summary>Leaf actor name (last path segment).</summary>
    public string ActorName { get; init; } = string.Empty;

    /// <summary>Applied value: DataLayer name, grid name, HLOD layer name or true/false.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>True when the log stated "This is a forced setting.".</summary>
    public bool Forced { get; init; }

    /// <summary>Extra detail: skip reason, warning text, missing layer name, ...</summary>
    public string? Reason { get; set; }

    /// <summary>Rules listed in a "matches multiple ... rules" warning.</summary>
    public IReadOnlyList<string> MatchedRules { get; init; } = Array.Empty<string>();

    /// <summary>How many identical lines were collapsed into this record.</summary>
    public int Occurrences { get; set; } = 1;

    public string RawLine { get; init; } = string.Empty;

    // ---- Filled by the triage engine ---------------------------------------
    public ReviewStatus Status { get; set; } = ReviewStatus.NeedsReview;
    public AnomalySeverity Severity { get; set; } = AnomalySeverity.None;
    public string? StatusReason { get; set; }
    public string? ExpectedValue { get; set; }

    /// <summary>Transient UI flag: user marked this row as read/reviewed (hidden unless "Show read").</summary>
    public bool IsRead { get; set; }

    /// <summary>Transient UI flag: user flagged this row as suspicious (a comment was saved).</summary>
    public bool IsReported { get; set; }

    /// <summary>Last suspicious comment entered for this row (used to pre-fill the edit dialog).</summary>
    public string? SuspiciousComment { get; set; }

    /// <summary>Transient UI flag: user approved this row as valid (a comment was saved).</summary>
    public bool IsApproved { get; set; }

    /// <summary>Last approval comment entered for this row (used to pre-fill the edit dialog).</summary>
    public string? ApprovalComment { get; set; }

    /// <summary>
    /// Transient UI note: when Auto-resolve reading examined this actor, related it to one or more
    /// reports, but deliberately left the final call to the human (e.g. a suspicious report makes the
    /// assignment ambiguous), this holds the AI's explanation. Empty when the AI never reasoned about
    /// this actor (it matched no report at all).
    /// </summary>
    public string? AiReviewNote { get; set; }

    /// <summary>Report ids the AI cited for <see cref="AiReviewNote"/> (for display in the AI Check dialog).</summary>
    public IReadOnlyList<string> AiReviewReportIds { get; set; } = Array.Empty<string>();

    /// <summary>The AI's confidence (1..5) attached to <see cref="AiReviewNote"/>.</summary>
    public int AiReviewConfidence { get; set; }

    /// <summary>True when the AI left a note asking the human to decide this actor's status.</summary>
    public bool HasAiReviewNote => !string.IsNullOrWhiteSpace(AiReviewNote);

    /// <summary>Exact identity used for de-duplication of records within a session.</summary>
    public string Key => $"{Category}|{AssignmentType}|{WarningKind}|{ActorPath}|{ActorName}|{Value}";

    public string DisplayActor => string.IsNullOrEmpty(ActorPath) ? ActorName : ActorPath;

    /// <summary>Full, human-readable context for the grid tooltip.</summary>
    public string DetailTooltip
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(CategoryLabel);
            if (!string.IsNullOrEmpty(Value)) sb.Append("  |  ").Append(Value);
            if (Forced) sb.Append("  (forced)");
            if (Occurrences > 1) sb.Append("  x").Append(Occurrences);
            sb.AppendLine();

            if (!string.IsNullOrEmpty(DisplayActor))
                sb.Append("Actor: ").AppendLine(DisplayActor);
            if (!string.IsNullOrEmpty(StatusReason))
                sb.Append("Status: ").Append(Status).Append("  -  ").AppendLine(StatusReason);
            else
                sb.Append("Status: ").AppendLine(Status.ToString());
            if (Severity != AnomalySeverity.None)
                sb.Append("Severity: ").AppendLine(Severity.ToString());
            if (!string.IsNullOrEmpty(ExpectedValue))
                sb.Append("Expected: ").AppendLine(ExpectedValue);
            if (MatchedRules.Count > 0)
                sb.Append("Rules: ").AppendLine(string.Join(", ", MatchedRules));
            if (!string.IsNullOrEmpty(Reason))
            {
                sb.AppendLine();
                sb.AppendLine(Reason);
            }
            if (!string.IsNullOrEmpty(RawLine))
            {
                sb.AppendLine();
                sb.Append("Line ").Append(LineNumber).Append(":  ").Append(RawLine);
            }
            return sb.ToString().TrimEnd();
        }
    }

    public string CategoryLabel => Category switch
    {
        RecordCategory.Applied => AssignmentType.ToString(),
        _ => Category.ToString()
    };

    /// <summary>A single, copy-paste friendly line describing the record.</summary>
    public string ToPlainLine()
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(CategoryLabel).Append("] ");
        if (Category == RecordCategory.Applied)
        {
            sb.Append(Value).Append("  ->  ").Append(DisplayActor);
            if (Forced) sb.Append("  (forced)");
        }
        else
        {
            sb.Append(DisplayActor);
            if (!string.IsNullOrEmpty(Reason)) sb.Append("  :  ").Append(Reason);
        }
        if (Occurrences > 1) sb.Append("  x").Append(Occurrences);
        return sb.ToString();
    }
}
