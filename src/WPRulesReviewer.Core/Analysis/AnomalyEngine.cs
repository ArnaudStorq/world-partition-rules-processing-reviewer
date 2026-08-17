using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Oracle;

namespace WPRulesReviewer.Core.Analysis;

/// <summary>
/// Runs the deterministic triage: classifies every record against the oracle (in parallel).
/// </summary>
public sealed class AnomalyEngine
{
    private readonly AppSettings _settings;

    public AnomalyEngine(AppSettings settings) => _settings = settings;

    public void Classify(SessionReport report, OracleConfig oracle, CancellationToken ct = default)
    {
        var evaluator = new OracleEvaluator(oracle, _settings);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _settings.MaxParallelSessions),
            CancellationToken = ct
        };
        Parallel.ForEach(report.Records, options, evaluator.Classify);
    }
}
