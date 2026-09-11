using System.Text.Json;
using System.Text.Json.Serialization;

namespace WPRulesReviewer.Core.Fixes;

/// <summary>One applied action, recorded so it can be explained later and undone when reversible.</summary>
public sealed class FixJournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.Now;
    public string BatchId { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public string SignatureId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AffectedCount { get; set; }

    /// <summary>Actor paths touched, so an undo can find the very same rows again.</summary>
    public List<string> AffectedActors { get; set; } = new();

    /// <summary>Previous value for a settings or file change (empty when the action only added something).</summary>
    public string? PreviousValue { get; set; }

    public string? NewValue { get; set; }

    /// <summary>False for actions the tool cannot roll back (a file write already reviewed and accepted).</summary>
    public bool IsReversible { get; set; } = true;
}

/// <summary>
/// Append-only journal of everything the Fix Advisor applied, stored next to the review reports.
/// It is both the audit trail and the source of truth for "Undo last fix batch".
/// </summary>
public sealed class FixJournal
{
    public const string FileName = "fix-journal.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _folder;

    public FixJournal(string folder) => _folder = string.IsNullOrWhiteSpace(folder) ? "." : folder;

    public string FilePath => Path.Combine(_folder, FileName);

    public IReadOnlyList<FixJournalEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Array.Empty<FixJournalEntry>();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<FixJournalEntry>>(json, Options) ?? new List<FixJournalEntry>();
        }
        catch
        {
            // A corrupted journal must never block the app: it is an audit trail, not state.
            return Array.Empty<FixJournalEntry>();
        }
    }

    public void Append(IEnumerable<FixJournalEntry> entries)
    {
        var all = Load().ToList();
        all.AddRange(entries);
        Save(all);
    }

    public void Remove(string batchId)
    {
        var kept = Load().Where(e => e.BatchId != batchId).ToList();
        Save(kept);
    }

    /// <summary>Entries of the most recent batch, newest first (what "Undo" would revert).</summary>
    public IReadOnlyList<FixJournalEntry> LastBatch()
    {
        var all = Load();
        if (all.Count == 0) return Array.Empty<FixJournalEntry>();

        var lastBatchId = all.OrderByDescending(e => e.AppliedAt).First().BatchId;
        return all.Where(e => e.BatchId == lastBatchId).ToList();
    }

    private void Save(List<FixJournalEntry> entries)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, Options));
    }
}
