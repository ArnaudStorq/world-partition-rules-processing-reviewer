using CommunityToolkit.Mvvm.ComponentModel;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>A TeamCity build shown in the sidebar with a review checkbox.</summary>
public sealed partial class BuildItemViewModel : ObservableObject
{
    public TeamCityBuild Build { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAnalyzed;

    public BuildItemViewModel(TeamCityBuild build) => Build = build;

    public string SessionName
    {
        get
        {
            var s = Build.SessionName;
            if (string.IsNullOrWhiteSpace(s) || s == $"#{Build.Number}" || s == Build.Number)
                return "(All)";
            return s;
        }
    }
    public string Number => $"#{Build.Number}";
    public string When => Build.StartDate is { } d ? d.LocalDateTime.ToString("ddd dd MMM  HH:mm") : "-";
    public bool IsSuccess => Build.IsSuccess;
    public string Status => Build.Status;
}
