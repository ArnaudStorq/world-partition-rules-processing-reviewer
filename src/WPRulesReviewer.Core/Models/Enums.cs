namespace WPRulesReviewer.Core.Models;

/// <summary>High level bucket a parsed log line falls into (mirrors the web report tabs).</summary>
public enum RecordCategory
{
    Applied,
    Warning,
    Error,
    Skipped,

    /// <summary>
    /// Asset load/import failure (e.g. LoadErrors "Failed import"). Surfaced as a blocker but kept out
    /// of the Warnings/Errors tally, which mirrors the web report and counts only WorldPartition rule output.
    /// </summary>
    ImportError
}

/// <summary>The World Partition property that a rule applied to an actor.</summary>
public enum AssignmentType
{
    None,
    DataLayer,
    RuntimeGrid,
    HLODLayer,
    IncludeInHLOD
}

/// <summary>
/// Unreal log verbosity the line was written with. This is the taxonomy an engine programmer reads
/// the log with, so it is the default top level of the Warning Explorer.
/// </summary>
public enum LogVerbosity
{
    Error,
    Warning,

    /// <summary>Display and Log lines: the normal, expected output of the rule pass.</summary>
    Log,

    /// <summary>Verbose and VeryVerbose lines: per-actor tracing.</summary>
    Verbose
}

/// <summary>Sub type of a warning line, used to drive triage.</summary>
public enum WarningKind
{
    None,
    MissingDataLayerEmpty,
    MissingDataLayerNamed,
    MultipleHLODLayerRules,
    MultipleRuntimeGridRules,
    MultipleDataLayerRules,
    /// <summary>Actor carries a runtime DataLayer that no rule asked for: an extra streaming cell.</summary>
    UntargetedRuntimeDataLayer,
    Other
}

/// <summary>Result of the triage engine for a single record.</summary>
public enum ReviewStatus
{
    /// <summary>Matches the oracle: the assignment is what the rules are expected to produce.</summary>
    Expected,

    /// <summary>Recurring, harmless output that can be suppressed (locked nav actors, foliage shadows, ...).</summary>
    KnownNoise,

    /// <summary>No strong signal either way: a human should confirm it.</summary>
    NeedsReview,

    /// <summary>Contradicts the oracle or a rule invariant: this is a litigious point.</summary>
    Anomaly
}

public enum AnomalySeverity
{
    None,
    Low,
    Medium,
    High
}

public enum ActivityLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error
}

public enum AppTheme
{
    Light,
    Dark,
    System
}
