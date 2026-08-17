namespace WPRulesReviewer.Core.Models;

/// <summary>
/// All persisted user settings. Serialized to %AppData%/WPRulesReviewer/settings.json.
/// Grouped so the Settings view can render one card per section.
/// </summary>
public sealed class AppSettings
{
    // ---- Appearance --------------------------------------------------------
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string AccentColor { get; set; } = "#6D5AE0";
    public double UiFontScale { get; set; } = 1.0;
    public bool CompactRows { get; set; }
    public bool ShowGridLines { get; set; } = true;
    /// <summary>Number of rows shown per page in the results grid.</summary>
    public int ItemsPerPage { get; set; } = 100;

    // ---- Tray / window -----------------------------------------------------
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowTrayNotifications { get; set; } = true;
    public bool ConfirmOnExit { get; set; } = true;
    public bool RememberWindowLayout { get; set; } = true;

    // ---- Persisted window / panel layout (used when RememberWindowLayout is true) ----
    public double WindowWidth { get; set; } = 1720;
    public double WindowHeight { get; set; } = 1160;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }
    public double BuildsPanelWidth { get; set; } = 400;
    public double SmartPanelWidth { get; set; } = 560;
    public double BottomPanelHeight { get; set; } = 360;

    // ---- Paths -------------------------------------------------------------
    public string DefaultEditorIniPath { get; set; } = @"D:\Sun\Sundance\Config\DefaultEditor.ini";
    public string LogDownloadFolder { get; set; } = string.Empty;   // empty => app data /Logs
    public string ExportFolder { get; set; } = string.Empty;        // empty => app data /Reports
    public string LastLogFolder { get; set; } = string.Empty;
    /// <summary>Folder where the app stores its own data (e.g. the suspicious-report journal).</summary>
    public string AppDataFolder { get; set; } = @"D:\WorldPartitionRules\";

    // ---- TeamCity ----------------------------------------------------------
    public string TeamCityBaseUrl { get; set; } = "https://slc-teamcity.wbiegames.com";
    public string TeamCityBuildTypeId { get; set; } = "Sundance_Dev_Tools_ApplyWorldPartitionRules";
    public string TeamCityToken { get; set; } = string.Empty;       // Bearer token (preferred)
    public string TeamCityUsername { get; set; } = string.Empty;    // fallback basic auth
    public string TeamCityPassword { get; set; } = string.Empty;
    public int TeamCityMaxBuilds { get; set; } = 300;
    public int TeamCityLookbackHours { get; set; } = 2160;  // fetch window (~3 months)
    public int TeamCityRecentDays { get; set; } = 14;       // sidebar shows this window before "More..."
    public bool AutoRefreshOnStartup { get; set; } = true;  // fetch TeamCity builds when the app launches
    public bool PreselectLatestDayOnStartup { get; set; } = true; // check all builds from the most recent day
    public bool TeamCityOnlySuccessful { get; set; } = true;
    public bool TeamCityVerifySsl { get; set; } = true;
    public string TeamCityArtifactLogPath { get; set; } = "Sundance/Saved/Logs";
    public string TeamCitySessionTagRegex { get; set; } = @"\(([^)]+)\)";

    // ---- Parsing -----------------------------------------------------------
    // Off by default so counts match the web report, which lists every warning line (no collapsing).
    public bool DeduplicateWarnings { get; set; }
    public bool ResolveContextPaths { get; set; } = true;
    public bool KeepProgressLines { get; set; }
    public int MaxRecordsPerSession { get; set; }  // 0 => unlimited

    // ---- Triage ------------------------------------------------------------
    public bool TreatNavSkipAsNoise { get; set; } = true;
    public bool TreatEmptyMissingDataLayerAsNoise { get; set; } = true;
    public bool ShowExpectedInReport { get; set; }
    public List<string> NoiseActorPatterns { get; set; } = new()
    {
        "Nanite_Shadow",
        "MercunaNavOctree",
        "MercunaNavSeed",
        "Landscape_WaterInfo",
        "Landscape_GrassLayers"
    };
    public List<string> FlagUnclassifiedTokens { get; set; } = new() { "#_TO_CLASSIFY", "temp_stacks" };

    // ---- AI (Cursor SDK local) --------------------------------------------
    public bool EnableAi { get; set; }
    public string CursorAgentPath { get; set; } = "cursor-agent";
    public string AiModel { get; set; } = "auto";
    public int AiMaxAnomalies { get; set; } = 120;
    public int AiTimeoutSeconds { get; set; } = 180;
    public string AiExtraInstructions { get; set; } = string.Empty;

    // ---- Performance -------------------------------------------------------
    public int MaxParallelSessions { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
    public int UiVirtualizationBatch { get; set; } = 500;

    // ---- Activity log ------------------------------------------------------
    public bool LogToFile { get; set; } = true;
    public bool VerboseLogging { get; set; }
    public int MaxActivityEntries { get; set; } = 5000;
    /// <summary>When a row is clicked, how many original log lines to show before/after the matched line.</summary>
    public int RowContextLines { get; set; } = 3;
    /// <summary>Log the original Sundance.log line(s) to the activity log when a row is selected.</summary>
    public bool LogRowContextOnSelect { get; set; } = true;

    // ---- Export ------------------------------------------------------------
    public string CsvDelimiter { get; set; } = ",";
    public bool AutoExportAnomaliesOnAnalyze { get; set; }
}
