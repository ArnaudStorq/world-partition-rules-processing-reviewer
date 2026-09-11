using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Models;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class ProblemSignatureTests
{
    private static RuleRecord Warning(string statusReason) => new()
    {
        Category = RecordCategory.Warning,
        WarningKind = WarningKind.MissingDataLayerNamed,
        AssignmentType = AssignmentType.DataLayer,
        Status = ReviewStatus.Anomaly,
        Severity = AnomalySeverity.Medium,
        StatusReason = statusReason
    };

    [Fact]
    public void Collapses_quoted_operands_into_one_signature()
    {
        var a = ProblemSignature.From(Warning("Expected DataLayer 'DL_HW_HB_Dorm_A3' does not exist"));
        var b = ProblemSignature.From(Warning("Expected DataLayer 'DL_HW_HB_Dorm_A17' does not exist"));

        Assert.Equal(a, b);
        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public void Collapses_numeric_suffixes_and_bracketed_counts()
    {
        var a = ProblemSignature.Normalize("Matches 3 HLODLayer rules: [R_A, R_B, R_C]");
        var b = ProblemSignature.Normalize("Matches 5 HLODLayer rules: [R_D, R_E]");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Keeps_different_problems_apart()
    {
        var missing = ProblemSignature.From(Warning("Expected DataLayer '...' does not exist"));
        var multiple = ProblemSignature.From(new RuleRecord
        {
            Category = RecordCategory.Warning,
            WarningKind = WarningKind.MultipleHLODLayerRules,
            Status = ReviewStatus.Anomaly,
            StatusReason = "Multiple single-valued rules match one actor (only one should)"
        });

        Assert.NotEqual(missing, multiple);
    }

    [Fact]
    public void Id_is_stable_across_instances()
    {
        var first = ProblemSignature.From(Warning("Expected DataLayer 'DL_A' does not exist")).Id;
        var second = ProblemSignature.From(Warning("Expected DataLayer 'DL_B' does not exist")).Id;

        Assert.Equal(first, second);
        Assert.StartsWith("WPR-", first);
    }

    [Fact]
    public void Falls_back_to_reason_then_value_then_category()
    {
        var fromReason = ProblemSignature.From(new RuleRecord { Category = RecordCategory.Skipped, Reason = "Checked out by bob" });
        Assert.Equal("Checked out by bob", fromReason.Title);

        var fromValue = ProblemSignature.From(new RuleRecord { Category = RecordCategory.Applied, Value = "DL_RENDER" });
        Assert.Equal("DL_RENDER", fromValue.Title);

        var fromCategory = ProblemSignature.From(new RuleRecord { Category = RecordCategory.Skipped });
        Assert.Equal("Skipped", fromCategory.Title);
    }
}
