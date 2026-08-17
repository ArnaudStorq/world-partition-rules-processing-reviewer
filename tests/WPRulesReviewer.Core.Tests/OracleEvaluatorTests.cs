using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Oracle;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class OracleEvaluatorTests
{
    private static OracleConfig BuildOracle()
    {
        var c = new OracleConfig();
        c.DataLayerRules.Add(new RuleAssetRef { Name = "DA_RENDER_Rules" });
        c.RuntimeGridRules.Add(new RuleAssetRef { Name = "DA_MainGrid_Rules" });
        c.OutlinerPathsToForceExcludeFromHLOD.Add("LV_Overland/NoHLOD");
        return c;
    }

    private static OracleEvaluator Evaluator(AppSettings? s = null) => new(BuildOracle(), s ?? new AppSettings());

    [Fact]
    public void Known_datalayer_is_expected()
    {
        var r = new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_RENDER", ActorPath = "LV_Overland/SM_A" };
        Evaluator().Classify(r);
        Assert.Equal(ReviewStatus.Expected, r.Status);
    }

    [Fact]
    public void Unknown_datalayer_is_anomaly()
    {
        var r = new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_MYSTERY", ActorPath = "LV_Overland/SM_A" };
        Evaluator().Classify(r);
        Assert.Equal(ReviewStatus.Anomaly, r.Status);
    }

    [Fact]
    public void Known_runtimegrid_is_expected()
    {
        var r = new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.RuntimeGrid, Value = "MainGrid", ActorPath = "LV_Overland/SM_A" };
        Evaluator().Classify(r);
        Assert.Equal(ReviewStatus.Expected, r.Status);
    }

    [Fact]
    public void Errors_are_high_severity_anomalies()
    {
        var r = new RuleRecord { Category = RecordCategory.Error, Reason = "boom" };
        Evaluator().Classify(r);
        Assert.Equal(ReviewStatus.Anomaly, r.Status);
        Assert.Equal(AnomalySeverity.High, r.Severity);
    }

    [Fact]
    public void Multiple_runtimegrid_rules_is_anomaly()
    {
        var r = new RuleRecord { Category = RecordCategory.Warning, WarningKind = WarningKind.MultipleRuntimeGridRules, ActorName = "SM_A" };
        Evaluator().Classify(r);
        Assert.Equal(ReviewStatus.Anomaly, r.Status);
    }

    [Fact]
    public void Include_in_hlod_true_on_force_excluded_is_anomaly()
    {
        var r = new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.IncludeInHLOD, Value = "true", ActorPath = "LV_Overland/NoHLOD/SM_A" };
        Evaluator().Classify(r);
        Assert.Equal(ReviewStatus.Anomaly, r.Status);
    }

    [Fact]
    public void Locked_skip_is_known_noise_when_enabled()
    {
        var r = new RuleRecord { Category = RecordCategory.Skipped, Reason = "package cannot be checked out" };
        Evaluator(new AppSettings { TreatNavSkipAsNoise = true }).Classify(r);
        Assert.Equal(ReviewStatus.KnownNoise, r.Status);
    }

    [Fact]
    public void Noise_pattern_downgrades_non_anomaly()
    {
        var settings = new AppSettings { NoiseActorPatterns = { } };
        settings.NoiseActorPatterns.Clear();
        settings.NoiseActorPatterns.Add("Nanite_Shadow");
        var r = new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_RENDER", ActorPath = "LV_Overland/Nanite_Shadow_1" };
        Evaluator(settings).Classify(r);
        Assert.Equal(ReviewStatus.KnownNoise, r.Status);
    }
}
