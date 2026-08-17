namespace WPRulesReviewer.Core.Models;

/// <summary>A rule Data Asset referenced from DefaultEditor.ini.</summary>
public sealed class RuleAssetRef
{
    public string Name { get; set; } = string.Empty;        // DA_RENDER_Rules
    public string PackagePath { get; set; } = string.Empty; // /Game/Data/DataLayers/DA_RENDER_Rules

    public override string ToString() => Name;
}

/// <summary>A DataLayer naming convention (DL_ prefix -> required ancestor layer).</summary>
public sealed class DataLayerParentConvention
{
    public string NamePrefix { get; set; } = string.Empty;
    public string RequiredAncestor { get; set; } = string.Empty;
    public string DefaultParent { get; set; } = string.Empty;
}

/// <summary>
/// Structured view of [/Script/WorldBuildingEditor.WorldPartitionRuleSettings] from
/// DefaultEditor.ini. This is the "oracle" the triage engine consults.
/// </summary>
public sealed class OracleConfig
{
    public string SourcePath { get; set; } = string.Empty;
    public bool AutoApplyRulesOnActorSave { get; set; }
    public List<string> MapsWithAutoApplyRules { get; } = new();

    public List<RuleAssetRef> DataLayerRules { get; } = new();
    public List<RuleAssetRef> HLODLayerRules { get; } = new();
    public List<RuleAssetRef> RuntimeGridRules { get; } = new();

    public List<string> ActorTypesIgnoredByDataLayerRules { get; } = new();
    public List<string> ActorTypesIgnoredByRuntimeGridRules { get; } = new();
    public List<string> ActorTypesIgnoredByHLODLayerRules { get; } = new();

    public List<string> ActorTypesToForceExcludeFromHLOD { get; } = new();
    public List<string> ActorTypesToClearRuntimeGrid { get; } = new();

    public List<string> OutlinerPathsToForceExcludeFromHLOD { get; } = new();
    public List<string> OutlinerPathsToClearRuntimeGrid { get; } = new();
    public List<string> OutlinerPathsToClearDataLayers { get; } = new();

    public string ActorTagExcludedFromRuntimeGridRules { get; set; } = "ExcludeFromRuntimeGridRules";
    public string ActorTagExcludedFromDataLayerRules { get; set; } = "ExcludeFromDataLayerRules";
    public string ActorTagExcludedFromHLODLayerRules { get; set; } = "ExcludeFromHLODLayerRules";

    public List<DataLayerParentConvention> ParentConventions { get; } = new();

    public bool IsLoaded => DataLayerRules.Count > 0 || HLODLayerRules.Count > 0 || RuntimeGridRules.Count > 0;

    /// <summary>Known DataLayer prefixes derived from the parent conventions (DL_LOC_, DL_HM_, ...).</summary>
    public IEnumerable<string> KnownDataLayerPrefixes => ParentConventions.Select(c => c.NamePrefix);
}
