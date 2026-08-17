using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Oracle;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class AnomalyEngineTests
{
    private static OracleConfig Oracle()
    {
        var c = new OracleConfig();
        c.DataLayerRules.Add(new RuleAssetRef { Name = "DA_RENDER_Rules" });
        c.RuntimeGridRules.Add(new RuleAssetRef { Name = "DA_MainGrid_Rules" });
        return c;
    }

    [Fact]
    public void Classifies_every_record_in_report()
    {
        var report = new SessionReport { SessionName = "Mixed" };
        report.Records.Add(new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_RENDER", ActorPath = "LV/SM_A" });
        report.Records.Add(new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.DataLayer, Value = "DL_MYSTERY", ActorPath = "LV/SM_B" });
        report.Records.Add(new RuleRecord { Category = RecordCategory.Error, Reason = "boom" });

        new AnomalyEngine(new AppSettings()).Classify(report, Oracle());

        Assert.Equal(1, report.ExpectedCount);
        Assert.Equal(2, report.AnomalyCount);
    }

    [Fact]
    public void Empty_report_does_not_throw()
    {
        var report = new SessionReport { SessionName = "Empty" };
        new AnomalyEngine(new AppSettings()).Classify(report, Oracle());
        Assert.Equal(0, report.TotalCount);
    }
}
