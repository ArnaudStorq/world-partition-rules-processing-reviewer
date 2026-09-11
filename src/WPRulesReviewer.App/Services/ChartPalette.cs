using System.Windows;
using System.Windows.Media;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.Services;

/// <summary>
/// Bridges the theme resource dictionaries to the Skia paints LiveCharts needs. Chart colors are
/// never hard-coded: they are read from the active palette so light/dark and the user accent are
/// respected, and re-read after every theme switch.
/// </summary>
public static class ChartPalette
{
    private const int CategoricalCount = 8;

    public static SKColor Categorical(int index) => Resolve($"Brush.Chart{index % CategoricalCount + 1}");

    public static SKColor Other => Resolve("Brush.ChartOther");
    public static SKColor Track => Resolve("Brush.ChartTrack");
    public static SKColor Accent => Resolve("Brush.Accent");
    public static SKColor Surface => Resolve("Brush.Surface");
    public static SKColor SurfaceAlt => Resolve("Brush.SurfaceAlt");
    public static SKColor Border => Resolve("Brush.Border");
    public static SKColor TextPrimary => Resolve("Brush.TextPrimary");
    public static SKColor TextSecondary => Resolve("Brush.TextSecondary");
    public static SKColor TextMuted => Resolve("Brush.TextMuted");

    public static SKColor Severity(AnomalySeverity severity) => Resolve(severity switch
    {
        AnomalySeverity.High => "Brush.SevHigh",
        AnomalySeverity.Medium => "Brush.SevMedium",
        AnomalySeverity.Low => "Brush.SevLow",
        _ => "Brush.SevNone"
    });

    public static SKColor Status(ReviewStatus status) => Resolve(status switch
    {
        ReviewStatus.Anomaly => "Brush.StatusAnomaly",
        ReviewStatus.NeedsReview => "Brush.StatusNeedsReview",
        ReviewStatus.Expected => "Brush.StatusExpected",
        _ => "Brush.StatusKnownNoise"
    });

    /// <summary>
    /// Color of a slice: anomalies keep the semantic red family so the eye still finds the danger
    /// first; everything else walks the categorical ramp.
    /// </summary>
    public static SKColor ForBucket(AnomalySeverity severity, int index) =>
        severity == AnomalySeverity.High ? Severity(severity) : Categorical(index);

    public static SolidColorPaint Paint(SKColor color) => new(color);

    public static SolidColorPaint LabelPaint() => new(TextMuted) { SKTypeface = SKTypeface.FromFamilyName("Segoe UI") };

    /// <summary>
    /// Text paint for the legend and tooltips LiveCharts draws itself. Their defaults are tuned for a
    /// white canvas, so without this they stay black on the dark theme's panels.
    /// </summary>
    public static SolidColorPaint ChromeTextPaint() =>
        new(TextSecondary) { SKTypeface = SKTypeface.FromFamilyName("Segoe UI") };

    private static SKColor Resolve(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return new SKColor(0x7C, 0x82, 0x98);
    }
}
