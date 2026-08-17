using WPRulesReviewer.Core.Oracle;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class OracleConfigLoaderTests : IDisposable
{
    private readonly string _iniPath = Path.Combine(Path.GetTempPath(), $"DefaultEditor_{Guid.NewGuid():N}.ini");

    private const string Ini = """
[/Script/Engine.Something]
Unrelated=True

[/Script/WorldBuildingEditor.WorldPartitionRuleSettings]
AutoApplyRulesOnActorSave=True
+DataLayerRulesForActorSave=/Game/Data/DataLayers/DA_RENDER_Rules.DA_RENDER_Rules
+DataLayerRulesForActorSave=/Game/Data/DataLayers/DA_GAMEPLAY_Rules.DA_GAMEPLAY_Rules
+RuntimeGridRulesForActorSave=/Game/Data/Grids/DA_MainGrid_Rules.DA_MainGrid_Rules
+HLODLayerRulesForActorSave=/Game/Data/HLOD/DA_Large_Rules.DA_Large_Rules
+OutlinerPathsToForceExcludeFromHLOD=LV_Overland/Region/Hogwarts Valley/NoHLOD
+DataLayerInstanceParentConventions=(NamePrefix="DL_HM_",RequiredAncestor="Hogsmeade",DefaultParent="Root")
""";

    public OracleConfigLoaderTests() => File.WriteAllText(_iniPath, Ini);
    public void Dispose() { try { File.Delete(_iniPath); } catch { } }

    [Fact]
    public void Loads_only_the_rules_section()
    {
        var config = new OracleConfigLoader().Load(_iniPath);
        Assert.True(config.IsLoaded);
        Assert.True(config.AutoApplyRulesOnActorSave);
        Assert.Equal(2, config.DataLayerRules.Count);
        Assert.Single(config.RuntimeGridRules);
        Assert.Single(config.HLODLayerRules);
    }

    [Fact]
    public void Parses_rule_names()
    {
        var config = new OracleConfigLoader().Load(_iniPath);
        Assert.Contains(config.DataLayerRules, r => r.Name == "DA_RENDER_Rules");
        Assert.Contains(config.RuntimeGridRules, r => r.Name == "DA_MainGrid_Rules");
    }

    [Fact]
    public void Parses_force_exclude_paths_and_conventions()
    {
        var config = new OracleConfigLoader().Load(_iniPath);
        Assert.Contains("LV_Overland/Region/Hogwarts Valley/NoHLOD", config.OutlinerPathsToForceExcludeFromHLOD);
        var conv = Assert.Single(config.ParentConventions);
        Assert.Equal("DL_HM_", conv.NamePrefix);
        Assert.Equal("Hogsmeade", conv.RequiredAncestor);
    }

    [Fact]
    public void Missing_file_returns_unloaded_config()
    {
        var config = new OracleConfigLoader().Load(Path.Combine(Path.GetTempPath(), "does_not_exist.ini"));
        Assert.False(config.IsLoaded);
    }
}
