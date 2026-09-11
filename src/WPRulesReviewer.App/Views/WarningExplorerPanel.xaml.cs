using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WPRulesReviewer.App.ViewModels;

namespace WPRulesReviewer.App.Views;

public partial class WarningExplorerPanel : UserControl
{
    public WarningExplorerPanel()
    {
        InitializeComponent();
    }

    private void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is WarningExplorerViewModel vm)
            vm.SelectedNode = e.NewValue as WarningNodeViewModel;
    }

    /// <summary>
    /// A right click does not select a TreeViewItem by default, which would make the context menu
    /// act on whatever was selected before. Select the item under the cursor first.
    /// </summary>
    private void Tree_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var item = FindAncestor<TreeViewItem>(source);
        if (item is null) return;

        item.IsSelected = true;
        item.Focus();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
