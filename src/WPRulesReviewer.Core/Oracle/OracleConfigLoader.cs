using System.Text.RegularExpressions;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Oracle;

/// <summary>
/// Reads the [/Script/WorldBuildingEditor.WorldPartitionRuleSettings] section of DefaultEditor.ini
/// into a structured <see cref="OracleConfig"/>.
/// </summary>
public sealed partial class OracleConfigLoader
{
    private const string SectionHeader = "[/Script/WorldBuildingEditor.WorldPartitionRuleSettings]";

    [GeneratedRegex(@"NamePrefix=""([^""]*)""")]
    private static partial Regex NamePrefix();
    [GeneratedRegex(@"RequiredAncestor=""([^""]*)""")]
    private static partial Regex RequiredAncestor();
    [GeneratedRegex(@"DefaultParent=""([^""]*)""")]
    private static partial Regex DefaultParent();

    public OracleConfig Load(string iniPath)
    {
        var config = new OracleConfig { SourcePath = iniPath };
        if (!File.Exists(iniPath)) return config;

        var inSection = false;
        foreach (var raw in File.ReadLines(iniPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;

            if (line.StartsWith('['))
            {
                inSection = line.Equals(SectionHeader, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].TrimStart('+').Trim();
            var value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "AutoApplyRulesOnActorSave":
                    config.AutoApplyRulesOnActorSave = value.Equals("True", StringComparison.OrdinalIgnoreCase);
                    break;
                case "MapsWithAutoApplyRules": config.MapsWithAutoApplyRules.Add(value); break;

                case "DataLayerRulesForActorSave": config.DataLayerRules.Add(ToRuleRef(value)); break;
                case "HLODLayerRulesForActorSave": config.HLODLayerRules.Add(ToRuleRef(value)); break;
                case "RuntimeGridRulesForActorSave": config.RuntimeGridRules.Add(ToRuleRef(value)); break;

                case "ActorTypesIgnoredByDataLayerRules": config.ActorTypesIgnoredByDataLayerRules.Add(value); break;
                case "ActorTypesIgnoredByRuntimeGridRules": config.ActorTypesIgnoredByRuntimeGridRules.Add(value); break;
                case "ActorTypesIgnoredByHLODLayerRules": config.ActorTypesIgnoredByHLODLayerRules.Add(value); break;

                case "ActorTypesToForceExcludeFromHLOD": config.ActorTypesToForceExcludeFromHLOD.Add(value); break;
                case "ActorTypesToClearRuntimeGrid": config.ActorTypesToClearRuntimeGrid.Add(value); break;

                case "OutlinerPathsToForceExcludeFromHLOD": config.OutlinerPathsToForceExcludeFromHLOD.Add(value); break;
                case "OutlinerPathsToClearRuntimeGrid": config.OutlinerPathsToClearRuntimeGrid.Add(value); break;
                case "OutlinerPathsToClearDataLayers": config.OutlinerPathsToClearDataLayers.Add(value); break;

                case "ActorTagExcludedFromRuntimeGridRules": config.ActorTagExcludedFromRuntimeGridRules = value; break;
                case "ActorTagExcludedFromDataLayerRules": config.ActorTagExcludedFromDataLayerRules = value; break;
                case "ActorTagExcludedFromHLODLayerRules": config.ActorTagExcludedFromHLODLayerRules = value; break;

                case "DataLayerInstanceParentConventions":
                    var conv = ParseConvention(value);
                    if (conv is not null) config.ParentConventions.Add(conv);
                    break;
            }
        }
        return config;
    }

    private static RuleAssetRef ToRuleRef(string value)
    {
        // /Game/Data/DataLayers/DA_RENDER_Rules.DA_RENDER_Rules
        var pkg = value;
        var dot = value.LastIndexOf('.');
        var name = dot >= 0 ? value[(dot + 1)..] : value;
        if (dot >= 0) pkg = value[..dot];
        return new RuleAssetRef { Name = name, PackagePath = pkg };
    }

    private static DataLayerParentConvention? ParseConvention(string value)
    {
        var np = NamePrefix().Match(value);
        if (!np.Success) return null;
        return new DataLayerParentConvention
        {
            NamePrefix = np.Groups[1].Value,
            RequiredAncestor = RequiredAncestor().Match(value) is { Success: true } ra ? ra.Groups[1].Value : string.Empty,
            DefaultParent = DefaultParent().Match(value) is { Success: true } dp ? dp.Groups[1].Value : string.Empty
        };
    }
}
