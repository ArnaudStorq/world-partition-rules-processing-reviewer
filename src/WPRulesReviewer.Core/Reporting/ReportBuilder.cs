using System.Text;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Reporting;

/// <summary>Builds clean, copy-paste friendly Markdown / CSV / plain-text reports.</summary>
public static class ReportBuilder
{
    public static string BuildAnomalyMarkdown(SessionReport report, bool includeExpected = false)
    {
        var sb = new StringBuilder();
        sb.Append("# WorldPartition Rules Review — ").AppendLine(report.SessionName);
        sb.AppendLine();
        sb.Append("- **Source:** `").Append(report.SourcePath).AppendLine("`");
        if (report.World is not null) sb.Append("- **World:** ").AppendLine(report.World);
        if (report.LogTime is { } t) sb.Append("- **Log time:** ").AppendLine(t.LocalDateTime.ToString("f"));
        sb.Append("- **Analyzed:** ").AppendLine(report.AnalyzedAt.LocalDateTime.ToString("f"));
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("| --- | ---: |");
        sb.Append("| Applied | ").Append(report.AppliedCount).AppendLine(" |");
        sb.Append("| Warnings | ").Append(report.WarningCount).AppendLine(" |");
        sb.Append("| Errors | ").Append(report.ErrorCount).AppendLine(" |");
        sb.Append("| Skipped | ").Append(report.SkippedCount).AppendLine(" |");
        sb.Append("| **Anomalies** | ").Append(report.AnomalyCount).AppendLine(" |");
        sb.Append("| Needs review | ").Append(report.NeedsReviewCount).AppendLine(" |");
        sb.AppendLine();

        AppendGroup(sb, "Anomalies (litigious points)", report.Records
            .Where(r => r.Status == ReviewStatus.Anomaly)
            .OrderByDescending(r => r.Severity));

        AppendGroup(sb, "Needs review", report.Records
            .Where(r => r.Status == ReviewStatus.NeedsReview)
            .OrderByDescending(r => r.Severity));

        if (includeExpected)
            AppendGroup(sb, "Expected", report.Records.Where(r => r.Status == ReviewStatus.Expected));

        if (report.AnomalyCount == 0 && report.NeedsReviewCount == 0)
            sb.AppendLine("_No anomalies or items needing review. Everything matches the oracle._");

        return sb.ToString();
    }

    private static void AppendGroup(StringBuilder sb, string title, IEnumerable<RuleRecord> records)
    {
        var list = records.ToList();
        if (list.Count == 0) return;

        sb.Append("## ").Append(title).Append(" (").Append(list.Count).AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("| Sev | Type | Value | Actor | Reason | x |");
        sb.AppendLine("| --- | --- | --- | --- | --- | -: |");
        foreach (var r in list)
        {
            sb.Append("| ").Append(r.Severity)
              .Append(" | ").Append(Escape(r.CategoryLabel))
              .Append(" | ").Append(Escape(r.Value))
              .Append(" | `").Append(Escape(r.DisplayActor)).Append('`')
              .Append(" | ").Append(Escape(r.StatusReason ?? r.Reason ?? string.Empty))
              .Append(" | ").Append(r.Occurrences)
              .AppendLine(" |");
        }
        sb.AppendLine();
    }

    public static string BuildCsv(IEnumerable<RuleRecord> records, string delimiter = ",")
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(delimiter, "Category", "Type", "Value", "Actor", "Status", "Severity", "Reason", "Occurrences", "Line"));
        foreach (var r in records)
        {
            sb.AppendLine(string.Join(delimiter,
                Csv(r.Category.ToString(), delimiter),
                Csv(r.CategoryLabel, delimiter),
                Csv(r.Value, delimiter),
                Csv(r.DisplayActor, delimiter),
                Csv(r.Status.ToString(), delimiter),
                Csv(r.Severity.ToString(), delimiter),
                Csv(r.StatusReason ?? r.Reason ?? string.Empty, delimiter),
                r.Occurrences.ToString(),
                r.LineNumber.ToString()));
        }
        return sb.ToString();
    }

    public static string BuildPlainText(IEnumerable<RuleRecord> records)
    {
        var sb = new StringBuilder();
        foreach (var r in records) sb.AppendLine(r.ToPlainLine());
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string Csv(string s, string delimiter)
    {
        var needsQuote = s.Contains(delimiter) || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        s = s.Replace("\"", "\"\"");
        return needsQuote ? $"\"{s}\"" : s;
    }
}
