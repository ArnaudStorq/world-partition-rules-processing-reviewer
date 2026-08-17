using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Logging;

public sealed class ActivityLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public ActivityLevel Level { get; init; }
    public string Category { get; init; } = "App";
    public string Message { get; init; } = string.Empty;

    /// <summary>The TeamCity build / session this action relates to, when applicable.</summary>
    public string? Build { get; init; }

    public bool HasBuild => !string.IsNullOrEmpty(Build);

    public string ToPlainLine() =>
        string.IsNullOrEmpty(Build)
            ? $"{Timestamp:HH:mm:ss.fff}  {Level,-7}  [{Category}]  {Message}"
            : $"{Timestamp:HH:mm:ss.fff}  {Level,-7}  [{Category}]  ({Build})  {Message}";
}

public interface IActivityLog
{
    event EventHandler<ActivityLogEntry>? EntryAdded;

    /// <summary>Ambient tag stamped on every new entry (e.g. the current TeamCity build/session). Null clears it.</summary>
    string? Scope { get; set; }

    void Log(ActivityLevel level, string category, string message);
    void Debug(string message, string category = "App");
    void Info(string message, string category = "App");
    void Success(string message, string category = "App");
    void Warning(string message, string category = "App");
    void Error(string message, string category = "App");
}

/// <summary>
/// Thread-safe activity sink. Raises <see cref="EntryAdded"/> for the UI to marshal to its dispatcher,
/// and optionally appends to a rolling file.
/// </summary>
public sealed class ActivityLog : IActivityLog
{
    private readonly object _gate = new();
    private readonly string? _filePath;
    private bool _verbose;

    public event EventHandler<ActivityLogEntry>? EntryAdded;

    /// <summary>Ambient tag stamped on every new entry (e.g. the current TeamCity build/session).</summary>
    public string? Scope { get; set; }

    public ActivityLog(string? filePath = null, bool verbose = false)
    {
        _filePath = filePath;
        _verbose = verbose;
        if (!string.IsNullOrWhiteSpace(_filePath))
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!); }
            catch { /* logging must never throw */ }
        }
    }

    public void SetVerbose(bool verbose) => _verbose = verbose;

    public void Log(ActivityLevel level, string category, string message)
    {
        if (level == ActivityLevel.Debug && !_verbose) return;

        var entry = new ActivityLogEntry { Level = level, Category = category, Message = message, Build = Scope };
        EntryAdded?.Invoke(this, entry);

        if (_filePath is null) return;
        lock (_gate)
        {
            try { File.AppendAllText(_filePath, entry.ToPlainLine() + Environment.NewLine); }
            catch { /* ignore file errors */ }
        }
    }

    public void Debug(string message, string category = "App") => Log(ActivityLevel.Debug, category, message);
    public void Info(string message, string category = "App") => Log(ActivityLevel.Info, category, message);
    public void Success(string message, string category = "App") => Log(ActivityLevel.Success, category, message);
    public void Warning(string message, string category = "App") => Log(ActivityLevel.Warning, category, message);
    public void Error(string message, string category = "App") => Log(ActivityLevel.Error, category, message);
}
