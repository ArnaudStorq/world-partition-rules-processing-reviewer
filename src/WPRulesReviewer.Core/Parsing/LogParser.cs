using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Parsing;

public sealed class LogParseOptions
{
    public bool DeduplicateWarnings { get; set; } = true;
    public bool ResolveContextPaths { get; set; } = true;
    public bool KeepProgressLines { get; set; }
    public int MaxRecords { get; set; }  // 0 => unlimited
}

/// <summary>
/// Streaming parser for a WorldPartitionRuleBuilder Sundance.log.
/// Recognizes applied assignments, warnings, errors and skipped actors, and resolves the
/// short actor names emitted by warnings back to their full Outliner path using the preceding
/// "Applying rules on actor:" progress line.
/// </summary>
public sealed partial class LogParser
{
    // Applied assignments (LogWorldPartitionRules: Display:)
    [GeneratedRegex(@"Applied DataLayer '([^']*)' to actor '([^']+)'\.")]
    private static partial Regex AppliedDataLayer();
    [GeneratedRegex(@"Applied RuntimeGrid '([^']*)' to actor '([^']+)'\.")]
    private static partial Regex AppliedRuntimeGrid();
    [GeneratedRegex(@"Applied HLODLayer '([^']*)' to actor '([^']+)'\.")]
    private static partial Regex AppliedHlodLayer();
    [GeneratedRegex(@"Applied IncludeInHLOD = (true|false) to actor '([^']+)'\.(?<forced> This is a forced setting\.)?")]
    private static partial Regex AppliedIncludeInHlod();

    // Warnings
    [GeneratedRegex(@"Missing DataLayer\s+(?:(\S+)\s+)?for actor '([^']+)'\.")]
    private static partial Regex MissingDataLayer();
    [GeneratedRegex(@"Actor '([^']+)' matches multiple (HLODLayer|RuntimeGrid|DataLayer) rules \((\d+)\): \[([^\]]*)\]")]
    private static partial Regex MultipleRules();
    [GeneratedRegex(@"Actor '([^']+)' is assigned to runtime DataLayer '([^']*)' but no DataLayer rule targets it")]
    private static partial Regex UntargetedRuntimeDataLayer();

    /// <summary>Last-resort actor extraction for warning shapes we do not model explicitly yet.</summary>
    [GeneratedRegex(@"\b[Aa]ctor '([^']+)'")]
    private static partial Regex AnyActor();

    // Skipped / progress
    [GeneratedRegex(@"Skipping rule application for actor \[([^\]]+)\]: (.+?)\s*$")]
    private static partial Regex SkippingActor();
    [GeneratedRegex(@"Applying rules on actor:\s*(.+?)\s*$")]
    private static partial Regex ApplyingActor();

    // Line timestamp: [2026.08.08-06.39.03:783]
    [GeneratedRegex(@"^\[(\d{4})\.(\d{2})\.(\d{2})-(\d{2})\.(\d{2})\.(\d{2}):(\d{3})\]")]
    private static partial Regex LineStamp();

    public SessionReport Parse(string path, LogParseOptions options, string? sessionName = null,
        CancellationToken ct = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var report = ParseStream(stream, options, ct);
        report.SourcePath = path;
        report.SessionName = sessionName ?? Path.GetFileNameWithoutExtension(path);
        return report;
    }

    public SessionReport ParseStream(Stream stream, LogParseOptions options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new SessionReport();

        // Dedup map for warnings/skips keyed by identity.
        var dedup = new Dictionary<string, RuleRecord>(StringComparer.Ordinal);
        string? lastActorPath = null;
        long lineNo = 0;

        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            lineNo++;

            if (report.World is null && line.Contains("PreRun for world:", StringComparison.Ordinal))
            {
                var idx = line.IndexOf("PreRun for world:", StringComparison.Ordinal);
                report.World = line[(idx + "PreRun for world:".Length)..].Trim();
            }

            if (lineNo == 1 && line.StartsWith("Log file open", StringComparison.OrdinalIgnoreCase))
                report.LogTime = TryParseHeaderTime(line);

            // Fast reject: only a few substrings matter.
            var isApplied = line.Contains("Applied ", StringComparison.Ordinal);
            var isWarning = line.Contains(": Warning:", StringComparison.Ordinal);
            var isError = line.Contains(": Error:", StringComparison.Ordinal);
            var isSkip = line.Contains("Skipping rule application", StringComparison.Ordinal);
            var isProgress = line.Contains("Applying rules on actor:", StringComparison.Ordinal);

            if (isProgress)
            {
                var m = ApplyingActor().Match(line);
                if (m.Success)
                {
                    lastActorPath = m.Groups[1].Value.Trim();
                    if (options.KeepProgressLines)
                        Add(report, dedup, options, MakeProgress(lineNo, line, lastActorPath));
                }
                continue;
            }

            if (isSkip)
            {
                var m = SkippingActor().Match(line);
                if (m.Success)
                {
                    var full = m.Groups[1].Value.Trim();
                    Add(report, dedup, options, new RuleRecord
                    {
                        LineNumber = (int)lineNo,
                        Timestamp = TryParseLineTime(line),
                        Category = RecordCategory.Skipped,
                        AssignmentType = AssignmentType.None,
                        ActorPath = full,
                        ActorName = LeafOf(full),
                        Reason = m.Groups[2].Value.Trim(),
                        RawLine = line
                    });
                }
                continue;
            }

            if (isApplied)
            {
                var rec = ParseApplied(line, lineNo);
                if (rec is not null) { Add(report, dedup, options, rec); continue; }
            }

            if (isWarning)
            {
                var rec = ParseWarning(line, lineNo, options.ResolveContextPaths ? lastActorPath : null);
                if (rec is not null) { Add(report, dedup, options, rec); continue; }
            }

            if (isError)
            {
                Add(report, dedup, options, ParseError(line, lineNo));
            }

            if (options.MaxRecords > 0 && report.Records.Count >= options.MaxRecords) break;
        }

        report.ParsedLineCount = lineNo;
        report.ParseDuration = sw.Elapsed;
        return report;
    }

    // LoadErrors: "<asset path> : Failed import for <ObjectType> <object path>"
    [GeneratedRegex(@"^(.*?)\s*:\s*Failed import for\s+(\S+)\s+(.+)$")]
    private static partial Regex FailedImport();

    private static RuleRecord ParseError(string line, long lineNo)
    {
        var msg = StripPrefix(line);
        var value = "Error";
        var actorPath = string.Empty;
        var actorName = string.Empty;

        // Only WorldPartition rule errors belong in the Warnings/Errors tally (mirrors the web report).
        // Everything else (LoadErrors "Failed import", other subsystems) is a blocker in its own bucket.
        var isRuleError = line.Contains("LogWorldPartitionRules: Error:", StringComparison.Ordinal)
                          || line.Contains("LogWorldPartitionRuleBuilder: Error:", StringComparison.Ordinal);

        var m = FailedImport().Match(msg);
        if (m.Success)
        {
            var external = m.Groups[1].Value.Trim();
            var objType = m.Groups[2].Value.Trim();
            var objPath = m.Groups[3].Value.Trim();
            value = $"Failed import ({objType})";
            actorPath = string.IsNullOrEmpty(objPath) ? external : objPath;
            actorName = LeafOf(actorPath);
        }

        return new RuleRecord
        {
            LineNumber = (int)lineNo,
            Timestamp = TryParseLineTime(line),
            Category = isRuleError ? RecordCategory.Error : RecordCategory.ImportError,
            AssignmentType = AssignmentType.None,
            Value = value,
            ActorPath = actorPath,
            ActorName = actorName,
            Reason = msg,
            RawLine = line
        };
    }

    private static RuleRecord? ParseApplied(string line, long lineNo)
    {
        Match m;
        if ((m = AppliedDataLayer().Match(line)).Success)
            return MakeApplied(lineNo, line, AssignmentType.DataLayer, m.Groups[1].Value, m.Groups[2].Value, false);
        if ((m = AppliedRuntimeGrid().Match(line)).Success)
            return MakeApplied(lineNo, line, AssignmentType.RuntimeGrid, m.Groups[1].Value, m.Groups[2].Value, false);
        if ((m = AppliedHlodLayer().Match(line)).Success)
            return MakeApplied(lineNo, line, AssignmentType.HLODLayer, m.Groups[1].Value, m.Groups[2].Value, false);
        if ((m = AppliedIncludeInHlod().Match(line)).Success)
            return MakeApplied(lineNo, line, AssignmentType.IncludeInHLOD, m.Groups[1].Value, m.Groups[2].Value,
                m.Groups["forced"].Success);
        return null;
    }

    private static RuleRecord ParseWarningMultiple(Match m, string line, long lineNo)
    {
        var kind = m.Groups[2].Value switch
        {
            "HLODLayer" => WarningKind.MultipleHLODLayerRules,
            "RuntimeGrid" => WarningKind.MultipleRuntimeGridRules,
            "DataLayer" => WarningKind.MultipleDataLayerRules,
            _ => WarningKind.Other
        };
        var rules = m.Groups[4].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new RuleRecord
        {
            LineNumber = (int)lineNo,
            Timestamp = TryParseLineTime(line),
            Category = RecordCategory.Warning,
            AssignmentType = AssignmentType.None,
            WarningKind = kind,
            ActorName = m.Groups[1].Value,
            MatchedRules = rules,
            Reason = $"Matches {m.Groups[3].Value} {m.Groups[2].Value} rules: {string.Join(", ", rules)}",
            RawLine = line
        };
    }

    private static RuleRecord? ParseWarning(string line, long lineNo, string? contextPath)
    {
        Match m;
        if ((m = MultipleRules().Match(line)).Success)
        {
            var rec = ParseWarningMultiple(m, line, lineNo);
            AttachContext(rec, contextPath);
            return rec;
        }
        if ((m = MissingDataLayer().Match(line)).Success)
        {
            var layer = m.Groups[1].Success ? m.Groups[1].Value.Trim() : string.Empty;
            var actor = m.Groups[2].Value;
            var rec = new RuleRecord
            {
                LineNumber = (int)lineNo,
                Timestamp = TryParseLineTime(line),
                Category = RecordCategory.Warning,
                AssignmentType = AssignmentType.DataLayer,
                WarningKind = string.IsNullOrEmpty(layer) ? WarningKind.MissingDataLayerEmpty : WarningKind.MissingDataLayerNamed,
                ActorName = actor,
                Value = layer,
                Reason = string.IsNullOrEmpty(layer)
                    ? "Actor matched no DataLayer rule (no layer assigned)"
                    : $"Expected DataLayer '{layer}' is missing"
            };
            AttachContext(rec, contextPath);
            return rec;
        }
        if ((m = UntargetedRuntimeDataLayer().Match(line)).Success)
        {
            var rec = new RuleRecord
            {
                LineNumber = (int)lineNo,
                Timestamp = TryParseLineTime(line),
                Category = RecordCategory.Warning,
                AssignmentType = AssignmentType.DataLayer,
                WarningKind = WarningKind.UntargetedRuntimeDataLayer,
                ActorPath = m.Groups[1].Value,
                ActorName = LeafOf(m.Groups[1].Value),
                Value = m.Groups[2].Value,
                Reason = StripPrefix(line),
                RawLine = line
            };
            AttachContext(rec, contextPath);
            return rec;
        }

        // Generic rule warning we still want to surface.
        if (line.Contains("LogWorldPartitionRules: Warning:", StringComparison.Ordinal))
        {
            // Even when the shape is unknown, the actor is what makes the row actionable, so pull the
            // first quoted actor out of the message rather than showing an anonymous warning.
            var actor = AnyActor().Match(line);
            var path = actor.Success ? actor.Groups[1].Value : string.Empty;

            var rec = new RuleRecord
            {
                LineNumber = (int)lineNo,
                Timestamp = TryParseLineTime(line),
                Category = RecordCategory.Warning,
                WarningKind = WarningKind.Other,
                ActorPath = path,
                ActorName = path.Length == 0 ? string.Empty : LeafOf(path),
                Reason = StripPrefix(line),
                RawLine = line
            };
            AttachContext(rec, contextPath);
            return rec;
        }
        return null;
    }

    private static void AttachContext(RuleRecord rec, string? contextPath)
    {
        if (string.IsNullOrEmpty(rec.ActorPath) && !string.IsNullOrEmpty(contextPath) &&
            (LeafOf(contextPath).Equals(rec.ActorName, StringComparison.Ordinal) || string.IsNullOrEmpty(rec.ActorName)))
        {
            rec.ActorPath = contextPath;
        }
    }

    private static RuleRecord MakeApplied(long lineNo, string line, AssignmentType type, string value, string actor, bool forced)
        => new()
        {
            LineNumber = (int)lineNo,
            Timestamp = TryParseLineTime(line),
            Category = RecordCategory.Applied,
            AssignmentType = type,
            ActorPath = actor,
            ActorName = LeafOf(actor),
            Value = value,
            Forced = forced,
            RawLine = line
        };

    private static RuleRecord MakeProgress(long lineNo, string line, string actor)
        => new()
        {
            LineNumber = (int)lineNo,
            Category = RecordCategory.Applied,
            AssignmentType = AssignmentType.None,
            ActorPath = actor,
            ActorName = LeafOf(actor),
            Value = "(processed)",
            RawLine = line
        };

    private static void Add(SessionReport report, Dictionary<string, RuleRecord> dedup, LogParseOptions options, RuleRecord rec)
    {
        var dedupable = options.DeduplicateWarnings &&
                        rec.Category is RecordCategory.Warning or RecordCategory.Skipped;
        if (dedupable)
        {
            if (dedup.TryGetValue(rec.Key, out var existing))
            {
                existing.Occurrences++;
                return;
            }
            dedup[rec.Key] = rec;
        }
        report.Records.Add(rec);
    }

    private static string LeafOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var idx = path.LastIndexOf('/');
        return idx >= 0 && idx < path.Length - 1 ? path[(idx + 1)..] : path;
    }

    private static string StripPrefix(string line)
    {
        var idx = line.IndexOf("]:", StringComparison.Ordinal);
        // Prefer text after the "Category: Level:" marker.
        var warn = line.IndexOf(": Warning:", StringComparison.Ordinal);
        var err = line.IndexOf(": Error:", StringComparison.Ordinal);
        var marker = warn >= 0 ? warn + ": Warning:".Length : err >= 0 ? err + ": Error:".Length : idx;
        return marker > 0 && marker < line.Length ? line[marker..].Trim() : line.Trim();
    }

    private static DateTimeOffset? TryParseLineTime(string line)
    {
        var m = LineStamp().Match(line);
        if (!m.Success) return null;
        try
        {
            return new DateTimeOffset(
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value),
                int.Parse(m.Groups[7].Value), TimeSpan.Zero);
        }
        catch { return null; }
    }

    private static DateTimeOffset? TryParseHeaderTime(string line)
    {
        // "Log file open, 08/08/26 00:34:45"
        var idx = line.IndexOf(',', StringComparison.Ordinal);
        if (idx < 0) return null;
        var s = line[(idx + 1)..].Trim();
        if (DateTimeOffset.TryParseExact(s, "MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dto))
            return dto;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var any) ? any : null;
    }
}
