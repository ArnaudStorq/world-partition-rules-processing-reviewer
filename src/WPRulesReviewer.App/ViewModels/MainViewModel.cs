using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WPRulesReviewer.App.Services;
using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Oracle;
using WPRulesReviewer.Core.Persistence;
using WPRulesReviewer.Core.TeamCity;

namespace WPRulesReviewer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly IActivityLog _log;
    private readonly ThemeManager _theme;
    private readonly ReviewPipeline _pipeline;
    private readonly CursorAgentService _ai;

    private OracleConfig? _oracle;
    private bool _initialSelectionDone;

    public AppSettings Settings { get; }
    public ActivityLogViewModel ActivityLog { get; }
    public SettingsViewModel SettingsVm { get; }
    public RecordInspectorViewModel Inspector { get; } = new();
    public ReportsViewModel ApprovedReports { get; }
    public ReportsViewModel SuspiciousReports { get; }

    private readonly List<BuildItemViewModel> _allBuilds = new();

    public ObservableCollection<BuildItemViewModel> Builds { get; } = new();
    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    [ObservableProperty] private SessionViewModel? _selectedSession;
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Ready.";

    /// <summary>Shows a full-view spinner overlay from "Analyze selected" until the first tab is ready.</summary>
    [ObservableProperty] private bool _isAnalyzing;

    /// <summary>Show/hide the per-session Smart Analysis side panel across all open tabs.</summary>
    [ObservableProperty] private bool _showSmartAnalysis = true;

    /// <summary>Show/hide the bottom Activity Log / inspector panel.</summary>
    [ObservableProperty] private bool _showActivityLog = true;

    public string SmartAnalysisToggleLabel => ShowSmartAnalysis ? "Hide Smart Analysis" : "Show Smart Analysis";
    /// <summary>Chevron on the Smart Analysis handle: right to hide (collapse right), left to show.</summary>
    public string SmartAnalysisToggleGlyph => ShowSmartAnalysis ? "\uE76C" : "\uE76B";
    public string ActivityLogToggleLabel => ShowActivityLog ? "Hide Activity Log" : "Show Activity Log";
    public string BottomPanelToggleLabel => ShowActivityLog ? "Hide Bottom Panel" : "Show Bottom Panel";
    /// <summary>Chevron shown on the wide handle: down to hide, up to show.</summary>
    public string BottomPanelToggleGlyph => ShowActivityLog ? "\uE70D" : "\uE70E";

    partial void OnShowSmartAnalysisChanged(bool value)
    {
        OnPropertyChanged(nameof(SmartAnalysisToggleLabel));
        OnPropertyChanged(nameof(SmartAnalysisToggleGlyph));
    }
    partial void OnShowActivityLogChanged(bool value)
    {
        OnPropertyChanged(nameof(ActivityLogToggleLabel));
        OnPropertyChanged(nameof(BottomPanelToggleLabel));
        OnPropertyChanged(nameof(BottomPanelToggleGlyph));
    }

    [RelayCommand] private void ToggleSmartAnalysis() => ShowSmartAnalysis = !ShowSmartAnalysis;
    [RelayCommand] private void ToggleActivityLog() => ShowActivityLog = !ShowActivityLog;

    [ObservableProperty] private bool _buildsExpanded;
    [ObservableProperty] private int _hiddenBuildsCount;
    [ObservableProperty] private string _moreBuildsLabel = "More...";

    public bool HasMoreBuilds => !BuildsExpanded && HiddenBuildsCount > 0;
    public bool CanCollapseBuilds => BuildsExpanded && HiddenBuildsCount > 0;

    partial void OnBuildsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasMoreBuilds));
        OnPropertyChanged(nameof(CanCollapseBuilds));
    }

    partial void OnHiddenBuildsCountChanged(int value)
    {
        MoreBuildsLabel = $"More... ({value} older)";
        OnPropertyChanged(nameof(HasMoreBuilds));
        OnPropertyChanged(nameof(CanCollapseBuilds));
    }

    public string AppTitle => "WPRulesReviewer";
    public string Copyright => "(c) 2026 WB Games";

    public MainViewModel(AppSettings settings, SettingsService settingsService, IActivityLog log, ThemeManager theme)
    {
        Settings = settings;
        _settingsService = settingsService;
        _log = log;
        _theme = theme;
        _pipeline = new ReviewPipeline(settings, log);
        _ai = new CursorAgentService(settings, log);

        ActivityLog = new ActivityLogViewModel(log, settings.MaxActivityEntries);
        SettingsVm = new SettingsViewModel(settings, settingsService, log, ApplySettings, () => IsSettingsOpen = false);

        ApprovedReports = new ReportsViewModel(settings, log,
            Core.Reporting.SuspiciousReportStore.ApprovedFileName, "Approved reports", "\uE8FB", "approval");
        SuspiciousReports = new ReportsViewModel(settings, log,
            Core.Reporting.SuspiciousReportStore.SuspiciousFileName, "Suspicious reports", "\uEB90", "suspicious");
        ApprovedReports.Refresh();
        SuspiciousReports.Refresh();

        _log.Info($"{AppTitle} started. {Copyright}", "App");
        _log.Info($"Build: {GetBuildTimestamp():yyyy-MM-dd HH:mm}", "App");

        // Tell the user how to fix AI if it is enabled but the Cursor CLI is missing.
        if (settings.EnableAi && !_ai.IsAvailable)
            Services.CursorAgentGuidance.Log(_log);
    }

    private static DateTime GetBuildTimestamp()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var path = asm.Location;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return File.GetLastWriteTime(path);
        }
        catch { /* best effort */ }
        return DateTime.Now;
    }

    private void ApplySettings(AppSettings s)
    {
        _theme.Apply(s.Theme, s.AccentColor);
        _log.Info("Theme/settings applied.", "App");
    }

    /// <summary>Persist the current settings (used to save the window/panel layout on exit).</summary>
    public void SaveSettings() => _settingsService.Save(Settings);

    // ---- Navigation --------------------------------------------------------
    [RelayCommand] private void ShowReview() => IsSettingsOpen = false;
    [RelayCommand] private void ShowSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void ToggleTheme()
    {
        Settings.Theme = _theme.IsDarkEffective ? AppTheme.Light : AppTheme.Dark;
        _theme.Apply(Settings.Theme, Settings.AccentColor);
        _settingsService.Save(Settings);
    }

    // ---- Oracle ------------------------------------------------------------
    private async Task<OracleConfig> EnsureOracleAsync()
    {
        if (_oracle is { IsLoaded: true }) return _oracle;
        _oracle = await Task.Run(() => _pipeline.LoadOracle());
        return _oracle;
    }

    [RelayCommand]
    private async Task ReloadOracleAsync()
    {
        _oracle = null;
        await EnsureOracleAsync();
    }

    // ---- TeamCity ----------------------------------------------------------
    [RelayCommand]
    private async Task RefreshBuildsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Fetching TeamCity builds...";
        try
        {
            using var client = new TeamCityClient(Settings);
            if (!client.IsConfigured)
            {
                StatusText = "TeamCity is not configured. Open Settings.";
                _log.Warning(StatusText, "TeamCity");
                TeamCityGuidance.Log(_log);
                return;
            }
            _log.Info("Requesting recent builds...", "TeamCity");
            var builds = await client.GetRecentBuildsAsync();
            _allBuilds.Clear();
            foreach (var b in builds) _allBuilds.Add(new BuildItemViewModel(b));
            RefreshBuildsView();

            if (!_initialSelectionDone && Settings.PreselectLatestDayOnStartup)
            {
                var n = PreselectLatestDay();
                _initialSelectionDone = true;
                if (n > 0) _log.Info($"Preselected {n} build(s) from the latest day.", "TeamCity");
            }

            StatusText = $"{builds.Count} build(s) loaded (showing last {Settings.TeamCityRecentDays} days).";
            _log.Success(StatusText, "TeamCity");
        }
        catch (Exception ex)
        {
            StatusText = $"TeamCity error: {ex.Message}";
            _log.Error(StatusText, "TeamCity");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AnalyzeSelectedAsync()
    {
        var selected = _allBuilds.Where(b => b.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Select at least one build to review.";
            return;
        }
        if (IsBusy) return;
        IsBusy = true;
        IsAnalyzing = true;
        try
        {
            var oracle = await EnsureOracleAsync();
            var destFolder = string.IsNullOrWhiteSpace(Settings.LogDownloadFolder) ? AppPaths.Logs : Settings.LogDownloadFolder;

            using var client = new TeamCityClient(Settings);
            var isFirstOfBatch = true;
            foreach (var item in selected)
            {
                _log.Scope = BuildScopeLabel(item.SessionName, item.Build.Number);
                try
                {
                    StatusText = $"Downloading log for {item.SessionName}...";
                    _log.Info(StatusText, "TeamCity");
                    var artifacts = await client.ListLogArtifactsAsync(item.Build.Id);
                    if (artifacts.Count == 0)
                    {
                        _log.Warning($"No .log artifact found for {item.SessionName}.", "TeamCity");
                        continue;
                    }
                    var chosen = artifacts.OrderByDescending(a => a.Size).First();
                    var fileName = BuildLogFileName(chosen.Name, item.Build, item.SessionName);
                    var localPath = await client.DownloadArtifactAsync(item.Build.Id, chosen.Name, destFolder,
                        destinationFileName: fileName);
                    _log.Success($"Downloaded {chosen.Name} -> {fileName} ({chosen.Size / 1024} KB).", "TeamCity");

                    await AnalyzeFileAsync(localPath, item.SessionName, oracle, item.Build, focus: isFirstOfBatch);
                    isFirstOfBatch = false;
                    item.IsAnalyzed = true;
                }
                finally { _log.Scope = null; }
            }
            StatusText = "Analysis complete.";
        }
        catch (Exception ex)
        {
            StatusText = $"Analysis error: {ex.Message}";
            _log.Error(StatusText, "Analyze");
        }
        finally { IsBusy = false; IsAnalyzing = false; _log.Scope = null; }
    }

    /// <summary>Builds a short label for the activity-log build/session scope, e.g. "Missions #17804932".</summary>
    private static string BuildScopeLabel(string sessionName, string? buildNumber)
        => string.IsNullOrWhiteSpace(buildNumber) ? sessionName : $"{sessionName} #{buildNumber}";

    [RelayCommand]
    private async Task OpenLocalLogAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Log file (*.log)|*.log|All files (*.*)|*.*",
            InitialDirectory = string.IsNullOrWhiteSpace(Settings.LastLogFolder) ? null : Settings.LastLogFolder
        };
        if (dlg.ShowDialog() != true) return;

        Settings.LastLogFolder = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
        _settingsService.Save(Settings);

        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var oracle = await EnsureOracleAsync();
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            _log.Scope = name;
            try { await AnalyzeFileAsync(dlg.FileName, name, oracle, null); }
            finally { _log.Scope = null; }
            StatusText = "Analysis complete.";
        }
        catch (Exception ex)
        {
            StatusText = $"Analysis error: {ex.Message}";
            _log.Error(StatusText, "Analyze");
        }
        finally { IsBusy = false; }
    }

    private async Task<SessionViewModel> AnalyzeFileAsync(string path, string sessionName, OracleConfig oracle,
        TeamCityBuild? build, bool focus = true)
    {
        var session = new SessionViewModel(sessionName, Settings, _log, _ai, Inspector);
        session.ReportSaved += approved =>
        {
            if (approved) ApprovedReports.Refresh();
            else SuspiciousReports.Refresh();
        };
        Sessions.Add(session);
        if (focus || SelectedSession is null) SelectedSession = session;
        IsSettingsOpen = false;
        // The tab (with its own processing spinner) is now visible: drop the full-view overlay.
        IsAnalyzing = false;

        var progress = new Progress<PipelineStep>(step => session.ApplyStep(step));
        StatusText = $"Analyzing {sessionName}...";
        var report = await _pipeline.RunAsync(path, sessionName, oracle, progress);

        report.SourcePath = path;
        if (build is not null)
        {
            report.TeamCityBuildId = (int)build.Id;
            report.BuildNumber = build.Number;
            report.WebUrl = build.WebUrl;
            session.SetProcessingDate(build.StartDate);
        }
        session.SetReport(report);
        return session;
    }

    /// <summary>
    /// Builds a descriptive local file name for a downloaded log, e.g. "Sundance_11August_Missions.log",
    /// using the TeamCity build's processing date (not today) and the session name.
    /// </summary>
    private static string BuildLogFileName(string artifactName, TeamCityBuild build, string sessionName)
    {
        var prefix = Path.GetFileNameWithoutExtension(artifactName);
        if (string.IsNullOrWhiteSpace(prefix)) prefix = "Sundance";
        var ext = Path.GetExtension(artifactName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".log";

        var date = (build.StartDate?.LocalDateTime ?? DateTime.Now)
            .ToString("dMMMM", System.Globalization.CultureInfo.InvariantCulture); // e.g. "11August"

        var session = SanitizeForFileName(sessionName);
        return string.IsNullOrEmpty(session)
            ? $"{prefix}_{date}{ext}"
            : $"{prefix}_{date}_{session}{ext}";
    }

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (c is '(' or ')' or '#') continue;              // drop decorative characters like in "(All)"
            if (char.IsWhiteSpace(c)) { sb.Append('-'); continue; }
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0) continue;
            sb.Append(c);
        }
        return sb.ToString().Trim('-');
    }

    [RelayCommand]
    private void CloseSession(SessionViewModel? session)
    {
        if (session is null) return;
        var idx = Sessions.IndexOf(session);
        Sessions.Remove(session);
        if (SelectedSession == session)
            SelectedSession = Sessions.Count > 0 ? Sessions[Math.Min(idx, Sessions.Count - 1)] : null;
    }

    private void RefreshBuildsView()
    {
        var cutoff = DateTimeOffset.Now.AddDays(-Math.Max(1, Settings.TeamCityRecentDays));
        var recent = _allBuilds.Where(b => b.Build.StartDate is null || b.Build.StartDate >= cutoff).ToList();
        HiddenBuildsCount = _allBuilds.Count - recent.Count;

        var display = BuildsExpanded ? _allBuilds : recent;
        Builds.Clear();
        foreach (var b in display) Builds.Add(b);
    }

    [RelayCommand]
    private void ShowMoreBuilds()
    {
        BuildsExpanded = true;
        RefreshBuildsView();
    }

    [RelayCommand]
    private void ShowLessBuilds()
    {
        BuildsExpanded = false;
        RefreshBuildsView();
    }

    /// <summary>Checks every build whose start date falls on the most recent day present in the list.</summary>
    private int PreselectLatestDay()
    {
        var dated = _allBuilds.Where(b => b.Build.StartDate is not null).ToList();
        if (dated.Count == 0) return 0;

        var latestDay = dated.Max(b => b.Build.StartDate!.Value.LocalDateTime.Date);
        var count = 0;
        foreach (var b in _allBuilds)
        {
            var isLatest = b.Build.StartDate is { } d && d.LocalDateTime.Date == latestDay;
            b.IsSelected = isLatest;
            if (isLatest) count++;
        }
        return count;
    }

    [RelayCommand]
    private void SelectAllBuilds()
    {
        foreach (var b in _allBuilds) b.IsSelected = true;
    }

    [RelayCommand]
    private void ClearBuildSelection()
    {
        foreach (var b in _allBuilds) b.IsSelected = false;
    }
}
