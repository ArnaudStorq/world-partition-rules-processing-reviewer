using System.Windows;
using System.Windows.Controls;
using WPRulesReviewer.App.ViewModels;

namespace WPRulesReviewer.App.Views;

public partial class BreakdownPanel : UserControl
{
    public BreakdownPanel()
    {
        InitializeComponent();
        Radial.NodeActivated += OnNodeActivated;
        Radial.NodeDrillDown += OnNodeDrillDown;
    }

    private WarningExplorerViewModel? Vm => DataContext as WarningExplorerViewModel;

    /// <summary>A single click selects the node, which scopes the grid through the explorer.</summary>
    private void OnNodeActivated(WarningNodeViewModel node)
    {
        if (Vm is null) return;
        Vm.SelectedNode = node;
    }

    /// <summary>A double click focuses the chart on that subtree.</summary>
    private void OnNodeDrillDown(WarningNodeViewModel node)
    {
        Radial.Root = node;
        Radial.CenterTitle = node.Title;
        FocusLabel.Text = node.Title;
        BackButton.Visibility = Visibility.Visible;
    }

    private void Back_OnClick(object sender, RoutedEventArgs e)
    {
        Radial.Root = null;
        Radial.CenterTitle = string.Empty;
        BackButton.Visibility = Visibility.Collapsed;
    }
}
