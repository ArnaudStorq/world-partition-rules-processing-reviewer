using System.Windows.Controls;
using System.Windows.Input;
using WPRulesReviewer.App.ViewModels;

namespace WPRulesReviewer.App.Views;

public partial class ReportsPanel : UserControl
{
    public ReportsPanel()
    {
        InitializeComponent();
    }

    private void ReportItem_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        if (vm.EditCommand.CanExecute(null))
            vm.EditCommand.Execute(null);
    }
}
