namespace WPRulesReviewer.Core.Persistence;

/// <summary>Central resolver for the application data folders.</summary>
public static class AppPaths
{
    public const string AppFolderName = "WPRulesReviewer";

    public static string Root
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string EnsureSub(string name)
    {
        var p = Path.Combine(Root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    public static string Logs => EnsureSub("Logs");
    public static string Reports => EnsureSub("Reports");
    public static string ActivityLogFile => Path.Combine(EnsureSub("ActivityLog"),
        $"activity-{DateTime.Now:yyyyMMdd}.log");
}
