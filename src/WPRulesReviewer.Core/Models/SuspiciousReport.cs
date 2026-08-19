using System.Text.Json.Serialization;

namespace WPRulesReviewer.Core.Models;

/// <summary>Per-actor context captured inside a (possibly multi-actor) report.</summary>
public sealed class SuspiciousReportItem
{
    public string Category { get; set; } = string.Empty;
    public string AssignmentType { get; set; } = string.Empty;
    public string ActorPath { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public string? Reason { get; set; }
    public int LineNumber { get; set; }
    public string RawLine { get; set; } = string.Empty;

    public static SuspiciousReportItem FromRecord(RuleRecord r) => new()
    {
        Category = r.Category.ToString(),
        AssignmentType = r.AssignmentType.ToString(),
        ActorPath = r.ActorPath,
        ActorName = r.ActorName,
        Value = r.Value,
        ExpectedValue = r.ExpectedValue,
        Status = r.Status.ToString(),
        StatusReason = r.StatusReason,
        Reason = r.Reason,
        LineNumber = r.LineNumber,
        RawLine = r.RawLine
    };
}

/// <summary>
/// A user-entered "this actor looks suspicious" (or "approved") note, persisted to the app data
/// folder. Captures the full context of one or more flagged records so it can be triaged later
/// without the log. Top-level fields mirror the first actor for convenient list display.
/// </summary>
public sealed class SuspiciousReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.Now;
    public string Comment { get; set; } = string.Empty;

    /// <summary>How confident the reviewer is about this report, from 1 (unsure) to 5 (full confidence).</summary>
    public int Confidence { get; set; } = 5;

    // ---- Session / TeamCity build context ----------------------------------
    public string SessionName { get; set; } = string.Empty;
    public int? TeamCityBuildId { get; set; }
    public string? BuildNumber { get; set; }
    public string? WebUrl { get; set; }
    public DateTimeOffset? LogTime { get; set; }
    public string SourcePath { get; set; } = string.Empty;

    // ---- Record / operation context (first actor; kept for list display) ----
    public string Category { get; set; } = string.Empty;
    public string AssignmentType { get; set; } = string.Empty;
    public string ActorPath { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public string? Reason { get; set; }
    public int LineNumber { get; set; }
    public string RawLine { get; set; } = string.Empty;

    /// <summary>All actors covered by this report (one entry per selected grid row).</summary>
    public List<SuspiciousReportItem> Actors { get; set; } = new();

    [JsonIgnore] public int ActorCount => Actors.Count == 0 ? 1 : Actors.Count;

    [JsonIgnore]
    public string ActorSummary => ActorCount <= 1
        ? ActorName
        : $"{ActorName}  (+{ActorCount - 1} more)";

    /// <summary>Build a single-actor report (kept for compatibility).</summary>
    public static SuspiciousReport FromRecord(RuleRecord r, SessionReport s, string comment)
        => FromRecords(new[] { r }, s, comment);

    /// <summary>Build a report covering one or more records; top-level fields mirror the first.</summary>
    public static SuspiciousReport FromRecords(IReadOnlyList<RuleRecord> records, SessionReport s, string comment)
    {
        var first = records[0];
        var report = new SuspiciousReport
        {
            Comment = comment,
            SessionName = s.SessionName,
            TeamCityBuildId = s.TeamCityBuildId,
            BuildNumber = s.BuildNumber,
            WebUrl = s.WebUrl,
            LogTime = s.LogTime,
            SourcePath = s.SourcePath,
            Category = first.Category.ToString(),
            AssignmentType = first.AssignmentType.ToString(),
            ActorPath = first.ActorPath,
            ActorName = first.ActorName,
            Value = first.Value,
            ExpectedValue = first.ExpectedValue,
            Status = first.Status.ToString(),
            StatusReason = first.StatusReason,
            Reason = first.Reason,
            LineNumber = first.LineNumber,
            RawLine = first.RawLine
        };
        foreach (var r in records) report.Actors.Add(SuspiciousReportItem.FromRecord(r));
        return report;
    }
}
