using WPRulesReviewer.Core.Models;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class RuleRecordTests
{
    [Fact]
    public void DisplayActor_prefers_full_path()
    {
        var r = new RuleRecord { ActorPath = "LV/Region/SM_A", ActorName = "SM_A" };
        Assert.Equal("LV/Region/SM_A", r.DisplayActor);
    }

    [Fact]
    public void DisplayActor_falls_back_to_name()
    {
        var r = new RuleRecord { ActorPath = "", ActorName = "SM_A" };
        Assert.Equal("SM_A", r.DisplayActor);
    }

    [Fact]
    public void CategoryLabel_uses_assignment_for_applied()
    {
        var r = new RuleRecord { Category = RecordCategory.Applied, AssignmentType = AssignmentType.RuntimeGrid };
        Assert.Equal("RuntimeGrid", r.CategoryLabel);
    }
}
