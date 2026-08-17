namespace WPRulesReviewer.Core.Models;

/// <summary>
/// The full parsed + triaged result for one rule pass (one TeamCity session / one Sundance.log).
/// </summary>
public sealed class SessionReport
{
    public string SessionName { get; set; } = "Session";
    public string SourcePath { get; set; } = string.Empty;
    public string? World { get; set; }
    public DateTimeOffset? LogTime { get; set; }

    public int? TeamCityBuildId { get; set; }
    public string? BuildNumber { get; set; }
    public string? WebUrl { get; set; }

    public List<RuleRecord> Records { get; } = new();

    public DateTimeOffset AnalyzedAt { get; set; } = DateTimeOffset.Now;
    public long ParsedLineCount { get; set; }
    public TimeSpan ParseDuration { get; set; }

    // ---- Aggregate counts --------------------------------------------------
    public int AppliedCount => Records.Count(r => r.Category == RecordCategory.Applied);
    public int WarningCount => Records.Count(r => r.Category == RecordCategory.Warning);
    public int ErrorCount => Records.Count(r => r.Category == RecordCategory.Error);
    public int ImportErrorCount => Records.Count(r => r.Category == RecordCategory.ImportError);
    public int SkippedCount => Records.Count(r => r.Category == RecordCategory.Skipped);
    public int TotalCount => Records.Count;

    public int DataLayerCount => Records.Count(r => r.AssignmentType == AssignmentType.DataLayer);
    public int RuntimeGridCount => Records.Count(r => r.AssignmentType == AssignmentType.RuntimeGrid);
    public int HLODLayerCount => Records.Count(r => r.AssignmentType == AssignmentType.HLODLayer);
    public int IncludeInHLODCount => Records.Count(r => r.AssignmentType == AssignmentType.IncludeInHLOD);

    public int AnomalyCount => Records.Count(r => r.Status == ReviewStatus.Anomaly);
    public int NeedsReviewCount => Records.Count(r => r.Status == ReviewStatus.NeedsReview);
    public int ExpectedCount => Records.Count(r => r.Status == ReviewStatus.Expected);
    public int KnownNoiseCount => Records.Count(r => r.Status == ReviewStatus.KnownNoise);
}
