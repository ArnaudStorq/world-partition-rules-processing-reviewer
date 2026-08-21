using System.Text;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Ai;

public sealed class AiReportResult
{
    public bool Success { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Error { get; init; }
}

public interface IAiAgentService
{
    bool IsAvailable { get; }
    Task<AiReportResult> GenerateReportAsync(SessionReport report, CancellationToken ct = default);

    /// <summary>
    /// Uses the agent to sanity-check the applied assignments (DataLayer / RuntimeGrid / HLODLayer /
    /// IncludeInHLOD) and flag anything that looks inconsistent with the actor's Outliner path or naming.
    /// </summary>
    Task<AiReportResult> GenerateAssignmentAnalysisAsync(SessionReport report, CancellationToken ct = default);

    /// <summary>Run an arbitrary prompt through the agent and return the raw text output.</summary>
    Task<AiReportResult> CompleteAsync(string prompt, CancellationToken ct = default);

    /// <summary>
    /// Run an arbitrary prompt and stream the agent's output as it arrives (one report call per line)
    /// so the UI can show the reasoning live. The final <see cref="AiReportResult"/> still carries the
    /// full accumulated text.
    /// </summary>
    Task<AiReportResult> CompleteStreamingAsync(string prompt, IProgress<string>? onOutput, CancellationToken ct = default);
}

/// <summary>Builds the deterministic prompt sent to the local Cursor agent.</summary>
public static class AiPromptBuilder
{
    public static string Build(SessionReport report, AppSettings settings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are reviewing the output of the Sundance WorldPartitionRuleBuilder.");
        sb.AppendLine("Below are the automatically-triaged anomalies and items needing review for one session.");
        sb.AppendLine("Write a concise report for a technical artist: group related items, explain the likely");
        sb.AppendLine("root cause for each cluster, and suggest a concrete fix. Only cover litigious points.");
        sb.AppendLine("Do not restate items that are already Expected. Be specific about actor paths.");
        sb.AppendLine("Always write the entire report in English, regardless of any other language used.");
        if (!string.IsNullOrWhiteSpace(settings.AiExtraInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("Additional instructions:");
            sb.AppendLine(settings.AiExtraInstructions);
        }
        sb.AppendLine();
        sb.Append("Session: ").AppendLine(report.SessionName);
        if (report.World is not null) sb.Append("World: ").AppendLine(report.World);
        sb.AppendLine();

        var items = report.Records
            .Where(r => r.Status is ReviewStatus.Anomaly or ReviewStatus.NeedsReview)
            .OrderByDescending(r => r.Severity)
            .Take(Math.Max(1, settings.AiMaxAnomalies));

        sb.AppendLine("Items:");
        foreach (var r in items)
        {
            sb.Append("- [").Append(r.Severity).Append("] ")
              .Append(r.CategoryLabel).Append(" = '").Append(r.Value).Append("' | ")
              .Append(r.DisplayActor)
              .Append(" | ").Append(r.StatusReason ?? r.Reason ?? string.Empty);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds the prompt asking the agent to review the *applied* assignments per actor and flag
    /// anything suspicious (path/assignment mismatches, naming conventions, HLOD inconsistencies).
    /// </summary>
    public static string BuildAssignmentAnalysis(SessionReport report, AppSettings settings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are reviewing the World Partition rule assignments produced by the Sundance WorldPartitionRuleBuilder.");
        sb.AppendLine("For each actor below you are given the assignments the rules applied: DataLayer, RuntimeGrid, HLODLayer and IncludeInHLOD.");
        sb.AppendLine("Judge whether each actor looks correctly assigned, using the actor's Outliner path and name as the main hint.");
        sb.AppendLine("Flag anything suspicious, for example (non-exhaustive):");
        sb.AppendLine("- An actor whose Outliner path or name contains an interior marker (e.g. '_INT') but has IncludeInHLOD = true.");
        sb.AppendLine("- An actor that looks exterior/landscape but is excluded from HLOD (IncludeInHLOD = false) or has no HLODLayer.");
        sb.AppendLine("- A DataLayer, RuntimeGrid or HLODLayer that does not match the actor's location/type implied by its path.");
        sb.AppendLine("- Inconsistent assignments between actors that clearly belong to the same group/room.");
        sb.AppendLine("- Naming that breaks the project convention (e.g. spaces or unexpected characters in the Outliner path).");
        sb.AppendLine("Only report the litigious/suspicious actors; do not list the ones that look fine.");
        sb.AppendLine("Group related findings, explain the likely reason, and suggest a concrete fix (which assignment to change and why).");
        sb.AppendLine("Be specific about actor paths. Always write the entire report in English, regardless of any other language used.");
        if (!string.IsNullOrWhiteSpace(settings.AiExtraInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("Additional instructions:");
            sb.AppendLine(settings.AiExtraInstructions);
        }
        sb.AppendLine();
        sb.Append("Session: ").AppendLine(report.SessionName);
        if (report.World is not null) sb.Append("World: ").AppendLine(report.World);
        sb.AppendLine();

        // Group the applied assignments per actor so the agent sees the full picture for each one.
        var applied = report.Records.Where(r => r.Category == RecordCategory.Applied);
        var byActor = applied
            .GroupBy(r => r.DisplayActor)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, settings.AiMaxAnomalies));

        sb.AppendLine("Actors and their applied assignments:");
        foreach (var g in byActor)
        {
            string? Value(AssignmentType t) => g.FirstOrDefault(r => r.AssignmentType == t)?.Value;
            var dataLayer = Value(AssignmentType.DataLayer);
            var grid = Value(AssignmentType.RuntimeGrid);
            var hlodLayer = Value(AssignmentType.HLODLayer);
            var includeInHlod = Value(AssignmentType.IncludeInHLOD);

            sb.Append("- ").Append(g.Key)
              .Append(" | DataLayer=").Append(dataLayer ?? "-")
              .Append(" | RuntimeGrid=").Append(grid ?? "-")
              .Append(" | HLODLayer=").Append(hlodLayer ?? "-")
              .Append(" | IncludeInHLOD=").Append(includeInHlod ?? "-");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
