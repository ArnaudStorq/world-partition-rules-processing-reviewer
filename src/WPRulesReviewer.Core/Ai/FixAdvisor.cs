using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Ai;

/// <summary>
/// What the tool is actually able to do about a recommendation. The AI proposes; only these typed
/// actions are ever executed, and the AI itself never gets write access.
/// </summary>
public enum FixKind
{
    /// <summary>Review decision only: mark read / approve / flag with an AI-authored comment.</summary>
    TriageOnly,

    /// <summary>Add a noise pattern to the settings and re-run the deterministic triage.</summary>
    NoiseSuppression,

    /// <summary>Edit the World Partition rule config (DefaultEditor.ini) behind a diff preview.</summary>
    IniRuleChange,

    /// <summary>Actor/asset data: cannot be applied from here, delegated to Cursor or an Unreal script.</summary>
    ActorDataChange,

    /// <summary>No action, just a written recommendation.</summary>
    Investigate
}

public enum FixRisk
{
    Low,
    Medium,
    High
}

/// <summary>One concrete step of a recommendation, executed by the FixExecutor.</summary>
public sealed class FixAction
{
    public string Type { get; init; } = string.Empty;
    public string? Value { get; init; }
    public string? Section { get; init; }
    public string? Key { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Comment { get; init; }

    /// <summary>Bindable form of <see cref="Describe"/>, shown in the recommendation card.</summary>
    public string Description => Describe();

    public string Describe() => Type switch
    {
        "AddNoisePattern" => $"Add \"{Value}\" to the noise patterns",
        "AddUnclassifiedToken" => $"Add \"{Value}\" to the unclassified-folder tokens",
        "MarkRead" => "Mark the affected rows as read",
        "Approve" => $"Approve the affected rows: {Comment}",
        "Flag" => $"Flag the affected rows: {Comment}",
        "EditIni" => $"[{Section}] {Key}: \"{From}\" -> \"{To}\"",
        _ => Type
    };
}

/// <summary>One AI recommendation about a class of problem.</summary>
public sealed class FixRecommendation
{
    public string SignatureId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string RootCause { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public FixKind Kind { get; init; } = FixKind.Investigate;
    public FixRisk Risk { get; init; } = FixRisk.Medium;
    public int Confidence { get; init; } = 3;
    public MatchRule? Match { get; init; }
    public IReadOnlyList<FixAction> Actions { get; init; } = Array.Empty<FixAction>();

    /// <summary>Rows the tool resolved from <see cref="Match"/>, filled in after parsing.</summary>
    public IReadOnlyList<RuleRecord> AffectedRecords { get; set; } = Array.Empty<RuleRecord>();

    /// <summary>True when the tool can execute every action of this recommendation on its own.</summary>
    public bool IsApplicable => Kind is FixKind.TriageOnly or FixKind.NoiseSuppression && Actions.Count > 0;
}

public sealed class FixPlan
{
    public IReadOnlyList<FixRecommendation> Recommendations { get; init; } = Array.Empty<FixRecommendation>();
    public string Narrative { get; init; } = string.Empty;
}

/// <summary>Builds the strict-JSON prompt asking the agent to diagnose and propose typed fixes.</summary>
public static class FixAdvisorPromptBuilder
{
    /// <summary>Signatures sent in one prompt: enough to cover a session, small enough to stay sharp.</summary>
    public const int MaxSignatures = 40;

    /// <summary>Sample actors listed per signature so the agent sees concrete data, not just a label.</summary>
    public const int SamplesPerSignature = 6;

    public static string Build(SessionReport report, IReadOnlyList<SignatureBucket> buckets, AppSettings settings,
        string? scopeTitle = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("IMPORTANT: Respond entirely in English, even though actor paths and comments may contain");
        sb.AppendLine("other languages. Never answer in French.");
        sb.AppendLine();
        sb.AppendLine("You are an expert Unreal Engine 5 World Partition engineer working on the 'Sundance' project.");
        sb.AppendLine("The Perforce workspace for Sundance / UnrealEngine is at D:\\Sun.");
        sb.AppendLine("World Partition rules are configured in DefaultEditor.ini, section");
        sb.AppendLine("[/Script/WorldBuildingEditor.WorldPartitionRuleSettings].");
        if (!string.IsNullOrWhiteSpace(settings.DefaultEditorIniPath))
            sb.AppendLine($"Rule oracle file: {settings.DefaultEditorIniPath}");
        sb.AppendLine();
        sb.AppendLine("Below are the distinct PROBLEM SIGNATURES found in one rules pass. A signature is a family of");
        sb.AppendLine("warnings/errors sharing the same message with different operands. For each one, diagnose the");
        sb.AppendLine("most likely root cause and propose a concrete fix.");
        sb.AppendLine();
        sb.AppendLine("FIX KINDS - pick the one that matches what a tool can realistically do:");
        sb.AppendLine("  - TriageOnly       : nothing is broken; the rows just need a review decision");
        sb.AppendLine("                       (mark read / approve / flag with a justification).");
        sb.AppendLine("  - NoiseSuppression : recurring, harmless output that should be filtered out from now on");
        sb.AppendLine("                       (e.g. helper actors, shadow proxies). Provide the substring to filter.");
        sb.AppendLine("  - IniRuleChange    : the World Partition rule config is wrong or incomplete.");
        sb.AppendLine("  - ActorDataChange  : the actor/asset data itself must change (level edit). The tool cannot");
        sb.AppendLine("                       apply this; it will be delegated to a Cursor agent.");
        sb.AppendLine("  - Investigate      : not enough information to act; explain what to look at.");
        sb.AppendLine();
        sb.AppendLine("ACTIONS - list the concrete steps. Only these action types exist:");
        sb.AppendLine("  { \"type\": \"AddNoisePattern\", \"value\": \"Nanite_Shadow\" }");
        sb.AppendLine("  { \"type\": \"AddUnclassifiedToken\", \"value\": \"#_TO_CLASSIFY\" }");
        sb.AppendLine("  { \"type\": \"MarkRead\" }");
        sb.AppendLine("  { \"type\": \"Approve\", \"comment\": \"why these assignments are correct\" }");
        sb.AppendLine("  { \"type\": \"Flag\", \"comment\": \"why these assignments are wrong\" }");
        sb.AppendLine("  { \"type\": \"EditIni\", \"section\": \"...\", \"key\": \"...\", \"from\": \"...\", \"to\": \"...\" }");
        sb.AppendLine("Use an empty \"actions\" array for ActorDataChange and Investigate.");
        sb.AppendLine();
        sb.AppendLine("HOW TO SELECT ROWS: do NOT enumerate rows. Give a \"match\" pattern; the tool applies it to every");
        sb.AppendLine("record so coverage is exhaustive. A record matches when ALL provided fields hold (logical AND):");
        sb.AppendLine("  - assignmentType    : one of None | HLODLayer | IncludeInHLOD | DataLayer | RuntimeGrid");
        sb.AppendLine("  - value             : exact assigned value");
        sb.AppendLine("  - valueRegex        : regex on the value");
        sb.AppendLine("  - actorNamePrefix   : leaf actor name (after the last '/') starts with this");
        sb.AppendLine("  - actorNameContains : leaf actor name contains this substring");
        sb.AppendLine("  - actorPathContains : full Outliner path contains this substring");
        sb.AppendLine("  - actorPathRegex    : regex on the full Outliner path");
        sb.AppendLine("Make each pattern specific enough to cover only what your diagnosis justifies.");
        sb.AppendLine();
        sb.AppendLine("Set \"risk\" to Low only when applying the fix cannot break anything (a review decision or a");
        sb.AppendLine("noise filter on an obviously harmless actor family). Anything touching rules or data is at");
        sb.AppendLine("least Medium. Set \"confidence\" from 1 (guess) to 5 (certain).");
        if (!string.IsNullOrWhiteSpace(settings.AiExtraInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("Additional instructions:");
            sb.AppendLine(settings.AiExtraInstructions);
        }
        sb.AppendLine();
        sb.AppendLine("Output format (two parts, in this order):");
        sb.AppendLine("  1. A short plain-English summary (a few sentences) of what is going on in this pass.");
        sb.AppendLine("  2. The machine-readable plan as one JSON object inside a ```json fenced code block:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"recommendations\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"signatureId\": \"WPR-XXXXXXXX\",");
        sb.AppendLine("      \"title\": \"...\",");
        sb.AppendLine("      \"rootCause\": \"...\",");
        sb.AppendLine("      \"recommendation\": \"...\",");
        sb.AppendLine("      \"fixKind\": \"NoiseSuppression\",");
        sb.AppendLine("      \"risk\": \"Low\",");
        sb.AppendLine("      \"confidence\": 4,");
        sb.AppendLine("      \"match\": { \"actorNameContains\": \"Nanite_Shadow\" },");
        sb.AppendLine("      \"actions\": [ { \"type\": \"AddNoisePattern\", \"value\": \"Nanite_Shadow\" } ]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();

        sb.Append("Session: ").AppendLine(report.SessionName);
        if (!string.IsNullOrWhiteSpace(report.World)) sb.Append("World: ").AppendLine(report.World);
        if (!string.IsNullOrWhiteSpace(report.BuildNumber)) sb.Append("TeamCity build: #").AppendLine(report.BuildNumber);
        if (!string.IsNullOrWhiteSpace(scopeTitle)) sb.Append("Scope: ").AppendLine(scopeTitle);
        sb.AppendLine();

        sb.AppendLine("PROBLEM SIGNATURES:");
        foreach (var bucket in buckets.Take(MaxSignatures))
        {
            var first = bucket.Records[0];
            sb.Append('[').Append(bucket.Signature.Id).Append("] ")
              .Append(bucket.Count).Append(" record(s), severity ").Append(bucket.Signature.Severity)
              .Append(", category ").Append(first.CategoryLabel)
              .AppendLine();
            sb.Append("    Message: ").AppendLine(bucket.Signature.Title);
            sb.AppendLine("    Sample actors:");
            foreach (var r in bucket.Records.Take(SamplesPerSignature))
            {
                sb.Append("      - ").Append(r.DisplayActor);
                if (!string.IsNullOrWhiteSpace(r.Value)) sb.Append("  |  ").Append(r.AssignmentType).Append('=').Append(r.Value);
                sb.AppendLine();
            }
            if (bucket.Count > SamplesPerSignature)
                sb.Append("      ... and ").Append(bucket.Count - SamplesPerSignature).AppendLine(" more");
        }
        if (buckets.Count > MaxSignatures)
            sb.Append("(").Append(buckets.Count - MaxSignatures).AppendLine(" less frequent signatures omitted)");

        return sb.ToString();
    }
}

/// <summary>Extracts the strict-JSON plan from the (possibly noisy) agent output.</summary>
public static class FixAdvisorResultParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static FixPlan Parse(string? text)
    {
        var json = ExtractJson(text);
        if (json is null) return new FixPlan { Narrative = Narrative(text, null) };

        try
        {
            var dto = JsonSerializer.Deserialize<PlanDto>(json, Options);
            if (dto?.Recommendations is null) return new FixPlan { Narrative = Narrative(text, json) };

            var recommendations = dto.Recommendations
                .Where(r => r is not null)
                .Select(Map)
                .ToList();

            return new FixPlan { Recommendations = recommendations, Narrative = Narrative(text, json) };
        }
        catch (JsonException)
        {
            return new FixPlan { Narrative = Narrative(text, json) };
        }
    }

    private static FixRecommendation Map(RecommendationDto dto)
    {
        MatchRule? match = null;
        if (dto.Match is not null)
        {
            match = new MatchRule
            {
                AssignmentType = Nn(dto.Match.AssignmentType),
                Value = Nn(dto.Match.Value),
                ValueRegex = Nn(dto.Match.ValueRegex),
                ActorNamePrefix = Nn(dto.Match.ActorNamePrefix),
                ActorNameContains = Nn(dto.Match.ActorNameContains),
                ActorPathContains = Nn(dto.Match.ActorPathContains),
                ActorPathRegex = Nn(dto.Match.ActorPathRegex)
            };
            if (!match.HasAnyCondition) match = null;
        }

        var actions = (dto.Actions ?? new List<ActionDto>())
            .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Type))
            .Select(a => new FixAction
            {
                Type = a!.Type!.Trim(),
                Value = Nn(a.Value),
                Section = Nn(a.Section),
                Key = Nn(a.Key),
                From = Nn(a.From),
                To = Nn(a.To),
                Comment = Nn(a.Comment)
            })
            .ToList();

        return new FixRecommendation
        {
            SignatureId = dto.SignatureId?.Trim() ?? string.Empty,
            Title = dto.Title?.Trim() ?? "(untitled)",
            RootCause = dto.RootCause?.Trim() ?? string.Empty,
            Recommendation = dto.Recommendation?.Trim() ?? string.Empty,
            Kind = ParseEnum(dto.FixKind, FixKind.Investigate),
            Risk = ParseEnum(dto.Risk, FixRisk.Medium),
            Confidence = Math.Clamp(dto.Confidence ?? 3, 1, 5),
            Match = match,
            Actions = actions
        };
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    /// <summary>The prose the agent wrote before the JSON block, shown above the recommendation cards.</summary>
    private static string Narrative(string? text, string? json)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Trim();

        var cut = s.IndexOf("```", StringComparison.Ordinal);
        if (cut < 0 && json is not null) cut = s.IndexOf(json, StringComparison.Ordinal);
        return (cut > 0 ? s[..cut] : s).Trim();
    }

    private static string? ExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();

        var fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = s.IndexOf('{', fence);
            var end = s.LastIndexOf('}');
            if (start >= 0 && end > start) return s.Substring(start, end - start + 1);
        }

        var first = s.IndexOf('{');
        var last = s.LastIndexOf('}');
        return first >= 0 && last > first ? s.Substring(first, last - first + 1) : null;
    }

    private static string? Nn(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed class PlanDto
    {
        [JsonPropertyName("recommendations")] public List<RecommendationDto>? Recommendations { get; set; }
    }

    private sealed class RecommendationDto
    {
        [JsonPropertyName("signatureId")] public string? SignatureId { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("rootCause")] public string? RootCause { get; set; }
        [JsonPropertyName("recommendation")] public string? Recommendation { get; set; }
        [JsonPropertyName("fixKind")] public string? FixKind { get; set; }
        [JsonPropertyName("risk")] public string? Risk { get; set; }
        [JsonPropertyName("confidence")] public int? Confidence { get; set; }
        [JsonPropertyName("match")] public MatchDto? Match { get; set; }
        [JsonPropertyName("actions")] public List<ActionDto>? Actions { get; set; }
    }

    private sealed class MatchDto
    {
        [JsonPropertyName("assignmentType")] public string? AssignmentType { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
        [JsonPropertyName("valueRegex")] public string? ValueRegex { get; set; }
        [JsonPropertyName("actorNamePrefix")] public string? ActorNamePrefix { get; set; }
        [JsonPropertyName("actorNameContains")] public string? ActorNameContains { get; set; }
        [JsonPropertyName("actorPathContains")] public string? ActorPathContains { get; set; }
        [JsonPropertyName("actorPathRegex")] public string? ActorPathRegex { get; set; }
    }

    private sealed class ActionDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
        [JsonPropertyName("section")] public string? Section { get; set; }
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("comment")] public string? Comment { get; set; }
    }
}
