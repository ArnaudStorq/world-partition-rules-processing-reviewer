using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WPRulesReviewer.App.ViewModels;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.Views;

public partial class SessionView : UserControl
{
    private SessionViewModel? _vm;
    private MainViewModel? _main;
    private GridLength _lastSmartWidth = new(560);
    private GridLength _lastExplorerWidth = new(320);
    private bool _explorerVisible = true;

    public SessionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel mvm) return;
        if (!ReferenceEquals(_main, mvm))
        {
            if (_main is not null) _main.PropertyChanged -= OnMainPropertyChanged;
            _main = mvm;
            _main.PropertyChanged += OnMainPropertyChanged;
        }

        // Restore the persisted Smart panel width.
        var width = _vm?.Settings.SmartPanelWidth ?? 0;
        if (width > 0)
        {
            _lastSmartWidth = new GridLength(width);
            if (_main.ShowSmartAnalysis) SmartColumn.Width = _lastSmartWidth;
        }

        var explorerWidth = _vm?.Settings.ExplorerPanelWidth ?? 0;
        if (explorerWidth > 0)
        {
            _lastExplorerWidth = new GridLength(explorerWidth);
            if (_explorerVisible) ExplorerColumn.Width = _lastExplorerWidth;
        }

        ApplyPanelsForTab();
    }

    private void SmartPanel_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Persist the Smart panel width as the user drags the splitter (saved on app exit).
        if (_vm is not null && SmartColumn.Width.IsAbsolute && SmartColumn.ActualWidth > 0)
            _vm.Settings.SmartPanelWidth = SmartColumn.ActualWidth;
    }

    private void ExplorerPanel_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_vm is not null && ExplorerColumn.Width.IsAbsolute && ExplorerColumn.ActualWidth > 0)
            _vm.Settings.ExplorerPanelWidth = ExplorerColumn.ActualWidth;
    }

    private void ExplorerHandle_OnClick(object sender, RoutedEventArgs e)
    {
        _explorerVisible = !_explorerVisible;
        ApplyExplorerVisible(_explorerVisible);
    }

    /// <summary>
    /// Both side panels are about triaging errors and warnings, so they only exist on that tab. The
    /// user's own show/hide choice is kept in <see cref="_explorerVisible"/> and restored on return.
    /// </summary>
    private void ApplyPanelsForTab()
    {
        // The Explorer browses errors and warnings; Smart Analysis also has something to say about
        // the applied assignments, and nothing at all about the skipped ones.
        var onWarnings = _vm?.IsWarningsTab ?? false;
        var hasSmart = _vm?.HasSmartAnalysis ?? false;

        ExplorerHandle.Visibility = onWarnings ? Visibility.Visible : Visibility.Collapsed;
        SmartHandle.Visibility = hasSmart ? Visibility.Visible : Visibility.Collapsed;
        HandleBandLeft.Width = new GridLength(onWarnings ? 30 : 0);
        HandleBandRight.Width = new GridLength(hasSmart ? 30 : 0);

        ApplyExplorerVisible(onWarnings && _explorerVisible);
        ApplySmartAnalysisVisible(hasSmart && (_main?.ShowSmartAnalysis ?? false));
    }

    private void ApplyExplorerVisible(bool visible)
    {
        if (visible)
        {
            ExplorerPanel.Visibility = Visibility.Visible;
            ExplorerSplitter.Visibility = Visibility.Visible;
            ExplorerColumn.MinWidth = 220;
            ExplorerColumn.Width = _lastExplorerWidth;
            ExplorerHandleGlyph.Text = "\uE76B";   // ChevronLeft: click to hide
        }
        else
        {
            _lastExplorerWidth = ExplorerColumn.Width.Value > 0 ? ExplorerColumn.Width : new GridLength(320);
            ExplorerPanel.Visibility = Visibility.Collapsed;
            ExplorerSplitter.Visibility = Visibility.Collapsed;
            ExplorerColumn.MinWidth = 0;
            ExplorerColumn.Width = new GridLength(0);
            ExplorerHandleGlyph.Text = "\uE76C";   // ChevronRight: click to show
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_main is not null) _main.PropertyChanged -= OnMainPropertyChanged;
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowSmartAnalysis)) ApplyPanelsForTab();
    }

    private void ApplySmartAnalysisVisible(bool visible)
    {
        if (visible)
        {
            if (SmartPanel.Visibility == Visibility.Visible) return;
            SmartPanel.Visibility = Visibility.Visible;
            SmartSplitter.Visibility = Visibility.Visible;
            SmartColumn.MinWidth = 240;
            SmartColumn.Width = _lastSmartWidth;
        }
        else
        {
            if (SmartPanel.Visibility != Visibility.Visible) return;
            _lastSmartWidth = SmartColumn.Width.Value > 0 ? SmartColumn.Width : new GridLength(560);
            SmartPanel.Visibility = Visibility.Collapsed;
            SmartSplitter.Visibility = Visibility.Collapsed;
            SmartColumn.MinWidth = 0;
            SmartColumn.Width = new GridLength(0);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.RecordRevealRequested -= OnRecordRevealRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = e.NewValue as SessionViewModel;
        if (_vm is not null)
        {
            _vm.RecordRevealRequested += OnRecordRevealRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            UpdateAiCheckColumnVisibility();
            ApplyPanelsForTab();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.AiReviewApplied)) UpdateAiCheckColumnVisibility();
        if (e.PropertyName == nameof(SessionViewModel.HasSmartAnalysis)) ApplyPanelsForTab();
    }

    // The "AI check" column only appears once Auto-resolve reading has marked rows as read.
    private void UpdateAiCheckColumnVisibility()
        => AiCheckColumn.Visibility = _vm?.AiReviewApplied == true ? Visibility.Visible : Visibility.Collapsed;

    private void RecordsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm?.SetSelectedRecords(RecordsGrid.SelectedItems.OfType<RuleRecord>());
    }

    private void OnRecordRevealRequested(RuleRecord record)
    {
        // Defer so the grid finishes rebuilding the current page before we scroll/select.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RecordsGrid.SelectedItem = record;
            RecordsGrid.ScrollIntoView(record);
            RecordsGrid.Focus();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
}
