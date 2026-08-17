using System.Text.Json;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Reporting;

/// <summary>
/// Appends and reads suspicious-actor reports from a single JSON journal file kept in the
/// configured app data folder. Thread-safe for the small write volume the UI produces.
/// </summary>
public sealed class SuspiciousReportStore
{
    public const string SuspiciousFileName = "SuspiciousReports.json";
    public const string ApprovedFileName = "ApprovedReports.json";

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _fileName;

    public string Folder { get; }
    public string FilePath => Path.Combine(Folder, _fileName);

    public SuspiciousReportStore(string? folder, string fileName = SuspiciousFileName)
    {
        Folder = string.IsNullOrWhiteSpace(folder) ? @"D:\WorldPartitionRules\" : folder.Trim();
        _fileName = string.IsNullOrWhiteSpace(fileName) ? SuspiciousFileName : fileName;
    }

    public IReadOnlyList<SuspiciousReport> Load()
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath)) return Array.Empty<SuspiciousReport>();
            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<SuspiciousReport>>(json) ?? new List<SuspiciousReport>();
            }
            catch
            {
                return Array.Empty<SuspiciousReport>();
            }
        }
    }

    /// <summary>Append a report to the journal, creating the folder/file if needed.</summary>
    public void Add(SuspiciousReport report)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Folder);
            var list = Load().ToList();
            list.Add(report);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Options));
        }
    }

    /// <summary>Overwrite the whole journal with the given set of reports.</summary>
    public void Save(IEnumerable<SuspiciousReport> reports)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(reports.ToList(), Options));
        }
    }
}
