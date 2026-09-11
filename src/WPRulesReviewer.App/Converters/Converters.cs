using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.Converters;

/// <summary>Bool to Visibility. Pass "invert" as parameter to reverse.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (IsInvert(parameter)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is Visibility.Visible;
        return IsInvert(parameter) ? !visible : visible;
    }

    private static bool IsInvert(object? p) => string.Equals(p as string, "invert", StringComparison.OrdinalIgnoreCase);
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not true;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not true;
}

/// <summary>Visible when the string is non-empty (or empty when "invert").</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var hasText = !string.IsNullOrWhiteSpace(value as string);
        if (string.Equals(p as string, "invert", StringComparison.OrdinalIgnoreCase)) hasText = !hasText;
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Visible when an int/count is greater than zero.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var n = value is int i ? i : 0;
        var visible = n > 0;
        if (string.Equals(p as string, "invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Slider values are doubles; int dependency properties need the rounded value.</summary>
public sealed class DoubleToIntConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is double d ? (int)Math.Round(d) : 0;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is int i ? (double)i : 0d;
}

/// <summary>Visible when a numeric value is strictly positive (progress bars, ratios).</summary>
public sealed class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var positive = value switch
        {
            double d => d > 0,
            int i => i > 0,
            _ => false
        };
        if (string.Equals(p as string, "invert", StringComparison.OrdinalIgnoreCase)) positive = !positive;
        return positive ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Checks that a value equals the given parameter (used for nav toggles / enum radios).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal);
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is true && p is not null ? p : Binding.DoNothing;
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var key = value switch
        {
            AnomalySeverity.High => "Brush.SevHigh",
            AnomalySeverity.Medium => "Brush.SevMedium",
            AnomalySeverity.Low => "Brush.SevLow",
            _ => "Brush.SevNone"
        };
        return BrushLookup.Find(key);
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var key = value switch
        {
            ReviewStatus.Anomaly => "Brush.StatusAnomaly",
            ReviewStatus.NeedsReview => "Brush.StatusNeedsReview",
            ReviewStatus.Expected => "Brush.StatusExpected",
            _ => "Brush.StatusKnownNoise"
        };
        return BrushLookup.Find(key);
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class CategoryToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var key = value switch
        {
            RecordCategory.Applied => "Brush.StatusExpected",
            RecordCategory.Warning => "Brush.LevelWarning",
            RecordCategory.Error => "Brush.LevelError",
            RecordCategory.ImportError => "Brush.SevHigh",
            RecordCategory.Skipped => "Brush.LevelInfo",
            _ => "Brush.TextMuted"
        };
        return BrushLookup.Find(key);
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public sealed class ActivityLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var key = value switch
        {
            ActivityLevel.Error => "Brush.LevelError",
            ActivityLevel.Warning => "Brush.LevelWarning",
            ActivityLevel.Success => "Brush.LevelSuccess",
            ActivityLevel.Info => "Brush.LevelInfo",
            _ => "Brush.LevelDebug"
        };
        return BrushLookup.Find(key);
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>
/// Hex string to Brush, falling back to the active theme accent. An empty accent setting means
/// "use the theme's own accent", so the swatch must still show something meaningful.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var hex = value as string;
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex) is Color color) return new SolidColorBrush(color);
            }
            catch { /* not a color: fall through to the theme accent */ }
        }
        return BrushLookup.Find("Brush.Accent");
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Maps a slice index to one of the eight categorical chart brushes (wraps around).</summary>
public sealed class IndexToChartBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not int index || index < 0) return BrushLookup.Find("Brush.ChartOther");
        return BrushLookup.Find($"Brush.Chart{index % 8 + 1}");
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

internal static class BrushLookup
{
    public static Brush Find(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
