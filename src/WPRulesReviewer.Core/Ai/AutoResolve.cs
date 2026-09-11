using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Ai;

// (types below)

/// <summary>One unread row offered to the AI for auto-resolution, addressed by <see cref="Index"/>.</summary>
public sealed class AutoResolveCandidate
{
    public int Index { get; init; }
    public string ActorPath { get; init; } = string.Empty;
    public string AssignmentType { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

/// <summary>A human review report (approved/suspicious) used as the knowledge base.</summary>
public sealed class AutoResolveKnownReport
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty; // "APPROVED" or "SUSPICIOUS"
    public int Confidence { get; init; } = 5;
    public string Comment { get; init; } = string.Empty;
    public IReadOnlyList<string> Actors { get; init; } = Array.Empty<string>();

    /// <summary>The underlying report, carried through for read-only display in the UI.</summary>
    public SuspiciousReport? Source { get; init; }
}

/// <summary>A cluster of candidates the AI proposes to mark as read, backed by one or more reports.</summary>
public sealed class AutoResolveGroup
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<string> ReportIds { get; init; } = Array.Empty<string>();
    public int Confidence { get; init; } = 3; // AI confidence, 1..5
    public string Rationale { get; init; } = string.Empty;
    public IReadOnlyList<int> Indices { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Deterministic pattern the tool applies to every candidate so coverage never depends on the AI
    /// exhaustively enumerating indices. When present it is the primary source of matched rows.
    /// </summary>
    public AutoResolveMatch? Match { get; init; }
}

/// <summary>
/// A deterministic match rule produced by the AI, addressing auto-resolve candidates.
/// The conditions and their evaluation live in <see cref="MatchRule"/>, shared with the Fix Advisor.
/// </summary>
public sealed class AutoResolveMatch : MatchRule
{
}

/// <summary>Applies an <see cref="AutoResolveMatch"/> rule to candidate rows.</summary>
public static class AutoResolveMatcher
{
    public static IReadOnlyList<int> MatchingIndices(AutoResolveMatch? rule, IReadOnlyList<AutoResolveCandidate> candidates)
    {
        var result = new List<int>();
        if (rule is null || !rule.HasAnyCondition) return result;
        foreach (var c in candidates)
            if (MatchRuleEvaluator.Matches(rule, c.AssignmentType, c.Value, c.ActorPath)) result.Add(c.Index);
        return result;
    }
}

public sealed class AutoResolvePlan
{
    public IReadOnlyList<AutoResolveGroup> Groups { get; init; } = Array.Empty<AutoResolveGroup>();

    /// <summary>
    /// Actors the AI reasoned about (they relate to one or more reports) but deliberately left for the
    /// human to decide - e.g. a suspicious report contradicts an approval. These are NOT marked read; the
    /// rationale is surfaced in the grid as an "AI Check" note. Actors matching no report are absent here.
    /// </summary>
    public IReadOnlyList<AutoResolveGroup> Deferrals { get; init; } = Array.Empty<AutoResolveGroup>();
}

/// <summary>Builds the strict-JSON prompt for the "Auto-resolve reading" feature.</summary>
public static class AutoResolvePromptBuilder
{
    public static string Build(SessionReport report, IReadOnlyList<AutoResolveKnownReport> reports,
        IReadOnlyList<AutoResolveCandidate> candidates, AppSettings settings, string rowsFilePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("IMPORTANT: Respond entirely in English. Every word you output - the live progress narration,");
        sb.AppendLine("the human-readable explanation, and every JSON title/rationale - MUST be in English, even though");
        sb.AppendLine("the reports, comments and actor paths may contain other languages. Never answer in French.");
        sb.AppendLine();
        sb.AppendLine("You are assisting a technical artist who reviews the output of the Sundance WorldPartitionRuleBuilder.");
        sb.AppendLine("The reviewer has already written REVIEW REPORTS about individual actors:");
        sb.AppendLine("  - APPROVED means the reviewer confirmed the assignment is correct/expected.");
        sb.AppendLine("  - SUSPICIOUS means the reviewer flagged the assignment as wrong or doubtful.");
        sb.AppendLine("Each report has a free-text comment explaining the human decision and a confidence from 1 to 5.");
        sb.AppendLine();
        sb.AppendLine("Your task: inspect the CANDIDATE rows (currently unread) and decide which ones are already");
        sb.AppendLine("covered by an existing report - i.e. they describe the same kind of situation, so the reviewer");
        sb.AppendLine("can safely mark them as READ (resolved), exactly like auto-resolving already-known merge conflicts.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("  - Be conservative. Only cover candidates that clearly match a report's situation:");
        sb.AppendLine("    comparable assignment type and value AND a comparable actor path/pattern, consistent with the comment.");
        sb.AppendLine("  - Prefer high-confidence reports. Never cover a candidate that a SUSPICIOUS report would contradict.");
        sb.AppendLine("  - Group candidates that share the same reason. Give each group a short, human title and a rationale.");
        sb.AppendLine("  - Reference EVERY report a group relies on by its id in \"reportIds\" (a candidate can be supported by several reports).");
        sb.AppendLine("  - Set \"confidence\" to how sure you are (1 = weak match, 5 = certain) that the group is correctly covered.");
        sb.AppendLine("  - If no report supports a situation, leave it out.");
        sb.AppendLine("  - Write EVERYTHING in English only - the human-readable explanation and every title and");
        sb.AppendLine("    rationale - regardless of any language used in the reports, comments or actor paths.");
        sb.AppendLine();
        sb.AppendLine("DEFERRALS - actors you examined but deliberately leave to the human:");
        sb.AppendLine("  Besides the groups you auto-resolve, some candidates DO relate to a report yet you must NOT mark");
        sb.AppendLine("  them read - typically because a SUSPICIOUS report contradicts an approval, or the situation is");
        sb.AppendLine("  ambiguous. List these in a separate \"deferrals\" array so the reviewer sees your reasoning next to");
        sb.AppendLine("  the actor (a per-row \"AI Check\" note). Each deferral uses the SAME shape as a group: a \"match\"");
        sb.AppendLine("  rule, \"reportIds\", a \"confidence\" (how sure you are it needs human attention) and a \"rationale\"");
        sb.AppendLine("  explaining WHY you left it to the human (name the conflicting report). IMPORTANT: only include");
        sb.AppendLine("  actors that relate to at least one report. Candidates matching NO report must NOT appear anywhere -");
        sb.AppendLine("  neither in \"groups\" nor in \"deferrals\" (they simply stay unread with no note).");
        sb.AppendLine("  A candidate must never appear in both \"groups\" and \"deferrals\".");
        sb.AppendLine();
        sb.AppendLine("HOW TO SELECT ROWS - IMPORTANT:");
        sb.AppendLine("  Do NOT enumerate individual row indices. Instead, describe each group with a \"match\" rule (a");
        sb.AppendLine("  pattern). The tool applies your rule to EVERY candidate row, so coverage is exhaustive and no");
        sb.AppendLine("  matching row is ever missed. Make each rule specific enough to only cover what the report justifies.");
        sb.AppendLine("  A candidate matches a rule when ALL provided fields hold (logical AND). Available fields:");
        sb.AppendLine("    - assignmentType : exact type, one of HLODLayer | IncludeInHLOD | DataLayer | RuntimeGrid");
        sb.AppendLine("    - value          : exact assigned value (e.g. DL_RENDER)");
        sb.AppendLine("    - valueRegex     : regex on the value (use instead of 'value' when needed)");
        sb.AppendLine("    - actorNamePrefix: the leaf actor name (after the last '/') starts with this (e.g. SM_)");
        sb.AppendLine("    - actorNameContains : the leaf actor name contains this substring");
        sb.AppendLine("    - actorPathContains : the full Outliner path contains this substring");
        sb.AppendLine("    - actorPathRegex : regex on the full Outliner path");
        sb.AppendLine("  Omit fields you don't need. Example: DL_RENDER approved for SM_ static meshes becomes");
        sb.AppendLine("    \"match\": { \"assignmentType\": \"DataLayer\", \"value\": \"DL_RENDER\", \"actorNamePrefix\": \"SM_\" }");
        if (!string.IsNullOrWhiteSpace(settings.AiExtraInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("Additional instructions:");
            sb.AppendLine(settings.AiExtraInstructions);
        }
        sb.AppendLine();
        sb.AppendLine("Output format (two parts, in this order):");
        sb.AppendLine("  1. A short human-readable explanation (a few sentences, plain English) describing your overall");
        sb.AppendLine("     reasoning: which reports you leaned on, the main patterns you matched, and anything you left");
        sb.AppendLine("     unread on purpose. Write it for a technical artist reviewing your decisions.");
        sb.AppendLine("  2. Then the machine-readable plan as a single JSON object inside a ```json fenced code block,");
        sb.AppendLine("     with exactly this shape (use \"match\", not \"indices\"):");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"groups\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"title\": \"...\",");
        sb.AppendLine("      \"reportIds\": [\"A1\", \"S2\"],");
        sb.AppendLine("      \"confidence\": 4,");
        sb.AppendLine("      \"rationale\": \"...\",");
        sb.AppendLine("      \"match\": { \"assignmentType\": \"DataLayer\", \"value\": \"DL_RENDER\", \"actorNamePrefix\": \"SM_\" }");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"deferrals\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"title\": \"...\",");
        sb.AppendLine("      \"reportIds\": [\"S1\"],");
        sb.AppendLine("      \"confidence\": 3,");
        sb.AppendLine("      \"rationale\": \"Left to the human: suspicious report S1 says these small books under _EXT should be excluded from HLOD, contradicting the _EXT approval.\",");
        sb.AppendLine("      \"match\": { \"assignmentType\": \"IncludeInHLOD\", \"value\": \"true\", \"actorPathContains\": \"LI_ViaductBridge_EXT\" }");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.Append("Session: ").AppendLine(report.SessionName);
        sb.AppendLine();

        sb.AppendLine("REPORTS:");
        if (reports.Count == 0)
        {
            sb.AppendLine("(none yet)");
        }
        else
        {
            foreach (var r in reports)
            {
                sb.Append('[').Append(r.Id).Append("] ").Append(r.Kind)
                  .Append(" (confidence ").Append(r.Confidence).Append("/5): \"")
                  .Append(Clean(r.Comment)).AppendLine("\"");
                foreach (var a in r.Actors)
                    sb.Append("     - ").AppendLine(Clean(a));
            }
        }
        sb.AppendLine();

        sb.AppendLine("CANDIDATES:");
        sb.Append("The candidate rows are NOT inlined in this prompt. Read them from this UTF-8 text file: ")
          .AppendLine(rowsFilePath);
        sb.AppendLine("File format: a short header, a separator line, then one candidate per line:");
        sb.AppendLine("  index | assignmentType=value | category | status | actorPath | reason");
        sb.Append("It contains ").Append(candidates.Count)
          .AppendLine(" candidate rows. Use the leading integer 'index' of each line in your \"indices\".");

        return sb.ToString();
    }

    private static string Clean(string? s) => (s ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}

/// <summary>Extracts the strict-JSON plan from the (possibly noisy) agent output.</summary>
public static class AutoResolveResultParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static AutoResolvePlan Parse(string? text)
    {
        var json = ExtractJson(text);
        if (json is null) return new AutoResolvePlan();

        try
        {
            var dto = JsonSerializer.Deserialize<PlanDto>(json, Options);
            if (dto is null) return new AutoResolvePlan();

            return new AutoResolvePlan
            {
                Groups = MapGroups(dto.Groups),
                Deferrals = MapGroups(dto.Deferrals)
            };
        }
        catch
        {
            return new AutoResolvePlan();
        }
    }

    private static IReadOnlyList<AutoResolveGroup> MapGroups(List<GroupDto>? dtos)
    {
        if (dtos is null) return Array.Empty<AutoResolveGroup>();

        return dtos
            .Where(g => g is not null)
            .Select(g =>
            {
                var ids = new List<string>();
                if (g!.ReportIds is not null)
                    ids.AddRange(g.ReportIds.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(g.ReportId))
                    ids.Add(g.ReportId!);
                var confidence = Math.Clamp(g.Confidence ?? 3, 1, 5);

                AutoResolveMatch? match = null;
                if (g.Match is not null)
                {
                    match = new AutoResolveMatch
                    {
                        AssignmentType = Nn(g.Match.AssignmentType),
                        Value = Nn(g.Match.Value),
                        ValueRegex = Nn(g.Match.ValueRegex),
                        ActorNamePrefix = Nn(g.Match.ActorNamePrefix),
                        ActorNameContains = Nn(g.Match.ActorNameContains),
                        ActorPathContains = Nn(g.Match.ActorPathContains),
                        ActorPathRegex = Nn(g.Match.ActorPathRegex)
                    };
                    if (!match.HasAnyCondition) match = null;
                }

                return new AutoResolveGroup
                {
                    Title = g.Title ?? string.Empty,
                    ReportIds = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Confidence = confidence,
                    Rationale = g.Rationale ?? string.Empty,
                    Indices = (g.Indices ?? new List<int>()).Distinct().ToList(),
                    Match = match
                };
            })
            .Where(g => g.Indices.Count > 0 || g.Match is not null)
            .ToList();
    }

    /// <summary>Find a JSON object in the text: a ```json fenced block, else the outermost braces.</summary>
    private static string? ExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();

        int fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            int start = s.IndexOf('{', fence);
            int end = s.LastIndexOf('}');
            if (start >= 0 && end > start) return s.Substring(start, end - start + 1);
        }

        int first = s.IndexOf('{');
        int last = s.LastIndexOf('}');
        if (first >= 0 && last > first) return s.Substring(first, last - first + 1);
        return null;
    }

    private sealed class PlanDto
    {
        [JsonPropertyName("groups")] public List<GroupDto>? Groups { get; set; }
        [JsonPropertyName("deferrals")] public List<GroupDto>? Deferrals { get; set; }
    }

    private static string? Nn(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed class GroupDto
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("reportId")] public string? ReportId { get; set; }
        [JsonPropertyName("reportIds")] public List<string>? ReportIds { get; set; }
        [JsonPropertyName("confidence")] public int? Confidence { get; set; }
        [JsonPropertyName("rationale")] public string? Rationale { get; set; }
        [JsonPropertyName("indices")] public List<int>? Indices { get; set; }
        [JsonPropertyName("match")] public MatchDto? Match { get; set; }
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
}
