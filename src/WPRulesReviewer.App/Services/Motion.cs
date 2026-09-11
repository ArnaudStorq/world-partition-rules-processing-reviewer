using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WPRulesReviewer.App.Services;

/// <summary>
/// Attached behaviour running the shared "fade in and rise" entrance on a panel.
///
/// Motion is opt-in per element and globally switchable: <see cref="IsEnabled"/> is set once from
/// the settings, and when it is off the element is simply shown at its final state, with no
/// storyboard created at all.
/// </summary>
public static class Motion
{
    /// <summary>Mirrors AppSettings.ReduceMotion, inverted. Set once at startup.</summary>
    public static bool IsEnabled { get; set; } = true;

    public static readonly DependencyProperty FadeInProperty = DependencyProperty.RegisterAttached(
        "FadeIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFadeInChanged));

    public static void SetFadeIn(DependencyObject element, bool value) => element.SetValue(FadeInProperty, value);
    public static bool GetFadeIn(DependencyObject element) => (bool)element.GetValue(FadeInProperty);

    private static void OnFadeInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= OnLoaded;

        if (!IsEnabled) return;

        var transform = new TranslateTransform(0, 8);
        element.RenderTransform = transform;
        element.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(180));

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
    }
}
