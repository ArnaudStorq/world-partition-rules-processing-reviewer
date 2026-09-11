using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using WPRulesReviewer.App.ViewModels;

namespace WPRulesReviewer.App.Views;

public partial class OverviewPanel : UserControl
{
    public OverviewPanel()
    {
        InitializeComponent();
    }

    private OverviewViewModel? Vm => DataContext as OverviewViewModel;

    /// <summary>Clicking a donut slice scopes the main grid to the records behind it.</summary>
    private void SignatureChart_OnDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
        => Select(points, Vm?.SignatureSeries, Vm?.Legend);

    private void StatusChart_OnDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
        => Select(points, Vm?.StatusSeries, Vm?.StatusLegend);

    /// <summary>
    /// A pie slice is a one-value series, so the clicked point identifies its series, and the series
    /// and the legend are built together in the same order.
    /// </summary>
    private void Select(IEnumerable<ChartPoint> points, ISeries[]? series, IList<OverviewSliceViewModel>? legend)
    {
        if (Vm is null || series is null || legend is null) return;

        var point = points.FirstOrDefault();
        if (point is null) return;

        var index = Array.FindIndex(series, s => ReferenceEquals(s, point.Context.Series));
        if (index < 0 || index >= legend.Count) return;

        Vm.SelectSliceCommand.Execute(legend[index]);
    }
}
