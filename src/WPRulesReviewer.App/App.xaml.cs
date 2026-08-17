using System.Windows;
using System.Windows.Threading;
using WPRulesReviewer.App.Services;
using WPRulesReviewer.App.ViewModels;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Persistence;

namespace WPRulesReviewer.App;

public partial class App : Application
{
    private ActivityLog? _activityLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        _activityLog = new ActivityLog(
            settings.LogToFile ? AppPaths.ActivityLogFile : null,
            settings.VerboseLogging);

        var theme = new ThemeManager();
        theme.Apply(settings.Theme, settings.AccentColor);

        var main = new MainViewModel(settings, settingsService, _activityLog, theme);
        var window = new MainWindow { DataContext = main };

        if (settings.RememberWindowLayout)
            ApplyWindowBounds(window, settings);

        MainWindow = window;
        if (settings.StartMinimized)
        {
            window.WindowState = WindowState.Minimized;
            window.Show();
        }
        else
        {
            window.Show();
        }
    }

    private static void ApplyWindowBounds(Window window, WPRulesReviewer.Core.Models.AppSettings s)
    {
        if (s.WindowWidth > 300) window.Width = s.WindowWidth;
        if (s.WindowHeight > 300) window.Height = s.WindowHeight;

        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
        {
            double vsLeft = SystemParameters.VirtualScreenLeft;
            double vsTop = SystemParameters.VirtualScreenTop;
            double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
            var onScreen = s.WindowLeft >= vsLeft - 50 && s.WindowTop >= vsTop - 10 &&
                           s.WindowLeft <= vsRight - 100 && s.WindowTop <= vsBottom - 60;
            if (onScreen)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = s.WindowLeft;
                window.Top = s.WindowTop;
            }
        }

        if (s.WindowMaximized) window.WindowState = WindowState.Maximized;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _activityLog?.Error($"Unhandled exception: {e.Exception.Message}", "App");
        MessageBox.Show(e.Exception.Message, "WPRulesReviewer — Unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
