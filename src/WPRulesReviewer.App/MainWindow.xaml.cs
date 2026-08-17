using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WPRulesReviewer.App.ViewModels;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App;

public partial class MainWindow : Window
{
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += (_, _) => Tray?.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        ApplyLayout(vm);
        SetLogPanelVisible(vm.ShowActivityLog);

        if (vm.Settings.AutoRefreshOnStartup && vm.RefreshBuildsCommand.CanExecute(null))
            await vm.RefreshBuildsCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowActivityLog) && DataContext is MainViewModel vm)
            SetLogPanelVisible(vm.ShowActivityLog);
    }

    private AppSettings? Settings => (DataContext as MainViewModel)?.Settings;

    private void ApplyLayout(MainViewModel vm)
    {
        var s = vm.Settings;
        if (!s.RememberWindowLayout) return;

        if (s.BuildsPanelWidth > 0)
        {
            BuildsColumn.Width = new GridLength(s.BuildsPanelWidth);
            _lastBuildsWidth = BuildsColumn.Width;
        }
        if (s.BottomPanelHeight > 0)
        {
            LogRow.Height = new GridLength(s.BottomPanelHeight);
            _lastLogHeight = LogRow.Height;
        }
    }

    private void SaveLayout()
    {
        if (DataContext is not MainViewModel vm || !vm.Settings.RememberWindowLayout) return;
        var s = vm.Settings;

        if (WindowState == WindowState.Maximized)
        {
            s.WindowMaximized = true;
            var rb = RestoreBounds;
            if (!rb.IsEmpty)
            {
                s.WindowWidth = rb.Width; s.WindowHeight = rb.Height;
                s.WindowLeft = rb.Left; s.WindowTop = rb.Top;
            }
        }
        else
        {
            s.WindowMaximized = false;
            s.WindowWidth = Width; s.WindowHeight = Height;
            s.WindowLeft = Left; s.WindowTop = Top;
        }

        s.BuildsPanelWidth = BuildsColumn.Width.Value > 0 ? BuildsColumn.Width.Value : _lastBuildsWidth.Value;
        s.BottomPanelHeight = LogRow.Height.Value > 0 ? LogRow.Height.Value : _lastLogHeight.Value;

        vm.SaveSettings();
    }

    private GridLength _lastBuildsWidth = new(400);

    private void ToggleBuildsButton_OnClick(object sender, RoutedEventArgs e)
        => SetBuildsPanelVisible(BuildsPanel.Visibility != Visibility.Visible);

    private void AnalyzeSelected_OnClick(object sender, RoutedEventArgs e)
        => SetBuildsPanelVisible(false);

    private void SetBuildsPanelVisible(bool visible)
    {
        if (visible)
        {
            if (BuildsPanel.Visibility == Visibility.Visible) return;
            BuildsPanel.Visibility = Visibility.Visible;
            BuildsSplitter.Visibility = Visibility.Visible;
            BuildsColumn.MinWidth = 260;
            BuildsColumn.Width = _lastBuildsWidth;
        }
        else
        {
            if (BuildsPanel.Visibility != Visibility.Visible) return;
            _lastBuildsWidth = BuildsColumn.Width.Value > 0 ? BuildsColumn.Width : new GridLength(400);
            BuildsPanel.Visibility = Visibility.Collapsed;
            BuildsSplitter.Visibility = Visibility.Collapsed;
            BuildsColumn.MinWidth = 0;
            BuildsColumn.Width = new GridLength(0);
        }

        // ChevronLeft to collapse toward the left, ChevronRight to reveal it again.
        BuildsHandleGlyph.Text = visible ? "\uE76B" : "\uE76C";
    }

    private GridLength _lastLogHeight = new(360);

    private void SetLogPanelVisible(bool visible)
    {
        if (visible)
        {
            if (BottomPanel.Visibility == Visibility.Visible) return;
            BottomPanel.Visibility = Visibility.Visible;
            LogSplitter.Visibility = Visibility.Visible;
            LogRow.MinHeight = 46;
            LogRow.Height = _lastLogHeight;
        }
        else
        {
            if (BottomPanel.Visibility != Visibility.Visible) return;
            _lastLogHeight = LogRow.Height.Value > 0 ? LogRow.Height : new GridLength(360);
            BottomPanel.Visibility = Visibility.Collapsed;
            LogSplitter.Visibility = Visibility.Collapsed;
            LogRow.MinHeight = 0;
            LogRow.Height = new GridLength(0);
        }
    }

    private void BottomTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from inner Selectors (e.g. the reports list);
        // only react to the tab strip's own selection change.
        if (!ReferenceEquals(e.OriginalSource, BottomTabs)) return;
        if (DataContext is not MainViewModel vm) return;

        switch (BottomTabs.SelectedIndex)
        {
            case 2: vm.ApprovedReports.Refresh(); break;
            case 3: vm.SuspiciousReports.Refresh(); break;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && Settings?.MinimizeToTray == true)
            Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveLayout();
        if (_reallyExit) return;
        if (Settings?.CloseToTray == true)
        {
            e.Cancel = true;
            Hide();
            if (Settings.ShowTrayNotifications)
                Tray?.ShowBalloonTip("WPRulesReviewer", "Still running in the tray.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void Tray_OnDoubleClick(object sender, RoutedEventArgs e) => RestoreFromTray();
    private void TrayShow_OnClick(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void TrayExit_OnClick(object sender, RoutedEventArgs e)
    {
        _reallyExit = true;
        Application.Current.Shutdown();
    }
}
