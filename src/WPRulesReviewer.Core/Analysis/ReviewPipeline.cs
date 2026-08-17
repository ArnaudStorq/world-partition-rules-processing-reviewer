using System.Diagnostics;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Oracle;
using WPRulesReviewer.Core.Parsing;

namespace WPRulesReviewer.Core.Analysis;

public enum StepStatus { Pending, Running, Done, Failed, Skipped }

public sealed class PipelineStep
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? Detail { get; set; }
    public TimeSpan Elapsed { get; set; }
}

/// <summary>
/// Orchestrates the full review of one log file and reports each step so the UI can show a
/// transparent, detailed processing pipeline. Every step is also written to the activity log.
/// </summary>
public sealed class ReviewPipeline
{
    private readonly AppSettings _settings;
    private readonly IActivityLog _log;

    public ReviewPipeline(AppSettings settings, IActivityLog log)
    {
        _settings = settings;
        _log = log;
    }

    public static IReadOnlyList<PipelineStep> CreateSteps()
    {
        var defs = new (string Key, string Name, string Description)[]
        {
            ("oracle", "Load oracle", "Read the WorldPartition rule registry, ignore/force/clear lists and DataLayer conventions from DefaultEditor.ini."),
            ("parse", "Parse log", "Stream the Sundance.log and extract every Applied assignment, Warning, Error and Skipped actor (with de-duplication and context resolution)."),
            ("triage", "Triage vs oracle", "Classify each record as Expected, Known noise, Needs review or Anomaly by comparing it to the oracle (multi-threaded)."),
            ("done", "Summarize", "Compute counts and build the copy-paste ready report.")
        };

        var total = defs.Length;
        return defs.Select((d, i) => new PipelineStep
        {
            Key = d.Key,
            Title = $"Step {i + 1}/{total} — {d.Name}",
            Description = d.Description
        }).ToList();
    }

    public OracleConfig LoadOracle()
    {
        var loader = new OracleConfigLoader();
        var oracle = loader.Load(_settings.DefaultEditorIniPath);
        if (oracle.IsLoaded)
            _log.Success($"Oracle loaded: {oracle.DataLayerRules.Count} DataLayer, {oracle.HLODLayerRules.Count} HLOD, {oracle.RuntimeGridRules.Count} RuntimeGrid rules.", "Oracle");
        else
            _log.Warning($"Oracle not loaded from '{_settings.DefaultEditorIniPath}'. Triage will be limited.", "Oracle");
        return oracle;
    }

    public async Task<SessionReport> RunAsync(
        string logPath,
        string sessionName,
        OracleConfig oracle,
        IProgress<PipelineStep>? progress = null,
        CancellationToken ct = default)
    {
        var steps = CreateSteps().ToDictionary(s => s.Key);

        void Begin(string key)
        {
            var s = steps[key];
            s.Status = StepStatus.Running;
            _log.Info($"{s.Title} — {s.Description}", "Pipeline");
            progress?.Report(s);
        }
        void End(string key, string detail, Stopwatch sw, StepStatus status = StepStatus.Done)
        {
            var s = steps[key];
            s.Status = status;
            s.Detail = detail;
            s.Elapsed = sw.Elapsed;
            _log.Success($"{s.Title} done in {sw.ElapsedMilliseconds} ms — {detail}", "Pipeline");
            progress?.Report(s);
        }

        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();

            // Step 1 — oracle (already loaded, reported for transparency)
            Begin("oracle");
            End("oracle", oracle.IsLoaded ? $"{oracle.DataLayerRules.Count + oracle.HLODLayerRules.Count + oracle.RuntimeGridRules.Count} rules" : "not loaded", sw,
                oracle.IsLoaded ? StepStatus.Done : StepStatus.Skipped);

            // Step 2 — parse
            sw.Restart();
            Begin("parse");
            var parser = new LogParser();
            var parseOptions = new LogParseOptions
            {
                DeduplicateWarnings = _settings.DeduplicateWarnings,
                ResolveContextPaths = _settings.ResolveContextPaths,
                KeepProgressLines = _settings.KeepProgressLines,
                MaxRecords = _settings.MaxRecordsPerSession
            };
            var report = parser.Parse(logPath, parseOptions, sessionName, ct);
            End("parse", $"{report.TotalCount} records from {report.ParsedLineCount} lines", sw);

            // Step 3 — triage
            sw.Restart();
            Begin("triage");
            new AnomalyEngine(_settings).Classify(report, oracle, ct);
            End("triage", $"{report.AnomalyCount} anomalies, {report.NeedsReviewCount} to review, {report.ExpectedCount} expected", sw);

            // Step 4 — summarize
            sw.Restart();
            Begin("done");
            report.AnalyzedAt = DateTimeOffset.Now;
            End("done", $"Applied {report.AppliedCount} · Warn {report.WarningCount} · Err {report.ErrorCount} · Skip {report.SkippedCount}", sw);

            return report;
        }, ct).ConfigureAwait(false);
    }
}
