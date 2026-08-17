using System.Text.Json;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Persistence;

/// <summary>Loads and saves <see cref="AppSettings"/> as indented JSON.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public SettingsService(string? path = null) => _path = path ?? AppPaths.SettingsFile;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings is not null) return settings;
            }
        }
        catch { /* fall back to defaults on any error */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, Options);
            File.WriteAllText(_path, json);
        }
        catch { /* ignore persistence errors */ }
    }
}
