using System.Text;
using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Parsing;
using Xunit;

namespace WPRulesReviewer.Core.Tests;

public class LogParserTests
{
    private const string Sample = """
Log file open, 08/08/26 06:39:00
[2026.08.08-06.39.01:100]LogWorldPartitionRules: Display: PreRun for world: /Game/Maps/LV_Overland
[2026.08.08-06.39.02:100]LogWorldPartitionRules: Display: Applying rules on actor: LV_Overland/Region/Hogwarts Valley/SM_Wall
[2026.08.08-06.39.02:200]LogWorldPartitionRules: Display: Applied DataLayer 'DL_RENDER' to actor 'LV_Overland/Region/Hogwarts Valley/SM_Wall'.
[2026.08.08-06.39.02:300]LogWorldPartitionRules: Display: Applied RuntimeGrid 'MainGrid' to actor 'LV_Overland/Region/Hogwarts Valley/SM_Wall'.
[2026.08.08-06.39.02:400]LogWorldPartitionRules: Display: Applied HLODLayer 'HLODLayer_Large' to actor 'LV_Overland/Region/Hogwarts Valley/SM_Wall'.
[2026.08.08-06.39.02:500]LogWorldPartitionRules: Display: Applied IncludeInHLOD = false to actor 'LV_Overland/Region/Hogwarts Valley/SM_Wall'. This is a forced setting.
[2026.08.08-06.39.03:100]LogWorldPartitionRules: Warning: Missing DataLayer for actor 'SM_Orphan'.
[2026.08.08-06.39.03:200]LogWorldPartitionRules: Error: Failed to process actor 'SM_Broken'.
[2026.08.08-06.39.03:300]LogWorldPartitionRules: Display: Skipping rule application for actor [LV_Overland/Region/Hogwarts Valley/SM_Locked]: package cannot be checked out.
""";

    private static SessionReport ParseSample(LogParseOptions? options = null)
    {
        var parser = new LogParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Sample));
        return parser.ParseStream(stream, options ?? new LogParseOptions());
    }

    [Fact]
    public void Parses_all_categories()
    {
        var report = ParseSample();
        Assert.Equal(4, report.AppliedCount);
        Assert.Equal(1, report.WarningCount);
        Assert.Equal(1, report.ErrorCount);
        Assert.Equal(1, report.SkippedCount);
    }

    [Fact]
    public void Extracts_world()
    {
        var report = ParseSample();
        Assert.Contains("LV_Overland", report.World);
    }

    [Fact]
    public void Applied_datalayer_has_value_and_full_path()
    {
        var report = ParseSample();
        var dl = report.Records.Single(r => r is { Category: RecordCategory.Applied, AssignmentType: AssignmentType.DataLayer });
        Assert.Equal("DL_RENDER", dl.Value);
        Assert.Equal("LV_Overland/Region/Hogwarts Valley/SM_Wall", dl.ActorPath);
        Assert.Equal("SM_Wall", dl.ActorName);
    }

    [Fact]
    public void IncludeInHlod_forced_flag_is_detected()
    {
        var report = ParseSample();
        var inc = report.Records.Single(r => r.AssignmentType == AssignmentType.IncludeInHLOD);
        Assert.True(inc.Forced);
        Assert.Equal("false", inc.Value);
    }

    [Fact]
    public void Missing_datalayer_without_name_is_empty_kind()
    {
        var report = ParseSample();
        var warn = report.Records.Single(r => r.Category == RecordCategory.Warning);
        Assert.Equal(WarningKind.MissingDataLayerEmpty, warn.WarningKind);
        Assert.Equal("SM_Orphan", warn.ActorName);
    }

    [Fact]
    public void Skipped_actor_keeps_reason()
    {
        var report = ParseSample();
        var skip = report.Records.Single(r => r.Category == RecordCategory.Skipped);
        Assert.Contains("checked out", skip.Reason);
        Assert.Equal("LV_Overland/Region/Hogwarts Valley/SM_Locked", skip.ActorPath);
    }

    [Fact]
    public void Deduplicates_identical_warnings()
    {
        var doubled = Sample + "\n" +
            "[2026.08.08-06.39.03:100]LogWorldPartitionRules: Warning: Missing DataLayer for actor 'SM_Orphan'.";
        var parser = new LogParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(doubled));
        var report = parser.ParseStream(stream, new LogParseOptions { DeduplicateWarnings = true });
        var warn = report.Records.Single(r => r.Category == RecordCategory.Warning);
        Assert.Equal(2, warn.Occurrences);
    }
}
