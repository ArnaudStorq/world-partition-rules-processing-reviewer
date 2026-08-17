using WPRulesReviewer.Core.Logging;

namespace WPRulesReviewer.App.Services;

/// <summary>Single source of truth for the "how to create a TeamCity access token" procedure.</summary>
public static class TeamCityGuidance
{
    public const string Title = "How to create a TeamCity access token";

    public static readonly string[] Steps =
    {
        "1. Open https://slc-teamcity.wbiegames.com in your browser.",
        "2. Top-right avatar > Profile > Access Tokens > Create access token.",
        "3. Name it (e.g. WPRulesReviewer), keep the default scope, then Create.",
        "4. Copy the token (it is shown only once).",
        "5. Paste it in Settings > TeamCity > Access token (Bearer), then click Test connection and Save."
    };

    /// <summary>Writes the full procedure to the activity log.</summary>
    public static void Log(IActivityLog log)
    {
        log.Info(Title, "TeamCity");
        foreach (var step in Steps) log.Info(step, "TeamCity");
    }
}
