using System.Text;
using System.Text.RegularExpressions;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Analysis;

/// <summary>
/// Stable identity of a *class* of problem, independent of the actor it happened on.
/// Two records share a signature when they describe the same situation with different operands,
/// e.g. "Expected DataLayer 'DL_A10_Fireflies_1' does not exist" and "... 'DL_A10_Fireflies_7' ...".
///
/// The signature is the single grouping key used by the Warning Explorer, the charts and the AI
/// prompts, so every view reports the same counts.
/// </summary>
public sealed partial class ProblemSignature : IEquatable<ProblemSignature>
{
    private ProblemSignature(RecordCategory category, WarningKind kind, AssignmentType assignmentType,
        string normalizedReason, AnomalySeverity severity)
    {
        Category = category;
        Kind = kind;
        AssignmentType = assignmentType;
        NormalizedReason = normalizedReason;
        Severity = severity;
        Id = BuildId(category, kind, assignmentType, normalizedReason);
    }

    public RecordCategory Category { get; }
    public WarningKind Kind { get; }
    public AssignmentType AssignmentType { get; }

    /// <summary>Reason text with all volatile operands collapsed to placeholders.</summary>
    public string NormalizedReason { get; }

    public AnomalySeverity Severity { get; }

    /// <summary>Short, stable id (e.g. "WPR-3F2A91") used by AI recommendations and the fix journal.</summary>
    public string Id { get; }

    /// <summary>Human-facing label shown in the tree, the charts and the recommendation cards.</summary>
    public string Title => NormalizedReason;

    public static ProblemSignature From(RuleRecord r)
    {
        var raw =
            !string.IsNullOrWhiteSpace(r.StatusReason) ? r.StatusReason!
            : !string.IsNullOrWhiteSpace(r.Reason) ? r.Reason!
            : !string.IsNullOrWhiteSpace(r.Value) ? r.Value
            : r.CategoryLabel;

        return new ProblemSignature(r.Category, r.WarningKind, r.AssignmentType, Normalize(raw), r.Severity);
    }

    // Volatile operands, collapsed so that a family of messages folds into one signature.
    [GeneratedRegex("'[^']*'")] private static partial Regex QuotedValue();
    [GeneratedRegex(@"\b[0-9A-Fa-f]{8}(?:-?[0-9A-Fa-f]{4}){3}-?[0-9A-Fa-f]{12}\b")] private static partial Regex Guid();
    [GeneratedRegex(@"\[[^\]]*\]")] private static partial Regex Bracketed();
    [GeneratedRegex(@"_\d+\b")] private static partial Regex NumericSuffix();
    [GeneratedRegex(@"\b\d+\b")] private static partial Regex StandaloneNumber();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex Whitespace();

    /// <summary>
    /// Collapses the parts of a message that vary from actor to actor. Order matters: GUIDs and
    /// quoted operands go first, so their inner digits are not turned into '#' beforehand.
    /// </summary>
    public static string Normalize(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return string.Empty;

        var s = reason.Replace('\r', ' ').Replace('\n', ' ');
        s = Guid().Replace(s, "\u2026");
        s = QuotedValue().Replace(s, "'\u2026'");
        s = Bracketed().Replace(s, "[\u2026]");
        s = NumericSuffix().Replace(s, "_#");
        s = StandaloneNumber().Replace(s, "#");
        s = Whitespace().Replace(s, " ");
        return s.Trim();
    }

    /// <summary>
    /// FNV-1a over the identity fields. Deterministic across runs and machines (unlike string
    /// hashing), which matters because the id is persisted in the fix journal and quoted by the AI.
    /// </summary>
    private static string BuildId(RecordCategory category, WarningKind kind, AssignmentType type, string reason)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        var payload = $"{category}|{kind}|{type}|{reason}";
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(payload))
        {
            hash ^= b;
            hash *= prime;
        }
        return $"WPR-{hash:X8}";
    }

    public bool Equals(ProblemSignature? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as ProblemSignature);
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => $"{Id} {Title}";
}
