namespace WPRulesReviewer.Core.Models;

/// <summary>A build returned by the TeamCity REST API for the Apply WorldPartition Rules config.</summary>
public sealed class TeamCityBuild
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string BuildTypeId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;   // SUCCESS / FAILURE / ...
    public string State { get; set; } = string.Empty;     // finished / running / ...
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? FinishDate { get; set; }
    public string WebUrl { get; set; } = string.Empty;
    public List<string> Tags { get; } = new();

    /// <summary>Human friendly session name (e.g. Dungeons, Missions, LI_Hogsmeade).</summary>
    public string SessionName { get; set; } = string.Empty;

    public bool IsSuccess => string.Equals(Status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

    public string DisplayLine =>
        $"#{Number}  {SessionName}".TrimEnd() +
        (StartDate is { } d ? $"  ({d.LocalDateTime:ddd dd MMM HH:mm})" : string.Empty);
}
