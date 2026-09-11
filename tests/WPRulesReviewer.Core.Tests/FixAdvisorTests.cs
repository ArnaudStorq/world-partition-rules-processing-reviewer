using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Models;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class FixAdvisorParserTests
{
    private const string GoodAnswer = """
        The pass is dominated by shadow proxy actors that no rule targets. Nothing is broken.

        ```json
        {
          "recommendations": [
            {
              "signatureId": "WPR-1234ABCD",
              "title": "Nanite shadow proxies are not covered by any rule",
              "rootCause": "Helper actors generated at cook time, intentionally excluded.",
              "recommendation": "Filter them out of future passes.",
              "fixKind": "NoiseSuppression",
              "risk": "Low",
              "confidence": 5,
              "match": { "actorNameContains": "Nanite_Shadow" },
              "actions": [ { "type": "AddNoisePattern", "value": "Nanite_Shadow" } ]
            },
            {
              "signatureId": "WPR-DEADBEEF",
              "title": "Dorm actors reference a missing DataLayer",
              "rootCause": "The DataLayer asset was renamed but the rule was not updated.",
              "recommendation": "Fix the rule in DefaultEditor.ini.",
              "fixKind": "IniRuleChange",
              "risk": "High",
              "confidence": 3,
              "match": { "assignmentType": "DataLayer", "actorPathContains": "HB_Dorm" },
              "actions": []
            }
          ]
        }
        ```
        """;

    [Fact]
    public void Parses_recommendations_and_narrative()
    {
        var plan = FixAdvisorResultParser.Parse(GoodAnswer);

        Assert.Equal(2, plan.Recommendations.Count);
        Assert.StartsWith("The pass is dominated", plan.Narrative);
    }

    [Fact]
    public void Maps_kind_risk_and_actions()
    {
        var first = FixAdvisorResultParser.Parse(GoodAnswer).Recommendations[0];

        Assert.Equal(FixKind.NoiseSuppression, first.Kind);
        Assert.Equal(FixRisk.Low, first.Risk);
        Assert.Equal(5, first.Confidence);
        Assert.True(first.IsApplicable);

        var action = Assert.Single(first.Actions);
        Assert.Equal("AddNoisePattern", action.Type);
        Assert.Equal("Nanite_Shadow", action.Value);
    }

    [Fact]
    public void Ini_and_actor_changes_are_not_applicable_by_the_tool()
    {
        var second = FixAdvisorResultParser.Parse(GoodAnswer).Recommendations[1];

        Assert.Equal(FixKind.IniRuleChange, second.Kind);
        Assert.False(second.IsApplicable);
    }

    [Fact]
    public void Unknown_kind_falls_back_to_investigate()
    {
        const string answer = """{ "recommendations": [ { "title": "x", "fixKind": "Teleport" } ] }""";
        var plan = FixAdvisorResultParser.Parse(answer);

        Assert.Equal(FixKind.Investigate, Assert.Single(plan.Recommendations).Kind);
    }

    [Fact]
    public void Garbage_answer_yields_an_empty_plan_instead_of_throwing()
    {
        var plan = FixAdvisorResultParser.Parse("I could not analyze this, sorry.");

        Assert.Empty(plan.Recommendations);
        Assert.Equal("I could not analyze this, sorry.", plan.Narrative);
    }

    [Fact]
    public void Empty_match_is_dropped_so_it_never_selects_anything()
    {
        const string answer = """{ "recommendations": [ { "title": "x", "match": { } } ] }""";
        var recommendation = Assert.Single(FixAdvisorResultParser.Parse(answer).Recommendations);

        Assert.Null(recommendation.Match);
    }
}

public class MatchRuleEvaluatorTests
{
    private static RuleRecord Rec(string path, AssignmentType type = AssignmentType.DataLayer, string value = "DL_X") => new()
    {
        ActorPath = path,
        ActorName = path.Split('/')[^1],
        AssignmentType = type,
        Value = value
    };

    private static readonly RuleRecord[] Records =
    {
        Rec("LV_Overland/North/SM_Nanite_Shadow_01"),
        Rec("LV_Overland/North/SM_Rock_02"),
        Rec("LV_Interior/HB_Dorm/SM_Bed_03", AssignmentType.RuntimeGrid, "MainGrid")
    };

    [Fact]
    public void Conditions_are_combined_with_and()
    {
        var rule = new MatchRule { AssignmentType = "DataLayer", ActorNameContains = "Nanite_Shadow" };
        var matched = MatchRuleEvaluator.Expand(rule, Records);

        Assert.Single(matched);
        Assert.Contains("Nanite_Shadow", matched[0].ActorName);
    }

    [Fact]
    public void A_rule_without_condition_matches_nothing()
    {
        Assert.Empty(MatchRuleEvaluator.Expand(new MatchRule(), Records));
        Assert.Empty(MatchRuleEvaluator.Expand(null, Records));
    }

    [Fact]
    public void Invalid_regex_matches_nothing_instead_of_throwing()
    {
        var rule = new MatchRule { ActorPathRegex = "([unclosed" };
        Assert.Empty(MatchRuleEvaluator.Expand(rule, Records));
    }

    [Fact]
    public void Name_prefix_applies_to_the_leaf_not_the_full_path()
    {
        var byLeaf = new MatchRule { ActorNamePrefix = "SM_Rock" };
        Assert.Single(MatchRuleEvaluator.Expand(byLeaf, Records));

        var byFullPath = new MatchRule { ActorNamePrefix = "LV_Overland" };
        Assert.Empty(MatchRuleEvaluator.Expand(byFullPath, Records));
    }

    [Fact]
    public void Path_contains_selects_a_whole_folder()
    {
        var rule = new MatchRule { ActorPathContains = "LV_Overland" };
        Assert.Equal(2, MatchRuleEvaluator.Expand(rule, Records).Count);
    }
}
