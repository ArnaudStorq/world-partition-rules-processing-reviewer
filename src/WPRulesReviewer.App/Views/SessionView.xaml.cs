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

        ApplySmartAnalysisVisible(_main.ShowSmartAnalysis);
    }

    private void SmartPanel_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Persist the Smart panel width as the user drags the splitter (saved on app exit).
        if (_vm is not null && SmartColumn.Width.IsAbsolute && SmartColumn.ActualWidth > 0)
            _vm.Settings.SmartPanelWidth = SmartColumn.ActualWidth;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_main is not null) _main.PropertyChanged -= OnMainPropertyChanged;
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowSmartAnalysis) && _main is not null)
            ApplySmartAnalysisVisible(_main.ShowSmartAnalysis);
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
        if (_vm is not null) _vm.RecordRevealRequested -= OnRecordRevealRequested;
        _vm = e.NewValue as SessionViewModel;
        if (_vm is not null) _vm.RecordRevealRequested += OnRecordRevealRequested;
    }

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
