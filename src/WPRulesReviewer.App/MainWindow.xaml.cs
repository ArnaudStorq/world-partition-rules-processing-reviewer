using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WPRulesReviewer.App.ViewModels;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChangedInternal;
    }

    // ---- Custom title bar (WindowChrome) -----------------------------------
    private void TitleBar_Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void TitleBar_MaximizeRestore(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void TitleBar_Close(object sender, RoutedEventArgs e) => Close();

    private void OnStateChangedInternal(object? sender, System.EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;

        // WindowChrome lets the maximized client area overflow the work area by the resize border;
        // compensate with matching padding so nothing is clipped and the taskbar stays visible.
        RootGrid.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);

        // Swap the glyph and tooltip between Maximize and Restore.
        MaxRestoreButton.Content = max ? "\uE923" : "\uE922";
        MaxRestoreButton.ToolTip = max ? "Restore" : "Maximize";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        ApplyLayout(vm);
        SetLogPanelVisible(vm.ShowActivityLog);

        ApplyBackdrop(vm);

        // Independent of the backdrop setting: the rounded frame is what the rest of Windows 11 does.
        Services.WindowBackdrop.TryRoundCorners(this);

        if (vm.Settings.AutoRefreshOnStartup && vm.RefreshBuildsCommand.CanExecute(null))
            await vm.RefreshBuildsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Mica only shows through a transparent window background, so the two go together: if the
    /// platform refuses the backdrop, the solid window brush is kept.
    /// </summary>
    private void ApplyBackdrop(MainViewModel vm)
    {
        if (!vm.Settings.UseMicaBackdrop) return;

        var dark = vm.Settings.Theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => Services.ThemeManager.IsSystemDark()
        };

        if (Services.WindowBackdrop.TryApplyMica(this, dark))
            SetResourceReference(BackgroundProperty, "Brush.WindowBackdrop");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowActivityLog) && DataContext is MainViewModel vm)
            SetLogPanelVisible(vm.ShowActivityLog);
    }

    private void ApplyLayout(MainViewModel vm)
    {
        var s = vm.Settings;
        if (!s.RememberWindowLayout) return;

        // Window bounds are restored pre-Show in App.OnStartup; here we only restore panel sizes.
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveLayout();
    }
}
