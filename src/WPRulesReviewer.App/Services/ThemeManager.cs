using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.Services;

/// <summary>Swaps the light/dark palette dictionary at runtime and applies the user accent color.</summary>
public sealed class ThemeManager
{
    private const string LightUri = "Themes/Theme.Light.xaml";
    private const string DarkUri = "Themes/Theme.Dark.xaml";

    private ResourceDictionary? _palette;

    public AppTheme Current { get; private set; } = AppTheme.System;
    public bool IsDarkEffective { get; private set; }

    public void Apply(AppTheme theme, string accentHex)
    {
        Current = theme;
        var dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark()
        };
        IsDarkEffective = dark;

        var app = Application.Current;
        if (app is null) return;

        var newPalette = new ResourceDictionary
        {
            Source = new Uri(dark ? DarkUri : LightUri, UriKind.Relative)
        };

        var merged = app.Resources.MergedDictionaries;
        if (_palette is not null) merged.Remove(_palette);

        // Remove any palette declared at design time in App.xaml (Theme.Light / Theme.Dark),
        // otherwise it would shadow the runtime palette (last dictionary wins on key conflicts).
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Theme.Light", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Theme.Dark", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }

        // Add last so the active palette wins over Styles for any shared keys.
        merged.Add(newPalette);
        _palette = newPalette;

        ApplyAccent(accentHex);
    }

    public void ApplyAccent(string accentHex)
    {
        var app = Application.Current;
        if (app is null) return;

        if (!TryParseColor(accentHex, out var accent)) return;
        var hover = IsDarkEffective ? Lighten(accent, 0.12) : Darken(accent, 0.12);
        var foreground = Luminance(accent) > 0.6 ? Color.FromRgb(0x1B, 0x1D, 0x28) : Colors.White;

        app.Resources["Color.Accent"] = accent;
        app.Resources["Color.AccentHover"] = hover;
        app.Resources["Color.AccentForeground"] = foreground;
        app.Resources["Brush.Accent"] = new SolidColorBrush(accent);
        app.Resources["Brush.AccentHover"] = new SolidColorBrush(hover);
        app.Resources["Brush.AccentForeground"] = new SolidColorBrush(foreground);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i == 0;
        }
        catch { /* default to light */ }
        return false;
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        color = Colors.MediumPurple;
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var obj = ColorConverter.ConvertFromString(hex);
            if (obj is Color c) { color = c; return true; }
        }
        catch { /* ignore */ }
        return false;
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)Math.Clamp(c.R + 255 * amount, 0, 255),
        (byte)Math.Clamp(c.G + 255 * amount, 0, 255),
        (byte)Math.Clamp(c.B + 255 * amount, 0, 255));

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)Math.Clamp(c.R - 255 * amount, 0, 255),
        (byte)Math.Clamp(c.G - 255 * amount, 0, 255),
        (byte)Math.Clamp(c.B - 255 * amount, 0, 255));
}
