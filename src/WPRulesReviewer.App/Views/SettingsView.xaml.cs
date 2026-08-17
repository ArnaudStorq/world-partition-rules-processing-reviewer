using System.Windows;
using System.Windows.Controls;
using WPRulesReviewer.App.ViewModels;

namespace WPRulesReviewer.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            TokenBox.Password = vm.Model.TeamCityToken;
            PwdBox.Password = vm.Model.TeamCityPassword;
        }
    }

    private void TokenBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.Model.TeamCityToken = TokenBox.Password;
    }

    private void PwdBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.Model.TeamCityPassword = PwdBox.Password;
    }
}
