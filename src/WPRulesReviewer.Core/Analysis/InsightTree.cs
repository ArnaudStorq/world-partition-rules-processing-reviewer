using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Analysis;

/// <summary>One level of grouping available in the Warning Explorer, the charts and the sunburst.</summary>
public enum GroupDimension
{
    /// <summary>Errors / Warnings / Logs / Verbose, the way the engine wrote the lines.</summary>
    LogVerbosity,

    Status,
    Severity,
    Category,
    WarningKind,
    ProblemSignature,
    AssignmentType,
    Value,

    /// <summary>Expands into one level per '/' segment of the Outliner path (Unreal-like browsing).</summary>
    OutlinerPath,

    Actor
}

/// <summary>Named grouping order offered in the UI.</summary>
public sealed class InsightPreset
{
    public InsightPreset(string name, params GroupDimension[] dimensions)
    {
        Name = name;
        Dimensions = dimensions;
    }

    public string Name { get; }
    public IReadOnlyList<GroupDimension> Dimensions { get; }

    /// <summary>The combo box falls back to ToString when no item template applies.</summary>
    public override string ToString() => Name;

    public static readonly InsightPreset ByProblem = new("By problem",
        GroupDimension.LogVerbosity, GroupDimension.ProblemSignature, GroupDimension.OutlinerPath, GroupDimension.Actor);

    public static readonly InsightPreset ByPath = new("By Outliner path",
        GroupDimension.OutlinerPath, GroupDimension.ProblemSignature, GroupDimension.Actor);

    public static readonly InsightPreset ByValue = new("By assigned value",
        GroupDimension.AssignmentType, GroupDimension.Value, GroupDimension.ProblemSignature, GroupDimension.Actor);

    public static readonly InsightPreset ByStatus = new("By triage status",
        GroupDimension.Status, GroupDimension.ProblemSignature, GroupDimension.OutlinerPath, GroupDimension.Actor);

    public static readonly InsightPreset BySeverity = new("By severity",
        GroupDimension.Severity, GroupDimension.WarningKind, GroupDimension.ProblemSignature, GroupDimension.Actor);

    public static IReadOnlyList<InsightPreset> All { get; } = new[] { ByProblem, ByPath, ByValue, ByStatus, BySeverity };
}

/// <summary>
/// A node of the aggregation tree. Children are built on first access so a 10 000-record session
/// only pays for the branches the user actually opens.
/// </summary>
public sealed class InsightNode
{
    private readonly InsightTree _tree;
    private readonly IReadOnlyList<GroupDimension> _remaining;
    private readonly int _pathSegment;
    private IReadOnlyList<InsightNode>? _children;

    internal InsightNode(InsightTree tree, string title, GroupDimension? dimension, IReadOnlyList<RuleRecord> records,
        IReadOnlyList<GroupDimension> remaining, int pathSegment, int depth)
    {
        _tree = tree;
        _remaining = remaining;
        _pathSegment = pathSegment;

        Title = title;
        Dimension = dimension;
        Records = records;
        Depth = depth;

        foreach (var r in records)
        {
            if (r.Severity > MaxSeverity) MaxSeverity = r.Severity;
            Occurrences += Math.Max(1, r.Occurrences);
            switch (r.Status)
            {
                case ReviewStatus.Anomaly: AnomalyCount++; break;
                case ReviewStatus.NeedsReview: NeedsReviewCount++; break;
                case ReviewStatus.Expected: ExpectedCount++; break;
                case ReviewStatus.KnownNoise: KnownNoiseCount++; break;
            }
        }
    }

    public string Title { get; }

    /// <summary>Dimension that produced this node (null for the root).</summary>
    public GroupDimension? Dimension { get; }

    public int Depth { get; }

    /// <summary>Every record under this node, all levels included.</summary>
    public IReadOnlyList<RuleRecord> Records { get; }

    public int Count => Records.Count;

    /// <summary>
    /// Number of log lines behind this node. Identical lines are collapsed into one record at parse
    /// time, so this is what "how often does this happen" really means, and what the tree sorts on.
    /// </summary>
    public int Occurrences { get; private set; }

    public AnomalySeverity MaxSeverity { get; }
    public int AnomalyCount { get; }
    public int NeedsReviewCount { get; }
    public int ExpectedCount { get; }
    public int KnownNoiseCount { get; }

    /// <summary>Computed on demand: the read flag is toggled by the user long after the tree is built.</summary>
    public int ReadCount => Records.Count(r => r.IsRead);

    /// <summary>Set when this node was produced by the <see cref="GroupDimension.ProblemSignature"/> level.</summary>
    public ProblemSignature? Signature { get; internal set; }

    /// <summary>A leaf is a single record (Actor level, or nothing left to group by).</summary>
    public bool IsLeaf => Records.Count == 1 && !HasMoreLevels;

    /// <summary>The single record behind a leaf node, so the UI can open the inspector directly.</summary>
    public RuleRecord? LeafRecord => Records.Count == 1 ? Records[0] : null;

    private bool HasMoreLevels => _remaining.Count > 0;

    /// <summary>
    /// Cheap, conservative test used by the UI to decide whether to draw an expander *before*
    /// paying for <see cref="Children"/>. True means "expanding will produce something".
    /// </summary>
    public bool MayHaveChildren => HasMoreLevels && Records.Count > 1;

    public bool HasChildren => Children.Count > 0;

    public IReadOnlyList<InsightNode> Children => _children ??= _tree.BuildChildren(Records, _remaining, _pathSegment, Depth + 1);
}

/// <summary>
/// Builds a hierarchical aggregation of a <see cref="SessionReport"/> driven by an ordered list of
/// <see cref="GroupDimension"/>. This is the single source of truth shared by the Warning Explorer,
/// the Overview charts, the radial breakdown and the AI prompts.
/// </summary>
public sealed class InsightTree
{
    private readonly Dictionary<RuleRecord, ProblemSignature> _signatures = new(ReferenceEqualityComparer.Instance);

    private InsightTree(IReadOnlyList<RuleRecord> records, IReadOnlyList<GroupDimension> dimensions, string rootTitle)
    {
        Dimensions = dimensions;
        Root = new InsightNode(this, rootTitle, null, records, dimensions, 0, 0);
    }

    public IReadOnlyList<GroupDimension> Dimensions { get; }
    public InsightNode Root { get; }

    public static InsightTree Build(IEnumerable<RuleRecord> records, IReadOnlyList<GroupDimension> dimensions,
        string rootTitle = "All records")
        => new(records as IReadOnlyList<RuleRecord> ?? records.ToList(), dimensions, rootTitle);

    public static InsightTree Build(SessionReport report, InsightPreset preset)
        => Build(report.Records, preset.Dimensions, report.SessionName);

    /// <summary>Signature of a record, memoized so repeated grouping passes stay cheap.</summary>
    public ProblemSignature SignatureOf(RuleRecord record)
    {
        if (_signatures.TryGetValue(record, out var cached)) return cached;
        var signature = ProblemSignature.From(record);
        _signatures[record] = signature;
        return signature;
    }

    /// <summary>All distinct signatures with their record counts, ordered by weight (feeds the charts).</summary>
    public IReadOnlyList<SignatureBucket> SignatureBuckets(IEnumerable<RuleRecord>? scope = null)
    {
        var source = scope ?? Root.Records;
        return source
            .GroupBy(SignatureOf)
            .Select(g => new SignatureBucket(g.Key, g.ToList()))
            .OrderByDescending(b => b.Occurrences)
            .ThenByDescending(b => b.Count)
            .ToList();
    }

    internal IReadOnlyList<InsightNode> BuildChildren(IReadOnlyList<RuleRecord> records,
        IReadOnlyList<GroupDimension> remaining, int pathSegment, int depth)
    {
        if (remaining.Count == 0 || records.Count == 0) return Array.Empty<InsightNode>();

        var dimension = remaining[0];
        var rest = remaining.Skip(1).ToList();

        // The Outliner path consumes one level per segment, so it stays the current dimension until
        // the records run out of segments.
        if (dimension == GroupDimension.OutlinerPath)
            return BuildPathChildren(records, remaining, rest, pathSegment, depth);

        var groups = new Dictionary<string, List<RuleRecord>>(StringComparer.OrdinalIgnoreCase);
        var signatures = new Dictionary<string, ProblemSignature>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in records)
        {
            var key = KeyOf(r, dimension, out var signature);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = new List<RuleRecord>();
                if (signature is not null) signatures[key] = signature;
            }
            list.Add(r);
        }

        // A dimension that does not discriminate anything (one single bucket holding everything)
        // would add a pointless level, so skip straight to the next one.
        if (groups.Count == 1 && rest.Count > 0 && dimension is not GroupDimension.ProblemSignature)
            return BuildChildren(records, rest, pathSegment, depth);

        var nodes = groups.Select(kv =>
        {
            var node = new InsightNode(this, kv.Key, dimension, kv.Value, rest, 0, depth);
            if (signatures.TryGetValue(kv.Key, out var sig)) node.Signature = sig;
            return node;
        });

        // Verbosity keeps the engine's own order (Errors first) so the important bucket never sinks
        // below the thousands of Log lines; every other level is ordered by weight.
        return dimension == GroupDimension.LogVerbosity
            ? nodes.OrderBy(n => VerbosityRank(n.Title)).ToList()
            : Sorted(nodes);
    }

    /// <summary>Heaviest branch first: the tree is a "where is the volume" tool before anything else.</summary>
    private static List<InsightNode> Sorted(IEnumerable<InsightNode> nodes) =>
        nodes.OrderByDescending(n => n.Occurrences)
            .ThenByDescending(n => n.Count)
            .ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<InsightNode> BuildPathChildren(IReadOnlyList<RuleRecord> records,
        IReadOnlyList<GroupDimension> remaining, IReadOnlyList<GroupDimension> rest, int segment, int depth)
    {
        var groups = new Dictionary<string, List<RuleRecord>>(StringComparer.OrdinalIgnoreCase);
        var deeper = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in records)
        {
            var segments = PathSegments(r);
            // Records shallower than the current level are shown in place rather than dropped.
            var key = segment < segments.Length ? segments[segment] : "(no path)";
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = new List<RuleRecord>();
                deeper[key] = false;
            }
            list.Add(r);
            if (segments.Length > segment + 1) deeper[key] = true;
        }

        if (groups.Count == 0) return Array.Empty<InsightNode>();

        // Nothing left to split on this dimension: hand over to the next one.
        if (deeper.Values.All(d => !d) && groups.Count == 1 && rest.Count > 0)
            return BuildChildren(records, rest, 0, depth);

        return Sorted(groups
            .Select(kv =>
            {
                var childHasDeeper = deeper[kv.Key];
                var childRemaining = childHasDeeper ? remaining : rest;
                var childSegment = childHasDeeper ? segment + 1 : 0;
                return new InsightNode(this, kv.Key, GroupDimension.OutlinerPath, kv.Value, childRemaining, childSegment, depth);
            }));
    }

    private static string[] PathSegments(RuleRecord r)
    {
        var path = r.DisplayActor;
        return string.IsNullOrEmpty(path)
            ? Array.Empty<string>()
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private string KeyOf(RuleRecord r, GroupDimension dimension, out ProblemSignature? signature)
    {
        signature = null;
        switch (dimension)
        {
            case GroupDimension.LogVerbosity:
                return VerbosityLabel(r.Verbosity);
            case GroupDimension.Status:
                return StatusLabel(r.Status);
            case GroupDimension.Severity:
                return r.Severity == AnomalySeverity.None ? "No severity" : $"{r.Severity} severity";
            case GroupDimension.Category:
                return r.CategoryLabel;
            case GroupDimension.WarningKind:
                return r.WarningKind == WarningKind.None ? "No warning kind" : SpaceCamelCase(r.WarningKind.ToString());
            case GroupDimension.ProblemSignature:
                signature = SignatureOf(r);
                return signature.Title;
            case GroupDimension.AssignmentType:
                return r.AssignmentType == AssignmentType.None ? "No assignment" : r.AssignmentType.ToString();
            case GroupDimension.Value:
                return string.IsNullOrEmpty(r.Value) ? "(none)" : r.Value;
            case GroupDimension.Actor:
                return string.IsNullOrEmpty(r.DisplayActor) ? "(no actor)" : r.DisplayActor;
            default:
                return r.CategoryLabel;
        }
    }

    public static string VerbosityLabel(LogVerbosity verbosity) => verbosity switch
    {
        LogVerbosity.Error => "Errors",
        LogVerbosity.Warning => "Warnings",
        LogVerbosity.Verbose => "Verbose",
        _ => "Logs"
    };

    /// <summary>Rank used to keep the verbosity buckets in engine order rather than by size.</summary>
    private static int VerbosityRank(string label) => label switch
    {
        "Errors" => 0,
        "Warnings" => 1,
        "Logs" => 2,
        "Verbose" => 3,
        _ => 4
    };

    public static string StatusLabel(ReviewStatus status) => status switch
    {
        ReviewStatus.Anomaly => "Anomalies",
        ReviewStatus.NeedsReview => "Needs review",
        ReviewStatus.Expected => "Expected",
        ReviewStatus.KnownNoise => "Known noise",
        _ => status.ToString()
    };

    /// <summary>"MissingDataLayerNamed" -> "Missing Data Layer Named" (enum names shown to humans).</summary>
    private static string SpaceCamelCase(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1])) sb.Append(' ');
            sb.Append(value[i]);
        }
        return sb.ToString();
    }
}

/// <summary>All records sharing one <see cref="ProblemSignature"/> (a slice of the donut).</summary>
public sealed class SignatureBucket
{
    public SignatureBucket(ProblemSignature signature, IReadOnlyList<RuleRecord> records)
    {
        Signature = signature;
        Records = records;
    }

    public ProblemSignature Signature { get; }
    public IReadOnlyList<RuleRecord> Records { get; }
    public int Count => Records.Count;
    public int Occurrences => Records.Sum(r => Math.Max(1, r.Occurrences));
    public int ReadCount => Records.Count(r => r.IsRead);
    public int UnreadCount => Count - ReadCount;
}
