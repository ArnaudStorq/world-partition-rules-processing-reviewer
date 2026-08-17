using CommunityToolkit.Mvvm.ComponentModel;
using WPRulesReviewer.Core.Analysis;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>Observable view of one <see cref="PipelineStep"/> for the transparent processing panel.</summary>
public sealed partial class PipelineStepViewModel : ObservableObject
{
    public string Key { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty]
    private StepStatus _status;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private string _elapsed = string.Empty;

    public PipelineStepViewModel(PipelineStep step)
    {
        Key = step.Key;
        Title = step.Title;
        Description = step.Description;
        Status = step.Status;
        Detail = step.Detail;
    }

    public void Update(PipelineStep step)
    {
        Status = step.Status;
        Detail = step.Detail;
        Elapsed = step.Elapsed.TotalMilliseconds >= 1 ? $"{step.Elapsed.TotalMilliseconds:0} ms" : string.Empty;
    }
}
