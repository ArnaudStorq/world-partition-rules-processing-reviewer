using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;
using WPRulesReviewer.Core.Persistence;
using WPRulesReviewer.Core.TeamCity;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>Exposes the full <see cref="AppSettings"/> surface for editing.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly IActivityLog _log;
    private readonly Action<AppSettings> _applyTheme;
    private readonly Action? _onSaved;

    public AppSettings Model { get; private set; }

    public AppTheme[] ThemeOptions { get; } = { AppTheme.System, AppTheme.Light, AppTheme.Dark };

    public string TeamCityTokenHelpTitle => Services.TeamCityGuidance.Title;
    public string TeamCityTokenHelp => string.Join(Environment.NewLine, Services.TeamCityGuidance.Steps);

    public string CursorAgentHelpTitle => Services.CursorAgentGuidance.Title;
    public string CursorAgentHelp => string.Join(Environment.NewLine, Services.CursorAgentGuidance.Steps);

    [ObservableProperty] private string _teamCityTestResult = string.Empty;
    [ObservableProperty] private bool _isTestingTeamCity;

    public SettingsViewModel(AppSettings settings, SettingsService service, IActivityLog log,
        Action<AppSettings> applyTheme, Action? onSaved = null)
    {
        Model = settings;
        _service = service;
        _log = log;
        _applyTheme = applyTheme;
        _onSaved = onSaved;
    }

    // ---- List <-> multiline text helpers ----------------------------------
    public string NoiseActorPatternsText
    {
        get => string.Join(Environment.NewLine, Model.NoiseActorPatterns);
        set { Model.NoiseActorPatterns = SplitLines(value); OnPropertyChanged(); }
    }

    public string FlagUnclassifiedTokensText
    {
        get => string.Join(Environment.NewLine, Model.FlagUnclassifiedTokens);
        set { Model.FlagUnclassifiedTokens = SplitLines(value); OnPropertyChanged(); }
    }

    private static List<string> SplitLines(string value) => value
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .ToList();

    // ---- Commands ----------------------------------------------------------
    [RelayCommand]
    private void Save()
    {
        _service.Save(Model);
        _applyTheme(Model);
        _log.Success("Settings saved.", "Settings");
        _onSaved?.Invoke();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanRead && p.CanWrite)
                p.SetValue(Model, p.GetValue(defaults));
        }
        RaiseAll();
        _applyTheme(Model);
        _log.Info("Settings reset to defaults.", "Settings");
    }

    [RelayCommand]
    private void ApplyTheme() => _applyTheme(Model);

    [RelayCommand]
    private void BrowseIni()
    {
        var dlg = new OpenFileDialog { Filter = "INI file (*.ini)|*.ini|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == true) { Model.DefaultEditorIniPath = dlg.FileName; OnPropertyChanged(nameof(Model)); }
    }

    [RelayCommand]
    private void BrowseLogFolder() => PickFolder(v => Model.LogDownloadFolder = v);
    [RelayCommand]
    private void BrowseExportFolder() => PickFolder(v => Model.ExportFolder = v);
    [RelayCommand]
    private void BrowseAppDataFolder() => PickFolder(v => Model.AppDataFolder = v);

    private void PickFolder(Action<string> assign)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) { assign(dlg.FolderName); OnPropertyChanged(nameof(Model)); }
    }

    [RelayCommand]
    private async Task TestTeamCityAsync()
    {
        if (IsTestingTeamCity) return;
        IsTestingTeamCity = true;
        TeamCityTestResult = "Testing...";
        try
        {
            using var client = new TeamCityClient(Model);
            if (!client.IsConfigured)
            {
                TeamCityTestResult = "Not configured: set base URL, build type id and a token.";
                Services.TeamCityGuidance.Log(_log);
                return;
            }
            var builds = await client.GetRecentBuildsAsync();
            TeamCityTestResult = $"OK — {builds.Count} recent build(s) found.";
            _log.Success(TeamCityTestResult, "TeamCity");
        }
        catch (Exception ex)
        {
            TeamCityTestResult = $"Failed: {ex.Message}";
            _log.Error(TeamCityTestResult, "TeamCity");
        }
        finally { IsTestingTeamCity = false; }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(NoiseActorPatternsText));
        OnPropertyChanged(nameof(FlagUnclassifiedTokensText));
    }
}
