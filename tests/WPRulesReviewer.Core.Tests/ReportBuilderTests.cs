using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Reporting;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class ReportBuilderTests
{
    private static SessionReport SampleReport()
    {
        var report = new SessionReport { SessionName = "Dungeons", SourcePath = @"C:\logs\Sundance.log" };
        report.Records.Add(new RuleRecord
        {
            Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_MYSTERY",
            ActorPath = "LV/SM_A", ActorName = "SM_A", Status = ReviewStatus.Anomaly, Severity = AnomalySeverity.Medium,
            StatusReason = "Unknown DataLayer target"
        });
        report.Records.Add(new RuleRecord
        {
            Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_OK",
            ActorPath = "LV/SM_B", ActorName = "SM_B", Status = ReviewStatus.NeedsReview
        });
        report.Records.Add(new RuleRecord
        {
            Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_OK",
            ActorPath = "LV/SM_C", ActorName = "SM_C", Status = ReviewStatus.Expected
        });
        return report;
    }

    [Fact]
    public void Markdown_contains_summary_and_sections()
    {
        var md = ReportBuilder.BuildAnomalyMarkdown(SampleReport());
        Assert.Contains("## Summary", md);
        Assert.Contains("Anomalies", md);
        Assert.Contains("DL_MYSTERY", md);
    }

    [Fact]
    public void Markdown_excludes_expected_by_default()
    {
        var md = ReportBuilder.BuildAnomalyMarkdown(SampleReport(), includeExpected: false);
        Assert.DoesNotContain("SM_C", md);
    }

    [Fact]
    public void Markdown_includes_expected_when_requested()
    {
        var md = ReportBuilder.BuildAnomalyMarkdown(SampleReport(), includeExpected: true);
        Assert.Contains("SM_C", md);
    }

    [Fact]
    public void Csv_has_header_and_rows()
    {
        var csv = ReportBuilder.BuildCsv(SampleReport().Records);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Category", lines[0]);
        Assert.Equal(4, lines.Length); // header + 3 rows
    }

    [Fact]
    public void Csv_quotes_values_with_delimiter()
    {
        var records = new[]
        {
            new RuleRecord { Category = RecordCategory.Warning, Reason = "a, b, c", ActorName = "SM_A", StatusReason = "x, y" }
        };
        var csv = ReportBuilder.BuildCsv(records);
        Assert.Contains("\"x, y\"", csv);
    }

    [Fact]
    public void PlainText_has_one_line_per_record()
    {
        var text = ReportBuilder.BuildPlainText(SampleReport().Records);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
    }
}
