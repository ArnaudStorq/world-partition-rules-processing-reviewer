using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using WPRulesReviewer.App.Services;
using WPRulesReviewer.Core.Analysis;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.ViewModels;

/// <summary>One entry of the legend / bar list, kept clickable so charts and grid stay in sync.</summary>
public sealed partial class OverviewSliceViewModel : ObservableObject
{
    public OverviewSliceViewModel(string title, int count, int total, System.Windows.Media.Brush brush,
        IReadOnlyList<RuleRecord> records)
    {
        Title = title;
        Count = count;
        Share = total == 0 ? 0 : (double)count / total;
        Brush = brush;
        Records = records;
    }

    public string Title { get; }
    public int Count { get; }
    public double Share { get; }
    public string ShareLabel => Share.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);
    public System.Windows.Media.Brush Brush { get; }
    public IReadOnlyList<RuleRecord> Records { get; }
}

/// <summary>
/// Smart Analysis "Overview" tab: proportions of the session at a glance. Every visual reads from
/// the same <see cref="InsightTree"/> as the Warning Explorer, so the numbers always agree.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action<string, IReadOnlyList<RuleRecord>> _scopeToRecords;

    private IReadOnlyList<RuleRecord> _records = Array.Empty<RuleRecord>();
    private InsightTree? _tree;

    public OverviewViewModel(AppSettings settings, Action<string, IReadOnlyList<RuleRecord>> scopeToRecords)
    {
        _settings = settings;
        _scopeToRecords = scopeToRecords;
    }

    // ---- Charts -------------------------------------------------------------
    [ObservableProperty] private ISeries[] _signatureSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _statusSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _topProblemSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _topProblemXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _topProblemYAxes = Array.Empty<Axis>();

    public ObservableCollection<OverviewSliceViewModel> Legend { get; } = new();
    public ObservableCollection<OverviewSliceViewModel> StatusLegend { get; } = new();

    // ---- Headline numbers ---------------------------------------------------
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _litigiousCount;
    [ObservableProperty] private int _readCount;
    [ObservableProperty] private int _signatureCount;

    public double ReadRatio => TotalCount == 0 ? 0 : (double)ReadCount / TotalCount;
    public string ReadRatioLabel => ReadRatio.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);
    public bool HasData => TotalCount > 0;

    /// <summary>
    /// The errors and warnings of the session. The Overview lives next to the Errors / Warnings grid,
    /// so it must count the same population, not the whole log.
    /// </summary>
    public void SetRecords(IReadOnlyList<RuleRecord> records)
    {
        _records = records;
        Rebuild();
    }

    /// <summary>Recomputes every visual. Also called after a theme switch so the paints follow.</summary>
    public void Rebuild()
    {
        var litigious = _records
            .Where(r => r.Status is ReviewStatus.Anomaly or ReviewStatus.NeedsReview
                        || r.Category is RecordCategory.Error or RecordCategory.ImportError)
            .ToList();

        _tree = InsightTree.Build(litigious, InsightPreset.ByProblem.Dimensions);
        var buckets = _tree.SignatureBuckets();

        TotalCount = _records.Count;
        LitigiousCount = litigious.Count;
        ReadCount = _records.Count(r => r.IsRead);
        SignatureCount = buckets.Count;

        BuildSignatureDonut(buckets);
        BuildStatusDonut();
        BuildTopProblems(buckets);

        OnPropertyChanged(nameof(ReadRatio));
        OnPropertyChanged(nameof(ReadRatioLabel));
        OnPropertyChanged(nameof(HasData));
    }

    private void BuildSignatureDonut(IReadOnlyList<SignatureBucket> buckets)
    {
        Legend.Clear();
        if (buckets.Count == 0)
        {
            SignatureSeries = Array.Empty<ISeries>();
            return;
        }

        var top = Math.Max(3, _settings.ChartTopSlices);
        var head = buckets.Take(top).ToList();
        var tail = buckets.Skip(top).ToList();

        var series = new List<ISeries>();
        var index = 0;
        var total = buckets.Sum(b => b.Count);

        foreach (var bucket in head)
        {
            var color = ChartPalette.ForBucket(bucket.Signature.Severity, index);
            series.Add(new PieSeries<int>
            {
                Values = new[] { bucket.Count },
                Name = bucket.Signature.Title,
                Fill = ChartPalette.Paint(color),
                InnerRadius = 68,
                MaxRadialColumnWidth = 28,
                Stroke = null
            });
            Legend.Add(new OverviewSliceViewModel(bucket.Signature.Title, bucket.Count, total, ToBrush(color), bucket.Records));
            index++;
        }

        if (tail.Count > 0)
        {
            var tailRecords = tail.SelectMany(b => b.Records).ToList();
            series.Add(new PieSeries<int>
            {
                Values = new[] { tailRecords.Count },
                Name = $"Other ({tail.Count} signatures)",
                Fill = ChartPalette.Paint(ChartPalette.Other),
                InnerRadius = 68,
                MaxRadialColumnWidth = 28,
                Stroke = null
            });
            Legend.Add(new OverviewSliceViewModel($"Other ({tail.Count} signatures)", tailRecords.Count, total,
                ToBrush(ChartPalette.Other), tailRecords));
        }

        SignatureSeries = series.ToArray();
    }

    private void BuildStatusDonut()
    {
        StatusLegend.Clear();

        var series = new List<ISeries>();
        var total = _records.Count;

        foreach (var status in new[] { ReviewStatus.Anomaly, ReviewStatus.NeedsReview, ReviewStatus.Expected, ReviewStatus.KnownNoise })
        {
            var records = _records.Where(r => r.Status == status).ToList();
            if (records.Count == 0) continue;

            var color = ChartPalette.Status(status);
            series.Add(new PieSeries<int>
            {
                Values = new[] { records.Count },
                Name = InsightTree.StatusLabel(status),
                Fill = ChartPalette.Paint(color),
                InnerRadius = 52,
                MaxRadialColumnWidth = 22,
                Stroke = null
            });
            StatusLegend.Add(new OverviewSliceViewModel(InsightTree.StatusLabel(status), records.Count, total,
                ToBrush(color), records));
        }

        StatusSeries = series.ToArray();
    }

    /// <summary>Top problems as a read/unread stacked row chart: what is left to review, per family.</summary>
    private void BuildTopProblems(IReadOnlyList<SignatureBucket> buckets)
    {
        const int topRows = 10;
        var top = buckets.Take(topRows).Reverse().ToList();   // reversed: the biggest ends up on top

        if (top.Count == 0)
        {
            TopProblemSeries = Array.Empty<ISeries>();
            TopProblemXAxes = Array.Empty<Axis>();
            TopProblemYAxes = Array.Empty<Axis>();
            return;
        }

        TopProblemSeries = new ISeries[]
        {
            new StackedRowSeries<int>
            {
                Name = "Unread",
                Values = top.Select(b => b.UnreadCount).ToArray(),
                Fill = ChartPalette.Paint(ChartPalette.Accent),
                Stroke = null,
                MaxBarWidth = 18
            },
            new StackedRowSeries<int>
            {
                Name = "Read",
                Values = top.Select(b => b.ReadCount).ToArray(),
                Fill = ChartPalette.Paint(ChartPalette.Track),
                Stroke = null,
                MaxBarWidth = 18
            }
        };

        TopProblemYAxes = new[]
        {
            new Axis
            {
                Labels = top.Select(b => Ellipsize(b.Signature.Title, 44)).ToArray(),
                LabelsPaint = ChartPalette.LabelPaint(),
                TextSize = 11,
                SeparatorsPaint = null
            }
        };

        TopProblemXAxes = new[]
        {
            new Axis
            {
                LabelsPaint = ChartPalette.LabelPaint(),
                TextSize = 11,
                SeparatorsPaint = ChartPalette.Paint(ChartPalette.Track),
                MinLimit = 0
            }
        };
    }

    private static string Ellipsize(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "\u2026";

    private static System.Windows.Media.Brush ToBrush(SkiaSharp.SKColor color) =>
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));

    /// <summary>Clicking a legend entry scopes the main grid to that slice.</summary>
    [RelayCommand]
    private void SelectSlice(OverviewSliceViewModel? slice)
    {
        if (slice is null || slice.Records.Count == 0) return;
        _scopeToRecords(slice.Title, slice.Records);
    }
}
