using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WPRulesReviewer.App.Controls;

/// <summary>
/// A compact, ergonomic 1..5 star confidence picker. Clicking a star sets the rating; hovering
/// previews it. Set <see cref="IsReadOnly"/> for a display-only strip. Raises
/// <see cref="ValueChanged"/> on user interaction (not on programmatic/binding updates).
/// </summary>
public sealed class StarRating : UserControl
{
    private const string Filled = "\uE735"; // FavoriteStarFill
    private const string Empty = "\uE734";  // FavoriteStar
    private static readonly Brush Gold = CreateGold();

    private readonly TextBlock[] _stars = new TextBlock[5];
    private int? _hover;

    /// <summary>Raised when the user picks a rating (1..5).</summary>
    public event EventHandler<int>? ValueChanged;

    public StarRating()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < 5; i++)
        {
            int value = i + 1;
            var star = new TextBlock
            {
                Text = Filled,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = GlyphSize,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            star.MouseLeftButtonUp += (_, _) =>
            {
                if (IsReadOnly) return;
                Rating = value;
                ValueChanged?.Invoke(this, Rating);
            };
            star.MouseEnter += (_, _) =>
            {
                if (IsReadOnly) return;
                _hover = value;
                Refresh();
            };
            star.MouseLeave += (_, _) =>
            {
                if (IsReadOnly) return;
                _hover = null;
                Refresh();
            };
            _stars[i] = star;
            panel.Children.Add(star);
        }

        Content = panel;
        Refresh();
    }

    public static readonly DependencyProperty RatingProperty =
        DependencyProperty.Register(nameof(Rating), typeof(int), typeof(StarRating),
            new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((StarRating)d).Refresh(), CoerceRating));

    /// <summary>Current rating, coerced to the 1..5 range.</summary>
    public int Rating
    {
        get => (int)GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(StarRating),
            new PropertyMetadata(18.0, (d, _) => ((StarRating)d).OnGlyphSizeChanged()));

    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(StarRating),
            new PropertyMetadata(false, (d, _) => ((StarRating)d).OnIsReadOnlyChanged()));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private static object CoerceRating(DependencyObject d, object baseValue)
    {
        int v = (int)baseValue;
        return v < 1 ? 1 : v > 5 ? 5 : v;
    }

    private void OnGlyphSizeChanged()
    {
        foreach (var s in _stars) s.FontSize = GlyphSize;
    }

    private void OnIsReadOnlyChanged()
    {
        var cursor = IsReadOnly ? Cursors.Arrow : Cursors.Hand;
        foreach (var s in _stars) s.Cursor = cursor;
    }

    private void Refresh()
    {
        int upTo = _hover ?? Rating;
        var muted = TryFindResource("Brush.TextMuted") as Brush ?? Brushes.Gray;
        for (int i = 0; i < 5; i++)
        {
            bool on = (i + 1) <= upTo;
            _stars[i].Text = on ? Filled : Empty;
            _stars[i].Foreground = on ? Gold : muted;
        }
    }

    private static Brush CreateGold()
    {
        var b = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
        b.Freeze();
        return b;
    }
}
