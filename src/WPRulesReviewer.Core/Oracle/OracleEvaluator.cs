using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Oracle;

/// <summary>
/// Pre-computed, fast lookup sets derived from the oracle config so classification is O(1) per record.
/// </summary>
public sealed class OracleKnowledge
{
    public HashSet<string> KnownDataLayers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> KnownDataLayerPrefixes { get; } = new();
    public HashSet<string> KnownRuntimeGrids { get; } = new(StringComparer.OrdinalIgnoreCase) { "None" };
    public List<string> ForceExcludeFromHlodPaths { get; } = new();

    public static OracleKnowledge Build(OracleConfig config)
    {
        var k = new OracleKnowledge();

        foreach (var r in config.DataLayerRules)
        {
            var dl = RuleNameToTarget(r.Name, "DL_");
            if (dl is not null) k.KnownDataLayers.Add(dl);
        }
        foreach (var c in config.ParentConventions)
            if (!string.IsNullOrEmpty(c.NamePrefix)) k.KnownDataLayerPrefixes.Add(c.NamePrefix);

        foreach (var r in config.RuntimeGridRules)
        {
            var g = StripRuleName(r.Name);
            if (!string.IsNullOrEmpty(g)) k.KnownRuntimeGrids.Add(g);
        }

        foreach (var p in config.OutlinerPathsToForceExcludeFromHLOD)
            if (!string.IsNullOrWhiteSpace(p)) k.ForceExcludeFromHlodPaths.Add(p);

        return k;
    }

    private static string StripRuleName(string name)
    {
        var n = name;
        if (n.StartsWith("DA_", StringComparison.Ordinal)) n = n[3..];
        if (n.EndsWith("_Rules", StringComparison.Ordinal)) n = n[..^"_Rules".Length];
        return n;
    }

    private static string? RuleNameToTarget(string name, string prefix)
    {
        if (!name.EndsWith("_Rules", StringComparison.Ordinal)) return null;
        var core = StripRuleName(name);
        return prefix + core;
    }
}

/// <summary>
/// Applies the oracle to a single record and produces a <see cref="ReviewStatus"/> + severity + reason.
/// This is the deterministic part of the triage (the AI layer handles what the ini cannot express).
/// </summary>
public sealed class OracleEvaluator
{
    private readonly OracleConfig _config;
    private readonly OracleKnowledge _knowledge;
    private readonly AppSettings _settings;

    public OracleEvaluator(OracleConfig config, AppSettings settings)
    {
        _config = config;
        _knowledge = OracleKnowledge.Build(config);
        _settings = settings;
    }

    public void Classify(RuleRecord r)
    {
        switch (r.Category)
        {
            case RecordCategory.Skipped: ClassifySkipped(r); break;
            case RecordCategory.Error: Set(r, ReviewStatus.Anomaly, AnomalySeverity.High, "Builder error (blocker)"); break;
            case RecordCategory.ImportError: Set(r, ReviewStatus.Anomaly, AnomalySeverity.High, "Asset import failure (blocker)"); break;
            case RecordCategory.Warning: ClassifyWarning(r); break;
            case RecordCategory.Applied: ClassifyApplied(r); break;
        }

        // Unclassified-folder flag (never downgrades an anomaly).
        if (r.Status != ReviewStatus.Anomaly && MatchesAny(r.DisplayActor, _settings.FlagUnclassifiedTokens))
            Set(r, ReviewStatus.NeedsReview, AnomalySeverity.Low, "Actor lives under an unclassified folder");

        // Global noise suppression (never hides an anomaly).
        if (r.Status != ReviewStatus.Anomaly && MatchesAny(r.DisplayActor, _settings.NoiseActorPatterns))
            Set(r, ReviewStatus.KnownNoise, AnomalySeverity.None, "Matches a configured noise pattern");
    }

    private void ClassifySkipped(RuleRecord r)
    {
        var locked = r.Reason?.Contains("cannot be checked out", StringComparison.OrdinalIgnoreCase) == true
                     || r.Reason?.Contains("Checked out by", StringComparison.OrdinalIgnoreCase) == true;
        if (locked && _settings.TreatNavSkipAsNoise)
            Set(r, ReviewStatus.KnownNoise, AnomalySeverity.None, "Package locked by another user (expected, retried next pass)");
        else
            Set(r, ReviewStatus.NeedsReview, AnomalySeverity.Low, "Actor skipped");
    }

    private void ClassifyWarning(RuleRecord r)
    {
        switch (r.WarningKind)
        {
            case WarningKind.MultipleHLODLayerRules:
            case WarningKind.MultipleRuntimeGridRules:
                Set(r, ReviewStatus.Anomaly, AnomalySeverity.High,
                    "Multiple single-valued rules match one actor (only one should)");
                break;
            case WarningKind.MultipleDataLayerRules:
                Set(r, ReviewStatus.NeedsReview, AnomalySeverity.Low,
                    "Multiple DataLayer rules match (additive: often by design, confirm)");
                break;
            case WarningKind.MissingDataLayerNamed:
                Set(r, ReviewStatus.Anomaly, AnomalySeverity.Medium,
                    $"Expected DataLayer '{r.Value}' does not exist / was not assigned");
                break;
            case WarningKind.MissingDataLayerEmpty:
                if (_settings.TreatEmptyMissingDataLayerAsNoise)
                    Set(r, ReviewStatus.KnownNoise, AnomalySeverity.None, "Actor matched no DataLayer rule (typically helper/foliage)");
                else
                    Set(r, ReviewStatus.NeedsReview, AnomalySeverity.Low, "Actor matched no DataLayer rule");
                break;
            case WarningKind.UntargetedRuntimeDataLayer:
                Set(r, ReviewStatus.NeedsReview, AnomalySeverity.Low,
                    $"Runtime DataLayer '{r.Value}' assigned to the actor but targeted by no rule");
                break;
            default:
                Set(r, ReviewStatus.NeedsReview, AnomalySeverity.Low, "Rule warning");
                break;
        }
    }

    private void ClassifyApplied(RuleRecord r)
    {
        switch (r.AssignmentType)
        {
            case AssignmentType.DataLayer:
                if (IsKnownDataLayer(r.Value))
                    Set(r, ReviewStatus.Expected, AnomalySeverity.None, "DataLayer is a known rule target");
                else
                    Set(r, ReviewStatus.Anomaly, AnomalySeverity.Medium, $"Unknown DataLayer target '{r.Value}'");
                break;

            case AssignmentType.RuntimeGrid:
                if (_knowledge.KnownRuntimeGrids.Contains(r.Value))
                    Set(r, ReviewStatus.Expected, AnomalySeverity.None, "RuntimeGrid is a known grid");
                else
                    Set(r, ReviewStatus.Anomaly, AnomalySeverity.Medium, $"Unknown RuntimeGrid '{r.Value}'");
                break;

            case AssignmentType.HLODLayer:
                if (r.Value.Equals("None", StringComparison.OrdinalIgnoreCase) || r.Value.Contains("HLODLayer", StringComparison.OrdinalIgnoreCase))
                {
                    if (!r.Value.Equals("None", StringComparison.OrdinalIgnoreCase) && IsForceExcluded(r.DisplayActor))
                        Set(r, ReviewStatus.Anomaly, AnomalySeverity.Medium, "Actor is force-excluded from HLOD yet got an HLOD layer");
                    else
                        Set(r, ReviewStatus.Expected, AnomalySeverity.None, "HLOD layer assignment");
                }
                else
                    Set(r, ReviewStatus.NeedsReview, AnomalySeverity.Low, $"Unusual HLOD layer value '{r.Value}'");
                break;

            case AssignmentType.IncludeInHLOD:
                var isTrue = r.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (isTrue && IsForceExcluded(r.DisplayActor))
                    Set(r, ReviewStatus.Anomaly, AnomalySeverity.Medium, "IncludeInHLOD=true but actor path is on the force-exclude list");
                else if (!isTrue && (r.Forced || IsForceExcluded(r.DisplayActor)))
                    Set(r, ReviewStatus.Expected, AnomalySeverity.None, "IncludeInHLOD=false matches force-exclude");
                else
                    Set(r, ReviewStatus.Expected, AnomalySeverity.None, "IncludeInHLOD assignment");
                break;

            default:
                Set(r, ReviewStatus.Expected, AnomalySeverity.None, "Processed");
                break;
        }
    }

    private bool IsKnownDataLayer(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (_knowledge.KnownDataLayers.Contains(value)) return true;
        return _knowledge.KnownDataLayerPrefixes.Any(p =>
            value.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsForceExcluded(string actor) => MatchesAny(actor, _knowledge.ForceExcludeFromHlodPaths);

    private static bool MatchesAny(string value, IEnumerable<string> tokens)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var t in tokens)
            if (!string.IsNullOrEmpty(t) && value.Contains(t, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void Set(RuleRecord r, ReviewStatus status, AnomalySeverity severity, string reason)
    {
        r.Status = status;
        r.Severity = severity;
        r.StatusReason = reason;
    }
}
