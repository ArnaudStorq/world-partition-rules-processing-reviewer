using System.Text;
using System.Text.RegularExpressions;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Ai;

/// <summary>
/// A deterministic selection rule produced by the AI. Every non-empty condition must hold (logical
/// AND); a rule with no condition matches nothing, which is the safe default.
///
/// The AI never enumerates rows: it describes a pattern and the tool expands it over every
/// candidate, so coverage never depends on the model being exhaustive.
/// </summary>
public class MatchRule
{
    public string? AssignmentType { get; init; }
    public string? Value { get; init; }
    public string? ValueRegex { get; init; }
    public string? ActorNamePrefix { get; init; }
    public string? ActorNameContains { get; init; }
    public string? ActorPathContains { get; init; }
    public string? ActorPathRegex { get; init; }

    public bool HasAnyCondition =>
        !string.IsNullOrWhiteSpace(AssignmentType) ||
        !string.IsNullOrWhiteSpace(Value) ||
        !string.IsNullOrWhiteSpace(ValueRegex) ||
        !string.IsNullOrWhiteSpace(ActorNamePrefix) ||
        !string.IsNullOrWhiteSpace(ActorNameContains) ||
        !string.IsNullOrWhiteSpace(ActorPathContains) ||
        !string.IsNullOrWhiteSpace(ActorPathRegex);

    /// <summary>Human-readable form of the rule, shown in the preview dialog.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(AssignmentType)) parts.Add($"type = {AssignmentType}");
        if (!string.IsNullOrWhiteSpace(Value)) parts.Add($"value = {Value}");
        if (!string.IsNullOrWhiteSpace(ValueRegex)) parts.Add($"value ~ /{ValueRegex}/");
        if (!string.IsNullOrWhiteSpace(ActorNamePrefix)) parts.Add($"name starts with {ActorNamePrefix}");
        if (!string.IsNullOrWhiteSpace(ActorNameContains)) parts.Add($"name contains {ActorNameContains}");
        if (!string.IsNullOrWhiteSpace(ActorPathContains)) parts.Add($"path contains {ActorPathContains}");
        if (!string.IsNullOrWhiteSpace(ActorPathRegex)) parts.Add($"path ~ /{ActorPathRegex}/");
        return parts.Count == 0 ? "(no condition)" : string.Join(" AND ", parts);
    }

    public override string ToString() => Describe();
}

/// <summary>Applies a <see cref="MatchRule"/> to anything exposing an assignment type, a value and a path.</summary>
public static class MatchRuleEvaluator
{
    public static bool Matches(MatchRule? rule, string assignmentType, string value, string actorPath)
    {
        if (rule is null || !rule.HasAnyCondition) return false;

        if (!string.IsNullOrWhiteSpace(rule.AssignmentType) &&
            !string.Equals(assignmentType, rule.AssignmentType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Value) &&
            !string.Equals(value, rule.Value, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.ValueRegex) && !RegexOk(rule.ValueRegex!, value))
            return false;

        var leaf = LeafName(actorPath);
        if (!string.IsNullOrWhiteSpace(rule.ActorNamePrefix) &&
            !leaf.StartsWith(rule.ActorNamePrefix!, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.ActorNameContains) &&
            leaf.IndexOf(rule.ActorNameContains!, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (!string.IsNullOrWhiteSpace(rule.ActorPathContains) &&
            actorPath.IndexOf(rule.ActorPathContains!, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (!string.IsNullOrWhiteSpace(rule.ActorPathRegex) && !RegexOk(rule.ActorPathRegex!, actorPath))
            return false;

        return true;
    }

    public static bool Matches(MatchRule? rule, RuleRecord record) =>
        Matches(rule, record.AssignmentType.ToString(), record.Value ?? string.Empty, record.DisplayActor);

    public static IReadOnlyList<RuleRecord> Expand(MatchRule? rule, IEnumerable<RuleRecord> records)
    {
        if (rule is null || !rule.HasAnyCondition) return Array.Empty<RuleRecord>();
        return records.Where(r => Matches(rule, r)).ToList();
    }

    private static string LeafName(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var i = path.LastIndexOf('/');
        return i >= 0 && i < path.Length - 1 ? path[(i + 1)..] : path;
    }

    /// <summary>An invalid AI-authored regex never matches, rather than throwing mid-plan.</summary>
    private static bool RegexOk(string pattern, string input)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase); }
        catch { return false; }
    }

    /// <summary>Renders the affected rows as the compact text block reused by prompts and previews.</summary>
    public static string Summarize(IReadOnlyList<RuleRecord> records, int maxItems)
    {
        var sb = new StringBuilder();
        foreach (var r in records.Take(maxItems))
            sb.Append("  - ").AppendLine(r.ToPlainLine());
        if (records.Count > maxItems)
            sb.Append("  ... and ").Append(records.Count - maxItems).AppendLine(" more");
        return sb.ToString();
    }
}
