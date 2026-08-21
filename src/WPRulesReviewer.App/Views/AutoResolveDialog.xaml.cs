using System.Windows;
using System.Windows.Controls;
using WPRulesReviewer.App.ViewModels;
using WPRulesReviewer.Core.Persistence;

namespace WPRulesReviewer.App.Views;

public partial class AutoResolveDialog : Window
{
    private void StreamBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) box.ScrollToEnd();
    }

    public AutoResolveDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoadedRestoreBounds;
        Closing += OnClosingSaveBounds;
        Closed += (_, _) =>
        {
            if (DataContext is AutoResolveViewModel vm) vm.RequestClose -= Close;
        };
    }

    private void OnLoadedRestoreBounds(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AutoResolveViewModel vm) return;
        var s = vm.Settings;

        if (s.AutoResolveWindowWidth > 300) Width = s.AutoResolveWindowWidth;
        if (s.AutoResolveWindowHeight > 300) Height = s.AutoResolveWindowHeight;

        if (!double.IsNaN(s.AutoResolveWindowLeft) && !double.IsNaN(s.AutoResolveWindowTop))
        {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth;
            double vb = vt + SystemParameters.VirtualScreenHeight;
            bool onScreen = s.AutoResolveWindowLeft >= vl - 50 && s.AutoResolveWindowTop >= vt - 10 &&
                            s.AutoResolveWindowLeft <= vr - 100 && s.AutoResolveWindowTop <= vb - 60;
            if (onScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.AutoResolveWindowLeft;
                Top = s.AutoResolveWindowTop;
            }
        }

        if (s.AutoResolveWindowMaximized) WindowState = WindowState.Maximized;
    }

    private void OnClosingSaveBounds(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not AutoResolveViewModel vm) return;
        var s = vm.Settings;

        if (WindowState == WindowState.Maximized)
        {
            s.AutoResolveWindowMaximized = true;
            var rb = RestoreBounds;
            if (!rb.IsEmpty)
            {
                s.AutoResolveWindowWidth = rb.Width; s.AutoResolveWindowHeight = rb.Height;
                s.AutoResolveWindowLeft = rb.Left; s.AutoResolveWindowTop = rb.Top;
            }
        }
        else
        {
            s.AutoResolveWindowMaximized = false;
            s.AutoResolveWindowWidth = Width; s.AutoResolveWindowHeight = Height;
            s.AutoResolveWindowLeft = Left; s.AutoResolveWindowTop = Top;
        }

        try { new SettingsService().Save(s); } catch { /* ignore persistence errors */ }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AutoResolveViewModel oldVm)
            oldVm.RequestClose -= Close;
        if (e.NewValue is AutoResolveViewModel newVm)
            newVm.RequestClose += Close;
    }
}
