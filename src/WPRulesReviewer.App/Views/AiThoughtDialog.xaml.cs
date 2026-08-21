using System.Collections.Generic;
using System.Windows;
using WPRulesReviewer.App.ViewModels;
using WPRulesReviewer.Core.Ai;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.App.Views;

public partial class AiThoughtDialog : Window
{
    public AiThoughtDialog(RuleRecord record, IReadOnlyList<AutoResolveKnownReport> reports)
    {
        InitializeComponent();
        DataContext = new AiThoughtViewModel(record, reports);
    }
}
