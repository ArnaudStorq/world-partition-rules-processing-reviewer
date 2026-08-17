using WPRulesReviewer.Core.Logging;

namespace WPRulesReviewer.App.Services;

/// <summary>Single source of truth for the "how to install the Cursor CLI (cursor-agent)" procedure.</summary>
public static class CursorAgentGuidance
{
    public const string Title = "How to enable AI (install the Cursor CLI)";

    public static readonly string[] Steps =
    {
        "1. Open Windows PowerShell (use Windows PowerShell 5.1 if the script fails on Get-WmiObject).",
        "2. Run: irm 'https://cursor.com/install?win32=true' | iex",
        "3. Verify with: cursor-agent --version (it installs to %LOCALAPPDATA%\\cursor-agent and is added to PATH).",
        "4. Restart this application so it picks up the updated PATH.",
        "5. If it is installed elsewhere, set the full path in Settings > AI > cursor-agent path."
    };

    /// <summary>Writes the full procedure to the activity log.</summary>
    public static void Log(IActivityLog log)
    {
        log.Warning("cursor-agent was not found. AI features are disabled until it is installed.", "AI");
        log.Info(Title, "AI");
        foreach (var step in Steps) log.Info(step, "AI");
    }
}
