using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Models;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class InsightTreeTests
{
    private static RuleRecord Rec(string actorPath, ReviewStatus status, AnomalySeverity severity, string reason,
        AssignmentType type = AssignmentType.DataLayer, string value = "DL_X",
        RecordCategory category = RecordCategory.Warning, int occurrences = 1) => new()
    {
        Category = category,
        AssignmentType = type,
        ActorPath = actorPath,
        ActorName = actorPath.Split('/')[^1],
        Value = value,
        Status = status,
        Severity = severity,
        StatusReason = reason,
        Occurrences = occurrences
    };

    private static IReadOnlyList<RuleRecord> Sample() => new[]
    {
        Rec("LV_Overland/North/SM_A", ReviewStatus.Anomaly, AnomalySeverity.High, "Expected DataLayer 'DL_1' does not exist"),
        Rec("LV_Overland/North/SM_B", ReviewStatus.Anomaly, AnomalySeverity.High, "Expected DataLayer 'DL_2' does not exist"),
        Rec("LV_Overland/South/SM_C", ReviewStatus.NeedsReview, AnomalySeverity.Low, "Actor skipped"),
        Rec("LV_Interior/Kitchen/SM_D", ReviewStatus.Expected, AnomalySeverity.None, "DataLayer is a known rule target")
    };

    private static int CountLeaves(InsightNode node) =>
        node.HasChildren ? node.Children.Sum(CountLeaves) : node.Count;

    [Fact]
    public void Root_holds_every_record()
    {
        var tree = InsightTree.Build(Sample(), InsightPreset.ByProblem.Dimensions);
        Assert.Equal(4, tree.Root.Count);
        Assert.Equal(2, tree.Root.AnomalyCount);
        Assert.Equal(AnomalySeverity.High, tree.Root.MaxSeverity);
    }

    [Fact]
    public void Child_counts_sum_to_the_parent()
    {
        var tree = InsightTree.Build(Sample(), InsightPreset.ByProblem.Dimensions);
        Assert.Equal(tree.Root.Count, tree.Root.Children.Sum(c => c.Count));
        Assert.Equal(tree.Root.Count, CountLeaves(tree.Root));
    }

    [Fact]
    public void Groups_by_log_verbosity_first_with_the_default_preset()
    {
        var records = Sample().Append(Rec("LV_Overland/SM_E", ReviewStatus.Anomaly, AnomalySeverity.High,
            "Rule pass failed", category: RecordCategory.Error)).ToList();

        var titles = InsightTree.Build(records, InsightPreset.ByProblem.Dimensions)
            .Root.Children.Select(c => c.Title).ToList();

        Assert.Equal(new[] { "Errors", "Warnings" }, titles);
    }

    [Fact]
    public void Verbosity_buckets_keep_engine_order_rather_than_size_order()
    {
        var records = new List<RuleRecord>();
        for (var i = 0; i < 20; i++)
            records.Add(Rec($"LV/SM_Log{i}", ReviewStatus.Expected, AnomalySeverity.None, "applied",
                category: RecordCategory.Applied));
        records.Add(Rec("LV/SM_Warn", ReviewStatus.Anomaly, AnomalySeverity.High, "boom"));

        var titles = InsightTree.Build(records, InsightPreset.ByProblem.Dimensions)
            .Root.Children.Select(c => c.Title).ToList();

        Assert.Equal(new[] { "Warnings", "Logs" }, titles);
    }

    [Fact]
    public void Signature_level_collapses_the_two_missing_layer_warnings()
    {
        var records = Sample().Append(Rec("LV_Overland/SM_E", ReviewStatus.Expected, AnomalySeverity.None,
            "applied", category: RecordCategory.Applied)).ToList();

        var warnings = InsightTree.Build(records, InsightPreset.ByProblem.Dimensions)
            .Root.Children.Single(c => c.Title == "Warnings");

        var biggest = warnings.Children[0];
        Assert.Equal(2, biggest.Count);
        Assert.NotNull(biggest.Signature);
    }

    [Fact]
    public void Siblings_are_ordered_by_occurrence_count()
    {
        // All warnings, so the verbosity level is elided and the signatures become the top level.
        var records = new[]
        {
            Rec("LV/SM_A", ReviewStatus.NeedsReview, AnomalySeverity.Low, "Rare problem"),
            Rec("LV/SM_B", ReviewStatus.Anomaly, AnomalySeverity.High, "Frequent problem", occurrences: 40),
            Rec("LV/SM_C", ReviewStatus.NeedsReview, AnomalySeverity.Low, "Medium problem", occurrences: 5)
        };

        var titles = InsightTree.Build(records, InsightPreset.ByProblem.Dimensions)
            .Root.Children.Select(c => c.Title).ToList();

        Assert.Equal(new[] { "Frequent problem", "Medium problem", "Rare problem" }, titles);
    }

    [Fact]
    public void Occurrences_aggregate_collapsed_log_lines()
    {
        var records = new[]
        {
            Rec("LV/SM_A", ReviewStatus.Anomaly, AnomalySeverity.High, "boom", occurrences: 12),
            Rec("LV/SM_B", ReviewStatus.Anomaly, AnomalySeverity.High, "boom", occurrences: 3)
        };

        var tree = InsightTree.Build(records, InsightPreset.ByProblem.Dimensions);
        Assert.Equal(2, tree.Root.Count);
        Assert.Equal(15, tree.Root.Occurrences);
    }

    [Fact]
    public void Outliner_path_expands_one_level_per_segment()
    {
        var tree = InsightTree.Build(Sample(), new[] { GroupDimension.OutlinerPath, GroupDimension.Actor });

        var overland = tree.Root.Children.Single(c => c.Title == "LV_Overland");
        Assert.Equal(3, overland.Count);

        var north = overland.Children.Single(c => c.Title == "North");
        Assert.Equal(2, north.Count);
        Assert.All(north.Children, leaf => Assert.Equal(1, leaf.Count));
    }

    [Fact]
    public void Non_discriminating_level_is_skipped()
    {
        // Every record is a Warning, so the Category level would add a single pointless node.
        var tree = InsightTree.Build(Sample(), new[] { GroupDimension.Category, GroupDimension.Status });
        Assert.DoesNotContain(tree.Root.Children, c => c.Title == "Warning");
        Assert.Contains(tree.Root.Children, c => c.Title == "Anomalies");
    }

    [Fact]
    public void Signature_buckets_are_ordered_by_weight()
    {
        var tree = InsightTree.Build(Sample(), InsightPreset.ByProblem.Dimensions);
        var buckets = tree.SignatureBuckets();

        Assert.Equal(3, buckets.Count);
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal(4, buckets.Sum(b => b.Count));
    }

    [Fact]
    public void Empty_input_yields_an_empty_root()
    {
        var tree = InsightTree.Build(Array.Empty<RuleRecord>(), InsightPreset.ByProblem.Dimensions);
        Assert.Equal(0, tree.Root.Count);
        Assert.False(tree.Root.HasChildren);
    }
}
