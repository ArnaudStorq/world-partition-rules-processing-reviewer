using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Fixes;

/// <summary>Outcome of applying one recommendation.</summary>
public sealed class FixResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int AffectedCount { get; init; }
    public IReadOnlyList<FixJournalEntry> Entries { get; init; } = Array.Empty<FixJournalEntry>();
}

/// <summary>
/// Executes the typed actions of a <see cref="FixRecommendation"/>. Only reversible, in-app actions
/// are handled here: review decisions and noise patterns. Anything touching files or actor data is
/// deliberately out of scope and reported as "delegated".
/// </summary>
public sealed class FixExecutor
{
    private readonly AppSettings _settings;
    private readonly IActivityLog _log;
    private readonly FixJournal _journal;

    public FixExecutor(AppSettings settings, IActivityLog log)
    {
        _settings = settings;
        _log = log;
        _journal = new FixJournal(settings.AppDataFolder);
    }

    public FixJournal Journal => _journal;

    /// <summary>
    /// Applies one recommendation to the given rows. <paramref name="dryRun"/> computes the effect
    /// without touching anything, which is what the preview dialog shows.
    /// </summary>
    public FixResult Apply(FixRecommendation recommendation, IReadOnlyList<RuleRecord> records,
        string sessionName, string batchId, bool dryRun = false)
    {
        if (recommendation.Kind is FixKind.ActorDataChange or FixKind.Investigate)
            return new FixResult
            {
                Success = false,
                Message = recommendation.Kind == FixKind.ActorDataChange
                    ? "Actor data cannot be edited from this tool. Use \"Open in Cursor\" to delegate the fix."
                    : "This recommendation is informational only."
            };

        if (recommendation.Kind == FixKind.IniRuleChange)
            return new FixResult
            {
                Success = false,
                Message = "Editing DefaultEditor.ini requires a reviewed diff; use \"Open in Cursor\" for now."
            };

        var entries = new List<FixJournalEntry>();
        var affected = 0;

        foreach (var action in recommendation.Actions)
        {
            switch (action.Type)
            {
                case "AddNoisePattern":
                    if (string.IsNullOrWhiteSpace(action.Value)) continue;
                    if (_settings.NoiseActorPatterns.Contains(action.Value, StringComparer.OrdinalIgnoreCase)) continue;
                    if (!dryRun) _settings.NoiseActorPatterns.Add(action.Value);
                    entries.Add(Entry(recommendation, action, sessionName, batchId, records,
                        previous: null, next: action.Value));
                    affected += records.Count;
                    break;

                case "AddUnclassifiedToken":
                    if (string.IsNullOrWhiteSpace(action.Value)) continue;
                    if (_settings.FlagUnclassifiedTokens.Contains(action.Value, StringComparer.OrdinalIgnoreCase)) continue;
                    if (!dryRun) _settings.FlagUnclassifiedTokens.Add(action.Value);
                    entries.Add(Entry(recommendation, action, sessionName, batchId, records,
                        previous: null, next: action.Value));
                    affected += records.Count;
                    break;

                case "MarkRead":
                    if (!dryRun) foreach (var r in records) r.IsRead = true;
                    entries.Add(Entry(recommendation, action, sessionName, batchId, records, null, null));
                    affected += records.Count;
                    break;

                case "Approve":
                    if (!dryRun)
                        foreach (var r in records)
                        {
                            r.IsApproved = true;
                            r.ApprovalComment = action.Comment;
                            r.IsRead = true;
                        }
                    entries.Add(Entry(recommendation, action, sessionName, batchId, records, null, action.Comment));
                    affected += records.Count;
                    break;

                case "Flag":
                    if (!dryRun)
                        foreach (var r in records)
                        {
                            r.IsReported = true;
                            r.SuspiciousComment = action.Comment;
                        }
                    entries.Add(Entry(recommendation, action, sessionName, batchId, records, null, action.Comment));
                    affected += records.Count;
                    break;

                default:
                    _log.Warning($"Unknown fix action \"{action.Type}\", skipped.", "Fix");
                    break;
            }
        }

        if (entries.Count == 0)
            return new FixResult { Success = false, Message = "Nothing to apply (the actions had no effect)." };

        if (!dryRun)
        {
            _journal.Append(entries);
            _log.Success($"Applied \"{recommendation.Title}\" to {records.Count} row(s).", "Fix");
        }

        return new FixResult
        {
            Success = true,
            AffectedCount = affected,
            Entries = entries,
            Message = dryRun
                ? $"Would apply {entries.Count} action(s) to {records.Count} row(s)."
                : $"Applied {entries.Count} action(s) to {records.Count} row(s)."
        };
    }

    /// <summary>
    /// Reverts the most recent batch. Records are matched back by actor path, so an undo works even
    /// after the session was re-analyzed.
    /// </summary>
    public FixResult UndoLastBatch(IReadOnlyList<RuleRecord> sessionRecords)
    {
        var batch = _journal.LastBatch();
        if (batch.Count == 0) return new FixResult { Success = false, Message = "Nothing to undo." };

        var reverted = 0;
        foreach (var entry in batch.Where(e => e.IsReversible))
        {
            var targets = sessionRecords
                .Where(r => entry.AffectedActors.Contains(r.DisplayActor, StringComparer.OrdinalIgnoreCase))
                .ToList();

            switch (entry.ActionType)
            {
                case "AddNoisePattern":
                    if (entry.NewValue is not null) _settings.NoiseActorPatterns.Remove(entry.NewValue);
                    break;
                case "AddUnclassifiedToken":
                    if (entry.NewValue is not null) _settings.FlagUnclassifiedTokens.Remove(entry.NewValue);
                    break;
                case "MarkRead":
                    foreach (var r in targets) r.IsRead = false;
                    break;
                case "Approve":
                    foreach (var r in targets) { r.IsApproved = false; r.ApprovalComment = null; }
                    break;
                case "Flag":
                    foreach (var r in targets) { r.IsReported = false; r.SuspiciousComment = null; }
                    break;
            }
            reverted += targets.Count;
        }

        _journal.Remove(batch[0].BatchId);
        _log.Info($"Reverted the last fix batch ({batch.Count} action(s)).", "Fix");

        return new FixResult
        {
            Success = true,
            AffectedCount = reverted,
            Message = $"Reverted {batch.Count} action(s)."
        };
    }

    /// <summary>Actor paths are capped: the journal is an audit trail, not a copy of the session.</summary>
    private const int MaxJournalActors = 500;

    private static FixJournalEntry Entry(FixRecommendation recommendation, FixAction action, string sessionName,
        string batchId, IReadOnlyList<RuleRecord> records, string? previous, string? next) => new()
        {
            BatchId = batchId,
            SessionName = sessionName,
            SignatureId = recommendation.SignatureId,
            Title = recommendation.Title,
            ActionType = action.Type,
            Description = action.Describe(),
            AffectedCount = records.Count,
            AffectedActors = records.Take(MaxJournalActors).Select(r => r.DisplayActor).Distinct().ToList(),
            PreviousValue = previous,
            NewValue = next
        };
}
