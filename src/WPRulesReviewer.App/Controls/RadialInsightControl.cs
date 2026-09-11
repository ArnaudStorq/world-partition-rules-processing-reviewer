using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WPRulesReviewer.App.ViewModels;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.Controls;

/// <summary>
/// Sunburst view of the insight tree: one ring per hierarchy level, each slice sized by
/// record count. Drawn directly in <see cref="OnRender"/> because a WPF element per slice would cost
/// thousands of visuals on a real session; the whole chart is a few hundred geometry operations.
/// </summary>
public sealed class RadialInsightControl : FrameworkElement
{
    private const double MinSweepForLabel = 14;   // degrees: below this, a label is unreadable
    private const double RingGap = 2;
    private const double CenterHoleRatio = 0.30;

    private readonly List<Slice> _slices = new();
    private Slice? _hovered;
    private Typeface? _typeface;

    public RadialInsightControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    // ---- Dependency properties ---------------------------------------------
    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(
        nameof(Root), typeof(WarningNodeViewModel), typeof(RadialInsightControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDataChanged));

    public static readonly DependencyProperty RootsProperty = DependencyProperty.Register(
        nameof(Roots), typeof(System.Collections.IEnumerable), typeof(RadialInsightControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDataChanged));

    public static readonly DependencyProperty MaxDepthProperty = DependencyProperty.Register(
        nameof(MaxDepth), typeof(int), typeof(RadialInsightControl),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsRender, OnDataChanged));

    public static readonly DependencyProperty CenterTitleProperty = DependencyProperty.Register(
        nameof(CenterTitle), typeof(string), typeof(RadialInsightControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Focused subtree. Null means "show the whole forest given by <see cref="Roots"/>".</summary>
    public WarningNodeViewModel? Root
    {
        get => (WarningNodeViewModel?)GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public System.Collections.IEnumerable? Roots
    {
        get => (System.Collections.IEnumerable?)GetValue(RootsProperty);
        set => SetValue(RootsProperty, value);
    }

    public int MaxDepth
    {
        get => (int)GetValue(MaxDepthProperty);
        set => SetValue(MaxDepthProperty, value);
    }

    public string CenterTitle
    {
        get => (string)GetValue(CenterTitleProperty);
        set => SetValue(CenterTitleProperty, value);
    }

    /// <summary>Raised when a slice is clicked: the host scopes the grid to that node.</summary>
    public event Action<WarningNodeViewModel>? NodeActivated;

    /// <summary>Raised on a double click: the host focuses the chart on that node (drill down).</summary>
    public event Action<WarningNodeViewModel>? NodeDrillDown;

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RadialInsightControl)d).Rebuild();

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Rebuild();
    }

    // ---- Layout -------------------------------------------------------------
    private sealed class Slice
    {
        public required WarningNodeViewModel Node { get; init; }
        public required double StartAngle { get; init; }
        public required double SweepAngle { get; init; }
        public required double InnerRadius { get; init; }
        public required double OuterRadius { get; init; }
        public required int Depth { get; init; }
        public required Brush Fill { get; init; }
        public Geometry? Geometry { get; set; }
    }

    private void Rebuild()
    {
        _slices.Clear();
        _hovered = null;

        var levels = Math.Max(1, MaxDepth);
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 4;
        if (radius <= 20) { InvalidateVisual(); return; }

        var hole = radius * CenterHoleRatio;
        var ringWidth = (radius - hole) / levels;

        var top = TopLevelNodes();
        var total = top.Sum(n => n.Count);
        if (total == 0) { InvalidateVisual(); return; }

        var angle = -90.0;   // start at twelve o'clock
        var index = 0;
        foreach (var node in top)
        {
            var sweep = 360.0 * node.Count / total;
            AddSlice(node, angle, sweep, 0, hole, ringWidth, levels, PaletteBrush(node, index));
            angle += sweep;
            index++;
        }

        InvalidateVisual();
    }

    private IReadOnlyList<WarningNodeViewModel> TopLevelNodes()
    {
        if (Root is not null)
        {
            Root.LoadChildren();
            return Root.Children.Count > 0 ? Root.Children : new[] { Root };
        }

        if (Roots is null) return Array.Empty<WarningNodeViewModel>();
        return Roots.OfType<WarningNodeViewModel>().ToList();
    }

    private void AddSlice(WarningNodeViewModel node, double startAngle, double sweep, int depth,
        double hole, double ringWidth, int levels, Brush fill)
    {
        if (sweep <= 0.05) return;   // sub-pixel slices only add noise

        var inner = hole + depth * ringWidth;
        var outer = inner + ringWidth - RingGap;

        _slices.Add(new Slice
        {
            Node = node,
            StartAngle = startAngle,
            SweepAngle = sweep,
            InnerRadius = inner,
            OuterRadius = outer,
            Depth = depth,
            Fill = fill
        });

        if (depth + 1 >= levels) return;

        // Only descend into branches already materialized, or cheap enough to materialize.
        if (!node.HasChildren) return;
        node.LoadChildren();
        if (node.Children.Count == 0) return;

        var childTotal = node.Children.Sum(c => c.Count);
        if (childTotal == 0) return;

        var angle = startAngle;
        foreach (var child in node.Children)
        {
            var childSweep = sweep * child.Count / childTotal;
            // Children inherit the parent hue, one shade lighter per ring, so a family reads as a block.
            AddSlice(child, angle, childSweep, depth + 1, hole, ringWidth, levels, Shade(fill, depth + 1));
            angle += childSweep;
        }
    }

    // ---- Painting -----------------------------------------------------------
    private Brush PaletteBrush(WarningNodeViewModel node, int index)
    {
        if (node.MaxSeverity == AnomalySeverity.High) return Resource("Brush.SevHigh");
        return Resource($"Brush.Chart{index % 8 + 1}");
    }

    /// <summary>Outer rings are progressively lighter, which reads as depth without extra colors.</summary>
    private static Brush Shade(Brush source, int depth)
    {
        if (source is not SolidColorBrush solid) return source;

        var factor = Math.Min(0.42, 0.16 * depth);
        var c = solid.Color;
        var shaded = Color.FromArgb(c.A,
            (byte)(c.R + (255 - c.R) * factor),
            (byte)(c.G + (255 - c.G) * factor),
            (byte)(c.B + (255 - c.B) * factor));

        var brush = new SolidColorBrush(shaded);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Theme brushes take their color from a <c>DynamicResource</c>, so they carry a live resource
    /// reference and cannot be frozen. Snapshotting the color gives a frozen brush the drawing code
    /// can hold on to; the control redraws on theme change anyway.
    /// </summary>
    private Brush Resource(string key)
    {
        var brush = TryFindResource(key) as Brush ?? Application.Current?.TryFindResource(key) as Brush;
        if (brush is null) return Brushes.Gray;
        if (brush.IsFrozen) return brush;

        if (brush is SolidColorBrush solid)
        {
            var snapshot = new SolidColorBrush(solid.Color);
            snapshot.Freeze();
            return snapshot;
        }
        return brush;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // A transparent background is what makes the whole surface hit-testable for hover.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_slices.Count == 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var stroke = new Pen(Resource("Brush.Surface"), 1);
        if (stroke.CanFreeze) stroke.Freeze();

        _typeface ??= new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        foreach (var slice in _slices)
        {
            slice.Geometry ??= BuildRing(center, slice);

            var isHovered = ReferenceEquals(slice, _hovered);
            dc.PushOpacity(isHovered ? 1.0 : 0.92);
            dc.DrawGeometry(slice.Fill, stroke, slice.Geometry);
            dc.Pop();

            if (slice.SweepAngle >= MinSweepForLabel)
                DrawLabel(dc, center, slice);
        }

        DrawCenter(dc, center);
    }

    private void DrawLabel(DrawingContext dc, Point center, Slice slice)
    {
        var mid = slice.StartAngle + slice.SweepAngle / 2;
        var radius = (slice.InnerRadius + slice.OuterRadius) / 2;
        var point = PointOnCircle(center, radius, mid);

        var available = slice.OuterRadius - slice.InnerRadius;
        var text = new FormattedText(Truncate(slice.Node.Title, slice.SweepAngle),
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface!,
            slice.Depth == 0 ? 11.5 : 10.5, Resource("Brush.TextPrimary"), 96)
        {
            MaxTextWidth = Math.Max(30, available * 3),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(text, new Point(point.X - text.Width / 2, point.Y - text.Height / 2));
    }

    private static string Truncate(string value, double sweep)
    {
        var budget = (int)Math.Clamp(sweep * 0.9, 6, 34);
        return value.Length <= budget ? value : value[..(budget - 1)] + "\u2026";
    }

    private void DrawCenter(DrawingContext dc, Point center)
    {
        var title = string.IsNullOrWhiteSpace(CenterTitle) ? Root?.Title : CenterTitle;
        if (string.IsNullOrWhiteSpace(title)) return;

        var hovered = _hovered?.Node;
        var line1 = hovered?.Title ?? title;
        var line2 = hovered is not null ? $"{hovered.Count} records" : $"{_slices.Where(s => s.Depth == 0).Sum(s => s.Node.Count)} records";

        var t1 = new FormattedText(Truncate(line1, 40), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface!, 12, Resource("Brush.TextPrimary"), 96) { MaxTextWidth = 150, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis };
        var t2 = new FormattedText(line2, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface!, 11, Resource("Brush.TextMuted"), 96);

        dc.DrawText(t1, new Point(center.X - t1.Width / 2, center.Y - t1.Height));
        dc.DrawText(t2, new Point(center.X - t2.Width / 2, center.Y + 2));
    }

    private static Geometry BuildRing(Point center, Slice slice)
    {
        var start = slice.StartAngle;
        var end = slice.StartAngle + slice.SweepAngle;
        var isLarge = slice.SweepAngle > 180;

        // A full circle cannot be expressed with a single arc: draw it as two half rings.
        if (slice.SweepAngle >= 359.99)
        {
            var full = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(center, slice.OuterRadius, slice.OuterRadius),
                new EllipseGeometry(center, slice.InnerRadius, slice.InnerRadius));
            full.Freeze();
            return full;
        }

        var outerStart = PointOnCircle(center, slice.OuterRadius, start);
        var outerEnd = PointOnCircle(center, slice.OuterRadius, end);
        var innerEnd = PointOnCircle(center, slice.InnerRadius, end);
        var innerStart = PointOnCircle(center, slice.InnerRadius, start);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, isFilled: true, isClosed: true);
            ctx.ArcTo(outerEnd, new Size(slice.OuterRadius, slice.OuterRadius), 0, isLarge,
                SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(innerEnd, true, false);
            ctx.ArcTo(innerStart, new Size(slice.InnerRadius, slice.InnerRadius), 0, isLarge,
                SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var rad = angleDegrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    // ---- Interaction --------------------------------------------------------
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var slice = HitTest(e.GetPosition(this));
        if (ReferenceEquals(slice, _hovered)) return;

        _hovered = slice;
        Cursor = slice is null ? Cursors.Arrow : Cursors.Hand;
        ToolTip = slice is null ? null : $"{slice.Node.Title}\n{slice.Node.CountTooltip}";
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hovered is null) return;
        _hovered = null;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var slice = HitTest(e.GetPosition(this));
        if (slice is null) return;

        if (e.ClickCount >= 2) NodeDrillDown?.Invoke(slice.Node);
        else NodeActivated?.Invoke(slice.Node);
        e.Handled = true;
    }

    private Slice? HitTest(Point position)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var dx = position.X - center.X;
        var dy = position.Y - center.Y;
        var radius = Math.Sqrt(dx * dx + dy * dy);

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;   // -180..180, 0 = three o'clock

        foreach (var slice in _slices)
        {
            if (radius < slice.InnerRadius || radius > slice.OuterRadius) continue;
            if (AngleInSlice(angle, slice.StartAngle, slice.SweepAngle)) return slice;
        }
        return null;
    }

    private static bool AngleInSlice(double angle, double start, double sweep)
    {
        var delta = Normalize(angle - start);
        return delta >= 0 && delta <= sweep;
    }

    private static double Normalize(double angle)
    {
        while (angle < 0) angle += 360;
        while (angle >= 360) angle -= 360;
        return angle;
    }
}
