using System.Windows;
using System.Windows.Controls;

namespace WPRulesReviewer.App.Services;

/// <summary>
/// A minimal, theme-aware modal prompt for multi-line free text.
/// Built in code so it needs no extra XAML and inherits the application resources.
/// </summary>
public static class PromptDialog
{
    /// <summary>Show the prompt. Returns the entered text, or null if the user cancelled.</summary>
    public static string? Show(Window? owner, string title, string message, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Owner = owner,
            Width = 560,
            Height = 340,
            MinWidth = 420,
            MinHeight = 260,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow
        };

        if (Application.Current?.Resources["Brush.Surface"] is System.Windows.Media.Brush bg)
            win.Background = bg;

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(label, 0);

        var box = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 120
        };
        Grid.SetRow(box, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button { Content = "Save", Width = 96, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 96, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);

        ok.Click += (_, _) => win.DialogResult = true;

        grid.Children.Add(label);
        grid.Children.Add(box);
        grid.Children.Add(buttons);
        win.Content = grid;

        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        };

        return win.ShowDialog() == true ? box.Text : null;
    }
}
